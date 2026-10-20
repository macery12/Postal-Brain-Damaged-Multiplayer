# Postal Dude player model research

> Historical research/census. Statements about scope, missing features and implementation status describe the dated milestone below. For current 0.8.0 behavior and limits, use [sync progress](sync-progress.md) and [verification](verification.md). Asset identifiers describe the reference build and are not portable runtime constants.

Research date: 2026-10-11. Sources are the reference game build's serialized assets and recovered declarations/native instructions, plus official Unity documentation. Asset inspection used UnityPy 1.25.4 against files read only. Path IDs below identify this build; they are diagnostic evidence, not stable runtime identifiers. Source paths use the `<GameDir>` and `<Decompiled>` [path placeholders](../README.md#path-placeholders).

## Result

The preferred source matching the requested coat, patterned turquoise gown and boots is **geo_AlterDude**, available through the Resources **NpcId** asset `Npc/NoLifeMatters/NPC_AlterDude`. Load the identifier, read its existing prefab reference, then reconstruct only its visual mesh and 63-bone skeleton. It does not require visiting a chase level first. Its original `AlterDude_BC` texture contains the black coat, patterned gown, boots, red hair, sunglasses and yellow smiley badge shown in the reference. Version 0.2's runtime render now confirms those visible features. Global resource registry (`<GameDir>/POSTAL Brain Damaged_Data/globalgamemanagers`), shared character prefab/material/texture (`<GameDir>/POSTAL Brain Damaged_Data/sharedassets0.assets`), [isolated model render](images/player-model.png)

The menu has another full Postal Dude body with a 74-bone rig, but its runtime preview showed a gown and bare feet without the coat. Keep it as a fallback rather than treating it as an exact match. The first-level **Npc_FakePostalDude** chase character uses the preferred **geo_AlterDude** mesh and **AlterDude** material. These two bodies have different skeletons. First-level serialized scene (`<GameDir>/POSTAL Brain Damaged_Data/level3`), shared character assets (`<GameDir>/POSTAL Brain Damaged_Data/sharedassets0.assets`)

## Confirmed coat-and-boots source

The ResourceManager maps `npc/nolifematters/npc_alterdude` to `sharedassets0.assets` object 60640. This object is **NpcId**, a ScriptableObject, not a GameObject and not the NpcModel settings object. Its script reference resolves to MonoScript 347 in `globalgamemanagers.assets`, explicitly naming `Hyperstrange.PBD.NpcId`. Raw serialized data at offset 128 contains a prefab reference to GameObject **14745**, `NPC_AlterDude`. `NpcId` declares `_npcPrefab` and public `AvailablePrefabs`. Local NpcId declaration (`<Decompiled>/CSharp/Assembly-CSharp/Hyperstrange/PBD/NpcId.cs`), script identity (`<GameDir>/POSTAL Brain Damaged_Data/globalgamemanagers.assets`), prefab and identifier (`<GameDir>/POSTAL Brain Damaged_Data/sharedassets0.assets`)

The concrete asset access path is below. The implementation uses the generated `_npcPrefab` field accessor to read the same validated prefab reference.

```csharp
var id = Resources.Load<Hyperstrange.PBD.NpcId>(
    "Npc/NoLifeMatters/NPC_AlterDude");
var prefab = id.AvailablePrefabs[0]; // Validate non-null and array bounds.
var visual = prefab.transform.Find("Renderer/AlterDude@bind");
```

Do not instantiate `NPC_AlterDude`. Its root includes NPC scripts, NavMeshAgent, Rigidbody and an Animator using the boss controller. Read its Transform/renderer metadata and construct a fresh visual hierarchy. Serialized original prefab (`<GameDir>/POSTAL Brain Damaged_Data/sharedassets0.assets`)

```text
NPC_AlterDude (14745)
  Renderer
    AlterDude@bind (24536)
      AlterDude_SHJntGrp
        AlterDude_TrajectorySHJnt ...
      geo_AlterDude (22712; renderer 54727)
      geo_MachineGun
      geo_RocketLauncher
      geo_Shotgun
```

`geo_AlterDude` is a single skinned body/head mesh, path ID 4647, with 63 bones/bind poses. It references material 1528 **AlterDude**, texture 2654 **AlterDude_BC** (256 × 256), and root bone Transform 47910. Its renderer bounds span about y=-0.001 to 1.765. Avatar 6248 **AlterDude@bindAvatar** is generic. Root Animator controller 6383 **NPC_AlterDude** has 22 StateMachineBehaviours, so it must not be copied for a visual-only remote player. Select only `geo_AlterDude` and its required bone transforms; mount replicated player weapons separately instead of copying all three boss guns. Original serialized mesh/rig/controller (`<GameDir>/POSTAL Brain Damaged_Data/sharedassets0.assets`)

## Confirmed Resources source and rig

