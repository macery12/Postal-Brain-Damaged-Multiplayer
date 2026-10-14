[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$manifestPath = Join-Path $PSScriptRoot '..\.local\plugin-install.json'
$install = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$root = (Resolve-Path -LiteralPath (Join-Path $install.GameDir 'BepInEx\plugins\PBDMP')).Path
if (Get-Process -Name 'POSTAL Brain Damaged' -ErrorAction SilentlyContinue) { throw 'Close the game first.' }
foreach ($file in $install.Files) {
    $target = [IO.Path]::GetFullPath((Join-Path $root $file.Name))
    if (!$target.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe manifest path.' }
    if (Test-Path -LiteralPath $target) {
        if ((Get-FileHash -LiteralPath $target).Hash -ne $file.Hash) { throw "File changed since installation: $target" }
        if ($file.Backup) { Copy-Item -LiteralPath $file.Backup -Destination $target -Force }
        else { Remove-Item -LiteralPath $target }
    }
}
Remove-Item -LiteralPath $manifestPath
Write-Host 'PBDMP plugin removed/restored. Config and logs retained.'
