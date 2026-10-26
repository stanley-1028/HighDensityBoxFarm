#requires -Version 5.1
<#
.SYNOPSIS
    Repeatable compatibility matrix for the five optional Timberborn mods (HDBF COMPAT-01).

.DESCRIPTION
    The five third-party mods are already downloaded under the Steam Workshop content folder but
    are NOT enabled. This script owns the install/remove lifecycle and the Player.log check, so the
    matrix in docs/workstreams/compat-01-adapters.md (TC-02..TC-08) is reproducible:

      * DryRun  (default) : print the exact source->destination copies and SHA-256 hashes, write nothing.
      * -Install          : copy the selected version folders into Documents\Timberborn\Mods\<Folder>,
                            after backing up the current Mods directory listing and writing an
                            ownership marker file that records every folder this script created.
      * -Remove           : delete ONLY folders listed in the ownership marker. Never touches any
                            other mod and never touches Saves.
      * -CheckLog <path>  : parse the HDBF/Compat lines of a Player.log and compare them with the
                            expected matrix row (no game is launched by this script).

    Safety rules:
      * no action switch => DryRun, regardless of other parameters;
      * -Install refuses to overwrite an unowned folder with one of its target names;
      * -Remove refuses to delete anything not recorded in the ownership marker;
      * the ownership marker and backups live outside the Mods folder (%USERPROFILE%\.hdbf\...);
      * no network access, no deployment of HDBF itself, no build.ps1 invocation.

.PARAMETER Install
    Copy the selected mods into the Mods folder. Combine with -DryRun for a preview.

.PARAMETER Remove
    Remove only the folders recorded in the ownership marker.

.PARAMETER CheckLog
    Path to a Timberborn Player.log. Parses HDBF/Compat enabled/missing/failed lines.

.PARAMETER Adapters
    Matrix rows to act on: TimberPipes, PowerLines, ConveyorBeltsV2, MoreMines, HoomanStairs.
    Default: all five. For the per-mod scenarios (TC-02..TC-06) pass exactly one.

.PARAMETER Expect
    Expected installed set for -CheckLog. Default: the -Adapters value. 'All' / 'None' accepted.
    Every expected adapter must log compat.enabled with api-anchors >= 1; every other adapter must
    log compat.missing; any compat.failed line fails the check.

.PARAMETER DryRun
    Force preview mode even when -Install / -Remove is present.

.PARAMETER Force
    Allow replacing a folder that this script already owns (re-install of the same matrix row).

.PARAMETER NoHash
    Skip SHA-256 computation in preview/install output (faster on huge mod folders).

.PARAMETER ModsRoot
    Override for Documents\Timberborn\Mods (tests / non-standard documents folder).

.PARAMETER StateRoot
    Override for the ownership marker + backups folder (default %USERPROFILE%\.hdbf\compat-matrix).

