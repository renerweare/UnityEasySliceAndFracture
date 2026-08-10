using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// 单个顶点的数据结构：包含位置（position）、法线（normal）与 UV 坐标（uv）。
/// 这是一个值类型（struct），在切分过程中会被大量拷贝，因此保持轻量。
/// 顶点相等性的判断只基于 position（见 Equals），用于切割面重合顶点的焊接等场景。
/// </summary>
public struct MeshVertex
{
    /// <summary>
    /// 顶点在网格局部空间中的位置坐标
    /// </summary>
    public Vector3 position;
    /// <summary>
    /// 顶点法线，用于光照计算（切割边上新生成的顶点由插值得到并做归一化）
    /// </summary>
    public Vector3 normal;
    /// <summary>
    /// 顶点 UV 坐标，用于纹理映射（切割边上新生成的顶点由插值得到）
    /// </summary>
    public Vector2 uv;

    /// <summary>
    /// 仅指定位置创建顶点，法线与 UV 默认为零向量
    /// </summary>
    /// <param name="position">顶点的位置坐标</param>
    public MeshVertex(Vector3 position)
    {
        this.position = position;
        this.normal = Vector3.zero;
        this.uv = Vector2.zero;
    }

    /// <summary>
    /// 完整创建顶点：同时指定位置、法线与 UV 坐标
    /// </summary>
    /// <param name="position">顶点的位置坐标</param>
    /// <param name="normal">顶点的法线方向</param>
    /// <param name="uv">顶点的 UV 坐标</param>
    public MeshVertex(Vector3 position, Vector3 normal, Vector2 uv)
    {
        this.position = position;
        this.normal = normal;
        this.uv = uv;
    }

    /// <summary>
    /// 判断两个顶点是否相等。
    /// 注意：只比较 position（位置），忽略 normal 与 uv——
    /// 位置相同的点即被视为同一个顶点，这是焊接切割面重合顶点的基础。
    /// </summary>
    /// <param name="obj">待比较的对象</param>
    /// <returns>若对象是 MeshVertex 且位置相同，返回 true</returns>
    public override bool Equals(object obj)
    {
        if (!(obj is MeshVertex)) return false;
       
        return ((MeshVertex)obj).position.Equals(this.position);
    }

    /// <summary>
    /// == 运算符重载：等价于调用 Equals（即仅比较位置）
    /// </summary>
    public static bool operator ==(MeshVertex lhs, MeshVertex rhs)
    {
        return lhs.Equals(rhs);
    }

    /// <summary>
    /// != 运算符重载：等价于 Equals 结果的取反
    /// </summary>
    public static bool operator !=(MeshVertex lhs, MeshVertex rhs)
    {
        return !lhs.Equals(rhs);
    }

    /// <summary>
    /// 哈希码基于位置计算，与 Equals 的契约保持一致
    /// </summary>
    public override int GetHashCode()
    {
        return this.position.GetHashCode();
    }

    /// <summary>
    /// 返回顶点的字符串描述（位置/法线/UV），便于调试
    /// </summary>
    [ExcludeFromCoverage]
    public override string ToString()
    {
        return $"Position = {position}, Normal = {normal}, UV = {uv}";
    }
}