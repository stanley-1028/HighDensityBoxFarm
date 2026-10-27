# HDBF 盒子農場 — ART-04 `.timbermesh` 匯出與材質貼片交接（`art/`）

> **v2 更新（2026-10-26）：本文件是 ART-04（3×2×1）歷史交接文檔。**
> 現行交付是 v2 **3×3×1** 室內高密度農場：生成器 `art/gen_models_v2.py`、驗證器
> `art/validate_models.py` / `art/validate_timbermesh.py`、匯出器 `art/export_timbermesh.py`
> （v2 已更新）、契約文檔 `data/ART_PLACEHOLDER.md`。本文件中的 3×2 座標、平台尺寸、
> 節點/材質表與 ART-02/04 報告數字**已過時**，僅作歷史記錄保留；v2 量測值以
> `art/validation-report.txt`、`art/timbermesh-validation.txt`、`art/timbermesh/export-manifest.json`
> 與 `data/ART_PLACEHOLDER.md` 為準。

> **v2.2 更新（2026-10-26，参考图驱动材质/开窗升级）**：外包络 3×3×1、平屋顶、3 端口、0 `#Slot#` 与
> flat-patch 契约不变；材质分区/开窗比例/四档可辨度升级。**本文件下方 §4–§14 仍是 ART-04（3×2×1）的历史
> 数值，不要引用**；v2.2 现行数值以本节 + `data/ART_PLACEHOLDER.md` + `art/timbermesh/export-manifest.json`
> + `art/validation-report.txt` / `art/timbermesh-validation.txt` 为准。

### v2.2 数值速查（现行）

| 项 | v2.2（2026-10-26 实测） |
|---|---|
| 16 档合计 | **1,236,471 bytes / 53,856 tris / 126,064 vertices** |
| 成品 8 档 LOD0 tri | 5,850 / 5,850 / 5,854 / 5,854 / 5,850 / 5,850 / **5,918** / **5,918**（预算 6,000，最高 5,918） |
| 施工灰盒 8 档 | 864 tris / 2,166 verts each（合计 140,975 B） |
| 包络 | export space min (0,0,0)、size (3.000, 1.000, 3.000)，`±0.02`；C3 16/16 PASS |
| 平屋顶 | 走行面 = 模型最高面 **y = 1.00**；25/25 源模型 `TOP=APEX=1.0000, ABOVE_TOP=0`；顶面框架边带顶 1.00，平台下缘梁 0.86–0.95 |
| 端口 | 3 个：Power(1.00,0.85,1.50)→(2.50,0.85,3.00)、Pipe(−1.50,0.40,0.00)→(0.00,0.40,1.50)、Logistics(−1.00,0.35,1.50)→(0.50,0.35,3.00)（contract→export）；偏差 0.0000；**0 个 `#Slot#`** |
| flat patch | **12 个 role**（v2.2 新增 `moss` / `plaster`（原 canvas 改名）/ `planks_dark`），材质名仍是原版 |
| Basic.Folktails 第二材质 | `ThatchedRoof.Folktails` → **`Moss.Folktails`**（原版 MaterialCollection 名称；苔藓/作物剪影） |
| 玻璃占比（侧墙） | Basic 0% / Intermediate **54.0%** / Advanced **53.3%** / Top **53.5%**（目标 45–55%） |
| 验证 | 源模型 C1–C10 **PASS**（25/25）；`.timbermesh` **16/16 PASS、128/128 checks**；数据 `data==art` 16/16 SHA-256 相同 |
| 生成确定性 | 图集 CROSS-RUN PASS；源模型两次生成 **50/50 文件字节一致**（FBX 时间戳+UID 已固定）；两次导出 16/16 SHA-256 相同 |
| 预览 | `art/previews/` **48 张**：8 模型 × 5 视角 + 施工 2 + 6 张 50% A/B 合图（含两阵营 A/B 与 4×2 全档格） |
| L1 | `dotnet run --project tests\HDBF.Tests ...` → **PASSED 530 / FAILED 0** |

状态：**離線匯出 + 純 Python 驗證完成**（16/16 檔 PASS、128/128 檢查 PASS、exit 0）。
**未在遊戲中驗證**：本輪沒有部署、沒有啟動 Timberborn、沒有跑 `build.ps1`、沒有碰 Unity；
所有「看起來如何」的敘述都是 `待實機確認`。

ART-04（2026-10-25 使用者指示）把 ART-01/02/03 的模型改成：

