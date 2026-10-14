[CmdletBinding()]
param([string]$GameDir=(Join-Path $PSScriptRoot '..\..\Game'))
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$levels=@('LEVEL_AD_Neighborhood','LEVEL_AD_Prison','LEVEL_AD_TheWall_Interior','LEVEL_AD_TheWall','LEVEL_AD_Wallmarkt',
    'LEVEL_Asylum_Area69','LEVEL_Asylum_Backyard','LEVEL_Asylum_CoronvirusDen','LEVEL_Asylum_Forest','LEVEL_Asylum_Hospital',
    'LEVEL_NLM_ComicCon','LEVEL_NLM_DeathStar','LEVEL_NLM_ElonDuskChambers','LEVEL_NLM_SozebTrain','LEVEL_NLM_VIPZone')
$output=Join-Path $repo ('.local\actor-campaign-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $output | Out-Null
foreach($level in $levels) {
    # Fresh processes keep unrelated episode NPC pools out of each map audit.
    & (Join-Path $PSScriptRoot 'Test-Travel.ps1') -GameDir $GameDir -Audit -AuditLevels $level
    $run=(Get-Content -LiteralPath (Join-Path $repo '.local\last-travel-test.txt')).Trim()
    Copy-Item -LiteralPath (Join-Path $run ('actors-'+$level+'.tsv')) -Destination $output
    $run | Add-Content -LiteralPath (Join-Path $output 'runs.txt')
}
$rows=Get-ChildItem -LiteralPath $output -Filter 'actors-*.tsv' | ForEach-Object {
    $level=$_.BaseName.Substring(7)
    foreach($line in Get-Content -LiteralPath $_.FullName) { "$level`t$line" }
}
$rows | Set-Content -LiteralPath (Join-Path $output 'campaign-actor-families.tsv')
Write-Host "All 15 campaign maps passed actor snapshot/catalogue audits: $output"
