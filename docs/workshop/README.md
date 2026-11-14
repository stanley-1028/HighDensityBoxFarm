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

## 發佈記錄（2026-11-02，2.5.4）

- 2.5.4 於 **2026-11-02 22:56（Asia/Macau）** 上傳成功：`ManifestID 2037025195551276545`、
  `Upload finished for workshop item 3828809784 : OK`（Steam `logs/workshop_log.txt`）。
- 上傳只更新**內容與 Change Note**；標題/描述/封面/標籤/可見性刻意未動。
- Change Note（雙語）：維護一鍵追平所有逾期期數 + 每棟自動維護開關、休耕→常規不再清空成熟作物、
  暫停殘留 1 Hz reconcile + 暫停中走進/拆除前撤出、管道原因碼與「只從接上管網的原版液體水塔抽水」說明、
  面板「為什麼不動」狀態列/三條補水路徑、補水 headroom、三語誤導文案修正。
- 建置：L1 **655/655**、`tools/validate` WARNINGS: none / OK、`build.ps1` **44 gates PASSED**；
  staged package **48 files / 2,165,600 bytes / manifest 2.5.4**；GitHub `master` = `2a4a237`（修復）
  + 本篇發佈記錄 commit。
- **頁面文案同步**：`docs/workshop/DESCRIPTION.md` / `PAGE_COPY.txt` 的 maintenance 句已改為
  「perform maintenance — or enable automatic maintenance」，但本次 API 更新不覆寫頁面描述；
  下次在 Steam 網頁編輯器改描述時直接貼 `PAGE_COPY.txt` 即可（避免覆寫 2026-10-28 改版頁面）。

## 發佈記錄（2026-11-06，2.5.7 熱修）

- 2.5.7 於 **2026-11-06 17:40（Asia/Macau）** 上傳成功：`ManifestID 4408964257799319853`、
  `Upload finished for workshop item 3828809784 : OK`（Steam `logs/workshop_log.txt`）。
- **⚠ 第一次 17:14 的上傳作廢**：本機 `%TEMP%` 在不同工具呼叫間不一致，實際送出舊 2.5.6 staging
  （玩家 `Player.log` 仍 `start HDBF version=2.5.6`），`ManifestID 6070896543431048995` 僅為該次
  錯誤內容；17:40 改用固定預設 work root 重建並重新上傳，以上面 17:40 的 ManifestID 為準。
- 上傳只更新**內容與 Change Note**；標題/描述/封面/標籤/可見性刻意未動。
- Change Note（雙語）：作物策略下拉打不開的修復（逐卡內容簽名 diff + 下拉清單開啟期間暫停面板刷新，
  原版 Dropdown 不再被卸載/關閉；選取/items 仍即時同步）；另有 `ui.crop-dropdown` 診斷日誌。
- 建置：L1 **717/717**、`tools/validate` RESULT: OK、`build.ps1` **44 gates PASSED**；
  staged package **48 files / 2,194,822 bytes / manifest 2.5.7**；GitHub `master` = `3969c76`
  + `b5a4846`（初次發佈記錄）+ 本輪「防護＋正確重傳」修正 commit。
- **本機測試包**：正確的 2.5.7 stage 已覆蓋到 `Steam\steamapps\workshop\content\1062090\3828809784`
  （同一個 Workshop item、無重複 Id），下一次啟動遊戲即可測到修復版。
- **實機點擊驗證仍 pending**（本機無法合成輸入）；玩家若仍打不開，請附 `Player.log`
  （確認開頭 `start HDBF version=2.5.7`，並看有無 `ui.crop-dropdown`）。

## 發佈記錄（2026-11-07，2.5.9）

- 2.5.9 於 **2026-11-07 21:49（Asia/Macau）** 上傳成功：`ManifestID 9210717745557344144`、
  `Uploaded new content ( ManifestID 9210717745557344144 ) for item 3828809784.`、
  `Upload finished for workshop item 3828809784 : OK`（Steam `logs/workshop_log.txt`，AppID 1062090）。
