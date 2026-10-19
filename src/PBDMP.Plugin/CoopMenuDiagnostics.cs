using Hyperstrange.PBD;
using Hyperstrange.PBD.UI.Menus;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PBDMP.Plugin;

// Test-Runtime -CoopMenu (one game, offline): the native-style Multiplayer menu, New co-op game's
// real level select and its multiplayer prompt, then Back. Nothing is hosted or loaded.
// Labels: buttons are activated through their onClick events; screenshots are the real game frame.
public sealed partial class MultiplayerBehaviour
{
    private int _coopMenuStep;
    private float _coopMenuAt;
    private string? _coopCampaignBefore;
    private string _coopCampaign = "", _coopLevel = "";
    private int _coopWrites;
    private static bool CoopMenuTestEnabled => Environment.GetEnvironmentVariable("PBDMP_COOP_MENU_TEST") == "1";
    private static Transform Group => MenuManager.MainCanvas.transform.Find("MainCanvasGroup");
    private static bool Shown(string menu) => Group.Find(menu) is { } t && t.gameObject.activeInHierarchy && t.GetComponent<CanvasGroup>() is { } g && g.alpha > 0.5f;
    // A hidden test window has no screen frame: render the scene cameras (the menu's 3D spiral),
    // then the game's canvas through a capture camera, into one texture.
    private void Shot(string name)
    {
        var canvas = MenuManager.MainCanvas; const int width = 1280, height = 720;
        var target = new RenderTexture(width, height, 24); var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
        var previousMode = canvas.renderMode; var previousCamera = canvas.worldCamera; var previousActive = RenderTexture.active;
        var captureObject = new GameObject("PBDMP co-op menu capture camera");
        var transforms = canvas.GetComponentsInChildren<Transform>(true); var layers = transforms.Select(t => t.gameObject.layer).ToArray();
        try
        {
            foreach (var camera in Camera.allCameras.OrderBy(c => c.depth))
            {
                var previousTarget = camera.targetTexture; camera.targetTexture = target;
                try { camera.Render(); } finally { camera.targetTexture = previousTarget; }
            }
            var capture = captureObject.AddComponent<Camera>();
            capture.enabled = false; capture.clearFlags = CameraClearFlags.Depth; capture.cullingMask = 1 << 31; capture.targetTexture = target;
            foreach (var t in transforms) t.gameObject.layer = 31;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = capture; canvas.planeDistance = 1;
            Canvas.ForceUpdateCanvases(); capture.Render();
            // PBDMP's own overlay canvas (Players & invite, prompts, HUD) draws above the game's.
            if (_ui != null && _ui.GetComponent<Canvas>() is { } overlay)
            {
                var overlayTransforms = overlay.GetComponentsInChildren<Transform>(true); var overlayLayers = overlayTransforms.Select(t => t.gameObject.layer).ToArray();
                var overlayMode = overlay.renderMode;
                try
                {
                    foreach (var t in overlayTransforms) t.gameObject.layer = 31;
                    overlay.renderMode = RenderMode.ScreenSpaceCamera; overlay.worldCamera = capture; overlay.planeDistance = 0.5f;
                    Canvas.ForceUpdateCanvases(); capture.Render();
                }
                finally
                {
                    overlay.renderMode = overlayMode;
                    for (var i = 0; i < overlayTransforms.Length; i++) overlayTransforms[i].gameObject.layer = overlayLayers[i];
                }
            }
            RenderTexture.active = target; pixels.ReadPixels(new UnityEngine.Rect(0, 0, width, height), 0, 0); pixels.Apply();
            File.WriteAllBytes(Path.Combine(SmokeOutput(), name), ImageConversion.EncodeToPNG(pixels));
        }
        finally
        {
            canvas.renderMode = previousMode; canvas.worldCamera = previousCamera; RenderTexture.active = previousActive;
            for (var i = 0; i < transforms.Length; i++) transforms[i].gameObject.layer = layers[i];
            target.Release(); Object.Destroy(target); Object.Destroy(pixels); Object.Destroy(captureObject);
        }
    }