1. **屋頂 = 單一空平台**：移除 6 個抬高作物槽（槽體、土壤、槽緣）與 `#Slot#Crop_0..5` 空節點，
   屋頂只留一片平的、空的平台（薄收邊 0.04，平台頂面 0.96、最高點 1.00）。
2. **模型沒有作物/土壤視覺**：地面作物的隱藏仍由 BUILD-05 的 runtime 視覺抑制負責，模型不帶。
3. **自製一致材質外觀（flat UV patch）**：沿用原版 `MaterialCollection` 材質資產名（分陣營），
   但把**同一材質槽的所有面固定映射到該材質的一小塊固定 UV patch**，不再逐面亂取樣 atlas；
   造型細節改由**幾何**（板縫、框料、鉚釘、腳座、門框、收邊）表現。**不需要 Unity/AssetBundle**。

> 使用者原話（逐字保留）：「你还是自己造一个材質一样的吧,至於农作物的顯示就不用且沒必要了屋頂还是別放東西,並只有一個空平台吧。」

本文件同時是 **「自製材質外觀對照表」**（§6）與重跑/驗收交接（§2、§12）。
完整工作報告：[docs/workstreams/art-04-flat-roof-own-material.md](../../docs/workstreams/art-04-flat-roof-own-material.md)。

---

## 1. 產出物清單

```
art/
├── timbermesh/                                    ← 16 個 .timbermesh（交付物本體）
│   ├── BoxFarm.<Tier>.<Faction>.Model.timbermesh                   ×8  成品（LOD0）
│   ├── BoxFarm.<Tier>.<Faction>.ConstructionStage0.Model.timbermesh ×8  施工階段灰盒
│   ├── export-manifest.json                       匯出清單（bytes/tris/vertices/materials/ports/sha256）
│   ├── export-determinism.txt                     兩次匯出的 16/16 SHA-256 一致性證據
│   └── axis-test/                                 ART-02 時代的軸向/單位測試（幾何轉換未變，仍可重跑）
├── models/**                                      ART-04 來源模型（LOD0/1/2 FBX+GLB、ConstructionStage0）
├── material_patches.py                            flat-patch 契約（patch 表 + 材質名→patch 對照）
├── material_slots.json                            機器可讀的材質/patch/平台/模型清單
├── gen_models.py                                  ART-04 生成器（空平台、flat-patch UV、幾何細節）
├── export_timbermesh.py                           Blender 4.2.1 headless 批次匯出器
├── validate_timbermesh.py                         純 Python 驗證器（不需 Blender / bpy / Unity）
├── timbermesh-validation.txt                     驗證報告（16 檔逐項 C1–C8，OVERALL VERDICT: PASS）
├── c9-art04-silhouette-probe.txt                 ART-04 的 C9（50% 剪影）離線重跑證據（12/12 PASS）
├── previews/                                      離線三視圖 / 50% / iso 預覽（非遊戲截圖）
├── icons/                                         4 檔 × 2 尺寸 PNG + icons-manifest.json
└── tools/timbermesh_blender_plugin/               官方插件 vendored 副本 v1.2.0（MIT）
```

| 檔案 | 角色 |
|---|---|
| [`material_patches.py`](material_patches.py) | **flat-patch 唯一事實來源**：`PATCH_PIXELS`（patch pixel/UV rect）、`ROLE_OF_MATERIAL`（原版材質名→patch）、`PATCH_TEXELS_PER_GRID = 64` |
| [`material_slots.json`](material_slots.json) | 由 `gen_models.py` 產生的機器可讀表：每个 model/LOD 的材質、patch、平台尺寸、三角數 |
| [`timbermesh_common.py`](timbermesh_common.py) | 凍結契約：座標、尺寸、材質白名單、`MATERIAL_MAP`、3 個埠座標、平台常數 |
| [`gen_models.py`](gen_models.py) | 產生 `art/models/**` 與 `material_slots.json`（deterministic） |
| [`export_timbermesh.py`](export_timbermesh.py) | 16 檔 + manifest（exit 0 才算 PASS；內建契約檢查） |
| [`validate_timbermesh.py`](validate_timbermesh.py) | 純 Python 解析 protobuf + C1–C8 契約檢查 |
| [`timbermesh-validation.txt`](timbermesh-validation.txt) | 逐檔逐項證據 + SUMMARY（16 檔 × C1–C8） |
| [`render_previews.py`](render_previews.py) / [`render_icons.py`](render_icons.py) | 離線 Blender 渲染（非遊戲截圖） |

