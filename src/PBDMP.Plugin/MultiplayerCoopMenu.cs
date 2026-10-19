using HarmonyLib;
using Hyperstrange.Base;
using Hyperstrange.PBD;
using Hyperstrange.PBD.UI.Menus;
using Il2CppInterop.Runtime.Attributes;
using PBDMP.Network;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PBDMP.Plugin;

// Entry flows of the native-style Multiplayer menu. Hosting happens on demand: a friends-only Steam
// lobby when Steam is available, otherwise Direct IP on the configured port. A step that needs the
// host's session (loading a save, a new campaign) waits until hosting has finished starting.
public sealed partial class MultiplayerBehaviour
{
    private NativeCoopMenu? _coopMenu;
    private NativeCoopLoadMenu? _coopLoad;
    private System.Action? _afterHost;
    internal static bool CoopLevelSelect { get; private set; }
    internal static bool StartingCoopLevel { get; private set; }
    internal static MultiplayerBehaviour? Instance { get; private set; }
    internal const string MultiplayerMapPrompt = "LOAD THIS MAP AS A MULTIPLAYER GAME?";

    private void BuildCoopMenu()
    {
        Instance = this;
        var group = NativeUiKit.Require(MenuManager.MainCanvas.transform, "MainCanvasGroup");
        _coopMenu = new NativeCoopMenu(group, _view!.Kit, ContinueCoop, NewCoopGame, LoadCoopGame, SurvivalCoop, JoinFriend);
        _coopLoad = new NativeCoopLoadMenu(group, _view.Kit, (campaign, slot) => EnsureHosting(() => _saves!.RequestLoad(campaign, slot)));
        _view.MainEntry.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
        _view.MainEntry.onClick.AddListener((UnityEngine.Events.UnityAction)(System.Action)OpenCoopMenu);
        Plugin.Logger.LogInfo("Native Multiplayer menu constructed from the game's main menu.");
    }

    private bool InMainMenu => LevelManager.CurrentLevel == null || _lifecycle?.State.Action == TravelAction.Menu;

    internal void OpenCoopMenu()
    {
        if (_coopMenu == null) return;
        if (_open) SetPanel(false);
        RefreshCoopPreview();
        _coopMenu.Open();
    }

