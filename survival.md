# Survival co-op — phased implementation handoff

Updated October 18, 2026. Intended implementer: Claude Opus 5.5.

**Status: owner-approved plan; owner authorized implementation on October 16, 2026. Phases 0 (inventory), 1 (entry/travel), 2 (host-owned waves/actors), 3 (offers, readiness and death), 4 (HUD, results and records), 5 (late join and reconnect), 6 (hardening and campaign regression) and 7 (documentation, package and handoff) are complete: Survival co-op is implemented for all three base arenas.** This document holds the plan and the running implementation handoff. The owner requested that existing work be committed first: baseline commit **`d511ea1` — Complete campaign sync closeout and weapon animation pass**, on `main`. Nothing was pushed. Keep this new plan separate from that checkpoint commit.

## Start here

1. Read this document, `README.md`, `docs/protocol.md`, `docs/sync-progress.md`, `docs/co-op-saves.md`, `animation-pass.md` and `docs/megapass-playtest.md`.
2. Check the current branch/diff before editing. Preserve unrelated work. Do not reset, rewrite history, change Git identity, commit or push without the owner's instruction.
3. Work phase by phase. Update the status table and append a short handoff after each useful stopping point: current source state, checks run, remaining issue and exact next step. Do not call an untested path verified.
4. Preserve existing campaign behavior, saves, transports, animation work and performance protections. This is Survival support, not another general campaign megapass.
5. Do not launch games during planning. At implementation/test time, use isolated diagnostic installations. Never close a player's running game; install only when it is closed. Keep raw logs, captures, recovered game assets and local installation paths in ignored `.local/`.

Repository paths below are relative to `<RepoDir>`. `<GameDir>` means the owner's selected installation; `<Decompiled>` means independently recovered game research output, excluded from Git. Never copy recovered game code/assets into the repository. No personal machine paths belong in public documentation.

## Confirmed design

| Decision | Agreed behavior |
| --- | --- |
| Content | All built-in, non-DLC Survival arenas using the existing two-player Steam and Direct IP connections. Custom arenas, new modes, more players and balance changes are deferred. Arena names/count must be discovered from the game, not guessed. |
| Authority | P1 owns the run, enemies, spawning, shared combat/world results, wave state, score and timers. P2 sends bounded requests and presents authoritative results. Enemies can target either living player. |
| Resources | Health, armor, weapons, ammo, items, powerups and pickups remain personal. Each player can collect their own copy of resources and enemy loot. No friendly fire. |
| Wave pacing | Each preparation phase gives players their own offers. Picking an offer marks that player ready; an explicit Ready control allows retaining current gear. Both admitted players must be ready, then one host-owned countdown starts the wave. P1 alone is sufficient when playing alone. |
| Offers | P1 selects the offer set; both players see identical available choices, independently collectible. They may select the same or different weapons. One choice must not remove the partner's choices or start two waves. Preserve native offer sizes/selection rules after inspecting the actual arena. |
| Death | A dead player waits until the next preparation phase, then revives with full health, zero armor and retained weapons/ammo. Restore availability early enough to choose an offer. Do not run campaign's three-second revival during Survival. |
| Spectator | Follow the surviving teammate with a Waiting for next wave notice and shared Survival HUD. Dead players cannot attack, collect, activate triggers or attract enemies. No free-flight camera in this version. |
| Team wipe | End the run once no admitted, connected participant is alive. A waiting newcomer is not an extra life. Both players receive the same results; P1 controls retry/arena selection/return to menu. |
| Admission | P2 joining during combat waits until preparation while P1 continues. Then hold consistently for current-world admission, place P2 safely, and copy P1's current weapons/ammo/equipped weapon; full health, zero armor and no temporary powerups. Inventories diverge normally afterward. |
| Reconnect | A dead participant cannot reconnect to get an early revival; preserve their run/life status until the normal wave boundary. Reuse existing Steam identity or persisted Direct IP identity and partner-assignment rules. Rejoining is not persistent run restoration. |
| Run lifetime | P1 may start/continue alone. P2 leaving removes their readiness requirement; if the only remaining active player is dead, end the run. P1 leaving ends the session; no host migration. P1 controls difficulty, arena, retry, shared pause and return to menu. P2's own menu does not pause P1. |
| Scores | One shared team score/combo, wave number, enemies-left and timer. Both players' kills/damage contribute under native scoring rules; either player taking eligible damage affects the shared combo. Avoid duplicate hit/kill attribution. No per-player contribution scoreboard in this version. |
| Records | Local co-op best scores per arena/difficulty plus last-run results; both peers may retain their local copy of the same team result. Separate from solo records, campaign saves and official solo leaderboards. No global co-op leaderboard or multiplayer solo-score submission. |
| Persistence | No persistent Survival run saves/checkpoints in this implementation. Hide/disable them with a clear mode-specific message. Campaign persistence remains unchanged. Small co-op result records are allowed; they cannot resume a run. |
| Balance | Retain native wave/enemy escalation, health and difficulty behavior. Personal resource copies and co-op readiness/death rules above are deliberate changes. No additional player-count scaling in this version. |

The owner confirmed Q1–Q12 and approved this planning handoff. Implementation remains a separate task for Claude; this session made no Survival code changes. If the owner changes a rule, update dependent phases and tests before implementation.

## Native source research and lookup

Native Survival is named **HORDE**, not a Survival class. Useful aliases:

- `CS`: `<Decompiled>/CSharp/Assembly-CSharp/Hyperstrange/PBD`
- `ISIL`: `<Decompiled>/Cpp2IL/Native-ISIL/IsilDump/Assembly-CSharp/Hyperstrange/PBD`

Recovered C# provides names, signatures and field layout; many bodies are placeholders. Behavioral findings were checked against the corresponding native ISIL, not inferred from empty C# methods. They still need runtime checks on the installed game build. Line numbers below refer to the researched declarations and are lookup hints, not a compatibility guarantee.

| Reference | Methods/fields to inspect | Finding / reason |
| --- | --- | --- |
| `CS/LevelId.cs` (enum near line 14; metadata near 669), `CS/LevelManager.cs` | `TypeEnum.HORDE`, `AllLevels`, `CurrentLevel`, `IsMod`, `DlcCategory`, `UpdateMetaData` | Enumerate actual base arenas. Native metadata contains Horde score/kills/waves/best weapon/damage/time; isolate co-op records. |
| `CS/HordeModeController.cs`, `ISIL/HordeModeController.txt` | `Begin`, `SetHordeModeState` (518), `CombatStart`, `CombatUpdate`, `CleanupUpdate`, `RestStart`, `ResetState`, `EndLevel` | State machine is INTRO / COMBAT / CLEANUP / REST. State changes have gameplay side effects; invoking them on P2 merely for HUD can spawn/clean actors or advance combat. |
| Same controller (fields near 174–242) | `_hordeModeState`, `_spawnedNpcs`, `_spawnCooldowns`, `_usedSpawnPower`, `_combatTimeLeft`, `_comboMultiplier`, `_currentScore`, `_currentWave`, `_cycleIndex`, `_damageStats` | Host simulation state extends well beyond enemies. Use explicit typed state and lifecycle ownership. |
| Same controller | `RegisterNpc`, `OnNpcEnabled`, `SpawnNextNpc`, `GetNextNpcSpawnPosition` (603), `OnSpawnedNpcHit`, `OnSpawnedNpcDeath`, `OnPlayerReceivedDamage` | Native spawn-distance test uses cached local player with minimum 8 m; account for both players. Native player-damage combo subscription only sees local player. Remote actor reports IsPlayer, but attribution must be verified. |
| `CS/HordeModeControllerModel.cs`, matching ISIL | Authored NPC lists and spawn-power/wave-duration escalation | Preserve native balancing. Handle late-wave eligibility, flyers and special actors. |
| `CS/Contraptions/HordeModeWeaponSpawner.cs`, matching ISIL | `OnStateChanged` (73), `SpawnPickups`, `OnPickup` (101), `ClearSpawnedPickups`, `Save` (122), `Load` (129) | A weapon choice can set COMBAT and clear offers. Native Save returns an empty dictionary; Load is a return-only body. Personal choice and shared readiness must be separated. |
| `CS/Contraptions/HordeModePickupSpawner.cs`, matching ISIL | `GetNextPickup`, `SpawnPickup`, `OnCurrentItemPickup`, `SpawnAll` (208), `Save` (187), `Load` (194) | Random resource offers/cooldowns and first-pickup UnityEvent. Save only records first-pickup state; Load refills the candidate pool and spawns again. No complete live-offer snapshot. |
| `CS/PlayerView/PlayerHordeModeView.cs`, matching ISIL | Controller score/multiplier/time/count/wave subscriptions and phase/countdown presentation | Reuse native HUD, sounds and presentation without granting P2 simulation authority. |
| `CS/UI/Menus/HordeModeMenu.cs` | `StartSelectedLevel`, `_levelButtons`, `_dlcButton` | Reuse selection/difficulty flow for P1; do not enable P2 independent starts or excluded content. |
| `CS/UI/Menus/EndHordeMenu.cs`, `CS/GameManager.cs` | Results binding, `ResetLevel`, exit; `GameManager.EndHorde` | Survival results differ from campaign LevelStats. Mirror actual team result data to both native result screens. |
| `ISIL/LevelController_NestedType___EndLevelReady_d__40.txt` | Native end coroutine | Dispatches campaign completion versus EndHorde. Existing campaign FINISH handling is not sufficient. |
| `CS/LostClown.cs`, `CS/Contraptions/ExplosiveBarrel.cs`, matching ISIL | `OnHordeModeStateChanged`, Horde controller references | Audit arena-specific actors and reset/respawn behavior, beyond the main controller. |

