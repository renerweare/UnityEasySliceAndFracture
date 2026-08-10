using UnityEditor;
using UnityEngine;

/// <summary>
/// 切割选项（SliceOptions）的自定义属性绘制器（PropertyDrawer）。
/// 作用：当某个序列化字段的类型为 SliceOptions 时，Unity 检查器（Inspector）会调用本绘制器，
/// 以「切片选项」折叠面板的形式展示并编辑该结构体下的各个子字段，
/// 把零散的选项组织成一个可展开/折叠的 UI 分组，保持检查器界面整洁、避免字段过多刷屏。
/// </summary>
[CustomPropertyDrawer(typeof(SliceOptions))]
public class SliceOptionsPropertyDrawer : PropertyDrawer
{   
    // 静态折叠状态：记录「切片选项」面板当前是展开（true）还是折叠（false）。
    // 声明为 static 后，场景中所有同类字段共享同一个折叠状态（与 Unity 官方示例行为一致）。
    private static bool foldout = true;

    // 覆写 PropertyDrawer.OnGUI：在检查器中绘制该属性的完整 UI。
    // 注意：由于本实现完全使用 EditorGUILayout 自动布局，position 参数实际未被使用。
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        // 先复位缩进层级为 0，避免继承到外部检查器残留的缩进状态
        EditorGUI.indentLevel = 0;
        // 开始绘制带箭头图标的折叠面板头，并把返回值写回 foldout：
        // 用户点击面板头后，Unity 会返回新的折叠状态，从而驱动下方内容的显示/隐藏。
        foldout = EditorGUILayout.BeginFoldoutHeaderGroup(foldout, "切片选项");

        // 仅当面板处于展开状态时，才继续绘制各个子字段
        if (foldout)
        {
            // 子字段统一缩进一级，形成清晰的层级结构
            EditorGUI.indentLevel = 1;

            // 逐一绘制 SliceOptions 结构体的各个序列化子字段。
            // FindPropertyRelative(字段名) 用于按名称查找当前属性下的相对子属性，
            // new GUIContent(显示名, 悬浮提示) 用于覆盖字段在检查器中的显示名称与工具提示。
            EditorGUILayout.PropertyField(property.FindPropertyRelative("enableReslicing"), new GUIContent("允许二次切割", "允许对碎片进行二次切割。")); // 是否允许对碎片进行二次切割
            EditorGUILayout.PropertyField(property.FindPropertyRelative("maxResliceCount"), new GUIContent("最大二次切割次数", "碎片可被再次切割的最大次数。")); // 碎片可被再次切割的最大次数限制
            EditorGUILayout.PropertyField(property.FindPropertyRelative("detectFloatingFragments"), new GUIContent("检测漂浮碎片", "切割非凸网格时启用对“漂浮”碎片的检测。该设置对凸网格无效，应保持禁用。")); // 是否检测并分离切割后"漂浮"的碎片
            EditorGUILayout.PropertyField(property.FindPropertyRelative("insideMaterial"), new GUIContent("内部面材质", "用于内部面的材质")); // 切割产生的新内部面所使用的材质
            EditorGUILayout.PropertyField(property.FindPropertyRelative("textureScale"), new GUIContent("纹理缩放", "应用于纹理坐标的缩放系数")); // 应用到内部面 UV 坐标上的缩放系数
            EditorGUILayout.PropertyField(property.FindPropertyRelative("textureOffset"), new GUIContent("纹理偏移", "应用于纹理坐标的偏移量")); // 应用到内部面 UV 坐标上的偏移量
            EditorGUILayout.PropertyField(property.FindPropertyRelative("invokeCallbacks"), new GUIContent("调用回调", "如果二次切割时也要调用回调函数，请启用。")); // 二次切割过程中是否同样触发回调
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
