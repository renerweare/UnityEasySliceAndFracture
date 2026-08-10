using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 切分调度器（Fragmenter）：对外提供"破碎"（Fracture）与"单刀切分"（Slice）两种入口。
/// - Fracture：把一个网格递归地切成 N 个碎片——每次从队列中取出一个碎片，
///   选一个随机切割面切成两半，再放回队列，直到碎片数达到目标数量；
///   FractureAsync 为协程版本，每切一刀让出一帧（yield return null），避免卡死主线程。
/// - Slice：只切一刀，把源物体切成上下两片。
/// 两种入口最终都会调用 CreateFragment，把切分得到的网格数据生成实际的
/// GameObject 碎片（克隆 fragmentTemplate 模板，并设置网格、碰撞体与质量）。
/// </summary>
public static class Fragmenter
{
    /// <summary>
    /// 根据选项生成网格碎片。生成的碎片物体作为 fragmentParent 的子物体
    /// </summary>
    /// <param name="sourceObject">要破碎的源物体。必须带有 MeshFilter、Rigidbody 和 Collider</param>
    /// <param name="options">破碎选项</param>
    /// <param name="fragmentTemplate">每个碎片要克隆的模板 GameObject</param>
    /// <param name="parent">碎片物体的父 Transform</param>
    /// <param name="saveToDisk">若为 true，生成的碎片网格会被保存到磁盘，以便在预制体里复用</param>
    /// <param name="saveFolderPath">碎片网格的保存位置</param>
    /// <returns></returns>
    public static void Fracture(GameObject sourceObject,
                                FractureOptions options,
                                GameObject fragmentTemplate,
                                Transform parent,
                                bool saveToDisk = false,
                                string saveFolderPath = "")
    {
        // 定义破碎用的源网格数据（把 Unity Mesh 转换为 FragmentData）
        FragmentData sourceMesh = new FragmentData(sourceObject.GetComponent<MeshFilter>().sharedMesh);
 
        // 先入队源网格，然后用 FIFO 队列逐个处理每个碎片，
        // 直到碎片数量达到目标（fragmentCount）
        var fragments = new Queue<FragmentData>();
        fragments.Enqueue(sourceMesh);

        // 把网格不断细分为多个碎片，直到达到碎片数量上限
        FragmentData topSlice, bottomSlice;
        while (fragments.Count < options.fragmentCount)
        {
            FragmentData meshData = fragments.Dequeue();
            meshData.CalculateBounds();

            // 选择任意的破碎面法线方向：
            // 根据选项决定是否允许在 x/y/z 轴上随机选取分量（全为 0 时法线为零向量）
            Vector3 normal = new Vector3(
                options.xAxis ? Random.Range(-1f, 1f) : 0f,
                options.yAxis ? Random.Range(-1f, 1f) : 0f,
                options.zAxis ? Random.Range(-1f, 1f) : 0f);

            // 切分！以碎片包围盒中心为切割面原点，随机法线为方向，把碎片一分为二
            MeshSlicer.Slice(meshData,
                             normal,
                             meshData.Bounds.center,
                             options.textureScale,
                             options.textureOffset,
                             out topSlice,
                             out bottomSlice);

            fragments.Enqueue(topSlice);
            fragments.Enqueue(bottomSlice);
        }

        int i = 0;
        // 把队列里所有碎片数据逐个生成实际物体
        foreach(FragmentData meshData in fragments)
        {
            CreateFragment(meshData, 
                           sourceObject,
                           fragmentTemplate, 
                           parent,
                           saveToDisk,
                           saveFolderPath,
                           options.detectFloatingFragments,
                           ref i);
        }
    }

