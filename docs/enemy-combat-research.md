# Enemy authority, damage and encounter snapshots

> Historical research/census. Statements about scope, missing features and implementation status describe the dated milestone below. For current 0.7.0 behavior and limits, use [sync progress](sync-progress.md) and [verification](verification.md). Asset identifiers describe the reference build and are not portable runtime constants.

October 12, 2026. Read-only recovered declaration and matching native ISIL research. No new enemy/proxy/combat hook has been runtime-validated. Let `CS` be `<Decompiled>/CSharp/Assembly-CSharp/Hyperstrange/PBD`, and `ISIL` be the matching `<Decompiled>/Cpp2IL/Native-ISIL/IsilDump/Assembly-CSharp/Hyperstrange/PBD` ([path placeholders](../README.md#path-placeholders)). Recovered C# bodies are placeholders.

## Two-player targeting

`NpcAwarenessComponent.Awake` caches the singleton `LevelController.Player` as private `IAiActor _player` (matching native lines 95–100). `UpdatePlayerTarget` and `OnSlowUpdate` are candidate hooks for considering both living players while retaining native range/visibility behavior. Merely copying enemy transforms would leave AI targeting P1 alone.

`IAiActor` exposes transforms/feet/center, velocity, radius/height, alive/targetable/player flags and hit/kill notifications independently of the local camera/controller. `Npc.SetTarget(IAiActor)` and `NpcTargetProcessor` hold current/last/fight targets. However, the target processor treats non-player actors as `Npc` for death subscriptions. A proxy must follow valid native actor semantics and explicitly clear dead/disconnected targets. Managed IL2CPP interface injection and any inert native proxy approach are **untested**; implementation must prove compatibility rather than assume it.

`PlayerTrigger` requires a real living player component, and current remote visuals have no gameplay collision. Two-player encounter/proximity/obstruction integration needs explicit host support without copying the local input/controller hierarchy.

## Damage and death ordering

`Npc.Hit` delegates to `NpcHitProcessorComponent.Hit` (`ISIL/Npc.txt:6261`). Before subtracting health, native handling applies weak spots, freezing/push, targeting and shields. Hit notifications then reach actor callbacks. The lethal path adjusts hit/gib data, calls `ActorManager.ActorWasKilled`, `LevelController.RegisterKill`, optionally `PickupSpawnerWeapon.SpawnPickupsFromNpc`, and NPC death delegates (`ISIL/NpcComponents/NpcHitProcessorComponent.txt:1540–1590`). Applying both local client native hits and host results would duplicate mechanics and progression.

Victim-side player damage should preserve the native `Player.Hit` / `OnPlayerHit` / `ProcessHitData` path instead of writing health directly. `HitData` includes actual projectile/reflection and dealer actor references. An incoming scalar damage amount alone cannot preserve blocking/reflection semantics; map host projectile identity and special-action outcomes deliberately. `HittableManager.OnHit` is a candidate centralized dispatch seam for melee and proxy victims.

NPC death presentation has its own `NpcDeathComponent`: ragdolls, gibs, particles, sounds and decals. Reuse presentation without running full client native kill consequences. Late join applies dead/removed state without historical kill sounds or explosions.

## Enemy identity and replica lifecycle

`NpcController.AddSpawnPoint` and `GatherSpawnPoints` call `Guid.NewGuid` (`ISIL/NpcController.txt:279`, `:3865`). Runtime/save GUIDs, pointers and Unity instance IDs cannot identify the same object on another PC. Verify authored scene/hierarchy/component catalogues and assign host IDs per epoch, with fresh generations for pooled/reused entities and monotonic revisions for updates/tombstones.

`SpawnPointSaveData` records spawned/alive/health/transform, target and movement/link state plus runtime NPC data. `SpawnPoint.Load` can recreate runtime enemies (`ISIL/SpawnPoint.txt:5816`), spawn authored ones (`:5896`), set target (`:5918`) and restore health (`:6004`). These are useful state ingredients, not a complete multiplayer contract; boss phase, animation/status and designer progress need additional adapters. Full solo save loading is unsuitable for personal inventories and save isolation.

Client AI is more than `NavMeshAgent`: `NpcBehaviourComponent` runs state machines, slow updates, positioning/rotation and animator root motion; awareness is separate. Replica mode must suppress attacks, targeting, spawning, damage authority and root motion while preserving animation, rendering and local aiming collision. Session cleanup must restore native behavior safely.

## Progression and loot

`SpawnPointDeathListener.OnDeath` invokes designer `UnityEvent` (`ISIL/SpawnPointDeathListener.txt:883`). Death group listeners and respawn-on-death components can advance waves or spawn more enemies. `LastBossArena` maintains separate Dude/mech kill flags and forcefield/hook/jump-pad/dialogue/callback progress, with singleton-player dependencies. Enemy health/transforms alone cannot reproduce an ongoing encounter. P1 must execute authored combat consequences once; client restoration needs their resulting supported states, not blind replay of old events.

Native `LootController` uses local health/ammo ratios and randomness (`ISIL/LootController.txt:3951` health, `:1917` weapon ammo); `NpcItemDropComponent` can also drop health/armor bags on death. The user chose identical host-selected loot spawns with independent per-player collection and fresh personal copies for late join. This follows P1's adaptive loot roll rather than a separate P2 roll; collection and quantities remain personal/native.

## Active projectiles and late join

Native `Projectile` is poolable, not generically `ISaveable`. It has owner/origin, lifetime and movement/collision/explosion modules. A purpose-built active registry must capture model identity, current position/direction/velocity/age, reflection ownership/revision and subtype targets/motion. An old spawn event alone cannot reconstruct a bouncing grenade, homing shot or lingering bomb.

Hold the existing travel barrier while installing a consistent snapshot of enemies/tombstones, encounter progress, active projectiles, included doors and personal loot spawns. Sequence incremental events against that snapshot, epoch and entity generation. Do not revive an old pooled lifetime or replay a historical kill.

## Required runtime checks

Prove deterministic catalogues in actual independent game instances, P2 actor/target/hit compatibility, all relevant native spawn paths and runtime children, client AI/death/progression suppression, source attribution for P2 attacks/specials, fresh versus historical effects and safe disconnect restoration. Verify native defense mechanics under host hit authority and personal resource ownership. Campaign boss/gate adapters require concrete coverage evidence; metadata presence alone is insufficient.

See [proposed shared-combat scope](shared-combat-spec.md), [gun/door research](gun-door-research.md) and [current progress](sync-progress.md).
