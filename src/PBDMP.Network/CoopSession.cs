using System.Net;

namespace PBDMP.Network;

public enum SessionRole { Offline, Host, Client }
public sealed record SessionSnapshot(SessionRole Role, bool Connected, string Status, PlayerPose? RemotePose,
    TravelState? Travel = null, ClientReady? Ready = null);

/// <summary>Two-player session over any reliable peer link. No Unity calls; one latest pose, bounded frames, monotonic async timeouts.</summary>
public sealed class CoopSession : IDisposable
{
    private readonly object _gate = new();
    private readonly string _gameFingerprint;
    private readonly string _modVersion;
    private readonly CancellationTokenSource _stop = new();
    private IPeerListener? _listener;
    private IPeerConnection? _peer;
    private PlayerPose _local = PlayerPose.Empty;
    private TravelState? _travel;
    private ClientReady? _ready;
    private SessionSnapshot _snapshot = new(SessionRole.Offline, false, "Offline", null);
    private bool _started;
    private string? _dropReason;
    private readonly Queue<WorldFrame> _worldOut = new();
    private readonly Queue<WorldFrame> _worldIn = new();
    public const int MaxWorldQueue = 4096;
    private readonly Queue<SavePage> _saveOut=new(), _saveIn=new();
    public bool PublishSave(SavePage page)
    {
        SaveProtocol.Validate(page);
        lock(_gate)
        {
            if(_snapshot.Role==SessionRole.Offline || SaveProtocol.FromClient(page.Command)!=(_snapshot.Role==SessionRole.Client)) throw new InvalidOperationException("Unauthorized save control.");
            if(!_snapshot.Connected || _saveOut.Count>=1024) return false;
            _saveOut.Enqueue(page with {Bytes=page.Bytes.ToArray()});return true;
        }
    }
    public SavePage[] DrainSaves()
    {lock(_gate) {var result=_saveIn.Take(128).ToArray();for(var i=0;i<result.Length;i++) _saveIn.Dequeue();return result;}}


    public bool PublishWorld(WorldFrame frame)
    {
        WorldProtocol.Validate(frame);
        lock (_gate)
        {
            var role = _snapshot.Role;
            if (role == SessionRole.Offline || WorldProtocol.FromClient(frame.Kind) != (role == SessionRole.Client))
                throw new InvalidOperationException("Unauthorized world publication.");
            if (!_snapshot.Connected) return false;
            // The caller retains an ordered pending frame until capacity returns.
            // Scene snapshots can exceed one transport window without dropping state.
            if (_worldOut.Count >= MaxWorldQueue) return false;
            var copy = frame with { Values = frame.Values?.ToArray(), Data = frame.Data?.ToArray() };
            _worldOut.Enqueue(copy); return true;
        }
    }
    public WorldFrame[] DrainWorld(int maximum = 512)
    {
        if (maximum is < 1 or > MaxWorldQueue) throw new ArgumentOutOfRangeException(nameof(maximum));
        lock (_gate)
        {
            var frames = new WorldFrame[Math.Min(maximum,_worldIn.Count)];
            for (var i=0;i<frames.Length;i++) frames[i]=_worldIn.Dequeue();
            return frames;
        }
    }

    public CoopSession(string gameFingerprint, string modVersion = ModVersion.Value)
    {
        Protocol.ValidateModVersion(modVersion);
        _gameFingerprint = gameFingerprint;
        _modVersion = modVersion;
    }

    public SessionSnapshot Snapshot { get { lock (_gate) return _snapshot; } }
    public void Publish(PlayerPose pose) { lock (_gate) _local = pose; }
    public void PublishTravel(TravelState state)
    {
        Protocol.Validate(state);
        lock (_gate)
        {
            if (_snapshot.Role != SessionRole.Host) throw new InvalidOperationException("Only P1 may publish travel.");
            _travel = state;
        }
    }
    public void PublishReady(ClientReady state)
    {
        lock (_gate)
        {
            if (_snapshot.Role != SessionRole.Client) throw new InvalidOperationException("Only P2 may publish readiness.");
            _ready = state;
        }
    }
    public void DropPeer(string reason)
    {
        lock (_gate)
        {
            if (_snapshot.Role != SessionRole.Host) throw new InvalidOperationException("Only host may drop a peer.");
            if (_peer == null) return;
            _dropReason = reason;
        }
    }

