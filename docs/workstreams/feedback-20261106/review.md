# 獨立 Code Review：作物策略下拉「打不開」逐卡簽名修法

- **審查者**：code-reviewer（獨立審查，唯讀；未修改任何被審程式）
- **固定點**：`HEAD 758968c` vs **工作區未提交變更**（`git status --short`：M `docs/VERIFICATION.md`、M `docs/v2/CHANGELOG.md`、M `HdbfFarmPanelFragment.Render.cs`、M `V2PanelContractTests.cs`、M `V2PanelVisualContractTests.cs`；新增 `HdbfFarmPanelFragment.Cards.cs`、`HdbfIndoorPanelSignature.cs`、`docs/workstreams/feedback-20261106/`）
- **方法**：`git diff` ＋ 直接讀新檔全文 ＋ 以 `dotnet ildasm` **獨立**反編譯安裝版 `Timberborn.DropdownSystem.dll`、`Timberborn.CoreUI.dll` ＋ 靜態計數交叉核對測試數。**未跑** `build.ps1`、未部署、未開遊戲、未動 `.git`。只寫本檔。
- **時效註記**：`docs/VERIFICATION.md`、`docs/v2/CHANGELOG.md`、`crop-dropdown.md` 在 13:14 由 Lead 更新；本報告引用的是 **13:19 前後**的內容（行號以該版本為準）。審查期間程式碼檔（13:02–13:05 寫入）未再變動。
- **Spec 來源**：玩家回饋（Steam Workshop / Roslyn，見 `crop-dropdown.md:3-8`）＋ task-2 描述；無 issue tracker。

---

## 0. Verdict

**APPROVE WITH NITS（不建議 blocking）。**

- 根因**成立**：我自己用 IL 複核了「detach → `HideDropdown()`」這條鏈，與 Lead 文件一致（見 §2）。修法與根因一一對應，方向正確。
- **簽名覆蓋面經逐欄位對照是完整的**（`HdbfPanelRow` 10 個屬性、`HdbfPanelSection` 5 個屬性、Workers 卡 6 個 extras、`HdbfWorkerChip` 3 個屬性全部有混入；無「欄位漏簽名 ⇒ 卡片停舊值」的模型層漏洞）。
- **索引/順序正確**：先建新卡再 `RemoveAt`+`Insert`，常駐 host（dropdown／radio）不會遺失、不會雙掛；8 張卡固定順序，首次渲染後永遠走 in-place，fallback 實質只在第一輪。
- **沒有發現會讓本回報 bug 修不好的方向性錯誤**；也沒有發現新的功能缺陷。原本我懷疑「模式 radio 會因不重建而停在舊值」——查 IL 後**排除**：vanilla `RadioToggle` 的點擊 callback 只發事件、不畫選取，選取只由 `Update(int)` 畫，而 `data.Mode` 在簽名內（見 §3.3）。
- 剩下的都是 Low 級 nit（文件一致性、測試有效性、可維護性），以及**只能實機確認**的部分（§7）。文件已誠實標 pending，沒有把離線證據寫成實機已驗證。
- **發佈前提**：可以合併／進實機，但對外文案不得寫「已修好」——需一次可互動實機點擊確認（開下拉 → 選固定作物 → 顯示＋持久化）。

---

## 1. 這個修法為什麼應該真的能修好玩家回報（正向證據鏈）

1. 面板 dirty 頻率：`HdbfIndoorPanelFingerprint.Compute` 混入 `Quantize(CurrentDay)`（`HdbfIndoorPanelFingerprint.cs:99`）與作物 `AverageProgress`（同檔 `:124-133`），所以每個模擬 tick 指紋都變；1x 下實際重繪約 1.7–2.5 次/秒（`crop-dropdown.md:12-20`，與 §3 的量化一致）。舊 `RenderCards` 每次重繪都 `_cards.Clear()`（`git diff` 舊 body）⇒ 常駐 `_cropHost` 被 detach。
2. Dropdown 會因此關清單（**獨立 IL 複核**）：`Dropdown.Initialize` 對名為 `Selection` 的 Button 註冊
   `DetachFromPanelEvent` → `<Initialize>b__25_0` → `DropdownListDrawer.HideDropdown()`；`HideDropdown` 會
   `_items.Clear()`、`ToggleDisplayStyle(_root,false)`、移除 InputProcessor、post `DropdownHiddenEvent`。
   這就是「點了閃一下就沒了」。彈出層 root 也確如文件所述：
   `RootVisualElementProvider.Create("DropdownListDrawer","Core/DropdownItems", 2)`。
3. 新路徑下作物卡**內容是靜態的**，所以不會被重建：
   - 策略列 value = `CropPolicyText`（rotation 時是固定的「auto rotation」字串，`HdbfIndoorPanelFactory.cs:160-169`）；
   - 第二列 = `FormatRotationSequence(snapshot.RotationSequence)`，而 `RotationSequence()` 是
     faction 作物表的快取字串（`HdbfBoxFarmController.IndoorSnapshot.Crops.cs:81-134`，rotation 時
     `_rotationSequence` 建立一次就回傳，不隨輪作進度變）；Fixed 時回 `FixedCropId`。
   - 作物警告只有「Fixed 且 crop id 不在活表」一種（`HdbfIndoorPanelWarnings.cs:165-176`），一般不變。
   ⇒ 在玩家開著下拉的幾秒內，簽名不變 ⇒ 卡片不重建 ⇒ `_selection` 留在面板上 ⇒ 清單不會被 Hide。
