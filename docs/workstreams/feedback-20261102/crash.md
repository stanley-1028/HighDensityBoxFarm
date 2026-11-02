# Feedback 2026-11-02｜致命崩潰（數分鐘後）與暫停崩潰：調查報告

| 項目 | 值 |
|---|---|
| 共享任務 | task-2（Feed-Bug-02） |
| 調查員 | crash |
| 版本前提 | HDBF v2.5.3（`data/manifest.json` Version=2.5.3）、Timberborn **1.1.2.8**（`docs/in-game/pause-fix2-A-Player.log` 開頭 `Starting game version 1.1.2.8-ffc2de0-sw`） |
| 可寫範圍 | 本檔（`docs/workstreams/feedback-20261102/crash.md`）；其餘全部唯讀 |
| 方法 | 唯讀 IL dump（`tools/ApiIndex`，對 `C:\Program Files (x86)\Steam\steamapps\common\Timberborn\Timberborn_Data\Managed`），唯讀掃描本機工作坊 DLL，重讀 `docs/in-game/**` 既有實機日誌，重讀 `.tmp-pause/pause-rootcause.md`、`docs/VERIFICATION.md` §34（皆未修改）。**未啟動遊戲、未執行 `build.ps1`、未改任何程式碼。** |
| 證據可信度標記 | **【已證實】**＝本機 1.1.2.8 IL／本專案日誌可直接驗證；**【高度可能】**＝多條間接證據指向、但缺玩家端日誌；**【推測】**＝尚未有直接證據，只能列為待驗假設 |

---

## 0. 結論摘要（先看這段）

1. **F-CRASH-01（致命 NRE）不是 HDBF 補丁造成的，也不是 T3MP 造成的。**
   `WorkerRootBehavior.Decide_Patch1` 是 **Harmony/MonoMod 生成的替換方法**，代表有第三方 Harmony mod 補丁了 `Timberborn.WorkSystem.WorkerRootBehavior.Decide`（同一個 mod 也補丁了 `BehaviorManager.Tick`）。本專案 v2 沒有任何 Harmony 補丁（README/CHANGELOG/原始碼/DLL 字串皆可證）；本機 T3MP 1.2.5 的補丁清單（IL 取證）只有 tick bucket／frontier 與 `TubeSystem.UpdateVisit`，**不含** `WorkerRootBehavior`／`BehaviorManager`。本機 71 個已啟用工作坊 mod 的所有 DLL 以 ASCII 掃描都找不到 `WorkerRootBehavior` 字串。**【已證實：HDBF、T3MP 不是 patcher；patch 來源未確定，需玩家 mod 清單／Player.log】**

2. **HDBF 只提供「狀態」，不提供「補丁」。** HDBF 讓農民在值勤時長時間待在建築內（`WaitInsideIdlyWorkplaceBehavior`，`HdbfGameConfigurator.cs:113`），而暫停時原版只解除僱用、不把室內工人帶出（`PausableBuilding.Pause → BlockableObject.Block → Workplace.OnObjectBlocked → UnassignAllWorkers → Worker.Unemploy`）。這種「已解僱／無 Workplace、但仍留在 Enterable 內」的 worker 狀態在原版單執行緒邏輯裡很罕見。第三方 Decide 補丁一旦假設「`Employed` ⇒ `Workplace != null`」或「農民不會在室內被解僱」，就會在這個狀態上 NRE。**【高度可能：H1，HDBF 是觸發因子而非 bug 擁有者】**

3. **HDBF 端不能「修掉」這個 NRE**（0 Harmony 是硬約束，也不該去補丁原版 `Decide`）。可做的是：**收斂 HDBF 自己製造的怪狀態**，讓第三方補丁更難踩到，並補上可判讀的日誌；另外建議在玩家端做 mod 隔離以指認 patcher。**【建議修復 D1–D4，見 §1.7】**

4. **F-CRASH-02（暫停崩潰）在 2.5.3 之後仍有殘留路徑，而且本專案自己的實機日誌已重現其中一條。**
   `docs/in-game/pause-fix3-A2-Player.log`：暫停存檔載入時，`indoor.paused`（2931 行）→ `indoor.entity active`（2932 行）→ `Load time`（2955 行）→ **`moved 2 beaver(s)` 出現在 2958 行、緊接 2959 行 `indoor.resumed`**。也就是：**載入完成前 HDBF 的撤出看到 0 名室內工人，兩名工人整段「暫停載入期間」都被留在被封鎖的農場裡，直到玩家恢復才被撤出**。這是 2.5.3 修復的**時序缺口（R1，已證實存在）**，正是 2.5.2 以前暫停崩潰所描述的高風險狀態。**【已證實：殘留狀態存在；是否為玩家 crash 的直接成因＝高度可能/待證】**

5. **「管道 No Connection」與兩個 crash 判為不同源（高度可能）。** 管道探測只走 `ReadPipeWater`（`Context.cs:77-107`，activate／日 tick／post-load／面板）＋ TimberPipes 反射（全程 try/catch 降級），不觸碰 Worker／Workplace／Enterer，也不在 beaver tick 路徑上；面板字串 `HDBF.Port.NotConnected` 只出現在 `HdbfIndoorPanelFactory.Cards.Output.cs:106/140`。**但有一個共用觸發面**：HDBF 只在「啟動／PostLoad／每個遊戲日 tick」重探管道，面板其餘時間只重用手上快取的 `_hasPipeWaterCached`（`WaterSupply.cs:218-245` **不會**重新探測）；**農場一旦暫停，日 tick 停止 → 接上管道後仍可能永久顯示 Not connected 直到恢復/重載**。這解釋「有接管道卻顯示 No Connection」，與 crash 無因果。管道細節交由管道調查員。

---

## 1. F-CRASH-01｜「After a few minutes of having a working Box Farm I get a fatal crash」

### 1.1 現象（玩家回報 + 錯誤日誌）

- 玩家：Annastasya（2026-11-02，v2.5.3 發布之後）。
- 原文：「After a few minutes of having a working Box Farm I get a fatal crash. Also, having pipes connected to the Box Farm, it still shows "No Connection".」
- 錯誤日誌：<https://pastebin.com/SBPK5AJQ>（本次已抓 raw 全文，逐行如下；`Rethrow as Exception` 以下為原版 `TickableEntity.Tick` 的包裝）：

```text
NullReferenceException: Object reference not set to an instance of an object
(wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition.Timberborn.WorkSystem.WorkerRootBehavior.Decide_Patch1(Timberborn.WorkSystem.WorkerRootBehavior,Timberborn.BehaviorSystem.BehaviorAgent)
(wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition.Glue:AbiFixup<Timberborn.BehaviorSystem.Decision Timberborn.WorkSystem.WorkerRootBehavior:Decide(Timberborn.BehaviorSystem.BehaviorAgent),Timberborn.BehaviorSystem.Decision Timberborn.WorkSystem.WorkerRootBehavior.Decide_Patch1(Timberborn.WorkSystem.WorkerRootBehavior, Timberborn.BehaviorSystem.BehaviorAgent)>(Timberborn.WorkSystem.WorkerRootBehavior,Timberborn.BehaviorSystem.Decision&,Timberborn.BehaviorSystem.BehaviorAgent)
Timberborn.BehaviorSystem.BehaviorManager.ProcessBehavior (Timberborn.BehaviorSystem.Behavior behavior)
Timberborn.BehaviorSystem.BehaviorManager.ProcessBehaviors ()
(wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition.Timberborn.BehaviorSystem.BehaviorManager.Tick_Patch1(Timberborn.BehaviorSystem.BehaviorManager)
Timberborn.TickSystem.MeteredTickableComponent.Tick ()
Timberborn.TickSystem.TickableEntity.TickTickableComponents ()
Timberborn.TickSystem.TickableEntity.Tick ()
Rethrow as Exception: Exception thrown while ticking entity b66554ae-0079-44de-a57b-af206e96d1f8 'BeaverAdult Zelaji'
Timberborn.TickSystem.TickableEntity.Tick ()
T3MP.Runtime.TickFrontier.TickAllFast (Timberborn.TickSystem.TickableEntityBucket bucket)
(wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition.Timberborn.TickSystem.TickableEntityBucket.TickAll_Patch1(Timberborn.TickSystem.TickableEntityBucket)
(wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition.Timberborn.TickSystem.TickableBucketService.TickNextBucket_Patch1(Timberborn.TickSystem.TickableBucketService)
(wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition.Timberborn.TickSystem.TickableBucketService.TickBuckets_Patch1(Timberborn.TickSystem.TickableBucketService,int)
Timberborn.TickSystem.Ticker.Update (System.Single deltaTimeInSeconds)
Timberborn.TickSystem.TickerUnityAdapter.Update ()
```

