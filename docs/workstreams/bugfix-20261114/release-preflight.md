# HDBF 2.6.1 发布预检（只读）— GitHub push + Steam Workshop 上传

> 生成：2026-11-14 21:28–21:38（Asia/Macau，UTC+8），执行者：release-preflight（只读预检）。
> 本文件是**清单**，不是发布记录；预检期间**未执行** push / upload / 备份复制 / 删除 / 任何 git 写操作。
> 观测值均带时间戳。**警告**：预检期间另有会话正在工作区构建/实机验证（`stage` 于 21:32–21:34 被重建），
> 工作区仍在变动；执行发布前必须按本文件 §3 重新核对一遍，不要直接沿用本文的瞬时数值。

---

## 0. 结论：现在能不能发？

**流程可执行，但当前工作区/暂存区不满足上传条件。** 发布前必须消除 4 个阻塞项：

| # | 阻塞项 | 证据（2026-11-14 21:3x 实测） | 处理 |
|---|---|---|---|
| B1 | TEMP 实机验证 harness 仍在源码，且**已进入当前 stage 的 DLL** | `stage\HDBF\HDBF.Gameplay.dll` 含 `HdbfUiVerifyDriver`；`stage\HDBF\HDBF.UI.dll` 含 `VerifyCropsRebuild`/`VerifyStackRebuild` | 删除 2 个未跟踪文件 + 13 处标记（11 处 C# 调用 + `HDBF.Gameplay.csproj` 2 处引用），再重建（§3 步骤 1） |
| B2 | 修复未提交；版本号 2.6.1 只存在于工作区 | `git status --short`：10 个 `M` + 2 个 `??`；`data/manifest.json` 与 `tools/validate/DataChecks.cs` 已改为 2.6.1 但未 commit | 清理 harness 后提交（§3 步骤 11） |
| B3 | 当前 stage 是「2.6.1 manifest + 含 harness 的构建」 | stage：48 files / 2,216,230 B / manifest `Version=2.6.1`，但 DLL 含 harness 字符串 | 必须从干净源码重建；**不得上传当前 stage**（§3 步骤 4–5） |
| B4 | 游戏正在运行 | `Timberborn` PID 3936（21:33 实测）；Steam PID 7608 运行中 | 覆盖本机包/上传前请玩家正常退出游戏；Steam 保持运行并登录（§3 步骤 4/7） |

**上传硬门槛（不符合就不许上传）**：

1. staged 包（固定 `%USERPROFILE%\.hdbf\work\stage\HDBF`）的 `manifest.json` 必须精确是 `"Version": "2.6.1"`；
2. 5 个 DLL + manifest 的 SHA-256 已算出并写入发布记录；
3. staged DLL 逐字节扫描：`HdbfUiVerifyDriver` / `VerifyCropsRebuild` / `TEMP verification harness` / `diag.` 全部 0 命中；
4. 构建使用**固定默认 work root** `%USERPROFILE%\.hdbf\work`，禁止 `-WorkRoot $env:TEMP\...`（见 §5 R1）。

---

## 1. GitHub 现状

### 1.1 实测值（2026-11-14 21:29–21:36 +0800）

| 项 | 值 | 证据命令 |
|---|---|---|
| 远端 | `https://github.com/stanley-1028/HighDensityBoxFarm.git`（fetch/push 同 URL） | `git remote -v` |
| 分支 | `master`（唯一本地分支，无其它 worktree） | `git branch -vv` / `git worktree list` |
| 本地 HEAD | `b74f0f889078d2b0a8e881be144c3e0da2732b27`（2026-11-14 10:10:01 +0800，`docs+workshop: 2.6.0 page copy ..., v2.6.0 tag`） | `git rev-parse HEAD` |
| **远端 master 实际值** | `b74f0f8…` = 本地 HEAD → **未推送提交 = 0** | `git ls-remote origin refs/heads/master` |
| 本地 `origin/master` 跟踪引用 | `65dc637…`（**陈旧**，因为没有 fetch） | `git rev-parse refs/remotes/origin/master` |
| `git log origin/master..HEAD` 输出 | 4 条（`b74f0f8`、`c53b372`、`2d59ba9`、`68d7ed1`）——**是跟踪引用陈旧造成的假象，不是真未推送** | `git log origin/master..HEAD --oneline` |
| 本地/远端 tag | `v2.6.0`（annotated `972be04` → commit `68d7ed1`）、`v2.5.1`（lightweight `40b112e`）、`v2.4.0`（annotated `56c3458` → `c967ce2`） | `git tag --sort=-creatordate` / `git ls-remote --tags origin` |

> ⚠ 判断「有没有未推送」**只能以 `git ls-remote` 为准**；本仓库的 `origin/master` 跟踪引用长期不 fetch 会误报。

### 1.2 tag 规则（从现有 3 个 tag 反推）

- 命名：`vMAJOR.MINOR.PATCH`（SemVer），如 `v2.6.0`。
- 只给**里程碑发布**打 tag：`v2.4.0`、`v2.5.1`、`v2.6.0` 存在；2.5.2–2.5.11 期间共 9 次已发布补丁（2.5.5 未发布）**都没有 tag**。
- 形式：`v2.4.0`/`v2.6.0` 是 **annotated tag**，message 里带 Workshop `ManifestID`；`v2.5.1` 是 lightweight。
- 2.6.1 建议照 2.6.0：**annotated** `v2.6.1`，打在本轮「版本号 bump + 修复」的 commit 上（不是 docs/发布记录 commit），
  message 形如 `HDBF 2.6.1: ... (Workshop ManifestID <新ManifestID>)`。是否打 tag 由 Lead 决定（补丁版历史上可打可不打）。

### 1.3 上次发布（2.6.0）记录

| 项 | 值 |
|---|---|
| 上传时间 | 2026-11-14 08:38:08–08:38:15（Asia/Macau） |
| ManifestID | `4848736136601558932` |
| 修复/版本 commit | `68d7ed1`（manifest 2.5.11→2.6.0、UI 原版化+折叠、L1 733/733、44 gates；message 带 ManifestID） |
| tag | `v2.6.0`（annotated `972be04` → `68d7ed1`，已推送到远端） |
| push 报告 | `65dc637..68d7ed1 master -> master`（VERIFICATION §53.1），其后 docs commit `2d59ba9`/`c53b372`/`b74f0f8` 继续 push；远端 master 现为 `b74f0f8` |
| 发布记录位置 | `docs/VERIFICATION.md` §53/§53.1/§53.2/§53.3；`docs/workshop/README.md` 末尾「發佈記錄（2026-11-14，2.6.0）」 |

### 1.4 push 命令模板（由 Lead 执行；本预检不执行）

```powershell
Set-Location 'C:\Users\user\Documents\Timberborn\Mods\HighDensityBoxFarm'
git rev-parse HEAD
git ls-remote origin refs/heads/master          # 先核对远端=本地，再 push
git push origin master
git push origin v2.6.1                          # 若打 tag
# 验证（push 后必须再做一次）
git ls-remote origin refs/heads/master refs/tags/v2.6.1
git status --short                              # 期望：clean
```

失败处理：push 被拒（non-fast-forward）→ **不许 force push**；先确认远端是否已含本轮 commit（可能只是本地跟踪引用陈旧），必要时人工合并后再推，并记录到 VERIFICATION。

