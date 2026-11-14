# 2026-11-14 玩家回饋維修：非暫停時作物策略下拉「彈窗很快消失」

* **狀態**：實作完成；離線門禁與實機 A/B 結果見 §4；發佈記錄見 §5（以本輪實際輸出為準）。
* **日期**：2026-11-14（Asia/Macau）
* **適用版本**：模組 2.6.1 / 遊戲 Timberborn 1.1.2.8-ffc2de0-sw（Unity 6000.5.11f1）/ 存檔 schema 2 / 配置 schema 2；修復對象 = 已發布的 2.6.0。
* **觸發回饋**：Steam Workshop 留言（litos5599，2026-11-14，約 5 小時前）：「i cant change the crop production, the crop selector
  closes all the time…」；中文原意：**暫停時能選固定作物；遊戲運行時下拉彈窗很快就消失，選不了**。
* **變更摘要**：面板 4 Hz 刷新在「彈窗打開的 layout 盲區幀」仍會觸碰打開中的原版下拉（重設 items → `ClearItems →
  HideDropdown`，或作物卡重建 → 選取按鈕 detach → `HideDropdown`）。2.6.1 改用原版下拉**同步**的
  `Showed` / `DropdownHiddenEvent` 作為精確開關狀態，彈窗開啟期間整體延後刷新與 items/value 同步，關閉後第一幀補上；
  另在切換選中實體時主動關閉彈窗。**只動 `src/HDBF.UI` + UI 契約測試**；資料/藍圖/數值/存檔/配置/Harmony 不變。
* **方法**：安裝版 `Timberborn.DropdownSystem.dll` / `Timberborn.CoreUI.dll` 用 `tools/ApiIndex il`（Mono.Cecil）反編譯取證；
  另由獨立代理 `root-cause-auditor` 重跑取證（非 Lead 轉述），報告見
  [root-cause-audit.md](root-cause-audit.md)；發布預檢見 [release-preflight.md](release-preflight.md)。

---

## 0. 玩家回饋與症狀邊界

| 場景 | 2.6.0 行為 | 2.6.1 期望 |
|---|---|---|
| 遊戲暫停，點作物策略下拉 | 彈窗可打開、可選固定作物 | 不變 |
| 遊戲運行，點作物策略下拉 | 彈窗「很快消失」，選不了 | 彈窗保持打開，可選固定作物 |
| 遊戲運行，切到固定作物後面板刷新 | 選取文字可能被重建 | 數值照常刷新，下拉不再被觸碰 |

對稱性（暫停正常 / 運行異常）正是根因的指紋：暫停時模擬時間不推進，面板指紋不變 → 4 Hz 刷新體不執行；運行時
`CurrentDay`/作物進度讓指紋每個模擬 tick 都變 → 每个 0.25 s 採樣都可能撞進「彈窗已開、layout 尚未解析」的盲區。

## 1. 考察：原版下拉的開/關信號（IL 直證）

證據：安裝版 `Timberborn.DropdownSystem.dll`、`Timberborn.CoreUI.dll`（1.1.2.8）完整 IL 存於
`%USERPROFILE%\.hdbf\il-dropdown.txt` / `il-coreui.txt`；獨立審計的 IL 證據在 `%USERPROFILE%\.hdbf\audit\cap-*.txt`。

1. **打開是同步的**：`Dropdown.ToggleSelectionListDisplayStyle(ClickEvent)` 在
   `DropdownListDrawer.ShowDropdown(...)` 返回後**同步** invoke `Dropdown.Showed`
   （IL_0031–0047：`ldsfld EventHandler Dropdown::Showed` → `Invoke(EventArgs.Empty)`）。
2. **可讀的打開狀態是 layout 滯後的**：`DropdownListDrawer.DropdownVisible` → `CoreUI.VisualElementExtensions.IsDisplayed(_root)`
   → `resolvedStyle.display == DisplayStyle.Flex`（`IsDisplayed` IL_0001–000e；`ToggleDisplayStyle` 只寫 inline
   `style.display`）。⇒ 彈窗打開當幀（可能不止一幀）`DropdownVisible` 仍回報「關閉」。
3. **關窗路徑只有兩類**（皆 IL 已證）：
   * `DropdownListDrawer.HideDropdown()`：清空 items、藏 root、`EventBus.Post(DropdownHiddenEvent)`；**前置條件**
     `DropdownVisible == true` 才執行（否則直接 return，且**不發事件**）。
   * 由誰呼叫：`Dropdown.ClearItems()`（→ `DropdownItemsSetter.SetItems` 每次重設 items 都走這裡）、
     `Dropdown.SetAndUpdate()`（選取/值變更後）、`Dropdown.Hide()`，以及 **Selection 按鈕的
     `DetachFromPanelEvent` 回調**（`Dropdown.Initialize` 註冊，<Initialize>b__25_0 → `HideDropdown`）。
   * `DropdownListDrawer.ProcessInput` 另有輸入型關窗：Cancel，或（主滑鼠鍵/滾輪 && `_isMouseOver == 0`）。
