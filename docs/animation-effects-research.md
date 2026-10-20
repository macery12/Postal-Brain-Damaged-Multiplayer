# Original Dude animations and firing effects

> Historical research/census. Statements about scope, missing features and implementation status describe the dated milestone below. For current 0.8.0 behavior and limits, use [sync progress](sync-progress.md) and [verification](verification.md). Asset identifiers describe the reference build and are not portable runtime constants.

Research date: October 11, 2026. This note records read-only inspection of the installed game's serialized assets using UnityPy 1.25.4, its recovered declarations, and official Unity documentation. Asset IDs describe the reference build; runtime code must resolve references and clip names rather than hard-code those IDs. Runtime rendering and playback acceptance are separate from these findings. Source paths use the `<GameDir>` and `<Decompiled>` [path placeholders](../README.md#path-placeholders).

## Available character animations

The coat/gown/boots character already used by PBDMP has a complete native armed animation set. `Resources.Load<NpcId>("Npc/NoLifeMatters/NPC_AlterDude")` resolves the existing identifier, whose `_npcPrefab` references `NPC_AlterDude`. Read the prefab's Animator controller only to get `runtimeAnimatorController.animationClips`. That array contains the clips below, with some duplicate references. It is not necessary to visit the chase level to acquire this animation set. Installed shared character and animation assets (`<GameDir>/POSTAL Brain Damaged_Data/sharedassets0.assets`), NpcId declaration (`<Decompiled>/CSharp/Assembly-CSharp/Hyperstrange/PBD/NpcId.cs`), [Unity RuntimeAnimatorController.animationClips](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/RuntimeAnimatorController-animationClips.html)

| Clip names, exact capitalization | Duration | Events in source clip |
| --- | --- | --- |
| `AlterDude@idle` | 2.133 s | None |
| `alterDude@idleTarget_machinegun`, `alterDude@idleTarget_shotgun`, `alterDude@idleTarget_rocketlauncher` | 1.067 s | None |
| `alterDude@runFront_machinegun`, `alterDude@runBack_machinegun`, `alterDude@runLeft_machinegun`, `alterDude@runRight_machinegun` | 0.533 s | Two `Step` callbacks each |
| The same four `runFront/Back/Left/Right` names ending `_shotgun` or `_rocketlauncher` | 0.533 s | Two `Step` callbacks each |
| `alterDude@shoot_machinegun` | 4.933 s | None |
| `alterDude@shoot_shotgun` | 0.533 s | None |
| `alterDude@shoot_rocketlauncher` | 1.067 s | None |
| `alterDude@inAir_machinegun`, `alterDude@inAir_shotgun`, `alterDude@inAir_rocketlauncher` | 0.533 s | None |
| `alterDude@weaponIdle` | 0.0167 s | None |
| `alterDude@weaponSwap` | 0.533 s | `OnAlterDudeWeaponSwap_AnimationCallback` |
| `alterDude@superHeroLanding` | 3.833 s | `OnAlterDudeHeroLand_AnimationCallback` |

Further referenced clips include `alterDude@bunnyHopIdle/Front/Back/Left/Right`, `alterDude@slideFront/Back/Left/Right`, `alterDude@kick`, `alterDude@stagger`, `alterDude@Launch`, and `alterDude@jumpStart`. The names in the table come directly from serialized AnimationClip objects and controller references; state display names are not a substitute. Installed controller 6383 and AnimationClip objects (`<GameDir>/POSTAL Brain Damaged_Data/sharedassets0.assets`)

The machinegun holding pose is a practical initial mapping for guns without their own boss pose. Shotguns and rocket launchers have authored families. Pistol-specific third-person clips were not present in this set. These family mappings are implementation choices and need visual checking for grip alignment.

## Exact binding compatibility

The coat/gown mesh uses 63 bones, with generic animation rather than automatic humanoid retargeting. For `AlterDude@idle`, `alterDude@idleTarget_machinegun`, `alterDude@runFront_machinegun`, and `alterDude@shoot_machinegun`, all **63 distinct Transform binding path hashes** match the source rig's paths relative to **`AlterDude@bind`**. They match zero rig paths relative to the NPC root or its `Renderer` wrapper. Each inspected clip has 196 bindings: 189 Transform position/rotation/scale bindings and seven Animator motion bindings. Serialized character hierarchy, avatar and clip bindings (`<GameDir>/POSTAL Brain Damaged_Data/sharedassets0.assets`)

