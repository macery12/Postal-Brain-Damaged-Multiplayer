using HarmonyLib;
using Hyperstrange.PBD.Contraptions;

namespace PBDMP.Plugin;

internal static class SharedLootPatches
{
    internal static void Install(Harmony harmony)
    {harmony.Patch(AccessTools.Method(typeof(Pickup),nameof(Pickup.Drop)),postfix:new(typeof(SharedLootPatches),nameof(Dropped)));}
    private static void Dropped(Pickup __instance)=>SharedWorld.Current?.PickupDropped(__instance);
}
