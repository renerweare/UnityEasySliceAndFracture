using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 三角剖分器（Triangulator）：对一组三维点做平面三角剖分。仅支持凸多边形。
/// 核心算法为"逐点插入 + 超三角形"的 Delaunay 三角剖分：
/// 1) 先把三维点投影到切割平面，得到二维坐标；
/// 2) 添加一个覆盖所有点的"超三角形"（super triangle）；
/// 3) 逐个把点插入到包含它的三角形中，并不断交换四边形对角线以恢复 Delaunay 性质；
/// 4) 最后丢弃所有含有超三角形顶点的三角形，剩余三角形即为结果。
/// 该基类负责无约束的三角剖分；ConstrainedTriangulator 在此基础上加入边约束支持。
/// </summary>
public class Triangulator
{
    // 三角剖分数组（triangulation）的列索引常量定义。
    // 每行表示一个三角形，共 6 列：前 3 列为三角形三个顶点的索引，后 3 列为三条边各自的相邻三角形索引。
    protected const int V1 = 0; // 顶点 1（第 1 个顶点）
    protected const int V2 = 1; // 顶点 2（第 2 个顶点）
    protected const int V3 = 2; // 顶点 3（第 3 个顶点）
    protected const int E12 = 3; // 边 (V1 -> V2) 的相邻三角形索引
    protected const int E23 = 4; // 边 (V2 -> V3) 的相邻三角形索引
    protected const int E31 = 5; // 边 (V3 -> V1) 的相邻三角形索引

    // 超三角形所在的行索引（triangulation 的第 0 行）
    protected const int SUPERTRIANGLE = 0;

    // 越界三角形索引：表示某条边是边界边（没有相邻三角形）
    protected const int OUT_OF_BOUNDS = -1;

    // 需要三角剖分的点的数量（不含超三角形的三个顶点）
    protected int N;

    // 三角剖分过程中生成（预分配）的三角形总数
    protected int triangleCount;

    // 三角形顶点与邻接数据（二维数组）。
    // 行索引 = 三角形编号；列索引见上方 V1/V2/V3/E12/E23/E31 常量，即每行结构为 [V1, V2, V3, E12, E23, E31]
    protected int[, ] triangulation;

    // 位于剖分平面上的点集（下标 0~N-1 为输入点，N~N+2 为超三角形顶点）
    public TriangulationPoint[] points;

    // 标记数组：记录哪些三角形应在最终结果中被丢弃（例如包含超三角形顶点的三角形）
    protected bool[] skipTriangle;

    // 点集所在剖分平面的法线（用于三维点投影到二维时构建坐标系）
    protected Vector3 normal;

    // 坐标归一化缩放系数：把二维坐标统一缩放到 [0,1] 区间时使用的除数
    public float normalizationScaleFactor = 1f;

    /// <summary>
    /// 用待剖分的顶点数据初始化三角剖分器
    /// </summary>
    /// <param name="inputPoints">待剖分的顶点列表（需要至少 3 个顶点）</param>
    /// <param name="normal">剖分平面的法线</param>
    public Triangulator(List<MeshVertex> inputPoints, Vector3 normal)
    {
        // 至少需要三个输入顶点才能进行三角剖分
        if (inputPoints == null || inputPoints.Count < 3)
        {
            return;
        }

        this.N = inputPoints.Count;
        this.triangleCount = 2 * N + 1;
        this.triangulation = new int[triangleCount, 6];
        this.skipTriangle = new bool[triangleCount];
        this.points = new TriangulationPoint[N + 3]; // 额外 3 个点用于存放超三角形顶点
        this.normal = normal;
        
        // 在剖分平面内构造一组正交基（e1, e3），把三维点投影到平面上：
        // 取第 0 个点指向第 1 个点的方向作为 e1，e2 为平面法线，
        // 再用叉积得到与两者都正交的 e3。这样 (e1, e3) 就张成剖分平面上的二维坐标系
        Vector3 e1 = (inputPoints[0].position - inputPoints[1].position).normalized;
        Vector3 e2 = normal.normalized;
        Vector3 e3 = Vector3.Cross(e1, e2).normalized;

        // 构造第二基向量的另一种方法：取出 e1 中绝对值最大的分量，与绝对值最小的分量交换并取反（原算法备注）。
        // 此处直接使用法线叉积得到的 e3 作为第二基向量，效果等价。
        
        // 把每个三维顶点投影到剖分平面，得到二维坐标（分别投影到 e1 与 e3 方向）
        for (int i = 0; i < N; i++)
        {
            var position = inputPoints[i].position;
            var coords = new Vector2(Vector3.Dot(position, e1), Vector3.Dot(position, e3));
            this.points[i] = new TriangulationPoint(i, coords);
        }
    }

