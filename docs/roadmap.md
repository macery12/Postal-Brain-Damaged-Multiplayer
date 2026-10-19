# Roadmap and session rules

Current development source: **PBDMP 0.10.0 / protocol 13**; the last accepted baseline is 0.9.0. The megapass implementation closeout hands broad campaign acceptance to the [maintainer playtest checklist](megapass-playtest.md). The [sync tracker](sync-progress.md) is the detailed feature/status reference; [verification](verification.md) separates implemented behavior from acceptance evidence.

Survival co-op for the three base arenas is installed as **41D7421713392F3B** (see the [Survival handoff](../survival.md)). The focused [weapon-animation pass](weapon-animation-testing.md), previously installed as **C990AE9ED22FC33F**, remains included: improved grips/aim/action poses and dual-wield presentation, with repeatable multi-angle captures and connected regression checks.

## Current behavior

- Two-player sessions through a friends-only or invite-only Steam lobby (overlay invites, Join Game while running, Steam relay), or direct IP for LAN/forwarded ports.
- Native-themed lobby panel with player cards, Direct IP dialog, prompts, session badge and notifications; main/pause-menu entries and F8 access. Joining from inside a solo level asks first.
- P1 owns base-campaign level starts, restarts, difficulty, transitions and shared pause. P2 follows and cannot independently start/load/restart levels.
- P1 owns enemy AI/health, shared combat results, supported doors, encounters, urine interactions and mission progression. Either player can interact or fight; enemies can target either player. Friendly fire is off.
- P2 joins near P1 when safe, falling back to normal map spawn. Each join/rejoin and co-op restore copies P1's weapons, remaining ammo and equipped weapon; P2 receives full health, zero armor and no temporary powerups. Non-ammo items and pickup history remain personal.
- Supported current actor/projectile/door/mission state is restored before a joining player is released. Arbitrary world scripts and complete world restoration remain unfinished.
- Independent respawn after three seconds at the map spawn: full health, zero armor, retained weapons/remaining ammo/items, cleared temporary powerups. Shared pause stops the timer.
- Walk-in exits gather both living players; leaving cancels readiness. Interactable endings preserve native interaction and require both nearby. Automatic endings wait until both are alive and loaded.
- P2 disconnecting lets P1 continue. P1 departing ends the session and returns P2 to the main menu; no host migration.

## Identical and personal systems

In the campaign, weapons, keys and mission items are one team inventory owned by P1. A pickup of any of them exists once, and both players receive it. A key door uses the key for both. See [the redesign plan](../coop-redesign.md).

Health, armor/shields, ammo, consumables and powerup types/durations remain personal. Collecting one of those pickups does not remove the partner's copy, and host-selected enemy drops receive separate collectible copies. Survival keeps every resource personal. Personal systems do not count as unfinished synchronization.

## Remaining priorities

| Area | Next work | Acceptance boundary |
| --- | --- | --- |
| FPS and frame pacing | Profile capture, allocations/GC, rendering and active-world spikes from the maintainer's playtest. | Major next-pass priority; compare before/after under retained budgets. |
| Network performance | Profile snapshot size, bandwidth, queue age, combat traffic and LAN/Steam delivery under load. | Functional synchronization is separate from responsiveness; measure before transport changes. |
| Mission/world coverage | Classify remaining callbacks, specialised machinery and timeline tracks. | Preserve host authority; do not replay arbitrary client callbacks. |
| Scenery acceptance | Gameplay fragments and natural destruction/prop routes. | World/scenery adapters and focused rollback/rejoin checks are implemented; remaining cases are in the playtest checklist. |
| Player presentation | Weapon grip, reload/jump/death transitions and visual polish. | Keep cosmetic animation separate from gameplay authority. |
| Checkpoint coverage | Exercise additional natural campaign checkpoint and machinery sequences. | Native world restoration plus durable multiplayer adapters are implemented. |
| Solo-save import | Explicit conversion into a new co-op slot. | Co-op persistence is implemented; solo slots are never implicitly imported. |
| Transport evaluation | Measure real LAN and Steam-relay latency, loss and snapshot pressure; consider unreliable pose messages on Steam. | Retain bounded queues and main-thread capture budgets. |
| Steam joining | Joining from a closed game (`+connect_lobby`) and an in-game friends list. Two-account joining, rejoining and the build-mismatch check passed in the October 18, 2026 playtest. | Both players keep the strict exact-build check. |

Host-owned co-op saves now provide named manual slots, one quicksave and three rotating autosaves. Either player can activate a supported checkpoint; only P1 can restore it. Ordinary deaths still respawn independently. Native solo save/profile writes remain blocked during multiplayer and return after session cleanup. See [co-op saves](co-op-saves.md).

## Outside the current scope

Survival co-op for the three base arenas is implemented (see the [Survival handoff](../survival.md)); its natural two-PC playtest list is in [verification](verification.md#survival-two-player-checklist). DLC/custom Survival arenas, persistent Survival saves, custom maps, DLC-specific content, more than two players, public matchmaking/lobby browsing, dedicated servers and host migration need separate design and validation. Steam sessions use Steam's relay and NAT traversal; Direct IP sessions remain unauthenticated and unencrypted and should not be exposed to untrusted networks.

## Historical milestones

| Version | Development milestone |
| --- | --- |
| 0.1 | LAN movement, primitive avatars, session guards and cleanup. |
| 0.2–0.3 | Native Dude/weapon presentation, initial animation and native muzzle effects. |
| 0.4 | Native-themed multiplayer UI. |
| 0.5 | Host campaign lifecycle, joining and independent respawn. |
| 0.6-era passes | Shared combat/actors/doors and urine interactions. |
| 0.7.0 | Expanded mission state, required destruction, exit gathering and large-snapshot backpressure. |
| 0.8.0 | Steam lobby hosting/joining over Steam relay; native lobby panel, player cards, prompts and in-game badge. |
| 0.9.0 | Separate co-op saves, host-controlled checkpoint rollback, personal pickup persistence and partner identity/catch-up. |

These are development milestones, not a list of published downloads. Older research notes describe their original scope and should not override current behavior.
