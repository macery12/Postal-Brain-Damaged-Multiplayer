using Hyperstrange.PBD;
using Hyperstrange.PBD.UI.Components;
using Hyperstrange.PBD.UI.Menus;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PBDMP.Plugin;

// The Multiplayer menu: a copy of the game's own main menu (spiral, lettering, button column)
// opened through MenuManager, so native fades, Escape/Back and controller navigation apply.
// Its native main-menu logic is replaced by a plain Menu, as the game's settings submenus use.
internal sealed class NativeCoopMenu
{
    internal Menu Menu { get; }
    internal MainHyperButton Continue { get; }
    internal MainHyperButton NewGame { get; }
    internal MainHyperButton Load { get; }
    internal MainHyperButton Survival { get; }
    internal MainHyperButton JoinFriend { get; }
    internal MainHyperButton Back { get; }
    private readonly TMP_Text _previewCaption, _previewTitle, _previewLine, _previewWhen;
    private readonly Image _thumbnail;
    private readonly GameObject _preview;
    private Texture2D? _thumbnailTexture;

    internal NativeCoopMenu(Transform group, NativeUiKit kit, System.Action continueGame, System.Action newGame, System.Action load,
        System.Action survival, System.Action join)
    {
        var source = NativeUiKit.Require(group, "MENU_Main").gameObject;
        var submenu = NativeUiKit.Require(group, "MENU_Settings_General").GetComponent<Menu>();
        var root = kit.InactiveCopy(source);
        root.name = "MENU_PBDMP_Multiplayer";
        Object.DestroyImmediate(root.GetComponent<MainMenu>());
        Menu = root.AddComponent<Menu>();
        Menu._canvasGroup = root.GetComponent<CanvasGroup>();
        Menu._fadeType = submenu._fadeType; Menu._fadeTime = submenu._fadeTime;
        Menu._disableOnEscape = submenu._disableOnEscape; Menu._overrideEscape = false;
        Menu._legendDatas = submenu._legendDatas;

        // The game's logo becomes the menu title in the main menu's own lettering.
        var logo = NativeUiKit.Require(root.transform, "LogoImage");
        logo.GetComponent<Image>().enabled = false;
        var title = kit.HeadingAt(logo, "Title", "MULTIPLAYER", Vector2.zero, Vector2.zero, 190);
        NativeUiKit.Stretch(title.rectTransform);
        var subtitle = kit.TextAt(logo, "Subtitle", "STEAM CO-OP", Vector2.zero, Vector2.zero, 64);
        NativeUiKit.Stretch(subtitle.rectTransform); subtitle.rectTransform.offsetMax = new Vector2(0, -330);

        var layout = NativeUiKit.Require(root.transform, "ButtonsLayout");
        MainHyperButton Take(string name, string label, System.Action action)
        {
            var button = NativeUiKit.Require(layout, name).GetComponent<MainHyperButton>();
            NativeUiKit.PrepareButton(button, label, action);
            return button;
        }
        Continue = Take("BUTTON_Continue", "Continue co-op", continueGame);
        NewGame = Take("BUTTON_Start", "New co-op game", newGame);
        Load = Take("BUTTON_Load", "Load co-op game", load);
        Survival = Take("BUTTON_Horde", "Survival co-op", survival);
        JoinFriend = Take("BUTTON_Settings", "Join a friend", join);
        Back = Take("BUTTON_Exit", "Back", () => MenuManager.Close());
        var keep = new[] { Continue, NewGame, Load, Survival, JoinFriend, Back };
        for (var i = layout.childCount - 1; i >= 0; i--)
        {
            var child = layout.GetChild(i);
            if (!keep.Any(b => b.transform.Pointer == child.Pointer)) Object.DestroyImmediate(child.gameObject);
        }
        for (var i = 0; i < keep.Length; i++) keep[i].transform.SetSiblingIndex(i);
        Navigate(keep);
        Menu._firstSelected = Continue;

        // Right half: what Continue co-op will load, like the native load screen's entries.
        _preview = NativeUiKit.Rect("Continue preview", root.transform, new Vector2(2750, 1150), new Vector2(1500, 1300));
        _previewCaption = kit.TextAt(_preview.transform, "Caption", "CONTINUE CO-OP", new Vector2(750, 1230), new Vector2(1500, 80), 56);
        _thumbnail = NativeUiKit.Rect("Thumbnail", _preview.transform, new Vector2(750, 780), new Vector2(1280, 720)).AddComponent<Image>();
        _thumbnail.preserveAspect = true;
        var titleBox = NativeUiKit.Rect("Campaign", _preview.transform, new Vector2(750, 330), new Vector2(1500, 130));
        // The game's own screen-header lettering (its main-menu font lacks punctuation glyphs).
        var header = Object.Instantiate(NativeUiKit.Require(group, "MENU_Load/HEADER_Load").gameObject, titleBox.transform, false);
        var headerText = header.GetComponent<LocalizedText>(); headerText.LocalizedLabel = null!;
        _previewTitle = headerText; NativeUiKit.Stretch(_previewTitle.rectTransform);
        _previewTitle.alignment = TextAlignmentOptions.Center; _previewTitle.enableAutoSizing = true;
        _previewTitle.fontSizeMin = 60; _previewTitle.fontSizeMax = 120; _previewTitle.enableWordWrapping = false;
        _previewLine = kit.TextAt(_preview.transform, "Details", "", new Vector2(750, 210), new Vector2(1500, 80), 60);
        _previewWhen = kit.TextAt(_preview.transform, "When", "", new Vector2(750, 110), new Vector2(1500, 80), 48);
        _preview.SetActive(false);

        root.transform.SetParent(group, false);
        root.transform.SetSiblingIndex(source.transform.GetSiblingIndex() + 1);
    }

    internal static void Navigate(MainHyperButton[] buttons)
    {
        for (var i = 0; i < buttons.Length; i++)
        {
            var navigation = buttons[i].navigation; navigation.mode = Navigation.Mode.Explicit;
            navigation.selectOnUp = buttons[(i + buttons.Length - 1) % buttons.Length];
            navigation.selectOnDown = buttons[(i + 1) % buttons.Length];
            buttons[i].navigation = navigation;
        }
    }

    internal string PreviewTitleForTest => _previewTitle.text;
    internal bool PreviewTitleRendersForTest => _previewTitle.isActiveAndEnabled && _previewTitle.textBounds.extents.x > 0;
    internal void Open()
    {
        MenuManager.Open(Menu);
        Menu.gameObject.SetActive(true);
    }

    // Shows the newest campaign; hides the preview and disables Continue when none exists.
    internal void ShowLatest(string? title, string details, string when, byte[]? thumbnail)
    {
        NativeUiKit.SetButtonState(Continue, title != null);
        _preview.SetActive(title != null);
        if (title == null) return;
        _previewTitle.text = title; _previewLine.text = details; _previewWhen.text = when;
        if (_thumbnailTexture != null) Object.Destroy(_thumbnailTexture);
        _thumbnailTexture = null; _thumbnail.sprite = null; _thumbnail.color = new Color(0.1f, 0.05f, 0.13f);
        if (thumbnail == null) return;
        _thumbnailTexture = new Texture2D(2, 2);
        if (!ImageConversion.LoadImage(_thumbnailTexture, thumbnail)) return;
        _thumbnail.sprite = Sprite.Create(_thumbnailTexture, new UnityEngine.Rect(0, 0, _thumbnailTexture.width, _thumbnailTexture.height), new Vector2(0.5f, 0.5f));
        _thumbnail.color = Color.white;
    }
}