4. 每輪的 `RenderCrop` 同步**不會**觸發那個 detach callback：IL 顯示 `Dropdown.UpdateSelectedValue(string)`
   只做 `_selectedItem.Clear()` ＋ 重建選取項內容（`_selectedItem` 是 `Selection` 的子容器），
   而 detach callback 註冊在 `Selection` 本身，所以每 tick 的 `UpdateSelectedValue()`／`PaintDropdownText()`
   不會關掉已開的清單。（`RenderCrop` 由 `Cards.cs:62-67` 每輪在 dropdown 已掛載時呼叫。）

結論：**修法直接對應根因，且我找不到會讓它失效的殘餘高頻 detach 路徑。**

---

## 2. task-2 六個重點逐項回答

### 2.1 簽名覆蓋面：完整（逐欄位對照）

`BuildSectionCard`（`Cards.cs:156-212`）＋ `RenderWorkerRoster`（`Render.cs:49-81`）真正讀的每個值 vs
`HdbfIndoorPanelSignature.ForSection`（`HdbfIndoorPanelSignature.cs:35-104`）：

| 渲染來源（file:line） | 讀到的欄位 | 簽名是否覆蓋 |
|---|---|---|
| `CreateCard(section.Tone)`（Cards.cs:158） | `HdbfPanelSection.Tone` | ✅ Signature.cs:48 |
| `CreateTitleRow(Title, IconKey)`（Cards.cs:166） | `Title`、`IconKey` | ✅ :46-47 |
| `CreateMetaRow(data.MetaLine, data.MetaTooltip)`（Cards.cs:163，僅 Workers） | 兩個 data 欄位 | ✅ :79-80 |
| 逐 row 的 `row.IsWarning` 分支（Cards.cs:169-178） | `IsWarning`、`Marker`、`Label`、`IconKey`、`Tooltip` | ✅ :61-66 |
| `CreateRow`（`HdbfPanelControls.cs:147-165`） | `IconKey`、`IsWarning`、`Marker`、`Label`、`Value`、`Tooltip` | ✅ 同上 |
| `CreateBar`（`HdbfPanelControls.cs:174-203`） | `HasBar`、`Bar`、`BarText`、`BarMarker`、`critical = Tone=="red"` | ✅ :65-70（critical 由 Tone 間接覆蓋） |
| `CreateWarningRow`（`HdbfPanelControls.cs:213-236`） | `IconKey`、`Marker`、`Label`、`Tooltip` | ✅ 同上 |
| `RenderWorkerRoster` chips（Render.cs:54-59；`Widgets.cs:69-117`） | `Name`、`Vacant`（決定頭像）、`Tooltip` | ✅ :96-98 |
| `RenderWorkerRoster` note／mode／transition（Render.cs:64-80） | `WorkerRosterNote`、`Mode`、`ModeTransitionText` | ✅ :81-83 |
| 行數／順序 | `Rows.Count` ＋ 逐 index | ✅ :51-71 |
| 卡片種類特例 | `section.Kind` | ✅ :45 |

**結論：模型層零漏洞。** `HdbfPanelRow` 的 10 個屬性、`HdbfPanelSection` 的 5 個、Workers 的 6 個 extras、
`HdbfWorkerChip` 的 3 個，全部有混入；且**每個屬性都真的有被渲染**，所以沒有「簽名多含不影響重繪的欄位」
（多含只會多重建，不會錯）。唯一沒進簽名的渲染輸入是靜態服務與 dropdown 的 out-of-band 狀態，見下。

**唯一真正的「X 變了但簽名沒含它」是作物卡的 dropdown 狀態**，它靠 `RenderCrop` 每輪同步兜住，不靠簽名：
`data.CropChoices`、`data.SelectedCropId`、`data.CropTooltip`（`Crop.cs:62-88`）。具體反例：

- 若 `RenderCrop` 某天被改成「只在卡片重建時才同步」，或 `_cropHost.parent` 守門失準（見 §6 N5），玩家在
  crop 清單改變（例如載入作物 mod、切語言）後，下拉會**停在舊清單／舊選取**；此時
  `HdbfCropDropdownProvider.SetValue` 用 `_items.IndexOf(value)` 找不到字串就**不會發 Selected**
  （`Crop.cs:235-246`）⇒ 玩家選了沒反應。目前這個洞被每輪 `RenderCrop` 蓋住，但它是「簽名相等 ⇏ 整張卡完全
  相同」的唯一缺口，文件必須說清楚（§6 N3）。
- 反過來，「若 `data.Mode` 漏進簽名」會怎樣：vanilla `RadioToggle` 的點擊 callback（IL
  `<Create>b__0`）只 `RadioButtonSelected.Invoke`，**不自己畫選取**；選取只由 `RadioToggle.Update(int)`
  以 `EnableInClassList("selected", …)` 畫。而 `Update` 只在 Workers 卡重建時被呼叫（`Render.cs:70`）。
  ⇒ 若 `Mode` 不在簽名，模式切換成功後 radio 會一直停在舊模式，直到別的原因讓 Workers 卡重建。目前有涵蓋 ✅。
- 「若 `data.MetaLine` 漏」同理：換 tier/faction 而其他 extras 相同時，工人卡第一行會停在舊 subtitle。目前有涵蓋 ✅。

### 2.2 索引／順序：正確，無錯位／殘留／雙掛

逐案例檢查（`Cards.cs:33-145`）：

- **首次渲染**：`_cardViews.Count(0) != sections.Count(8)` ⇒ `CanUpdateCardsInPlace` false ⇒ `RebuildCards`
  （`cards.Clear()` 後逐張 `Add`，並記錄 3 條 cache）✅。
