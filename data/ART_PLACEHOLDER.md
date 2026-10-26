# Art contract — HDBF v2 3×3×1 indoor box farm `.timbermesh` models

> **状态**：v2 模型交付（2026-10-26），离线下验证完成；**未实机验证**。
> **适用**：模组 0.2.0 / v2 美术卡（REQ-V2-BLD-01/03/04/06/07、REQ-V2-ART-01..04）。
> **变更摘要**：v1/ART-04 的 3×2×1 外部盒子模型全部作废，改为 **严格 3×3×1** 室内高密度农场：
> 单一平坦空屋顶（走行面 = 模型最高面 **y = 1.00**，其上无任何几何）、静态室内货架/开窗/材质分层表达
> “室内高密度”、恰好 3 个 `#Port#` 空节点、0 个 `#Slot#` 节点。16 个 `.timbermesh` 的**文件名与资产路径不变**。

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

## 1. v2 改动（相对 ART-04）

| 项 | ART-04（旧，已作废） | v2（本目录现行） |
|---|---|---|
| 外包络 | 3.00 × 1.00 × 2.00（X×Y×Z，asset space） | **3.00 × 1.00 × 3.00**（严格，含装饰/开窗/收边/端口） |
| 屋顶 | 空平台，**走行面 0.96**，收边顶 1.00 | 单一平坦空平台，**走行面 = 模型最高面 1.00**；其上 0 几何；收边为**齐平** 0.04 高侧板（不高于顶面，不阻断叠放） |
| 室内表达 | 无（只有空屋顶） | **静态 3 层货架**透过开窗/开放立面可见；4 档用开窗方式/玻璃量/框架密度/材质分层区分 |
| 四档可辨 | 靠屋顶平台尺寸 | 靠**立面开窗 + 材质 + 轮廓/屋顶平面 + 端口件**（C9 12/12 对 PASS） |
| 空节点 | 3 个 `#Port#` + 0 `#Slot#` | 相同：**恰好 3 个 `#Port#`，0 个 `#Slot#`** |
| 端口坐标 | 3×2 深度（Z=+1.00 背面） | 按 3×3 重定：背面 Z=+1.50（见 §5） |
| 材质 | 原版 `MaterialCollection` 名称 + flat-patch UV | 不变（无自订 PBR / AssetBundle） |
| 施工阶段 | 8 个共享灰盒（空平台） | 8 个（tier×faction 路径各一）共享灰盒，**同样 3×3×1 + 同一平屋顶规则** |
| 生成器 | `art/gen_models.py`（ART-04） | **`art/gen_models_v2.py`**（v2；旧生成器保留为历史，不再用于交付） |

**交付的 16 个 `.timbermesh`（文件名/路径不变）：**

* **8 个成品模型（LOD0）**：`BoxFarm.<Tier>.<Faction>.Model.timbermesh`
* **8 个施工阶段模型**：`BoxFarm.<Tier>.<Faction>.ConstructionStage0.Model.timbermesh`

tiers = `Basic` / `Intermediate` / `Advanced` / `Top`；factions = `Folktails` / `IronTeeth`。

---

## 2. 模型清单（实测 values；资产路径冻结）

