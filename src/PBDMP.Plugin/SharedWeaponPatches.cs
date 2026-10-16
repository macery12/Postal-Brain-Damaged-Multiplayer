using HarmonyLib;
using Hyperstrange.PBD;

namespace PBDMP.Plugin;

internal static class SharedWeaponPatches
{
    internal static void Install(Harmony harmony)
    {
        foreach(var name in new[]{"PlayFireEvent","PlayFireFullChargeEvent","StartChargeLoop","StopChargeLoop","PlayChargeFullEvent"})
            harmony.Patch(AccessTools.Method(typeof(WeaponAction.FxModule),name),postfix:new(typeof(SharedWeaponPatches),nameof(Fx)));
        // Awake runs for each real pooled renderer at first activation. The
        // callback accepts Component because IL2CPP can share small native bodies.
        foreach(var type in new[]{typeof(Hyperstrange.PBD.LightningGun.LightningGunBeamRenderer),typeof(Hyperstrange.Base.LineController),typeof(PeeStreamRenderer)})
            harmony.Patch(AccessTools.Method(type,"Awake"),postfix:new(typeof(SharedWeaponPatches),nameof(StreamCreated)));
        harmony.Patch(AccessTools.Method(typeof(Hyperstrange.PBD.LightningGun.LightningGunBeam),"SetActive"),postfix:new(typeof(SharedWeaponPatches),nameof(Beam)));
        harmony.Patch(AccessTools.Method(typeof(Hyperstrange.PBD.PlayerWeapons.PlayerWeaponNoWeapon),"AnimationEvent_MeleeStart"),postfix:new(typeof(SharedWeaponPatches),nameof(Melee)));
    }
    private static void StreamCreated(UnityEngine.Component __instance)=>SharedWorld.Current?.TrackStream(__instance);
    private static void Beam(Hyperstrange.PBD.LightningGun.LightningGunBeam __instance,bool __0)=>SharedWorld.Current?.BeamState(__instance,__0);
    private static void Melee(Hyperstrange.PBD.PlayerWeapons.PlayerWeaponNoWeapon __instance)=>SharedWorld.Current?.MeleeSwing(__instance);
    private static void Fx(WeaponAction.FxModule __instance,System.Reflection.MethodBase __originalMethod)
    {
        uint action=__originalMethod.Name switch {"PlayFireFullChargeEvent"=>1,"StartChargeLoop"=>2,"StopChargeLoop"=>3,"PlayChargeFullEvent"=>4,_=>0};
        SharedWorld.Current?.WeaponFx(__instance,action);
    }
}
