# HDBF v2 变更记录

> **状态**：**2.5.7 已发布**（2026-11-06 17:40，玩家回馈热修 + 下拉开启期间暂停刷新的防护；Steam Workshop item 3828809784，ManifestID 4408964257799319853；第一次 17:14 上传误带旧 2.5.6 staging，17:40 已重新上传正确包，详见 VERIFICATION §43；实机点击验证仍 pending，不写成已完成）；2.5.6 已发布（Steam Workshop ManifestID 3045694293614393693；GitHub 见 push 后的 master；离线门禁全绿；实机验证部分 pending，见 VERIFICATION §40）；2.5.5 为未发布的第二维修轮；2.5.4 已发布（Steam Workshop ManifestID 2037025195551276545）
> **日期**：2026-11-06
> **适用版本**：2.5.7
> **变更摘要**：v2.1 需求引入室内农场方案；本文件记录 v1→v2 的行为/数据/工程变更与实现状态。

## [2.5.7] — 2026-11-06（玩家反馈热修：作物策略下拉打不开）

依据 Steam Workshop 留言（Roslyn）与 `docs/workstreams/feedback-20261106/crop-dropdown.md`
的根因分析；离线验证见 `docs/VERIFICATION.md` §42。

### 【修复】面板 4 Hz 刷新不再拆掉整叠卡片，作物策略下拉可以正常打开/选择固定作物（REQ-V2-UI-01/02/03）

* 【根因】面板 dirty 指纹含 `CurrentDay`（`PartialDayNumber` 小数日，量化 ×1000）与作物
  `AverageProgress`：一天 768 ticks、1x 每 tick 0.6 秒 ⇒ **每个模拟 tick 指纹必变**，面板以
  「每个 tick 撞一次 4 Hz 取样」的频率重绘（1x 约 1.7–2.5 次/秒，2x/3x 逼近 4 Hz）；旧
  `RenderCards` 每次 `_cards.Clear()` 重建全部卡片，作物卡里的原版
  `Dropdown` 因此不断被 detach/re-attach。安装版 `Timberborn.DropdownSystem.dll` 的
  `Dropdown.Initialize` 对 `Selection` 注册的 `DetachFromPanelEvent` callback 直接调用
  `DropdownListDrawer.HideDropdown()`，所以打开的清单一秒内就被关掉（按下/放开跨过一次重建时
  ClickEvent 还会丢，属次要机制、未 IL 取证），玩家看到的就是「点了打不开」。
  2.5.6 的 L-11 才把 `CurrentDay` 放进指纹，时间点与 11/06 的回馈吻合。
* 【修复】新增 `HdbfIndoorPanelSignature`（每张卡的「实际渲染内容」64-bit 签名）与
  `HdbfFarmPanelFragment.Cards.cs` 的逐卡 diff：只把内容签名变了的卡片在同 index 替换，其余卡片
  （含作物卡的 Dropdown、工人卡的模式 RadioToggle）保持挂载；首次渲染/版面改变才整堆重建。
  下拉的选中文字、tooltip 与 items 仍由 `RenderCrop` 每次刷新同步 `UpdateSelectedValue()`。
* 【防护】下拉弹出清单开启期间**暂停 4 Hz 面板刷新**（读 `DropdownListDrawer.DropdownVisible`）：
  即使有任何未预期的卡片重建，也不会关闭玩家正在使用的清单；清单关闭后的第一次刷新补上最新数值。
  （2026-11-06 玩家实机回馈：暂停时能选、运行时清单很快消失——这是对「刷新拆卡」这条链的最后一道保险。）
* 【测试】L1 +5：`TestCardSignatureIsStableForIdenticalRenderedContent`、
  `TestCropProgressRefreshDoesNotRebuildTheCropCard`（含「政策改变必须重建作物卡」正向案例）、
  `TestCardSignatureTracksEveryRenderedFieldOfACard`、
  `TestEveryFieldTheCardRenderersReadIsPartOfTheSignature`（扫渲染器源码，防未来新增渲染字段漏签名）、
  `TestRefreshRebuildsOnlyChangedCardsAndKeepsTheDropdownAttached`（717/717 PASSED）。
* 【诊断】`BuildCropDropdown` 的静默 catch 现在记一行 `HDBF/Warn`（key `ui.crop-dropdown`，含异常型别）：
  若玩家的「下拉不见」其实是 UXML 资产缺失而不是 detach，下次 log 就能分辨。
* 【发布更正】2026-11-06 17:14 的首次上传因本机 `%TEMP%` 在不同工具调用间不一致，实际送出的是旧
  **2.5.6** staging（玩家端 `Player.log` 仍是 `start HDBF version=2.5.6`）；17:40 已改用固定的默认
  work root 重建并重新上传**正确的 2.5.7**（ManifestID `4408964257799319853`），本机 workshop 内容
  文件夹也已覆盖为 2.5.7 供立即测试。详见 VERIFICATION §43。
* 【未验证】本机无法合成输入、本轮 policy 为 workspace-write：**没有实机点击验证**，
  需要作者/玩家在可互动会话确认「能打开下拉并选固定作物」；不写成已完成。

## [2.5.6] — 2026-11-03（第三维修轮：把剩余离线可修项全部落地）

依据 `docs/BUG_REPAIR_20261102.md` 的 pending 清单与实作卡
`docs/workstreams/bugfix-20261102/REPAIR_PLAN_ROUND3.md`；离线验证见 `docs/VERIFICATION.md` §39。
**本轮把「不需要实机/外部模组」的 pending 项目全部修完**；需要实机会话的项目（§7.2 A–K）与需要
Emberpelts 主模组的 WP-13 仍为 pending，不写成已完成。

### 【修复】管道 3-strike 改成 60 秒滚动时间窗＋单农场失败不再全域停摆（L-02(c) / REQ-V2-COMPAT-01/02）

* 【修复】`CompatStateMachine` 的 3-strike 原本无时间窗：一次 Timber Pipes 改版会让同一 session 的
  adapter 直接 Failed，所有农场同日断水。新增 `FailureWindowSeconds=60` 滚动窗口：窗口内 3 次失败才
  Failed、成功清零；`Fail(reason)` 旧语义保留给无时钟调用端，`ReflectionCompatAdapter` 改传 monotonic
  `Stopwatch` 秒（取不到时退回旧语义）。
* 【新增】单农场一次抽水失败只降级（Degraded），不再立即等于全域 Failed。
* 证据：`CompatStateMachineTests`（+6 条时间窗/边界/时钟倒退/非有限时间戳）、
  `V2AdapterHostContractTests.TestRuntimeFailuresReuseTheThreeStrikeStateMachine`。

