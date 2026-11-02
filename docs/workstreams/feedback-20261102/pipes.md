# F-PIPE：已接管道，Box Farm 仍顯示「No Connection／未連接」

> **狀態**：調查完成（唯讀）；**未修改任何 `src/`、`data/`、`tests/`、`tools/`、藍圖或本地化檔，未執行 `build.ps1`，未啟動遊戲、未安裝/移除任何遊戲檔案**
> **日期**：2026-11-02｜**模組**：v2.5.3（2026-10-29，HEAD `de1015c`）｜**遊戲**：Timberborn 1.1.2.8
> **任務**：task-3（Feed-Bug-03）｜**本檔為本工作流唯一寫入檔**
> **玩家回饋**（Annastasya，Steam Workshop 2026-11-02，v2.5.3 之後）：
> 「having pipes connected to the Box Farm, it still shows "No Connection"」，且回報數分鐘後致命崩潰。
> 崩潰本身另案調查；本檔第 4 節只判斷「是否同源」。

**證據等級標記**：**【已證實】**＝本機原始碼／已安裝 DLL metadata+IL／藍圖／本地化檔／既有實機日誌可直接證明；**【推測】**＝由程式結構推得、但未實機重現；**【待驗證】**＝本次未做（唯讀、未開遊戲），已列驗證步驟。

---

## 0. 一句話結論

**「No Connection」不是 TimberPipes 或原版的字串，而是 HDBF 面板 Ports 卡的 `HDBF.Port.NotConnected`（enUS「Not connected」、zhTW「未連接」）。最可能根因是一條「設計與期待落差」：Box Farm 根本不是 TimberPipes 端點，而 HDBF 的「已連接」判定其實是「整個 settlement 的 `PipeRegistry.Tanks` 裡有沒有一顆已接上管網、吃 Water 的液體水塔（`PipeTank`）」——與農場旁邊有沒有鋪管完全無關。玩家把管接到農場外殼，任何 TimberPipes 元件都不會被建立，判定因此永遠 false：面板顯示未連接，且永遠不會自動補水。**

**根因鏈一句話（含證據行號）**：

> `Ports 卡：Pipe port ＝ 未連接`
> ⇐ `snapshot.WaterSupply != Pipe`（`src/HDBF.UI/Panels/HdbfIndoorPanelFactory.Cards.Output.cs:109-110`）
> ⇐ `IndoorAdapterHost.IsPipeConnected == false`（`src/HDBF.Integration/Adapters/IndoorAdapterHost.cs:106-120`）
> ⇐ `TimberPipesWaterSource.HasNetwork == false`（`src/HDBF.Integration/Adapters/TimberPipesWaterSource.cs:97-122`）
> ⇐ 全 settlement `PipeRegistry.Tanks` 中沒有「`IsFinished && Pipe.Graph != null && TakesGood("Water")`」的 `PipeTank`（`src/HDBF.Integration/Adapters/TimberPipesWaterBinding.Tank.cs:17-45`）
> ⇐ **Box Farm 藍圖沒有任何 TimberPipes 元件**（只有 UI 旗標 `"HasPipePort": true`，`data/Buildings/Food/BoxFarm/BoxFarm.Basic.Folktails.blueprint.json:187`；`data/` 全目錄 grep `Pipe|Stockpile` 在農場藍圖 0 命中），而 **`PipeTank` 只會被加到「液體 Stockpile」建築**（`TimberPipes.Components.TankPipe` 的 template-module 屬性是加在 `Stockpile` 上，`TankPipe.GetBuildingPipeSpec()` 只在 `StockpileSpec.WhitelistedGoodType == "Liquid"` 時產生管道端口；原版只有 SmallTank／MediumTank／LargeTank 是液體 Stockpile）。

### 0.1 根因分級（設計 / bug / UX / 驗證缺口）

| 級別 | 編號 | 一句話 | 優先級 |
|---|---|---|---|
| **Bug（語義＋判定）** | F-PIPE-01 | 「Pipe port」是虛擬端口：農場不是 TimberPipes 端點，接在農場外殼的管對 HDBF／TimberPipes 都不存在；`Not connected` 實際意思是「全域水箱清單裡沒有可抽水的 PipeTank」，不是「農場沒接管」 | **P0**（本回報主因） |
| **Bug（判定錯位）** | F-PIPE-02 | 判定只看 `PipeRegistry.Tanks`，**沒有農場連通/座標條件**：①假陰性＝農場旁有管但沒有水塔 → 永遠未連接；②假陽性＝世界上任何一顆接上管網的水塔（不分距離、不分 district）都能讓農場顯示「已連接」並隔空抽水（平衡漏洞） | **P0**（①）／P1（②） |
| **UX** | F-PIPE-03 | 面板只給「Not connected」，沒有原因、沒有正確接法提示；Water 卡「Water source: none」與「Connected 但水箱乾」也是同類語義混淆 | **P1** |
| **診斷缺口** | F-PIPE-04 | v2 水 binding 的 8 個反射成員（`get_Tanks`、`get_VolumeM3`、`ApplyVolume`、`get_Graph`、`GoodsToVolume`…）**不在 adapter health 的 4 個 A 級 anchor 內**；綁定失敗只回 false、不進三振、無 `compat.failed`，表現與「沒有水塔」完全相同（版本漂移會被誤認為玩家不會接） | **P1** |
| **驗證缺口** | F-PIPE-05 | §19.6「連接時自動補水」**從未在遊戲內驗證過**（文件自承）；現有 in-game fixture 從未建過任何 PipeTank，因此「這條自動抽水路徑其實一次都沒成功過」也無法排除 | **P0（先驗證）** |

### 0.2 最小維修順序（建議）

1. **F-PIPE-01/03 文案＋原因列**（純 UI/本地化，改動小、風險低，先讓玩家看得懂「為什麼未連接、正確接法是什麼」）。
2. **F-PIPE-05 實機 fixture**（一顆 vanilla 液體水塔＋管網），確認自動抽水到底能不能通；這是所有後續修復的前提。
3. **F-PIPE-04 診斷**（水 binding 錨點納入健康檢查／面板原因碼，讓版本漂移可辨識）。
4. **F-PIPE-02 真實連通判定**（用既有 anchor `PipeRegistry.TryGetGraph` ＋新的農場端口資料；或至少把「全域抽水」寫成明確設計並在 UI 標示）。
5. **完整端點方案**（農場 blueprint 加 `BuildingPipeSpec`／`PipeTank`）列設計決策，不在本輪實作。

