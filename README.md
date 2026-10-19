# POSTAL: Brain Damaged Multiplayer (PBDMP)

Play **POSTAL: Brain Damaged** together. PBDMP adds two-player co-op to the campaign, letting you team up with a Steam friend over the internet or with someone on your LAN.

Built for BepInEx 6 IL2CPP. **Current development version: 0.10.0.**

## Sync progress

A quick look at what's ready and what's next. See the [full sync checklist](docs/sync-progress.md) for individual features.

| Feature | Planned | Partially synced | Synced | Comments |
| --- | :---: | :---: | :---: | --- |
| Steam Host / Invite / Join | | | ✅ | Friends lobby, overlay invites, Join Game; Steam relays the connection. |
| LAN Host / Join | | | ✅ | Direct IP for LAN or a forwarded port. |
| Player movement and aim | | | ✅ | See each other in-game. |
| Player model and animations | | ✅ | | Improved weapon grips and movement; further animation polish planned. |
| Campaign travel and pause | | | ✅ | Play through levels together. |
| Independent respawns | | | ✅ | Keep playing when someone dies. |
| Gunfire, projectiles and combat | | | ✅ | Fight together. |
| NPCs, pedestrians and flying enemies | | | ✅ | Shared enemies and reactions. |
| Doors and shared unlocks | | | ✅ | Either player can interact. |
| Urine interactions | | | ✅ | Shared puzzles and effects. |
| Mission triggers and machinery | | ✅ | | World adapters implemented; campaign playtesting continues. |
| Joining an active session | | ✅ | | Current-world restoration implemented; broader playtesting continues. |
| Shared checkpoints | | | ✅ | P1 can restore the shared recovery point. |
| Multiplayer saves | | | ✅ | Separate co-op slots, quicksave and autosaves. |
| Survival co-op | | ✅ | | All three base arenas: shared waves, personal offers, readiness, wave revival, shared results, co-op records and late join. Natural two-PC playtesting continues. |
| FPS and network performance | | ✅ | | Optimization remains a priority. |

**Development coverage: 93%** (includes partial work); **last accepted baseline: 90%.** [Score breakdown and intentionally local systems](docs/sync-progress.md#progress-score).

![Multiplayer in the native main menu](docs/images/main-menu.png)

## Installation

No public release is available yet while the mod is in development. To play with a friend, one of you builds it (instructions below), runs `.\scripts\Package.ps1`, and sends the resulting `artifacts\PBDMP-<version>.zip`; its `INSTALL.txt` lists the BepInEx build to install. Both players must use the same zip.

<details>
<summary>Build and install from source</summary>

You need Windows x64, your own game installation, PowerShell, and **.NET SDK 10** to build from source. The plugin targets .NET 6 for the pinned loader runtime; the supplied network test runner targets .NET 10.

The reference game build uses **Unity 2021.3.14f1**, **IL2CPP metadata 29**, and **BepInEx 6 IL2CPP win-x64**, pinned to `6.0.0-be.788+5b766a3`. Other game builds require compatibility testing. A Mono loader is incompatible with this setup.

### Path placeholders

All paths in these instructions are relative paths or placeholders for the reader's own setup.

| Placeholder | Meaning |
| --- | --- |
| `<RepoDir>` | Your repository checkout. |
| `<GameDir>` | The game folder containing `POSTAL Brain Damaged.exe`. In Steam, use **Manage → Browse local files**. |
| `<Decompiled>` | Optional, independently generated research output from your own game installation. Not included or needed for a normal build. |

### Build and install

Run from PowerShell. Replace `<RepoDir>` with your checkout; enter your game folder when prompted. Skip `Install-Loader.ps1` if a compatible loader is already installed.

```powershell
Set-Location '<RepoDir>'
$GameDir = Read-Host 'Game folder containing POSTAL Brain Damaged.exe'

.\scripts\Install-Loader.ps1 -GameDir $GameDir

Start-Process -FilePath (Join-Path $GameDir 'POSTAL Brain Damaged.exe') -Wait

.\scripts\Build.ps1 -GameDir $GameDir
.\scripts\Install-Plugin.ps1 -GameDir $GameDir
```

The loader installer checks a pinned archive hash and refuses to replace an existing BepInEx installation. The build reads generated assemblies from `<GameDir>/BepInEx/interop`; recovered dummy/decompiler DLLs cannot replace them.

`Build.ps1` builds the plugin, runs network and reporting acceptance tests and places the plugin package in `artifacts/PBDMP`. Install **both** `PBDMP.dll` and `PBDMP.Network.dll` together; the installer handles this. Game binaries, extracted assets, generated interop and loader binaries are not supplied by this repository.

Always pass `-GameDir` to build/install/runtime-test scripts. Their legacy default expects a sibling `Game` folder and is not automatic Steam discovery. Rollback manifests and backups are stored in the checkout's ignored `.local` directory; keep the checkout used for installation until uninstall is complete.

### Play together

Launch the game from Steam on both PCs after installation. **Multiplayer** sits below Survival Mode in the main menu and in the pause menu; **F8** opens it anywhere.

**Through Steam (recommended):** no IP addresses, port forwarding or firewall rules.

1. P1 selects **Host Steam game**. The lobby is friends-only by default; **Lobby: friends only / invite only** switches it.
2. P1 selects **Invite friend** to pick a friend in the Steam overlay. Alternatively the friend uses **Join Game** on P1's name in their Steam friends list.
3. P2 accepts while the game is running. From the menus the join starts at once; from inside a level P2 is asked first, because unsaved progress is lost. A different PBDMP build or version is reported before connecting.

**Direct IP (LAN or a forwarded TCP port):** select **Direct IP...**. P1 chooses a port (default `7777`) and selects **Host (P1)**. P2 enters P1's IPv4 address (`ipconfig` on P1's PC) and port, then selects **Join (P2)**. `127.0.0.1` only reaches the same PC. Allow the game/TCP port on your private network if Windows Firewall prompts.

