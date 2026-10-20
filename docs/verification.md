# Verification and troubleshooting

Current release: **PBDMP 1.0.0 / protocol 13 / save schema 3**; see the 1.0.0 release record below. Earlier sections are historical. This page summarizes recorded development checks and how to repeat them. Raw logs, private install manifests and generated test output are excluded from the repository. The maintainer has verified two-PC play; the automated results below provide additional regression coverage.

## 1.0.0 release — October 19, 2026

Installed and packaged **1.0.0 / protocol 13 / save schema 3**, build **960F3A0ADE93A2BB** (`PBDMP.dll` 8F48E0C20209C8E7, `PBDMP.Network.dll` 154AA4D3299BFE3F). `artifacts/PBDMP-1.0.0.zip` holds exactly that pair, and the game folder's installed DLLs match it. Every check below ran on that DLL pair in isolated installations.

| Check | Result |
| --- | --- |
| Compile / managed | 0 warnings; 41 network scenarios, 8 reporting checks. |
| Single game | Runtime smoke 46; `-CoopMenu` 36; `Test-Steam` 25 (one account, including Join a friend). |
| Campaign (two processes) | Travel 54; `-Saves` 49, `-SaveResume` 23, `-SaveReplacePartner` 24; `-JoinInput` 10; `-SharedItems` 19; `-Doors` 20; `-DoorReload` 6; `-Combat` 28; `-State` (Prison) 3,775; `-Actors` 597; `-ActorParts` 33; `-Urine` 3,175; `-Mission` 1,829; `-WorldRegression` Neighborhood 919, Prison 1,595; `-MissionExits` Prison 13, Wallmarkt 13. |
| Survival, each base arena (Suburbs / UFO / VIP Zone) | `-Survival` 29 / 29 / 29; `-SurvivalWaves` 30 / 30 / 32; `-SurvivalPrep` 47 / 47 / 48; `-SurvivalResults` 22 each; `-SurvivalJoin` 23 each; `-SurvivalFailures` 16 each. `-SurvivalSoak` (Suburbs) 24; arena `-JoinInput` 10 and `-JoinFromLevel` 11. |

Fixture fixes found during the release pass:
- `-ActorParts` and `-DoorReload` need `-Level LEVEL_AD_Neighborhood`, the map they test. Their earlier failures came from running on the default map.
- `-MissionExits` on Wallmarkt now dismisses the native tutorial popup that map shows. An open tutorial holds the shared pause, so P2's respawn timer never ran. The pre-redesign build failed at the same point.

Maintainer checks still open (two Steam accounts):
- Join a friend: seeing and joining a host, "Full" after joining, invite-only lobbies hidden;
- Join Game with P2's game closed (`+connect_lobby`);
- Steam hosting from the new menus and the Direct IP switch in Players & invite.

## Survival co-op closeout — October 18, 2026

