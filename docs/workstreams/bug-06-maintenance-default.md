# BUG-06 (P1) — 實體無 HDBF 紀錄／缺 `maintenance` 欄位時，`dueDay` 落在載入當日 → 載入首日就逾期 1 期（`G_maint = 0.5`）

Status: **程式層修復完成、L1 全綠、獨立驗證 PASS；實機未驗證**（本卡禁止部署、禁止啟動
Timberborn、禁止執行 `build.ps1`）。下一實機回合由 Lead 建置/部署後確認。
**round-15 的 in-game 回合已由同期工作流用掉**（Basic 對照組，且跑的是修復前的已部署 DLL，
`docs/in-game/round15-*`、`docs/VERIFICATION.md` §20.4），因此本修正的實機確認要等下一個
部署後的實機回合（本文一律稱 round 16）。卡日期：2026-10-25。只改程式與測試。

誠實一句話：成因用 round-14 實機日誌（`docs/in-game/round14-Player.log`）＋現行原始碼定位，
並以「修復前已部署的 `HDBF.Core.dll`」實跑驗證 pre-fix 行為；程式層修掉同一族的三個預設站點，
新增 24 條 game-free L1 測試，全量 L1 **PASSED 428 FAILED 0**（本卡新增 24 條；另 46 條為
同期 BUILD-02 卡的新測試，見 §9）。
**沒有**聲稱「實機已修好」——那要等下一個部署後的實機回合（round 16）日誌顯示注入的頂級離網農場載入首日
`compat.harmony-patch-applied.*` 的 `growthRate` 由 0.198 變為 ≈0.396（= round-14 的兩倍，
即 `G_maint` 由 0.50 回到 1.00），`scaledDays` 由 50.63 變為 ≈25.3。

---

## 1. 實機證據（round 14，2026-10-25 13:23–13:31）

日誌：`docs/in-game/round14-Player.log`、`round14-summary.txt`。fixture 注入一座
`BoxFarm.Top.Folktails`（頂級、27×27、容量 600、離網無電網），該實體在存檔中**沒有 HDBF 持久化
紀錄**。關鍵行（`round14-summary.txt`，行號即該檔行號）：

```
:25 [HDBF/Info] diag.day...: day.tick ... day=2.18 registered=600 moist=359 dry=241
    tank=0.00 coverage=0.598 workers=2 shutdown=False
:31 [HDBF/Info] compat.harmony-patch-applied.1: ... cell=(131,142,3)
    growthRate=0.198 vanillaDays=10.00 scaledDays=50.63
    （共 12 行，數值相同；第一個 crop 起始就被改寫）
:44 diag.day: day=3.18 ...   :51 diag.day: day=4.18 ...   :58 diag.day: day=5.18 ...
```

乘數分解（與 `docs/VERIFICATION.md` §20.1 一致，可反推）：

* `G_irr = 0.663`（`C=0.598`，頂級 `C_min=0.80`）；
* `G_power = 0.60`（頂級離網回退）；
* `G_maint = 0.50`（**逾期 1 期**，本卡主題）；
* `0.663 × 0.60 × 0.50 = 0.199 ≈ 實機 growthRate 0.198`；`10.00 / 0.198 ≈ 50.6` 天
  （`scaledDays=50.63`）。

也就是說：一座「沒有任何 HDBF 紀錄」的既有實體，在載入後**第一個日 tick 就已被判逾期 1 期**，
而不是需求 12.1/12.2「缺欄位安全預設值」要求的 `dueDay = 當前遊戲日 + 一個維護週期`、
`cyclesMissed = 0`（理由：避免讀檔即停擺/降產）。

> 註：`docs/VERIFICATION.md` §20.3 與 `docs/NEXT_ROUND.md` 當時寫「安全預設 0」。經本次以
> pre-fix 已部署 DLL 實跑（§3.1）更正為 **`dueDay == 載入日`（≈1.18），不是 0**；兩者同樣造成
> day 2.18 時 `cyclesMissed=1`，但成因的程式路徑必須記為「載入日 + `MaintenanceCycleDays(0)`」。
> `VERIFICATION.md` 由 Lead 依流程維護，本卡依硬規則不改動它。

---

