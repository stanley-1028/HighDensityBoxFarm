# Art contract — HDBF v2 3×3×1 indoor box farm `.timbermesh` models

> **状态**：v2.4 / **ART-07「原生纹素密度世界尺度 UV」升级**交付（2026-10-27），离线验证完成；**未实机验证**。
> **适用**：模组 2.4.0 / v2 美术卡 + `art/references/` 升级卡（REQ-V2-BLD-01/03/04/06、REQ-V2-ART-01..04）。
> **本次升级（ART-07）针对用户 2026-10-27 实机反馈**："不错，但只要贴图是清晰和高质量的，就更好了。"
> 根因：ART-06 的 flat-patch 把每个面塞进材质贴图上一个小 patch（`PATCH_TEXELS_PER_GRID=32`），
> 3 m 宽的屋顶/墙面只采样约 96 px 的原版 1024² 贴图 → 木纹/石膏/金属被拉伸、发糊。
> ART-07 改为**世界尺度平面投影**：按面主法线把世界坐标线性映射进该材质自己的真实贴图画布，
> 每个材质一个冻结的纹素密度（tile 画布 **340.0–357.8 px/m**、patch 画布 **82.4–164.0 px/m**，
> 较 ART-06 的 32 px/m 提升 **4.5–11×**）。几何/外包络/平屋顶/端口/无 `#Slot#` 契约不变。
> **为什么不是 UV>1 平铺**：实机 `AutoAtlasingPrefabOptimizer` 会把网格 UV 重映射进阵营图集单元
> （`uv*(1/sizeMultiplier)+cell_offset`，每格仍是原生 1024²），UV>1 会采样到**别的材质**。
> 因此 ART-07 的 UV 全部留在各自材质画布内，密度取该约束下的最大值。技术证据见
> `art/ART07_PROVENANCE.md`，映射契约见 `art/ART07_UV_TILING.md`。

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

> `ART_CONTRACT: material=flat-patch` 是**冻结的机器可读 token**（`tools/validate` + L1 测试按整行断言），
> 其 ART-07 语义为：**每个材质一个固定采样画布**——同一材质的每个面都按世界尺度平面投影映射到该材质
> 真实 `_D` 贴图上的同一个 `uv_canvas`（tile 材质 = 整张 1024²；patch 材质 = 精选的一致区域），
> 不再是 ART-06 的 32 px/m 小 patch，也绝不是 ART-01 的 2048 假图集。
> 材质名仍是**当前阵营的原版 `MaterialCollection` 材质资产名**；`PaintedMetal.*`（整张黄黑/蓝黑警示条
> 贴图）被列入禁用清单，不得用于外壳（见 §6）。

---

## 1. ART-05 → ART-07 变更摘要

| 项 | v2.3（ART-05，历史） | v2.4（ART-07，本版） |
|---|---|---|
| UV 基准 | 每材质真实 1024² 贴图上的**低方差小 patch**；每面塞进 patch 的确定性子矩形 | **世界尺度平面投影**：`uv = uv_center + (world_uv - world_center) * px_per_m / texture_size`；每材质一个冻结画布与密度（`art/material_uv_policy.py` → `art/material_texture_map.json`） |
| 纹素密度 | 固定 32 px/m（3 m 面 ≈ 96 px） | tile 材质 **340.0–357.8 px/m**（3 m 面 ≈ 1020–1074 px，接近满 1024² 画布）；patch 材质 **145.3–164.0 px/m**；玻璃 82.4 px/m → 较 ART-06 提升 4.5–11× |
| UV 是否 >1 | 否（小 patch 内） | 否（**必须**：实机 auto-atlas 把 UV 重映射进材质单元，UV>1 会采样别的材质；见 `art/ART07_PROVENANCE.md`） |
| 画面一致性 | 同材质所有面落在同一 patch（近均匀） | 同材质所有面共享同一世界尺度映射：相邻共面连续、密度恒定、无逐面随机拉伸 |
| 画布 | patch（128–512 px 窗口） | `uv_canvas`：tile = 整张 1024²；patch = 精选一致大区（木纹板田 508×440、素钢板 496×496、细节板 504×496、玻璃 248×248） |
| 材质映射 | ART-05（不变） | ART-05 的材质→真实贴图映射**完全保留**；`PaintedMetal.*` 仍禁用；`BaseMetal.D1/D2` 画布一致以保持施工阶段共享 UV 集 |
| 预览/图标 | 真实 `_D` PNG + `_Color` tint | 同源；Blender 贴图节点改为 `extension="REPEAT"`（与实机采样器一致）；新增 50%/100% 近景与四档同框 |
| 验证器 | C1–C14 + 负向对照 3/3 | C1–C16 + 负向对照 **6/6**（新增 C15 纹素密度、C16 UV 映射契约；新增 UV scale ×0.5/×2.0 与单面 0..1 拉伸 3 个负向对照） |

