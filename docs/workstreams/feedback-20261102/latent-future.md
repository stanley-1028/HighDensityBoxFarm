# HDBF 潛在 bug 盤點＋未來玩家回報預測（feedback-20261102）

> **狀態**：唯讀調查完成（2026-11-02）。本檔是四個已知回報調查之外的「下一波」清單。
> **對象版本**：HDBF 2.5.3（2026-10-29 發佈，Steam Workshop item 3828809784）／Timberborn 1.1.2.8／5 程序集／L1 613 測試／0 Harmony。
> **工作區**：`C:\Users\user\Documents\Timberborn\Mods\HighDensityBoxFarm`（本次只讀 `src/`、`data/`、`docs/`、`tests/` 與藍圖/本地化，未執行 build、未啟動遊戲、未修改任何其他檔案）。
> **唯一寫入檔**：本檔本身。

## 0. 方法、證據等級與邊界（誠實聲明）

| 標記 | 意思 | 本次數量 |
|---|---|---|
| **已證實（程式碼）** | 由 `src/` 的真實程式碼路徑即可確定行為，不需開遊戲；每條都附檔案:行 | 14 |
| **高度可能** | 程式碼路徑成立，且有日誌/文件/藍圖資料的間接支持；缺最後一次實機確認 | 4 |
| **推測** | 需要遊戲內或第三方模組行為才能確定（我沒有實機證據） | 2 |

邊界（不假裝）：

* 本次**沒有啟動 Timberborn**，所有「玩家會看到什麼」都是從程式碼＋既有實機日誌（`docs/VERIFICATION.md` §26–§35、`docs/in-game/*`）推導，不是本輪實測。
* 面板執行期行為（4 Hz 面板、點擊、世界圖標渲染）在 `docs/VERIFICATION.md` §26.3/§28.2 已被列為「本機合成輸入不可用」而無法驗證；本檔凡涉及「畫面上會怎樣」都降一級標記。
* 未重複另外 4 個已知回報（維護需手動、數分鐘後致命崩潰、暫停崩潰、已接管道仍 No Connection、Ember-pelts 陣營）。凡相關處只寫一句交叉引用。
* Timber Pipes 相關條目（F-LATENT-02）是 Lead 指定單獨列出的方向，與 pipes 調查共用結論但不重複其細節。

### 已知 4 項的交叉引用（不重複內容）

* 維護/保養要手動 → 見 **F-LATENT-08**（維護可無限提前）與 **F-LATENT-07**（缺料判定不一致）的「相關但不相同」段落。
* 數分鐘後致命崩潰 → 見 **F-LATENT-14**（拆除不撤離室內工人，與已修的暫停崩潰同族）。
* 暫停崩潰 → 2.5.3 已修（`HdbfBoxFarmController.Pause*.cs`）；本檔只在需要時引用其根因。
* 已接管道仍 No Connection → 屬 pipes 調查；本檔 **F-LATENT-02(a)** 是同一根因的平衡面（隔空抽水），不重述其診斷。
* Ember-pelts 自訂陣營 → 屬另一調查；本檔不涉及陣營解析。

---

## 1. 條目清單（F-LATENT-01 … 19）

### F-LATENT-01（P0）休耕結束後的第一個日 tick，整批成熟作物以「0 產量」被清空

**現象（玩家語氣）**
> 「我把農場切到休耕讓土休息，過幾天切回常規——可收穫 60 瞬間變 0，倉庫一件作物都沒有。作物去哪了？」

**原因（已證實：程式碼路徑可完全確定）**
1. 休耕模式**完全禁止農事**（含收穫）：`IndoorDayTick.cs:187` `actionsAllowed = !entranceBlocked && !stalled && record.Mode != IndoorMode.Fallow`。因此在休耕期間成熟作物只會一直留在槽裡。
2. 切回常規時，`_modeTransitionFrom` 記下 `Fallow`（`HdbfBoxFarmController.Commands.cs:90`），下一個日 tick 的 `ModeTransitionPrevious` 回傳 `Fallow`（`HdbfBoxFarmController.Tick.cs:144-154`），於是 `transitionScale = IndoorGrowthMath.ModeTransitionYieldScale(Normal, Fallow)`；`Fallow` 的 `ModeYieldMultiplier` 是 **0**（`IndoorGrowthMath.cs:25-39`），所以 scale 被算成 **0**（`IndoorGrowthMath.cs:114-130`，這裡沒有「lower==0 就跳過」的保護）。
3. `HarvestItems(..., yieldScale=0)` 回傳 **0**（`IndoorGrowthMath.cs:94-104`），而 `TryHarvest` 只在 `amount > 0` 時才寫入庫存；**amount == 0 時仍會清空槽位、記疲勞、回報「收穫成功」**（`IndoorLaborMath.Scheduling.cs:27-61`）。日 tick 因此統計 `harvested=N` 且 `changed=true`。

**影響**：休耕→常規（或 →密植）的第一天，所有成熟槽位被「無聲收成」成 0 件，隔天自動補種。玩家的整盒作物直接人間蒸發；日誌看起來是正常收穫（`harvested=60`），不會有 Error。這是**把「休耕」這個設計誘餌與「收成歸零」綁在一起**的最壞組合，而面板只提示「模式切換中，今日產出降低」（`HDBF.Panel.ModeTransition`），沒有說會歸零。

**解法**
* *短期（一行級）*：`TryHarvest` 在 `amount <= 0` 時**不得清空槽位**（視為 blocked，回報 backlog 或直接跳過）；`ModeTransitionYieldScale` 在 `lower <= 0` 時回傳 1（休耕本來就不產出，不需要再跌一次）。
* *長期*：把「休耕」定義補進需求（是否允許收穫成熟作物？），並在面板/工具提示寫明「休耕期間不收穫」；切模式時若場上有成熟作物，給一次明確確認或警告列。

**信心度**：**已證實（程式碼）**。唯一未做的是遊戲內重現（不需要新功能，只要一份「休耕中且整盒成熟」的存檔＋2 天時間推進）。

**驗證方式**
* 離線（最快、足以釘死）：L1 新測 `TestFallowExitTransitionDayDoesNotDestroyMatureCrops`——`IndoorDayTick.Run`，30 個成熟槽、`Mode=Normal`、`PreviousMode=Fallow`、扣掉後斷言「輸出庫存收到 ≥ 期望件數」；現行程式碼會得到 0 件且槽位變空 → 測試在修好前應該 FAIL。
* 實機：存檔副本改寫 payload（`CraftIndoorFixture --planted 60 --growth 10`）→ 面板切休耕 1 天 → 切回常規 → 看 `indoor.day: harvested=` 與倉庫/頂部資源列數量。

**風險**：P0（資料/產出損失、必踩、無錯誤訊息）。

---

### F-LATENT-02（P0/P1）Timber Pipes「已連接」是全域假訊號：隔空抽水、接了也不算連、模組改版全場斷水

> Lead 指定獨立條目；與 pipes 調查（No Connection）共用根因，但本條處理**另外兩個方向**。

**現象（玩家語氣，三個方向）**
> (a)「我農場旁邊根本沒有管線，面板卻顯示管道接口已連接，而且水一直自動補——這不科學吧？」
> (b)「我明明把管子接到農場旁邊了，它還是說沒連接。」（細節見 pipes 調查，交叉引用）
> (c)「Timber Pipes 更新之後，我所有盒子農場同一天全部沒水，只能一座一座按補水。」

