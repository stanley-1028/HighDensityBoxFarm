# 玩家回饋 Bug 維修文檔 — 2026-11-02（HDBF v2.5.3 → v2.5.4 已維修並發佈）

> **文件名**：`docs/BUG_REPAIR_20261102.md`
> **狀態**：調查完成；**維修已實作並發佈（2026-11-02 實作輪，v2.5.4；離線 L1／validator／44 gates 全綠；Steam Workshop ManifestID `2037025195551276545`；GitHub `2a4a237`；實機 fixture 驗證 pending）**；**第二實作輪（2026-11-03，v2.5.5，未發佈）已修離線可證明的 L-04 / L-12 / L-15 / L-09（L1 672/672、44 gates 全綠；實機仍 pending）** — 見文末 §10 與 §11。§0–§8 保留原本調查內容不變。
> **對象版本**：HDBF **2.5.3**（2026-10-29 發佈，HEAD `de1015c`；Steam Workshop item `3828809784`）
> **遊戲版本**：Timberborn **1.1.2.8**（`1.1.2.8-ffc2de0-sw`，Unity 6000.5.11f1）
> **來源**：Steam Workshop 玩家留言（截圖，2026-11-02 21:16 擷取）、Annastasya 的錯誤日誌（pastebin）、專案內既有實機紀錄/日誌
> **證據檔**（本次 5 條獨立調查的完整證據，唯讀）：
> `docs/workstreams/feedback-20261102/maintenance.md`、`crash.md`、`pipes.md`、`factions.md`、`latent-future.md`

---

## 0. 摘要（先看這頁）

> **狀態標記（2026-11-02 v2.5.4 實作輪）**：✅ = 已完成（程式碼＋L1 契約＋validator＋44 release gates；已隨 Steam Workshop ManifestID `2037025195551276545`／GitHub `2a4a237` 發佈）；🟡 = 部分完成（已完成部分見各節說明，其餘 pending）；⏸ = 未做（pending）。**§7.2 的實機 fixture 回合本輪未執行，全部仍標 pending**，不寫成已驗證。

### 0.1 這輪到底要修什麼（P0 一覽）

| 編號 | 問題 | 類型 | 玩家痛感 | 對應工作包 | 狀態（v2.5.4） |
|---|---|---|---|---|---|
| RE-01 | 維護只能手動；**逾期 ≥2 期時按一次「執行維護」仍停擺**（按了沒用），面板/tooltip 又說會恢復 | Bug + 設計落差 | 高（玩家直接說「什麼都不發生」） | WP-01、WP-02、WP-06 | ✅ 已修（追平付費＋未到期禁用） |
| RE-04 | 2.5.3 修了「按暫停當下」的崩潰，但**暫停中存檔→讀檔**仍會把工人留在被封鎖農場內（專案自己的日誌已證實 R1） | 殘留 crash 狀態 | 高（crash 家族） | WP-04 | ✅ 已修（1 Hz reconcile＋撤出強化） |
| RE-05 | 把管接到農場後面板仍顯示「未連接」：**農場不是 Timber Pipes 端點**，判定看的是全世界的水塔清單 | Bug（語義/判定） | 高（玩家已回報） | WP-05 | 🟡 原因碼/文案/probe 已修；真實連通判定 pending |
| L-01 | **休耕→常規的第一個日 tick，整盒成熟作物以 0 產量被清空**（未回報，但程式碼已證實） | Bug（資料損失） | 極高（作物人間蒸發） | WP-03 | ✅ 已修（零產量不清槽） |
| L-03 | 水罐 3–4 天見底後農場**靜默停工**，三條補水路徑在 UI 完全沒揭露 | 設計代價 + UX | 極高（最大負評來源） | WP-06 | 🟡 面板透明度已做；水罐平衡未改 |
| L-04 | 存檔「驗證有修復就整筆拒寫」：未知作物/舊狀態可能靜默不存檔、進度回捲 | Bug（條件式） | 中（觸發時災難） | WP-14 | ✅ 已修（v2.5.5 離線：可修復即寫入修復副本＋`PreviousPayload` 回滾副本；keyed bridge 待統一） |
| RE-03 | 數分鐘後致命 NRE：**第三方 Harmony 補丁**的 `WorkerRootBehavior.Decide` 在 HDBF 的室內工人狀態上 NRE；patcher 未確定 | 相容性（非 HDBF 補丁） | 高（玩家只看到 crash） | WP-07（診斷）＋WP-04（收斂狀態） | ⏸ pending（第三方補丁；probe 未做） |
| RE-06 | Ember-pelts 等自訂陣營**完全看不到** HDBF 建築（template collection 未註冊） | 功能缺口 | 中高（陣營玩家） | WP-13 | ⏸ pending |
| RE-02 | 比佛不會做 upkeep（維護/補水沒有工人行為，維護是玩家按鈕、補水是瞬時） | 設計落差 | 中高 | WP-02、WP-06 | 🟡 自動維護已補；工人行為未做 |

> **一句話**：這次回報表面上只有 5 組問題（維護、崩潰、暫停、管道、陣營），但程式碼審查另外找到 **20 條未回報的潛在 bug**（其中 3 條 P0：休耕清空作物、管道全域假連通、水耗盡靜默停工），以及 12 條高機率的「下一波玩家留言」預測。**最該先修的三件事跟 crash 無關：L-01（休耕轉換吃收成）、L-03（水 3 天見底沒說明）、L-02（管道判定）**。

### 0.2 三類清單的關係

```
玩家已回報（Section 3）── 6 條 + 5 條建議
        │
        ├── 直接對應的根因（維護手動、暫停殘留、管道端點、陣營資料）
        │
        └── 暴露出的系統性問題 ── 潛在 bug（Section 4，20 條＝19 條潛在調查 + 1 條 crash 相容性）
                                        │
                                        └── 未來回報預測（Section 5，Top 12）
```

### 0.3 最重要的 5 個結論（含證據強度）

1. **「按了維護還是停擺」是真的 bug，不是玩家誤會。** `TryApplyMaintenance` 只做 `dueDay += 1 個週期`（`src/HDBF.Gameplay/Components/HdbfBoxFarmController.Commands.cs:254-258`），逾期 3 期時按一次仍剩 2 期 → 仍停擺；同時把 `MaintenanceCyclesMissed` 歸零（`:266`）造成面板「逾期 0」與「停擺」並存。**（已證實，純邏輯）**
   **→ 2.5.4：✅ 已修（一次點擊追平所有逾期期數；未到期禁用；`V2MaintenancePowerTests`）。**
2. **休耕切回常規會吃掉整盒成熟作物。** 轉換日 `ModeTransitionYieldScale(Normal, Fallow)` 因 Fallow 產量 = 0 而回傳 scale = 0（`src/HDBF.Core/Indoor/IndoorGrowthMath.cs:114-130`），`TryHarvest` 在 `amount == 0` 時仍清空槽位並記「收穫成功」（`src/HDBF.Core/Indoor/IndoorLaborMath.Scheduling.cs:27-61`）。**（已證實，程式碼路徑完全確定；L1 可釘死）**
   **→ 2.5.4：✅ 已修（零產量不清槽＋轉換 scale 修正；`V2FallowTransitionTests`）。**
3. **「No Connection」是 HDBF 面板自己的字串**（`HDBF.Port.NotConnected` = `Not connected` / `未連接`，`HdbfIndoorPanelFactory.Cards.Output.cs:109-110`），不是 Timber Pipes 或原版。根因：農場藍圖沒有任何 Timber Pipes 元件，判定其實是「全世界有沒有一顆接上管網的液體水塔」（`TimberPipesWaterSource.cs:97-122`）。**（已證實；但「玩家該怎麼接」仍缺實機驗證）**
   **→ 2.5.4：🟡 文案/原因碼/唯讀 probe 已完成；`TryGetGraph` 真實連通判定 pending（需實機）。**
4. **2.5.3 的暫停修復有時序缺口，專案日誌已重現。** `pause-fix3-A2-Player.log`：`indoor.paused`（2931）→ `Load time`（2955）→ 才 `moved 2 beaver(s)`（2958）——整段「暫停載入」兩隻工人都在被封鎖農場內。**（狀態已證實；是否直接造成玩家 crash＝高度可能）**
   **→ 2.5.4：✅ 已修（1 Hz reconcile＋`EntererAdded`＋撤出強化；實機 E/F 仍 pending）。**
5. **Ember-pelts 是「未支援」不是「壞掉」：** 建造選單只載入 `Common` + 當前陣營的 `TemplateCollectionIds`，HDBF 只註冊 `Buildings.Folktails` / `Buildings.IronTeeth`，所以自訂陣營看不到四檔建築。**（遊戲機制與 HDBF 資料已用 IL/藍圖證實；Emberpelts 內部資料需安裝主模組才能確認）**
   **→ 2.5.4：⏸ pending（WP-13 未做，需 Emberpelts 主模組）。**

---

## 1. 來源、版本與時間線

### 1.1 玩家留言（Steam Workshop 截圖逐則）

截圖擷取時間：**2026-11-02 21:16**（Asia/Macau）。相對時間換算為大約實際時間。

| # | 作者 | 大約時間 | 原文 | 中文意思 |
|---|---|---|---|---|
| 1 | Eltharion | 11-02 ~20:27（49 分鐘前） | "Is there an option to automate the maintenance? You have to do it manually, otherwise nothing happens as far as I can see." | 有沒有自動維護的選項？你必須手動做，不然看起來什麼都不會發生。 |
| 2 | stanley1028〔作者〕 | 11-02 ~15:16（6 小時前） | "Ok, I know it, and I'm sorry to have let you down." | 好，我知道了，很抱歉讓你失望。 |
| 3 | Annastasya | 11-02 ~13:16（8 小時前） | "After a few minutes of having a working Box Farm I get a fatal crash. Also, having pipes connected to the Box Farm, it still shows \"No Connection\". Here is the error log: https://pastebin.com/SBPK5AJQ" | 盒子農場運作幾分鐘後就致命崩潰。另外，管子接到農場了，它還是顯示「No Connection」。錯誤日誌：… |
| 4 | stanley1028〔作者〕 | 11-02 ~12:16（9 小時前） | "maybe I'll fix it at Saturday and sunday" | 也許週六日會修。 |
| 5 | stanley1028〔作者〕 | 11-02 ~12:16（9 小時前） | "Ok, I'll fix it, thanks" | 好，我會修，謝謝。 |
| 6 | merlin4059 | 11-02 ~12:16（9 小時前） | "I like it, it does get a crash on pause though, and for some reason my beavers wont do upkeep, get that worked out though and I like it." | 我喜歡它，不過暫停時會 crash；而且我的比佛不知為何不做維護。把這搞定我就喜歡。 |
| 7 | saqlain141 | 11-01 04:48 | "found it sadly it doesn't work for the Ember-pelts" | 找到了，可惜它對 Ember-pelts 陣營不能用。 |

> 留言 1 與 6 講的是同一組體驗（維護/upkeep 要手動）；留言 3 同時包含兩個獨立問題（崩潰、管道顯示）；留言 7 是陣營支援。**作者的兩則回覆（#2、#4/#5）顯示已經知道問題，但尚未提供版本修復。**

### 1.2 錯誤日誌（pastebin `SBPK5AJQ`）

- 張貼時間：**2026-11-01**（2.48 KB，Gaming 分類）。
- 內容全文已抓取（raw：`https://pastebin.com/raw/SBPK5AJQ`），核心堆疊：

```text
NullReferenceException: Object reference not set to an instance of an object
(wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition.Timberborn.WorkSystem.WorkerRootBehavior.Decide_Patch1(
    Timberborn.WorkSystem.WorkerRootBehavior, Timberborn.BehaviorSystem.BehaviorAgent)
(wrapper dynamic-method) ... Glue:AbiFixup<Decision WorkerRootBehavior:Decide(...), ...Decide_Patch1(...)>(...)
Timberborn.BehaviorSystem.BehaviorManager.ProcessBehavior (Behavior behavior)
Timberborn.BehaviorSystem.BehaviorManager.ProcessBehaviors ()
(wrapper dynamic-method) ... BehaviorManager.Tick_Patch1(BehaviorManager)
Timberborn.TickSystem.MeteredTickableComponent.Tick ()
...
Rethrow as Exception: Exception thrown while ticking entity b66554ae-0079-44de-a57b-af206e96d1f8 'BeaverAdult Zelaji'
T3MP.Runtime.TickFrontier.TickAllFast (TickableEntityBucket bucket)
(wrapper dynamic-method) ... TickableEntityBucket.TickAll_Patch1(...)
(wrapper dynamic-method) ... TickableBucketService.TickNextBucket_Patch1(...)
(wrapper dynamic-method) ... TickableBucketService.TickBuckets_Patch1(..., int)
Timberborn.TickSystem.Ticker.Update (float deltaTimeInSeconds)
Timberborn.TickSystem.TickerUnityAdapter.Update ()
```

**判讀（詳見 `feedback-20261102/crash.md` §1）**：
- `Decide_Patch1` + `Glue:AbiFixup` 是 **Harmony/MonoMod 生成的第三方補丁替換方法**；`BehaviorManager.Tick` 也被補丁。
- `T3MP.Runtime.TickFrontier` 是工作坊「More More More Performance! (T3MP)」1.2.5 的 tick frontier（本機已安裝、可查）。
- 被 tick 的實體是 **成年海狸**（不是建築）→ 崩潰發生在 beaver 的行為決策路徑。

### 1.3 版本時間線（重要：留言都在 2.5.3 之後）

| 日期時間 | 事件 |
|---|---|
| 2026-10-29 23:24 | commit `6c6d64a`：v2.5.3 修「暫停農場導致崩潰」 |
| 2026-10-29 23:25 | Steam Workshop 更新 2.5.3（ManifestID 1710856574140546396） |
| 2026-11-01 04:48 | saqlain141：Ember-pelts 不能用 |
| 2026-11-01（無顯示時間） | pastebin 錯誤日誌張貼 |
| 2026-11-02 ~12:16–13:16 | merlin4059（暫停 crash + upkeep）、Annastasya（致命 crash + No Connection） |
| 2026-11-02 ~20:27 | Eltharion（維護自動化） |
| 2026-11-02 21:16 | 截圖擷取 |
| 2026-11-02 | 本維修文檔調查（唯讀） |
| 2026-11-02 22:56 | **v2.5.4 維修輪發佈**：Steam Workshop ManifestID `2037025195551276545`、GitHub `2a4a237`（見 §10、`VERIFICATION.md` §37） |