The ResourceManager object at path ID 13 in `globalgamemanagers` maps `menuscenecomponent` to file ID 5 / path ID 543, the `MenuSceneComponent` GameObject in `resources.assets`. Its child chain is:

```text
MenuSceneComponent (GameObject 543)
  Dude_Transform (2521; local offset y=0.262, z=0.058)
    PostalDudeMenu@bind (2152)
      DudeMenu_SHJntGrp
        DudeMenu_TrajectorySHJnt
          DudeMenu_ROOTSHJnt ...
      NPC_PostalDud_body (900)
        NPC_PostalDud (1230)
```

The fallback path is `Resources.Load<GameObject>("MenuSceneComponent")`, followed by `transform.Find("Dude_Transform/PostalDudeMenu@bind")`. Unity Resources paths omit extensions and are case insensitive. This path also succeeded in runtime tests, showing the coatless menu variant. [Unity Resources.Load](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Resources.Load.html), serialized resources (`<GameDir>/POSTAL Brain Damaged_Data/resources.assets`)

| Object | Serialized evidence |
| --- | --- |
| Skinned body | Mesh path ID 149, `NPC_PostalDud_body`; 74 bind poses; renderer 4851; 74 bones |
| Skinned head | Mesh 122, `NPC_PostalDud`; 74 bind poses; renderer 4849; 74 bones |
| Original material | Both renderers reference `sharedassets0.assets` material 1531, `PostalDude` |
| Original texture | Material base map is texture 3993, `PostalDude_BC`, 256 × 256 |
| Generic avatar | `PostalDudeMenu@bindAvatar`, avatar 214; all humanoid bone mappings are -1 and humanoid description is empty |
| Menu controller | AnimatorController 215, `MENU_PostalDude`, one state, `Sit_idle` clip 211, duration about 2.13 seconds |
| Static body/head | Meshes 148 / 121 respectively, no bind poses; separate menu objects use `PostalDudNOslippers` material 1530 |

The body bind bounds span about y=-0.002 to 1.537; the head spans y=1.442 to 1.749. Preserve the skinned mesh's bone references, bind poses and source transforms. Do not treat a MeshFilter copy of a skinned body as a ready posed model. The menu bones include `DudeMenu_l/r_Arm_ShoulderSHJnt`, `...ElbowSHJnt`, `...WristSHJnt`, `DudeMenu_Spine_01SHJnt`, `DudeMenu_Spine_TopSHJnt`, `DudeMenu_Head_AuxSHJnt`, and several dress helper bones. Local model/material data (`<GameDir>/POSTAL Brain Damaged_Data/resources.assets`), local shared textures/materials (`<GameDir>/POSTAL Brain Damaged_Data/sharedassets0.assets`), [Unity SkinnedMeshRenderer.bones](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/SkinnedMeshRenderer-bones.html)

The generic-rig conclusion comes from serialized mappings; inspect `avatar.isHuman` at runtime for an explicit API result. The menu idle clip is not a walking animation. Its controller has no StateMachineBehaviours and its clip has no events, but the full model hierarchy contains **295 GameObjects**, including attached props/weapons, MonoBehaviours, particles and MeshColliders. The root alone contains just Transform and Animator; that does **not** make the full hierarchy safe to instantiate as-is. [Unity Avatar.isHuman](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Avatar-isHuman.html), local Resources hierarchy (`<GameDir>/POSTAL Brain Damaged_Data/resources.assets`)

## Chase character and animation evidence

In `level3`, `Npc_FakePostalDude` GameObject 17343 and its duplicate 17342 have Animator, Rigidbody, and gameplay MonoBehaviours. Their visual `geo_AlterDude` renderer references `sharedassets0.assets` mesh 4647 with **63 bind poses**, material 1528 **AlterDude**, and avatar 6248 **AlterDude@bindAvatar**. That avatar also has empty humanoid mappings. Serialized scene (`<GameDir>/POSTAL Brain Damaged_Data/level3`), shared mesh and avatar (`<GameDir>/POSTAL Brain Damaged_Data/sharedassets0.assets`)

Their controller is `sharedassets3.assets` object 653 **NPC_FakeAlterDude**. State names include `postalDude@idle` and `postalDude@runAway`, but referenced clips are actually **fakeAlterDude@idle** (648), **fakeAlterDude@run** (646), **fakeAlterDude@noticeStart** (650) and **fakeAlterDude@noticeEnd** (649). The run clip has two `Step` events. The controller has four StateMachineBehaviours. Chase controller and clips (`<GameDir>/POSTAL Brain Damaged_Data/sharedassets3.assets`)