**原因（已證實：程式碼路徑可完全確定）**
* 管道水源列舉的是**整個 settlement 的 PipeRegistry 水箱清單**，只過濾 `IsFinished && HasPipeGraph && TakesWaterGood`（`TimberPipesWaterSource.cs:241-259`）＋流體是清水（`:250-255`）。**沒有任何一步用到「這座農場」的座標、面向或 pipe port 連通性**（類別只有 `_binding`/`_health`/`_reportFailure` 三個欄位，`:39-41`）。
* 面板/快照的「已連接」＝ `IndoorAdapterHost.IsPipeConnected` ＝ `pipes.HasNetwork`（`IndoorAdapterHost.cs:105-120`）＝「settlement 裡至少有一個接了網的水箱」，與該農場無關。所以：(a) 只要地圖任何角落有「接在管網上的水箱」，每一座盒子農場都會被判定已連通並開始抽水（平衡漏洞）；(b) 農場本身不是 PipeTank，管線模組的連通判定不會把它算進去（No Connection 的來源，pipes 調查）。
* 模組更新風險（兩條路徑都通到「全場同日斷水」）：
  * **完全失效**：`TimberPipesWaterBinding.TryBind` 對每個反射錨點都要求簽名完全相符（`TimberPipesWaterBinding.cs:150-170`），錨點型別/成員缺失 → `HasNetwork` 回 false → **每一座農場在同一天全部失去管道供水**、退回人工補水；而 `PipePort` 仍維持 true（見下一段），面板於是同時顯示「已連接」與「來源：無」。
  * **部分失效**（列舉/讀值仍在，但 `ApplyVolume` 或 `get_VolumeM3` 被改名）：`HasNetwork` 仍是 true → 日 tick 會真的去抽水 → `TryBind` 失敗 → `FailDraw` → `ReportRuntimeFailure` 上交 `CompatStateMachine`（`IndoorAdapterHost.cs:139-149`）→ **3 次失敗即本 session Failed 且不重試**（`CompatStateMachine.cs:26,132-153`，失敗次數沒有時間窗）。此時每個日 tick 每座農場各貢獻一次失敗，等於**頭幾座農場的第一個日 tick 就能把整個 adapter 打成 Failed**，之後所有農場同時斷水。

**影響**：(a) 水資源平衡被繞過（不需要接管的農場也有免費水），也讓「水管」這個代價失去意義；(c) 一次外部模組更新就能讓**所有**農場同一天斷水，且玩家只看到「管道接口已連接／補水來源：無」的矛盾面板，不會知道是第三方模組改版。

**解法**
* *短期（讓它誠實）*：`IsPipeConnected` 不再回傳全域 `HasNetwork`，至少改成「本農場 pipe port 座標上有 PipeGraph 連通」——用 `PipeRegistry.TryGetGraph`（或對 pipe port 座標上的 `BuildingPipe.Graph` 判空）在農場座標驗證；拿不到就誠實顯示「未連接」並只依賴人工補水。這同時把 (a) 的隔空抽水關掉。
* *長期*：把農場註冊成 pipe endpoint（真正的 `PipeTank` 消費端或模組提供的 consumer 介面），讓管線模組自己的連通/流量計算負責這條鏈路；並把「adapter Failed」與「這座農場斷水」解耦（單農場 fallback，不因全域狀態連坐）。另把 3-strike 改成有時間窗或每日重置，避免一次模組改版造成永久失效。
* 交叉引用：與 pipes 調查的 (b) 同一根因，本條不重述其 No Connection 診斷。

**信心度**：(a)(c) **已證實（程式碼）**；(b) 已在 other investigator 手上（此處只交叉引用）。玩家「不用接管也能補水」的實際感受屬**高度可能**（需要一次實機 A/B：地圖任意處放一個接網水箱 + 一座無接管農場，看 tank 有沒有上升）。

**驗證方式**
* 離線：以 `TimberPipesWaterSource` 的 reflection binding 樁（或既有 `InstallAdapter`/`TestHooks`），斷言 `IsPipeConnected` 在不含本農場座標的圖上不得為 true；新增 source-contract 測試（`V2GameplayContractTests` 風格）釘住「adapter Failed 不得使某座農場仍顯示 PipePort=true」。
* 實機：乾淨地圖 + 只放 Timber Pipes + 一座未接管的基礎農場，觀察 `water.settle` 的 tank 是否下降得比 demand 慢（現況會上升）；再更新/改錨點模擬 signature 變更，看三座農場是否同日全部 `pipe-unavailable`。

**風險**：P0（平衡）／P1（整場同時斷水、矛盾顯示）。

---

### F-LATENT-03（P0）水罐 3–4 天見底後農場「靜默停工」：不補種、生長歸零，而玩家不知道水要從哪裡來

**現象（玩家語氣）**
> 「農場前三天好好的，第四天開始完全不動：槽位全空、生長進度卡住，面板只說缺水。我有水塔啊，為什麼它不去拿？」

**原因（已證實：數值＋程式碼）**
* 水罐容量＝`WaterPerPlotPerDay × PlotCapacity × TankCapacityDays`（`TierCatalog.cs`）：基礎 0.02×60×3 = **3.6**（3 天）、中級 10.8（3 天）、高級 33.6（4 天）、頂級 76.8（4 天）。每日需求就是 `TankCapacityDays` 的倒數；連續排水（`HdbfWaterFlowService` → `ConsumeElapsedWater`）讓罐子真的每天掉一格。
* 缺水時：`Coverage=0` → `G_irr=0` → `Advance` 直接 return（生長凍結）；日 tick 的**補種**被 `waterAvailable: record.Tank > 0f` 擋掉（`IndoorDayTick.cs:192-193`），所以槽位收完就一路空著。收穫仍可進行（不受水限制）。
* 兩條有源補水路徑都有限制：管道要 Timber Pipes（軟依賴），人工補水要「12 格內、可步行到達、未污染」的真實水源（`HdbfManualWaterSource.Search.cs:39-102`）；第三條是「工人/搬運工把水貨物送進水輸入庫存」（`HdbfGameConfigurator.cs:115-121`），但那要求聚落裡已經有 `Water` 貨物（抽水站/水塔）。
* 實機日誌佐證：`round42-v2` 的 `water.settle tank=3.600→2.400→1.200` 每天正好 −1.2，且該輪沒有任何補水來源。

**影響**：這是最容易被誤判成「模組壞了」的設計代價：早期玩家（還沒抽水站、沒有管道模組、附近沒湖）蓋了 40 木頭＋200 科學點的農場，3 天後變成裝飾品；面板雖有缺水/世界圖標，但玩家不知道「下一步該蓋什麼」。可預期留言：「farm stops working after 3 days」「needs water but I have a water tank」。

**解法**
* *短期*：面板缺水列直接寫明三條補水路徑（「12 格內水源／管道模組／把水貨物送進來」）並顯示「距離最近水源：無」；藍圖描述或 tooltip 標明「滿罐僅夠 3 天」。
* *長期*：把「水輸入庫存」真的接上原版補水鏈（現在靠 `FillInputWorkplaceBehavior` 自己搬，玩家常不知道它存在），或給一個「低水位自動請求搬運」的行為；平衡上考慮把基礎檔水罐拉到 4–5 天（目前 3 天剛好卡在「玩家還在蓋抽水站」的節奏上）。

**信心度**：**已證實（數值與程式碼）**；「玩家因此罵」屬高度可能。

