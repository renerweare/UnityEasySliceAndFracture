using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 破碎触发方式枚举：定义物体通过哪种输入被"自动破碎"。
/// - 碰撞（0）：受到足够大的物理碰撞冲击时破碎（见 SliceAndFracture.OnCollisionEnter）；
/// - 触发器（1）：其他物体进入本物体的 Trigger 区域时破碎（见 OnTriggerEnter）；
/// - 键盘（2）：玩家按下指定按键时破碎（见 Update）。
/// 无论哪种方式，命中后都会先派发 onFracture 回调再调用 ComputeFracture()。
/// </summary>
public enum TriggerType 
{
    碰撞 = 0,
    触发器 = 1,
    键盘 = 2
}

/// <summary>
/// 破碎触发选项配置类（序列化到 Inspector，由 SliceAndFracture.triggerOptions 持有）。
/// 控制破碎的自动触发条件：用什么方式触发（triggerType）、力量阈值（minimumCollisionForce）、
/// 是否只允许特定标签的物体触发（filterCollisionsByTag / triggerAllowedTags）、触发按键（triggerKey）。
/// 注意：这些触发逻辑只在 mode 允许破碎（CanFracture）时生效。
/// </summary>
[Serializable]
public class TriggerOptions
{
    /// <summary>
    /// 破碎触发方式（碰撞 / 触发器 / 键盘），默认 碰撞。取值含义见 TriggerType 枚举说明。
    /// 建议：被球砸碎 → 碰撞；踩中地雷区域 → 触发器；调试/演示 → 键盘。
    /// </summary>
    [Tooltip("触发破碎的输入类型。")]
    public TriggerType triggerType;

    /// <summary>
    /// 触发破碎所需的最小碰撞力（默认 0，即任意碰撞都触发）。
    /// 实际碰撞力 = 冲量模长 ÷ 固定时间步长（见 OnCollisionEnter）；
    /// 只有碰撞力大于该值才破碎。设为较大值可模拟"只有重击才能打碎"。
    /// </summary>
    [Tooltip("导致物体破碎所需的最小碰撞力。")]
    public float minimumCollisionForce;

    /// <summary>
    /// 是否启用按标签过滤（默认 false）。
    /// 为 true 时，只有碰撞/触发对方的标签在 triggerAllowedTags 列表中的物体才会触发破碎，
    /// 其他物体撞上来会被忽略。
    /// </summary>
    [Tooltip("如果为真，只有标签在“允许标签”列表中的物体会触发碰撞。")]
    public bool filterCollisionsByTag;

    /// <summary>
    /// 允许触发破碎的标签白名单（当 filterCollisionsByTag 为 true 时生效）。
    /// 命中判断见 IsTagAllowed()：触发者的 GameObject.tag 存在于该列表即为放行。
    /// 注意：Unity 默认标签为 "Untagged"，若过滤请务必把目标物体的标签正确设置并加入此列表。
    /// </summary>
    [Tooltip("如果“按标签过滤碰撞”为真，只有标签在此列表中的物体会触发破碎。")]
    public List<string> triggerAllowedTags;

    /// <summary>
    /// 键盘触发模式下触发破碎的按键（默认 KeyCode.None）。
    /// 仅当 triggerType == 键盘 时生效，按下瞬间（GetKeyDown）触发一次破碎。
    /// </summary>
    [Tooltip("如果触发类型为键盘，这是按下后会触发破碎的按键。")]
    public KeyCode triggerKey;

    /// <summary>
    /// 构造函数：提供一组安全的默认值，保证未在 Inspector 赋值时也有合理行为。
    /// </summary>
    public TriggerOptions()
    {
        this.triggerType = TriggerType.碰撞;
        this.minimumCollisionForce = 0f;
        this.filterCollisionsByTag = false;
        this.triggerAllowedTags = new List<string>();
        this.triggerKey = KeyCode.None;
    }

    /// <summary>
    /// 判断指定标签是否允许触发破碎（是否在白名单 triggerAllowedTags 中）。
    /// 由 SliceAndFracture 的 OnCollisionEnter / OnTriggerEnter 调用，
    /// 用于 filterCollisionsByTag 为 true 时的标签过滤。
    /// </summary>
    /// <param name="tag">要检查的标签（通常是触发方 GameObject 的 tag）</param>
    /// <returns>true 表示该标签在允许列表中，允许触发破碎；false 表示禁止触发</returns>
    public bool IsTagAllowed(string tag)
    {
        return triggerAllowedTags.Contains(tag);
    }
}