Phase 0 resolved these facts at runtime; see **Phase 0 findings** below. Spectator camera integration remains a Phase 3 design task (no native spectator camera exists).

## Phase 0 findings (October 16, 2026)

Build checked: game `GameAssembly.dll`/metadata SHA-256 prefixes `4281F23F…`/`30537A06…`, identical to the recovery manifest; PE ImageBase `0x180000000` read from the PE header. Runtime evidence comes from the offline fixture below, run on an isolated installation; it never hosts a session and keeps profile/metadata writes blocked.

```powershell
./scripts/Test-SurvivalInventory.ps1 -GameDir $TestGameDir [-Levels LEVEL_Horde_Ufo] [-Observe LEVEL_Horde_Ufo]
```

It discovers levels from `LevelManager.AllLevels`, dumps every base HORDE arena, then drives one native solo cycle on the observed arena (default: first discovered). Outputs go to ignored `.local/survival-inventory-<time>/` (`levels.tsv`, `arena-*.tsv`, `census-*.tsv`, `events.tsv`). Reference pass: `.local/survival-inventory-20261016-192803`, 3 arenas, Suburbs observed.

**Fixture action labels:** start and offer selection use native pickup paths (contact for the start shotgun, `IInteractable.Interact` for the offer after contact did not collect it). To bound the run, the fixture kills each registered enemy with `Npc.Kill` and ends the run with `Player.Kill`; these are direct native calls, not natural combat. Native first-use tutorials stop scaled time until Use; the fixture dismisses them through the native `OnUseDown`.

### Arena inventory

| Level asset | ID | Scene / cluster | Enemy spawns | Resource spawners | Arena-specific content (census) |
| --- | --- | --- | ---: | ---: | --- |
| `LEVEL_Horde_Suburbs` | `dd65ee8b08523bf4bad9442b75bf7aff` | `HORDE_Suburbs` / `CLUSTER_Horde_Suburbs` | 22 | 29 | 6 barrels, 1 jump pad, kill plane, 4 hookables, 38 breakables, 51 physical props |
| `LEVEL_Horde_Ufo` | `b5ae84a20b230ab41aab166cd2aa94bc` | `HORDE_Ufo` / `CLUSTER_Horde_Ufo` | 12 | 28 | 8 barrels, teleport, 6 breakable capsules whose `BreakListener` toggles `water` (`Toggler`), decals |
| `LEVEL_Horde_VipZone` | `8036ff1dd55137044a3a5533bee1574b` | `HORDE_VipArea` / `CLUSTER_Horde_VipArea` | 16 | 28 | 11 barrels, 7 jump pads, 21 glass objects, 185 physical props |

Excluded: `LEVEL_Horde_DLC` (`DlcCategory` PTSD). All arenas have `Type` HORDE, `IsMod` false, `Id` −1 and no episode. Each has exactly one `HordeModeController` (child of `LevelController`), one `HordeModeWeaponSpawner` with 3 offer slots, one `PlayerHordeModeView`, one `PlayerSpawnPoint`, and no doors, campaign `SpawnPoint`s, checkpoints, `LostClown` or `HordeTeaserController`. All three use models with identical values: initial spawn power 200 (+70, exponent 1.1), wave 1 duration 30 s (+3 s, exponent 1.03; wave 10 = 60.48 s), all 34 authored enemy types reached by wave 12 (points 45–650). The offer pool is the same everywhere: 5 weapons (DildoBow, GatlingGun, LightningGun, MachineGun, RocketLauncher) and 9 items (Steroids, Quick Pillz, Portable Armor, Health Pipe, Ice/Fire/Confusion drinks, Ammo Box, Akimbo). Resource spawners are `onDemand`, mostly 30 s cooldown (usable-item spawners 45 s, repetition allowed) and spawn on `Begin`.

### Native solo flow (observed on Suburbs, confirmed against ISIL)

| Step | Native behavior | Multiplayer consequence |
| --- | --- | --- |
| Load | State INTRO, wave 1, timer 30. Nothing starts by itself (idle 10 s). | P2 must not run any start path. |
| Start | Authored `PlayerWeaponPickup_Shotgun` (auto-pickup `ItemPickup`) has an `InteractListener` whose persistent listener calls `HordeModeController.Begin`, plus a one-liner sound. `Begin` sets COMBAT directly, runs `CombatStart` (wave duration, `StopAllCoroutines`, starts `_CombatCountDown`, clears ragdolls), raises static `OnStateChanged`, then `HordeModePickupSpawner.SpawnAll`. The same binding exists in all three arenas. | The start pickup is personal, but `Begin` is a once-per-run host side effect; P2's copy must request it, not invoke the listener. |
| Countdown | `_CombatCountDown` ticks `OnCombatCountDownTick` 3→1 on scaled `WaitForSeconds(1)`, sets music, `_combatCountDownFinished`, `OnCombatCountDownFinish`. No spawns until finished. | Host pause stops it. Present ticks on P2 from host state. |
| Combat | `OnSlowUpdate` → `CombatUpdate`: spawn-cooldown ticking, spawn while used power < `GetSpawnPower(wave)`, `GetNextNpcSpawnPosition` = random ready spawn ≥8 m from the controller's cached `_player` (observed minimum 8.3 m), 2 s per-spawn cooldown, `SpawnNextNpc` → `PoolManager.SpawnPoolable` → `RegisterNpc`. `RegisterNpc` adds `HordeModePoints` to used power, `SetTarget(Player.Instance)` (every observed enemy targeted the local player), subscribes death/hit. Timer counts down; at ≤0 → CLEANUP. | Spawn distance, target and combo-damage subscription see only P1's local player; Phase 2 must add P2. |
| Scoring | Death: used power −= points; score += points × difficulty factor × combo; kills++; in COMBAT combo += 0.5 (max 5). Hit: damage/`_damageStats[weapon]`. Player damage (subscribed on cached `_player` only): combo −1 (min 1). Combo decays 0.1/s; observed decaying during REST as well. | Both players' attribution and P2 damage penalties are Phase 4 work. |
| Cleanup | `SetHordeModeState(CLEANUP)` kills spawned enemies where `!CanReachPlayer`; `OnSlowUpdate` → REST once `_spawnedNpcs` is empty. | Count authoritative live enemies only. |
| Rest | `SetHordeModeState(REST)`: `WavesSurvived = wave`, wave++, timer preview = next duration (does not tick), music. Static `OnStateChanged(REST)` → weapon spawner `SpawnPickups` fills the 3 slots: random not-yet-taken weapons first, otherwise items. Observed: RocketLauncher, DildoBow, LightningGun. | Host selects offer set; each player needs personal copies. |
| Offer | Teleporting onto an offer did not collect it within 8 s; `Interact` collects. `OnPickup`: remove that weapon from `_weaponPickupsLeft`, `SetHordeModeState(COMBAT)`, unsubscribe, `ClearSpawnedPickups` (other offers despawned). Observed wave 2 started, 0 offers remaining. | One native choice both starts the wave and clears offers. Split personal acceptance from shared readiness. |
| End | `Player.Kill` → `LevelController.OnPlayerDeathStart` → `EndLevel(DEATH)` → `UpdateMetaData` (Horde score/kills/waves/best weapon into profile; blocked here) → `_EndLevelReady` → `GameManager.EndHorde` → `EndHordeMenu` opened 2 s after death. | Campaign DEATH override must not swallow this in Survival; results/records are mode-specific. |
| Retry / reset | `EndHordeMenu.ResetLevel` → `GameManager.Load(null)` (native same-map restart). `LevelController.ResetLevel` calls the controller's despawn/reset (`ResetState`: stop coroutines, despawn to pool, score/combo/wave/kills reset, `SetHordeModeState`). | Existing `Load(null)` Restart travel already covers same-map retry. |