---

## 2. Steam Workshop 现状

### 2.1 上传工具：真实路径 / 命令行模式（读源码核实）

| 项 | 实测值 |
|---|---|
| exe 真实路径 | `C:\Users\user\hdbf-tools\steamupload\bin\Release\net9.0\SteamUpload.exe`（已 `Test-Path` 存在；同目录还有 `SteamUpload.deps.json`、`SteamUpload.runtimeconfig.json`、`steam_appid.txt`） |
| 源码 | `C:\Users\user\hdbf-tools\steamupload\Program.cs`（218 行，2026-11-14 10:05 修改；本次已读） |
| 工程/构建 | `SteamUpload.csproj`（net9.0，Steamworks.NET）；**未入 repo**，只在本机 `hdbf-tools` |
| AppID / item | 硬编码常量：`AppIdValue = 1062090`（Timberborn）、`WorkshopItemId = 3828809784`（High-Density Box Farm）→ **不可能误传到其它 item 或新开 item** |
| 依赖状态 | 本机 2026-11-14 10:05 已成功跑过 `--description` 模式（Steam 日志有记录），运行时可用 |

**CLI 模式（源码 usage 行逐字核实，`Program.cs:37`）**：

```text
usage: SteamUpload --probe | SteamUpload <contentFolder> <changeNoteFile> | SteamUpload --description <descriptionUtf8File>
```

- **内容上传（本次要用的模式）**：`SteamUpload.exe <contentFolder> <changeNoteFile>` —— **两个位置参数，没有 `--content` / `--changenote` 这类命名参数**。
  - `contentFolder` = staged 包目录（`…\stage\HDBF`），内部必须直接是 `manifest.json` + 5 DLL + 数据文件（不是外层 `stage`）；
  - `changeNoteFile` = UTF-8 Change Note 文本（程序 `File.ReadAllText(..., Encoding.UTF8).Trim()` 后作为 `SubmitItemUpdate` 的说明）；
  - 程序只调用 `SteamUGC.StartItemUpdate` → `SetItemContent` → `SubmitItemUpdate(handle, note)`，**不调用任何** SetItemTitle/SetItemDescription/SetItemPreview/SetItemTags/SetItemVisibility → **天然不会改标题/描述/封面/标签/可见性**；
  - 进度轮询上限 **25 分钟**；退出码：`0`=成功；`2`=SteamAPI.Init 失败；`3`=无效 handle；`4`=SetItemContent 失败；`5`=无效 call；`6`=超时；`7`=提交结果非 OK/IO 失败。
- `--probe`：只初始化 Steam 并打印 `steamRunning=… loggedOn=… steamId=…`，不提交任何东西；**上传前应先跑**。
- `--description <file>`：metadata-only 模式（只 `SetItemDescription` + `SubmitItemUpdate`，内容不动）。注意它内部硬编码了一条 2.6.0 的 submit 说明；**本次 2.6.1 不要跑这个模式**（本次无页面文案变更）。
- 运行目录：程序会在**当前工作目录**自动写 `steam_appid.txt`（`EnsureAppIdFile`），并设 `SteamAppId/SteamGameId`；exe 同目录（`…\bin\Release\net9.0`）与工具根目录都有 `steam_appid.txt`（内容 `1062090`）。**照 2.5.11 先例：在 exe 同目录启动**。

**实际成功调用的日志证据（2026-11-09 2.5.11，来自 `%USERPROFILE%\.hdbf\tmp\analysis\steamupload-2511.log`，UTF-16 保存；已读）**：

```text
cwd=C:\Users\user\hdbf-tools\steamupload\bin\Release\net9.0
& ".\SteamUpload.exe" $stage "C:\Users\user\hdbf-tools\change-note-2.5.11.txt"
loggedOn=True ...
content=C:\Users\user\.hdbf\work\stage\HDBF
status=k_EItemUpdateStatusPreparingConfig 0/0
status=k_EItemUpdateStatusPreparingContent 2204337/2204337
status=k_EItemUpdateStatusUploadingContent 453997/453997
status=k_EItemUpdateStatusCommittingChanges 0/0
submitDone ioFailure=False eResult=k_EResultOK needsLegalAgreement=False publishedFileId=3828809784
```

→ 确认：两个位置参数 = `$stage`（`%USERPROFILE%\.hdbf\work\stage\HDBF`）+ Change Note 文件路径；cwd = exe 同目录；进度状态先 `PreparingConfig` → `PreparingContent <pkgsize>` → `UploadingContent <uploaded>` → `CommittingChanges`；成功后最后一行是 `submitDone …`。2.5.11 用的 Change Note 是 `hdbf-tools\change-note-2.5.11.txt`（与 `~/.hdbf/release-2511-changenote.txt` 同为 2,621 B），2.6.1 用绝对路径传 repo 内文件即可。

### 2.2 Steam 日志

- 路径：`C:\Program Files (x86)\Steam\logs\workshop_log.txt`（AppID 1062090）。
- 2026-11-14 实测最近记录（`Get-Content -Tail`）：

```text
[2026-11-14 08:38:08] [AppID 1062090] Upload starting for workshop item 3828809784 by AppID 1062090
[2026-11-14 08:38:15] [AppID 1062090] Uploaded new content ( ManifestID 4848736136601558932 ) for item 3828809784.
[2026-11-14 08:38:15] [AppID 1062090] Upload finished for workshop item 3828809784 : OK
[2026-11-14 08:46:03] [AppID 1062090] Detected workshop change (latest from server) : changed cached item 3828809784, new manifest 4848736136601558932
[2026-11-14 10:05:39] [AppID 1062090] Upload starting for workshop item 3828809784 by AppID 1062090
[2026-11-14 10:05:39] [AppID 1062090] Upload finished for workshop item 3828809784 : OK
```

> 10:05 那条是**metadata-only**（`--description`）：只有 start/finish，**没有** `Uploaded new content ( ManifestID … )` 行。2.6.1 内容上传必须看到 `Uploaded new content` 行，否则视为未成功。

### 2.3 备份既有路径与命名（实测存在）

