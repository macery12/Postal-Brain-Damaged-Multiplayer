using Hyperstrange.PBD;
using Hyperstrange.PBD.UI.Components;
using Hyperstrange.PBD.UI.Menus;
using PBDMP.Network;
using TMPro;
using UnityEngine;
using EventSystem = UnityEngine.EventSystems.EventSystem;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PBDMP.Plugin;

/// <summary>Everything the lobby panel shows, rebuilt from session and Steam state every frame.</summary>
internal readonly record struct LobbyModel(SessionRole Role, LobbyPhase Phase, bool SteamAvailable, bool SteamSession,
    bool InviteOnly, bool Connected, CardModel First, CardModel Second, string Status);

/// <summary>Own multiplayer controls; borrow only native UI assets and audited control components.</summary>
internal sealed class NativeMultiplayerUI : IDisposable
{
    internal GameObject Root { get; }
    internal GameObject Panel { get; }
    internal MainHyperButton MainEntry { get; private set; } = null!;
    internal MainHyperButton PauseEntry { get; private set; } = null!;
    internal MainHyperButton SteamHostButton { get; }
    internal MainHyperButton InviteButton { get; }
    internal MainHyperButton PrivacyButton { get; }
    internal MainHyperButton DirectButton { get; }
    internal MainHyperButton DisconnectButton { get; }
    internal MainHyperButton BackButton { get; }
    internal MainHyperButton HostButton { get; }
    internal MainHyperButton JoinButton { get; }
    internal MainHyperButton DirectBackButton { get; }
    internal TMP_InputField Address { get; }
    internal TMP_InputField Port { get; }
    internal TMP_Text Status { get; }
    internal TMP_Text DirectStatus { get; }
    internal PlayerCard FirstCard { get; }
    internal PlayerCard SecondCard { get; }
    internal NativePrompt Prompt { get; }
    internal NativeHud Hud { get; }
    internal GameObject DirectDialog { get; }
    internal MainMenu MainMenu { get; private set; } = null!;
    private readonly NativeUiKit _kit;
    private readonly RectTransform _spiral, _directSpiral;
    private readonly System.Action _toggle;
    private readonly List<(Selectable Button, Navigation Original)> _nativeNavigation = new();
    private Canvas _nativeCanvas;
    private MenuManager? _blockedManager;
    private CanvasGroup? _blockedGroup;
    private bool _previousManagerEnabled, _previousInteractable, _previousRaycasts;
    private GameObject? _previousSelected;
    private bool _modal;
    private int _cancelFrame = -1;
    private string _navigation = "";
    private LobbyPhase _phase;
    private readonly Il2CppSystem.Action _cancel;