**驗證方式**
* 離線：L1 純 Core 模擬 60 槽、無水源、跑 6 個 game day → 斷言 day 3 後 tank=0、day 4 起 `Planted=0`、`GrowthDays` 不變；另加面板契約測試（coverage=0 → 缺水列存在）。
* 實機：乾淨地圖只放一座基礎農場（不接水、附近無水），跑 4–5 天，看 `indoor.day: planted=0` 與畫面。

**風險**：P0（玩家留負評/退訂的最大來源；且屬「設計代價」而非程式錯誤，處理成本低但影響大）。

---

### F-LATENT-04（P1）手動補水在「水罐快滿」時仍抽滿 1/3 罐，水被倒掉、2 個動作照扣

**現象（玩家語氣）**
> 「我按補水，畫面顯示 +0.4，但我看湖邊水位少了一截；而且今天動作少 2 次。罐子才差一點點而已耶。」

**原因（已證實：程式碼）**
* `TryRequestManualRefill` 只在 `tank >= capacity` 時整筆跳過（`HdbfBoxFarmController.ManualRefill.cs:92-101`）；只要差一滴，就對水源 `source.TryDraw(RefillAmount(capacity))` 抽滿 **1/3 罐**（`:103-110`），然後才 `IndoorWaterMath.TryRefill` 依剩餘空間截斷（`:112`）——**抽出來的多餘水量直接消失**。
* 同一次操作仍然扣 `ManualRefillActions = 2` 個當日農事動作（`ManualRefill.cs:113`、`IndoorConstants.cs:84`）。

**影響**：水資源與動作的雙重浪費；玩家會覺得「補水按鈕在吃我的水」。以基礎檔為例，差 0.1 單位時仍抽 1.2 單位、丟掉 1.1 單位＝一整天的水。

**解法**
* *短期*：先算 `headroom = capacity − tank`，只抽 `min(refillAmount, headroom)`（`IWaterSource.TryDraw` 本來就接受任意量）；`headroom < 1 個單位`時直接視為「已滿」不扣動作。
* *長期*：把「一趟 = 1/3 罐」改成「一趟 = 補到某比例或固定量」的明確語意，並在面板顯示本次會補多少、扣多少動作。

**信心度**：**已證實（程式碼）**。

**驗證方式**：離線把「抽水量計算」抽成 Core 純函式（`RefillAmountFor(tank, capacity)`）＋L1 斷言 `drawn == headroom`；或沿用 `docs/VERIFICATION.md` §4 的 net9 harness 直接驅動 controller 的 public 命令。

**風險**：P1（可重現、代價明確，但需要玩家注意到）。

---

### F-LATENT-05（P1）「12 格 + 可達性」的硬限制與「工人自己搬水」雙軌並存 → 「我明明有水源/水塔卻不能補水」

**現象（玩家語氣）**
> 「抽水站就在農場隔壁 15 格，面板卻說『12 格內沒有可用水源』；可是我看農夫自己一直進出搬水——到底哪個說法才對？」

**原因（已證實：程式碼，觸發頻率屬高度可能）**
* 人工補水的探測是硬編碼 `ManualRefillRadiusCells = 12`（`IndoorConstants.cs:90`）＋`IsReachable`（導航網格可達，`HdbfManualWaterSource.Search.cs:83-88`、`HdbfManualWaterSource.cs:139-150`）；只接受真實水體或「該區倉庫裡整筆足夠的水貨物」（`HdbfManualWaterSource.Search.cs:161-227`）。
* 同時，農場本身有原版 `FillInputWorkplaceBehavior`＋`FillInputHaulBehaviorProvider`（`HdbfGameConfigurator.cs:119-121`），所以**農場工人會自己去搬水**（也吃 `Water` 庫存）。兩套機制互不知道對方的存在：搬水成功但人工補水按鈕仍可能顯示「沒有水源」；反之亦然。
* 供應狀態只在日 tick／面板節流探測時更新（`RefreshWaterSupply`，`ManualRefill.cs:141-172`），玩家剛蓋好抽水站時可能先看到錯的舊狀態（面板 4 s 一次探測，`SupplyProbeSnapshotInterval = 16`）。

**影響**：玩家無法從面板理解「水是怎麼進來的」，也無法預期 12 格規則；「12」這個數字沒有任何 UI 說明。

**解法**
* *短期*：缺水時把最近水源距離/失敗原因（不可達/污染/只有 10 格外的水塔）寫進面板；工具提示說明「工人也會自行搬水」。
* *長期*：統一成單一補水模型（例如把人工補水改成「請求一次搬水任務」），或至少讓 12 格與倉庫搜尋條件由 config 控制並顯示。

**信心度**：**已證實（程式碼）**；「因此產生留言」屬高度可能。

**驗證方式**：實機用既有 fixture（`Repro3` 存檔副本）把水塔放到 13/15 格處，對照面板「補水來源」與 `water.manual-refill` 日誌；L1 無法覆蓋（需要水體/導航 API）。

**風險**：P1。

---

### F-LATENT-06（P1）管道接口的兩個矛盾：面板「已連接」與「來源：無」同時成立；玩家關掉它隔天又自己打開

**現象（玩家語氣）**
> 「面板寫『管道接口：已連接』，但同一頁寫『補水來源：無』。我把管道接口關掉想改成手動，隔天它又自己變回已連接。」

**原因（已證實：程式碼）**
* `UpdatePorts`（`HdbfBoxFarmController.Context.cs:245-297`）每個日 tick **無條件重算並覆寫** `record.PipePort`：`adapterHidden` 只包含 `Disabled`/`Unknown`，`Failed` 時仍會把 `PipePort` 設成 true（`:256-257`）；但同一時間 `ReadPipeWater` 在 Failed 時回傳 null、`hasPipeWater=false`（`:77-104`）→ 快照就出現「管道接口已連接、補水來源：無」。
* 玩家用 `TrySetPortConnected` 關掉管道（`Commands.cs:115-130`）只寫進 record；下一個日 tick 立刻被 `UpdatePorts` 蓋回去（並記一條 `indoor.port: … pipe=True`）。換句話說，玩家關不掉管道，只是讓自己在「今天剩下的時間」用到一個不存在的開關。

**影響**：玩家無法關閉管道（例如想省水、或想確認是不是管道才讓農場不長），面板狀態還會自相矛盾；日誌同時誤導維修者。

**解法**
* *短期*：`UpdatePorts` 只更新「能力」（有沒有這個 port），不要覆寫玩家的啟用/停用選擇；把玩家選擇另存一個欄位（或沿用 `PipePort` 但不被日 tick 改寫）。`Failed` 時 `PipePort` 應與 `IsPipeConnected` 一致（false 或顯示「故障」而非「已連接」）。
* *長期*：把「port 存在」與「port 已啟用」與「port 實際連線」三態在 DTO/面板分開呈現（現在只有兩態）。

**信心度**：**已證實（程式碼）**。

**驗證方式**：source-contract 測試（`V2GameplayContractTests` 現有風格）釘住「日 tick 不得寫入 `PipePort`」；實機則按一次管道接口、等一個日 tick、再截圖（玩家已回報的 No Connection 是另一條路徑，不衝突）。

**風險**：P1。

---

### F-LATENT-07（P1）缺料判定與實際交易不一致：面板說「材料夠」，按維護才失敗（或反之）

**現象（玩家語氣）**
> 「面板沒有顯示缺料，我按『執行維護』卻說材料不足；我明明有 8 個木板，只是分散在兩個倉庫。」

