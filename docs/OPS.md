# HDBF 運維手冊（OPS）— 建置、發佈門禁、補丁與日誌

本文件是 HDBF（High-Density Box Farm）的**操作面**單一入口：怎麼建置、怎麼看懂 `build.ps1` 的
發佈門禁、有哪些補丁與開關、怎麼在需要取證時重開 dev 診斷、以及怎麼解讀遊戲的 `HDBF/*` 日誌。

> 權威來源（有衝突時以這些為準）：
> * 需求文件：`C:\Users\user\Documents\deepseek-harness\default-workspace\high-density-box-farm\高密度盒子农场模组需求文档.md`
>   （唯讀；本工作區不複製）
> * [docs/VERIFICATION.md](VERIFICATION.md) — 實機輪次證據與各門禁現況
> * [docs/API_FACTS.md](API_FACTS.md) — 已驗證的 Timberborn API 事實（版本 1.1.2.8）
> * [docs/REQ_TRACEABILITY.md](REQ_TRACEABILITY.md) — 需求 → 實作 → 證據追溯
> * [tools/release/release-gates.json](../tools/release/release-gates.json) — 門禁的機器可讀契約
> * [build.ps1](../build.ps1) — 門禁的實作
> * [docs/workstreams/rel-01-release-gates.md](workstreams/rel-01-release-gates.md) — REL-01 收尾報告與負向對照

---

## 1. 建置指令

全部指令都在工作區根目錄執行；腳本必須以 Bypass 執行（本機 ExecutionPolicy 會擋未簽章腳本）：

```powershell
# 標準發佈建置（5 程序集 + L1 + validate + staging + 發佈門禁；不部署）
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1

# 發佈建置 + 部署到 Documents\Timberborn\Mods\HDBF（只有門禁全綠才會到這一步）
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Install

# 沙箱/CI dry-run：把 bin/obj/stage 全部改到指定目錄（不碰 %USERPROFILE%\.hdbf）
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -WorkRoot "$env:TEMP\hdbf-card-work"

# 只重新稽核一個「已經 stage 好」的套件（CI 或負向對照用；不建置、不執行測試/validate）
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -GateOnly -GateStage <staged 目錄>

# 取證用 dev 診斷建置（會重新開啟 diag.* 行；門禁反轉為「必須含 marker」；-Install 被拒絕）
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -DevDiagnostics -WorkRoot "$env:TEMP\hdbf-dev"
```

其他開關：

| 參數 | 作用 | 注意 |
|---|---|---|
| `-WorkRoot <dir>` | 覆寫建置輸出根（預設 `%USERPROFILE%\.hdbf\work`） | 沙箱、CI、避免污染本機快取時使用 |
| `-SkipTests` | 跳過 L1 測試 | **不是發佈建置**：L1 門禁會跳過並印黃字警告 |
| `-GateOnly -GateStage <dir>` | 只跑套件/原始碼層門禁（不含 L1、validate 摘要） | 負向對照與既有產物重稽核 |
| `-DevDiagnostics` | `-p:HdbfDevDiagnostics=true`；編譯 `HDBF_DEV_DIAGNOSTICS` | 產物**不可發佈**，`-Install` 直接拒絕 |
| `-Clean` | 清掉 `-WorkRoot` 與工作區內殘留 `obj/bin` | 只用於清快取 |
| `-ReleaseManifest <file>` | 換一份門禁 manifest | 只用於負向對照/測試；正式建置不要帶 |
| `-Install` | 部署到 `%USERPROFILE%\Documents\Timberborn\Mods\HDBF` | 在門禁 PASS 之後才執行 |

`-m:1` 是硬規則（本機平行 MSBuild 會在 restore 靜默失敗，見 [docs/API_FACTS.md](API_FACTS.md) §6）；
`build.ps1` 內每次 `dotnet` 呼叫都已帶上。

### 1.1 建置輸出位置

| 產物 | 位置 | 是否可發佈 |
|---|---|---|
| 5 個 mod 程序集 + 資料 | `<WorkRoot>\stage\HDBF` | 是（門禁 PASS 後） |
| 各專案 bin/obj | `<WorkRoot>\bin`、`<WorkRoot>\obj` | 否（不得進遊戲資料夾） |
| validate 參考資料 | `%USERPROFILE%\.hdbf\ref` | 否（唯讀、永不隨包） |

工作區本身**不是**模組資料夾：`build.ps1` 在開頭就擋掉工作區內的 `manifest.json` 與 stray DLL，
因為 Timberborn 會遞迴載入模組目錄下所有 `*.dll` / `*.blueprint.json`。

