using System.Security.Cryptography;
using System.Text;
using System.Reflection;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using Il2CppInterop.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PBDMP.Plugin;

internal sealed partial class WorldCatalogue
{
    internal readonly Dictionary<string, Component> Components = new();
    internal readonly Dictionary<string, Transform> DoorMotion = new();
    internal readonly Dictionary<string, Transform> EncounterMotion = new();
    internal readonly Dictionary<IntPtr,string> PointerKeys = new();
    /// <summary>Readable identity behind each hashed key, for diffing two peers' catalogues.</summary>
    internal readonly Dictionary<string,string> Identities = new();
    internal readonly Dictionary<string, NpcId> NpcAssets = new();
    internal readonly Dictionary<string, EntityId> Assets = new();
    internal readonly HashSet<string> DoorKeys = new();
    internal readonly HashSet<string> EncounterKeys = new();
    internal readonly HashSet<string> HookableKeys = new();
    internal string Manifest = "";
    internal KeyValuePair<string,Component>[] ScriptedActors=Array.Empty<KeyValuePair<string,Component>>();
    internal KeyValuePair<string,Transform>[] AuthoredMotion=Array.Empty<KeyValuePair<string,Transform>>();
    private static readonly HashSet<string> Doors = new(StringComparer.Ordinal)
        { "Door", "DoubleDoor", "SlidingDoors", "ElevatorDoors", "ToiletDoor", "Vault" };
    private static readonly HashSet<string> Encounters = new(StringComparer.Ordinal)
        { "SpawnPointDeathListener", "SpawnPointsDeathListener", "NpcGroupWipeListener", "NpcGroup", "NpcGroupChain", "LastBossArena", "BigCatBossArena", "PlayerTriggerSpawnEnemies", "SpawnPointRespawnOnDeath" };