- 根目录：`C:\Users\user\hdbf-tools\workshop-backup\`（**没有** `%USERPROFILE%\.hdbf\workshop-backup`，已 `Test-Path` 确认不存在）。
- 现有目录：

| 目录 | 内容 |
|---|---|
| `3828809784-2.5.8` / `-2.5.9` / `-2.5.10` / `-2.5.11` | 各旧版本 Steam 内容包（48 files） |
| `3828809784-2.6.0` | **已发布 2.6.0 的干净内容包**：48 files / 2,208,034 B / manifest 2.6.0 / `manifest.json` SHA-256 `3D4FA158D852C5EBF75337FF3FDDC2C21920F0E6DC20076C5814A29B4FB4E6AF` |
| `HDBF-local-2.5.11` | `Documents\Timberborn\Mods\HDBF` 的 2.5.11 本地包备份 |

- 命名惯例：Steam 内容 = `3828809784-<旧版本>`；本地 Mods = `HDBF-local-<旧版本>`。
- 仓库内还有页面元数据备份：`docs/workshop/publish/`（`manifest.json` 6005 B / `thumbnail.png` 486,059 B / `workshop_data.json`）。
- 2.6.1 发布前**不需要重新备份 2.6.0**（`3828809784-2.6.0` 已是干净回滚点）；**不要**把当前本机 content 目录（含 harness 测试 DLL，见 2.4）另存为 "2.6.0" 备份，会污染回滚点。

### 2.4 本机三份包的状态（21:29–21:34 快照，均非发布态）

| 位置 | files/bytes | manifest | 判定 |
|---|---|---|---|
| `%USERPROFILE%\.hdbf\work\stage\HDBF`（21:34 被并发会话重建；21:36 复核） | 48 / 2,216,230 B | `2.6.1` | ❌ **含 TEMP harness**（`HdbfUiVerifyDriver` in Gameplay.dll / `VerifyCropsRebuild`+`VerifyStackRebuild` in UI.dll；byte-scan 实测命中）→ 不可上传 |
| `…\Steam\steamapps\workshop\content\1062090\3828809784` | 48 / 2,212,642 B | 6005 B（2.6.0 旧 description） | ❌ 混合态：manifest 是 2.6.0，但 `HDBF.Gameplay.dll` SHA `0BCF7153…`、`HDBF.UI.dll` SHA `A321FA5D…` 是含 harness 的测试构建 |
| `%USERPROFILE%\Documents\Timberborn\Mods\HDBF` | 48 / 2,208,034 B | 6005 B（2.6.0） | ✅ 已发布 2.6.0 干净包（`HDBF.Gameplay.dll` SHA `3109ED09…`，与 VERIFICATION §52.1 记录一致） |

> 结论：**除了远端已发布的 2.6.0，本机没有任何一份可用的「2.6.1 干净包」**——必须重新构建。

### 2.5 线上工坊页（抓取成功，2026-11-14 21:31–21:32 +0800）

- URL：<https://steamcommunity.com/sharedfiles/filedetails/?id=3828809784>（HTTP 200）；Change Notes 页：<https://steamcommunity.com/sharedfiles/filedetails/changelog/3828809784>（HTTP 200）。
- 标题：`High-Density Box Farm — 3x3x1 Indoor Stackable Farm`
- `Posted`：`28 Oct @ 4:33am`；**`Updated`：`13 Nov @ 4:38pm`**（Steam 页面显示时区，= 2026-11-14 08:38 Asia/Macau 的 2.6.0 上传）
- **`File Size`：`2.208 MB`**（= 已发布 2.6.0 staged 2,208,034 B）
- **`Change Notes`：14 条**（changelog 页 "Showing 1-10 of 14 entries"；最新一条 = `Update: 13 Nov @ 4:38pm` 的 2.6.0 三语说明）
- 页面 Description 已含 2.6.0 文案（`Vanilla-style panel with foldable sections` bullet + 中文简介「2.6.0 起面板全面改用游戏原版控件…折叠条（▲）」）→ 2.6.0 的 metadata-only 更新已生效。
- 社交数据（顺带记录）：2,592 Unique Visitors / 920 Current Subscribers / 37 Favorites；44 Comments。
- 最新玩家评论（约 5 小时前，litos5599）：「i cant change the crop production, the crop selector closes all the time…」——**正是 2.6.1 要修的 bug**；作者已回复会修。发布后可在该评论下回复（可选，见 §6）。

---

## 3. 发布 2.6.1 步骤清单（可复制执行）

> 全部命令在 `C:\Users\user\Documents\Timberborn\Mods\HighDensityBoxFarm` 执行（步骤 7 例外，会切到工具目录）。
> 每一步的「验证点」不通过就停，**不要跳到下一步**。

### 步骤 0 — 冻结与前置检查

```powershell
Set-Location 'C:\Users\user\Documents\Timberborn\Mods\HighDensityBoxFarm'
git rev-parse HEAD
git status --short
git ls-remote origin refs/heads/master
Get-Process Timberborn -ErrorAction SilentlyContinue    # 期望空输出
Get-Process steam -ErrorAction SilentlyContinue         # 期望有 steam
```

- 验证点：HEAD 记录在案；无并发 build（`Get-Process dotnet,powershell` 里没有 `build.ps1`）；游戏已退出；Steam 运行。
- 失败处理：有并发构建/实机 → 等待其结束再继续（stage 会被 build.ps1 先删后建，不能同时跑）。

### 步骤 1 — 移除 TEMP 实机验证 harness（发布阻塞项）

```powershell
Remove-Item 'src\HDBF.Gameplay\Components\HdbfUiVerifyDriver.cs' -Force
Remove-Item 'src\HDBF.UI\Panels\HdbfFarmPanelFragment.Verify.cs' -Force
# 手工删除 13 处 "// TEMP verification harness" 标记（11 处 C# 调用 + 2 处 csproj 引用；行号见 §附录A.1）：
#   HdbfFarmQueryService.cs:90
#   HdbfGameConfigurator.cs:48
#   HDBF.Gameplay.csproj:37（ProjectReference HDBF.UI）、:191（Reference Timberborn.DropdownSystem）
#   HdbfFarmPanelFragment.Cards.cs:158, :172
#   HdbfFarmPanelFragment.Crop.cs:61, :120, :125
#   HdbfFarmPanelFragment.cs:209, :238, :351
#   HdbfFarmPanelFragment.Render.cs:35
# 删除后验证：
Get-ChildItem src -Recurse -File -Include *.cs,*.csproj |
  Select-String -Pattern 'TEMP verification harness|HdbfUiVerifyDriver|VerifyCropsRebuild|VerifyDropdownCreated|VerifySetItems|VerifyGuardSkip|VerifyRender'
```

- 验证点：上一条命令 **0 行**；且 `git diff v2.6.0 -- src/HDBF.Gameplay src/HDBF.Core src/HDBF.Persistence src/HDBF.Integration` 为空
  （真正的修复只改 `src/HDBF.UI`：`HdbfFarmPanelFragment.cs`、`HdbfFarmPanelFragment.Crop.cs`、`HdbfFarmPanelFragment.Cards.cs`）。
- 失败处理：还有命中 → 逐个删；`Gameplay.csproj` 若保留了对 `HDBF.UI`/`Timberborn.DropdownSystem` 的引用，会破坏分层且把 harness 带进包。

### 步骤 2 — 确认版本号两处同步（工作区已改，未提交）

```powershell
Select-String -Path data\manifest.json         -Pattern '"Version"\s*:\s*"([^"]+)"'
Select-String -Path tools\validate\DataChecks.cs -Pattern 'ExpectedReleaseVersion'
git diff -- data/manifest.json tools/validate/DataChecks.cs   # 只应改这两个值（+ BOM 差异）
```

- 验证点：两处都是 `2.6.1`；manifest 其它字段（Name/Id/Description/MinimumGameVersion）**未动**。
- 失败处理：漏改 `DataChecks.cs` → `tools/validate` 会提示 pinned version 不符；两处必须一起 bump（注释写明 warning-only，但发布记录要求 `WARNINGS: none`）。

### 步骤 3 — 写 2.6.1 Change Note（格式见 §4）

```powershell
$noteRepo = 'docs\workshop\changenote-2.6.1.txt'          # 建议的正式路径（随 repo 跟踪，2.6.0 先例）
$noteHist = "$env:USERPROFILE\.hdbf\release-261-changenote.txt"  # 历史惯例的镜像路径
# 编写/粘贴内容到 $noteRepo 后：
Copy-Item $noteRepo $noteHist -Force
Get-Content $noteRepo -Raw -Encoding UTF8 | Select-Object -First 1   # 首行应为 "HDBF 2.6.1 - ..."
(Get-Item $noteRepo).Length                                          # 建议 < 4 KB（2.6.0 为 2,306 B）
```

- 验证点：首行是 `HDBF 2.6.1`；含 `[EN]`/`[ZH]`/`[TW]` 三节（见 §4.3）；UTF-8（不要 UTF-16）；无凭据/无绝对路径。
- 失败处理：编码错（Steam 上会乱码）→ 用 `Set-Content -Encoding UTF8` 重写或从 §4.3 骨架重新粘贴。

### 步骤 4 — 干净构建（固定默认 work root；必须关游戏）

```powershell
Set-Location 'C:\Users\user\Documents\Timberborn\Mods\HighDensityBoxFarm'
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 2>&1 |
  Tee-Object -FilePath "$env:USERPROFILE\.hdbf\work\build-261.log"
