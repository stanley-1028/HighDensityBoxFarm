# 非暂停时作物策略下拉「弹窗很快消失」——独立根因审计（只读）

* **状态**：离线审计完成；**实机点击 A/B 仍 pending**（本机无法合成鼠标输入、本轮未运行游戏）。文中所有「已证」均指安装版 DLL 的 IL 或仓库源码行号；未离线确定的点逐条标出。
* **日期**：2026-11-14（Asia/Macau），审计快照 21:20–22:0x。
* **适用版本**：
  * **玩家报告版本 = 2.6.0（已发布）**。用 `hdbf-tools/workshop-backup/3828809784-2.6.0/HDBF.UI.dll`（干净 2.6.0 内容包，见 `release-preflight.md` §2.3）反编译核对：**发布二进制与 HEAD 源码一致**（`UpdateFragment` 只在 IL_0009–0011 有 `DropdownVisible` 守卫；`RenderCrop` 无开启守卫；`UpdateChangedCards` 无作物卡守卫）→ 见附录 B。
  * 游戏 1.1.2.8-ffc2de0-sw / Unity 6000.5.11f1（`Timberborn_Data/StreamingAssets/Version.txt`）。
  * 候选修复 = 工作树未提交改动，**本报告审查快照为 2026-11-14 21:59:53**（Lead 在审计期间仍在编辑该工作树，行号以本快照为准；`Crop.cs` mtime 21:31、`Cards.cs` 20:53、`HdbfFarmPanelFragment.cs` 21:30）。
