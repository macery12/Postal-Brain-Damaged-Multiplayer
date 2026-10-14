using System.Reflection;
using HarmonyLib;
using Hyperstrange.PBD;
using UnityEngine;

namespace PBDMP.Plugin;

internal static class SharedProjectilePatches
{
    internal static void Install(Harmony harmony)
    {
        foreach(var type in typeof(Projectile).Assembly.GetTypes())
        {
            if(type.ContainsGenericParameters) continue;
            if(typeof(ProjectileModel).IsAssignableFrom(type))
            {
                var method=type.GetMethod("CreateInstance",BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly,null,new[]{typeof(Vector3),typeof(Quaternion),typeof(Vector3),typeof(IAiActor),typeof(EntityId)},null);
                if(method!=null && !method.IsAbstract && type!=typeof(ProjectileModel)) harmony.Patch(method,postfix:new(typeof(SharedProjectilePatches),nameof(Created)));
            }
            if(typeof(Projectile).IsAssignableFrom(type))
            {
                var method=type.GetMethod("OnUpdate",BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly,null,new[]{typeof(float)},null);
                if(method!=null) harmony.Patch(method,prefix:new(typeof(SharedProjectilePatches),nameof(Update)));
                var fixedUpdate=type.GetMethod("OnFixedUpdate",BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly,null,new[]{typeof(float)},null);
                if(fixedUpdate!=null) harmony.Patch(fixedUpdate,prefix:new(typeof(SharedProjectilePatches),nameof(Update)));
            }
        }
        harmony.Patch(AccessTools.Method(typeof(Projectile),"HandleColliderHit"),postfix:new(typeof(SharedProjectilePatches),nameof(Impact)));
        harmony.Patch(AccessTools.Method(typeof(SolidManager),"SpawnSolidImpact"),prefix:new(typeof(SharedProjectilePatches),nameof(SolidImpact)));
        harmony.Patch(AccessTools.Method(typeof(Explosion),"Invoke"),prefix:new(typeof(SharedProjectilePatches),nameof(Explosion)));
        harmony.Patch(AccessTools.Method(typeof(Projectile),"OnEnable"),postfix:new(typeof(SharedProjectilePatches),nameof(Enabled)));
        harmony.Patch(AccessTools.Method(typeof(Projectile),"SetOwnerAndOriginEntity"),postfix:new(typeof(SharedProjectilePatches),nameof(Initialized)));
        harmony.Patch(AccessTools.Method(typeof(ProjectileHoming),"SetTarget"),postfix:new(typeof(SharedProjectilePatches),nameof(HomingTarget)));
        harmony.Patch(AccessTools.Method(typeof(ProjectileModelDildo),"CreateInstance",new[]{typeof(Vector3),typeof(Quaternion),typeof(Vector3),typeof(IAiActor),typeof(EntityId),typeof(float)}),postfix:new(typeof(SharedProjectilePatches),nameof(CreatedCharged)));
    }
    private static void Created(ProjectileModel __instance,Projectile __result,Vector3 __0,Quaternion __1,Vector3 __2,IAiActor __3,EntityId __4)
    {SharedWorld.Current?.CapturedShot(__instance,__result,__0,__1,__2,__3,__4);}
    private static bool Update(Projectile __instance)=>SharedWorld.Current?.FrozenClientProjectile(__instance)!=true;
    private static void Impact(Projectile __instance,RaycastHit __0)=>SharedWorld.Current?.ProjectileImpact(__instance,__0);
    private static bool SolidImpact(SolidObjectModel __0,RaycastHit __1)=>SharedWorld.Current?.SolidImpact(__0,__1)!=false;
    private static bool Explosion(Explosion __instance,Vector3 __0,IAiActor __1,EntityId __2)=>SharedWorld.Current?.ExplosionInvoked(__instance,__0,__1,__2)!=false;
    private static void Initialized(Projectile __instance)=>SharedWorld.Current?.NativeProjectileInitialized(__instance);
    private static void Enabled(Projectile __instance)=>SharedWorld.Current?.NativeProjectileEnabled(__instance);
    private static void HomingTarget(ProjectileHoming __instance,Transform __0)=>SharedWorld.Current?.ClientHomingTarget(__instance,__0);
    private static void CreatedCharged(ProjectileModelDildo __instance,Projectile __result,Vector3 __0,Quaternion __1,Vector3 __2,IAiActor __3,EntityId __4,float __5)
    {SharedWorld.Current?.CapturedShot(__instance,__result,__0,__1,__2,__3,__4,__5);}
}
