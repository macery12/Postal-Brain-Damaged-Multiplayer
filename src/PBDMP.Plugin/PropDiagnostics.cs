using System.Text.Json;
using Hyperstrange.Base;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using PBDMP.Network;
using UnityEngine;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    internal PhysicalObject? PhysicalForTest(uint id)=>_physicalInstances.GetValueOrDefault(id);
}

public sealed partial class MultiplayerBehaviour
{
    internal static void EnsurePropSpawnerFixture()
    {
        if(Environment.GetEnvironmentVariable("PBDMP_PROP_TEST")!="1" || LevelController._instance==null) return;
        const string name="DIAGNOSTIC_NativePropSpawner";
        var existing=UnityEngine.Object.FindObjectsOfType<PhysicalObjectSpawner>(true).FirstOrDefault(s=>s.name==name);
        if(existing!=null) {existing.transform.SetParent(LevelController._instance.transform,true);return;}
        var source=Resources.FindObjectsOfTypeAll<PhysicalObjectSpawner>().First(s=>s._physicalObjects is {Length:>0} && s._pushExplosion!=null);
        var root=new GameObject(name);root.transform.SetParent(LevelController._instance.transform,false);
        root.transform.position=Player.Instance.FeetPosition+Vector3.right*3+Vector3.up*2;
        var fixture=root.AddComponent<PhysicalObjectSpawner>();fixture._physicalObjects=source._physicalObjects;
        fixture._pushExplosion=source._pushExplosion;fixture._amount=4;fixture._frames=2;fixture._radius=1;
    }
    private sealed record PropFixture(string Body,string Spawner);
    private int _propStep;
    private float _propAt;
    private uint _propEpoch;
    private PropFixture? _propFixture;
    private Vector3 _propInitial;
    private WorldFrame[] _propSaved=Array.Empty<WorldFrame>();
    private PhysicalObject TestProp=>_world!.ComponentForTest(_propFixture!.Body)!.Cast<PhysicalObject>();
    private PhysicalObjectSpawner TestSpawner=>_world!.ComponentForTest(_propFixture!.Spawner)!.Cast<PhysicalObjectSpawner>();
    private void RunPropTest()
    {
        if(_propStep==99) return;
        File.WriteAllText(Marker(TravelRole+"-prop-state.txt"),$"step={_propStep}, ready={_lifecycle?.NativeReady}, save={_saves?.Status}, pending={_world?.PendingPropSpawns}");
        if(_propStep==0)
        {
            _port!.text=Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!;
            if(TravelRole=="host") {Host();SharedWorldPatches.DiagnosticFreeze=true;GameManager.LoadLevel(TestLevel(),true);}
            else {_address!.text="127.0.0.1";Join();}
            _propStep=1;return;
        }
        if(_lifecycle is {NativeReady:false}) {_lifecycle.AutoContinue();return;}
        if(_lifecycle?.LocalTutorialActive==true)
        {Player.Instance.GetComponent<Hyperstrange.PBD.PlayerComponents.PlayerTutorialComponent>().OnUseDown();return;}
        if(TravelRole=="host" && _propStep==5 && _lifecycle is {WorldReady:true} life && life.Epoch>_propEpoch) life.HostPauseRequest(true);
        if(TravelRole=="host") PropHostTest();else PropClientTest();
    }
    private void CheckPropFrames(string stage)
    {
        foreach(var f in _propSaved)
        {
            var prop=f.Key.Length>0?_world!.ComponentForTest(f.Key)?.TryCast<PhysicalObject>():_world!.PhysicalForTest(f.Entity);
            TravelCheck(prop!=null,stage+" reconstructs physical identity "+f.Entity);
            var body=prop!.Rigidbody;
            var parent=body.transform.parent;
            File.AppendAllText(Marker("prop-pose-audit-"+TravelRole+".tsv"),$"{stage}\t{f.Entity}\tkey={f.Key}\tposition={body.position.ToString("R")}\texpected={new Vector3(f.Position.X,f.Position.Y,f.Position.Z).ToString("R")}\tangle={Quaternion.Angle(body.rotation,Quaternion.Euler(f.Rotation.X,f.Rotation.Y,f.Rotation.Z))}\tvelocity={body.velocity}\tparent={parent?.name}\tparentPosition={parent?.position}\tlocal={body.transform.localPosition}\tfrozen={_lifecycle?.Frozen}\tpaused={_lifecycle?.State.Paused}\tclock={Time.time}\tscale={Time.timeScale}\n");
            TravelCheck(Vector3.Distance(body.position,new(f.Position.X,f.Position.Y,f.Position.Z))<.2f &&
                Quaternion.Angle(body.rotation,Quaternion.Euler(f.Rotation.X,f.Rotation.Y,f.Rotation.Z))<2,
                stage+" preserves physical pose "+f.Entity+"; actual="+body.position+", expected="+f.Position+", angle="+Quaternion.Angle(body.rotation,Quaternion.Euler(f.Rotation.X,f.Rotation.Y,f.Rotation.Z)));
            TravelCheck(prop.gameObject.activeSelf==(f.Values![4]!=0),stage+" preserves active/tombstone state "+f.Entity);
            if(TravelRole=="host")
            {
                var moving=f.Values[3]==0 && f.Values[4]!=0;
                TravelCheck((!moving || Vector3.Distance(body.velocity,new(f.Velocity.X,f.Velocity.Y,f.Velocity.Z))<.2f) && body.isKinematic==(f.Values[6]!=0),
                    stage+" preserves moving velocity and simulation ownership "+f.Entity+"; velocity="+body.velocity+", expected="+f.Velocity+", kinematic="+body.isKinematic+"/"+f.Values[6]);
            }
            else TravelCheck(body.isKinematic,"P2 prop presentation never runs competing physics");
        }
        if(TravelRole=="host") TravelCheck(TestSpawner._spawnedObjects.Count==4 && TestSpawner._spawnedObjects.ToArray().Select(p=>p.Pointer).Distinct().Count()==4,"native spawner owns exactly four restored pool instances; count="+TestSpawner._spawnedObjects.Count+", distinct="+TestSpawner._spawnedObjects.ToArray().Select(p=>p.Pointer).Distinct().Count()+", listed="+string.Join(";",TestSpawner._spawnedObjects.ToArray().Select(p=>p.name+"/"+p.Pointer))+", restored="+string.Join(";",_propSaved.Where(f=>f.Key.Length==0).Select(f=>f.Entity+"/"+_world!.PhysicalForTest(f.Entity)?.Pointer)));
    }
    private void PropHostTest()
    {
        switch(_propStep)
        {
            case 1 when _world?.Active==true:
                File.WriteAllLines(Marker("prop-candidates.tsv"),UnityEngine.Object.FindObjectsOfType<PhysicalObjectSpawner>(true).Select(s=>"spawner\t"+WorldCatalogue.DiagnosticPath(s.transform)+"\tknown="+_world.KeyForTest(s)+"\tactive="+s.gameObject.activeInHierarchy+"\tprefabs="+s._physicalObjects.Length)
                    .Concat(_world.CatalogueForTest.SceneryKeys.Select(k=>_world.ComponentForTest(k)?.TryCast<PhysicalObject>()).Where(p=>p!=null).Select(p=>"body\t"+WorldCatalogue.DiagnosticPath(p!.transform)+"\tactive="+p.gameObject.activeInHierarchy+"\tkinematic="+p.Rigidbody.isKinematic+"\tcontacts="+p.GetComponentsInChildren<Collider>().Length)));
                File.WriteAllLines(Marker("prop-prefabs.tsv"),UnityEngine.Object.FindObjectsOfType<PhysicalObjectSpawner>(true).SelectMany(source=>source._physicalObjects.ToArray().Select(prefab=>
                    WorldCatalogue.DiagnosticPath(source.transform)+"\t"+prefab.name+"\tplayerCollision="+prefab.GetComponentsInChildren<Collider>(true).Any(c=>!c.isTrigger && !Physics.GetIgnoreLayerCollision(c.gameObject.layer,Player.Instance.Collider.gameObject.layer))+
                    "\tcomponents="+string.Join(',',prefab.GetComponentsInChildren<MonoBehaviour>(true).Select(c=>c.GetIl2CppType().Name)))));
                QuietNpcs();
                var body=_world.CatalogueForTest.SceneryKeys.Select(k=>_world.ComponentForTest(k)?.TryCast<PhysicalObject>()).First(p=>p!=null && p.gameObject.activeInHierarchy && p.GetComponentsInChildren<Collider>().Any(c=>c.enabled && !c.isTrigger));
                var spawner=_world.CatalogueForTest.SpawnerKeys.Select(k=>_world.ComponentForTest(k)!.Cast<PhysicalObjectSpawner>()).First(s=>s.name=="DIAGNOSTIC_NativePropSpawner" && s._physicalObjects.Length>0);
                _propFixture=new(_world.KeyForTest(body!)!,_world.KeyForTest(spawner)!);body!.Rigidbody.isKinematic=false;body.Rigidbody.WakeUp();_propInitial=body.Rigidbody.position;
                File.WriteAllText(Marker("prop-fixture.json"),JsonSerializer.Serialize(_propFixture));TravelMark("host-ready");_propStep=2;break;
            case 2 when _lifecycle!.State.Released && Marked("prop-client-ready") && !_saves!.Busy:
                GameManager.DisableMenu();TravelMark("prop-push");_propAt=Time.unscaledTime+8;_propStep=3;break;
            case 3 when Marked("prop-client-pushed"):
                if(Vector3.Distance(TestProp.Rigidbody.position,_propInitial)<.05f && Time.unscaledTime<_propAt) return;
                TravelCheck(Vector3.Distance(TestProp.Rigidbody.position,_propInitial)>.05f,"P2 native Push moves P1's authored physical prop");
                TravelCheck(PlayerSessionState.FindNearby(Player.Instance,Player.Instance.FeetPosition+Vector3.right*2,out var bodyPoint),"moving-body restore fixture has safe space beside P1");
                TestProp.StopOptimizing();TestProp.gameObject.SetActive(true);TestProp.StopAllCoroutines();TestProp._isOutOfBounds=false;
                TestProp.Rigidbody.position=bodyPoint+Vector3.up;
                TestProp.Rigidbody.rotation=Quaternion.identity;
                TestProp.Rigidbody.velocity=new(3,2,1);TestProp.Rigidbody.angularVelocity=new(.1f,.2f,.3f);TestProp.Rigidbody.WakeUp();
                var source=TestSpawner;source.StopAllCoroutines();source.Load(new());source._amount=4;source._frames=2;source.SpawnPhysicalObjects();
                TravelCheck(_world!.PendingPropSpawns,"capture observes the actual native spawn coroutine as pending");
                _saves!.RequestSave("manual-props","Authored body and native pooled props");_propStep=4;break;
            case 4 when !_saves!.Busy:
                var save=_saves.Store.Read("manual-props").Save;
                _propSaved=save.World.Where(f=>f.Kind==WorldKind.Scenery && f.Flags==4 && (f.Key==_propFixture!.Body || f.Key.Length==0 && f.Data is {Length:1} && f.Data[0]==_propFixture.Spawner)).ToArray();
                TravelCheck(_propSaved.Count(f=>f.Key.Length==0)==4 && !_world!.PendingPropSpawns,"save waits for the complete native batch and includes all four runtime props");
                var savedBody=_propSaved.Last(f=>f.Key==_propFixture!.Body);
                TravelCheck(savedBody.Values![4]==1 && savedBody.Velocity.X!=0,"stale-transform regression captures an active moving body rather than an inactive tombstone");
                File.WriteAllText(Marker("prop-saved.json"),JsonSerializer.Serialize(_propSaved));
                // A moving interpolated body can have an older display transform.
                // Keep that duplicate pose deliberately distinct: the durable
                // rigidbody, not this generic presentation record, owns physics.
                var transformKey=WorldCatalogue.TransformKey(TestProp.transform);
                var presentation=Array.FindLastIndex(save.World,f=>f.Kind==WorldKind.Door && (f.Flags&2)==0 && f.Key==transformKey);
                TravelCheck(presentation>=0,"fixture has a generic transform overlapping its authoritative physical body");
                var display=save.World[presentation];
                save.World[presentation]=display with {Position=display.Position with {X=display.Position.X+3}};
                _saves.Store.Write(save);
                TestProp.Rigidbody.position+=Vector3.right*20;
                TestSpawner.Load(new());_propEpoch=_lifecycle!.Epoch;_saves.RequestLoad("manual-props");_propStep=5;break;
            case 5 when !_saves!.Busy && _lifecycle!.State.Released && _lifecycle.Epoch>_propEpoch:
                GameManager.PauseGame();_propStep=6;_propAt=Time.unscaledTime+.5f;break;
            case 6 when Time.unscaledTime>=_propAt:
                CheckPropFrames("host rollback");TravelMark("prop-restored");_propStep=7;break;
            case 7 when Marked("prop-client-restored"):
                // Exercise a complete pool lifetime, then reuse the source pool.
                TestSpawner.Load(new());_propAt=Time.unscaledTime+1;_propStep=8;break;
            case 8 when Time.unscaledTime>=_propAt:
                TravelMark("prop-pooled");_propStep=9;break;
            case 9 when Marked("prop-client-pooled"):
                TestSpawner._amount=4;TestSpawner._frames=2;TestSpawner.SpawnPhysicalObjects();_propAt=Time.unscaledTime+1;_propStep=10;break;
            case 10 when Time.unscaledTime>=_propAt && !_world!.PendingPropSpawns:
                if(!TryDiagnosticWorldCapture(out var current)) return;
                var reused=current.Where(f=>f.Kind==WorldKind.Scenery && f.Flags==4 && f.Key.Length==0 && f.Data is {Length:1} && f.Data[0]==_propFixture!.Spawner).ToArray();
                TravelCheck(reused.Length==4 && reused.All(f=>_propSaved.All(old=>old.Entity!=f.Entity)),"reused pool instances receive new durable lifetime IDs");
                _propSaved=reused;File.WriteAllText(Marker("prop-current.json"),JsonSerializer.Serialize(reused));TravelMark("prop-current");_propStep=11;break;
            case 11 when Marked("prop-client-left") && !_session!.Snapshot.Connected:
                TravelMark("prop-rejoin");_propStep=12;break;
            case 12 when _lifecycle!.State.Released && Marked("prop-client-current"):
                TravelCheck(_session!.Snapshot.Connected,"pooled-prop current-world rejoin remains connected");EndFrozenDiagnosticSession();_propStep=13;break;
            case 13 when Marked("client-pass"):
                TravelMark("host-pass");_propStep=99;break;
        }
    }
    private void PropClientTest()
    {
        switch(_propStep)
        {
            case 1 when _world?.Active==true && _lifecycle!.State.Released:
                _propFixture=JsonSerializer.Deserialize<PropFixture>(File.ReadAllText(Marker("prop-fixture.json")))!;
                TravelMark("prop-client-ready");_propStep=2;break;
            case 2 when Marked("prop-push"):
                Player.Instance.Teleport(TestProp.transform.position+Vector3.back,Vector3.zero,Vector3.zero);_propAt=Time.unscaledTime+.5f;_propStep=3;break;
            case 3 when Time.unscaledTime>=_propAt && !_lifecycle!.Frozen:
                TestProp.Push(Vector3.right*8,true);TravelMark("prop-client-pushed");_propStep=4;break;
            case 4 when Marked("prop-restored") && !_saves!.Busy && _lifecycle!.State.Released:
                _propSaved=JsonSerializer.Deserialize<WorldFrame[]>(File.ReadAllText(Marker("prop-saved.json")))!;
                CheckPropFrames("P2 rollback");TravelMark("prop-client-restored");_propStep=5;break;
            case 5 when Marked("prop-pooled"):
                TravelCheck(_propSaved.Where(f=>f.Key.Length==0).All(f=>_world!.PhysicalForTest(f.Entity)==null),"native pool returns remove P2 replicas through tombstones");
                TravelMark("prop-client-pooled");_propStep=6;break;
            case 6 when Marked("prop-current"):
                ResetSession();TravelMark("prop-client-left");_propStep=7;break;
            case 7 when Marked("prop-rejoin") && SessionLifecycle.Current==null && !SessionManager.SessionActive:
                _address!.text="127.0.0.1";Join();_propStep=8;break;
            case 8 when _world?.Active==true && _lifecycle!.State.Released && _lifecycle.PersonalReady:
                _propSaved=JsonSerializer.Deserialize<WorldFrame[]>(File.ReadAllText(Marker("prop-current.json")))!;
                CheckPropFrames("P2 current-world rejoin");TravelMark("prop-client-current");_propStep=9;break;
            case 9 when SessionLifecycle.Current==null && !SessionManager.SessionActive:
                TravelMark("client-pass");_propStep=99;break;
        }
    }
}
