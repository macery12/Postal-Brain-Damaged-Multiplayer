# Multiplayer synchronization progress

**October 16, 2026 — megapass implementation closeout.** Reproduced loaded-map join and runtime/stale-contact hazard bugs are fixed. Focused bridge, wheel/door, rider, scenery/prop and save checks pass. Remaining natural campaign and edge-case acceptance is recorded in the [maintainer playtest checklist](megapass-playtest.md). Those cases retain partial markers where appropriate.

Current source: **PBDMP 0.10.0 / protocol 11 / save schema 2**. Installed/exported animation build **C990AE9ED22FC33F** supersedes megapass build **E19C56EB3B893F6A**. The [animation check guide](weapon-animation-testing.md) describes the focused presentation pass. Both players need matching plugin and network DLLs. [Verification](verification.md) records exact evidence; older build/timing references below are historical.

**Development coverage: 93% (19.5 / 21 points). Last accepted milestone baseline: 90% (19 / 21).** The handoff does not change the scoring rubric or turn untested routes into verified results. [Scoring details](#progress-score).

## Current stopping point

The focused weapon/animation pass is installed: explicit weapon grips (including the saw and dual variants), supporting hand poses, movement/action metadata, native clip blending, grounded crouch feet and pause-safe animation clocks. All 621 exact-build captures, connected equip/action checks and retained performance budgets pass. First-person internal mechanism parity and player-death presentation remain separate polish; see [animation checks](weapon-animation-testing.md).

P2 joining an already-loaded Prison map now uses native same-map restart and reuses duplicate in-flight admission loads. Pooled paw replicas cannot apply duplicate damage on P2. Host-owned damage triggers revalidate P2's current contact before ticking, preventing stale rider contacts from causing extra hits while preserving genuine damage. Shared pause/restore/loading holds suppress these hazards.

Native checks cover Forest bridge state and geometry through pause/rollback/completion; Backyard's actual wheel driver and linked cart doors; complete restored Area69 elevator survival; scenery advancement after restore/current-world rejoin; prop push/pool/restore/rejoin; live saves and fresh-process host-alone resume with the partner returning. Native story/pause/input checks and 34 managed network plus eight reporting scenarios also pass.

The existing poster, blood-impact, garage/portal/hint, urine, cannon, conveyor, gravity, pusher and personal-resource behavior remains in the build. The controlled FPS improvements are in the [brief performance report](../preformence_report.md). Busy-world/network optimization and longer campaign traversal remain follow-up work.

## Reading the tables

Each shared feature has one checkmark in its current status column. **Synced** means the listed behavior is coordinated between players; it does not mean every campaign situation has passed manual testing. **Partially synced** identifies an implemented feature with remaining gaps. **Planned** identifies work without a synchronization implementation yet. Comments describe coverage and limitations.

Personal systems appear in a separate **Intentionally local** table. Those are design choices, not unfinished sync.

## Session and campaign

| Feature | Planned | Partially synced | Synced | Comments |
| --- | :---: | :---: | :---: | --- |
| Host a Steam lobby | | | ✅ | P1 creates the session through Steam. |
| Join a Steam friend | | | ✅ | Friends can join the host through the multiplayer menu. |
| Steam invitations | | | ✅ | Native Steam overlay invitations and accepted invites connect to the lobby. |
| Steam relay transport | | | ✅ | Steam networking carries the same host-authoritative session state. |
| Steam player cards | | | ✅ | Multiplayer UI displays connected player names and avatars. |
| Host by IP/port | | | ✅ | P1 creates the two-player session. |
| Join by IP/port | | | ✅ | P2 connects directly to the host by IP and port. |
| Game/mod compatibility checks | | | ✅ | Both players must use matching game and exact mod builds. |
| Reconnect | | | ✅ | A returning client receives current supported state. |
| Peer timeout | | | ✅ | Lost connections end waiting instead of leaving the session stuck. |
| P2 disconnect | | | ✅ | P1 continues playing. |
| P1 departure | | | ✅ | Ends the session and returns P2 to the main menu. |
| Main-menu multiplayer controls | | | ✅ | Native-themed lobby, player cards, Steam/Direct IP controls and session status. |
| Main-menu news panel | | | ✅ | Local information panel uses native artwork/fonts; hidden behind menus/dialogs and during gameplay. |
| Bug-report shortcut | | | ✅ | Opens the structured issue form with software versions/build IDs and in-memory last-session context; attachments remain manual. Template must be published on GitHub. |
| Last-session report context | | | ✅ | Local-only map, host/joining role and transport survive title/disconnect; cleared by a new game launch. |
| Pause-menu multiplayer controls | | | ✅ | Native-themed entry and F8 shortcut. |
| Start campaign level | | | ✅ | P1 starts; P2 follows. Built-in campaign only. |
| Restart current level | | | ✅ | P1 restarts both players' level. |
| Next-level travel | | | ✅ | P2 follows P1's campaign transition. |
| Return to main menu | | | ✅ | P1 controls shared campaign travel. |
| Difficulty selection | | | ✅ | P1 selects the session difficulty. |
| P2 level-complete screen | | | ✅ | P1's accepted finish opens the native end-level UI for both players. |
| Level-complete statistics | | | ✅ | P2 displays host counts and the exact completion time; travel remains host-controlled. |
| Block independent P2 travel | | | ✅ | P2 cannot start, load or restart a level independently. |
| Shared host pause | | | ✅ | P1 freezes/resumes gameplay for both players. |
| Host-pause screen on P2 |  |  | ✅ | Real host menu pauses open the native P2 pause screen; autosave/loading holds do not open it. |
| Host-pause caption | | | ✅ | Native-themed Host has paused notice in the pause screen's empty area. |
| Automatic guest menu resume | | | ✅ | Host resume closes the screen opened by host pause after its native fade. |
| Preserve P2-owned pause menus | | | ✅ | A menu P2 opened independently stays open when P1 resumes. |
| P2 menu during gameplay | | | ✅ | P2 can open a menu without pausing P1. |
| Wait for both players to load | | | ✅ | Gameplay waits for readiness and the current shared snapshot. |
| Failed client load recovery | | | ✅ | P2 returns to the menu; P1 can continue. A transport timing check remains under observation. |
| Menu/pause input release | | | ✅ | Native checks cover overlapping pause/menu locks and focus round trips. |
| Isolate solo saves and profiles | | | ✅ | Native solo writes stay blocked; dedicated host-owned co-op slots provide multiplayer persistence. |

## Player presentation and respawn

| Feature | Planned | Partially synced | Synced | Comments |
| --- | :---: | :---: | :---: | --- |
| Player position | | | ✅ | Remote location and height follow the player. |
| Player aim | | | ✅ | Yaw/pitch and weapon aim follow the player. |
| Living/dead visibility | | | ✅ | Remote avatar visibility follows life state. |
| Respawn life generation | | | ✅ | Separates the new life from stale events. |
| P2 gameplay target on P1 | | | ✅ | Native targeting/hit proxy is separate from the visible avatar. |
| Native Dude body model | | | ✅ | Uses the game's coat/gown rig. |
| Weapon geometry and grip alignment | | | ✅ | Authored pickup orientation/scale and reachable palm/support targets; 27 available variants pass multi-angle grip checks. |
| Saw holding orientation | | | ✅ | Removes pickup display tilt and aligns the blade forward. |
| Saw teeth animation | | | ✅ | Native active/idle texture scrolling on owned material copies. |
| Supporting-hand placement | | | ✅ | Two-arm reach follows each weapon profile through aim and action poses. |
| Bow hold and draw | | | ✅ | Left-hand hold; right hand draws toward the player along the weapon axis. |
| Dual-wield geometry and grips | | | ✅ | Resolves missing-pickup variants to their base geometry; independent left/right holding. |
| Dual-wield handed muzzle flashes | | | ✅ | Native visual sources follow the reported firing hand. |
| Weapon aim interpolation | | | ✅ | Smoothed pitch/yaw without a bone-transform network stream. |
| Weapon swap presentation | | | ✅ | Native swap clip and a short lower/raise pose. |
| Charge holding pose | | | ✅ | Charge metadata drives the supporting pose; resources remain personal. |
| Blocking holding pose | | | ✅ | Native block state drives the shared visual pose. |
| Shield holding pose | | | ✅ | Native shield state raises the weapon and supporting arms. |
| Melee and kick presentation | | | ✅ | Native attack phase/loop and kick notifications drive visual poses. |
| Crouching presentation | | | ✅ | Uniform body proportions, bent legs, grounded feet and weapon grips. |
| Sliding presentation | | | ✅ | Uses native slide clips from reported movement state. |
| Shared-pause animation clocks | | | ✅ | Clip/recoil/grip/teeth clocks stop during shared holds. |
| Idle animation | | | ✅ | Uses native clips. |
| Armed holding animation | | | ✅ | Uses native clips; grip polish is tracked separately. |
| Movement animation | | | ✅ | Uses native clips. |
| Shooting animation | | | ✅ | Uses native clips. |
| Weapon internal mechanism/recovery parity | | ✅ | | Recoil and native character-family recovery exist; dedicated pump/bolt/barrel and every first-person mechanism remain polish. No universal reload gameplay is added. |
| Jump and airborne presentation | | | ✅ | Native movement snapshots select jump-start and air clips; connected state delivery is covered. |
| Complete player death animation transitions | ✅ | | | Life-state visibility exists; complete animation presentation remains unfinished. |
| Independent respawn timer | | | ✅ | Three seconds; waits during P1's shared pause. |
| Respawn location | | | ✅ | Normal map spawn; the world is not reset. |
| Respawn health reset | | | ✅ | Full personal health. |
| Respawn armor reset | | | ✅ | Zero personal armor. |
| Retain gear after death | | | ✅ | Retains earned weapons. |
| Retain ammo after death | | | ✅ | Preserves remaining ammunition, without a refill. |
| Retain items after death | | | ✅ | Preserves personal item counts. |
| Clear temporary powerups on respawn | | | ✅ | Clears temporary effects for the respawning player. |

## Guns and special combat

Coverage is for registered base-campaign weapon paths. P1 resolves shared combat; remote presentation cannot apply duplicate damage. DLC-specific weapons are outside this pass.

| Feature | Planned | Partially synced | Synced | Comments |
| --- | :---: | :---: | :---: | --- |
| Equipped weapon identity | | | ✅ | Other player sees the equipped weapon. Inventory quantities stay personal. |
| Primary shot events | | | ✅ | Individual shots are preserved by event transport. |
| Secondary shot events | | | ✅ | Registered alternate-fire modes are included. |
| Native muzzle effects | | | ✅ | Reuses game effects. |
| Hitscan shot origin/direction | | | ✅ | P1 resolves both players' combat requests. |
| Bullet trails | | | ✅ | Replicates native trail presentation. |
| Projectile spawn | | | ✅ | Host-owned identities and native factories. |
| Projectile movement | | | ✅ | Follows P1's active projectile state. |
| Projectile impact | | | ✅ | Gameplay results belong to P1. |
| Projectile despawn | | | ✅ | Removes the matching remote projectile. |
| Charged weapon modes | | | ✅ | Included in registered native attack paths. |
| Lightning/beam attacks | | | ✅ | Shared combat and native beam presentation. |
| Melee swings | | | ✅ | P1 resolves shared enemy hits. |
| Kicks | | | ✅ | P1 resolves shared enemy hits. |
| Grappling hooks | | | ✅ | Included in implemented native special-combat paths. |
| Chains | | | ✅ | Replicates native special-combat presentation/results. |
| Charge state | | | ✅ | Included in implemented special-combat state. |
| Blocking | | | ✅ | Shared combat results; personal protection values remain local. |
| Weapon shields | | | ✅ | Shield presentation/results synchronize; personal resources remain local. |
| Projectile reflection | | | ✅ | P1 owns resulting projectile/combat state. |
| Native impact effects | | | ✅ | Remote effects are harmless presentation. |
| Native explosion effects | | | ✅ | P1 owns gameplay results. |
| P2 explosive-barrel hits | | | ✅ | P2 requests hit damage; P1 executes the barrel and linked mission consequences. |
| Special explosion replay | | | ✅ | Explicit IL2CPP nullable rotation avoids a native-wrapper disconnect. |
| Positional firing sounds | | | ✅ | Native audio; full acoustic parity needs playtesting. |
| Positional impact sounds | | | ✅ | Native audio at the replicated impact. |
| Positional explosion sounds | | | ✅ | Native audio at the replicated explosion. |
| Special-weapon audio loop cleanup | | | ✅ | Stops remote loops when their effect ends. |
| Friendly-fire prevention | | | ✅ | Both players' shared combat excludes friendly damage. |

## Enemies, pedestrians and encounters

| Feature | Planned | Partially synced | Synced | Comments |
| --- | :---: | :---: | :---: | --- |
| NPC simulation during map loading | | | ✅ | Native update ticks wait for level readiness while state-entry initialization runs normally; the Hospital Goliath startup regression passes. |
| Enemy AI authority | | | ✅ | P1 simulates NPCs; P2 suppresses competing AI/navigation/gameplay callbacks. |
| Target either living player | | | ✅ | Includes P2's native gameplay proxy. |
| Enemy movement | | | ✅ | Replicates movement and root motion. |
| Enemy scale | | | ✅ | Follows host actor scale. |
| Enemy animations | | | ✅ | Animator layers/parameters follow P1. |
| Human enemies | | | ✅ | Base-campaign NPC families. |
| Pedestrians | | | ✅ | Base-campaign damageable NPCs. |
| Ground creatures | | | ✅ | Base-campaign NPC families. |
| Flying enemies | | | ✅ | Host movement/AI and combat. |
| Boss actors | | | ✅ | Natural full boss fights remain manual acceptance work. |
| Runtime summons | | | ✅ | P1 owns spawned actors. |
| Transforming enemies | | | ✅ | Replicas follow host spawn/transformation state. |
| Scripted shoot-reactive humans | | | ✅ | FakeCitizen/FakePostalDude reactions; native invulnerability stays where appropriate. |
| Balloon targets | | | ✅ | Separate BalloonGroup targets are included. |
| Covid eye targets | | | ✅ | CovidEye and MiniCovidEye parts are included. |
| Elon mech weak spots | | | ✅ | Independent weak-spot hit/state authority. |
| Enemy attacks | | | ✅ | P1 owns attack/hit results. |
| Runtime paw damage authority | | | ✅ | Pooled attacks use P1 authority even without an authored catalogue key. |
| P2 paw duplicate prevention | | | ✅ | Native replica cannot apply local duplicate damage. |
| Runtime damage-trigger authority | | | ✅ | Native damage ticks run on P1; replicas stay passive. |
| Stale remote hazard contacts | | | ✅ | Current capsule/volume overlap replaces stale contact membership before damage. |
| Genuine remote hazard damage | | | ✅ | Native positive control still hurts P2 once. |
| Hazard shared-pause gating | | | ✅ | Native paw and damage-trigger ticks honor session holds. |
| Enemy projectile authority | | | ✅ | Host-owned projectiles; replicas cannot deal duplicate damage. |
| Incoming player-hit events | | | ✅ | P1 sends the hit to its victim; personal health/armor is applied locally. |
| Enemy health | | | ✅ | Both players' attacks resolve on P1. |
| Enemy shields | | | ✅ | Shared protection state belongs to P1. |
| Enemy status effects | | | ✅ | Shared native status state follows P1. |
| Enemy death type/timing | | | ✅ | P1 commits deaths and kills. |
| Blood impact particles |  |  | ✅ | Relays the actual native flesh/armor material effect, including its full emitter hierarchy; shooter effects remain immediate. |
| Immediate P2 native impacts | | | ✅ | Local harmless flesh/armor effects play without waiting for host damage resolution. |
| Native impact echo suppression | | | ✅ | P2 does not replay its own forwarded effect or sound. |
| Blood decals | | | ✅ | Native NPC hit presentation projects the corresponding blood decals. |
| Gib presentation | | | ✅ | Selected native death effects; harmless physics stays local. |
| Ragdoll presentation | | | ✅ | Shared death presentation; harmless settling can differ. |
| Actor particle effects | | | ✅ | Supported native cosmetic particle state. |
| Actor light effects | | | ✅ | Supported native cosmetic light state. |
| Positional actor sounds | | | ✅ | Native sounds follow shared actor events. |
| Boss health HUD | | | ✅ | Native HUD health follows P1. |
| Encounter/wave progress | | | ✅ | Combat-driven progress follows P1. |
| Boss-phase state | | | ✅ | Supported encounter state follows P1; natural phase progression needs playtesting. |
| Combat-driven door/switch changes | | | ✅ | P1 owns progression consequences in implemented paths. |
| Enemy loot selection | | | ✅ | P1 selects the drop. |
| Enemy loot spawn position | | | ✅ | Identical personal copies are created in both games. |

## Doors and gates

Scope covers ordinary doors and identified door/gate movers. Unrelated scenery and machinery appear under future work.

| Feature | Planned | Partially synced | Synced | Comments |
| --- | :---: | :---: | :---: | --- |
| Hinged doors | | | ✅ | P1 owns state; either player can interact. |
| Double doors | | | ✅ | Shared state/motion. |
| Sliding doors | | | ✅ | Shared state/motion. |
| Elevator-panel doors | | | ✅ | Door panels; moving elevator platforms remain separate work. |
| Vault doors | | | ✅ | Shared state/motion. |
| Physics toilet doors | | | ✅ | Shared door state. Toilet filling/flush is tracked below. |
| Identified door/gate movers | | | ✅ | Identified movers use host state. |
| Door opening | | | ✅ | Either player's accepted interaction is committed by P1. |
| Door closing | | | ✅ | Motion follows host state. |
| Door collision | | | ✅ | Collision follows the shared door state. |
| Proximity door activation | | | ✅ | Both players can activate supported proximity doors. |
| Door-control switches | | | ✅ | Switches directly controlling doors are included. |
| Requester's key/item validation | | | ✅ | Checks/consumes only the acting player's own required items. |
| Shared door unlock | | | ✅ | Accepted unlock applies to both; duplicate keys are not required. |
| Script/enemy-driven door changes | | | ✅ | Follow P1's world. |
| Door failure diagnostics | | | ✅ | Missing decorative references are tolerated; errors identify the affected door. |

## Piss / urine interactions

Either player can trigger shared outcomes; P1 commits gameplay results. Personal urine powerup selection and duration remain local.

| Feature | Planned | Partially synced | Synced | Comments |
| --- | :---: | :---: | :---: | --- |
| Native urine stream visuals | | | ✅ | Replicates stream points/color. |
| Positional urine loop audio | | | ✅ | Native audio follows the remote stream. |
| Urine stream/audio cleanup | | | ✅ | Clears effects on stream end, pause and disconnect. |
| Electrical-box urine activation | | | ✅ | P1 or P2 can trigger the native fuse-box action. |
| Electrical-box explosion | | | ✅ | Shared result/presentation; P1 owns gameplay. |
| Hot-sauce urine activating electrical boxes | | | ✅ | Native powered-urine collision routes fuse activation to P1. |
| Fire urine igniting explosive barrels | | | ✅ | P1 applies flame damage and executes native explosion/listener consequences. |
| Fire urine burning supported objects | | | ✅ | P1 owns burn damage and persistent burned state; powerup duration stays personal. |
| First-mission garage opening |  |  | ✅ | Matches the permanent wall cutout and native hide/show portal state before crossing the opening; passage and rejoin checks pass. |
| First-mission garage collision | | | ✅ | Actual solid wall collision disappears; both native player controllers traverse the same opening, including after rejoin. |
| Explosion-linked occlusion portals | | | ✅ | Native hide/show portal bits accompany shared surfaces; older surface layouts retain native baseline portal state. |
| First-mission urine tutorial trigger | | | ✅ | Completion/disable state follows P1. |
| First-mission urine hint cleanup |  |  | ✅ | P2 completing the fuse retires P1's exact trigger-owned hint and preserves unrelated local notifications. |
| Urine-activated shootable buttons | | | ✅ | Native target interactions route through P1. |
| Urine-activated metal buttons | | | ✅ | Uses the requester's native interaction/key handshake. |
| Fractional filler progress | | | ✅ | Preserves native incremental filling. |
| Tank fill progress | | | ✅ | Current fill fraction follows P1. |
| Native tank endpoint overshoot | | | ✅ | Normalizes finite native drain/fill overshoot for replication/restoration; malformed nonfinite or unbounded values remain rejected. |
| Tank/filler completion | | | ✅ | P1 commits completion and authored consequences. |
| Fill-driven moving objects |  |  | ✅ | P1 owns linked poses; P2 fill callbacks cannot overwrite them. Strict Hospital live/current-world rejoin comparison passes. |
| Fill-driven scaling objects | | | ✅ | Replicates linked object scale. |
| Replica body presentation | | | ✅ | Replicated bodies use host poses without stale local physics interpolation; original interpolation returns when ownership ends. |
| Urine-linked material state | | | ✅ | Replicates supported native material/tiling state. |
| Urine-linked light state | | | ✅ | Replicates supported native light state. |
| Urine-linked laser switches | | | ✅ | Supported authored state follows P1. Full natural progression needs playtesting. |
| Urine-linked rocket sequence state | | | ✅ | State replication exists; natural sequence completion remains manual. |
| Urine-linked rail path state | | | ✅ | Host-selected paths follow P1. Natural rail rides remain manual acceptance. |
| Urine-linked wagon state/motion | | | ✅ | Ordered wagon identities/poses and rider requests; natural rides need playtesting. |
| Toilet water level | | | ✅ | Replicates current water state without replaying completion actions. |
| Toilet overflow | | | ✅ | Shared native overflow state. |
| Toilet flush state | | | ✅ | Native water decline follows P1; shared pause holds host flush motion. |
| Toilet flush effects/audio | | | ✅ | Native live presentation; historical sounds do not replay on join. |
| NPC urine reactions | | | ✅ | Native reaction results use host actor authority. |
| Urine fire effects on NPCs | | | ✅ | Acting player's effect reaches shared host combat. |
| Urine ice effects on NPCs | | | ✅ | Acting player's effect reaches shared host combat. |
| Urine confusion effects on NPCs | | | ✅ | Acting player's effect reaches shared host combat. |
| Urine damage to exposed weak spots | | | ✅ | Preserves native frame-timed hit calculation; P2 has a native regression check. |
| Fireball extinguishing | | | ✅ | Either player can request host extinguish/despawn with native effects/audio. |

## Joining an active game

| Feature | Planned | Partially synced | Synced | Comments |
| --- | :---: | :---: | :---: | --- |
| Automatically load P1's map | | | ✅ | P2 follows the active built-in campaign map. |
| Spawn beside P1 | | | ✅ | Grounded, unblocked position; normal spawn fallback with explanation. |
| Initial weapon copy | | | ✅ | One-time copy of P1's owned weapons. |
| Initial ammo copy | | | ✅ | One-time copy of P1's remaining ammunition. |
| Preserve P2's normal non-ammo items | | | ✅ | Join loadout does not copy health, armor, keys or powerups. |
| Restore completed level results | | | ✅ | A completed host map restores its results when the guest is ready. |
| Restore current actors | | | ✅ | Includes spawned/dead state and actor parts. |
| Restore enemy health/status | | | ✅ | Current host combat state is received before release. |
| Restore doors/locks | | | ✅ | Current door state, rather than replaying old interactions. |
| Restore encounter state | | | ✅ | Supported current encounter/boss state. |
| Restore active projectiles | | | ✅ | Active identities/state are included. |
| Restore existing enemy loot | | | ✅ | Late joiner receives personal copies of existing shared drops. |
| Restore urine puzzle state | | | ✅ | Current box/button/tank/toilet state. |
| Restore urine-linked machinery | | | ✅ | Current supported linked object/material/light/laser/rocket/rail state. |
| Suppress historical transient effects | | | ✅ | Old shots, explosions, death sounds and flush sounds do not replay. |
| Restore settled historical corpses | ✅ | | | Historical corpse physics is not reconstructed. Live corpse settling is intentionally local. |
| Restore complete progressed world | | ✅ | | Supported snapshots exist; unsynchronized machinery/scenery prevents complete restoration. |

## Mission triggers and linked world state

| Feature | Planned | Partially synced | Synced | Comments |
| --- | :---: | :---: | :---: | --- |
| Mission trigger enter | | | ✅ | Either living player activates supported authored triggers; P1 commits the result. |
| Mission trigger exit | | | ✅ | Aggregate occupancy prevents one player leaving from releasing a trigger while the other remains inside. |
| One-shot mission triggers | | | ✅ | Host tracks consumed state; repeat contact does not rerun a one-shot action. |
| Repeatable mission triggers | | | ✅ | Host tracks entry/exit transitions and native multi-use rules. |
| Delayed mission callbacks | | | ✅ | Host runs Invoker sequences; client restores current state without rerunning the callback. |
| Multi-condition switches | | | ✅ | Host evaluates native state/listener conditions; scalars and bounded Boolean condition pages restore on join. |
| Mission object activation | | | ✅ | Referenced object visibility/activation follows P1. Personal UI, actors and pickups are excluded. |
| Mission collider state | | | ✅ | Referenced collision/nav-obstacle enable state follows P1. |
| Required wall destruction | | | ✅ | P1 commits destruction; P2 does not replay gameplay explosions. |
| Broken-wall replacement meshes | | | ✅ | Keeps the authored permanent geometry around the hole; loose debris stays cosmetic. |
| Broken-wall opening collision | | | ✅ | Native controller passage and remaining static surfaces match P1. |
| Prison barrel-to-pipe opening | | | ✅ | P2 fire damage activates the authored barrel and opens the actual Pipe_hole on both peers. |
| Mission moving-object pose | | | ✅ | Referenced mover transforms follow P1. |
| Mission rotating-object pose | | | ✅ | Referenced rotater transforms follow P1. |
| Mission scaling-object pose | | | ✅ | Referenced transform scale follows P1. |
| Mission control-panel state | | | ✅ | Host execution and pressed/activation state are replicated. |
| Mission light color | | | ✅ | Current referenced light colors are transmitted as bounded values. |
| Mission light intensity | | | ✅ | Current referenced light intensities follow P1. |
| Mission light enable/activation | | | ✅ | Referenced enable/activation state passes three repeated strict Prison comparisons against a settled shared pause. Deliberately flickering lights are compared at the same held state. |
| Mission emission color | | | ✅ | Native emission state is restored through the existing presentation adapter. |
| Native mission positional sounds | | | ✅ | Registered native one-shot sounds are forwarded; historical sounds are not replayed. |
| Laser activation and linked pose | | | ✅ | Host executes damage; registered state/presentation follows P1. |
| Falling-platform destruction | | ✅ | | Host requests and persistent geometry adapter exist; all natural contact/delay sequences need acceptance. |
| Scripted timeline consequences | | ✅ | | Referenced state/poses are restored; arbitrary timeline tracks/signals are not blanket-replayed. |
| Walk-in exit waiting | | | ✅ | Both living players must occupy the authored exit area. Prison native regression passes. |
| Exit readiness cancellation | | | ✅ | Leaving the exit area withdraws readiness. |
| Interactable ending waiting | | | ✅ | Preserve the native interact action; both must be nearby before validating/consuming items. Wallmarkt bed regression passes. |
| P2 interaction at an ending | | | ✅ | Requester-owned handshake reaches the host once both are nearby. |
| Automatic-ending readiness | | | ✅ | Store the completed ending while a player is dead/loading; finish once both are alive and loaded. |
| Exit after partner disconnect | | | ✅ | Remaining P1 can finish alone; no host migration. |
| Restore mission destruction on join | | | ✅ | Current solid geometry restores without replaying old blasts or rewards. |
| Restore mission conditions on join | | | ✅ | Host scalar/enum state and bounded Boolean arrays are included before gameplay release. |
| Restore referenced machinery on join | | | ✅ | Current activation/collision/poses/light state is included. Unclassified world objects remain outside coverage. |
| Large mission snapshot delivery | | | ✅ | Ordered retry/backpressure keeps transport queues bounded without dropping state. |

## Broader world coverage and persistence

| Feature | Planned | Partially synced | Synced | Comments |
| --- | :---: | :---: | :---: | --- |
| General scripted world triggers | | ✅ | | Expanded host-owned mission graph, state adapters and exit rules exist. Unclassified callbacks and complete natural campaign acceptance remain; see [mission pass details](mission-trigger-sync-plan.md). |
| Unrelated puzzle machinery | | ✅ | | Expanded authored references and dedicated machinery adapters exist; remaining specialized and natural-route checks are tracked in the megapass. |
| Moving elevator platforms | | ✅ | | Referenced platform poses and elevator state are replicated; every natural ride/attachment scenario still needs acceptance. |
| Moving-platform rider positioning | | ✅ | | Complete Area69 live/pause/restored journey passes after stale hazard-contact correction; other natural rides and obstructions remain manual. |
| Elevator underside-hit prevention | | ✅ | | Current-transform contact revalidation rejects stale underside hits; genuine overlap remains dangerous. Full restored ride passes; natural obstruction cases remain manual. |
| Conveyor activation | | | ✅ | Both native Area69 controllers execute on P1; P2 does not independently advance them. |
| Conveyor listener motion | | | ✅ | Native arrays of 12 and 20 listeners follow host poses and resume after restoration. |
| Conveyor collision geometry | | | ✅ | Solid collider bounds, enabled states and native ray contacts agree on both peers. |
| Conveyor save restoration | | | ✅ | Every referenced listener survives disk restore and continues native movement. |
| Conveyor current-world rejoin | | | ✅ | P2 receives current listener poses and collision after further movement. |
| Pusher victim attribution | | | ✅ | Hospital's native pusher reaches P2's movement component without pushing P1. |
| Pusher duplicate prevention | | | ✅ | P2's passive replica produces no independent push; one host force is received. |
| TNT/destructible gates | | ✅ | | Native WireWallGate requester planting, remaining fuse and shared destruction pass the focused The Wall fixture. Other gate routes and negative cases remain in the megapass. |
| Cat-paw machinery | | ✅ | | Runtime damage authority and pause/victim checks pass; referenced machinery exists. Natural trap/puzzle flow remains manual. |
| Shootable explosive barrels | | | ✅ | P1 owns shooting/fire damage, native explosion and linked mission results; decorative breakables remain separate. |
| Breakable props | | ✅ | | Host-owned destruction and current structural state exist; Neighborhood/Backyard native fixtures pass. Broader natural-route acceptance remains in progress. |
| Decorative glass damage | | | ✅ | P2 native hits reach P1; current broken state restores without replaying old smash events. |
| Partial explosive-barrel health | | | ✅ | Remaining health restores without refilling an unexploded barrel. |
| Pending barrel explosion | | | ✅ | A partly elapsed native chain saves its remaining fuse; disk restoration and shared pause retain it, then one native future explosion reaches P2 without disconnecting. |
| Live updates during save capture | | | ✅ | Durable changes first observed by capture also reach P2 through the bounded live queue; the native restored-barrel regression verifies the result without another mutation or rejoin. |
| Physical prop restore pose ownership | | | ✅ | An active moving prop restores its physics pose despite a conflicting generic display transform; native pooling and peer rejoin also pass. |
| Physical bodies after parent machinery | | ✅ | | Restore and publication now order parent state before physical bodies; the combined shifted-parent/body fixture remains to be expanded. |
| Partial burnable health | | | ✅ | Remaining burn damage survives disk restore and current-world rejoin. |
| Native live smash sounds | | | ✅ | New destruction uses native positional sources; old destruction does not replay on join/load. |
| Native live smash particles | | | ✅ | Native presentation is isolated from gameplay callbacks. |
| Gameplay fragment physics | | ✅ | | Shared pose, collision, velocity and sleep adapters exist; natural fragment classification and interaction checks remain. |
| Authored movable props | | | ✅ | P2 push requests move the host body; native pose/state restoration passes. |
| Runtime prop spawn batches | | | ✅ | Consistent capture waits for the complete native batch, including its final push. |
| Runtime prop motion restoration | | | ✅ | Moving native props retain position, velocity, spin and sleep state. |
| Runtime prop pool returns | | | ✅ | Tombstones remove P2 replicas; native source ownership is cleared after pooling. |
| Runtime prop pool reuse | | | ✅ | Reused pool instances receive new lifetime IDs; current-world rejoin is covered. |
| Falling-platform countdown | | | ✅ | Explicit remaining delay survives pause and disk restoration; natural rider coverage remains separate. |
| Mover continuation after restore | | | ✅ | Native remaining motion resumes and its future completion runs once in machinery fixtures. |
| Rotater continuation after restore | | | ✅ | Remaining duration and future callback restore without a duplicate door path. |
| Elevator continuation after restore | | | ✅ | Moving native platforms resume; dormant optional helpers do not cause restore errors. |
| Multi-step rotation restoration | | | ✅ | Native phase and future endpoint pass the train machinery fixture. |
| Scale continuation after restore | | | ✅ | Native ComicCon fixture verifies phase, rollback and the future endpoint. Natural rider scenarios remain separate. |
| TNT-gate remaining fuse | | | ✅ | The Wall fixture preserves a partially elapsed fuse through pause/disk restore; the future destruction callback runs once. |
| TNT planting count | | | ✅ | Native P2 interaction plants exactly the required charges, including duplicate request delivery. |
| TNT requester inventory cost | | | ✅ | P2 spends only planted charges; P1 items remain unchanged. Rejection/timeout coverage remains separate. |
| TNT-gate destruction replication | | | ✅ | Both peers reach the broken gate state after the restored fuse completes. |
| Native barrel overkill replication | | | ✅ | Below-zero native health from excessive flame damage is preserved without rejecting P2's session; the Prison regression verifies the actual overkill path. |
| Machinery loop playback | | ✅ | | Native loop state, seek position and owned pause handling are implemented; remaining audio checks are pending. |
| Train timeline playback | | | ✅ | Native sleeping-car bindings, peak/completed phase, future signals and current-world rejoin pass the focused fixture. Wider timeline/audio coverage remains separate. |
| Rocket animation phase | | | ✅ | VIP native fixture verifies phase/state during launch, shared pause and both-peer disk restoration. |
| Rocket launch continuation | | | ✅ | VIP fixture captures the native quarter-second launch, restores its remaining motion and verifies one future head-hit callback. |
| COVID arena segment order | | | ✅ | Native shift order and base position survive disk restore on both peers. |
| COVID syringe activation | | | ✅ | P2's native shootable button reaches the host-owned controller. |
| COVID syringe animation | | | ✅ | Native phase and injection eligibility restore on both peers without replaying old hits. |
| COVID syringe cooldown | | | ✅ | Remaining native cooldown survives shared pause/load and resumes to completion. |
| COVID syringe injection particles | | | ✅ | Audited native injection particles are isolated from gameplay; the arena presentation/current-world rejoin fixture passes. |
| Shared gravity-volume state | | ✅ | | Typed host state exists and focused contacts pass. Wider changing-volume/puzzle variants remain part of machinery acceptance. |
| Personal gravity contacts | | | ✅ | Native Area69 entry/exit applies each player's own gravity and terminal fall speed independently. |
| Gravity contact restoration | | | ✅ | A valid grounded save inside the AirTube restores P2's actual native gravity contact. |
| Gravity contacts after rejoin | | | ✅ | Joining beside P1 clears old contacts; entering the volume again works. |
| Cannon targets either player | | | ✅ | Native P1 aim and nearest-P2 targeting pass without relocating P1. |
| Cannon projectile visibility | | | ✅ | Ownerless native scenery shots receive harmless visual replicas; the six-emitter cannon source passed its serialized particle audit. |
| Cannon damage victim | | | ✅ | The native shell hits P2 through P1 combat authority; P1 health is unchanged and P2 cannot simulate a duplicate cannon attack. |
| Scripted teleport attribution | | | ✅ | DeathStar's native P2 button invokes one requester teleport with correct destination/view and nonzero transformed pending momentum; P1 stays put. |
| Shared secret discovery | | | ✅ | Either player can discover; P2 native entry and duplicate callbacks are exercised. |
| Shared secret result count | | | ✅ | One discovered identity increments the native shared result once on both peers. |
| Secret checkpoint/save rollback | | | ✅ | Undiscovered rollback permits rediscovery; discovered disk state restores its identity and count. |
| Secret late-join restoration | | | ✅ | Current discovered identities/counts survive rejoin without double-credit. |
| Shared poster collection |  |  | ✅ | Either player collects the authored poster once; P1 commits its shared state. Distance-hidden P1 copies can still be collected by a nearby P2. |
| Poster level-result counts | | | ✅ | Shared collected count increments once, including duplicate interactions. |
| Poster late-join restoration | | | ✅ | Current collection/count and readable entries restore without historical collection sounds. |
| Poster save/checkpoint restoration | | | ✅ | Durable authored GUID records restore collection and counts. Earlier schema-2 personal poster histories become a shared union. |
| Shared checkpoint capture | | | ✅ | Timed autosaves provide the host-owned recovery snapshots. Native checkpoint/interest-area requests cannot force early disk captures. |
| Shared checkpoint restoration | | | ✅ | P1 rolls both players and supported native/shared world state back together. Deaths still respawn independently. |
| Persistent co-op saves | | | ✅ | Separate host-owned storage; native serializers plus durable multiplayer records. |
| Restore P1 inventory | | | ✅ | Native saved weapons, ammo and non-ammo items. |
| P2 weapon/ammo catch-up | | | ✅ | Exact P1 owned weapons, remaining ammo and equipped weapon on every join/rejoin and restore. |
| Restore P2 non-ammo inventory | | | ✅ | Personal saved keys/items for the current map. |
| Explicit solo-save import | ✅ | | | Future import flow; never overwrite solo saves implicitly. |

## Save and recovery details

| Feature | Planned | Partially synced | Synced | Comments |
| --- | :---: | :---: | :---: | --- |
| Native-themed save browser | | | ✅ | Named slots, pagination, confirmations and host-only controls. |
| Named manual slots | | | ✅ | Create new saves and explicitly overwrite selected manual slots. |
| Dedicated quicksave | | | ✅ | One rotating quicksave, separate from solo quicksave. |
| Co-op quickload | | | ✅ | P1 loads the co-op quicksave; P2 cannot load independently. |
| Three rotating autosaves | | | ✅ | Timer-based captures rotate through three slots with recovery backups. |
| Gameplay autosave timer | | | ✅ | Five active gameplay minutes by default; pauses, loading and transactions do not count. No checkpoint-volume write storm. |
| Autosave interval setting | | | ✅ | Host config accepts 5–10 minutes; new levels/restores and completed saves reset the interval. |
| Autosaving overlay | | | ✅ | Both players see a native-themed capture notice; it clears on release. |
| Manual saving overlay | | | ✅ | Distinguishes explicit save capture from autosaving. |
| Map/world loading overlay | | | ✅ | Explains the shared loading/restoration hold. |
| Restored gameplay input | | | ✅ | Completes the native player-load lifecycle; progressed-save replay verifies real movement and mouse-look. |
| Partner-assignment menu cleanup | | | ✅ | Closes a panel opened only for assignment, preserving a pre-existing local panel. |
| Pending unsafe saves | | | ✅ | Waits for loading, death/respawn and active story sequences. |
| Shared capture pause | | | ✅ | Holds both games for consistent host/player records. |
| Budgeted native capture | | | ✅ | Yields between serializers under a frame budget. |
| Background disk writes | | | ✅ | Only managed snapshots reach the worker. |
| Atomic replacement | | | ✅ | Flushes temporary files before replacing the slot. |
| Recovery backup | | | ✅ | Preserves the previous valid generation and tries it on primary corruption. |
| Checksum validation | | | ✅ | SHA-256 envelope rejects damaged payloads. |
| Save format compatibility | | | ✅ | Schema, game build, authored GUIDs and catalogue checks. |
| Authored prop catalogue stability | | | ✅ | Scene-load membership preserves map props and excludes later detached corpse chunks across restore/rehost. |
| Compatible catalogue backup recovery | | | ✅ | Only a backup matching schema, game, map and catalogue is accepted; reports the older recovery point and preserves the primary. |
| Preserve usable recovery generations | | | ✅ | A known incompatible same-map primary cannot displace the compatible backup during the next save. |
| Host campaign statistics | | | ✅ | Native level/world statistics plus isolated campaign metadata. |
| Authored pickup histories | | | ✅ | Resource copies restore separately; posters use shared records. |
| Generated loot persistence | | | ✅ | Stable saved drop records and separate collection histories. |
| Returning Steam partner | | | ✅ | Validates the saved slot against the connected Steam account. |
| Returning Direct IP partner | | | ✅ | Persistent local player ID; not based on name or address. |
| Replacement partner confirmation | | | ✅ | P1 must assign the saved partner slot before release. |
| Resume without P2 | | | ✅ | P1 can continue while retaining the absent partner record. |
| P2 health reset on join/restore | | | ✅ | Full health; no resource refill during ordinary play. |
| P2 armor reset on join/restore | | | ✅ | Zero armor. |
| P2 powerup reset on join/restore | | | ✅ | Clears temporary effects. |
| P1 saved health/armor/powers | | | ✅ | Native personal values restore. |
| Safe saved position | | | ✅ | Validates placement; nearby/map-spawn fallback with notice. |
| World restoration barrier | | | ✅ | Native guest baseline, authoritative world snapshot and personal restore acknowledge before release. |
| Clear transient attacks on load | | | ✅ | Old projectiles/effects do not replay; completed results remain. |

See [co-op saves](co-op-saves.md) for controls and exact partner/resource rules.

## Intentionally local systems

These are agreed design choices and do not reduce the progress score. Picking something up never removes the other player's copy.

| System | Intentionally local | Comments |
| --- | :---: | --- |
| Player health values | ✅ | Victim applies P1's incoming-hit events locally. Life/death visibility is shared separately. |
| Player armor/shield values | ✅ | Personal protection amounts; weapon shield presentation/combat results can synchronize. |
| Health pickup collection | ✅ | Each player collects their own copy. |
| Armor pickup collection | ✅ | Each player collects their own copy. |
| Ammo pickup collection | ✅ | Each player collects their own copy. |
| Weapon pickup collection | ✅ | Each player collects their own copy. |
| Key pickup collection | ✅ | Each player collects their own copy; an accepted door unlock is shared. |
| Powerup pickup collection | ✅ | Each player collects their own copy. |
| Other item pickup collection | ✅ | Each player collects their own copy. |
| Owned weapon inventory | ✅ | Independent after catch-up on every join/rejoin and co-op restore. |
| Remaining ammo inventory | ✅ | Independent after catch-up on every join/rejoin and co-op restore. |
| Key inventory | ✅ | Only the interacting player's own keys are checked/consumed. |
| Required-item inventory | ✅ | Only the interacting player's own required items are checked/consumed. |
| Other item counts | ✅ | Personal quantities remain independent. |
| Active personal powerups | ✅ | Personal effects/durations remain independent. |
| Urine powerup type | ✅ | Personal fire/ice/confusion selection; shared targets receive the acting player's results. |
| Urine powerup duration | ✅ | Personal effect timer, not a shared resource. |
| Harmless corpse-piece physics | ✅ | Shared death type/timing; cosmetic pieces can settle differently. |

## Progress score

**Development coverage: 93%. Last accepted milestone baseline: 90%.** Both use the same original 21 equally weighted milestones, not the number of detail rows. Splitting features never adds points. Intentionally local systems and out-of-scope modes are excluded.

| Milestone group | Count | Development credit each | Accepted baseline credit each |
| --- | :---: | :---: | :---: |
| Accepted milestones | 17 | 1 | 1 |
| Remote model and animation polish | 2 | 0.5 | 0.5 |
| General world triggers and complete active-session restoration | 2 | 0.75 | 0.5 |

Development credit: **0 = planned**, **0.5 = partially implemented**, **0.75 = mostly implemented with focused native coverage, but known gaps or broader acceptance outstanding**, **1 = accepted milestone**. The two world milestones receive 0.75 for the implemented machinery, destruction, current-world and persistent-state adapters plus focused checks. Their remaining gaps stay partial.

Development: **17 + (2 × 0.5) + (2 × 0.75) = 19.5 / 21 = 92.86%, rounded to 93%.** Accepted baseline: **17 + (4 × 0.5) = 19 / 21 = 90.48%, rounded to 90%.** This explicitly revises the development partial-credit rubric; it does not declare the remaining paths verified or the current build regression-free. Neither score measures FPS, network quality or release readiness.

The four incompletely accepted milestones remain remote player model, remote player animations, general scripted world triggers and complete active-session restoration. Checkpoint/world restoration and persistent co-op saves are implemented; broader family/recovery checks remain listed below.

## Save verification

Two native game processes pass manual save/load, three-slot autosave rotation, checkpoint rollback, completed-level results/statistics, per-player resource pickup histories, generated loot and structural wall restoration. Fresh-process resume works with P1 alone, then partner join; a different partner remains held until P1 confirms assignment. Managed tests cover atomic replacement, damaged-primary recovery, unusable backups, schema/slot/event rejection, bounded paging and sender authority. See [verification](verification.md) for commands and evidence limits.

## Combat and actor coverage

The visual-only gun milestone was superseded by shared enemy/combat authority. P1 resolves bullets, projectiles, beams, melee/kicks, hooks/chains, charge/block/shield and reflection results in the implemented native paths. P2 submits attacks rather than independently changing shared enemy health. Personal resources stay local and friendly fire stays off.

The actor pass includes ordinary NPCs, pedestrians, flyers, bosses, runtime-spawned/transformed enemies, `FakeCitizen`/`FakePostalDude` scripted reactions, `BalloonGroup`, `CovidEye`/`MiniCovidEye` and `ElonMechWeakSpotComponent`. Scripted humans retain native invulnerability where appropriate. Replicas suppress local AI, navigation, animation gameplay callbacks and competing rigidbody motion. Native boss HUD health follows P1. Cosmetic particle/light state and native positional actor sounds are also relayed.

## Mission pass verification (historical 0.9 baseline)

- These historical results used matching 0.9.0 / protocol 8 DLLs. The current playtest pair and its checks are recorded at the top of this page.
- Neighborhood's actual `OBJECTIVES/BreakableWall` failed before the adapter and passes after it: renderer/collider removal, matching native controller passage, either-player activation and rejoin without historical effects. Repeat with `Test-Travel.ps1 -GameDir $GameDir -Mission -Level LEVEL_AD_Neighborhood`.
- Prison walk-in exits and Wallmarkt's interactable bed ending enforce partner gathering, readiness cancellation and one committed finish. Automatic endings wait for independent respawn. Use `-MissionExits -Level LEVEL_AD_Prison` or `LEVEL_AD_Wallmarkt`.
- Native `-WorldRegression` checks P2 hit particles, powered urine, host-owned barrel damage, actual Prison pipe geometry and the formerly crashing explosion replay. Use `-Level LEVEL_AD_Prison` or `LEVEL_AD_Neighborhood`.
- Prison's extended state fixture verifies current door/mover/mission scalar state, authoritative P2 encounter activation and group progression. Use `-State -Level LEVEL_AD_Prison`.
- Twenty-two network scenarios include persistent-save corruption recovery, bounded save paging and authority checks, plus 12,288 ordered mission-state frames through bounded queues with a delayed consumer. Native snapshots retain frames for retry and use TCP receive backpressure.
- The controlled 60 FPS check measures about 3.7 ms p95 world work, 150 ms p95 sweeps and 16.7 ms p95 frame time. An earlier input run encountered a shared host pause while awaiting samples; the check passed on rerun. This does not establish every intermittent focus/pause issue's cause.
- All 15 maps were inspected with the expanded catalogue. A long sequential run hit a native NPC initialization error entering VIP; the remaining maps passed in a fresh process. Discovery/bounded serialization is not natural campaign completion.
- Shared-combat regressions and representative Prison/Forest/ComicCon machinery catalogue/state checks passed. Use the [verification commands](verification.md#running-checks) to repeat the relevant fixtures; private run folders are not distributed.

## Evidence and practical limits

- Current protocol 10 passes **33 managed network acceptance scenarios**, including bounded carrier references and large streamed world baselines over TCP/message carriers. Current focused native evidence and the remaining acceptance matrix are recorded in the [megapass checkpoint](../megapass.md); historical timings below do not establish performance for the expanded playtest.

- Urine regressions use two native processes in Neighborhood, Prison, Hospital and ComicCon: either-player electrical-box/tank activation, catalogued garage surface/collision and tutorial-trigger state (including native portal bits and exact host hint retirement), fractional fill and linked motion/scale, toilet overflow/flush, fireball extinguishing and disconnect/rejoin restoration. The subsequent house-wall regression is fixed by a dedicated destruction adapter; native tests now check the actual renderer, collider and matching player-controller passage before and after joining again. An exposed native boss weak spot also accepts P2's frame-timed urine damage through the existing actor-hit authority path.
- All 15 campaign maps pass discovery and bounded serialization with the new urine/progression catalogue. This does not establish natural completion of every urine puzzle, rail ride, rocket sequence or boss fight. An earlier urine-only VIP fixture stopped in a native shared pause before its action. The later focused machinery fixture passes native launch/rollback/continuation; natural progression remains manual acceptance work.
- The final urine build passes shared-combat and bedroom-door reload regressions. Its controlled 60 FPS host check measures 3.5 ms p95 world work and 100 ms p95 completed sweeps; this preserves the frame-budgeted sampling behavior while adding urine state.
- Historical protocol 8 passed 22 network scenarios. The earlier loading-failure-reason race was fixed by allowing the receiver to retain the peer's stated reason before transport cleanup; see [verification](verification.md).
- Network acceptance checks cover bounded actor/combat packets, event preservation, authority restrictions, travel, reconnect and build mismatch.
- Two actual local IL2CPP processes pass native combat, door/key/reload, encounter, late-join input, travel/respawn and UI checks. P2 targeting/hit probes exercise a real native enemy attack.
- The input regression reproduces and fixes a stranded `NONE` lock when P2 closes its menu during P1's pause. Five real window focus round trips also verify native movement and mouse-look input after returning to P2; this does not establish that every intermittent Alt-Tab failure had that same cause.
- Host periodic state sampling now yields between actors, encounters, doors and movers under a roughly 3 ms shared capture budget. NPC spawn/enable tracking avoids repeated scene-wide NPC discovery; native beam/line/urine renderers are seeded once and tracked as new instances appear, stable animator/encounter metadata is cached, and menu/save discovery scans run periodically while cached guards remain enforced each frame. Combat events retain their immediate handling and join snapshots still complete before gameplay release.
- Controlled two-instance Neighborhood timings confirmed the previous periodic host hitch: frame p95 after a full sync batch was 105 ms versus 7.8 ms between batches. Host world-work p95 fell from 19.9 ms to 3.2 ms after the fixes. A separate 60 FPS check measured 83 ms p95 for completed world sweeps and the expected 16.7 ms displayed-frame p95. Median capture allocations fell from about 2.5 MB to 1.5 MB per cycle. These are stationary native diagnostic results under two-instance load, not a guarantee about every combat scene, hardware configuration or LAN setup.
- Actor fixtures exercise **49 base-campaign NPC families** for spawning, targeting, movement, health/status/shield and death presentation. Separate fixtures exercise weak spots, balloons, scripted human reactions and the native boss HUD.
- All **15 base-campaign maps** pass authored actor catalogue/snapshot audits: **43 distinct authored NPC families across 243 map/family rows**. These audits establish discovery and bounded serialization, not a complete combat playthrough of every map.
- Two-PC play is verified by the maintainer. Regression testing should continue to exercise natural mission/boss sequences, LAN latency and special attacks alongside native fixtures.

## Remaining manual acceptance

The implementation closeout is handed over with these **partially verified** campaign cases. Use the [playtest checklist](megapass-playtest.md); they do not require another exhaustive automated pass before other development continues.

| Feature | Planned | Partially synced | Synced | Comments |
| --- | :---: | :---: | :---: | --- |
| Forest bridge progression | | ✅ | | Native fall/geometry/pause/rollback/completion pass; natural crossing and current-world join remain maintainer playtests. |
| Wheel/cart progression | | ✅ | | Actual driver and linked doors pass pause/rollback/completion; natural button/rider/save/rejoin flow remains manual. |
| Cat-paw/shockwave damage | | ✅ | | Runtime authority, duplicate prevention, victim isolation and pause pass; natural boss/trap variants and cadence remain manual. |
| Laser/crush/rider safety | | ✅ | | Stale-contact extra damage is fixed and a full restored ride passes; natural laser/ceiling/obstruction behavior remains manual. |
| Rejected/stale interactions | | ✅ | | Finish eligibility, simultaneous/stale requests, timeout and item-refund cases. |
| Gameplay-fragment behavior | | ✅ | | Finish native fragment classification, collision and interaction checks. |
| Cross-family persistent recovery | | ✅ | | Live and fresh-process save checks pass; extended family/progression/rejoin matrix remains manual. |
| Native interrupted recovery | | ✅ | | Missing targets, interrupted transfer/restore and peer loss must preserve files and release or fail clearly. |
| Active-world performance | | ✅ | | Controlled idle hardening passes; busy combat/world and real transport load belong to the next optimization phase. |
| Audio/story/UI regression | | ✅ | | Targeted pause/story/input checks pass; natural loop, seek and sequence combinations remain manual. |
| Natural campaign routes | | ✅ | | Complete meaningful traversal with both players, save points and rejoining. |

## Remaining work

Co-op persistence now combines native world saves with durable multiplayer records. Solo-save import remains planned. Supported mission triggers and referenced machinery now have shared state, while unclassified callbacks, arbitrary timeline tracks and general decorative scenery/destructibles remain outside complete coverage. Natural campaign/ride/trap acceptance and player-presentation polish remain before treating this development build as a validated release.
