using Hyperstrange.Base;
using Hyperstrange.PBD;
using Hyperstrange.PBD.PlayerComponents;
using UnityEngine;

namespace PBDMP.Plugin;

public sealed partial class MultiplayerBehaviour
{
    private Vector3 _gravityHome,_gravityInside;
    private float _gravityBase;
    private GravityChanger GravityFixture=>_world!.ComponentForTest(_pathKey)!.Cast<GravityChanger>();
    private PlayerMovementComponent GravityMovement=>Player.Instance.GetComponent<PlayerMovementComponent>();
    private void CheckGravityContact(string stage,bool inside)
    {
        var selected=GravityMovement._gravityChanger;
        TravelCheck(inside?selected?.Pointer==GravityFixture.Pointer:selected?.Pointer!=GravityFixture.Pointer,stage+" selects the correct personal gravity volume");
        if(inside)
        {
            TravelCheck(GravityFixture.col.bounds.Intersects(Player.Instance.Collider.bounds),stage+" retains actual player/volume overlap");
            TravelCheck(Mathf.Abs(GravityMovement.GravityScale-GravityFixture.GravityScale)<.001f && Mathf.Abs(GravityMovement.MaxFallSpeed-GravityFixture.MaxFallSpeed)<.01f,stage+" applies native gravity and terminal fall speed");
        }
        else TravelCheck(Mathf.Abs(GravityMovement.GravityScale-_gravityBase)<.001f,stage+" returns to the player's original gravity");
    }
    private Vector3 GroundedGravityPoint()
    {
        var b=GravityFixture.col.bounds;var player=Player.Instance;var mask=~(1<<player.gameObject.layer);
        foreach(var x in new[]{0f,-.4f,.4f,-.8f,.8f}) foreach(var z in new[]{0f,-.4f,.4f,-.8f,.8f})
        {
            var origin=new Vector3(b.center.x+x*b.extents.x,b.max.y-.1f,b.center.z+z*b.extents.z);
            if(!Physics.Raycast(origin,Vector3.down,out var floor,b.size.y,mask,QueryTriggerInteraction.Ignore) || Vector3.Dot(floor.normal,Vector3.up)<.7f) continue;
            var feet=floor.point+Vector3.up*.08f;
            if(!b.Contains(feet+Vector3.up*(player.Height*.5f)) || Physics.CheckCapsule(feet+Vector3.up*.4f,feet+Vector3.up*(player.Height-.35f),.3f,mask,QueryTriggerInteraction.Ignore)) continue;
            return feet;
        }
        throw new InvalidOperationException("Native gravity fixture needs grounded, unblocked space inside its authored volume.");
    }
    private void RunGravityTest()
    {
        if(_pathStep==99) return;
        if(_pathStarted==0) _pathStarted=Time.unscaledTime;
        if(Time.unscaledTime-_pathStarted>240) throw new TimeoutException("Native gravity fixture stalled: "+_pathStep);
        File.WriteAllText(Marker(TravelRole+"-gravity-state.txt"),$"step={_pathStep}, gameTime={Time.time}, frozen={_lifecycle?.Frozen}, save={_saves?.Status}");
        if(_pathStep==0)
        {
            _port!.text=Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!;
            if(TravelRole=="host") {Host();SharedWorldPatches.DiagnosticFreeze=true;GameManager.LoadLevel(TestLevel(),true);}
            else {_address!.text="127.0.0.1";Join();}
            _pathStep=1;return;
        }
        if(_lifecycle is {NativeReady:false}) {_lifecycle.AutoContinue();return;}
        if(_lifecycle?.LocalTutorialActive==true)
        {Player.Instance!.GetComponent<PlayerTutorialComponent>().OnUseDown();return;}
        if(TravelRole=="host" && _pathStep==6 && _lifecycle is {WorldReady:true} life && life.Epoch>_pathEpoch) life.HostPauseRequest(true);
        if(TravelRole=="host") switch(_pathStep)
        {
            case 1 when _world?.Active==true:
                QuietNpcs();_gravityHome=Player.Instance.FeetPosition;_gravityBase=GravityMovement.GravityScale;
                var target=_world.CatalogueForTest.MissionKeys.Select(k=>_world.ComponentForTest(k)?.TryCast<GravityChanger>()).First(g=>g!=null && g.isActiveAndEnabled && g.col!=null && g.col.enabled && Mathf.Abs(g.GravityScale-_gravityBase)>.01f && !g.col.bounds.Intersects(Player.Instance.Collider.bounds))!;
                _pathKey=_world.KeyForTest(target)!;
                File.WriteAllText(Marker("gravity-source.txt"),WorldCatalogue.DiagnosticPath(target.transform)+", bounds="+target.col.bounds+", ground="+GroundedGravityPoint()+", scale="+target.GravityScale);File.WriteAllText(Marker("path-key"),_pathKey);TravelMark("host-ready");_pathStep=2;break;
            case 2 when _lifecycle!.State.Released && Marked("gravity-client-ready"):
                GameManager.DisableMenu();TimeManager.SetApplicationTimeScale(1);TravelMark("gravity-go");_pathStep=3;break;
            case 3 when Marked("gravity-client-entered"):
                CheckGravityContact("P1 while P2 is inside",false);_lifecycle!.HostPauseRequest(true);_pathStep=4;break;
            case 4 when _lifecycle!.Frozen && Marked("gravity-client-held"):
                _saves!.RequestSave("manual-gravity","Personal gravity volume contacts");_pathStep=5;break;
            case 5 when !_saves!.Busy:
                TravelMark("gravity-saved");_pathStep=50;break;
            case 50 when Marked("gravity-client-exited"):
                _pathEpoch=_lifecycle!.Epoch;_saves!.RequestLoad("manual-gravity");_pathStep=6;break;
            case 6 when !_saves!.Busy && _lifecycle!.State.Released && _lifecycle.Epoch>_pathEpoch:
                CheckGravityContact("P1 disk restoration",false);TravelMark("gravity-restored");_pathStep=7;break;
            case 7 when Marked("gravity-client-restored"):
                _gravityInside=GroundedGravityPoint();
                Player.Instance.Teleport(_gravityInside,Vector3.zero,Vector3.zero);_world!.RebasePlatforms();CheckGravityContact("P1 own contact entry",true);
                Player.Instance.Teleport(_gravityHome,Vector3.zero,Vector3.zero);_world.RebasePlatforms();CheckGravityContact("P1 own contact exit",false);
                TravelMark("gravity-host-contact-done");_pathStep=8;break;
            case 8 when Marked("gravity-client-left") && !_session!.Snapshot.Connected:
                TravelMark("gravity-rejoin");_pathStep=9;break;
            case 9 when Marked("gravity-client-current") && _lifecycle!.State.Released:
                EndFrozenDiagnosticSession();_pathStep=10;break;
            case 10 when Marked("client-pass"):
                TravelMark("host-pass");_pathStep=99;break;
        }
        else switch(_pathStep)
        {
            case 1 when _world?.Active==true && _lifecycle!.State.Released:
                _pathKey=File.ReadAllText(Marker("path-key"));_gravityHome=Player.Instance.FeetPosition;_gravityBase=GravityMovement.GravityScale;
                TravelMark("gravity-client-ready");_pathStep=2;break;
            case 2 when Marked("gravity-go") && !_lifecycle!.Frozen:
                _gravityInside=GroundedGravityPoint();
                Player.Instance.Teleport(_gravityInside,Vector3.zero,Vector3.zero);_world!.RebasePlatforms();CheckGravityContact("P2 initial volume entry",true);
                TravelMark("gravity-client-entered");_pathStep=3;break;
            case 3 when _lifecycle!.Frozen:
                CheckGravityContact("P2 held before save",true);TravelMark("gravity-client-held");_pathStep=4;break;
            case 4 when Marked("gravity-saved"):
                Player.Instance.Teleport(_gravityHome,Vector3.zero,Vector3.zero);_world!.RebasePlatforms();CheckGravityContact("P2 volume exit before restore",false);
                TravelMark("gravity-client-exited");_pathStep=5;break;
            case 5 when Marked("gravity-restored") && !_saves!.Busy && _lifecycle!.State.Released:
                CheckGravityContact("P2 native disk restoration",true);TravelMark("gravity-client-restored");_pathStep=6;break;
            case 6 when Marked("gravity-host-contact-done"):
                CheckGravityContact("P2 after P1's independent entry and exit",true);ResetSession();TravelMark("gravity-client-left");_pathStep=7;break;
            case 7 when Marked("gravity-rejoin") && SessionLifecycle.Current==null && !SessionManager.SessionActive:
                _address!.text="127.0.0.1";Join();_pathStep=8;break;
            case 8 when _world?.Active==true && _lifecycle!.State.Released && _lifecycle.PersonalReady:
                CheckGravityContact("P2 normal join beside P1",false);
                Player.Instance.Teleport(GroundedGravityPoint(),Vector3.zero,Vector3.zero);_world!.RebasePlatforms();CheckGravityContact("P2 re-entry after travel",true);
                TravelMark("gravity-client-current");_pathStep=9;break;
            case 9 when SessionLifecycle.Current==null:
                TravelMark("client-pass");_pathStep=99;break;
        }
    }
}