---

## 2. 模型清单（实测 values；资产路径冻结）

| Tier | Faction | Kind | `TimbermeshSpec.Model` 资产路径（无扩展名） | 文件（`data/Buildings/Food/BoxFarm/`） | Bytes | Tris | Vertices |
|---|---|---|---:|---:|---:|---:|
| Basic | Folktails | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Basic.Folktails.Model` | `BoxFarm.Basic.Folktails.Model.timbermesh` | 115647 | 5850 | 13664 |
| Basic | IronTeeth | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Basic.IronTeeth.Model` | `BoxFarm.Basic.IronTeeth.Model.timbermesh` | 114464 | 5850 | 13638 |
| Intermediate | Folktails | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Intermediate.Folktails.Model` | `BoxFarm.Intermediate.Folktails.Model.timbermesh` | 110351 | 5854 | 13556 |
| Intermediate | IronTeeth | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Intermediate.IronTeeth.Model` | `BoxFarm.Intermediate.IronTeeth.Model.timbermesh` | 110078 | 5854 | 13564 |
| Advanced | Folktails | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Advanced.Folktails.Model` | `BoxFarm.Advanced.Folktails.Model.timbermesh` | 111588 | 5850 | 13544 |
| Advanced | IronTeeth | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Advanced.IronTeeth.Model` | `BoxFarm.Advanced.IronTeeth.Model.timbermesh` | 111355 | 5850 | 13552 |
| Top | Folktails | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Top.Folktails.Model` | `BoxFarm.Top.Folktails.Model.timbermesh` | 110104 | 5918 | 13612 |
| Top | IronTeeth | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Top.IronTeeth.Model` | `BoxFarm.Top.IronTeeth.Model.timbermesh` | 109860 | 5918 | 13620 |
| Basic | Folktails | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Basic.Folktails.ConstructionStage0.Model` | `BoxFarm.Basic.Folktails.ConstructionStage0.Model.timbermesh` | 19005 | 864 | 2166 |
| Basic | IronTeeth | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Basic.IronTeeth.ConstructionStage0.Model` | `BoxFarm.Basic.IronTeeth.ConstructionStage0.Model.timbermesh` | 19003 | 864 | 2166 |
| Intermediate | Folktails | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Intermediate.Folktails.ConstructionStage0.Model` | `BoxFarm.Intermediate.Folktails.ConstructionStage0.Model.timbermesh` | 19012 | 864 | 2166 |
| Intermediate | IronTeeth | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Intermediate.IronTeeth.ConstructionStage0.Model` | `BoxFarm.Intermediate.IronTeeth.ConstructionStage0.Model.timbermesh` | 19011 | 864 | 2166 |
| Advanced | Folktails | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Advanced.Folktails.ConstructionStage0.Model` | `BoxFarm.Advanced.Folktails.ConstructionStage0.Model.timbermesh` | 19008 | 864 | 2166 |
| Advanced | IronTeeth | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Advanced.IronTeeth.ConstructionStage0.Model` | `BoxFarm.Advanced.IronTeeth.ConstructionStage0.Model.timbermesh` | 19005 | 864 | 2166 |
| Top | Folktails | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Top.Folktails.ConstructionStage0.Model` | `BoxFarm.Top.Folktails.ConstructionStage0.Model.timbermesh` | 19003 | 864 | 2166 |
| Top | IronTeeth | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Top.IronTeeth.ConstructionStage0.Model` | `BoxFarm.Top.IronTeeth.ConstructionStage0.Model.timbermesh` | 19000 | 864 | 2166 |

合计：**1,045,494 bytes / 53,856 tris / 126,078 vertices**（成品 8 档 893,447 B / 46,944 tris；施工 8 档 152,047 B / 6,912 tris）。
ART-07 只改 UV（顶点数/三角数不变；文件体积因 UV 压缩略降）。每档 LOD0 三角预算 6,000，最高 **5,918**。
每个文件 **4 个节点**：1 root mesh + 3 个端口空节点；**0 个 `#Slot#`**。
`art/timbermesh/*.timbermesh` 与 `data/Buildings/Food/BoxFarm/*.timbermesh` 逐字节一致（16/16 SHA-256 相同，
见 `art/timbermesh/export-determinism.txt`；两次完整导出 SHA-256 亦相同）：

