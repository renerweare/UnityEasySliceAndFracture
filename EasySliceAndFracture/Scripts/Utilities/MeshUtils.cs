using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using UnityEngine.Rendering;

/// <summary>
/// 网格工具静态类。
/// 提供网格相关的通用处理功能。目前的核心能力是
/// FindDisconnectedMeshes：检测一个网格中包含的所有互不相连的几何体集合
/// （称为"岛屿" island），并将每个岛屿拆分重建为一个独立的 Mesh。
/// 该功能在破碎/切割系统中用于在碎片生成后，把不再连通的几何体拆分成独立网格。
/// </summary>
public static class MeshUtils
{
    // 岛屿（island）网格使用的顶点属性描述（顶点缓冲区布局）。
    // 仅保留三个通道，分别是：
    //   - Position（位置）：Float32，3 个分量（xyz 坐标）
    //   - Normal（法线）：Float32，3 个分量（xyz 分量）
    //   - TexCoord0（第一套 UV）：Float32，2 个分量（uv 坐标）
    // 该描述与源网格的顶点数据（vertices / normals / uv）一一对应，
    // 用于在重建新网格时通过 SetVertexBufferParams 声明顶点布局。
    private static VertexAttributeDescriptor[] layout = new[]
    {
        new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
        new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
        new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
    };