插件來源：官方 `timbermesh_blender_plugin` **v1.2.0**，MIT，vendored 於
`art/tools/timbermesh_blender_plugin/`。**執行期不需要 `C:\Users\user\.hdbf` 下的任何東西，
除了 blender.exe 本身**：插件、protobuf schema、來源模型、驗證器全在工作區。

## 2. 重跑方式（完整命令）

全部命令都在工作區根目錄執行。Blender 為 4.2.1 LTS headless（`--factory-startup` 確保不吃使用者設定）。

```powershell
$env:BLENDER_USER_CONFIG = "$env:TEMP\blender-config"

# 0) 重建 ART-04 來源模型（25 檔：8 tier×陣營 × LOD0/1/2 + ConstructionStage0）+ material_slots.json
& 'C:\Users\user\.hdbf\sdk\blender-4.2.1-windows-x64\blender.exe' -b --factory-startup -noaudio --python art\gen_models.py

# 1) 匯出 16 個 .timbermesh + export-manifest.json（exit 0 = 16/16 成功）
& 'C:\Users\user\.hdbf\sdk\blender-4.2.1-windows-x64\blender.exe' -b --factory-startup -noaudio --python art\export_timbermesh.py

# 2) 驗證：純 Python，不需要 Blender（寫 timbermesh-validation.txt，exit 0 = 全 PASS）
python art\validate_timbermesh.py

# 3)（可選）離線預覽 / 圖示重渲染（不是遊戲截圖）
& 'C:\Users\user\.hdbf\sdk\blender-4.2.1-windows-x64\blender.exe' -b --factory-startup -noaudio --python art\render_previews.py
& 'C:\Users\user\.hdbf\sdk\blender-4.2.1-windows-x64\blender.exe' -b --factory-startup -noaudio --python art\render_icons.py
```

* 步驟 0/1/3 只需 blender.exe；步驟 2 只需 CPython（本輪實跑 CPython 3.14.7、約 5.9 s）。
* `export_timbermesh.py` 任何一檔失敗即 `EXPORT RESULT: FAIL` + exit 1（manifest 的 `failures` 有明細）。
* `validate_timbermesh.py` 只有「16 個預期檔案都在、每檔 C1–C8 全過」才 exit 0。
* **確定性**：同機、同 Blender、同來源，連續兩次完整匯出 → **16/16 SHA-256 相同**
  （本輪實測；證據：[`timbermesh/export-determinism.txt`](timbermesh/export-determinism.txt)）。

## 3. 座標：需求文件的假設 vs 遊戲的實際模型空間

| | 需求 13.3 / 13.6 的座標系（「contract space」） | 遊戲 `.timbermesh` 的模型空間（「export space」） |
|---|---|---|
| 原點 | 佔地**底面中心**（X=0, Z=0, Y=0） | **Block 角落**（該格左下前角） |
| X | [−1.5, +1.5] | [0, 3]（寬） |
| Y | [0, 1]（向上） | [0, 1]（向上） |
| Z | [−1, +1]，正面 −Z | [0, 2]，正面在 **Z=0** |

* 遊戲讀 `.timbermesh` 時**不會**替我們平移半個佔地；若照 contract 座標直出，整棟會往 −X 偏 1.5 格、
  往 −Z 偏 1.0 格。ART-02 的量測（官方 SDK 範例 ShantySpeaker、原版 blueprint `CollidersSpec`、
  本專案 8 個 blueprint 的碰撞盒、以及「不補償」負控制）確立 export space 是 **block-corner 原點**。
* 換算：`export = contract + (1.5, 0, 1.0)`；交付檔案量測全部是 **min=(0,0,0)、max=(3,1,2)**。
* 官方插件 v1.2.0 的固定映射是 `export = (−blender.x, blender.z, −blender.y)`（含 X/Z 鏡射與
  winding 反轉、不做置中平移）。匯出器先寫成反函數
  `blender_out = (−src.x − 1.5, src.y − 1.0, src.z)`，再 `flip_winding()` 抵銷插件那次反轉，
  最後得到 `export = contract + (1.5, 0, 1.0)`。同一組公式以純 Python 形式保存在
  `timbermesh_common.source_to_export_blender()` 與驗證器/匯出器共用的常數。
* ART-04 **沒有改座標補償**；ART-02 的 `axis-test/` 證據與負控制仍然有效（可重跑）。

## 4. 16 檔輸出清單（**ART-04 3×2×1 歷史值；v2.2 現行值見頂部速查表與 `export-manifest.json`**）