---

## 1. 現象：玩家看到的到底是哪一行

### 1.1 面板結構（v2 室內農場）

| 卡片 | 列 | 文字鍵 | 玩家所見（enUS） |
|---|---|---|---|
| Water（水） | 水罐／覆蓋率／C_min／**水源** | `HDBF.Panel.SupplyLabel` + `SupplyKey` → `PanelWaterPipe/Manual/None` | `Water source: pipe / manual / none`（`Cards.Water.cs:62-69`；`HdbfIndoorPanelFormat.cs:123-131`） |
| Water 警告列（僅 C=0 或警告時） | 缺水 | `HDBF.Warning.NoWater` + `Tooltip.NoWaterSource` | `Water shortage · No water source is connected; the tank only drains.`（`HdbfIndoorPanelWarnings.cs:44-49,241-252`；enUS:143） |
| Output（輸出） | Output port | `HDBF.Panel.OutputPortLabel` + `PortConnected/NotConnected` | `Output port: Not connected`（**永遠會出現**，無物流模組時也是這行；`Cards.Output.cs:100-108`） |
| Output（輸出） | **Pipe port** | `HDBF.Port.Pipe` + `PortConnected/NotConnected/NotApplicable` | **`Pipe port: Not connected`**（回報最可能指這行；`Cards.Output.cs:109-110,133-148`） |
| Output（輸出） | Logistics port／Power port | 同上 | 無模組/無該端口時 `Not applicable` |

- **【已證實】** 農場四檔都開 `HasPipePort`：`src/HDBF.Gameplay/Specs/HdbfBoxFarmSpec.cs:87-89`（預設 true）、藍圖 `BoxFarm.*.blueprint.json:187/191`；所以不論哪一檔，Ports 卡都會有一列 Pipe port。
- **【已證實】** `Pipe port` 的「已連接/未連接」不是 `record.PipePort`（那是「端口存在/可見」），而是 **`snapshot.WaterSupply == WaterSupplyKind.Pipe`**（`Cards.Output.cs:109`）。`WaterSupply` 只有三個值：`pipe/manual/none`（`HdbfIndoorPanelFormat.cs:123-131`、enUS:38-40）。

### 1.2 「No Connection」字串來源：**不是** TimberPipes、**不是**原版

**【已證實】** 本機做了四層字串掃描，結論一致：**精確字串「No Connection／No connection」在 HDBF、TimberPipes 11.4.0、全部 296 個 workshop DLL、497 個 Timberborn 組件、遊戲 `resources.assets`（UTF-8 文字資產）都不存在**。

| 掃描對象 | 方法 | 結果 |
|---|---|---|
| HDBF 全 repo | ripgrep `No Connection|no connection` | 0 命中 |
| HDBF 三語本地化 | `data/Localizations/{enUS,zhCN,zhTW}.csv` 全表 | 只有 `HDBF.Port.NotConnected`＝`Not connected`／`未连接`／`未連接`（enUS:81、zhCN:81、zhTW:81）；`HDBF.Port.Pipe`＝`Pipe port`／`管道接口`（:78） |
| HDBF 歷史 | `git log -S'No connection' -- data/Localizations` | 0 commit（字串從未存在過） |
| TimberPipes 11.4.0 | `Localizations/enUS.csv` 全表＋DLL ASCII/UTF-16 字串 | 0 命中；只有例外訊息 `Port {0} on {1} is already connected…` 與類名 |
| 全部 workshop DLL（296 支、26.7 MB） | ASCII＋UTF-16 字串搜尋 `no connection|not connected` | 0 命中 |
| 全部原版 `Timberborn*.dll`（497 支） | 同上 | 0 命中 |
| 遊戲 `resources.assets`（646 MB） | 分塊 UTF-8 掃描 `No Connection|No connection|Not connected|Unconnected` | 只有 `Unconnected`（原版 `UnconnectedBuilding` 狀態圖示名，用在「入口被擋」而不是管道；HDBF 也重用它在 `HdbfWorldIconService.cs:60`） |

**結論（已證實）**：玩家引號裡的 "No Connection" 是**對 HDBF 面板英文/中文的轉述**，最貼近的實際字串依序是
1. `Pipe port` 那一列的 **`Not connected`**（enUS:81；zhTW「未連接」）；
2. 缺水警告列句子 **`No water source is connected; the tank only drains.`**（enUS:143）；
3. `Water source: none`（enUS:40）。
因此本調查定位為 **HDBF 顯示層**；沒有證據顯示 TimberPipes 有「No Connection」UI（它的面板 fragment 只有 PipeTank 液位、閥門 Fill/Extract、污染、headlift 等，見 `TimberPipes.UI.*Fragment` 與其 enUS.csv）。**【已證實】**

### 1.3 世界圖示沒有「未連接管道」這一類

**【已證實】** HDBF 世界圖示只有：缺水（`BuildingNeedsWater`）、維護（`UnspecifiedRecipe`）、無電（`NoPower`）、入口被擋（`UnconnectedBuilding`）（`src/HDBF.Gameplay/Components/HdbfWorldIconService.cs:55-60`；`data/Localizations` `HDBF.Warning.*`）。玩家不可能從世界圖示讀到「No Connection」；`UnconnectedBuilding` 這張圖是「入口被擋」，別混淆。

---

## 2. 現況事實（逐條附證據）

### 2.1 Box Farm 不是 TimberPipes 端點（沒有任何管道元件）【已證實】