.PARAMETER WorkshopRoot
    Override for the Steam Workshop content root (default C:\Program Files (x86)\Steam\...\1062090).

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\dev\CompatMatrix.ps1

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\dev\CompatMatrix.ps1 -Install -Adapters TimberPipes

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\dev\CompatMatrix.ps1 -CheckLog "$env:TEMP\Player.log" -Adapters TimberPipes

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\dev\CompatMatrix.ps1 -Remove
#>
[CmdletBinding()]
param(
    [switch]$Install,
    [switch]$Remove,
    [string]$CheckLog,
    [string[]]$Adapters = @('TimberPipes', 'PowerLines', 'ConveyorBeltsV2', 'MoreMines', 'HoomanStairs'),
    [string[]]$Expect,
    [switch]$DryRun,
    [switch]$Force,
    [switch]$NoDependencies,
    [switch]$NoHash,
    [string]$ModsRoot,
    [string]$StateRoot,
    [string]$WorkshopRoot = 'C:\Program Files (x86)\Steam\steamapps\workshop\content\1062090'
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

# --------------------------------------------------------------------------------------------
# Verified mod table (2026-10-25, game 1.1.2.8, docs/workstreams/compat-01-adapters.md).
# Folder = the directory this script creates under Documents\Timberborn\Mods. It is deliberately
# namespaced so it can never collide with a player's own mod folder or with the HDBF deployment.
# --------------------------------------------------------------------------------------------
$ModTable = @(
    [pscustomobject]@{ Key = 'TimberPipes';      WorkshopId = '3801726769'; VersionDir = 'version-1.1';     Folder = 'HDBFCompat.TimberPipes';      ModId = 'TimberPipes';              Name = 'Timber Pipes' },
    [pscustomobject]@{ Key = 'PowerLines';       WorkshopId = '3766893705'; VersionDir = 'version-1.1';     Folder = 'HDBFCompat.PowerLines';       ModId = 'PowerLines';               Name = 'Power Lines' },
    [pscustomobject]@{ Key = 'ConveyorBeltsV2';  WorkshopId = '3815376360'; VersionDir = 'version-1.1';     Folder = 'HDBFCompat.ConveyorBeltsV2';  ModId = 'ConveyorBelt';             Name = 'Conveyor Belts v2' },
    [pscustomobject]@{ Key = 'MoreMines';        WorkshopId = '3723216060'; VersionDir = 'version-1.1.0.2'; Folder = 'HDBFCompat.MoreMines';        ModId = 'TonWolfe.MoreMines';       Name = 'More Mines [ModContest1]' },
    [pscustomobject]@{ Key = 'HoomanStairs';     WorkshopId = '3682012904'; VersionDir = 'Version-1.1';     Folder = 'HDBFCompat.HoomanStairs';     ModId = 'Calloatti.HoomanStairs';   Name = 'Hooman Stairs' }
)


# --------------------------------------------------------------------------------------------
# Transitive dependencies of the five target mods (resolved from the workshop manifests on
# 2026-10-25: the modern Conveyor Belts v2 / Timber Pipes need ModdableTimberborn + ModdableToolGroups
# + ModSettings, Power Lines needs TimberUi, More Mines needs TimberUi + MoreTunnels.Ladder +
# TradeOffice, everything needs Harmony). Without this closure the game refuses to load the target
# mods, so the matrix would only prove "missing", not integration.
# Folder names keep the same HDBFCompat. prefix and are removed by -Remove through the marker.
# --------------------------------------------------------------------------------------------
$DependencyTable = @(
    [pscustomobject]@{ Key = 'ModdableTimberborn';   WorkshopId = '3578009707'; VersionDir = 'version-1.1';  Folder = 'HDBFCompat.Dep.ModdableTimberborn';   ModId = 'ModdableTimberborn';       Name = 'Moddable Timberborn' },
    [pscustomobject]@{ Key = 'ModdableToolGroups';   WorkshopId = '3612110827'; VersionDir = 'version-1.1';  Folder = 'HDBFCompat.Dep.ModdableToolGroups';   ModId = 'ModdableToolGroups';       Name = 'Moddable Tool Groups' },
    [pscustomobject]@{ Key = 'ModSettings';          WorkshopId = '3283831040'; VersionDir = 'version-1.1';  Folder = 'HDBFCompat.Dep.ModSettings';          ModId = 'eMka.ModSettings';         Name = 'Mod Settings' },
    [pscustomobject]@{ Key = 'TimberUi';             WorkshopId = '3430626620'; VersionDir = 'version-1.1';  Folder = 'HDBFCompat.Dep.TimberUi';             ModId = 'TimberUi';                 Name = 'TimberUi' },
    [pscustomobject]@{ Key = 'MoreTunnels';          WorkshopId = '3471578532'; VersionDir = 'version-1.1.0.2'; Folder = 'HDBFCompat.Dep.MoreTunnels';       ModId = 'MoreTunnels';              Name = 'More Tunnels' },
    [pscustomobject]@{ Key = 'MoreTunnelsLadder';    WorkshopId = '3603928258'; VersionDir = 'version-1.0.7.0'; Folder = 'HDBFCompat.Dep.MoreTunnelsLadder'; ModId = 'MoreTunnels.Ladder';       Name = 'More Tunnels - Ladder' },
    [pscustomobject]@{ Key = 'TradeOffice';          WorkshopId = '3721718127'; VersionDir = 'version-1.1.0.0'; Folder = 'HDBFCompat.Dep.TradeOffice';       ModId = 'TonWolfe.TradeOffice';     Name = 'Trade Office' },
    [pscustomobject]@{ Key = 'TobbertLadder';        WorkshopId = '3286476486'; VersionDir = 'version-1.1';  Folder = 'HDBFCompat.Dep.TobbertLadder';        ModId = 'Tobbert.Ladder';           Name = 'Ladder' }
)

$AdapterOrder = @('TimberPipes', 'PowerLines', 'ConveyorBeltsV2', 'MoreMines', 'HoomanStairs')
$AdapterIds = @{
    TimberPipes     = 'HDBF.Adapter.TimberPipes'
    PowerLines      = 'HDBF.Adapter.PowerLines'
    ConveyorBeltsV2 = 'HDBF.Adapter.ConveyorBeltsV2'
    MoreMines       = 'HDBF.Adapter.MoreMines'
    HoomanStairs    = 'HDBF.Adapter.HoomanStairs'
}

function Get-DefaultModsRoot {
    $documents = [Environment]::GetFolderPath('MyDocuments')
    if ([string]::IsNullOrEmpty($documents)) {
        $documents = Join-Path $env:USERPROFILE 'Documents'
    }
    return (Join-Path (Join-Path $documents 'Timberborn') 'Mods')
}

if ([string]::IsNullOrEmpty($ModsRoot)) {
    $ModsRoot = Get-DefaultModsRoot
}
if ([string]::IsNullOrEmpty($StateRoot)) {
    $StateRoot = Join-Path (Join-Path $env:USERPROFILE '.hdbf') 'compat-matrix'
}

$MarkerPath = Join-Path $StateRoot 'ownership.json'
$BackupDir = Join-Path $StateRoot 'backups'

# --------------------------------------------------------------------------------------------
# Helpers
# --------------------------------------------------------------------------------------------
function Write-Info([string]$message) { Write-Host $message }
function Write-Step([string]$message) { Write-Host ''; Write-Host ('== ' + $message) -ForegroundColor Cyan }
function Write-Ok([string]$message) { Write-Host ('[OK]   ' + $message) -ForegroundColor Green }
function Write-Warn2([string]$message) { Write-Host ('[WARN] ' + $message) -ForegroundColor Yellow }
function Fail([string]$message) {
    Write-Host ('[FAIL] ' + $message) -ForegroundColor Red
    exit 1
}

function Resolve-AdapterRows {
    param([string[]]$Keys)
    if ($null -eq $Keys -or $Keys.Count -eq 0) {
        Fail 'no adapters selected'
    }

    $expanded = @()
    foreach ($key in $Keys) {
        foreach ($part in ($key -split ',')) {
            $trimmed = $part.Trim()
            if ($trimmed.Length -eq 0) { continue }
            if ($trimmed -ieq 'All') {
                foreach ($item in $ModTable) { $expanded += $item }
                continue
            }
            if ($trimmed -ieq 'None') {
                continue
            }
            $match = $ModTable | Where-Object { $_.Key -ieq $trimmed }
            if ($null -eq $match) {
                Fail ("unknown adapter '" + $trimmed + "'; expected one of " + ($AdapterOrder -join ', ') + ' or All/None')
            }
            $expanded += $match
        }
    }

    # De-duplicate while keeping the fixed chapter 8.1 order.
    $ordered = @()
    foreach ($key in $AdapterOrder) {
        foreach ($item in $expanded) {
            if ($item.Key -eq $key -and -not ($ordered | Where-Object { $_.Key -eq $key })) {
                $ordered += $item
            }
        }
    }
    return $ordered
}

function Resolve-ExpectRows {
    if ($null -eq $Expect -or $Expect.Count -eq 0) {
        return (Resolve-AdapterRows $Adapters)
    }
    $onlyNone = $true
    foreach ($item in $Expect) {
        foreach ($part in ($item -split ',')) {
            if ($part.Trim().Length -gt 0 -and $part.Trim() -ine 'None') { $onlyNone = $false }
        }
    }
    if ($onlyNone) { return @() }
    return (Resolve-AdapterRows $Expect)
}

function Get-SourceDir {
    param($Row)
    return (Join-Path (Join-Path $WorkshopRoot $Row.WorkshopId) $Row.VersionDir)
}

function Get-TreeInfo {
    param([string]$Directory, [bool]$ComputeHash)
    $files = @(Get-ChildItem -LiteralPath $Directory -Recurse -File -Force | Sort-Object FullName)
    $totalBytes = 0
    foreach ($file in $files) { $totalBytes += $file.Length }

    $treeHash = $null
    if ($ComputeHash) {
        $builder = New-Object System.Text.StringBuilder
        foreach ($file in $files) {
            $relative = $file.FullName.Substring($Directory.Length).TrimStart('\', '/')
            $fileHash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
            [void]$builder.Append($relative.Replace('\', '/')).Append('|').Append($fileHash).Append("`n")
        }
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($builder.ToString())
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try {
            $treeHash = ([System.BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '')
        }
        finally {
            $sha.Dispose()
        }
    }

    return [pscustomobject]@{
        FileCount = $files.Count
        TotalBytes = $totalBytes
        TreeSha256 = $treeHash
    }
}

function Read-Manifest {
    param([string]$Directory)
    $manifestPath = Join-Path $Directory 'manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        return $null
    }
    try {
        return (Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json)
    }
    catch {
        return $null
    }
}

function Assert-SourceRow {
    param($Row)
    $source = Get-SourceDir $Row
    if (-not (Test-Path -LiteralPath $source -PathType Container)) {
        Fail ('source version folder not found: ' + $source)
    }
    $manifest = Read-Manifest $source
    if ($null -eq $manifest) {
        Fail ('manifest.json missing or unreadable in ' + $source)
    }
    if ($manifest.Id -ne $Row.ModId) {
        Fail ('manifest Id mismatch in ' + $source + ': expected ' + $Row.ModId + ', got ' + $manifest.Id)
    }
    if ($manifest.MinimumGameVersion) {
        $min = [string]$manifest.MinimumGameVersion
        # Compare against the installed game instead of demanding a 1.1.x minimum: dependencies
        # legitimately declare older minimums (ModdableToolGroups 1.0.0, Harmony 0.6.0.0) and still
        # load on 1.1.2.8. A minimum NEWER than the installed game is the real refusal condition.
        $minOk = $true
        try { $minOk = ([version]$min) -le ([version]'1.1.2.8') } catch { $minOk = $true }
        if (-not $minOk) {
            Fail ('manifest MinimumGameVersion ' + $min + ' in ' + $source + ' is newer than the installed game 1.1.2.8')
        }
    }
    return [pscustomobject]@{ Directory = $source; Manifest = $manifest }
}

function Assert-DestinationPath {
    param([string]$Folder)
    $row = @($ModTable) + @($DependencyTable) | Where-Object { $_.Folder -eq $Folder }
    if ($null -eq $row) {
        Fail ('refusing to touch unknown folder name: ' + $Folder)
    }
    $modsFull = [System.IO.Path]::GetFullPath($ModsRoot).TrimEnd('\', '/')
    $destFull = [System.IO.Path]::GetFullPath((Join-Path $ModsRoot $Folder))
    if (-not $destFull.StartsWith($modsFull + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
        Fail ('destination escapes the Mods root: ' + $destFull)
    }
    if ($Folder -ieq 'HDBF') {
        Fail 'refusing to touch the HDBF deployment folder'
    }
    return $destFull
}

function Read-Ownership {
    if (-not (Test-Path -LiteralPath $MarkerPath -PathType Leaf)) {
        return [pscustomobject]@{ InstalledAt = $null; ModsRoot = $ModsRoot; Entries = @() }
    }
    try {
        $marker = Get-Content -LiteralPath $MarkerPath -Raw | ConvertFrom-Json
        if ($null -eq $marker.Entries) { $marker | Add-Member -NotePropertyName Entries -NotePropertyValue @() }
        return $marker
    }
    catch {
        Fail ('ownership marker is unreadable; fix or remove it manually: ' + $MarkerPath)
    }
}

function Write-Ownership {
    param($Marker)
    if (-not (Test-Path -LiteralPath $StateRoot -PathType Container)) {
        New-Item -ItemType Directory -Path $StateRoot -Force | Out-Null
    }
    $Marker | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $MarkerPath -Encoding UTF8
}

function New-ModsBackup {
    $backupFile = Join-Path $BackupDir ('mods-before-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.txt')
    if (-not (Test-Path -LiteralPath $BackupDir -PathType Container)) {
        New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null
    }
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('# HDBF compatibility matrix - Mods directory listing taken before install')
    $lines.Add('# timestamp: ' + (Get-Date -Format 'o'))
    $lines.Add('# modsRoot: ' + $ModsRoot)
    $lines.Add('# Never deleted by this script unless the folder is listed in ownership.json.')
    if (Test-Path -LiteralPath $ModsRoot -PathType Container) {
        foreach ($child in (Get-ChildItem -LiteralPath $ModsRoot -Force | Sort-Object Name)) {
            if ($child.PSIsContainer) {
                $manifest = Read-Manifest $child.FullName
                if ($null -ne $manifest) {
                    $lines.Add('D|' + $child.Name + '|Id=' + $manifest.Id + '|Name=' + $manifest.Name + '|Version=' + $manifest.Version)
                }
                else {
                    $lines.Add('D|' + $child.Name + '|(no manifest)')
                }
            }
            else {
                $lines.Add('F|' + $child.Name)
            }
        }
    }
    else {
        $lines.Add('(Mods root did not exist)')
    }
    Set-Content -LiteralPath $backupFile -Value $lines -Encoding UTF8
    return $backupFile
}

# --------------------------------------------------------------------------------------------
# CheckLog parser (TC-02..TC-08 expected evidence lives in HDBF/Compat lines)
# --------------------------------------------------------------------------------------------
function Invoke-CheckLog {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Fail ('log file not found: ' + $Path)
    }

    $enabled = @{}
    $missing = @{}
    $failed = @{}
    $anchors = @{}
    $evidence = @{}
    $errorCount = 0
    $warnCount = 0

    # The runtime line repeats the adapter id between the key and the state
    # ("compat.enabled:HDBF.Adapter.X: HDBF.Adapter.X active; api-anchors=4"), so allow the
    # optional repeat instead of requiring "active;" right after the captured id.
    $enabledPattern = 'HDBF/(?:Compat|Info):\s+compat\.enabled:(HDBF\.Adapter\.[A-Za-z0-9]+):?[^;]*?active;\s+api-anchors=(\d+)'
    $missingPattern = 'HDBF/Compat:\s+compat\.missing:(HDBF\.Adapter\.[A-Za-z0-9]+)'
    $failedPattern = 'HDBF/Compat:\s+compat\.failed:(HDBF\.Adapter\.[A-Za-z0-9]+)'

    foreach ($line in [System.IO.File]::ReadLines((Resolve-Path -LiteralPath $Path).ProviderPath)) {
        if ($line -match $enabledPattern) {
            $id = $Matches[1]
            $enabled[$id] = $true
            $anchors[$id] = [int]$Matches[2]
            $evidence[$id] = $line.Trim()
            continue
        }
        if ($line -match $missingPattern) {
            $id = $Matches[1]
            $missing[$id] = $true
            if (-not $evidence.ContainsKey($id)) { $evidence[$id] = $line.Trim() }
            continue
        }
        if ($line -match $failedPattern) {
            $id = $Matches[1]
            $failed[$id] = $true
            if (-not $evidence.ContainsKey($id)) { $evidence[$id] = $line.Trim() }
            continue
        }
        if ($line -match 'HDBF/Error') { $errorCount++ }
        if ($line -match 'HDBF/Warn') { $warnCount++ }
    }

    $expectedRows = Resolve-ExpectRows
    $expectedKeys = @()
    foreach ($row in $expectedRows) { $expectedKeys += $row.Key }

    Write-Step 'Compatibility matrix log check'
    Write-Info ('log:      ' + $Path)
    Write-Info ('expected: ' + (($expectedRows | ForEach-Object { $_.Key }) -join ', '))
    Write-Info ''
    Write-Info ('{0,-20} {1,-11} {2,-10} {3,-8} {4,-8} {5,-8} {6}' -f 'adapter', 'expected', 'detected', 'enabled', 'failed', 'anchors', 'result')

    $failures = New-Object System.Collections.Generic.List[string]

    foreach ($key in $AdapterOrder) {
        $id = $AdapterIds[$key]
        $isExpected = $expectedKeys -contains $key
        $isEnabled = $enabled.ContainsKey($id)
        $isMissing = $missing.ContainsKey($id)
        $isFailed = $failed.ContainsKey($id)
        $anchorCount = if ($anchors.ContainsKey($id)) { $anchors[$id] } else { 0 }
        $detected = if ($isEnabled -or $isFailed) { 'yes' } elseif ($isMissing) { 'no' } else { 'unknown' }

        $result = 'PASS'
        if ($isExpected) {
            if ($isFailed) { $result = 'FAIL(failed)' }
            elseif (-not $isEnabled) { $result = 'FAIL(not-enabled)' }
            elseif ($anchorCount -lt 1) { $result = 'FAIL(no-anchors)' }
        }
        else {
            if ($isEnabled -or $isFailed) { $result = 'FAIL(unexpected)' }
            elseif (-not $isMissing) { $result = 'FAIL(no-line)' }
        }

        if ($result -ne 'PASS') {
            $failures.Add($key + ': ' + $result)
        }

        Write-Info ('{0,-20} {1,-11} {2,-10} {3,-8} {4,-8} {5,-8} {6}' -f
            $key,
            $(if ($isExpected) { 'installed' } else { 'absent' }),
            $detected,
            $(if ($isEnabled) { 'yes' } else { 'no' }),
            $(if ($isFailed) { 'yes' } else { 'no' }),
            $anchorCount,
            $result)

        if ($evidence.ContainsKey($id)) {
            Write-Info ('    evidence: ' + $evidence[$id])
        }
    }

    Write-Info ''
    Write-Info ('HDBF/Error lines: ' + $errorCount + '   HDBF/Warn lines: ' + $warnCount)

    if ($failures.Count -gt 0) {
        Write-Info ''
        foreach ($failure in $failures) { Write-Warn2 $failure }
        Write-Host 'MATRIX RESULT: FAILED' -ForegroundColor Red
        exit 1
    }

    Write-Host 'MATRIX RESULT: OK' -ForegroundColor Green
    exit 0
}

# --------------------------------------------------------------------------------------------
# DryRun preview
# --------------------------------------------------------------------------------------------
function Invoke-Preview {
    param($Rows)
    Write-Step 'DRY RUN - nothing will be written'
    Write-Info ('Mods root:  ' + $ModsRoot)
    Write-Info ('State root: ' + $StateRoot)
    Write-Info ('Workshop:   ' + $WorkshopRoot)
    Write-Info ''

    $marker = Read-Ownership
    $ownedFolders = @()
    foreach ($entry in $marker.Entries) { $ownedFolders += $entry.folder }

    foreach ($row in $Rows) {
        $source = Assert-SourceRow $row
        $dest = Assert-DestinationPath $row.Folder
        $info = Get-TreeInfo $source.Directory (-not $NoHash)
        Write-Info ('[' + $row.Key + '] ' + $row.Name + ' ' + $source.Manifest.Version + '  (Id ' + $source.Manifest.Id + ')')
        Write-Info ('  source:     ' + $source.Directory)
        Write-Info ('  destination:' + $dest)
        Write-Info ('  files:      ' + $info.FileCount + '   bytes: ' + $info.TotalBytes)
        if ($info.TreeSha256) {
            Write-Info ('  tree sha256:' + $info.TreeSha256)
        }
        $manifestPath = Join-Path $source.Directory 'manifest.json'
        if (-not $NoHash) {
            Write-Info ('  manifest sha256: ' + (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash)
        }
        $destExists = Test-Path -LiteralPath $dest -PathType Container
        if (-not $destExists) {
            Write-Info ('  destination exists: False')
        }
        elseif ($ownedFolders -contains $row.Folder) {
            Write-Info ('  destination exists: True (owned by this script)')
        }
        else {
            Write-Info ('  destination exists: True (NOT owned - install would refuse)')
        }
        Write-Info ''
    }

    Write-Step 'DryRun summary'
    Write-Info ('To install:  powershell -NoProfile -ExecutionPolicy Bypass -File tools\dev\CompatMatrix.ps1 -Install -Adapters ' + (($Rows | ForEach-Object { $_.Key }) -join ','))
    Write-Info ('To verify:   powershell -NoProfile -ExecutionPolicy Bypass -File tools\dev\CompatMatrix.ps1 -CheckLog <Player.log> -Adapters ' + (($Rows | ForEach-Object { $_.Key }) -join ','))
    Write-Info 'To remove:   powershell -NoProfile -ExecutionPolicy Bypass -File tools\dev\CompatMatrix.ps1 -Remove'
    Write-Info 'No files were written.'
}

# --------------------------------------------------------------------------------------------
# Install
# --------------------------------------------------------------------------------------------
function Invoke-Install {
    param($Rows)

    Write-Step 'INSTALL optional mods for the compatibility matrix'
    Write-Info ('Mods root:  ' + $ModsRoot)
    Write-Info ('State root: ' + $StateRoot)
    $rowsToInstall = @()

    # Validate every source + destination before writing anything.
    foreach ($row in $Rows) {
        $source = Assert-SourceRow $row
        $dest = Assert-DestinationPath $row.Folder
        $rowsToInstall += [pscustomobject]@{ Row = $row; Source = $source; Dest = $dest }
    }

    if (-not (Test-Path -LiteralPath $ModsRoot -PathType Container)) {
        $parent = Split-Path -Parent $ModsRoot
        if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
            Fail ('cannot create Mods root, parent does not exist: ' + $parent)
        }
        New-Item -ItemType Directory -Path $ModsRoot -Force | Out-Null
        Write-Info ('created Mods root: ' + $ModsRoot)
    }

    $marker = Read-Ownership
    if ($null -ne $marker.modsRoot -and [string]$marker.modsRoot -ne '') {
        $markerRoot = [System.IO.Path]::GetFullPath([string]$marker.modsRoot).TrimEnd('\', '/')
        $currentRoot = [System.IO.Path]::GetFullPath($ModsRoot).TrimEnd('\', '/')
        if ($markerRoot -ne $currentRoot) {
            Fail ('ownership marker belongs to a different Mods root: ' + $markerRoot + ' (current: ' + $currentRoot + ')')
        }
    }
    $existingEntries = New-Object System.Collections.Generic.List[object]
    foreach ($entry in $marker.Entries) { $existingEntries.Add($entry) | Out-Null }
    $ownedFolders = @()
    foreach ($entry in $existingEntries) { $ownedFolders += [string]$entry.folder }

    foreach ($item in $rowsToInstall) {
        if (Test-Path -LiteralPath $item.Dest -PathType Container) {
            if ($ownedFolders -contains $item.Row.Folder) {
                if ($Force) {
                    Write-Warn2 ('removing previously owned folder before re-install: ' + $item.Dest)
                    Remove-Item -LiteralPath $item.Dest -Recurse -Force
                }
                else {
                    Fail ('destination already exists and is owned by this script: ' + $item.Dest + ' (use -Remove first, or -Force to re-install)')
                }
            }
            else {
                Fail ('destination already exists and is NOT owned by this script: ' + $item.Dest + ' - refusing to overwrite')
            }
        }
    }

    $backupFile = New-ModsBackup
    Write-Info ('backup of current Mods listing: ' + $backupFile)

    $newEntries = New-Object System.Collections.Generic.List[object]
    foreach ($item in $rowsToInstall) {
        $infoBefore = Get-TreeInfo $item.Source.Directory (-not $NoHash)
        Write-Info ('copying ' + $item.Row.Key + ': ' + $item.Source.Directory + ' -> ' + $item.Dest)
        Copy-Item -LiteralPath $item.Source.Directory -Destination $item.Dest -Recurse -Force
        $infoAfter = Get-TreeInfo $item.Dest (-not $NoHash)
        if ($infoAfter.FileCount -ne $infoBefore.FileCount) {
            Fail ('copy verification failed for ' + $item.Row.Key + ': file count ' + $infoAfter.FileCount + ' != ' + $infoBefore.FileCount)
        }
        if ($infoBefore.TreeSha256 -and $infoAfter.TreeSha256 -and $infoBefore.TreeSha256 -ne $infoAfter.TreeSha256) {
            Fail ('copy verification failed for ' + $item.Row.Key + ': tree hash mismatch')
        }
        Write-Ok ('installed ' + $item.Row.Key + ' (' + $infoAfter.FileCount + ' files, ' + $infoAfter.TotalBytes + ' bytes' + $(if ($infoAfter.TreeSha256) { ', sha256 ' + $infoAfter.TreeSha256 } else { '' }) + ')')

        $newEntries.Add([pscustomobject]@{
                folder = $item.Row.Folder
                adapter = $item.Row.Key
                modId = $item.Row.ModId
                modName = [string]$item.Source.Manifest.Name
                manifestVersion = [string]$item.Source.Manifest.Version
                source = $item.Source.Directory
                dest = $item.Dest
                fileCount = $infoAfter.FileCount
                totalBytes = $infoAfter.TotalBytes
                treeSha256 = $infoAfter.TreeSha256
                installedAt = (Get-Date -Format 'o')
            }) | Out-Null
    }

    # Merge: drop entries replaced by this install, keep other owned rows untouched.
    $merged = New-Object System.Collections.Generic.List[object]
    $installedKeys = @()
    foreach ($entry in $newEntries) { $installedKeys += [string]$entry.adapter }
    foreach ($entry in $existingEntries) {
        if ($installedKeys -notcontains [string]$entry.adapter) { $merged.Add($entry) | Out-Null }
    }
    foreach ($entry in $newEntries) { $merged.Add($entry) | Out-Null }

    $newMarker = [pscustomobject]@{
        schemaVersion = 1
        installedAt = (Get-Date -Format 'o')
        modsRoot = $ModsRoot
        workshopRoot = $WorkshopRoot
        backupFile = $backupFile
        entries = $merged.ToArray()
    }
    Write-Ownership $newMarker
    Write-Ok ('ownership marker written: ' + $MarkerPath)
    Write-Info ''
    $installedKeys = @($rowsToInstall | ForEach-Object { $_.Row.Key })
    Write-Info ('Installed matrix row(s): ' + ($installedKeys -join ', '))
    Write-Info 'Next: launch Timberborn with the save copy, then check the Player.log:'
    Write-Info ('  powershell -NoProfile -ExecutionPolicy Bypass -File tools\dev\CompatMatrix.ps1 -CheckLog <Player.log> -Adapters ' + ($installedKeys -join ','))
}

# --------------------------------------------------------------------------------------------
# Remove
# --------------------------------------------------------------------------------------------
function Invoke-Remove {
    param($Rows)

    Write-Step 'REMOVE only the folders installed by this script'
    $marker = Read-Ownership
    if ($null -ne $marker.modsRoot -and [string]$marker.modsRoot -ne '') {
        $markerRoot = [System.IO.Path]::GetFullPath([string]$marker.modsRoot).TrimEnd('\', '/')
        $currentRoot = [System.IO.Path]::GetFullPath($ModsRoot).TrimEnd('\', '/')
        if ($markerRoot -ne $currentRoot) {
            Fail ('ownership marker belongs to a different Mods root: ' + $markerRoot + ' (current: ' + $currentRoot + ')')
        }
    }
    if ($null -eq $marker.Entries -or $marker.Entries.Count -eq 0) {
        Write-Info ('nothing to do: no ownership entries in ' + $MarkerPath)
        return
    }

    $selectedKeys = @()
    foreach ($row in $Rows) { $selectedKeys += $row.Key }
    $removed = New-Object System.Collections.Generic.List[string]
    $kept = New-Object System.Collections.Generic.List[object]

    foreach ($entry in $marker.Entries) {
        $key = [string]$entry.adapter
        if ($selectedKeys -notcontains $key) {
            $kept.Add($entry) | Out-Null
            continue
        }

        $dest = Assert-DestinationPath ([string]$entry.folder)
        if (-not (Test-Path -LiteralPath $dest -PathType Container)) {
            Write-Warn2 ('owned folder already absent: ' + $dest)
            $removed.Add($key) | Out-Null
            continue
        }

        if ($entry.treeSha256) {
            $now = Get-TreeInfo $dest $true
            if ($now.TreeSha256 -and $now.TreeSha256 -ne [string]$entry.treeSha256) {
                Write-Warn2 ('owned folder was modified since install; removing anyway: ' + $dest)
            }
        }

        Write-Info ('removing ' + $dest)
        Remove-Item -LiteralPath $dest -Recurse -Force
        Write-Ok ('removed ' + $key)
        $removed.Add($key) | Out-Null
    }

    if ($kept.Count -eq 0) {
        Remove-Item -LiteralPath $MarkerPath -Force
        Write-Ok ('ownership marker cleared: ' + $MarkerPath)
    }
    else {
        $marker | Add-Member -NotePropertyName entries -NotePropertyValue $kept.ToArray() -Force
        Write-Ownership $marker
        Write-Info ('ownership marker kept for still-installed rows: ' + (($kept | ForEach-Object { $_.adapter }) -join ', '))
    }

    Write-Info ''
    Write-Info ('Removed: ' + $(if ($removed.Count -gt 0) { $removed -join ', ' } else { '(none)' }))
    Write-Info ('Kept:    ' + $(if ($kept.Count -gt 0) { ($kept | ForEach-Object { $_.adapter }) -join ', ' } else { '(none)' }))
    Write-Info ('Backups remain under: ' + $BackupDir)
}

# --------------------------------------------------------------------------------------------
# Main
# --------------------------------------------------------------------------------------------
$selectedRows = Resolve-AdapterRows $Adapters
if (-not $NoDependencies) {
    $selectedRows = @($selectedRows) + @($DependencyTable)
}
$actions = 0
if ($Install) { $actions++ }
if ($Remove) { $actions++ }
if (-not [string]::IsNullOrEmpty($CheckLog)) { $actions++ }
if ($actions -gt 1) {
    Fail 'use only one of -Install, -Remove, -CheckLog per invocation'
}

Write-Host 'HDBF compatibility matrix helper (TC-02..TC-08 support)' -ForegroundColor Cyan

if (-not [string]::IsNullOrEmpty($CheckLog)) {
    if ($null -eq $Expect) { $Expect = $Adapters }
    Invoke-CheckLog $CheckLog
    exit 0
}

if ($Remove) {
    if ($DryRun) {
        $marker = Read-Ownership
        Write-Step 'DRY RUN - would remove only these owned folders'
        if ($null -eq $marker.Entries -or $marker.Entries.Count -eq 0) {
            Write-Info '(no ownership entries)'
        }
        else {
            foreach ($entry in $marker.Entries) {
                $dest = Join-Path $ModsRoot ([string]$entry.folder)
                Write-Info ('  ' + [string]$entry.adapter + ' -> ' + $dest + $(if (Test-Path -LiteralPath $dest) { ' (present)' } else { ' (already absent)' }))
            }
        }
        Write-Info 'No files were deleted.'
        exit 0
    }
    Invoke-Remove $selectedRows
    exit 0
}

if ($Install -and -not $DryRun) {
    Invoke-Install $selectedRows
    exit 0
}

# No action switch, -Install -DryRun, or -DryRun alone: preview only.
Invoke-Preview $selectedRows
exit 0
