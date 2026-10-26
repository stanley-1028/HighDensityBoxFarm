<#
.SYNOPSIS
    HDBF game smoke checker: deploy-file checks + Player.log analysis, optional guarded
    windowed launch, optional quiet batchmode autostart run.

.DESCRIPTION
    Three modes, selected explicitly:

      DryRun (default)
          Starts nothing. Verifies that the deployed mod folder
          (Documents\Timberborn\Mods\HDBF) contains a loadable package, that the source workspace
          is NOT accidentally a mod folder, and that the current Player.log can be parsed for HDBF
          startup markers / exception signatures. With -BatchMode it additionally validates the
          save source for the autostart path and prints the exact game command line, still without
          copying or starting anything.

      -Launch (windowed)
          Same checks, then starts the game executable, captures only the bytes Player.log gains
          from that launch, greps for "HDBF/Info" and exception signatures, and stops ONLY the PID
          it started. It never clicks, never loads/saves a game and never writes into Saves.

      -Launch -BatchMode (quiet autostart, no window)
          Uses the game's own command-line autostart (IL-verified against 1.1.2.8):
            Timberborn.MainMenuScene.AutoStarter reads "-saveName" and "-settlementName"
            (Timberborn.CommandLine.CommandLineArguments.Has/GetString prepend "-" themselves) and
            calls GameSceneLoader.StartSaveGameInstantly(new SaveReference(...)).
            GameSaveRepository.SaveNameToFileName = <saveDirectory>/<settlementName>/<saveName>.timber.
          The script therefore:
            1. validates the deployed package as in DryRun;
            2. copies the chosen settlement folder (default Saves\110000) into the throwaway
               settlement Saves\HDBFTest -- the game only ever reads that copy and any autosave it
               writes lands in the copy; the source folder is hashed before/after to prove it was
               not modified;
            3. starts Timberborn.exe -batchmode -saveName <name> -settlementName HDBFTest
               -logFile <temp>\Player.log (no visible window; -nographics is NOT passed because
               Unity documents that it turns log output off, and the log is the evidence);
            4. polls up to -TimeoutSeconds (default 180) for the HDBF startup marker, the save-open
               marker ("Opening file: ...Saves\HDBFTest\..."), HDBF/Error and blueprint-conversion
               errors, then stops ONLY the PID it started;
            5. copies the game log next to the JSON verdict, deletes the throwaway settlement
               folder (unless -KeepTestSettlement) and verifies afterwards that the source save
               folder and the historical Player.log are byte-identical.

    Safety rules baked in:
      * refuses to launch when a Timberborn process is already running (nothing is ever killed
        that this script did not start);
      * refuses to launch when the deploy check failed;
      * refuses to reuse a Saves\HDBFTest folder it does not own (ownership marker file);
      * stopping is graceful first (CloseMainWindow -> WM_CLOSE) for a windowed run, and
        Stop-Process -Id for the recorded PID in batchmode (there is no window to close);
      * no game input, no save games read directly, no file inside the user's real settlement is
        created/modified; the only files written by the script are the JSON evidence, a copy of
        the game log and (temporarily) the throwaway settlement folder.

    Exit codes:
      0  dry run / plan completed and deploy checks passed (historical log findings are reported,
         not fatal)
      1  dry run completed but deploy checks failed, or the batchmode save source is invalid
      2  cannot run (missing prerequisites: game dir, mod dir, unexpected state)
      3  launch ran and the smoke verdict is FAIL
      4  launch refused by a safety precondition (already running / deploy check failed / save
         source invalid / unowned test settlement folder)

.PARAMETER Launch
    Actually start the game. Off by default (dry run only).

.PARAMETER BatchMode
    With -Launch: use the quiet command-line autostart path described above (no window).
    Without -Launch: only validate and print the exact command line (still no copy, no launch).

.PARAMETER SourceSettlement
    Settlement folder name under Documents\Timberborn\Saves. Default: "110000" when present,
    otherwise the most recently written settlement folder. Never modified.

.PARAMETER SaveName
    Save name INSIDE that folder, without the ".timber" extension (the game appends it).
    Default: the most recently written *.timber file in the settlement folder.

.PARAMETER TestSettlementName
    Throwaway settlement folder the game is pointed at. Default "HDBFTest". Deleted afterwards.

.PARAMETER TimeoutSeconds
    Total capture budget. Default: 180 s for -BatchMode, 120 s for a windowed launch.

.PARAMETER SettleSeconds
    Extra capture time after the save-open marker (or after the first error) so trailing
    exceptions are still captured. Default 10 s.

.PARAMETER KeepTestSettlement
    Leave Saves\HDBFTest in place after a batchmode run for manual inspection. Default: delete it.

.PARAMETER KeepGameLog
    Leave the temporary game log under %TEMP% in place (a copy is always stored next to the JSON).

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\smoke\Invoke-GameSmoke.ps1
    Dry run: deploy checks + analysis of the existing Player.log.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\smoke\Invoke-GameSmoke.ps1 -BatchMode
    Dry-run the batchmode autostart plan and print the exact game command line.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\smoke\Invoke-GameSmoke.ps1 -Launch -BatchMode
    Quiet autostart run: copies Saves\110000 to Saves\HDBFTest, loads the copy headless, captures
    the log, stops only its own PID, deletes the copy and writes the JSON verdict.
#>

[CmdletBinding()]
param(
    [switch]$Launch,
    [switch]$BatchMode,
    [int]$TimeoutSeconds = 0,
    [int]$SettleSeconds = 10,
    [string]$GameDir = '',
    [string]$ModDir = '',
    [string]$LogPath = '',
    [string]$OutputDir = '',
    [string]$SourceSettlement = '',
    [string]$SaveName = '',
    [string]$TestSettlementName = 'HDBFTest',
    [switch]$KeepTestSettlement,
    [switch]$KeepGameLog
)

$ErrorActionPreference = 'Stop'
$scriptVersion = '1.1'
$startedAt = Get-Date

if ($TimeoutSeconds -le 0) { $TimeoutSeconds = if ($BatchMode) { 180 } else { 120 } }

function Resolve-DefaultPath {
    param([string]$Value, [string]$Fallback)
    if (-not [string]::IsNullOrWhiteSpace($Value)) { return $Value }
    return $Fallback
}

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$gameDir = Resolve-DefaultPath $GameDir 'C:\Program Files (x86)\Steam\steamapps\common\Timberborn'
$modDir = Resolve-DefaultPath $ModDir (Join-Path $env:USERPROFILE 'Documents\Timberborn\Mods\HDBF')
$logPath = Resolve-DefaultPath $LogPath (Join-Path $env:USERPROFILE 'AppData\LocalLow\Mechanistry\Timberborn\Player.log')
$outputDir = Resolve-DefaultPath $OutputDir (Join-Path $env:USERPROFILE '.hdbf\smoke')
$stageDir = Join-Path $env:USERPROFILE '.hdbf\work\stage\HDBF'
$userDataDir = Join-Path $env:USERPROFILE 'Documents\Timberborn'
$savesRoot = Join-Path $userDataDir 'Saves'
$exePath = Join-Path $gameDir 'Timberborn.exe'
$managedDir = Join-Path $gameDir 'Timberborn_Data\Managed'
$ownedMarkerName = '.hdbf-smoke-owned'

$expectedAssemblies = @(
    'HDBF.Core.dll',
    'HDBF.Persistence.dll',
    'HDBF.Gameplay.dll',
    'HDBF.Integration.dll',
    'HDBF.UI.dll'
)

$exceptionLinePatterns = @(
    'First uncaught exception',
    'An unhandled exception',
    'Unhandled exception',
    'NullReferenceException',
    'InvalidOperationException',
    'InvalidDataException',
    'ArgumentException',
    'MissingMethodException',
    'MissingFieldException',
    'TypeLoadException',
    'DllNotFoundException',
    'EntryPointNotFoundException',
    'BadImageFormatException',
    'NotSupportedException',
    'IndexOutOfRangeException',
    'FormatException',
    'Crash!!!',
    'Creating an exception game save',
    'Creating an error report'
)

# Blueprint / prefab conversion failures seen in earlier in-game rounds (VERIFICATION.md 8-12).
$blueprintErrorPatterns = @(
    'Processing prefab .* failed',
    'not found in repository',
    'Localization file contains errors',
    'Unnecessary comma',
    'PrefabOptimization',
    'Could not load blueprint',
    'Failed to (load|convert|create).*blueprint',
    'Conversion failed',
    'Timbermesh.*(fail|error)',
    'Material .* not found'
)

$materialErrorPatterns = @(
    'Material .* not found',
    'not found in repository'
)

# Mod-folder scanning failures: a misdeployed or unreadable mod never reaches StartMod.
$modLoadWarningPatterns = @(
    'No manifest file found',
    'Failed to load mod',
    'Could not load mod',
    'Failed to load manifest',
    'Mod assembly',
    'Failed to load mod directory'
)