- **多張卡同時變更**：`UpdateChangedCards` 以 index 迴圈，每張在**自己的 index** `RemoveAt`+`Insert`
  （`Cards.cs:111-116`）；索引不會位移，`_cardViews/_cardSignatures/_cardKinds` 同步更新 ✅。
- **卡片數量／kind 改變**：`_cardViews.Count != sections.Count`（:77）、`_cardKinds[index] != Kind`（:84）、
  `!ReferenceEquals(cards[index], _cardViews[index])`（:89）任一成立就整堆重建 ✅。實務上
  `HdbfIndoorPanelFactory.Create` 永遠回固定 8 張、固定順序（`HdbfIndoorPanelFactory.cs:106-121`），
  所以 **fallback 實質只在第一輪**；「版面改變」是保險絲。
- **`_cards` 子元素不符**：`cards.childCount != sections.Count`（:77）已擋；連「`InitializeFragment` 被呼叫
  第二次、`_cards` 換成新空容器但 `_cardViews` 還滿」這個情境也會被 childCount 擋下走重建 ✅。
- **常駐 host 搬遷順序（最容易出錯處）**：`UpdateChangedCards` 是「**先** `BuildSectionCard()`（內含
  `cropPolicyRow.Add(_cropHost)`／`card.Add(_modeHost)`，即先把 host 從舊卡 reparent 到新卡）**再**
  `RemoveAt(index)`＋`Insert`」。順序正確：
  - 舊卡被移除時 host 已不在裡面 ⇒ 不會連帶丟掉 host；
  - `VisualElement.Add` 是 reparent ⇒ 不可能雙掛；
  - `_cropHost`/`_modeHost`/`_modeTransition` 各自只屬於一張卡，互不竊取。
  `RebuildCards`（:127）則是先 `cards.Clear()` 再逐張建卡（host 在 `cards.Clear()` 後仍寄在舊卡的 row 內，
  建新卡時被 reparent 回來）✅。
- **換選另一棟農場／`ShowFragment`／`ClearFragment`／`Hide()`**：`ShowFragment`（`HdbfFarmPanelFragment.cs:127-140`）
  不重置 `_cardViews`，但**卡片元素本來就還在 `_cards` 裡**（`ClearFragment`/`Hide` 只 `display:none`），
  所以 diff 基準仍然有效；`_cropSignature` 被清空 ⇒ 換場時 `SetItems` 會重跑 ✅。
  兩棟農場若有內容完全相同的卡，保留舊元素＝視覺相同，無殘留問題（卡片內沒有 per-entity 隱藏狀態）。
- **`_cropHost`/`_modeHost` 被搬到新卡後舊卡移除**：如上，安全 ✅。
- **殘留／漏渲**：未發現。未變更卡片不重建＝內容簽名相同＝渲染結果相同（除了 §2.1 的 dropdown out-of-band
  缺口，由 `RenderCrop` 每輪補）。
- **`_cropHost` 守門偏弱**：`_cropHost.parent is not null`（Cards.cs:64）在「host 仍寄在**已 detach 的舊卡**
  的 row 內」時也為 true。今天唯一能到這種狀態的是 `sections.Count == 0` 早退路徑（Cards.cs:42-51），而
  factory 永遠回 8 張卡 ⇒ **目前不可達**，列為 N5 的防禦性 nit。

### 2.3 行為時序：hover/tooltip/scroll/radio/dropdown 都不會變舊

- **hover／tooltip**：未重建卡保留原元素與原 `_tooltips.Register` 綁定；內容變了才會重建並重新註冊。
  tooltip 文字本身在簽名內（`row.Tooltip`、`data.MetaTooltip`、chips.Tooltip）⇒ 文字變一定重建 ✅。
- **scroll 高度**：`UpdateScrollHeight`（Render.cs:157-206）未變、量的是 `_actionCard`（本來就只建一次），
  與卡片 diff 無關；`_resetScrollOnNextRender` 行為未變 ✅。
- **模式 radio**：如上，`Mode` 在簽名內、`Update` 是唯一會畫選取的呼叫、且 `Update` 冪等；不重建的輪次
  radio 保持上一輪由 snapshot 算出的正確狀態 ✅。
  （先前懷疑「命令被拒時 radio 不會回彈」是回歸——**查 IL 後排除**：點擊本身不畫選取，所以不存在「畫錯再回彈」
  的情境；被拒時 radio 從頭到尾沒動，正好與權威值一致，且 action line 會顯示失敗原因。這點值得寫進 review 記錄。）
- **dropdown 選取文字／items／tooltip**：`RenderCrop` 在 `RenderCards` 結尾每輪呼叫（Cards.cs:62-67），
  `HdbfIndoorPanelSignature` 不參與；`ScheduleCropValueSync`（`Crop.cs:188-196`）時序仍成立：
  `OnCropSelected`（`Commands.cs:69-98`）在 vanilla `SetValue` 內被喚起 → 先 `Refresh(force:true)` 同步一輪
  → 之後 vanilla 在 `SetAndUpdate` 內 `UpdateSelectedValue(newValue)` → `HideDropdown` → 我們 deferred 的
  `UpdateSelectedValue()` 再補一次。新流程只把「作物卡同步重建」提前到同一 render 內（因為
  `CropPolicyText` 變了），detach/attach 發生在**選取已套用之後**，不影響該次點擊 ✅。
- **殘留風險（無法離線排除）**：若清單開著時作物卡內容真的變了（活作物表重建／語言切換／出現
  invalid-crop 警告），仍會 detach 關清單。今天觸發條件少（策略列與輪作列在 steady state 不變），可接受；
  建議實機時順手驗「開著清單按 Esc 外的地方／切語言」不至於秒關。