**原因（已證實：程式碼）**
* 面板/每日警告用的 `IndoorMaintenanceMaterialSource.Available` 把**所有已完成區中心**的庫存加總（`IndoorMaintenanceMaterialSource.cs:39-84`、`:112-131`）。
* 實際交易 `HdbfMaintenanceTransaction.TryConsumeNearest` 要求**單一倉庫** `HasUnreservedStock(整筆)`（`HdbfMaintenanceTransaction.cs:82-97`、`:109-116`），加總夠但分散就失敗 → 失敗鍵 `HDBF.Error.MaterialsMissing`。
* 反之，`Available` 只在日 tick 更新旗標（`IndoorDayTick.cs:244-247`），下一個 tick 前玩家可能已被判「缺料」（面板顯示缺料但其實剛好補貨了）。面板的 `MaintenanceBlockedReason` 也直接看這個舊旗標（`HdbfIndoorPanelFactory.cs:222-225`）。

**影響**：玩家看面板與實際命令不一致，會認為「模組亂扣材料/亂顯示」；高密度殖民地（多倉庫、多區）幾乎必踩。

**解法**
* *短期*：把 `Available` 改成「可支付」語意（找得到單一庫存能付整筆才算有料），或交易改成跨庫存拼湊（原版倉庫系統的 normal transfer 更合適）。
* *長期*：維護材料走原版的 material 需求/搬運流程（像其他建築的 upkeep），而不是「按一下直接從任意倉庫扣」。

**信心度**：**已證實（程式碼）**。

**驗證方式**：離線把「可支付判定」抽成 Core 純函式並加 L1（總量夠但分散 → false）；實機用兩個倉庫各放 4 木板、中級農場按維護，比對面板與 `ui.command-failed` / `indoor.maintenance-missing`。

**風險**：P1。

---

### F-LATENT-08（P2）維護可以無限提前按、沒有到期檢查或二次確認：連點兩下 = 多付一份材料

**現象（玩家語氣）**
> 「我手滑點了兩下『執行維護』，8 個木板就沒了，到期日直接往後跳 20 天。」

**原因（已證實：程式碼）**
* `TryPerformMaintenance` 的流程是「計畫（只要求這個檔位有材料帳單）→ 扣料 → `dueDay += cycleDays`」（`HdbfFarmCommandService.cs:85-109`、`HdbfBoxFarmController.Commands.cs:213-273`），**不檢查是否已逾期、不看冷卻、不做確認**；連續呼叫就是連續付料並把到期日往後推。
* 與已知回報「維護需手動」的關係：那條是「要人按」，本條是「按了沒有防呆」。修法可以一起做，但不要把兩條當同一件事。

**影響**：材料被誤吃（8 木板 / 6 金屬 / 15 金屬）；玩家不會理解自己剛剛預付了 10 天的保養。

**解法**：`dueDay` 還在未來時禁用按鈕（或改成「提前保養」需要確認對話框）；連點去重（同一 frame/同一日只允許一次）；面板顯示「本次扣 8 木板、下次到期 day X」。

**信心度**：**已證實（程式碼）**。

**驗證方式**：source-contract／L1 契約測試（`TryApplyMaintenance` 已有可測簽名，可在 net9 harness 驅動 controller）；實機連點截圖 `ui.command-failed` 與材料變化。

**風險**：P2。

---

### F-LATENT-09（P2）「物流接口／輸出端口」只是能力標籤，沒有任何實際輸出行為，面板卻說「已連接」

**現象（玩家語氣）**
> 「我裝了傳送帶模組，面板說輸出端口『已連接』，可是我把輸送帶接到農場旁邊，它完全不會把作物拉走。」

**原因（已證實：程式碼）**
* `LogisticsConnected()` 直接回傳 `_adapterHost.IsLogisticsConnected`（`HdbfBoxFarmController.IndoorSnapshot.Helpers.cs:14-24`），後者是 `CompatAdapterRegistry.IsPortAvailable(Logistics)`（`IndoorAdapterHost.cs:123-137`）＝**只是偵測到 Conveyor Belts v2**；`record.LogisticsPort` 也由同一個健康狀態決定（`Context.cs:258`）。
* 專案裡沒有任何把產物寫進輸送帶/物流端的程式碼：產物只進 `SimpleOutputInventory`，靠原版搬運工（`IndoorInventorySink.cs`、`HdbfGameConfigurator.cs:123-128`）；`HDBF_Config.json` 的 `features.logisticsOutput` 也是 `false`。
* 附帶一個顯示語意問題：`record.LogisticsPort` 同時代表「能力」與「已啟用」，而 `PortRow` 用 `record.LogisticsPort` 當作「此檔位有沒有這個 port」（`HdbfIndoorPanelFactory.Cards.Output.cs:109-114,133-148`）。所以中/高/頂級農場在**沒裝 Conveyor Belts 時**，物流接口會顯示「不適用」而不是「未連接」——玩家會以為這個檔位根本沒有物流端口。

**影響**：玩家會照著面板去接輸送帶，然後回報「端口是假的」。這比「沒有這個功能」更糟：面板給出了錯誤的承諾。

**解法**：*短期*：把「輸出端口／物流接口」改名成「物流模組（僅顯示）」或直接顯示「不適用」直到真的實作；port 行只反映「模組存在」而不說「已連接」。*長期*：真的接 Conveyor Belts 的 output port（或在工坊頁明說只支援原版搬運）。

**信心度**：**已證實（程式碼）**；「玩家會誤解」屬高度可能。

**驗證方式**：L1 面板契約測試（給定 adapter 健康 → port 列文字），並在 `docs/VERIFICATION.md` 標註「物流端口僅能力探測」。實機：裝 Conveyor Belts、接帶、看是否搬。

**風險**：P2。

---

### F-LATENT-10（P2）0 工人時面板顯示「水量充足、覆蓋率 100%、水罐不消耗」，也沒有世界圖標

**現象（玩家語氣）**
> 「農場沒工人（我以為在休假），面板水跟效率都是綠的，地圖上也沒警告，我一直以為它在運作。」

**原因（已證實：程式碼）**
* 需求與耗水都以「有效槽位」計算，而 `EffectiveSlots(tier, 0) = 0`（`IndoorLaborMath.cs:30-37`）→ `DemandPerDay = 0` → `Coverage(_, 0) = 1`（`IndoorWaterMath.cs:51-70`）→ 面板顯示 100%、水罐「不消耗」（`IndoorSnapshot.cs:40-41`、`DaysRemaining` 回 ∞ 顯示「—」）。
* 世界圖標完全沒有「無工人」這一類：`IndoorWarningMap.IconKindOf` 只有 4 類（`IndoorWarnings.cs:91-119`），`SeverityOf` 把 `NoWorkers` 當 Low（`:142-144`）；但面板的 `BuildWorkerWarnings` 又把它當 Critical（`HdbfIndoorPanelWarnings.cs:123-127`）——**兩套嚴重度模型不一致**。

**影響**：無人農場看起來最健康；玩家不會發現自己忘了派工。也是「為什麼我的農場不長」的常見誤判來源。

**解法**：demand 用「槽位容量」而非有效槽位計算水耗（或 0 工人時覆蓋率顯示「—」）；`NoWorkers` 至少升到 P1 並給世界圖標（或沿用原版「缺工」圖標）。

**信心度**：**已證實（程式碼）**。

**驗證方式**：L1 契約測試（`EffectiveSlots=0 → Coverage 應顯示 — 或警告`、`IconKindOf(NoWorkers)` 決策）；實機把工人數改成 0 看面板與地圖。

**風險**：P2。

---

### F-LATENT-11（P1）世界警告圖標落後一整天：面板即時顯示缺水，地圖上卻沒有圖標；補完水圖標還掛著