$saveOpenPattern = 'Opening file:\s*(.+)$'
$worldLoadPatterns = @('Opening file:', 'Load time:', 'Loading game', 'Loading save', 'GameSceneLoader')

$infoPattern = '\[HDBF/Info\]'
$compatPattern = 'HDBF/Compat:'
$warnPattern = '\[HDBF/Warn\]'
$errorPattern = '\[HDBF/Error\]'
$startupPattern = '\[HDBF/Info\]\s+(start\b|startup:)'
$tickPattern = '\[HDBF/Info\]\s+(gameplay\.(tick|entity|scan|save)|.*\btick\b)'

# ----------------------------------------------------------------------------- generic helpers

function Get-Sha256 {
    param([string]$Path)
    try {
        return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    } catch {
        return $null
    }
}

function Get-Sha256FromText {
    param([string]$Text)
    if ($null -eq $Text) { $Text = '' }
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
        $hash = $sha.ComputeHash($bytes)
        return (($hash | ForEach-Object { $_.ToString('x2') }) -join '')
    } finally {
        $sha.Dispose()
    }
}

function Quote-Arg {
    param([string]$Value)
    if ($null -eq $Value) { return '""' }
    if ($Value -match '[\s"]') {
        return '"' + ($Value -replace '"', '\"') + '"'
    }
    return $Value
}

