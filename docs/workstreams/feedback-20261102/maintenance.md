# F-MAINT：維護／保養必須手動，否則「什麼都不發生」

> **狀態**：調查完成（唯讀）；**未修改任何 `src/`、`data/`、`tests/`、`tools/` 檔，未執行 `build.ps1`，未啟動遊戲**
> **日期**：2026-11-02｜**模組**：v2.5.3（2026-10-29）｜**遊戲**：Timberborn 1.1.2.8
> **任務**：task-1（Feed-Bug-01）｜**本檔為本工作流唯一寫入檔**
> **玩家回饋**：
> * Eltharion（Steam Workshop，2026-11-02）：「Is there an option to automate the maintenance? You have to do it manually, otherwise nothing happens as far as I can see.」
> * merlin4059（同日）：「...for some reason my beavers wont do upkeep, get that worked out though and I like it.」

---

## 0. 一句話結論

**v2.5.3 的維護是「玩家在面板按一次按鈕」的純手動命令，程式裡沒有任何工人／日 tick 的自動維護路徑；而「什麼都不發生」是兩條互相獨立的機制造成的同一個體感：**
**(a) 水罐見底 → `G_irr=0` 生長停止且不播種；(b) 材料維護連續錯過 ≥2 期 → 停擺（不播種、不收穫、生長降到 1/20）。**
兩者都只能靠玩家點按鈕（補水／執行維護）解除，且**維護逾期 ≥2 期時，按一次「執行維護」仍然停擺**（只前進一個週期），UI 文案卻說「重置倒數／恢復產出」——這是本項最該修的實質 bug。

### 0.1 根因分級（設計 vs bug vs UX）

| 級別 | 編號 | 一句話 | 優先級 |
|---|---|---|---|
| **Bug** | F-MAINT-02 | 逾期 ≥2 期時單次維護無法解除停擺（只 `dueDay += cycle`），且面板把逾期數即時歸零、隔日又回到 2；tooltip 說「重置倒數／恢復產出」與行為不符 | **P0** |
| **設計（不是 bug）＋期待落差** | F-MAINT-01 | 維護 100% 手動：只有面板命令，無工人／日 tick 自動路徑；工作坊文案「until a beaver performs maintenance」製造錯誤期待 | **P0**（補自動化） |
| **UX** | F-MAINT-03 | 維護狀態不可見：維護卡在面板第 5 張卡（1080p 需滾動）、世界圖示只有 1 個且水優先遮蔽維護、沒有「為什麼閒置」狀態列、沒有通知 | **P1** |
| **未驗證風險** | F-MAINT-04 | 「維護材料扣料」與「人工補水正路徑」**從未實機驗證**（README／VERIFICATION 自承）；若正路徑在實機壞掉，玩家會永遠無法維護／補水 | **P0**（先驗證） |
| **UX** | F-MAINT-05 | 基礎檔沒有材料維護，但面板仍顯示停用的「執行維護」按鈕＋「無材料維護」，玩家容易誤判「我要手動維護」 | P2 |
| **設計負擔** | F-MAINT-06 | 沒接管道時，每棟每 1–3 個遊戲日就要手動補一次水（每趟只補 1/3 罐 ≈ 1 個滿載日）；無「一鍵全部補水」 | P1 |
| **表現落差** | F-MAINT-07 | 手動補水是面板瞬時取水（扣 2 個動作但看不到任何比佛外出），玩家當然覺得「比佛不做 upkeep」 | P1 |
| **UX／潛在** | F-MAINT-08 | 完全沒有主動通知（無 toast／音效／事件）；`HDBF_Config.json` 的 `features.maintenance` 等 5 個旗標解析後**從未被任何程式讀取**（死旗標） | P2 |

### 0.2 最小維修順序（建議）

1. **F-MAINT-02**（純邏輯＋文案，改動小、風險低、直接解「按了也沒用」）
2. **F-MAINT-01 的自動維護**（新增 per-building「自動維護」開關；付一樣的料 → 不破壞 §19.8 數值與代價）
3. **F-MAINT-03 狀態列／圖示／面板位置**（讓玩家在 1 秒內知道「為什麼不動」）
4. **F-MAINT-04 實機驗證**（材料扣料＋人工補水正路徑；這是所有方案的前提）
5. **F-MAINT-06/07 自動補水與表現**（自動巡查取水；可選一鍵全部補水）
6. **F-MAINT-05/08 文案與旗標清理**

---

## 1. 玩家回饋分流：兩位玩家可能講的是不同東西

Eltharion 用「maintenance」這個字，但**基礎檔（Basic）根本沒有材料維護**，所以兩條路都要查：

| 可能情境 | 玩家看到的 | 實際機制 | 結論 |
|---|---|---|---|
| 用 **Basic（60 槽）** | 面板「無材料維護」、按鈕停用；水罐 3.6／需求 1.2 每日 | 只有**補水**是手動（無管道時） | Eltharion 若用 Basic，他說的「maintenance」其實是**維持運轉＝補水**（與 Lead 補充一致） |
| 用 **Intermediate／Advanced／離網 Top** | 面板有「執行維護」按鈕、到期日、缺料 | 材料維護**純手動** | 「You have to do it manually」字面成立（設計如此） |
| 任何檔位水耗盡 | 面板「水短缺」、世界圖示（水） | `G_irr=0`→不生長、不播種 | 「nothing happens」 |
| 任何材料檔位逾期 ≥2 期 | 面板「維護逾期」、世界圖示（若水沒同時壞） | 停擺：不播種、不收穫、生長 ×0.05 | 「nothing happens」 |
| 農場被**暫停** | 建築暫停態 | v2.5.3 起暫停＝日 tick／水耗／圖示全停 | 也是「什麼都不發生」（2.5.3 才有的第三條） |
| 模式＝**休耕 Fallow** | 面板模式＝休耕 | `actionsAllowed=false`（只停農事） | 設計如此，但玩家可能誤判 |

> merlin4059 的「my beavers wont do upkeep」在現行實作下**字面為真**：全模組沒有任何「比佛執行維護／取水」的行為；農事動作只包含播種與收穫（`IndoorLaborMath.RunActions`）。

---

## 2. 現況事實（逐條附檔案:行號）

### 2.1 維護只有一條路徑：面板按鈕 → 命令 → 扣料 → 推進到期日

| 步驟 | 位置 | 行為 |
|---|---|---|
| UI 按鈕 | `src/HDBF.UI/Panels/HdbfFarmPanelFragment.Build.cs:158-166` | 建立「執行維護」按鈕（文字鍵 `HDBF.Action.Maintenance`），放在面板底部 action card |
| UI 點擊 | `src/HDBF.UI/Panels/HdbfFarmPanelFragment.Commands.cs:138-164` | `OnMaintenanceClicked` → `_command.TryPerformMaintenance` |
| 命令入口 | `src/HDBF.Gameplay/Components/HdbfFarmCommandService.cs:76-117` | 先算帳單（plan）→ 扣料 → 推進到期日（all-or-nothing） |
| 帳單 | `src/HDBF.Gameplay/Components/HdbfBoxFarmController.Commands.cs:213-234` | `TryGetMaintenancePlan`：`IrrigationOnly` 直接 `false`；接電頂級 `cycleDays<=0` 也 `false`；否則回傳 `Plank`/`MetalBlock` 數量 |
| 扣料 | `src/HDBF.Gameplay/Components/HdbfMaintenanceTransaction.cs:51-133` | 從**最近一個已完工地區**的 inventory 扣，需 `Takes(itemId)` 且 `HasUnreservedStock(全部數量)`；不足＝不扣任何料＋`HDBF.Error.MaterialsMissing`＋節流日誌 `indoor.maintenance-missing` |
| 推進 | `src/HDBF.Gameplay/Components/HdbfBoxFarmController.Commands.cs:244-273` | `nextDue = currentDue + cycleDays`（:254-258）、`cyclesMissed = 0`（:266） |
| 日 tick | `src/HDBF.Core/Indoor/IndoorDayTick.cs:139-144`（只算 cycles）、`:219-248`（只寫回 cycles／warnings） | **沒有任何扣料或 reset 到期日的動作** |
| L1 契約釘死 | `tests/HDBF.Tests/V2MaintenancePowerTests.cs:70-77` | 測試訊息即「the tick never performs the maintenance」 |

