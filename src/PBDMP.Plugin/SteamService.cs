using BepInEx;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using PBDMP.Network;
using PBDMP.Network.Steam;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PBDMP.Plugin;

internal enum LobbyPhase { None, Creating, Hosting, Joining, Joined }

/// <summary>Main-thread Steam lobby and identity state. Reads the callback tap; never pumps Steam itself.</summary>
internal sealed class SteamService : IDisposable
{
    internal const string VersionKey = "pbdmp_version", ProtocolKey = "pbdmp_proto", BuildKey = "pbdmp_build";
    private readonly Action _hosted;
    private readonly Action<ulong> _entered;
    private readonly Action<ulong, ulong> _joinRequested;
    private readonly Action<string> _failed, _notice;
    private readonly Dictionary<ulong, (Texture2D? Texture, float Retry, int Tries)> _avatars = new();
    private ulong _pending;
    private float _deadline, _nextStart, _nextMembers;
    private volatile SteamP2PPipe? _pipe;

    internal bool Available { get; private set; }
    internal string Unavailable { get; private set; } = "Checking Steam...";
    internal LobbyPhase Phase { get; private set; }
    internal ulong Lobby { get; private set; }
    internal ulong LocalId { get; private set; }
    internal ulong Partner { get; private set; }
    internal bool InviteOnly => Plugin.InviteOnly.Value || PrivateOnly;
    /// <summary>Keeps diagnostic lobbies out of friends' Join Game lists without touching the player's setting.</summary>
    internal bool PrivateOnly { get; set; }
    internal SteamP2PListener? Listener { get; set; }
    internal int Ping => _pipe?.Ping ?? -1;

    internal SteamService(Action hosted, Action<ulong> entered, Action<ulong, ulong> joinRequested, Action<string> failed, Action<string> notice)
    { _hosted = hosted; _entered = entered; _joinRequested = joinRequested; _failed = failed; _notice = notice; }

    /// <summary>Records the carrier of the current session; called from Steam or worker threads.</summary>
    internal void Opened(SteamP2PPipe pipe) => _pipe = pipe;

    internal void Tick()
    {
        if (!Available) { TryStart(); return; }
        while (SteamCallbackTap.TryDequeue(out var steamEvent)) Handle(steamEvent);
        if (Phase is LobbyPhase.Creating or LobbyPhase.Joining && Time.unscaledTime > _deadline)
        {
            var creating = Phase == LobbyPhase.Creating;
            Leave();
            _failed(creating ? "Steam did not create the lobby in time. Try again." : "Steam did not answer the join request in time.");
        }
        if (Lobby != 0 && Time.unscaledTime >= _nextMembers) { _nextMembers = Time.unscaledTime + 0.5f; TrackMembers(); }
        Listener?.Poll();
    }

    private void TryStart()
    {
        if (Time.unscaledTime < _nextStart) return;
        _nextStart = Time.unscaledTime + 1f;
        var path = Path.Combine(Paths.GameRootPath, "POSTAL Brain Damaged_Data", "Plugins", "x86_64", "steam_api64.dll");
        if (!File.Exists(path)) { Unavailable = "This copy of the game has no Steam support. Use Direct IP."; _nextStart = float.MaxValue; return; }
        var module = SteamNative.Bind(path);
        if (!SteamNative.Running) { Unavailable = "Steam is not running for this game. Start it from Steam, or use Direct IP."; return; }
        SteamCallbackTap.Install(module);
        LocalId = SteamNative.LocalId;
        Available = true; Unavailable = "";
        Plugin.Logger.LogInfo("Steam available for multiplayer.");
    }

    private void Handle(SteamEvent steamEvent)
    {
        switch (steamEvent.Id)
        {
            case SteamNative.LobbyCreatedCallback:
            {
                var (result, lobby) = SteamNative.ReadLobbyCreated(steamEvent.Data);
                if (Phase != LobbyPhase.Creating) { if (result == SteamNative.ResultOk && lobby != 0) SteamNative.LeaveLobby(lobby); return; }
                if (steamEvent.Failed || result != SteamNative.ResultOk || lobby == 0)
                { Leave(); _failed($"Steam could not create a lobby (result {result})."); return; }
                Lobby = lobby; Phase = LobbyPhase.Hosting;
                SteamNative.SetLobbyData(lobby, VersionKey, Plugin.Version);
                SteamNative.SetLobbyData(lobby, ProtocolKey, Protocol.Version.ToString());
                SteamNative.SetLobbyData(lobby, BuildKey, Plugin.LobbyBuild);
                Plugin.Logger.LogInfo("Steam lobby created" + (InviteOnly ? " (invite only)." : " (friends only)."));
                _hosted();
                return;
            }
            case SteamNative.LobbyEnterCallback:
            {
                // Steam reports entering as both a call result and a callback, and also for a lobby we just created.
                var (lobby, response) = SteamNative.ReadLobbyEnter(steamEvent.Data);
                if (Phase != LobbyPhase.Joining || lobby != _pending)
                {
                    if (lobby != Lobby && response == SteamNative.EnterSuccess && Phase != LobbyPhase.Creating) SteamNative.LeaveLobby(lobby);
                    return;
                }
                if (steamEvent.Failed || response != SteamNative.EnterSuccess) { Phase = LobbyPhase.None; _pending = 0; _failed(EnterFailure(response)); return; }
                Lobby = lobby; Phase = LobbyPhase.Joined; _pending = 0;
                var mismatch = Mismatch(lobby);
                if (mismatch != null) { Leave(); _failed(mismatch); return; }
                var owner = SteamNative.LobbyOwner(lobby);
                Plugin.Logger.LogInfo("Entered a compatible Steam lobby; connecting to its host.");
                _entered(owner);
                return;
            }
            case SteamNative.LobbyJoinRequestedCallback:
            {
                var (lobby, friend) = SteamNative.ReadLobbyJoinRequested(steamEvent.Data);
                if (lobby != 0 && lobby != Lobby) _joinRequested(lobby, friend);
                return;
            }
            case SteamNative.ConnectionStatusChangedCallback:
                Listener?.OnStatusChanged(SteamNative.ReadStatusChangedConnection(steamEvent.Data));
                return;
        }
    }