Other confirmed facts: `HordeModeController.OnNpcEnabled` is a native `ret` (no-op). Weapon-spawner `Save` returns an empty dictionary and `Load` is a native `ret`. The results menu performs no uploads; the only native leaderboard calls found are campaign time uploads inside `UpdateMetaData`, already blocked by `SessionPatches.Protect`. The native camera is the player's own first-person rig (`PlayerCamera`/`PlayerWorldCamera`/`PlayerWeaponCamera`); `EventCamera` serves cutscenes. A spectator view requires a PBDMP-owned camera owner.

Not exercised natively in Phase 0: unreachable-enemy cleanup, flying/summoned enemies (`LevelController.RegisterSummonedNpc` → `RegisterNpc`), resource-spawner cooldown refills, Ufo water toggles and barrel/scenery resets on retry. These are Phase 2–3 targets.

## Existing implementation map

| Repository reference | Relevant seam / constraint |
| --- | --- |
| `src/PBDMP.Plugin/SessionLifecycle.cs` | `Supported()` accepts only base CAMPAIGN; constructor rejects Horde; Tick disables BUTTON_Horde. BeginHost / BeginClient / OnReady / Frozen / epoch and MapGeneration own travel/release. Expand mode support deliberately, not by removing every guard. |
| `src/PBDMP.Plugin/SessionPatches.cs` | LoadLevel/OpenLoading guards, BlockMenus(HordeModeMenu), death/EndLevel overrides, DeathScreen, metadata/profile guards. Route by session mode; retain campaign behavior and offline pass-through. |
| `src/PBDMP.Plugin/PlayerSessionState.cs` | CaptureStart, Died, Tick, CaptureJoin, ApplyJoin. Current Tick automatically revives after 3 seconds; Survival needs wave-authorized revival while retaining life generations and safe placement. |
| `src/PBDMP.Plugin/MultiplayerBehaviour.cs`, `NativeMultiplayerUI.cs` | Creates lifecycle/world/save services, updates them, owns native lobby/menu access and reporting context. Existing Survival button sits above Multiplayer; keep navigation/news and keyboard/controller behavior. |
| `src/PBDMP.Network/HostTravel.cs`, `ClientTravelPolicy.cs`, `Protocol.cs` | Travel epochs/readiness/release; avoid duplicate same-map loads. Current wire 11, version 0.10.0. Introduce a validated mode discriminator/run state where needed; bump wire for incompatible layout changes. |
| `src/PBDMP.Network/WorldProtocol.cs`, `CoopSession.cs` | Bounded typed frames, client/host authority, ordered delivery and snapshot backpressure. Current generic numeric limits must not silently cap a legitimate long-run Survival score. |
| `src/PBDMP.Plugin/WorldCatalogue.cs` | Fixed encounter families currently describe campaign; add stable authored Horde controller/spawner identities. Preserve personal-pickup exclusion and inactive-parent handling. |
| `src/PBDMP.Plugin/SharedWorld.cs`, `SharedWorldPatches.cs`, `SharedActors.cs`, `RemoteActor.cs` | Reuse authoritative NPC registration, pooling identity/life handling, targeting and hit routing. Generic NPC guards do not suppress the separate Horde controller; P2 requires explicit controller/spawner guards. |
| `src/PBDMP.Plugin/SharedEnemyPresentation.cs`, `SharedWeaponPatches.cs`, `SharedWeaponEffects.cs` | Reuse current attacks and presentation. Do not create functional remote weapon/NPC duplicates or replay native attacks for visual feedback. |
| `src/PBDMP.Plugin/SharedLoot.cs`, `SharedLootPatches.cs` | Reuse shared loot decisions and separate collection; extend live offers/cooldowns and personal collection generations as necessary. |
| `src/PBDMP.Plugin/SharedMissionCompletion.cs`, `SharedMissions.cs`, `CoopCampaignState.cs` | Campaign completion/statistics/unlock behavior must remain campaign-only. Survival needs its own result adapter. Do not run campaign next-level/unlock bookkeeping after a Survival run. |
| `src/PBDMP.Plugin/CoopSaveService.cs`, `CoopSavePatches.cs`, `NativeSaveBridge.cs`, `SharedWorldPersistence.cs`, `NativeCoopSaves.cs`; `src/PBDMP.Network/CoopSave.cs` | Current save service also handles personal pickup tracking, identity/admission and live baseline transfer. Do not remove it wholesale for Survival just to disable disk saves; separate/reuse required live-session duties. Schema 2 campaign files stay compatible and untouched. |
| `src/PBDMP.Plugin/SyncPerformanceDiagnostics.cs`, `PerformanceBaselineDiagnostics.cs`, `scripts/Test-PerformanceBudget.ps1` | Existing timing/traffic instrumentation and controlled comparison seams. Preserve bounded/time-sliced work. |
| `scripts/Test-Travel.ps1`, `Test-Runtime.ps1`, `Build.ps1`; `tests/PBDMP.Network.Tests/Program.cs` | Existing isolated launch, fixture, managed codec/authority and smoke patterns. Survival fixtures/runner extensions are proposed, not existing commands. |

Potential new modules (names are suggestions): `SurvivalSession`, `SurvivalState` and a pure managed run-state reducer; `SurvivalPatches`; `SharedSurvival` controller/offer adapters; native HUD/results adapter; spectator owner; local co-op record store; Survival diagnostics and runner. Keep the mode interface narrow. Avoid a repository-wide rewrite or spreading unrelated flags throughout campaign classes.

## State, authority and ordering requirements

- Identify the run separately from arena name; identify phase/wave and offer generation. Retry on the same map is a new run. Reject stale wave-ready, pickup, death and score requests across runs/travel epochs.
- Model preparation/readiness/admission, COMBAT, CLEANUP, countdown, dead/spectating and run-ended explicitly. Do not force a new native enum value just to represent multiplayer preparation/terminal state.
- P1 resolves waves exactly once. CLEANUP must account for host-authoritative live actors, pooled reuse, summons/transforms and native unreachable-enemy cleanup; do not count cosmetic dead replicas as enemies.
- Spawn selection respects both living participants and native navigation/flying rules. If no safe spawn exists, retry through a bounded scheduling path, never spin/block the main thread or spawn on a player to satisfy a timer.
- Separate native simulation from client presentation. Guard updates, coroutine starts, first-pickup callbacks and static phase events that could create side effects. Preserve initialization and unsubscribe exactly the delegates this mode owns.
- Use host-selected offer identity/generation and asset IDs; keep personal acceptance/collection state distinct. Native first-pickup callbacks that affect gameplay execute once under host authority. One participant cannot consume the other's offer or advance the shared phase twice.
- Readiness requires living, admitted and fully restored participants. Admission at REST must finish before it adds a ready requirement. A newly admitted participant joins preparation before the next countdown; freeze/recompute an unstarted transition if membership changes. Never release P2 before its baseline is complete.
- Waiting joins do not freeze an active combat wave. A shared hold at the safe admission boundary is permitted. Add clear Waiting for next wave / Loading messages; loading timeout releases P1 and returns P2 with an explanation.
- Reconnect identity/death status survives disconnect within the live run. No stale input/hits from a spectator or previous life can affect the world. Receiving a pose is not authority to resurrect a player.
- A dead P1 must not stop the authoritative controller while P2 survives. Keep enemy targeting, timer, scoring and wave advancement alive independently of the host's local player/camera.
- Score each authoritative hit/kill once. Confirm eligibility for damage/combo penalties across the native and forwarded-hit paths, including armor/shields and environmental hits; do not penalize twice for presentation or acknowledgements. Preserve native scoring formulas rather than inventing replacements.
- Preserve personal temporary effects during ordinary play; wave revival clears temporary powerups using the existing respawn policy. Spectator menus/weapon input must not leak into gameplay or leave a permanent NONE/MENU lock after revival/retry/disconnect.
- Co-op records use immutable shared final results with a run ID; once-only finalization, bounded retention, safe file replacement and clear corruption handling. No new remote service, personal identifiers in public diagnostics, or automatic leaderboard uploads.
- Do not reuse campaign result replay or the disk-save serializer as a substitute for a complete Survival live baseline.

## Phases and completion gates

Phases 0–7 are **Complete**. Do not mark a phase complete solely because hooks compile.