**結論（已證實）**：維護沒有任何自動路徑。全 `src/` 的 `Auto*` 只有三種：作物自動輪作（`IndoorEnums.cs:27`）、管道自動補水（`IndoorEnums.cs:53`）、水罐有水時自動種滿（`IndoorLaborMath.cs:141-148`）——**維護不在其中**。

`src/HDBF.Core/Indoor/IndoorMaintenanceMaterialSource.cs:1-6,21-23` 的檔頭更直接寫明：「Used by the daily tick only to report MissingMaterial; the actual maintenance transaction is performed by `HdbfFarmCommandService`」，註解：「maintenance stays an explicit player/command action」。

### 2.2 基礎檔真的免材料維護——而且面板按鈕是停用的

* `src/HDBF.Core/Balance/TierCatalog.cs:120-141`：Basic `MaintenanceKind=IrrigationOnly`、`MaintenanceCycleDays=0`、`MaintenanceMaterialCount=0`（同一表：中級 8 木板/10 日、高級 6 金屬/10 日、頂級接電 0、離網 15 金屬/8 日）。
* `src/HDBF.UI/Panels/HdbfIndoorPanelFactory.cs:214-228`：`IrrigationOnly` → `MaintenanceBlockedReason` 回傳「無材料維護」→ `CanPerformMaintenance=false`（面板按鈕 disabled，:85-87）。
* `src/HDBF.UI/Panels/HdbfIndoorPanelFactory.Cards.Maintenance.cs:24-36`：到期日列顯示「無材料維護」而不是天數。
* `src/HDBF.Core/Indoor/IndoorDayTick.cs:139-143` + `BalanceFormulas.cs:150-155`：`cycleDays=0` → `MaintenanceCyclesMissed` 恆回 0 → 基礎檔永不停擺。
* L1：`tests/HDBF.Tests/V2PanelContractTests.cs:418-432`（停用按鈕＋原因字串）、`tests/HDBF.Tests/V2MaintenancePowerTests.cs:106-113`（Basic 永不 stall）。
* 三語鍵：`HDBF.Panel.MaintenanceNone`、`HDBF.Error.MaintenanceUnavailable`（`data/Localizations/{enUS,zhCN,zhTW}.csv`，三檔 194 列鍵集一致）。

**結論（已證實）**：Basic 免材料維護是設計；但 UI 仍保留一顆停用的「執行維護」按鈕（見 F-MAINT-05）。

### 2.3 停擺數學（§19.8 數值，未改動）

```text
cycles = 0                                   today < dueDay
       = 1 + floor((today - dueDay)/cycle)   today >= dueDay     (BalanceFormulas.cs:150-165)
G_maint(0)=1.0  G_maint(1)=0.5  G_maint(>=2)=0（停擺）            (BalanceFormulas.cs:171-179 / IndoorConstants.cs:66-70)
停擺時：G_growth 用 0.05 取代 0（作物仍長，20× 慢）              (IndoorDayTick.cs:149-151 / IndoorConstants.cs:72-78)
停擺只停農事：actionsAllowed = !entranceBlocked && !stalled && mode!=Fallow  (IndoorDayTick.cs:187)
        → IndoorLaborMath.RunActions: !farmActionsAllowed 直接 return（IndoorLaborMath.cs:171）
```

### 2.4 水：三條補水路徑，只有一條是「玩家不用管」的（而且未實機驗證）

| 路徑 | 觸發 | 成本 | 自動？ | 證據 |
|---|---|---|---|---|
| 管道補水 | 需要 **Timber Pipes** 外部模組 + 管道接上 | 免費（按當日需求補） | **是**（但需外部模組） | `IndoorDayTick.cs:79-91`、`IWaterSource`/adapter |
| 搬運工送水 | 原版物流把 `Water` 貨物搬進農場的公共 input inventory，進罐 | 原版搬運 | **理論上是**，但**從未實機驗證** | `HdbfWaterInventoryInitializer.cs:1-7,44-66`、`HdbfBoxFarmController.WaterSupply.cs:146-210`、`CHANGELOG.md:122` |
| 手動補水 | 玩家點「補水」 | 2 個農事動作／趟，+1/3 罐 | **否** | `HdbfBoxFarmController.ManualRefill.cs:27-134`、`IndoorConstants.cs:83-90` |

* 水耗是**連續**扣的（`HdbfBoxFarmController.WaterFlow.cs:25-59`）；**0 工人時 demand=0 → 水不扣**（同檔 :38-44，`EffectiveSlots` 依在崗人數）——所以「沒工人」的農場水也不會掉，但同樣什麼都不做。
* 覆蓋率與灌溉乘數：`IndoorWaterMath.cs:27-42`（每日需求）、`:51-70`（`C=clamp(tank/demand,0,1)`）、`:79-110`（`G_irr`，低於 `0.25×C_min` 時為 0）。
* 水罐空時的連鎖：`IndoorDayTick.cs:131-136`（`C=0`→`NoWater`）、`:192-193`（`waterAvailable = tank>0` → 空罐**不播種**）、`IndoorLaborMath.cs:205-206`（明寫「never plants into a dry tank」）。收穫不受水限制。
* **手動補水一「趟」只有 1/3 罐**，換算成「滿載日」：

| 檔位 | 水罐（單位） | 滿載日需求（單位/日） | 1 趟補水（1/3 罐） | 可支撐 |
|---|---|---|---|---|
| Basic | 3.6 | 60 槽×0.020 = 1.2 | 1.2 | **恰好 1.0 日** |
| Intermediate | 10.8 | 150×0.024 = 3.6 | 3.6 | **恰好 1.0 日** |
| Advanced | 33.6 | 300×0.028 = 8.4 | 11.2 | 1.33 日 |
| Top | 76.8 | 600×0.032 = 19.2 | 25.6 | 1.33 日 |

（數字來源：`TierCatalog.cs:122-127,146-151,170-175,194-199`；`TankCapacity = WaterPerPlotPerDay × PlotCapacity × TankCapacityDays`，同檔 :75；補水比例 `IndoorConstants.cs:86-87`。）

* 手動補水需要：在崗工人、入口未堵、今日剩 ≥2 動作、**12 格內有可用且可走到的乾淨水源**（`ManualRefill.cs:44-86`、`HdbfManualWaterSource.cs:83-137`、`WalkStart.cs:35-61`）。任何一條不成立＝按鈕停用，玩家只能看原因字串。
* 手動補水是**瞬時生效**（`ManualRefill.cs:88-124`）：取水成功即入罐，不派工人走過去、沒有動畫；只扣 2 個動作。

### 2.5 提示管道：面板卡（會滾動）＋世界圖示（只有 1 個）＋節流日誌；沒有通知

