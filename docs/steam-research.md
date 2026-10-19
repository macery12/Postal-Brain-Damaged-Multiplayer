# Steam hosting research

Research date: October 13, 2026, against the reference build (Unity 2021.3.14f1, Steam app 1359980, build 21193832). It records why PBDMP 0.8.0 reaches Steam the way it does. Statements describe that build; re-check them after a game update. Paths use the README's [path placeholders](../README.md#path-placeholders).

## What the game already ships

- `<GameDir>/POSTAL Brain Damaged_Data/Plugins/x86_64/steam_api64.dll`, version 06.28.18.86 (Steamworks SDK 1.51), with Steamworks.NET 15.0.1 compiled into `GameAssembly.dll`.
- The DLL's export table contains the flat API needed for co-op: `SteamAPI_SteamMatchmaking_v009`, `SteamAPI_SteamFriends_v017`, `SteamAPI_SteamUtils_v010`, `SteamAPI_SteamUser_v021`, `SteamAPI_SteamNetworkingSockets_SteamAPI_v009`, `SteamAPI_SteamNetworkingUtils_SteamAPI_v003`, lobby create/join/data/leave, `ActivateGameOverlayInviteDialog`, avatar image access, and `CreateListenSocketP2P`/`ConnectP2P`/`SendMessageToConnection`/`ReceiveMessagesOnConnection`/`CreateSocketPair`.
- `Hyperstrange.Base.SteamPlatformHandler` initialises Steam at startup. Started outside Steam without an app id, `PostalAppBuilder` logs "Would restart Steam" and the game exits, so direct launches (the test scripts) put `SteamAppId` in the process environment.

## Why PBDMP does not use the game's Steamworks.NET

The IL2CPP wrapper only has ahead-of-time code for the generic callbacks the game itself uses: `Callback<DownloadItemResult_t>` and leaderboard/UGC `CallResult<T>` instantiations (`<Decompiled>/Il2CppDumper/dump.cs`). `Callback<LobbyCreated_t>`, `Callback<GameLobbyJoinRequested_t>` and the networking status callback do not exist and cannot be created at runtime. Non-generic static wrappers exist, but calling IL2CPP code from network worker threads would also require attaching those threads to the IL2CPP domain.

PBDMP therefore calls the flat C API directly (`src/PBDMP.Network/Steam/SteamNative.cs`). These calls are plain P/Invokes into the module the game already loaded, are safe from worker threads, and never initialise or shut down Steam.

## Callbacks: observe the game's pump, never run a second one

Steamworks.NET 15 uses manual dispatch. `SteamManager.Update` → `SteamAPI.RunCallbacks` → `CallbackDispatcher.RunFrame` pulls each callback with `SteamAPI_ManualDispatch_GetNextCallback` and fetches every completed call result with `SteamAPI_ManualDispatch_GetAPICallResult`, whether or not anything is registered for it. A second dispatcher would consume callbacks the game needs (achievements, leaderboards, overlay state).

The ISIL for `RunFrame` (`<Decompiled>/Cpp2IL/Native-ISIL/IsilDump/Steamworks.NET/Steamworks/CallbackDispatcher.txt`) shows those two P/Invokes inlined: a lazily resolved function pointer followed by `Call rax`. A Harmony patch on the IL2CPP `NativeMethods` wrapper would never run. `src/PBDMP.Plugin/SteamCallbackTap.cs` instead installs BepInEx native detours on the two DLL exports. The hooks call the original, copy the payloads PBDMP needs into a queue, and return the original result; the game sees every callback unchanged. The plugin drains the queue on Unity's main thread.

