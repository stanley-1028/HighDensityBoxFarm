# Art contract — HDBF v2 3×3×1 indoor box farm `.timbermesh` models

> **状态**：v2.3 / **ART-05「真实原版材质贴图 patch」修复**交付（2026-10-27），离线验证完成；**未实机验证**。
> **适用**：模组 2.2.0 / v2 美术卡 + `art/references/` 升级卡（REQ-V2-BLD-01/03/04/06、REQ-V2-ART-01..04）。
> **本次修复（ART-05）针对用户 2026-10-27 实机截图缺陷**：四档模型表面呈**棋盘/拼布块**、顶级出现**黄黑警示条纹**。
> 根因：ART-04 的 flat-patch UV 假设所有原版材质共用一张 2048 ART-01 图集；实际每个原版
> `MaterialCollection` 材质有自己的 1024² `_D` 反照率贴图 + 材质 `_Color` 染色。本版把
> **每个材质的真实贴图与其低方差代表区域 patch** 作为唯一 UV 契约（`art/material_texture_map.json`），
> 预览/图标也改为采样同一批真实贴图，不再使用自造图集。几何/外包络/平屋顶/端口/无 `#Slot#` 契约不变。

> **文件名保留说明。** `data/ART_PLACEHOLDER.md` 被 `tools/validate`、`docs/REQ_TRACEABILITY.md`、
> `docs/ARCHITECTURE.md`、`docs/OPS.md` 引用；改名会造成悬空引用。文件名是历史遗留，内容早已不是占位美术。

---

## 0. 机器可读契约（`ART_CONTRACT`）

以下每一行都是数据契约的一部分（`tools/validate` + L1 测试按**整行精确匹配**断言，不要重排/改写）：

```text
ART_CONTRACT: envelope=3.00x1.00x3.00
ART_CONTRACT: roof-top=1.00
ART_CONTRACT: roof=empty-flat-platform
ART_CONTRACT: slots=none
ART_CONTRACT: slot-markers=0
ART_CONTRACT: ports=#Port#Power,#Port#Pipe,#Port#Logistics
ART_CONTRACT: material=flat-patch
ART_CONTRACT: ground-crops-hidden=BUILD-05
```

> `ART_CONTRACT: material=flat-patch` 自 ART-05 起语义为：**同一材质的每个面都映射到该材质真实
> `_D` 贴图上的同一个固定 patch**（"flat patch" 指“一个材质一个固定采样区”，不再指 ART-01 的 2048 图集）。
> 材质名仍是**当前阵营的原版 `MaterialCollection` 材质资产名**；`PaintedMetal.*`（整张黄黑/蓝黑警示条
> 贴图）被列入禁用清单，不得用于外壳（见 §6）。

---

## 1. ART-05 变更摘要（相对 v2.2）

| 项 | v2.2（ART-04） | v2.3（ART-05，本版） |
|---|---|---|
| UV 基准 | 假设所有原版材质共用 2048² ART-01 图集，patch 是图集像素矩形 | **每个材质用自己的真实 1024² `_D` 贴图**；patch 是该贴图上的低方差代表区域（`art/material_texture_map.json`） |
| 材质来源真相 | 按材质名猜图集内容 | 从**游戏内真实 Material 资产**读出 `_MainTex` + `_Color`（AssetStudio 枚举 + `Timberborn.PrefabOptimization` IL 交叉验证，见 §6.1） |
| 面 UV | 落在图集任意内容 → 实机棋盘/拼布 | 同材质所有面落在同一真实贴图 patch 的确定性子矩形内（每面逐点断言在 patch 内） |
| Advanced 外壳 | `PaintedMetal.*`（**整张黄黑/蓝黑警示条**） | `BaseMetal.Folktails`（橄榄灰烤漆钢板）/ `BaseMetal.IronTeeth`（蓝灰钢板）；**PaintedMetal 完全禁用** |
| Top 外壳 | `BaseMetal.*` | `Details.*`（深色厚板/铆接件区域）+ `BaseMetal.Folktails`（钢板强调）/ `IrregularPlanks_MetalForged.IronTeeth`（锻造铜/钢板强调） |
| Basic / Intermediate | 深棕木板 / 蜂蜜木板 + 石膏 + 玻璃 | 同档位语义，但改用低方差代表 patch：`BaseWood_Brown`（深棕木纹）/ `BaseWood_LightBrown`（蜂蜜木）/ `BaseWood_Grey` + `Plaster_White/Orange` |
| 预览/图标 | 用 `art/gen_textures.py` 自造 2048 伪图集渲染 → “预览好看、实机崩” | **直接加载真实 `_D` PNG + 材质 `_Color` 染色**（`art/preview_materials.py`），与实机采样一致 |
| 验证器 | C7 检查 UV 在 ART-04 role patch 内 | C7/C11 检查 UV 在**材质真实 patch** 内；C10/C12 材质贴图映射覆盖；C11/C13 从真实 PNG 复测 patch 的 mean RGB/std；C12/C14 黄黑警示材质禁用；负向对照 3/3 必须 FAIL |

---

## 2. 模型清单（实测 values；资产路径冻结）

