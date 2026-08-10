using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 数学工具静态类。
/// 集中提供切割/破碎算法中常用的二维与三维几何计算，包括：
/// 四边形凸性判断、线段相交判断、线段与平面求交、以及点相对有向线段的位置判断等。
/// 所有方法均为纯函数式静态方法，不持有任何状态，可在算法中被反复调用。
/// </summary>
public static class MathUtils
{
    /// <summary>
    /// 判断由两条对角线 a1->a2 和 b1->b2 构成的四边形是否为凸四边形。
    /// </summary>
    /// <remarks>
    /// 原理：一个简单四边形是凸四边形，当且仅当它的两条对角线彼此相交。
    /// 因此本方法直接复用线段相交判定 LinesIntersectInternal，
    /// 并传入 includeSharedEndpoints = true，表示当两条对角线共享端点时
    /// （四边形退化成三角形）也视为凸。
    /// </remarks>
    /// <param name="a1">对角线 A 的起点</param>
    /// <param name="a2">对角线 A 的终点</param>
    /// <param name="b1">对角线 B 的起点</param>
    /// <param name="b2">对角线 B 的终点</param>
    /// <returns>若四边形为凸（或退化成一个三角形）则返回 true，否则返回 false</returns>
    public static bool IsQuadConvex(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2)
    {
        return LinesIntersectInternal(a1, a2, b1, b2, true);
    }

    /// <summary>
    /// 判断线段 a1->a2 与线段 b1->b2 是否相交（不含仅共享端点的情况）。
    /// </summary>
    /// <remarks>
    /// 这是对内部实现 LinesIntersectInternal 的公开封装，
    /// includeSharedEndpoints 固定为 false，即当两条线段仅在一个端点处
    /// 相接（共享端点）时不视为相交。若需要把共享端点也算作相交，
    /// 请使用 IsQuadConvex（它内部传入了 true）。
    /// </remarks>
    /// <param name="a1">线段 A 的起点</param>
    /// <param name="a2">线段 A 的终点</param>
    /// <param name="b1">线段 B 的起点</param>
    /// <param name="b2">线段 B 的终点</param>
    /// <returns>两条线段（非仅端点相接）相交时返回 true，否则返回 false</returns>
    public static bool LinesIntersect(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2)
    {
        return LinesIntersectInternal(a1, a2, b1, b2, false);
    }

    /// <summary>
    /// 线段相交判定的内部实现。
    /// </summary>
    /// <remarks>
    /// 算法原理（基于叉积符号）：
    /// 1. 计算两条线段的方向向量 a12 = a2 - a1、b12 = b2 - b1；
    /// 2. 先处理特例：如果两条线段的端点存在重合（共享顶点），则四边形退化成三角形，
    ///    此时是否"相交"由调用方通过 includeSharedEndpoints 决定；
    /// 3. 否则分别计算线段 A 的两个端点相对线段 B 所在直线的叉积（a1xb、a2xb），
    ///    以及线段 B 的两个端点相对线段 A 所在直线的叉积（b1xa、b2xa）。
    ///    叉积的正负号表示点位于直线的哪一侧；
    /// 4. 两条线段相交的条件是：A 的两个端点分居在 B 所在直线的两侧
    ///    （或至少有一个端点恰好落在 B 上），同时 B 的两个端点分居在 A 所在直线的两侧。
    ///    由于是线段（而非无限直线）相交，两个条件必须同时成立。
    /// 5. 使用 &gt;= / &lt;= 而非严格大于/小于，是为了把"端点恰好落在对方线段上"
    ///    的退化情况（四边形坍缩成三角形）也判定为相交。
    /// </remarks>
    /// <param name="a1">线段 A 的起点</param>
    /// <param name="a2">线段 A 的终点</param>
    /// <param name="b1">线段 B 的起点</param>
    /// <param name="b2">线段 B 的终点</param>
    /// <param name="includeSharedEndpoints">为 true 时，若两条线段共享端点（四边形退化为三角形）也视为相交</param>
    /// <returns>两条线段相交（或按 includeSharedEndpoints 规则视为相交）时返回 true</returns>
    private static bool LinesIntersectInternal(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2, bool includeSharedEndpoints)
    {
        // 分别计算两条线段的方向向量（从起点指向终点）
        Vector2 a12 = new Vector2(a2.x - a1.x, a2.y - a1.y);
        Vector2 b12 = new Vector2(b2.x - b1.x, b2.y - b1.y);
        
        // 特例：如果任意两个端点重合（两条线段共享顶点），
        // 四边形会坍缩成一个三角形，此时按约定视为凸/相交。
        if (a1 == b1 || a1 == b2 || a2 == b1 || a2 == b2)
        {
            return includeSharedEndpoints;
        }
        else
        {
            // 计算每个端点相对"另一条线段所在直线"的叉积。
            // 叉积（二维 z 分量）的符号可以判断点在直线的哪一侧：
            // 例如 a1xb = b12 x (a1 - b1)，即向量 (a1 - b1) 与方向向量 b12 的叉积。
            float a1xb = (a1.x - b1.x) * b12.y - (a1.y - b1.y) * b12.x;
            float a2xb = (a2.x - b1.x) * b12.y - (a2.y - b1.y) * b12.x;
            float b1xa = (b1.x - a1.x) * a12.y - (b1.y - a1.y) * a12.x;
            float b2xa = (b2.x - a1.x) * a12.y - (b2.y - a1.y) * a12.x;

            // 检查每条线段的两个端点是否分居在另一条线段所在直线的两侧：
            // - a1xb 与 a2xb 符号相反（或其一为零）说明 A 的两个端点分别位于 B 的左右两侧；
            // - b1xa 与 b2xa 符号相反（或其一为零）说明 B 的两个端点分别位于 A 的左右两侧。
            // 两个条件同时成立时两线段必然相交。
            // 使用 >= 与 <= 而非 > 与 <，是因为当某个端点恰好落在对方线段上时
            // 叉积为 0，此时形状退化成一个三角形，仍应判定为相交。
            return ((a1xb >= 0 && a2xb <= 0) || (a1xb <= 0 && a2xb >= 0)) &&
                   ((b1xa >= 0 && b2xa <= 0) || (b1xa <= 0 && b2xa >= 0));
        }
    }

