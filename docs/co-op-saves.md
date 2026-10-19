# Co-op saves and checkpoints

PBDMP stores multiplayer progress separately from solo saves. P1 owns the files and controls saving/loading. Open **Multiplayer → Co-op saves...** from the main menu or pause menu, or through F8. Host a lobby before loading a saved campaign; P1 can resume alone and invite the partner later.

## Controls

| Control | Behavior |
| --- | --- |
| New slot / Save new slot | Enter a name and create a manual save. |
| Overwrite slot | Select a manual slot; replacement requires confirmation. |
| Quicksave | Replaces the dedicated quicksave slot; the native quicksave action also routes here. |
| Quickload | P1 loads the co-op quicksave rather than a solo slot. |
| Autosave | Three slots rotate every five minutes of active gameplay by default. Checkpoint/interest volumes do not force captures. Pause, loading, story sequences and save transactions do not advance the timer. |
| Restore checkpoint | P1 restores the newest valid autosave for both players and the world. |
| Load selected | Restores the selected slot after confirmation. |

Set `[Saves] AutosaveMinutes` in `BepInEx/config/pbdmp.multiplayer.cfg` to an integer from **5 to 10**, then restart the game/session. Loading a level or save, or completing a manual/quicksave, starts a fresh interval. There is no immediate autosave simply for entering a map or remaining inside an interest area.

Dying continues to use independent respawns. It never automatically rewinds the shared world. P2 cannot save, load or restore checkpoints.

## What a save records

The game's authored save serializers capture the host world and campaign data. Multiplayer adapters supplement enemy/spawn/death records, encounters and boss state, doors, mission triggers, required destruction, urine puzzles, machinery and generated loot. Restoring reloads the map, restores native state and applies durable multiplayer records before either player is released.

Weapons, keys and mission items are identical for both players: P1's inventory holds them for the team, and their pickups exist once. Ammo, health, armor and consumable pickups and their history remain personal, including authored resource pickups and separate copies of enemy drops. Posters and their level-result count are shared; their authored records persist independently of personal resource histories. A host-collected pickup does not disappear for P2. Completed damage and destruction persist; in-flight attacks, explosion sounds and other transient effects are cleared rather than replayed.

| Player state | P1 | P2 |
| --- | --- | --- |
| Weapons, keys and mission items | Restored from the save; this is the team inventory. | Always identical to P1, including on join/rejoin and restore. |
| Remaining ammo and equipped weapon | Restored from the save. | Copy of P1 on every join/rejoin and save/checkpoint restore. Independent during subsequent play. |
| Health and armor | Saved values restored. | Full health, zero armor on join/rejoin and restore. |
| Temporary powerups | Saved remaining effects restored. | Cleared on join/rejoin and restore. |
| Keys, non-ammo items and pickup history | Personal saved record. | Personal saved record for the current map, independent of P1. |
| Position and view | Saved position/view, with safe-position fallback. | Saved position/view on restore; normal joins spawn beside P1 with map-spawn fallback. |

If P1 advances while P2 is absent, the partner's record is retained. A return on another map uses that map's normal starting non-ammo state rather than stale positions/keys/pickup history, then catches up to P1's current weapons/ammo.

## Partner identity

Steam sessions recognize the connected Steam account. Direct IP sessions use a locally generated persistent player ID in `BepInEx/PBDMP/player-id`; keep that file when moving your installation if you want to retain the same identity. Names and IP addresses do not determine ownership. Direct IP identity is a continuity mechanism, not authentication.

Anyone can join a co-op game, with no assignment prompt. A saved personal record is used only by the player it belongs to. A different player joins fresh: the team's weapons, keys and items, P1's remaining ammo, full health and zero armor, and untouched personal pickups. Resuming without P2 keeps the absent partner's record.

## Capture, loading and recovery

Unsafe saves show **Save pending** until players are alive, loading/respawn has finished and an active story sequence has ended. Capture briefly holds both games while copying a consistent world and personal records. Both players see **Autosaving…** or **Saving…** during capture and **Loading…** during map/world restoration. The overlay clears when its hold is released; background disk writing does not keep gameplay frozen. Capture/restoration yields between native entries; disk serialization and writes run in the background. A single native serializer can still take longer than the frame budget.

Capture retains live state delivery: durable records copied into the save also continue through the bounded replication queue. This prevents a change first observed by save capture from being suppressed by the sender's delta cache and remaining absent on P2. Historical transient effects are still excluded from the capture path.

Files live under `BepInEx/PBDMP/saves/` in P1's installation. Each slot uses a `.pbdmp` file with a schema version, game/catalogue compatibility data and SHA-256 checksum. Writes flush a temporary file and atomically replace the slot; `.bak` retains its previous valid generation. Interrupted temporary files are ignored. If the primary is damaged, loading tries the valid backup and reports recovery. A catalogue-incompatible primary can also recover the previous backup only when its schema, game build, map and catalogue all match. This loads that older recovery point, reports the choice, and leaves the primary and backup unchanged. If no compatible backup exists, loading refuses instead of inventing missing state. Resaving after recovery does not promote the incompatible same-map primary over the usable backup. Keep backups before manually moving or deleting save files.

Unsupported schema, mismatching game/content or damaged data produces an error. A failed save keeps the previous file. A failure after partial world restoration ends that restoration instead of releasing an inconsistent world. Both connected players must finish map, world and personal restoration before play resumes.

Solo save/profile writes and leaderboard uploads remain isolated during multiplayer; disconnect restores normal offline behavior. Solo-save import is a future explicit conversion feature. The [sync checklist](sync-progress.md) distinguishes supported world adapters from still-unclassified scripts and decorative scenery; persistent native restoration does not imply live synchronization for every decorative object.


## Development world-format expansion

The 0.10 development source uses save schema 2 and wire protocol 10. Its [world megapass](../megapass.md) is still undergoing acceptance. Typed records add remaining machinery delays/motion, partial scenery damage, physical prop lifetimes and motion, current arena ordering, native cooldowns and mechanical timeline phases. Personal player records also retain an optional authored platform reference and relative feet position. Restoration uses the restored platform pose before safe-position validation and rebuilding rider contacts. Personal pickups and non-ammo resources retain the rules above.

Joining captures the host's current world, including changes made since loading a save. Baselines up to the disk limit stream through bounded chunks; incomplete restoration never releases gameplay. Saving also waits for a native prop-spawn batch or unresolved item reservation to finish.

Older 0.9 schema-1 saves do not contain enough state for a reliable expanded-world restoration. The development loader clearly refuses those saves and preserves their originals/backups rather than silently resetting missing machinery or scenery. A future migration requires sufficient source state; there is no automatic destructive conversion.

## World and collectible consistency

Authored physical props are recorded when the scene loads. Detachable NPC corpse pieces and runtime spawner objects do not become permanent catalogue members merely because they later sit at scene roots. Spawned gameplay props retain their separate lifetime records. This keeps restored/rehosted map identities stable without dropping legitimate unsaved swing props.

Poster collection is host-owned and counted once. Current-world joining and save restoration restore the poster and count without replaying its old sound/notification. Earlier schema-2 saves can recover shared poster progress from the union of recorded authored poster pickup GUIDs. Other resource pickups stay personal.
