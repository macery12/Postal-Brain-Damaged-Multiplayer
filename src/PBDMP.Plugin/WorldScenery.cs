using Hyperstrange.Base;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using PBDMP.Network;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    private readonly Dictionary<string,WorldFrame> _sceneryState=new();
    private readonly Dictionary<uint,PhysicalObject> _physicalInstances=new();
    private readonly Dictionary<IntPtr,(uint Id,int Cycle)> _physicalIds=new();
    private readonly HashSet<uint> _ownedPhysical=new();
    private readonly Dictionary<uint,string> _physicalSources=new();
    private readonly Dictionary<uint,string> _physicalAssets=new();
    private readonly Dictionary<Rigidbody,(bool Kinematic,bool Collisions)> _physicalBodies=new();
    private readonly Dictionary<string,float> _sceneryRequestAt=new();
    private float _spawnerScanAt;
    internal bool HasPendingInteractions=>_doorRequests.Count!=0 || PendingPropSpawns;
    internal bool OwnsScenery(Component c)=>_catalogue?.Key(c) is {} key && _catalogue.SceneryKeys.Contains(key);
    internal bool SharedPhysics(PhysicalObject c)=>OwnsScenery(c) || _physicalIds.ContainsKey(c.Pointer);
    private IEnumerable<int> CaptureScenery(bool complete)
    {
        if(complete || Time.unscaledTime>=_spawnerScanAt)
        {
            _spawnerScanAt=Time.unscaledTime+.5f;
            foreach(var key in _catalogue!.SpawnerKeys)
            {
                var spawner=_catalogue.Components[key].TryCast<PhysicalObjectSpawner>();if(spawner==null) continue;
                foreach(var prop in spawner._spawnedObjects)
                {
                    if(prop==null || !prop.gameObject.activeInHierarchy) continue;
                    if(_physicalIds.TryGetValue(prop.Pointer,out var old) && old.Cycle==prop.CycleIndex) continue;
                    if(old.Id!=0 && _sceneryState.TryGetValue("runtime:"+old.Id,out var prior)) Send(prior with {Values=new[]{0f,0,0,1,0,1,0}});
                    if(old.Id!=0) {_physicalInstances.Remove(old.Id);_sceneryState.Remove("runtime:"+old.Id);_physicalSources.Remove(old.Id);_physicalAssets.Remove(old.Id);}
                    var id=++_nextId;_physicalIds[prop.Pointer]=(id,prop.CycleIndex);_physicalInstances[id]=prop;_physicalSources[id]=key;_physicalAssets[id]=_catalogue.PhysicalPrefabKey(prop);
                }
            }
        }
        foreach(var key in _catalogue!.SceneryKeys)
        {
            var c=_catalogue.Components[key];if(c==null) {yield return 0;continue;}
            WorldFrame? frame=null;
            if(c.TryCast<GlassObject>() is {} glass)
                frame=new(WorldKind.Scenery,_epoch,1,Id(key),Key:key,Flags:1,Values:new[]{glass._hit?1f:0,glass._disabled?1f:0});
            else if(c.TryCast<BurnableObject>() is {} burn)
                frame=new(WorldKind.Scenery,_epoch,1,Id(key),Key:key,Flags:2,Value:burn._hitPoints.Current,Value2:burn._hitPoints.Max,Values:new[]{burn._isBurning?1f:0,burn._burned?1f:0});
            else if(c.TryCast<ExplosiveBarrel>() is {} barrel)
                frame=BarrelState(key,barrel);
            else if(c.TryCast<PhysicalObject>() is {} prop) frame=PhysicalState(prop,Id(key),key,"");
            if(frame!=null) PublishScenery(key,frame,complete);
            yield return 0;
        }
        foreach(var step in CaptureFragments(complete)) yield return step;
        foreach(var pair in _physicalInstances.ToArray())
        {
            var p=pair.Value;
            if(p!=null) PublishScenery("runtime:"+pair.Key,PhysicalState(p,pair.Key,"",_physicalAssets.GetValueOrDefault(pair.Key)??_catalogue.PhysicalPrefabKey(p)),complete);
            if(p==null || !p.gameObject.activeSelf)
            {
                _physicalInstances.Remove(pair.Key);_sceneryState.Remove("runtime:"+pair.Key);_physicalSources.Remove(pair.Key);_physicalAssets.Remove(pair.Key);
                if(p!=null && _physicalIds.GetValueOrDefault(p.Pointer).Id==pair.Key) _physicalIds.Remove(p.Pointer);
            }
            yield return 0;
        }
    }
    private WorldFrame PhysicalState(PhysicalObject p,uint id,string key,string asset)
    {
        var body=p.Rigidbody;
        return new(WorldKind.Scenery,_epoch,1,id,Revision:(uint)Math.Max(0,p.CycleIndex),Key:key,Asset:asset,Flags:4,
            Position:Net(body.position),Rotation:Net(body.rotation.eulerAngles),Velocity:Net(body.velocity),
            Values:new[]{body.angularVelocity.x,body.angularVelocity.y,body.angularVelocity.z,body.IsSleeping()?1f:0,
                p.gameObject.activeSelf?1f:0,p._isOutOfBounds?1f:0,body.isKinematic?1f:0},Data:key.Length==0?new[]{_physicalSources.GetValueOrDefault(id)??""}:null);
    }
    private void PublishScenery(string identity,WorldFrame frame,bool complete)
    {
        if(complete || !_sceneryState.TryGetValue(identity,out var old) || old.Position!=frame.Position || old.Rotation!=frame.Rotation ||
            old.Velocity!=frame.Velocity || old.Value!=frame.Value || old.Value2!=frame.Value2 ||
            !(old.Values??Array.Empty<float>()).SequenceEqual(frame.Values??Array.Empty<float>()))
        {Send(frame);_sceneryState[identity]=frame;}
    }
    private void ApplyScenery(WorldFrame f,bool hostRestore=false)
    {
        if(f.Flags==5) {ApplyFragment(f,hostRestore);return;}
        if(f.Flags==4) {ApplyPhysical(f,hostRestore);return;}
        if(!_catalogue!.SceneryKeys.Contains(f.Key) || f.Values is not {} v) throw new InvalidDataException("Unknown scenery state.");
        var c=_catalogue.Components[f.Key];if(c==null) return;
        if(f.Flags==1 && v.Length==2 && c.TryCast<GlassObject>() is {} glass)
        {
            if(v.Any(n=>n is not (0 or 1))) throw new InvalidDataException("Invalid glass state.");
            glass._hit=v[0]!=0;glass._disabled=v[1]!=0;
            var parts=_catalogue.GlassDefaults[f.Key];
            foreach(var renderer in parts.Renderers) if(renderer.Component!=null) renderer.Component.enabled=!glass._hit && renderer.Enabled;
            foreach(var collider in parts.Colliders) if(collider.Component!=null) collider.Component.enabled=!glass._hit && collider.Enabled;
        }
        else if(f.Flags==2 && v.Length==2 && c.TryCast<BurnableObject>() is {} burn)
        {
            // Native HitPoints.Subtract retains below-zero overkill. The wire
            // already bounds finite values; preserve that native state exactly.
            if(f.Value2<0 || f.Value>f.Value2 || v.Any(n=>n is not (0 or 1))) throw new InvalidDataException("Invalid burn state.");
            var hiddenChanged=burn._burned!=(v[1]!=0);
            var changed=burn._isBurning!=(v[0]!=0);burn._hitPoints.SetMax(f.Value2);burn._hitPoints.SetCurrentSilent(f.Value);
            burn._burned=v[1]!=0;
            if(changed) {if(v[0]!=0 && !burn._burned) burn.FlameStart();else burn.FlameEnd();}
            if(hiddenChanged || hostRestore) Extension_GameObject.Hide(burn.gameObject,burn._burned);
            SetBurnPresentation(burn);
        }
        else if(f.Flags==3 && v.Length==8 && c.TryCast<ExplosiveBarrel>() is {} barrel)
        {
            if(f.Value2<0 || f.Value>f.Value2 || v[0] is not (0 or 1) || v[1] is not (0 or 1) || v[2]<0 || v[2]>60 || v[3]<0 || v[3]>1000000 || v[4]<0 || v[4]>2047 || v[4]!=Mathf.Floor(v[4]))
                throw new InvalidDataException($"Invalid barrel state: health={f.Value}/{f.Value2}, values=[{string.Join(',',v)}].");
            var hiddenChanged=barrel._exploded!=(v[0]!=0);
            barrel._health.SetMax(f.Value2);barrel._health.SetCurrentSilent(f.Value);barrel._exploded=v[0]!=0;
            // Native explosion/burn hides renderers and colliders while keeping
            // the object alive for its native lifecycle; root activation is
            // separately replicated by the authored motion/surface adapters.
            if(hiddenChanged || hostRestore) Extension_GameObject.Hide(barrel.gameObject,barrel._exploded);
            if(hostRestore) RestoreBarrelQueue(barrel,f);else barrel._explosionQueued=v[1]!=0;
        }
        else throw new InvalidDataException("Scenery adapter/type mismatch.");
    }
    private void ApplyPhysical(WorldFrame f,bool hostRestore)
    {
        if(f.Values is not {Length:7} v || v.Skip(3).Any(n=>n is not (0 or 1))) throw new InvalidDataException("Invalid physical prop state.");
        PhysicalObject? p=null;
        if(f.Key.Length==0 && v[4]==0)
        {
            if(_physicalInstances.Remove(f.Entity,out var removed) && removed!=null)
            {
                _physicalIds.Remove(removed.Pointer);_physicalBodies.Remove(removed.Rigidbody);
                if(_ownedPhysical.Remove(f.Entity)) PoolManager.ReturnToPool(removed.Cast<Hyperstrange.Base.IPoolable>());
                _physicalSources.Remove(f.Entity);_physicalAssets.Remove(f.Entity);
            }
            return;
        }
        if(f.Key.Length>0)
        {
            if(!_catalogue!.SceneryKeys.Contains(f.Key)) throw new InvalidDataException("Unknown authored physical prop.");
            p=_catalogue.Components[f.Key].TryCast<PhysicalObject>();
        }
        else if(!_physicalInstances.TryGetValue(f.Entity,out p) || p==null)
        {
            if(!_catalogue!.PhysicalPrefabs.TryGetValue(f.Asset,out var prefab)) throw new InvalidDataException("Missing physical prop prefab.");
            if(f.Data is not {Length:1} source || !_catalogue.SpawnerKeys.Contains(source[0])) throw new InvalidDataException("Unknown physical prop source spawner.");
            p=PoolManager.SpawnPoolable(prefab.Cast<Hyperstrange.Base.IPoolable>(),Unity(f.Position),Quaternion.Euler(Unity(f.Rotation))).Cast<PhysicalObject>();
            p.StopAllCoroutines();
            _physicalInstances[f.Entity]=p;_physicalIds[p.Pointer]=(f.Entity,p.CycleIndex);_ownedPhysical.Add(f.Entity);
            _physicalSources[f.Entity]=source[0];_physicalAssets[f.Entity]=f.Asset;
            if(hostRestore) _catalogue.Components[source[0]].Cast<PhysicalObjectSpawner>()._spawnedObjects.Add(p);
        }
        if(p==null) throw new InvalidDataException("Invalid physical prop target.");
        p.gameObject.SetActive(v[4]!=0);
        // OnEnable can schedule a deferred native reset. It must finish before
        // the authoritative body state is applied, never overwrite it next frame.
        p.StopAllCoroutines();
        var body=p.Rigidbody;OwnBodyPresentation(body);
        if(!hostRestore && !_physicalBodies.ContainsKey(body)) _physicalBodies[body]=(body.isKinematic,body.detectCollisions);
        body.isKinematic=hostRestore?v[6]!=0:true;
        body.position=Unity(f.Position);body.rotation=Quaternion.Euler(Unity(f.Rotation));
        if(!body.isKinematic) {body.velocity=Unity(f.Velocity);body.angularVelocity=new(v[0],v[1],v[2]);if(v[3]!=0) body.Sleep();else body.WakeUp();}
        p._isOutOfBounds=v[5]!=0;
    }
    internal bool RequestSceneryHit(Component c,HitData hit)
    {
        if(!Client || !Active || Applying || !OwnsScenery(c) && !(c.TryCast<PhysicalObject>() is {} candidate && SharedPhysics(candidate)) ) return false;
        if(_life.Frozen || Player.Instance?.IsAlive!=true || hit.projectile!=null || hit.isExplosion || hit.dealer?.Pointer!=Player.Instance.Pointer) return true;
        var key=_catalogue!.Key(c)??"";var id=c.TryCast<PhysicalObject>() is {} prop?_physicalIds.GetValueOrDefault(prop.Pointer).Id:0;
        Send(new(WorldKind.Special,_epoch,1,id,Key:key,Asset:hit.originEntity?.ID??"",Actor:2,Life:_life.Life,Flags:90,
            Position:Net(hit.position),Rotation:Net(hit.direction),Velocity:Net(hit.pushVelocity),Value:hit.damage,Value2:HitFlags(hit),Values:new[]{hit.weakSpotMultiplier,hit.ragdollForce,hit.chanceToGib}));
        return true;
    }
    internal bool RequestPhysicalPush(PhysicalObject p,Vector3 velocity)
    {
        if(!Client || !Active || !SharedPhysics(p)) return false;
        if(!_life.Frozen && Player.Instance.IsAlive && Vector3.Distance(p.transform.position,Player.Instance.CenterOfMass)<5)
            Send(new(WorldKind.Special,_epoch,1,_physicalIds.GetValueOrDefault(p.Pointer).Id,Key:_catalogue!.Key(p)??"",
                Actor:2,Life:_life.Life,Flags:92,Position:Net(Player.Instance.FeetPosition),Velocity:Net(Vector3.ClampMagnitude(velocity,100))));
        return true;
    }
    internal bool RequestCollisionBreak(CollisionBreakableObject c,Collider collider)
    {
        if(!Client || !Active || !OwnsScenery(c)) return false;
        if(collider.GetComponentInParent<Player>()?.Pointer==Player.Instance?.Pointer && !_life.Frozen && Player.Instance!.IsAlive)
            Send(new(WorldKind.Special,_epoch,1,Key:_catalogue!.Key(c)!,Actor:2,Life:_life.Life,Flags:91,Position:Net(Player.Instance.FeetPosition)));
        return true;
    }
    private void ApplySceneryRequest(WorldFrame f)
    {
        Component? c=f.Key.Length>0?_catalogue!.Components.GetValueOrDefault(f.Key):_physicalInstances.GetValueOrDefault(f.Entity);
        if(c==null || f.Key.Length>0 && !_catalogue!.SceneryKeys.Contains(f.Key)) return;
        var center=c.GetComponentInChildren<Collider>(true)?.bounds.center??c.transform.position;
        if(Vector3.Distance(center,_remote!.CenterOfMass)>100) return;
        if(f.Flags==90)
        {
            if(Vector3.Distance(Unity(f.Position),center)>20) return;
            if(f.Value2<0 || f.Value2>2047 || f.Value2!=Mathf.Floor(f.Value2) || f.Values is not {Length:3} hitValues) return;
            var hit=DecodeHit(f with {Entity=0,Flags=(uint)f.Value2,Value=Mathf.Clamp(f.Value,0,10000),Value2=Mathf.Clamp(hitValues[0],0,100),
                Values=new[]{Mathf.Clamp(hitValues[1],0,10000),Mathf.Clamp(hitValues[2],0,1),f.Velocity.X,f.Velocity.Y,f.Velocity.Z}});
            hit.dealer=_remote.Actor;c.TryCast<IHittable>()?.Hit(hit);
        }
        else if(f.Flags==91 && c.TryCast<CollisionBreakableObject>() is {} collision && !collision._destroyed)
        {
            var remoteCollider=_remote.GetComponent<Collider>();
            if(remoteCollider!=null && collision._collider!=null && collision._collider.enabled &&
                collision._collider.bounds.Intersects(remoteCollider.bounds) &&
                !Physics.GetIgnoreLayerCollision(collision._collider.gameObject.layer,remoteCollider.gameObject.layer))
                collision.OnTriggerEnter(remoteCollider);
        }
        else if(f.Flags==92 && c.TryCast<PhysicalObject>() is {} prop && Vector3.Distance(center,_remote.CenterOfMass)<5)
        {prop.gameObject.SetActive(true);prop.Rigidbody.WakeUp();prop.Push(Vector3.ClampMagnitude(Unity(f.Velocity),100),true);}
    }
    private void ClearScenery(bool preserveHost=false)
    {
        ReleaseBodyPresentation();
        foreach(var pair in _physicalBodies) if(pair.Key!=null) {pair.Key.isKinematic=pair.Value.Kinematic;pair.Key.detectCollisions=pair.Value.Collisions;}
        if(!preserveHost) foreach(var id in _ownedPhysical)
            if(_physicalInstances.GetValueOrDefault(id) is {} p && p!=null && _physicalIds.GetValueOrDefault(p.Pointer) is var identity && identity.Id==id && identity.Cycle==p.CycleIndex)
            {
                var source=_physicalSources.GetValueOrDefault(id);
                var spawner=source!=null?_catalogue?.Components.GetValueOrDefault(source)?.TryCast<PhysicalObjectSpawner>():null;
                if(spawner!=null)
                {
                    if(spawner._spawnedObjects.Remove(p)) PoolManager.ReturnToPool(p.Cast<IPoolable>());
                }
                else if(p.gameObject.activeSelf) PoolManager.ReturnToPool(p.Cast<IPoolable>());
            }
        _physicalSources.Clear();_physicalAssets.Clear();_physicalBodies.Clear();_ownedPhysical.Clear();_physicalInstances.Clear();_physicalIds.Clear();_sceneryState.Clear();_sceneryRequestAt.Clear();
        foreach(var piece in _breakDebris) piece.Visual.Dispose();_breakDebris.Clear();
        _bodyPushes.Clear();_spawningProps.Clear();ClearFragments();ClearBarrelQueues();
    }
}