---

## 2. 發佈門禁清單 G-01..G-09 → 自動檢查對照

需求 §15.5 的門禁與本 repo 的自動化程度如下。**G-01~G-08 全綠才可發佈（REQ-TEST-15）**；
自動化只涵蓋「可離線證明」的部分，其餘是人工簽核（見 §7）。

| 門禁 | 需求條件 | 本 repo 的自動檢查 | 自動化程度 |
|---|---|---|---|
| G-01 | 全部 P0 用例 100% 通過 | `build.ps1` 的 L1 檢查：`FAILED 0`、`PASSED >= 460`（manifest `l1.minTests`），共 3 個斷言 | 部分：L1 單元證據；實機 P0 仍人工 |
| G-02 | P1 通過率 ≥95%，未過項有書面豁免 | 無自動檢查（P1 多為實機行為） | 人工 |
| G-03 | §15.3 效能閾值全達標 | 無自動檢查（需 Profiler） | 人工 |
| G-04 | 存檔往返 3 次逐欄位一致 | L1 `SaveRoundTripFaultTests` / `SaveCodecTests` / `SaveBudgetTests` 覆蓋資料層往返；3 次實機往返仍人工 | 部分 |
| G-05 | 兼容矩陣（單載 + 五組合 + 全組合 + 缺失）全綠 | `build.ps1` 的 10.4 相容報告檢查：13 欄位齊全/順序、`ToJson()` 存在（3 個斷言）＋ validate 5/5 程序集；矩陣本身用 `tools/dev/CompatMatrix.ps1` 實機跑 | 部分 |
| G-06 | 30 遊戲日壓力無未捕獲異常、日誌無 `Error` | 無自動檢查（需實機長跑） | 人工 |
| G-07 | 11 條硬驗收全部簽字 | 無自動檢查；清單見 §7 AC-01..AC-11 | 人工 |
| G-08 | **發佈包不含調試命令、測試藍圖與作弊開關** | `build.ps1` 的套件/補丁/診斷檢查（30 個斷言，見 §2.1） | **自動（本卡新增）** |
| G-09 | 本地化鍵缺失 0，1080p/1440p 無截斷 | 3 語言 CSV 鍵集合相等 + 表頭/重複/空字串檢查、validate `RESULT: OK` 無 `[localization]`（19 個斷言）；UI 版面仍人工 | 部分 |

### 2.1 發佈門禁的分組（`build.ps1` 的 release gates）

正式建置（Release、非 dev）共 **55** 個檢查；全部 PASS 才會 staging 完成後繼續，任一 FAIL 立即
throw、腳本 non-zero 退出、`-Install` 不會被執行。分組如下：

