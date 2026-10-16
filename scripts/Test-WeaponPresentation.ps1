[CmdletBinding()]
param([Parameter(Mandatory)][string]$GameDir,[string]$Label='capture')
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$GameDir=(Resolve-Path -LiteralPath $GameDir).Path
if(Get-Process -Name 'POSTAL Brain Damaged' -ErrorAction SilentlyContinue){throw 'Close POSTAL before the isolated weapon capture.'}
$output=Join-Path $repo ('.local/weapon-'+$Label+'-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $output | Out-Null
$names=@('PBDMP_POSE_CAPTURE','PBDMP_SMOKE_OUTPUT','SteamAppId')
$previous=@{};foreach($name in $names){$previous[$name]=[Environment]::GetEnvironmentVariable($name)}
$env:PBDMP_POSE_CAPTURE='1';$env:PBDMP_SMOKE_OUTPUT=$output;$env:SteamAppId='1359980'
$game=$null
$loaderLog=Join-Path $GameDir 'BepInEx/LogOutput.log'
if(Test-Path -LiteralPath $loaderLog){Move-Item -LiteralPath $loaderLog -Destination (Join-Path $output 'previous-bepinex.log')}
try {
    $game=Start-Process -FilePath (Join-Path $GameDir 'POSTAL Brain Damaged.exe') -WorkingDirectory $GameDir -WindowStyle Hidden -PassThru -ArgumentList '-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-logFile',('"'+(Join-Path $output 'unity.log')+'"')
    $watch=[Diagnostics.Stopwatch]::StartNew();$passed=$false
    while($watch.Elapsed.TotalSeconds -lt 220){
        Start-Sleep -Milliseconds 800;$game.Refresh()
        if($game.HasExited){throw ('Game exited: '+$game.ExitCode)}
        $path=Join-Path $GameDir 'BepInEx/LogOutput.log'
        if(Test-Path -LiteralPath $path){
            $log=Get-Content -LiteralPath $path -Raw
            if($log -match 'PBDMP stopped after runtime error|PBDMP failed|PBDMP POSE CAPTURE FAIL'){throw ('Weapon capture failed; see '+$path)}
            if((Test-Path -LiteralPath (Join-Path $output 'weapon-assets.txt')) -and $log -match 'PBDMP POSE CAPTURE PASS'){$passed=$true;break}
        }
    }
    if(!$passed){throw 'Weapon capture timed out.'}
    Write-Host ('Weapon capture passed: '+$output)
} finally {
    if($game){$game.Refresh();if(!$game.HasExited){Stop-Process -Id $game.Id;$game.WaitForExit(5000)|Out-Null}}
    foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$previous[$name])}
    $path=Join-Path $GameDir 'BepInEx/LogOutput.log'
    if(Test-Path -LiteralPath $path){Copy-Item -LiteralPath $path -Destination (Join-Path $output 'bepinex.log')}
}
