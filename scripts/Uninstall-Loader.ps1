[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$manifestPath = Join-Path $PSScriptRoot '..\.local\loader-install.json'
$install = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$root = (Resolve-Path -LiteralPath $install.GameDir).Path
if (Get-Process -Name 'POSTAL Brain Damaged' -ErrorAction SilentlyContinue) { throw 'Close the game first.' }
foreach ($file in $install.Files) {
    $target = [IO.Path]::GetFullPath((Join-Path $root $file.Relative))
    if (!$target.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe manifest path.' }
    if (Test-Path -LiteralPath $target) {
        if ((Get-FileHash -LiteralPath $target).Hash -ne $file.Hash) { throw "File changed since installation; preserve and inspect it: $target" }
        if ($file.Backup) { Copy-Item -LiteralPath $file.Backup -Destination $target -Force }
        else { Remove-Item -LiteralPath $target }
    }
}
Remove-Item -LiteralPath $manifestPath
Write-Host 'Loader files restored/removed. Generated interop, config, and logs were retained. PBDMP plugin removal: scripts/Uninstall-Plugin.ps1.'
