using System.Text.Json;
using Hyperstrange.Base;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using UnityEngine;

namespace PBDMP.Plugin;

public sealed partial class MultiplayerBehaviour
{
    private string[] _conveyorKeys=Array.Empty<string>();
    private Vector3[] _conveyorInitial=Array.Empty<Vector3>();
    private int _conveyorPauseFrame;
    private CeilingConveyor Conveyor=>_world!.ComponentForTest(_pathKey)!.Cast<CeilingConveyor>();
    private void WriteConveyorProbe(string file)
        =>File.WriteAllText(Marker(file),JsonSerializer.Serialize(ConveyorNativeProbe.Read(Conveyor)));
    private void CompareConveyorProbe(string file,string stage)
    {
        var expected=JsonSerializer.Deserialize<ConveyorNativeProbe.State>(File.ReadAllText(Marker(file)))!;
        var actual=ConveyorNativeProbe.Read(Conveyor);
        File.WriteAllText(Marker("conveyor-observed-"+TravelRole+"-"+_pathStep+".json"),JsonSerializer.Serialize(actual));
        TravelCheck(actual.Active==expected.Active && actual.Listeners.Length==expected.Listeners.Length,stage+" retains conveyor activation and listener count");
        var solid=0;var rays=0;
        for(var i=0;i<actual.Listeners.Length;i++)
        {
            var a=actual.Listeners[i];var e=expected.Listeners[i];
            TravelCheck(Vector3.Distance(ConveyorNativeProbe.Vector(a.Position),ConveyorNativeProbe.Vector(e.Position))<.15f && Quaternion.Angle(ConveyorNativeProbe.Rotation(a.Rotation),ConveyorNativeProbe.Rotation(e.Rotation))<1,stage+" retains listener "+i+" world pose; actual="+ConveyorNativeProbe.Vector(a.Position)+", expected="+ConveyorNativeProbe.Vector(e.Position));
            TravelCheck(a.Colliders.Length==e.Colliders.Length,stage+" retains listener "+i+" collider count");
            for(var c=0;c<a.Colliders.Length;c++)
            {
                var ac=a.Colliders[c];var ec=e.Colliders[c];
                TravelCheck(ac.Path==ec.Path && ac.Enabled==ec.Enabled && ac.Active==ec.Active && ac.Trigger==ec.Trigger && Vector3.Distance(ConveyorNativeProbe.Vector(ac.Center),ConveyorNativeProbe.Vector(ec.Center))<.15f && Vector3.Distance(ConveyorNativeProbe.Vector(ac.Size),ConveyorNativeProbe.Vector(ec.Size))<.05f,stage+" retains native collider "+ac.Path);
                TravelCheck(ac.Rays.Length==ec.Rays.Length && ac.Rays.Zip(ec.Rays).All(pair=>pair.First.Hit==pair.Second.Hit && (!pair.First.Hit || Vector3.Distance(ConveyorNativeProbe.Vector(pair.First.Point),ConveyorNativeProbe.Vector(pair.Second.Point))<.15f)),stage+" retains native collider ray contacts "+ac.Path);
                if(ac.Enabled && ac.Active && !ac.Trigger) {solid++;rays+=ac.Rays.Count(r=>r.Hit);}
            }
        }
        TravelCheck(solid>0 && rays>0,stage+" checks actual solid conveyor geometry and native ray contacts; colliders="+solid+", contacts="+rays);
    }
    private void RunConveyorTest()
    {
        if(_pathStep==99) return;
        if(_pathStarted==0) _pathStarted=Time.unscaledTime;
        if(Time.unscaledTime-_pathStarted>240) throw new TimeoutException("Conveyor fixture stalled: "+_pathStep);
        File.WriteAllText(Marker(TravelRole+"-conveyor-state.txt"),$"step={_pathStep}, gameTime={Time.time}, frozen={_lifecycle?.Frozen}, save={_saves?.Status}");
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
        if(TravelRole=="host" && _pathStep==6 && _lifecycle is {WorldReady:true} life && life.Epoch>_pathEpoch) life.HostPauseRequest(true);
        if(TravelRole=="host") switch(_pathStep)
        {
            case 1 when _world?.Active==true:
                QuietNpcs();
                var index=int.TryParse(Environment.GetEnvironmentVariable("PBDMP_CONVEYOR_INDEX"),out var requested)?requested:0;
                var targets=_world.CatalogueForTest.MechanismKeys.Select(k=>_world.ComponentForTest(k)?.TryCast<CeilingConveyor>()).Where(c=>c!=null && c.isActiveAndEnabled && c._ceilingConveyorListener.Length>0 && c._moveSpeed>0).OrderBy(c=>WorldCatalogue.DiagnosticPath(c!.transform),StringComparer.Ordinal).ToArray();
                var target=targets[index]!;
                File.WriteAllText(Marker("conveyor-source.txt"),$"index={index}, controllers={targets.Length}, listeners={target._ceilingConveyorListener.Length}, path={WorldCatalogue.DiagnosticPath(target.transform)}");
                _pathKey=_world.KeyForTest(target)!;
                _conveyorKeys=target._ceilingConveyorListener.Select(c=>_world.KeyForTest(c)??throw new InvalidOperationException("Conveyor listener is missing from world catalogue")).ToArray();
                _conveyorInitial=target._ceilingConveyorListener.Select(c=>c.transform.position).ToArray();
                File.WriteAllText(Marker("path-key"),_pathKey);TravelMark("host-ready");_pathStep=2;break;
            case 2 when _lifecycle!.State.Released && Marked("conveyor-client-ready"):
                GameManager.DisableMenu();TimeManager.SetApplicationTimeScale(1);Conveyor.Activate();_pathAt=Time.time+2;_pathStep=3;break;
            case 3 when Time.time>=_pathAt:
                TravelCheck(Conveyor._ceilingConveyorListener.Select((c,i)=>Vector3.Distance(c.transform.position,_conveyorInitial[i])).Any(d=>d>.1f),"native conveyor update moves its authored listeners");
                _lifecycle!.HostPauseRequest(true);_conveyorPauseFrame=Time.frameCount;_pathStep=4;break;
            case 4 when _lifecycle!.Frozen && Time.timeScale==0 && Time.frameCount>_conveyorPauseFrame+1:
                // HostPauseRequest applies scale in the next lifecycle tick;
                // that frame can still consume an already sampled deltaTime.
                WriteConveyorProbe("conveyor-paused.json");CompareConveyorProbe("conveyor-paused.json","P1 paused conveyor");
                TravelMark("conveyor-paused");_pathStep=5;break;
            case 5 when Marked("conveyor-client-paused"):
                _saves!.RequestSave("manual-conveyor","Moving conveyor contacts");_pathStep=50;break;
            case 50 when !_saves!.Busy:
                var saved=_saves.Store.Read("manual-conveyor").Save;
                TravelCheck(_conveyorKeys.All(k=>saved.World.Any(f=>f.Key==k)),"persistent capture contains every authored conveyor listener");
                _pathEpoch=_lifecycle!.Epoch;_saves.RequestLoad("manual-conveyor");_pathStep=6;break;
            case 6 when !_saves!.Busy && _lifecycle!.State.Released && _lifecycle.Epoch>_pathEpoch:
                CompareConveyorProbe("conveyor-paused.json","P1 disk restoration");TravelMark("conveyor-restored");_pathStep=7;break;
            case 7 when Marked("conveyor-client-restored"):
                _conveyorInitial=Conveyor._ceilingConveyorListener.Select(c=>c.transform.position).ToArray();
                _lifecycle!.HostPauseRequest(false);GameManager.DisableMenu();_pathAt=Time.time+2;_pathStep=8;break;
            case 8 when Time.time>=_pathAt:
                TravelCheck(Conveyor._ceilingConveyorListener.Select((c,i)=>Vector3.Distance(c.transform.position,_conveyorInitial[i])).Any(d=>d>.1f),"restored native conveyor continues its future movement");
                _lifecycle!.HostPauseRequest(true);_conveyorPauseFrame=Time.frameCount;_pathStep=9;break;
            case 9 when _lifecycle!.Frozen && Time.timeScale==0 && Time.frameCount>_conveyorPauseFrame+1:
                WriteConveyorProbe("conveyor-current.json");TravelMark("conveyor-current");_pathStep=10;break;
            case 10 when Marked("conveyor-client-left") && !_session!.Snapshot.Connected:
                TravelMark("conveyor-rejoin");_pathStep=11;break;
            case 11 when Marked("conveyor-client-current") && _lifecycle!.State.Released:
                EndFrozenDiagnosticSession();_pathStep=12;break;
            case 12 when Marked("client-pass"):
                TravelMark("host-pass");_pathStep=99;break;
        }
        else switch(_pathStep)
        {
            case 1 when _world?.Active==true && _lifecycle!.State.Released:
                _pathKey=File.ReadAllText(Marker("path-key"));TravelMark("conveyor-client-ready");_pathStep=2;break;
            case 2 when Marked("conveyor-paused") && _lifecycle!.Frozen:
                _pathAt=Time.unscaledTime+.75f;_pathStep=3;break;
            case 3 when Time.unscaledTime>=_pathAt:
                CompareConveyorProbe("conveyor-paused.json","P2 shared pause");TravelMark("conveyor-client-paused");_pathStep=4;break;
            case 4 when Marked("conveyor-restored") && !_saves!.Busy && _lifecycle!.State.Released:
                _pathAt=Time.unscaledTime+.75f;_pathStep=5;break;
            case 5 when Time.unscaledTime>=_pathAt:
                CompareConveyorProbe("conveyor-paused.json","P2 disk restoration");
                Conveyor.OnUpdate(1);CompareConveyorProbe("conveyor-paused.json","P2 passive native update");
                TravelMark("conveyor-client-restored");_pathStep=6;break;
            case 6 when Marked("conveyor-current") && _lifecycle!.Frozen:
                _pathAt=Time.unscaledTime+.75f;_pathStep=7;break;
            case 7 when Time.unscaledTime>=_pathAt:
                CompareConveyorProbe("conveyor-current.json","P2 future native motion");ResetSession();TravelMark("conveyor-client-left");_pathStep=8;break;
            case 8 when Marked("conveyor-rejoin") && SessionLifecycle.Current==null && !SessionManager.SessionActive:
                _address!.text="127.0.0.1";Join();_pathStep=9;break;
            case 9 when _world?.Active==true && _lifecycle!.State.Released && _lifecycle.PersonalReady:
                CompareConveyorProbe("conveyor-current.json","P2 current-world rejoin");TravelMark("conveyor-client-current");_pathStep=10;break;
            case 10 when SessionLifecycle.Current==null:
                TravelMark("client-pass");_pathStep=99;break;
        }
    }
}

