using System.Reflection;
using HarmonyLib;
using Hyperstrange.Base;
using Hyperstrange.PBD;
using Hyperstrange.PBD.UI.Menus;
using Il2CppInterop.Runtime;

namespace PBDMP.Plugin;

internal static class SessionPatches
{
    internal static bool Protect => SessionLifecycle.Current != null || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PBDMP_TRAVEL_TEST")) ||
        Environment.GetEnvironmentVariable("PBDMP_COMBAT_PROBE") == "1" || SurvivalInventory.Enabled;
    internal static int BlockedSaves;
    internal static void Install(Harmony harmony)
    {
        foreach (var patch in typeof(SessionPatches).GetNestedTypes(BindingFlags.NonPublic))
            harmony.CreateClassProcessor(patch).Patch();
    }
    [HarmonyPatch(typeof(UnityEngine.Cursor), "set_lockState")]
    private static class UnattendedCursorLock
    {
        static void Prefix(ref UnityEngine.CursorLockMode __0)
        {if(DiagnosticCursor.KeepFree) __0=UnityEngine.CursorLockMode.None;}
    }
    [HarmonyPatch(typeof(UnityEngine.Cursor), "set_visible")]
    private static class UnattendedCursorVisibility
    {
        static void Prefix(ref bool __0)
        {if(DiagnosticCursor.KeepFree) __0=true;}
    }
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.LoadLevel))]
    private static class LoadLevel
    {
        static bool Prefix(LevelId levelId,bool loaded)
        {
            var life = SessionLifecycle.Current;
            if (life == null) return true;
            if (!life.PermitTravel()) return false;
            if (!SessionLifecycle.Supported(levelId)) { life.Explain(SessionLifecycle.UnsupportedContent); return false; }
            if(!loaded && levelId.VideoData!=null && !SettingsManager.GeneralSettings.SkipCutscenes && !DebugManager.NoVideo) life.ExpectStory(levelId.VideoData);
            life.BeginHost(PBDMP.Network.TravelAction.Load, levelId); return true;
        }
    }
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.Load))]
    private static class Load
    {
        static bool Prefix(SaveData saveData)
        {
            var life = SessionLifecycle.Current;
            if (life == null) return true;
            if (saveData != null) return false;
            if (!life.PermitTravel()) return false;
            life.BeginHost(PBDMP.Network.TravelAction.Restart, LevelManager.CurrentLevel); return true;
        }
    }
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.OpenTitle))]
    private static class Title
    {
        static bool Prefix()
        {
            var life = SessionLifecycle.Current;
            if (life == null) return true;
            if (!life.PermitTravel()) return false;
            life.BeginHost(PBDMP.Network.TravelAction.Menu, null); return true;
        }
    }
    [HarmonyPatch]
    private static class ClientTravel
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(GameManager), nameof(GameManager.LoadNextLevel));
            yield return AccessTools.Method(typeof(LevelController), nameof(LevelController.QuickLoad));
            yield return AccessTools.Method(typeof(LevelController), nameof(LevelController.Load));
        }
        static bool Prefix() => SessionLifecycle.Current?.PermitTravel() != false;
    }
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.EndGame))]
    private static class ClientCredits {static bool Prefix()=>SessionLifecycle.Current?.PermitTravel()!=false;}

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.LoadModLevel))]
    private static class ModLevel { static bool Prefix() => SessionLifecycle.Current == null; }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.OpenLoading))]
    private static class OpenLoading
    {
        static bool Prefix()
        {
            var life = SessionLifecycle.Current;
            if (life == null) return true;
            if (!life.PermitTravel()) return false;
            if (!SessionLifecycle.Supported(LevelManager.CurrentLevel)) return false;
            // NextLevelMenu/cutscenes use OpenLoading directly, bypassing LoadLevel.
            if (!life.Bypass && (life.NativeReady || life.State.Level != LevelManager.CurrentLevel.ID))
                life.BeginHost(PBDMP.Network.TravelAction.Load, LevelManager.CurrentLevel);
            return true;
        }
    }

    [HarmonyPatch(typeof(LevelController), "EndLevel")]
    private static class ClientEnd
    {
        static bool Prefix(LevelController __instance,LevelController.EndLevelType __0,out bool __state)
        {
            __state=false;
            var life = SessionLifecycle.Current;
            if (life == null) return true;
            if (__0 == LevelController.EndLevelType.DEATH) { if (SurvivalPatches.Ending) return true; life.Death(); return false; }
            if(__0==LevelController.EndLevelType.FINISH && SharedWorld.Current is {} world)
            {var accepted=world.FinishMission(__instance);__state=accepted && !world.Client;return accepted;}
            return !life.IsClient || __0 != LevelController.EndLevelType.FINISH;
        }
        static void Postfix(bool __state)
        {if(__state) SharedWorld.Current?.PublishMissionCompletion();}
    }

    [HarmonyPatch(typeof(LevelController), nameof(LevelController.GetLevelStats))]
    private static class SharedResults
    {
        static void Postfix(ref LevelStats __result)
        {if(SharedWorld.Current?.ClientLevelStats is {} stats) __result=stats;}
    }

    [HarmonyPatch(typeof(LevelController), "OnPlayerDeathStart")]
    private static class Death
    {
        static bool Prefix()
        {
            var life = SessionLifecycle.Current;
            if (life == null) return true;
            life.Death(); return false;
        }
    }
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.OpenDeathScreen))]
    private static class DeathScreen { static bool Prefix() => SessionLifecycle.Current == null; }

    [HarmonyPatch(typeof(InputManager), nameof(InputManager.AddInputMode))]
    private static class Gate
    {
        static void Postfix(InputManager.InputState __0)
        { if (__0 == InputManager.InputState.NONE) {SessionLifecycle.Current?.ArmGate();if(SessionLifecycle.Current==null && (Environment.GetEnvironmentVariable("PBDMP_DOOR_RELOAD_TEST")=="1" || Environment.GetEnvironmentVariable("PBDMP_JOIN_INPUT_TEST")=="1" || SurvivalInventory.Enabled || Environment.GetEnvironmentVariable("PBDMP_SURVIVAL_TRAVEL_TEST")=="1")) MultiplayerBehaviour.DoorSoloGate=true;} }
    }
    [HarmonyPatch(typeof(InputManager), nameof(InputManager.RemoveLastInputMode))]
    private static class PopInput
    {
        static void Prefix(out (IntPtr Stack,int Count) __state)
        { var stack=InputManager.InputStateStack;__state=(stack.Pointer,stack.Count); }
        static void Postfix((IntPtr Stack,int Count) __state)
        {
            var stack=InputManager.InputStateStack;
            if(stack.Pointer==__state.Stack && stack.Count==__state.Count-1)
                OwnedInputMode.Removed(stack.Pointer,stack.Count);
        }
    }
    [HarmonyPatch(typeof(Hyperstrange.PBD.PlayerComponents.PlayerTutorialComponent), "OnTutorialShow")]
    private static class TutorialShow
    {static void Prefix()=>SessionLifecycle.Current?.BeginTutorial();}
    [HarmonyPatch(typeof(Hyperstrange.PBD.PlayerComponents.PlayerTutorialComponent), "OnUseDown")]
    private static class TutorialDismiss
    {
        static void Prefix(Hyperstrange.PBD.PlayerComponents.PlayerTutorialComponent __instance,out bool __state)
        {__state=__instance._isTutorialShown;}
        static void Postfix(bool __state)
        {if(__state) SessionLifecycle.Current?.EndTutorial();}
    }

    [HarmonyPatch(typeof(TimeManager), nameof(TimeManager.SetApplicationTimeScale))]
    private static class TimeScale
    { static void Prefix(ref float applicationTimeScale) => SessionLifecycle.Current?.NativeTime(ref applicationTimeScale); }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.PauseGame))]
    private static class Pause { static bool Prefix() {var life=SessionLifecycle.Current;if(life?.ClientPause()==true) return false;life?.HostPauseRequest(true);return true;} }
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.DisableMenu))]
    private static class Unpause { static bool Prefix() {var life=SessionLifecycle.Current;if(life?.ClientUnpause()==true) return false;life?.HostPauseRequest(false);return true;} }

    [HarmonyPatch]
    private static class SaveBlock
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(LevelController), nameof(LevelController.QuickSave));
            yield return AccessTools.Method(typeof(LevelController), nameof(LevelController.AutoSave));
            yield return AccessTools.Method(typeof(LevelId), nameof(LevelId.UpdateMetaData));
            yield return AccessTools.Method(typeof(ProfileManager), nameof(ProfileManager.SaveProfilesData));
        }
        static bool Prefix() { if (!Protect) return true; BlockedSaves++; return false; }
    }
    [HarmonyPatch(typeof(ProfileManager), nameof(ProfileManager.SaveProfilesDataAsync))]
    private static class AsyncSaveBlock
    {
        static bool Prefix(ref Il2CppSystem.Threading.Tasks.Task __result)
        {
            if (!Protect) return true;
            BlockedSaves++;
            __result = Il2CppSystem.Threading.Tasks.Task.CompletedTask; return false;
        }
    }
    [HarmonyPatch(typeof(SaveController), nameof(SaveController.Save))]
    private static class Save
    {
        static bool Prefix(Il2CppSystem.Action onFinished)
        {
            if (!Protect) return true;
            BlockedSaves++;
            onFinished?.Invoke(); return false;
        }
    }
    [HarmonyPatch(typeof(SaveController), nameof(SaveController.Load))]
    private static class RestoreSave { static bool Prefix(SaveData saveData) => SessionLifecycle.Current == null || saveData == null; }

    [HarmonyPatch(typeof(MenuManager), nameof(MenuManager.Open))]
    private static class BlockMenus
    {
        static bool Prefix(Menu window)
        {
            if (SessionLifecycle.Current == null || window == null) return true;
            return window.TryCast<SaveLoadMenu>() == null && window.TryCast<ModMenu>() == null && (window.TryCast<HordeModeMenu>() == null || !SessionLifecycle.Current.IsClient);
        }
    }
    [HarmonyPatch(typeof(GameplaySettings), nameof(GameplaySettings.SetDifficulty))]
    private static class Difficulty
    { static bool Prefix() => SessionLifecycle.Current is not { IsClient: true, Bypass: false }; }
}
