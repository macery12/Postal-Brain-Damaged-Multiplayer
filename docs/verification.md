# Verification and troubleshooting

Baseline: **PBDMP 0.8.0 / protocol 7**. This page summarizes recorded development checks and how to repeat them. Raw logs, private install manifests and generated test output are excluded from the repository. The maintainer has verified two-PC play; the automated results below provide additional regression coverage.

## Recorded checks

| Area | Recorded result | Practical limit |
| --- | --- | --- |
| Release compilation | Zero warnings/errors for the final mission-pass build. | Requires matching game-generated interop. |
| Network acceptance | 19 executable scenarios pass over TCP and an in-memory message carrier, including 12,288 ordered mission frames under bounded-queue pressure on each, and a stated drop reason surviving ten consecutive closes. | The earlier transient loading-failure-reason check was a race between the closing peer's send failure and its pending Reject; the session now waits briefly for the stated reason. |
| Steam (single account) | The probe passes on the real Steam client: callback tap, identity, native struct layouts, 300 ordered frames and a drop reason over a Steam socket pair, a P2P dial into the local listen socket (status callbacks, admission, accept), and private lobby create/data/leave. The game's own leaderboard call results still arrive with the tap installed. | Steam cannot relay between two sessions of one account. A first two-account session joined the friends lobby and connected over the relay twice; the rest of the checklist below is still open. |
| Native UI/player presentation | Lobby panel, Direct IP dialog and prompt layers; main/pause entries, dialog ownership, per-layer navigation and Escape order; model rendering and native clips exercised. | Isolated renders and callback checks measure presentation; physical controller input is a separate test path. |
| Travel and input | Two game processes exercise host travel, readiness, respawn, late join and overlapping pause/menu locks. | Focus round trips cover a reproduced input lock, not every intermittent Alt-Tab failure. |
| Combat and actors | Native attacks, host targeting/hits, personal incoming damage, actor lifecycle and special targets exercised. | All natural attack/boss sequences still need acceptance. |
| Actor catalogue | 49 NPC families exercised in fixtures; 43 authored families across 243 map/family rows inspected. | Fixture and authored-census counts measure different coverage. |
| Doors and encounter state | Native requests, keys, reload, late join, P2 encounter activation and group progression exercised. | Unclassified authored variants remain possible. |
| Urine | Electrical boxes, tanks, toilet state, extinguishing, exposed weak-spot damage and supported late join exercised. | Natural rail/rocket/puzzle sequences are not all complete acceptance tests. |
| Neighborhood house wall | P1/P2 activation removes the actual obstruction's renderer/collider; both controllers pass through, including after rejoin. | Does not establish general decorative destruction support. |
| Level endings | Prison walk-in and Wallmarkt interactable endings exercise gathering/cancellation/one finish; automatic endings wait for respawn. | Representative ending fixtures, not every mission ending. |
| Campaign census | All 15 base maps inspected for supported catalogue/bounded-state serialization. | A long sequential audit hit native NPC initialization errors; remaining maps passed fresh-process checks. |
| Host timing | Controlled 60 FPS mission fixture: about 3.7 ms p95 world work, 150 ms p95 sweep and 16.7 ms p95 frame time. | Stationary two-instance diagnostic, not a hardware-independent benchmark. |

Fixed during 0.8.0 two-player testing: P2 loaded Neighborhood, logged `Authored enemy/door catalogue mismatch` and returned to the menu (P1 762 authored components, P2 754). The game's distance optimizer switches far pickups off shortly after a map loads. The catalogue's "never share pickups, NPCs, players or UI" exclusion used Unity's default parent lookup, which skips inactive objects, so an inactive pickup's `InteractListener` entered the catalogue. Whether it did depended on when each peer built its catalogue and where its player stood. The exclusion now includes inactive parents. Building both variants at the same moment on all 15 maps showed the only removed components are on `ItemPickup`/pickup objects (274 rows: ammo, health, weapons, keys and carried quest items such as levers and winches), plus mission-flag changes on enemy groups those listeners referenced. The earlier Prison 559/563 travel-test failure had the same cause; the default, Neighborhood, mission, combat and Prison state travel runs now pass. Each catalogue build writes `BepInEx/PBDMP/catalogue-<scene>.tsv` so two players can diff their catalogues.