    internal NativeMultiplayerUI(Canvas nativeCanvas, System.Action hostSteam, System.Action invite, System.Action privacy,
        System.Action host, System.Action join, System.Action disconnect, System.Action toggle)
    {
        _nativeCanvas = nativeCanvas; _toggle = toggle;
        _cancel = (Il2CppSystem.Action)(System.Action)(() => { if (_modal && !Cancel()) _toggle(); });
        Root = new GameObject("PBDMP native themed UI");
        Root.AddComponent<RectTransform>();
        Object.DontDestroyOnLoad(Root);
        var canvas = Root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 30000;
        var scaler = Root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 1;
        Root.AddComponent<GraphicRaycaster>();
        _kit = new NativeUiKit(nativeCanvas, Root.transform);
        Hud = new NativeHud(_kit, Root.transform);

        Panel = NativeUiKit.Rect("Multiplayer overlay", Root.transform, new Vector2(960, 540), new Vector2(1920, 1080));
        NativeUiKit.Stretch(Panel.GetComponent<RectTransform>());
        var dim = Panel.AddComponent<Image>();
        NativeUiKit.CopyImage(NativeUiKit.Require(nativeCanvas.transform, "MainCanvasGroup/PROMPT_Input/OverlayImage").GetComponent<Image>(), dim);
        NativeUiKit.Stretch(dim.rectTransform); dim.color = new Color(0, 0, 0, 0.88f); dim.raycastTarget = true;

        // 1340 units wide stays inside a 5:4 viewport at the 1080-unit reference height.
        var box = _kit.BoxAt(Panel.transform, "Multiplayer panel", new Vector2(1340, 820), out _spiral).transform;
        _kit.HeadingAt(box, "Title", "MULTIPLAYER", new Vector2(670, 712), new Vector2(1100, 66), 42);
        _kit.TextAt(box, "Subtitle", "STEAM CO-OP  /  TWO PLAYERS", new Vector2(670, 656), new Vector2(1100, 40), 24);
        SteamHostButton = Action(box, "Host Steam game", 0, hostSteam);
        InviteButton = Action(box, "Invite friend", 1, invite);
        PrivacyButton = Action(box, "Lobby: friends only", 2, privacy);
        DirectButton = Action(box, "Direct IP...", 3, () => ShowDirect(true));
        DisconnectButton = Action(box, "Leave lobby", 4, disconnect);
        BackButton = Action(box, "Back", 5, toggle);
        FirstCard = new PlayerCard(_kit, box, "Player slot 1", new Vector2(960, 506), new Vector2(660, 184));
        SecondCard = new PlayerCard(_kit, box, "Player slot 2", new Vector2(960, 302), new Vector2(660, 184));
        Status = _kit.TextAt(box, "Connection status", "Offline", new Vector2(670, 112), new Vector2(1140, 70), 23);
        _kit.TextAt(box, "Shortcut", "F8 / ESC", new Vector2(1160, 60), new Vector2(200, 36), 20);

        DirectDialog = NativeUiKit.Rect("Direct IP dialog", Panel.transform, Vector2.zero, Vector2.zero);
        NativeUiKit.Stretch(DirectDialog.GetComponent<RectTransform>());
        _kit.DimAt(DirectDialog.transform, "Direct dim", 0.7f);
        var direct = _kit.BoxAt(DirectDialog.transform, "Direct IP panel", new Vector2(1000, 580), out _directSpiral).transform;
        _kit.HeadingAt(direct, "Title", "DIRECT IP", new Vector2(500, 474), new Vector2(800, 58), 38).alignment = TextAlignmentOptions.Center;
        _kit.TextAt(direct, "Subtitle", "SAME NETWORK, OR A FORWARDED TCP PORT", new Vector2(500, 420), new Vector2(860, 38), 22);
        _kit.TextAt(direct, "Address label", "HOST'S IP", new Vector2(205, 336), new Vector2(240, 52), 28).alignment = TextAlignmentOptions.Left;
        Address = _kit.InputAt(direct, "Host IP", Plugin.DefaultAddress, new Vector2(600, 336), new Vector2(540, 62));
        Address.characterLimit = 15;
        _kit.TextAt(direct, "Port label", "PORT", new Vector2(205, 252), new Vector2(240, 52), 28).alignment = TextAlignmentOptions.Left;
        Port = _kit.InputAt(direct, "TCP port", Plugin.DefaultPort.ToString(), new Vector2(445, 252), new Vector2(230, 62));
        Port.characterLimit = 5; Port.contentType = TMP_InputField.ContentType.IntegerNumber;
        Address.onSubmit.AddListener((UnityEngine.Events.UnityAction<string>)(_ => join()));
        Port.onSubmit.AddListener((UnityEngine.Events.UnityAction<string>)(_ => join()));
        _kit.TextAt(direct, "Port help", "HOSTING USES THE PORT ONLY", new Vector2(745, 252), new Vector2(330, 62), 20);
        HostButton = _kit.ActionAt(direct, "Host (P1)", new Vector2(220, 154), new Vector2(250, 64), host);
        JoinButton = _kit.ActionAt(direct, "Join (P2)", new Vector2(500, 154), new Vector2(250, 64), join);
        DirectBackButton = _kit.ActionAt(direct, "Back", new Vector2(780, 154), new Vector2(250, 64), () => ShowDirect(false));
        DirectStatus = _kit.TextAt(direct, "Direct status", "", new Vector2(500, 72), new Vector2(880, 52), 21);
        DirectDialog.SetActive(false);

        Prompt = new NativePrompt(_kit, Panel.transform);
        Panel.SetActive(false);
        EnsureEntries();
    }

    private MainHyperButton Action(Transform box, string label, int row, System.Action action)
    {
        var button = _kit.ActionAt(box, label, new Vector2(310, 566 - row * 76), new Vector2(420, 62), action);
        // Labels change with lobby state; the longest ones shrink to stay on the button art.
        button.LabelText.enableAutoSizing = true; button.LabelText.fontSizeMin = 18; button.LabelText.fontSizeMax = 27;
        button.LabelText.margin = new Vector4(18, 0, 34, 0);
        return button;
    }

