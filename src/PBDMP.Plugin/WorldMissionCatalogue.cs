using System.Reflection;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using Hyperstrange.PBD.Ready;
using UnityEngine;

namespace PBDMP.Plugin;

internal sealed partial class WorldCatalogue
{
    internal readonly HashSet<string> MissionKeys=new();
    internal readonly HashSet<string> MissionBreakKeys=new();
    internal readonly HashSet<string> MissionTriggerKeys=new();
    internal readonly HashSet<string> MissionExitKeys=new();
    internal readonly Dictionary<string,Collider[]> MissionContacts=new();
    internal readonly HashSet<string> MissionVolumeKeys=new();
    internal readonly HashSet<string> MissionInteractionExits=new();
    internal readonly HashSet<string> MissionConditionKeys=new();
    internal readonly List<string> MissionAudit=new();
    private static readonly HashSet<string> MissionTypes=new(StringComparer.Ordinal)
    {"Mover","MoverController","Rotater","MultiRotater","Toggler","ScriptToggler","GenericStateable","GenericInteractableStateable",
        "Elevator","ElevatorButton","WireWallGate","Laser","DamageTrigger","TimelineSequence","EventListener","MultiStateListener","MultiInteractListener",
        "MultiBreakListener","ExplosionListener","BreakListener","InteractListener","InteractableStateListener","ActiveStateSaveable","Explosive","FallingPlatform","GameplayVolume","TriggerWallComponent",
        "Invoker","ControlPanel","EmissionAnimator","LightAnimator","NeonSignController","AirTube","GlassObject","SpawnPointsController","SpawnPointInFight","Cannon","FuckOffCannon",
        "CovidArenaController","CovidArenaSegment","AntiCovidSyringeController","AntiCovidSyringe","GravityChanger","ForestBridge","FerrisWheelCart","CeilingConveyor","CeilingConveyorListener","Scaler","CovidArenaBase","DrinkFountain",
        "PhysicalObjectSpawner","Pusher","PawShockwaveTrigger","LeonDuskSwitch","Teleporter","KillVolume","LocalRotation","RocketSequence"};
    private void BuildMissionTargets()
    {
        var all=UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true).Where(c=>c.gameObject.scene.IsValid() && SafeMotion(c.transform)).ToArray();
        // Asset events become shared only when an authored world listener owns
        // them. UI/local-only events must not seed a world trigger accidentally.
        var worldEvents=new HashSet<IntPtr>();
        foreach(var listener in all.Select(c=>c.TryCast<EventListener>()).Where(c=>c!=null))
            if(listener!._gameEventsToListen!=null) foreach(var asset in listener._gameEventsToListen) if(asset!=null) worldEvents.Add(asset.Pointer);
        var pending=new Queue<Component>();var seen=new HashSet<IntPtr>();
        void Enqueue(Component? c)
        {
            if(c==null || !c.gameObject.scene.IsValid() || !SafeMotion(c.transform)) return;
            var type=c.GetIl2CppType().Name;
            if(!MissionTypes.Contains(type) && c.TryCast<BreakableObject>()==null && c.TryCast<CollisionBreakableObject>()==null && c.TryCast<PlayerTrigger>()==null &&
                c.TryCast<Interactable>()==null && c.TryCast<SpawnPoint>()==null && !Encounters.Contains(type) && !UrineTypes.Contains(type)) return;
            if(seen.Add(c.Pointer)) pending.Enqueue(c);
        }
        foreach(var c in all)
        {
            var seedType=c.GetIl2CppType().Name;
            // Decorative glass, lights and signs join the graph only when an
            // authored mission controller references them. Do not poll scenery.
            if(MissionTypes.Contains(seedType) && seedType is not ("GlassObject" or "LightAnimator" or "EmissionAnimator" or "NeonSignController" or "AirTube" or "LocalRotation")) Enqueue(c);
            if(c.TryCast<BreakableObject>() is {} b && (MissionHierarchy(b.transform) || b._onlyExplosion || b.GetComponent<BreakListener>()!=null)) Enqueue(b);
            if(c.TryCast<PlayerTriggerListener>() is {} listener)
            {
                var trigger=listener.GetComponent<PlayerTrigger>();var shared=false;var exit=false;
                foreach(var events in new[]{listener._onTriggerEvents,listener._onTriggerExitEvents}) if(events!=null)
                    for(var i=0;i<events.GetPersistentEventCount();i++)
                    {
                        var target=events.GetPersistentTarget(i);var component=target?.TryCast<Component>();var method=events.GetPersistentMethodName(i);
                        var type=component?.GetIl2CppType().Name??target?.GetIl2CppType().Name??"<null>";
                        MissionAudit.Add($"trigger\t{DiagnosticPath(trigger!.transform)}\t{type}.{method}");
                        if(type=="LevelController" && method=="CloseLevel") {shared=true;exit=true;}
                        if(method=="Raise" && target?.TryCast<Hyperstrange.Base.GameEvent>() is {} gameEvent && worldEvents.Contains(gameEvent.Pointer)) shared=true;
                        if(component!=null && (MissionTypes.Contains(type) || component.TryCast<Interactable>()!=null || component.TryCast<SpawnPoint>()!=null || Encounters.Contains(type))) {shared=true;Enqueue(component);}
                        if(target?.TryCast<GameObject>() is {} obj) {shared=true;RegisterUrineSurface(obj.transform);foreach(var child in obj.GetComponents<MonoBehaviour>()) Enqueue(child);}
                    }
                if(shared && trigger!=null) {Enqueue(trigger);MissionTriggerKeys.Add(Add(trigger));if(exit) MissionExitKeys.Add(Add(trigger));}
            }
        }
        while(pending.Count>0)
        {
            var c=pending.Dequeue();var key=Add(c);var type=c.GetIl2CppType().Name;MissionKeys.Add(key);
            if(c.TryCast<BreakableObject>()!=null || c.TryCast<CollisionBreakableObject>()!=null || c.TryCast<WireWallGate>()!=null || c.TryCast<FallingPlatform>()!=null) MissionBreakKeys.Add(key);
            else if(c.TryCast<Interactable>()!=null && c.TryCast<DrinkFountain>()==null) DoorKeys.Add(key);
            else EncounterKeys.Add(key);
            if(c.TryCast<DrinkFountain>()!=null) DoorKeys.Remove(key); // Powerup use belongs to the actual local player.
            if(c.TryCast<PlayerTrigger>()!=null) MissionTriggerKeys.Add(key);
            if(type=="EmissionAnimator") UrineConsequences.Add(key);
            if(c.TryCast<WireWallGate>()!=null || c.TryCast<FallingPlatform>()!=null) EncounterKeys.Add(key);
            if(c.TryCast<GameplayVolume>() is {} volume) {MissionVolumeKeys.Add(key);if(volume.kind==VolumeKind.Win) MissionExitKeys.Add(key);}
            if(c.TryCast<ICustomInteractTrigger>() is {} custom && custom.interactableGameObject!=null) foreach(var component in custom.interactableGameObject.GetComponents<MonoBehaviour>()) Enqueue(component);
            if(MissionBreakKeys.Contains(key))
            {
                var breaker=c.TryCast<BreakableObject>()?._breaker??c.TryCast<CollisionBreakableObject>()?._breaker??c.TryCast<WireWallGate>()?._breaker??c.TryCast<FallingPlatform>()?._breaker;
                if(breaker?._wholeObject!=null) RegisterUrineSurface(breaker._wholeObject.transform);
                if(c.TryCast<BreakableObject>()!=null || c.TryCast<CollisionBreakableObject>()!=null) continue;
            }
            RegisterUrineSurface(c.transform);
            if(c.TryCast<InteractListener>()!=null || c.TryCast<InteractableStateListener>()!=null) Enqueue(c.GetComponent<Interactable>());
            foreach(var sibling in c.GetComponents<MonoBehaviour>()) if(MissionTypes.Contains(sibling.GetIl2CppType().Name)) Enqueue(sibling);
            var actual=NativeAssetGraph.Actual(c);
            if(actual.GetType().GetProperties(BindingFlags.Public|BindingFlags.Instance).Any(p=>p.Name.StartsWith('_') && p.CanWrite && p.PropertyType==typeof(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<bool>))) MissionConditionKeys.Add(key);
            foreach(var p in actual.GetType().GetProperties(BindingFlags.Public|BindingFlags.Instance).Where(p=>p.GetIndexParameters().Length==0))
            {
                if(typeof(UnityEngine.Events.UnityEventBase).IsAssignableFrom(p.PropertyType) && p.GetValue(actual) is UnityEngine.Events.UnityEventBase events)
                {
                    for(var i=0;i<events.GetPersistentEventCount();i++)
                    {
                        var target=events.GetPersistentTarget(i);var component=target?.TryCast<Component>();var obj=target?.TryCast<GameObject>();
                        MissionAudit.Add($"callback\t{DiagnosticPath(c.transform)}\t{component?.GetIl2CppType().Name??target?.GetIl2CppType().Name}.{events.GetPersistentMethodName(i)}");
                        if(component?.TryCast<LevelController>()!=null && events.GetPersistentMethodName(i)=="CloseLevel") RegisterMissionExit(c);
                        Enqueue(component);if(obj!=null && SafeMotion(obj.transform)) RegisterUrineSurface(obj.transform);
                    }
                }
                else if(AuthoredMember(p) && typeof(Component).IsAssignableFrom(p.PropertyType) && p.GetValue(actual) is Component child)
                {Enqueue(child);if(child is Transform t && SafeMotion(t)) RegisterUrineSurface(t);}
                else if(AuthoredMember(p) && p.PropertyType==typeof(GameObject) && p.GetValue(actual) is GameObject obj && SafeMotion(obj.transform))
                {RegisterUrineSurface(obj.transform);foreach(var behaviour in obj.GetComponents<MonoBehaviour>()) Enqueue(behaviour);}
                else if(AuthoredMember(p) && p.PropertyType.IsGenericType &&
                    p.PropertyType.GetGenericTypeDefinition()==typeof(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<>) &&
                    typeof(UnityEngine.Object).IsAssignableFrom(p.PropertyType.GetGenericArguments()[0]) && p.GetValue(actual) is {} collection)
                {
                    // Traverse authored typed references only; never enumerate resources
                    // or invoke arbitrary object properties to expand the graph.
                    var length=Convert.ToInt32(p.PropertyType.GetProperty("Length")!.GetValue(collection));
                    if(length>4096) throw new InvalidOperationException("Mission reference collection exceeds bound: "+p.Name);
                    var item=p.PropertyType.GetProperty("Item");
                    for(var i=0;i<length;i++)
                    {
                        var value=item?.GetValue(collection,new object[]{i});
                        if(value is Component member) {Enqueue(member);if(member.TryCast<Transform>() is {} t && SafeMotion(t)) RegisterUrineSurface(t);}
                        else if(value is GameObject memberObject && SafeMotion(memberObject.transform)) {RegisterUrineSurface(memberObject.transform);foreach(var b in memberObject.GetComponents<MonoBehaviour>()) Enqueue(b);}
                    }
                }
                else if(typeof(FMODEvent).IsAssignableFrom(p.PropertyType) && p.GetValue(actual) is FMODEvent sound) UrineAudioAssets[sound.Pointer]=sound;
            }
            if(c.TryCast<MoverController>() is {} movers && movers._movers!=null) foreach(var m in movers._movers) Enqueue(m);
            if(c.TryCast<Toggler>() is {} toggler)
                foreach(var list in new[]{toggler._objectsToShow,toggler._objectsToHide}) if(list!=null) foreach(var obj in list) if(obj!=null && SafeMotion(obj.transform)) RegisterUrineSurface(obj.transform);
            if(c.TryCast<ScriptToggler>() is {} scripts)
                foreach(var list in new[]{scripts._behavioursToEnable,scripts._behavioursToDisable}) if(list!=null) foreach(var b in list) Enqueue(b);
            if(c.TryCast<MultiBreakListener>() is {} breaks && breaks._breakersToListen!=null) foreach(var b in breaks._breakersToListen) if(b!=null) {Enqueue(b.GetComponent<BreakableObject>());Enqueue(b.GetComponent<CollisionBreakableObject>());}
            if(c.TryCast<MultiStateListener>() is {} states && states._statesToListen!=null) foreach(var state in states._statesToListen) if(state.StateableGO!=null) foreach(var s in state.StateableGO.GetComponents<MonoBehaviour>()) Enqueue(s);
            if(c.TryCast<InteractableStateListener>() is {} interactions && interactions._stateEvents!=null) foreach(var state in interactions._stateEvents)
                if(state.OnStateChangedEvent is {} events) for(var i=0;i<events.GetPersistentEventCount();i++)
                {var target=events.GetPersistentTarget(i)?.TryCast<Component>();Enqueue(target);if(target?.TryCast<LevelController>()!=null && events.GetPersistentMethodName(i)=="CloseLevel") RegisterMissionExit(c);}
            if(c.TryCast<LightAnimator>() is {} lights && lights._lights!=null) foreach(var light in lights._lights) if(light!=null) RegisterUrineSurface(light.transform);
            if(c.TryCast<ControlPanel>() is {} panel && panel._lights!=null) foreach(var light in panel._lights) if(light!=null) RegisterUrineSurface(light.transform);
        }
        MissionAudit.Add($"summary\tcomponents={MissionKeys.Count}\ttriggers={MissionTriggerKeys.Count}\texits={MissionExitKeys.Count}\tdestruction={MissionBreakKeys.Count}");
        foreach(var key in MissionTriggerKeys.Concat(MissionVolumeKeys).Distinct()) MissionContacts[key]=Components[key].GetComponents<Collider>().ToArray();
        Plugin.Logger.LogInfo($"Mission catalogue: {MissionKeys.Count} components, {MissionTriggerKeys.Count} player triggers, {MissionExitKeys.Count} exits, {MissionBreakKeys.Count} progression breakables.");
    }
    private void RegisterMissionExit(Component source)
    {
        var trigger=source.GetComponent<PlayerTrigger>();var actor=source.GetComponent<Interactable>();
        if(trigger!=null) MissionExitKeys.Add(Add(trigger));
        else if(actor!=null) {var key=Add(actor);MissionKeys.Add(key);DoorKeys.Add(key);MissionExitKeys.Add(key);MissionInteractionExits.Add(key);}
    }
    internal static string DiagnosticPath(Transform t) {var names=new List<string>();for(var p=t;p!=null;p=p.parent) names.Add(p.name);names.Reverse();return string.Join("/",names);}
    private static bool MissionHierarchy(Transform t)
    {
        for(var p=t;p!=null;p=p.parent)
            if(p.name.Equals("OBJECTIVES",StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