| 證據 | 內容 |
|---|---|
| HDBF 藍圖 | `data/Buildings/Food/BoxFarm/*.blueprint.json` 只有 HDBF 自己的 spec：`HdbfBoxFarmSpec`、`SimpleOutputInventorySpec`、`WorkplaceSpec` 等；**沒有** `BuildingPipeSpec`、`PipeTank`、`Stockpile`、`BuildingPipeTargetPortsSpec`。`HasPipePort: true` 只是 `HdbfBoxFarmSpec` 的資料旗標，供 UI 決定是否顯示一排「Pipe port」（`HdbfBoxFarmSpec.cs:87-89`；UI 只在 `Cards.Output.cs:109` 讀 snapshot）。 |
| HDBF 原始碼 | 全 `src/` grep `PipeTank|BuildingPipe`：**沒有任何新增 TimberPipes 元件的程式**（只有 Integration 的反射讀取）。v2 起全模組 **0 Harmony patch**（`src/HDBF.Integration/HdbfIntegrationConfigurator.cs:23-25`；`src/HDBF.Core/Compat/CompatReport.cs:47-48` 永遠發佈 `harmonyPatchCount = 0`），所以也沒有 patch 手段去替農場掛管道元件。 |
| 資料夾 | HDBF `data/` 內沒有 `Stockpile` 元件，因此 TimberPipes 的 `TankPipe`→`PipeTank` 自動模板模組也不會被觸發。 |

**因此：拿 pipe 方塊貼在農場外牆，兩邊都不會建立任何「連接」——TimberPipes 不知道這棟建築有管道端口，HDBF 也不知道。**（【已證實】由上述兩邊的程式/資料結構推得）

### 2.2 TimberPipes 的 PipeTank 只會長在「液體 Stockpile」上（原版＝三種水塔）【已證實】

從已安裝 `TimberPipes.dll`（11.4.0，`C:\Program Files (x86)\Steam\steamapps\workshop\content\1062090\3801726769\version-1.1\TimberPipes.dll`，225,792 B）用 Mono.Cecil 讀 metadata＋IL，得到：

| 事實 | 證據（IL／attribute） |
|---|---|
| `PipeRegistry.Tanks` 是「全 settlement 的 PipeTank 清單」 | `TimberPipes.Services.PipeRegistry` 欄位 `List<PipeTank> tanks`；`get_Tanks()` 直接回傳它；`AddToBuckets` 在 `BuildingPipe` 註冊時 `tanks.Add(pipe.Tank)`。介面是 `ILoadableSingleton`（不是 district-scoped）。 |
| 哪種建築會有 PipeTank | `TimberPipes.Components.PipeTank` 掛有 `[AddTemplateModule2(typeof(TankPipe))]`；`TankPipe` 掛有 `[AddTemplateModule2(typeof(Stockpile))]`；`BuildingPipe` 掛有 `[AddTemplateModule2(typeof(BuildingPipeSpec))]` 與 `[AddTemplateModule2(typeof(IGeneratedBuildingPipeComponent))]`。 |
| 只有液體 Stockpile 才有管道端口 | `TankPipe.GetBuildingPipeSpec()` IL：取 `StockpileSpec`，若 `WhitelistedGoodType != "Liquid"` → 回 `null`（不生成端口）；否則 `BuildingPipeLayout.FromBlockObject(BlockObject, PipePortState.Open)`。 |
| 原版哪些建築符合 | 掃 `StreamingAssets/Modding/Blueprints.zip`（1067 entries）中 `"WhitelistedGoodType": "Liquid"`：只有 `Buildings/Storage/{SmallTank,MediumTank,LargeTank}.{Folktails,IronTeeth}.blueprint.json`（+ MapEditor ReserveTank）。SmallTank `MaxCapacity=30`、MediumTank 300、LargeTank 1200。 |
| 水塔要「已接上管網」才算 | `BuildingPipe.Graph`（`null`＝沒接網路）；HDBF 用 `HasPipeGraph` 過濾（見 2.3）。 |
| 每個 BuildingSpec 都會有 `BuildingPipeTarget`（可以「倒水進建築」） | `TimberPipes.Components.BuildingPipeTarget` 掛 `[AddTemplateModule2(typeof(Buildings.BuildingSpec))]`；`DefaultBuildingPipeConnectionProvider` 用建築的 enabled inventories 的 liquid id 判定可 Fill/Extract。→ 單向閥把出水端對準建築時，可以把水 **goods** 灌進該建築的庫存（這是 TimberPipes 的 building-target 功能，不是 PipeTank）。 |

**→ 正確的「自動補水」接法（依實作）是：蓋一顆原版液體水塔（Small／Medium／Large Tank），把它接進 TimberPipes 管網（管網要有水進得去），HDBF 會從這顆水塔抽水。農場本身不需要、也無法連管。**

### 2.3 HDBF 的「已連接」判定＝全域水箱清單過濾（與農場位置無關）【已證實】

```
IsPipeConnected (IndoorAdapterHost.cs:106-120)
  = PipeWaterSource is TimberPipesWaterSource pipes && pipes.HasNetwork
HasNetwork (TimberPipesWaterSource.cs:97-122)
  = adaptor serving (health Ok/Degraded, :261-272)
    且 TryEnumerateFeedableTanks 成功 (TryGetTanks → PipeRegistry.get_Tanks, Binding.cs:228-255)
    且 任一 tank 通過 TryDescribeFeedableTank (Tank.cs:17-45)
TryDescribeFeedableTank = IsFinished && HasPipeGraph && TakesGood("Water") && FluidGoodId∈{null,Water}
```

- **【已證實】** 這條路徑**完全沒有**「農場座標」「農場是否在該 pipe graph 上」「距離」「district」條件。
- **【已證實】** `CompatAnchorTable.cs:262-272` 有 `TimberPipes.Services.PipeRegistry.TryGetGraph(UnityEngine.Vector3Int, out PipeGraph)` 這個 anchor，但全 `src/` **沒有任何地方使用它**——也就是「用座標查管網」的能力本來就有，只是 HDBF 的 v2 水路徑沒接上。
- **【已證實】** 反向問題：只要世界上任何一顆「已完成＋在管網上＋吃 Water」的水塔存在（例如遠方抽水站旁的水塔），所有農場都會顯示 `Pipe port: Connected` 並從它抽水；水會「隔空」送進農場（跨 distance／跨 district 皆無限制，因為 `PipeRegistry` 是 `ILoadableSingleton`）。**【推測】** 這是平衡漏洞；是否可接受是設計決策（§19.6 只寫「連接時自動補水」，沒有定義「連接」）。
- 另外 `HasNetwork` 不要求水塔裡有水；`IsAvailable` 才要求 `VolumeM3 > 0`（`TimberPipesWaterSource.cs:63-91`）。所以面板的「Connected」也可能出現「已連接但沒水」的狀態；反之「未連接」不代表附近沒管。

