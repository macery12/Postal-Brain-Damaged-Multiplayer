[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GameDir,
    [ValidateRange(1280,7680)][int]$Width=1920,
    [ValidateRange(720,4320)][int]$Height=1080
)
$ErrorActionPreference='Stop'
$GameDir=(Resolve-Path -LiteralPath $GameDir).Path
if(Get-Process -Name 'POSTAL Brain Damaged' -ErrorAction SilentlyContinue) {throw 'Close the game before the news-panel test.'}
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output=Join-Path $repo ('.local/news-{0}x{1}-{2}' -f $Width,$Height,(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $output | Out-Null
$logPath=Join-Path $GameDir 'BepInEx/LogOutput.log'
if(Test-Path -LiteralPath $logPath) {Move-Item -LiteralPath $logPath -Destination (Join-Path $output 'previous-runtime.log')}
$previousTest=$env:PBDMP_NEWS_TEST
$previousOutput=$env:PBDMP_NEWS_OUTPUT
$previousSteam=$env:SteamAppId
$env:PBDMP_NEWS_TEST='1'
$env:PBDMP_NEWS_OUTPUT=$output
$env:SteamAppId='1359980'
$game=$null
try {
    $game=Start-Process -FilePath (Join-Path $GameDir 'POSTAL Brain Damaged.exe') -WorkingDirectory $GameDir -WindowStyle Hidden -PassThru -ArgumentList '-screen-fullscreen','0','-screen-width',$Width,'-screen-height',$Height,'-logFile',('"'+(Join-Path $output 'unity.log')+'"')
    $watch=[Diagnostics.Stopwatch]::StartNew()
    $passed=$false
    while($watch.Elapsed.TotalSeconds -lt 110) {
        Start-Sleep -Seconds 1
        $game.Refresh()
        if($game.HasExited) {throw "Game exited with code $($game.ExitCode)."}
        if(!(Test-Path -LiteralPath $logPath)) {continue}
        $log=Get-Content -LiteralPath $logPath -Raw
        if($log -match '\[Error\s+:\s+PBDMP\]') {throw "News-panel runtime error. Inspect $output"}
        if($log -match 'PBDMP NEWS PASS:') {$passed=$true;break}
    }
    if(!$passed) {throw "News-panel test timed out. Inspect $output"}
    Write-Host "News panel passed at ${Width}x${Height}: $output"
} finally {
    $env:PBDMP_NEWS_TEST=$previousTest
    $env:PBDMP_NEWS_OUTPUT=$previousOutput
    $env:SteamAppId=$previousSteam
    if($game) {$game.Refresh();if(!$game.HasExited) {Stop-Process -Id $game.Id;$game.WaitForExit(5000) | Out-Null}}
    if(Test-Path -LiteralPath $logPath) {Copy-Item -LiteralPath $logPath -Destination (Join-Path $output 'bepinex.log')}
}