### 【修复】管道接口诚实区分「聚落级供水」（WP-05 部分 / L-02(b)）

* 【新增】Core `PipeProbeReason.GlobalSupply = 8` 与 read-only `PipeProbeAnchor`/`IIndoorPipeProbeAnchor`
  （冻结的单方法 `IIndoorPipeProbe` 不变）；Timber Pipes 端用既有 `PipeRegistry.TryGetGraph` anchor
  在农场坐标判定：管网有接网水塔但农场端口无 graph → `GlobalSupply`；任何 reflection 失败/未设 anchor
  → 回到原 census 结果。
* 【修改】面板管道端口行把 `GlobalSupply` 渲染为 `HDBF.Port.GlobalSupply`（「已连接（聚落级管道供水）」）。
* **行为仍未改**：抽水仍是 settlement-wide（per-farm 真实判定/端点是设计决策，需实机与第三方程 API 取证，
  见 VERIFICATION §39.3）。
* 证据：`V2AdapterHostContractTests.TestPipeProbeDistinguishesSettlementWideSupplyHonestly`、
  `TestIndoorAdapterHostForwardsTheProbeAnchor`、`V2LiveWarningContractTests`
  `TestGlobalSupplyProbeReasonRendersTheSettlementWideState`。

### 【修复】世界图示与面板改吃同一组 live 警告旗标（L-11 / WP-10 / L-10）

* 【新增】`IndoorFarmSnapshot` 新增 `CurrentDay`/`EntranceBlocked`/`LiveWarnings?`；Core 新增纯函数
  `IndoorWarningMap.LiveFlagsOf(snapshot)`（live coverage、live day 的维护逾期/stalled、动力、工人、入口、
  输出满）与 `EffectiveFlags(snapshot) = LiveWarnings ?? Warnings`；controller 先建 snapshot 再 `with`
  填 LiveWarnings 并以它重算 `IdleReason`。
* 【修复】世界图示与面板 maintenance/power/worker/output 判定全部改吃 `EffectiveFlags`，图示与面板不可能
  再不同步（旧 snapshot 不帶 LiveWarnings → 行为不变）。
* 【修改】`SeverityOf(NoWorkers)` 由 Low 升 **High（P1）**；四视觉限制维持（NoWorkers 无第五颗 sprite，
  由面板列＋idle reason 承担，测试注明）。
* 证据：新档 `V2LiveWarningContractTests`（13 条）、`V2WarningIconContractTests`、`V2PanelContractTests`。

### 【修复】世界图示 tooltip 不再内嵌动态距离（L-17 剩余项）

* 【修改】vanilla 1.1.2.8 经 `tools/ApiIndex` 查证**没有可写描述 API**（`StatusSpecification` 不可变、
  `StatusInstance.StatusDescription` get-only、`StatusSubject` 无 unregister/更新入口），因此 tooltip 改为
  **固定本地化 warning 句子**（不再把注册时的 `worldIconMaxDistance` 写死，设置改了也不会说谎）；
  缺失 API/loc 由 try/catch 隔离。残留：语言切换要等下一次注册（finished-state/reload）才反映。
* 证据：`V2LiveWarningContractTests.TestWorldIconTooltipIsFixedAndFreeOfTheDynamicDistance`。

### 【修复】config 五个死旗标全部接上消费者（L-18 / REQ-V2-EXT-04）

* 【新增】Core `IndoorFeatureToggles`（default 全启用＝v2.5.5 行为）与 `IndoorDayContext.Features`；
  `rotation`（拒绝 Rotation、启动转 Fixed 取 live 表首作物）、`maintenance`（永不到期、G_maint=1）、
  `pipeConnection`（端口强制关闭、不抽管水）、`logisticsOutput`（端口强制关闭）、`soilFatigue`
  （不累积/不惩罚、G_fat 恒 1，确认无除以 0）全部接上；每栋一次性 `indoor.feature-disabled` 日志。
* 【新增】L1 契约「每个 `FeatureFlags` property 都有非 config 消费者」＋各旗标关闭行为测试。
* 证据：新档 `V2FeatureFlagContractTests`（7 条）。
* 已知限制：`maintenance=false` 期间面板的「Next due」仍显示旧 due day（不改 UI，避免本轮扩散）；
  端口旗标重新启用后玩家需在面板重新开启端口（可逆性对「玩家选择」是部分成立）。

### 【修复】加水流量/水源探测的帧节流（L-14 离线部分 / SAFE-17）

* 【修改】`ApplyHauledWater` 改 30 帧节流（≈0.5 s@60fps）、库存解析失败加 300 帧冷却、`_saveDirty`
  只在 tank 真的变化时设；水源探测快取 16→24 个 snapshot；人工水源搜索改整数圆形预筛＋最近距离早停；
  世界图示在 `ui.worldIcons=false` 时整 pass 短路、只在图示类别变化时 Activate/Deactivate。
* 【新增】`V2WaterTests`：30 vs 144 帧同一天 tank 相同（证明节流不改每日总水量）；
  `V2NfrPerformanceTests`：水流量/人工搜索节流契约。
* **仍 pending**：100 栋实机 Profiler 量测（需实机会话）。

### 【新增】一键为全部室内农场补水（WP-08 剩余项 / REQ-V2-WATER-04）

* 【新增】Core `IBoxFarmCommand.TryRefillAll(out failureKey, out refilled)`＋`HdbfCommandFailures.NoFarms`；
  gameplay 逐农场复用 `TryRequestManualRefill`（每座农场各自 all-or-nothing：工人/入口/2 动作/12 格水源），
  单农场故障被隔离；面板 action card 新增「Refill all farms」按钮＋tooltip＋结果行（refilled n）。
* 不做「多趟」：一次点击＝每农场一趟（与手动相同成本），避免自动多花玩家动作预算；已在文件记录。
* 证据：新档 `V2RefillAndProbeContractTests`（4 条 WP-08 契约）。

### 【新增】启动时唯读行为补丁诊断（WP-07 / RE-03）

* 【新增】`HdbfBehaviorProbe`（game singleton，load 时一次）：用 reflection 找已载入的 Harmony/MonoMod
  runtime 与 vanilla `WorkerRootBehavior.Decide`，记 `indoor.behavior-probe(-patched)`；**不注册任何补丁**、
  不调用任何 patch API、不改变状态；任何反射失败吞掉。
* 证据：`V2RefillAndProbeContractTests`（2 条 probe 契约）＋ G-08 0-Harmony 扫描仍绿。

### 【修复】蓝图模板名/占格历史契约门禁（L-16）

