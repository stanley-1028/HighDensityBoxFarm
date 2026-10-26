# workstream: bug-08-block-axis-size — BlockObjectSpec.Size 軸向修正（3×2×1 占格）＋實機 A/B

Task：使用者問「能不能像倉庫一樣做多層」→ 決定先修占格 bug、再做多層；本卡範圍＝
**修正 `BlockObjectSpec.Size` 的 Y/Z 軸向 + Top 動力 transput + 驗證器 + 文件 + 實機確認 3×2**。
狀態：**離線修正完成、`build.ps1` 全綠、已部署、實機 A/B 取得占格證據**。
**未做**：2 層（多層）模型與資料（另行開卡）；本卡不動平衡數值；不聲稱「2 層已實作」。

---

## 0. 摘要（可先看這段）

* 8 份 BoxFarm blueprint 的 `BlockObjectSpec.Size` 原本寫 `{X:3, Y:1, Z:2}`。遊戲網格座標是
  **X=寬、Y=深、Z=高**，所以那其實是「3 寬 × 1 深 × **2 高**」，跟模型 / `CollidersSpec` /
  需求 13.3 的 **3 寬 × 2 深 × 1 高** 不符。已全部改為 **`{X:3, Y:2, Z:1}`**。
* 連帶修正 Top 動力 transput：舊 `(x,0,1)` + `Top`（+Z 垂直面）→ 後排 `(x,1,0)` +
  `Left, Up` / `Up` / `Right, Up`（`Up` = +Y = 背面）。
* `tools/validate/DataChecks.cs` 同步改成正確軸向語意（Size 期望值、depth-row、transput 方向、
  `Size.Z` 必須=1）；**負向樣本**（改回舊 Size / 改回舊 transput）都被明確拒絕。
* `build.ps1`：**RELEASE GATES PASSED（55 checks）**、L1 `PASSED`、`tools/validate RESULT: OK`；
  `build.ps1 -Install` 後 `Mods\HDBF` 內已是 `{X:3, Y:2, Z:1}`。
* 實機：fixture 於 (129,140,3) 放 Basic farm 載入乾淨、farm active、自製模型正常顯示
  （`docs/in-game/art04-size-loaded.png`）。
* 實機占格 A/B（決定性）：在修正後「後排」格子 (130,141,3) 注入 Path → 遊戲 log
  `Can't validate loaded BlockObject BoxFarm.Basic.Folktails(Clone) at (129, 140, 3). It's not
  backward compatible. Deleting it.` ＋載入問題對話框（農場被刪）；同一深度排、佔地外一格
  (128,141,3) 的對照組 → 農場正常、無刪除、無對話框。證明修正後占格含 y=141 後排 = 3 寬 × 2 深。
* ⚠️ **舊存檔風險**：若 v0.1.0 存檔中，農場「新後排格」已被路徑/其他物件佔用，載入時農場會被
  判定位置無效而刪除（探針就是這個情境）。見 §6。
* 使用者已定案：未來做 2 層時地塊容量 **60 → 120**（本卡未實作，見 §7）。

## 1. 根因與證據

### 1.1 遊戲網格座標：X=寬、Y=深、Z=高

* IL：`Timberborn.TerrainSystem.TerrainService.GetTerrainHeight` 回傳 `coordinates.z` + 相對高度；
  `TerrainMap.TryGetRelativeHeight` 對 `coordinates.z` 比較；`VectorExtensions.XY(Vector3Int)`
  回傳水平 (x, y) 對。→ **垂直軸是 Z，水平面是 X/Y**。
* 因此 `BlockObjectSpec.Size{X,Y,Z}` = 寬 / 深 / 高；`Blocks` 的索引
  `n → (x=n%X, y=(n/X)%Y, z=n/(X*Y))` 中 y 是深度排、z 是高度層。

### 1.2 原版資料對照（Size 與碰撞盒量測）

