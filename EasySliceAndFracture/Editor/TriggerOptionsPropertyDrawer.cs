using UnityEditor;
using UnityEngine;

/// <summary>
/// 触发选项（TriggerOptions）的自定义属性绘制器（PropertyDrawer）。
/// 作用：当某个序列化字段的类型为 TriggerOptions 时，Unity 检查器（Inspector）会调用本绘制器，
/// 以「触发选项」折叠面板的形式展示并编辑该结构体下的各个子字段。
/// 与其它选项面板不同的是：本面板会根据当前选择的「触发类型」（碰撞 / 触发器 / 键盘）
/// 动态显示不同的字段组合，即"按触发类型条件化显示字段"。
/// </summary>
[CustomPropertyDrawer(typeof(TriggerOptions))]
public class TriggerOptionsPropertyDrawer : PropertyDrawer
{   
    // 静态折叠状态：记录「触发选项」面板当前是展开（true）还是折叠（false）。
    // 声明为 static 后，场景中所有同类字段共享同一个折叠状态（与 Unity 官方示例行为一致）。
    private static bool foldout = true;

    // 覆写 PropertyDrawer.OnGUI：在检查器中绘制该属性的完整 UI。
    // 注意：由于本实现完全使用 EditorGUILayout 自动布局，position 参数实际未被使用。
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        // 先按名称缓存本结构体需要绘制的全部序列化子属性（后面绘制时直接复用，
        // 避免重复查找；不同的触发类型会用到这里的不同子集）：
        var minimumCollisionForce = property.FindPropertyRelative("minimumCollisionForce"); // 触发破碎所需的最小碰撞力
        var triggerType = property.FindPropertyRelative("triggerType");                     // 触发类型（TriggerType 枚举）
        var triggerKey = property.FindPropertyRelative("triggerKey");                       // 键盘触发时使用的按键
        var filterCollisionsByTag = property.FindPropertyRelative("filterCollisionsByTag"); // 是否按标签过滤碰撞
        var triggerAllowedTags = property.FindPropertyRelative("triggerAllowedTags");       // 允许触发破碎的标签列表

        // 复位缩进层级为 0，避免继承到外部检查器残留的缩进状态
        EditorGUI.indentLevel = 0;
        // 开始绘制带箭头图标的折叠面板头，并把返回值写回 foldout：
        // 用户点击面板头后，Unity 会返回新的折叠状态，从而驱动下方内容的显示/隐藏。
        foldout = EditorGUILayout.BeginFoldoutHeaderGroup(foldout, "触发选项");

        // 仅当面板处于展开状态时，才继续绘制各个子字段
        if (foldout)
        {
            // 子字段统一缩进一级，形成清晰的层级结构
            EditorGUI.indentLevel = 1;
            // 先绘制「触发类型」枚举，它是决定后续显示哪些字段的关键
            EditorGUILayout.PropertyField(triggerType, new GUIContent("触发类型", "触发破碎的输入类型。"));

            // 核心逻辑：根据「触发类型」的当前值，动态显示对应的一组字段。
            // enumValueIndex 即枚举在序列化中的整数值（下标）。
            switch (triggerType.enumValueIndex)
            {
                case ((int)TriggerType.碰撞):
                    // 碰撞触发：显示「最小碰撞力」和「按标签过滤碰撞」
                    EditorGUILayout.PropertyField(minimumCollisionForce, new GUIContent("最小碰撞力", "导致物体破碎所需的最小碰撞力。")); // 当碰撞力超过该值时触发破碎
                    EditorGUILayout.PropertyField(filterCollisionsByTag, new GUIContent("按标签过滤碰撞", "如果为真，只有标签在“允许标签”列表中的物体会触发碰撞。")); // 是否只允许特定标签的物体触发碰撞
                    if (filterCollisionsByTag.boolValue)
                    {
                        // 注意：这里先结束折叠面板头组（缩进临时退出折叠组），
                        // 以便在折叠面板下方绘制多行标签列表（Tag 列表控件无法在组内良好显示），
                        // 绘制完后再由函数末尾的 EndFoldoutHeaderGroup 收尾。
                        EditorGUILayout.EndFoldoutHeaderGroup();
                        EditorGUILayout.PropertyField(triggerAllowedTags, new GUIContent("允许的标签", "如果“按标签过滤碰撞”为真，只有标签在此列表中的物体会触发破碎。")); // 允许触发破碎的标签白名单
                    }
                    break;
                case ((int)TriggerType.触发器):
                    // 触发器触发：只关心标签过滤相关字段（通过触发器（Trigger）进入物体范围来触发）
                    EditorGUILayout.PropertyField(filterCollisionsByTag, new GUIContent("按标签过滤碰撞", "如果为真，只有标签在“允许标签”列表中的物体会触发碰撞。")); // 是否只允许特定标签的物体触发碰撞
                    // 与碰撞分支相同：先把折叠组结束，再绘制标签列表
                    EditorGUILayout.EndFoldoutHeaderGroup();
                    EditorGUILayout.PropertyField(triggerAllowedTags, new GUIContent("允许的标签", "如果“按标签过滤碰撞”为真，只有标签在此列表中的物体会触发破碎。")); // 允许触发破碎的标签白名单
                    break;
                case ((int)TriggerType.键盘):
                    // 键盘触发：只显示触发按键
                    EditorGUILayout.PropertyField(triggerKey, new GUIContent("触发按键", "如果触发类型为键盘，这是按下后会触发破碎的按键。")); // 按下该按键时触发破碎
                    break;
            }
        }

        // 结束折叠面板头组的绘制（对于在分支内提前 EndFoldoutHeaderGroup 的情况，
        // 这里会再次调用以保持 API 配对、防止绘制状态错乱）
        EditorGUILayout.EndFoldoutHeaderGroup();
        // 恢复缩进层级为 0，避免影响检查器中后续其它属性的绘制
        EditorGUI.indentLevel = 0;
    }

    // 让 Unity 认为本属性的默认绘制高度为 0（关键技巧，原理如下）：
    // OnGUI 内部完全依赖 EditorGUILayout 的自动布局（Auto Layout）来安排控件位置，
    // 控件的高度与纵向位置由布局系统在绘制时自行计算并推进。
    // 若 GetPropertyHeight 返回非零值，Unity 会在该属性顶部预先留出对应高度的空白，
    // 再调用 OnGUI 进行自动布局，最终导致面板上方出现一段多余的空隙；
    // 返回 0 即可消除这段多余空间（代价是无法使用 position 参数手动布局）。
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label) { return 0; }
}
