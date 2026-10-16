using System.Reflection;
using HarmonyLib;
using Hyperstrange.Base;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using UnityEngine;

namespace PBDMP.Plugin;

public sealed partial class MultiplayerBehaviour
{
    private string _actionButton="";
    private Cannon? _actionCannon;
    private Vector3 _actionGuestPoint;
    private float _actionHostHealth;
    private bool _actionSawProjectile;
    private void RunWorldActionTest()
    {
        if(_pathStep==99) return;
        if(_pathStarted==0) _pathStarted=Time.unscaledTime;
        if(Time.unscaledTime-_pathStarted>180) throw new TimeoutException("Native action fixture stalled: "+PathMode+"/"+_pathStep);
        File.WriteAllText(Marker(TravelRole+"-action-state.txt"),$"mode={PathMode}, step={_pathStep}, ready={_lifecycle?.State.Released}, frozen={_lifecycle?.Frozen}");
        if(_pathStep==0)
        {
            _port!.text=Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!;
            if(PathMode=="teleport") WorldTeleportDiagnostic.Install();
            if(TravelRole=="host") {Host();SharedWorldPatches.DiagnosticFreeze=true;GameManager.LoadLevel(TestLevel(),true);}
            else {_address!.text="127.0.0.1";Join();}
            _pathStep=1;return;
        }
        if(_lifecycle is {NativeReady:false}) {_lifecycle.AutoContinue();return;}
        if(_lifecycle?.LocalTutorialActive==true)
        {Player.Instance!.GetComponent<Hyperstrange.PBD.PlayerComponents.PlayerTutorialComponent>().OnUseDown();return;}
        if(TravelRole=="client" && PathMode=="cannon" && _world?.ProjectilePresentationsForTest>0) _actionSawProjectile=true;
        if(TravelRole=="host") switch(_pathStep)
        {
            case 1 when _world?.Active==true:
                QuietNpcs();_pathHostFeet=Player.Instance.FeetPosition;_actionHostHealth=Player.Instance.HitPoints.Current;
                if(PathMode=="teleport")
                {
                    var teleporter=_world.CatalogueForTest.MissionKeys.Select(k=>_world.ComponentForTest(k)?.TryCast<Teleporter>()).First(t=>t?._destination!=null)!;
                    _pathKey=_world.KeyForTest(teleporter)!;
                    var listener=UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true).First(c=>c.gameObject.scene.IsValid() && c.GetComponent<Interactable>()!=null && NativeAssetGraph.Actual(c).GetType().GetProperties(BindingFlags.Instance|BindingFlags.Public).Where(p=>p.GetIndexParameters().Length==0 && typeof(UnityEngine.Events.UnityEventBase).IsAssignableFrom(p.PropertyType)).Any(p=>p.GetValue(NativeAssetGraph.Actual(c)) is UnityEngine.Events.UnityEventBase ev && Enumerable.Range(0,ev.GetPersistentEventCount()).Any(i=>ev.GetPersistentTarget(i)?.Pointer==teleporter.Pointer && ev.GetPersistentMethodName(i)=="Teleport")));
                    var button=listener.GetComponent<Interactable>();
                    TravelCheck(_world.IsDoor(button),"native teleporter callback's sibling button belongs to shared interactions");
                    _actionButton=_world.KeyForTest(button)!;
                    File.WriteAllText(Marker("action-button"),_actionButton);
                }
                else
                {
                    _actionCannon=_world.CatalogueForTest.MechanismKeys.Select(k=>_world.ComponentForTest(k)?.TryCast<Cannon>()).First(c=>c!=null)!;
                    _pathKey=_world.KeyForTest(_actionCannon)!;
                    var hostDirection=Player.Instance.FeetPosition-_actionCannon.transform.position;hostDirection.y=0;
                    _actionCannon.OnLateUpdate(0);
                    TravelCheck(Vector3.Angle(_actionCannon._directionToPlayer,hostDirection)<1 && Vector3.Distance(Player.Instance.FeetPosition,_pathHostFeet)<.3f,"native cannon retains correct P1 aiming when no guest is present");
                    var origin=_actionCannon._projectileAnchor.position;
                    var found=false;
                    foreach(var distance in new[]{16f,24f,32f,48f})
                    {
                        var above=origin+_actionCannon.transform.forward*distance+Vector3.up*30;
                        if(Physics.Raycast(above,Vector3.down,out var floor,500,LayerManager.Masks.ENVIRONMENT_ONLY,QueryTriggerInteraction.Ignore) &&
                            PlayerSessionState.FindNearby(Player.Instance,floor.point+Vector3.up*.05f,out _actionGuestPoint)) {found=true;break;}
                    }
                    TravelCheck(found,"native cannon has a grounded, unblocked guest target position");
                    File.WriteAllText(Marker("action-guest-point"),System.Text.Json.JsonSerializer.Serialize(new[]{_actionGuestPoint.x,_actionGuestPoint.y,_actionGuestPoint.z}));
                    _actionCannon.DisableShootingAtPlayer();
                }
                File.WriteAllText(Marker("path-key"),_pathKey);TravelMark("host-ready");_pathStep=2;break;
            case 2 when _lifecycle!.State.Released && Marked("action-client-ready"):
                GameManager.DisableMenu();TimeManager.SetApplicationTimeScale(1);TravelMark("action-go");_pathStep=3;break;
            case 3 when Marked("action-client-positioned"):
                _pathAt=Time.unscaledTime+1.5f;_pathStep=4;break;
            case 4 when Time.unscaledTime>=_pathAt:
                if(PathMode=="teleport")
                {
                    if(!Marked("action-client-teleported")) return;
                    TravelCheck(WorldTeleportDiagnostic.NativeCalls==0 && Vector3.Distance(Player.Instance.FeetPosition,_pathHostFeet)<.3f,"P2's native button teleports P2 without teleporting P1");
                    TravelMark("action-host-checked");_pathStep=7;break;
                }
                var remote=_world!.Remote!;
                _world.CannonTarget(_actionCannon!,out var selected);
                // Native aiming computes direction before sliding the cannon
                // along its authored track, even when the explicit delta is zero.
                var desired=remote.FeetPosition-_actionCannon!.transform.position;desired.y=0;
                _actionCannon.OnLateUpdate(0);
                TravelCheck(Vector3.Angle(_actionCannon._directionToPlayer,desired)<1,"native cannon aims at the nearer P2 through its scoped Player getters; guest distance="+Vector3.Distance(remote.FeetPosition,_actionCannon.transform.position)+", host distance="+Vector3.Distance(Player.Instance.FeetPosition,_actionCannon.transform.position)+", selected="+(selected!=null)+", actual="+_actionCannon._directionToPlayer+", wanted="+desired.normalized+", cached player="+(_actionCannon._player?.Pointer==Player.Instance.Pointer));
                TravelCheck(Vector3.Distance(Player.Instance.FeetPosition,_pathHostFeet)<.3f,"native cannon targeting P2 never moves the real P1");
                _actionCannon.EnableShootingAtPlayer();_actionCannon._shootCooldown.Reset();_actionCannon.OnSlowUpdate(.25f);
                TravelCheck(_actionCannon._projectileLobs.Count>0,"native cannon produces a real hostile projectile for its selected player");
                TravelMark("action-cannon-fired");_pathAt=Time.unscaledTime+12;_pathStep=5;break;
            case 5 when Marked("action-client-hit"):
                TravelCheck(Player.Instance.HitPoints.Current==_actionHostHealth,"cannon hitting P2 does not charge P1's personal health");
                _actionCannon!.DisableShootingAtPlayer();TravelMark("action-host-checked");_pathStep=7;break;
            case 5 when Time.unscaledTime>=_pathAt:
                TravelCheck(false,"native cannon projectile must hit the actual P2 victim, not merely appear");break;
            case 7 when Marked("action-client-done"):
                WorldTeleportDiagnostic.Cleanup();EndFrozenDiagnosticSession();_pathStep=8;break;
            case 8 when Marked("client-pass"):
                TravelMark("host-pass");_pathStep=99;break;
        }
        else switch(_pathStep)
        {
            case 1 when _world?.Active==true && _lifecycle!.State.Released:
                _pathKey=File.ReadAllText(Marker("path-key"));TravelMark("action-client-ready");_pathStep=2;break;
            case 2 when Marked("action-go"):
                if(PathMode=="teleport")
                {
                    var button=_world!.ComponentForTest(File.ReadAllText(Marker("action-button")))!.Cast<Interactable>();
                    TravelCheck(PlayerSessionState.FindNearby(Player.Instance,button.transform.position,out var feet),"native teleporter button has safe requester space");
                    Player.Instance.Teleport(feet,new(0,37,0),new(3,0,2));_pathAt=Time.unscaledTime+.6f;_pathStep=3;break;
                }
                var point=System.Text.Json.JsonSerializer.Deserialize<float[]>(File.ReadAllText(Marker("action-guest-point")))!;
                Player.Instance.Teleport(new(point[0],point[1],point[2]),Vector3.zero,Vector3.zero);_actionHostHealth=Player.Instance.HitPoints.Current;
                var replicaCannon=_world!.ComponentForTest(_pathKey)!.Cast<Cannon>();var localShots=replicaCannon._projectileLobs.Count;
                replicaCannon.OnSlowUpdate(.25f);
                TravelCheck(replicaCannon._projectileLobs.Count==localShots,"P2's native cannon does not independently simulate another hostile shot");
                TravelMark("action-client-positioned");_pathStep=5;break;
            case 3 when Time.unscaledTime>=_pathAt && !_lifecycle!.Frozen:
                // Supply nonzero incoming momentum just before the request;
                // the delayed native teleport must transform the current value.
                Player.Instance.Push(new(3,4,2),false);_pathAt=Time.unscaledTime+2;_pathStep=31;break;
            case 31:
                if(Player.Instance.Velocity.sqrMagnitude<1 && Time.unscaledTime<_pathAt) {Player.Instance.Push(new(3,4,2),false);return;}
                TravelCheck(Player.Instance.Velocity.sqrMagnitude>1,"native movement has nonzero momentum before the teleporter request");
                _world!.ComponentForTest(File.ReadAllText(Marker("action-button")))!.Cast<Interactable>().HandleInteract(Player.Instance);
                TravelMark("action-client-positioned");_pathAt=Time.unscaledTime+8;_pathStep=4;break;
            case 4:
                if(WorldTeleportDiagnostic.NativeCalls==0 && Time.unscaledTime<_pathAt) {Player.Instance.Push(new(3,4,2),false);return;}
                TravelCheck(WorldTeleportDiagnostic.NativeCalls==1 && WorldTeleportDiagnostic.PoseMatches && WorldTeleportDiagnostic.VelocityMatches && WorldTeleportDiagnostic.HadMomentum,"requester executes one native teleporter with destination position/view and transformed personal momentum; calls="+WorldTeleportDiagnostic.NativeCalls+", pose="+WorldTeleportDiagnostic.PoseMatches+", velocity="+WorldTeleportDiagnostic.VelocityMatches+", momentum="+WorldTeleportDiagnostic.HadMomentum);
                TravelMark("action-client-teleported");_pathStep=6;break;
            case 5 when Player.Instance!=null && Player.Instance.HitPoints.Current<_actionHostHealth:
                TravelCheck(_actionSawProjectile,"P2 sees the real cannon projectile before its hit");
                TravelCheck(true,"native cannon projectile damages P2's personal health through host combat authority");TravelMark("action-client-hit");_pathStep=6;break;
            case 6 when Marked("action-host-checked"):
                TravelMark("action-client-done");_pathStep=8;break;
            case 8 when SessionLifecycle.Current==null:
                WorldTeleportDiagnostic.Cleanup();TravelMark("client-pass");_pathStep=99;break;
        }
    }
}

