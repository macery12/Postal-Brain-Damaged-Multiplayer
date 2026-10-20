# Campaign world consistency megapass

**October 16, 2026 — implementation closeout.** The agreed world systems are implemented, with focused regressions for the closing fixes. Broad campaign traversal and the remaining edge-case matrix are handed to the maintainer for playtesting, per the updated testing preference. The maintainer covered those cases in the October 18, 2026 two-PC playtest.

Current source: **0.10.0 / protocol 11 / save schema 2** after the subsequent [weapon-animation pass](../weapon-animation-testing.md). The megapass evidence below refers to its recorded protocol-10 builds. Sync status continues to distinguish implemented behavior from unverified campaign cases. No commit or push is included in this closeout.

## Current remaining work

The remaining work is manual acceptance and the next performance phase. It is separate from the implementation closeout; partial checkmarks remain where coverage is incomplete.

| Original closing area | Result / handoff | Lookup |
| --- | --- | --- |
| Forest bridge route | Native fall, full transform state, pause, rollback and completed state pass; natural trigger/crossing/rejoin goes to campaign playtest. | T07; MechanismDiagnostics.cs |
| Wheel/cart route | Actual wheel driver and linked cart doors pass pause/rollback/completion; real button/rider/save/rejoin flow goes to campaign playtest. | T08; MechanismDiagnostics.cs |
| Cat-paw/shockwave | Fixed runtime-prefab authority: harmless P2 replicas, one host hit, correct victim and shared-pause gating. Natural attack cadence/variants remain manual. | T12; WorldSceneryPatches.cs; HazardDiagnostics.cs |
| Laser/crush/rider safety | Fixed stale host hazard contacts using current capsule/volume geometry; genuine damage remains active. Full Area69 restored ride passes. Natural obstructions/lasers remain manual. | T13; WorldHazards.cs; SharedMissionPatches.cs |
| Negative interactions | Eligibility/idempotence/retry/refund paths audited; simultaneous and interruption combinations remain manual. | T10, T19–T20; WorldInteractionLedger.cs; SharedDoors.cs |
| Gameplay fragments | Physical/spawned prop pushes, pooling, disk rollback and rejoin pass. Native fragment classification/collision remains explicitly partial for playtesting. | T27; WorldFragments.cs; PropDiagnostics.cs |
| Cross-family persistence | Live save and fresh-process host-alone resume/P2 join pass; extended family/route matrix remains manual. | T30–T32; SaveDiagnostics.cs |
| Native failure recovery | Save/transfer bounds, corruption and compatibility have managed evidence; abrupt native interruptions/missing targets remain manual. | T33; CoopSaveService.cs; WorldNativeCapture.cs |
| Active-world performance | Prior connected-idle FPS fix retained. Busy combat/world and real transport load move to the optimization phase. | T34; docs/performance.md |
| Audio/story/UI | Targeted native pause/story/input checks pass. Natural loop/audio/sequence combinations remain manual. | T15–T16, T35; StoryDiagnostics.cs |
| Campaign traversal | Maintainer campaign playtest with meaningful saves/rejoins. Existing all-map census stays separately labeled. | T36 |

## Closing fixes and source lookup

- **Already-loaded-map join:** P2 uses the native same-map restart path, and newer admission metadata can reuse an unfinished identical map load. This fixes the reproduced Prison falling-platform missing-reference disconnect without skipping world records. [SessionLifecycle.cs](../../src/PBDMP.Plugin/SessionLifecycle.cs), [ClientTravelPolicy.cs](../../src/PBDMP.Network/ClientTravelPolicy.cs), [JoinInputDiagnostics.cs](../../src/PBDMP.Plugin/JoinInputDiagnostics.cs).
- **Runtime hazards:** pooled paw attacks and damage-trigger ticks belong to P1 even without an authored catalogue key. Shared holds suppress damage. Before a native damage tick, P2's existing contact is checked against current transforms; departed/dead contacts are removed. Native eligibility is retained, and genuine overlap is a positive control. [WorldHazards.cs](../../src/PBDMP.Plugin/WorldHazards.cs), [SharedMissionPatches.cs](../../src/PBDMP.Plugin/SharedMissionPatches.cs), [WorldSceneryPatches.cs](../../src/PBDMP.Plugin/WorldSceneryPatches.cs), [HazardDiagnostics.cs](../../src/PBDMP.Plugin/HazardDiagnostics.cs).
- **Bridge/wheel checks:** the existing machinery test now covers Forest bridge geometry/state and Backyard's real root driver with both cart-door references. [MechanismDiagnostics.cs](../../src/PBDMP.Plugin/MechanismDiagnostics.cs).

The native hazard regression failed before the fix on P2 local duplicate damage and separately on stale-contact extra damage. Its positive control verifies that the fix does not make P2 immune. The full restored-elevator survival latch remains enabled; no tolerance or death check was removed.

## Handoff gate

- [x] Retain the approved campaign, personal-resource, save, authority and exit rules.
- [x] Resolve reproduced closing blockers with focused native regressions.
- [x] Keep catalogue discovery, synthetic fixtures and natural campaign acceptance distinguishable.
- [x] Record unverified routes, fragment classification, failure injection and active-load work in the maintainer checklist.
- [x] Final matching build/install/package verification: **E19C56EB3B893F6A**; installed, artifact and partner ZIP DLL hashes match. See the latest verification entry.

Development coverage remains 93%; the last accepted milestone baseline remains 90%. Transferring manual testing does not invent acceptance evidence or inflate these scores.

## Historical plan and checkpoints

The original research, strict acceptance matrix and dated checkpoints below are retained for lookup. Their old installed-build IDs and pending statements are historical; current status above takes precedence.

### Original research baseline (historical)

Prepared October 14, 2026 against **PBDMP 0.9.0 / wire 8**, development build **534BED70CED37266**. Git HEAD was **f10172b — steamnetworking**; preserve the uncommitted save/megapass work. The research sections below describe that inspected baseline. Later checkpoints record implementation and runtime evidence.

## Quick navigation

