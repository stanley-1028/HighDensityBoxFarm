# F-PIPE-20261107：Timber Pipes 水管接在高密度農場上，農場完全不接受管子供的水

> **狀態**：🟢 根因已修並已發佈（v2.5.8；Steam Workshop item 3828809784，ManifestID `8359214256551403205`，2026-11-07；GitHub `b900e8a`；離線門禁全綠；**實機驗證 pending**，不寫成已完成）
> **日期**：2026-11-07｜**模組**：v2.5.8（未發布）｜**遊戲**：Timberborn 1.1.2.8｜**Timber Pipes**：11.4.0（Workshop 3801726769）
> **玩家回饋（原文要點）**：「把 Timber Pipes 中的水管接在高密度農場的時候，我發現高密度農場們都不接受 Timber Pipes 的建築所供的水；我想它接受 Timber Pipes 的建築所供的水。」
> **離線驗證**：`docs/VERIFICATION.md` §44｜**變更記錄**：`docs/v2/CHANGELOG.md` [2.5.8]｜**歷史條目**：`docs/BUG_REPAIR_20261102.md` §3.6（RE-05）

---

## 1. 一句話結論

**不是玩家接錯，而是 HDBF 的「管道供水」其實有兩個互相獨立的缺口：**
1. **既有承諾的「接網水塔自動補水」從未生效**——判定用 `PipeTank.Pipe.Graph != null`，但 Timber Pipes 11.4.0 只對
   transport pipe 賦值 graph，液體水塔的 BuildingPipe 是非 transport 端點 ⇒ 該 Graph **恆為 null**；
2. **HDBF 沒有任何「從緊鄰農場的管取水」的程式**——普通 pipe 貼農場外殼在 Timber Pipes 端也不會建立連接
   （只有 valve Fill 會走 `BuildingPipeTarget`），所以玩家所見的「接管無效」是設計層面的真實缺口。

v2.5.8 兩者都修：水塔改看「port 連到 transport pipe」；新增「農場占格 → 鄰格 → 相鄰輸水管」的反射取水路徑。

---

## 2. 根因與證據（Timber Pipes 11.4.0 IL + HDBF 原始碼）

| # | 事實 | 證據 |
|---|---|---|
| R1 | `PipeTank` 掛在液體 Stockpile 建築上；其 BuildingPipe 因無 `TransportPipeSpec` ⇒ `IsTransportPipe=false`（`BuildingPipe.Awake` IL） | TimberPipes.dll IL：`BuildingPipe.Awake`、`get_IsTransportPipe` |
| R2 | `PipeRegistry.RebuildGraph(adding)` 只對 transport pipe 調 `AssignMergedGraph`；`FloodFillTransport` 對非 transport 直接回空集合 ⇒ **水塔 pipe 的 `Pipe.Graph` 恆 null** | IL：`PipeRegistry.RebuildGraph` / `FloodFillTransport` / 全部 `set_Graph` 呼叫點 |
| R3 | HDBF `TryDescribeFeedableTank` 以 `graph is not null` 過濾 ⇒ `HasNetwork`/`IsPipeConnected` 恆 false ⇒ 水塔路徑與面板「已連接」從未生效 | `src/HDBF.Integration/Adapters/TimberPipesWaterBinding.Tank.cs`、`TimberPipesWaterSource.cs`、`IndoorAdapterHost.cs` |
| R4 | HDBF 全 `src/` 沒有從農場鄰管取水的程式；農場藍圖也沒有 TimberPipes 元件（`HasPipePort` 只是 UI 旗標） | `docs/workstreams/feedback-20261102/pipes.md` §2.1–2.3；`data/Buildings/Food/BoxFarm/*.blueprint.json` |
| R5 | 普通 transport pipe 只與 BuildingPipe 互連；貼在農場外殼 = 兩邊都看不到。能灌建築庫存的只有 valve Fill（`ExtractionPipe`→`BuildingPipeTarget`） | TimberPipes IL：`ValvePipeIo.FacesBuilding`、`DefaultBuildingConnectionProvider.TryConnecting`、`ExtractionPipe.TryInlet` |

