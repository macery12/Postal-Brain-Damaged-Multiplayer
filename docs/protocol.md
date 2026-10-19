# Protocol (development)

PBDMP 0.10.0 development source carries protocol 12 over either of two links, with P1 hosting and P2 joining. Protocol versions and build fingerprints must match. See `src/PBDMP.Network/Protocol.cs`, `WorldProtocol.cs` and `CoopSession.cs` for the authoritative byte layout and validation, and `src/PBDMP.Plugin/Plugin.cs` for fingerprint generation.

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
| Pose | 3 | Both | Level key, readiness, feet position, yaw/height, weapon GUID, pitch, visual shot sequence, alive flag, life generation, travel epoch, optional moving-platform reference and bounded presentation state. |
| Reject | 4 | P1 → P2 | Reason, including host loading failure/session closure feedback. |
| Travel | 5 | P1 → P2 | Epoch, Menu/Load/Restart action, level GUID, difficulty, host-ready/paused/released/joining flags and optional join loadout. |
| Ready | 6 | P2 → P1 | Travel epoch and readiness. |
| World | 7 | By world kind | Typed shared-state, presentation or client-request frame described below. |
| Save | 8 | By save command | Bounded persistence control/data pages described below. |

Pose and lifecycle updates are sent on a 50 ms loop (nominal 20 Hz). Old poses are replaced rather than accumulated. The pose shot sequence is a presentation signal; authoritative attacks use world frames. The optional platform reference travels atomically with its pose: a strict presence flag, a 64-character hexadecimal authored transform key and three finite local feet coordinates, each bounded to ±256. The Unity adapter resolves only registered platform volumes and bounds the correction against the reported world position. This prevents network delay from placing a rider below a moving lift on P1.

Join loadouts contain current/last weapon GUIDs, up to 64 unique item records (GUID plus quantity 0–1,000,000), and host feet position/yaw. The Unity adapter selects known weapons/ammo and clamps native capacities; host health, armor, keys and powerups are excluded.

## Shared-world frames

`WorldFrame` carries, in order: byte kind; uint32 epoch; uint64 sequence; uint32 entity/revision; key/asset strings; byte actor; uint32 life/flags; three float32 vectors (position, rotation, velocity); two float32 values; a byte count and up to 32 float32 values; a byte count and up to 32 UTF-8 strings. Each string is bounded to 256 bytes; numeric values must be finite and within the schema's bounds.

| World family | Direction | Purpose |
| --- | --- | --- |
| Manifest, SnapshotBegin/End | P1 → P2 | Match authored catalogues and delimit current-state restoration. |
| ManifestAck, SnapshotAck | P2 → P1 | Acknowledge catalogue and restored state. |
| Enemy, ActorState/Effect | P1 → P2 | Host-owned lifecycle, health, animation/presentation and actor state. |
| Scenery, SceneryEvent, Mechanism, Sequence, PlayerEffect | P1 → P2 | Typed scenery/motion/timing, passive timeline presentation and victim-specific world effects. |
| Door, Encounter, UrineState, MissionState | P1 → P2 | Supported shared world/controller state and progression. |
| Projectile, Loot, Impact, WeaponEffect | P1 → P2 | Native projectile/effect presentation and personal copies of host-selected drops. |
| IncomingHit, InteractionResult | P1 → P2 | Victim-local damage application and requester interaction results. |
| Fire, Melee, Interact, Trigger | P2 → P1 | Validated attack/interaction intent. |
| Defense, Reflect, Special, ClientEffect | P2 → P1 | Supported special-action and effect requests. |

Client request kinds start at 32; client acknowledgements are the explicit exceptions below that value. `WorldProtocol.FromClient` enforces transport direction, while native adapters validate current epoch/life, targets, proximity and supported actions. P2 cannot publish authoritative mission/enemy state. This is host authority, not a complete anti-cheat system.

Entity identity is scoped to a travel epoch; revisions and life generations distinguish pooled objects and respawns. An enemy id names one life: P1 gives a pooled enemy a new id each time it is enabled again and drops the previous id's per-life state. P2 destroys a replica it instantiated once P1 reports that life inactive (authored scene enemies are only hidden), and ignores an unknown id that is already inactive, so endless pooling (Survival) stays bounded by P1's live pool. A presentation or actor part of a pooled enemy that has no counterpart on P2's replica (a runtime-only child on P1) is logged once and ignored instead of ending the session; authored scripted actors must still match exactly. Authored scene/component identities are catalogue-based rather than native pointers. Typed bounded data is transmitted, never arbitrary serialized objects or executable callbacks.

