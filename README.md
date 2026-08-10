# EasySliceAndFracture

一个基于 **OpenFracture** 改造的 Unity 物体切割 / 破碎组件包。

> 版权声明：本资源改编自 [OpenFracture]（MIT License）。
> 在保留原插件核心网格切分算法的基础上，将 Slice（切割）与 Fracture（破碎）合并为**一个组件**，
> 增加了「接触即切割」的刀刃系统，并对编辑器界面做了完整汉化。

---

## 功能特性

- **一个组件搞定切割 + 破碎**：`SliceAndFracture` 同时支持切割与破碎，无需再挂两个脚本
- **三种功能模式**：
  - `仅切割`：物体可被刀切割，不会破碎
  - `仅破碎`：物体只能碰撞破碎，刀切不动它
  - `切割并破碎`：既能切又能破（默认）
- **接触式刀刃切割器** `BladeSlicer`：挂在刀刃（plane）上，接触物体即可切割，无需按键
  - 支持冷却时间（CD）
  - 支持单次挥刀最多切割数量
  - 可选手动切割按键
  - 切割平面沿刀刃精确生成（Scene 中有绿色平面预览）
- **能力随碎片传递**：破碎产生的碎片可继续被切割，切割产生的切片可继续破碎（最多嵌套 4 层，防止无限递归）
- **切面 / 破碎面分别着色**：切割面与破碎面使用各自的内部材质（最多 3 个材质槽），旧切面不会被新切面的颜色覆盖
- **编辑器界面全中文**：所有选项标签、悬停提示、按钮均为中文

---

## 目录结构

```
Assets/EasySliceAndFracture/
├── README.md
├── Editor/                                  # 编辑器专用脚本（Inspector 面板绘制器）
│   ├── SliceOptionsPropertyDrawer.cs        # 切割选项面板
│   ├── FractureOptionsPropertyDrawer.cs     # 破碎选项面板
│   ├── RefractureOptionsPropertyDrawer.cs   # 二次破碎选项面板
│   ├── TriggerOptionsPropertyDrawer.cs      # 触发选项面板
│   └── CallbackOptionsPropertyDrawer.cs     # 回调选项面板
└── Scripts/
    ├── SliceAndFracture.cs                  # ★ 核心组件：切割 + 破碎（挂在可切/可破物体上）
    ├── BladeSlicer.cs                       # ★ 刀刃切割器（挂在刀刃上，接触即切）
    ├── Options/                             # 各选项配置类（Inspector 中显示为折叠面板）
    │   ├── SliceOptions.cs                  # 切割选项
    │   ├── FractureOptions.cs               # 破碎选项
    │   ├── RefractureOptions.cs             # 二次破碎选项
    │   ├── TriggerOptions.cs                # 破碎触发选项（碰撞 / 触发器 / 键盘）
    │   └── CallbackOptions.cs               # 回调事件选项
    ├── Fragment/                            # 网格切分核心算法（改编自 OpenFracture）
    │   ├── Fragmenter.cs                    # 切分调度：负责把网格切成 N 个碎片 / 两片切片
    │   ├── MeshSlicer.cs                    # 平面切分：按切割面把网格一分为二
    │   ├── FragmentData.cs                  # 切分过程中的网格数据结构
    │   ├── MeshVertex.cs                    # 顶点数据（位置 / 法线 / UV）
    │   ├── Quad.cs                          # 四边形结构（三角剖分辅助）
    │   ├── EdgeConstraint.cs                # 边约束（保证切面边缘闭合）
    │   ├── TriangulationPoint.cs            # 三角剖分点
    │   ├── Triangulator.cs                  # 简单三角剖分
    │   └── ConstrainedTriangulator.cs       # 受约束三角剖分（填充切面）
    └── Utilities/                           # 工具类
        ├── BinSort.cs                       # 箱式排序
        ├── MathUtils.cs                     # 数学工具
        ├── MeshUtils.cs                     # 网格工具（连通域检测等）
        └── Vector3Extensions.cs             # Vector3 扩展方法
```

