# REL-01 — 診斷收斂 + 發佈門禁（release gate card）

狀態：**完成（離線可證部分）**；實機 AC-01..AC-11 與部分 G 門禁仍需人工簽核（§8）。
日期：2026-10-25。執行者：Lead（單卡）。
工作區：`C:\Users\user\Documents\Timberborn\Mods\HighDensityBoxFarm`。
限制遵守：未改平衡數值、未改 `docs/VERIFICATION.md` 與 `docs/REQ_TRACEABILITY.md`、未部署、
未啟動 Timberborn、未跑 `build.ps1 -Install`。UI 面板 / 交付格註冊 / 灌溉 / 兼容適配器邏輯未觸碰。

---

## 1. 結論摘要

| 項目 | 結果 |
|---|---|
| 5 程序集建置（Release） | **0 warnings / 0 errors**（Core / Persistence / Gameplay / Integration / UI） |
| L1 | **PASSED 462 FAILED 0**（原 457，新增 5 條 REL-01 契約測試） |
| `tools/validate` | `5/5 OK (expected 5)`、`WARNINGS: none`、`RESULT: OK - data/ is valid.` |
| 發佈門禁（完整建置） | **PASSED 55 checks**（staging 419 KB / 29 files / 10 藍圖） |
| 發佈門禁（`-GateOnly` 既有 stage） | **PASSED 48 checks**（不含 L1/validate 摘要） |
| Dev 診斷正向對照（`-DevDiagnostics -SkipTests`） | **PASSED 52 checks**；dev stage 424 KB；marker 與 `diag.*` 均存在 |
| `-DevDiagnostics -Install` | **拒絕**（non-zero，訊息明確） |
| Transpiler 數 | **0**（全 `src` grep 無 `Transpiler`；門禁也斷言） |
| 新增 static 可變狀態 | **0**（診斷由 9 個 instance 欄位減為 0，改讀 production `PlotScanStats`） |
| 正式包二進位證據 | `HDBF.Gameplay.dll` 內 `diag.`／`scan.probe`／`dryCells`／`farm.plan`／`HDBF.DevDiagnostics` 全部不存在；dev 包全部存在 |

> 本卡**沒有**真的部署，也沒有啟動遊戲。上面全部是離線（編譯/測試/靜態）證據；實機驗證清單見 §8。

---

## 2. 改了什麼（file:line）

### 2.1 診斷收斂（compile-time gate）

| 檔案 | 變更 | 行 |
|---|---|---|
| `src/HDBF.Gameplay/Components/HdbfBoxFarmController.Diagnostics.cs` | 整份 dev 診斷包進 `#if HDBF_DEV_DIAGNOSTICS`；新增 `[assembly: AssemblyMetadata("HDBF.DevDiagnostics", "HDBF_DEV_DIAGNOSTICS")]` 可稽核 marker；新增 `#else` release 空實作（4 個 `Diag*` seam），讓 Scan/Simulation/Farming 的無條件呼叫點照常編譯；移除 `FarmDiagnostics` const 與 4 個 `if (!FarmDiagnostics) return;`（const true 造成 CS0162） | 18（`#if`）、29（marker）、277（`#else`）、310（`#endif`） |
| `Directory.Build.props` | 新增單一 MSBuild 屬性 `HdbfDevDiagnostics`（預設 `false`）與條件式 `DefineConstants`；註解說明為何不用 `features.devDiagnostics` | 63、65–66 |
| `src/HDBF.Integration/Harmony/HdbfGrowthPatchBridge.cs` | round 14–16 的 ADR-009 取證行 `compat.harmony-patch-applied.1..12` 包進同一個 `#if`（VERIFICATION §21.1 指定 REL-01 要收斂） | 199–215 |
| `src/HDBF.Core/Logging/HdbfLogKeys.cs` | `HarmonyPatchApplied` 常數（`"compat.harmony-patch-applied"`）只在 dev 編譯存在，正式 Core 不含此字串 | 113–122 |
| `src/HDBF.Gameplay/Components/HdbfBoxFarmController.Scan.cs` | 刪除 9 個 `_diag*` 診斷專用計數器與其 17 個遞增點/9 個 reset 點（與 `_scanStats.*` 完全重複）；診斷改讀 production counters。保留 `_scanStats.Scanned++` / `ResetScanCounters()` 等 production 行為 | 163、529–532 |
| `src/HDBF.Gameplay/Components/HdbfBoxFarmController.Diagnostics.cs` | `DiagScanEnd` 改印 `_scanStats.Scanned/Accepted/TerrainRejects/...`，訊息欄位名不變（`kept=/added=/dropped=/coverageAfterScan=` 仍在，BUG-05 L1 契約不受影響） | 61–97 |