| 原版 blueprint | Size {X,Y,Z} | CollidersSpec 實測（Unity: y=高, z=深） | 說明 |
|---|---|---|---|
| `MediumWarehouse.Folktails` | **(3,2,1)** | 高 1.0 / 深 2.0 | 3×2 佔地、1 高倉庫（就是「像倉庫」的對照） |
| `Roof3x2.Folktails` | (3,2,1) | 高 0.2 / 深 2.0 | 3×2 屋頂 |
| `PowerWheel.Folktails` | (3,1,2) | 高 1.83 / 深 1.0 | 3 寬 × 1 深 × 2 高 |
| `HydroponicGarden.IronTeeth` | (3,3,1) | 高 0.99 / 深 2.88 | 3×3 佔地、1 高 |
| `BuildersHut.Folktails` | (3,2,2) | 高 1.60 / 深 2.0 | 多層（2 高） |
| `LargeWarehouse.Folktails` | (3,3,2) | 高 2.0 / 深 3.0 | 多層倉庫 |

（量測腳本：讀 `%USERPROFILE%\.hdbf\ref\Blueprints\**` 的 `BlockObjectSpec.Size` 與
`Children.#Finished.CollidersSpec.BoxColliders`，取 y/z 聯集範圍。）

### 1.3 本專案原本的不一致

* 需求 13.3 / 模型 / `.timbermesh` 匯出（ART-02）：3 寬 × 2 深 × 1 高（Unity 局部座標為
  x 0..3、y 0..1、z 0..2；block-corner origin 見 `docs/workstreams/art-02-timbermesh.md`）。
* 本工作區 blueprint 舊值 `Size {X:3, Y:1, Z:2}` → 遊戲視為 3 寬 × 1 深 × 2 高（6 blocks 的
  `Blocks[3..5]` 因此落在 **高度層 z=1**，而不是「後排」）。
* 舊 smoke 測試只驗「放得下」，未驗占格形狀，所以此不一致在 v0.1.0 一直沒被發現。

## 2. 修正內容

### 2.1 Blueprint（8 份）

* `BlockObjectSpec.Size`: `{X:3, Y:1, Z:2}` → **`{X:3, Y:2, Z:1}`**（8 檔）。
* `Blocks` **內容不動**（原本就是對的）：`Blocks[0..2]` = y=0 入口深度排
  `GroundOrStackable` + `Bottom, Top, Corners, Path, Middle`；`Blocks[3..5]` = y=1 後排
  `Any` + `Floor, Bottom, Corners, Path, Middle`。
* Top ×2：`TransputProviderSpec.Transputs` 由
  `(0,0,1)/"Left, Top"`、`(1,0,1)/"Top"`、`(2,0,1)/"Right, Top"`
  改為 `(0,1,0)/"Left, Up"`、`(1,1,0)/"Up"`、`(2,1,0)/"Right, Up"`。
* **不動**：`CollidersSpec`（Unity 空間 3×1×2，本來就對）、`Entrance {1,-1,0}`、
  `BuildingAccessibleSpec.LocalAccess`、`HdbfBoxFarmSpec` 與所有平衡數值、Model 路徑。

### 2.2 `tools/validate/DataChecks.cs`

* `CheckBlockObject`：Size 期望值改 X=3 / Y=2 / Z=1（預設值同步）；新增「`Size.Z` 是高度、
  必須=1」檢查；block 檢查由「z 列」改為「y 深度排」（`depthRow = (n/X)%Y`、`heightLayer = n/(X*Y)`），
  錯誤訊息帶 `y=… depth row (z=…)`。
* `CheckMechanicalPower`：Size 預設值同步；transput 方向語意改為
  `Up`=+Y（背面）、`Left/Right`=∓X、`Down`=−Y（正面）、`Top/Bottom`=±Z（垂直面）；
  allow-list 改 `{Up, Left, Right}`，reject 訊息改為「`Down` 指向正面」「`Top`/`Bottom` 是垂直面」，
  `hasRearFace` 改認 `Up`。
* 檔案頭/doxygen 註解同步註明軸向。

## 3. 離線驗證（可重跑）

```powershell
# 1) 完整建置＋測試＋release gates（55 checks）
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
# 2) 部署
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Install
# 3) 只跑資料驗證（免建置時可用已建好的 validator DLL）
dotnet %USERPROFILE%\.hdbf\work\bin\validate\Release\net9.0\HdbfValidate.dll data `
  --assemblies %USERPROFILE%\.hdbf\work\bin `
  --managed "C:\Program Files (x86)\Steam\steamapps\common\Timberborn\Timberborn_Data\Managed"
```