| Tier | Faction | Kind | `TimbermeshSpec.Model` 资产路径（无扩展名） | 文件（`data/Buildings/Food/BoxFarm/`） | Bytes | Tris | Vertices |
|---|---|---|---:|---:|---:|---:|
| Basic | Folktails | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Basic.Folktails.Model` | `BoxFarm.Basic.Folktails.Model.timbermesh` | 137112 | 5850 | 13662 |
| Basic | IronTeeth | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Basic.IronTeeth.Model` | `BoxFarm.Basic.IronTeeth.Model.timbermesh` | 131612 | 5850 | 13638 |
| Intermediate | Folktails | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Intermediate.Folktails.Model` | `BoxFarm.Intermediate.Folktails.Model.timbermesh` | 131560 | 5854 | 13554 |
| Intermediate | IronTeeth | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Intermediate.IronTeeth.Model` | `BoxFarm.Intermediate.IronTeeth.Model.timbermesh` | 128163 | 5854 | 13564 |
| Advanced | Folktails | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Advanced.Folktails.Model` | `BoxFarm.Advanced.Folktails.Model.timbermesh` | 133058 | 5850 | 13539 |
| Advanced | IronTeeth | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Advanced.IronTeeth.Model` | `BoxFarm.Advanced.IronTeeth.Model.timbermesh` | 133106 | 5850 | 13549 |
| Top | Folktails | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Top.Folktails.Model` | `BoxFarm.Top.Folktails.Model.timbermesh` | 131620 | 5918 | 13610 |
| Top | IronTeeth | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Top.IronTeeth.Model` | `BoxFarm.Top.IronTeeth.Model.timbermesh` | 131781 | 5918 | 13620 |
| Basic | Folktails | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Basic.Folktails.ConstructionStage0.Model` | `BoxFarm.Basic.Folktails.ConstructionStage0.Model.timbermesh` | 22810 | 864 | 2166 |
| Basic | IronTeeth | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Basic.IronTeeth.ConstructionStage0.Model` | `BoxFarm.Basic.IronTeeth.ConstructionStage0.Model.timbermesh` | 22810 | 864 | 2166 |
| Intermediate | Folktails | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Intermediate.Folktails.ConstructionStage0.Model` | `BoxFarm.Intermediate.Folktails.ConstructionStage0.Model.timbermesh` | 22821 | 864 | 2166 |
| Intermediate | IronTeeth | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Intermediate.IronTeeth.ConstructionStage0.Model` | `BoxFarm.Intermediate.IronTeeth.ConstructionStage0.Model.timbermesh` | 22817 | 864 | 2166 |
| Advanced | Folktails | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Advanced.Folktails.ConstructionStage0.Model` | `BoxFarm.Advanced.Folktails.ConstructionStage0.Model.timbermesh` | 22815 | 864 | 2166 |
| Advanced | IronTeeth | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Advanced.IronTeeth.ConstructionStage0.Model` | `BoxFarm.Advanced.IronTeeth.ConstructionStage0.Model.timbermesh` | 22812 | 864 | 2166 |
| Top | Folktails | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Top.Folktails.ConstructionStage0.Model` | `BoxFarm.Top.Folktails.ConstructionStage0.Model.timbermesh` | 22808 | 864 | 2166 |
| Top | IronTeeth | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Top.IronTeeth.ConstructionStage0.Model` | `BoxFarm.Top.IronTeeth.ConstructionStage0.Model.timbermesh` | 22806 | 864 | 2166 |

合计：**1,240,511 bytes / 53,856 tris / 126,064 vertices**（成品 8 档 1,098,512 B / 46,944 tris；施工 8 档 141,999 B / 6,912 tris）。
每档 LOD0 三角预算 6,000，最高 **5,918**。每个文件 **4 个节点**：1 root mesh + 3 个端口空节点；**0 个 `#Slot#`**。
`art/timbermesh/*.timbermesh` 与 `data/Buildings/Food/BoxFarm/*.timbermesh` 逐字节一致（16/16 SHA-256 相同，见 `art/timbermesh/export-determinism.txt`）：

```text
BoxFarm.Advanced.Folktails.Model                           96476b32d104f0fba82f933fecee95f73a6caa6b9e9b3b86b01325b8038ef648
BoxFarm.Advanced.IronTeeth.Model                           77225b3cd13f654b7ea0956fbc1dbb3d69044042430bee199a73fae25295cf35
BoxFarm.Basic.Folktails.Model                              856faee1468fea35f2cb4859bc5690f380168a55978b81e60caf137bce3cdbea
BoxFarm.Basic.IronTeeth.Model                              5ac0175f04ff63f32548aa45abb2a320ed0b1c7a4ea9bf4f7fc6627f4012d18e
BoxFarm.Intermediate.Folktails.Model                       bdc0a19b34065e1d30b441f35f28c3d4cbe6d7386a4b968c6faedb12b35d213f
BoxFarm.Intermediate.IronTeeth.Model                       13adc447cf9baa71e80b91ea5b911d41b232ba5adbb74cd6db91a1adcf100c0d
BoxFarm.Top.Folktails.Model                                34ae271a77842cd5023dca3b197818a35380bfba71a9e3fc777ec316ba0e5f75
BoxFarm.Top.IronTeeth.Model                                61bed1f2277395c54639ed81c0a1c4baaf90820757767eb9ab25479daac70955
BoxFarm.Advanced.Folktails.ConstructionStage0.Model        8d4aebf8ab988084e4e81d2feca95c32cb575ebc376715e1b2fa88fc8233bed5
BoxFarm.Advanced.IronTeeth.ConstructionStage0.Model        0a9a8634d15378ce6b6c3b53ed1c3aa7f2809e148445feda20e61c338e9c4c7c
BoxFarm.Basic.Folktails.ConstructionStage0.Model           91081dd74171ee15cf697e3fb8303650f7179360f85464a3642ee4a0d110e607
BoxFarm.Basic.IronTeeth.ConstructionStage0.Model           a0b851bf9d3f13cbc31e03e8a054d821920d2b1d505c6cf23ebba7d0d89f81d3
BoxFarm.Intermediate.Folktails.ConstructionStage0.Model    df6219f2b57029c1fd288101b2db2bb611a54128782908355942b4b4a6b5b694
BoxFarm.Intermediate.IronTeeth.ConstructionStage0.Model    2c34f2b7fff184da767674d77dcf3518fb46b5bae031c85dc1942703857c37a7
BoxFarm.Top.Folktails.ConstructionStage0.Model             041c6e38bf8a18e0107d2a6ca16dabf0e719eaf86906e5582bfea263e8511a84
BoxFarm.Top.IronTeeth.ConstructionStage0.Model             b1de924ea7dcbcab7d6f09d4a44c959d684e40002d652ce12986c30ebc09b4f5
```

---

## 3. 外包络：严格 3.00 × 1.00 × 3.00（实测）

`.timbermesh` 内部是游戏 block-corner 模型空间（X 宽、Y 高、Z 深；正面 Z=0）：
`export = contract + (1.5, 0, 1.5)`，因此文件量测必须是 **min=(0,0,0)、max=(3,1,3)**。
`art/validate_timbermesh.py` 的 **C3** 对 16/16 文件实测：