**為什麼選條件編譯而不是 `HDBF_Config.json` 的 `features.devDiagnostics`**（任務 1 的二選一）：

1. G-08 要求「發佈包不含」，不是「執行期關掉」。config flag 會把整份診斷程式碼與 `diag.*`
   字串留在 DLL，玩家改一行 JSON 就能重新打開 → 違反 REQ-BAL-27「發佈版不暴露除錯入口」。
2. 條件編譯可做**可稽核的 byte 層斷言**（`build.ps1` G-08 掃 ASCII + UTF-16），關掉就是
   二進位層不存在，不是靠預設值。
3. 單一建置參數、不新增 config schema；dev 產物不可部署（`-Install` 拒絕）且門禁會反轉驗證。
4. 正式版零成本：無執行期分支、無診斷字串、無重複計數器。

代價：取證要重新建置（不能只改 config）——已寫入 `docs/OPS.md` §4，並以 dev 正向對照證明開關有效。

### 2.2 發佈門禁（build.ps1）

| 檔案 | 變更 | 行 |
|---|---|---|
| `build.ps1` | 新參數 `-WorkRoot`（沙箱/CI）、`-DevDiagnostics`、`-GateOnly`/`-GateStage`、`-ReleaseManifest` | 38–50 |
| `build.ps1` | `Read-BytesForTokenSearch`：同時解 ASCII（metadata #Strings / attribute）與 UTF-16LE 偶/奇位移（#US 字串常值）——第一版只解 ASCII 曾漏掉 `hdbf.highdensityboxfarm`（實測抓到，已修） | 100–110 |
| `build.ps1` | `Assert-ReleaseGate`：每個斷言印 `PASS/FAIL [G-xx] title -- detail`、累積失敗、最後 throw（non-zero） | 146–164 |
| `build.ps1` | `Invoke-ReleaseGates`：全部門禁（套件形狀/檔案黑名單/config 黑鍵/診斷標記/補丁白名單/10.4 欄位/三語系/validate/test/build warnings） | 167–533 |
| `build.ps1` | `-DevDiagnostics -Install` 提早拒絕 | 544 |
| `build.ps1` | `-GateOnly -GateStage` 只重稽核既有 stage（供 CI 與負向對照） | 547–556 |
| `build.ps1` | 門禁呼叫點：staging 之後、`-Install` 之前；工作區安全檢查移到建置之前 | 685 |
| `tools/release/release-gates.json` | **新增**：門禁的機器可讀契約（5 程序集、診斷 forbidden strings/marker、補丁上限/開關、10.4 13 欄位、三語系、測試接縫、L1 下限 460） | 整份 |
| `tests/HDBF.Tests/HDBF.Tests.csproj` | embed `release-gates.json`、`Directory.Build.props`、`data/HDBF_Config.json` 供 L1 交叉驗證 | 91–99 |
| `tests/HDBF.Tests/ReleaseGateContractTests.cs` | **新增 5 條 L1**（manifest 形狀、診斷預設關閉/條件編譯、補丁上限與可關契約、10.4 欄位、三語系 key 相等） | 34、72、148、198、233 |

### 2.3 文件

| 檔案 | 內容 |
|---|---|
| `docs/OPS.md` | **新增**：建置指令、G-01..G-09 對照自動檢查、補丁/開關清單、dev 診斷開關法、`HDBF/*` 日誌解讀、套件內容、人工簽核與失敗排查 |
| `docs/workstreams/rel-01-release-gates.md` | 本報告 |

> 依卡面限制，**沒有**改 `docs/VERIFICATION.md`、`docs/REQ_TRACEABILITY.md`；也沒有動
> `data/`（平衡/開關預設值）、UI、灌溉、交付格註冊、兼容適配器實作。

---

## 3. 診斷開/關方式

### 關（正式發佈，預設）

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
# 等價：dotnet build ... -p:HdbfDevDiagnostics=false（或省略）
```

* `HdbfDevDiagnostics` 預設 `false` → 不定義 `HDBF_DEV_DIAGNOSTICS` → Diagnostics.cs 走 `#else`
  空實作、取證行與 log key 不存在。