### 1.2 精確判讀（每一層是什麼）

| 堆疊層 | 判讀 | 依據 |
|---|---|---|
| `...WorkerRootBehavior.Decide_Patch1(...)`＋`Glue:AbiFixup<原始簽名, patch 簽名>` | `Decide` **被一個 Harmony 補丁取代**。`_Patch1` 是 Harmony 2.x（HarmonyX）以 `MonoMod.Utils.DynamicMethodDefinition` 產生的替換方法命名；`Glue:AbiFixup` 是 MonoMod 的 ABI 轉接層（把 patch 方法的簽名橋接到原方法）。 | 堆疊本身＋本機工作坊 `3284904751` = **Harmony 2.4.1**（`pause-fix2-A-Player.log` 的 mod 清單第 55 行） |
| `BehaviorManager.Tick_Patch1` | `BehaviorManager.Tick` **也被補丁**（同一或另一個 Harmony mod）。 | 同上 |
| `T3MP.Runtime.TickFrontier.TickAllFast`、`TickableEntityBucket.TickAll_Patch1`、`TickableBucketService.TickNextBucket_Patch1`、`TickBuckets_Patch1` | **T3MP**（More More More Performance! 1.2.5）的 tick frontier 最佳化。 | `pause-fix2-A-Player.log:123-150`：`[T3MP] Loaded. Version=1.2.5`、`[T3MP] Tick frontier installed.`、`[T3MP] Mono inline limit: limit at rva 0x744C60 -> 300`；本機 T3MP `Code.dll` IL 取證（§1.3） |
| `Exception thrown while ticking entity b66554ae-... 'BeaverAdult Zelaji'` | 原版 `TickableEntity.Tick` 把「tick 這個實體時發生的例外」包成 rethrow；**被 tick 的是一隻成年海狸，不是建築**。 | 堆疊 |

**關鍵限制**：NRE 發生在 `Decide_Patch1` 的**方法體內**，堆疊中**沒有** `DecideAsWorker`、`WorkAtWorkplace`、`_communityServiceBehavior.Decide` 等原版內層 frame（見 §1.4 的 frame 缺席推論）。這代表 NRE 不是「原版 Decide 呼叫到的某個方法拋錯」，而是：patch 自己新增的 code、或 `Decide` 本體最外層的直接解參考（含可能被 JIT inline 的小方法）。

### 1.3 證據鏈 A：誰 patch 了 `WorkerRootBehavior.Decide`？（**已證實不是 HDBF、不是 T3MP；來源未確定**）

#### A-1 HDBF v2 沒有任何 Harmony 補丁【已證實】

- 文件：`README.md:8`「Harmony 補丁**已全部刪除（0 Harmony）**」；`docs/v2/CHANGELOG.md` [2.5.3] 段（第 8 行起）與 `git show 6c6d64a` 皆宣稱 0 Harmony；`docs/v2/TRACEABILITY.md:150` 記載 `src/HDBF.Integration/Harmony/**` 已刪除。
- 原始碼：對 `src/` 全文搜尋 `Harmony`／`WorkerRootBehavior`／`MonoMod` 無命中（本模組只透過 `ExternalProxy` 反射第三方 API）。
- 已發布 DLL：`steamapps\workshop\content\1062090\3828809784\HDBF.Gameplay.dll` 的 ASCII 掃描：`WorkerRootBehavior` **不存在**、`Harmony` **不存在**；只有 `Decide`／`WorkplaceBehavior`（＝`IndoorFarmWorkplaceBehavior.Decide`，正常）。
- 結論：`Decide_Patch1` 不可能是 HDBF 產生。**【已證實】**

#### A-2 T3MP 1.2.5 的補丁清單不含 `Decide`／`BehaviorManager`【已證實，本機版本】

以 `tools/ApiIndex` 對 `steamapps\workshop\content\1062090\3756656421\Code.dll` 做型別索引與 IL dump（產物 `%TEMP%\hdbf-il\t3mp-index.txt`、`t3mp-runtime-il.txt`）：

- T3MP 的 runtime 型別只有：`RuntimePatches`、`TickFrontier`、`TubeVisitFix`、`EventBusFastDelegates`、`MonoInlineLimit`、`MovementSubsteps`、`WalkerSpeedDelegates`、`RequestedSpeedPolicy`、`TerrainNeighborVisits`、`WaterTextureUpload`、`YielderReachabilitySkip`、`AllowedGoodRows`、`LoadPatches`、`LoadPatchBridge`。
- `RuntimePatches.Install/Apply` 的 `ldstr` 目標只有：
  `"TickAll"`（→ `TickAllFast`）、`"UpdateVisit"`（→ `AfterUpdateVisit`）、
  `"Add"/"Remove"`（→ `AfterAdd`/`AfterRemove`）、
  `"EnableComponent"/"DisableComponent"`（→ `BeforeEnable`/`FinalizeEnable`/`FinalizeDisable`）、
  `"OnDestroy"`（→ `BeforeDestroy`）、
  `"HarmonyLib.Harmony"`、`"HarmonyLib.HarmonyMethod"`、
  harmony id `"t3mp.runtime.frontier"`／`"t3mp.runtime.tube-visit"`。
- `Code.dll` 的 ASCII 內容**沒有** `WorkerRootBehavior`、`Decide`、`BehaviorManager` 字串。
- 結論：T3MP 只補丁 **tick bucket／frontier**（`TickableEntityBucket.TickAll`、`TickableBucketService.TickNextBucket/TickBuckets` 等）與 `TubeSystem.UpdateVisit`；堆疊中的 `TickAll_Patch1`/`TickNextBucket_Patch1`/`TickBuckets_Patch1` 是 T3MP，但 `Decide_Patch1`、`BehaviorManager.Tick_Patch1` **不是**。**【已證實】**

#### A-3 本機 71 個已啟用 mod 都不 patch `Decide`【已證實，本機 mod 集】

- `pause-fix2-A-Player.log:47-117` 是本機完整 mod 清單（Harmony 2.4.1、T3MP 1.2.5、Second Shift、Efficient Workplaces、Workplace Priority Manager、XtraPlzNoCrashes、ModdableTimberborn、ChooChoo、More Mines…共 71 個）。
- 對 `steamapps\workshop\content\1062090\**\*.dll` 全部做 `findstr /S /M /C:"WorkerRootBehavior"`：**零命中**（Managed 下的 `Timberborn.WorkSystem.dll`／`Timberborn.BeaverBehavior.dll`／`Timberborn.BotBehavior.dll` 有命中，代表掃描方式有效）。
- 逐一檢視最可能補丁 worker 行為的 mod 的 patch 類別（`ApiIndex dump`＋字串比對）：
  - **Second Shift 1.1.1.0**：`SecondShift.Patching.{WorkplacePatcher, WorkerWorkingHoursPatcher, DwellerPatcher, HoursToDawnPatcher, WalkerPoweredGeneratorPatcher, DwellerHomeAssignerPatcher, SecondShiftPatcher}`——補丁對象是 `Workplace`／`WorkerWorkingHours` 等，**不是 `WorkerRootBehavior.Decide`**（DLL 內也沒有該字串）。
  - **Efficient Workplaces 1.1.0.0**、**Workplace Priority Manager 1.1.1**、**XtraPlzNoCrashes 11.2.0**、**ChooChoo 1.4.2**、**More Mines 1.1.20**：皆無 `WorkerRootBehavior` 字串；ChooChoo 有 `TravellerRootBehaviorPatch`（乘客行為，非 worker）、`MovePassengersBehavior`（自己的 WorkplaceBehavior）；More Mines 引用 `BehaviorManager`/`WaitInsideIdlyWorkplaceBehavior` 只是欄位/型別引用，沒有補丁目標。
