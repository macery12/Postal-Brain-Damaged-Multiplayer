# Development status

Current baseline: **PBDMP 0.8.0 / protocol 7**. This page replaces a superseded in-progress checkpoint; it is not a published-release announcement.

Implemented foundations include host-owned campaign lifecycle, actor/combat authority, native weapon/projectile presentation, shared doors/requester keys, personal resources, urine interactions and bounded mission-state adapters. Required Neighborhood wall destruction and representative walk-in/interactable ending fixtures pass. Large snapshots retain ordering through bounded queues and backpressure.

0.8.0 adds Steam lobby hosting/joining through the game's own Steam client and relay, with the session made carrier-independent, and replaces the connection dialog with a native lobby panel, prompts and an in-game badge. The single-account Steam probe passes, and the first two-account session joined the lobby and connected over Steam's relay. That session exposed a catalogue determinism bug (P2 returned to the menu after loading Neighborhood), now fixed; a full two-account playthrough is the next acceptance step. See [Steam research](steam-research.md).

See the [detailed sync tracker](sync-progress.md) for per-mechanic status, [verification](verification.md) for evidence/reproduction commands and [roadmap](roadmap.md) for outstanding work.

Before treating this build as a validated co-op release, complete natural two-PC campaign acceptance, classify unsupported mission callbacks/machinery and address remaining player-presentation gaps. Full checkpoint/world restoration and persistent co-op saves remain future work.
