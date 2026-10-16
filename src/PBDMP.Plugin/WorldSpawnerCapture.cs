using System.Reflection;
using HarmonyLib;
using Hyperstrange.PBD.Contraptions;
using UnityEngine;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    private readonly Dictionary<IntPtr,IntPtr> _spawningProps=new();
    internal void ResetPropSpawnersForRestore()
    {
        foreach(var key in _catalogue!.SpawnerKeys)
            if(_catalogue.Components[key].TryCast<PhysicalObjectSpawner>() is {} source)
            {source.StopAllCoroutines();source.Load(new());}
    }
    internal bool PendingPropSpawns=>_spawningProps.Count!=0;
    internal void PropSpawnRequested(PhysicalObjectSpawner source)
    {if(Active && !Client && source.gameObject.activeInHierarchy && IsSharedSpawner(source)) _spawningProps[source.Pointer]=source.Pointer;}
    internal void PropSpawnStarted(PhysicalObjectSpawner source,IntPtr routine)
    {if(Active && !Client && IsSharedSpawner(source)) {_spawningProps.Remove(source.Pointer);_spawningProps[routine]=source.Pointer;}}
    internal void PropSpawnEnded(IntPtr routine)=>_spawningProps.Remove(routine);
    internal void PropSpawnCancelled(PhysicalObjectSpawner source)
    {foreach(var pair in _spawningProps.Where(p=>p.Value==source.Pointer).ToArray()) _spawningProps.Remove(pair.Key);}
}

internal static class WorldSpawnerCapturePatches
{
    private static Type _routineType=null!;
    private static PropertyInfo _routineSource=null!;
    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(PhysicalObjectSpawner),"SpawnPhysicalObjects"),prefix:new(typeof(WorldSpawnerCapturePatches),nameof(Requested)));
        // The caller inlines iterator creation; the small factory is not used.

        // This native coroutine's substantive MoveNext owns the complete batch
        // and its final push explosion. Wait for it before consistent capture.
        var type=typeof(PhysicalObjectSpawner).GetNestedTypes(BindingFlags.Public|BindingFlags.NonPublic).Single(t=>t.Name.Contains("SpawnRoutine",StringComparison.Ordinal));
        _routineType=type;_routineSource=type.GetProperties(BindingFlags.Public|BindingFlags.Instance).Single(p=>p.PropertyType==typeof(PhysicalObjectSpawner) && p.CanRead);
        harmony.Patch(AccessTools.Method(type,"MoveNext"),prefix:new(typeof(WorldSpawnerCapturePatches),nameof(Advancing)),postfix:new(typeof(WorldSpawnerCapturePatches),nameof(Advanced)),finalizer:new(typeof(WorldSpawnerCapturePatches),nameof(Failed)));
        harmony.Patch(AccessTools.Method(typeof(PhysicalObjectSpawner),"Load"),prefix:new(typeof(WorldSpawnerCapturePatches),nameof(Cancelled)),postfix:new(typeof(WorldSpawnerCapturePatches),nameof(Pooled)));
    }
    private static void Requested(PhysicalObjectSpawner __instance)=>SharedWorld.Current?.PropSpawnRequested(__instance);
    private static void Advancing(Il2CppSystem.Object __instance)
    {
        if(SharedWorld.Current is not {Active:true,Client:false} world) return;
        var wrapper=Activator.CreateInstance(_routineType,new object[]{__instance.Pointer});
        if(_routineSource.GetValue(wrapper) is PhysicalObjectSpawner source) world.PropSpawnStarted(source,__instance.Pointer);
    }
    private static void Advanced(Il2CppSystem.Object __instance,bool __result)
    {if(!__result) SharedWorld.Current?.PropSpawnEnded(__instance.Pointer);}
    private static void Failed(Il2CppSystem.Object __instance,Exception? __exception)
    {if(__exception!=null) SharedWorld.Current?.PropSpawnEnded(__instance.Pointer);}
    private static void Cancelled(PhysicalObjectSpawner __instance)
    {
        if(SharedWorld.Current is not {Active:true} world || !world.IsSharedSpawner(__instance)) return;
        __instance.StopAllCoroutines();world.PropSpawnCancelled(__instance);
    }
    private static void Pooled(PhysicalObjectSpawner __instance)
    {
        // Native Load returns each entry to the pool but leaves the list intact.
        // A restored/reused pool object must have exactly one current owner entry.
        if(SharedWorld.Current is {Active:true} world && world.IsSharedSpawner(__instance)) __instance._spawnedObjects.Clear();
    }
}
