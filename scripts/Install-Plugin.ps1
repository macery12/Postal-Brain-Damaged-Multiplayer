[CmdletBinding()]
param([string]$GameDir = (Join-Path $PSScriptRoot '..\..\Game'))
$ErrorActionPreference = 'Stop'
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
if (Get-Process -Name 'POSTAL Brain Damaged' -ErrorAction SilentlyContinue) { throw 'Close the game before installing the plugin.' }
if (!(Test-Path -LiteralPath (Join-Path $GameDir 'BepInEx\interop\Assembly-CSharp.dll'))) { throw 'Launch the installed IL2CPP loader once first.' }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$source = Join-Path $repo 'artifacts\PBDMP\BepInEx\plugins\PBDMP'
$target = Join-Path $GameDir 'BepInEx\plugins\PBDMP'
$manifestPath = Join-Path $repo '.local\plugin-install.json'
if (!(Test-Path -LiteralPath (Join-Path $source 'PBDMP.dll'))) { throw 'Run scripts/Build.ps1 first.' }
# Updates preserve the original pre-PBDMP state in the first install manifest.
if (!(Test-Path -LiteralPath $manifestPath)) {
    $records = @()
    foreach ($name in 'PBDMP.dll','PBDMP.Network.dll') {
        $file = Join-Path $target $name
        $prior = $null
        if (Test-Path -LiteralPath $file) {
            $prior = Join-Path $repo ".local\plugin-backup\$name"
            New-Item -ItemType Directory -Force -Path (Split-Path $prior) | Out-Null
            Copy-Item -LiteralPath $file -Destination $prior
        }
        $records += [PSCustomObject]@{ Name=$name; Backup=$prior }
    }
    @{ GameDir=$GameDir; Files=$records } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.GameDir -ne $GameDir) { throw 'Manifest belongs to a different game folder.' }
New-Item -ItemType Directory -Force -Path $target | Out-Null
foreach ($record in $manifest.Files) {
    $file = Join-Path $source $record.Name
    $record | Add-Member -NotePropertyName Hash -NotePropertyValue (Get-FileHash -LiteralPath $file).Hash -Force
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath
foreach ($record in $manifest.Files) { Copy-Item -LiteralPath (Join-Path $source $record.Name) -Destination $target -Force }
Write-Host "Installed PBDMP: $target"