| Tier | Faction | Kind | `TimbermeshSpec.Model` 资产路径（无扩展名） | 文件（`data/Buildings/Food/BoxFarm/`） | Bytes | Tris | Vertices |
|---|---|---|---|---:|---:|---:|
| Basic | Folktails | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Basic.Folktails.Model` | `BoxFarm.Basic.Folktails.Model.timbermesh` | 143976 | 5942 | 14260 |
| Basic | IronTeeth | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Basic.IronTeeth.Model` | `BoxFarm.Basic.IronTeeth.Model.timbermesh` | 143173 | 5942 | 14230 |
| Intermediate | Folktails | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Intermediate.Folktails.Model` | `BoxFarm.Intermediate.Folktails.Model.timbermesh` | 143247 | 5950 | 14326 |
| Intermediate | IronTeeth | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Intermediate.IronTeeth.Model` | `BoxFarm.Intermediate.IronTeeth.Model.timbermesh` | 141732 | 5950 | 14248 |
| Advanced | Folktails | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Advanced.Folktails.Model` | `BoxFarm.Advanced.Folktails.Model.timbermesh` | 148967 | 5950 | 14744 |
| Advanced | IronTeeth | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Advanced.IronTeeth.Model` | `BoxFarm.Advanced.IronTeeth.Model.timbermesh` | 147868 | 5950 | 14710 |
| Top | Folktails | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Top.Folktails.Model` | `BoxFarm.Top.Folktails.Model.timbermesh` | 149060 | 5958 | 14752 |
| Top | IronTeeth | finished LOD0 | `Buildings/Food/BoxFarm/BoxFarm.Top.IronTeeth.Model` | `BoxFarm.Top.IronTeeth.Model.timbermesh` | 147704 | 5958 | 14698 |
| Basic | Folktails | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Basic.Folktails.ConstructionStage0.Model` | `BoxFarm.Basic.Folktails.ConstructionStage0.Model.timbermesh` | 10090 | 372 | 928 |
| Basic | IronTeeth | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Basic.IronTeeth.ConstructionStage0.Model` | `BoxFarm.Basic.IronTeeth.ConstructionStage0.Model.timbermesh` | 10095 | 372 | 928 |
| Intermediate | Folktails | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Intermediate.Folktails.ConstructionStage0.Model` | `BoxFarm.Intermediate.Folktails.ConstructionStage0.Model.timbermesh` | 10098 | 372 | 928 |
| Intermediate | IronTeeth | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Intermediate.IronTeeth.ConstructionStage0.Model` | `BoxFarm.Intermediate.IronTeeth.ConstructionStage0.Model.timbermesh` | 10103 | 372 | 928 |
| Advanced | Folktails | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Advanced.Folktails.ConstructionStage0.Model` | `BoxFarm.Advanced.Folktails.ConstructionStage0.Model.timbermesh` | 10094 | 372 | 928 |
| Advanced | IronTeeth | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Advanced.IronTeeth.ConstructionStage0.Model` | `BoxFarm.Advanced.IronTeeth.ConstructionStage0.Model.timbermesh` | 10098 | 372 | 928 |
| Top | Folktails | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Top.Folktails.ConstructionStage0.Model` | `BoxFarm.Top.Folktails.ConstructionStage0.Model.timbermesh` | 10087 | 372 | 928 |
| Top | IronTeeth | construction stage | `Buildings/Food/BoxFarm/BoxFarm.Top.IronTeeth.ConstructionStage0.Model` | `BoxFarm.Top.IronTeeth.ConstructionStage0.Model.timbermesh` | 10093 | 372 | 928 |

合计：**1,246,485 bytes / 50,576 tris / 123,392 vertices**（成品 8 档 1,161,727 B / 47,792 tris；施工 8 档 84,758 B / 2,784 tris）。
每档 LOD0 三角预算 6,000，最高 5,958。每个文件 **4 个节点**：1 root mesh + 3 个端口空节点；**0 个 `#Slot#`**。

`art/timbermesh/*.timbermesh` 与 `data/Buildings/Food/BoxFarm/*.timbermesh` 逐字节一致
（**16/16 SHA-256 相同**，2026-10-26 实测）。SHA-256：

