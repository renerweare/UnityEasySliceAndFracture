using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 刀刃切割器（BladeSlicer）：挂到"刀刃"物体（通常是 Plane/Quad 或任意扁平物体，cube也行）上，接触物体即可将其切割。
/// 核心机制：
/// - 接触检测：通过 Trigger 接触（OnTriggerEnter/OnTriggerStay/OnTriggerExit）记录并切割可切物体（所以得勾上 Is Trigger）。
///   （指带有 SliceAndFracture 组件、且 mode 不是"仅破碎"的物体）；
///   若碰撞体未勾选 Is Trigger，则退化为实体碰撞接触切割（OnCollisionEnter），测试起来效果一般。
/// - 切割方式：沿刀刃所在平面精确切割——法线取本物体局部轴 cutNormalAxis（默认 +Y，即刀刃朝上的面），
///   平面原点取刀刃的世界坐标位置，调用目标物体的 ComputeSlice()。
/// - 挥刀与冷却机制：把"一次挥刀"定义为一连串接触动作。单刀最多切割 maxCutCount 个物体，
///   每切一刀刷新 lastCutTime；若两次切割间隔超过 swingDuration，则视为挥刀超时、重置计数开新的一刀；
///   切满一刀后进入冷却 cooldown（nextSwingTime 之后才能再次挥刀），防止快速连切刷碎片。
/// - 手动按键：可设置 cutKey 在接触范围内按键一次性切割（可配合 cutOnContact = false 使用）。
/// 使用注意：
///   1. 刀刃物体上必须有碰撞体（建议 MeshCollider 并勾选 Is Trigger）；
///   2. 刀刃的位置/旋转决定了切割平面的位置与朝向，可以在场景中看见黄色的法线和绿色的边框
///   3. 被切物体需要挂 SliceAndFracture 组件且模式允许切割，否则接触无效。
/// </summary>

public class BladeSlicer : MonoBehaviour
{
    [Header("切割方式")]
    [Tooltip("是否接触物体即可切割")]
    // 是否"一接触就自动切割"。为 true 时，Trigger/Collision 接触瞬间即尝试切割；
    // 为 false 时，仅记录接触（contactColliders），等待手动按键（cutKey）触发切割。
    public bool cutOnContact = true;

    [Tooltip("可选：手动切割按键（KeyCode.None 表示不启用按键切割）")]
    // 手动切割按键。按下瞬间会对当前接触中的所有可切物体各尝试一次切割，依然受CD等限制
    public KeyCode cutKey = KeyCode.None;

    [Header("切割限制")]
    [Tooltip("挥刀冷却时间（秒）。切满一刀后，需等待冷却才能再次挥刀")]
    public float cooldown = 0.5f;

    [Tooltip("单次挥刀最多切割的物体数量")]
    // 一次挥刀内切满该数量后即进入冷却，避免一刀把所有接触物体全部切碎。
    public int maxCutCount = 3;

    [Tooltip("单次挥刀最长持续时间（秒），超过后自动重置切割计数")]
    // 挥刀超时（秒）：两次切割间隔超过该值则视为挥刀中断，
    public float swingDuration = 1f;

    [Header("切割平面")]
    [Tooltip("切割平面法线方向（局部坐标，默认 +Y，即刀刃朝上的面）")]
    public Vector3 cutNormalAxis = Vector3.up;

    private int cutsInSwing = 0;    // 当前这刀已切割数量：一次"挥刀"内累计切了几刀，达到 maxCutCount 即触发冷却
    private float lastCutTime = 0f;    // 本次挥刀最后一次切割的时间（Time.time），用于挥刀超时判断（超过 swingDuration 重置计数）
    private float nextSwingTime = 0f;    // 下一次允许挥刀的时间：冷却期间（Time.time < nextSwingTime）拒绝一切切割请求
    
    // 当前接触中的碰撞体集合：OnTriggerEnter/Stay 添加、OnTriggerExit 移除，
    // 供按键切割（Update 中遍历）使用；HashSet 自动去重，避免重复记录
    private readonly HashSet<Collider> contactColliders = new HashSet<Collider>();

    /// <summary>
    /// Trigger 接触进入回调：把进入的碰撞体记入接触集合，并按需立即尝试切割。
    /// 注意：本物体需勾选 Is Trigger 的碰撞体（MeshCollider）+ Rigidbody 才能收到该回调。
    /// </summary>
    void OnTriggerEnter(Collider collider)
    {
        contactColliders.Add(collider);
        if (cutOnContact) TryCut(collider);
    }

    /// <summary>
    /// Trigger 接触持续回调：物体停留在 Trigger 内时每帧触发。
    /// 作用：物体在刀刃范围内"扫过"时能持续尝试切割（即使 Enter 那一帧因冷却被拒绝，
    /// 后续 Stay 帧仍有机会命中）；同样把停留者加入接触集合。
    /// </summary>
    void OnTriggerStay(Collider collider)
    {
        contactColliders.Add(collider);
        if (cutOnContact) TryCut(collider);
    }

    /// <summary>
    /// Trigger 接触离开回调：把离开的碰撞体从接触集合中移除，
    /// 使其不再参与按键切割的遍历。
    /// </summary>
    void OnTriggerExit(Collider collider)
    {
        contactColliders.Remove(collider);
    }