"exit=$LASTEXITCODE"
```

- **不要**传 `-WorkRoot`（默认即 `%USERPROFILE%\.hdbf\work`）；**不要**用 `$env:TEMP`（见 §5 R1）。
- 验证点（在 build-261.log 里逐条核对）：
  - 5 个程序集 `0 warning / 0 error`；
  - L1 `PASSED N FAILED 0`（2.6.0 为 733；本轮新增 1 条契约，预期 ≥734，以实际为准）；
  - `tools/validate` → `RESULT: OK`（发布记录惯例还要求 `WARNINGS: none`）；
  - `RELEASE GATES: PASSED (44 checks)`；
  - 末尾 `Staged mod package: C:\Users\user\.hdbf\work\stage\HDBF`，exit 0。
- 失败处理：任一 FAIL → 按 `docs/OPS.md` §8「常见门禁失败与处理」修因；**不许为省时间用 `-SkipTests` 发版**。

### 步骤 5 — staged 包硬核对（不通过绝不上传）

```powershell
$stage = "$env:USERPROFILE\.hdbf\work\stage\HDBF"
$mf = Get-Content "$stage\manifest.json" -Raw -Encoding UTF8 | ConvertFrom-Json
"Version=$($mf.Version)  Id=$($mf.Id)  Name=$($mf.Name)"
if ($mf.Version -ne '2.6.1') { throw "staged manifest Version=$($mf.Version), expected 2.6.1" }

$files = Get-ChildItem $stage -Recurse -File
"files=$($files.Count)  bytes=$(($files | Measure-Object Length -Sum).Sum)"
if ($files.Count -ne 48) { throw "staged file count=$($files.Count), expected 48" }

# 5 DLL + manifest 的 SHA-256：粘进发布记录/commit message
$dlls = $files | Where-Object { $_.Extension -eq '.dll' } | Sort-Object Name
$dlls | Get-FileHash -Algorithm SHA256 | Format-Table Hash, Path -AutoSize
(Get-FileHash "$stage\manifest.json" -Algorithm SHA256).Hash

# byte-scan：harness 与 dev 诊断字符串必须 0 命中
$bad = @('HdbfUiVerifyDriver','VerifyCropsRebuild','VerifyStackRebuild','TEMP verification harness','verify-auto-select','diag.')
foreach ($f in Get-ChildItem $stage -Filter *.dll) {
  $bytes = [IO.File]::ReadAllBytes($f.FullName)
  $ascii = [Text.Encoding]::ASCII.GetString($bytes)
  $utf16 = [Text.Encoding]::Unicode.GetString($bytes, 1, $bytes.Length - 1)
  foreach ($t in $bad) {
    if ($ascii.Contains($t) -or $utf16.Contains($t)) { throw "$($f.Name) contains '$t' -> NOT releasable" }
  }
}
'staged package checks PASS'
```

- 验证点：全部 PASS；同时确认 `git diff v2.6.0 -- src/HDBF.Gameplay src/HDBF.Core src/HDBF.Persistence src/HDBF.Integration` 为空（UI 之外的程序集不应有任何差异）。
- 失败处理：version/count/字符串任一不符 → **回去重建**，不要「先传看看」。把此步输出（SHA 表 + bytes）存档，发布记录要抄。

### 步骤 6 — 确认回滚点（不复制、不删除）

```powershell
$bk = "$env:USERPROFILE\hdbf-tools\workshop-backup\3828809784-2.6.0"
(Get-ChildItem $bk -Recurse -File).Count                                       # 期望 48
(Get-FileHash "$bk\manifest.json" -Algorithm SHA256).Hash                      # 期望 3D4FA158D852C5EBF75337FF3FDDC2C21920F0E6DC20076C5814A29B4FB4E6AF
(Get-FileHash "$bk\HDBF.Gameplay.dll" -Algorithm SHA256).Hash                  # 期望 3109ED09274454CC715486CF92B14BB38EDA564388AF2760DADBA7A0C11C1D89
```

- 验证点：三项与上表一致 → 2.6.0 干净回滚点可用；若不一致，先补一份干净备份再继续（备份动作由 Lead 执行）。
- 失败处理：备份损坏 → 从 `Documents\Timberborn\Mods\HDBF`（干净 2.6.0）重新复制一份到 `3828809784-2.6.0`。

### 步骤 7 — 上传（Steam 必须运行且已登录；内容 + Change Note）

```powershell
$exe   = 'C:\Users\user\hdbf-tools\steamupload\bin\Release\net9.0\SteamUpload.exe'
$stage = "$env:USERPROFILE\.hdbf\work\stage\HDBF"
$note  = 'C:\Users\user\Documents\Timberborn\Mods\HighDensityBoxFarm\docs\workshop\changenote-2.6.1.txt'
Push-Location (Split-Path $exe)     # 照 2.5.11 先例：cwd = exe 同目录（steam_appid.txt 在此）
& $exe --probe
if ($LASTEXITCODE -ne 0) { Pop-Location; throw 'Steam probe failed (Steam 未运行/未登录?)' }
& $exe $stage $note 2>&1 | Tee-Object -FilePath "$env:USERPROFILE\.hdbf\work\upload-261.log"
$code = $LASTEXITCODE
Pop-Location
"SteamUpload exit=$code"
```

- 前置：步骤 5 全 PASS；步骤 0 游戏已退出；`--probe` 输出 `steamRunning=True loggedOn=True`。
- 成功标志（两处都要）：
  1. 控制台进度依次出现 `k_EItemUpdateStatusPreparingContent` → `UploadingContent` → `CommittingChanges`（2.5.11 实测：
     `PreparingContent 2,204,337/2,204,337` → `UploadingContent 453,997/453,997` → `CommittingChanges`），
     最后 `submitDone ioFailure=False eResult=k_EResultOK needsLegalAgreement=False publishedFileId=3828809784`，exit 0；
  2. Steam 日志 `Uploaded new content ( ManifestID <新ID> ) for item 3828809784.` + `Upload finished for workshop item 3828809784 : OK`。
- 失败处理（**任何失败都不要换一份 stage 重试**；重试必须同一份 stage + 同一 SHA）：

| 现象 | 原因 | 处理 |
|---|---|---|
| exit=2 | SteamAPI 初始化失败 | 确认 Steam 已启动并登录；确认从 exe 同目录启动（steam_appid.txt） |
| exit=3/4/5 | Steam API/handle 异常 | 关闭游戏后重试一次；仍失败则查 Steam 状态 |
| exit=6 | 25 分钟超时 | 查网络，重试同一份 stage |
| exit=7 / `eResult` 非 OK | 常见 `k_EResultLimitExceeded`、`k_EResultAccessDenied`、`needsLegalAgreement=True` | `needsLegalAgreement=True` → 用浏览器接受 Workshop 法律协议后重传；其它 → 记录准确 eResult 给 Lead 决策 |
| 找不到 `Uploaded new content` 行 | 可能只是 metadata/无变化 | 先等 1 分钟重看日志；确认 change note 文件非空 |

### 步骤 8 — 记录 ManifestID 并交叉核对

```powershell
$log = 'C:\Program Files (x86)\Steam\logs\workshop_log.txt'
Get-Content $log -Tail 120 | Select-String '3828809784|Upload starting|Uploaded new content|Upload finished'
```

- 验证点：有且仅有**一条**本轮 `Upload starting`；`Uploaded new content ( ManifestID <新ID> )`；`Upload finished … : OK`。把 `<新ID>` + 时间写进发布记录。
- 失败处理：出现两条 start（重复提交）→ 以**最后一条**成功的 `Upload finished … OK` 的 ManifestID 为准，并在发布记录里如实写「第一次作废」（2.5.7 先例，见 §5 R5）。

### 步骤 9 — 公开页核对（等 1–5 分钟缓存）

```powershell
Start-Sleep -Seconds 300
# 然后用浏览器/抓取工具打开：
# https://steamcommunity.com/sharedfiles/filedetails/?id=3828809784
```

- 验证点：`Updated` 变为今天（Steam 页面显示 PST 时区，UTC-8；Asia/Macau 22:xx ≈ 当日 06:xx PST）；`File Size` ≈ 新包大小（48 files，量级 2.20–2.22 MB，3 位小数）；`Change Notes` 从 14 → **15**；标题/描述/封面/标签/可见性**未变**；Description 仍含折叠 bullet。
- 失败处理：5 分钟后仍未刷新 → **不要立刻重传**；以步骤 8 的 `ManifestID` 为权威（VERIFICATION §51.1 先例），再等 5 分钟重抓；抓取失败就在发布记录写「抓取失败」，不编造。

### 步骤 10 — 同步本机测试包（让下次启动即测到修复版；游戏必须已关）

```powershell
$stage   = "$env:USERPROFILE\.hdbf\work\stage\HDBF"
$content = 'C:\Program Files (x86)\Steam\steamapps\workshop\content\1062090\3828809784'
$mods    = "$env:USERPROFILE\Documents\Timberborn\Mods\HDBF"
Copy-Item "$stage\*" $content -Recurse -Force
Copy-Item "$stage\*" $mods    -Recurse -Force
# 验证三份一致
@($content, $mods, $stage) | ForEach-Object {
  $m = Get-Content "$_\manifest.json" -Raw -Encoding UTF8 | ConvertFrom-Json
  "$_ -> Version=$($m.Version) sha=$((Get-FileHash "$_\HDBF.UI.dll" -Algorithm SHA256).Hash.Substring(0,16))"
}
```

- 验证点：三处 `Version=2.6.1` 且 `HDBF.UI.dll` SHA 相同；Steam 重启后会以官方 ManifestID 刷新，本机包仅用于本地测试。
- 失败处理：文件被锁 → 说明游戏未关，先退出游戏再复制。

### 步骤 11 — 提交代码 + 发布记录（commit message 惯例：带 ManifestID、L1、gates）

```powershell
Set-Location 'C:\Users\user\Documents\Timberborn\Mods\HighDensityBoxFarm'
git add src/HDBF.UI/Panels/HdbfFarmPanelFragment.cs `
        src/HDBF.UI/Panels/HdbfFarmPanelFragment.Crop.cs `
        src/HDBF.UI/Panels/HdbfFarmPanelFragment.Cards.cs `
        tests/HDBF.Tests/V2PanelVisualContractTests.cs `
        data/manifest.json tools/validate/DataChecks.cs docs/workshop/changenote-2.6.1.txt
