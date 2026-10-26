# HDBF workstreams — coordination rules (Lead-maintained)

Source of truth for the product: `C:\Users\user\Documents\deepseek-harness\default-workspace\high-density-box-farm\高密度盒子农场模组需求文档.md`
(read-only for everyone; do not copy it into this workspace).

## 1. What this folder is (and is not)

| Path | Meaning |
|---|---|
| `C:\Users\user\Documents\Timberborn\Mods\HighDensityBoxFarm` | **source workspace**. NOT a mod folder. |
| `C:\Users\user\Documents\Timberborn\Mods\HDBF` | **deployed mod** the game loads (`build.ps1 -Install` writes here). |
| `%USERPROFILE%\.hdbf\work` | build outputs + staged package (`bin/`, `obj/`, `stage/`). |
| `%USERPROFILE%\.hdbf\ref` | vanilla 1.1.2.8 reference blueprints/UI, never shipped. |

Why: Timberborn recursively converts every `*.blueprint.json` / `*.timbermesh` / sprite below a
mod folder and loads every `*.dll` below it. Mixing sources, vanilla refs or build output into the
mod folder would register the vanilla blueprint set a second time or load duplicate assemblies.
`build.ps1` enforces this (fails when `manifest.json` or stray DLLs appear in the workspace).

## 2. Build & test (the only supported commands)

```powershell
# full gate: 5 assemblies + L1 tests + data validation + staging + deploy
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Install

# single project (fast compile feedback)
dotnet build src\HDBF.Gameplay\HDBF.Gameplay.csproj -c Release -m:1

# L1 tests only
dotnet run --project tests\HDBF.Tests -c Release -m:1
```

* `-m:1` is mandatory on this machine (parallel MSBuild fails silently during restore).
* Do **not** run `build.ps1` while another build is running; tell the Lead first if you need a full
  build. Single-project builds are safe because each project has its own output folder.
* Every project may reference at most one other project (assembly graph:
  `Core <- Persistence <- Gameplay`; `Integration`/`UI` -> `Core` only).

## 3. File ownership (authoritative during this round)

| Owner | Writes | Never writes |
|---|---|---|
| `farm-loop` teammate | `src/HDBF.Gameplay/**` | data, UI, Core, tests, tools, build files |
| `tech-data` teammate | `data/Buildings/**`, `data/Research/**`, `data/TemplateCollections/**`, `tools/validate/**`, own report | src, data/Localizations, tests, build files |
| `ui-panel` teammate | `src/HDBF.UI/**`, `src/HDBF.Core/Abstractions/IBoxFarmCommand.cs` (new), `data/Localizations/**`, own report | src/HDBF.Gameplay, data/Buildings, tests, build files |
| `verify` teammate | `tests/**`, `tools/smoke/**`, `docs/VERIFICATION.md`, own report | src, data, `docs/REQ_TRACEABILITY.md`, build files |
| Lead | `build.ps1`, `Directory.Build.*`, `docs/workstreams/README.md`, `docs/REQ_TRACEABILITY.md`, gameplay command wiring, final integration | — |

Each workstream writes its evidence to `docs/workstreams/<name>.md`. Do not edit another
workstream's report.

## 4. Ground truth for game APIs

Never guess a Timberborn type/signature; the installed game is the source of truth.

```powershell
dotnet build tools\ApiIndex\ApiIndex.csproj -c Release -m:1
dotnet %USERPROFILE%\.hdbf\work\bin\ApiIndex\Release\net9.0\ApiIndex.dll `
  dump "C:\Program Files (x86)\Steam\steamapps\common\Timberborn\Timberborn_Data\Managed" `
  "$env:USERPROFILE\.hdbf\api\<topic>.txt" "TypeNamePattern" "AnotherPattern"
```

Vanilla blueprints: `%USERPROFILE%\.hdbf\ref\Blueprints\...` (search there, read-only).
Existing verified facts: `docs/API_FACTS.md`; interfaces: `docs/INTERFACES.md`.

## 5. Correctness rules that apply to every workstream

* Never invent production: crops may only come from crops that really grow on managed plots.
* Determinism: daily tick may only read the day counter / persisted record (no frame-order deps).
* No uncaught exception may reach the game loop; log with the `HDBF/` categories and throttle keys.
* No new static mutable state; caches expose `Reset()`.
* Do not weaken or bypass balance numbers from requirement chapters 5/6; values live in
  `TierCatalog` / spec data, not in branches by tier.
* Report honest status: "implemented + verified here" vs "compiles, needs in-game check" vs
  "blocked". The final docs (`VERIFICATION.md`, `REQ_TRACEABILITY.md`) must not overclaim.

## 6. Reporting

When a workstream finishes (or is blocked), message the Lead with: task id, files changed,
compile/test result, what is proven, what still needs a running game, and open questions.
Keep the full evidence in your `docs/workstreams/<name>.md`.
