using Hyperstrange.Base;
using Hyperstrange.PBD;
using Hyperstrange.PBD.UI.Components;
using Hyperstrange.PBD.UI.Menus;
using PBDMP.Network;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PBDMP.Plugin;

// The pause menu's Save during a co-op session (P1): a copy of the game's own save screen listing a
// "New save" entry and the current campaign's manual saves. Saving goes through CoopSaveService.
internal sealed class NativeCoopSaveMenu
{
    internal Menu Menu { get; }
    internal MainHyperButton SaveButton { get; }
    internal MainHyperButton DeleteButton { get; }
    internal MainHyperButton BackButton { get; }
    internal int EntryCount => _entries.Count;
    internal string? SelectedSlot => _selected;
    private readonly SaveHyperButton _prefab;
    private readonly Transform _list;
    private readonly List<(SaveHyperButton Button, SaveListing? Save)> _entries = new();
    private readonly List<Texture2D> _textures = new();
    private string? _selected;

    internal NativeCoopSaveMenu(Transform group, NativeUiKit kit)
    {
        var source = NativeUiKit.Require(group, "MENU_Save").gameObject;
        _prefab = source.GetComponent<SaveMenu>()._saveButtonPrefab;
        var root = kit.InactiveCopy(source);
        root.name = "MENU_PBDMP_CoopSave";
        var copy = root.GetComponent<SaveMenu>();
        var fade = copy._fadeType; var fadeTime = copy._fadeTime; var legend = copy._legendDatas; var escape = copy._disableOnEscape;
        Object.DestroyImmediate(copy);
        Menu = root.AddComponent<Menu>();
        Menu._canvasGroup = root.GetComponent<CanvasGroup>();
        Menu._fadeType = fade; Menu._fadeTime = fadeTime; Menu._legendDatas = legend; Menu._disableOnEscape = escape;
        var header = NativeUiKit.Require(root.transform, "HEADER_Save").GetComponent<LocalizedText>();
        header.LocalizedLabel = null!; header.text = "Co-op save";
        _list = NativeUiKit.Require(root.transform, "Scroll View/Viewport/Content");
        for (var i = _list.childCount - 1; i >= 0; i--) Object.DestroyImmediate(_list.GetChild(i).gameObject);
        SaveButton = NativeUiKit.Require(root.transform, "BUTTON_Save").GetComponent<MainHyperButton>();
        DeleteButton = NativeUiKit.Require(root.transform, "BUTTON_Delete").GetComponent<MainHyperButton>();
        BackButton = NativeUiKit.Require(root.transform, "BUTTON_Back").GetComponent<MainHyperButton>();
        NativeUiKit.PrepareButton(SaveButton, "Save", SaveSelected);
        NativeUiKit.PrepareButton(DeleteButton, "Delete", DeleteSelected);
        NativeUiKit.PrepareButton(BackButton, "Back", () => MenuManager.Close());
        root.transform.SetParent(group, false);
        root.transform.SetSiblingIndex(source.transform.GetSiblingIndex() + 1);
    }

    internal void Open()
    {
        Refresh();
        MenuManager.Open(Menu);
        Menu.gameObject.SetActive(true);
    }

    private void Refresh()
    {
        foreach (var texture in _textures) Object.Destroy(texture);
        _textures.Clear(); _entries.Clear();
        for (var i = _list.childCount - 1; i >= 0; i--) Object.DestroyImmediate(_list.GetChild(i).gameObject);
        var level = SessionLifecycle.Current?.State.Level ?? "";
        Add(null, "New save", MultiplayerBehaviour.MapName(level), "", CoopSaveService.CaptureThumbnail());
        if (CoopSaveService.Current?.Store is { } store)
            foreach (var save in store.List().Where(s => s.Error == null && s.Slot.StartsWith("manual-", StringComparison.Ordinal)))
                Add(save, save.Name, MultiplayerBehaviour.MapName(save.Level), save.Created.ToLocalTime().ToString("MMM d, yyyy  HH:mm", System.Globalization.CultureInfo.InvariantCulture), store.Thumbnail(save.Slot));
        Select(null);
        NativeUiKit.SetButtonState(SaveButton, true);
        for (var i = 0; i < _entries.Count; i++)
        {
            var navigation = _entries[i].Button.navigation; navigation.mode = Navigation.Mode.Explicit;
            navigation.selectOnUp = i > 0 ? _entries[i - 1].Button : null;
            navigation.selectOnDown = i + 1 < _entries.Count ? _entries[i + 1].Button : SaveButton;
            _entries[i].Button.navigation = navigation;
        }
        Menu._firstSelected = _entries[0].Button;
    }

