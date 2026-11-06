# 作物策略下拉「打不開」——獨立根因審計（唯讀）

* **審計者**：`root-cause-auditor`（獨立於 Lead 的重跑取證）
* **時間**：2026-11-06 13:0x–13:2x（Asia/Macau）
* **範圍**：只讀程式碼／IL／遊戲資料；**未改動 `src/`、`data/`、`tests/`、build 檔、`.git`**，未啟動遊戲。
* **方法**：讀 HEAD 版 mod 原始碼 → `dotnet ildasm` 反編譯安裝版遊戲組件 → 直接掃描遊戲 blueprint 資料 → Unity 官方文件補足 UIElements 事件語意（managed 組件無法 ildasm，見 §5）。
* **受審版本**：HEAD（2.5.6 + 之後的提交內容）。**注意**：本輪修法已於 13:02 起改寫工作區（新增 `HdbfFarmPanelFragment.Cards.cs`、`HdbfIndoorPanelSignature.cs`，`Render.cs` 改寫）；本報告對「壞掉的版本」引用 HEAD，並用 `git show HEAD:…`（唯讀）核對舊內容，未評測未定案的新修法（另見 §7 簡短觀察）。

---

## 0. 結論（一句）

**Lead 假說成立**：原版 `Timberborn.DropdownSystem.Dropdown` 本身沒有壞；是面板每 0.25 s 的 dirty 輪詢 + 指紋中的連續時鐘 `CurrentDay`（=`DayNightCycle.PartialDayNumber`）讓面板**幾乎每個模擬 tick** 就 `RenderCards` 全量重建卡片堆疊（`_cards.Clear()`），常駐的 crop `Dropdown` 因此被 detach；原版 `Dropdown.Initialize` 註冊的 `DetachFromPanelEvent` callback 呼叫 `DropdownListDrawer.HideDropdown()`，把剛打開的彈出清單清空並隱藏 → 玩家看到「打不開／閃一下就消失」。**z-order、items 空、input processor 立即關閉、pickingMode、版面 0 寬高等替代原因均已排除**；`ClickEvent` 因 detach 遺失屬「次要、未完全取證」的機制。

---

## 1. 證據鏈（逐環，全部可重跑）

### E1 面板以 4 Hz 輪詢，且 `UpdateFragment` 每幀都會被呼叫

* `src/HDBF.UI/Panels/HdbfFarmPanelFragment.cs:44`：`RefreshIntervalSeconds = 0.25f`。
* `:142-160` `UpdateFragment()`：`Time.unscaledTime` 節流，`_nextRefreshTime = now + 0.25`；之後呼叫 `Refresh(force:false)`。
* `:181-222` `Refresh()`：取 snapshot → `HdbfIndoorPanelFingerprint.Compute(...)`（`:205`）→ **只有指紋改變才** `Render(...)`（`:206-213`）。
* 遊戲側：`Timberborn.EntityPanelSystem.EntityPanel::UpdateSingleton()` → `UpdateFragments()` → 對每個 `IEntityPanelFragment` 呼叫 `UpdateFragment()`（IL 已核對；`AddFragments` 於 `EntityPanel::Load()` 時呼叫一次 `InitializeFragment()` 並常駐同一個 element，之後只重複 `UpdateFragment`）。

### E2 指紋含連續時鐘 `CurrentDay` ⇒ 幾乎每個模擬 tick 都 dirty（並可量化）

* `src/HDBF.UI/Panels/HdbfIndoorPanelFingerprint.cs:99`：`hash = hash * 31 + Quantize(snapshot.CurrentDay);`
  * `Quantize`（`:139-152`）＝ `(long)(value * 1000f)`。
  * `:124-133` 另混入 `crops[*].AverageProgress * 100`（較慢，非主因）。
* `src/HDBF.Gameplay/Components/HdbfBoxFarmController.Context.cs:32-45`：`ReadCurrentDay()` 回傳 `_dayNightCycle.PartialDayNumber`（`:36`）。
* `src/HDBF.Gameplay/Components/HdbfBoxFarmController.IndoorSnapshot.cs:44,100`：每次 snapshot 都填 `CurrentDay = currentDay`（live）。
* 安裝版 IL（`Timberborn.TimeSystem.dll`）：
  * `DayNightCycle::get_PartialDayNumber()` = `DayNumber + DayProgress`；
  * `DayNightCycle::get_DayProgress()` = `(float)_ticksPassedToday / (float)ConfiguredDayLengthInTicks`；
  * `DayNightCycle::Tick()` → `ProgressTimeBy(1)`，即**每個模擬 tick `_ticksPassedToday += 1`**。
