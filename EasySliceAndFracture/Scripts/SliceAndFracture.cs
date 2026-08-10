using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 功能模式枚举：定义该物体允许被哪种方式拆解。
/// 该枚举通过 CanSlice / CanFracture 两个属性贯穿全组件，控制行为边界：
/// 注意：切割与破碎的次数是分开计数的（currentSliceCount / currentRefractureCount），
/// 模式只负责"限制"某类操作，不会自动触发操作。
/// </summary>
public enum SliceFractureMode
{
    仅切割 = 0,
    仅破碎 = 1,
    切割并破碎 = 2
}

/// <summary>
/// 切割与破碎的核心组件（挂到任意"可拆解"的物体上）。
/// 功能概览：
/// - 切割（Slice）：外部调用 ComputeSlice()，沿指定平面把网格切成两半，适合刀刃切割；
/// - 破碎（Fracture）：调用 ComputeFracture() 或通过触发方式（碰撞/触发器/键盘）自动触发，把网格递归破成多个碎片；
/// - 递归拆解：生成的切片/碎片会复制本组件（见 CopyComponent），可被再次切割/破碎，
///   直到嵌套代数达到 MaxGeneration（4 代），超出后新碎片不再保留拆解能力，防止无限递归；
/// - 二次拆解限制：currentSliceCount / currentRefractureCount 记录已拆解次数，
///   分别受 SliceOptions.enableReslicing 与 RefractureOptions.enableRefracturing 限制。
/// 使用前提（由 RequireComponent 强制保证）：挂载物体上MeshFilter + MeshRenderer + Rigidbody + MeshCollider（推荐）
/// 注意：如果父物体缩放不均匀（非等比），碎片网格会被压扁导致渲染异常，详见 OnValidate 的警告。
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(Rigidbody))]
public class SliceAndFracture : MonoBehaviour
{
    [Tooltip("功能模式：仅切割（可被切割，不会破碎）/ 仅破碎（只能破碎，不能被切割）/ 切割并破碎")]
    public SliceFractureMode mode = SliceFractureMode.切割并破碎;

    [Tooltip("切割选项")]
    // 切割行为配置（是否允许二次切割、最大二次切割次数、内部面材质、纹理缩放/偏移、回调开关），
    public SliceOptions sliceOptions;

    [Tooltip("破碎选项")]
    // 破碎行为配置（碎片数量、拆解轴、漂浮碎片检测、是否异步、内部面材质、纹理缩放/偏移），
    public FractureOptions fractureOptions;

    [Tooltip("二次破碎选项")]
    // 碎片再次破碎的配置（是否允许二次破碎、最大二次破碎次数、是否回调），
    public RefractureOptions refractureOptions;

    [Tooltip("破碎触发选项")]
    // 破碎自动触发的配置（触发类型、最小碰撞力、按标签过滤、触发按键），
    public TriggerOptions triggerOptions;

    [Tooltip("回调选项")]
    // 拆解事件回调（onFracture 在破碎瞬间调用、onCompleted 在过程完成时调用），
    public CallbackOptions callbackOptions;

    [HideInInspector]
    public int currentRefractureCount = 0;//存储当前破碎次数

    [HideInInspector]
    public int currentSliceCount = 0;//存储当前切割次数

    [HideInInspector]
    public int generation = 0;//存储当前嵌套代数，最大为MaxGeneration，防止爆炸
    private const int MaxGeneration = 4;

    /// <summary>
    /// 收集所有碎片/切片的容器物体（名字为 "物体名Slices" 或 "物体名Fragments"）。
    /// 创建逻辑见 ComputeSlice / ComputeFracture：把容器挂到本物体父级下，位置/旋转与本体一致，
    /// 局部缩放固定为 1（避免父物体非等比缩放破坏碎片网格）。
    /// 所有新生成的碎片都会 SetParent 到这个容器下，方便整组管理（如整体移动/清理）。
    /// </summary>
    private GameObject fragmentRoot;

    private bool CanSlice => mode != SliceFractureMode.仅破碎;
    private bool CanFracture => mode != SliceFractureMode.仅切割;

