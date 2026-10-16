using HarmonyLib;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;

namespace PBDMP.Plugin;

internal static class CoopSavePatches
{
    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(LevelController),nameof(LevelController.AutoSave)),prefix:new(typeof(CoopSavePatches),nameof(Auto)));
        harmony.Patch(AccessTools.Method(typeof(LevelController),nameof(LevelController.QuickSave)),prefix:new(typeof(CoopSavePatches),nameof(Quick)));
        harmony.Patch(AccessTools.Method(typeof(LevelController),nameof(LevelController.QuickLoad)),prefix:new(typeof(CoopSavePatches),nameof(LoadQuick)));
        harmony.Patch(AccessTools.Method(typeof(PlayerTriggerCheckpointComponent),"OnPlayerEnter"),prefix:new(typeof(CoopSavePatches),nameof(Checkpoint)));
        harmony.Patch(AccessTools.Method(typeof(LevelId),nameof(LevelId.UpdateMetaData)),postfix:new(typeof(CoopSavePatches),nameof(Campaign)));
        harmony.Patch(AccessTools.Method(typeof(Pickup),"PickupComplete"),prefix:new(typeof(CoopSavePatches),nameof(BeforeCollected)),postfix:new(typeof(CoopSavePatches),nameof(Collected)));
    }
    private static void Campaign(LevelId __instance)=>CoopSaveService.Current?.CampaignCompleted(__instance);
    private static void Auto()=>CoopSaveService.Current?.AutoSave();
    private static void Quick() {if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PBDMP_TRAVEL_TEST")) || Environment.GetEnvironmentVariable("PBDMP_SAVE_TEST")=="1") CoopSaveService.Current?.QuickSave();}
    private static bool LoadQuick()
    {if(CoopSaveService.Current is not {} saves) return true;saves.RequestLoad("quick");return false;}
    private static bool Checkpoint(PlayerTriggerCheckpointComponent __instance,Player __0)
    {if(CoopSaveService.Current is not {} saves) return true;__0.SaveLastCheckpointPosition();saves.Checkpoint(__instance);return false;}
    private static void BeforeCollected(Pickup __instance,out string? __state)=>__state=CoopSaveService.Current?.CollectionKey(__instance);
    private static void Collected(string? __state)=>CoopSaveService.Current?.CollectedKey(__state);
}