* 面板卡序（`HdbfIndoorPanelFactory.cs:92-104`）：工人 → 正在生長 → 槽位 → **水** → **維護** → 動力 → 作物 → 輸出。**維護是第 5 張卡**。
* 面板可視高度會依解析度裁切（`HdbfFarmPanelFragment.Render.cs:205-254`；`MinScrollHeight=160 / MaxScrollHeight=620` 在 `HdbfFarmPanelFragment.cs:45-47`）；而 2.4.2 的實機結論是「第 3 張卡（正在生長）在 1080p 需要滾動才看得到」（`docs/v2/CHANGELOG.md:90-92`）→ **第 5 張的維護卡在 1080p 幾乎確定不在首屏**（高度可能；待截圖確認）。按鈕本身在底部 action card（`Build.cs:140-174`），所以玩家看得到「執行維護」按鈕，卻看不到「到期日／逾期／缺料」。
* 世界圖示每個農場**只顯示 1 個**，優先序 水 > 維護 > 斷電 > 入口堵（`src/HDBF.Core/Indoor/IndoorWarnings.cs:91-119`）；`HdbfWorldIconService.cs:115-184` 註冊 4 個 vanilla StatusToggle、只留 1 個 active。→ 同時缺水＋停擺時，**維護停擺完全看不到**。
* 日誌：`indoor.maintenance` / `indoor.maintenance-missing`（`IndoorLogKeys.cs:36-40`），於日 tick 寫入（`HdbfBoxFarmController.Tick.cs:210-229`），但同一 key 30 秒節流且玩家預設看不到 log。
* **沒有任何 toast／音效／事件通知**：全 `src/` grep `Notification|Toast|Popup|Alert` 只命中下拉選單繪製（`HdbfFarmPanelFragment.Crop.cs`），與通知無關。
* 面板有「缺料」紅字與停擺警告列（`HdbfIndoorPanelWarnings.cs:68-95`），但都在第 5 張卡裡。

### 2.6 「什麼都不發生」的四條獨立成因（症狀相同、解除方式不同）

| 成因 | 觸發條件 | 停止的東西 | 仍然在跑 | 玩家怎麼發現（現況） | 解除方式 |
|---|---|---|---|---|---|
| **A. 水罐見底** | 無管道／無搬運補水，`tank→0` | 生長（`G_irr=0`）、播種（空罐不種） | 成熟槽仍會被收穫；疲勞/輪作照算 | 面板水卡（第 4 張卡）＋世界圖示（水優先，看得到）＋`water.settle`/`water.refill-failed` | 點「補水」、接管道、靠搬運 |
| **B. 維護停擺** | 材料檔位逾期 ≥2 期（10 日/期；離網 Top 8 日） | 播種、收穫；`G_maint` 名目 0，生長降到 0.05 | 生長（極慢）、維護/水/動力計算 | 面板維護卡（第 5 張卡，需滾動）＋世界圖示**僅在水正常時**才看得到＋`indoor.maintenance` 日誌 | 點「執行維護」**1–3 次**（付料） |
| **C. 無工人／入口堵** | 工人 0 或入口被擋 | 播種、收穫（`NoWorkers`/`EntranceBlocked`） | 生長、水耗（0 工人時水也不扣） | 面板工人卡＋世界圖示（入口堵） | 補聘工人、清入口 |
| **D. 農場暫停** | 玩家按暫停 | 日 tick、連續水耗、世界圖示**全停**（v2.5.3） | 幾乎沒有（時間感上完全靜止） | 建築暫停外觀 | 取消暫停 |
| （E. 休耕模式） | 模式=休耕 | 播種、收穫（設計如此） | 生長照舊 | 面板模式列 | 切回常規 |

**體感疊加**：A 與 B 可以同時發生，且互為因果（缺水 → 沒收穫 → 沒收入；停擺 → 沒收穫）。兩者的世界圖示**只會顯示水**，面板維護卡又在摺線下 → 玩家最可能的解讀是「這東西壞了／全部都要我手動」。

---

## 3. 問題清單（F-MAINT-xx）

### F-MAINT-01【設計，非 bug】維護沒有任何自動路徑；與工作坊文案的期待落差

* **現象**：玩家必須自己在面板按「執行維護」；不按就永遠不會被維護，逾期 1 期 `G_maint=0.5`、逾期 2 期停擺。merlin4059 的「beavers won't do upkeep」字面成立。
* **原因（已證實）**：設計就是手動（`REQ_V2_SECTION_19.md` §19.11 面板表「維護｜到期日、逾期周期、缺料；按鈕『執行維護』」；§19.7 動作優先序只有「收穫 > 播種 > 補水」，**沒有維護**）。實作只提供面板命令（見 §2.1），日 tick 只讀不寫。
* **期待落差（已證實，文案問題）**：`docs/workshop/DESCRIPTION.md:56` 與 `docs/workshop/PAGE_COPY.txt:22` 寫「Miss it and the farm stalls **until a beaver performs maintenance**」——暗示比佛會做維護。實際是**玩家**按按鈕。這正是 Eltharion 問題的直接來源。
* **信心度**：已證實（程式與 L1 契約、文案原文均可查）。
* **建議**：新增「自動維護」開關（預設 ON，見 §4 方案 A）；同時修正工作坊文案為「until you perform maintenance (or enable automatic maintenance)」。

### F-MAINT-02【Bug，P0】逾期 ≥2 期時，按一次「執行維護」仍然停擺；面板數字與 tooltip 同時誤導

* **現象**：玩家看到「維護逾期」「農場已停擺」，按下「執行維護」（成功扣料、顯示「維護完成。」），農場**還是停擺**；隔天面板逾期數又變回 2。
* **機制（已證實，純數學）**：`TryApplyMaintenance` 做的是 `nextDue = 舊 dueDay + 1 個週期`（`Commands.cs:254-258`），**不是**重置到 `today + 週期`。逾期 k 期時，一次點擊只會讓 k 減 1，但停擺門檻是 k≥2：

```text
例：中級（cycle=10），dueDay=2，今日=25
  現在：cycles = 1 + floor((25-2)/10) = 3  → 停擺
  按 1 次（付 8 木板）：due=12 → cycles = 1 + floor(13/10) = 2 → 仍停擺
  按 2 次（累計 16 木板）：due=22 → cycles = 1 + floor(3/10) = 1 → 解除停擺，但 G_maint=0.5
  按 3 次（累計 24 木板）：due=32 → cycles = 0 → 回到 G_maint=1.0
```

* **連帶誤導（已證實）**：
  1. `TryApplyMaintenance` 立刻把 `record.MaintenanceCyclesMissed=0`（`Commands.cs:266`），面板「逾期週期」當場顯示 0，但警告列用上一日日 tick 的 `_lastWarnings`（`IndoorBoxFarmController.IndoorSnapshot.cs:65,76`）→ 卡片同時顯示「逾期週期 0」與紅色「維護逾期／停擺」；隔日 tick 又把逾期數算回 2（`IndoorDayTick.cs:220-224`）。
  2. 三語 tooltip 與行為不符：`HDBF.Tooltip.Maintenance`＝「消耗維護材料並**重置維護倒數**」（enUS: "resets the maintenance timer"）、`HDBF.Tooltip.MaintenanceShutdown`＝「農場已停擺，**執行維護可恢復產出**」——逾期 ≥2 期時兩句都不成立。
  3. 按鈕沒有任何「還需要按幾次」或「本次維護只能追回 1 期」的提示；日誌只寫 `indoor maintenance scheduled, dueDay=…`（`Commands.cs:269-271`）。
* **分級**：**Bug**（付了材料卻沒有承諾的效果；且畫面數字自相矛盾）。這也很可能是「按了 manual，otherwise nothing happens」最直接的實例。
* **信心度**：已證實（`BalanceFormulas.MaintenanceCyclesMissed` 與 `TryApplyMaintenance` 皆為純邏輯；可用 L1 重現，見 §6）。
* **建議**：方案 B（§4）——追平付費（一次點擊付 k 期料、`dueDay += k×cycle`）或改成 `dueDay = today + cycle` 並同步修文案；兩者都要修掉「歸零顯示」與 tooltip。

### F-MAINT-03【UX，P1】維護狀態在面板摺線下、世界圖示被水遮蔽、沒有「為什麼閒置」與通知