> **推論**：這些留言應是針對 **2.5.3**（除非玩家未重啟遊戲/未更新）。`merlin4059` 的「crash on pause」尤其需要玩家 log 才能確認是否為 2.5.3 的殘留路徑（見 RE-04 判別步驟）。

### 1.4 既有驗證狀態（避免把「已修/已驗證」誤報成 bug）

| 事實 | 狀態 | 出處 |
|---|---|---|
| 2.5.3 暫停當下崩潰修復（活動中暫停/恢復） | **已實機驗證** | `docs/VERIFICATION.md` §34、`docs/in-game/pause-fix2-A-*`、`pause-fix3-*` |
| 2.5.3 之後暫停殘留（暫停存檔載入 R1） | **專案日誌已證實存在**（不是玩家 log） | `docs/in-game/pause-fix3-A2-Player.log:2931/2955/2958` |
| 管道自動補水正路徑 | **從未實機驗證**（文件自承、TC-06 未跑） | `docs/VERIFICATION.md:1009-1012`、`docs/workstreams/compat-01-adapters.md:384` |
| 維護材料扣料、人工補水正路徑 | **從未實機驗證** | `README.md:131`、`docs/VERIFICATION.md:783,1223-1226` |
| 屋頂疊放 A/B、真實建造菜單、面板互動、100 棟效能 | 未驗證/需可互動會話 | `README.md:126-133` |
| L1 測試 | 613 PASSED / 0 FAILED | `README.md:19`、`docs/v2/CHANGELOG.md` |
| Harmony 補丁 | **0**（v2 承諾） | `README.md:8`、程式碼/DLL 字串掃描 |

---

## 2. 調查方法與證據等級

### 2.1 方法

本輪由 5 條獨立、唯讀的調查組成（每條只寫自己的證據檔）：

1. **維護/保養自動化**（`maintenance.md`，F-MAINT-01..08）：完整呼叫鏈、日 tick、停擺數學、水三路徑、面板/圖示/日誌、config、L1 契約、實機驗證缺口。
2. **崩潰**（`crash.md`，F-CRASH-01/02）：用 `tools/ApiIndex` 對安裝版 1.1.2.8 Managed DLL 做 IL dump（`WorkerRootBehavior.Decide`、`Enterer/Enterable`、`PausableBuilding` 等）；掃描本機 71 個工作坊 mod 與 296 支 DLL；重讀專案既有實機日誌。
3. **管道 No Connection**（`pipes.md`，F-PIPE-01..05）：掃 HDBF/三語本地化/TimberPipes 11.4.0/全部工作坊 DLL/遊戲 DLL/resources.assets 字串；用 Mono.Cecil 讀 TimberPipes metadata/IL（`PipeRegistry.Tanks`、`TankPipe`/`PipeTank`/`BuildingPipe` 的 `AddTemplateModule2`、`TankPipe.GetBuildingPipeSpec`）；對照既有實機日誌。
4. **Ember-pelts/自訂陣營**（`factions.md`，F-FACTION-01..10）：IL 證實建造選單＝`Common` + `FactionSpec.TemplateCollectionIds`；官方 `Blueprints.zip`；本機 6 個以上 mod 的自訂陣營做法；HDBF 藍圖/材質/存檔路徑。
5. **潛在 bug 與未來回報**（`latent-future.md`，F-LATENT-01..19）：全 `src/`、`data/`、`docs/` 唯讀審查，逐條標證據等級與驗證方式。

### 2.2 證據等級（本文件全篇使用）

| 標記 | 意思 |
|---|---|
| **已證實（程式碼/IL/日誌）** | 由原始碼、真實遊戲 IL、專案日誌或資料檔可直接驗證，不需開遊戲 |
| **高度可能** | 機制已證實、多條間接證據指向，但缺最後一次實機/玩家端確認 |
| **推測** | 需實機或第三方模組行為才能確定 |
| **設計如此** | 與需求/設計一致，但可能就是玩家體驗落差來源 |

### 2.3 本輪限制（誠實聲明）

- **沒有玩家的 `Player.log`、存檔、mod 清單**：RE-03 的 patcher 身分、RE-04 的 merlin4059 版本、RE-05 的實際建築配置都無法 100% 定案。
- **本機沒有 Emberpelts 主模組**（Steam `3346318229` 不在本機工作坊內容）：RE-06 只能以遊戲機制＋生態系做法推導。
- **本輪未開遊戲**：所有「玩家看到什麼」都是從程式碼/IL/既有日誌推得；面板互動、世界圖示、100 棟效能仍未實機覆蓋。
- **2.5.4 實作輪同樣未開遊戲**：只完成可離線證明的程式碼、L1 契約與文案；實機 fixture（§7.2）仍全部 pending。
- **0 Harmony 是硬約束**：不可用補丁去修別人的補丁，也不可補丁原版 `WorkerRootBehavior.Decide`。

---

## 3. 已回報問題（現象 / 原因 / 解決方案）

### 3.1 總覽

| 編號 | 玩家 | 問題 | 類型 | 優先級 | 狀態（2.5.3） | 狀態（2.5.4） |
|---|---|---|---|---|---|---|
| RE-01 | Eltharion | 維護只能手動；逾期後按維護仍「什麼都不發生」 | Bug + 設計落差 | **P0** | 未修 | ✅ 已修（§3.2） |
| RE-02 | merlin4059 | 比佛不會做 upkeep | 設計落差（無工人維護/補水行為） | P1 | 未修（設計如此） | 🟡 自動維護已補（§3.3） |
| RE-03 | Annastasya | 盒子農場運作數分鐘後致命崩潰（NRE，beaver Decide） | 第三方補丁 × HDBF 狀態 | **P0（給玩家）** | 未修（HDBF 無法補丁；可收斂狀態＋診斷） | ⏸ pending（§3.4） |
| RE-04 | merlin4059 | 暫停時 crash（2.5.3 後仍回報） | 2.5.3 修復的殘留時序缺口 | **P0** | 部分修復（R1/R2 殘留） | ✅ 已修（R1/R2/R3/R4；實機 pending）（§3.5） |
| RE-05 | Annastasya | 已接管道仍顯示 No Connection | Bug（語義/判定） | **P0** | 未修 | 🟡 原因碼/文案/probe；真實連通 pending（§3.6） |
| RE-06 | saqlain141 | Ember-pelts 陣營不能用 | 功能缺口（未支援自訂陣營） | P1 | 未支援 | ⏸ pending（§3.7） |
| RE-07 | Eltharion | 建議：自動維護選項 | 功能建議 | P0（與 RE-01 同修） | 未實作 | ✅ 已完成（自動維護開關）（§3.2） |

### 3.2 RE-01 — 維護要手動；「按了維護還是什麼都不發生」（Eltharion）

> **維修狀態（v2.5.4）**：✅ **已完成**。方案 A「追平付費」＋未到期禁用（L-08）＋方案 B 自動維護開關（WP-02）皆已實作，面板/tooltip 與行為一致；缺料判定改用本次追平總帳單（L-07）。釘死測試見 §7.1/§10；**實機情境 C（手動對照）pending**。

#### 現象

- 玩家必須自己在面板按「執行維護」（`Perform maintenance`），不按就永遠不會維護；逾期 1 期 `G_maint=0.5`，連續 ≥2 期**停擺**（停止播種/收穫，作物仍以 0.05 倍慢速生長）。
- **按一次按鈕不一定恢復**：逾期 ≥2 期時，按一次「執行維護」成功扣料、畫面顯示「維護完成」，農場**仍在停擺**；隔天面板逾期數又回到 2。
- 面板同時顯示「逾期週期 0」與紅色「維護逾期／停擺」；tooltip 說「重置維護倒數」「執行維護可恢復產出」，與行為不符。
- 基礎檔（Basic）沒有材料維護，但面板仍有一顆永遠按不下去的「執行維護」按鈕 → 玩家誤以為「這遊戲要我手動維護」。
- 「什麼都不發生」還有第二種來源：**水罐見底**（基礎檔 3.6 單位 ≈ 3 個滿載日；手動補水一次只補 1/3 罐 ≈ 1 天）→ 生長 `G_irr=0`、播種停止。

#### 原因（逐條附證據）

| # | 原因 | 證據 | 等級 |
|---|---|---|---|
| 1 | **維護 100% 手動**：唯一路徑＝面板按鈕 → `HdbfFarmCommandService.TryPerformMaintenance` → 扣料 → 推進到期日；日 tick 只讀週期、不扣料不重置 | `src/HDBF.UI/Panels/HdbfFarmPanelFragment.Build.cs:158-166` → `.Commands.cs:138-164` → `src/HDBF.Gameplay/Components/HdbfFarmCommandService.cs:76-117`；`src/HDBF.Core/Indoor/IndoorDayTick.cs:139-144,219-248`；L1 `V2MaintenancePowerTests.cs:70-77` 明文「the tick never performs the maintenance」 | 已證實 |
| 2 | **逾期追平 bug**：`TryApplyMaintenance` 只做 `nextDue = 舊 dueDay + 1 週期`（不是 `today + 週期`），所以逾期 k 期按一次只減 1 期；停擺門檻是 2 期 | `Commands.cs:244-273`（`nextDue` 在 :254-258、`cyclesMissed = 0` 在 :266）；`IndoorDayTick.cs:139-155,219-248`；`BalanceFormulas.MaintenanceCyclesMissed` | 已證實 |
| 3 | **面板數字自相矛盾**：apply 時把 `MaintenanceCyclesMissed` 立即歸零，但警告列用上一日日 tick 的 `_lastWarnings`（舊 flags）；隔天 tick 又算回 2 | `HdbfBoxFarmController.IndoorSnapshot.cs:65,76`；`IndoorDayTick.cs:220-224` | 已證實 |
| 4 | **tooltip/文案與行為不符**：三語 `HDBF.Tooltip.Maintenance`＝「重置維護倒數」、`HDBF.Tooltip.MaintenanceShutdown`＝「執行維護可恢復產出」 | `data/Localizations/{enUS,zhCN,zhTW}.csv`（`HDBF.Tooltip.Maintenance`/`MaintenanceShutdown`） | 已證實 |
| 5 | **維護狀態不可見**：維護卡是面板第 5 張卡（1080p 需滾動）；世界圖示每棟只顯示 1 個且**水優先**（同時缺水＋停擺時看不到維護）；無通知/音效 | `src/HDBF.UI/Panels/HdbfIndoorPanelFactory.cs:92-104`；`src/HDBF.Core/Indoor/IndoorWarnings.cs:91-119`；全 `src/` 無 Notification/Toast/Popup 消費者 | 已證實 |
| 6 | **Basic 仍顯示停用的維護按鈕**（`IrrigationOnly` → 按鈕 disabled，不隱藏） | `src/HDBF.Core/Balance/TierCatalog.cs:120-141`；`src/HDBF.UI/Panels/HdbfIndoorPanelFactory.cs:214-228`、`HdbfIndoorPanelFactory.cs` 按鈕建立處；三語 `HDBF.Error.MaintenanceUnavailable` | 已證實 |
| 7 | **水也是手動維持**：無管道時每趟 1/3 罐；12 格內可達水源限制；瞬時補水無工人動畫 | `src/HDBF.Gameplay/Components/HdbfBoxFarmController.ManualRefill.cs:27-134`；`src/HDBF.Core/Indoor/IndoorConstants.cs:83-90`；`src/HDBF.Gameplay/Components/HdbfManualWaterSource*.cs` | 已證實 |
| 8 | **維護/補水正路徑從未實機驗證**：若「最近庫存扣料」或「12 格水源探測」在實機失效，玩家會遇到按鈕按了沒反應／永遠缺水源 | `README.md:131`、`docs/VERIFICATION.md:783,1223-1226` | 已證實（未驗證事實本身） |

#### 解決方案（保留 §19.8 數值與代價，不改平衡）

**A. 先修「按了沒用」（WP-01，P0，改動小）**

- 把 `TryApplyMaintenance` 改成「**追平付費**」：一次點擊付 `k` 期材料（`k = 1 + floor((today - dueDay)/cycle)`），`dueDay += k × cycle`，`cyclesMissed = 0`。材料不足 `k` 期 → 拒絕並顯示「需要 24 木板（3 期）」，不扣任何料。
- 或採「重置語意」`dueDay = today + cycle`（較符合現有 tooltip 直覺，但逾期很久只付 1 期；推薦追平付費）。
- 修掉「先歸零再被 tick 算回」：面板「逾期週期」改由即時 `cycles` 計算或等 tick 更新；停擺時顯示「需再維護 n 期」。
- 三語 tooltip 改成與行為一致；停擺時面板明確寫「再按 a 次、共需 b 材料」。
- 選項：`dueDay` 尚在未來時禁用按鈕（避免連點兩下多付一份材料，F-LATENT-08）。

**B. 自動維護開關（WP-02，P0，對應 Eltharion 建議與 merlin「beavers won't do upkeep」）**

- 每棟農場持久化 `AutoMaintenance`（bool，預設來自 `features.autoMaintenance`，建議 **true**）；舊存檔缺欄位 → 用 config 預設，且不重排 `dueDay`（不會免費維護）。
- 觸發：每日 `RunDailyTick` 在 `IndoorDayTick.Run` **之前**：`AutoMaintenance && 需要材料 && dueDay <= today` → 用 Core 純函式算 `k` 期帳單 → 從最近已完工地區庫存**一次扣足**（all-or-nothing）→ `dueDay += k × cycle`。
- **付的料與手動完全相同**（不另外加動作成本）→ 不破壞 §19.8 經濟與「代價」設計；缺料時不扣、不推進，寫 `indoor.maintenance-auto-missing`，面板沿用 `MissingMaterial` 紅字，手動按鈕仍可用。
- 現成 seam：`IMaintenanceMaterialSource.TryConsume` 已實作但生產程式碼零呼叫（`src/HDBF.Core/Indoor/IMaintenanceMaterialSource.cs:13-27`、`IndoorMaintenanceMaterialSource.cs:87-132`）；扣料語意與 `HdbfMaintenanceTransaction` 共用。
- Basic（`IrrigationOnly`）與接電頂級（`cycleDays<=0`）不動作（與手動一致）。
- UI：面板加「自動維護」勾選＋下次到期＋「本次會追平 k 期」提示；三語鍵同步。
- 日誌鍵：`indoor.maintenance-auto`、`indoor.maintenance-auto-missing`。

**C. 讓玩家一秒知道「為什麼不動」（WP-06，P1）**