* 安裝版遊戲資料（`Timberborn_Data/resources.assets` 內嵌 blueprint 字串，直接掃描取得）：
  * `DayNightCycle.blueprint` → `"ConfiguredDayLengthInTicks": 768`
  * `TickTime.blueprint` → `"TickIntervalInSeconds": 0.6`
* **量化推算**：每 tick Δ`PartialDayNumber` = 1/768 = **0.001302 日**；指紋以 ×1000 量化 ⇒ 每 tick 變化 **+1.3**，**每個 tick 都改變 64-bit 指紋**（`AverageProgress` 只是額外項，不需要它）。
  * 1x 速度：tick 約每 **0.6 s** 一次（`Ticker` 以 `Time.deltaTime` 累積後 `TickBuckets`；速度倍率會加速）。
  * 因此面板重繪率 ≈「每個 tick 最多撞到一次 4 Hz poll」：**1x ≈ 1.7–2.5 次/秒；2x/3x 時逼近 4 Hz 上限**（與 Lead 文件「幾乎每 0.25 秒」在 2x/3x 成立，在 1x 則是約每 0.6–0.9 s 一次——結論不變，見 §4）。
  * 「為何現在才壞」：`docs/v2/CHANGELOG.md:62-66`（2.5.6，2026-11-03）的 L-11 才把 `CurrentDay` 加進 snapshot；`HdbfIndoorPanelFingerprint.cs` 於 11/03 修改、含此項。2.5.6 之前面板在待機農場上基本上不 dirty → 下拉可正常使用。這與回饋日期（11/06）吻合（中高信心推測，未 diff 2.5.4/2.5.5 的 fingerprint）。

### E3 壞掉的 `RenderCards` 每次都 `Clear()` 整疊卡片，並把 `_cropHost` 搬到新卡片

* HEAD `src/HDBF.UI/Panels/HdbfFarmPanelFragment.Render.cs:56`：`_cards.Clear();`（整個卡片堆疊 detach）。
* HEAD `:107-112`：`if (cropPolicyRow is not null && _cropHost is not null) { cropPolicyRow.Add(_cropHost); RenderCrop(data); }` → 常駐的 `_cropHost`（內含唯一一個 `Dropdown` 實例）被 re-parent 到**新建的**作物卡。
* `src/HDBF.UI/Panels/HdbfFarmPanelFragment.Build.cs:52-55,76-79`：`_cards` 是 `ScrollView` 內容，`_scroll` 是 `_root` 子樹；`_cropHost` 在 `:54` 只建立一次（`BuildCropDropdown`）。
* `src/HDBF.UI/Panels/HdbfFarmPanelFragment.Crop.cs:27-53`：`_cropHost`/`Dropdown` 只建一次；`:39-40` `new Dropdown(); dropdown.Initialize(_dropdownListDrawer);`；`:49-50` 註冊 provider 與 `Showed`；`:53` `_cropHost.Add(dropdown)`。
* 同一輪 `RenderCards` 內順序是「先 `_cards.Clear()`（detach 舊卡，連帶 detach `_selection`）→ 建新卡 → `cropPolicyRow.Add(_cropHost)`（attach）」，所以事件是**成對**的，但 detach 那一次就會觸發 Hide（E4）。

### E4 原版 `Dropdown.Initialize` 的 detach callback 內容＝呼叫 `HideDropdown()`

`Timberborn.DropdownSystem.dll` IL（`dotnet ildasm`，方法名為 ildasm 輸出原名）：

