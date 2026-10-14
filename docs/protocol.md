# Protocol 7

PBDMP 0.8.0 carries protocol 7 over either of two links, with P1 hosting and P2 joining. Protocol versions and build fingerprints must match. See `src/PBDMP.Network/Protocol.cs`, `WorldProtocol.cs` and `CoopSession.cs` for the authoritative byte layout and validation, and `src/PBDMP.Plugin/Plugin.cs` for fingerprint generation.

## Carriers

| Carrier | Setup | Notes |
| --- | --- | --- |
| Steam P2P | P1's two-slot Steam lobby; P2 dials the lobby owner with `ConnectP2P` on virtual port 0. | Relayed and encrypted by Steam; the host admits only lobby members. Each protocol frame travels as one reliable Steam message. 2 MB send buffer, 512 KB/s send rate, 60-second connect allowance. Details: [Steam research](steam-research.md). |
| Direct TCP | IPv4 address and port (default `7777`). | Unencrypted and unauthenticated; intended for a trusted LAN or a deliberately forwarded port. |

`CoopSession` sees both as an ordered reliable byte stream (`IPeerConnection`), so framing, deadlines and queues below are identical. Steam lobbies additionally advertise `pbdmp_version`, `pbdmp_proto` and `pbdmp_build` (the first 16 hex characters of SHA-256 over the fingerprint) so a joiner can report a mismatch before dialling; the handshake below remains the authoritative check.

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

Transport send/receive world queues are capped at 4,096 frames. A full send queue returns false so its caller retains and retries the frame. The native publisher drains at most 128 retained frames per tick; the transport sends batches of up to 512 per send loop. Receive saturation waits asynchronously rather than discarding state: TCP applies backpressure to the sender, and Steam keeps unread messages queued in its client. Main-thread capture yields between objects under its frame budget.

Connect allowance: 5 seconds for TCP, 60 seconds for Steam. Handshake deadline: 5 seconds. Write-batch deadline: 3 seconds. Incoming-frame deadline: 10 seconds. Campaign readiness deadline: 120 unscaled seconds. Network servicing remains independent of Unity pause. When a peer closes with a Reject reason, the other side's receiver gets 250 ms to report that reason even if its own send failed first. P2 leaving releases host waiting; P1 leaving ends P2's session.

Player resource values/collection remain personal. P1 owns enemies, shared hit results and supported world progression. Replica effects cannot apply the same gameplay result again. The protocol itself adds no authentication or encryption: Steam sessions rely on Steam's authenticated, encrypted relay and lobby membership, while Direct IP sessions are meant for a trusted LAN.

## Completion, fire and impact adapters

`MissionState` flag 1024 carries the host's final level statistics: three current/max quantity pairs, completion bits and a decimal-string completion time in milliseconds (avoiding the bounded float field's duration limit). It persists within the travel epoch and is included in late-join snapshots. P2 opens the native result screen after map/world readiness; P1 retains level-travel authority. Statistics are captured after the native host completion method gathers them.

`Special` actions 75 and 76 request supported world flame ticks and barrel hits respectively. P1 validates the target, living sender/life generation, range and bounded damage, then invokes the native target so linked explosion/progression listeners execute on the host. Barrel/burn state and linked machinery restore through the existing bounded snapshot.

`Impact` flag 4 carries native material hit particles/audio and a contact normal. P1 selects flesh/armor effects after accepted NPC hits; P2 instantiates harmless presentation, including for its own attacks. Impact bursts, audio and explosions are transient and do not replay on late join.
