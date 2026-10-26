# workstream: art-03-blueprint-wiring — 自製 `.timbermesh` 接入 8 藍圖（data / validate / icons / docs / tests）

Task: ART-03 建造卡（Lead 派工；Team 四條寫入工作流 + Lead 整合驗證）。
狀態：**離線接線 + 資料契約/驗證器/L1 完成並以本機命令實測通過**。
**未做**：`build.ps1`、部署、啟動遊戲、實機外觀驗證（本卡明令不得執行）；自訂 PBR 材質 AssetBundle。
本報告**不聲稱**任何實機已驗證項目。

前置卡：ART-01（模型來源/座標契約）、ART-02（16 檔 `.timbermesh` 匯出與驗證）。
前置 gate 在本卡開始前已確認（見 §1），不是本卡的產出。

---

## 0. 摘要（可先看這段）

* **模型落地**：`art/timbermesh/` 的 **16 檔** `.timbermesh`（8 成品 LOD0 + 8 ConstructionStage0）
  已原樣複製到 `data/Buildings/Food/BoxFarm/`，**16/16 SHA-256 與來源相同**，
  合計 **1,172,971 bytes**。
* **8 份 blueprints**：`Children.#Finished.TimbermeshSpec.Model` 與
  `Children.#Unfinished.Children.ConstructionStage0.TimbermeshSpec.Model` 改指自製模型
  （asset 路徑不含副檔名，見 §2）；**只改了 16 個 Model 字串，並移除根層
  `TransformSlotInitializerSpec` + `WorkplaceSlotManagerSpec`**（共 14 個 root spec）。
  `CollidersSpec`（單一 3×1×2 box）、`BlockObjectSpec`、`Entrance`、`HdbfBoxFarmSpec` 與所有平衡數值
  **逐欄位比對未變**（30 個結構差異 = 16 Model + 14 spec 移除，0 個非預期差異）。
* **圖示**：4 張 128×128 生產渲染圖已換入 `data/Icons/BoxFarm<Basic|Intermediate|Advanced|Top>.png`
  （14,941 / 15,622 / 16,558 / 16,020 bytes），檔名與 `.png.meta.json`（`{"isSprite": true}`）不變。
* **驗證器**：`tools/validate` 現在把「模型路徑 → `data/**/*.timbermesh`」視為合法自製資產，並檢查
  檔案存在 + 非空 + **zlib header + 可完整解壓 + Adler-32 trailer**；原版路徑 fallback 仍保留。
  正常跑：`mod assemblies : 5/5 OK (expected 5)`、`WARNINGS: none`、**`RESULT: OK`**、exit 0。
  負向對照（不存在/截斷/壞檔/塞回 slot spec/文件漏路徑）**全部 exit 1**（見 §3.2）。
* **L1**：新增 6 條資料契約測試（不引用任何遊戲/Unity 組件），全套 **`PASSED 510  FAILED 0`**、
  exit 0（改動前實測 baseline 為 **504**；卡面寫的 492 與本機實測不同，見 §3.3）。
* **文件**：`data/ART_PLACEHOLDER.md` 全篇改寫（保留檔名，理由見 §2.4），README 美術條目更新。
* 未改 `src/`、`build.ps1`、`docs/VERIFICATION.md`、`docs/REQ_TRACEABILITY.md`、`docs/NEXT_ROUND.md`；
  未跑 `build.ps1`、未部署、未啟動遊戲；工作區無 `bin/obj`、無暫存檔殘留（§3.5）。

## 1. 前置：ART-02 gate（來源證據）

`art/timbermesh-validation.txt`（本卡未修改）結尾：

```text
FILES:  16/16 present; 16 PASS, 0 FAIL
CHECKS: 128 PASS, 0 FAIL, 0 NOT RUN (of 128 = 16 files x 8 checks)
OVERALL VERDICT: PASS
```

* 16 檔逐檔 C1–C8 全 PASS：zlib+protobuf 可解析、`Model.name == 檔名 stem`、
  bounds 3.000×1.000×2.000 @ min (0,0,0)（block-corner 原點，**非置中**）、tris ≤ 6000、
  材質名全在原版 faction whitelist、6×`#Slot#Crop_0..5` + 3×`#Port#*` 空節點座標偏差 0。