* 正式 `HDBF.Gameplay.dll` 實測：`diag.`/`scan.probe`/`dryCells`/`farm.plan`/`HDBF.DevDiagnostics`
  **0 命中**（ASCII + UTF-16 掃描）。

### 開（取證）

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -DevDiagnostics -SkipTests -WorkRoot "$env:TEMP\hdbf-dev"
# 或只重編 Gameplay：
dotnet build src\HDBF.Gameplay\HDBF.Gameplay.csproj -c Release -m:1 `
  -p:HdbfDevDiagnostics=true -p:HdbfWorkRoot="$env:TEMP\hdbf-dev"
```

* 會多出 marker、`diag.*` 行、`compat.harmony-patch-applied.1..12`。
* `build.ps1` 門禁反轉：**必須**看到 marker 與 `diag.*` 才 PASS；並印
  `DEV-DIAGNOSTICS BUILD ... NOT releasable`。
* `-DevDiagnostics -Install` 直接 throw（exit 1）。
* 要把 dev 包放進遊戲取證，必須手動複製 `<WorkRoot>\stage\HDBF`（見 OPS §4.2）；取證完畢務必
  重建正式包。

---

## 4. 門禁清單與對照（摘要）

完整 55 項由 `build.ps1` 的 `Invoke-ReleaseGates` 執行（`docs/OPS.md` §2.1 逐組說明）。分組：

| 群組 | 檢查數 | Gate | 內容 |
|---|---|---|---|
| staging 形狀 | 6 | G-08 | 非空/5 DLL/PE/檔案類型/檔名黑名單/config 黑鍵 |
| dev 診斷 | 7 | G-08 | 來源 `#if/#else/#endif`、release 分支無 token、屬性預設 false、條件 define、staged 全檔掃描、marker |
| 補丁清單 | 14 | G-08 | 上限 1、Postfix、單一 Harmony id、0 Transpiler、單一 patch body、可關/可卸載、shipped config 開關 boolean、staged DLL 內容 |
| 測試接縫 | 1 | G-08 | `TestHooks` 只在 `src/HDBF.Integration` |
| 相容報告 | 3 | G-05 | 13 欄位集合/順序、`ToJson()` |
| 本地化 | 19 | G-09 | 三語系存在/表頭/重複/空字串/key 相等（17）＋ validate `RESULT: OK`/無 `[localization]`（2） |
| 建置/validate | 2 | G-08 | 0 warnings/0 errors、validate 5/5 |
| L1 | 3 | G-01 | 摘要存在、`FAILED 0`、`PASSED >= 460` |

`-GateOnly` 只跑套件/原始碼層（48 項；L1/validate 摘要與 build log 檢查需完整建置）。

---

## 5. 負向對照（門禁真的會 FAIL）

以 `-GateOnly` 對 staging 複本注入破壞，全部 exit 1：

| # | 注入方式 | 實際輸出（節錄） |
|---|---|---|
| N1 | `ART_PLACEHOLDER.md` 追加 `REL-01 negative control: diag.scan.begin ...` | `FAIL [G-08] no dev diagnostics strings in any staged file ... -- ART_PLACEHOLDER.md contains "diag."` |
| N2 | 從 shipped `HDBF_Config.json` 移除 `features.growthMultiplierPatch`（JSON 仍合法） | `FAIL [G-08] shipped HDBF_Config.json exposes the disable switch features.growthMultiplierPatch` + `FAIL ... features.growthMultiplierPatch is a boolean` |
| N3 | 用多要求 `cheatPanel`（14 欄位）的 manifest 跑 `-ReleaseManifest` | `FAIL [G-05] the chapter-10.4 report field list has 13 fields` + `FAIL ... compatibility report serializes exactly the chapter-10.4 fields -- cheatPanel (<=)` |
| N4 | 刪除 staging 的 `HDBF.UI.dll` | `FAIL [G-08] staged package contains exactly the five HDBF assemblies -- expected: ...HDBF.UI.dll...` |
| N5 | 對 **release** stage 用 `-DevDiagnostics` 跑（反向斷言） | `FAIL [G-08] POSITIVE CONTROL: dev build carries the HDBF.DevDiagnostics assembly marker` + `... contains diag.* evidence strings` |

正向對照：

