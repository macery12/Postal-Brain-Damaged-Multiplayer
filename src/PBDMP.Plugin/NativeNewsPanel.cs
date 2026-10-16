using Hyperstrange.PBD;
using Hyperstrange.PBD.UI.Components;
using Hyperstrange.PBD.UI.Menus;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using EventSystem = UnityEngine.EventSystems.EventSystem;

namespace PBDMP.Plugin;

/// <summary>Offline reporting information, visible only on the uncovered native main menu.</summary>
internal sealed class NativeNewsPanel
{
    internal const string RepositoryUrl = ReportDraft.RepositoryUrl;
    internal static string ReportUrl => IssueReport.Url;

    internal GameObject Root { get; }
    internal MainHyperButton ReportButton { get; }
    internal MainHyperButton RepositoryButton { get; }
    internal bool Visible => Root.activeSelf;
    // Native diagnostics can intercept a click without opening the desktop browser.
    internal System.Action<string> OpenLink { get; set; } = Application.OpenURL;
    private readonly RectTransform _rect, _spiral;
    private readonly Dictionary<int, (Selectable Button, Navigation Original)> _navigation = new();
    private readonly Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3> _corners = new(4);
    private int _width, _height;
    private Transform? _layout;
    private Selectable[] _menuButtons = Array.Empty<Selectable>();

    internal NativeNewsPanel(NativeUiKit kit, Transform parent)
    {
        Root = kit.BoxAt(parent, "PBDMP News", new Vector2(600, 770), out _spiral);
        _rect = Root.GetComponent<RectTransform>();
        _rect.anchorMin = _rect.anchorMax = new Vector2(1, .5f);
        _rect.pivot = new Vector2(1, .5f);

        kit.HeadingAt(Root.transform, "News title", "PBDMP NEWS", new Vector2(300, 690), new Vector2(500, 60), 40);
        kit.HeadingAt(Root.transform, "Welcome heading", "WELCOME TO PBDMP", new Vector2(300, 597), new Vector2(500, 44), 28);
        Body(kit, "Welcome", "Play POSTAL: Brain Damaged together.", 550, 50, 26);
        kit.HeadingAt(Root.transform, "Reporting heading", "FOUND SOMETHING BROKEN?", new Vector2(300, 468), new Vector2(500, 44), 27);
        Body(kit, "Reporting invitation", "PBDMP is in development. Help improve multiplayer by reporting bugs, sync issues and anything that doesn't behave as expected.", 379, 126, 26);
        Body(kit, "Reporting guidance", "Click REPORT A BUG for a form with software details and available last-session info filled in. Describe the issue and add any screenshots or logs.", 241, 144, 24);
        ReportButton = kit.ActionAt(Root.transform, "REPORT A BUG", new Vector2(300, 130), new Vector2(482, 62), () => Open(ReportUrl));
        RepositoryButton = kit.ActionAt(Root.transform, "GITHUB", new Vector2(300, 72), new Vector2(230, 46), () => Open(RepositoryUrl));
        RepositoryButton.LabelText.fontSize = 22;
        kit.TextAt(Root.transform, "Author and version", $"PBDMP by macery12 • v{Plugin.Version}", new Vector2(300, 25), new Vector2(520, 32), 21);
        Root.SetActive(false);
    }

    private void Open(string url)
    {
        try { OpenLink(url); }
        catch (Exception ex) { Plugin.Logger.LogWarning("Could not open the PBDMP project link: " + ex.Message); }
    }

    private void Body(NativeUiKit kit, string name, string text, float y, float height, int size)
    {
        var label = kit.TextAt(Root.transform, name, text, new Vector2(300, y), new Vector2(480, height), size);
        label.alignment = TextAlignmentOptions.Left;
    }