- 頁面核對（2026-11-07 21:52）：`Updated 7 Nov @ 5:49am`（PST 顯示）、`File Size 2.203 MB`、
  `Change Notes` 共 11 條，最新一條為 v2.5.9 雙語 Change Note；標題/描述/封面/標籤/可見性未動。
- 上傳只更新**內容與 Change Note**；標題/描述/封面/標籤/可見性刻意未動。
- Change Note（雙語）：輸出庫存改為「滿員一天暫存」（104/208/416/832，原整盒 240/600/1200/2400）；
  原因（原版搬運按輸出填充率排序清空任務，且必須有接受該貨物的倉庫）；空緩衝下單日收穫必裝得下
  （不會回到 2026-10-29 的「600 玉米只送 50」）；沒有接收倉庫時按設計回壓並提示補倉庫；舊存檔
  超容量貨物保留不刪、暫不再收，搬走後恢復。檔案：`%USERPROFILE%\.hdbf\release-259-changenote.txt`。
- 建置：L1 **732/732**、`tools/validate` RESULT: OK、`build.ps1` **44 gates PASSED**；
  staged package **48 files / 2,202,809 bytes / manifest 2.5.9**；GitHub `master` = `d187cd6`（修復）
  + 本篇發佈記錄 commit。
- **本機測試包**：2.5.9 stage 已覆蓋到 `Steam\steamapps\workshop\content\1062090\3828809784`
  （同一個 Workshop item、無重複 Id），下一次啟動遊戲即可測到修復版；舊 2.5.8 內容備份在
  `C:\Users\user\hdbf-tools\workshop-backup\3828809784-2.5.8`。
- **實機驗證仍 pending**（本輪無可互動實機會話，且 sandbox 禁止寫 Saves/遊戲目錄）；玩家若仍有問題，
  請附 `Player.log`（確認開頭 `start HDBF version=2.5.9`）。

## 發佈記錄（2026-11-08，2.5.10）

- 2.5.10 於 **2026-11-08 10:00（Asia/Macau）** 上傳成功：`ManifestID 4179524907985799025`、
  `Uploaded new content ( ManifestID 4179524907985799025 ) for item 3828809784.`、
  `Upload finished for workshop item 3828809784 : OK`（Steam `logs/workshop_log.txt`，AppID 1062090；
  `SteamUpload.exe` 回報 `eResult=k_EResultOK`、`needsLegalAgreement=False`）。
- 上傳只更新**內容與 Change Note**；標題/描述/封面/標籤/可見性刻意未動。
- Change Note（雙語）：搬運鏈組件配對修復——補 `EmptyInventoriesWorkplaceBehavior` /
  `RemoveUnwantedStockWorkplaceBehavior` 兩個原版行為組件，普通搬運工恢復把農場暫存作物搬進普通倉庫；
  被標記清空的農場 / 舊存檔超容量庫存不再把 NULL 行為放進搬運候選表（修復前整個地區搬運 tick 拋
  NullReferenceException，搬運工永遠不搬）；**本版已完成實機驗證**（93 件 Carrot 2.5 個遊戲日內進倉、
  4.1 個遊戲日零異常；修復前同存檔 2.5 分鐘內必崩）。檔案：`%USERPROFILE%\.hdbf\release-2510-changenote.txt`。
- 建置：L1 **732/732**、`tools/validate` RESULT: OK、`build.ps1` **44 gates PASSED**；
  staged package **48 files / 2,203,313 bytes / manifest 2.5.10**；GitHub `master` = `b94990c`（修復）
  + 本篇發佈記錄 commit（push 回報見 VERIFICATION §49.1）。
- **本機測試包**：2.5.10 stage 已覆蓋到 `Steam\steamapps\workshop\content\1062090\3828809784`
  （同一個 Workshop item、無重複 Id），下一次啟動遊戲即可測到修復版；舊 2.5.9 內容備份在
  `C:\Users\user\hdbf-tools\workshop-backup\3828809784-2.5.9`。
- **本版已完成實機驗證**（不再是 pending）：證據見 `docs/VERIFICATION.md` §48 與
  `docs/workstreams/feedback-20261108/hauling.md`。