    /// <summary>
    /// Unity 编辑器回调：在 Inspector 中修改本组件参数、或于编辑器里加载/实例化该物体时自动调用。
    /// 作用：检测父物体scale.xyz是否均匀，若有则输出警告。
    /// 原因：碎片会作为父物体的子物体生成，网格顶点的局部坐标会被父物体的缩放拉伸，或者出现意想不到的的问题。所以还是不要有父物体比较好
    /// </summary>
    void OnValidate()
    {
        if (this.transform.parent != null)
        {
            var scale = this.transform.parent.localScale;
            if ((scale.x != scale.y) || (scale.x != scale.z) || (scale.y != scale.z))
            {
                Debug.LogWarning($"警告：被破碎物体的父物体必须在所有轴上均匀缩放，否则碎片将无法正确渲染。", this.transform);
            }
        }
    }

    /// <summary>
    /// 【切割入口】沿指定平面把本物体网格切成两半（由切割刀等外部脚本调用，如 BladeSlicer）。
    /// 完整流程：
    ///  1. 模式检查：仅破碎模式下直接返回（CanSlice 为 false，防止"仅破碎"物体被刀切）；
    ///  2. 二次切割检查：若本物体本身就是切片（currentSliceCount &gt; 0），
    ///     必须满足"启用了二次切割（enableReslicing）且已切次数未达上限（maxResliceCount）"才能继续切，否则返回；
    ///  3. 空网格保护：MeshFilter 没有 sharedMesh 时返回；
    ///  4. 懒创建碎片容器 fragmentRoot（仅第一次调用时创建，之后复用）；
    ///  5. 用 CreateSliceTemplate() 生成"切片模板"（包含材质/碰撞体/刚体/本组件的复制件）；
    ///  6. 把世界坐标的切割平面换算成本地坐标（网格运算发生在本地空间），交给 Fragmenter.Slice 真正切网格；
    ///  7. 收尾：销毁模板、隐藏原物体（SetActive(false)）、触发 onCompleted 完成回调。
    /// 注意：Fragmenter.Slice 会就地生成两个新的切片 GameObject 并挂到 fragmentRoot 容器下。
    /// </summary>
    /// <param name="sliceNormalWorld">切割平面法线（世界坐标）。例如刀刃朝上的平面法线为 transform.up</param>
    /// <param name="sliceOriginWorld">切割平面原点（世界坐标）。例如刀刃的位置 transform.position</param>

    public void ComputeSlice(Vector3 sliceNormalWorld, Vector3 sliceOriginWorld)
    {
        // 仅破碎模式下不允许切割
        if (!CanSlice) return;

        //能否切割和切割上限检查
        if (this.currentSliceCount > 0 &&
            !(this.sliceOptions.enableReslicing && this.currentSliceCount < this.sliceOptions.maxResliceCount))
        {
            return;
        }

        var mesh = this.GetComponent<MeshFilter>().sharedMesh;
        // 没有网格可切，直接返回
        if (mesh == null) return;

        // 若碎片容器尚未创建，先创建：
        // 容器放在本物体父级下、位置/旋转与本体对齐、局部缩放 = 1，
        // 这样所有切片在层级面板中集中管理，且不受父物体缩放影响
        if (this.fragmentRoot == null)
        {
            // 命名：切割产生的容器叫 "原物体名Slices"（区别于破碎的 "Fragments"）
            this.fragmentRoot = new GameObject($"{this.name}Slices");
            this.fragmentRoot.transform.SetParent(this.transform.parent);
            this.fragmentRoot.transform.position = this.transform.position;
            this.fragmentRoot.transform.rotation = this.transform.rotation;
            this.fragmentRoot.transform.localScale = Vector3.one;
        }

        // 创建“切片模板”：一个带完整组件（网格/材质/碰撞体/刚体/本组件）的空物体，
        // Fragmenter 切割后会从它克隆出两个实际切片，最后模板本身会被销毁
        var sliceTemplate = CreateSliceTemplate();

        // 将世界坐标的切割平面换算成本地坐标：
        // 因为后续对网格的切割运算发生在网格的本地空间，法线与原点都必须变换到本地系
        var sliceNormalLocal = this.transform.InverseTransformDirection(sliceNormalWorld);
        var sliceOriginLocal = this.transform.InverseTransformPoint(sliceOriginWorld);

        // 真正执行网格切割：
        // Fragmenter.Slice(本体, 本地法线, 本地原点, 切割选项, 模板, 容器)
        // 内部会做 网格二分（切片算法）→ 按模板克隆组件 → 生成的切片 SetParent 到容器
        Fragmenter.Slice(this.gameObject,
                         sliceNormalLocal,
                         sliceOriginLocal,
                         this.sliceOptions,
                         sliceTemplate,
                         this.fragmentRoot.transform);

        // 模板用完即销毁（模板只是复制用的“模具”，不参与场景渲染/物理）
        GameObject.Destroy(sliceTemplate);

        // 隐藏原物体：原网格已被“切走”，本体不再需要显示，用 SetActive(false) 隐藏
        // （而非 Destroy，因为同帧内其他逻辑仍可能引用它）
        this.gameObject.SetActive(false);

        // 触发完成回调（onCompleted）：若在 Inspector 中绑定了事件则在此处执行
        if (callbackOptions != null && callbackOptions.onCompleted != null)
        {
            callbackOptions.onCompleted.Invoke();
        }
    }

//----------------------------------------------------------------------------------------------

