# tools/smoke — HDBF game smoke checker

`Invoke-GameSmoke.ps1` is the only supported way to produce game-side smoke evidence without a
human clicking. It is written for Windows PowerShell 5.1 (the command line used by `build.ps1`
and the project docs) and has no external dependencies.

**It never clicks, never loads or saves a world through the UI, and never writes into a real
settlement folder.** The only files it writes are its JSON verdict (and a copy of the game log)
under `%USERPROFILE%\.hdbf\smoke\`, plus a temporary throwaway settlement copy that it deletes.

## Modes

| Invocation | What happens | Starts the game? |
|---|---|---|
| *(no switch)* | Deploy-file check (manifest + 5 DLLs + data, SHA-256 against the staging folder, workspace safety guard) and analysis of the existing `Player.log` | no |
| `-BatchMode` | The dry-run above **plus** validation of the batchmode autostart source save and the exact game command line | no |
| `-Launch` | Windowed launch; captures only the new `Player.log` bytes, stops only its own PID | yes (window + audio) |
| `-Launch -BatchMode` | Quiet command-line autostart into a throwaway save copy (see below) | yes (no window) |

## Exact commands

```powershell
# 1. dry run: deploy checks + current-log analysis
powershell -NoProfile -ExecutionPolicy Bypass -File tools\smoke\Invoke-GameSmoke.ps1

# 2. dry-run the batchmode plan and print the exact game command line
powershell -NoProfile -ExecutionPolicy Bypass -File tools\smoke\Invoke-GameSmoke.ps1 -BatchMode

# 3. actual quiet autostart smoke (Lead decision; ~1-3 min)
powershell -NoProfile -ExecutionPolicy Bypass -File tools\smoke\Invoke-GameSmoke.ps1 -Launch -BatchMode
```

Optional parameters:

| Parameter | Default | Meaning |
|---|---|---|
| `-SourceSettlement` | `110000`, else newest folder | settlement folder under `Documents\Timberborn\Saves` |
| `-SaveName` | newest `*.timber` in it | save name **without** `.timber` |
| `-TestSettlementName` | `HDBFTest` | throwaway copy the game is pointed at |
| `-TimeoutSeconds` | 180 batchmode / 120 windowed | capture budget |
| `-SettleSeconds` | 10 | extra capture after the save-open marker / first error |
| `-KeepTestSettlement` | off | keep `Saves\HDBFTest` for inspection |
| `-KeepGameLog` | off | keep the temporary `%TEMP%\hdbf-smoke-*\Player.log` |
| `-LogPath` / `-ModDir` / `-GameDir` / `-OutputDir` | see defaults in the script | overrides for a different machine |

Exit codes: `0` dry run/plan OK, `1` dry run FAIL, `2` prerequisites missing, `3` launch FAIL,
`4` launch REFUSED by a safety precondition.

## How the batchmode autostart works (verified against 1.1.2.8)

IL facts taken from the installed game (`Timberborn.MainMenuScene.dll`, `Timberborn.CommandLine.dll`,
`Timberborn.GameSaveRepositorySystem.dll`, `Timberborn.ExperimentalModeSystem.dll`):

* `Timberborn.MainMenuScene.AutoStarter` reads the command-line keys `saveName` and `settlementName`.
  `Timberborn.CommandLine.CommandLineArguments` prepends `-` itself and `GetString` returns the
  token after the key, so the actual arguments are `-saveName <value> -settlementName <value>`.
  `AutoStartingInStandalone` is true when the Player is not the editor and `-saveName` is present.
* It then calls `GameSceneLoader.StartSaveGameInstantly(new SaveReference(saveName,
  new SettlementReference(settlementName, Path.Combine(UserDataFolder.Folder, "Saves"))))`.
* `GameSaveRepository.SaveNameToFileName` = `<SaveDirectory>\<settlementName>\<saveName>.timber`;
  `SaveExtension` is literally `".timber"` and `GetSaves` strips the extension, so `-saveName`
  must be passed **without** `.timber`.
* `ExperimentalMode` is enabled only by the `-experimental` argument; without it the game reads
  `Saves`, which is why the throwaway copy lives at `Saves\HDBFTest`.

Therefore the script runs:

```
Timberborn.exe -batchmode
  -saveName "<save name without .timber>"
  -settlementName "HDBFTest"
  -logFile "<%TEMP%>\hdbf-smoke-<timestamp>\Player.log"
