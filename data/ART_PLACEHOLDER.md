# Art contract — HDBF v2 3×3×1 indoor box farm `.timbermesh` models

> **状态**：v2.2「参考图驱动的材质/开窗升级」交付（2026-10-26），离线验证完成；**未实机验证**。
> **适用**：模组 0.2.0 / v2 美术卡 + `art/references/` 升级卡（REQ-V2-BLD-01/03/04/06、REQ-V2-ART-01..04）。
> **变更摘要（v2.2）**：按 `art/references/` 的 7 张 Seedream 参考图做了一轮**材质分区 + 开窗比例 + 四档可辨度**
> 升级（木框包边 / 大面积分格玻璃 / 金属补丁+铆钉 / 苔藓点缀 / 分层室内剪影，四档递进），
> 外包络 3×3×1、平屋顶行走面 y=1.00、3 个 `#Port#`、0 个 `#Slot#`、flat-patch 材质的**硬契约不变**。
> 16 个 `.timbermesh` 的**文件名与资产路径不变**；参考图里与本契约冲突的做法（屋顶立管、外伸台阶、斜玻璃顶、
> 出占地的脚手架）**没有照抄**，见 §8。

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

---

## 1. v2.2 升级内容（相对 v2）

参考图只做**美术方向依据**，不直接进游戏；游戏资产仍是 `art/gen_models_v2.py` 产出的 `.timbermesh`。

| 项 | v2（本文件上一版） | v2.2（本版） |
|---|---|---|
| 立面分区 | 每档「一种墙 + 开口切洞 + 少量框料」 | **木框包边 / 大面积分格玻璃 / 金属补丁+铆钉 / 苔藓点缀** 四层语言；四档递进 |
| Basic | 全开放木棚 + 茅草/帆布屋顶，无门 | **深棕粗木板**（`BaseWood_Brown`）+ **原版苔藓**（`Moss.Folktails`，仅 Folktails）+ 两跨开放窗 + **正面居中木门** + 斜撑 + 木屋面板 |
| Intermediate | 2 扇小窗 + 抹灰带 | **蜂蜜/浅棕木框 + 温室大玻璃分格**（侧墙玻璃 **54.0%**）+ **奶油石膏平屋顶/墙带**（`Plaster_White/Orange`） |
| Advanced | 4+4 小玻璃格 + 金属带 | **蓝灰金属壳/框 + 大玻璃幕（分格 mullion）+ 木板补丁 + 铆接金属带**（钢构视觉占比 > 木） |
| Top | 5+5 小玻璃格 + 百叶 | **深色重钢 + 更密构架（3×3 / 2×3 分格、竖向肋）+ 铆钉/法兰 + 工业金属平屋顶**（仍**平屋顶**，无斜玻璃顶） |
| 入口 | Basic 无门；Top 门在背面 | 四档**正面居中门**（Basic/Intermediate 木门，Advanced/Top 金属门）；**齐平门槛 + 内侧踏步**，无伸出占地的台阶 |
| 室内表达 | 3 层静态货架 | 3 层静态货架 + **成排作物剪影**（托盘+叶簇，纯装饰几何，非 600 个槽位模型）；开阔档直接可见，玻璃档透玻璃可见 |
| 屋顶 | 板 + 齐平 0.04 收边 | **框式平台**：周边框梁（frame 材质，顶面=1.00 同高）+ 板缝/板格 + 平台下缘梁；顶面仍是单一平面 y=1.00，**其上 0 几何** |
| flat patch 角色 | 10 个 | **12 个**：新增 `moss`（苔藓/作物剪影）、`plaster`（奶油石膏）、`planks_dark`（IronTeeth 屋面板） |
| 生成确定性 | FBX 每次重写内嵌 CreationTimeStamp/UID | **FBX 时间戳与对象 UID 均固定**：两次生成 50/50 源模型逐字节一致 |

**交付的 16 个 `.timbermesh`（文件名/路径不变）：**

* **8 个成品模型（LOD0）**：`BoxFarm.<Tier>.<Faction>.Model.timbermesh`
* **8 个施工阶段模型**：`BoxFarm.<Tier>.<Faction>.ConstructionStage0.Model.timbermesh`

tiers = `Basic` / `Intermediate` / `Advanced` / `Top`；factions = `Folktails` / `IronTeeth`。

---

## 2. 模型清单（实测 values；资产路径冻结）