* 逐檔 bytes / tris / materials / sha256：`art/timbermesh/export-manifest.json`（`model_count: 16`、
  `failures: []`）。
* 需求與匯出細節：`docs/workstreams/art-01-models.md`、`docs/workstreams/art-02-timbermesh.md`。

## 2. 改動清單

### 2.1 模型檔（16 個新檔，`data/Buildings/Food/BoxFarm/`）

檔名與 `art/timbermesh/` 完全相同，內容 byte-identical（SHA-256 16/16 相同）。

| Tier | Faction | Finished LOD0 | ConstructionStage0 |
|---|---|---|---|
| Basic | Folktails | `BoxFarm.Basic.Folktails.Model.timbermesh`（130,970 B / 5,940 tris） | `BoxFarm.Basic.Folktails.ConstructionStage0.Model.timbermesh`（10,391 B / 408 tris） |
| Basic | IronTeeth | `BoxFarm.Basic.IronTeeth.Model.timbermesh`（129,352 B / 5,940 tris） | `BoxFarm.Basic.IronTeeth.ConstructionStage0.Model.timbermesh`（10,398 B / 408 tris） |
| Intermediate | Folktails | `BoxFarm.Intermediate.Folktails.Model.timbermesh`（136,055 B / 5,950 tris） | `BoxFarm.Intermediate.Folktails.ConstructionStage0.Model.timbermesh`（10,400 B / 408 tris） |
| Intermediate | IronTeeth | `BoxFarm.Intermediate.IronTeeth.Model.timbermesh`（136,358 B / 5,950 tris） | `BoxFarm.Intermediate.IronTeeth.ConstructionStage0.Model.timbermesh`（10,405 B / 408 tris） |
| Advanced | Folktails | `BoxFarm.Advanced.Folktails.Model.timbermesh`（137,383 B / 5,942 tris） | `BoxFarm.Advanced.Folktails.ConstructionStage0.Model.timbermesh`（10,396 B / 408 tris） |
| Advanced | IronTeeth | `BoxFarm.Advanced.IronTeeth.Model.timbermesh`（137,881 B / 5,942 tris） | `BoxFarm.Advanced.IronTeeth.ConstructionStage0.Model.timbermesh`（10,402 B / 408 tris） |
| Top | Folktails | `BoxFarm.Top.Folktails.Model.timbermesh`（140,821 B / 5,950 tris） | `BoxFarm.Top.Folktails.ConstructionStage0.Model.timbermesh`（10,389 B / 408 tris） |
| Top | IronTeeth | `BoxFarm.Top.IronTeeth.Model.timbermesh`（140,975 B / 5,950 tris） | `BoxFarm.Top.IronTeeth.ConstructionStage0.Model.timbermesh`（10,395 B / 408 tris） |

**路徑慣例**（已用官方範例確認）：`TimbermeshSpec.Model` 是**不含副檔名、相對於 mod 根目錄**的
asset 路徑；`Buildings/Food/BoxFarm/BoxFarm.Basic.Folktails.Model`
→ `<mod>/Buildings/Food/BoxFarm/BoxFarm.Basic.Folktails.Model.timbermesh`。
證據：官方 modding SDK 範例
`%USERPROFILE%\.hdbf\sdk\sdk-repo\timberborn-modding-main\Assets\Mods\ShantySpeaker\Data\Buildings\Decorations\ShantySpeaker\ShantySpeaker.IronTeeth.blueprint.json`
宣告 `"Model": "Buildings/Decorations/ShantySpeaker/ShantySpeaker.IronTeeth.Model"`，同目錄就是
`ShantySpeaker.IronTeeth.Model.timbermesh`；另見 `docs/API_FACTS.md`（`TimbermeshSpec` 列）。

### 2.2 8 份 blueprint（`data/Buildings/Food/BoxFarm/BoxFarm.<Tier>.<Faction>.blueprint.json`）

每份只改這兩個欄位（`<Tier>`/`<Faction>` 對應檔名）：

```text
Children.#Finished.TimbermeshSpec.Model
  Buildings/Food/BoxFarm/BoxFarm.<Tier>.<Faction>.Model
Children.#Unfinished.Children.ConstructionStage0.TimbermeshSpec.Model
  Buildings/Food/BoxFarm/BoxFarm.<Tier>.<Faction>.ConstructionStage0.Model
```