* **触发回馈**：Steam 评论 litos5599（2026-11-14，约 5 小时前）：「i cant change the crop production, the crop selector closes all the time…」；中文回馈原意：暂停时能选固定作物，运行时下拉弹窗很快消失、选不了。
* **审计者**：`root-cause-auditor`（独立于 Lead 的重跑取證；未修改 `src/`、`tests/`、`data/`、build 脚本、git；未运行游戏/build.ps1；唯一写入文件为本报告）。
* **方法**：安装版游戏 DLL 用 `tools/ApiIndex il`（Mono.Cecil）反编译 → 逐方法读 IL → 与仓库源码/发布二进制交叉核对（发布二进制同样用 ApiIndex 反编译）。证据文件全部在 `%USERPROFILE%\.hdbf\audit\`（见附录 A）。
* **变更摘要**：2.6.0 的作物下拉仍会被 4 Hz 面板刷新关掉：原版 `DropdownListDrawer.HideDropdown` 由「Selection 按钮 detach」或「`ClearItems`→`HideDropdown`」触发，而 2.5.7 起的唯一防线 `DropdownVisible` 读 `resolvedStyle.display`（上一轮 layout 的值）——弹窗打开当帧必为「未开」，守卫存在 layout 滞后盲区；2.6.0 **没有引入新的关窗路径**（发布 DLL IL 直证），只是把整卡重建换成逐卡内容签名 diff，降低触发面但未消除盲区。

---

## 0. 结论（一句）

**主根因（结构层，IL 已证）**：原版下拉的打开信号是同步的（`Showed` 在 `ToggleSelectionListDisplayStyle` 内、`ShowDropdown` 之后同步 Invoke），但**打开状态的可读信号 `DropdownListDrawer.DropdownVisible` 是 layout 滞后的**（`IsDisplayed` = `resolvedStyle.display == Flex`）；HDBF 2.6.0 的刷新守卫只有这个滞后信号，所以「弹窗打开的当帧（可能不止一帧）」里，4 Hz 刷新体仍会完整执行——刷新体内有两条原版关窗路径：`DropdownItemsSetter.SetItems → Dropdown.ClearItems → DropdownListDrawer.HideDropdown`（无条件调用）与「作物卡重建 → `_selection` 子树 detach → `<Initialize>b__25_0 → HideDropdown`」。暂停时面板指纹冻结（不刷新）→ 弹窗正常；运行时指纹每模拟 tick 必变 → 每个 0.25 s 采样都可能撞进盲区。

**置信度分层**：
| 命题 | 置信度 | 依据 |
|---|---|---|
| 守卫 `DropdownVisible` 是 layout 滞后量，弹窗打开当帧必读到「关」 | **0.95** | `VisualElementExtensions.IsDisplayed` IL + `ToggleDisplayStyle` IL + `Showed` 同步时点 IL |
| 2.6.0 没有引入新的关窗路径（仍是 2.5.7 的两条防线） | **0.90** | 发布 2.6.0 `HDBF.UI.dll` IL 与 HEAD 源码一致 |
| 上述盲区是玩家「运行时老是关掉」的**主要原因** | **0.65** | 逐卡 diff 已使稳态刷新不再重建作物卡；同帧盲区要「每击必现」，取决于 `UpdateFragment` 与 UIElements 事件派发的帧内先后（**无法离线确定**，见 §5）。可能还有一个未取证的放大器（见备选 H2/H6），需实机日志定级 |
| 备选 H2（`UpdateSelectedValue` 4 Hz 重建 Selection 内容本身触发 detach 回调） | **0.20（降级）** | DetachFromPanelEvent 在 managed `EventBase.LocalInit` 把 propagation 置 0（不冒泡/不下钻），被清掉的是 `_selectedItem` 的子元素、`_selection` 不在该子树内；native 派发无法完全排除 |

---

## 1. 原版打开路径（全部 IL 已证）

反编译 `Timberborn.DropdownSystem.dll`（安装版 1.1.2.8；证据 `cap-dropdown-system.txt`）。

### 1.1 点击 Selection 按钮 → `ToggleSelectionListDisplayStyle(ClickEvent)`

`Dropdown::Initialize`（IL_005c `brfalse` → IL_00bf 分支，`_buttonsOnlySelection == false`）：
* IL_00bf–00de：隐藏 `ArrowLeft`/`ArrowRight`；
* IL_00e3–00fb：`_selection.RegisterCallback<ClickEvent>(ToggleSelectionListDisplayStyle, NoTrickleDown)`。

`Dropdown::ToggleSelectionListDisplayStyle`：
| 偏移 | 行为 |
|---|---|
| IL_0006 | `_dropdownListDrawer.get_DropdownVisible()` |
| IL_000b `brfalse IL_0014` | **若 DropdownVisible 为真 → IL_000d `Hide()` 后 return**（这就是「第二下点击/或布局滞后时点击反而关掉」的分支） |
| IL_0015 | `AddItemsIfNeeded()`（`_elements.Count != 0` 时直接 return；否则对 provider.Items 逐个建 item 元素、注册 item 的 ClickEvent/MouseEnter/MouseLeave） |
| IL_001a–002c | `_dropdownListDrawer.ShowDropdown(_selection, _elements)` |
| IL_0031–0047 | **`Showed` 同步 Invoke**：`ldsfld EventHandler ... Invoke(EventArgs.Empty)`，即**在同一个 ClickEvent 调用栈里、ShowDropdown 返回后立即同步触发** |

`Dropdown.add_Showed`：类型是 `System.EventHandler`（object sender, EventArgs），非 `EventCallback`。⇒ 候选修复 `dropdown.Showed += OnCropDropdownShowed` 合法且**不是延迟信号**。

`DropdownListDrawer::ShowDropdown`：IL_0000 先 `HideDropdown()`（防重入；若本来就不可见则早退）、IL_0007 `_parent = 传入父元素`、IL_000e `AddInputProcessor(this)`、IL_0019–0074 在 `_parent` 与 `_items` 上注册 MouseEnter/Leave、IL_0079 `_isMouseOver = 1`、IL_0081–0087 `ToggleDisplayStyle(_root, true)`（只写 inline style）、IL_008c–00b0 把 item 元素加进 `_items` ScrollView、IL_00bc `CalculateDimensions()`（用 `_parent.resolvedStyle` 的宽度/高度/位置，读的是**上一轮 layout 的值**）。

**打开成功的判定**：原版没有返回值判定，唯一同步成功信号就是 `Showed`；而 `_root` 的可见性要等下一轮 style/layout 解析后 `resolvedStyle.display` 才会变成 Flex。

### 1.2 `DropdownListDrawer.DropdownVisible` = layout 滞后（已证，锚点）

* `DropdownListDrawer::get_DropdownVisible` IL_0000–000b：`_root → CoreUI.VisualElementExtensions.IsDisplayed(_root)`。
* `Timberborn.CoreUI.VisualElementExtensions::IsDisplayed`（`cap-visualext2.txt`）：IL_0001 `get_resolvedStyle()`、IL_0006 `IResolvedStyle.get_display()`、IL_000b `ldc.i4.0`、IL_000c `ceq` ⇒ **`resolvedStyle.display == DisplayStyle.Flex(0)`**。
* `VisualElementExtensions::ToggleDisplayStyle`：IL_0000–0017 只写 `style.display = display ? Flex : None`（inline style），不碰 resolvedStyle。
* ⇒ `ToggleDisplayStyle(_root, true)` 之后的**当帧**，`DropdownVisible` 仍可能是 `false`；`Showed` 是唯一精确的「已打开」信号。**候选修复的该前提成立。**

---

## 2. 原版所有会关闭弹出菜单的路径（全部 IL 已证）

| # | 路径 | IL 依据 | 是否投递 `DropdownHiddenEvent` |
|---|---|---|---|
| C1 | `Dropdown::<Initialize>b__25_0(DetachFromPanelEvent)` = **仅** `HideDropdown()`；注册在 `_selection`（Button）上，NoTrickleDown | `cap-dropdown-system.txt:527-531`；注册见 `Initialize` IL_004a–0057 | 取决于 C5 的前置判断 |
| C2 | `DropdownItemsSetter.SetItems(dropdown, provider)`（2-arg 重载 IL_0000–000f）→ 4-arg 重载 IL_0000 `dropdown.ClearItems()` → `ClearItems` IL_0036–003c **无条件调用 `HideDropdown()`** | `:693-701`、`:760-769`、`ClearItems :253-275` | 同上 |
| C3 | `Dropdown::ClearItems` 自身：provider=null、HoveredItem=null、`_selectedItem.Clear()`、`_items.Clear()`、`_elements.Clear()`，末尾 `HideDropdown()` | `:253-275` | 同上 |
| C4 | `Dropdown::SetAndUpdate(item)`：若 provider 值不同 → `SetValue` + `UpdateSelectedValue` + `ValueChanged`；**最后无条件 `HideDropdown()`**（选完即关，属正常） | `:469-495` | 同上 |
| C5 | `DropdownListDrawer::HideDropdown()`：**先判 `if (!DropdownVisible) return;` 且这个 return 在 Post 之前**；可见时才 RemoveInputProcessor → `_items.Clear()` → `ToggleDisplayStyle(_root,false)` →（错误 delegate 反注册，见 §2.1）→ `_isMouseOver=0`、`_parent=null` → `_eventBus.Post(new DropdownHiddenEvent())` | `:1068-1121`（IL_0000/0006 早退，IL_009c–00a7 Post） | **只有可见时才发**；不可见时早退**不发事件** |
| C6 | `Dropdown::Hide()` = `HideDropdown()` + `HoveredItem=null`（`ToggleSelectionListDisplayStyle` 的二次点击分支、`Dropdown.Hide()` 公开 API） | `:285-292` | 同 C5 |
| C7 | `DropdownListDrawer::ProcessInput()`：`Cancel` → Hide；`(MainMouseButtonDown \|\| ScrollWheelActive) && _isMouseOver == 0` → Hide | `:969-997` | 同 C5 |
| C8 | `ShowDropdown` 开头的 `HideDropdown()`（重入保护） | `:999-1006` | 同 C5 |
| C9 | `ClearItems`/`SetItems` 在「弹窗本来就不可见」时：`HideDropdown` 早退，**不会**发 `DropdownHiddenEvent` | C5 | **不发** ← 候选修复的漏洞来源 |

**`_isMouseOver` 语义**：`MouseEntered` IL_0000–000e `+1`；`MouseLeft` IL_0000–000e `-1`；`ShowDropdown` 置 1。`_isMouseOver` 只在 0 时配合「主鼠标键按下/滚轮」关窗。

### 2.1 原版次要 quirk（方向是「更难关」，不是「自动关」）
`ShowDropdown` 注册 `<ShowDropdown>b__15_0..3`，`HideDropdown` 反注册的是 `<HideDropdown>b__16_0..3`（不同 delegate，Unregister 不匹配）⇒ 每次打开都会在 `_parent`/`_items` 上累积一对 enter/leave 回调，`_isMouseOver` 计数随打开次数漂移。该 quirk 会让弹窗**更不容易**被外部点击关闭（计数偏大）或让 item 判定异常，不是本症状的关窗来源；但它使「鼠标从按钮移入列表途中短暂 `_isMouseOver==0` + 按下」的偶发关闭成为可能（次要机制，未实机取證）。

### 2.2 `DetachFromPanelEvent` 的投递范围（重要，含证据边界）
* `Dropdown.Initialize` 把回调挂在 `_selection`（Dropdown 的**后代** Button）。真正被移除的是卡片/整卡子树，`_selection` 作为后代收到自己的 detach 事件 → C1 → C5。
* Unity 6 安装版 `UnityEngine.UIElementsModule.dll` 可被 Cecil 读取：`EventBase::LocalInit` IL_0025–0026 `set_propagation(0)`（`cap-eventbase.txt`）；`DetachFromPanelEvent` 的 `.ctor/.cctor` 只设置创建函数、**未覆盖 propagation**（`cap-uie-events.txt:1762-1789`）；`get_bubbles`/`get_tricklesDown` 分别取 propagation 的 bit0/bit1（同文件 IL_0000–002b）。
* **代码推断（0.75）**：在 managed 层 DetachFromPanelEvent 是 propagation=None（target-only 派发，Unity 对「被移除元素及其后代」逐个派发），**不会**冒泡到仍然挂着的祖先。因此「`UpdateSelectedValue` 每 4 Hz 重建 `_selectedItem` 内容 → 旧子元素 detach → 触发 `_selection` 回调 → 关窗」这条链**缺乏 IL 支持**（除非 native 层实现与 managed 默认不同）。
* **无法离线确定**：`VisualElement.OnDetachFromPanel`/native 派发实现不在 managed 程序集（`referrers DetachFromPanelEvent` 只列出 `ScrollView/GenericDropdownMenu/...` 的 override，没有基类派发方法）——本机装的是 Unity 6 Player，不能跑编辑器测试。

---

## 3. HDBF 2.6.0「运行中」4 Hz 刷新对打开中下拉做了什么

### 3.1 刷新频率：为什么运行中必刷新、暂停时不刷新
* `HdbfFarmPanelFragment.cs:46` `RefreshIntervalSeconds = 0.25f`；`UpdateFragment()`（`:194`）每帧被 EntityPanel 调用 → 节流后 `Refresh(force:false)`（`:223`）。
* `HdbfIndoorPanelFingerprint.cs:99` 混入 `Quantize(snapshot.CurrentDay)`，`:151` `Quantize = (long)(value*1000f)`；`CurrentDay = DayNightCycle.PartialDayNumber = DayNumber + _ticksPassedToday/768`，**每个模拟 tick +1/768 日 ⇒ ×1000 后每 tick 必变**（与 11/06 审计一致）。另 `:132` 每作物 `AverageProgress*100`、水罐/进度等连续量也在变。
* ⇒ **运行中**：每 0.25 s 采样必然发现指纹变化 → `Render()` 全量重建 read model 并重跑 `RenderCards`/`RenderCrop`；**暂停时**：模拟 tick 停止 → 所有连续量冻结 → 指纹不变 → `Refresh` 在 `:341`（指纹相等）直接 return，`RenderCrop` 根本不被调用。
* **这就是「暂停能选 / 运行选不了」的分界。**

### 3.2 刷新体对打开中弹窗做过什么（2.6.0 发布版；IL+源码双证）
1. **`RenderCrop` 每次刷新都调用**（`HdbfFarmPanelFragment.Cards.cs:92-95`：`_cropHost.panel != null` 时），它在 2.6.0 里的行为（发布 DLL `cap-shipped260-ui.txt` RenderCrop）：
   * `:115` `_cropProvider.Load(...)`（只改 HDBF 自己的 provider）；
   * 若 `CropSignature` 变了 → `:121` `_dropdownItemsSetter.SetItems(...)` → **原版 C2/C3 → `HideDropdown()` → 弹窗关闭**（发布 DLL IL_0054 call SetItems 直证）；
   * 否则 → `:126` `_dropdown.UpdateSelectedValue()`（发布 DLL IL_0061 直证）→ 原版只 `_selectedItem.Clear()` + 重建该项内容（`UpdateSelectedValue :497-520`），**不触碰 drawer 的 `_items`/`_root`**。
2. **卡片重建路径**（`HdbfFarmPanelFragment.Cards.cs:127-161` 逐卡内容签名 diff）：只有内容签名变化的卡片才 `BuildSectionCard` + `RemoveAt/Insert`。`BuildSectionCard` 会把常驻的 `_cropHost`（内含 Dropdown）re-parent 到新卡片的策略行（`:256-262`）→ 旧卡 detach → `_selection` 收到 detach → **原版 C1/C5 关窗**。
3. 逐卡 diff 是否覆盖？
   * **已覆盖**：2.5.6 及以前的「`_cards.Clear()` 整堆重建 → 作物卡每 tick 被 detach」这一主路径，2.5.7 起被逐卡 diff 消掉；2.6.0 沿用。
   * **未覆盖（稳态无害、事件时致命）**：只要作物卡**内容签名**变化（切换 rotation↔fixed、固定作物名变化、invalid-crop 警告出现、轮作序列文本变化）→ 该卡重建 → 弹窗被 detach 关闭。这些不是每 tick 事件，但它会把「打开弹窗时恰好有内容变化」变成必关。
   * **未覆盖（layout 盲区）**：守卫 `IsCropDropdownOpen()`（`HdbfFarmPanelFragment.cs:236`，发布 DLL `IsCropDropdownOpen` = 仅 `get_DropdownVisible`，IL_0006）在 `UpdateFragment` 顶部（`:207`）执行；弹窗打开当帧它仍为 false ⇒ 该帧刷新体照跑。此时若刷新导致上面 1/2 任一动作 → 弹窗在「刚打开」的瞬间被关掉，玩家看到菜单一闪（甚至看不到）。
4. **2.6.0 是否引入新路径？**
   * 发布 2.6.0 `HDBF.UI.dll` 与 HEAD 源码一致：`RenderCrop` 无开启守卫、`UpdateChangedCards` 无作物卡守卫（IL 直证）——**2.6.0 没有新增关窗调用**；折叠功能只切 body 的 display（`Fold.cs`），不 detach；`_visualElementInitializer` 只在建树时跑一次。
   * **但 2.6.0 的 UI 原版化把「自订下拉文字重绘」删掉时，也删掉了 2.5.x 里挂在 `Showed` 上的既有订阅**（`PaintDropdownPopupText`）；该订阅与关窗无关，但说明 `Showed` 这个唯一同步开信号在 2.6.0 里**已无人使用** → 守卫只剩 layout 滞后的 `DropdownVisible`。
   * 结论：**这是 2.5.7 修复不完整的延续，不是 2.6.0 新回归。**（玩家在 2.6.0 报告，因为 2.6.0 是当时最新版。）

### 3.3 命令路径绕开 `UpdateFragment` 守卫（2.6.0 源码事实）
`Refresh(force: true)` 在 `:65/96/131/163/197/226`（Commands.cs）被命令回调直接调用，**不经过** `UpdateFragment` 顶部的守卫；其中 `:96-97` 正是作物选择回调（它在原版 `SetAndUpdate` 内部、`HideDropdown` 之前同步执行）。若弹窗仍开（例如选了无效项/命令被拒/重入），该 force refresh 会重建作物卡或重设 items 而关窗。候选修复中用 `Cards.cs:138` 与 `Crop.cs:110` 两道守卫覆盖此路径（见 §6）。

---

## 4. 主根因与备选假设

### 4.1 主根因判定
**「运行中 4 Hz 刷新 + 守卫用 layout 滞后信号」**：
原版弹窗的关窗路径只有两类（§2 表，IL 已证）——(a) 卡片/卡片祖先被 detach（C1→C5），(b) `ClearItems`/`SetItems`（C2/C3→C5）。2.6.0 的刷新体恰好包含两者的触发点：作物卡重建、`RenderCrop→SetItems`；而**唯一防线 `DropdownVisible` 在弹窗打开当帧必为 false**（§1.2 IL 已证）。暂停时刷新被指纹冻结（§3.1），弹窗因此不受影响——完整解释「暂停能选、运行很快消失」的对称性。

**置信度：结构判定 0.90；「这是玩家『老是关掉』的唯一主因」0.65。**
保留 0.35 的理由（诚实列出）：
1. 逐卡 diff 后，稳态（作物策略未变、choices 未变）的刷新**不再执行**任何关窗动作，只剩 `UpdateSelectedValue`（§2.2 判定它不关窗）。因此「必现」要求每次点开都恰好有内容变化或恰好撞进 layout 盲区帧——离线**无法确定** `UpdateFragment` 与 UIElements 事件派发的帧内先后（Unity PlayerLoop 组合是 native 的；本机无编辑器）。若实测为「偶发」而非「必现」，主因仍是该盲区；若实测「每次必关」，则另有放大器，见 4.2-H6。
2. 本机无实机点击证据（`docs/in-game/ui260-Player.log` 只是 2.6.0 截图会话，`[verify-auto-select]` 是截图 harness，未点下拉）。

### 4.2 备选假设与否决理由
| # | 假设 | 判定 | 理由 |
|---|---|---|---|
| H1 | 弹层被 `EntityPanel`/其他 UIDocument 盖住（z-order） | **排除（0.95）** | `DropdownListDrawer.Load` IL_0006–0011 `RootVisualElementProvider.Create(..., 2)`（sortingOrder=2），`PanelStack` 为 1（11/06 审计已核 `CreateUIDocument`/`PanelStack` IL）；发布 2.6.0 面板仍是 EntityPanel 子片段。 |
| H2 | `RenderCrop` 每 4 Hz 的 `UpdateSelectedValue` 重建 `_selectedItem` 触发 detach 回调 | **降级（0.20）** | `UpdateSelectedValue` IL 只清/加 `_selectedItem` 子元素（`:497-520`），被移除者不是 `_selection` 祖先；managed 层 DetachFromPanelEvent propagation=0（§2.2）。native 派发无法离线排除，保留为低概率。候选修复注释里「re-parent 鼠标下的元素→下一次点击关窗」的说法**目前没有 IL 支持**，建议改写为「避免在交互中改动控件」而不是断言关窗机制。 |
| H3 | items 为空/provider null/点击穿透 | **排除（0.90）** | `HdbfCropDropdownProvider.Load` 恒有第 0 项（rotation，空 Id）→ `Count ≥ 1`（`HdbfIndoorPanelWarnings.Crops.cs:57-105`）；`_cropHost`/`dropdown` 的 pickingMode 未改；发布 DLL 里 `BuildCropDropdown` 无异常路径证据（异常会 log `ui.crop-dropdown`）。 |
| H4 | 玩家实际运行的是旧版本（Steam 未更新） | **无法离线排除（0.15）** | 需玩家 `start HDBF version=` log 行；Steam 评论时间在 2.6.0 上传后 ~5 h，默认已更新。 |
| H5 | 原版 `ProcessInput` 因 `_isMouseOver==0` 关窗（鼠标从按钮移入列表途中按下/计数漂移） | **次要保留（0.30）** | 与暂停无关（暂停同样要移鼠标），不能解释对称性；但 §2.1 quirk 会放大偶发。 |
| H6 | 2.6.0 折叠/原版化改动了面板结构，导致作物卡**每次刷新都被重建** | **待实机（0.25）** | 离线看 `HdbfIndoorPanelSignature.ForSection`（`:39-108`）只混入渲染字段；作物卡行来自 `HdbfIndoorPanelFactory.Cards.Output.cs:43-75`（policy 文本、固定作物名/轮作序列、invalid-crop 警告），运行中均静态 → 稳态签名不变。但「折叠 body 每次 render 重挂」「`CanUpdateCardsInPlace` 因 childCount 失配走 `RebuildCards` 全量清卡」等只可能由实机 log 的 rebuild 计数排除，建议候选修复保留 `VerifyStackRebuild/VerifyCropsRebuild` 计数到 A/B 结束（发布前删除）。 |

---

## 5. 对当前候选修复的审查（工作树快照 21:59:53；只写审查意见，未改源码）

### 5.1 覆盖度（正确的部分）
| 关窗路径（§2） | 候选修复的防线 | 覆盖 |
|---|---|---|
| 4 Hz 刷新 → 作物卡重建（C1） | `HdbfFarmPanelFragment.cs:207` 开启期间整体 return；`Cards.cs:138` 作物卡跳过 | ✔（盲区当帧也覆盖，因为 `Showed` 同步置位） |
| 4 Hz 刷新 → `RenderCrop→SetItems`（C2/C3） | `Crop.cs:110` 开启期间整体 defer（items/value/tooltip 都不动） | ✔ |
| 命令 `Refresh(force:true)`（Commands.cs）绕开 UpdateFragment | `Cards.cs:138` + `Crop.cs:110` 两道守卫 | ✔ |
| 弹窗打开当帧 `DropdownVisible` 仍 false | `Showed` 同步置 `_cropPopupOpen`（`Crop.cs:57,76`） | ✔（设计正确，原版 IL 已证 Showed 是同步信号） |
| 弹窗关闭后的恢复 | `DropdownHiddenEvent`（`Crop.cs:87-92` `[OnEvent]`）清标志 | ✔（前提是事件确实投递，见漏洞 1） |
| 标志卡死的兜底 | `StaleCropPopupFrameLimit = 60`（`HdbfFarmPanelFragment.cs:58,264-281`） | △ 帧数而非秒数，见漏洞 4 |

### 5.2 漏洞 / 阻塞问题（按严重度）

1. **【高 | 逻辑漏洞】`HideDropdown` 早退时不投递 `DropdownHiddenEvent`，标志可能卡住。**
   IL：`DropdownListDrawer::HideDropdown` IL_0000–0006 `brfalse IL_00ac` 早退，Post 在 IL_009c–00a7 之后才执行。场景：`Showed` 已置 `_cropPopupOpen=true`，随后（同帧或下一帧 layout 解析前）某个 detach/命令触发 `HideDropdown`——此时 `DropdownVisible` 仍为 false → **早退：弹窗既没被真正清理、也不发 Hidden 事件** → 标志残留，面板刷新被冻结到 stale 上限（`UpdateFragment` 顶部整体 return）。更糟的是如果同时有卡片 detach，`_parent` 被拆而 draw root 还是 Flex，会出现孤儿弹层。
   **最小修正**：(a) stale 上限改成**时间**（`_cropPopupDeadline = Time.unscaledTime + 1.0f`），不再数帧；(b) `Showed` 后允许「短暂 opening 窗口」（例如 0.15 s）内 `DropdownVisible==false` 也算开，之后以 `DropdownVisible` 为准自愈（而不是固定 60 帧）；(c) 不依赖单一 close 事件，`IsCropDropdownOpen` 保留两级信号（当前实现方向对）。
2. **【高 | 状态一致性】`ClearFragment()` 只清标志、不关弹层。** `HdbfFarmPanelFragment.cs:298-317` 把 root `display:none`，但弹层在 `DropdownListDrawer` 自己的 UIDocument 里，**不会**因此关闭：弹层可能继续浮在画面上、drawer 也仍注册着 input processor；标志却被清掉（`:307`），后续 `ShowFragment` 的 force refresh 在「弹层仍开」状态下无守卫执行。
   **最小修正**：`ClearFragment` 里若 `IsCropDropdownOpen()` 则先 `_cropDropdown?.Hide()`（会走 HideDropdown → 发事件 → 清标志），再清 root；`ShowFragment` 同理（`:187` 先置 false 再 `:191` force refresh 的顺序保留但要有上述清理）。
3. **【中 | 时序】`ShowFragment` 在 force refresh 前无条件 `_cropPopupOpen=false`（`:187` vs `:191`）。** 若换选中实体时弹层仍开（面板 root 与卡片常驻，detach 不会发生），首帧 force refresh 在无守卫下执行 `RenderCrop`/卡片重建；标志与真实状态不一致（尽管随后会被 detach 关窗纠正）。
   **最小修正**：只在「drawer 已不可见」时清标志（`if (!_dropdownListDrawer.DropdownVisible) _cropPopupOpen=false;`），或与漏洞 2 合并：先 `Hide()` 再清。
4. **【中 | 帧率相关】60 帧 = 「1 秒」只在 60 FPS 成立。** `:58` 注释与设计按 60 帧≈1 s；实际 20 FPS 时冻结 3 s、10 FPS 时 6 s（加载卡顿/大存档下 FPS 更低）。**用户可感知**（面板数值冻结/滚动高度不更新），也违反「硬保证不冻结面板」的初衷。**最小修正**：改 `Time.unscaledTime` 截止时间。
5. **【中 | 移植性/健壮性】`_eventBus.Register(this)` 失败被静默吞掉**（`:156-172`）：注册失败时唯一精确 close 信号消失，只剩 `DropdownVisible`+stale 兜底。`RegisterNow` 要求 `[OnEvent]` 方法 **public**、void、单参数（IL：`cap-eventbus.txt:203-252`，非 public 会抛 `ArgumentException`）；当前 `OnDropdownHidden` 是 public → 正常。**建议**：注册失败写一条一次性的 Warn log（不要每帧），便于实机诊断；`InitializeFragment` + `ShowFragment` 双重注册是幂等的（`_dropdownHiddenListenerRegistered`），OK。
6. **【中 | 发布阻塞（非本文件逻辑）】TEMP 验证 harness 不得进入发布包。** `src/HDBF.UI/Panels/HdbfFarmPanelFragment.Verify.cs`、`src/HDBF.Gameplay/Components/HdbfUiVerifyDriver.cs`、以及散落在生产代码里的 `VerifyRenderCall()/VerifySetItems()/VerifyCropsRebuild()/VerifyStackRebuild()/VerifyGuardSkip()/NoteSource()` 调用和 `HdbfGameConfigurator` 的 `Bind<HdbfUiVerifyDriver>()` 都是 harness；其中 driver 会**自动选中第一座农场并改游戏速度**，若误發佈是玩家可见的行为变更。`release-preflight.md` §2.4 已用 byte-scan 证明 staging/本机三份包「均非发布态」——发布前必须重新构建干净包并字节核对；`Verify.cs` 里的 `_verify*` 计数若保留到 A/B，发布前删除。
7. **【低 | 语义】`RenderCrop` 的整体 defer 也会推迟 `tooltip` 更新（`Crop.cs:128`）**；弹窗关闭后第一次刷新才恢复。可接受，但若玩家在弹窗打开期间切到别的面板再回来，tooltip 短暂旧值。无修正必要，记录即可。
8. **【低 | 副作用】`UpdateFragment` 顶部 return 同时跳过 `UpdateScrollHeight()`（`:216-223`）**：弹窗打开期间窗口尺寸变化不重算卡片滚动高度，关闭后一秒内才纠正。可不改；若追求完整，把 `UpdateScrollHeight()` 移到守卫之前（它不碰 dropdown）。
9. **【低 | 防御纵深】守卫只在 `UpdateFragment`。** 未来若有别的调用方直接 `Refresh(force:true)`（例如新的命令/事件），会绕过顶部守卫；当前靠 `Cards.cs`/`Crop.cs` 两道局部守卫兜底，方向正确。建议在 `Refresh()` 内（指纹比较之后、`Render` 之前）再加一道 `if (!force && IsCropDropdownOpen()) return;`——不改变现有行为，但让「刷新入口」只有一个语义点；注意**不能**挡住 `ShowFragment` 的首次渲染，否则新面板不显示。
10. **【低 | 记录】`_cropPopupLastCountedFrame` 在 `Showed` 时不重置**：只是帧计数的去重变量，无正确性影响；改时间制时可一并删掉。

### 5.3 最小可执行修正建议（按顺序，仍只写文档）
1. 把 `_cropPopupStaleFrames`（帧数）改为 `_cropPopupDeadline = Time.unscaledTime + 1.0f`；`IsCropDropdownOpen` 判定改为：`DropdownVisible==true` → 开；否则 `_cropPopupOpen` 且 `now < deadline` → 开；否则清标志。
2. `ClearFragment` / `ShowFragment`：先 `if (_cropDropdown is not null && IsCropDropdownOpen()) _cropDropdown.Hide();` 再清标志；`ShowFragment` 的 `_cropPopupOpen=false` 移到 `Refresh(force:true)` 之后或按 drawer 可见性条件化。
3. `RegisterDropdownHiddenListener` 失败时记一次 Warn（含异常类型），成功注册后不要再重复注册。
4. 发布前删除全部 TEMP harness（Verify.cs、HdbfUiVerifyDriver.cs、生产代码里的 Verify* 调用、GameConfigurator 绑定、FarmQueryService.NoteSource、以及引用这些源串的测试断言），用干净构建重出 2.6.1 包并做 byte-scan 核对。
5. `Crop.cs:102-109` 注释里关于 `UpdateSelectedValue`「关窗」的断言降级为「避免在交互中改动控件/减少无谓重建」，不要把未取证的机制写进发布说明；发布说明只写「弹窗打开期间不再刷新/重建作物卡与 items」。
6. 保留一处可观测诊断（可选，发布时可留 Info 级）：弹窗开启/关闭各一条限流 log（含 `DropdownVisible`、frame、`_cropPopupOpen`），便于 2.6.1 若仍回報时定级。

---

## 6. 证据等级表（不假装已证）

| 结论 | 等级 |
|---|---|
| `Showed` 在 `ToggleSelectionListDisplayStyle` 内、`ShowDropdown` 之后同步 Invoke（EventHandler） | **IL 已证** |
| `DropdownVisible = IsDisplayed(_root) = resolvedStyle.display==Flex`；`ToggleDisplayStyle` 只写 inline style | **IL 已证** |
| `HideDropdown` 先判 `DropdownVisible`，false 早退且不发 `DropdownHiddenEvent`；只有可见关闭才 Post | **IL 已证** |
| 关窗路径 = `_selection` detach 回调 / `ClearItems`→HideDropdown / `SetAndUpdate`→HideDropdown / ProcessInput(Cancel、主键或滚轮且 `_isMouseOver==0`) | **IL 已证** |
| `SetItems` → `ClearItems` → `HideDropdown`（DropdownItemsSetter 2-arg IL 直证） | **IL 已证** |
| `UpdateSelectedValue` 只清/加 `_selectedItem` 子元素，不碰 drawer | **IL 已证** |
| 发布 2.6.0 二进制与 HEAD 源码一致；2.6.0 无新增关窗路径；2.6.0 守卫只读 `DropdownVisible` | **IL（发布包反编译）已证** |
| `DetachFromPanelEvent` managed 默认 propagation=None（不冒泡）⇒ 拆 `_selectedItem` 子元素不会通知 `_selection` | **代码+IL 推断（0.75）**；native 派发实现**无法离线确定** |
| 运行中指纹每模拟 tick 必变（`CurrentDay ×1000`）、暂停时冻结 | **IL+数据已证**（游戏 blueprint 768 ticks/day 见 11/06 审计） |
| 4 Hz 刷新落在「弹窗打开当帧（当帧必为 DropdownVisible=false）」 | **IL 已证该窗口存在**；窗口**跨越几帧 / 与 UpdateFragment 的先后** = **无法离线确定**（Unity 6 native PlayerLoop/UIElements 派发） |
| 逐卡 diff 使稳态刷新不重建作物卡 | **源码推断（0.85）**（签名字段清单 + 作物卡工厂字段静态）；实机 rebuild 计数未取（需 A/B） |
| 「玩家每次必关」的完整解释 | **未定（0.65）**；需实机 A/B：开启/关闭事件、refresh 与 Showed 的 frame、rebuild/setitems 计数 |
| 玩家运行的版本 | **未知**；需玩家 log `start HDBF version=`（H4） |

---

## 7. 可验证预测（给 Lead 的实机 A/B）
1. **修復前（2.6.0）**：弹窗打开后 `DropdownHiddenEvent` 若在 <0.1 s 内出现，且同一帧有 `SetItems`/`VerifyCropsRebuild`++ → 主根因成立（盲区 + 内容变化）；若 `rebuild/setitems` 计数不涨而弹窗仍关 → H2/H5/H6 需要重查（`ProcessInput`/`_isMouseOver` 或 native detach 派发）。
2. **修復后**：运行中打开弹窗应可停留任意久；`VerifyGuardSkip` 计数持续增长（证明 4 Hz 刷新被真正冻结）；`SetItems`/`VerifyCropsRebuild` 在弹窗开启期间不应发生；关闭后第一次刷新才应用新 items/value。
3. **暂停对照**：暂停时 `guardskip=0`（本来就不刷新），弹窗行为应与修复前一致——若修复后暂停反而变差，说明标志/事件路径引入了新 bug。
4. 若修復后仍偶发关闭：先查 §2.1 `_isMouseOver` 漂移与「鼠标从按钮到列表之间按下」，再查 H6（折叠 body 是否导致每次 render 重挂）。

---

## 附录 A：证据文件与重跑命令（唯讀）

游戏 Managed：`C:\Program Files (x86)\Steam\steamapps\common\Timberborn\Timberborn_Data\Managed`
工具：`tools/ApiIndex`（Mono.Cecil；`il` 模式第 4 参为类型名 regex、第 5 参为方法名 regex）

```powershell
# 原版 DropdownSystem 全部 IL（本报告 §1/§2/§2.1 引用）
dotnet run --project tools/ApiIndex -c Release -- il "<Managed>" "%USERPROFILE%\.hdbf\audit\cap-dropdown-system.txt" "Timberborn\.DropdownSystem" ".*"

