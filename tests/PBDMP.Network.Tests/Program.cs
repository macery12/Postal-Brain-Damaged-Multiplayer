using System.Net;
using System.Net.Sockets;
using PBDMP.Network;

var passed = 0;
await Test("world codec bounds, strict values and per-event preservation", () =>
{
    var frame = new WorldFrame(WorldKind.Projectile,2,17,21,3,"key","asset",2,4,1,
        new(1,2,3),new(0,90,0),new(0,0,80),4,7,new[]{1f,0.5f},new[]{"phase","2"});
    var bytes=Protocol.Encode(new Packet(MessageKind.World,World:frame));
    Check(bytes.SequenceEqual(Protocol.Encode(Protocol.Decode(bytes))));
    foreach(var kind in new[]{WorldKind.ActorState,WorldKind.ActorEffect,WorldKind.UrineState})
    {
        var actor=frame with{Kind=kind,Values=new[]{-32768f,65535f,32767f,65535f},Data=new[]{"actor-controller","death-context"}};
        var actorBytes=Protocol.Encode(new Packet(MessageKind.World,World:actor));
        Check(actorBytes.SequenceEqual(Protocol.Encode(Protocol.Decode(actorBytes))));
    }
    Throws(()=>Protocol.Encode(new Packet(MessageKind.World,World:frame with {Epoch=0})));
    Throws(()=>Protocol.Encode(new Packet(MessageKind.World,World:frame with {Kind=(WorldKind)31})));
    Throws(()=>Protocol.Encode(new Packet(MessageKind.World,World:frame with {Values=new[]{float.NaN}})));
    Throws(()=>Protocol.Encode(new Packet(MessageKind.World,World:frame with {Values=new float[33]})));
    Throws(()=>Protocol.Encode(new Packet(MessageKind.World,World:frame with {Data=new string[33]})));
    Throws(()=>Protocol.Encode(new Packet(MessageKind.World,World:frame with {Data=new[]{new string('界',100)}})));
    Throws(()=>Protocol.Encode(new Packet(MessageKind.World,World:frame with {Data=new string[]{null!}})));
    Throws(()=>Protocol.Encode(new Packet(MessageKind.World,World:frame with {Key=new string('界',100)})));
    return Task.CompletedTask;
});
await Test("reliable world events, role guards and array ownership over TCP", async () =>
{
    var port=FreePort(); using var host=new CoopSession("world"); using var client=new CoopSession("world");
    host.StartHost(port,IPAddress.Loopback); client.StartJoin(IPAddress.Loopback,port);
    await Until(()=>host.Snapshot.Connected && client.Snapshot.Connected);
    var values=new[]{2f};
    var data=new[]{"phase"};
    for (ulong i=1;i<=120;i++) host.PublishWorld(new(WorldKind.Impact,1,i,Values:values,Data:data));
    values[0]=99;
    data[0]="mutated";
    client.PublishWorld(new(WorldKind.Fire,1,1,Actor:2));
    Throws(()=>client.PublishWorld(new(WorldKind.Enemy,1,2)));
    Throws(()=>client.PublishWorld(new(WorldKind.ActorState,1,2)));
    Throws(()=>client.PublishWorld(new(WorldKind.ActorEffect,1,2)));
    Throws(()=>host.PublishWorld(new(WorldKind.Fire,1,121)));
    var received=new List<WorldFrame>(); var requests=new List<WorldFrame>();
    await Until(()=>{received.AddRange(client.DrainWorld()); requests.AddRange(host.DrainWorld()); return received.Count==120 && requests.Count==1;});
    Check(received.Select(f=>f.Sequence).SequenceEqual(Enumerable.Range(1,120).Select(i=>(ulong)i)));
    Check(received.All(f=>f.Values![0]==2 && f.Data![0]=="phase") && requests[0].Kind==WorldKind.Fire);
    client.Dispose(); await Until(()=>!host.Snapshot.Connected);
    Check(host.DrainWorld().Length==0);
});
await Test("large mission snapshots survive bounded transport backpressure", async () =>
{
    var port=FreePort();using var host=new CoopSession("bulk");using var client=new CoopSession("bulk");
    host.StartHost(port,IPAddress.Loopback);client.StartJoin(IPAddress.Loopback,port);
    await Until(()=>host.Snapshot.Connected && client.Snapshot.Connected);
    const int total=12288;ulong next=1;var received=new List<WorldFrame>();var clock=System.Diagnostics.Stopwatch.StartNew();var blocked=false;
    while(next<=total || received.Count<total)
    {
        Check(clock.Elapsed.TotalSeconds<10 && host.Snapshot.Connected && client.Snapshot.Connected);
        while(next<=total)
        {
            if(!host.PublishWorld(new(WorldKind.MissionState,1,next,Key:"mission",Flags:128,Values:new[]{(float)next}))) {blocked=true;break;}
            next++;
        }
        // Model a main thread busy building the scene before it can consume its snapshot.
        if(clock.Elapsed.TotalMilliseconds>=600) received.AddRange(client.DrainWorld(CoopSession.MaxWorldQueue));
        await Task.Delay(1);
    }
    Check(blocked && received.Select(f=>f.Sequence).SequenceEqual(Enumerable.Range(1,total).Select(i=>(ulong)i)));
    Check(received.All(f=>f.Values![0]==f.Sequence));
    Throws(()=>client.PublishWorld(new(WorldKind.MissionState,1,1)));
});
await Test("protocol round trip and fragmented TCP frame", async () =>
{
    var pose = new PlayerPose("CAMPAIGN:1:test", true, 12, 4, -20, 150, 1.8f, "weapon-guid", -30f, uint.MaxValue);
    var packet = new Packet(MessageKind.Pose, Pose: pose);
    Check(Protocol.Decode(Protocol.Encode(packet)) == packet);
    using var stream = new MemoryStream();
    await Protocol.WriteAsync(stream, packet, CancellationToken.None);
    using var fragments = new FragmentStream(stream.ToArray());
    Check(await Protocol.ReadAsync(fragments, CancellationToken.None) == packet);
});
await Test("malformed, oversized, trailing, NaN and truncated frames rejected", async () =>
{
    Throws(() => Protocol.Decode(new byte[] { 99 }));
    Throws(() => Protocol.Decode(Protocol.Encode(new Packet(MessageKind.Reject, Text: "ok")).Concat(new byte[] { 0 }).ToArray()));
    Throws(() => Protocol.Encode(new Packet(MessageKind.Reject, Text: new string('x', 257))));
    Throws(() => Protocol.Encode(new Packet(MessageKind.Pose, Pose: new("test", true, float.NaN, 0, 0, 0, 2))));
    Throws(() => Protocol.Encode(new Packet(MessageKind.Pose, Pose: new("", true, 0, 0, 0, 0, 2))));
    Throws(() => Protocol.Encode(new Packet(MessageKind.Pose, Pose: new("test", true, 0, 0, 0, 0, 2, Pitch: float.NaN))));
    Throws(() => Protocol.Encode(new Packet(MessageKind.Pose, Pose: new("test", true, 0, 0, 0, 0, 2, Pitch: 91))));
    Throws(() => Protocol.Encode(new Packet(MessageKind.Pose, Pose: new("test", true, 0, 0, 0, 0, 2, Weapon: new string('w',65)))));
    using var huge = new MemoryStream(BitConverter.GetBytes(Protocol.MaxFrameBytes + 1));
    await ThrowsAsync(() => Protocol.ReadAsync(huge, CancellationToken.None));
    using var truncated = new MemoryStream(new byte[] { 10, 0, 0, 0, 3 });
    await ThrowsAsync(() => Protocol.ReadAsync(truncated, CancellationToken.None));
});
await Test("travel generations, join loadouts, stale readiness and malformed state", () =>
{
    var host = new HostTravel();
    host.Begin(TravelAction.Load, "level-guid", 2, true);
    var first = host.State.Epoch;
    host.Acknowledge(new(first, true)); Check(!host.State.Released);
    var loadout = new JoinLoadout("weapon", "weapon", new[] { new InventoryEntry("weapon",1), new InventoryEntry("ammo",20) }, 1,2,3,90);
    host.SetReady(loadout); host.Acknowledge(new(first, false)); Check(!host.State.Released);
    host.Acknowledge(new(first, true)); Check(host.State.Released);
    var encoded = Protocol.Encode(new Packet(MessageKind.Travel, Travel: host.State));
    Check(encoded.SequenceEqual(Protocol.Encode(Protocol.Decode(encoded))));
    host.SetPaused(true); Check(host.State.Paused);
    host.Begin(TravelAction.Restart, "level-guid", 2); Check(host.State.Epoch != first && !host.State.HostReady);
    host.SetReady(); host.Acknowledge(new(first, true)); Check(!host.State.Released);
    host.PeerLeft(); Check(host.State.Released);
    Throws(() => Protocol.Encode(new Packet(MessageKind.Travel, Travel: host.State with { Epoch = 0 })));
    Throws(() => Protocol.Encode(new Packet(MessageKind.Travel, Travel: host.State with { Action = (TravelAction)99 })));
    Throws(() => Protocol.Encode(new Packet(MessageKind.Travel, Travel: host.State with { Difficulty = -1 })));
    Throws(() => Protocol.Encode(new Packet(MessageKind.Travel, Travel: host.State with { Joining = true, Loadout = loadout with { X = float.NaN } })));
    Throws(() => Protocol.Encode(new Packet(MessageKind.Travel, Travel: host.State with { Joining = true, Loadout = loadout with { Items = new[] { new InventoryEntry("x",1), new InventoryEntry("x",2) } } })));
    Throws(() => Protocol.Encode(new Packet(MessageKind.Travel, Travel: host.State with { Joining = true, Loadout = loadout with { Items = new[] { new InventoryEntry("x",-1) } } })));
    return Task.CompletedTask;
});
await Test("host-only travel, client-only ready and reconnect generations over TCP", async () =>
{
    var port = FreePort(); using var host = new CoopSession("travel"); using var client = new CoopSession("travel");
    host.StartHost(port, IPAddress.Loopback); client.StartJoin(IPAddress.Loopback, port);
    Throws(() => client.PublishTravel(new(1, TravelAction.Menu, "",0,true,false,true,false)));
    Throws(() => host.PublishReady(new(1,true)));
    var state = new TravelState(10,TravelAction.Load,"level",1,false,false,false,false);
    host.PublishTravel(state); client.PublishReady(new(10,false));
    await Until(() => client.Snapshot.Travel?.Epoch == 10 && host.Snapshot.Ready?.Ready == false);
    host.PublishTravel(state with { HostReady = true }); client.PublishReady(new(10,true));
    await Until(() => client.Snapshot.Travel?.HostReady == true && host.Snapshot.Ready?.Ready == true);
    host.PublishTravel(state with { Epoch = 11, Action = TravelAction.Restart });
    await Until(() => client.Snapshot.Travel?.Epoch == 11);
    client.Dispose(); await Until(() => !host.Snapshot.Connected && host.Snapshot.Ready == null);
    using var next = new CoopSession("travel"); next.StartJoin(IPAddress.Loopback,port);
    await Until(() => next.Snapshot.Travel?.Epoch == 11 && host.Snapshot.Ready == null);
});
await Test("wire authority violation releases slot; host loading-failure reason reaches client", async () =>
{
    var port = FreePort(); using var host = new CoopSession("authority"); host.StartHost(port,IPAddress.Loopback);
    using (var forged = new TcpClient())
    {
        await forged.ConnectAsync(IPAddress.Loopback,port);
        var stream = forged.GetStream();
        await Protocol.WriteAsync(stream,new(MessageKind.Hello,Text:"authority"),CancellationToken.None);
        Check((await Protocol.ReadAsync(stream,CancellationToken.None)).Kind == MessageKind.Welcome);
        await Until(()=>host.Snapshot.Connected);
        await Protocol.WriteAsync(stream,new(MessageKind.Travel,Travel:new(1,TravelAction.Menu,"",0,true,false,true,false)),CancellationToken.None);
        await Until(()=>!host.Snapshot.Connected);
        Check(host.Snapshot.Status.Contains("unauthorized"));
    }
    using var client = new CoopSession("authority"); client.StartJoin(IPAddress.Loopback,port);
    await Until(()=>client.Snapshot.Connected && host.Snapshot.Connected);
    host.DropPeer("Campaign loading timed out.");
    await Until(()=>!client.Snapshot.Connected && !host.Snapshot.Connected);
    Check(client.Snapshot.Status.Contains("Campaign loading timed out"));
});
await Test("invalid ports and duplicate start rejected", () =>
{
    using var session = new CoopSession("test");
    Throws(() => session.StartHost(0));
    session.StartHost(FreePort(), IPAddress.Loopback);
    Throws(() => session.StartJoin(IPAddress.Loopback, FreePort()));
    return Task.CompletedTask;
});
await Test("bidirectional poses, level change, full host, leave, rejoin, host stop", async () =>
{
    var port = FreePort();
    using var host = new CoopSession("test-game");
    using var client = new CoopSession("test-game");
    var p1 = new PlayerPose("level-1", true, 1, 2, 3, 90, 1.8f);
    var p2 = new PlayerPose("level-1", true, 4, 5, 6, 180, 1.3f);
    host.Publish(p1); client.Publish(p2);
    host.StartHost(port, IPAddress.Loopback);
    client.StartJoin(IPAddress.Loopback, port);
    await Until(() => host.Snapshot.RemotePose == p2 && client.Snapshot.RemotePose == p1);
    var firing = p2 with { Weapon = "shotgun", Pitch = -40f, ShotSequence = 42 };
    client.Publish(firing);
    await Until(() => host.Snapshot.RemotePose == firing);
    client.Publish(firing with { Weapon = "pistol", Pitch = 20f, ShotSequence = 43 });
    await Until(() => host.Snapshot.RemotePose?.Weapon == "pistol" && host.Snapshot.RemotePose.ShotSequence == 43);
    for (var attempt = 0; attempt < 5; attempt++)
    {
        using var extra = new CoopSession("test-game");
        extra.StartJoin(IPAddress.Loopback, port);
        try { await Until(() => extra.Snapshot.Status.Contains("full")); }
        catch { throw new Exception("Full-host rejection failed: " + extra.Snapshot.Status); }
        Check(host.Snapshot.Connected && client.Snapshot.Connected);
    }
    client.Publish(p2 with { Level = "level-2" });
    await Until(() => host.Snapshot.RemotePose?.Level == "level-2");
    client.Publish(PlayerPose.Empty);
    await Until(() => host.Snapshot.RemotePose?.Ready == false);
    client.Dispose();
    await Until(() => !host.Snapshot.Connected && host.Snapshot.RemotePose == null);
    using var rejoin = new CoopSession("test-game");
    rejoin.StartJoin(IPAddress.Loopback, port);
    await Until(() => rejoin.Snapshot.Connected && host.Snapshot.Connected);
    host.Dispose();
    await Until(() => !rejoin.Snapshot.Connected);
});
await Test("game and protocol version mismatches rejected", async () =>
{
    var port = FreePort();
    using var host = new CoopSession("game-a");
    host.StartHost(port, IPAddress.Loopback);
    using var wrongGame = new CoopSession("game-b");
    wrongGame.StartJoin(IPAddress.Loopback, port);
    await Until(() => wrongGame.Snapshot.Status.Contains("Game or PBDMP build mismatch"));
    using var wrongVersion = new CoopSession("game-a", 1); // Previous 0.1 wire format.
    wrongVersion.StartJoin(IPAddress.Loopback, port);
    await Until(() => wrongVersion.Snapshot.Status.Contains("protocol version mismatch"));
    Check(!host.Snapshot.Connected);
});
await Test("occupied host port and refused join report errors", async () =>
{
    var port = FreePort();
    using var host = new CoopSession("test");
    host.StartHost(port, IPAddress.Loopback);
    using var duplicate = new CoopSession("test");
    Throws(() => duplicate.StartHost(port, IPAddress.Loopback));
    using var client = new CoopSession("test");
    client.StartJoin(IPAddress.Loopback, FreePort());
    await Until(() => client.Snapshot.Status.StartsWith("Join failed:"));
});
await Test("malformed peer dropped and host admits a valid replacement", async () =>
{
    var port = FreePort();
    using var host = new CoopSession("test");
    host.StartHost(port, IPAddress.Loopback);
    using (var bad = new TcpClient())
    {
        await bad.ConnectAsync(IPAddress.Loopback, port);
        await Protocol.WriteAsync(bad.GetStream(), new(MessageKind.Hello, Text: "test"), CancellationToken.None);
        await Protocol.ReadAsync(bad.GetStream(), CancellationToken.None);
        await bad.GetStream().WriteAsync(new byte[] { 0, 0, 0, 0 });
        await Until(() => !host.Snapshot.Connected);
    }
    using var client = new CoopSession("test");
    client.StartJoin(IPAddress.Loopback, port);
    await Until(() => client.Snapshot.Connected && host.Snapshot.Connected);
});
await Test("silent peer times out and releases host slot", async () =>
{
    var port = FreePort();
    using var host = new CoopSession("test");
    host.StartHost(port, IPAddress.Loopback);
    using var silent = new TcpClient();
    await silent.ConnectAsync(IPAddress.Loopback, port);
    await Protocol.WriteAsync(silent.GetStream(), new(MessageKind.Hello, Text: "test"), CancellationToken.None);
    await Protocol.ReadAsync(silent.GetStream(), CancellationToken.None);
    await Until(() => host.Snapshot.Connected);
    await Until(() => !host.Snapshot.Connected && host.Snapshot.Status.Contains("timed out"), 13);
});
await Test("cancellation during handshake and idempotent disposal", async () =>
{
    var port = FreePort();
    using var host = new CoopSession("test");
    host.StartHost(port, IPAddress.Loopback);
    using var pending = new TcpClient();
    await pending.ConnectAsync(IPAddress.Loopback, port);
    host.Dispose(); host.Dispose();
    await Task.Delay(100);
    Check(host.Snapshot.Role == SessionRole.Offline && !host.Snapshot.Connected);
    Throws(() => host.StartHost(port));
});
await Test("handshake, poses, ordered world events, full host, stated drop reason and rejoin over a message carrier", async () =>
{
    using var listener = new LoopbackListener();
    using var host = new CoopSession("carrier"); using var client = new CoopSession("carrier");
    var p1 = new PlayerPose("level-1", true, 1, 2, 3, 90, 1.8f);
    var p2 = new PlayerPose("level-1", true, 4, 5, 6, 180, 1.3f);
    host.Publish(p1); client.Publish(p2);
    host.StartHost(listener, "Hosting carrier; waiting for P2");
    Check(host.Snapshot.Role == SessionRole.Host && host.Snapshot.Status == "Hosting carrier; waiting for P2");
    Throws(() => host.StartHost(new LoopbackListener(), "again"));
    client.StartJoin(listener.ConnectAsync, "Connecting through carrier", 5);
    await Until(() => host.Snapshot.RemotePose == p2 && client.Snapshot.RemotePose == p1);
    for (ulong i=1;i<=120;i++) host.PublishWorld(new(WorldKind.Impact,1,i,Values:new[]{2f}));
    client.PublishWorld(new(WorldKind.Fire,1,1,Actor:2));
    var received=new List<WorldFrame>(); var requests=new List<WorldFrame>();
    await Until(()=>{received.AddRange(client.DrainWorld()); requests.AddRange(host.DrainWorld()); return received.Count==120 && requests.Count==1;});
    Check(received.Select(f=>f.Sequence).SequenceEqual(Enumerable.Range(1,120).Select(i=>(ulong)i)));
    using (var extra = new CoopSession("carrier"))
    {
        extra.StartJoin(listener.ConnectAsync, "Connecting through carrier", 5);
        await Until(() => extra.Snapshot.Status.Contains("full"));
        Check(host.Snapshot.Connected && client.Snapshot.Connected);
    }
    for (var attempt = 0; attempt < 10; attempt++)
    {
        using var dropped = attempt == 0 ? client : new CoopSession("carrier");
        if (attempt > 0) dropped.StartJoin(listener.ConnectAsync, "Connecting through carrier", 5);
        await Until(() => dropped.Snapshot.Connected && host.Snapshot.Connected);
        host.DropPeer("Campaign loading timed out.");
        await Until(() => !dropped.Snapshot.Connected && !host.Snapshot.Connected);
        // The peer's own send can fail on the closed carrier before it reads the reason.
        if (!dropped.Snapshot.Status.Contains("Campaign loading timed out")) throw new Exception("Drop reason lost: " + dropped.Snapshot.Status);
    }
    using var rejoin = new CoopSession("carrier");
    rejoin.StartJoin(listener.ConnectAsync, "Connecting through carrier", 5);
    await Until(() => rejoin.Snapshot.Connected && host.Snapshot.Connected);
    host.Dispose();
    await Until(() => !rejoin.Snapshot.Connected);
});
await Test("large snapshots survive a bounded message carrier", async () =>
{
    // Each refusal by the full carrier costs the sender one timer tick, so capacity sets the throughput here.
    using var listener = new LoopbackListener(64);
    using var host=new CoopSession("bulk");using var client=new CoopSession("bulk");
    host.StartHost(listener,"Hosting");client.StartJoin(listener.ConnectAsync,"Connecting",5);
    await Until(()=>host.Snapshot.Connected && client.Snapshot.Connected);
    const int total=12288;ulong next=1;var received=new List<WorldFrame>();var clock=System.Diagnostics.Stopwatch.StartNew();var blocked=false;
    while(next<=total || received.Count<total)
    {
        Check(clock.Elapsed.TotalSeconds<20 && host.Snapshot.Connected && client.Snapshot.Connected);
        while(next<=total)
        {
            if(!host.PublishWorld(new(WorldKind.MissionState,1,next,Key:"mission",Flags:128,Values:new[]{(float)next}))) {blocked=true;break;}
            next++;
        }
        if(clock.Elapsed.TotalMilliseconds>=600) received.AddRange(client.DrainWorld(CoopSession.MaxWorldQueue));
        await Task.Delay(1);
    }
    Check(blocked && received.Select(f=>f.Sequence).SequenceEqual(Enumerable.Range(1,total).Select(i=>(ulong)i)));
});
await Test("build mismatch, refused dial and slow dial report errors over a message carrier", async () =>
{
    using var listener = new LoopbackListener();
    using var host = new CoopSession("game-a");
    host.StartHost(listener, "Hosting");
    using var wrongGame = new CoopSession("game-b");
    wrongGame.StartJoin(listener.ConnectAsync, "Connecting", 5);
    await Until(() => wrongGame.Snapshot.Status.Contains("Game or PBDMP build mismatch"));
    using var refused = new CoopSession("game-a");
    refused.StartJoin(_ => Task.FromException<IPeerConnection>(new IOException("Host is not reachable.")), "Connecting", 5);
    await Until(() => refused.Snapshot.Status == "Join failed: Host is not reachable.");
    using var slow = new CoopSession("game-a");
    slow.StartJoin(async stop => { await Task.Delay(Timeout.Infinite, stop); throw new IOException(); }, "Connecting", 1);
    await Until(() => slow.Snapshot.Status == "Join failed: Connection timed out");
    Check(!host.Snapshot.Connected);
});
await Test("message stream reassembles split and coalesced frames", async () =>
{
    var (near, far) = LoopbackPipe.Pair(64);
    using var stream = new MessageStream(near);
    var first = new Packet(MessageKind.Pose, Pose: new PlayerPose("level", true, 1, 2, 3, 4, 1.8f, "weapon"));
    var second = new Packet(MessageKind.Reject, Text: "reason");
    using var wire = new MemoryStream();
    await Protocol.WriteAsync(wire, first, CancellationToken.None);
    await Protocol.WriteAsync(wire, second, CancellationToken.None);
    var bytes = wire.ToArray();
    // One byte, an empty message, then everything else including the whole second frame.
    Check(far.TrySend(bytes.AsSpan(0, 1)) && far.TrySend(ReadOnlySpan<byte>.Empty) && far.TrySend(bytes.AsSpan(1)));
    Check(await Protocol.ReadAsync(stream, CancellationToken.None) == first);
    Check(await Protocol.ReadAsync(stream, CancellationToken.None) == second);
    far.Dispose();
    await ThrowsAsync(() => Protocol.ReadAsync(stream, CancellationToken.None));
});
Console.WriteLine($"PASS: {passed} network acceptance scenarios.");