The [sync tracker](sync-progress.md) records feature-specific gaps. Historical [campaign counts](campaign-coverage.md) predate the latest mission adapters and are retained as a dated census.

## Explosion, urine, hit-effect and completion regressions

The wall failure was reproducible with a stronger native geometry comparison: P2 hid the replacement hierarchy, removing the permanent wall around the opening as well as the loose fragments. The destruction adapter now keeps the authored replacement mesh and static collision, while hiding cosmetic debris. The Neighborhood fixture compares the remaining active structural surfaces and tests actual player passage, including late join. Hidden/pooled debris is excluded from structural comparisons.

Special explosion replay passed a null managed wrapper for IL2CPP's nullable rotation parameter. Supplying an explicit empty IL2CPP nullable avoids the reported marshalling exception. Explosive-barrel hits and world flame ticks now resolve on P1, where native explosion listeners advance linked mission geometry. The Prison regression requires the actual `OBJECTIVES/Pipe_hole` to open, compares its active geometry on P2, and verifies both players remain connected after native explosion replay and a separate P2 barrel hit.

NPC VFX callbacks previously relayed decals but omitted native material impact bursts. The host now publishes the selected flesh/armor particles and positional audio for harmless client presentation. The native regression checks accepted P2 damage and actual emitted hit particles.

A host-authorized finish now publishes final level statistics after the native game gathers them. P2 runs the native completion presentation after readiness, with host result values and exact completion time. The exit regression checks the native screen, matching statistics and rejoining a completed map; P1 retains restart/next-level authority.

## Running checks

Use a compatible installation with BepInEx-generated interop and the current plugin installed. Run from `<RepoDir>`, with `$GameDir` set to your own game folder. Close existing game processes before native tests.

```powershell
# Compiles/packages and runs the network acceptance executable.
.\scripts\Build.ps1 -GameDir $GameDir

# Native UI/model smoke test; does not enter gameplay.
.\scripts\Test-Runtime.ps1 -GameDir $GameDir

# Steam path on one account; needs the Steam client running. Creates and leaves a private lobby.
.\scripts\Test-Steam.ps1 -GameDir $GameDir

# Two-process campaign lifecycle and focused regressions.
.\scripts\Test-Travel.ps1 -GameDir $GameDir
.\scripts\Test-Travel.ps1 -GameDir $GameDir -Combat
.\scripts\Test-Travel.ps1 -GameDir $GameDir -Doors
.\scripts\Test-Travel.ps1 -GameDir $GameDir -DoorReload
.\scripts\Test-Travel.ps1 -GameDir $GameDir -State -Level LEVEL_AD_Prison
.\scripts\Test-Travel.ps1 -GameDir $GameDir -Urine
.\scripts\Test-Travel.ps1 -GameDir $GameDir -Mission -Level LEVEL_AD_Neighborhood
.\scripts\Test-Travel.ps1 -GameDir $GameDir -WorldRegression -Level LEVEL_AD_Neighborhood
.\scripts\Test-Travel.ps1 -GameDir $GameDir -WorldRegression -Level LEVEL_AD_Prison
.\scripts\Test-Travel.ps1 -GameDir $GameDir -MissionExits -Level LEVEL_AD_Prison
.\scripts\Test-Travel.ps1 -GameDir $GameDir -MissionExits -Level LEVEL_AD_Wallmarkt
.\scripts\Test-Travel.ps1 -GameDir $GameDir -Performance -PerformanceFrameRate 60

# Fresh-process actor/catalogue census across the 15 campaign maps.
.\scripts\Test-ActorCampaign.ps1 -GameDir $GameDir
```

Additional `Test-Travel.ps1` modes include `-Actors`, `-ActorParts`, `-UrineParts`, `-JoinInput`, `-FocusInput` and `-Audit -AuditLevels LEVEL_Name`. `Test-CombatProbe.ps1` checks a native P2 targeting/hit proxy. Inspect each script's parameter block before combining diagnostic modes.