| 對象 | 結果 |
|---|---|
| 正常 release stage（完整建置） | `RELEASE GATES: PASSED (55 checks)`、exit 0 |
| 正常 release stage（`-GateOnly`） | `RELEASE GATES: PASSED (48 checks)`、exit 0 |
| dev stage（`-DevDiagnostics -SkipTests`） | `RELEASE GATES: PASSED (52 checks)`、`POSITIVE CONTROL: ... marker` / `... diag.*` PASS、exit 0 |
| `-DevDiagnostics -Install` | exit 1，`-Install is refused with -DevDiagnostics ...` |

可重現指令（在 repo 根）：

```powershell
$w = Join-Path $env:TEMP 'hdbf-card-work'
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -WorkRoot $w            # 55 PASS
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -WorkRoot $w -GateOnly -GateStage "$w\stage\HDBF"  # 48 PASS
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -WorkRoot "$env:TEMP\hdbf-dev" -DevDiagnostics -SkipTests  # 52 PASS
```

> 注意：本卡沙箱的 `ExecutionPolicy` 會擋未簽章腳本，必須用 `-ExecutionPolicy Bypass -File`；
> 直接 `& .\build.ps1` 會得到 SecurityError（這不是腳本錯誤）。

---

## 6. 二進位層證據（release vs dev）

對 staged `HDBF.Gameplay.dll` 以 **ordinal** 掃 ASCII + UTF-16LE（偶/奇位移）：

| Token | Release | Dev |
|---|---|---|
| `diag.` | 不存在 | 存在 |
| `scan.probe` | 不存在 | 存在 |
| `dryCells` | 不存在 | 存在 |
| `farm.plan` | 不存在 | 存在 |
| `HDBF.DevDiagnostics` | 不存在 | 存在 |
| `HDBF.Integration.dll` 的 `hdbf.highdensityboxfarm` | 存在（UTF-16） | 存在 |
| `Transpiler`（Integration） | 不存在 | 不存在 |

補充：門禁第一版只以 ASCII 解 DLL，`hdbf.highdensityboxfarm` 因為是 UTF-16 字串常值而誤報；
已修成 ASCII + UTF-16（offset 0/1）雙解碼，並保留此案例作為「門禁自身也要被驗證」的紀錄。

---

## 7. L1 新增測試（純資料契約，無遊戲組件）

`tests/HDBF.Tests/ReleaseGateContractTests.cs`（embedded：`release-gates.json`、
`Directory.Build.props`、`data/HDBF_Config.json`、`HdbfBoxFarmController.Diagnostics.cs`、
`HdbfPatchCatalog.cs`、`HarmonyBootstrap.cs`；只用 `System.Text.Json` + `HDBF.Core`）：

| 測試 | 釘住什麼 |
|---|---|
| `TestReleaseGateManifestIsWellFormed` | manifest 版本、G-01..G-09、5 程序集、13 欄位、補丁上限 1、L1 下限 |
| `TestDevDiagnosticsAreCompileTimeOnlyAndOffByDefault` | `defaultEnabled=false`、property 預設 false/條件 define、`#if/#else` 行首結構、release 分支無 `diag.*`、dev 分支保留 4 個 seam、**shipped config 不得有 `features.devDiagnostics`** |
| `TestPatchGateMatchesShippedCatalogAndIsDisableable` | 恰 1 Postfix、單一 Harmony id、單一目標方法、無 Transpiler、bootstrap 查 `IsPatchEnabled`/`Unload`、shipped config 的 `growthMultiplierPatch` 存在且 boolean、codec 可解析 true/false |
| `TestCompatReportCarriesEveryReleaseGateField` | 13 欄位集合、**順序等於 table 10-4**、`ReportVersion` 與 manifest 一致 |
| `TestAllShippedLocalizationsHaveTheSameKeys` | 三語系表頭、重複/空字串、key 集合與 enUS 完全相等（含 zhTW） |

任何「門禁清單與程式碼/資料漂移」都會先讓 L1 紅，而不是只靠人工比對。

---

## 8. 未完成 / 需要人工簽核（誠實）

### 8.1 11 條硬驗收（AC-01..AC-11，G-07）——全部仍需實機證據