**現象（玩家語氣）**
> 「面板說缺水，地圖上農場頭上什麼都沒有；我補完水了，圖標又掛在那裡一整天。」

**原因（已證實：程式碼）**
* 圖標用 `snapshot.Warnings`（`HdbfWorldIconService.Evaluate.cs:28-31`），也就是控制器快取的 `_lastWarnings`，**只在每日 tick 寫入**（`HdbfBoxFarmController.Tick.cs:156-174`）；圖標服務每秒跑一次但讀的是同一份舊旗標。
* 面板的水警告則是**即時**計算：`BuildWaterWarnings` 用即時 coverage（`HdbfIndoorPanelWarnings.cs:34-59`），而 coverage 由水罐即時值/需求算出（`IndoorSnapshot.cs:36-62`）。水罐是逐幀排水的，所以兩者必然不同步。
* 維護/缺料圖標同理（日 tick 才更新），玩家按完維護後圖標會留到隔天。

**影響**：世界圖標是這個模組「讓玩家一眼看到哪座農場有事」的主要設計（§19.11），延遲一天等於讓它失去意義；也會出現「面板綠、地圖紅」的矛盾。

**解法**：*短期*：圖標服務改用即時快照推導的四類狀態（coverage/C_min、逾期/停擺、斷電、入口），或至少把 `_lastWarnings` 在命令成功後（維護/補水）立即重算。*長期*：把「警告評估」抽成 Core 純函式，面板與圖標共用同一份即時結果（現在是面板即時、圖標快取，正好相反）。

**信心度**：**已證實（程式碼）**。

**驗證方式**：L1 新增「同一份 snapshot 給面板與圖標 → 分類一致」契約測試；實機：把水罐改成 0 後立刻截圖（不等日 tick），再補水後立刻截圖。

**風險**：P1。

---

### F-LATENT-12（P1/P2）固定作物下拉、「固定作物無效」警告、作物名稱都吃「內建 fallback 表」→ 作物模組/新版本作物：選不到、誤報無效、顯示英文 id

**現象（玩家語氣）**
> 「我裝了作物模組，面板的固定作物只列得出原本那幾種；我種下去的新作物在面板顯示英文 id（還是根本被說『固定作物無效』）。」

**原因（已證實：程式碼）**
* 下拉選單 `BuildCropChoices` 用 `FallbackCropCatalog.Default.ForFaction`（`HdbfIndoorPanelWarnings.cs:177-206`），不是真正的 `GameCropCatalog`（後者會列舉遊戲 `PlantableSpec.ResourceGroup == "Farmhouse"`，`GameCropCatalog.cs:178-237`）。
* 「固定作物無效」警告也用 fallback 判斷（`HdbfIndoorPanelWarnings.cs:159-171`），而設定命令卻用真實 catalog 驗證（`Commands.cs:176-184`）→ 真實 catalog 有、fallback 沒有的作物會被面板誤報無效。
* 名稱解析 `CropNameKey` 只硬編碼 10 個作物 id（`HdbfIndoorPanelFormat.cs:160-183`），表外 id **直接顯示原始英文 id**（`CropName`：`key is null ? cropId`）。
* 輪作序列用的是真實 catalog（`IndoorSnapshot.cs:219-255`），與下拉的 fallback 來源不同 → 兩處顯示可能不一致。

**影響**：與作物 mod 或未來遊戲版本共存時，玩家會遇到「選不到作物」「被說無效」「英文殘留」三種回報；這是工坊頁面「相容」承諾最容易被打破的地方。

**解法**：面板的作物清單/驗證一律改吃 `GameCropCatalog`（把 `ICropCatalog` 注入 UI 讀模型路徑，或讓 snapshot 直接帶 `CropChoices`）；表外作物名顯示本地化名（遊戲本身有 `PlantableSpec` 的顯示名）而不是 raw id。

**信心度**：**已證實（程式碼）**；是否真的踩到取決於外部作物表（推測部分）。

**驗證方式**：L1 面板契約測試（注入一個含額外作物的 `ICropCatalog`，斷言下拉含它、且無「無效」警告）。

**風險**：P1（與 mod 生態共存）／P2（純原版玩家）。

---

### F-LATENT-13（P2）本地化/文案與實際行為脫節（3 處）

| 位置 | 現況文字 | 問題 |
|---|---|---|
| `HDBF.Error.RefillUnavailable` | 「本版本尚未開放人工補水。」（zhCN `/enUS: not yet available`） | 人工補水**已經實作**（`HdbfBoxFarmController.ManualRefill.cs`）；任何走進 `RefillUnavailable` 的例外路徑（`:105`、`:128`、`HdbfFarmCommandService.cs:145/152`）都會給玩家一句錯誤的「本版本尚未開放」。 |
| `HDBF.Warning.AdapterFailed` / `HDBF.Warning.Shutdown` | 兩者中文都抄成「入口堵塞」/「維護逾期」 | 兩個 key 與 `HDBF.Warning.EntranceBlocked` / `MaintenanceOverdue` 文字重複；若未來啟用（世界圖標已預留 slot，`HdbfUiLocKeys.cs:211-212`）會顯示錯誤語意。`HdbfWorldIconService` 的 tooltip 在**註冊時只本地化一次**（`HdbfWorldIconService.Update.cs:173-186`），切換語言不會更新。 |
| `HDBF.Panel.WaterShortage` | 「缺水：生長效率 {0}%（**減產** {1}%）」 | 懲罰是**生長變慢**（G_irr），不是每次收成減量；「減產」會讓玩家以為收成量下降（平衡誤解）。 |

**信心度**：**已證實（文字內容可讀）**。**解法**：短線改字（RefillUnavailable 改成「補水來源不可用」之類）；長線把圖標 tooltip 改成延遲本地化/每次 Evaluate 重建。**驗證**：`tools/validate` 的三語鍵集已能抓缺鍵，但抓不到「語意錯誤」——需要人工字串審查或 L1 字串快照測試。**風險**：P3（不影響功能，但影響評價與誤解）。

---

### F-LATENT-14（P1）「有修復就整筆拒寫」的存檔契約：某些狀態下會靜默不存檔、進度回捲（推測需存檔比對）

**現象（玩家語氣）**
> 「存檔再讀檔，農場的作物/水罐回到好幾天前；日誌只有一行 validation failed。」

**原因（程式碼契約成立，觸發條件待證）**
* `IndoorEntitySaveBridge.Save` 的規則是「驗證有任何修復（`validation.Repaired`）就整筆拒絕寫入」（`IndoorEntitySaveBridge.cs:76-81`），而驗證器會**主動修掉**它不認可的狀態：未知作物 id／成長時間非正的 mod 作物 → 清空槽位（`IndoorSlotRepair.cs:144-233`）、`FixedCropId` 不在表內 → 清空（`IndoorSaveFieldRepair.cs:135-160`）、`maintenance.dueDay = 0` 的材料檔 → 補一個週期（`:103-129`）、`LogisticsPort` 出現在 Intermediate 以下 → 強制 false（`IndoorSaveValidator.cs:157-161`）。
* 只要這個「被修掉的狀態」在遊戲內反覆出現（例如某個 mod 作物的 `GrowthTimeInDays` 為 0，日 tick 會把它立即成熟再收穫，而玩家又被固定作物策略持續種回去），存檔就**每次都拒寫**，玩家只會看到進度回捲，不會看到錯誤對話框。

**影響**：若觸發＝災難級（玩家數小時進度消失，且難以自行診斷）。目前無法排除，也無法確定（需要真實存檔 + Player.log 比對）。