* 【新增】`tools/validate/boxfarm-history.json`（8 份 blueprint 的已发布 TemplateName＋占格历史
  3×1×2→3×2×1→3×3×1 与更新流程）＋ `HDBF.Core.Data.BlueprintHistoryContract` 纯函数（validator 与 L1
  共用）＋ `tools/validate` 硬检查：(a) 现 TemplateName 在历史中、(b) 历史旧名必须在
  `BackwardCompatibleTemplateNames`、(c) Size 必须等于历史最后一笔已发布值；违反即 FAIL 并要求迁移审查。
* 【查证】git 历史显示 8 份 blueprint 的 TemplateName **从未变更**（故 backward 清单合理维持 `[]`）。
* 证据：新档 `V2BlueprintHistoryContractTests`（4 条，含 (a)(b)(c) negative control）＋ validator 手动
  4 种负向案例 exit 1。

### 【修复】keyed bridge 与 L-04 写入策略统一（L-04 剩余项）

* 【修改】`IndoorEntitySaveBridge.Keyed.cs` 不再「Repaired 即拒写」：CannotWrite 才拒写；Repaired 写修复
  副本＋`save.write-repaired`＋`PreviousPayload` rollback（`save.previous-snapshot`），与 payload bridge 一致。
* 证据：`V2PersistenceBridgeContractTests.TestKeyedBridgeFollowsThePrimitiveAndFailSafeContract`（更新）。

### 【修改】需求/ADR 漂移修正（L-19）

* 【修改】`ADR-V2-004` 增修订段、`REQ-V2-SECTION-19` 的 `REQ-V2-WATER-02` 增修订注记：连续水耗（时间切片）
  取代「禁止逐帧扣水」的旧表述，原意（扣水不得发生在面板/查询路径、不得帧率依赖）保留。

### 发布状态

* 【修改】`data/manifest.json` 版本 `2.5.6`；README 版本/L1 数字同步。**已发布 Steam Workshop**：
  2026-11-03 13:14:10 上传，ManifestID `3045694293614393693`（item `3828809784`，只更新内容与 Change Note；
  页面描述/封面/标签沿用旧版）。发布记录见 `docs/VERIFICATION.md` §41。
* 离线门禁：5 组程序集 0 warning / 0 error；L1 **712/712**（基线 672，本轮 +40）；`tools/validate`
  `RESULT: OK`；`RELEASE GATES: PASSED (44 checks)`；`NFR GATE MATRIX: PASSED`。
* 【实机（部分）】首次 2.5.6 实机烟雾：`-Install` 部署后与数十个第三方 mod 共同加载 **0 exception**；
  `indoor.behavior-probe-patched`（0Harmony/HarmonySharedState）实机生效、L-18 `features.logisticsOutput`
  旗标与 L-04 `maintenance.auto-default` 迁移日志实机生效。注入 fixture 农场因坐标与现有存档不符被游戏删除、
  无人值守输入无法解暂停/加速，**每日 tick/面板/存档往返仍未实机验证**；证据与限制见
  `docs/VERIFICATION.md` §40。环境已还原（workshop manifest 复原、本机部署已移除，不留重复 mod）。

## [2.5.5] — 2026-11-03（第二维修轮：L-04 存档修复副本、L-12 live 作物表、L-15 config profile、L-09 物流端口语义）

依据 `docs/BUG_REPAIR_20261102.md` §4 的 pending 清单与实作卡
`docs/workstreams/bugfix-20261102/REPAIR_PLAN_ROUND2.md`；离线验证见 `docs/VERIFICATION.md` §38，
存档语义修订见 ADR-V2-006。**本文件只收可离线证明的修复；需要实机/外部模组的项目维持 pending，
没有可用的实机会话，所有实机 fixture（BUG_REPAIR §7.2 A–K）仍为 pending。**

### 【修复】存档不再因「校验修复过」而整笔拒写（L-04 / REQ-V2-SAVE-01/04/06）

* 【修复】`IndoorEntitySaveBridge.Save` 原本只要验证过程 `Repaired` 就拒写整笔记录；未知作物、
  非正成长时间、`dueDay=0` 等状态会反复触发修复 → 每次存档都拒写 → 玩家进度回卷且只有一行日志。
* 【修复】`IndoorRepairResult` 新增 `CannotWrite`（`Fail(...)` 触发：记录/档位缺失、tier profile 非法、
  容量非法、校验器例外），把「不可写」与「可修复」分开；`Repaired` 的语义改为「写入修复后的副本」。
* 【新增】可修复记录的写入路径：`save.write-repaired`（含修复摘要与可定位 notes）＋
  `PreviousPayload` 回滚副本（修复前状态编码，仅修复路径写入，记 `save.previous-snapshot`）；
  干净存档不增加该属性。实体 ID 不一致仍按字段修复（修正为存档键），不列为不可写。
* 【新增】记录键 `PreviousPayload`（`IndoorSaveKeys`）与日志键 `save.write-repaired` /
  `save.previous-snapshot`；回滚副本不保证可载入、`Load` 不读它（见 ADR-V2-006）。
* 证据：`V2SaveWriteStrategyTests`（7 条）、
  `V2PersistenceBridgeContractTests.TestSaveWritesRepairedCopyAndKeepsRollbackCopy`、
  `V2SaveValidatorTests` 的 `CannotWrite` 矩阵。

### 【修复】固定作物下拉/名称/无效判定改吃游戏 live 作物表（L-12 / REQ-V2-EXT-01、REQ-V2-UI-02/03）

* 【修复】面板固定作物下拉与 invalid-crop 警告读 `FallbackCropCatalog`、名称表硬编码 10 种作物；
  作物模组新增的作物会显示 raw id 或被判无效，而命令层其实接受它。
* 【新增】`IndoorCropChoice`（Core）＋ `IndoorFarmSnapshot.CropChoices`：gameplay 以
  `_cropCatalog.ForFaction(_faction)` 的 live 表填充（`ReferenceEquals` 快取，steady state 不配置）；
  `CropDefinition.DisplayNameLocKey` 从 blueprint 的 `LabeledEntitySpec.DisplayNameLocKey` 读取（guarded）。
* 【修改】下拉、固定作物行、Growing 卡作物名与 invalid-crop 判定改为 live-first；live 清表为空时
  才退回内建表与 10 种内置名称（旧行为保留）。
* 证据：`V2PanelContractTests`、`V2CropCatalogTests`（`DisplayNameLocKey` 契约）。

### 【修复】面板维护材料/周期改吃 config 覆写后的真实 profile（L-15 / REQ-V2-EXT-04、REQ-V2-UI-02）

* 【修复】面板用编译期 `TierCatalog.Get` 的 profile，模拟却用 `balance.tier` JSON 覆写后的 profile →
  玩家改 config 后，维护材料/周期与面板显示不一致。
