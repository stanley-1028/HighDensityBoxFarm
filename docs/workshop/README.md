# 工坊页面改版包（Steam Workshop 3828809784）

发布当天（2026-10-28）页面数据：**16 独立访客 / 5 订阅 / 0 收藏 / 0 评分 / 0 评论**；
2026-10-29 复查已涨到 **43 访客 / 13 订阅**，但页面内容仍是旧的（标题、描述、预览图都没变）。
转化率 5÷16 = **31%**，在 Timberborn 工坊属正常区间（热门物品 25–40%）——**问题不是文案说服力，是没人看到**。
所以这个包的顺序是：**先换封面（提高点击）→ 再改描述（提高转化+搜索命中）→ 再导流（把人带过来）**。

## 文件

| 文件 | 用途 |
|---|---|
| **[PAGE_COPY.txt](PAGE_COPY.txt)** | 只含页面文案本体（BBCode），打开全选复制到 Steam 描述编辑器即可，无任何说明文字 |
| **[DESCRIPTION.md](DESCRIPTION.md)** | 页面文案终稿 + 元数据（标题/标签）+ 纯文本短描述 + 中文社区文案 + manifest 短描述 + 发页面前必须确认的诚实清单 |
| **[COVER_PROMPTS.md](COVER_PROMPTS.md)** | 封面交付物：Seedream 5.0 Lite 参数、3 个可直接粘贴的提示词（A 主封面 / B 剖面 / C 四档）、压字规格、上传前 QA；**§0.5 是这张已生成封面的处理结论** |
| [cover/](cover/) | 实际封面素材与 QA：`seedream-raw.jpg`（原图，带豆包水印）、`clean-1920x1080.jpg`（去水印无字版）、`mockup-A-1920x1080.jpg`（压字推荐版）、322px 可读性对照图 |
| [brief-growth.md](brief-growth.md) | 研究底稿：工坊机制、标签体系、热门物品拆解、社区渠道、72 小时清单、14 天节奏、KPI、关键词（全部标注 VERIFIED / INFERENCE） |
| [brief-art.md](brief-art.md) | 研究底稿：Timberborn 视觉识别、8 个热门封面原图分析、当前封面诊断、构图/配色/字体依据 |

## 立即要做的 5 件事（按性价比排序）

1. **换封面**：已生成并压字 → 已同步进 `data/thumbnail.png`（1280×720，475 KB）。
2. **改描述**：完整页面文案已在 `data/manifest.json` 的 Description 里（游戏内上传器直接读它）。
3. **发布更新**：本地包已部署到 `%USERPROFILE%\Documents\Timberborn\Mods\HDBF`，进游戏点更新即可（见下）。
4. **补 3–5 张实机图**：四档同框、面板特写、屋顶叠放、（可选）水链路。游戏内上传器不能内嵌图片，
   这一步要在 Steam 网页编辑器里做。
5. **开发 Discussion 帖**（known issues / what's next）并当天回复每条评论：0 评论的页面没有社会证明。

> **不要**以「求好评/求收藏」的方式拉数据——违反 Steam 行为准则，风险不对称。

## 发布（本地已备好，只剩在 Steam 上点两下）

已替你改好并部署：

| 文件 | 改动 |
|---|---|
| `data/manifest.json` | 标题 → `High-Density Box Farm — 3x3x1 Indoor Stackable Farm`；Description → 完整页面文案（含 Credits、中文简介；**去掉了 [img] 行**，因为游戏内上传器不支持内嵌图片） |
| `data/workshop_data.json` | `Name` 同步；`UpdateDescription/UpdatePreview/UpdateTags/UpdateVisibility` 均 true |
| `data/thumbnail.png` | 新封面，1280×720 PNG，475 KB（低于常见 1 MB 限制） |
| `%USERPROFILE%\Documents\Timberborn\Mods\HDBF` | 整包已部署（内容 = 线上 2.4.0 包 + 上述 3 个文件，避免把未完成的代码发出去） |

**你要点的：**

1. 启动 Timberborn（Steam 在线）→ 主菜单 **Mods** → 找到 **High-Density Box Farm** →
   上传/更新（Update / Publish）→ 勾选 **Update description** + **Update preview** → 发布。
2. 发布后到物品页确认标题/描述/封面已变（Steam 缓存 1–5 分钟）。
3. **删掉 `Documents\Timberborn\Mods\HDBF`**（它和订阅的工坊版重复），重启游戏。
4. 之后在网页 **Edit** 里加 3–5 张实机截图；加完把图片 URL 补回 `data/manifest.json` 的 `[img]` 行，
   否则下次游戏内更新会用「无图版」覆盖网页上的图片。

**备选路线（不想动游戏就用这个）**：网页 Edit → 粘 [PAGE_COPY.txt](PAGE_COPY.txt) → 传
[cover/mockup-A-1920x1080.jpg](cover/mockup-A-1920x1080.jpg) → 改标题 → 保存；不需要本地 `HDBF` 文件夹。

