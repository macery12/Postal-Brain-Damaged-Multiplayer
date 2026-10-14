# Protocol 7

PBDMP 0.7.0 uses TCP over IPv4, with P1 hosting and P2 joining (default port `7777`). Protocol versions and build fingerprints must match. See `src/PBDMP.Network/Protocol.cs`, `WorldProtocol.cs` and `LanSession.cs` for the authoritative byte layout and validation, and `src/PBDMP.Plugin/Plugin.cs` for fingerprint generation.

## Compatibility and framing

The fingerprint is `SHA256(GameAssembly.dll):SHA256(global-metadata.dat):BuildId`. `BuildId` is SHA256 of the concatenated hexadecimal SHA256 strings of `PBDMP.dll` and `PBDMP.Network.dll`. This rejects differing mod rebuilds as well as game-code/metadata differences; it does not hash every game asset. Both DLLs must be distributed as one matching pair.

Each frame has an int32 little-endian payload length, followed by 1–8192 payload bytes. The payload starts with a byte message kind. Strings use strict UTF-8, a uint16 byte length and at most 256 bytes. Invalid lengths, enum values, booleans, non-finite values, trailing data and role violations reject the connection. IDs and quantities have additional schema-specific bounds.

| Message | Kind | Direction | Payload overview |
| --- | ---: | --- | --- |
| Hello | 1 | P2 → P1 | int32 protocol version and fingerprint text. |
| Welcome | 2 | P1 → P2 | int32 protocol version and fingerprint text. |
| Pose | 3 | Both | Level key, readiness, feet position, yaw/height, weapon GUID, pitch, visual shot sequence, alive flag, life generation and travel epoch. |
| Reject | 4 | P1 → P2 | Reason, including host loading failure/session closure feedback. |
| Travel | 5 | P1 → P2 | Epoch, Menu/Load/Restart action, level GUID, difficulty, host-ready/paused/released/joining flags and optional join loadout. |
| Ready | 6 | P2 → P1 | Travel epoch and readiness. |
| World | 7 | By world kind | Typed shared-state, presentation or client-request frame described below. |

Pose and lifecycle updates are sent on a 50 ms loop (nominal 20 Hz). Old poses are replaced rather than accumulated. The pose shot sequence is a presentation signal; authoritative attacks use world frames.

Join loadouts contain current/last weapon GUIDs, up to 64 unique item records (GUID plus quantity 0–1,000,000), and host feet position/yaw. The Unity adapter selects known weapons/ammo and clamps native capacities; host health, armor, keys and powerups are excluded.

## Shared-world frames

`WorldFrame` carries, in order: byte kind; uint32 epoch; uint64 sequence; uint32 entity/revision; key/asset strings; byte actor; uint32 life/flags; three float32 vectors (position, rotation, velocity); two float32 values; a byte count and up to 32 float32 values; a byte count and up to 32 UTF-8 strings. Each string is bounded to 256 bytes; numeric values must be finite and within the schema's bounds.

| World family | Direction | Purpose |
| --- | --- | --- |
| Manifest, SnapshotBegin/End | P1 → P2 | Match authored catalogues and delimit current-state restoration. |
| ManifestAck, SnapshotAck | P2 → P1 | Acknowledge catalogue and restored state. |
| Enemy, ActorState/Effect | P1 → P2 | Host-owned lifecycle, health, animation/presentation and actor state. |
| Door, Encounter, UrineState, MissionState | P1 → P2 | Supported shared world/controller state and progression. |
| Projectile, Loot, Impact, WeaponEffect | P1 → P2 | Native projectile/effect presentation and personal copies of host-selected drops. |
| IncomingHit, InteractionResult | P1 → P2 | Victim-local damage application and requester interaction results. |
| Fire, Melee, Interact, Trigger | P2 → P1 | Validated attack/interaction intent. |
| Defense, Reflect, Special, ClientEffect | P2 → P1 | Supported special-action and effect requests. |

Client request kinds start at 32; client acknowledgements are the explicit exceptions below that value. `WorldProtocol.FromClient` enforces transport direction, while native adapters validate current epoch/life, targets, proximity and supported actions. P2 cannot publish authoritative mission/enemy state. This is host authority, not a complete anti-cheat system.

Entity identity is scoped to a travel epoch; revisions and life generations distinguish pooled objects and respawns. Authored scene/component identities are catalogue-based rather than native pointers. Typed bounded data is transmitted, never arbitrary serialized objects or executable callbacks.

## Loading, queues and authority

Gameplay release waits for matching-epoch map readiness and supported world restoration. Snapshots restore current supported state; old explosions, death audio, progression callbacks and rewards are not replayed. General scripts/timelines and persistent checkpoint/save restoration remain incomplete.

Transport send/receive world queues are capped at 4,096 frames. A full send queue returns false so its caller retains and retries the frame. The native publisher drains at most 128 retained frames per tick; the transport sends batches of up to 512 per send loop. Receive saturation waits asynchronously, applying TCP backpressure rather than discarding state. Main-thread capture yields between objects under its frame budget.

Handshake deadline: 5 seconds. Write-batch deadline: 3 seconds. Incoming-frame deadline: 10 seconds. Campaign readiness deadline: 120 unscaled seconds. Network servicing remains independent of Unity pause. P2 leaving releases host waiting; P1 leaving ends P2's session.

Player resource values/collection remain personal. P1 owns enemies, shared hit results and supported world progression. Replica effects cannot apply the same gameplay result again. Neither authentication nor encryption is implemented; the intended environment is a trusted LAN.