- 面板頂部常駐狀態列：`運作中 / 缺水（剩 0.8 日）/ 維護停擺（逾期 2 期，需再維護 1 次）/ 已暫停 / 無工人 / 入口堵塞 / 輸出滿 / 休耕中`（由 Core 純函式 `IdleReason` 產生，L1 可測）。
- 維護卡移到水卡之前（或併入首卡）；世界圖示允許「水＋維護」輪播（需實機確認 vanilla `StatusIconCycler`）。
- 缺水列直接寫明三條補水路徑與最近水源距離（見 L-03）。
- Basic 隱藏維護按鈕（或改成一句被動說明「本檔位免材料維護，請注意補水」）。

**D. 驗證**

- L1：`TestCatchUpCyclesMatrix`、`TestSingleMaintenanceCatchUpClearsStall`、`TestOneCycleBillCannotClearTwoMissedCycles`（回歸釘死舊 bug）、`TestAutoMaintenancePaysSameBillAsManual`、`TestAutoMaintenanceSkipsWithoutMaterial`、`TestAutoMaintenanceSettingRoundTripsThroughSave`、`TestBasicTierHidesMaintenanceAction` 等（完整清單見 `maintenance.md` §6）。
- 實機：Intermediate 逾期 3 期 + 倉庫 24 木板 → 一次維護後 `dueDay` 前進 3 期、解除停擺；缺料 8 木板 → 拒絕且不扣；Basic 面板無維護按鈕；人工補水正路徑（12 格內水塔）首次取證。

### 3.3 RE-02 — 「我的比佛不做 upkeep」（merlin4059）

> **維修狀態（v2.5.4）**：🟡 **部分完成**。短期方案（WP-02 自動維護＋工作坊文案改為「perform maintenance — or enable automatic maintenance」）已完成並發佈；**「比佛實際去維護/取水」的行為層（中期方案）⏸ 未做**。

#### 現象

玩家期待看到比佛去維護/取水，但遊戲中沒有這個行為；維護是玩家按按鈕，補水是瞬時生效（只扣 2 個農事動作，沒有動畫）。

#### 原因

- **全模組沒有任何「比佛執行維護/取水」的行為**：維護是面板命令；農事動作只有播種與收穫（`IndoorLaborMath.RunActions`）；補水是瞬時取水（`HdbfBoxFarmController.ManualRefill.cs:88-124`，註解明說「applied the moment the player asks」）。
- 農民在值勤時多半待在室內（`WaitInsideIdlyWorkplaceBehavior`，`src/HDBF.Gameplay/HdbfGameConfigurator.cs:113`），玩家看到的是進出建築，不是「去做維護」。
- 工作坊文案寫「until a beaver performs maintenance」（`docs/workshop/DESCRIPTION.md:56`、`PAGE_COPY.txt:22`）→ 直接暗示比佛會做，**與實作不符**。
- 「upkeep」在玩家語彙裡就是維護；merlin 的字面描述 100% 正確。

#### 解決方案

- 短期：A 方案自動維護（WP-02）讓「農場自己維護」成立；把工作坊文案改為「until you perform maintenance (or enable automatic maintenance)」。
- 中期：若要「比佛真的去做」，可讓 1 名在崗農夫在自動維護日播放原版取水/搬運行為（不改平衡、不加路徑尋路），或至少在面板顯示「今日由農夫完成維護 +k 期」。
- 補水同理：自動補水（WP-06，可選）沿用 12 格探測與 2 動作成本，讓工人行為可見；否則至少顯示「本趟消耗 2 個農事動作」。

### 3.4 RE-03 — 運作數分鐘後致命崩潰（Annastasya）

> **維修狀態（v2.5.4）**：⏸ **pending**。HDBF 仍 0 Harmony、未加 `indoor.behavior-probe` 唯讀診斷（與 G-08 的 source 字串掃描衝突，需另案）；玩家端 mod 二分/隔離流程（方案 3–4）未做。

#### 現象

農場正常運作數分鐘後遊戲致命崩潰（fatal crash），日誌為 `NullReferenceException`，堆疊在 `WorkerRootBehavior.Decide_Patch1`，被 tick 的實體是成年海狸。

#### 原因（已證實部分）

| # | 事實/假設 | 證據 | 等級 |
|---|---|---|---|
| 1 | `Decide_Patch1` 是**第三方 Harmony/MonoMod 補丁**的替換方法；`BehaviorManager.Tick` 也被補丁 | 堆疊語意（`MonoMod.Utils.DynamicMethodDefinition` + `Glue:AbiFixup`）；本機工作坊有 Harmony 2.4.1 | 已證實 |
| 2 | **HDBF 不是 patcher**：v2 全部 0 Harmony、原始碼/DLL 字串皆無 `WorkerRootBehavior` | `README.md:8`；`src/` 全搜尋 0 命中；已發布 `HDBF.Gameplay.dll` 字串掃描 0 命中 | 已證實 |
| 3 | **T3MP 1.2.5 不是 patcher**：以 IL 取證只補 TickAll/UpdateVisit 等 tick frontier 面，不含 Decide/BehaviorManager | `feedback-20261102/crash.md` §1.3 A-2；本機 `3756656421/Code.dll` IL dump | 已證實（本機版本） |
| 4 | 本機 71 個已啟用 mod 都不 patch `WorkerRootBehavior` | 296 支工作坊 DLL 字串掃描 0 命中（Managed 對照有命中） | 已證實（本機 mod 集） |
| 5 | **patcher 身分未確定**：玩家必定還有一個「不在本機清單」的 Harmony mod，或某 mod 的版本與本機不同 | 上述掃描的缺口 | 未確定 |
| 6 | 原版 `Decide` 的解參考：`_workRefuser`、`_worker.Employed`、`_workerWorkingHours`、`_communityServiceBehavior`；`DecideAsWorker` 再解 `_worker.Workplace`。`WorkAtWorkplace` 有 try/finally 不會被 inline，堆疊缺該 frame → NRE 最可能在**patch 自己新增的程式碼**或 inline 的 `DecideAsWorker` | 1.1.2.8 IL dump（`crash.md` 附錄 A） | 已證實（IL） |
| 7 | **HDBF 只提供「怪狀態」，不提供補丁**：HDBF 讓農民長時間待在室內；暫停時原版只解僱（`Worker.Unemploy`，`Workplace=null`）不帶出 enterer；`WaitInsideIdly` 走路失敗也會解僱。第三方 Decide patch 若假設「employed ⇒ Workplace 非 null / 農民不會在室內被解僱」就會 NRE | `crash.md` §1.5；`HdbfGameConfigurator.cs:113`；`Pause.cs`/`Pause.Eject.cs` | 高度可能（HDBF 是觸發因子） |
| 8 | T3MP 的 inline limit（300）與 tick frontier 可能放大「stale entity tick」（實體已刪除仍被 tick） | `crash.md` §1.4 推論 | 推測 |

#### 解決方案

- **不能**新增 Harmony 去 patch 原版 `Decide`（違反 0 Harmony 與 REQ-V2-SAFE-05，也會與未知 patcher 互相覆蓋）。
- 可做（WP-07，P1）：
  1. **唯讀診斷**：啟動後記一行 `indoor.behavior-probe`（用 reflection 看 `WorkerRootBehavior.Decide` 是否被動態補丁、`MethodInfo.Name` 是否含 `_Patch`、是否載入 Harmony 組件），下次玩家回報可直接確認「Decide 被第三方補丁」，不必猜。不引用 HarmonyLib、不安裝補丁。
  2. **收斂 HDBF 製造的怪狀態**（與 WP-04 共用）：暫停/blocked 農場週期 reconcile、`EntererAdded` 即時撤出、拆除前撤出；讓「已解僱仍留在室內」的窗口最小化。
  3. **玩家端診斷/隔離流程**：先乾淨 profile（只 HDBF）跑 10 分鐘 → 只加 Harmony+T3MP → 再逐步加玩家 mod 全集二分，找出 patcher；收集完整 `Player.log`（開頭 mod 清單）、存檔、操作時機。
  4. **工作坊說明**加註：HDBF 不帶 Harmony，若與其他修改 worker 行為的 mod 同時使用出現 `WorkerRootBehavior.Decide` 崩潰，請提供 log 以定位 patcher。

### 3.5 RE-04 — 暫停 crash（merlin4059，2.5.3 之後仍回報）

> **維修狀態（v2.5.4）**：✅ **已完成 FIX-1/2/3/5**：新增 1 Hz `IndoorPauseReconcileService`（R1）、暫停中 `EntererAdded` 即時撤出（R2）、`UnreserveSlotAndExit`＋`IsInside/CurrentBuilding` 檢查＋`eject-summary`/`eject-failed`/`residual-enterer` 日誌＋`Abandon`+`Remove` fallback（R3/R4）、不補丁不自行 `Unemploy`。FIX-4（already-blocked 唯讀診斷）⏸；**實機 E/F/J pending**。

#### 現象

玩家按暫停（或暫停後/讀取暫停存檔時）遊戲崩潰。2.5.3 已修過一次（「暫停農場導致崩潰」，commit `6c6d64a`，實機驗證 §34），但玩家在 2.5.3 之後仍回報。

#### 原因（2.5.3 修了什麼、還漏什麼）

**2.5.3 已修**：暫停 → `BlockableObject.Block` → `Workplace.OnObjectBlocked` → `UnassignAllWorkers` → `Worker.Unemploy`（**不會** `Enterer.Exit`）；HDBF 是唯一讓農民持續待在室內的農場，殘留 enterer 會撞原版 Enterer/Enterable/Accessible 不變量。修法：暫停時停日 tick/水耗/圖示，並用 `Enterer.Exit` 逐隻撤出（`Pause.cs` + `Pause.Eject.cs`）；實機 `pause-fix2/3` 已驗證活動中暫停/恢復。

**2.5.3 仍漏（殘留路徑）**：

| 編號 | 殘留路徑 | 證據 | 等級 |
|---|---|---|---|
| R1 | **暫停存檔載入**：`EjectBeaversFromPausedFarm` 在 activate 時跑，但 beaver 的 `Enterer` 尚未 restore → 當下 0 人；載入完成後兩隻工人才以「在室內」存在，且 HDBF 不會再收到 `PausedChanged`（同值 early-return）→ 整段暫停期間都在 blocked 農場內，直到恢復才被撤出 | 專案日誌 `docs/in-game/pause-fix3-A2-Player.log:2931`（indoor.paused）→ `:2955`（Load time）→ `:2958`（moved 2）→ `:2959`（resumed）；`Pause.cs:167-172` | 已證實（狀態存在）；是否直接造成玩家 crash＝高度可能 |
| R2 | 暫停瞬間「正在走進農場」的 beaver 不在 snapshot；`Enterable.CanEnter` 只看 Enabled/容量，**blocked 不影響** → 他照樣走進 blocked 農場，HDBF 不會 reconcile | 1.1.2.8 IL（`crash.md` §1.5 C-4、§2.2） | 高度可能 |
| R3 | `Enterer.Exit()` 對半狀態 enterer（`CurrentBuilding == null`）是 no-op → 永久卡在集合內 | IL（`crash.md` §1.5） | 推測（路徑已證實） |
| R4 | 撤出用 `Exit()` 而非 `UnreserveSlotAndExit()`，可能留下 reservation | `Pause.Eject.cs:68` | 推測 |
| R5 | `Deactivate()`/`DeleteEntity` 不撤出（依賴原版 `Enterable.OnExitFinishedState → ForceRemoveEveryone`） | `Activation.cs:100-146`；IL 證實 vanilla fallback 存在 | 已證實（vanilla fallback） |
| R6 | 建築「本來就 blocked」時，暫停只加第二個 blocker、不發 `ObjectBlocked` → worker 不被解僱，仍 employed 在 blocked workplace | `crash.md` §2.2 R6（IL） | 已證實（機制）／推測（玩家是否踩到） |
| R7 | 多人/多農場/Bot：未見具體風險 | — | 待驗 |

#### 解決方案（WP-04，P0）

1. **FIX-1（R1，最高優先）**：不要在 activate 當下只做一次撤出；改成 session singleton（`IUpdatableSingleton`，~1 Hz）持有 `_paused` 農場清單，載入完成後下一拍再 reconcile 一次；或至少在 `PostLoadEntity` 後延遲一幀撤出。新增日誌 `indoor.paused.reconcile`、`indoor.paused.residual-enterer`。
2. **FIX-2（R2）**：暫停中訂閱 `_enterable.EntererAdded`，一有人進入 blocked 農場就撤出（與 FIX-1 雙保險）。
3. **FIX-3（R3/R4）**：撤出前檢查 `enterer.IsInside && CurrentBuilding == _enterable`；改用 `UnreserveSlotAndExit()`；`Exit` 後檢查 `NumberOfEnterersInside` 是否下降，未下降 → 記 `eject-failed`，必要時 `ForceRemoveEveryone()` fallback；每次撤出記 `eject-summary`（requested/ejected/skipped/remaining），讓玩家 log 可判讀是否漏人。
4. **FIX-4（R6，選配）**：暫停 reconcile 時記 `indoor.paused.already-blocked` 唯讀診斷，區分「pause 造成 blocked」與「本來就 blocked」。
5. **不做**：不自行 `Worker.Unemploy`、不補丁 `Workplace.AssignWorker`（原版已自行 catch+log）。

**判別玩家是哪一種（給客服/回覆）**：
- 先看 `Player.log` 的 `start HDBF version=` 是否 2.5.3；沒有 `indoor.paused`/`indoor.resumed` 行 → 不是走 HDBF 暫停整合或 crash 在 pause 之前。
- 有 `indoor.paused` 但**沒有** `moved N` 行，且前後有 `Load time` → **R1（暫停存檔載入）**。
- Crash stack 是 `NullReferenceException` in `WorkerRootBehavior.Decide_Patch1` → **F-CRASH-01（第三方補丁）**，不是 2.5.3 暫停修復的回歸。
- Stack 是 `InvalidOperationException`/`ArgumentException`（`Can't remove enterer … not inside`、`tried to enter … already inside`、`Sequence contains no elements`、`Can't assign … isn't understaffed`）→ 暫停殘留 R1/R2/R3/R6。

### 3.6 RE-05 — 已接管道仍顯示「No Connection」（Annastasya）

> **維修狀態（v2.5.4）**：🟡 **部分完成**。方案 1（文案＋原因列）✅、方案 2（`IIndoorPipeProbe` 7 原因碼＋`water.pipe-probe` 日誌）✅、方案 6（玩家繞過文案）✅；**方案 3（`PipeRegistry.TryGetGraph` 真實連通判定）⏸ pending、方案 4（讓農場成為 Timber Pipes 端點）⏸、方案 5（實機 G/H/I）pending**。

#### 現象

