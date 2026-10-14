using System.Net;
using System.Net.Sockets;

namespace PBDMP.Network;

public sealed class TcpPeerConnection : IPeerConnection
{
    private readonly TcpClient _client;
    public Stream Stream { get; }

    public TcpPeerConnection(TcpClient client)
    {
        _client = client; client.NoDelay = true;
        Stream = client.GetStream();
    }

    public static async Task<IPeerConnection> ConnectAsync(IPAddress address, int port, CancellationToken stop)
    {
        var client = new TcpClient(address.AddressFamily) { NoDelay = true };
        try
        {
            await client.ConnectAsync(address, port, stop).ConfigureAwait(false);
            return new TcpPeerConnection(client);
        }
        catch { client.Dispose(); throw; }
    }

    public void Dispose() => _client.Dispose();
}

public sealed class TcpPeerListener : IPeerListener
{
    private readonly TcpListener _listener;

    public TcpPeerListener(int port, IPAddress? bindAddress = null)
    {
        _listener = new TcpListener(bindAddress ?? IPAddress.Any, port);
        _listener.Start(4);
    }

    public async Task<IPeerConnection> AcceptAsync(CancellationToken stop) =>
        new TcpPeerConnection(await _listener.AcceptTcpClientAsync(stop).ConfigureAwait(false));

    public void Dispose() => _listener.Stop();
}