| Tier | Faction | Kind | `TimbermeshSpec.Model` 资产路径（无扩展名） | 文件（`data/Buildings/Food/BoxFarm/`） | Bytes | Tris | Vertices |
|---|---|---|---|---:|---:|---:|
| Basic | Folktails | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Basic.Folktails.Model` | `BoxFarm.Basic.Folktails.Model.timbermesh` | 135810 | 5850 | 13664 |
| Basic | IronTeeth | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Basic.IronTeeth.Model` | `BoxFarm.Basic.IronTeeth.Model.timbermesh` | 135169 | 5850 | 13638 |
| Intermediate | Folktails | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Intermediate.Folktails.Model` | `BoxFarm.Intermediate.Folktails.Model.timbermesh` | 130181 | 5854 | 13556 |
| Intermediate | IronTeeth | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Intermediate.IronTeeth.Model` | `BoxFarm.Intermediate.IronTeeth.Model.timbermesh` | 129979 | 5854 | 13564 |
| Advanced | Folktails | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Advanced.Folktails.Model` | `BoxFarm.Advanced.Folktails.Model.timbermesh` | 131715 | 5850 | 13537 |
| Advanced | IronTeeth | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Advanced.IronTeeth.Model` | `BoxFarm.Advanced.IronTeeth.Model.timbermesh` | 131584 | 5850 | 13545 |
| Top | Folktails | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Top.Folktails.Model` | `BoxFarm.Top.Folktails.Model.timbermesh` | 130630 | 5918 | 13612 |
| Top | IronTeeth | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Top.IronTeeth.Model` | `BoxFarm.Top.IronTeeth.Model.timbermesh` | 130428 | 5918 | 13620 |
| Basic | Folktails | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Basic.Folktails.ConstructionStage0.Model` | `BoxFarm.Basic.Folktails.ConstructionStage0.Model.timbermesh` | 22617 | 864 | 2166 |
| Basic | IronTeeth | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Basic.IronTeeth.ConstructionStage0.Model` | `BoxFarm.Basic.IronTeeth.ConstructionStage0.Model.timbermesh` | 22622 | 864 | 2166 |
| Intermediate | Folktails | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Intermediate.Folktails.ConstructionStage0.Model` | `BoxFarm.Intermediate.Folktails.ConstructionStage0.Model.timbermesh` | 22626 | 864 | 2166 |
| Intermediate | IronTeeth | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Intermediate.IronTeeth.ConstructionStage0.Model` | `BoxFarm.Intermediate.IronTeeth.ConstructionStage0.Model.timbermesh` | 22631 | 864 | 2166 |
| Advanced | Folktails | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Advanced.Folktails.ConstructionStage0.Model` | `BoxFarm.Advanced.Folktails.ConstructionStage0.Model.timbermesh` | 22621 | 864 | 2166 |
| Advanced | IronTeeth | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Advanced.IronTeeth.ConstructionStage0.Model` | `BoxFarm.Advanced.IronTeeth.ConstructionStage0.Model.timbermesh` | 22625 | 864 | 2166 |
| Top | Folktails | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Top.Folktails.ConstructionStage0.Model` | `BoxFarm.Top.Folktails.ConstructionStage0.Model.timbermesh` | 22614 | 864 | 2166 |
| Top | IronTeeth | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Top.IronTeeth.ConstructionStage0.Model` | `BoxFarm.Top.IronTeeth.ConstructionStage0.Model.timbermesh` | 22619 | 864 | 2166 |

合计：**1,236,471 bytes / 53,856 tris / 126,064 vertices**（成品 8 档 1,095,496 B / 46,944 tris；施工 8 档 140,975 B / 6,912 tris）。
每档 LOD0 三角预算 6,000，最高 **5,918**（v2 最高 5,958，本版未放松预算）。每个文件 **4 个节点**：1 root mesh + 3 个端口空节点；**0 个 `#Slot#`**。

`art/timbermesh/*.timbermesh` 与 `data/Buildings/Food/BoxFarm/*.timbermesh` 逐字节一致
（**16/16 SHA-256 相同**，2026-10-26 实测）。SHA-256：