### 2.4 面板狀態機：`PipePort` 是「端口存在」，不是「已連接」【已證實】

| 步驟 | 位置 | 行為 |
|---|---|---|
| 每日 `UpdatePorts` | `HdbfBoxFarmController.Context.cs:245-297` | `nextPipe = _spec.HasPipePort && !adapterHidden`；`adapterHidden = health == Disabled || Unknown`。Adapter Ok/Degraded/Failed → `record.PipePort = true`；只有「完全偵測不到 TimberPipes」才變 false。 |
| 每日 `ReadPipeWater` | `Context.cs:77-111` | `record.PipePort == false` → 直接 null；否則 `_adapterHost.IsPipeConnected` 為 false 時寫節流日誌 `water.pipe-unavailable: pipe port has no usable water connection; manual refill stays possible`（:87-93），且**不嘗試抽水**。 |
| 每日 tick | `HdbfBoxFarmController.Tick.cs:60,74` | 先 UpdatePorts，再 ReadPipeWater → 進 `IndoorDayTick`；`hasPipeWater=false` 時整段管道補水跳過（`IndoorDayTick.cs:79-91`）。 |
| 面板 | `HdbfBoxFarmController.IndoorSnapshot.cs:62,73,167-194` | `WaterSupply()` 每次面板快照即時判斷：`PipePort && IsPipeConnected && PipeWaterSource != null` → `Pipe`；否則 manual / none。 |
| 每日結算 | `ManualRefill.cs:141-172` | `_waterSupply = hasPipeWater ? Pipe : (manual source 可達 ? Manual : None)`；手動來源是 12 格內的實體水源探測。 |
| Ports 列 | `Cards.Output.cs:109-110,133-148` | `PortRow(snapshot.PipePort, snapshot.WaterSupply == WaterSupplyKind.Pipe, ...)` → 「Connected／Not connected／Not applicable」。 |

**回答 Lead 的疑問（已證實）**：
* `record.PipePort` 的語義是「**端口存在／adapter 可見**」，不是「已連接」；連接狀態不在存檔裡，是 UI 即時由 `WaterSupply` 算。
* `UpdatePorts` **不會**在「偵測失敗」時把 `PipePort` 改成 false 而造成永久未連接；只有「adapter 完全 Disabled/Unknown」才會 false，而那種情況面板顯示的是 **`Not applicable`（不適用）**，不是 `Not connected`。Degraded/Failed 仍會顯示 `Not connected`。
* 因此玩家看到的 `Not connected` 對應的是 **adapter 有被偵測到，但 `HasNetwork` 永遠 false**（沒有可餵水的 PipeTank），不是端口被關掉。

### 2.5 adapter health 與「水 binding」是兩套東西；失敗是靜默的【已證實】

| 層 | anchor／成員 | 是否納入健康檢查 |
|---|---|---|
| Adapter A 級 anchor（4 個，`CompatAnchorTable.cs:262-272`） | `PipeRegistry.TryGetGraph`、`PipeFluidSimulator.Tick`、`PipeGraph.RefreshContamination`、`PipeTank.TakesGood` | 是；log 顯示 `compat.enabled:…TimberPipes active; api-anchors=4` |
| v2 水 binding 額外成員（`TimberPipesWaterBinding.cs`） | `PipeRegistry.get_Tanks`、`PipeTank.get_IsFinished/get_VolumeM3/get_FluidGoodId/get_Pipe/ApplyVolume(float,string)`、`BuildingPipe.get_Graph`、`PipeFlowSolver.GoodsToVolume(int)`、`PipeFluids.WaterGoodId` | **否**（沒有任何健康回報路徑） |

* 綁定失敗時：`TryBind()` 回 false → `TryGetTanks` 回 false → `HasNetwork` 回 false（`TimberPipesWaterSource.cs:97-122`），**不呼叫** `ReportRuntimeFailure`（只有 `TryDraw` 會，而 `TryDraw` 只有在 `HasPipeWater=true` 時才會被叫到）。所以：**玩家只會看到「未連接」，日誌不會有 `compat.failed`，只有一句沒有原因的 `water.pipe-unavailable`。**
* **【已證實】** 這 8 個成員從未以真實 DLL 執行過：`tools/` 的 `ModAnchorCheck`/`ReflectAnchorCheck` 只驗 19 個 A 級 anchor（grep `get_Tanks|ApplyVolume` 在 `tools/` 0 命中）；`tests/HDBF.Tests/V2AdapterHostContractTests.cs:149-164` 只是**字串檢查**「原始碼裡有沒有這些 anchor 名稱」，不是執行。→ 若 registry DI 解析（`_container.GetInstance(PipeRegistry)`，`Binding.cs:193-226`）在遊戲內失敗，也沒有任何測試會發現。

### 2.6 §19.6 的「連接時自動補水」與實作有落差；且從未實機驗證【已證實】

* 需求原文（`docs/REQ_V2_SECTION_19.md:149-151`）：「管道補水（Timber Pipes 適配器）：**连接时**按当日需求自动补水…无水源/断开时降级并警告」。實作把「連接」定義成「全 settlement 有可餵水的 PipeTank」，**沒有**「連到農場」的判定。
* 工作坊文案也讓玩家以為要接管到農場：`docs/workshop/DESCRIPTION.md:45`（`Refill it automatically through a pipe (Timber Pipes)`）、`:77`。
* `docs/VERIFICATION.md:1009-1012`（§22.6）自承：**「未完成：端口實際行為（接電/接管/出帶/斷線）…需要可互動遊戲會話」**；`docs/workstreams/compat-01-adapters.md:384` 的 TC-06（「接管道 1 日 → 拆管 1 日」）**只被定義、沒被執行**；`compat-01-adapters.md:395-408` 也寫明「沒有在遊戲內載入過這五個 mod…`-CheckLog` 只證明偵測/初始化，不能證明端口真的工作」。
* 既有 in-game fixture 從未建過任何 PipeTank（只建 Basic 農場；`docs/in-game/pause-fix*`、`round4x` 都是乾淨環境或只有 TimberPipes 空載），所以 **「HDBF 從 PipeTank 抽水」這條路徑在實機上一次都沒成功過，也無法排除整條路徑壞掉。**