Then P1 starts a built-in campaign level, or hosts an already active one. P2 loads the same map and receives the supported current world state before play resumes. The lobby panel shows both players (Steam name, avatar and ping for Steam sessions); a badge in the corner and short notifications show session changes in game. P1 controls difficulty, starts, restarts and level changes. Walk-in exits require both living players at the exit; interactable endings require both nearby. Automatic endings wait until both are alive and loaded.

P2 joins in clear space near P1, with normal-map-spawn fallback. P2 receives P1's weapons, remaining ammo and equipped weapon on each join/rejoin and save restoration, then inventories stay independent. P2 starts those events at full health, zero armor and without temporary powerups; keys and other non-ammo items remain personal.

Death respawns only that player after three seconds at the map spawn with full health, zero armor, retained gear/ammo/items and cleared temporary powerups. P1's pause stops the timer. P2 opening a menu does not pause P1. P2 leaving lets P1 continue; P1 leaving ends the session and returns P2 to the main menu.

P1 can open **Multiplayer → Co-op saves...** for named slots, quicksave and checkpoint restoration. To resume, host a lobby first, then load a co-op slot. Solo saves stay separate. See [co-op saves](docs/co-op-saves.md) for the recovery and partner rules.

#### Survival co-op

P1 picks an arena and difficulty from the native **Survival Mode** menu while hosting, or hosts from inside an arena. The three built-in arenas (Suburbs, Ufo, VIP Zone) are supported; DLC and custom arenas are not. P2's Survival menu stays closed.

- **One shared run.** P1's game runs the waves, enemies, timer, score and combo; both players see them on the normal Survival HUD. Either player's kills score, and every hit either player takes costs the team combo once.
- **Your own gear.** Health, armor, weapons, ammo and pickups stay personal. Each player gets their own copy of every weapon offer and resource, so taking one never removes your partner's.
- **Ready up.** At the start and between waves, take the shotgun or an offer, or hold **Use** for a second to keep your current gear. The next wave starts once both living players are ready. Playing alone, P1 starts at once.
- **Death.** A dead player watches their partner and revives at the next break with full health, zero armor and their weapons and ammo. The run ends when nobody is left alive. Both players then see the same results, and only P1 can retry or leave.
- **Joining late.** A player who joins mid-wave waits ("Waiting for P1's next Survival wave") while P1 keeps playing. They enter at the next break with P1's weapons and ammo. A player who died can't reconnect to come back early.
- **Records.** Each PC keeps its own co-op best per arena and difficulty, plus the last 20 runs, in `BepInEx/PBDMP/survival-records.json`. Solo records and leaderboards are untouched. A Survival run can't be saved or resumed.

### Development and testing

From `<RepoDir>`, with `$GameDir` set as above:

```powershell
.\scripts\Build.ps1 -GameDir $GameDir

.\scripts\Test-Runtime.ps1 -GameDir $GameDir
.\scripts\Test-Steam.ps1 -GameDir $GameDir
.\scripts\Test-Travel.ps1 -GameDir $GameDir

.\scripts\Test-Travel.ps1 -GameDir $GameDir -Combat
.\scripts\Test-Travel.ps1 -GameDir $GameDir -Mission -Level LEVEL_AD_Neighborhood
.\scripts\Test-Travel.ps1 -GameDir $GameDir -MissionExits -Level LEVEL_AD_Prison

.\scripts\Test-Travel.ps1 -GameDir $GameDir -SurvivalPrep -Level LEVEL_Horde_Ufo
.\scripts\Test-Travel.ps1 -GameDir $GameDir -SurvivalSoak
```

`Build.ps1 -SkipTests` skips network tests. `Test-Steam.ps1` needs the Steam client running and signed in; it creates and leaves a private lobby and exercises the Steam connection path on one account. Runtime scripts launch owned game processes and block profile writes through diagnostic flags; on a Steam install they put the game's app id in the process environment so a direct launch is not redirected through Steam. Most test windows are hidden; `-FocusInput` intentionally exercises window focus. Two-instance travel tests use local hardlinks/junctions and retain ignored diagnostic output. See [verification](docs/verification.md) for prerequisites, additional modes and evidence limits.

Source layout: `src/PBDMP.Network` contains the bounded protocol/session with its TCP and Steam carriers; `src/PBDMP.Plugin` contains Unity/BepInEx adapters; `tests/PBDMP.Network.Tests` contains executable network scenarios. Start with the [documentation index](docs/README.md) and [contributor guidance](docs/contributing.md). Keep Unity calls on Unity's main thread.

### Uninstall

Close the game and run these from the same checkout used to install it. Run the loader command only if that checkout also installed the loader:

```powershell
.\scripts\Uninstall-Plugin.ps1
.\scripts\Uninstall-Loader.ps1
```

Uninstall uses recorded manifests, preserves original backups and refuses to overwrite files whose hashes changed. Config, logs and generated interop remain available. Do not copy machine-specific install manifests into Git.

</details>

## Documentation

Browse the [documentation](docs/README.md), [roadmap](docs/roadmap.md) or [contributor guide](docs/contributing.md).

## License

PBDMP's original source and documentation are licensed under the [MIT License](LICENSE), copyright 2026 macery12. Game content, illustrative game renders and third-party components retain their respective owners' rights. The mod resolves native models, UI, sounds and effects from each player's own game installation at runtime.
