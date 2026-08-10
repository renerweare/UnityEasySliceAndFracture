using UnityEngine;

/// <summary>
/// 平面切分器（MeshSlicer）：给定切割面的原点（sliceOrigin）与法线（sliceNormal），
/// 把一个网格切成两半，输出位于平面两侧的两个新网格（topSlice / bottomSlice）。
/// 整个过程分为三步：
/// 1) 对所有顶点做"面两侧分类"（IsAbovePlane），划分到上半/下半；
/// 2) 对每个三角形分类：全部在上 → 归入上半；全部在下 → 归入下半；
///    跨过切割面 → 用 SplitTriangle 分裂成多个三角形（在切割边上插值出新顶点）；
/// 3) 用三角剖分（ConstrainedTriangulator）填充两半的切割面，并生成正确的法线与 UV。
/// 切割面法线方向约定：sliceNormal 指向"上半"（topSlice）一侧。
/// </summary>
public static class MeshSlicer
{
    /// <summary>
    /// 按 sliceNormal 与 sliceOrigin 指定的平面把网格切成两半。
    /// 切分结果通过 out 参数返回
    /// </summary>
    /// <param name="meshData">待切分的源网格数据</param>
    /// <param name="sliceNormal">切割面法线（指向"上半"切片）</param>
    /// <param name="sliceOrigin">切割面原点</param>
    /// <param name="textureScale">应用于 UV 坐标的缩放系数</param>
    /// <param name="textureOffset">应用于 UV 坐标的偏移量</param>
    /// <param name="topSlice">out 参数：位于平面上方的切片网格数据</param>
    /// <param name="bottomSlice">out 参数：位于平面下方的切片网格数据</param>
    public static void Slice(FragmentData meshData,
                             Vector3 sliceNormal,
                             Vector3 sliceOrigin,
                             Vector2 textureScale,
                             Vector2 textureOffset,
                             out FragmentData topSlice,
                             out FragmentData bottomSlice)
    {
        // 输出子网格数量由源网格结构决定（并在递归中传递）：
        // 源网格无内部面 → 2 个子网格；已有内部面 → 3 个子网格，旧面与新切面分开上色
        int submeshCount = meshData.outputSubmeshCount;

        topSlice = new FragmentData(meshData.vertexCount, meshData.triangleCount, submeshCount);
        bottomSlice = new FragmentData(meshData.vertexCount, meshData.triangleCount, submeshCount);
        topSlice.outputSubmeshCount = submeshCount;
        bottomSlice.outputSubmeshCount = submeshCount;

        // 记录每个顶点位于切割面的哪一侧（true = 上半）
        bool[] side = new bool[meshData.vertexCount];

        // 第一步：遍历源网格的普通顶点（Vertices），
        // 判断每个顶点在切割面的上方还是下方，并映射到对应的切片
        for (int i = 0; i < meshData.Vertices.Count; i++)
        {
            var vertex = meshData.Vertices[i];
            side[i] = vertex.position.IsAbovePlane(sliceNormal, sliceOrigin);
            var slice = side[i] ? topSlice : bottomSlice;
            slice.AddMappedVertex(vertex, i);
        }

        // 对上一轮切分留下的切割面顶点（CutVertices）也做同样的分类与映射。
        // 顶点索引整体偏移 Vertices 数量，避免与上面的 side 数组下标冲突
        int offset = meshData.Vertices.Count;
        for (int i = 0; i < meshData.CutVertices.Count; i++)
        {
            var vertex = meshData.CutVertices[i];
            side[i + offset] = vertex.position.IsAbovePlane(sliceNormal, sliceOrigin);
            var slice = side[i + offset] ? topSlice : bottomSlice;
            slice.AddMappedVertex(vertex, i + offset);
        }

        // 第二步：分裂三角形。先处理表面子网格（Default）
        SplitTriangles(meshData, topSlice, bottomSlice, sliceNormal, sliceOrigin, side, SlicedMeshSubmesh.Default);
        // 若源网格还有旧的切割面子网格，也一并按同样方式分裂并迁移到对应的子网格
        if (meshData.Triangles.Length > 1 && meshData.Triangles[1].Count > 0)
        {
            SplitTriangles(meshData, topSlice, bottomSlice, sliceNormal, sliceOrigin, side, SlicedMeshSubmesh.CutFace);
        }
        // 若源网格还有上次操作生成的切割面，也一并搬运到相同子网格
        if (meshData.Triangles.Length > 2 && meshData.Triangles[2].Count > 0)
        {
            SplitTriangles(meshData, topSlice, bottomSlice, sliceNormal, sliceOrigin, side, SlicedMeshSubmesh.NewCutFace);
        }

        // 第三步：为两半网格填充切割面。
        // 切割面法线指向"上半"网格，所以上半网格切割面的法线与 sliceNormal 相反；
        // 反过来，下半网格切割面的法线与 sliceNormal 同向。
        // 切割面三角形被放入最后一个子网格（索引 = submeshCount - 1，即 NewCutFace）
        FillCutFaces(topSlice, bottomSlice, -sliceNormal, textureScale, textureOffset, (SlicedMeshSubmesh)(submeshCount - 1));
    }