玩家把管子接到盒子農場（或附近）後，面板仍顯示管道接口「未連接」（英文 `Not connected`，玩家轉述為 "No Connection"），且不會自動補水。

#### 原因

**「No Connection」字串來源（已證實）**：不是 Timber Pipes、不是原版。HDBF 三語 `HDBF.Port.NotConnected` = `Not connected` / `未連接`（`data/Localizations/enUS.csv:81`、`zhTW.csv:81`），由面板 Ports 卡顯示：`PortRow(snapshot.PipePort, snapshot.WaterSupply == WaterSupplyKind.Pipe, ...)`（`src/HDBF.UI/Panels/HdbfIndoorPanelFactory.Cards.Output.cs:109-110,133-148`）。對 HDBF、TimberPipes 11.4.0、296 支工作坊 DLL、497 支遊戲 DLL、`resources.assets` 做字串掃描，**都沒有**精確字串「No Connection」→ 玩家是轉述面板。

**根因鏈（已證實）**：

```
面板 Pipe port = 未連接
 ⇐ snapshot.WaterSupply != Pipe
 ⇐ IndoorAdapterHost.IsPipeConnected == false
 ⇐ TimberPipesWaterSource.HasNetwork == false
 ⇐ 全 settlement PipeRegistry.Tanks 中沒有「finished && Pipe.Graph != null && TakesGood("Water")」的 PipeTank
 ⇐ Box Farm 藍圖沒有 TimberPipes 元件（只有純 UI 旗標 HasPipePort:true）；
    而 PipeTank 只會長在「液體 Stockpile」建築（原版 Small/Medium/Large Tank）
```

- **接到農場外殼的管，兩邊都看不到**：TimberPipes 不知道這棟建築有管道端口（沒有 `BuildingPipe`/`PipeTank` 元件），HDBF 的判定也不看農場旁有沒有管（`TimberPipesWaterSource.cs:97-122`、`TimberPipesWaterBinding.cs:228-255`、`Tank.cs:17-45`）→ 永遠 `Not connected`、永遠不自動補水。
- **正確接法（依實作）**：蓋一顆原版液體水塔（Small/Medium/Large Tank），把它接進 TimberPipes 管網並讓它有水；HDBF 會從「任何一顆接上管網的水塔」抽水。**農場不必在旁邊**。
- **反向缺陷**：判定只掃全世界水塔、沒有農場座標/距離/district 條件（`CompatAnchorTable` 已有 `PipeRegistry.TryGetGraph` anchor，但全 `src/` 沒使用）→ 地圖任何角落有接網水塔，所有農場都會顯示已連接並「隔空抽水」（平衡漏洞）。
- **診斷缺口**：v2 水 binding 的 8 個反射成員不在 adapter health 的 4 個 A 級 anchor 內；binding 失敗只回 false、不進三振、無 `compat.failed` → 版本漂移會顯示成「未連接」，玩家/維修者都無法分辨。
- **暫停時 stale**：管道只在啟動/PostLoad/每日 tick 重探；面板其餘時間重用快取；農場暫停 → 日 tick 停止 → 接上管道後可能一直顯示未連接，直到恢復/重載（與 RE-04 共用觸發面，非同一 bug）。
- **從未實機驗證**：§19.6「連接時自動補水」沒有任何實機成功紀錄；既有 fixture 從未建過 PipeTank（`docs/VERIFICATION.md:1009-1012`、`docs/workstreams/compat-01-adapters.md:384`）。

#### 解決方案（WP-05，P0）

1. **先改文案＋原因列（低風險）**：
   - `HDBF.Port.Pipe` → `Pipe network (auto-refill)` /「管道管網（自動補水）」；未連接時改用專用鍵 `HDBF.Port.NoPipeTank` = `No water tank in pipe network` /「管網內沒有可抽水的水塔」（不要與「沒裝模組」共用 `Not connected`）。
   - 新增 tooltip：**「自動補水只從『接上管網的原版液體水塔（Small/Medium/Large Tank）』抽水；把管接在農場外殼不會生效，農場不必在管線旁。」**
   - 在 Water 卡或 Ports 列顯示原因碼：`NoPipeTank` / `NoPipeNetwork` / `TankDry` / `AdapterUnavailable` / `BindingFailed`。
2. **診斷（低中風險）**：`IIndoorAdapterHost` 增加唯讀 `PipeProbe` DTO（tank 數、graph 數、有水數、binding 是否失敗、原因字串），水 binding 失敗時回報 `ReportRuntimeFailure("water-anchor-...")`，讓 `compat.degraded/failed` 可在日誌與面板看到；新增 `water.pipe-probe` 節流日誌。
3. **真實連通判定（中風險）**：用既有 anchor `PipeRegistry.TryGetGraph(Vector3Int, out PipeGraph)` 在農場 pipe port 座標判連通（先在 `HdbfBoxFarmSpec` 加 port offset/方向資料）；`graph == null` → `NoPipeNetwork`；graph 存在但無符合條件 tank → `NoPipeTank`；有 → `Connected`。這同時關掉「隔空抽水」（或至少在 UI/文件明示「全域供水」是設計）。
4. **完整端點方案（P2，設計決策）**：讓農場成為真正 TimberPipes 端點（藍圖加 `BuildingPipeSpec`／`PipeTank`）。風險：缺 TimberPipes 時未知 ComponentSpec 可能解析失敗，破壞「無前置依賴」與 validator；需 optional blueprint injection／分流藍圖 + ADR，不建議本輪。
5. **實機驗證（P0，所有修復的前提）**：見 §7.2 的 A–E 場景（農場旁鋪管但不接水塔 / 正確接法 / 斷管 / 版本漂移）；同時驗證 `container.GetInstance(PipeRegistry)` 在遊戲內是否成功（若失敗，**所有玩家**的自動補水都不會生效）。
6. **玩家臨時繞過（可寫進回覆）**：接原版液體水塔進管網；或用單向閥（one-way valve）出水端對準農場勾 Fill（推測可把水 goods 灌進農場 `Inventory:Water`，此時面板可能仍顯示未連接＝顯示 bug）；或用手動補水/搬運工。

### 3.7 RE-06 — Ember-pelts（自訂陣營）不能用（saqlain141）

> **維修狀態（v2.5.4）**：⏸ **pending**（WP-13 未做；需安裝 Emberpelts 主模組 `3346318229` 才能驗證 M0）。

#### 現象

玩家在 Emberpelts 陣營開局，盒子農場「不能用」——最可能是建造選單**完全看不到**四檔建築；若透過其他方式出現，可能建造選單載入失敗或作物/陣營顯示錯誤。

#### 原因

| # | 原因 | 證據 | 等級 |
|---|---|---|---|
| 1 | **建造選單只載入 `Common` + 當前陣營的 `TemplateCollectionIds`**（`FactionTemplateCollectionIdProvider` → `FactionService.Current.TemplateCollectionIds`） | 1.1.2.8 IL（`factions.md` §2.1） | 已證實（遊戲機制） |
| 2 | **HDBF 只註冊 `Buildings.Folktails` / `Buildings.IronTeeth`** 兩個 collection；Emberpelts 的 collection（生態系慣例 `Buildings.Emberpelts`）沒有 HDBF 建築 → 選單看不到 | `data/TemplateCollections/` 只有兩份；`docs/VERIFICATION.md:194-196` | 已證實（HDBF 資料） |
| 3 | **即使裝 `ModdedFactionsBuildingSupport` 也不會出現**：它依賴 `Buildings.Support.ModdedFactions` collection id，HDBF 沒註冊 | HDBF `data/` 0 命中；本機 Conveyor Belts v2/Timber Pipes 有該 id | 已證實（HDBF 資料） |
| 4 | **若靠 clone/手改檔把建築塞進 Emberpelts → 材質坑**：HDBF 模型使用 faction-scoped 原版材質（`BaseWood_Brown.Folktails` 等）；`MaterialRepository.GetMaterial` 找不到材質會丟 `ArgumentException: Material ... not found in repository`，既有實機證據顯示會讓**整個建造選單**載入失敗（不只一棟） | `factions.md` §2.4；`docs/API_FACTS.md` §8、`docs/VERIFICATION.md` §9；`.timbermesh` 材質字串 | 已證實（機制與失敗樣態） |
| 5 | **若建築真的出現，陣營語意仍錯（bug 群）**：`ResolveFaction` 只認 IronTeeth，其餘一律 Folktails；控制器在 Awake 定死 `_faction`；存檔載入時用藍圖解析值**強制覆寫** record 的 Faction；`BuildTable` 先用官方 fallback 作物表打底（Emberpelts 多為新作物）；UI 固定作物下拉/警告用 `FallbackCropCatalog` 而非 live catalog | `GameCropCatalog.cs:120-129`、`HdbfBoxFarmController.cs:163`、`IndoorEntitySaveBridge.cs:180-185`、`.Keyed.cs:239-243`、`GameCropCatalog.SpecReader.cs:91-121`、`HdbfIndoorPanelWarnings.cs:164,180` | 已證實（程式碼） |
| 6 | **好消息**：`Log/Plank/MetalBlock/Water` 都在永遠載入的 `GoodCollection.Common`；`WorkerTypeSpec` 無 faction 欄位、`Beaver` 是全域 spec → **建造材料、水貨物、就業本身不會因陣營失效**（水「來源」另需實機） | 官方 `Blueprints.zip`；`factions.md` §2.3/§2.6 | 已證實 |
| 7 | Emberpelts 內部資料（`TemplateCollectionIds`/`MaterialCollectionIds`/作物 `ResourceGroup`/水源建築）本機沒有主模組，無法直接讀 | — | 推測/待驗 |

#### 解決方案（WP-13，P1；分層）

**M0 最小可行（不依賴 Emberpelts 也能離線做大部分）**：
1. 資料：新增中立變體藍圖（使用 `Common` 材質或陣營中立材質）；新增 `Buildings.Emberpelts`（或 `Buildings.HDBF`）collection，內容指向中立變體；用 `.optional.blueprint.json` 把 collection id append 到 `Factions/Faction.Emberpelts.blueprint.json`（`.optional` 語意已用 IL 證實：只會在同名原版 blueprint 存在時套用，前提是 Emberpelts 存在）。
2. 程式：`ResolveFaction` 改為「以當前 `FactionSpec.Id` 對照，未知陣營→保留/傳遞字串而非硬映射 Folktails」；作物表改「只以當前 template collection 的 live 作物為準」（不先鋪官方 fallback）；UI 固定作物/警告改吃 snapshot 帶來的 live 作物清單；存檔保留未知陣營 id（schema 決策：是否升版、如何相容）。
3. 文件：工作坊頁面標明「官方陣營原生支援；自訂陣營需該陣營 mod 或支援外掛」（在 M0 上線前）。

**M1 完整支持任意自訂陣營**：
- 用遊戲現成擴充點 `ITemplateCollectionIdProvider` 多綁定（IL 已證實是官方 MultiBind）：HDBF 自寫 provider，對「非 Folktails/IronTeeth」的當前陣營自動注入 HDBF 的中立 collection；搭配中立材質模型與資料驅動陣營，不需為每個陣營 clone 藍圖。
- 需 ADR（存檔 faction 欄位、材質、模板名、`BackwardCompatibleTemplateNames`）與 Emberpelts 實機驗證。

**驗證限制**：必須安裝 Emberpelts 主模組（`3346318229`）才能確認其 `FactionSpec`/材質/作物/水源；在那之前，M0 的中立藍圖可先用「官方陣營 + 中立材質」離線驗證（validator + L1 + in-game 建造菜單）。

### 3.8 玩家建議（非 bug，但屬維修範圍）

| 建議 | 來源 | 對應 | 狀態（v2.5.4） |
|---|---|---|---|
| 自動維護選項（可開關） | Eltharion | WP-02（付料與手動相同） | ✅ 已完成（WP-02） |
| 比佛做 upkeep / 看得到維護行為 | merlin4059 | WP-02 + WP-06（行為呈現） | 🟡 自動維護已補；可見的工人維護/取水行為未做 |
| 正確接管道就能自動補水 | Annastasya（期望） | WP-05（文案+端點方案） | 🟡 原因碼/探測/文案已修；端點與真實連通判定 pending |
| Ember-pelts 支援 | saqlain141 | WP-13 | ⏸ pending（WP-13） |
| （潛在需求）一鍵補水全部農場、面板告訴我為什麼不動、材料分散也要能維護 | 由本輪潛在 bug 推得 | WP-06、WP-09 | 🟡 面板原因與材料一致已做；一鍵補水全部農場 pending |

---

## 4. 潛在 bug（未回報，但已查證；每條含現象/原因/解決方案）

> 以下 19 條來自 `feedback-20261102/latent-future.md`；另外補入 crash/pipes 調查的交叉項（不重複列已知 4 條）。**標記「已證實」= 程式碼路徑可直接確定；「推測」= 需實機/玩家存檔。**

### 4.1 P0 潛在 bug（未回報但代價最高）

#### L-01【已證實｜P0】休耕→常規的第一個日 tick，整盒成熟作物以 0 產量被清空 → ✅ **已完成（2.5.4）**：`TryHarvest` 零產量不清槽＋轉換 scale 修正；`V2FallowTransitionTests` 釘死。

- **現象（玩家語氣）**：「我把農場切休耕讓土休息，過幾天切回常規——可收穫 60 瞬間變 0，倉庫一件作物都沒有，作物去哪了？」
- **原因**：
  1. 休耕完全禁止農事（含收穫）（`IndoorDayTick.cs:187`），成熟作物留在槽裡；
  2. 切回常規時 `ModeTransitionPrevious` 回傳 `Fallow`（`HdbfBoxFarmController.Tick.cs:144-154`），`ModeTransitionYieldScale(Normal, Fallow)` 因 Fallow 產量=0 → scale=0（`IndoorGrowthMath.cs:114-130`，沒有 `lower<=0` 保護）；
  3. `HarvestItems(..., yieldScale=0)` 回 0（`IndoorGrowthMath.cs:94-104`），但 `TryHarvest` 在 `amount==0` 時**仍清空槽位、記疲勞、回報收穫成功**（`IndoorLaborMath.Scheduling.cs:27-61`）→ 日誌 `harvested=N`、產物 0。
- **影響**：整盒成熟作物無聲消失（資料/產出損失、必踩、無錯誤訊息），玩家會以為「休耕會吃掉收成」。
- **解決方案**：
  - 短期（一行級）：`TryHarvest` 在 `amount <= 0` 時**不得清空槽位**（視為 blocked/跳過）；`ModeTransitionYieldScale` 在 `lower <= 0` 時回傳 1（休耕本來就不產出，不需再跌一次）。
  - 長期：把「休耕是否允許收穫成熟作物」補進需求；面板/工具提示寫明「休耕期間不收穫」；切模式時若場上有成熟作物給確認/警告。