    /// <summary>
    /// 识别网格中所有互不相连的几何体集合（连通域），并将每一组
    /// 几何体拆分（重建）为一个独立的 Mesh，最终返回所有拆分后的 Mesh 数组。
    /// </summary>
    /// <remarks>
    /// 整体流程（重要概念——"岛屿" island）：
    /// 网格中如果存在两组三角形，它们之间没有任何共享顶点、也没有任何
    /// 位置重合（coincident）的顶点，则这两组三角形就是"互不相连"的，
    /// 各自构成一个独立的岛屿。本方法通过广度优先搜索（BFS）在
    /// 「顶点—三角形」的邻接关系图上找出全部岛屿：
    ///
    /// 预备阶段（#region 预备阶段）：
    ///   1. 提取网格的顶点、三角形、法线、UV 数据；
    ///   2. 为每个三角形计算其所属的子网格（sub-mesh）下标；
    ///   3. 找出所有位置重合（坐标相等）的顶点对（coincident vertices），
    ///      它们虽然索引不同但几何上位于同一位置，也应被视为"相连"；
    ///   4. 为每个顶点记录其参与的所有三角形（vertexTriangles）。
    ///
    /// 搜索与重建阶段：
    ///   1. 从未访问过的顶点出发，以 BFS 方式沿「顶点→三角形→顶点」扩散，
    ///      收集整个连通域内的顶点与三角形；
    ///   2. 将收集到的数据重建为一个新的 Mesh（岛屿），并保留原网格的子网格结构；
    ///   3. 重复上述过程，直到所有顶点都被访问过。
    /// </remarks>
    /// <param name="mesh">要搜索的源网格</param>
    /// <returns>由所有互不相连的子网格（岛屿）构成的 Mesh 数组</returns>
    public static Mesh[] FindDisconnectedMeshes(Mesh mesh)
    {
        // 每个互不相连的几何体集合被称为一个"岛屿"（island），
        // 全部岛屿最终以 Mesh 数组的形式返回。
        List<Mesh> islands = new List<Mesh>();

        #region 预备阶段（Preliminaries）：提取并预处理源网格数据

        // 提取源网格的基础几何数据：
        //   vertices  —— 全部顶点（位置）
        //   triangles —— 全部三角形的顶点索引（每 3 个一组构成一个三角形，
        //                且所有子网格的三角形都平铺在这个数组里）
        //   normals   —— 每个顶点的法线
        //   uvs       —— 每个顶点的第一套 UV 坐标
        var vertices = mesh.vertices;
        var triangles = mesh.triangles;
        var normals = mesh.normals;
        var uvs = mesh.uv;

        // 为每个三角形计算其所属子网格（sub-mesh）的下标。
        // 说明：Mesh.triangles 是把所有子网格的三角形顺序平铺在一个数组中的，
        // 因此需要按子网格三角形的数量依次切分，才能知道每个三角形属于哪个子网格。
        int[] triangleSubMesh = new int[triangles.Length / 3]; // 每个三角形对应一个子网格下标
        int subMeshIndex = 0;                                  // 当前正在遍历的子网格下标
        // 第 0 个子网格的三角形数量（取整后）
        int subMeshSize = mesh.GetTriangles(subMeshIndex).Length / 3;
        // 遍历全部三角形（i 为三角形序号，对应 triangles 数组中下标 [i*3, i*3+3) 的三个索引）
        for (int i = 0; i < triangles.Length / 3; i++)
        {
            // 如果当前三角形序号已经超出当前子网格的范围，说明已经走到下一个子网格了：
            // 子网格下标 +1，并把新的子网格三角形数量累加进边界
            if (i >= subMeshSize)
            {
                subMeshIndex++;
                subMeshSize += mesh.GetTriangles(subMeshIndex).Length / 3;
            }
            // 记录第 i 个三角形所属的子网格下标
            triangleSubMesh[i] = subMeshIndex;
        }

        // 找出所有"重合顶点"（coincident vertices）：即坐标完全相等但索引不同的顶点。
        // 为什么要找它们？在 Unity 网格中，同一个几何位置经常有多个顶点副本
        // （例如不同法线的接缝处、被多次引用的顶点等）。从"几何连通性"角度看，
        // 这些位置相同的顶点应当被视为互相连接的，因此后面做连通域搜索时必须
        // 把重合顶点之间的关系也考虑进去。
        // coincidentVertices[i] 记录了与第 i 个顶点坐标重合的所有其它顶点下标。
        List<int>[] coincidentVertices = new List<int>[vertices.Length];
        for(int i = 0; i < vertices.Length; i++)
        {
            coincidentVertices[i] = new List<int>();
        }
        // 两层循环两两比较（只比较 i < k 的组合，避免重复），
        // 若位置相等则把双方互相登记到对方的重合列表中。
        for(int i = 0; i < vertices.Length; i++)
        {
            Vector3 v_i = vertices[i];
            for (int k = i + 1; k < vertices.Length; k++)
            {
                Vector3 v_k = vertices[k];
                if (v_i == v_k)
                {
                    coincidentVertices[k].Add(i);   // 顶点 k 与顶点 i 重合
                    coincidentVertices[i].Add(k);   // 顶点 i 与顶点 k 重合
                }
            }
        }

        // 找出每个顶点参与的所有三角形（按三角形下标记录）。
        // vertexTriangles[i] 记录了第 i 个顶点作为三角形顶点出现的所有三角形下标。
        // 这是后面 BFS 的核心邻接表：由顶点可以快速查到它所属的全部三角形。
        List<int>[] vertexTriangles = new List<int>[vertices.Length];

        // 为每个顶点初始化一个空的三角形下标列表
        for (int i = 0; i < vertices.Length; i++)
        {
            vertexTriangles[i] = new List<int>();
        }

        // 遍历全部三角形索引（每次步进 3，即遍历一个三角形的 3 个顶点索引）
        int v1, v2, v3;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            // 当前三角形在 triangles 数组中的三角形序号（= i / 3）
            int t = i / 3;

            v1 = triangles[i];       // 三角形第 1 个顶点索引
            v2 = triangles[i + 1];   // 三角形第 2 个顶点索引
            v3 = triangles[i + 2];   // 三角形第 3 个顶点索引

            // 把该三角形登记到它的三个顶点各自的"所属三角形列表"中
            vertexTriangles[v1].Add(t);
            vertexTriangles[v2].Add(t);
            vertexTriangles[v3].Add(t);
        }

        #endregion 预备阶段结束

        // ======== 连通域搜索（广度优先搜索 BFS）========
        // 整体算法步骤：
        // 1) 找到一个尚未被访问过的顶点，作为新岛屿的种子；
        // 2) 把该顶点放入队列，开始一次广度优先搜索；
        // 3) 从队列中取出一个顶点 v；
        // 4) 找出 v 参与的所有三角形，把它们收集到当前岛屿的三角形列表中；
        // 5) 对每个相连三角形的其它顶点，若尚未访问过则放入队列；
        // 6) 对与 v 重合（coincident）的所有顶点，若尚未访问过也放入队列；
        // 7) 重复步骤 3~6 直到队列为空——此时已收集完一个完整的连通域（岛屿）；
        // 8) 用收集到的三角形和源网格的顶点数据重建出一个新的岛屿 Mesh；
        // 9) 回到步骤 1，继续处理剩余顶点，直到所有顶点都被访问过。