git status --short          # 确认没有 harness 文件/无关文件被 add
git commit -m "v2.6.1: 作物策略下拉弹窗被 4Hz 刷新关闭的热修——Dropdown.Showed 同步开/关信号 + 打开期间推迟 SetItems/卡片重建（L1 <N>/<N>、44 gates；Workshop ManifestID <新ID>）"
git rev-parse HEAD          # 这就是 v2.6.1 tag 的目标 commit
```

发布记录文档（单独 commit，照 2.6.0 先例）需要更新：

| 文件 | 内容 |
|---|---|
| `docs/VERIFICATION.md` | 新增 §54（2.6.1 离线门禁 + 实机验证证据）与 §55（發佈記錄：含 ManifestID、时间、页面对照、push 回报）——当前编号止于 §53.3 |
| `docs/workshop/README.md` | 末尾新增「發佈記錄（2026-11-14，2.6.1）」 |
| `docs/v2/CHANGELOG.md` | 顶部新增 `## [2.6.1] — 2026-11-14（…）` + 状态行补 2.6.1 |
| `README.md` | 第 6/13 行版本与第 22–28 行截图说明、2.6.0 特性段（保持历史，不改 2.6.0 记录；版本数字改 2.6.1） |
| `docs/VERIFICATION.md` §54 | 若本机实机点击验证仍未做，必须如实写 pending（不要抄 2.6.0 的「已通过」） |

### 步骤 12 — tag + push（可选 tag；由 Lead 执行）

```powershell
Set-Location 'C:\Users\user\Documents\Timberborn\Mods\HighDensityBoxFarm'
# HEAD~1 = 步骤 11 的版本 commit（若发布记录另起 commit）
git tag -a v2.6.1 -m "HDBF 2.6.1: crop-strategy dropdown popup hotfix (Workshop ManifestID <新ID>)" <版本commit>
git push origin master
git push origin v2.6.1
# 验证
git ls-remote origin refs/heads/master refs/tags/v2.6.1
git show v2.6.1:data/manifest.json | Select-String '"Version"'   # 必须 2.6.1
git rev-list -n1 v2.6.1                                           # 必须 = 版本 commit
git status --short                                                # 期望 clean
```

- 验证点：远端 master = 本地 HEAD；远端 tag peeled = 版本 commit；tag 内 manifest = 2.6.1；工作区 clean。
- 失败处理：push 拒绝 → 不 force；先 `git ls-remote` 判断是否其实已推（§1.1 教训）。

### 步骤 13 — 收尾核对（发布完成的最低标准）

- [ ] staged 包已上传且 Steam 日志有新 ManifestID + OK；
- [ ] 公开页 `File Size`/`Updated`/`Change Notes`（14→15）三项已核对（或如实记录抓取失败）；
- [ ] 本机三份包 = 2.6.1 且 UI DLL SHA 一致；
- [ ] 发布记录（VERIFICATION §54/§55、workshop README、CHANGELOG、README）已提交并 push；
- [ ] tag（若打）已推且指向版本 commit；
- [ ] `git status` clean；工作区无 harness 残留（`src` 与 staged DLL 双检）；
- [ ] 未改标题/描述/封面/标签/可见性、未开新 item（对照 §6）。

