using Hyperstrange.PBD;
using Hyperstrange.PBD.UI.Components;
using Hyperstrange.PBD.UI.Menus;
using PBDMP.Network;
using TMPro;
using UnityEngine;
using EventSystem = UnityEngine.EventSystems.EventSystem;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Il2CppInterop.Runtime;

namespace PBDMP.Plugin;

/// <summary>Own multiplayer controls; borrow only native UI assets and audited control components.</summary>
internal sealed class NativeMultiplayerUI : IDisposable
{
    internal GameObject Root { get; }
    internal GameObject Panel { get; }
    internal MainHyperButton MainEntry { get; private set; } = null!;
    internal MainHyperButton PauseEntry { get; private set; } = null!;
    internal MainHyperButton HostButton { get; }
    internal MainHyperButton JoinButton { get; }
    internal MainHyperButton DisconnectButton { get; }
    internal MainHyperButton BackButton { get; }
    internal TMP_InputField Address { get; }
    internal TMP_InputField Port { get; }
    internal TMP_Text Status { get; }
    internal TMP_Text Hud { get; }
    internal MainMenu MainMenu { get; private set; } = null!;
    private readonly GameObject _staging;
    private readonly GameObject _actionTemplate;
    private readonly GameObject _inputTemplate;
    private readonly TMP_Text _bodyTemplate;
    private readonly System.Action _toggle;
    private readonly List<(Selectable Button, Navigation Original)> _nativeNavigation = new();
    private Canvas _nativeCanvas;
    private MenuManager? _blockedManager;
    private CanvasGroup? _blockedGroup;
    private bool _previousManagerEnabled, _previousInteractable, _previousRaycasts;
    private GameObject? _previousSelected;
    private bool _modal;
    private readonly Il2CppSystem.Action _cancel;

