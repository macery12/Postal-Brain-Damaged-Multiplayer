using System.Globalization;
using Hyperstrange.Base;
using Hyperstrange.PBD;
using Hyperstrange.PBD.NpcComponents;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PBDMP.Plugin;

public sealed partial class MultiplayerBehaviour
{
    private int _actorTestStep,_actorTestIndex;
    private NpcId[] _actorTestAssets=Array.Empty<NpcId>();
    private Npc? _actorTestNpc;
    private float _actorTestAt;
    private int _actorTestDeaths,_actorTestEffects;
    private uint _actorTestId;
    private string _actorTestName="";
    private Vector3? _actorFixturePosition;
    // These model declarations are under the game's Npc/DLC asset menu. Some
    // shipped resource entries are unfinished placeholders, not campaign actors.
    private static readonly HashSet<string> DlcActorModels=new(StringComparer.Ordinal)
    {"NpcModelBalloonMan","NpcModelBigCat","NpcModelCatAgent","NpcModelChad","NpcModelDudeDoll","NpcModelHotDogger","NpcModelMotoKid","NpcModelMotoDad","NpcModelMotoMom","NpcModelPuppetPresident","NpcModelThot"};
    private void RunActorTest()
    {
        if(_actorTestStep==99) return;
        if(TravelRole=="client" && _actorTestStep==8 && _session?.Snapshot.Connected!=true)
        {TravelMark("client-pass");_actorTestStep=99;return;}
        File.WriteAllText(Marker(TravelRole+"-state.txt"),$"actors={_actorTestStep}, index={_actorTestIndex}, asset={_actorTestName}, native={_lifecycle?.NativeReady}, world={_lifecycle?.WorldReady}, frozen={_lifecycle?.Frozen}");
        if(_actorTestStep==0)
        {
            _port!.text=Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!;
            if(TravelRole=="host") {Host();GameManager.LoadLevel(TestLevel(),true);} else {_address!.text="127.0.0.1";Join();}
            _actorTestStep=1;return;
        }
        if(_lifecycle is {NativeReady:false}) {_lifecycle.AutoContinue();return;}
        if(_world?.Active!=true) return;
        if(_actorTestNpc!=null)
        {
            File.WriteAllText(Marker(TravelRole+"-actor-position.txt"),VectorText(_actorTestNpc.transform.position));
            File.WriteAllLines(Marker(TravelRole+"-actor-bodies.txt"),_actorTestNpc.GetComponentsInChildren<Rigidbody>(true).Select(b=>$"{b.name} kinematic={b.isKinematic} gravity={b.useGravity} position={VectorText(b.position)}"));
        }
        if(TravelRole=="host") ActorTestHost();else ActorTestClient();
    }
    private void ActorTestHost()
    {
        switch(_actorTestStep)
        {
            case 1:
                QuietNpcs();SharedWorldPatches.DiagnosticFreeze=true;
                _actorTestAssets=Resources.FindObjectsOfTypeAll<NpcId>().Where(a=>a._npcPrefab!=null && a._npcPrefab.GetComponent<Npc>()!=null && a.NpcModel!=null && !DlcActorModels.Contains(a.NpcModel.GetIl2CppType().Name))
                    .GroupBy(a=>a._npcPrefab.GetComponent<Npc>().GetIl2CppType().Name).Select(g=>g.First()).OrderBy(a=>a.name).ToArray();
                File.WriteAllLines(Marker("actor-families.tsv"),_actorTestAssets.Select(a=>$"{a.name}\t{a.ID}\t{a._npcPrefab.GetComponent<Npc>().GetIl2CppType().Name}"));
                TravelCheck(_actorTestAssets.Length>0,"campaign loads native actor prefab families");
                SpawnActorFixture();TravelMark("host-ready");_actorTestStep=2;break;
            case 2 when Marked("actor-ready-"+_actorTestIndex) && _world!.Remote!=null:
                _actorTestId=_world.ActorIdForTest(_actorTestNpc!);
                TravelCheck(_actorTestId!=0,"runtime-spawned actor has an authoritative lifetime: "+_actorTestNpc!.name);
                if(_actorTestNpc.GetComponent<NpcAwarenessComponent>() is {} awareness)
                {
                    awareness.UpdatePlayerTarget(100);
                    var position=awareness.transform.position;
                    var local=Player.Instance.Cast<IAiActor>();var remote=_world.Remote!;
                    var expected=!local.IsAlive || Vector3.Distance(position,remote.CenterOfMass)<Vector3.Distance(position,local.CenterOfMass)?remote.Pointer:local.Pointer;
                    TravelCheck(awareness._player?.Pointer==expected,"native awareness considers the nearer living player: "+_actorTestNpc.name);
                }
                _actorTestNpc.IsFrozen=true;_actorTestNpc.IsBurned=true;
                _actorTestNpc._npcHitProcessorComponent.ShieldProcessor?.SetCurrentArmor(23);
                _actorTestNpc.transform.position+=Vector3.right;
                TravelMark("actor-status-"+_actorTestIndex);_actorTestStep=3;break;
            case 3 when Marked("actor-status-received-"+_actorTestIndex):
                _actorTestNpc!.IsFrozen=false;_actorTestNpc.IsBurned=false;
                _actorTestNpc._npcHitProcessorComponent.ShieldProcessor?.SetCurrentArmor(0);
                File.WriteAllText(Marker("actor-pose-"+_actorTestIndex),VectorText(_actorTestNpc.transform.position));
                TravelMark("actor-hit-ready-"+_actorTestIndex);_actorTestStep=4;break;
            case 4 when Marked("actor-hit-"+_actorTestIndex):
                // Wait for the host to process the actual client request before checking its native outcome.
                if(_actorTestAt==0) {_actorTestAt=Time.unscaledTime+0.6f;return;}
                if(Time.unscaledTime<_actorTestAt) return;
                TravelCheck(_world!.AttackOriginCount(_actorTestNpc!.NpcId.ID)>0,"P2 attack reaches host-native actor: "+_actorTestNpc.name);
                File.WriteAllText(Marker("actor-health-"+_actorTestIndex),_actorTestNpc.HitPoints.Current.ToString("R",CultureInfo.InvariantCulture));
                File.WriteAllText(Marker("actor-pose-"+_actorTestIndex),VectorText(_actorTestNpc.transform.position));
                TravelMark("actor-health-result-"+_actorTestIndex);_actorTestAt=0;_actorTestStep=5;break;
            case 5 when Marked("actor-health-received-"+_actorTestIndex):
                _actorTestDeaths=_world!.ActorDeathEffectsForTest;
                _actorTestNpc!.Kill(true);
                File.WriteAllText(Marker("actor-death-count-"+_actorTestIndex),(_world.ActorDeathEffectsForTest-_actorTestDeaths).ToString(CultureInfo.InvariantCulture));
                TravelMark("actor-dead-"+_actorTestIndex);_actorTestStep=6;break;
            case 6 when Marked("actor-death-received-"+_actorTestIndex):
                if(_actorTestNpc!=null) _actorTestNpc.gameObject.SetActive(false);
                _actorTestIndex++;
                if(_actorTestIndex<_actorTestAssets.Length) {SpawnActorFixture();_actorTestStep=2;break;}
                TravelMark("actor-all-complete");_actorTestStep=7;break;
            case 7 when Marked("actor-client-complete"):
                ResetSession();SharedWorldPatches.DiagnosticFreeze=false;TravelMark("host-pass");_actorTestStep=99;break;
        }
    }
    private static string VectorText(Vector3 position)=>$"{position.x.ToString("R",CultureInfo.InvariantCulture)},{position.y.ToString("R",CultureInfo.InvariantCulture)},{position.z.ToString("R",CultureInfo.InvariantCulture)}";
    private void SpawnActorFixture()
    {
        var asset=_actorTestAssets[_actorTestIndex];
        _actorTestName=asset.name;
        if(_actorFixturePosition==null)
        {
            var desired=Player.Instance.FeetPosition+Player.Instance.transform.forward*6;
            TravelCheck(PlayerSessionState.FindNearby(Player.Instance,desired,out var grounded),"actor suite uses a grounded, unblocked fixture location");
            _actorFixturePosition=grounded;
        }
        var position=_actorFixturePosition.Value;
        // Death helpers have already been asserted by both peers. Retire their
        // collision bodies before reusing the controlled fixture location.
        foreach(var corpse in Object.FindObjectsOfType<NpcRagdoll>()) corpse.gameObject.SetActive(false);
        var source=new GameObject("PBDMP diagnostic runtime spawn "+_actorTestIndex);source.transform.position=position;
        var spawn=source.AddComponent<SpawnPoint>();spawn.Init(asset);
        typeof(SpawnPoint).GetProperty(nameof(SpawnPoint.NpcPrefabPoolable))!.SetValue(spawn,asset._npcPrefab.GetComponent<Npc>().Cast<IPoolable>());
        spawn.Spawn();_actorTestNpc=spawn.SpawnedNpc;
        TravelCheck(_actorTestNpc!=null,"native spawn point constructs "+asset.name);
        var root=_actorTestNpc!.gameObject;_actorTestNpc.HitPoints.SetMax(100);_actorTestNpc.HitPoints.SetCurrentSilent(57);
        foreach(var agent in root.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) agent.enabled=false;
        // This suite controls pose directly. With navigation disabled, native
        // rigidbodies otherwise fall into hazards and invalidate the next HP test.
        foreach(var body in root.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic=true;
        foreach(var animator in root.GetComponentsInChildren<Animator>(true)) animator.applyRootMotion=false;
        _actorTestNpc._npcHitProcessorComponent.ShieldProcessor?.SetCurrentArmor(0);
        foreach(var attack in root.GetComponentsInChildren<NpcMeleeDamageComponent>(true)) attack.SetActive(false);
        File.WriteAllText(Marker("actor-asset-"+_actorTestIndex),WorldCatalogue.NpcAssetKey(asset));
        File.WriteAllText(Marker("actor-pose-"+_actorTestIndex),VectorText(position));
        _actorTestAt=0;
        TravelMark("actor-prepared-"+_actorTestIndex);
    }
    private void ActorTestClient()
    {
        switch(_actorTestStep)
        {
            case 1 when _lifecycle!.WorldReady && _lifecycle.State.Released && Marked("actor-prepared-"+_actorTestIndex):
                var asset=File.ReadAllText(Marker("actor-asset-"+_actorTestIndex));
                _actorTestNpc=_world!.EnemyForTest(asset);if(_actorTestNpc==null || _actorTestNpc.HitPoints.Current!=57) return;
                _actorTestName=_actorTestNpc.name;
                TravelCheck(_actorTestNpc.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true).All(a=>!a.enabled),"replica navigation suppressed: "+_actorTestNpc.name);
                TravelCheck(_actorTestNpc.GetComponentsInChildren<Animator>(true).All(a=>!a.fireEvents),"replica animation callbacks suppressed: "+_actorTestNpc.name);
                var boid=_actorTestNpc.GetComponentInChildren<Boid>(true);
                if(boid!=null)
                {
                    var before=_actorTestNpc.transform.position;boid.OnUpdate(0.25f);boid.OnSlowUpdate(0.25f);
                    TravelCheck(_actorTestNpc.transform.position==before,"client flying update cannot simulate native movement: "+_actorTestNpc.name);
                }
                _actorTestEffects=_world.ActorDeathEffectsForTest;
                Player.Instance.Teleport(_actorTestNpc.FeetPosition+Vector3.right*2,Vector3.zero,Vector3.zero);
                _actorTestAt=Time.unscaledTime+0.7f;
                _actorTestStep=10;break;
            case 10 when Time.unscaledTime>=_actorTestAt:
                TravelMark("actor-ready-"+_actorTestIndex);_actorTestStep=2;break;
            case 2 when Marked("actor-status-"+_actorTestIndex) && _actorTestNpc!.IsFrozen && _actorTestNpc.IsBurned:
                TravelCheck(_actorTestNpc._npcHitProcessorComponent.ShieldProcessor is not {} shield || shield.CurrentShield==23,"enemy shield amount is authoritative: "+_actorTestNpc.name);
                TravelCheck(true,"enemy frozen/burning status reaches replica: "+_actorTestNpc.name);
                TravelMark("actor-status-received-"+_actorTestIndex);_actorTestStep=3;break;
            case 3 when Marked("actor-hit-ready-"+_actorTestIndex) && !_actorTestNpc!.IsFrozen && !_actorTestNpc.IsBurned:
                _actorTestNpc.Hit(new HitData{dealer=Player.Instance.Cast<IAiActor>(),originEntity=_actorTestNpc.NpcId,damage=7,weakSpotMultiplier=1,position=_actorTestNpc.CenterOfMass,direction=Vector3.forward});
                TravelCheck(_actorTestNpc.HitPoints.Current==57,"P2 native hit cannot apply independent damage: "+_actorTestNpc.name);
                TravelMark("actor-hit-"+_actorTestIndex);_actorTestStep=4;break;
            case 4 when Marked("actor-health-result-"+_actorTestIndex):
                var health=float.Parse(File.ReadAllText(Marker("actor-health-"+_actorTestIndex)),CultureInfo.InvariantCulture);
                if(_actorTestNpc!.HitPoints.Current!=health) return;
                var coordinates=File.ReadAllText(Marker("actor-pose-"+_actorTestIndex)).Split(',').Select(v=>float.Parse(v,CultureInfo.InvariantCulture)).ToArray();
                var expected=new Vector3(coordinates[0],coordinates[1],coordinates[2]);
                if(Vector3.Distance(_actorTestNpc.transform.position,expected)>0.1f) return;
                TravelCheck(true,"P2 receives host-native damage result: "+_actorTestNpc.name+" health="+health);
                TravelCheck(true,"replica converges to host actor movement: "+_actorTestNpc.name);
                TravelMark("actor-health-received-"+_actorTestIndex);_actorTestStep=5;break;
            case 5 when Marked("actor-dead-"+_actorTestIndex):
                if(_actorTestNpc!=null && _actorTestNpc.IsAlive) return;
                var count=int.Parse(File.ReadAllText(Marker("actor-death-count-"+_actorTestIndex)),CultureInfo.InvariantCulture);
                if(_world!.ActorDeathEffectsForTest<_actorTestEffects+count) return;
                TravelCheck(true,"authoritative death and selected native presentation reach P2: "+_actorTestName+" helpers="+count);
                TravelMark("actor-death-received-"+_actorTestIndex);_actorTestIndex++;_actorTestStep=1;break;
        }
        if(Marked("actor-all-complete")) {TravelMark("actor-client-complete");_actorTestStep=8;}
        if(_actorTestStep==8 && _session?.Snapshot.Connected!=true) {TravelMark("client-pass");_actorTestStep=99;}
    }
}
