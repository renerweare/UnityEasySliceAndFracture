/// <summary>
/// 定义了「可被按箱子编号排序」的对象接口。
/// 凡是实现了本接口的类/结构体，都会拥有一个名为 bin 的整数属性，
/// 用于记录该对象属于哪一个"箱子"（bin），随后即可被 BinSort 类中的
/// 计数排序方法按 bin 的编号进行稳定排序。
/// 在切割/破碎系统中，网格顶点会被划分到一个个网格单元（箱子）中，
/// 本接口正是为这种按单元编号排序的场景服务的。
/// </summary>
public interface IBinSortable
{
    /// <summary>
    /// 该对象所属的箱子（bin）编号，用于排序时的关键字。
    /// </summary>
    int bin { get; set; }
}

/// <summary>
/// 提供在有序网格上按箱子编号对对象进行排序的方法。
/// 
/// 网格的编号顺序如下面示意图所示（蛇形/弓字形排布）：
/// 偶数行（第 0 行为最底下一行）从左到右依次编号，
/// 而奇数行则从右到左依次编号（即反向排布）。
/// 这种蛇形排布的好处是：相邻编号的箱子在空间上也相邻，
/// 有利于提高空间局部性。
/// 
/// 示意图（3 行 x 3 列，共 9 个箱子的编号排布）：
///  _____ _____ _____
/// |     |     |     |
/// |  6  |  7  |  8  |    ← 第 2 行（偶数行，从左到右）
/// |_____|_____|_____|
/// |     |     |     |
/// |  5  |  4  |  3  |    ← 第 1 行（奇数行，从右到左）
/// |_____|_____|_____|
/// |     |     |     |
/// |  0  |  1  |  2  |    ← 第 0 行（偶数行，从左到右）
/// |_____|_____|_____|
/// 
/// </summary>
public class BinSort
{
    /// <summary>
    /// 根据给定的网格行列坐标计算对应的箱子编号。
    /// </summary>
    /// <remarks>
    /// 编号规则：
    /// - 偶数行（i 为偶数）：箱子编号 = i * n + j，即从左到右依次递增；
    /// - 奇数行（i 为奇数）：箱子编号 = (i + 1) * n - j - 1，即从右到左依次递增，
    ///   其中 (i + 1) * n - 1 是本行最右侧箱子的编号，减去 j 后即得到从左数第 j 列的编号。
    /// 这样保证了相邻编号的箱子在蛇形网格中空间相邻。
    /// </remarks>
    /// <param name="i">网格行号（从 0 开始，0 为最底行）</param>
    /// <param name="j">网格列号（从 0 开始）</param>
    /// <param name="n">网格大小（每行/每列箱子的数量，即 n x n 网格）</param>
    /// <returns>该网格坐标对应的箱子编号</returns>
    internal static int GetBinNumber(int i, int j, int n)
    {
        // 偶数行从左到右编号；奇数行从右到左编号（蛇形排布）。
        return (i % 2 == 0) ? (i * n) + j : (i + 1) * n - j - 1;
    }

    /// <summary>
    /// 对输入数组基于其 bin 编号执行计数排序。只对索引范围 [0, count) 内的元素进行排序。
    /// 如果 binCount（箱子数量）小于等于 1，则不进行任何排序。
    /// 如果 lastIndex 大于 input.Length，则对整个输入数组进行排序。
    /// </summary>
    /// <remarks>
    /// 为什么使用计数排序而非普通比较排序？
    /// 因为这里的"关键字"（bin 编号）是取值有限的非负整数（0 ~ binCount-1），
    /// 计数排序的时间复杂度仅为 O(n + binCount)，是线性时间，
    /// 比基于比较的排序（O(n log n)）更快，特别适合大量顶点按箱子分组的场景。
    /// 注意：该排序是稳定的（相同 bin 编号的元素保持原有相对顺序）。
    /// </remarks>
    /// <param name="input">待排序的输入数组</param>
    /// <param name="lastIndex">需要排序的最后一个元素的下标。实际只会对 [0, lastIndex) 范围内的
    /// 元素排序，超出该范围（即数组尾部）的元素保持原样，直接被拷贝到输出数组尾部。</param>
    /// <param name="binCount">箱子（bin）的总数量，即编号的取值范围 [0, binCount)</param>
    /// <returns>排序后的数组（若 binCount &lt;= 1 则直接返回原始输入数组）</returns>
    internal static T[] Sort<T>(T[] input, int lastIndex, int binCount) where T: IBinSortable
    {
        // count[j]：统计编号为 j 的箱子中元素个数的计数数组
        int[] count = new int[binCount];
        // output：存放排序结果的输出数组，长度与输入数组一致
        T[] output = new T[input.Length];

        #region Validation 参数校验区
        // 至少需要两个箱子才有排序的必要；若箱子数量 <= 1，
        // 所有元素都属于同一个箱子，顺序无意义，直接返回原数组。
        if (binCount <= 1)
        {
            return input;
        }

        // 如果 lastIndex 超出数组范围，则退化为对整个数组排序
        // （把 lastIndex 截断到 input.Length）。
        if (lastIndex > input.Length)
        {
            lastIndex = input.Length;
        }
        #endregion

        // 第一步：统计每个箱子中的元素个数。
        // 只统计前 [0, lastIndex) 范围内的点，不希望排到数组尾部
        // 那些不在排序范围内的元素（例如超级三角形（super-triangle）的顶点，
        // 它们在三角剖分中用于兜底，不能被移动到别处）。
        for (int i = 0; i < lastIndex; i++)
        {
            int j = input[i].bin;   // 取出元素所属的箱子编号
            count[j] += 1;          // 该箱子的计数加一
        }

        // 第二步：将计数数组转换为前缀和（累加）。
        // 执行完后，count[j] 表示"编号 <= j 的箱子中元素总数"，
        // 由此可以推算出每个箱子中元素在输出数组中的起始下标区间。
        for (int i = 1; i < binCount; i++)
        {
            count[i] += count[i - 1];
        }

        // 第三步：从后往前遍历待排序元素，根据前缀和把每个元素放到输出数组的正确位置。
        // 这里必须从后往前（倒序）遍历，因为 count[j] 此时记录的是"尚未放置的元素中，
        // 编号 <= j 的个数"，每放置一个元素就将 count[j] 减一。
        // 倒序遍历可以保证排序的稳定性：相同 bin 编号的元素保持原来的相对顺序。
        for (int i = lastIndex - 1; i >= 0; i--)
        {
            int j = input[i].bin;
            count[j] -= 1;                       // 为当前元素预留一个位置
            output[count[j]] = input[i];         // 将元素放到计算出的位置上
        }

        // 第四步：把数组尾部未参与排序的元素原样拷贝到输出数组中。
        // 这些元素不在排序范围 [0, lastIndex) 内（例如超级三角形顶点），
        // 必须保持原有位置不变。
        for (int i = lastIndex; i < output.Length; i++)
        {
            output[i] = input[i];
        }

        return output;
    }

}