舊值（原版佔位模型）→ 新值，逐檔：

| 藍圖 | 舊 `#Finished` Model | 新 `#Finished` Model |
|---|---|---|
| Basic.Folktails | `Buildings/Food/GathererFlag/GathererFlag.Folktails.Model` | `Buildings/Food/BoxFarm/BoxFarm.Basic.Folktails.Model` |
| Basic.IronTeeth | `Buildings/Food/GathererFlag/GathererFlag.IronTeeth.Model` | `Buildings/Food/BoxFarm/BoxFarm.Basic.IronTeeth.Model` |
| Intermediate.Folktails | `Buildings/Food/EfficientFarmHouse/EfficientFarmHouse.Folktails.Model` | `Buildings/Food/BoxFarm/BoxFarm.Intermediate.Folktails.Model` |
| Intermediate.IronTeeth | `Buildings/Food/FarmHouse/FarmHouse.IronTeeth.Model` | `Buildings/Food/BoxFarm/BoxFarm.Intermediate.IronTeeth.Model` |
| Advanced.Folktails | `Buildings/Food/Gristmill/Gristmill.Folktails.Model` | `Buildings/Food/BoxFarm/BoxFarm.Advanced.Folktails.Model` |
| Advanced.IronTeeth | `Buildings/Food/HydroponicGarden/HydroponicGarden.IronTeeth.Model` | `Buildings/Food/BoxFarm/BoxFarm.Advanced.IronTeeth.Model` |
| Top.Folktails | `Buildings/Storage/LargeWarehouse/LargeWarehouse.Folktails.Model` | `Buildings/Food/BoxFarm/BoxFarm.Top.Folktails.Model` |
| Top.IronTeeth | `Buildings/Food/FoodFactory/FoodFactory.IronTeeth.Model` | `Buildings/Food/BoxFarm/BoxFarm.Top.IronTeeth.Model` |

（`ConstructionStage0` 同表，只是新值加 `.ConstructionStage0`。所有 Model 字串都不含 `.timbermesh`。）

**Slot spec（依 ART-01 §12 提醒處理）**：7 份藍圖的根層 `TransformSlotInitializerSpec`
（Basic 為 `Seat`、其餘原為 `Entrance`）與 `WorkplaceSlotManagerSpec` **已移除**；
Top.Folktails 原本就沒有這兩個 spec，只改 Model。理由與安全性：

* 自製模型只有 `#Slot#Crop_0..5` / `#Port#Power|Pipe|Logistics` 空節點，**沒有任何 Seat/Entrance
  transform**；`TransformSlotInitializer` 對每個關鍵字呼叫 `SlotRetriever.GetSlots`，0 個匹配即
  `InvalidOperationException: There are no "<keyword>" slots in <prefab>`（round-4 實機已撞過，
  `docs/VERIFICATION.md` §12）。
* 同樣沒有 worker 站位時，保留空的 `WorkplaceSlotManager` 會讓「沒有站位」被讀成「站位全滿」；
  原版 Top/Folktails 與 10 工人的 `Mine` 的做法都是兩個 spec 一起省略。
* 影響僅為「工人沒有坐下動畫站位」，不影響崗位/工作邏輯；視覺結果列入**需實機確認**。

**未動的契約**：`CollidersSpec` 仍為單一 box `Center {1.5, 0.5, 1.0}` / `Size {3.0, 1.0, 2.0}`；
`BlockObjectSpec`（3×1×2、6 blocks、entrance（1,-1,0））、`HdbfBoxFarmSpec`、成本、
`BuildingModelSpec`、template/loc/icon 參照全部不變。8 份 JSON 均可解析，格式維持 UTF-8 no BOM / LF。

### 2.3 圖示（`data/Icons/`）

| 檔案 | 尺寸 | bytes | 來源 |
|---|---:|---:|---|
| `BoxFarmBasic.png` | 128×128 RGBA | 14,941 | `art/icons/BoxFarm.Basic.128.png` |
| `BoxFarmIntermediate.png` | 128×128 RGBA | 15,622 | `art/icons/BoxFarm.Intermediate.128.png` |
| `BoxFarmAdvanced.png` | 128×128 RGBA | 16,558 | `art/icons/BoxFarm.Advanced.128.png` |
| `BoxFarmTop.png` | 128×128 RGBA | 16,020 | `art/icons/BoxFarm.Top.128.png` |