internal static class WorldTeleportDiagnostic
{
    private sealed record Probe(Player Player,Vector3 Position,Quaternion View,Vector3 Velocity);
    private static Harmony? _harmony;
    internal static int NativeCalls;
    internal static bool PoseMatches,VelocityMatches,HadMomentum;
    internal static void Install()
    {
        _harmony=new Harmony("PBDMP.diagnostic.teleport");
        _harmony.Patch(AccessTools.Method(typeof(Teleporter),"Teleport"),prefix:new(typeof(WorldTeleportDiagnostic),nameof(Before)),postfix:new(typeof(WorldTeleportDiagnostic),nameof(After)));
    }
    private static void Before(Teleporter __instance,Player __0,out Probe __state)
    {__state=new(__0,__instance._destination.position,__instance._destination.rotation,__instance._destination.TransformDirection(__0.ReferenceTransform.InverseTransformDirection(__0.Velocity)));}
    private static void After(bool __runOriginal,Probe __state)
    {
        if(!__runOriginal) return;
        NativeCalls++;HadMomentum=__state.Velocity.sqrMagnitude>1;
        PoseMatches=Vector3.Distance(__state.Player.FeetPosition,__state.Position)<.05f && Quaternion.Angle(__state.Player.ReferenceTransform.rotation,__state.View)<1;
        // CharacterController.velocity is the previous Move sample after a warp; the native pending velocity is the continuation contract.
        VelocityMatches=Vector3.Distance(__state.Player.GetComponent<Hyperstrange.PBD.PlayerComponents.PlayerMovementComponent>().VelocityToApply,__state.Velocity)<.05f;
    }
    internal static void Cleanup() {_harmony?.UnpatchSelf();_harmony=null;}
}