4. **2.6.0 發布二進位與 HEAD 源碼一致**（審計用 `hdbf-tools/workshop-backup/3828809784-2.6.0/HDBF.UI.dll`
   反編譯核對）：`UpdateFragment` 只有讀 `DropdownVisible` 的守衛、`RenderCrop` 無開啟守衛、
   `UpdateChangedCards` 無作物卡守衛 ⇒ **2.6.0 沒有引入新關窗路徑，是 2.5.7「下拉打不開」修復不完整的延續**。

## 2. 根因

**主根因（結構判定 0.90，IL 已證）**：2.6.0 的刷新守衛只讀 layout 滯後的 `DropdownListDrawer.DropdownVisible`，
彈窗打開當幀必為「未開」；運行時指紋每 tick 必變，於是 4 Hz 刷新體在盲區內照常執行，而刷新體含兩條原版關窗路徑：
`RenderCrop → DropdownItemsSetter.SetItems → Dropdown.ClearItems → HideDropdown`，以及作物卡重建 →
`_selection` detach → `<Initialize>b__25_0 → HideDropdown`。暫停時指紋凍結、刷新不執行，故正常。

**「每次必關」的主要性（0.65，需實機定級）**：逐卡內容簽名 diff 後，穩態刷新通常不再重建作物卡，因此每次點開都
「恰好撞進盲區幀」取決於 `UpdateFragment` 與 UIElements 事件派發的 native 幀內先後，離線不可判定；實機 A/B 見 §4。
另有備選假設（z-order、items 為空、舊版本、滑鼠計數漂移等）與排除理由見
[root-cause-audit.md §4.2](root-cause-audit.md)。**不採信的機制不寫進發布說明**：早期草稿曾推測
`UpdateSelectedValue` 重建子元素會觸發 detach 關窗，審計指出 managed `DetachFromPanelEvent` propagation=0、
被移除者不在 `_selection` 子樹內（置信度 0.20），故發布文案只寫「刷新期間不再觸碰打開中的下拉」。

## 3. 修法（根因：刷新不得觸碰打開中的下拉）

改動集中在 `src/HDBF.UI/Panels/`（+ 1 條 UI 契約測試）：

1. **精確開關狀態**（`HdbfFarmPanelFragment.Crop.cs`）：`dropdown.Showed += OnCropDropdownShowed` 置
   `_cropPopupOpen = true`（同步）；抽屜發布的 `DropdownHiddenEvent`（`[OnEvent]`，EventBus 註冊）清 false。
   兩者都不依賴 layout。
2. **刷新守衛**（`HdbfFarmPanelFragment.cs` `IsCropDropdownOpen()`）：**先**看精確旗標；旗標為真時以抽屜的
   `DropdownVisible` 作次級確認，若抽屜持續回報關閉超過 `CropPopupStaleSeconds = 1 s`（**時間制**，低幀率下
   不會變成數秒）則信任抽屜、清旗標——保證任何路徑都不會把面板永久凍住。旗標為假時仍以抽屜 visible 作
   兜底（其他面板擁有的彈窗）。
3. **週期刷新整體延後**（`HdbfFarmPanelFragment.cs` `UpdateFragment`）：彈窗打開期間 4 Hz 刷新直接 return
   （不重算、不重建、不 reflow），關閉後下一幀立即補上。
4. **items/value 同步延後**（`Crop.cs` `RenderCrop`）：打開期間整段 return（`SetItems` 會 `ClearItems → HideDropdown`，
   值同步也沒有必要），關閉後第一次刷新套用最新清單/選取/tooltip；這樣連命令觸發的 `Refresh(force: true)`
   （選取回調仍在原版 click 調用棧內）也不會碰到打開中的控件。
5. **作物卡重建守衛**（`HdbfFarmPanelFragment.Cards.cs` `UpdateChangedCards`）：打開期間跳過作物卡重建（保險層），
   其他卡片照常 diff 刷新。
6. **切換實體收尾**（`HdbfFarmPanelFragment.cs` `ShowFragment`/`ClearFragment` → `CloseCropPopup()`）：
   切換選中建築或隱藏片段時主動 `Dropdown.Hide()` 並清旗標，避免抽屜獨立 UIDocument 留下孤兒彈窗。
7. **契約測試**（`tests/HDBF.Tests/V2PanelVisualContractTests.cs`
   `TestCropPopupGuardUsesTheExactShowHideSignal`）：鎖定 Showed/Hidden 精確信號、守衛順序、items/value 延後、
   卡片重建守衛，防止未來重構把盲區守衛改回去。

設計取捨：彈窗打開時整塊面板數值暫停刷新（彈窗生命週期通常 < 5 s，玩家正在與面板互動，凍結不可感知），
換取「任何刷新路徑都不可能關掉玩家正在用的彈窗」的硬保證；關閉後立即恢復 4 Hz。無 Harmony、無反射、
無新增依賴、無存檔/配置變更。

## 4. 驗證