```text
BoxFarm.Advanced.Folktails.ConstructionStage0.Model        a8b7fba835c23e0f43b14d1d5b37b44d96b4f369a0e39e933042680b310d1172
BoxFarm.Advanced.Folktails.Model                           0be7aee5350b6b6ab8175e98e4c75f7fb8d4e623cb24c1295ca5c7e9b3169e04
BoxFarm.Advanced.IronTeeth.ConstructionStage0.Model        d79f53b5f728fcca3c088ddf09722bbc19e02959b1980471c921bb5fb8e594b3
BoxFarm.Advanced.IronTeeth.Model                           978bab009321832fe634559f97d2931627bea04273b2d4722e6cd9b64ce9ddac
BoxFarm.Basic.Folktails.ConstructionStage0.Model           77813a43be4a21de17aeed0939192ca3d911da3a6356cc5b5ab79ab0445bc750
BoxFarm.Basic.Folktails.Model                              2acc142877ba952745da8d897f5ae2d00c4749d1f1b970750001729050673631
BoxFarm.Basic.IronTeeth.ConstructionStage0.Model           7102b5bde50274e97ab5d2fe3d308705349a361b58bf15ff6e6cbe862246e734
BoxFarm.Basic.IronTeeth.Model                              26234edc349dda2e0ec6e9cc151e93f4f809aac8a3fe53d1cb40506e80a6b6f1
BoxFarm.Intermediate.Folktails.ConstructionStage0.Model    183b36ad27e24f82f29b8b83ab2ac0265c2e876c45cfc38bf446abd377372b1c
BoxFarm.Intermediate.Folktails.Model                       a29e6e9dada488c384176016499d410b68ecc8cdea734de19333aed1f8f45034
BoxFarm.Intermediate.IronTeeth.ConstructionStage0.Model    87e6b1bbf196406becd6c8b8eb248e816c9acdfd0e7b1db71d492bb43e3e0469
BoxFarm.Intermediate.IronTeeth.Model                       19fd520252147eca656d48fa6292e1804412ba9ae49560dd3c6b795071a00b0a
BoxFarm.Top.Folktails.ConstructionStage0.Model             7c5844c6cc2adef9a2b2c5d593c4df99bd1c30376c54e146eaa74d91a5dcfaa6
BoxFarm.Top.Folktails.Model                                6da053f8dff7b6ecaac95276e9ae82238ee3aa4b2d7074a55aa099ff5895084b
BoxFarm.Top.IronTeeth.ConstructionStage0.Model             abea19a271b543e7cdf7e2f30f7881d6e793af4494cbdca6fc460111dd79eb96
BoxFarm.Top.IronTeeth.Model                                ef8c94edf42121118e32d55f7da65781897d521781d1f95c097b54544ce5cf3f
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

## 6. 材质：原版 `MaterialCollection` 名称 + 真实贴图 ART-07 世界尺度画布（ART-05/07 核心）

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

### 6.3 材质 → 真实贴图 → ART-07 世界尺度画布/密度（权威＝`art/material_texture_map.json` + `art/material_uv_policy.py`）

`mode`：`tile` = 整张 `_D` 贴图是可平铺的满幅表面贴图（画布 = 1024² 全图）；`patch` = 贴图是
“贴图集/图集页”，画布只能取其中**最大的一致区域**（避开阵营横幅/logo/文字/画布留白/警示条/铆接花纹）。
`tile_meters` = REQ 目标（一个完整贴图对应的世界尺寸）；`px/m` = 实际生效密度 =
`min(REQ 目标, 可用画布 / 世界覆盖)`（可用画布 = 画布边各留 2 texel 保护带）。
`uv_canvas` 是唯一采样契约；patch 材质的 `patch` 记录即该画布（`art07_canvas: true`），
tile 材质的 `patch` 记录仍是 ART-05 小探针窗口（仅供 C13 贴图身份/方差复测），`uv_canvas` 才是全图。

| 材质（原版资产名） | 真实 `_D` 贴图 | `_Color` tint | mode | tile_meters | canvas pixel rect | canvas UV rect (u0,v0,u1,v1) | px/m |
|---|---|---|---:|---:|---|---|---:|
| `BaseMetal.Folktails` | `BaseMetal_D1.png` | (1.0000,1.0000,1.0000) | patch | — | 8,8,504,504 | 0.007812,0.507812,0.492188,0.992188 | 164.0 |
| `BaseMetal.IronTeeth` | `BaseMetal_D2.png` | (1.0000,1.0000,1.0000) | patch | — | 8,8,504,504 | 0.007812,0.507812,0.492188,0.992188 | 164.0 |
| `BaseWood_Brown.Folktails` | `BaseWood_1K_D_Brown.png` | (1.0000,1.0000,1.0000) | patch | — | 4,4,512,444 | 0.003906,0.566406,0.500000,0.996094 | 145.3 |
| `BaseWood_DarkBrown.IronTeeth` | `BaseWood_1K_D_Grey.png` | (0.3113,0.2673,0.2071) | patch | — | 4,4,512,444 | 0.003906,0.566406,0.500000,0.996094 | 145.3 |
| `BaseWood_Grey.IronTeeth` | `BaseWood_1K_D_Grey.png` | (0.6226,0.5796,0.5257) | patch | — | 4,4,512,444 | 0.003906,0.566406,0.500000,0.996094 | 145.3 |
| `BaseWood_LightBrown.Folktails` | `BaseWood_1K_D_White.png` | (0.6604,0.5552,0.4081) | patch | — | 4,4,512,444 | 0.003906,0.566406,0.500000,0.996094 | 145.3 |
| `BaseWood_White.Folktails` | `BaseWood_1K_D_White.png` | (0.8868,0.8641,0.8324) | patch | — | 4,4,512,444 | 0.003906,0.566406,0.500000,0.996094 | 145.3 |
| `Details.Folktails` | `Details_D.Folktails.png` | (1.0000,1.0000,1.0000) | patch | — | 4,516,508,1012 | 0.003906,0.011719,0.496094,0.496094 | 164.0 |
| `Details.IronTeeth` | `Details_D.IronTeeth.png` | (1.0000,1.0000,1.0000) | patch | — | 4,516,508,1012 | 0.003906,0.011719,0.496094,0.496094 | 164.0 |
| `IrregularPlanks_Brown.Folktails` | `IrregularPlanks_D_Brown.png` | (1.0000,1.0000,1.0000) | tile | 2.4 | 0,0,1024,1024 | 0.000000,0.000000,1.000000,1.000000 | 340.0 |
| `IrregularPlanks_MetalForged.IronTeeth` | `PlanksWithMetal_D.png` | (1.0000,1.0000,1.0000) | tile | 2.2 | 0,0,1024,1024 | 0.000000,0.000000,1.000000,1.000000 | 340.0 |
| `Moss.Folktails` | `Moss_D.Folktails.png` | (1.0000,1.0000,1.0000) | tile | 2.2 | 0,0,1024,1024 | 0.000000,0.000000,1.000000,1.000000 | 341.1 |
| `Plaster_Orange.IronTeeth` | `Plaster_D_White.IronTeeth.png` | (0.8353,0.4824,0.2588) | tile | 2.6 | 0,0,1024,1024 | 0.000000,0.000000,1.000000,1.000000 | 340.0 |
| `Plaster_White.Folktails` | `Plaster_D_White.Folktails.png` | (1.0000,1.0000,1.0000) | tile | 2.6 | 0,0,1024,1024 | 0.000000,0.000000,1.000000,1.000000 | 340.0 |
| `RoofPlanks.IronTeeth` | `RoofPlanks_D.png` | (1.0000,1.0000,1.0000) | tile | 2.2 | 0,0,1024,1024 | 0.000000,0.000000,1.000000,1.000000 | 357.8 |
| `WindowsAtlas.Folktails` | `WindowsAtlas_D.Folktails.png` | (1.0000,1.0000,1.0000) | patch | — | 772,772,1020,1020 | 0.753906,0.003906,0.996094,0.246094 | 82.4 |
| `WindowsAtlas.IronTeeth` | `WindowsAtlas_D.IronTeeth.png` | (1.0000,1.0000,1.0000) | patch | — | 772,772,1020,1020 | 0.753906,0.003906,0.996094,0.246094 | 82.4 |

画布取区理由（`art/material_uv_policy.py` 的 `reason` 字段逐条记录）：

* **tile（满幅表面贴图）**：`Plaster_*`（石膏）、`Moss`（苔藓）、`RoofPlanks`（屋面板）、
  `IrregularPlanks_Brown`、`IrregularPlanks_MetalForged`——这些 `_D` 贴图整张就是可平铺表面，任意子区都可读；
* **patch（图集页取最大一致区）**：`BaseWood_*` 取左上**竖向板田**（508×440，避开右侧木板束/下方锯切面/留白画布）；
  `BaseMetal_*` 取左上**素钢板**（496×496，避开格栅/肋条/六边形/瓷砖块）；`Details_*` 取左下**framed 深色木板墙板**
  （504×496，避开阵营横幅/口袋/旗带/木箱）；`WindowsAtlas_*` 取右下**平整玻璃渐变**（248×248，避开窗框/圆窗/背景）；
* `BaseMetal.Folktails` 与 `BaseMetal.IronTeeth` 画布**完全相同**（两张贴图 atlas 布局相同）——施工阶段共享 UV 集必需；
* 画布内容的 `mean RGB / std / hazard_fraction` 都从真实 PNG 重新计算并记录在 `uv_canvas.stats`，
  供验证器逐点复算；任何画布命中警示黄/横幅/文字特征都会让生成器报 warning（当前 17/17 画布 hazard=0.0）。

**探针 patch（C13 用）**：tile 材质的 `patch` 保持 ART-05 的小窗（`std ≤ 分级预算`），
patch 材质的 `patch` = 大画布本身（`std ≤ 硬上限 30`；木纹/细节区天然 std 约 11.7–28.6，
“细节更多”是本轮目标，不再套用 ART-05 的 ≤18 均匀预算，但硬上限与 hazard 检查仍强制）。

### 6.4 UV 分配（ART-07 世界尺度平面投影）

* 每个面按主法线选投影平面（Blender Z-up 作者空间）：`±X → U=y,V=z`；`±Y → U=x,V=z`；`±Z → U=x,V=y`；
  法线先归一化，`|分量|` 四舍五入到 4 位小数后取主分量（并列时 X>Y>Z）——45° 倒角面有 2–3 个相等分量，
  这是**冻结的并列判定**，生成器/验证器/测试必须一致（否则 float32 往返会让投影平面翻转）；
* `uv = uv_center + (world_uv - world_center) * px_per_m / texture_size`，
  世界中心 `(0, 0, 0.5)`，`uv_center` = 画布 UV 中心；密度见 §6.3；
* **所有 UV 必须留在该材质画布内**（tile 画布边 2 texel 保护带，patch 画布 2 texel 内缩），
  生成器逐点硬断言（越界/NaN 直接失败）；同一材质同一平面的相邻面因此**世界尺度连续**、无逐面拉伸；
* 为什么不用 UV>1 Repeat 平铺：实机 `AutoAtlasingPrefabOptimizer` 把网格 UV 重映射进阵营图集单元
  （`uv_atlas = uv*(1/sizeMultiplier) + cell_offset`，每格仍是该材质原生 1024²），UV>1 会离开本材质格子、
  **采样到图集里别的材质**。贴图资产本身是 Repeat（`m_WrapMode=0`），但重复在 UV 端被图集契约禁止。
  证据与 IL 引文见 `art/ART07_PROVENANCE.md`；
* 工程含义：1024² 贴图要覆盖整个 3 m 模型时，密度上限 = `1024/3.0 = 341 px/m`（tile 满幅）
  或 `画布像素/3.0`（patch 画布）；ART-07 就取这个上限（因此 `tile_meters` 的 REQ 目标会被世界覆盖钳制，
  文件里同时记录 `tile_meters`（REQ 目标）与 `canvas_meters`（实际世界跨度 = 画布像素/密度））；
* 法线并列的 45° 倒角三角面（占三角面 8.1%、占表面积 0.08%）在“精确 UV 复算”检查中跳过，
  但仍受 C11 包含性与 C15 面积聚合密度约束（见 §11）。

### 6.5 诚实的限制

* **木/板/金属画布密度 145.3–164.0 px/m** 低于 tile 材质（340–358 px/m）：因为原版木/金属 `_D` 是
  **贴图集页**（多块变体共用一张 1024²），能取的最大一致区只有约 500 px；这不是映射错误，
  而是原版资产 + 图集契约的硬上限。3 m 屋顶/墙面现在采样约 436–492 px（ART-06 只有 96 px）。
* **玻璃 82.4 px/m**：玻璃槽取的是 248 px 的平整渐变窗格（保证是玻璃而不是窗框/背景），
  玻璃本身近乎纯渐变，观感以光滑/反光为主，不追求高频细节。
* **tile 材质实际“一贴图覆盖 3 m”**（不是 REQ 的 2.2–2.8 m）：REQ 目标密度 400–500 px/m 需要 UV>1 平铺，
  与 §6.4 的图集契约冲突；当前 340–358 px/m 是**合法**上限，仍满足 300–600 px/m 验收带。
* patch 取的是原版贴图固定区域，单面颜色随画布内纹理细节轻微变化；不再跨材质/跨贴图跳变。
* 材质 `_Color` tint 的乘法语义来自 shader 属性 + `EnvironmentMaterialProperties` IL（`_Color` 被读取/写回）；
  预览按 sRGB 乘法近似，实机最终亮度受游戏光照/tonemap 影响，**待实机确认**。
* `BaseWood_Brown` 本身是很深的棕色（画布均值约 47/33/22）；Basic 档实机会是**深棕近黑木屋**，
  这是原版材质真实颜色，不是渲染错误。FT 阵营没有蓝灰/深灰“重钢”材质，因此 FT 的 Advanced 用
  `BaseMetal.Folktails`（橄榄灰烤漆钢板）、Top 用 `Details.Folktails`（深色厚板/铆接件）；
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

**ART-07 关键点：施工阶段只有一套烘焙 UV，却要同时满足两个阵营的材质**。做法不是复制源模型，而是：
用 **Folktails 施工材质**（`BaseWood_Brown.Folktails`、`BaseMetal.Folktails`）跑同一个
`assign_world_tiled_uvs` 烘焙 UV；IT 文件复用同一 UV 集、但材质换成
`RoofPlanks.IronTeeth` / `BaseMetal.IronTeeth`。可行性由**画布包含关系**保证
（记录在 `art/material_texture_map.json` → `construction_shared_patches`）：

| 槽位 | Folktails 材质（画布） | IronTeeth 材质（画布） | 共享 UV 窗口（= FT 材质画布） | 窗口内 per-texture mean / std |
|---|---|---|---|---|
| `M_HDBF_Wood` | `BaseWood_Brown.Folktails`（patch 4,4,512,444） | `RoofPlanks.IronTeeth`（tile 全图） | 0.003906,0.566406,0.500000,0.996094 | Brown (47.2,33.0,21.7)/11.73；RoofPlanks (37.2,40.5,31.4)/12.08 |
| `M_HDBF_Metal` | `BaseMetal.Folktails`（patch 8,8,504,504） | `BaseMetal.IronTeeth`（**同一画布**） | 0.007812,0.507812,0.492188,0.992188 | D1 (109.5,97.6,74.2)/14.45；D2 (108.0,103.3,103.3)/14.68 |

即：FT 的 UV 窗口必落在 IT 对应材质的画布内（`RoofPlanks` 是满幅 tile 画布，天然包含木画布窗口；
`BaseMetal_D1/D2` 画布像素矩形相同），所以 16 个 `.timbermesh` 里 FT 施工件材质 =
{`BaseWood_Brown.Folktails`, `BaseMetal.Folktails`}，IT 施工件材质 =
{`RoofPlanks.IronTeeth`, `BaseMetal.IronTeeth`}，而 **UV 完全相同**。
四个共享窗口的 per-texture 统计（mean/std/hazard=0）都记录在 JSON 中并由 C13/C16 从真实 PNG 复算。
蓝图 `Children.#Unfinished.Children.ConstructionStage0.TimbermeshSpec.Model` 路径不变。
---

