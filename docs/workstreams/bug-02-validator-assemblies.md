# bug-02 — Validator 程序集口徑收緊（`tools/validate`）

狀態：**完成（離線驗證；未跑 build.ps1）**
改動範圍：只有 `tools/validate/Program.cs`（單檔）；新增本報告。

## 1. 問題與根因

- 改前觀測（`docs/workstreams/tech-data.md` §6.2）：`--assemblies %USERPROFILE%\.hdbf\work\bin` 會輸出
  `mod assemblies  : 7 (ApiIndex.dll, HDBF.Core.dll, ..., HDBF.Tests.dll, HDBF.UI.dll)`。
- 根因：舊 `DiscoverModAssemblies`（`tools/validate/Program.cs` 舊版行 190-251）把 `--assemblies` 根目錄下
  **每個子目錄**都當成 `<子目錄名>.dll` 載入；它既不檢查白名單，也不檢查檔案內真正的 `AssemblyName`。
  `validate` 專案只因資料夾名 `validate` 與產物 `HdbfValidate.dll` 不同，才偶然沒被算入。
- 口徑：模組只出貨 5 個遊戲程序集（與 `build.ps1:88` staging 清單一致）：
  `HDBF.Core`、`HDBF.Persistence`、`HDBF.Gameplay`、`HDBF.Integration`、`HDBF.UI`。

## 2. 改動內容（file:line 以本報告當下的最終檔為準）

| 位置 | 改動 |
|---|---|
| `tools/validate/Program.cs:16-29` | 新增 `ExpectedModAssemblies` 明確白名單（5 個 AssemblyName）。不再用資料夾名推斷。 |
| `tools/validate/Program.cs:108-110` | Main 先取得 discovery 結果物件，再把其 `Assemblies` 交給 `GameMetadata.Load`。 |
| `tools/validate/Program.cs:119-122` | 保留原本 `mod assemblies  : N (檔名...)` 行；新增可稽核斷言行 `mod assemblies  : 5/5 OK (expected 5)`（FAIL 時 `4/5 FAIL (expected 5; missing: HDBF.Gameplay)`）；缺件問題寫入 report。 |
| `tools/validate/Program.cs:209-310` | 重寫 `DiscoverModAssemblies`：遞迴掃描 search root 內所有 `*.dll`，用 `AssemblyName.GetAssemblyName` 讀真正的 assembly identity，只接受白名單中的名稱。同一 assembly 在 bin 裡常有多份副本（例如每個專案輸出都複製 `HDBF.Core.dll`），依「專案自己資料夾 > Release > Debug > netstandard2.1 > net9.0/其他 TFM > 路徑序」只選一份。`--assemblies` 未提供時沿用 `src\<Project>\bin\<Config>\<tfm>\<Project>.dll` 的舊探路（同一套掃描）；明確提供但不存在的 root → error。 |
| `tools/validate/Program.cs:313-352` | 遞迴 DLL 列舉；個別不可讀目錄跳過而不是整個中止。 |
| `tools/validate/Program.cs:360-403` | 去重偏好排序輔助函式。TFM 沒有寫死：任何 TFM 子目錄的 DLL 都掃得到，只是排序偏好 `netstandard2.1`。 |
| `tools/validate/Program.cs:409-425` | 斷言文字：discovered==5 且 readable==5 且 missing==0 才 `OK`，否則 `FAIL`。 |
| `tools/validate/Program.cs:431-460` | 缺件是硬錯誤：逐項列出缺哪個 `.dll`、掃過的 root、預期 layout，以及被拒絕的候選（檔名像但 AssemblyName 不符）。 |
| `tools/validate/Program.cs:462-471` | 小型結果容器 `ModAssemblyDiscovery`。 |

**不變**：`--managed` / `--ref` / data 檢查語意、既有輸出欄位與順序、exit code 意義（0=OK，1=問題，2=無法啟動）。
**刻意變嚴**：明確傳入的 `--assemblies` root 不存在，或 5 個程序集缺一 → 明確 error 且 exit 1，不再靜默降級。

## 3. 建置（沙箱，寫入 `$env:TEMP`）

```powershell
$w = Join-Path $env:TEMP 'hdbf-card-work'
dotnet build tools\validate\validate.csproj -c Release -m:1 -p:HdbfWorkRoot="$w"
```

結果：`已成功生成。 0 個警告 0 個錯誤`（exit 0）。
產物實際路徑：`$w\bin\validate\Release\net9.0\HdbfValidate.dll`
（資料夾是專案名 `validate`，不是 AssemblyName `HdbfValidate`；與任務描述的預期路徑不同，以實際為準。）
本 session 的 `$env:TEMP` = `C:\Users\user\AppData\Local\Temp\dsh-djVeLV`；沙箱**沒有**拒絕寫入。

## 4. 正常案例（唯讀指向 Lead 已建置好的 `%USERPROFILE%\.hdbf\work\bin`）

命令：