* **現象**：玩家只看到底部「執行維護」按鈕，不知道「現在該不該按、按了會怎樣、是不是已經停擺」。
* **證據（已證實／高度可能）**：
  * 維護卡是第 5 張卡（`HdbfIndoorPanelFactory.cs:92-104`），面板卡區會依解析度裁切並隱藏滾動條（`Render.cs:205-254`、`Build.cs:59-78`）；2.4.2 的實機紀錄指出第 3 張卡在 1080p 就需滾動（`docs/v2/CHANGELOG.md:90-92`）→ 第 5 張幾乎確定看不到（**高度可能**，需截圖確認）。
  * 世界圖示一棟農場只有 1 個，水優先（`IndoorWarnings.cs:91-119`）→ 缺水時維護停擺不可見（**已證實**）。
  * 沒有任何通知／音效（**已證實**，grep 無 Notification/Toast/Popup）。
  * 面板沒有「閒置原因」欄位：`Fallow`／無工人／入口堵／缺水／停擺／輸出滿 分散在不同卡片（**已證實**）。
* **建議**：方案 D（§4）：面板頂部常駐狀態列（含「下一次維護到期」「自動維護：開/關」「閒置原因」）、維護卡移到水卡之前或併入首卡、世界圖示允許水＋維護同時輪播（需實機確認 vanilla `StatusIconCycler` 行為）。

### F-MAINT-04【未驗證風險，P0】「維護扣料」與「人工補水正路徑」從未在實機跑過

* **證據（已證實的「未驗證」事實）**：
  * `README.md:131`：未驗證清單含「人工補水正路徑、維護材料扣料、頂級接電、管道實際供水」。
  * `docs/VERIFICATION.md:783`：「仍未驗證：…維護材料扣料（Basic 無材料維護，需中/高級檔）…」；`:1223-1226`：「人工補水正路徑：注入實體缺少原版 Accessible/access 元件，日誌每次都說 `water.manual-refill: farm has no walk access`；這是 fixture 限制」「維護材料扣料（Intermediate+）…本輪 Basic 檔、無材料維護」。
  * `docs/v2/CHANGELOG.md:227-228` 同樣列為未實機。
* **風險（推測，未證實）**：若實機上「最近庫存扣料」或「12 格內水源探測」有問題，玩家會遇到按鈕按了沒反應／補水永遠停用，症狀正是「什麼都不發生」。目前只有 L1 契約（`V2GameplayWiringTests` / `V2MaintenancePowerTests`）與原始碼支撐，**沒有任何一筆實機日誌證明這兩條正路徑可用**。
* **建議**：把它列為本輪第一優先實機驗證（§7 步驟 A/B/D），再決定是否需要修路徑本身。

### F-MAINT-05【UX，P2】基礎檔沒有材料維護，但面板仍顯示停用的「執行維護」

* **現象**：Basic 玩家看到一顆永遠按不下去的「執行維護」，以及「無材料維護」；Eltharion 很可能就是這樣理解成「這遊戲要我手動維護」。
* **證據（已證實）**：按鈕永遠建立（`Build.cs:158-166`）、`CanPerformMaintenance=false`（`HdbfIndoorPanelFactory.cs:217-220`）、按鈕只改 enabled 未隱藏（`Render.cs:168-177`）、失敗鍵 `HDBF.Error.MaintenanceUnavailable`＝「該檔位沒有材料維護。」
* **建議**：Basic（`IrrigationOnly`）與接電頂級直接**隱藏**維護按鈕，或把它換成一句被動說明（「本檔位免材料維護，請注意補水」）。

### F-MAINT-06【設計負擔，P1】沒管道時每棟每 1–3 個遊戲日就要手動補一次水

* **現象**：手動補水一趟只補 1/3 罐，換算剛好 1 個滿載日（Basic/Intermediate）或 1.33 日（Advanced/Top）（見 §2.4 表）；面板沒有「補到滿」選項，按一次只能補 1/3。
* **證據（已證實，算式見 §2.4）**：`IndoorConstants.cs:86-87`（1/3）、`ManualRefill.cs:76,92`（`RefillAmount` 固定 1/3 罐）；且每次都要重新探測水源（`RefreshWaterSupply`，`ManualRefill.cs:141-172`）。
* **設計上是否合理**：§19.6 明寫「增加 `TankCapacity / 3` 單位（≈1 個滿負荷日）」＝**設計如此**。但設計沒有考慮「多棟農場、每 1–3 日手動點擊」的**操作量**。
* **建議**：不改單趟數值，只改操作：面板「補水」可連續派 N 趟直到 `C ≥ 1` 或動作不足；另加「一鍵補水全部農場」（多棟時）。

### F-MAINT-07【表現落差，P1】補水是瞬時取水，完全看不到比佛行為

* **現象**：點「補水」→ 水罐當場變多，沒有任何比佛走出建築取水；只扣 2 個動作（玩家看不到）。
* **證據（已證實）**：`ManualRefill.cs:88-124`（註解：the refill is applied the moment the player asks for it… it no longer waits for the next daily tick）、`:113`（`_actionsUsedToday += 2`）。
* **設計對照**：§19.6 / `REQ-V2-WATER-04` 原意是「工人每趟…消耗 2 個農事動作 + 外部往返，不得瞬移」，實作已演化成「驗證可達後瞬時取水」（2.4.0 的玩家要求）。這是**已被接受的行為變更**，但代價是玩家完全沒有「有人在做事」的回饋。
* **建議**：至少讓面板／日誌明示「本趟消耗 2 個農事動作」；若要做表現，可讓 1 名在崗農夫短暫離開（不做尋路，只播放 vanilla 取水行為/動畫），或至少在面板加「工人正在取水」的狀態行。**注意不要因此改平衡數值。**

### F-MAINT-08【UX／潛在，P2】沒有主動通知；`features.*` 有 5 個死旗標

* **現象**：維護到期／停擺／缺水只反映在面板、世界圖示、log；沒有通知、沒有聲音、沒有事件計數。
* **證據（已證實）**：
  * 面板警告列會被摺線藏住（F-MAINT-03），世界圖示只有 1 個（`IndoorWarnings.cs:91-119`），log 30 秒節流（`Architecture` §6、`IndoorLogKeys.cs` 檔頭）。
  * **死旗標**：`data/HDBF_Config.json` 的 `features.{densePlanting,rotation,maintenance,powerConnection,pipeConnection,logisticsOutput,soilFatigue}` 全部被解析（`src/HDBF.Core/Config/HdbfConfigCodec.Sections.cs:19-33`），但只有 `densePlanting`（`Commands.cs:73`）與 `powerConnection`（`Context.cs:198`）被讀取；`features.maintenance` 等 5 個**沒有任何消費者**（全 `src/` grep 可證）。玩家若改 `"maintenance": false` 期待關閉維護，不會有任何效果。
* **建議**：新增日誌鍵（`indoor.maintenance-auto`／`indoor.maintenance-auto-missing`／`water.auto-refill`／`water.auto-refill-failed`）＋面板常駐狀態列；`features.maintenance` 要嘛接上（關閉＝免維護，明確跳出「非預設」提示與存檔相容說明），要嘛從 config 移除；`features.autoMaintenance`／`features.autoRefill` 可作為新開關的預設來源。

---

## 4. 維修方案（保留 §19.8 數值門檻與代價；不改任何平衡數字）

> 共同原則：**週期 10/10/8 日、材料 8/6/15、停擺門檻（1 期×0.5、2 期停擺）、停擺只停農事、停擺期間生長 0.05 全部不動。** 自動化只把「玩家記得按按鈕」換成「農場每天自己按」，**付的料與動作成本完全相同**，因此不是作弊、不影響經濟。

### 方案 A（P0）：可選「自動維護」——最小實作位置＝Gameplay 日 tick 編排 + Core 純函式

**行為規格**