| 檔案（`art/timbermesh/` 下） | bytes | 三角面 | 頂點 | 材質數 |
|---|---:|---:|---:|---:|
| `BoxFarm.Basic.Folktails.Model` | 148,999 | 5,944 | 14,380 | 2 |
| `BoxFarm.Basic.IronTeeth.Model` | 148,660 | 5,944 | 14,372 | 2 |
| `BoxFarm.Intermediate.Folktails.Model` | 147,832 | 5,944 | 14,326 | 3 |
| `BoxFarm.Intermediate.IronTeeth.Model` | 146,779 | 5,944 | 14,254 | 3 |
| `BoxFarm.Advanced.Folktails.Model` | 150,326 | 5,944 | 14,355 | 4 |
| `BoxFarm.Advanced.IronTeeth.Model` | 149,746 | 5,944 | 14,333 | 4 |
| `BoxFarm.Top.Folktails.Model` | 150,934 | 5,948 | 14,342 | 4 |
| `BoxFarm.Top.IronTeeth.Model` | 150,306 | 5,948 | 14,332 | 4 |
| `BoxFarm.Basic.Folktails.ConstructionStage0.Model` | 9,142 | 348 | 834 | 2 |
| `BoxFarm.Basic.IronTeeth.ConstructionStage0.Model` | 9,146 | 348 | 834 | 2 |
| `BoxFarm.Intermediate.Folktails.ConstructionStage0.Model` | 9,151 | 348 | 834 | 2 |
| `BoxFarm.Intermediate.IronTeeth.ConstructionStage0.Model` | 9,156 | 348 | 834 | 2 |
| `BoxFarm.Advanced.Folktails.ConstructionStage0.Model` | 9,147 | 348 | 834 | 2 |
| `BoxFarm.Advanced.IronTeeth.ConstructionStage0.Model` | 9,151 | 348 | 834 | 2 |
| `BoxFarm.Top.Folktails.ConstructionStage0.Model` | 9,141 | 348 | 834 | 2 |
| `BoxFarm.Top.IronTeeth.ConstructionStage0.Model` | 9,144 | 348 | 834 | 2 |
| **合計（16 檔）** | **1,266,760** | **50,344** | **121,366** | — |

* 成品 8 檔：1,193,582 B / 47,560 tris；施工 8 檔：73,178 B / 2,784 tris。LOD0 預算 6,000/檔，最高 5,948。
* 每檔都是 **4 個節點**：1 個 root mesh 節點 + 3 個埠空節點（`#Port#Power/#Port#Pipe/#Port#Logistics`）；
  **0 個 `#Slot#`**。
* 頂點屬性：`position`(3)、`normal`(3)、`tangent`(4)、`color`(4)、`uv0`(2)。
  `color` 是 ART-04 新加的**每面一色**嘗試（16/16 檔都帶非白色值）；**shader 是否使用未知，待實機確認**。
* 檔案是 `zlib.compress(model_pb2.Model.SerializeToString())`；磁碟 bytes 是壓縮後大小。
  每檔 SHA-256（16 個完整值）見 `art/timbermesh/export-manifest.json` 或
  `data/ART_PLACEHOLDER.md` §1。

## 5. 屋頂：單一空平台（幾何契約）

* （**v2.2 正確值**）平台走行面 **Y = 1.00**（= 模型最高點）、平台板底 0.95；四周為與走行面同高的
  **框架邊帶**（frame 材質，凸出頂面 0.000 m）與齊平 0.04 收邊；平台下方另有 0.86–0.95 的屋頂邊梁。
  下列 ART-04 的 0.96 平台描述**已作廢**，僅保留歷史。
* **平台上面沒有任何物件**：驗證器 C6 檢查「平台平面內、平台頂面 +0.06 以上沒有任何頂點」，
  16/16 檔 `above=0`；平台頂面本身存在於 0.96 ± 0.01。
* 平台**平面尺寸**是四檔的辨識來源之一（空屋頂下不再有屋頂剪影差異）：

  | Tier | 平台 X × Z（v2.2） |
  |---|---|
  | Basic | 2.84 × 2.84 |
  | Intermediate | 2.92 × 2.92 |
  | Advanced | 2.96 × 2.96 |
  | Top | 2.98 × 2.98 |
  | ConstructionStage0（8 檔共用灰盒） | 2.90 × 2.90 |

* 沒有槽體、土壤、通風帽、雨棚、山牆、龍門架、輸送帶罩；沒有 `#Slot#Crop_*` 節點。
* 平台下方仍有支撐柱/樑/托架（屬結構，位於平台底 0.91 以下），不違反空頂契約。

## 6. 自製材質外觀對照表（flat UV patch）

### 6.1 機制