---

## 4. Change Note 文件格式

### 4.1 实测格式 A — `~/.hdbf/release-2511-changenote.txt`（2,621 B，2026-11-09）

逐字结构（**注意：这个文件实际只有 ENGLISH + 中文 两节，没有 zhTW 节**）：

```text
v2.5.11 — the Box Farm never shows the vanilla "Requires haulers to function." status.

ENGLISH
- Player request: ...
- Investigation (...): ...
- What changed: ...
- Verified: ...

中文
- 玩家要求：...
- 考察（...）：...
- 改动：...
- 验证：...
```

- 第 1 行：`vX.Y.Z — <一句英文标题>`；空行；`ENGLISH` 标题 + `- ` 项目符号；空行；`中文` 标题 + `- ` 项目符号。
- 历史文件里语言节标题不统一：`release-2510/2511/257/258/259` 用 `ENGLISH`+`中文`；`release-256` 用 `ENGLISH`+`繁體中文`。
- 该文件与 `hdbf-tools/change-note-2.5.11.txt` 内容相同（2,621 B，同一份的副本）。

### 4.2 实测格式 B — `docs/workshop/changenote-2.6.0.txt`（2,306 B，**2.6.0 实际上传使用**）

逐字结构（本次已读全文；线上 changelog 渲染结果一致）：

```text
HDBF 2.6.0 - Vanilla-style UI + collapsible sections / 原版化面板 + 可折叠分区

[EN] UI/GUI pass. ...（一段英文，无项目符号）

[ZH] 面板全面原版化：...（一段简体中文）

[TW] 面板全面原版化：...（一段繁体中文）
```

- 第 1 行：`HDBF <版本> - <英文标题> / <中文标题>`；`[EN]`、`[ZH]`、`[TW]` 三节各一段，节间空行。
- Steam 页面按原样渲染（换行保留、无 markdown 标题）；2.6.0 在线上 Change Notes 里就是这种观感。

### 4.3 2.6.1 建议（走 2.6.0 三语先例；内容留 TODO 给 Lead 填）

- 正式路径：`docs\workshop\changenote-2.6.1.txt`（随 repo 跟踪；2.6.0 先例）
- 镜像路径：`%USERPROFILE%\.hdbf\release-261-changenote.txt`（历史惯例）
- 建议骨架（UTF-8）：

```text
HDBF 2.6.1 - Crop-strategy dropdown stays open while you pick / 作物策略下拉不再自己关掉

[EN] Hotfix for the player report of 2026-11-14 ("the crop selector closes all the time"): the 4 Hz entity-panel refresh (and a command-triggered force refresh) could rebuild the crop card or re-set the crop dropdown's items while its popup was open, which closed the popup before an item could be picked. The panel now takes the exact open state from the vanilla dropdown's synchronous Showed event and the list drawer's DropdownHiddenEvent, and defers the whole crop sync (items, selected value, tooltip) plus the crop-card rebuild to the first refresh after the popup closes. The old DropdownVisible check (previous layout pass) stays only as a fallback, with a one-second stale guard so the panel can never freeze. No Harmony, no reflection, no new dependencies, no save-schema change; existing farms and crops are unchanged. Verified: TODO (L1 <N>/<N>, validator OK, 44 release checks passed; in-game click test TODO / pending — fill in honestly).

[ZH] 2.6.1 热修（玩家 2026-11-14 回报「作物下拉菜单老是自己关掉」）：面板 4 Hz 刷新（以及命令触发的强制刷新）可能在弹窗打开期间重建作物卡片或重设下拉项目，导致还没选就关闭。现在用原版下拉的 Showed 事件（同步）与抽屉的 DropdownHiddenEvent 拿准确的打开/关闭状态；弹窗打开期间，作物项目的整段同步（items、选中值、tooltip）与作物卡片重建都推迟到弹窗关闭后的第一次刷新再执行。旧的 DropdownVisible 判定（读上一次布局结果）仅保留为后备，并加 1 秒防呆上限，保证面板不会被冻住。无 Harmony、无反射、无新增依赖、存档 schema 不变；既有农场与作物不受影响。验证：TODO（L1 <N>/<N>、validator OK、44 项发布门禁；实机点击验证 TODO / 如实写 pending）。

[TW] 2.6.1 熱修（玩家 2026-11-14 回報「作物下拉選單老是自行關閉」）：面板 4 Hz 更新（以及命令觸發的強制更新）可能在彈出清單開啟期間重建作物卡片或重設下拉項目，導致還沒選就關閉。現在改用原版下拉的 Showed 事件（同步）與抽屜的 DropdownHiddenEvent 取得準確的開/關狀態；清單開啟期間，作物項目的整段同步（items、選取值、tooltip）與作物卡片重建都延後到清單關閉後的第一次更新再執行。舊的 DropdownVisible 判定（讀上一次版面結果）僅保留為後備，並加 1 秒防呆上限，保證面板不會被凍住。無 Harmony、無反射、無新增依賴、存檔 schema 不變；既有農場與作物不受影響。驗證：TODO（L1 <N>/<N>、validator OK、44 項發佈門禁；實機點擊驗證 TODO / 如實寫 pending）。
```

- 内容要点（供 Lead 填）：玩家报告出处（Steam 评论 2026-11-14，litos5599）；根因（刷新/强制刷新在弹窗打开时重建卡片或重设 items → 原版抽屉关闭弹窗）；修复（Showed + DropdownHiddenEvent 精确信号、打开期间推迟同步、DropdownVisible 仅后备 + 60 帧 stale 上限、换实体重置标志）；兼容性（0 Harmony / 无新依赖 / 存档不变）；验证数字（以实际 L1、gates、实机证据为准，未做就写 pending）。
- 约束：UTF-8；纯文本（不要 markdown 标题/代码块）；长度 <4 KB；不要写内网路径、token、玩家隐私信息。上传程序会 `Trim()`，首尾空行无影响。

---

## 5. 风险清单（每条：现象 → 规避）