- **驗證**：L1 `TestFallowExitTransitionDayDoesNotDestroyMatureCrops`（30 成熟槽、Mode=Normal、Previous=Fallow → 斷言輸出 ≥ 期望件數；現行會 0）；實機用 fixture 存檔切休耕 1 天再切回，看 `indoor.day: harvested=` 與倉庫數量。

#### L-02【已證實｜P0/P1】Timber Pipes 的「已連接」是全域假訊號：隔空抽水、接了也不算連、模組改版全場斷水 → 🟡 **部分完成（2.5.4）**：原因碼/probe、端口不覆寫、文案已修；全域抽水解耦、3-strike 時間窗、`TryGetGraph` 真實連通判定 pending。

- **現象（玩家語氣）**：(a)「我農場旁邊根本沒管線，面板卻顯示管道已連接，水一直自動補——這不科學吧？」(b)「我把管接到農場，它還是說沒連接」（= RE-05）；(c)「Timber Pipes 更新後，我所有盒子農場同一天全部沒水。」
- **原因**：
  - 管道水源列舉的是 **全 settlement `PipeRegistry.Tanks`**，只過濾 finished + HasPipeGraph + TakesWaterGood，**沒有農場座標/距離/district 條件**（`TimberPipesWaterSource.cs:97-122,229-259`、`TimberPipesWaterBinding.cs:228-255`、`Tank.cs:17-45`）→ 任何遠方接網水塔都能被抽（平衡漏洞）。
  - 反之農場不是 PipeTank，接在農場外殼的管兩邊都看不到 → 永遠 Not connected（RE-05 主因）。
  - 水 binding 的 8 個反射成員不在 adapter health 的 4 個 A 級 anchor；版本漂移 → 每次抽水 FailDraw → 3 次失敗（無時間窗）→ session-wide `Failed` → 所有農場同日斷水，且面板可能仍顯示「已連接／來源：無」。
- **影響**：水資源平衡被繞過；一次外部模組更新讓全場斷水；玩家無法理解。
- **解決方案**：同 RE-05（真實連通判定 `TryGetGraph`、probe 診斷、端點方案）；另外把「adapter Failed 與單農場斷水解耦」、3-strike 改成有時間窗/每日重置；短期至少把全域抽水在 UI/文件寫明為設計。

#### L-03【已證實｜P0（設計代價+UX）】水罐 3–4 天見底後農場「靜默停工」，三條補水路徑沒揭露 → 🟡 **部分完成（2.5.4）**：面板狀態列、三條補水路徑、0 工人「—」、補水 headroom 已做；水罐容量平衡與低水位自動請求搬水 pending（設計決策）。

- **現象（玩家語氣）**：「農場前三天好好的，第四天開始完全不動：槽位全空、生長卡住，面板只說缺水。我有水塔啊，為什麼它不去拿？」
- **原因**：
  - 水罐容量＝`WaterPerPlotPerDay × PlotCapacity × TankCapacityDays`：Basic 3.6（3 天）、Intermediate 10.8（3 天）、Advanced 33.6（4 天）、Top 76.8（4 天）；連續排水讓罐子每天掉一格（`TierCatalog.cs`、`HdbfWaterFlowService`/`WaterFlow.cs`）。
  - 缺水 → `Coverage=0` → `G_irr=0`（生長凍結）；空罐不播種（`IndoorDayTick.cs:192-193`）；收穫不受水限制但收完就空著。
  - 三條補水路徑各有門檻：管道需 Timber Pipes（且要液體水塔在管網上）、人工補水需 12 格內可達水源、搬水需聚落已有 `Water` 貨物；面板只說「缺水」，沒有告訴玩家下一步該蓋什麼。
- **影響**：早期玩家（還沒抽水站/沒管道/附近沒水）蓋了農場，3 天後變裝飾品；極可能負評/退訂。
- **解決方案**：短期面板缺水列寫明三條路徑＋「距離最近水源：無/12 格」＋藍圖/tooltip 標「滿罐僅夠 3 天」；長期把水輸入庫存鏈接好或低水位自動請求搬水，平衡上考慮 Basic 水罐拉到 4–5 天（需 product 決策）。

#### L-04【已證實（條件式）｜P0 風險】存檔「有修復就整筆拒寫」：某些狀態會靜默不存檔、進度回捲 → ✅ **已修（v2.5.5 離線）**：可修復即寫入修復副本＋`PreviousPayload` 回滾副本（ADR-V2-006）；只有 `CannotWrite` 才拒寫。

- **現象（玩家語氣）**：「存檔再讀檔，農場的作物/水罐回到好幾天前；日誌只有一行 validation failed。」
- **原因**：`IndoorEntitySaveBridge.Save` 在驗證有任何 `Repaired` 時**整筆拒絕寫入**（`IndoorEntitySaveBridge.cs:76-81`）；驗證器會主動修掉未知作物、非正成長時間、`FixedCropId` 不在表、`dueDay=0`、非法 port 等狀態（`IndoorSlotRepair.cs:144-233`、`IndoorSaveFieldRepair.cs:103-160`、`IndoorSaveValidator.cs:157-161`）。若該「被修掉狀態」反覆出現（例如 mod 作物成長時間 0），存檔會每次拒寫 → 進度回捲，且無錯誤對話框。
- **影響**：若觸發＝災難級（數小時進度消失，難自行診斷）；目前無法排除。
- **解決方案**：短期改成「寫入修復後的副本 + 把修復清單寫進日誌/相容性報告」，保留上一份良好快照（先寫 `.prev`）；長期把玩家狀態與暫存狀態分開驗證。
- **驗證**：用 §7 的 net9 harness 造「槽位含未知作物」的 record → `Save` 目前回 false → 再量進度回捲；實機用含未知作物 id 的存檔副本存/讀。

### 4.2 P1 潛在 bug

| 編號 | 現象（玩家語氣） | 原因（已證實程式碼） | 解決方案 | 狀態（v2.5.4） |
|---|---|---|---|---|
| L-05 | 「我按補水，畫面只加一點，但湖邊水位少一截；今天動作還少 2 次。」 | 手動補水在水罐快滿時仍先抽滿 1/3 罐，再 `TryRefill` 依剩餘空間截斷，多餘水直接消失；照樣扣 2 動作（`ManualRefill.cs:92-124`、`IndoorConstants.cs:84-90`） | 先算 `headroom`，只抽 `min(1/3罐, headroom)`；`headroom < 1 單位`視為已滿、不扣動作；面板顯示本次補多少/扣多少 | ✅ 已完成（headroom 上限；TankFull 不扣動作） |
| L-06 | 「我按了管道接口關閉，隔天它自己又打開。」 | `UpdatePorts` 每個日 tick 無條件覆寫 `record.PipePort`（`Context.cs:245-297`）；玩家 toggle 只寫 record，隔日被蓋回，還可能記誤導日誌 | 日 tick 只更新「端口存在/能力」，不覆寫玩家啟用選擇；或把「存在/啟用/實際連線」三態分開 | ✅ 已完成（日 tick 不覆寫玩家端口選擇） |
| L-07 | 「面板說材料夠，按維護卻說不足；我有 8 木板，只是分散在兩個倉庫。」 | 面板/警告的 `Available` 加總所有地區庫存（`IndoorMaintenanceMaterialSource.cs:39-84`），實際交易要求**單一倉庫**整筆（`HdbfMaintenanceTransaction.cs:82-97,109-116`）→ 加總夠但分散就失敗；反之補貨後面板也可能顯示舊缺料 | 把 `Available` 改成「找得到單一庫存能付整筆」或交易支援跨庫拼湊；面板與命令共用同一判定 | ✅ 已完成（單一庫存可付整筆；缺料用追平總帳單） |
| L-08 | 「我手滑點兩下維護，8 木板就沒了，到期日往後跳 20 天。」 | `TryPerformMaintenance` 不檢查是否已逾期、無冷卻/確認；連點＝連付並把 dueDay 往後推（`HdbfFarmCommandService.cs:85-109`、`Commands.cs:213-273`） | dueDay 還在未來時禁用按鈕（或需確認）；同一日去重；面板顯示本次扣料與下次到期 | ✅ 已完成（未到期禁用＋MaintenanceNotDue） |
| L-09 | 「我裝了傳送帶，面板說輸出端口已連接，但帶子不搬作物。」 | `LogisticsConnected()` 只回傳 adapter 探測結果（`IndoorSnapshot.Helpers.cs:14-24`、`IndoorAdapterHost.cs:123-137`），專案沒有把產物寫進輸送帶的程式；`features.logisticsOutput=false`（`HDBF_Config.json:9`） | 短期把「物流接口」改成「僅偵測到模組/不適用」不承諾連接；長期真接輸出端口或文件明說只支援原版搬運 | ✅ 已完成（v2.5.5：端口永不顯示 Connected；`HDBF.Port.LogisticsDetected`＋tooltip；三語） |
| L-10 | 「農場沒工人，面板水跟效率都是綠的，地圖也沒警告，我以為它在運作。」 | 0 工人 → `EffectiveSlots=0` → demand=0 → Coverage=1（`IndoorLaborMath.cs:30-37`、`IndoorWaterMath.cs:51-70`）；世界圖示沒有「無工人」類別（`IndoorWarnings.cs:91-148`），面板與 Icon 的嚴重度模型不一致 | 0 工人時 coverage 顯示「—」或警告；`NoWorkers` 升到 P1 並給世界圖標 | 🟡 面板「—」＋閒置原因已做；世界圖標/嚴重度 pending |
| L-11 | 「面板說缺水，地圖上農場頭上卻沒有圖標；我補完水，圖標又掛一整天。」 | 世界圖示讀 `snapshot.Warnings`（日 tick 快取 `_lastWarnings`，`HdbfWorldIconService.Evaluate.cs:28-31`、`Tick.cs:156-174`），面板讀即時 coverage（`HdbfIndoorPanelWarnings.cs:34-59`）→ 兩者必然不同步 | 圖示改用即時快照推導的四類狀態，或命令成功後立即重算；面板/圖示共用同一 Core 純函式 | ⏸ pending |
| L-12 | 「我裝了作物模組，固定作物只列得出原本幾種；新作物顯示英文 id，還被說無效。」 | 下拉/警告用 `FallbackCropCatalog`（`HdbfIndoorPanelWarnings.cs:159-171,177-206`），但設定命令用 live catalog（`Commands.cs:176-184`）；名稱表只硬編碼 10 個作物（`HdbfIndoorPanelFormat.cs:160-183`），表外顯示 raw id | 面板清單/驗證一律吃 `GameCropCatalog`（snapshot 帶 CropChoices）；表外作物用遊戲本地化名 | ✅ 已完成（v2.5.5：`IndoorFarmSnapshot.CropChoices`＋`CropDefinition.DisplayNameLocKey`，面板 live-first） |
| L-13 | 「我拆掉/搬遷盒子農場，遊戲就閃退。」 | `Deactivate()` 沒有撤離室內工人（`Activation.cs:100-146`）；2.5.3 的撤出只在暫停路徑（`Pause.cs:214,228`）；依賴原版 `Enterable.OnExitFinishedState → ForceRemoveEveryone` 的順序 | 在 `Deactivate()`/`OnExitFinishedState` 前先撤一輪（與 WP-04 共用），失敗只記 Warn；這是 3–5 行的防禦性修復 | ✅ 已完成（Deactivate 前先撤出；併入 WP-04） |
| L-14 | 「蓋到 20 幾座農場後開始卡；打開面板更卡，三倍速更明顯。」 | 每幀 `ConsumeElapsedWater` 內每幀 `ApplyHauledWater()`（含庫存不存在時每幀重掃 `Inventories.AllInventories`、查庫存、每幀成功扣水就 `_saveDirty=true`）；面板 4 Hz 每 16 次讀取觸發 `RefreshWaterSupply` → 25×25=625 格水源圖＋導航可達性；世界圖示每秒對最多 24 座農場取 snapshot（`WaterFlow.cs:25-59`、`WaterSupply.cs:20-27,37-83,94-121,152-243`、`HdbfManualWaterSource.Search.cs:39-154`、`HdbfWorldIconService.Update.cs:59-104`） | 把 `ApplyHauledWater` 移到事件/節流（每 0.5 s 或每 N 幀）；庫存解析失敗加冷卻；`_saveDirty` 只在實際變化時設；水源探測快取延長/按距離提早跳出；加 100 棟 fixture + Profiler 量測 | ⏸ pending |
| L-15 | 「我改了 config 的水罐容量/維護材料，面板顯示跟實際行為不一樣。」 | UI 讀模型用編譯期 `TierCatalog.Get`（`HdbfIndoorPanelFactory.cs:273-284`），模擬用 config 覆寫後的 `TierProfile`（`Activation.cs:225-252`、`HDBF_Config.json` 的 `balance.tier`） | 把解析後的 profile 塞進 snapshot（或 UI 透過 query 取得），加 L1「config 覆寫→面板跟著變」 | ✅ 已完成（v2.5.5：`IndoorFarmSnapshot.Profile`，面板優先它、null 才退回 `TierCatalog.Get`） |

### 4.3 P2/P3 潛在 bug 與工程風險

