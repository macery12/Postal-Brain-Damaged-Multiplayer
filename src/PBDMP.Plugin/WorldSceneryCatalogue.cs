using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using Hyperstrange.PBD.Ready;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PBDMP.Plugin;

internal sealed partial class WorldCatalogue
{
    internal readonly Dictionary<string,((Renderer Component,bool Enabled)[] Renderers,(Collider Component,bool Enabled)[] Colliders)> GlassDefaults=new();
    internal readonly Dictionary<string,Rigidbody> Fragments=new();
    internal readonly HashSet<IntPtr> FragmentPointers=new();
    internal readonly HashSet<string> SceneryKeys=new();
    internal readonly HashSet<string> SpawnerKeys=new();
    internal readonly Dictionary<string,PhysicalObject> PhysicalPrefabs=new();
    internal readonly Dictionary<IntPtr,string> PhysicalPrefabKeys=new();
    internal readonly HashSet<string> PhysicalPrefabReferences=new();
    internal string PhysicalPrefabKey(PhysicalObject p)
    {
        var original=p.OriginalPoolable;
        return original!=null && PhysicalPrefabKeys.TryGetValue(original.Pointer,out var key)?key:PhysicalAsset(p);
    }
    internal readonly Dictionary<IntPtr,string> BreakOwners=new();
    internal readonly HashSet<string> MechanismKeys=new();
    internal readonly HashSet<string> SecretKeys=new();
    internal readonly HashSet<string> SequenceKeys=new();
    internal readonly List<MovingPlatformTrigger> Platforms=new();
    internal readonly Dictionary<string,MovingPlatformTrigger> CarrierPlatforms=new();
    internal readonly Dictionary<IntPtr,string> CarrierKeys=new();
    internal readonly Dictionary<IntPtr,Collider[]> CarrierContacts=new();
    internal readonly Dictionary<IntPtr,Collider[]> CarrierSurfaces=new();
    private static readonly HashSet<string> MechanismTypes=new(StringComparer.Ordinal)
    {"Mover","Rotater","MultiRotater","Elevator","Scaler","FallingPlatform","CeilingConveyor","CeilingConveyorListener",
     "CovidArenaController","CovidArenaSegment","AntiCovidSyringeController","AntiCovidSyringe","Cannon","RocketSequence","ForestBridge","FerrisWheelCart","Pusher","PawShockwaveTrigger","LeonDuskSwitch","WireWallGate"};
    internal static string PhysicalAsset(PhysicalObject p)=>Hash(p.GetIl2CppType().FullName+"|"+p.name.Replace("(Clone)","",StringComparison.Ordinal));
    private void BuildSceneryTargets()
    {
        foreach(var prefab in Resources.FindObjectsOfTypeAll<PhysicalObject>())
            if(!prefab.gameObject.scene.IsValid()) PhysicalPrefabs[PhysicalAsset(prefab)]=prefab;
        var spawned=new HashSet<IntPtr>();
        foreach(var spawner in Object.FindObjectsOfType<PhysicalObjectSpawner>(true).OrderBy(p=>PathOf(p.transform),StringComparer.Ordinal))
        {
            if(!spawner.gameObject.scene.IsValid() || !SafeMotion(spawner.transform)) continue;
            var key=Add(spawner);SpawnerKeys.Add(key);
            for(var i=0;i<spawner._physicalObjects.Length;i++)
            {
                var prefab=spawner._physicalObjects[i];if(prefab==null) continue;
                var asset=Hash(key+"|physical-prefab|"+i);PhysicalPrefabs[asset]=prefab;PhysicalPrefabReferences.Add(asset);
                PhysicalPrefabKeys.TryAdd(prefab.Pointer,asset);
            }
            foreach(var prop in spawner._spawnedObjects) if(prop!=null) spawned.Add(prop.Pointer);
        }
        foreach(var c in Object.FindObjectsOfType<MonoBehaviour>(true))
        {
            if(!c.gameObject.scene.IsValid() || !SafeMotion(c.transform)) continue;
            var type=c.GetIl2CppType().Name;
            if(c.TryCast<PhysicalObject>() is {} physical && (spawned.Contains(c.Pointer) || physical.OriginalPoolable!=null || !WorldAuthoredProps.Contains(physical))) continue;
            if(c.TryCast<BreakableObject>()!=null || c.TryCast<CollisionBreakableObject>()!=null ||
                c.TryCast<GlassObject>()!=null || c.TryCast<PhysicalObject>()!=null ||
                c.TryCast<ExplosiveBarrel>()!=null || c.TryCast<BurnableObject>()!=null)
            {
                var key=Add(c);SceneryKeys.Add(key);
                if(c.TryCast<GlassObject>() is {} pane)
                    GlassDefaults[key]=(pane.GetComponentsInChildren<Renderer>(true).Select(v=>(v,v.enabled)).ToArray(),pane.GetComponentsInChildren<Collider>(true).Select(v=>(v,v.enabled)).ToArray());
                if(c.TryCast<BreakableObject>()!=null || c.TryCast<CollisionBreakableObject>()!=null)
                {MissionKeys.Add(key);MissionBreakKeys.Add(key);}
            }
            if(MechanismTypes.Contains(type) || type=="LocalRotation" && Key(c) is {} referenced && MissionKeys.Contains(referenced))
            {
                var key=Add(c);MechanismKeys.Add(key);MissionKeys.Add(key);EncounterKeys.Add(key);
                RegisterUrineSurface(c.transform);
                if(c.TryCast<CeilingConveyor>() is {} conveyor && conveyor._ceilingConveyorListener!=null)
                    foreach(var listener in conveyor._ceilingConveyorListener)
                        if(listener!=null) {var child=Add(listener);MechanismKeys.Add(child);MissionKeys.Add(child);EncounterKeys.Add(child);RegisterUrineSurface(listener.transform);}
            }
            if(c.TryCast<MovingPlatformTrigger>() is {} platform)
            {
                Platforms.Add(platform);
                if(platform._transformToFollow is {} follow && SafeMotion(follow))
                {
                    var key=TransformKey(follow);CarrierPlatforms.TryAdd(key,platform);CarrierKeys[platform.Pointer]=key;
                    EncounterMotion[key]=follow;
                    // Elevators can keep the behaviour on their root and the
                    // actual rider volume on the referenced follow transform.
                    CarrierContacts[platform.Pointer]=platform.GetComponents<Collider>().Concat(follow.GetComponents<Collider>()).DistinctBy(c=>c.Pointer).ToArray();
                    var deck=platform.GetComponent<Elevator>()?._platform??follow;
                    CarrierSurfaces[platform.Pointer]=deck.GetComponentsInChildren<Collider>(true).Where(c=>!c.isTrigger && SafeMotion(c.transform)).ToArray();
                }
            }
            if(c.TryCast<PlayerTriggerSecretComponent>()!=null)
            {
                var key=Add(c);SecretKeys.Add(key);
                var trigger=c.GetComponent<PlayerTrigger>();if(trigger!=null) {var t=Add(trigger);MissionKeys.Add(t);MissionTriggerKeys.Add(t);EncounterKeys.Add(t);}
            }
            if(c.TryCast<TimelineSequence>() is {} timeline) {AuditSequence(timeline);var key=Add(c);SequenceKeys.Add(key);MissionKeys.Add(key);EncounterKeys.Add(key);}
        }
        foreach(var key in MissionBreakKeys)
        {
            var c=Components[key];
            var breaker=c.TryCast<BreakableObject>()?._breaker??c.TryCast<CollisionBreakableObject>()?._breaker??
                c.TryCast<FallingPlatform>()?._breaker??c.TryCast<WireWallGate>()?._breaker;
            if(breaker!=null)
            {
                BreakOwners[breaker.Pointer]=key;
                if(breaker._rigidbodies!=null) for(var i=0;i<breaker._rigidbodies.Length;i++)
                {
                    var body=breaker._rigidbodies[i];if(body==null || !SharedWorld.GameplayFragment(body)) continue;
                    Fragments[Hash(key+"|fragment|"+i)]=body;FragmentPointers.Add(body.Pointer);
                }
            }
        }
        foreach(var key in MissionTriggerKeys.Concat(MissionVolumeKeys).Distinct())
            MissionContacts[key]=Components[key].GetComponentsInChildren<Collider>(true).ToArray();
    }
}