| # | 风险 | 现象 | 规避动作 |
|---|---|---|---|
| R1 | **传旧包/混合包（2026-11-06 事故重演）** | 2026-11-06 17:14 首传因本机 `%TEMP%` 在不同工具调用间不一致，实际上传了**旧 2.5.6 staging**（`ManifestID 6070896543431048995` 作废；玩家 `Player.log` 仍是 `start HDBF version=2.5.6`），17:40 用固定 work root 重传才正确（VERIFICATION §43） | 发布构建只用**固定默认 work root** `%USERPROFILE%\.hdbf\work`；上传前必跑 §3 步骤 5（manifest Version=2.6.1 + SHA 表 + byte-scan）；上传日志 `Tee-Object` 存档；怀疑不一致时以最新 `ManifestID` + 页面 `File Size`/`Change Notes` 交叉核对；**绝不**用 `-WorkRoot $env:TEMP\...` |
| R2 | **TEMP harness 混进发布包（当前已发生）** | 当前 stage 的 `HDBF.Gameplay.dll` 含 `HdbfUiVerifyDriver`、`HDBF.UI.dll` 含 `VerifyCropsRebuild`：发布后玩家侧会被程序化选中农场/暂停/打开下拉；门禁只查 `diag.`/文件命名，**查不到这个字符串** | 按 §3 步骤 1 删除 2 个未跟踪文件 + 13 处标记（11 处 C# 调用 + 2 处 csproj 引用）；`git diff v2.6.0 -- src/HDBF.Gameplay src/HDBF.Core src/HDBF.Persistence src/HDBF.Integration` 必须为空；staged DLL byte-scan（§3 步骤 5）必须 0 命中 |
| R3 | **版本号只改一半** | `data/manifest.json`=2.6.1 但 `tools/validate/DataChecks.cs` 仍 2.6.0（或反之）→ validate pinned version 警告，发布记录「WARNINGS: none」不成立 | 两处一起改（§3 步骤 2）；`Select-String` 双断言；构建输出必须 `RESULT: OK` 且无 WARNINGS |
| R4 | **上传时游戏还开着** | 当前实机 `Timberborn` PID 3936 正在运行（21:33）：覆盖 `Documents\Timberborn\Mods\HDBF` / Steam content 目录会撞文件锁或读到半写入 DLL；上传后本机测试也失真 | 上传与步骤 10 同步前请玩家正常退出游戏（`Get-Process Timberborn` 无输出）；Steam 保持运行并 `--probe` 确认 `loggedOn=True`；不要强制 kill 游戏（可能丢存档） |
| R5 | **Change Note 不可撤回** | 每次成功 `SubmitItemUpdate` 都会在页面追加一条 Change Note，Steam 没有删除接口；2.5.7 事故在页面上永久留下两条同名 hotfix | 上传前逐项确认：note 首行版本 2.6.1、三语齐全、stage SHA 已记录；只提交**一次**；eResult=OK 后立刻把 ManifestID 写进发布记录；**不要**「再传一次看看」 |
| R6 | **并发会话同时构建/上传** | 预检实测：另一会话 21:32–21:34 正在重建 `%USERPROFILE%\.hdbf\work\stage\HDBF`（build.ps1 会先 `Remove-Item -Recurse` 再复制）；同时跑两个 build 或 build+upload 会交叉污染 stage（可能传半份包） | 发布串行化：步骤 0 确认没有 `build.ps1`/`dotnet build` 进程、stage mtime 稳定；上传期间不跑构建/实机验证；Lead 统一指挥只有一个执行者 |
| R7 | **页面字段被误改** | 若有人用**游戏内 Mods 上传器**（会读 `data/workshop_data.json` 的 `UpdateDescription/UpdatePreview/UpdateTags/UpdateVisibility=true`）或跑 `--description` 模式，可能覆盖标题/描述/封面/标签/可见性 | 只用 `SteamUpload.exe <stage> <note>` 模式（源码确保只 SetItemContent+SubmitItemUpdate）；本次不跑 `--description`；不打开游戏内上传界面；`data/workshop_data.json` 保持不动 |
| R8 | **页面核验误判 → 重复上传** | Steam 缓存 1–5 分钟；metadata-only 更新**不刷新 `Updated`**（2.6.0 已实测）；抓取偶尔失败 | 等 5 分钟再抓；以 Steam 日志 `ManifestID` 为权威（VERIFICATION §51.1 先例）；抓取失败就如实写「抓取失败」，**不重传**；发布记录同时记 `File Size` 与 `Change Notes` 数 |
| R9 | **GitHub 状态误判** | 本地 `origin/master` 跟踪引用陈旧（= `65dc637`），`git log origin/master..HEAD` 误报 4 个未推送；实际远端 master = `b74f0f8` = HEAD（0 未推送） | 判定一律用 `git ls-remote origin refs/heads/master`；push 前后各核对一次；不 force push、不删 tag |
| R10 | **tag 指错 commit** | `v2.6.1` 指向 docs/发布记录 commit 或旧 commit → GitHub tag 里的 manifest 不是 2.6.1，与上传包对不上 | tag 前 `git rev-list`/`git show v2.6.1:data/manifest.json` 双验证；tag 指向「版本 bump + 修复」commit；push 后 `git ls-remote --tags origin` 核对 peeled SHA |
| R11 | **实机验证与发布混淆** | 本轮 2.6.1 的实机点击验证（打开下拉→选作物）可能仍是 pending；若发布记录写成「已实机验证通过」就是造假 | 发布记录里按实际证据写：有截图/日志就附证据，没有就写 pending（2.5.7/2.5.9 先例）；本次 harness 只用于取证，**不得随包发布** |
| R12 | **门禁数字写死** | 文档/发布记录里的 gates 数存在不一致：`docs/OPS.md` §2.1 写「共 55 个检查」，而 2.5.x–2.6.0 发布记录全部是 `RELEASE GATES: PASSED (44 checks)`；`NFR GATE MATRIX: PASSED` 字样在当前 `build.ps1` 中找不到 | 以当次 `build.ps1` 实际输出为准，不写死 55/NFR；发布记录抄实际行；发现不一致在记录里注明 |

---

## 6. 本次不做（发布纪律）

- ❌ 不改 workshop **标题 / 描述 / 封面 / 缩略图 / 标签 / 可见性**（上传工具只做 `SetItemContent` + Change Note）。
- ❌ **不新开 workshop item**、不改 Id/AppID、不迁移发布（会清零订阅与收藏信号）。
- ❌ 不跑 `SteamUpload --description` 模式（本工具该模式会附带一条硬编码的 2.6.0 页面说明；本次无页面文案变更）。
- ❌ 不用**游戏内 Mods 上传器**（它会读 `data/workshop_data.json` 的 `Update*=true`，可能覆盖页面字段）。
- ❌ 不跑 `-DevDiagnostics`、不发布 dev 诊断包；不把 harness / 测试 / 调试文件带进包。
- ❌ 不改蓝图/本地化/数值/`HDBF_Config.json`/存档 schema；不新增 Harmony/反射/第三方依赖（2.6.1 只动 `src/HDBF.UI` + UI 契约测试 + 版本 pin）。
- ❌ 不用 `%TEMP%` 或 `-WorkRoot` 做发布构建；不用旧 stage/旧备份当上传源。
- ❌ 不 force push、不删/移动历史 tag、不改写已发布的 Change Note（也删不掉）。
- ❌ 不删除/移动 `hdbf-tools\workshop-backup` 里的任何历史备份；不把当前含 harness 的本机包另存为「2.6.0」备份。
- ❌ 不更新 README 实机截图（除非 Lead 明确要求；2.6.1 无新 UI 视觉）。
- ❌ 不代玩家回复 Steam 评论（可选后续动作，不属于本次上传范围）。

---

## 附录 A — 工作区当前状态快照（21:28–21:38，仍在变动）

### A.1 git

- `HEAD` = `b74f0f8`，`git status --short`（21:32 快照）：

```text
 M data/manifest.json
 M src/HDBF.Gameplay/Components/HdbfFarmQueryService.cs
 M src/HDBF.Gameplay/HDBF.Gameplay.csproj
 M src/HDBF.Gameplay/HdbfGameConfigurator.cs
 M src/HDBF.UI/Panels/HdbfFarmPanelFragment.Cards.cs
 M src/HDBF.UI/Panels/HdbfFarmPanelFragment.Crop.cs
 M src/HDBF.UI/Panels/HdbfFarmPanelFragment.Render.cs
 M src/HDBF.UI/Panels/HdbfFarmPanelFragment.cs
 M tests/HDBF.Tests/V2PanelVisualContractTests.cs
 M tools/validate/DataChecks.cs
?? src/HDBF.Gameplay/Components/HdbfUiVerifyDriver.cs
?? src/HDBF.UI/Panels/HdbfFarmPanelFragment.Verify.cs
```