    private void Add(SaveListing? save, string type, string level, string date, byte[]? thumbnail)
    {
        var entry = Object.Instantiate(_prefab, _list, false);
        entry.gameObject.SetActive(true);
        foreach (var localized in entry.GetComponentsInChildren<LocalizedText>(true)) localized.LocalizedLabel = null!;
        entry.TypeText.text = type; entry.LevelText.text = level; entry.DateText.text = date;
        if (entry.ObsoleteVignette != null) entry.ObsoleteVignette.SetActive(false);
        if (entry.DLCVignette != null) entry.DLCVignette.SetActive(false);
        entry.onClick = new Button.ButtonClickedEvent();
        var slot = save?.Slot;
        entry.onClick.AddListener((UnityEngine.Events.UnityAction)(System.Action)(() => Select(slot)));
        entry.SetDoubleClickAction((Il2CppSystem.Action)(System.Action)(() => { Select(slot); SaveSelected(); }));
        if (thumbnail != null)
        {
            var texture = new Texture2D(2, 2);
            if (ImageConversion.LoadImage(texture, thumbnail))
            { _textures.Add(texture); entry.ThumbnailImage.sprite = Sprite.Create(texture, new UnityEngine.Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f)); }
            else Object.Destroy(texture);
        }
        _entries.Add((entry, save));
    }

    internal void Select(string? slot)
    {
        _selected = slot;
        foreach (var (button, save) in _entries) { if (save?.Slot == slot) button.SetChecked(); else button.SetInteractable(); }
        NativeUiKit.SetButtonState(DeleteButton, slot != null);
    }

    private void SaveSelected()
    {
        if (CoopSaveService.Current is not { } service) return;
        if (_selected == null)
        {
            var map = MultiplayerBehaviour.MapName(SessionLifecycle.Current?.State.Level ?? "");
            MenuManager.OpenInputPrompt((Il2CppSystem.Action<string>)(System.Action<string>)(name =>
            {
                name = name.Trim(); if (name.Length == 0) name = map + " (co-op)";
                service.RequestSave("manual-" + Guid.NewGuid().ToString("N"), name);
            }), ScriptableObject.CreateInstance<LocalizedString>(), map + " (co-op)", false);
            MultiplayerBehaviour.SetPromptText("PROMPT_Input", "NAME THIS SAVE");
            return;
        }
        var slot = _selected; var existing = _entries.First(e => e.Save?.Slot == slot).Save!;
        MenuManager.OpenConfirmationPrompt((Il2CppSystem.Action)(System.Action)(() => service.RequestSave(slot, existing.Name)),
            ScriptableObject.CreateInstance<LocalizedString>());
        MultiplayerBehaviour.SetPromptText("OVERWRITE THIS SAVE?");
    }

    private void DeleteSelected()
    {
        if (_selected is not { } slot || CoopSaveService.Current?.Store is not { } store) return;
        MenuManager.OpenConfirmationPrompt((Il2CppSystem.Action)(System.Action)(() =>
        {
            try { store.Delete(slot); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
            { Plugin.Logger.LogWarning("Co-op save could not be deleted: " + error.Message); }
            Refresh();
        }), ScriptableObject.CreateInstance<LocalizedString>());
        MultiplayerBehaviour.SetPromptText("DELETE THIS SAVE?");
    }
}
