# compat-01 — 五個第三方模組真實組件對照與適配器錨點修正

狀態：**完成（離線驗證；未部署、未啟動 Timberborn、未跑 build.ps1）**
卡片範圍：`src/HDBF.Integration/**`、`src/HDBF.Core/Compat/**`（新增純資料/純函式）、`src/HDBF.Core/Logging/HdbfLogKeys.cs`、`tests/HDBF.Tests/`（新增測試檔）、`tools/dev/`（新增兩個檢查工具 + 矩陣腳本）、`docs/API_FACTS.md`（新增 §11）、`docs/INTERFACES.md`（ExternalProxy 新增一個方法簽名）、本報告。
未改：平衡數值、`docs/VERIFICATION.md`、`docs/REQ_TRACEABILITY.md`、`data/manifest.json`、任何 Harmony 補丁、任何第三方 DLL（沒有複製進工作區）。

---

## 1. 結論（先看這裡）

1. 舊的 20 個「錨點」全部是**發明的名稱**，在五個真實 DLL 裡**一個都不存在**（`MISSING`，見 §4）。
   因此修正前：Timber Pipes / Power Lines 會在偵測後初始化失敗（`api-anchor-not-found`），
   Conveyor Belts v2 連偵測都會漏掉（複數 token `conveyorbelts` 對不上真實 assembly `ConveyorBelt`），
   More Mines / Hooman Stairs 雖會被偵測到，但 `api-anchors=0`（能力其實沒綁到任何 API）。
2. 已用真實組件重新建立 **19 個錨點（type + member name + signature）**，全部落在五個 DLL 的
   public 型別上；`tools/dev/ModAnchorCheck`（metadata）與 `tools/dev/ReflectAnchorCheck`
   （實際跑 `HDBF.Integration` 的 production reflection 路徑）都是 **19/19 PASS**。
3. `ExternalProxy` 現在真的按「type full name + member name + signature」快取
   (`FindMethodBySignature`)；A 級適配器只有在錨點型別與成員簽名都命中時才會 `active`，
   簽名變更會降級為 `api-anchor-signature-changed` 而不是謊報能力。
4. `tools/dev/CompatMatrix.ps1` 提供可重複的 `DryRun` / `-Install` / `-Remove` / `-CheckLog`，
   已用假的 `Player.log` 與 `%TEMP%` 內的 Mods root 完整測過所有分支（含擁有權標記、拒絕覆寫別人的
   mod、只刪自己安裝的資料夾、unowned mod 保留）。
5. 真正的跨模組握手（遊戲內載入五個 mod、端口真的接上）**尚未驗證**，留給 Lead 的實機矩陣；
   §8/§9 列出已知風險與尚未證明的事項。

---

## 2. 五個模組的實體清單（2026-10-25，遊戲 1.1.2.8）

