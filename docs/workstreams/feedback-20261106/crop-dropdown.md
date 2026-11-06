# 2026-11-06 玩家回饋維修：作物策略下拉「打不開」

## 0. 玩家回饋（Steam Workshop 留言）

> **Roslyn**（7 小時前）：「The drop down to change from rotation to a specific crop doesn't open
> for me. I'd like to not rely on a random crop rotation.」

需求解讀：面板「作物策略」列的原版 Dropdown 點不開，玩家無法從「自動輪作」切到單一固定作物。

## 1. 根因（不是下拉本身壞掉，是面板 4 Hz 刷新把它拆掉）

1. **面板幾乎每個模擬 tick 都判定 dirty。** `HdbfFarmPanelFragment.UpdateFragment` 以
   `RefreshIntervalSeconds = 0.25f`（4 Hz）刷新；dirty 檢查用的是
   `HdbfIndoorPanelFingerprint.Compute`，它混入了 `CurrentDay`（
   `DayNightCycle.PartialDayNumber` 的小數日，`Quantize ×1000`）與每種作物的
   `AverageProgress * 100`（`HdbfIndoorPanelFingerprint.cs` 的 `CurrentDay`／`Crops` 段；
   `HdbfBoxFarmController.Context.cs:36`）。獨立審計用安裝版遊戲資料量化：一天 768 ticks、
   1x 每 tick 0.6 秒，`1/768 × 1000 = 1.3` ⇒ **每個模擬 tick 指紋必變**，面板因此以
   「每 tick 撞一次 4 Hz 取樣」的頻率重繪（1x 約 1.7–2.5 次/秒，2x/3x 才逼近 4 Hz 上限）。
   2.5.6 的 L-11 才把 `CurrentDay` 放進 snapshot/指紋，時間點與 11/06 的回饋吻合。
2. **舊的 `RenderCards` 用 `_cards.Clear()` 重建整個卡片堆疊。** 常駐的 `_cropHost`
   （內含原版 `Timberborn.DropdownSystem.Dropdown`）位於作物卡的策略列，每次重繪都被
   detach／re-attach 一次。
3. **原版 Dropdown 一旦 detach 就關閉彈出清單。** 以 `dotnet ildasm` 反編譯安裝版
   `Timberborn.DropdownSystem.dll`：`Dropdown.Initialize` 對 `Selection` 按鈕註冊
   `DetachFromPanelEvent` callback（`<Initialize>b__25_0`），其內容就是
   `_dropdownListDrawer.HideDropdown()`；`DropdownListDrawer.HideDropdown` 會清空 items 並隱藏
   root。因此彈出清單最多存活到下一個 4 Hz 重繪。
4. **按下／放開之間的重建會吃掉 ClickEvent（次要機制、未 IL 取證；主因仍是第 3 點的
   HideDropdown）。** `Dropdown.Initialize` 把開關綁在
   `ToggleSelectionListDisplayStyle`（ClickEvent）；按鈕在 pointer down 時取得 pointer capture，
   而 detach 會釋放 capture，若按下與放開之間正好跨過一次重建，ClickEvent 就不成立——
   這可以解釋「偶發完全沒反應」，但即使此機制不成立，第 3 點已足以解釋「閃一下就消失」。
5. **z-order 已排除：** 彈出清單由 `DropdownListDrawer` 用
   `RootVisualElementProvider.Create("DropdownListDrawer", "Core/DropdownItems", 2)` 建立
   （sorting order 2），高於 `Underlay`(0)/`PanelStack`(1)，所以不是被實體面板蓋住。

## 2. 修法（根因：不要把還能用的卡片拆掉）

* 新增 `HdbfIndoorPanelSignature.ForSection(section, data)`：把「一張卡片實際會畫出來的欄位」
  全部混成 64-bit 內容簽名（卡片的 Tone/Title/IconKey、每列 Label/Value/Tooltip/IsWarning/
  Marker/Bar/BarText/BarMarker，以及工人卡額外的 meta 行、worker chips、模式、過渡句）。