    internal void Build()
    {
        _skipCachedParent=SessionLifecycle.Survival(LevelManager.CurrentLevel);
        foreach (var asset in Resources.FindObjectsOfTypeAll<EntityId>())
            if (!string.IsNullOrEmpty(asset.ID)) Assets[asset.ID]=asset;
        foreach (var asset in Resources.FindObjectsOfTypeAll<NpcId>()) NpcAssets[NpcAssetKey(asset)]=asset;
        foreach (var spawn in Resources.FindObjectsOfTypeAll<SpawnPoint>())
            if (spawn.gameObject.scene.IsValid()) Add(spawn);
        foreach (var component in Object.FindObjectsOfType<MonoBehaviour>(true))
        {
            if (!component.gameObject.scene.IsValid()) continue;
            var type=component.GetIl2CppType().Name;
            var door=Doors.Contains(type) || (type is "Mover" or "MoverController" or "Rotater" or "Toggler") && DoorName(component.transform);
            if (!door && !Encounters.Contains(type) && type is not ("FakeCitizen" or "FakePostalDude")) continue;
            var key=Add(component);
            if(type is "FakeCitizen" or "FakePostalDude") continue;
            if (!door) {EncounterKeys.Add(key);continue;}
            DoorKeys.Add(key);
            foreach (var transform in component.GetComponentsInChildren<Transform>(true))
            {
                // Local pickups, player/NPC objects never become door-owned state.
                if (!SafeMotion(transform)) continue;
                DoorMotion[TransformKey(transform)]=transform;
            }
        }
        foreach(var listener in Object.FindObjectsOfType<SpawnPointTriggerListener>(true))
        {
            if(listener._playerTrigger==null) continue;
            EncounterKeys.Add(Add(listener));EncounterKeys.Add(Add(listener._playerTrigger));
        }
        foreach(var listener in Object.FindObjectsOfType<PlayerTriggerListener>(true))
        {
            var related=false;
            foreach(var events in new[]{listener._onTriggerEvents,listener._onTriggerExitEvents})
            {
                if(events==null) continue;
                for(var i=0;i<events.GetPersistentEventCount();i++)
                {
                    var target=events.GetPersistentTarget(i)?.TryCast<Component>();if(target==null) continue;
                    var type=target.GetIl2CppType().Name;
                    if(type=="SpawnPoint" || Encounters.Contains(type) || Key(target) is {} targetKey && DoorKeys.Contains(targetKey)) related=true;
                }
            }
            if(!related) continue;
            EncounterKeys.Add(Add(listener));var trigger=listener.GetComponent<PlayerTrigger>();if(trigger!=null) EncounterKeys.Add(Add(trigger));
        }
        foreach(var key in EncounterKeys.ToArray())
        {
            var component=Components[key];var actual=NativeAssetGraph.Actual(component);
            foreach(var property in actual.GetType().GetProperties(BindingFlags.Public|BindingFlags.Instance).Where(p=>p.Name.StartsWith('_') && p.GetIndexParameters().Length==0))
            {
                if(typeof(UnityEngine.Events.UnityEventBase).IsAssignableFrom(property.PropertyType))
                {
                    var events=property.GetValue(actual) as UnityEngine.Events.UnityEventBase;if(events==null) continue;
                    for(var i=0;i<events.GetPersistentEventCount();i++)
                    {
                        var target=events.GetPersistentTarget(i);var child=target?.TryCast<Component>();var obj=target?.TryCast<GameObject>();
                        if(child!=null) AddEncounterTarget(child);
                        else if(obj!=null && SafeMotion(obj.transform)) EncounterMotion[TransformKey(obj.transform)]=obj.transform;
                    }
                }
            }
            if(component.TryCast<Hyperstrange.PBD.NpcComponents.LastBossArena>() is {} boss)
            {
                foreach(var obj in new[]{boss._forceField,boss._fakeMech,boss._hookable?.gameObject})
                    if(obj!=null && SafeMotion(obj.transform)) EncounterMotion[TransformKey(obj.transform)]=obj.transform;
                if(boss._jumpPads!=null) foreach(var pad in boss._jumpPads) if(pad!=null) AddEncounterTarget(pad);
                if(boss._hookable!=null) {var hookKey=Add(boss._hookable);HookableKeys.Add(hookKey);EncounterKeys.Add(hookKey);}
            }
            if(component.TryCast<BigCatBossArena>() is {} catBoss)
            {
                foreach(var obj in new[]{catBoss._cage,catBoss._table,catBoss._normalCeiling,catBoss._exitCageButton?.gameObject})
                    if(obj!=null && SafeMotion(obj.transform)) EncounterMotion[TransformKey(obj.transform)]=obj.transform;
                foreach(var list in new[]{catBoss._chairPhaseObjectsToDisable,catBoss._arenaDoors,catBoss._newCeiling})
                    if(list!=null) foreach(var obj in list) if(obj!=null && SafeMotion(obj.transform)) EncounterMotion[TransformKey(obj.transform)]=obj.transform;
                if(catBoss._cageTriggers!=null) foreach(var trigger in catBoss._cageTriggers) if(trigger!=null) EncounterKeys.Add(Add(trigger));
                if(catBoss._arenaEntranceDoor!=null) AddEncounterTarget(catBoss._arenaEntranceDoor);
                if(catBoss._hatch!=null) AddEncounterTarget(catBoss._hatch);
                if(catBoss._exitCageButton!=null) {var hookKey=Add(catBoss._exitCageButton);HookableKeys.Add(hookKey);EncounterKeys.Add(hookKey);}
                if(catBoss._buttons!=null) foreach(var button in catBoss._buttons)
                {
                    if(button==null) continue;
                    var target=button.GetComponent<Interactable>();if(target!=null) DoorKeys.Add(Add(target));
                    var hooked=button.GetComponent<ButtonHookable>();if(hooked!=null){var hookKey=Add(hooked);HookableKeys.Add(hookKey);EncounterKeys.Add(hookKey);}
                }
            }
        }
        foreach (var listener in Object.FindObjectsOfType<InteractableStateListener>(true))
        {
            if (listener._stateEvents==null) continue;
            var related=false;
            foreach (var state in listener._stateEvents)
            {
                var callbacks=state.OnStateChangedEvent; if (callbacks==null) continue;
                for (var i=0;i<callbacks.GetPersistentEventCount();i++)
                {
                    var target=callbacks.GetPersistentTarget(i)?.TryCast<Component>();
                    if (target!=null && PointerKeys.TryGetValue(target.Pointer,out var targetKey) && DoorKeys.Contains(targetKey)) related=true;
                }
            }
            if (!related) continue;
            var interactable=listener.GetComponent<Interactable>();
            if (interactable!=null) DoorKeys.Add(Add(interactable));
            var hookable=listener.GetComponent<ButtonHookable>();if(hookable!=null){var hookKey=Add(hookable);HookableKeys.Add(hookKey);EncounterKeys.Add(hookKey);}
        }
        BuildUrineTargets();
        BuildMissionTargets();
        BuildSceneryTargets();BuildMachineSounds();
        // These authored sets change only when a new map catalogue is built.
        // Preserve the original door-first ordering/duplicate precedence.
        ScriptedActors=Components.Where(p=>p.Value!=null && p.Value.GetIl2CppType().Name is "FakeCitizen" or "FakePostalDude").ToArray();
        AuthoredMotion=DoorMotion.Concat(EncounterMotion).GroupBy(p=>p.Key).Select(g=>g.First()).ToArray();
        Manifest=Hash(string.Join("\n",Components.Keys.OrderBy(k=>k,StringComparer.Ordinal)) + "\n" +
            string.Join("\n",DoorMotion.Keys.Concat(EncounterMotion.Keys).Concat(Fragments.Keys).Concat(PhysicalPrefabReferences).OrderBy(k=>k,StringComparer.Ordinal)));
        Plugin.Logger.LogInfo($"World catalogue {Manifest}: {Components.Count} authored components, {DoorKeys.Count} door/gate components, {DoorMotion.Count} motion transforms.");
        WriteListing();
    }
    /// <summary>Peers only exchange the manifest hash; this file is what two players diff after a mismatch.</summary>
    internal static string ListingPath(string scene) => Path.Combine(BepInEx.Paths.BepInExRootPath, "PBDMP", "catalogue-" + scene + ".tsv");
    private void WriteListing()
    {
        try
        {
            var lines=new List<string>{$"manifest\t{Manifest}\t{Components.Count}\t{MissionKeys.Count}\t{DoorMotion.Count}\t{EncounterMotion.Count}"};
            lines.AddRange(Components.OrderBy(p=>Identities[p.Key],StringComparer.Ordinal).Select(p=>$"component\t{Identities[p.Key]}\tmission={MissionKeys.Contains(p.Key)}"));
            lines.AddRange(DoorMotion.Values.Concat(EncounterMotion.Values).Select(t=>"motion\t"+PathOf(t)).Distinct().OrderBy(l=>l,StringComparer.Ordinal));
            var path=ListingPath(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(path,lines);
        }
        catch(Exception error) {Plugin.Logger.LogWarning("World catalogue listing not written: "+error.Message);}
    }
    private void AddEncounterTarget(Component component)
    {
        var type=component.GetIl2CppType().Name;
        // These are authored combat consequences. Excluded hazards/destructible gates
        // never become shared just because an event happens to reference them.
        if(type is not ("Mover" or "MoverController" or "Rotater" or "Toggler" or "JumpPad") && !Doors.Contains(type)) return;
        if(Doors.Contains(type)) DoorKeys.Add(Add(component));
        else EncounterKeys.Add(Add(component));
        foreach(var transform in component.GetComponentsInChildren<Transform>(true)) if(SafeMotion(transform)) EncounterMotion[TransformKey(transform)]=transform;
    }
    // Hyperstrange components cache their LevelController in _parent the first time .Parent
    // is read; P1's native Survival results retry does so before a late joiner loads fresh,
    // so following it made the two Survival catalogues differ. Survival has no saves, so it
    // follows serialized references only; campaign keeps its manifests (saves store them).
    private bool _skipCachedParent;
    internal bool AuthoredMember(PropertyInfo p) => p.Name.StartsWith('_') && !(_skipCachedParent && p.Name == "_parent");
    internal static bool SafeMotion(Transform transform)
    {
        // Shared scene controllers can reference menu art. UI animation/input is
        // local; synchronizing those transforms would fight each peer's menus.
        // Runtime corpses and pooled scenery can already exist on P1 before P2
        // loads. They are not authored catalogue members; adapters assign IDs.
        for(var ancestor=transform;ancestor!=null;ancestor=ancestor.parent)
            if(ancestor.name.Contains("(Clone)",StringComparison.Ordinal) || ancestor.name.StartsWith("PBDMP ",StringComparison.Ordinal)) return false;
        if(transform.TryCast<RectTransform>()!=null || InParent<Canvas>(transform)!=null) return false;
        if(InParent<Pickup>(transform)!=null || InParent<Npc>(transform)!=null || InParent<Player>(transform)!=null) return false;
        var breaker=InParent<Hyperstrange.PBD.Contraptions.Breaker>(transform);
        return breaker?._parts==null || !transform.IsChildOf(breaker._parts.transform);
    }
    // Membership must not depend on what is active when the catalogue is built. The default
    // parent lookup skips inactive objects, so a pickup P1 already collected (or one a map
    // hides until later) used to join P1's catalogue but not a freshly loaded P2's.
    private static T? InParent<T>(Transform transform) where T:Component =>
        transform.GetComponentInParent(Il2CppType.Of<T>(),true)?.TryCast<T>();
    private static bool DoorName(Transform transform)
    {
        for(var current=transform;current!=null;current=current.parent)
            if(current.name.Contains("door",StringComparison.OrdinalIgnoreCase) || current.name.Contains("gate",StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
    private string Add(Component component)
    {
        var type=component.GetIl2CppType().FullName;
        var same=component.gameObject.GetComponents<Component>().Where(c=>c.GetIl2CppType().FullName==type).ToArray();
        var index=Array.FindIndex(same,c=>c.Pointer==component.Pointer);
        var identity=PathOf(component.transform)+"|"+type+"|"+index;
        var key=Hash(identity);
        if (Components.TryGetValue(key,out var prior) && prior.Pointer!=component.Pointer) throw new InvalidOperationException($"Duplicate world catalogue identity: {PathOf(component.transform)} {type} index={index}; prior={prior.Pointer}, next={component.Pointer}");
        Components[key]=component; PointerKeys[component.Pointer]=key; Identities[key]=identity; return key;
    }
    internal string? Key(Component? component) => component!=null && PointerKeys.TryGetValue(component.Pointer,out var key) ? key : null;
    // BigCat and PuppetPresident ship with the same EntityId GUID. The prefab
    // identity must distinguish them when a peer constructs a runtime actor.
    internal static string NpcAssetKey(NpcId asset)=>asset.ID+"|"+asset.name+"|"+asset._npcPrefab?.GetComponent<Npc>()?.GetIl2CppType().Name;
    internal static string TransformKey(Transform transform) => Hash(PathOf(transform)+"|transform");
    internal static string PathOf(Transform transform)
    {
        var segments=new List<string>();
        for (var current=transform;current!=null;current=current.parent)
        {
            var sibling=current.GetSiblingIndex();
            // Unity reports zero for multiple scene roots in this IL2CPP build.
            // The scene's root array supplies their actual authored order.
            if(current.parent==null && current.gameObject.scene.IsValid())
            {
                var roots=current.gameObject.scene.GetRootGameObjects();
                for(var i=0;i<roots.Length;i++) if(roots[i].transform.Pointer==current.Pointer) {sibling=i;break;}
            }
            segments.Add(sibling+":"+current.name);
        }
        segments.Reverse();var scene=transform.gameObject.scene;
        return scene.path+"|"+scene.name+"/"+string.Join("/",segments);
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    internal static string[] Save(Component component)
    {
        var saveable=component.TryCast<ISaveable>(); if (saveable==null) return Array.Empty<string>();
        var data=saveable.Save();
        var values=new List<string>();
        var ordered=new SortedDictionary<string,string>(StringComparer.Ordinal);
        foreach (var pair in data) ordered.Add(pair.Key,pair.Value);
        foreach (var pair in ordered) { values.Add(pair.Key); values.Add(pair.Value); }
        if (values.Count>32 || values.Any(v=>Encoding.UTF8.GetByteCount(v)>256))
            throw new InvalidOperationException("Native state needs a dedicated bounded adapter: "+component.GetIl2CppType().FullName);
        return values.ToArray();
    }
    internal static void Load(Component component,string[] values)
    {
        if (values.Length%2!=0) throw new InvalidDataException("Invalid native state pairs.");
        var saveable=component.TryCast<ISaveable>(); if (saveable==null) return;
        var data=new Il2CppSystem.Collections.Generic.Dictionary<string,string>();
        for (var i=0;i<values.Length;i+=2) { if (data.ContainsKey(values[i])) throw new InvalidDataException("Duplicate native state key."); data.Add(values[i],values[i+1]); }
        saveable.Load(data);
    }
}