Steam Workshop 內容根：`C:\Program Files (x86)\Steam\steamapps\workshop\content\1062090\`
以下 `<root>` 都代表這個路徑；Managed 目錄為
`C:\Program Files (x86)\Steam\steamapps\common\Timberborn\Timberborn_Data\Managed`。

| 適配器 | 版本資料夾 | manifest Name / Id / Version / MinimumGameVersion | DLL（assembly identity） |
|---|---|---|---|
| `HDBF.Adapter.TimberPipes` | `<root>\3801726769\version-1.1` | Timber Pipes / `TimberPipes` / `11.4.0` / `1.1.1` | `TimberPipes.dll`（`TimberPipes`, 1.0.0.0, 225,792 B） |
| `HDBF.Adapter.PowerLines` | `<root>\3766893705\version-1.1` | Power Lines / `PowerLines` / `11.1.1` / `1.1.1` | `PowerLines.dll`（`PowerLines`, 1.0.0.0, 122,880 B） |
| `HDBF.Adapter.ConveyorBeltsV2` | `<root>\3815376360\version-1.1` | Conveyor Belts v2 / `ConveyorBelt` / `11.4.0` / `1.1.1` | `ConveyorBelt.dll`（`ConveyorBelt`, 1.0.0.0, 173,056 B）+ `ConveyorBelt.Algorithm.dll`（`ConveyorBelt.Algorithm`, 1.0.0.0, 51,200 B） |
| `HDBF.Adapter.MoreMines` | `<root>\3723216060\version-1.1.0.2` | `More Mines [ModContest1]` / `TonWolfe.MoreMines` / `1.1.20` / `1.1.0.2` | `Scripts\More Mines.dll`（assembly name **`More Mines`**（含空格）, 420,864 B）+ `Scripts\SharpNoise\SharpNoise.dll`（相依，66560 B） |
| `HDBF.Adapter.HoomanStairs` | `<root>\3682012904\Version-1.1` | Hooman Stairs / `Calloatti.HoomanStairs` / `2.0.2` / `1.1.2.4` | `hoomanstairs.dll`（assembly name **`hoomanstairs` 全小寫**, 47,104 B） |

各 mod 自己宣告的 RequiredMods（實機矩陣的風險，見 §9）：

| mod | RequiredMods |
|---|---|
| Timber Pipes | ModdableTimberborn, ModdableToolGroups, eMka.ModSettings |
| Conveyor Belts v2 | ModdableTimberborn, ModdableToolGroups, eMka.ModSettings（Optional: ModdedFactionsBuildingSupport） |
| Power Lines | TimberUi, Harmony |
| More Mines | Harmony, eMka.ModSettings, ModdableTimberborn, TimberUi, MoreTunnels.Ladder, TonWolfe.TradeOffice |
| Hooman Stairs | Harmony >= 2.4.1 |

> 這五個版本資料夾只是「已下載」；遊戲只掃描 `Documents\Timberborn\Mods`，所以必須由
> `CompatMatrix.ps1 -Install` 複製進去（實機步驟見 §7）。

### 2.1 為什麼不是「更早的 Conveyor Belts」

Workshop 根目錄下同時存在舊版 `3761295060\version-1.1\ConveyorBelt.dll`（Name `Conveyor Belts`，
Version `11.2.0`）與 `3682012904` 的 `Version-1.0`/`Version-1.1` 兩份 Hooman Stairs。
`ModAnchorCheck` / `CompatMatrix.ps1` 都**只指向卡片指定的版本資料夾**，不會用整包 workshop 根目錄
自動挑檔（整包會因同名 assembly 撞到舊版）。實機前若玩家 Mods 目錄裡已有同 manifest Id 的別份 mod，
遊戲的 `ModRepository.MarkDuplicatedIds` 可能讓兩份互相干擾，因此安裝前務必看 `-Install` 產生的
`mods-before-*.txt` 備份清單。

---

## 3. 偵測字串（before → after）

| 適配器 | 舊偵測資料 | 實際 manifest | 修正後 |
|---|---|---|---|
| TimberPipes | aliases `Timber Pipes`, `TimberPipes`, `Timber Pipes Continued`；token `timberpipes` | Name `Timber Pipes`, Id `TimberPipes` | aliases `Timber Pipes`, `TimberPipes`；token `timberpipes`（清掉發明的 "Continued"） |
| PowerLines | aliases `Power Lines`, `PowerLines`, `Power Lines Continued`；token `powerlines` | Name `Power Lines`, Id `PowerLines` | aliases `Power Lines`, `PowerLines`；token `powerlines` |
| ConveyorBeltsV2 | aliases `Conveyor Belts V2`, `ConveyorBeltsV2`, `Conveyor Belts`；token **`conveyorbelts`** | Name `Conveyor Belts v2`, Id **`ConveyorBelt`** | aliases `Conveyor Belts v2`, `ConveyorBelt`, `ConveyorBeltsV2`；token **`conveyorbelt`**（單數，同時命中 `ConveyorBelt` 與 `ConveyorBelt.Algorithm`） |
| MoreMines | aliases `More Mines`, `MoreMines`, `More Mines Continued`；token `moremines` | Name `More Mines [ModContest1]`, Id **`TonWolfe.MoreMines`** | aliases `More Mines`, `TonWolfe.MoreMines`；token `moremines`（normalize 後 `More Mines`→`moremines`，仍命中） |
| HoomanStairs | aliases `Hooman Stairs`, `HoomanStairs`, `Hooman Stairs Continued`；token `hoomanstairs` | Name `Hooman Stairs`, Id **`Calloatti.HoomanStairs`** | aliases `Hooman Stairs`, `Calloatti.HoomanStairs`；token `hoomanstairs` |

舊偵測的實際後果（依 `CompatDetector`/`ReflectionCompatAdapter` 邏輯推導，且已由新 L1 測試鎖住）：

| 適配器 | 修正前偵測 | 修正前初始化 | 修正後預期 |
|---|---|---|---|
| TimberPipes | 命中（assembly token） | 失敗：5 個錨點全 MISSING → `api-anchor-not-found`，健康度 Degraded | detected + enabled，`api-anchors=4` |
| PowerLines | 命中 | 失敗：4 個錨點全 MISSING | detected + enabled，`api-anchors=4` |
| ConveyorBeltsV2 | **漏偵測**（`conveyorbelts` ⊄ `conveyorbelt`，且 type 錨點全錯）→ `compat.missing` | 不會執行 | detected + enabled，`api-anchors=4` |
| MoreMines | 命中 | 成功但 `api-anchors=0`（能力沒綁到任何 API） | detected + enabled，`api-anchors=4` |
| HoomanStairs | 命中 | 成功但 `api-anchors=0` | detected + enabled，`api-anchors=3`（2 個 member 錨點 + 1 個 type-only） |

---

## 4. 錨點對照表（核心證據）

### 4.1 舊錨點：全部 MISSING（20/20）

以 `tools/ApiIndex dump` 對五個 DLL 做全型別 dump（`=== <full type name>` 逐行比對）：
**沒有任一舊錨點存在**；連 `ConveyorBelts*` / `MoreMines*` / `HoomanStairs*` 這三個 namespace
都不存在（真名是 `ConveyorBelt.*` / `TonWolfe.DeepMines.*` / `Calloatti.HoomanStairs.*`）。

| 舊錨點（`ThirdPartyProbes.cs` 修正前） | 判定 | 真實情況 |
|---|---|---|
| `TimberPipes.TimberPipeNetwork` | MISSING | 真 API 是 `TimberPipes.Services.PipeRegistry` / `Models.PipeGraph` |
| `TimberPipes.PipeNetwork` | MISSING | 同上 |
| `TimberPipes.TimberPipe` | MISSING | 元件名是 `Components.BuildingPipe` / `PipeTank` |
| `Timberborn.Pipes.TimberPipeNetwork` | MISSING | 此 namespace 在 mod 內不存在 |
| `TimberPipes.WaterPipeNetwork` | MISSING | — |
| `PowerLines.PowerLineNetwork` | MISSING | 真 API 是 `PowerLines.Services.PowerLineConnectionService` / `PowerLineGraphService` |
| `PowerLines.PowerLine` | MISSING | 元件名是 `Components.PowerLineComponent` |
| `Timberborn.PowerLines.PowerLineNetwork` | MISSING | 此 namespace 在 mod 內不存在 |
| `PowerLines.PowerNetwork` | MISSING | — |
| `ConveyorBelts.ConveyorBelt` | MISSING | namespace 應為 `ConveyorBelt.*`（無 s） |
| `ConveyorBeltsV2.ConveyorBelt` | MISSING | 同上 |
| `ConveyorBelts.BeltNetwork` | MISSING | 真 API 是 `Services.BeltRegistry` / `BeltSimulator` |
| `ConveyorBeltsV2.BeltNetwork` | MISSING | 同上 |
| `ConveyorBelts.LogisticsInput` | MISSING | — |
| `MoreMines.MoreMinesSpec` | MISSING | namespace 應為 `TonWolfe.DeepMines.*` |
| `MoreMines.MineSpec` | MISSING | 真實 spec 是 `TWOreGroupSpec` / `TWTunnelSpec` / `TWOreSpec` 等 |
| `MoreMines.MoreMinesConfigurator` | MISSING | 真實 configurator 是 `TonWolfe.DeepMines.TWDeepMinesGameConfigurator`（且 internal） |
| `HoomanStairs.HoomanStair` | MISSING | namespace 應為 `Calloatti.HoomanStairs.*` |
| `HoomanStairs.HoomanStairs` | MISSING | — |
| `HoomanStairs.StairBlockObject` | MISSING | 真實型別是 `HoomanStairsRegistry` / `HoomanStairsManager` / `StairConnection` |

### 4.2 新錨點：19/19 已被真實 DLL metadata 與 production reflection 證實

證據來源（可重跑，見 §6）：
* `ModAnchorCheck`：直接讀 workshop DLL 的 metadata（assembly identity + type + member + signature），
  並印出 DLL 絕對路徑與 manifest 版本；
* `ReflectAnchorCheck`：把 660 個遊戲 assembly + 6 個 mod assembly 載入 .NET 程序，
  呼叫真正的 `ExternalProxy.FindType` / `ExternalProxy.FindMethodBySignature`。

| # | 適配器 | assembly | type.full.name → member(sig) | DLL 證據 |
|---|---|---|---|---|
| 1 | TimberPipes | `TimberPipes` | `TimberPipes.Services.PipeRegistry.TryGetGraph(UnityEngine.Vector3Int, out TimberPipes.Models.PipeGraph) -> System.Boolean` | `<root>\3801726769\version-1.1\TimberPipes.dll` |
| 2 | TimberPipes | `TimberPipes` | `TimberPipes.Services.PipeFluidSimulator.Tick() -> System.Void` | 同上 |
| 3 | TimberPipes | `TimberPipes` | `TimberPipes.Models.PipeGraph.RefreshContamination() -> System.Void` | 同上 |
| 4 | TimberPipes | `TimberPipes` | `TimberPipes.Components.PipeTank.TakesGood(System.String) -> System.Boolean` | 同上 |
| 5 | PowerLines | `PowerLines` | `PowerLines.Services.PowerLineConnectionService.CanConnect(PowerLines.Components.PowerLineComponent, PowerLines.Components.PowerLineComponent) -> System.String` | `<root>\3766893705\version-1.1\PowerLines.dll` |
| 6 | PowerLines | `PowerLines` | `PowerLines.Services.PowerLineGraphService.OnNodeAddedToGraph(Timberborn.MechanicalSystem.MechanicalNode) -> System.Void` | 同上 |
| 7 | PowerLines | `PowerLines` | `PowerLines.Components.PowerLineComponent.ShortestDistanceTo(PowerLines.Components.PowerLineComponent) -> System.Single` | 同上 |
| 8 | PowerLines | `PowerLines` | `PowerLines.Specs.PowerLineSettingsSpec.get_DefaultMaxConnections() -> System.Int32` | 同上 |
| 9 | ConveyorBeltsV2 | `ConveyorBelt` | `ConveyorBelt.Services.BeltRegistry.Register(ConveyorBelt.Components.BeltCarrier) -> System.Void` | `<root>\3815376360\version-1.1\ConveyorBelt.dll` |
| 10 | ConveyorBeltsV2 | `ConveyorBelt` | `ConveyorBelt.Services.BeltSimulator.Tick() -> System.Void` | 同上 |
| 11 | ConveyorBeltsV2 | `ConveyorBelt` | `ConveyorBelt.Components.BeltInventory.ClearContents() -> System.Void` | 同上 |
| 12 | ConveyorBeltsV2 | `ConveyorBelt.Algorithm` | `ConveyorBelt.Algorithm.BeltSimulation.Tick(System.Single) -> System.Void` | `<root>\3815376360\version-1.1\ConveyorBelt.Algorithm.dll` |
| 13 | MoreMines | `More Mines` | `TonWolfe.DeepMines.TWOreGroupSpec.get_GroupId() -> System.String` | `<root>\3723216060\version-1.1.0.2\Scripts\More Mines.dll` |
| 14 | MoreMines | `More Mines` | `TonWolfe.DeepMines.TWTunnelSpec.get_ExplosionPrefabPath() -> System.String` | 同上 |
| 15 | MoreMines | `More Mines` | `TonWolfe.DeepMines.TWOreUpdateService.HasViewBlockingObject(UnityEngine.Vector3Int) -> System.Boolean` | 同上 |
| 16 | MoreMines | `More Mines` | `TonWolfe.DeepMines.TWOre.IsHidden() -> System.Boolean` | 同上 |
| 17 | HoomanStairs | `hoomanstairs` | `Calloatti.HoomanStairs.HoomanStairsRegistry.RemoveNode(System.Int32) -> System.Void` | `<root>\3682012904\Version-1.1\hoomanstairs.dll` |
| 18 | HoomanStairs | `hoomanstairs` | `Calloatti.HoomanStairs.HoomanStairsManager.RefreshAllBuildings() -> System.Void` | 同上 |
| 19 | HoomanStairs | `hoomanstairs` | `Calloatti.HoomanStairs.HoomanStairsPathfinder`（type-only；public 成員只有複雜簽名的 `TryGenerateInternalPath`，不適合當穩定錨點） | 同上 |

備註：
* #1 的第 2 個參數在 metadata 是 `ByRef` + `Out`，錨點資料記元素型別 `...PipeGraph`；
  `ModAnchorCheck` 依 parameter flag 顯示成 `out`。`ReflectAnchorCheck` 顯示
  `Boolean TryGetGraph(UnityEngine.Vector3Int, TimberPipes.Models.PipeGraph ByRef)`。
* 全部錨點型別都是 **public**（用 Cecil 檢查 `IsPublic`；19/19）。刻意避開 internal 的
  `TonWolfe.DeepMines.TWDeepMinesGameConfigurator`、`TonWolfe.DeepMines.TWDeepOreDepositSpec`、
  `TonWolfe.DeepMines.TWTailingsCollector`（原本候選，改成 public 的 `TWOre.IsHidden()`）
  與 internal 的 `ConveyorBelt.Components.BeltBlocks`。
* 沒有新增任何編譯期第三方引用：`HDBF.Integration.csproj` 未改；資料全部是字串
  （`HDBF.Core.Compat.CompatAnchorTable`），且 Core 的測試在沒有遊戲/第三方 DLL 的情況下就能跑。

---

## 5. 改動清單（file:line 以最終檔為準）

| 檔案 | 位置 | 改動 |
|---|---|---|
| `src/HDBF.Core/Compat/CompatAnchorTable.cs` | 新增，1–405 | 五個 adapter 的 immutable 資料表（adapter id / manifest Id / Name / aliases / assembly tokens / 19 個 anchor）。`CompatAnchorTable.All` 刻意宣告在五個 spec 之後（C# static field 初始化順序）。 |
| `src/HDBF.Core/Compat/CompatMatching.cs` | 新增，1–116 | 純函式 `Normalize` / `MatchesAssemblyName` / `MatchModName`，detector 與 L1 測試共用同一份比對語意。 |
| `src/HDBF.Core/Logging/HdbfLogKeys.cs` | 31–36 | 新增 `CompatEnabled = "compat.enabled"`（matrix parser 的契約）。 |
| `src/HDBF.Integration/Adapters/ThirdPartyProbes.cs` | 整體重寫，1–212 | 刪掉全部發明錨點；`ApiAnchor` 改為 assembly + type + member + parameter type names + return type；`ThirdPartyProbes.From(CompatAdapterSpec)` 由 Core 資料表建構。 |
| `src/HDBF.Integration/Adapters/ReflectionCompatAdapter.cs` | 137–143、214–279、311–320、353 | Initialize 改用 `BindCapability(out signatureMismatch)`；A 級錨點需 type+member signature 都命中；新增 `SignatureMatches`；signature 變更回報 `api-anchor-signature-changed`；`compat.enabled` 改在 `HDBF/Compat` 輸出。 |
| `src/HDBF.Integration/Detection/CompatDetector.cs` | 240–265、400–408 | 改用 `CompatMatching`；`ResolveAnchor` 先用 anchor 的 assembly hint，再全域 full name、再 simple name。 |
| `src/HDBF.Integration/Reflection/ExternalProxy.cs` | 170–242、293–391 | 新增 `FindMethodBySignature(Type, memberName, parameterTypeNames, returnTypeName)`：按「type AQN + member + normalized signature」存進既有 `MethodCache`；新增 `BuildSignatureKey` / `TypeDisplayName` / `ParametersMatch`。 |
| `tests/HDBF.Tests/CompatAnchorTableTests.cs` | 新增，1–359 | 15 個 L1 測試（見 §6.2）。 |
| `tools/dev/ModAnchorCheck/` | 新增（Program.cs 1–555） | metadata 錨點檢查器（System.Reflection.Metadata，零 NuGet，直接讀 workshop）。 |
| `tools/dev/ReflectAnchorCheck/` | 新增（Program.cs 1–208） | production reflection 檢查器（載入遊戲+mod assembly，呼叫 `ExternalProxy`）。 |
| `tools/dev/CompatMatrix.ps1` | 新增，1–731 | DryRun / `-Install` / `-Remove` / `-CheckLog` 矩陣流程。 |
| `docs/API_FACTS.md` | 182–248 | 新增 §11「third-party mod APIs (verified 2026-10-25)」：版本、DLL、public 型別、遊戲載入語意、signature cache。 |
| `docs/INTERFACES.md` | 344–351 | `ExternalProxy` 介面補上 `FindMethodBySignature`（向後相容的新增）。 |

行為變化（有意義的差異，其他卡若讀 log 需知道）：

1. 適配器成功時改寫 `HDBF/Compat: compat.enabled:<id> active; api-anchors=<N>`（原本是 `HDBF/Info`）。
   `CompatMatrix.ps1 -CheckLog` 兩種前綴都接受，所以舊的 `round9-Player.log` 仍可解析。
2. 新增失敗原因 `api-anchor-signature-changed`（型別在、成員簽名不在），與 `api-anchor-not-found`
   區分；兩者都走既有 3 次失敗停用語意。
3. `DetectedMods` 現在寫真實 manifest Name（`Content/...` 不變的隱私規則：仍只有名字，沒有路徑、沒有第三方型別名）。
4. 沒有新增 static 可變狀態：Core 資料表是 static readonly 且建構子複製陣列；`ExternalProxy`
   只是沿用既有（已被核准的）`TypeCache`/`MethodCache`，`ClearCache()` 一併清掉新 key。

---

## 6. 驗證（可重跑）

### 6.1 建置 / L1 / 兩個錨點檢查

沙箱命令（輸出導到 `$env:TEMP`，不寫 repo）：

```powershell
$w = Join-Path $env:TEMP 'hdbf-card-work'
dotnet build src\HDBF.Core\HDBF.Core.csproj              -c Release -m:1 -p:HdbfWorkRoot=$w
dotnet build src\HDBF.Integration\HDBF.Integration.csproj -c Release -m:1 -p:HdbfWorkRoot=$w
dotnet build tests\HDBF.Tests\HDBF.Tests.csproj           -c Release -m:1 -p:HdbfWorkRoot=$w
dotnet "$w\bin\HDBF.Tests\Release\net9.0\HDBF.Tests.dll"
```

本機結果（2026-10-25）：

```
Core:         0 warnings / 0 errors
Integration:  0 warnings / 0 errors
Tests build:  0 warnings / 0 errors
L1:           PASSED 307  FAILED 0     (既有 292 + 本卡新增 15)
```

錨點檢查：

```powershell
dotnet build tools\dev\ModAnchorCheck\ModAnchorCheck.csproj -c Release -m:1 -p:HdbfWorkRoot=$w
dotnet "$w\bin\ModAnchorCheck\Release\net9.0\ModAnchorCheck.dll" check