    internal NativeMultiplayerUI(Canvas nativeCanvas, System.Action host, System.Action join,
        System.Action disconnect, System.Action toggle)
    {
        _nativeCanvas = nativeCanvas; _toggle = toggle;
        _cancel = (Il2CppSystem.Action)(System.Action)(() => { if (_modal) _toggle(); });
        var prompt = Require(nativeCanvas.transform, "MainCanvasGroup/PROMPT_Input/OverlayImage/Background");
        _actionTemplate = Require(prompt, "BUTTON_Confirm").gameObject;
        _inputTemplate = Require(prompt, "INPUT_Field/Image").gameObject;
        _bodyTemplate = Require(prompt, "Text").GetComponent<TMP_Text>();
        Root = new GameObject("PBDMP native themed UI");
        Root.AddComponent<RectTransform>();
        Object.DontDestroyOnLoad(Root);
        var canvas = Root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 30000;
        var scaler = Root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 1;
        Root.AddComponent<GraphicRaycaster>();
        _staging = new GameObject("PBDMP inactive UI staging");
        _staging.transform.SetParent(Root.transform, false); _staging.SetActive(false);

        Hud = TextAt(Root.transform, "Session status", "", new Vector2(800, 1040), new Vector2(1500, 44), 22);
        Hud.alignment = TextAlignmentOptions.Left;
        Panel = Rect("Multiplayer overlay", Root.transform, new Vector2(960, 540), new Vector2(1920, 1080));
        Stretch(Panel.GetComponent<RectTransform>());
        var dim = Panel.AddComponent<Image>();
        CopyImage(Require(nativeCanvas.transform, "MainCanvasGroup/PROMPT_Input/OverlayImage").GetComponent<Image>(), dim);
        Stretch(dim.rectTransform); dim.color = new Color(0, 0, 0, 0.88f); dim.raycastTarget = true;
        var box = Rect("Multiplayer panel", Panel.transform, new Vector2(960, 540), new Vector2(1060, 820));
        var boxRect = box.GetComponent<RectTransform>();
        boxRect.anchorMin = boxRect.anchorMax = new Vector2(0.5f, 0.5f); boxRect.anchoredPosition = Vector2.zero;
        var backing = box.AddComponent<Image>(); CopyImage(prompt.GetComponent<Image>(), backing);
        backing.type = Image.Type.Sliced; backing.pixelsPerUnitMultiplier = 2;
        backing.raycastTarget = true;
        var spiral = Rect("Native spiral", box.transform, new Vector2(530, 410), new Vector2(740, 740)).AddComponent<Image>();
        CopyImage(Require(prompt, "SpiralImage").GetComponent<Image>(), spiral);
        spiral.raycastTarget = false; spiral.color = new Color(1, 1, 1, 0.18f);

        var title = TextAt(box.transform, "Title", "MULTIPLAYER", new Vector2(530, 702), new Vector2(870, 64), 40);
        var mainTemplate = Require(nativeCanvas.transform, "MainCanvasGroup/MENU_Main/ButtonsLayout/BUTTON_Start").GetComponent<MainHyperButton>();
        title.font = mainTemplate.LabelText.font; title.fontSharedMaterial = mainTemplate.ClickableMaterial;
        title.color = Extension_UI.INTERACTABLE_BASE_COLOR;
        TextAt(box.transform, "Subtitle", "TWO PLAYERS  /  LOCAL CONNECTION", new Vector2(530, 644), new Vector2(850, 45), 25);
        TextAt(box.transform, "Address label", "HOST'S IP", new Vector2(240, 552), new Vector2(245, 52), 29).alignment = TextAlignmentOptions.Left;
        Address = InputAt(box.transform, "Host IP", Plugin.DefaultAddress, new Vector2(635, 552), new Vector2(590, 62));
        Address.characterLimit = 15;
        TextAt(box.transform, "Port label", "PORT", new Vector2(240, 466), new Vector2(245, 52), 29).alignment = TextAlignmentOptions.Left;
        Port = InputAt(box.transform, "TCP port", Plugin.DefaultPort.ToString(), new Vector2(455, 466), new Vector2(230, 62));
        Port.characterLimit = 5; Port.contentType = TMP_InputField.ContentType.IntegerNumber;
        Address.onSubmit.AddListener((UnityEngine.Events.UnityAction<string>)(_ => join()));
        Port.onSubmit.AddListener((UnityEngine.Events.UnityAction<string>)(_ => join()));
        TextAt(box.transform, "Port help", "HOSTING USES THE PORT ONLY", new Vector2(753, 466), new Vector2(350, 62), 21);
        HostButton = ActionAt(box.transform, "Host (P1)", new Vector2(237, 364), new Vector2(260, 66), host);
        JoinButton = ActionAt(box.transform, "Join (P2)", new Vector2(530, 364), new Vector2(260, 66), join);
        DisconnectButton = ActionAt(box.transform, "Disconnect", new Vector2(823, 364), new Vector2(260, 66), disconnect);
        Status = TextAt(box.transform, "Connection status", "Offline", new Vector2(530, 266), new Vector2(850, 108), 25);
        TextAt(box.transform, "Session guidance", "P1 controls campaign, enemies and combat. Doors are shared.\nHealth, armor and pickups are personal. Multiplayer saves disabled.",
            new Vector2(530, 159), new Vector2(850, 88), 24);
        BackButton = ActionAt(box.transform, "Back", new Vector2(530, 75), new Vector2(250, 60), toggle);
        TextAt(box.transform, "Shortcut", "F8 / ESC", new Vector2(854, 75), new Vector2(160, 44), 20);
        SetDialogNavigation();
        Panel.SetActive(false);
        EnsureEntries();
    }

    internal TMP_Text TextAt(Transform parent, string name, string value, Vector2 center, Vector2 size, int fontSize)
    {
        var text = Rect(name, parent, center, size).AddComponent<TextMeshProUGUI>();
        text.font = _bodyTemplate.font; text.fontSharedMaterial = _bodyTemplate.fontSharedMaterial;
        text.color = Extension_UI.INTERACTABLE_HIGHLIGHTED_COLOR; text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center; text.enableWordWrapping = true;
        text.raycastTarget = false; text.text = value;
        return text;
    }

