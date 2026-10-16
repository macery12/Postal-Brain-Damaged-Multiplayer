namespace PBDMP.Network;

/// <summary>Opt-in application-wire counters. Excludes transport/IP overhead and retransmissions.</summary>
public static class ProtocolTraffic
{
    public static readonly bool Enabled=Environment.GetEnvironmentVariable("PBDMP_PERF_BASELINE")=="1";
    private static long _sentBytes,_receivedBytes,_sentFrames,_receivedFrames;
    public readonly record struct Snapshot(long SentBytes,long ReceivedBytes,long SentFrames,long ReceivedFrames);
    public static Snapshot Read()=>new(Interlocked.Read(ref _sentBytes),Interlocked.Read(ref _receivedBytes),Interlocked.Read(ref _sentFrames),Interlocked.Read(ref _receivedFrames));
    internal static void Sent(int bytes)
    {if(!Enabled)return;Interlocked.Add(ref _sentBytes,bytes);Interlocked.Increment(ref _sentFrames);}
    internal static void Received(int bytes)
    {if(!Enabled)return;Interlocked.Add(ref _receivedBytes,bytes);Interlocked.Increment(ref _receivedFrames);}
}