    internal void Host()
    {
        if (!Available) { _failed(Unavailable); return; }
        Leave();
        if (SteamNative.CreateLobby(InviteOnly ? SteamNative.LobbyPrivate : SteamNative.LobbyFriendsOnly, 2) == 0)
        { _failed("Steam refused to create a lobby."); return; }
        Phase = LobbyPhase.Creating; _deadline = Time.unscaledTime + 15;
    }

    internal void Join(ulong lobby)
    {
        if (!Available) { _failed(Unavailable); return; }
        Leave();
        if (SteamNative.JoinLobby(lobby) == 0) { _failed("Steam refused the join request."); return; }
        _pending = lobby; Phase = LobbyPhase.Joining; _deadline = Time.unscaledTime + 20;
    }

    internal void Leave()
    {
        if (Lobby != 0) SteamNative.LeaveLobby(Lobby);
        Lobby = 0; Partner = 0; _pending = 0; Phase = LobbyPhase.None;
        Listener = null; _pipe = null;
    }

    internal void Invite()
    {
        if (Phase != LobbyPhase.Hosting) return;
        if (!SteamNative.OverlayEnabled)
        { _failed("The Steam overlay is disabled, so the invite list cannot open. Your friend can use Join Game on your name in their Steam friends list."); return; }
        SteamNative.OpenInviteDialog(Lobby);
    }

    internal void TogglePrivacy()
    {
        Plugin.InviteOnly.Value = !Plugin.InviteOnly.Value;
        if (Phase == LobbyPhase.Hosting) SteamNative.SetLobbyType(Lobby, InviteOnly ? SteamNative.LobbyPrivate : SteamNative.LobbyFriendsOnly);
    }

    internal bool IsMember(ulong id) => Lobby != 0 && SteamNative.LobbyMembers(Lobby).Contains(id);

    internal string Name(ulong id)
    {
        if (!Available || id == 0) return "";
        var name = id == LocalId ? SteamNative.LocalName : SteamNative.NameOf(id);
        return name.Length > 32 ? name[..29] + "..." : name;
    }

    /// <summary>Medium Steam avatar, kept across scene unloads. Null while Steam is still fetching it.</summary>
    internal Texture2D? Avatar(ulong id)
    {
        if (!Available || id == 0) return null;
        _avatars.TryGetValue(id, out var cached);
        if (cached.Texture != null) return cached.Texture;
        if (cached.Tries >= 30 || Time.unscaledTime < cached.Retry) return null;
        var pixels = SteamNative.Avatar(id, out var width, out var height);
        if (pixels == null) { _avatars[id] = (null, Time.unscaledTime + 1f, cached.Tries + 1); return null; }
        // Steam rows run top-down; Unity's run bottom-up.
        var flipped = new byte[pixels.Length]; var row = width * 4;
        for (var y = 0; y < height; y++) Buffer.BlockCopy(pixels, y * row, flipped, (height - 1 - y) * row, row);
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            { name = "PBDMP Steam avatar", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        texture.LoadRawTextureData((Il2CppStructArray<byte>)flipped); texture.Apply(false, true);
        _avatars[id] = (texture, 0, cached.Tries + 1);
        return texture;
    }

    private void TrackMembers()
    {
        var members = SteamNative.LobbyMembers(Lobby);
        var partner = members.FirstOrDefault(id => id != LocalId);
        if (partner == Partner) return;
        if (Partner != 0) _notice(Name(Partner) + " left the lobby.");
        if (partner != 0 && Phase == LobbyPhase.Hosting) _notice(Name(partner) + " joined the lobby.");
        Partner = partner;
    }

    private static string? Mismatch(ulong lobby)
    {
        var version = SteamNative.GetLobbyData(lobby, VersionKey);
        var protocol = SteamNative.GetLobbyData(lobby, ProtocolKey);
        if (version.Length == 0) return "That Steam lobby was not created by PBDMP.";
        if (version != Plugin.Version || protocol != Protocol.Version.ToString())
            return $"Version mismatch: the host runs PBDMP {version} (protocol {protocol}); you run {Plugin.Version} (protocol {Protocol.Version}).";
        if (SteamNative.GetLobbyData(lobby, BuildKey) != Plugin.LobbyBuild)
            return "Build mismatch: both players need the exact same PBDMP files and the same game version.";
        return null;
    }

    private static string EnterFailure(int response) => response switch
    {
        2 => "That lobby no longer exists.",
        3 => "You are not allowed to join that lobby.",
        4 => "That lobby is full.",
        6 => "You are banned from that lobby.",
        7 => "Limited Steam accounts cannot join lobbies.",
        _ => $"Steam could not join the lobby (response {response})."
    };

    public void Dispose()
    {
        if (Available) Leave();
        foreach (var avatar in _avatars.Values) if (avatar.Texture != null) Object.Destroy(avatar.Texture);
        _avatars.Clear();
    }
}
