# BUG-03 / D-2 — 單一 Postfix 讓四層乘數真正拖慢原版作物生長（ADR-009）

Status: **離線實作完成；Core/Integration/Gameplay/Tests/UI/validate 編譯 0 warning / 0 error；L1 `PASSED 358 FAILED 0`。
尚未實機驗證**（本卡禁止部署、禁止啟動 Timberborn、禁止跑 `build.ps1`；實機由 Lead 之後執行）。

Card date: 2026-10-25. Workspace: `Documents\Timberborn\Mods\HighDensityBoxFarm`（source only）。
前置卡：BUG-01（plot registration）、BUILD-01（panel plots）與 BUG-04/05 已完成；本卡未改灌溉取樣、託管格註冊、
UI、兼容適配器邏輯。

一句話結論：原版作物生長時間是 **wall-clock 排程**（`TimeTriggerService` 的絕對完成時間戳），不是每幀 progress
累加；唯一乾淨的 Postfix 點是 **`Timberborn.Growing.Growable.ResumeGrowing()`**（private instance void）。
在該 Postfix 把該作物的 timer 長度乘上 `1 / GrowthRateMultiplier(G_irr×G_fat×G_maint×G_power)`，只對
`HdbfBoxFarmController` 擁有的托管格生效；Harmony 以 **純反射** 建立（不編譯期引用 HarmonyLib、不夾帶 0Harmony.dll），
開關 `features.growthMultiplierPatch`（預設 on）關閉時完全不安裝補丁，回到原本 D-2 行為。

---

## 0. 權威依據與硬約束對照

| 來源 | 內容 |
|---|---|
| `docs/ARCHITECTURE.md` ADR-009（使用者 2026-10-25 批准） | 最多 1 個 Postfix，只准 Postfix、禁 Transpiler、單一 Harmony instance `hdbf.highdensityboxfarm`、整補丁 try/catch、失敗 3 次自動卸載、開關 `features.growthMultiplierPatch`、只影響本模組托管格 |
| 需求文件 ch. 6.1 / REQ-CORE-03 / REQ-BAL-02 / REQ-BAL-28 | `R = base × G_irr × G_fat × G_maint × G_power × G_mode`，任一層 0 → 該格不產出 |
| 需求文件 ch. 8.4 / REQ-COMPAT-16/17/18 | 補丁類 `HDBF.Patches.`、單一實例、白名單+簽名校驗、衝突檢查、try/catch、3 次失敗卸載、補丁類不可持有 static 可變狀態 |
| 本卡 | `G_irr/G_fat/G_maint/G_power` 要真的拖慢生長、只影響托管格、快取 cell→farm、不新增 static 可變狀態、Integration 只引用 Core |

---

## 1. IL 取證：原版生長的真正推進點（1.1.2.8）

工具：`tools/ApiIndex`（Mono.Cecil 讀 `Timberborn_Data\Managed`，唯讀）。本卡新增的 dump（在 `%TEMP%\hdbf-il\`，
可重跑，見 §7 指令）：`timetrigger-all.txt`、`timetriggerservice-il.txt`、`growable-all.txt`、`cuttable-il.txt`、
`yielder-il.txt`、`blockobject-il.txt`、`entitycomponent-il.txt`。

### 1.1 `Growable` 只擁有 timer，不自行累加 progress

`Timberborn.Growing.Growable`（`[Timberborn.Growing]`）成員（`%TEMP%\hdbf-il\growable-all.txt`）：

```il
=== Timberborn.Growing.Growable::Awake
  IL_0024: ldarg.0
  IL_0025: ldarg.0
  IL_0026: call !!0 BaseComponent::GetComponent<GrowableSpec>()
  IL_002b: stfld GrowableSpec Growable::_growableSpec
  IL_0030: ldarg.0
  IL_0031: ldarg.0
  IL_0032: ldfld ITimeTriggerFactory Growable::_timeTriggerFactory
  IL_0037: ldarg.0
  IL_0038: ldftn Void Growable::Grow()
  IL_003e: newobj Action::.ctor
  IL_0043: ldarg.0
  IL_0044: call Single Growable::get_GrowthTimeInDays()
  IL_0049: callvirt ITimeTrigger ITimeTriggerFactory::Create(Action, Single)
  IL_004e: stfld ITimeTrigger Growable::_timeTrigger
```

→ 成長時間唯一來源是 `GrowableSpec.GrowthTimeInDays`（Wheat = 10.0 天）；`Growable` 本身沒有逐幀增加 progress 的方法。
`get_IsGrown` / `get_GrowthInProgress` / `get_GrowthProgress` 都只是轉呼 `_timeTrigger.Finished/InProgress/Progress`。

### 1.2 成長開始：`InitializeEntity → ResumeGrowing()`（private）→ `ITimeTrigger.Resume()`

```il
=== Timberborn.Growing.Growable::InitializeEntity
  IL_0030: ldarg.0
  IL_0031: call Boolean Growable::get_IsGrown()
  IL_0036: brtrue.s IL_004a
  ...  BlockReproduction(this)
  IL_0044: ldarg.0
  IL_0045: call Void Growable::ResumeGrowing()      // ← private instance void，無參數

=== Timberborn.Growing.Growable::ResumeGrowing   (API: method private Void ResumeGrowing())
  IL_0001: call Boolean Growable::get_IsGrown();  brtrue → ret
  IL_000e: ldfld LivingNaturalResource Growable::_livingNaturalResource; get_IsDead → ret
  IL_001b: ldfld DyingNaturalResource Growable::_dyingNaturalResource; get_IsDying → ret
  IL_0028: callvirt Void ITimeTrigger::Resume()