    internal TMP_Text TextAt(Transform parent, string name, string value, Vector2 center, Vector2 size, int fontSize) =>
        _kit.TextAt(parent, name, value, center, size, fontSize);

    internal bool DirectOpen => DirectDialog.activeSelf;

    internal void ShowDirect(bool open)
    {
        if (DirectDialog.activeSelf == open) return;
        DirectDialog.SetActive(open);
        Prompt.Root.transform.SetAsLastSibling();
        _navigation = "";
        if (_modal) EventSystem.current?.SetSelectedGameObject(First()?.gameObject);
    }

    /// <summary>Closes the topmost dialog layer. False when only the panel itself remains.</summary>
    internal bool Cancel()
    {
        if (Time.frameCount == _cancelFrame) return true;
        if (Prompt.Open) { _cancelFrame = Time.frameCount; Prompt.Cancel(); return true; }
        if (DirectOpen) { _cancelFrame = Time.frameCount; ShowDirect(false); return true; }
        return false;
    }

    private Selectable[] Controls()
    {
        if (Prompt.Open) return Prompt.Controls;
        if (DirectOpen) return new Selectable[] { Address, Port, HostButton, JoinButton, DirectBackButton }.Where(s => s.interactable).ToArray();
        return new Selectable[] { SteamHostButton, InviteButton, PrivacyButton, DirectButton, DisconnectButton, BackButton }.Where(s => s.interactable).ToArray();
    }

    private Selectable? First() => Controls().FirstOrDefault();

    private Transform Layer() => Prompt.Open ? Prompt.Root.transform : DirectOpen ? DirectDialog.transform : Panel.transform;