## 2. 根因（pre-fix 程式路徑；行號為修復前後皆相同的現行位置，已逐一核對）

### 2.1 完整呼叫鏈

| 步 | 位置（現行原始碼） | 行為 |
|---|---|---|
| (a) 建預設紀錄 | `HdbfBoxFarmController.cs:201-202` `Awake` → `HdbfSaveCodec.CreateDefault(0L, tier, profile, today)`；fixture 載入路徑為 `HdbfEntitySaveBridge.Load` → `SafeDefault` → 同一個 `CreateDefault`（`HdbfEntitySaveBridge.cs:438-457`，呼叫點 `:125`/`:129`） | 無紀錄實體的 `DueDay = currentDay + profile.MaintenanceCycleDays`（pre-fix `HdbfSaveCodec.cs:78-80`） |
| (b) 讀檔 | save 裡沒有 `HDBF.BoxFarm` 元件 → `loader.TryGetComponent(...)` 回 false，**這不是損壞**，直接回 fallback（`HdbfEntitySaveBridge.cs:155-161`） | fallback 就是 (a) 的預設紀錄（Top：`dueDay = 1.18 + 0 = 1.18`），`degraded=false` |
| (c) 初始化鎖定 | `HdbfBoxFarmController.Load`（`HdbfBoxFarmController.cs:288-309`）把 `_record` 換成 fallback 後設 `_saveLoaded = true; _maintenanceInitialized = true;`（`:296-297`） | **關鍵**：日 tick 的「首次初始化」分支（`HdbfBoxFarmController.Simulation.cs:253-265`）只在 `!_maintenanceInitialized` 時執行，因此載入後的日 tick **永遠不會**重設 `dueDay`；`dueDay=1.18 > 0`，`else if (_record.DueDay <= 0f)` 的離網切換分支（`:266-271`）也不會觸發 |
| (d) 逾期計算 | day 2.18 tick：`offGrid = NeedsPower && !_powerConnected`（`:225`）→ 離網 cycle 8；`MaintenanceCyclesMissed(1.18, 2.18, 8)`（`:273`；`BalanceFormulas.cs:150-165`） | `today >= dueDay` → `1 + floor((2.18-1.18)/8) = 1`（逾期 **1** 期） |
| (e) 乘數 | `MaintenanceMultiplier(1)`（`:281`；`BalanceFormulas.cs:171-179`）= **0.50** → `_maintenanceMultiplier` → `TryGetPlotGrowthRate` 的 `GrowthMultiplier(...)`（`HdbfBoxFarmController.Growth.cs:54-59`）→ Harmony postfix 改寫 crop timer | 實機 `growthRate` 由 1.0 附近降為 0.198 |

### 2.2 為什麼偏偏是頂級（Top）

`TierCatalog.cs:174-195`：Top 的 `MaintenanceKind = PowerOrOffGridMetal`，
`MaintenanceCycleDays = 0f`（接電時由電力自動維護，沒有材料週期），
`OffGridMaintenanceCycleDays = 8f`（離網 = 15 金屬 / 8 日）。
`CreateDefault` 只用了 `MaintenanceCycleDays`，**忽略了 `OffGridMaintenanceCycleDays`**，於是
`currentDay + 0 = currentDay`＝載入當日，第一個 tick 必然算 1 期逾期。
中級/高級 `MaintenanceCycleDays=10` 本來就對；基礎版 `IrrigationOnly` 在日 tick 一開始就被
短路（`Simulation.cs:215-223`），永不計逾期——所以只有 Top 以「0 週期」踩中。

### 2.3 同病站點（本次一併修掉，否則換條路徑仍會重現）

以 pre-fix 已部署 DLL 實跑（見 §3.1）確認：

1. **`CreateDefault`**（`HdbfSaveCodec.cs:78-80` pre-fix）：`Top` 得到 `dueDay = currentDay`。
2. **`TrySanitize` 的非有限 `dueDay` 修復**（pre-fix `HdbfSaveCodec.cs:157-164`）：用同一個
   `currentDay + MaintenanceCycleDays` 公式（Top → `currentDay`），**且沒有把 `cyclesMissed`
   歸零**（實測 `cyclesMissed=3` 保留）。另外 `dueDay=0`／負數被視為 clean（`clean=true`，
   不修），與 §12.1「負/缺 → 安全預設」不符。
