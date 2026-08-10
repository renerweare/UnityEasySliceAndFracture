using System;
using UnityEngine;

/// <summary>
/// Vector3 扩展方法类。
/// 为 UnityEngine.Vector3 提供一批便捷的几何判断工具方法。
/// 本类为静态类，所有方法均为 C# 扩展方法，因此可以直接以「向量.方法名(...)」的形式调用，
/// 例如 `point.IsAbovePlane(normal, origin)`，使用起来与 Vector3 自带方法无异。
/// 本插件（切割/破碎系统）在判断顶点位于切割平面哪一侧时大量使用该类中的方法。
/// </summary>
public static class Vector3Extensions
{
    // 点相对平面位置判断的核心数学说明：
    //   - p: 被检测的点（要判断方位的那个点）
    //   - n: 平面的法线（法线所指向的一侧即被视为平面的"上方"）
    //   - o: 平面上的原点（平面所经过的参考点，用于和 p 一起构造方向向量）
    // 利用点积 n · (p - o) 的符号即可判断 p 位于平面的哪一侧。
    /// <summary>
    /// 如果点 p 位于平面上或平面"上方"，则返回 true。所谓"上方"指的是平面法线所指向的那一侧。
    /// </summary>
    /// <remarks>
    /// 原理详解：
    /// 1. 构造向量 (p - o)，即从平面原点 o 指向检测点 p 的方向向量；
    /// 2. 计算该向量与平面法线 n 的点积，点积的符号反映了 p 相对平面的方位：
    ///    - 点积 &gt; 0：p 在法线指向的一侧（"上方"）；
    ///    - 点积 &lt; 0：p 在法线反方向的一侧（"下方"）；
    ///    - 点积 = 0：p 恰好落在平面上。
    /// 3. 因此只要判断点积是否 &gt;= 0，即可确定点是否位于平面上方（或刚好在平面上）。
    ///    这里使用 &gt;= 而不是 &gt;，是为了把"恰好落在平面上"的点也视为上方，避免切割边界上的点被遗漏。
    /// </remarks>
    /// <param name="p">被检测的点（扩展方法调用者）</param>
    /// <param name="n">平面法线，指示平面"上方"的方向，建议使用单位向量</param>
    /// <param name="o">平面原点，即平面所经过的参考点</param>
    /// <returns>点位于平面上或平面法线所指向的一侧时返回 true，否则返回 false</returns>
    public static bool IsAbovePlane(this Vector3 p, Vector3 n, Vector3 o)
    {
        // 将点积 n · (p - o) 展开为逐分量的乘法与加法：
        //   n.x * (p.x - o.x) + n.y * (p.y - o.y) + n.z * (p.z - o.z)
        // 结果 >= 0 表示点位于平面上或平面法线一侧（上方）。
        return (n.x * (p.x - o.x) + n.y * (p.y - o.y) + n.z * (p.z - o.z)) >= 0;
    }
}