using UnityEditor;
using UnityEngine;

/// <summary>
/// 回调选项（CallbackOptions）的自定义属性绘制器（PropertyDrawer）。
/// 作用：当某个序列化字段的类型为 CallbackOptions 时，Unity 检查器（Inspector）会调用本绘制器，
/// 以「回调选项」折叠面板的形式展示并编辑该结构体下的各个子字段。
/// 该面板用于在检查器中为破碎/切割过程绑定两个 UnityEvent 回调
/// （破碎触发时 onFracture、过程完成时 onCompleted）。
/// </summary>
[CustomPropertyDrawer(typeof(CallbackOptions))]
public class CallbackOptionsPropertyDrawer : PropertyDrawer
{   
    // 静态折叠状态：记录「回调选项」面板当前是展开（true）还是折叠（false）。
    // 声明为 static 后，场景中所有同类字段共享同一个折叠状态（与 Unity 官方示例行为一致）。
    private static bool foldout = true;

    // 覆写 PropertyDrawer.OnGUI：在检查器中绘制该属性的完整 UI。
    // 注意：由于本实现完全使用 EditorGUILayout 自动布局，position 参数实际未被使用。
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        // 开始绘制带箭头图标的折叠面板头，并把返回值写回 foldout：
        // 用户点击面板头后，Unity 会返回新的折叠状态，从而驱动下方内容的显示/隐藏。
        // （本面板没有像其它面板那样先复位 indentLevel，因此会保留调用处的缩进层级）
        foldout = EditorGUILayout.BeginFoldoutHeaderGroup(foldout, "回调选项");

        // 仅当面板处于展开状态时，才继续绘制各个子字段
        if (foldout)
        {
            // 绘制两个回调事件字段：
            // FindPropertyRelative(字段名) 用于按名称查找当前属性下的相对子属性，
            // new GUIContent(显示名, 悬浮提示) 用于覆盖字段在检查器中的显示名称与工具提示。
            EditorGUILayout.PropertyField(property.FindPropertyRelative("onFracture"), new GUIContent("破碎触发时", "当触发破碎时调用此回调。切割和预破碎不会调用。")); // 破碎被触发的那一刻调用的回调（切割与预破碎不触发）
            EditorGUILayout.PropertyField(property.FindPropertyRelative("onCompleted"), new GUIContent("完成时", "当破碎/切割过程完成时调用此回调。")); // 整个破碎/切割过程完成时调用的回调
        }
        
        // 结束折叠面板头组的绘制
        EditorGUILayout.EndFoldoutHeaderGroup();
    }
    
    // 让 Unity 认为本属性的默认绘制高度为 0（关键技巧，原理如下）：
    // OnGUI 内部完全依赖 EditorGUILayout 的自动布局（Auto Layout）来安排控件位置，
    // 控件的高度与纵向位置由布局系统在绘制时自行计算并推进。
    // 若 GetPropertyHeight 返回非零值，Unity 会在该属性顶部预先留出对应高度的空白，
    // 再调用 OnGUI 进行自动布局，最终导致面板上方出现一段多余的空隙；
    // 返回 0 即可消除这段多余空间（代价是无法使用 position 参数手动布局）。
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label) { return 0; }
}