    /// <summary>
    /// 计算线段 a->b 与平面（由法线 n 和原点 p0 定义）的交点。
    /// 若存在交点，将通过 out 参数 intersection 返回；参数 s 的定义见下方说明，
    /// 它用于正确插值出交点处的法线与 UV。
    /// </summary>
    /// <remarks>
    /// 参数化求交原理：
    /// 把线段上的任意一点表示为 x = a + (b - a) * s，其中 s 取值于 [0, 1]。
    /// 该点落在平面上的条件是 (x - p0) · n = 0，代入 x 得：
    ///   (a + (b - a) * s - p0) · n = 0
    ///   =>  (p0 - a) · n = s * (b - a) · n
    ///   =>  s = (p0 - a) · n / (b - a) · n
    /// 当 0 &lt;= s &lt;= 1 时，交点位于线段范围内（而非其延长线上），求交成功。
    /// 得到 s 之后，交点坐标 = a + (b - a) * s；同时 s 也可以作为权重，
    /// 在 a、b 两端的法线/UV 之间做线性插值，从而得到交点的法线与 UV。
    /// </remarks>
    /// <param name="a">线段的起点</param>
    /// <param name="b">线段的终点</param>
    /// <param name="n">平面法线</param>
    /// <param name="p0">平面上的原点（平面所经过的点）</param>
    /// <param name="x">存在交点时，通过该 out 参数返回交点坐标</param>
    /// <param name="s">返回交点的参数化位置，满足 x = a + (b - a) * s</param>
    /// <returns>线段与平面存在交点时返回 true，否则返回 false</returns>
    public static bool LinePlaneIntersection(Vector3 a,
                                             Vector3 b,
                                             Vector3 n,
                                             Vector3 p0,
                                             out Vector3 x,
                                             out float s)
    {
        // 初始化 out 参数，避免调用方拿到未赋值的变量
        s = 0;
        x = Vector3.zero;

        // 处理退化情况：
        // 1) 线段退化为一个点（a == b），无法定义方向，不可能与平面相交；
        // 2) 平面法线为零向量，平面本身未定义，求交无意义。
        if (a == b)
        {
            return false;
        }
        else if (n == Vector3.zero)
        {
            return false;
        }

        // `s` 是线段 a -> b 的参数，取值范围 0.0 <= s <= 1.0。
        // 推导：交点在平面上的条件 (x - p0) · n = 0，将 x = a + (b - a) * s 代入，
        // 解得 s = (p0 - a) · n / (b - a) · n。
        s = Vector3.Dot(p0 - a, n) / Vector3.Dot(b - a, n);

        // 只有当 s 落在 [0, 1] 区间内，交点才真正位于线段上（而非其延长线上）
        if (s >= 0 && s <= 1)
        {
            // 按参数 s 线性插值得到交点坐标
            x = a + (b - a) * s;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 判断点 c 是否位于有向线段 i -> j 的右侧。
    /// </summary>
    /// <remarks>
    /// 用途：判断点是否在三角形内部。由于三角形顶点按逆时针（CCW）方向排列，
    /// 一个点位于三角形某条边的左侧，就意味着该点处于三角形这条边所围成的内部一侧。
    /// 因此配合每条边逐一判断，即可确定点是否在三角形内。
    /// 注意：方法名中的 i、j 语义取自原版实现（对应 points 数组中的顶点下标），
    /// 实际参数为二维坐标 a（边起点）、b（边终点）、c（被检测的点）。
    /// </remarks>
    /// <param name="a">有向线段的起点</param>
    /// <param name="b">有向线段的终点</param>
    /// <param name="c">被检测的点</param>
    /// <returns>点 c 位于有向线段 a->b 的右侧时返回 true</returns>
    public static bool IsPointOnRightSideOfLine(Vector2 a, Vector2 b, Vector2 c)
    {
        // 计算向量 (b - a) 与 (c - a) 的二维叉积（z 分量）：
        //   (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)
        // 叉积为负表示 c 在 a->b 的右侧；为零表示 c 恰好落在直线 a->b 上。
        // 使用 <=（而非 <）至关重要：若写成 <，恰好落在边上的点会被误判为"不在右侧"，
        // 导致位于边上的点无法被正确归类，整套三角剖分逻辑将崩溃。
        return ((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) <= 0;
    }

}