* 【新增】`IndoorFarmSnapshot.Profile`（config 解析后的 `TierProfile`）；gameplay 填 `_profile`；
  UI `FindProfile(snapshot)` 先吃 snapshot.Profile、null 才退回 `TierCatalog.Get`（旧测试/snapshot 行为不变）。
* 证据：`V2PanelContractTests.TestPanelUsesTheConfigOverrideProfileFromTheSnapshot`。

### 【修复】物流端口不再宣称「已连接」（L-09 / REQ-V2-COMPAT-02、REQ-V2-UI-02/03）

* 【修复】Output 卡物流/输出端口行只反映 adapter 探测结果却显示「已连接」，但 HDBF 没有把产物
  交给输送带的实作（`features.logisticsOutput=false`）→ 玩家以为输送带会搬作物。
* 【修改】物流端口永不显示 Connected：无端口 → `Not applicable`；检测到模块 → 新键
  `HDBF.Port.LogisticsDetected`（「已检测到模组（尚未接出货物）」）；有端口未检测 → `Not connected`。
* 【新增】`HDBF.Tooltip.LogisticsPort`（三语）：HDBF 目前不把产物交给输送带/物流模组，请用原版搬运
  或仓库。三语 key 集一致（220 keys、0 空字符串、0 v1 术语）。
* 证据：`V2PanelContractTests.TestLogisticsPortRowsNeverClaimAConnection`、
  `V2LocalizationTests.TestLogisticsPortTextsDescribeTheHonestOutputSemantics`、
  `V2PanelVisualContractTests` 的端口/tooltip 契约。

### 发布状态

* 【修改】`data/manifest.json` 版本 `2.5.5`；README 版本/L1 数字同步（**未上传 Steam Workshop、未 `-Install`
  部署，未跑实机**）。
* 离线门禁：5 组程序集 0 warning / 0 error；L1 **672/672**（基线 655，本卡 +17）；`tools/validate`
  `RESULT: OK`；`RELEASE GATES: PASSED (44 checks)`。

## [2.5.4] — 2026-11-02（玩家回馈维修轮：维护自动/追平、休耕不清空、暂停残留、管道原因、透明度）

依据 `docs/BUG_REPAIR_20261102.md`（5 条只读调查）与实作卡 `docs/workstreams/bugfix-20261102/REPAIR_PLAN.md`；
离线验证见 `docs/VERIFICATION.md` §36。**本轮回馈的 5 组问题中，除第三方补丁崩溃（RE-03）与需实机/外部模组的
项目外，P0 工作包全部落地；没有可用的实机会话，所有实机 fixture 维持 pending。**

### 【修复】维护「按了没反应」：一次点击追平所有逾期期数（WP-01 / RE-01 / L-08）

* 【修复】`TryApplyMaintenance` 原本只把 `dueDay += 1 个周期`，逾期 3 期按一次仍剩 2 期 → 仍停摆。
  现在一次点击支付 `k = 1 + floor((today − dueDay) / cycle)` 期材料（`IndoorMaintenanceMath.CatchUpCycles`
  ＋ `CatchUpMaterialAmount`，all-or-nothing），`dueDay += k×cycle`、`cyclesMissed = 0`；材料不足 k 期时
  不扣任何料并回 `HDBF.Error.MaterialsMissing`。
* 【修复】未到期（k=0，例如同一天连点两下）直接禁用按钮并回 `HDBF.Error.MaintenanceNotDue`，不再多付一份料
  （L-08）；面板新增「本次会追平 k 期、共需 n 材料」行。
* 【修改】面板「逾期周期」与停摆状态改由即时快照计算，不再出现「逾期 0 + 红色停摆」自相矛盾；三语
  `Tooltip.Maintenance` / `MaintenanceShutdown` 改为追平语义。

### 【新增】每栋农场的自动维护开关（WP-02 / RE-02 / RE-07）

* 【新增】`IndoorFarmRecord.AutoMaintenance`（持久化，旧存档缺栏位 → 安全预设 true，并记 `maintenance.auto-default`），
  预设来自新 config `features.autoMaintenance=true`（`data/HDBF_Config.json` + schema-2 codec + validator）。
* 【新增】每日 tick 在模拟前自动支付**与手动完全相同**的追平帐单（不改 §19.8 经济、不加动作成本）：
  成功写 `indoor.maintenance-auto`；单仓库库存不足整笔时不扣料、不推进并写 `indoor.maintenance-auto-missing`
  （面板沿用缺料红字，手动按钮仍可用）。Basic（无材料维护）与接电顶级（cycle=0）不动作。
* 【新增】面板维护卡新增 On/Off 开关（`HdbfFarmCommandService.TrySetAutoMaintenance`，持久化玩家选择）。

### 【修复】休耕→常规的第一个日 tick 吃掉整盒成熟作物（WP-03 / L-01）

* 【修复】切回常规的转换日，`ModeTransitionYieldScale(Normal, Fallow)` 因休耕产量 0 而回传 0；旧 `TryHarvest`
  在 `amount == 0` 时仍清空槽位并记「收获成功」→ 整盒作物无声消失。现在 `TryHarvest` 在零产量时**不清槽**
  （视为跳过，动作不消耗），且转换 scale 在「上一模式产量非正（休耕）」时回 1。

### 【修复】暂停残留：暂停存档载入后工人仍留在被封鎖農場（WP-04 / RE-04 / L-13）

* 【修复】新增 `IndoorPauseReconcileService`（`IUpdatableSingleton`，约 1 Hz）持有暂停农场清单，载入完成后持续
  reconcile 残留 enterer（R1），并在暂停中订阅原版 `EntererAdded`，走进被封锁农场的人立即撤出（R2）。
* 【修改】撤出改用原版 `UnreserveSlotAndExit()`（清 reservation，R4），撤出前检查 `IsInside && CurrentBuilding == enterable`
  （跳过半状态 enterer，R3）；每趟记 `indoor.paused.eject-summary`（requested/ejected/skipped/forced/remaining）、
  `eject-failed`、`residual-enterer`、`reconcile`。仍无法清除时用「`Abandon()` + `Remove()`」等价公开路径取代
  原版 private `ForceRemoveEveryone`（记 `indoor.paused.force-remove`）。
* 【修复】`Deactivate()`（拆除/失去 finished 状态）先撤出室内工人一輪（L-13），失败只记 Warn；仍 0 Harmony。

### 【修复】管道「已连接」语义：原因码而不是单一「未连接」（WP-05 / RE-05 / L-02）

