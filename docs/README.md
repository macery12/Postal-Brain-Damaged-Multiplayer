# Documentation

Start with the [project README](../README.md) for installation and play instructions.

## Current references

| Document | Purpose |
| --- | --- |
| [Sync progress](sync-progress.md) | Individual shared/local feature status, planning score and limits. |
| [Verification](verification.md) | Recorded checks, repeatable commands, troubleshooting and manual acceptance. |
| [Roadmap](roadmap.md) | Current session rules and remaining work. |
| [Protocol](protocol.md) | Protocol 7 carriers (Steam P2P, TCP), framing, authority, snapshots and bounds. |
| [Steam research](steam-research.md) | How PBDMP reaches the game's Steam client, callbacks, native layouts and single-account verification. |
| [Contributor guidance](contributing.md) | Build/test expectations and publication hygiene. |
| [Mission-trigger design](mission-trigger-sync-plan.md) | Accepted mission/exit rules, wall root cause and adapter limits. |
| [Development status](development-progress.md) | Short implementation handoff. |

## Design and historical research

These notes preserve the findings and scope at the time of research. They may describe earlier versions or pre-implementation uncertainty; use the current sync tracker and verification page for delivered behavior. Research references to `<GameDir>` and `<Decompiled>` describe optional reader-owned files, not repository links or dependencies to download.

| Document | Subject |
| --- | --- |
| [Initial research](research.md) | Loader choice and initial movement architecture. |
| [Player model](player-model-research.md) | Runtime source assets and clean rig reconstruction. |
| [Early gun presentation](gun-sync-research.md) | Original visual-only weapon milestone. |
| [Animation and effects](animation-effects-research.md) | Native clips, binding and visual asset reuse. |
| [Native UI](ui-theme-research.md) | Fonts, controls, dialog art and input ownership. |
| [Campaign travel](session-travel-research.md) | Native level/life/save lifecycle findings. |
| [Gun and door research](gun-door-research.md) | Native attack and interaction paths. |
| [Enemy/combat research](enemy-combat-research.md) | Targeting, hit authority and encounter findings. |
| [Shared-combat design](shared-combat-spec.md) | Accepted combat/resource ownership design. |
| [Historical campaign census](campaign-coverage.md) | Dated component counts before the mission pass. |

Native UI/model renders are in `images/`; they are diagnostic captures, not a full co-op gameplay recording. The multiplayer-panel capture is the 0.8.0 lobby panel from the runtime smoke test, which never shows the tester's Steam identity. Extracted game asset files and raw diagnostic reports are not distributed.