- [Outcome and boundaries](#outcome-and-boundaries)
- [Confirmed decisions](#confirmed-decisions)
- [Confirmed gaps](#confirmed-gaps)
- [Source lookup](#source-lookup)
- [Native reference index](#native-reference-index)
- [Campaign lookup](#campaign-lookup)
- [Implementation order](#implementation-order)
- [State and restoration contract](#state-and-restoration-contract)
- [Acceptance matrix](#acceptance-matrix)
- [Commands and evidence](#commands-and-evidence)
- [Completion gate](#completion-gate)

## Outcome and boundaries

Complete these three connected areas across the **15 built-in base-campaign maps**:

1. Shared mission triggers, puzzle machinery, riding platforms, timed hazards and scripted story sequences.
2. Correct current-state late join, checkpoint rollback and persistent save/load for those systems, including motion/countdowns and completed consequences.
3. General destructible scenery, glass, collision breakables, partial barrel/burning state, live smash presentation and gameplay-relevant movable/spawned props.

Either player must be able to operate the world. Both players must encounter the same solid geometry and progression. Joining/loading must not undo recent changes, refill partly damaged objects or execute completed actions again. A discovered component or matching transform alone does not satisfy this outcome.

Retain existing rules: P1 owns shared AI/combat/world results and travel; either living player can interact; native puzzle conditions remain; requester-owned keys/items are consumed once; resources and pickups remain personal. P2 receives P1's exact weapons, remaining ammo and equipped weapon on join/rejoin and restore, then inventories remain independent. P2 starts those events with full health, zero armor and no temporary powerups. Deaths still respawn independently. Walk-in exits still require both living players; automatic endings retain the alive/loaded rule.

Survival, DLC, custom maps, extra players, host migration, solo-save import, unrelated player-animation polish and a general networking redesign remain separate. Do not classify fragments as cosmetic solely because they have a Rigidbody: objects that block, damage or operate a puzzle have shared gameplay consequences.

## Confirmed decisions

The grilling interview resolved the following choices. Do not reopen inherited rules unless research exposes an actual conflict.

| ID | Decision | Confirmed behavior |
| --- | --- | --- |
| D0 | Existing session rules | Base campaign, P1 authority, personal resources, existing saves/respawns/exits retained. |
| Q1 | Destruction and debris | Share destruction, remaining structure and collision. Harmless fragments may settle locally; gameplay-relevant fragments require shared state. |
| Q2 | Story participation and skipping | Both admitted players follow the shared story sequence. P1 alone controls skipping. Either player can operate ordinary machinery. |
| Q3 | Movable/spawned scenery | Include props whose movement affects collision, combat or puzzles. P1 owns motion; either player can push/hit; current gameplay state survives join/save/load. Pure decoration stays cosmetic. |
| Q4 | Existing 0.9 saves | Preserve originals. Migrate only where recorded data is sufficient for safe restoration. Otherwise clearly refuse and require a new save; never silently reset missing state. |
| Q5 | Joining during a story sequence | P2 waits with “Waiting for story sequence” while P1 finishes the noninteractive sequence. P1 continues during this wait. Admit P2 against the current world afterward. Future sequences play together. |
| Q6 | Secret discovery | Either player discovering a secret credits it once to shared level results and saved world progress. Rewards/pickups remain individually collectible; ordinary HUD/tutorial behavior remains local. |
| FINAL | Begin implementation | Confirmed: “Yes — begin the megapass.” Build, install and test locally; no commit/push. |

The design tree is: shared world → machinery/riding/hazards; destruction → structure versus cosmetic debris → gameplay physics; progression → story/skip → admission/secrets; persistence → current-state capture → compatibility/recovery. All gameplay branches and final implementation authorization are resolved.

Existing save policy stays: requests during active story sequences show **Save pending** until the sequence ends. Frame-accurate mid-cutscene disk persistence is outside this pass. Ordinary mechanical movement/countdowns must be captured and resumed; snapping all machines to an endpoint is not an acceptable shortcut.

## Confirmed gaps

These are inspected implementation/native facts, not claims that every map has a reproduced failure.

| ID | Finding | Required work | Lookup |
| --- | --- | --- | --- |
| G01 | Mission graph uses a family allowlist, UnityEvent/direct references and selected arrays; general component-array traversal is absent. | Inventory/classify callbacks and typed reference collections. Conveyor listeners are a concrete discovery gap. | R01, N06 |
| G02 | Machinery uses primitive/enum fields and local transform/active state; client controllers/tweens are stopped and timelines paused. | Add explicit phase, direction, timer and continuation contracts; separate passive replicas from host simulation. | R02–R04 |
| G03 | No explicit mod references to MovingPlatformTrigger, Pusher, PawShockwaveTrigger, LeonDuskSwitch or CeilingConveyorListener. | Implement rider/contact handling, victim-specific force/hits, switch callbacks and child-machine coverage. Scene-census absence does not prove prefab absence. | N01, N06, N09–N11 |
| G04 | Native platform carry computes world-position delta from _lastPostion and dispatches IPlatformMoveable. | Rebuild valid rider contacts and rebase caches after join/load/teleport; avoid a whole saved journey becoming one carry impulse. | N01 |
| G05 | P2 fall request calls FallWithDelay(Player.Instance) on P1; native player overload shakes that supplied player's camera. Native save records only _fallen. | Attribute rider effects correctly and preserve warning/countdown/fall state explicitly. | R05, N03 |
| G06 | Native Mover.Load can restart movement; Elevator.Load resumes GoTo/emits callbacks; TimelineSequence.Load performs Stop → initialTime → Play. | Audit host continuation versus passive client loading and guard side effects per family. Native Load is not inherently harmless. | R06, N02, N04, N12 |
| G07 | Ordinary breakables rely on OBJECTIVES/explosion-only/BreakListener discovery. Collision breakables are dependency-discovered. Decorative glass is excluded as a seed and lacks a shared Hit hook. | Catalogue scenery explicitly, preserve native eligibility/penetration and route P2 actions to P1 once. | R01, R05, N13–N16 |
| G08 | Existing reconstruction preserves permanent replacement wall/collision and hides loose pieces; it avoids native Break/Load. | Extend exact structural reconstruction and add separate live-only native smash presentation. Never hide an entire replacement parent indiscriminately. | R05, existing wall fixtures |
| G09 | Barrel Save only records exploded; Load(false) resets health/queued explosion. Burnable Save only records burned; Load resets partial unburned state. | Persist partial health/burn and pending fuse/countdown where gameplay-relevant. Test remaining-damage thresholds after restore. | N17, N18 |
| G10 | PhysicalObject save omits velocity/spin; PhysicalObjectSpawner.Save is empty and Load only pools spawned objects. | Persist gameplay prop instances, generation, motion, sleep/spawner state and both-player interest explicitly. | N19, N20 |
| G11 | Disk-loaded _nativeBaseline is from the loaded save, not a fresh capture for each subsequent join. Supported frames overlay it. | Restore current state for every new family. Mandatory regression: load → advance/break world → late join sees current state. | R06 |
| G12 | Catalogue membership changes its exact compatibility hash; no migration exists. Disk cap is 32 MiB but one save transfer is capped at 4 MiB. | Implement Q4 and bounded streaming/backpressure; do not create a valid disk save that cannot be transferred silently. | R07, R08 |
| G13 | Native secret entry combines notification, player callback and RegisterFoundSecret. | Deduplicate shared secret identity/counter without blindly executing the whole callback on both peers. | N22 |

## Source lookup

Anchors describe the inspected 0.9.0 working tree and may move. Method names are the stable search targets. Older research docs may describe superseded scope; current code and tracker take precedence.

| Ref | File and entry points | Purpose |
| --- | --- | --- |
| R01 | [WorldMissionCatalogue.cs](../../src/PBDMP.Plugin/WorldMissionCatalogue.cs#L20): BuildMissionTargets, MissionTypes, MissionBreakKeys, MissionAudit | Discovery, dependencies, exclusions and contacts. |
| R02 | [SharedWorld.cs](../../src/PBDMP.Plugin/SharedWorld.cs#L281): CaptureState, Send, ApplyDoorState | Complete/delta sweeps, snapshots, transform application and bounded backlog. |
| R03 | [SharedEncounters.cs](../../src/PBDMP.Plugin/SharedEncounters.cs#L17): EncounterFields, SendEncounter, ApplyEncounter | Generic field boundary and client controller suppression. |
| R04 | [SharedMissionState.cs](../../src/PBDMP.Plugin/SharedMissionState.cs#L14): SendMissionArrays, StopMissionController | Boolean pages, coroutine/tween cancellation, director pause. |
| R05 | [SharedMissions.cs](../../src/PBDMP.Plugin/SharedMissions.cs#L13): ClientMissionHit, ApplyMissionFall, SendMissionBreak, ApplyMissionState; [SharedMissionPatches.cs](../../src/PBDMP.Plugin/SharedMissionPatches.cs) | Requests, persistent structure and native authority hooks. |
| R06 | [CoopSaveService.cs](../../src/PBDMP.Plugin/CoopSaveService.cs#L186): TickCore, Safe, CaptureWorld, RestoreGuestNative, StartRestore, RestoreWorld; [NativeSaveBridge.cs](../../src/PBDMP.Plugin/NativeSaveBridge.cs#L17) | Current/loaded baseline, native serializers, hold/readiness and restore order. |
| R07 | [CoopSave.cs](../../src/PBDMP.Network/CoopSave.cs#L33): Validate, Read, Write; [SaveProtocol.cs](../../src/PBDMP.Network/SaveProtocol.cs#L7): SaveAssembler | Disk/schema/checksum/backups and transfer bounds. |
| R08 | [WorldCatalogue.cs](../../src/PBDMP.Plugin/WorldCatalogue.cs#L137): Build, SafeMotion, Add, PathOf, TransformKey | Authored identity, inactive-parent exclusions, manifest and listing. |
| R09 | [SharedWorldPersistence.cs](../../src/PBDMP.Plugin/SharedWorldPersistence.cs#L32): CapturePersistent, ReservePersistentIds, RestorePersistent; [SharedLoot.cs](../../src/PBDMP.Plugin/SharedLoot.cs) | Durable adapter integration, ID reservation, personal drop ledger. |
| R10 | [SessionLifecycle.cs](../../src/PBDMP.Plugin/SessionLifecycle.cs#L166); [SharedMissionExits.cs](../../src/PBDMP.Plugin/SharedMissionExits.cs); [SharedMissionCompletion.cs](../../src/PBDMP.Plugin/SharedMissionCompletion.cs) | Connect/readiness, occupancy, endings and results. |
| R11 | [SharedProjectiles.cs](../../src/PBDMP.Plugin/SharedProjectiles.cs); [SharedWorldFire.cs](../../src/PBDMP.Plugin/SharedWorldFire.cs); [SharedExplosions.cs](../../src/PBDMP.Plugin/SharedExplosions.cs#L75) | Host attack reconstruction/fire and existing IL2CPP nullable-rotation fix. |
| R12 | [WorldUrineCatalogue.cs](../../src/PBDMP.Plugin/WorldUrineCatalogue.cs); [SharedUrinePatches.cs](../../src/PBDMP.Plugin/SharedUrinePatches.cs); [SharedUrineRails.cs](../../src/PBDMP.Plugin/SharedUrineRails.cs) | Existing barrel/burnable/fuse/rail paths; preserve instead of duplicating. |
| R13 | [CampaignAudit.cs](../../src/PBDMP.Plugin/CampaignAudit.cs#L15); [WorldRegressionDiagnostics.cs](../../src/PBDMP.Plugin/WorldRegressionDiagnostics.cs); [MissionDiagnostics.cs](../../src/PBDMP.Plugin/MissionDiagnostics.cs); [SaveDiagnostics.cs](../../src/PBDMP.Plugin/SaveDiagnostics.cs) | Census/discovery and native fixtures. |
| R14 | [WorldProtocol.cs](../../src/PBDMP.Network/WorldProtocol.cs#L5); [CoopSession.cs](../../src/PBDMP.Network/CoopSession.cs); [tests/Program.cs](../../tests/PBDMP.Network.Tests/Program.cs#L6) | Direction rules, bounded records/queues, transport/save acceptance. |
| R15 | [SyncPerformanceDiagnostics.cs](../../src/PBDMP.Plugin/SyncPerformanceDiagnostics.cs); [Test-Travel.ps1](../../scripts/Test-Travel.ps1); [Test-Runtime.ps1](../../scripts/Test-Runtime.ps1) | Timing, input/UI regressions and existing flags. |
| R16 | [sync-progress.md](../sync-progress.md), [co-op-saves.md](../co-op-saves.md), [verification.md](../verification.md), [earlier mission plan](../mission-trigger-sync-plan.md) | Current rules/evidence and historical context. |

## Native reference index

**&lt;Decompiled&gt;** means reader-owned research output, defined in the [README placeholders](../../README.md#path-placeholders). The paths below are lookup references, not repository dependencies or broken links. No game binaries, dumps, raw logs or workstation paths belong in the repository.

C# prefix: **&lt;Decompiled&gt;/CSharp/Assembly-CSharp/Hyperstrange/PBD/**.

ISIL prefix: **&lt;Decompiled&gt;/Cpp2IL/Native-ISIL/IsilDump/Assembly-CSharp/Hyperstrange/PBD/**. Replace .cs with .txt. Recovered C# bodies are stubs; behavioral claims were checked against ISIL. Actual playable tracks/bindings/contact behavior still require runtime validation.

| Ref | File below prefix | Methods / fields |
| --- | --- | --- |
| N01 | MovingPlatformTrigger.cs | Move, UpdateLastPosition, ClearMovables, OnTriggerEnter/Exit; _transformToFollow, _affectedMoveables, _lastPostion (native spelling). Carry uses IPlatformMoveable deltas, not necessarily parenting. |
| N02 | Contraptions/Elevator.cs | GoTo, CallTop/Bottom/Opposite, Save/Load, CurrentState; platform/endpoints/desired transform, moving-platform trigger, movement/music FMOD instances and callback-suppression flags. |
| N03 | Contraptions/FallingPlatform.cs | FallWithDelay overloads, Fall(Player), Save/Load; _destructionDelay, pre-fall particle/event, shaking transforms, _fallen, _breaker. |
| N04 | Contraptions/Mover.cs | Move/MoveBack, Save/Load; _state, _initPos, _movementTimeLeft, _totalOffset, _onMoveEnd. Inspected ISIL Load near line 883, continuation branches 1176–1280, Save near 1297. |
| N05 | Contraptions/Rotater.cs; Contraptions/MultiRotater.cs | Rotate/RotateBack, remaining duration, current rotation/state/end event; multi-step animations, rotation index/duration/euler fields. |
| N06 | Contraptions/CeilingConveyor.cs; Contraptions/CeilingConveyorListener.cs | OnUpdate, Save/Load, ResetState; _activated, _moveDirection, _ceilingConveyorListener, listener _startPosition. |
| N07 | Contraptions/ForestBridge.cs; Contraptions/FerrisWheelCart.cs; Contraptions/Scaler.cs | Bridge Fall/triggered/static/animated state; cart Activate/DeActivate and doors (wheel driver must be found separately); scale phase/state. |
| N08 | Contraptions/WireWallGate.cs | Interact, Save/Load; TNT count/required count, interacted state, explosion delay, TNT objects, required-items component, exploder, breaker. |
| N09 | Contraptions/PawShockwaveTrigger.cs | OnTriggerEnter/Stay/Exit, Hit; _damage, _trigger, _hittables. Inspect prefab/runtime sources; absent from dated scene census. |
| N10 | Contraptions/Pusher.cs | Push; _pushForce, _pushCurve, _pushCollider. |
| N11 | Contraptions/LeonDuskSwitch.cs | TriggerSwitch, OnSwitch_AnimationCallback, Save/Load; _hidden, _triggered, _animator. |
| N12 | Contraptions/TimelineSequence.cs | StartSequence, ResetState, Save/Load; _sequenceStarted, _sequenceTime, _playableDirector; inventory runtime tracks/bindings/signals. |
| N13 | Contraptions/BreakableObject.cs | Hit, Save/Load, SetIndestructible; ignored weapons, explosion-only, destroyed/indestructible, breaker. Load may call Break again. |
| N14 | Contraptions/CollisionBreakableObject.cs | OnTriggerEnter, Save/Load; collider, force multiplier, destroyed, breaker; audit eligible native layers/actors. |
| N15 | Contraptions/Breaker.cs | Break, ResetState, AlignWholeAndParts; whole/replacement geometry, initial transforms, rigidbodies, offset, portal, listeners and sound/particles. |
| N16 | GlassObject.cs | Enable/Disable, Hit, Save/Load; _disabledOnStart, _hit, _disabled; native projectile-through behavior differs from solid breakables. |
| N17 | Contraptions/ExplosiveBarrel.cs | Hit, FlameTick, Execute, _Execute, _Respawn, Save/Load; health, chain delay, queued/exploded state. Native Save only records _exploded. |
| N18 | Contraptions/BurnableObject.cs | FlameStart/Tick/End, Save/Load; hitpoints, burning/burned, property animator, soakable, loop audio. Native Save only records _burned. |
| N19 | Contraptions/PhysicalObject.cs | Hit, Push, ApplyForce, OnCollisionEnter, optimization, physics/out-of-bounds, Save/Load; native Save omits velocities. |
| N20 | Contraptions/PhysicalObjectSpawner.cs | SpawnPhysicalObjects, SpawnRoutine, Save/Load; prefab/amount/frame/radius fields and _spawnedObjects; native Save returns empty state. |
| N21 | Explosion.cs | Invoke, hit/push dedup sets, 200-collider buffer; preserve once-per-target outcomes and nullable fix. |
| N22 | Contraptions/PlayerTriggerSecretComponent.cs; SecretsController.cs | OnPlayerEnter, RegisterFoundSecret, RegisterSecrets, Save/Load; separate local notification/player callback from shared counter. |
| N23 | Contraptions/PlayerTriggerDialogueComponent.cs; FMODDialogue.cs; SkipCutscenesController.cs; UI/Menus/CutsceneMenu.cs | Classify story versus ambient dialogue; inspect camera/input/skip settings and callbacks. Actual tracks/bindings remain a runtime audit. |

## Campaign lookup

**Dated discovery evidence: October 13, 2026**, from R13 two seconds after readiness with diagnostic NPC freeze. Counts locate fixtures; they do not establish natural interaction, riding or persistence correctness. Refresh the inventory, including inactive authored objects and runtime prefabs. Preserve native map misspellings.

| Map ID | Discovered families / counts | First acceptance focus |
| --- | --- | --- |
| LEVEL_AD_Neighborhood | Mover 3, Rotater 1, Invoker 7 | Existing fuse/wall cutout; ordinary prop/glass fixtures selected from inventory. |
| LEVEL_AD_Prison | Elevator 7, platform trigger 7, FallingPlatform 1, DamageTrigger 7, Mover 194 | Riders, falling countdown, barrel → OBJECTIVES/Pipe_hole. |
| LEVEL_AD_TheWall_Interior | Elevator 7, platform trigger 7, FallingPlatform 1, Laser 5, DamageTrigger 5 | Elevator continuation and hazard attribution. |
| LEVEL_AD_TheWall | Elevator 2, platform trigger 2, WireWallGate 3, Laser 3, Cannon 1, FuckOffCannon 1 | Personal TNT, queued gate explosion and resulting collision. |
| LEVEL_AD_Wallmarkt | No listed specialized family in that census | Preserve interactable ending and scenery; absence is not proof of no machinery. |
| LEVEL_Asylum_Area69 | Elevator 8, platform trigger 8, CeilingConveyor 2, listeners 35, DamageTrigger 33, Laser 7 | Array discovery, conveyors and hazards. |
| LEVEL_Asylum_Backyard | FerrisWheelCart 1, Rotater 17, Laser 2 | Identify wheel driver; cart/rider/door behavior. |
| LEVEL_Asylum_CoronvirusDen | Platform trigger 1, CovidArenaBase 1, MoverController 1 | Moving arena and boss-linked consequences. |
| LEVEL_Asylum_Forest | ForestBridge 1, Mover 13, Rotater 8 | Bridge fall and subsequent route after join/load. |
| LEVEL_Asylum_Hospital | Elevator 1, platform trigger 1, DamageTrigger 1 | Push_Force_trigger → Pusher.Push; correct victim force. |
| LEVEL_NLM_ComicCon | Elevator 6, platform trigger 11, Scaler 1, DamageTrigger 6 | Riding/scaling and linked urine machinery. |
| LEVEL_NLM_DeathStar | Elevator 4, platform trigger 4, DamageTrigger 1 | Platform collision and restored occupants. |
| LEVEL_NLM_ElonDuskChambers | Elevator 1, platform trigger 1 | TRIGGER_LeonDusk → LeonDuskSwitch.TriggerSwitch; boss progression. |
| LEVEL_NLM_SozebTrain | Elevator 4, platform trigger 4, TimelineSequence 2, MultiRotater 2, Laser 13 | GravityCar (1)/Airlock 3/AirLockTrigger (1) → ResetState; Lever_Mechanism (3) and EndLevelSequence → StartSequence. |
| LEVEL_NLM_VIPZone | Elevator 3, platform trigger 3, DamageTrigger 3, Laser 2 | Later-campaign rides, hazards and resume cleanup. |

Exact ordinary-glass/prop/spawner fixture paths are **not yet verified**. Select and record them from refreshed inventory; do not invent paths. Cat-paw coverage requires a runtime/prefab census beyond stored scene counts.

## Implementation order

Each wave includes live, join and persistence tests. Do not build all live adapters first and postpone persistence.

| Wave | Deliverable | Dependencies / seams |
| --- | --- | --- |
| A | Preserve baseline/saves; refresh all-map callbacks/components. Give each gameplay callback family an owner, side-effect classification, adapter and acceptance ID; record remaining runtime facts. | R01, R08, R13; FINAL approval first. |
| B | Typed adapter contract and bounded versioned records; separate host activation/continuation, passive state and transient presentation. Set wire/schema versions from final records. | R02–R09, R14; Q4. |
| C | Consistent current-world capture for admission/rejoin; replace stale loaded-baseline dependence. Incremental transfer with bounds, ordering, ack/cancellation/backpressure. | G11–G12; before claiming new-family join support. |
| D | Movers/rotaters, elevators/rider carry, falling platforms, conveyors/listeners, bridge/wheel/scaler. Persist phase/direction/time, host continuation and positional audio loop lifecycle. | N01–N08. |
| E | TNT gates, pushers, paw/trap damage, lasers and remaining puzzle/switch/secret callbacks. Preserve requester items, native conditions and one committed outcome. | N08–N11, N22; existing combat/item transactions. |
| F | Shared sequence start/reset/end/skip; correct local camera/input presentation without duplicate gameplay tracks. Add waiting admission and pending saves. | N12, N23; Q2/Q5. |
| G | General breakable/glass discovery and host hit/collision authority. Exact durable geometry plus live-only native sound/particles/local harmless debris. | N13–N18; Q1. |
| H | Gameplay physical props/spawners: authored/runtime IDs, host physics/interest, client requests, passive replicas and durable motion. | Q3; N19–N20; B/C. |
| I | Cross-family fresh-process/checkpoint/join tests, compatibility/refusal, natural routes and performance/transport stress. Update granular docs and package after checks pass. | All waves. |

Proposed module boundaries: **WorldStateAdapters** (capture/validate/apply/host-resume), **WorldMechanisms** (timed controllers/riders), **WorldScenery** (destruction/gameplay props), **WorldSequences** (story/admission). These are proposed modules, not existing files. Extend current modules where clearer; avoid an unrelated wholesale rewrite.

## State and restoration contract

### Identity, authority and events

- Authored identities include inactive objects and stable source/component identity. Exclude personal pickups, players, NPC presentation and runtime clones from authored hashes. Test determinism under differing player positions and optimizer states.
- Runtime props use host-issued instance ID, known prefab/source, generation and tombstones. Pointer reuse creates a new lifetime; reserve IDs before restoration. Never persist native pointers or arbitrary executable callbacks/type names.
- Reuse host-reconstructed hitscan/projectiles; do not also forward a second break request for the same projectile. Melee/collision/push requests need membership, epoch/life, plausible contact, native eligibility and bounded payload/rate validation.
- Preserve explosion-only, ignored-weapon, indestructible, lock and multi-condition rules, plus glass penetration. Item interactions need idempotent accept/reject/refund outcomes across retries, timeouts, simultaneous use and capture. Resolve pending item reservations before declaring a save consistent.
- Host owns shared hazard/crush state and victim hit results. Only the actual victim receives health/armor/camera/force effects, once for its current life. Machinery does not gain an artificial partner-gathering rule.

### Typed state

Each family records sufficient logical phase, pose/reference frame, direction/target, remaining gameplay time, completion markers, conditions, collision/activation and continuation prerequisites. Rider records follow IPlatformMoveable; add parent/local-anchor state only where actual parenting exists.

Barrels/burnables need partial health and queued/burning progress. Gameplay physical props need velocity/spin, sleep/out-of-bounds and spawner membership. Ongoing audio loops follow current state. Old one-shot sounds, particles, rewards, damage and waves never replay on join/load.

### Capture and restoration phases

1. Validate format/game/content/adapter versions and resolve authored identities before changes. Follow Q4 and preserve original files.
2. Reload under the shared barrier; clear transient attacks, stop old continuations and reserve IDs. Keep native NPC despawn before native restore.
3. Restore audited native controller baseline with per-family side-effect suppression. **ISaveable.Load is not the passive client contract.** Native spawner cleanup precedes explicit spawned-object reconstruction.
4. Recreate runtime gameplay objects and references. Apply structure, gates, collision, portals and conditions in dependency order.
5. Restore motion/countdowns; resume only the host's future completion logic. Completed actions do not run again. Shared pause freezes remaining gameplay time.
6. Apply passive peer presentation, safe placement and valid rider contacts. Rebase last-position caches before the first carry update. Report a safe fallback instead of embedding a player in geometry.
7. Acknowledge baseline, adapter records and personal state for one snapshot/epoch, then release. Cancel stale work on travel/disconnect; partial restore failure cannot release an inconsistent world.

Joining an ordinary moving mechanism restores its current phase. For Q5, add a distinct **connected but not admitted** state: P2 waits with status/cancel while P1's noninteractive story continues. Do not start the normal join barrier merely because the socket connected. Begin a fresh capture at actual admission. Integrate readiness/exits/timeouts so an unadmitted guest cannot deadlock a story ending. Directors using unscaled time must still honor host pause.

Ordinary ambient dialogue/tutorial notices remain local unless the audit shows a required shared gameplay consequence; required story presentation follows Q2. Secret discovery is shared progress, not shared pickup removal.

### Payload and performance

Retain roughly 3 ms budgeted capture, bounded queues/backpressure and immutable managed disk-worker data. Use dirty/event-driven registration and staggered interest updates; no full-scene reflection scan every 100 ms. Native physics optimization must account for either player where gameplay props require it.

Measure all-map counts/bytes and destruction bursts. Address the 32 MiB disk versus 4 MiB transfer mismatch with bounded incremental records/chunks and retry, rather than queueing a whole larger baseline. Validate total/per-entry size, ordering and completeness before application; networking stays serviced during holds.

0.9.0 controlled 60 FPS baseline: world work p95 about **3.1 ms**, completed sweep p95 **66.7 ms**, displayed frame p95 **16.7 ms**. Existing native checks require steady world p95 ≤4 ms and sweep p95 ≤200 ms. Retain them and add moving-machinery/destruction/join stress: capture spikes, allocations, queue pressure and payload size. Idle checks alone are insufficient.

## Acceptance matrix

This is the **original acceptance specification**, not a live status table. Use Current remaining work above and the later evidence checkpoints for current results. Record exact map/object paths and evidence as fixtures are selected. Exercise P1, P2, duplicate/stale/simultaneous actions, pause, disconnect/rejoin and reset where applicable.

| ID | Mechanic / scenario | Required observable result |
| --- | --- | --- |
| T01 | All-map callback/catalogue discovery | Deterministic IDs; inactive arrays/listeners/runtime prefabs classified; unknown gameplay callbacks are not silently declared supported. |
| T02 | Elevator riding | P1, P2 and both ride; step on/off, reverse and use with P2 distant from P1 without slipping, duplicate carry or wall embedding. |
| T03 | Mid-ride join/restore | Correct direction/time continues; rider caches rebase; no teleport impulse; both can disembark and continue the route. |
| T04 | Movers/rotaters/multi-step mechanisms | Phase/target/time survives join, rollback and fresh process; end listener executes once. |
| T05 | Falling countdown | P2 activation warns/shakes the correct player, retains delay, falls once and restores collision. |
| T06 | Conveyors/listeners | Area69 listener array discovered; movement/activation/collision and restore agree for either player. |
| T07 | Forest bridge | Fall/static-to-animated transition and subsequent route correct through join and rollback. |
| T08 | Wheel/cart | Actual wheel driver identified; doors/riders coherent before and after save/join. |
| T09 | Scaling machinery | Collision/rider placement follows scale; restore does not carry twice. |
| T10 | TNT gate | Requester consumes once; P1 items unaffected by P2 use; rejected/timed-out retries refund correctly; fuse/destruction persists. |
| T11 | Pushers | Hospital force affects the actual living victim, never substitutes P1 for P2. |
| T12 | Paw/shockwave | Actual prefab/runtime source identified; intended victims receive intended hits once with pause/life gating. |
| T13 | Lasers/crush/hazards | Shared activation/collision/cadence; no duplicate damage from restore or visual replay. |
| T14 | LeonDusk/puzzle callbacks | Either player activates once; animation callbacks do not duplicate boss/world progress. |
| T15 | Story start/reset/skip | Shared sequence/camera/input, P1-only skip, correct final world and one completion; settings/cancel/departure cannot strand input. |
| T16 | Join during story | Visible waiting without freezing P1, no ending deadlock, missed permanent result, duplicate signal or replayed reward. |
| T17 | Secret discovery | Either player credits once; dedup/restore correct; personal rewards remain collectible. |
| T18 | Ordinary breakable hits | P1/P2 hitscan, projectile, melee/kick and explosions where eligible; one committed result and intended collision removed. |
| T19 | Negative break cases | Ignored weapons, explosion-only and indestructible rules hold; stale/ineligible requests cannot bypass them. |
| T20 | Collision breakable | Eligible actual contact shatters from either peer; inactive/ineligible/presentation-only contacts do not. |
| T21 | Glass | Enabled/disabled/hit state, penetration, collision, either-side shooting, join and rollback agree. |
| T22 | Replacement geometry | Neighborhood OBJECTIVES/BreakableWall and Prison OBJECTIVES/Pipe_hole retain exact permanent cutouts; compare meshes/colliders and actual player passage. |
| T23 | Live smash FX | Native sound/particles/local harmless debris once during live break; historical breaks silent on join/load. |
| T24 | Partial barrel damage | Save before detonation, mutate, restore, apply remaining damage: exact threshold retained, not refilled. |
| T25 | Partial burning | Health/progress/ongoing loop resume; burn-completion event does not repeat. |
| T26 | Queued explosion chain | Pause/save/join retains remaining fuse gameplay time and one explosion/progression outcome. |
| T27 | Gameplay physical props | Both players walk/push/kick/shoot, sleep/wake, block routes and interact at distance; restored pose/velocity coherent. |
| T28 | Prop spawner/pooling | Prefab/count/generation/tombstones and fresh-process reconstruction correct; native cleanup does not erase or duplicate rebuilt props. |
| T29 | Personal resources/drops | Shared actions never remove partner pickups; drops keep separate histories; pending reservations cannot corrupt capture. |
| T30 | Current-world join | Load save → move/break/advance → join: P2 sees current world, not old _nativeBaseline. |
| T31 | Persistence matrix | Before/during/after mechanical actions; mutate, rollback, fresh process, host-alone resume then join, repeated restores; next native progression step still works. |
| T32 | Old-save policy | Verified migration retains originals or clear refusal preserves them; no silent partial reset. |
| T33 | Failure recovery | Wrong schema/missing target/truncated or oversized transfer/lost peer/interrupted write preserve recovery files and never release partial state. |
| T34 | Active-world/transport stress | Many moving props/destruction chains, large baseline and delayed consumer remain bounded over TCP and Steam message-carrier paths. |
| T35 | Existing regression suite | Travel/input/focus, actors/combat, doors/keys, urine, endings/results, save identity/backups and native UI smoke still pass. |
| T36 | Natural campaign routes | Both can actually traverse inventoried paths and resume/rejoin meaningful states; record natural play separately from synthetic fixtures/census. |

## Commands and evidence

Run from the repository with GameDir set to the reader's installation; close running games before native checks. These are **existing commands**, not tests run during this planning session:

~~~powershell
$GameDir = Read-Host 'Game folder containing POSTAL Brain Damaged.exe'
./scripts/Build.ps1 -GameDir $GameDir
./scripts/Test-Runtime.ps1 -GameDir $GameDir
./scripts/Test-Travel.ps1 -GameDir $GameDir -WorldRegression -Level LEVEL_AD_Neighborhood
./scripts/Test-Travel.ps1 -GameDir $GameDir -WorldRegression -Level LEVEL_AD_Prison
./scripts/Test-Travel.ps1 -GameDir $GameDir -MissionExits -Level LEVEL_AD_Prison
./scripts/Test-Travel.ps1 -GameDir $GameDir -MissionExits -Level LEVEL_AD_Wallmarkt
./scripts/Test-Travel.ps1 -GameDir $GameDir -Saves -Level LEVEL_AD_Neighborhood
./scripts/Test-Travel.ps1 -GameDir $GameDir -Saves -SaveResume -Level LEVEL_AD_Neighborhood
./scripts/Test-Travel.ps1 -GameDir $GameDir -Saves -SaveResume -SaveReplacePartner -Level LEVEL_AD_Neighborhood
./scripts/Test-Travel.ps1 -GameDir $GameDir -Performance -PerformanceFrameRate 60
./scripts/Test-Travel.ps1 -GameDir $GameDir -Audit -AuditLevels LEVEL_Asylum_Area69
~~~

Add dedicated machinery/scenery/sequence/save-matrix diagnostics during implementation. New flag names are not established yet; do not present proposed switches as runnable. Prefer isolated fresh processes per map where native pool reuse causes cross-map initialization errors.

Maintain per-map/per-family evidence: native class/source identity, exact object path, P1/P2 activation, phase/time, collision/rider outcome, damage attribution, join, rollback/fresh process, event counts and performance. Publish redacted summaries; keep raw logs, snapshots, saves/player IDs, installation manifests and recovered game data ignored.

The 0.9.0 baseline has 22 managed acceptance scenarios and representative native save/UI/performance checks. Its save fixture covers Neighborhood wall/NPC/door, personal pickups/generated loot, autosave/checkpoint/results. It does not establish every natural elevator, trap or timeline. Serialization success alone does not earn a synced checkmark.

## Original full acceptance gate

- [x] Q1–Q6 resolved; gameplay choices recorded.
- [x] Maintainer explicitly confirms final shared understanding before implementation.
- [ ] All applicable T01–T36 checks have evidence; newly discovered blockers are resolved or explicitly renegotiated.
- [ ] Inventory, synthetic fixtures and meaningful natural progression checks remain distinguishable.
- [ ] No known shared route/collision/trigger/persistence disagreement remains within the agreed scope.
- [ ] Current-state join, repeated rollback and fresh-process load preserve future progression without historical callbacks.
- [ ] Compatibility, native serializer handling and disk/wire bounds follow the agreed contract.
- [ ] Personal systems, exits, independent respawns and solo isolation retain their rules.
- [ ] Capture/frame/queue budgets pass idle and active-world checks.
- [ ] Granular sync tracker, roadmap and save/protocol/verification docs reflect actual results. Added rows do not inflate milestone score.
- [ ] Build/install/package matching DLLs after implementation authorization and successful checks. No commit or push unless separately requested.

## Implementation checkpoint (October 14)

The megapass remains **in progress**. These notes distinguish implemented adapters from completed campaign validation; they do not mark the completion gate as passed.

- Development source is now 0.10.0 / wire 9 / save schema 2. The published/package baseline remains 0.9.0 until final validation.
- Joining captures a fresh native baseline, with bounded chunking/backpressure up to the disk format limit. Incomplete schema-1 saves are refused without altering originals.
- Added scenery adapters for glass, partial barrel/burn state, authored and spawned physics props, structural fragments, and live-only smash presentation. Spawned props retain source spawner/prefab identities and native pool lifetimes.
- Added explicit fall, scale and TNT-gate clocks; rider rebasing/contact reconstruction; remote pushes; machinery audio state and owned audio pause handling.
- Added shared secret identities/counters and immutable interaction request IDs with cached host results.
- Story video playback, P1-only skipping, waiting late-join admission, input recovery and shared credits passed the dedicated native two-peer `-Stories` regression.
- Native `-Scenery` regressions passed in Backyard and Neighborhood: P2 glass/breakable actions, exact partial burn/barrel damage, disk restoration, and load → change world → late join. A teardown callback gap found during these tests was corrected; retain final log checks.
- The train runtime audit records actual animation, signal, control and FMOD bindings. Its two timelines are mechanical/progression sequences. Passive playback and continuation adapters still require the focused machinery tests.
- Network/save acceptance tests currently cover 26 scenarios, including bounded story metadata and non-destructive legacy-save refusal. Re-run after final source changes.
- Fresh-process catalogue and two-peer readiness/input checks passed across all 15 campaign maps through `Test-WorldCampaign.ps1 -WithPeer` on the foundation build. Later scripted-action additions require their focused native regressions; this does not certify all natural progression routes. A single-process cross-map audit encountered native pool reuse errors and is not accepted as equivalent evidence.

Still required: complete the all-map matrix; focused motion/riding/hazard/fuse/sequence/prop-pooling tests; fresh-process persistence and failure checks for the new families; performance/UI/explosion regressions; final granular tracker/documentation update; final installation/package hash verification. No commit or push is authorized by this plan.

### New implementation lookups

| File | Entry points / purpose |
| --- | --- |
| [WorldNativeCapture.cs](../../src/PBDMP.Plugin/WorldNativeCapture.cs) | `TickNativeJoin`, `CaptureCurrentBaseline`, `SendBaseline`, `ReceiveBaseline`, `DrainTransfers`. |
| [WorldSceneryCatalogue.cs](../../src/PBDMP.Plugin/WorldSceneryCatalogue.cs) | Authored scenery, fragment bodies, spawner/prefab references, mechanism and secret discovery. |
| [WorldScenery.cs](../../src/PBDMP.Plugin/WorldScenery.cs) | Typed scenery capture/application, physical lifetimes and validated P2 requests. |
| [WorldFragments.cs](../../src/PBDMP.Plugin/WorldFragments.cs) | Gameplay fragment pose, collision, velocity and sleep state. |
| [WorldMechanisms.cs](../../src/PBDMP.Plugin/WorldMechanisms.cs) | Barrel/fall/scale clocks, platform carry, rider rebasing and player force/shake effects. |
| [WorldGateFuses.cs](../../src/PBDMP.Plugin/WorldGateFuses.cs) | Native interaction commit followed by a durable TNT countdown. |
| [WorldMechanismAudio.cs](../../src/PBDMP.Plugin/WorldMechanismAudio.cs) | Machinery loops, restored playback position and owned pause/resume. |
| [WorldSequences.cs](../../src/PBDMP.Plugin/WorldSequences.cs), [WorldSequenceAudit.cs](../../src/PBDMP.Plugin/WorldSequenceAudit.cs) | Passive timeline phase/presentation and actual bindings/signals; host continuation guards. |
| [SessionStories.cs](../../src/PBDMP.Plugin/SessionStories.cs), [StoryDiagnostics.cs](../../src/PBDMP.Plugin/StoryDiagnostics.cs) | Shared native video menus, admission and skip/credits regressions. |
| [WorldSecrets.cs](../../src/PBDMP.Plugin/WorldSecrets.cs) | Shared discovery once, personal native notices. |
| [WorldPropPush.cs](../../src/PBDMP.Plugin/WorldPropPush.cs) | Local contacts become validated host body impulses. |
| [WorldInteractionLedger.cs](../../src/PBDMP.Plugin/WorldInteractionLedger.cs) | Request/result deduplication and confirmed retry semantics. |
| [SceneryDiagnostics.cs](../../src/PBDMP.Plugin/SceneryDiagnostics.cs) | Authored scenery actions, partial damage, restoration and current-world rejoin fixtures. |
| [Test-WorldCampaign.ps1](../../scripts/Test-WorldCampaign.ps1) | Fresh-process catalogue/snapshot audits with optional per-map peer/input checks. |

The optional particle audit now accepts additional reader-owned serialized files. The expanded allowlist was checked against `sharedassets0.assets`, `sharedassets15.assets` and `level15`; no previously accepted source was removed. Reproduce with `Audit-VisualParticles.py --game-dir <GameDir> --additional-asset sharedassets15.assets --additional-asset level15` in a Python environment containing UnityPy. The generated allowlist contains names only; raw audit output stays in ignored `.local` storage.


### Machinery validation checkpoint

The full megapass is still unfinished. Additional implemented adapters are being validated before packaging or advancing the public sync score.

- The Wall Interior two-peer `-Mechanisms` regression now passes: real native mover/rotater/elevator phase and pose, delayed fall, shared pause, consistent disk capture, both-peer restoration, future native endpoints and exactly one mover/rotater completion callback. Fixture durations are extended to make in-flight capture deterministic; this is separate from natural-route coverage.
- That regression caught and corrected unsafe dormant elevator reset/load assumptions, invalid null native callback delegates during continuation, missing rotater tween continuation after restoration, and the delayed fall's missing completed-state flag. Native loader state restoration and future callbacks remain separate operations.
- Native tutorials preserve their noncancelable dismiss input under a shared pause. A reproduced Dildo Bow tutorial deadlock was corrected and verified by the map/input matrix. Saving waits while a local tutorial is active.
- A bounded `WorldBaseline` receiver now has direct paging/corruption/cancellation/limit checks plus 9 MiB transfers over TCP and a bounded message carrier with a delayed consumer. All 26 managed acceptance scenarios passed.
- Added host cannon target selection for either player without relocating P1, world-event-backed trigger classification, requester-owned scripted teleport execution, personal tutorial/HUD callback isolation, and durable referenced `LocalRotation` state. Focused cannon/teleporter/rotation checks remain outstanding.
- Added native prop-spawn coroutine capture gating and a full scan at complete capture. The native `-Props` fixture now passes: P2 pushes an authored body; complete native spawn batches are captured; pose/velocity and source ownership restore; pooling sends tombstones; reused objects receive new lifetime IDs; current-world rejoin restores them. A reader-owned native spawner fixture is explicitly separate from the COVID boss’s runtime cosmetic spawner.
- Added rocket animation/launch state and a durable quarter-second native launch clock. Its original easing, target and completion semantics were checked against native code. VIP sequence/rollback tests remain outstanding.
- Train `-Mechanisms` coverage includes the authored sleeping-car timeline and its animated bindings, plus multi-step rotation where present. It has not yet passed its focused native run.
- Native diagnostics now allow asynchronous teardown to settle and reject relevant Unity/native callback exceptions before accepting pass markers. Temporary diagnosis traces are removed after each resolved loop; retain the final source scan.

Additional lookups: [SessionTutorials.cs](../../src/PBDMP.Plugin/SessionTutorials.cs), [WorldBaseline.cs](../../src/PBDMP.Network/WorldBaseline.cs), [WorldSpawnerCapture.cs](../../src/PBDMP.Plugin/WorldSpawnerCapture.cs), [WorldCannon.cs](../../src/PBDMP.Plugin/WorldCannon.cs), [WorldScriptedActions.cs](../../src/PBDMP.Plugin/WorldScriptedActions.cs), [WorldRockets.cs](../../src/PBDMP.Plugin/WorldRockets.cs), [MechanismDiagnostics.cs](../../src/PBDMP.Plugin/MechanismDiagnostics.cs), and [PropDiagnostics.cs](../../src/PBDMP.Plugin/PropDiagnostics.cs).

Native research references: `<Decompiled>/CSharp/Assembly-CSharp/Hyperstrange/PBD/Contraptions/{Elevator,Rotater,FallingPlatform,PhysicalObjectSpawner,Cannon,Teleporter,RocketSequence}.cs`, `Hyperstrange/PBD/PlayerComponents/PlayerTutorialComponent.cs`, and `Hyperstrange/Base/{LocalRotation,GameEvent}.cs`. The matching native ISIL dumps establish callback order, optional helper assumptions, timing and remaining-motion behavior; keep recovered game data out of the repository.

### Prop and arena validation checkpoint

- Native spawner `Load` returns objects to the pool without clearing its ownership list. Shared sources now clear that list after pooling, preventing duplicate ownership and repeated returns after restoration. Native reactivation also precedes restored velocity, with deferred reset coroutines cancelled.
- A machine identified as a door by hierarchy name previously received two restore paths. Typed machine/encounter restoration now owns that continuation. Rotater continuation uses its remaining duration, rather than restarting the configured full duration. The train fixture verifies one future mover/rotater completion callback.
- The COVID arena now has explicit segment-order/base-position state, native syringe animation/eligibility state, and remaining cooldown capture. P1 owns arena and syringe actions; local gravity-volume contacts remain personal against shared volume state. Controller enable/disable changes are published even when primitive fields are unchanged.
- The native `-Arena` regression passed P2 button activation, shifted segment order, shared pause, disk restoration on both peers, syringe animation/eligibility, and future cooldown completion. The expanded current-world disconnect/rejoin variant is the next regression, not yet accepted.
- The train pose/continuation regression revealed that a stopped director can reset its reported time while retaining a later pose. Completed phase tracking and a stricter peak-playback assertion are under validation. Earlier stop-state-only results are not sufficient timeline acceptance.
- All 26 managed network/save scenarios were rerun successfully, including large TCP/message-carrier baselines and compatibility refusal. No final package or sync-score increase has been made.

Additional lookups: [WorldArena.cs](../../src/PBDMP.Plugin/WorldArena.cs), [ArenaDiagnostics.cs](../../src/PBDMP.Plugin/ArenaDiagnostics.cs). Native references: `<Decompiled>/CSharp/Assembly-CSharp/Hyperstrange/PBD/Contraptions/{CovidArenaController,CovidArenaSegment,CovidArenaBase,AntiCovidSyringeController,AntiCovidSyringe}.cs`, `Hyperstrange/PBD/GravityChanger.cs`, and `Hyperstrange/Base/Cooldown.cs`, with matching ISIL bodies. Native update registration handles remain local.


### Focused validation and remaining work

The pass is still in development; there is no final 0.10 package or completed campaign acceptance claim.

- Train native timelines now pass the stricter peak-phase check, one future mover/rotater callback, and completed-phase current-world disconnect/rejoin. This supersedes the earlier pending timeline notes above.
- VIP launch machinery passes capture during the native quarter-second launch, pause, both-peer restoration and one future head-hit callback. ComicCon scale continuation also passes its focused native fixture.
- The expanded COVID arena regression passes native syringe presentation, current ordering/cooldowns, disk rollback and current-world disconnect/rejoin. Its visual sources were audited against the matching reader-owned level assets.
- Wall Interior live and restored elevator riding passed before the newer carrier change. The Area69 rider fixture uncovered an additional blocker: network-delayed rider positioning and replica steps colliding with the already-moved lift deck. Atomic authored platform references, contact reconstruction and a replica carry correction are implemented; the strengthened Area69 test and cross-map rider regressions remain required. Do not treat the earlier permissive rider drift threshold as final acceptance.
- Managed acceptance now has 27 passing scenarios, including bounded platform context in poses and personal save records. Native validation is a separate gate.
- Native hazard contact lists are cleaned before destroying the host-side partner proxy, addressing the reproduced dangling-collider callback during disconnect. The dedicated victim/disconnect matrix remains required.

Outstanding: finish rider and TNT-gate regressions; cannon, teleporter, secret and specialised hazard/trigger checks; destruction eligibility/collision/fragment and queued-chain cases; fresh-process save/rejoin/rollback and failure recovery for the expanded world families; natural campaign routes; performance/audio/UI/existing-feature regressions; final granular documentation and matching installed/package hashes. The public milestone score remains unchanged. No commit or push has been performed.

Rider lookups: [WorldCarriers.cs](../../src/PBDMP.Plugin/WorldCarriers.cs), [PlayerCarrier.cs](../../src/PBDMP.Network/PlayerCarrier.cs), [WorldSceneryCatalogue.cs](../../src/PBDMP.Plugin/WorldSceneryCatalogue.cs), [WorldMechanisms.cs](../../src/PBDMP.Plugin/WorldMechanisms.cs), and the native references `<Decompiled>/CSharp/Assembly-CSharp/Hyperstrange/PBD/MovingPlatformTrigger.cs` and `PlayerComponents/PlayerMovementComponent.cs` with matching ISIL bodies.


### Area69 rider regression resolved

The strengthened native two-peer Area69 machinery regression now passes: P2 boards the actual mesh floor, remains alive with bounded relative drift during live travel, survives shared pause/disk rollback, rides the restored platform and reaches the future native machinery endpoints. The generated save also contains P2's authored carrier reference. Temporary rider probes were removed after the green reproduction.

The fix covers distinct problems exposed by this test: rider volumes referenced from an elevator-root behaviour; stale native child-contact membership; a replicated deck moving through a character before its carry; and P1 evaluating a network-delayed rider against a newer platform. Replica carry checks actual volume occupancy on both sides of the received motion step, excludes its own deck during the native character sweep while retaining external collision, and does not reapply motion to host-owned props. Poses and saves keep an atomic platform-relative feet reference. Cross-map riders, ceiling/obstruction checks and performance remain part of the outstanding acceptance matrix.


### Campaign playtest checkpoint

At the maintainer's request, the immediate pass was limited to campaign blockers before a playtest. Broader save/recovery, natural campaign coverage, performance/audio/UI checks and final release validation are deferred; the megapass remains unfinished.

- The Wall native two-peer machinery fixture passes on the cleaned rider build: actual P2 TNT planting, immutable duplicate requests, exact requester consumption with P1 inventory unchanged, a partially elapsed fuse, shared pause, both-peer disk restoration, future gate destruction and one completion callback. Its elevator also passes live/restored riding with the stricter drift/alive check.
- Neighborhood's strict native world regression and Prison's explicitly scoped progression-only regression pass: P2 authoritative hit particles, powered urine/fire, exact explosion-linked permanent geometry, special explosion replay and a native barrel hit without losing the session. Prison explicitly verifies OBJECTIVES/Pipe_hole. These are focused authored fixtures, not completed natural playthroughs.
- The initial Prison run caught a new barrel-state disconnect. Native HitPoints.Subtract stores below-zero overkill; the client adapter incorrectly rejected it. Signed native health is now preserved under existing finite wire bounds, including the matching burnable adapter check. The strengthened Prison regression verifies actual negative barrel health and the subsequent shared pipe opening without disconnecting. Broader burn-overkill cases remain part of the deferred scenery checks.
- Native barrel/burn destruction uses Extension_GameObject.Hide, which hides components while retaining the object lifecycle. Replica state now uses the same operation instead of deactivating the root. The strict Prison rerun subsequently caught a point-light mismatch, retained as an outstanding visual issue. The default strict test is unchanged. Optional Test-Travel -WorldRegression -ProgressionOnly records mismatches only for light-only transforms with no descendant colliders; geometry, collision, state and connection assertions still run. Raw visual-difference listings stay in ignored diagnostic output. Complete lighting acceptance remains deferred.
- The local playtest ZIP contains only the matching two mod DLLs, MIT license and playtest installation notes. It is labelled development/playtest rather than a completed release. Both players must use the same build. Older 0.9 co-op saves remain preserved and refused by the expanded world format.

No commit or push is included. Resume the remaining acceptance work from the outstanding matrix above after the playtest; any reported campaign blocker takes priority.


## Reported campaign playtest follow-up

These reports are required follow-up work before completing the megapass. Checkboxes below remain open until their real paths are verified.

- [x] Replace repeated checkpoint/interest-volume autosaves with a five-minute gameplay timer; manual/quicksaves stay explicit, three autosave slots rotate, and pause/loading time does not advance the timer.
- [x] Restore from the native pause/co-op-save UI without leaving either player's movement/mouse or time frozen. Normal live restore, rollback and progressed-save input replay pass; expanded failure recovery remains part of the wider acceptance matrix. Originals are preserved.
- [x] Show a native-themed Autosaving/Saving/Loading overlay for shared capture, restoration and map-loading holds.
- [x] Replay native tank drain endpoint overshoot (reported FillPercentage -0.005902154) without disconnecting P2; verify native completion, directly linked machinery and current-world rejoin. Natural full-drain timing remains outside this focused fixture.
- [x] Synchronize collectible posters and their shared level-result progress, including current-world join and saves.
- [x] Restore the missing large native blood-hit splatter on P2, separately from existing smaller blood effects.
- [x] Open P2's native pause screen during a real host pause, show Host has paused, and automatically close the host-owned screen on resume while preserving a pre-existing local menu.
- [x] Fix the Neighborhood garage view/occlusion through the exploded wall before P2 crosses the threshold; distinguish render visibility from actual scene loading.
- [x] Clear P1's urine tutorial prompt when P2 completes the breaker interaction; other personal tutorials remain personal.
- [x] Review playtest warnings/errors and limit repeated missing remote weapon geometry messages. Raw logs/private save identities remain ignored local evidence.
- [x] Reproduce and fix runtime save-catalogue drift from seven detached NPC corpse pieces. Scene-load authorship retains legitimate map props and excludes those pieces. An older incompatible primary can use its exact compatible backup with notice; originals remain untouched and missing state is not invented.

The reports do not advance the milestone score. No commit or push is authorized.

### Save/input follow-up evidence — October 15

- Replayed a preserved progressed autosave: the original path had GAMEPLAY maps enabled and time running but `CancelInput` still true. Native ISIL confirms `PlayerSpawnPoint.Load` calls `Player.OnLevelStart`; the missing matching `OnLevelStartReady` was the input/view blocker. [NativeSaveBridge](../../src/PBDMP.Plugin/NativeSaveBridge.cs) now completes the pair while preserving tutorial and session-owned barriers.
- Both native peers pass live manual load, checkpoint rollback, timer rejection of repeated checkpoint/autosave calls, three-slot rotation/backup, personal pickups/drops, completed-map results and hold-overlay release. A progressed-save replay also verifies actual P2 displacement and view rotation from real keyboard/mouse input after focus round trips. Native overlays were inspected in rendered frames.
- [AutosaveClock](../../src/PBDMP.Network/AutosaveClock.cs) measures active gameplay only; [CoopSaveService](../../src/PBDMP.Plugin/CoopSaveService.cs) applies the 5–10 minute host setting and queues a single due capture. Managed acceptance now has 29 passing scenarios, including timer pause/reset/no-catch-up and native fill endpoint normalization.
- Warning review: missing remote pickup geometry now reports once per avatar instead of flooding every weapon switch. It does not fabricate a missing model. Existing IL2CPP diagnostic-signature warnings, native FMOD MusicOffset warnings and stale-target DOTween warnings remain visible rather than being globally suppressed. Runtime catalogue drift is a separate compatibility issue, retained for investigation; incompatible files remain preserved/refused rather than partially loaded.
- Regression fixture correction: a dynamically appended scene-root checkpoint had different peer paths once local rendering pools diverged. The test fixture now anchors under an authored checkpoint at a fixed child index. This changes diagnostics, not network authority validation.

### Previous handoff — save/input fixes (superseded below)

- Installed/package build: **78BD8DD945E4CB7C**, version 0.10.0, protocol 9, save schema 2. Both installed DLLs and the diagnostic peer match the verified ZIP. No commit/push.
- Native runs: visible two-peer live save/timer/rollback/results/overlay checks pass; progressed-save replay with real WASD/mouse displacement and rotation passes; latest progressed-save replay plus a catalogue rebuild also passes. Originals remain untouched. The exact native tank underflow first reproduced the reported disconnect, then the focused tank/directly-linked machinery/fill/rejoin fixture passes with normalization.
- The broad Hospital urine/world fixture still finds a surgical-table phase difference on late join and an animated jump-pad presentation mismatch. Those are retained as wider acceptance work. The focused tank fixture follows the selected tank and authored linked moving/scaling listeners; it does not waive a failure in the strict whole-map fixture.
- Remaining gameplay reports above stay open. The accepted milestone score stays at 90%; this is a development playtest, not megapass completion.

## World consistency and presentation follow-up

This completes the focused reported-issue work, not the whole megapass. The outstanding specialised machinery/hazard/scenery cases, expanded failure matrix, natural campaign routes and final release/performance acceptance remain above. The accepted milestone score stays at 90%. No commit or push is included.

- Catalogue reproduction: a preserved progressed backup creates seven detached `PhysicalObject` corpse pieces with neither a Clone suffix nor a pool source. Rebuilding previously admitted them as authored map props. A GUID-only prototype also removed four legitimate swing bones, so the final implementation captures safe authored physical components at scene load, keeps that membership across sessions, and clears it on scene unload. The exact backup/rebuild now retains the original authored manifest; the mismatching primary is preserved and recovered only through a fully compatible previous backup.
- Hospital reproduction: native local rotation and fill-driven callbacks could overwrite shared surface poses. After guarding those writers, a paused/current-host comparison still reproduced stale render transforms while P2's kinematic rigidbody held the correct pose. Replica physics interpolation is now disabled under mod ownership and restored on cleanup. The strict whole-map urine/live/late-join fixture passes with full renderer, collision, activation and pose checks. Comparisons use the current paused host state rather than a settling prop sampled before the join.
- Garage reproduction: native hide/show also opens/closes occlusion portals. The surface adapter now carries those bits. The native garage fixture compares authored portals before crossing, verifies the permanent wall cutout and controller passage, and restores current state on rejoin. P2 completing the fuse also retires P1's exact trigger-owned notification; an unrelated notice remains visible.
- Posters: either player requests one host-owned collection; result count, durable state, save restoration and current-world joining are shared. Current-state catch-up is silent, while resource pickups remain personal. Changed-state capture avoids repeated unchanged-poster traffic.
- Blood: the native particle hierarchy itself matches the original emitter counts. The missing route was native NPC material impacts, which were excluded while a synthetic callback guessed an override. The game-selected native effect now reaches the peer; P2 keeps immediate harmless local presentation and ignores its echo. Enemy damage remains host-owned.
- Pause: real menu pause has its own atomic travel bit, distinct from save/loading holds. P2 receives the native pause screen and Host has paused notice. Host resume releases only the host-owned menu; an independently opened P2 menu stays open. Native fade and gameplay input are covered by the focused regression.

| Lookup | Purpose |
| --- | --- |
| [WorldAuthoredProps.cs](../../src/PBDMP.Plugin/WorldAuthoredProps.cs), [WorldSceneryCatalogue.cs](../../src/PBDMP.Plugin/WorldSceneryCatalogue.cs) | Scene authorship and corpse/runtime exclusion. |
| [WorldBodyPresentation.cs](../../src/PBDMP.Plugin/WorldBodyPresentation.cs), [WorldScriptedActions.cs](../../src/PBDMP.Plugin/WorldScriptedActions.cs) | Replica interpolation and native-writer ownership. |
| [WorldPosters.cs](../../src/PBDMP.Plugin/WorldPosters.cs) | Poster authority, shared counts, silent restoration and old history union. |
| [WorldTriggerHints.cs](../../src/PBDMP.Plugin/WorldTriggerHints.cs), [SharedUrine.cs](../../src/PBDMP.Plugin/SharedUrine.cs) | Exact hint retirement and portal surface bits. |
| [SessionPauseMenu.cs](../../src/PBDMP.Plugin/SessionPauseMenu.cs), [Protocol.cs](../../src/PBDMP.Network/Protocol.cs) | Host-pause ownership and protocol-10 metadata. |
| [SharedExplosions.cs](../../src/PBDMP.Plugin/SharedExplosions.cs) | Native impact asset relay and echo suppression. |
| [ReportedPresentationDiagnostics.cs](../../src/PBDMP.Plugin/ReportedPresentationDiagnostics.cs), [BloodPresentationDiagnostics.cs](../../src/PBDMP.Plugin/BloodPresentationDiagnostics.cs) | Poster/pause/native-burst regressions. |
| [SavedPlaytestDiagnostics.cs](../../src/PBDMP.Plugin/SavedPlaytestDiagnostics.cs), [CoopSave.cs](../../src/PBDMP.Network/CoopSave.cs) | Exact progressed-save replay, compatible recovery and protected backups. |

Native research references: `<Decompiled>/CSharp/Assembly-CSharp/Hyperstrange/PBD/{SolidManager,ParticleController,NpcRagdoll,CodexController,CodexId}.cs`, `Contraptions/{Pickup,CodexPickup,Trigger,PlayerTriggerNotificationComponent,Toggler,PhysicalObject,PissFillListenerMovingObject,PissFillListenerScalingObject}.cs`, `Hyperstrange/Base/Extension_GameObject.cs`, and their matching native ISIL dumps. Recovered game sources/assets remain outside the repository.

### Final focused rider regression

The Area69 native machinery/rider check passes again after the host-side hit surface follows validated platform state in LateUpdate, avoiding a frame of stale placement after native elevator animation. The final client deck correction is retained during pause; travel/restoration rebase instead of applying a saved journey as movement. Native hazards remain active. The test samples actual restored motion under a finite deadline and preserves its original alive/drift/phase assertions. This closes the focused regression, not all natural rides, obstruction cases or the overall acceptance gate.

Lookup: [RemoteActor.cs](../../src/PBDMP.Plugin/RemoteActor.cs), [WorldCarriers.cs](../../src/PBDMP.Plugin/WorldCarriers.cs), [WorldMechanisms.cs](../../src/PBDMP.Plugin/WorldMechanisms.cs), [MechanismDiagnostics.cs](../../src/PBDMP.Plugin/MechanismDiagnostics.cs). Temporary collision and carrier traces have been removed from source.