* 【新增】Core `IIndoorPipeProbe` + `PipeProbe`（`Connected` / `NoPipeNetwork` / `NoPipeTank` / `TankDry` /
  `AdapterUnavailable` / `BindingFailed` / `PortOff` / `Unknown`）。Timber Pipes 端做**只读 census**（不抽水、
  不计三振），面板显示对应原因与专用 tooltip：**自动补水只从「接上管网的原版液体水塔（Small/Medium/Large Tank）」
  抽水；把管接在农场外壳不会生效，农场不必在管线旁。**（真实连通判定 `PipeRegistry.TryGetGraph` 留 pending。）
* 【修复】L-06：`UpdatePorts` 每日 tick 不再覆写玩家的管道/物流端口开关选择（之前「关了隔天自己又打开」）。
* 【说明】`HDBF.Port.Pipe` 改「Pipe network (auto-refill) / 管网（自动补水）」；「未连接」只保留给「没有 probe 可用」的旧路径，
  避免与「没装模组」共用同一句话。

### 【修改】水/闲置原因透明化（WP-06 / L-03 / L-05 / L-10 UI / L-17）

* 【新增】Core `IndoorIdleReasonMath`（Paused > NoWorkers > EntranceBlocked > OutputFull > WaterShortage >
  MaintenanceStalled > MaintenanceDue > Fallow）＋面板第一张卡顶部常驻状态列；缺水时写三條补水路徑
  （管网水塔 / 人工补水 ≤12 格 / 搬水货物）。
* 【修复】手动补水先算 `headroom`：只抽 `min(1/3 罐, headroom)`；headroom < 1 单位视为已满，不抽水、不扣 2 个动作
  （`ManualRefillOutcome.TankFull`，L-05）。
* 【修改】0 工人时覆盖率/生长效率显示「—」且不再全绿（L-10 面板部分；世界图标多状态仍 pending）。
* 【修改】L-17 三语文案：`Error.RefillUnavailable`、`Warning.Shutdown`/`AdapterFailed` 重复、`Panel.WaterShortage`
  （减產→生長變慢）、维护 tooltip、`Port.Pipe`/`Tooltip.PipeWater`。
* 【修复】L-07：维护材料 `Available()` 改为「单一库存最大 unreserved 量」（与 all-or-nothing 交易一致），
  面板不再出现「合计够、分散在两仓却按不下去」；缺料判定改用本次追平总帐单。
* 【测试】L1 **655/655**（新增 42 条：休耕转换 3、维护追平/自动维护、存档/配置往返、pipe probe 5、暂停契约 2、
  闲置原因矩阵、补水 headroom/TankFull、面板/本地化 contract）。
* 【说明】本轮未改存檔 schema 版本号（新栏位为向后兼容选填栏位）；未执行实机 fixture；未上传 Workshop。
  §7.2 的 A–K 实机场景仍 pending，回复玩家的诊断模板见 `docs/BUG_REPAIR_20261102.md` §7.3。

## [2.5.3] — 2026-10-29（修复：暂停建筑导致游戏崩溃）

### 变更（玩家回报：「it crash when you paused it」）

* 【修复】暂停农场时游戏可能崩溃：原版暂停按钮只做 `BlockableObject.Block` →
  `Workplace.OnObjectBlocked` → `UnassignAllWorkers` → `Worker.Unemploy`，**不会**把已经进入建筑的工人
  带出来；而 HDBF 是唯一让农民持续待在室内的农场（`WaitInsideIdlyWorkplaceBehavior`）。暂停后这些
  「已解除雇佣但仍在已封锁建筑内」的角色会撞上原版 Enterer/Enterable/Accessible 的不变量
  （`Enterer.Enter` "already inside"、`Enterable.Remove` "not inside"、
  `Accessible.UnblockedSingleAccess` 的 `Single()` 等），都属于未捕获异常。
* 【修复】农场现在完整接管原版暂停：订阅 `PausableBuilding.PausedChanged`；暂停时停止日 tick、连续水耗
  与世界警告图标，并用原版 `Enterer.Exit()` 把室内工人安全移出（逐个隔离，单个失败不影响暂停）；恢复时
  重新注册/恢复以上子系统。读档时若建筑本来就是暂停状态，激活阶段直接进入暂停态；离开 finished 态时解绑。
* 【说明】行为语义：暂停的农场真的停下（不再在暂停期间增长作物/继续日 tick），恢复后由原版工作系统自动
  补聘工人继续；不改存档 schema、不改面板契约、0 Harmony。
* 【测试】新增 `V2PauseContractTests`（6 条 L1 源码/日志键契约）；L1 613/613。

## [2.5.2] — 2026-10-29（面板：「生长中」= 种植槽内作物总数）

### 变更（玩家需求：有多少个农作物在种植槽中，就显示多少个正在生长的农作物）

* 【修改】「槽位」卡的「生长中」原本只统计 `Stage=Growing` 的槽（成熟待收的作物不计入）。每日收获受
  动作预算限制（2 工人 × 13 = 26/日），所以一盒 60 株成熟后同一 tick 只收得掉 26 株，面板会出现
  「生长中 26／可收获 34」——玩家看到盒子里明明有 60 株，却只显示 26 株在长。现在
  **「生长中」＝种植槽内已有作物的格数**（生长中＋待收；`IndoorFarmSnapshot.GrowingSlots +
  MatureSlots`），「可收获」保留为其中已成熟的细分，因此「空槽＋生长中＝槽位上限」重新成立。
* 【修改】「正在生长」卡每作物行的数字同步改为**该作物在槽内的总株数**与其中待收数
  （`IndoorCropStatus.TotalSlots` / `MatureSlots`），例如「胡萝卜 生长 60 · 待收 34」；
  总览列「已种植槽位 n/capacity」不变。
* 【说明】纯面板显示语义，不改模拟：`Stage`、生长/收获/预算、存档 schema 均不变，旧存档直接兼容。
* 【测试】`V2PanelContractTests` 更新槽位卡/作物行期望，并新增
  `TestGrowingCountIsEveryCropInThePlantingSlots`（35 生长＋25 待收 → 生长中 60、可收获 25、
  作物行「60 growing · 25 ready」）；L1 **607/607**、发布门禁 44 PASSED。
* 【实机】用作者存檔改寫成「35 生长＋25 待收」後載入：面板「已种植槽位 60/60」、
  「胡萝卜 生长 60 · 待收 9」（截图 `docs/in-game/repro52-panel.png`）；旧语意会显示 51，
  证明新规则生效。证据与善后记录见 `docs/VERIFICATION.md` §32。

## [2.5.1] — 2026-10-29（两个玩家 bug 修复：叠层农场不播种、600 槽农场只送出 50 个作物）

### 修复 1：叠放/升高层的农场永远不播种，「正在生长」一直为 0