    /// <summary>
    /// 执行三角剖分
    /// </summary>
    /// <returns>返回三角形顶点索引数组（每 3 个一组），索引对应初始化时传入的点列表中的点</returns>
    public virtual int[] Triangulate()
    {
        // 至少需要 3 个点才能剖分
        if (N < 3) 
        {
            return new int[] { };
        }

        this.AddSuperTriangle();
        this.NormalizeCoordinates();
        this.ComputeTriangulation();
        this.DiscardTrianglesWithSuperTriangleVertices();

        List<int> triangles = new List<int>(3 * triangleCount);
        for (int i = 0; i < triangleCount; i++)
        {
            // 只收集不含超三角形顶点的三角形
            if (!skipTriangle[i])
            {
                triangles.Add(triangulation[i, V1]);
                triangles.Add(triangulation[i, V2]);
                triangles.Add(triangulation[i, V3]);
            }
        }

        return triangles.ToArray();
    }

    /// <summary>
    /// 把所有点的二维坐标统一缩放到 [0,1] 区间。
    /// 缩放必须保持均匀（x、y 用同一个系数），否则会改变点之间的相对位置关系
    /// </summary>
    protected void NormalizeCoordinates()
    {
        // 归一化坐标：缩放使所有点落在 [0,1] 区间内。
        // 缩放系数对 x、y 相同，保证是均匀缩放，点之间的相对位置不变

        float xMin = float.MaxValue;
        float xMax = float.MinValue;
        float yMin = float.MaxValue;
        float yMax = float.MinValue;

        // 找出点集中 x、y 方向的最小/最大值
        for (int i = 0; i < N; i++)
        {
            var point = points[i];
            if (point.coords.x < xMin) xMin = point.coords.x;
            if (point.coords.y < yMin) yMin = point.coords.y;
            if (point.coords.x > xMax) xMax = point.coords.x;
            if (point.coords.y > yMax) yMax = point.coords.y;
        }

        // 归一化系数：取 x、y 跨度的较大者；x、y 共用同一系数保证缩放均匀。
        // 该值会对外暴露，用于在生成切割面 UV 时把归一化坐标还原回真实尺度
        normalizationScaleFactor = Mathf.Max(xMax - xMin, yMax - yMin);

        // 逐个点归一化：(坐标 - 最小值) / 缩放系数，使坐标落在 [0,1]
        for (int i = 0; i < N; i++)
        {
            var point = points[i];
            var normalizedPos = new Vector2(
                (point.coords.x - xMin) / normalizationScaleFactor,
                (point.coords.y - yMin) / normalizationScaleFactor);

            points[i].coords = normalizedPos;            
        }
    }

    /// <summary>
    /// 使用有序网格（bin）把点分箱排序，
    /// 使空间上相邻的点在数组中靠在一起，从而加速"逐点插入"时对目标三角形的查找
    /// </summary>
    /// <returns>返回排序后的点数组</returns>
    protected TriangulationPoint[] SortPointsIntoBins()
    {
        // 计算每个轴上的分箱数量：n = N 的四分之一次方
        int n = Mathf.RoundToInt(Mathf.Pow((float) N, 0.25f));
        
        // 总箱数 = n * n
        int binCount = n * n;

        // 根据归一化坐标把每个点分配到 n×n 网格中的一个箱子里。
        // 乘 0.99 是为了防止坐标恰好等于 1 时下标越界（下标最大为 n-1）
        for (int k = 0; k < N; k++)
        {
            var point = this.points[k];
            int i = (int) (0.99f * n * point.coords.y);
            int j = (int) (0.99f * n * point.coords.x);
            point.bin = BinSort.GetBinNumber(i, j, n);
        }

        return BinSort.Sort<TriangulationPoint>(this.points, N, binCount);
    }