實跑結果：

* `build.ps1` → `RELEASE GATES: PASSED (55 checks)`、`RESULT: OK`、L1 `PASSED`（exit 0）。
* 正常資料 → `RESULT: OK`（0 errors、`WARNINGS: none`）。
* **負向 A**（把 Basic 的 Size 改回 `{3,1,2}`）→ 9 errors，包含
  `BlockObjectSpec.Size.Y must be 2 (found 1)`、`Size.Z is the building HEIGHT … must be 1`、
  `Blocks[3].MatterBelow must be 'GroundOrStackable' for the y=0 depth row (z=1)`。
* **負向 B**（把 Top transput 改回 `(x,0,1)`/`Top`）→ 8 errors，包含
  `Coordinates (0,0,1) is outside the 3x2x1 footprint`、
  `Directions 'Top' is a vertical face … (allowed: Up (rear), Left, Right)`。

## 4. 實機驗證（2026-10-25 21:24–21:29）

工具鏈：`tools/dev/fixtures/craft-farm-fixture.py`（把 farm entity 注入存檔副本）、
`tools/dev/Invoke-GameRound.ps1`（Prepare → Launch → ConfirmMods → Stop；多人存檔只讀，跑在
`Saves\HDBFTest` 的複本）。

### 4.1 正常載入（tag `art04-size`）

* fixture：Basic.Folktails 於 (129,140,3)，entrance 對到既有道路 (130,139)。
* log：`Opening file: …HDBFTest…`、`gameplay.entity: box farm active … tier=Basic`、
  `Load time: 81881ms`，無例外、無「載入問題」對話框。
* 截圖：`docs/in-game/art04-size-loaded.png`（自製 Basic 模型正常顯示在道路旁）。

### 4.2 占格 A/B（tag `art04-probe-in` / `art04-probe-out`）

| 回合 | 注入的 Path 座標 | 遊戲 log | 結果 |
|---|---|---|---|
| `art04-probe-in` | **(130,141,3)**＝修正後農場的後排 | `Can't validate loaded BlockObject BoxFarm.Basic.Folktails(Clone) at (129, 140, 3). It's not backward compatible. Deleting it.` | **農場被刪**；跳出「載入問題：對象位置無效，已被刪除：高密度盒子農場（基礎版）」 |
| `art04-probe-out` | **(128,141,3)**＝同一深度排、佔地外一格 | 無刪除訊息；`gameplay.entity: box farm active`；`Load time: 69148ms` | 正常載入、無對話框 |

* 證據檔：`docs/in-game/art04-probe-in-Player.log`、`art04-probe-in-loaded.png`、
  `art04-probe-out-Player.log`、`art04-probe-out-loaded.png`。
* 解讀：兩個回合唯一的差別是 Path 的 X 座標；後排 (130,141) 會讓農場的 blocks 驗證失敗 →
  證明修正後的占格 **包含 y=141 後排**，也就是 3 寬 × 2 深 × 1 高。舊的 `{3,1,2}` 資料
  只佔 y=140、高度 2 層，不可能在此格子衝突。
* 誠實限制：這個 A/B 證明的是「占格含後排」，不是「畫面看起來是 3×2」；視覺上的 3×2 仍以
  §4.1 截圖 + 已在檔案的 bounds/碰撞盒為準。遊戲對重疊物件採「載入時刪除」而非彈錯 dialog 給
  玩家選擇，這是引擎行為。

## 5. 檔案變更（本卡寫入範圍）

| 檔案 | 變更 |
|---|---|
| `data/Buildings/Food/BoxFarm/BoxFarm.<Tier>.<Faction>.blueprint.json` ×8 | Size → `{X:3,Y:2,Z:1}`；Top ×2 transput → 後排 `(x,1,0)`/`Up` |
| `tools/validate/DataChecks.cs` | 軸向語意修正（§2.2） |
| `docs/API_FACTS.md` | 修正 `Vanilla 1-high layout` / `HDBF consequence` 兩列 |
| `data/ART_PLACEHOLDER.md` | 修正 `BlockObjectSpec.Size` 段＋加 block-axis follow-up |
| `docs/VERIFICATION.md` | 新增 §25（修正摘要＋實機 A/B） |
| `docs/workstreams/art-03-blueprint-wiring.md` | 尾端補記（舊 Size 字面值說明） |
| `docs/workstreams/tech-data.md` | 尾端補記（舊 transput 軸向假設說明） |
| 本檔 | 報告 |