    // Two synthetic campaigns (valid schema-3 files with native-sized thumbnails), never loaded.
    private static void SeedCoopCampaigns()
    {
        var thumbnail = new Texture2D(160, 90, TextureFormat.RGB24, false);
        for (var x = 0; x < 160; x++) for (var y = 0; y < 90; y++) thumbnail.SetPixel(x, y, new Color(0.35f + x / 400f, 0.1f, 0.4f));
        thumbnail.Apply(); byte[] png = ImageConversion.EncodeToPNG(thumbnail); Object.Destroy(thumbnail);
        PBDMP.Network.CoopSave Save(string slot, string level, int difficulty, DateTimeOffset at, params string[] players) => new()
        {
            Game = new string('a', 64) + ":" + new string('b', 64), Catalogue = new string('c', 64), Slot = slot, Name = slot == "quick" ? "Quicksave" : slot.StartsWith("auto-", StringComparison.Ordinal) ? "Timed autosave" : "Before the riot",
            Level = level, Difficulty = difficulty, Created = at, Players = players,
            Host = new("direct:fixture", level, new() { { "_saveData", "{}" } }, Array.Empty<string>()), Native = new() { { "world", new() { { "state", "true" } } } }
        };
        var now = DateTimeOffset.UtcNow;
        var prison = new PBDMP.Network.CoopSaveStore(PBDMP.Network.CoopSaveStore.CampaignFolder(CoopSaveService.Root, PBDMP.Network.CoopSaveStore.NewCampaignId()));
        prison.Write(Save("auto-0", "LEVEL_AD_Prison", 2, now.AddMinutes(-40), "You", "Alex"), png);
        prison.Write(Save("manual-riot", "LEVEL_AD_Prison", 2, now.AddMinutes(-20), "You", "Alex"), png);
        prison.Write(Save("quick", "LEVEL_AD_Prison", 2, now.AddMinutes(-2), "You", "Alex"), png);
        var forest = new PBDMP.Network.CoopSaveStore(PBDMP.Network.CoopSaveStore.CampaignFolder(CoopSaveService.Root, PBDMP.Network.CoopSaveStore.NewCampaignId()));
        forest.Write(Save("auto-1", "LEVEL_Asylum_Forest", 1, now.AddDays(-1), "You", "Sam"), png);
    }

