# Campaign mission trigger synchronization plan

October 13, 2026. Accepted mission design and implementation record. Current development build: **PBDMP 0.8.0 / protocol 7**. Feature coverage and acceptance limits are recorded in [sync progress](sync-progress.md).

## Reported blocker

Before the fix, P2 activating Neighborhood's electrical box produced an explosion and removed the box, but left the house wall intact on P2 while P1's route opened. The regression confirmed both a renderer and collision mismatch.

The native regression reproduced `OBJECTIVES/BreakableWall` remaining destroyed=false, blocking=true and visible=true on P2 while P1 had destroyed=true, blocking=false and visible=false. The wall was outside the urine consequence graph: the host explosion damaged it through the explosion area, while P2 received cosmetic effects. A dedicated persistent destruction adapter now restores the solid geometry and occlusion without invoking native Break/Load callbacks or explosion damage. Tests independently check the wall and pass both player controllers through a route proven traversable on P1, including after rejoining.

## Settled design

- Cover built-in campaign mission triggers and their consequences: progression-required destruction, switches, barriers, scripted gates/platforms, hazard activation, encounter activation and exits.
- Either living player may activate mission triggers. P1 validates requests and commits shared results once; client presentation cannot reapply damage or competing progression callbacks.
- **Level exits require both players**, using the confirmed gathering and automatic-ending rules below.
- Keep health, armor, inventories, pickups and personal powerups under the existing local-resource policy. Existing shared key-unlock and combat rules continue to apply.
- Restore completed mission state on late join before releasing gameplay. Do not replay old explosions, sounds, rewards, damage or spawn waves.
- General decorative breakables, cosmetic debris, persistent saves and checkpoint restoration remain separate work. Include scenery destruction where it is part of a mission progression chain.

## Confirmed exit rules

Either player can reach an authored exit first. Show a waiting-for-partner message, but do not begin the level transition until both living players occupy the mapped exit area. A player leaving that area withdraws readiness. A dead player respawns under the existing rules and must reach the exit again; do not silently teleport them there. If P2 disconnects, the remaining host returns to ordinary solo exit behavior. Reconnecting players must complete loading before they can count as ready.

Automatic story endings, including boss-death or scripted-sequence endings without a walk-in exit, wait until both players are alive and finished loading, then transition together. They do not require an exit area that the map does not have. Preserve the completed ending while waiting so it does not need to be triggered again. If P2 disconnects, the remaining host can continue alone. Required puzzle conditions remain intact. Personal tutorials, HUD notices and ambient music remain local; classify optional secrets, dialogue and checkpoint callbacks rather than indiscriminately replaying them across peers.

## Implementation approach

1. **Make the wall failure reproducible.** Start with fresh Neighborhood on two matching builds. Identify the actual obstruction and its controlling objects/components on both peers. Record renderer, collider, active state and route passability before and after P2's action. Repeat from P1. Establish a failing check before changing synchronization.
2. **Audit campaign trigger graphs.** Inventory all built-in campaign maps, including inactive and externally referenced objects. Record trigger sources, condition gates, downstream targets and available state adapters. Separate mission actions, presentation, personal UI/resources, and unsupported callbacks. Include explosion-driven consequences outside explicit UnityEvents.
3. **Add bounded persistent state adapters.** Cover activation/condition state, required destruction, object visibility and collision, switch/barrier state and linked machinery pose/motion. Use stable identities and versioned, validated state. Avoid native load/callback paths that restart damage, spawn waves, rewards or destruction sequences. A missing target must produce actionable diagnostics rather than silent success.
4. **Extend interaction authority.** Route supported P2 trigger activations to P1 with life, scene and proximity/condition checks. Respect one-shot versus repeatable triggers and native puzzle requirements. Account for simultaneous activation, death, disconnect and level reload. Apply shared hazard hit results through the existing personal-health authority path.
5. **Implement exit gathering.** Integrate agreed readiness with host-controlled travel, native trigger conditions and loading barriers. Prevent P2's native exit callbacks from starting independent travel.
6. **Restore mission state during joining.** Include changed/destroyed objects, completed trigger conditions and linked machinery in the supported world snapshot. Apply in dependency order; completed sequences must remain completed without replaying transient events. Clear readiness and stale state on scene/life/session changes as appropriate.
7. **Validate and document individual mechanics.** Run focused native fixtures, representative natural progression and campaign-wide discovery audits. Preserve the existing frame-budget capture approach and measure host performance. Update `sync-progress.md` per mechanic with actual coverage, evidence and remaining limits; discovery alone is not gameplay acceptance.

Native families to investigate include `Trigger`/`TriggerListener`, `PlayerTrigger`/`PlayerTriggerListener`, state and explosion listeners, `GenericStateable`/`GenericInteractableStateable`, `Toggler`/`ScriptToggler`/`ActiveStateSaveable`, `BreakableObject`/`CollisionBreakableObject`/`Breaker`, movers/rotators and sequence/timeline controllers. This is an audit list, not a promise to blindly execute every family on both peers.

## Acceptance checks

| Scenario | Required evidence |
| --- | --- |
| P2 activates Neighborhood fuse | Both peers lose the actual obstructing wall renderer and collider; ordinary player movement passes through the opened route. Box disappearance alone is insufficient. |
| P1 activates the same fuse | Same persistent result on both peers. |
| Duplicate or simultaneous activation | No duplicate damage, progression, waves or transition; unrelated walls and personal pickups stay correct. |
| Late join after destruction | Route already open; no historical explosion, damage or reward replay. |
| Level restart/reload | Fresh authored state returns, with no stale target identities or readiness. |
| Trigger activated by either player | P1 applies native conditions and both receive downstream world state. |
| Repeatable switches/machinery | State and collision agree through reversal and movement; riding/attachment requires appropriate native validation. |
| Shared hazard activation | Both see the hazard state; only the correct victim receives each authoritative hit. |
| Exit gathering | One player cannot transition alone; both-player readiness, departure from the area, death/respawn and disconnect follow the agreed rule. |
| Automatic story ending | Completed ending waits for both players to be alive and loaded, then transitions once without requiring a walk-in exit; disconnect releases the remaining host. |
| Performance | Report world capture and frame timing against the current build; avoid adding unbounded periodic main-thread scans. |

Campaign-wide inventory and controlled fixtures must be distinguished from natural mission completion and two-PC LAN acceptance. Do not mark the general scripted-world milestone complete while known progression blockers remain.

## Handoff

The current implementation includes the supported adapters. Mission graph/state adapters, trigger occupancy, native walk-in and interactable endings, automatic-ending readiness, supported late-join state and bounded transport backpressure are implemented. Exact coverage and remaining limits are recorded in `sync-progress.md`. General decorative destruction, arbitrary timeline tracks/signals, complete world/checkpoint saves and full natural campaign acceptance remain outside the verified coverage.