未動：`src/`、`tests/`、`build.ps1`、`docs/VERIFICATION.md` 既有章節（只 append）、
`HdbfBoxFarmSpec`/成本/科學等平衡資料、`CollidersSpec`、模型/圖示/材質。
（工作區還有另一個平行 session 正在做 flat-roof own-material 卡，會改 `art/`、`data/*.timbermesh`
與 `art/export_timbermesh.py`；本卡的 16 個 `.timbermesh` 與 `art/timbermesh-validation.txt`
在 21:27 後已由該卡重出，屬該卡範圍。）

## 6. 舊存檔相容性（重要、誠實）

* Size 由 3×1×2（block 佔用 x129..131 / y=140 / **z=3..4**）改成 3×2×1
  （x129..131 / **y=140..141** / z=3）。
* 對「沒被佔用的空地」存檔：只是把同一棟建築改登記成正確占格，載入正常（§4.1）。
* 對「新後排格已放路徑/其他物件」的舊存檔：遊戲會在載入時驗證失敗並**刪除該農場**
  （§4.2 的 probe-in 就是此情境的實測）。v0.1.0 的玩家若在農場後方鋪過路，升級後可能失去農場。
* 建議（Lead 決策）：在 release note 標明；或後續卡在 `HDBF.Gameplay` 載入時偵測/寬容處理
  （本卡未做，屬 gameplay 變更）。

## 7. 後續：2 層（多層）方案（未實作，已定案方向）

* 2 層＝`Size {X:3, Y:2, Z:2}`（12 blocks）、`CollidersSpec` `size (3,2,2) / center (1.5,1,1)`、
  模型局部 y 0..2（block-corner origin 的 X/Z 平移不變）、block 索引
  `n → (x, y, z=n/(X*Y))`：z=0 為地面層、z=1 為二樓層；上層需要 `Floor`/`Any` 與樓梯/作業可達性設計。
* 地塊容量：**使用者已定案 2 層時 60 → 120**；需同步修需求 13.3（3×2×1 硬約束）、13.5、5.1 表與
  成本/科學/工人/水耗平衡，並重算 §7 的試算。
* 美術：ART-01 產生器目前只做 1 層；2 層模型（牆面約 2× 面積、6000 tri 預算要重配或提高）＋
  圖示重畫。ART-02 的 `.timbermesh` 管線只需把 `SIZE_Y` 改 2、模型 y 0..2，
  X/Z 的半格補償不變（Y 無補償）。
* 引擎可行性：原版已有 `BuildersHut (3,2,2)`、`LargeWarehouse (3,3,2)`、`DistrictCenter (3,3,5)`、
  `EarthRecultivator (7,7,8)` 等多層建築，無引擎風險。

## 8. 重跑與證據路徑

* 修正腳本（一次性，已執行）：`.tmp-art04/fix_data.py`、`.tmp-art04/patch_validator.py`
  （暫存，非交付物；備份在 `.tmp-art04-backup/`）。
* 驗證證據：`build.ps1` 輸出、`tools/validate` 正常/負向輸出（本報告 §3）；
  實機證據 `docs/in-game/art04-*`（4 個回合檔：pre/loaded/run 截圖、Player.log、summary）。
* 實機重跑：
  ```powershell
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\dev\Invoke-GameRound.ps1 -Action Start -Tag <tag> -Tier BoxFarm.Basic.Folktails
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\dev\Invoke-GameRound.ps1 -Action ConfirmMods -Tag <tag> -Seconds 120
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\dev\Invoke-GameRound.ps1 -Action Stop -Tag <tag>
  ```
  占格 A/B 重跑：先 `craft-farm-fixture.py --out Saves\HDBFTest --path <x,y,z>`，再跑
  `Invoke-GameRound.ps1 -Action Prepare/Launch/ConfirmMods/Stop`（不要跑 `-Action Fixture`，
  它會重建沒有 probe path 的 fixture）。