```text
BoxFarm.Basic.Folktails.Model                              df418df4eeeac89c6531174355ebaa6ea5284d00dd23b293c7043f3342632f5d
BoxFarm.Basic.IronTeeth.Model                              7098193a549df99604a4f9371f8c6a371eeb5a56205fb2244087e7451b72815d
BoxFarm.Intermediate.Folktails.Model                       68e5bda5669c3a091082b69412cfab269acc3e01f73fdbc0e9b6100e6347ac29
BoxFarm.Intermediate.IronTeeth.Model                       aefb8c5e1f6f7a21b7dc853d768ea4f347bf6a1aecdea044ed79b28ac02e61ee
BoxFarm.Advanced.Folktails.Model                           48a378611fff539431a05f283a6c5ba3d05a672ab375504ba9d21da00cac7189
BoxFarm.Advanced.IronTeeth.Model                           4f5f2bf88f1f4d53c3abaa51a8be76502f9e4a5f129d6306f3306a4b9c416ba1
BoxFarm.Top.Folktails.Model                                15c61fde477066ded02a66e5cf0808c96395ed140b7566a14d3594552706378f
BoxFarm.Top.IronTeeth.Model                                4b5e1f802b67f699859bf46ed39ebfa9b278c39c9c638ef1beb0c54929d38ef2
BoxFarm.Basic.Folktails.ConstructionStage0.Model           e062241471acf4389b95cae472c35e38c5211bb0135c8cd322a8c376ee1eed62
BoxFarm.Basic.IronTeeth.ConstructionStage0.Model           dba6b2f3b6cac0f5df172ebef76d3a3e6dae4c8bb6747590435393de6f659f40
BoxFarm.Intermediate.Folktails.ConstructionStage0.Model    7a5e8dfdb864b317aa966d6a3a28a4282b8411d85b396b45d41ddc6161165613
BoxFarm.Intermediate.IronTeeth.ConstructionStage0.Model    78709ccc198f137d3948bb2db3e549cf099b7f35c2422e373c1d2084f303dc16
BoxFarm.Advanced.Folktails.ConstructionStage0.Model        e1bb232578eefc19da1173be22492feb1f9ccf363fe45adefcdeeb55e0fac3ea
BoxFarm.Advanced.IronTeeth.ConstructionStage0.Model        b85ce29498f95453efddaecc1187c91aa6d8c852870273db0db51dcc77a8e59e
BoxFarm.Top.Folktails.ConstructionStage0.Model             67ccf219fa9f2e91fb1bd65ec10c8d7720727a32b5944cf221f31d3a999d2116
BoxFarm.Top.IronTeeth.ConstructionStage0.Model             d378e99a6ff13fc1de2c62c94727a3477f15af1f5b375b87ae165946efda015e
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

所有装饰、开窗、收边、端口件都在包络内；生成器 `art/gen_models_v2.py` 对每个模型做硬断言
（任何越界直接令生成失败，不会静默产出）。

---

## 4. 屋顶：单一平坦空平台（几何契约 + 实测）

冻结规则（`art/atlas_layout.py` 常量 + 两个验证器共同执行）：

* 屋顶是**一个**平的、空的平台；**走行面 = 模型最高面 y = 1.00**（每档/LOD/阵营相同）；
* **顶面之上没有任何几何**；顶面以上 30 cm 体积为空（**可叠放**，REQ-V2-BLD-03/04、REQ-V2-ART-01）；
* 边缘“收边”为 **齐平** 的 0.04 高 × 0.04 宽侧板，顶面与走行面同高（**凸出顶面 0.000 m**），
  不阻断上方放置；收边高度 ≤ 0.04 满足要求；
* 平台板厚 0.05（走行面 1.00、板底 0.95），平台平面近乎铺满 3×3 足迹：

| Tier | 平台平面 X × Z |
|---|---|
| Basic | 2.84 × 2.84 |
| Intermediate | 2.92 × 2.92 |
| Advanced | 2.96 × 2.96 |
| Top | 2.98 × 2.98 |
| ConstructionStage0 | 2.90 × 2.90 |

实测证据（`art/validation-report.txt` 的 “FLAT ROOF EVIDENCE” 段，25/25 源模型）：
**TOP = 1.0000、APEX = 1.0000、ABOVE_TOP = 0、TOP_VERTS ≥ 60（顶面存在）**。
`art/validate_timbermesh.py` **C6** 对 16/16 文件额外断言：走行面顶点存在、顶面横跨平台平面、
**y > 1.00 的顶点数 = 0**（≥30 cm 净空由“顶点严格不高于 1.00”直接推出）。

屋顶上**没有**作物槽、土壤、通风塔、设备、尖顶或任何高于 1.00 的装饰。

---

## 5. 空节点：恰好 3 个端口（0 个 `#Slot#`）

模型里唯一的空节点是 3 个 `#Port#` 标记；**没有任何 `#Slot#` / `#Slot#Crop_*` 节点**，
也没有槽体/土壤/作物几何。8 个蓝图的根节点仍**不含** `TransformSlotInitializerSpec` /
`WorkplaceSlotManagerSpec`（沿用 ART-04 结论：自定义网格没有 Seat/Entrance 插槽，声明会在放置时抛异常）。

| 空节点 | contract space（中心轴心） | 游戏 export space（block-corner） | 朝外 | 语义 |
|---|---|---|---|---|
| `#Port#Power` | (1.00, 0.85, 1.50) | (2.50, 0.85, 3.00) | +Z | 背面右侧接线盒/绝缘子（Top 外露，低档盖板） |
| `#Port#Pipe` | (−1.50, 0.40, 0.00) | (0.00, 0.40, 1.50) | −X | 左侧短边中心法兰（低档为木盖板） |
| `#Port#Logistics` | (−1.00, 0.35, 1.50) | (0.50, 0.35, 3.00) | +Z | 背面左侧输送带开口（Basic 为空白盖板） |