**離線門禁**（固定 work root `%USERPROFILE%\.hdbf\work`，2026-11-14 22:30 最終乾淨包）：
5 程序集 **0 warning / 0 error**；L1 **PASSED 734 / FAILED 0**；`tools/validate` **RESULT: OK**；
**RELEASE GATES: PASSED (44 checks)**；staged **48 files / 2,209,062 bytes / manifest 2.6.1**；
發布包 byte-scan：harness 字串（`HdbfUiVerifyDriver`/`VerifyClickPopupItem`）**0 命中**；
`git diff v2.6.0 -- src/HDBF.Gameplay src/HDBF.Core src/HDBF.Persistence src/HDBF.Integration` 為空。
新增契約測試 `TestCropPopupGuardUsesTheExactShowHideSignal`。staged DLL SHA-256 見 VERIFICATION §54.3。

**實機 A/B**（Timberborn 1.1.2.8，HDBFTest 存檔副本，2560×1440，臨時 harness 於發布前刪除；
`docs/in-game/bugfix261-Player.log`，`Load time: 132609ms`）：

| 場景 | 彈窗打開 | 打開期間面板側變更 | 關閉後 | 選取命令 |
|---|---|---|---|---|
| 暫停（修復側） | `showed`，vis=1/flag=1 | **guardskip 0→8；valuesync/renders/croprebuil/setitems 全不變（0 次）** | — | `applied=Potato` ✓ |
| 運行（修復側） | `showed`，vis=1/flag=1 | **guardskip 8→14；valuesync 3→3、renders 4→4、croprebuil 1→1（0 次）** | 4 Hz 恢復（4 s 內 valuesync 3→10） | `applied=Carrot` ✓ |
| 運行（跳過守衛＝2.6.0 行為） | `showed`，vis=1/flag=1 | **打開窗口內即執行一次刷新：valuesync 13→14、renders 14→15** | 4 Hz 繼續（14→20） | `applied=Potato` ✓ |

本機限制（誠實記錄）：本機第三方 mod **Steam Engine Expansion**（Workshop item `3836039641`）的面板刷新
會呼叫 `Dropdown.Hide()`；原版 `Hide()` 無條件關閉共用 drawer（不檢查是否屬於自己），managed stack
`BoilerPanelFragment::Refresh → BoilerPanelView::SetVisible → Dropdown.Hide →
DropdownListDrawer.HideDropdown` 已取證，故本機任何下拉彈窗都會在 0.04–0.22 s 內被它關掉。
這是環境第三方行為、不是 HDBF 路徑。⇒ 本輪可證「修復側在彈窗打開期間 0 次面板側變更、刷新被凍結，
下拉可開、可選、命令生效」；「保持打開到玩家選完」被第三方 mod 干擾，**標 pending 等玩家回饋**，不寫成已驗證。

**證據檔案**：`docs/in-game/bugfix261-Player.log`（A/B harness log）、
`bugfix261-paused-popup.png` / `bugfix261-running-guarded-popup.png` / `bugfix261-running-legacy-popup.png`
（harness 截圖；彈窗已被上述第三方 mod 關閉，僅作面板狀態證據）、
`bugfix261-final-panel.png` + `bugfix261-final-round-Player.log`（最終 2.6.1 發布包實機 smoke，見 §54.5）。

## 5. 發佈記錄（2.6.1）

* **GitHub**：`stanley-1028/HighDensityBoxFarm`，修復 commit 與 `v2.6.1` tag 見 VERIFICATION §54.6
  （upload 後補本節：最終 master SHA、tag SHA、push 回報）。
* **Steam Workshop**：item `3828809784`（AppID 1062090），ManifestID、上傳日誌、頁面核對
  （File Size / Updated / Change Notes 數）見 VERIFICATION §54.6；變更說明
  [docs/workshop/changenote-2.6.1.txt](../..//workshop/changenote-2.6.1.txt)（EN/zhCN/zhTW），
  只更新內容 + Change Note，標題/描述/封面/標籤/可見性刻意未動。

## 6. 限制與未完成

* 本機 DSH 會話**無法合成 OS 滑鼠/鍵盤輸入**（`docs/VERIFICATION.md` §16.2 已記錄），實機 A/B 透過原版下拉自己的
  私有回調方法驅動（等價於玩家點擊後的下游邏輯，只跳過硬體指標），**不是**「玩家滑鼠實測」；發布記錄不得寫成
  「實機點擊驗證通過」。
* 玩家原話中的「彈窗很快消失」若其實由未取證的滑鼠計數路徑（`ProcessInput`：主滑鼠鍵 && `_isMouseOver == 0`）造成，
  本版同樣消除：打開期間不再有任何 4 Hz 觸碰（值重建/卡片重建/items 重設），該路徑失去輸入源。此路徑無法在無輸入
  環境重現，誠實標 pending 給下一輪/玩家回饋驗證。
* 多實例/並行會話共用同一台機器時，實機驗證必須串行（本輪有另一會話同時使用 Timberborn；已用「等遊戲空檔 +
  finally 還原 2.6.0」處理，見 §4）。