**解法**：*短期*：把「拒寫」改成「寫入修復後的副本，並把修復清單寫進日誌/相容性報告」（保留上一份良好快照的原意仍可成立：可以先寫 `<key>.prev`）。*長期*：把「玩家狀態」與「暫存狀態」分開驗證，暫存狀態壞掉不應阻止整筆存檔。

**信心度**：**推測**（程式碼契約確定；實際觸發需要特定 mod 作物或舊存檔狀態）。

**驗證方式**：沿用 `docs/VERIFICATION.md` §4 的 net9 harness（真實 `EntitySaver`/`EntityLoader`）：造一個「槽位含未知作物」的 record → `IndoorEntitySaveBridge.Save` → 斷言目前回傳 false（現行行為）→ 再量「玩家進度是否回捲」；並在實機用一個含未知作物 id 的存檔副本存/讀一輪。

**風險**：P1（條件式 P0）。

---

### F-LATENT-15（P2）8 份藍圖 `BackwardCompatibleTemplateNames` 全空＋佔格曾變更：任何 blueprint 改名/佔格調整都會讓舊存檔農場被遊戲直接刪除

**現象（玩家語氣）**
> 「更新模組後我的農場通通不見了，讀檔只跳一個『對象位置無效，已被刪除：高密度盒子農場』。」

**原因（已證實：資料）**
* 8 份藍圖的 `TemplateSpec.BackwardCompatibleTemplateNames` 都是 `[]`（例：`data/Buildings/Food/BoxFarm/BoxFarm.Basic.Folktails.blueprint.json`）。這個欄位是原版「改名後仍能讀舊存檔」的唯一機制。
* 歷史上這類事故已經發生過一次同型事件：`docs/VERIFICATION.md` §25 的 art04 A/B 證明「新佔格（3×3×1，`Size {X:3,Y:3,Z:1}`）與舊物件衝突時，遊戲會印 `It's not backward compatible. Deleting it.` 並刪除農場」。佔格或模板名任一變更，舊存檔就會踩到同一條路。
* 另外 `BlockObjectSpec.Size` 從 `{3,1,2}` 改為 `{3,2,1}` 再到 `{3,3,1}`（§25、§26），只要玩家跳過某個版本更新，就會遇到佔格不符。

**影響**：一次性、不可逆的存檔資產損失（而且玩家已被告知「位置無效」但不會理解是被改版刪掉）。目前沒發生只是因為 v2 發佈以來 template 名與佔格沒再變。

**解法**：*短期*：把 8 份藍圖的 `BackwardCompatibleTemplateNames` 補上所有歷史模板名（`HDBF.BoxFarm.*`、`BoxFarm.*` 等），並在 `tools/validate` 加硬性檢查「本次版本的 template 名變化必須反映在 backward 清單」。*長期*：發佈流程把「template 名/佔格變更」列為需要遷移卡的紅線（像 schema 變更一樣對待）。

**信心度**：**已證實（資料現況）**；「未來某次改版會踩」屬推測。

**驗證方式**：L1/validator 層即可（讀 8 份藍圖斷言 backward 清單非空且包含歷史名）；實機則需要一個舊版存檔才能證。

**風險**：P2（未來風險，但代價極高）。

---

### F-LATENT-16（P1）拆除／回退工地時沒有撤離室內工人：與已修好的「暫停崩潰」同族

**現象（玩家語氣）**
> 「我拆掉盒子農場改建，遊戲就閃退；日誌裡又是 Enterer/Enterable 那組錯誤。」

**原因（程式碼顯示缺口；實機未驗證＝推測）**
* 2.5.3 修暫停崩潰時確認的根因是「HDBF 是唯一讓農夫持續待在室內的農場，而原版 `Worker.Unemploy` 不會把 enterer 帶出來」（`docs/VERIFICATION.md` §34、`HdbfBoxFarmController.Pause.Eject.cs:1-11`）。
* 該修復只在**暫停**路徑呼叫 `EjectBeaversFromPausedFarm()`（`Pause.cs:214`、`:228`）；而**離開 finished 態／拆除**走的是 `Deactivate()`（`Activation.cs:100-146`），裡面只有 `UnbindPauseState`/`Unregister`/`DisableComponent`，**沒有撤離工人**。
* 因此「拆除一座有工人在裡面的農場」會製造與暫停崩潰完全相同的前置狀態（已解雇但仍在建築內／建築被銷毀）。原版拆除是否剛好會替我們清理 enterer 沒有證據，所以標推測。

**影響**：如果成立，會是「暫停崩潰修好之後的下一個崩潰回報」，而且比暫停更常發生（玩家會拆/搬遷農場）。

**解法**：`Deactivate()`（以及 `OnExitFinishedState`）在停用元件前呼叫同一個 `EjectBeaversFromPausedFarm()`；`DeleteEntity` 再檢查一次。這是一個 3 行級別的防禦性修復，值得先做（即使實機尚未證實）。

**信心度**：**推測**（同族程式碼路徑缺少撤離；未實機）。

**驗證方式**：實機用 `Repro3` 存檔副本（2 名工人已在室內）→ 直接拆除該農場 → 看是否潰；再對照修復後版本。也可先用 §4 harness 模擬 `Deactivate` 順序（但 Enterer 是遊戲型別，需實機）。

**風險**：P1（崩潰類，且時機可預期）。

---

### F-LATENT-17（P1）每幀成本與「4 Hz 面板觸發的 625 格水源探測」：100 棟時未量測的效能面

**現象（玩家語氣）**
> 「蓋到 20 幾座農場後開始卡；打開面板更卡。三倍速時更明顯。」

**原因（已證實的成本來源；可感測性屬推測）**
* `ConsumeElapsedWater` 是**每幀**、每座農場呼叫（`HdbfWaterFlowService.UpdateSingleton` → `farm.ConsumeElapsedWater`），而它每幀都做 `ApplyHauledWater()`（`:36`）：其中包含 `EnsureWaterInventoryEnabled()` → 庫存不存在時**每幀重新掃 `Inventories.AllInventories`**（`WaterSupply.cs:94-121`、`:37-83`），以及 `UnreservedAmountInStock` 查詢（`:178-196`）；每幀成功扣水還會 `_saveDirty = true`（`WaterFlow.cs:46-50`）。
* 另一條：面板 4 Hz 讀 snapshot（`HdbfFarmPanelFragment.cs:43`、`:141-159`），而 snapshot 每 16 次讀取觸發一次 `RefreshWaterSupply`（`WaterSupply.cs:20-27`、`:218-243`）→ `HdbfManualWaterSource.TryFindSource` 掃 **25×25 = 625 格**水體圖、每格最多再往上下找 17 個 z（`HdbfManualWaterSource.Search.cs:39-102`、`:108-154`），再加導航可達性。世界圖標服務每秒也會對最多 24 座農場各取一次 snapshot（`HdbfWorldIconService.Update.cs:59-104`）。
* `docs/VERIFICATION.md` §26.1 的 NFR 只量了「日 tick 0.0113 ms/棟」，**沒有量逐幀排水與面板/圖標 snapshot 路徑**。

**影響**：在 100 棟（REQ 的目標規模）或低階機上可能是「莫名掉幀」；玩家不會歸因到模組。