## 10. 复现命令（工作区根目录；确定性、无时间戳）

沙箱约束：构建/中间产物写 `%TEMP%`；Blender 用 4.2.1 headless（`--factory-startup`）。

```powershell
$env:BLENDER_USER_CONFIG = "$env:TEMP\blender-config"
$b = 'C:\Users\user\.hdbf\sdk\blender-4.2.1-windows-x64\blender.exe'
$py = 'python'   # 任意 Python 3.11+（需 numpy/Pillow；Blender 自带 Python 也可）

# 0a) ART-07 UV 策略表自检（画布/密度/REQ 区间；不读贴图）
& $py art\material_uv_policy.py                      # RESULT: OK (17 materials)

# 0b) 材质→真实贴图→ART-07 画布/密度映射表（读游戏安装目录的 vanilla _D PNG，只读）
#    需要 %USERPROFILE%\.hdbf\sdk\vanilla-tex-all 与 vanilla-tex-extra（AssetStudio 解包的游戏贴图）
& $py art\material_texture_analysis.py --verify     # 复现性检查（PASS 才继续）
& $py art\material_texture_analysis.py              # 重新生成 art\material_texture_map.json

# 1) 生成 25 个源模型（8 tier×faction × LOD0/1/2 + ConstructionStage0）+ material_slots.json
& $b -b --factory-startup -noaudio --python art\gen_models_v2.py

# 2) 验证源模型（FBX 重导入 + GLB 导入双检；C1-C16 + 负向对照 6/6；写 art/validation-report.txt）
& $b -b --factory-startup -noaudio --python art\validate_models.py

# 3) 导出 16 个 .timbermesh + export-manifest.json（exit 0 = 16/16）
& $b -b --factory-startup -noaudio --python art\export_timbermesh.py

# 4) 验证 16 个 .timbermesh（纯 Python，无需 Blender；C1-C16 + 负向对照 6/6；写 art/timbermesh-validation.txt）
& $py art\validate_timbermesh.py

# 5) 预览（真实 _D 贴图 + 材质 _Color + REPEAT；双阵营 + 50% A/B + closeups 50%/100% + 四档同框）
& $b -b --factory-startup -noaudio --python art\render_previews.py

# 6) 图标（真实 _D 贴图；512px 源 + 128px 发布图标 + manifest）
& $b -b --factory-startup -noaudio --python art\render_icons.py

# 7) 部署 16 个 .timbermesh 到 data/
Copy-Item art\timbermesh\BoxFarm.*.timbermesh data\Buildings\Food\BoxFarm\ -Force

# 8) L1（含 ArtModelContractTests + V2MaterialPatchContractTests + V2MaterialTilingContractTests）
dotnet run --project tests\HDBF.Tests -c Release -m:1 -p:HdbfWorkRoot="$env:TEMP\hdbf-art07"

# 9) 发布门禁
powershell.exe -NoProfile -ExecutionPolicy Bypass -File build.ps1 -WorkRoot "$env:TEMP\hdbf-art07-gate"
```

