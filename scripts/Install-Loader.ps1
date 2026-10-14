[CmdletBinding()]
param([string]$GameDir = (Join-Path $PSScriptRoot '..\..\Game'))
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
if (!(Test-Path -LiteralPath (Join-Path $GameDir 'GameAssembly.dll'))) { throw 'Expected an IL2CPP game directory.' }
if (Get-Process -Name 'POSTAL Brain Damaged' -ErrorAction SilentlyContinue) { throw 'Close the game before installing.' }
$version = '6.0.0-be.788+5b766a3'
$archive = Join-Path $repo ".local\downloads\BepInEx-Unity.IL2CPP-win-x64-$version.zip"
$expected = 'F4CC496BD098A0DF4164B81E3737297707F13A47C2478DBA2F60EEFAB784817A'
New-Item -ItemType Directory -Force -Path (Split-Path $archive) | Out-Null
if (!(Test-Path -LiteralPath $archive)) {
    Invoke-WebRequest "https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2B5b766a3.zip" -OutFile $archive
}
if ((Get-FileHash -LiteralPath $archive).Hash -ne $expected) { throw 'Loader archive SHA256 differs from recorded artifact.' }
$staging = Join-Path $repo '.local\loader'
Expand-Archive -LiteralPath $archive -DestinationPath $staging -Force
$manifestPath = Join-Path $repo '.local\loader-install.json'
if (Test-Path -LiteralPath $manifestPath) { throw 'Loader installation is already recorded. Uninstall before reinstalling.' }
if (Get-ChildItem -LiteralPath (Join-Path $GameDir 'BepInEx\core') -Filter 'BepInEx*.dll' -ErrorAction SilentlyContinue) { throw 'Existing BepInEx install found; preserve it and use its bindings, or uninstall it first.' }
$backup = Join-Path $repo '.local\loader-backup'
$records = @()
foreach ($file in Get-ChildItem -LiteralPath $staging -Recurse -File) {
    $relative = $file.FullName.Substring($staging.Length + 1)
    $target = Join-Path $GameDir $relative
    $prior = $null
    if (Test-Path -LiteralPath $target) {
        $prior = Join-Path $backup $relative
        New-Item -ItemType Directory -Force -Path (Split-Path $prior) | Out-Null
        Copy-Item -LiteralPath $target -Destination $prior
    }
    $records += [PSCustomObject]@{ Relative = $relative; Backup = $prior; Hash = (Get-FileHash -LiteralPath $file.FullName).Hash }
}
# Save rollback information before any game mutation, including partially failed installs.
@{ GameDir = $GameDir; Version = $version; ArchiveSHA256 = $expected; Files = $records } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath
foreach ($file in Get-ChildItem -LiteralPath $staging -Recurse -File) {
    $target = Join-Path $GameDir $file.FullName.Substring($staging.Length + 1)
    New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target -Force
}
Write-Host "Installed BepInEx $version. Launch the game once to generate BepInEx/interop; inspect BepInEx/LogOutput.log."
