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

// Join a friend: a copy of the game's own load screen listing Steam friends who are hosting PBDMP, as
// the game's own save entries (avatar, name, where they are, players). The list refreshes while open.
internal sealed class NativeCoopJoinMenu
{
    internal Menu Menu { get; }
    internal MainHyperButton JoinButton { get; }
    internal MainHyperButton RefreshButton { get; }
    internal MainHyperButton DirectButton { get; }
    internal MainHyperButton BackButton { get; }
    internal FriendHost? Selected { get; private set; }
    internal int EntryCount => _entries.Count;
    internal string EmptyText => _empty.gameObject.activeSelf ? _empty.text : "";
    internal string EntryTextForTest(int index) => _entries[index].Button.TypeText.text + " | " + _entries[index].Button.LevelText.text + " | " + _entries[index].Button.DateText.text;
    /// <summary>Fixture hook: lists these instead of the real Steam scan.</summary>
    internal IReadOnlyList<FriendHost>? TestHosts { get; set; }
    private readonly SaveHyperButton _prefab;
    private readonly Transform _list;
    private readonly TMP_Text _empty;
    private readonly List<(SaveHyperButton Button, FriendHost Host)> _entries = new();
    private readonly List<Sprite> _sprites = new();
    private readonly Func<IReadOnlyList<FriendHost>> _scan;
    private readonly Func<string> _unavailable;
    private readonly Func<ulong, Texture2D?> _avatar;
    private readonly System.Action<FriendHost> _join;
    private readonly System.Action _refresh;
    private string? _signature;
    private float _nextScan;

    internal NativeCoopJoinMenu(Transform group, NativeUiKit kit, Func<IReadOnlyList<FriendHost>> scan, Func<string> unavailable,
        Func<ulong, Texture2D?> avatar, System.Action<FriendHost> join, System.Action refresh, System.Action direct)
    {
        _scan = scan; _unavailable = unavailable; _avatar = avatar; _join = join; _refresh = refresh;
        var source = NativeUiKit.Require(group, "MENU_Load").gameObject;
        _prefab = source.GetComponent<LoadMenu>()._saveButtonPrefab;
        var root = kit.InactiveCopy(source);
        root.name = "MENU_PBDMP_CoopJoin";
        var copy = root.GetComponent<LoadMenu>();
        var fade = copy._fadeType; var fadeTime = copy._fadeTime; var legend = copy._legendDatas; var escape = copy._disableOnEscape;
        Object.DestroyImmediate(copy);
        Menu = root.AddComponent<Menu>();
        Menu._canvasGroup = root.GetComponent<CanvasGroup>();
        Menu._fadeType = fade; Menu._fadeTime = fadeTime; Menu._legendDatas = legend; Menu._disableOnEscape = escape;
        var header = NativeUiKit.Require(root.transform, "HEADER_Load").GetComponent<LocalizedText>();
        header.LocalizedLabel = null!; header.text = "Join a friend";
        _list = NativeUiKit.Require(root.transform, "Scroll View/Viewport/Content");
        Clear();

        JoinButton = NativeUiKit.Require(root.transform, "BUTTON_Load").GetComponent<MainHyperButton>();
        RefreshButton = NativeUiKit.Require(root.transform, "BUTTON_Delete").GetComponent<MainHyperButton>();
        BackButton = NativeUiKit.Require(root.transform, "BUTTON_Back").GetComponent<MainHyperButton>();
        NativeUiKit.PrepareButton(JoinButton, "Join", JoinSelected);
        NativeUiKit.PrepareButton(RefreshButton, "Refresh", Refresh);
        NativeUiKit.PrepareButton(BackButton, "Back", () => MenuManager.Close());
        DirectButton = Object.Instantiate(RefreshButton.gameObject, RefreshButton.transform.parent, false).GetComponent<MainHyperButton>();
        DirectButton.name = "BUTTON_DirectIP";
        DirectButton.GetComponent<RectTransform>().anchoredPosition += new Vector2(0, -95);
        NativeUiKit.PrepareButton(DirectButton, "Direct IP...", direct);
        _empty = kit.TextAt(root.transform, "No friends hosting", "", new Vector2(1920, 1080), new Vector2(2400, 300), 72);
        _empty.gameObject.SetActive(false);

        root.transform.SetParent(group, false);
        root.transform.SetSiblingIndex(source.transform.GetSiblingIndex() + 1);
    }

    internal void Open()
    {
        _signature = null; Scan();
        MenuManager.Open(Menu);
        Menu.gameObject.SetActive(true);
    }

    // Friends start and stop hosting while the screen is open; the entries are rebuilt only when something changed.
    internal void Tick()
    {
        if (!Menu.gameObject.activeInHierarchy || Time.unscaledTime < _nextScan) return;
        Scan();
    }

    private void Refresh() { _refresh(); _signature = null; Scan(); }