| 編號 | 現象/問題 | 原因 | 解決方案 | 狀態（v2.5.4） |
|---|---|---|---|---|
| L-16 | 8 份藍圖 `BackwardCompatibleTemplateNames: []`＋歷史佔格變更（3×1×2→3×2×1→3×3×1） | `data/Buildings/Food/BoxFarm/*.blueprint.json:26`；`docs/VERIFICATION.md` §25 已出現過 `not backward compatible. Deleting it.` 實例 | 把歷史模板名補進 backward 清單；`tools/validate` 加硬性檢查「模板名/佔格變更必須反映在 backward 清單」；發佈流程把模板名/佔格變更列為遷移紅線 | ⏸ pending |
| L-17 | 三語文案誤導/重複：`HDBF.Error.RefillUnavailable`＝「本版本尚未開放人工補水」（實際已實作）；`HDBF.Warning.AdapterFailed`/`Shutdown` 中文抄成「入口堵塞」/「維護逾期」；`HDBF.Panel.WaterShortage` 寫「減產」但懲罰是生長變慢；世界圖示 tooltip 只在註冊時本地化一次 | `data/Localizations/*.csv`；`HdbfUiLocKeys.cs:211-212`；`HdbfWorldIconService.Update.cs:173-186` | 改字（RefillUnavailable→「補水來源不可用」）；修正重複 key；延遲本地化 tooltip；加字串審查/快照測試 | 🟡 三語字串已修＋測試；世界圖示 tooltip 延遲本地化 pending |
| L-18 | `features` 有 5 個死旗標：`rotation`/`maintenance`/`pipeConnection`/`logisticsOutput`/`soilFatigue` 被解析但無消費者（只有 densePlanting、powerConnection 有） | `data/HDBF_Config.json:3-11`；`HdbfConfigCodec.Sections.cs:19-33`；`Commands.cs:73`、`Context.cs:198` | 接上（關閉＝免維護/停用管路，明確 log）或從 config 移除；加 L1「每個 feature 都有消費者」 | ⏸ pending |
| L-19 | 文件 vs 實作漂移：ADR-V2-004 說「禁止逐幀扣水」，實作已改逐幀（2026-10-28 玩家要求）；需求/追蹤矩陣未同步 | `docs/v2/ADRS.md` ADR-V2-004 vs `IndoorWaterMath.Flow.cs`/`HdbfWaterFlowService`/`WaterFlow.cs` | 補 ADR 修訂與 `REQ-V2-WATER-02` 重新定義；更新 `docs/REQ_TRACEABILITY.md` | ⏸ pending（本輪只補維修 trace，未改 ADR-V2-004） |
| L-20 | 0 Harmony 承諾 vs 玩家環境的第三方補丁（RE-03）：HDBF 無從得知/隔離 | `README.md:8`；`crash.md` §1.3 | 啟動診斷 log（`indoor.behavior-probe`）＋工作坊相容性說明＋玩家診斷流程（WP-07） | ⏸ pending（probe 未做） |

---

## 5. 未來回報預測（玩家還沒說，但極可能說）

> 方法：以「發生機率 × 玩家痛感」排序，來源＝程式碼路徑＋設計代價＋本輪已知回報的延伸。**目的**：在玩家回報前先修，避免下一輪負評。

| # | 預測留言（玩家語氣，英文原句＋中文） | 觸發條件 | 風險 | 建議先修根因 | 本輪狀態（v2.5.4） |
|---|---|---|---|---|---|
| 1 | "I switched from Fallow back to Normal and my whole harvest vanished — 0 crops, empty slots."（休耕切回來，整批作物消失） | 任何人在休耕期間有成熟作物，再切回常規/密植 | **P0** | L-01：`TryHarvest` 在 amount==0 仍清槽＋transition scale 由 Fallow 算出 0 | ✅ 已修（L-01） |
| 2 | "It says no water after 3 days and the farm just stops. I have water tanks!"（三天後就沒水停工，我有水塔啊！） | 早期玩家：無抽水站/無管道/附近沒水就蓋農場 | **P0** | L-03：水罐 3 天＋三條補水路徑沒在 UI 說清楚 | 🟡 面板透明度已做；水罐平衡未改 |
| 3 | "My farms have no pipes nearby but the pipe port says connected and water keeps refilling."（沒接管也能補水） | 地圖任何一處接在管網上的水塔＋任意盒子農場 | **P0（平衡）** | L-02：`HasNetwork` 掃全 settlement 而非本農場 | 🟡 原因碼/probe 已做；真實連通 pending |
| 4 | "After updating Timber Pipes all my farms went dry at once."（更新管道模組後全場同時斷水） | Timber Pipes 改版導致任一 anchor 簽名不符 → 3 次抽水失敗 → adapter Failed | **P1** | L-02(c)：3-strike 無時間窗＋全域 health 決定每座農場 | ⏸ pending |
| 5 | "I clicked refill and lost water / lost 2 actions for almost nothing."（補水浪費） | 水罐剩 90% 以上時按補水 | **P1** | L-05：先抽滿 1/3 罐才做 headroom 截斷 | ✅ 已修（L-05） |
| 6 | "Panel says maintenance material missing but I have planks in two warehouses."（材料分散就說不足） | 多倉庫/多區、材料分散 | **P1** | L-07：加總判定 vs 單倉庫交易不一致 | ✅ 已修（L-07） |
| 7 | "The farm says 'no water source' but a water pump is 15 tiles away, and the farmers fetch water themselves."（12 格規則不可理解） | 最近水源 >12 格或不可達，但工人搬水正常 | **P1** | L-03/L-05：12 格硬限制＋兩套補水模型沒有 UI 說明 | 🟡 三條路徑與 12 格限制已在面板說明；硬限制未改 |
| 8 | "I turned the pipe port off and it turned itself back on next day."（管道接口自己回來） | 玩家手動關 pipe port（想省水/除錯） | **P1** | L-06：`UpdatePorts` 每 tick 覆寫玩家選擇 | ✅ 已修（L-06） |
| 9 | "The map warning icon doesn't match the panel / stays for a whole day after I fixed it."（圖標與面板不同步） | 水罐中途見底或被補滿、剛按完維護 | **P1** | L-11：圖標讀日 tick 快取、面板讀即時 | ⏸ pending（L-11） |
| 10 | "My farm shows 100% water and green everything but nobody is working there."（沒工人卻全綠） | 工人被調走/未指派（尤其新蓋好的農場） | **P2** | L-10：0 工人 → demand 0 → coverage 100% | ✅ 已修（L-01） |
| 11 | "The output port says connected but the conveyor doesn't take anything."（物流端口是假的） | 安裝 Conveyor Belts v2，把帶子接農場 | **P2** | L-09：物流端口只是能力標籤，沒有輸出實作 | ✅ 已修（L-01） |
| 12 | "I set a fixed crop from my crop mod and the panel says it's invalid / shows the English name."（作物模組不相容） | 安裝作物模組或遊戲新增作物 | **P2** | L-12：下拉/警告/名稱全吃內建 fallback 表 | ✅ 已修（L-01） |

**候補（較低機率但代價高）**：
- 「我拆掉農場時閃退」→ L-13（拆除未撤離室內工人，與已修暫停崩潰同族；3–5 行防禦性修復）。
- 「讀檔後農場進度回捲」→ L-04（存檔拒寫；需存檔比對）。
- 「更新後我的舊農場被刪除／位置無效」→ L-16（BackwardCompatibleTemplateNames 全空＋佔格歷史變更）。
- 「自訂陣營（不只 Ember-pelts）都不能用」→ RE-06 的通用化（M1 `ITemplateCollectionIdProvider`）。

---

## 6. 維修計畫（優先級、工作包、受影響檔案、驗收）

### 6.1 工作包一覽

| WP | 名稱 | 對應問題 | 優先級 | 主要層 | 工作量 | 風險 | 本輪狀態（v2.5.4） |
|---|---|---|---|---|---|---|---|
| WP-01 | 維護追平修正＋面板/tooltip 一致 | RE-01、L-08 | **P0** | Core/Gameplay/UI/Localization | S–M | 低（純邏輯/字串） | ✅ 已完成 |
| WP-02 | 自動維護開關（付料與手動相同） | RE-01、RE-02、Eltharion | **P0** | Core/Persistence/Gameplay/UI/data/Localization | M | 中（新增存檔欄位/順序） | ✅ 已完成 |
| WP-03 | 休耕轉換不清空作物 | L-01 | **P0** | Core | S | 低 | ✅ 已完成 |
| WP-04 | 暫停殘留 reconcile＋撤出強化 | RE-04、L-13 | **P0** | Gameplay | M | 中（Enterer/Enterable 邊界） | ✅ 已完成 |
| WP-05 | 管道語義/原因/真實連通判定＋驗證 | RE-05、L-02 | **P0**（先驗證） | Integration/UI/Localization/data | M | 中（第三方程 API） | 🟡 文案/原因/probe 完成；真實連通判定 pending |
| WP-06 | 水/閒置原因透明化（面板＋文案） | L-03、RE-01 UX、L-10 | **P0/P1** | UI/Core/Localization/docs | M | 低 | 🟡 面板狀態列完成；世界圖示多狀態屬 WP-10 pending |
| WP-07 | 崩潰診斷與玩家隔離流程 | RE-03 | P1 | Gameplay/文件/客服 | S | 低（唯讀診斷） | ⏸ pending |
| WP-08 | 補水浪費修正＋多趟/一鍵補水 | L-05、L-04(water UX) | P1 | Gameplay/UI | S–M | 低 | 🟡 headroom 已修；多趟/一鍵補水全部農場 pending |
| WP-09 | 維護材料可用性一致 | L-07 | P1 | Gameplay/Core | S | 低 | ✅ 已完成 |
| WP-10 | 世界圖示即時化＋多狀態 | L-11、RE-01 UX | P1 | Gameplay/Core/UI | M | 中（vanilla icon 行為） | ⏸ pending |
| WP-11 | 拆除/失去 finished 前撤出 | L-13 | P1 | Gameplay | S | 低 | ✅ 已完成（併入 WP-04） |
| WP-12 | 作物清單與本地化（crop mod 相容） | L-12、L-17 | P1 | UI/Gameplay/data | M | 低 | ✅ 已完成（v2.5.5：live 作物表＋`DisplayNameLocKey`；世界圖示 tooltip 延遲本地化仍 pending） |
| WP-13 | 自訂陣營支援 M0（Ember-pelts） | RE-06 | P1 | data/UI/Gameplay/Persistence | L | 中高（需 Emberpelts） | ⏸ pending |
| WP-14 | 其他 P2（物流端口語義、config 死旗標、0 工人、backward 清單、L-04 存檔策略、效能量測） | L-09/10/14/15/16/18/19 | P2/P3 | 多層 | L | 中 | 🟡 部分完成（v2.5.5：L-09/L-15 完成；L-14/L-16/L-18/L-19 與 100 棟量測 pending） |

### 6.2 建議排程（依風險與回報）

**本輪（v2.5.4）執行結果**：第 1 批 ✅ 全部完成（WP-03→WP-01→WP-02→WP-04→WP-05 文案/原因列→WP-06 面板狀態列；WP-05 真實連通判定與 §7.2 實機仍 pending）。第 2 批只完成 WP-08 的補水 headroom、WP-09、WP-11（併入 WP-04）；WP-10/WP-12 ⏸。第 3 批（WP-13/WP-14/100 棟/L-04）⏸ 未做。

**第二輪（v2.5.5）執行結果**：第 2 批剩餘的 **WP-12（L-12）** 與第 3 批的 **L-04、L-09、L-15** ✅ 完成（離線可證明，見 §11；L-04 的寫入策略修訂見 ADR-V2-006）；WP-10/L-11、WP-05 真實判定、WP-13、L-14/L-16/L-18/L-19 維持 ⏸ pending。

1. **第 1 批（P0，可離線完成＋L1 釘死）**：WP-03（2 行修法，最高性價比）→ WP-01 → WP-02 → WP-04（含 L-13 防禦）→ WP-05 的文案/原因列＋實機驗證準備 → WP-06 面板狀態列。
2. **第 2 批（P1，實機驗證綁定）**：WP-05 真實連通判定＋probe、WP-08、WP-09、WP-10、WP-11、WP-12。
3. **第 3 批（P1/P2，需外部模組或設計決策）**：WP-13（Ember-pelts/自訂陣營 M0→M1）、WP-14、效能 100 棟、存檔拒寫策略（L-04）。

**共同原則**：不改 §19.8 的週期/材料/停擺門檻；自動化只把「玩家記得按」換成「農場每天自己按」，付的料與動作成本相同；0 Harmony 不變；新字串三語同步；`build.ps1` 全量門禁（5 組件 0/0 → L1 655 → `tools/validate` → 44 gates）通過才可發佈；2.5.4 已通過。

### 6.3 受影響檔案（分層；實作卡用）

- **Core**：`Indoor/IndoorDayTick.cs`、`Indoor/IndoorGrowthMath.cs`、`Indoor/IndoorLaborMath.Scheduling.cs`、`Indoor/IndoorFarmRecord.cs`、`Indoor/IndoorSaveCodec.cs`、`Indoor/IndoorLogKeys.cs`、`Indoor/IndoorWarnings.cs`、`Balance/MaintenanceDefaults.cs`、`Config/HdbfConfig*.cs`、`Compat/CompatStateMachine.cs`
- **Persistence**：`Indoor/IndoorSaveKeys.cs`、`Indoor/IndoorEntitySaveBridge*.cs`
- **Gameplay**：`Components/HdbfBoxFarmController.{Tick,Commands,Context,IndoorSnapshot,ManualRefill,Pause,Pause.Eject,Activation}.cs`、`HdbfMaintenanceTransaction.cs`、`IndoorMaintenanceMaterialSource.cs`、`HdbfFarmCommandService.cs`、`IndoorInventorySink.cs`、`HdbfWorldIconService*.cs`、`HdbfManualWaterSource*.cs`、`HdbfGameConfigurator.cs`、`Farming/IndoorFarmWorkplaceBehavior.cs`
- **Integration**：`Adapters/TimberPipesWaterSource.cs`、`TimberPipesWaterBinding*.cs`、`IndoorAdapterHost.cs`、`CompatAdapterRegistry*.cs`
- **UI**：`Panels/HdbfIndoorPanelFactory*.cs`、`HdbfFarmPanelFragment*.cs`、`HdbfIndoorPanelWarnings.cs`、`HdbfIndoorPanelFormat.cs`、`HdbfIndoorPanelData.cs`、`HdbfIndoorCommandBridge.cs`、`Localization/HdbfUiLocKeys.cs`
- **data**：`data/HDBF_Config.json`、`data/Localizations/{enUS,zhCN,zhTW}.csv`、`data/TemplateCollections/*`、8 份 `data/Buildings/Food/BoxFarm/*.blueprint.json`（僅 WP-13 中立變體/backward 清單）
- **tests**：`V2MaintenancePowerTests`、`V2PauseContractTests`、`V2TickOrderTests`、`V2SaveCodecTests`、`V2PanelContractTests`、`V2LocalizationTests`、`V2AdapterHostContractTests`、`V2WaterTests`、`V2GameplayWiringTests`、`V2Nfr*`；本輪實際新增 `V2FallowTransitionTests`、`V2IdleReasonTests`，其餘以既有測試檔擴充（追平/自動維護/pipe probe/暫停/面板/本地化皆在既有檔內）
- **tools**：`tools/dev/CraftIndoorFixture`（新增 `--due-day`/`--cycles-missed`/`--auto-maintenance`/`--pipe-tank` 等 fixture 參數；工具屬 Lead 工作流）

### 6.4 依賴與風險

