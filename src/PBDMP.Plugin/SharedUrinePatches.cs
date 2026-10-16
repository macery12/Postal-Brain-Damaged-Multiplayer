using HarmonyLib;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using Hyperstrange.PBD.NpcComponents;
using UnityEngine;

namespace PBDMP.Plugin;

internal static class SharedUrinePatches
{
    internal static bool DiagnosticHoldTankMotion;
    internal static void Install(Harmony harmony)
    {
        // Piss on FuseBox/ButtonShootable is a tiny native tail jump or inlined.
        // Patch the substantive interaction bodies reached by native collisions.
        foreach(var pair in new[]{(typeof(FuseBox),"Execute"),(typeof(ButtonShootable),"Interact")})
            harmony.Patch(AccessTools.Method(pair.Item1,pair.Item2),prefix:new(typeof(SharedUrinePatches),nameof(Activate)));
        foreach(var type in new[]{typeof(MetalButton),typeof(Toilet),typeof(PissListener)})
            harmony.Patch(AccessTools.Method(type,"Piss"),prefix:new(typeof(SharedUrinePatches),nameof(Piss)));
        foreach(var type in new[]{typeof(PissFillListenerMovingObject),typeof(PissFillListenerScalingObject)})
            harmony.Patch(AccessTools.Method(type,"OnFillChanged"),prefix:new(typeof(SharedUrinePatches),nameof(FillSurface)));
        foreach(var type in new[]{typeof(PissFiller),typeof(PissTank)})
            harmony.Patch(AccessTools.Method(type,"OnPiss"),prefix:new(typeof(SharedUrinePatches),nameof(Fill)));
        foreach(var type in new[]{typeof(ExplosiveBarrel),typeof(BurnableObject)})
            harmony.Patch(AccessTools.Method(type,"FlameTick"),prefix:new(typeof(SharedUrinePatches),nameof(WorldFlame)));
        harmony.Patch(AccessTools.Method(typeof(ExplosiveBarrel),"Hit"),prefix:new(typeof(SharedUrinePatches),nameof(BarrelHit)));
        harmony.Patch(AccessTools.Method(typeof(PeeCollisionParticle),"HandleCollision"),prefix:new(typeof(SharedUrinePatches),nameof(FirePeeCollision)));
        // Weak-spot Piss builds a frame-timed HitData and reaches the existing
        // actor-part Hit guard. Keep its native damage calculation on the sender.
        harmony.Patch(AccessTools.Method(typeof(ProjectileFireBall),"Piss"),prefix:new(typeof(SharedUrinePatches),nameof(FireBall)),postfix:new(typeof(SharedUrinePatches),nameof(Extinguished)));
        harmony.Patch(AccessTools.Method(typeof(PissTank),"OnUpdate"),prefix:new(typeof(SharedUrinePatches),nameof(TankUpdate)));
        harmony.Patch(AccessTools.Method(typeof(Toilet),"OnInteractSuccess"),prefix:new(typeof(SharedUrinePatches),nameof(Flush)),postfix:new(typeof(SharedUrinePatches),nameof(FlushFx)));
        foreach(var pair in new[]{(typeof(Laser),"OnUpdate"),(typeof(Laser),"DealDamage"),(typeof(RocketSequence),"OnFall_AnimationCallback"),(typeof(RocketSequence),"ShootRocket"),(typeof(RocketSequence),"StartSequence"),(typeof(RailSwitch),"OnTriggerEnter"),(typeof(Rail),"OnRailWagonUpdated")})
            harmony.Patch(AccessTools.Method(pair.Item1,pair.Item2),prefix:new(typeof(SharedUrinePatches),nameof(Consequence)));
        harmony.Patch(AccessTools.Method(typeof(RailWagon),"OnUpdate"),prefix:new(typeof(SharedUrinePatches),nameof(WagonUpdate)));
        harmony.Patch(AccessTools.Method(typeof(RailWagon),"AttachPlayer",new[]{typeof(Player)}),postfix:new(typeof(SharedUrinePatches),nameof(Boarded)));
        // KickPlayerOff and OnPlayerJumpedOff share the same substantive body.
        harmony.Patch(AccessTools.Method(typeof(RailWagon),"OnPlayerJumpedOff"),postfix:new(typeof(SharedUrinePatches),nameof(LeftWagon)));
        harmony.Patch(AccessTools.Method(typeof(FMODEvent),"PlayOneShot",new[]{typeof(Vector3)}),prefix:new(typeof(SharedUrinePatches),nameof(PermitSound)),postfix:new(typeof(SharedUrinePatches),nameof(Sound)));
        harmony.Patch(AccessTools.Method(typeof(FMODEvent),"PlayOneShot",new[]{typeof(GameObject)}),prefix:new(typeof(SharedUrinePatches),nameof(PermitSound)),postfix:new(typeof(SharedUrinePatches),nameof(ObjectSound)));
    }
    private static bool WorldFlame(Component __instance,IAiActor __0,float __1,float __2)
    {var world=SharedWorld.Current;if(__instance.TryCast<ExplosiveBarrel>() is {} barrel) return world?.QueueBarrelFlame(barrel,__0,__1,__2)!=true;return world?.ClientWorldFlame(__instance,__0,__1,__2)!=true;}
    private static bool BarrelHit(ExplosiveBarrel __instance,HitData __0)=>SharedWorld.Current?.QueueBarrelHit(__instance,__0)!=true;
    private static void FirePeeCollision(PeeCollisionParticle __instance,RaycastHit __0)
    {
        if(__instance._owner?.Pointer!=Player.Instance?.Pointer || __instance._playerWeaponModelDong?.PeeType!=PeeType.Fire) return;
        // Some powered-urine collision paths burn/soak before the IPissable
        // forwarding stub. Preserve the fuse's urine activation as well.
        var box=__0.collider?.GetComponentInParent<FuseBox>();
        if(box!=null) SharedWorld.Current?.ClientUrine(box);
    }
    private static bool Activate(Component __instance)=>SharedWorld.Current?.ClientUrine(__instance)!=true;
    private static bool Piss(Component __instance,PissData __0)
    {
        if(__instance.TryCast<MetalButton>() is {} metal && SharedWorld.Current is {Client:true,Active:true,Applying:false} world)
        {
            // Native MetalButton.Piss casts the owner to a real Player. Use the
            // requester-owned item/door handshake instead of passing a proxy.
            if(__0.owner?.Pointer==Player.Instance?.Pointer) world.RequestDoor(metal,Player.Instance!);
            return false;
        }
        return SharedWorld.Current?.ClientUrine(__instance,70,__0.stagger)!=true;
    }
    private static bool FillSurface(Component __instance)=>SharedWorld.Current?.PermitFillDrivenSurface(__instance)!=false;
    private static bool Fill(Component __instance,PissData __0)=>SharedWorld.Current?.ClientUrine(__instance,71,__0.stagger)!=true;
    private static bool FireBall(ProjectileFireBall __instance,PissData __0)=>SharedWorld.Current?.ClientProjectileUrine(__instance,__0)!=true;
    private static void Extinguished(ProjectileFireBall __instance)=>SharedWorld.Current?.FireBallExtinguished(__instance);
    private static bool TankUpdate(Component __instance)
    {
        if(DiagnosticHoldTankMotion && Environment.GetEnvironmentVariable("PBDMP_URINE_TEST")=="1") return false;
        return SharedWorld.Current is not {Client:true,Active:true} world || !world.OwnsUrineConsequence(__instance);
    }
    private static bool Flush()=>SharedWorld.Current is not {Client:true,Active:true};
    private static bool Consequence(Component __instance,System.Reflection.MethodBase __originalMethod)
    {
        var world=SharedWorld.Current;
        if(__instance.TryCast<RocketSequence>() is {} rocket && __originalMethod.Name=="ShootRocket") return world?.StartRocketLaunch(rocket)!=true;
        if(world is not {Active:true} || !world.OwnsUrineConsequence(__instance)) return true;
        return !world.Client && (!world.RestoringNative || __instance.TryCast<RocketSequence>()==null);
    }
    private static bool WagonUpdate(RailWagon __instance)=>SharedWorld.Current?.UpdateReplicaWagon(__instance)!=true;
    private static void Boarded(RailWagon __instance,Player __0) {if(__0?.Pointer==Player.Instance?.Pointer) SharedWorld.Current?.UrineWagonBoarded(__instance,true);}
    private static void LeftWagon(RailWagon __instance)=>SharedWorld.Current?.UrineWagonBoarded(__instance,false);
    private static void FlushFx(Toilet __instance)=>SharedWorld.Current?.ToiletFlushPresentation(__instance);
    private static bool PermitSound(FMODEvent __instance)=>SharedWorld.Current?.PermitWorldOneShot(__instance)!=false;
    private static void Sound(FMODEvent __instance,Vector3 __0)=>SharedWorld.Current?.UrineSound(__instance,__0);
    private static void ObjectSound(FMODEvent __instance,GameObject __0) {if(__0!=null) SharedWorld.Current?.UrineSound(__instance,__0.transform.position);}
}