    private void Scan()
    {
        _nextScan = Time.unscaledTime + 3f;
        IReadOnlyList<FriendHost> hosts;
        try { hosts = TestHosts ?? _scan(); }
        catch (Exception error) { Plugin.Logger.LogWarning("Steam friends could not be listed: " + error.Message); hosts = Array.Empty<FriendHost>(); }
        var unavailable = TestHosts == null ? _unavailable() : "";
        var rows = hosts.OrderBy(h => h.Advert == null || h.Advert.Problem(Plugin.Version, Protocol.Version, Plugin.LobbyBuild) != null)
            .ThenBy(h => h.Name, StringComparer.OrdinalIgnoreCase)
            .Select(h => (Host: h, Where: Where(h.Advert), Status: Status(h.Advert), Avatar: _avatar(h.Friend))).ToList();
        var signature = unavailable + "\n" + string.Join("\n", rows.Select(r => $"{r.Host.Lobby}|{r.Host.Name}|{r.Where}|{r.Status}|{r.Avatar != null}"));
        if (signature == _signature) return;
        _signature = signature;

        var focused = EventSystem.current != null && _entries.Any(e => e.Button.gameObject == EventSystem.current.currentSelectedGameObject);
        var keep = Selected?.Lobby;
        Clear();
        foreach (var (host, where, status, avatar) in rows)
        {
            var entry = Object.Instantiate(_prefab, _list, false);
            entry.gameObject.SetActive(true);
            foreach (var localized in entry.GetComponentsInChildren<LocalizedText>(true)) localized.LocalizedLabel = null!;
            entry.TypeText.text = host.Name.Length > 0 ? host.Name : "Steam friend"; entry.LevelText.text = where; entry.DateText.text = status;
            if (entry.ObsoleteVignette != null) entry.ObsoleteVignette.SetActive(false);
            if (entry.DLCVignette != null) entry.DLCVignette.SetActive(false);
            if (avatar != null)
            {
                var sprite = Sprite.Create(avatar, new UnityEngine.Rect(0, 0, avatar.width, avatar.height), new Vector2(0.5f, 0.5f));
                _sprites.Add(sprite); entry.ThumbnailImage.sprite = sprite; entry.ThumbnailImage.preserveAspect = true;
            }
            else entry.ThumbnailImage.enabled = false; // Steam is still fetching it; the entry is rebuilt once it arrives.
            entry.onClick = new Button.ButtonClickedEvent();
            var selected = host;
            entry.onClick.AddListener((UnityEngine.Events.UnityAction)(System.Action)(() => Select(selected)));
            entry.SetDoubleClickAction((Il2CppSystem.Action)(System.Action)(() => { Select(selected); JoinSelected(); }));
            _entries.Add((entry, host));
        }
        _empty.text = unavailable.Length > 0 ? unavailable
            : "No friends are hosting PBDMP right now.\nInvite-only games arrive as Steam invites instead.";
        _empty.gameObject.SetActive(_entries.Count == 0);
        var again = _entries.FirstOrDefault(e => e.Host.Lobby == keep);
        Select(again.Button != null ? again.Host : _entries.Count > 0 ? _entries[0].Host : null);
        Navigate();
        if (focused && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject((_entries.FirstOrDefault(e => e.Host.Lobby == Selected?.Lobby).Button ?? (Selectable)RefreshButton).gameObject);
    }

    internal static string Where(LobbyAdvert? advert) => advert == null ? "Checking..." : advert.Mode switch
    {
        LobbyAdvert.ModeSurvival => "Survival co-op",
        LobbyAdvert.ModeStory when advert.Level.Length > 0 => MultiplayerBehaviour.MapName(advert.Level) + " (co-op)",
        _ => "In the Multiplayer menu"
    };

    internal static string Status(LobbyAdvert? advert) => advert == null ? "" :
        advert.Problem(Plugin.Version, Protocol.Version, Plugin.LobbyBuild) ?? $"{advert.Players} of {LobbyAdvert.Capacity} players";

    internal void Select(FriendHost? host)
    {
        Selected = host;
        foreach (var (button, listed) in _entries) { if (listed.Lobby == host?.Lobby) button.SetChecked(); else button.SetInteractable(); }
        NativeUiKit.SetButtonState(JoinButton, host?.Advert is { } advert && advert.Problem(Plugin.Version, Protocol.Version, Plugin.LobbyBuild) == null);
    }

    private void JoinSelected()
    {
        if (Selected is not { Advert: { } advert } host || advert.Problem(Plugin.Version, Protocol.Version, Plugin.LobbyBuild) != null) return;
        _join(host);
    }

    private void Clear()
    {
        _entries.Clear();
        for (var i = _list.childCount - 1; i >= 0; i--) Object.DestroyImmediate(_list.GetChild(i).gameObject);
        foreach (var sprite in _sprites) Object.Destroy(sprite);
        _sprites.Clear();
    }

    // The list navigates up/down into the bottom row: Back, Refresh, Join, with Direct IP... under Refresh.
    private void Navigate()
    {
        var items = _entries.Select(e => (Selectable)e.Button).ToList();
        for (var i = 0; i < items.Count; i++)
        {
            var navigation = items[i].navigation; navigation.mode = Navigation.Mode.Explicit;
            navigation.selectOnUp = i > 0 ? items[i - 1] : null; navigation.selectOnDown = i + 1 < items.Count ? items[i + 1] : JoinButton;
            items[i].navigation = navigation;
        }
        var row = new Selectable[] { BackButton, RefreshButton, JoinButton };
        for (var i = 0; i < row.Length; i++)
        {
            var navigation = row[i].navigation; navigation.mode = Navigation.Mode.Explicit;
            navigation.selectOnLeft = i > 0 ? row[i - 1] : null; navigation.selectOnRight = i + 1 < row.Length ? row[i + 1] : null;
            navigation.selectOnUp = items.LastOrDefault(); navigation.selectOnDown = DirectButton; row[i].navigation = navigation;
        }
        var direct = DirectButton.navigation; direct.mode = Navigation.Mode.Explicit;
        direct.selectOnUp = RefreshButton; direct.selectOnLeft = BackButton; direct.selectOnRight = JoinButton; DirectButton.navigation = direct;
        Menu._firstSelected = items.FirstOrDefault() ?? RefreshButton;
    }
}