---

## 快速上手

### 1. 可切割 / 可破碎物体

给物体挂上 **Slice And Fracture** 组件（会自动要求 MeshFilter / MeshRenderer / Rigidbody）：

1. **Mode（模式）**：选 `切割并破碎`（或按需选 `仅切割` / `仅破碎`）
2. **Slice Options（切割选项）**：
   - `Inside Material`：切割面材质（如绿色）
3. **Fracture Options（破碎选项）**：
   - `Fragment Count`：破碎成多少个碎片（默认 10）
   - `Inside Material`：破碎面材质（如橙色）
4. **Trigger Options（触发选项）**：选触发破碎的方式
   - `碰撞`：碰撞力超过 `Minimum Collision Force` 即破碎（可勾选按标签过滤）
   - `触发器`：进入触发器区域即破碎
   - `键盘`：按下指定按键即破碎

### 2. 刀刃

给刀刃（plane）物体挂上 **Blade Slicer**：

1. 刀刃的碰撞体勾选 **Is Trigger**（如 MeshCollider 需同时把 Rigidbody 设为 **Is Kinematic**，避免"非凸网格碰撞体不能用于动态刚体"的报错）
2. `Cut On Contact`：勾选后接触物体即可切割
3. 按需调整 `Cooldown`（挥刀冷却）、`Max Cut Count`（单次最多切几个）
4. 选中刀刃时，Scene 视图中绿色方框即切割平面预览，黄色射线为切割法线

---

## 配置详解

### SliceAndFracture（核心组件）

| 字段 | 说明 |
|---|---|
| `Mode` | 功能模式：仅切割 / 仅破碎 / 切割并破碎 |
| `Slice Options` | 切割参数：允许二次切割、最大二次切割次数、内部面材质、纹理缩放/偏移 |
| `Fracture Options` | 破碎参数：碎片数量、破碎轴向、检测漂浮碎片、异步破碎、内部面材质 |
| `Refracture Options` | 二次破碎参数：是否允许碎片再次破碎、最大次数、是否调用回调 |
| `Trigger Options` | 破碎触发方式与条件 |
| `Callback Options` | 破碎触发时 / 破碎切割完成时的回调事件 |

### BladeSlicer（刀刃）

| 字段 | 说明 |
|---|---|
| `Cut On Contact` | 接触物体即可切割（默认开） |
| `Cut Key` | 可选手动切割按键（None 表示不用） |
| `Cooldown` | 挥刀冷却时间（秒），切满一刀后需等待 |
| `Max Cut Count` | 单次挥刀最多切割的物体数量 |
| `Swing Duration` | 挥刀最长持续时间，超时自动重置计数 |
| `Cut Normal Axis` | 切割平面法线方向（局部坐标，默认 +Y） |

---

## 与 OpenFracture 的差异

| 项目 | OpenFracture | 本资源 |
|---|---|---|
| 组件 | 切割用 `Slice`，破碎用 `Fracture`，各挂一个 | 合并为 `SliceAndFracture` 一个组件 |
| 切割触发 | 需要按键 + 射线检测 | `BladeSlicer` 接触即切，含 CD 与数量限制 |
| 碎片能力 | 破碎的碎片默认不可切割 | 碎片可切割、切片可破碎，双向互通（最多 4 层） |
| 切面颜色 | 再次切割会覆盖旧切面颜色 | 最多 3 个材质槽，旧切面颜色保留 |
| 界面语言 | 英文 | 全中文 |

---

## 已知限制

- 交替操作（破碎 → 切割 → 破碎 → 切割）超过 2 层后，更早的内面颜色可能被新操作的颜色覆盖（受限于最多 3 个材质槽）
- 使用 `仅破碎` 模式的物体不会被刀刃切割（符合预期）

---

## 许可

本资源改编自 [OpenFracture](https://github.com/dgreenheck/OpenFracture)，遵循其 **MIT License**。
其余新增代码（合并组件、刀刃系统、汉化）同样以 MIT 许可发布。