```text
size=3.000/1.000/3.000  min=-0.000/-0.000/-0.000  (tolerance +-0.02)   16/16 PASS
```

8 个成品模型（asset/contract space，FBX 与 GLB 双检，`art/validate_models.py` C2）：

| 模型 | SIZE X | SIZE Y | SIZE Z | MIN (X,Y,Z) |
|---|---:|---:|---:|---|
| Basic.Folktails | 3.0000 | 1.0000 | 3.0000 | (-1.500, 0.000, -1.500) |
| Basic.IronTeeth | 3.0000 | 1.0000 | 3.0000 | (-1.500, 0.000, -1.500) |
| Intermediate.Folktails | 3.0000 | 1.0000 | 3.0000 | (-1.500, 0.000, -1.500) |
| Intermediate.IronTeeth | 3.0000 | 1.0000 | 3.0000 | (-1.500, 0.000, -1.500) |
| Advanced.Folktails | 3.0000 | 1.0000 | 3.0000 | (-1.500, 0.000, -1.500) |
| Advanced.IronTeeth | 3.0000 | 1.0000 | 3.0000 | (-1.500, 0.000, -1.500) |
| Top.Folktails | 3.0000 | 1.0000 | 3.0000 | (-1.500, 0.000, -1.500) |
| Top.IronTeeth | 3.0000 | 1.0000 | 3.0000 | (-1.500, 0.000, -1.500) |

所有装饰、开窗、收边、屋顶框梁、苔藓、补丁板、端口件都在包络内；生成器 `art/gen_models_v2.py`
对每个模型做硬断言（任何越界直接令生成失败，不会静默产出）。

---

## 4. 屋顶：单一平坦空平台（几何契约 + 实测）

冻结规则（`art/atlas_layout.py` 常量 + 两个验证器共同执行）：

* 屋顶是**一个**平的、空的平台；**走行面 = 模型最高面 y = 1.00**（每档/LOD/阵营相同）；
* **顶面之上没有任何几何**；顶面以上 30 cm 体积为空（**可叠放**，REQ-V2-BLD-03/04、REQ-V2-ART-01）；
* 平台**平面**按档位 2.84 / 2.92 / 2.96 / 2.98（Basic→Top），平面尺寸仍参与 50% 可辨（C9 `plan`）；
* 平台下的**屋顶框梁/收边全部低于 1.00**：周边框梁顶 0.95（平台底），平台顶面四周为**与走行面同高**的
  框架边带（frame 材质，顶 1.00，**凸出顶面 0.000 m**）；跨边收边 ≤0.04 且不阻断叠放；
* 顶面 **板缝/板格** 通过“板条顶=1.00 + 缝内基板 0.990”表达（缝隙低于顶面，不是凸起）；
* 屋顶上**没有**作物槽、土壤、通风塔、管道、法兰、天线、设备或任何高于 1.00 的装饰。

实测证据（`art/validation-report.txt` 的 “FLAT ROOF EVIDENCE” 段，25/25 源模型）：
**TOP = 1.0000、APEX = 1.0000、ABOVE_TOP = 0**，TOP_VERTS = 108–564（顶面存在且覆盖平台平面）。
`art/validate_timbermesh.py` **C6** 对 16/16 文件额外断言：走行面顶点存在、顶面横跨平台平面、
**y > 1.00 的顶点数 = 0**（≥30 cm 净空由“顶点严格不高于 1.00”直接推出）。
**浮点说明（诚实）**：导出为 float32，走行面实测最大 y = **1.0000002384**（超过 1.0 约 2.4e-7，即 2 ulp；
这些顶点全部是平台走行面本身、都在平台平面内），最低点 y = −2.4e-7（基础板底面）。
冻结容差为 `above_top ±1e-6`、apex ±0.005、包络 ±0.02，实测偏差比这些阈值小 4–8 万倍，两个验证器都判 PASS；
“字面上严格等于 1.00/0.00”在 float32 下不成立，这是已知且被容差覆盖的表达限制，不是几何溢出。

| Tier | 平台平面 X × Z |
|---|---|
| Basic | 2.84 × 2.84 |
| Intermediate | 2.92 × 2.92 |
| Advanced | 2.96 × 2.96 |
| Top | 2.98 × 2.98 |
| ConstructionStage0 | 2.90 × 2.90 |

---

## 5. 空节点：恰好 3 个端口（0 个 `#Slot#`）

模型里唯一的空节点是 3 个 `#Port#` 标记；**没有任何 `#Slot#` / `#Slot#Crop_*` 节点**，
也没有槽体/土壤/作物实体。8 个蓝图的根节点仍**不含** `TransformSlotInitializerSpec` /
`WorkplaceSlotManagerSpec`（自定义网格没有 Seat/Entrance 插槽，声明会在放置时抛异常）。

| 空节点 | contract space（中心轴心） | 游戏 export space（block-corner） | 朝外 | 语义 |
|---|---|---|---|---|
| `#Port#Power` | (1.00, 0.85, 1.50) | (2.50, 0.85, 3.00) | +Z | 背面右侧接线盒/绝缘子（Top 外露法兰，低档盖板） |
| `#Port#Pipe` | (−1.50, 0.40, 0.00) | (0.00, 0.40, 1.50) | −X | 左侧短边中心法兰（低档木盖板；高级/Top 带螺栓环） |
| `#Port#Logistics` | (−1.00, 0.35, 1.50) | (0.50, 0.35, 3.00) | +Z | 背面左侧输送带开口（Basic 空白盖板，其余档带滚轮） |

实测：`art/validate_timbermesh.py` C6 对 16/16 文件：**3/3 端口存在且唯一、位置偏差 0.0000**
（容差 0.02）、vertexCount=0、无 mesh、**0 个 `#Slot#`**。源模型（FBX/GLB）中端口空节点位置与
local +Z 朝向由 `art/validate_models.py` C5 双检。

---

## 6. 材质：原版 `MaterialCollection` 名称 + 真实贴图 patch（ART-05 核心）

