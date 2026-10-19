[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GameDir,
    # Optional subset of discovered base HORDE level asset names; unknown names select nothing.
    [string[]]$Levels,
    # Arena that receives the native solo phase/offer/end observation; defaults to the first discovered arena.
    [string]$Observe,
    [ValidateRange(120,3600)][int]$TimeoutSeconds=900
)
$ErrorActionPreference='Stop'
$GameDir=(Resolve-Path -LiteralPath $GameDir).Path
if(Get-Process -Name 'POSTAL Brain Damaged' -ErrorAction SilentlyContinue) {throw 'Close the game before the Survival inventory.'}
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output=Join-Path $repo ('.local/survival-inventory-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $output | Out-Null
$logPath=Join-Path $GameDir 'BepInEx/LogOutput.log'
if(Test-Path -LiteralPath $logPath) {Move-Item -LiteralPath $logPath -Destination (Join-Path $output 'previous-runtime.log')}
$previous=@{}
foreach($name in 'PBDMP_SURVIVAL_INVENTORY','PBDMP_SURVIVAL_OUTPUT','PBDMP_SURVIVAL_LEVELS','PBDMP_SURVIVAL_OBSERVE','SteamAppId') {$previous[$name]=[Environment]::GetEnvironmentVariable($name)}
$env:PBDMP_SURVIVAL_INVENTORY='1'
$env:PBDMP_SURVIVAL_OUTPUT=$output
$env:PBDMP_SURVIVAL_LEVELS=if($Levels){$Levels -join ','}else{$null}
$env:PBDMP_SURVIVAL_OBSERVE=if($Observe){$Observe}else{$null}
# A Steam install started directly relaunches itself through Steam unless the app id is already in its environment.
if(!$env:SteamAppId -and !(Test-Path -LiteralPath (Join-Path $GameDir 'steam_appid.txt'))) {$env:SteamAppId='1359980'}
$game=$null
try {
    $game=Start-Process -FilePath (Join-Path $GameDir 'POSTAL Brain Damaged.exe') -WorkingDirectory $GameDir -WindowStyle Hidden -PassThru -ArgumentList '-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-logFile',('"'+(Join-Path $output 'unity.log')+'"')
    $watch=[Diagnostics.Stopwatch]::StartNew()
    $passed=$false
    while($watch.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        Start-Sleep -Seconds 2
        $game.Refresh()
        if($game.HasExited) {throw "Game exited with code $($game.ExitCode). Inspect $output"}
        if(!(Test-Path -LiteralPath $logPath)) {continue}
        $log=Get-Content -LiteralPath $logPath -Raw -Encoding UTF8
        if($log -match '\[Error\s+:\s+PBDMP\]') {throw "Survival inventory runtime error. Inspect $output"}
        if($log -match 'PBDMP SURVIVAL INVENTORY PASS:') {$passed=$true;break}
    }
    if(!$passed) {throw "Survival inventory timed out. Inspect $output"}
    $output | Set-Content -LiteralPath (Join-Path $repo '.local/last-survival-inventory.txt') -Encoding UTF8
    Write-Host "Survival inventory passed: $output"
} finally {
    foreach($name in $previous.Keys) {[Environment]::SetEnvironmentVariable($name,$previous[$name])}
    if($game) {$game.Refresh();if(!$game.HasExited) {Stop-Process -Id $game.Id;$game.WaitForExit(5000) | Out-Null}}
    if(Test-Path -LiteralPath $logPath) {Copy-Item -LiteralPath $logPath -Destination (Join-Path $output 'bepinex.log')}
}