- 結論：**本機這套 mod 不會產生這兩個 `_Patch1`**。玩家的安裝裡必定還有一個「不在本機測試清單」的 Harmony mod，或某個 mod 的版本與本機不同（本機工作坊有多個 `version-*` 子目錄，實際載入版本以玩家 log 為準）。**【已證實範圍：本機；來源未確定】**

> **誠實標註**：無法從本機檔案指認 patcher。要指認，必須拿到玩家的 `Player.log`（開頭有完整 mod 清單與版本）或請玩家做 mod 二分。**不要**在文件中把這個 NRE 硬歸給 T3MP 或任何本機 mod——現有證據明確排除本機這兩個候選。

### 1.4 證據鏈 B：原版 `WorkerRootBehavior.Decide` 的解參考（1.1.2.8 IL，**已證實**）

以 ApiIndex 對安裝版 Managed 反編譯（產物 `%TEMP%\hdbf-il\workerroot-il.txt`；同一份 IL 亦由本專案舊調查 `.tmp-pause/` 佐證）：

```text
=== Timberborn.WorkSystem.WorkerRootBehavior::Awake
  GetComponent<Worker>()            -> _worker
  GetComponent<WorkerWorkingHours>() -> _workerWorkingHours
  GetComponent<WorkRefuser>()       -> _workRefuser
  GetComponent<BehaviorAgent>()     -> _behaviorAgent
  GetComponent<CommunityServiceBehavior>() -> _communityServiceBehavior

=== WorkerRootBehavior::Decide(agent)
  _workRefuser.RefusesWork()                    // NRE if _workRefuser null
  _worker.Employed                              // NRE if _worker null
  _workerWorkingHours.AreWorkingHours()         // NRE if _workerWorkingHours null
  -> DecideAsWorker()
  _communityServiceBehavior.Decide(agent)       // NRE if _communityServiceBehavior null

=== WorkerRootBehavior::DecideAsWorker
  _worker.get_Workplace().get_Overstaffed()     // NRE if Worker.Workplace == null
  (Overstaffed) -> _worker.Unemploy(); ReleaseNextTick()
  else WorkAtWorkplace()

=== WorkerRootBehavior::WorkAtWorkplace
  _worker.get_Workplace().get_WorkplaceBehaviors()   // NRE if Workplace == null
  foreach behavior: behavior.Decide(_behaviorAgent)
     ReleaseNow -> continue; 其他 -> Decision.TransferNow(behavior, decision)
```

`Worker.get_Employed()`（IL）：`Enabled && (Workplace != null)`。
`Worker.Unemploy()`（IL）：若 `Workplace != null` → `set_Workplace(null)` → `oldWorkplace.UnassignWorker(this)` → `GotUnemployed`／`RelationsChanged`。
`WorkerWorkingHours.get_AreWorkingHours()`／`WorkRefuser.get_RefusesWork()` 都是單純欄位讀取（不會在讀取途中改狀態）。

#### frame 缺席推論（**高度可能，非絕對**）

- 堆疊只有 `Decide_Patch1`，沒有 `DecideAsWorker`／`WorkAtWorkplace` frame。
- `WorkAtWorkplace` 內含 `foreach` 產生的 try/finally（IL 有 EH），Mono 不會 inline 它 → 若 NRE 在 `WorkAtWorkplace` 的 `_worker.Workplace` 解參考，堆疊應出現該 frame。**沒有** → 可排除「`Workplace == null` 在 WorkAtWorkplace 被解參考」。
- `DecideAsWorker` 沒有 EH、方法極小；且 **T3MP 把 Mono inline limit 提高到 300**（`pause-fix2-A-Player.log:150`）→ 它很可能被 inline 進 `Decide`。因此 NRE 落在被 inline 的 `_worker.Workplace.get_Overstaffed()` 也是可能的。
- 但**原版邏輯下**「`Employed == true` 且 `Workplace == null`」不可能同一個執行緒瞬間出現（`Employed` 就是 `Workplace != null`，中間沒有任何呼叫能改它）——除非：
  - **`Worker.get_Employed` 本身也被第三方補丁改寫**（玩家堆疊沒有顯示，但 Harmony 補丁不會在堆疊中具名）；
  - 或 NRE 其實在 **patch 自己加的 prefix/postfix/transpiled code**（最可能）；
  - 或 `_worker`／`_communityServiceBehavior`／`_workRefuser`／`_workerWorkingHours` 是**已銷毀的 MonoBehaviour（fake-null）**——例如實體在 tick 中途被刪除、而 T3MP 的 frontier 仍把它排進 tick（T3MP 的 `TickFrontier` 自己維護 tick 清單並 hook `Add/Remove/EnableComponent/DisableComponent/OnDestroy`，這是「舊實體多 tick 一次」這類風險的來源；**推測**，未證實）。
- 因此：**NRE 的第一候選是「第三方 Decide 補丁的程式碼」；第二候選是「beaver 的 component 在 tick 時已是 fake-null」；vanilla Decide 在正常狀態下不會自己 NRE。**

### 1.5 證據鏈 C：HDBF 的工人生命週期（HDBF 製造了什麼狀態）

#### C-1 HDBF 不碰 `Worker`／`Workplace` 的僱用關係【已證實】

- `IndoorFarmWorkplaceBehavior.Decide()`（`src/HDBF.Gameplay/Farming/IndoorFarmWorkplaceBehavior.cs:76-91`）：永遠 `Decision.ReleaseNow()`，不派工、不改僱用、不導航。
- 整個 `src/` 沒有 `Unemploy`／`EmployAt`／`Worker.Workplace`／`AssignWorker` 的寫入（只有 UI/快照讀 `NumberOfAssignedWorkers`、`MaxWorkers`）。
- HDBF 只「使用」原版：`Workplace`（`WorkplaceSpec.MaxWorkers`）、`Enterable`、`AutoEmptiable`、`WaitInsideIdlyWorkplaceBehavior`（`HdbfGameConfigurator.cs:106-128`）。

#### C-2 HDBF 讓農民長時間待在建築內【已證實（行為），HDBF 特殊性＝程度差異】

- `HdbfGameConfigurator.cs:113`：`builder.AddDecorator<HdbfBoxFarmSpec, WaitInsideIdlyWorkplaceBehavior>();`
- 原版 `WaitInsideIdlyWorkplaceBehavior.Decide`（IL）：
  - `WalkInsideExecutor.LaunchForLimitedTime(_enterable)`；
  - `ExecutorStatus.Success(0)` → `GetComponent<WaitExecutor>().LaunchForIdleTime()`＋`ReleaseWhenFinished(waitExecutor)`（＝待在室內閒置）；
  - `ExecutorStatus.Failure(1)` → **`_workplace.UnassignWorker(GetComponent<Worker>())`**＋`ReleaseNextTick()`（＝**走不進建築就把 worker 解僱**，`Worker.Workplace` 變 `null`、`Employed=false`）；
  - `ExecutorStatus.Running(2)` → `ReleaseWhenFinished(walkInside)`。
- 這個 behavior **不是 HDBF 專有**（原版 `FarmHouse` 也掛：`Timberborn.Fields.FieldsConfigurator::InitializeBehaviors` IL 有 `AddDecorator<FarmHouse, WaitInsideIdlyWorkplaceBehavior>`；Workshop/Gathering/Forestry/Hauling/PowerGeneration… 也有）。**HDBF 的特殊點是程度**：室內農場沒有戶外工單（種植/收成由日 tick 模擬），所以農民**幾乎 100% 的時間都待在 `Enterable` 裡**；原版農舍的農民大部分時間在田裡，室內停留只是少數工時。
- 因此「室內工人被解僱／停在建築內」這種狀態，在 HDBF 的曝光率遠高於任何原版建築。**【已證實：行為與順序；推測：曝光率造成玩家端 crash 率上升】**