# CoreUI.IsDisplayed / ToggleDisplayStyle（§1.2）
dotnet run --project tools/ApiIndex -c Release -- il "<Managed>" "%USERPROFILE%\.hdbf\audit\cap-visualext2.txt" "VisualElementExtensions" ".*"

# Unity UIElements 事件传播（§2.2；UnityEngine.UIElementsModule.dll 可被 Cecil 读取）
dotnet run --project tools/ApiIndex -c Release -- il "<Managed>" "%USERPROFILE%\.hdbf\audit\cap-uie-events.txt" "UnityEngine\.UIElements\.(DetachFromPanelEvent|AttachToPanelEvent|PanelChangedEventBase|EventDispatchUtilities|EventDispatcher)" ".*"
dotnet run --project tools/ApiIndex -c Release -- il "<Managed>" "%USERPROFILE%\.hdbf\audit\cap-eventbase.txt" "UnityEngine\.UIElements\.EventBase" "get_bubbles|get_tricklesDown|get_propagation|Init|LocalInit|.ctor"

# EventBus.Register/[OnEvent] 语义（§5.2-5）
dotnet run --project tools/ApiIndex -c Release -- il "<Managed>" "%USERPROFILE%\.hdbf\audit\cap-eventbus.txt" "Timberborn\.SingletonSystem\.EventBus" ".*"

