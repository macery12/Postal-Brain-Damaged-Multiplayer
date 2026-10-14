[CmdletBinding()]
param([string]$GameDir = (Join-Path $PSScriptRoot '..\..\Game'))
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
if (Get-Process -Name 'POSTAL Brain Damaged' -ErrorAction SilentlyContinue) { throw 'Close the game before the combat probe.' }
$output = Join-Path $repo ('.local\combat-probe-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $output | Out-Null
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
$listener.Start(); $port = $listener.LocalEndpoint.Port; $listener.Stop()
$previousProbe = $env:PBDMP_COMBAT_PROBE; $previousOutput = $env:PBDMP_COMBAT_OUTPUT; $previousPort = $env:PBDMP_COMBAT_PORT
$env:PBDMP_COMBAT_PROBE = '1'; $env:PBDMP_COMBAT_OUTPUT = $output; $env:PBDMP_COMBAT_PORT = $port
# A Steam install started directly relaunches itself through Steam unless the app id is already in its environment.
$previousSteamApp = $env:SteamAppId
if (!$env:SteamAppId -and !(Test-Path -LiteralPath (Join-Path $GameDir 'steam_appid.txt'))) { $env:SteamAppId = '1359980' }
$ownedProcess = $null
try {
    $loaderLog = Join-Path $GameDir 'BepInEx\LogOutput.log'
    if (Test-Path -LiteralPath $loaderLog) { Move-Item -LiteralPath $loaderLog -Destination (Join-Path $output 'previous-loader.log') }
    $ownedProcess = Start-Process -FilePath (Join-Path $GameDir 'POSTAL Brain Damaged.exe') -WorkingDirectory $GameDir -WindowStyle Hidden -PassThru -ArgumentList '-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-logFile',('"' + (Join-Path $output 'unity.log') + '"')
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while (!(Test-Path -LiteralPath (Join-Path $output 'pass'))) {
        $ownedProcess.Refresh()
        if ($ownedProcess.HasExited) { throw "Combat probe exited: $($ownedProcess.ExitCode). Inspect $output" }
        if ($watch.Elapsed.TotalSeconds -gt 110) { throw "Combat probe timed out. Inspect $output" }
        if ((Test-Path -LiteralPath $loaderLog) -and (Get-Content -LiteralPath $loaderLog -Raw) -match 'PBDMP stopped after runtime error|Campaign session failed|Error loading \[PBDMP') { throw "Combat probe failed. Inspect $output and $loaderLog" }
        Start-Sleep -Seconds 1
    }
    if ((Get-Content -LiteralPath $loaderLog -Raw) -match 'Exception in IL2CPP-to-Managed trampoline') { throw "Native proxy teardown failed. Inspect $output" }
    Write-Host "Native P2 targeting/hit probe passed: $output"
} finally {
    if ($ownedProcess) { $ownedProcess.Refresh(); if (!$ownedProcess.HasExited) { Stop-Process -Id $ownedProcess.Id; $ownedProcess.WaitForExit(5000) | Out-Null } }
    $env:PBDMP_COMBAT_PROBE = $previousProbe; $env:PBDMP_COMBAT_OUTPUT = $previousOutput; $env:PBDMP_COMBAT_PORT = $previousPort
    $env:SteamAppId = $previousSteamApp
    if (Test-Path -LiteralPath (Join-Path $GameDir 'BepInEx\LogOutput.log')) { Copy-Item -LiteralPath (Join-Path $GameDir 'BepInEx\LogOutput.log') -Destination (Join-Path $output 'bepinex.log') }
    $output | Set-Content -LiteralPath (Join-Path $repo '.local\last-combat-probe.txt')
}