3. **`Migrate` 的 v0 補值**（pre-fix `HdbfSaveCodec.cs:306-314`）：v0 紀錄無 `dueDay` 時用同一
   buggy 公式（v0 Top → `currentDay`），是第二條重現路徑（目前沒有已發布的 v0 存檔，但遷移器
   契約要求正確）。
4. **`HdbfEntitySaveBridge.Load` 的缺欄位補值**（`HdbfEntitySaveBridge.cs:176-192`，`dueDay`
   在 `:180`）：缺 `dueDay` 鍵時用 `fallback.DueDay` —— bridge 本身不用改，fallback 修好後
   自動繼承正確預設。
5. **日 tick 首次初始化**（`Simulation.cs:253-265`）：新放置建築走的是
   `_record.DueDay = today + cycleDays`，cycle 本來就由執行期 power 狀態解析（Top 離網 8），
   方向正確；本次只是把 cycle 解析收斂到同一個 `MaintenanceDefaults.CycleDaysFor`，
   讓「載入預設」與「執行期初始化」不可能再分叉。
6. **`HdbfEntitySaveBridge.BareDefault`**（`:463-483`，`DueDay = 0f`）：只在 `profile` 為
   null 或 `CreateDefault` 丟例外的最後防線；不是 round-14 路徑。`dueDay=0` 之後會被日 tick 的
   `else if (_record.DueDay <= 0f)` 分支（`Simulation.cs:266-271`）在離網時重新排程，維持原樣
   並在 §8 記錄為殘餘風險。
7. `Awake` 的 `TankWater = TankCapacity` 與 `HdbfEntitySaveBridge` 的讀寫本身與本 bug 無關；
   `TryPerformMaintenance`（`Commands.cs:72`）走執行期 `_cycleDays`（已是正確來源），未改。

> 因此只修日 tick 或只修 bridge 都**不會**修好本 bug：載入後 `_maintenanceInitialized=true`
> 使日 tick 不再重設 `dueDay`，**載入時的第一個預設值才是決定性的**。

---

## 3. 修法

### 3.1 先以 pre-fix 二進位證實假設（獨立驗證者執行）

用 scratch net9.0 console（`%TEMP%`，未動工作區）載入**修復前已部署**的
`C:\Users\user\Documents\Timberborn\Mods\HDBF\HDBF.Core.dll`（mtime 13:24:34，sha256 `28994122…`）：

```
CreateDefault(Top, currentDay=1.18)        -> DueDay=1.18, CyclesMissed=0   ← 載入日即到期
CreateDefault(Intermediate, 1.18)          -> 11.18
CreateDefault(Advanced, 1.18)              -> 11.18
CreateDefault(Basic, 1.18)                 -> 0
TrySanitize(null, Top, currentDay=1.18)    -> DueDay=1.18
TrySanitize(Top, due=NaN/±Inf, currentDay=1.18) -> DueDay=1.18, cyclesMissed 保留 3（未歸零）
```

這同時修正了文件上的「安全預設 0」說法（見 §1 註）。

### 3.2 新增單一來源：`src/HDBF.Core/Balance/MaintenanceDefaults.cs`（新檔，game-free）

所有週期都從 **config 解析後的 `TierProfile`** 推導，程式不寫死 8/10：

| API | 語意 |
|---|---|
| `CycleDaysFor(profile, offGrid)` | 執行期日 tick 用：`IrrigationOnly → 0`；`offGrid && OffGridMaintenanceCycleDays>0` → 該值，否則 `MaintenanceCycleDays`；非正/非有限 → 0。Top 離網 8、接電 0；中級/高級 10；基礎 0。 |
| `DefaultCycleDays(profile)` | 載入時「缺 `maintenance`」的保守週期：`IrrigationOnly → 0`；`PowerOrOffGridMetal` 取 `OffGridMaintenanceCycleDays`（>0 時，Top=8），否則 `MaintenanceCycleDays`；其餘取 `MaintenanceCycleDays`（中/高=10）；非正/非有限 → 0。 |
| `IsUsableDueDay(profile, dueDay)` | 既有合法紀錄的判定：有限且（`>0` 或（`==0` 且 `IrrigationOnly`））。負數／NaN／±Inf 一律不可用。 |
| `ResolveDueDay(profile, currentDay, savedDueDay)` | **先**原封不動回傳可用的 `savedDueDay`（避免「讀檔重置倒數＝免費維護」）；不可用時：`IrrigationOnly → 0`；`cycle = DefaultCycleDays`；`cycle<=0` 或 `currentDay` 非有限 → 0；否則 `currentDay + cycle`（結果非有限 → 0）。 |

