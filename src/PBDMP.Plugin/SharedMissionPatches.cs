using System.Reflection;
using HarmonyLib;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using Hyperstrange.PBD.Ready;
using UnityEngine;

namespace PBDMP.Plugin;

internal static class SharedMissionPatches
{
    internal static void Install(Harmony harmony)
    {
        // Audited substantive native bodies. Short forwarding stubs and shared
        // UnityEvent thunks cannot safely receive independent IL2CPP detours.
        var methods=new[]{(typeof(MultiStateListener),"OnStateChanged",1),(typeof(MultiBreakListener),"OnBreak",0),(typeof(EventListener),"OnEventRaised",1),
            (typeof(InteractListener),"OnInteractSuccess",1),(typeof(InteractableStateListener),"OnStateChanged",1),(typeof(ScriptToggler),"Toggle",1),
            (typeof(MultiRotater),"Rotate",1),(typeof(MultiRotater),"Rotate",2),(typeof(Elevator),"GoTo",1),(typeof(ElevatorButton),"OnStateChanged",1),
            (typeof(ElevatorButton),"CallElevator",0),(typeof(DamageTrigger),"OnSlowUpdate",1),(typeof(DamageTrigger),"OnTriggerEnter",1),
            (typeof(DamageTrigger),"OnTriggerExit",1),(typeof(Laser),"OnUpdate",1),(typeof(Laser),"DealDamage",0),(typeof(Explosive),"Explode",0),
            (typeof(Invoker),"Invoke",0),(typeof(ControlPanel),"Execute",0),(typeof(ControlPanel),"OnUpdate",1),(typeof(NeonSignController),"OnUpdate",1),(typeof(NeonSignController),"SetActiveState",1),
            (typeof(ForestBridge),"Fall",0),(typeof(Scaler),"Scale",0),(typeof(CeilingConveyor),"OnUpdate",1),(typeof(FuckOffCannon),"Execute",0),(typeof(FuckOffCannon),"Shoot",0)};
        foreach(var (type,name,count) in methods)
        {
            var method=type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly).Single(m=>m.Name==name && m.GetParameters().Length==count);
            harmony.Patch(method,prefix:new(typeof(SharedMissionPatches),nameof(HostOnly)));
        }
        harmony.Patch(AccessTools.Method(typeof(Trigger),nameof(Trigger.Disable)),postfix:new(typeof(SharedMissionPatches),nameof(DisabledTrigger)));
        harmony.Patch(AccessTools.Method(typeof(BreakableObject),"Hit"),prefix:new(typeof(SharedMissionPatches),nameof(BreakHit)));
        harmony.Patch(AccessTools.Method(typeof(CollisionBreakableObject),"OnTriggerEnter"),prefix:new(typeof(SharedMissionPatches),nameof(Collision)));
        harmony.Patch(AccessTools.Method(typeof(WireWallGate),"Interact"),prefix:new(typeof(SharedMissionPatches),nameof(Gate)));
        harmony.Patch(AccessTools.Method(typeof(PlayerTriggerListener),"OnPlayerEnter"),prefix:new(typeof(SharedMissionPatches),nameof(Listener)));
        harmony.Patch(AccessTools.Method(typeof(PlayerTriggerListener),"OnPlayerExit"),prefix:new(typeof(SharedMissionPatches),nameof(Listener)));
        harmony.Patch(AccessTools.Method(typeof(GameplayVolume),"OnTriggerEnter"),prefix:new(typeof(SharedMissionPatches),nameof(Volume)));
        foreach(var method in typeof(FallingPlatform).GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly).Where(m=>m.Name is "Fall" or "FallWithDelay"))
            harmony.Patch(method,prefix:new(typeof(SharedMissionPatches),nameof(Falling)));
    }
    private static bool HostOnly(Component __instance,System.Reflection.MethodBase __originalMethod)
    {
        var world=SharedWorld.Current;
        if(world==null) return true;
        // Damage triggers also occur in runtime prefabs outside the catalogue.
        if(__instance.TryCast<DamageTrigger>() is {} hazard && __originalMethod.Name=="OnSlowUpdate")
        {
            if(!world.PermitWorldHazard) return false;
            world.ReconcileRemoteHazardContact(hazard);return true;
        }
        if(!world.Active || !world.OwnsMission(__instance)) return true;
        if(__instance.TryCast<Scaler>() is {} scaler && __originalMethod.Name=="Scale") return !world.StartScale(scaler);
        if(world.Client) return false;
        return !world.RestoringNative || __instance.GetIl2CppType().Name is "Mover" or "Rotater" or "MultiRotater" or "Elevator";
    }
    private static void DisabledTrigger(Trigger __instance)=>SharedWorld.Current?.RetireTriggerHint(__instance);
    private static bool Collision(CollisionBreakableObject __instance,Collider __0)=>SharedWorld.Current?.RequestCollisionBreak(__instance,__0)!=true;
    private static bool Volume(GameplayVolume __instance)
    {
        if(SharedWorld.Current is not {Active:true} world || !world.OwnsMission(__instance)) return true;
        return __instance.kind!=VolumeKind.Win && !world.Client && __instance.isActiveAndEnabled;
    }
    private static bool Falling(FallingPlatform __instance,System.Reflection.MethodBase __originalMethod)=>SharedWorld.Current?.MechanismFall(__instance,__originalMethod.Name)!=true;
    private static bool Listener(PlayerTriggerListener __instance,System.Reflection.MethodBase __originalMethod)
    {
        if(SharedWorld.Current is not {Client:true,Active:true} world || !world.OwnsMission(__instance.GetComponent<PlayerTrigger>())) return true;
        var events=__originalMethod.Name=="OnPlayerExit"?__instance._onTriggerExitEvents:__instance._onTriggerEvents;
        if(events!=null) for(var i=0;i<events.GetPersistentEventCount();i++)
            if(events.GetPersistentTarget(i)?.TryCast<TutorialId>() is {} tutorial && events.GetPersistentMethodName(i)=="Show") tutorial.Show(Player.Instance);
                else if(events.GetPersistentTarget(i)?.TryCast<Hyperstrange.Base.LocalizedString>() is {} text && events.GetPersistentMethodName(i)=="ShowNotification") text.ShowNotification(Player.Instance);
        return false;
    }
    private static bool BreakHit(BreakableObject __instance,HitData __0)
    {
        if(SharedWorld.Current is not {Client:true,Active:true} world || !world.OwnsMission(__instance)) return true;
        if(__0.projectile==null && !__0.isExplosion && __0.dealer?.Pointer==Player.Instance?.Pointer) world.ClientMissionHit(__instance,__0);
        return false;
    }
    private static bool Gate(WireWallGate __instance,Player __0)
    {
        if(SharedWorld.Current is not {Client:true,Active:true} world || !world.OwnsMission(__instance)) return true;
        world.RequestMissionGate(__instance,__0);return false;
    }
}