**确定性**：

* `art/material_texture_analysis.py --verify` 重算出的 JSON 与提交版**逐字节一致**，且每个探针 patch 与
  每个 ART-07 画布的 mean RGB/std/hazard 都从真实 PNG 字节重新导出核对（PASS）；
* 两次完整 `gen_models_v2.py` → `export_timbermesh.py`：16/16 `.timbermesh` 与 manifest SHA-256 相同
  （`art/timbermesh/export-determinism.txt`）；UV 映射是纯函数（世界坐标 + 冻结策略表，无随机数）；
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

1. **仍需用户实机截图确认清晰度与观感**：本轮仍是离线验证（没有部署/启动游戏/实机截图）。UV 契约、密度、
   贴图映射都按**游戏内真实资产 + `PrefabOptimization` IL** 推导（见 `art/ART07_PROVENANCE.md`），
   但“实机看是否清晰、是否还有发糊/接缝”必须由用户截图验收。**特别需要用户确认**：3 m 屋顶/墙面的
   木板/钢板/石膏纹理是否达到期望的清晰度；不同材质交界处（例如木板墙 + 金属框）的内容自然不连续，
   这是“每材质独立画布”的必然结果，不是贴图错位。
2. **木/金属/细节/玻璃是 patch 画布，密度 82.4–164.0 px/m**：原版这些 `_D` 是**贴图集页**（一张 1024²
   里有多个变体/门窗/横幅），可用的最大一致区只有约 248–508 px；3 m 面最多采样约 436–492 px
   （ART-06 只有 96 px）。若用户认为还不够清晰，唯一合法提升路径是**在 Unity 端另造可平铺的材质资产**
   （超出本工作区“不碰 Unity/不造 AssetBundle”的约束）。