```

`-nographics` is deliberately **not** passed: Unity's Player command line reference states that
"-nographics ... output logs are turned off in this mode", and the log is the whole evidence
channel. `-batchmode` alone is documented as headless ("the application doesn't display anything
or accept user input") and the game itself never checks `Application.isBatchMode` (all 660
managed assemblies were scanned: no game assembly references it), so the game still runs its
normal startup and mod scan in that mode.

### What a quiet batchmode run can and cannot prove

Can prove (if the log shows it):

* the mod folder is discovered and `HDBF.Gameplay` loads: `[HDBF/Info] start HDBF version=...`,
  `[HDBF/Info] startup: HDBF 0.1.0 ready; ...`;
* the save was actually loaded from the throwaway copy: `Opening file: ...\Saves\HDBFTest\...`;
* no localization/prefab/material conversion errors, no `InvalidDataException` /
  `InvalidOperationException` / `ArgumentException`, no `[HDBF/Error]`;
* whether the world reached a running state (`HDBF/Info gameplay.*` lines when the loaded save
  contains an HDBF farm).

Cannot prove (because batchmode displays nothing and the harness does not interact):

* building-menu placement, entrance/worker behaviour, prefab previews and the material repository
  path (the round-2 `Material BaseWood_Grey.IronTeeth not found` class of failure);
* crop growth / planting / harvest / output inventory, maintenance material deduction, power
  network connection;
* save/load of `HDBF.BoxFarm` component data (the default source save `110000` was created before
  any HDBF building existed) and save-file size/performance budgets;
* UI panel rendering, localization in context, L5 profiler thresholds, compatibility matrix.

Those remain L3+ items in `docs/VERIFICATION.md`.

## Safety model

* **Refuses to launch** if any `Timberborn` process already runs, if the deploy check fails, if
  the save source is invalid, or if `Saves\<TestSettlementName>` exists without the
  `.hdbf-smoke-owned` marker this script writes (so it can never delete a folder it did not
  create). Pass a different `-TestSettlementName` if that happens.
* **The real settlement is only read**: the chosen folder is copied to the throwaway name, the
  source folder is hashed before and after the run, and a change is a hard FAIL in the verdict.
* **Autosaves land in the copy**, which is deleted afterwards (or kept with
  `-KeepTestSettlement` for inspection).
* **Only the started PID** is stopped: `CloseMainWindow` first for a windowed run,
  `Stop-Process -Id <pid>` for batchmode (there is no window to close). Nothing is killed by
  process name.
* The historical `Player.log` is hashed before/after a batchmode run (`-logFile` redirects the
  game log elsewhere); a change is a hard FAIL.
* `-Launch` (windowed) appends to the standard `Player.log` and is not intended at night: it
  creates a window and can play audio.

## Evidence format (`schemaVersion: 2`)

`%USERPROFILE%\.hdbf\smoke\smoke-<timestamp>.json` contains: deploy manifest/assembly hashes and
staging match, workspace safety guard, `Player.log` metadata + analysis (before), the batchmode
plan (source settlement, save name/size/SHA-256, throwaway path, exact command line), the launch
record (PID, arguments, timings, stop method), the captured-log analysis (HDBF markers, tick
lines, exception signatures, blueprint/prefab conversion errors, material errors, mod-scan
warnings, opened save paths), the save-copy verification (source inventory before/after), cleanup
result and the final verdict with explicit `caveats`.

For a batchmode run the game log is also copied next to the JSON as `batchmode-<timestamp>-Player.log`.

## Evidence from this round (2026-10-24)

* Dry run (`-BatchMode`): exit `0`; deploy 266 KB / 5 DLLs / 10 blueprints / 3 CSVs, all DLL
  SHA-256 equal to `%USERPROFILE%\.hdbf\work\stage\HDBF`; plan target `Saves\110000`, save
  `2026-10-24 18h28m, Day 1-1.autosave` (443,844 B); evidence
  `smoke-20261024-224850.json`.
* Negative controls: empty `-ModDir` -> exit `1` (6 deploy problems); `-Launch` with a bad deploy
  or an unowned `Saves\HDBFTest` -> exit `4`, zero `Timberborn` processes started.
* The `-Launch -BatchMode` run itself has **not** been executed yet in this round; the Lead owns
  the first run (the current `Player.log` only proves a later no-args launch, which never loaded
  the mod).
