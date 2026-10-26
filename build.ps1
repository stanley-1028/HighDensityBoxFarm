# HDBF build + package script
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1              # build, test, stage + release gates
#   powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -SkipTests   # build + stage (L1 gate skipped)
#   powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Install     # + deploy to Mods\HDBF
#   powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -GateOnly -GateStage <dir>
#                                                                              # re-audit an existing staged package only
#   powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -WorkRoot <dir>
#                                                                              # redirect build output (sandbox / CI dry-run)
#
# This repository is the *source workspace*. It must NOT be a valid mod folder itself:
# Timberborn recursively converts every *.blueprint.json / *.timbermesh / sprite below a mod
# directory, so sources, vanilla references and build output all live outside the deployed mod.
#   deploy folder : %USERPROFILE%\Documents\Timberborn\Mods\HDBF
#   build outputs : %USERPROFILE%\.hdbf\work (see Directory.Build.props; -WorkRoot overrides)
#   vanilla refs  : %USERPROFILE%\.hdbf\ref  (validator cross-checks, never shipped)
#
# REL-01 release gates (after staging, before install):
#   * G-08  staged package: exactly the five assemblies, valid PE files, no test/debug/cheat/dev
#           file names or config switches;
#   * G-08  0 Harmony (section 19.18.3-B5): the v1 patch source directory does not exist, the
#           staged HDBF.Integration.dll contains no Harmony token, no source file registers a
#           patch (harmonyPatchCount stays 0) and the shipped config exposes no patch switch;
#   * G-08  config schema 2: staged HDBF_Config.json has configSchemaVersion 2, the four required
#           sections, and none of the deleted v1 scan/irrigation/crop-visibility keys;
#   * G-05  compatibility report: exactly the 13 chapter-10.4 fields (harmonyPatchCount included,
#           always 0), cross-checked against tools/release/release-gates.json;
#   * G-09  localizations: enUS/zhCN/zhTW have identical key sets (missing = 0) and no empty text,
#           plus tools/validate RESULT: OK and the 5/5 mod-assembly assertion;
#   * G-01  L1 suite: "FAILED 0" and at least the manifest's minTests (P0/P1 unit evidence).
# Any failed check throws, so the script exits non-zero and stops before -Install can deploy.
#
# IMPORTANT: every msbuild invocation uses -m:1. On this machine parallel MSBuild
# fails project-reference restore silently (exit 1, zero messages). See docs/API_FACTS.md §6.

[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$TimberbornDir = 'C:\Program Files (x86)\Steam\steamapps\common\Timberborn',
    [string]$RefDir = '',
    [string]$WorkRoot = '',
    [switch]$SkipTests,
    [switch]$Install,
    [switch]$Clean,
    [switch]$GateOnly,
    [string]$GateStage = '',
    [string]$ReleaseManifest = ''
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$workRoot = if ($WorkRoot) { [System.IO.Path]::GetFullPath($WorkRoot) } else { Join-Path $env:USERPROFILE '.hdbf\work' }
$refRoot = if ($RefDir) { $RefDir } else { Join-Path $env:USERPROFILE '.hdbf\ref' }
$buildDir = Join-Path $workRoot 'stage'
$stageDir = Join-Path $buildDir 'HDBF'
$managedDir = Join-Path $TimberbornDir 'Timberborn_Data\Managed'
$installedModDir = Join-Path $env:USERPROFILE 'Documents\Timberborn\Mods\HDBF'
$releaseManifestPath = if ($ReleaseManifest) { [System.IO.Path]::GetFullPath($ReleaseManifest) } else { Join-Path $root 'tools\release\release-gates.json' }

# ---------------------------------------------------------------------------------------------
# helpers
# ---------------------------------------------------------------------------------------------

function Invoke-Dotnet {
    param([string[]]$Arguments, [switch]$Capture)
    Write-Host "> dotnet $($Arguments -join ' ')" -ForegroundColor DarkCyan

    # Native stderr redirected with 2>&1 must not be promoted to a terminating error; the exit
    # code is the contract. Restore the caller's preference afterwards.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = @(& dotnet @Arguments 2>&1 | ForEach-Object { "$_" })
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }

    foreach ($line in $output) {
        Write-Host $line
    }

    if ($exitCode -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $exitCode"
    }

    if ($Capture) {
        return ($output -join [Environment]::NewLine)
    }
}