```text
NPC_AlterDude
  Renderer
    AlterDude@bind             <- native clip sampling root
      AlterDude_SHJntGrp
        AlterDude_TrajectorySHJnt
          AlterDude_ROOTSHJnt
            ... 63 bones ...
      geo_AlterDude
```

PBDMP's reconstructed skeleton keeps the hierarchy and bone names. On that copy, find `Renderer/AlterDude@bind` and use that GameObject for `AnimationClip.SampleAnimation`. Sampling at the entire reconstructed NPC root would use the wrong binding prefix. **Matching Transform paths alone was insufficient in the actual game:** sampling the copied rig without an Animator left the bind pose unchanged. A fresh, **disabled, controller-free Animator** on the copied `AlterDude@bind` root, assigned the original `AlterDude@bindAvatar`, enables native clip sampling. No NPC Animator or runtime controller is copied, so no original controller StateMachineBehaviours are installed. Set `applyRootMotion=false`, `fireEvents=false`, `cullingMode=AlwaysAnimate`, and `enabled=false`; explicit `SampleAnimation` calls drive the bones. This recipe is verified by the runtime probe below rather than inferred only from path hashes. Verified RemoteAnimation construction and sampling (`src/PBDMP.Plugin/RemoteAnimation.cs`), Current VisualClone (`src/PBDMP.Plugin/VisualClone.cs`)

The original prefab's Animator (53450) is on `NPC_AlterDude` (14745), while its imported Avatar (6248) describes paths relative to `AlterDude@bind`. The Avatar contains 69 nodes: an empty root, four geometry siblings, the skeleton group, and 63 animated bone descendants. Its root-motion bone index 6 maps to `AlterDude_SHJntGrp/AlterDude_TrajectorySHJnt`, hash 742629434. The imported original clips are non-legacy; changing a copied clip's legacy flag is not the verified playback solution. Original Animator and Avatar serialized data (`<GameDir>/POSTAL Brain Damaged_Data/sharedassets0.assets`)

