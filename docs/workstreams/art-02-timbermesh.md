# workstream: art-02-timbermesh — 四檔 `.timbermesh` 匯出與驗證（docs-writer 彙整）

Task: shared task `task-3`（docs-writer；上游 `task-1` 圖示、`task-2` 驗證；匯出器由 Lead 完成）。
狀態：**離線匯出 + 純 Python 驗證完成（16/16 PASS、128/128 檢查 PASS）**。
**未做**：遊戲內驗證、Blueprint 路徑替換、部署、自訂 PBR 材質 AssetBundle、LOD1/LOD2 `.timbermesh`、Unity 匯入。
本輪只聲稱「離線匯出與驗證完成」，不聲稱「已在遊戲中看到」。

---

## 0. 摘要（可先看這段）

* 產出 **16 個 `.timbermesh`**：8 個成品（4 檔 × 2 陣營，LOD0）＋ 8 個 ConstructionStage0
  （同一組 tier × faction 各一份施工階段灰盒）。合計 **1,172,971 bytes**（zlib 壓縮後）、
  **50,828 三角面**（成品 47,564、施工 3,264）、121,934 頂點。
* 每個檔案的包圍盒都是 **3.000 × 1.000 × 2.000**、min = (0, 0, 0)：**block-corner 原點**，
  正面在 Z = 0。三角面最高 5,950（LOD0 預算 6,000）。
* **本卡最重要的發現**：需求 13.3/13.6 假設「佔地底面中心軸心」，但遊戲的模型空間實測是
  **block 角落原點**。官方插件只做軸轉換＋X/Z 鏡射（`export = (−bx, bz, −by)`，winding 反轉），
  **不做置中→角點平移**；負控制量到原始 ART-01 座標直出仍是 X [−1.5, 1.5]、Z [−1, 1]。
  匯出器因此補償 `blender_out = (−src.x − 1.5, src.y − 1.0, src.z)`，淨效果
  `export = contract + (1.5, 0, 1.0)`。
* 材質全部改指原版 MaterialCollection 資產名（白名單 **21/21 逐一比對命中**、實際用到 20 個，
  每檔 2/2/3/3/4/4/4/4 個材質）；自訂 PBR 材質與 AssetBundle 不在本卡範圍，因此遊戲會用原版
  atlas 拼布取樣（已誠實記錄）。
* 6 個 `#Slot#Crop_0..5` + 3 個 `#Port#Power/#Port#Pipe/#Port#Logistics` 空節點，座標偏差
  **0.0000**（容差 0.02）；空節點 vertexCount=0、無 mesh。
* 驗證：`python art\validate_timbermesh.py` → **16/16 PASS、128/128、`OVERALL VERDICT: PASS`、exit 0**
  （CPython 3.14.7、0.47 s、不需 Blender）。軸向測試 `OVERALL VERDICT: PASS`（10/10 項）。
* 可重跑性：第二次完整重跑（Blender 4.2.1 LTS headless）後 16/16 檔案 **SHA-256 完全相同**
  （逐檔 hash 見 §8.2）；8 張圖示亦經雙跑驗證 byte-identical（見 §9）。
* 未修改 `src/`、`data/`、`tests/`、`docs/VERIFICATION.md`、`docs/REQ_TRACEABILITY.md`；
  未部署、未啟動遊戲、未跑 `build.ps1`、未碰 Unity。

## 1. 需求出處（只讀來源）

* `高密度盒子農場模組需求文件.md` 第 13 章：13.3（比例與網格規範：3×2×1、底面中心軸心、
  X∈[−1.5,1.5]、Z∈[−1,1]、Y∈[0,1]）、13.6（碰撞體與四類連接埠：端口座標「原點為佔地底面中心軸心」、
  空物件 local +Z 朝外）、13.5（LOD 預算）、13.7（命名與匯出要求）。
* `data/ART_PLACEHOLDER.md`（佔地/pivot/端口契約）、`docs/ARCHITECTURE.md` ADR-003
  （v0.1.0 無 `.timbermesh`，美術以 placeholder 契約先行）。