```text
BoxFarm.Basic.Folktails.Model                              afa14a1124a9f21fd7d675a87b3fd5bb001399a454e625433f12b12388f427e2
BoxFarm.Basic.IronTeeth.Model                              73e5a5b0f3dd2850d002c262445e2f52080605905fd1c08eae3a1bdc60d946a0
BoxFarm.Intermediate.Folktails.Model                       43622a6e25d9f6ea86de47f0be61bf294baa6615d43fd059ed571a46b760cecd
BoxFarm.Intermediate.IronTeeth.Model                       1855f2606662095a34bddea04e444a49e04fd03816c9ffeb466d2ff8421c1fbf
BoxFarm.Advanced.Folktails.Model                           62daa625a29a0772e1c2fffda03584a3cc562a4c67f5fb9a9aad5a2ce286fb59
BoxFarm.Advanced.IronTeeth.Model                           8c911b3198191f89c09938f2ae86914bd31cdd1e5f60849e391947a4a6c3c2b7
BoxFarm.Top.Folktails.Model                                aa333816b1d6ef33d7885f3fecd6ca42720443b441f4d7acd2e93f8f550f343e
BoxFarm.Top.IronTeeth.Model                                1f90464aa39ffae955f7a7280c24940ac4e96b623346c2955c9e94ab026c26b5
BoxFarm.Basic.Folktails.ConstructionStage0.Model           532bae8f15ec20afc5b06fa6156021b111facdaef899eba4d7dc7b1a8f656c90
BoxFarm.Basic.IronTeeth.ConstructionStage0.Model           994342b907db7aa702cda79799a72b0abde2319a85900cc8a9a9974f99076c84
BoxFarm.Intermediate.Folktails.ConstructionStage0.Model    38c65687f422164781b3cd6873fa12d04c4f4c431e3c578d7ccf2142c5cfd6d0
BoxFarm.Intermediate.IronTeeth.ConstructionStage0.Model    b453234f208af760ad2be8db80c855a3af6087af3ad2a7404b1e909f511dc3fb
BoxFarm.Advanced.Folktails.ConstructionStage0.Model        018bb4b6d5b5dbe50fbca76ea15ffcb988fa6b06e8c15730fd929d8f5296e868
BoxFarm.Advanced.IronTeeth.ConstructionStage0.Model        b2c878b710bec9d4926eaebefe2637faa964f11d21d236e78a4288db005d3098
BoxFarm.Top.Folktails.ConstructionStage0.Model             8b4a58444a183696b846b4135f3f7079ea8784fd71c3c7e1cba3814fd8dad2be
BoxFarm.Top.IronTeeth.ConstructionStage0.Model             94231f0a684bcef48a4c1a0eaf0b9f548cde07ad1975cfe3277678d702801e30
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
`WorkplaceSlotManagerSpec`（沿用 ART-04 结论：自定义网格没有 Seat/Entrance 插槽，声明会在放置时抛异常）。

| 空节点 | contract space（中心轴心） | 游戏 export space（block-corner） | 朝外 | 语义 |
|---|---|---|---|---|
| `#Port#Power` | (1.00, 0.85, 1.50) | (2.50, 0.85, 3.00) | +Z | 背面右侧接线盒/绝缘子（Top 外露法兰，低档盖板） |
| `#Port#Pipe` | (−1.50, 0.40, 0.00) | (0.00, 0.40, 1.50) | −X | 左侧短边中心法兰（低档木盖板；高级/Top 带螺栓环） |
| `#Port#Logistics` | (−1.00, 0.35, 1.50) | (0.50, 0.35, 3.00) | +Z | 背面左侧输送带开口（Basic 空白盖板，其余档带滚轮） |

实测：`art/validate_timbermesh.py` C6 对 16/16 文件：**3/3 端口存在且唯一、位置偏差 0.0000**
（容差 0.02）、vertexCount=0、无 mesh、**0 个 `#Slot#`**。源模型（FBX/GLB）中端口空节点位置与
local +Z 朝向也由 `art/validate_models.py` C5 双检。

> **参考图冲突处理**：参考图把三根立管放在**屋顶/顶面**；按 REQ-V2-BLD-03/06 端口必须在**后立面/侧立面、
> 低于屋面**。本版保持冻结坐标：Power、Logistics 在**后立面**（+Z），Pipe 在**左侧短边**（−X，v1 契约位置）；
> 三者都在屋面以下，屋顶顶面保持空。

---

## 6. 材质：原版 `MaterialCollection` 名称 + flat-patch UV

16 个文件里的材质名全部是**当前阵营的原版材质资产名**（游戏按名字在 `MaterialCollection` 解析；
不附带任何自订贴图/材质 AssetBundle；v2.2 新增的 `Moss.Folktails` 也是原版
`MaterialCollection.Folktails` 已列出的材质，见 `C:\Users\user\.hdbf\ref\Blueprints\MaterialCollections`）。
UV 沿用 ART-04 的 **flat patch** 策略：同一材质槽的所有面映射到该材质的一个固定小 UV 矩形
（`art/material_patches.py`，v2.2 共 **12 个 patch**、64 texels/grid），造型细节由几何承担。
`art/validate_models.py` C7 对 25 个源模型（FBX+GLB）逐三角形检查：**所有 UV 都落在其材质自己的 flat patch 内**。

