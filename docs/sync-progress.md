# Multiplayer synchronization progress

Updated October 13, 2026. Current development baseline: **PBDMP 0.7.0 / protocol 7**. This describes implemented behavior and its limits. Both players need the same current plugin and network DLLs. See [verification](verification.md) for recorded checks, reproduction commands and remaining acceptance work.

**Current milestone coverage: 81% (17 / 21 points).** This is an equally weighted planning checklist for base-campaign co-op, not a stability rating, campaign completion claim, or percentage of two-PC testing completed.

## Reading the tables

Each shared feature has one checkmark in its current status column. **Synced** means the listed behavior is coordinated between players; it does not mean every campaign situation has passed manual testing. **Partially synced** identifies an implemented feature with remaining gaps. **Planned** identifies work without a synchronization implementation yet. Comments describe coverage and limitations.

Personal systems appear in a separate **Intentionally local** table. Those are design choices, not unfinished sync.

## Session and campaign

| Feature | Planned | Partially synced | Synced | Comments |
| --- | :---: | :---: | :---: | --- |
| Host by IP/port | | | ✅ | P1 creates the two-player session. |
| Join by IP/port | | | ✅ | P2 connects directly to the host by IP and port. |
| Game/mod compatibility checks | | | ✅ | Both players must use matching game and exact mod builds. |
| Reconnect | | | ✅ | A returning client receives current supported state. |
| Peer timeout | | | ✅ | Lost connections end waiting instead of leaving the session stuck. |
| P2 disconnect | | | ✅ | P1 continues playing. |
| P1 departure | | | ✅ | Ends the session and returns P2 to the main menu. |
| Main-menu multiplayer controls | | | ✅ | Native-themed Host/Join/status/Disconnect dialog. |
| Pause-menu multiplayer controls | | | ✅ | Native-themed entry and F8 shortcut. |
| Start campaign level | | | ✅ | P1 starts; P2 follows. Built-in campaign only. |
| Restart current level | | | ✅ | P1 restarts both players' level. |
| Next-level travel | | | ✅ | P2 follows P1's campaign transition. |
| Return to main menu | | | ✅ | P1 controls shared campaign travel. |
| Difficulty selection | | | ✅ | P1 selects the session difficulty. |
| Block independent P2 travel | | | ✅ | P2 cannot start, load or restart a level independently. |
| Shared host pause | | | ✅ | P1 freezes/resumes gameplay for both players. |
| P2 menu during gameplay | | | ✅ | P2 can open a menu without pausing P1. |
| Wait for both players to load | | | ✅ | Gameplay waits for readiness and the current shared snapshot. |
| Failed client load recovery | | | ✅ | P2 returns to the menu; P1 can continue. A transport timing check remains under observation. |
| Menu/pause input release | | | ✅ | Native checks cover overlapping pause/menu locks and focus round trips. |
| Block multiplayer campaign saving | | | ✅ | Save/load, quicksave and autosave are disabled during multiplayer. |

## Player presentation and respawn

| Feature | Planned | Partially synced | Synced | Comments |
| --- | :---: | :---: | :---: | --- |
| Player position | | | ✅ | Remote location and height follow the player. |
| Player aim | | | ✅ | Yaw/pitch and weapon aim follow the player. |
| Living/dead visibility | | | ✅ | Remote avatar visibility follows life state. |
| Respawn life generation | | | ✅ | Separates the new life from stale events. |
| P2 gameplay target on P1 | | | ✅ | Native targeting/hit proxy is separate from the visible avatar. |
| Native Dude body model | | | ✅ | Uses the game's coat/gown rig. |
| Weapon geometry and grip alignment | | ✅ | | Equipped geometry exists; some geometry and hand alignment remain preliminary. |
| Idle animation | | | ✅ | Uses native clips. |
| Armed holding animation | | | ✅ | Uses native clips; grip polish is tracked separately. |
| Movement animation | | | ✅ | Uses native clips. |
| Shooting animation | | | ✅ | Uses native clips. |
| Complete reload animation transitions | ✅ | | | Remote presentation remains unfinished. |
| Complete jump animation transitions | ✅ | | | Remote presentation remains unfinished. |
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
| Positional firing sounds | | | ✅ | Native audio; full acoustic parity needs playtesting. |
| Positional impact sounds | | | ✅ | Native audio at the replicated impact. |
| Positional explosion sounds | | | ✅ | Native audio at the replicated explosion. |
| Special-weapon audio loop cleanup | | | ✅ | Stops remote loops when their effect ends. |
| Friendly-fire prevention | | | ✅ | Both players' shared combat excludes friendly damage. |