        // visitedVertices[i]：源网格第 i 个顶点是否已被某个岛屿"吸收"
        bool[] visitedVertices = new bool[vertices.Length];
        // visitedTriangles[t]：第 t 个三角形是否已被归入某个岛屿
        // 注意：数组长度取 triangles.Length（索引下标即三角形序号 t，t < triangles.Length / 3）
        bool[] visitedTriangles = new bool[triangles.Length];
        // frontier：BFS 的待处理队列
        Queue<int> frontier = new Queue<int>();

        // 岛屿网格的顶点数据缓冲区。只在循环外初始化一次（长度 = 源网格顶点数），
        // 内部用 vertexCount 记录当前写入位置，这样所有岛屿共用一个缓冲区，
        // 可以减少垃圾回收（GC）压力（避免每个岛屿都 new 一个大数组）。
        NativeArray<MeshVertex> islandVertices = new NativeArray<MeshVertex>(vertices.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

        // 岛屿网格的三角形索引数据。由于要保留源网格的子网格结构，
        // 需要为每个子网格单独维护一份索引数组。
        // 每个子网格的数组长度都取 triangles.Length（最大值，实际使用量由计数控制），
        // 同样是"一次分配、循环复用"以减小 GC 压力的做法。
        int[][] islandTriangles = new int[mesh.subMeshCount][];
        for (int i = 0; i < mesh.subMeshCount; i++)
        {
            islandTriangles[i] = new int[triangles.Length];
        }

        // 用于记录当前岛屿（连通域）规模的计数器：
        //   vertexCount          —— 当前岛屿已收集的顶点数量
        //   totalIndexCount      —— 当前岛屿所有子网格的索引总数
        //   subMeshIndexCounts[j]—— 当前岛屿第 j 个子网格已收集的索引数量
        int vertexCount = 0;
        int totalIndexCount = 0;
        int[] subMeshIndexCounts = new int[mesh.subMeshCount];

        // 外层循环：遍历所有顶点，以每个"尚未访问"的顶点为起点，
        // 开始一次新的 BFS，从而找到一个新的岛屿。
        for (int i = 0; i < vertices.Length; i++)
        {
            // 顶点已被之前的岛屿访问过，跳过
            if (visitedVertices[i]) continue;

            // 开始新岛屿前，重置顶点/三角形计数器
            vertexCount = 0;
            totalIndexCount = 0;
            for(int j = 0; j < mesh.subMeshCount; j++)
            {
                subMeshIndexCounts[j] = 0;
            }

            // 以顶点 i 为起点开始 BFS 搜索整个连通域。
            // 搜索方式：不断查找顶点所属的三角形，再把这些三角形涉及的新顶点
            // 加入队列，如此往复，直到该连通域内的所有三角形都被访问过。
            frontier.Enqueue(i);

            // vertexMap：源网格顶点索引 → 岛屿网格顶点索引 的映射表。
            // 重建岛屿时，三角形索引必须从"源网格顶点下标"改写为"岛屿网格顶点下标"，
            // 这张表就是用来做这个转换的。
            int[] vertexMap = new int[vertices.Length];
            // 把所有映射初始化为 -1，作为"尚未映射"的标记值
            // （因为合法的顶点下标 >= 0，-1 不会与任何真实下标冲突）。
            for(int j = 0; j < vertices.Length; j++)
            {
                vertexMap[j] = -1;
            }

            while (frontier.Count > 0)
            {
                // 从队列头部取出一个待处理顶点 k
                int k = frontier.Dequeue();

                // 若该顶点已被访问过（可能通过其它路径先到达），直接跳过
                if (visitedVertices[k])
                {
                    continue;
                }
                else
                {
                    // 标记为已访问，确保每个顶点只会被处理一次
                    visitedVertices[k] = true;   
                }

                // 把顶点 k 加入岛屿网格的顶点数组：
                // 1) 在 vertexMap 中登记映射：源网格下标 k → 岛屿网格新下标 vertexCount；
                //    （这个映射在后面会把三角形索引改写为岛屿网格的下标）
                // 2) 把顶点 k 的位置/法线/UV 打包成 MeshVertex 写入顶点缓冲区，
                //    然后 vertexCount 自增（指向下一个可用位置）。
                vertexMap[k] = vertexCount;
                islandVertices[vertexCount++] = new MeshVertex(vertices[k], normals[k], uvs[k]);

                // 遍历顶点 k 参与的所有三角形（利用预备阶段建好的邻接表）
                foreach(int t in vertexTriangles[k])
                {
                    // 若该三角形已归入当前岛屿（或之前的岛屿），跳过
                    if (!visitedTriangles[t])
                    {
                        // 标记三角形为已访问，把它归入当前岛屿
                        visitedTriangles[t] = true;

                        // 遍历该三角形的 3 个顶点索引
                        // （三角形 t 在 triangles 数组中占据下标 [t*3, t*3+3)）
                        for (int m = t * 3; m < t * 3 + 3; m++)
                        {
                            int v = triangles[m];   // 源网格中的顶点下标
                            // 确定该三角形属于哪个子网格，把顶点索引 v 追加到
                            // 对应子网格的岛屿索引数组中（下标由 subMeshIndexCounts 计数）
                            subMeshIndex = triangleSubMesh[t];
                            islandTriangles[subMeshIndex][subMeshIndexCounts[subMeshIndex]++] = v;
                            totalIndexCount++;      // 累计总索引数

                            // 把该顶点加入 BFS 队列，继续向外扩散搜索
                            frontier.Enqueue(v);
                                
                            // 若顶点 v 还有其它位置重合（coincident）的顶点，
                            // 它们几何上属于同一个位置，也要一起加入队列继续搜索，
                            // 从而保证"仅靠重合顶点相连"的几何体也被并入同一个岛屿。
                            foreach(int cv in coincidentVertices[v]) 
                            { 
                                frontier.Enqueue(cv);
                            }
                        }
                    }
                }
            }

            // 如果当前岛屿至少包含一个顶点（说明确有几何体），就重建一个新的 Mesh
            if (vertexCount > 0)
            {
                Mesh island = new Mesh();

                // 1) 设置索引缓冲区：总索引数 totalIndexCount，索引格式用 UInt32
                //    （32 位无符号整数，可容纳超过 65535 个顶点的大网格）
                island.SetIndexBufferParams(totalIndexCount, IndexFormat.UInt32);
                // 2) 设置顶点缓冲区：顶点数 vertexCount，布局使用本类顶部定义的 layout
                //    （仅含 Position / Normal / TexCoord0 三个通道）
                island.SetVertexBufferParams(vertexCount, layout);
                // 3) 把 BFS 期间写入 islandVertices 的顶点数据拷入新网格顶点缓冲区
                //    （参数含义：数据源、源起始下标 0、目标起始下标 0、拷贝数量 vertexCount）
                island.SetVertexBufferData(islandVertices, 0, 0, vertexCount);

                // 为每个子网格设置三角形（索引）数据
                island.subMeshCount = mesh.subMeshCount;
                // indexStart：当前子网格的索引在总索引缓冲区中的起始偏移
                int indexStart = 0;
                // 遍历源网格的每一个子网格
                for (subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
                {
                    // 取出该子网格的索引数组及其有效元素个数
                    var subMeshIndexBuffer = islandTriangles[subMeshIndex];
                    var subMeshIndexCount = subMeshIndexCounts[subMeshIndex];

                    // 关键一步：把三角形索引从"源网格顶点下标"改写为"岛屿网格顶点下标"。
                    // BFS 收集时索引数组里存的是源网格的下标（因为直接取自 triangles 数组），
                    // 而岛屿网格的顶点是重新排列过的，必须借助 vertexMap 完成一一映射。
                    for(int k = 0; k < subMeshIndexCount; k++)
                    {
                        int originalIndex = subMeshIndexBuffer[k];   // 源网格顶点下标
                        subMeshIndexBuffer[k] = vertexMap[originalIndex]; // 改写为岛屿网格顶点下标
                    }
                    
                    // 把改写后的索引数据写入索引缓冲区（从 indexStart 偏移处开始），
                    // 并用 SubMeshDescriptor 声明该子网格在缓冲区中的范围
                    island.SetIndexBufferData(subMeshIndexBuffer, 0, indexStart, (int)subMeshIndexCount);
                    island.SetSubMesh(subMeshIndex, new SubMeshDescriptor(indexStart, subMeshIndexCount));
                    
                    // 下一个子网格的起始偏移向后移动
                    indexStart += subMeshIndexCount;
                }

                // 重新计算包围盒（AABB），保证新网格的 bounds 是正确的
                island.RecalculateBounds();
 
                // 把重建好的岛屿网格加入结果列表
                islands.Add(island);
            }
        }

        // 遍历完所有顶点后，返回全部岛屿网格。
        // （当只有一个连通域时，返回数组中只会包含一个与原网格等价的 Mesh）
        return islands.ToArray();
    }
}