| Tier | Faction | # | 材质（原版资产名） | v2.2 视觉分工 |
|---|---|---:|---|---|
| Basic | Folktails | 2 | `BaseWood_Brown.Folktails`, `Moss.Folktails` | 深棕粗木板（墙/框/屋顶）+ 苔藓/作物剪影 |
| Basic | IronTeeth | 2 | `BaseWood_DarkBrown.IronTeeth`, `RoofPlanks.IronTeeth` | 深色木板 + 深色屋面板 |
| Intermediate | Folktails | 3 | `IrregularPlanks_LightBrown.Folktails`, `Plaster_White.Folktails`, `WindowsAtlas.Folktails` | 蜂蜜/浅棕木框 + 奶油石膏 + 大玻璃 |
| Intermediate | IronTeeth | 3 | `IrregularPlanks_DarkBrown.IronTeeth`, `Plaster_Orange.IronTeeth`, `WindowsAtlas.IronTeeth` | 深棕木框 + 暖石膏 + 大玻璃 |
| Advanced | Folktails | 4 | `BaseWood_White.Folktails`, `PaintedMetal.Folktails`, `Details.Folktails`, `WindowsAtlas.Folktails` | 金属壳/框 + 玻璃幕 + 木板补丁 + 铆接带 |
| Advanced | IronTeeth | 4 | `BaseWood_Grey.IronTeeth`, `PaintedMetal.IronTeeth`, `Details.IronTeeth`, `WindowsAtlas.IronTeeth` | 蓝灰金属壳/框 + 玻璃幕 + 木板补丁 + 铆接带 |
| Top | Folktails | 4 | `IrregularPlanks_Brown.Folktails`, `BaseMetal.Folktails`, `Details.Folktails`, `WindowsAtlas.Folktails` | 重钢 + 铆钉/法兰 + 密构架 + 暗色木点缀 |
| Top | IronTeeth | 4 | `BaseWood_DarkBrown.IronTeeth`, `BaseMetal.IronTeeth`, `IrregularPlanks_MetalForged.IronTeeth`, `WindowsAtlas.IronTeeth` | 深色重钢 + 铆钉/法兰 + 密构架 + 锻木板 |
| construction（8 档） | 各自阵营 | 2 | Folktails: `BaseWood_Brown.Folktails`, `BaseMetal.Folktails` / IronTeeth: `BaseWood_DarkBrown.IronTeeth`, `BaseMetal.IronTeeth` | 原木 + 裸金属 |

flat-patch 角色表（权威＝`art/material_patches.py`，机器可读＝`art/material_slots.json`；v2.2 新增 3 个角色）：

| patch id | atlas 分區 | pixel rect | UV rect (u0,v0,u1,v1) | texels | 原版材質名 |
---|---|---|---:|---:|---|
| `wood_plain` | wood | 96,352,480,736 | 0.046875,0.640625,0.234375,0.828125 | 384×384 | BaseWood_Brown.Folktails, BaseWood_DarkBrown.IronTeeth |
| `wood_light` | wood | 96,1056,480,1440 | 0.046875,0.296875,0.234375,0.484375 | 384×384 | BaseWood_LightBrown/White.Folktails, BaseWood_Grey.IronTeeth |
| `planks_light` | wood | 96,1568,480,1952 | 0.046875,0.046875,0.234375,0.234375 | 384×384 | IrregularPlanks_LightBrown.Folktails, IrregularPlanks_DarkBrown.IronTeeth |
| `planks_brown` | wood | 528,352,784,608 | 0.257812,0.703125,0.382812,0.828125 | 256×256 | IrregularPlanks_Brown.Folktails |
| `planks_dark` **(新)** | wood | 528,700,784,956 | 0.257812,0.533203,0.382812,0.658203 | 256×256 | RoofPlanks.IronTeeth（Basic IronTeeth 屋面板） |
| `metal_base` | metal | 1056,384,1440,768 | 0.515625,0.625,0.703125,0.8125 | 384×384 | BaseMetal.Folktails/IronTeeth |
| `metal_painted` | metal | 1056,1088,1440,1472 | 0.515625,0.28125,0.703125,0.46875 | 384×384 | PaintedMetal.Folktails/IronTeeth |
| `metal_details` | metal | 1056,1568,1312,1824 | 0.515625,0.109375,0.640625,0.234375 | 256×256 | Details.Folktails/IronTeeth, IrregularPlanks_MetalForged.IronTeeth |
| `glass` | glass | 1744,704,1832,792 | 0.851562,0.613281,0.894531,0.65625 | 88×88 | WindowsAtlas.Folktails/IronTeeth |
| `plaster` **(改)** | plaster | 1872,288,2000,416 | 0.914062,0.796875,0.976562,0.859375 | 128×128 | Plaster_White.Folktails, Plaster_Orange.IronTeeth |
| `roof` | thatch | 1872,1472,2000,1600 | 0.914062,0.21875,0.976562,0.28125 | 128×128 | ThatchedRoof.Folktails（保留映射；本版模型不再使用） |
| `moss` **(新)** | moss | 1900,940,2020,1060 | 0.927734,0.482422,0.986328,0.541016 | 120×120 | Moss.Folktails |

