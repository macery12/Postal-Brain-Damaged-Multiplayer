[CmdletBinding()]
param([string]$GameDir = (Join-Path $PSScriptRoot '..\..\Game'), [switch]$DisableMeshGuard,
    [ValidateRange(1280, 7680)][int]$Width = 1280, [ValidateRange(720, 4320)][int]$Height = 720, [switch]$CoopMenu)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
if (Get-Process -Name 'POSTAL Brain Damaged' -ErrorAction SilentlyContinue) { throw 'Close the game before the smoke test.' }
$logPath = Join-Path $GameDir 'BepInEx\LogOutput.log'
$output = Join-Path $repo '.local'
New-Item -ItemType Directory -Force -Path $output | Out-Null
# Move the previous log aside: a stale PASS line must never count before the new process truncates it.
if (Test-Path -LiteralPath $logPath) { Move-Item -LiteralPath $logPath -Destination (Join-Path $output 'previous-runtime.log') -Force }
$previousSmoke = $env:PBDMP_SMOKE_TEST
$previousOutput = $env:PBDMP_SMOKE_OUTPUT
$previousMeshGuard = $env:PBDMP_TEST_DISABLE_MESH_GUARD
$env:PBDMP_SMOKE_TEST = '1'
$previousCoopMenu = $env:PBDMP_COOP_MENU_TEST
$env:PBDMP_COOP_MENU_TEST = if ($CoopMenu) { '1' } else { $null }
# The co-op menu fixture seeds synthetic campaigns in its own ignored save root.
$previousSaveTest = $env:PBDMP_SAVE_TEST; $previousSaveRoot = $env:PBDMP_SAVE_ROOT
$coopSaves = Join-Path $output 'coop-menu-saves'
if ($CoopMenu) { if (Test-Path -LiteralPath $coopSaves) { Remove-Item -LiteralPath $coopSaves -Recurse -Force }; $env:PBDMP_SAVE_TEST = '1'; $env:PBDMP_SAVE_ROOT = $coopSaves }
$env:PBDMP_SMOKE_OUTPUT = $output
$env:PBDMP_TEST_DISABLE_MESH_GUARD = if ($DisableMeshGuard) { '1' } else { $null }
# A Steam install started directly relaunches itself through Steam unless the app id is already in its environment.
$previousSteamApp = $env:SteamAppId
if (!$env:SteamAppId -and !(Test-Path -LiteralPath (Join-Path $GameDir 'steam_appid.txt'))) { $env:SteamAppId = '1359980' }
$game = $null
try {
    $game = Start-Process -FilePath (Join-Path $GameDir 'POSTAL Brain Damaged.exe') -WorkingDirectory $GameDir -WindowStyle Hidden -PassThru -ArgumentList '-screen-fullscreen','0','-screen-width', $Width,'-screen-height', $Height,'-logFile', ('"' + (Join-Path $output 'runtime-unity.log') + '"')
    $game.Id | Set-Content -LiteralPath (Join-Path $output 'runtime.pid')
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $passed = $false
    while ($watch.Elapsed.TotalSeconds -lt $(if ($CoopMenu) { 300 } else { 90 })) {
        Start-Sleep -Seconds 1
        $game.Refresh()
        if ($game.HasExited) { throw "Game exited with code $($game.ExitCode)." }
        if (Test-Path -LiteralPath $logPath) {
            $log = Get-Content -LiteralPath $logPath -Raw
            if ($log -match 'PBDMP SMOKE FAIL') { throw "Runtime smoke failed. Inspect $logPath" }
            if ($log -match 'PBDMP SMOKE PASS:') { $passed = $true; break }
        }
    }
    if (!$passed) { throw "Runtime smoke timed out. Inspect $logPath" }
    # ScreenCapture saves at end of frame, after the PASS message.
    Start-Sleep -Seconds 2
    $unityLog = Get-Content -LiteralPath (Join-Path $output 'runtime-unity.log') -Raw
    if ($unityLog -match "Material '.*' with Shader '.*' doesn't have a texture property") { throw 'Runtime regression: material texture property error in Unity log.' }
    if ($unityLog -match 'Not allowed to access triangles/indices on mesh') { throw 'Runtime regression: unreadable mesh triangle access in Unity log.' }
    Write-Host 'Runtime smoke passed. Synthetic menu poses verify native UI/avatars; two-PC gameplay still requires manual testing.'
} finally {
    $env:PBDMP_SMOKE_TEST = $previousSmoke
    $env:PBDMP_COOP_MENU_TEST = $previousCoopMenu
    $env:PBDMP_SAVE_TEST = $previousSaveTest; $env:PBDMP_SAVE_ROOT = $previousSaveRoot
    $env:PBDMP_SMOKE_OUTPUT = $previousOutput
    $env:PBDMP_TEST_DISABLE_MESH_GUARD = $previousMeshGuard
    $env:SteamAppId = $previousSteamApp
    if ($game) {
        $game.Refresh()
        if (!$game.HasExited) { Stop-Process -Id $game.Id; $game.WaitForExit(5000) | Out-Null }
    }
    if (Test-Path -LiteralPath $logPath) { Copy-Item -LiteralPath $logPath -Destination (Join-Path $output 'runtime-bepinex.log') -Force }
}
