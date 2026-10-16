using System.Text.Json;
using Hyperstrange.Base;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace PBDMP.Plugin;

public sealed partial class MultiplayerBehaviour
{
    private sealed record UrineFixture(string Key,string[] Data,float[]? Pose=null,float[]? Enabled=null);
    private int _urineStep,_urineCount;
    private float _urineAt;
    private Component? _urineTarget;
    private Toilet? _urineToilet;
    private ProjectileFireBall? _urineFireBall;
    private int _urineEffectsBefore;
    private string[] _urineBefore=Array.Empty<string>();
    private void RunUrineTest()
    {
        if(_urineStep==99) return;
        File.WriteAllText(Marker(TravelRole+"-urine-state.txt"),$"step={_urineStep}, ready={_lifecycle?.NativeReady}, world={_lifecycle?.WorldReady}, released={_lifecycle?.State.Released}, frozen={_lifecycle?.Frozen}, requests={_world?.UrineRequestsApplied}");
        if(_urineStep==0) {_port!.text=Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!;if(TravelRole=="host") {Host();GameManager.LoadLevel(TestLevel(),true);} else {_address!.text="127.0.0.1";Join();}_urineStep=1;return;}
        if(_lifecycle is {NativeReady:false}) {_lifecycle.AutoContinue();return;}
        if(TravelRole=="host") UrineHostTest();else UrineClientTest();
    }
    private void UrineHostTest()
    {
        switch(_urineStep)
        {
            case 1 when _world?.Active==true:
                QuietNpcs();SharedWorldPatches.DiagnosticFreeze=true;TimeManager.SetApplicationTimeScale(1);
                var targets=_world.CatalogueForTest.UrineKeys.Select(k=>_world.ComponentForTest(k)!).ToArray();
                _urineTarget=Environment.GetEnvironmentVariable("PBDMP_TANK_REPLAY")=="1"?targets.First(c=>c.TryCast<PissTank>()!=null):targets.FirstOrDefault(c=>c.TryCast<FuseBox>()!=null)??targets.First(c=>c.TryCast<PissTank>()!=null);
                if(_urineTarget.TryCast<PissTank>() is {} tank) {tank.Enable();tank._shrinkSpeed=0;tank._fillSpeed=100;if(Environment.GetEnvironmentVariable("PBDMP_TANK_REPLAY")=="1") tank.FillPercentage=-0.005902154f;}
                _urineBefore=WorldCatalogue.Save(_urineTarget);File.WriteAllText(Marker("urine-target"),_world.KeyForTest(_urineTarget)!);
                TravelMark("host-ready");_urineStep=2;break;
            case 2 when _lifecycle!.WorldReady && _lifecycle.State.Released && Marked("client-target-ready"):
                if(MissionTest) {GameManager.DisableMenu();TimeManager.SetApplicationTimeScale(1);}
                BeginMissionWallFixture();_urineCount=_world!.UrineRequestsApplied;TravelMark("host-urine-go");_urineStep=3;break;
            case 3 when Marked("client-urinated") && _world!.UrineRequestsApplied>_urineCount:
                TravelCheck(!WorldCatalogue.Save(_urineTarget!).SequenceEqual(_urineBefore),"P2 urine changes the canonical P1 target state");
                // Hold continuously turning cosmetic cogs while comparing an
                // exact two-process fixture. Gameplay fill callbacks still run.
                SharedUrinePatches.DiagnosticHoldTankMotion=true;
                _urineAt=Time.unscaledTime+2;_urineStep=4;break;
            case 4 when Time.unscaledTime>=_urineAt:
                if(_urineTarget!.TryCast<FuseBox>() is {} fuse)
                {
                    TravelCheck(fuse._exploded,"native fuse box commits its exploded state on P1");
                    var events=fuse.GetComponent<ExplosionListener>()._onExplosionEvents;
                    for(var i=0;i<events.GetPersistentEventCount();i++) {var c=events.GetPersistentTarget(i)?.TryCast<Component>();if(c==null) continue;TravelCheck(_world!.KeyForTest(c)!=null,"fuse progression target is catalogued: "+c.name+"."+events.GetPersistentMethodName(i));if(c.TryCast<Trigger>() is {} trigger) TravelCheck(!trigger._enabled,"P2 activation disables the P1 urine tutorial trigger");}
                }
                // Compare one stable physical-world instant, not a prop pose
                // sampled before it continued settling during the peer's load.
                GameManager.PauseGame();
                WriteUrineFixture("urine-live.json");TravelMark("host-urine-shared");_urineStep=5;break;
            case 5 when Marked("client-live-checked"):
                GameManager.DisableMenu();
                if(MissionTest) {_urineStep=14;break;}
                _urineToilet=_world!.CatalogueForTest.UrineKeys.Select(k=>_world.ComponentForTest(k)!.TryCast<Toilet>()).First(t=>t!=null && t._toiletHandle!=null && t._flushParticle!=null);
                _urineToilet!._isOpen=true;if(_urineToilet._toiletFlap!=null) _urineToilet._toiletFlap._isFirstState=false;_urineToilet._currentWaterLevel=0.99f;_urineToilet.UpdateWaterLevel(true);
                File.WriteAllText(Marker("urine-toilet"),_world.KeyForTest(_urineToilet)!);_urineStep=10;break;
            case 10 when Marked("client-toilet-filled") && _urineToilet!._currentWaterLevel>=1:
                TravelCheck(true,"P2 urine fills the host toilet and crosses its overflow progression threshold");TravelMark("host-toilet-overflow");_urineStep=11;break;
            case 11 when Marked("client-toilet-flushed") && _urineToilet!._currentWaterLevel<0.99f:
                TravelCheck(true,"P2 handle interaction starts the canonical native P1 flush");_urineAt=Time.unscaledTime+_urineToilet._flushingTime+0.5f;_urineStep=12;break;
            case 12 when Time.unscaledTime>=_urineAt:
                TravelCheck(!_urineToilet!._isFlushing && _urineToilet._currentWaterLevel<0.01f,"native toilet flush completes without leaving a permanent interaction lock");
                var prefab=Resources.FindObjectsOfTypeAll<ProjectileFireBall>().First(p=>!p.gameObject.scene.IsValid());
                _urineFireBall=PoolManager.SpawnPoolable(prefab.Cast<Projectile>(),Player.Instance.CenterOfMass+Vector3.up*4,Quaternion.identity).Cast<ProjectileFireBall>();
                _urineFireBall.Init(Vector3.forward,0,0,0,1,0,0,Player.Instance.Cast<IAiActor>());_urineFireBall.SetLocalTimeScale(0);
                File.WriteAllText(Marker("urine-ball-position"),JsonSerializer.Serialize(new[]{_urineFireBall.Position.x,_urineFireBall.Position.y,_urineFireBall.Position.z}));TravelMark("host-fireball-ready");_urineStep=13;break;
            case 13 when Marked("client-fireball-urinated") && !_urineFireBall!.gameObject.activeInHierarchy:
                TravelCheck(true,"P2 urine extinguishes the canonical P1 fireball through its harmless raycast receiver");TravelMark("host-fireball-out");_urineStep=14;break;
            case 14 when MissionTest || Marked("client-fireball-out"):
                if(_urineTarget!.TryCast<FuseBox>() is {} box) {WorldCatalogue.Load(box,new[]{"_exploded","False"});box.Piss(new PissData(Player.Instance.Cast<IAiActor>()));TravelCheck(box._exploded,"P1 urine uses the same canonical activation path");}
                else if(_urineTarget.TryCast<PissTank>() is {} filled) {for(var i=0;i<10;i++) filled.OnPiss(new PissData(Player.Instance.Cast<IAiActor>()));TravelCheck(filled._filled,"P1 urine completes the shared native tank");}
                _urineAt=Time.unscaledTime+2;_urineStep=6;break;
            case 6 when Time.unscaledTime>=_urineAt:
                GameManager.PauseGame();
                WriteUrineFixture("urine-late.json");TravelMark("host-urine-late");_urineStep=7;break;
            case 7 when Marked("client-late-ready") && !Marked("host-urine-current"):
                WriteUrineFixture("urine-current.json");TravelMark("host-urine-current");break;
            case 7 when Marked("client-late-checked"):
                ResetSession();SharedWorldPatches.DiagnosticFreeze=false;SharedUrinePatches.DiagnosticHoldTankMotion=false;_urineStep=8;break;
            case 8 when Marked("client-pass"):
                TravelMark("host-pass");_urineStep=99;break;
        }
    }
    private void UrineClientTest()
    {
        switch(_urineStep)
        {
            case 1 when _world?.Active==true && _lifecycle!.WorldReady && _lifecycle.State.Released:
                _urineTarget=_world.ComponentForTest(File.ReadAllText(Marker("urine-target")))!;Player.Instance.Teleport(_urineTarget.transform.position+Vector3.back*2,Vector3.zero,Vector3.zero);
                _urineAt=Time.unscaledTime+0.5f;_urineStep=2;break;
            case 2 when Time.unscaledTime>=_urineAt:
                TravelMark("client-target-ready");_urineStep=3;break;
            case 3 when Marked("host-urine-go") && !_lifecycle!.Frozen:
                if(_urineTarget!.TryCast<PissTank>() is {} tank) {for(var i=0;i<3;i++) tank.OnPiss(new PissData(Player.Instance.Cast<IAiActor>()));}
                else _urineTarget.Cast<IPissable>().Piss(new PissData(Player.Instance.Cast<IAiActor>()));
                TravelMark("client-urinated");_urineStep=4;break;
            case 4 when Marked("host-urine-shared"):
                CheckUrineFixture("urine-live.json");TravelMark("client-live-checked");_urineStep=5;break;
            case 5 when Marked("host-urine-late"):
                ResetSession();_urineStep=6;break;
            case 5 when File.Exists(Marker("urine-toilet")):
                _urineToilet=_world!.ComponentForTest(File.ReadAllText(Marker("urine-toilet")))!.Cast<Toilet>();
                if(!_urineToilet._isOpen || _urineToilet._currentWaterLevel<0.98f) return;
                Player.Instance.Teleport(_urineToilet.transform.position+Vector3.back*2,Vector3.zero,Vector3.zero);_urineAt=Time.unscaledTime+0.5f;_urineStep=10;break;
            case 10 when Time.unscaledTime>=_urineAt:
                for(var i=0;i<3;i++) _urineToilet!.Piss(new PissData(Player.Instance.Cast<IAiActor>()));TravelMark("client-toilet-filled");_urineStep=11;break;
            case 11 when Marked("host-toilet-overflow") && _urineToilet!._currentWaterLevel>=1:
                TravelCheck(true,"P2 receives shared toilet water/overflow state");_urineEffectsBefore=_world!.UrineEffectsPlayed;
                _urineToilet._toiletHandle.HandleInteract(Player.Instance);TravelMark("client-toilet-flushed");_urineStep=12;break;
            case 12 when Marked("host-fireball-ready"):
                TravelCheck(_world!.UrineEffectsPlayed>_urineEffectsBefore,"P2 sees/hears the native live flush presentation");
                var p=JsonSerializer.Deserialize<float[]>(File.ReadAllText(Marker("urine-ball-position")))!;Player.Instance.Teleport(new(p[0]-2,p[1]-1,p[2]),Vector3.zero,Vector3.zero);
                _urineAt=Time.unscaledTime+0.5f;_urineStep=13;break;
            case 13 when Time.unscaledTime>=_urineAt:
                var proxy=UnityEngine.Object.FindObjectsOfType<UrineProjectileProxy>().FirstOrDefault();if(proxy==null) return;
                TravelCheck(proxy.GetComponent<SphereCollider>().enabled,"replica fireball has a native urine-raycast surface");proxy.Cast<IPissable>().Piss(new PissData(Player.Instance.Cast<IAiActor>()));TravelMark("client-fireball-urinated");_urineStep=14;break;
            case 14 when Marked("host-fireball-out") && UnityEngine.Object.FindObjectsOfType<UrineProjectileProxy>().Length==0:
                TravelCheck(true,"P2 removes the extinguished fireball presentation");TravelMark("client-fireball-out");_urineStep=15;break;
            case 15 when Marked("host-urine-late"):
                ResetSession();_urineStep=6;break;
            case 6 when SessionLifecycle.Current==null && !SessionManager.SessionActive:
                _address!.text="127.0.0.1";Join();_urineStep=7;break;
            case 7 when _world?.Active==true && _lifecycle!.WorldReady && _lifecycle.State.Released:
                TravelMark("client-late-ready");if(!Marked("host-urine-current")) return;
                CheckUrineFixture("urine-current.json");TravelCheck(_world.UrineEffectsPlayed==0,"late join restores urine state without replaying historical effects/sounds");TravelMark("client-late-checked");_urineStep=8;break;
            case 8 when SessionLifecycle.Current==null && !SessionManager.SessionActive:
                TravelMark("client-pass");_urineStep=99;break;
        }
    }
    private void WriteUrineFixture(string name)
    {
        WriteMissionWallFixture(name);
        var catalogue=_world!.CatalogueForTest;var fixtures=new List<UrineFixture>();
        var tankReplay=Environment.GetEnvironmentVariable("PBDMP_TANK_REPLAY")=="1";
        HashSet<IntPtr>? tankTransforms=null;
        if(tankReplay)
        {
            // The endpoint replay is explicitly a single-tank regression. Its
            // fixture includes that tank and every directly fill-driven linked
            // mover/scaler, rather than unrelated moving scenery across the map.
            tankTransforms=new();
            void Include(Transform? root) {if(root!=null) foreach(var t in root.GetComponentsInChildren<Transform>(true)) tankTransforms.Add(t.Pointer);}
            var tank=_urineTarget!.Cast<PissTank>();Include(tank.transform);
            foreach(var t in new[]{tank._view.fillContainer,tank._view.totem,tank._view.piston01,tank._view.piston02,tank._view.leftCog,tank._view.rightCog}) Include(t);
            foreach(var listener in UnityEngine.Object.FindObjectsOfType<PissFillListener>(true))
                if(listener._pissFillablesGO.Any(go=>go!=null && go.Pointer==tank.gameObject.Pointer))
                {Include(listener.TryCast<PissFillListenerMovingObject>()?._moveTransform);Include(listener.TryCast<PissFillListenerScalingObject>()?._scaleTransform);}
        }
        foreach(var key in catalogue.UrineKeys.Where(k=>!tankReplay || k==catalogue.Key(_urineTarget!))) {var data=SharedWorld.UrineSnapshot(catalogue.Components[key]);if(data.Length>0) fixtures.Add(new(key,data));}
        foreach(var pair in catalogue.UrineSurfaces)
        {
            var t=catalogue.EncounterMotion[pair.Key];if(tankTransforms!=null && !tankTransforms.Contains(t.Pointer)) continue;var p=t.localPosition;var r=t.localEulerAngles;var s=t.localScale;
            fixtures.Add(new(pair.Key,new[]{t.gameObject.activeSelf.ToString()},new[]{p.x,p.y,p.z,r.x,r.y,r.z,s.x,s.y,s.z},pair.Value.Select(c=>UrineFixtureEnabled(c)?1f:0f).ToArray()));
        }
        File.WriteAllText(Marker(name),JsonSerializer.Serialize(fixtures));
    }
    private static bool UrineFixtureEnabled(Component c)=>c.TryCast<Renderer>()?.enabled??c.TryCast<Collider>()?.enabled??c.TryCast<Light>()?.enabled??c.TryCast<UnityEngine.AI.NavMeshObstacle>()?.enabled??c.TryCast<OcclusionPortal>()?.open??false;
    private void CheckUrineFixture(string name,bool progressionOnly=false)
    {
        CheckMissionWallFixture(name);
        foreach(var fixture in JsonSerializer.Deserialize<UrineFixture[]>(File.ReadAllText(Marker(name)))!)
        {
            if(fixture.Pose is {} pose)
            {
                var t=_world!.CatalogueForTest.EncounterMotion[fixture.Key];var enabled=_world.CatalogueForTest.UrineSurfaces[fixture.Key].Select(c=>UrineFixtureEnabled(c)?1f:0f).ToArray();
                var matches=enabled.SequenceEqual(fixture.Enabled!) && t.gameObject.activeSelf.ToString()==fixture.Data[0];
                var components=_world.CatalogueForTest.UrineSurfaces[fixture.Key];
                if(!matches && progressionOnly && components.Length>0 && components.All(c=>c.TryCast<Light>()!=null) && t.GetComponentsInChildren<Collider>(true).Length==0)
                {
                    // This optional pre-play check is explicitly narrower than
                    // the default strict regression; never waive collision or
                    // structural geometry. Retain each cosmetic mismatch.
                    File.AppendAllText(Marker("regression-light-differences.tsv"),WorldCatalogue.DiagnosticPath(t)+"\tactive="+t.gameObject.activeSelf+"/"+fixture.Data[0]+"\tenabled="+string.Join(',',enabled)+"/"+string.Join(',',fixture.Enabled!)+"\n");
                    Plugin.Logger.LogWarning("PREPLAY: light-only difference deferred: "+WorldCatalogue.DiagnosticPath(t));
                    continue;
                }
                if(!matches) File.WriteAllText(Marker("surface-mismatch.json"),JsonSerializer.Serialize(new{path=WorldCatalogue.DiagnosticPath(t),key=fixture.Key,expectedActive=fixture.Data[0],actualActive=t.gameObject.activeSelf,expected=fixture.Enabled,actual=enabled,received=_world.ReceivedSurfaceForTest(fixture.Key),components=components.Select(c=>c.GetIl2CppType().Name),parents=t.GetComponentsInParent<MonoBehaviour>(true).Select(c=>new{path=WorldCatalogue.DiagnosticPath(c.transform),type=c.GetIl2CppType().Name,c.enabled})}));
                TravelCheck(matches,"urine-driven renderer/collision/activation matches P1: "+t.name);
                File.AppendAllText(Marker("urine-pose-comparison.jsonl"),JsonSerializer.Serialize(new{phase=name,path=WorldCatalogue.DiagnosticPath(t),expected=pose,position=t.localPosition.ToString("F5"),rotation=t.localEulerAngles.ToString("F5"),scale=t.localScale.ToString("F5"),body=t.GetComponent<Rigidbody>() is {} body?new{position=body.position.ToString("F5"),rotation=body.rotation.eulerAngles.ToString("F5"),interpolation=body.interpolation.ToString(),kinematic=body.isKinematic}:null,animators=t.GetComponentsInParent<Animator>(true).Select(a=>new{a.name,a.enabled}),listeners=UnityEngine.Object.FindObjectsOfType<PissFillListenerMovingObject>(true).Where(l=>l._moveTransform?.Pointer==t.Pointer).Select(l=>new{l.name,l.enabled,start=l._startPos.position.ToString("F5"),end=l._endPos.position.ToString("F5")})})+"\n");
                TravelCheck(Vector3.Distance(t.localPosition,new(pose[0],pose[1],pose[2]))<0.03f && Quaternion.Angle(t.localRotation,Quaternion.Euler(pose[3],pose[4],pose[5]))<1 && Vector3.Distance(t.localScale,new(pose[6],pose[7],pose[8]))<0.03f,"urine-driven motion/scale matches P1: "+t.name);
                continue;
            }
            var c=_world!.ComponentForTest(fixture.Key);TravelCheck(c!=null,"urine snapshot target exists: "+fixture.Key[..8]);var data=SharedWorld.UrineSnapshot(c!);
            TravelCheck(data.Length==fixture.Data.Length && data.Zip(fixture.Data).All(p=>EquivalentNativeText(p.First,p.Second)),"current urine target state matches host: "+c!.name);
        }
    }
}
