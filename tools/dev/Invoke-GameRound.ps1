# One automated in-game verification round for the HDBF mod (Windows, Steam install).
#
# Why this exists: on this machine the game only reaches a world load when it is started with
# SteamAppId/SteamGameId in the child environment plus steam_appid.txt, the "Mods changed" dialog
# is confirmed once, and the save is opened through -saveName/-settlementName (docs/VERIFICATION.md
# §14). This script turns that recipe into repeatable, reviewable steps; it never edits the player's
# real save (it autoloads a copy under Saves\HDBFTest) and it mutes/restores the system audio.
#
# Usage (each action is one shell call; keep launch/click/screenshot in the same call when possible):
#   .\tools\dev\Invoke-GameRound.ps1 -Action Prepare           # audio state saved + muted, appid file
#   .\tools\dev\Invoke-GameRound.ps1 -Action Launch            # start game, screenshot at Mods dialog
#   .\tools\dev\Invoke-GameRound.ps1 -Action ConfirmMods       # click the Mods dialog OK, wait for load
#   .\tools\dev\Invoke-GameRound.ps1 -Action SpeedUp -Seconds 120
#   .\tools\dev\Invoke-GameRound.ps1 -Action Stop              # kill our PID, restore audio, cleanup
#   .\tools\dev\Invoke-GameRound.ps1 -Action Status
#
# Every action prints a compact state line so the caller (Lead) can log it as evidence.

[CmdletBinding()]
param(
    [ValidateSet('Prepare', 'Fixture', 'Launch', 'Start', 'ConfirmMods', 'SpeedUp', 'Stop', 'Status')]
    [string]$Action = 'Status',
    [string]$TimberbornDir = 'C:\Program Files (x86)\Steam\steamapps\common\Timberborn',
    [string]$SaveName = '2026-10-24 18h28m, Day 1-1.autosave',
    [string]$SettlementName = 'HDBFTest',
    [string]$Tier = 'BoxFarm.Basic.Folktails',
    [int]$Seconds = 60,
    [string]$Tag = 'round',
    [double]$ModsOkX = 0.5,
    [double]$ModsOkY = 0.817,
    [double]$SpeedX = 0.952,
    [double]$SpeedY = 0.015
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)   # tools/dev -> repo root
$dev = $PSScriptRoot
. (Join-Path $dev 'GameUi.ps1')
. (Join-Path $dev 'GameAudio.ps1')

# State (pid/log/audio) lives in a *stable* directory: the harness gives every shell a different
# %TEMP%, and a timed-out tool call kills the shell, so %TEMP% state would be lost exactly when a
# cleanup (audio restore, log copy) is most needed.
$stateDir = Join-Path (Join-Path $env:USERPROFILE '.hdbf') 'round'
New-Item -ItemType Directory -Force -Path $stateDir | Out-Null
$pidFile = Join-Path $stateDir 'game.pid'
$logFile = Join-Path $stateDir 'Player.log'
$audioFile = Join-Path $stateDir 'audio.json'
$appIdFile = Join-Path $TimberbornDir 'steam_appid.txt'
$evidenceDir = Join-Path $root 'docs\in-game'

function Write-State([string]$text) {
    Write-Host ("[round] " + $text)
}

function Get-GameProcess {
    if (-not (Test-Path $pidFile)) { return $null }
    $saved = [int](Get-Content $pidFile -Raw)
    $process = Get-Process -Id $saved -ErrorAction SilentlyContinue
    if ($null -eq $process) { return $null }
    return $process
}

function Save-Screenshot([string]$name) {
    New-Item -ItemType Directory -Force -Path $evidenceDir | Out-Null
    $path = Join-Path $evidenceDir ($Tag + '-' + $name + '.png')
    Save-HdbfScreenshot $path | Out-Null
    Write-State ("screenshot: " + $path)
    return $path
}

function Get-LogTail([int]$count = 40) {
    if (-not (Test-Path $logFile)) { return @() }
    return Get-Content $logFile -Tail $count
}