舊 1,062–1,066 B 佔位圖被覆蓋；`*.png.meta.json` 保持 `{"isSprite": true}` 未動；
`LabeledEntitySpec.Icon`（`Icons/BoxFarm<Tier>`）不變。

### 2.4 驗證器（`tools/validate/DataChecks.cs`）

* `TierExpectation` 不再硬寫 16 個原版字串，改由 `FinishedModelFor(faction)` /
  `ConstructionModelFor(faction)` 從 tier+faction 推導（單一真實來源）；平衡期望值未變。
* `CheckModelAndColliders`：`#Finished` 用 finished 期望、`#Unfinished.ConstructionStage0` 用
  construction 期望，ordinal 完全相等比對；新增 `CheckModelAsset`：
  * 拒絕絕對路徑與 `..` 段；解析 `data/ + model + ".timbermesh"`；
  * 檔案存在、長度 > 4；zlib header（CM=8、`(CMF<<8|FLG) % 31 == 0`）；
  * `ZLibStream` 完整解壓且 payload 非空；**新增 Adler-32 trailer 比對**（檔案最後 4 bytes
    大端 = 解壓 payload 的 Adler-32）→ 中途截斷也算壞檔；
  * 原版 fallback 保留：`data/` 沒有的路徑視為原版佔位模型，仍要求
    `<ref>/Blueprints/<model 去掉 .Model>.blueprint.json` 存在，並報錯要求改指自製模型。
* `CheckTransformSlots`：自製模型路徑要求 `TransformSlotInitializerSpec` 與
  `WorkplaceSlotManagerSpec` 皆不存在；若硬留 spec，其 `SlotKeyword` 必須能在解壓後的模型位元組中
  找到（protobuf 節點名為 UTF-8），否則報錯。原版路徑仍走舊的 `.ref` 關鍵字交叉檢查。
* `CheckArtPlaceholderNotes`：仍讀 `data/ART_PLACEHOLDER.md`，要求文件列出全部 8 finished +
  8 construction 的自製 model 路徑（原版佔位字串要求已移除）。
* **必要的整合修正（超出卡面字面 `tools/validate` 範圍，已明示）**：
  `tools/release/release-gates.json` 的 `allowedStagedExtensions` 加入 `".timbermesh"`。
  原因：`build.ps1` G-08 會拒絕清單外副檔名，而 `build.ps1` 把 `data\*` 原樣 staging，
  不改這一行則**每次 `build.ps1 -Install` 都會在 release gate 失敗**。未動 `build.ps1`；
  `ReleaseGateContractTests` 對該清單沒有斷言（已確認），L1 不受影響。

### 2.5 測試（`tests/HDBF.Tests`，新檔）

* `tests/HDBF.Tests/ArtModelContractTests.cs`（新，21,360 B，6 個 `Test*`）：
  1. 8 藍圖的 finished + ConstructionStage0 Model 必須等於預期自製路徑；
  2. 每個引用路徑 → `data/<path>.timbermesh` 存在、非空、zlib header、可解壓，payload 含模型 stem；
  3. 8 個原版佔位 model 字串不得殘留；
  4. 自製模型藍圖不得有 `TransformSlotInitializerSpec` / `WorkplaceSlotManagerSpec`；
  5. 4 張 tier 圖示為 128×128、> 5 KB 的生產渲染圖、meta `isSprite == true`；
  6. `data/ART_PLACEHOLDER.md` 必須列出 16 條自製 model 路徑。
* `tests/HDBF.Tests/HDBF.Tests.csproj`：只加一個 `AssemblyMetadata("HdbfRepoRoot", <repo root>)`
  （build 輸出在 `%TEMP%`，測試需在 runtime 找到 `data/`）。未新增任何遊戲/Unity reference
  （仍只有 `HDBF.Core` ProjectReference；grep 確認 0 個 `Timberborn.*`/`UnityEngine` 引用）。

### 2.6 文件