### 6.1 材质 → 真实贴图的技术事实（不是猜测）

* 游戏按名字从**当前阵营**的 `MaterialCollection` 解析材质（`Timberborn.TimbermeshMaterials.MaterialRepository`）；
  材质名不带自订 PBR / AssetBundle。
* 每个材质有**自己的** `_MainTex`（1024×1024 `_D` 反照率 PNG）与材质 `_Color` 染色。
  本表由**游戏安装目录的真实 Material 资产**读出（AssetStudioModCLI 枚举 `resources.assets` 的 Material +
  自写 probe 解析 `m_SavedProperties._MainTex / _Color`，2026-10-27）。
* 运行时 UV 语义（`Timberborn.PrefabOptimization.AutoAtlaser` / `AutoAtlasingPrefabOptimizer` IL 实测）：
  阵营 auto-atlas 会把网格 UV 重映射为 `uv_atlas = uv * (1/sizeMultiplier) + cell_offset`，即
  **材质自己贴图内的归一化位置被保留**；不在 auto-atlas 里的材质直接 1:1 采样；`_Color` 乘到采样结果上。
  因此 patch 用“该材质贴图的归一化 UV 矩形”表达即可在实机得到正确采样。

### 6.2 四档材质映射（`art/timbermesh_common.py MATERIAL_MAP`，权威）

| Tier | Faction | 槽位 → 原版材质 | 视觉意图 |
|---|---|---|---|
| Basic | Folktails | `M_HDBF_Wood`=`BaseWood_Brown.Folktails`；`M_HDBF_Fabric`=`Moss.Folktails` | 深棕粗木板 + 苔藓/作物剪影 |
| Basic | IronTeeth | `M_HDBF_Wood`=`BaseWood_DarkBrown.IronTeeth`；`M_HDBF_Fabric`=`RoofPlanks.IronTeeth` | 深色木板 + 深色屋面板 |
| Intermediate | Folktails | `M_HDBF_Wood`=`BaseWood_LightBrown.Folktails`；`M_HDBF_Glass`=`WindowsAtlas.Folktails`；`M_HDBF_Fabric`=`Plaster_White.Folktails` | 蜂蜜浅木 + 奶油石膏 + 大玻璃 |
| Intermediate | IronTeeth | `M_HDBF_Wood`=`BaseWood_Grey.IronTeeth`；`M_HDBF_Glass`=`WindowsAtlas.IronTeeth`；`M_HDBF_Fabric`=`Plaster_Orange.IronTeeth` | 浅灰木 + 暖橘石膏 + 大玻璃 |
| Advanced | Folktails | `M_HDBF_Wood`=`BaseWood_White.Folktails`；`M_HDBF_Metal`=`BaseMetal.Folktails`；`M_HDBF_Glass`=`WindowsAtlas.Folktails`；`M_HDBF_Fabric`=`Details.Folktails` | 烤漆钢板壳 + 白木补丁 + 玻璃 + 细节板 |
| Advanced | IronTeeth | `M_HDBF_Wood`=`IrregularPlanks_MetalForged.IronTeeth`；`M_HDBF_Metal`=`BaseMetal.IronTeeth`；`M_HDBF_Glass`=`WindowsAtlas.IronTeeth`；`M_HDBF_Fabric`=`Details.IronTeeth` | **蓝灰钢板壳** + 玻璃幕 + 细节板 |
| Top | Folktails | `M_HDBF_Wood`=`IrregularPlanks_Brown.Folktails`；`M_HDBF_Metal`=`Details.Folktails`；`M_HDBF_Glass`=`WindowsAtlas.Folktails`；`M_HDBF_Fabric`=`BaseMetal.Folktails` | 深色厚板/铆接壳 + 钢板强调 + 玻璃 |
| Top | IronTeeth | `M_HDBF_Wood`=`BaseWood_DarkBrown.IronTeeth`；`M_HDBF_Metal`=`Details.IronTeeth`；`M_HDBF_Glass`=`WindowsAtlas.IronTeeth`；`M_HDBF_Fabric`=`IrregularPlanks_MetalForged.IronTeeth` | 深色厚板壳 + 锻造铜/钢板强调（“黄铜点缀”） + 玻璃 |
| construction（8 档） | Folktails | `M_HDBF_Wood`=`BaseWood_Brown.Folktails`；`M_HDBF_Metal`=`BaseMetal.Folktails` | 原木框架 + 裸钢板 |
| construction（8 档） | IronTeeth | `M_HDBF_Wood`=`RoofPlanks.IronTeeth`；`M_HDBF_Metal`=`BaseMetal.IronTeeth` | 深色原木框架 + 裸钢板（共享 UV 集，见 §9） |

> **PaintedMetal 禁用**：`PaintedMetal.Folktails` / `PaintedMetal.IronTeeth` 的整张 `_D` 贴图就是
> 黄黑 / 蓝黑**警示条纹**，大面积使用正是用户截图里的“黄黑顶级”。它们仍留在 `MATERIAL_WHITELIST`
> （白名单是真实 MaterialCollection 的超集），但 `MATERIAL_MAP` 断言禁止使用，验证器与 L1 测试双重禁止。

### 6.3 材质 → 真实贴图 → patch 表（权威＝`art/material_texture_map.json`）

`std` = patch 内 R/G/B 三通道总体标准差的**最大值**（sRGB 0–255 原始字节）；阈值为按材质分级预算
（wood 18 / metal 14 / glass 12 / plaster 10 / details 26 / moss 16 / roof_planks 16 / forged_planks 27，
硬上限 30）。**负向对照**：把 patch 换成 PaintedMetal 黄黑条纹区（std≈58–60）必须 FAIL。