```

呼叫頻率：**每株作物實體初始化時一次**（`InitializeEntity`），以及死亡中止後的 recovery 事件（`StoppedDying` /
`ReversedDeath` handler → `ResumeGrowing`）。不是每幀、不是全域 Tick。

### 1.3 完成時間是絕對 wall-clock：`TimeTrigger.Resume → TimeTriggerService.Add(now + delayLeft)`

```il
=== Timberborn.TimeSystem.TimeTrigger::Resume
  IL_0011: ldfld IDayNightCycle TimeTrigger::_dayNightCycle
  IL_0016: callvirt Single IDayNightCycle::get_PartialDayNumber()
  IL_001c: ldfld TimeTriggerService TimeTrigger::_timeTriggerService
  IL_0022: ldarg.0
  IL_0023: ldloc.0
  IL_0024: ldarg.0
  IL_0025: ldfld Single TimeTrigger::_delayLeftInDays
  IL_002a: add
  IL_002b: callvirt Void TimeTriggerService::Add(TimeTrigger, Single)   // 絕對時間戳
  IL_0030: ldarg.0
  IL_0031: ldloc.0
  IL_0032: stfld Single TimeTrigger::_resumedTimestamp
  IL_0037: ... set_InProgress(true)

=== Timberborn.TimeSystem.TimeTrigger::get_DaysLeft
  InProgress ? (_delayLeftInDays - DaysSinceStart) : _delayLeftInDays
=== TimeTrigger::get_Progress
  IL_0000: ldc.r4 1
  IL_0005: ldarg.0; call Single TimeTrigger::get_DaysLeft()
  IL_000b: ldarg.0; ldfld Single TimeTrigger::_fullDelayInDays
  IL_0011: div
  IL_0012: call Single Mathf::Clamp01(Single)
  IL_0017: sub
```

`TimeTriggerService`（`il-timetriggerservice.txt`）在 `Tick()` 裡 `FindReadyToTrigger()`（比較
`SortableKey.Timestamp <= PartialDayNumber`）→ `Trigger(trigger)` → `Remove(trigger); trigger.Finish()`。
`TimeTrigger.Finish()`：`InProgress=false; Finished=true; _delayLeftInDays=0; _action.Invoke()`。
→ **完成時間在 Resume 時就被固定，progress 只是時間的函數，沒有逐幀累加可攔截**。

### 1.4 為什麼不能補在「完成」之後：`Grow` 已被 `Yielder` 消費

```il
=== Timberborn.Growing.Growable::Grow
  UnblockReproduction(this); HasGrown?.Invoke(this, EventArgs.Empty)

=== Timberborn.Cutting.Cuttable::Awake
  ... get_Yielder(); <Awake>b__22_0 (YieldDecreased)
  ... ldfld Growable Cuttable::_growable
  ... <Awake>b__22_1   // void <Awake>b__22_1 => get_Yielder().Enable()
  ... callvirt Void Growable::add_HasGrown(EventHandler)
=== Timberborn.Cutting.Cuttable::<Awake>b__22_1
  get_Yielder(); callvirt Void Yielder::Enable()
=== Timberborn.Yielding.Yielder::get_IsYielding
  get_Yield().Amount > 0      // Enable 後即為可收獲