#### C-3 暫停時序：HDBF 看到的是「已解僱、仍在裡面」【已證實】

原版 `PausableBuilding.Pause()`（IL，`.tmp-pause/pause-rootcause.md` a.1 亦記載）：

```text
set_Paused(true)
_blockableObject.Block(this)          // 第一個 blocker → ObjectBlocked
  → Workplace.OnObjectBlocked()       // PostLoadEntity 訂閱
  → UnassignAllWorkers() → UnassignWorker() → Worker.Unemploy()   // Workplace = null
_pauseStatusToggle.Activate()
PausedChanged.Invoke(...)             // HDBF 的 OnPausedChanged 才在這裡被呼叫
```

`BlockableObject.Block` 只會在 `_blockers` 由 0→1 時發 `ObjectBlocked`（IL）；若建築「本來就有別的 blocker」，暫停不會再發事件 → worker 不會被解僱（這是另一條殘留面，見 F-CRASH-02 R6）。
`Worker.Unemploy` 全程**不呼叫 `Enterer.Exit`／`Enterable.Remove`**（IL）→ 這是 2.5.3 存在的理由。

#### C-4 Enterer/Enterable 的原版不變量（HDBF 撤出時會踩到的 throw）【已證實】

以 IL 驗證（`%TEMP%\hdbf-il\enterer-il.txt`）：

| 方法 | 行為／throw |
|---|---|
| `Enterer.Exit()` | `if (CurrentBuilding != null) { CurrentBuilding.Remove(this); Abandon(); }` → 若 `CurrentBuilding == null` 是 **no-op**（不會 throw、也不會把自己從任何集合移除） |
| `Enterable.Remove(enterer)` | 若 enterer 不在 `_enterersInside` → **`ArgumentException`「Can't remove enterer … because it's not inside.」** |
| `Enterer.Abandon()` | 先記舊建築→`CurrentBuilding = null`→`_characterModel.Rotation = 舊建築.ExitWorldSpaceRotation`→`_characterModel.Show()`→`ExitedEnterable`。**若「舊建築」是 fake-null，會在 `ExitWorldSpaceRotation` NRE，且此時 `CurrentBuilding` 已被清成 null（半狀態）** |
| `Enterer.Enter(building)` | 若已在另一建築內 → **`InvalidOperationException`「tried to enter … while already inside …」** |
| `Enterable.get_CanEnter` | `Enabled && (!LimitedCapacity || count < Capacity)`；HDBF 藍圖 `LimitedCapacityFinished=false` → **永遠 true（只要 Enterable enabled）**。**暫停/Blocked 不會讓 CanEnter 變 false**，Enterable 只在「失去 finished 狀態」時被 disable。 |
| `Enterable.OnExitFinishedState()` | → `ForceRemoveEveryone()`（vanilla 安全的大量撤出：對每個 enterer `Abandon()`＋`Remove()`） |
| `Enterable.ForceRemoveEveryone()` | 先 `Abandon()` 再 `Remove()`（與 `Exit()` 的順序相反），且不看 `CurrentBuilding` |

### 1.6 F-CRASH-01 可能原因排序

| 編號 | 假設 | 可能性 | 判準／證據 |
|---|---|---|---|
| **H1** | **第三方 Harmony mod 補丁 `WorkerRootBehavior.Decide`（可能同時補丁 `BehaviorManager.Tick`），其 patch code 假設「worker 一定 employed／Workplace 非 null／農民不會在室內被解僱」，在 HDBF 的室內 worker 狀態（暫停解僱、`WaitInsideIdly` Failure 解僱、換班）上 NRE。**HDBF 只提供 state。 | **高（~55–65%）** | frame 缺席 → 最外層/patch code；HDBF 不 patch、不碰這些欄位（§1.3、§1.5）；T3MP 已排除；本機 mod 集已排除；只剩「玩家另有 mod」這個缺口 |
| **H2** | beaver 的 `_worker`／`_communityServiceBehavior`／`_workRefuser`／`_workerWorkingHours` 在 tick 時是 fake-null（實體正在死亡/被刪除，T3MP frontier 仍排入 tick），NRE 在 Decide 最外層解參考 | **中（~20–25%）** | `WorkerRootBehavior.Decide` IL 前 4 個解參考都符合「無內層 frame」；T3MP 自有 tick frontier 清單（hooks Add/Remove/Enable/Disable/Destroy）是這類 stale tick 的合理來源（**推測**）；需玩家 log 的死亡時間點比對 |
| **H3** | 補丁與遊戲版本不相容（mod 針對舊版 1.0.x 編譯），patch 的 transpiled IL 解參考到錯誤/為 null 的成員 | **低–中（~10–15%）** | Harmony patch 若 transpiler 找不到 pattern 通常會在 patch 時丟例外；但 prefix/postfix 內對 `__instance` 欄位的假設不受保護。需玩家 mod 清單＋是否每次啟動都成功 patch |
| **H4** | HDBF 直接造成 NRE | **極低（<5%，可視為不成立）** | HDBF 無 Harmony、不碰 `Worker`/`Workplace`/`Enterer` 的 null 相關欄位；所有 HDBF 入口都有 try/catch（`.tmp-pause/pause-rootcause.md` (c) 已逐項排除） |
| H5 | T3MP 的 inline limit／frontier 本身造成 NRE | 低 | T3MP 補丁清單已證實不含 Decide/BehaviorManager；其 tick 路徑只負責「哪些實體被 tick」，NRE 發生在 Decide 內 |

**「幾分鐘後才發生」的時間相關因子（依可能性排序）**

1. **換班／WorkingHours 邊界**：`WorkerRootBehavior.Decide` 每 tick 都跑，但換班會讓 `AreWorkingHours` 翻轉、`WaitInsideIdlyWorkplaceBehavior` 重新 `LaunchForLimitedTime`；入口不可達（堆疊農場、門口被擋）→ `Failure` → `UnassignWorker` → `Workplace=null`（§1.5 C-2）。**【高度可能】**
2. **玩家暫停/恢復農場**：暫停 → `UnassignAllWorkers`（worker 在室內被解僱）；恢復 → 重新僱用/再進入。這是室內 worker 狀態變化最劇烈的操作。**【高度可能】**
3. 工人進入/離開建築的 entity/component churn ＋ T3MP frontier（H2 的溫床）。**【推測】**
4. HDBF 日 tick（每遊戲日一次，`Tick.cs:19`）／水耗（每幀，`WaterFlow.cs:25`）／世界圖示（每秒一次，`HdbfWorldIconService.Update.cs:20`）：這些**不會**呼叫 beaver 行為，本身與 Decide NRE 無直接因果；但它們是 HDBF 讓農場「持續運作」的背景，與 1–3 同時發生。**【已證實（呼叫面）】**

### 1.7 最小修復方案（HDBF 端；0 Harmony，不補丁原版）

> 原則：**不修別人的 patch，只收斂 HDBF 自己造成的怪狀態，並留下可判讀證據。** 全部改動都在 `src/HDBF.Gameplay/Components/`（本次調查不實作，僅提出；實作需另開卡片與測試）。

**D1（優先）：暫停/blocked 農場的「室內工人持續 reconcile」**
- 目前 `EjectBeaversFromPausedFarm` 只在 `PausedChanged` 時跑一次（`Pause.cs:214`、`:228`），因此漏掉兩種人：
  - **R1**：暫停存檔載入時，activate 階段 beaver 的 `Enterer` 尚未 restore（§2.2 已用本專案日誌證實）；
  - **R2**：暫停瞬間正在「走進農場」途中、之後才 `Enterer.Enter` 的人（`CanEnter` 不因 blocked 而 false）。