    /// <summary>
    /// 异步（协程）版本：根据选项生成网格碎片。生成的碎片物体作为 fragmentParent 的子物体。
    /// 每次切割之间通过 yield return null 让出一帧，避免一次性大量计算导致卡顿
    /// </summary>
    /// <param name="sourceObject">要破碎的源物体。必须带有 MeshFilter、Rigidbody 和 Collider</param>
    /// <param name="options">破碎选项</param>
    /// <param name="fragmentTemplate">每个碎片要克隆的模板 GameObject</param>
    /// <param name="parent">碎片物体的父 Transform</param>
    /// <returns></returns>
    public static IEnumerator FractureAsync(GameObject sourceObject,
                                            FractureOptions options,
                                            GameObject fragmentTemplate,
                                            Transform parent,
                                            Action onCompletion)
    {
        // 定义破碎用的源网格数据
        FragmentData sourceMesh = new FragmentData(sourceObject.GetComponent<MeshFilter>().sharedMesh);
 
        // 先入队源网格，然后用 FIFO 队列逐个处理，直到碎片数量达到目标
        var fragments = new Queue<FragmentData>();
        fragments.Enqueue(sourceMesh);

        // 把网格不断细分为多个碎片，直到达到碎片数量上限
        FragmentData topSlice, bottomSlice;
        while (fragments.Count < options.fragmentCount)
        {
            FragmentData meshData = fragments.Dequeue();
            meshData.CalculateBounds();

            // 选择任意的破碎面法线方向（依据 x/y/z 轴选项随机取值）
            Vector3 normal = new Vector3(
                options.xAxis ? Random.Range(-1f, 1f) : 0f,
                options.yAxis ? Random.Range(-1f, 1f) : 0f,
                options.zAxis ? Random.Range(-1f, 1f) : 0f);

            // 切分！
            MeshSlicer.Slice(meshData,
                             normal,
                             meshData.Bounds.center,
                             options.textureScale,
                             options.textureOffset,
                             out topSlice,
                             out bottomSlice);

            // 下一次切分放到下一帧执行，避免一帧内完成全部切分造成卡顿
            yield return null;

            fragments.Enqueue(topSlice);
            fragments.Enqueue(bottomSlice);
        }

        int i = 0;
        // 把队列里所有碎片数据逐个生成实际物体
        foreach(FragmentData meshData in fragments)
        {
            CreateFragment(meshData, 
                           sourceObject,
                           fragmentTemplate, 
                           parent,
                           false,
                           "",
                           options.detectFloatingFragments,
                           ref i);
        }

        // 全部完成后回调
        onCompletion?.Invoke();
    }

    /// <summary>
    /// 把源物体沿指定平面切成两片。生成的切片物体作为 fragmentParent 的子物体
    /// </summary>
    /// <param name="sourceObject">要切分的源物体。必须带有 MeshFilter、Rigidbody 和 Collider</param>
    /// <param name="sliceNormal">切割面法线（在 sourceObject 的局部坐标系中）</param>
    /// <param name="sliceOrigin">切割面原点（在 sourceObject 的局部坐标系中）</param>
    /// <param name="options">切分选项</param>
    /// <param name="fragmentTemplate">每个切片要克隆的模板 GameObject</param>
    /// <param name="parent">切片物体的父 Transform</param>
    /// <returns></returns>
    public static void Slice(GameObject sourceObject,
                             Vector3 sliceNormal,
                             Vector3 sliceOrigin,
                             SliceOptions options,
                             GameObject fragmentTemplate,
                             Transform parent)
    {
        // 定义切分用的源网格数据
        FragmentData sourceMesh = new FragmentData(sourceObject.GetComponent<MeshFilter>().sharedMesh);
        // 声明切分结果
        FragmentData topSlice, bottomSlice;

        // 切分！
        MeshSlicer.Slice(sourceMesh,
                         sliceNormal,
                         sliceOrigin,
                         options.textureScale,
                         options.textureOffset,
                         out topSlice,
                         out bottomSlice);

        int i = 0;
        // 生成上半切片物体
        CreateFragment(topSlice,
                       sourceObject,
                       fragmentTemplate,
                       parent,
                       false,
                       "",
                       options.detectFloatingFragments,
                       ref i);

        // 生成下半切片物体
        CreateFragment(bottomSlice,
                       sourceObject,
                       fragmentTemplate,
                       parent,
                       false,
                       "",
                       options.detectFloatingFragments,
                       ref i);
    }