| # | 檢查 | 證明什麼 |
|---|---|---|
| 1 | staging 恰好 5 個 DLL、名稱等於 manifest 白名單、每個都是合法 PE（`MZ`） | 不會多帶測試/工具/第三方 DLL |
| 2 | staged 檔案類型白名單（`.dll/.json/.csv/.png/.md/.txt`）與檔名黑名單（`*test* *debug* *cheat* *diag* *dev* *.pdb *.xml *.deps.json *.runtimeconfig.json`） | G-08「測試藍圖/調試命令」不進包 |
| 3 | shipped `HDBF_Config.json` 的 root/features 不含 `debug/debugPanel/devDiagnostics/cheat/cheatPanel/godMode/unlockAll/devCommands` | G-08「作弊開關」不進包 |
| 4 | 所有 staged 檔案（以 ASCII + UTF-16 兩種編碼掃描）不含 `diag.`、`scan.probe`、`DescribeDryPlots`、`dryCells`、`farm.plan`、`compat.harmony-patch-applied`、`HDBF.DevDiagnostics`、`HDBF_DEV_DIAGNOSTICS` | Release 套件**完全沒有** dev 診斷字串（不是執行期關掉） |
| 5 | `Diagnostics.cs` 有行首 `#if HDBF_DEV_DIAGNOSTICS` / `#else` / `#endif`，release 分支無 `diag.` 與 marker | 診斷收斂機制本身可稽核 |
| 6 | `Directory.Build.props` 的 `HdbfDevDiagnostics` 預設 `false`，且只有在明確 `true` 時才加 `HDBF_DEV_DIAGNOSTICS` | 預設關閉、只能由建置參數開啟 |
| 7 | `HdbfPatchCatalog`：唯一 Harmony 實例 id `hdbf.highdensityboxfarm`、恰 1 個 `Postfix` 種類、恰 1 個目標方法 | ADR-009 / REQ-COMPAT-16 |
| 8 | 補丁實作來源（5 檔）無 `Transpiler`、恰 1 個 `public static void Postfix` | v1 不改寫 IL（REQ-IMPL-15/17） |
| 9 | bootstrap 在安裝前查 `IsPatchEnabled` 且可 `Unload`；installer 有 `Unpatch` 且把 patch count 歸 0 | 補丁可關、可卸載（REQ-COMPAT-18） |
| 10 | shipped config 有 `features.growthMultiplierPatch` 且為 boolean；staged `HDBF.Integration.dll` 含 id / `HdbfPatchCatalog` / `Postfix` 且無 `Transpiler` | 發佈包內的補丁清單可稽核且可關閉 |
| 11 | 測試接縫 `TestHooks` 只出現在 `src/HDBF.Integration`，UI/Gameplay/命令層 0 引用 | 沒有玩家可達的測試/作弊入口（G-08） |
| 12 | 相容報告序列化恰好 13 個 10.4 欄位（集合相等、順序等於 manifest）、`ToJson()` 存在 | REQ-SAFE-16 / G-05 |
| 13 | enUS/zhCN/zhTW 三個 CSV：存在、表頭 `ID,Text,Comment`、無重複 key、無空字串、三語言 key 集合完全相等 | G-09「本地化鍵缺失 0」（zhTW 一併納入） |
| 14 | validate：`RESULT: OK`、無 `[localization]` 錯誤、`5/5 OK (expected 5)` | 資料/藍圖/本地化/程序集範圍（BUG-02 防回歸） |
| 15 | 5 個程序集編譯 `0 warnings / 0 errors` | 乾淨建置 |
| 16 | L1：`FAILED 0`、`PASSED >= 460`、有摘要行 | P0 單元證據（G-01 部分） |

> 想改門禁內容時：改 `tools/release/release-gates.json` 與 `build.ps1`，並讓
> `tests/HDBF.Tests/ReleaseGateContractTests.cs` 仍綠（它把同一份 manifest 當 embedded resource
> 驗證程式碼/資料沒有偷偷偏離）。

### 2.2 負向對照（門禁真的會擋）

`-GateOnly` 可對「故意弄壞的 staging 複本」重跑門禁。實際演練（rel-01 報告 §5）：

| 注入 | 預期/實際結果 |
|---|---|
| 在 `ART_PLACEHOLDER.md` 塞一行 `diag.scan.begin` | exit 1；`FAIL [G-08] no dev diagnostics strings ... ART_PLACEHOLDER.md contains "diag."` |
| 從 shipped `HDBF_Config.json` 移除 `features.growthMultiplierPatch` | exit 1；`FAIL [G-08] shipped HDBF_Config.json exposes the disable switch ...` + `features.growthMultiplierPatch is a boolean` |
| 用一份多要求 `cheatPanel` 欄位的 manifest 跑（`-ReleaseManifest`） | exit 1；`FAIL [G-05] report field list has 13 fields` + `... fields -- cheatPanel (<=)` |
| 刪掉 staging 的 `HDBF.UI.dll` | exit 1；`FAIL [G-08] staged package contains exactly the five HDBF assemblies` |
| 對 release 套件用 `-DevDiagnostics` 跑（反轉斷言） | exit 1；`FAIL POSITIVE CONTROL: dev build carries the HDBF.DevDiagnostics marker` |
| 正常 release 套件 | exit 0；`RELEASE GATES: PASSED (55 checks)` |

---

## 3. 補丁與開關清單

### 3.1 Harmony 補丁（ADR-009，deviation D-2）