| 項目 | 規格 |
|---|---|
| 開關 | 每棟農場持久化 `AutoMaintenance`（bool）；預設值來自 config `features.autoMaintenance`（建議預設 **true**；舊存檔缺欄位 → 用 config 預設，且不重排 `dueDay`，因此不會出現免費維護） |
| 觸發 | 每日 tick（`RunDailyTick`）在 `IndoorDayTick.Run` **之前**：`AutoMaintenance && tier 需要材料 && dueDay <= today` |
| 動作 | `k = CyclesToCatchUp(dueDay, today, cycle)`（Core 純函式，`k ≥ 1`）→ 帳單 `k × amount`（含離網/斷電 ×2 規則）→ 從最近已完工地區庫存一次扣足（all-or-nothing）→ `dueDay += k × cycle`、`cyclesMissed = 0` |
| 缺料 | 不扣任何料、不推進；寫 `indoor.maintenance-auto-missing`（帶 needed/available/item）、面板沿用 `MissingMaterial` 紅字＋「缺料：木板 ×8」；保持停擺（誠實：沒料就是不能維護），**手動按鈕仍然可用** |
| 不需要材料 | `IrrigationOnly`（Basic）與接電頂級（`cycleDays<=0`）→ 完全不動作（與手動一致，`Commands.cs:218-229`） |
| 不該觸發 | `dueDay > today`（尚未到期）→ 不動作（避免提前燒料）；`Fallow` 模式→ 仍然維護（維護與模式無關） |
| 成本 | 與手動完全相同（料 + 0 動作）。**不建議**額外加動作成本，否則等於改平衡 |
| 日誌 | `indoor.maintenance-auto`（成功：k/amount/item/dueDay）、`indoor.maintenance-auto-missing`（缺料） |
| 事件 | 自動維護成功時把 `tryGetMaintenancePlan` 等邏輯收斂到單一入口，避免手動/自動兩份實作分叉 |

**為什麼放在 Gameplay 而不是 Core 日 tick**
`ARCHITECTURE.md` §1 規則 5：「`Gameplay` 只做遊戲對象橋接與編排，公式與狀態遷移只在 Core」。扣料是**外部世界 I/O**（inventory），不應放進純函式 `IndoorDayTick`。但**決策**（要追幾期、該不該付）必須是 Core 純函式才能 L1 覆蓋。現成 seam 已經存在：

* `IMaintenanceMaterialSource`（`src/HDBF.Core/Indoor/IMaintenanceMaterialSource.cs:13-27`）**本來就有 `TryConsume(itemId,count)`**，而且 `IndoorMaintenanceMaterialSource` 已實作（`:87-132`）——**目前生產程式碼完全沒用到這個方法**（只有測試呼叫），是為自動化預留卻未接上的接口。
* 控制器已注入 `DistrictCenterRegistry`（`HdbfBoxFarmController.cs:58,165`）與 `_log`，可直接建立一個 `HdbfMaintenanceTransaction` 實例（與 UI 路徑共用同一套「最近庫存」語意，`HdbfMaintenanceTransaction.cs:51-133`）。
* 「追平 k 期」的數學放 `HDBF.Core.Balance.MaintenanceDefaults`（已有 `CycleDaysFor` / `ResolveDueDay`，`MaintenanceDefaults.cs:27-110`），新增 `CyclesToCatchUp(due, today, cycle)` + `CanAfford(profile, record, k)`。

**受影響檔案（方案 A）**

| 層 | 檔案 | 改動 |
|---|---|---|
| Core | `src/HDBF.Core/Balance/MaintenanceDefaults.cs` | 新增 `CyclesToCatchUp`、`NextDueAfterCatchUp`、`RequiredMaterial(profile, record, k)`（純函式） |
| Core | `src/HDBF.Core/Indoor/IndoorFarmRecord.cs` | 新增 `AutoMaintenance`（bool，預設 false，由載入/新建時套 config 預設） |
| Core | `src/HDBF.Core/Indoor/IndoorSaveCodec.cs` | 寫/讀 `autoMaintenance` 欄位（容錯：缺 → 預設） |
| Core | `src/HDBF.Core/Indoor/IndoorLogKeys.cs` | 新增 `indoor.maintenance-auto`、`indoor.maintenance-auto-missing` |
| Core | `src/HDBF.Core/Config/HdbfConfig.cs` / `HdbfConfigCodec.Sections.cs` | 新增 `features.autoMaintenance`（並決定 `features.maintenance` 去留） |
| Persistence | `src/HDBF.Persistence/Indoor/IndoorSaveKeys.cs` | 新增 `AutoMaintenance` 的 `PropertyKey<bool>` |
| Persistence | `src/HDBF.Persistence/Indoor/IndoorEntitySaveBridge.KeyedFields.cs` / `.Keyed.cs` / `.cs` | 寫入/讀取/fallback（缺鍵＝預設） |
| Gameplay | `src/HDBF.Gameplay/Components/HdbfBoxFarmController.Tick.cs` | `RunDailyTick` 內、`IndoorDayTick.Run` 前呼叫 `TryAutoMaintain()`；結果併入 warnings/日誌 |
| Gameplay | **新檔** `src/HDBF.Gameplay/Components/HdbfBoxFarmController.AutoMaintenance.cs` | `TryAutoMaintain()`：plan → k 期帳單 → `HdbfMaintenanceTransaction.TryConsumeNearest` → `TryApplyMaintenance(k×cycle)`；隔離例外（REQ-V2-SAFE-01） |
| Gameplay | `src/HDBF.Gameplay/Components/HdbfBoxFarmController.Commands.cs` | `TryApplyMaintenance` 增加可選 `cycles` 參數（預設 1，手動路徑不變） |
| Gameplay | `src/HDBF.Gameplay/Components/HdbfFarmCommandService.cs` | 手動路徑可重用同一 catch-up 計算（讓手動按鈕也一次追平，見方案 B） |
| UI | `src/HDBF.UI/Panels/HdbfFarmPanelFragment.Build.cs` / `.Render.cs` / `.Commands.cs` | 新增「自動維護」開關（vanilla Checkbox/Toggle）；顯示下次到期與 k 期提示 |
| UI | `src/HDBF.UI/Panels/HdbfIndoorPanelData.cs` / `HdbfIndoorPanelFactory.cs` / `HdbfIndoorCommandBridge.cs` | 面板資料增加 `AutoMaintenance`、`IdleReason`、`CyclesToCatchUp`；新增命令 `TrySetAutoMaintenance` |
| UI | `src/HDBF.UI/Localization/HdbfUiLocKeys.cs` | 新增鍵：`HDBF.Panel.AutoMaintenance`、`HDBF.Tooltip.AutoMaintenance`、`HDBF.Action.AutoMaintenanceOn/Off`、`HDBF.Panel.NextDueIn`、`HDBF.Panel.NeedMaintenanceClicks` |
| data | `data/HDBF_Config.json` | 新增 `features.autoMaintenance`（預設 true） |
| Localizations | `data/Localizations/enUS.csv` / `zhCN.csv` / `zhTW.csv` | 三語同步新增鍵（現有 194 列、鍵集一致，需維持一致） |
| tests | 新檔 `tests/HDBF.Tests/V2AutoMaintenanceTests.cs`；改 `V2TickOrderTests`、`V2SaveCodecTests`、`V2PanelContractTests`、`V2LocalizationTests` | 見 §6 |
| tools（唯讀調查，不在本輪改） | `tools/dev/CraftIndoorFixture/Program.cs` | 驗證需要 `--due-day` / `--cycles-missed` / `--auto-maintenance`（方案 A 落地時再加） |

### 方案 B（P0）：修 F-MAINT-02 —— 讓一次維護真的能解除停擺，並讓文案與面板一致

兩個選項（**保留「不得免費維護」**）：