    /// <summary>
    /// 根据碎片网格数据创建一个新的 GameObject（克隆 fragmentTemplate 模板），
    /// 并设置其网格、网格碰撞体与刚体质量。
    /// 若启用 detectFloatingFragments，还会把碎片网格中互不相连的几何体
    /// 拆成多个独立的物理物体
    /// </summary>
    /// <param name="fragmentMeshData">切分器产生的碎片几何数据</param>
    /// <param name="sourceObject">要破碎的源物体（提供网格与刚体参数）</param>
    /// <param name="fragmentTemplate">每个碎片要克隆的模板 GameObject</param>
    /// <param name="parent">碎片物体的父 Transform</param>
    /// <param name="i">碎片计数器（引用传递，跨碎片递增）</param>
    private static void CreateFragment(FragmentData fragmentMeshData,
                                       GameObject sourceObject,
                                       GameObject fragmentTemplate,
                                       Transform parent,
                                       bool saveToDisk,
                                       string saveFolderPath,
                                       bool detectFloatingFragments,
                                       ref int i)
    {
        // 若没有网格数据，不创建物体
        if (fragmentMeshData.Triangles.Length == 0)
        {
            return;
        }

        Mesh[] meshes;
        Mesh fragmentMesh = fragmentMeshData.ToMesh();

        // 若启用了"Detect Floating Fragments"选项，把碎片网格中的
        // 不相连几何体识别出来，每一块都作为独立的物理物体处理
        if (detectFloatingFragments)
        {
            meshes = MeshUtils.FindDisconnectedMeshes(fragmentMesh);
        }
        else
        {
            meshes = new Mesh[] { fragmentMesh };
        }

        var parentSize = sourceObject.GetComponent<MeshFilter>().sharedMesh.bounds.size;
        var parentMass = sourceObject.GetComponent<Rigidbody>().mass;

        for(int k = 0; k < meshes.Length; k++)
        {
            // 克隆模板物体，命名为 Fragment{i}
            GameObject fragment = GameObject.Instantiate(fragmentTemplate, parent);
            fragment.name = $"Fragment{i}";
            fragment.transform.localPosition = Vector3.zero;
            fragment.transform.localRotation = Quaternion.identity;
            fragment.transform.localScale = sourceObject.transform.localScale;

            // 给网格一个唯一的名字（用于保存到磁盘时作为文件名）
            meshes[k].name = System.Guid.NewGuid().ToString();

            // 把网格更新为新的切片网格
            var meshFilter = fragment.GetComponent<MeshFilter>();
            meshFilter.sharedMesh = meshes[k];

            var collider = fragment.GetComponent<MeshCollider>();

            // 若碎片碰撞被禁用，collider 会为 null
            collider.sharedMesh = meshes[k];
            collider.convex = true;
            collider.sharedMaterial = fragment.GetComponent<Collider>().sharedMaterial;

            // 通过"网格体积 / 密度"计算切片物体的质量：
            // 密度 = 父物体体积 / 父物体质量，质量 = 碎片体积 / 密度，
            // 使碎片的质量与体积成比例（保持总体密度一致）
            var parentRigidBody = sourceObject.GetComponent<Rigidbody>();
            var rigidBody = fragment.GetComponent<Rigidbody>();

            var size = fragmentMesh.bounds.size;
            float density = (parentSize.x * parentSize.y * parentSize.z) / parentMass;
            rigidBody.mass = (size.x * size.y * size.z) / density;
            
            // 以下代码只在编辑器中编译
            #if UNITY_EDITOR
            if (saveToDisk)
            {
                // 把碎片网格保存为 .asset 资源文件
                string path = $"{saveFolderPath}/{meshes[k].name}.asset";
                AssetDatabase.CreateAsset(meshes[k], path);
            }
            #endif

            i++;
        }
    }
}