# 已发布 2.6.0 的 HDBF.UI.dll（§0、§3.2；干净内容包见 release-preflight §2.3）
dotnet run --project tools/ApiIndex -c Release -- il "C:\Users\user\hdbf-tools\workshop-backup\3828809784-2.6.0" "%USERPROFILE%\.hdbf\audit\cap-shipped260-ui.txt" "HdbfFarmPanelFragment" "UpdateFragment|IsCropDropdownOpen|RenderCrop|RenderCards|UpdateChangedCards|BuildSectionCard"
```

HDBF 源码引用（工作树快照 21:59:53）：
* `src/HDBF.UI/Panels/HdbfFarmPanelFragment.cs:46`（0.25s）、`:156-172`（事件注册）、`:174-192`（ShowFragment/标志复位）、`:194-224`（UpdateFragment+守卫）、`:236-295`（IsCropDropdownOpen/stale）、`:298-317`（ClearFragment）、`:319-361`（Refresh/指纹比较）、`:363`（Hide）
* `src/HDBF.UI/Panels/HdbfFarmPanelFragment.Crop.cs:34`（`_cropPopupOpen`）、`:57`（Showed 订阅）、`:76-92`（开启/关闭回调）、`:94-128`（RenderCrop+defer）、`:143`（ScheduleCropValueSync）
* `src/HDBF.UI/Panels/HdbfFarmPanelFragment.Cards.cs:92-95`（RenderCrop 挂载条件）、`:127-161`（逐卡 diff/重建）、`:138`（作物卡守卫）
* `src/HDBF.UI/Panels/HdbfIndoorPanelFingerprint.cs:99,151,132`（CurrentDay/AverageProgress/Quantize）
* `src/HDBF.UI/Panels/HdbfIndoorPanelSignature.cs:39-108`（卡片内容签名）
* `src/HDBF.UI/Panels/HdbfIndoorPanelFactory.Cards.Output.cs:43-75`（作物卡内容字段）
* `src/HDBF.UI/Panels/HdbfFarmPanelFragment.Commands.cs:96-97`（作物选择 force refresh）；`:65,131,163,197,226`（其他 force refresh）
* `src/HDBF.UI/Panels/HdbfFarmPanelFragment.Build.cs:86-88`（`_cards` 容器）
* 已发布 2.6.0 行为：`cap-shipped260-ui.txt` — `UpdateFragment` IL_0009–0011 守卫、`RenderCrop` IL_0054 SetItems / IL_0061 UpdateSelectedValue（无开启守卫）、`UpdateChangedCards` 无 Crops 分支。

## 附录 B：关键 IL 偏移速查

**Timberborn.DropdownSystem（安装版 1.1.2.8）**
* `Dropdown.ToggleSelectionListDisplayStyle`：DropdownVisible IL_0006；Hide IL_000d；AddItemsIfNeeded IL_0015；ShowDropdown IL_001a–002c；**Showed 同步 Invoke IL_0031–0047**
* `Dropdown.Initialize`：DetachFromPanel 回调注册（`_selection`）IL_004a–0057；ClickEvent 注册（非 buttonsOnly）IL_00e3–00fb
* `Dropdown.<Initialize>b__25_0`：IL_0000–000b `HideDropdown()`
* `Dropdown.UpdateSelectedValue`：`_selectedItem.Clear()` IL_0000–0006；新内容 IL_000b–0018；Add IL_0033–003a
* `Dropdown.SetAndUpdate`：末尾 `HideDropdown()` IL_003d–0043
* `Dropdown.ClearItems`：`_items.Clear()` IL_0020–0031；`HideDropdown()` IL_0036–003c
* `DropdownListDrawer.HideDropdown`：可见性早退 IL_0000–0006→IL_00ac；Post(DropdownHiddenEvent) IL_009c–00a7（**仅可见关闭才投递**）
* `DropdownListDrawer.ShowDropdown`：初始 HideDropdown IL_0000；`_isMouseOver=1` IL_0079；`ToggleDisplayStyle(_root,true)` IL_0081–0087；`CalculateDimensions` IL_00bc
* `DropdownListDrawer.ProcessInput`：Cancel→Hide IL_0015–0020；主键/滚轮 && `_isMouseOver==0`→Hide IL_0022–004d
* `DropdownListDrawer.Load`：`Create(..., 2)` IL_0006–0011（sortingOrder 2）；`_root` Q IL_0017–0024；初始 display=false IL_0051–0058
* `DropdownListDrawer.MouseEntered/MouseLeft`：`_isMouseOver ±1`
* `DropdownItemsSetter.SetItems(dropdown, provider)`：IL_0000–000f → ClearItems + SetItems(provider)
* `VisualElementExtensions.IsDisplayed`：resolvedStyle IL_0001–0006；`==DisplayStyle.Flex(0)` IL_000b–000e
* `VisualElementExtensions.ToggleDisplayStyle`：只写 `style.display` IL_0000–0017
* `EventBase.LocalInit`：`set_propagation(0)` IL_0025–0026；`get_bubbles/tricklesDown` IL_0000–002b
* `EventBus.Register`→`RegisterNow`：反射扫描 `[OnEvent]`、要求 public/void/单参（非 public 抛 ArgumentException）