| ID | 內容 | 缺什麼 |
|---|---|---|
| AC-01 | 四檔可放置/建造/研究解鎖 | 建造面板逐檔截圖 + 科技前置 |
| AC-02 | 工作範圍與託管格綁定 | 掃描/排除/不可達截圖（L1 部分已證） |
| AC-03 | 農民種植/收穫、無農民不產出 | 有/無農民各 1 日錄屏 + 產出統計 |
| AC-04 | 灌溉不足停止生長/減產 | 四檔 `C_min` 邊界 + `0.25×C_min` 面板 |
| AC-05 | 維護懲罰/P0 圖標 | 逾期 1 週期、連續逾期、頂級斷電 |
| AC-06 | 存讀檔不丢狀態 | 實機往返 3 次逐欄位（L1 只到資料層） |
| AC-07 | 單載/組合不崩潰 | 兼容矩陣實機（round 17–20 已有部分） |
| AC-08 | 缺失/版本不匹配降級 | 刪模組讀檔、偽造版本、清快取 |
| AC-09 | 注入/壞檔節點不崩潰 | 注入日誌 + 備份檔 |
| AC-10 | 大量建築效能 | Profiler 取樣 + 錄屏 |
| AC-11 | 四檔視覺可區分 | 27 格盲測 + LOD2 + 剪影評審 |

### 8.2 其他人工門禁

* **G-02**（P1 ≥95%）、**G-03**（效能閾值）、**G-06**（30 日長跑 `HDBF/Error=0`）：
  自動門禁只能覆蓋單元/資料層。
* **G-05 矩陣**：報告欄位已自動化；「單載 + 五組合 + 全組合 + 缺失」仍要實機跑
  `tools/dev/CompatMatrix.ps1`。
* **G-09 UI 版面**：1080p/1440p 無截斷仍需實機；key 缺失已自動化（含 zhTW）。

### 8.3 需要 Lead/人類決策的 3 件事

1. **D-2 補丁預設值**：`docs/VERIFICATION.md` §19.2 記錄「發佈預設先關閉
   （`growthMultiplierPatch=false`）」，但現行 shipped config 與 `HdbfConfig` 預設都是 `true`
   （round 16 已驗證四層乘算）。REL-01 **沒有**擅自改值（卡面禁止改平衡/預設）；
   門禁只保證「可關」。請在發佈前決定並更新 VERIFICATION / REQ_TRACEABILITY。
2. **`TestHooks` 是否隨包**：它是 inert 的測試接縫、無 UI/命令入口，門禁只保證
   「只有 `src/HDBF.Integration` 引用」。若發佈政策要求連測試接縫都不得出貨，需另開卡用
   條件編譯移除（會動 Integration 內部呼叫點）。
3. **相容報告的遊戲內匯出**：`CompatAdapterRegistry.BuildReport()` + `CompatReport.ToJson()`
   已實作並由門禁/L1 驗證 13 欄位；但沒有 UI/命令入口可直接匯出 JSON（REQ-SAFE-16 的
   「遊戲內匯出」部分屬 UI 卡）。REL-01 只保證資料契約。

### 8.4 刻意沒做的事

* 沒跑 `build.ps1 -Install`、沒部署、沒啟動 Timberborn（卡面硬規則）。
* 沒改 `docs/VERIFICATION.md`、`docs/REQ_TRACEABILITY.md`（Lead 負責）。
* 沒改 UI 面板、交付格註冊、灌溉、兼容適配器邏輯（各有其他卡）。
* 沒改 `data/` 的平衡/開關預設值；`HDBF_Config.json` 只被門禁**讀取**驗證。
* 工作區非 git repo（`git status` 不可用），因此本報告以 file:line 與門禁輸出為變更證據。

---

## 9. 已知風險 / 後續建議

1. **門禁清單與 manifest 的同步**：`build.ps1` 以 `tools/release/release-gates.json` 為契約，
   L1 會交叉驗證；但 `build.ps1` 內仍硬編了少量 gate id 與標題。改檢查時請同時跑完整建置 +
   L1，並保留負向對照（§5）作為門禁自測。
2. **`l1.minTests=460`**：目前 462；若日後刪減測試，門禁會紅——這是刻意的防回歸。新增測試
   不需要改 manifest（下限只會更容易滿足）。
3. **`-SkipTests`**：正式發佈不可使用；它會跳過 L1 門禁並印黃字警告。CI 建議永遠跑完整命令。
4. **Dev 產物誤放**：`-DevDiagnostics` 的 `<WorkRoot>\stage` 含 marker；若手動部署後忘了重建，
   release 門禁仍能在 staging 抓到（前提是走 `build.ps1`）。建議取證 session 結束後立刻重跑
   正式建置並再次 `-GateOnly` 稽核部署檔案。