#### 变更（玩家 bug 回報：「生長中」只在种植槽种满时才显示正在生长的数量）

* 【修复】玩家回報「面板的『生長中』只有在種植槽種滿農作物時才會顯示正在生長的農作物數量」。
  顯示/資料鏈本身沒有「種滿才顯示」的條件（第一格種下就出行，見 REQ-V2-UI-05 與 VERIFICATION §28），
  真正原因是**入口判定錯誤**：`HdbfBoxFarmController.ReadEntranceBlocked` 原本要求門口那格上有
  **Path 方塊物件**，但這只對「門口就在路面層」的農場成立。屋頂是真正的樓層（REQ-V2-BLD-04），
  疊在另一座盒子農場屋頂上的農場，其門口前方是**屋頂格**而不是 Path，於是被永遠判定「入口堵塞」，
  每日農事（補種）完全不執行 → 槽位永遠空、面板永遠 0，而地面上那座（門口有 Path）正常顯示，
  玩家於是看到「只有種滿的那座才顯示」。
* 【修改】入口判定改用**遊戲原版定義**（`Timberborn.Buildings.BuildingBlockedAccessible.IsBlocked`）：
  `INavMeshService.AreConnected(門口格, 門前台階格)`。導航網格連通即視為可通行（屋頂/樓層也算），
  真正被牆堵住的入口仍然判定堵塞並照舊暫停農事、顯示「入口堵塞」；REQ-V2-BLD-02 的
  「入口前方至少 1 格通路」因此對所有樓層都成立。
* 【依賴】控制器不再注入 `IBlockService`（唯一用途就是上述錯誤判定），改注入 `INavMeshService`
  （遊戲單例，Bindito 已綁定）。
* 【測試】L1 新增 2 條回歸：`V2GameplayContractTests.TestEntranceProbeUsesNavMeshConnectivityNotAPathObject`
  釘住入口判定必須用導航網格連通、不得再要求門口 Path；`V2CropReportTests.TestEveryTierReportsCropsFromTheFirstPlantedSlot`
  對四檔各驗證「只種 1 格也立即產出作物列」。L1 606/606、發布門禁 PASSED。

### 修复 2：600 槽农场收获后只送出 50 个作物

#### 变更（玩家 bug 回報：Steam Workshop 留言）

* 【修复】玩家回報「600 個玉米成熟後只有 50 個送進倉庫，其餘完全沒有運出」。根因不是搬運鏈壞掉，而是
  建築輸出庫存沿用原版農家的固定容量 **50**：本模組是**每日批量收穫**，50 的緩衝只夠收約 25 槽，
  之後所有槽位卡在成熟、`blockedByOutput` 一直累積，玩家只看到 50 個作物離開農場。
* 【修改】四檔 `SimpleOutputInventorySpec.Capacity` 改為**整盒一次收成的上限** `槽位上限 × 4`：
  基礎 240、中級 600、高級 1200、頂級 2400（4 = 最高原版作物基礎產量 3 × 密植 1.35 四捨五入；
  疲勞與模式過渡只會降低產量，因此任何一槽最多 4 件）。一盒收成永遠放得下，瓶頸只剩搬運，
  不會再出現「收不了、送不出」的卡死。
* 【修改】面板「輸出」列的容量改為依快照槽位上限推算的每檔容量（`HdbfIndoorPanelLimits.OutputCapacity`），
  不再寫死 50；`HdbfIndoorPanelLimits.OutputFill` 同步接受槽位上限。
* 【修改】`tools/validate` 的 inventory 檢查由「四檔同為 50、範圍 [20,100]」改為「每檔 == PlotCapacity × 4」；
  `V2BlueprintContractTests` 與 `V2PanelContractTests` 以同一條規則釘住資料/面板，資料再漂移會紅燈。
* 【測試】L1 契約更新（面板 12/600、藍圖容量 = 槽位 × 4）；全套 L1 **606/606 PASS**、`tools/validate` OK、
  `build.ps1` **RELEASE GATES: PASSED (44 checks)**。

## [2.4.2] — 2026-10-29（修复「正在生长」卡看不到 / 数量不够显眼）

### 变更（REQ-V2-UI-05）

* 【修复】玩家实机反馈「面板找不到正在生长卡、生長中的数量不会动态显示」：原因是该卡原本排在
  「槽位」卡之后，1080p 高度下 325 px 面板需要滚动才看得到。现在**卡片移动到「工人与效率」卡正下方**
  （位置 2），选中建筑即可直接看到。
* 【新增】卡片顶部新增**总览列**「已种植槽位 n / capacity」+ 平均生长进度条与百分比：数量与进度都由
  快照实时计算、4 Hz 脏标记刷新，种下第一格就出现；下方再列出每个作物一行。
* 【修改】本地化新增 2 个键（总览列标签/数值），三语 193 键集合一致。
* 【测试】`V2PanelContractTests` 更新卡片顺序与总览列断言；L1 601/601、44 条发布门禁 PASSED。

## [2.4.1] — 2026-10-29（面板「正在生长」实时作物卡）

### 变更（REQ-V2-UI-05）

* 【新增】面板新增「正在生长」卡片（排在 Slots 之后、Water 之前）：当前每个占用槽位的作物一行——
  作物名、生长中/待收槽数、平均生长进度条（成熟槽按 100 % 计入平均值）。卡片**从第一格种下就出现**，
  随既有 4 Hz 刷新实时递增，不再等种植槽种满才显示；无作物时显示明确的空状态行。
* 【新增】Core 纯函数 `IndoorCropReport.Build` + `IndoorCropStatus`：单趟扫描槽位聚合，按阵营作物表顺序
  稳定输出（未知作物 id 追加并按 ordinal 排序），非有限数值按 0、成熟按 100 %，坏 spec/坏 catalog 只降级
  该行，不影响其他行或面板（REQ-V2-SAFE-01）。
* 【修改】`IndoorFarmSnapshot` 增加 `Crops` 初始化属性（位置参数不变，既有构造点无需改动）；面板指纹纳入
  作物行数据（按 1 % 量化），数值变化才重绘，空闲面板仍不做格式化/写入。
* 【修改】三语新增 4 个本地化键：卡片标题、逐作物计数值、空状态、悬浮说明（键集三语一致）。
* 【测试】新增 `V2CropReportTests`（聚合、成熟=100 %、稳定排序、未知作物、损坏值、行数上限、确定性），
  更新 `V2PanelContractTests` 的卡片顺序与快照字段契约。L1 601/601、`tools/validate` RESULT: OK、
  `build.ps1` 44 条 release gates PASSED；实机 round57 证据见 `docs/VERIFICATION.md` §28。