## Loading, queues and authority

Gameplay release waits for matching-epoch map readiness and supported world restoration. Snapshots restore current supported state; old explosions, death audio, progression callbacks and rewards are not replayed. Persistent saves additionally restore a native shared baseline and per-player data before release. Unclassified arbitrary scripts/timelines remain outside complete live-sync coverage.

Transport send/receive world queues are capped at 4,096 frames. A full send queue returns false so its caller retains and retries the frame. The native publisher drains at most 128 retained frames per tick; the transport sends batches of up to 512 per send loop. Receive saturation waits asynchronously rather than discarding state: TCP applies backpressure to the sender, and Steam keeps unread messages queued in its client. Main-thread capture yields between objects under its frame budget.

Connect allowance: 5 seconds for TCP, 60 seconds for Steam. Handshake deadline: 5 seconds. Write-batch deadline: 3 seconds. Incoming-frame deadline: 10 seconds. Campaign readiness deadline: 120 unscaled seconds. Network servicing remains independent of Unity pause. When a peer closes with a Reject reason, the other side's receiver gets 250 ms to report that reason even if its own send failed first. P2 leaving releases host waiting; P1 leaving ends P2's session.

Player resource values/collection remain personal. P1 owns enemies, shared hit results and supported world progression. Replica effects cannot apply the same gameplay result again. The protocol itself adds no authentication or encryption: Steam sessions rely on Steam's authenticated, encrypted relay and lobby membership, while Direct IP sessions are meant for a trusted LAN.

## Completion, fire and impact adapters

`MissionState` flag 1024 carries the host's final level statistics: three current/max quantity pairs, completion bits and a decimal-string completion time in milliseconds (avoiding the bounded float field's duration limit). It persists within the travel epoch and is included in late-join snapshots. P2 opens the native result screen after map/world readiness; P1 retains level-travel authority. Statistics are captured after the native host completion method gathers them.

`Special` actions 75 and 76 request supported world flame ticks and barrel hits respectively. P1 validates the target, living sender/life generation, range and bounded damage, then invokes the native target so linked explosion/progression listeners execute on the host. Barrel/burn state and linked machinery restore through the existing bounded snapshot.

`Impact` flag 4 carries native material hit particles/audio and a contact normal. P1 selects flesh/armor effects after accepted NPC hits; P2 instantiates harmless presentation, including for its own attacks. Impact bursts, audio and explosions are transient and do not replay on late join.

## Persistence control and disk state

`SavePage` carries a command byte, uint32 travel epoch and transaction, int32 page index/count/length, and up to 4,096 bytes. Transactions are non-interleaved and strictly ordered, with a 4 MiB maximum payload. The transport copies owned bytes and enforces command direction. Native pointers and callbacks never cross the wire.

| Command | Value | Direction | Purpose |
| --- | ---: | --- | --- |
| Identity | 1 | P2 → P1 | Steam account or persisted Direct IP player ID. |
| GuestState | 2 | P2 → P1 | Current personal record and collection history. |
| HoldAck | 3 | P2 → P1 | Consistent personal capture for the requested transaction. |
| Restored | 4 | P2 → P1 | Personal restore acknowledged. |
| Checkpoint | 5 | P2 → P1 | Bounded native checkpoint identity/pose request, validated against host trigger bounds. |
| WorldRestored | 6 | P2 → P1 | Native shared baseline applied before the authoritative snapshot. |
| Capture | 16 | P1 → P2 | Hold gameplay and capture personal state. Optional one-byte capture kind: 0 manual/quicksave, 1 autosave, used for the hold overlay. Empty payload means manual; other values/lengths are rejected. |
| Resume | 17 | P1 → P2 | Release a capture hold. |
| Restore | 18 | P1 → P2 | Personal record, host gear catch-up and restoration flags. |
| WorldRestore | 19 | P1 → P2 | One ordered chunk of the current non-player/non-NPC native baseline. |
| WorldRestoreBegin | 20 | P1 → P2 | Bounded byte count and SHA-256 digest for a fresh baseline transaction. |
| WorldRestoreEnd | 21 | P1 → P2 | Finalize and verify the assembled native baseline. |