    private void RunCoopMenuTest()
    {
        if (_coopMenuStep == 99) return;
        // In a level: continue past the native loading gate and dismiss native tutorials, like the travel fixtures.
        if (_coopMenuStep >= 22 && _lifecycle is { NativeReady: false } gate && gate.State.Action != PBDMP.Network.TravelAction.Menu) gate.AutoContinue();
        if (_coopMenuStep >= 22 && _lifecycle is { NativeReady: true, LocalTutorialActive: true } && Player.Instance != null)
            Player.Instance.GetComponent<Hyperstrange.PBD.PlayerComponents.PlayerTutorialComponent>().OnUseDown();
        if (Time.unscaledTime < _coopMenuAt) return;
        switch (_coopMenuStep)
        {
            case 0 when _coopMenu != null && Shown("MENU_Main"):
                SeedCoopCampaigns(); OpenCoopMenu(); _coopMenuAt = Time.unscaledTime + 1.5f; _coopMenuStep = 1; break;
            case 1:
                SmokeAssert(Shown("MENU_PBDMP_Multiplayer") && !Shown("MENU_Main"), "Multiplayer opens the native-style co-op menu in place of the main menu");
                SmokeAssert(_coopMenu!.NewGame.LabelText.text == "New co-op game" && _coopMenu.Back.LabelText.text == "Back", "co-op menu shows its own labels in the main menu's lettering");
                SmokeAssert(_coopMenu.Continue.interactable, "Continue co-op is available once a campaign exists");
                SmokeAssert(_coopMenu.PreviewTitleForTest == CampaignName("LEVEL_AD_Prison") && _coopMenu.PreviewTitleRendersForTest, "the Continue preview names the newest campaign by its map in the game's header lettering");
                Shot("coop-menu.png");
                _coopMenu.Load.onClick.Invoke(); _coopMenuAt = Time.unscaledTime + 1.5f; _coopMenuStep = 10; break;
            case 10:
                SmokeAssert(Shown("MENU_PBDMP_CoopLoad") && _coopLoad!.CampaignCount == 2 && _coopLoad.SaveCount == 3, "Load co-op game lists both campaigns and the newest campaign's three saves");
                SmokeAssert(_coopLoad!.SelectedCampaign?.Level == "LEVEL_AD_Prison" && _coopLoad.SelectedSave?.Slot == "quick", "the newest campaign and its newest save are selected first");
                Shot("coop-load.png");
                _coopLoad.DeleteButton.onClick.Invoke(); _coopMenuAt = Time.unscaledTime + 1f; _coopMenuStep = 11; break;
            case 11:
                var question = Group.Find("PROMPT_Confirmation/OverlayImage/Background/Text").GetComponent<TMPro.TMP_Text>();
                SmokeAssert(Shown("PROMPT_Confirmation") && question.text.StartsWith("DELETE THIS CO-OP GAME", StringComparison.Ordinal), "Delete asks before removing a whole co-op game");
                Shot("coop-delete-prompt.png");
                Group.Find("PROMPT_Confirmation/OverlayImage/Background/BUTTON_Confirm").GetComponent<Hyperstrange.PBD.UI.Components.MainHyperButton>().onClick.Invoke();
                _coopMenuAt = Time.unscaledTime + 1.2f; _coopMenuStep = 12; break;
            case 12:
                SmokeAssert(_coopLoad!.CampaignCount == 1 && _coopLoad.SelectedCampaign?.Level == "LEVEL_Asylum_Forest", "the deleted co-op game disappears and the next one is selected");
                MenuManager.Close(); _coopMenuAt = Time.unscaledTime + 1.2f; _coopMenuStep = 13; break;
            case 13:
                SmokeAssert(Shown("MENU_PBDMP_Multiplayer"), "Back from Load co-op game returns to the co-op menu");
                _coopMenuAt = Time.unscaledTime + 0.5f; _coopMenuStep = 2; break;
            case 2:
                _coopMenu!.NewGame.onClick.Invoke(); _coopMenuAt = Time.unscaledTime + 2.5f; _coopMenuStep = 3; break;
            case 3:
                SmokeAssert(Shown("MENU_LevelSelection") && CoopLevelSelect, "New co-op game opens the game's own skull level select");
                Shot("coop-level-select.png");
                Group.Find("MENU_LevelSelection").GetComponent<LevelSelectionMenu>().StartLevel();
                _coopMenuAt = Time.unscaledTime + 1.2f; _coopMenuStep = 4; break;
            case 4:
                var prompt = Group.Find("PROMPT_Confirmation/OverlayImage/Background/Text").GetComponent<TMPro.TMP_Text>();
                SmokeAssert(Shown("PROMPT_Confirmation") && prompt.text == MultiplayerMapPrompt, "Start asks to load the map as a multiplayer game in the native prompt");
                SmokeAssert(_session == null, "the prompt hosts nothing until confirmed");
                Shot("coop-map-prompt.png");
                MenuManager.Close(); _coopMenuAt = Time.unscaledTime + 1f; _coopMenuStep = 5; break;
            case 5:
                SmokeAssert(!Shown("PROMPT_Confirmation") && Shown("MENU_LevelSelection"), "cancelling the prompt returns to the level select");
                MenuManager.Close(); _coopMenuAt = Time.unscaledTime + 1.2f; _coopMenuStep = 6; break;
            case 6:
                SmokeAssert(Shown("MENU_PBDMP_Multiplayer"), "Back from the level select returns to the co-op menu");
                _coopMenu!.Back.onClick.Invoke(); _coopMenuAt = Time.unscaledTime + 1.2f; _coopMenuStep = 7; break;
            case 7:
                SmokeAssert(Shown("MENU_Main") && !CoopLevelSelect, "Back returns to the native main menu and leaves co-op level select");
                OpenCoopMenu(); _coopMenu!.Survival.onClick.Invoke(); _coopMenuAt = Time.unscaledTime + 2f; _coopMenuStep = 15; break;
            case 15:
                SmokeAssert(Shown("MENU_Horde") && _session?.Snapshot.Role == PBDMP.Network.SessionRole.Host, "Survival co-op hosts and opens the game's own Survival menu");
                Shot("coop-survival.png");
                MenuManager.Close(); ResetSession(); _coopMenuAt = Time.unscaledTime + 1.5f; _coopMenuStep = 16; break;
            case 16:
                SmokeAssert(Shown("MENU_PBDMP_Multiplayer") && _session == null, "Back from Survival returns to the co-op menu");
                // End to end: a new co-op game from the real level select, a real save, then Continue co-op.
                _coopMenu!.NewGame.onClick.Invoke(); _coopMenuAt = Time.unscaledTime + 2f; _coopMenuStep = 20; break;
            case 20:
                Group.Find("MENU_LevelSelection").GetComponent<LevelSelectionMenu>().StartLevel();
                _coopMenuAt = Time.unscaledTime + 1f; _coopMenuStep = 21; break;
            case 21:
                _coopCampaignBefore = _saves?.Campaign;
                Group.Find("PROMPT_Confirmation/OverlayImage/Background/BUTTON_Confirm").GetComponent<Hyperstrange.PBD.UI.Components.MainHyperButton>().onClick.Invoke();
                _coopMenuAt = Time.unscaledTime + 2f; _coopMenuStep = 22; break;
            case 22 when Shown("PROMPT_Difficulty"):
                SmokeAssert(_session?.Snapshot.Role == PBDMP.Network.SessionRole.Host && _saves != null, "confirming hosts a co-op session before the native difficulty prompt");
                Shot("coop-difficulty.png");
                Group.Find("PROMPT_Difficulty/OverlayImage/Background/BUTTON_Confirm").GetComponent<Hyperstrange.PBD.UI.Components.MainHyperButton>().onClick.Invoke();
                _coopMenuAt = Time.unscaledTime + 3f; _coopMenuStep = 23; break;
            case 23 when _lifecycle is { NativeReady: true } life && life.State.Action != PBDMP.Network.TravelAction.Menu && _world?.Active == true && Player.Instance != null && Player.Instance.IsAlive:
                _coopCampaign = _saves!.Campaign;
                SmokeAssert(_coopCampaign.Length == 32 && _coopCampaign != CoopSaveService.DiagnosticCampaign, "the new co-op game starts its own campaign");
                _coopLevel = LevelManager.CurrentLevel!.name;
                _saves.RequestSave("manual-coop-menu", "Menu fixture"); _coopMenuAt = Time.unscaledTime + 1f; _coopMenuStep = 24; break;
            case 24 when !_saves!.Busy && _saves.CompletedWritesForTest > 0:
                var campaign = PBDMP.Network.CoopSaveStore.Campaigns(CoopSaveService.Root).FirstOrDefault(c => c.Id == _coopCampaign);
                SmokeAssert(campaign != null && campaign.Level == SessionLifecycle.FindLevel(campaign.Level)?.ID && campaign.Saves == 1 && _saves.Store.Thumbnail("manual-coop-menu") != null,
                    "a save in the new co-op game lands in its campaign with the game's thumbnail");
                ResetSession(); GameManager.OpenTitle(); _coopMenuAt = Time.unscaledTime + 4f; _coopMenuStep = 25; break;
            case 25 when Shown("MENU_Main") && _session == null:
                OpenCoopMenu(); _coopMenuAt = Time.unscaledTime + 1.5f; _coopMenuStep = 26; break;
            case 26:
                SmokeAssert(_coopMenu!.PreviewTitleForTest == CampaignName(PBDMP.Network.CoopSaveStore.Campaigns(CoopSaveService.Root).First().Level), "the Continue preview now shows the campaign just played");
                _coopMenu.Continue.onClick.Invoke(); _coopMenuAt = Time.unscaledTime + 3f; _coopMenuStep = 27; break;
            case 27 when _lifecycle is { NativeReady: true } life && life.State.Action != PBDMP.Network.TravelAction.Menu && _world?.Active == true && _saves?.Busy == false && Player.Instance != null:
                SmokeAssert(_session?.Snapshot.Role == PBDMP.Network.SessionRole.Host && _saves.Campaign == _coopCampaign && LevelManager.CurrentLevel!.name == _coopLevel,
                    "Continue co-op hosts and loads the newest save of that campaign");
                Shot("coop-continued.png");
                // The native pause menu's Save during the session opens the co-op save screen.
                GameManager.PauseGame(); _coopMenuAt = Time.unscaledTime + 1f; _coopMenuStep = 30; break;
            case 30:
                Group.Find("MENU_Pause/ButtonsLayout/BUTTON_Save").GetComponent<Hyperstrange.PBD.UI.Components.MainHyperButton>().onClick.Invoke();
                _coopMenuAt = Time.unscaledTime + 1.2f; _coopMenuStep = 31; break;
            case 31:
                SmokeAssert(Shown("MENU_PBDMP_CoopSave") && !Shown("MENU_Save") && _coopSave!.EntryCount == 2, "pause Save opens the co-op save screen with New save and the campaign's manual save");
                SmokeAssert(Group.Find("MENU_Pause/ButtonsLayout/BUTTON_PBDMP_Multiplayer").GetComponent<Hyperstrange.PBD.UI.Components.MainHyperButton>().LabelText.text == "Players & invite", "the pause entry is Players & invite");
                Shot("coop-pause-save.png");
                _coopWrites = _saves!.CompletedWritesForTest;
                _coopSave!.SaveButton.onClick.Invoke(); _coopMenuAt = Time.unscaledTime + 1f; _coopMenuStep = 32; break;
            case 32:
                SmokeAssert(Shown("PROMPT_Input") && Group.Find("PROMPT_Input/OverlayImage/Background/Text").GetComponent<TMPro.TMP_Text>().text == "NAME THIS SAVE", "New save asks for a name in the native prompt");
                Shot("coop-save-name.png");
                Group.Find("PROMPT_Input/OverlayImage/Background/BUTTON_Confirm").GetComponent<Hyperstrange.PBD.UI.Components.MainHyperButton>().onClick.Invoke();
                _coopMenuAt = Time.unscaledTime + 1f; _coopMenuStep = 33; break;
            case 33 when !_saves!.Busy && _saves.CompletedWritesForTest > _coopWrites:
                SmokeAssert(_saves.Store.List().Count(s => s.Slot.StartsWith("manual-", StringComparison.Ordinal)) == 2, "the named save is written to the same campaign");
                MenuManager.Close(); _coopMenuAt = Time.unscaledTime + 1f; _coopMenuStep = 34; break;
            case 34:
                Group.Find("MENU_Pause/ButtonsLayout/BUTTON_Load").GetComponent<Hyperstrange.PBD.UI.Components.MainHyperButton>().onClick.Invoke();
                _coopMenuAt = Time.unscaledTime + 1.2f; _coopMenuStep = 35; break;
            case 35:
                SmokeAssert(Shown("MENU_PBDMP_CoopLoad") && !Shown("MENU_Load") && _coopLoad!.LatestAutosaveButton.gameObject.activeInHierarchy && _coopLoad.SelectedCampaign?.Id == _coopCampaign,
                    "pause Load opens co-op Load on the current campaign with Load latest autosave");
                Shot("coop-pause-load.png");
                MenuManager.Close(); _coopMenuAt = Time.unscaledTime + 1f; _coopMenuStep = 36; break;
            case 36:
                MenuManager.Instance.Unpause(); TogglePanel(); _coopMenuAt = Time.unscaledTime + 1f; _coopMenuStep = 37; break;
            case 37:
                SmokeAssert(_open && _view!.DisconnectButton.LabelText.text == "Leave session" && _view.DirectButton.LabelText.text == "Direct IP: port 47777" && !_view.SavesButton.gameObject.activeSelf,
                    "Players & invite shows the session, Direct IP hosting and Leave session, without separate save controls");
                Shot("coop-players.png");
                TogglePanel();
                Plugin.Logger.LogInfo("PBDMP SMOKE PASS: native co-op menu flow"); _coopMenuStep = 99; break;
        }
    }
}
