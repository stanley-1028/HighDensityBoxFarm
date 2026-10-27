# HDBF v2 变更记录

> **状态**：2.2.0 已实现（离线门禁全绿；实机覆盖范围见 VERIFICATION §26.3）
> **日期**：2026-10-26
> **适用版本**：2.2.0
> **变更摘要**：v2.1 需求引入室内农场方案；本文件记录 v1→v2 的行为/数据/工程变更与实现状态。

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