    /// <summary>
    /// 实体碰撞回调：当刀刃碰撞体未勾选 Is Trigger 时（退化为物理碰撞），
    /// 碰撞瞬间（contactCount &gt; 0）取第一个接触点对面的碰撞体尝试切割。
    /// </summary>
    void OnCollisionEnter(Collision collision)
    {
        // 若刀刃碰撞体未勾选 Is Trigger，则退化为碰撞接触切割
        if (cutOnContact && collision.contactCount > 0)
        {
            TryCut(collision.contacts[0].otherCollider);
        }
    }

    /// <summary>
    /// 每帧执行：
    ///  1. 挥刀超时检测：若本刀已切过（cutsInSwing &gt; 0）且距上次切割超过 swingDuration，
    ///     则重置计数，把后续接触视为"新的一刀"；
    ///  2. 手动按键切割：若配置了 cutKey 且检测到按下（GetKeyDown），
    ///     遍历接触集合（拷贝一份，避免遍历中修改集合）逐个尝试切割。
    /// </summary>
    void Update()
    {
        // 挥刀超时后重置计数，下次接触视为新的一刀
        if (cutsInSwing > 0 && Time.time - lastCutTime > swingDuration)
        {
            cutsInSwing = 0;
        }

        // 手动按键切割：对接触中的所有可切物体尝试切割
        // 拷贝集合是为了避免 TryCut 过程中接触集合被 OnTrigger* 回调修改导致遍历异常
        if (cutKey != KeyCode.None && Input.GetKeyDown(cutKey))
        {
            foreach (Collider collider in new List<Collider>(contactColliders))
            {
                TryCut(collider);
            }
        }
    }

    /// <summary>
    /// 尝试切割一个物体（受冷却与单刀数量限制）。
    /// 判定顺序：
    ///  1. 单刀上限：取 maxCutCount 与 1 的较大值（防止配置为 0/负数时一刀都切不了）；
    ///  2. 冷却检查：Time.time &lt; nextSwingTime 说明上一刀冷却未结束，直接放弃；
    ///  3. 单刀数量：cutsInSwing 已达上限则放弃（保持"一刀切满即冷却"的节奏）；
    ///  4. 有效性检查：碰撞体可能已失效（其物体已被上次切割 SetActive(false) 隐藏），跳过；
    ///  5. 组件检查：对方必须带 SliceAndFracture 组件，否则不可切；
    ///  6. 模式检查：mode == 仅破碎 的物体禁止被切割；
    ///  7. 执行切割：用刀刃局部轴 cutNormalAxis 换算世界法线，以刀刃位置为平面原点
    ///     调用 saf.ComputeSlice(worldNormal, 刀刃位置) 切出两半；
    ///  8. 计数收尾：cutsInSwing +1、刷新 lastCutTime；
    ///     若本刀已达 maxCutCount，设置冷却时间 nextSwingTime = 当前时间 + cooldown 并重置计数。
    /// </summary>
    /// <param name="collider">接触到的碰撞体（将被切割的物体上的碰撞体）</param>
    private void TryCut(Collider collider)
    {
        int maxCuts = Mathf.Max(1, maxCutCount);

        // 冷却中或本刀已切满
        if (Time.time < nextSwingTime) return;
        if (cutsInSwing >= maxCuts) return;

        // 物体可能已被隐藏（上次切割后 SetActive(false)）
        if (collider == null || !collider.gameObject.activeInHierarchy) return;

        var saf = collider.GetComponent<SliceAndFracture>();
        if (saf == null) return;

        // 仅破碎模式的物体不可切割
        if (saf.mode == SliceFractureMode.仅破碎) return;

        // 沿刀刃平面精确切割：法线为刀刃的 cutNormalAxis 方向，原点为刀刃位置
        // TransformDirection 把局部法线换算成世界方向（含缩放与旋转影响）
        Vector3 worldNormal = this.transform.TransformDirection(cutNormalAxis.normalized);
        saf.ComputeSlice(worldNormal, this.transform.position);

        // 记录本刀已切割数量与最后切割时间（供挥刀超时判断）
        cutsInSwing++;
        lastCutTime = Time.time;

        // 切满一刀后进入冷却，冷却结束开始新的一刀
        if (cutsInSwing >= maxCuts)
        {
            nextSwingTime = Time.time + cooldown;
            cutsInSwing = 0;
        }
    }

    /// <summary>
    /// 编辑器 Gizmos 绘制回调：仅在编辑器中选中本物体时绘制，
    /// 用黄色射线显示切割平面法线、用绿色四边形显示切割平面范围，方便在场景里对齐刀刃。
    /// 该回调不影响游戏运行。
    /// </summary>
    void OnDrawGizmosSelected()
    {
        // 显示切割平面，便于对齐刀刃
        Vector3 normal = this.transform.TransformDirection(cutNormalAxis.normalized);
        Vector3 right = this.transform.right;
        Vector3 fwd = this.transform.forward;
        Vector3 o = this.transform.position;

        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(o, normal * 0.5f);

        Gizmos.color = Color.green;
        Gizmos.DrawLine(o - right * 0.5f - fwd * 0.5f, o + right * 0.5f - fwd * 0.5f);
        Gizmos.DrawLine(o + right * 0.5f - fwd * 0.5f, o + right * 0.5f + fwd * 0.5f);
        Gizmos.DrawLine(o + right * 0.5f + fwd * 0.5f, o - right * 0.5f + fwd * 0.5f);
        Gizmos.DrawLine(o - right * 0.5f + fwd * 0.5f, o - right * 0.5f - fwd * 0.5f);
    }
}