* `HdbfFarmPanelFragment` 改成逐卡 diff（新 partial `HdbfFarmPanelFragment.Cards.cs`）：
  * 卡片與新 section 一一對應時，只重建「內容簽名真的變了」的卡片，並在同一個 index
    `RemoveAt` + `Insert`；**其餘卡片（含作物卡與其中的 Dropdown）保持掛在面板上不動**。
  * 首次渲染或 section 版面改變時才走舊的整堆重建（fallback）。
  * 下拉的選取文字／tooltip／items 仍由 `RenderCrop` 每次刷新同步，因此「即時值」不會變舊。
* 這同時修掉同類風險：模式 RadioToggle 也住在卡片裡，以前同樣每 0.25 秒被搬動一次。

## 3. 離線證據（本輪可執行）

* 獨立根因審計（另一位代理重跑 IL／遊戲資料，非 Lead 轉述）：
  `docs/workstreams/feedback-20261106/root-cause-audit.md`。同一結論，主鏈信心 0.90；
  它另補上「detach 事件會送到所有後代（Unity 2022.3 UIElements 文件）」與
  「Drawer sortingOrder 2 vs PanelStack 1（UIDocument.sortingOrder IL 直證）」；
  ClickEvent 因 detach 遺失標為次要機制（0.55，未 IL 取證），不影響主結論。
* 獨立 code review（APPROVE WITH NITS，無 blocking）：
  `docs/workstreams/feedback-20261106/review.md`；其 nit 已收進本輪（檔頭/可追溯性/簽名宣稱、
  vacuous 斷言、新增渲染欄位漏簽名的守門測試、正向重建案例）。
* `build.ps1` 全綠：5 組件 **0 warning / 0 error**、L1 **717/717 PASSED**、
  `tools/validate` **RESULT: OK**、**RELEASE GATES: PASSED (44 checks)**、staged 48 files。
* 新增測試：
  * `V2PanelContractTests.TestCardSignatureIsStableForIdenticalRenderedContent`
    （同一份 read model 兩次渲染 → 每張卡簽名相同，不會重建）。
  * `V2PanelContractTests.TestCropProgressRefreshDoesNotRebuildTheCropCard`
    （作物進度／小數日變動 → 作物卡簽名不變、Growing/Slots 卡簽名改變；
    另含「政策改變必須重建作物卡」的正向案例）。
  * `V2PanelContractTests.TestCardSignatureTracksEveryRenderedFieldOfACard`
    （手寫欄位子集：任一 rendered 欄位變動都必須改變簽名）。
  * `V2PanelContractTests.TestEveryFieldTheCardRenderersReadIsPartOfTheSignature`
    （掃 `BuildSectionCard`／`RenderWorkerRoster`／`HdbfPanelControls*` 原始碼中的
    `data.X/section.X/row.X/chip.X`，逐一要求在簽名檔出現 → 抓「未來新增渲染欄位卻漏簽名」）。
  * `V2PanelVisualContractTests.TestRefreshRebuildsOnlyChangedCardsAndKeepsTheDropdownAttached`
    （fragment 源碼契約：4 Hz 刷新不得整堆 `Clear()`、必須用簽名逐卡替換、只在 dropdown 已掛載時同步）。

## 4. 未完成／限制（不得寫成實機已驗證）

* 本機無法合成滑鼠／鍵盤輸入（`docs/VERIFICATION.md` §16.2），且本輪 DSH file policy 為
  `workspace-write`，**沒有部署到 `Mods\HDBF`、也沒有開遊戲點擊驗證**。因此
  「下拉可以打開、選固定作物後面板顯示固定作物」仍需要作者／玩家在可互動會話用本包實測一次。
* 若實測仍打不開，下一個懷疑點依序是：`HdbfFarmPanelFragment.BuildCropDropdown` 的
  `new Dropdown()`/`Initialize` 例外被 catch 成 `_cropDropdown = null`（此時整列只剩文字，沒有
  下拉），以及 `game-dropdown` 在該列寬高為 0／被其他元素覆蓋。這兩項在無輸入環境無法分辨。
