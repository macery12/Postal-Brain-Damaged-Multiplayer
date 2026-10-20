using HarmonyLib;
using Hyperstrange.PBD.Leaderboards;

namespace PBDMP.Plugin;

// Installed before any menu/fixture initialization. Title-scene startup can submit
// previously stored solo times, even before a co-op session exists.
internal static class ReadmeCaptureIsolation
{
    internal static void Install()
    {
        if (Environment.GetEnvironmentVariable("PBDMP_README_CAPTURE") != "1") return;
        var harmony = new Harmony("pbdmp.documentation.no-score-uploads");
        harmony.Patch(AccessTools.Method(typeof(LeaderboardManager), nameof(LeaderboardManager.UploadTime)),
            prefix: new(typeof(ReadmeCaptureIsolation), nameof(Block)));
        harmony.Patch(AccessTools.Method(typeof(SteamLeaderboard), nameof(SteamLeaderboard.UploadScore)),
            prefix: new(typeof(ReadmeCaptureIsolation), nameof(Block)));
        harmony.Patch(AccessTools.Method(typeof(Steamworks.SteamUserStats), nameof(Steamworks.SteamUserStats.UploadLeaderboardScore)),
            prefix: new(typeof(ReadmeCaptureIsolation), nameof(Block)));
        Plugin.Logger.LogInfo("README capture: leaderboard uploads blocked before title startup (manager, native Steam leaderboard and Steam API).");
    }

    private static bool Block()
    {
        Plugin.Logger.LogInfo("README capture: blocked score upload.");
        return false;
    }
}
