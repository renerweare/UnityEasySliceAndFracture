using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 切片网格的子网格枚举：
/// Default —— 原始网格表面（外部表面）的三角形；
/// CutFace —— 上一次操作（当前源网格自带）的切割面三角形，如第二次切分时旧切割面的迁移；
/// NewCutFace —— 本次切分新生成的切割面三角形。
/// 把旧切割面与新切割面分开存放，可以给它们分配不同的材质/UV，便于二次切分时正确迁移
/// 以及最终渲染时新旧切割面使用不同的纹理。
/// </summary>
public enum SlicedMeshSubmesh
{
    Default = 0,
    CutFace = 1,
    NewCutFace = 2
}

/// <summary>
/// 切分过程中的网格数据结构（FragmentData）：
/// 在把网格切成两半的过程中，暂时把顶点与三角形存放在这里，
/// 最后通过 ToMesh() 重建为 Unity 的 Mesh 对象。
/// 顶点分为两类：
/// - Vertices：网格原有的顶点（与源网格一一对应，通过 IndexMap 映射）；
/// - CutVertices：切割平面上新生成的顶点（由跨切割面的边插值得到），
///   同时也会被加入 Vertices（两者共享几何位置，但 CutVertices 之后会被
///   赋予切割面的法线/UV，用于填充切割面）。
/// 三角形按子网格分组存放（Triangles 数组），并记录切割面轮廓的边约束
/// （Constraints），供切割面三角剖分使用。
/// </summary>
public class FragmentData
{
    /// <summary>
    /// 非切割面顶点缓冲区：存放网格原有表面顶点
    /// （以及新切割顶点的一份拷贝，用于最终网格的顶点流）
    /// </summary>
    public List<MeshVertex> Vertices;

    /// <summary>
    /// 切割面顶点缓冲区：只存放本次切分在切割平面上新生成的顶点，
    /// 用于切割面的三角剖分；最终重建网格时排在 Vertices 之后
    /// </summary>
    public List<MeshVertex> CutVertices;

    /// <summary>
    /// 索引缓冲区：每个子网格一个 List（参见 SlicedMeshSubmesh 枚举）
    /// </summary>
    public List<int>[] Triangles;

    /// <summary>
    /// 切割面三角剖分的边约束列表（切割面轮廓边）
    /// </summary>
    public List<EdgeConstraint> Constraints;

    /// <summary>
    /// 源网格顶点索引到本切片网格顶点索引的映射。
    /// 源网格的第 i 个顶点在本切片中的新索引为 IndexMap[i]
    /// </summary>
    public int[] IndexMap;

    /// <summary>
    /// 顶点数据的包围盒（需要手动调用 CalculateBounds() 来更新）
    /// </summary>
    public Bounds Bounds;

    /// <summary>
    /// 输出切片时应保留的子网格数量（由源网格结构决定，并在递归中传递）。
    /// 源网格无内部面 → 2；已有内部面 → 3。
    /// </summary>
    public int outputSubmeshCount = 2;