    /// <summary>
    /// 直接触发破碎（公开入口，供外部脚本/事件一键砸碎本物体）。
    /// 流程：先调用 CallbackOptions.CallOnFracture 派发"破碎瞬间"回调（引发者为空，位置取本物体位置），
    /// 再调用 ComputeFracture() 真正执行破碎。
    /// 本方法不做任何限制校验，限制逻辑都在 ComputeFracture 内部。
    /// </summary>
    public void CauseFracture()
    {
        callbackOptions.CallOnFracture(null, gameObject, transform.position);
        this.ComputeFracture();
    }

    /// <summary>
    /// 【破碎入口】把本物体网格递归破成多个碎片。
    /// 完整流程：
    ///  1. 模式检查：仅切割模式下直接返回（CanFracture 为 false）；
    ///  2. 二次破碎检查，切割上限和能否切割
    ///  3. 空网格保护：没有 sharedMesh 时return；
    ///  4. 懒创建碎片容器 fragmentRoot（命名为 "原物体名Fragments"）；
    ///  5. 用 CreateFragmentTemplate() 生成"碎片模板"；
    ///  6. 分两条路径执行：
    ///      - 同步（asynchronous == false）：直接调用 Fragmenter.Fracture 一次性算出全部碎片；
    ///      - 异步（asynchronous == true）：启动协程 Fragmenter.FractureAsync，破碎过程跨多帧执行，
    ///        每帧切一刀避免卡死主线程，完成后执行回调 lambda 收尾。
    ///  7. 收尾（同步路径或异步协程完成时）：销毁模板、隐藏原物体、触发完成回调。
    /// 注意：Fragmenter.Fracture 会按 FractureOptions 中的 xAxis/yAxis/zAxis 逐轴递归切割网格，
    /// 每个最终碎片都从 fragmentTemplate 克隆组件并挂到 fragmentRoot 下。
    /// </summary>
    private void ComputeFracture()
    {
        // 仅切割模式下不允许破碎
        if (!CanFracture) return;

        // 碎片再次破碎受“二次破碎”选项限制,
        if (this.currentRefractureCount > 0 &&
            !(this.refractureOptions.enableRefracturing && this.currentRefractureCount < this.refractureOptions.maxRefractureCount))
        {
            return;
        }

        var mesh = this.GetComponent<MeshFilter>().sharedMesh;
        // 没有网格可碎，直接返回
        if (mesh == null) return;

        // 若碎片容器尚未创建，先创建（逻辑与 ComputeSlice 相同，仅命名后缀不同为 "Fragments"）
        if (this.fragmentRoot == null)
        {
            this.fragmentRoot = new GameObject($"{this.name}Fragments");
            this.fragmentRoot.transform.SetParent(this.transform.parent);
            this.fragmentRoot.transform.position = this.transform.position;
            this.fragmentRoot.transform.rotation = this.transform.rotation;
            this.fragmentRoot.transform.localScale = Vector3.one;
        }

        // 创建“碎片模板”：一个带完整组件（网格/材质/碰撞体/刚体/本组件）的空物体，
        // 每个最终碎片都由它克隆而来，模板本身最后会被销毁
        var fragmentTemplate = CreateFragmentTemplate();

        // 分支一：异步破碎（asynchronous == true）
        // 启动协程逐帧执行破碎，避免一次生成大量碎片导致主线程卡顿；
        // 协程结束后执行回调 lambda 完成收尾（销毁模板/隐藏本体/触发回调）
        if (fractureOptions.asynchronous)
        {
            StartCoroutine(Fragmenter.FractureAsync(
                this.gameObject,
                this.fractureOptions,
                fragmentTemplate,
                this.fragmentRoot.transform,
                () =>
                {
                    // 模板用完即销毁（异步完成后才销毁）
                    GameObject.Destroy(fragmentTemplate);

                    // 隐藏原物体：原网格已被“碎走”，本体不再需要显示
                    this.gameObject.SetActive(false);

                    // 触发完成回调（二次破碎时受 invokeCallbacks 开关控制，见 FireCompletedCallback）
                    FireCompletedCallback();
                }
            ));
        }
        // 分支二：同步破碎（asynchronous == false，默认）
        else
        {
            // 一次性完成全部递归切割，期间主线程会阻塞，碎片量大时可能卡顿
            Fragmenter.Fracture(this.gameObject,
                                this.fractureOptions,
                                fragmentTemplate,
                                this.fragmentRoot.transform);

            // 模板用完即销毁
            GameObject.Destroy(fragmentTemplate);

            // 隐藏原物体
            this.gameObject.SetActive(false);

            // 触发完成回调
            FireCompletedCallback();
        }
    }