- 修法：新增 session singleton（`IUpdatableSingleton`，約 1 Hz）持有 `_paused` 農場清單；每拍對每個清單農場呼叫 `EjectBeaversFromPausedFarm("reconcile")`，並在 `BindPauseState` 時把 `_enterable.EntererAdded` 也接上（暫停中一有人進入就撤出）。成本 O(暫停農場數)，且暫停時日 tick 已停，不會與日結算打架。
- 驗收日誌鍵（新增）：`indoor.paused.reconcile`（次數）、`indoor.paused.residual-enterer`（beaver 名/entityId/原因：`added-while-paused` | `loaded-inside`）。

**D2：`EjectBeaversFromPausedFarm` 強化（仍走原版 API）**
- 撤出前檢查 `enterer.IsInside`（＝`CurrentBuilding != null`）且 `CurrentBuilding == _enterable`；用 **`UnreserveSlotAndExit()`**（原版，會先 `UnreserveSlot()` 再 `Exit()`）取代裸 `Exit()`，清掉暫停建築上的 reservation（R4）。
- 每個 enterer 失敗要記 `indoor.paused.eject-failed`（含 exception 型別＋beaver 名），成功/跳過/失敗各計數成一行 `indoor.paused.eject-summary`（現在只有 `moved N beaver(s)`，沒有「本來 N 人、剩 M 人」的對照，玩家 log 无法判斷是否漏人）。
- 全部撤出後若 `NumberOfEnterersInside > 0`（表示有 enterer 卡在半狀態、`Exit` no-op）→ 用 `_enterable.ForceRemoveEveryone()` 作 fallback（vanilla 安全路徑，`.tmp-pause/pause-rootcause.md` (d) 原推薦），並記 `indoor.paused.force-remove` 與人數。

**D3：`Deactivate()` 前先撤出（低優先）**
- 失去 finished 狀態時原版 `Enterable.OnExitFinishedState → ForceRemoveEveryone` 會處理（IL 已證實），但 `DeleteEntity`/`Deactivate` 順序若早於它，HDBF 先自己撤一輪更保險；失敗只記 Warn。

**D4：唯讀診斷（可選，不加依賴）**
- 在 `Activate()` 成功後記一行 `indoor.behavior-probe`：以 reflection 讀 `typeof(WorkerRootBehavior).GetMethod("Decide")`，記錄其 `MethodInfo.Name` 是否含 `_Patch`／`GetMethodBody()` 是否為 dynamic（不引用 HarmonyLib、不安裝任何補丁）。有這一行的話，下次玩家回報就能直接確認「Decide 被第三方補丁」而不必猜。
- 另外把 HDBF 啟動行補上「偵測到的 Harmony 是否載入」旗標（讀 `AppDomain` 已載入組件名，不引用型別）。

**D5（最後手段，不建議先做）**
- 移除 `HdbfGameConfigurator.cs:113` 的 `WaitInsideIdlyWorkplaceBehavior`：一次性消除「農民長時間在室內」的狀態面，但會失去 2026-10-28 玩家要求的「農民在建築內工作」體驗，且不解決 F-CRASH-02 的暫停殘留（R1/R2 仍存在於 enterable 的任何用途）。只有在玩家端確認 crash 與室內等待強相關、且無法隔離 patcher 時才考慮。

**明確不做**
- 不新增 Harmony 補丁去包 `WorkerRootBehavior.Decide`（違反 v2 0 Harmony 承諾與 `REQ-V2-SAFE-05`；也會與未知 patcher 互相覆蓋，風險更高）。
- 不改 `Workplace`／`Enterer` 的原版語意、不自行呼叫 `Worker.Unemploy`。

### 1.8 回歸測試與實機驗證（F-CRASH-01）

**L1 契約測試（不動遊戲）**
- `V2PauseContractTests` 擴充文字契約：`EjectBeaversFromPausedFarm` 使用 `UnreserveSlotAndExit`、`EntererAdded` reconcile、`indoor.paused.eject-summary`／`eject-failed`／`residual-enterer` 三個新 log key、`ForceRemoveEveryone` fallback 存在、仍 0 Harmony（`TestPauseIntegrationStaysHarmonyFree` 既有，`V2PauseContractTests.cs:73`）。
- 新增（可選）`V2DiagnosticsContractTests`：`indoor.behavior-probe` 字串存在、不引用 `HarmonyLib`。

**實機重現／隔離（依序）**
1. **乾淨 profile**（只 HDBF 2.5.3，無 Harmony、無 T3MP、無其他 mod）：Basic 農場 2 農民，跑 10 分鐘（含至少 2 次換班、1 次暫停/恢復、入口暫放木頭擋住再移除）。期望：**不得出現** `NullReferenceException`／`Exception thrown while ticking entity`。若乾淨 profile 也 crash → H1/H2 的 patcher 假設要重寫（改看 HDBF 狀態本身＋T3MP 與否）。
2. **只加 Harmony + T3MP**（不加其他）：同劇本 10 分鐘。期望：仍然不 crash（T3MP 已被排除 patch Decide）；若 crash 且 stack 相同 → 回頭重驗 A-2。
3. **玩家 mod 全集**（用 `pause-fix2-A-Player.log` 的 71 個清單）：同劇本。若 crash 重現 → 以二分法移除「會補丁行為」的候選（Second Shift → Efficient Workplaces → Workplace Priority Manager → XtraPlzNoCrashes → ModdableTimberborn → 其他）逐一啟動，直到找出 `Decide_Patch1` 的擁有者。
4. 每次跑完收集：`Player.log`（`Starting game version` 行、`High-Density Box Farm (v2.5.3)` 行、完整 mod 清單、HDBF 行、任何 `NullReference`/`Exception`）、存檔、暫停/恢復的操作時間點。

**需要玩家提供的證據（回覆 Annastasya 的模板）**
1. 完整 `Player.log`（`%USERPROFILE%\AppData\LocalLow\Mechanistry\Timberborn\Player.log`，不是只貼 crash dialog 的 stack）。
2. Mods 清單＋版本（log 開頭就有），特別是「不是 HDBF/T3MP/Harmony」的任何行為／效能／工作相關 mod。
3. 存檔（`.timber`）與 crash 前大約在做什麼（暫停？換班？拆管？）。
4. 是否每次都在「農場運作幾分鐘後」、是否與暫停/換班/接管道同時發生；HDBF 版本是否確實 2.5.3（log 的 `start HDBF version=` 行）。

---

## 2. F-CRASH-02｜「it does get a crash on pause though」（2.5.3 之後仍回報）

### 2.1 v2.5.3 到底修了什麼（**已證實**）

- 文件：`docs/v2/CHANGELOG.md:8` [2.5.3]（2026-10-29）＋`git show 6c6d64a` 訊息；`docs/VERIFICATION.md:1560` §34 有 IL 取證與實機回合。
- 根因（專案自己的說法，本次以 IL 复核一致）：暫停 → `BlockableObject.Block` → `Workplace.OnObjectBlocked` → `UnassignAllWorkers` → `Worker.Unemploy`，**不會** `Enterer.Exit`；HDBF 是唯一「讓農民持續待在室內」的農場（程度差異），殘留 enterer 會撞上原版未捕捉的 Enterer/Enterable/Accessible 不變量（"already inside"／"not inside"／`Single()`），即玩家回報的暫停崩潰。
- 修法：`HdbfBoxFarmController.Pause.cs`（暫停時 `_waterFlow.Unregister`、`_dayTrigger.Pause`、`_worldIcons.Deactivate`、`EjectBeaversFromPausedFarm`；恢復時反向；`SyncPauseStateOnActivate` 處理「載入即暫停」）＋`Pause.Eject.cs`（用 `Enterer.Exit()` 逐隻撤出）。
- 實機證據：`docs/in-game/pause-fix2-A-*`（活動中暫停/恢復，moved 2）、`pause-fix3-A2-*`（暫停存檔載入＋恢復時撤出 2）、`pause-fix3-B-*`。

### 2.2 殘留路徑（2.5.3 之後仍可能發生）