### 2.1 為什麼「農場鄰接管」是安全的物理取水

* `BuildingPipe.get_Coordinates()` ＝ `BlockObject.Coordinates`（世界原點）；`PipeRegistry` 的 pipes 字典就以它為鍵，
  所以「農場 6 面鄰格 → `PipeRegistry.TryGetPipe(cell)`」是 O(1) 且與 mod 自身一致（15 種管中 14 種為 1×1×1；
  唯一例外 OutfallPipe 1×2×1，見 §6 已知限制）。
* 可抽水條件：`IsFinished && IsTransportPipe && !IsContaminated && NetworkGoodId == "Water" && FluidHeight >= 0.2 m³`
  （`NetworkGoodId` 在污染時為 null；`FluidHeight` ∈ [0,1]，1 pipe 容量 = 1 m³）。
* 抽取只用公開 `RemoveFluid(float)`：IL 等價 `SetWaterHeight(FluidHeight - amount)`，`SetWaterHeight` = `Math.Clamp(h,0,1)`，
  **不做 0.2 量化、不改 good/污染、不會為負**。取水量上限＝當次請求量，並以「實際減少的 FluidHeight」記帳
  （1 HDBF 水單位 = 1 good = 0.2 m³），因此不會造水。
* Timber Pipes 的 fluid solver 每 tick 重新讀取每根 pipe 的 `FluidHeight` 與每個水塔的庫存體積再解算 ⇒ 抽走的是
  真實網路水；網路無水時管會乾，回退水塔/人工路徑。

---

## 3. v2.5.8 修法（檔案級）

### 3.1 水塔路徑（既有承諾恢復）

* `TimberPipesWaterBinding.Tank.cs`：`hasTransportLink = graph is not null || PipeHasTransportLink(pipe)`；
  `PipeHasTransportLink` = 列舉 `BuildingPipe.Ports`，任一 port `IsConnected` 且 `PipeRegistry.TryGetConnectedBuilding`
  對端 `IsTransportPipe`（與 Timber Pipes 自身 `PipeGraphFlowCache.Rebuild` 的節點判定一致）。
  單看 `Ports.Any(IsConnected)` 不可靠：兩個相鄰液體水塔的端口會互連（`ConnectPorts` 沒有 transport 過濾）。
* 新 partial：`TimberPipesWaterBinding.TankConnection.cs`（port 走查）。

### 3.2 農場鄰接管取水（玩家主訴求）

| 層 | 檔案 | 內容 |
|---|---|---|
| Core | `src/HDBF.Core/Indoor/IIndoorPipeWaterAnchor.cs` | 新可選 companion：`SetWaterAnchor(IReadOnlyList<PipeCell>?)`；與 frozen 的 `IIndoorAdapterHost`/`IWaterSource` 分離；`PipeCell` 純值型別 |
| Core | `src/HDBF.Core/Indoor/PipeIntakeMath.cs` | 純函數 `NeighborCells`：9 格 3×3×1 占格 → **30** 個 6 面鄰格（去重、排序、空輸入安全） |
| Gameplay | `src/HDBF.Gameplay/Components/HdbfBoxFarmController.PipeIntake.cs` | 用 `BlockObject.PositionedBlocks.GetOccupiedCoordinates()`（世界座標，旋轉/翻轉已套用）取占格，按 block origin 快取；在 `ReadPipeWater` 的 `IsPipeConnected` 之前、面板 `PublishProbeAnchor` 之前發布 |
| Integration | `TimberPipesWaterBinding.PipeAnchors.cs` | 一次性解析新反射錨點（`TryGetPipe`/`TryGetConnectedBuilding`/`get_IsTransportPipe`/`get_IsFinished`/`get_IsContaminated`/`get_NetworkGoodId`/`get_FluidHeight`/`RemoveFluid`/`get_Ports`/`get_IsConnected`）；缺任何一個只關閉新路徑，不破壞水塔路徑 |
| Integration | `TimberPipesWaterBinding.Intake.cs` | 只讀 census（`TryReadIntake`）＋有上限抽取（`TryDrawIntake`）：`takeM3 = min(剩餘請求, 管內量)`，`RemoveFluid(takeM3)`，按 before−after 記帳；先管後水塔；全程 try/catch |
| Integration | `TimberPipesWaterSource.Intake.cs` | 保存 Gameplay 發布的 footprint→鄰格快取（引用相等時不重算）；`HasIntakeConnection()` / `HasIntakeWater()` / `TryDrawIntake()` |
| Integration | `TimberPipesWaterSource.cs` / `.Probe.cs` | `HasNetwork`/`IsAvailable`/`TryDraw` 納入鄰接管；只讀 probe：鄰接管有水 → `Connected`；有管但乾 → `TankDry`；水塔供水仍 `GlobalSupply` |
| Integration | `IndoorAdapterHost.cs` | 實作 `IIndoorPipeWaterAnchor` 並轉發給 source（異常隔離，不抽水、不記失敗） |