3. **tile 材质实际是一个贴图覆盖约 3 m（340–358 px/m），不是 REQ 的 2.2–2.8 m（400–500 px/m）**：
   后者需要 UV>1 平铺，实机 auto-atlas 会把 UV 重映射进材质格子、UV>1 会采样到别的材质
   （`art/ART07_PROVENANCE.md` §4）。340–358 px/m 已满足本轮“tile 材质 300–600 px/m”验收带，
   且 3 m 面采样约 1020–1074 px（原版 1024² 贴图原生密度）。
4. **FT 没有蓝灰/深灰“重钢”材质**：Folktails 的 `MaterialCollection` 里唯一金属材质是
   `BaseMetal.Folktails`（橄榄灰烤漆钢板），没有 IronTeeth 的蓝灰钢板（`BaseMetal_D2`）。
   因此 FT 的 Advanced 是橄榄灰烤漆钢板、Top 是 `Details.Folktails` 深色厚板 + 钢板强调；
   **“蓝灰金属+玻璃”在 IT 上才完整成立**。这是原版资产集限制，不是映射错误。
5. **PaintedMetal 保留在白名单但禁用**：它是真实原版材质（黄黑/蓝黑警示条），为保持白名单=原版集合超集
   而保留；`MATERIAL_MAP` 断言 + 两个验证器 + L1 测试都禁止它出现在任何发布模型里。