```powershell
$w = Join-Path $env:TEMP 'hdbf-card-work'
dotnet "$w\bin\validate\Release\net9.0\HdbfValidate.dll" data `
  --managed "C:\Program Files (x86)\Steam\steamapps\common\Timberborn\Timberborn_Data\Managed" `
  --ref "$env:USERPROFILE\.hdbf\ref" `
  --assemblies "$env:USERPROFILE\.hdbf\work\bin"
```

輸出末段（exit 0）：

```
  game assemblies : 660
  spec types      : 506 (game + mod ComponentSpec subclasses)
  mod assemblies  : 5 (HDBF.Core.dll, HDBF.Gameplay.dll, HDBF.Integration.dll, HDBF.Persistence.dll, HDBF.UI.dll)
  mod assemblies  : 5/5 OK (expected 5)
  blueprints      : 10

WARNINGS: none

RESULT: OK - data/ is valid.
```

該 root 實際含 7 個專案資料夾（ApiIndex、HDBF.Core、HDBF.Gameplay、HDBF.Integration、HDBF.Persistence、
HDBF.Tests、HDBF.UI）＋ `validate` ＋ 各專案輸出的相依副本；計數仍是 5，證明 ApiIndex/HDBF.Tests/validate
與 `HDBF.Core.dll` 副本都不會被算成多餘模組程序集。

## 5. 負向對照（實際執行；fixtures 建在 `$env:TEMP`）

固定前置：

```powershell
$srcBin = Join-Path $env:USERPROFILE '.hdbf\work\bin'
$validator = Join-Path $env:TEMP 'hdbf-card-work\bin\validate\Release\net9.0\HdbfValidate.dll'
$common = @('data',
  '--managed','C:\Program Files (x86)\Steam\steamapps\common\Timberborn\Timberborn_Data\Managed',
  '--ref',"$env:USERPROFILE\.hdbf\ref")
```

### 5.1 缺件：假 root 只有 4 個，另加多餘的 ApiIndex / HDBF.Tests

```powershell
$fake = Join-Path $env:TEMP 'hdbf-fake-bin-missing-gameplay'
foreach ($a in @('HDBF.Core','HDBF.Persistence','HDBF.Integration','HDBF.UI')) {
  $dst = Join-Path $fake "$a\Release\netstandard2.1"
  New-Item -ItemType Directory -Force -Path $dst | Out-Null
  Copy-Item (Join-Path $srcBin "$a\Release\netstandard2.1\$a.dll") $dst
}
foreach ($x in @(@('ApiIndex','net9.0'), @('HDBF.Tests','net9.0'))) {
  $dst = Join-Path $fake "$($x[0])\Release\$($x[1])"
  New-Item -ItemType Directory -Force -Path $dst | Out-Null
  Copy-Item (Join-Path $srcBin "$($x[0])\Release\$($x[1])\$($x[0]).dll") $dst
}
& dotnet $validator @common --assemblies $fake
"EXIT=$LASTEXITCODE"
```

觀測（exit 1）：

```
  mod assemblies  : 4 (HDBF.Core.dll, HDBF.Integration.dll, HDBF.Persistence.dll, HDBF.UI.dll)
  mod assemblies  : 4/5 FAIL (expected 5; missing: HDBF.Gameplay)
  ...
ERRORS (13):
  [assemblies] expected 5 HDBF mod assemblies, found 4; missing: HDBF.Gameplay.dll
  [assemblies] missing mod assembly: HDBF.Gameplay.dll (no *.dll with AssemblyName 'HDBF.Gameplay' was found)
  [assemblies] searched root: C:\Users\user\AppData\Local\Temp\dsh-djVeLV\hdbf-fake-bin-missing-gameplay (recursively, *.dll; accepted AssemblyName: HDBF.Core, HDBF.Persistence, HDBF.Gameplay, HDBF.Integration, HDBF.UI)
  [assemblies] expected layout: ...\hdbf-fake-bin-missing-gameplay\<AssemblyName>\<Release|Debug>\<tfm>\<AssemblyName>.dll
  [metadata] HdbfBoxFarmSpec was not found in any built mod assembly. ...
  [spec] ... unknown spec 'HdbfBoxFarmSpec' ...
RESULT: FAILED - fix the problems above.
```

多餘的 ApiIndex/HDBF.Tests 沒有被計入（仍是 4，不是 6）；後續 `[metadata]`/`[spec]` 錯誤是缺
`HDBF.Gameplay` 的既有下游後果（沒有 Gameplay 就沒有 `HdbfBoxFarmSpec`），不是新邏輯產生的。

### 5.2 資料夾名陷阱：`HDBF.UI.dll` 的內容其實是 `HDBF.Core`

```powershell
$fake = Join-Path $env:TEMP 'hdbf-fake-bin-name-trap'
foreach ($a in @('HDBF.Core','HDBF.Persistence','HDBF.Gameplay','HDBF.Integration')) {
  $dst = Join-Path $fake "$a\Release\netstandard2.1"
  New-Item -ItemType Directory -Force -Path $dst | Out-Null
  Copy-Item (Join-Path $srcBin "$a\Release\netstandard2.1\$a.dll") $dst
}
$dst = Join-Path $fake 'HDBF.UI\Release\netstandard2.1'
New-Item -ItemType Directory -Force -Path $dst | Out-Null
Copy-Item (Join-Path $srcBin 'HDBF.Core\Release\netstandard2.1\HDBF.Core.dll') (Join-Path $dst 'HDBF.UI.dll')
& dotnet $validator @common --assemblies $fake
"EXIT=$LASTEXITCODE"
```

觀測（exit 1）——檔案在，但 AssemblyName 不是 HDBF.UI，因此不算 UI，並明確說明：

```
  mod assemblies  : 4/5 FAIL (expected 5; missing: HDBF.UI)
  ...