**沒有**做的事（刻意）：不改藍圖（不把農場變成 PipeTank 端點）、不改存檔 schema、不加 feature flag、不加 Harmony、
不新增 `PipeProbeReason` 數值/本地化 key、不碰 `CompatAnchorTable` 的 4 個 A 級 anchor。

---

## 4. 面板 / 文案 / 本地化

* 三語 `HDBF.Tooltip.PipeWater` 改寫為：「自動補水只抽取真正的管道水：貼在農場外殼上的 Timber Pipes 水管，
  或接入管網的原版液體水塔（Small/Medium/Large）。管道必須輸送乾淨的水。」（enUS 保留既有測試所需的
  `Auto-refill draws only from` / `liquid tanks` / `farm shell` token）。
* 面板 Pipe network 行、Water source=pipe、缺水警告的既有語義不變（`Connected` 代表「可自動補水」）。

---

## 5. 測試

* 新增 `tests/HDBF.Tests/V2PipeIntakeContractTests.cs`（7 條）：
  Core 可選接口形狀（1 方法、`IReadOnlyList<PipeCell>`、非 host 子接口、Core 無遊戲字串）、9→30 鄰格/去重/排序/
  相鄰格案例、Gameplay 發布契約（`PositionedBlocks`/先發布後查詢/不逐幀/≤300 行/固定頭）、host 轉發不抽水、
  反射取水「有上限＋按實際減量＋登記真實錨點」、census 只讀、`PipeProbeReason` 0–8 凍結。
* `V2AdapterHostContractTests` +4 條：host 轉發、新錨點清單、水塔 transport-port 接網判定、端到端 wiring。
* 三語 tooltip 斷言更新；L1 總數 **717 → 728**，全綠。

---

## 6. 已知限制 / 未驗證

1. **實機未驗證（pending）**：本機無可互動遊戲會話；「水管貼農場 → 面板 Connected＋水罐上升」「接網水塔 → 自動抽水」
   都需實機確認。日誌檢查點：`compat.enabled`、`water.pipe-probe: reason=Connected/… detail=farm-pipe…`、
   `water.settle`（tank 上升）。
2. **OutfallPipe 盲區**：1×2×1 的 outfall 只有原點格可被 `TryGetPipe` 命中；管身第二格貼農場不會被識別（本版接受）。
3. **與 valve Fill 雙供給**：若玩家同時用 valve Fill 把水 goods 灌進農場庫存，又讓水管貼農場，兩條路會同時供水
   （水罐容量/headroom 兜底，不會造水）；屬可接受的行為邊界。
4. **相鄰管長期乾**：若農場旁管的網路本身沒有水源/水頭不足，抽不到水時回退水塔/人工路徑（屬播放狀態，不計 3-strike）。
5. `container.GetInstance(PipeRegistry)` 在真實遊戲容器是否成功仍未實機證明（失敗會由 probe 的 `BindingFailed` 顯示）。
