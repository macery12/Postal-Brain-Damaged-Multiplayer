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

$stagingParent = [IO.Path]::GetFullPath((Join-Path $repo '.local'))
$staging = Join-Path $stagingParent 'package'
# Full-path comparison (Windows PowerShell 5.1 has no Path.GetRelativePath).
if (!$staging.Equals((Join-Path $repo '.local\package'), [StringComparison]::OrdinalIgnoreCase)) { throw 'Package staging path is outside the repository.' }
if ((Test-Path -LiteralPath $stagingParent) -and (Get-Item -LiteralPath $stagingParent).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw 'Package parent cannot be a junction or symbolic link.' }
if ((Test-Path -LiteralPath $staging) -and (Get-Item -LiteralPath $staging).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw 'Package staging cannot be a junction or symbolic link.' }
if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
$plugin = Join-Path $staging 'BepInEx\plugins\PBDMP'
New-Item -ItemType Directory -Force -Path $plugin | Out-Null
Copy-Item -LiteralPath $files -Destination $plugin
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $staging
@"
PBDMP $version - POSTAL: Brain Damaged two-player co-op (build $buildId)

Both players need this exact package. A different build is refused when joining.

Install
1. Install BepInEx 6.0.0-be.788+5b766a3, Unity IL2CPP win-x64, into the game folder
   (Steam: right-click the game > Manage > Browse local files):
   https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2B5b766a3.zip
2. Start the game from Steam once and let BepInEx finish its first-run setup, then quit.
3. Copy the BepInEx folder from this zip into the game folder, merging with the existing one.
4. Start the game from Steam. BepInEx\LogOutput.log should show: PBDMP $version build $buildId

Play
Main menu > Multiplayer (or F8 in the menus):
- Continue co-op     load the newest save of your latest co-op game
- New co-op game     pick a map in the game's level select, confirm, choose difficulty
- Load co-op game    co-op games on the left, their saves on the right
- Survival co-op     the game's own arena and difficulty menu
- Join a friend      Steam friends hosting PBDMP; Join, Refresh, or Direct IP...

Through Steam, P1 hosts a friends-only lobby when picking one of the first four.
In game, pause > Players & invite holds Invite friend, lobby privacy, Direct IP hosting
and Leave session. P2 joins from Join a friend, an invite, or Join Game on P1's name in
Steam (this also works while P2's game is closed). Steam relays the connection: no IP
addresses or port forwarding. Over a LAN or a forwarded TCP port, P1 switches Direct IP
on in Players & invite and P2 uses Join a friend > Direct IP...

Campaign: weapons, keys and mission items are shared - a pickup disappears for both and
both players get it. Ammo, health, armor and powerups stay personal.
Saves: P1's pause Save and Load open the co-op save screens. Each co-op game keeps its own
quicksave, autosaves and manual saves in BepInEx/PBDMP/saves, separate from solo saves.
Survival: take the shotgun or an offer, or hold Use, to ready up; waves start when both are
ready. Dead players revive at the next break. Records: BepInEx/PBDMP/survival-records.json.

Files:
  PBDMP.dll          $($hashes[0])
  PBDMP.Network.dll  $($hashes[1])
"@ | Set-Content -LiteralPath (Join-Path $staging 'INSTALL.txt') -Encoding utf8
$zip = Join-Path $repo "artifacts\PBDMP-$version.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip
Write-Host "Package: $zip (build $buildId). Send this exact file to the other player."