## [2.4.0] — 2026-10-28（即时补水 + 农夫在室内工作）

### 变更（REQ-V2-WATER-02/04, REQ-V2-LABOR-03, REQ-V2-PERF-02）

* 【修改】补水即时生效：点击「补水」时不再排队等下一个每日 tick，而是在同一指令内从已校验的
  真实水源取水并立刻加进水罐（仍消耗当日 2 个农事动作，且计入当日预留动作，绝不过支预算）；
  面板提示从「已请求补水」改为「补水完成」。
* 【修改】搬运来的水立刻入罐：`ApplyHauledWater` 现在也由连续水流服务每帧调用，工人刚搬进
  `Inventory:Water` 的水货物当帧变成水罐水量，不再等每日 tick 或面板刷新。
* 【修改】农夫在室内工作：农场模板新增原版 `WaitInsideIdlyWorkplaceBehavior`（简单工坊同款），
  在岗农夫会从入口进入建筑内部并在里面待岗，而不是在室外闲置游荡；室内种植/采收仍由模组的
  室内模拟完成（建筑为封闭 3×3 盒子，内部动作以模拟状态体现）。
* 【测试】`V2GameplayWiringTests` 增加即时补水/室内待岗/每帧入罐的源码契约；L1 全绿、
  44 条发布门禁 PASSED。

## [2.3.0] — 2026-10-28（水耗连续化 + 水罐有水时自动种满）

### 变更（REQ-V2-WATER-02, REQ-V2-LABOR-01, REQ-V2-PERF-02）

* 【修改】水耗由「每日 tick 一次性扣水」改为「按经过的游戏时间连续扣水」：
  `IndoorWaterMath.DemandForElapsedDays`（日需求 × 经过的游戏日）+ `Consume`（水罐不为负、
  修复损坏值）。Gameplay 新增 `HdbfWaterFlowService`（`IUpdatableSingleton`）每帧读取
  `IDayNightCycle.PartialDayNumber` 的增量并交给已完工农场；暂停不扣、倍速按比例、
  一整天恰好扣 `DemandPerDay`（帧率无关）。每日 tick 不再扣水，只修复/读取实时水罐并算覆盖率。
* 【修改】水罐有水时，空槽由工人自动种满、不再受每日 13 动作/人的播种预算限制（收获仍受预算
  限制，收获后同一 tick 即补种）；水罐没水则不播种。`IndoorLaborMath.RunActions` 新增
  `waterAvailable` 重载，旧的 6/8 参数重载保持原行为（向后兼容）。
* 【测试】更新 `V2WaterTests`（连续扣水/夹取/修复值）、`V2TickOrderTests`、`V2SlotFaultIsolationTests`
  （自动种满 37 槽）并新增连续扣水切片用例；L1 全绿、44 条发布门禁 PASSED。

## [0.2.1] — 2026-10-27（UI v2.4：按实机截图修正面板）

### 变更（REQ-V2-UI-01/02/03/04, REQ-V2-READ-04）

* 【删除】fragment 内的第二个头部（建筑图标+名称+档位·阵营+模式徽章）：原版 `EntityPanel` 已显示
  图标/名称/地区，重复头部连同其 NineSlice 空框与模式徽章一并删除；档位/阵营收进原版 header 下方的
  一行紧凑 subtitle row（`tier · faction`），不再重复建筑名。
* 【修复】棕色/米黄大底、深色文字、底部重叠：根因是 `AddToClassList("a b")` 只添加一个带空格的
  无效类名（面板因此退回默认深色文字/列布局），且透明 fragment 让 EntityPanel 的 tan 背景从卡片
  间隙与滚动条槽透出。现在所有多类名逐条添加（`HdbfPanelControls.AddClasses`）、卡片满宽且无间距、
  滚动条 `ScrollerVisibility.Hidden`、根容器透明。
* 【修复】全部面板文字统一暖白 `#F2EBDD`（`HdbfIndoorPanelStyle.LightTextRgba` +
  `HdbfPanelControls.ApplyLightText`；含原版 crop 下拉选中项与打开列表的重绘）；警告仍保留
  图标 + `▲/!` 文本标记 + 3 px 色条三编码。
* 【补回】水（水罐条/覆盖率 C 条 + C_min 标记/C_min/补水来源/缺水减产警告）、维护（到期日、逾期周期、
  材料需求行、缺料明细、停滞警告）、原版 RadioToggle 模式组 + 1 日过渡提示、输出占用原版 ProgressBar；
  输出端口与管道/物流/动力接口拆成独立行，全部文本可换行不再截断或重叠。
* 【修复】空缺工人 chip 使用原版 vacant 空头像（禁用、无警告三角）；⚠ 仅用于真实问题。
* 【新增】三语键 `HDBF.Panel.MaintenanceMaterialLabel` / `...MaterialValue`（enUS/zhCN/zhTW 同为 187 键）。
* 【测试】新增 `V2PanelVisualContractTests`（第二个 header/棕底/白色文字/ProgressBar/RadioToggle/
  按钮/端口分行/空缺 chip/三语键契约）；更新 `V2PanelContractTests`、`V2UiLiveWaterContractTests`、
  `V2LocalizationTests`。L1 `PASSED 569 / FAILED 0`；HDBF.UI/HDBF.Gameplay 0 warning / 0 error。
* 【修复·审计】原版进度条色调类 `progress-bar--green/red` 曾被与基类拼成一个字符串传给
  `AddToClassList`（整串变成无效类名，填充色不生效）；现按单类逐个添加，条形恢复原版绿/红填充。
* 【修复·审计】原版 crop 下拉的选中项与打开的下拉列表 Label 自带 `.dropdown-item__text` 灰色
  （弹出列表由单例 `DropdownListDrawer` 持有，不在 Dropdown 子树内）：选中项在每次
  `SetItems`/`UpdateSelectedValue` 后重新着白，弹出列表在 `Showed` 后经 panel 树重新着白；
  固定 28px 的列表行保持原版 nowrap/ellipsis（面板行本身仍换行，D/H 才真正达标）。
* 【修复·审计】`WorkerView` 模板缺失时 fallback chip 的姓名 Label 补上 `name="Name"`。
* 【修复·审计】`HdbfIndoorCommandBridge` 复用 4 Hz 查询的 invoke 参数数组；README 修正“空闲零分配”
  表述并登记 gameplay 侧每次查询新建快照的已知边界。
* 【测试·审计】新增 5 项回归（多类名直传 `AddToClassList`、进度条单类色调、下拉重绘、fallback chip、
  含引号逗号的 CSV 解析完整性）。
## [2.2.0] — 2026-10-27（美术/UI 修订；与 0.2.0 行为一致）