* `Dropdown::Initialize(DropdownListDrawer)`：
  * `_selectedItem = this.Q("SelectedItemContent")`、`_selection = this.Q<Button>("Selection")`；
  * `this._selection.RegisterCallback<DetachFromPanelEvent>(<Initialize>b__25_0, TrickleDown.NoTrickleDown)`；
  * `_buttonsOnlySelection == false`（本 mod 未設定，預設 false）→ 走 `:0xBF` 分支：隱藏 `ArrowLeft/ArrowRight`，並把 `ToggleSelectionListDisplayStyle` 註冊到 `_selection` 的 `ClickEvent`。
  * **重點**：detach callback 掛在 `_selection`（Dropdown 的**後代** Button），不是 Dropdown root。
* `Dropdown::<Initialize>b__25_0(DetachFromPanelEvent)`：**唯一動作** `this._dropdownListDrawer.HideDropdown();`

### E5 `HideDropdown()` 到底做什麼（彈出層清空＋隱藏）

`DropdownListDrawer::HideDropdown()` IL：

1. `if (!this.DropdownVisible) return;`（`get_DropdownVisible()` = `_root.IsDisplayed()`，見 IL）
2. `_inputService.RemoveInputProcessor(this)`
3. `this._items.Clear()` ← **彈出清單項目全部移除**
4. `VisualElementExtensions.ToggleDisplayStyle(this._root, false)` ← **彈出層 display=None**
5. 反註冊 callback（見附錄 B 的 quirk）
6. `_isMouseOver = 0; _parent = null;`
7. `_eventBus.Post(new DropdownHiddenEvent())`

配套 IL：
* `Dropdown::ToggleSelectionListDisplayStyle(ClickEvent)`：`if (DropdownVisible) { Hide(); return; }` 否則 `AddItemsIfNeeded()` + `ShowDropdown(_selection, _elements)` + 觸發 `Showed`。
* `DropdownListDrawer::ShowDropdown(parent, items)`：先 `HideDropdown()`（防重入）、`_parent = parent`、`AddInputProcessor(this)`、在 `_parent`/`_items` 註冊 MouseEnter/Leave、`_isMouseOver = 1`、`_root` 顯示、把 items 加進 `_items`、`CalculateDimensions()`。

### E6 `DetachFromPanelEvent` 會送到所有後代（鏈條的關鍵一步，非 IL 可得）

`_selection` 是「被移除的卡片」的後代，不是被移除的 element 本身。Unity 2022.3 官方手冊（Timberborn 1.1.2.8 同世代）明言：

> 面板事件僅在發生面板更改時發送到階層結構中直接受影響的視覺元素**及其後代**。……如果您從同一 UXML 階層結構中分離 `parent`，**所有視覺元素都會收到 `DetachFromPanel` 事件**。

來源：<https://docs.unity.cn/cn/tuanjiemanual/1.9/Manual/UIE-Panel-Events.html>（2022.3 手冊鏡像，Unity 官方）。
⇒ `_cards.Clear()` 移除舊卡時，卡片子樹（含 Dropdown 的 `_selection`）全部收到 `DetachFromPanelEvent` ⇒ E4 callback ⇒ E5 `HideDropdown()`。

### E7 z-order：彈出層在 EntityPanel **之上**（不是被蓋住）

* `Timberborn.DropdownSystem.dll` `DropdownListDrawer::Load()`：
  `_rootVisualElementProvider.Create("DropdownListDrawer", "Core/DropdownItems", 2)` ← **sorting order 2**。
* `Timberborn.CoreUI.dll` `PanelStack::Initialize(string,string)`：
  `_rootVisualElementProvider.CreateEmpty("PanelStack", 1)` ← **sorting order 1**。
* `Timberborn.EntityPanelSystem.dll` `EntityPanel::Load()`：
  `_visualElementLoader.LoadVisualElement("Common/EntityPanel/EntityPanel")` + `_uiLayout.AddAbsoluteItem(_root)`；`UILayout::Load()` 用 `_panelStack.Initialize(...)` 取得同一個 PanelStack 文件。
* `Timberborn.RootProviders.dll` `RootVisualElementProvider::CreateUIDocument(GameObject,int32,string)`：`UIDocument.sortingOrder = (float)sortOrder`（明確把 int 寫進 UIDocument）。
⇒ 2 > 1，彈出層不可能被 EntityPanel 遮住。**z-order 排除**。

### E8 `ClickEvent` 遺失（次要機制，未定級）