### 2.4 效能／REQ-V2-UI-01

- **簽名成本**：8 張卡 × 每卡 1–7 rows × ~10 個字串 hash ≈ 數百次 `string.GetHashCode()` ＋算術；無 boxing、
  無 list、無字串配置（`Text()` 只呼叫 `GetHashCode`；`Quantize` 是純算術）。相對每輪 factory 已經在建
  localized 字串與 DTO（既有成本）可忽略，遠低於 ARCHITECTURE 的「UI 刷新 ≤4 Hz、≤0.10 ms」預算。
- **卡片重建量**：從「每輪 8 張」降到「每輪約 1–2 張」。以 1.7–2.5 次/秒 × 8 vs × 1–2 估算，
  detach/attach 與 element 配置大幅下降（Water 卡的 tank/coverage bar 會因量化變動幾乎每輪重建；
  Growing 卡要 percent 跨整數才變）。**radio／dropdown 所在卡都不會因值刷新而重建**，這正是修法目的。
- **`GetHashCode` 對 string 的風險**：安全。`_cardSignatures` 只存在記憶體、不進存檔，.NET 的
  randomized string hash（每 process 一組種子）不影響正確性；跨 process 也不需要一致。
  碰撞機率：任一字串以 32-bit hash 混入，單欄位變更造成整體碰撞約 2^-32，後果是「跳過一次重建」。
  **文件說法不精確**：「the next panel fingerprint change repairs it」（Signature.cs:21-23）——fingerprint
  變更本身不會重建卡片，必須再發生一次**非碰撞的內容變更**；若農場之後完全靜止，理論上會一直停在舊值。
  實務影響可忽略，但文件該改（N3）。
- **本 diff 沒引入、但仍是「每輪配置」的來源（pre-existing）**：`RenderCrop` 每輪都跑
  `CropSignature` → `new StringBuilder(...)` ＋ `.ToString()`（`Crop.cs:70,198-212`）；以及 vanilla
  `UpdateSelectedValue()` 每輪重建 selection 元素。以前也一樣每輪跑，所以不是回歸；但若要落實
  「4 Hz 無配置抖動」，這是下一個該收的地方（N4）。
- 記憶體：`_cardViews` 只留 8 個元素；被替換的舊卡若 tooltip registrar 持有強參考可能延遲釋放
  ——這是既有模式（舊版每輪丟 8 張卡，現在更少），非本 diff 引入，且無法離線確認 registrar 實作。

### 2.5 測試有效性

新測試 4 條都存在、都是 `public static void Test*()` 且會被 `TestRunner` 反射發現
（`TestRunner.cs:169-197` 只收 static class 的 public static void Test*）。全庫
`public static void Test*` 靜態計數 = **716**，與文件「716/716、+4」相符（本輪 diff 正好 +4）。

強弱評估（不是全部等價）：

1. `TestCardSignatureIsStableForIdenticalRenderedContent`（`V2PanelContractTests.cs:718`）
   **偏弱**：第一個斷言是**同一個表達式比對自己**（同 section/同 data 呼叫兩次），任何純函式都過；
   只抓得到「用 identity/RNG 當 hash」。第二個斷言（兩份獨立建出的 model）才有內容意義（不是參考比對）。
2. `TestCropProgressRefreshDoesNotRebuildTheCropCard`（:743）**最有價值**：釘住「作物進度／小數日變動
   ⇒ 作物卡簽名不變、Growing/Slots 卡簽名變」的核心不變量。限制：它只證明簽名，不證明 renderer 真的
   因此不重建（renderer 只能靠源碼契約，見 4）。
3. `TestCardSignatureTracksEveryRenderedFieldOfACard`（:782）**名不符實**：宣稱
   「BuildSectionCard 會畫的每個欄位變動都必須改變簽名」，實際是**手寫固定子集**。它抓不到
   「未來在 BuildSectionCard 新增一個渲染欄位但忘了加進簽名」——那正是它宣稱要防的回歸。
   也漏了 `WorkerChip.Name`/`Tooltip`、`Rows.Count` 變動、`Rows` 為 null 等變體。
4. `TestRefreshRebuildsOnlyChangedCardsAndKeepsTheDropdownAttached`（`V2PanelVisualContractTests.cs:97`）
   是源碼字串契約（本 repo 慣例）：能釘住「RenderCards 只有 fallback 會 `cards.Clear()`、用簽名逐卡替換、
   只在 dropdown 掛載時同步」。**但其中 `"row.Bar"` 這條是 vacuous**：`"row.BarText"`/`"row.BarMarker"`
   已含 `"row.Bar"` 子字串，就算刪掉 `Quantize(row.Bar)` 仍會通過（行為測試 3 有補到 bar fill，但契約
   測試本身的意圖沒達成）。另外它把 `_cropHost.parent is not null` 這個**實作細節**釘死，若依 N5 改成
   `.panel` 需要同步改測試（可接受，但要記得）。

**缺少的關鍵回歸測試**：

- 完全沒有測到「要重建哪些 index / 何時 fallback」的決策邏輯（Cards.cs:75-117）。UIElements 無法在 L1 跑，
  但這段是**純決策**，可抽成純函式（例如 `HdbfCardDiff.ChangedIndices(oldKinds, newKinds, oldSigs, newSigs)`
  回傳 index 清單／是否需要整堆重建），就能 L1 覆蓋「多卡同時變、kind 改變、count 改變、順序重排」。
  （N13）