**解法**：*短期*：把 `ApplyHauledWater` 從逐幀路徑移到「有貨物送達」的事件/節流（例如每 N 幀或每 0.5 s）；水庫存解析失敗時加冷卻，不要每幀重掃；`_saveDirty` 只在實際變化時設。水源探測的快取時間延長並按距離排序提早跳出。*長期*：加一個「每幀成本」計數器/diagnostic gate（像 `HdbfBoxFarmFragment` 的 4 Hz 一樣），並在 L5 建 100 棟 fixture。

**信心度**：**已證實（成本存在）／推測（玩家可感測）**。

**驗證方式**：離線計數（包一層計時器或加 counters 統計每幀呼叫次數）；實機 Profiler：100 座農場、面板關/開、1×/3× 三種情境。

**風險**：P1（規模化時）。

---

### F-LATENT-18（P2）面板用「編譯期 TierCatalog」，模擬用「config 覆寫後的 TierProfile」→ 改過 `HDBF_Config.json` 的玩家看到兩套數字

**現象（玩家語氣）**
> 「我把 config 的水罐容量改大、維護材料改少，面板顯示的 C_min/材料量跟實際行為不一樣。」

**原因（已證實：程式碼）**
* UI 讀模型用編譯期表：`HdbfIndoorPanelFactory.FindProfile(snapshot.Tier)` → `TierCatalog.Get`（`HdbfIndoorPanelFactory.cs:273-284`），C_min、材料量/週期、槽位上限都由此推導；`BuildSlotsSection` 的 `SlotsPerFarmer` 也是常數。
* 模擬用 config：控制器 `ResolveTierProfile` 走 `IHdbfGameConfigProvider.TryGetTierProfile`（`Activation.cs:225-252`），而 `HDBF_Config.json` 明確開放 `balance.tier` 覆寫（`data/HDBF_Config.json`）。

**影響**：config 覆寫是文件化的功能（REQ-IMPL-03「可從 JSON 覆寫」），但面板不會反映 → 玩家會回報「面板數字是錯的」。也讓 QA 更難用 config 做平衡實驗。

**解法**：把解析後的 `TierProfile` 塞進 `IndoorFarmSnapshot`（或讓 UI 透過 query 取得 profile），不要讓 UI 自己查編譯期表；加 L1 契約測試「config 覆寫 → 面板材料量跟著變」。

**信心度**：**已證實（程式碼）**。**驗證**：L1（注入覆寫 profile 的 snapshot → 斷言面板文字）。**風險**：P2。

---

### F-LATENT-19（P3）休耕/水耗的「文件 vs 實作」漂移：ADR 說「禁止逐幀扣水」，程式碼就是逐幀扣水

**現象**：玩家不會直接看到，但這是**下一個調查者會踩的坑**，也會讓「水罐為什麼每秒都在掉」變成每次回報的爭論點。

**原因（已證實）**：`docs/v2/ADRS.md` ADR-V2-004 寫「`tank` 由 `WaterMath` 每日結算一次…**禁止逐帧扣水**（REQ-V2-WATER-02）」；實作已改成逐幀（`IndoorWaterMath.Flow.cs`、`HdbfWaterFlowService.cs`、`HdbfBoxFarmController.WaterFlow.cs`），程式碼註解說這是「2026-10-28 玩家要求」，但 ADR/需求/追蹤矩陣沒有同步更新。

**影響**：需求追溯失真；未來任何人照 ADR 修 bug 會把行為改回去。**解法**：補一份 ADR 修訂（逐幀排水取代每日結算、`REQ-V2-WATER-02` 重新定義）並更新 `docs/REQ_TRACEABILITY.md`。**信心度**：已證實（文件 vs 程式碼）。**驗證**：文件審查。**風險**：P3（工程風險）。

---

## 2. 未來回報預測排行（Top 12）

> 排序＝「發生機率 × 玩家痛感」。每條給「玩家一句話（模組語言：英文原句＋中文意思）／觸發條件／風險／建議先修的根因」。

| # | 預測留言（玩家語氣） | 觸發條件 | 風險 | 建議先修的根因 |
|---|---|---|---|---|
| 1 | **"I switched from Fallow back to Normal and my whole harvest vanished — 0 crops, empty slots."**（休耕切回來，整批作物消失） | 任何人在**休耕期間有成熟作物**，再切回常規/密植 | **P0** | F-LATENT-01：`TryHarvest` 在 `amount==0` 仍清槽＋`ModeTransitionYieldScale` 由 Fallow 算出 0 |
| 2 | **"It says no water after 3 days and the farm just stops. I have water tanks!"**（三天後就沒水停工） | 早期玩家：無抽水站/無管道/附近沒水就蓋農場 | **P0** | F-LATENT-03：水罐 3 天＋三條補水路徑都沒在 UI 說清楚 |
| 3 | **"My farms have no pipes nearby but the pipe port says connected and water keeps refilling."**（沒接管也能補水） | 地圖上任何一處「接在管網上的水箱」＋任意盒子農場 | **P0（平衡）** | F-LATENT-02：`HasNetwork` 掃全 settlement 而不是本農場 |
| 4 | **"After updating Timber Pipes all my farms went dry at once."**（更新管道模組後全場同時斷水） | Timber Pipes 改版使錨點失效（完全失效→全場同日沒水；部分失效→3 次抽水失敗把 adapter 打成 session Failed） | **P1** | F-LATENT-02(c)：全域 health 決定每座農場＋3-strike 無時間窗 |
| 5 | **"I clicked refill and lost water / lost 2 actions for almost nothing."**（補水浪費） | 水罐剩 90% 以上時按補水 | **P1** | F-LATENT-04：先抽滿 1/3 罐才做 headroom 截斷 |
| 6 | **"Panel says maintenance material missing but I have planks in two warehouses."**（材料分散就說不足） | 多倉庫/多區、材料分散 | **P1** | F-LATENT-07：加總判定 vs 單庫存交易不一致 |
| 7 | **"The farm says 'no water source' but a water pump is 15 tiles away, and the farmers fetch water themselves."**（12 格規則不可理解） | 最近水源 >12 格或不可達，但工人搬水正常 | **P1** | F-LATENT-05：12 格硬限制＋兩套補水模型沒有 UI 說明 |
| 8 | **"I turned the pipe port off and it turned itself back on next day."**（管道接口自己回來） | 玩家手動關 pipe port（想省水/除錯） | **P1** | F-LATENT-06：`UpdatePorts` 每個日 tick 覆寫玩家選擇 |
| 9 | **"The map warning icon doesn't match the panel / stays for a whole day after I fixed it."**（圖標與面板不同步） | 水罐中途見底或被補滿、剛按完維護 | **P1** | F-LATENT-11：圖標讀日 tick 快取、面板讀即時 |
| 10 | **"My farm shows 100% water and green everything but nobody is working there."**（沒工人卻全綠） | 工人被調走/未指派（尤其新蓋好的農場） | **P2** | F-LATENT-10：0 工人 → demand 0 → coverage 100% |
| 11 | **"The output port says connected but the conveyor doesn't take anything."**（物流端口是假的） | 安裝 Conveyor Belts v2，把帶子接農場 | **P2** | F-LATENT-09：物流端口只是能力標籤，沒有輸出實作 |
| 12 | **"I set a fixed crop from my crop mod and the panel says it's invalid / shows the English name."**（作物模組不相容） | 安裝作物模組或遊戲新增作物 | **P2** | F-LATENT-12：下拉/警告/名稱全吃內建 fallback 表 |

**候補（第 13–15，較低機率但代價高）**
* 「拆除農場時閃退」（F-LATENT-16，與已修暫停崩潰同族，推測）；「讀檔後農場進度回捲」（F-LATENT-14，需存檔比對）；「更新後舊農場被刪除／位置無效」（F-LATENT-15）。