| 材质（原版资产名） | 真实 `_D` 贴图 | `_Color` tint | patch pixel rect | patch UV rect (u0,v0,u1,v1) | mean RGB | std / max |
|---|---|---|---|---:|---:|---:|---:|
| `BaseMetal.Folktails` | `BaseMetal_D1.png` | (1.00,1.00,1.00) | 68,132,388,452 | 0.066406,0.558594,0.378906,0.871094 | (112.0,100.2,76.7) | 11.29/14.0 |
| `BaseMetal.IronTeeth` | `BaseMetal_D2.png` | (1.00,1.00,1.00) | 68,132,388,452 | 0.066406,0.558594,0.378906,0.871094 | (110.8,106.1,106.2) | 10.68/14.0 |
| `BaseWood_Brown.Folktails` | `BaseWood_1K_D_Brown.png` | (1.00,1.00,1.00) | 4,756,260,1012 | 0.003906,0.011719,0.253906,0.261719 | (54.5,40.4,27.0) | 17.73/18.0 |
| `BaseWood_DarkBrown.IronTeeth` | `BaseWood_1K_D_Grey.png` | (0.3113,0.2673,0.2071) | 364,308,508,452 | 0.355469,0.558594,0.496094,0.699219 | (135.7,129.9,121.0) | 17.93/18.0 |
| `BaseWood_Grey.IronTeeth` | `BaseWood_1K_D_Grey.png` | (0.6226,0.5796,0.5257) | 364,308,508,452 | 0.355469,0.558594,0.496094,0.699219 | (135.7,129.9,121.0) | 17.93/18.0 |
| `BaseWood_LightBrown.Folktails` | `BaseWood_1K_D_White.png` | (0.6604,0.5552,0.4081) | 20,244,212,436 | 0.019531,0.574219,0.207031,0.761719 | (148.4,135.1,117.5) | 14.47/18.0 |
| `BaseWood_White.Folktails` | `BaseWood_1K_D_White.png` | (0.8868,0.8641,0.8324) | 20,244,212,436 | 0.019531,0.574219,0.207031,0.761719 | (148.4,135.1,117.5) | 14.47/18.0 |
| `Details.Folktails` | `Details_D.Folktails.png` | (1.00,1.00,1.00) | 100,748,356,1004 | 0.097656,0.019531,0.347656,0.269531 | (105.3,80.5,48.3) | 23.73/26.0 |
| `Details.IronTeeth` | `Details_D.IronTeeth.png` | (1.00,1.00,1.00) | 196,748,452,1004 | 0.191406,0.019531,0.441406,0.269531 | (89.2,75.0,58.0) | 25.40/26.0 |
| `IrregularPlanks_Brown.Folktails` | `IrregularPlanks_D_Brown.png` | (1.00,1.00,1.00) | 4,204,260,460 | 0.003906,0.550781,0.253906,0.800781 | (51.0,38.0,26.2) | 17.88/18.0 |
| `IrregularPlanks_MetalForged.IronTeeth` | `PlanksWithMetal_D.png` | (1.00,1.00,1.00) | 636,372,860,596 | 0.621094,0.417969,0.839844,0.636719 | (94.9,73.7,57.6) | 26.26/27.0 |
| `Moss.Folktails` | `Moss_D.Folktails.png` | (1.00,1.00,1.00) | 188,292,444,548 | 0.183594,0.464844,0.433594,0.714844 | (43.2,48.6,20.9) | 13.10/16.0 |
| `Plaster_Orange.IronTeeth` | `Plaster_D_White.IronTeeth.png` | (0.8353,0.4824,0.2588) | 468,4,788,324 | 0.457031,0.683594,0.769531,0.996094 | (176.1,159.8,141.2) | 9.97/10.0 |
| `Plaster_White.Folktails` | `Plaster_D_White.Folktails.png` | (1.00,1.00,1.00) | 468,4,788,324 | 0.457031,0.683594,0.769531,0.996094 | (176.1,159.8,141.2) | 9.97/10.0 |
| `RoofPlanks.IronTeeth` | `RoofPlanks_D.png` | (1.00,1.00,1.00) | 4,756,260,1012 | 0.003906,0.011719,0.253906,0.261719 | (37.9,39.6,30.4) | 13.55/16.0 |
| `WindowsAtlas.Folktails` | `WindowsAtlas_D.Folktails.png` | (1.00,1.00,1.00) | 828,772,1020,964 | 0.808594,0.058594,0.996094,0.246094 | (122.3,133.7,139.7) | 11.54/12.0 |
| `WindowsAtlas.IronTeeth` | `WindowsAtlas_D.IronTeeth.png` | (1.00,1.00,1.00) | 828,772,1020,964 | 0.808594,0.058594,0.996094,0.246094 | (122.3,133.7,139.7) | 11.54/12.0 |

贴图根目录（只读引用，**不复制进发布包**）：`%USERPROFILE%\.hdbf\sdk\vanilla-tex-all` 与
`%USERPROFILE%\.hdbf\sdk\vanilla-tex-extra`（每张 1024×1024 8-bit RGBA PNG，sha256 记录在 JSON 中）。

patch 选择规则（`art/material_texture_analysis.py`，确定性）：在真实贴图上滑动方窗，要求
**std ≤ 该材质分级预算**、**窗内均值与材质代表色距离 ≤ 逐材质色差预算**（默认 18/255，玻璃等窄带区域单独放宽）、
并**禁止**含黄黑警示条 / 阵营横幅 logo / 画布留白 / 补板 / 板缝 / 铆接花纹窗口；在满足条件的窗口中取
“最大尺寸 + 最小 std+色差”（保证既均匀又像该材质）。同一贴图被多个材质共用时（如 `BaseWood_1K_D_White`
同时供 LightBrown/White）**共用同一个 patch**，这样 `_Color` 染色不同、采样位置相同。

### 6.4 UV 分配

* 每个面按投影尺寸映射到该材质 patch 内的一个子矩形，texel 密度固定 **32 px/grid**（真实贴图 1024²；
  等价于 ART-04 在 2048² 上的 64 px/grid，**子矩形密度保持不变**），距 patch 边 4 px 保护带；
* 生成器对每个面做**硬断言**：所有 UV 必须落在 patch `uv_rect` 内，否则生成失败（不会静默出界）；
* 同一材质的所有面都落在同一 patch 内 → 实机不会出现跨材质/跨贴图内容的棋盘。

### 6.5 诚实的限制