    /// <summary>
    /// 触发"破碎/切割完成"回调，带二次破碎过滤。
    /// 规则：
    ///  - 若本物体从未被二次破碎（currentRefractureCount == 0，即第一次拆解）：无条件触发；
    ///  - 若本物体是二次破碎的碎片（currentRefractureCount &gt; 0）：
    ///    只有当 RefractureOptions.invokeCallbacks 为 true 时才触发（避免每次碎片再生都刷一遍回调）。
    /// 这与切割路径（ComputeSlice 中无条件触发 onCompleted）形成对比：切割的完成回调不受此开关限制。
    /// </summary>
    private void FireCompletedCallback()
    {
        if ((this.currentRefractureCount == 0) ||
            (this.currentRefractureCount > 0 && this.refractureOptions.invokeCallbacks))
        {
            if (callbackOptions != null && callbackOptions.onCompleted != null)
            {
                callbackOptions.onCompleted.Invoke();
            }
        }
    }

    /// <summary>
    /// Unity 物理回调：发生实体碰撞时调用，用于"碰撞触发破碎"。
    /// 触发条件（全部满足才破碎）：
    ///  1. 当前模式允许破碎（CanFracture）；
    ///  2. triggerOptions.triggerType == TriggerType.碰撞；
    ///  3. 碰撞冲击力（冲量÷固定时间步长）大于 minimumCollisionForce；
    ///  4. 未启用按标签过滤，或碰撞对方的标签在允许列表 triggerAllowedTags 中。
    /// 命中后先派发 onFracture 回调（带引发碰撞的碰撞体/本物体/接触点），再执行 ComputeFracture()。
    /// 注意：本物体需挂非触发器 Collider 且对方也有刚体或碰撞体才能收到该回调。
    /// </summary>
    void OnCollisionEnter(Collision collision)
    {
        if (!CanFracture) return;

        if (triggerOptions.triggerType == TriggerType.碰撞)
        {
            if (collision.contactCount > 0)
            {
                var contact = collision.contacts[0];
                // 把冲量（impulse）除以固定时间步长换算成"平均碰撞力"，
                // 与 minimumCollisionForce 比较判断这次撞击是否够狠
                float collisionForce = collision.impulse.magnitude / Time.fixedDeltaTime;

                // 若启用了按标签过滤，只有标签在允许列表中的物体会触发破碎
                bool tagAllowed = triggerOptions.IsTagAllowed(contact.otherCollider.gameObject.tag);

                // 同时满足"力量达标"与"标签放行"才触发破碎
                if (collisionForce > triggerOptions.minimumCollisionForce &&
                   (!triggerOptions.filterCollisionsByTag || tagAllowed))
                {
                    // 派发"破碎瞬间"回调：引发者 = 撞过来的碰撞体，位置 = 实际接触点
                    callbackOptions.CallOnFracture(contact.otherCollider, gameObject, contact.point);
                    this.ComputeFracture();
                }
            }
        }
    }

