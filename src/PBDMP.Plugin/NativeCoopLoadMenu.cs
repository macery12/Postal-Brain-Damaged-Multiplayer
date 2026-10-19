using Hyperstrange.PBD;
using Hyperstrange.PBD.UI.Components;
using Hyperstrange.PBD.UI.Menus;
using PBDMP.Network;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PBDMP.Plugin;

// Load co-op game: a copy of the game's own load screen. Campaigns on the left, the selected
// campaign's saves on the right, both as the game's own save entries (thumbnail, type, level, date).
internal sealed class NativeCoopLoadMenu
{
    internal Menu Menu { get; }
    internal MainHyperButton LoadButton { get; }
    internal MainHyperButton DeleteButton { get; }
    internal MainHyperButton BackButton { get; }
    internal MainHyperButton LatestAutosaveButton { get; }
    internal CampaignListing? SelectedCampaign { get; private set; }
    internal SaveListing? SelectedSave { get; private set; }
    internal int CampaignCount => _campaigns.Count;
    internal int SaveCount => _saves.Count;
    private readonly SaveHyperButton _prefab;
    private readonly Transform _campaignList, _saveList;
    private readonly TMP_Text _empty;
    private readonly List<(SaveHyperButton Button, CampaignListing Campaign)> _campaigns = new();
    private readonly List<(SaveHyperButton Button, SaveListing Save)> _saves = new();
    private readonly List<Texture2D> _textures = new();
    private readonly System.Action<string, string> _load;
    private bool _campaignFocus = true;

    internal NativeCoopLoadMenu(Transform group, NativeUiKit kit, System.Action<string, string> load)
    {
        _load = load;
        var source = NativeUiKit.Require(group, "MENU_Load").gameObject;
        var native = source.GetComponent<LoadMenu>();
        _prefab = native._saveButtonPrefab;
        var root = kit.InactiveCopy(source);
        root.name = "MENU_PBDMP_CoopLoad";
        var copy = root.GetComponent<LoadMenu>();
        var fade = copy._fadeType; var fadeTime = copy._fadeTime; var legend = copy._legendDatas; var escape = copy._disableOnEscape;
        Object.DestroyImmediate(copy);
        Menu = root.AddComponent<Menu>();
        Menu._canvasGroup = root.GetComponent<CanvasGroup>();
        Menu._fadeType = fade; Menu._fadeTime = fadeTime; Menu._legendDatas = legend; Menu._disableOnEscape = escape;

        var header = NativeUiKit.Require(root.transform, "HEADER_Load").GetComponent<LocalizedText>();
        header.LocalizedLabel = null!; header.text = "Load co-op game";

        // The game's single save column becomes two: campaigns (left) and their saves (right).
        var left = NativeUiKit.Require(root.transform, "Scroll View").GetComponent<RectTransform>();
        var right = Object.Instantiate(left.gameObject, left.parent, false).GetComponent<RectTransform>();
        right.name = "Scroll View (saves)";
        // The native column is anchored left; the saves column sits beside it.
        right.anchoredPosition += new Vector2(1450, 0);
        _campaignList = NativeUiKit.Require(left, "Viewport/Content");
        _saveList = NativeUiKit.Require(right, "Viewport/Content");
        Clear(_campaignList); Clear(_saveList);

        LoadButton = NativeUiKit.Require(root.transform, "BUTTON_Load").GetComponent<MainHyperButton>();
        DeleteButton = NativeUiKit.Require(root.transform, "BUTTON_Delete").GetComponent<MainHyperButton>();
        BackButton = NativeUiKit.Require(root.transform, "BUTTON_Back").GetComponent<MainHyperButton>();
        NativeUiKit.PrepareButton(LoadButton, "Load", LoadSelected);
        NativeUiKit.PrepareButton(DeleteButton, "Delete", DeleteSelected);
        NativeUiKit.PrepareButton(BackButton, "Back", () => MenuManager.Close());
        // During P1's session: the game's own wording for restoring the shared checkpoint.
        LatestAutosaveButton = Object.Instantiate(DeleteButton.gameObject, DeleteButton.transform.parent, false).GetComponent<MainHyperButton>();
        LatestAutosaveButton.name = "BUTTON_LoadLatestAutosave";
        LatestAutosaveButton.GetComponent<RectTransform>().anchoredPosition += new Vector2(0, -95);
        NativeUiKit.PrepareButton(LatestAutosaveButton, "Load latest autosave", LoadLatestAutosave);
        LatestAutosaveButton.gameObject.SetActive(false);
        _empty = kit.TextAt(root.transform, "No co-op games", "No co-op games saved yet.\nStart one with New co-op game.", new Vector2(1920, 1080), new Vector2(1800, 300), 72);
        _empty.gameObject.SetActive(false);

        root.transform.SetParent(group, false);
        root.transform.SetSiblingIndex(source.transform.GetSiblingIndex() + 1);
    }

