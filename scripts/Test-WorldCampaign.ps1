[CmdletBinding()]
param([string]$GameDir=(Join-Path $PSScriptRoot '..\..\Game'),[switch]$WithPeer,[string[]]$Levels)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$levels=@('LEVEL_AD_Neighborhood','LEVEL_AD_Prison','LEVEL_AD_TheWall_Interior','LEVEL_AD_TheWall','LEVEL_AD_Wallmarkt',
    'LEVEL_Asylum_Area69','LEVEL_Asylum_Backyard','LEVEL_Asylum_CoronvirusDen','LEVEL_Asylum_Forest','LEVEL_Asylum_Hospital',
    'LEVEL_NLM_ComicCon','LEVEL_NLM_DeathStar','LEVEL_NLM_ElonDuskChambers','LEVEL_NLM_SozebTrain','LEVEL_NLM_VIPZone')
$output=Join-Path $repo ('.local\world-campaign-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $output | Out-Null
if($Levels) {
    foreach($requested in $Levels) {if($requested -notin $levels) {throw "Unknown campaign map: $requested"}}
    $levels=@($Levels | Select-Object -Unique)
}
foreach($level in $levels) {
    # Native pools from unrelated maps must never be reused by a census run.
    & (Join-Path $PSScriptRoot 'Test-Travel.ps1') -GameDir $GameDir -Audit -AuditLevels $level
    $run=(Get-Content -LiteralPath (Join-Path $repo '.local\last-travel-test.txt')).Trim()
    foreach($report in @('coverage','census','missions','sequences')) {
        Copy-Item -LiteralPath (Join-Path $run ($report+'-'+$level+'.tsv')) -Destination $output
    }
    "$level`taudit`t$run" | Add-Content -LiteralPath (Join-Path $output 'runs.tsv')
    if($WithPeer) {
        & (Join-Path $PSScriptRoot 'Test-Travel.ps1') -GameDir $GameDir -JoinInput -Level $level
        $peer=(Get-Content -LiteralPath (Join-Path $repo '.local\last-travel-test.txt')).Trim()
        "$level`tpeer`t$peer" | Add-Content -LiteralPath (Join-Path $output 'runs.tsv')
    }
}
$output | Set-Content -LiteralPath (Join-Path $repo '.local\last-world-campaign-test.txt')
Write-Host "All $($levels.Count) requested campaign world catalogues and bounded snapshots passed: $output"