| Phase | Status | Work | Exit evidence |
| --- | --- | --- | --- |
| 0 — Arena and call-graph inventory | Complete | Enumerate base HORDE levels, IDs/scenes/controller model/spawners; inspect native begin/end/reset/coroutines and camera/death integration. Record map-specific differences and public relative references. | Arena inventory; one native solo run's phase/offer/end flow understood; no guessed scene names or claimed C# stub behavior. |
| 1 — Mode-aware entry and travel | Complete | Add explicit supported mode policy; P1 native selection/difficulty; P2 protected starts; both load barriers; host-inside-arena hosting, retry and menu cleanup. Disable only unsupported Survival disk-save/checkpoint controls. | Start from lobby, host active arena, same-map retry, campaign↔Survival transition and disconnect input checks; campaign travel/save smoke retained. |
| 2 — Authoritative waves and actors | Complete | Host-only Horde updates/spawning/callbacks; reuse dynamic enemies/combat; dual-player spawn safety; wave/phase/timer state and pooled actor lifecycle. Native arena mechanics/reset adapters as inventory requires. | Two processes see one enemy population, both can fight/be targeted; waves advance once; flying/summoned/transformed/cleanup paths work; pause and host-dead continuation work. |
| 3 — Offers, readiness and death | Complete | Same offers/personal collection; Ready control and host countdown; death ledger, wave revival, spectator camera and team wipe. Enforce native first-pickup side-effect ownership. | Same/different offers both succeed; no forced partner loss; ready waits for both; no timed campaign respawn; spectator cannot interact; both-dead finalizes once; disconnect recomputes membership. |
| 4 — HUD, results and local records | Complete | Native score/combo/wave/timer/enemy HUD and sounds; two-player scoring and combo penalties; Survival result screens; host-only retry/quit; separate local team records. | Matching authoritative values/results on both PCs; native HUD callbacks cannot start gameplay; solo profiles/leaderboards untouched; records survive restart but cannot resume runs. |
| 5 — Safe late join and reconnect | Complete | REST-boundary admission; complete actor/world/phase/offer/collection/death baseline, host loadout catch-up; timeout/cancel cleanup; no replay of historical effects. | Join requested during combat leaves P1 running; admit once at preparation; dead rejoin waits; disconnect during admission/countdown/result handling does not strand controls or duplicate NPCs/offers. |
| 6 — Targeted hardening and campaign regression | Complete | All-arena smoke; multiwave/endless pooling soak; high score/long timer bounds; score/offer burst traffic; frame-time/alloc/network comparisons; selected failure paths. | Evidence table below, bounded memory/queues, no periodic main-thread freeze; campaign fixtures still pass. Record maintainer manual checks separately. |
| 7 — Documentation, package and handoff | Complete | Granular Survival checklist and usage; current limitations; final matching build package; install when closed after checks. | Exact plugin/network pair and package hashes; concise before/after metrics; Pending/In progress/Complete handoff. No automatic commit/push. |

Implementation order is intentional; later work can share infrastructure but must not hide an unfinished earlier gate. Each phase should leave campaign playable. Small reviewable changes are preferred to a single large Survival patch. Persistent Survival saves are a separate future proposal, not an extra gate in this plan.

## Targeted verification matrix

Use real native methods for integration evidence. Clearly label synthetic state/codec fixtures. Not every natural route needs automation; give the owner an actionable playtest list instead of blocking indefinitely on exhaustive capture.

| Area | Minimum evidence |
| --- | --- |
| Managed rules / wire | Mode/run/wave validation, finite nonnegative times/scores with adequate range, unknown modes/assets, stale/duplicate readiness and pickup requests, client authority violations, terminal idempotency, truncated/oversized frames. |
| Arena coverage | Load every discovered base arena in a fresh/clean session; verify catalogue and initial offers/spawns/HUD. Exercise multiple full native waves on a representative arena; list any remaining natural arena-specific routes. |
| Combat / scoring | P1 and P2 hits/kills count once; P2 incoming damage changes team combo once; flying/pooled/summoned enemies and cleanup; host alive/dead combinations; environmental interactions and personal loot. |
| Preparation | Same and different offer choices; Ready without collection; first pickup/start event once; repeated input/stale offer rejection; one player slow/paused/disconnected. No resource collection removes partner's copy. |
| Death | Either player dies; host spectates while client fights; revive at preparation; both dead; dead reconnect; last living player disconnects; revive controls/camera and gear; no friendly fire or spectator contact. |
| Membership / travel | Host alone; P2 joins during COMBAT/CLEANUP/REST/countdown/results; load fails/cancels; same-map retry; host/client menu access; host leaves; campaign↔Survival transition. |
| Baseline | Current enemies/health/projectiles/world and selected offers/phase/time/score/readiness restored before release; no old explosion/kill audio replay, no duplicate effects/offer grants. |
| Presentation | Both native HUD/results screens, spectator notice, waiting/loading notices, host pause, keyboard/controller/escape/focus recovery; no stuck input/time scale on quit/retry. |
| Records / isolation | Both peers see same final result; local best uses arena+difficulty; failed record write is nonfatal; solo metadata/profile/leaderboard calls remain suppressed only for co-op; no Survival disk-save entry slips through. |
| Soak / performance | Long multiwave run with pooled enemies and recurring drops; observe live/dead entity ledgers, tombstones, subscriber counts, offers, collection history and queue sizes. Bound growth by active/relevant state, not elapsed wave count. |
| Campaign regression | Existing managed suite + runtime smoke; targeted travel/input, combat, pause, join and save/load/checkpoint fixtures. Avoid rerunning the complete animation capture matrix unless presentation changes justify it. |

For performance, compare the same arena/workload: offline native Survival, P1 hosted alone, and P1+P2. Record frame p50/p95/p99, worst frame/stalls, world update/sweep p95, allocations/GC and actual per-direction transport traffic. Separate steady combat, wave transitions and admission. Account for two local processes sharing a PC; do not claim a two-PC network result from that setup. Use existing campaign budgets (world p95 <4 ms, sweep p95 <200 ms in their controlled fixture) as regression references, not promises for every arena/workload. Avoid per-frame scene discovery, unbounded history, blocking file I/O and full-world snapshots on every score change. Send score/HUD deltas at a bounded cadence; interpolate display timers locally from authoritative state.

Existing build/check entry points (not new Survival commands):

```powershell
# From <RepoDir>; select an isolated game copy for native tests.
$GameDir = Read-Host 'Isolated game folder'
./scripts/Build.ps1 -GameDir $GameDir
./scripts/Test-Runtime.ps1 -GameDir $GameDir
# Existing focused campaign regression; choose only checks relevant to changed seams.
./scripts/Test-Travel.ps1 -GameDir $GameDir -JoinInput -Level LEVEL_AD_Neighborhood
./scripts/Test-Travel.ps1 -GameDir $GameDir -Saves -Level LEVEL_AD_Neighborhood
```

A Survival runner should discover valid arenas or accept verified IDs, refuse to interrupt an existing game, own/clean up only its processes, require current-run evidence and write ignored outputs. Do not document nonexistent switches as usable commands.

## Deferred work and completion definition

Deferred: persistent Survival run/checkpoint saves, solo import, DLC/custom arenas, extra players, host migration, extra balance/scaling options, global leaderboards, individual contribution scoreboard, and unrelated animation/death-model polish. Preserve these boundaries unless the owner expands scope.

Survival implementation is complete when every supported arena can enter/play/retry/leave; native waves/combat, personal offers, readiness, death/wipe, shared HUD/results and safe reconnect satisfy the agreed rules; campaign regressions and bounded performance checks pass; and remaining manual natural-route checks are explicit. Do not claim complete from one arena loading or from broad campaign sync being present.

Update `docs/sync-progress.md` with individual Survival rows, not one grouped checkbox: selection, arena travel, host waves, spawn safety, pooling/cleanup, timer, score, combo, resource spawns, weapon offers, readiness/countdown, wave revival, spectator, wipe/results, retry, records, late join, reconnect/dead identity, pause, isolation, performance. Keep campaign coverage and new Survival coverage distinguishable; do not silently apply the old campaign percentage to a larger denominator.

Persistent Survival support, if later requested, requires a separate versioned snapshot of controller wave/phase/timer/score/combo, spawn budgets/cooldowns/cycle, live actors, offer RNG/remaining pools/current choices, personal collection state and run identity. HordeModeController has no native ISaveable, weapon-spawner Load is a no-op and pickup-spawner Load respawns items. Campaign NativeSaveBridge alone cannot provide correct Survival resume.

## Phase 1 handoff (October 16, 2026)

