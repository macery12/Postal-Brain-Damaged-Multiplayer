using HarmonyLib;
using Hyperstrange.Base;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using Hyperstrange.PBD.PlayerComponents;
using UnityEngine;

namespace PBDMP.Plugin;

public sealed partial class MultiplayerBehaviour
{
    private Vector3 _pusherCenter,_pusherStart;
    private Pusher PusherFixture=>_world!.ComponentForTest(_pathKey)!.Cast<Pusher>();
    private void RunPusherTest()
    {
        if(_pathStep==99) return;
        if(_pathStarted==0) _pathStarted=Time.unscaledTime;
        if(Time.unscaledTime-_pathStarted>180) throw new TimeoutException("Native pusher fixture stalled: "+_pathStep);
        File.WriteAllText(Marker(TravelRole+"-pusher-state.txt"),$"step={_pathStep}, ready={_lifecycle?.State.Released}, frozen={_lifecycle?.Frozen}, calls={NativePusherProbe.Calls}");
        if(_pathStep==0)
        {
            NativePusherProbe.Install();_port!.text=Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!;
            if(TravelRole=="host") {Host();SharedWorldPatches.DiagnosticFreeze=true;GameManager.LoadLevel(TestLevel(),true);}
            else {_address!.text="127.0.0.1";Join();}
            _pathStep=1;return;
        }
        if(_lifecycle is {NativeReady:false}) {_lifecycle.AutoContinue();return;}
        if(_lifecycle?.LocalTutorialActive==true)
        {Player.Instance!.GetComponent<PlayerTutorialComponent>().OnUseDown();return;}
        if(TravelRole=="host") switch(_pathStep)
        {
            case 1 when _world?.Active==true:
                QuietNpcs();_pathHostFeet=Player.Instance.FeetPosition;
                var target=_world.CatalogueForTest.MechanismKeys.Select(k=>_world.ComponentForTest(k)?.TryCast<Pusher>()).First(p=>p!=null && p.isActiveAndEnabled && p._pushCollider!=null && p._pushForce>1 && Vector3.Distance(p._pushCollider.bounds.center,_pathHostFeet)>10)!;
                _pathKey=_world.KeyForTest(target)!;File.WriteAllText(Marker("path-key"),_pathKey);
                File.WriteAllText(Marker("pusher-source.txt"),WorldCatalogue.DiagnosticPath(target.transform)+", force="+target._pushForce+", collider="+target._pushCollider.GetIl2CppType().Name);
                TravelMark("host-ready");_pathStep=2;break;
            case 2 when _lifecycle!.State.Released && Marked("pusher-client-ready"):
                GameManager.DisableMenu();TimeManager.SetApplicationTimeScale(1);TravelMark("pusher-go");_pathAt=Time.unscaledTime+8;_pathStep=3;break;
            case 3 when Marked("pusher-client-positioned"):
                Physics.SyncTransforms();
                if((_world!.Remote==null || !PusherFixture._pushCollider.bounds.Intersects(_world.Remote.GetComponent<Collider>().bounds)) && Time.unscaledTime<_pathAt) return;
                TravelCheck(_world!.Remote!=null && PusherFixture._pushCollider.bounds.Intersects(_world.Remote.GetComponent<Collider>().bounds),"real remote-player capsule occupies the authored native pusher volume");
                PusherFixture.Push();TravelMark("pusher-fired");_pathStep=4;break;
            case 4 when Marked("pusher-client-hit"):
                TravelCheck(NativePusherProbe.Calls==0 && Vector3.Distance(Player.Instance.FeetPosition,_pathHostFeet)<.3f,"native pusher hitting P2 leaves P1's personal movement untouched");
                TravelMark("pusher-host-checked");_pathStep=5;break;
            case 5 when Marked("pusher-client-done"):
                NativePusherProbe.Cleanup();EndFrozenDiagnosticSession();_pathStep=6;break;
            case 6 when Marked("client-pass"):
                TravelMark("host-pass");_pathStep=99;break;
        }
        else switch(_pathStep)
        {
            case 1 when _world?.Active==true && _lifecycle!.State.Released:
                _pathKey=File.ReadAllText(Marker("path-key"));TravelMark("pusher-client-ready");_pathStep=2;break;
            case 2 when Marked("pusher-go") && !_lifecycle!.Frozen:
                _pusherCenter=PusherFixture._pushCollider.bounds.center-Vector3.up*(Player.Instance.Height*.5f);
                Player.Instance.Teleport(_pusherCenter,Vector3.zero,Vector3.zero);
                PusherFixture.Push();TravelCheck(NativePusherProbe.Calls==0,"P2's native pusher replica cannot independently push its player");
                TravelMark("pusher-client-positioned");_pathAt=Time.unscaledTime+10;_pathStep=3;break;
            case 3:
                if(NativePusherProbe.Calls==0 && Time.unscaledTime<_pathAt)
                {Player.Instance.Teleport(_pusherCenter,Vector3.zero,Vector3.zero);return;}
                TravelCheck(NativePusherProbe.Calls==1 && NativePusherProbe.FromHost && NativePusherProbe.Force.sqrMagnitude>1 && NativePusherProbe.Pending.sqrMagnitude>1,"native host pusher delivers one nonzero force to P2's actual movement component; calls="+NativePusherProbe.Calls+", force="+NativePusherProbe.Force+", pending="+NativePusherProbe.Pending);
                _pusherStart=Player.Instance.FeetPosition;_pathAt=Time.time+.15f;_pathStep=4;break;
            case 4 when Time.time>=_pathAt && !_lifecycle!.Frozen:
                TravelCheck(Player.Instance.IsAlive && (Vector3.Distance(Player.Instance.FeetPosition,_pusherStart)>.05f || Player.Instance.Velocity.sqrMagnitude>1),"P2's native movement continues after the received push");
                TravelCheck(NativePusherProbe.Calls==1,"P2 receives no duplicate local or remote push");TravelMark("pusher-client-hit");_pathStep=5;break;
            case 5 when Marked("pusher-host-checked"):
                TravelMark("pusher-client-done");_pathStep=6;break;
            case 6 when SessionLifecycle.Current==null:
                NativePusherProbe.Cleanup();TravelMark("client-pass");_pathStep=99;break;
        }
    }
}

internal static class NativePusherProbe
{
    private static Harmony? _hooks;
    internal static int Calls;
    internal static bool FromHost;
    internal static Vector3 Force,Pending;
    internal static void Install()
    {
        Calls=0;FromHost=false;Force=Pending=Vector3.zero;
        _hooks=new("pbdmp.acceptance.native-pusher");
        _hooks.Patch(AccessTools.Method(typeof(Player),"Push"),postfix:new(typeof(NativePusherProbe),nameof(Pushed)));
    }
    private static void Pushed(Player __instance,Vector3 __0,bool __runOriginal)
    {
        if(!__runOriginal) return;
        Calls++;FromHost=SharedWorld.Current?.Applying==true;Force=__0;
        Pending=__instance.GetComponent<PlayerMovementComponent>().VelocityToApply;
    }
    internal static void Cleanup() {_hooks?.UnpatchSelf();_hooks=null;}
}
