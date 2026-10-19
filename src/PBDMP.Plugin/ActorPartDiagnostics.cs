using System.Globalization;
using Hyperstrange.Base;
using Hyperstrange.PBD;
using Hyperstrange.PBD.NpcComponents;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PBDMP.Plugin;

public sealed partial class MultiplayerBehaviour
{
    private int _partTestStep,_partTestIndex,_partTestRequests;
    private Component? _partTestTarget;
    private Npc? _partTestNpc;
    private float _partTestAt;
    private static readonly string[] PartFixtures={"MiniCovidEye","CovidEye","ElonMechWeakSpotComponent","BalloonGroup","FakeCitizen","FakePostalDude"};
    private void RunActorPartTest()
    {
        if(_partTestStep==99) return;
        if(TravelRole=="client" && _partTestStep==5 && _session?.Snapshot.Connected!=true)
        {TravelMark("client-pass");_partTestStep=99;return;}
        File.WriteAllText(Marker(TravelRole+"-state.txt"),$"parts={_partTestStep}, index={_partTestIndex}, ready={_lifecycle?.NativeReady}, world={_lifecycle?.WorldReady}");
        if(_partTestStep==0)
        {
            _port!.text=Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!;
            if(TravelRole=="host") {Host();GameManager.LoadLevel(TestLevel(),true);}else{_address!.text="127.0.0.1";Join();}
            _partTestStep=1;return;
        }
        if(_lifecycle is {NativeReady:false}) {_lifecycle.AutoContinue();return;}
        if(_world?.Active!=true) return;
        if(TravelRole=="host") PartTestHost();else PartTestClient();
    }
    private void PartTestHost()
    {
        switch(_partTestStep)
        {
            case 1:
                if(Environment.GetEnvironmentVariable("PBDMP_URINE_PARTS_TEST")=="1") _partTestIndex=2;
                QuietNpcs();SharedWorldPatches.DiagnosticFreeze=true;PreparePartFixture();TravelMark("host-ready");_partTestStep=2;break;
            case 2 when Marked("part-ready-"+_partTestIndex):
                _partTestRequests=_world!.ActorPartHitsForTest;TravelMark("part-hit-ready-"+_partTestIndex);_partTestStep=3;break;
            case 3 when Marked("part-hit-"+_partTestIndex) && _world!.ActorPartHitsForTest>_partTestRequests:
                if(_partTestAt==0) {_partTestAt=Time.unscaledTime+0.4f;return;}if(Time.unscaledTime<_partTestAt) return;
                TravelCheck(_world.ActorPartHitsForTest==_partTestRequests+1,"P2 part hit reaches host once: "+PartFixtures[_partTestIndex]);
                File.WriteAllLines(Marker("part-result-"+_partTestIndex),PartFixtureState(_partTestTarget!).Select(v=>v.ToString("R",CultureInfo.InvariantCulture)));
                TravelMark("part-result-ready-"+_partTestIndex);_partTestStep=4;break;
            case 4 when Marked("part-result-received-"+_partTestIndex):
                var bossView=SharedWorld.BossView();
                if(_partTestNpc!=null && bossView?._currentBoss?.Pointer==_partTestNpc.Pointer) bossView.OnBossEncounterEnd(_partTestNpc);
                if(_partTestNpc!=null) _partTestNpc.gameObject.SetActive(false);
                _partTestIndex++;
                if(_partTestIndex<PartFixtures.Length && Environment.GetEnvironmentVariable("PBDMP_URINE_PARTS_TEST")!="1") {PreparePartFixture();_partTestStep=2;break;}
                ResetSession();SharedWorldPatches.DiagnosticFreeze=false;TravelMark("host-pass");_partTestStep=99;break;
        }
    }
    private void PreparePartFixture()
    {
        var type=PartFixtures[_partTestIndex];_partTestAt=0;_partTestNpc=null;
        if(type is "FakeCitizen" or "FakePostalDude")
        {
            _partTestTarget=_world!.ScriptActorForTest(type);TravelCheck(_partTestTarget!=null,"campaign includes scripted shoot-reactive human: "+type);
            var actual=NativeAssetGraph.Actual(_partTestTarget!);
            actual.GetType().GetProperty(type=="FakeCitizen"?"_running":"_runTriggered")!.SetValue(actual,false);
            _partTestTarget!.gameObject.SetActive(true);
            if(_partTestTarget.TryCast<FakePostalDude>() is {} dude) dude.Hide(false);
            File.WriteAllText(Marker("part-key-"+_partTestIndex),_world.KeyForTest(_partTestTarget)!);
            File.WriteAllText(Marker("part-asset-"+_partTestIndex),"");
        }
        else
        {
            var actorType=type switch{"MiniCovidEye"=>"MiniCovid","CovidEye"=>"Covid","ElonMechWeakSpotComponent"=>"ElonMech",_=>"BalloonMan"};
            var asset=Resources.FindObjectsOfTypeAll<NpcId>().First(a=>a._npcPrefab!=null && a._npcPrefab.GetComponent<Npc>()?.GetIl2CppType().Name==actorType);
            var position=Player.Instance.FeetPosition+Vector3.forward*8;
            var source=new GameObject("PBDMP part test spawn "+_partTestIndex);source.transform.position=position;
            var spawn=source.AddComponent<SpawnPoint>();spawn.Init(asset);
            typeof(SpawnPoint).GetProperty(nameof(SpawnPoint.NpcPrefabPoolable))!.SetValue(spawn,asset._npcPrefab.GetComponent<Npc>().Cast<IPoolable>());
            spawn.Spawn();_partTestNpc=spawn.SpawnedNpc;TravelCheck(_partTestNpc!=null,"native runtime spawn supplies "+actorType);
            foreach(var agent in _partTestNpc!.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) agent.enabled=false;
            _partTestNpc.HitPoints.SetMax(500);_partTestNpc.HitPoints.SetCurrentSilent(500);
            _partTestTarget=_partTestNpc.GetComponentsInChildren<Component>(true).First(c=>c.GetIl2CppType().Name==type);
            if(type=="CovidEye") {var hud=SharedWorld.BossView();TravelCheck(hud!=null,"native player HUD includes boss view");hud!.OnBossEncounterStart(_partTestNpc);}
            if(_partTestTarget.TryCast<CovidEye>() is {} eye) eye.Open();
            if(Environment.GetEnvironmentVariable("PBDMP_URINE_PARTS_TEST")=="1" && _partTestTarget.TryCast<ElonMechWeakSpotComponent>() is {} urinePart) urinePart._armor._hooked=true;
            File.WriteAllText(Marker("part-asset-"+_partTestIndex),WorldCatalogue.NpcAssetKey(asset));
            File.WriteAllText(Marker("part-key-"+_partTestIndex),SharedWorld.ActorPartKey(_partTestTarget,_partTestNpc.transform));
        }
        File.WriteAllLines(Marker("part-before-"+_partTestIndex),PartFixtureState(_partTestTarget!).Select(v=>v.ToString("R",CultureInfo.InvariantCulture)));
        TravelMark("part-prepared-"+_partTestIndex);
    }
    private static float[] PartFixtureState(Component target)
    {
        var npc=target.GetComponentInParent<Npc>();
        var health=target.TryCast<BalloonGroup>()?.HitPoints??target.TryCast<ElonMechWeakSpotComponent>()?.HitPoints;
        var actual=NativeAssetGraph.Actual(target);var field=target.GetIl2CppType().Name switch{"FakeCitizen"=>"_running","FakePostalDude"=>"_runTriggered","CovidEye"=>"_weakSpotHitted",_=>""};
        var flag=field.Length!=0 && actual.GetType().GetProperty(field)?.GetValue(actual) is true?1f:0f;
        return new[]{npc?.HitPoints.Current??-1,health?.Current??-1,flag};
    }
    private void PartTestClient()
    {
        if(_partTestStep==1 && Environment.GetEnvironmentVariable("PBDMP_URINE_PARTS_TEST")=="1") _partTestIndex=2;
        switch(_partTestStep)
        {
            case 1 when _lifecycle!.WorldReady && _lifecycle.State.Released && Marked("part-prepared-"+_partTestIndex):
                var asset=File.ReadAllText(Marker("part-asset-"+_partTestIndex));var key=File.ReadAllText(Marker("part-key-"+_partTestIndex));
                _partTestNpc=asset.Length!=0?_world!.EnemyForTest(asset):null;
                _partTestTarget=asset.Length==0?_world!.ComponentForTest(key):_partTestNpc?.GetComponentsInChildren<Component>(true).FirstOrDefault(c=>SharedWorld.ActorPartKey(c,_partTestNpc.transform)==key);
                if(_partTestTarget==null) return;
                var before=File.ReadAllLines(Marker("part-before-"+_partTestIndex)).Select(v=>float.Parse(v,CultureInfo.InvariantCulture)).ToArray();
                if(!PartFixtureState(_partTestTarget).SequenceEqual(before)) return;
                if(PartFixtures[_partTestIndex]=="CovidEye")
                {
                    var hud=SharedWorld.BossView()!;
                    if(hud._currentBoss?.Pointer!=_partTestNpc!.Pointer || !hud._healthBar.activeSelf) return;
                    TravelCheck(true,"P2 receives native boss HUD from the current encounter");
                }
                TravelCheck(true,"late/dynamic actor part state matches host: "+PartFixtures[_partTestIndex]);
                Player.Instance.Teleport(_partTestTarget.transform.position+Vector3.right*2,Vector3.zero,Vector3.zero);
                _partTestAt=Time.unscaledTime+0.7f;_partTestStep=2;break;
            case 2 when Time.unscaledTime>=_partTestAt:
                TravelMark("part-ready-"+_partTestIndex);_partTestStep=3;break;
            case 3 when Marked("part-hit-ready-"+_partTestIndex):
                var original=PartFixtureState(_partTestTarget!);
                if(Environment.GetEnvironmentVariable("PBDMP_URINE_PARTS_TEST")=="1") _partTestTarget!.Cast<IPissable>().Piss(new PissData(Player.Instance.Cast<IAiActor>()));
                else _partTestTarget!.Cast<IHittable>().Hit(new HitData{dealer=Player.Instance.Cast<IAiActor>(),damage=7,weakSpotMultiplier=1,position=_partTestTarget.transform.position,direction=Vector3.forward});
                TravelCheck(PartFixtureState(_partTestTarget).SequenceEqual(original),"client part hit cannot mutate local gameplay: "+PartFixtures[_partTestIndex]);
                TravelMark("part-hit-"+_partTestIndex);_partTestStep=4;break;
            case 4 when Marked("part-result-ready-"+_partTestIndex):
                var expected=File.ReadAllLines(Marker("part-result-"+_partTestIndex)).Select(v=>float.Parse(v,CultureInfo.InvariantCulture)).ToArray();
                // P1 deactivates the killed enemy (its part then has no active NPC: -1), after which P2
                // destroys the replica it instantiated instead of only hiding it.
                if(_partTestTarget==null)
                {
                    TravelCheck(_partTestNpc==null && expected[0]<0,"P2 retires the replica after P1 deactivates the killed actor: "+PartFixtures[_partTestIndex]);
                    TravelMark("part-result-received-"+_partTestIndex);_partTestIndex++;_partTestStep=_partTestIndex<PartFixtures.Length && Environment.GetEnvironmentVariable("PBDMP_URINE_PARTS_TEST")!="1"?1:5;break;
                }
                if(!PartFixtureState(_partTestTarget!).SequenceEqual(expected)) return;
                if(PartFixtures[_partTestIndex]=="CovidEye")
                {
                    var hud=SharedWorld.BossView()!;
                    TravelCheck(Mathf.Abs(hud._fillImage.fillAmount-_partTestNpc!.HitPoints.Ratio)<0.0001f,"P2 native boss health bar follows authoritative damage");
                }
                TravelCheck(true,"host-native part outcome restored on P2: "+PartFixtures[_partTestIndex]+" "+string.Join(',',expected));
                if(Environment.GetEnvironmentVariable("PBDMP_URINE_PARTS_TEST")=="1") TravelCheck(expected[1]<1000,"P2 native urine damages the exposed canonical boss weak spot");
                TravelMark("part-result-received-"+_partTestIndex);_partTestIndex++;_partTestStep=_partTestIndex<PartFixtures.Length && Environment.GetEnvironmentVariable("PBDMP_URINE_PARTS_TEST")!="1"?1:5;break;
            case 5 when _session?.Snapshot.Connected!=true:
                TravelMark("client-pass");_partTestStep=99;break;
        }
    }
}