`.timbermesh` 每個 mesh 只帶**材質名字串**與頂點 **uv0**；遊戲拿材質名到當前陣營的
`MaterialCollection` 解析成原版材質，再用 uv0 採樣該材質的貼圖。ART-02/03 是逐面唯一 UV0
→ 原版 atlas 近看是拼布。ART-04 改成：

* **同一個材質槽的所有面，全部映射到該材質的一小塊固定紫外線矩形（flat patch）**；
* patch 內以固定 texel 密度（`PATCH_TEXELS_PER_GRID = 64`）做小範圍平鋪，面與面之間有
  deterministic 的小偏移，但**絕不跨到別的材質區**；
* 造型細節（板縫、框料、鉚釘、腳座、門框、平台收邊/托架）改由幾何承擔；
* 材質名維持原版（分陣營），**不需要 Unity / AssetBundle / 自訂貼圖**。

### 6.2 patch 表（權威＝`art/material_patches.py`，機器可讀＝`art/material_slots.json`）

| patch id | atlas 分區 | pixel rect（2048²，左上原點） | UV rect (u0,v0,u1,v1) | texels | 使用它的原版材質名 |
|---|---|---|---:|---:|---|
| `wood_plain` | wood | 96,352,480,736 | 0.046875,0.640625,0.234375,0.828125 | 384×384 | BaseWood_Brown.Folktails, BaseWood_DarkBrown.IronTeeth |
| `wood_light` | wood | 96,1056,480,1440 | 0.046875,0.296875,0.234375,0.484375 | 384×384 | BaseWood_LightBrown.Folktails, BaseWood_White.Folktails, BaseWood_Grey.IronTeeth |
| `planks_light` | wood | 96,1568,480,1952 | 0.046875,0.046875,0.234375,0.234375 | 384×384 | IrregularPlanks_LightBrown.Folktails, IrregularPlanks_DarkBrown.IronTeeth |
| `planks_brown` | wood | 528,352,784,608 | 0.257812,0.703125,0.382812,0.828125 | 256×256 | IrregularPlanks_Brown.Folktails |
| `metal_base` | metal | 1056,384,1440,768 | 0.515625,0.625,0.703125,0.8125 | 384×384 | BaseMetal.Folktails, BaseMetal.IronTeeth |
| `metal_painted` | metal | 1056,1088,1440,1472 | 0.515625,0.28125,0.703125,0.46875 | 384×384 | PaintedMetal.Folktails, PaintedMetal.IronTeeth |
| `metal_details` | metal | 1056,1568,1312,1824 | 0.515625,0.109375,0.640625,0.234375 | 256×256 | Details.Folktails, Details.IronTeeth, IrregularPlanks_MetalForged.IronTeeth |
| `glass` | glass | 1744,704,1832,792 | 0.851562,0.613281,0.894531,0.65625 | 88×88 | WindowsAtlas.Folktails, WindowsAtlas.IronTeeth |
| `plaster`（v2.2 由 canvas 改名） | plaster | 1872,288,2000,416 | 0.914062,0.796875,0.976562,0.859375 | 128×128 | Plaster_White.Folktails, Plaster_Orange.IronTeeth |
| `roof` | thatch | 1872,1472,2000,1600 | 0.914062,0.21875,0.976562,0.28125 | 128×128 | ThatchedRoof.Folktails（保留映射，本版模型不再使用） |
| `planks_dark`（v2.2 新增） | wood | 528,700,784,956 | 0.257812,0.533203,0.382812,0.658203 | 256×256 | RoofPlanks.IronTeeth |
| `moss`（v2.2 新增） | moss | 1900,940,2020,1060 | 0.927734,0.482422,0.986328,0.541016 | 120×120 | Moss.Folktails |

> patch 尺寸是 **88–384 texels**（不是「64×64」）：64 是 **texel/grid 密度**，patch 大小依
> 該材質需要覆蓋的最大面而定（例如玻璃 88×88、金屬板 384×384）。patch 都落在 ART-01
> 2048² 圖集的語意分區內、距分區邊界 ≥4 px，且兩兩不重疊（`material_patches.validate_contract()`）。

### 6.3 材質名對映（沿用 ART-02，分陣營；權威＝`timbermesh_common.MATERIAL_MAP`）