function Read-TextFile {
    param([string]$Path)
    return [System.IO.File]::ReadAllText($Path)
}

function Read-BytesForTokenSearch {
    param([string]$Path)
    # Search both metadata string encodings:
    # * type/member names live in the #Strings heap as UTF-8 (ASCII decode keeps them);
    # * every .NET string literal / const string value is UTF-16, so decode the byte stream as
    #   UTF-16LE both at offset 0 and offset 1 (the #US heap entry may start unaligned).
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $ascii = [System.Text.Encoding]::ASCII.GetString($bytes)
    $unicodeEven = [System.Text.Encoding]::Unicode.GetString($bytes)
    $unicodeOdd = [System.Text.Encoding]::Unicode.GetString($bytes, 1, $bytes.Length - 1)
    return $ascii + "`n" + $unicodeEven + "`n" + $unicodeOdd
}

function Resolve-GateSource {
    param([string]$RelativePath)
    $path = Join-Path $root $RelativePath
    if (-not (Test-Path -LiteralPath $path)) {
        throw "release-gate source file is missing: $RelativePath"
    }

    return $path
}

function Test-TextContains {
    param([string]$Text, [string]$Needle)
    return $Text.IndexOf($Needle, [System.StringComparison]::Ordinal) -ge 0
}

function Get-OccurrenceCount {
    param([string]$Text, [string]$Needle)
    $count = 0
    $index = 0
    while ($true) {
        $index = $Text.IndexOf($Needle, $index, [System.StringComparison]::Ordinal)
        if ($index -lt 0) {
            return $count
        }

        $count++
        $index += $Needle.Length
    }
}


$script:gateChecks = 0
$script:gateFailures = New-Object System.Collections.Generic.List[string]

function Assert-ReleaseGate {
    param([bool]$Condition, [string]$GateId, [string]$Title, [string]$Detail = '')
    $script:gateChecks++
    if ($Condition) {
        Write-Host ("  PASS  [{0}] {1}" -f $GateId, $Title) -ForegroundColor Green
        return
    }

    $line = "[{0}] {1}" -f $GateId, $Title
    if ($Detail) {
        $line = $line + ' -- ' + $Detail
    }

    [void]$script:gateFailures.Add($line)
    Write-Host ("  FAIL  " + $line) -ForegroundColor Red
}

# ---------------------------------------------------------------------------------------------
# REL-01 release gates (staged package + 0-Harmony + config schema 2 + report + localization + L1)
# ---------------------------------------------------------------------------------------------