6. **Basic 实机会很暗**：`BaseWood_Brown` 的真实贴图本身是深棕（ART-07 画布均值 ~47/33/22），
   Basic 档会是“深棕近黑木屋”。这是原版材质真实颜色；若用户认为太暗，只能换更浅的原版木材质
   （例如 `BaseWood_LightBrown`）——那会改变“深棕”档位语义，需要用户确认。
7. **材质 `_Color` 乘法与预览 sRGB 近似**：IL 证明 `_Color` 被读取/写回，但 shader 中精确的线性/色彩空间
   处理与游戏 tonemap 未实机验证；预览按 sRGB 乘法近似，可能比游戏偏暗/偏亮。
8. **预览/图标是离线 studio 渲染**，不是游戏截图；光照与游戏昼夜/雾气不同，颜色以实机为准。
   100%/50% 近景只用于证明“贴图清晰度”，不冒充实机截图。
9. **8 个蓝图与碰撞盒仍是旧的 3×2 足迹**（`BlockObjectSpec.Size={X:3,Y:2,Z:1}`、`CollidersSpec` 3×1×2），
   本卡按约束**不改蓝图**；实机放置仍会出现“模型 3 深、碰撞 2 深”的不一致，需蓝图卡处理。
10. **屋顶叠放 A/B**（REQ-V2-BLD-04/07）仍待实机用一座原版小建筑验证。
11. **顶点色是否被 shader 使用未知**：16/16 文件带非白 `color` 顶点属性，游戏 shader 是否消费未验证。
12. **只导出 LOD0** `.timbermesh`；LOD1/2 只存在于 `art/models/**` 源资产，游戏端依赖
    `Timberborn.PrefabOptimization` 自动生成，切换质量未实机确认（ART-07 的 UV 映射对 LOD1/2 同样成立，
    密度恒定，不会因 LOD 切换跳变）。