### 2.7 日誌證據：同樣的「adapter OK 但無可餵水箱」狀態【已證實】

`docs/in-game/pause-fix2-A-summary.txt`、`review`／`round42-v2-summary.txt` 等既有 fixture 都出現：

```
HDBF/Compat: compat.enabled:HDBF.Adapter.TimberPipes: HDBF.Adapter.TimberPipes active; api-anchors=4
[HDBF/Compat] water.pipe-unavailable: pipe port has no usable water connection; manual refill stays possible, entityId=...
```

但那些 fixture **沒有真的接管**，所以只能證明「adapter 有載入、判定為 false、面板會顯示未連接」這條狀態機，不能當成玩家案例的重現。**【已證實】**

---

## 3. 原因排序（含可能性）

> 可能性是依「程式結構＋玩家敘述」的估計，不是統計；每一條都附「如何確認」。

| 排序 | 原因 | 可能性（估計） | 證據 | 如何確認 |
|---|---|---|---|---|
| **R1** | **設計與期待落差（F-PIPE-01）**：玩家把 pipe 接到農場外殼；HDBF 的 Pipe port 是虛擬的，判定只看全 settlement 的有水 PipeTank。管網裡沒有液體水塔（或水塔不在同一管網）→ `HasNetwork=false` → 「Not connected」且不補水。 | **~60-70%** | 2.1–2.3 全鏈已證實；玩家敘述「pipes connected to the Box Farm」正是這個誤解；面板沒有正確接法提示 | 進遊戲看該農場附近的管網上有沒有原版 Small/Medium/Large Tank（且已完工、有 pipe 貼在它生成的端口上）；或看 HDBF log 是否只有 `water.pipe-unavailable`、沒有 `compat.failed` |
| **R2** | **有液體水塔，但沒被 `PipeRegistry` 收錄**：水塔未接上管（沒有 pipe 貼在它的 4 面端口之一）、還在施工、或根本不是 liquid Stockpile（其他模組的儲存建築）、或水塔被閥門關閉/污染。 | ~20% | 2.2（PipeTank 生成條件）；`IsFinished && Pipe.Graph != null` 過濾（Tank.cs:17-45） | 點水塔看它有沒有「Tank」面板 fragment、管有沒有真的接上；HDBF log 應開始出現抽水/`water.settle` 回升 |
| **R3** | **版本漂移／水 binding 綁定或 DI 失敗（F-PIPE-04）**：玩家的 TimberPipes 不是本機驗證的 11.4.0（workshop 自動更新），`get_Tanks`/`ApplyVolume`/`get_Graph` 等簽名變了，或 `_container.GetInstance(PipeRegistry)` 在遊戲內解析失敗 → `HasNetwork` 永遠 false，但 adapter health 仍 `Ok`（4 個 A 級 anchor 不包含水成員），日誌靜默。 | ~10% | 2.5 已證實「靜默」；`docs/API_FACTS.md:201` 只釘 11.4.0；adapter 無版本區間檢查（`compat-01-adapters.md:404-406`，TC-09 未做） | 要求玩家回報 TimberPipes 版本與 `Player.log` 中 `compat.enabled` 行；若 health 不是 Ok 或水 binding 自檢失敗，即 R3 |
| **R4** | **adapter 本身 Degraded/Failed**：4 個 A 級 anchor 也解析失敗（例如玩家版本改了 `RefreshContamination`）。此時 Ports 仍顯示 `Not connected`，但 log 會有 `compat.failed`/`api-anchor-*`。 | <5% | `Context.cs:253-257`（Failed 不隱藏端口）、`compat-01-adapters.md:81,372-376` | log 搜 `compat.failed`／`api-anchor` |
| **R5** | **玩家接的是原版自來水／泵，根本沒安裝 TimberPipes** | <1%（且與字串不符） | `UpdatePorts` 在 adapter Disabled 時會把端口隱藏，面板顯示 **`Not applicable`** 而非 `Not connected`（`Context.cs:253-257`、`Cards.Output.cs:133-140`） | 看面板是 `Not applicable` 還是 `Not connected`；前者＝沒裝/沒偵測到 TimberPipes |

**最可能的情境（R1）用一句話說**：玩家在農場旁鋪了管，但管網上沒有「原版液體水塔」這顆 HDBF 唯一認得的 tank；接在農場上的管在 TimberPipes 眼中也不存在（農場沒有 pipe 端口），所以面板只能是 `Not connected`，且不會有任何自動補水。

**注意另一個「玩家其實有在補水」的變體（【推測】，需實機驗證）**：HDBF 的農場有一個真正的 `Inventory:Water` 公共輸入庫存（`HdbfWaterInventoryInitializer.cs:23-26,58-66`），且 TimberPipes 會把 `BuildingPipeTarget` 加到**所有** `BuildingSpec` 上（`AddTemplateModule2(typeof(Buildings.BuildingSpec))`）。因此玩家若用「單向閥（one-way valve）出水端指向農場＋勾 Fill」，水 goods 可能真的被倒進農場的 `Inventory:Water`，再由 `ApplyHauledWater()` 轉成水罐（`WaterSupply.cs:152-210`）——**但面板的 Pipe port 仍會顯示 `Not connected`**，因為虛擬判定只看 PipeTank。若玩家屬這種情況，他看到的是「水有進、字卻說沒接」，語義 bug 更明顯。此變體本輪未實機驗證。

---

## 4. 與「數分鐘後致命崩潰」的關係：不同源（【推測】，待崩潰 log 定案）

**判斷：本項與崩潰幾乎不可能同源。** 理由（依程式）：