Tests create ignored output under `.local`, block profile writers with diagnostic flags, and stop only processes they own. A Steam install started directly would relaunch itself through Steam and exit, so the scripts set `SteamAppId` for the processes they start; the Steam client must be running. Travel tests create a second loader/executable directory using hardlinks/junctions to the reader's installed game; they require a compatible Windows filesystem. Most windows are hidden, while `-FocusInput` exercises real window focus. Game/platform profile access and graphics initialization must work in the environment used for testing. Hidden automation is not the normal player launch path.

## Two-player Steam checklist

Use the same `Package.ps1` zip on both PCs and two Steam accounts that are friends and own the game.

- P1 hosts from the main menu; the lobby panel shows P1's name and avatar. **Invite friend** opens the Steam overlay; P2 accepts from the main menu and joins without a question.
- P2 instead uses **Join Game** on P1's name; with the lobby set to invite only, Join Game is not offered.
- P1 hosts from a level's pause menu; P2 accepts while inside their own level, sees the confirmation, chooses **Stay** once (nothing changes) and **Join** the second time.
- Both cards show names, avatars, roles and a plausible ping; the in-game badge, notifications and P2's nameplate show the partner's Steam name.
- Play a level through an exit together. P2 quits and rejoins; P1 quits (P2 returns to the menu and the lobby is gone).
- A deliberately different build on P2 shows the mismatch prompt before connecting.
- Logs on both PCs contain no `Steam callback tap:` errors, and the game's own leaderboards still work.

## Gameplay regression checklist

- Both PCs use the same game and exact DLL pair; Steam lobby or LAN/firewall connectivity works.
- Native main/pause entries, F8, pointer editing, keyboard navigation and physical controller input work after map changes.
- Both players move, aim and fire; remote models align and remain visible through respawn/rejoin.
- P2 follows starts/restarts/next-map travel, cannot start levels independently, and is released after current-state restoration.
- P1 pause freezes both; P2 menus do not freeze P1; Alt-Tab and overlapping menus restore input.
- Enemies can attack either player; both players damage shared enemies once; pickups remain personal.
- Keys unlock shared doors using only the requester's inventory.
- Neighborhood's electrical box opens the house route for both players, including late join.
- Natural mission puzzles, rides, traps, boss phases and ending sequences complete on both PCs.
- Exits wait for both living players; leaving/death/disconnect follow the gathering rules.
- Session departure restores offline controls and save behavior without corrupting solo progress.

Use these scenarios to regression-test future changes.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| Missing menu entry | Press F8. Inspect `<GameDir>/BepInEx/LogOutput.log` for PBDMP startup and native UI errors. Confirm the IL2CPP loader and both plugin DLLs are installed. |
| Build cannot resolve game types | Launch the compatible loader once to generate interop. Pass the correct `-GameDir`; do not use decompiler dummy DLLs. |
| Build mismatch | Copy the same freshly built DLL pair to both PCs; matching version strings alone are insufficient. |
| Steam options greyed out | The panel's status line explains why: Steam must be running and the game started from Steam. Non-Steam copies can still use Direct IP. |
| Steam invite does nothing | Enable the Steam overlay, or have the friend use Join Game on the host's name. Both players need the exact same build; a mismatch is reported after accepting. |
| Cannot host/join by IP | Check numeric IPv4, port, occupied ports, reachable LAN and private-network firewall rules. Loopback only reaches the same PC. |
| P2 loads the level, then returns to the menu (`catalogue mismatch` in the log) | Both PCs need the same game build and the same zip. Every level load writes `<GameDir>/BepInEx/PBDMP/catalogue-<scene>.tsv`; diff both players' files for that map and report the differing lines with the logs. |
| Connected but unable to move | Check loading/shared-pause status, close both multiplayer dialogs, resume P1 and inspect input-lock logs. Report the exact status and steps if it persists. |
| Late join stalls | Preserve sanitized logs from both peers and the map/action sequence. Campaign readiness has a 120-second deadline. |
| Host spikes | Compare normal gameplay with the controlled performance fixture. Two game instances also share CPU/GPU resources. |
| An object differs between peers | Report map, object, which player activated it, fresh/reloaded/late-join state and sanitized logs. General world callbacks remain partially covered. |

Logs can contain installation paths, network addresses and profile details. Review/redact them before attaching to a public issue. Do not publish game files, save data or generated interop.
