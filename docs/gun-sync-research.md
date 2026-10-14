# Visual gun synchronization, version 0.2

> Historical research/census. Statements about scope, missing features and implementation status describe the dated milestone below. For current 0.8.0 behavior and limits, use [sync progress](sync-progress.md) and [verification](verification.md). Asset identifiers describe the reference build and are not portable runtime constants.

Scope confirmed October 11, 2026: replicate equipped weapon, aim and visible firing effects. Damage, projectiles, enemies and world state stay local.

## Primary local API evidence

`Hyperstrange.PBD.Player.CurrentWeapon` gives the active `PlayerWeapon`. That weapon exposes `WeaponId`, and `EntityId.ID` supplies a stable serialized GUID string. `Player.WorldCamera` provides view yaw and pitch. The Player also has an `Action<PlayerWeapon>` field `OnWeaponFired`, distinct from input-button, no-ammo and charge callbacks. These declarations are available in the Player metadata (`<Decompiled>/CSharp/Assembly-CSharp/Hyperstrange/PBD/Player.cs`), weapon metadata (`<Decompiled>/CSharp/Assembly-CSharp/Hyperstrange/PBD/PlayerWeapon.cs`), and EntityId metadata (`<Decompiled>/CSharp/Assembly-CSharp/Hyperstrange/PBD/EntityId.cs`). Recovered C# bodies are placeholders; runtime behavior must be tested. `<Decompiled>` is one of the [path placeholders](../README.md#path-placeholders) defined in the README.

`WeaponId` references both `_pickupPrefab` and `PlayerWeaponPrefab`. Pickup meshes are useful world geometry; first-person prefabs can include camera-relative arms, controllers, animations and weapon effects. The mod reads these assets but never instantiates their gameplay hierarchies. See WeaponId (`<Decompiled>/CSharp/Assembly-CSharp/Hyperstrange/PBD/WeaponId.cs`).

## Implemented presentation

The local adapter samples equipped GUID and view yaw/pitch at Unity frame rate, publishes the latest snapshot at 20 Hz, and subscribes to the actual player's `OnWeaponFired` delegate. The subscription is removed/rebound on player replacement. A monotonically incremented uint32 shot sequence survives local player replacement; it is a visual signal, not a hit/damage event.

Protocol 2 appends weapon GUID, aim pitch and shot sequence to the player pose. Version 0.3 retains this format; protocol 1 peers are rejected. Use the same current mod on both PCs for matching presentation. Initial packets establish a sequence baseline so old shots do not replay when joining or returning to a level. A changed sequence triggers the local weapon's audited native muzzle particles and a short native shooting pose; unchanged packets do not retrigger it. Multiple shots between snapshots can collapse into one visual pulse.

The receiver resolves GUIDs against local WeaponId assets, constructs fresh transforms and mesh renderers from pickup geometry, and mounts the gun at the remote right wrist. Skinned pickup geometry is baked into mod-owned static meshes. Materials use URP Unlit with original base textures, falling back to first-person materials for empty pickup slots. No controllers, colliders, ammo, sounds, projectile spawners or damage methods are copied/executed. [Unity SkinnedMeshRenderer.BakeMesh](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/SkinnedMeshRenderer.BakeMesh.html), [Unity Material.SetTexture](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Material.SetTexture.html)

Gun facing follows replicated yaw/pitch. Initial placement normalizes pickup bounds and their longest axis; it is a generic mounting rule, not authored per-weapon alignment. Unsupported/unloaded weapon assets produce a warning and no gun rather than spawning gameplay objects. All resource and rendering calls run on Unity's main thread.

## Evidence and remaining checks

Network tests pass GUID/pitch/sequence round trips, bidirectional updates, invalid pitch/oversized IDs and old-protocol rejection. Unity synthetic tests pass actual pistol and standard shotgun geometry loading, equip/swap, aim pitch, sequence-triggered flash, no flash invented by a switch, and unequip/cleanup. Renderer pixel tests exercise the running game's URP; an enabled renderer alone is insufficient.

The original cylinder reproduced zero pixels with the built-in Unlit/Color shader and 10,820 pixels after changing only to URP Unlit. This identified the missing-cylinder cause. New body/gun material tests use the actual pipeline too.

Version 0.3 replaces the primitive sphere with original `VFX_Muzzle_Flash_Pistol` and `VFX_Muzzle_Flash_Shotgun` hierarchies. Clones stay inactive until gameplay components are removed, preserve native particle materials, and simulate on unscaled time. Native effect sources are allowlisted after serialized module audits because collision/trigger/stop-action setters are stripped from the reference build's IL2CPP bindings. These two effects and pistol retriggering pass pixel/runtime checks. The Dude's native armed holding/run/shoot clips animate the copied skeleton without the boss controller. See [animation/effects research](animation-effects-research.md).

Still requires real two-PC validation: the native weapon-fired callback during actual shooting, rapid/burst fire, alt fire/charge weapons, all weapon models and mounting offsets, crouching, player death/replacement, reload and level transitions. Other muzzle sources require an audit before enabling. Remote audio, projectile synchronization, remote hit registration and shared damage remain unimplemented.
