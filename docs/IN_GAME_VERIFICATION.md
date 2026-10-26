# 进游戏验证清单（用户实机，2026-10-24 深夜交接）

本环境（无交互桌面、游戏进程一启动就自退）无法完成游戏内验证。所有离线门禁已全绿，下面是你
明早开机后 15–20 分钟可以跑完的验证顺序；每条都给出“看什么/怎么算通过/失败时把什么发给我”。

## 0. 装载（30 秒）

1. 部署目录已更新：`%USERPROFILE%\Documents\Timberborn\Mods\HDBF`（303 KB，29 文件，5 DLL + 10 蓝图 + 3 本地化）。
   - 工作区 `Mods\HighDensityBoxFarm` **不是** mod（无 manifest），游戏会忽略它；不要把它复制成 mod。
2. 启动 Timberborn → Mods 界面确认 **High Density Box Farm 0.1.0** 已启用（若未启用手动启用）→ 重启游戏。
3. 打开 `%USERPROFILE%\AppData\LocalLow\Mechanistry\Timberborn\Player.log`，确认出现：
   - `[HDBF/Info] start HDBF version=0.1.0`
   - `[HDBF/Info] startup: HDBF 0.1.0 ready; tiers=4 ...`
   - **不应**出现 `InvalidDataException` / `Material ... not found` / `There are no "..." slots` / `[HDBF/Error]`。
   - 允许出现 `No manifest file found in ...\Mods\HighDensityBoxFarm`（那只是工作区目录，不是 mod）。

## 1. 放置与建造（P0，5 分钟）

新建一局（建议 Folktails, 平地地图），四档各建 1 座（研究点够的话用开发者模式，或直接建基础版）。

| 检查 | 通过标准 |
|---|---|
| 四档出现在 Food 分类末尾 | 名称/图标正确，未研究时锁定并按研究点扣费 |
| 3×2×1 占地、入口朝前 | 放置预览显示 6 格；能落位；入口前一格可通行 |
| 工地→完工 | 放下去**不再抛异常**（第 4 轮 `no "Entrance" slots` 崩溃必须消失） |
| 面板出现 | 选中建筑出现 HDBF 面板：工人、有效托管格、覆盖率、疲劳、维护、水罐、动力、模式、端口 |
| 派工 | 派 1 个农民，面板工人数变化；无人时面板有效格=0 |

失败时发我：`Player.log` 从 `[HDBF/Info] start` 到异常处的整段 + 建筑名。

## 2. P1 作物循环（本轮新接通，最关键）

1. 基础版放一块可种植的平地，周围有自然水源；派 2 个农民，等 1–2 个游戏日。
2. 看地里是否出现**真实原版作物**（胡萝卜/土豆等）在托管格上生长——不是凭空刷物品。
3. 成熟后看农民是否走去收获、建筑面板/库存里是否出现作物（SimpleOutputInventory 容量 50），
   随后是否有搬运工把作物搬去仓库。
4. 撤走全部农民 1 个游戏日：不应再有播种/收获；作物继续按原版生长；派回后恢复。
5. 基础版等 3–4 天：水罐应从满到见底，覆盖率下降，面板出现缺水/灌溉不足警告（P1）。
6. 切密植 1 天：面板提示产量/水耗/疲劳代价；再切休耕 1 天：停止农事、疲劳每日下降。

失败时发我：`HDBF/Error`/`HDBF/Warn` 的 `gameplay.farm` 行 + 截图（地块与面板）。

## 3. 维护 / 动力（P1，5 分钟）

1. 中级版（或高级版）：建仓库放 8 木板（高级放 6 金属），点面板「执行维护」→ 仓库库存应减少、倒计时重置。
2. 材料不足时点维护：按钮应给出“缺材料”原因，不扣库存、不重置计时。
3. 吊着不维护 1 个周期：减产警告；连续 2 个周期：停摆（停止播种/收获，作物继续长），维护一次即恢复。
4. 顶级版：接电网后动力=已供电（G_power=1.0，后侧接口）；拆掉电后应转离网（0.6，15 金属/8 日），面板给出断电提示。

失败时发我：面板截图 + `Player.log` 里 `[HDBF/` 行。

## 4. 存档安全（P0，3 分钟）

保存 → 读档 → 再保存 → 再读档，检查：建筑存在、托管格数、疲劳、维护倒计时、水罐、模式、端口状态不丢；
日志无 `[HDBF/Error] save...`。把 `Saves\<settlement>\` 里的存档名发我即可（不用发文件）。

## 5. 一键复验（可选，随时可跑）

```powershell
# 只读检查：部署完整性 + 历史日志分析（不发车、不碰存档）
powershell -NoProfile -ExecutionPolicy Bypass -File tools\smoke\Invoke-GameSmoke.ps1

# 想复现“自动加载存档”的安静尝试（会复制存档到 Saves\HDBFTest，结束自动删除；本机已证明会自退，仅留证据）
powershell -NoProfile -ExecutionPolicy Bypass -File tools\smoke\Invoke-GameSmoke.ps1 -Launch -BatchMode
```

## 6. 已知未完成（不是 bug，是记录在案的缺口）

- 美术：仍是阵营正确的原版占位模型，`.timbermesh` 需要 Unity 工具链（ADR-003）。
- 托管格高亮/框选（REQ-UI-05 的渲染层）未实现；数据契约已就绪。
- 第 7 章科技前置链：1.1.2.8 没有 research node，只有 `ScienceCost` 门禁（偏离 D-3）。
- 五个第三方模组：本机未安装，适配器只验证了“缺失时安全降级”。
- 灌溉/疲劳/维护/动力对原版作物生长速度的“硬惩罚”需要私有 API 补丁，v0.1 用“派发门禁 + 生长通道”
  （偏离 D-2）；实机若觉得产量偏高/偏低，先记录数据，我们再决定是否取 1 个 Postfix。

把任何失败日志（`Player.log`）和截图放到工作区 `docs/in-game/` 下，下一轮自动化会按这份清单逐项复验并修。
