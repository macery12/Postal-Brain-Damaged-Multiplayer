# Megapass campaign playtest

**Completed October 18, 2026.** The maintainer played this checklist on two PCs over Steam, including saves, rejoining and checkpoints. Busy-world performance remains for the next optimization phase.

The implementation closeout uses focused native regressions and a maintainer campaign playtest, as agreed on October 16, 2026. These checks can happen during normal co-op play; exhaustive automated traversal is not a prerequisite for starting the next development phase. Unverified paths keep their partial markers in [sync progress](sync-progress.md).

## Start here

1. Play Neighborhood into Prison. Let P2 trigger the breaker, cross the garage opening, use the barrel/pipe route and join from a running solo map.
2. In Backyard, operate the wheel using its actual buttons, ride it together, then save/load and rejoin. In Forest, let P2 trigger the bridge and cross it after restoration.
3. Save at meaningful progression points, continue or reconnect, and check that the next puzzle/encounter still works. Report the first difference between players.

## Remaining manual acceptance

| Area | Targeted evidence | What to check while playing |
| --- | --- | --- |
| Forest bridge | Native activation, every bridge transform, shared pause, disk rollback and completed state pass. | Natural P2 trigger, crossing, late join and progression beyond the bridge. |
| Wheel/cart | The actual root driver is identified through its native completion callback; linked doors and motion pass pause/rollback/completion. | Actual button flow, both riders, collision, saving on the cart and rejoining during travel. |
| Paw/shockwave | Runtime paw replicas cannot hurt P2 locally; P1 delivers native damage once; pause and victim isolation pass. | Real boss/trap timing, leaving/re-entering an attack, death/respawn and different attack variants. |
| Lasers/crush/riders | Stale native damage contacts no longer hit P2; genuine overlap still hurts. The full Area69 restored journey passes. | Real lasers, ceilings, underside entry and obstruction. Legitimate hazards must remain dangerous. |
| Interaction edge cases | Host eligibility checks and immutable request/result/refund ledger are implemented; existing key/TNT duplicate checks remain. | Both players use the same control at once, missing items, locked conditions, walking away and disconnecting during a pending interaction. |
| Gameplay fragments | Host-owned physical/spawned props pass pushing, pooling, rollback and rejoin. Fragment adapters/classification exist. | Fragments that block, damage or operate anything; harmless debris can settle differently. Fragment classification remains partial until these cases are checked. |
| Persistent world recovery | Live save/restore and fresh-process host-alone resume plus P2 join pass, including personal pickups/drops and structural state. | Additional machinery families, multiple rollbacks, advancing after load and joining the current progressed world. |
| Interrupted recovery | Managed corruption, compatibility, paging and transport failure checks pass; native recovery paths are implemented. | Close/disconnect a peer during loading/restoration; check clear recovery, usable controls and preserved save/backup files. Use disposable co-op slots. |
| Performance | Connected-idle FPS hardening and retained timing-budget checks are recorded in [performance](performance.md). | Busy combat, moving machinery and destruction over Steam/LAN. Broader optimization is the next performance phase. |
| Audio/story/UI | Native pause, story ownership, skip, pending admission, endings and input checks pass. | Listen for stuck/missing loops after pause, load, skip and disconnect; check menus through natural sequences. |
| Campaign traversal | All 15 maps have catalogue/bounded-state evidence; representative native routes/actions pass. | Meaningful two-player play through the campaign, including bosses, saves and reconnects. |

## Reporting a difference

Use **REPORT A BUG** on the main menu. Include the level, P1/P2 role, action and whether a join, save/load or restart preceded it. The form prefills software/build information and available last-session context. Review attached logs for personal information.

A failing manual case becomes a specific bug with reproduction steps. It does not require restarting the entire megapass checklist. This handoff does not mark untested campaign paths as verified or change the progress-score formula.
