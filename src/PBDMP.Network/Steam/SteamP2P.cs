using System.Threading.Channels;

namespace PBDMP.Network.Steam;

/// <summary>One relayed Steam connection carrying reliable ordered messages. Steam's own threads keep it alive.</summary>
public sealed class SteamP2PPipe : IMessagePipe
{
    private readonly uint _connection;
    private readonly IntPtr[] _scratch = new IntPtr[64];
    private long _nextCheck;
    private int _closed;
    private volatile int _ping = -1;

    public ulong Remote { get; }
    public uint Connection => _connection;
    public int Ping => _ping;

    public SteamP2PPipe(uint connection, ulong remote) { _connection = connection; Remote = remote; }

    public bool TrySend(ReadOnlySpan<byte> message)
    {
        var result = SteamNative.Send(_connection, message);
        if (result == SteamNative.ResultOk) return true;
        // Steam's send buffer is full; the caller retries once it has drained.
        if (result == SteamNative.ResultLimitExceeded) return false;
        throw new IOException(Ended());
    }

    public void Receive(List<byte[]> into)
    {
        var count = SteamNative.Receive(_connection, _scratch, into);
        if (count < 0) throw new IOException(Ended());
        // Messages queued before a close stay readable, so the state only matters once the queue is empty.
        if (count > 0 || Environment.TickCount64 < _nextCheck) return;
        _nextCheck = Environment.TickCount64 + 250;
        if (!SteamNative.TryGetConnectionInfo(_connection, out var info) ||
            info.State is SteamNative.StateNone or SteamNative.StateClosedByPeer or SteamNative.StateProblemDetectedLocally)
            throw new IOException(Ended());
        _ping = SteamNative.Ping(_connection);
    }

    private string Ended()
    {
        if (!SteamNative.TryGetConnectionInfo(_connection, out var info)) return "Steam connection closed.";
        var detail = string.IsNullOrWhiteSpace(info.EndDebug) ? "" : " " + info.EndDebug.Trim();
        return info.State == SteamNative.StateClosedByPeer ? "Peer closed the Steam connection." + detail
            : $"Steam connection lost ({info.EndReason}).{detail}";
    }

    public void Dispose()
    {
        // Linger lets a final reject or drop reason reach the peer before the link goes away.
        if (Interlocked.Exchange(ref _closed, 1) == 0) SteamNative.Close(_connection, "PBDMP session ended", true);
    }
}

/// <summary>Accepts relayed Steam connections from admitted players. Status changes are fed in from the game's callback pump.</summary>
public sealed class SteamP2PListener : IPeerListener
{
    private readonly uint _socket;
    private readonly Func<ulong, bool> _admit;
    private readonly Action<SteamP2PPipe>? _opened;
    private readonly Channel<IPeerConnection> _incoming = Channel.CreateUnbounded<IPeerConnection>();
    private readonly HashSet<uint> _handed = new();
    private readonly Dictionary<uint, (ulong Remote, long Until)> _waiting = new();
    private int _disposed;
    private const int AdmitMilliseconds = 10_000;

    public SteamP2PListener(Func<ulong, bool> admit, Action<SteamP2PPipe>? opened = null)
    {
        _admit = admit; _opened = opened;
        _socket = SteamNative.CreateListenSocket(SteamP2P.Tuning);
        if (_socket == 0) throw new IOException("Steam could not open a P2P listen socket.");
    }

    public void OnStatusChanged(uint connection)
    {
        if (_disposed != 0 || !SteamNative.TryGetConnectionInfo(connection, out var info) || info.ListenSocket != _socket) return;
        switch (info.State)
        {
            case SteamNative.StateConnecting:
                // Lobby membership can reach the host after the joiner's connection request does.
                if (!_admit(info.Remote)) _waiting[connection] = (info.Remote, Environment.TickCount64 + AdmitMilliseconds);
                else Accept(connection);
                break;
            case SteamNative.StateConnected:
                lock (_handed) if (!_handed.Add(connection)) return;
                var pipe = new SteamP2PPipe(connection, info.Remote);
                _opened?.Invoke(pipe);
                if (!_incoming.Writer.TryWrite(new MessagePeerConnection(pipe))) pipe.Dispose();
                break;
            case SteamNative.StateClosedByPeer:
            case SteamNative.StateProblemDetectedLocally:
                // A pipe releases its own handle; one that never reached a pipe is released here.
                _waiting.Remove(connection);
                lock (_handed) if (_handed.Contains(connection)) return;
                SteamNative.Close(connection, "", false);
                break;
        }
    }