1. **「No Connection」狀態下，管道路徑只讀不寫**：`ReadPipeWater` 在 `IsPipeConnected == false` 時直接 return null（`Context.cs:87-94`），不會 `TryDraw`、不會呼叫 TimberPipes 的 `ApplyVolume`；面板路徑（`WaterSupply()`）也只有反射查詢。**【已證實】**
2. 所有外部呼叫都包 try/catch：`IndoorAdapterHost`（:55-159）、`TimberPipesWaterSource`（:63-297）、`Binding`（:130-255）；失敗最多 Degraded，不會拋進遊戲主迴圈（REQ-V2-SAFE-01 的既有契約）。**【已證實】**
3. 唯一會**寫** TimberPipes 狀態的是「已連接」時每日抽水的 `PipeTank.ApplyVolume`（`TimberPipesWaterBinding.Tank.cs:52-90`），它是在 HDBF 日 tick 呼叫，而不是 TimberPipes 自己的 `PipeFluidSimulator` tick——**理論上**與模組流體求解時序交錯時可能不同步（`pending` 欄位/inventory 量化），但那是「已連接」才會走的路，與本回報的未連接狀態無關。**【推測】**，且需崩潰 stack 才能成立。
4. v2.5.3 的崩潰修正是「暫停農場導致的遊戲崩潰（暫停時停日 tick／水耗／圖示並安全移出室內工人）」（HEAD commit `6c6d64a` 訊息），與管道判定無直接交集。**【已證實】**

**結論**：不把崩潰歸因於管道 bug；若崩潰 log 顯示 `ApplyVolume`／`PipeFluidSimulator`／TimberPipes 的 stack，才需要回頭查第 3 點。等待崩潰調查員的 stack 與 log。

---

## 5. 可實作修復（分層）

> 以下只是提案；本輪未改任何檔案。所有新字串需同步 `data/Localizations/{enUS,zhCN,zhTW}.csv` 三語鍵集（validator 要求一致）。

### 5.1 P0-A：把「未連接」變成可理解的狀態（UI／本地化，低風險）

1. **文案改名**（enUS／zhTW／zhCN）：
   * `HDBF.Port.Pipe`：`Pipe port` → `Pipe network (auto-refill)`／`管道管網（自動補水）`／`管道管网（自动补水）`；
   * `HDBF.Port.NotConnected`（piPe 用）→ 專用鍵 `HDBF.Port.NoPipeTank`＝`No water tank in pipe network`／`管網內沒有可抽水的水塔`；保留 `Connected` 只在「判定為 pipe」時顯示。
   * 新增 tooltip（`HDBF.Tooltip.PipePort`）明確寫：**「自動補水只從『接上管網的原版液體水塔』抽水；管接在農場外殼不會生效。把你家 Small/Medium/Large Tank 接進管網即可，農場不必在旁邊。」**
2. **原因列**：在 Water 卡或 Output 卡的 Pipe port 列下方顯示原因碼（見 5.2 的診斷 DTO）：
   * `NoPipeTank`＝管網存在但沒有已完工水塔；
   * `NoPipeNetwork`＝附近/全 settlement 都沒有管網；
   * `TankDry`＝有水塔但沒水；
   * `AdapterUnavailable`＝TimberPipes 未載入/停用（此時列應顯示 `Not applicable`）；
   * `BindingFailed`＝API 版本不相容（見 5.2）。
3. **語義修正**：`Water supply = pipe` 不應只因「有 tank 存在」就成立，建議區分「可抽水（tank 有水量）」與「已連接管網」。這可消除「Connected 但沒水」與「實際有 valve 補水卻 Not connected」兩個矛盾。

### 5.2 P0-B：診斷（Integration，低中風險）

1. 在 `IIndoorAdapterHost` 增加**唯讀**診斷 DTO（不帶第三方型別）：
   `PipeProbe { int TankCount; int GraphTankCount; int WaterTankCount; bool RegistryBindFailed; bool WaterAnchorsOk; string? Reason }`；由 `TimberPipesWaterBinding` 在 `TryBind`/`TryGetTanks` 失敗時填入原因，不再只回 false。
2. **把水 binding 的 8 個成員做一次「子集自檢」**（可另建 anchor 子集，或在 `TryBind` 失敗時 `ReportRuntimeFailure("water-anchor-...")`），讓 `compat.degraded`/`compat.failed` 能在日誌與面板被看到；同時讓面板顯示「Timber Pipes API 不相容，請回報版本」而不是「未連接」。
3. 版本區間提示（不用 mod manager provider 也能做的最小版）：把 `PipeRegistry`/`PipeTank` 的 assembly 版本（`TimberPipes, 1.0.0.0` 沒用）或缺失成員名寫進日誌一次，方便回報。

### 5.3 P1：真實連通判定（Integration＋資料，中風險）

> 不用新增第三方編譯期參考；全部走既有反射。

1. 在 `HdbfBoxFarmSpec` 新增資料欄位（皆 `[Serialize]`、預設值安全）：`PipePortOffsetX/Y/Z`＋`PipePortDirection`（例如農場某一面外牆中心，或入口對側）。
2. 每日（或面板節流）用**已存在的 anchor** `PipeRegistry.TryGetGraph(Vector3Int, out PipeGraph)`：
   * `graph == null` → `NoPipeNetwork`；
   * `graph != null` 但 graph 內沒有符合條件的 PipeTank → `NoPipeTank`；
   * 有 → `Connected`。
3. 若要做「閥門物理補水」的偵測（把 `Inventory:Water` 被 TimberPipes Fill 也算連接），需要 iterate `PipeRegistry.All`（`IEnumerable<BuildingPipe>`）與其 `Ports`/`PipePort.Connection`，或 `ValvePipeService` 的查詢；**這需要先以 ApiIndex/Cecil 確認 public 成員並新增 anchor**（本輪未做，列工作項）。若不可行，至少用 (2) 的「管網在 port 旁」作第一層狀態，並在 UI 說明「物理補水請用單向閥 Fill」。
4. 反向修正（F-PIPE-02②）：把全域抽水限制在「農場 port 所在 graph 內的水塔」，或至少在 UI／文件明示「所有接上管網的水塔都供全 settlement 農場」，避免玩家以為要接到農場旁。

### 5.4 P2：完整方案（設計決策，不建議本輪）

讓農場成為真正 TimberPipes 端點：藍圖加 `"BuildingPipeSpec"`（ports）＋讓農場有 `Stockpile`/`PipeTank`。**風險**：① 缺 TimberPipes 時，藍圖含未知 ComponentSpec 可能解析失敗（破壞「無前置依賴」承諾與 `data/` validator）；② 需 optional blueprint injection／分開的相容藍圖機制，目前沒有。替代方案（建議寫進 workshop 說明而不是改藍圖）：
* 自動補水：原版液體水塔接進管網；
* 物理接管：單向閥 Fill 指向農場（利用 TimberPipes 的 `BuildingPipeTarget`）；
* 或手動補水／搬運工。

