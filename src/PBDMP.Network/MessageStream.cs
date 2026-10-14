namespace PBDMP.Network;

/// <summary>Presents a message carrier as the byte stream the framed protocol expects. One reader, one writer.</summary>
public sealed class MessageStream : Stream
{
    private readonly IMessagePipe _pipe;
    private readonly List<byte[]> _batch = new();
    private readonly Queue<byte[]> _pending = new();
    private byte[]? _current;
    private int _offset;

    public MessageStream(IMessagePipe pipe) => _pipe = pipe;

    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
    {
        while (_current == null || _offset == _current.Length)
        {
            if (_pending.Count == 0)
            {
                // Polling only while the session wants data leaves a busy receiver's backlog in the carrier.
                _pipe.Receive(_batch);
                foreach (var message in _batch) _pending.Enqueue(message);
                _batch.Clear();
                if (_pending.Count == 0) { await Task.Delay(1, token).ConfigureAwait(false); continue; }
            }
            _current = _pending.Dequeue(); _offset = 0;
        }
        var count = Math.Min(buffer.Length, _current.Length - _offset);
        _current.AsMemory(_offset, count).CopyTo(buffer);
        _offset += count;
        return count;
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
    {
        // The protocol writes each frame in one call, so one frame travels as one message.
        while (!_pipe.TrySend(buffer.Span)) await Task.Delay(1, token).ConfigureAwait(false);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) =>
        ReadAsync(buffer.AsMemory(offset, count), token).AsTask();
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token) =>
        WriteAsync(buffer.AsMemory(offset, count), token).AsTask();
    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override void Write(byte[] buffer, int offset, int count) =>
        WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _pipe.Dispose();
        base.Dispose(disposing);
    }
}

public sealed class MessagePeerConnection : IPeerConnection
{
    public Stream Stream { get; }
    public MessagePeerConnection(IMessagePipe pipe) => Stream = new MessageStream(pipe);
    public void Dispose() => Stream.Dispose();
}