| Tier | Faction | # | 材質（原版 MaterialCollection 資產名） |
|---|---|---:|---|
| Basic | Folktails | 2 | BaseWood_Brown.Folktails, **Moss.Folktails**（v2.2；原 ThatchedRoof） |
| Basic | IronTeeth | 2 | BaseWood_DarkBrown.IronTeeth, RoofPlanks.IronTeeth |
| Intermediate | Folktails | 3 | IrregularPlanks_LightBrown.Folktails, WindowsAtlas.Folktails, Plaster_White.Folktails |
| Intermediate | IronTeeth | 3 | IrregularPlanks_DarkBrown.IronTeeth, WindowsAtlas.IronTeeth, Plaster_Orange.IronTeeth |
| Advanced | Folktails | 4 | BaseWood_White.Folktails, PaintedMetal.Folktails, WindowsAtlas.Folktails, Details.Folktails |
| Advanced | IronTeeth | 4 | BaseWood_Grey.IronTeeth, PaintedMetal.IronTeeth, WindowsAtlas.IronTeeth, Details.IronTeeth |
| Top | Folktails | 4 | IrregularPlanks_Brown.Folktails, BaseMetal.Folktails, WindowsAtlas.Folktails, Details.Folktails |
| Top | IronTeeth | 4 | BaseWood_DarkBrown.IronTeeth, BaseMetal.IronTeeth, WindowsAtlas.IronTeeth, IrregularPlanks_MetalForged.IronTeeth |
| 施工（8 檔） | 各自陣營 | 2 | Folktails: BaseWood_Brown.Folktails, BaseMetal.Folktails / IronTeeth: BaseWood_DarkBrown.IronTeeth, BaseMetal.IronTeeth |

**誠實的限制**：flat patch 採樣的是**原版材質貼圖上的固定小區域**，不是我們畫的新貼圖；
顏色/紋理由原版資產決定。一致（不拼布）是 ART-04 買到的東西；真正自訂 PBR 材質仍需 Unity
AssetBundle（本輪沒做）。貼片放大到整面牆的觀感、法線/roughness 取樣、mipmap 行為
**全部待實機確認**，本文件不聲稱「好看」。

## 7. 幾何細節（補回貼圖不再承擔的造型）

* 板縫：背牆/側牆細長條（LOD0 每面 8–13 條，LOD1 減量）。
* 框料：四角柱、頂圈樑、牆頂橫帶、基礎收邊。
* 鉚釘：小方塊（低面數），沿基礎/牆頂/平台收邊排列；Advanced/Top 更密。
* 腳座：基礎四角 0.20×0.06×0.20 pad。
* 門框：門楣 + 兩側門柱；門板依 tier 用木板/金屬板/帆布。
* 平台：收邊、外側 fascia 鉚釘列、下方托架/樑/支撐柱。
* **全部細節都低於平台頂面/信封內**；驗證器 C3（bounds）與 C6（空平台）會抓任何溢出。

## 8. 空節點：只有 3 個埠（沒有槽）

| 空節點 | contract（中心軸心，v2.2） | export／遊戲（block corner） | 朝外 | 語意 |
|---|---|---|---|---|
| `#Port#Power` | (1.00, 0.85, 1.50) | (2.50, 0.85, 3.00) | +Z | 背面右側接線盒 |
| `#Port#Pipe` | (−1.50, 0.40, 0.00) | (0.00, 0.40, 1.50) | −X | 左短邊中心法蘭 |
| `#Port#Logistics` | (−1.00, 0.35, 1.50) | (0.50, 0.35, 3.00) | +Z | 背面左端輸送帶開口 |

* 空節點 vertexCount=0、無 mesh、不影響包圍盒；位置偏差 16/16 檔 = 0.0000（容差 0.02）。
* **0 個 `#Slot#`**；6 個作物槽標記已移除。每檔 4 節點（1 root + 3 ports）。
* rotation 仍為 identity（local +Z 朝外只記錄在 `timbermesh_common.PORT_SPECS`，沒有消費端）。

## 9. 驗證摘要（`timbermesh-validation.txt`）

```powershell
python art\validate_timbermesh.py
# → FILES:  16/16 present; 16 PASS, 0 FAIL
# → CHECKS: 128 PASS, 0 FAIL, 0 NOT RUN (128 = 16 files x 8 checks)
# → OVERALL VERDICT: PASS   （exit 0）
```

* 模式：pure-python / Blender-free（zlib + vendored `model_pb2`；不需要 bpy/Blender/Unity）。
* 檢查項（ART-04）：
  * C1 解析（zlib + protobuf）；C2 身分（`Model.name`=stem、`version`=0）。
  * C3 包圍盒：size (3,1,3) 且 min (0,0,0)，±0.02（block-corner；v2.2 現行）。
  * C4 三角面 ≤ 6,000/檔。
  * C5 材質數 1–4 且全在該陣營白名單（v2.2 白名單含 `Moss.Folktails`）。
  * **C6 = 3 個埠空節點（位置 ±0.02、vertexCount=0）+ 0 個 `#Slot#` + 空屋頂平台檢查**
    （平台平面內頂面存在、走行面 y=1.00、其上 0 頂點）。**沒有另一個 C9**。
  * C7 頂點屬性（名稱/型別/維度/長度/NaN/Inf）；C8 拓樸（索引範圍、parent、單一 root、uv0∈[0,1]）。