function Read-LogTail {
    param(
        [string]$Path,
        [int]$MaxBytes = 524288
    )
    $result = [ordered]@{
        exists = $false
        length = 0
        lastWriteTime = $null
        tailBytes = 0
        truncatedFromStart = $false
        lines = @()
    }

    if (-not (Test-Path -LiteralPath $Path)) { return $result }
    $result.exists = $true

    $file = Get-Item -LiteralPath $Path
    $result.length = $file.Length
    $result.lastWriteTime = $file.LastWriteTime.ToString('o')

    $stream = $null
    try {
        $stream = New-Object System.IO.FileStream($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        $start = if ($file.Length -gt $MaxBytes) { $file.Length - $MaxBytes } else { 0 }
        $result.truncatedFromStart = $start -gt 0
        $stream.Seek($start, [System.IO.SeekOrigin]::Begin) | Out-Null
        $count = [int]($file.Length - $start)
        $buffer = New-Object byte[] $count
        $read = 0
        while ($read -lt $count) {
            $chunk = $stream.Read($buffer, $read, $count - $read)
            if ($chunk -le 0) { break }
            $read += $chunk
        }
        $result.tailBytes = $read
        $text = [System.Text.Encoding]::UTF8.GetString($buffer, 0, $read)
        $result.lines = @($text -split "`r?`n")
    } finally {
        if ($null -ne $stream) { $stream.Dispose() }
    }

    return $result
}

function Read-NewLogBytes {
    param(
        [string]$Path,
        [long]$Offset
    )

    $result = [ordered]@{ text = ''; newOffset = $Offset; rotated = $false; length = 0; exists = $false }
    if (-not (Test-Path -LiteralPath $Path)) { return $result }

    $file = Get-Item -LiteralPath $Path
    $result.exists = $true
    $result.length = $file.Length

    if ($file.Length -lt $Offset) {
        $result.rotated = $true
        $Offset = 0
    }

    if ($file.Length -le $Offset) {
        $result.newOffset = $Offset
        return $result
    }

    $stream = $null
    try {
        $stream = New-Object System.IO.FileStream($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        $stream.Seek($Offset, [System.IO.SeekOrigin]::Begin) | Out-Null
        $count = [int]($file.Length - $Offset)
        $buffer = New-Object byte[] $count
        $read = 0
        while ($read -lt $count) {
            $chunk = $stream.Read($buffer, $read, $count - $read)
            if ($chunk -le 0) { break }
            $read += $chunk
        }
        $result.text = [System.Text.Encoding]::UTF8.GetString($buffer, 0, $read)
        $result.newOffset = $Offset + $read
    } finally {
        if ($null -ne $stream) { $stream.Dispose() }
    }

    return $result
}

function Test-LineMatchesAny {
    param([string]$Line, [string[]]$Patterns)
    foreach ($pattern in $Patterns) {
        if ([regex]::IsMatch($Line, $pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
            return $pattern
        }
    }
    return $null
}

function Analyze-LogLines {
    param(
        [string[]]$Lines,
        [int]$MaxExcerpts = 6
    )

    $infoCount = 0
    $compatCount = 0
    $warnCount = 0
    $errorCount = 0
    $tickCount = 0
    $startupMarkers = New-Object System.Collections.Generic.List[string]
    $tickSamples = New-Object System.Collections.Generic.List[string]
    $infoSamples = New-Object System.Collections.Generic.List[string]
    $noiseCount = 0
    $noiseSamples = New-Object System.Collections.Generic.List[string]
    $exceptionHits = [ordered]@{}
    $exceptionSamples = New-Object System.Collections.Generic.List[object]
    $blueprintHits = [ordered]@{}
    $blueprintSamples = New-Object System.Collections.Generic.List[object]
    $materialHits = [ordered]@{}
    $materialSamples = New-Object System.Collections.Generic.List[object]
    $worldLoadSamples = New-Object System.Collections.Generic.List[string]
    $modLoadHits = [ordered]@{}
    $modLoadSamples = New-Object System.Collections.Generic.List[object]
    $openedSavePaths = New-Object System.Collections.Generic.List[string]
    $lineNumber = 0

    foreach ($line in $Lines) {
        $lineNumber++
        if ($null -eq $line -or $line.Length -eq 0) { continue }
        $trimmed = $line.Trim()

        if ($line -match $infoPattern) {
            $infoCount++
            if ($infoSamples.Count -lt $MaxExcerpts) { $infoSamples.Add($trimmed) }
            if ([regex]::IsMatch($line, $tickPattern)) {
                $tickCount++
                if ($tickSamples.Count -lt $MaxExcerpts) { $tickSamples.Add($trimmed) }
            }
        }

        if ($line -match $compatPattern) { $compatCount++ }
        if ($line -match $warnPattern) { $warnCount++ }
        if ($line -match $errorPattern) { $errorCount++ }

        if ($line -match $startupPattern) {
            if ($startupMarkers.Count -lt 5) { $startupMarkers.Add($trimmed) }
        }

        if ($line -like '*config.invalid*') {
            $noiseCount++
            if ($noiseSamples.Count -lt $MaxExcerpts) { $noiseSamples.Add($trimmed) }
        }

        $matched = $null
        foreach ($pattern in $exceptionLinePatterns) {
            if ($line.IndexOf($pattern, [System.StringComparison]::Ordinal) -ge 0) { $matched = $pattern; break }
        }
        if ($null -ne $matched) {
            if (-not $exceptionHits.Contains($matched)) { $exceptionHits[$matched] = 0 }
            $exceptionHits[$matched] = $exceptionHits[$matched] + 1
            if ($exceptionSamples.Count -lt ($MaxExcerpts * 2)) {
                $exceptionSamples.Add([ordered]@{ pattern = $matched; line = $lineNumber; excerpt = $trimmed })
            }
        }

        $blueprintMatch = Test-LineMatchesAny -Line $line -Patterns $blueprintErrorPatterns
        if ($null -ne $blueprintMatch) {
            if (-not $blueprintHits.Contains($blueprintMatch)) { $blueprintHits[$blueprintMatch] = 0 }
            $blueprintHits[$blueprintMatch] = $blueprintHits[$blueprintMatch] + 1
            if ($blueprintSamples.Count -lt ($MaxExcerpts * 2)) {
                $blueprintSamples.Add([ordered]@{ pattern = $blueprintMatch; line = $lineNumber; excerpt = $trimmed })
            }
        }

        $materialMatch = Test-LineMatchesAny -Line $line -Patterns $materialErrorPatterns
        if ($null -ne $materialMatch) {
            if (-not $materialHits.Contains($materialMatch)) { $materialHits[$materialMatch] = 0 }
            $materialHits[$materialMatch] = $materialHits[$materialMatch] + 1
            if ($materialSamples.Count -lt ($MaxExcerpts * 2)) {
                $materialSamples.Add([ordered]@{ pattern = $materialMatch; line = $lineNumber; excerpt = $trimmed })
            }
        }

        $modLoadMatch = Test-LineMatchesAny -Line $line -Patterns $modLoadWarningPatterns
        if ($null -ne $modLoadMatch) {
            if (-not $modLoadHits.Contains($modLoadMatch)) { $modLoadHits[$modLoadMatch] = 0 }
            $modLoadHits[$modLoadMatch] = $modLoadHits[$modLoadMatch] + 1
            if ($modLoadSamples.Count -lt ($MaxExcerpts * 2)) {
                $modLoadSamples.Add([ordered]@{ pattern = $modLoadMatch; line = $lineNumber; excerpt = $trimmed })
            }
        }

        if ($null -ne (Test-LineMatchesAny -Line $line -Patterns $worldLoadPatterns)) {
            if ($worldLoadSamples.Count -lt ($MaxExcerpts * 2)) { $worldLoadSamples.Add($trimmed) }
        }

        $saveMatch = [regex]::Match($line, $saveOpenPattern)
        if ($saveMatch.Success) {
            if ($openedSavePaths.Count -lt 20) { $openedSavePaths.Add($saveMatch.Groups[1].Value.Trim()) }
        }
    }

    $signatureList = New-Object System.Collections.Generic.List[object]
    foreach ($key in $exceptionHits.Keys) { $signatureList.Add([ordered]@{ pattern = $key; count = $exceptionHits[$key] }) }
    $blueprintList = New-Object System.Collections.Generic.List[object]
    foreach ($key in $blueprintHits.Keys) { $blueprintList.Add([ordered]@{ pattern = $key; count = $blueprintHits[$key] }) }
    $materialList = New-Object System.Collections.Generic.List[object]
    foreach ($key in $materialHits.Keys) { $materialList.Add([ordered]@{ pattern = $key; count = $materialHits[$key] }) }

    return [ordered]@{
        lineCount = $Lines.Count
        hdbfInfoCount = $infoCount
        hdbfInfoSamples = $infoSamples.ToArray()
        hdbfTickCount = $tickCount
        hdbfTickSamples = $tickSamples.ToArray()
        hdbfCompatCount = $compatCount
        hdbfWarnCount = $warnCount
        hdbfErrorCount = $errorCount
        hasStartupMarker = ($startupMarkers.Count -gt 0)
        startupMarkers = $startupMarkers.ToArray()
        knownNoiseCount = $noiseCount
        knownNoiseExcerpts = $noiseSamples.ToArray()
        exceptionSignatureCount = $signatureList.Count
        exceptionSignatures = $signatureList.ToArray()
        exceptionSamples = $exceptionSamples.ToArray()
        blueprintConversionErrorCount = $blueprintList.Count
        blueprintConversionErrors = $blueprintList.ToArray()
        blueprintConversionSamples = $blueprintSamples.ToArray()
        materialErrorCount = $materialList.Count
        materialErrors = $materialList.ToArray()
        materialErrorSamples = $materialSamples.ToArray()
        modLoadWarningCount = $modLoadSamples.Count
        modLoadWarnings = @($modLoadHits.Keys | ForEach-Object { [ordered]@{ pattern = $_; count = $modLoadHits[$_] } })
        modLoadWarningSamples = $modLoadSamples.ToArray()
        worldLoadSamples = $worldLoadSamples.ToArray()
        openedSavePaths = $openedSavePaths.ToArray()
    }
}

function Test-DeployPackage {
    param(
        [string]$ModPath,
        [string]$StagePath,
        [string[]]$Assemblies
    )

    $problems = New-Object System.Collections.Generic.List[string]
    $assembliesOut = New-Object System.Collections.Generic.List[object]
    $manifestOut = $null
    $blueprints = 0
    $localizations = 0
    $configPresent = $false
    $packageBytes = 0
    $matchesStage = $null

    if (-not (Test-Path -LiteralPath $ModPath)) {
        $problems.Add("mod folder missing: $ModPath")
        return [ordered]@{ ok = $false; problems = $problems.ToArray(); assemblies = @(); manifest = $null }
    }

    $manifestPath = Join-Path $ModPath 'manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        $problems.Add('manifest.json missing from the mod folder')
    } else {
        try {
            $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
            $manifestOut = [ordered]@{
                name = $manifest.Name
                version = $manifest.Version
                id = $manifest.Id
                minimumGameVersion = $manifest.MinimumGameVersion
                requiredMods = @($manifest.RequiredMods)
                optionalMods = @($manifest.OptionalMods)
                sha256 = Get-Sha256 $manifestPath
            }
            foreach ($field in @('Name', 'Version', 'Id', 'MinimumGameVersion')) {
                if (-not $manifest.$field) { $problems.Add("manifest.json is missing required field: $field") }
            }
        } catch {
            $problems.Add("manifest.json is not valid JSON: $($_.Exception.Message)")
        }
    }

    foreach ($assembly in $Assemblies) {
        $file = Join-Path $ModPath $assembly
        if (-not (Test-Path -LiteralPath $file)) {
            $problems.Add("missing assembly: $assembly")
            continue
        }
        $item = Get-Item -LiteralPath $file
        $assembliesOut.Add([ordered]@{
            name = $assembly
            bytes = $item.Length
            sha256 = Get-Sha256 $file
        })
    }

    $stageMatches = $true
    if (-not (Test-Path -LiteralPath $StagePath)) {
        $matchesStage = $null
    } else {
        foreach ($entry in $assembliesOut) {
            $stageFile = Join-Path $StagePath $entry.name
            if (-not (Test-Path -LiteralPath $stageFile)) {
                $stageMatches = $false
                continue
            }
            if ((Get-Sha256 $stageFile) -ne $entry.sha256) { $stageMatches = $false }
        }
        $matchesStage = $stageMatches
        if (-not $stageMatches) {
            $problems.Add('deployed assemblies do not match %USERPROFILE%\.hdbf\work\stage\HDBF (stale deploy)')
        }
    }

    $blueprints = @(Get-ChildItem -LiteralPath $ModPath -Recurse -File -Filter '*.blueprint.json' -ErrorAction SilentlyContinue).Count
    if ($blueprints -lt 5) { $problems.Add("expected at least 5 blueprint files, found $blueprints") }

    $localizations = @(Get-ChildItem -LiteralPath $ModPath -Recurse -File -Filter '*.csv' -ErrorAction SilentlyContinue).Count
    if ($localizations -lt 2) { $problems.Add("expected at least 2 localization CSVs, found $localizations") }

    $configPath = Join-Path $ModPath 'HDBF_Config.json'
    $configPresent = Test-Path -LiteralPath $configPath
    if (-not $configPresent) { $problems.Add('HDBF_Config.json missing') }

    $packageBytes = (@(Get-ChildItem -LiteralPath $ModPath -Recurse -File -ErrorAction SilentlyContinue) | Measure-Object -Sum Length).Sum
    if ($null -eq $packageBytes) { $packageBytes = 0 }

    return [ordered]@{
        ok = ($problems.Count -eq 0)
        path = $ModPath
        problems = $problems.ToArray()
        manifest = $manifestOut
        assemblies = $assembliesOut.ToArray()
        blueprints = $blueprints
        localizations = $localizations
        configPresent = $configPresent
        packageBytes = $packageBytes
        matchesStage = $matchesStage
    }
}

function Test-WorkspaceGuard {
    param([string]$Root)

    $stray = New-Object System.Collections.Generic.List[object]
    $hasManifest = $false
    $hasStrayDll = $false

    if (Test-Path -LiteralPath (Join-Path $Root 'manifest.json')) { $hasManifest = $true }

    if (Test-Path -LiteralPath $Root) {
        $dlls = @(Get-ChildItem -LiteralPath $Root -Recurse -File -Filter '*.dll' -ErrorAction SilentlyContinue)
        foreach ($dll in $dlls) {
            $hasStrayDll = $true
            if ($stray.Count -lt 10) { $stray.Add($dll.FullName) }
        }
    }

    $ok = (-not $hasManifest) -and (-not $hasStrayDll)
    $note = if ($ok) { 'source workspace is not a mod folder (no manifest.json, no DLLs)' }
            else { 'workspace would be loaded by the game as a mod; build.ps1 -Clean and remove stray DLLs' }
    return [ordered]@{
        ok = $ok
        root = $Root
        hasManifest = $hasManifest
        hasStrayDll = $hasStrayDll
        strayDlls = $stray.ToArray()
        note = $note
    }
}

function Get-SaveInventory {
    param([string]$Folder)

    $files = New-Object System.Collections.Generic.List[object]
    $totalBytes = 0
    $hashLines = New-Object System.Collections.Generic.List[string]

    if (Test-Path -LiteralPath $Folder) {
        foreach ($file in @(Get-ChildItem -LiteralPath $Folder -Recurse -File -ErrorAction SilentlyContinue | Sort-Object FullName)) {
            $relative = $file.FullName.Substring($Folder.Length).TrimStart('\', '/')
            $hash = Get-Sha256 $file.FullName
            $files.Add([ordered]@{ relativePath = $relative; bytes = $file.Length; sha256 = $hash })
            $totalBytes += $file.Length
            $hashLines.Add($relative + ':' + $hash)
        }
    }

    return [ordered]@{
        folder = $Folder
        exists = (Test-Path -LiteralPath $Folder)
        fileCount = $files.Count
        totalBytes = $totalBytes
        combinedHash = (Get-Sha256FromText ($hashLines -join "`n"))
        files = $files.ToArray()
    }
}

function Resolve-SaveSource {
    param(
        [string]$SavesRoot,
        [string]$RequestedSettlement,
        [string]$RequestedSave
    )

    $problems = New-Object System.Collections.Generic.List[string]
    $settlement = $RequestedSettlement
    $folder = $null
    $saveName = $null
    $saveFile = $null
    $saveBytes = 0
    $saveSha = $null
    $availableSaves = @()

    if (-not (Test-Path -LiteralPath $SavesRoot)) {
        $problems.Add("saves root not found: $SavesRoot")
        return [ordered]@{
            ok = $false; problems = $problems.ToArray(); savesRoot = $SavesRoot;
            settlement = $settlement; folder = $null; saveName = $null; saveFile = $null;
            saveBytes = 0; saveSha256 = $null; availableSaves = @()
        }
    }

    if ([string]::IsNullOrWhiteSpace($settlement)) {
        $preferred = Join-Path $SavesRoot '110000'
        if (Test-Path -LiteralPath $preferred) {
            $settlement = '110000'
        } else {
            $candidates = @(Get-ChildItem -LiteralPath $SavesRoot -Directory -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)
            if ($candidates.Count -gt 0) { $settlement = $candidates[0].Name }
        }
    }

    if ([string]::IsNullOrWhiteSpace($settlement)) {
        $problems.Add("no settlement folder available under $SavesRoot")
        return [ordered]@{
            ok = $false; problems = $problems.ToArray(); savesRoot = $SavesRoot;
            settlement = $null; folder = $null; saveName = $null; saveFile = $null;
            saveBytes = 0; saveSha256 = $null; availableSaves = @()
        }
    }

    if ($settlement -notmatch '^[A-Za-z0-9][A-Za-z0-9 ._()\[\]-]*$' -or $settlement.Contains('..')) {
        $problems.Add("invalid settlement folder name: '$settlement'")
    } else {
        $folder = Join-Path $SavesRoot $settlement
        $resolved = [System.IO.Path]::GetFullPath($folder)
        $rootResolved = [System.IO.Path]::GetFullPath($SavesRoot)
        if (-not $resolved.StartsWith($rootResolved, [System.StringComparison]::OrdinalIgnoreCase)) {
            $problems.Add("settlement folder escapes the Saves root: $resolved")
            $folder = $null
        }
    }

    if ($null -ne $folder -and (Test-Path -LiteralPath $folder)) {
        $timberFiles = @(Get-ChildItem -LiteralPath $folder -File -Filter '*.timber' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)
        $availableSaves = @($timberFiles | ForEach-Object { [System.IO.Path]::GetFileNameWithoutExtension($_.Name) })
        if ($timberFiles.Count -eq 0) {
            $problems.Add("settlement '$settlement' contains no *.timber save files")
        } elseif ([string]::IsNullOrWhiteSpace($RequestedSave)) {
            $saveName = [System.IO.Path]::GetFileNameWithoutExtension($timberFiles[0].Name)
            $saveFile = $timberFiles[0].FullName
        } else {
            $requested = $RequestedSave.Trim()
            if ($requested.EndsWith('.timber', [System.StringComparison]::OrdinalIgnoreCase)) {
                $requested = $requested.Substring(0, $requested.Length - '.timber'.Length)
            }
            $candidate = Join-Path $folder ($requested + '.timber')
            if (Test-Path -LiteralPath $candidate) {
                $saveName = $requested
                $saveFile = $candidate
            } else {
                $problems.Add("save '$requested.timber' not found in settlement '$settlement'")
            }
        }
    } elseif ($null -ne $folder) {
        $problems.Add("settlement folder not found: $folder")
    }

    if ($null -ne $saveFile) {
        $saveBytes = (Get-Item -LiteralPath $saveFile).Length
        $saveSha = Get-Sha256 $saveFile
    }

    return [ordered]@{
        ok = ($problems.Count -eq 0)
        problems = $problems.ToArray()
        savesRoot = $SavesRoot
        settlement = $settlement
        folder = $folder
        saveName = $saveName
        saveFile = $saveFile
        saveBytes = $saveBytes
        saveSha256 = $saveSha
        availableSaves = $availableSaves
    }
}

function Remove-OwnedTestSettlement {
    param([string]$Folder, [string]$MarkerName)
    if (-not (Test-Path -LiteralPath $Folder)) { return [ordered]@{ deleted = $false; reason = 'not-present' } }
    $marker = Join-Path $Folder $MarkerName
    if (-not (Test-Path -LiteralPath $marker)) {
        return [ordered]@{ deleted = $false; reason = 'refused: folder exists without the HDBF smoke ownership marker' }
    }
    Remove-Item -LiteralPath $Folder -Recurse -Force
    return [ordered]@{ deleted = $true; reason = 'deleted' }
}

function New-TestSettlementCopy {
    param(
        [string]$SourceFolder,
        [string]$DestinationFolder,
        [string]$MarkerName
    )

    $problems = New-Object System.Collections.Generic.List[string]
    $marker = Join-Path $DestinationFolder $MarkerName

    if (Test-Path -LiteralPath $DestinationFolder) {
        $markerExists = Test-Path -LiteralPath $marker
        if (-not $markerExists) {
            $problems.Add("refusing to reuse '$DestinationFolder': it exists and was not created by this script (no $MarkerName marker)")
            return [ordered]@{ ok = $false; problems = $problems.ToArray(); destination = $DestinationFolder }
        }
        Remove-Item -LiteralPath $DestinationFolder -Recurse -Force
    }

    try {
        New-Item -ItemType Directory -Force -Path $DestinationFolder | Out-Null
        Copy-Item -Path (Join-Path $SourceFolder '*') -Destination $DestinationFolder -Recurse -Force
        [System.IO.File]::WriteAllText($marker,
            ("HDBF smoke throwaway settlement copy`ncreated: " + (Get-Date).ToString('o') + "`nsource: " + $SourceFolder + "`n"),
            (New-Object System.Text.UTF8Encoding($false)))
    } catch {
        $problems.Add('copy failed: ' + $_.Exception.Message)
        return [ordered]@{ ok = $false; problems = $problems.ToArray(); destination = $DestinationFolder }
    }

    $sourceInventory = Get-SaveInventory -Folder $SourceFolder
    $destinationInventory = Get-SaveInventory -Folder $DestinationFolder
    $copiedFiles = @($destinationInventory.files | Where-Object { $_.relativePath -ne $MarkerName })
    if ($sourceInventory.fileCount -ne $copiedFiles.Count) {
        $problems.Add("copy file count mismatch: source $($sourceInventory.fileCount), copy $($copiedFiles.Count)")
    }

    $sourceByName = @{}
    foreach ($file in $sourceInventory.files) { $sourceByName[$file.relativePath] = $file.sha256 }
    foreach ($file in $copiedFiles) {
        if (-not $sourceByName.ContainsKey($file.relativePath)) {
            $problems.Add("copy contains unexpected file: " + $file.relativePath)
        } elseif ($sourceByName[$file.relativePath] -ne $file.sha256) {
            $problems.Add("copy hash mismatch: " + $file.relativePath)
        }
    }

    return [ordered]@{
        ok = ($problems.Count -eq 0)
        problems = $problems.ToArray()
        source = $SourceFolder
        destination = $DestinationFolder
        sourceFileCount = $sourceInventory.fileCount
        copiedFileCount = $copiedFiles.Count
        sourceInventory = $sourceInventory
        destinationInventory = $destinationInventory
    }
}

function Stop-StartedGame {
    param(
        [System.Diagnostics.Process]$Process,
        [int]$GraceSeconds = 10,
        [switch]$NoWindow
    )

    $out = [ordered]@{ method = 'none'; exitedBefore = $false; forced = $false; waitedSeconds = 0 }
    if ($null -eq $Process) { return $out }
    if ($Process.HasExited) {
        $out.exitedBefore = $true
        $out.method = 'already-exited'
        return $out
    }

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    if (-not $NoWindow) {
        try {
            if ($Process.MainWindowHandle -ne [System.IntPtr]::Zero) {
                $Process.CloseMainWindow() | Out-Null
                $out.method = 'close-main-window'
            }
        } catch {
            $out.method = 'close-main-window-failed'
        }
    }

    if ($out.method -eq 'none' -or $out.method -eq 'close-main-window-failed') {
        $out.method = 'stop-process'
    }

    if (-not $Process.WaitForExit($GraceSeconds * 1000)) {
        try {
            Stop-Process -Id $Process.Id -Force -ErrorAction Stop
            $out.forced = $true
            $out.method = 'stop-process-forced'
            $Process.WaitForExit(10000) | Out-Null
        } catch {
            $out.method = 'stop-process-failed'
        }
    }

    $sw.Stop()
    $out.waitedSeconds = [math]::Round($sw.Elapsed.TotalSeconds, 1)
    return $out
}

# ----------------------------------------------------------------------------- preflight

$preflightProblems = New-Object System.Collections.Generic.List[string]
if (-not (Test-Path -LiteralPath $gameDir)) { $preflightProblems.Add("game directory not found: $gameDir") }
if (-not (Test-Path -LiteralPath $exePath)) { $preflightProblems.Add("game executable not found: $exePath") }
if (-not (Test-Path -LiteralPath $managedDir)) { $preflightProblems.Add("managed assemblies directory not found: $managedDir") }
if (-not (Test-Path -LiteralPath $outputDir)) { New-Item -ItemType Directory -Force -Path $outputDir | Out-Null }

$deploy = Test-DeployPackage -ModPath $modDir -StagePath $stageDir -Assemblies $expectedAssemblies
$workspaceGuard = Test-WorkspaceGuard -Root $repoRoot

$logBefore = Read-LogTail -Path $logPath
$analysisBefore = Analyze-LogLines -Lines @($logBefore.lines)
$historicalLogHashBefore = Get-Sha256 $logPath
$logAgeSeconds = $null
if ($logBefore.exists -and $null -ne $logBefore.lastWriteTime) {
    $lastWrite = [datetime]::Parse($logBefore.lastWriteTime, [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::RoundtripKind)
    $logAgeSeconds = [math]::Round(((Get-Date).ToUniversalTime() - $lastWrite.ToUniversalTime()).TotalSeconds, 1)
}

# ----------------------------------------------------------------------------- batchmode plan

$batchModePlan = $null
if ($BatchMode) {
    $saveSource = Resolve-SaveSource -SavesRoot $savesRoot -RequestedSettlement $SourceSettlement -RequestedSave $SaveName
    $testSettlement = $TestSettlementName.Trim()
    $planProblems = New-Object System.Collections.Generic.List[string]
    foreach ($problem in $saveSource.problems) { $planProblems.Add($problem) }
    if ($testSettlement -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$' -or $testSettlement.Contains('..')) {
        $planProblems.Add("invalid -TestSettlementName: '$testSettlement' (letters, digits, dot, underscore and dash only)")
    } elseif ($null -ne $saveSource.settlement -and [string]::Equals($testSettlement, $saveSource.settlement, [System.StringComparison]::OrdinalIgnoreCase)) {
        $planProblems.Add('-TestSettlementName must differ from -SourceSettlement')
    }
    if ($testSettlement -in @('04', '06', '07', '110000')) {
        $planProblems.Add("-TestSettlementName '$testSettlement' collides with a real settlement folder name; pick another name")
    }

    $destinationFolder = Join-Path $savesRoot $testSettlement
    $tempLogDir = Join-Path $env:TEMP ('hdbf-smoke-' + (Get-Date).ToString('yyyyMMdd-HHmmss'))
    $gameLogPath = Join-Path $tempLogDir 'Player.log'
    $commandArguments = @()
    if ($null -ne $saveSource.saveName) {
        $commandArguments = @(
            '-batchmode',
            '-saveName', (Quote-Arg $saveSource.saveName),
            '-settlementName', (Quote-Arg $testSettlement),
            '-logFile', (Quote-Arg $gameLogPath)
        )
    }

    $sourcePlanInventory = [ordered]@{ fileCount = 0; totalBytes = 0; combinedHash = $null }
    if ($null -ne $saveSource.folder -and (Test-Path -LiteralPath $saveSource.folder)) {
        $sourcePlanInventory = Get-SaveInventory -Folder $saveSource.folder
    }

    $batchModePlan = [ordered]@{
        ok = ($planProblems.Count -eq 0)
        problems = $planProblems.ToArray()
        savesRoot = $savesRoot
        sourceSettlement = $saveSource.settlement
        sourceFolder = $saveSource.folder
        sourceFileCount = $sourcePlanInventory.fileCount
        sourceTotalBytes = $sourcePlanInventory.totalBytes
        sourceCombinedHash = $sourcePlanInventory.combinedHash
        saveName = $saveSource.saveName
        saveFile = $saveSource.saveFile
        saveBytes = $saveSource.saveBytes
        saveSha256 = $saveSource.saveSha256
        availableSaves = $saveSource.availableSaves
        testSettlementName = $testSettlement
        destinationFolder = $destinationFolder
        destinationExists = (Test-Path -LiteralPath $destinationFolder)
        destinationOwned = (Test-Path -LiteralPath (Join-Path $destinationFolder $ownedMarkerName))
        willCopy = $true
        willDeleteTestSettlementAfterRun = (-not $KeepTestSettlement)
        gameLogPath = $gameLogPath
        exactCommandLine = ('"' + $exePath + '" ' + ($commandArguments -join ' '))
        commandArguments = $commandArguments
        gameExeVersion = $null
    }

    try {
        $batchModePlan.gameExeVersion = (Get-Item -LiteralPath $exePath).VersionInfo.FileVersion
    } catch { }
}

# ----------------------------------------------------------------------------- launch

$launchInfo = $null
$captureAnalysis = $null
$batchModeRun = $null
$verdictStatus = 'Unknown'
$verdictReasons = New-Object System.Collections.Generic.List[string]
$verdictCaveats = New-Object System.Collections.Generic.List[string]
$sourceInventoryBefore = $null
$sourceInventoryAfter = $null
$historicalLogHashAfter = $null

if ($Launch) {
    $existing = @(Get-Process -Name 'Timberborn' -ErrorAction SilentlyContinue)
    $safetyBlocked = $false
    if ($existing.Count -gt 0) {
        $safetyBlocked = $true
        $verdictReasons.Add("refused to launch: a Timberborn process is already running (PIDs: " + (($existing | ForEach-Object { $_.Id }) -join ', ') + ')')
    }
    if (-not $deploy.ok) {
        $safetyBlocked = $true
        $verdictReasons.Add('refused to launch: deployed mod package failed the pre-launch check')
    }
    if ($preflightProblems.Count -gt 0) {
        $safetyBlocked = $true
        foreach ($problem in $preflightProblems) { $verdictReasons.Add($problem) }
    }
    if ($BatchMode -and ($null -eq $batchModePlan -or -not $batchModePlan.ok)) {
        $safetyBlocked = $true
        $verdictReasons.Add('refused to launch: the batchmode autostart plan is invalid')
        if ($null -ne $batchModePlan) {
            foreach ($problem in $batchModePlan.problems) { $verdictReasons.Add($problem) }
        }
    }
    if ($BatchMode -and $null -ne $batchModePlan -and $batchModePlan.ok -and $batchModePlan.destinationExists -and -not $batchModePlan.destinationOwned) {
        $safetyBlocked = $true
        $verdictReasons.Add('refused to launch: throwaway folder exists but was not created by this script: ' + $batchModePlan.destinationFolder + '; delete it first or pass a different -TestSettlementName')
    }

    if ($safetyBlocked) {
        $verdictStatus = 'Refused'
        $launchInfo = [ordered]@{
            attempted = $false
            batchMode = [bool]$BatchMode
            skippedReason = ($verdictReasons -join '; ')
        }
    } elseif ($BatchMode) {
        # -------------------------------------------------- quiet batchmode autostart run
        $sourceInventoryBefore = Get-SaveInventory -Folder $batchModePlan.sourceFolder
        $copyResult = New-TestSettlementCopy -SourceFolder $batchModePlan.sourceFolder `
            -DestinationFolder $batchModePlan.destinationFolder -MarkerName $ownedMarkerName
        if (-not $copyResult.ok) {
            $verdictStatus = 'Fail'
            foreach ($problem in $copyResult.problems) { $verdictReasons.Add($problem) }
            $launchInfo = [ordered]@{ attempted = $false; batchMode = $true; skippedReason = 'save copy failed' }
        } else {
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $batchModePlan.gameLogPath) | Out-Null
            $arguments = $batchModePlan.commandArguments
            $launchInfo = [ordered]@{
                attempted = $true
                batchMode = $true
                startedAt = (Get-Date).ToString('o')
                exePath = $exePath
                workingDirectory = $gameDir
                arguments = $arguments
                commandLine = ('"' + $exePath + '" ' + ($arguments -join ' '))
                gameLogPath = $batchModePlan.gameLogPath
                processId = $null
                waitedSeconds = 0
                hdbfStartupSeen = $false
                hdbfStartupAtSeconds = $null
                saveOpenSeen = $false
                saveOpenAtSeconds = $null
                errorSeen = $false
                firstErrorAtSeconds = $null
                processExited = $false
                exitCode = $null
                logExists = $false
                logBytes = 0
                openedSaveIntoTestSettlement = $false
                stop = $null
            }

            $process = $null
            $capturedLines = New-Object System.Collections.Generic.List[string]
            $offset = 0
            $startupSeen = $false
            $startupAt = $null
            $saveOpenSeen = $false
            $saveOpenAt = $null
            $errorSeen = $false
            $firstErrorAt = $null

            try {
                $process = Start-Process -FilePath $exePath -WorkingDirectory $gameDir -ArgumentList ($arguments -join ' ') -PassThru
                $launchInfo.processId = $process.Id
                $sw = [System.Diagnostics.Stopwatch]::StartNew()

                while ($true) {
                    Start-Sleep -Milliseconds 1000
                    $elapsed = $sw.Elapsed.TotalSeconds

                    if (Test-Path -LiteralPath $batchModePlan.gameLogPath) {
                        $chunk = Read-NewLogBytes -Path $batchModePlan.gameLogPath -Offset $offset
                        if ($chunk.text.Length -gt 0) {
                            $offset = $chunk.newOffset
                            foreach ($line in ($chunk.text -split "`r?`n")) {
                                if ($line.Length -eq 0) { continue }
                                $capturedLines.Add($line)
                                if (-not $startupSeen -and $line -match $startupPattern) {
                                    $startupSeen = $true
                                    $startupAt = [math]::Round($elapsed, 1)
                                }
                                if (-not $saveOpenSeen) {
                                    $saveMatch = [regex]::Match($line, $saveOpenPattern)
                                    if ($saveMatch.Success -and $saveMatch.Groups[1].Value.IndexOf($batchModePlan.testSettlementName, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
                                        $saveOpenSeen = $true
                                        $saveOpenAt = [math]::Round($elapsed, 1)
                                    }
                                }
                                if (-not $errorSeen) {
                                    $isError = ($line -match $errorPattern) -or
                                               ($line.IndexOf('First uncaught exception', [System.StringComparison]::Ordinal) -ge 0) -or
                                               ($null -ne (Test-LineMatchesAny -Line $line -Patterns $blueprintErrorPatterns))
                                    foreach ($exceptionPattern in $exceptionLinePatterns) {
                                        if ($line.IndexOf($exceptionPattern, [System.StringComparison]::Ordinal) -ge 0) { $isError = $true; break }
                                    }
                                    if ($isError) {
                                        $errorSeen = $true
                                        $firstErrorAt = [math]::Round($elapsed, 1)
                                    }
                                }
                            }
                        }
                    }

                    $process.Refresh()
                    if ($process.HasExited) {
                        $launchInfo.processExited = $true
                        try { $launchInfo.exitCode = $process.ExitCode } catch { }
                        break
                    }

                    $elapsed = $sw.Elapsed.TotalSeconds
                    if ($errorSeen -and ($elapsed - $firstErrorAt) -ge $SettleSeconds) { break }
                    if ($startupSeen -and $saveOpenSeen -and ($elapsed - $saveOpenAt) -ge $SettleSeconds) { break }
                    if ($elapsed -ge $TimeoutSeconds) { break }
                }

                $launchInfo.waitedSeconds = [math]::Round($sw.Elapsed.TotalSeconds, 1)
                $launchInfo.hdbfStartupSeen = $startupSeen
                $launchInfo.hdbfStartupAtSeconds = $startupAt
                $launchInfo.saveOpenSeen = $saveOpenSeen
                $launchInfo.saveOpenAtSeconds = $saveOpenAt
                $launchInfo.errorSeen = $errorSeen
                $launchInfo.firstErrorAtSeconds = $firstErrorAt

                $launchInfo.stop = Stop-StartedGame -Process $process -NoWindow

                if (Test-Path -LiteralPath $batchModePlan.gameLogPath) {
                    $final = Read-NewLogBytes -Path $batchModePlan.gameLogPath -Offset $offset
                    if ($final.text.Length -gt 0) {
                        foreach ($line in ($final.text -split "`r?`n")) {
                            if ($line.Length -gt 0) { $capturedLines.Add($line) }
                        }
                    }
                    $logItem = Get-Item -LiteralPath $batchModePlan.gameLogPath
                    $launchInfo.logExists = $true
                    $launchInfo.logBytes = $logItem.Length
                }

                $process.Refresh()
                if ($process.HasExited) {
                    $launchInfo.processExited = $true
                    try { $launchInfo.exitCode = $process.ExitCode } catch { }
                }

                $captureAnalysis = Analyze-LogLines -Lines @($capturedLines)

                $openedIntoTestSettlement = $false
                foreach ($openedPath in $captureAnalysis.openedSavePaths) {
                    if ($openedPath.IndexOf($batchModePlan.testSettlementName, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
                        $openedIntoTestSettlement = $true
                        break
                    }
                }
                $launchInfo.openedSaveIntoTestSettlement = $openedIntoTestSettlement

                $errorReasons = New-Object System.Collections.Generic.List[string]
                if ($captureAnalysis.lineCount -eq 0) {
                    $errorReasons.Add('the game produced no log output at all')
                }
                if (-not $captureAnalysis.hasStartupMarker) {
                    $errorReasons.Add('no HDBF startup marker ([HDBF/Info] start / startup:) appeared in the captured log')
                    foreach ($modWarning in $captureAnalysis.modLoadWarningSamples) {
                        $errorReasons.Add('mod scan/load warning: ' + $modWarning.excerpt)
                    }
                }
                if (-not $openedIntoTestSettlement) {
                    $errorReasons.Add("no save-open marker into Saves\$($batchModePlan.testSettlementName) appeared (the autostart save load was not proven)")
                }
                if ($captureAnalysis.hdbfErrorCount -gt 0) {
                    $errorReasons.Add("HDBF/Error lines: $($captureAnalysis.hdbfErrorCount)")
                }
                if ($captureAnalysis.exceptionSignatureCount -gt 0) {
                    foreach ($signature in $captureAnalysis.exceptionSignatures) {
                        $errorReasons.Add("exception signature: " + $signature.pattern + ' x' + $signature.count)
                    }
                }
                if ($captureAnalysis.blueprintConversionErrorCount -gt 0) {
                    foreach ($signature in $captureAnalysis.blueprintConversionErrors) {
                        $errorReasons.Add("blueprint/conversion error: " + $signature.pattern + ' x' + $signature.count)
                    }
                }

                if ($errorReasons.Count -gt 0) {
                    $verdictStatus = 'Fail'
                    foreach ($reason in $errorReasons) { $verdictReasons.Add($reason) }
                } else {
                    $verdictStatus = 'Pass'
                    $verdictReasons.Add('HDBF startup marker and save-open marker into the throwaway settlement captured; no error signature')
                }

                if ($captureAnalysis.knownNoiseCount -gt 0) {
                    $verdictCaveats.Add("known config.invalid noise present x$($captureAnalysis.knownNoiseCount) (Info level, tracked separately)")
                }
                if ($captureAnalysis.hdbfTickCount -gt 0) {
                    $verdictCaveats.Add("HDBF gameplay lines captured x$($captureAnalysis.hdbfTickCount) (world is running)")
                } else {
                    $verdictCaveats.Add('no HDBF gameplay/tick line captured: the selected save contains no HDBF farm, so HDBF runtime behaviour was not exercised')
                }
                $verdictCaveats.Add('-batchmode displays nothing and never opens the building menu: UI panels, building-button prefab previews and material resolution for previews are NOT exercised (the round-2 material crash cannot be reproduced this way; the captured log may still show a real graphics device being initialized)')
                $verdictCaveats.Add('no building was placed and no world was saved by the harness; placement, farming and save/load of HDBF components still need the windowed in-game run')
                $verdictCaveats.Add('the game may autosave into the throwaway settlement; it is deleted afterwards unless -KeepTestSettlement was passed')
            } catch {
                $verdictStatus = 'Fail'
                $verdictReasons.Add('batchmode launch/capture failed: ' + $_.Exception.Message)
                if ($null -ne $process) {
                    $launchInfo.stop = Stop-StartedGame -Process $process -NoWindow
                }
            } finally {
                # ---- evidence copy of the game log -------------------------------------
                $savedGameLog = $null
                if (Test-Path -LiteralPath $batchModePlan.gameLogPath) {
                    $savedGameLog = Join-Path $outputDir ('batchmode-' + (Get-Date).ToString('yyyyMMdd-HHmmss') + '-Player.log')
                    try { Copy-Item -LiteralPath $batchModePlan.gameLogPath -Destination $savedGameLog -Force } catch { $savedGameLog = $null }
                }

                # ---- cleanup: throwaway settlement folder ------------------------------
                $cleanup = [ordered]@{ kept = $false; deleted = $false; reason = ''; folder = $batchModePlan.destinationFolder }
                if ($KeepTestSettlement) {
                    $cleanup.kept = $true
                    $cleanup.reason = 'kept because -KeepTestSettlement was passed'
                } else {
                    $removal = Remove-OwnedTestSettlement -Folder $batchModePlan.destinationFolder -MarkerName $ownedMarkerName
                    $cleanup.deleted = $removal.deleted
                    $cleanup.reason = $removal.reason
                }

                # ---- cleanup: temporary log dir ---------------------------------------
                $tempLogKept = $false
                if ($KeepGameLog) {
                    $tempLogKept = $true
                } else {
                    try {
                        if (Test-Path -LiteralPath (Split-Path -Parent $batchModePlan.gameLogPath)) {
                            Remove-Item -LiteralPath (Split-Path -Parent $batchModePlan.gameLogPath) -Recurse -Force
                        }
                    } catch { }
                }

                # ---- verification: source save folder must be byte-identical ----------
                $sourceInventoryAfter = Get-SaveInventory -Folder $batchModePlan.sourceFolder
                $sourceUnchanged = ($null -ne $sourceInventoryBefore) -and
                                   ($sourceInventoryBefore.combinedHash -eq $sourceInventoryAfter.combinedHash) -and
                                   ($sourceInventoryBefore.fileCount -eq $sourceInventoryAfter.fileCount)
                $historicalLogHashAfter = Get-Sha256 $logPath
                $historicalLogUnchanged = ($historicalLogHashAfter -eq $historicalLogHashBefore)

                if (-not $sourceUnchanged) {
                    $verdictReasons.Add('SAFETY VIOLATION: the source settlement folder changed during the run')
                    $verdictStatus = 'Fail'
                }
                if (-not $historicalLogUnchanged) {
                    $verdictReasons.Add('SAFETY VIOLATION: the historical Player.log changed during the run')
                    $verdictStatus = 'Fail'
                }

                $batchModeRun = [ordered]@{
                    saveCopy = $copyResult
                    gameLogCopy = $savedGameLog
                    gameLogTempRemoved = (-not $tempLogKept)
                    testSettlementCleanup = $cleanup
                    sourceSettlementUnchanged = $sourceUnchanged
                    sourceInventoryBefore = $sourceInventoryBefore
                    sourceInventoryAfter = $sourceInventoryAfter
                    historicalPlayerLogUnchanged = $historicalLogUnchanged
                    historicalPlayerLogSha256Before = $historicalLogHashBefore
                    historicalPlayerLogSha256After = $historicalLogHashAfter
                }
            }
        }
    } else {
        # -------------------------------------------------- windowed launch (needs a window/audio)
        $startOffset = 0
        if ($logBefore.exists) { $startOffset = $logBefore.length }
        $arguments = @()

        $launchInfo = [ordered]@{
            attempted = $true
            batchMode = $false
            startedAt = (Get-Date).ToString('o')
            exePath = $exePath
            arguments = $arguments
            initialLogOffset = $startOffset
            initialLogLength = $logBefore.length
            processId = $null
            waitedSeconds = 0
            hdbfMarkerSeen = $false
            markerSeenAtSeconds = $null
            processExited = $false
            exitCode = $null
            logRotated = $false
            stop = $null
        }

        $process = $null
        $capturedLines = New-Object System.Collections.Generic.List[string]
        $offset = $startOffset
        $markerSeen = $false
        $markerAtSeconds = $null
        $rotated = $false

        try {
            $process = Start-Process -FilePath $exePath -WorkingDirectory $gameDir -ArgumentList $arguments -PassThru
            $launchInfo.processId = $process.Id
            $sw = [System.Diagnostics.Stopwatch]::StartNew()

            while ($true) {
                Start-Sleep -Milliseconds 1000
                $elapsed = $sw.Elapsed.TotalSeconds

                $chunk = Read-NewLogBytes -Path $logPath -Offset $offset
                if ($chunk.rotated) { $rotated = $true }
                if ($chunk.text.Length -gt 0) {
                    $offset = $chunk.newOffset
                    foreach ($line in ($chunk.text -split "`r?`n")) {
                        if ($line.Length -gt 0) { $capturedLines.Add($line) }
                    }
                }

                if (-not $markerSeen) {
                    foreach ($line in $capturedLines) {
                        if ($line -match $startupPattern) {
                            $markerSeen = $true
                            $markerAtSeconds = [math]::Round($elapsed, 1)
                            break
                        }
                    }
                }

                $process.Refresh()
                if ($process.HasExited) {
                    $launchInfo.processExited = $true
                    try { $launchInfo.exitCode = $process.ExitCode } catch { }
                    break
                }

                if ($markerSeen -and ($elapsed - $markerAtSeconds) -ge $SettleSeconds) { break }
                if ($elapsed -ge $TimeoutSeconds) { break }
            }

            $launchInfo.waitedSeconds = [math]::Round($sw.Elapsed.TotalSeconds, 1)
            $launchInfo.hdbfMarkerSeen = $markerSeen
            $launchInfo.markerSeenAtSeconds = $markerAtSeconds
            $launchInfo.logRotated = $rotated

            $launchInfo.stop = Stop-StartedGame -Process $process -NoWindow

            $final = Read-NewLogBytes -Path $logPath -Offset $offset
            if ($final.rotated) { $rotated = $true }
            if ($final.text.Length -gt 0) {
                foreach ($line in ($final.text -split "`r?`n")) {
                    if ($line.Length -gt 0) { $capturedLines.Add($line) }
                }
            }

            $process.Refresh()
            if ($process.HasExited) {
                $launchInfo.processExited = $true
                try { $launchInfo.exitCode = $process.ExitCode } catch { }
            }

            $captureAnalysis = Analyze-LogLines -Lines @($capturedLines)

            $errors = @($captureAnalysis.exceptionSignatures)
            if (-not $captureAnalysis.hasStartupMarker) {
                $verdictStatus = 'Fail'
                $verdictReasons.Add('no HDBF startup marker ([HDBF/Info] start / startup:) appeared in the captured Player.log lines')
            } elseif ($errors.Count -gt 0) {
                $verdictStatus = 'Fail'
                foreach ($signature in $errors) {
                    $verdictReasons.Add("exception signature in captured log: " + $signature.pattern + ' x' + $signature.count)
                }
            } else {
                $verdictStatus = 'Pass'
                $verdictReasons.Add('HDBF startup marker captured; no exception signature in the captured lines')
            }

            if ($captureAnalysis.hdbfWarnCount -gt 0 -or $captureAnalysis.hdbfErrorCount -gt 0) {
                $verdictCaveats.Add("HDBF logged warnings/errors (Warn=$($captureAnalysis.hdbfWarnCount), Error=$($captureAnalysis.hdbfErrorCount)); inspect the JSON excerpts")
            }
            if ($captureAnalysis.knownNoiseCount -gt 0) {
                $verdictCaveats.Add("known config.invalid noise present x$($captureAnalysis.knownNoiseCount) (non-fatal, tracked separately)")
            }
            $verdictCaveats.Add('the smoke run never loads or saves a world, so it does not prove in-game placement, farming or save behaviour')
        } catch {
            $verdictStatus = 'Fail'
            $verdictReasons.Add('launch/capture failed: ' + $_.Exception.Message)
            if ($null -ne $process) {
                $launchInfo.stop = Stop-StartedGame -Process $process
            }
        }
    }
} else {
    $verdictStatus = 'DryRun'
    if (-not $deploy.ok) {
        $verdictStatus = 'Fail'
        foreach ($problem in $deploy.problems) { $verdictReasons.Add($problem) }
    } else {
        $verdictReasons.Add('deploy package check passed')
    }
    if (-not $workspaceGuard.ok) {
        $verdictReasons.Add('workspace safety guard failed (see workspaceGuard)')
        if ($deploy.ok) { $verdictStatus = 'Fail' }
    }
    foreach ($problem in $preflightProblems) { $verdictReasons.Add($problem) }
    if ($null -ne $batchModePlan) {
        if ($batchModePlan.ok) {
            $verdictReasons.Add('batchmode autostart plan is valid (source save found, throwaway copy path free)')
            if (-not $batchModePlan.destinationExists) {
                $verdictCaveats.Add('plan only: no save was copied and the game was not started; pass -Launch -BatchMode to run it')
            } elseif ($batchModePlan.destinationOwned) {
                $verdictCaveats.Add('throwaway folder already exists and is owned by this script; it will be recreated on -Launch')
            } else {
                $verdictCaveats.Add('throwaway folder already exists and is NOT owned by this script; -Launch will refuse until it is removed: ' + $batchModePlan.destinationFolder)
            }
        } else {
            if ($deploy.ok) { $verdictStatus = 'Fail' }
            foreach ($problem in $batchModePlan.problems) { $verdictReasons.Add($problem) }
        }
    }
    if (-not $logBefore.exists) {
        $verdictCaveats.Add('Player.log not found; log analysis skipped (run with -Launch after a game session exists)')
    } elseif (-not $analysisBefore.hasStartupMarker) {
        $verdictCaveats.Add('the analysed log contains no HDBF startup marker; the mod may not have been loaded in that session (check modLoadWarnings)')
    } elseif ($analysisBefore.hasStartupMarker -and $analysisBefore.exceptionSignatures.Count -gt 0) {
        $verdictCaveats.Add('the existing Player.log is HISTORICAL: it contains HDBF startup lines AND exception signatures from a previous session; it is not evidence about the currently deployed package')
    } elseif ($analysisBefore.exceptionSignatureCount -gt 0) {
        $verdictCaveats.Add('the existing Player.log contains exception signatures from a previous session (stale)')
    }
    if ($null -ne $logAgeSeconds -and $logAgeSeconds -gt 3600) {
        $verdictCaveats.Add("Player.log was last written $([math]::Round($logAgeSeconds / 3600, 1)) h before this run; treat it as historical")
    }
}

# ----------------------------------------------------------------------------- evidence

$timestamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
$outputPath = Join-Path $outputDir ("smoke-{0}.json" -f $timestamp)
$mode = if ($Launch) { if ($BatchMode) { 'BatchModeLaunch' } else { 'WindowedLaunch' } } elseif ($BatchMode) { 'BatchModePlan' } else { 'DryRun' }
$exitCode = 0
if ($verdictStatus -eq 'Refused') { $exitCode = 4 }
elseif ($verdictStatus -eq 'Fail' -and $Launch) { $exitCode = 3 }
elseif ($verdictStatus -eq 'Fail') { $exitCode = 1 }

$gameVersion = $null
try {
    $versionInfo = (Get-Item -LiteralPath $exePath).VersionInfo
    $gameVersion = $versionInfo.FileVersion
} catch { }

$evidence = [ordered]@{
    schemaVersion = 2
    tool = 'tools/smoke/Invoke-GameSmoke.ps1'
    toolVersion = $scriptVersion
    mode = $mode
    batchMode = [bool]$BatchMode
    startedAt = $startedAt.ToString('o')
    finishedAt = (Get-Date).ToString('o')
    durationSeconds = [math]::Round(((Get-Date) - $startedAt).TotalSeconds, 1)
    environment = [ordered]@{
        user = $env:USERNAME
        machine = $env:COMPUTERNAME
        powershell = $PSVersionTable.PSVersion.ToString()
        gameDir = $gameDir
        gameExeVersion = $gameVersion
        unityDllPresent = (Test-Path -LiteralPath (Join-Path $gameDir 'UnityPlayer.dll'))
        steamRunning = (@(Get-Process -Name 'steam' -ErrorAction SilentlyContinue).Count -gt 0)
        userDataDir = $userDataDir
        savesRoot = $savesRoot
    }
    paths = [ordered]@{
        modDir = $modDir
        logPath = $logPath
        outputFile = $outputPath
        stageDir = $stageDir
    }
    preflightProblems = $preflightProblems.ToArray()
    deploy = $deploy
    workspaceGuard = $workspaceGuard
    batchModePlan = $batchModePlan
    log = [ordered]@{
        exists = $logBefore.exists
        path = $logPath
        length = $logBefore.length
        lastWriteTime = $logBefore.lastWriteTime
        ageSeconds = $logAgeSeconds
        tailBytes = $logBefore.tailBytes
        tailTruncatedFromStart = $logBefore.truncatedFromStart
        historicalSha256Before = $historicalLogHashBefore
        historicalSha256After = $historicalLogHashAfter
    }
    analysisBefore = $analysisBefore
    launch = $launchInfo
    captureAnalysis = $captureAnalysis
    batchModeRun = $batchModeRun
    verdict = [ordered]@{
        status = $verdictStatus
        reasons = $verdictReasons.ToArray()
        caveats = $verdictCaveats.ToArray()
    }
    exitCode = $exitCode
}

$json = $evidence | ConvertTo-Json -Depth 14
[System.IO.File]::WriteAllText($outputPath, $json, (New-Object System.Text.UTF8Encoding($false)))

# ----------------------------------------------------------------------------- console summary

Write-Host ''
Write-Host ('HDBF smoke v{0} - mode {1}' -f $scriptVersion, $mode) -ForegroundColor Cyan
Write-Host ('  mod folder        : {0}' -f $modDir)
Write-Host ('  deploy package    : {0} ({1} files, {2:N0} KB)' -f $(if ($deploy.ok) { 'OK' } else { 'FAIL' }), @($deploy.assemblies).Count, ($deploy.packageBytes / 1KB))
if ($null -ne $deploy.manifest) {
    Write-Host ('  manifest          : Id={0} Version={1} MinimumGameVersion={2}' -f $deploy.manifest.id, $deploy.manifest.version, $deploy.manifest.minimumGameVersion)
}
Write-Host ('  workspace guard   : {0}' -f $(if ($workspaceGuard.ok) { 'OK (not a mod folder)' } else { 'FAIL' }))
foreach ($problem in $deploy.problems) { Write-Host ('  problem           : ' + $problem) -ForegroundColor Red }
foreach ($problem in $preflightProblems) { Write-Host ('  problem           : ' + $problem) -ForegroundColor Red }

Write-Host ('  Player.log        : {0}' -f $(if ($logBefore.exists) { ('{0} bytes, last write {1}' -f $logBefore.length, $logBefore.lastWriteTime) } else { 'missing' }))
if ($logBefore.exists) {
    Write-Host ('  log analysis      : Info={0} Compat={1} Warn={2} Error={3} startupMarker={4} exceptionSignatures={5}' -f `
        $analysisBefore.hdbfInfoCount, $analysisBefore.hdbfCompatCount, $analysisBefore.hdbfWarnCount, `
        $analysisBefore.hdbfErrorCount, $analysisBefore.hasStartupMarker, $analysisBefore.exceptionSignatureCount)
    foreach ($signature in $analysisBefore.exceptionSignatures) {
        Write-Host ('    exception       : {0} x{1}' -f $signature.pattern, $signature.count) -ForegroundColor Yellow
    }
    foreach ($noise in $analysisBefore.knownNoiseExcerpts) {
        Write-Host ('    known noise     : ' + $noise) -ForegroundColor DarkYellow
        break
    }
}

if ($null -ne $batchModePlan) {
    Write-Host '  batchmode plan    :' -ForegroundColor Cyan
    Write-Host ('    source settlement : {0} ({1} files, {2:N0} bytes)' -f $batchModePlan.sourceSettlement, $batchModePlan.sourceFileCount, $batchModePlan.sourceTotalBytes)
    Write-Host ('    save              : {0}.timber ({1:N0} bytes)' -f $batchModePlan.saveName, $batchModePlan.saveBytes)
    Write-Host ('    throwaway copy    : {0}' -f $batchModePlan.destinationFolder)
    Write-Host ('    game log          : {0}' -f $batchModePlan.gameLogPath)
    Write-Host ('    exact command     : {0}' -f $batchModePlan.exactCommandLine)
    foreach ($problem in $batchModePlan.problems) { Write-Host ('    problem           : ' + $problem) -ForegroundColor Red }
}

if ($Launch) {
    if ($null -ne $launchInfo) {
        if (-not $launchInfo.attempted) {
            Write-Host ('  launch            : REFUSED - ' + $launchInfo.skippedReason) -ForegroundColor Red
        } elseif ($launchInfo.batchMode) {
            Write-Host ('  launch (batchmode): PID {0}, waited {1} s, startup={2}, saveOpen={3}, error={4}, exited={5}' -f `
                $launchInfo.processId, $launchInfo.waitedSeconds, $launchInfo.hdbfStartupSeen, `
                $launchInfo.saveOpenSeen, $launchInfo.errorSeen, $launchInfo.processExited)
            Write-Host ('  captured lines    : {0}' -f $captureAnalysis.lineCount)
            Write-Host ('  opened save paths : {0}' -f (($captureAnalysis.openedSavePaths) -join ' | '))
            Write-Host ('  stop              : {0} forced={1}' -f $launchInfo.stop.method, $launchInfo.stop.forced)
            if ($null -ne $batchModeRun) {
                Write-Host ('  game log copy     : {0}' -f $batchModeRun.gameLogCopy)
                Write-Host ('  test settlement   : {0} ({1})' -f $batchModeRun.testSettlementCleanup.folder, $batchModeRun.testSettlementCleanup.reason)
                Write-Host ('  source untouched  : {0}; historical Player.log untouched: {1}' -f `
                    $(if ($batchModeRun.sourceSettlementUnchanged) { 'YES' } else { 'NO' }), `
                    $(if ($batchModeRun.historicalPlayerLogUnchanged) { 'YES' } else { 'NO' }))
            }
        } else {
            Write-Host ('  launch            : PID {0}, args [{1}], waited {2} s, marker={3}, exited={4}' -f `
                $launchInfo.processId, ($launchInfo.arguments -join ' '), $launchInfo.waitedSeconds, `
                $launchInfo.hdbfMarkerSeen, $launchInfo.processExited)
            Write-Host ('  captured lines    : {0}' -f $captureAnalysis.lineCount)
            Write-Host ('  stop              : {0} forced={1}' -f $launchInfo.stop.method, $launchInfo.stop.forced)
        }
    }
}

$color = switch ($verdictStatus) {
    'Pass' { 'Green' }
    'DryRun' { 'Green' }
    default { 'Red' }
}
Write-Host ('  verdict           : {0}' -f $verdictStatus) -ForegroundColor $color
foreach ($reason in $verdictReasons) { Write-Host ('    reason          : ' + $reason) }
foreach ($caveat in $verdictCaveats) { Write-Host ('    caveat          : ' + $caveat) -ForegroundColor DarkGray }
Write-Host ('  evidence          : {0}' -f $outputPath)
Write-Host ''

exit $exitCode