| 項目 | 值 | 出處 |
|---|---|---|
| Harmony 實例 id（唯一） | `hdbf.highdensityboxfarm` | `src/HDBF.Integration/Harmony/HdbfPatchCatalog.cs` |
| 補丁數上限 | **1**（只允許 Postfix；0 個 Transpiler） | `tools/release/release-gates.json` `patches.maxCount` |
| 補丁 id | `HDBF.Patches.BoxFarmGrowthPatch` | `HdbfPatchCatalog.GrowthPatchId` |
| 目標 | `Timberborn.Growing.Growable.ResumeGrowing`（private instance void） | `HdbfPatchCatalog` |
| 種類 / 優先級 | Postfix / `HarmonyLib.Priority.Normal`（300） | `HdbfPatchCatalog` |
| 安裝方式 | **全反射**，不編譯參考 HarmonyLib、不隨包 `0Harmony.dll` | `HdbfHarmonyInstaller.cs` |
| 卸載 | 3 次失敗自動卸載（`PatchFailureStateMachine`）；session unload 反向卸載；patch count 歸 0 | `HdbfGrowthPatchBridge` / `HdbfHarmonyInstaller` |
| 衝突 | 目標方法已有別人的 postfix → 跳過並記 `HDBF/Warn` | `HasConflictingPostfix` |
| 功能開關 | `HDBF_Config.json` → `features.growthMultiplierPatch`（boolean，預設 true） | `data/HDBF_Config.json` |
| 開關關閉時 | 完全不安裝、不碰 Harmony；回到 pre-patch D-2 行為 | `HarmonyBootstrap.Load` |

> **未決的產品決策（人工簽核）**：`docs/VERIFICATION.md` §19.2 曾記錄「發佈預設先關閉補丁
> （`growthMultiplierPatch=false`）」，但目前 shipped config 與 `HdbfConfig` 預設都是 `true`
> （round 16 已實機驗證四層乘算與健康農場 no-op）。REL-01 不擅自改平衡/預設值；門禁只保證
> **可關**。發佈前請由 Lead 依 ADR-009 的「實機驗證通過才可保留」條款決定預設值並記錄於
> VERIFICATION / REQ_TRACEABILITY。

### 3.2 其他開關（`data/HDBF_Config.json`）

| 區段 | 鍵 | 預設 | 用途 |
|---|---|---|---|
| features | `densePlanting` | true | 密植模式（章 6.7） |
| features | `rotation` | true | 輪作疲勞減免（章 6.4） |
| features | `maintenance` | true | 維護週期/停擺（章 6.5） |
| features | `powerConnection` | true | 頂級並網 + 離網回退 |
| features | `pipeConnection` | true | Timber Pipes 灌溉適配 |
| features | `logisticsOutput` | **false** | Conveyor Belts 出帶（需適配器，table 10-3） |
| features | `soilFatigue` | true | 土壤疲勞累積 |
| features | `growthMultiplierPatch` | true | ADR-009 單一 Postfix（見 §3.1） |
| perf | `scanBudgetPerFrame` 等 8 鍵 | 見檔案 | 分幀預算/降頻（REQ-SAFE-18/19） |
| ui | `worldIcons`、`worldIconBlink` 等 6 鍵 | 見檔案 | 世界警告圖標與聚合（章 14.2/14.6） |
| balance | 8 個乘數 + `tier[4]` | 見檔案 | §6 平衡參數（**改動需重跑平衡驗收**） |

### 3.3 建置/診斷開關

| 開關 | 形式 | 預設 | 說明 |
|---|---|---|---|
| `HdbfDevDiagnostics` | MSBuild property | **false** | 決定是否編譯 `HDBF_DEV_DIAGNOSTICS`；`-DevDiagnostics` 或 `-p:HdbfDevDiagnostics=true` 開啟 |
| `HDBF_DEV_DIAGNOSTICS` | C# define | 未定義 | 只包住 `HdbfBoxFarmController.Diagnostics.cs` 的 dev 分支、ADR-009 取證行、`HdbfLogKeys.HarmonyPatchApplied` |
| `features.*` | 執行期 JSON | 見 §3.2 | 玩家可改；**dev 診斷刻意不放在這裡**（見 §4） |

### 3.4 測試接縫（`TestHooks`）

`src/HDBF.Integration/TestHooks.cs` 是唯一的 static mutable state，用於在沒有遊戲程序的情況下
模擬「模組存在但 API 缺失」與注入 log sink。它：

* 生產環境 inert（沒有任何程式會呼叫 `Inject*`，也沒有 UI/命令/設定入口）；
* 門禁檢查它只出現在 `src/HDBF.Integration`（UI/Gameplay/命令層 0 引用）；
* 保留與否列為人工簽核項（見 §7.4）。

---

## 4. 如何開啟 dev 診斷（取證用）

### 4.1 為什麼用「條件編譯」而不是 `HDBF_Config.json` 的 `features.devDiagnostics`

REL-01 選擇 **MSBuild 屬性 `HdbfDevDiagnostics` → define `HDBF_DEV_DIAGNOSTICS`**，理由：