- 缺「作物卡**應該**重建」的正向案例：目前沒有測試斷言「policy 從 rotation→fixed（或 fixed→rotation）時
  作物卡簽名必須改變」。只有「不該重建」的測試，未來有人把簽名改成只看部分欄位而過度抑制重建時，
  測試仍會全綠。（N14）
- 測不到真正 bug 的端到端（無遊戲環境）；目前用「簽名不變量＋源碼契約」拼出來是合理折衷，但
  `crop-dropdown.md:63-64` 把 3 號測試說成「每個欄位」，是 overclaim（N11）。

### 2.6 文件與程式碼一致性

- **根因敘述與 IL 一致** ✅：`DetachFromPanelEvent`→`<Initialize>b__25_0`→`HideDropdown`、
  sorting order = 2、`Selection` 註冊……我逐項在安裝版 DLL 看到（方法名／巢狀 lambda 名都對得上）。
  `VERIFICATION.md:1908-1916`、`CHANGELOG.md:15-23`、`crop-dropdown.md:10-35` 的技術描述可信。
- **「不再整堆 Clear」是否只剩 fallback** ✅：`RenderCards` 只剩 `sections.Count==0` 早退的
  `cards.Clear()`（`Cards.cs:42-51`），真正的 fallback 在 `RebuildCards`（:124-138）。源碼契約測試
  `CountOccurrences(renderCards,"cards.Clear()")==1` 與程式相符。文件「首次渲染/版面改變才整堆重建」
  與程式一致（實務上版面永不改變，見 §2.2）。
- **716/716、44 gates 等數字**：
  - 716：靜態計數相符（716 條 `public static void Test*`；diff +4；上一版 712 ⇒ 712+4=716 自洽）✅
  - 「PASSED」「RELEASE GATES: PASSED (44 checks)」「5 組件 0 warning/0 error」「staged 48 files / 2,143 KB」：
    **本輪禁止跑 `build.ps1`，無法複核**（`build.ps1` 內 `Assert-ReleaseGate` 靜態呼叫點 35 處，含
    每語言迴圈後 44 是合理值，但這只是旁證，不是證據）。文件沒有誇大成實機，這點 OK。
- **文件自身的時間點**：`CHANGELOG`/`VERIFICATION` 都明確寫「未發佈／未實機」，沒有把 pending 寫成 done ✅。
- 不一致處（皆 Low，見 §6）：新 partial 沒更新兩份**被審程式檔頭**的 Purpose（N1、N2）；
  `TRACEABILITY.md:85` 的 REQ-V2-UI-01 實作欄沒收 `HdbfIndoorPanelSignature.cs`（N15）；
  `crop-dropdown.md` §1.4 與 §3 對 ClickEvent 機制的語氣不一致（N16）；
  `HdbfIndoorPanelSignature.cs:21-23` 的兩句宣稱過強（N3）。

---

## 3. Standards 軸（repo 標準 ＋ smell baseline）

**記錄在案的 repo 標準**（`docs/REQ_V2_SECTION_19.md` §19.18.1）：

| 標準 | 結果 |
|---|---|
| 新檔檔頭模板（Layer/Owner/Purpose/Requirements） | ✅ 兩個新檔都符合；既有 `Render.cs` 的 Requirements 行未動 |
| 公開型別/方法需 XML 註解（`/// <summary>`） | ✅ `HdbfIndoorPanelSignature`／`ForSection` 有；私有方法多數有，`ClearCardCache`（Cards.cs:140）沒有（trivial，nit） |
| 單檔 >300 行需拆分或書面理由 | ✅ Cards.cs 214、Signature.cs 132、Render.cs 230 |
| 關鍵行為處 `// REQ-V2-...` 行內標記 | ⚠️ 兩份新檔只有檔頭 Requirements，沒有行內 `// REQ-V2-...`；fragment 本體有這個慣例。Low |
| 魔法數字集中 `IndoorConstants` | ⚠️ `17L/31/1000f` 散在 Signature.cs；但 `HdbfIndoorPanelFingerprint.cs:40-152` 是同一個既有 pattern ⇒ 依「repo 既有實作優先」判為可接受，不列違規 |

**Smell baseline（皆為 judgement call，非 hard violation）**：

- **Duplicated Code**（Low）：`HdbfIndoorPanelSignature.Quantize`（:111-130）與
  `HdbfIndoorPanelFingerprint.Quantize`（:139-152）**逐字相同** ⇒ 抽成共用 helper（順便把「fingerprint/signature
  用同一套量化」變成可單測的事實）。
- **Data Clumps／平行清單**（Low）：`_cardViews`/`_cardSignatures`/`_cardKinds`（Cards.cs:21-25）三條**必須
  同步**的清單，新增/替換/清空/比對都要同時動三個；任何一處漏改就是隱性錯位。建議合成
  `private readonly List<CardState> _cards`, `readonly record struct CardState(VisualElement View, long Signature,
  HdbfPanelSectionKind Kind)`。這是本 diff 最值得順手做的一項可維護性改善。
- **Mysterious Name**（Low）：`Text(string?)`（Signature.cs:106-109）實際回傳「字串的 hash」，命名像取文字。
  建議 `HashOf`。
- **Repeated Switches**（judgement：**壓下不報**）：`section.Kind == Workers` 在 `BuildSectionCard` 出現兩次
  （Cards.cs:159、200），加上簽名的 third dispatch；但目前只有 Workers/Crops 兩種特例，硬抽成
  per-kind renderer 反而更複雜，保留現狀合理。