為什麼載入時用「離網週期」：載入發生在電網狀態被解析之前，Top 的 on-grid 週期是 0（接電自動
維護、不需材料），只有離網週期是「真正的材料週期」；用 8 天做預設才不會讀檔即降產
（需求表格「`maintenance` 缺 → `dueDay = 當前遊戲日 + 周期`」）。

### 3.3 三個站點改為呼叫單一來源（`HdbfSaveCodec.cs`）

1. `CreateDefault`（`:78-83`）：`DueDay = MaintenanceDefaults.ResolveDueDay(profile, currentDay, 0f)`。
   → Top `currentDay+8`、中/高 `currentDay+10`、基礎 `0`。
2. `TrySanitize`（`:159-173`）：v1+ 且 `dueDay` 不可用（非有限，或材料維護檔位 `<=0`）→
   `degraded=true`、`reason="due-day-invalid"`、`DueDay = ResolveDueDay(...)`、
   `CyclesMissed = 0`。**v0 紀錄刻意不在這裡修**（保留既有行為：交給 `Migrate` 填一次，
   結構完整的 v0 紀錄仍 `clean=true`、`reason=null`，見 `SaveRoundTripFaultTests`）。
3. `Migrate` v0 分支（`:312-330`）：改用同一個 `ResolveDueDay`；只有真的「缺/不可用」才填
   （`resolvedDueDay != copy.DueDay`），填的時候 `CyclesMissed=0`；已有合法的 v0 `dueDay`
   原封不動。v0 Top 的預設因此是 `currentDay+8` 而非 `currentDay+0`。

### 3.4 日 tick 對齊（`HdbfBoxFarmController.Simulation.cs:225-229`）

`cycleDays = MaintenanceDefaults.CycleDaysFor(_profile, offGrid)` 取代原本的 inline 三元式；
其餘（首次初始化 `today + cycleDays`、離網切換、`MaintenanceCyclesMissed`）不變，逾期數學不動。

### 3.5 不變更保證（逐項對照卡要求）

* **既有合法 dueDay 原封不動**：`ResolveDueDay` 先回傳 `savedDueDay`；L1
  `TestSanitizeKeepsLegalTopDueDayAndMissedCount`（42.5 / missed=2 逐位相同、clean、reason=null）、
  `TestMigratePrototypeKeepsExistingDueDay`（v0 33.5 保留）、
  `TestResolveDueDayKeepsUsableSavedValue` 固定。
* **逾期計算不動**：`BalanceFormulas.MaintenanceCyclesMissed` / `MaintenanceMultiplier` 零改動；
  L1 `TestOverdueMathStillReportsOneAndTwoCycles`、`TestOverdueMathNotYetDueIsOnSchedule`
  （1 期 0.5、≥2 期 0、未到期 1.0）固定。
* **無魔術數字**：8/10 只存在於 `TierCatalog`；`MaintenanceDefaults` 分支沒有 8/10 字面值；
  L1 `TestConfigOverrideDrivesDefaultCycle` 用 `Top with { OffGridMaintenanceCycleDays = 12 }`、
  `Intermediate with { MaintenanceCycleDays = 7 }` 證明 config 解析值驅動預設。
* **無新增 static 可變狀態**：`MaintenanceDefaults` 是無狀態 static 純函式。
* **不動 Harmony patch**（BUG-03 補丁與 `src/HDBF.Integration/**` 零改動）、
  **不動 Diagnostics.cs**（仍在，未改）、不動巡邏/灌溉/UI/平衡數值/data。

---

## 4. 測試（L1，game-free）

新檔：`tests/HDBF.Tests/Bug06MaintenanceDefaultTests.cs`（24 條，`HDBF.Tests` 反射自動發現，
未改 `TestRunner`/csproj；只引用 `System` + `HDBF.Core`，不碰遊戲組件）。分組：