Installed/exported **0.10.0 / protocol 12 / save schema 2**, build **41D7421713392F3B** (`PBDMP.dll` 360F140AFC3F198F, `PBDMP.Network.dll` 71E8B7CD12EB7F5D). The installed DLLs and the zip entries match the tested pair. The previous install and package are kept privately for rollback. No commit or push. Full evidence and limits are in the [Survival handoff](../survival.md#phase-6-handoff-october-18-2026).

| Check | Result |
| --- | --- |
| Compile / managed | 0 warnings; 38 network scenarios + 8 reporting checks. |
| Survival fixtures, all three base arenas | `-Survival` 29, `-SurvivalWaves` 30–33, `-SurvivalPrep` 47–48, `-SurvivalResults` 22, `-SurvivalJoin` 23, `-SurvivalFailures` 16 checks on each arena; all pass. |
| Survival soak (Suburbs) | 24 checks. 15 waves, 321 pooled respawns, managed ledgers bounded by the live pool, no frame over 100 ms, kill and request bursts bounded, high score/kills/timer exact on both PCs and records. Comparison figures are in [performance](performance.md). |
| Arena join input | Main-menu and already-loaded-arena joins pass, 10 and 11 checks. The loaded-arena join failed intermittently (3 of 8 runs) during hardening; see the handoff. |
| Campaign regression | Travel 54, `-Saves` 42, `-JoinInput` 10, `-Actors` 597, `-State` 3,775 pass. `-ActorParts` (FakeCitizen) fails at the same point as baseline `d511ea1` on this installation. `-Doors` (19, three runs) and `-Combat` (28) pass after the [fixture fixes below](#door-and-loot-fixture-fixes--october-18-2026). |

Fixture actions are native calls, not natural combat; the [Survival two-player checklist](#survival-two-player-checklist) lists what a natural playtest should confirm.

## Identical players (shared items) — October 19, 2026

Protocol 13, on an isolated test copy, not installed. See the [redesign plan](../coop-redesign.md#phase-12-handoff-october-19-2026).

| Check | Result |
| --- | --- |
| `-SharedItems -Level LEVEL_AD_Prison` | 17 checks: shared and personal classification; the team weapons match; P1's new gun reaches P2 with its ammo and a native notice; a P2 key pickup gives both the key and removes the pickup on both; an owned gun gives the partner nothing; a dead partner receives at once and keeps it through the respawn. |
| `-Doors` | 20 checks; the key door uses the team key once and both players hold the same count. |
| `-Saves` / `-SaveResume` / `-SaveReplacePartner` (Neighborhood) | 42 / 21 / 22; a different player joins without a prompt, with fresh personal pickups. |
| Default travel, `-Combat`, `-JoinInput`, `-SurvivalPrep` (Suburbs) | 54, 28, 10, 47. |

Fixture pickups use native `Interact`; natural walk-over pickups are a maintainer check.

## Door and loot fixture fixes — October 18, 2026

Build **D5F77CFC3F12EBFC** (`PBDMP.dll` 287488ACAEF342A5, `PBDMP.Network.dll` 6F0B1AFF1BA59FC8), run on an isolated test copy. Not installed.

- **`-Doors` (DeathStar proximity close).** DeathStar shows a native tutorial that waits for Use. On P1 that tutorial is an intended shared pause: time scale 0 and the session frozen. The sliding-door tween could not finish, and `TickDoorProximity` skips frozen sessions, so the fixture waited forever. Once the tutorial is dismissed, PBDMP's proximity close runs as designed. The fixture now dismisses native tutorials, as `-Combat` already did. No product change was needed.
- **`-Combat` (pre-join loot ledger).** P1's ledger correctly includes native drops made before hosting began, which the hosting rescan of `LootController` picks up. The fixture expected exactly one drop. It now checks that P2 receives one personal copy of every entry in P1's ledger, and that each entry exists exactly once on P2, with no native duplicate.
- **Robustness fix.** The native optimizer can tick a `PhysicalObject` that a scene unload has already destroyed. The native body then throws inside `get_Rigidbody`. PBDMP's Harmony prefix on `PhysicalObject.OnOptimizeUpdate` turned that into a trampoline error, which failed one `-Doors` run during P2's Prison→DeathStar unload. The prefix now skips destroyed objects. The same native error from unpatched `Pickup`/`Npc.OnOptimizeUpdate` appears in 4 of 1,242 retained Unity logs. This fix is reasoned from the stack; the rare unload timing was not reproduced on demand.

| Check | Result |
| --- | --- |
| Compile / managed | 0 warnings; network and reporting checks pass. |
| `-Doors` | 19 checks, 3 of 3 runs. |
| `-Combat` | 28 checks. |
| Default travel, `-Props -Level LEVEL_Asylum_CoronvirusDen`, `-Scenery` | 54, 66 and 19 checks. |

## Weapon-animation pass — October 16, 2026

Installed/exported **0.10.0 / protocol 11 / save schema 2**, build **C990AE9ED22FC33F**. Installed plugin/network hashes match build artifacts and independently hashed ZIP entries. Previous DLLs, install manifest and ZIP are retained privately for rollback. No commit or push.

| Check | Result |
| --- | --- |
| Compile / managed scenarios | Zero compiler warnings/errors; 34 network scenarios + 8 reporting checks pass. |
| Full native render matrix | `.local/weapon-release-20261016-144901`: 27 available regular/extra/dual variants, 23 samples each, 621 PNGs; primary/support grip < 0.035 m, grounded crouch foot < 0.01 m, no gameplay scripts/colliders. Saw before/after and all-weapon contact sheets reviewed. |
| Connected native presentation | `.local/travel-20261016-144937`: 10 base-campaign weapon equips in both directions; native pistol/shotgun/saw attacks; eight explicit action/motion transport fixtures; pause stops and resumes clip clocks. |
| Runtime smoke | Native menu/avatar/material/mesh/effect checks pass on the same build, including resource-unload survival, event-free animation and interpolated aim. |
| Avatar update microbenchmark | Saw idle, 1,000 calls: mean 0.10426 ms, p95 0.10550 ms, max 0.56730 ms, zero managed allocation. Excludes construction, camera rendering and whole-world sync. |
| Retained Area69 budget/input | `.local/travel-20261016-145157`: host world p95 3.067 ms (<4); sweep p95 52.826 ms (<200). Main-menu join, shared pause, F8/local menu and gameplay input pass. |
| Package | `PBDMP-0.10.0-animations-C990AE9ED22FC33F.zip`; both players need this matching DLL pair. |

The connected action-state fixtures are explicitly synthetic except the native equips and three native attack families; they do not certify every natural alternate mode. Internal pump/bolt/barrel/string deformation parity and complete player-death animation remain polish. Existing broad campaign/manual acceptance is unchanged. See [weapon-animation testing](weapon-animation-testing.md) for commands and asset boundaries.

Earlier failed diagnostic iterations are not acceptance: ForceAttack was a native no-op; the saw uses its own press/release flow; dual bow initially constrained the wrong shoulder; partial armed clips needed a reset/native idle base to prevent procedural accumulation; crouch needed grounded feet with preserved ankle rotation. Area69 attempt 145104 used an invalid map name and was corrected to `LEVEL_Asylum_Area69` without changing assertions or production code.

## Megapass implementation closeout — October 16, 2026

Installed/exported **0.10.0 / protocol 10 / save schema 2**, build **E19C56EB3B893F6A**. Both installed DLLs and the independently hashed ZIP entries match the build artifacts. The previous install/package was retained privately for rollback. No commit or push was performed.

| Closing check | Result |
| --- | --- |
| Already-loaded Prison join | Reproduced the falling-platform missing-reference disconnect; native same-map restart fixes it. Newer same-map admission metadata reuses an unfinished load. Final native join, keyboard/mouse, pause and menu cleanup pass. |
| Runtime paw damage | Reproduced P2 applying local duplicate damage outside the authored catalogue. P1-only runtime guard passes with no native damage immunity hiding the result. |
| Stale hazard contacts | Reproduced an extra incoming hit from a native contact ledger after P2 left its volume. Current-transform penetration revalidation rejects it; genuine overlap and the paw each deliver one expected native hit. |
| Hazard pause / victim ownership | P2 damage leaves P1 unchanged; native shared pause delivers no extra hits. Native contact producers retain their own eligibility/layer rules. |
| Full restored elevator | Strict live ride, shared pause, disk restore and whole-journey survival pass. Final platform travel is about 76.6 units with about 0.058 units relative drift; death/respawn latch stays enabled. |
| Forest bridge | Native activation, every authored bridge transform, shared pause, rollback and completed state pass. This fixture invokes native Fall directly; natural P2 trigger/crossing/rejoin is in the playtest checklist. |
| Backyard wheel/cart | Actual driver is `OBJECTIVES/Ferris_wheel_Base/Ferris_wheel`, identified through its native cart-completion callback. Driver and linked native cart-door state pass pause/rollback/future completion. Actual button/riding flow remains manual. |
| Scenery / props | Partial damage, destruction, disk rollback, further advancement and current-world rejoin pass. Native spawner/pooling/push/restore/rejoin passes in CoronavirusDen. |
| Persistent saves | Live capture/restore and fresh-process P1-alone resume/P2 join pass, including wall geometry, retained personal pickups/drops, gear catch-up and usable controls. |
| Shared story | Final native host-only skip, waiting admission, both-player controls and ending/credits pass. |
| Performance budget | Final controlled Area69 check: **3.068 ms** host-world p95 and **44.834 ms** sweep p95 against unchanged 4/200 ms limits at a 120 FPS cap; join/pause input checks pass. Active combat/transport stress remains separate. |
| Managed checks / compile | **33 network + 8 reporting** checks pass; latest source compilation has zero warnings/errors. No wire or save schema change. |

The hazard fixture uses real native component methods after catalogue construction. It tests both a stale ledger entry and an actually overlapping capsule/volume. Its first positive-control attempt incorrectly imposed the global layer matrix on the game's native contact producer; the final check preserves native contact eligibility and retains exact hit-count/health assertions. The prop fixture requires a native spawner source; Neighborhood lacks one, so the supported CoronavirusDen fixture is used. The runner now catches initialization failures promptly and reports an unsupported prop fixture clearly.

Repeat targeted checks from the repository root, with `$GameDir` pointing to your isolated test installation:

~~~powershell
./scripts/Test-Travel.ps1 -GameDir $GameDir -JoinInput -JoinFromLevel -Level LEVEL_AD_Prison
./scripts/Test-Travel.ps1 -GameDir $GameDir -WorldPath Hazard -Level LEVEL_Asylum_Area69
./scripts/Test-Travel.ps1 -GameDir $GameDir -Mechanisms -Level LEVEL_Asylum_Area69
./scripts/Test-Travel.ps1 -GameDir $GameDir -Mechanisms -Level LEVEL_Asylum_Forest
./scripts/Test-Travel.ps1 -GameDir $GameDir -Mechanisms -Level LEVEL_Asylum_Backyard
./scripts/Test-Travel.ps1 -GameDir $GameDir -Scenery -Level LEVEL_AD_Neighborhood
./scripts/Test-Travel.ps1 -GameDir $GameDir -Props -Level LEVEL_Asylum_CoronvirusDen
./scripts/Test-Travel.ps1 -GameDir $GameDir -Saves -Level LEVEL_AD_Neighborhood
./scripts/Test-Travel.ps1 -GameDir $GameDir -Saves -SaveResume -Level LEVEL_AD_Neighborhood
./scripts/Test-Travel.ps1 -GameDir $GameDir -Stories -Level LEVEL_AD_Neighborhood
./scripts/Test-Travel.ps1 -GameDir $GameDir -Performance -PerformanceFrameRate 120 -Level LEVEL_Asylum_Area69
~~~

The final candidate reran Prison admission, hazards, full Area69 riding, story and performance. Forest/wheel/props/save checks also passed earlier in this closeout; later production changes only add hazard authority/contact guards. The remaining natural routes, fragment classification, interruption cases and busy-world/audio matrix went to the maintainer's playtest. The implementation handoff leaves those explicit rather than claiming exhaustive campaign acceptance.

## Recorded checks

| Area | Recorded result | Practical limit |
| --- | --- | --- |
| Release compilation | Zero warnings/errors for the 0.9.0 save-phase build. | Requires matching game-generated interop. |
| Network acceptance | 22 executable scenarios pass over TCP and an in-memory message carrier, including 12,288 ordered mission frames under bounded-queue pressure on each, and a stated drop reason surviving ten consecutive closes. | The earlier transient loading-failure-reason check was a race between the closing peer's send failure and its pending Reject; the session now waits briefly for the stated reason. |
| Steam (single account) | The probe passes on the real Steam client: callback tap, identity, native struct layouts, 300 ordered frames and a drop reason over a Steam socket pair, a P2P dial into the local listen socket (status callbacks, admission, accept), and private lobby create/data/leave. The game's own leaderboard call results still arrive with the tap installed. | Steam cannot relay between two sessions of one account. A first two-account session joined the friends lobby and connected over the relay twice; the rest of the checklist below is still open. |
| Native UI/player presentation | Lobby panel, Direct IP, Co-op Saves and prompt layers; save-dialog navigation/Escape, main/pause entries, dialog ownership, per-layer navigation and Escape order; model rendering and native clips exercised. | Isolated renders and callback checks measure presentation; physical controller input is a separate test path. |
| Travel and input | Two game processes exercise host travel, readiness, respawn, late join and overlapping pause/menu locks. | Focus round trips cover a reproduced input lock, not every intermittent Alt-Tab failure. |
| Combat and actors | Native attacks, host targeting/hits, personal incoming damage, actor lifecycle and special targets exercised. | All natural attack/boss sequences still need acceptance. |
| Actor catalogue | 49 NPC families exercised in fixtures; 43 authored families across 243 map/family rows inspected. | Fixture and authored-census counts measure different coverage. |
| Doors and encounter state | Native requests, keys, reload, late join, P2 encounter activation and group progression exercised. | Unclassified authored variants remain possible. |
| Urine | Electrical boxes, tanks, toilet state, extinguishing, exposed weak-spot damage and supported late join exercised. | Natural rail/rocket/puzzle sequences are not all complete acceptance tests. |
| Neighborhood house wall | P1/P2 activation removes the actual obstruction's renderer/collider; both controllers pass through, including after rejoin. | Does not establish general decorative destruction support. |
| Level endings | Prison walk-in and Wallmarkt interactable endings exercise gathering/cancellation/one finish; automatic endings wait for respawn. | Representative ending fixtures, not every mission ending. |
| Campaign census | All 15 base maps inspected for supported catalogue/bounded-state serialization. | A long sequential audit hit native NPC initialization errors; remaining maps passed fresh-process checks. |
| Co-op save files | Atomic replacement, previous-generation recovery, corrupt primary/backup errors, schema/slot/event bounds and strictly ordered save pages pass. | Data integrity checks do not authenticate Direct IP users. |
| Native co-op restoration | Two processes restore NPC health, host resources, door state, structural wall geometry, personal authored pickups and generated loot; three autosaves rotate with a recovery backup, and checkpoint rollback keeps the connection. | Neighborhood is the controlled native save fixture; other natural campaign save points should continue to receive regression coverage. |
| Fresh-process resume and partner identity | P1 restores alone, retains the absent record, and restores the joining partner. A replacement guest waits for explicit native confirmation before receiving the saved personal slot. | Direct IP uses a local persisted ID; Steam uses the connected account. |
| Completed-map saves | Both players restore native level-complete screens with matching counts and exact completion time. | Representative completion fixture. |
| Host timing | 0.9.0 controlled 60 FPS fixture: about 3.1 ms p95 world work, 66.7 ms p95 sweep and 16.7 ms p95 frame time; persistence service p95 0.004 ms. | Stationary two-instance diagnostic, not a hardware-independent benchmark. |

Fixed during 0.8.0 two-player testing: P2 loaded Neighborhood, logged `Authored enemy/door catalogue mismatch` and returned to the menu (P1 762 authored components, P2 754). The game's distance optimizer switches far pickups off shortly after a map loads. The catalogue's "never share pickups, NPCs, players or UI" exclusion used Unity's default parent lookup, which skips inactive objects, so an inactive pickup's `InteractListener` entered the catalogue. Whether it did depended on when each peer built its catalogue and where its player stood. The exclusion now includes inactive parents. Building both variants at the same moment on all 15 maps showed the only removed components are on `ItemPickup`/pickup objects (274 rows: ammo, health, weapons, keys and carried quest items such as levers and winches), plus mission-flag changes on enemy groups those listeners referenced. The earlier Prison 559/563 travel-test failure had the same cause; the default, Neighborhood, mission, combat and Prison state travel runs now pass. Each catalogue build writes `BepInEx/PBDMP/catalogue-<scene>.tsv` so two players can diff their catalogues.

The [sync tracker](sync-progress.md) records feature-specific gaps.

## Explosion, urine, hit-effect and completion regressions

The wall failure was reproducible with a stronger native geometry comparison: P2 hid the replacement hierarchy, removing the permanent wall around the opening as well as the loose fragments. The destruction adapter now keeps the authored replacement mesh and static collision, while hiding cosmetic debris. The Neighborhood fixture compares the remaining active structural surfaces and tests actual player passage, including late join. Hidden/pooled debris is excluded from structural comparisons.

Special explosion replay passed a null managed wrapper for IL2CPP's nullable rotation parameter. Supplying an explicit empty IL2CPP nullable avoids the reported marshalling exception. Explosive-barrel hits and world flame ticks now resolve on P1, where native explosion listeners advance linked mission geometry. The Prison regression requires the actual `OBJECTIVES/Pipe_hole` to open, compares its active geometry on P2, and verifies both players remain connected after native explosion replay and a separate P2 barrel hit.

NPC VFX callbacks previously relayed decals but omitted native material impact bursts. The host now publishes the selected flesh/armor particles and positional audio for harmless client presentation. The native regression checks accepted P2 damage and actual emitted hit particles.

A host-authorized finish now publishes final level statistics after the native game gathers them. P2 runs the native completion presentation after readiness, with host result values and exact completion time. The exit regression checks the native screen, matching statistics and rejoining a completed map; P1 retains restart/next-level authority.

## Co-op save regression

Use `-Saves -Level LEVEL_AD_Neighborhood` to run the native persistence fixture. It uses isolated diagnostic storage, captures distinct player pickup histories and a generated drop, destroys the authored mission wall, damages an NPC, and changes a door. It then changes the live state and reloads, comparing native health/resources, collection history, loot and wall geometry on both peers. The fixture also invokes a native P2 checkpoint component, checks that the request is validated without an early capture, advances the diagnostic gameplay clock across the interval, rotates autosaves, rolls back a checkpoint and reloads a completed map's results. The isolated checkpoint component avoids invoking unrelated authored mission callbacks; natural checkpoint placement is separate campaign coverage.

Run `-Saves -SaveResume` afterward to start fresh game processes against that fixture's files. P1 restores before P2 joins. Adding `-SaveReplacePartner` gives only the diagnostic guest a temporary test identity, verifies the hold before assignment, and invokes P1's native confirmation control. Production identity files are not replaced. `-SaveUi` shows the host window and captures the native save browser for visual inspection; normal test windows stay hidden.

Native restoration needs the game's own asynchronous `ISaveable.SaveAsync` contract, NPC despawn before restore and the native same-map restart path. These preconditions were verified while correcting partial initialization and stale scene references. Persistent capture adds the native baseline to the existing durable world adapters; general decorative live synchronization and unclassified script callbacks retain their existing checklist status.

## Running checks

Use a compatible installation with BepInEx-generated interop and the current plugin installed. Run from `<RepoDir>`, with `$GameDir` set to your own game folder. Close existing game processes before native tests.

```powershell
# Compiles/packages and runs the network acceptance executable.
.\scripts\Build.ps1 -GameDir $GameDir

# Native UI/model smoke test; does not enter gameplay.
.\scripts\Test-Runtime.ps1 -GameDir $GameDir
# The Multiplayer menu, co-op Load/Save screens, Join a friend and Players & invite (one game, Direct IP).
.\scripts\Test-Runtime.ps1 -GameDir $GameDir -CoopMenu

# Steam path on one account; needs the Steam client running. Creates and leaves a private lobby, and checks
# Join a friend against it (lobby data, the friend scan, the list, Join's prompt, Direct IP...).
.\scripts\Test-Steam.ps1 -GameDir $GameDir

# Two-process campaign lifecycle and focused regressions.
.\scripts\Test-Travel.ps1 -GameDir $GameDir
.\scripts\Test-Travel.ps1 -GameDir $GameDir -Combat
.\scripts\Test-Travel.ps1 -GameDir $GameDir -Doors
.\scripts\Test-Travel.ps1 -GameDir $GameDir -DoorReload -Level LEVEL_AD_Neighborhood
.\scripts\Test-Travel.ps1 -GameDir $GameDir -SharedItems
.\scripts\Test-Travel.ps1 -GameDir $GameDir -ActorParts -Level LEVEL_AD_Neighborhood
.\scripts\Test-Travel.ps1 -GameDir $GameDir -State -Level LEVEL_AD_Prison
.\scripts\Test-Travel.ps1 -GameDir $GameDir -Urine
.\scripts\Test-Travel.ps1 -GameDir $GameDir -Mission -Level LEVEL_AD_Neighborhood
.\scripts\Test-Travel.ps1 -GameDir $GameDir -WorldRegression -Level LEVEL_AD_Neighborhood
.\scripts\Test-Travel.ps1 -GameDir $GameDir -WorldRegression -Level LEVEL_AD_Prison
.\scripts\Test-Travel.ps1 -GameDir $GameDir -MissionExits -Level LEVEL_AD_Prison
.\scripts\Test-Travel.ps1 -GameDir $GameDir -MissionExits -Level LEVEL_AD_Wallmarkt
.\scripts\Test-Travel.ps1 -GameDir $GameDir -Performance -PerformanceFrameRate 60
.\scripts\Test-Travel.ps1 -GameDir $GameDir -Saves -SaveUi -Level LEVEL_AD_Neighborhood
.\scripts\Test-Travel.ps1 -GameDir $GameDir -Saves -SaveResume -Level LEVEL_AD_Neighborhood
.\scripts\Test-Travel.ps1 -GameDir $GameDir -Saves -SaveResume -SaveReplacePartner -Level LEVEL_AD_Neighborhood

# Fresh-process actor/catalogue census across the 15 campaign maps.
.\scripts\Test-ActorCampaign.ps1 -GameDir $GameDir

# Survival co-op (base arenas: LEVEL_Horde_Suburbs, LEVEL_Horde_Ufo, LEVEL_Horde_VipZone; default Suburbs).
.\scripts\Test-SurvivalInventory.ps1 -GameDir $GameDir                       # offline arena inventory, no session
.\scripts\Test-Travel.ps1 -GameDir $GameDir -Survival -Level LEVEL_Horde_Ufo  # entry, travel, guards, retry, host loss
.\scripts\Test-Travel.ps1 -GameDir $GameDir -SurvivalWaves                    # host-owned waves, enemies, spawn safety, pause
.\scripts\Test-Travel.ps1 -GameDir $GameDir -SurvivalPrep                     # offers, readiness, revival, spectator, wipe
.\scripts\Test-Travel.ps1 -GameDir $GameDir -SurvivalResults                  # P2 HUD, combo damage, results, records
.\scripts\Test-Travel.ps1 -GameDir $GameDir -SurvivalJoin                     # combat-time join hold, dead reconnect
.\scripts\Test-Travel.ps1 -GameDir $GameDir -SurvivalFailures                 # disconnects in countdown/admission/results/combat
.\scripts\Test-Travel.ps1 -GameDir $GameDir -SurvivalSoak                     # comparison, soak (-SurvivalSoakWaves 8-200, default 15), bursts, high values
```

Additional `Test-Travel.ps1` modes include `-Actors`, `-UrineParts`, `-JoinInput`, `-FocusInput` and `-Audit -AuditLevels LEVEL_Name`. `Test-CombatProbe.ps1` checks a native P2 targeting/hit proxy. Inspect each script's parameter block before combining diagnostic modes.

Tests create ignored output under `.local`, block profile writers with diagnostic flags, and stop only processes they own. A Steam install started directly would relaunch itself through Steam and exit, so the scripts set `SteamAppId` for the processes they start; the Steam client must be running. Travel tests create a second loader/executable directory using hardlinks/junctions to the reader's installed game; they require a compatible Windows filesystem. Most windows are hidden. Unattended travel diagnostics keep the desktop cursor unlocked and verify that it stays free; `-FocusInput` deliberately retains native cursor behavior to exercise real window focus and mouse input. Game/platform profile access and graphics initialization must work in the environment used for testing. Hidden automation is not the normal player launch path.

## Two-player Steam checklist

Completed by the maintainer on October 18, 2026, on two PCs with two Steam accounts.

Use the same `Package.ps1` zip on both PCs and two Steam accounts that are friends and own the game.

- P1 hosts from the main menu; the lobby panel shows P1's name and avatar. **Invite friend** opens the Steam overlay; P2 accepts from the main menu and joins without a question.
- P2 instead uses **Join Game** on P1's name; with the lobby set to invite only, Join Game is not offered.
- P2 opens **Multiplayer → Join a friend**. P1 is listed with P1's avatar, where P1 is (menu, map or Survival) and "1 of 2 players". **Join** connects. An invite-only lobby is not listed.
- With P2's game closed, **Join Game** on P1's name starts the game and joins once the main menu appears.
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

## Survival two-player checklist

Completed by the maintainer on October 18, 2026, on two PCs over Steam.

Natural play that the fixtures do not certify (they kill enemies, end waves and ready players through native APIs). Use the same package on both PCs; repeat on Suburbs, Ufo and VIP Zone where practical.

- P1 opens **Survival Mode** while hosting, picks an arena and each difficulty; P2's Survival button stays unavailable and P2 follows into the arena.
- Both players take the start shotgun; the 3-2-1 countdown and wave start once on both HUDs. Hold **Use** for a second to ready without a pickup (keyboard and controller).
- Fight a full natural wave: both players' kills score, P2 getting hit drops the shared combo, enemies chase whichever player is nearer, nothing spawns on top of P2.
- At REST, take the same offer and different offers; each player keeps their own pickups and resource spawns; the wave waits for both.
- One player dies: the spectator view follows the partner with the waiting notice, the dead player cannot pick anything up, and revives at the next REST with full health, zero armor and their weapons. Check whether the native HUD stays visible while spectating.
- Both die: both PCs show the same results (score, kills, waves, time, best weapon); only P1 can retry or leave; the co-op best appears after retry.
- P2 joins mid-wave (held with the waiting notice, admitted at REST with P1's weapons); P2 dies and reconnects (held until the following REST); P2 leaves during the countdown; P1 leaves mid-wave.
- Ufo: breakable water capsules and the teleport behave the same for both players. VIP Zone: jump pads and glass. Suburbs: jump pad, barrels and the kill plane.
- Play several long waves (wave 10+), watching frame time on both PCs; a natural ClownElemental summon and an unreachable enemy being cleared at CLEANUP.
- Through Steam with a friend: the same join, death-rejoin and results checks.

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

## October 15 save/input follow-up

The native live-save test passes pause-menu restoration, per-player pickup/drop isolation, damaged NPC/door/wall restoration, three rotating autosaves plus backup, checkpoint rollback, completed-map results, and saving/loading overlay state/cleanup on both peers. Running the focus variant exposes the owned test windows for rendered-overlay inspection:

```powershell
./scripts/Test-Travel.ps1 -GameDir $GameDir -Saves -FocusInput -Level LEVEL_AD_Neighborhood
```

Diagnostic clock advancement simulates the five-minute boundary without waiting in real time. Repeated native checkpoint and autosave calls remain idle before it. Managed `AutosaveClock` tests separately verify pause exclusion, reset, invalid inputs and no catch-up write storm.

A progressed-save fixture also reproduced the native input-mask bug and passed after pairing the personal-load lifecycle callbacks. P1 resumes alone; P2 then joins, follows five real focus round trips, and physically moves/turns with real WASD/mouse input. Private fixtures, identities and captured logs remain outside source control. The standard generated live-save test above covers the same player restore adapter without requiring private fixtures.

The warning/error review distinguishes unresolved presentation/loader warnings from session failures. Repeated missing remote geometry warnings are limited per avatar. Native FMOD MusicOffset and stale-target DOTween warnings, and IL2CPP warnings about unsupported diagnostic signatures, are retained for further investigation. The runtime catalogue drift was later reproduced and corrected in the world/presentation follow-up below; incompatible originals remain preserved and recovery requires a fully compatible previous backup. No global warning suppression or blanket acceptance of mismatched saves was added.

## Player input after restoration

The native player-spawn serializer calls `Player.OnLevelStart` even when restoring personal state into an already loaded map. Co-op restoration completes that lifecycle with `Player.OnLevelStartReady`, restoring the player view and releasing the native input mask. Multiplayer loading/capture barriers and any active personal tutorial retain their own locks until ready. Assigning a replacement partner also closes a multiplayer panel opened solely for that confirmation; an already open panel remains under the user's control.

## Current world/presentation playtest

Build **F68FE66F1FC90AA7**, version **0.10.0 / protocol 10 / save schema 2**, is installed and packaged as `PBDMP-0.10.0-playtest-F68FE66F1FC90AA7.zip`. Both DLLs match the installed host and diagnostic peer byte-for-byte. The ZIP contains only those DLLs, MIT LICENSE and installation notes. No commit or push is included. This supersedes the previous save/input artifact; the whole megapass remains unfinished.

The first acceptance-closing batch adds native `Test-Travel -WorldPath` fixtures. `Secrets` exercises real trigger entry, shared counts, duplicate callbacks, rollback/rediscovery, discovered disk restoration and late join. `Barrel` captures an eight-second native chain after two elapsed seconds, checks the saved remaining delay, restores without an old explosion, holds its fuse through shared pause and observes one future native result. Its event observer is removed before reload so the fixture itself cannot duplicate callbacks.

`Teleport` uses DeathStar's authored button and its sibling listener. P2 has actual nonzero native movement before the request. An opt-in observer compares the native destination/view and queued `VelocityToApply` with the transformed incoming momentum; `CharacterController.velocity` is the previous Move sample immediately after a warp. P1 never teleports. Native tutorial ownership remains active and the fixture dismisses it through its normal Use callback.

`Cannon` covers native P1 aiming, nearest-P2 aim, visible shell before impact, actual P2 damage, unchanged P1 position/health and blocked client cannon simulation. Before the fix, the real shell hit P2 invisibly because `RegisterNativeProjectile` rejected its null actor owner. Registering known-prefab ownerless shots as host scenery attacks exposed a missing `Projectile_Cannon` visual audit. Read-only serialized inspection of its six particle systems passed collision-force/messages, trigger, internal sub-emitter and stop-action checks. The allowlist was regenerated with The Wall data while retaining every previous accepted entry. Copies still strip gameplay scripts/colliders and run only their native presentation.

A final pending-barrel rerun exposed a separate live-state publishing bug: `CapturePersistent` reused the sender's delta caches while `Send` diverted all frames into the saved list. A change captured before its normal broadcast was then considered already sent and could remain missing on P2 indefinitely. Durable capture records now continue through the bounded live queue as well as the snapshot list. Transient/historical effects remain excluded from capture. The native fixture reproduces this at the barrel's future explosion and requires P2 to receive the changed state without a second mutation or rejoin; the full save suite protects personal pickup/resource isolation and native restoration.

The fresh-process Hospital audit caught a separate pre-ready actor tick: `GoliathMovementComponent.OnUpdate` called `GoliathLeg.ForceMove`/`UpdatePosition` while native references were not ready. `SharedWorldPatches.BlockSimulation` defers NPC/AI update callbacks while a multiplayer level is not natively ready. State-entry/exit initialization keeps its previous authority guard, and offline updates remain native. The same Hospital audit passes without suppressing errors or freezing its normal post-ready simulation window.

Three repeated strict Prison runs pass. The earlier intermittent Point Light (4) difference came from a native `ControlPanel.OnUpdate` flicker timer: the assertion compared a sampled host light against a later live frame. The strict world fixture now pauses both games, samples the settled host and allows state delivery before its unchanged exact comparisons. It resumes before the live explosion/shot assertions. There is no progression-only waiver in these runs.

```powershell
./scripts/Test-Travel.ps1 -GameDir $GameDir -WorldPath Secrets -Level LEVEL_AD_Neighborhood
./scripts/Test-Travel.ps1 -GameDir $GameDir -WorldPath Barrel -Level LEVEL_AD_Prison
./scripts/Test-Travel.ps1 -GameDir $GameDir -WorldPath Teleport -Level LEVEL_NLM_DeathStar
./scripts/Test-Travel.ps1 -GameDir $GameDir -WorldPath Cannon -Level LEVEL_AD_TheWall
./scripts/Test-Travel.ps1 -GameDir $GameDir -WorldRegression -Level LEVEL_AD_Prison
```

These are targeted native fixtures. They do not establish full natural campaign-route, specialist hazard or failure-recovery acceptance. The remaining list in `megapass.md` remains authoritative. Raw failing/passing logs and asset-audit reports stay ignored.

The authored-catalogue failure was reproduced from an affected progressed save. Seven detached native corpse pieces had become independent roots without a clone suffix or pool source, and were admitted as map props when rebuilding. Recording safe authored physical components at scene load keeps the original map catalogue across deaths, restoration and rehosting, while retaining legitimate unsaved swing bones. Scene membership is released on unload and never sent over the network.

A mismatching existing primary can recover only a previous backup whose schema, game, map and full catalogue match. The user is told that this is an older recovery generation. Tests preserve both original files, reject incompatible recovery candidates and protect a compatible backup against promotion of the mismatching primary. No missing state is invented.

The strict Hospital urine/live/late-join fixture now passes. Native rotation/fill callbacks no longer overwrite host-owned replica surfaces; disabling interpolation on client-owned kinematic bodies also prevents a correct physics pose from displaying an older local transform. Original interpolation is restored on cleanup. Full renderer, collision, activation and pose assertions remain in place, using a paused current host reference rather than a prop sampled earlier while settling.

| Regression | Verified behavior |
| --- | --- |
| Preserved progressed-save replay | Compatible backup recovery, stable catalogue rebuild and same-map rehost, both-player controls, real P2 WASD/mouse movement after five focus round trips. |
| Native Hospital urine/world fixture | Tank endpoints and completion, linked machinery, physical prop poses, toilet/flush/fireball paths and current state after rejoin; no historical effects replay. |
| Posters | P2 collection, shared removal and result count, duplicate prevention, disk restoration and current-world rejoin. Resource pickups remain personal. |
| P2 host pause | Native pause screen and Host has paused caption, blocked P2 resume, automatic host-owned screen closure, retained independent P2 menu and working input. Rendered output inspected. |
| Neighborhood garage | Correct permanent cutout/collision, authored occlusion portals before crossing, native controller passage and rejoin. P2 fuse completion hides only P1's exact finished trigger hint. |
| Native flesh impacts | Native emitter counts retained, actual native material selection reaches P2 with emitted particles, P2's local hit is immediate and its echo is ignored; damage remains host-owned. |
| Prop lifetimes | Native pushes, spawn/pool lifetimes, save restoration and current-world rejoin in the COVID map fixture. |
| Save/input suite | Native pause-menu save/load, timer and rotating autosaves, pickup/drop isolation, NPC/door/wall rollback, completed results, hold overlays and real focus/input regression. |
| Area69 machinery/rider regression | Live and restored rider drift/alive samples, shared pause, native mover/rotater/elevator disk restoration, future endpoints and single completion callbacks. Natural full-route obstruction coverage remains separate. |
| Refreshed campaign catalogue | All 15 base maps pass separate-process discovery and bounded-state serialization on the final build. This is catalogue coverage, not natural route completion. |

Managed acceptance: **31 scenarios passed**. Release compilation: **zero warnings/errors**. The native tests are targeted regressions, not natural completion of every campaign route. The outstanding matrix in [megapass.md](../megapass.md) remains authoritative, including the separately recorded Prison light-only mismatch.

```powershell
./scripts/Test-Travel.ps1 -GameDir $GameDir -Presentation Posters -Level LEVEL_AD_Neighborhood
./scripts/Test-Travel.ps1 -GameDir $GameDir -Presentation Pause -FocusInput -Level LEVEL_AD_Neighborhood
./scripts/Test-Travel.ps1 -GameDir $GameDir -Urine -Level LEVEL_Asylum_Hospital
./scripts/Test-Travel.ps1 -GameDir $GameDir -Mission -Level LEVEL_AD_Neighborhood
./scripts/Test-Travel.ps1 -GameDir $GameDir -Props -Level LEVEL_Asylum_CoronvirusDen
./scripts/Test-Travel.ps1 -GameDir $GameDir -Mechanisms -Level LEVEL_Asylum_Area69

# Exercise actual native blood material selection as well as progression.
$env:PBDMP_BLOOD_AUDIT = '1'
try {
    ./scripts/Test-Travel.ps1 -GameDir $GameDir -WorldRegression -Level LEVEL_AD_Neighborhood
} finally {
    Remove-Item Env:PBDMP_BLOOD_AUDIT
}

# Fresh-process catalogue and bounded-state audit, not a natural playthrough.
./scripts/Test-WorldCampaign.ps1 -GameDir $GameDir
```

The native platform pause fixture samples after the current Update frame finishes: a tween can still consume that frame's already sampled delta when pause is requested inside Update. Pose, clock and callback assertions retain their original tolerances.

The rider follow-up also reproduced a host-side timing problem: native elevator animation can advance after the shared world's Update has placed the remote player's hit surface. That surface now follows a validated current platform reference in LateUpdate before the next physics step. Client riders retain the final authoritative platform correction during pause, while travel/restoration rebase their caches. No native hazard is disabled. The restored-rider test waits for an actual received platform step with a finite timeout, retaining the original motion, alive and drift requirements. Full natural rides and external obstruction cases remain in the megapass closing list.

The extended Area69 diagnostic can still observe an independent respawn after its bounded rider samples while the restored native scene runs. Its passing samples/endpoints do not certify uninterrupted full-route safety; the source of those later hits and natural rider/hazard obstruction coverage remain in T02/T03/T13/T36. Preserve that distinction when extending the regression.

## Physical pose ownership follow-up

A saved generic display transform could replace a physical prop's world-space pose with a lagging interpolation sample. Offline comparison reproduced the measured offset from the saved frame order. The native prop regression now keeps an active moving authored body and deliberately offsets its duplicate display record by three units: the original restoration fails its pose assertion, and the corrected ownership/order passes on host and peer, through pool reuse and current-world rejoin. Native shifted-arena/syringe/cooldown restoration also passes. The first general save follow-up stopped at its personal health-pickup fixture before restore. The fixture now selects an item using native eligibility after setting health, waits gameplay time, and acknowledges a native tutorial when shown. Its complete native save/restore, pickup isolation, timer/rotation/backup, checkpoint, results and input/overlay checks pass; the strict Prison follow-up also passes. The intermittent combat-pause and rider cases remain open rather than waived.


## Contact and renderer follow-up — October 15, 2026

Both native Area69 conveyor controllers pass: Citizen_Air_Tube has 12 referenced listeners, and Hangar/Ceiling_Conveyor has 20. Checks compare activation, world poses, enabled solid colliders and native ray contacts through shared pause, disk restoration, future movement and current-world rejoin. A failed early comparison captured a reference before the final sampled-delta movement step; settled P1 and P2 matched exactly. The fixture now waits for that step without relaxing assertions.

Area69's gravity-zero AirTube passes personal contact entry/exit, grounded saved-position restoration, independent P1/P2 contacts and clean re-entry after joining. Hospital's actual pusher applies one native force to P2's movement, leaves P1 unchanged and does not independently simulate on P2.

The elevator investigation reproduced a 5,000-damage hit from P1's stale remote capsule entering the lethal underside with zero P2 movement input. Direct carrier parenting improves that path. Earlier complete restored journeys pass, but the latest extended rerun still detects a P2 death/respawn after the early sample. Native machinery endpoints pass; uninterrupted rider safety remains partial. Its queued pause and shared-story checks did not execute after failure and must not be reported as passing this batch.

Initial seeding and native renderer-instance registration replace recurring scene searches. Native beam, line and urine visuals reach the peer, including active objects with disabled controller components. Stream work drops from about 2.52 ms to 0.041–0.044 ms p95; Area69 sweep p95 improves from 934 ms to roughly 199–203 ms. Some runs still fail the unchanged 200 ms target. The maintainer reports substantial FPS/network problems; broad performance acceptance remains open.

The performance/input fixture now respects the existing rule that P2's own menu survives P1's pause/resume: it closes that menu explicitly and waits for the native fade before checking input. This changes the test contract, not production mouse/input behavior. Compilation has zero warnings/errors and all 31 managed network scenarios pass. These source follow-ups are newer than the unchanged C8C097B0726D204B exported playtest.

```powershell
$env:PBDMP_CONVEYOR_INDEX = '0'
try {
    ./scripts/Test-Travel.ps1 -GameDir $GameDir -WorldPath Conveyor -Level LEVEL_Asylum_Area69
    $env:PBDMP_CONVEYOR_INDEX = '1'
    ./scripts/Test-Travel.ps1 -GameDir $GameDir -WorldPath Conveyor -Level LEVEL_Asylum_Area69
} finally {
    Remove-Item Env:PBDMP_CONVEYOR_INDEX
}
./scripts/Test-Travel.ps1 -GameDir $GameDir -WorldPath Gravity -Level LEVEL_Asylum_Area69
./scripts/Test-Travel.ps1 -GameDir $GameDir -WorldPath Pusher -Level LEVEL_Asylum_Hospital
# Known intermittent failure: full restored rider survival.
./scripts/Test-Travel.ps1 -GameDir $GameDir -Mechanisms -Level LEVEL_Asylum_Area69
./scripts/Test-Travel.ps1 -GameDir $GameDir -Combat -Level LEVEL_AD_Neighborhood
```


## Main-menu news panel — October 16, 2026

Earlier build **7523135E5315F9B5** introduced the native-themed, main-menu-only news/reporting panel and was packaged as `PBDMP-0.10.0-news-7523135E5315F9B5.zip`. The structured-form follow-up below supersedes that installation; the existing eleven megapass acceptance areas remain open, and the progress score is unchanged.

Native checks cover text fit and non-overlapping menu bounds at 1280×720, 1920×1080, 1280×1024 and 2560×1080. The final wording explains the prefilled report. A subsequent 65%-scale adjustment passes the same native checks at 1920×1080; the other layout checks preceded this uniform size reduction. Pointer/controller callbacks select the intended URLs, an intercepted browser action verifies the editable issue draft, and navigation reaches both buttons and returns to the native menu. Settings, the multiplayer dialog and native loading hide the panel; final 720p/1080p checks also cover the exit confirmation prompt. Main-menu reentry restores one panel. No report is submitted or log uploaded by these checks.

Run the opt-in fixture against an installation with the current DLL pair, with other game instances closed:

```powershell
./scripts/Test-News.ps1 -GameDir $GameDir -Width 1920 -Height 1080
```

The fixture writes private screenshots/logs under ignored `.local`, intercepts browser launches, and stops its own process. The release build does not fetch a news feed. Compile checks report zero warnings/errors. Existing loader/interop warnings remain; no new PBDMP error occurs in the passing panel runs. These checks do not close the separate gameplay or performance acceptance gaps.


## Structured bug-report form — October 16, 2026

Current installed/package build: **5748151ED9710F76**, `PBDMP-0.10.0-reporting-5748151ED9710F76.zip`. The tested DLL pair matches the normal installation. The panel retains its 65% scale and now explains software/session prefill. No commit or push is included.

The local YAML form parses successfully, uses unique field IDs and valid field types, and matches the URL's `software` and `session` keys. Only the problem-description field is required by the form; GitHub requires the title. Eight managed reporting checks cover unavailable context, map replacement, new-session clearing, path/multiline rejection and encoded URL round trips. The normal build runs these alongside all 31 passing network scenarios; compilation has zero warnings/errors.

The native UI fixture verifies current software versions/build identifiers, bounded software-only prefill, fresh-launch unavailable context and native button/navigation/menu visibility. Browser launch is intercepted; no issue is submitted. A two-process Direct IP travel regression separately verifies real P1/P2 map/role/transport capture, replacement after next-level travel, retention at title and retention through P1 disconnect/P2 host-loss cleanup. A subsequent fresh-process news check confirms prior context is not persisted between launches.

**Publication dependency:** `.github/ISSUE_TEMPLATE/bug-report.yml` must reach the repository's default branch before the installed button's template URL can work on GitHub. Local validation does not verify the unpublished form in GitHub's live UI. The maintainer explicitly chose local installation before publication. No API credentials, automatic submissions or attachment uploads are used. Existing world/performance acceptance gaps and scores are unchanged.


## Focused FPS and stall hardening — October 16, 2026

The [performance results and measurement method](performance.md#before-and-after-results) record the controlled before/after comparison. Initial timing build `7106890D0DCAD31A` drops from approximately 120 FPS offline to 71.3 FPS on connected P1, with 72 frames above 50 ms in 25 seconds. Final timing build `8C014EA539801E69` measures 117.9 FPS on P1 and 118.9 FPS on P2, with 0/1 frames above 50 ms. The fixed FPS, p99, stall and host-work budget check passes; an earlier fixed repeat still shows occasional outliers, so broad performance acceptance remains open.

Microsoft runtime allocation traces identify repeated authored surface path/hash lookups and full actor/motion catalogue scans. Map-lifetime caches preserve the original native membership, actor identities and door-first motion ordering. The catalogue hash is unchanged. Framed application-byte counters are opt-in and tested; network framing/protocol version is unchanged. CPU/allocation savings do not imply lower bandwidth.

Native cache/reference checks pass on both peers. Area69 urine/machinery, toilets, fireball extinguishing and late join pass. Area69 world p95 is 3.077 ms and sweep p95 is 47.058 ms, under the unchanged 4/200 ms budgets, followed by passing join/pause/input checks. The last two checks use `0742FFD62A637F2B`, whose only changes after the timing build are diagnostic tutorial dismissal and settled-pause sampling. No production pause behavior or comparison tolerance changes. Builds report zero warnings/errors; 32 managed network scenarios and eight reporting checks pass.

Steam closed during profiling and later native startup attempts failed before the menu with Steamworks uninitialized. After the maintainer reopened the game through Steam, tracing/tests resumed successfully. A separate already-loaded Prison join calibration failed on missing authored breakable geometry; that path remains recorded for follow-up. No private trace or raw game log is included in the repository.