* `data/ART_PLACEHOLDER.md`：**保留檔名**、全篇改寫（276 行）。保留檔名的理由與內容一併寫在文件開頭：
  `tools/validate`、`docs/REQ_TRACEABILITY.md`、`docs/ARCHITECTURE.md`、`docs/OPS.md` 與舊
  workstream 報告都引用這個檔名，而本卡不得改 Lead-owned 的 `VERIFICATION.md`/`REQ_TRACEABILITY.md`，
  改名會造成 dangling reference；卡面本身允許「直接改寫內容」。內容含：16 檔清單與 bytes/tris、
  路徑慣例、材質策略（原版 MaterialCollection 名 + 原版 atlas 採樣 ART-01 UV0 → 近看拼布、
  自訂 PBR 待 Unity）、LOD（`Timberborn.PrefabOptimization` 的 `TimbermeshPrefabOptimizer`/
  `AutoAtlaser`，標**待實機確認**）、slot spec 移除與後果（**需實機確認**）、未動契約、圖示、
  驗證器契約、仍未做清單。
* `README.md`：美術偏離條目（§7 第 3 點）與「未驗證」表的「建筑实际外观」一列更新為 ART-03 現況。

## 3. 驗證證據（本機實跑，Lead 親自重跑）

所有命令在工作區根目錄執行，build 輸出全部導到 `%TEMP%`：

```powershell
$w = Join-Path $env:TEMP 'hdbf-card-work'
dotnet build tools\validate\validate.csproj -c Release -m:1 -p:HdbfWorkRoot=$w
dotnet "$w\bin\validate\Release\net9.0\HdbfValidate.dll" data `
  --managed "C:\Program Files (x86)\Steam\steamapps\common\Timberborn\Timberborn_Data\Managed" `
  --ref "$env:USERPROFILE\.hdbf\ref" --assemblies "$env:USERPROFILE\.hdbf\work\bin"
dotnet build tests\HDBF.Tests\HDBF.Tests.csproj -c Release -m:1 -p:HdbfWorkRoot=$w
dotnet "$w\bin\HDBF.Tests\Release\net9.0\HDBF.Tests.dll"
```

### 3.1 `tools/validate` 正常輸出（Lead 最終重跑，exit 0）

```text
HDBF data validator
  data dir        : C:\Users\user\Documents\Timberborn\Mods\HighDensityBoxFarm\data
  mod root        : C:\Users\user\Documents\Timberborn\Mods\HighDensityBoxFarm
  reference dir   : C:\Users\user\.hdbf\ref
  managed dir     : C:\Program Files (x86)\Steam\steamapps\common\Timberborn\Timberborn_Data\Managed
  game assemblies : 660
  spec types      : 506 (game + mod ComponentSpec subclasses)
  mod assemblies  : 5 (HDBF.Core.dll, HDBF.Gameplay.dll, HDBF.Integration.dll, HDBF.Persistence.dll, HDBF.UI.dll)
  mod assemblies  : 5/5 OK (expected 5)
  blueprints      : 10

WARNINGS: none

RESULT: OK - data/ is valid.
```

建置：`tools/validate` 與 `tests/HDBF.Tests` 均 **0 warnings / 0 errors**。
16 個模型全部被實際開啟、解壓並通過 Adler-32 比對。

### 3.2 負向對照（壞資料必須 FAIL）

Lead 獨立重跑（在 `%TEMP%` 的 **data 副本**上操作，真實 `data/` 全程未寫入；副本 restore 後重跑回到
`RESULT: OK`）：

**(a) Model 指向不存在檔案** → exit 1：

```text
ERRORS (1):
  [blueprint] Buildings/Food/BoxFarm/BoxFarm.Basic.Folktails.blueprint.json:
  Children.#Finished.TimbermeshSpec.Model 'Buildings/Food/BoxFarm/BoxFarm.Basic.Folktails.Model'
  -> custom model asset not found:
  ...\hdbf-lead-neg\data\Buildings\Food\BoxFarm\BoxFarm.Basic.Folktails.Model.timbermesh
  (data/ must ship this .timbermesh)
RESULT: FAILED - fix the problems above.
```

**(b) `.timbermesh` 中途截斷（前 4096 bytes）** → exit 1（Adler-32 trailer 檢查）：