实测：`art/validate_timbermesh.py` C6 对 16/16 文件：**3/3 端口存在且唯一、位置偏差 0.0000**
（容差 0.02）、vertexCount=0、无 mesh、**0 个 `#Slot#`**。源模型（FBX/GLB）中端口空节点位置与
local +Z 朝向也由 `art/validate_models.py` C5 双检。

---

## 6. 材质：原版 `MaterialCollection` 名称 + flat-patch UV

16 个文件里的材质名全部是**当前阵营的原版材质资产名**（游戏按名字在 `MaterialCollection` 解析；
不附带任何自订贴图/材质 AssetBundle）。UV 沿用 ART-04 的 **flat patch** 策略：同一材质槽的所有面
映射到该材质的一个固定小 UV 矩形（`art/material_patches.py`，10 个 patch、64 texels/grid），
造型细节由几何承担。`art/validate_models.py` C7 对 25 个源模型（FBX+GLB）逐三角形检查：
**所有 UV 都落在其材质自己的 flat patch 内**。

| Tier | Faction | # | 材质（原版资产名） |
|---|---|---:|---|
| Basic | Folktails | 2 | `BaseWood_Brown.Folktails`, `ThatchedRoof.Folktails` |
| Basic | IronTeeth | 2 | `BaseWood_DarkBrown.IronTeeth`, `RoofPlanks.IronTeeth` |
| Intermediate | Folktails | 3 | `IrregularPlanks_LightBrown.Folktails`, `Plaster_White.Folktails`, `WindowsAtlas.Folktails` |
| Intermediate | IronTeeth | 3 | `IrregularPlanks_DarkBrown.IronTeeth`, `Plaster_Orange.IronTeeth`, `WindowsAtlas.IronTeeth` |
| Advanced | Folktails | 4 | `BaseWood_White.Folktails`, `PaintedMetal.Folktails`, `Details.Folktails`, `WindowsAtlas.Folktails` |
| Advanced | IronTeeth | 4 | `BaseWood_Grey.IronTeeth`, `PaintedMetal.IronTeeth`, `Details.IronTeeth`, `WindowsAtlas.IronTeeth` |
| Top | Folktails | 4 | `BaseMetal.Folktails`, `IrregularPlanks_Brown.Folktails`, `Details.Folktails`, `WindowsAtlas.Folktails` |
| Top | IronTeeth | 4 | `BaseMetal.IronTeeth`, `BaseWood_DarkBrown.IronTeeth`, `IrregularPlanks_MetalForged.IronTeeth`, `WindowsAtlas.IronTeeth` |
| construction（8 档） | 各自阵营 | 2 | Folktails: `BaseWood_Brown.Folktails`, `BaseMetal.Folktails` / IronTeeth: `BaseWood_DarkBrown.IronTeeth`, `BaseMetal.IronTeeth` |

**诚实的限制**：flat patch 采样的是原版材质贴图上的固定小区域，不是我们绘制的 PBR 贴图；观感
（放大、法线/粗糙度、mipmap）**待实机确认**。自订 PBR 材质仍需 Unity AssetBundle，本卡未做。

---

## 7. “室内高密度”的静态表达（REQ-V2-ART-04）

3×3×1 只有 1 格高，室内用**静态装饰**表达密度，不生成 600 个槽位模型、不生成作物实体：

* **3 层静态货架**（每档相同层数，材料不同）位于前半室内，透过立面开口可见；
  货架板/立柱/边轨 + deterministic 小箱为纯装饰，模拟不读取（C4 逐档检测 ≥2 个不同高度的
  水平货架层，Basic 开放立面直接可见，Intermediate/Advanced/Top 透过玻璃可见）；
* **开窗方式分档**：Basic = 3 个开放bay（无玻璃）；Intermediate = 2 扇大窗 + 门 + 抹灰带；
  Advanced = 4+4 扇金属框玻璃幕（上下两排）；Top = 5+5 扇工业玻璃幕 + 金属百叶；