* **版本号对齐**：此前 mod 版本为 `0.2.0`，而美术/UI 修订在工程内部用 `v2.2` 命名，造成混淆；
  自本版起 mod 版本统一为 **2.2.0**，与「需求 v2.1 + 美术/UI 修订 2.2」对齐。存档/配置 schema 不变（2）。
* 【修改】美术：参考图驱动的材质分区/开窗比例/室内货架剪影（16 个 `.timbermesh` 重导出，128/128 校验）；
  四档图标从新模型重渲染（`data/Icons/BoxFarm*.png`）。
* 【修改】面板：改为原版 UXML/USS 控件；删除重复头部与棕/米黄大底；文字统一暖白 `#F2EBDD`；
  水罐/覆盖率/维护/动力/模式/策略/输出分区与按钮补齐；实时覆盖率（满罐=100%，不再载入显示 0%）。
* 【修复】材质/贴图：原 flat-patch UV 假设所有材质共享同一张 2048 atlas，实机出现棋盘拼布与
  黄黑警示条。改为**逐材质使用其真实原版 `_D` 贴图**的低方差 patch（`art/material_texture_map.json`
  记录材质→贴图→patch→mean/std），预览/图标改用同一真实贴图；禁止 `PaintedMetal` 用于大面积外壳。
  16 个 `.timbermesh` 重导出，验证器新增 C11–C14 + 负向对照；L1 577/577、44 gates PASS。
* 【修复】贴图清晰度：大面（木/板/石膏/金属/屋面）改为**原生分辨率 world-scale 平铺** UV
  （贴图 Repeat，按 `tile_meters` 控制每米纹素密度），玻璃/细节等 atlas 材质继续 patch；
  预览/图标使用同源真实贴图并加 50%/100% 近景；验证器新增纹素密度/UV 契约与 6 个负向对照。
  L1 585/585、44 gates PASS。
* 门禁：L1 564/564、`build.ps1` 44 checks PASS。

## [0.2.0] — 2026-10-26（v2.1 实现完成；实机覆盖范围见 VERIFICATION §26.3）

### 变更（相对 v1.0.0 / 0.1.0）

* 【作废】世界格扫描、托管格注册、工作范围（11×11…27×27）、地块高亮/框选、世界地块图标。
* 【作废】外部土壤水分采样与「灌溉覆盖率来自外部地块」的公式入口。
* 【作废】原版种植/收获任务派发与 `PlantingService` / `NaturalResourceFactory` 调用。
* 【作废】托管作物可见性补丁与 ADR-009 的 Harmony 生长补丁（v2 目标 **0 Harmony**，已达成）。
* 【修改】四档占地统一 **3×3×1**（原 3×2×1）；屋顶改为**平的、可通行、可叠放**。
* 【新增】室内槽位模型：60/150/300/600；`P_eff = min(Capacity, 在岗工人 × 38)`。
* 【新增】室内生长模拟：`G_growth = G_irr × G_maint × G_power`；疲劳/轮作/密植/休耕沿用 v1 数值。
* 【新增】内部水罐 + 管道/人工补水；覆盖率 `C` 与 `G_irr` 由纯函数计算，每日结算一次。
* 【新增】存档 schema v2（单条原生持久化负载 + 写前校验 + v1→v2 尽力迁移 + 失败安全默认）。
* 【修改】配置 schema **2**（v1 配置可读并迁移；未知键忽略并记日志）。
* 【修改】建造工时 **6/10/16/28 → 2/3/5/8 工日**（v2.1，只影响建造等待时间）。
* 【新增】§19.18 非功能需求：文档集、代码标记、日志键、模块化、扩展点、存档/崩溃安全门禁。
* 【新增】`docs/v2/` 文档集（README/ARCHITECTURE/INTERFACES/ADRS/TRACEABILITY/CHANGELOG）。

### 实现与门禁（本机实测，2026-10-26）

* 5 程序集 0 warning / 0 error；`L1 PASSED 529 / FAILED 0`；`tools/validate RESULT: OK`；
  `RELEASE GATES: PASSED (44 checks)`（0 Harmony）；`NFR GATE MATRIX: PASSED`。
* 100×600 日 tick 0.0113 ms/栋（预算 0.5）、0 bytes 配置；100×600 存档 1.05 MB（预算 1.5 MB）、
  编码 34.8 ms（预算 50 ms）；30/144 fps 结果一致。
* 3×3×1 蓝图（9 块、`Stackable: BlockObject`、`GenerateFloorsOnStackable`、建造 2/3/5/8 工日）；
  16 个 `.timbermesh` 全部 3.000×1.000×3.000、平屋顶、3 端口、无 slot 节点（128/128 校验 PASS）。
* 【修改】美术升级（参考图驱动）：四档材质分区（木框/玻璃分格/金属铆接/木补丁/苔藓）、开窗比例
  （Intermediate 侧墙玻璃约 45–55%）、室内静态货架剪影、齐平入口与后立面端口；16 个 `.timbermesh`
  重导出（LOD0 面数 5,850–5,918，预算内），128/128 美术校验 PASS，预览见 `art/previews/`。
* 实机（round41/42）：模组 0.2.0 加载、v2 记录读取/修复、室内日循环（每日一次水耗、覆盖率）、
  收获/播种/动作预算、输出满积压、缺 Timber Pipes 降级、配置 schema 2 加载。
* 未实机：真实建造/拆除、屋顶叠放 A/B、面板交互、人工补水正路径、维护材料扣料、顶级接电、
  游戏内存档往返、100 栋 Profiler、五模组矩阵、50% 四档同框 A/B（见 VERIFICATION §26.3）。

### 迁移说明（玩家）

* v1 存档：尽力迁移槽位/水罐/维护/模式/端口；**不删除**玩家原有地块与原版作物，
  旧农田恢复为普通原版农田；迁移失败时建筑以「空槽 + 满水罐 + 默认维护」安全载入并记警告。
* 已建 v1 农场建议拆除重建以获得 v2 行为（旧建筑不保证外观/端口一致）。

### 工程说明

* 构建工作根：本机沙箱下使用 `%TEMP%\hdbf-<tag>`（ADR-V2-005），不改变发布包结构。
* 发布门禁：`build.ps1`（5 程序集 0/0 → L1 全绿 → `tools/validate RESULT: OK` → 44 条 release gates）。

## 后续（未发布）

* 阶段 4 剩余实机证据（TC-V2-01 放置/拆除、TC-V2-02 屋顶叠放 A/B、TC-V2-05/06/07/08/09/10 的
  互动部分）逐条填入 `docs/VERIFICATION.md`；未实机项不得标「完成」。