13. **施工阶段蓝本**：`Children.#Unfinished` 仍使用 `ConstructionBase3x2#nested`（旧 3×2 建造基座），
    另一处需随蓝图卡一起改，本卡只更新灰盒模型。
14. **旧 ART-01 图集/`art/gen_textures.py` 未重跑**：`art/textures/atlas_*.png` 仍在仓库里
    （v1 `gen_models.py` 兼容/预览兜底），但**ART-06 起预览/图标与实机都不再用它**，ART-07 也没有改它，
    因此不在本轮重跑序列里；`art/material_patches.py` 的 ART-04 role 表保留为 import 兼容，标注 deprecated。
15. **`material_slots.json` 已改成 ART-07 结构**：每个模型新增 `uv_mapping` 块（material/mode/px_per_m/
    canvas），顶层新增 `uv_mapping` 摘要，`uv_space` 文本更新；删除旧 `patch_texels_per_grid` 的“shipped density”
    语义（保留 `legacy_patch_texels_per_grid` 供历史对照）。
16. **45° 倒角面的投影轴并列**：法线先归一化、`|分量|` 四舍五入 4 位后按 X>Y>Z 判定（冻结规则）；
    这些三角面（8.1% 数量、0.08% 表面积）在“精确 UV 复算”里跳过，但受 C11 包含性与 C15 面积聚合密度覆盖。
17. **画布内容取自原版贴图，不是我们绘制的 PBR**：个别面可能出现原版纹理的天然图案（板端/铆钉/补板），
    这是“使用真实原版材质”的必然结果（也是本轮的验收目标）。
18. `art/README.md` / `art/README-timbermesh.md` 保留 ART-04 历史内容（本文档、`art/ART07_UV_TILING.md`、
    `art/ART07_PROVENANCE.md`、`art/material_texture_map.json`、`art/timbermesh/export-manifest.json`
    是现行契约与数值）。
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

1. 改 UV 画布/密度时改 `art/material_uv_policy.py` 的 `_MATERIAL_POLICIES`（mode / tile_meters /
   canvas_pixel_rect / coverage），跑 `python art\material_uv_policy.py` 自检，再跑
   `python art\material_texture_analysis.py`（生成 `uv_canvas`/`px_per_m`/`stats`）；
   新增/改名材质必须同步 `art/timbermesh_common.MATERIAL_WHITELIST`（且仍是**原版 MaterialCollection 名称**）。
2. 重跑 §10 的 0a–9 步；必须：两个验证器 PASS（C1–C16，含负向对照 **6/6** 被拒绝）、`data/` 16 文件与
   `art/timbermesh/` 逐字节一致、L1 `FAILED 0`、`build.ps1` RELEASE GATES PASSED，
   并同步更新本文档 §2/§3/§4/§5/§6/§9/§11 数值与 SHA-256。
3. 严禁把 `PaintedMetal.*` 用于外壳；严禁回到“所有材质共用一张假图集”的 patch 假设，也**不要**把
   大面积材质的 UV 改成 >1（实机 auto-atlas 会采样到别的材质；`V2MaterialPatchContractTests` /
   `V2MaterialTilingContractTests` 会红）。
4. 蓝图/碰撞/叠放块与图标路径属于蓝图卡，不要在本卡范围内改（图标仅重渲染，不换路径）。

## 15. 图标与预览刷新（ART-07，2026-10-27）

模型定稿后重跑 `art\render_previews.py` 与 `art\render_icons.py`：预览/图标直接加载真实 `_D` PNG + 材质
`_Color` tint（`art/preview_materials.py`，Blender 贴图节点 `extension="REPEAT"` 与实机采样器一致）。
ART-07 新增近景证据（512²，无文字/叠加层）：

```text
art/previews/closeups/<Tier>.<Faction>_iso_50.png     (约 4.0 m 视野)
art/previews/closeups/<Tier>.<Faction>_iso_100.png    (约 2.0 m 视野，看木纹/钢板/石膏细节)
art/previews/closeups/<Tier>.<Faction>_front_50.png
art/previews/closeups/<Tier>.<Faction>_front_100.png
art/previews/tiers_lineup_iso_100.png                 (四档同框，Folktails)
```

`data/Icons/BoxFarm*.png` 已替换为 128px 真实材质渲染（`.meta.json` 保留 `{"isSprite": true}`）。