| 組 | 測試 | 釘住的行為 |
|---|---|---|
| 單一來源／cycle | `TestDefaultCycleDaysMatchesTierTable`、`TestConfigOverrideDrivesDefaultCycle`、`TestCycleDaysForSelectsLivePowerState`、`TestCycleDaysForNormalisesInvalidConfigCycle` | tier 表 0/10/10/8；config 覆寫（Top 12、Inter 7）驅動預設；`CycleDaysFor` 矩陣；負/NaN/∞ cycle → 0 |
| `CreateDefault` | `TestCreateDefaultTopSchedulesOffGridCycle`、`TestCreateDefaultMaterialTiersScheduleMaterialCycle`、`TestCreateDefaultBasicHasNoMaintenanceDate`、`TestCreateDefaultTopAtLoadDayDoesNotScheduleToday` | Top@100→108、Inter@5→15、Adv@7.5→17.5、Basic→0；**回歸：Top@1.18→9.18，絕不 <= 載入日** |
| round-14 重演 | `TestRound14TopLoadStartsOnSchedule`、`TestRound14IntermediateLoadStartsOnSchedule`、`TestRound14GrowthRateIsNotDeratedByMaintenance` | 缺 maintenance → due=load day+cycle、首 tick `missed=0`、`G_maint=1.0`；同情境 growthRate = 0.663×0.60×1.0 = 0.3978（實機 0.198 的兩倍） |
| `TrySanitize`（v1） | `TestSanitizeReschedulesInvalidTopDueDay`、`TestSanitizeReschedulesInvalidIntermediateDueDay`、`TestSanitizeKeepsLegalTopDueDayAndMissedCount`、`TestSanitizeKeepsBasicZeroDueDay` | 迴圈 `0 / -5 / NaN / +Inf / -Inf` → degraded、`"due-day-invalid"`、`currentDay+8`（Top）／`+10`（Inter）、`cyclesMissed` 3→0、tier 不變；合法 42.5/missed=2 逐位保留；基礎 0 合法不降級 |
| v0 遷移 | `TestMigratePrototypeIntermediateFillsDefaultOnce`、`TestMigratePrototypeTopFillsOffGridDefault`、`TestMigratePrototypeKeepsExistingDueDay`、`TestMigratePrototypeDefaultIsFilledExactlyOnce` | v0 Inter 7→17 且 clean/schema=1；v0 Top 7→15；既有 33.5 保留；隔日重載仍 17（只填一次） |
| 契約矩陣 | `TestIsUsableDueDayMatrix`、`TestResolveDueDayKeepsUsableSavedValue`、`TestResolveDueDayNonFiniteCurrentDayFallsBackToZero` | 可用性矩陣；可用值 verbatim；currentDay 非有限 → 0 |
| 逾期數學 | `TestOverdueMathStillReportsOneAndTwoCycles`、`TestOverdueMathNotYetDueIsOnSchedule` | 1 期→0.5、2 期→0、未到期→1.0（本次零改動的 guard） |

測試與決策都放在 Core 純函式上，**不需要遊戲組件**（符合卡要求「把決策抽成 Core 純函式再測」）。

---

## 5. 建置與測試實際輸出（本機沙箱，2026-10-25）

```powershell
$w = Join-Path $env:TEMP 'hdbf-card-work-final'
dotnet build src\HDBF.Core\HDBF.Core.csproj -c Release -m:1 -p:HdbfWorkRoot=$w
dotnet build src\HDBF.Persistence\HDBF.Persistence.csproj -c Release -m:1 -p:HdbfWorkRoot=$w
dotnet build src\HDBF.Gameplay\HDBF.Gameplay.csproj -c Release -m:1 -p:HdbfWorkRoot=$w
dotnet build tests\HDBF.Tests\HDBF.Tests.csproj -c Release -m:1 -p:HdbfWorkRoot=$w
dotnet "$w\bin\HDBF.Tests\Release\net9.0\HDBF.Tests.dll"
```

四支 build 皆 `0 個警告 / 0 個錯誤`；L1：

```
PASSED 428  FAILED 0
```