ERRORS (5):
  [assemblies] expected 5 HDBF mod assemblies, found 4; missing: HDBF.UI.dll
  [assemblies] missing mod assembly: HDBF.UI.dll (no *.dll with AssemblyName 'HDBF.UI' was found)
  [assemblies] searched root: ...\hdbf-fake-bin-name-trap (recursively, *.dll; accepted AssemblyName: ...)
  [assemblies] expected layout: ...\<AssemblyName>\<Release|Debug>\<tfm>\<AssemblyName>.dll
  [assemblies] name mismatch: ...\hdbf-fake-bin-name-trap\HDBF.UI\Release\netstandard2.1\HDBF.UI.dll is named HDBF.UI.dll but its AssemblyName is 'HDBF.Core'; only the assembly identity counts, so it is not a HDBF.UI candidate
RESULT: FAILED - fix the problems above.
```

### 5.3 附加正向對照（證明沒有退化成「只認資料夾形狀」）

| fixture | 內容 | 結果 |
|---|---|---|
| `$env:TEMP\hdbf-fake-bin-flat` | 5 個 HDBF dll 平鋪在同一層（無案號/Config/TFM 子目錄）＋多餘 ApiIndex.dll、HDBF.Tests.dll | `5/5 OK (expected 5)`、`RESULT: OK`、exit 0 |
| `$env:TEMP\hdbf-fake-bin-debug-other-tfm` | 混用 `Debug\netstandard2.1`、`Debug\net8.0`、`Debug\net9.0`、`Release\netstandard2.1` | `5/5 OK (expected 5)`、`RESULT: OK`、exit 0 |

兩者都證實：Release/Debug、`netstandard2.1` 與其他 TFM 子目錄、以及非標準資料夾形狀都處理得到；
多餘 DLL 一律不計入。

## 6. Lead 全量門禁預期看到什麼

我沒有跑 `build.ps1`（依任務邊界）。Lead 跑完整 `build.ps1 -Install` 時，validate 階段（`build.ps1:77-79`）
應看到 exit 0 且輸出含：

```
  mod assemblies  : 5 (HDBF.Core.dll, HDBF.Gameplay.dll, HDBF.Integration.dll, HDBF.Persistence.dll, HDBF.UI.dll)
  mod assemblies  : 5/5 OK (expected 5)
...
RESULT: OK - data/ is valid.
```

可稽核的 grep 目標：

- 通過：`mod assemblies  : 5/5 OK (expected 5)`
- 失敗：`mod assemblies  : N/5 FAIL (expected 5; missing: ...)`，並在 `ERRORS` 區有 `[assemblies]` 的
  missing dll / searched root / expected layout / name mismatch 行；exit 1。

其他門禁細節：

- 全量建置應仍是 5 assemblies 0/0、L1 tests 綠、staging 出 5 個 dll（build.ps1 `$assemblies` 清單未動）。
- 若 Lead 目錄同時有 Release 與 Debug 輸出，會固定偏好 Release；只有 Debug 也能通過（已測）。
- 若 Lead 看到 FAIL：先看 `[assemblies]` 行——它會指出是哪個 DLL 缺、掃了哪個 root、被拒的候選是什麼。
- 若手動跑 validate **不帶** `--assemblies`：會退回 `src\...\bin` 探路；本專案 build output 被
  `Directory.Build.props` 導到 `%USERPROFILE%\.hdbf\work`，所以 workspace 內通常找不到 DLL，會得到
  0/5 FAIL（exit 1）。`build.ps1` 一定會帶 `--assemblies`；手動跑時請照 §4 帶上。這個 FAIL 比舊版
  「0 個 mod assemblies + spec error」更早也更明確，但 exit code 同為 1。

## 7. 邊界與未做事項

- 未執行 `build.ps1`、未部署到 `Documents\Timberborn\Mods\HDBF`、未啟動 Timberborn。
- 未修改 `src/`、`data/`、`tests/`、`build.ps1`、`docs/VERIFICATION.md`、`docs/REQ_TRACEABILITY.md`、
  `docs/NEXT_ROUND.md`。
- `%USERPROFILE%\.hdbf` 只做唯讀取用（讀 `work\bin` 的建置產物與 `ref`）；所有 fixture、建置輸出、log
  都寫在 `$env:TEMP`。
- 未新增測試檔（`tests/` 是禁區）；驗證方式是實際 CLI 的 1 正常 + 2 負向 + 2 附加正向對照。
