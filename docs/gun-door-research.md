# Gun and door synchronization: native findings

> Historical research/census. Statements about scope, missing features and implementation status describe the dated milestone below. For current 0.7.0 behavior and limits, use [sync progress](sync-progress.md) and [verification](verification.md). Asset identifiers describe the reference build and are not portable runtime constants.

October 12, 2026. Read-only inspection of the installed game's recovered declarations and matching native ISIL. **No new hooks or synchronization were runtime-tested by this research.** Implementation awaits the scope interview. Existing installed behavior remains version 0.5.

Sources are local game data: `<Decompiled>/CSharp/Assembly-CSharp/Hyperstrange/PBD` and matching `<Decompiled>/Cpp2IL/Native-ISIL/IsilDump/Assembly-CSharp/Hyperstrange/PBD` ([path placeholders](../README.md#path-placeholders)). Recovered C# contains placeholder bodies; use native ISIL to establish call ordering and runtime tests to establish compatibility. Existing gun presentation is documented in [gun-sync-research.md](gun-sync-research.md).

## Gun capture and presentation

The current `LocalWeaponObserver` observes `Player.OnWeaponFired`. A cumulative shot counter rides the latest 20 Hz pose, so multiple shots can collapse into one flash. It cannot describe individual bullet origins, paths, impacts, secondary modes or projectile lifetime.

| Native path | Finding |
| --- | --- |
| Pistol / shotgun | `ProjectileModelBasic` creates fast physical projectiles. Collision uses casts along movement; these are not a separate hitscan API. |
| Machinegun | Nail projectiles; time-bomb secondary. |
| Rocket launcher | Rocket primary; charged, bouncing grenade secondary. |
| Bubble gum | Gum projectile; local shield, reflection and gum balloon have additional state. |
| Lightning | Continuous beam and chaining conductors; brain projectile secondary. |
| Dildo bow / nuclear syringe | Charged model creation carries charge-dependent behavior. |
| Gatling / cat cannon | Tracking, homing, priming or recall paths outside simple basic shooting. |
| Shovel / unarmed / hook / pee-specific paths | Melee, blocking, reflection, hook/chain/player movement and streams need dedicated adapters. |

Enumerate registered `WeaponId` assets and filter `Category == DLCCategory.NONE` to establish base-campaign coverage. A class present in metadata is not evidence that its weapon belongs to the agreed campaign scope.

`Projectile._OnProjectileSpawned` fires in `OnEnable` before pooled instances are initialized. It can expose stale owner/speed fields. Concrete `ProjectileModel*.CreateInstance` postfixes provide origin, direction, owner, originating weapon and prefab after creation; assign a new network identity for every pooled lifetime. Inspect ownership assignment timing per model. Capture actual collision/impact and `OnDisable`, including subclass behavior, rather than inferring hits on the receiver.

Reusable assets include `ProjectileModel.Prefab`, `ProjectileTrailComponent._trailPrefab`, `ProjectileVfxComponent` activation/despawn sources, `WeaponAction.FxModule` muzzle/fire assets and `LightningGunBeamRenderer.SetPoints`. Explosion settings expose original VFX/audio. Native projectile audio components also expose activation/impact/despawn events.

Remote replicas must be presentation-only while enemies remain independent: reconstruct approved renderers/particles in inactive staging, remove gameplay behavior/colliders/rigidbodies before activation and apply sender trajectories/events on Unity's main thread. Audit particle collision/trigger modules before reuse; some module setters are stripped in this IL2CPP build. Do not call native `Shoot`, `ForceAttack`, beam damage behavior or `Explosion.Invoke` on receipt: those consume ammo, alter local gameplay, damage divergent enemies and trigger world interactions.

Consistent enemy damage requires shared enemy identities, host enemy state and client replicas. Local player health/armor and per-player pickups can remain independent under that future combat authority.

## Door families and state

Declarations and native dumps for the following components are in `Contraptions` under the source roots above.

| Component | Hooks / state | Synchronization requirement |
| --- | --- | --- |
| `Door` | `UpdateDesiredRotationAndMove`, `Move`, `OpenDoor`, `CloseAndLockDoor`; desired/previous angle, lock, first interaction | Transmit host target angle: independently opening on each PC can choose opposite swing directions. Native tween/audio/occlusion need controlled replay. |
| `DoubleDoor` | Similar fields; `MoveDoors` | Synchronize both leaves and collision state. |
| `SlidingDoors` | `UpdateDoorPosition`, open/close/lock/icon methods; open, locked, hidden icon, first interaction | Suppress independent client automatic/script changes; host decides transitions and closing obstruction. |
| `ElevatorDoors` | `MoveDoors`, `_moved`, save/load/reset | One-way door panels; avoid replaying completed motion. The moving elevator platform is a separate system. |
| `ToiletDoor` | `Hit` / `Push` apply rigidbody force | Needs host-owned physics transform/state snapshots; replaying forces on both PCs diverges. |
| `Vault` | Interaction, `_opened`, `_keyActive`, first interaction | Restore door/key presentation without duplicating inventory or pickup effects. |
| `CatDoor` | Paw attack/fake attack, shockwave, breakables | A damaging hazard and sequence, beyond ordinary door motion. |
| `WireWallGate` | TNT interaction/count, delayed explosion, breaker and sequence notifications | Inventory consumption plus destructible world progression; requires a separate explicit scope. |
| `Mover` / `Rotater` / `Toggler` / `MoverController` | Move/back, rotate/back, toggle/change-state | Some authored gates use these generic components. Identify door-linked instances; synchronizing every mover would also alter unrelated machinery. |

`InteractableRequiredItemsComponent.TryInteract` checks and sometimes consumes the interacting player's inventory. Running a P2 request through P1's native player would use the wrong inventory and position. Local keys/items therefore need a clear requester validation/consumption policy before host state commitment.

Native `InteractableStateListener` callbacks can drive enemy spawns, sequences and unrelated movers. Replay presentation/state without blindly invoking authored callbacks twice. `PlayerTrigger` requires an actual living `Player`; the existing remote display cannot activate host proximity triggers. Host closing checks also lack P2's collision body. Include explicit remote proximity/occupancy handling rather than adding gameplay scripts to the visual avatar.

## Identity, late join and coverage

Native `SaveController.BehaviourToSave` GUIDs are generated at runtime and are unsuitable as cross-PC identity. Candidate identity is scene name + full hierarchy sibling path + component type/index, captured before hierarchy mutations. Verify identical registries across real game instances, exchange a manifest/hash and assign host session IDs. Do not silently match different registries.

Individual door `Save()` methods offer in-memory state ingredients. Avoid `SaveController.Save`, which reaches profile-writing paths. `Door.Load` kills tweens and restores angles/locks/occlusion; `SlidingDoors.Load` restores immediate positions, kills tween/actor-check work and does not emit `OnStateChanged` in the inspected method. Elevator doors and vaults restore completed states immediately. Runtime checks must verify live updates, initialization and cleanup.

Late join must receive current door state before the existing travel readiness barrier releases. Event history alone cannot restore an already-open door. Include a per-map component coverage report and test supported families; a short class allowlist cannot justify an “all campaign doors” claim.

## Decisions awaiting confirmation

Gun appearance versus shared enemy damage; primary/secondary and special-mode coverage; remote audio and friendly fire. Door families versus platforms/destructible gates/hazards; either-player interaction under P1 authority; local key consumption; P1-only enemy/script outcomes. Record confirmed decisions in [roadmap.md](roadmap.md) and update [sync-progress.md](sync-progress.md) only after implementation and evidence exist.