**诚实的限制**：flat patch 采样的是**原版材质贴图上的固定小区域**，不是我们绘制的 PBR 贴图；离线预览里的
颜色来自 `art/gen_textures.py` 画的 2048² 图集（只为预览/图标服务），**实机里采样的是原版材质贴图**，
观感（放大、法线/粗糙度、mipmap）**待实机确认**。自订 PBR 材质仍需 Unity AssetBundle，本卡未做。

---

## 7. 四档视觉差异（可量化）与“室内高密度”的静态表达

3×3×1 只有 1 格高，室内用**静态装饰**表达密度，不生成 600 个槽位模型、不生成作物实体：

* **3 层静态货架**（每档相同层数，材料不同）+ **成排作物剪影**（托盘 + 叶簇，deterministic 小件）
  位于前半室内，Basic 通过开放窗口直接可见，其余档透过玻璃可见；`validate_models.py` C4 逐档检测
  ≥2 个不同高度的水平货架层（实测 15–16 层/模型，远高于门槛 2）。
* **开窗方式分档**：Basic = 2 跨开放窗 + 正面中门（无玻璃，C4 强制 glass=0）；
  Intermediate = 7 樘温室大玻璃（2×2 分格）+ 门；Advanced = 7 樘金属框玻璃幕（2×2 分格）+ 木板补丁；
  Top = 8 樘工业玻璃（前 3×3、侧 2×3 更密分格）+ 金属门 + 背部百叶。
* **玻璃占比（v2.2 新增客观指标，来自 `art/material_slots.json`）**：

  | Tier | 侧墙玻璃占比（left+right / 2×墙带面积） | 正面玻璃占比 | 玻璃可见面积 grid² | C4 玻璃面积（含盒体，门槛） |
  |---|---:|---:|---:|---:|
  | Basic | 0.0% | 0.0% | 0.000 | 0.000（必须 0） |
  | Intermediate | **54.0%**（目标 45–55%） | 41.7% | 4.026 | 8.049（≥0.35） |
  | Advanced | **53.3%** | 37.7% | 3.896 | 7.922（≥0.60） |
  | Top | **53.5%** | 34.8% | 3.474 | 7.076（≥0.85） |

* **50% 镜头可辨（C9）**：以 32 px/grid 多视向 material/silhouette/glass/port 栅格比较，
  **12/12 tier pair PASS**，每对至少 2/4 个判据不同；最紧的是 Intermediate vs Advanced（material 0.942、
  windows 0.032、plan 0.04，3/4 类）与 Advanced vs Top（material 0.296、windows 0.045、plan 0.02、ports 0.48，4/4 类）。

---

## 8. 参考图采纳 / 不采纳对照（`art/references/`）