* patch 取的是原版贴图上的固定代表区域，不是我们绘制的 PBR；**单面颜色会随 patch 内纹理细节有轻微变化**，
  但不再跨区域跳变。
* 材质 `_Color` tint 的乘法语义来自 shader 属性 + `EnvironmentMaterialProperties` IL（`_Color` 被读取/写回）；
  预览按 sRGB 乘法近似，实机最终亮度受游戏光照/tonemap 影响，**待实机确认**。
* `BaseWood_Brown` 本身是**很深的棕色**（patch 均值约 54/40/27）；Basic 档实机会是**深棕近黑木屋**，
  这是原版材质真实颜色，不是渲染错误。FT 阵营的 `MaterialCollection` **没有**蓝灰/深灰“重钢”材质，
  因此 FT 的 Advanced 用 `BaseMetal.Folktails`（橄榄灰烤漆钢板）、Top 用 `Details.Folktails`（深色厚板/铆接件）；
  IT 的 Advanced 才真正呈现蓝灰钢板（`BaseMetal_D2`）。详见 §12。

---

## 7. 四档视觉差异（可量化）与“室内高密度”的静态表达

3×3×1 只有 1 格高，室内用**静态装饰**表达密度，不生成 600 个槽位模型、不生成作物实体：

* **3 层静态货架** + **成排作物剪影**（托盘 + 叶簇，deterministic 小件）位于前半室内；
  Basic 通过开放窗口直接可见，其余档透过玻璃可见。
* **开窗方式分档**：Basic = 两跨开放窗 + 正面中门（无玻璃）；
  Intermediate = 7 樘温室大玻璃（2×2 分格）+ 门；Advanced = 7 樘金属框玻璃幕（2×2 分格）+ 木板补丁；
  Top = 8 樘工业玻璃（前 3×3、侧 2×3 更密分格）+ 金属门 + 背部百叶。
* **玻璃占比（来自 `art/material_slots.json`）**：

  | Tier | 侧墙玻璃占比（left+right / 2×墙带面积） | 正面玻璃占比 | 玻璃可见面积 grid² |
  |---|---:|---:|---:|
  | Basic | 0.0% | 0.0% | 0.000 |
  | Intermediate | 54.0% | 41.7% | 4.026 |
  | Advanced | 53.3% | 37.7% | 3.896 |
  | Top | 53.5% | 34.8% | 3.474 |

* **50% 镜头可辨（C9）**：以 32 px/grid 多视向 material/silhouette/glass/port 栅格比较，
  **12/12 tier pair PASS**（每对至少 2/4 判据不同）。ART-05 后材质集合四档互不相同（见 §6.2），
  可辨度只增不减；实机 50% 可辨仍待截图确认。

---

## 8. 参考图采纳 / 不采纳对照（`art/references/`）

| 参考图 | 采纳 | 不采纳（与硬契约冲突） | 本版做法 |
|---|---|---|---|
| `ref-basic-folktails.jpg` | 深棕粗木板、板缝、苔藓点缀、斜撑、开放两跨、室内农产货架剪影 | 门前外伸台阶；屋顶边缘厚凸起 | 齐平门槛 + 内侧踏步；屋顶框梁收在 1.00 以下 |
| `ref-intermediate-folktails.jpg` | 蜂蜜/浅棕木框、大面积温室玻璃分格、奶油/浅色平屋顶 | 立面顶部斜玻璃；屋顶立管 | 垂直玻璃 + 平屋顶；端口在后立面/侧面、低于屋面 |
| `ref-industrial-ironteeth.jpg` | 蓝灰金属壳/框、金属分格、铆钉、木板补丁、金属门、法兰/粗管 | 屋顶边缘凸起金属边框；门前格栅台阶 | 框梁/补丁全部在 1.00 内；门槛齐平 |
| `ref-tier-lineup.jpg` | 四档材质/开窗层层递进、同尺寸同镜头、50% 可辨目标 | 第 3/4 栋仍带斜玻璃顶 | 全部平屋顶；用材质/分格/框架密度/屋顶板缝区分 |
| `ref-interior-cutaway.jpg` | 多层货架密度、成排作物剪影、暖色室内感 | 室内层高 >1 格；600 槽动态模型 | 1 格高静态 3 层货架 + 作物剪影（纯装饰） |
| `ref-construction-greybox.jpg` | 灰盒/木框架/脚手架语言、板材堆 | 脚手架伸出占地、高于 1 格 | 脚手架收进 3×3、全部 <1.00；端口节点保留 |

---

## 9. 施工阶段（REQ-V2-ART-03）

`data/Buildings/Food/BoxFarm/` 的 8 个 `*ConstructionStage0.Model.timbermesh` 由**同一份共享灰盒源模型**
（`art/models/ConstructionStage0/ConstructionStage0.fbx`，3×3×1、木框架 + 内部钢脚手架 + 半建墙体 +
同一平坦空屋顶、864 tris）导出，每 tier×faction 一份文件（路径/`Model.Name` 不同）。

**ART-05 关键点：施工阶段只有一套烘焙 UV，却要同时满足两个阵营的材质**。解决方式不是复制源模型，
而是把施工阶段的两个材质对**联合搜索到同一个 UV 窗口**（`material_texture_map.json` →
`construction_shared_patches`）：

| 槽位 | Folktails 材质 | IronTeeth 材质 | 共用 UV 窗口（两者相同） |
|---|---|---|---|
| `M_HDBF_Wood` | `BaseWood_Brown.Folktails` | `RoofPlanks.IronTeeth` | 0.003906,0.011719,0.253906,0.261719 |
| `M_HDBF_Metal` | `BaseMetal.Folktails` | `BaseMetal.IronTeeth` | 0.066406,0.558594,0.378906,0.871094 |

因此 16 个 `.timbermesh` 里：FT 施工件材质 = {`BaseWood_Brown.Folktails`, `BaseMetal.Folktails`}，
IT 施工件材质 = {`RoofPlanks.IronTeeth`, `BaseMetal.IronTeeth`}，但 UV 完全相同且各自都在自己的 patch 内。
蓝图 `Children.#Unfinished.Children.ConstructionStage0.TimbermeshSpec.Model` 路径不变。

