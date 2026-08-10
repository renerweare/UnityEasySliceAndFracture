using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 受约束三角剖分器（ConstrainedTriangulator）：在 Triangulator（Delaunay 逐点插入）基础上，
/// 支持边约束（EdgeConstraint）。可以剖分凸/凹多边形以及带孔的多边形。
/// 原理：先做一次普通 Delaunay 三角剖分，然后对每条约束边：
/// 1) 找出所有与约束边相交的三角形边（FindIntersectingEdges）；
/// 2) 对这些相交边逐一交换所在四边形的对角线，直到没有任何三角形边跨越约束边
///    （RemoveIntersectingEdges）；
/// 3) 最后只保留约束边界内部（或边界上）的三角形，丢弃外侧的三角形
///    （DiscardTrianglesViolatingConstraints）。
/// 用于在切割面上填充三角面片，同时保证切割面的轮廓（约束边）完整。
/// </summary>
public sealed class ConstrainedTriangulator : Triangulator
{
    /// <summary>
    /// 给定一条边（E12/E23/E31），返回该边第一个顶点的索引（分别为 V1/V2/V3）。
    /// 例如 edgeVertex1[E12]=V1、edgeVertex1[E23]=V2、edgeVertex1[E31]=V3；
    /// 前三个元素（0~2）无意义，仅占位
    /// </summary>
    /// <value></value>
    private static readonly int[] edgeVertex1 = new int[] { 0, 0, 0, V1, V2, V3 };

    /// <summary>
    /// 给定一条边（E12/E23/E31），返回该边第二个顶点的索引（分别为 V2/V3/V1）
    /// </summary>
    /// <value></value>
    private static readonly int[] edgeVertex2 = new int[] { 0, 0, 0, V2, V3, V1 };

    /// <summary>
    /// 给定一条边（E12/E23/E31），返回该边对面顶点的索引（分别为 V3/V1/V2）
    /// </summary>
    /// <value></value>
    private static readonly int[] oppositePoint = new int[] { 0, 0, 0, V3, V1, V2 };

    /// <summary>
    /// 给定一条边（E12/E23/E31），返回按顺时针方向的下一条边（分别为 E23/E31/E12）
    /// </summary>
    /// <value></value>
    private static readonly int[] nextEdge = new int[] { 0, 0, 0, E23, E31, E12 };

    /// <summary>
    /// 给定一条边（E12/E23/E31），返回按顺时针方向的上一条边（分别为 E31/E12/E23）
    /// </summary>
    /// <value></value>
    private static readonly int[] previousEdge = new int[] { 0, 0, 0, E31, E12, E23 };

    /// <summary>
    /// 初始化时提供的边约束列表（切割面轮廓边）
    /// </summary>
    private List<EdgeConstraint> constraints;

    /// <summary>
    /// 顶点到三角形的映射数组：记录每个顶点所属的一个三角形，
    /// 用于在查找与约束边相交的边时快速定位起始三角形。
    /// 无需记录每个顶点的所有三角形，一个就足够
    /// </summary>
    private int[] vertexTriangles;

    /// <summary>
    /// 每个三角形的"已访问"标记，用于查找起始边时避免重复遍历。
    /// 定义为类字段是为了在多次调用 FindStartingEdge 时避免重复分配导致的 GC
    /// </summary>
    private bool[] visited;

    /// <summary>
    /// 用待剖分的顶点数据与边约束初始化受约束三角剖分器
    /// </summary>
    /// <param name="inputPoints">待剖分的顶点列表</param>
    /// <param name="constraints">边约束列表，定义了 inputPoints 中顶点之间的连接关系</param>
    /// <param name="normal">inputPoints 所在剖分平面的法线</param>
    /// <returns></returns>
    public ConstrainedTriangulator(List<MeshVertex> inputPoints, List<EdgeConstraint> constraints, Vector3 normal)
        : base(inputPoints, normal)
    {
        this.constraints = constraints;
    }