1. **G-08 要求的是「包裡沒有」，不是「執行期關掉」**：config flag 會把整份診斷程式碼與
   `diag.*` 字串留在 DLL 裡，只靠預設值 false；玩家改一行 JSON 就能把除錯入口打開，
   而 REQ-BAL-27 明確要求發佈版不得暴露除錯入口。
2. **可稽核**：Release DLL 連 `diag.`、`HDBF.DevDiagnostics` marker 都不存在，`build.ps1`
   可以在 byte 層斷言（ASCII + UTF-16 兩種編碼掃描），負向對照也能證明門禁會擋。
3. **單一開關、不新增 config schema**：dev 建置只是 `-DevDiagnostics` 一個參數；正式建置
   不可能「忘了關」——屬性預設 false，且 dev 產物會被門禁標記為不可發佈。
4. **正式版零成本**：沒有執行期分支、沒有多餘字串、沒有診斷計數器（診斷現在直接引用 production
   的 `PlotScanStats`，不再另存一份）。

代價：要取證就必須重新建置（不能只改 config）。dev 建置的門禁會**反轉斷言**：必須看到
`HDBF.DevDiagnostics` marker 與 `diag.*`，否則代表開關根本沒生效。

### 4.2 操作步驟

```powershell
# 1) 建 dev 診斷包到獨立 WorkRoot（避免蓋掉正式快取與 stage）
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -DevDiagnostics -WorkRoot "$env:TEMP\hdbf-dev"

# 2) 只有門禁 PASSED 才把 <WorkRoot>\stage\HDBF 複製到 Mods\HDBF（手動；-Install 會被拒絕）
#    例：Copy-Item "$env:TEMP\hdbf-dev\stage\HDBF\*" "$env:USERPROFILE\Documents\Timberborn\Mods\HDBF" -Recurse -Force

# 3) 啟動遊戲取證；證據通常落在 Player.log（見 §5）
```

單專案 dev 建置（只重編 Gameplay，速度最快）：

```powershell
dotnet build src\HDBF.Gameplay\HDBF.Gameplay.csproj -c Release -m:1 `
  -p:HdbfDevDiagnostics=true -p:HdbfWorkRoot="$env:TEMP\hdbf-dev"