* 官方 modding SDK：`C:\Users\user\.hdbf\sdk\sdk-repo\timberborn-modding-main\`
  （`Assets\Mods\ShantySpeaker\` 的 `.timbermesh` 與 blueprint、Blender 插件 v1.2.0）。
* 本輪交付卡（Team Lead 派工）：匯出 16 檔、依 13.6 加 9 個空節點、材質改指原版資產、
  軸向測試含負控制、純 Python 驗證報告、4 檔圖示、README 與本報告；不得碰 Unity / 部署 / 遊戲。

## 2. 產出物清單（全部在工作區）

```
art/
├── timbermesh_common.py              凍結契約（座標轉換/尺寸/材質白名單/槽位/端口；Lead）
├── export_timbermesh.py              Blender headless 批次匯出器（Lead）
├── axis_test.py                      Blender headless 軸向/單位測試（Lead）
├── validate_timbermesh.py            純 Python 驗證器，不需 Blender（validator）
├── render_icons.py                   4 檔圖示離線渲染（icon-renderer）
├── timbermesh-validation.txt         驗證報告（16 檔 × C1–C8 + SUMMARY）
├── timbermesh/
│   ├── BoxFarm.<Tier>.<Faction>.Model.timbermesh                  ×8
│   ├── BoxFarm.<Tier>.<Faction>.ConstructionStage0.Model.timbermesh ×8
│   ├── export-manifest.json          匯出清單（bytes/tris/vertices/materials/slots/ports/sha256）
│   └── axis-test/                    axis-test-report.txt + 2 個測試 .timbermesh（補償/負控制）
├── tools/timbermesh_blender_plugin/  官方插件 v1.2.0 MIT vendored（含 LICENSE 與 protobuf runtime）
└── icons/                            BoxFarm.<Tier>.png（512）+ BoxFarm.<Tier>.128.png（128）+ manifest
```

交付文件：`art/README-timbermesh.md`（技術交接，含重跑命令與座標推導）、本報告。
`art/timbermesh/*` 合計 **1,172,971 bytes**（16 檔本體）＋ manifest 34,994 bytes；
軸向測試 2 檔共 1,820 bytes。

主要證據檔（可直接點開核對）：

| 產出 | 連結 |
|---|---|
| 16 檔清單（bytes / tris / materials / sha256 / slots / ports） | [export-manifest.json](../../art/timbermesh/export-manifest.json) |
| 驗證報告（16 檔 × C1–C8 + SUMMARY） | [timbermesh-validation.txt](../../art/timbermesh-validation.txt) |
| 軸向/單位測試報告（補償 vs 負控制） | [axis-test-report.txt](../../art/timbermesh/axis-test/axis-test-report.txt) |
| 圖示清單（8 檔、相機、hash） | [icons-manifest.json](../../art/icons/icons-manifest.json) |
| 技術交接 README | [README-timbermesh.md](../../art/README-timbermesh.md) |
| 契約 / 匯出器 / 驗證器 | [timbermesh_common.py](../../art/timbermesh_common.py) / [export_timbermesh.py](../../art/export_timbermesh.py) / [validate_timbermesh.py](../../art/validate_timbermesh.py) |

## 3. 座標 / 軸向實測結果（本卡核心）

### 3.1 兩套座標系

| | 需求 13.3/13.6（contract） | 遊戲 `.timbermesh`（export） |
|---|---|---|
| 原點 | 佔地底面中心 | **block 角落**（左下前角） |
| X | [−1.5, +1.5] | **[0, 3]** |
| Y | [0, 1] | [0, 1]（向上） |
| Z | [−1, +1]，正面 −Z | **[0, 2]，正面在 Z = 0** |

換算：`export = contract + (1.5, 0, 1.0)`；反之 `contract = export − (1.5, 0, 1.0)`。

### 3.2 證據表

| # | 來源 | 量測 | 結論 |
|---|---|---|---|
| E1 | 官方 SDK 範例 `ShantySpeaker.IronTeeth.Model.timbermesh`（1×1×2） | root 節點 X **[0, 1]**、Y **[0, 1.869]**、Z **[0, 1]**；子節點 `#SpeakersAnimated.001` local 位置 (0.5, 1.7, 0.5) | 模型從 block 角點長起 |
| E2 | `ShantySpeaker.IronTeeth.blueprint.json` 的 `CollidersSpec` | box center (0.5,0.25,0.5)/(0.5,1.0,0.5)/(0.5,1.6,0.5) | 碰撞盒中心 = 尺寸一半 → 角原點 |
| E3 | 原版 `BuildersHut.Folktails.blueprint.json`（3×2） | 主 box center **(1.5, 0.5, 1.0)**、size (3, 1, 2) | 原版 blueprint 的通用慣例 |
| E4 | 本工作區 8 個 blueprint 現有 `CollidersSpec` | center (1.5, 0.5, 1.0)、size (3.0, 1.0, 2.0) | 已正確，**不需改** |
| E5 | 負控制：原始 ART-01 直出、不補償 | X **[−1.5, 1.5]**、Y [0, 1]、Z **[−1, 1]** | 插件不做置中平移；中心軸心會原樣保留 |
| E6 | 補償後（production 路徑） | min **(−0.0, 0.0, −0.0)**、max **(3.0, 1.0, 2.0)** | 補償公式正確 |

原始檔案：[axis-test-report.txt](../../art/timbermesh/axis-test/axis-test-report.txt)。
負控制檔 `BoxFarm.AxisTestNoCompensation.Model.timbermesh`（895 bytes）、production 測試檔
`BoxFarm.AxisTest.Model.timbermesh`（925 bytes）。

> 量測法：zlib 解壓 → 官方 `model_pb2` → 讀各節點 `position`。**節點位置是相對父節點**，
> 因此必須依節點分開量測；驗證器的 `--reference` 診斷模式（選用、不在交付報告本體）把 ShantySpeaker
> 所有節點的地區頂點直接合併（不套 transform）得到 1.43×2.06×1.45，那只是解析器交叉驗證的診斷值，
> 不是 E1 的結論。

### 3.3 官方插件映射與匯出器補償

官方插件 `node_builder.py` 寫檔時固定做：

```
export.position = (−bx, bz, −by)        # 位置/法線/切線同映射
export.rotation = (qx, −qz, qy, qw)     # 四元數同語意
export.scale    = (sx, sz, sy)
三角形索引順序反轉（node_builder.py:140–141）
```

映射矩陣行列式 = −1（X/Z 鏡射），所以必須反轉 winding 才不會讓面朝向/法線反掉。
**插件沒有「置中 → 角原點」的平移**，那是我們在場景端補的：

```
blender_out = (−src.x − 1.5,  src.y − 1.0,  src.z)      # 套 EXPORT_MATRIX
淨效果       export = (src.x + 1.5,  src.z,  −src.y + 1.0) = contract + (1.5, 0, 1.0)
```

實作細節：`export_timbermesh.py` 先清掉 FBX 的 stale custom split normals（鏡射後會錯），
再 `mesh.transform(EXPORT_MATRIX)`、`flip_winding()`（`mesh.flip_normals()`）抵銷插件那次
winding 反轉。同一公式以純 Python 形式留在 `timbermesh_common.source_to_export_blender()`，
驗證器與 README 都引用同一份數字。

### 3.4 軸向/單位測試（`axis-test.py` 實跑）

* 測試模型：3×2×1 方塊（ART-01 作者座標）＋ 5 個 0.08 不對稱探針方塊 ＋ 1 個標記空物件；
  production 檔 72 tri / 144 頂點。
* 10 項全部 PASS：bounds size (3,1,2)、min (0,0,0)、front/back/left/right/high 五個探針
  8/8 頂點命中、空物件位置 (1.75, 0.5, 0.5)、`Model.Name`、`color` 屬性。
* 負控制量測：bounds X [−1.5, 1.5]、Y [0, 1]、Z [−1, 1]、size [3.0, 1.0, 2.0]。
* 報告：`OVERALL VERDICT: PASS`（Blender 4.2.1 LTS、插件 1.2.0）。

## 4. 檔案清單與大小（16 檔，數值取自 `export-manifest.json` / 驗證報告）

| 檔案 | bytes | tris | 頂點 | 材質數 |
|---|---:|---:|---:|---:|
| `BoxFarm.Basic.Folktails.Model` | 130,970 | 5,940 | 14,029 | 2 |
| `BoxFarm.Basic.IronTeeth.Model` | 129,352 | 5,940 | 14,015 | 2 |
| `BoxFarm.Intermediate.Folktails.Model` | 136,055 | 5,950 | 14,342 | 3 |
| `BoxFarm.Intermediate.IronTeeth.Model` | 136,358 | 5,950 | 14,364 | 3 |
| `BoxFarm.Advanced.Folktails.Model` | 137,383 | 5,942 | 14,233 | 4 |
| `BoxFarm.Advanced.IronTeeth.Model` | 137,881 | 5,942 | 14,237 | 4 |
| `BoxFarm.Top.Folktails.Model` | 140,821 | 5,950 | 14,266 | 4 |
| `BoxFarm.Top.IronTeeth.Model` | 140,975 | 5,950 | 14,256 | 4 |
| `BoxFarm.Basic.Folktails.ConstructionStage0.Model` | 10,391 | 408 | 1,024 | 2 |
| `BoxFarm.Basic.IronTeeth.ConstructionStage0.Model` | 10,398 | 408 | 1,024 | 2 |
| `BoxFarm.Intermediate.Folktails.ConstructionStage0.Model` | 10,400 | 408 | 1,024 | 2 |
| `BoxFarm.Intermediate.IronTeeth.ConstructionStage0.Model` | 10,405 | 408 | 1,024 | 2 |
| `BoxFarm.Advanced.Folktails.ConstructionStage0.Model` | 10,396 | 408 | 1,024 | 2 |
| `BoxFarm.Advanced.IronTeeth.ConstructionStage0.Model` | 10,402 | 408 | 1,024 | 2 |
| `BoxFarm.Top.Folktails.ConstructionStage0.Model` | 10,389 | 408 | 1,024 | 2 |
| `BoxFarm.Top.IronTeeth.ConstructionStage0.Model` | 10,395 | 408 | 1,024 | 2 |
| **合計** | **1,172,971** | **50,828** | **121,934** | — |

* 成品 8 檔：1,089,795 bytes / 47,564 tris；施工 8 檔：83,176 bytes / 3,264 tris。
* 檔案格式 = `zlib.compress(model_pb2.Model.SerializeToString())`；解壓後例如
  Basic.Folktails 為 934,520 bytes protobuf。磁碟 bytes 是壓縮後大小（`export-manifest.json` 的 `bytes` 同義）。
* 單檔節點數固定 10：1 root mesh + 6 slot 空節點 + 3 port 空節點；頂點屬性
  `position/normal/tangent/color/uv0`。
* 每個檔案的 `Model.Name` = 檔名 stem、`Model.version` = 0。

## 5. 材質對映（ART-01 槽位 → 原版 MaterialCollection 資產名）

ART-01 槽位語意固定 `0 Wood / 1 Metal / 2 Glass / 3 Fabric`；匯出只換名字，不動幾何分面。

| 檔位 | 陣營 | Wood | Metal | Glass | Fabric | 材質數 | 策略 |
|---|---|---|---|---|---|---:|---|
| Basic | Folktails | `BaseWood_Brown.Folktails` | — | — | `ThatchedRoof.Folktails` | 2 | 原木 + 茅草 |
| Basic | IronTeeth | `BaseWood_DarkBrown.IronTeeth` | — | — | `RoofPlanks.IronTeeth` | 2 | 深木 + 屋頂木板 |
| Intermediate | Folktails | `IrregularPlanks_LightBrown.Folktails` | — | `WindowsAtlas.Folktails` | `Plaster_White.Folktails` | 3 | 木板 + 白灰泥 + 玻璃 |
| Intermediate | IronTeeth | `IrregularPlanks_DarkBrown.IronTeeth` | — | `WindowsAtlas.IronTeeth` | `Plaster_Orange.IronTeeth` | 3 | 深木板 + 橘灰泥 + 玻璃 |
| Advanced | Folktails | `BaseWood_White.Folktails` | `PaintedMetal.Folktails` | `WindowsAtlas.Folktails` | `Details.Folktails` | 4 | 白木 + 烤漆金屬框 + 玻璃 + 細節 |
| Advanced | IronTeeth | `BaseWood_Grey.IronTeeth` | `PaintedMetal.IronTeeth` | `WindowsAtlas.IronTeeth` | `Details.IronTeeth` | 4 | 灰木 + 烤漆金屬框 + 玻璃 + 細節 |
| Top | Folktails | `IrregularPlanks_Brown.Folktails` | `BaseMetal.Folktails` | `WindowsAtlas.Folktails` | `Details.Folktails` | 4 | 素金屬 + 玻璃 + 木板 + 鉚接件 |
| Top | IronTeeth | `BaseWood_DarkBrown.IronTeeth` | `BaseMetal.IronTeeth` | `WindowsAtlas.IronTeeth` | `IrregularPlanks_MetalForged.IronTeeth` | 4 | 素金屬 + 玻璃 + 深木 + 鍛金屬板 |
| 施工（每檔） | Folktails | `BaseWood_Brown.Folktails` | `BaseMetal.Folktails` | — | — | 2 | 原木 + 裸金屬 |
| 施工（每檔） | IronTeeth | `BaseWood_DarkBrown.IronTeeth` | `BaseMetal.IronTeeth` | — | — | 2 | 原木 + 裸金屬 |

* **白名單 21 個候選名逐一比對** `%USERPROFILE%\.hdbf\ref\Blueprints\MaterialCollections\MaterialCollection.Folktails.blueprint.json`
  與 `MaterialCollection.IronTeeth.blueprint.json`：21/21 命中；實際用到 20 個
  （`BaseWood_LightBrown.Folktails` 未使用）。`timbermesh_common.MATERIAL_WHITELIST` 的 assert
  另外鎖住「faction 字尾必須與目標陣營一致」。
* 遊戲只依名字到當前陣營材質倉庫解析 → 不需自訂材質資產（本輪也沒有）。
* 驗證 C5：每檔 1–4 個 distinct 材質且全在白名單；並比對 `MATERIAL_MAP` 預期集合
  extra/missing 皆空。16/16 PASS。
* manifest 的 `materials` 欄是**插件 mesh 項的出現順序**＝合併後 mesh 沿多邊形順序走訪時、材質
  第一次被用到的順序（既非字母序、也非槽位順序）；**權威槽位順序**以 §5 的表（slot 0–3）與
  manifest 的 `material_slots_object` 欄為準（該欄才是物件 material slot 順序）。
  例：`Advanced.Folktails` 的 `materials` = [BaseWood_White, PaintedMetal, Details, WindowsAtlas]，
  而 `material_slots_object` = [BaseWood_White, PaintedMetal, WindowsAtlas, Details]。

## 6. 槽位 / 端口座標

### 6.1 作物槽（6 個，2 排 × 3 列，四檔/兩陣營一致）

| 空節點 | contract（13.6 中心軸心） | export／遊戲（角原點） | 高度語意 |
|---|---|---|---|
| `#Slot#Crop_0` | (−1.00, 0.925, −0.25) | (0.50, 0.925, 0.75) | y=0.925 槽體土面 |
| `#Slot#Crop_1` | (0.00, 0.925, −0.25) | (1.50, 0.925, 0.75) | 同上 |
| `#Slot#Crop_2` | (1.00, 0.925, −0.25) | (2.50, 0.925, 0.75) | 同上 |
| `#Slot#Crop_3` | (−1.00, 0.925, 0.20) | (0.50, 0.925, 1.20) | 後排 |
| `#Slot#Crop_4` | (0.00, 0.925, 0.20) | (1.50, 0.925, 1.20) | 後排 |
| `#Slot#Crop_5` | (1.00, 0.925, 0.20) | (2.50, 0.925, 1.20) | 後排 |

### 6.2 連接埠（3 個；13.6 表列 5 類，本卡只要求 3 類）

| 空節點 | contract | export／遊戲 | 朝外 | 13.6 說明 |
|---|---|---|---|---|
| `#Port#Power` | (1.00, 0.85, 1.00) | (2.50, 0.85, 2.00) | +Z | 背面右側，Ø0.12 接線盒 |
| `#Port#Pipe` | (−1.50, 0.40, 0.00) | (0.00, 0.40, 1.00) | −X | 左短邊中心，Ø0.16 法蘭 |
| `#Port#Logistics` | (−1.00, 0.35, 1.00) | (0.50, 0.35, 2.00) | +Z | 背面左端，0.8×0.4 輸送帶開口 |

* 9 個空節點與契約偏差 **0.0000**（容差 0.02，驗證 C6 逐個列 dev）；vertexCount=0、無 mesh。
  **16 個檔案一律含 3 個埠空節點（3/3）**；13.6 的「出現檔位」是外露語意，不是檔案裡有沒有標記。
* 未納入的 2 類：入口（−Z）、屋頂兩支通風口（+Y）——本卡只要求 6 槽 + 3 埠。
* **偏差**：9 個空節點旋轉皆 identity（13.6 要求 local +Z 朝外）；朝外方向只記在
  `PORT_SPECS`/本表，沒有寫進 transform。目前無消費端。

## 7. 驗證摘錄（`art/timbermesh-validation.txt`）

```
mode:       pure-python / Blender-free (zlib + the vendored official plugin's
            protobuf schema model_pb2; no bpy, no Blender, no Unity)
files:      16 expected (8 finished LOD0 + 8 ConstructionStage0), 16 present
tolerances: bounds +-0.050, min corner +-0.050, empty positions +-0.020, uv0 [0,1] +-0.001
tri budget: <= 6000 per file
elapsed:    0.47 s

FILES:  16/16 present; 16 PASS, 0 FAIL
CHECKS: 128 PASS, 0 FAIL, 0 NOT RUN (of 128 = 16 files x 8 checks)
OVERALL VERDICT: PASS
```

逐檔摘要（節錄，完整版在報告）：

| 檔案 | bounds size | min | tris | 材質數 | slots | ports |
|---|---|---|---:|---:|---|---|
| 16 檔全部 | 3.000 / 1.000 / 2.000 | −0.000 / −0.000 / −0.000 | 5,940–5,950（成品）/408（施工） | 2–4 | 6/6 | 3/3 |

檢查項定義：C1 zlib+protobuf 解析、C2 `Model.name`/`version`、C3 包圍盒（角原點）、
C4 三角面 ≤6,000、C5 材質白名單/數量、C6 6 槽 + 3 埠位置、C7 頂點屬性（型別/維度/長度/NaN）、
C8 拓樸（索引/parent/單 root/uv0 範圍）。
驗證器另有 `--reference` 診斷模式（選用；交付的報告本體不含該附錄）：用原版
`ShantySpeaker.IronTeeth.Model.timbermesh` 證明解析器可用，原版照預期 FAIL C2/C3/C6/C8
（它本來就不合我們的契約），不影響 exit code。

## 8. 重跑與確定性證據

### 8.1 命令（工作區根目錄）

```powershell
$env:BLENDER_USER_CONFIG = "$env:TEMP\blender-config"
# 匯出 16 檔 + manifest
& 'C:\Users\user\.hdbf\sdk\blender-4.2.1-windows-x64\blender.exe' -b --factory-startup -noaudio --python art\export_timbermesh.py
# 軸向測試（補償 + 負控制）
& 'C:\Users\user\.hdbf\sdk\blender-4.2.1-windows-x64\blender.exe' -b --factory-startup -noaudio --python art\axis_test.py
# 純 Python 驗證（不需 Blender；16/16 全過才 exit 0）
python art\validate_timbermesh.py
# 圖示（Blender）
& 'C:\Users\user\.hdbf\sdk\blender-4.2.1-windows-x64\blender.exe' -b --factory-startup -noaudio --python art\render_icons.py
```

### 8.2 確定性

第二次完整重跑（同機、Blender 4.2.1 LTS、同來源模型）後，16 個檔案的 SHA-256 與 manifest
紀錄**全部相同（16/16）**。逐檔 hash（可直接 `Get-FileHash` 重驗）：

| 檔案 | SHA-256 |
|---|---|
| `BoxFarm.Basic.Folktails.Model` | `35f7c3b86521636d8dc4b55d3ce51a00e8819cc9d4c48346b95259ce98a896fe` |
| `BoxFarm.Basic.IronTeeth.Model` | `71d345f0704872a3423b1deec1488e3d65836fbd207360cb6c7aa05a28a0c068` |
| `BoxFarm.Intermediate.Folktails.Model` | `a1b1a59d24c63cfefed437cd4756aa871d0d092298200b39920726c854a3723d` |
| `BoxFarm.Intermediate.IronTeeth.Model` | `7aa024b332c6077327a4f5f5f7f2ed65ac8b5c0828dc2a051e003410619db85a` |
| `BoxFarm.Advanced.Folktails.Model` | `c3d33e20a199b360752dff748941932aa16060eb135352317058725c883f660d` |
| `BoxFarm.Advanced.IronTeeth.Model` | `96a21ce8790b297b35abf6f6df25f7debd041ffa82b77586568640fe056d3966` |
| `BoxFarm.Top.Folktails.Model` | `182e6729a000e90553a7c3564ab4a655f82b69726b520bb909b878a6e389074b` |
| `BoxFarm.Top.IronTeeth.Model` | `caf2311e3e1734863ebcacfc90035092fd739625c16d789b21cc20a81122911b` |
| `BoxFarm.Basic.Folktails.ConstructionStage0.Model` | `bc9bc5da0f0b09a40d2dcafed757cd1c6d21c193f0ac2f569fc35fbb071c75c1` |
| `BoxFarm.Basic.IronTeeth.ConstructionStage0.Model` | `2e5cedcb14fb4b3b4d602cf886ed5358170e4406ae5581dfda4905e5e304392e` |
| `BoxFarm.Intermediate.Folktails.ConstructionStage0.Model` | `ca357f2b500fe2401f103a51f3f265e3580e16acec063392808ccb823028ef53` |
| `BoxFarm.Intermediate.IronTeeth.ConstructionStage0.Model` | `0db21d9308fa8449ab5f7e3a85dc09e599be0de079267b9a991498352f0929ec` |
| `BoxFarm.Advanced.Folktails.ConstructionStage0.Model` | `88ad8ae6045737d131a0c355906403bc5f10508aaf6b1a647daf6d8db899322d` |
| `BoxFarm.Advanced.IronTeeth.ConstructionStage0.Model` | `d0fb023f88e7c3413fe9a720c6ee4f30b58f05b032c910a9f61b0c579f1f4e1b` |
| `BoxFarm.Top.Folktails.ConstructionStage0.Model` | `af786bca445df43c1554d773b5dfa29615805647bbd15271c4a8b8b583b253b9` |
| `BoxFarm.Top.IronTeeth.ConstructionStage0.Model` | `1c0c4a9ca15b9a515c7416423ff7af3baf95d1d205b7720cc3f7e1434af237dd` |

* 來源是 manifest 的 `sha256` 欄（`export_timbermesh.py` 對每個輸出檔直接算）。
* 確定性成立的前提：同一台機器、同一 Blender 4.2.1 LTS、同一 `art/models/` 來源、同一
  `timbermesh_common.py`（契約凍結）。改其中任一項都應預期 hash 變動，重跑後請重新比對。

## 9. 圖示（`art/icons/`，task-1）

* **8 張 PNG**：4 檔 × 2 尺寸（512×512、128×128），全部 RGBA 8-bit、透明背景；來源
  `art/models/<Tier>.Folktails/LOD0.fbx`（兩陣營共用同一組圖示）。
* 渲染：Blender 4.2.1 LTS、Cycles CPU、64 samples、OpenImageDenoise、seed 0、
  Standard view transform、exposure 0.25、無地面、無文字/LOGO。
* 相機（四檔共用一台）：perspective 85 mm / 36 mm sensor、位置 (−5.067928, 7.310596, 5.886399)、
  look-at (0, 0, 0.5)、距離 10.398、投影填滿率 0.84（每邊 8% 留白）、azimuth 34.7° / elevation 32°。
* 128×128 由 512×512 以 alpha-aware premultiplied 4×4 box filter 降採樣（只渲染一次）。

| 檔案 | 尺寸 | bytes | 覆蓋率 | SHA-256 |
|---|---|---:|---:|---|
| `BoxFarm.Basic.png` | 512² | 263,365 | 0.2945 | `2431e8e1ec4a4c9af9c6e709eb65130d3d3789fd64918a0976c472331f94791d` |
| `BoxFarm.Basic.128.png` | 128² | 14,941 | 0.2943 | `5f8e87751686c9bc6fbc5377d7a50024317fd6ed164ba486ec7dcb70b9ff0e9f` |
| `BoxFarm.Intermediate.png` | 512² | 272,174 | 0.2976 | `5f6bbf1e582f7338c4f495cfd39dd2f7ffd4c4d608a43937ce9ca61d6886819f` |
| `BoxFarm.Intermediate.128.png` | 128² | 15,622 | 0.2974 | `029b44c9562bc981d59e03f7b05310e36c455ad90c01b2258f47a2e8b1dd9c51` |
| `BoxFarm.Advanced.png` | 512² | 283,525 | 0.2978 | `5961bf9c66dea302ffef60fe1fdb5d02c3d5adf2946a0850ae6fc6c6ce39aa9b` |
| `BoxFarm.Advanced.128.png` | 128² | 16,558 | 0.2988 | `371bb93ee9688409e7520c45d2db575d46a6d77bbf98d9eb03d627384c3afce8` |
| `BoxFarm.Top.png` | 512² | 270,356 | 0.3054 | `9cd45a9b54bd1b0396efa81938c384e2fcb71f74b6019c0f162ddc37f8c20a9c` |
| `BoxFarm.Top.128.png` | 128² | 16,020 | 0.3052 | `6dcb3367db426dccd083a0841dee399ffaf0e7d4c450d25628a04528b40c4996` |

* 四檔確實不同：512 版兩兩 `mean_abs_rgb_diff` **23.582–41.335**、主體覆蓋率
  0.2945–0.3054（差 ≤ 0.0109）；RGB 差最小的一對是 Basic vs Intermediate（23.582），
  覆蓋率最接近的是 Intermediate vs Advanced（差 0.0002、mean\|ΔRGB\| 27.067）。
* 整批 8 張的渲染實跑約 34 s（icon-renderer 回報；manifest 已不記 per-tier 時間）；128×128 是
  512×512 的降採樣，不另外渲染。
* manifest `verification`：`expected_file_count` 8、`files_ok` 8、`failures` []、**verdict PASS**。
* **可重跑／確定性**：PNG 內的 Blender `tEXt` Date/RenderTime 中繼資料已剝除
  （`render.metadata_stripped = true`）、並以 bpy image API 重寫；同機連續兩次完整重跑 →
  8 張 PNG 與 `icons-manifest.json` 的 SHA-256 **完全相同**，逐像素比較每通道 maxdiff = 0。
  限制：只證明本機 / Blender 4.2.1 LTS 這個建置，換 CPU 或 Blender 建置（OIDN/Cycles CPU）
  仍可能不同；先前看到的 bytes 差異是迭代與 PNG Date/RenderTime metadata，不是像素改變。
* **誠實**：這是離線渲染，不是遊戲截圖；圖示接進建造選單（`data/Icons/BoxFarm<Tier>.png`
  目前是約 1 KB 佔位圖）是 Lead 的動作，本輪未動 `data/`。

## 10. 偏差與限制（誠實）

1. **座標補償偏離 13.3/13.6 字面**：export = contract + (1.5, 0, 1.0)，因為遊戲模型空間是
   block-corner（證據 E1–E6）。若未來改成中心軸心，需還原此補償。
2. **`Model.Name` 由我們 post-process 蓋上**：官方插件輸出空字串（驗證器 `--reference` 模式
   讀到的原版 ShantySpeaker 就是 `Model.name = ''`），本管線蓋成檔名 stem 供辨識/驗證；
   此欄位對載入的影響本輪未在遊戲中驗證。
3. **材質只給名字、沒有自訂 PBR AssetBundle**：遊戲用原版 atlas 採樣 ART-01 的逐面唯一 UV，
   遠看材質類別正確、近看是拼布。UV0 已保留，未來補材質不需重匯出幾何。
4. **只交付 LOD0**：沒有 LOD1/LOD2 `.timbermesh`，靠 `Timberborn.PrefabOptimization` 自動 LOD；
   `art/timbermesh/lod/` 刻意不存在。手動 LOD 需求目前不存在。
5. **9 個空節點旋轉 identity**：13.6 的「local +Z 朝外」沒寫進 transform（無消費端）。
6. **施工階段每 tier 各一份**（8 檔幾何/材質相同，僅路徑不同）：卡要求 16 檔；若 Lead 想省檔案可
   只留一份共用（本輪沒這樣做）。
7. **未經 Unity 匯入**：管線是 Blender → `.timbermesh`；沒有 Unity prefab / LODGroup / 材質檢查。
8. **圖示是離線渲染，不是遊戲截圖**（見 §9）。
9. **沒有實機驗證**：外觀、擺放、施工階段、圖示在遊戲中的實際呈現全部未驗。

## 11. 團隊協作

| Task | Owner | 產出 | 結果 |
|---|---|---|---|
| task-1 | icon-renderer | `art/render_icons.py`、`art/icons/`（4 檔 × 512/128 RGBA）、`icons-manifest.json` | 完成；8/8 PNG、manifest verdict PASS、四檔可辨識（mean\|ΔRGB\| 23.582–41.335） |
| task-2 | validator | `art/validate_timbermesh.py`、`art/timbermesh-validation.txt` | 完成；16/16 PASS、128/128 檢查、exit 0；另提供 `--reference` 診斷（原版 ShantySpeaker 預期 FAIL，交付報告本體不含附錄） |
| task-3 | docs-writer | `art/README-timbermesh.md`、`docs/workstreams/art-02-timbermesh.md`（本檔） | 完成；數字全部取自 manifest / 驗證報告 / 軸測報告 / icons-manifest |
| （Lead） | Lead | `timbermesh_common.py`、`export_timbermesh.py`、`axis_test.py`、`art/timbermesh/`（16 檔 + manifest + axis-test）、§3 的座標推導 | 完成；16/16 匯出、軸測 PASS、二次重跑 hash 相同 |

協作介面：`art/timbermesh_common.py` 是凍結契約（三個腳本 runtime import 它，不各自複製常數）；
寫入範圍互斥（匯出器/輸出、驗證器/報告、圖示、文件），沒有互相覆蓋。
本文件作者只寫兩個 `.md`；`art/` 其餘檔案與 `data/`、`src/` 未動。

## 12. 下一步（Lead）

1. **放檔**：`art/timbermesh/*.timbermesh`（16 檔）→ `data/Buildings/Food/BoxFarm/`。
2. **改 8 個 blueprint 的兩個 Model 路徑**（其餘欄位不動）：
   * `Children.#Finished.TimbermeshSpec.Model` = `Buildings/Food/BoxFarm/BoxFarm.<Tier>.<Faction>.Model`
   * `Children.#Unfinished.Children.ConstructionStage0.TimbermeshSpec.Model` =
     `Buildings/Food/BoxFarm/BoxFarm.<Tier>.<Faction>.ConstructionStage0.Model`
3. **`CollidersSpec` 保持原樣**（center (1.5, 0.5, 1.0)、size (3.0, 1.0, 2.0) 已正確），
   `HdbfBoxFarmSpec` 不動。
4. `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Install`（含 tools/validate + staging + 部署）。
5. **實機驗收**：4 檔外觀/圖示一致、貼齊 3×2 佔地與正面朝向、施工階段→完工替換、遠近 LOD 無跳動、
   建造選單圖示正常。失敗時看 `docs/in-game/` 的 Player.log/截圖（流程見 `docs/IN_GAME_VERIFICATION.md`）。
6. 若外觀因原版 atlas 拼布不可接受，另開一卡做自訂 PBR 材質（UV0 已在檔內，不需重匯出幾何）。

## 13. 仍未做（非本輪範圍）

* 遊戲內驗證（外觀、擺放、LOD、施工階段、圖示）與 50% 鏡頭檢視。
* 自訂 PBR 材質 / shader / AssetBundle（含把原版 atlas 換成 ART-01 的 2048² 圖集）。
* LOD1/LOD2 `.timbermesh` 與手動 LODGroup（若之後放棄自動 LOD）。
* 空節點旋轉（local +Z 朝外）與端口消費端接線。
* Unity 匯入流程（若要走 prefab 端做材質/pivot 二次檢查）。
