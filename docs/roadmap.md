# Roadmap and session rules

Current development baseline: **PBDMP 0.7.0 / protocol 7**. The [sync tracker](sync-progress.md) is the detailed feature/status reference; [verification](verification.md) separates implemented behavior from acceptance evidence.

## Current behavior

- Two-player direct-IP LAN sessions with native-themed main/pause-menu controls and F8 access.
- P1 owns base-campaign level starts, restarts, difficulty, transitions and shared pause. P2 follows and cannot independently start/load/restart levels.
- P1 owns enemy AI/health, shared combat results, supported doors, encounters, urine interactions and mission progression. Either player can interact or fight; enemies can target either player. Friendly fire is off.
- P2 joins near P1 when safe, falling back to normal map spawn. Joining copies P1's weapons/ammo once, without copying health, armor, keys or powerups.
- Supported current actor/projectile/door/mission state is restored before a joining player is released. Arbitrary world scripts and complete world restoration remain unfinished.
- Independent respawn after three seconds at the map spawn: full health, zero armor, retained weapons/remaining ammo/items, cleared temporary powerups. Shared pause stops the timer.
- Walk-in exits gather both living players; leaving cancels readiness. Interactable endings preserve native interaction and require both nearby. Automatic endings wait until both are alive and loaded.
- P2 disconnecting lets P1 continue. P1 departing ends the session and returns P2 to the main menu; no host migration.

## Intentionally personal systems

Health, armor/shields, ammo, weapons, keys, item counts, pickups and powerup types/durations remain personal. Collecting a pickup does not remove the partner's copy. Host-selected enemy drops receive separate collectible copies.

Keys/items are checked and consumed on the interacting player when native rules require them. An accepted unlock opens shared progression for both players. This preserves resource balance without requiring duplicate keys at a shared door. Personal systems do not count as unfinished synchronization.

## Remaining priorities

| Area | Next work | Acceptance boundary |
| --- | --- | --- |
| Natural campaign validation | Two-PC progression through puzzles, encounters and bosses. | Catalogue discovery alone is insufficient. |
| Mission/world coverage | Classify remaining callbacks, specialised machinery and timeline tracks. | Preserve host authority; do not replay arbitrary client callbacks. |
| Destructible scenery | General barrels/props and other gameplay-relevant destruction. | Required mission wall destruction already has an adapter. |
| Player presentation | Weapon grip, reload/jump/death transitions and visual polish. | Keep cosmetic animation separate from gameplay authority. |
| Checkpoints | Complete shared world capture/restoration. | Restore both players and world state consistently. |
| Persistent co-op saves | Separate host-owned multiplayer saves and explicit solo-save import. | Never implicitly overwrite solo profiles. |
| Transport evaluation | Measure real LAN latency, loss and snapshot pressure. | Retain bounded queues and main-thread capture budgets. |

Campaign save/load, quicksave and autosave stay disabled during multiplayer until persistent restoration is implemented. Offline saving returns after session cleanup.

## Outside the current scope

Survival, custom maps, DLC-specific content, more than two players, matchmaking, relays, NAT traversal, dedicated servers and host migration need separate design and validation. Internet-facing use would also require authentication/encryption work.

## Historical milestones

| Version | Development milestone |
| --- | --- |
| 0.1 | LAN movement, primitive avatars, session guards and cleanup. |
| 0.2–0.3 | Native Dude/weapon presentation, initial animation and native muzzle effects. |
| 0.4 | Native-themed multiplayer UI. |
| 0.5 | Host campaign lifecycle, joining and independent respawn. |
| 0.6-era passes | Shared combat/actors/doors and urine interactions. |
| 0.7.0 | Expanded mission state, required destruction, exit gathering and large-snapshot backpressure. |

These are development milestones, not a list of published downloads. Older research notes describe their original scope and should not override current behavior.
