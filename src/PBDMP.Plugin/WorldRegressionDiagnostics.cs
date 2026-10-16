using System.Text.Json;
using Hyperstrange.Base;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using Hyperstrange.PBD.NpcComponents;
using Il2CppInterop.Runtime;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PBDMP.Plugin;

public sealed partial class MultiplayerBehaviour
{
    private int _worldRegressionStep,_worldRegressionEffects;
    private bool _nativeBloodObserved;
    private float _worldRegressionAt;
    private Component? _worldRegressionTarget;
    private ExplosiveBarrel? _worldRegressionShotBarrel;
    private void RunWorldRegression()
    {
        if(_worldRegressionStep==99) return;
        File.WriteAllText(Marker(TravelRole+"-regression-state.txt"),$"step={_worldRegressionStep}, status={_lifecycle?.Status}, impact={_world?.SolidImpactEffectsForTest}, particles={_world?.EffectParticlesForTest}");
        if(_worldRegressionStep==0)
        {
            _port!.text=Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!;
            if(TravelRole=="host") {Host();GameManager.LoadLevel(TestLevel(),true);}else {_address!.text="127.0.0.1";Join();}
            _worldRegressionStep=1;return;
        }
        if(_lifecycle is {NativeReady:false}) {_lifecycle.AutoContinue();return;}
        if(TravelRole=="host") WorldRegressionHost();else WorldRegressionClient();
    }
    private void WorldRegressionHost()
    {
        switch(_worldRegressionStep)
        {
            case 1 when _world?.Active==true:
                QuietNpcs();SharedWorldPatches.DiagnosticFreeze=true;TimeManager.SetApplicationTimeScale(1);
                if(Environment.GetEnvironmentVariable("PBDMP_BLOOD_AUDIT")=="1") AuditNativeBloodPresentation();
                _actorTestAssets=new[]{Resources.FindObjectsOfTypeAll<NpcId>().First(a=>a._npcPrefab!=null && a.NpcModel!=null &&
                    a._npcPrefab.GetComponent<Npc>()!=null && !DlcActorModels.Contains(a.NpcModel.GetIl2CppType().Name) && a._npcPrefab.GetComponentInChildren<NpcVfxComponent>(true)?._bloodDecal!=null)};
                SpawnActorFixture();TravelMark("host-ready");_worldRegressionStep=2;break;
            case 2 when _lifecycle!.State.Released && Marked("regression-client-ready"):
                GameManager.DisableMenu();TimeManager.SetApplicationTimeScale(1);
                if(Environment.GetEnvironmentVariable("PBDMP_BLOOD_AUDIT")=="1")
                {
                    var nativeHit=NativeFleshRaycast();SolidManager.Hit(nativeHit);
                    File.WriteAllText(Marker("native-solid-key"),_world!.NativeSolidKeyForTest);
                    TravelCheck(_world.NativeSolidKeyForTest.Length>0,"native weapon hit resolves an actual game flesh impact asset");
                    TravelMark("native-solid-fired");_worldRegressionStep=20;break;
                }
                TravelMark("regression-hit-go");_worldRegressionStep=3;break;
            case 20 when Marked("native-solid-checked"):
                TravelMark("regression-hit-go");_worldRegressionStep=3;break;
            case 3 when Marked("regression-client-hit") && _actorTestNpc!.HitPoints.Current<57:
                TravelCheck(_actorTestNpc.HitPoints.Current==50,"P2 attack applies damage once on P1");TravelMark("regression-host-hit");_worldRegressionStep=4;break;
            case 4 when Marked("regression-blood-checked"):
                _worldRegressionTarget=LevelManager.CurrentLevel.name=="LEVEL_AD_Neighborhood"
                    ?Object.FindObjectsOfType<FuseBox>(true).First(b=>b.gameObject.scene.IsValid()).Cast<Component>()
                    :Object.FindObjectsOfType<ExplosiveBarrel>(true).Where(b=>b.gameObject.scene.IsValid()).OrderBy(b=>_world!.CatalogueForTest.MissionBreakKeys.Select(k=>_world.ComponentForTest(k)?.TryCast<BreakableObject>()).Where(w=>w!=null).Min(w=>Vector3.Distance(w!._breaker._wholeObject.GetComponentInChildren<Renderer>(true).bounds.center,b.transform.position))).First().Cast<Component>();
                File.WriteAllLines(Marker("regression-barrels.txt"),Object.FindObjectsOfType<ExplosiveBarrel>(true).Where(b=>b.gameObject.scene.IsValid()).Select(b=>MissionPath(b.transform)));
                File.WriteAllText(Marker("regression-target-path.txt"),MissionPath(_worldRegressionTarget.transform));
                File.WriteAllLines(Marker("regression-layout.tsv"),Object.FindObjectsOfType<BreakableObject>(true).Where(b=>b.gameObject.scene.IsValid() && _world!.CatalogueForTest.MissionBreakKeys.Contains(_world.KeyForTest(b)??"")).Select(b=>$"{MissionPath(b.transform)}\t{b._breaker._wholeObject?.GetComponentInChildren<Renderer>(true)?.bounds.center}\tindestructible={b._indestructible}"));
                File.WriteAllLines(Marker("regression-required-breaks.txt"),_world!.CatalogueForTest.MissionBreakKeys.Select(k=>MissionPath(_world.ComponentForTest(k)!.transform)));
                File.WriteAllText(Marker("regression-target"),_world!.KeyForTest(_worldRegressionTarget)!);TravelMark("regression-fire-go");_worldRegressionStep=5;break;
            case 5 when Marked("regression-fire-sent"):
                var exploded=_worldRegressionTarget!.TryCast<FuseBox>()?._exploded??_worldRegressionTarget.TryCast<ExplosiveBarrel>()!._exploded;
                if(!exploded) return;
                TravelCheck(true,"P2 powered urine/fire commits the native host explosion and listeners");
                if(_worldRegressionTarget.TryCast<ExplosiveBarrel>() is {} overkill)
                    TravelCheck(overkill._health.Current<0,"native barrel flame overkill retains below-zero health for replication");
                _worldRegressionAt=Time.unscaledTime+2;_worldRegressionStep=6;break;
            case 6 when Time.unscaledTime>=_worldRegressionAt:
                if(LevelManager.CurrentLevel.name=="LEVEL_AD_Prison")
                {
                    var pipe=Object.FindObjectsOfType<BreakableObject>(true).First(b=>MissionPath(b.transform)=="OBJECTIVES/Pipe_hole");
                    TravelCheck(pipe._destroyed,"authored Prison barrel explosion opens OBJECTIVES/Pipe_hole on P1");
                }
                // Native control-panel lights deliberately flicker with Time.time.
                // Compare one stable authoritative state after the shared hold,
                // rather than an older P1 sample against P2's later live frame.
                _lifecycle!.HostPauseRequest(true);_worldRegressionAt=Time.unscaledTime+.35f;_worldRegressionStep=60;break;
            case 60 when Time.unscaledTime>=_worldRegressionAt:
                WriteUrineFixture("regression-world.json");
                File.WriteAllText(Marker("regression-breaks.json"),JsonSerializer.Serialize(Object.FindObjectsOfType<BreakableObject>(true).Where(b=>b.gameObject.scene.IsValid() && b._destroyed && _world!.CatalogueForTest.MissionBreakKeys.Contains(_world.KeyForTest(b)??"")).Select(MissionBreakState)));
                TravelMark("regression-world-ready");_worldRegressionStep=7;break;
            case 7 when Marked("regression-world-checked"):
                _lifecycle!.HostPauseRequest(false);TravelMark("regression-world-resumed");_worldRegressionStep=70;break;
            case 70 when _world!.SpecialExplosionsForTest>0:
                TravelCheck(_session!.Snapshot.Connected,"special native explosion request preserves the session");
                _worldRegressionShotBarrel=Object.FindObjectsOfType<ExplosiveBarrel>(true).First(b=>b.gameObject.scene.IsValid() && !b._exploded && b.gameObject.activeInHierarchy);
                File.WriteAllText(Marker("regression-shot-barrel"),_world.KeyForTest(_worldRegressionShotBarrel)!);
                TravelMark("regression-explosion-checked");_worldRegressionStep=80;break;
            case 80 when Marked("regression-barrel-shot-sent") && _worldRegressionShotBarrel!._exploded:
                TravelCheck(_session!.Snapshot.Connected,"P2 native barrel hit explodes on P1 without disconnecting");TravelMark("regression-barrel-shot-host");_worldRegressionStep=8;break;
            case 8 when Marked("regression-client-done"):
                if(_actorTestNpc!=null) _actorTestNpc.gameObject.SetActive(false);
                ResetSession();SharedWorldPatches.DiagnosticFreeze=false;_worldRegressionStep=9;break;
            case 9 when Marked("client-pass"):
                TravelMark("host-pass");_worldRegressionStep=99;break;
        }
    }
    private void WorldRegressionClient()
    {
        switch(_worldRegressionStep)
        {
            case 1 when _world?.Active==true && _lifecycle!.WorldReady && _lifecycle.State.Released && Marked("actor-prepared-0"):
                _actorTestNpc=_world.EnemyForTest(File.ReadAllText(Marker("actor-asset-0")));
                if(_actorTestNpc==null || _actorTestNpc.HitPoints.Current!=57) return;
                _worldRegressionEffects=_world.SolidImpactEffectsForTest;
                Player.Instance.Teleport(_actorTestNpc.FeetPosition+Vector3.right*2,Vector3.zero,Vector3.zero);TravelMark("regression-client-ready");_worldRegressionStep=2;break;
            case 2 when Marked("native-solid-fired") && !Marked("native-solid-checked"):
                _worldRegressionAt=Time.unscaledTime+2;_worldRegressionStep=20;break;
            case 20:
                _nativeBloodObserved|=_world!.SolidImpactEffectsForTest>_worldRegressionEffects && _world.ReceivedSolidKeyForTest==File.ReadAllText(Marker("native-solid-key")) && _world.EffectParticlesForTest>0;
                if(Time.unscaledTime<_worldRegressionAt) break;
                TravelCheck(_nativeBloodObserved,"P2 receives the exact native flesh/blood burst from P1's weapon impact path");
                _worldRegressionEffects=_world.SolidImpactEffectsForTest;TravelMark("native-solid-checked");_worldRegressionStep=2;break;
            case 2 when Marked("regression-hit-go") && !_lifecycle!.Frozen:
                SolidManager.Hit(NativeFleshRaycast());
                _actorTestNpc!.Hit(new HitData{dealer=Player.Instance.Cast<IAiActor>(),originEntity=_actorTestNpc.NpcId,damage=7,weakSpotMultiplier=1,
                    position=_actorTestNpc.CenterOfMass,direction=Vector3.left,normal=Vector3.right});
                TravelMark("regression-client-hit");_worldRegressionStep=3;break;
            case 3 when Marked("regression-host-hit"):
                TravelCheck(_world!.LocalSolidImpactsForTest>0,"P2's actual native flesh impact plays immediately while damage remains host-owned");
                TravelCheck(_world.SolidImpactEffectsForTest==_worldRegressionEffects,"P2 does not replay its own authoritative impact echo");TravelMark("regression-blood-checked");_worldRegressionStep=4;break;
            case 4 when Marked("regression-fire-go"):
                _worldRegressionTarget=_world!.ComponentForTest(File.ReadAllText(Marker("regression-target")))!;
                Player.Instance.Teleport(_worldRegressionTarget.transform.position+Vector3.back*2,Vector3.zero,Vector3.zero);_worldRegressionAt=Time.unscaledTime+0.6f;_worldRegressionStep=5;break;
            case 5 when Time.unscaledTime>=_worldRegressionAt:
                if(_worldRegressionTarget!.TryCast<ExplosiveBarrel>() is {} barrel) barrel.FlameTick(Player.Instance.Cast<IAiActor>(),0.25f,10000);
                else
                {
                    var fire=Resources.FindObjectsOfTypeAll<WeaponId>().First(w=>w.PlayerWeaponModel?.TryCast<PlayerWeaponModelDong>()?.PeeType==PeeType.Fire);
                    Player.Instance.SetQuickPiss(fire);var dong=Player.Instance.CurrentDong;var model=fire.PlayerWeaponModel.Cast<PlayerWeaponModelDong>();
                    var collider=_worldRegressionTarget.GetComponentInChildren<Collider>();var bounds=collider.bounds;
                    TravelCheck(collider.Raycast(new Ray(bounds.center+Vector3.back*(bounds.extents.magnitude+1),Vector3.forward),out var hit,20),"fire urine fixture hits the actual fuse collider");
                    var renderer=PoolManager.SpawnPoolable(model.StreamRenderer,hit.point,Quaternion.identity).Cast<PeeStreamRenderer>();
                    var particle=PoolManager.SpawnPoolable(model.CollisionParticle,hit.point,Quaternion.identity).Cast<PeeCollisionParticle>();
                    particle.Init(renderer,Vector3.forward,dong,Player.Instance.Cast<IAiActor>());
                    particle.HandleCollision(hit,0.02f);PoolManager.ReturnToPool(particle.Cast<IPoolable>());
                }
                TravelMark("regression-fire-sent");_worldRegressionStep=6;break;
            case 6 when Marked("regression-world-ready") && _lifecycle!.State.Paused:
                _worldRegressionAt=Time.unscaledTime+.75f;_worldRegressionStep=60;break;
            case 60 when Time.unscaledTime>=_worldRegressionAt:
                CheckUrineFixture("regression-world.json",Environment.GetEnvironmentVariable("PBDMP_PROGRESSION_ONLY")=="1");
                var breaks=Object.FindObjectsOfType<BreakableObject>(true).Where(b=>b.gameObject.scene.IsValid()).ToDictionary(b=>WorldCatalogue.TransformKey(b.transform));
                foreach(var expected in JsonSerializer.Deserialize<MissionBreakFixture[]>(File.ReadAllText(Marker("regression-breaks.json")))!)
                {
                    var target=breaks[expected.Key];var actual=MissionBreakState(target);
                    File.AppendAllText(Marker("regression-break-comparison.jsonl"),JsonSerializer.Serialize(new{expected,actual,whole=MissionPath(target._breaker._wholeObject.transform),parts=target._breaker._parts!=null?MissionPath(target._breaker._parts.transform):""})+"\n");
                    TravelCheck(actual.Destroyed==expected.Destroyed && actual.Blocking==expected.Blocking && actual.Visible==expected.Visible && actual.StaticParts.SequenceEqual(expected.StaticParts),"explosion-driven mission breakable retains matching geometry: "+expected.Path);
                }
                TravelMark("regression-world-checked");_worldRegressionStep=61;break;
            case 61 when Marked("regression-world-resumed") && !_lifecycle!.Frozen:
                new Explosion(new ExplosionSettings{radius=0.25f,damage=0}).Invoke(Player.Instance.CenterOfMass+Vector3.up*4,Player.Instance.Cast<IAiActor>(),particleRotation:new Il2CppSystem.Nullable<Quaternion>());
                _worldRegressionStep=7;break;
            case 7 when Marked("regression-explosion-checked"):
                TravelCheck(_session!.Snapshot.Connected,"P2 remains connected after special explosion replay");
                _worldRegressionShotBarrel=_world!.ComponentForTest(File.ReadAllText(Marker("regression-shot-barrel")))!.Cast<ExplosiveBarrel>();
                Player.Instance.Teleport(_worldRegressionShotBarrel.transform.position+Vector3.back*2,Vector3.zero,Vector3.zero);_worldRegressionAt=Time.unscaledTime+0.6f;_worldRegressionStep=70;break;
            case 70 when Time.unscaledTime>=_worldRegressionAt:
                _worldRegressionShotBarrel!.Hit(new HitData{dealer=Player.Instance.Cast<IAiActor>(),damage=10000,weakSpotMultiplier=1,position=_worldRegressionShotBarrel.transform.position,direction=Vector3.forward});
                TravelMark("regression-barrel-shot-sent");_worldRegressionStep=71;break;
            case 71 when Marked("regression-barrel-shot-host") && _worldRegressionShotBarrel!._exploded:
                TravelCheck(_session!.Snapshot.Connected,"P2 receives the native barrel hit result and stays connected");TravelMark("regression-client-done");_worldRegressionStep=8;break;
            case 8 when SessionLifecycle.Current==null && !SessionManager.SessionActive:
                TravelMark("client-pass");_worldRegressionStep=99;break;
        }
    }
}