* **玻璃面积**（C4 下限）：0 / 0.35 / 0.60 / 0.85 grid²（Basic/Intermediate/Advanced/Top），实测均高于下限；
* **50% 镜头可辨（C9）**：以 32 px/grid（= 50% 缩放的 64 px/grid 基准）多视向 material/silhouette/
  glass/port 栅格比较，**12/12 tier pair PASS**，每对至少 2/4 个判据不同（material ≥0.05、
  windows ≥0.03、轮廓/屋顶平面、端口件 ≥10%）。四档在 50% 镜头下可辨。

预览图（离线 Cycles 渲染，非游戏截图）：`art/previews/`，含 4 档 front/side/top/iso/scale50 与
`tiers_scale50_grid.png`、`tier_ab_basic_vs_intermediate_50.png`、`tier_ab_advanced_vs_top_50.png`
（50% 缩放 A/B 对照）；施工灰盒 front/iso。

---

## 8. 施工阶段（REQ-V2-ART-03）

`data/Buildings/Food/BoxFarm/` 的 8 个 `*.ConstructionStage0.Model.timbermesh` 由同一份共享灰盒
几何导出（每 tier×faction 一份文件，只有路径/`Model.Name` 不同）：3×3×1 包络、脚手架 + 半建墙体 +
**同一个平坦空屋顶（走行面 1.00、其上无几何）**、2 个材质（原木 + 裸金属）、372 tris。
蓝图的 `Children.#Unfinished.Children.ConstructionStage0.TimbermeshSpec.Model` 路径不变；
完工替换由蓝图侧完成（本卡不改蓝图）。

---

## 9. 复现命令（工作区根目录）

沙箱约束：构建/中间产物写 `%TEMP%`；Blender 用 4.2.1 headless（`--factory-startup`）。

```powershell
$env:BLENDER_USER_CONFIG = "$env:TEMP\blender-config"
$b = 'C:\Users\user\.hdbf\sdk\blender-4.2.1-windows-x64\blender.exe'

# 1) 生成 25 个源模型（8 tier×faction × LOD0/1/2 + ConstructionStage0）+ material_slots.json
& $b -b --factory-startup -noaudio --python art\gen_models_v2.py

# 2) 验证源模型（FBX 重导入 + GLB 导入双检；C1-C10；写 art/validation-report.txt）
& $b -b --factory-startup -noaudio --python art\validate_models.py

# 3) 导出 16 个 .timbermesh + export-manifest.json（exit 0 = 16/16）
& $b -b --factory-startup -noaudio --python art\export_timbermesh.py

# 4) 验证 16 个 .timbermesh（纯 Python，无需 Blender；写 art/timbermesh-validation.txt）
python art\validate_timbermesh.py

# 5) 预览（离线渲染，非游戏截图）
& $b -b --factory-startup -noaudio --python art\render_previews.py
```

**确定性**：同机同输入下两次完整“生成→导出”与两次验证产生一致结果——
`art/timbermesh/export-determinism.txt` 记录两次导出 16/16 SHA-256 相同；
两份验证报告（`art/validation-report.txt`、`art/timbermesh-validation.txt`）均为**无时间戳的确定性报告**，
重复运行逐字节相同。生成器/导出器/验证器全程无随机数（只有固定 hash 噪声）与时间戳。

---

## 10. 验证摘要（本次实测，离线）

| 检查 | 命令 | 结果 |
|---|---|---|
| 源模型 C1–C10（25 模型，FBX+GLB 双检） | `blender ... --python art\validate_models.py` | **OVERALL VERDICT: PASS**，exit 0；C8 LOD 收缩 PASS；C9 12/12 tier pair PASS；C10 material_slots.json 一致 |
| 16 个 `.timbermesh` C1–C8 | `python art\validate_timbermesh.py` | **FILES: 16/16 present; 16 PASS, 0 FAIL**；**CHECKS: 128 PASS, 0 FAIL, 0 NOT RUN**；**OVERALL VERDICT: PASS**，exit 0 |
| data ↔ art 一致性 | `Get-FileHash` | **16/16 SHA-256 相同** |
| 导出确定性 | 两次 `export_timbermesh.py` | `art/timbermesh/export-determinism.txt`：16/16 相同，`PASS - deterministic` |
| 报告确定性 | 同一验证命令重复运行 | 两份报告逐字节相同（无时间戳/耗时） |
| L1 数据契约 | `dotnet run --project tests\HDBF.Tests -c Release -m:1 -p:HdbfWorkRoot="$env:TEMP\hdbf-work"` | 见本卡最终报告（`PASSED ... FAILED 0`） |

