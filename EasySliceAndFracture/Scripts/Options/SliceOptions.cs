using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 切割选项配置类（序列化到 Inspector，由 SliceAndFracture.sliceOptions 持有）。
/// 控制"切割（Slice）"相关的全部行为：
///  - 碎片是否还能被再次切割（二次切割，enableReslicing / maxResliceCount）；
///  - 切割非凸网格时的漂浮碎片检测（detectFloatingFragments）；
///  - 新切割截面的材质（insideMaterial）与 UV 变换（textureScale / textureOffset）；
///  - 二次切割时是否也触发完成回调（invokeCallbacks）。
/// 建议在 Inspector 中逐项调整；大多数场景保持默认值即可。
/// </summary>
[Serializable]
public class SliceOptions
{
    [Tooltip("允许对碎片进行二次切割。")]
    /// <summary>
    /// 是否允许碎片被再次切割（二次切割）。
    /// 为 false（默认）时，切片只有一次生命：切出两半后就不能再切，避免碎片无限细分；
    /// 为 true 时，新切片会保留切割能力，直到切割次数达到 maxResliceCount。
    /// 建议：需要"切了再切"（如切水果）时开启。
    /// </summary>
    public bool enableReslicing;

    [Tooltip("碎片可被再次切割的最大次数。")]
    /// <summary>
    /// 碎片可被再次切割的最大次数（1 ~ 100，默认 1）。
    /// 只有当 enableReslicing 为 true 时生效：currentSliceCount 达到该值后，
    /// ComputeSlice 会拒绝再次切割。值越大，一片能切出的最终切片数越多（指数级增长）。
    /// </summary>
    [Range(1, 100)]
    public int maxResliceCount;

    [Tooltip("切割非凸网格时启用对“漂浮”碎片的检测。该设置对凸网格无效，应保持禁用。")]
    /// <summary>
    /// 切割非凸网格时，是否检测"漂浮"（不与主体相连）的碎片。
    /// 对凸网格无效，应保持禁用（默认 false）；
    /// 仅在网格非凸（如带凹陷的模型）且出现切割后碎块漂浮、边界错位等问题时尝试开启。
    /// </summary>
    public bool detectFloatingFragments;

    [Tooltip("用于内部面的材质")]
    /// <summary>
    /// 切割后新产生的"截面"材质（物体内部露出来的面）。
    /// 建议使用纯色或不透明材质；若留空（null），截面会使用默认材质显示。
    /// 常见做法：给截面一个与表面同色的深色材质，模拟果肉/内部结构。
    /// </summary>
    public Material insideMaterial;

    [Tooltip("应用于纹理坐标的缩放系数")]
    /// <summary>
    /// 应用于纹理坐标（UV）的缩放系数，默认 (1, 1) 即不缩放。
    /// 改变该值会让内部面材质上的贴图平铺密度发生变化（类似 Material 的 Tiling）。
    /// </summary>
    public Vector2 textureScale;

    [Tooltip("应用于纹理坐标的偏移量")]
    /// <summary>
    /// 应用于纹理坐标（UV）的偏移量，默认 (0, 0) 即不偏移。
    /// 改变该值会让内部面材质上的贴图平移（类似 Material 的 Offset）。
    /// </summary>
    public Vector2 textureOffset;

    [Tooltip("如果二次切割时也要调用回调函数，请启用。")]
    /// <summary>
    /// 二次切割（碎片再切割）时是否也触发 onCompleted 完成回调。
    /// 为 false（默认）时，只有第一次切割触发回调，后续每次碎片切割不再触发，
    /// 避免回调被高频刷屏；为 true 时每次切割完成都会触发。
    /// </summary>
    public bool invokeCallbacks;

    /// <summary>
    /// 构造函数：提供一组安全的默认值，保证未在 Inspector 赋值时也有合理行为。
    /// </summary>
    public SliceOptions()
    {
        this.enableReslicing = false;
        this.maxResliceCount = 1;
        this.insideMaterial = null;
        this.textureScale = Vector2.one;
        this.textureOffset = Vector2.zero;
        this.invokeCallbacks = false;
    }
}