- **Speculative Generality**（judgement：**不是 smell**）：`_cardKinds` 看似可由簽名推得（Kind 也在簽名裡），
  但 kind 改變時必須整堆重建以保住 host 掛對卡，屬於必要的正確性檢查，不是投機抽象。
- **Feature Envy／Middle Man／Divergent Change／Refused Bequest／Message Chains／Primitive Obsession**：
  未在 diff 中發現。`HdbfIndoorPanelSignature` 讀整個 `data` 是「把 Worker 卡 extras 也拿來簽名」的必要投影，
  不是 envy。

**Hard violation：無。**

---

## 4. Spec 軸（玩家回饋）

玩家原話（`crop-dropdown.md:5-6`）：「The drop down to change from rotation to a specific crop doesn't open
for me. I'd like to not rely on a random crop rotation.」⇒ 兩個要求：(a) 下拉要能打開／可操作；
(b) 要能改成固定作物。

| 要求 | 狀態 |
|---|---|
| (a) 下拉打不開 | **實作層已修**（逐卡 diff 讓作物卡不再每輪 detach），根因與修法我已獨立複核；**實機點擊仍 pending**，文件已標明。 |
| (b) 能改成固定作物 | 這條原本就存在、被 (a) 擋住：items 由 `HdbfIndoorPanelWarnings.BuildCropChoices`（`HdbfIndoorPanelWarnings.Crops.cs:57-105`）提供（rotation ＋ 陣營作物），選取 → `OnCropSelected` → `TrySetCropPolicy`；本次未改動該路徑，也沒有離線證據能證明「選了之後面板顯示固定作物並持久化」（需實機）。 |
| 有沒有做 spec 沒要的事（scope creep） | **沒有**。變更精準：1 個新簽名檔、1 個卡片 diff partial、Render.cs 搬 body、4 條測試、3 份文件。RadioToggle 不再每 tick 搬動是同根因的附帶改善，不是新功能。 |
| 看起來對但實作可疑 | 未發現。唯一「行為差異」是選取作物後同一 render 內作物卡會同步重建（policy 文字變）⇒ 選取完成後 detach/attach 一次，不影響點擊（見 §2.3）。 |

**沒有方向性錯誤**；我沒有反證可以推翻根因或修法。

---

## 5. 逐條問題清單（severity / file:line / 為何錯 / 建議修法）

> 全部 Low；沒有 Medium/High。N1–N3 屬「文件/註解與程式碼一致性」，N4–N9 屬可維護性，
> N10–N14 屬測試，N15–N17 屬文件/驗證限制。

**N1（Low，檔頭過時）** `src/HDBF.UI/Panels/HdbfFarmPanelFragment.cs:5-7`
Purpose 還寫「Rendering lives in .Render.cs, element construction in .Build.cs, …」，沒有提到新的
`.Cards.cs`。為何錯：本輪把卡片堆疊的 reconciliation 搬進 `.Cards.cs`，檔頭是讀者第一個會相信的地圖，
現在不完整。建議：補「card-stack reconciliation / per-card content diff in .Cards.cs」。

**N2（Low，檔頭過時）** `src/HDBF.UI/Panels/HdbfFarmPanelFragment.Render.cs:3-9`
Purpose 仍宣稱本檔負責「the ordered cards (workers/efficiency, slots, water, maintenance, power, crop
strategy, output)」，但 `RenderCards` 已移出。為何錯：與實際責任不符，會誤導後續維修者往錯的檔案找卡片
渲染。建議：改成「applies actions / action status / scroller height / tooltips；卡片堆疊在 .Cards.cs」。

**N3（Low，註解宣稱過強）** `src/HDBF.UI/Panels/HdbfIndoorPanelSignature.cs:21-23`
「Equal signatures mean "the built card would be visually identical"」與「the fields mixed here are exactly
the fields BuildSectionCard renders, no more and no less」忽略 dropdown 是 out-of-band 同步的。
為何錯：相等簽名**不足以**保證整張卡相同——`data.CropChoices`/`SelectedCropId`/`CropTooltip` 不在簽名內，
目前由 `RenderCrop` 每輪同步（Cards.cs:62-67、Crop.cs:62-88）兜住；若哪天把那個每輪同步拿掉，
下拉會停在舊選取（反例見 §2.1）。另外「the next panel fingerprint change repairs it」也不精確：
需要的是「下一次非碰撞的卡片內容變更」。
建議：把「dropdown 內容由 RenderCrop 每輪另行同步，不在本簽名內」與碰撞修復條件寫清楚。

**N4（Low，pre-existing 效能）** `src/HDBF.UI/Panels/HdbfFarmPanelFragment.Crop.cs:70,198-212`
`RenderCrop` 每輪（1.7–2.5 次/秒）都 `new StringBuilder(...).ToString()` 算 `CropSignature`。
為何錯：這是 UI 刷新路徑上仍存在的逐次配置，與 REQ-V2-UI-01「無分配抖動」不完全相符；**不是本 diff
引入**（舊版同樣每輪呼叫），但既然本輪已建立逐卡簽名基礎，順手收掉很自然。
建議：比對 provider 現有 items（string 比較、零配置）或把 `CropSignature` 結果快取，只在
`SelectedCropId`/`CropChoices` 真的變時重建字串。

**N5（Low，防禦性）** `src/HDBF.UI/Panels/HdbfFarmPanelFragment.Cards.cs:64`
`_cropHost.parent is not null` 當成「已掛載」判斷偏弱：host 寄在已 detach 的舊卡 row 內時也為 true
（今天只有 `sections.Count==0` 早退路徑能造成，而 factory 不會回空 sections，故**目前不可達**）。
為何錯：若未來 sections 可能為空/條件式，`RenderCrop` 會對未掛載的 dropdown 做 `SetItems`/重繪，
浪費且語意錯誤。建議：改 `_cropHost.panel is not null`（或直接記錄作物卡元素是否在 `_cards` 內）。
注意：`V2PanelVisualContractTests.cs:118-126` 有釘這個字串，改的時候要同步改測試。