    /// <summary>Re-checks connection requests that arrived before their sender was admitted. Same thread as <see cref="OnStatusChanged"/>.</summary>
    public void Poll()
    {
        if (_disposed != 0 || _waiting.Count == 0) return;
        foreach (var (connection, (remote, until)) in _waiting.ToArray())
        {
            if (_admit(remote)) { _waiting.Remove(connection); Accept(connection); }
            else if (Environment.TickCount64 > until) { _waiting.Remove(connection); SteamNative.Close(connection, "Not a member of this lobby", false); }
        }
    }

    private static void Accept(uint connection)
    {
        if (SteamNative.Accept(connection) != SteamNative.ResultOk) SteamNative.Close(connection, "Accept failed", false);
    }

    public async Task<IPeerConnection> AcceptAsync(CancellationToken stop) =>
        await _incoming.Reader.ReadAsync(stop).ConfigureAwait(false);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _incoming.Writer.TryComplete();
        while (_incoming.Reader.TryRead(out var unclaimed)) unclaimed.Dispose();
        foreach (var connection in _waiting.Keys) SteamNative.Close(connection, "Host closed", false);
        _waiting.Clear();
        SteamNative.CloseListenSocket(_socket);
    }
}

public static class SteamP2P
{
    /// <summary>Scene snapshots are bursts of small reliable frames; Steam's defaults (512 KB buffer, 256 KB/s) throttle them.</summary>
    internal static readonly SteamOption[] Tuning =
    {
        new(SteamNative.ConfigSendBufferSize, 2 << 20),
        new(SteamNative.ConfigSendRateMin, 512 << 10),
        new(SteamNative.ConfigSendRateMax, 512 << 10),
    };

    /// <summary>Allowance for <see cref="ConnectAsync"/>: a host inside a loading screen only accepts on its next frame.</summary>
    public const int ConnectSeconds = 60;

    public static async Task<IPeerConnection> ConnectAsync(ulong host, CancellationToken stop, Action<SteamP2PPipe>? opened = null)
    {
        var connection = SteamNative.Connect(host, Tuning.Append(new(SteamNative.ConfigTimeoutInitial, ConnectSeconds * 1000)).ToArray());
        if (connection == 0) throw new IOException("Steam could not start a P2P connection.");
        try
        {
            while (true)
            {
                stop.ThrowIfCancellationRequested();
                if (!SteamNative.TryGetConnectionInfo(connection, out var info)) throw new IOException("Steam connection closed.");
                if (info.State == SteamNative.StateConnected) break;
                if (info.State is SteamNative.StateNone or SteamNative.StateClosedByPeer or SteamNative.StateProblemDetectedLocally)
                    throw new IOException(string.IsNullOrWhiteSpace(info.EndDebug) ? $"Host could not be reached through Steam ({info.EndReason})." : info.EndDebug.Trim());
                await Task.Delay(50, stop).ConfigureAwait(false);
            }
        }
        catch { SteamNative.Close(connection, "", false); throw; }
        var pipe = new SteamP2PPipe(connection, host);
        opened?.Invoke(pipe);
        return new MessagePeerConnection(pipe);
    }

    /// <summary>A connected in-process pair on the real Steam carrier, for single-account verification.</summary>
    public static (SteamP2PPipe First, SteamP2PPipe Second) LoopbackPair()
    {
        if (!SteamNative.CreateSocketPair(out var first, out var second)) throw new IOException("Steam could not create a socket pair.");
        return (new(first, SteamNative.LocalId), new(second, SteamNative.LocalId));
    }
}