    /// <summary>
    /// Unity 触发器回调：其他碰撞体进入本物体 Trigger 区域时调用，用于"触发器触发破碎"。
    /// 触发条件：
    ///  1. 当前模式允许破碎（CanFracture）；
    ///  2. triggerOptions.triggerType == TriggerType.触发器；
    ///  3. 未启用按标签过滤，或进入者的标签在允许列表 triggerAllowedTags 中。
    /// 注意：本物体上的 Collider 需勾选 Is Trigger，并配合 Rigidbody 才能收到该回调。
    /// </summary>
    void OnTriggerEnter(Collider collider)
    {
        if (!CanFracture) return;

        if (triggerOptions.triggerType == TriggerType.触发器)
        {
            // 若启用了按标签过滤，只有标签在允许列表中的物体会触发破碎
            bool tagAllowed = triggerOptions.IsTagAllowed(collider.gameObject.tag);

            // 未启用过滤，或标签放行 → 触发破碎（回调位置取本物体位置，因为触发器没有接触点）
            if (!triggerOptions.filterCollisionsByTag || tagAllowed)
            {
                callbackOptions.CallOnFracture(collider, gameObject, transform.position);
                this.ComputeFracture();
            }
        }
    }

    /// <summary>
    /// Unity 帧回调：每帧执行，用于"键盘触发破碎"。
    /// 触发条件：
    ///  1. 当前模式允许破碎（CanFracture）；
    ///  2. triggerOptions.triggerType == TriggerType.键盘；
    ///  3. 玩家按下 triggerOptions.triggerKey（GetKeyDown 只响应按下瞬间，不会连续触发）。
    /// 适合调试或需要手动按碎物体的场景（如打碎花瓶、开关门等）。
    /// </summary>
    void Update()
    {
        if (!CanFracture) return;

        if (triggerOptions.triggerType == TriggerType.键盘)
        {
            if (Input.GetKeyDown(triggerOptions.triggerKey))
            {
                // 键盘触发没有"引发者"，传 null；位置取本物体位置
                callbackOptions.CallOnFracture(null, gameObject, transform.position);
                this.ComputeFracture();
            }
        }
    }

