using UnityEngine.TestTools;

/// <summary>
/// 四边形（Quad）数据结构：存放三角剖分中由两个共享边的三角形拼成的"四边形"及其邻接信息。
/// 两个共享一条边（q1-q2）的三角形 t1 与 t2 构成一个四边形（q1->q2->q3->q4），
/// 当需要交换该四边形的对角线（SwapQuadDiagonal）时，需要用到这里记录的
/// 四个顶点以及四个相邻三角形（t1L/t1R/t2L/t2R）的索引来同步更新三角剖分的邻接关系。
/// 注意：q1~q4 不保证按任何顺序（顺时针/逆时针）排列，它们只是从两个三角形中提取的四个顶点编号。
/// </summary>
public struct Quad
{
    //               q3        
    //      *---------*---------*
    //       \       / \       /
    //        \ t2L /   \ t2R /
    //         \   /     \   /
    //          \ /   t2  \ /
    //        q1 *---------* q2 
    //          / \   t1  / \    
    //         /   \     /   \     
    //        / t1L \   / t1R \   
    //       /       \ /       \  
    //      *---------*---------*
    //               q4           
    // 示意图：t1 与 t2 共享边 q1-q2；q4 是 t1 中共享边对面的顶点，
    // q3 是 t2 中共享边对面的顶点；t1L/t1R 是与 t1 另外两条边相邻的三角形，
    // t2L/t2R 是与 t2 另外两条边相邻的三角形。

    /// <summary>
    /// 四边形四个顶点在三角剖分点数组中的索引。
    /// q1 与 q2 是共享边（t1 与 t2 的公共边）的两个端点，
    /// q4 是 t1 中共享边对面的顶点，q3 是 t2 中共享边对面的顶点。
    /// </summary>
    public int q1, q2, q3, q4;

    /// <summary>
    /// 构成该四边形的两个三角形索引：t1 为基准三角形，t2 为与 t1 共享边 q1-q2 的相邻三角形
    /// </summary>
    public int t1, t2;

    /// <summary>
    /// 三角形邻接信息：
    /// t1L/t1R 为与 t1 的另外两条边相邻的三角形索引；
    /// t2L/t2R 为与 t2 的另外两条边相邻的三角形索引。
    /// 交换对角线后需要同步更新这些相邻三角形的邻接指针。
    /// </summary>
    public int t1L, t1R, t2L, t2R;

    /// <summary>
    /// 构造函数：由两个三角形及其邻接信息初始化四边形
    /// </summary>
    /// <param name="q1">四边形第一个顶点索引（共享边端点）</param>
    /// <param name="q2">四边形第二个顶点索引（共享边端点）</param>
    /// <param name="q3">四边形第三个顶点索引（t2 中共享边对面的顶点）</param>
    /// <param name="q4">四边形第四个顶点索引（t1 中共享边对面的顶点）</param>
    /// <param name="t1">基准三角形索引</param>
    /// <param name="t2">共享边上的相邻三角形索引</param>
    /// <param name="t1L">与 t1 左边相邻的三角形索引</param>
    /// <param name="t1R">与 t1 右边相邻的三角形索引</param>
    /// <param name="t2L">与 t2 左边相邻的三角形索引</param>
    /// <param name="t2R">与 t2 右边相邻的三角形索引</param>
    public Quad(int q1, int q2, int q3, int q4, int t1, int t2, int t1L, int t1R, int t2L, int t2R)
    {
        this.q1 = q1;
        this.q2 = q2;
        this.q3 = q3;
        this.q4 = q4;
        this.t1 = t1;
        this.t2 = t2;
        this.t1L = t1L;
        this.t1R = t1R;
        this.t2L = t2L;
        this.t2R = t2R;
    }

    /// <summary>
    /// 返回四边形的字符串描述（两个三角形与四个顶点），便于调试
    /// </summary>
    [ExcludeFromCoverage]
    public override string ToString()
    {
        return $"T{t1}/T{t2} (V{q1},V{q2},V{q3},V{q4})";
    }
}