# Campaign travel and independent respawn

> Historical research/census. Statements about scope, missing features and implementation status describe the dated milestone below. For current 0.7.0 behavior and limits, use [sync progress](sync-progress.md) and [verification](verification.md). Asset identifiers describe the reference build and are not portable runtime constants.

Scope confirmed October 12, 2026: base campaign; P1 controls starts/restarts/next/title/difficulty/shared pause. P2 follows automatically, waits for both ready and joins beside P1 with a one-time weapons/ammo copy. Three-second individual respawn at normal map spawn retains gear, restores health, removes armor/powerups and leaves the world intact. Saves are blocked; separate MP saves and shared combat/world snapshots follow later.

## Native loading evidence

Recovered C# under `<Decompiled>/CSharp/Assembly-CSharp` ([path placeholders](../README.md#path-placeholders)) contains declarations with placeholder bodies. Behavior was checked in matching `Cpp2IL/Native-ISIL/IsilDump/Assembly-CSharp` dumps and then in the installed runtime.

- `GameManager.LoadLevel(LevelId,bool)` starts a native scene-cluster load; `loaded=true` skips its opening cutscene. Use this flow rather than raw Unity scene loading.
- `GameManager.LoadNextLevel` selects the next level and opens NextLevelMenu. It does **not** call LoadLevel. The later `GameManager.OpenLoading` path needs a host announcement too.
- `GameManager.OpenTitle` ends SessionManager and loads title, but CurrentLevel stays remembered. Actual initialized LevelController and SessionActive determine map/player state.
- `GameManager.Load(null)` resets the current world without necessarily changing scene/ID. Each restart needs a new generation; stale ready messages must not release it.
- LoadingMenu's `_sceneLoaded` gates its native continuation. Level start then installs another InputState.NONE/any-button gate. OnStartLevelReady fires after that second continuation. Call each handler once when its gate exists.
- GameManager.EnableMenu pushes MENU, pauses application time and sound buses; DisableMenu reverses that. Application scaling preserves separate game/meta slowdown scales. P2's local menu owns an input mode and must not release P1's shared pause.
- Runtime difficulty setter is `SettingsManager.GameplaySettings.SetDifficulty`; transmit a validated registered index and restore P2's original runtime difficulty during cleanup.

## Native respawn and inventory evidence

- LevelController.OnPlayerDeathStart invokes EndLevel(DEATH). Suppress that world-ending transition while retaining native player death callbacks. Guard the DEATH end path as well.
- Player.Respawn restores native health/head/collider/look/HUD and sets armor zero. It does not teleport; native respawn resets inventory to starting items.
- PlayerInventoryComponent.SaveData creates a fresh GUID/quantity dictionary of **all** held items plus current/last weapon and selected usable. Inventory-only LoadState clears/re-adds those entries. In-memory death snapshots avoid file saving.
- Joining imports WeaponId and ItemId.IsAmmo entries only, merged with guest non-ammo defaults. Validate GUID/category/count/equipped references before native LoadState.
- Player.Teleport uses movement Warp and Physics.SyncTransforms, but Warp neither collision-checks a destination nor clears all special movement modes. Revival applies clean local movement data and zero velocity.
- Find the authored PlayerSpawnPoint associated with this player for normal death spawn, including when P1 hosts mid-level. The current player position would be the wrong respawn point.
- Nearby joining searches grounded, clear capsule space around P1's feet, otherwise leaves the normal spawn and explains why. It checks local geometry, not shared doors, hazards or enemies.

Native ISIL references: `Player.txt:4525` revival; `Player.txt:4807` teleport; `PlayerComponents/PlayerHitProcessorComponent.txt:4564` health/armor; `PlayerComponents/PlayerInventoryComponent_NestedType_SaveData.txt:373` snapshot; `PlayerComponents/PlayerInventoryComponent.txt:10396` inventory load; `PlayerComponents/PlayerMovementComponent.txt:4203` warp and `:14117` load.

## Save isolation and future world sync

SaveController.Save eventually writes ProfileManager, even when called just for a snapshot. Block native save/auto/quick entrypoints, campaign save/load UI, progression updates and final profile writers. Clear solo CurrentSaveData before MP loading so native startup cannot apply stale solo state. Preserve original level metadata, difficulty and save reference for offline cleanup; guest guards remain until native title unload finishes.

Save dictionaries contain useful future snapshot ingredients, but runtime SaveController.BehaviourToSave and NpcController.SpawnPointToSave constructors use Guid.NewGuid. Arbitrary host dictionary entries cannot reliably map to client runtime entities. Shared restoration needs host-assigned entity IDs, NPC/door/pickup lifecycle mapping and an in-memory snapshot system.

See [verification.md](verification.md) for runtime evidence and remaining manual checks. This milestone loads the map; it does not reproduce P1's progressed world.
