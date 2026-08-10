using UnityEngine.TestTools;

/// <summary>
/// 边约束（EdgeConstraint）：表示三角剖分中必须保留的两顶点（v1-v2）之间的边。
/// 切割面的轮廓边以"约束"的形式传给受约束三角剖分器，使剖分出的三角形
/// 不会跨越这条边（从而完整保留切割面的边界形状，支持凹多边形与带孔多边形）。
/// 相等性判断不区分方向：v1-v2 与 v2-v1 视为同一条边。
/// </summary>
public class EdgeConstraint
{
    /// <summary>
    /// 约束边的第一个端点（在三角剖分点数组中的索引）
    /// </summary>
    public int v1;
    
    /// <summary>
    /// 约束边的第二个端点（在三角剖分点数组中的索引）
    /// </summary>
    public int v2;

    /// <summary>
    /// 沿 v1 -> v2 方向，跨越该约束边之前所在三角形的索引
    /// </summary>
    public int t1;

    /// <summary>
    /// 沿 v1 -> v2 方向，跨越该约束边之后所在三角形的索引
    /// </summary>
    public int t2;

    /// <summary>
    /// 该约束边在三角形 t1 中对应的边索引（E12/E23/E31 之一）
    /// </summary>
    public int t1Edge;

    /// <summary>
    /// 仅指定两个端点创建边约束，两侧三角形暂设为 -1（未知），
    /// 之后由三角剖分过程查找并填充
    /// </summary>
    /// <param name="v1">第一个端点的索引</param>
    /// <param name="v2">第二个端点的索引</param>
    public EdgeConstraint(int v1, int v2)
    {
        this.v1 = v1;
        this.v2 = v2;
        this.t1 = -1;
        this.t2 = -1;
    }

    /// <summary>
    /// 创建边约束，同时记录边两侧的三角形，以及该边在三角形 triangle1 中对应的边索引。
    /// 这些信息用于：当需要找出与约束边相交的三角形边（构成四边形）时，
    /// 快速定位三角形之间的邻接关系。
    /// </summary>
    /// <param name="v1">第一个端点的索引</param>
    /// <param name="v2">第二个端点的索引</param>
    /// <param name="triangle1">约束边所在（出发侧）的三角形索引</param>
    /// <param name="triangle2">约束边跨越后进入的三角形索引</param>
    /// <param name="edge1">约束边在 triangle1 中对应的边索引（E12/E23/E31）</param>
    public EdgeConstraint(int v1, int v2, int triangle1, int triangle2, int edge1)
    {
        this.v1 = v1;
        this.v2 = v2;
        this.t1 = triangle1;
        this.t2 = triangle2;
        this.t1Edge = edge1;
    }

    /// <summary>
    /// 判断两条边约束是否相等：端点相同即相等，不区分方向（v1-v2 等价于 v2-v1）
    /// </summary>
    /// <param name="obj">待比较的对象</param>
    /// <returns>端点（忽略顺序）相同返回 true</returns>
    public override bool Equals(object obj)
    {
        if (obj is EdgeConstraint)
        {
            var other = (EdgeConstraint)obj;
            return (this.v1 == other.v1 && this.v2 == other.v2) ||
                   (this.v1 == other.v2 && this.v2 == other.v1);
        }
        return false;
    }

    /// <summary>
    /// 哈希码：对两个方向端点组合的哈希值求和，保证 v1-v2 与 v2-v1 的哈希一致
    /// </summary>
    public override int GetHashCode()
    {
        return new { v1, v2 }.GetHashCode() + new { v2, v1 }.GetHashCode();
    }

    /// <summary>
    /// == 运算符重载：等价于 Equals（无方向性的端点比较）
    /// </summary>
    public static bool operator ==(EdgeConstraint lhs, EdgeConstraint rhs)
    {
        return lhs.Equals(rhs);
    }

    /// <summary>
    /// != 运算符重载：等价于 Equals 结果的取反
    /// </summary>
    public static bool operator !=(EdgeConstraint lhs, EdgeConstraint rhs)
    {
        return !lhs.Equals(rhs);
    }

    /// <summary>
    /// 返回边约束的字符串描述，便于调试
    /// </summary>
    [ExcludeFromCoverage]
    public override string ToString()
    {
        return $"Edge: T{t1}->T{t2} (V{v1}->V{v2})";
    }
}
