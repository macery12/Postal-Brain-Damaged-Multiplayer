using System.Reflection;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PBDMP.Plugin;

internal sealed partial class WorldCatalogue
{
    internal readonly HashSet<string> UrineKeys=new();
    internal readonly HashSet<string> UrineConsequences=new();
    internal readonly Dictionary<IntPtr,FMODEvent> UrineAudioAssets=new();
    internal readonly Dictionary<string,Component[]> UrineSurfaces=new();
    private static readonly HashSet<string> UrineTypes=new(StringComparer.Ordinal)
        {"FuseBox","ButtonShootable","MetalButton","Toilet","PissListener","PissFiller","PissTank","PissFillListenerMovingObject","PissFillListenerScalingObject"};
    private void BuildUrineTargets()
    {
        var all=Object.FindObjectsOfType<MonoBehaviour>(true).Where(c=>c.gameObject.scene.IsValid()).ToArray();
        var explosionListeners=all.Select(c=>c.TryCast<MultiExplosionListener>()).Where(c=>c!=null).ToArray();
        var stateListeners=all.Select(c=>c.TryCast<MultiStateListener>()).Where(c=>c!=null).ToArray();
        var pending=new Queue<Component>();var seen=new HashSet<IntPtr>();
        void Enqueue(Component? c) {if(c!=null && c.GetIl2CppType().Name is not ("RailWagon" or "RailWagonInteractable") && c.gameObject.scene.IsValid() && seen.Add(c.Pointer)) pending.Enqueue(c);}
        foreach(var c in all) if(UrineTypes.Contains(c.GetIl2CppType().Name)) Enqueue(c);
        while(pending.Count>0)
        {
            var c=pending.Dequeue();var type=c.GetIl2CppType().Name;var key=Add(c);
            UrineConsequences.Add(key);
            if(UrineTypes.Contains(type)) UrineKeys.Add(key);
            if(c.TryCast<Interactable>()!=null) DoorKeys.Add(key);
            else if(UrineTypes.Contains(type)) { }
            else EncounterKeys.Add(key);
            RegisterUrineSurface(c.transform);
            // Native input callbacks are attached to sibling listeners, rather
            // than appearing in the urine target's own serialized UnityEvents.
            foreach(var sibling in c.GetComponents<MonoBehaviour>())
                if(sibling.GetIl2CppType().Name is "ExplosionListener" or "InteractableStateListener" or "PissListener" or "Exploder") Enqueue(sibling);
            if(c.TryCast<Toilet>() is {} toilet) {Enqueue(toilet._toiletFlap);Enqueue(toilet._toiletHandle);}
            if(c.TryCast<ICustomInteractTrigger>() is {} source && source.interactableGameObject!=null)
                foreach(var target in source.interactableGameObject.GetComponents<MonoBehaviour>()) if(SafeMotion(target.transform)) Enqueue(target);
            if(c.TryCast<PissFillListenerMovingObject>() is {} moving && moving._moveTransform!=null) RegisterUrineSurface(moving._moveTransform);
            if(c.TryCast<PissFillListenerScalingObject>() is {} scaling && scaling._scaleTransform!=null) RegisterUrineSurface(scaling._scaleTransform);
            if(c.TryCast<Toggler>() is {} toggler && toggler._objectsToHide!=null)
                foreach(var obj in toggler._objectsToHide) if(obj!=null) RegisterUrineSurface(obj.transform);
            if(c.TryCast<Toggler>() is {} shower && shower._objectsToShow!=null)
                foreach(var obj in shower._objectsToShow) if(obj!=null) RegisterUrineSurface(obj.transform);
            var actual=NativeAssetGraph.Actual(c);
            foreach(var p in actual.GetType().GetProperties(BindingFlags.Public|BindingFlags.Instance).Where(p=>p.GetIndexParameters().Length==0))
            {
                if(typeof(UnityEngine.Events.UnityEventBase).IsAssignableFrom(p.PropertyType))
                {
                    if(p.GetValue(actual) is not UnityEngine.Events.UnityEventBase events) continue;
                    for(var i=0;i<events.GetPersistentEventCount();i++)
                    {
                        var target=events.GetPersistentTarget(i);var child=target?.TryCast<Component>();var obj=target?.TryCast<GameObject>();
                        if(target?.TryCast<FMODEvent>() is {} sound) UrineAudioAssets[sound.Pointer]=sound;
                        if(child!=null && SafeMotion(child.transform)) Enqueue(child);
                        if(obj!=null) RegisterUrineSurface(obj.transform);
                    }
                }
                // Explicit native movement references can point outside the
                // controller's hierarchy (e.g. fill-driven platforms/rails).
                else if(p.Name.StartsWith('_') && p.PropertyType==typeof(Transform) && p.GetValue(actual) is Transform t) RegisterUrineSurface(t);
                else if(p.Name.StartsWith('_') && p.PropertyType==typeof(GameObject) && p.GetValue(actual) is GameObject obj) RegisterUrineSurface(obj.transform);
                else if(p.Name.StartsWith('_') && typeof(Component).IsAssignableFrom(p.PropertyType) && p.GetValue(actual) is Component reference && reference.gameObject.scene.IsValid() && SafeMotion(reference.transform)) Enqueue(reference);
                else if(typeof(FMODEvent).IsAssignableFrom(p.PropertyType) && p.GetValue(actual) is FMODEvent sound) UrineAudioAssets[sound.Pointer]=sound;
            }
            foreach(var explosions in explosionListeners)
                if(explosions!._explodersToListen!=null) foreach(var e in explosions._explodersToListen) if(e!=null && seen.Contains(e.Pointer)) {Enqueue(explosions);break;}
            foreach(var states in stateListeners)
                if(states!._statesToListen!=null) foreach(var s in states._statesToListen) if(s.StateableGO!=null && s.StateableGO.GetComponents<Component>().Any(x=>seen.Contains(x.Pointer))) {Enqueue(states);break;}
        }
        Plugin.Logger.LogInfo($"Urine catalogue: {UrineKeys.Count} targets, {UrineSurfaces.Count} presentation/collision transforms.");
    }
    private void RegisterUrineSurface(Transform root)
    {
        foreach(var t in root.GetComponentsInChildren<Transform>(true))
        {
            if(!SafeMotion(t)) continue;
            var components=t.GetComponents<Component>().Select(c=>(Component?)c.TryCast<Renderer>()??(Component?)c.TryCast<Collider>()??(Component?)c.TryCast<Light>()??c.TryCast<UnityEngine.AI.NavMeshObstacle>()).Where(c=>c!=null).Select(c=>c!).ToArray();
            if(components.Length>32) throw new InvalidOperationException("Urine surface exceeds bounded component adapter: "+t.name);
            var key=TransformKey(t);EncounterMotion[key]=t;UrineSurfaces[key]=components;
        }
    }
}