（428 = 本卡前基線 358 + 本卡新增 24 + 同期 BUILD-02 卡新測試 46；本卡自身為
358 + 24 = 382，全數含在 PASSED 內。完整 log 由執行者留存於 `%TEMP%\hdbf-bug06-gate.log`。）

獨立驗證者（verifier，唯讀）用**另一個全新 workroot** 重跑同一組 5 條命令：四支 build 全部
`0 warnings / 0 errors`、`PASSED 428 FAILED 0`、exit 0；並用反射逐條呼叫我方
`Bug06MaintenanceDefaultTests` 的 24 條測試：**24/24 PASS**。BUILD-02 在 13:48 又寫了一次後，
verifier 於 13:51 在同一棵樹上再跑一輪 drift gate：仍然 4×0/0 + `PASSED 428 FAILED 0`（見 §10）。
所有 build 產物都在 `%TEMP%`，工作區沒有新增 `bin/obj`。

> 沙箱註記：`$env:TEMP` 被 DSH 導向可寫的 temp 子目錄，`HdbfWorkRoot` 寫入正常，無拒寫情形。
> 13:39 左右 `HDBF.Gameplay` 曾因**同期 BUILD-02 卡**半寫完的
> `HdbfPlotHighlightService.cs` 暫時編譯失敗（非本卡檔案、非本卡改動），該卡 13:44 完成後
> Gameplay 恢復 `0 錯誤`；本卡最終 gate 於其後重跑，結果如上。

---

## 6. 下一實機回合（round 16）實機預期 — 待驗證，不是已驗證

> 先釐清：round 15（13:34–13:52）是同期 in-game 工作流跑的 **Basic 對照組**，且部署的
> `HDBF.Core.dll` 仍是 13:24:34 的修復前版本（sha256 `28994122…`），所以 round-15 日誌
> **不是**本修正的證據；本節寫的是「下一次部署本卡修正後」的預期。

部署本卡 Core 後，用與 round-14 相同的 fixture（`craft-farm-fixture.py --tier
BoxFarm.Top.Folktails`，注入無 HDBF 紀錄的頂級離網農場，載入日 ≈ day 1.18）：

1. **載入即排程**：該實體 `Load` fallback 的 `dueDay = 載入日 + OffGridMaintenanceCycleDays =
   1.18 + 8 = 9.18`，`cyclesMissed = 0`。
2. **第一個維護週期內 `G_maint=1.0`**（day 2.18～day 9.18 之前；round-14 只跑到 day 5.18，
   全程都應是 1.0）。證據看 `compat.harmony-patch-applied.*`：
   * `growthRate` ≈ **0.396**（round-14 是 0.198，剛好兩倍；實際 = `G_irr(coverage≈0.595~0.598)
     × G_power 0.60 × 1.0`，會隨 coverage 小幅浮動）；
   * `scaledDays` ≈ **25.3**（round-14 是 50.63 的一半）；
   * 分解式由 `G_irr × 0.60 × 0.50` 變成 `G_irr × 0.60 × 1.00`。
3. **`diag.day` 沒有維護欄位**（只有 `shutdown=` 與間接的 growthRate）；`shutdown=False` 在
   round-14 就已如此，不能單獨當證據，必須看 patch 行的 `growthRate`/`scaledDays` 或另開
   診斷。若下一輪要直接看 `dueDay`，需由 Lead 決定是否臨時加一行診斷（本卡不動 Diagnostics.cs）。
4. **真實存檔回歸**：任何「已有合法 HDBF 紀錄」的農場（含既有存檔）讀檔後 `dueDay` **完全不變**，
   不會重置倒數（避免免費維護）；此點由 L1 逐位固定，實機可用「存檔前後 dueDay 不變」的
   存讀檔回合確認。
5. **逾期降級仍會正常發生**：無紀錄的 fixture 若跑到 day ≥ 9.18 仍未維護，`G_maint` 會變 0.5，
   day ≥ 17.18 起 shutdown——這是需求行為（round-14 只跑到 5.18，不會看到）。
6. **不要**期待 `docs/VERIFICATION.md` 由本卡更新：依硬規則本卡不改該檔與
   `docs/REQ_TRACEABILITY.md`，需由 Lead 在實機回合後更新（含修正 §20.3 的「預設 0」為
   「預設＝載入日」的註記）。