The recovered `FakePostalDude` declaration exposes path/door references, Animator, Collider, save/load, Hide, `TriggeRun`, and animation callbacks. Native instructions confirm its constructor hashes `notice`, `notice_type`, and `locomotion_speed_multiplier`; `ResetToIdle()` calls `Animator.Play("postalDude@idle", ...)`. This proves animation state names, rather than assuming recovered placeholder C# bodies execute. Declaration (`<Decompiled>/CSharp/Assembly-CSharp/Hyperstrange/PBD/FakePostalDude.cs`), native instructions (`<Decompiled>/Cpp2IL/Native-ISIL/IsilDump/Assembly-CSharp/Hyperstrange/PBD/FakePostalDude.txt`), recovery limitations (`<Decompiled>/README.md`)

The menu generic rig uses `DudeMenu_*` paths while the chase rig uses `AlterDude_*` paths; clip bindings contain path hashes. Direct chase-clip playback on the menu skeleton therefore needs verified rebinding/retargeting. It is not automatic humanoid retargeting. For the first model milestone, a clean reconstructed rig and simple procedural movement/arm pose are a practical route; original locomotion is a separate improvement. Local generic bindings (`<GameDir>/POSTAL Brain Damaged_Data/sharedassets3.assets`), local menu skeleton (`<GameDir>/POSTAL Brain Damaged_Data/resources.assets`)

## Other candidate access paths

The game's Addressables catalog lists `Npc/NoLifeMatters/NPC_AlterDude`, `Assets/Prefabs/Spawners/No Live Matters/The Other Dude.prefab`, and materials `SOLID_PostalDude`, `SOLID_PostalDude 1`, and `SOLID_PostalDudNOslippers_BC`. These are confirmed catalog internal IDs; they do not independently establish that every ID is a callable Addressables key. The typed Resources identifier and its verified prefab reference are the preferred path. Local catalog (`<GameDir>/POSTAL Brain Damaged_Data/StreamingAssets/aa/catalog.json`), resource registry (`<GameDir>/POSTAL Brain Damaged_Data/globalgamemanagers`)

If Addressables supplies a model later, retain its load handle until all renderer copies stop using its meshes/materials. Releasing a LoadAssetAsync handle early can unload dependencies still used by manually cloned GameObjects. Scan loaded Resources only on demand or at scene transitions; `FindObjectsOfTypeAll` includes disabled assets but is slow and is not an asset loader. [Unity Addressables loading/lifetime](https://docs.unity3d.com/Packages/com.unity.addressables@1.19/manual/LoadingAddressableAssets.html), [Unity Resources.FindObjectsOfTypeAll](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Resources.FindObjectsOfTypeAll.html)

## Safe implementation and acceptance

1. Load the typed NpcId source asset on the Unity thread, read its prefab reference and log mesh/material names, bone counts, and bounds. Cache its visual data across level changes using a mod-owned root. If unavailable, fall back to the verified menu body.
2. Build fresh Transform objects for the needed skeleton and a fresh SkinnedMeshRenderer for only `geo_AlterDude` (or body/head renderers for the menu fallback). Remap each renderer's bones and rootBone to the new transforms. Share the original meshes/materials; keep their asset references alive. This avoids constructing NPC/input/camera/weapon systems or enabling serialized effects.
3. Position the avatar at the replicated player's feet, check size and facing in gameplay, and set visible layers explicitly. Body/head material must work with the game's actual render pipeline. A visible name tag alone does not prove mesh visibility.
4. Pose/animate only the new bones. Do not modify source assets. If an Animator is added, disable root motion so snapshots own movement. Inspect/strip copied clip events and controller behaviours before animation reuse. Active Object.Instantiate hierarchies can invoke Awake/OnEnable during cloning, so removing NPC scripts afterward is too late. [Unity Instantiate lifecycle](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Object.Instantiate.html), [root motion](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Animator-applyRootMotion.html), [clip events](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/AnimationClip-events.html)
5. Use a visual gun mesh mounted on the remote rig, driven by replicated weapon identity, aim and firing sequence. Original NPC weapon controllers and player weapons must not execute for the remote visual. The gun-sync API research is recorded separately.
6. Verify on two PCs: body/head visible in the first level and another level, feet height/facing, movement, weapon switches and aim/fire visuals, scene reload, disconnect and reconnect. Check that the story NPC and the original menu character are unaffected. Distinguish visual gun sync from shared projectiles, damage, enemies and authoritative combat.

Runtime tests confirm typed Resources access, body reconstruction, coat/gown/boots appearance, original URP material rendering, and basic gun mounting. Version 0.3 adds original generic idle/armed holding/directional running/shooting clips through a fresh disabled Animator sharing only the Avatar. No NPC controller, gameplay scripts or active colliders are copied. Copied clips have no events; root motion is suppressed. See [animation/effects research](animation-effects-research.md) for binding requirements and runtime evidence. Still unknown until gameplay validation: asset lifetime across real level transitions, gameplay lighting, feet/facing on both PCs, natural transitions and all weapon mounting offsets. Shared combat remains unimplemented. See [verification](verification.md).