* **B-1（推薦）追平付費**：一次點擊＝付 `k` 期料、`dueDay += k×cycle`（`k = 1 + floor((today-dueDay)/cycle)`，即讓 `cycles` 歸零的最小整數）。料不足 `k` 期 → 拒絕並顯示「需要 24 木板（3 期）」＋`HDBF.Error.MaterialsMissing`。經濟與原設計一致（每期都付），且一次修好。
* **B-2 reset 語意**：`dueDay = today + cycle`，付 1 期料。最符合現有 tooltip「重置倒數」與玩家直覺；代價是逾期很久時只付 1 期料（比 B-1 少付），等於給 ≤1 期的寬限。
* **共同必改**：`MaintenanceCyclesMissed` 不要在 apply 時先歸零後又被 tick 算回（改成快照即時重算或等 tick 更新）；tooltip 三語改成與行為一致（B-1：「本次維護會追平所有逾期週期並扣除對應材料」；B-2：「重置維護倒數到一個週期後」）；停擺時面板顯示「需再維護 n 期」。

### 方案 C（P1）：自動補水（讓「比佛做 upkeep」成真），並驗證搬運路徑

* **C-1 自動取水**：每棟持久化 `AutoRefill`（bool，預設來自 config）。日 tick 時，若無管道水、`tank < demandPerDay`（或 `C < C_min`）、在崗工人 > 0、入口未堵、動作預算 ≥2 → 自動執行與手動相同的取水流程（探測 12 格內可達水源 → 扣 2 動作 → `+TankCapacity/3`）。失敗時寫 `water.auto-refill-failed`（原因沿用既有 `HDBF.Error.*`），並保留手動按鈕。**成本與手動完全相同**（2 動作/趟），不改平衡。
* **C-2 搬運水驗證**：先實機確認 `Inventory:Water` 是否真的會被原版搬運工自動補（`HdbfWaterInventoryInitializer` 的註解聲稱如此，但 README/VERIFICATION 從未驗證）。若不通，兩個選擇：(i) 修链（確認 `HasPublicInput` + 物流優先權是否讓搬運工送水）；(ii) 修文案（不要寫「by hauler trips」）。
* **C-3 補水操作改良**：單次點擊可連續派多趟直到 `C ≥ 1` 或動作不足（單趟仍為 1/3 罐，不改 §19.6 數值）；多棟時提供「一鍵補水全部農場」。
* 受影響：`HdbfBoxFarmController.ManualRefill.cs`（重用）、`Tick.cs`（呼叫）、`IndoorFarmRecord`/存檔（`AutoRefill`）、UI（開關＋狀態列）、Localizations、`V2WaterTests`。

### 方案 D（P1）：讓「為什麼不動」一秒可見

1. **面板常駐狀態列**（放在首卡或 scroller 上方固定列）：`✅ 運作中` / `⚠ 缺水（剩 0.8 日）` / `⛔ 維護停擺（逾期 2 期，需再維護 1 次）` / `⏸ 已暫停` / `👷 無工人` / `🚧 入口堵塞` / `📦 輸出滿` / `🌱 休耕中`。由 Core 純函式 `IdleReason`（輸入 flags/cycles/tank/workers/mode/paused）產生 → L1 可測。
2. **維護卡位置**：把「維護」卡移到「水」卡之前（或把到期／逾期／缺料三行併進首卡）。按鈕仍留在底部 action card。
3. **世界圖示**：允許「水」與「維護」兩個 StatusToggle 同時 active（讓 vanilla cycler 輪播），或把停擺（P0）優先於低水（P1）——需實機確認 vanilla `StatusIconCycler` 多 active 的行為。
4. **通知**：先在 stall 發生當日寫一條非節流的 `HDBF/Warn`；音效／toast 需先確認 vanilla API（本輪未查，標「待確認」）。
5. `features.maintenance` 死旗標：接上（關閉＝免維護，明確 log `config.feature-disabled`）或移除。

### 方案 E（P2）：文件與文案

* 工作坊描述／PAGE_COPY 的「until a beaver performs maintenance」改成實際行為（或等方案 A 上線後保留，但要寫「自動維護需開啟」）。
* 面板「無材料維護」→ 基礎檔隱藏維護按鈕（F-MAINT-05）。
* `HDBF.Error.RefillUnavailable`＝「本版本尚未開放人工補水。」（enUS: "Manual refill is not available in this build."）是誤導字串：這個失敗鍵其實是 runtime 取水失敗，不是「版本沒開」。改成「本次取水失敗」。
* 更新 `README.md` §5 未驗證表、`docs/VERIFICATION.md`、`TRACEABILITY`（由 Lead 維護）。

---

## 5. 受影響檔案總表（依層）

| 層 | 檔案（現況唯讀；方案落地時才改） | 對應 |
|---|---|---|
| Core | `Balance/MaintenanceDefaults.cs`、`Indoor/IndoorMaintenanceMath.cs`、`Indoor/IndoorFarmRecord.cs`、`Indoor/IndoorSaveCodec.cs`、`Indoor/IndoorDayTick.cs`、`Indoor/IndoorLogKeys.cs`、`Indoor/IndoorDayContracts.cs`、`Indoor/IndoorWarnings.cs`、`Config/HdbfConfig.cs`、`Config/HdbfConfigCodec.Sections.cs` | A、B、C、D |
| Persistence | `Indoor/IndoorSaveKeys.cs`、`Indoor/IndoorEntitySaveBridge*.cs` | A、C（新欄位） |
| Gameplay | `Components/HdbfBoxFarmController.Tick.cs`、`Commands.cs`、`ManualRefill.cs`、`WaterSupply.cs`、`IndoorSnapshot.cs`、`HdbfFarmCommandService.cs`、`HdbfMaintenanceTransaction.cs`、`IndoorMaintenanceMaterialSource.cs`、`HdbfWorldIconService*.cs`、`HdbfManualWaterSource*.cs` | A、B、C、D |
| UI | `Panels/HdbfFarmPanelFragment.{Build,Render,Commands}.cs`、`HdbfIndoorPanelFactory.cs`、`HdbfIndoorPanelData.cs`、`HdbfIndoorCommandBridge.cs`、`HdbfIndoorPanelWarnings.cs`、`Localization/HdbfUiLocKeys.cs` | A、D |
| data | `data/HDBF_Config.json` | A、C |
| Localizations | `data/Localizations/enUS.csv`、`zhCN.csv`、`zhTW.csv` | A、B、C、D（三語鍵集必須一致） |
| tests | 新 `V2AutoMaintenanceTests.cs`、新 `V2IdleReasonTests.cs`；改 `V2TickOrderTests.cs`、`V2SaveCodecTests.cs`、`V2PersistenceBridgeContractTests.cs`、`V2PanelContractTests.cs`、`V2LocalizationTests.cs`、`V2MaintenancePowerTests.cs`、`V2GameplayWiringTests.cs`、`V2NfrCodeContractTests.cs`（檔案行數/Contracts） | A、B、C、D |
| tools | `tools/dev/CraftIndoorFixture/Program.cs`（新增 fixture 參數，僅驗證用） | 驗證 |

---

## 6. L1 契約測試（game-free，`dotnet run --project tests/HDBF.Tests`）

> 測試框架：`tests/HDBF.Tests/TestRunner.cs` 以反射自動發現「public static 類別 + 名稱 `Test*` 的 public static 無參方法」，新檔不需改 runner。

**F-MAINT-02（B 方案）**
1. `TestCatchUpCyclesMatrix`：`CyclesToCatchUp(2,25,10)=3`、`(2,12,10)=2`、`(2,11.9,10)=1`、`(2,2,10)=1`、`(32,25,10)=0`（未到期→0）。
2. `TestSingleMaintenanceCatchUpClearsStall`：due=2、today=25、cycle=10 → 套用後 due=32、`MaintenanceCyclesMissed=0`、`IsStalled=false`。
3. `TestOneCycleBillCannotClearTwoMissedCycles`（回歸釘住舊 bug）：明確斷言「只付 1 期」時 cycles 仍 ≥2。
4. `TestMaintenanceCountdownDoesNotResetFree`：未到期（due=40、today=25）時 `CyclesToCatchUp=0`、無動作。
5. `TestMaintenanceTooltipMatchesCatchUpBehaviour`：三語 tooltip 字串含「追平／all overdue」語意（字串契約，與 `V2LocalizationTests` 同风格）。