---

## 7. 改動檔案清單

新增：

* `src/HDBF.Core/Balance/MaintenanceDefaults.cs`（單一來源：cycle 選擇 + safe default + 合法 dueDay 判定）
* `tests/HDBF.Tests/Bug06MaintenanceDefaultTests.cs`（24 條 game-free L1）
* `docs/workstreams/bug-06-maintenance-default.md`（本報告）

修改：

* `src/HDBF.Core/Save/HdbfSaveCodec.cs` — `CreateDefault` / `TrySanitize` / `Migrate` 改用
  `MaintenanceDefaults`（三個站點，見 §3.3）
* `src/HDBF.Gameplay/Components/HdbfBoxFarmController.Simulation.cs` — cycle 來源收斂到
  `MaintenanceDefaults.CycleDaysFor`（見 §3.4）

刻意未改（硬規則）：

* `data/**`、`docs/VERIFICATION.md`、`docs/REQ_TRACEABILITY.md`、`build.ps1`、
  `Directory.Build.*`、`src/HDBF.Integration/**`（0 Harmony patch 變更）、
  `HdbfBoxFarmController.Diagnostics.cs`（保留給 Lead 取證）、`HdbfEntitySaveBridge.cs`
  （已審查：其 fallback 繼承修正後的 `CreateDefault`，bridge 本身無須改；`BareDefault`
  的最後防線見 §8）、巡邏/灌溉/UI/兼容碼。

---

## 8. 殘餘風險／已知範圍外

1. **無紀錄的頂級離網農場只在第一個 8 天週期內「準時」**：day 9.18 之後若沒有玩家維護
   （`TryPerformMaintenance`；材料扣除仍是 v0.1 的整合缺口），仍會 0.5 → shutdown。這與正常
   農場相同語意，不是本 bug 的回歸。
2. **頂級接電 ↔ 離網切換的長期行為是既有語意**：日 tick 只在 `dueDay <= 0` 時重新排程
   （`Simulation.cs:266-271`），接電期間 `dueDay` 會停在舊值；若一座農場接電很久後才斷電，
   可能一次算很多期逾期。此為 pre-fix 既有行為、不在本卡範圍（本卡不重設既有 `dueDay`，
   否則等於免費維護）；若要修需另開卡並修改需求 REQ-IMPL-21 的排程語意。
3. **`HdbfEntitySaveBridge.BareDefault` 仍寫 `DueDay=0`**（只在 profile null／`CreateDefault`
   丟例外時）：離網時日 tick 的 `dueDay <= 0` 分支會重新排程，且不會出現在正常路徑；
   若要更保守可另開卡讓它也吃 `TierCatalog` fallback。
4. **部分損壞（元件存在、只有 `dueDay` 鍵缺失、`cyclesMissed` 鍵存在）**：bridge 用
   `fallback.DueDay`（修好後＝載入日+cycle）補位，`cyclesMissed` 保留存檔值；第一個日 tick 會
   用 `dueDay` 重算 `cyclesMissed`，而 gameplay/UI 讀的是快取 `_cyclesMissed`（初始 0），
   因此不會出現讀檔即降產。bridge 未特別把此組合標記為 degraded。
5. **同期 BUILD-02 卡**在同工作區改動 plot-highlight 相關檔案（Core/Gameplay/UI/tests）；
   本卡 gate 已在其 13:42 後的空檔重跑並全綠，但若兩卡同時再寫，`HDBF.Gameplay` 可能暫時
   編譯失敗——merge/最終 gate 由 Lead 負責。

---

## 9. 驗證邊界（誠實聲明）

* 本卡**只做程式層與 L1**：沒有部署、沒有啟動 Timberborn、沒有跑 `build.ps1`、沒有實機日誌。
* 根因是「依 round-14 實機日誌定位 + 對 pre-fix 已部署 DLL 實跑重現」；修法是「程式層修正 +
  game-free L1 回歸」；**實機修復效果待 round 16（部署本卡後）驗證**。
* 全量 L1 428 條中，本卡新增 24 條；其餘 404 條（基線 358 + 同期 BUILD-02 卡 46）亦全綠，
  代表本次改動沒有破壞既有存檔／平衡／遷移契約。
