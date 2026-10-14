# Verification and troubleshooting

Baseline: **PBDMP 0.7.0 / protocol 7**. This page summarizes recorded development checks and how to repeat them. Raw logs, private install manifests and generated test output are excluded from the repository. The maintainer has verified two-PC play; the automated results below provide additional regression coverage.

## Recorded checks

| Area | Recorded result | Practical limit |
| --- | --- | --- |
| Release compilation | Zero warnings/errors for the final mission-pass build. | Requires matching game-generated interop. |
| Network acceptance | 15 executable scenarios pass, including 12,288 ordered mission frames under bounded-queue pressure. | A loading-failure-reason timing check failed transiently and passed on rerun; not fully diagnosed. |
| Native UI/player presentation | Main/pause entries, dialog ownership/navigation, model rendering and native clips exercised. | Isolated renders and callback checks measure presentation; physical controller input is a separate test path. |
| Travel and input | Two game processes exercise host travel, readiness, respawn, late join and overlapping pause/menu locks. | Focus round trips cover a reproduced input lock, not every intermittent Alt-Tab failure. |
| Combat and actors | Native attacks, host targeting/hits, personal incoming damage, actor lifecycle and special targets exercised. | All natural attack/boss sequences still need acceptance. |
| Actor catalogue | 49 NPC families exercised in fixtures; 43 authored families across 243 map/family rows inspected. | Fixture and authored-census counts measure different coverage. |
| Doors and encounter state | Native requests, keys, reload, late join, P2 encounter activation and group progression exercised. | Unclassified authored variants remain possible. |
| Urine | Electrical boxes, tanks, toilet state, extinguishing, exposed weak-spot damage and supported late join exercised. | Natural rail/rocket/puzzle sequences are not all complete acceptance tests. |
| Neighborhood house wall | P1/P2 activation removes the actual obstruction's renderer/collider; both controllers pass through, including after rejoin. | Does not establish general decorative destruction support. |
| Level endings | Prison walk-in and Wallmarkt interactable endings exercise gathering/cancellation/one finish; automatic endings wait for respawn. | Representative ending fixtures, not every mission ending. |
| Campaign census | All 15 base maps inspected for supported catalogue/bounded-state serialization. | A long sequential audit hit native NPC initialization errors; remaining maps passed fresh-process checks. |
| Host timing | Controlled 60 FPS mission fixture: about 3.7 ms p95 world work, 150 ms p95 sweep and 16.7 ms p95 frame time. | Stationary two-instance diagnostic, not a hardware-independent benchmark. |

The [sync tracker](sync-progress.md) records feature-specific gaps. Historical [campaign counts](campaign-coverage.md) predate the latest mission adapters and are retained as a dated census.

## Running checks

Use a compatible installation with BepInEx-generated interop and the current plugin installed. Run from `<RepoDir>`, with `$GameDir` set to your own game folder. Close existing game processes before native tests.

```powershell
# Compiles/packages and runs the network acceptance executable.
.\scripts\Build.ps1 -GameDir $GameDir

# Native UI/model smoke test; does not enter gameplay.
.\scripts\Test-Runtime.ps1 -GameDir $GameDir

# Two-process campaign lifecycle and focused regressions.
.\scripts\Test-Travel.ps1 -GameDir $GameDir
.\scripts\Test-Travel.ps1 -GameDir $GameDir -Combat
.\scripts\Test-Travel.ps1 -GameDir $GameDir -Doors
.\scripts\Test-Travel.ps1 -GameDir $GameDir -DoorReload
.\scripts\Test-Travel.ps1 -GameDir $GameDir -State -Level LEVEL_AD_Prison
.\scripts\Test-Travel.ps1 -GameDir $GameDir -Urine
.\scripts\Test-Travel.ps1 -GameDir $GameDir -Mission
.\scripts\Test-Travel.ps1 -GameDir $GameDir -MissionExits -Level LEVEL_AD_Prison
.\scripts\Test-Travel.ps1 -GameDir $GameDir -MissionExits -Level LEVEL_AD_Wallmarkt
.\scripts\Test-Travel.ps1 -GameDir $GameDir -Performance -PerformanceFrameRate 60

# Fresh-process actor/catalogue census across the 15 campaign maps.
.\scripts\Test-ActorCampaign.ps1 -GameDir $GameDir
```

Additional `Test-Travel.ps1` modes include `-Actors`, `-ActorParts`, `-UrineParts`, `-JoinInput`, `-FocusInput` and `-Audit -AuditLevels LEVEL_Name`. `Test-CombatProbe.ps1` checks a native P2 targeting/hit proxy. Inspect each script's parameter block before combining diagnostic modes.

Tests create ignored output under `.local`, block profile writers with diagnostic flags, and stop only processes they own. Travel tests create a second loader/executable directory using hardlinks/junctions to the reader's installed game; they require a compatible Windows filesystem. Most windows are hidden, while `-FocusInput` exercises real window focus. Game/platform profile access and graphics initialization must work in the environment used for testing. Hidden automation is not the normal player launch path.

## Gameplay regression checklist

- Both PCs use the same game and exact DLL pair; LAN/firewall connectivity works.
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
| Cannot host/join | Check numeric IPv4, port, occupied ports, reachable LAN and private-network firewall rules. Loopback only reaches the same PC. |
| Connected but unable to move | Check loading/shared-pause status, close both multiplayer dialogs, resume P1 and inspect input-lock logs. Report the exact status and steps if it persists. |
| Late join stalls | Preserve sanitized logs from both peers and the map/action sequence. Campaign readiness has a 120-second deadline. |
| Host spikes | Compare normal gameplay with the controlled performance fixture. Two game instances also share CPU/GPU resources. |
| An object differs between peers | Report map, object, which player activated it, fresh/reloaded/late-join state and sanitized logs. General world callbacks remain partially covered. |

Logs can contain installation paths, network addresses and profile details. Review/redact them before attaching to a public issue. Do not publish game files, save data or generated interop.