| Callback | Id | Arrives as |
| --- | ---: | --- |
| `GameLobbyJoinRequested_t` | 333 | Callback (invite accepted / Join Game while running) |
| `LobbyEnter_t` | 504 | Call result of `JoinLobby`, and also a callback |
| `LobbyDataUpdate_t` | 505 | Callback (a lobby's data arrived after `RequestLobbyData`, or changed) |
| `LobbyCreated_t` | 513 | Call result of `CreateLobby` |
| `SteamNetConnectionStatusChangedCallback_t` | 1221 | Callback (incoming connection requests and state changes) |

Fallback if the detours ever misbehave: inject a subclass of the abstract `Steamworks.Callback`/`CallResult` with Il2CppInterop and add it to `CallbackDispatcher.m_registeredCallbacks`/`m_registeredCallResults`. Not needed on the reference build.

## Native layouts

Blittable callback structs match the IL2CPP field offsets in `<Decompiled>/CSharp/Steamworks.NET/Steamworks/*_t.cs`. Structures holding `SteamNetworkingIdentity` or fixed character arrays do not: their IL2CPP offsets describe managed layouts. Those use the SDK 1.51 header layout: `SteamNetworkingIdentity` 136 bytes, `SteamNetConnectionInfo_t` 696 bytes (listen socket @144, state @176, end reason @180, end debug text @184), `SteamNetworkingConfigValue_t` 16 bytes, `SteamNetworkingMessage_t` data pointer @0 and size @8, quick status ping @4. The Steam probe verifies each of these against known values.

## Transport choices

- **Lobby**: two-slot, friends-only by default (invite-only on request). Lobby data carries `pbdmp_version`, `pbdmp_proto` and `pbdmp_build` (16 hex characters of SHA-256 over the full fingerprint) so a joiner sees a mismatch before dialling. The full Hello/Welcome fingerprint check still runs afterwards. The host also writes `pbdmp_level`, `pbdmp_mode` and `pbdmp_players` when they change.
- **Join a friend**: every 3 s while the screen is open, `GetFriendCount`/`GetFriendByIndex` (immediate friends) and `GetFriendGamePlayed` find friends in this app (`GetAppID`) with a lobby. `FriendGameInfo_t` is 24 bytes: game id @0, lobby @16. Steam reports a friends-only lobby there and hides an invite-only one. Each lobby's data is requested with `RequestLobbyData` at most every 10 s; `LobbyDataUpdate_t` (lobby @0, member @8, success @16) says it arrived. Lobbies without PBDMP data are left out, and a different version or build is shown but cannot be joined. Join uses the same path as an accepted invite.
- **Join Game while closed**: Steam starts the game with `+connect_lobby <id>`; PBDMP joins that lobby once the main menu and Steam are ready.
- **Carrier**: `ISteamNetworkingSockets` P2P on virtual port 0 through Steam's relay network. One reliable message carries one protocol frame; `MessageStream` presents the connection as the byte stream `CoopSession` already used for TCP, so the protocol is unchanged.
- **Tuning**: Steam's default 512 KB send buffer and 256 KB/s send rate throttle scene snapshot bursts, so connections use a 2 MB buffer and 512 KB/s. The joiner allows 60 seconds to connect, because the host only accepts on Unity's main thread and may be inside a loading screen.
- **Admission**: the host accepts only lobby members. A request that arrives before Steam has delivered the member list waits up to 10 seconds for it.
- **Closing**: connections close with linger so a final reject or drop reason reaches the peer. A peer's send can fail on the closed link before it reads that reason; the session gives its receiver 250 ms to report the stated reason first.

## Single-account verification

Steam cannot connect two sessions of one account across the relay, but it supports two in-process routes that exercise everything else: `CreateSocketPair` (a connected pair on the real carrier) and `ConnectP2P` to the local identity, which Steam routes into the local listen socket and reports through the normal status callbacks. `scripts/Test-Steam.ps1` uses both, then creates, inspects and leaves a private lobby. Only the cross-account relay hop needs a second player.

Sources: the files under `<GameDir>` and `<Decompiled>` named above; Steamworks SDK 1.51 headers (`steam_api_flat.h`, `isteamnetworkingsockets.h`, `steamnetworkingtypes.h`) for native layouts.
