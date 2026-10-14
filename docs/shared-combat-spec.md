# Shared combat and doors: accepted design

Design established October 12, 2026. The 0.7.0 development build implements the supported combat/door paths. This document records ownership and acceptance requirements, not proof of complete campaign validation. Current per-feature implementation and remaining gaps are in [sync progress](sync-progress.md) and [verification](verification.md).

## Scope and ownership

Two-player LAN, built-in base campaign. P1 owns enemy AI, enemy health, spawning, projectiles, hit results, kills, encounters/boss phases and combat-driven progression. P2 sends actions; host results drive replicas. Preserve native enemy difficulty, health and spawn counts; do not introduce automatic co-op scaling. Enemies must be able to detect and attack either living player. Either player entering an encounter trigger can request host activation; authored combat progress executes once on P1.

Player health and armor/shields, weapon/ammo/item inventories, keys and powerup pickups remain personal. P1 delivers incoming-hit events to the victim; that player's game applies native health/armor and relevant defense behavior locally. Continuous player health/armor values do not become a shared resource. Living/dead/life state remains synchronized. Friendly fire stays off, and remote presentation never applies damage a second time.

Retain the existing one-time host weapons/ammo copy on joining, independent inventories afterward, nearby join spawn with normal-spawn fallback, three-second independent respawn with retained gear and cleared temporary powerups, P1-controlled pause/travel and session departure behavior. Campaign save/load/quick/autosave remain blocked; persistent co-op saves/checkpoints are future work.

## Weapons and specials

Cover registered base-campaign primary/secondary firing, fast bullets, pellets/trails, projectiles, charged shots, continuous beams, impact/explosion effects and native positional firing/impact/explosion audio. Include melee swings/kicks, charge/block/shield states, hook/chain/grapple behavior and reflection under the shared-combat rules. Projectile ownership after reflection, enemy-facing special outcomes and resulting player motion must remain consistent with host combat authority and personal player resources.

Use original local game assets, not generic replacement flashes. Replica effects and looping audio clean up on impact/despawn, travel, death where relevant and disconnect. Shared pause/loading barriers freeze combat; old epochs/lifetimes cannot resurrect projectiles or replay shots. Keep local firing feedback responsive while preventing local speculative attacks from independently killing replica enemies or advancing progress.

## Doors and connected progression

P1 owns ordinary hinged/double/sliding/elevator-panel/vault/physics toilet doors and identified door/gate movers: opening/closing, motion, collision, locks and current state. Either player can interact or activate proximity doors; include switches directly controlling them. Closing and obstruction checks must account for both players. Enemy/script-driven changes follow P1.

Check the requester's own keys/items and consume only theirs when the original game requires it. A committed unlock applies to both players; P2 does not need a duplicate key for an already-unlocked shared door. Local item collection never removes the other player's pickup.

Moving elevator platforms, TNT/destructible gates, the damaging cat-paw trap and unrelated puzzle machinery/hazards remain outside this pass. Include combat-driven waves, boss phases, death-triggered doors and encounter switches needed for the agreed shared encounters; this does not imply synchronization of every world object.

## Loot and joining an active fight

P1 selects enemy loot and its spawn location through native loot behavior. Replicate the selected spawn into both games; each player can collect their own copy independently. Native loot is adaptive/random and tied to the local player, so identical host-selected drops follow P1's health/ammo-based selection, not a separate P2 adaptive roll. This was the chosen identical-drop policy; no changes to native pickup quantities are proposed.

A newly joining player receives personal copies of existing runtime loot, even if P1 collected theirs. Local collection state persists through that player's independent respawn; map restart resets the shared encounter. Persistent reconnect/save entitlements are outside the current save scope.

Before releasing P2's existing loading barrier, install the current authoritative enemy state (identity, spawn/alive/dead state, health, transforms and required status/presentation), encounter/boss/progression state, active projectile state and included door state. Restore current state without replaying historical shots, kills, explosions or audio. Host load/readiness failure behavior still releases P1 and returns P2 to title with an explanation.

## Implementation boundaries

All Unity/native calls remain on the main thread. Use verified authored-object manifests plus host-assigned runtime entity identities and epoch/lifetime/revision checks. Native save GUIDs, instance IDs and pointers are not network identities. Bounded state/event traffic must distinguish reliable one-time combat/lifecycle events from coalesced position snapshots; the current pose-only shot counter is insufficient.

P2 needs host-side targeting/collision support without cloning camera/input/local movement behavior. Client NPC replicas suppress AI, attack/spawn authority, root motion, loot and campaign callbacks while retaining appropriate rendering, animation and aiming collision. Incoming damage must preserve native defense behavior; scalar health assignment would omit reflection/blocking/armor semantics. Do not replay native client kills or historical designer events to reconstruct state.

Disconnect/return-to-menu cleanup must release host proxies, replica authority, audio/effects and suppression hooks. Restore normal offline native behavior through a safe session cleanup/reload flow. Preserve existing solo save protection throughout. Add a map/component coverage report rather than claiming unsupported authored gates or boss adapters are verified.

## Evidence required for completion

- Network acceptance: role authority, bounded queues/frames/snapshots, epoch and pooled-lifetime identity, duplicate/stale events, reliable shot/kill/door delivery, manifest mismatch and reconnect cleanup.
- Two actual game instances: P1 and P2 shots/melee/specials damage the same enemy once; enemies can target and damage either player; personal health/armor/ammo and pickups remain separate; one shared kill/loot/progression outcome.
- Native projectile/effect/audio coverage: primary/secondary/charged/beam cases, reflection/ownership, impact/despawn, rapid fire, pause and disconnect; no accidental damage from replica effects.
- Doors: either-player proximity/interaction, requester key use, shared unlock/close/collision, physics doors, directly connected switches, late join and restart restoration, deterministic registry matching.
- Encounters: authored and runtime enemy spawns, waves, representative boss phases/progression, P2-triggered activation and late join during combat without historical-event replay.
- Regression: campaign travel, respawn, menus/input, pause, save isolation and session cleanup. Document remaining physical two-PC/input and campaign-map coverage honestly in verification.

Research: [gun/door findings](gun-door-research.md), [enemy/combat findings](enemy-combat-research.md). Progress: [sync-progress.md](sync-progress.md). Update progress only after the relevant behavior and evidence exist.
