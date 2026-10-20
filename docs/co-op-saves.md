# Co-op saves and checkpoints

PBDMP stores multiplayer progress separately from solo saves. P1 owns the files and controls saving and loading; P2 cannot save, load or restore checkpoints. The screens are copies of the game's own Save and Load menus, with its save entries and thumbnails.

## Controls

| Where | Control | Behavior |
| --- | --- | --- |
| Multiplayer menu | Continue co-op | Hosts and loads the newest save of the most recently played co-op game. |
| Multiplayer menu | New co-op game | The game's level select, *Load this map as a multiplayer game?*, difficulty, then the map loads. Starts a new co-op game (campaign). |
| Multiplayer menu | Load co-op game | Co-op games on the left (map, difficulty, players, last played) and the selected game's saves on the right. **Load**, **Delete** (the focused save, or the whole co-op game) and **Back**, each with the game's confirmation prompt. |
| Pause menu (P1) | Save | **New save** (with a live thumbnail) asks for a name; selecting an existing manual save overwrites it after confirmation. Saves can be deleted here. |
| Pause menu (P1) | Load | Load co-op game on the current co-op game, plus **Load latest autosave**, which restores the newest valid autosave for both players and the world. Loading from inside a level asks first, because unsaved progress is lost. |
| In game (P1) | Quicksave / Quickload | The game's own quicksave and quickload act on the co-op game's quicksave. |
| Automatic | Autosave | Three slots per co-op game rotate every five minutes of active gameplay by default. Checkpoint/interest volumes do not force captures. Pause, loading, story sequences and save transactions do not advance the timer. |

P1 can resume alone and be joined later. P2's pause Save and Load are greyed out, and P2's Multiplayer menu shows a read-only *Playing with [host]* view.

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
| Ammo, health, armor and powerup pickup history | Personal saved record. | Personal saved record for the current map, independent of P1. |
| Position and view | Saved position/view, with safe-position fallback. | Saved position/view on restore; normal joins spawn beside P1 with map-spawn fallback. |

If P1 advances while P2 is absent, the partner's record is retained. A return on another map uses that map's normal starting non-ammo state rather than stale positions/keys/pickup history, then catches up to P1's current weapons/ammo.

## Partner identity

Steam sessions recognize the connected Steam account. Direct IP sessions use a locally generated persistent player ID in `BepInEx/PBDMP/player-id`; keep that file when moving your installation if you want to retain the same identity. Names and IP addresses do not determine ownership. Direct IP identity is a continuity mechanism, not authentication.

Anyone can join a co-op game, with no assignment prompt. A saved personal record is used only by the player it belongs to. A different player joins fresh: the team's weapons, keys and items, P1's remaining ammo, full health and zero armor, and untouched personal pickups. Resuming without P2 keeps the absent partner's record.

## Capture, loading and recovery

Unsafe saves show **Save pending** until players are alive, loading/respawn has finished and an active story sequence has ended. Capture briefly holds both games while copying a consistent world and personal records. Both players see **Autosaving…** or **Saving…** during capture and **Loading…** during map/world restoration. The overlay clears when its hold is released; background disk writing does not keep gameplay frozen. Capture/restoration yields between native entries; disk serialization and writes run in the background. A single native serializer can still take longer than the frame budget.

Capture retains live state delivery: durable records copied into the save also continue through the bounded replication queue. This prevents a change first observed by save capture from being suppressed by the sender's delta cache and remaining absent on P2. Historical transient effects are still excluded from the capture path.

Saves are grouped into **co-op campaigns**. A campaign is one continuous run of the story, stored as one folder `BepInEx/PBDMP/saves/<campaign id>/` in P1's installation. Each campaign has its own manual saves, one quicksave and three rotating autosaves. Only a new co-op game starts another campaign; loading a save continues the campaign it belongs to. Each save also writes:
- a small `.info` sidecar (name, level, time, difficulty and the names of everyone who has played), so listings never parse whole worlds;
- a `.png` thumbnail rendered from P1's camera at the game's own 160×90 size.

Save schema 3 stores one personal record per player who has joined. Saves from older development builds (schemas 1 and 2) are refused with a clear message and left untouched.

Each slot uses a `.pbdmp` file with a schema version, game/catalogue compatibility data and SHA-256 checksum. Writes flush a temporary file and atomically replace the slot; `.bak` retains its previous valid generation. Interrupted temporary files are ignored. If the primary is damaged, loading tries the valid backup and reports recovery. A catalogue-incompatible primary can also recover the previous backup only when its schema, game build, map and catalogue all match. This loads that older recovery point, reports the choice, and leaves the primary and backup unchanged. If no compatible backup exists, loading refuses instead of inventing missing state. Resaving after recovery does not promote the incompatible same-map primary over the usable backup. Keep backups before manually moving or deleting save files.

Unsupported schema, mismatching game/content or damaged data produces an error. A failed save keeps the previous file. A failure after partial world restoration ends that restoration instead of releasing an inconsistent world. Both connected players must finish map, world and personal restoration before play resumes.

Solo save/profile writes and leaderboard uploads remain isolated during multiplayer; disconnect restores normal offline behavior. Solo-save import is a future explicit conversion feature. The [sync checklist](sync-progress.md) distinguishes supported world adapters from still-unclassified scripts and decorative scenery; persistent native restoration does not imply live synchronization for every decorative object.


## World format

Save schema 3 builds on the [world megapass](plans/megapass.md) records. Typed records add remaining machinery delays/motion, partial scenery damage, physical prop lifetimes and motion, current arena ordering, native cooldowns and mechanical timeline phases. Personal player records also retain an optional authored platform reference and relative feet position. Restoration uses the restored platform pose before safe-position validation and rebuilding rider contacts. Personal pickups and non-ammo resources retain the rules above.

Joining captures the host's current world, including changes made since loading a save. Baselines up to the disk limit stream through bounded chunks; incomplete restoration never releases gameplay. Saving also waits for a native prop-spawn batch or unresolved item reservation to finish.

Older schema-1 and schema-2 saves are refused with a clear message, and their originals and backups are preserved. There is no automatic conversion.

## World and collectible consistency

Authored physical props are recorded when the scene loads. Detachable NPC corpse pieces and runtime spawner objects do not become permanent catalogue members merely because they later sit at scene roots. Spawned gameplay props retain their separate lifetime records. This keeps restored/rehosted map identities stable without dropping legitimate unsaved swing props.

Poster collection is host-owned and counted once. Current-world joining and save restoration restore the poster and count without replaying its old sound/notification. Earlier schema-2 saves can recover shared poster progress from the union of recorded authored poster pickup GUIDs. Other resource pickups stay personal.