**N6（Low，防禦性）** `src/HDBF.UI/Panels/HdbfFarmPanelFragment.Cards.cs:205-209`
`cropPolicyRow` 只在「存在非 warning row」時才非 null；若 Crops 卡哪天只剩 warning rows，`_cropHost`
不會被加進新卡（會留在舊/已 detach 的卡裡）⇒ 下拉消失且無告警。今天 factory 保證 Crops 卡至少 2 個
非 warning row（`HdbfIndoorPanelFactory.Cards.Output.cs:47-64`），所以不可達。建議：加註解／debug
assert，或在 `cropPolicyRow` 為 null 時退回把 `_cropHost` 掛到卡片本體。

**N7（Low，可維護性／Data Clumps）** `src/HDBF.UI/Panels/HdbfFarmPanelFragment.Cards.cs:21-25`
三條平行清單（`_cardViews`/`_cardSignatures`/`_cardKinds`）必須永遠同長、同 index 同步；本 diff 已經有
4 個地方要同時動它們。建議合成單一 `List<CardState>`（record struct），讓「三條不同步」這種 bug 不可能
發生。

**N8（Low，Duplicated Code）** `src/HDBF.UI/Panels/HdbfIndoorPanelSignature.cs:111-130`
`Quantize` 與 `src/HDBF.UI/Panels/HdbfIndoorPanelFingerprint.cs:139-152` 逐字相同。
建議：抽成共用 internal helper（例如 `HdbfPanelHash.Quantize`），兩邊呼叫；順便統一碰撞語意。

**N9（Low，命名）** `src/HDBF.UI/Panels/HdbfIndoorPanelSignature.cs:106-109`
`private static int Text(string? value)` 回傳的是 hash，不是文字。建議改名 `HashOf`。

**N10（Low，測試 vacuous）** `tests/HDBF.Tests/V2PanelVisualContractTests.cs:118-126`
rendered 清單裡的 `"row.Bar"` 會被 `"row.BarText"`/`"row.BarMarker"` 滿足，即使 `Quantize(row.Bar)`
被刪也照過。為何錯：契約測試宣稱要釘住 bar fill 的覆蓋，實際上沒釘到。
建議：改斷言 `"Quantize(row.Bar)"`（或對每個 token 做 `char.IsLetterOrDigit` 邊界檢查）。

**N11（Low，測試 overclaim）** `tests/HDBF.Tests/V2PanelContractTests.cs:782-865`
docstring/名稱宣稱「every rendered field」，實際是手寫子集，抓不到未來新增的渲染欄位（正是它說要防的事），
也漏 `WorkerChip.Name`/`Tooltip`、`Rows.Count`、`Rows==null`。為何錯：真正的風險是「BuildSectionCard
新增渲染欄位但忘了加簽名」，這條測試對此無效。建議：加自動守門——掃 `BuildSectionCard` 與
`RenderWorkerRoster` 原始碼內出現的 `data.X`/`section.X`/`row.X`/`chip.X` 名稱，逐一斷言
`HdbfIndoorPanelSignature.cs` 原始碼也出現該名稱；這能抓「新增渲染欄位漏簽名」，成本低且零遊戲依賴。

**N12（Low，測試冗餘）** `tests/HDBF.Tests/V2PanelContractTests.cs:727-728`
第一個斷言 `Equal(sig, ForSection(section, first))` 是**同一表達式比對自己**，對任何純函式都成立，只在
「實作改成回傳 RNG/身分 hash」時有訊號。建議保留（成本為零）但把訊息改成「signature 必須是內容的純函式」，
或改為斷言「同一 section 從兩份獨立 model 取到的簽名相同」（那是真正有效的那條）。

**N13（Low，測試缺口）** 缺 `Cards.cs:75-117` 的決策邏輯測試。建議把「哪些 index 要重建／要不要整堆重建」
抽成純函式（例如 `HdbfCardDiff.ChangedIndices(...)`），L1 測多卡同時變、kind 改變、count 改變、順序重排。

**N14（Low，測試缺口）** 缺「作物卡**應該**重建」的正向測試：斷言 rotation→fixed（或反之）時 Crops 卡
簽名必須改變。否則未來把簽名改窄導致過度抑制重建時，現有 4 條測試可能仍全綠。

**N15（Low，文件可追溯性）** `docs/v2/TRACEABILITY.md:85`
REQ-V2-UI-01 的實作欄只有 `U:Panels/HdbfFarmPanelFragment*.cs`，沒有新的
`U:Panels/HdbfIndoorPanelSignature.cs`；L1 欄也沒有 `T:V2PanelVisualContractTests`。
建議補上（§19.18.1-A／REQ-V2-READ-02 的可追溯性要求）。

**N16（Low，文件內部不一致）** `docs/workstreams/feedback-20261106/crop-dropdown.md:29-32` vs `:51-55`
§1.4 把「ClickEvent 因 detach 遺失 pointer capture」寫成機制之一（「使用者看到的正是…」），但同檔 §3
（:51-55）、`VERIFICATION.md:1914`、`CHANGELOG.md:22` 都已降級為「次要、未 IL 取證」。
建議：§1.4 加「（次要、未取證）」字樣，全篇口徑一致。

