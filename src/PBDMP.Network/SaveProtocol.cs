namespace PBDMP.Network;

public enum SaveCommand : byte { Identity=1, GuestState, HoldAck, Restored, Checkpoint, WorldRestored, Capture=16, Resume, Restore, WorldRestore, WorldRestoreBegin, WorldRestoreEnd }
public sealed record SavePage(SaveCommand Command,uint Epoch,uint Transaction,int Index,int Count,byte[] Bytes);
public static class SaveProtocol
{
    public const int PageBytes=4096, MaxBytes=4*1024*1024;
    public static bool FromClient(SaveCommand command)=>(byte)command<16;
    public static void Validate(SavePage p)
    {
        if(!Enum.IsDefined(typeof(SaveCommand),p.Command) || p.Transaction==0 || p.Count<1 || p.Count>MaxBytes/PageBytes || p.Index<0 || p.Index>=p.Count ||
            p.Bytes==null || p.Bytes.Length>PageBytes || (p.Index<p.Count-1 && p.Bytes.Length!=PageBytes)) throw new InvalidDataException("Invalid co-op save transfer page.");
    }
    public static SavePage[] Split(SaveCommand command,uint epoch,uint transaction,byte[] data)
    {
        if(data.Length>MaxBytes) throw new InvalidDataException("Personal state transfer is too large.");
        var count=Math.Max(1,(data.Length+PageBytes-1)/PageBytes);
        return Enumerable.Range(0,count).Select(i=>new SavePage(command,epoch,transaction,i,count,data.Skip(i*PageBytes).Take(PageBytes).ToArray())).ToArray();
    }
    internal static void Write(BinaryWriter w,SavePage p)
    { Validate(p);w.Write((byte)p.Command);w.Write(p.Epoch);w.Write(p.Transaction);w.Write(p.Index);w.Write(p.Count);w.Write(p.Bytes.Length);w.Write(p.Bytes); }
    internal static SavePage Read(BinaryReader r)
    {
        var command=(SaveCommand)r.ReadByte();var epoch=r.ReadUInt32();var transaction=r.ReadUInt32();var index=r.ReadInt32();var count=r.ReadInt32();var length=r.ReadInt32();
        if(length<0 || length>PageBytes) throw new InvalidDataException("Invalid save page length.");
        var bytes=r.ReadBytes(length);if(bytes.Length!=length) throw new EndOfStreamException();
        var p=new SavePage(command,epoch,transaction,index,count,bytes);Validate(p);return p;
    }
}
public sealed class SaveAssembler
{
    private SavePage? _first; private readonly MemoryStream _bytes=new(); private int _next;
    public byte[]? Accept(SavePage p)
    {
        SaveProtocol.Validate(p);
        if(_first==null) {if(p.Index!=0) throw new InvalidDataException("Missing first save page.");_first=p;}
        if(p.Command!=_first.Command || p.Epoch!=_first.Epoch || p.Transaction!=_first.Transaction || p.Count!=_first.Count || p.Index!=_next)
            throw new InvalidDataException("Out-of-order save transfer.");
        _bytes.Write(p.Bytes);_next++;
        if(_next!=p.Count) return null;
        var bytes=_bytes.ToArray();Reset();return bytes;
    }
    public void Reset() {_first=null;_bytes.SetLength(0);_next=0;}
}
