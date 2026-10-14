namespace PBDMP.Network;

/// <summary>One ordered, reliable duplex byte stream to the peer. One reader and one writer; disposing closes it.</summary>
public interface IPeerConnection : IDisposable
{
    Stream Stream { get; }
}

/// <summary>Source of inbound peer connections for a host. The session owns it once started; disposing stops listening.</summary>
public interface IPeerListener : IDisposable
{
    Task<IPeerConnection> AcceptAsync(CancellationToken stop);
}

/// <summary>Carrier of whole reliable ordered messages, such as a relayed P2P link. Called from worker threads.</summary>
public interface IMessagePipe : IDisposable
{
    /// <summary>Queues one message. False while the carrier is momentarily full; throws once it has ended.</summary>
    bool TrySend(ReadOnlySpan<byte> message);
    /// <summary>Appends received messages. Throws with the close reason once the carrier has ended and is drained.</summary>
    void Receive(List<byte[]> into);
}
