using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 回调选项配置类（序列化到 Inspector，由 SliceAndFracture.callbackOptions 持有）。
/// 对外暴露两个 UnityEvent，供其他脚本/动画事件在拆解发生时挂接监听：
///  - onFracture：物体"将要破碎"的瞬间回调（带引发者、被碎物体、破碎点三个参数）；
///  - onCompleted：切割/破碎过程"完成"后的回调（无参数）。
/// 使用方式：在 Inspector 中把脚本方法拖拽到对应事件上，或代码里 callbackOptions.onFracture.AddListener(...)。
/// </summary>
[Serializable]
public class CallbackOptions
{
    /// <summary>
    /// "破碎瞬间"回调：参数 (Collider 引发者, GameObject 被碎物体, Vector3 破碎位置)。
    /// 仅由触发破碎（碰撞/触发器/键盘/CauseFracture）时调用；
    /// 切割（ComputeSlice）与"预破碎"阶段不会触发该事件。
    /// 常用于：播放破碎音效、生成粒子、加分计数等。
    /// 注意：引发者可能为 null（键盘触发、CauseFracture 直接调用时）。
    /// </summary>
    [Tooltip("当触发破碎时调用此回调。切割和预破碎不会调用。")]
    public UnityEvent<Collider, GameObject, Vector3> onFracture;

    /// <summary>
    /// "拆解完成"回调（无参数）：切割/破碎流程全部结束、碎片已生成、原物体已隐藏时调用。
    /// 注意触发时机差异：
    ///  - 切割：每次 ComputeSlice 完成都会触发；
    ///  - 破碎：第一次触发；二次破碎时受 RefractureOptions.invokeCallbacks 控制（见 FireCompletedCallback）。
    /// 常用于：隐藏 UI 提示、解锁后续流程、刷新任务状态等。
    /// </summary>
    [Tooltip("当破碎/切割过程完成时调用此回调。")]
    public UnityEvent onCompleted;

    /// <summary>
    /// 构造函数：onFracture 无需初始化（UnityEvent 字段由 Unity 序列化器自动创建实例），
    /// 仅需保证 onCompleted 有默认值即可。
    /// </summary>
    public CallbackOptions()
    {
        this.onCompleted = null;
    }

    /// <summary>
    /// 派发"破碎瞬间"回调（onFracture）。
    /// 由 SliceAndFracture 的 OnCollisionEnter / OnTriggerEnter / Update / CauseFracture 调用。
    /// 内部使用 null 条件调用（?.），未绑定监听时安全跳过，不会抛异常。
    /// </summary>
    /// <param name="instigator">引发破碎的碰撞体（键盘触发/直接调用时为 null）</param>
    /// <param name="fracturedObject">正在破碎的物体（本 GameObject）</param>
    /// <param name="point">破碎发生的位置（碰撞触发取接触点，其他取物体位置）</param>
    public void CallOnFracture(Collider instigator, GameObject fracturedObject, Vector3 point)
    {
        onFracture?.Invoke(instigator, fracturedObject, point);
    }
}