**Source changes**

- `SessionLifecycle`: `Supported()` = `Campaign()` ∨ `Survival()` (base, non-mod, non-DLC; mode derived from the authored level asset on both peers, so travel needs no wire change and protocol 11 is unchanged). `InSurvival` (cached per travel level) drives mode routing. P1 keeps `BUTTON_Horde`; P2's is blocked with its start/restart buttons. Status text is mode-aware.
- `SessionPatches`: the native `HordeModeMenu` is blocked only for P2; unsupported content gets one explanation. Campaign-only callers (`CampaignAudit`, campaign travel fixtures, co-op save loading) use `Campaign()`.
- `SurvivalPatches` (new): P2's `HordeModeController.Begin` is inert with a "P1 starts the Survival run" notice. P2 still collects its personal copy of the authored start pickup.
- `CoopSaveService`/`NativeCoopSaves`: manual, quick and automatic saves are refused in Survival with a mode-specific message, and the autosave clock does not advance. Loading a campaign co-op save from an arena remains allowed (Survival→campaign transition). `CoopCampaignState.CampaignCompleted` is campaign-only.
- `NativeSaveBridge.SurvivalOwned`: the Horde controller and both spawners are excluded from native world baselines (capture and restore). Without this, P2's join baseline ran `HordeModePickupSpawner.Load` and spawned resource pickups that P1 did not have (found by the new fixture).
- Fixture: `SurvivalTravelDiagnostics.cs`, `scripts/Test-Travel.ps1 -Survival [-Level <discovered arena>]`.

**Evidence (isolated installation, current build)**

| Run | Result |
| --- | --- |
| `Test-Travel.ps1 -Survival` | Pass, 30 checks (`.local/travel-20261016-194117`): P1 hosts an already-loaded arena in INTRO; P1 keeps Survival selection; Survival saves refused without reaching native writers; P2 follows; P2 Survival button/menu, LoadLevel and restart blocked; P2 `Begin` inert and no P2 resource pickups; P2 collects its start shotgun by contact without starting either run; same-arena retry (new epoch, both INTRO wave 1); Survival→campaign (matching catalogues); lobby; lobby→Survival; P1 leaves inside the arena and P2 returns to title with offline guards and no input barrier. |
| `-JoinInput -Level LEVEL_Horde_Suburbs` | Pass, 10 checks: menu join, shared pause/resume and modal input cleanup in an arena. |
| `-JoinInput -JoinFromLevel -Level LEVEL_Horde_Suburbs` | Pass, 11 checks: P2 joins from its own loaded solo arena. |
| Campaign regression | Default travel/respawn 54 checks; `-JoinInput` Neighborhood 10; `-Saves` Neighborhood 42. All pass. |

**Interim behavior and limits (not Phase 1 claims)**

- Survival is not yet playable together. P1's native run is not presented on P2; enemies/waves/HUD are Phase 2–4.
- Death in Survival still uses campaign's three-second respawn until Phase 3 replaces it with wave revival.
- Joining while P1's run is in COMBAT loads the arena but does not restore run state; admission at preparation is Phase 5.
- The native Survival menu's difficulty prompt was not driven by a fixture (the fixture uses `GameManager.LoadLevel`, the prompt's own callback target). Maintainer check: P1 selects each arena/difficulty from the Survival menu while hosting.

## Phase 2 handoff (October 16, 2026)

**Source changes**

- Protocol 12: `WorldKind.Survival` (24) and `src/PBDMP.Network/SurvivalProtocol.cs` (`SurvivalState`: run, phase, wave, waves survived, timer, countdown, score, combo, kills, live enemies, damage). Score/damage are decimal strings, so long runs are not capped by the ±1,000,000 world-float bound. Strict layout/bounds validation; stale lower runs are ignored. Documented in `docs/protocol.md`.
- `SharedSurvival.cs` (`SharedWorld` partial): P1 publishes the state from the native controller in each capture sweep (field changes immediately, timer-only changes at most every 0.5 s; included in join snapshots). P2 validates it and mirrors values into its inert controller's fields; no native state events are raised on P2.
- `SurvivalPatches`: on P2, `OnSlowUpdate` and `RegisterNpc` are inert (no combat/cleanup ticks, spawning, countdown or score subscriptions). On P1: `Begin` increments the run; `CombatStart` timestamps the presentation countdown; `GetNextNpcSpawnPosition` also rejects spawns within 8 m of living P2 (the native 2 s spawn cooldown makes the retry bounded); `RegisterNpc` starts each enemy on the nearer living player. A 0.5 s host tick retargets enemies whose target player is dead.
- Enemies, hits, deaths and pooling reuse the existing shared enemy path unchanged: P1's pooled Horde enemies replicate to P2, and P2's hits reach P1's native `Npc.Hit` with the remote actor as dealer, so native Horde kill/score callbacks run once on P1.
- Fixture: `SurvivalWaveDiagnostics.cs`, `scripts/Test-Travel.ps1 -SurvivalWaves [-Level <arena>]`.

**Findings**

- Native `RegisterNpc` calls `Npc.SetTarget(Player.Instance)`. Because the NPC already has a target, the campaign awareness patch never chose P2. Fixed by the registration/dead-target retargeting above.
- A synthetic P1 hit without a collider throws inside native `DecalManager.SpawnDeathBloodDecal` on Suburbs. Natural hits and P2's forwarded hits carry a collider; the fixture now does too. Watch for decal errors during natural Suburbs play.

**Evidence (isolated installation, build pair `PBDMP.dll` 85F6AF6B6EA38566 / `PBDMP.Network.dll` 3A0A3D6D89B94CC5)**

| Run | Result |
| --- | --- |
| `Test-Travel.ps1 -SurvivalWaves` (Suburbs) | Pass, 25 checks (`.local/travel-20261016-201530`): INTRO state on P2; P1's start pickup begins run 1; P2 mirrors COMBAT and the countdown with zero local spawns; P1's native kill and P2's hit request both kill P1 enemies and score; with P2 parked on a spawn cluster every new spawn kept ≥ 8 m from P2 and the partner guard rejected spawns; an enemy nearer P2 targeted P2; P1 pause froze the timer on both; with P1 dead the timer kept running and enemies targeted P2; wave 1 resolved once into REST wave 2 (P2 mirrored, no P2 offers); a native offer started wave 2 of the same run; native `SpawnNextNpc` flying StarFuck/ClownElemental spawned at the native +4 m height and appeared on P2 within ~0.3 m; pooled wave-2 population matched; CLEANUP cleared into REST once and P2 replicas cleared. |
| Regression | `-Survival` 29 checks, `-JoinInput -Level LEVEL_Horde_Suburbs` 10, campaign travel/respawn 54, campaign `-Saves` 42, managed 35 network + 8 reporting. All pass. |
| `-Combat` (campaign) | Failed at "late join receives a personal copy of the host's pre-join loot ledger" on this build and on baseline `d511ea1`. Fixed after Phase 7: the fixture expected exactly one drop, but P1's ledger also correctly holds native drops made before hosting began. Now passes (28); see [verification](docs/verification.md#door-and-loot-fixture-fixes--october-18-2026). |

**Fixture labels:** wave 1 is synthetically lengthened by 60 s and ends by shortening the timer, so every observation fits one wave (natural wave timing was observed in Phase 0); enemies are removed with native `Npc.Kill` and players kept alive by restoring native hit points, except during the host-death step; P2's hits are synthetic native `Npc.Hit` calls on replicas.

**Not exercised (remaining natural checks):** natural summons (ClownElemental summons are behaviour-driven and did not occur in the window); native unreachable-enemy cleanup (the fixture had already cleared enemies); transforming enemies (no Horde enemy with transform logic was identified); Ufo/VipZone runs and their arena mechanics (Phase 6 all-arena smoke); long multiwave pooling (Phase 6 soak).

## Phase 3 handoff (October 16, 2026)

**Source changes**

