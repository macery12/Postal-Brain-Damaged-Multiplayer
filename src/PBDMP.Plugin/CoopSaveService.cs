using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BepInEx;
using Hyperstrange.PBD;
using Il2CppInterop.Runtime;
using Hyperstrange.PBD.Contraptions;
using Hyperstrange.PBD.PlayerComponents;
using PBDMP.Network;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PBDMP.Plugin;

/// <summary>Main-thread capture/restore transactions. Only immutable managed data reaches disk workers.</summary>
internal sealed partial class CoopSaveService : IDisposable
{
    internal static CoopSaveService? Current;
    private sealed record Request(string Slot,string Name);
    private sealed record RestorePlayer(SavedPlayer? Player,JoinLoadout? Loadout,bool Loading,bool CatchUp);
    private sealed record CheckpointRequest(string Key,NetVector Feet,float Height);
    private readonly CoopSession _session;
    private readonly SessionLifecycle _life;
    private readonly SharedWorld _world;
    private readonly Action<string> _notice;
    private readonly Action<string,Action,Action> _confirm;
    private readonly Func<ulong> _remoteSteam;
    private readonly string _identity;
    private readonly CoopSaveStore _store;
    private readonly AutosaveClock _autosave=new(Plugin.AutosaveMinutes.Value*60);
    internal int CompletedWritesForTest {get;private set;}
    internal int CheckpointRequestsForTest {get;private set;}
    internal double AutosaveRemainingForTest=>_autosave.Remaining;
    internal void AdvanceAutosaveForTest(double seconds)
    {
        if(Environment.GetEnvironmentVariable("PBDMP_SAVE_TEST")!="1") throw new InvalidOperationException("Diagnostic clock advancement is unavailable.");
        _autosave.Advance(seconds,true);
    }
    private readonly Queue<SavePage> _out=new();
    private readonly SaveAssembler _assembler=new();
    private readonly Queue<Request> _pending=new();
    private readonly Dictionary<IntPtr,string> _pickupKeys=new();
    private readonly HashSet<string> _collected=new(StringComparer.Ordinal);
    private NativeSaveBridge.Entry[] _entries=Array.Empty<NativeSaveBridge.Entry>();
    private SavedPlayer? _guest;
    private string _peerIdentity="", _approved="", _waitingIdentity="";
    private bool _connected, _asking, _holdAck, _restoredAck, _restoreSent;
    private uint _transaction, _captureTransaction, _sentEpoch, _generation;
    private float _deadline, _profileAt, _readyAt, _checkpointAt;
    private int _autoSlot,_holdFrame,_clientHoldFrame;
    private Request? _request;
    private CoopSave? _document, _restore;
    private IEnumerator<int>? _work;
    private Task? _write;
    private Task<LoadedSave>? _read;
    private bool _catalogueRecoveryTried;
    private RestorePlayer? _incomingRestore;
    private uint _incomingEpoch, _incomingTransaction, _pendingCapture;
    private bool _disposed, _restoringNative, _restoreStarted;
    private bool _captureHold,_captureAuto;
    internal string HoldCaption=>_restore!=null || _life.RestoringWorld || _incomingRestore!=null || _guestNativeWork!=null || _awaitNative
        ? "Loading..." : _life.PersistenceHold && (_request!=null || _captureHold)
        ? (_request?.Slot.StartsWith("auto-",StringComparison.Ordinal)==true || _captureAuto?"Autosaving...":"Saving...") : "";
    private Dictionary<string,Dictionary<string,string>>? _nativeBaseline,_incomingNative;
    private string _baselineLevel="";
    private uint _worldSentEpoch,_controlEpoch;
    private bool _awaitNative;
    private IEnumerator<int>? _guestNativeWork;
    internal bool MaySendSnapshot=>!_awaitNative && (!_life.State.Joining || _baselineAckEpoch==_life.Epoch);
    internal bool Busy=>_request!=null || _work!=null || _write!=null || _read!=null || _restore!=null || _pending.Count>0;
    internal string Status {get;private set;}="";
        internal bool Restoring=>_restore!=null;
    internal bool TransactionActive=>_request!=null || _restore!=null || _work!=null;
    internal SavedPlayer? GuestForTest=>_guest;
    internal CoopSaveStore Store=>_store;
    internal static string Root=>Environment.GetEnvironmentVariable("PBDMP_SAVE_ROOT") is {Length:>0} diagnostic && Environment.GetEnvironmentVariable("PBDMP_SAVE_TEST")=="1"
        ?diagnostic:Path.Combine(Paths.BepInExRootPath,"PBDMP","saves");
    internal static string DirectIdentity()
    {
        if(Environment.GetEnvironmentVariable("PBDMP_SAVE_TEST")=="1" && Environment.GetEnvironmentVariable("PBDMP_SAVE_TEST_ID") is {Length:>0} testId && Guid.TryParseExact(testId,"N",out _)) return "direct:"+testId;
        var root=Path.Combine(Paths.BepInExRootPath,"PBDMP");Directory.CreateDirectory(root);
        var path=Path.Combine(root,"player-id");
        if(File.Exists(path) && Guid.TryParse(File.ReadAllText(path).Trim(),out var existing)) return "direct:"+existing.ToString("N");
        var id=Guid.NewGuid().ToString("N");
        using(var stream=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None))
        {var bytes=Encoding.ASCII.GetBytes(id);stream.Write(bytes);stream.Flush(true);}
        return "direct:"+id;
    }
    internal CoopSaveService(CoopSession session,SessionLifecycle life,SharedWorld world,string identity,Func<ulong> remoteSteam,
        Action<string> notice,Action<string,Action,Action> confirm)
    {
        _session=session;_life=life;_world=world;_identity=identity;_remoteSteam=remoteSteam;_notice=notice;_confirm=confirm;
        _store=new(Root);Current=this;
    }
    internal void RequestSave(string slot,string name)
    {
        if(_life.IsClient) {Tell("P1 controls co-op saves.");return;}
        name=new string(name.Trim().Where(c=>!char.IsControl(c)).Take(80).ToArray());
        if(!CoopSaveStore.ValidSlot(slot) || name.Length==0) {Tell("Enter a save name.");return;}
        if(_life.State.Action==TravelAction.Menu) {Tell("Start or load a campaign before saving.");return;}
        if(_pending.Count>=8) {Tell("A co-op save is already pending.");return;}
        if(_request?.Slot==slot || _pending.Any(p=>p.Slot==slot)) return;
        _pending.Enqueue(new(slot,name));Status="Save pending.";
    }
    internal void QuickSave()=>RequestSave("quick","Quicksave");
    internal void AutoSave()
    {
        if(_life.IsClient || !_life.NativeReady || _life.State.Action==TravelAction.Menu || !AutosEnabled || Restoring || Busy || _life.Frozen || !_autosave.Due) return;
        if(!_autosave.TryConsume()) return;
        _autoSlot=Enumerable.Range(0,3).OrderBy(i=>File.Exists(Path.Combine(Root,"auto-"+i+".pbdmp"))?File.GetLastWriteTimeUtc(Path.Combine(Root,"auto-"+i+".pbdmp")):DateTime.MinValue).First();RequestSave("auto-"+_autoSlot,"Timed autosave");
    }
    private static bool AutosEnabled=>string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PBDMP_TRAVEL_TEST")) || Environment.GetEnvironmentVariable("PBDMP_SAVE_AUTOTEST")=="1";
    internal void RestoreCheckpoint()
    {
        if(_life.IsClient) {Tell("P1 controls checkpoint restoration.");return;}
        if(Busy) {Tell("Wait for the current save operation to finish.");return;}
        _catalogueRecoveryTried=false;Status="Reading co-op checkpoint...";
        _read=Task.Run(()=>
        {
            var latest=_store.List().Where(s=>s.Slot.StartsWith("auto-",StringComparison.Ordinal) && s.Error==null).OrderByDescending(s=>s.Created).FirstOrDefault();
            if(latest==null) throw new InvalidDataException("No valid co-op checkpoint has been saved yet.");return _store.Read(latest.Slot);
        });
    }
    internal void RequestLoad(string slot)
    {
        if(_life.IsClient) {Tell("P1 controls co-op loading.");return;}
        if(Busy) {Tell("Wait for the current save operation to finish.");return;}
        _catalogueRecoveryTried=false;Status="Reading co-op save...";_read=Task.Run(()=>_store.Read(slot));
    }
    internal void Tick()
    {
        if(_disposed) return;
        try {TickCore();}
        catch(Exception e) {Failure(e);}
    }
    private void TickCore()
    {
        var connected=_session.Snapshot.Connected;
        if(connected!=_connected)
        {
            _connected=connected;ClearNativeTransfer();_assembler.Reset();_out.Clear();_sentEpoch=0;_worldSentEpoch=0;_awaitNative=connected && _nativeBaseline!=null && _baselineLevel==_life.State.Level;_peerIdentity="";_waitingIdentity="";_approved="";_asking=false;
            if(connected)
            {
                _deadline=Time.unscaledTime+60;
                if(_life.IsClient) Send(SaveCommand.Identity,JsonSerializer.SerializeToUtf8Bytes(_identity),0);
            }
            else if(_request!=null) throw new IOException("Partner disconnected during save capture; the previous save was retained.");
            else {_life.PersistenceHold=_restore!=null && _restoreStarted;_restoredAck=true;}
        }
        if(_controlEpoch!=_life.Epoch)
        {
            _controlEpoch=_life.Epoch;ClearNativeTransfer();_out.Clear();_assembler.Reset();_guestNativeWork?.Dispose();_guestNativeWork=null;_incomingNative=null;_incomingRestore=null;_pendingCapture=0;_captureHold=false;_captureAuto=false;
            if(connected && _life.IsClient) Send(SaveCommand.Identity,JsonSerializer.SerializeToUtf8Bytes(_identity),0);
        }
        foreach(var page in _session.DrainSaves())
        {
            if(page.Command!=SaveCommand.Identity && page.Epoch!=_life.Epoch) continue;
            var bytes=_assembler.Accept(page);if(bytes!=null) Receive(page,bytes);
        }
        if(_read?.IsCompleted==true)
        {
            var loaded=_read.GetAwaiter().GetResult();_read=null;
            CoopSaveStore.RequireRestorableFormat(loaded.Save);
            if(loaded.Save.Game!=NativeSaveBridge.GameKey) throw new InvalidDataException("This save belongs to a different game build.");
            if(!SessionLifecycle.Supported(SessionLifecycle.FindLevel(loaded.Save.Level))) throw new InvalidDataException("This co-op save uses unsupported content.");
            _restoreStarted=false;_restore=loaded.Save;_guest=_restore.Guest;_approved="";_restoreSent=false;_restoredAck=!connected;
            if(loaded.Recovered) Tell(_catalogueRecoveryTried?"The primary world catalogue was incompatible. Recovered the previous compatible backup; original files were preserved.":"Recovered the previous valid co-op save backup.");
        }
        if(_waitingIdentity.Length>0 && _remoteSteam()!=0) {AcceptIdentity(_waitingIdentity);_waitingIdentity="";}
        if(!_life.IsClient && connected && _peerIdentity.Length>0) CheckAssignment();
        if(_restore!=null && !_restoreStarted)
        {
            if((_life.NativeReady || _life.State.Action==TravelAction.Menu) && (!connected || Assigned)) StartRestore();
        }
        if(_life.NativeReady && _world.Active && _generation!=_life.MapGeneration)
        {
            if(_restore==null && (!_life.State.Joining || _baselineLevel!=_life.State.Level)) {_nativeBaseline=null;_awaitNative=false;}
            _generation=_life.MapGeneration;_entries=NativeSaveBridge.Entries();_pickupKeys.Clear();_collected.Clear();_readyAt=Time.unscaledTime+1;
            foreach(var entry in _entries) if(entry.Component.TryCast<Pickup>() is {} pickup && pickup.TryCast<CodexPickup>()==null)
            {var key="a:"+entry.Guid;_pickupKeys[pickup.Pointer]=key;if(pickup._pickedUp) _collected.Add(key);}
        }
        if(_restore!=null && _life.NativeReady && _world.Active && _work==null && _life.RestoringWorld)
        {
            if(_restore.Catalogue!=_world.PersistentManifest)
            {
                if(_catalogueRecoveryTried) throw new InvalidDataException("Saved map catalogue is incompatible with this installation. Original files were preserved.");
                _catalogueRecoveryTried=true;
                var slot=_restore.Slot;var game=NativeSaveBridge.GameKey;var catalogue=_world.PersistentManifest;var level=_life.State.Level;
                _restore=null;_restoreStarted=false;
                _read=Task.Run(()=>_store.ReadCompatibleRecovery(slot,game,catalogue,level));
                Status="Checking the previous compatible recovery backup...";
                return;
            }
            _restoringNative=true;_work=RestoreWorld(_restore).GetEnumerator();Status="Restoring shared world...";
        }
        if(_work!=null)
        {
            var deadline=Stopwatch.GetTimestamp()+Stopwatch.Frequency*3/1000;
            while(_work!=null && Stopwatch.GetTimestamp()<deadline)
            {
                if(_work.MoveNext()) continue;
                _work.Dispose();_work=null;
                if(_restoringNative)
                {
                    _restoringNative=false;_life.RestoringWorld=false;_life.CompletePersistentRestore();_sentEpoch=0;
                    if(!connected) FinishRestore();
                }
                else CompleteCapture();
            }
        }
        TickNativeJoin(connected);
        if(!_life.IsClient && connected && Assigned && _life.NativeReady && _world.Active && !_life.RestoringWorld && _joinCapture==null && _joinCaptureEpoch==_life.Epoch && _nativeBaseline!=null && _baselineLevel==_life.State.Level && _worldSentEpoch!=_life.Epoch)
        {
            _awaitNative=true;_worldSentEpoch=_life.Epoch;_deadline=Time.unscaledTime+120;
            var guestWorld=new Dictionary<string,Dictionary<string,string>>(StringComparer.Ordinal);
            foreach(var entry in _entries)
                if(entry.Component!=null && entry.Component.TryCast<NpcController>()==null && !NativeSaveBridge.NpcOwned(entry.Component) && _nativeBaseline.TryGetValue(entry.Guid,out var data)) guestWorld[entry.Guid]=data;
            SendBaseline(guestWorld);
        }
        if(!_life.IsClient && connected && Assigned && _life.NativeReady && _world.Active && !_life.RestoringWorld && !_awaitNative && _sentEpoch!=_life.Epoch)
        {
            _restoreSent=_restore!=null;_restoredAck=false;
            var record=_guest?.Level==_life.State.Level?_guest:null;
            var catchUp=_life.State.Joining || _restore!=null;
            if(!catchUp) record=null;
            Send(SaveCommand.Restore,JsonSerializer.SerializeToUtf8Bytes(new RestorePlayer(record,catchUp?_life.PlayerState.CaptureJoin():null,_restore!=null,catchUp)),_life.Epoch);
            _sentEpoch=_life.Epoch;_deadline=Time.unscaledTime+120;
        }
        if(_life.IsClient) TickClient();
        else
        {
            if(_restore!=null && Time.unscaledTime>_deadline) throw new TimeoutException("Co-op restoration timed out before both players were ready.");
            if(_restore!=null && _restoreSent && _restoredAck && !_life.RestoringWorld && _work==null) FinishRestore();
            if(_request==null && _work==null && _write==null && _restore==null && _pending.Count>0 && Safe && (!connected || Assigned && _life.State.Released))
            {
                _request=_pending.Dequeue();_life.PersistenceHold=true;_holdFrame=Time.frameCount;_holdAck=!connected;_captureTransaction=Next();_deadline=Time.unscaledTime+30;
                if(connected) Send(SaveCommand.Capture,new[]{_request.Slot.StartsWith("auto-",StringComparison.Ordinal)?(byte)1:(byte)0},_life.Epoch,_captureTransaction);
                Status="Capturing co-op save...";
            }
            if(_request!=null && _holdAck && Time.frameCount>_holdFrame && _work==null && _document==null)
            {_document=Document(_request);_work=CaptureWorld(_document).GetEnumerator();}
            if(_request!=null && Time.unscaledTime>_deadline) throw new TimeoutException("Partner did not acknowledge the save capture.");
            if(_request==null && _restore==null && _life.NativeReady && _world.Active && _readyAt>0 && Time.unscaledTime>=_readyAt && _life.State.Released)
            {_readyAt=0;_autosave.Reset();}
            if(AutosEnabled)
            {
                _autosave.Advance(Time.unscaledDeltaTime,_life.NativeReady && _world.Active && _life.State.Released && !_life.Frozen && !Busy && !_life.StoryActive && !_life.LocalTutorialActive);
                AutoSave();
            }
        }
        if(_write?.IsCompleted==true)
        {_write.GetAwaiter().GetResult();_write=null;_autosave.Reset();CompletedWritesForTest++;Tell("Co-op save written.");Status=_pending.Count>0?"Save pending.":"";}
        if(connected && _peerIdentity.Length==0 && !_life.IsClient && Time.unscaledTime>_deadline)
            _session.DropPeer("P2 did not provide a multiplayer player identity.");
        DrainTransfers();
    }
    private bool Assigned=>_peerIdentity.Length>0 && (_guest==null || _guest.Identity==_peerIdentity || _approved==_peerIdentity);
    private void CheckAssignment()
    {
        if(Assigned) return;
        _life.PersistenceHold=true;
        if(_asking) return;_asking=true;
        _confirm("This save belongs to a different partner. Assign the saved P2 slot and its personal progress to the joining player?",
            ()=>{_approved=_peerIdentity;if(_guest!=null) _guest=_guest with {Identity=_peerIdentity};_asking=false;if(_restore==null) _life.PersistenceHold=false;},
            ()=>{_asking=false;_session.DropPeer("P1 declined assigning the saved partner slot.");_life.PersistenceHold=false;});
    }
    private bool Safe=>!_world.HasPendingInteractions && _life.NativeReady && _world.Active && Player.Instance?.IsAlive==true && !_life.PlayerState.Pending && !_life.RestoringWorld &&
        (!_session.Snapshot.Connected || _session.Snapshot.RemotePose is {Alive:true,Ready:true} pose && pose.Epoch==_life.Epoch) &&
        !_life.StoryActive && !_life.LocalTutorialActive;
    private void TickClient()
    {
        if(_incomingNative!=null && _life.NativeReady && _world.Active && _life.State.HostReady && _guestNativeWork==null)
        {_life.PersistenceHold=true;_guestNativeWork=RestoreGuestNative(_incomingNative).GetEnumerator();}
        if(_guestNativeWork!=null)
        {
            var deadline=Stopwatch.GetTimestamp()+Stopwatch.Frequency*3/1000;
            while(Stopwatch.GetTimestamp()<deadline)
            {
                if(_guestNativeWork.MoveNext()) continue;
                _guestNativeWork.Dispose();_guestNativeWork=null;_incomingNative=null;_life.PersistenceHold=false;
                Send(SaveCommand.WorldRestored,Array.Empty<byte>(),_life.Epoch);break;
            }
        }
        if(_incomingRestore!=null && _incomingEpoch==_life.Epoch && _life.NativeReady && _world.Active && _life.WorldReady && _life.State.HostReady)
        {
            var payload=_incomingRestore;_incomingRestore=null;
            if(payload.Player!=null)
            {
                CoopSaveStore.ValidatePlayer(payload.Player);
                if(payload.Player.Identity!=_identity || payload.Player.Level!=_life.State.Level) throw new InvalidDataException("P2 personal save identity/map mismatch.");
                _collected.Clear();foreach(var key in payload.Player.Collected) _collected.Add(key);
                if(payload.Loading) NativeSaveBridge.LoadPlayer(payload.Player.Native);
                else RestoreNonAmmo(payload.Player);
                RestorePickups();
            }
            if(payload.CatchUp && payload.Loadout!=null)
            {
                var player=Player.Instance;player.GetComponent<PlayerPowerupHandlerComponent>().ClearAllPowerups();
                _life.PlayerState.ApplyJoin(payload.Loadout,!payload.Loading);
                var health=player.GetComponent<PlayerHitProcessorComponent>();
                if(!player.IsAlive) player.Respawn();health.SetCurrentHitPoints(player.HitPoints.Max);health.SetArmor(0);

            }
            if(payload.Loading) {_world.RestoreCarrier(payload.Player?.Carrier);EnsureSafePosition();}
            _world.RebasePlatforms();_life.CompletePersonalRestore();
            Send(SaveCommand.Restored,Array.Empty<byte>(),_life.Epoch,_incomingTransaction);
            _profileAt=0;
        }
        if(_pendingCapture!=0 && Safe && _life.PersonalReady && Time.frameCount>_clientHoldFrame)
        {
            _life.PersistenceHold=true;
            Send(SaveCommand.HoldAck,JsonSerializer.SerializeToUtf8Bytes(PlayerRecord()),_life.Epoch,_pendingCapture);_pendingCapture=0;
        }
        if(_session.Snapshot.Connected && _life.PersonalReady && _life.NativeReady && _world.Active && Player.Instance.IsAlive && !_life.PersistenceHold && !_world.HasPendingInteractions && _out.Count==0 && Time.unscaledTime>=_profileAt)
        {_profileAt=Time.unscaledTime+2;Send(SaveCommand.GuestState,JsonSerializer.SerializeToUtf8Bytes(PlayerRecord()),_life.Epoch);}
    }
    private void Receive(SavePage page,byte[] bytes)
    {
        if(page.Command==SaveCommand.Identity)
        {
            var identity=JsonSerializer.Deserialize<string>(bytes)??"";
            if(_identity.StartsWith("steam:",StringComparison.Ordinal) && _remoteSteam()==0) {_waitingIdentity=identity;return;}
            AcceptIdentity(identity);return;
        }
        if(page.Epoch!=_life.Epoch) return;
        switch(page.Command)
        {
            case SaveCommand.GuestState:
            case SaveCommand.HoldAck:
                if(!Assigned || _restore!=null) return;
                var record=JsonSerializer.Deserialize<SavedPlayer>(bytes)??throw new InvalidDataException("Missing personal state.");CoopSaveStore.ValidatePlayer(record);
                if(record.Identity!=_peerIdentity || record.Level!=_life.State.Level) throw new InvalidDataException("Partner personal state identity/map mismatch.");
                if(page.Command==SaveCommand.HoldAck && (_request==null || page.Transaction!=_captureTransaction)) return;
                _guest=record;if(page.Command==SaveCommand.HoldAck) _holdAck=true;break;
            case SaveCommand.Capture:
                if(bytes.Length>1 || bytes.Length==1 && bytes[0]>1) throw new InvalidDataException("Invalid save capture kind.");
                _captureHold=true;_captureAuto=bytes.Length==1 && bytes[0]==1;
                _pendingCapture=page.Transaction;_clientHoldFrame=Time.frameCount;_life.PersistenceHold=true;break;
            case SaveCommand.Resume: _life.PersistenceHold=false;_pendingCapture=0;_captureHold=false;_captureAuto=false;break;
            case SaveCommand.Restore:
                _incomingRestore=JsonSerializer.Deserialize<RestorePlayer>(bytes)??throw new InvalidDataException("Missing personal restore.");
                _incomingEpoch=page.Epoch;_incomingTransaction=page.Transaction;_life.PersonalReady=false;break;
            case SaveCommand.WorldRestoreBegin:
            case SaveCommand.WorldRestore:
            case SaveCommand.WorldRestoreEnd:
                ReceiveBaseline(page.Command,bytes);break;
            case SaveCommand.WorldRestored:
                if(_worldSentEpoch==page.Epoch) {_awaitNative=false;_baselineAckEpoch=page.Epoch;}break;
            case SaveCommand.Restored:
                if(_sentEpoch==page.Epoch) _restoredAck=true;break;
            case SaveCommand.Checkpoint:
                var checkpoint=JsonSerializer.Deserialize<CheckpointRequest>(bytes)??throw new InvalidDataException("Missing checkpoint request.");
                var pose=_session.Snapshot.RemotePose;
                if(pose?.Alive!=true || !pose.Ready || pose.Epoch!=page.Epoch || Vector3.Distance(new(pose.X,pose.Y,pose.Z),SharedWorld.Unity(checkpoint.Feet))>3) return;
                foreach(var point in Object.FindObjectsOfType<PlayerTriggerCheckpointComponent>())
                    if(point.enabled && point.GetComponent<PlayerTrigger>()._enabled && NativeSaveBridge.CheckpointKey(point)==checkpoint.Key && point.GetComponents<Collider>().Any(c=>c.enabled && c.bounds.Intersects(new Bounds(new Vector3(pose.X,pose.Y+pose.Height/2,pose.Z),new Vector3(.8f,pose.Height,.8f)))))
                    {Checkpoint(point,true);break;}
                break;
        }
    }
    private void AcceptIdentity(string identity)
    {
        var steam=_remoteSteam();
        if(_identity.StartsWith("steam:",StringComparison.Ordinal))
        {if(steam==0 || identity!="steam:"+steam) throw new InvalidDataException("Steam identity did not match the connected partner.");}
        else if(!identity.StartsWith("direct:",StringComparison.Ordinal) || !Guid.TryParseExact(identity[7..],"N",out _)) throw new InvalidDataException("Invalid Direct IP player identity.");
        if(_peerIdentity.Length>0 && _peerIdentity!=identity) throw new InvalidDataException("Partner changed identity within a connection.");
        _peerIdentity=identity;
    }
    internal void Checkpoint(PlayerTriggerCheckpointComponent point,bool fromPeer=false)
    {
        if(!_life.NativeReady || !_world.Active || (!fromPeer && !Player.Instance.IsAlive) || Time.unscaledTime<_checkpointAt) return;
        _checkpointAt=Time.unscaledTime+2;CheckpointRequestsForTest++;
        if(_life.IsClient) Send(SaveCommand.Checkpoint,JsonSerializer.SerializeToUtf8Bytes(new CheckpointRequest(NativeSaveBridge.CheckpointKey(point),SharedWorld.Net(Player.Instance.FeetPosition),Player.Instance.Height)),_life.Epoch);
        else AutoSave();
    }
    private static Dictionary<string,string> CampaignState()
    {var result=new Dictionary<string,string>(StringComparer.Ordinal);foreach(var level in LevelManager.AllLevels) result[level.ID]=level.Save();return result;}
    private CoopSave Document(Request request)=>new()
    {
        Game=NativeSaveBridge.GameKey,Mod=Plugin.Version,Catalogue=_world.PersistentManifest,Slot=request.Slot,Name=request.Name,
        Level=_life.State.Level,Difficulty=SessionLifecycle.Difficulty(),Host=PlayerRecord(),Guest=_guest,
        Campaign=CampaignState()
    };
    private IEnumerable<int> CaptureWorld(CoopSave document)
    {
        _entries=NativeSaveBridge.Entries();
        foreach(var entry in _entries)
        {
            if(entry.Component==null || NativeSaveBridge.Personal(entry.Component) || NativeSaveBridge.NpcOwned(entry.Component) || entry.Component.TryCast<LootController>()!=null) continue;
            Dictionary<string,string>? state=null;
            foreach(var step in CaptureNativeEntry(entry,v=>state=v)) yield return step;
            if(state!=null) document.Native.Add(entry.Guid,state);
            yield return 0;
        }
        var frames=new List<WorldFrame>();foreach(var step in _world.CapturePersistent(frames)) yield return step;
        document.World=frames.ToArray();
    }
    private static bool PassiveNative(UnityEngine.Component c)=>c.TryCast<BreakableObject>()!=null || c.TryCast<CollisionBreakableObject>()!=null ||
        c.TryCast<FallingPlatform>()!=null || c.TryCast<WireWallGate>()!=null || c.TryCast<Mover>()!=null || c.TryCast<Rotater>()!=null ||
        c.TryCast<MultiRotater>()!=null || c.TryCast<Elevator>()!=null || c.TryCast<Scaler>()!=null || c.TryCast<TimelineSequence>()!=null || c.TryCast<RocketSequence>()!=null ||
        c.TryCast<ExplosiveBarrel>()!=null || c.TryCast<BurnableObject>()!=null || c.TryCast<PhysicalObject>()!=null || c.TryCast<PhysicalObjectSpawner>()!=null || c.TryCast<Cannon>()!=null || c.TryCast<CovidArenaController>()!=null || c.TryCast<AntiCovidSyringeController>()!=null || c.TryCast<CovidArenaBase>()!=null;
    private IEnumerable<int> RestoreGuestNative(Dictionary<string,Dictionary<string,string>> entries)
    {
        if(entries.Count>32768) throw new InvalidDataException("Native world baseline exceeds its entry bound.");
        var available=_entries.ToDictionary(e=>e.Guid,StringComparer.Ordinal);
        _world.BeginGuestRestore();
        try
        {
            _world.ClearTransientForRestore();_world.ResetPropSpawnersForRestore();
            foreach(var pair in entries)
            {
                if(!available.TryGetValue(pair.Key,out var entry) || NativeSaveBridge.Personal(entry.Component) || entry.Component.TryCast<NpcController>()!=null || entry.Component.TryCast<LootController>()!=null || NativeSaveBridge.NpcOwned(entry.Component))
                    throw new InvalidDataException("Unknown or personal native world restore entry: "+pair.Key);
                try
                {
                    if(entry.Component.TryCast<PhysicalObjectSpawner>() is {} spawner)
                    {spawner.StopAllCoroutines();spawner.Load(new());}
                    else if(!PassiveNative(entry.Component) && !_world.RestoreHingedDoor(entry.Component,pair.Value)) entry.Component.Cast<ISaveable>().Load(NativeSaveBridge.Native(pair.Value));
                }
                catch(Exception error) {throw new InvalidDataException("Guest native restore failed: "+entry.Component.GetIl2CppType().Name+" / "+pair.Key,error);}
                yield return 0;
            }
        }
        finally {_world.EndGuestRestore();}
    }
    private void CompleteCapture()
    {
        var document=_document!;CoopSaveStore.Validate(document);_document=null;_request=null;
        _life.PersistenceHold=false;if(_session.Snapshot.Connected) Send(SaveCommand.Resume,Array.Empty<byte>(),_life.Epoch);
        Status="Writing co-op save...";_write=Task.Run(()=>_store.Write(document));
    }
    private void StartRestore()
    {
        var save=_restore!;_nativeBaseline=save.Native;_baselineLevel=save.Level;_awaitNative=_session.Snapshot.Connected;_worldSentEpoch=0;_restoreStarted=true;_life.PersistenceHold=true;_life.RestoringWorld=true;_life.WorldReady=false;_pending.Clear();
        _life.Authorized(()=>SettingsManager.GameplaySettings.SetDifficulty(DifficultyManager.Difficulties[save.Difficulty]));
        foreach(var level in LevelManager.AllLevels) if(save.Campaign.TryGetValue(level.ID,out var data)) level.Load(data);
        var target=SessionLifecycle.FindLevel(save.Level)!;
        var sameMap=SessionManager.SessionActive && _life.NativeReady && LevelController._instance?.LevelId?.ID==save.Level;
        _life.BeginHost(sameMap?TravelAction.Restart:TravelAction.Load,target,true);
        _life.Authorized(()=>{if(sameMap) GameManager.Load(null);else GameManager.LoadLevel(target,true);});
        Status="Loading co-op save...";_deadline=Time.unscaledTime+120;
    }
    private IEnumerable<int> RestoreWorld(CoopSave save)
    {
        // Match the native StartLevel save restore precondition: existing actors must be pooled first.
        _world.ClearTransientForRestore();
        Object.FindObjectOfType<SpawnPointsController>().DespawnNpcs();
        _world.ReservePersistentIds(save.World);
        _world.ResetPropSpawnersForRestore();
        var available=_entries.ToDictionary(e=>e.Guid,StringComparer.Ordinal);
        foreach(var pair in save.Native)
            if(!available.ContainsKey(pair.Key)) throw new InvalidDataException("Missing authored save entry: "+pair.Key);
        foreach(var entry in _entries)
        {
            if(!save.Native.TryGetValue(entry.Guid,out var data)) continue;
            try
            {
                var c=entry.Component;
                if(c.TryCast<PhysicalObjectSpawner>() is {} spawner) {spawner.StopAllCoroutines();spawner.Load(new());}
                // These families are restored from durable adapters. Native Load may
                // explode, emit progression callbacks, spawn debris or restart a story.
                var adapted=c.TryCast<BreakableObject>()!=null || c.TryCast<CollisionBreakableObject>()!=null ||
                    c.TryCast<FallingPlatform>()!=null || c.TryCast<WireWallGate>()!=null || c.TryCast<TimelineSequence>()!=null || c.TryCast<RocketSequence>()!=null ||
                    c.TryCast<ExplosiveBarrel>()!=null || c.TryCast<BurnableObject>()!=null || c.TryCast<PhysicalObject>()!=null || c.TryCast<PhysicalObjectSpawner>()!=null || c.TryCast<CovidArenaController>()!=null || c.TryCast<AntiCovidSyringeController>()!=null || c.TryCast<CovidArenaBase>()!=null;
                if(!adapted && !_world.RestoreNativeMachine(c,data) && !_world.RestoreHingedDoor(c,data)) c.Cast<ISaveable>().Load(NativeSaveBridge.Native(data));
            }
            catch(Exception error) {throw new InvalidDataException("Native restore failed: "+entry.Component.GetIl2CppType().Name+" / "+entry.Guid,error);}
            yield return 0;
        }
        foreach(var step in _world.RestorePersistent(save.World)) yield return step;
        _world.RestoreLegacyPosters(save);
        NativeSaveBridge.LoadPlayer(save.Host.Native);_collected.Clear();foreach(var key in save.Host.Collected) _collected.Add(key);
        RestorePickups();_world.RestoreCarrier(save.Host.Carrier);EnsureSafePosition();_world.ResumeNativeMachines(save.World);_life.WorldReady=true;
    }
    private void FinishRestore()
    {
        _restore=null;_restoreStarted=false;_life.PersistenceHold=false;_life.RestoringWorld=false;_restoreSent=false;_readyAt=0;_autosave.Reset();
        Tell("Co-op save restored.");Status="";
    }
    private SavedPlayer PlayerRecord()
    {
        foreach(var pickup in Object.FindObjectsOfType<Pickup>(true))
            if(pickup.TryCast<CodexPickup>()==null && pickup._pickedUp && (_world.DropKey(pickup)??_pickupKeys.GetValueOrDefault(pickup.Pointer)) is {} key) _collected.Add(key);
        return new(_identity,_life.State.Level,NativeSaveBridge.Player(),_collected.OrderBy(k=>k,StringComparer.Ordinal).ToArray(),_world.CaptureCarrier());
    }
    internal string? CollectionKey(Pickup pickup)=>_world.DropKey(pickup)??_pickupKeys.GetValueOrDefault(pickup.Pointer);
    internal void CollectedKey(string? key) {if(key!=null) {_collected.Add(key);_profileAt=0;}}
    internal void Collected(Pickup pickup)=>CollectedKey(CollectionKey(pickup));
    internal void RestoreDropCollection(Pickup pickup,string key)
    {if(_collected.Contains(key)) {pickup.SetEnabled(true);}}
    private void RestorePickups()
    {
        foreach(var pickup in Object.FindObjectsOfType<Pickup>(true))
        {
            if(pickup.TryCast<CodexPickup>()!=null) continue;
            var key=_world.DropKey(pickup)??_pickupKeys.GetValueOrDefault(pickup.Pointer);if(key==null) continue;
            pickup.SetEnabled(_collected.Contains(key));
        }
    }
    private static void RestoreNonAmmo(SavedPlayer record)
    {
        var player=Player.Instance;var position=player.Transform.position;var look=player.WorldCamera.transform.eulerAngles;
        // Let the game's fixed player serializer restore non-ammo inventory and then replace gear below.
        NativeSaveBridge.LoadPlayer(record.Native);player.Teleport(position,look,Vector3.zero);
    }
    private void EnsureSafePosition()
    {
        var player=Player.Instance;var feet=player.FeetPosition;
        var mask=~(1<<player.gameObject.layer);
        if(Physics.Raycast(feet+Vector3.up*.5f,Vector3.down,out var hit,3,mask,QueryTriggerInteraction.Ignore) && Vector3.Dot(hit.normal,Vector3.up)>.5f &&
            !Physics.CheckCapsule(feet+Vector3.up*.4f,feet+Vector3.up*(player.Height-.35f),.3f,mask,QueryTriggerInteraction.Ignore)) return;
        if(PlayerSessionState.FindNearby(player,feet,out var position)) player.Teleport(position,player.WorldCamera.transform.eulerAngles,Vector3.zero);
        else player.Teleport(NativeSaveBridge.PlayerPoint().transform.position,NativeSaveBridge.PlayerPoint().transform.eulerAngles,Vector3.zero);
        Tell("Saved position was obstructed; used a safe spawn position.");
    }
    private uint Next()=>checked(++_transaction);
    private void Send(SaveCommand command,byte[] bytes,uint epoch,uint transaction=0)
    {
        if(!_session.Snapshot.Connected) return;
        if(bytes.Length>SaveProtocol.MaxBytes || _transfers.Count>=128 || _transferBytes+bytes.Length>CoopSaveStore.MaxBytes+65536L)
            throw new InvalidOperationException("Persistence transfer backlog exceeded its bound.");
        _transfers.Enqueue((command,bytes,epoch,transaction==0?Next():transaction));_transferBytes+=bytes.Length;
    }
    private void Tell(string message) {_notice(message);Plugin.Logger.LogInfo(message);}
    private void Failure(Exception error)
    {
        Plugin.Logger.LogError("Co-op persistence failed: "+error);Tell("Co-op save/load failed: "+error.Message);
        ClearNativeTransfer();_out.Clear();_work?.Dispose();_work=null;_guestNativeWork?.Dispose();_guestNativeWork=null;_incomingNative=null;_awaitNative=false;_pending.Clear();_request=null;_document=null;_read=null;_write=null;
        _life.PersistenceHold=false;_pendingCapture=0;_captureHold=false;_captureAuto=false;
        if(_life.IsClient) {_life.StopClient("Co-op restoration failed: "+error.Message);return;}
        if(_restore!=null || _restoringNative || _life.RestoringWorld)
        {
            _restore=null;_restoreStarted=false;_restoringNative=false;_life.RestoringWorld=false;
            _life.BeginHost(TravelAction.Menu,null);_life.Authorized(GameManager.OpenTitle);
        }
        if(!_life.IsClient && _session.Snapshot.Connected)
        {
            if(_life.State.Joining) _session.DropPeer("Current world could not be restored for P2. Rejoin after P1 finishes loading.");
            else Send(SaveCommand.Resume,Array.Empty<byte>(),_life.Epoch);
        }
        Status="Save operation failed; previous save retained.";
    }
    public void Dispose()
    {
        _disposed=true;ClearNativeTransfer();_guestNativeWork?.Dispose();_guestNativeWork=null;_work?.Dispose();_work=null;_out.Clear();_assembler.Reset();_life.PersistenceHold=false;_life.RestoringWorld=false;
        if(_read!=null) _read.ContinueWith(t=>Plugin.Logger.LogError("Co-op save read: "+t.Exception),TaskContinuationOptions.OnlyOnFaulted);
        if(_write!=null) _write.ContinueWith(t=>Plugin.Logger.LogError("Co-op save disk write: "+t.Exception),TaskContinuationOptions.OnlyOnFaulted);
        if(Current==this) Current=null;
    }
}