### 5.5 玩家臨時繞過（可立刻寫進回覆玩家的話）

1. **最穩**：蓋一顆原版 **Small／Medium／Large Tank**，把管接到**水塔**（水塔四面會自動生成管道端口），並確保水塔有水（原版抽水/搬運或 TimberPipes 灌水）→ HDBF 會自動從它補水；農場不必在管線旁。
2. **物理灌水**（【推測】，需玩家實測）：用 **單向閥（one-way valve）**，出水端對準農場、勾選 Fill；水以 goods 進農場的 `Inventory:Water`，HDBF 轉成水罐（面板 Pipe port 可能仍顯示未連接，屬顯示 bug）。
3. **完全不靠管道**：面板「手動補水」按鈕（12 格內有可達水源時可用），或讓搬運工把水搬進農場輸入庫存。
4. **檢查順序**：先看面板是 `Not applicable`（＝沒偵測到 TimberPipes）還是 `Not connected`（＝有 TimberPipes 但沒有可抽水水塔）；再把 `Player.log` 的 `compat.enabled`／`water.pipe-unavailable`／`compat.failed` 三行回報。

---

## 6. 驗證步驟（本輪未執行；供後續實作卡照做）

### 6.1 五模組矩陣中的 TimberPipes 格（既有工具）

```powershell
# 1) 只安裝 TimberPipes + 依賴閉包（腳本自己管理 HDBFCompat.* 資料夾，不碰玩家 mod）
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dev\CompatMatrix.ps1 -Adapters TimberPipes -Install
# 2) 啟動遊戲、載 fixture、建農場／水塔／管（見 6.2）
# 3) 讀 log 驗證偵測與 anchor
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dev\CompatMatrix.ps1 -CheckLog "<Player.log>" -Expect TimberPipes
# 4) 收尾
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dev\CompatMatrix.ps1 -Remove -Adapters TimberPipes
```

預期：`compat.enabled:HDBF.Adapter.TimberPipes … active; api-anchors=4`、無 `compat.failed`；**新增**（修 F-PIPE-04 後）水 binding 自檢行，例如 `compat.pipe-probe: tanks=… graph=… water=… anchors=ok`。

### 6.2 In-game round fixture（需擴充；`tools/dev/Invoke-GameRound.ps1` + `tools/dev/CraftIndoorFixture`）

目前 fixture 只會 craft 農場 payload、不會放置水塔/管道（`tools/dev/CraftIndoorFixture/Program.cs` 只處理 farm payload；`tools/dev/fixtures/craft-farm-fixture.py` 放置農場）。**需要一個新的「水塔＋管道」fixture**（或玩家存檔副本），步驟：

| # | 場景 | 操作 | 預期（修復前 → 修復後） |
|---|---|---|---|
| A | 重現回報 | 農場旁鋪 2–3 段 pipe，**不接任何 liquid tank** | 現在：`Pipe port: Not connected`＋log `water.pipe-unavailable`。修後：`No water tank in pipe network`＋tooltip 正確接法 |
| B | 對照：只接農場不管 | 同上，確認 pipe 貼在農場外牆 | 現在：狀態不變（證明「貼在農場」不被任何一邊看到） |
| C | 正確接法 | 建 SmallTank（或 MediumTank），用 pipe 接上它＋灌水；農場在別處 | 現在：應變 `Pipe port: Connected`、每日 `water.settle`/抽水後 tank 回升；若沒變＝R2/R3（binding/DI 壞） |
| D | 斷管 | 拆掉水塔旁 pipe，跑 1 日 | 次日回到未連接並告警（`water.pipe-unavailable`） |
| E | 版本漂移對照（可選） | 用不同版本 TimberPipes 或 mock 缺 `get_Tanks` | 修後：面板顯示 `BindingFailed`／log `compat.failed`，而不是「未連接」 |

日誌檢查點：`water.pipe-unavailable`（未連接）、`water.settle`（tank 數值）、`indoor.port`（pipe=True/False）、`compat.enabled`/`compat.failed`、以及修復後新增的 probe 行。**需截圖**：Water 卡＋Output 卡的 Ports 列。

### 6.3 L1 契約測試建議（不需遊戲，屬後續實作卡）

* `V2AdapterHostContractTests`：新增「無 tank／有 tanks 但無 graph／有 graph 但乾／binding 失敗」四態，斷言 `PipeProbe.Reason` 與 `IsPipeConnected`。
* `V2PanelContractTests`：新增 Pipe port 文案與原因列、not-connected 不再與「未裝模組」共用字串。
* Anchor 子集測試：把 8 個水成員列成期望清單（目前只有字串檢查，改成可重跑的 metadata 檢查需要真實 DLL；至少讓清單集中一處）。

---

## 7. 未解問題

1. **玩家實際建築配置未知**：沒有截圖/存檔，無法分辨 R1（沒有水塔）、R2（水塔沒接上管）、或 5.3 的「valve Fill 有在灌水」變體。
2. **玩家 TimberPipes 版本未知**：本機只有 11.4.0；若玩家已被更新到 11.5+，R3 機率上升。需要玩家 `Player.log` 與 mod 清單。
3. **DI 解析未驗證**：`_container.GetInstance(TimberPipes.Services.PipeRegistry)`（`TimberPipesWaterBinding.cs:193-226`）在真實遊戲容器中是否成功，從未驗證；若不成功，**所有玩家的自動補水都不會生效**（不只這位玩家）。這是 6.2-C 最優先要回答的問題。
4. **崩潰同源**：需崩潰 log/stack（另案）。
5. **「物理接管（valve Fill）」是否真的能把水送進農場**：需實機驗證；若可，則 HDBF 面板的 Pipe port 語義一定要改（否則永遠與玩家觀察矛盾）。
6. **是否要讓農場變成真端點**（blueprint 加 `BuildingPipeSpec`）＝設計決策；關係到「無前置依賴」承諾與 blueprint 版本相容，需 ADR。
7. **版本區間／TC-09**：adapter 沒有 TimberPipes 版本檢查（`compat-01-adapters.md:404-406`），本回報的「靜默 false」正是這個缺口的用戶可見後果。