| 参考图 | 采纳 | 不采纳（与硬契约冲突） | 本版做法 |
|---|---|---|---|
| `ref-basic-folktails.jpg` | 深棕粗木板、板缝、**苔藓点缀**、斜撑、开放两跨、室内农产货架剪影 | 门前 2–3 级**外伸台阶**；屋顶边缘厚凸起 | 齐平门槛 + 内侧踏步；屋顶框梁收在 1.00 以下 |
| `ref-intermediate-folktails.jpg` | 蜂蜜/浅棕木框、**大面积温室玻璃分格**、奶油/浅色平屋顶、背部短管 | 立面顶部**斜玻璃**；屋顶立管 | 垂直玻璃 + 平屋顶；端口在后立面/侧面、低于屋面 |
| `ref-industrial-ironteeth.jpg` | **蓝灰金属壳/框**、金属分格、铆钉、**木板补丁**、金属门、法兰/粗管、金属平屋顶 | 屋顶边缘凸起金属边框；门前格栅台阶 | 框梁/补丁全部在 1.00 内；门槛齐平 |
| `ref-tier-lineup.jpg` | 四档**材质/开窗层层递进**、同尺寸同镜头、50% 可辨目标 | 第 3/4 栋仍带**斜玻璃顶** | 全部平屋顶；用材质/分格/框架密度/屋顶板缝区分 |
| `ref-interior-cutaway.jpg` | 多层货架密度、成排作物剪影、暖色室内感 | 室内层高 >1 格；600 槽动态模型；前景水池 | 1 格高静态 3 层货架 + 作物剪影（纯装饰） |
| `ref-construction-greybox.jpg` | 灰盒/木框架/脚手架语言、板材堆 | **脚手架伸出占地、高于 1 格** | 脚手架收进 3×3、全部 <1.00；端口节点保留 |
| `ref-roof-stacking.jpg` | 平屋顶**承托上方建筑**的贴合关系 | 上层建筑本身（不是本卡范围） | 顶面 y=1.00 单一平台、其上 0 几何；叠放实机待验 |

---

## 9. 施工阶段（REQ-V2-ART-03）

`data/Buildings/Food/BoxFarm/` 的 8 个 `*.ConstructionStage0.Model.timbermesh` 由同一份共享灰盒
**参考图语言**导出（每 tier×faction 一份文件，只有路径/`Model.Name` 不同）：3×3×1 包络、
**木框架 + 内部钢脚手架（立杆/横杆/斜撑，全部在占地内）+ 半建墙体 + 同一平坦空屋顶**
（走行面 1.00、其上无几何）、2 个材质（原木 + 裸金属）、**864 tris**。
蓝图的 `Children.#Unfinished.Children.ConstructionStage0.TimbermeshSpec.Model` 路径不变；
完工替换由蓝图侧完成（本卡不改蓝图）。

---

## 10. 复现命令（工作区根目录）

沙箱约束：构建/中间产物写 `%TEMP%`；Blender 用 4.2.1 headless（`--factory-startup`）。

```powershell
$env:BLENDER_USER_CONFIG = "$env:TEMP\blender-config"
$b = 'C:\Users\user\.hdbf\sdk\blender-4.2.1-windows-x64\blender.exe'

# 0)（仅当图集区域/材质 patch 有变化时）重建 3 张 2048² 图集（v2.2 有变化：moss/plaster/planks_dark）
& $b -b --factory-startup -noaudio --python art\gen_textures.py
#    （连跑两次，第二次输出 CROSS-RUN DETERMINISM: PASS；第一次在内容变更后显示 FAIL 属预期）

# 1) 生成 25 个源模型（8 tier×faction × LOD0/1/2 + ConstructionStage0）+ material_slots.json
& $b -b --factory-startup -noaudio --python art\gen_models_v2.py

# 2) 验证源模型（FBX 重导入 + GLB 导入双检；C1-C10；写 art/validation-report.txt）
& $b -b --factory-startup -noaudio --python art\validate_models.py

# 3) 导出 16 个 .timbermesh + export-manifest.json（exit 0 = 16/16）
& $b -b --factory-startup -noaudio --python art\export_timbermesh.py

# 4) 验证 16 个 .timbermesh（纯 Python，无需 Blender；写 art/timbermesh-validation.txt）
python art\validate_timbermesh.py

# 5) 预览（离线渲染，非游戏截图；双阵营 + 50% A/B）
& $b -b --factory-startup -noaudio --python art\render_previews.py

# 6) 部署 16 个 .timbermesh 到 data/
Copy-Item art\timbermesh\BoxFarm.*.timbermesh data\Buildings\Food\BoxFarm\ -Force

# 7) L1（含 ArtModelContractTests）
dotnet run --project tests\HDBF.Tests -c Release -m:1 -p:HdbfWorkRoot="$env:TEMP\hdbf-art2"
```

**确定性**：v2.2 固定了 Blender FBX 导出器的 `CreationTimeStamp` 与对象 UID（`gen_models_v2.py`
的 `pin_fbx_timestamp()` / `pin_fbx_uids()`），因此两次完整“生成→导出”结果一致：
`art/timbermesh/export-determinism.txt` 记录两次导出 16/16 SHA-256 相同；
两份验证报告（`art/validation-report.txt`、`art/timbermesh-validation.txt`）均为**无时间戳的确定性报告**。
图集：`art/gen_textures.py` 连跑两次逐字节相同（CROSS-RUN PASS）。
源模型：两次 `gen_models_v2.py` 的 50/50 文件 SHA-256 相同（含 FBX）。