**F-MAINT-01（A 方案）**
6. `TestAutoMaintenancePaysSameBillAsManual`：同 record/檔位，自動與手動產生的扣料 item/amount 完全相同。
7. `TestAutoMaintenanceOnlyRunsWhenDue`：due=40/today=25 → 不扣料、due 不變。
8. `TestAutoMaintenanceSkipsWithoutMaterial`：`IMaintenanceMaterialSource.Available<bill` → 不扣料、due 不變、`MissingMaterial` flag、log key `indoor.maintenance-auto-missing`（以測試替身記錄呼叫）。
9. `TestAutoMaintenanceDisabledDoesNothing`：`AutoMaintenance=false` → 0 消費、0 變更。
10. `TestAutoMaintenanceIgnoresBasicAndOnGridTop`：Basic／接電 Top → 不動作（`TryGetMaterialBill=false`）。
11. `TestAutoMaintenanceOffGridTopBillsFifteenMetalPerCycle`：離網 Top、逾期 2 期 → 30 金屬、cycle=8 → due+16。
12. `TestAutoMaintenanceStalledGrowthStillZeroPointZeroFive`：自動維護後下一 tick 的 `GrowthMultiplier` 回到 1.0（不是 0.05）。
13. `TestAutoMaintenanceSettingRoundTripsThroughSave`：`Encode→Decode` 保留 bool；缺欄位 payload → 預設值；v1 payload 遷移後 → 預設值。
14. `TestAutoMaintenanceDefaultComesFromConfig`：`features.autoMaintenance=false` → 新建/載入預設 false。

**F-MAINT-03/05/08（D 方案）**
15. `TestIdleReasonPriority`：水→維護→工人→入口→輸出→休耕→暫停 的優先序與字串鍵（純函式矩陣）。
16. `TestPanelShowsNextDueAndCatchUpHint`：`IndoorFarmPanelData` 含 `AutoMaintenance`、`NextDueIn`、`CyclesToCatchUp`；停擺時提示「需再維護 n 期」。
17. `TestBasicTierHidesMaintenanceAction`：`IrrigationOnly` → 新旗標 `ShowMaintenanceAction=false`（取代只 disable）。
18. `TestNewLocalizationKeysShipInAllThreeLanguages`（擴充既有 `V2LocalizationTests` 的鍵集一致性斷言）。
19. `TestConfigFeatureFlagsHaveConsumers`（NFR 契約）：列舉 `HdbfConfig.Features` 每個欄位，斷言在 `src/` 有讀取點——防止再生死旗標。

**F-MAINT-06/07（C 方案）**
20. `TestAutoRefillTriggersBelowOneDayOfWater`、`TestAutoRefillCostsTwoActions`、`TestAutoRefillNeverTeleportsBeyondProbe`（source 為 null → 不補）、`TestAutoRefillRespectsManualToggleOff`。

**既有回歸**：全量 L1（v2.5.3 基線 **613 PASSED / 0 FAILED**）必須維持；`V2TickOrderTests` 的固定順序若因新增「auto 維護階段」需更新，必須同步更新 `docs/v2/ARCHITECTURE.md` §3 的順序表（文件與程式同時改，否則 NFR 文件門禁會紅）。

**（本輪不執行）** `build.ps1` 全量門禁由 Lead 執行：5 程序集 0/0 → L1 全綠 → `tools/validate` → 44 release gates。

---

## 7. 實機 fixture 驗證步驟（部署後；需可互動或半自動回合）

前置：`build.ps1 -Install` 部署到 `Documents\Timberborn\Mods\HDBF`；回合腳本 `tools/dev/Invoke-GameRound.ps1 -Action Prepare|Launch|SpeedUp|Stop`（README §4）。**fixture 需先擴充**（目前 `CraftIndoorFixture` 只有 `--tier/--faction/--mode/--crop/--growth/--planted/--tank/--out`，`Program.cs:34-54`）：新增 `--due-day`、`--cycles-missed`、`--auto-maintenance on|off`、`--auto-refill on|off`。

| # | 情境 | fixture | 預期（可判讀證據） |
|---|---|---|---|
| A | **自動維護（缺料→有料）** | Intermediate、`--due-day` = 載入日−25（3 期）、地區倉庫放 24 木板、`auto-maintenance on` | 載入後首個日 tick：`indoor.maintenance-auto`（item=Plank, amount=24, k=3）、`dueDay` 前進 30、`cyclesMissed=0`、`indoor actions harvested/planted > 0`、面板無停擺 |
| B | **自動維護缺料** | 同 A 但倉庫只有 8 木板 | `indoor.maintenance-auto-missing`（needed=24, available=8）、倉庫仍 8、`cyclesMissed` 不變、面板「缺料：木板 ×8」、維持停擺 |
| C | **手動對照（重現 F-MAINT-02）** | Intermediate、due=載入日−25、`auto-maintenance off`、倉庫 24 木板 | 點「執行維護」1 次 → 顯示「維護完成」但**仍停擺**（`growthMultiplier≈0.05`、`indoor actions=0`）；再點 2 次（共 24 木板）→ 解除。修 B 方案後：1 次即解除且扣 24 木板 |
| D | **人工補水正路徑**（F-MAINT-04） | Basic、`--tank 0.2`、附近 12 格內有水塔/滿水水體、2 名工人 | 面板「補水」可按 → 點擊後 tank +1.2 且 `water.manual-refill` 日誌；`tank` 顯示與日誌一致（**這是首次正路徑證據**） |
| E | **無水源降級** | 同 D 但拆除所有水源 | 按鈕停用、原因「12 格內沒有可用水源。」、`water.manual-refill` 節流警告；無例外 |
| F | **搬運水是否真的自動**（F-MAINT-04/方案 C-2） | Basic、tank 半滿、農場旁倉庫放 `Water`、物流開啟、**不點任何按鈕** | 觀察 `water.settle hauled water accepted` 是否自然出現；若 3 個遊戲日內完全沒有 → 「hauler trips」宣稱不成立，需修链或修文案 |
| G | **面板可見性與圖示**（F-MAINT-03） | 同時讓水與維護都壞（tank=0 + due 逾期 3 期） | 1080p 與 1440p 各截一張面板：維護狀態是否在首屏；世界圖示顯示哪一個；日誌同時有 `water.refill-failed` 與 `indoor.maintenance` |
| H | **存檔往返** | 設 `auto-maintenance on` → 存檔 → 讀檔 | 開關保留、`dueDay` 不變（不得免費重排）；老存檔（無此欄位）讀入後＝config 預設且 `dueDay` 逐位不變 |

> 每一回合都要保存 `docs/in-game/<tag>-Player.log` 與 summary（既有慣例），並在 `docs/VERIFICATION.md` 新增一節（由 Lead 維護）。**沒有日誌就不算驗證完成。**

---

## 8. 未解問題／需要 Lead 決策