---

## 8. 掃描範圍與可重跑指令（誠實揭露）

* **HDBF**：`rg -i "no connection|not connected"`（0 hit）；三語 CSV 全表；`git log -S'No connection'`（0 commit）。
* **TimberPipes 11.4.0**：`Localizations/enUS.csv` 全表；`TimberPipes.dll` 以 ASCII＋UTF-16LE 抽字串（0 hit）；Mono.Cecil 讀 metadata/IL（`PipeRegistry.tanks/get_Tanks`、`TankPipe`/`PipeTank`/`BuildingPipe` 的 `AddTemplateModule2Attribute`、`TankPipe.GetBuildingPipeSpec` IL、`PipeTank.get_VolumeM3/TakesGood` IL、`DefaultBuildingConnectionProvider.TryConnecting` IL）。
* **Workshop 全部 mod**：296 支 DLL（26.7 MB）ASCII＋UTF-16 字串掃描；所有 `*/Localizations/enUS.csv` 掃描（0 hit）。
* **原版**：497 支 `Timberborn*.dll` 字串掃描（0 hit）；`resources.assets`（646 MB）分塊 UTF-8 掃描（只有 `Unconnected`）；`StreamingAssets/Modding/Blueprints.zip`（1067 entries）掃 `WhitelistedGoodType: Liquid`（7 檔）。
* **未掃**：非 `enUS` 的第三方本地化 CSV（玩家以英文引述，風險低）；遊戲 Resources 的 UTF-16 字串（假設本地化以 UTF-8 文字資產存放；已掃到 `Unconnected` 可佐證）。

## 附錄 A：本回報關鍵檔案:行號索引

| 檔案 | 行號 | 內容 |
|---|---|---|
| `src/HDBF.UI/Panels/HdbfIndoorPanelFactory.Cards.Output.cs` | 109-110, 133-148 | `PortRow(snapshot.PipePort, snapshot.WaterSupply == Pipe, …)`；Connected/Not connected/Not applicable |
| `src/HDBF.UI/Panels/HdbfIndoorPanelFactory.Cards.Water.cs` | 62-69 | Water 卡「Water source: pipe/manual/none」 |
| `src/HDBF.UI/Panels/HdbfIndoorPanelWarnings.cs` | 44-49, 241-252 | 缺水警告句＋`Tooltip.NoWaterSource` |
| `src/HDBF.UI/Panels/HdbfIndoorPanelFormat.cs` | 123-131 | `SupplyKey` pipe/manual/none |
| `src/HDBF.Gameplay/Components/HdbfBoxFarmController.Context.cs` | 77-111, 245-297 | `ReadPipeWater`、`UpdatePorts`（PipePort 語義） |
| `src/HDBF.Gameplay/Components/HdbfBoxFarmController.Tick.cs` | 60, 74 | 每日先 UpdatePorts 再 ReadPipeWater |
| `src/HDBF.Gameplay/Components/HdbfBoxFarmController.IndoorSnapshot.cs` | 62, 73, 167-194 | `WaterSupply()` 判定與 snapshot |
| `src/HDBF.Gameplay/Components/HdbfBoxFarmController.ManualRefill.cs` | 141-172 | `RefreshWaterSupply`（pipe/manual/none） |
| `src/HDBF.Gameplay/Components/HdbfBoxFarmController.WaterSupply.cs` | 152-210 | `ApplyHauledWater`（任何來源的水 goods→水罐；valve Fill 的可能性來源） |
| `src/HDBF.Gameplay/Components/HdbfWaterInventoryInitializer.cs` | 23-26, 58-66 | 農場真有 `Inventory:Water`（公共輸入、可收 Water） |
| `src/HDBF.Gameplay/Specs/HdbfBoxFarmSpec.cs` | 87-89 | `HasPipePort` 預設 true（純 UI 旗標） |
| `data/Buildings/Food/BoxFarm/*.blueprint.json` | 187/191 | `"HasPipePort": true`；無 TimberPipes 元件 |
| `src/HDBF.Integration/Adapters/IndoorAdapterHost.cs` | 55-72, 74-103, 106-120 | health／PipeWaterSource／IsPipeConnected |
| `src/HDBF.Integration/Adapters/TimberPipesWaterSource.cs` | 63-91, 97-122, 229-259, 261-272 | IsAvailable／HasNetwork／tank 列舉與過濾／health 接受範圍 |
| `src/HDBF.Integration/Adapters/TimberPipesWaterBinding.cs` | 130-191, 193-226, 228-255 | TryBind／registry 解析／TryGetTanks |
| `src/HDBF.Integration/Adapters/TimberPipesWaterBinding.Tank.cs` | 17-45, 52-90 | tank 狀態過濾／ApplyVolume 抽水 |
| `src/HDBF.Core/Compat/CompatAnchorTable.cs` | 262-272 | 4 個 A 級 anchor（**不含**水 binding 成員） |
| `src/HDBF.Core/Indoor/IndoorDayTick.cs` | 79-91 | `hasPipeWater=false` 時整段管道補水跳過 |
| `src/HDBF.Core/Indoor/IndoorWaterMath.cs` | 221-236 | `IsAvailable` → `TryDraw` |
| `src/HDBF.Core/Indoor/IndoorFarmRecord.cs` | 106 | 新農場 `PipePort = true` 預設 |
| `data/Localizations/enUS.csv` / `zhTW.csv` / `zhCN.csv` | 78, 80-81, 40, 143 | Port.Pipe／Connected／NotConnected／WaterNone／Tooltip.NoWaterSource |
| `docs/REQ_V2_SECTION_19.md` | 149-151 | §19.6「連接時自動補水」需求原文 |
| `docs/VERIFICATION.md` | 1009-1012 | §22.6 自承「端口實際行為未驗證」 |
| `docs/workstreams/compat-01-adapters.md` | 384, 395-408 | TC-06 已定義未執行；CheckLog 只證明偵測 |
| `docs/API_FACTS.md` | 201, 230-237 | TimberPipes 11.4.0 身分／workshop 不自動載入／RequiredMods 不是載入 gate |
| `docs/in-game/pause-fix2-A-summary.txt` 等 | 8, 16 | 同狀態既有日誌：adapter active ＋ `water.pipe-unavailable` |