## 發佈記錄（2026-11-09，2.5.11）

- 2.5.11 於 **2026-11-09 00:09（Asia/Macau）** 上傳成功：`ManifestID 2025374107308867942`、
  `Upload finished for workshop item 3828809784 : OK`（Steam `logs/workshop_log.txt`）。
- 上傳只更新**內容與 Change Note**；標題/描述/封面/標籤/可見性刻意未動。
- Change Note（雙語）：盒子農場永不顯示原版「需要搬運工才能運轉」狀態（原版公開
  `NoHaulingPostStatus.Disable()` opt-out + 每遊戲日復檢 + 契約測試鎖定；無 Harmony/反射/新依賴；搬運鏈不變）。
  檔案：`%USERPROFILE%\.hdbf\release-2511-changenote.txt`（2,621 B）。
- 建置：L1 **732/732**、`tools/validate` RESULT: OK、`build.ps1` **44 gates PASSED**；staged package
  **48 files / 2,204,337 bytes / manifest 2.5.11**；GitHub `master` = `ccf99c0`（修復）+
  本篇發佈記錄 commit（push 回報見 VERIFICATION §51.1）。
- **本機測試包**：2.5.11 stage 已覆蓋到 `Steam\steamapps\workshop\content\1062090\3828809784`；
  舊 2.5.10 內容備份在 `C:\Users\user\hdbf-tools\workshop-backup\3828809784-2.5.10`。
- **實機**：本機視窗模式載入玩家 21:51 存檔副本，日誌確認 `indoor.hauler-status`（農場無該原版狀態組件）
  且 `[HDBF/Error]` = 0，見 `docs/VERIFICATION.md` §50。

## 發佈記錄（2026-11-14，2.6.0）

- 2.6.0 = **UI/GUI 原版化 + 可折疊分區**（玩家要求「把 UI 和 GUI 都原版化，收縮型菜單也是」）。
  只動 `src/HDBF.UI` + UI 契約測試；資料/藍圖/數值/存檔/配置/Harmony 不變。
- 上傳：2026-11-14 08:38（Asia/Macau）成功，`ManifestID 4848736136601558932`、
  `Uploaded new content ( ManifestID 4848736136601558932 ) for item 3828809784.`、
  `Upload finished for workshop item 3828809784 : OK`（Steam `logs\workshop_log.txt`，AppID 1062090）；
  `SteamUpload.exe` 回報 `submitDone ioFailure=False eResult=k_EResultOK needsLegalAgreement=False
  publishedFileId=3828809784`（exit 0）。
- Change Note 檔案：[changenote-2.6.0.txt](changenote-2.6.0.txt)（三語：EN / zhCN / zhTW）。
- 建置：5 程序集 0/0、L1 **733/733**、`tools/validate` RESULT: OK、
  `build.ps1` **44 gates PASSED**、staged 48 files / manifest **2.6.0**。
- 上傳只更新**內容與 Change Note**；標題/描述/封面/標籤/可見性刻意未動。
- **頁面說明已於 2026-11-14 10:0x 更新（僅 metadata，不動內容/圖片）**：用 Steamworks
  `SteamUGC.SetItemDescription` + `SubmitItemUpdate`（`SteamUpload.exe --description <file>`，
  `eResult=k_EResultOK`）把 `data/manifest.json` 的 Description 寫上頁面：
  「Vanilla-style panel with foldable sections」bullet + 中文簡介補一句「原版控件 + 折叠条」。
  公開頁核對：新文案已上線、原有封面與 3 張截圖未受影響、File Size 仍 2.208 MB、
  Change Notes 仍 14 筆、Updated 仍 13 Nov 4:38pm（Steam 對 metadata-only 更新不刷新 Updated）。
  同一份文案已同步 `data/manifest.json` / [PAGE_COPY.txt](PAGE_COPY.txt) / [DESCRIPTION.md](DESCRIPTION.md)。