    internal void Refresh()
    {
        if (MainEntry == null || PauseEntry == null) EnsureEntries();
        RepairNavigation(MainEntry!.transform.parent);
        RepairNavigation(PauseEntry!.transform.parent);
        Hud.Tick();
        if (!_modal) return;
        _spiral.localRotation = Quaternion.Euler(0, 0, -Time.unscaledTime * 4f);
        _directSpiral.localRotation = Quaternion.Euler(0, 0, -Time.unscaledTime * 5f);
        Prompt.Tick();
        SetDialogNavigation();
        var current = EventSystem.current?.currentSelectedGameObject;
        if (EventSystem.current != null && (current == null || !current.activeInHierarchy || !current.transform.IsChildOf(Layer()) ||
            current.GetComponent<Selectable>() is { } blocked && !blocked.interactable))
            EventSystem.current.SetSelectedGameObject(First()?.gameObject);
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            var order = Controls();
            if (order.Length > 0)
            {
                var selected = EventSystem.current?.currentSelectedGameObject;
                var index = Array.FindIndex(order, s => selected != null && s.gameObject.Pointer == selected.Pointer);
                var step = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? -1 : 1;
                EventSystem.current?.SetSelectedGameObject(order[(index + step + order.Length) % order.Length].gameObject);
            }
        }
        if (!InputManager.JoystickEnabled && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space)))
        {
            var button = EventSystem.current?.currentSelectedGameObject?.GetComponent<Button>();
            if (button != null && button.IsActive() && button.IsInteractable())
                button.OnPointerClick(new UnityEngine.EventSystems.PointerEventData(EventSystem.current)
                    { button = UnityEngine.EventSystems.PointerEventData.InputButton.Left });
        }
    }

    internal void Apply(in LobbyModel model)
    {
        var offline = model.Role == SessionRole.Offline && model.Phase == LobbyPhase.None;
        var steamReady = model.SteamAvailable && offline;
        NativeUiKit.SetLabel(SteamHostButton, model.Phase == LobbyPhase.Creating ? "Creating lobby..." : "Host Steam game");
        NativeUiKit.SetButtonState(SteamHostButton, steamReady);
        NativeUiKit.SetButtonState(InviteButton, model.Phase == LobbyPhase.Hosting && !model.Connected);
        NativeUiKit.SetLabel(PrivacyButton, model.InviteOnly ? "Lobby: invite only" : "Lobby: friends only");
        NativeUiKit.SetButtonState(PrivacyButton, model.SteamAvailable && model.Role != SessionRole.Client &&
            model.Phase is LobbyPhase.None or LobbyPhase.Hosting);
        NativeUiKit.SetButtonState(DirectButton, offline);
        NativeUiKit.SetLabel(DisconnectButton, model.SteamSession || model.Phase != LobbyPhase.None ? "Leave lobby" : "Disconnect");
        NativeUiKit.SetButtonState(DisconnectButton, !offline);
        NativeUiKit.SetButtonState(HostButton, offline);
        NativeUiKit.SetButtonState(JoinButton, offline);
        Address.interactable = offline; Port.interactable = offline;
        FirstCard.Set(model.First); SecondCard.Set(model.Second);
        if (Status.text != model.Status) Status.text = model.Status;
        if (DirectStatus.text != model.Status) DirectStatus.text = model.Status;
        // A freshly opened lobby's next step is inviting someone, not leaving it.
        if (model.Phase != _phase && model.Phase == LobbyPhase.Hosting && _modal && !Prompt.Open && !DirectOpen)
            EventSystem.current?.SetSelectedGameObject(InviteButton.gameObject);
        _phase = model.Phase;
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
            _navigation = "";
            EventSystem.current?.SetSelectedGameObject(First()?.gameObject);
        }
        else
        {
            // Closing the panel (F8, or campaign travel) dismisses a prompt without answering it: a join in progress continues.
            Prompt.Hide();
            DirectDialog.SetActive(false);
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
        var main = NativeUiKit.Require(canvas.transform, "MainCanvasGroup/MENU_Main");
        MainMenu = main.GetComponent<MainMenu>();
        if (MainEntry == null) MainEntry = Entry(NativeUiKit.Require(main, "ButtonsLayout"), "BUTTON_Start", "BUTTON_Horde", true);
        var pause = NativeUiKit.Require(canvas.transform, "MainCanvasGroup/MENU_Pause/ButtonsLayout");
        if (PauseEntry == null) PauseEntry = Entry(pause, "BUTTON_Settings", "BUTTON_Load", false);
    }

    private MainHyperButton Entry(Transform parent, string templateName, string afterName, bool main)
    {
        var existing = parent.Find("BUTTON_PBDMP_Multiplayer");
        if (existing != null) return existing.GetComponent<MainHyperButton>();
        var copy = _kit.InactiveCopy(NativeUiKit.Require(parent, templateName).gameObject);
        copy.name = "BUTTON_PBDMP_Multiplayer";
        var button = copy.GetComponent<MainHyperButton>();
        NativeUiKit.PrepareButton(button, "Multiplayer", _toggle);
        copy.transform.SetParent(parent, false);
        copy.transform.SetSiblingIndex(NativeUiKit.Require(parent, afterName).GetSiblingIndex() + 1);
        copy.SetActive(true); button.SetInteractable();
        Plugin.Logger.LogInfo("Native Multiplayer entry added to " + (main ? "main menu below Survival Mode" : "pause menu"));
        return button;
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

    /// <summary>Links only the active layer's enabled controls, so arrows and sticks never reach a covered or disabled one.</summary>
    private void SetDialogNavigation()
    {
        var controls = Controls();
        var key = string.Join(",", controls.Select(c => c.GetInstanceID()));
        if (key == _navigation) return;
        _navigation = key;
        foreach (var hidden in new Selectable[] { SteamHostButton, InviteButton, PrivacyButton, DirectButton, DisconnectButton, BackButton,
                     Address, Port, HostButton, JoinButton, DirectBackButton, Prompt.ConfirmButton, Prompt.CancelButton })
        {
            var none = hidden.navigation; none.mode = Navigation.Mode.None; hidden.navigation = none;
        }
        for (var i = 0; i < controls.Length; i++)
        {
            var navigation = controls[i].navigation; navigation.mode = Navigation.Mode.Explicit;
            navigation.selectOnUp = controls[(i + controls.Length - 1) % controls.Length];
            navigation.selectOnDown = controls[(i + 1) % controls.Length];
            navigation.selectOnLeft = navigation.selectOnUp; navigation.selectOnRight = navigation.selectOnDown;
            controls[i].navigation = navigation;
        }
    }

    public void Dispose()
    {
        SetModal(false);
        foreach (var item in _nativeNavigation) if (item.Button != null) item.Button.navigation = item.Original;
        if (MainEntry != null) Object.Destroy(MainEntry.gameObject);
        if (PauseEntry != null) Object.Destroy(PauseEntry.gameObject);
        Object.Destroy(Root);
    }
}