* Unity `ClickEvent` 文件：「A click consists of a mouse down event followed by a mouse up event on the same VisualElement.」<https://docs.unity.cn/cn/tuanjiemanual/1.9/ScriptReference/UIElements.ClickEvent.html>
* 若「按下→放開」之間剛好跨過一次 `_cards.Clear()`，`_selection` 已被移出面板（pointer capture 隨之釋放），mouse-up 不再落在同一個 element ⇒ 該次點擊**完全沒有 ClickEvent**，玩家看到「點了沒反應」。
* 本機**無法**用 IL 取證 UIElements 的 pointer capture 實作（見 §5），故標為「機制上合理、未直接觀測」。但**即使此機制不成立，E1–E6 已足以解釋症狀**（彈出層開了也會在下一次重繪被 Hide 掉）。

---

## 2. 被排除的替代原因（逐一）

| # | 替代原因 | 判定 | 依據 |
|---|---|---|---|
| a | 彈出層被 EntityPanel 蓋住（z-order） | **排除** | E7：UIDocument sortingOrder 2 vs 1；`CreateUIDocument` 明確寫入。 |
| b | `AddItemsIfNeeded` 的 items 為空 / provider 為 null（點擊時 NRE） | **排除** | `HdbfIndoorPanelWarnings.Crops.cs:57-105` `BuildCropChoices` **一定**包含第 0 項「auto rotation」（id="")，`Count ≥ 1`；`HdbfFarmPanelFragment.Crop.cs:70-75` 首次 `RenderCrop` 的簽名由空字串變成非空 ⇒ `SetItems` 一定被呼叫 ⇒ `_dropdownProvider` 非 null。 |
| c | `BuildCropDropdown` 初始化例外被 catch 成 `_cropDropdown = null` | **保留（低機率）** | `Crop.cs:35-59` 的 `catch (Exception)` **靜默**（無 log）→ 若 `new Dropdown()`／`Initialize` 拋例外，整列只剩靜態文字、沒有下拉。與玩家描述「下拉」不符，但離線無法 100% 排除；建議在 catch 加 log（見 §7）。 |
| d | `_cropHost` 沒有被掛進面板 | **排除** | HEAD `Render.cs:90-100` 作物 section 的第 1 個非 warning 列一定是 policy row（`HdbfIndoorPanelFactory.Cards.Output.cs:47-56`；warning rows 由 `Cards.cs:186-202` 插在更前面，loop 會 `continue` 跳過），`:107-112` 必把 `_cropHost` 掛上。 |
| e | `pickingMode` 讓點擊穿透 | **排除** | `pickingMode = Ignore` 只設在裝飾性 marker/strip/icon（`HdbfPanelControls.cs:198,223,228,282`、`Widgets.cs:126`）；`_cropHost`/`Dropdown`/卡片祖先皆為預設 `Position`。 |
| f | 版面寬/高為 0、`game-dropdown` 被壓扁 | **排除（離線不可量測，但無跡象）** | `Crop.cs:29-44`：`_cropHost` flexGrow/flexShrink=1、`Dropdown` flexGrow/flexShrink=1，位於 `CreateRow` 的 row（icon+label+value，`HdbfPanelControls.cs:147-165`），policy row 的 value label 在有 dropdown 時被隱藏；彈出層 `CalculateDimensions()` 用 `_parent.resolvedStyle.width/height`，若宿主可見即為正常值。無任何 ancestry 的 `display:none`/`overflow:hidden` 會裁掉 dropdown 本體（彈出層在獨立 UIDocument，不受 ScrollView 裁剪）。 |
| g | popup 被 input processor 在開啟同一幀關掉 | **排除** | `DropdownListDrawer::ShowDropdown` 先把 `_isMouseOver = 1`；`ProcessInput()` 只在 `Cancel`、或 `(_MainMouseButtonDown \|\| ScrollWheelActive) && _isMouseOver == 0` 時才 `HideDropdown()`。開啟瞬間 `MainMouseButtonDown` 成立但 `_isMouseOver == 1` → 不關。 |
| h | EntityPanel 生命週期（ShowFragment 重入/換建築） | **排除為主因** | fragment 是 singleton、root 只建一次；`ShowFragment` 只在選取時呼叫（`EntityPanel::ShowFragments`）。它是「放大器」而不是原因：4 Hz 重繪在整個面板顯示期間都發生。 |
| i | `_cards.Clear()` 也搬動了 RadioToggle | **附帶確認（latent，非本次回報）** | HEAD `Render.cs:102-105,139-149`：`_modeHost` 同樣每輪被搬進新工人卡；它沒有彈出層，症狀只會是「偶發點擊被吞」。修法同時解掉此風險。 |
| j | 原版 `ShowDropdown`/`HideDropdown` callback 反註冊錯對象 | **附註（方向相反）** | 見附錄 B：這是 vanilla quirk，會讓彈出層「關不掉／item 點擊異常」，不是「打不開」。 |