    private void RefreshCoopPreview()
    {
        CampaignListing? latest = null; byte[]? thumbnail = null;
        try
        {
            latest = CoopSaveStore.Campaigns(CoopSaveService.Root).FirstOrDefault();
            if (latest != null) thumbnail = new CoopSaveStore(CoopSaveStore.CampaignFolder(CoopSaveService.Root, latest.Id)).Thumbnail(latest.NewestSlot);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        { Plugin.Logger.LogWarning("Co-op campaigns could not be listed: " + error.Message); }
        if (latest == null) { _coopMenu!.ShowLatest(null, "", "", null); return; }
        _coopMenu!.ShowLatest(CampaignName(latest.Level), DifficultyName(latest.Difficulty) + PlayersLine(latest.Players),
            latest.LastPlayed.ToLocalTime().ToString("MMM d, HH:mm", System.Globalization.CultureInfo.InvariantCulture), thumbnail);
    }

    internal static string MapName(string level)
    {
        var found = SessionLifecycle.FindLevel(level)?.name ?? level;
        foreach (var prefix in new[] { "LEVEL_AD_", "LEVEL_Asylum_", "LEVEL_NLM_", "LEVEL_" })
            if (found.StartsWith(prefix, StringComparison.Ordinal)) { found = found[prefix.Length..]; break; }
        return found.Replace('_', ' ');
    }
    internal static string CampaignName(string level) => MapName(level).ToUpperInvariant() + " (CO-OP)";
    internal static string DifficultyName(int difficulty) => DifficultyManager.Difficulties is { } all && difficulty >= 0 && difficulty < all.Count ? DifficultyLabel(all[difficulty]) : "Difficulty " + difficulty;
    // The game's own localized difficulty name, else its asset name.
    private static string DifficultyLabel(DifficultyData data)
    {
        string? name = null;
        try { name = data.Name != null ? (string)data.Name : null; } catch (Exception) { }
        return string.IsNullOrWhiteSpace(name) ? data.name.Replace("DIFFICULTY_", "").Replace('_', ' ') : name!;
    }
    internal static string PlayersLine(string[] players) => players.Length == 0 ? "" : "  ·  Players: " + string.Join(", ", players);

    // Hosts when needed, then runs the step once P1's session and co-op saves exist.
    private void EnsureHosting(System.Action then)
    {
        if (_session?.Snapshot.Role == SessionRole.Host && _saves != null) { then(); return; }
        _afterHost = then;
        // The offline menu fixture hosts over Direct IP on its own port, never a real Steam lobby.
        if (CoopMenuTestEnabled) { _port!.text = "47777"; Host(); }
        else if (_steam?.Available == true) HostSteam(); else Host();
    }

    private void TickAfterHost()
    {
        if (_afterHost == null) return;
        if (_session == null) { _afterHost = null; return; }
        if (_saves == null || _lifecycle == null || _lifecycle.IsClient) return;
        var step = _afterHost; _afterHost = null; step();
    }

    private void ContinueCoop() => EnsureHosting(() => _saves!.ContinueLatest());

    private void NewCoopGame()
    {
        var levels = NativeUiKit.Require(MenuManager.MainCanvas.transform, "MainCanvasGroup/MENU_LevelSelection").GetComponent<LevelSelectionMenu>();
        CoopLevelSelect = true;
        MenuManager.Open(levels);
    }

    private void SurvivalCoop()
    {
        var horde = NativeUiKit.Require(MenuManager.MainCanvas.transform, "MainCanvasGroup/MENU_Horde").GetComponent<HordeModeMenu>();
        EnsureHosting(() => MenuManager.Open(horde));
    }

    private void LoadCoopGame() => _coopLoad?.Open();

    private void JoinFriend() { SetPanel(true); _view!.ShowDirect(true); }

    // Native Start on the skull level select, reached through New co-op game.
    internal bool BeforeStartLevel(LevelSelectionMenu menu)
    {
        if (!CoopLevelSelect || StartingCoopLevel) return true;
        MenuManager.OpenConfirmationPrompt((Il2CppSystem.Action)(System.Action)(() => EnsureHosting(() =>
        {
            _saves!.StartNewCampaign();
            StartingCoopLevel = true;
            try { menu.StartLevel(); }
            finally { StartingCoopLevel = false; }
        })), ScriptableObject.CreateInstance<LocalizedString>());
        SetPromptText(MultiplayerMapPrompt);
        return false;
    }

    // The native confirmation prompt with PBDMP's own question (its localized key is replaced).
    internal static void SetPromptText(string text)
    {
        var label = NativeUiKit.Require(MenuManager.MainCanvas.transform, "MainCanvasGroup/PROMPT_Confirmation/OverlayImage/Background/Text").GetComponent<LocalizedText>();
        label.LocalizedLabel = null!; label.text = text;
    }

    internal static void LeftCoopLevelSelect() { if (!StartingCoopLevel) CoopLevelSelect = false; }
}

internal static class CoopMenuPatches
{
    internal static void Install(Harmony h)
    {
        h.Patch(AccessTools.Method(typeof(LevelSelectionMenu), nameof(LevelSelectionMenu.StartLevel)), prefix: new(typeof(CoopMenuPatches), nameof(StartLevel)));
        h.Patch(AccessTools.Method(typeof(MainMenu), nameof(MainMenu.Enable)), postfix: new(typeof(CoopMenuPatches), nameof(MainMenuShown)));
    }
    private static bool StartLevel(LevelSelectionMenu __instance) => MultiplayerBehaviour.Instance?.BeforeStartLevel(__instance) != false;
    private static void MainMenuShown() => MultiplayerBehaviour.LeftCoopLevelSelect();
}