    /// <summary>
    /// 计算受约束的三角剖分
    /// </summary>
    /// <returns>返回三角形顶点索引数组（每 3 个一组），索引对应初始化时传入的点列表中的点</returns>
    public override int[] Triangulate()
    {
        // 至少需要 3 个顶点才能剖分
        if (N < 3)
        {
            return new int[] { };
        }

        this.AddSuperTriangle();
        this.NormalizeCoordinates();
        this.ComputeTriangulation();

        // 若存在边约束，先强制把约束边"打入"三角剖分，
        // 再丢弃跨越约束边或位于约束区域之外的三角形
        if (constraints.Count > 0)
        {
            this.ApplyConstraints();
            this.DiscardTrianglesViolatingConstraints();
        }

        this.DiscardTrianglesWithSuperTriangleVertices();

        List<int> triangles = new List<int>(3 * triangleCount);
        for (int i = 0; i < triangleCount; i++)
        {
            // 只收集未被标记跳过的三角形
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
    /// 把边约束应用到三角剖分中：
    /// 对每条约束边，找出所有与它相交的三角形边，然后通过交换这些边所在四边形的
    /// 对角线把它们逐条消除，使约束边成为三角剖分中的一条真实边
    /// </summary>
    internal void ApplyConstraints()
    {
        visited = new bool[triangulation.GetLength(0)];

        // 为每个顶点记录一个包含它的三角形，用于快速定位搜索起点
        vertexTriangles = new int[N + 3];
        for (int i = 0; i < triangulation.GetLength(0); i++)
        {
            vertexTriangles[triangulation[i, V1]] = i;
            vertexTriangles[triangulation[i, V2]] = i;
            vertexTriangles[triangulation[i, V3]] = i;
        }

        // 逐个处理每条边约束
        foreach (EdgeConstraint constraint in constraints)
        {
            // 两个端点相同（长度为 0 的边）没有意义，跳过
            if (constraint.v1 == constraint.v2) continue;

            // 找出三角剖分中与约束边相交的所有边并移除它们：
            // 对每条相交边，找到共享该边的两个三角形（构成一个四边形），
            // 然后翻转这个四边形的对角线
            Queue<EdgeConstraint> intersectingEdges = FindIntersectingEdges(constraint, vertexTriangles);
            RemoveIntersectingEdges(constraint, intersectingEdges);
        }
    }

    /// <summary>
    /// 在三角剖分中搜索所有与约束边相交的三角形边
    /// </summary>
    /// <param name="constraint">约束边（起点 v_i，终点 v_j）</param>
    /// <param name="vertexTriangles">顶点到三角形的映射数组</param>
    /// <returns>返回与约束边相交的三角形边队列（每条边同时记录了它两侧的三角形）</returns>
    internal Queue<EdgeConstraint> FindIntersectingEdges(EdgeConstraint constraint, int[] vertexTriangles)
    {
        Queue<EdgeConstraint> intersectingEdges = new Queue<EdgeConstraint>();

        // 先找到约束边跨越的第一条三角形边作为搜索起点
        EdgeConstraint startEdge;
        if (FindStartingEdge(vertexTriangles, constraint, out startEdge))
        {
            intersectingEdges.Enqueue(startEdge);
        }
        else
        {
            // 没有找到起点（例如约束边已经存在于三角剖分中），直接返回空
            return intersectingEdges;
        }

        // 从起始边出发，逐个三角形向前推进，找出所有与约束边相交的边。
        // 当到达包含约束边终点 v_j 的三角形时停止
        int t = startEdge.t1;
        int edgeIndex = startEdge.t1Edge;
        int lastTriangle = t;
        bool finalTriangleFound = false;
        while (!finalTriangleFound)
        {
            // 跨越最后一条相交边，进入下一个三角形
            lastTriangle = t;
            t = triangulation[t, edgeIndex];

            // 取约束边两端点与当前三角形三个顶点的坐标
            Vector2 v_i = points[constraint.v1].coords;
            Vector2 v_j = points[constraint.v2].coords;
            Vector2 v1 = points[triangulation[t, V1]].coords;
            Vector2 v2 = points[triangulation[t, V2]].coords;
            Vector2 v3 = points[triangulation[t, V3]].coords;

            // 若当前三角形包含约束边的终点 v_j，搜索结束
            if (TriangleContainsVertex(t, constraint.v2))
            {
                finalTriangleFound = true;
            }
            // 否则约束边必然穿过本三角形的一条边（忽略我们进入该三角形时穿过的那条边）
            else if ((triangulation[t, E12] != lastTriangle) && MathUtils.LinesIntersect(v_i, v_j, v1, v2))
            {
                edgeIndex = E12;
                var edge = new EdgeConstraint(triangulation[t, V1], triangulation[t, V2], t, triangulation[t, E12], edgeIndex);
                intersectingEdges.Enqueue(edge);
            }
            else if ((triangulation[t, E23] != lastTriangle) && MathUtils.LinesIntersect(v_i, v_j, v2, v3))
            {
                edgeIndex = E23;
                var edge = new EdgeConstraint(triangulation[t, V2], triangulation[t, V3], t, triangulation[t, E23], edgeIndex);
                intersectingEdges.Enqueue(edge);
            }
            else if ((triangulation[t, E31] != lastTriangle) && MathUtils.LinesIntersect(v_i, v_j, v3, v1))
            {
                edgeIndex = E31;
                var edge = new EdgeConstraint(triangulation[t, V3], triangulation[t, V1], t, triangulation[t, E31], edgeIndex);
                intersectingEdges.Enqueue(edge);
            }
            else
            {
                // 理论上不应走到这一步：说明约束边与当前三角形没有边相交，
                // 提前退出以防死循环
                Debug.LogWarning("未能找到最终的三角形，提前退出。");
                break;
            }
        }

        return intersectingEdges;
    }

    /// <summary>
    /// 为"查找与约束边相交的所有边"的搜索找到起始边：
    /// 从包含约束边起点 v_i 的三角形出发，绕 v_i 顺时针搜索，
    /// 找到第一条与约束边相交的三角形边
    /// </summary>
    /// <param name="vertexTriangles">顶点到三角形的映射数组</param>
    /// <param name="constraint">约束边</param>
    /// <param name="startingEdge">通过 out 参数返回起始边（含两侧三角形与边索引）</param>
    /// <returns>若找到起始边返回 true；若约束边已存在于三角剖分中或没有候选边，返回 false</returns>
    internal bool FindStartingEdge(int[] vertexTriangles, EdgeConstraint constraint, out EdgeConstraint startingEdge)
    {
        // 初始化 out 参数为默认值
        startingEdge = new EdgeConstraint(-1, -1);

        // v_i->v_j 分别是约束边的起点与终点
        int v_i = constraint.v1;
        int v_j = constraint.v2;

        // 用一个包含 v_i 的三角形作为搜索起点
        int tSearch = vertexTriangles[v_i];

        // 重置所有三角形的访问标记
        for (int i = 0; i < visited.Length; i++)
        {
            visited[i] = false;
        }

        // 围绕 v_i 逐个检查相邻三角形，直到找到一条与约束边相交的边。
        // 这条边就是"找出所有相交边"搜索的起点
        bool intersectionFound = false;
        bool noCandidatesFound = false;
        int intersectingEdgeIndex = E12;
        int tE12, tE23, tE31;
        while (!intersectionFound && !noCandidatesFound)
        {
            visited[tSearch] = true;

            // 若当前三角形已经包含约束边（两端点都在该三角形里），
            // 说明约束边已经存在于三角剖分中，无需处理，返回 false
            if (TriangleContainsConstraint(tSearch, constraint))
            {
                return false;
            }
            // 检查约束边是否与当前三角形的某条边相交
            else if (EdgeConstraintIntersectsTriangle(tSearch, constraint, out intersectingEdgeIndex))
            {
                intersectionFound = true;
                break;
            }

            tE12 = triangulation[tSearch, E12];
            tE23 = triangulation[tSearch, E23];
            tE31 = triangulation[tSearch, E31];

            // 若当前三角形没有被相交，则跨越"以 v_i 为端点的边"进入相邻三角形继续检查。
            // 跳过越界边（边界边）与已经访问过的三角形
            if (tE12 != OUT_OF_BOUNDS && !visited[tE12] && TriangleContainsVertex(tE12, v_i))
            {
                tSearch = tE12;
            }
            else if (tE23 != OUT_OF_BOUNDS && !visited[tE23] && TriangleContainsVertex(tE23, v_i))
            {
                tSearch = tE23;
            }
            else if (tE31 != OUT_OF_BOUNDS && !visited[tE31] && TriangleContainsVertex(tE31, v_i))
            {
                tSearch = tE31;
            }
            else
            {
                // 围绕 v_i 的所有相邻三角形都检查完了，仍没有找到相交边
                noCandidatesFound = true;
                break;
            }
        }
        
        if (intersectionFound)
        {
            // 记录相交边的两个端点、它两侧的三角形以及在该三角形中的边索引
            int v_k = triangulation[tSearch, edgeVertex1[intersectingEdgeIndex]];
            int v_l = triangulation[tSearch, edgeVertex2[intersectingEdgeIndex]];
            int triangle2 = triangulation[tSearch, intersectingEdgeIndex];
            startingEdge = new EdgeConstraint(v_k, v_l, tSearch, triangle2, intersectingEdgeIndex);

            return true;
        }
        else
        {
            return false;
        }
    }

    /// <summary>
    /// 从三角剖分中移除与约束边相交的边：对每条相交边，找到共享它的两个三角形
    /// （构成一个四边形），交换四边形的对角线，重复这个过程直到没有边再与约束边相交
    /// </summary>
    /// <param name="constraint">要处理的约束边</param>
    /// <param name="intersectingEdges">先前找到的、与约束边相交的边队列</param>
    internal void RemoveIntersectingEdges(EdgeConstraint constraint, Queue<EdgeConstraint> intersectingEdges)
    {
        // 移除相交边，同时记录过程中新产生的边
        List<EdgeConstraint> newEdges = new List<EdgeConstraint>();
        EdgeConstraint edge, newEdge;

        // 记录循环执行的次数：如果所有边都处理过但没有任何新边产生，就停止循环。
        // 每当有新边加入 newEdges，就把计数器清零
        int counter = 0;

        // 循环处理所有相交边，直到它们全部被解决，
        // 或者所有边都被访问过但没有发生任何对角线交换（无法继续处理）
        while (intersectingEdges.Count > 0 && counter <= intersectingEdges.Count)
        {
            edge = intersectingEdges.Dequeue();

            Quad quad;
            if (FindQuadFromSharedEdge(edge.t1, edge.t1Edge, out quad))
            {
                // 若四边形是凸的（判断标准：四边形的两条对角线相交），
                // 就交换对角线；否则先把它放回队列，等后续再尝试
                if (MathUtils.LinesIntersect(points[quad.q4].coords,
                        points[quad.q3].coords,
                        points[quad.q1].coords,
                        points[quad.q2].coords))
                {
                    // 交换对角线相交且与约束边相交的凸四边形
                    SwapQuadDiagonal(quad, intersectingEdges, newEdges, constraints);

                    // 交换后新的对角线位于 Q3 与 Q4 之间
                    newEdge = new EdgeConstraint(quad.q3, quad.q4, quad.t1, quad.t2, E31);

                    // 若新对角线仍然与约束边 v_i->v_j 相交，
                    // 把它放回相交边队列继续处理
                    if (MathUtils.LinesIntersect(points[constraint.v1].coords,
                            points[constraint.v2].coords,
                            points[quad.q3].coords,
                            points[quad.q4].coords))
                    {
                        intersectingEdges.Enqueue(newEdge);
                    }
                    // 否则记录为新产生的边
                    else
                    {
                        counter = 0;
                        newEdges.Add(newEdge);
                    }
                }
                else
                {
                    // 四边形不是凸的，先放回队列，等前面的边处理完后再试
                    intersectingEdges.Enqueue(edge);
                }
            }

            counter++;
        }

        // 若因为交换对角线产生了新的边，则在尊重约束的前提下恢复 Delaunay 条件
        if (newEdges.Count > 0)
        {
            RestoreConstrainedDelauneyTriangulation(constraint, newEdges);
        }
    }

    /// <summary>
    /// 约束边插入后，恢复 Delaunay 三角剖分性质：
    /// 迭代处理新产生的边，对不满足 Delaunay 条件且不是约束边的对角线进行交换
    /// </summary>
    /// <param name="constraint">刚插入三角剖分的约束边</param>
    /// <param name="newEdges">交换过程中新产生的边列表</param>
    internal void RestoreConstrainedDelauneyTriangulation(EdgeConstraint constraint, List<EdgeConstraint> newEdges)
    {
        // 遍历新产生的边，持续交换不满足条件的非约束对角线，直到没有交换发生
        bool swapOccurred = true;
        int counter = 0;
        while (swapOccurred)
        {
            counter++;
            swapOccurred = false;

            for (int i = 0; i < newEdges.Count; i++)
            {
                EdgeConstraint edge = newEdges[i];

                // 如果新产生的边恰好就是约束边本身，不能翻转它，跳过
                if (edge == constraint)
                {
                    continue;
                }

                Quad quad;
                if (FindQuadFromSharedEdge(edge.t1, edge.t1Edge, out quad))
                {
                    // Delaunay 测试：点 q4 是否在三角形 (q1,q2,q3) 的外接圆内
                    if (SwapTest(points[quad.q1].coords, points[quad.q2].coords, points[quad.q3].coords, points[quad.q4].coords))
                    {
                        // 交换对角线，并把新的对角线放回列表继续检查
                        SwapQuadDiagonal(quad, newEdges, constraints, null);

                        // 新对角线位于 v_m 与 v_n 之间
                        int v_m = quad.q3;
                        int v_n = quad.q4;
                        newEdges[i] = new EdgeConstraint(v_m, v_n, quad.t1, quad.t2, E31);

                        swapOccurred = true;
                    }
                }
            }
        }
    }

    /// <summary>
    /// 丢弃违反边约束的三角形：
    /// 约束边构成封闭的边界（切割面轮廓），用"洪水填充"的方式，
    /// 从含有约束边的三角形出发，沿非约束边向外扩散，只保留能到达的三角形
    /// </summary>
    internal void DiscardTrianglesViolatingConstraints()
    {
        // 初始化：默认所有三角形都标记为跳过
        for (int i = 0; i < triangleCount; i++)
        {
            skipTriangle[i] = true;
        }

        // 收集所有约束边（无方向），作为"边界"集合
        HashSet < (int, int) > boundaries = new HashSet < (int, int) > ();
        for (int i = 0; i < this.constraints.Count; i++)
        {
            EdgeConstraint constraint = this.constraints[i];
            boundaries.Add((constraint.v1, constraint.v2));
        }

        // 重置所有三角形的访问标记
        for (int i = 0; i < visited.Length; i++)
        {
            visited[i] = false;
        }

        // 搜索队列（洪水填充的边界）
        Queue<int> frontier = new Queue<int>();

        int v1, v2, v3;
        bool boundaryE12, boundaryE23, boundaryE31;
        for (int i = 0; i < triangleCount; i++)
        {
            // 已访问过的三角形跳过
            if (visited[i])
            {
                continue;
            }

            v1 = triangulation[i, V1];
            v2 = triangulation[i, V2];
            v3 = triangulation[i, V3];
            boundaryE12 = boundaries.Contains((v1, v2));
            boundaryE23 = boundaries.Contains((v2, v3));
            boundaryE31 = boundaries.Contains((v3, v1));

            // 若该三角形含有一条约束边界边，则它是边界上的三角形，
            // 从这里开始向其内部扩散搜索
            if (boundaryE12 || boundaryE23 || boundaryE31)
            {
                skipTriangle[i] = false;

                // 沿非约束边方向入队相邻三角形
                frontier.Clear();
                if (!boundaryE12)
                {
                    frontier.Enqueue(triangulation[i, E12]);
                }
                if (!boundaryE23)
                {
                    frontier.Enqueue(triangulation[i, E23]);
                }
                if (!boundaryE31)
                {
                    frontier.Enqueue(triangulation[i, E31]);
                }

                // 递归地沿所有非边界边扩散，把能到达的相邻三角形标记为"保留"
                while (frontier.Count > 0)
                {
                    int k = frontier.Dequeue();

                    if (k == OUT_OF_BOUNDS || visited[k])
                    {
                        continue;
                    }

                    skipTriangle[k] = false;
                    visited[k] = true;

                    v1 = triangulation[k, V1];
                    v2 = triangulation[k, V2];
                    v3 = triangulation[k, V3];

                    // 继续沿非边界边扩散
                    if (!boundaries.Contains((v1, v2)))
                    {
                        frontier.Enqueue(triangulation[k, E12]);
                    }
                    if (!boundaries.Contains((v2, v3)))
                    {
                        frontier.Enqueue(triangulation[k, E23]);
                    }
                    if (!boundaries.Contains((v3, v1)))
                    {
                        frontier.Enqueue(triangulation[k, E31]);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 判断三角形是否包含边约束的两个端点（即约束边是否已经存在于该三角形中）
    /// </summary>
    /// <param name="t">要测试的三角形</param>
    /// <param name="constraint">边约束</param>
    /// <returns>若三角形同时包含约束边的两个端点，返回 true</returns>
    internal bool TriangleContainsConstraint(int t, EdgeConstraint constraint)
    {
        return (triangulation[t, V1] == constraint.v1 || triangulation[t, V2] == constraint.v1 || triangulation[t, V3] == constraint.v1) &&
               (triangulation[t, V1] == constraint.v2 || triangulation[t, V2] == constraint.v2 || triangulation[t, V3] == constraint.v2);
    }

    /// <summary>
    /// 判断边约束是否与三角形 t 的某条边相交
    /// </summary>
    /// <param name="t">要测试的三角形</param>
    /// <param name="constraint">边约束</param>
    /// <param name="intersectingEdgeIndex">通过 out 参数返回相交边的索引（E12/E23/E31）</param>
    /// <returns>若找到相交边返回 true，否则返回 false</returns>
    internal bool EdgeConstraintIntersectsTriangle(int t, EdgeConstraint constraint, out int intersectingEdgeIndex)
    {
        Vector2 v_i = points[constraint.v1].coords;
        Vector2 v_j = points[constraint.v2].coords;
        Vector2 v1 = points[triangulation[t, V1]].coords;
        Vector2 v2 = points[triangulation[t, V2]].coords;
        Vector2 v3 = points[triangulation[t, V3]].coords;

        if (MathUtils.LinesIntersect(v_i, v_j, v1, v2))
        {
            intersectingEdgeIndex = E12;
            return true;
        }
        else if (MathUtils.LinesIntersect(v_i, v_j, v2, v3))
        {
            intersectingEdgeIndex = E23;
            return true;
        }
        else if (MathUtils.LinesIntersect(v_i, v_j, v3, v1))
        {
            intersectingEdgeIndex = E31;
            return true;
        }
        else
        {
            intersectingEdgeIndex = -1;
            return false;
        }
    }

    /// <summary>
    /// 返回由三角形 t1 与共享"相交边"的另一三角形组成的四边形
    /// </summary>
    /// <param name="t1">基准三角形</param>
    /// <param name="t1SharedEdge">被相交的边索引（在 t1 中的）</param>
    /// <param name="quad">通过 out 参数返回四边形数据</param>
    /// <returns>若成功找到四边形返回 true，否则返回 false</returns>
    internal bool FindQuadFromSharedEdge(int t1, int t1SharedEdge, out Quad quad)
    {
        //               q3        
        //      *---------*---------*
        //       \       / \       /
        //        \ t2L /   \ t2R /
        //         \   /     \   /
        //          \ /   t2  \ /
        //        q1 *---------* q2 
        //          / \   t1  / \    
        //         /   \     /   \     
        //        / t1L \   / t1R \   
        //       /       \ /       \  
        //      *---------*---------*
        //               q4             
        // 示意图：t1 与 t2 共享边 q1-q2；q4 是 t1 中共享边对面的顶点，
        // q3 是 t2 中共享边对面的顶点；t1L/t1R、t2L/t2R 分别为与 t1/t2 相邻的三角形。

        int q1, q2, q3, q4;
        int t1L, t1R, t2L, t2R;

        // t2 是沿 t1Edge 与 t1 相邻的三角形
        int t2 = triangulation[t1, t1SharedEdge];
        int t2SharedEdge;
        if (FindSharedEdge(t2, t1, out t2SharedEdge))
        {
            // 从 t2 中取出四边形的上面三个顶点（q1、q2、q3），
            // 顺序随 t2 与 t1 共享的边不同而不同
            if (t2SharedEdge == E12)
            {
                q2 = triangulation[t2, V1];
                q1 = triangulation[t2, V2];
                q3 = triangulation[t2, V3];
            }
            else if (t2SharedEdge == E23)
            {
                q2 = triangulation[t2, V2];
                q1 = triangulation[t2, V3];
                q3 = triangulation[t2, V1];
            }
            else // (t2SharedEdge == E31)
            {
                q2 = triangulation[t2, V3];
                q1 = triangulation[t2, V1];
                q3 = triangulation[t2, V2];
            }

            // q4 是 t1 中与共享边相对的顶点（oppositePoint 表把边索引映射到对顶点索引）
            q4 = triangulation[t1, oppositePoint[t1SharedEdge]];

            // 取出相邻三角形索引，便于交换对角线时更新邻接关系
            t1L = triangulation[t1, previousEdge[t1SharedEdge]];
            t1R = triangulation[t1, nextEdge[t1SharedEdge]];
            t2L = triangulation[t2, nextEdge[t2SharedEdge]];
            t2R = triangulation[t2, previousEdge[t2SharedEdge]];

            quad = new Quad(q1, q2, q3, q4, t1, t2, t1L, t1R, t2L, t2R);

            return true;
        }

        quad = new Quad();

        return false;
    }

    /// <summary>
    /// 交换四边形（由 t1 与 t2 构成）的对角线，把对角线从 q1-q2 翻转为 q4-q3。
    /// 同时更新相邻三角形的邻接指针，以及传入的边列表（edges1/edges2/edges3，
    /// 通常为相交边队列、新边列表与约束列表）中记录的三角形归属
    /// </summary>
    /// <param name="quad">要交换对角线的四边形（见方法内 BEFORE/AFTER 示意图）</param>
    internal void SwapQuadDiagonal(Quad quad, IEnumerable<EdgeConstraint> edges1, IEnumerable<EdgeConstraint> edges2, IEnumerable<EdgeConstraint> edges3)
    {
        // BEFORE
        //               q3        
        //      *---------*---------*
        //       \       / \       /
        //        \ t2L /   \ t2R /
        //         \   /     \   /
        //          \ /   t2  \ /
        //        q1 *---------* q2 
        //          / \   t1  / \    
        //         /   \     /   \     
        //        / t1L \   / t1R \   
        //       /       \ /       \  
        //      *---------*---------*
        //               q4           

        // AFTER
        //               q3        
        //      *---------*---------*
        //       \       /|\       /
        //        \ t2L / | \ t2R /
        //         \   /  |  \   /
        //          \ /   |   \ /
        //        q1 * t1 | t2 * q2 
        //          / \   |   / \    
        //         /   \  |  /   \     
        //        / t1L \ | / t1R \   
        //       /       \|/       \  
        //      *---------*---------*
        //               q4      
        // 交换前 t1（q4,q1,q2）、t2（q2,q3,q1）共享对角线 q1-q2；
        // 交换后 t1（q4,q1,q3）、t2（q4,q3,q2）共享对角线 q4-q3。

        int t1 = quad.t1;
        int t2 = quad.t2;
        int t1R = quad.t1R;
        int t1L = quad.t1L;
        int t2R = quad.t2R;
        int t2L = quad.t2L;

        // 执行对角线交换。依旧约定新顶点（q4）放在每个三角形的 V1 位置
        triangulation[t1, V1] = quad.q4;
        triangulation[t1, V2] = quad.q1;
        triangulation[t1, V3] = quad.q3;

        triangulation[t2, V1] = quad.q4;
        triangulation[t2, V2] = quad.q3;
        triangulation[t2, V3] = quad.q2;

        triangulation[t1, E12] = t1L;
        triangulation[t1, E23] = t2L;
        triangulation[t1, E31] = t2;

        triangulation[t2, E12] = t1;
        triangulation[t2, E23] = t2R;
        triangulation[t2, E31] = t1R;

        // 更新相邻三角形的邻接指针
        UpdateAdjacency(t2L, t2, t1);
        UpdateAdjacency(t1R, t1, t2);

        // 三角形的位置变了，边约束记录的两侧三角形信息也需要同步更新
        UpdateEdgesAfterSwap(edges1, t1, t2, t1L, t1R, t2L, t2R);
        UpdateEdgesAfterSwap(edges2, t1, t2, t1L, t1R, t2L, t2R);
        UpdateEdgesAfterSwap(edges3, t1, t2, t1L, t1R, t2L, t2R);

        // 还需要更新 vertexTriangles 数组：顶点 q1、q2 原本可能分别指向 t2、t1，
        // 交换后它们已不再属于这两个三角形
        vertexTriangles[quad.q1] = t1;
        vertexTriangles[quad.q2] = t2;
    }

    /// <summary>
    /// 对角线交换后，同步更新边列表（edges）中各条边记录的三角形归属，
    /// 因为三角形 t1/t2 的构成发生了改变
    /// </summary>
    /// <param name="edges">需要更新的边约束列表（可为 null）</param>
    /// <param name="t1">交换后的第一个三角形</param>
    /// <param name="t2">交换后的第二个三角形</param>
    /// <param name="t1L">与 t1 左边相邻的三角形</param>
    /// <param name="t1R">与 t1 右边相邻的三角形</param>
    /// <param name="t2L">与 t2 左边相邻的三角形</param>
    /// <param name="t2R">与 t2 右边相邻的三角形</param>
    internal void UpdateEdgesAfterSwap(IEnumerable<EdgeConstraint> edges, int t1, int t2, int t1L, int t1R, int t2L, int t2R)
    {
        if (edges == null)
        {
            return;
        }

        // 更新各条边，使其反映三角形的变化
        foreach (EdgeConstraint edge in edges)
        {
            if (edge.t1 == t1 && edge.t2 == t1R)
            {
                // t1 与 t1R 的邻接关系因交换而变为 t2 与 t1R，边在 t1 侧变为 E31
                edge.t1 = t2;
                edge.t2 = t1R;
                edge.t1Edge = E31;
            }
            else if (edge.t1 == t1 && edge.t2 == t1L)
            {
                // 两侧三角形保持不变，只需更新边索引为 E12
                edge.t1Edge = E12;
            }
            else if (edge.t1 == t1R && edge.t2 == t1)
            {
                // t1 一侧被 t2 取代
                edge.t2 = t2;
            }
            else if (edge.t1 == t1L && edge.t2 == t1)
            {
                // 保持不变
            }
            else if (edge.t1 == t2 && edge.t2 == t2R)
            {
                // 两侧三角形保持不变，只需更新边索引为 E23
                edge.t1Edge = E23;
            }
            else if (edge.t1 == t2 && edge.t2 == t2L)
            {
                // t2 一侧被 t1 取代，并更新边索引为 E23
                edge.t1 = t1;
                edge.t2 = t2L;
                edge.t1Edge = E23;
            }
            else if (edge.t1 == t2R && edge.t2 == t2)
            {
                // 保持不变
            }
            else if (edge.t1 == t2L && edge.t2 == t2)
            {
                // t2 一侧被 t1 取代
                edge.t2 = t1;
            }
        }
    }
}