---

## 3. 與 Lead 假說的差異（不迎合的地方）

1. **「幾乎每 0.25 秒重繪」需修正**：dirty 的真正來源是**模擬 tick**（`_ticksPassedToday` 每 tick +1，一天 768 ticks，tick 間隔 0.6 s @1x），面板只是 4 Hz **取樣**。1x 時實際重繪約 **1.7–2.5 次/秒（每 0.6–0.9 s 一次）**；2x/3x 才會逼近 4 Hz。**結論不受影響**（一次 detach 就足以關掉彈出層），但文件/變更記錄若寫「每 0.25 秒都重建」是高估；精確說法是「每個模擬 tick 都可能重建，最多受 4 Hz 上限約束」。
2. **鏈條缺一環要補**：Lead 文件寫「callback 掛在 `Selection`」，但沒寫**為什麼**後代會收到祖先的 detach 事件。E6 用 Unity 官方手冊補上；這是「UIElements managed 組件在本機不可 ildasm」下唯一能給的證據層級（見 §5）。
3. **ClickEvent 遺失是次要、未取證**：Lead 把它列為根因之一；我判定它只能解釋「部分點擊完全沒反應」，**主要機制是彈出層被打開後被 `HideDropdown()` 關掉**。即使 pointer capture 機制不成立，症狀仍成立。
4. **「為何現在才壞」有更強解釋**：2.5.6（11/03）的 L-11 才把 `CurrentDay` 放進 snapshot/fingerprint（`docs/v2/CHANGELOG.md:62-66`）；在那之前，待機農場的面板指紋基本穩定 → 下拉可用。這與 11/06 的回饋時間吻合（中高信心，未 diff 舊版 fingerprint）。
5. 沒有發現能推翻假說的證據；唯一真正的「反例情境」是**遊戲暫停時**（tick 不跑、fingerprint 凍結 → 不重建 → 下拉應正常），可作為可證偽預測（§6）。

---

## 4. 信心程度

| 項目 | 信心 | 說明 |
|---|---|---|
| 主鏈（4 Hz poll + `CurrentDay` dirty + `_cards.Clear()` + detach + `HideDropdown` 清空/隱藏） | **0.90（高）** | 每一環都有 mod 原始碼行號或安裝版 IL 方法；dirty 頻率用遊戲 blueprint 數據算出。 |
| z-order 排除 | **0.95** | UIDocument.sortingOrder 2 > 1，IL 直證。 |
| items 空 / input processor / pickingMode / 未掛載 | **0.90** | 程式碼路徑與 IL 直證。 |
| 按下/放開跨重建吞掉 ClickEvent | **0.55（未定）** | UIElements managed 組件不可 ildasm；以 Unity 文件推論。不影響主結論。 |
| `CurrentDay` 是 2.5.6 新增的「回歸觸發點」 | **0.70** | changelog 與檔案時間吻合；未取得 2.5.5 版本 diff。 |

**殘餘不確定（無法離線排除）**：
* `BuildCropDropdown` 的**靜默 catch**：若該玩家的安裝有 UXML/USS 缺失，`_cropDropdown` 會是 null（整列沒有控制項）。這與「下拉打不開」的措辭不符，但需要實機 log 才能完全排除。
* `game-dropdown` 的實際 USS 在 asset bundle 內，無法離線檢查（寬高/picking）。
* 本機**完全無法**合成滑鼠/鍵盤輸入（`docs/VERIFICATION.md` §16.2），因此無法實機點擊驗證。

---