---

## 10. 复现命令（工作区根目录；确定性、无时间戳）

沙箱约束：构建/中间产物写 `%TEMP%`；Blender 用 4.2.1 headless（`--factory-startup`）。

```powershell
$env:BLENDER_USER_CONFIG = "$env:TEMP\blender-config"
$b = 'C:\Users\user\.hdbf\sdk\blender-4.2.1-windows-x64\blender.exe'
$py = 'python'   # 任意 Python 3.11+（需 numpy/Pillow；Blender 自带 Python 也可）

# 0) ART-05 材质→真实贴图→patch 映射表（读游戏安装目录的 vanilla _D PNG，只读）
#    需要 %USERPROFILE%\.hdbf\sdk\vanilla-tex-all 与 vanilla-tex-extra（AssetStudio 解包的游戏贴图）
& $py art\material_texture_analysis.py --verify     # 复现性检查（PASS 才继续）
& $py art\material_texture_analysis.py              # 重新生成 art\material_texture_map.json
#    可选：& $py art\material_texture_analysis.py --swatches .tmp-art05\swatches（人工目检 patch）

# 1) 生成 25 个源模型（8 tier×faction × LOD0/1/2 + ConstructionStage0）+ material_slots.json
& $b -b --factory-startup -noaudio --python art\gen_models_v2.py

# 2) 验证源模型（FBX 重导入 + GLB 导入双检；C1-C14 + 负向对照；写 art/validation-report.txt）
& $b -b --factory-startup -noaudio --python art\validate_models.py

# 3) 导出 16 个 .timbermesh + export-manifest.json（exit 0 = 16/16）
& $b -b --factory-startup -noaudio --python art\export_timbermesh.py

# 4) 验证 16 个 .timbermesh（纯 Python，无需 Blender；C1-C12 + 负向对照；写 art/timbermesh-validation.txt）
& $py art\validate_timbermesh.py

# 5) 预览（真实 _D 贴图 + 材质 _Color；离线渲染，非游戏截图；双阵营 + 50% A/B）
& $b -b --factory-startup -noaudio --python art\render_previews.py

# 6) 图标（真实 _D 贴图；512px 源 + 128px 发布图标 + manifest）
& $b -b --factory-startup -noaudio --python art\render_icons.py

# 7) 部署 16 个 .timbermesh 到 data/
Copy-Item art\timbermesh\BoxFarm.*.timbermesh data\Buildings\Food\BoxFarm\ -Force

# 8) L1（含 ArtModelContractTests + V2MaterialPatchContractTests）
dotnet run --project tests\HDBF.Tests -c Release -m:1 -p:HdbfWorkRoot="$env:TEMP\hdbf-art05"

# 9) 发布门禁（44 gates）
powershell.exe -NoProfile -ExecutionPolicy Bypass -File build.ps1 -WorkRoot "$env:TEMP\hdbf-art05-gate"
```

**确定性**：

* `art/material_texture_analysis.py --verify` 重算出的 JSON 与提交版**逐字节一致**，且每个 patch 的
  mean RGB/std 都从真实 PNG 字节重新导出核对（PASS）。
* 两次完整 `gen_models_v2.py` → `export_timbermesh.py`：16/16 `.timbermesh` 与 manifest SHA-256 相同
  （`art/timbermesh/export-determinism.txt`）。
* 两份验证报告（`art/validation-report.txt`、`art/timbermesh-validation.txt`）均为**无时间戳的确定性报告**。

---

## 11. 验证摘要（本次实测，离线）

| 检查 | 命令 | 结果 |
|---|---|---|
| 材质映射表复现性 | `python art\material_texture_analysis.py --verify` | **PASS**：17 材质 / 15 真实贴图，JSON 逐字节可复现，mean RGB/std 从真实 PNG 复算一致 |
| 源模型 C1–C14（25 模型，FBX+GLB 双检） | `blender ... --python art\validate_models.py` | **OVERALL VERDICT: PASS**，exit 0（C7/C11 UV 落在真实 patch 内；C12/C13/C14 材质贴图/方差/警示材质；负向对照 3/3 被拒绝） |
| 16 个 `.timbermesh` C1–C12 + 负向对照 | `python art\validate_timbermesh.py` | **FILES: 16/16 present; 16 PASS, 0 FAIL**；**OVERALL VERDICT: PASS**，exit 0；负向对照 3/3 被拒绝 |
| `data` ↔ `art` 一致性 | SHA-256 对比 | **16/16 相同** |
| 导出确定性 | 两次 `art/export_timbermesh.py` | 16/16 `.timbermesh` + manifest SHA-256 相同 |
| 预览 | `blender ... --python art\render_previews.py` | 48 张 8-bit PNG（8 模型 × 5 视角 + 施工 2 + 6 张 A/B 合图），材质绑定 28/28 槽位真实贴图，UV 28/28 落在 patch 内 |
| 图标 | `blender ... --python art\render_icons.py` | 4×512 + 4×128 图标 + manifest；`data/Icons/BoxFarm*.png` 已替换（128×128、`.meta.json` 保留） |
| L1 数据契约 | `dotnet run --project tests\HDBF.Tests ...` | **PASSED 577 / FAILED 0**（含 `ArtModelContractTests` + 新增 `V2MaterialPatchContractTests`） |
| 发布门禁 | `build.ps1 -WorkRoot ...` | **RELEASE GATES PASSED**（44 gates） |

---

## 12. 未完成 / 显式偏差（绝不隐瞒）

1. **仍需用户实机截图确认**：本次仍是离线验证（没有部署/启动游戏/实机截图）。材质、UV、颜色都按
   **游戏内真实 Material 资产的 `_MainTex`/`_Color`** 与 **AutoAtlaser IL** 推导，但“实机看是否正确”
   必须由用户截图验收（尤其是深色 Basic 与 FT/Top 的深色厚板外壳）。
