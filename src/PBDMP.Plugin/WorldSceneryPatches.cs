using HarmonyLib;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using UnityEngine;

namespace PBDMP.Plugin;

internal static class WorldSceneryPatches
{
    internal static void Install(Harmony h)
    {
        h.Patch(AccessTools.Method(typeof(GlassObject),"Hit"),prefix:new(typeof(WorldSceneryPatches),nameof(Hit)));
        // PhysicalObject.Hit and ApplyForce share one substantive native body.
        h.Patch(AccessTools.Method(typeof(PhysicalObject),"Hit"),prefix:new(typeof(WorldSceneryPatches),nameof(Hit)));
        h.Patch(AccessTools.Method(typeof(PhysicalObject),"Push"),prefix:new(typeof(WorldSceneryPatches),nameof(Push)));
        h.Patch(AccessTools.Method(typeof(PhysicalObject),"OnSlowUpdate"),prefix:new(typeof(WorldSceneryPatches),nameof(PhysicsUpdate)));
        h.Patch(AccessTools.Method(typeof(PhysicalObject),"OnFixedUpdate"),prefix:new(typeof(WorldSceneryPatches),nameof(PhysicsUpdate)));
        h.Patch(AccessTools.Method(typeof(PhysicalObject),"OnOptimizeUpdate"),prefix:new(typeof(WorldSceneryPatches),nameof(Optimize)));
        h.Patch(AccessTools.Method(typeof(PhysicalObjectSpawner),"SpawnPhysicalObjects"),prefix:new(typeof(WorldSceneryPatches),nameof(Spawner)));
        h.Patch(AccessTools.Method(typeof(Breaker),"Break"),prefix:new(typeof(WorldSceneryPatches),nameof(BeforeBreak)),postfix:new(typeof(WorldSceneryPatches),nameof(Broke)));
        h.Patch(AccessTools.Method(typeof(MovingPlatformTrigger),"Move"),prefix:new(typeof(WorldSceneryPatches),nameof(Carry)));
        h.Patch(AccessTools.Method(typeof(Pusher),"Push"),prefix:new(typeof(WorldSceneryPatches),nameof(HostMechanism)));
        h.Patch(AccessTools.Method(typeof(LeonDuskSwitch),"TriggerSwitch"),prefix:new(typeof(WorldSceneryPatches),nameof(HostMechanism)));
        h.Patch(AccessTools.Method(typeof(LeonDuskSwitch),"OnSwitch_AnimationCallback"),prefix:new(typeof(WorldSceneryPatches),nameof(HostMechanism)));
        h.Patch(AccessTools.Method(typeof(PawShockwaveTrigger),"Hit"),prefix:new(typeof(WorldSceneryPatches),nameof(HostMechanism)));
    }
    private static bool Hit(Component __instance,HitData __0)=>SharedWorld.Current?.RequestSceneryHit(__instance,__0)!=true;
    private static bool Push(PhysicalObject __instance,Vector3 __0)=>SharedWorld.Current?.RequestPhysicalPush(__instance,__0)!=true;
    private static bool PhysicsUpdate(PhysicalObject __instance)=>SharedWorld.Current is not {Client:true,Active:true} w || !w.SharedPhysics(__instance);
    private static bool Optimize(PhysicalObject __instance,ref bool __0,ref bool __1)
    {
        if(SharedWorld.Current is {Active:true,Client:true} guest && guest.SharedPhysics(__instance)) return false;
        if(SharedWorld.Current is {Active:true,Client:false} w && w.Remote?.IsAlive==true &&
            Vector3.Distance(__instance.transform.position,w.Remote.CenterOfMass)<60)
        {__0=false;__1=false;}
        return true;
    }
    private static bool Spawner(Component __instance)=>SharedWorld.Current is not {Active:true,Client:true} w || !w.IsSharedSpawner(__instance);
    private static bool HostMechanism(Component __instance)=>SharedWorld.Current is not {Active:true} w || !w.OwnsMission(__instance) || !w.Client && !w.RestoringNative;
    private static bool BeforeBreak(Breaker __instance)=>SharedWorld.Current?.SuppressRestoredBreak(__instance)!=true;
    private static void Broke(Breaker __instance,Vector3 __0,Vector3 __1)=>SharedWorld.Current?.SceneryBroke(__instance,__0,__1);
    private static bool Carry(MovingPlatformTrigger __instance)=>SharedWorld.Current?.AllowPlatformCarry(__instance)!=false;
}
