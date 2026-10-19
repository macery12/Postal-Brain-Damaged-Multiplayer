using System.Diagnostics;
using System.Text.Json;
using Hyperstrange.PBD;
using PBDMP.Network;
using Il2CppInterop.Runtime;

namespace PBDMP.Plugin;

internal sealed partial class CoopSaveService
{
    private IEnumerator<int>? _joinCapture;
    private uint _joinCaptureEpoch,_baselineAckEpoch;
    private string _captureWait="";
    internal string JoinCaptureForTest=>$"capture={_joinCapture!=null} wait={_captureWait} captureEpoch={_joinCaptureEpoch} sent={_worldSentEpoch} ack={_baselineAckEpoch} await={_awaitNative} incoming={_incomingNative!=null} restoring={_guestNativeWork!=null}";
    private readonly Queue<(SaveCommand Command,byte[] Bytes,uint Epoch,uint Transaction)> _transfers=new();
    private long _transferBytes;
    private readonly WorldBaseline _baselineTransfer=new();

    private void TickNativeJoin(bool connected)
    {
        if(_joinCapture!=null && (_joinCaptureEpoch!=_life.Epoch || !connected))
        {_joinCapture.Dispose();_joinCapture=null;}
        if(!_life.IsClient && connected && Assigned && _life.State.Joining &&
            _life.NativeReady && _world.Active && !_life.RestoringWorld && _restore==null && _joinCaptureEpoch!=_life.Epoch)
        {
            _awaitNative=true;_joinCaptureEpoch=_life.Epoch;
            _joinCapture=CaptureCurrentBaseline().GetEnumerator();Status="Capturing current world for P2...";
        }
        // Disk restoration also needs a fresh passive baseline after native reconstruction.
        if(!_life.IsClient && connected && Assigned && _restore!=null && !_life.RestoringWorld && _life.NativeReady &&
            _world.Active && _joinCaptureEpoch!=_life.Epoch)
        {
            _awaitNative=true;_joinCaptureEpoch=_life.Epoch;
            _joinCapture=CaptureCurrentBaseline().GetEnumerator();
        }
        if(_joinCapture==null) return;
        var deadline=Stopwatch.GetTimestamp()+Stopwatch.Frequency*3/1000;
        do
        {
            if(_joinCapture.MoveNext()) continue;
            _joinCapture.Dispose();_joinCapture=null;
            if(_restore==null) Status="";
            break;
        } while(Stopwatch.GetTimestamp()<deadline);
    }
    private IEnumerable<int> CaptureCurrentBaseline()
    {
        _captureWait="props";
        while(_world.PendingPropSpawns) yield return 0;
        var result=new Dictionary<string,Dictionary<string,string>>(StringComparer.Ordinal);
        foreach(var entry in _entries)
        {
            if(entry.Component==null || NativeSaveBridge.Personal(entry.Component) ||
                entry.Component.TryCast<NpcController>()!=null || entry.Component.TryCast<LootController>()!=null ||
                NativeSaveBridge.NpcOwned(entry.Component) || NativeSaveBridge.SurvivalOwned(entry.Component)) continue;
            Dictionary<string,string>? state=null;_captureWait=entry.Component.GetIl2CppType().Name+":"+entry.Guid;
            foreach(var step in CaptureNativeEntry(entry,v=>state=v)) yield return step;
            if(state!=null) result.Add(entry.Guid,state);
        }
        _nativeBaseline=result;_baselineLevel=_life.State.Level;_captureWait="";
        _worldSentEpoch=0;_deadline=UnityEngine.Time.unscaledTime+120;
    }
    private static IEnumerable<int> CaptureNativeEntry(NativeSaveBridge.Entry entry,Action<Dictionary<string,string>?> accept)
    {
        Dictionary<string,string>? state=null;Exception? error=null;var called=false;var gate=new object();
        Il2CppSystem.Action<Il2CppSystem.Collections.Generic.Dictionary<string,string>> callback=
            (Action<Il2CppSystem.Collections.Generic.Dictionary<string,string>>)(pairs=>
            {lock(gate) {try {if(pairs!=null) state=NativeSaveBridge.Copy(pairs);}catch(Exception e) {error=e;}called=true;}});
        var task=entry.Component.Cast<ISaveable>().SaveAsync(callback);
        while(!task.IsCompleted) yield return 0;
        if(task.IsFaulted) throw new InvalidOperationException("Native capture failed: "+entry.Guid+" / "+task.Exception);
        lock(gate)
        {
            if(error!=null) throw new InvalidOperationException("Native state copy failed: "+entry.Guid,error);
            if(!called) throw new InvalidOperationException("Native capture supplied no result: "+entry.Guid);
            accept(state);
        }
        GC.KeepAlive(callback);
    }
    private void SendBaseline(Dictionary<string,Dictionary<string,string>> entries)
    {
        var bytes=JsonSerializer.SerializeToUtf8Bytes(entries);
        if(bytes.Length>CoopSaveStore.MaxBytes) throw new InvalidDataException("Current shared baseline exceeds its supported size.");
        Send(SaveCommand.WorldRestoreBegin,WorldBaseline.Begin(bytes),_life.Epoch);
        for(var at=0;at<bytes.Length;at+=WorldBaseline.ChunkBytes)
            Send(SaveCommand.WorldRestore,bytes.AsSpan(at,Math.Min(WorldBaseline.ChunkBytes,bytes.Length-at)).ToArray(),_life.Epoch);
        Send(SaveCommand.WorldRestoreEnd,Array.Empty<byte>(),_life.Epoch);
    }
    private void ReceiveBaseline(SaveCommand command,byte[] bytes)
    {
        var payload=_baselineTransfer.Accept(command,bytes);
        if(payload==null) return;
        _incomingNative=JsonSerializer.Deserialize<Dictionary<string,Dictionary<string,string>>>(payload)??throw new InvalidDataException("Missing world baseline.");
        if(_incomingNative.Count>32768) throw new InvalidDataException("Too many native world entries.");
    }

    private void DrainTransfers()
    {
        // Split one bounded transaction at a time; do not enqueue an entire 32 MiB baseline as pages.
        if(_out.Count<256 && _transfers.Count>0)
        {
            var next=_transfers.Dequeue();_transferBytes-=next.Bytes.Length;
            foreach(var page in SaveProtocol.Split(next.Command,next.Epoch,next.Transaction,next.Bytes)) _out.Enqueue(page);
        }
        for(var i=0;i<64 && _out.Count>0;i++) {if(!_session.PublishSave(_out.Peek())) break;_out.Dequeue();}
    }
    private void ClearNativeTransfer()
    {
        _joinCapture?.Dispose();_joinCapture=null;_joinCaptureEpoch=0;_baselineAckEpoch=0;
        _baselineTransfer.Reset();
        _transfers.Clear();_transferBytes=0;
    }
}