1. **自動維護預設值**：預設 true（多數玩家期待）或 false（保守、不改變既有行為）？我建議 **true**，因為材料成本完全相同、且工作坊文案已暗示「有人會做維護」。是否對既有存檔也套用預設？（建議：套用，因為不重排 `dueDay`，不會產生免費維護。）
2. **F-MAINT-02 修法**：B-1 追平付費（推薦，保留每期付費）或 B-2 reset 語意（最符合 tooltip 直覺，但逾期久時少付）。
3. **autoRefill 是否納入本輪**：經濟上與手動等價（同為 2 動作/趟），但它是「新增行為」，需要明確的 product 決定；本輪可先只做「自動維護 + 狀態列」。
4. **是否修改工作坊文案**（`docs/workshop/DESCRIPTION.md:56`、`PAGE_COPY.txt:22`）——這個檔不屬於 `src/`，但屬於發布內容；請 Lead 決定由誰改。
5. **世界圖示多狀態輪播**的可行性：需實機看 vanilla `StatusIconCycler` 多 active 行為；本輪未驗證。
6. **F-MAINT-04 的驗證歸屬**：實機回合需要可互動會話或半自動腳本（面板按鈕需要合成輸入）；README §5 已記錄本機無法合成 UI 輸入，這會卡住 A–H 中的按鈕情境（C/D/E）——需要 Lead 決定由誰、用什麼方法跑。
7. `tools/dev/CraftIndoorFixture` 的新參數由誰改（工具屬 Lead 工作流，本卡不可寫）。
8. `features.maintenance` 死旗標的處置（接上或移除）可能與「潛在 bug／未來回報預測」工作流重疊，請 Lead 去重。

---

## 9. 誠實分級總結

| 分級 | 內容 |
|---|---|
| **已證實（可從原始碼/測試/文件逐行核對）** | 維護無自動路徑（§2.1）；Basic 免材料維護且按鈕停用（§2.2）；停擺數學與只停農事（§2.3）；水三條路徑與手動補水 1/3 罐（§2.4）；面板卡序、世界圖示單一優先、無通知（§2.5）；F-MAINT-02 的「一次點擊只追 1 期」與面板/tooltip 矛盾（§3）；F-MAINT-06 的每趟 1 個滿載日換算；F-MAINT-07 瞬時補水；F-MAINT-08 的死旗標；「維護扣料/人工補水正路徑未實機驗證」這件事本身。 |
| **高度可能（有強證據，仍需一項實機/截圖確認）** | 維護卡在 1080p 面板首屏之外（依 2.4.2 的同類實機結論推得）；Basic 玩家把停用的維護按鈕理解成「必須手動維護」；玩家會把水耗盡與停擺都解讀成「壞了」。 |
| **推測（僅邏輯可能，無證據）** | 若實機上「最近庫存扣料」或「12 格內水源探測」正路徑失效，玩家會遇到完全無法維護/補水（目前 0 筆實機正路徑日誌）；搬運工自動送水可能根本不會發生（`HasPublicInput` 的實際語意未經實機驗證）。 |
| **明確不是 bug（設計如此）** | Basic 免材料維護；逾期 1 期 `G_maint=0.5`、2 期停擺；停擺只停農事、生長繼續（0.05）；休耕停農事；補水每趟 1/3 罐、2 動作、12 格限制；維護需要玩家按按鈕（需求 §19.11 明列按鈕）。**但設計與玩家期待的落差（F-MAINT-01）確實存在，且工作坊文案放大了它。** |

---

## 10. 附：一頁證據索引

| 事實 | 檔案:行號 |
|---|---|
| 維護只有面板命令（呼叫鏈） | `src/HDBF.UI/Panels/HdbfFarmPanelFragment.Build.cs:158-166` → `.Commands.cs:138-164` → `src/HDBF.Gameplay/Components/HdbfFarmCommandService.cs:76-117` |
| 帳單（檔位/離網） | `src/HDBF.Gameplay/Components/HdbfBoxFarmController.Commands.cs:213-234`；`src/HDBF.Core/Indoor/IndoorMaintenanceMath.cs:64-138` |
| 扣料（最近庫存、all-or-nothing） | `src/HDBF.Gameplay/Components/HdbfMaintenanceTransaction.cs:51-133` |
| `dueDay += cycle`、`cyclesMissed=0` | `src/HDBF.Gameplay/Components/HdbfBoxFarmController.Commands.cs:254-267` |
| 日 tick 只讀不寫維護（契約） | `src/HDBF.Core/Indoor/IndoorDayTick.cs:139-144,219-248`；`tests/HDBF.Tests/V2MaintenancePowerTests.cs:70-77` |
| 「maintenance stays an explicit player/command action」 | `src/HDBF.Gameplay/Components/IndoorMaintenanceMaterialSource.cs:1-6,21-23` |
| `IMaintenanceMaterialSource.TryConsume` 已存在但無人使用 | `src/HDBF.Core/Indoor/IMaintenanceMaterialSource.cs:13-27`；實作 `IndoorMaintenanceMaterialSource.cs:87-132`（生產程式碼零呼叫） |
| 停擺數學 | `src/HDBF.Core/Balance/BalanceFormulas.cs:150-179`；`IndoorConstants.cs:66-78`；`IndoorDayTick.cs:143-155,187`；`IndoorLaborMath.cs:171` |
| Basic 免材料維護 | `src/HDBF.Core/Balance/TierCatalog.cs:120-141`；UI `HdbfIndoorPanelFactory.cs:214-228`、`Cards.Maintenance.cs:24-36` |
| 水耗/覆蓋率/G_irr | `src/HDBF.Core/Indoor/IndoorWaterMath.cs:27-110`；`IndoorDayTick.cs:131-136,192-193` |
| 連續水耗 | `src/HDBF.Gameplay/Components/HdbfBoxFarmController.WaterFlow.cs:25-59` |
| 手動補水（瞬時、2 動作、1/3 罐） | `src/HDBF.Gameplay/Components/HdbfBoxFarmController.ManualRefill.cs:27-134`；`IndoorConstants.cs:83-90` |
| 12 格水源探測 | `src/HDBF.Gameplay/Components/HdbfManualWaterSource.cs:83-137`、`WalkStart.cs:35-61` |
| 搬運水入罐 | `HdbfWaterInventoryInitializer.cs:1-7,44-66`；`HdbfBoxFarmController.WaterSupply.cs:146-210`；`docs/v2/CHANGELOG.md:122` |
| 世界圖示單一優先（水>維護） | `src/HDBF.Core/Indoor/IndoorWarnings.cs:91-119`；`HdbfWorldIconService.cs:55-60,115-184` |
| 面板卡序（維護第 5 張） | `src/HDBF.UI/Panels/HdbfIndoorPanelFactory.cs:92-104`；可視高度 `HdbfFarmPanelFragment.Render.cs:205-254` |
| 1080p 需滾動的先例 | `docs/v2/CHANGELOG.md:90-92`（2.4.2） |
| 維護警告列（在卡內） | `src/HDBF.UI/Panels/HdbfIndoorPanelWarnings.cs:68-95` |
| tooltip 文案與行為不符 | `data/Localizations/enUS.csv`、`zhTW.csv`（`HDBF.Tooltip.Maintenance`、`HDBF.Tooltip.MaintenanceShutdown`） |
| 無通知 | 全 `src/` grep `Notification\|Toast\|Popup\|Alert` → 僅下拉選單 |
| 死 config 旗標 | `data/HDBF_Config.json`；`src/HDBF.Core/Config/HdbfConfigCodec.Sections.cs:19-33`；消費點僅 `Commands.cs:73`、`Context.cs:198` |
| 未實機驗證清單 | `README.md:131`；`docs/VERIFICATION.md:783,1223-1226`；`docs/v2/CHANGELOG.md:227-228` |
| 工作坊文案期待落差 | `docs/workshop/DESCRIPTION.md:56`；`docs/workshop/PAGE_COPY.txt:22` |
| 需求依據（手動按鈕、無維護動作） | `docs/REQ_V2_SECTION_19.md` §19.8（180-193）、§19.11（253-263）、§19.7 優先序（166） |
| 暫停＝全停 | `docs/v2/CHANGELOG.md:18-20`（2.5.3）；`src/HDBF.Gameplay/Components/HdbfBoxFarmController.Pause.cs` |