    internal void Tick(MainMenu main, MainHyperButton entry, bool modal)
    {
        if (modal || SessionManager.SessionActive || main == null || !main.gameObject.activeInHierarchy) { Hide(entry); return; }
        var manager = MenuManager.Instance;
        var group = MenuManager.MainCanvas?.transform.Find("MainCanvasGroup")?.GetComponent<CanvasGroup>();
        var current = manager != null ? manager.CurrentMenu : null;
        var visible = !modal && !SessionManager.SessionActive && main != null && main.gameObject.activeInHierarchy &&
            main.CanvasGroup != null && main.CanvasGroup.alpha > .99f && main.CanvasGroup.interactable &&
            group != null && group.alpha > .99f && group.interactable && manager != null && manager.enabled &&
            current != null && current.Pointer == main.Pointer;
        if (!visible) { Hide(entry); return; }

        if (!Visible || _width != Screen.width || _height != Screen.height || _layout == null || _layout.Pointer != entry.transform.parent.Pointer)
        {
            Place(entry.transform.parent);
            _width = Screen.width; _height = Screen.height; _layout = entry.transform.parent;
        }
        Root.SetActive(true);
        _spiral.localRotation = Quaternion.Euler(0, 0, -Time.unscaledTime * 3f);
        if (_navigation.Count == 0) LinkNavigation(entry);
        HandleKeyboard(entry);
    }

    private void Place(Transform layout)
    {
        var viewport = Root.transform.parent.GetComponent<RectTransform>();
        // Use the actual native menu bounds so height-scaled 5:4 and ultrawide menus remain separate.
        var right = viewport.rect.xMin;
        _menuButtons = layout.GetComponentsInChildren<Selectable>(true).ToArray();
        foreach (var button in _menuButtons)
        {
            if (!button.gameObject.activeInHierarchy) continue;
            button.GetComponent<RectTransform>().GetWorldCorners(_corners);
            foreach (var corner in _corners) right = Mathf.Max(right, viewport.InverseTransformPoint(corner).x);
        }
        var available = Mathf.Max(1, viewport.rect.xMax - right - 80);
        var scale = .65f * Mathf.Min(1, available / 600, (viewport.rect.height - 100) / 770);
        _rect.localScale = new Vector3(scale, scale, 1);
        _rect.anchoredPosition = new Vector2(-40, 0);
    }

    private void LinkNavigation(MainHyperButton entry)
    {
        foreach (var button in _menuButtons)
        {
            if (!button.gameObject.activeInHierarchy || !button.interactable) continue;
            var id = button.GetInstanceID();
            if (!_navigation.ContainsKey(id)) _navigation.Add(id, (button, button.navigation));
            var nav = button.navigation;
            nav.selectOnRight = ReportButton; button.navigation = nav;
        }
        var report = ReportButton.navigation; report.mode = Navigation.Mode.Explicit;
        report.selectOnUp = RepositoryButton; report.selectOnDown = RepositoryButton;
        report.selectOnLeft = entry; report.selectOnRight = RepositoryButton; ReportButton.navigation = report;
        var repo = RepositoryButton.navigation; repo.mode = Navigation.Mode.Explicit;
        repo.selectOnUp = ReportButton; repo.selectOnDown = ReportButton;
        repo.selectOnLeft = entry; repo.selectOnRight = entry; RepositoryButton.navigation = repo;
    }

    private void HandleKeyboard(MainHyperButton entry)
    {
        var events = EventSystem.current;
        if (events == null) return;
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            var controls = _menuButtons
                .Where(b => b.gameObject.activeInHierarchy && b.interactable).OrderBy(b => b.transform.GetSiblingIndex())
                .Concat(new Selectable[] { ReportButton, RepositoryButton }).ToArray();
            var selected = events.currentSelectedGameObject;
            var index = Array.FindIndex(controls, b => selected != null && b.gameObject.Pointer == selected.Pointer);
            var step = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? -1 : 1;
            events.SetSelectedGameObject(controls[(index + step + controls.Length) % controls.Length].gameObject);
        }
        var button = events.currentSelectedGameObject?.GetComponent<MainHyperButton>();
        if (!InputManager.JoystickEnabled && button != null && (button.Pointer == ReportButton.Pointer || button.Pointer == RepositoryButton.Pointer) &&
            (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space)))
            button.OnPointerClick(new PointerEventData(events) { button = PointerEventData.InputButton.Left });
    }

    private void Hide(MainHyperButton entry)
    {
        if (!Visible) return;
        var selected = EventSystem.current?.currentSelectedGameObject;
        if (selected != null && selected.transform.IsChildOf(Root.transform))
            EventSystem.current?.SetSelectedGameObject(entry != null && entry.gameObject.activeInHierarchy ? entry.gameObject : null);
        Root.SetActive(false);
        foreach (var item in _navigation.Values)
        {
            if (item.Button == null) continue;
            var nav = item.Button.navigation;
            nav.selectOnRight = item.Original.selectOnRight; item.Button.navigation = nav;
        }
        _navigation.Clear();
    }
}