`.timbermesh` 检查明细（`art/timbermesh-validation.txt`）：

* C1 zlib+protobuf 解析；C2 `Model.name`/`version`；C3 严格 3.00×1.00×3.00 且 min=(0,0,0)；
* C4 三角预算 ≤6000/档；C5 材质白名单/数量；**C6 3 端口 + 0 #Slot# + 平屋顶**；
* C7 顶点属性；C8 拓扑/uv0。

---

## 11. 未完成 / 显式偏差（绝不隐瞒）

1. **8 个蓝图与碰撞盒仍是旧的 3×2 足迹**（`BlockObjectSpec.Size={X:3,Y:2,Z:1}`、
   `CollidersSpec` 3×1×2）。本卡按任务约束**不改蓝图**（蓝图/规格是另一张卡）；因此**在此状态下
   实机放置会出现“模型 3 深、碰撞 2 深”的不一致**（模型会向背面多占约 1 格）。必须先由蓝图卡把
   `BlockObjectSpec`/`CollidersSpec`/`Stackable` 顶层块与端口 transput 改为 3×3×1 后再实机验收。
   这是本卡最关键的未完成依赖。
2. **未实机验证**：没有部署、没有启动游戏、没有跑 `build.ps1`；所有“看起来如何/是否可叠放”的
   结论都是离线的，**待实机确认**（TC-V2-01/02/10）。
3. **屋顶叠放 A/B**（REQ-V2-BLD-04/07）需要实机用一座原版小建筑验证；本卡只保证模型侧
   “走行面 1.00、其上无几何、收边齐平、平台平面近满铺 2.84–2.98”。
4. **材质外观**：flat patch 是原版材质贴图的固定采样区，不是自订 PBR；放大/法线/roughness/
   mipmap 的观感未实机确认。
5. **顶点色是否被 shader 使用未知**：16/16 文件带非白 `color` 顶点属性（`vertex_colors.non_white=true`），
   但游戏 shader 是否消费未验证——不要依赖它。
6. **端口空节点 rotation 为 identity**（导出器只写位置；朝向记录在契约里，游戏侧当前无消费者）。
7. **只导出 LOD0** `.timbermesh`；LOD1/2 只存在于 `art/models/**` 源资产，游戏端依赖
   `Timberborn.PrefabOptimization` 自动生成，切换质量未实机确认。
8. **施工阶段蓝本**：`Children.#Unfinished` 仍使用 `ConstructionBase3x2#nested`（旧 3×2 建造基座，
   另一处需随蓝图卡一起改），本卡只更新灰盒模型。
9. **图标未重渲染**：`data/Icons/BoxFarm*.png` 仍是 ART-04 的 3×2 模型渲染；本卡交付范围是模型 +
   预览；图标与建造菜单呈现**待后续卡/实机确认**。
10. `art/README.md` / `art/README-timbermesh.md` 保留 ART-04 历史内容（本文件与
    `art/validate_*.py` 是 v2 现行契约）；`art/models/**` 的旧 3×2 源已被 v2 覆盖。

---

## 12. `TimbermeshSpec.Model` 路径约定

路径 = **不带扩展名、相对 mod 根目录**的资产路径（游戏递归转换 mod 文件夹下所有 `*.timbermesh`）：

```text
Buildings/Food/BoxFarm/BoxFarm.Basic.Folktails.Model
  -> <mod>/Buildings/Food/BoxFarm/BoxFarm.Basic.Folktails.Model.timbermesh
  -> data/Buildings/Food/BoxFarm/BoxFarm.Basic.Folktails.Model.timbermesh   (本仓库)
```

官方 SDK 样本与 `docs/API_FACTS.md` §3.1 均确认该约定；本卡保持 16 个路径不变，因此蓝图引用无需改动。

---

## 13. 后续修改美术时

1. 改 `art/gen_models_v2.py`（几何/材质）→ 重跑 §9 的 1–5 步；保持外包络 3×3×1、
   平屋顶走行面 1.00、3 端口、0 `#Slot#`、材质白名单与 flat-patch UV。
2. 重跑后必须：`art/validation-report.txt` PASS、`art/timbermesh-validation.txt` PASS、
   `data/` 16 文件与 `art/timbermesh/` 逐字节一致、本文件 §2/§3/§5 数值同步更新。
3. 蓝图/碰撞/叠放块与图标属于蓝图卡，不要在本卡范围内改。