* 本輪實跑：CPython 3.14.7、約 5.9 s、16/16 檔全 PASS。
* **C9（50% 剪影可辨）**：ART-01 的 C9 指標**沒有被刪除**；本輪用**未修改的** ART-01 指標函式與門檻
  （`OUTLINE_MEAN_MIN=0.04`、`OUTLINE_MAX_MIN=0.12`、`SIL_IOU_MAX=0.85`、50% downsample）對新的
  ART-04 `art/models/<Tier>.<Faction>/LOD{0,2}.glb` 重跑一次離線量測：
  **LOD2（strict）12/12 tier pairs PASS、LOD0 12/12 PASS、兩陣營皆同，deciding views = front+side**。
  證據：[`art/c9-art04-silhouette-probe.txt`](c9-art04-silhouette-probe.txt)（一次性 probe，
  `python .tmp-art04\c9_probe.py`；**不是**完整 `art/validate_models.py` 的重跑，該檔的 C4 槽體檢查
  仍停留 ART-01）。這是**離線幾何證據**，不是實機；實機 50% 可讀性仍**待實機確認**。

## 10. LOD

* **只匯出 LOD0**（每模型 1 檔）。`art/models/**` 仍有 LOD0/1/2（來源），但 `.timbermesh` 只給 LOD0；
  遊戲端由 `Timberborn.PrefabOptimization`（`TimbermeshPrefabOptimizer`/`AutoAtlaser`）自動產生 LOD。
  `art/timbermesh/lod/` 刻意不存在。
* 來源 LOD 三角面（v2.2 生成器輸出）：LOD0 **5,850–5,918** / LOD1 **2,362–2,378** / LOD2 **790–814**；
  LOD0 預算 6,000（每檔 ≤6,000，未放鬆）。
* 實際 LOD 切換品質/距離**未實機確認**。

## 11. 預覽與圖示（離線渲染，非遊戲截圖）

* `art/previews/`（v2.2，**48 張**）：8 個模型（4 tier × 2 faction）× {front, side, top, scale50, iso}
  + ConstructionStage0 {front, iso} + 6 張 50% A/B 合圖（`tier_ab_*`、`tiers_scale50_grid`、
  `faction_ab_basic_50`、`faction_ab_top_50`、`tiers_factions_scale50_grid`）；Blender Cycles 離線渲染，
  重跑 `art\render_previews.py`。Folktails 保留舊檔名 `<Tier>_*.png`，IronTeeth 為 `<Tier>.IronTeeth_*.png`。
* `art/icons/`：4 個 tier × {512×512, 128×128} RGBA PNG + `icons-manifest.json`（相機參數、bytes、
  覆蓋率、SHA-256）；兩陣營共用同一組圖示。`data/Icons/BoxFarm<Basic|Intermediate|Advanced|Top>.png`
  已換成 ART-04 的 128×128 版本（`.png.meta.json` 不變）。
* 檔案名稱/`LabeledEntitySpec.Icon`（`Icons/BoxFarm<Tier>`）不變；圖示是離線渲染，
  **建造選單實際呈現待實機確認**。精確 bytes/SHA 見 `art/icons/icons-manifest.json`。

## 12. Lead 的建置 / 部署 / 實機步驟（本輪未執行）

1. 前置（本輪已完成，可重跑）：`python art\validate_timbermesh.py` 全 PASS、
   `dotnet run --project tools\validate -c Release -m:1 -- data` → `RESULT: OK`、
   L1 `dotnet run --project tests\HDBF.Tests -c Release -m:1` 全綠。
2. **放檔案**：`art/timbermesh/*.timbermesh`（16 檔）→ `data/Buildings/Food/BoxFarm/`；
   `art/icons/BoxFarm.<Tier>.128.png` → `data/Icons/BoxFarm<Tier>.png`（本輪已由 Lead 複製）。