function Invoke-ReleaseGates {
    param(
        [string]$StagedDir,
        [string]$ManifestFile,
        [string]$ValidateLog = '',
        [string]$TestLog = '',
        [string]$BuildLog = ''
    )

    $script:gateChecks = 0
    $script:gateFailures = New-Object System.Collections.Generic.List[string]

    if (-not (Test-Path -LiteralPath $ManifestFile)) {
        throw "release gate manifest not found: $ManifestFile"
    }

    if (-not (Test-Path -LiteralPath $StagedDir)) {
        throw "staged package directory not found: $StagedDir"
    }

    $gate = Get-Content -LiteralPath $ManifestFile -Raw | ConvertFrom-Json

    Write-Host ''
    Write-Host '===== REL-01 release gates ===================================================' -ForegroundColor Cyan
    Write-Host ("  gate manifest  : {0}" -f (Resolve-Path -LiteralPath $ManifestFile).Path)
    Write-Host ("  staged package : {0}" -f (Resolve-Path -LiteralPath $StagedDir).Path)
    Write-Host ("  gate ids       : {0}" -f (@($gate.gateIds) -join ', '))

    $stagedRoot = (Resolve-Path -LiteralPath $StagedDir).Path
    $stagedFiles = @(Get-ChildItem -LiteralPath $stagedRoot -Recurse -File)

    # ---- staged package shape (G-08) -------------------------------------------------------
    Assert-ReleaseGate ($stagedFiles.Count -gt 0) 'G-08' 'staged package is not empty'

    $expectedDllNames = @(@($gate.requiredAssemblies) | ForEach-Object { $_.ToString() + '.dll' } | Sort-Object)
    $actualDllNames = @($stagedFiles | Where-Object { $_.Extension -eq '.dll' } | ForEach-Object { $_.Name } | Sort-Object)
    Assert-ReleaseGate (($actualDllNames -join '|') -eq ($expectedDllNames -join '|')) 'G-08' `
        'staged package contains exactly the five HDBF assemblies' `
        ("expected: " + ($expectedDllNames -join ', ') + '; actual: ' + ($actualDllNames -join ', '))

    $badPeFiles = @()
    foreach ($dll in @($stagedFiles | Where-Object { $_.Extension -eq '.dll' })) {
        $bytes = [System.IO.File]::ReadAllBytes($dll.FullName)
        $isPe = $bytes.Length -gt 2 -and $bytes[0] -eq 0x4D -and $bytes[1] -eq 0x5A
        if (-not $isPe) {
            $badPeFiles += $dll.Name
        }
    }
    Assert-ReleaseGate ($badPeFiles.Count -eq 0) 'G-08' 'every staged assembly is a Windows PE file' ($badPeFiles -join ', ')

    $badExtensions = @($stagedFiles |
        Where-Object { @($gate.allowedStagedExtensions) -notcontains $_.Extension } |
        ForEach-Object { $_.Name })
    Assert-ReleaseGate ($badExtensions.Count -eq 0) 'G-08' 'staged package uses only allowed file types (no pdb/xml/dev artefacts)' ($badExtensions -join ', ')

    $badPaths = @()
    foreach ($file in $stagedFiles) {
        $relative = $file.FullName.Substring($stagedRoot.Length).TrimStart('\', '/')
        foreach ($pattern in @($gate.forbiddenStagedPatterns)) {
            if ($relative -like [string]$pattern) {
                $badPaths += ($relative + ' ~ ' + [string]$pattern)
            }
        }
    }
    Assert-ReleaseGate ($badPaths.Count -eq 0) 'G-08' 'no test/debug/cheat/dev file names in the package' ($badPaths -join '; ')

    # ---- shipped config: schema 2, no cheat/debug/patch/v1 keys (G-08) ----------------------
    $configContract = $gate.configContract
    $stagedConfigPath = Join-Path $stagedRoot ([string]$configContract.stagedFile)
    $stagedConfig = $null
    if (Test-Path -LiteralPath $stagedConfigPath) {
        try {
            $stagedConfig = Get-Content -LiteralPath $stagedConfigPath -Raw | ConvertFrom-Json
        }
        catch {
            $stagedConfig = $null
        }
    }

    Assert-ReleaseGate ($null -ne $stagedConfig) 'G-08' 'staged package contains a readable HDBF_Config.json'
    if ($null -ne $stagedConfig) {
        $configKeySets = @(
            @($stagedConfig.PSObject.Properties.Name),
            @($stagedConfig.features.PSObject.Properties.Name),
            @($stagedConfig.perf.PSObject.Properties.Name),
            @($stagedConfig.ui.PSObject.Properties.Name)
        )

        $configBadKeys = @()
        foreach ($keySet in $configKeySets) {
            foreach ($forbidden in @($gate.forbiddenConfigKeys)) {
                if ($keySet -contains [string]$forbidden) {
                    $configBadKeys += [string]$forbidden
                }
            }

            foreach ($forbidden in @($configContract.forbiddenKeys)) {
                if ($keySet -contains [string]$forbidden) {
                    $configBadKeys += [string]$forbidden
                }
            }

            foreach ($forbidden in @($gate.harmony.forbiddenConfigKeys)) {
                if ($keySet -contains [string]$forbidden) {
                    $configBadKeys += [string]$forbidden
                }
            }
        }

        Assert-ReleaseGate ($configBadKeys.Count -eq 0) 'G-08' `
            'shipped HDBF_Config.json exposes no debug/cheat/dev or deleted v1 switch' `
            (($configBadKeys | Sort-Object -Unique) -join ', ')

        $schemaKey = [string]$configContract.schemaVersionKey
        $schemaValue = $stagedConfig.$schemaKey
        Assert-ReleaseGate ($schemaValue -is [int] -and $schemaValue -eq [int]$configContract.schemaVersion) 'G-08' `
            ('HDBF_Config.json declares config schema ' + [int]$configContract.schemaVersion) `
            ('found: ' + [string]$schemaValue)

        $missingSections = @()
        foreach ($section in @($configContract.requiredSections)) {
            if ($null -eq $stagedConfig.$section) {
                $missingSections += [string]$section
            }
        }

        Assert-ReleaseGate ($missingSections.Count -eq 0) 'G-08' `
            'HDBF_Config.json contains the required v2 sections' ($missingSections -join ', ')
    }

    # ---- 0 Harmony (G-08, section 19.18.3-B5) ----------------------------------------------
    $harmony = $gate.harmony
    $maxPatches = [int]$harmony.maxPatches
    Assert-ReleaseGate ($maxPatches -eq 0) 'G-08' `
        'section 19.18.3-B5: the release contract allows exactly 0 Harmony patches'

    $patchDirectory = Join-Path $root ([string]$harmony.forbiddenSourceDirectory)
    Assert-ReleaseGate (-not (Test-Path -LiteralPath $patchDirectory)) 'G-08' `
        ('the v1 patch source directory is deleted: ' + [string]$harmony.forbiddenSourceDirectory)

    $sourceRoot = Join-Path $root ([string]$harmony.sourceScanRoot)
    $sourceFiles = @()
    if (Test-Path -LiteralPath $sourceRoot) {
        $sourceFiles = @(Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -Filter '*.cs')
    }

    $sourceTokenHits = @()
    $patchCountHits = @()
    $patchCountPattern = [string]$harmony.nonzeroPatchCountPattern
    foreach ($file in $sourceFiles) {
        $text = Read-TextFile $file.FullName
        $relative = $file.FullName.Substring($root.Length).TrimStart('\', '/')
        foreach ($token in @($harmony.forbiddenSourceTokens)) {
            if (Test-TextContains $text ([string]$token)) {
                $sourceTokenHits += ($relative + ' contains "' + [string]$token + '"')
            }
        }

        if ($patchCountPattern -and [regex]::IsMatch($text, $patchCountPattern)) {
            $patchCountHits += $relative
        }
    }

    Assert-ReleaseGate ($sourceTokenHits.Count -eq 0) 'G-08' `
        'no source file references Harmony or the deleted patch session' ($sourceTokenHits -join '; ')
    Assert-ReleaseGate ($patchCountHits.Count -eq 0) 'G-08' `
        'no source file reports a non-zero harmonyPatchCount (the report always says 0)' ($patchCountHits -join ', ')

    $compatSource = Read-TextFile (Resolve-GateSource ([string]$harmony.compatReportSource))
    Assert-ReleaseGate ((Test-TextContains $compatSource ([string]$harmony.reportField)) -and
                        (Test-TextContains $compatSource 'public int HarmonyPatchCount')) 'G-08' `
        'the compatibility report keeps the harmonyPatchCount field (contract unchanged, value 0)'

    $configSource = Read-TextFile (Resolve-GateSource ([string]$harmony.configFile))
    $configSourceConfig = $null
    try {
        $configSourceConfig = $configSource | ConvertFrom-Json
    }
    catch {
        $configSourceConfig = $null
    }

    $patchKeyHits = @()
    if ($null -ne $configSourceConfig) {
        $sourceFeatureNames = @($configSourceConfig.features.PSObject.Properties.Name)
        foreach ($forbidden in @($harmony.forbiddenConfigKeys)) {
            if ($sourceFeatureNames -contains [string]$forbidden) {
                $patchKeyHits += [string]$forbidden
            }
        }
    }

    Assert-ReleaseGate ($patchKeyHits.Count -eq 0) 'G-08' `
        'the shipped data/HDBF_Config.json exposes no Harmony patch switch' ($patchKeyHits -join ', ')

    $integrationDll = Join-Path $stagedRoot ([string]$harmony.stagedAssembly)
    if (Test-Path -LiteralPath $integrationDll) {
        $integrationText = Read-BytesForTokenSearch $integrationDll
        $integrationHits = @()
        foreach ($token in @($harmony.forbiddenTokens)) {
            if (Test-TextContains $integrationText ([string]$token)) {
                $integrationHits += [string]$token
            }
        }

        Assert-ReleaseGate ($integrationHits.Count -eq 0) 'G-08' `
            ('staged ' + [string]$harmony.stagedAssembly + ' contains no Harmony/patch token') `
            ($integrationHits -join ', ')
    }
    else {
        Assert-ReleaseGate $false 'G-08' ('staged package is missing ' + [string]$harmony.stagedAssembly)
    }

    # ---- test seam confinement (G-08: no player-reachable test/cheat hook) ----------------
    if ($null -ne $gate.testSeams) {
        $seam = $gate.testSeams
        $seamType = [string]$seam.type
        $allowedDirectory = Join-Path $root ([string]$seam.allowedAssemblyDirectory)
        $seamReferences = @()
        Get-ChildItem -LiteralPath (Join-Path $root 'src') -Recurse -File -Filter '*.cs' | ForEach-Object {
            if ($_.FullName.StartsWith($allowedDirectory, [System.StringComparison]::OrdinalIgnoreCase)) {
                return
            }

            if (Test-TextContains (Read-TextFile $_.FullName) $seamType) {
                $seamReferences += $_.FullName.Substring($root.Length).TrimStart('\', '/')
            }
        }
        Assert-ReleaseGate ($seamReferences.Count -eq 0) 'G-08' `
            ('test seam ' + $seamType + ' is confined to ' + [string]$seam.allowedAssemblyDirectory +
             ' (no UI/Gameplay/command path)') ($seamReferences -join ', ')
    }

    # ---- compatibility report fields (G-05 / table 10-4) ----------------------------------
    $compat = $gate.compatReport
    $compatSource = Read-TextFile (Resolve-GateSource ([string]$compat.sourceFile))
    $expectedFields = @($compat.fields | ForEach-Object { [string]$_ })
    $serializedFields = @([regex]::Matches($compatSource, '\.Property\("([A-Za-z][A-Za-z0-9]*)"\)') |
        ForEach-Object { $_.Groups[1].Value })
    $fieldDiff = @(Compare-Object -ReferenceObject $expectedFields -DifferenceObject $serializedFields)
    $fieldDiffDetail = ($fieldDiff | ForEach-Object {
        [string]$_.InputObject + ' (' + [string]$_.SideIndicator + ')'
    }) -join ', '

    Assert-ReleaseGate ($expectedFields.Count -eq 13) 'G-05' 'the chapter-10.4 report field list has 13 fields'
    Assert-ReleaseGate ($serializedFields.Count -eq $expectedFields.Count -and $fieldDiff.Count -eq 0) 'G-05' `
        'compatibility report serializes exactly the chapter-10.4 fields' $fieldDiffDetail
    Assert-ReleaseGate (Test-TextContains $compatSource 'public string ToJson()') 'G-05' `
        'compatibility report still exports JSON for bug reports (REQ-SAFE-16)'

    # ---- localization key parity (G-09) ---------------------------------------------------
    $localizations = $gate.localizations
    $localizationDirectory = Join-Path $stagedRoot ([string]$localizations.stagedDirectory)
    $languages = @($localizations.languages | ForEach-Object { [string]$_ })
    $expectedHeader = [string]$localizations.requiredHeader
    $keyTables = @{}

    foreach ($language in $languages) {
        $languagePath = Join-Path $localizationDirectory ($language + '.csv')
        $exists = Test-Path -LiteralPath $languagePath
        Assert-ReleaseGate $exists 'G-09' ('staged Localizations/' + $language + '.csv exists')
        if (-not $exists) {
            continue
        }

        $headerLine = (Get-Content -LiteralPath $languagePath -TotalCount 1)
        Assert-ReleaseGate ((($headerLine -replace '\s', '') -eq $expectedHeader)) 'G-09' `
            ($language + '.csv header is ' + $expectedHeader + ' (the game rejects any other shape)')

        $rows = @(Import-Csv -LiteralPath $languagePath -Encoding UTF8)
        Assert-ReleaseGate ($rows.Count -gt 0) 'G-09' ($language + '.csv contains at least one key')

        $ids = @($rows | ForEach-Object { [string]$_.ID })
        $duplicates = @($ids | Group-Object | Where-Object { $_.Count -gt 1 } | ForEach-Object { $_.Name })
        Assert-ReleaseGate ($duplicates.Count -eq 0) 'G-09' ($language + '.csv has no duplicate keys') ($duplicates -join ', ')

        $emptyText = @($rows | Where-Object { [string]::IsNullOrWhiteSpace([string]$_.Text) } |
            ForEach-Object { [string]$_.ID })
        Assert-ReleaseGate ($emptyText.Count -eq 0) 'G-09' ($language + '.csv has no empty text') ($emptyText -join ', ')

        $keyTables[$language] = $ids
    }

    if ($keyTables.ContainsKey('enUS')) {
        $englishKeys = $keyTables['enUS']
        foreach ($language in $languages) {
            if ($language -eq 'enUS' -or -not $keyTables.ContainsKey($language)) {
                continue
            }

            $missing = @($englishKeys | Where-Object { $keyTables[$language] -notcontains $_ })
            $extra = @($keyTables[$language] | Where-Object { $englishKeys -notcontains $_ })
            Assert-ReleaseGate (($missing.Count -eq 0) -and ($extra.Count -eq 0)) 'G-09' `
                ($language + '.csv key set equals enUS (missing keys = 0)') `
                ('missing: ' + ($missing -join ', ') + '; extra: ' + ($extra -join ', '))
        }
    }

    if ($ValidateLog) {
        Assert-ReleaseGate (Test-TextContains $ValidateLog 'RESULT: OK') 'G-09' `
            'tools/validate finished with RESULT: OK (data/, localization and config clean)'
        Assert-ReleaseGate (-not (Test-TextContains $ValidateLog '[localization]')) 'G-09' `
            'tools/validate reports zero localization problems'
        Assert-ReleaseGate (Test-TextContains $ValidateLog '5/5 OK (expected 5)') 'G-08' `
            'tools/validate loaded 5/5 mod assemblies (assembly scope gate)'
    }

    # ---- L1 suite + compiler warnings (G-01 / build integrity) ----------------------------
    if ($BuildLog) {
        # The build log repeats each diagnostic in the trailing summary; count distinct codes.
        $warningLines = @([regex]::Matches($BuildLog, '(?i)\bwarning (CS|MSB|NU)\d+') |
            ForEach-Object { $_.Value } | Sort-Object -Unique)
        $errorLines = @([regex]::Matches($BuildLog, '(?i)\berror (CS|MSB|NU)\d+') |
            ForEach-Object { $_.Value } | Sort-Object -Unique)
        Assert-ReleaseGate (($warningLines.Count -eq 0) -and ($errorLines.Count -eq 0)) 'G-08' `
            'the five mod assemblies compiled with 0 warnings / 0 errors' `
            ('warnings=' + $warningLines.Count + ', errors=' + $errorLines.Count)
    }

    if ($TestLog) {
        $summary = [regex]::Match($TestLog, 'PASSED\s+(\d+)\s+FAILED\s+(\d+)')
        Assert-ReleaseGate ($summary.Success) 'G-01' 'L1 suite printed a PASSED/FAILED summary'
        if ($summary.Success) {
            $passed = [int]$summary.Groups[1].Value
            $failed = [int]$summary.Groups[2].Value
            Assert-ReleaseGate ($failed -eq 0) 'G-01' 'L1 suite has zero failures (P0 unit evidence)' ('failed=' + $failed)
            Assert-ReleaseGate ($passed -ge [int]$gate.l1.minTests) 'G-01' `
                ('L1 suite ran at least ' + [int]$gate.l1.minTests + ' tests') ('passed=' + $passed)
        }
    }

    # ---- verdict -------------------------------------------------------------------------
    Write-Host ''
    if ($script:gateFailures.Count -gt 0) {
        Write-Host ("RELEASE GATES: FAILED ({0} of {1} checks)" -f $script:gateFailures.Count, $script:gateChecks) -ForegroundColor Red
        foreach ($failure in $script:gateFailures) {
            Write-Host ('  FAIL ' + $failure) -ForegroundColor Red
        }

        Write-Host '================================================================================' -ForegroundColor Cyan
        throw ('release gates failed (' + $script:gateFailures.Count + '): ' + ($script:gateFailures -join ' | '))
    }

    Write-Host ("RELEASE GATES: PASSED ({0} checks)" -f $script:gateChecks) -ForegroundColor Green
    Write-Host '================================================================================' -ForegroundColor Cyan
}

# ---------------------------------------------------------------------------------------------
# main
# ---------------------------------------------------------------------------------------------

# Gate-only mode: re-audit an already staged package (CI / negative-control runs). No build, no
# test, no validate, no deployment.
if ($GateOnly) {
    if (-not $GateStage) {
        throw '-GateOnly requires -GateStage <staged package directory>.'
    }

    $gateStagePath = (Resolve-Path -LiteralPath $GateStage).Path
    Invoke-ReleaseGates -StagedDir $gateStagePath -ManifestFile $releaseManifestPath
    exit 0
}

# ---- workspace safety gate ---------------------------------------------------------------
# A mod directory is scanned recursively by the game, so build output must never appear here.
$strayDlls = @(Get-ChildItem $root -Recurse -File -Filter '*.dll' -ErrorAction SilentlyContinue |
    Where-Object { $_.DirectoryName -ne $root })
if ($strayDlls.Count -gt 0) {
    $strayDlls | ForEach-Object { Write-Host ("  stray DLL: " + $_.FullName) -ForegroundColor Red }
    throw "DLLs found below the workspace root. Build output belongs in $workRoot."
}

if (Test-Path (Join-Path $root 'manifest.json')) {
    $why = "This workspace is the source project, but it contains manifest.json at its root. " +
        "That makes Timberborn treat it as a mod and load its sources/references as assets. " +
        "Deploy with -Install (targets Mods\HDBF) and keep the workspace payload-free."
    throw $why
}

Write-Host 'Package safety gate: workspace has no manifest and no stray DLLs.' -ForegroundColor Green

if (-not (Test-Path $managedDir)) {
    throw "Timberborn assemblies not found at '$managedDir'. Pass -TimberbornDir <game folder>."
}

if ($Clean) {
    Remove-Item -Recurse -Force $workRoot -ErrorAction SilentlyContinue
    # Legacy in-tree artefacts from before the redirect (they would be loaded by the game).
    Get-ChildItem $root -Recurse -Directory -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -in @('obj', 'bin') } |
        ForEach-Object { Remove-Item -Recurse -Force $_.FullName }
}

$projects = @(
    'src\HDBF.Core\HDBF.Core.csproj',
    'src\HDBF.Persistence\HDBF.Persistence.csproj',
    'src\HDBF.Gameplay\HDBF.Gameplay.csproj',
    'src\HDBF.Integration\HDBF.Integration.csproj',
    'src\HDBF.UI\HDBF.UI.csproj'
)

$buildProperties = @(
    "-p:HdbfWorkRoot=$workRoot",
    "-p:TimberbornDir=$TimberbornDir"
)

Write-Host ''
Write-Host ("Build configuration: {0}; work root: {1}" -f $Configuration, $workRoot) -ForegroundColor DarkCyan

$buildLog = ''
foreach ($project in $projects) {
    $buildArguments = @('build', (Join-Path $root $project), '-c', $Configuration, '-m:1', '--nologo', '-v', 'q') + $buildProperties
    $buildLog += (Invoke-Dotnet -Arguments $buildArguments -Capture) + [Environment]::NewLine
}

$testLog = ''
if (-not $SkipTests) {
    $testArguments = @('run', '--project', (Join-Path $root 'tests\HDBF.Tests\HDBF.Tests.csproj'),
        '-c', $Configuration, '-m:1', '--nologo') + $buildProperties
    $testLog = Invoke-Dotnet -Arguments $testArguments -Capture
}
else {
    Write-Host 'WARNING: -SkipTests skips the L1 release-gate check; this is not a release build.' -ForegroundColor Yellow
}

# Data validation needs the built HDBF.Gameplay.dll (it checks our custom spec fields against
# the real game metadata), so it runs after the build loop and before staging.
$validateArguments = @('run', '--project', (Join-Path $root 'tools\validate\validate.csproj'),
    '-c', $Configuration, '-m:1', '--nologo') + $buildProperties + @(
    '--', (Join-Path $root 'data'), '--managed', $managedDir, '--ref', $refRoot,
    '--assemblies', (Join-Path $workRoot 'bin'))
$validateLog = Invoke-Dotnet -Arguments $validateArguments -Capture

# ---- stage a game-ready mod folder -------------------------------------------------------
if (Test-Path $stageDir) {
    Remove-Item -Recurse -Force $stageDir
}

New-Item -ItemType Directory -Force -Path $stageDir | Out-Null

$assemblies = @('HDBF.Core', 'HDBF.Persistence', 'HDBF.Gameplay', 'HDBF.Integration', 'HDBF.UI')
foreach ($assembly in $assemblies) {
    $dll = Join-Path $workRoot "bin\$assembly\$Configuration\netstandard2.1\$assembly.dll"
    if (-not (Test-Path $dll)) {
        throw "expected build output missing: $dll"
    }

    Copy-Item $dll $stageDir
}

# Data files: everything under data\ ships verbatim at the mod root (blueprints, localizations,
# manifest, config). The game discovers blueprint JSON by walking the mod directory.
Copy-Item (Join-Path $root 'data\*') $stageDir -Recurse -Force

# ---- sanity checks on the staged package -------------------------------------------------
$required = @('manifest.json', 'HDBF.Core.dll', 'HDBF.Gameplay.dll')
foreach ($file in $required) {
    if (-not (Test-Path (Join-Path $stageDir $file))) {
        throw "staged package is missing $file"
    }
}

$manifest = Get-Content (Join-Path $stageDir 'manifest.json') -Raw | ConvertFrom-Json
foreach ($field in @('Name', 'Version', 'Id', 'MinimumGameVersion')) {
    if (-not $manifest.$field) {
        throw "manifest.json is missing required field $field"
    }
}

$blueprints = @(Get-ChildItem $stageDir -Recurse -Filter '*.blueprint.json')
if ($blueprints.Count -lt 5) {
    throw "expected at least 5 blueprint files (4 buildings + template collection), found $($blueprints.Count)"
}

$localizationFiles = @(Get-ChildItem $stageDir -Recurse -Filter '*.csv')
if ($localizationFiles.Count -lt 2) {
    throw "expected enUS.csv and zhCN.csv localization files, found $($localizationFiles.Count)"
}

foreach ($languageFile in @('enUS.csv', 'zhCN.csv', 'zhTW.csv')) {
    if (-not (Get-ChildItem $stageDir -Recurse -Filter $languageFile)) {
        throw "staged package is missing Localizations/$languageFile"
    }
}

# ---- REL-01 release gates: must pass before anything can be deployed ----------------------
Invoke-ReleaseGates -StagedDir $stageDir -ManifestFile $releaseManifestPath `
    -ValidateLog $validateLog -TestLog $testLog -BuildLog $buildLog

$sizeBytes = (Get-ChildItem $stageDir -Recurse -File | Measure-Object -Sum Length).Sum
Write-Host ''
Write-Host ("Staged mod package: {0}" -f $stageDir) -ForegroundColor Green
Write-Host ("  assemblies : {0}" -f ($assemblies -join ', '))
Write-Host ("  blueprints : {0}" -f $blueprints.Count)
Write-Host ("  size       : {0:N0} KB" -f ($sizeBytes / 1KB))

# ---- optional install -------------------------------------------------------------------
if ($Install) {
    $target = $installedModDir
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Copy-Item (Join-Path $stageDir '*') $target -Recurse -Force
    Write-Host "Installed to $target" -ForegroundColor Green
    Write-Host 'Enable it in Timberborn -> Mods, then restart the game.' -ForegroundColor Yellow
}

Write-Host 'Build + release gates complete.' -ForegroundColor Green