```

→ 若 Postfix 掛在 `Grow` / `TimeTrigger.Finish` / `TimeTriggerService.Trigger`，`HasGrown` 已觸發、`Yielder` 已
Enable，作物當下就會被農場 `TryClassifyPlot`（`yielder.IsYielding && growable.IsGrown`）判定可收獲——無法再「事後慢下來」。
所以必須在 **完成時間被排定之前**（`ResumeGrowing` 的 Postfix）改寫 timer。

### 1.5 排除的候選

| 候選 | 為何不選 |
|---|---|
| `Growable.get_GrowthProgress` | 純 getter，回傳 `TimeTrigger.Progress`；不影響完成時間 |
| `Growable.IncreaseGrowthProgress` | 只被存檔還原與 HDBF 既有 D-2 密植通道呼叫；原版成長不經過它 |
| `GrowableSpec.get_GrowthTimeInDays` | 回傳 blueprint spec，spec 是每個 template 共用的，無法區分托管格/非托管格 |
| `TimeTriggerService.Tick` / `FindReadyToTrigger` | 全域 Tick，chapter 8.4 明文禁止「補全局 Tick」 |
| `TimeTriggerService.Add` | 每次 pause/resume/load 都呼叫；stateless 加長會與存檔/讀檔疊加造成越來越慢（詳見 §3.4） |
| `Growable.Grow` / `TimeTrigger.Finish` | 太晚：`HasGrown`→`Yielder.Enable` 已發生（§1.4） |

### 1.6 可否從被補丁物件取得 cell 與作物、Postfix 能否就地改寫欄位

| 問題 | 答案（IL 證據） |
|---|---|
| 取得 cell | `Growable` 是 crop entity 的 `BaseComponent`；crop blueprint 有 `BlockObjectSpec`（`Wheat.blueprint.json`），entity 上有 `BlockObject`。`BlockObject.Coordinates` 在 `BlockObject.PreInitializeEntity → Reposition → UpdateValues → set_Coordinates` 設定（`il`：`BlockObject::UpdateValues`），而 `PreInitializeEntity` 在 `EntityComponent.InitializeIfUninitialized` 中先於 `InitializeEntity` 執行（`entitycomponent-il.txt`）。Postfix 內 `__instance.GetComponent<BlockObject>().Coordinates` 即托管格的 plant cell |
| 取得目前作物 | `__instance` 就是 `Growable`；作物模板可再用 `GetComponent<TemplateSpec>()`（本卡不需要） |
| 就地改寫結果 | 完成時間欄位在 **internal** class `Timberborn.TimeSystem.TimeTrigger`：`_fullDelayInDays`（原始成長天數）與 `_delayLeftInDays`（剩餘天數）。Postfix 透過安裝期解析、bridge 快取的 `FieldInfo` 改寫這兩個 float，並用 public interface `ITimeTrigger.Pause()/Resume()` 重排時間戳 |
| 呼叫時機 | Postfix 執行於 `ResumeGrowing()` 之後：trigger `InProgress=true`、`resumedTimestamp=now`、`delayLeft=_fullDelayInDays`；此時改寫安全且尚未累積任何 progress |

---

## 2. 選定補丁點（唯一一個）

| 項目 | 值 |
|---|---|
| Patch id | `HDBF.Patches.BoxFarmGrowthPatch` |
| Harmony instance | `hdbf.highdensityboxfarm`（唯一） |
| 類型 | **Postfix**（無 Prefix、無 Transpiler；全補丁類別 1 個） |
| 目標型別 | `Timberborn.Growing.Growable`（`Timberborn.Growing.dll`） |
| 目標方法 | `private void ResumeGrowing()` — instance、無參數、回傳 void |
| Postfix signature | `public static void Postfix(Growable __instance, ITimeTrigger ____timeTrigger)` |
| Harmony 注入 | `__instance` = Growable；`____timeTrigger` = `Growable._timeTrigger`（private `ITimeTrigger`）。注意 Harmony 2.4.1 的欄位注入規則是「**三個底線 + 完整欄位名**」：IL `HarmonyLib.MethodCreatorTools::EmitCallParameter` 先 `ldstr "___"` + `StartsWith("___", Ordinal)` + `Substring(3)`，再用 `AccessTools.Field(type, name)`（`%TEMP%\hdbf-il\harmony-all-il.txt`），所以欄位 `_timeTrigger` 的參數名是 4 個底線的 `____timeTrigger`，不是 `___timeTrigger` |
| 白名單/簽名校驗 | 安裝前反射檢查 target 存在、不是 static、參數 0、回 void；`Growable._timeTrigger` 欄位存在且型別可轉 `ITimeTrigger`；`TimeTrigger`（internal，用字串找型別）的 `_fullDelayInDays`/`_delayLeftInDays` 皆 private float。任一不符 → 不裝、記 `HDBF/Warn`（`HdbfHarmonyInstaller.ValidateTarget`） |
| 衝突檢查 | `Harmony.GetPatchInfo(target)`：若已有任何 Postfix（其他模組）→ 不裝並記 warning（`HasConflictingPostfix`） |
| 失敗回退 | patch body 外層 try/catch；bridge 內層 try/catch；例外記 `HDBF/Error` 並保留原版成長；bridge 累計 3 次 → 自動 unpatch（REQ-COMPAT-18） |
| 卸載 | `HarmonyBootstrap.Unload()` → installer `Shutdown()`：`Harmony.Unpatch(target, postfix)`（找不到時 fallback `UnpatchAll(id)`）、bridge 隔離、`compat.ReportHarmonyPatchCount(0)` |

### 2.1 加長公式（真正讓四層「拖慢」的算式）

```
G     = BalanceFormulas.GrowthMultiplier(G_irr, G_fat, G_maint, G_power)   // 四層乘積，clamp [0,1]
rate  = BalanceFormulas.GrowthRateMultiplier(G)                           // clamp [0.05, 1]（見 §3.3）
scale = BalanceFormulas.GrowthTimeScale(rate) = 1 / rate                  // 1..20
TimeTrigger._fullDelayInDays = originalFullDelay * scale
TimeTrigger._delayLeftInDays = originalFullDelay * scale
```

例：Wheat 原始 10 天。
* 全滿足（G=1）→ Postfix 直接 return，10 天（補丁對健康農場是 no-op）。
* 頂級離網 `G_power=0.6` → 16.67 天。
* 維護逾期 1 週期 `G_maint=0.5` → 20 天。
* 疲勞 F=1 `G_fat=0.4` → 25 天。
* 四層同時 0.5×0.5×0.5×0.6 → G=0.075 → 133 天。
* 任一層 0（理論值）→ 走 §3.3 的 5% 下限 → 200 天（不是無限凍結）。

### 2.2 為什麼「取樣一次」而不是每個 Tick 重算

乘數在 **作物開始生長的那一刻** 取樣（新作物 entity 每次種下都是新的 `Growable`/`TimeTrigger`，所以每個生長週期都會
重新取樣）。週期進行中疲勞/維護/電力/覆蓋率變動不會回頭改這一株（下一株才套新值）。這是有意的取捨：

* 需求只要求「最終產出 = 乘積」；對稳态的維護逾期/離網/疲勞，每週期取樣一次即可讓週期長度 ≈ `vanilla / G`。
* 每 Tick 重算需要 per-trigger 狀態，會與 pause/resume（死亡恢復、存檔）疊加；chapter 8.4 也不允許全域 Tick 補丁。
* 殘餘風險已列在 §8（mid-cycle 電力/維護變化不追溯）。

---

## 3. 實作設計

### 3.1 裝配鏈如何守住（Integration 只引用 Core）

* `HDBF.Integration.csproj`：仍然 **只有 1 個 `ProjectReference`（→ HDBF.Core）**；本卡只新增一個 *game assembly*
  `<Reference Include="Timberborn.Growing">`（`Private=false`，遊戲提供，不複製到輸出）。沒有新增
  Integration→Gameplay 專案引用。
* patch（Integration）與 Gameplay 之間只透過 **HDBF.Core 介面** `HDBF.Core.Growth.IGrowthMultiplierSource` 通訊：
  * 介面在 Core：`IsPatchEnabled` + `TryGetGrowthRate(cellX, cellY, cellZ, out rate)`；
  * Gameplay 實作 `HdbfGrowthRateProvider`（`HdbfGameConfigurator` 綁定 `Bind<IGrowthMultiplierSource>().
    ToExisting<HdbfGrowthRateProvider>()`）；
  * Integration 的 `HarmonyBootstrap` 建構子注入這個 Core 介面（與 UI 用 `IBoxFarmQuery` 相同的跨組件 DI 模式）。
* cell→farm 查詢（Gameplay 內）：`cell → HdbfPlotCellCodec.Encode → plotId → IHdbfFarmRegistry.TryGetPlotOwner
  → IHdbfFarmQueryService.TryGetSource → controller.TryGetPlotGrowthRate(plotId)`。
  * plot 歸屬表由既有掃描/註冊路徑維護（本卡未改註冊邏輯），查詢是單純 dictionary read；
  * 只有 HDBF controller 擁有、且仍註冊的 plot 才會回 true；原版農田、其他模組作物、路過樹木一律 false → 原版生長時間。
* `HdbfBoxFarmController.TryGetPlotGrowthRate(plotId)`（新 partial，`HdbfBoxFarmController.Growth.cs`）是**唯一**
  四層相乘點：`IrrigationMultiplier(_coverage,C_min)`、`FatigueMultiplier(fatigue[index],penalty)`、
  `_maintenanceMultiplier`、`_powerMultiplier` → `BalanceFormulas.GrowthMultiplier` → `GrowthRateMultiplier`。
  補丁本身不重算公式（本卡硬約束）。

### 3.2 為什麼沒有 static 可變狀態（chapter 8.4）

| session 狀態 | 放在哪 |
|---|---|
| provider 參照 / log / 失敗狀態機 / 反射 FieldInfo / 3 次卸載 callback / **全 process 唯一的 Harmony instance** | `HdbfGrowthPatchBridge : MonoBehaviour`（DontDestroyOnLoad 的 **component cache**，instance 欄位；跨 session 重用同一 Harmony 實例） |
| Harmony instance / target / postfix / patch 狀態 | `HdbfHarmonyInstaller`（session singleton 的 instance 欄位） |
| 開關與裝卸載入口 | `HarmonyBootstrap`（Bindito game-context singleton，`ILoadableSingleton`/`IUnloadableSingleton`） |
| patch 本體 | `BoxFarmGrowthPatch` — 只有 static method 與 const 字串；無任何欄位 |
| 補丁白名單 | `HdbfPatchCatalog` — 只有 `const`（不可變） |

patch 每次執行時用 `UnityEngine.Object.FindAnyObjectByType<HdbfGrowthPatchBridge>()` 找 bridge：這是 Unity API
呼叫，不是我們的 static 狀態；頻率是「每株作物開始生長一次」（約每農場每日數十次），不是每幀，也不是每幀掃描。

### 3.3 G=0 的處理與 REQ-IMPL-20 的張力（明示）

需求 ch. 6.3 說 `C < 0.25×C_min` 時「生長完全停止」；但 REQ-IMPL-20 說維護停擺時「只停止播種與收獲，**不得停止作物
生長**」。四層乘積把 `G_maint=0` 帶入會得到 0。為此 Core 定義 `MinimumGrowthRate = 0.05`：補丁最慢只到 20 倍慢
（Wheat 10 天 → 200 天），不永久凍結。真正的「當期不產出」仍由既有派發門禁保證（停擺不播種/不收獲 → 收獲量 0）。

注意實務上的交互：乘數只在作物 **開始生長** 時取樣，而停擺期間本模組不派發播種，所以「以 `G_maint=0` 開新一株」
實際上不會發生；停擺發生在週期中途時也不會回頭改這一株。因此 REQ-IMPL-20「停擺時作物繼續生長」在進行中的作物上
仍然成立，5% 下限主要是乾旱（`G_irr=0`）等少數取樣場景的保險。這是 ADR-009 與 REQ-IMPL-20 之間有意識的折衷，
**留給 Lead 在實機 run 後裁決**（ADR-009 明言「in-game 沒證明變慢就移除」）。

### 3.4 為什麼改 `_fullDelayInDays` 而不是只加 `delayLeft`

只加 `delayLeft`（例如 `IncreaseGrowthProgress(-x)`）會讓 `Progress` 的語意與存檔進度不一致：`GrowthProgress` 在
週期末才跳到 1，`Growable.Save` 存的 progress 會偏小，讀檔 `Growable.Load → FastForwardProgress(saved)` 會再次
把時間加長 → 每次存讀檔越來越慢。把 `_fullDelayInDays` 與 `_delayLeftInDays` 一起縮放後：

* `Progress` = 已成長比例（0→1），週期長度 = `vanilla / rate`；
* 存檔存的是 **有效** 進度 p；讀檔 `FastForwardProgress(p)` 會得到 `remaining = (vanilla/rate)×(1−p)`，與原本一致，不疊加；
* `Growable.Load` 若發生在 `InitializeEntity` 之後，兩者順序都安全（詳見 §8 的順序殘餘風險）。

### 3.5 開關、日誌與自動卸載

* 開關：`features.growthMultiplierPatch`（`HdbfConfig.FeatureFlags`，預設 `true`，也在 `HdbfConfig.Default` 與
  shipped `data/HDBF_Config.json`、`tools/validate/ConfigValidator.FeatureKeys` 中）。
  * **off**：`HarmonyBootstrap.Load()` 讀 `IGrowthMultiplierSource.IsPatchEnabled` 為 false → 直接 return，
    不碰 Harmony、不建立 bridge、不 patch；D-2 行為（派發門禁 + 既有生長通道）完整保留。
* 日誌（全部走既有 `ThrottledLog`，30 s throttle）：
  * 安裝成功 → `HDBF/Compat` key `compat.harmony-patch-installed`，內容含 patch id、Postfix/priority、
    target `Timberborn.Growing.Growable.ResumeGrowing`、`harmonyInstance=hdbf.highdensityboxfarm`；
  * 關閉/Harmony 缺失/簽名改變/衝突 → `HDBF/Warn` key `compat.harmony-patch-unavailable`（一次性，不影響載入）；
  * 例外 → `HDBF/Error` key `compat.harmony-patch-failed`（有 stack、compressed by throttle）；
  * 卸載（正常或 3 次失敗）→ `HDBF/Compat` key `compat.harmony-patch-unloaded`。
* 失敗 3 次自動卸載：`HDBF.Core.Compat.PatchFailureStateMachine`（純狀態機，L1 覆蓋）由 bridge 持有；
  `RecordFailure()` 第 3 次回 true → bridge 標記 quarantined、寫 unloaded log、呼叫 installer `Shutdown()`
  （unpatch + compat count 0）。之後 patch 若仍被呼叫（race）也立即 return。
* 單一 Harmony 實例：instance 由 `HdbfGrowthPatchBridge`（DontDestroyOnLoad）持有，installer 優先重用
  `TryGetHarmony`（REQ-COMPAT-16：整個 process 只有一個 Harmony instance、ID 固定）；只有第一次
  （或 Harmony 型別改變）才 `Activator.CreateInstance`。
* 重複觸發防護：`Apply` 只在 timer 仍等於 blueprint `GrowthTimeInDays` 時縮放一次；同一株若 `ResumeGrowing`
  被呼叫兩次（理論上的同幀事件）也不會把減速平方。
* 兼容性報告：`ICompatRegistry.ReportHarmonyPatchCount(int)` 新增；bootstrap 安裝/卸載時回報 1/0，
  `CompatAdapterRegistry.BuildReport().HarmonyPatchCount` 現在反映實際值（預設 0 不變）。

---

## 4. 改動檔案（file:line）

### Core

| 檔案 | 行 | 內容 |
|---|---|---|
| `src/HDBF.Core/Balance/BalanceFormulas.cs` | `:357-422` | `MinimumGrowthRate=0.05f`（`:369`）、`GrowthMultiplier`（`:381`）、`GrowthRateMultiplier`（`:402`）、`GrowthTimeScale`（`:417`） |
| `src/HDBF.Core/Config/HdbfConfig.cs` | `:70-76` | `FeatureFlags.GrowthMultiplierPatch = true`（`:76`） |
| `src/HDBF.Core/Config/HdbfConfigCodec.cs` | `:66-68` / `:164` | parse / serialize `features.growthMultiplierPatch` |
| `src/HDBF.Core/Compat/PatchFailureStateMachine.cs` | new `:1-81` | 3 次失敗狀態機（`FailureThreshold` `:17`、`RecordFailure` `:53`、`Reset` `:75`） |
| `src/HDBF.Core/Growth/IGrowthMultiplierSource.cs` | new `:1-32` | patch↔Gameplay 的 Core seam |
| `src/HDBF.Core/Compat/ICompatRegistry.cs` | `:44` | `ReportHarmonyPatchCount(int)` |
| `src/HDBF.Core/Logging/HdbfLogKeys.cs` | `:72-100` | 4 個 `compat.harmony-patch-*` keys（`HarmonyPatchInstalled` `:78`、`HarmonyPatchUnavailable` `:85`、`HarmonyPatchFailed` `:92`、`HarmonyPatchUnloaded` `:98`） |
| `src/HDBF.Core/Abstractions/*`、`Save/*` | — | **未改**（沒有新存檔欄位） |

### Gameplay

| 檔案 | 行 | 內容 |
|---|---|---|
| `src/HDBF.Gameplay/Components/HdbfBoxFarmController.Growth.cs` | new `:1-74` | `TryGetPlotGrowthRate`（`:33`），唯一四層相乘點（`:54`） |
| `src/HDBF.Gameplay/Components/HdbfGrowthRateProvider.cs` | new `:1-93` | `IGrowthMultiplierSource` 實作：`IsPatchEnabled` `:45`、`TryGetGrowthRate` `:64`（cell→plot→farm） |
| `src/HDBF.Gameplay/Components/HdbfGameplayContracts.cs` | `:35-41` / `:62-68` | `IHdbfBoxFarmSnapshotSource.TryGetPlotGrowthRate`、`IHdbfFarmRegistry.TryGetPlotOwner` |
| `src/HDBF.Gameplay/Components/IHdbfFarmQueryService.cs` | `:21-26` | `TryGetSource` |
| `src/HDBF.Gameplay/Components/HdbfFarmQueryService.cs` | `:98-119` | 實作 `TryGetSource`（`:103`）、`TryGetPlotOwner`（`:116`） |
| `src/HDBF.Gameplay/HdbfGameConfigurator.cs` | `:48-49` | `Bind<HdbfGrowthRateProvider>().AsSingleton(); Bind<IGrowthMultiplierSource>().ToExisting<...>()`；`:27` 註解更新 |

### Integration（`HDBF.Patches.` + `HDBF.Integration.HarmonyBootstrap`）

| 檔案 | 行 | 內容 |
|---|---|---|
| `src/HDBF.Integration/Harmony/HdbfPatchCatalog.cs` | new `:1-50` | 白名單：單一 Postfix、target/method、Harmony id（`HarmonyInstanceId` `:18`、`ResumeGrowing` `:27`、`GrowthPatchKind="Postfix"` `:33`） |
| `src/HDBF.Integration/Harmony/BoxFarmGrowthPatch.cs` | new `:1-79` | 唯一 patch body `Postfix(Growable, ITimeTrigger)`（`:46`），無 HarmonyLib 型別、外層 try/catch |
| `src/HDBF.Integration/Harmony/HdbfGrowthPatchBridge.cs` | new `:1-232` | MonoBehaviour component cache：Harmony 實例持有（`TryGetHarmony` `:71`、`SetHarmony` `:79`）、`Apply`（`:116`）、失敗處理/自動卸載（`:207`）；快取 `FieldInfo`、無 static |
| `src/HDBF.Integration/Harmony/HdbfHarmonyInstaller.cs` | new `:1-588` | 純反射 Harmony：`TryInstall`（`:85`）、`ValidateTarget`（`:306`）、`HasConflictingPostfix`（`:477`）、Patch/Unpatch/GetPatchInfo/Priority |
| `src/HDBF.Integration/Harmony/HarmonyBootstrap.cs` | new `:1-104` | game-context singleton `ILoadableSingleton/IUnloadableSingleton`：`Load`（`:45`）、`Unload`（`:81`） |
| `src/HDBF.Integration/Harmony/IHdbfPatchSession.cs` | new `:1-22` | 無 HarmonyLib 型別 session 介面（缺 Harmony 時 assembly 仍可載入） |
| `src/HDBF.Integration/HdbfIntegrationConfigurator.cs` | `:33` | `Bind<HarmonyBootstrap>().AsSingleton()` |
| `src/HDBF.Integration/Adapters/CompatAdapterRegistry.cs` | `:48-53`, `:229-243`, `:265` | `_harmonyPatchCount` 欄位（`:53`）、`ReportHarmonyPatchCount`（`:233`）、`BuildReport` 使用實際值（`:265`） |
| `src/HDBF.Integration/HDBF.Integration.csproj` | `:27-34` | 只加 `<Reference Include="Timberborn.Growing">`（`Private=false`）；ProjectReference 仍只有 Core |

### Data / tools / docs / tests

| 檔案 | 行 | 內容 |
|---|---|---|
| `data/HDBF_Config.json` | `:11` | `"growthMultiplierPatch": true` |
| `tools/validate/ConfigValidator.cs` | `:24-27` | 新 feature key（Expected=true），避免 validate 把它當 unknown key |
| `docs/INTERFACES.md` | `:52`, `:147-178`, `:318-343`, `:418-444` | 新 Core 介面/常數、Gameplay 契約、patch/bootstrap 契約 |
| `docs/ARCHITECTURE.md` | ADR-009 末段 | 實作落點補述（patch point、反射 Harmony、開關） |
| `tests/HDBF.Tests/GrowthMultiplierPatchTests.cs` | new `:1-319` | 16 個 L1 tests（§6） |
| `tests/HDBF.Tests/HDBF.Tests.csproj` | `:60-82` | 5 個 patch 源碼以 EmbeddedResource 嵌入（game-free assembly 不能引用 Integration） |
| 本報告 | new | 證據與待驗證步驟 |

### 未改（本卡約束）

`docs/VERIFICATION.md`、`docs/REQ_TRACEABILITY.md`、平衡數值（`TierCatalog`、`data/Buildings/**`、
`HDBF_Config.json` 的數值欄位）、灌溉取樣、託管格註冊/掃描、UI、5 個兼容適配器邏輯、`build.ps1`、
`Mods\HDBF` 部署資料夾、遊戲本體、任何存檔。**沒有新增第二個 ProjectReference、沒有新增 static 可變狀態、
沒有 Prefix/Finalizer/Transpiler、補丁總數 = 1。**
唯一新增的 game assembly 編譯引用是 `Timberborn.Growing`（Integration，`Private=false`）。

---

## 5. 為什麼 Harmony 用反射（不引用 HarmonyLib）

* REQ-COMPAT-02/05 + ADR-002：`HDBF.Integration` 是唯一可碰第三方模組字串的組件，但**不得有第三方 assembly
  引用**；既有 `ExternalProxy` 已是這個模式。Harmony 是另一個 mod（Steam Workshop `3284904751`，`0Harmony.dll` 2.4.1）。
* 更關鍵的是 **缺 Harmony 時不能弄壞整個遊戲**：Bindito 在建立容器時對每個已載入 mod assembly 呼叫
  `Assembly.GetTypes()`（`Bindito.Core.Internal.ContainerDefinition+<>c::<FindConfigurators>b__10_0` IL：
  `ldarg.1; callvirt Assembly::GetTypes`）。若 `HDBF.Integration` 裡有型別帶 `[HarmonyPatch]`（或任何 HarmonyLib
  基底/介面），而玩家沒裝 Harmony，`GetTypes()` 會拋 `ReflectionTypeLoadException` → 整個 DI 容器建立失敗。
  所以 patch 類別**沒有任何 HarmonyLib 型別**，安裝器以字串/反射操作 `HarmonyLib.Harmony`、`HarmonyMethod`、
  `Priority`、`Patch/Unpatch/GetPatchInfo`。缺 Harmony 時只記一條 `HDBF/Warn`，其餘功能照常。
* 編譯期不引用 0Harmony → 部署包不需要夾帶 `0Harmony.dll`（避免與玩家的 Harmony mod 產生重複 assembly）。

---

## 6. 測試（L1，game-free）

`tests/HDBF.Tests/GrowthMultiplierPatchTests.cs` — 16 tests：

* **四層乘算**（`:29`）：`0.5×0.4×0.5×0.6 = 0.06`；任一層 0 → 0（含 maintenance shutdown，`:45`）；
  clamp >1 與 NaN/負數（`:53`）；
* **速率/時間映射**（`:64`,`:81`,`:92`）：`G=0→0.05`、`0.6→1/0.6`、上限 20、`G>1` 不會加速；
  健康農場 `G=1 → scale=1`（補丁 no-op）；
* **失敗狀態機**（`:106`,`:123`,`:137`）：第 1/2 次不卸載、第 3 次回 true 且 `IsUnloaded`、第 4 次不再要求、
  count 上限 3、`Reset` 重新武裝、threshold<=0 clamp 1；
* **開關**（`:151`,`:158`,`:172`）：`FeatureFlags`/`HdbfConfig.Default` 預設 true、parse false/true、
  缺 key 保持 true、serialize round-trip；
* **shipped source contract**（`:203`-`:276`，patch 源碼以 EmbeddedResource 嵌入）：
  `namespace HDBF.Patches`、唯一 `GrowthPatchKind="Postfix"`、唯一 target `Growable.ResumeGrowing`、
  **`Transpiler` 字樣 0 次**、`public static void Postfix` 恰好 1 個、
  Harmony id 字面值恰好 1 次（只有 catalog 宣告）、沒有 `new Harmony(`、
  **欄位注入名 pin 在 `ITimeTrigger ____timeTrigger`**（Harmony 2.4.1 只剝 3 個底線，欄位本身叫 `_timeTrigger`；
  這是離線檢出並修正的一個真實 bug，見 §2）、bootstrap 讀 `IsPatchEnabled`、
  bridge 使用 `PatchFailureStateMachine` 與 `TryGetGrowthRate`。

實機才能驗的部分（本卡不聲稱）：patch body 真的在遊戲中被呼叫、timer 改寫真的讓作物變慢、
`FindAnyObjectByType`/`AddComponent` 在 Unity Mono runtime 正常、原版農田無回歸。

---

## 7. 建置與測試輸出（本機真實輸出）

指令（work root 在 workspace 外，避免 bin/obj 進工作區）：

```powershell
$w = Join-Path $env:TEMP 'hdbf-card-work'
dotnet build src\HDBF.Core\HDBF.Core.csproj                 -c Release -m:1 -p:HdbfWorkRoot=$w
dotnet build src\HDBF.Integration\HDBF.Integration.csproj   -c Release -m:1 -p:HdbfWorkRoot=$w
dotnet build src\HDBF.Gameplay\HDBF.Gameplay.csproj         -c Release -m:1 -p:HdbfWorkRoot=$w
dotnet build src\HDBF.UI\HDBF.UI.csproj                     -c Release -m:1 -p:HdbfWorkRoot=$w
dotnet build tests\HDBF.Tests\HDBF.Tests.csproj            -c Release -m:1 -p:HdbfWorkRoot=$w
dotnet build tools\validate\validate.csproj                 -c Release -m:1 -p:HdbfWorkRoot=$w
dotnet "$w\bin\HDBF.Tests\Release\net9.0\HDBF.Tests.dll"
```

實測（2026-10-25T13:05:01–13:05:11 +08:00，含 §2.1–§3 的所有修正後重跑）：

```
src\HDBF.Core\HDBF.Core.csproj                  -> 0 个警告 | 0 个错误
src\HDBF.Integration\HDBF.Integration.csproj    -> 0 个警告 | 0 个错误
src\HDBF.Gameplay\HDBF.Gameplay.csproj          -> 0 个警告 | 0 个错误
src\HDBF.UI\HDBF.UI.csproj                      -> 0 个警告 | 0 个错误
tests\HDBF.Tests\HDBF.Tests.csproj              -> 0 个警告 | 0 个错误
tools\validate\validate.csproj                  -> 0 个警告 | 0 个错误

HDBF L1 test run - 2026-10-25T05:05:10.9439057Z
PASSED 358  FAILED 0
```

（`tools/validate` 只編譯、未執行，release gate 由 Lead 跑；`build.ps1`、部署、遊戲啟動本卡皆未執行。）
工作區內沒有 `bin/`、`obj/`（`Get-ChildItem -Recurse -Directory -Include bin,obj` → none）。

驗收計數（靜態 grep，可重跑）：

```
patch sources Transpiler occurrences: 0
Postfix patch-kind declarations:      1
Harmony instance id literals:         1
Postfix bodies:                       1
```

---

## 8. 待實機驗證步驟（Lead；本卡不聲稱已驗證）

前置：把 Harmony mod 放進 Mods（Steam Workshop 內容
`C:\Program Files (x86)\Steam\steamapps\workshop\content\1062090\3284904751` 內含 `manifest.json` + `0Harmony.dll`，
複製為 `Documents\Timberborn\Mods\Harmony`）；`build.ps1 -Install` 部署 HDBF（Lead 的 release gate）。

1. **補丁有被安裝**：啟動 fixture（`-skipModManager -saveName ...`），在 `Player.log` 找
   `[HDBF/Compat] compat.harmony-patch-installed ... target=Timberborn.Growing.Growable.ResumeGrowing
   harmonyInstance=hdbf.highdensityboxfarm`；不得出現 `compat.harmony-patch-unavailable` / `compat.harmony-patch-failed`。
   `Player.log` 的 mod 清單應同時有 Harmony (v2.4.1) 與 HDBF。
2. **作物真的變慢（每週期取樣）**：用既有 fixture 種 Wheat（原版 10 天），以遊戲內 `>>>` 加速到成熟，比對
   `diag`/`gameplay.farm` 的種植→`harvested plot ... crop Wheat` 間隔；也可看實體面板 GrowthProgress 條填滿所需天數。預期：
   * Normal + 全灌溉 + 不疲勞 + 有電（基礎/中級/高級）：≈10 天（補丁 no-op；先驗這個排除「全部都變慢」的 regression）；
   * Top 離網（`G_power=0.6`）：≈16–17 天；
   * 中級以上維護逾期 1 週期（`G_maint=0.5`，材料不足可達）：≈20 天；
   * 高疲勞（F→1，`G_fat=0.4`）：≈25 天；
   * 四層同時變差：週期 ≈ `10 / 乘積`；乘積 0（停擺/嚴重缺水）→ 走 5% 下限，約 200 天且停擺時無收獲（輸出 0）。
   注意：乘數只在 **新一株** 開始生長時取樣；同一株週期進行中改電力/維護，要等下一株才反映（§2.2）。
3. **存讀檔一致（不疊加）**：在上面任一個變慢場景中途存檔 → 讀檔，確認同一株的剩餘時間/GrowthProgress 與存檔前連續，
   且第二次讀檔不會再變更慢（本卡改 `_fullDelayInDays` 的目的，§3.4）。若讀檔後該株恢復原版速度，記錄該株在讀檔前是否已
   progress>0（載入順序殘餘風險，§9）。
4. **原版農田/其他作物不受影響**：在同一張圖建原版 `FarmHouse`（或原本就有的野地作物），在 **HDBF 托管格以外** 種植，
   量測成熟時間應仍是原版值；其他 mod 作物、裝飾樹木（同為 `Growable`）也不得變慢。另可放一株原版農舍作物在
   HDBF 27×27 範圍內、但不在已註冊托管格上 → 仍應原版速度（cell→farm lookup 只認註冊格）。
5. **關閉開關回到 D-2**：把 `HDBF_Config.json` 的 `features.growthMultiplierPatch` 改 `false`，重新部署啟動；
   預期只有 `[HDBF/Compat] ... features.growthMultiplierPatch=false; no Harmony patch is installed ...`，
   沒有 `harmony-patch-installed`、沒有 Harmony patch 例外；作物回到原版速度，密植加速等 D-2 行為照舊。
6. **沒有 Harmony 時安全降級**：移除 Mods\Harmony 再啟動；預期 `[HDBF/Warn] ... Harmony (HarmonyLib.Harmony)
   is not loaded ... stays off and pre-patch D-2 behaviour is used`，HDBF 正常建造/運作，**不得**有 assembly load /
   ReflectionTypeLoadException / DI 容器錯誤（§5 的設計目的）。
7. **卸載/重載**：從遊戲內回主選單再讀檔；補丁應重新安裝（每次 session 一條 installed），不得出現同一方法被 patch 兩次
   （`harmonyPatchCount` 應為 1；若之後有 report 匯出，看 `harmonyPatchCount: 1`）。
8. **失敗自動卸載（只能間接驗）**：3 次失敗的自動卸載由 L1 狀態機保證；runtime 路徑沒有可安全製造 fault 的遊戲內方法，
   本卡不聲稱在實機驗過。若要演練，只能另建一個「故意簽名不符」的 dev build 驗證 `ValidateTarget` 跳過 + warning。
9. **裁決點（ADR-009）**：實機確認「托管格作物變慢 + 原版作物無回歸」後補丁才可保留；若 REQ-IMPL-20（停擺時作物仍生長）
   或玩家體感不可接受，把開關預設改 false 或移除本卡 patch（程式路徑已可整段卸載）。

---

## 9. 殘餘風險 / 尚未證明

1. **patch body 未在遊戲中執行過**：`FindAnyObjectByType` 找 bridge、`AddComponent<HdbfGrowthPatchBridge>`（runtime
   Assembly.Load 的 MonoBehaviour）、Harmony 反射 patch private method、`____timeTrigger`/private 欄位注入、
   internal `TimeTrigger` 的 FieldInfo 讀寫——全部在離線層面「可編譯、簽名校驗通過」，但只有實機能證明。
2. **取樣時機近似**：乘數在作物開始生長時取樣；週期中途電力/維護/覆蓋率變動不追溯（下一株才套）。這是刻意的近似
   （§2.2），若實機要求更精確，需要 per-trigger 狀態或改 patch 點，屬後續卡。
3. **讀檔順序**：若 `Growable.Load`（套用存檔 progress）發生在 `InitializeEntity`（patch 時機）之前，該株在該週期
   保持原版剩餘時間（不會變慢、也不會壞）；下一株恢復。若發生在之後，`Progress`/剩餘時間仍正確（§3.4）。兩種順序都安全，
   但「已種到一半的存檔作物在讀檔當下是否變慢」需要在實機 step 3 確認。
4. **`G_maint=0` 的 5% 下限**（§3.3）：與 REQ-IMPL-20 的張力已明示；實務上停擺期間不播種、乘數只在開始生長時取樣，
   所以進行中的作物仍照原速長完，需在實機確認這一點；Lead 實機後裁決是否需要更精確的處置。
5. **Dense 模式互動**：既有密植通道是「每日加固定 progress」`(G_mode−1)/GrowthTimeInDays`，本卡未改它。補丁把 timer 拉長後，
   密植仍每日加 `0.35/T` 的絕對進度，因此 combined 週期 ≈ `T/(G+0.35)`，而非嚴格 `T/(G×1.35)`。此偏差在四層接近 1 時可忽略，
   在 `G` 很小時密植會部分抵消減速。屬未在 ADR-009 範圍內處理的已知近似，建議後續卡把密植增量乘上 `G`（需回歸 D-2 行為）。
6. **`FindAnyObjectByType` 成本**：每次作物開始生長呼叫一次（非每幀）；大量樹木/作物同時生成（map load、大規模收種）
   時是 O(1) 型 Unity 查詢的重複，理論上可觀。實機可看 frame spike；必要時把 bridge 改為更便宜的取得方式（但不可加 static state，
   需重新設計）。
7. **private method 的穩定性**：`Growable.ResumeGrowing` 是 private；遊戲更新若改名/改簽名，白名單校驗會跳過並記 warning
   （D-2 行為），不會崩潰；需要時重新 IL 取證。
8. **Harmony 缺失/版本**：設計上缺 Harmony 只降級；但「Harmony 已載入但版本不相容 API」的行為未實機驗（`GetPatchInfo`/`Patch`
   反射方法名以 2.4.1 的 metadata 為準）。
9. 本卡未跑 `build.ps1`、未部署、未啟動遊戲；`tools/validate` 只編譯。任何「已驗證」的說法都不成立——此卡只有
   「編譯 + L1」與「IL 事實」。

---

## 10. 本卡需求對映

| 要求 | 本卡狀態 |
|---|---|
| REQ-CORE-03 / REQ-BAL-02 / REQ-BAL-28：四層真正影響生長 | 實作：Core `GrowthMultiplier` 單一乘算點 + 唯一 Postfix 把乘積映射成 timer 時間；L1 驗證乘積/0/下限；**實機變慢未證** |
| ADR-009：最多 1 Postfix、單一 instance id、純 Postfix、禁 Transpiler、try/catch、3 次卸載、開關、只托管格 | 全部實作並以 source contract/L1 固定；runtime 待實機 |
| REQ-COMPAT-16/17/18 | 白名單+簽名校驗、衝突檢查、反射 Harmony、單一 id、try/catch、狀態機卸載、日誌齊備 |
| 需求 ch. 8.4：補丁類不可持 static 可變狀態 | 全 session 狀態在 bridge component/bootstrap/injector instance；patch 本體零欄位 |
| 需求 ch. 10.4 相容性報告 | `harmonyPatchCount` 由 bootstrap 回報（安裝 1 / 卸載 0） |
| 本卡：不新增 Integration→Gameplay 引用 | 守住：只有 1 個 ProjectReference；跨組件只經 Core 介面（DI） |
| 本卡：不改灌溉取樣/註冊/UI/兼容適配器 | 守住（見 §4「未改」）；唯一相關的 Gameplay 新檔是唯讀查詢 partial 與 provider |
| 本卡：不要寫「已驗證」 | §8/§9 全部標示待實機；本卡無任何實機聲稱 |
