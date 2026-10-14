using HarmonyLib;
using Hyperstrange.PBD;
using Hyperstrange.PBD.PlayerWeapons;
using UnityEngine;

namespace PBDMP.Plugin;

internal static class RemoteWeaponPatches
{
    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(PlayerWeaponGatlingGun),"StartHomingMode"),prefix:new(typeof(RemoteWeaponPatches),nameof(TrackerStart)));
        harmony.Patch(AccessTools.Method(typeof(PlayerWeaponGatlingGun),"EndHomingMode"),prefix:new(typeof(RemoteWeaponPatches),nameof(TrackerEnd)));
        harmony.Patch(AccessTools.Method(typeof(PlayerWeaponCatCanon),"OnCatReturned"),prefix:new(typeof(RemoteWeaponPatches),nameof(CatReturn)));
        harmony.Patch(AccessTools.Method(typeof(PlayerWeaponCatCanon),"RecallAllPrimedCats"),prefix:new(typeof(RemoteWeaponPatches),nameof(CatRecall)));
        harmony.Patch(AccessTools.Method(typeof(PlayerWeaponCatCanon),"OnCatAttachedToWall"),postfix:new(typeof(RemoteWeaponPatches),nameof(CatAttached)));
        harmony.Patch(AccessTools.Method(typeof(ProjectileCat),"Kill"),prefix:new(typeof(RemoteWeaponPatches),nameof(CatKill)));
    }
    private static bool TrackerStart(PlayerWeaponGatlingGun __instance,ProjectileTracker __0,RaycastHit __1)
    {var world=SharedWorld.Current;if(world?.IsRemoteWeapon(__instance)!=true) return true;world.TrackerMode(__instance,__0,__1);return false;}
    private static bool TrackerEnd(PlayerWeaponGatlingGun __instance)
    {if(SharedWorld.Current?.IsRemoteWeapon(__instance)!=true) return true;__instance._currentTracker=null;return false;}
    private static bool CatReturn(PlayerWeaponCatCanon __instance,ProjectileCat __0)
    {var world=SharedWorld.Current;if(world?.IsRemoteWeapon(__instance)!=true) return true;world.CatReturned(__instance,__0);return false;}
    private static bool CatRecall(PlayerWeaponCatCanon __instance)
    {SharedWorld.Current?.CatRecall(__instance);return true;}
    private static void CatAttached(ProjectileCat __0)=>SharedWorld.Current?.CatAttached(__0);
    private static void CatKill(ProjectileCat __instance)=>SharedWorld.Current?.ClientCatKilled(__instance);
}