## Enemies, pedestrians and encounters

| Feature | Planned | Partially synced | Synced | Comments |
| --- | :---: | :---: | :---: | --- |
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
| Enemy projectile authority | | | ✅ | Host-owned projectiles; replicas cannot deal duplicate damage. |
| Incoming player-hit events | | | ✅ | P1 sends the hit to its victim; personal health/armor is applied locally. |
| Enemy health | | | ✅ | Both players' attacks resolve on P1. |
| Enemy shields | | | ✅ | Shared protection state belongs to P1. |
| Enemy status effects | | | ✅ | Shared native status state follows P1. |
| Enemy death type/timing | | | ✅ | P1 commits deaths and kills. |
| Blood effects | | | ✅ | Native presentation is relayed. |
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
| First-mission garage opening | | | ✅ | Fixed OBJECTIVES/BreakableWall destruction on P2; direct native renderer and controller-passage regression. |
| First-mission garage collision | | | ✅ | Actual solid wall collision disappears; both native player controllers traverse the same opening, including after rejoin. |
| First-mission urine tutorial trigger | | | ✅ | Completion/disable state follows P1. |
| Urine-activated shootable buttons | | | ✅ | Native target interactions route through P1. |
| Urine-activated metal buttons | | | ✅ | Uses the requester's native interaction/key handshake. |
| Fractional filler progress | | | ✅ | Preserves native incremental filling. |
| Tank fill progress | | | ✅ | Current fill fraction follows P1. |
| Tank/filler completion | | | ✅ | P1 commits completion and authored consequences. |
| Fill-driven moving objects | | | ✅ | Replicates linked object motion. |
| Fill-driven scaling objects | | | ✅ | Replicates linked object scale. |
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
| Required wall destruction | | | ✅ | Dedicated breakable adapter removes solid geometry without replaying gameplay explosions. |
| Mission moving-object pose | | | ✅ | Referenced mover transforms follow P1. |
| Mission rotating-object pose | | | ✅ | Referenced rotater transforms follow P1. |
| Mission scaling-object pose | | | ✅ | Referenced transform scale follows P1. |
| Mission control-panel state | | | ✅ | Host execution and pressed/activation state are replicated. |
| Mission light color | | | ✅ | Current referenced light colors are transmitted as bounded values. |
| Mission light intensity | | | ✅ | Current referenced light intensities follow P1. |
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

## Broader world coverage and future saves

| Feature | Planned | Partially synced | Synced | Comments |
| --- | :---: | :---: | :---: | --- |
| General scripted world triggers | | ✅ | | Expanded host-owned mission graph, state adapters and exit rules exist. Unclassified callbacks and complete natural campaign acceptance remain; see [mission pass details](mission-trigger-sync-plan.md). |
| Unrelated puzzle machinery | ✅ | | | Outside the implemented door/combat/urine chains. |
| Moving elevator platforms | | ✅ | | Referenced platform poses and elevator state are replicated; every natural ride/attachment scenario still needs acceptance. |
| TNT/destructible gates | | ✅ | | WireWallGate uses requester-owned item validation and host state/destruction; natural gate sequences need acceptance. |
| Cat-paw machinery | | ✅ | | Referenced movers/rotators/switches are covered; specialised trap damage and full natural puzzle flow need validation. |
| Shootable barrels | ✅ | | | Scenery/destructible synchronization remains future work. |
| Breakable props | ✅ | | | Scenery/destructible synchronization remains future work. |
| Shared checkpoint capture | ✅ | | | Current session snapshots do not provide persistent checkpoints. |
| Shared checkpoint restoration | ✅ | | | Needs complete shared world restoration. |
| Persistent co-op saves | ✅ | | | Separate host-owned multiplayer saves are the agreed design. |
| Restore both players' saved inventories | ✅ | | | Part of future persistent co-op saves. |
| Explicit solo-save import | ✅ | | | Future import flow; never overwrite solo saves implicitly. |

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
| Owned weapon inventory | ✅ | Independent after the one-time joining loadout copy. |
| Remaining ammo inventory | ✅ | Independent after the one-time joining loadout copy. |
| Key inventory | ✅ | Only the interacting player's own keys are checked/consumed. |
| Required-item inventory | ✅ | Only the interacting player's own required items are checked/consumed. |
| Other item counts | ✅ | Personal quantities remain independent. |
| Active personal powerups | ✅ | Personal effects/durations remain independent. |
| Urine powerup type | ✅ | Personal fire/ice/confusion selection; shared targets receive the acting player's results. |
| Urine powerup duration | ✅ | Personal effect timer, not a shared resource. |
| Harmless corpse-piece physics | ✅ | Shared death type/timing; cosmetic pieces can settle differently. |