```

### 4.3 dev 建置會多出什麼

| 產物 | Release | Dev（`-DevDiagnostics`） |
|---|---|---|
| `HDBF.Gameplay.dll` 內 `HDBF.DevDiagnostics` assembly marker | 無 | 有 |
| `diag.*` 日誌行 | 無 | 有（`diag.scan.begin/end/probe`、`diag.day`、`diag.farm.plan`） |
| ADR-009 取證行 `compat.harmony-patch-applied.1..12` | 無 | 有 |
| `build.ps1` 門禁 | 斷言「不存在」 | 斷言「存在」，並印 `DEV-DIAGNOSTICS ... NOT releasable` |
| `-Install` | 允許（門禁 PASS 後） | **拒絕**（throw） |

範例（dev，round 16 格式）：

```
[HDBF/Info] diag.scan.begin.<entityId>: scan.begin entityId=... origin=... side=11 ...
[HDBF/Info] diag.scan.end.<entityId>: scan.end ... scanned=121 accepted=60 ... kept=60 added=0 dropped=0 coverageAfterScan=1.000 rejects[terrain=0,field=0,...]
[HDBF/Info] diag.day.<entityId>: day.tick ... registered=600 moist=358 dry=242 coverage=0.597 ...
HDBF/Info: compat.harmony-patch-applied.1: ... growthRate=0.397 vanillaDays=10.00 scaledDays=25.21
```

### 4.4 如何確認「正式包真的沒有」

```powershell
# 1) 正常建置（不帶 -DevDiagnostics）後，門禁會印：
#    PASS [G-08] no dev diagnostics strings in any staged file (...)
#    PASS [G-08] no HDBF.DevDiagnostics assembly marker in the release package
# 2) 自己再掃一次 staged DLL：
$stage = "$env:USERPROFILE\.hdbf\work\stage\HDBF"
$bytes = [IO.File]::ReadAllBytes("$stage\HDBF.Gameplay.dll")
$ascii = [Text.Encoding]::ASCII.GetString($bytes)
$utf16 = [Text.Encoding]::Unicode.GetString($bytes, 1, $bytes.Length - 1)
"ascii diag=" + $ascii.Contains("diag.") + " utf16 diag=" + $utf16.Contains("diag.")
#    兩個都必須是 False
```

---

## 5. 如何解讀 `HDBF/*` 日誌

### 5.1 分類與格式

依需求 §10.4 / REQ-OPS-03，日誌固定四類（由呼叫端的 `HdbfLogCategory` 決定）：

| 分類 | 意義 | 典型用途 |
|---|---|---|
| `HDBF/Info` | 生命週期、偵測結果、狀態摘要 | `startup`、`gameplay.entity`、`diag.*`（dev） |
| `HDBF/Warn` | 降級、截斷、邊界修正、可恢復問題 | `compat.missing`、`plot.scan-empty`、`perf.reduced` |
| `HDBF/Error` | 異常、存檔失敗、不可恢復錯誤 | `compat.harmony-patch-failed`、`ui.render` |
| `HDBF/Compat` | 適配器偵測/初始化/衝突、補丁安裝/卸載 | 兼容矩陣解析用 |

兩種實際前綴（不同組件、同一套分類）：

```
[HDBF/Info] <key>: <message>          # HDBF.Gameplay 的 log sink（HdbfSystemRegistrar）
HDBF/Compat: <key>: <message>         # HDBF.Integration 的 log sink（IntegrationLog）
```

### 5.2 節流規則（REQ-SAFE-14）

* 同一個 **key** 30 秒內最多一行；重複次數不丢，於下一次輸出附 `suppressed=<n>`。
* key 是節流身分：`compat.failed:<adapterId>`、`ui.world-icons:<entityId>`、
  `gameplay.path-unreachable.<entityId>` 等都把 id 接在 key 後，**每棟建築/每個適配器獨立節流**。
* 看到 `suppressed=40` 代表「這 30 秒內另外被壓掉 40 次」，不是遺失。

### 5.3 錯誤行格式

`IHdbfLog.Error` 會在訊息後自動附上（REQ-SAFE-15）：

```
<key>: <message>; entityId=<id>; exception=<完整型別>: <訊息>; stack=<最多 20 行，以 " | " 串接> suppressed=<n>
```

因此排查時：先 grep `HDBF/Error`，再從 `entityId` 找同 id 的 `gameplay.entity` /
`diag.day`（dev）上下文。日誌不含玩家絕對路徑。

### 5.4 常用 key

| Key | 分類（典型） | 意義 / 排查 |
|---|---|---|
| `startup` | Info | mod 啟動版本與入口；確認 mod 有被載入 |
| `gameplay.entity` | Info | 農場實體建立（tier、blueprint）；配合 entityId |
| `gameplay.save` / `save.degraded` / `save.record-reset` | Info/Warn | 存檔節點降級、單棟重建成安全預設；不是崩潰 |
| `plot.scan-empty` | Warn | 一次掃描 0 格註冊（灌溉永遠 0）；看訊息內 `rejects[...]` 分類 |
| `plot.scan-reachability` | Warn | 可達性拒絕 > 接受（農場缺格） |
| `plot.scan-budget` | Info | 掃描用盡本幀預算，下一幀續（正常） |
| `gameplay.farm` / `gameplay.growth-rate` | Info/Warn | 每日農事派工、生長倍率查詢（debug 用） |
| `gameplay.path-unreachable.<entityId>` | Warn | 單格尋路失敗（no cached flow field），該格視為不可達、不中斷整棟 |
| `compat.enabled:<adapterId>` | Compat | 適配器 active；`CompatMatrix.ps1 -CheckLog` 依賴此前綴 |
| `compat.missing:<adapterId>` | Compat | 模組不存在 → 降級（正常，非錯誤） |
| `compat.failed:<adapterId>` | Compat/Warn | 適配器偵測/初始化失敗（已隔離） |
| `compat.harmony-patch-installed` | Compat | 唯一 Postfix 安裝成功（含 target、priority、instance id） |
| `compat.harmony-patch-unavailable` | Warn | 未安裝（Harmony 缺/簽名不符/衝突/開關關閉）→ D-2 行為 |
| `compat.harmony-patch-failed` | Error | 補丁執行期異常（已吞掉、該作物維持原版）；3 次後卸載 |
| `compat.harmony-patch-unloaded` | Compat | 卸載完成，patch count 歸 0 |
| `config.missing` / `config.invalid` | Warn | 找不到/壞掉的 config → 內建預設 + 備份 |
| `perf.reduced` | Info/Warn | 進入 Reduced 模式（>100 棟） |
| `ui.world-icons:<entityId>` / `ui.world-icon-toggle:<entityId>:<kind>` | Warn | 世界圖標不可用/單一 toggle 失敗；面板仍可用 |
| `ui.render` / `ui.command-exception` / `ui.command-rejected` | Error/Warn | 面板渲染或指令失敗（含 entityId、堆疊摘要） |
| `diag.*`（dev only） | Info | 取證列：`scan.begin/end/probe`、`day`、`farm.plan`；正式包不存在 |
| `compat.harmony-patch-applied.1..12`（dev only） | Info | ADR-009 取證：前 12 株被拉伸作物的 rate/days；正式包不存在 |

### 5.5 常用 grep

Player.log 的常見位置（Steam + Windows）：
`%USERPROFILE%\AppData\LocalLow\Mechanistry\Timberborn\Player.log`；repo 內的歷史證據在
`docs/in-game/round*-Player.log`。

```powershell
# 只看 HDBF（兩種前綴都命中）
Select-String -Path Player.log -Pattern 'HDBF/'
# 只看錯誤
Select-String -Path Player.log -Pattern 'HDBF/Error'
# 補丁生命週期
Select-String -Path Player.log -Pattern 'compat\.harmony-patch'
# 掃描註冊問題
Select-String -Path Player.log -Pattern 'plot\.scan-(empty|reachability)'
# 節流統計
Select-String -Path Player.log -Pattern 'suppressed='
# dev 取證（正式包應該一行都沒有）
Select-String -Path Player.log -Pattern 'diag\.'
# 兼容矩陣（五個適配器逐行）
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dev\CompatMatrix.ps1 -CheckLog Player.log -Adapters TimberPipes,PowerLines,ConveyorBeltsV2,MoreMines,HoomanStairs
```

---

## 6. 發佈套件內容（G-08 稽核對象）

`<WorkRoot>\stage\HDBF`（10 份藍圖、419 KB 量級）應包含：

```
manifest.json                 HDBF_Config.json          ART_PLACEHOLDER.md
HDBF.Core.dll                 HDBF.Persistence.dll      HDBF.Gameplay.dll
HDBF.Integration.dll          HDBF.UI.dll
Buildings/Food/BoxFarm/BoxFarm.{Basic,Intermediate,Advanced,Top}.{Folktails,IronTeeth}.blueprint.json
TemplateCollections/TemplateCollection.Buildings.{Folktails,IronTeeth}.blueprint.json
Localizations/{enUS,zhCN,zhTW}.csv
Icons/BoxFarm{Basic,Intermediate,Advanced,Top}.png (+ .png.meta.json)
```

**不得出現**：`*.pdb`、`*.xml`（doc）、`*.deps.json`、`*.runtimeconfig.json`、測試/除錯/作弊
命名檔案、`0Harmony.dll`、任何 dev 診斷字串或 marker（門禁逐檔掃描 ASCII + UTF-16）。

---

## 7. 人工簽核項（自動門禁不能取代）

### 7.1 11 條硬驗收（需求 §15.4，G-07）

| ID | 驗收內容 | 需要的證據 |
|---|---|---|
| AC-01 | 四檔可放置/建造/研究解鎖 | 建造面板逐檔截圖 + 科技前置截圖 |
| AC-02 | 正確綁定工作範圍與託管格 | 掃描/排除/不可達截圖 + 單測輸出 |
| AC-03 | 農民種植/收穫；無農民不產出 | 有/無農民各 1 日錄屏 + 產出統計 |
| AC-04 | 灌溉不足停止生長/減產 | 四檔 `C_min` 邊界 + `0.25×C_min` 面板截圖 |
| AC-05 | 高階維護懲罰明確 | 逾期 1 週期/連續逾期停擺/頂級斷電三場景 |
| AC-06 | 存讀檔不丢託管格/疲勞/維護/端口 | 往返 3 次逐欄位比對 + 存檔片段 |
| AC-07 | 單載與指定模組組合不崩潰 | 兼容報告 + 錄屏（矩陣見 `tools/dev/CompatMatrix.ps1`） |
| AC-08 | 外部模組缺失/版本不匹配安全降級 | 刪模組讀檔、偽造版本、清快取日誌 |
| AC-09 | 注入/壞存檔節點不崩潰、不壞檔 | 注入日誌 + 備份檔 |
| AC-10 | 大量建築效能可接受 | Profiler 取樣 + 錄屏（600 格/100 棟） |
| AC-11 | 四檔視覺可區分 | 27 格盲測 + LOD2 複測 + 剪影評審 |

### 7.2 其他門禁的人工部分

* **G-02**：P1 用例統計與未過項書面豁免。
* **G-03**：§15.3 效能閾值（需 Profiler）。
* **G-04**：3 次實機存讀檔往返（L1 只證明資料層）。
* **G-05**：兼容矩陣「單載 + 五組合 + 全組合 + 缺失」實機重跑；報告欄位本身已自動化。
* **G-06**：30 遊戲日壓力長跑，`HDBF/Error` 必須 0。
* **G-09**：1080p / 1440p UI 無截斷（鍵缺失已自動化）。

### 7.3 已知未完成/未驗證（誠實清單）

以下直接引用 [docs/VERIFICATION.md](VERIFICATION.md) 的現況，發佈前必須逐項確認或豁免：

1. **D-2 補丁預設值決策**（§3.1 註記）：VERIFICATION §19.2 的「預設關閉」與 shipped `true`
   不一致；ADR-009 要求「實機驗證通過才可保留」。需 Lead 決策並更新
   `REQ_TRACEABILITY` / `VERIFICATION`（本卡不越權改這兩份）。
2. **端口實際行為**（接電/接管/出帶/斷線回退）與「存量存檔 + 增刪模組」：需可互動遊戲會話。
3. **TC-09 版本不匹配的優雅降級**：仍需「只有 HDBF + 一個不相容外部模組」的乾淨對照。
4. **P1 作物循環完整 25 天拉伸週期收穫、非託管原版作物回歸量測**：VERIFICATION §20.2/§21。
5. **UI 實機**：面板互動、世界圖標、1080p/1440p 版面。
6. **美術**：仍為陣營正確的原版佔位模型 + 凍結契約（`data/ART_PLACEHOLDER.md`）。

### 7.4 需要人類覆核的實作決定

* `TestHooks`（§3.4）：保留但 inert、不可從 UI/命令觸達；門禁只保證「沒有跨組件引用」。
  若發佈政策要求連測試接縫都不得出貨，需另開卡以條件編譯移除（會影響 Integration 內部呼叫點）。
* **相容報告的遊戲內匯出入口**：`CompatAdapterRegistry.BuildReport()` 與
  `CompatReport.ToJson()` 已實作並由 L1/門禁驗證欄位；但目前沒有 UI/命令入口可直接匯出
  JSON（REQ-SAFE-16 的「遊戲內匯出」部分）。這是 UI 卡的範圍，REL-01 只保證資料契約。

---

## 8. 常見門禁失敗與處理

| 訊息 | 原因 | 處理 |
|---|---|---|
| `staged package contains exactly the five HDBF assemblies` | 多/少 DLL | 確認 `src\*\bin` 沒有被複製進 stage；用 `-WorkRoot` 乾淨目錄重跑 |
| `no dev diagnostics strings in any staged file` | Dev 建置的產物被當成 release stage，或資料檔含 `diag.` | 重跑不帶 `-DevDiagnostics`；檢查 staged 資料 |
| `release branch of the diagnostics partial contains no diag.* token` | 有人把診斷字串/呼叫搬到 `#else` 分支 | 移回 `#if HDBF_DEV_DIAGNOSTICS` 分支 |
| `HdbfDevDiagnostics defaults to false` | `Directory.Build.props` 預設被改開 | 改回 `false`（開啟只能靠建置參數） |
| `patch whitelist declares exactly 1 Postfix patch` | 新增/移除補丁未同步 manifest | 更新 `tools/release/release-gates.json` 並跑 L1 |
| `features.growthMultiplierPatch is a boolean` | config 缺鍵/型別錯 | 補回 boolean 鍵 |
| `compatibility report serializes exactly the chapter-10.4 fields` | `CompatReport` 與 manifest 漂移 | 兩邊一起改，L1 `CompatReportTests`/`ReleaseGateContractTests` 會先紅 |
| `*.csv key set equals enUS (missing keys = 0)` | 某語言缺 key | 用 `tools/validate` 的錯誤行補 key（三語言） |
| `L1 suite has zero failures` | 測試紅 | 先修 L1；不要用 `-SkipTests` 繞過發佈 |
| `the five mod assemblies compiled with 0 warnings / 0 errors` | 新增 warning | 修掉；`-DevDiagnostics` 下若因 const 產生 CS0162，移除不可達分支 |

---

## 9. 變更紀錄

| 版本 | 日期 | 內容 |
|---|---|---|
| 1.0 | 2026-10-25 | REL-01：診斷收斂（`HDBF_DEV_DIAGNOSTICS`）、build.ps1 發佈門禁（55 檢查）、`release-gates.json` 契約、L1 `ReleaseGateContractTests`（5 條）、本文件建立 |