- Protocol 12 (still unreleased, so no new version): the Survival state gains ready/required/revive masks, a run-ended flag and the REST offer list (`slot:token`, token `W<n>`/`I<n>` indexing the spawner's authored prefab arrays). New client kind `SurvivalRequest` (40): P2's Ready or Offer request for P1's current run, wave and preparation phase. Documented in `docs/protocol.md`; managed tests cover bounds, stale/forged layouts and round trips.
- `SharedSurvival.cs` (P1): a preparation ledger per INTRO/REST. The start shotgun, a taken offer, or the Ready control marks a player ready; P1's native `Begin` (INTRO) or `SetHordeModeState(COMBAT)` (REST) runs only once every admitted living participant (or one awaiting this revival) is ready, so the native countdown is the single host-owned countdown. P1 alone starts at once, as natively. A newly admitted P2 must ready again; P2 leaving drops its requirement.
- Offers: P1's native REST `SpawnPickups` runs unchanged; a `SpawnPickup` postfix records each instance's prefab token and slot, which P1 publishes. P2 spawns identical personal copies through its own native `SpawnPickup`. Inside native `OnPickup` the state change to COMBAT is suppressed on both PCs (P1: until everyone is ready), so a choice clears only that player's other offers. Pool rule (implementation decision): before each native REST spawn, P1 rebuilds `_weaponPickupsLeft` so a weapon leaves the pool once every current participant has taken it from an offer; solo hosting therefore matches the native rule.
- Resources (implementation decision): P2's native resource spawners run locally once P2 sees the run start (`HordeModePickupSpawner.SpawnAll`), with their own random items and cooldowns. Native spawners have no first-pickup listeners in any base arena, so this has no shared side effects; collection on either PC never touches the partner's pickups.
- Death: `PlayerSessionState` gains wave-revival mode (no campaign timer in Survival) and `Revive(beside, yaw)`. Entering REST authorizes every dead participant (1 s grace for an in-flight death; a player already seen alive in this phase is not re-authorized; P2's authorization lapses after 5 s if it never revives). P1 revives locally; P2 revives from the frame's revive mask once per run/wave. Revival = native `Player.Respawn` (full health, zero armor) + retained inventory, beside the living partner on clear ground, otherwise the arena spawn.
- Team wipe: when no admitted participant is alive or awaiting revival, P1 runs native `EndLevel(DEATH)` once (`SurvivalPatches.Ending` lets it past the campaign death override); P2 runs its own native end once from the ended flag. P2's inert controller never caches its `LevelController` parent, which native `EndLevel` reads, so P2 sets it first (found by the fixture as a native NRE).
- Spectator (`SurvivalSpectator.cs`): after 1.5 s of native death presentation, the dead player's native world camera follows the living partner from behind (sphere-cast against walls); the weapon overlay camera is disabled. Both are restored on revival, retry or leaving. While spectating, `PlayerInteractionComponent` updates/use and `Pickup.Interact` are blocked; enemies already retarget away from dead players.
- Ready control and notices: holding Use for 1 s in INTRO/REST marks the player ready (native `INPUT_Use`, so keyboard and controller). A top-centre notice (`NativeHud.Banner`) shows readiness, waiting-for-partner and the spectator notice with wave/time/enemies; it is shown only with a partner, so solo hosting looks native.
- Begin's postfix now counts a run only when native `Begin` actually ran (`__runOriginal`).
- Fixture: `SurvivalPrepDiagnostics.cs`, `scripts/Test-Travel.ps1 -SurvivalPrep [-Level <arena>]`. The Phase 2 `-SurvivalWaves` fixture now readies P2 at INTRO/REST and expects P1's revival at REST, not after three seconds.

**Evidence (isolated installation, build pair `PBDMP.dll` 9EA4FAC21C38A724 / `PBDMP.Network.dll` 6133E6E640BEE138)**

| Run | Result |
| --- | --- |
| `Test-Travel.ps1 -SurvivalPrep` (Suburbs) | Pass, 46 checks (`.local/travel-20261016-210957`): P1's start pickup waits for P2 (both required, notices shown); P2's start pickup begins exactly one run; P2 gets personal resources and collecting one leaves P1's; P2 dies, stays dead (no timed respawn), spectates P1, cannot collect, is not targeted and is not required; REST publishes the native offers and both see identical sets; P1's pick waits and leaves P2's offers; P2 takes the same offer and wave 2 starts once; P1 dies and spectates while the run continues; P1 and P2 each revive at their REST with full health, zero armor and retained gear, P2 beside P1; a weapon both took leaves the pool; stale-wave and unoffered requests are rejected; different picks start wave 3 once; one survivor keeps the run; both dead ends it once through native results on both PCs; P2 cannot retry; P1's results retry gives both a clean INTRO with no input barrier; P1's Ready control waits for P2 and P2 leaving starts the run once. |
| Regression | `-SurvivalWaves` pass (33 check lines), `-Survival` 29, `-JoinInput -Level LEVEL_Horde_Suburbs` pass, campaign travel/respawn 54, campaign `-Saves` 42, campaign `-JoinInput` Neighborhood pass, managed 36 network + 8 reporting. |

**Fixture labels:** the start pickup is collected by contact and offers by native `Interact`; deaths use native `HandleKill`; enemies are removed with native `Npc.Kill` and waves end by shortening the native timer; living players are kept alive by restoring native hit points; the Ready control and stale/forged requests use the fixture API rather than physical input.

**Interim behaviour and limits (not Phase 3 claims)**

- Results: P1's native results set time scale 0, which the session treats as a shared host pause, so P2's own native results most likely sit under the shared-pause input hold (P2's results opened; navigation was not checked). P2's results come from its mirrored controller fields, not an exact shared result. Phase 4 owns exact results, host-only results controls and records.
- P2's native Survival HUD is not bound yet (Phase 4); the spectator notice carries wave, time and enemy count meanwhile.
- A P2 joining during COMBAT still loads immediately and becomes a participant once released; a host who is dead when P2 asks to join holds the join until P1's next revival or the 120 s loading timeout. Both are Phase 5 (admission at the preparation boundary).
- Not exercised: physical Hold-Use input (the fixture calls the same Ready path), Ufo/VipZone, a dead player reconnecting (Phase 5), a disconnect during the countdown, and controller navigation of the notices.
- A `NullReferenceException: routine is null` from native `MusicController`/`StopCoroutine` during P2's level reset appears in P2's Unity log in Phase 2 runs too; it is pre-existing and not Survival-specific.

## Phase 4 handoff (October 17, 2026)

**Source changes**

- Protocol 12 (unreleased): the Survival state also carries P1's run ID (32 hex, generated at native `Begin`) and, once ended, the native level `TimeStat` value and best-weapon asset ID. New request kind Damage (3). Timer and combo changes are throttled together (both decay continuously); everything else is sent at once.
- `SurvivalResults.cs` (`SharedWorld` partial). P2's native HUD: P2's controller raises no events, so P2 calls its `PlayerHordeModeView` handlers (`OnStateChanged`, `OnCurrentWaveChanged`, `OnScoreChanged`, `OnSpawnedNpcCountChanged`, countdown tick/finish, `OnCombatTimeLeftChanged`) from P1's state. These were checked in ISIL to touch only labels, notices and sounds. The timer counts down locally on scaled time between P1's frames.
- P2 damage and combo: at runtime P2's inert controller is **not** subscribed to P2's player damage event; P1's is. So `ProcessHitData` on P2 reports each native damage event (health or armor lost) as a Damage request. P1 applies its native `OnPlayerReceivedDamage` (combo −1, minimum 1) once. The controller handler is P1-only, so P2 never penalizes its mirrored combo.
- Results: P1 captures the final time/weapon after native `EndLevel`. Before P2 runs its own native end, it sets its controller's score, kills, waves survived, damage and best-weapon stats from P1's final state, and restores P1's `TimeStat` afterwards. Both native results screens therefore show the same team result. P2's native retry and menu buttons are disabled, with a notice that P1 chooses; P2's own `Load`/`OpenTitle` stay refused.
- Local co-op records (`src/PBDMP.Network/SurvivalRecords.cs`, managed-tested): one copy per PC at `<GameDir>/BepInEx/PBDMP/survival-records.json`, or beside the run output during fixtures.
  - Content: the best result per arena and difficulty, plus the last 20 runs, keyed by run ID, so each run is recorded once.
  - Robustness: writes replace the file atomically, an unreadable file is set aside as `.corrupt`, and a failed write only logs and shows a notice.
  - Display: the native results compare with the co-op best (`IsScoreNewBest`/`OldScoreToDisplay`), and the HUD best line shows it.
  - Solo metadata stays blocked and untouched.
- Fixture: `SurvivalResultsDiagnostics.cs`, `scripts/Test-Travel.ps1 -SurvivalResults [-Level <arena>]`.

**Evidence (isolated installation, build pair `PBDMP.dll` 7FA909C7F423E283 / `PBDMP.Network.dll` 71E8B7CD12EB7F5D)**

| Run | Result |
| --- | --- |
| `-SurvivalResults` (Suburbs) | Pass, 22 checks (`.local/travel-20261017-004909`): both HUD best lines show the empty co-op best; P2's native HUD is shown and its score/hostiles labels match P1, and its wave timer counts down between P1's updates; one native damage event on P2 (P1's hit through the shared incoming-hit path, with spawns stopped and enemies cleared) is reported once and applies the native penalty once on P1; after a team wipe P1 records the team result once and both native results show the first co-op best; solo Survival metadata unchanged and native writes blocked; P1 keeps its results controls; P2's results use P1's exact score, kills, waves and time, and P2 keeps an identical record; P2's buttons are disabled with the notice, and its own leave/retry is refused; P1's results retry clears results and input on both, and both HUD best lines then show the recorded best. |

**Fixture labels:** enemies are killed with native `Npc.Hit` (without a weapon origin, so the best weapon was empty and the best-weapon check was vacuous); players are kept alive by restoring native hit points; spawning is paused for the damage check by raising the native used spawn power; deaths use native `HandleKill`.

**Not exercised:** a non-empty best weapon on the results screens; records surviving a game restart (covered by the managed store test only); controller navigation of P2's disabled results; whether the native HUD stays visible while a player spectates.

## Phase 5 handoff (October 17, 2026)

**Source changes**

- Travel (protocol 12) gains `AdmissionWaiting`. P1 sets it in `SessionLifecycle`/`SessionStories` and holds a connecting P2 (`_survivalAdmissionPending`) while `SharedWorld.HoldSurvivalAdmission` says so:
  - the run is outside INTRO/REST (combat, cleanup, countdown);
  - the run has ended (results keep P1 dead, so a join could not capture P1's loadout);
  - the joiner's identity is not yet known;
  - or the joiner is a participant P1 saw die during this preparation phase.
- P1 keeps its run going and is not frozen while holding. At the next INTRO/REST it runs the normal join admission: hold, P1's weapons/ammo, placement beside P1, world snapshot including the offers, and P2's own resources. P2 waits where it is (main menu, or frozen in its own map) with a "Waiting for P1's next Survival wave" notice, and its loading deadline is extended.
- P1 advertises the hold even while nobody is connected. A newcomer otherwise acts on the first travel state it receives — the found bug: a reconnecting P2 followed P1's previous, still-released join generation straight into the arena.
- Dead identity: P1 records a participant's death by its Steam or Direct IP identity (`CoopSaveService.PeerIdentity`) with run and wave, and clears it when it sees that player alive again. A rejoin waits until a REST with a higher wave than the death.
- Fixture: `SurvivalJoinDiagnostics.cs`, `scripts/Test-Travel.ps1 -SurvivalJoin [-Level <arena>]`.

**Evidence (same build pair)**

| Run | Result |
| --- | --- |
| `-SurvivalJoin` (Suburbs) | Pass, 23 checks (`.local/travel-20261017-005004`): P1 alone starts at once from the start pickup; P2 joining during combat is held with its notice, outside the arena, while P1's timer keeps running unfrozen and P2 is not a participant; at REST P2 is admitted once and becomes a participant with full health, zero armor, P1's weapon, placement beside P1, P1's offers, its own resources and no historical enemies, and P1 has no input barrier; the admitted P2's readiness is required for wave 2; P2 dies in combat and leaves, P1 keeps the death by identity, and P2 rejoins alive at the wave-3 REST; P2 dies during REST 3 and reconnects at once, is held while P1 starts wave 3 alone, and joins alive at REST 4. |
| Regression (Phases 0–5) | `-SurvivalPrep` 46, `-SurvivalWaves` 31 check lines, `-Survival` 29, `-JoinInput -Level LEVEL_Horde_Suburbs` 10, `-JoinInput -JoinFromLevel -Level LEVEL_Horde_Suburbs` 11, campaign travel/respawn 54, campaign `-Saves` 42, campaign `-JoinInput` Neighborhood 10, managed 37 network + 8 reporting. All pass. |

**Fixture labels:** waves end by native `Npc.Kill` and a shortened native timer; readiness uses the Ready control's API; reconnects use the normal Direct IP join; deaths use native `HandleKill`.

**Not exercised:** a Steam reconnect (identity path shared with Direct IP but not run here); joining during the countdown or results specifically (same hold rule as combat); a P2 disconnect while its admission is loading (existing load-timeout path); P2 joining from its own loaded arena during combat (frozen-wait path).

## Phase 6 handoff (October 18, 2026)

**Findings and source changes.** The new soak and failure-path fixtures found seven defects. All are fixed and keep the fixture assertion that exposed them:

| Found by | Defect | Fix |
| --- | --- | --- |
| Soak, ledger rows | Each pooled respawn gets a new enemy id. P2 never reused or released replicas: one new replica per P1 respawn (63 → 253 by wave 8), and per-life ledgers grew on both PCs. | `SharedEnemyLifetimes.cs`: P1 drops a life's per-id state when the pooled object respawns. P2 destroys a replica it instantiated once P1 reports that life inactive (authored scene enemies are only hidden) and ignores an unknown id that is already inactive. Campaign respawns share this path. |
| Soak, ledger rows | `Cast`/`TryCast` wrappers are new managed objects, so the wrapper-keyed "original value" dictionaries gained a duplicate key per frame: `_animatorEvents`, `_rootMotion`, `_encounterBehaviours`, also `_behaviours`, `_agents`, `_actorBodies`, `_doorBodies`. Campaign grew the same way, more slowly. | `NativeOriginals.cs`: same API, keyed by native address. Restoring originals on exit is unchanged. |
| Soak, wave 8 | The Mage's pooled instance on P1 has runtime-only child parts. The unknown presentation/actor part threw and ended P2's session. | Enemy part mismatches are logged once and ignored. Authored scripted actors must still match exactly. |
| Failure paths, F4 | P1's results retry runs native `ResetLevel`, which fills Hyperstrange's lazily cached `_parent` (LevelController). The urine catalogue followed it, so a P2 joining at that retry got a catalogue mismatch. | Survival catalogues skip `_parent`. Campaign manifests are unchanged, because co-op saves store them. |
| Failure paths, F2 | A guest leaving mid-load interrupted the native loading screen, whose NONE input barrier stayed buried under the title menu. | When P2's session cleanup reaches the title, a NONE barrier covered by a MENU state is removed. |
| Arena matrix, Ufo join (intermittent, about 1 in 3) | At a REST admission P1 is already in the arena and sent the guest baseline within a frame of the new travel epoch. P2 dropped save pages from an epoch it had not adopted yet, so P1 waited forever for the baseline ack (120 s timeout). | P2 keeps pages from a later epoch (at most 4,096) and replays them once it adopts that epoch. A later rerun logged one replay and the admission completed. |
| Failure paths, F2 (intermittent) | Right after a join hold ended (P2 left mid-admission), a native component restored the cached zero time scale. P1 latched it as its own pause and stayed frozen with nobody connected. | For 2 s after a join/save hold ends, a native zero becomes a shared pause only through P1's pause menu. P1 pause transitions from native time scale are logged. |

Fixtures:
- `SurvivalSoakDiagnostics.cs`, `-SurvivalSoak [-SurvivalSoakWaves 8-200]`. Traffic counters reuse `PBDMP_PERF_BASELINE`; the soak workload label is in the exported summaries.
- `SurvivalFailureDiagnostics.cs`, `-SurvivalFailures`.
- Fixture fixes:
  - `-SurvivalPrep` tries each resource, because native health pickups refuse a full-health player.
  - `-SurvivalJoin` waits for the start countdown before its timer check.
  - `-ActorParts` accepts the now-retired replica of a killed MiniCovid.
- Diagnostic counters: enemy registrations, Survival frames sent, retired replicas, join/baseline state.
- Managed: a long-run extremes test (38 network scenarios).

**Evidence (isolated installation, final build pair `PBDMP.dll` 33881AAD99026698 / `PBDMP.Network.dll` 71E8B7CD12EB7F5D; managed 38 network + 8 reporting; 0 warnings)**

| Run (each fixture on Suburbs, Ufo and VIP Zone unless noted) | Result |
| --- | --- |
| `-Survival` | Pass, 29 checks on each arena. |
| `-SurvivalWaves` | Pass, 30–33 check lines per arena. |
| `-SurvivalPrep` | Pass, 47 / 47 / 48 checks; VIP Zone passed in three consecutive runs. |
| `-SurvivalResults` | Pass, 22 checks on each arena. |
| `-SurvivalJoin` | Pass, 23 checks on each arena. Ufo also passed 6 of 6 consecutive runs after the early-page fix, and one run logged the replay. |
| `-SurvivalFailures` | Pass, 16 checks on each arena: F1 P2 leaves during the countdown (P1 continues, no freeze); F2 P2 leaves while its REST admission loads (P1 released, REST/offers intact, one wave start, no input barrier on P2); F3 the last living player leaves while P1 is dead (run ends once, recorded once); F4 a join during P1's results is held and admitted at the retry; F5 P1 leaves in combat (P2 back at the menu with no input barrier). |
| `-SurvivalSoak` (Suburbs) | Pass, 24 checks: offline / hosted-alone / connected comparison; 15 waves with 321 pooled respawns, with every managed ledger bounded by P1's live pool; managed heap +2.0 / +2.4 MiB; no frame over 100 ms (worst 30 / 40 ms); a burst of 6 kills sends 5 Survival frames in 1.06 s; 80 duplicate offer/ready requests mark P2 ready once and start one wave; score 987,654,321,024, 123,456,789 kills and a 7,200 s timer reach P2's view, native score label, results and record exactly; P2's results show P1's native best weapon. |
| `-JoinInput` on Suburbs, from the main menu and from an already-loaded arena | Pass, 10 and 11 checks. |
| Campaign: default travel, `-Saves`, `-JoinInput` (Neighborhood), `-Actors`, `-State` | Pass, 54 / 42 / 10 / 597 / 3,775 checks. |
| Campaign: `-ActorParts` | Fails at its pre-existing point (below). `-Doors` and `-Combat` were fixed after Phase 7 and now pass. |

Comparison and soak (`-SurvivalSoak`, Suburbs, `.local/travel-20261018-210312`). The workload is wave 1 with native spawning, and the fixture kills every live enemy each 1.5 s. Both processes share one PC at a 120 FPS cap and 1280x720, sampled for 25 s:

| Configuration | Mean FPS | p50 / p95 / p99 ms | Worst ms | Frames >50 ms | Main-thread alloc MiB/s | Out / in KiB/s |
| --- | ---: | --- | ---: | ---: | ---: | --- |
| Offline native (P2 idle at the menu) | 119.8 | 8.34 / 9.42 / 9.66 | 18.7 | 0 | 2.55 | — |
| P1 hosting alone | 119.7 | 8.33 / 9.44 / 9.73 | 19.5 | 0 | 2.72 | — |
| P1 with P2 connected | 119.4 | 8.34 / 9.51 / 9.89 | 21.7 | 0 | 3.77 | 55.2 / 11.9 |
| P2 connected | 119.6 | 8.34 / 9.59 / 9.99 | 25.5 | 0 | 3.32 | 11.8 / 54.9 |

P1's connected shared-world update: p95 0.77 ms, max 2.5 ms (campaign budgets 4 / 16 ms). Both processes ran on one PC, so these are not two-PC network figures.

**Fixture labels:**
- Enemies are killed with native `Npc.Hit` carrying P1's weapon. Native `Npc.Kill` is the fallback for enemies that ignore the synthetic hit (Illuminati).
- Waves end by a shortened native timer.
- Living players are kept alive by restoring native hit points; deaths use native `HandleKill`.
- Readiness, the request flood and the high-value bounds use fixture APIs or the native controller's fields.
- Joins and leaves use the Direct IP session.

**Pre-existing failure (identical on baseline `d511ea1`, built in a temporary worktree and run on the same installation):**
- `-ActorParts`: stops at "campaign includes scripted shoot-reactive human: FakeCitizen". With this build it first passes the MiniCovid checks, including the retired replica.

`-Doors` and `-Combat` also failed here and on the baseline. Both were fixture defects, fixed after Phase 7; see [verification](docs/verification.md#door-and-loot-fixture-fixes--october-18-2026).

**Open intermittent issue:** `-JoinInput -JoinFromLevel -Level LEVEL_Horde_Suburbs`, where P2 joins from its own loaded solo arena, failed in 3 of 8 runs during Phase 6. Shortly after the admission, one PC has a native `MENU` open. P1's native pause path (`GameManager.PauseGame`) was entered, which shared-pauses P2; in another run P2 kept a menu. The native caller was not identified. The campaign variant passed 3 of 3, and the main-menu join into an arena passes. Maintainer check: join from an already-loaded solo arena and watch for an unexpected pause menu.

**Not exercised (maintainer checks):**
- Natural combat (fixtures kill enemies) and natural summons or unreachable-enemy cleanup.
- Arena mechanics under play (Ufo water capsules and teleport, VIP Zone jump pads and glass).
- Physical Hold-Use and controller navigation of P2's disabled results.
- Whether the native HUD stays visible while spectating.
- Steam sessions and reconnects, and two-PC network figures.
- The native Survival menu's difficulty prompt.
- Records across a real restart (managed test only).

## Phase 7 handoff (October 18, 2026)

- Player documentation: README "Survival co-op" play section and sync row; package `INSTALL.txt` Survival lines; [verification](docs/verification.md) Survival commands and the two-player Survival checklist.
- Reference documentation: [sync progress](docs/sync-progress.md) Survival rows (arena travel, pooling, performance, late join, reconnect); [protocol](docs/protocol.md) enemy-life retirement, part mismatch tolerance and early save-page replay; [performance](docs/performance.md) Survival comparison; [roadmap](docs/roadmap.md) scope; documentation index link.
- Package and install: `scripts/Package.ps1` now runs in Windows PowerShell 5.1 (a full-path check replaces `Path.GetRelativePath`). Package `PBDMP-0.10.0.zip`, development build **41D7421713392F3B** (a copy is kept as `PBDMP-0.10.0-survival-41D7421713392F3B.zip`). The zip entries match the tested pair. Installed with `Install-Plugin.ps1` into the normal game folder while the game was closed: the installed DLLs and manifest hashes match the pair. The previously installed pair and zip are kept privately for rollback.
- Not committed or pushed. The working tree's staged index was left as found.

## Current handoff

| Item | Status | Next step |
| --- | --- | --- |
| Existing work checkpoint commit | Complete | `d511ea1`; clean tree immediately before this document. Build: 0 warnings/errors; 34 network + 8 reporting checks pass; credential/personal-path pattern scan of 217 tracked/candidate files found no matches. No push. |
| Survival native/repo research | Complete | References and factual gaps above. Static research only; runtime arena inventory remains Phase 0. |
| Owner decisions Q1–Q11 | Complete | Confirmed in the design table. |
| Final shared-understanding confirmation | Complete | Owner approved Q12: handoff ready for Claude. Planning only; no implementation in this task. |
| Phase 0 inventory | Complete | 3 base arenas and the native solo start/wave/offer/end flow recorded in Phase 0 findings. Fixture `scripts/Test-SurvivalInventory.ps1` + `src/PBDMP.Plugin/SurvivalInventoryDiagnostics.cs` (offline, `PBDMP_SURVIVAL_INVENTORY`), passing on the isolated installation. Full build passes: 0 warnings/errors, 34 network + 8 reporting checks. Nothing installed, committed or pushed. |
| Phase 1 entry and travel | Complete | See Phase 1 handoff below. Build pair `PBDMP.dll` 2D90C554CC387014 / `PBDMP.Network.dll` 4F20BB0C291F6B7E (SHA-256 prefixes): 0 warnings/errors, 34 network + 8 reporting checks. Not installed, committed or pushed. |
| Phase 2 waves and actors | Complete | See Phase 2 handoff: protocol 12 Survival state, P2 inert controller, partner spawn safety and targeting; natural summon/unreachable-cleanup checks remain. Not installed, committed or pushed. |
| Phase 3 offers, readiness and death | Complete | See Phase 3 handoff: personal offers/resources, readiness and countdown, wave revival, spectator, team wipe. Not installed, committed or pushed. |
| Phase 4 HUD, results and records | Complete | See Phase 4 handoff. Not installed, committed or pushed. |
| Phase 5 late join and reconnect | Complete | See Phase 5 handoff. Not installed, committed or pushed. |
| Phase 6 hardening and campaign regression | Complete | See Phase 6 handoff: seven defects found and fixed; pre-existing campaign failures unchanged from `d511ea1`. |
| Phase 7 documentation, package and handoff | Complete | Package/install build 41D7421713392F3B (`PBDMP.dll` 360F140AFC3F198F / `PBDMP.Network.dll` 71E8B7CD12EB7F5D). Not committed or pushed. |
| Survival implementation | Complete | Remaining: maintainer natural two-PC playtest, the open join-from-loaded-arena pause, deferred work below. |

Resume instruction for Claude: Survival co-op is complete for the three base arenas (Phases 0–7). Remaining work is the maintainer's natural two-PC playtest ([checklist](docs/verification.md#survival-two-player-checklist)), the open intermittent join-from-loaded-arena pause, and the deferred items below. Ask the owner before expanding scope.