## 5. 為什麼 E6/E8 只有文件級證據

* 嘗試 ildasm `C:\Program Files (x86)\Steam\steamapps\common\Timberborn\Timberborn_Data\Managed\UnityEngine.UIElementsModule.dll` → **ildasm 崩潰（exit `-1073740791` / `0xC0000409`，只輸出 assembly ref externs）**，無法取得 `VisualElement.DetachFromPanel`/`Clickable` 的 IL。
* 該組件是 Unity 引擎的 managed facade，實際實作在原生層；因此改用 **Unity 2022.3 官方手冊**（E6、E8 引用）作為事件語意來源，並以原版 Dropdown 的設計意圖交叉印證（callback 特地掛在後代 `_selection` 上，只有在「後代會收到祖先 detach」的前提下才有意義）。

---

## 6. 可驗證預測（給可互動會話；本機不可合成輸入）

1. **暫停測試（最強、可證偽）**：暫停遊戲 → 打開面板 → 點 crop 下拉。若假說正確，選單應可正常打開並保持；恢復運行後 1x 下選單最多存活約 0.6–0.9 s 就被關閉（3x 更短）。
2. 1x 下連點下拉：約每 4 次有 1 次「完全沒反應」（按下/放開跨 tick+重建），其餘會「閃一下」。
3. 修法後：1x/3x 下拉都應可維持；只有在作物卡內容真的變了（例如新增作物 warning 列）時才可能閃一次（見 §7）。
4. 若「暫停時也打不開」⇒ 假說不完整，應優先查 §2(c) 的靜默 catch 與 `game-dropdown` 的 USS/寬高（此時應能在 `Player.log` 看到 `ui.render` 例外或什麼都沒有）。

---

## 7. 對工作區進行中修法的獨立觀察（非本任務範圍，僅提醒）

* 方向正確：只替換「內容簽名改變」的卡片、其餘保留掛載，能讓作物卡（含 Dropdown）不再每 tick 被搬動（新 `HdbfFarmPanelFragment.Cards.cs`、`HdbfIndoorPanelSignature.cs`）。
* 但仍要知道的點：
  1. 作物卡簽名包含 warning rows 與 rotation/fixed 文字；**作物 warning 出現/消失時該卡仍會重建** ⇒ dropdown 仍會被重掛一次（可接受，屬罕見事件）。
  2. `RenderCrop` 在每次重繪仍呼叫 `_dropdownProvider.Load()` 與 `UpdateSelectedValue()`（HEAD 舊 `Crop.cs:69-79`）——不影響開啟，但每 4 Hz 重建一次選取 Label；若想更穩可考慮「彈出層開啟中跳過 `UpdateSelectedValue`」（`DropdownListDrawer.DropdownVisible` 可讀）。
  3. `BuildCropDropdown` 的 catch 沒有 log（`Crop.cs:55-59`）；若日後再回報「打不開」，沒有這行 log 會很難分辨是 UXML 缺失還是 detach。

---

## 8. 重現指令（唯讀，於 repo 根目錄執行）

```powershell
# 受審版本的卡片重建程式碼（HEAD）
git show HEAD:src/HDBF.UI/Panels/HdbfFarmPanelFragment.Render.cs | Select-Object -Skip 48 -First 70

# 原版 DropdownSystem IL（此沙箱不可寫 %TEMP%，故直接讀 stdout）
$dll='C:\Program Files (x86)\Steam\steamapps\common\Timberborn\Timberborn_Data\Managed\Timberborn.DropdownSystem.dll'
$il = (dotnet ildasm $dll 2>&1 | Out-String) -split "`r?`n"
# Dropdown::Initialize / <Initialize>b__25_0 / ToggleSelectionListDisplayStyle / HideDropdown / ShowDropdown / Load / ProcessInput

# z-order：Drawer=2、PanelStack=1
dotnet ildasm 'C:\Program Files (x86)\Steam\steamapps\common\Timberborn\Timberborn_Data\Managed\Timberborn.RootProviders.dll'
dotnet ildasm 'C:\Program Files (x86)\Steam\steamapps\common\Timberborn\Timberborn_Data\Managed\Timberborn.CoreUI.dll'   # PanelStack::Initialize

# 遊戲資料：DayNightCycle.blueprint(768 ticks/day)、TickTime.blueprint(0.6 s/tick)
# 以 Latin1 byte scan 掃 Timberborn_Data\resources.assets 的 "ConfiguredDayLengthInTicks"/"TickIntervalInSeconds"
```

