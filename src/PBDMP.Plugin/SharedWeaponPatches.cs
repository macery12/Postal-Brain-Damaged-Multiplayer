using HarmonyLib;
using Hyperstrange.PBD;

namespace PBDMP.Plugin;

internal static class SharedWeaponPatches
{
    internal static void Install(Harmony harmony)
    {
        foreach(var name in new[]{"PlayFireEvent","PlayFireFullChargeEvent","StartChargeLoop","StopChargeLoop","PlayChargeFullEvent"})
            harmony.Patch(AccessTools.Method(typeof(WeaponAction.FxModule),name),postfix:new(typeof(SharedWeaponPatches),nameof(Fx)));
        harmony.Patch(AccessTools.Method(typeof(Hyperstrange.PBD.LightningGun.LightningGunBeam),"SetActive"),postfix:new(typeof(SharedWeaponPatches),nameof(Beam)));
        harmony.Patch(AccessTools.Method(typeof(Hyperstrange.PBD.PlayerWeapons.PlayerWeaponNoWeapon),"AnimationEvent_MeleeStart"),postfix:new(typeof(SharedWeaponPatches),nameof(Melee)));
    }
    private static void Beam(Hyperstrange.PBD.LightningGun.LightningGunBeam __instance,bool __0)=>SharedWorld.Current?.BeamState(__instance,__0);
    private static void Melee(Hyperstrange.PBD.PlayerWeapons.PlayerWeaponNoWeapon __instance)=>SharedWorld.Current?.MeleeSwing(__instance);
    private static void Fx(WeaponAction.FxModule __instance,System.Reflection.MethodBase __originalMethod)
    {
        uint action=__originalMethod.Name switch {"PlayFireFullChargeEvent"=>1,"StartChargeLoop"=>2,"StopChargeLoop"=>3,"PlayChargeFullEvent"=>4,_=>0};
        SharedWorld.Current?.WeaponFx(__instance,action);
    }
}