**发布元数据备份**：[publish/](publish/) 里是本次要上线的三件套（`manifest.json` / `thumbnail.png` /
`workshop_data.json`）。2026-10-29 07:23–07:24 观测到 `data/` 下这三个文件被某个并行进程/会话回滚成
HEAD 版本（manifest 之外的两个在 07:24:47 同一秒被批量恢复），已重写并备份。如果再次被回滚，执行：

```bash
cp docs/workshop/publish/manifest.json docs/workshop/publish/thumbnail.png docs/workshop/publish/workshop_data.json data/
cp docs/workshop/publish/* "$USERPROFILE/Documents/Timberborn/Mods/HDBF/"
```

## 构建状态（2026-10-29）

`build.ps1` 当前 **597 passed / 3 failed**，3 条失败全部来自工作区里**未提交的进行中改动**
（`Growing` 卡片顺序、快照字段 35 个、日 tick 分配 72 B），与封面/文案无关。
因此这次没有走 `-Install`（会带出未完成的代码），而是用**线上 2.4.0 包 + 新元数据**部署的。

## 发布后导流（一处就够，不要群发）

- 官方 Discord `#individual-mods` 单帖：2 张图 + 一段话 + 链接，当天回帖答疑。
- 或 r/Timberborn 一帖（`Modding` flair，约 46k 成员），附实机截图 + 具体数字。
- 镜像 mod.io，然后把链接加进描述末尾。
- 关注 Mechanistry 的模组比赛（上次 77 份投稿，**必须走报名表**，只在工坊上架不算）。

## 不要做

- 不要为了「重新上架」再发一个新物品：会把唯一积累的信号清零。
- 标签永久不可删，别加没验证过的 `Update 1.0`。
- 不要在正式封面里赌模型拼标题（压字更稳）。
- 不要用「求好评/求收藏」拉数据（违反 Steam 行为准则）。

## 發佈記錄（2026-10-29，2.5.1）

- 2.5.1 於 **2026-10-29 21:40（Asia/Macau）** 上傳成功：`ManifestID 895539692198601087`、
  `Upload finished : OK`（Steam `logs/workshop_log.txt`）。
- 上傳只更新**內容與 Change Note**（Steamworks UGC API，等同遊戲內上傳器的核心呼叫），
  標題/描述/封面/標籤/可見性刻意未動，避免覆蓋頁面既有內容。
- 工坊頁面核對：File Size **2.128 MB**（部署包 48 files / 2,128,345 bytes）、
  Updated **29 Oct @ 6:40am**（Steam 顯示時區）、Change Notes 2 → **3** 筆。
- 建置：L1 **606/606**、`tools/validate` OK、`build.ps1` **44 gates PASSED**；GitHub `master` = `40b112e`。
- 註：上面「構建狀態（2026-10-29）」的 597/3 是 2.5.0 當時的狀態；2.5.1 已全綠並完成上傳。

## 發佈記錄（2026-10-29，2.5.2）

- 2.5.2 於 **2026-10-29 22:17（Asia/Macau）** 上傳成功：`ManifestID 5678398362231513031`、
  `Upload finished for workshop item 3828809784 : OK`（Steam `logs/workshop_log.txt`）。
- 上傳只更新**內容與 Change Note**；標題/描述/封面/標籤/可見性刻意未動。
- 工坊頁面核對（22:20 抓取）：File Size **2.128 MB**、Updated **29 Oct @ 7:17am**（Steam 顯示時區）、
  Change Notes 3 → **4** 筆（最新一條為 2.5.2 雙語變更說明）。
- 建置：L1 **607/607**、`tools/validate` OK、`build.ps1` **44 gates PASSED**；
  staged package **48 files / 2,128,344 bytes / manifest 2.5.2**；GitHub `master` = `ee0a279`。

## 發佈記錄（2026-10-29，2.5.3）

- 2.5.3 於 **2026-10-29 23:24（Asia/Macau）** 上傳成功：`ManifestID 1710856574140546396`、
  `Upload finished for workshop item 3828809784 : OK`（Steam `logs/workshop_log.txt`）。
- 上傳只更新**內容與 Change Note**；標題/描述/封面/標籤/可見性刻意未動。
- Change Note（雙語）：暫停農場現在安全（暫停時日 tick/連續水耗/世界圖示停止，並把室內工人移出；
  恢復後自動繼續），修復玩家回報的「暫停即崩潰」。
- 建置：L1 **613/613**、`tools/validate` WARNINGS: none / OK、`build.ps1` **44 gates PASSED**；
  staged package **48 files / 2,132,440 bytes / manifest 2.5.3**；GitHub `master` = `6c6d64a`（修復）
  + 本篇發佈記錄 commit。