    private static void Clear(Transform list) { for (var i = list.childCount - 1; i >= 0; i--) Object.DestroyImmediate(list.GetChild(i).gameObject); }

    internal void Open()
    {
        var host = CoopSaveService.Current != null && SessionLifecycle.Current is { IsClient: false } life && life.State.Action != TravelAction.Menu;
        LatestAutosaveButton.gameObject.SetActive(host);
        if (host) NativeUiKit.SetButtonState(LatestAutosaveButton, CoopSaveService.Current!.Store.List().Any(s => s.Error == null && s.Slot.StartsWith("auto-", StringComparison.Ordinal)));
        Refresh(CoopSaveService.Current?.Campaign);
        MenuManager.Open(Menu);
        Menu.gameObject.SetActive(true);
    }

    internal void Refresh(string? keepCampaign)
    {
        CampaignListing[] campaigns;
        try { campaigns = CoopSaveStore.Campaigns(CoopSaveService.Root); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        { Plugin.Logger.LogWarning("Co-op campaigns could not be listed: " + error.Message); campaigns = Array.Empty<CampaignListing>(); }
        foreach (var texture in _textures) Object.Destroy(texture);
        _textures.Clear(); _campaigns.Clear(); Clear(_campaignList);
        foreach (var campaign in campaigns)
        {
            var store = new CoopSaveStore(CoopSaveStore.CampaignFolder(CoopSaveService.Root, campaign.Id));
            var entry = Entry(_campaignList, MultiplayerBehaviour.CampaignName(campaign.Level),
                MultiplayerBehaviour.DifficultyName(campaign.Difficulty) + MultiplayerBehaviour.PlayersLine(campaign.Players), When(campaign.LastPlayed), store.Thumbnail(campaign.NewestSlot));
            var selected = campaign;
            entry.onClick.AddListener((UnityEngine.Events.UnityAction)(System.Action)(() => { _campaignFocus = true; SelectCampaign(selected); }));
            _campaigns.Add((entry, campaign));
        }
        _empty.gameObject.SetActive(campaigns.Length == 0);
        SelectCampaign(campaigns.FirstOrDefault(c => c.Id == keepCampaign) ?? campaigns.FirstOrDefault());
    }

    private void SelectCampaign(CampaignListing? campaign)
    {
        SelectedCampaign = campaign; SelectedSave = null; _saves.Clear(); Clear(_saveList);
        foreach (var (button, listing) in _campaigns) Mark(button, listing.Id == campaign?.Id);
        if (campaign != null)
        {
            var store = new CoopSaveStore(CoopSaveStore.CampaignFolder(CoopSaveService.Root, campaign.Id));
            foreach (var save in store.List().Where(s => s.Error == null))
            {
                var entry = Entry(_saveList, SaveType(save), MultiplayerBehaviour.MapName(save.Level), When(save.Created), store.Thumbnail(save.Slot));
                var selected = save;
                entry.onClick.AddListener((UnityEngine.Events.UnityAction)(System.Action)(() => { _campaignFocus = false; SelectSave(selected); }));
                entry.SetDoubleClickAction((Il2CppSystem.Action)(System.Action)(() => { SelectSave(selected); LoadSelected(); }));
                _saves.Add((entry, save));
            }
        }
        SelectSave(_saves.Count > 0 ? _saves[0].Save : null);
        Navigate();
    }

    private void SelectSave(SaveListing? save)
    {
        SelectedSave = save;
        foreach (var (button, listing) in _saves) Mark(button, listing.Slot == save?.Slot);
        NativeUiKit.SetButtonState(LoadButton, save != null);
        NativeUiKit.SetButtonState(DeleteButton, SelectedCampaign != null);
    }

    private static void Mark(SaveHyperButton button, bool selected) { if (selected) button.SetChecked(); else button.SetInteractable(); }

    internal static string SaveType(SaveListing save) =>
        save.Slot.StartsWith("auto-", StringComparison.Ordinal) ? "Autosave" :
        save.Slot == "quick" ? "Quicksave" : "Manual save  ·  " + save.Name;
    private static string When(DateTimeOffset at) => at.ToLocalTime().ToString("MMM d, yyyy  HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    private SaveHyperButton Entry(Transform list, string type, string level, string date, byte[]? thumbnail)
    {
        var entry = Object.Instantiate(_prefab, list, false);
        entry.gameObject.SetActive(true);
        foreach (var localized in entry.GetComponentsInChildren<LocalizedText>(true)) localized.LocalizedLabel = null!;
        entry.TypeText.text = type; entry.LevelText.text = level; entry.DateText.text = date;
        if (entry.ObsoleteVignette != null) entry.ObsoleteVignette.SetActive(false);
        if (entry.DLCVignette != null) entry.DLCVignette.SetActive(false);
        entry.onClick = new Button.ButtonClickedEvent();
        if (thumbnail != null)
        {
            var texture = new Texture2D(2, 2);
            if (ImageConversion.LoadImage(texture, thumbnail))
            {
                _textures.Add(texture);
                entry.ThumbnailImage.sprite = Sprite.Create(texture, new UnityEngine.Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            }
            else Object.Destroy(texture);
        }
        return entry;
    }

    // Columns navigate up/down; left/right switches columns; the bottom row holds Back, Delete, Load.
    private void Navigate()
    {
        void Column(IReadOnlyList<Selectable> items, Selectable? across)
        {
            for (var i = 0; i < items.Count; i++)
            {
                var navigation = items[i].navigation; navigation.mode = Navigation.Mode.Explicit;
                navigation.selectOnUp = i > 0 ? items[i - 1] : null; navigation.selectOnDown = i + 1 < items.Count ? items[i + 1] : LoadButton;
                navigation.selectOnLeft = navigation.selectOnRight = across; items[i].navigation = navigation;
            }
        }
        var campaigns = _campaigns.Select(c => (Selectable)c.Button).ToList(); var saves = _saves.Select(s => (Selectable)s.Button).ToList();
        Column(campaigns, saves.FirstOrDefault()); Column(saves, campaigns.FirstOrDefault());
        var row = new[] { BackButton, DeleteButton, LoadButton };
        for (var i = 0; i < row.Length; i++)
        {
            var navigation = row[i].navigation; navigation.mode = Navigation.Mode.Explicit;
            navigation.selectOnLeft = i > 0 ? row[i - 1] : null; navigation.selectOnRight = i + 1 < row.Length ? row[i + 1] : null;
            navigation.selectOnUp = saves.LastOrDefault() ?? campaigns.LastOrDefault(); row[i].navigation = navigation;
        }
        Menu._firstSelected = campaigns.FirstOrDefault() ?? (Selectable)BackButton;
    }

    private void LoadSelected()
    {
        if (SelectedCampaign is not { } campaign) return;
        _load(campaign.Id, SelectedSave?.Slot ?? campaign.NewestSlot);
    }

    private static void LoadLatestAutosave()
    {
        MenuManager.OpenConfirmationPrompt((Il2CppSystem.Action)(System.Action)(() => CoopSaveService.Current?.RestoreCheckpoint()), ScriptableObject.CreateInstance<Hyperstrange.Base.LocalizedString>());
        MultiplayerBehaviour.SetPromptText("LOAD THE LATEST AUTOSAVE FOR BOTH PLAYERS?");
    }

    private void DeleteSelected()
    {
        if (SelectedCampaign is not { } campaign) return;
        var deleteCampaign = _campaignFocus || SelectedSave == null;
        var save = SelectedSave;
        MenuManager.OpenConfirmationPrompt((Il2CppSystem.Action)(System.Action)(() =>
        {
            try
            {
                if (deleteCampaign) CoopSaveStore.DeleteCampaign(CoopSaveService.Root, campaign.Id);
                else new CoopSaveStore(CoopSaveStore.CampaignFolder(CoopSaveService.Root, campaign.Id)).Delete(save!.Slot);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
            { Plugin.Logger.LogWarning("Co-op save could not be deleted: " + error.Message); }
            Refresh(deleteCampaign ? null : campaign.Id);
        }), ScriptableObject.CreateInstance<Hyperstrange.Base.LocalizedString>());
        MultiplayerBehaviour.SetPromptText(deleteCampaign ? "DELETE THIS CO-OP GAME AND ALL ITS SAVES?" : "DELETE THIS SAVE?");
    }
}