* 同期工作流（BUILD-02 plot-highlight、round-15 in-game、UI）也在同一工作區寫檔案：
  `docs/VERIFICATION.md` 的 §20.4、`data/Localizations/*.csv`、`HdbfPlotHighlight*.cs` 等
  **不是本卡改的**（verifier 逐檔 hash/時間戳比對確認）；本卡沒有碰
  `docs/VERIFICATION.md`／`docs/REQ_TRACEABILITY.md`（見 §10 的 constraint audit）。

---

## 10. 獨立驗證（verifier，read-only；程式層，非實機）

由另一個 agent（不共用本卡假設）獨立完成，全程只讀工作區、build 產物寫在 `%TEMP%`：

1. **根因獨立確認（含文件更正）**：以 scratch net9.0 probe 載入**修復前已部署**的
   `Mods\HDBF\HDBF.Core.dll`（mtime 13:24:34，sha256 `28994122…`）實跑，得到
   `CreateDefault(Top, currentDay=1.18) = DueDay 1.18`，證明 pre-fix 預設是**載入日**、
   不是文件 §20.3 寫的「0」；並以 `MaintenanceCyclesMissed(1.18, 2.18, 8)=1` 對上 round-14
   日誌的 `G_maint=0.5` / `growthRate=0.198`。
2. **Gate PASS（獨立 workroot 重跑卡上 5 條命令）**：Core / Persistence / Gameplay / Tests
   四支 build 全部 exit 0、`0 warnings / 0 errors`；`PASSED 428 FAILED 0`；反射逐條呼叫本卡
   24 條測試 **24/24 PASS**；BUILD-02 13:48 再寫入後做 drift re-run 仍全綠。
3. **Adversarial probe（pre-fix DLL vs 修復後 Core）**：

   | 情境 | pre-fix | 修復後 |
   |---|---|---|
   | `CreateDefault(Top, 1.18)` | 1.18 | **9.18**, missed=0 |
   | `TrySanitize(v1 Top due=0/-5, missed=3)` | clean/未修（0/-5 保留） | degraded `due-day-invalid`, 9.18, missed=0 |
   | `TrySanitize(v1 Top due=NaN/±Inf)` | 1.18，missed 保留 3 | 9.18, missed=0 |
   | `TrySanitize(v1 Top due=42.5, missed=2)` | 42.5/2 clean | 42.5/2 clean（逐位相同） |
   | `TrySanitize(v0 Top due=0)` | 1.18 | **9.18**, schema→1 |
   | `MaintenanceCyclesMissed(9.18, 2.18, 8)` | 0 → G_maint 1.0 | 0 → 1.0（數學未動） |
   | 乘積（0.663×0.60×…） | 0.199 → 日誌 0.198 | **0.398** |

4. **Constraint audit（135 檔 baseline，逐檔 hash/mtime）**：本卡只新增/修改
   `MaintenanceDefaults.cs`、`HdbfSaveCodec.cs`、`Simulation.cs`、`Bug06MaintenanceDefaultTests.cs`
   與本報告，全部 hash-stable；`data/**`（含 `HDBF_Config.json`）、
   `src/HDBF.Integration/Harmony/**`（6/6，0 patch 變更）、
   `HdbfBoxFarmController.Diagnostics.cs`（存在且未改）、`REQ_TRACEABILITY.md`、`build.ps1`、
   `Directory.Build.*`、既有 24 個測試檔皆不變；`MaintenanceDefaults` 無欄位/無 static 可變狀態；
   resolver 原始碼（去註解）只有 `0f` 字面值、沒有 8/10。
   唯一「protected file」衝突是同期 round-15 in-game 工作流在 13:48:04 改了
   `docs/VERIFICATION.md`（新增 §20.4 與 `round15-*` 檔），**非本卡所為**，已列入 §8。
5. verifier 的殘餘風險清單與本卡 §8 一致（程式層驗證、非實機；record-less Top 只在
   loadDay+8 內準時；on-grid→off-grid 舊 `dueDay` 是既有語意、範圍外；v1 `dueDay=0` 的紀錄
   現在會記一次 degraded 修復）。