function Write-HdbfLines([string]$label) {
    if (-not (Test-Path $logFile)) { Write-State ("$label : no log yet"); return }
    $lines = Select-String -Path $logFile -Pattern 'HDBF|Opening file:|Mods|Exception|error|Error' -SimpleMatch:$false |
        Select-Object -Last 30
    Write-State ("$label : log = $logFile ($((Get-Item $logFile).Length) B)")
    foreach ($line in $lines) { Write-Host ("  " + $line.Line) }
}

switch ($Action) {
    'Start' {
        # Do Fixture + Prepare + Launch in one call so no foreground change can happen between them.
        & $PSCommandPath -Action Fixture -Tag $Tag -SaveName $SaveName -SettlementName $SettlementName -TimberbornDir $TimberbornDir -Tier $Tier
        & $PSCommandPath -Action Prepare -Tag $Tag -SaveName $SaveName -SettlementName $SettlementName -TimberbornDir $TimberbornDir
        & $PSCommandPath -Action Launch -Tag $Tag -SaveName $SaveName -SettlementName $SettlementName -TimberbornDir $TimberbornDir
    }

    'Prepare' {
        $state = Get-HdbfAudioState
        if ($null -ne $state) {
            $state | ConvertTo-Json | Set-Content $audioFile -Encoding UTF8
            Write-State ("audio before: muted=$($state.Muted) volume=$($state.Volume)")
            Write-State ("audio muted: " + (Set-HdbfMute $true))
        } else {
            Write-State 'audio: no default render endpoint (no mute needed)'
        }

        # SteamAppId file is what stops Steamworks RestartAppIfNecessary from killing our launch.
        if (Test-Path $appIdFile) {
            Copy-Item $appIdFile (Join-Path $stateDir 'steam_appid.txt.saved') -Force
        }
        Set-Content -Path $appIdFile -Value '1062090' -Encoding ASCII -NoNewline
        Write-State "appid file: $appIdFile = 1062090"

        $fixture = Join-Path ($env:USERPROFILE) ("Documents\Timberborn\Saves\" + $SettlementName)
        $fixtureSave = Join-Path $fixture ($SaveName + '.timber')
        if (-not (Test-Path $fixtureSave)) {
            Write-State "fixture save missing: $fixtureSave"
        } else {
            Write-State ("fixture save present: " + $fixtureSave + " (" + (Get-Item $fixtureSave).Length + " B)")
        }
    }

    'Fixture' {
        # Rebuild the fixture from the pristine source save (the game may autosave over the copy).
        $craft = Join-Path $dev 'fixtures\craft-farm-fixture.py'
        if (-not (Test-Path $craft)) { throw "fixture script missing: $craft" }
        $outDir = Join-Path ($env:USERPROFILE) ("Documents\Timberborn\Saves\" + $SettlementName)
        & python $craft --out $outDir --tier $Tier
        if ($LASTEXITCODE -ne 0) { throw "fixture crafting failed with exit code $LASTEXITCODE" }
    }

    'Launch' {
        $existing = Get-Process -Name Timberborn -ErrorAction SilentlyContinue
        if ($existing) { throw "Timberborn already running (pid $($existing.Id)); stop it first" }
        if (-not (Test-Path (Join-Path $TimberbornDir 'Timberborn.exe'))) { throw "game not found: $TimberbornDir" }
        if (Test-Path $logFile) { Remove-Item $logFile -Force }

        $env:SteamAppId = '1062090'
        $env:SteamGameId = '1062090'
        # -skipModManager: IL-verified (Timberborn.ModManagerSceneUI.ModManagerScenePanel.cctor
        # SkipModManagerKey = "skipModManager"; ShouldSkipModManager() = CommandLineArguments.Has(...))
        # -> the first-load "Mods" dialog is bypassed and LoadModsAndStartGame() runs directly.
        # This is what makes the fully unattended round possible (no synthetic input needed).
        $arguments = @(
            '-skipModManager',
            '-saveName', ('"' + $SaveName + '"'),
            '-settlementName', ('"' + $SettlementName + '"'),
            '-logFile', ('"' + $logFile + '"')
        )
        $process = Start-Process -FilePath (Join-Path $TimberbornDir 'Timberborn.exe') `
            -ArgumentList $arguments -PassThru
        $process.Id | Set-Content $pidFile -Encoding ASCII
        Write-State "launched pid=$($process.Id) save='$SaveName' settlement=$SettlementName"

        # Window + first-load Mods dialog: give it ~22 s, then capture what is actually on screen.
        $deadline = (Get-Date).AddSeconds(45)
        $game = $null
        while ((Get-Date) -lt $deadline) {
            $game = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
            if ($null -eq $game) { break }
            if ($game.MainWindowHandle -ne [IntPtr]::Zero) { break }
            Start-Sleep -Milliseconds 500
        }
        if ($null -eq $game) { throw "game exited during startup; see $logFile" }
        Start-Sleep -Seconds 6
        Focus-HdbfGame $game | Out-Null
        Save-Screenshot 'pre' | Out-Null
        Write-HdbfLines 'launch'
    }

    'ConfirmMods' {
        $game = Get-GameProcess
        if ($null -eq $game) { throw 'game is not running' }

        # The game's first-load "Mods" dialog is a plain Unity button. Clicking it can fail when the
        # window did not receive a mouse-move first or the foreground flips back, so this tries a
        # small grid around the button (and Enter as the dialog's default action) and stops as soon
        # as the log shows the save being opened.
        $markerPattern = 'gameplay\.entity|Load time:|Opening file:'
        function Get-LoadMarker {
            if (-not (Test-Path $logFile)) { return $null }
            return (Select-String -Path $logFile -Pattern $markerPattern | Select-Object -Last 1)
        }

        if (Get-LoadMarker) {
            Write-State 'save already loading; nothing to confirm'
        } else {
            $points = @(
                @($ModsOkX, $ModsOkY),
                @(0.5, 0.800), @(0.5, 0.835),
                @(0.470, 0.817), @(0.530, 0.817),
                @(0.5, 0.817)
            )
            $confirmed = $false
            foreach ($point in $points) {
                Focus-HdbfGame $game | Out-Null
                Start-Sleep -Milliseconds 500

                # Hover: move the cursor onto the button first, then click (mouse_event uses the
                # current cursor position; this mirrors a real user's move-then-click).
                [HdbfGameUi]::ClickFraction($game.MainWindowHandle, $point[0], $point[1]) | Out-Null
                Start-Sleep -Milliseconds 900
                Click-HdbfFraction $game $point[0] $point[1] | Out-Null
                Start-Sleep -Milliseconds 600
                Send-HdbfKey 0x0D | Out-Null   # Enter = default dialog button
                Start-Sleep -Seconds 3

                if (Get-LoadMarker) { $confirmed = $true; Write-State ("mods confirmed at (" + $point[0] + "," + $point[1] + ")"); break }
                Write-State ("no load yet after click at (" + $point[0] + "," + $point[1] + ")")
            }
            if (-not $confirmed) {
                Write-State 'WARNING: could not confirm the Mods dialog; capturing evidence only'
            }
        }

        $waited = 0
        while ($waited -lt $Seconds) {
            $marker = Get-LoadMarker
            if ($marker -and (Select-String -Path $logFile -Pattern 'gameplay\.entity|Load time:' | Select-Object -Last 1)) { break }
            Start-Sleep -Seconds 5
            $waited += 5
        }
        Start-Sleep -Seconds 4
        Focus-HdbfGame $game | Out-Null
        Save-Screenshot 'loaded' | Out-Null
        Write-HdbfLines 'confirm'
    }

    'SpeedUp' {
        $game = Get-GameProcess
        if ($null -eq $game) { throw 'game is not running' }
        Focus-HdbfGame $game | Out-Null
        Start-Sleep -Milliseconds 500
        Click-HdbfFraction $game $SpeedX $SpeedY | Out-Null
        Write-State "clicked fastest speed at ($SpeedX,$SpeedY); running ${Seconds}s"
        $slices = [Math]::Max(1, [int]($Seconds / 6))
        for ($i = 0; $i -lt $slices; $i++) {
            Start-Sleep -Seconds 6
            # Re-focus periodically: the DSH window can steal the foreground between actions.
            $live = Get-GameProcess
            if ($null -eq $live) { Write-State 'game exited during the run'; break }
            if (($i % 5) -eq 4) { Focus-HdbfGame $live | Out-Null }
        }
        $live = Get-GameProcess
        if ($null -ne $live) { Focus-HdbfGame $live | Out-Null; Start-Sleep -Milliseconds 700 }
        Save-Screenshot 'run' | Out-Null
        Write-HdbfLines 'run'
    }

    'Stop' {
        $game = Get-GameProcess
        if ($null -ne $game) {
            Stop-Process -Id $game.Id -Force -ErrorAction SilentlyContinue
            Write-State "stopped pid=$($game.Id)"
        } else {
            Write-State 'game not running (nothing to stop)'
        }
        Remove-Item $pidFile -ErrorAction SilentlyContinue

        if (Test-Path $audioFile) {
            $saved = Get-Content $audioFile -Raw | ConvertFrom-Json
            $restored = Restore-HdbfAudioState $saved
            Write-State "audio restored: $restored (muted=$($saved.Muted) volume=$($saved.Volume))"
            Remove-Item $audioFile -Force -ErrorAction SilentlyContinue
        }

        $savedAppId = Join-Path $stateDir 'steam_appid.txt.saved'
        if (Test-Path $savedAppId) {
            Copy-Item $savedAppId $appIdFile -Force
            Remove-Item $savedAppId -Force
            Write-State 'appid file restored'
        } elseif (Test-Path $appIdFile) {
            Remove-Item $appIdFile -Force
            Write-State 'appid file removed (was absent before the round)'
        }

        if (Test-Path $logFile) {
            $copy = Join-Path $evidenceDir ($Tag + '-Player.log')
            New-Item -ItemType Directory -Force -Path $evidenceDir | Out-Null
            Copy-Item $logFile $copy -Force
            Write-State "log copied: $copy"

            # Machine-readable round verdict: the exact lines a verification round must look at,
            # plus a small summary that is easy to paste into docs/VERIFICATION.md.
            $interesting = Select-String -Path $logFile -Pattern 'HDBF/|diag\.|farm loop fault|Opening file:|Load time:|Exception' |
                ForEach-Object { $_.Line }
            $summary = Join-Path $evidenceDir ($Tag + '-summary.txt')
            $header = @(
                ("round        : " + $Tag),
                ("finished     : " + (Get-Date).ToString('s')),
                ("save         : " + $SaveName),
                ("settlement   : " + $SettlementName),
                ("log          : " + $copy),
                ""
            )
            $header + $interesting | Set-Content -Path $summary -Encoding UTF8
            Write-State "summary: $summary ($($interesting.Count) interesting lines)"
            Write-HdbfLines 'stop'
        }
    }

    'Status' {
        $game = Get-GameProcess
        Write-State ("game running: " + ($null -ne $game) + $(if ($game) { " pid=$($game.Id)" } else { '' }))
        if (Test-Path $logFile) {
            Write-State ("log: $logFile ($((Get-Item $logFile).Length) B, last write $((Get-Item $logFile).LastWriteTime))")
            Get-LogTail 12 | ForEach-Object { Write-Host ("  " + $_) }
        }
        Write-State ("fixture: " + (Join-Path ($env:USERPROFILE) "Documents\Timberborn\Saves\$SettlementName"))
    }
}
