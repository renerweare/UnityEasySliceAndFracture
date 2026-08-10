using UnityEditor;
using UnityEngine;

/// <summary>
/// 二次破碎选项（RefractureOptions）的自定义属性绘制器（PropertyDrawer）。
/// 作用：当某个序列化字段的类型为 RefractureOptions 时，Unity 检查器（Inspector）会调用本绘制器，
/// 以「二次破碎选项」折叠面板的形式展示并编辑该结构体下的各个子字段，
/// 把零散的选项组织成一个可展开/折叠的 UI 分组，保持检查器界面整洁。
/// </summary>
[CustomPropertyDrawer(typeof(RefractureOptions))]
public class RefractureOptionsPropertyDrawer : PropertyDrawer
{   
    // 静态折叠状态：记录「二次破碎选项」面板当前是展开（true）还是折叠（false）。
    // 声明为 static 后，场景中所有同类字段共享同一个折叠状态（与 Unity 官方示例行为一致）。
    private static bool foldout = true;

    // 覆写 PropertyDrawer.OnGUI：在检查器中绘制该属性的完整 UI。
    // 注意：由于本实现完全使用 EditorGUILayout 自动布局，position 参数实际未被使用。
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        // 先按名称缓存本结构体需要绘制的三个序列化子属性（后面绘制时直接复用，
        // 避免重复查找）：
        var enableRefracturing = property.FindPropertyRelative("enableRefracturing"); // 是否允许二次破碎
        var maxRefractureCount = property.FindPropertyRelative("maxRefractureCount"); // 可二次破碎的最大次数
        var invokeCallbacks = property.FindPropertyRelative("invokeCallbacks");       // 二次破碎时是否调用回调

        // 复位缩进层级为 0，避免继承到外部检查器残留的缩进状态
        EditorGUI.indentLevel = 0;
        // 开始绘制带箭头图标的折叠面板头，并把返回值写回 foldout：
        // 用户点击面板头后，Unity 会返回新的折叠状态，从而驱动下方内容的显示/隐藏。
        foldout = EditorGUILayout.BeginFoldoutHeaderGroup(foldout, "二次破碎选项");

        // 仅当面板处于展开状态时，才继续绘制各个子字段
        if (foldout)
        {
            // 子字段统一缩进一级，形成清晰的层级结构
            EditorGUI.indentLevel = 1;
            // 逐一绘制三个子字段，new GUIContent(显示名, 悬浮提示) 用于覆盖
            // 字段在检查器中的显示名称与工具提示。
            EditorGUILayout.PropertyField(enableRefracturing, new GUIContent("启用", "允许对碎片进行二次破碎。警告：此设置会产生大量碎片，启用时建议保持较低的碎片数量。")); // 是否允许对碎片进行二次破碎（会大幅增加碎片数量）
            EditorGUILayout.PropertyField(invokeCallbacks, new GUIContent("调用回调", "如果二次破碎时也要调用回调函数，请启用。")); // 二次破碎过程中是否同样触发回调
            EditorGUILayout.PropertyField(maxRefractureCount, new GUIContent("最大二次破碎次数", "碎片可被再次破碎的最大次数。")); // 碎片可被再次破碎的最大次数限制
        }

        // 结束折叠面板头组的绘制
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