---

## 11. 验证摘要（本次实测，离线，2026-10-26）

| 检查 | 命令 | 结果 |
|---|---|---|
| 源模型 C1–C10（25 模型，FBX+GLB 双检） | `blender ... --python art\validate_models.py` | **OVERALL VERDICT: PASS**，exit 0；C8 LOD 收缩 PASS；C9 12/12 tier pair PASS；C10 material_slots.json 一致（12 patches） |
| 16 个 `.timbermesh` C1–C8 | `python art\validate_timbermesh.py` | **FILES: 16/16 present; 16 PASS, 0 FAIL**；**CHECKS: 128 PASS, 0 FAIL, 0 NOT RUN**；**OVERALL VERDICT: PASS**，exit 0 |
| `data` ↔ `art` 一致性 | `Get-FileHash` | **16/16 SHA-256 相同** |
| 导出确定性 | 两次 `art/export_timbermesh.py` | 16/16 `.timbermesh` + manifest SHA-256 相同 |
| 生成确定性 | 两次 `gen_models_v2.py` / `gen_textures.py` | 50/50 源模型逐字节一致；图集 CROSS-RUN PASS |
| 预览 | `blender ... --python art\render_previews.py` | **48 张 8-bit PNG PASS**（8 模型 × 5 视角 + 施工 2 + 6 张 A/B 合图） |
| L1 数据契约 | `dotnet run --project tests\HDBF.Tests -c Release -m:1 -p:HdbfWorkRoot="$env:TEMP\hdbf-art2"` | **PASSED 530 / FAILED 0**（含 `ArtModelContractTests`） |

`.timbermesh` 检查明细（`art/timbermesh-validation.txt`）：

* C1 zlib+protobuf 解析；C2 `Model.name`/`version`；C3 严格 3.00×1.00×3.00 且 min=(0,0,0)；
* C4 三角预算 ≤6000/档（实测 5850–5918）；C5 材质白名单/数量（含 `Moss.Folktails`）；
* **C6 3 端口 + 0 #Slot# + 平屋顶**；C7 顶点属性；C8 拓扑/uv0。

---

## 12. 未完成 / 显式偏差（绝不隐瞒）

1. **8 个蓝图与碰撞盒仍是旧的 3×2 足迹**（`BlockObjectSpec.Size={X:3,Y:2,Z:1}`、
   `CollidersSpec` 3×1×2）。本卡按任务约束**不改蓝图**；实机放置仍会出现“模型 3 深、碰撞 2 深”的不一致。
   必须先由蓝图卡把 `BlockObjectSpec`/`CollidersSpec`/`Stackable` 顶层块与端口 transput 改为 3×3×1 后再实机验收。
2. **未实机验证**：没有部署、没有启动游戏、没有跑 `build.ps1`；所有“看起来如何/是否可叠放”的结论都是离线的。
3. **屋顶叠放 A/B**（REQ-V2-BLD-04/07）仍待实机用一座原版小建筑验证；本卡只保证模型侧
   “走行面 1.00、其上无几何、收边齐平、平台平面 2.84–2.98”。
4. **材质外观**：flat patch 是原版材质贴图的固定采样区，不是自订 PBR；离线预览用 `art/gen_textures.py`
   的程序化图集，**实机观感（颜色/法线/roughness/mipmap）未确认**。v2.2 的“奶油石膏/苔藓/屋面板”颜色
   在实机由原版材质决定（`Plaster_White` / `Moss.Folktails` / `RoofPlanks`），预览图不代表实机最终颜色。
5. **“烟灰玻璃”无法用材质表达**：4 个材质槽冻结（`M_HDBF_*`）且玻璃只能用 `WindowsAtlas`；本版用
   **更密的深色金属构架 + 更窄玻璃分格**表达 Top 的“重工业/烟灰”观感，玻璃本身材质与其它档相同。
6. **阵营差异是材质/细节差**：Basic/Intermediate 两阵营共用同一木构几何，差异在材质（苔藓 vs 屋面板、
   奶油 vs 暖石膏）与倒角；Advanced/Top 差异在金属材质（`PaintedMetal.Folktails` 偏橄榄、`.IronTeeth` 蓝灰）。
