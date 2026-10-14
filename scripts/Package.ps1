[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$source = Join-Path $repo 'artifacts\PBDMP\BepInEx\plugins\PBDMP'
$files = 'PBDMP.dll','PBDMP.Network.dll' | ForEach-Object { Join-Path $source $_ }
foreach ($file in $files) { if (!(Test-Path -LiteralPath $file)) { throw 'Run scripts/Build.ps1 first.' } }
$version = ((Get-Item -LiteralPath $files[0]).VersionInfo.ProductVersion -split '\+')[0]
# Same derivation as Plugin.BuildId, so both players can compare it with their loader log.
$hashes = $files | ForEach-Object { (Get-FileHash -LiteralPath $_).Hash }
$sha = [Security.Cryptography.SHA256]::Create()
try { $buildId = (($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($hashes[0] + $hashes[1])) | ForEach-Object { $_.ToString('X2') }) -join '').Substring(0, 16) }
finally { $sha.Dispose() }

$staging = Join-Path $repo '.local\package'
if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
$plugin = Join-Path $staging 'BepInEx\plugins\PBDMP'
New-Item -ItemType Directory -Force -Path $plugin | Out-Null
Copy-Item -LiteralPath $files -Destination $plugin
@"
PBDMP $version - POSTAL: Brain Damaged two-player co-op (development build $buildId)

Both players need this exact package. A different build is refused when joining.

1. Install BepInEx 6.0.0-be.788+5b766a3, Unity IL2CPP win-x64, into the game folder
   (Steam: right-click the game > Manage > Browse local files):
   https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2B5b766a3.zip
2. Start the game from Steam once and let BepInEx finish its first-run setup, then quit.
3. Copy the BepInEx folder from this zip into the game folder, merging with the existing one.
4. Start the game from Steam. BepInEx\LogOutput.log should show: PBDMP $version development build $buildId

Playing through Steam
- Host: Multiplayer > Host Steam game > Invite friend (or the friend uses Join Game on your name in Steam).
- Friend: accept the invite or click Join Game while the game is running.
- No IP addresses or port forwarding are needed; Steam relays the connection.

Playing over a LAN or a forwarded port: Multiplayer > Direct IP.

Files:
  PBDMP.dll          $($hashes[0])
  PBDMP.Network.dll  $($hashes[1])
"@ | Set-Content -LiteralPath (Join-Path $staging 'INSTALL.txt') -Encoding utf8
$zip = Join-Path $repo "artifacts\PBDMP-$version.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip
Write-Host "Package: $zip (build $buildId). Send this exact file to the other player."