    /// <summary>
    /// 创建"切片模板"：一个空的 GameObject，承载切片所需的全部组件，
    /// Fragmenter.Slice 会把它作为模具（template）克隆出两个实际切片。
    /// 模板中包含：
    ///  1. MeshFilter：占位，实际切片网格由 Fragmenter 生成后赋值；
    ///  2. MeshRenderer + 材质数组：材质槽 0 = 原表面材质；槽 1 = 原内部面材质（若原物体已拆解过则沿用）；
    ///     最后一个槽 = sliceOptions.insideMaterial（新产生的切割截面材质）；
    ///  3. MeshCollider：凸包碰撞体（convex），物理材质与 Is Trigger 属性继承原碰撞体；
    ///  4. Rigidbody：把本物体的线速度/角速度/阻尼/重力等复制给切片，保证拆解瞬间运动连续；
    ///  5. （可选）SliceAndFracture 组件：若 generation 未达 MaxGeneration，
    ///     通过 CopyComponent 复制本组件，使切片还能被再次切割/破碎。
    /// 注意：模板在每次切割时新建、用完即销毁（见 ComputeSlice），不会长期占用场景。
    /// </summary>
    private GameObject CreateSliceTemplate()
    {
        GameObject obj = new GameObject();
        // 模板只作为"模具"，名字用于区分，实际切片会由 Fragmenter 生成
        obj.name = "Slice";
        // 继承原物体标签，保证碰撞/过滤逻辑（如 triggerAllowedTags）对新切片同样生效
        obj.tag = this.tag;

//...............................................................
//      这里可以操作切割后的物体
//      obj.layer = LayerMask.("");
//      obj.AddComponent<>();
//      obj.tag = "";
//      破碎物体也一样
//...............................................................

        // 更新网格：占位用，切片生成后 Fragmenter 会把切割出的半块网格赋给克隆体
        obj.AddComponent<MeshFilter>();

        // 材质：表面 / 已有内部面(若存在) / 新切割面
        // 组装渲染器的材质数组：
        //  - 槽0：原物体的表面材质（sharedMaterial）；
        //  - 槽1：若原物体已拥有第二个材质（说明此前被拆解过、有旧内部面），则沿用；
        //  - 最后槽：本次切割新产生的截面使用 sliceOptions.insideMaterial。
        var meshRenderer = obj.AddComponent<MeshRenderer>();
        var srcRenderer = this.GetComponent<MeshRenderer>();
        var srcMaterials = srcRenderer.sharedMaterials;
        if (srcMaterials.Length > 1 && srcMaterials[1] != null)
        {
            // 原物体已有内部面材质 → 三材质：表面 / 旧内部面 / 新切割面
            meshRenderer.sharedMaterials = new Material[3] {
                srcRenderer.sharedMaterial,
                srcMaterials[1],
                this.sliceOptions.insideMaterial
            };
        }
        else
        {
            // 原物体没有内部面材质（首次切割）→ 两材质：表面 / 新切割面
            meshRenderer.sharedMaterials = new Material[2] {
                srcRenderer.sharedMaterial,
                this.sliceOptions.insideMaterial
            };
        }

        // 复制碰撞体：
        // 切片统一使用 MeshCollider（其网格稍后由 Fragmenter 赋入），
        // convex = true 保证可参与物理碰撞；物理材质与是否触发器都继承原碰撞体设置
        var thisCollider = this.GetComponent<Collider>();
        var fragmentCollider = obj.AddComponent<MeshCollider>();
        fragmentCollider.convex = true;
        fragmentCollider.sharedMaterial = thisCollider.sharedMaterial;
        fragmentCollider.isTrigger = thisCollider.isTrigger;

        // 复制刚体：
        // 把本物体的运动状态（速度/角速度/阻尼/重力开关）复制给切片，
        // 使拆解瞬间切片继续沿原方向运动，看起来更自然
        var thisRigidBody = this.GetComponent<Rigidbody>();
        var fragmentRigidBody = obj.AddComponent<Rigidbody>();
        fragmentRigidBody.linearVelocity = thisRigidBody.linearVelocity;
        fragmentRigidBody.angularVelocity = thisRigidBody.angularVelocity;
        fragmentRigidBody.linearDamping = thisRigidBody.linearDamping;
        fragmentRigidBody.angularDamping = thisRigidBody.angularDamping;
        fragmentRigidBody.useGravity = thisRigidBody.useGravity;

        // 若未达到嵌套上限，让切片保留切割/破碎能力（fromSlice = true 表示这是切割产物）
        if (this.generation < MaxGeneration)
        {
            CopyComponent(obj, true);
        }

        return obj;
    }

