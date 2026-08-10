using System;
using UnityEngine;

/// <summary>
/// 破碎选项配置类（序列化到 Inspector，由 SliceAndFracture.fractureOptions 持有）。
/// 控制"破碎（Fracture）"相关的全部行为：
///  - 破碎的碎片数量（fragmentCount）与拆解方向（xAxis / yAxis / zAxis）；
///  - 破碎非凸网格时的漂浮碎片检测（detectFloatingFragments）；
///  - 是否异步破碎以缓解卡顿（asynchronous）；
///  - 新破碎截面的材质（insideMaterial）与 UV 变换（textureScale / textureOffset）。
/// 建议在 Inspector 中逐项调整；大多数场景保持默认值即可。
/// </summary>
[Serializable]
/// <summary>
/// 破碎网格的选项
/// </summary>
public class FractureOptions
{
    /// <summary>
    /// 物体（及其子物体）被递归破碎产生的碎片总数（1 ~ 1024，默认 10）。
    /// 值越大碎片越碎、越接近真实碎裂效果，但网格计算量成倍增长、性能开销越大。
    /// 建议：简单演示 5 ~ 10；追求碎裂感 20 ~ 50；高配机可尝试更高，注意异步破碎。
    /// </summary>
    [Range(1, 1024)]
    [Tooltip("物体及其子物体被递归破碎的最大次数。碎片数量越多，计算时间越长。")]
    public int fragmentCount;

    /// <summary>
    /// 是否启用局部 X 轴方向的拆解平面（默认开启）。
    /// 破碎时网格会沿着每个开启的轴反复二分，三个轴全开（默认）效果最均匀；
    /// 只开个别轴会得到"板状/棒状"的碎片，适合特定造型需求。
    /// </summary>
    [Tooltip("启用在局部 X 平面上的破碎")]
    public bool xAxis;

    /// <summary>
    /// 是否启用局部 Y 轴方向的拆解平面（默认开启）。说明同 xAxis。
    /// </summary>
    [Tooltip("启用在局部 Y 平面上的破碎")]
    public bool yAxis;

    /// <summary>
    /// 是否启用局部 Z 轴方向的拆解平面（默认开启）。说明同 xAxis。
    /// </summary>
    [Tooltip("启用在局部 Z 平面上的破碎")]
    public bool zAxis;

    /// <summary>
    /// 破碎非凸网格时，是否检测"漂浮"（不与主体相连）的碎片。
    /// 对凸网格无效，应保持禁用（默认 false）；
    /// 仅在网格非凸且出现破碎后碎块漂浮、边界错位等问题时尝试开启。
    /// </summary>
    [Tooltip("破碎非凸网格时启用对“漂浮”碎片的检测。该设置对凸网格无效，应保持禁用。")]
    public bool detectFloatingFragments;

    /// <summary>
    /// 是否在主线程上异步（协程跨帧）执行破碎（默认 false = 同步）。
    /// 同步破碎：一次调用内完成全部计算，简单直接，但碎片多时会明显卡顿；
    /// 异步破碎：通过协程逐帧切分，主线程不长时间阻塞，适合碎片数量大的场景。
    /// 注意：异步路径下原物体要等全部碎片算完才被隐藏（见 ComputeFracture）。
    /// </summary>
    [Tooltip("在主线程上异步执行破碎。")]
    public bool asynchronous;

    /// <summary>
    /// 破碎后新产生的"截面"材质（物体内部露出来的面）。
    /// 建议使用纯色或不透明材质；若留空（null），截面会使用默认材质显示。
    /// </summary>
    [Tooltip("用于内部面的材质")]
    public Material insideMaterial;

    /// <summary>
    /// 应用于纹理坐标（UV）的缩放系数，默认 (1, 1) 即不缩放（类似 Tiling）。
    /// </summary>
    [Tooltip("应用于纹理坐标的缩放系数")]
    public Vector2 textureScale;

    /// <summary>
    /// 应用于纹理坐标（UV）的偏移量，默认 (0, 0) 即不偏移（类似 Offset）。
    /// </summary>
    [Tooltip("应用于纹理坐标的偏移量")]
    public Vector2 textureOffset;

    /// <summary>
    /// 构造函数：提供一组安全的默认值，保证未在 Inspector 赋值时也有合理行为。
    /// </summary>
    public FractureOptions()
    {
        this.fragmentCount = 10;
        this.xAxis = true;
        this.yAxis = true;
        this.zAxis = true;
        this.detectFloatingFragments = false;
        this.asynchronous = false;
        this.insideMaterial = null;
        this.textureScale = Vector2.one;
        this.textureOffset = Vector2.zero;
    }
}