dotnet build tools\dev\ReflectAnchorCheck\ReflectAnchorCheck.csproj -c Release -m:1 -p:HdbfWorkRoot=$w
dotnet "$w\bin\ReflectAnchorCheck\Release\net9.0\ReflectAnchorCheck.dll"
```

結果：

```
ModAnchorCheck: 19 anchors checked, 19 PASS, 0 FAIL, 0 MISSING
                RESULT: OK - every anchor matches the installed DLL metadata.

ReflectAnchorCheck: game assemblies loaded: 660 (failed: 0)
                    mod assemblies loaded: 6/6
                    SUMMARY pass=19 fail=0 missing=0 detectionFail=0
                    RESULT: OK - production reflection resolves every verified anchor.
```

> 第二個檢查已經實際執行了 `ExternalProxy.FindType` 與 `ExternalProxy.FindMethodBySignature`
> 對真實 mod DLL 的解析（包含 `out PipeGraph` 的 ByRef 參數）。它仍然**不是**「遊戲內握手」：
> 沒有 Timberborn 世界、沒有 DI container、沒有端口事件。

### 6.2 新增 L1 測試（`tests/HDBF.Tests/CompatAnchorTableTests.cs`）

| 測試 | 鎖住的東西 |
|---|---|
| `TestChapter81AdapterOrderAndIds` | 表 8-1 的五個 adapter id 與固定順序（REQ-COMPAT-10） |
| `TestAdapterIdConstantsMatchTable` | Core 常數與資料表一致 |
| `TestDetectionStringsMatchTheRealAssembliesAndManifests` | 每個 adapter 的 assembly token / manifest Name / manifest Id 都能命中（真實字串寫在測試資料裡） |
| `TestConveyorBeltUsesTheSingularAssemblyToken` | 舊 bug：複數 token 對不上 `ConveyorBelt` |
| `TestMoreMinesAssemblyNameHasASpaceAndStillMatches` | assembly `More Mines`（含空格）仍可命中 |
| `TestUnrelatedAssembliesAndModsNeverMatch` | 缺 mod / 無關 mod 清單不誤判（缺 mod 降級的資料面） |
| `TestEveryAdapterKeepsAtLeastThreeVerifiedAnchors` | 每個 adapter ≥3 錨點；總數 19（防止靜默刪減） |
| `TestAnchorNamesAreWellFormed` | 錨點有名稱空間、member/return 非空、參數是具體非泛型名稱、ByRef 以元素型別記錄 |
| `TestApiBindingIsRequiredOnlyForLevelAAdapters` | A 級 = pipes/power/conveyor 需綁定；B 級/read-only = 免綁定；A 級每個錨點都要有 signature |
| `TestObsoleteInventedAnchorsAreGone` | 20 個舊發明名稱不得再出現 |
| `TestDocumentedSignaturesArePinned` | 幾個代表簽名（含 `out` 那條、`More Mines`/`hoomanstairs` assembly 名）固定在測試裡 |
| `TestNormalizeIsCaseAndSeparatorInsensitive` | normalize 規則 |
| `TestMissingModIsANeutralNonFailureStateForEveryAdapter` | 每個 adapter：缺 mod → Disabled、0 failure、可重試（REQ-COMPAT-09/12） |
| `TestThreeStrikesDisableEveryAdapterForTheSession` | 每個 adapter：3 次失敗 → Failed、不可重試（REQ-COMPAT-07） |
| `TestCompatLogKeysAreStableForTheMatrixParser` | `compat.missing` / `compat.failed` / `compat.enabled` 字串契約 |

測試工程仍然只引用 `HDBF.Core`（零遊戲/第三方組件），符合 REQ-TEST-03；反射只在 Integration 發生。

---

## 7. `tools/dev/CompatMatrix.ps1`（Lead 執行）

### 7.1 語意

| 參數 | 行為 |
|---|---|
| （無 action） | **DryRun**：印出每個 mod 的 source→destination、檔案數/bytes、SHA-256（tree + manifest），不寫任何檔案 |
| `-Install` | 驗證來源 manifest Id/版本 → 備份目前 Mods 目錄清單到 `%USERPROFILE%\.hdbf\compat-matrix\backups\mods-before-<ts>.txt` → 複製到 `Documents\Timberborn\Mods\HDBFCompat.<Adapter>` → 驗證 copy 後的 tree hash → 寫 `ownership.json` |
| `-Remove` | 只刪 `ownership.json` 有記錄的資料夾；完全不會碰其他 mod、不會碰 Saves；沒有 marker 就什麼都不做 |
| `-CheckLog <Player.log>` | 解析 `HDBF/Compat` 的 `compat.enabled` / `compat.missing` / `compat.failed`，逐 adapter 印 detected/enabled/failed/anchors 與 PASS/FAIL |
| `-Adapters A,B` | 只處理指定列（單模組 TC-02..TC-06 用）；預設五個 |
| `-Expect All\|None\|A,B` | `-CheckLog` 的預期安裝集合；預設等於 `-Adapters` |
| `-DryRun` | 即使帶 `-Install`/`-Remove` 也只預覽 |
| `-Force` | 允許覆蓋「本腳本自己」先前安裝的同名資料夾（重新跑同一格） |
| `-NoHash` | 略過 SHA-256（大 mod 預覽更快） |
| `-ModsRoot` / `-StateRoot` / `-WorkshopRoot` | 測試或非標準文件夾覆寫 |

安全設計：
* marker 與備份放在 `%USERPROFILE%\.hdbf\compat-matrix\`，**不在** Mods 目錄裡（避免遊戲掃到）；
* `-Install` 目標已存在且**不在** marker 裡 → 直接拒絕（不覆寫別人的 mod）；
* `-Remove` 只解析 marker 的 folder，再用白名單 + `StartsWith(ModsRoot + '\')` 防呆；marker 屬於
  另一個 ModsRoot 時拒絕；
* 不呼叫 `build.ps1`、不部署 HDBF、不啟動遊戲、不碰存檔。

DryRun 範例（本機實際輸出，`-Adapters TimberPipes -NoHash`）：

```
HDBF compatibility matrix helper (TC-02..TC-08 support)

== DRY RUN - nothing will be written
Mods root:  C:\Users\user\Documents\Timberborn\Mods
State root: C:\Users\user\.hdbf\compat-matrix
Workshop:   C:\Program Files (x86)\Steam\steamapps\workshop\content\1062090

[TimberPipes] Timber Pipes 11.4.0  (Id TimberPipes)
  source:     C:\Program Files (x86)\Steam\steamapps\workshop\content\1062090\3801726769\version-1.1
  destination:C:\Users\user\Documents\Timberborn\Mods\HDBFCompat.TimberPipes
  files:      84   bytes: 941043
  destination exists: False

== DryRun summary
To install:  powershell -NoProfile -ExecutionPolicy Bypass -File tools\dev\CompatMatrix.ps1 -Install -Adapters TimberPipes
To verify:   powershell -NoProfile -ExecutionPolicy Bypass -File tools\dev\CompatMatrix.ps1 -CheckLog <Player.log> -Adapters TimberPipes
To remove:   powershell -NoProfile -ExecutionPolicy Bypass -File tools\dev\CompatMatrix.ps1 -Remove
No files were written.
```

### 7.2 本卡已經測過的分支（都在 `%TEMP%`，不動真實 Mods/存檔）

| 情境 | 結果 |
|---|---|
| DryRun（TimberPipes） | 只印路徑/hash，TEMP 與真實 Mods 目錄都沒變 |
| `-Install -Adapters TimberPipes,ConveyorBeltsV2` | 84 + 207 files 複製、tree hash 一致、marker 寫入、備份清單含既有 `PlayerOwnMod` |
| 同一格再 `-Install` | exit 1，拒絕覆蓋自己已安裝的資料夾（要求先 `-Remove` 或 `-Force`） |
| `-Install ... -Force` | 重新安裝成功，marker 只保留本次列並保留其他已安裝列 |
| 目標資料夾存在且未擁有 | exit 1，明確拒絕覆寫 |
| `-Remove -Adapters TimberPipes` | 只刪 TimberPipes，ConveyorBeltsV2 與 unowned `PlayerOwnMod` 保留 |
| `-Remove`（全部） | 刪完 owned 資料夾、清掉 marker；`PlayerOwnMod` 仍在 |
| 無 marker 再 `-Remove` | 印 "nothing to do"，exit 0 |
| `-CheckLog docs/in-game/round9-Player.log -Expect None` | PASS（五個都 missing） |
| `-CheckLog round9 -Expect All` | FAIL（5 個 not-enabled） |
| 假 log：五個 enabled、`-Expect All` | PASS |
| 假 log：五個 enabled、`-Expect None` | FAIL（unexpected） |
| 假 log：只有 TimberPipes enabled + 其他 missing、`-Adapters TimberPipes` | PASS |
| 同上、預設 `-Adapters`（五個） | FAIL（四個 not-enabled） |
| 假 log：TimberPipes enabled + PowerLines `compat.failed`、`-Expect All` | FAIL（failed） |
| 五個全裝（`%TEMP%` 內） | 5/5 複製成功、marker 記錄 5 列、destination manifest Id/Version 與來源一致；`-Remove` 5/5 刪除並清 marker |

五個 mod 的完整安裝樹 hash（同一次 `-Install` 實測；可用來核對 copy 是否一致）：

| mod | files | bytes | tree SHA-256 |
|---|---|---|---|
| TimberPipes | 84 | 941,043 | `ADC568750B396E68A8AA5338520BDECF1C6A33A0BD0D83EA06BDA02BE409B89A` |
| PowerLines | 29 | 133,920 | `AC07625ABDC9333907568543F644EDEFDEAEDB12402ECED713A546270D2AA0EB` |
| ConveyorBeltsV2 | 207 | 901,017 | `8516A55F14AE0D9CA8D55CEFA78D0598AE4A5016B593734799BE5625E45A6987` |
| MoreMines | 643 | 36,919,795 | `6CD74AA3A3CD3571D8E41B0BD7F50615C371F901CE35D308295A061A515246EC` |
| HoomanStairs | 4 | 49,540 | `210215B1DF88867739FF17BC70867EACDE8C121EDAA243A0363E334260400558` |

（五個合計約 37.1 MB、967 個檔案；矩陣每格裝/卸都很快，且 marker 會記住每一列的 tree hash。）

---

## 8. 實機矩陣步驟與預期日誌（Lead 在遊戲內跑；本卡沒有跑）

前置（每格都一樣）：
1. 確認 `Documents\Timberborn\Mods\HDBF`（部署版）存在，且當時沒有其他同名 manifest Id 的 mod。
2. `powershell -NoProfile -ExecutionPolicy Bypass -File tools\dev\CompatMatrix.ps1 -Install -Adapters <該格集合>`
3. 用既有的自動化流程啟動（見 `docs/NEXT_ROUND.md`）：
   `tools\dev\Invoke-GameRound.ps1 -Action Start -Tag compat-tc0X`（Start = Fixture→Prepare→Launch），
   載入 `Saves\HDBFTest` 的存檔複本；遊戲用 `-skipModManager` 時不需要點 Mods 對話框。
4. 跑 1 個遊戲日（或該格要求的天數），`-Action SpeedUp -Seconds 120`，再 `-Action Stop`。
5. `Player.log` 在 `%TEMP%\hdbf-round\Player.log`（同時可另存 `docs/in-game/compat-tc0X-Player.log`）。
6. `powershell ... -CheckLog <log> -Expect <該格集合>`；exit 0 + `MATRIX RESULT: OK` 才算該格通過。

預期日誌行（adapter 成功時）：
```
HDBF/Compat: compat.enabled:HDBF.Adapter.TimberPipes active; api-anchors=4
HDBF/Compat: compat.enabled:HDBF.Adapter.PowerLines active; api-anchors=4
HDBF/Compat: compat.enabled:HDBF.Adapter.ConveyorBeltsV2 active; api-anchors=4
HDBF/Compat: compat.enabled:HDBF.Adapter.MoreMines active; api-anchors=4
HDBF/Compat: compat.enabled:HDBF.Adapter.HoomanStairs active; api-anchors=3
```
未安裝的 adapter（每格都會有）：
```
HDBF/Compat: compat.missing:HDBF.Adapter.<X>: HDBF.Adapter.<X> not detected (not-installed); capability unavailable
```
簽名/型別失效時（只有外部 mod 改版才會出現）：
```
HDBF/Compat: compat.failed:HDBF.Adapter.<X>: HDBF.Adapter.<X> api-anchor-not-found
HDBF/Compat: compat.failed:HDBF.Adapter.<X>: HDBF.Adapter.<X> api-anchor-signature-changed
```

| TC | 安裝集合 | 遊戲內步驟 | 預期 `HDBF/Compat` / 判讀 | CheckLog |
|---|---|---|---|---|
| TC-02 | Conveyor Belts v2 | 頂級建築接傳送帶，收穫 1 日；看產物是否經出帶口輸出、無重複/遺失 | Conveyor 那行 `api-anchors=4`；其餘四行 missing；不得有 `compat.failed` | `-Expect ConveyorBeltsV2` |
| TC-03 | More Mines | 建高級版並核對金屬成本；確認沒有 resource-id 衝突 log | MoreMines 那行 `api-anchors=4`；其餘 missing | `-Expect MoreMines` |
| TC-04 | Hooman Stairs | 建築旁搭平台/樓梯並鋪到屋頂上方；屋頂不可站、工作位不落平台、入口可達 | HoomanStairs 那行 `api-anchors=3`；其餘 missing | `-Expect HoomanStairs` |
| TC-05 | Power Lines | 頂級接電 1 日 → 斷電 → 再接通；面板 `G_power` 1.0→0.6→1.0、維護模式跟隨 | PowerLines 那行 `api-anchors=4`；其餘 missing | `-Expect PowerLines` |
| TC-06 | Timber Pipes | 接管道 1 日 → 拆管 1 日；接管道時水罐回升，斷管後停止補水並告警 | TimberPipes 那行 `api-anchors=4`；其餘 missing | `-Expect TimberPipes` |
| TC-07 | 五個全裝 | 重複 TC-02..TC-06 的關鍵步驟（接電/接管道/出帶） | 五行 enabled（4/4/4/4/3），不得有 `compat.failed` | `-Expect All` |
| TC-08 | 先全裝，存檔後 `-Remove -Adapters TimberPipes` | 直接讀該存檔並跑 1 日；建築降級為只讀取樣、可繼續玩 | TimberPipes 變 missing；其餘四行 enabled；不得有 `compat.failed`、`HDBF/Error` | `-Expect PowerLines,ConveyorBeltsV2,MoreMines,HoomanStairs` |

每一格結束後：`-Remove -Adapters <該格集合>`（TC-07/TC-08 可 `-Remove` 全部）。
每格的 log 切片命名建議 `<組合>-<TC>-<日期>.log`（需求 §8.5 的證據命名規則）。

---

## 9. 未驗證項目 / 已知風險（要說清楚，不能當成已驗證）

1. **沒有在遊戲內載入過這五個 mod**。本卡只證明「DLL metadata 與 production reflection 路徑」；
   真正的 mod 載入、DI 建構、端口事件、產物出帶/水罐/電網握手全部仍是 Lead 的實機矩陣工作。
2. **五個 mod 的 RequiredMods 大多不在本機**（見 §2 表）：遊戲的載入流程不檢查 RequiredMods
   （IL 證據：只有 `ModSorter` 與 `ModWarningUpdater` 讀它），所以 assembly 應該會被載入、HDBF 的
   偵測/錨點應該照樣成立；但這些 mod **自己的程式**可能因為缺少相依 mod 而報錯或功能不完整。
   如果實機 log 出現大量第三方例外，先按它們 manifest 的 RequiredMods 補齊（Harmony 已在）。
3. **載入時機**：`ModCodeStarter.Start()` 先 `LoadAssemblies()`（所有 enabled mod 的所有 dll），
   再 `StartMods()`，所以 HDBF 的 assembly scan 理論上會看到全部五個 mod；但真正的 DI container
   建構時點沒有在本卡用遊戲驗證。
4. **版本不匹配/TC-09**：目前適配器沒有版本區間檢查（mod assembly 版本全是 1.0.0.0，manifest
   版本要透過 mod manager 清單才拿得到，而 production 沒有 mod-list provider）。TC-09 不在本卡
   範圍；若需要，應另開卡片在 Integration 增加「由 mod manager 取 manifest 版本」的軟依賴。
5. **`-CheckLog` 只讀日誌**：它不能證明端口真的工作，只能證明 HDBF 的偵測/初始化狀態符合矩陣預期；
   TC-02..TC-07 的玩法結果仍需截圖/錄影（需求 §8.5）。
6. **`HDBF/Compat` enabled 行是本次新加的**：舊 log 只有 missing 行；parser 同時接受舊的
   `HDBF/Info: compat.enabled` 前綴，但只有**新部署**的版本才會產生 enabled 行。
7. **未跑 `build.ps1`**：本卡沒有做 5 assembly 全量 gate、staging、deploy SHA 比對；Lead 跑全量 gate
   時應看到與本卡相同的 Integration/Core/Tests 0 error、L1 307 綠。`data/manifest.json` 未改，所以
   `tools/validate` 的 manifest 檢查不受影響。
8. **`data/manifest.json` 的 RequiredMods/OptionalMods 刻意保持空**（題目「若適用」）：
   * HDBF 的偵測完全不讀 manifest；
   * 遊戲的 OptionalMods 只影響載入排序，而 `LoadAssemblies()` 已在 `StartMods()` 之前載完所有
     assembly，HDBF 也沒有 Harmony 補丁需要排在別人後面；
   * 現行 `tools/validate/DataChecks.cs` 要求 RequiredMods/OptionalMods 是 **string array**，而遊戲
     真正的 schema 是 `[{Id, MinVersion}]` 物件陣列；要加就必須同步改 validator（超出本卡），
     且亂填會讓「manifest 同步」變成新的風險。
   若未來要加，正確做法是：`OptionalMods: [{"Id":"TimberPipes"}, ...]` + 把 validator 改成接受
   object array（並保留 string array 的舊格式相容），再重跑 `tools/validate`。
9. **`ExternalProxy.FindMethodBySignature` 只在 .NET 程序裡對真實 DLL 驗證過**（ReflectAnchorCheck），
   還沒在 Mono/Timberborn 的 runtime 跑過；風險低（只用 `Type.GetMethods` + 名稱比對），但實機若
   出現 `api-anchor-signature-changed`，代表某個 member 在遊戲 runtime 解析不到，應看那一行並用
   `ModAnchorCheck` 重驗。

---

## 10. 重跑順序（handoff）

```powershell
# 0) 只讀檢查：錨點是否還對得上本機 workshop 版本
$w = Join-Path $env:TEMP 'hdbf-card-work'          # 或 %USERPROFILE%\.hdbf\work
dotnet build tools\dev\ModAnchorCheck\ModAnchorCheck.csproj -c Release -m:1 -p:HdbfWorkRoot=$w
dotnet "$w\bin\ModAnchorCheck\Release\net9.0\ModAnchorCheck.dll" check
# -> 19/19 PASS；若有 FAIL/MISSING，先更新 CompatAnchorTable + 本報告，不要進遊戲

# 1) 矩陣流程（DryRun 安全）
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dev\CompatMatrix.ps1 -Adapters TimberPipes

# 2) 實機單格（Lead）
powershell ... -File tools\dev\CompatMatrix.ps1 -Install -Adapters TimberPipes
tools\dev\Invoke-GameRound.ps1 -Action Start -Tag compat-tc06
tools\dev\Invoke-GameRound.ps1 -Action SpeedUp -Seconds 120
tools\dev\Invoke-GameRound.ps1 -Action Stop
powershell ... -File tools\dev\CompatMatrix.ps1 -CheckLog "$env:TEMP\hdbf-round\Player.log" -Expect TimberPipes
powershell ... -File tools\dev\CompatMatrix.ps1 -Remove -Adapters TimberPipes
```
