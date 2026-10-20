using Hyperstrange.PBD;
using Hyperstrange.PBD.UI.Menus;
using PBDMP.Network;
using PBDMP.Network.Steam;
using UnityEngine;

namespace PBDMP.Plugin;

// Single-account Steam verification at the main menu. It uses the real Steam client, carrier, lobby and
// callback tap; only the cross-account relay hop needs a second player.
public sealed partial class MultiplayerBehaviour
{
    private readonly bool _steamProbe = Environment.GetEnvironmentVariable("PBDMP_STEAM_PROBE") == "1";
    private int _steamStep, _probeUpdates;
    private FriendHost? _probeOwn;
    private float _steamDeadline, _steamNext;
    private CoopSession? _probeHost, _probeClient;
    private SteamP2PPipe? _probePipe;
    private readonly List<WorldFrame> _probeFrames = new();

    private void RunSteamProbe()
    {
        if (_steamStep == 99) return;
        if (_steamStep == 0)
        {
            if (!_buttonVisibleLogged) return;
            _steamDeadline = Time.unscaledTime + 150f; _steamStep = 1;
        }
        if (Time.unscaledTime > _steamDeadline) throw new TimeoutException($"Steam probe step {_steamStep} timed out. {_steam?.Unavailable}");
        if (Time.unscaledTime < _steamNext) return;
        var steam = _steam!;
        switch (_steamStep)
        {
            case 1:
                if (!steam.Available) return;
                SteamAssert(SteamCallbackTap.Installed && steam.LocalId != 0 && SteamNative.LocalName.Length > 0,
                    "Steam identity read from the game's running client; callback tap installed");
                var (first, second) = SteamP2P.LoopbackPair();
                _probePipe = first;
                SteamAssert(SteamNative.TryGetConnectionInfo(first.Connection, out var info) && info.State == SteamNative.StateConnected &&
                    info.Remote == steam.LocalId && info.ListenSocket == 0, "connection info layout: state, remote identity and listen socket");
                _probeHost = new(Plugin.Fingerprint);
                _probeHost.StartHost(new SinglePeerListener(new MessagePeerConnection(first)), "Probe host");
                _probeClient = new(Plugin.Fingerprint);
                _probeClient.StartJoin(_ => Task.FromResult<IPeerConnection>(new MessagePeerConnection(second)), "Probe client", 5);
                _probeHost.Publish(new("PBDMP_STEAM_PROBE", true, 1, 2, 3, 4, 1.8f));
                _probeClient.Publish(new("PBDMP_STEAM_PROBE", true, 5, 6, 7, 8, 1.8f));
                _steamStep = 2;
                break;
            case 2:
                if (_probeHost!.Snapshot.RemotePose?.X != 5 || _probeClient!.Snapshot.RemotePose?.X != 1) return;
                SteamAssert(true, "session handshake and poses cross the real Steam carrier");
                for (ulong i = 1; i <= 300; i++) _probeHost.PublishWorld(new(WorldKind.MissionState, 1, i, Key: "probe", Values: new[] { (float)i }));
                _steamStep = 3;
                break;
            case 3:
                _probeFrames.AddRange(_probeClient!.DrainWorld());
                if (_probeFrames.Count < 300) return;
                SteamAssert(_probeFrames.Select(f => f.Sequence).SequenceEqual(Enumerable.Range(1, 300).Select(i => (ulong)i)),
                    "300 ordered world frames over the Steam carrier");
                SteamAssert(_probePipe!.Ping >= 0, $"quick status layout: ping {_probePipe.Ping} ms");
                _probeHost!.DropPeer("Probe drop reason.");
                _steamStep = 4;
                break;
            case 4:
                if (_probeClient!.Snapshot.Connected) return;
                SteamAssert(_probeClient.Snapshot.Status.Contains("Probe drop reason"), "a host's stated drop reason survives the Steam close");
                _probeClient.Dispose(); _probeHost!.Dispose(); _probeClient = _probeHost = null; _probePipe = null;
                // Steam routes a P2P dial to our own identity into our own listen socket, so the real join path
                // (options, status callbacks through the tap, admission and accept) runs without a second account.
                var listener = new SteamP2PListener(id => id == steam.LocalId, steam.Opened);
                steam.Listener = listener;
                _probeHost = new(Plugin.Fingerprint);
                _probeHost.StartHost(listener, "Probe host");
                _probeClient = new(Plugin.Fingerprint);
                _probeClient.StartJoin(stop => SteamP2P.ConnectAsync(steam.LocalId, stop), "Probe client", 20);
                _probeHost.Publish(new("PBDMP_STEAM_PROBE", true, 1, 2, 3, 4, 1.8f));
                _probeClient.Publish(new("PBDMP_STEAM_PROBE", true, 5, 6, 7, 8, 1.8f));
                _steamStep = 5;
                break;
            case 5:
                if (_probeClient!.Snapshot.Status.StartsWith("Join failed", StringComparison.Ordinal))
                    SteamAssert(false, "self-dial through the P2P listen socket: " + _probeClient.Snapshot.Status);
                if (_probeHost!.Snapshot.RemotePose?.X != 5 || _probeClient.Snapshot.RemotePose?.X != 1) return;
                SteamAssert(true, "dial, status callbacks through the tap, admission and accept carry a full session");
                _probeClient.Dispose(); _probeHost.Dispose(); _probeClient = _probeHost = null;
                steam.Listener = null;
                steam.PrivateOnly = true;
                SetPanel(true); HostSteam();
                _steamStep = 6;
                break;
            case 6:
                if (steam.Phase == LobbyPhase.Creating) return;
                SteamAssert(steam.Phase == LobbyPhase.Hosting && steam.Listener != null && _session?.Snapshot.Role == SessionRole.Host,
                    "lobby created through the callback tap; P2P host listening");
                SteamAssert(SteamNative.GetLobbyData(steam.Lobby, SteamService.BuildKey) == Plugin.LobbyBuild &&
                    SteamNative.LobbyOwner(steam.Lobby) == steam.LocalId && steam.IsMember(steam.LocalId), "lobby advertises this build and lists its owner");
                SteamAssert(_view!.InviteButton.interactable && _view.DisconnectButton.interactable && !_view.SteamHostButton.interactable,
                    "hosting enables invite and leave");
                _steamNext = Time.unscaledTime + 3f; _steamStep = 7;
                break;
            case 7:
                CaptureUI("steam-lobby.png");
                Plugin.Logger.LogInfo("STEAM PROBE: local avatar " + (steam.Avatar(steam.LocalId) != null ? "loaded" : "not available (none set, or still loading)"));
                _view!.Hud.Toast("Probe notification");
                SetPanel(false);
                _steamNext = Time.unscaledTime + 0.5f; _steamStep = 8;
                break;
            case 8:
                CaptureUI("steam-hud.png");
                _steamNext = Time.unscaledTime + 0.5f; _steamStep = 20;
                break;
            case 20:
                // Join a friend, on one account: this lobby read the way a friend's copy reads it.
                if (SteamNative.GetLobbyData(steam.Lobby, SteamService.ModeKey) != LobbyAdvert.ModeMenu) return;
                SteamAssert(SteamNative.GetLobbyData(steam.Lobby, SteamService.PlayersKey) == "1", "the lobby advertises where the host is (menu) and its player count");
                _probeUpdates = steam.LobbyDataUpdates; steam.ForgetLobbyData();
                SteamAssert(SteamNative.RequestLobbyData(steam.Lobby), "Steam accepts a lobby data request");
                _steamStep = 21;
                break;
            case 21:
            {
                if (steam.LobbyDataUpdates <= _probeUpdates) return;
                var own = steam.Describe(steam.LocalId, steam.Lobby);
                SteamAssert(own?.Advert is { Mode: LobbyAdvert.ModeMenu, Players: 1 } advert && advert.Problem(Plugin.Version, Plugin.LobbyBuild) == null,
                    "lobby data arrives through the tap's LobbyDataUpdate and reads as a compatible PBDMP game");
                SteamAssert(SteamNative.AppId == 1359980u, "the friend scan matches friends' games against this app id");
                var hosting = steam.FriendsHosting();
                Plugin.Logger.LogInfo($"STEAM PROBE: {SteamNative.FriendCount} friends listed; {hosting.Count} in a joinable PBDMP lobby.");
                _probeOwn = own;
                OpenCoopMenu();
                _coopJoin!.TestHosts = new[] { own!, own! with { Friend = 1, Lobby = 2, Name = "Older friend", Advert = own.Advert! with { Version = "0.0.1" } } };
                _coopMenu!.JoinFriend.onClick.Invoke();
                _steamNext = Time.unscaledTime + 1.5f; _steamStep = 22;
                break;
            }
            case 22:
            {
                var own = _probeOwn!; var name = own.Name.Length > 0 ? own.Name : "Steam friend"; var join = _coopJoin!;
                SteamAssert(Shown("MENU_PBDMP_CoopJoin") && join.EntryCount == 2, "Join a friend lists friends' PBDMP games as the game's own entries");
                SteamAssert(join.EntryTextForTest(0) == name + " | In the Multiplayer menu | 1 of 2 players" &&
                    join.EntryTextForTest(1) == "Older friend | In the Multiplayer menu | Different PBDMP version (0.0.1)", "entries show the friend, where they are, players or why they cannot be joined");
                SteamAssert(join.Selected?.Lobby == steam.Lobby && join.JoinButton.interactable, "the joinable game is listed and selected first");
                join.Select(join.TestHosts![1]);
                SteamAssert(!join.JoinButton.interactable, "Join is disabled for a different PBDMP version");
                join.Select(own);
                Shot("steam-join-friend.png");
                join.JoinButton.onClick.Invoke();
                _steamNext = Time.unscaledTime + 0.8f; _steamStep = 23;
                break;
            }
            case 23:
                SteamAssert(_view!.Prompt.Open && _view.Prompt.Message.StartsWith("Join ", StringComparison.Ordinal) && steam.Phase == LobbyPhase.Hosting,
                    "Join while hosting asks first, through the same path as a Steam invite");
                Shot("steam-join-prompt.png");
                _view.Prompt.CancelButton.onClick.Invoke();
                _steamNext = Time.unscaledTime + 0.8f; _steamStep = 24;
                break;
            case 24:
                SteamAssert(!_view!.Prompt.Open && steam.Phase == LobbyPhase.Hosting && _session?.Snapshot.Role == SessionRole.Host, "Stay keeps hosting");
                _coopJoin!.TestHosts = Array.Empty<FriendHost>(); _coopJoin.RefreshButton.onClick.Invoke();
                SteamAssert(_coopJoin.EntryCount == 0 && _coopJoin.EmptyText.StartsWith("No friends are hosting", StringComparison.Ordinal), "an empty list says nobody is hosting");
                _coopJoin.TestHosts = null; _coopJoin.RefreshButton.onClick.Invoke();
                Plugin.Logger.LogInfo($"STEAM PROBE: the real friend scan through the screen lists {_coopJoin.EntryCount} games.");
                _coopJoin.DirectButton.onClick.Invoke();
                SteamAssert(_open && _view.DirectOpen, "Direct IP... opens the Direct IP join dialog");
                _view.ShowDirect(false); SetPanel(false);
                _coopJoin.BackButton.onClick.Invoke();
                _steamNext = Time.unscaledTime + 1.2f; _steamStep = 25;
                break;
            case 25:
                SteamAssert(Shown("MENU_PBDMP_Multiplayer"), "Back from Join a friend returns to the co-op menu");
                // One press from offline: Survival co-op waits for Steam to create the lobby, then opens the arena menu.
                ResetSession();
                _coopMenu!.Survival.onClick.Invoke();
                _steamStep = 26;
                break;
            case 26:
                if (!Shown("MENU_Horde")) return;
                SteamAssert(steam.Phase == LobbyPhase.Hosting && _steamSession && _session?.Snapshot.Role == SessionRole.Host,
                    "one Survival co-op press hosts a Steam lobby and opens the game's Survival menu");
                MenuManager.Close();
                _steamNext = Time.unscaledTime + 1.2f; _steamStep = 27;
                break;
            case 27:
                SteamAssert(Shown("MENU_PBDMP_Multiplayer"), "Back from the Survival menu returns to the co-op menu");
                MenuManager.Close();
                _steamNext = Time.unscaledTime + 1f; _steamStep = 30;
                break;
            case 30:
                ResetSession();
                steam.PrivateOnly = false;
                SteamAssert(steam.Phase == LobbyPhase.None && steam.Lobby == 0 && _session == null, "leaving releases the lobby and the P2P listener");
                Plugin.Logger.LogInfo("PBDMP STEAM PROBE PASS: callback tap, identity, carrier layouts and ordering, drop reason, P2P dial/admit/accept, lobby create/data/leave, Join a friend. Cross-account relay and joining untested.");
                _steamStep = 99;
                break;
        }
    }

    private static void SteamAssert(bool result, string label)
    {
        if (!result) throw new InvalidOperationException("Steam probe assertion failed: " + label);
        Plugin.Logger.LogInfo("STEAM PROBE PASS: " + label);
    }

    // Hands the session one prepared connection, then idles like a listener with nobody else calling.
    private sealed class SinglePeerListener : IPeerListener
    {
        private IPeerConnection? _connection;
        public SinglePeerListener(IPeerConnection connection) => _connection = connection;
        public async Task<IPeerConnection> AcceptAsync(CancellationToken stop)
        {
            if (Interlocked.Exchange(ref _connection, null) is { } connection) return connection;
            await Task.Delay(Timeout.Infinite, stop).ConfigureAwait(false);
            throw new OperationCanceledException(stop);
        }
        public void Dispose() => Interlocked.Exchange(ref _connection, null)?.Dispose();
    }
}
