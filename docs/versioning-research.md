# Versioning decision

Investigated and implemented October 20, 2026, starting with PBDMP 1.0.2. Repository source is the authority; the evidence below is managed testing and compilation, not a new native/two-PC playtest.

## Two maintained versions

| Version | Source of truth | Purpose |
| --- | --- | --- |
| Mod release (`1.0.2`) | [Directory.Build.props](../Directory.Build.props) | Both DLLs, plugin metadata, Steam adverts, connection handshake, reports and package names. Every distributed update gets a new version; peers must match exactly. |
| Internal save schema (`3`) | [SaveSchema.Current](../src/PBDMP.Network/SaveSchema.cs) | Campaign disk state and local Survival records. Changes independently when disk-format compatibility requires it. |

The network project [generates ModVersion.Value](../src/PBDMP.Network/PBDMP.Network.csproj) from the release property. [Plugin.Version](../src/PBDMP.Plugin/Plugin.cs) aliases that constant, so the release is no longer handwritten in both C# and the plugin project.

## Connection policy

There is no separately numbered protocol or cross-release negotiation. Both Hello and Welcome carry the exact mod release string plus the existing game/mod fingerprint. Both peers check them before gameplay exchange. Steam advertises the release and shortened fingerprint, and the friends list uses those checks. Earlier integer-version handshakes fail closed. [Codec](../src/PBDMP.Network/Protocol.cs), [handshake admission](../src/PBDMP.Network/CoopSession.cs), [Steam metadata](../src/PBDMP.Plugin/SteamService.cs), [friends-list checks](../src/PBDMP.Network/LobbyAdvert.cs).

Automatic game and DLL hashes remain file-integrity/compatibility checks, not additional maintained versions. They catch mixed DLLs, changed game code or differing rebuilds carrying the same release label. Packet bounds, sender authority and snapshot rules remain required. [Fingerprint generation](../src/PBDMP.Plugin/Plugin.cs), [network format](protocol.md).

## Disk compatibility

Campaign files retain schema 3 and record their creating mod version for diagnostics; a different mod release alone does not invalidate them. Schema, game and catalogue compatibility still apply. The save game key excludes mod DLL hashes. [Campaign storage](../src/PBDMP.Network/CoopSave.cs), [restoration](../src/PBDMP.Plugin/CoopSaveService.cs), [game-only key](../src/PBDMP.Plugin/NativeSaveBridge.cs).

Survival records now use the shared save schema rather than maintaining another counter. Supported original `Version: 1` files normalize in memory without rewriting or losing results; the next new result writes the current schema. [Record storage](../src/PBDMP.Network/SurvivalRecords.cs).

## Evidence

43 managed network scenarios pass, including exact release rejection over TCP/message carriers, client-side Welcome rejection, bounded/malformed/legacy handshakes, campaign saves from a previous mod release and original Survival-record preservation. Eight reporting checks pass. The plugin builds against the selected installation's real loader/interop with zero warnings/errors. See [verification](verification.md#versioning-simplification--october-20-2026) for commands and practical limits.
