using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 二次破碎选项配置类（序列化到 Inspector，由 SliceAndFracture.refractureOptions 持有）。
/// 控制"碎片再次破碎（Refracture）"的相关行为：
///  - 是否允许碎片继续破碎（enableRefracturing）与最大二次破碎次数（maxRefractureCount）；
///  - 二次破碎时是否也触发完成回调（invokeCallbacks）。
/// 注意：二次破碎会让碎片数量呈指数级增长，务必保持较低的 fragmentCount 并控制 maxRefractureCount。
/// </summary>
[Serializable]
/// <summary>
/// 二次破碎的选项
/// </summary>
public class RefractureOptions
{
    /// <summary>
    /// 是否允许碎片被再次破碎（二次破碎），默认 false。
    /// 为 true 时，每次破碎产生的碎片仍保留破碎能力（受 maxRefractureCount 限制），
    /// 可以反复砸碎直至达到次数上限，适合"先碎成大块、再碎成小块"的分层破坏。
    /// 警告：每层碎片数会成倍增长，启用时应同时调低 FractureOptions.fragmentCount。
    /// </summary>
    [Tooltip("允许对碎片进行二次破碎。警告：此设置会产生大量碎片，启用时建议保持较低的碎片数量。")]
    public bool enableRefracturing;

    /// <summary>
    /// 碎片可被再次破碎的最大次数（1 ~ 3，默认 1）。
    /// 与 SliceOptions.maxResliceCount 不同，此值范围刻意限制在 1 ~ 3，
    /// 防止碎片数量以 2 的幂次快速爆炸（3 层即可生成 8 倍以上的碎片量）。
    /// </summary>
    [Tooltip("碎片可被再次破碎的最大次数。")]
    [Range(1, 3)]
    public int maxRefractureCount;

    /// <summary>
    /// 二次破碎（碎片再破碎）时是否也触发 onCompleted 完成回调，默认 false。
    /// 为 false 时只有第一次破碎触发回调，后续每层碎片破碎不再触发；
    /// 为 true 时每次破碎完成都会触发（见 FireCompletedCallback 的判断逻辑）。
    /// </summary>
    [Tooltip("如果二次破碎时也要调用回调函数，请启用。")]
    public bool invokeCallbacks;

    /// <summary>
    /// 构造函数：提供一组安全的默认值，保证未在 Inspector 赋值时也有合理行为。
    /// </summary>
    public RefractureOptions()
    {
        this.enableRefracturing = false;
        this.maxRefractureCount = 1;
        this.invokeCallbacks = false;
    }
}