```text
ERRORS (1):
  [blueprint] Buildings/Food/BoxFarm/BoxFarm.Basic.Folktails.blueprint.json:
  Children.#Finished.TimbermeshSpec.Model 'Buildings/Food/BoxFarm/BoxFarm.Basic.Folktails.Model'
  has a corrupt/truncated zlib stream: Adler-32 trailer mismatch in
  ...\hdbf-lead-neg\data\Buildings\Food\BoxFarm\BoxFarm.Basic.Folktails.Model.timbermesh
  (declared 0xC95AD8BA, computed 0xF3625BF8)
RESULT: FAILED - fix the problems above.
```

validator 工作流另外在 `%TEMP%\hdbf-card-negative.log` 記錄了更完整的注入矩陣，全部 exit 1：

| 注入 | 錯誤類型 |
|---|---|
| Model → 不存在的 `...Basic.Folktails.Missing` | `custom model asset not found` + `must point at the shipped custom model` |
| `.timbermesh` 截到 3 bytes | `is not a usable .timbermesh ... only 3 byte(s)` |
| 20 bytes（合法 header、無 payload） | `decompresses to an empty payload` |
| 100 bytes 全 0 | `invalid zlib header (0x00 0x00)` |
| header + 200 bytes 垃圾 deflate | `could not be decompressed (corrupt .timbermesh?)` |
| 中途截斷 4096 bytes（修 Adler 後） | `Adler-32 trailer mismatch` |
| 塞回根層 `TransformSlotInitializerSpec`（`Entrance`）+ `WorkplaceSlotManagerSpec` | 兩條 `must be absent for the custom model` + `SlotKeyword 'Entrance' does not occur in the custom model` |
| Model 改回原版 `GathererFlag` | `must point at the shipped custom model`（舊 `.ref` slot 檢查仍可達） |
| 原版路徑 + `--ref` 指空目錄 | `reference blueprint is missing` + 降級 warning，不 crash |
| 從 `ART_PLACEHOLDER.md` 副本刪掉一條 ConstructionStage0 路徑 | `ART_PLACEHOLDER.md does not document ...` |

已知邊界（誠實）：驗證器只做 **zlib 容器層**檢查（存在/非空/header/完整解壓/Adler-32），
**不解析 protobuf**；幾何/材質/slot 節點契約由 ART-02 的
`python art\validate_timbermesh.py`（16/16 PASS、128/128）負責，兩者互補。

### 3.3 L1（`tests/HDBF.Tests`）

```text
HDBF L1 test run - 2026-10-25T...
...
PASSED 510  FAILED 0
```

* 改動前 baseline（tests 工作流在加入新檔前實跑）：**`PASSED 504  FAILED 0`**。
* 本卡新增 6 條 → **510 / 0**、exit 0。
* **與卡面數字的差異（誠實記錄）**：卡面寫「既有 492 條全綠」，本機實測 baseline 是 **504**；
  本卡沒有刪改任何既有測試，510 = 504 + 6。差異來源應是 492 之後其他已完成卡的測試增量，
  本卡未追查其出處，也沒有為了湊數字改測試。

### 3.4 資料完整性

* 16 個 `data/Buildings/Food/BoxFarm/*.timbermesh` 與 `art/timbermesh/` 同檔名來源
  **SHA-256 16/16 相同**，總計 1,172,971 B。
* 8 藍圖的結構 diff（Lead 用改動前快照逐節點比對）：**30 個差異**，全部是
  §2.2 所述 16 個 Model + 14 個 spec 移除；其餘欄位 0 差異。
* 4 張圖示與 `art/icons/*.128.png` byte-identical；`*.png.meta.json` hash 未變。

### 3.5 工作區審計

本卡寫入的檔案（完整清單）：

```text
data/Buildings/Food/BoxFarm/*.timbermesh                 16 個新檔
data/Buildings/Food/BoxFarm/BoxFarm.<Tier>.<Faction>.blueprint.json   8 份修改
data/Icons/BoxFarm<Basic|Intermediate|Advanced|Top>.png               4 份覆蓋
data/ART_PLACEHOLDER.md                                  全篇改寫（保留檔名）
README.md                                                美術條目/未驗證列
tools/validate/DataChecks.cs                             驗證器擴充
tools/release/release-gates.json                         allowedStagedExtensions += ".timbermesh"（必要整合修正，§2.4）
tests/HDBF.Tests/ArtModelContractTests.cs                新檔
tests/HDBF.Tests/HDBF.Tests.csproj                       +AssemblyMetadata（無新 reference）
docs/workstreams/art-03-blueprint-wiring.md              本報告
```

