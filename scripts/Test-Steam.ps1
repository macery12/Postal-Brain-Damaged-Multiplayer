[CmdletBinding()]
param([string]$GameDir = (Join-Path $PSScriptRoot '..\..\Game'))
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
if (Get-Process -Name 'POSTAL Brain Damaged' -ErrorAction SilentlyContinue) { throw 'Close the game before the Steam probe.' }
if (!(Get-Process -Name 'steam' -ErrorAction SilentlyContinue)) { throw 'Start the Steam client and sign in to an account that owns the game.' }
$logPath = Join-Path $GameDir 'BepInEx\LogOutput.log'
$output = Join-Path $repo ('.local\steam-probe-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $output | Out-Null
if (Test-Path -LiteralPath $logPath) { Move-Item -LiteralPath $logPath -Destination (Join-Path $output 'previous-loader.log') }
$previousProbe = $env:PBDMP_STEAM_PROBE; $previousOutput = $env:PBDMP_SMOKE_OUTPUT
$env:PBDMP_STEAM_PROBE = '1'; $env:PBDMP_SMOKE_OUTPUT = $output
# A Steam install started directly relaunches itself through Steam unless the app id is already in its environment.
$previousSteamApp = $env:SteamAppId
if (!$env:SteamAppId -and !(Test-Path -LiteralPath (Join-Path $GameDir 'steam_appid.txt'))) { $env:SteamAppId = '1359980' }
$game = $null
try {
    $game = Start-Process -FilePath (Join-Path $GameDir 'POSTAL Brain Damaged.exe') -WorkingDirectory $GameDir -WindowStyle Hidden -PassThru -ArgumentList '-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-logFile',('"' + (Join-Path $output 'unity.log') + '"')
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $passed = $false
    while ($watch.Elapsed.TotalSeconds -lt 150) {
        Start-Sleep -Seconds 1
        $game.Refresh()
        if ($game.HasExited) { throw "Game exited with code $($game.ExitCode). Inspect $output" }
        if (Test-Path -LiteralPath $logPath) {
            $log = Get-Content -LiteralPath $logPath -Raw
            if ($log -match 'PBDMP stopped after runtime error|Error loading \[PBDMP|Steam callback tap:') { throw "Steam probe failed. Inspect $logPath" }
            if ($log -match 'PBDMP STEAM PROBE PASS:') { $passed = $true; break }
        }
    }
    if (!$passed) { throw "Steam probe timed out. Inspect $logPath" }
    Start-Sleep -Seconds 2
    $unityLog = Get-Content -LiteralPath (Join-Path $output 'unity.log') -Raw
    if ($unityLog -match '\[Steamworks\.NET\].*(Exception|Error)') { throw 'The game reported a Steamworks error while the tap was installed.' }
    Write-Host "Steam probe passed: $output. Two-account relay joining still needs a second player."
} finally {
    $env:PBDMP_STEAM_PROBE = $previousProbe; $env:PBDMP_SMOKE_OUTPUT = $previousOutput
    $env:SteamAppId = $previousSteamApp
    if ($game) {
        $game.Refresh()
        if (!$game.HasExited) { Stop-Process -Id $game.Id; $game.WaitForExit(5000) | Out-Null }
    }
    if (Test-Path -LiteralPath $logPath) { Copy-Item -LiteralPath $logPath -Destination (Join-Path $output 'bepinex.log') -Force }
}