#### R1（**已證實存在**，高度可能是 post-2.5.3 暫停 crash 的來源）：暫停存檔載入時，撤出時機早於 beaver 的 `Enterer` restore

- 證據（本專案自己的 log，`docs/in-game/pause-fix3-A2-Player.log`）：

```text
2931: [HDBF/Info] indoor.paused: indoor farm paused by the player; daily tick, water drain and warning icons stopped, entityId=c6c2da15-...
2932: [HDBF/Info] indoor.entity: indoor farm active, ... blueprint=BoxFarm.Basic.Folktails
2955: Load time: 112777ms (scene index: 2)
2958: [HDBF/Info] indoor.paused: moved 2 beaver(s) out of the paused farm, entityId=c6c2da15-...
2959: [HDBF/Info] indoor.resumed: indoor farm resumed by the player; ...
```

- 解讀：2931（`EnterPausedState`）時 `EjectBeaversFromPausedFarm` 執行、但 **沒有** `moved` 行 → 當下 `Enterable.EnterersInside` 是 0；2955 載入完成後，兩名工人才以「在室內」的狀態存在；HDBF 不會再獲得 `PausedChanged`（`ApplyPausedState` 對相同 `paused` 值 early-return，`Pause.cs:167-172`），所以**整段暫停期間兩人都在被 blocked 的農場室內**，直到 2958 恢復時才被撤出。
- 觸發條件：暫停中存檔 → 讀檔（`PausableBuilding.Load()` 在載入期呼叫 `Pause()`；HDBF 的 `SyncPauseStateOnActivate` 在 activate 期進入暫停態）＋ 存檔時農民在室內（`.tmp-pause` 的存檔探測已證明 HDBF 存檔會保留 `Enterer.CurrentBuilding = farm`，4 隻農民都是）。
- 風險：這正是 2.5.2 前暫停崩潰的「高危狀態」；原版後續任何 `WalkerMover.Move → Enterer.Exit`、`Enterer.Enter`（already inside）、`Accessible.Single()`、`Enterable.Remove`（not inside）都可能變 unhandled（`.tmp-pause/pause-rootcause.md` C1/C2 已逐條做過 IL 驗證）。**【已證實：狀態存在；高度可能：為玩家 crash 成因】**

#### R2（中）：暫停瞬間「正在走進農場」的 beaver 不在 snapshot 裡

- `EjectBeaversFromPausedFarm` 只快照 `_enterable.EnterersInside`（`Pause.Eject.cs:42-48`）。若暫停時某農民已被 `WalkInsideExecutor` 送出、尚未 `Enterer.Enter`，他不在集合內；之後 `Enterer.Enter` 仍會成功，因為 `Enterable.get_CanEnter` 只看 `Enabled` 與容量（HDBF 藍圖不限容量），**暫停/blocked 不影響**（IL，§1.5 C-4）。結果：beaver 在暫停期間走進 blocked 農場，成為殘留 enterer，且 HDBF 不會再 reconcile。**【已證實：CanEnter 行為；高度可能：可發生】**

#### R3（中低）：`Enterer.Exit()` 對「半狀態 enterer」是 no-op

- `Enterer.Exit()` 在 `CurrentBuilding == null` 時直接 no-op（IL）。若某 enterer 仍在 `EnterersInside` 但 `CurrentBuilding` 已被清成 null（例如先前的 `Abandon()` 在 `_characterModel`／`ExitWorldSpaceRotation` 上 NRE，見 §1.5 C-4），HDBF 的撤出永遠無效，該 enterer 永久卡在集合內；之後任何 `Enterable.Remove` 會丟 `ArgumentException`。**【推測：需要實際半狀態才發生；程式路徑已證實】**

#### R4（低–中）：撤出沒清 reserved slot

- 目前用 `enterer.Exit()`（`Pause.Eject.cs:68`），不是 `UnreserveSlotAndExit()`。暫停建築上可能留下 reservation（`NumberOfReservedSlots`），恢復後可能造成「進不去」或 access 相關的怪狀態。**【推測】**

#### R5（低）：`Deactivate()` 不撤出

- `HdbfBoxFarmController.cs:187` `OnExitFinishedState → Deactivate`、`:203` `DeleteEntity → Deactivate` 都不呼叫撤出；但原版 `Enterable.OnExitFinishedState → ForceRemoveEveryone`（IL）會處理「失去 finished 狀態」的情況，所以風險低。若 `Deactivate` 先於 Enterable 的 finished-state callback，順序上仍安全（都在同一同步流程）。**【已證實：vanilla fallback 存在】**

#### R6（低，但屬 pause-blocked 家族）：建築「本來就 blocked」時，暫停不會解僱 worker

- `BlockableObject.Block` 只在 `_blockers` 0→1 時發 `ObjectBlocked`（IL）→ 若農場入口已被別的原因 blocked，暫停只加第二個 blocker、**不發事件 → worker 不被解僱**，仍 employed 在 blocked workplace。原版 `Workplace.AssignWorker` 在 blocked 時會丟 `InvalidOperationException`（`Understaffed` 在 blocked 時為 false；`.tmp-pause/pause-rootcause.md` a.4 已在讀檔路徑看到 `After loading … couldn't get employed …` 的實際 log），而 assigner 正常會用 `Understaffed` 預過濾、讀檔路徑不會。這是「pause + blocked」的殘留面，**不在 2.5.3 修的範圍**。**【已證實：機制；推測：玩家是否踩到】**

#### R7（低）：多人/多農場/其他 worker type

- 撤出是每 farm、每 enterer 隔離（`Pause.Eject.cs:58-78`），多農場互不影響；Bot（IronTeeth）不是 Enterer，HDBF 不處理。未見具體風險，列為待驗。**【推測】**

### 2.3 判別 merlin4059 的留言是哪一種（回覆/客服用）

依序檢查玩家提供的 `Player.log`：

1. **版本**：`Starting game version 1.1.2.8`（遊戲）＋mod 清單中 `High-Density Box Farm … (v2.5.3)`；HDBF 自己會印 `[HDBF/Info] start HDBF version=2.5.3`。若版本 < 2.5.3 → 屬「舊版留言／Steam 尚未重啟」，先要求更新並重啟再測。
2. **2.5.3 的暫停日誌**：是否有 `indoor.paused: indoor farm paused by the player; …` 與 `indoor.paused: moved N beaver(s) out …`、`indoor.resumed: …`。
   - 完全沒有這些行 → 可能不是走 HDBF 暫停整合（或 crash 發生在 pause 之前）。
   - 有 `indoor.paused` 但 **沒有** `moved` 行，而 crash 前後有 `Load time` → 指向 **R1**（暫停存檔載入）。
3. **Crash stack 型別**：
   - `NullReferenceException` in `WorkerRootBehavior.Decide_Patch1` → **F-CRASH-01（第三方補丁）**，不是 2.5.3 暫停修復的回歸。
   - `InvalidOperationException`／`ArgumentException`（`"Can't remove enterer … because it's not inside."`、`"tried to enter … while already inside"`、`"Sequence contains no elements"`、`"Can't assign … this workplace isn't understaffed"`）→ **暫停殘留路徑（R1/R2/R3/R6）**。
4. 玩家敘述：「按暫停就 crash」 vs 「暫停後過一陣子/恢復時 crash」 vs 「載入存檔後一動就 crash」——R1 的典型是**讀取暫停中的存檔後**；2.5.3 前的典型是**按暫停當下**。

### 2.4 最小修復方案（F-CRASH-02）