async Task Test(string name, Func<Task> action)
{
    await action();
    passed++;
    Console.WriteLine("PASS " + name);
}
static void Check(bool value) { if (!value) throw new Exception("Assertion failed."); }
static void Throws(Action action)
{
    try { action(); } catch { return; }
    throw new Exception("Expected exception.");
}
static async Task ThrowsAsync(Func<Task> action)
{
    try { await action(); } catch { return; }
    throw new Exception("Expected exception.");
}
static async Task Until(Func<bool> predicate, int seconds = 5)
{
    var watch = System.Diagnostics.Stopwatch.StartNew();
    while (!predicate())
    {
        if (watch.Elapsed.TotalSeconds > seconds) throw new Exception("Timed out waiting for session state.");
        await Task.Delay(20);
    }
}
static int FreePort()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
}
sealed class FragmentStream : MemoryStream
{
    public FragmentStream(byte[] bytes) : base(bytes) { }
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        => base.ReadAsync(buffer[..Math.Min(buffer.Length, 1)], token);
}
// In-process stand-in for a relayed message link: bounded, ordered, and closed for both ends at once.
sealed class LoopbackPipe : IMessagePipe
{
    private sealed class Link { public volatile bool Closed; }
    private readonly System.Collections.Concurrent.ConcurrentQueue<byte[]> _inbound, _outbound;
    private readonly Link _link;
    private readonly int _capacity;
    private LoopbackPipe(System.Collections.Concurrent.ConcurrentQueue<byte[]> inbound,
        System.Collections.Concurrent.ConcurrentQueue<byte[]> outbound, Link link, int capacity)
    { _inbound = inbound; _outbound = outbound; _link = link; _capacity = capacity; }
    public static (LoopbackPipe Near, LoopbackPipe Far) Pair(int capacity)
    {
        var a = new System.Collections.Concurrent.ConcurrentQueue<byte[]>(); var b = new System.Collections.Concurrent.ConcurrentQueue<byte[]>();
        var link = new Link();
        return (new(a, b, link, capacity), new(b, a, link, capacity));
    }
    public bool TrySend(ReadOnlySpan<byte> message)
    {
        if (_link.Closed) throw new IOException("Carrier closed.");
        if (_outbound.Count >= _capacity) return false;
        _outbound.Enqueue(message.ToArray()); return true;
    }
    public void Receive(List<byte[]> into)
    {
        var closed = _link.Closed;
        while (_inbound.TryDequeue(out var message)) into.Add(message);
        if (closed && into.Count == 0) throw new IOException("Carrier closed.");
    }
    public void Dispose() => _link.Closed = true;
}
sealed class LoopbackListener : IPeerListener
{
    private readonly System.Threading.Channels.Channel<IPeerConnection> _incoming = System.Threading.Channels.Channel.CreateUnbounded<IPeerConnection>();
    private readonly int _capacity;
    public LoopbackListener(int capacity = 64) => _capacity = capacity;
    public Task<IPeerConnection> ConnectAsync(CancellationToken stop)
    {
        var (near, far) = LoopbackPipe.Pair(_capacity);
        if (!_incoming.Writer.TryWrite(new MessagePeerConnection(far))) throw new IOException("Host is not listening.");
        return Task.FromResult<IPeerConnection>(new MessagePeerConnection(near));
    }
    public async Task<IPeerConnection> AcceptAsync(CancellationToken stop) => await _incoming.Reader.ReadAsync(stop);
    public void Dispose() => _incoming.Writer.TryComplete();
}