---

## 附錄 A：關鍵 IL 摘錄（安裝版 1.1.2.8）

**A1 `Dropdown::Initialize(DropdownListDrawer)`**（節錄）

```
_selection = this.Q<Button>("Selection");
this._selection.RegisterCallback<DetachFromPanelEvent>(<Initialize>b__25_0, NoTrickleDown);
...
// _buttonsOnlySelection == false 分支：
this._selection.RegisterCallback<ClickEvent>(ToggleSelectionListDisplayStyle, NoTrickleDown);
```

**A2 `Dropdown::<Initialize>b__25_0(DetachFromPanelEvent)`**

```
DropdownListDrawer.HideDropdown();
```

**A3 `DropdownListDrawer::HideDropdown()`**（節錄）

```
if (!this.DropdownVisible) return;          // get_DropdownVisible() = _root.IsDisplayed()
_inputService.RemoveInputProcessor(this);
this._items.Clear();
VisualElementExtensions.ToggleDisplayStyle(this._root, false);
// 反註冊 callback（對象不匹配，見附錄 B）
this._isMouseOver = 0;
this._parent = null;
this._eventBus.Post(new DropdownHiddenEvent());
```

**A4 `DropdownListDrawer::Load()`**

```
_root = _rootVisualElementProvider.Create("DropdownListDrawer", "Core/DropdownItems", 2);
_items = _root.Q<ScrollView>("DropdownItems");
_scrollBarInitializationService.InitializeVisualElement(_items);
ToggleDisplayStyle(_root, false);
```

**A5 `DropdownListDrawer::ProcessInput()`**（節錄）

```
if (_ignoreWorldInput && !_inputService.MouseOverUI) return false;
if (_inputService.Cancel) { HideDropdown(); return true; }
if (_inputService.MainMouseButtonDown || _inputService.ScrollWheelActive) {
    if (_isMouseOver == 0) { HideDropdown(); return true; }
}
return false;
```

**A6 `PanelStack::Initialize(string,string)`**

```
var document = _rootVisualElementProvider.CreateEmpty("PanelStack", 1); // UIDocument.sortingOrder = 1
...
return document.rootVisualElement;
```

**A7 `RootVisualElementProvider::CreateUIDocument(GameObject,int32,string)`**

```
document.sortingOrder = (float)sortOrder;   // => DropdownListDrawer = 2, PanelStack = 1
```

**A8 `DayNightCycle` (Timberborn.TimeSystem.dll)**

```
Tick()                     { ProgressTimeBy(1); }
ProgressTimeBy(int d)      { _ticksPassedToday += d; if (_ticksPassedToday >= ConfiguredDayLengthInTicks) {...} ... }
get_DayProgress()          { return (float)_ticksPassedToday / (float)ConfiguredDayLengthInTicks; }
get_PartialDayNumber()     { return (float)DayNumber + DayProgress; }
```

遊戲資料：`DayNightCycle.blueprint` `ConfiguredDayLengthInTicks = 768`；`TickTime.blueprint` `TickIntervalInSeconds = 0.6`。

---

## 附錄 B：原版 Dropdown 的次要 quirk（與本症狀方向相反，僅記錄）

`ShowDropdown` 在 `_parent`（`_selection`）與 `_items` 上註冊的是 `<ShowDropdown>b__15_0..3`；
`HideDropdown` 反註冊的卻是 `<HideDropdown>b__16_0..3`（不同 delegate，`UnregisterCallback` 不會匹配）⇒ **每次開啟都會累積 2 個 MouseEnter + 2 個 MouseLeave callback**，`_isMouseOver` 的計數會隨開啟次數漂移（可能變成負數）。
後果方向是「彈出層變得更不容易被外部點擊關閉 / item 點擊判定異常」，**不是**「打不開」。本報告不把它列為根因，但如果玩家後續回報「選單關不掉」或「選了作物沒反應」，這是第一個要看的 vanilla 行為。