| 風險 | 影響 | 緩解 |
|---|---|---|
| 實機驗證需可互動會話/合成輸入（目前本機不可用） | 面板按鈕、維護扣料、人工補水正路徑、管道實際供水、屋頂疊放都卡住 | 先做離線 L1/validator/fixture；能自動化的回合（`Invoke-GameRound.ps1`）先跑非按鈕情境；明確記錄「編譯/離線通過 ≠ 實機驗證」 |
| 管道 API 反射/DI 未實機驗證 | WP-05 可能修完仍不生效 | 先做 F-PIPE-05 實機 fixture（水塔＋管）再改判定；probe 日誌先上 |
| Emberpelts 主模組缺席 | WP-13 只能做 M0 一半 | 先做中立材質/collection/動態陣營與離線測試；M1 等主模組 |
| 存檔 schema 變更（AutoMaintenance/FactionId） | 舊存檔相容 | 缺欄位→預設；不重排 dueDay；升 schema 版需遷移與 L1；遵守 `REQ-V2-SAVE-*` |
| 第三方 Harmony patcher 未知（RE-03） | 無法從 HDBF 修 | 收斂狀態＋probe 診斷＋玩家 mod 二分；文件如實標「未確定」；不硬指任何 mod |

> **2.5.4 狀態**：存檔 schema 變更風險已以「選填欄位 `maintenance.auto`＋缺欄位安全預設＋L1 往返」實作並驗證（舊檔安全）；`FactionId`（WP-13）仍 pending。

---

## 7. 驗證計畫

### 7.1 離線（L1 / validator；不需遊戲）

**結果（2026-11-02 v2.5.4）**：1–6、8、9 ✅ 已完成；7 🟡（補水 headroom／材料可支付已做，作物清單與自訂陣營 pending）。**實際測試名稱以 `tests/HDBF.Tests` 為準**（本節原列為調查階段的建議名稱；`V2AutoMaintenanceTests`/`V2PipeProbeTests` 未建立，相關案例放在既有檔內）。

1. **[✅ 已完成]** **WP-01**：`TestCatchUpCyclesMatrix`、`TestSingleMaintenanceCatchUpClearsStall`、`TestOneCycleBillCannotClearTwoMissedCycles`（回歸舊 bug）、`TestMaintenanceCountdownDoesNotResetFree`、`TestMaintenanceTooltipMatchesCatchUpBehaviour`。
2. **[✅ 已完成]** **WP-02**：`TestAutoMaintenancePaysSameBillAsManual`、`...OnlyRunsWhenDue`、`...SkipsWithoutMaterial`、`...DisabledDoesNothing`、`...IgnoresBasicAndOnGridTop`、`...OffGridTopBillsFifteenMetalPerCycle`、`...SettingRoundTripsThroughSave`、`...DefaultComesFromConfig`。
3. **[✅ 已完成]** **WP-03**：`TestFallowExitTransitionDayDoesNotDestroyMatureCrops`（現行會 FAIL，修後 PASS）；`TestModeTransitionScaleNeverZeroWhenPreviousModeHasNoYield`。
4. **[✅ 已完成]** **WP-04/11**：`V2PauseContractTests` 擴充（`UnreserveSlotAndExit`、`EntererAdded`、`eject-summary`/`eject-failed`/`residual-enterer`、`ForceRemoveEveryone` fallback、0 Harmony）；拆除路徑契約。
5. **[✅ 已完成]** **WP-05**（原因碼/四態/文案；真實連通判定另 pending）：`V2AdapterHostContractTests` 四態（無 tank／有 tank 無 graph／有 graph 乾／binding 失敗）＋ `PipeProbe.Reason`；面板文案契約（未連接不再與「沒裝模組」共用字串）。
6. **[🟡 部分完成]** **WP-06/10**（WP-06 面板狀態列/閒置原因已完成；WP-10 世界圖示多狀態 pending）：`TestIdleReasonPriority`、面板 `AutoMaintenance/NextDueIn/CyclesToCatchUp`、圖示與面板同源的純函式矩陣。
7. **[🟡 部分完成]** **WP-08/09/12/13**（補水 headroom、材料可支付 ✅；作物清單/自訂陣營 pending）：補水 headroom、材料可支付判定、作物清單吃 live catalog、自訂陣營解析/存檔保留。
8. **[✅ 已完成]** **NFR**：檔案行數/0 Harmony/依賴方向/文件追溯（`V2Nfr*`、44 release gates）維持全綠；`tools/validate` 三語鍵集一致。
9. **[✅ 已完成]** **全量門禁**：`build.ps1 -WorkRoot "$env:TEMP\hdbf-work"` → 5 程序集 0 warning/0 error、L1 **655/655**、validate OK、44 gates PASSED（2026-11-02 最終 HEAD `dd12a48`）。

### 7.2 實機（fixture/回合；每項收集 `Player.log` + 截圖 + summary）

**本輪未執行任何 fixture 回合**；以下全部 pending。前置：`build.ps1 -Install` 部署；`tools/dev/Invoke-GameRound.ps1 -Action Prepare/Launch/SpeedUp/Stop`；fixture 需擴充（WP-01/02 的 due-day/cycles-missed/auto-maintenance；WP-05 的水塔+管道）。

| # | 情境 | 預期（修復後） | 狀態（v2.5.4） |
|---|---|---|---|
| A | Intermediate、逾期 3 期、倉庫 24 木板、自動維護 on | 首個日 tick：`indoor.maintenance-auto`（Plank×24、k=3）、dueDay 前進 30、解除停擺、`indoor actions > 0` | ⏸ pending（本輪未跑實機回合） |
| B | 同上但只有 8 木板 | `maintenance-auto-missing`（needed=24, available=8）、不扣料、維持停擺、面板缺料紅字 | ⏸ pending（本輪未跑實機回合） |
| C | 手動對照（重現 RE-01） | 修前：按 1 次仍停擺（growth≈0.05、actions=0），要按 3 次；修後：1 次解除且扣 24 木板 | ⏸ pending（本輪未跑實機回合） |
| D | 休耕→常規（L-01） | 修前：`harvested=60` 但倉庫 0 件、槽位清空；修後：成熟作物至少收穫 1 件/槽（或保留），倉庫有貨 | ⏸ pending（本輪未跑實機回合） |
| E | 暫停存檔載入（RE-04 R1） | 修後：載入完成後 `reconcile` 日誌、室內 enterer=0；不再「Load time 後才 moved」 | ⏸ pending（本輪未跑實機回合） |
| F | 走進途中暫停（R2） | 修後：`residual-enterer` 日誌、最終 `NumberOfEnterersInside == 0` | ⏸ pending（本輪未跑實機回合） |
| G | 管道 A/B（RE-05） | 修前：農場旁鋪管但無水塔→`Not connected`；正確接法（液體水塔接網）→ `Connected`＋抽水；修後：錯誤接法顯示原因 `NoPipeTank`＋tooltip | ⏸ pending（本輪未跑實機回合） |
| H | 版本漂移模擬（可選） | binding 失敗→面板顯示 `BindingFailed`／`compat.failed`，不再靜默「未連接」 | ⏸ pending（本輪未跑實機回合） |
| I | 人工補水正路徑（首次取證） | 12 格內水塔/水體＋2 工人：按補水→tank 增加、`water.manual-refill` 日誌 | ⏸ pending（本輪未跑實機回合） |
| J | 拆除含室內工人的農場（L-13） | 無 Enterer/Enterable 例外；工人被安全撤出 | ⏸ pending（本輪未跑實機回合） |
| K | Ember-pelts（需安裝 3346318229） | M0：建造選單看得到四檔建築、可建造、面板陣營顯示正常、作物表以該陣營 live 作物為準、存檔往返保留陣營 | ⏸ pending（本輪未跑實機回合） |

### 7.3 玩家回覆/診斷模板（工作坊留言用）

> **2.5.4 已發佈**：可直接回覆玩家「已修復」；以下保留原診斷/隔離模板（回覆前請確認對方版本 ≥ 2.5.4）。

**給 Eltharion（維護自動化）**：
> Thanks — confirmed. Maintenance is currently a manual button, and there is a real bug where one click may not clear an overdue farm. We're adding (1) a fix so one maintenance click clears all overdue cycles (you pay the matching materials), and (2) an optional automatic-maintenance toggle per farm. The material cost stays exactly the same as manual. Workaround for now: click "Perform maintenance" repeatedly until the panel no longer shows "stalled".

**給 Annastasya（崩潰 + No Connection）**：
> Sorry for the crash. The log shows the null reference comes from a third-party Harmony patch of the beaver worker behavior (`WorkerRootBehavior.Decide_Patch1`), not from HDBF's own code — we ship 0 Harmony patches. To identify the patching mod, please send your full `Player.log` (it lists all mods and versions) and, if possible, your save. For the pipe: "Not connected" is our panel text. Auto-refill only draws water from a **vanilla liquid water tank (Small/Medium/Large Tank) that is connected to the Timber Pipes network** — the farm itself is not a pipe endpoint, so pipes attached directly to the farm don't register. We're fixing the panel text/tooltip and adding a proper connection check.

**給 merlin4059（暫停 crash + upkeep）**：
> Thanks. Could you check whether your `Player.log` contains `indoor.paused` / `indoor.resumed` lines and your HDBF version (`start HDBF version=`)? We fixed one pause crash in 2.5.3, but there is a remaining case when a paused save is loaded (workers stay inside the blocked farm until resume) — we're fixing that. "Beavers won't do upkeep": correct — maintenance is currently a player button; an automatic-maintenance option is planned with the same material cost.

**給 saqlain141（Ember-pelts）**：
> Thanks for testing. HDBF currently registers its buildings only for Folktails and Iron Teeth, so custom factions like Ember-pelts don't see them in the build menu yet. We're working on custom-faction support (neutral-material building variants + faction collection registration). No ETA yet.

**要求玩家提供的證據（通用）**：完整 `%USERPROFILE%\AppData\LocalLow\Mechanistry\Timberborn\Player.log`（不是只貼 crash stack）、mod 清單與版本、存檔 `.timber`、crash 前操作（暫停/換班/接管道/拆除）、HDBF 版本行。

---

## 8. 附錄

### A. 玩家留言原文與翻譯

見 §1.1（7 則，含作者回覆 3 則）。截圖檔：使用者提供之 `屏幕截图 2026-11-02 211610.png`（1065×1098）。

### B. 關鍵證據索引（本文件引用的主要檔案:行）

| 主題 | 證據 |
|---|---|
| 維護手動/追平 bug | `src/HDBF.Gameplay/Components/HdbfBoxFarmController.Commands.cs:213-273`（dueDay += cycle :254-258、cycles=0 :266）；`IndoorDayTick.cs:139-155,187,219-248`；`src/HDBF.UI/Panels/HdbfFarmPanelFragment.Build.cs:158-166`；`HdbfFarmCommandService.cs:76-117`；`HdbfMaintenanceTransaction.cs:51-133`；L1 `V2MaintenancePowerTests.cs:70-77` |
| 維護/水狀態不可見 | `HdbfIndoorPanelFactory.cs:92-104`；`IndoorWarnings.cs:91-119`；`HdbfWorldIconService*.cs` |
| 基礎檔免維護/停用按鈕 | `TierCatalog.cs:120-141`；`HdbfIndoorPanelFactory.cs:214-228` |
| 水 3 天/手動補水 | `TierCatalog.cs`（3.6/10.8/33.6/76.8）；`ManualRefill.cs:27-134`；`IndoorConstants.cs:83-90`；`IndoorWaterMath.cs:51-110`；`IndoorDayTick.cs:192-193` |
| 休耕清空作物 | `IndoorGrowthMath.cs:94-130`；`IndoorLaborMath.Scheduling.cs:27-61`；`HdbfBoxFarmController.Tick.cs:144-154`；`Commands.cs:90` |
| 崩潰 stack | https://pastebin.com/SBPK5AJQ（raw 已抓）；`crash.md` §1 |
| 暫停殘留 R1 | `docs/in-game/pause-fix3-A2-Player.log:2931,2955,2958,2959`；`Pause.cs:167-172`；`crash.md` §2.2 |
| 撤出實作 | `HdbfBoxFarmController.Pause.Eject.cs:31-85`；`Pause.cs:189-253`；IL `Enterer/Enterable`（`crash.md` 附錄 A） |
| 管道判定/字串 | `HdbfIndoorPanelFactory.Cards.Output.cs:109-110,133-148`；`TimberPipesWaterSource.cs:97-122`；`TimberPipesWaterBinding.cs:228-255`；`Tank.cs:17-45`；`IndoorAdapterHost.cs:105-120`；`data/Localizations/enUS.csv:78-82` |
| 管道未驗證 | `docs/VERIFICATION.md:1009-1012`；`docs/workstreams/compat-01-adapters.md:384`；`README.md:131` |
| Ember-pelts/陣營 | `GameCropCatalog.cs:120-129,178-237`；`IndoorEntitySaveBridge.cs:180-185`；`data/TemplateCollections/*`；`docs/VERIFICATION.md:194-196`；`factions.md` §2 |
| 存檔拒寫 | `IndoorEntitySaveBridge.cs:76-81`；`IndoorSlotRepair.cs:144-233`；`IndoorSaveFieldRepair.cs:103-160` |
| Backward 清單/佔格 | 8 份 `data/Buildings/Food/BoxFarm/*.blueprint.json:26`、`:38-42`；`docs/VERIFICATION.md` §25 |
| Log 依賴/版本 | `README.md`（版本 2.5.4、L1 655、0 Harmony）；`docs/v2/CHANGELOG.md` [2.5.4]；`docs/VERIFICATION.md` §36/§37 |

### C. 本次調查檔案（唯讀；只新增，未改任何既有檔）

| 檔案 | 內容 |
|---|---|
| `docs/workstreams/feedback-20261102/maintenance.md` | F-MAINT-01..08：維護/補水/停擺/面板/實機缺口＋方案 A–E＋L1 測試清單＋fixture 步驟 |
| `docs/workstreams/feedback-20261102/crash.md` | F-CRASH-01/02：堆疊判讀、IL 證據、殘留路徑 R1–R7、修復 D1–D5、驗證劇本 |
| `docs/workstreams/feedback-20261102/pipes.md` | F-PIPE-01..05：字串來源、PipeTank 機制、判定鏈、修復分層、實機 fixture A–E |
| `docs/workstreams/feedback-20261102/factions.md` | F-FACTION-01..10：template collection IL、材質風險、失敗鏈 H1/H2、M0/M1 修法 |
| `docs/workstreams/feedback-20261102/latent-future.md` | F-LATENT-01..19：19 條潛在 bug＋Top 12 未來回報預測＋證據索引 |
| `docs/BUG_REPAIR_20261102.md` | **本文件**（整合版） |