---

## 3. 未解問題 / 需要下一輪補證

1. **F-LATENT-14 的實際觸發條件**：需要一份「槽位含未知作物 / dueDay=0 / 分散倉庫材料」的真實存檔＋`Player.log`，才能把「靜默不存檔」從推測升為已證實。建議用 §4 的 net9 harness 先跑 `IndoorEntitySaveBridge.Save` 的負向案例。
2. **F-LATENT-16（拆除崩潰）**：需要實機（`Repro3` 副本 → 拆除含工人的農場）。這是本清單中最值得**先寫防禦性修復**的一條（成本 3 行）。
3. **F-LATENT-17 的可感測性**：需要 100 棟 fixture + Profiler；離線只能量呼叫次數，不能量遊戲幀時間。
4. **F-LATENT-02 的實機 A/B**：需要「只裝 Timber Pipes + 一座未接管農場」的乾淨組合（`CompatMatrix.ps1 -Adapters TimberPipes` 已有工具），看 tank 是否異常上升、以及模擬 anchor 失效時是否三座農場同日斷水。
5. **兩個世界圖標 key（AdapterFailed/Shutdown）是否真的會被啟用**：`HdbfUiLocKeys.cs` 有常數，但目前找不到使用點；若刪除請確認 `tools/validate` 的三語鍵集同步。
6. **`HdbfFarmPanelFragment` 高度/滾動**：`docs/VERIFICATION.md` §28.2/§32 已記錄 1080p 需滾動，本輪無法判讀面板是否遮住「槽位」卡；屬實機可點擊會話的待辦（不在本檔判定）。

---

## 4. 附錄：主要證據索引（檔案:行）

| 條目 | 證據 |
|---|---|
| F-LATENT-01 | `src/HDBF.Core/Indoor/IndoorDayTick.cs:185-193`；`IndoorGrowthMath.cs:25-39,114-130`；`IndoorLaborMath.Scheduling.cs:27-61`；`HdbfBoxFarmController.Tick.cs:144-154`；`Commands.cs:90` |
| F-LATENT-02 | `src/HDBF.Integration/Adapters/TimberPipesWaterSource.cs:97-122,229-259`；`TimberPipesWaterBinding.cs:130-191`；`IndoorAdapterHost.cs:75-120,139-149`；`src/HDBF.Core/Compat/CompatStateMachine.cs:26,132-153` |
| F-LATENT-03 | `src/HDBF.Core/Balance/TierCatalog.cs`（3.6/10.8/33.6/76.8）；`IndoorWaterMath.cs:51-70`；`IndoorLaborMath.cs:30-37`；`IndoorDayTick.cs:192-193`；`IndoorGrowthMath.cs:142-186` |
| F-LATENT-04 | `HdbfBoxFarmController.ManualRefill.cs:92-124`；`IndoorConstants.cs:84-87` |
| F-LATENT-05 | `IndoorConstants.cs:90`；`HdbfManualWaterSource.Search.cs:39-102,161-227`；`HdbfGameConfigurator.cs:115-121`；`ManualRefill.cs:141-172` |
| F-LATENT-06 | `HdbfBoxFarmController.Context.cs:245-297`；`HdbfBoxFarmController.Commands.cs:107-150`；`Context.cs:77-104`（`ReadPipeWater`） |
| F-LATENT-07 | `IndoorMaintenanceMaterialSource.cs:39-84,112-131`；`HdbfMaintenanceTransaction.cs:82-97,109-116`；`IndoorDayTick.cs:244-247` |
| F-LATENT-08 | `HdbfFarmCommandService.cs:85-109`；`HdbfBoxFarmController.Commands.cs:213-273` |
| F-LATENT-09 | `HdbfBoxFarmController.IndoorSnapshot.Helpers.cs:14-24`；`IndoorAdapterHost.cs:123-137`；`Context.cs:258`；`data/HDBF_Config.json`（`logisticsOutput:false`） |
| F-LATENT-10 | `IndoorLaborMath.cs:30-37`；`IndoorWaterMath.cs:51-70`；`IndoorWarnings.cs:91-148`；`HdbfIndoorPanelWarnings.cs:114-136` |
| F-LATENT-11 | `HdbfWorldIconService.Evaluate.cs:28-31`；`HdbfBoxFarmController.Tick.cs:156-174`；`HdbfIndoorPanelWarnings.cs:34-59`；`IndoorSnapshot.cs:36-62` |
| F-LATENT-12 | `HdbfIndoorPanelWarnings.cs:159-171,177-206`；`HdbfIndoorPanelFormat.cs:160-183`；`GameCropCatalog.cs:178-237`；`Commands.cs:176-184`；`IndoorSnapshot.cs:219-255` |
| F-LATENT-13 | `data/Localizations/zhCN.csv`（`HDBF.Error.RefillUnavailable`、`HDBF.Warning.AdapterFailed`、`HDBF.Warning.Shutdown`、`HDBF.Panel.WaterShortage`）；`HdbfUiLocKeys.cs:211-212`；`HdbfWorldIconService.Update.cs:173-186` |
| F-LATENT-14 | `IndoorEntitySaveBridge.cs:76-81`；`IndoorSlotRepair.cs:144-233`；`IndoorSaveFieldRepair.cs:103-129,135-160`；`IndoorSaveValidator.cs:157-161` |
| F-LATENT-15 | 8 份 `data/Buildings/Food/BoxFarm/*.blueprint.json`（`BackwardCompatibleTemplateNames: []`、`Size {3,3,1}`）；`docs/VERIFICATION.md` §25（`not backward compatible. Deleting it.`） |
| F-LATENT-16 | `HdbfBoxFarmController.Activation.cs:100-146`（`Deactivate` 無撤離）vs `Pause.cs:214,228`（暫停/恢復有撤離）；`Pause.Eject.cs:1-11`；`docs/VERIFICATION.md` §34 |
| F-LATENT-17 | `HdbfWaterFlowService.cs:100-149`；`HdbfBoxFarmController.WaterFlow.cs:25-59`；`WaterSupply.cs:20-27,37-83,94-121,152-243`；`HdbfFarmPanelFragment.cs:43,141-159`；`HdbfManualWaterSource.Search.cs:39-154`；`docs/VERIFICATION.md` §26.1（只有日 tick 預算） |
| F-LATENT-18 | `HdbfIndoorPanelFactory.cs:273-284`（`TierCatalog.Get`）；`Activation.cs:225-252`（config profile）；`data/HDBF_Config.json`（`balance.tier`） |
| F-LATENT-19 | `docs/v2/ADRS.md` ADR-V2-004 vs `IndoorWaterMath.Flow.cs`、`HdbfWaterFlowService.cs`、`HdbfBoxFarmController.WaterFlow.cs` |

---

## 5. 一句話總結（給 Lead 的決策用）

**最該先修的兩件事跟「已知崩潰」無關**：(1) **F-LATENT-01 休耕轉換日清空作物**——一個 L1 測試就能釘死、修法 2 行，但它會讓「休耕」這個玩家會主動使用的功能直接吃掉一整盒收成；(2) **F-LATENT-03 水罐 3 天見底＋三條補水路徑沒說清楚**——這是設計代價，但也是「農場壞了」回報的最大來源，改 UI 文案與補水/搬水鏈的揭露就能大幅降低回報量。第三名是 **F-LATENT-02 管道連通是全域假訊號**：它同時是平衡漏洞、矛盾面板與「模組改版一夜斷水」的根因。