* `src/`：**0 個檔案被修改**；`build.ps1`、`docs/VERIFICATION.md`、`docs/REQ_TRACEABILITY.md`、
  `docs/NEXT_ROUND.md` 未動。
* 工作區內 `bin/obj` 目錄 0 個、暫存/備份檔 0 個（build 全部在 `%TEMP%`）。
* 旁註（非本卡寫入）：`docs/workstreams/art-02-timbermesh.md` 在 16:44:07 由 ART-02 工作流做了
  最終修訂（內容是 ART-02 自己的圖示確定性證據，無 ART-03 內容）；本卡四條工作流皆回報未寫入該檔。

## 4. Lead 的建置 / 部署步驟（本卡**未執行**，照卡面移交）

前置條件：本卡驗證已綠（§3.1、§3.3）。`build.ps1` 會依序跑 5 個組件 + L1 + `tools/validate` +
staging + REL-01 gate + 部署到 `C:\Users\user\Documents\Timberborn\Mods\HDBF`。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Install
```

預期與檢查點：

1. 5 個模組組件 **0 warning / 0 error**。
2. L1：`PASSED 510  FAILED 0`（新增的 6 條會一起跑）。
3. `tools/validate`：`mod assemblies : 5/5 OK`、`WARNINGS: none`、`RESULT: OK`。
4. **REL-01 G-08 應該通過**（`.timbermesh` 已在 `allowedStagedExtensions`）；
   staging 應包含 `Buildings/Food/BoxFarm/` 下 **16 個 `.timbermesh`**、8 份 blueprint、
   4 張新圖示；staged 檔案類型檢查不得再出現 `not an allowed file type`。
5. 部署後抽查（工作區路徑 → 部署路徑 SHA-256 相同）：
   `data/Buildings/Food/BoxFarm/*.timbermesh`（16）、8 份 blueprint、4 張 `data/Icons/BoxFarm*.png`。
6. 若 staging 缺模型或用舊路徑，先看 `build.ps1` 的 staging/gate 輸出；**不要**用
   `dotnet build` 取代完整 gate。

## 5. 實機驗證清單（**未執行**；需使用者/實機，每一步都要留證據）

> 本卡與 ART-02 都沒有跑過遊戲，以下全部是「待做」，不是「已通過」。

1. **裝載**：`build.ps1 -Install` 後開遊戲，確認 mod 載入、建造選單出現四檔（科學解鎖
   200/600/1000/3000），`Player.log` 無 `Material ... not found` / prefab / blueprint 轉換錯誤。
2. **每檔外觀 × 兩陣營**（Basic/Intermediate/Advanced/Top × Folktails/IronTeeth，共 8 座）：
   模型貼齊 3×2 佔地、不穿地/不懸空、正面（−Z）朝向與入口同側、四檔剪影可分辨。
   材質預期是「原版 atlas 拼布」（正常現象，不是 bug），近看外觀列入自訂 PBR 的後續卡。
3. **施工階段**：放工地後應顯示自製 `ConstructionStage0` 灰盒 + 原版 `ConstructionBase3x2`
   土基；完工後切到 finished 模型。特別確認**放置瞬間不拋**
   `InvalidOperationException: There are no ... slots`（slot spec 已移除，理論上不會）。
4. **碰撞**：用工人/道路/堆疊物件確認碰撞確實是單一 3×1×2 box（可走在上方、側面不凸出、
   沒有模型自身的額外碰撞）。
5. **入口**：入口在長邊中點（local (1,-1,0) → 世界座標依旋轉），工人能從正面進入、
   路徑可達 `BuildingAccessibleSpec.LocalAccess (1.5, 0, 0.5)`。
6. **50% 鏡頭**：拉遠到 50% 視角，四檔剪影彼此可辨、與相鄰建築不混淆（需求 2.3/5.4）。
7. **LOD**：遠近拉動觀察是否有自動 LOD（預期由 `Timberborn.PrefabOptimization` 生成）；
   記錄切換距離/是否跳動——這是**待實機確認**項，不是已知事實。
8. **圖示**：建造選單中四個 tier 圖示為新 128×128 渲染（與舊單色方塊不同）、無糊邊。
9. **工人站位**：工人仍能上工、完成播種/收穫；沒有坐下動畫站位（預期行為，**需實機確認**
   視覺是否可接受）。
10. 證據歸檔：截圖與 `Player.log` 放 `docs/in-game/`，並在 Lead 的
    `docs/VERIFICATION.md` / `REQ_TRACEABILITY.md` 記錄（本卡依規定不動那兩份）。

## 6. 仍未做 / 待實機確認（誠實）

* **實機未驗**：外觀、施工階段外觀與切換、碰撞手感、入口可達、50% 鏡頭、LOD 切換品質、
  圖示在選單中的呈現、工人座位動畫缺失的視覺接受度。→ 全部標 `待實機確認`/`需實機確認`。
* **自訂 PBR 材質 / atlas / shader / AssetBundle 未做**（需 Unity）：目前用原版
  MaterialCollection 材質名 + 原版 atlas 採樣 ART-01 的逐面 UV0 → 遠看材質類別正確、近看拼布。
  UV0 已保留，補材質不必重匯出幾何。
* **LOD1/LOD2 未交付**：只交 LOD0，依賴遊戲端自動 LOD（class 名稱已在本機組件證實，
  但行為未實機確認）。
* **空節點朝向**：9 個空節點的 rotation 仍是 identity，需求 13.6 的「local +Z 朝外」只記錄在
  ART-01/02 文件，尚無消費端。
* **驗證器不做 protobuf/幾何解析**：容器層（zlib/Adler-32）+ 資料契約由 `tools/validate`，
  幾何/材質/slot 節點契約由 ART-02 的 `art/validate_timbermesh.py`；兩者都綠才算離線完成。
* **未跑** `build.ps1`、未部署、未啟動遊戲（卡面明令），也**不聲稱**任何實機結果。

## 7. 團隊協作與重跑

| Task | Owner | 寫入範圍 | 結果 |
|---|---|---|---|
| task-1 | data-wiring | `data/Buildings/Food/BoxFarm/`、`data/Icons/*.png` | 16 模型 + 8 藍圖 + 4 圖示；SHA 16/16 相同、結構 diff 30/30 預期、meta 未動 |
| task-2 | validator | `tools/validate/`、`tools/release/release-gates.json` | 自製模型資產檢查（zlib+Adler-32）、slot 契約、文件完整性；正常 OK、負向全 FAIL |
| task-3 | tests | `tests/HDBF.Tests/` | 新檔 6 條 L1；504 → 510、FAILED 0，無遊戲組件引用 |
| task-4 | docs | `data/ART_PLACEHOLDER.md`、`README.md` | 全篇改寫 + README 兩處；標註所有待實機項 |
| Lead | lead | 報告、整合驗證、獨立重跑 | §3 全部證據由 Lead 重跑；工作區審計、diff 審查 |

寫入範圍互斥（data 模型/藍圖 vs tools vs tests vs docs 檔案不重疊），每條任務完成後由 Lead 對
改動前快照做結構 diff 與全套重跑。重跑命令見 §3 開頭；驗證器負向矩陣在
`%TEMP%\hdbf-card-negative.log`（validator 產生，含命令與輸出），Lead 的最終正常/負向輸出在
`%TEMP%\hdbf-card-validate-final-normal.txt` / `hdbf-card-validate-final-negative.txt`。

## 補記（block-axis 修正卡，2026-10-25）

本報告 §2.2/§3 寫的 `BlockObjectSpec`（3×1×2、6 blocks）是 JSON 欄位字面值；其中 Y/Z 的軸向語意
是錯的（遊戲網格 Y=深、Z=高）。8 份 blueprint 的 Size 已由 `{X:3,Y:1,Z:2}` 修正為
`{X:3,Y:2,Z:1}`（同一 3 寬 × 2 深 × 1 高 佔地），Top 動力 transput 也改到後排 `(x,1,0)`/`Up`。
其餘內容（Model 路徑、icons、gates）不受影響。詳見 `docs/workstreams/bug-08-block-axis-size.md`。