    public void StartHost(int port, IPAddress? bindAddress = null)
    {
        CheckStart(port);
        IPeerListener listener;
        try { listener = new TcpPeerListener(port, bindAddress); }
        catch { _started = false; throw; }
        Listen(listener, $"Hosting TCP port {port}; waiting for P2");
    }

    /// <summary>Hosts on a caller-built listener, which the session disposes with itself.</summary>
    public void StartHost(IPeerListener listener, string waiting)
    {
        CheckStart();
        Listen(listener, waiting);
    }

    public void StartJoin(IPAddress address, int port)
    {
        CheckStart(port);
        Connect(stop => TcpPeerConnection.ConnectAsync(address, port, stop), $"Connecting to {address}:{port}", 5);
    }

    /// <summary>Joins through a caller-supplied dial, allowing it <paramref name="connectSeconds"/> before the handshake.</summary>
    public void StartJoin(Func<CancellationToken, Task<IPeerConnection>> connect, string connecting, int connectSeconds)
    {
        CheckStart();
        Connect(connect, connecting, connectSeconds);
    }

    private void CheckStart(int port = 1)
    {
        if (_stop.IsCancellationRequested) throw new ObjectDisposedException(nameof(CoopSession));
        if (_started) throw new InvalidOperationException("Session already started.");
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        _started = true;
    }

    private void Listen(IPeerListener listener, string waiting)
    {
        _listener = listener;
        Set(new(SessionRole.Host, false, waiting, null));
        _ = AcceptLoop(listener, _stop.Token);
    }

    private void Connect(Func<CancellationToken, Task<IPeerConnection>> connect, string connecting, int connectSeconds)
    {
        Set(new(SessionRole.Client, false, connecting, null));
        _ = Join(connect, connectSeconds, _stop.Token);
    }

