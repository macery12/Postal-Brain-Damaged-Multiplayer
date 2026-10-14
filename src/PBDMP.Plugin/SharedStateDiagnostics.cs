using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Hyperstrange.Base;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using Hyperstrange.PBD.NpcComponents;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PBDMP.Plugin;

public sealed partial class MultiplayerBehaviour
{
    private sealed record StateFixture(string Key,string Type,string[] Data,float[] Motion);
    private int _stateTestStep;
    private float _stateTestAt;
    private LastBossArena? _stateBoss;
    private SpawnPointTriggerListener? _stateSpawnListener;
    private PlayerTrigger? _stateTrigger;
    private SpawnPoint? _stateSpawnPoint;
    private NpcGroup? _stateGroup;
    private int _stateGroupWipes;
    private Il2CppSystem.Action<NpcGroup>? _stateGroupWipeHandler;
    private Npc[] _stateKillReplicas=Array.Empty<Npc>();
    private void RunSharedStateTest()
    {
        if(_stateTestStep==99) return;
        File.WriteAllText(Marker(TravelRole+"-state.txt"),$"state={_stateTestStep}, ready={_lifecycle?.NativeReady}, world={_lifecycle?.WorldReady}, frozen={_lifecycle?.Frozen}, fx={_world?.FxEventsForTest}");
        if(_stateTestStep==0)
        {
            _port!.text=Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!;
            if(TravelRole=="host") {Host();GameManager.LoadLevel(TestLevel(),true);} else {_address!.text="127.0.0.1";Join();}
            _stateTestStep=1;return;
        }
        if(_lifecycle is {NativeReady:false}) {_lifecycle.AutoContinue();return;}
        if(_stateTestStep<3 && _world?.Active!=true) return;
        if(TravelRole=="host") StateTestHost();else StateTestClient();
    }
    private void StateTestHost()
    {
        switch(_stateTestStep)
        {
            case 1:
                QuietNpcs();SharedWorldPatches.DiagnosticFreeze=true;TimeManager.SetApplicationTimeScale(1);
                foreach(var group in _world!.CatalogueForTest.DoorKeys.Select(k=>_world.ComponentForTest(k)!).GroupBy(c=>c.GetIl2CppType().Name))
                {
                    var component=group.First();
                    if(component.TryCast<SlidingDoors>() is {} sliding) {sliding._useTrigger=false;sliding.UnlockAndOpen();}
                    else if(component.TryCast<ElevatorDoors>() is {} elevator) elevator.MoveDoors();
                    else if(component.TryCast<ToiletDoor>() is {} toilet) toilet.Push(Vector3.right*3,false);
                    else if(component.TryCast<Vault>() is {} vault) {vault.PlayerInteractSuccess(Player.Instance);vault.PlayerInteractSuccess(Player.Instance);}
                    else if(component.TryCast<Mover>() is {} mover) mover.Move();
                    else if(component.TryCast<Rotater>() is {} rotater) rotater.Rotate();
                    else if(component.TryCast<Toggler>() is {} toggler) toggler.Toggle();
                    else continue;
                    File.AppendAllText(Marker("native-state-actions.txt"),component.GetIl2CppType().Name+" "+component.name+"\n");
                }
                _stateBoss=Object.FindObjectsOfType<LastBossArena>().FirstOrDefault();
                if(_stateBoss!=null)
                {
                    _stateBoss._mechSpawnPoint.Spawn();
                    var mech=_stateBoss._mechSpawnPoint.SpawnedNpc;
                    TravelCheck(mech!=null,"native boss spawnpoint supplies the actor for its phase callback");
                    _stateBoss.EnableContraptions(true);_stateBoss.OnMechDeath(mech!);
                    TravelCheck(_stateBoss._mechKilled,"native boss death callback advances the host phase");
                }
                _stateSpawnListener=Object.FindObjectsOfType<SpawnPointTriggerListener>(true).FirstOrDefault(l=>l._playerTrigger!=null && l._playerTrigger.gameObject.activeInHierarchy && l._spawnPoint?.NpcId!=null && Vector3.Distance(l._playerTrigger.transform.position,Player.Instance.CenterOfMass)>20);
                if(_stateSpawnListener!=null)
                {
                    _stateSpawnListener._spawnPoint.Despawn();_stateSpawnListener._response=SpawnPointListenerResponseEnum.SPAWN;_stateSpawnListener._delay=0;
                    _stateTrigger=_stateSpawnListener._playerTrigger;_stateSpawnPoint=_stateSpawnListener._spawnPoint;
                }
                else foreach(var listener in Object.FindObjectsOfType<PlayerTriggerListener>(true))
                {
                    var trigger=listener.GetComponent<PlayerTrigger>();var events=listener._onTriggerEvents;
                    if(trigger==null || !trigger.gameObject.activeInHierarchy || !trigger.GetComponents<Collider>().Any(c=>c.enabled) || events==null || Vector3.Distance(trigger.transform.position,Player.Instance.CenterOfMass)<20) continue;
                    for(var i=0;i<events.GetPersistentEventCount();i++)
                    {
                        var group=events.GetPersistentTarget(i)?.TryCast<NpcGroup>();
                        if(group?._spawnPoints==null || group._spawnPoints.Length==0 || !group._spawnPoints.Any(p=>p?.NpcId!=null)) continue;
                        _stateGroup=group;_stateTrigger=trigger;_stateSpawnPoint=group._spawnPoints.First(p=>p?.NpcId!=null);
                        File.WriteAllText(Marker("encounter-fixture.txt"),$"listener={listener.name} active={listener.enabled} trigger={trigger.name} group={group.name} event={events.GetPersistentMethodName(i)} points={group._spawnPoints.Length}");
                        foreach(var point in group._spawnPoints) if(point!=null) {point.Despawn();point._spawnedThisPlay=false;}
                        group._groupActivated=false;break;
                    }
                    if(_stateTrigger!=null) break;
                }
                if(_stateTrigger!=null)
                {
                    _stateTrigger._triggeredOnEnter=false;_stateTrigger._triggeredOnExit=false;_stateTrigger.Enable();
                    File.WriteAllText(Marker("encounter-trigger"),_world.KeyForTest(_stateTrigger)!);
                    File.WriteAllText(Marker("encounter-spawnpoint"),_world.KeyForTest(_stateSpawnPoint!)!);
                }
                if(LevelManager.CurrentLevel.name=="LEVEL_AD_Prison") TravelCheck(_stateTrigger!=null,"Prison fixture includes an authored enemy-group trigger for P2 activation");
                _stateTestAt=Time.unscaledTime+8f;_stateTestStep=2;break;
            case 2 when Time.unscaledTime>=_stateTestAt:
                TimeManager.SetApplicationTimeScale(0);
                _stateTestAt=Time.unscaledTime+0.5f;_stateTestStep=21;break;
            case 21 when Time.unscaledTime>=_stateTestAt:
                var pistol=Resources.FindObjectsOfTypeAll<WeaponId>().First(w=>w.name=="WEAPON_Pistol");
                pistol.PlayerWeaponModel.Cast<PlayerWeaponModelPistol>().PrimaryFire.ProjectileModel.CreateInstance(Player.Instance.CenterOfMass+Vector3.up*100f,Quaternion.LookRotation(Vector3.up),Vector3.up,Player.Instance.Cast<IAiActor>(),pistol);
                File.WriteAllText(Marker("state-fixture.json"),JsonSerializer.Serialize(CaptureStateFixture()));
                TravelMark("host-ready");_stateTestStep=3;break;
            case 3 when Marked("client-state-shared"):
                TimeManager.SetApplicationTimeScale(1);SendFixtureWeaponFx();_stateTestStep=10;break;
            case 10 when Marked("client-fx-shared") && _world!.FxEventsForTest>=int.Parse(File.ReadAllText(Marker("fx-count"))):
                TravelCheck(_world.ChargeLoopsForTest==0,"P1 renders every native remote firing/charging FX source and releases charge loops");
                TravelMark("host-fx-shared");_stateTestStep=11;break;
            case 11 when Marked("client-fx-complete"):
                if(_stateTrigger==null) {ResetSession();SharedWorldPatches.DiagnosticFreeze=false;_stateTestStep=6;break;}
                TimeManager.SetApplicationTimeScale(1);TravelMark("host-encounter-ready");_stateTestStep=4;break;
            case 4 when Marked("client-encounter-entered") && _stateSpawnPoint!.SpawnedNpc is {} npc && npc.gameObject.activeInHierarchy:
                TravelCheck(true,"P2 enters an authored encounter trigger and P1 runs its native spawn callback");
                if(_stateGroup!=null) TravelCheck(_stateGroup._groupActivated,"P2's trigger activates the native P1 enemy group");
                TravelMark("host-encounter-spawned");_stateTestStep=5;break;
            case 5 when Marked("client-encounter-shared"):
                if(_stateGroup!=null)
                {
                    _stateGroupWipeHandler=(Il2CppSystem.Action<NpcGroup>)(Action<NpcGroup>)(_=>_stateGroupWipes++);
                    _stateGroup.OnGroupWiped=Il2CppSystem.Delegate.Combine(_stateGroup.OnGroupWiped,_stateGroupWipeHandler).Cast<Il2CppSystem.Action<NpcGroup>>();
                    foreach(var point in _stateGroup._spawnPoints) if(point.SpawnedNpc!=null) point.SpawnedNpc.HitPoints.SetCurrentSilent(1);
                    File.WriteAllLines(Marker("encounter-kill-keys"),_stateGroup._spawnPoints.Select(p=>_world!.KeyForTest(p)!));
                    _stateTestStep=12;break;
                }
                ResetSession();SharedWorldPatches.DiagnosticFreeze=false;_stateTestStep=6;break;
            case 12 when Marked("client-encounter-killed") && _stateGroupWipes==1 && _stateGroup!.AliveNpcs==0:
                TravelCheck(true,"P2's attacks wipe the native P1 enemy group and invoke its progression callback exactly once");
                _stateGroup.OnGroupWiped=Il2CppSystem.Delegate.Remove(_stateGroup.OnGroupWiped,_stateGroupWipeHandler)?.Cast<Il2CppSystem.Action<NpcGroup>>();
                TravelMark("host-encounter-wiped");_stateTestStep=13;break;
            case 13 when Marked("client-encounter-wiped"):
                ResetSession();SharedWorldPatches.DiagnosticFreeze=false;_stateTestStep=6;break;
            case 6 when Marked("client-pass"):
                TravelMark("host-pass");_stateTestStep=99;break;
        }
    }
    private void StateTestClient()
    {
        switch(_stateTestStep)
        {
            case 1 when _lifecycle!.WorldReady && _lifecycle.State.Released:
                _stateTestAt=Time.unscaledTime+1f;_stateTestStep=2;break;
            case 2 when Time.unscaledTime>=_stateTestAt:
                var expected=JsonSerializer.Deserialize<StateFixture[]>(File.ReadAllText(Marker("state-fixture.json")))!;
                var captured=CaptureStateFixture();File.WriteAllText(Marker("client-state-fixture.json"),JsonSerializer.Serialize(captured));
                var actual=captured.ToDictionary(s=>s.Type+"/"+s.Key);
                foreach(var state in expected)
                {
                    TravelCheck(actual.TryGetValue(state.Type+"/"+state.Key,out var peer),"late join resolves "+state.Type+" identity "+state.Key[..8]);
                    TravelCheck(state.Data.Length==peer!.Data.Length && state.Data.Zip(peer.Data).All(p=>EquivalentNativeText(p.First,p.Second)),"late join restores native "+state.Type+" state "+state.Key[..8]);
                    if(state.Motion.Length!=0)
                    {
                        var a=state.Motion;var b=peer.Motion;
                        TravelCheck(Vector3.Distance(new(a[0],a[1],a[2]),new(b[0],b[1],b[2]))<0.02f && Quaternion.Angle(new(a[3],a[4],a[5],a[6]),new(b[3],b[4],b[5],b[6]))<0.1f,"late join restores current motion/collision pose "+state.Key[..8]+" ("+string.Join(",",a)+" / "+string.Join(",",b)+")");
                    }
                }
                var boss=Object.FindObjectsOfType<LastBossArena>().FirstOrDefault();
                if(boss!=null) TravelCheck(boss._mechKilled && !boss.enabled,"late join restores boss phase without running replica encounter authority");
                TravelCheck(_world!.ProjectilePresentationsForTest==1 && _world.ProjectileSoundsForTest==0,"late join restores the active projectile without replaying its firing sound");
                TravelMark("client-state-shared");_stateTestStep=3;break;
            case 3 when Marked("host-encounter-ready") && !_lifecycle!.Frozen:
                var trigger=_world!.ComponentForTest(File.ReadAllText(Marker("encounter-trigger")))!.Cast<PlayerTrigger>();
                var bounds=trigger.GetComponents<Collider>().First(c=>c.enabled).bounds;
                Player.Instance.Teleport(bounds.center-Vector3.up*0.9f,Vector3.zero,Vector3.zero);
                File.WriteAllText(Marker("client-trigger-pose.txt"),$"bounds={bounds.center}/{bounds.size}, feet={Player.Instance.FeetPosition}, center={Player.Instance.CenterOfMass}, collider={Player.Instance.Collider.bounds.center}/{Player.Instance.Collider.bounds.size}");
                TravelMark("client-encounter-entered");_stateTestStep=4;break;
            case 3 when !_lifecycle!.Frozen && !Marked("client-fx-complete") && File.Exists(Marker("fx-count")) && _world!.FxEventsForTest>=int.Parse(File.ReadAllText(Marker("fx-count"))):
                TravelCheck(_world.ChargeLoopsForTest==0,"P2 renders every native remote firing/charging FX source and releases charge loops");
                SendFixtureWeaponFx();TravelMark("client-fx-shared");_stateTestStep=10;break;
            case 10 when Marked("host-fx-shared"):
                TravelMark("client-fx-complete");_stateTestStep=3;break;
            case 4 when Marked("host-encounter-spawned"):
                var spawnKey=File.ReadAllText(Marker("encounter-spawnpoint"));
                var replica=_world!.EnemyForSpawnForTest(spawnKey);
                if(replica==null) return;
                TravelCheck(replica.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true).All(a=>!a.enabled),"P2 receives the encounter-spawned enemy with navigation authority suppressed");
                TravelMark("client-encounter-shared");_stateTestStep=5;break;
            case 5 when File.Exists(Marker("encounter-kill-keys")):
                var keys=File.ReadAllLines(Marker("encounter-kill-keys"));
                var victims=keys.Select(k=>_world!.EnemyForSpawnForTest(k)).ToArray();
                if(victims.Any(n=>n==null || n.HitPoints.Current!=1)) return;
                _stateKillReplicas=victims.Select(n=>n!).ToArray();
                foreach(var victim in _stateKillReplicas) victim.Hit(new HitData{dealer=Player.Instance.Cast<IAiActor>(),damage=100,weakSpotMultiplier=1,position=victim.CenterOfMass,direction=Vector3.forward});
                TravelMark("client-encounter-killed");_stateTestStep=11;break;
            case 11 when Marked("host-encounter-wiped") && _stateKillReplicas.All(n=>n==null || !n.IsAlive):
                TravelCheck(true,"P2 receives all authoritative encounter kills without independently advancing its replica group");
                TravelMark("client-encounter-wiped");_stateTestStep=12;break;
            case 3 when _session==null && SessionLifecycle.Current==null && !SessionManager.SessionActive:
            case 5 when _session==null && SessionLifecycle.Current==null && !SessionManager.SessionActive:
            case 12 when _session==null && SessionLifecycle.Current==null && !SessionManager.SessionActive:
                TravelMark("client-pass");_stateTestStep=99;break;
        }
    }
    private void SendFixtureWeaponFx()
    {
        var effects=_world!.EffectsForTest.ToArray();File.WriteAllText(Marker("fx-count"),(effects.Length*5).ToString());
        foreach(var fx in effects) foreach(var action in new uint[]{0,1,2,4,3}) _world.WeaponFx(fx,action);
    }
    [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
    private StateFixture[] CaptureStateFixture()
    {
        var catalogue=_world!.CatalogueForTest;var state=new List<StateFixture>();
        foreach(var key in catalogue.DoorKeys) state.Add(new(key,catalogue.Components[key].GetIl2CppType().Name,WorldCatalogue.Save(catalogue.Components[key]),Array.Empty<float>()));
        foreach(var key in catalogue.EncounterKeys)
        {
            var actual=NativeAssetGraph.Actual(catalogue.Components[key]);var data=new List<string>();
            foreach(var property in actual.GetType().GetProperties(BindingFlags.Public|BindingFlags.Instance).Where(p=>p.DeclaringType!=typeof(BaseBehaviour) && p.Name.StartsWith('_') && p.CanWrite && p.GetIndexParameters().Length==0 && (p.PropertyType==typeof(bool) || p.PropertyType==typeof(int) || p.PropertyType==typeof(float))).OrderBy(p=>p.Name,StringComparer.Ordinal))
            {data.Add(property.Name);data.Add(Convert.ToString(property.GetValue(actual),CultureInfo.InvariantCulture)!);}
            state.Add(new(key,"encounter "+actual.GetType().Name,data.ToArray(),Array.Empty<float>()));
        }
        foreach(var pair in catalogue.DoorMotion.Concat(catalogue.EncounterMotion).GroupBy(p=>p.Key).Select(g=>g.First()))
        {var t=pair.Value;var p=t.localPosition;var q=t.localRotation;state.Add(new(pair.Key,"transform",new[]{t.gameObject.activeSelf.ToString()},new[]{p.x,p.y,p.z,q.x,q.y,q.z,q.w}));}
        return state.ToArray();
    }
    private static bool EquivalentNativeText(string a,string b)
    {
        if(a==b) return true;
        if(double.TryParse(a,NumberStyles.Float,CultureInfo.InvariantCulture,out var x) && double.TryParse(b,NumberStyles.Float,CultureInfo.InvariantCulture,out var y)) return Math.Abs(x-y)<0.02;
        if(!a.StartsWith('{') || !b.StartsWith('{')) return false;
        using var left=JsonDocument.Parse(a);using var right=JsonDocument.Parse(b);
        var expected=left.RootElement.EnumerateObject().ToArray();var actual=right.RootElement.EnumerateObject().ToArray();
        return expected.Length==actual.Length && expected.All(p=>right.RootElement.TryGetProperty(p.Name,out var value) && EquivalentNativeText(p.Value.GetRawText(),value.GetRawText()));
    }
}