Save queues hold 1,024 pages in each transport direction; the adapter retains at most 2,048 pending pages. Each send cycle includes at most 32 save pages alongside regular traffic. Epoch changes discard stale restoration work; pages for a *later* epoch that reach P2 before it adopts that travel epoch (P1 can begin a join baseline within a frame of a Survival REST admission) are kept, at most 4,096, and replayed once P2 adopts it, instead of being dropped and stalling the join. Loading waits for native baseline acknowledgement, the shared-world snapshot and personal readiness; P2 cannot promote its own state to shared authority.

Disk schema 2 is independent of wire epochs. The envelope is eight ASCII bytes `PBDMPS01`, int32 JSON byte length, 32 SHA-256 bytes and bounded UTF-8 JSON, up to 32 MiB. `CoopSaveStore` validates data-only records, schema, scalar bounds and permitted durable world kinds. The plugin checks game hashes, supported content and the authored catalogue before native loading. Atomic replacement retains a known-good `.bak`; corrupt primary files cannot replace a valid backup.

[Co-op save behavior](co-op-saves.md) specifies host control, personal-resource resets, partner assignment and recovery.


## Current-world baseline streaming

`WorldBaseline` bounds the complete native baseline to 32 MiB. A begin header is at most 512 bytes; each ordered chunk is at most 1 MiB and uses the existing 4,096-byte `SavePage` framing. Each page transaction remains bounded to 4 MiB. The receiver requires begin → chunks → end, verifies length and SHA-256, and discards incomplete work on epoch changes or disconnect. The publisher retains and drains bounded chunks rather than filling transport queues with an entire baseline at once.

Every ordinary join captures the current host world. Loading an older checkpoint and then changing the world does not reuse that checkpoint as the next guest's baseline. Native baseline application, typed world state and personal restoration all finish before gameplay release.

## Development typed adapters

Adapters resolve known authored keys and fields; payloads cannot request arbitrary component calls or native pointers. `Scenery` distinguishes glass, partial burning, partial/queued barrels, authored or pooled physical bodies, and gameplay fragments. `Sequence` carries mechanical timeline phase/play state; old signals are suppressed during restoration. Unity directors that reset their reported time after stopping retain the phase that actually played.

| Mechanism flag | State |
| ---: | --- |
| 1 | Falling-platform remaining delay and requester. |
| 2 | Scale phase, origin, duration and continuation. |
| 3 | Stable shared secret discovery. |
| 4 | TNT-gate remaining fuse. |
| 5 | Registered native machinery loop playback/seek state. |
| 7 | Referenced scripted angular speed and accumulated rotation. |
| 8 | Rocket animation/head pose and pending launch continuation. |
| 9 | COVID arena segment ordering and base position. |
| 10 | Registered native machinery cooldown state. |
| 11 | Native syringe animation, eligibility and passive injection presentation. |

Interaction retries retain their original request ID. P1 caches the committed result, so retransmission cannot plant another TNT charge or consume another key. Personal recovery records wait while the requester has an unresolved item reservation.

Schema-1 files can be inspected by the storage reader, but the 0.10 loader refuses restoration when the older format lacks required world state. Original files and backups remain untouched. The [megapass](../megapass.md) and [tracker](sync-progress.md) distinguish these development adapters from completed runtime acceptance.

## World/presentation follow-up (protocol 10)

Travel appends strict `HostMenuPaused` after the story fields. It implies `Paused`; capture/loading holds can pause gameplay with this flag false. P2 opens the native pause screen only after map/world/personal admission. The client closes only a screen it opened for the host, preserving an existing local menu.

Shared posters use their authored native save GUIDs. `Mechanism` flag 6 carries a 0/1 collected value, a 0/1 live-notice hint and an empty values array. Snapshot/persistent capture uses a zero live-notice hint. `SceneryEvent` flag 2 carries the separate live collection sound/notice. P2 requests collection with `Special` flag 94; P1 validates current life, known identity, availability and proximity. Native distance optimization of P1's copy is not interpreted as collection.

Native material impacts retain their game-selected effect and audio keys. `Special` flag 95 requests a harmless NPC surface effect: P1 validates the current player/target, bounded position and an existing solid-material asset pair. It changes no health or inventory. P1 relays `Impact` flag 4; actor 2 identifies the already-present local P2 effect, suppressing its echo on P2. Native P1 impacts are forwarded directly, rather than guessed from an arbitrary NPC child override.

Urine surface flag 4 includes authored `OcclusionPortal.open` alongside renderer/collider/light/navigation bits. Legacy layouts that omit only portal bits retain their restored native portal state. All remaining lengths and boolean values are still validated. Authored prop membership is local scene-load bookkeeping; Unity instance IDs never cross the wire.


