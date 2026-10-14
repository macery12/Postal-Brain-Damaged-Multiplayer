using PBDMP.Network;
using PBDMP.Network.Steam;
using UnityEngine;

namespace PBDMP.Plugin;

// Single-account Steam verification at the main menu. It uses the real Steam client, carrier, lobby and
// callback tap; only the cross-account relay hop needs a second player.
public sealed partial class MultiplayerBehaviour
{
    private readonly bool _steamProbe = Environment.GetEnvironmentVariable("PBDMP_STEAM_PROBE") == "1";
    private int _steamStep;
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
            _steamDeadline = Time.unscaledTime + 90f; _steamStep = 1;
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
                ResetSession();
                steam.PrivateOnly = false;
                SteamAssert(steam.Phase == LobbyPhase.None && steam.Lobby == 0 && _session == null, "leaving releases the lobby and the P2P listener");
                Plugin.Logger.LogInfo("PBDMP STEAM PROBE PASS: callback tap, identity, carrier layouts and ordering, drop reason, P2P dial/admit/accept, lobby create/data/leave. Cross-account relay untested.");
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