- 真正的 2.6.1 修复（应进发布）：`HdbfFarmPanelFragment.cs` / `.Crop.cs` / `.Cards.cs`（`IsCropDropdownOpen` 精确信号 + 打开期间推迟同步/重建）、`tests/HDBF.Tests/V2PanelVisualContractTests.cs`（新增契约）、`data/manifest.json`（Version 2.6.0→2.6.1）、`tools/validate/DataChecks.cs`（ExpectedReleaseVersion 2.6.0→2.6.1）。
- 纯 harness（应删）：2 个未跟踪文件 + 下列 13 处标记（11 处 C# 调用 + 2 处 csproj 引用）：

```text
src/HDBF.Gameplay/Components/HdbfFarmQueryService.cs:90
src/HDBF.Gameplay/HDBF.Gameplay.csproj:37, :191
src/HDBF.Gameplay/HdbfGameConfigurator.cs:48
src/HDBF.UI/Panels/HdbfFarmPanelFragment.Cards.cs:158, :172
src/HDBF.UI/Panels/HdbfFarmPanelFragment.Crop.cs:61, :120, :125
src/HDBF.UI/Panels/HdbfFarmPanelFragment.cs:209, :238, :351
src/HDBF.UI/Panels/HdbfFarmPanelFragment.Render.cs:35
```

### A.2 修复内容（供 Change Note 与实际描述使用）

- 玩家报告（Steam 评论 2026-11-14）：非暂停时作物策略下拉弹出后很快消失，无法选作物。
- 根因：面板 4 Hz 刷新 / 命令触发的强制刷新在弹窗打开期间重建作物卡片（重新挂载选择按钮），或 `DropdownItemsSetter.SetItems`（末端的 `ClearItems`）与 `UpdateSelectedValue` 重设下拉内容 → 原版抽屉调用 `HideDropdown` 把弹窗关掉；原判断 `DropdownListDrawer.DropdownVisible` 读 `resolvedStyle.display`（上一次布局结果），弹窗刚打开的那几帧仍报 "closed"。
- 修复：`dropdown.Showed` 同步置位 `_cropPopupOpen`；`[OnEvent] OnDropdownHidden(DropdownHiddenEvent)`（`EventBus.Register(this)`）清除；`IsCropDropdownOpen()` 先看精确标志、`DropdownVisible` 仅后备、60 帧 stale 上限防冻死；弹窗打开期间 `RenderCrop` 整段同步与作物卡片重建都推迟；换实体/隐藏面板时重置标志。
- 范围：只改 `src/HDBF.UI`（3 文件）+ 1 个 UI 契约测试；0 Harmony、无新依赖、存档 schema 不变。

---

## 附录 B — 事实核对与文档不一致（不要在发布记录里照抄错的）

| 项 | 观测 | 说明 |
|---|---|---|
| OPS 门禁数 | `docs/OPS.md` §2.1 写「共 55 个检查」，2.5.7–2.6.0 发布记录全是 `RELEASE GATES: PASSED (44 checks)` | 以当次 `build.ps1` 输出为准；`grep -c Assert-ReleaseGate build.ps1` = 35（部分断言在循环里，计数方式不同） |
| `NFR GATE MATRIX: PASSED` | 发布记录常见，但当前 `build.ps1`/`tools` 中 grep 不到该字样 | 可能来自旧轮次工具；不要把它写成本次必然输出 |
| `~/.hdbf/release-2511-changenote.txt` 语言数 | 实际只有 `ENGLISH` + `中文` 两节；`release-256` 是 `ENGLISH`+`繁體中文`；2.6.0 的 repo 文件才是 `[EN]`/`[ZH]`/`[TW]` 三语 | 2.6.1 建议沿 2.6.0 三语先例（§4.3），并把镜像副本放回 `~/.hdbf` |
| 本地 `origin/master` | 陈旧引用 `65dc637`，`git log origin/master..HEAD` 误报 4 条 | 用 `git ls-remote` 判定未推送数 |
| 本机 content 目录 | manifest 2.6.0 但 DLL 是含 harness 的测试构建（SHA 与已发布 2.6.0 不同） | 不代表线上内容；Steam 重启后以官方 ManifestID 为准；发布后按 §3 步骤 10 覆盖为 2.6.1 |
| 构建可复现性 | 今天两次本机构建的 `HDBF.Core.dll` 都是 169,472 B / SHA `217831A9…`，而已发布 2.6.0 包是 169,984 B / SHA `A57B50CE…`；`src/HDBF.Core` 源码自 2.6.0 起未改（`git status` 无该目录） | 说明本机产物**不能逐字节复现** 2.6.0 发布包（SDK/环境差异）。发布门槛不要写「SHA 与 2.6.0 相同」，只要求「5 DLL + manifest 来自同一次干净构建并记录本轮 SHA」 |
| `--description` 模式 | `Program.cs:162-163` 硬编码提交说明「页面说明更新（2.6.0 UI）…」 | 2.6.1 不要跑；要更新页面描述走网页编辑器或改源码 |
| `%USERPROFILE%\.hdbf\workshop-backup` | **不存在**；实际备份根是 `C:\Users\user\hdbf-tools\workshop-backup` | 文档曾写 ~/.hdbf；以免踩空路径 |

---

## 附录 C — 只读证据命令与抓取记录

```powershell
# GitHub
git remote -v
git branch -vv
git rev-parse HEAD
git log -6 --format='%h %ad %s' --date=iso
git tag --sort=-creatordate
git ls-remote --heads --tags origin
git status --short
git log origin/master..HEAD --oneline

# Steam / 工具
Test-Path 'C:\Users\user\hdbf-tools\steamupload\bin\Release\net9.0\SteamUpload.exe'
Get-ChildItem 'C:\Users\user\hdbf-tools\workshop-backup' -Directory
Get-Content 'C:\Program Files (x86)\Steam\logs\workshop_log.txt' -Tail 120 |
  Select-String '3828809784|Upload starting|Uploaded new content|Upload finished'
Get-Content 'C:\Users\user\hdbf-tools\steamupload\Program.cs' -TotalCount 40   # usage/常量

# 包状态
$p = "$env:USERPROFILE\.hdbf\work\stage\HDBF"
(Get-ChildItem $p -Recurse -File).Count
(Get-Content "$p\manifest.json" -Raw -Encoding UTF8 | ConvertFrom-Json).Version
Get-FileHash "$p\HDBF.UI.dll" -Algorithm SHA256
```

- 线上页抓取（成功，HTTP 200，2026-11-14 21:31–21:32 +0800）：
  - <https://steamcommunity.com/sharedfiles/filedetails/?id=3828809784>
  - <https://steamcommunity.com/sharedfiles/filedetails/changelog/3828809784>
- 参考文档（本次已读）：`docs/VERIFICATION.md` §43/§49/§51/§52/§53、`docs/workshop/README.md` 發佈記錄 2.5.1–2.6.0、`docs/OPS.md` §1/§2.1/§6/§8、`docs/v2/CHANGELOG.md` 顶部。