Unity documents that sampling writes animated properties at the requested time; it does not blend two clips for the caller. A two-sample Transform pose blend or a controller-free PlayableGraph is a separate future choice. The runtime fix above uses direct sampling with the disabled binding Animator. [Unity AnimationClip.SampleAnimation](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/AnimationClip.SampleAnimation.html), [Unity Playables examples](https://docs.unity3d.com/2021.3/Documentation/Manual/Playables-Examples.html)

The `runFront_machinegun` source reports a root delta of **5.1519 units forward over 0.5333 seconds**. Its `m_LoopTime` is true. Idle/idleTarget/shoot-machinegun are also serialized as looping. The presence of motion curves means a visual-only locomotion implementation must suppress clip displacement; snapshots continue to own world movement. The source `AlterDude_TrajectorySHJnt` starts at zero local position and identity rotation. When directly sampling, restore the trajectory's root displacement and any sampling-root transform changes after each sample. Verify that ordinary skeletal bob is retained while the mesh stays anchored to remote feet. For an Animator/PlayableGraph implementation, set `applyRootMotion=false` before playback and verify the same invariant. Serialized run clip 5943 and trajectory Transform (`<GameDir>/POSTAL Brain Damaged_Data/sharedassets0.assets`), [Unity Animator.applyRootMotion](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Animator-applyRootMotion.html)

## Safe playback recipe

1. Read the source prefab references on the Unity thread; do not instantiate the NPC or assign its controller to the remote avatar. The original `NPC_AlterDude` controller contains **22 StateMachineBehaviours** and its hierarchy contains gameplay components. Original controller and prefab (`<GameDir>/POSTAL Brain Damaged_Data/sharedassets0.assets`)
2. Reconstruct only mesh renderers and the required Transform/bone hierarchy, as the current visual clone does. Read clip references from the source controller and deduplicate by object identity/name.
3. Add a fresh disabled Animator at the copied `AlterDude@bind` root, share only the original Avatar, and leave its runtime controller unset. Make mod-owned copies of the chosen AnimationClips and set their `events` to an empty array, leaving game assets unchanged. Also set `fireEvents=false`; that option suppresses AnimationEvents, not StateMachineBehaviours, so it does not make the boss controller safe. Verified binding Animator (`src/PBDMP.Plugin/RemoteAnimation.cs`), [Unity AnimationClip.events](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/AnimationClip-events.html), [Unity Animator.fireEvents](https://docs.unity3d.com/ScriptReference/Animator-fireEvents.html)
4. Choose idle/armed-idle/forward/backward/left/right from smoothed horizontal velocity in avatar-local coordinates. Sample loop phase from elapsed time and scale locomotion playback speed within a reasonable bound. Avoid resetting loop time on every network packet.
5. After sampling or manual graph evaluation, apply aim offsets to the upper body and attach the visual gun to the actual animated wrist. Apply explicit gun recoil on new shot-sequence edges. Native holding poses should remain the base pose instead of overriding both arms back into a straight line every frame.
6. Keep world positioning outside the animation hierarchy, suppress clip displacement, clear pose/interpolation state after teleports or scene mismatches, and release copied clips/graph state with the remote avatar.

## Original gun effect access

The game separates its firing effect from shot/damage logic. `WeaponAction.FxModule` serializes `_muzzleFlash` and `_projectileOrigin`; its `MuzzleFlash(Transform parent)` method is distinct from projectile origins and audio methods. `Hyperstrange.PBD.MuzzleFlash` serializes a **ParticleController** field named `_muzzleFlash`, plus `FlashDisabled` and `_zeroRotation`. This supplies an original authored particle effect source to inspect through each local `WeaponId.PlayerWeaponPrefab`. Recovered C# method bodies are placeholders; use the metadata as declarations rather than executing the recovered source. WeaponAction declaration (`<Decompiled>/CSharp/Assembly-CSharp/Hyperstrange/PBD/WeaponAction.cs`), MuzzleFlash declaration (`<Decompiled>/CSharp/Assembly-CSharp/Hyperstrange/PBD/MuzzleFlash.cs`)

Prefer the weapon-specific effect reference over searching unrelated loaded ParticleSystems. Reuse a detached visual particle prefab whose components have been audited; do not instantiate an entire player weapon or call its firing action. Play the copied particle hierarchy from a mod-owned muzzle Transform on shot-sequence edges and retain original particle shaders/materials. Exact per-weapon effect hierarchies and rendered acceptance are recorded by implementation diagnostics; this note does not claim every weapon effect has been validated. [Unity ParticleSystem.Play](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/ParticleSystem.Play.html)

## Acceptance still needed at implementation time

### Native particle source audit (October 12, 2026)

The actual player pistol and standard shotgun muzzle references were followed through serialized `MuzzleFlash` components to their ParticleController assets in `sharedassets0.assets`. This audit verifies module configuration in the reference build; it does not infer safety for all weapons from these two sources. Installed muzzle components and particle assets (`<GameDir>/POSTAL Brain Damaged_Data/sharedassets0.assets`)

| Weapon | Original particle root | ParticleSystem IDs in its hierarchy |
| --- | --- | --- |
| `PlayerWeapon_Pistol` | `VFX_Muzzle_Flash_Pistol` (GameObject 24089, ParticleController 69569) | 57421, 57169, 56781, 57438 |
| `PlayerWeapon_Shotgun` | `VFX_Muzzle_Flash_Shotgun` (GameObject 19084, ParticleController 66498) | 57092, 57315, 56865, 56438, 57140 |

Pistol's source MuzzleFlash component 66290 points to ParticleController 69569; shotgun's source MuzzleFlash component 69270 points to ParticleController 66498. All nine audited ParticleSystems have `CollisionModule.enabled=false`, `collisionMessages=false`, `TriggerModule.enabled=false`, and `SubModule.enabled=false`. Their serialized subemitter array contains only a null emitter reference (`m_FileID=0`, `m_PathID=0`), and they are non-looping. All nine have serialized `stopAction=0` (`ParticleSystemStopAction.None`), so they do not destroy or disable themselves or issue a stopped callback. `stopAction` is a top-level serialized ParticleSystem property in this build, not a field inside serialized `InitialModule`. Installed particle module data (`<GameDir>/POSTAL Brain Damaged_Data/sharedassets0.assets`), [Unity ParticleSystemStopAction](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/ParticleSystemStopAction.html)

Each hierarchy contains only Transform, ParticleSystem, and ParticleSystemRenderer components, plus one ParticleController MonoBehaviour on the root. The root's script reference is external file 1 / path 5416; the external file is `globalgamemanagers.assets`, whose MonoScript identifies `Hyperstrange.PBD.ParticleController` in `Assembly-CSharp.dll`. No child scripts, rigidbodies, or colliders appear in either audited hierarchy. Remove that root ParticleController from the inactive clone before activation; its Awake/OnEnable register game lifecycle behavior even though the underlying native particles have no enabled physics modules. Script identity (`<GameDir>/POSTAL Brain Damaged_Data/globalgamemanagers.assets`), ParticleController lifecycle declarations (`<Decompiled>/CSharp/Assembly-CSharp/Hyperstrange/PBD/ParticleController.cs`)

The pistol hierarchy has a flash, sparkles and two local fire systems; the shotgun has a flash, two sparkle systems and two local fire systems. `PlayerWeapon_MeatShotgun` and `PlayerWeapon_StickyHand` also point to the same audited shotgun ParticleController asset, but their weapon mounting and remote effect presentation still require separate checking. Serialized source hierarchies and references (`<GameDir>/POSTAL Brain Damaged_Data/sharedassets0.assets`)

### Runtime acceptance

Version 0.4 adds an asset-lifetime regression: `Resources.UnloadUnusedAssets` reproduced the live 0.3 `AnimationClip.get_length` exception. Copied clips without a controller were referenced only by the managed dictionary, which Unity's native asset scan does not retain. PBDMP now marks owned copies `HideFlags.DontUnloadUnusedAsset` and explicitly destroys them on Dispose. Native animation/weapon checks pass after the cleanup completes. Clip ownership (`src/PBDMP.Plugin/RemoteAnimation.cs`), [Unity Resources.UnloadUnusedAssets](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Resources.UnloadUnusedAssets.html), [Unity HideFlags.DontUnloadUnusedAsset](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/HideFlags.DontUnloadUnusedAsset.html)

The October 12 runtime probe with the fresh binding Animator reports **90.722 degrees maximum joint rotation difference** between `alterDude@runFront_machinegun` samples at 0.05 and 0.30 seconds, and `eventless=True` for copied clips. This proves changing bone poses rather than only loading clip names. The original pistol effect is also confirmed playing with four script-free ParticleSystems and producing **154 visible pixels** using the game's `Universal Render Pipeline/Particles/Unlit` shader. These are synthetic runtime acceptance checks; actual two-PC gameplay and mounting quality are still distinct checks. Probe implementation (`src/PBDMP.Plugin/RemoteAnimation.cs`), Game smoke log (`<GameDir>/BepInEx/LogOutput.log`), Visual diagnostics implementation (`src/PBDMP.Plugin/VisualDiagnostics.cs`)

- Visible idle and forward/back/strafe movement differ from the bind pose, without affecting the source NPC or invoking `Step`/weapon/landing callbacks.
- Native arm holding pose, wrist attachment, aim pitch and recoil remain coherent through weapon switches.
- Animation samples do not translate the mesh away from replicated feet, drift between loops, or move the networking root.
- Native muzzle effect renders in the running game's URP, retriggers across several shots, stops and cleans up after unequip/disconnect, and cannot deal damage or emit projectiles.
- Missing animation/effect assets use a visible safe fallback and a clear diagnostic.
- Two-PC gameplay verifies actual fire callbacks and high-rate shots, since synthetic poses alone cannot verify gameplay event capture.