7. **苔藓只出现在 Basic.Folktails**：`Moss.Folktails` 只在 Folktails 的 MaterialCollection 里；IronTeeth
   没有对等苔藓材质，Basic.IronTeeth 用深色屋面板，**不能为了对称而编造不存在的原版材质名**。
8. **顶点色是否被 shader 使用未知**：16/16 文件带非白 `color` 顶点属性，但游戏 shader 是否消费未验证。
9. **端口空节点 rotation 为 identity**（导出器只写位置；朝向记录在契约里，游戏侧当前无消费者）。
10. **只导出 LOD0** `.timbermesh`；LOD1/2 只存在于 `art/models/**` 源资产，游戏端依赖
    `Timberborn.PrefabOptimization` 自动生成，切换质量未实机确认。
11. **施工阶段蓝本**：`Children.#Unfinished` 仍使用 `ConstructionBase3x2#nested`（旧 3×2 建造基座，
    另一处需随蓝图卡一起改），本卡只更新灰盒模型。
12. **图标未重渲染**：`data/Icons/BoxFarm*.png` 仍是上一版模型渲染；图标属于蓝图/图标卡范围
    （本卡只允许改 art/** 与 data/Buildings 下 .timbermesh 与本文档），建造菜单呈现**待后续卡/实机确认**。
13. **FBX 容器字节确定性是 v2.2 新做的**：通过固定导出器时间戳与对象 UID 实现；BLENDER 升级大版本后
    需重跑并确认（脚本会在无法 patch 时打印 WARNING，不会静默）。
14. `art/README.md` / `art/README-timbermesh.md` 保留 ART-04 历史内容（本文档、`art/validate_*.py`、
    `art/timbermesh/export-manifest.json` 是 v2.2 现行契约与数值）。`README-timbermesh.md` 顶部已加 v2.2 速查表。
15. **`material_slots.json` 的 ConstructionStage0 条目是阵营无关的**（只有 1 个 key，列 Folktails 材质名）；
    4 个 IronTeeth 施工 `.timbermesh` 实际用 IronTeeth 材质（`MATERIAL_MAP` 正确、白名单校验通过），
    但 JSON 不单独描述该变体。这是沿用 v2 的 JSON 结构（C10 的 key 契约不允许拆成两个 key）。
16. **C10 “12 flat patches” 是记录数，不是“全部被几何使用”**：`roof`（`ThatchedRoof.Folktails`）本版没有
    模型使用（保留映射以覆盖任何未来 Basic 茅草屋面板），实际被 16 档几何使用到的是 11 个 role。
17. **C6 的 top-face `face=/ext=` 是“近顶带（±0.01）内、平台平面 ±0.02 内”顶点统计**，会把 Advanced/Top
    位于平台边缘的 y≈0.997 金属铆钉底算进去，所以报告里 `ext=2.98/3.00` 而真正的共面屋面板
    （|y−1|≤1e-4、精确平面内）恰好跨满冻结平面 2.96/2.98。没有几何超过走行面；这是度量口径差异。
18. **C9 的 Intermediate↔Advanced / Intermediate↔Top 余量偏薄**（windows 0.032 vs 门槛 0.030、
    silhouette 0.003/0.005 vs 0.02、ports 0.04 vs 0.10；靠 material + roof-plan 过 3/4 类）。
    若未来调相机/指标或改屋顶平面，这两对可能翻转，重跑时必须复核。

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

1. 改 `art/gen_models_v2.py`（几何/材质分区）与（必要时）`art/atlas_layout.py` / `art/material_patches.py`
   / `art/gen_textures.py` → 重跑 §10 的 0–5 步；保持外包络 3×3×1、平屋顶走行面 1.00、3 端口、0 `#Slot#`、
   材质白名单与 flat-patch UV（新增/改名材质必须同步 `timbermesh_common.MATERIAL_WHITELIST` 与
   `material_patches.ROLE_OF_MATERIAL`，且仍是**原版 MaterialCollection 名称**）。
2. 重跑后必须：`art/validation-report.txt` PASS、`art/timbermesh-validation.txt` PASS、
   `data/` 16 文件与 `art/timbermesh/` 逐字节一致、本文 §2/§3/§4/§5/§6/§7 数值同步更新。
3. 蓝图/碰撞/叠放块与图标属于蓝图卡，不要在本卡范围内改。

## 15. 图标刷新（2026-10-27）
v2.2 模型定稿后重新跑 `art/render_icons.py`（4 x 512 RGBA + 4 x 128 RGBA，判定 PASS），data/Icons/BoxFarm*.png 已替换为 128px 新渲染；L1 530/530、build.ps1 44 gates PASS。