- **FIX-1（對應 R1，最高優先）**：`PostLoadEntity`（`HdbfBoxFarmController.cs:193`）之後補一次「延遲 reconcile」：不要在 activate 當下只做一次，改成由 D1 的 session singleton 在載入完成的下一拍（或 1 秒內）對所有 `_paused` 農場再撤一輪；並在撤出後記 `indoor.paused.eject-summary`（requested/ejected/skipped/remaining）。這直接消滅「暫停存檔載入後工人留在 blocked 農場」的窗口。
- **FIX-2（對應 R2）**：暫停中對 `_enterable.EntererAdded` 反應（一有人進 blocked 農場就撤出）；同一事件也覆蓋「暫停後才走進來」的人。搭配 FIX-1 的週期 reconcile 作雙保險。
- **FIX-3（對應 R3/R4）**：`EjectBeaversFromPausedFarm` 改用 `IsInside + CurrentBuilding == _enterable` 檢查與 `UnreserveSlotAndExit()`；`Exit` 後檢查 `NumberOfEnterersInside` 是否下降，未下降 → 記 `eject-failed` 並改用 `ForceRemoveEveryone()` fallback。
- **FIX-4（對應 R6，選配）**：若 `_pausable.Paused` 而 `_blockableObject.IsUnblocked == false`（`Workplace` 本來就 blocked），HDBF 不應依賴 `ObjectBlocked` 的解僱；可在暫停 reconcile 時記 `indoor.paused.already-blocked`（唯讀診斷），讓玩家 log 能區分「pause 造成 blocked」與「本來就 blocked」。
- **不改**：不自行 `Worker.Unemploy`／不補丁 `Workplace.AssignWorker`（原版在讀檔時已自行 catch+log，屬 vanilla 行為）。

### 2.5 回歸測試與實機驗證（F-CRASH-02）

**L1**：`V2PauseContractTests` 加 4 條（對應 FIX-1..4 的字串契約與新 log keys），並保留 `TestPauseIntegrationStaysHarmonyFree`。

**實機劇本（每項都收集 `Player.log` 與截圖）**
1. **活動中暫停**：Basic 農場 2 農民、至少 1 人在室內 → 暫停 → 恢復 ×5。期望：`indoor.paused` + `moved N`、`indoor.resumed`、無 exception，恢復後 2/2 重新上工、作物繼續長。
2. **暫停存檔載入（R1 專項）**：暫停中存檔 → 重載 → **不恢復**，等 10 秒，開面板看工人數；期望（含 FIX-1）：reconcile 日誌出現、室內 enterer = 0；舊行為重現：2/2→0/2 但兩隻仍在建築內（`moved` 只在恢復時出現）。
3. **走進農場途中暫停（R2 專項）**：把農場入口設在較遠、讓農民走進去；在 `WalkInsideExecutor` 行走中暫停。期望（含 FIX-2）：`residual-enterer` log、最終 `NumberOfEnterersInside == 0`。
4. **堆疊農場／入口不可達（觸發 `WaitInsideIdly` Failure）**：上層農場門口無步道連接（參考 `.tmp-pause` 的 Repro3 存檔），換班 1–2 次。期望：農民被解僱且**不**留在室內（`Failure` → `UnassignWorker` 是由原版處理；HDBF 不應在室內留人），無 exception。
5. **拆除/undo/失去 finished 狀態**：完成農場（含室內工人）→ 拆除或 undo → 重蓋。期望：原版 `ForceRemoveEveryone` 生效、無 HDBF error。
6. **暫停 + 管道/面板**：暫停中把管道接上 → 恢復 → 觀察 pipe port 狀態（與 §3 交叉）。
7. **反覆 pause/resume ×20**、多人存檔、快速存讀：無 `ArgumentException`/`InvalidOperationException`/`NullReferenceException`。

**每次驗證要檢查的 log 行**（可直接用 `Select-String`）：
`start HDBF version=`、`indoor.paused`、`indoor.resumed`、`eject-summary`、`residual-enterer`、`Exception thrown while ticking entity`、`NullReferenceException`、`Can't remove enterer`、`while already inside`、`isn't understaffed`、`Load time`。

---

## 3. 交叉判斷：與「管道 No Connection」是否同源？

**判定：不同源（高度可能）。** 兩個問題不共用程式路徑，也不共用失敗點；但共享「暫停」這個操作面。

**證據（已證實）**
1. 管道探測只出現在：`Context.cs:77-107 ReadPipeWater`（由 `Activation.cs:57`、`Tick.cs:74`、`WaterSupply.cs:251-270 RefreshSupplyAfterLoad` 呼叫）與面板刷新 `WaterSupply.cs:218-245 RefreshSupplyForPanel`。它透過 `IIndoorAdapterHost`（`IndoorAdapterHost.cs`）→ `TimberPipesWaterSource` → 反射 TimberPipes，全程 try/catch 降級（`REQ-V2-COMPAT-02`），**不會碰 Worker/Workplace/Enterer**，也不在 beaver tick 路徑上；沒有「管道 → beaver 行為」的呼叫邊。
2. 面板「Not connected」字串只來自 UI：`data/Localizations/enUS.csv:81 HDBF.Port.NotConnected` → `HdbfIndoorPanelFactory.Cards.Output.cs:106/140`（Output port 與 Pipe/Logistics/Power 三個 PortRow），是**顯示狀態**，不是 crash。
3. 兩個 crash 的堆疊與管道完全無關（見 §1.1）、`F-CRASH-02` 的殘留路徑也不經過管道程式碼。

**但有一個共用觸發面（高度可能 → 管道問題的獨立成因假設，交由管道調查員驗證）**
- HDBF **只在** 啟動、PostLoad、每個遊戲日 tick 重新探測管道（`ReadPipeWater`）；面板每秒只重用快取的 `_hasPipeWaterCached`（`RefreshSupplyForPanel` 不會重探）。因此「農場完成後才接上管道」→ 面板會維持 Not connected 到下一個遊戲日／重載；**若農場被暫停，日 tick 已停 → 永遠不會更新，直到恢復或重載**。這與 Annastasya 的「有接管道卻顯示 No Connection」高度吻合（她可能同時在暫停/調整農場）。
- 其他獨立可能（交接）：adapter health 非 Ok/Degraded（`CompatAdapterRegistry` 三振或錨點簽名不符，本機釘 Timber Pipes 11.4.0）、`TimberPipesWaterSource.HasNetwork` 找不到「finished + 有 PipeGraph + 收水」的 PipeTank、或玩家接的是「管道但沒有水箱節點」（HDBF 的 `IsPipeConnected` 定義是「任一網路節點上的可取水水箱」，不是物理接頭）。
- **不建議**把管道問題與 crash 綁成同一張修復卡；建議管道調查員另外加一個 throttled `water.pipe-probe` 診斷鍵（`IsPipeConnected`／`PipeHealth`／節點數），並在暫停期間仍允許輕量重探。

---

## 4. 未解問題（誠實列出）

1. **`Decide` patcher 的身分未確定**：本機 71 mod 已排除（含 T3MP、Second Shift、Efficient Workplaces、Workplace Priority Manager、XtraPlzNoCrashes…），最有價值的一步是拿到玩家的 `Player.log` 完整 mod 清單，或用二分法隔離。**在拿到之前，不要把 F-CRASH-01 歸因於任何具名 mod。**
2. **merlin4059 的 crash log 未取得**：無法判定是 2.5.3 暫停殘留（R1/R2…）還是另一個 crash；需版本行＋stack。
3. **R1/R2 的殘留狀態是否真的會把例外帶出遊戲**：狀態存在已證實（A2 log），但「哪個原版 throw site 被踩到」未證實（`.tmp-pause/pause-rootcause.md` C1/C2 也只能做機制推導）。
4. **T3MP 的 inline limit（300）與 tick frontier 是否放大 H2（stale entity tick）**：推測，未證實；需要玩家 crash 前後的實體死亡/刪除時間線。
5. **管道「Not connected」的根因**屬管道調查員；本檔只做同源判斷與交接點。
6. 本次**未重新建置** `tools/ApiIndex`：`dotnet build` 被沙箱拒絕寫入 `%USERPROFILE%\.hdbf\work\obj`（`Access denied`），改用 2026-10-29 既有的 `ApiIndex.dll`（比 `Program.cs`（2026-10-24）新，內容一致）。此限制不影響 dump 正確性，但記錄於此供審計。

---

## 附錄 A：關鍵 IL 摘錄（1.1.2.8，安裝版 Managed）

### A-1 `WorkerRootBehavior.Decide`（完整）

