# Documentation

Start with the [project README](../README.md) for installation and play instructions.

## Current references

| Document | Purpose |
| --- | --- |
| [Agent research and testing](agent-research-and-testing.md) | Decompiled declarations vs native behavior, ISIL address resolution and isolated runtime checks. |
| [Sync progress](sync-progress.md) | Individual shared/local feature status, development credit, accepted baseline and remaining gaps. |
| [Performance baseline](performance.md) | Initial connected/disconnected FPS, stalls and repeatable budget checks. |
| [Weapon animation checks](weapon-animation-testing.md) | Grip profiles, native animation reuse, multi-angle captures and connected presentation tests. |
| [Verification](verification.md) | Recorded checks, repeatable commands, troubleshooting and manual acceptance. |
| [Co-op saves](co-op-saves.md) | Save controls, checkpoint restoration, personal progress and recovery. |
| [Roadmap](roadmap.md) | Current session rules and remaining work. |
| [Protocol](protocol.md) | Network carriers (Steam P2P, TCP), framing, authority, snapshots and bounds. |
| [Steam research](steam-research.md) | How PBDMP reaches the game's Steam client, callbacks, native layouts and single-account verification. |
| [Contributor guidance](contributing.md) | Build/test expectations and publication hygiene. |
| [Mission-trigger design](mission-trigger-sync-plan.md) | Accepted mission/exit rules, wall root cause and adapter limits. |

## Design and historical research

These notes preserve the findings and scope at the time of research. They may describe earlier versions or pre-implementation uncertainty; use the current sync tracker and verification page for delivered behavior. Research references to `<GameDir>` and `<Decompiled>` describe optional reader-owned files, not repository links or dependencies to download.

| Document | Subject |
| --- | --- |
| [Player model](player-model-research.md) | Runtime source assets and clean rig reconstruction. |
| [Animation and effects](animation-effects-research.md) | Native clips, binding and visual asset reuse. |
| [Native UI](ui-theme-research.md) | Fonts, controls, dialog art and input ownership. |
| [Campaign travel](session-travel-research.md) | Native level/life/save lifecycle findings. |
| [Gun and door research](gun-door-research.md) | Native attack and interaction paths. |
| [Enemy/combat research](enemy-combat-research.md) | Targeting, hit authority and encounter findings. |
| [Shared-combat design](shared-combat-spec.md) | Accepted combat/resource ownership design. |

The seven README JPGs in `images/` are full-frame PBDMP 1.0.1 captures from two visible local game copies over Direct IP. See [capture staging and reproduction](readme-captures.md) for the privacy controls, live saves and capture-only presentation adjustments. `player-model.png` remains the diagnostic model render used by the player-model research. Extracted game assets and raw captures/reports are not distributed.