2. **FT 没有蓝灰/深灰“重钢”材质**：Folktails 的 `MaterialCollection` 里唯一的金属材质是
   `BaseMetal.Folktails`（橄榄灰烤漆钢板，`BaseMetal_D1`），没有 IronTeeth 的蓝灰钢板（`BaseMetal_D2`）。
   因此 FT 的 Advanced 是橄榄灰烤漆钢板、Top 是 `Details.Folktails` 深色厚板/铆接件 + 钢板强调；
   **“蓝灰金属+玻璃”在 IT 上才完整成立**。这是原版资产集限制，不是映射错误。
3. **PaintedMetal 保留在白名单但禁用**：它是真实原版材质（黄黑/蓝黑警示条），为保持白名单=原版集合超集
   而保留；`MATERIAL_MAP` 断言 + 两个验证器 + L1 测试都禁止它出现在任何发布模型里。
4. **Basic 实机会很暗**：`BaseWood_Brown` 的真实贴图本身是深棕（patch 均值 ~54/40/27），
   Basic 档会是“深棕近黑木屋”。这是原版材质真实颜色；如果用户认为太暗，只能改用更浅的原版木材质
   （例如 `BaseWood_LightBrown`）——那会改变“深棕”档位语义，需要用户确认。
5. **材质 `_Color` 乘法与预览 sRGB 近似**：IL 证明 `_Color` 被读取/写回，但 shader 中精确的线性/色彩空间
   处理与游戏 tonemap 未实机验证；预览按 sRGB 乘法近似，可能比游戏偏暗/偏亮。
6. **预览/图标是离线 studio 渲染**，不是游戏截图；光照（3 盏 sun + 世界环境光）与游戏昼夜/雾气不同，
   颜色以实机为准。图标/预览只用于“同一模型比四档/看轮廓”，不冒充实机截图。
7. **8 个蓝图与碰撞盒仍是旧的 3×2 足迹**（`BlockObjectSpec.Size={X:3,Y:2,Z:1}`、`CollidersSpec` 3×1×2），
   本卡按约束**不改蓝图**；实机放置仍会出现“模型 3 深、碰撞 2 深”的不一致，需蓝图卡处理。
8. **屋顶叠放 A/B**（REQ-V2-BLD-04/07）仍待实机用一座原版小建筑验证。
9. **顶点色是否被 shader 使用未知**：16/16 文件带非白 `color` 顶点属性，游戏 shader 是否消费未验证。
10. **只导出 LOD0** `.timbermesh`；LOD1/2 只存在于 `art/models/**` 源资产，游戏端依赖
    `Timberborn.PrefabOptimization` 自动生成，切换质量未实机确认。
11. **施工阶段蓝本**：`Children.#Unfinished` 仍使用 `ConstructionBase3x2#nested`（旧 3×2 建造基座），
    另一处需随蓝图卡一起改，本卡只更新灰盒模型。
12. **旧 ART-01 图集/`art/gen_textures.py` 退居预览兜底**：`art/textures/atlas_*.png` 仍在仓库里
    （源 FBX 的 Blender 材质引用它、`gen_models.py` v1 兼容），但**预览/图标与实机都不再用它**
    （预览/图标由 `art/preview_materials.py` 直接加载真实 `_D` PNG）。`art/material_patches.py` 的
    ART-04 role 表保留为 import 兼容/预览兜底，标注 deprecated。
13. **`material_slots.json` 已改成 ART-05 结构**：删除旧 `atlas` 块与 role-based `material_patches`，
    新增 `material_texture_map` 摘要、`material_texture_patches`、`construction_faction_materials`、`uv_space`；
    下游若仍读旧字段需要同步（仓库内验证器/测试已同步）。
14. `art/README.md` / `art/README-timbermesh.md` 保留 ART-04 历史内容（本文档、`art/validate_*.py`、
    `art/material_texture_map.json`、`art/timbermesh/export-manifest.json` 是现行契约与数值）。

---

## 13. `TimbermeshSpec.Model` 路径约定

路径 = **不带扩展名、相对 mod 根目录**的资产路径（游戏递归转换 mod 文件夹下所有 `*.timbermesh`）：

```text
Buildings/Food/BoxFarm/BoxFarm.Basic.Folktails.Model
  -> <mod>/Buildings/Food/BoxFarm/BoxFarm.Basic.Folktails.Model.timbermesh
  -> data/Buildings/Food/BoxFarm/BoxFarm.Basic.Folktails.Model.timbermesh   (本仓库)
```

官方 SDK 样本与 `docs/API_FACTS.md` §3.1 均确认该约定；本卡保持 16 个路径不变，因此蓝图引用无需改动。

---

## 14. 后续修改美术时

1. 改材质时先改 `art/material_texture_analysis.py` 的 `MATERIAL_TEXTURES` / `TEXTURE_HINTS`
   （材质→真实贴图/`_Color`/代表色/patch 预算 → 重新生成 JSON 并 `--verify`），再改
   `art/gen_models_v2.py` / `art/timbermesh_common.py`；新增/改名材质必须同步
   `timbermesh_common.MATERIAL_WHITELIST`（且仍是**原版 MaterialCollection 名称**）。
2. 重跑 §10 的 1–9 步；必须：两个验证器 PASS（含负向对照 3/3 被拒绝）、`data/` 16 文件与
   `art/timbermesh/` 逐字节一致、L1 `FAILED 0`、`build.ps1` RELEASE GATES PASSED，
   并同步更新本文档 §2/§3/§4/§5/§6/§7 数值与 SHA-256。
3. 严禁把 `PaintedMetal.*` 用于外壳；严禁回到“所有材质共用一张假图集”的 patch 假设
   （`V2MaterialPatchContractTests` 会红）。
4. 蓝图/碰撞/叠放块与图标路径属于蓝图卡，不要在本卡范围内改（图标仅重渲染，不换路径）。

## 15. 图标与预览刷新（ART-05，2026-10-27）
模型定稿后重跑 `art\render_previews.py` 与 `art\render_icons.py`：预览/图标直接加载真实 `_D` PNG + 材质
`_Color` tint（`art/preview_materials.py`），不再使用自造 2048 图集。`data/Icons/BoxFarm*.png` 已替换为
128px 真实材质渲染（`.meta.json` 保留 `{"isSprite": true}`）。