## Progress score

**81% remains the milestone score, not a percentage of rows checked above.** Splitting features into smaller rows changes the display, not implementation progress. The original 21 equally weighted milestones remain the scoring baseline: **15 synced + (4 partial × 0.5) + (2 planned × 0) = 17 / 21 = 80.95%, rounded to 81%.**

The four partial milestones are remote player model, remote player animations, general scripted world triggers and complete active-session restoration. The two planned milestones are shared checkpoints/world restoration and persistent multiplayer saves. Detail rows above explain each milestone's implemented parts and gaps. The historical urine milestone increased the baseline from 16 / 20 (80%) to 17 / 21 (81%).

Intentionally local systems are excluded. Survival, custom maps, DLC-specific content, matchmaking, relays and dedicated servers are outside this score.

## Combat and actor coverage

The visual-only gun milestone was superseded by shared enemy/combat authority. P1 resolves bullets, projectiles, beams, melee/kicks, hooks/chains, charge/block/shield and reflection results in the implemented native paths. P2 submits attacks rather than independently changing shared enemy health. Personal resources stay local and friendly fire stays off.

The actor pass includes ordinary NPCs, pedestrians, flyers, bosses, runtime-spawned/transformed enemies, `FakeCitizen`/`FakePostalDude` scripted reactions, `BalloonGroup`, `CovidEye`/`MiniCovidEye` and `ElonMechWeakSpotComponent`. Scripted humans retain native invulnerability where appropriate. Replicas suppress local AI, navigation, animation gameplay callbacks and competing rigidbody motion. Native boss HUD health follows P1. Cosmetic particle/light state and native positional actor sounds are also relayed.

## Mission pass verification