    /// <summary>
    /// 所有子网格的三角形总数（只读属性，遍历 Triangles 数组累加）
    /// </summary>
    /// <value></value>
    public int triangleCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < this.Triangles.Length; i++)
            {
                count += this.Triangles[i].Count;
            }
            return count;
        }
    }

    /// <summary>
    /// 网格的顶点总数（只读属性 = Vertices 数量 + CutVertices 数量）
    /// </summary>
    /// <value></value>
    public int vertexCount
    {
        get
        {
            return this.Vertices.Count + this.CutVertices.Count;
        }
    }

    /// <summary>
    /// 初始化一个新的切片网格
    /// </summary>
    /// <param name="name">网格的名称（参数保留未使用）</param>
    /// <param name="vertexCount">顶点数量，用于初始化 List 的容量。预先按近似大小初始化可以减少扩容与 GC</param>
    /// <param name="triangleCount">三角形数量，用于初始化 List 的容量。预先按近似大小初始化可以减少扩容与 GC</param>
    public FragmentData(int vertexCount, int triangleCount, int submeshCount = 2)
    {
        this.Vertices = new List<MeshVertex>(vertexCount);
        // 切割顶点通常远少于表面顶点，按十分之一预分配
        this.CutVertices = new List<MeshVertex>(vertexCount / 10);

        // 每个子网格单独存储三角形索引
        this.Triangles = new List<int>[submeshCount];
        this.Triangles[0] = new List<int>(triangleCount);
        for (int i = 1; i < submeshCount; i++)
        {
            // 切割面子网格的三角形远少于表面，按十分之一预分配
            this.Triangles[i] = new List<int>(triangleCount / 10);
        }

        this.Constraints = new List<EdgeConstraint>();
        this.IndexMap = new int[vertexCount];
    }

    /// <summary>
    /// 从源网格数据创建一个新的切片网格数据集。
    /// 会读取源网格的顶点/法线/UV/三角形，并按源网格的子网格结构初始化 Triangles
    /// </summary>
    /// <param name="mesh">源网格数据</param>
    public FragmentData(Mesh mesh)
    {
        var positions = mesh.vertices;
        var normals = mesh.normals;
        var uv = mesh.uv;

        this.Vertices = new List<MeshVertex>(mesh.vertexCount);
        this.CutVertices = new List<MeshVertex>(mesh.vertexCount / 10);
        this.Constraints = new List<EdgeConstraint>();
        this.IndexMap = new int[positions.Length];

        // 添加网格顶点
        for (int i = 0; i < positions.Length; i++)
        {
            this.Vertices.Add(new MeshVertex(positions[i], normals[i], uv[i]));
        }

        // 子网格列表数量与源网格一致：表面 / 已有切割面 / 上次新切割面。
        // 无内部面时数量为 2（Default + NewCutFace），已有内部面时为 3（再加 CutFace）。
        // 这里用 Mathf.Clamp(mesh.subMeshCount + 1, 2, 3) 保证最少 2 个、最多 3 个子网格
        this.Triangles = new List<int>[Mathf.Clamp(mesh.subMeshCount + 1, 2, 3)];
        this.Triangles[0] = new List<int>(mesh.GetTriangles(0));

        this.Triangles[1] = mesh.subMeshCount >= 2
            ? new List<int>(mesh.GetTriangles(1))
            : new List<int>();

        // 只有数组长度为 3 时才初始化第三个子网格，避免留下 null
        if (this.Triangles.Length > 2)
        {
            this.Triangles[2] = mesh.subMeshCount >= 3
                ? new List<int>(mesh.GetTriangles(2))
                : new List<int>();
        }

        // 根据源网格结构决定输出保留的子网格数量，并在后续递归切分中保持不变。
        // 这就是 outputSubmeshCount 的由来：它告诉 MeshSlicer 本切片网格应保留
        // 几个子网格，从而保证旧切割面与新切割面不会被混到同一个子网格里
        this.outputSubmeshCount = Mathf.Clamp(mesh.subMeshCount + 1, 2, 3);

        this.CalculateBounds();
    }

    /// <summary>
    /// 添加一个新的切割面顶点。
    /// 顶点会被同时加入 Vertices 与 CutVertices：
    /// - Vertices 中的这份用于最终网格的顶点流（顶点位置与表面顶点连成一体）；
    /// - CutVertices 中的这份在三角剖分时会更新为切割面的法线/UV。
    /// </summary>
    /// <param name="position">顶点位置</param>
    /// <param name="normal">顶点法线</param>
    /// <param name="uv">顶点 UV 坐标</param>
    /// <returns>该顶点在 cutVertices 数组中的索引</returns>
    public void AddCutFaceVertex(Vector3 position, Vector3 normal, Vector2 uv)
    {
        var vertex = new MeshVertex(position, normal, uv);

        // 同时把顶点加入普通网格顶点数据与切割面顶点数据。
        // 切割面里的那份顶点会拥有不同的法线/UV，
        // 稍后在三角剖分（FillCutFaces）过程中会被填充为正确的值
        this.Vertices.Add(vertex);
        this.CutVertices.Add(vertex);
    }

    /// <summary>
    /// 添加一个与源网格映射关联的新顶点
    /// </summary>
    /// <param name="vertex">顶点数据</param>
    /// <param name="sourceIndex">该顶点在源网格中的索引</param>
    public void AddMappedVertex(MeshVertex vertex, int sourceIndex)
    {
        this.Vertices.Add(vertex);
        this.IndexMap[sourceIndex] = this.Vertices.Count - 1;
    }

    /// <summary>
    /// 向本网格添加一个三角形。参数 v1、v2、v3 是相对于本网格顶点列表
    /// （Vertices）的索引，不进行任何映射
    /// </summary>
    /// <param name="v1">第一个顶点的索引</param>
    /// <param name="v2">第二个顶点的索引</param>
    /// <param name="v3">第三个顶点的索引</param>
    /// <param name="subMesh">该三角形所属的子网格</param>
    public void AddTriangle(int v1, int v2, int v3, SlicedMeshSubmesh subMesh)
    {
        this.Triangles[(int)subMesh].Add(v1);
        this.Triangles[(int)subMesh].Add(v2);
        this.Triangles[(int)subMesh].Add(v3);
    }

    /// <summary>
    /// 向本网格添加一个三角形。参数 v1、v2、v3 是源网格中的顶点索引，
    /// 会通过 IndexMap 映射为本切片网格中的索引
    /// </summary>
    /// <param name="v1">第一个顶点在源网格中的索引</param>
    /// <param name="v2">第二个顶点在源网格中的索引</param>
    /// <param name="v3">第三个顶点在源网格中的索引</param>
    /// <param name="subMesh">该三角形所属的子网格</param>
    public void AddMappedTriangle(int v1, int v2, int v3, SlicedMeshSubmesh subMesh)
    {
        this.Triangles[(int)subMesh].Add(IndexMap[v1]);
        this.Triangles[(int)subMesh].Add(IndexMap[v2]);
        this.Triangles[(int)subMesh].Add(IndexMap[v3]);
    }

    /// <summary>
    /// 找出切割面上位置重合（coincident）的顶点并把它们焊接在一起。
    /// 例如一个四边形切割面被分成两个三角形后，分割线两端的顶点位置相同，
    /// 需要合并为单个顶点，三角剖分才能正常工作。
    /// 同时会把边约束的端点索引映射到焊接后的新索引
    /// </summary>
    public void WeldCutFaceVertices()
    {
        // 临时数组存放焊接后的唯一顶点。
        // 容量预置为当前切割顶点数，避免不必要的重新分配
        List<MeshVertex> weldedVerts = new List<MeshVertex>(CutVertices.Count);

        // 记录每个被跳过（重复）的顶点索引 → 焊接后顶点索引的映射，
        // 用于之后更新边约束的端点
        int[] indexMap = new int[CutVertices.Count];

        // 临时数组中焊接后的顶点数量
        int k = 0;

        // 逐个顶点查找重复项。必须直接比较位置而不是用哈希表，
        // 因为浮点误差会导致"非常接近但不完全重合"的顶点在哈希表中不可靠
        for(int i = 0; i < CutVertices.Count; i++)
        {
            bool duplicate = false;
            for(int j = 0; j < weldedVerts.Count; j++)
            {
                if (CutVertices[i].position == weldedVerts[j].position)
                {
                    // 位置相同：记录映射并标记为重复
                    indexMap[i] = j;
                    duplicate = true;
                    break;
                }
            }

            if (!duplicate)
            {
                // 不是重复顶点：加入焊接列表
                weldedVerts.Add(CutVertices[i]);
                indexMap[i] = k;
                k++;
            }
        }

        // 更新边约束：把端点索引重映射到焊接后的索引
        for(int i = 0; i < Constraints.Count; i++)
        {
            var edge = Constraints[i];
            edge.v1 = indexMap[edge.v1];
            edge.v2 = indexMap[edge.v2];
        }

        weldedVerts.TrimExcess();

        // 用焊接后的顶点列表替换原切割顶点列表
        this.CutVertices = new List<MeshVertex>(weldedVerts);
    }

    /// <summary>
    /// 获取指定子网格的三角形索引数组
    /// </summary>
    /// <param name="subMeshIndex">子网格索引</param>
    /// <returns></returns>
    public int[] GetTriangles(int subMeshIndex)
    {
        return this.Triangles[subMeshIndex].ToArray();
    }

    /// <summary>
    /// 计算网格数据的包围盒
    /// </summary>
    public void CalculateBounds()
    {
        float vertexCount = (float)Vertices.Count;
        Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        // 切割面上的顶点不会超出物体原本的范围，所以只需要遍历原始顶点即可确定包围盒
        foreach(MeshVertex vertex in Vertices)
        {
            if (vertex.position.x < min.x) min.x = vertex.position.x;
            if (vertex.position.y < min.y) min.y = vertex.position.y;
            if (vertex.position.z < min.z) min.z = vertex.position.z;
            if (vertex.position.x > max.x) max.x = vertex.position.x;
            if (vertex.position.y > max.y) max.y = vertex.position.y;
            if (vertex.position.z > max.z) max.z = vertex.position.z;
        }

        this.Bounds = new Bounds((max + min) / 2f, max - min);
    }

    /// <summary>
    /// 把切片网格数据重建为一个 Unity Mesh 对象。
    /// 使用"设置顶点/索引缓冲"的高级 API：
    /// - 顶点流 = Vertices 在前、CutVertices 在后（CutVertices 依次接在 Vertices 末尾）；
    /// - 每个子网格的索引连续存放，通过 SubMeshDescriptor 描述其起始与长度
    /// </summary>
    /// <returns>返回重建好的 Mesh 对象</returns>
    public Mesh ToMesh()
    {
        Mesh mesh = new Mesh();
        
        // 声明顶点布局：位置（Float32 × 3）、法线（Float32 × 3）、UV0（Float32 × 2）
        var layout = new[]
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
        };

        mesh.SetIndexBufferParams(triangleCount, IndexFormat.UInt32);
        mesh.SetVertexBufferParams(vertexCount, layout);
        // 先把表面顶点写入顶点缓冲，再把切割顶点接在后面（偏移 = Vertices 数量）
        mesh.SetVertexBufferData(Vertices, 0, 0, Vertices.Count);
        mesh.SetVertexBufferData(CutVertices, 0, Vertices.Count, CutVertices.Count);

        mesh.subMeshCount = Triangles.Length;
        int indexStart = 0;
        for(int i = 0; i < Triangles.Length; i++)
        {
            var subMeshIndexBuffer = Triangles[i];
            // 把每个子网格的索引连续写入索引缓冲，并登记子网格描述符
            mesh.SetIndexBufferData(subMeshIndexBuffer, 0, indexStart, subMeshIndexBuffer.Count);
            mesh.SetSubMesh(i, new SubMeshDescriptor(indexStart, subMeshIndexBuffer.Count));
            indexStart += subMeshIndexBuffer.Count;
        }
        
        mesh.RecalculateBounds();
        
        return mesh;
    }
}