> 驗證：`git status --short` 只顯示 `?? docs/workstreams/feedback-20261102/` 與本文件；`src/`、`data/`、`tests/`、`tools/` 無改動。

### D. 環境與版本

| 項目 | 值 |
|---|---|
| 工作區 | `C:\Users\user\Documents\Timberborn\Mods\HighDensityBoxFarm`（source workspace，不是 mod 安裝目錄） |
| 遊戲 | Timberborn 1.1.2.8（`1.1.2.8-ffc2de0-sw`），Unity 6000.5.11f1 |
| 模組版本 | **2.5.4（2026-11-02 發佈；`data/manifest.json`；Workshop ManifestID `2037025195551276545`）** |
| 程序集 | HDBF.Core / Persistence / Gameplay / Integration / UI（5 組件，0 Harmony） |
| L1 測試 | **655 PASSED / 0 FAILED**（2.5.4） |
| 發佈門禁 | 44 checks PASSED、`tools/validate RESULT: OK` |
| 相關工作坊項目 | HDBF `3828809784`、Timber Pipes `3801726769`（11.4.0）、Harmony `3284904751`（2.4.1）、T3MP `3756656421`（1.2.5）、Emberpelts `3346318229`（本機未安裝） |

---

## 9. 修復完成定義（Definition of Done，給實作卡）

**本輪（v2.5.4）結果**：第 1、2、3、4、6 項 ✅ 已滿足；**第 5 項（實機 fixture 回合）⏸ pending** — 因此本輪不宣稱 DoD 全部完成，實機證據仍待補。
**第二輪（v2.5.5，未發佈）結果**：第 1、2、3、4、6 項 ✅（L1 672/672、44 gates PASSED）；第 5 項（實機 fixture）仍 ⏸ pending。

一項修復只有同時滿足以下條件才算完成：

1. **[✅ 已滿足]** **程式碼**：符合 REQ-V2-SAFE-01（無例外外洩）、0 Harmony、依賴方向不變、Core 純邏輯不在 Gameplay 重複。
2. **[✅ 已滿足]** **測試**：新增/更新 L1 契約測試並在本機全綠（回歸釘死原 bug）；`build.ps1` 5 組件 0/0、L1 **655/655**、`tools/validate` OK、44 gates PASSED（2.5.4 已達成）。
3. **[✅ 已滿足]** **字串**：三語 `enUS/zhCN/zhTW` 鍵集一致、語意與行為一致；不新增死鍵。
4. **[✅ 已滿足]** **存檔**：新欄位可缺省（舊檔安全）、不重排既有 `dueDay`、遷移/驗證有 L1。
5. **[⏸ pending]** **實機**：對應的 fixture 回合（§7.2）有 `Player.log` + 截圖/summary 證據；未驗證項在文檔明確標 pending，不標完成。
6. **[✅ 已滿足]** **文件**：更新 `docs/VERIFICATION.md`（新回合）、`docs/v2/CHANGELOG.md`、`docs/REQ_TRACEABILITY.md` 與本文件狀態。

---

*本文件由 5 條唯讀調查整合而成；所有「已證實」均附檔案:行、IL 或日誌證據；「高度可能/推測」已明確標記，未把未實機驗證的項目寫成已完成。*

---

## 10. 維修實作狀態（2026-11-02 實作輪，v2.5.4）

> 實作卡計畫：`docs/workstreams/bugfix-20261102/REPAIR_PLAN.md`（範圍、檔案所有權、Core 契約、驗收）。
> 離線驗證：`docs/VERIFICATION.md` §36；變更記錄：`docs/v2/CHANGELOG.md` [2.5.4]。
> **重要**：本輪沒有可互動的遊戲會話，所有「面板實際按鈕／實機供水／暫停存檔載入」仍屬 §7.2 的 pending 項目；本輪只完成可離線證明的程式碼、L1 契約與文案。

| WP | 狀態 | 實際修法（一句話） | 釘死測試 |
|---|---|---|---|
| WP-03 / L-01 | ✅ 完成 | `TryHarvest` 在 `amount <= 0` 時不清槽；`ModeTransitionYieldScale` 對「上一模式產量 0（休耕）」回傳 1 | `V2FallowTransitionTests`（3 條） |
| WP-01 / RE-01 / L-08 | ✅ 完成 | 維護改「追平付費」：一鍵付 `k = 1 + floor((today − dueDay) / cycle)` 期材料（`IndoorMaintenanceMath.CatchUpCycles`）、`dueDay += k×cycle`、`cyclesMissed=0`；未到期（k=0）禁用並回 `HDBF.Error.MaintenanceNotDue`；面板顯示本次追平期數/總料 | `V2MaintenancePowerTests`、`V2GameplayWiringTests` 維護段 |
| WP-02 / RE-02 / RE-07 | ✅ 完成 | 每棟持久化 `AutoMaintenance`（預設 config `features.autoMaintenance=true`）；日 tick 在模擬前自動付同一筆追平帳單（缺料不扣不推進，寫 `indoor.maintenance-auto(-missing)`）；面板 On/Off 按鈕 | `V2SaveCodecTests`、`ConfigCodecTests`、`V2GameplayWiringTests` |
| WP-04 / RE-04 / L-13 | ✅ 完成 | 新增 `IndoorPauseReconcileService`（~1 Hz）持有暫停農場清單 → `ReconcilePausedEnterers()`；暫停中 `EntererAdded` 即時撤出；撤出改 `UnreserveSlotAndExit()`＋`IsInside/CurrentBuilding` 檢查＋`eject-summary`/`eject-failed`/`residual-enterer` 日誌＋`Abandon`+`Remove` fallback（原版 `ForceRemoveEveryone` 為 private，改以等價公開步驟實作）；`Deactivate()` 先撤一輪（L-13） | `V2PauseContractTests`（9 條） |
| WP-05 / RE-05 / L-02 | ✅ 完成（真實連通判定 pending） | 新增 Core `IIndoorPipeProbe`/`PipeProbe`（7 種原因碼）；Timber Pipes 端做唯讀 census（`NoPipeNetwork`/`NoPipeTank`/`TankDry`/`AdapterUnavailable`/`BindingFailed`/`Connected`），不抽水、不計三振；面板改用原因碼＋新 tooltip（只從接上管網的原版液體水塔抽水、接農場外殼無效）。**用 `PipeRegistry.TryGetGraph(農場座標)` 的真實連通判定留 pending**（需實機驗證）。 | `V2AdapterHostContractTests`（+5 條） |
| L-06 | ✅ 完成 | `UpdatePorts` 不再每 tick 覆寫玩家 `PipePort`/`LogisticsPort` 選擇（只保留 `PowerPort` profile 派生與 adapter 狀態日誌） | `V2AdapterHostContractTests.TestDailyTickNeverOverwritesThePlayerPortChoice` |
| WP-06 / L-03 / L-05 / L-10(UI) | ✅ 完成 | Core `IndoorIdleReasonMath`（Paused > NoWorkers > EntranceBlocked > OutputFull > WaterShortage > MaintenanceStalled > MaintenanceDue > Fallow）＋面板第一張卡的狀態列；缺水列寫明三條補水路徑（管道/人工 ≤12 格/搬水貨物）；0 工人時覆蓋率/效率顯示「—」（不再全綠）；補水改 `ManualRefillDrawAmount = min(1/3 罐, headroom)`，headroom < 1 單位視為已滿、不扣動作（`TankFull`） | `V2IdleReasonTests`、`V2WaterTests`、`V2PanelContractTests`、`V2LocalizationTests` |
| WP-09 / L-07 | ✅ 完成 | 維護材料 `Available()` 改「單一庫存最大 unreserved 量」（與交易 all-or-nothing 一致）；缺料警告用「本次追平總帳單」判斷 | `V2GameplayWiringTests`、`V2MaintenancePowerTests` |
| L-17 | ✅ 完成（字串） | 三語修正：`RefillUnavailable`、`Warning.Shutdown`/`AdapterFailed` 重複、`WaterShortage`（減產→生長變慢）、維護 tooltip 改追平語意、`Port.Pipe`/`PipeWater` 說明；世界圖示 tooltip 延遲本地化屬 L-11/WP-10 pending | `V2LocalizationTests`（key parity＋L-17 distinctness） |
| WP-07 / RE-03 | ⏸ pending | 唯讀 `indoor.behavior-probe` 診斷**未實作**：`build.ps1` G-08 會掃「source 不得含 Harmony 字串」，需另案設計不改 gate 的寫法；玩家端診斷流程仍見 §3.4 | — |
| L-08（連點） | ✅ 併入 WP-01 | 未到期按鈕禁用＋命令層 `MaintenanceNotDue`（同一天第二下不會多付） | 同上 |
| L-04 / L-09 / L-11 / L-12 / L-14 / L-15 / L-16 / L-18 / L-19 / WP-12 / WP-13 / WP-14 | ⏸ pending | 未在本輪變更（P1/P2 或需外部模組/實機/設計決策）；維持 §4 原方案 | — |

**本輪明確未做**：未改 `§19.8` 的週期/材料/停擺門檻與經濟；未新增 Harmony；未進行任何實機回合；未改存檔 schema 版本號（新增欄位為向後相容的選填欄位，舊檔缺欄位 → 安全預設並記 `maintenance.auto-default`）。
已於 2026-11-02 22:56 更新 Steam Workshop 內容與 Change Note（ManifestID `2037025195551276545`，只更新內容、未動頁面描述/封面/標籤），並 push GitHub `2a4a237`；發佈記錄見 `docs/VERIFICATION.md` §37。

---

## 11. 第二實作輪（2026-11-03，v2.5.5，未發佈）

> 實作卡計畫：`docs/workstreams/bugfix-20261102/REPAIR_PLAN_ROUND2.md`（範圍、檔案所有權、契約、驗收）。
> 離線驗證：`docs/VERIFICATION.md` §38；變更記錄：`docs/v2/CHANGELOG.md` [2.5.5]；存檔語意修訂：`ADR-V2-006`。
> **本輪同樣沒有可互動的遊戲會話**：只做 §4 清單中「離線可證明」的項目；所有實機 fixture（§7.2 A–K）維持 pending，
> 不把任何未實機驗證的項目寫成已完成。

| Bug | 狀態 | 實際修法（一句話） | 釘死測試 |
|---|---|---|---|
| L-04（P0 風險） | ✅ 完成（離線） | 寫前校驗分兩態：`CannotWrite`（記錄/檔位缺失、tier profile/容量非法、校驗器例外）→ 維持拒寫＋`save.validation-failed`；`Repaired`（欄位/槽位可修復）→ **寫入修復後的副本**＋`save.write-repaired`（含摘要/notes）＋修復前狀態存 `PreviousPayload` 回滾副本（`save.previous-snapshot`，乾淨存檔不寫）；entity ID 不一致仍以 Clamp 修復、不列不可寫。**偏離處理見 ADR-V2-006**。 | `V2SaveWriteStrategyTests`（7 條）、`V2PersistenceBridgeContractTests.TestSaveWritesRepairedCopyAndKeepsRollbackCopy`、`V2SaveValidatorTests`（`CannotWrite` 矩陣） |
| L-12 / WP-12 | ✅ 完成（離線） | `IndoorCropChoice`（Core）＋`IndoorFarmSnapshot.CropChoices`（gameplay 以 live `_cropCatalog.ForFaction` 快取填充）＋`CropDefinition.DisplayNameLocKey`（blueprint `LabeledEntitySpec.DisplayNameLocKey`，guarded）；面板下拉/固定作物名/invalid-crop 警告/Growing 列名改 live-first，空清單才退回內建表與 10 種名稱。 | `V2PanelContractTests.TestCropDropdownUsesTheLiveSnapshotCatalogue`、`V2CropCatalogTests`（3 條）、`V2PanelVisualContractTests` |
| L-15 | ✅ 完成（離線） | `IndoorFarmSnapshot.Profile` 帶 config 覆寫後的 `TierProfile`；UI `FindProfile(snapshot)` 先吃 snapshot.Profile、null 才退回 `TierCatalog.Get`（既有 snapshot/測試行為不變）；面板指紋混入 Profile/CropChoices。 | `V2PanelContractTests.TestPanelUsesTheConfigOverrideProfileFromTheSnapshot`、`TestFingerprintCoversTheLiveProfileAndCropChoices` |
| L-09 | ✅ 完成（離線） | 物流/輸出端口列永不再顯示 Connected：無端口 → Not applicable；偵測到模組 → 新 `HDBF.Port.LogisticsDetected`（尚未接出貨物）；有端口未偵測 → Not connected；新 `HDBF.Tooltip.LogisticsPort` 說明請用原版搬運（三語，key 集一致）。 | `V2PanelContractTests.TestLogisticsPortRowsNeverClaimAConnection`、`V2LocalizationTests.TestLogisticsPortTextsDescribeTheHonestOutputSemantics`、`V2PanelVisualContractTests` |
| L-11 / WP-10 | ⏸ pending | 世界圖示即時化與面板同源：需實機驗證 vanilla icon 行為；本輪未動。 | — |
| WP-05 真實連通 / L-02(b)(c) | ⏸ pending | `PipeRegistry.TryGetGraph(農場座標)` 真實判定、全域假連通解耦、3-strike 時間窗：需第三方程 API 與實機。 | — |
| WP-07 / L-20 | ⏸ pending | `indoor.behavior-probe` 唯讀診斷：G-08 掃 source 禁 Harmony 字樣，需另案設計。 | — |
| WP-13 / RE-06 | ⏸ pending | 自訂陣營（Ember-pelts）：需外部主模組。 | — |
| L-14 / L-16 / L-18 / L-19 | ⏸ pending | 100 棟效能量測、backward 清單、config 死旗標、ADR/需求漂移：需設計決策或量測。 | — |

**本輪明確未做／已知風險**：

* 未改 `§19.8` 的週期/材料/停擺門檻與經濟；未新增 Harmony；未進行任何實機回合；未上傳 Steam Workshop、未 push GitHub、未 `-Install`。
* `IndoorEntitySaveBridge.Keyed.cs`（非 live 存檔路徑）仍保留舊「`Repaired` 即拒寫」契約，統一留待後續小卡。
* `PreviousPayload` 是「修復前回滾副本」，**不保證可載入**、`Load` 不讀它；一次修復寫入後，後續乾淨寫入不會主動清除它（殘留不影響讀取）。
* `HdbfUiLocKeys.cs` 已達 300 行上限；下一批面板字串需先拆 partial 並授權改 `tests/HDBF.Tests/HDBF.Tests.csproj` 的 linked Compile。