    internal void Refresh()
    {
        if (MainEntry == null || PauseEntry == null) EnsureEntries();
        RepairNavigation(MainEntry!.transform.parent);
        RepairNavigation(PauseEntry!.transform.parent);
        if (_modal && EventSystem.current != null &&
            (EventSystem.current.currentSelectedGameObject == null ||
             !EventSystem.current.currentSelectedGameObject.transform.IsChildOf(Panel.transform)))
            EventSystem.current.SetSelectedGameObject(HostButton.gameObject);
        if (!_modal) return;
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            var selected = EventSystem.current?.currentSelectedGameObject;
            var order = new Selectable[] { Address, Port, HostButton, JoinButton, DisconnectButton, BackButton }
                .Where(s => s.interactable).ToArray();
            var index = Array.FindIndex(order, s => selected != null && s.gameObject.Pointer == selected.Pointer);
            var step = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? -1 : 1;
            EventSystem.current?.SetSelectedGameObject(order[(index + step + order.Length) % order.Length].gameObject);
        }
        if (!InputManager.JoystickEnabled && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space)))
        {
            var button = EventSystem.current?.currentSelectedGameObject?.GetComponent<Button>();
            if (button != null && button.IsActive() && button.IsInteractable())
                button.OnPointerClick(new UnityEngine.EventSystems.PointerEventData(EventSystem.current)
                    { button = UnityEngine.EventSystems.PointerEventData.InputButton.Left });
        }
    }

    internal void SetSession(SessionSnapshot snapshot)
    {
        SetButtonState(HostButton, snapshot.Role == SessionRole.Offline);
        SetButtonState(JoinButton, snapshot.Role == SessionRole.Offline);
        SetButtonState(DisconnectButton, snapshot.Role != SessionRole.Offline);
        Address.interactable = snapshot.Role == SessionRole.Offline;
        Port.interactable = snapshot.Role == SessionRole.Offline;
        SetDialogNavigation();
    }

    internal void SetModal(bool open)
    {
        if (_modal == open) return;
        _modal = open;
        if (open)
        {
            _previousSelected = EventSystem.current?.currentSelectedGameObject;
            _blockedManager = Object.FindObjectOfType<MenuManager>();
            _blockedGroup = _nativeCanvas.transform.Find("MainCanvasGroup")?.GetComponent<CanvasGroup>();
            if (_blockedManager != null) { _previousManagerEnabled = _blockedManager.enabled; _blockedManager.enabled = false; }
            InputManager.OnMenuCancelDown = Il2CppSystem.Delegate.Combine(InputManager.OnMenuCancelDown, _cancel).Cast<Il2CppSystem.Action>();
            if (_blockedGroup != null)
            {
                _previousInteractable = _blockedGroup.interactable; _previousRaycasts = _blockedGroup.blocksRaycasts;
                _blockedGroup.interactable = false; _blockedGroup.blocksRaycasts = false;
            }
            Panel.SetActive(true);
            EventSystem.current?.SetSelectedGameObject(HostButton.interactable ? HostButton.gameObject : BackButton.gameObject);
        }
        else
        {
            Panel.SetActive(false);
            InputManager.OnMenuCancelDown = Il2CppSystem.Delegate.Remove(InputManager.OnMenuCancelDown, _cancel)?.Cast<Il2CppSystem.Action>();
            if (_blockedGroup != null) { _blockedGroup.interactable = _previousInteractable; _blockedGroup.blocksRaycasts = _previousRaycasts; }
            if (_blockedManager != null) _blockedManager.enabled = _previousManagerEnabled;
            _blockedGroup = null; _blockedManager = null;
            EventSystem.current?.SetSelectedGameObject(_previousSelected != null && _previousSelected.activeInHierarchy ? _previousSelected : null);
            _previousSelected = null;
        }
    }

    private void EnsureEntries()
    {
        var canvas = MenuManager.MainCanvas;
        if (canvas == null) return;
        _nativeCanvas = canvas;
        var main = Require(canvas.transform, "MainCanvasGroup/MENU_Main");
        MainMenu = main.GetComponent<MainMenu>();
        if (MainEntry == null) MainEntry = Entry(Require(main, "ButtonsLayout"), "BUTTON_Start", "BUTTON_Horde", true);
        var pause = Require(canvas.transform, "MainCanvasGroup/MENU_Pause/ButtonsLayout");
        if (PauseEntry == null) PauseEntry = Entry(pause, "BUTTON_Settings", "BUTTON_Load", false);
    }

    private MainHyperButton Entry(Transform parent, string templateName, string afterName, bool main)
    {
        var existing = parent.Find("BUTTON_PBDMP_Multiplayer");
        if (existing != null) return existing.GetComponent<MainHyperButton>();
        var copy = InactiveCopy(Require(parent, templateName).gameObject);
        copy.name = "BUTTON_PBDMP_Multiplayer";
        var button = copy.GetComponent<MainHyperButton>();
        PrepareButton(button, "Multiplayer", _toggle);
        copy.transform.SetParent(parent, false);
        copy.transform.SetSiblingIndex(Require(parent, afterName).GetSiblingIndex() + 1);
        copy.SetActive(true); button.SetInteractable();
        Plugin.Logger.LogInfo("Native Multiplayer entry added to " + (main ? "main menu below Survival Mode" : "pause menu"));
        return button;
    }

    private MainHyperButton ActionAt(Transform parent, string label, Vector2 center, Vector2 size, System.Action action)
    {
        var copy = InactiveCopy(_actionTemplate); copy.name = label;
        foreach (var fitter in copy.GetComponentsInChildren<ContentSizeFitter>(true)) Object.DestroyImmediate(fitter);
        foreach (var layout in copy.GetComponentsInChildren<LayoutGroup>(true)) Object.DestroyImmediate(layout);
        foreach (var resetter in copy.GetComponentsInChildren<LayoutResetter>(true)) Object.DestroyImmediate(resetter);
        var button = copy.GetComponent<MainHyperButton>();
        PrepareButton(button, label, action);
        Place(copy.GetComponent<RectTransform>(), parent, center, size);
        var image = copy.transform.Find("Image").GetComponent<Image>(); Stretch(image.rectTransform);
        button.LabelText.fontSize = 27; button.LabelText.enableAutoSizing = false;
        Stretch(button.LabelText.rectTransform); button.LabelText.margin = new Vector4(10, 0, 10, 0);
        button.LabelText.alignment = TextAlignmentOptions.Center;
        copy.SetActive(true); button.SetInteractable();
        return button;
    }

    private TMP_InputField InputAt(Transform parent, string name, string value, Vector2 center, Vector2 size)
    {
        var copy = InactiveCopy(_inputTemplate); copy.name = name;
        Place(copy.GetComponent<RectTransform>(), parent, center, size);
        var field = copy.GetComponentInChildren<TMP_InputField>(true);
        field.onValueChanged = new TMP_InputField.OnChangeEvent();
        field.onEndEdit = new TMP_InputField.SubmitEvent(); field.onSubmit = new TMP_InputField.SubmitEvent();
        field.onSelect = new TMP_InputField.SelectionEvent(); field.onDeselect = new TMP_InputField.SelectionEvent();
        field.onTextSelection = new TMP_InputField.TextSelectionEvent(); field.onEndTextSelection = new TMP_InputField.TextSelectionEvent();
        field.textComponent.fontSize = 28; field.textComponent.enableAutoSizing = false;
        Stretch(field.GetComponent<RectTransform>());
        field.textViewport.offsetMin = new Vector2(14, 4); field.textViewport.offsetMax = new Vector2(-14, -4);
        field.text = value; field.lineType = TMP_InputField.LineType.SingleLine;
        copy.SetActive(true);
        return field;
    }

    private GameObject InactiveCopy(GameObject source)
    {
        var copy = Object.Instantiate(source, _staging.transform, false);
        copy.SetActive(false);
        foreach (var localized in copy.GetComponentsInChildren<LocalizedText>(true)) localized.LocalizedLabel = null!;
        return copy;
    }

    private static void PrepareButton(MainHyperButton button, string label, System.Action action)
    {
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener((UnityEngine.Events.UnityAction)action);
        button.LabelText.LocalizedLabel = null!; button.LabelText.text = label;
        button._doubleClickAction = null!;
        var navigation = button.navigation; navigation.mode = Navigation.Mode.None; button.navigation = navigation;
    }

    private void RepairNavigation(Transform parent)
    {
        var all = parent.GetComponentsInChildren<Selectable>(true).ToArray();
        foreach (var button in all)
            if (button.name != "BUTTON_PBDMP_Multiplayer" && !_nativeNavigation.Any(item => item.Button.Pointer == button.Pointer))
                _nativeNavigation.Add((button, button.navigation));
        var enabled = all.Where(b => b.gameObject.activeInHierarchy && b.interactable).OrderBy(b => b.transform.GetSiblingIndex()).ToArray();
        for (var i = 0; i < enabled.Length; i++)
        {
            var navigation = enabled[i].navigation; navigation.mode = Navigation.Mode.Explicit;
            navigation.selectOnUp = enabled[(i + enabled.Length - 1) % enabled.Length];
            navigation.selectOnDown = enabled[(i + 1) % enabled.Length];
            enabled[i].navigation = navigation;
        }
    }

    private void SetDialogNavigation()
    {
        var controls = new Selectable[] { Address, Port, HostButton, JoinButton, DisconnectButton, BackButton }
            .Where(s => s.interactable).ToArray();
        for (var i = 0; i < controls.Length; i++)
        {
            var navigation = controls[i].navigation; navigation.mode = Navigation.Mode.Explicit;
            navigation.selectOnUp = controls[(i + controls.Length - 1) % controls.Length];
            navigation.selectOnDown = controls[(i + 1) % controls.Length];
            navigation.selectOnLeft = navigation.selectOnUp; navigation.selectOnRight = navigation.selectOnDown;
            controls[i].navigation = navigation;
        }
    }

    private static void SetButtonState(MainHyperButton button, bool enabled)
    {
        if (button.interactable == enabled) return;
        if (enabled) button.SetInteractable(); else button.SetDisabled();
        button.interactable = enabled;
    }

    private static void CopyImage(Image source, Image target)
    { target.sprite = source.sprite; target.material = source.material; target.color = source.color; target.type = source.type; }

    private static Transform Require(Transform root, string path) => root.Find(path) ?? throw new InvalidOperationException("Native UI template missing: " + path);
    private static GameObject Rect(string name, Transform parent, Vector2 center, Vector2 size)
    { var obj = new GameObject(name); Place(obj.AddComponent<RectTransform>(), parent, center, size); return obj; }
    private static void Place(RectTransform rect, Transform parent, Vector2 center, Vector2 size)
    {
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f); rect.anchoredPosition = center; rect.sizeDelta = size;
        rect.localScale = Vector3.one; rect.localRotation = Quaternion.identity;
    }
    private static void Stretch(RectTransform rect)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }

    public void Dispose()
    {
        SetModal(false);
        foreach (var item in _nativeNavigation) if (item.Button != null) item.Button.navigation = item.Original;
        if (MainEntry != null) Object.Destroy(MainEntry.gameObject);
        if (PauseEntry != null) Object.Destroy(PauseEntry.gameObject);
        Object.Destroy(Root);
    }
}