- 公開頁核對（抓取 2026-11-14）：**File Size 2.208 MB**、**Updated 13 Nov @ 4:38pm**（Steam 顯示時區
  = 2026-11-14 08:38 Asia/Macau）、**Change Notes 14 筆**（最新為 2.6.0 三語說明）。
- **本機測試包**：2.6.0 stage 已覆蓋到 `Documents\Timberborn\Mods\HDBF` 與
  `Steam\steamapps\workshop\content\1062090\3828809784`（兩處 manifest 均 2.6.0，`HDBF.UI.dll`
  SHA-256 相同）；舊 2.5.11 內容備份在 `C:\Users\user\hdbf-tools\workshop-backup\3828809784-2.5.11`
  與 `...\HDBF-local-2.5.11`。
- 實機面板視覺驗證 **已通過**（2026-11-14，2560×1440，基礎版面版；原版卡片/進度條/按鈕/單選/折疊 clamp，
  `[HDBF/Error]`=0），證據 `docs/in-game/ui260-*.png` 與 VERIFICATION §52.1。
  截圖用臨時 harness（自動選中農場，因本機無法注入輸入），**未發佈**，發布 DLL 已還原並核對 SHA-256。

## 發佈記錄（2026-11-14，2.6.1）

- 2.6.1 = 修復玩家回饋「遊戲運行時作物策略下拉彈窗很快消失、選不了」（留言 litos5599；暫停時正常）。
  只動 `src/HDBF.UI` + UI 契約測試；資料/藍圖/數值/存檔/配置/Harmony 不變。
- 上傳：2026-11-14 22:37（Asia/Macau）成功，`ManifestID 5329795286704880229`、
  `Uploaded new content ( ManifestID 5329795286704880229 ) for item 3828809784.`、
  `Upload finished for workshop item 3828809784 : OK`（Steam `logs\workshop_log.txt`，AppID 1062090）；
  `SteamUpload.exe` 回報 `submitDone ioFailure=False eResult=k_EResultOK needsLegalAgreement=False
  publishedFileId=3828809784`（exit 0）。
- Change Note 檔案：[changenote-2.6.1.txt](changenote-2.6.1.txt)（三語：EN / ZH / TW；鏡像
  `~/.hdbf/release-261-changenote.txt`）。
- 建置：5 程序集 0/0、L1 **734/734**、`tools/validate` RESULT: OK、`build.ps1` **44 gates PASSED**、
  staged 48 files / 2,209,062 B / manifest **2.6.1**。
- 上傳只更新**內容 + Change Note**；標題/描述/封面/標籤/可見性刻意未動。
- 公開頁核對（抓取 2026-11-14）：**File Size 2.209 MB**、**Updated 14 Nov @ 6:37am**（Steam 顯示時區）、
  **Change Notes 15 筆**（最新為 2.6.1 三語說明）。
- **本機測試包**：2.6.1 已覆蓋 `Documents\Timberborn\Mods\HDBF` 與
  `Steam\steamapps\workshop\content\1062090\3828809784`（兩處 manifest 2.6.1、5 DLL + manifest
  SHA-256 與 stage 相同）；舊 2.6.0 內容備份在 `%USERPROFILE%\.hdbf\workshop-backup\3828809784-2.6.0-round`
  與 `C:\Users\user\hdbf-tools\workshop-backup\3828809784-2.6.0`。
- **實機**：最終 2.6.1 包以 HDBFTest 存檔副本載入（`Load time: 154651ms`）：`start HDBF version=2.6.1`、
  `startup: HDBF 2.6.1 ready`、`[HDBF/Error]`=0、無 `ui.render`/`ui.crop-dropdown` 例外；截圖
  `docs/in-game/bugfix261-final-panel.png`。修復驗證的 A/B 見 VERIFICATION §54.4；本機第三方 mod
  （Steam Engine Expansion）會用 `Dropdown.Hide()` 關掉共用抽屜，「保持打開」玩家側確認 **pending**。
- GitHub `master` = `264f5d8`（修復）+ 本發佈記錄 commit；`v2.6.1` tag → `264f5d8`（見 VERIFICATION §54.6）。
