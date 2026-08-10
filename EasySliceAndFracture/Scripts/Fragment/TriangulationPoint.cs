using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// 三角剖分点（TriangulationPoint）：表示三角剖分过程中的一个点。
/// 三角剖分在二维平面上进行，因此三维顶点会被投影成二维坐标（coords）后放入此结构。
/// 实现 IBinSortable 接口，可被 BinSort 按"网格分箱编号"（bin）排序，
/// 从而加速逐点插入（incremental）三角剖分算法中"查找包含目标点的三角形"这一步。
/// </summary>
public class TriangulationPoint: IBinSortable
{
    /// <summary>
    /// 该点在三角剖分平面上的二维坐标（由三维位置投影得到，之后会被归一化到 [0,1] 区间）
    /// </summary>
    public Vector2 coords;

    /// <summary>
    /// 分箱编号：把归一化后的坐标划分为 n×n 的网格，记录该点所在的格子编号，
    /// 用于按空间邻近性排序点集
    /// </summary>
    public int bin { get; set; }

    /// <summary>
    /// 该点在原始输入点列表中的索引（排序后依然可以找回原始编号）
    /// </summary>
    public int index = 0;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="index">该点在原始点列表中的索引</param>
    /// <param name="coords">该点在三角剖分平面上的二维坐标</param>
    public TriangulationPoint(int index, Vector2 coords)
    {
        this.index = index;
        this.coords = coords;
    }

    /// <summary>
    /// 返回点的字符串描述（坐标与分箱编号），便于调试
    /// </summary>
    [ExcludeFromCoverage]
    public override string ToString()
    {
        return $"{coords} -> {bin}";
    }
}