```text
IL_0000: _workRefuser
IL_0006: callvirt WorkRefuser::get_RefusesWork()   ; brtrue -> ReleaseNow
IL_000d: _worker
IL_0013: callvirt Worker::get_Employed()           ; brfalse -> communityService
IL_001a: _workerWorkingHours
IL_0020: callvirt WorkerWorkingHours::get_AreWorkingHours()  ; brfalse -> communityService
IL_0027: call DecideAsWorker(); ret
IL_002e: _communityServiceBehavior
IL_0034: ldarg.1
IL_0035: callvirt Behavior::Decide(agent); ret
IL_003b: call Decision::ReleaseNow(); ret
```

### A-2 `Worker.get_Employed` / `Worker.Unemploy`（順序很重要）

```text
get_Employed:
  call BaseComponent::get_Enabled(); brfalse -> false
  call Worker::get_Workplace()
  call BaseComponent::op_Implicit()      ; Workplace != null
  ret

Unemploy:
  if (get_Workplace() == null) return
  set_Workplace(null)                    ; <-- 先清欄位
  get_Workplace().UnassignWorker(this)   ; (此時已為 null，實際先存 old)
  GotUnemployed.Invoke
  RelationsChanged.Invoke
```

### A-3 `WaitInsideIdlyWorkplaceBehavior.Decide`（switch 原文）

```text
var walkInside = agent.GetComponent<WalkInsideExecutor>();
var status = walkInside.LaunchForLimitedTime(_enterable);
switch (status):
  case Success(0):  agent.GetComponent<WaitExecutor>().LaunchForIdleTime();
                    return Decision.ReleaseWhenFinished(waitExecutor);
  case Failure(1):  var worker = agent.GetComponent<Worker>();
                    _workplace.UnassignWorker(worker);      // Workplace = null
                    return Decision.ReleaseNextTick();
  case Running(2):  return Decision.ReleaseWhenFinished(walkInside);
  default:          throw new ArgumentOutOfRangeException();
```

### A-4 `Enterer.Exit` / `Enterable.Remove` / `Enterable.get_CanEnter` / `ForceRemoveEveryone`

```text
Enterer.Exit:
  if (CurrentBuilding != null) { CurrentBuilding.Remove(this); Abandon(); }
  ; CurrentBuilding == null -> no-op

Enterable.Remove:
  if (!_enterersInside.Contains(enterer)) throw ArgumentException("... because it's not inside.");
  _enterersInside.Remove(enterer); EntererRemoved.Invoke

Enterable.get_CanEnter:
  if (!Enabled) return false;
  if (!LimitedCapacity) return true;          ; HDBF: LimitedCapacityFinished=false -> true
  return _enterersInside.Count < Capacity;

Enterable.OnExitFinishedState:
  if (ShouldOperate) { ForceRemoveEveryone(); DisableComponent(); }

ForceRemoveEveryone:
  foreach (e in _enterersInside.ToArray()) { e.Abandon(); Remove(e); }
```

### A-5 `PausableBuilding.Pause`（與 HDBF 訂閱的順序）

```text
if (Paused) return;
set_Paused(true);
_blockableObject.Block(this);      // 第一個 blocker -> ObjectBlocked -> Workplace.OnObjectBlocked -> UnassignAllWorkers
_pauseStatusToggle.Activate();
PausedChanged.Invoke(...);         // HDBF 的 OnPausedChanged 在「解僱之後」才跑
```

---

## 附錄 B：證據檔案與工具命令（可重跑）

| 證據 | 位置／命令 |
|---|---|
| 玩家 crash 全文 | <https://pastebin.com/SBPK5AJQ>（`https://pastebin.com/raw/SBPK5AJQ`） |
| 遊戲版本／mod 清單 | `docs/in-game/pause-fix2-A-Player.log`（第 28 行 `Starting game version 1.1.2.8-ffc2de0-sw`；第 47–117 行 mod 清單；第 123–150 行 T3MP 安裝/patch/inline limit） |
| 2.5.3 暫停修復 commit | `git show 6c6d64a`；`docs/v2/CHANGELOG.md:8`；`docs/VERIFICATION.md:1560` §34 |
| 暫停載入殘留（R1） | `docs/in-game/pause-fix3-A2-Player.log:2931,2932,2955,2958,2959`；`docs/in-game/pause-fix2-A-Player.log:2952-2956`（對照：活動中暫停正常） |
| 舊暫停根因分析 | `.tmp-pause/pause-rootcause.md`（唯讀參考；a.1–a.9、C1–C5） |
| Managed IL dump | `dotnet %USERPROFILE%\.hdbf\work\bin\ApiIndex\Release\net9.0\ApiIndex.dll dump "<Managed>" "<out>" "WorkerRootBehavior"`；同工具 `il` 模式：`ApiIndex il <Managed> <out> "^Timberborn\.WorkSystem\.WorkerRootBehavior$" "Decide|DecideAsWorker|WorkAtWorkplace|Awake"`；`Enterer/Enterable` 同法 |
| T3MP patch 清單 | `ApiIndex index "<3756656421>" out`＋`ApiIndex il "<3756656421>" out "^T3MP\.Runtime\.(RuntimePatches|TubeVisitFix|TickFrontier)" ".*"`；`ldstr` 目標見 §1.3 A-2 |
| 本機 mod 掃描 | `findstr /S /M /C:"WorkerRootBehavior" "<workshop>\*.dll"` → 零命中（Managed 對照有命中） |
| HDBF DLL 檢查 | `steamapps\workshop\content\1062090\3828809784\HDBF.Gameplay.dll`：`WorkerRootBehavior`/`Harmony` 不存在 |

**產物**：本次 dump 位於 `%TEMP%\hdbf-il\*.txt`（`workerroot-il.txt`、`workplace-il.txt`、`enterer-il.txt`、`waitinside-il.txt`、`workrefuser-il.txt`、`behavior-dump.txt`、`community-dump.txt`、`pause-block-il.txt`、`t3mp-index.txt`、`t3mp-runtime-il.txt`、`waitinside-referrers.txt` 等）；屬工作暫存，不進版控。

---

## 附錄 C：判定表（給 Lead／回覆玩家用）

| 結論 | 判定 |
|---|---|
| `WorkerRootBehavior.Decide` 被第三方 Harmony 補丁（`Decide_Patch1`） | **已證實**（堆疊語意） |
| `BehaviorManager.Tick` 被第三方 Harmony 補丁 | **已證實**（堆疊） |
| HDBF 不是 patcher（0 Harmony、無字串、無 API 呼叫） | **已證實**（§1.3 A-1） |
| T3MP 不是 Decide/BehaviorManager patcher | **已證實（本機 1.2.5）**（§1.3 A-2） |
| 本機 71 mod 皆不 patch Decide | **已證實（本機）**（§1.3 A-3） |
| patcher 身分 | **未確定**（需玩家 log／mod 二分） |
| vanilla `Decide` 在 Employed=true 且 Workplace=null 時會 NRE | **已證實（IL）**，但原版正常流程不會產生該狀態 |
| HDBF 會讓 worker 在室內被解僱（暫停／WaitInside Failure） | **已證實**（§1.5 C-2/C-3） |
| HDBF 直接拋出 F-CRASH-01 的 NRE | **不成立（已排除）** |
| 暫停存檔載入後工人留在 blocked 農場（R1） | **已證實（本專案日誌）** |
| R1 是玩家 F-CRASH-02 的直接成因 | **高度可能（待玩家 log）** |
| R2（走進途中暫停）可發生 | **高度可能（IL 已證實 CanEnter 不看 blocked）** |
| R3 半狀態 enterer 會讓 Exit no-op | **推測（路徑已證實）** |
| 管道 No Connection 與 crash 同源 | **不同源（高度可能）**；但共用「暫停時日 tick 停止 → 管道狀態 stale」觸發面 |
| HDBF 2.5.3 已修「按暫停當下」的暫停崩潰 | **已證實（專案實機日誌）** |
| 2.5.3 之後完全沒有暫停殘留 | **不成立（R1 已證實存在）** |