**N17（Info，驗證限制）** `docs/VERIFICATION.md:1921-1923`
「716/716 PASSED」「44 checks」「0 warning/0 error」「staged 48 files / 2,143 KB」本輪**無法複核**
（禁止 build）。716 與 +4 我以靜態計數交叉驗證自洽；其餘數字屬 Lead 門禁輸出，本報告不背書也不否定。
無需修改，僅供最終回報時標示「未獨立複核」。

---

## 6. 我無法離線證明、必須實機確認的部分

1. **真機可互動驗證**：點開作物策略下拉 → 清單停留（不再被 4 Hz 刷新關掉）→ 選一個固定作物 → 面板
   顯示「Fixed crop: X」→ 存檔／重載後仍固定。這是唯一能證明玩家回報真的修好的證據；文件已正確標 pending。
2. **實機的 detach 頻率歸零**：我只能從程式面證明「unchanged Crops 卡不會重建」＋「RenderCrop 不 detach
   `_selection`」；實機是否有其它 4 Hz 路徑（vanilla entity panel 重建 fragment、UI Toolkit 版本行為）
   還會造成 detach，離線看不到。
3. **殘留情境**：清單開著時若作物卡內容真的變（作物表重建／語言切換／invalid-crop 警告出現）仍會關清單；
   以及開著清單時視窗 resize / 上方卡片高度改變造成彈出層位移。頻率低，但只有實機能判。
4. **`build.ps1` 的 716/716、44 gates、0 warning、staged 48 files/2,143 KB**：本輪 policy 禁止跑 build，
   只能做靜態交叉核對（716 條測試方法、+4、35 個 gate 呼叫點）；**未獨立複核**。
5. **ClickEvent／pointer capture 遺失**：屬 Unity UIElements 引擎側行為，`ildasm` 看不到；文件已降級為
   「次要、未取證」，我同意這個處置。
6. **tooltip registrar 是否強參考被丟棄的元素**（長期記憶體）：無法離線驗證；本 diff 大幅減少重建量，
   若真有泄漏也只在變好的方向。

---

## 7. 我實際看過的檔案（含唯讀取證）

被審變更：
- `src/HDBF.UI/Panels/HdbfIndoorPanelSignature.cs`（新，132 行，全文）
- `src/HDBF.UI/Panels/HdbfFarmPanelFragment.Cards.cs`（新，214 行，全文）
- `src/HDBF.UI/Panels/HdbfFarmPanelFragment.Render.cs`（全文＋git diff 舊 `RenderCards` body）
- `tests/HDBF.Tests/V2PanelContractTests.cs`（新測試 718-865、helper `IntermediateFixture`/`CreateData`/`Section`/`ReadUiFile`）
- `tests/HDBF.Tests/V2PanelVisualContractTests.cs`（新測試 97-127、被改的 82-90、既有 Render.cs 契約 206/297/311/475/604-640）
- `docs/VERIFICATION.md` §42（:1902-1926）、`docs/v2/CHANGELOG.md`（:1-37）、
  `docs/workstreams/feedback-20261106/crop-dropdown.md`（全文 75 行）

被審程式所依賴、為做逐欄位/時序對照而必讀：
- `src/HDBF.UI/Panels/HdbfFarmPanelFragment.cs`、`.Build.cs`、`.Crop.cs`、`.Commands.cs`
- `src/HDBF.UI/Panels/HdbfIndoorPanelData.cs`（`HdbfPanelSection`/`HdbfPanelRow`/`HdbfWorkerChip`/`HdbfIndoorPanelData`）
- `src/HDBF.UI/Panels/HdbfIndoorPanelFactory.cs`、`.Cards.cs`、`.Cards.Output.cs`、`.Cards.Growing.cs`
- `src/HDBF.UI/Panels/HdbfIndoorPanelWarnings.cs`、`.Crops.cs`
- `src/HDBF.UI/Panels/HdbfPanelControls.cs`、`.Widgets.cs`、`HdbfIndoorPanelStyle.cs`、`HdbfPanelIcons.cs`
- `src/HDBF.UI/Panels/HdbfIndoorPanelFingerprint.cs`
- `src/HDBF.Gameplay/Components/HdbfBoxFarmController.Commands.cs`、`HdbfBoxFarmController.IndoorSnapshot.Crops.cs`
- `tests/HDBF.Tests/TestRunner.cs`（測試探索機制）
- `docs/REQ_V2_SECTION_19.md` §19.18.1（repo 編碼標準）、`docs/v2/TRACEABILITY.md`、`docs/v2/ARCHITECTURE.md`、`build.ps1`（gate 計數，唯讀）
- 遊戲 DLL（`dotnet ildasm`，安裝版 Timberborn 1.1.2.8）：`Timberborn.DropdownSystem.dll`
  （`Dropdown.Initialize`/`UpdateSelectedValue`/`SetAndUpdate`/`ToggleSelectionListDisplayStyle`/`<Initialize>b__25_0`、
  `DropdownListDrawer.Load`/`HideDropdown`/`ShowDropdown`）、`Timberborn.CoreUI.dll`
  （`RadioToggle.Create`/`Update`/`<Create>b__0`）

> 附註：`docs/workstreams/feedback-20261106/root-cause-audit.md` 是審查期間另一位成員新增的獨立根因報告
> （13:14 出現，非本次變更的一部分）；我只把它當交叉參照，未依賴其結論——本報告的 IL 證據是我自己反編譯
> 安裝版 DLL 得到的。`docs/workstreams/feedback-20261106/review.md`（本檔）是本輪唯一由我寫入的檔案。