3. **建置 + 部署**（完整 gate）：
   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Install
   ```
4. **實機檢查清單**（開遊戲後）：
   * 建造選單四檔出現；`Player.log` 無 `Material ... not found in repository`、無 prefab/blueprint 轉換錯誤。
   * 每檔放一座（兩陣營各一次，共 8 座）並截圖：`docs/in-game/round<NN>-ART04-<Tier>-<Faction>.png`。
   * [ ] **材質一致**：同一材質槽的所有面是同一個色/紋 swatch，沒有 atlas 拼布。
   * [ ] **屋頂是單一空平台**：沒有槽、土壤、通風管、雨棚、山牆、龍門架、輸送帶罩；只有平台 + 矮收邊。
   * [ ] **沒有作物/土壤視覺**（地面作物仍由 BUILD-05 隱藏）。
   * [ ] **3×2×1 佔地**：貼齊、不穿地/不懸空、正面（−Z）朝入口同側。
   * [ ] **3 埠存在**：動力/管道/物流接頭與空節點對得上。
   * [ ] **施工階段**：放置不拋 slot 例外；灰盒是空平台、完工換新成品模型。
   * [ ] **圖示**：建造選單四檔圖示與新模型一致。
   * [ ] **50% 鏡頭**：四檔彼此可辨（材質/牆體/細節/平台平面，不是屋頂剪影）。
   * [ ] **LOD 拉遠近**：無明顯跳動/破面（遊戲自動 LOD）。
5. 證據歸檔：截圖 + `Player.log` → `docs/in-game/`；由 Lead 記入 `docs/VERIFICATION.md` /
   `docs/REQ_TRACEABILITY.md`（本輪依規定不動那兩份）。

## 13. 未做 / 已知限制（誠實清單）

1. **沒有實機驗證**：只有離線幾何/格式驗證與離線渲染；遊戲中外觀尚未確認。
2. **沒有自訂 PBR 材質 / AssetBundle**：flat patch 是原版材質貼圖上的固定取樣區，不是新貼圖。
3. **頂點色是否被 shader 使用未知**：`.timbermesh` 帶 `color`（16/16 非白），但 shader 支援未驗證。
4. **C9 只在離線 probe 重跑**：舊 ART-01 驗證器 `art/validate_models.py` 仍是 ART-01 契約
   （含已移除槽體的 C4），本輪**沒有更新**它；C9 指標則用其未修改的函式/門檻對新模型離線重跑，
   結果 12/12 pair PASS（`art/c9-art04-silhouette-probe.txt`）。**實機 50% 可辨性未驗**。
5. **空節點 rotation identity**：3 埠沒有把朝外方向寫進 transform（無消費端）。
6. **只有 LOD0 進 `.timbermesh`**：依賴遊戲自動 LOD；切換品質未驗。
7. **施工階段 8 份**（每 tier × 陣營一份，幾何/材質相同，只有路徑與 `Model.Name` 不同）。
8. **`Model.Name` 由匯出器 post-process 蓋成檔名 stem**（官方插件輸出空字串）。
9. **`art/__pycache__/`** 是 Python import 副作用，非交付物。

## 14. 偏差記錄

| # | 偏差 | 為什麼 | 影響 |
|---|---|---|---|
| 1 | `Model.Name` 由匯出器蓋成 stem（官方插件留空） | asset 名稱/除錯可讀性；驗證 C2 靠它 | 與官方範例欄位不同（未實機驗證影響） |
| 2 | 座標從 contract 中心軸心**平移**到 block-corner（export = contract + (1.5,0,1.0)） | 遊戲模型空間就是角原點 | 若遊戲改回中心軸心需還原；集中在 `EXPORT_MATRIX` 與 `timbermesh_common` |
| 3 | 只交付 LOD0 | 遊戲自動 LOD/AutoAtlas | 手動 LOD group 未滿足（目前不需要） |
| 4 | 空節點 rotation identity | 本卡只要求座標；無消費端 | 接埠的卡要補 |
| 5 | 「自製材質」= flat patch + 原版材質名，不是自訂貼圖/AssetBundle | Unity 工具鏈目前不可得（見 docs/NEXT_ROUND.md） | 外觀一致但非自訂 PBR；實際觀感待實機 |
| 6 | 施工階段 8 份 | 卡要求 16 檔 = 8 + 8 | 檔案數與體積多一點（合計 73,178 B） |
| 7 | C9 50% 剪影指標只用一次性 probe 重跑，未更新 `art/validate_models.py` | 該驗證器仍是 ART-01 契約（含槽體 C4），本卡不重寫它 | 離線 C9 12/12 PASS 有證據；實機可辨性仍待驗 |
| 8 | 官方插件 vendored 而非引用 `.hdbf` 安裝 | 可重跑、可審、不需外部路徑 | 需同步上游時手動更新 |