- Both peers need the matching 0.7.0 / protocol 7 DLL pair.
- Neighborhood's actual `OBJECTIVES/BreakableWall` failed before the adapter and passes after it: renderer/collider removal, matching native controller passage, either-player activation and rejoin without historical effects. Repeat with `Test-Travel.ps1 -GameDir $GameDir -Mission`.
- Prison walk-in exits and Wallmarkt's interactable bed ending enforce partner gathering, readiness cancellation and one committed finish. Automatic endings wait for independent respawn. Use `-MissionExits -Level LEVEL_AD_Prison` or `LEVEL_AD_Wallmarkt`.
- Prison's extended state fixture verifies current door/mover/mission scalar state, authoritative P2 encounter activation and group progression. Use `-State -Level LEVEL_AD_Prison`.
- Fifteen network scenarios include 12,288 ordered mission-state frames through bounded queues with a delayed consumer. Native snapshots retain frames for retry and use TCP receive backpressure.
- The controlled 60 FPS check measures about 3.7 ms p95 world work, 150 ms p95 sweeps and 16.7 ms p95 frame time. An earlier input run encountered a shared host pause while awaiting samples; the check passed on rerun. This does not establish every intermittent focus/pause issue's cause.
- All 15 maps were inspected with the expanded catalogue. A long sequential run hit a native NPC initialization error entering VIP; the remaining maps passed in a fresh process. Discovery/bounded serialization is not natural campaign completion.
- Shared-combat regressions and representative Prison/Forest/ComicCon machinery catalogue/state checks passed. Use the [verification commands](verification.md#running-checks) to repeat the relevant fixtures; private run folders are not distributed.

## Evidence and practical limits

- Urine regressions use two native processes in Neighborhood, Prison, Hospital and ComicCon: either-player electrical-box/tank activation, catalogued garage visibility/collision and tutorial-trigger state, fractional fill and linked motion/scale, toilet overflow/flush, fireball extinguishing and disconnect/rejoin restoration. The subsequent house-wall regression is fixed by a dedicated destruction adapter; native tests now check the actual renderer, collider and matching player-controller passage before and after joining again. An exposed native boss weak spot also accepts P2's frame-timed urine damage through the existing actor-hit authority path.
- All 15 campaign maps pass discovery and bounded serialization with the new urine/progression catalogue. This does not establish natural completion of every urine puzzle, rail ride, rocket sequence or boss fight. The VIP live fixture stopped in a native shared pause before its test action; its natural progression remains manual acceptance work.
- The final urine build passes shared-combat and bedroom-door reload regressions. Its controlled 60 FPS host check measures 3.5 ms p95 world work and 100 ms p95 completed sweeps; this preserves the frame-budgeted sampling behavior while adding urine state.
- The 15 network scenarios pass with protocol 7, including 12,288 ordered mission frames with bounded send/receive backpressure. One run of the pre-existing loading-failure-reason check failed transiently and passed on rerun; this pass does not claim that transport timing issue has been diagnosed.
- Network acceptance checks cover bounded actor/combat packets, event preservation, authority restrictions, travel, reconnect and build mismatch.
- Two actual local IL2CPP processes pass native combat, door/key/reload, encounter, late-join input, travel/respawn and UI checks. P2 targeting/hit probes exercise a real native enemy attack.
- The input regression reproduces and fixes a stranded `NONE` lock when P2 closes its menu during P1's pause. Five real window focus round trips also verify native movement and mouse-look input after returning to P2; this does not establish that every intermittent Alt-Tab failure had that same cause.
- Host periodic state sampling now yields between actors, encounters, doors and movers under a roughly 3 ms shared capture budget. NPC spawn/enable tracking avoids repeated scene-wide NPC discovery; beam/chain/stream searches are staggered, stable animator/encounter metadata is cached, and menu/save discovery scans run periodically while cached guards remain enforced each frame. Combat events retain their immediate handling and join snapshots still complete before gameplay release.
- Controlled two-instance Neighborhood timings confirmed the previous periodic host hitch: frame p95 after a full sync batch was 105 ms versus 7.8 ms between batches. Host world-work p95 fell from 19.9 ms to 3.2 ms after the fixes. A separate 60 FPS check measured 83 ms p95 for completed world sweeps and the expected 16.7 ms displayed-frame p95. Median capture allocations fell from about 2.5 MB to 1.5 MB per cycle. These are stationary native diagnostic results under two-instance load, not a guarantee about every combat scene, hardware configuration or LAN setup.
- Actor fixtures exercise **49 base-campaign NPC families** for spawning, targeting, movement, health/status/shield and death presentation. Separate fixtures exercise weak spots, balloons, scripted human reactions and the native boss HUD.
- All **15 base-campaign maps** pass authored actor catalogue/snapshot audits: **43 distinct authored NPC families across 243 map/family rows**. These audits establish discovery and bounded serialization, not a complete combat playthrough of every map.
- Two-PC play is verified by the maintainer. Regression testing should continue to exercise natural mission/boss sequences, LAN latency and special attacks alongside native fixtures.

## Remaining work

Shared checkpoints and persistent co-op saves need complete world snapshots. Supported mission triggers and referenced machinery now have shared state, while unclassified callbacks, arbitrary timeline tracks and general decorative scenery/destructibles remain outside complete coverage. Natural campaign/ride/trap acceptance and player-presentation polish remain before treating this development build as a validated release.
