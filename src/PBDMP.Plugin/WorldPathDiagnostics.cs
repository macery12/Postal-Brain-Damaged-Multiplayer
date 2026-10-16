using Il2CppInterop.Runtime.Attributes;
using Hyperstrange.Base;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using PBDMP.Network;
using UnityEngine;

namespace PBDMP.Plugin;

// Opt-in native acceptance fixtures; never selected during ordinary gameplay.
public sealed partial class MultiplayerBehaviour
{
    private int _pathStep,_pathCount,_pathCallbacks;
    private float _pathAt,_pathStarted,_pathRemaining,_pathClock;
    private uint _pathEpoch;
    private string _pathKey="";
    private Vector3 _pathHostFeet;
    private ExplosiveBarrel? _pathBarrel;
    private Il2CppSystem.Action<int>? _pathHandler;
    private string PathMode=>Environment.GetEnvironmentVariable("PBDMP_PATH_TEST")!;
    private int SecretCount=>LevelController._instance.GetComponent<SecretsController>().FoundSecretsCount;
    private PlayerTriggerSecretComponent PathSecret=>_world!.ComponentForTest(_pathKey)!.Cast<PlayerTriggerSecretComponent>();
    private void RunWorldPathTest()
    {
        if(PathMode=="hazard") {RunHazardTest();return;}
        if(PathMode=="gravity") {RunGravityTest();return;}
        if(PathMode=="pusher") {RunPusherTest();return;}
        if(PathMode=="conveyor") {RunConveyorTest();return;}
        if(PathMode is "teleport" or "cannon") {RunWorldActionTest();return;}
        if(_pathStep==99) return;
        if(_pathStarted==0) _pathStarted=Time.unscaledTime;
        if(Time.unscaledTime-_pathStarted>240) throw new TimeoutException("World path fixture stalled: "+PathMode+"/"+_pathStep);
        File.WriteAllText(Marker(TravelRole+"-path-state.txt"),$"mode={PathMode}, step={_pathStep}, status={_saves?.Status}, frozen={_lifecycle?.Frozen}, alive={Player.Instance?.IsAlive}");
        if(_pathStep==0)
        {
            _port!.text=Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!;
            if(TravelRole=="host") {Host();SharedWorldPatches.DiagnosticFreeze=true;GameManager.LoadLevel(TestLevel(),true);}
            else {_address!.text="127.0.0.1";Join();}
            _pathStep=1;return;
        }
        if(_lifecycle is {NativeReady:false}) {_lifecycle.AutoContinue();return;}
        if(_lifecycle?.LocalTutorialActive==true)
        {Player.Instance!.GetComponent<Hyperstrange.PBD.PlayerComponents.PlayerTutorialComponent>().OnUseDown();return;}
        if(TravelRole=="host") WorldPathHost();else WorldPathClient();
    }
    private IEnumerator<int>? _diagnosticCapture;
    private List<WorldFrame>? _diagnosticFrames;
    private bool _diagnosticCapturePaused;
    [HideFromIl2Cpp]
    private bool TryDiagnosticWorldCapture(out List<WorldFrame> frames)
    {
        frames=null!;
        if(_diagnosticCapture==null)
        {
            _diagnosticCapturePaused=_lifecycle!.State.Paused;
            _lifecycle.HostPauseRequest(true);
            _diagnosticFrames=new();
            _diagnosticCapture=_world!.CapturePersistent(_diagnosticFrames).GetEnumerator();
            // Apply the shared hold before reading the native world. Capture
            // yields under queue pressure, so never drain it synchronously.
            return false;
        }
        var deadline=System.Diagnostics.Stopwatch.GetTimestamp()+System.Diagnostics.Stopwatch.Frequency*3/1000;
        while(System.Diagnostics.Stopwatch.GetTimestamp()<deadline)
        {
            if(_diagnosticCapture.MoveNext()) continue;
            _diagnosticCapture.Dispose();_diagnosticCapture=null;
            frames=_diagnosticFrames!;_diagnosticFrames=null;
            _lifecycle!.HostPauseRequest(_diagnosticCapturePaused);
            return true;
        }
        return false;
    }
    [HideFromIl2Cpp]
    private bool TryPathBarrelState(out WorldFrame frame)
    {
        frame=null!;
        if(!TryDiagnosticWorldCapture(out var frames)) return false;
        frame=WorldPathFrames.Find(frames,WorldKind.Scenery,3,_pathKey);
        return true;
    }
    private void ObservePathBarrel()
    {
        _pathBarrel=_world!.ComponentForTest(_pathKey)!.Cast<ExplosiveBarrel>();_pathCallbacks=0;
        _pathHandler=(Action<int>)(state=>{_pathCallbacks++;File.AppendAllText(Marker("barrel-callbacks.txt"),$"state={state}, time={Time.time}, exploded={_pathBarrel?._exploded}\n");});
        _pathBarrel.OnStateChanged=Il2CppSystem.Delegate.Combine(_pathBarrel.OnStateChanged,_pathHandler)?.Cast<Il2CppSystem.Action<int>>();
    }
    private void EnterPathSecret()
    {
        var secret=PathSecret;var trigger=secret.GetComponent<PlayerTrigger>();
        var contact=trigger.GetComponents<Collider>().First(c=>c.enabled && c.isTrigger);
        var feet=contact.bounds.center-Vector3.up*(Player.Instance.Height*.5f);
        Player.Instance.Teleport(feet,Vector3.zero,Vector3.zero);
        // Run the native trigger entry against its real player collider. P1's
        // ordinary pose/occupancy validation must still authorize the result.
        trigger.OnTriggerEnter(Player.Instance.Collider);
    }
    private void WorldPathHost()
    {
        switch(_pathStep)
        {
            case 1 when _world?.Active==true:
                QuietNpcs();_pathHostFeet=Player.Instance.FeetPosition;
                if(PathMode=="secrets")
                {
                    _pathCount=SecretCount;
                    _pathKey=_world.CatalogueForTest.SecretKeys.First(k=>_world.ComponentForTest(k) is {} c && c.gameObject.activeInHierarchy && c.GetComponent<PlayerTrigger>() is {} t && t._enabled && !t._triggeredOnEnter && t.GetComponents<Collider>().Any(col=>col.enabled && col.isTrigger));
                }
                else
                {
                    var barrel=_world.CatalogueForTest.SceneryKeys.Select(k=>_world.ComponentForTest(k)?.TryCast<ExplosiveBarrel>()).Where(b=>b!=null && b.gameObject.activeInHierarchy && !b._exploded && Vector3.Distance(b.transform.position,Player.Instance.FeetPosition)>40).OrderByDescending(b=>Vector3.Distance(b!.transform.position,Player.Instance.FeetPosition)).First()!;
                    _pathKey=_world.KeyForTest(barrel)!;
                }
                File.WriteAllText(Marker("path-key"),_pathKey);TravelMark("host-ready");_pathStep=2;break;
            case 2 when _lifecycle!.State.Released && Marked("path-client-ready"):
                GameManager.DisableMenu();TimeManager.SetApplicationTimeScale(1);
                if(PathMode=="secrets") {_saves!.RequestSave("manual-path-base","Secret undiscovered");_pathStep=3;break;}
                ObservePathBarrel();_pathBarrel!._chainReactionDelay=8;
                _pathBarrel.Hit(new HitData{dealer=Player.Instance.Cast<IAiActor>(),damage=1,isExplosion=true,weakSpotMultiplier=1,position=_pathBarrel.transform.position});
                TravelCheck(_pathBarrel._explosionQueued && !_pathBarrel._exploded,"native explosion hit starts a delayed barrel chain rather than exploding immediately");
                _pathAt=Time.time+2;_pathStep=30;break;
            case 3 when !_saves!.Busy:
                TravelMark("path-go");_pathStep=4;_pathAt=Time.unscaledTime+8;break;
            case 4 when Marked("path-client-entered"):
                if(SecretCount!=_pathCount+1 && Time.unscaledTime<_pathAt) return;
                TravelCheck(SecretCount==_pathCount+1,"P2 native secret entry credits P1's shared results exactly once");
                TravelCheck(Vector3.Distance(Player.Instance.FeetPosition,_pathHostFeet)<.3f,"P2 discovery does not move P1");
                PathSecret.OnPlayerEnter(Player.Instance);PathSecret.OnPlayerEnter(Player.Instance);
                TravelCheck(SecretCount==_pathCount+1,"duplicate native secret callbacks do not double-count");
                TravelMark("path-counted");_pathStep=5;break;
            case 5 when Marked("path-client-counted"):
                _pathEpoch=_lifecycle!.Epoch;_saves!.RequestLoad("manual-path-base");_pathStep=6;break;
            case 6 when !_saves!.Busy && _lifecycle!.State.Released && _lifecycle.Epoch>_pathEpoch:
                TravelCheck(SecretCount==_pathCount,"disk rollback restores undiscovered secret count on P1");
                TravelMark("path-rolled-back");_pathStep=7;_pathAt=Time.unscaledTime+8;break;
            case 7 when Marked("path-client-reentered"):
                if(SecretCount!=_pathCount+1 && Time.unscaledTime<_pathAt) return;
                TravelCheck(SecretCount==_pathCount+1,"native secret can be discovered again after rollback");
                _saves!.RequestSave("manual-path-current","Secret discovered");_pathStep=8;break;
            case 8 when !_saves!.Busy:
                var saved=_saves.Store.Read("manual-path-current").Save;
                TravelCheck(WorldPathFrames.Find(saved.World,WorldKind.Mechanism,3,_pathKey).Value==1,"disk save records the discovered secret identity");
                _pathEpoch=_lifecycle!.Epoch;_saves.RequestLoad("manual-path-current");_pathStep=9;break;
            case 9 when !_saves!.Busy && _lifecycle!.State.Released && _lifecycle.Epoch>_pathEpoch:
                TravelCheck(SecretCount==_pathCount+1,"discovered secret survives native disk reconstruction");
                TravelMark("path-restored");_pathStep=10;break;
            case 10 when Marked("path-client-left") && !_session!.Snapshot.Connected:
                TravelMark("path-rejoin");_pathStep=11;break;
            case 11 when Marked("path-client-current") && _lifecycle!.State.Released:
                TravelCheck(SecretCount==_pathCount+1,"late join does not double-credit the shared secret");
                EndFrozenDiagnosticSession();_pathStep=12;break;
            case 12 when Marked("client-pass"):
                TravelMark("host-pass");_pathStep=99;break;
            case 30 when Time.time>=_pathAt:
                if(!TryPathBarrelState(out var current)) return;
                TravelCheck(current.Values![2] is >4 and <7 && current.Values[1]==1 && !_pathBarrel!._exploded,"partially elapsed chain has a finite remaining delay");
                _saves!.RequestSave("manual-path-barrel","Pending native barrel chain");_pathStep=31;break;
            case 31 when !_saves!.Busy:
                var state=WorldPathFrames.Find(_saves.Store.Read("manual-path-barrel").Save.World,WorldKind.Scenery,3,_pathKey);
                _pathRemaining=state.Values![2];
                TravelCheck(_pathRemaining is >0 and <7 && state.Values[1]==1,"disk capture stores remaining barrel delay, not the original full delay");
                if(_pathHandler!=null) _pathBarrel!.OnStateChanged=Il2CppSystem.Delegate.Remove(_pathBarrel.OnStateChanged,_pathHandler)?.Cast<Il2CppSystem.Action<int>>();
                _pathHandler=null;
                _pathEpoch=_lifecycle!.Epoch;_saves.RequestLoad("manual-path-barrel");_pathStep=32;break;
            case 32 when !_saves!.Busy && _lifecycle!.State.Released && _lifecycle.Epoch>_pathEpoch:
                if(!TryPathBarrelState(out var restored)) return;ObservePathBarrel();
                TravelCheck(!_pathBarrel!._exploded && restored.Values![2]>_pathRemaining-.75f && restored.Values[2]<=_pathRemaining,"restoration resumes the saved pending barrel without replaying its explosion");
                _lifecycle.HostPauseRequest(true);_pathClock=restored.Values![2];_pathAt=Time.unscaledTime+1;_pathStep=33;break;
            case 33 when Time.unscaledTime>=_pathAt:
                if(!TryPathBarrelState(out var paused)) return;
                TravelCheck(Mathf.Abs(paused.Values![2]-_pathClock)<.05f && !_pathBarrel!._exploded,"shared pause holds the restored barrel fuse");
                _lifecycle!.HostPauseRequest(false);_pathClock=Time.time;_pathStep=34;break;
            case 34:
                if(!_pathBarrel!._exploded && Time.time-_pathClock<_pathRemaining+2) return;
                if(!TryPathBarrelState(out var explodedState)) return;
                TravelCheck(_pathBarrel._exploded && _pathCallbacks==1 && Time.time-_pathClock<=_pathRemaining+.75f,"restored chain produces one native future explosion within its remaining delay; exploded="+_pathBarrel._exploded+", callbacks="+_pathCallbacks+", elapsed="+(Time.time-_pathClock)+", saved="+_pathRemaining+", remaining="+explodedState.Values![2]);
                _pathAt=Time.time+2;TravelMark("path-barrel-exploded");_pathStep=35;break;
            case 35 when Time.time>=_pathAt:
                if(!Marked("path-client-exploded") && Time.time<_pathAt+5) return;
                TravelCheck(Marked("path-client-exploded"),"P2 receives barrel changes first observed by persistent capture without requiring another world mutation");
                TravelCheck(_pathCallbacks==1 && _session!.Snapshot.Connected,"restored chain does not duplicate its result or disconnect P2");
                if(_pathHandler!=null) _pathBarrel!.OnStateChanged=Il2CppSystem.Delegate.Remove(_pathBarrel.OnStateChanged,_pathHandler)?.Cast<Il2CppSystem.Action<int>>();
                EndFrozenDiagnosticSession();_pathStep=12;break;
        }
    }
    private void WorldPathClient()
    {
        switch(_pathStep)
        {
            case 1 when _world?.Active==true && _lifecycle!.State.Released:
                _pathKey=File.ReadAllText(Marker("path-key"));_pathCount=SecretCount;
                TravelMark("path-client-ready");_pathStep=PathMode=="secrets"?2:30;break;
            case 2 when Marked("path-go"):
                EnterPathSecret();TravelMark("path-client-entered");_pathAt=Time.unscaledTime+8;_pathStep=3;break;
            case 3 when Marked("path-counted"):
                if(SecretCount!=_pathCount+1 && Time.unscaledTime<_pathAt) return;
                TravelCheck(SecretCount==_pathCount+1,"P2 receives the single shared native secret result");
                TravelMark("path-client-counted");_pathStep=4;break;
            case 4 when Marked("path-rolled-back") && _lifecycle!.State.Released && _lifecycle.PersonalReady:
                TravelCheck(SecretCount==_pathCount,"P2 receives undiscovered secret rollback");
                EnterPathSecret();TravelMark("path-client-reentered");_pathStep=5;break;
            case 5 when Marked("path-restored") && _lifecycle!.State.Released && _lifecycle.PersonalReady:
                TravelCheck(SecretCount==_pathCount+1,"P2 receives restored discovered secret state");
                ResetSession();TravelMark("path-client-left");_pathStep=6;break;
            case 6 when Marked("path-rejoin") && SessionLifecycle.Current==null:
                _address!.text="127.0.0.1";Join();_pathStep=7;break;
            case 7 when _world?.Active==true && _lifecycle!.State.Released && _lifecycle.PersonalReady:
                TravelCheck(SecretCount==_pathCount+1,"current-world late join restores discovered secret without replay");
                TravelMark("path-client-current");_pathStep=8;break;
            case 30 when Marked("path-barrel-exploded"):
                _pathBarrel=_world!.ComponentForTest(_pathKey)!.Cast<ExplosiveBarrel>();
                if(!_pathBarrel._exploded) return;
                TravelCheck(_session!.Snapshot.Connected,"P2 receives the restored future barrel result and stays connected");
                TravelMark("path-client-exploded");_pathStep=8;break;
            case 8 when SessionLifecycle.Current==null:
                TravelMark("client-pass");_pathStep=99;break;
        }
    }
}

// Keep managed protocol records outside IL2CPP MonoBehaviour method discovery.
internal static class WorldPathFrames
{
    internal static WorldFrame Find(IEnumerable<WorldFrame> frames,WorldKind kind,uint flags,string key)
    {
        WorldFrame? found=null;
        foreach(var frame in frames)
            if(frame.Kind==kind && frame.Flags==flags && frame.Key==key)
            // Capture can include repeated durable updates; restoration applies them in order.
            {found=frame;}
        return found??throw new InvalidDataException("Missing native path fixture record.");
    }
}