    private async Task AcceptLoop(IPeerListener listener, CancellationToken stop)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var client = await listener.AcceptAsync(stop).ConfigureAwait(false);
                if (Snapshot.Connected)
                {
                    using (client)
                    using (var deadline = Deadline(stop, 2))
                    {
                        try
                        {
                            var stream = client.Stream;
                            // Consume the bounded hello before closing: unread incoming data can reset
                            // a Windows TCP socket and discard the rejection instead of delivering it.
                            await Protocol.ReadAsync(stream, deadline.Token).ConfigureAwait(false);
                            await Protocol.WriteAsync(stream, new(MessageKind.Reject, Text: "Host is full (two players)."), deadline.Token).ConfigureAwait(false);
                        }
                        catch (Exception) when (!stop.IsCancellationRequested) { }
                    }
                    continue;
                }
                // Sequential admission prevents two simultaneous handshakes reserving the sole P2 slot.
                using (var deadline = Deadline(stop, 5))
                {
                    try
                    {
                        var stream = client.Stream;
                        var hello = await Protocol.ReadAsync(stream, deadline.Token).ConfigureAwait(false);
                        var error = CheckHello(hello, MessageKind.Hello);
                        if (error != null)
                        {
                            await Protocol.WriteAsync(stream, new(MessageKind.Reject, Text: error), deadline.Token).ConfigureAwait(false);
                            client.Dispose();
                            continue;
                        }
                        await Protocol.WriteAsync(stream, new(MessageKind.Welcome, _modVersion, _gameFingerprint), deadline.Token).ConfigureAwait(false);
                        lock (_gate)
                        {
                            if (stop.IsCancellationRequested) { client.Dispose(); return; }
                            _peer = client;
                            _snapshot = new(SessionRole.Host, true, "Connected: P1 host + P2", null);
                        }
                        _ = Exchange(client, SessionRole.Host, stop);
                    }
                    catch (Exception) when (!stop.IsCancellationRequested) { client.Dispose(); }
                    catch { client.Dispose(); throw; }
                }
            }
        }
        catch (Exception ex)
        {
            if (!stop.IsCancellationRequested) Set(new(SessionRole.Host, false, $"Host stopped: {ex.Message}", null));
        }
    }

    private async Task Join(Func<CancellationToken, Task<IPeerConnection>> connect, int connectSeconds, CancellationToken stop)
    {
        IPeerConnection? client = null;
        try
        {
            using (var deadline = Deadline(stop, connectSeconds))
                client = await connect(deadline.Token).ConfigureAwait(false);
            using (var deadline = Deadline(stop, 5))
            {
                var stream = client.Stream;
                await Protocol.WriteAsync(stream, new(MessageKind.Hello, _modVersion, _gameFingerprint), deadline.Token).ConfigureAwait(false);
                var welcome = await Protocol.ReadAsync(stream, deadline.Token).ConfigureAwait(false);
                if (welcome.Kind == MessageKind.Reject) throw new InvalidDataException(welcome.Text);
                var error = CheckHello(welcome, MessageKind.Welcome);
                if (error != null) throw new InvalidDataException(error);
            }
            lock (_gate)
            {
                if (stop.IsCancellationRequested) { client.Dispose(); return; }
                _peer = client;
                _snapshot = new(SessionRole.Client, true, "Connected: P2 joined P1", null);
            }
            await Exchange(client, SessionRole.Client, stop).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            client?.Dispose();
            var reason = ex is OperationCanceledException ? "Connection timed out" : ex.Message;
            if (!stop.IsCancellationRequested) Set(new(SessionRole.Client, false, $"Join failed: {reason}", null));
        }
    }

    private string? CheckHello(Packet packet, MessageKind expected)
    {
        if (packet.Kind != expected) return "Expected handshake.";
        if (!string.Equals(packet.Version, _modVersion, StringComparison.Ordinal))
            return expected == MessageKind.Hello
                ? $"Version mismatch: the host runs PBDMP {_modVersion}; you run {packet.Version}."
                : $"Version mismatch: the host runs PBDMP {packet.Version}; you run {_modVersion}.";
        if (!string.Equals(packet.Text, _gameFingerprint, StringComparison.Ordinal)) return "Game or PBDMP build mismatch. Both players need the same game and mod DLLs.";
        return null;
    }

    private async Task Exchange(IPeerConnection client, SessionRole role, CancellationToken stop)
    {
        using var connection = CancellationTokenSource.CreateLinkedTokenSource(stop);
        var stream = client.Stream;
        var sender = SendLoop(stream, role, connection.Token);
        var receiver = ReceiveLoop(stream, role, connection.Token);
        var reason = "Peer disconnected";
        try
        {
            var finished = await Task.WhenAny(sender, receiver).ConfigureAwait(false);
            // A closing peer's stated reason can still be queued inbound when our own send fails first.
            if (finished == sender && sender.IsFaulted)
                await Task.WhenAny(receiver, Task.Delay(250, stop)).ConfigureAwait(false);
            if (receiver.IsFaulted && receiver.Exception!.InnerException is InvalidDataException) finished = receiver;
            await finished.ConfigureAwait(false);
        }
        catch (Exception ex) { reason = ex is OperationCanceledException ? "Connection timed out" : ex.Message; }
        finally
        {
            connection.Cancel();
            client.Dispose();
            // Observe both workers; shutdown cannot leave an unobserved exception.
            try { await Task.WhenAll(sender, receiver).ConfigureAwait(false); } catch { }
            lock (_gate)
            {
                if (ReferenceEquals(_peer, client))
                {
                    reason = _dropReason ?? reason; _dropReason = null;
                    _peer = null;
                    _worldIn.Clear(); _worldOut.Clear(); _saveIn.Clear(); _saveOut.Clear();
                    if (!stop.IsCancellationRequested)
                        _snapshot = new(role, false, role == SessionRole.Host ? $"P2 left: {reason}. Waiting for P2" : $"Disconnected: {reason}", null);
                }
            }
        }
    }

    private async Task SendLoop(Stream stream, SessionRole role, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            PlayerPose pose;
            TravelState? travel; ClientReady? ready; string? drop;
            WorldFrame[] world; SavePage[] saves;
            lock (_gate)
            {
                pose = _local; travel = _travel; ready = _ready; drop = _dropReason;
                saves=_saveOut.Take(32).ToArray();for(var i=0;i<saves.Length;i++) _saveOut.Dequeue();
                world = new WorldFrame[Math.Min(512,_worldOut.Count)];
                for (var i=0;i<world.Length;i++) world[i]=_worldOut.Dequeue();
            }
            using (var deadline = Deadline(stop, 3))
            {
                if (role == SessionRole.Host && drop != null)
                {
                    await Protocol.WriteAsync(stream, new(MessageKind.Reject, Text: drop), deadline.Token).ConfigureAwait(false);
                    throw new IOException(drop);
                }
                if (role == SessionRole.Host && travel != null)
                    await Protocol.WriteAsync(stream, new(MessageKind.Travel, Travel: travel), deadline.Token).ConfigureAwait(false);
                if (role == SessionRole.Client && ready != null)
                    await Protocol.WriteAsync(stream, new(MessageKind.Ready, Ready: ready), deadline.Token).ConfigureAwait(false);
                await Protocol.WriteAsync(stream, new(MessageKind.Pose, Pose: pose), deadline.Token).ConfigureAwait(false);
                foreach(var page in saves) await Protocol.WriteAsync(stream,new(MessageKind.Save,Save:page),deadline.Token).ConfigureAwait(false);
                foreach (var frame in world)
                    await Protocol.WriteAsync(stream, new(MessageKind.World, World: frame), deadline.Token).ConfigureAwait(false);
            }
            await Task.Delay(50, stop).ConfigureAwait(false); // 20 Hz; no queued old poses.
        }
    }

    private async Task ReceiveLoop(Stream stream, SessionRole role, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            using var deadline = Deadline(stop, 10);
            var packet = await Protocol.ReadAsync(stream, deadline.Token).ConfigureAwait(false);
            if(packet.Kind==MessageKind.Save)
            {
                var page=packet.Save!;
                if(SaveProtocol.FromClient(page.Command)!=(role==SessionRole.Host)) throw new InvalidDataException("Peer sent unauthorized save control.");
                lock(_gate) {if(_saveIn.Count>=1024) throw new InvalidDataException("Save control queue exceeded its bound.");_saveIn.Enqueue(page);}
                continue;
            }
            if(packet.Kind==MessageKind.World)
            {
                var world=packet.World!;
                if(WorldProtocol.FromClient(world.Kind)!=(role==SessionRole.Host)) throw new InvalidDataException("Peer sent unauthorized world packet.");
                while(true)
                {
                    lock(_gate)
                    {
                        if(stop.IsCancellationRequested) return;
                        if(_worldIn.Count<MaxWorldQueue) {_worldIn.Enqueue(world);break;}
                    }
                    // Apply TCP backpressure while the native main thread builds
                    // or restores its scene. Keep the receive queue bounded.
                    await Task.Delay(2,stop).ConfigureAwait(false);
                }
                continue;
            }
            lock (_gate)
            {
                if (stop.IsCancellationRequested) return;
                _snapshot = packet.Kind switch
                {
                    MessageKind.Pose => _snapshot with { RemotePose = packet.Pose },
                    MessageKind.Travel when role == SessionRole.Client => _snapshot with { Travel = packet.Travel },
                    MessageKind.Ready when role == SessionRole.Host => _snapshot with { Ready = packet.Ready },
                    MessageKind.Reject when role == SessionRole.Client => throw new InvalidDataException(packet.Text),
                    _ => throw new InvalidDataException("Peer sent unauthorized session packet.")
                };
            }
        }
    }

    private static CancellationTokenSource Deadline(CancellationToken stop, int seconds)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop);
        deadline.CancelAfter(TimeSpan.FromSeconds(seconds));
        return deadline;
    }

    private void Set(SessionSnapshot snapshot)
    {
        lock (_gate) if (!_stop.IsCancellationRequested) _snapshot = snapshot;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_stop.IsCancellationRequested) return;
            _stop.Cancel();
            _listener?.Dispose();
            _peer?.Dispose();
            _peer = null;
            _worldIn.Clear(); _worldOut.Clear(); _saveIn.Clear(); _saveOut.Clear();
            _snapshot = new(SessionRole.Offline, false, "Offline", null);
        }
        // CTS kept alive until async workers finish using the token; session is small and disposable.
    }
}