internal static class ConveyorNativeProbe
{
    internal sealed record RayContact(bool Hit,float[] Point);
    internal sealed record Contact(string Path,bool Enabled,bool Active,bool Trigger,float[] Center,float[] Size,RayContact[] Rays);
    internal sealed record Listener(float[] Position,float[] Rotation,Contact[] Colliders);
    internal sealed record State(bool Active,Listener[] Listeners);
    internal static Vector3 Vector(float[] v)=>new(v[0],v[1],v[2]);
    internal static Quaternion Rotation(float[] v)=>new(v[0],v[1],v[2],v[3]);
    private static float[] Values(Vector3 v)=>new[]{v.x,v.y,v.z};
    internal static State Read(CeilingConveyor target)
    {
        Physics.SyncTransforms();
        return new(target._activated,target._ceilingConveyorListener.Select(listener=>
        {
            var t=listener.transform;var q=t.rotation;
            var contacts=t.GetComponentsInChildren<Collider>(true).OrderBy(c=>WorldCatalogue.DiagnosticPath(c.transform),StringComparer.Ordinal).Select(c=>
            {
                var b=c.bounds;
                var rays=new[]{Vector3.up,Vector3.down,Vector3.right,Vector3.left,Vector3.forward,Vector3.back}.Select(axis=>
                {
                    var radius=b.extents.magnitude+2;
                    if(c.enabled && c.gameObject.activeInHierarchy && c.Raycast(new Ray(b.center+axis*radius,-axis),out var point,radius*2))
                        return new RayContact(true,Values(point.point));
                    return new RayContact(false,Array.Empty<float>());
                }).ToArray();
                return new Contact(WorldCatalogue.DiagnosticPath(c.transform),c.enabled,c.gameObject.activeInHierarchy,c.isTrigger,Values(b.center),Values(b.size),rays);
            }).ToArray();
            return new Listener(Values(t.position),new[]{q.x,q.y,q.z,q.w},contacts);
        }).ToArray());
    }
}