## Player presentation extension (protocol 11)

After the optional carrier, every pose appends 15 bytes: byte motion (grounded/airborne/sliding), uint16 actions, uint32 action sequence, float32 normalized phase and float32 charge. Action bits are firing 1, secondary 2, melee 4, loop 8, block 16, shield 32, charge 64, kick 128, left-hand 256 and saw-running 512. Undefined motion/action bits, non-finite numbers, phase outside 0–1 and charge outside 0–100 are rejected. Exact game/mod fingerprint checks remain unchanged.

This data controls visuals only. Existing world messages still authorize attacks and damage. Death/scene generations retain their existing guards; initial snapshots do not replay old shot counters. Local native callbacks and current weapon state supply the snapshot, while remote visual rigs contain no gameplay scripts or colliders. The 20 Hz loop and save schema 2 are unchanged.

## Survival state (protocol 12)

Protocol 12 appends world kind **Survival = 24** (P1 → P2). It carries P1's native Survival run presentation state; P2's native controller does not simulate. `SurvivalProtocol` in `src/PBDMP.Network/SurvivalProtocol.cs` owns the layout and validation:

| Field | Encoding | Bounds |
| --- | --- | --- |
| Run | `Entity` (uint32) | Increments on each native `Begin` in a session. P2 ignores a lower run within the same epoch; retry is also a new epoch. |
| Phase, countdown finished | `Flags` bits 0–3, bit 4 | Native INTRO/COMBAT/CLEANUP/REST (0–3); other bits rejected. |
| Timer, combo | `Value`, `Value2` | Timer −60…86,400 s; combo 1–5. |
| Score, damage | `Data[0]`, `Data[1]` decimal strings | Finite, 0…1e15. Strings avoid the generic ±1,000,000 world-float bound for long runs. |
| Wave, waves survived, countdown, kills, live enemies | `Data[2..6]` canonical integers | Wave 1…1,000,000 (survived ≤ wave), countdown 0–10, kills ≤ 1e9, live ≤ 4,096. |
| Ready, required, revive | `Data[7..9]` masks | Bit 1 = P1, bit 2 = P2 (0–3). Required lists admitted participants alive or awaiting revival; revive authorizes the listed dead players at this REST. |
| Run ended | `Flags` bit 5 | Set once the team is wiped; P2 then runs its native end once. |
| Offers | `Data[10]` | Empty, or up to 8 `slot:token` pairs, REST only. Slot 0–15 is the native offer spawn; token `W<n>`/`I<n>` (n ≤ 63) indexes the spawner's authored weapon/item prefab arrays. Duplicate slots rejected. |
| Run ID | `Data[11]` | Empty or 32 lowercase hex characters; P1 generates it at native `Begin`. Both PCs key their local co-op record by it. |
| Result time, best weapon | `Data[12]`, `Data[13]` | Only once the run has ended (otherwise 0 and empty): the native level `TimeStat` value (0…100,000,000) and the best weapon's asset ID (≤ 64 printable characters, no comma). |

Key, asset, values, revision, actor and life must be empty/zero. The frame is transient (not saved) and is included in join snapshots. P1 sends it immediately when any field except the timer and combo changes (both decay continuously), otherwise at most every 0.5 s. P2 calls its native HUD view's own label handlers from this state and counts the timer down locally between frames. Enemies, hits and deaths use the existing Enemy/Melee/IncomingHit families unchanged.

Protocol 12 also appends client kind **SurvivalRequest = 40** (P2 → P1): `Entity` = run, `Revision` = wave, `Actor` = 2, `Flags` bits 0–3 = phase and bits 4–5 = kind (1 Ready, 2 Offer, 3 Damage). Ready and Offer are INTRO/REST only; an Offer request carries exactly one REST token in `Data`; Ready and Damage carry none. Damage reports one native damage event on P2 (health or armor lost); P1 applies the native combo penalty once for the current run, in any phase but INTRO, also for a killing blow. P1 accepts a request only from an admitted, living P2 for its current run, wave and preparation phase, and an Offer only for a token in the current offer set; anything else is counted and ignored. A request only marks readiness and records the weapon choice; P2 collects its own copy locally.

Travel (protocol 12) appends strict `AdmissionWaiting` after `HostMenuPaused`. It is valid only for an arena (not Menu) and tells a connecting P2 to stay where it is: P1 is holding Survival admission until its next preparation phase. P1 advertises it whenever no peer is connected and a newcomer would be held, so the first state a newcomer receives already holds it.