    /// <summary>
    /// 创建"碎片模板"：逻辑与 CreateSliceTemplate 完全一致，区别在于：
    ///  - 名字为 "Fragment"（区分破碎产物）；
    ///  - 新截面材质使用 fractureOptions.insideMaterial；
    ///  - CopyComponent 传入 false（表示这是破碎产物，破碎次数 +1）。
    /// Fragmenter.Fracture / FractureAsync 会把它作为模具克隆出所有最终碎片。
    /// </summary>
    private GameObject CreateFragmentTemplate()
    {
        GameObject obj = new GameObject();
        obj.name = "Fragment";
        // 继承原物体标签，保证碰撞/过滤逻辑对新碎片同样生效
        obj.tag = this.tag;

        // 更新网格：占位用，碎片网格由 Fragmenter 生成后赋入
        obj.AddComponent<MeshFilter>();

        // 材质：表面 / 已有内部面(若存在) / 新破碎面
        // 与切片模板相同：槽0 = 表面材质；槽1 = 旧内部面（若存在）；最后槽 = 新破碎面材质
        var meshRenderer = obj.AddComponent<MeshRenderer>();
        var srcRenderer = this.GetComponent<MeshRenderer>();
        var srcMaterials = srcRenderer.sharedMaterials;
        if (srcMaterials.Length > 1 && srcMaterials[1] != null)
        {
            // 原物体已有内部面材质 → 三材质：表面 / 旧内部面 / 新破碎面
            meshRenderer.sharedMaterials = new Material[3] {
                srcRenderer.sharedMaterial,
                srcMaterials[1],
                this.fractureOptions.insideMaterial
            };
        }
        else
        {
            // 原物体没有内部面材质（首次破碎）→ 两材质：表面 / 新破碎面
            meshRenderer.sharedMaterials = new Material[2] {
                srcRenderer.sharedMaterial,
                this.fractureOptions.insideMaterial
            };
        }

        // 复制碰撞体：凸包 MeshCollider，物理材质与触发器设置继承原碰撞体
        var thisCollider = this.GetComponent<Collider>();
        var fragmentCollider = obj.AddComponent<MeshCollider>();
        fragmentCollider.convex = true;
        fragmentCollider.sharedMaterial = thisCollider.sharedMaterial;
        fragmentCollider.isTrigger = thisCollider.isTrigger;

        // 复制刚体：继承本物体的运动状态，保证拆解瞬间运动连续
        var thisRigidBody = this.GetComponent<Rigidbody>();
        var fragmentRigidBody = obj.AddComponent<Rigidbody>();
        fragmentRigidBody.linearVelocity = thisRigidBody.linearVelocity;
        fragmentRigidBody.angularVelocity = thisRigidBody.angularVelocity;
        fragmentRigidBody.linearDamping = thisRigidBody.linearDamping;
        fragmentRigidBody.angularDamping = thisRigidBody.angularDamping;
        fragmentRigidBody.useGravity = thisRigidBody.useGravity;

        // 若未达到嵌套上限，让碎片保留切割/破碎能力（fromSlice = false 表示这是破碎产物）
        if (this.generation < MaxGeneration)
        {
            CopyComponent(obj, false);
        }

        return obj;
    }

    /// <summary>
    /// 把本组件（SliceAndFracture）复制到生成的碎片/切片上，使新物体继承全部拆解能力与统计信息。
    /// 复制内容：
    ///  - mode 及全部选项对象（sliceOptions/fractureOptions/refractureOptions/triggerOptions/callbackOptions）：
    ///    与父物体共享同一份配置引用，修改配置对所有碎片统一生效；
    ///  - generation：+1（子物体比父物体多一代）；
    ///  - fragmentRoot：共享同一个碎片容器（所有碎片挂在同一个根下）；
    ///  - 计数继承规则：
    ///      fromSlice == true  → currentSliceCount = 父切片次数 + 1（本次切出的新切片），破碎次数沿用；
    ///      fromSlice == false → currentRefractureCount = 父碎片次数 + 1（本次碎出的新碎片），切割次数沿用。
    /// 该计数用于 ComputeSlice / ComputeFracture 中的二次拆解上限判断。
    /// </summary>
    /// <param name="obj">目标物体（即将成为切片/碎片模板的 GameObject）</param>
    /// <param name="fromSlice">true 表示本次操作是切割（目标为切片），false 表示破碎（目标为碎片）</param>
    private void CopyComponent(GameObject obj, bool fromSlice)
    {
        var copy = obj.AddComponent<SliceAndFracture>();

        // 原样复制功能模式与全部配置引用（共享同一份，便于统一调整）
        copy.mode = this.mode;
        copy.sliceOptions = this.sliceOptions;
        copy.fractureOptions = this.fractureOptions;
        copy.refractureOptions = this.refractureOptions;
        copy.triggerOptions = this.triggerOptions;
        copy.callbackOptions = this.callbackOptions;
        // 子物体比父物体多一代，用于 MaxGeneration 上限判断
        copy.generation = this.generation + 1;
        // 所有碎片共享同一个容器物体
        copy.fragmentRoot = this.fragmentRoot;

        if (fromSlice)
        {
            // 切片：切割次数 +1，破碎次数沿用
            copy.currentSliceCount = this.currentSliceCount + 1;
            copy.currentRefractureCount = this.currentRefractureCount;
        }
        else
        {
            // 碎片：破碎次数 +1，切割次数沿用
            copy.currentRefractureCount = this.currentRefractureCount + 1;
            copy.currentSliceCount = this.currentSliceCount;
        }
    }
}