    /// <summary>
    /// 计算点集的三角剖分（Delaunay 逐点插入法）
    /// </summary>
    /// <returns>三角剖分成功返回 true</returns>
    protected bool ComputeTriangulation()
    {
        // 当前正在搜索的三角形索引
        int tSearch = 0;
        // 最近生成（最后一个）三角形的索引
        int tLast = 0;

        var sortedPoints = SortPointsIntoBins();

        // 依次把每个点插入到三角剖分中
        for (int i = 0; i < N; i++)
        {
            TriangulationPoint point = sortedPoints[i];

            // 插入新点：先从超三角形出发，找到包含该点的三角形。
            // counter 记录访问过的三角形数量，防止查找失败时陷入死循环
            int counter = 0;
            bool pointInserted = false;
            while (!pointInserted)
            {
                if (counter++ > tLast || tSearch == OUT_OF_BOUNDS)
                {
                    break;
                }

                // 取当前搜索三角形的三个顶点坐标
                var v1 = this.points[triangulation[tSearch, V1]].coords;
                var v2 = this.points[triangulation[tSearch, V2]].coords;
                var v3 = this.points[triangulation[tSearch, V3]].coords;
                
                // 判断点是否位于三角形每条边的"右侧"。
                // 如果点在某条边的左侧，就跨过这条边进入相邻三角形继续检查；
                // 不断重复，直到找到包含该点的三角形，或确定点位于所有三角形之外
                // （Unity 使用顺时针（CW）绕序，所以"右侧"即三角形内部一侧）
                if (!MathUtils.IsPointOnRightSideOfLine(v1, v2, point.coords))
                {
                    tSearch = triangulation[tSearch, E12];
                }
                else if (!MathUtils.IsPointOnRightSideOfLine(v2, v3, point.coords))
                {
                    tSearch = triangulation[tSearch, E23];
                }
                else if (!MathUtils.IsPointOnRightSideOfLine(v3, v1, point.coords))
                {
                    tSearch = triangulation[tSearch, E31];
                }
                // 若点在三条边的右侧（三角形内部），则该三角形包含此点，进行插入
                else
                {
                    InsertPointIntoTriangle(point, tSearch, tLast);
                    tLast += 2;
                    tSearch = tLast;
                    pointInserted = true;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// 初始化剖分：在点集外围添加超三角形。
    /// 超三角形足够大，保证覆盖所有输入点，使插入过程中不会出现"点在所有三角形之外"的情况
    /// </summary>
    protected void AddSuperTriangle()
    {
        // 在 points 数组末尾追加 3 个超三角形顶点（固定的大坐标三角形）
        this.points[N] = new TriangulationPoint(N, new Vector2(-100f, -100f));
        this.points[N + 1] = new TriangulationPoint(N + 1, new Vector2(0f, 100f));
        this.points[N + 2] = new TriangulationPoint(N + 2, new Vector2(100f, -100f));

        // 把超三角形存入 triangulation 的第 0 行
        triangulation[SUPERTRIANGLE, V1] = N;
        triangulation[SUPERTRIANGLE, V2] = N + 1;
        triangulation[SUPERTRIANGLE, V3] = N + 2;

        // 超三角形的三条边都是边界边：邻接三角形记为 OUT_OF_BOUNDS
        triangulation[SUPERTRIANGLE, E12] = OUT_OF_BOUNDS;
        triangulation[SUPERTRIANGLE, E23] = OUT_OF_BOUNDS;
        triangulation[SUPERTRIANGLE, E31] = OUT_OF_BOUNDS;
    }

    /// <summary>
    /// 把点 p 插入到三角形 t 中：原来的一个三角形被替换为 3 个新三角形，
    /// 并更新相应的顶点与邻接信息（见下方示意图）
    /// </summary>
    /// <param name="p">要插入的点</param>
    /// <param name="t">包含该点的三角形索引</param>
    /// <param name="triangleCount">当前已生成的三角形总数（用于给新三角形分配索引）</param>
    protected void InsertPointIntoTriangle(TriangulationPoint p, int t, int triangleCount)
    {
        //                         V1
        //                         *
        //                        /|\
        //                       /3|2\
        //                      /  |  \
        //                     /   |   \
        //                    /    |    \
        //                   /     |     \
        //                  /  t1  |  t3  \
        //                 /       |       \
        //                /      1 * 1      \
        //               /      __/1\__      \
        //              /    __/       \__    \
        //             / 2__/     t2      \__3 \
        //            / _/3                 2\_ \
        //           *---------------------------*
        //         V3                             V2
        // 示意图：点 p（图中的 *）插入到三角形 t（V1,V2,V3）内部，
        // 拆分成 t1（p,V3,V1）、t2（p,V1,V2）、t3（p,V2,V3）三个新三角形。
        // 边旁标注的 1/2/3 表示各三角形的边所对应的 E12/E23/E31 邻接信息。

        int t1 = t;
        int t2 = triangleCount + 1;
        int t3 = triangleCount + 2;

        // 填写两个新三角形 t2、t3 的顶点与邻接信息。
        // 约定：新插入的顶点放在每个三角形的 V1 位置，便于后续恢复 Delaunay 性质时统一处理
        triangulation[t2, V1] = p.index;
        triangulation[t2, V2] = triangulation[t, V2];
        triangulation[t2, V3] = triangulation[t, V3];

        triangulation[t2, E12] = t3;
        triangulation[t2, E23] = triangulation[t, E23];
        triangulation[t2, E31] = t1;

        triangulation[t3, V1] = p.index;
        triangulation[t3, V2] = triangulation[t, V1];
        triangulation[t3, V3] = triangulation[t, V2];

        triangulation[t3, E12] = t1;
        triangulation[t3, E23] = triangulation[t, E12];
        triangulation[t3, E31] = t2;

        // 原三角形 t 的 E12 边保持原样（其邻接三角形就是 t 的 E12，索引不变，无需更新）；
        // 只需更新原来 t 的两条边（E12/E23）的相邻三角形，使它们指向新的 t3/t2
        UpdateAdjacency(triangulation[t, E12], t, t3);
        UpdateAdjacency(triangulation[t, E23], t, t2);

        // 把原三角形 t 复用为 t1（p, V3, V1），更新其顶点与邻接信息
        triangulation[t1, V2] = triangulation[t, V3];
        triangulation[t1, V3] = triangulation[t, V1];
        triangulation[t1, V1] = p.index;

        triangulation[t1, E23] = triangulation[t, E31];
        triangulation[t1, E12] = t2;
        triangulation[t1, E31] = t3;

        // 新三角形插入完成后，交换不满足 Delaunay 条件的对角线，恢复 Delaunay 三角剖分
        RestoreDelauneyTriangulation(p, t1, t2, t3);
    }

    /// <summary>
    /// 新三角形加入后，通过交换四边形对角线恢复 Delaunay 三角剖分性质。
    /// 对新点 p 所在的三个新三角形 t1/t2/t3，逐个检查其"对边相邻三角形"：
    /// 若 p 落在该相邻三角形的外接圆内，则交换四边形的对角线，并递归检查新生成的四边形
    /// </summary>
    /// <param name="p">被插入的点</param>
    /// <param name="t1">第一个需要检查的三角形</param>
    /// <param name="t2">第二个需要检查的三角形</param>
    /// <param name="t3">第三个需要检查的三角形</param>
    protected void RestoreDelauneyTriangulation(TriangulationPoint p, int t1, int t2, int t3)
    {
        int t4;
        Stack < (int, int) > s = new Stack < (int, int) > ();

        // 把三个新三角形与其"对边相邻三角形"（E23 边对面的三角形）压入栈。
        // 因为新顶点在 V1 位置，其对边就是 E23
        s.Push((t1, triangulation[t1, E23]));
        s.Push((t2, triangulation[t2, E23]));
        s.Push((t3, triangulation[t3, E23]));
        
        while (s.Count > 0)
        {
            // 弹出下一对待检查的三角形：
            // t1 包含新插入的顶点（在 V1 位置）；
            // t2 是与 t1 沿 V1 对边（E23）相邻的三角形
            (t1, t2) = s.Pop();

            if (t2 == OUT_OF_BOUNDS)
            {
                continue;
            }
            // 如果 t2 的外接圆包含 p，说明 t1+t2 构成的四边形对角线方向画反了，需要交换
            else if (SwapQuadDiagonalIfNeeded(p.index, t1, t2, out t3, out t4))
            {
                // 交换后产生了新的四边形，把它们压入栈，继续检查是否还需要交换
                s.Push((t1, t3));
                s.Push((t2, t4));
            }
        }
    }

    /// <summary>
    /// 判断并执行对角线交换：由三角形 t1 与其对边相邻三角形 t2 构成一个四边形，
    /// 若点 p 落在 t2 的外接圆内，则把四边形对角线从 q1-q2 翻转为 q4-q3（见方法内示意图）
    /// </summary>
    /// <param name="p">被插入点的索引</param>
    /// <param name="t1">包含 p 的三角形索引</param>
    /// <param name="t2">与 t1 共享边 E23（p 的对边）的相邻三角形索引</param>
    /// <param name="t3">交换后与 t1 相邻的三角形索引（out 参数）</param>
    /// <param name="t4">交换后与 t2 相邻的三角形索引（out 参数）</param>
    /// <returns>返回是否执行了交换；若未交换（返回 false），t3、t4 无意义</returns>
    protected bool SwapQuadDiagonalIfNeeded(int p, int t1, int t2, out int t3, out int t4)
    {
        // 1) 由 t1 + t2 构成四边形（q1->q2->q3->q4）
        // 2) 把对角线从 q1->q2 交换为 q4->q3
        //
        //               BEFORE                            AFTER
        //  
        //                 q3                                q3
        //    *-------------*-------------*    *-------------*-------------*
        //     \           / \           /      \           /|\           / 
        //      \   t3    /   \   t4    /        \   t3    /3|2\   t4    /  
        //       \       /     \       /          \       /  |  \       /   
        //        \     /       \     /            \     /   |   \     /    
        //         \   /   t2    \   /              \   /    |    \   /     
        //          \ /           \ /                \ /     |     \ /     
        //        q1 *-------------*  q2           q1 * 2 t1 | t2 3 * q2
        //            \2         3/                    \     |     /        
        //             \         /                      \    |    /         
        //              \  t1   /                        \   |   /          
        //               \     /                          \  |  /          
        //                \   /                            \1|1/            
        //                 \1/                              \|/             
        //                  *  q4 == p                       *  q4 == p   
        // 示意图：交换前 t1（q4,q1,q2）与 t2（q2,q3,q1）共享对角线 q1-q2；
        // 交换后 t1（q4,q1,q3）与 t2（q4,q3,q2）共享对角线 q4-q3。

        // 取四边形的顶点。约定新插入的顶点 p 位于 t1 的 V1 位置
        int q4 = p;
        int q1, q2, q3;

        // t2 的朝向是任意的，需要找出它哪条边与 t1 相邻（即共享边）。
        // 四边形中共享边对面的顶点就是第 4 个顶点（q3）。
        // 同时还要找出与 t2 另外两条边相邻的三角形 t3、t4，
        // 因为交换对角线后需要更新它们的邻接信息
        if (triangulation[t2, E12] == t1)
        {
            q1 = triangulation[t2, V2];
            q2 = triangulation[t2, V1];
            q3 = triangulation[t2, V3];

            t3 = triangulation[t2, E23];
            t4 = triangulation[t2, E31];
        }
        else if (triangulation[t2, E23] == t1)
        {
            q1 = triangulation[t2, V3];
            q2 = triangulation[t2, V2];
            q3 = triangulation[t2, V1];

            t3 = triangulation[t2, E31];
            t4 = triangulation[t2, E12];
        }
        else // (triangulation[t2, E31] == t1)
        {
            q1 = triangulation[t2, V1];
            q2 = triangulation[t2, V3];
            q3 = triangulation[t2, V2];

            t3 = triangulation[t2, E12];
            t4 = triangulation[t2, E23];
        }

        // Delaunay 判定：检查 p 是否位于三角形 t2 的外接圆内
        if (SwapTest(points[q1].coords, points[q2].coords, points[q3].coords, points[q4].coords))
        {
            // 更新与 t1、t2 相邻的三角形的邻接指针
            UpdateAdjacency(t3, t2, t1);
            UpdateAdjacency(triangulation[t1, E31], t1, t2);

            // 执行对角线交换。依旧约定新顶点放在每个三角形的 V1 位置
            triangulation[t1, V1] = q4;
            triangulation[t1, V2] = q1;
            triangulation[t1, V3] = q3;

            triangulation[t2, V1] = q4;
            triangulation[t2, V2] = q3;
            triangulation[t2, V3] = q2;

            // 更新邻接信息（注意操作顺序很重要：这里会覆盖旧数据，必须先取出旧值再覆盖）
            triangulation[t2, E12] = t1;
            triangulation[t2, E23] = t4;
            triangulation[t2, E31] = triangulation[t1, E31];

            // triangulation[t1, E12] = t2; // （原实现中被注释掉的语句，保留原样）
            triangulation[t1, E23] = t3;
            triangulation[t1, E31] = t2;

            return true;
        }
        else
        {
            return false;
        }
    }

    /// <summary>
    /// 把包含超三角形顶点的三角形标记为丢弃（写入 skipTriangle）
    /// </summary>
    protected void DiscardTrianglesWithSuperTriangleVertices()
    {
        for (int i = 0; i < triangleCount; i++)
        {
            // 只要三角形含有任意一个超三角形顶点（N/N+1/N+2），就标记为跳过
            if (TriangleContainsVertex(i, N) || 
                TriangleContainsVertex(i, N + 1) || 
                TriangleContainsVertex(i, N + 2))
            {
                skipTriangle[i] = true;
            }
        }
    }

    /// <summary>
    /// Delaunay 外接圆测试：判断三角形 (v1,v2,v3) 的外接圆是否包含点 v4。
    /// 基于角度（余弦/正弦）判断，避免显式求外接圆圆心，数值更稳定
    /// </summary>
    /// <param name="v1">三角形第 1 个顶点的坐标</param>
    /// <param name="v2">三角形第 2 个顶点的坐标</param>
    /// <param name="v3">三角形第 3 个顶点的坐标</param>
    /// <param name="v4">待测试点的坐标</param>
    /// <returns>若三角形 t 的外接圆包含点 p，返回 true</returns>
    protected bool SwapTest(Vector2 v1, Vector2 v2, Vector2 v3, Vector2 v4)
    {
        float x13 = v1.x - v3.x;
        float x23 = v2.x - v3.x;
        float y13 = v1.y - v3.y;
        float y23 = v2.y - v3.y;
        float x14 = v1.x - v4.x;
        float x24 = v2.x - v4.x;
        float y14 = v1.y - v4.y;
        float y24 = v2.y - v4.y;

        // 计算四边形两个对角顶点处的夹角余弦值（用向量点积表示）
        float cosA = x13 * x23 + y13 * y23;
        float cosB = x24 * x14 + y24 * y14;

        // 若两个对角都是锐角或直角（cos >= 0），v4 一定不在外接圆内，无需交换
        if (cosA >= 0 && cosB >= 0)
        {
            return false;
        }
        // 若两个对角都是钝角（cos < 0），v4 必在外接圆内，需要交换
        else if (cosA < 0 && cosB < 0)
        {
            return true;
        }
        // 一个锐角一个钝角时，需要通过正弦值做进一步精确判定（等价于行列式/叉积判定）
        else
        {
            float sinA = (x13 * y23 - x23 * y13);
            float sinB = (x24 * y14 - x14 * y24);
            float sinAB = sinA * cosB + sinB * cosA;
            return sinAB < 0;
        }
    }

    /// <summary>
    /// 检查三角形 t 是否包含指定顶点 v
    /// </summary>
    /// <param name="t">三角形索引</param>
    /// <param name="v">顶点索引</param>
    /// <returns>若三角形 t 包含顶点 v，返回 true</returns>
    protected bool TriangleContainsVertex(int t, int v)
    {
        return triangulation[t, V1] == v || triangulation[t, V2] == v || triangulation[t, V3] == v;
    }

    /// <summary>
    /// 更新三角形 t 中的邻接信息：把指向 tOld 的邻接引用替换为 tNew
    /// </summary>
    /// <param name="t">需要更新邻接信息的三角形索引</param>
    /// <param name="tOld">要被替换掉的旧三角形索引</param>
    /// <param name="tNew">替换成的新三角形索引</param>
    protected void UpdateAdjacency(int t, int tOld, int tNew)
    {
        // 边界边：不存在相邻三角形，直接返回
        int sharedEdge;
        if (t == OUT_OF_BOUNDS)
        {
            return;
        }
        else if (FindSharedEdge(t, tOld, out sharedEdge))
        {
            triangulation[t, sharedEdge] = tNew;
        }
    }

    /// <summary>
    /// 找出三角形 tOrigin 中指向 tAdjacent 的那条边的索引
    /// </summary>
    /// <param name="tOrigin">被查找的三角形</param>
    /// <param name="tAdjacent">要查找的相邻三角形索引</param>
    /// <param name="edgeIndex">通过 out 参数返回共享边的索引（E12/E23/E31）</param>
    /// <returns>若 tOrigin 与 tAdjacent 相邻，返回 true 并通过 out 参数给出共享边索引；
    /// 若 tOrigin 无效或两者不相邻，返回 false</returns>
    protected bool FindSharedEdge(int tOrigin, int tAdjacent, out int edgeIndex)
    {
        edgeIndex = 0;

        if (tOrigin == OUT_OF_BOUNDS)
        {
            return false;
        }
        else if (triangulation[tOrigin, E12] == tAdjacent)
        {
            edgeIndex = E12;
            return true;
        }
        else if (triangulation[tOrigin, E23] == tAdjacent)
        {
            edgeIndex = E23;
            return true;
        }
        else if (triangulation[tOrigin, E31] == tAdjacent)
        {
            edgeIndex = E31;
            return true;
        }
        else
        {
            return false;
        }
    }
}