    /// <summary>
    /// 为两半切片网格填充切割面。参数 sliceNormal 是切割面的法线，
    /// 指向 topMeshData 一侧
    /// </summary>
    /// <param name="topSlice">切割面上方的切片网格数据</param>
    /// <param name="bottomSlice">切割面上方的切片网格数据</param>
    /// <param name="sliceNormal">切割面法线（指向"上半"切片）</param>
    /// <param name="textureScale">应用于 UV 坐标的缩放系数</param>
    /// <param name="textureOffset">应用于 UV 坐标的偏移量</param>
    private static void FillCutFaces(FragmentData topSlice,
                                     FragmentData bottomSlice,
                                     Vector3 sliceNormal,
                                     Vector2 textureScale,
                                     Vector2 textureOffset,
                                     SlicedMeshSubmesh subMesh)
    {
        // 上下两半共享同一个切割面，所以只需要计算一次切割面的
        // 顶点/三角形数据，然后两半复用同一份数据，只是法线方向相反

        // 首先需要焊接切割面上位置重合的顶点，三角剖分才能正确工作
        topSlice.WeldCutFaceVertices();

        // 至少需要 3 个顶点才能做三角剖分
        if (topSlice.CutVertices.Count < 3) return;

        // 对切割面做受约束三角剖分（约束 = 切割面轮廓边）
        var triangulator = new ConstrainedTriangulator(topSlice.CutVertices, topSlice.Constraints, sliceNormal);
        int[] triangles = triangulator.Triangulate();

        // 更新切割面顶点的法线与 UV
        for (int i = 0; i < topSlice.CutVertices.Count; i++)
        {
            var vertex = topSlice.CutVertices[i];
            var point = triangulator.points[i];

            // UV 坐标基于三角剖分时使用的二维坐标计算：
            // 剖分时坐标被归一化到了 [0,1]，所以需要乘以归一化缩放系数
            // 还原回真实尺度，再乘以纹理缩放并加上偏移
            Vector2 uv = new Vector2(
                (triangulator.normalizationScaleFactor * point.coords.x) * textureScale.x + textureOffset.x,
                (triangulator.normalizationScaleFactor * point.coords.y) * textureScale.y + textureOffset.y);

            // 更新切割顶点的法线与 UV：上半使用 sliceNormal（法线朝外），
            // 下半使用 -sliceNormal（法线朝向相反，保证可见面朝外）
            var topVertex = vertex;
            topVertex.normal = sliceNormal;
            topVertex.uv = uv;

            var bottomVertex = vertex;
            bottomVertex.normal = -sliceNormal;
            bottomVertex.uv = uv;

            topSlice.CutVertices[i] = topVertex;
            bottomSlice.CutVertices[i] = bottomVertex;
        }

        // 把三角剖分得到的新三角形加入上/下两半网格。
        // 顶点索引需要加上各自 Vertices 的数量作为偏移，
        // 因为三角形的索引是相对于"切割顶点列表"的，而网格顶点流里切割顶点排在 Vertices 之后
        int offsetTop = topSlice.Vertices.Count;
        int offsetBottom = bottomSlice.Vertices.Count;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            topSlice.AddTriangle(
                offsetTop + triangles[i],
                offsetTop + triangles[i + 1],
                offsetTop + triangles[i + 2],
                subMesh);

            bottomSlice.AddTriangle(
                offsetBottom + triangles[i],
                offsetBottom + triangles[i + 2], // 交换两个顶点使三角形绕序变为顺时针（CW）
                offsetBottom + triangles[i + 1],
                subMesh);
        }
    }

    /// <summary>
    /// 找出被切割面穿过的三角形并把它们分裂成两半
    /// </summary>
    /// <param name="meshData">源网格数据</param>
    /// <param name="topSlice">切割面上方的切片网格数据</param>
    /// <param name="bottomSlice">切割面上方的切片网格数据</param>
    /// <param name="sliceNormal">切割面法线（指向"上半"切片）</param>
    /// <param name="sliceOrigin">切割面原点</param>
    /// <param name="side">每个顶点位于上半还是下半的标记数组</param>
    /// <param name="subMesh">要处理的子网格</param>
    private static void SplitTriangles(FragmentData meshData,
                                       FragmentData topSlice,
                                       FragmentData bottomSlice,
                                       Vector3 sliceNormal,
                                       Vector3 sliceOrigin,
                                       bool[] side,
                                       SlicedMeshSubmesh subMesh)
    {
        int[] triangles = meshData.GetTriangles((int)subMesh);

        // a、b、c 分别缓存当前三角形的三个顶点索引
        int a, b, c;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            // 取出该三角形的三个顶点索引
            a = triangles[i];
            b = triangles[i + 1];
            c = triangles[i + 2];

            // 三角形完全位于上半（网格 A）
            if (side[a] && side[b] && side[c])
            {
                topSlice.AddMappedTriangle(a, b, c, subMesh);
            }
            // 三角形完全位于下半（网格 B）
            else if (!side[a] && !side[b] && !side[c])
            {
                bottomSlice.AddMappedTriangle(a, b, c, subMesh);
            }
            // 三角形被切割面穿过，需要细分：
            // 下面分"两个顶点在上、一个在下"和"两个在下、一个在上"两种情况处理。
            // 统一把"与另两个顶点异侧的那个顶点"作为 v3 传入 SplitTriangle，
            // 并用 v3BelowCutPlane 标记 v3 是否在平面下方
            else
            {
                // 以下情况：两个顶点在平面上方，一个顶点在下方
                if (side[b] && side[c] && !side[a])
                {
                    SplitTriangle(b, c, a, sliceNormal, sliceOrigin, meshData, topSlice, bottomSlice, subMesh, true);
                }
                else if (side[c] && side[a] && !side[b])
                {
                    SplitTriangle(c, a, b, sliceNormal, sliceOrigin, meshData, topSlice, bottomSlice, subMesh, true);
                }
                else if (side[a] && side[b] && !side[c])
                {
                    SplitTriangle(a, b, c, sliceNormal, sliceOrigin, meshData, topSlice, bottomSlice, subMesh, true);
                }
                // 以下情况：两个顶点在平面下方，一个顶点在上方
                else if (!side[b] && !side[c] && side[a])
                {
                    SplitTriangle(b, c, a, sliceNormal, sliceOrigin, meshData, topSlice, bottomSlice, subMesh, false);
                }
                else if (!side[c] && !side[a] && side[b])
                {
                    SplitTriangle(c, a, b, sliceNormal, sliceOrigin, meshData, topSlice, bottomSlice, subMesh, false);
                }
                else if (!side[a] && !side[b] && side[c])
                {
                    SplitTriangle(a, b, c, sliceNormal, sliceOrigin, meshData, topSlice, bottomSlice, subMesh, false);
                }
            }
        }
    }

    /// <summary>
    /// 把由点 (v1,v2,v3) 定义的三角形分裂成两半：
    /// v1、v2 在切割面同一侧，v3 在另一侧。切割面与边 (v1,v3)、(v2,v3) 的交点
    /// 通过线性插值得到（位置/法线/UV），再重组三角形
    /// </summary>
    /// <param name="v1_idx">三角形第一个顶点索引</param>
    /// <param name="v2_idx">三角形第二个顶点索引</param>
    /// <param name="v3_idx">三角形第三个顶点索引</param>
    /// <param name="sliceNormal">切割面法线（指向"上半"切片）</param>
    /// <param name="sliceOrigin">切割面原点</param>
    /// <param name="meshData">源网格数据</param>
    /// <param name="topSlice">上半切片网格数据</param>
    /// <param name="bottomSlice">下半切片网格数据</param>
    /// <param name="subMesh">该三角形所属的子网格</param>
    /// <param name="v3BelowCutPlane">v3 是否位于切割面下方</param>                                             
    private static void SplitTriangle(int v1_idx,
                                      int v2_idx,
                                      int v3_idx,
                                      Vector3 sliceNormal,
                                      Vector3 sliceOrigin,
                                      FragmentData meshData,
                                      FragmentData topSlice,
                                      FragmentData bottomSlice,
                                      SlicedMeshSubmesh subMesh,
                                      bool v3BelowCutPlane)       
    {
        // - `v1`、`v2`、`v3` 是三角形三个顶点在源网格数据中的索引
        // - `v1` 和 `v2` 位于切割面属于 meshA（上半）的一侧
        // - `v3` 位于切割面属于 meshB（下半）的一侧
        // - 通过 `s13`/`s23` 插值比例在边上计算新顶点的位置/法线/UV
        //      
        // v3BelowCutPlane = true
        // ======================
        //                                
        //     v1 *_____________* v2   .
        //         \           /      /|\  cutNormal
        //          \         /        |
        //       ----*-------*---------*--
        //        v13 \     /  v23       cutOrigin
        //             \   /
        //              \ /
        //               *  v3         triangle normal out of screen                                                                                  
        //    
        // v3BelowCutPlane = false
        // =======================
        //
        //               *  v3         .                                             
        //              / \           /|\  cutNormal  
        //         v23 /   \ v13       |                    
        //       -----*-----*----------*--
        //           /       \         cut origin                                
        //          /         \                                                                  
        //      v2 *___________* v1    triangle normal out of screen
        //                 
        // 上面两图分别展示了"v3 在平面下方"与"v3 在平面上方"两种情形，
        // 切割面与两条边相交于 v13、v23 两点。

        float s13;
        float s23;
        Vector3 v13;
        Vector3 v23;

        // 根据索引范围取出顶点：小于 Vertices.Count 的是普通顶点，
        // 否则是切割面顶点（需要减去 Vertices.Count 偏移）
        MeshVertex v1 = v1_idx < meshData.Vertices.Count ? meshData.Vertices[v1_idx] : meshData.CutVertices[v1_idx - meshData.Vertices.Count];
        MeshVertex v2 = v2_idx < meshData.Vertices.Count ? meshData.Vertices[v2_idx] : meshData.CutVertices[v2_idx - meshData.Vertices.Count];
        MeshVertex v3 = v3_idx < meshData.Vertices.Count ? meshData.Vertices[v3_idx] : meshData.CutVertices[v3_idx - meshData.Vertices.Count];

        // 计算两条边 (v1,v3)、(v2,v3) 与切割面的交点：
        // v13/s13 为边 (v1,v3) 的交点位置与插值比例，v23/s23 同理
        if (MathUtils.LinePlaneIntersection(v1.position, v3.position, sliceNormal, sliceOrigin, out v13, out s13) &&
            MathUtils.LinePlaneIntersection(v2.position, v3.position, sliceNormal, sliceOrigin, out v23, out s23))
        {
            // 插值法线与 UV 坐标（s 为插值比例，0~1 之间）
            var norm13 = (v1.normal + s13 * (v3.normal - v1.normal)).normalized;
            var norm23 = (v2.normal + s23 * (v3.normal - v2.normal)).normalized;
            var uv13 = v1.uv + s13 * (v3.uv - v1.uv);
            var uv23 = v2.uv + s23 * (v3.uv - v2.uv);

            // 把两个交点作为新顶点加入上/下两半网格的切割面顶点列表
            //（同时也会加入 Vertices，见 AddCutFaceVertex）
            topSlice.AddCutFaceVertex(v13, norm13, uv13);
            topSlice.AddCutFaceVertex(v23, norm23, uv23);
            bottomSlice.AddCutFaceVertex(v13, norm13, uv13);
            bottomSlice.AddCutFaceVertex(v23, norm23, uv23);

            // 两个交点在各自网格顶点列表中的索引：
            // 因为两半都加入了相同顺序的两个顶点，所以索引分别是倒数第 2、倒数第 1
            int index13_A = topSlice.Vertices.Count - 2;
            int index23_A = topSlice.Vertices.Count - 1;
            int index13_B = bottomSlice.Vertices.Count - 2;
            int index23_B = bottomSlice.Vertices.Count - 1;

            if (v3BelowCutPlane)
            {
                // 切割面以上的部分是四边形（v1, v13, v23, v2），拆成两个三角形
                topSlice.AddTriangle(index23_A, index13_A, topSlice.IndexMap[v2_idx], subMesh);
                topSlice.AddTriangle(index13_A, topSlice.IndexMap[v1_idx], topSlice.IndexMap[v2_idx], subMesh);

                // 切割面以下的部分是一个三角形（v13, v23, v3）
                bottomSlice.AddTriangle(bottomSlice.IndexMap[v3_idx], index13_B, index23_B, subMesh);

                // 从切割面外侧看，切割面的轮廓边应按逆时针绕序，
                // 记录约束边供三角剖分使用（注意两半的方向相反）
                topSlice.Constraints.Add(new EdgeConstraint(topSlice.CutVertices.Count - 2, topSlice.CutVertices.Count - 1));
                bottomSlice.Constraints.Add(new EdgeConstraint(bottomSlice.CutVertices.Count - 1, bottomSlice.CutVertices.Count - 2));
            }
            else
            {
                // 切割面以上的部分是一个三角形（v13, v23, v3）
                topSlice.AddTriangle(index13_A, index23_A, topSlice.IndexMap[v3_idx], subMesh);

                // 切割面以下的部分是四边形（v1, v2, v23, v13），拆成两个三角形
                bottomSlice.AddTriangle(bottomSlice.IndexMap[v1_idx], bottomSlice.IndexMap[v2_idx], index13_B, subMesh);
                bottomSlice.AddTriangle(bottomSlice.IndexMap[v2_idx], index23_B, index13_B, subMesh);

                // 从切割面外侧看，切割面的轮廓边应按逆时针绕序，
                // 记录约束边供三角剖分使用（注意两半的方向相反）
                topSlice.Constraints.Add(new EdgeConstraint(topSlice.CutVertices.Count - 1, topSlice.CutVertices.Count - 2));
                bottomSlice.Constraints.Add(new EdgeConstraint(bottomSlice.CutVertices.Count - 2, bottomSlice.CutVertices.Count - 1));
            }
        }
    }
}