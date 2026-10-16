using System.Net;
using BepInEx;
using Il2CppInterop.Runtime.Attributes;
using Hyperstrange.PBD;
using Hyperstrange.PBD.UI.Menus;
using PBDMP.Network;
using PBDMP.Network.Steam;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using EventSystem = UnityEngine.EventSystems.EventSystem;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace PBDMP.Plugin;

public sealed partial class MultiplayerBehaviour : MonoBehaviour
{
    private CoopSession? _session;
    private SessionLifecycle? _lifecycle;
    private SharedWorld? _world;
    private CoopSaveService? _saves;
    private uint _remoteLife, _remoteEpoch;
    private GameObject? _ui;
    private GameObject? _panel;
    private GameObject? _menuButton;
    private NativeMultiplayerUI? _view;
    private SteamService? _steam;
    private bool _steamSession, _promptOpenedPanel, _wasConnected;
    private GameObject? _avatar;
    private TMP_Text? _avatarLabel;
    private RemotePlayerVisual? _remoteVisual;
    private readonly LocalWeaponObserver _weaponObserver = new();
    private TMP_Text? _status;
    private TMP_InputField? _address;
    private TMP_InputField? _port;
    private MainMenu? _mainMenu;
    private bool _open;
    private readonly OwnedInputMode _panelInput = new(InputManager.InputState.MENU);
    private bool _inputPushed => _panelInput.Active;
    private bool _hadRemote;
    private CursorLockMode _previousLock;
    private bool _previousVisible;
    private float _nextMenuScan;
    private string _feedback = "";
    private string _lastStatus = "";
    private CoopSession? _reportSession;
    private string _reportLevel = "";
    private bool _failed;
    private bool _buttonVisibleLogged;
    private readonly bool _smokeEnabled = Environment.GetEnvironmentVariable("PBDMP_SMOKE_TEST") == "1";
    private int _smokeStep;
    private float _smokeDeadline;
    private float _smokeNext;
    private CoopSession? _smokePeer;
    private PlayerPose? _smokeLocal;
    private int _smokePort;
    private Button? _hostButton;
    private Button? _joinButton;
    private Button? _disconnectButton;
    private string[] _smokeWeapons = Array.Empty<string>();
    private AsyncOperation? _smokeUnload;

    public MultiplayerBehaviour(IntPtr pointer) : base(pointer) { }

    public void Start()
    {
        try { _smokeDeadline = Time.unscaledTime + 90f; }
        catch (Exception ex) { Fail(ex); }
    }

    public void Update()
    {
        if (_failed) return;
        var baselineStart=PerformanceBaseline.BeginUpdate();
        try
        {
            DiagnosticCursor.Verify();
            SessionLifecycle.TickCleanup();
            if (_view == null)
            {
                if (Object.FindObjectOfType<MenuManager>() == null || MenuManager.MainCanvas == null) return;
                if (MenuManager.MainCanvas.transform.Find("MainCanvasGroup/PROMPT_Input") == null) return;
                BuildUI();
            }
            if (Input.GetKeyDown(KeyCode.F8)) TogglePanel();
            else if (_open && Input.GetKeyDown(KeyCode.Escape) && !_view!.Cancel()) SetPanel(false);
            _view!.Refresh();
            _steam!.Tick();
            if (Time.unscaledTime >= _nextMenuScan)
            {
                _nextMenuScan = Time.unscaledTime + 0.5f;
                _mainMenu = _view.MainMenu;
                var menuVisible = _mainMenu != null && _mainMenu.gameObject.activeInHierarchy &&
                    _mainMenu.CanvasGroup != null && _mainMenu.CanvasGroup.alpha > 0.5f;
                if (menuVisible && !_buttonVisibleLogged)
                {
                    _buttonVisibleLogged = true;
                    Plugin.Logger.LogInfo("Main menu detected; Multiplayer button visible.");
                }
            }
            try
            {
                var perf=SyncPerformance.Start();_lifecycle?.Tick();SyncPerformance.Finish("lifecycle",perf);
                _session?.Publish(_smokeLocal ?? ReadPlayer());
                perf=SyncPerformance.Start();_world?.Tick();SyncPerformance.Finish("world",perf);
                perf=SyncPerformance.Start();_saves?.Tick();SyncPerformance.Finish("persistence",perf);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError("Campaign session failed: " + ex);
                ResetSession(); _feedback = "Campaign session ended: " + ex.Message;
            }
            var local = _smokeLocal ?? ReadPlayer();
            _session?.Publish(local);
            var snapshot = _session?.Snapshot ?? new(SessionRole.Offline, false, "Offline", null);
            // A failed Steam join releases its lobby place at once, so the friend can simply try again.
            if (_steamSession && snapshot.Role == SessionRole.Client && !snapshot.Connected && snapshot.Status.StartsWith("Join failed", StringComparison.Ordinal))
            {
                ResetSession(); ShowInfo(snapshot.Status.Replace("Join failed: ", "Could not join: "));
                snapshot = new(SessionRole.Offline, false, "Offline", null);
            }
            if (snapshot.Connected && _view.Prompt.Busy) _view.Prompt.Hide();
            if (snapshot.Connected != _wasConnected)
            {
                _wasConnected = snapshot.Connected;
                if (snapshot.Connected) _view.Hud.Toast(snapshot.Role == SessionRole.Host ? PartnerName(snapshot) + " joined." : "Connected to " + PartnerName(snapshot) + ".");
            }
            UpdateAvatar(snapshot, local);
            RememberReportContext(snapshot);
            var detail = snapshot.Status;
            if (snapshot.Connected)
            {
                if (_lifecycle != null) detail = _lifecycle.Status;
                else if (!local.Ready || snapshot.RemotePose?.Ready != true) detail += "\nBoth players must load the same level.";
                else if (local.Level != snapshot.RemotePose.Level) detail += "\nLevel mismatch.";
                else detail += "\nSame level. Remote " + (snapshot.Role == SessionRole.Host ? "P2" : "P1");
            }
            else if (_steam.Phase == LobbyPhase.Creating) detail = "Creating a Steam lobby...";
            else if (_steam.Phase == LobbyPhase.Joining) detail = "Joining a friend's Steam lobby...";
            else if (_steam.Phase == LobbyPhase.Hosting) detail = $"Steam lobby open ({(_steam.InviteOnly ? "invite only" : "friends only")}). Invite a friend, or they can use Join Game in Steam.";
            else if (snapshot.Role == SessionRole.Offline && !_steam.Available) detail = _steam.Unavailable;
            if(!string.IsNullOrEmpty(_saves?.Status)) detail+="\n"+_saves.Status;
            if(snapshot.Connected && !string.IsNullOrEmpty(_world?.MissionStatus)) detail+="\n"+_world.MissionStatus;
            _status!.text = string.IsNullOrEmpty(_feedback) ? detail : _feedback + "\n" + detail;
            _view.Apply(Model(snapshot, local, _status.text));
            _view.Hud.Set(Badge(snapshot));
            var hold=_saves?.HoldCaption??"";
            if(hold.Length==0 && _lifecycle is {} holdLife && holdLife.State.Action!=TravelAction.Menu &&
                (!holdLife.NativeReady || snapshot.Connected && !holdLife.State.Released || holdLife.IsClient && (!holdLife.WorldReady || !holdLife.PersonalReady))) hold="Loading...";
            if(_view.Prompt.Open) hold="";
            var hostPause=hold.Length==0 && _lifecycle?.HostPauseNotice==true;
            _view.Hud.Hold(hostPause?"Host has paused":hold,hostPause);
            if (_lastStatus != snapshot.Status)
            {
                _lastStatus = snapshot.Status;
                Plugin.Logger.LogInfo(snapshot.Status);
            }
            if (_smokeEnabled) RunSmoke();
            if (Environment.GetEnvironmentVariable("PBDMP_NEWS_TEST") == "1") RunNewsTest();
            if (_steamProbe) RunSteamProbe();
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PBDMP_TRAVEL_TEST")))
            {
                if(PerformanceBaseline.Enabled) RunPerformanceBaseline();
                else if(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PBDMP_PATH_TEST"))) RunWorldPathTest();
                else if(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PBDMP_PRESENTATION_TEST"))) RunReportedPresentationTest();
                else if(Environment.GetEnvironmentVariable("PBDMP_ARENA_TEST")=="1") RunArenaTest();
                else if(Environment.GetEnvironmentVariable("PBDMP_PROP_TEST")=="1") RunPropTest();
                else if(Environment.GetEnvironmentVariable("PBDMP_MECHANISM_TEST")=="1") RunMechanismTest();
                else if(Environment.GetEnvironmentVariable("PBDMP_SCENERY_TEST")=="1") RunSceneryTest();
                else if(Environment.GetEnvironmentVariable("PBDMP_STORY_TEST")=="1") RunStoryTest();
                else if(Environment.GetEnvironmentVariable("PBDMP_SAVE_TEST")=="1") RunSaveTest();
                else if(Environment.GetEnvironmentVariable("PBDMP_WORLD_REGRESSION")=="1") RunWorldRegression();
                else if(Environment.GetEnvironmentVariable("PBDMP_MISSION_EXITS_TEST")=="1") RunMissionExitTest();
                else if(Environment.GetEnvironmentVariable("PBDMP_URINE_TEST")=="1") RunUrineTest();
                else if(Environment.GetEnvironmentVariable("PBDMP_ACTOR_PART_TEST")=="1") RunActorPartTest();
                else if(Environment.GetEnvironmentVariable("PBDMP_ACTOR_TEST")=="1") RunActorTest();
                else if(Environment.GetEnvironmentVariable("PBDMP_JOIN_INPUT_TEST")=="1") RunJoinInputTest();
                else if(Environment.GetEnvironmentVariable("PBDMP_CAMPAIGN_AUDIT")=="1") RunCampaignAudit();
                else if(Environment.GetEnvironmentVariable("PBDMP_DOOR_RELOAD_TEST")=="1") RunDoorReloadTest();
                else if(Environment.GetEnvironmentVariable("PBDMP_STATE_TEST")=="1") RunSharedStateTest();
                else if(Environment.GetEnvironmentVariable("PBDMP_DOOR_TEST")=="1") RunDoorTest();
                else if(Environment.GetEnvironmentVariable("PBDMP_SHARED_COMBAT_TEST")=="1") RunSharedCombatTest();
                else RunTravelTest();
            }
            if (Environment.GetEnvironmentVariable("PBDMP_COMBAT_PROBE") == "1") RunCombatProbe();
        }
        catch (Exception ex) { Fail(ex); }
        finally {PerformanceBaseline.EndUpdate(baselineStart);}
    }

    public void LateUpdate()
    {
        if (_open) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
    }

    [HideFromIl2Cpp]
    private PlayerPose ReadPlayer()
    {
        var player = Player.Instance;
        _weaponObserver.Bind(player);
        var level = LevelManager.CurrentLevel;
        if (!SessionManager.SessionActive || player == null || level == null || !player.gameObject.activeInHierarchy) return PlayerPose.Empty;
        var feet = player.FeetPosition;
        // Scene name/id/type guards normal campaign and mod levels without calling their loader.
        var key = _lifecycle?.LevelKey ?? $"{level.Type}:{level.Id}:{level.name}";
        var camera = player.WorldCamera;
        return new(key, _lifecycle?.NativeReady ?? true, feet.x, feet.y, feet.z, camera != null ? camera.transform.eulerAngles.y : player.Transform.eulerAngles.y,
            Mathf.Clamp(player.Height, 0.5f, 3f), player.CurrentWeapon?.WeaponId?.ID ?? "",
            camera != null ? Mathf.Clamp(Mathf.DeltaAngle(0f, camera.transform.eulerAngles.x), -90f, 90f) : 0f,
            _weaponObserver.ShotSequence, player.IsAlive, _lifecycle?.Life ?? 0, _lifecycle?.Epoch ?? 0, _world?.CaptureCarrier());
    }

    [HideFromIl2Cpp]
    private void UpdateAvatar(SessionSnapshot snapshot, PlayerPose local)
    {
        var remote = snapshot.RemotePose;
        var show = snapshot.Connected && local.Ready && remote?.Ready == true && remote.Alive && remote.Level == local.Level &&
            remote.Epoch == local.Epoch;
        if (!show)
        {
            if (_avatar != null) _avatar.SetActive(false);
            if (_avatarLabel != null) _avatarLabel.gameObject.SetActive(false);
            _hadRemote = false;
            _remoteVisual?.ResetInterpolation();
            return;
        }
        if (_avatar == null)
        {
            var color = snapshot.Role == SessionRole.Host ? new Color(1f, 0.45f, 0.08f) : new Color(0.15f, 0.6f, 1f);
            _remoteVisual = new RemotePlayerVisual(color);
            _avatar = _remoteVisual.Root;
            _avatarLabel = TextAt(_ui!.transform, snapshot.Role == SessionRole.Host ? "P2" : "P1",
                Vector2.zero, new Vector2(360, 36), 26);
            _avatarLabel.color = color; _avatarLabel.enableWordWrapping = false;
        }
        // Steam sessions name the partner; Direct IP sessions only know the role.
        var label = _steamSession && PartnerName(snapshot) is { Length: > 0 } partner ? partner : snapshot.Role == SessionRole.Host ? "P2" : "P1";
        if (_avatarLabel != null && _avatarLabel.text != label) _avatarLabel.text = label;
        _avatar.SetActive(true);
        if (_remoteLife != remote!.Life || _remoteEpoch != remote.Epoch)
        { _hadRemote = false; _remoteVisual!.ResetInterpolation(); _remoteLife = remote.Life; _remoteEpoch = remote.Epoch; }
        var target = _world?.ResolvePoseFeet(remote!) ?? new Vector3(remote!.X, remote.Y, remote.Z);
        var factor = 1f - Mathf.Exp(-20f * Time.unscaledDeltaTime);
        _avatar.transform.position = !_hadRemote || Vector3.Distance(_avatar.transform.position, target) > 8f
            ? target : Vector3.Lerp(_avatar.transform.position, target, factor);
        _avatar.transform.rotation = Quaternion.Euler(0f, remote.Yaw, 0f);
        _remoteVisual!.Update(remote, Time.unscaledDeltaTime);
        var camera = Player.Instance != null ? Player.Instance.WorldCamera : Camera.main;
        if (_avatarLabel != null)
        {
            var screen = camera != null ? camera.WorldToScreenPoint(target + new Vector3(0f, remote.Height + 0.25f, 0f)) : Vector3.zero;
            _avatarLabel.gameObject.SetActive(screen.z > 0f);
            var canvasRect = _ui!.GetComponent<RectTransform>().rect;
            _avatarLabel.rectTransform.anchoredPosition = new Vector2(screen.x * canvasRect.width / Screen.width, screen.y * canvasRect.height / Screen.height);
        }
        _hadRemote = true;
    }

    private void TogglePanel()
    {
        SetPanel(!_open);
    }

    private void SetPanel(bool open)
    {
        if (_open == open) return;
        if (open)
        {
            _previousLock = Cursor.lockState;
            _previousVisible = Cursor.visible;
            _panelInput.Set(true);
        }
        else
        {
            if (_panelInput.Release())
            { Cursor.lockState = _previousLock; Cursor.visible = _previousVisible; }
        }
        _open = open;
        _view!.SetModal(open);
        Plugin.Logger.LogInfo(open ? "Multiplayer panel opened" : "Multiplayer panel closed");
    }

    private void Host()
    {
        if (!TryPort(out var port)) return;
        ResetSession();
        try
        {
            _session = new(Plugin.Fingerprint);
            _session.StartHost(port);
            StartCampaign();
            _feedback = "Give P2 your LAN IP and this port.";
            _view?.ShowDirect(false);
        }
        catch (Exception ex) { ResetSession(); _feedback = "Cannot host: " + ex.Message; }
    }

    private void Join()
    {
        if (!TryPort(out var port)) return;
        if (!IPAddress.TryParse(_address!.text.Trim(), out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        { _feedback = "Enter a numeric IPv4 address, e.g. 192.168.1.20."; return; }
        ResetSession();
        try
        {
            _session = new(Plugin.Fingerprint);
            _session.StartJoin(address, port);
            StartCampaign();
            _feedback = "";
            _view?.ShowDirect(false);
        }
        catch (Exception ex) { ResetSession(); _feedback = "Cannot join: " + ex.Message; }
    }

    [HideFromIl2Cpp]
    private void ConfirmPartner(string text,Action yes,Action no)
    {
        var opened=!_open;SetPanel(true);
        _view!.Prompt.Ask(text,"Assign partner",()=>{if(opened) SetPanel(false);yes();},"Decline",()=>{if(opened) SetPanel(false);no();});
    }

    private void StartCampaign()
    {
        if (!_smokeEnabled) _lifecycle = new(_session!, Notify, () => { if (_open) SetPanel(false); }, ResetSession);
        if (_lifecycle != null)
        {
            _world = new(_session!, _lifecycle);
            var id=_steamSession && _steam!.LocalId!=0?"steam:"+_steam.LocalId:CoopSaveService.DirectIdentity();
            _saves=new(_session!,_lifecycle,_world,id,()=>_steamSession?_steam!.Partner:0,Notify,
                ConfirmPartner);
        }
    }

    private void HostSteam()
    {
        ResetSession();
        _steamSession = true;
        _steam!.Host();
    }

    // The lobby exists; the listener only admits its members.
    private void OnLobbyHosted()
    {
        try
        {
            var listener = new SteamP2PListener(id => id != _steam!.LocalId && _steam.IsMember(id), _steam!.Opened);
            _steam.Listener = listener;
            _session = new(Plugin.Fingerprint);
            _session.StartHost(listener, "Steam lobby open; waiting for a friend");
            StartCampaign();
            _feedback = "";
        }
        catch (Exception ex) { ResetSession(); ShowInfo("Cannot host: " + ex.Message); }
    }

    [HideFromIl2Cpp]
    private void OnJoinRequested(ulong lobby, ulong friend)
    {
        var name = _steam!.Name(friend);
        var host = name.Length > 0 ? name + "'s" : "your friend's";
        // Joining from the menus is what the player just asked Steam for; leaving a game in progress gets a question.
        if (!SessionManager.SessionActive && _session == null && _steam.Phase == LobbyPhase.None) { JoinSteam(lobby, host); return; }
        var leaving = SessionManager.SessionActive ? "Progress since your last save will be lost." : "You will leave the current multiplayer session.";
        OpenForPrompt();
        _view!.Prompt.Ask($"Join {host} game?\n{leaving}", "Join", () => JoinSteam(lobby, host), "Stay", ClosePromptPanel);
    }

    [HideFromIl2Cpp]
    private void JoinSteam(ulong lobby, string host)
    {
        ResetSession();
        _steamSession = true;
        _steam!.Join(lobby);
        if (_steam.Phase != LobbyPhase.Joining) return;
        if (!_open) SetPanel(true);
        _view!.Prompt.Wait($"Joining {host} game...", "Cancel", () => { ResetSession(); Notify("Join cancelled."); });
    }

    private void OnLobbyEntered(ulong host)
    {
        try
        {
            var name = _steam!.Name(host);
            _session = new(Plugin.Fingerprint);
            _session.StartJoin(stop => SteamP2P.ConnectAsync(host, stop, _steam.Opened), $"Connecting to {name} through Steam", SteamP2P.ConnectSeconds);
            StartCampaign();
            if (_view!.Prompt.Busy) _view.Prompt.Update($"Connecting to {name} through Steam...");
        }
        catch (Exception ex) { ResetSession(); ShowInfo("Cannot join: " + ex.Message); }
    }

    private void Notify(string message)
    {
        _feedback = message;
        _view?.Hud.Toast(message);
    }

    // Problems the player has to read now get the native prompt, opened over whatever is on screen.
    private void ShowInfo(string message)
    {
        Plugin.Logger.LogInfo("Multiplayer notice: " + message);
        _feedback = message;
        if (_view == null) return;
        OpenForPrompt();
        _view.Prompt.Ask(message, "OK", ClosePromptPanel);
    }

    private void OpenForPrompt()
    {
        if (_open) return;
        _promptOpenedPanel = true;
        SetPanel(true);
    }

    private void ClosePromptPanel()
    {
        if (!_promptOpenedPanel) return;
        _promptOpenedPanel = false;
        SetPanel(false);
    }

    [HideFromIl2Cpp]
    private string PartnerName(SessionSnapshot snapshot)
    {
        if (!_steamSession || _steam?.Available != true) return snapshot.Role == SessionRole.Host ? "P2" : "P1";
        var name = _steam.Name(_steam.Partner);
        return name.Length > 0 ? name : snapshot.Role == SessionRole.Host ? "P2" : "P1";
    }

    [HideFromIl2Cpp]
    private LobbyModel Model(SessionSnapshot snapshot, PlayerPose local, string status)
    {
        var steam = _steam!;
        var role = snapshot.Role;
        var inLobby = role != SessionRole.Offline || steam.Phase != LobbyPhase.None;
        // Smoke captures are shared as documentation, so they never show the tester's Steam identity.
        var identity = steam.Available && !_smokeEnabled;
        var self = new CardModel(identity ? steam.Name(steam.LocalId) : "You",
            !inLobby ? "Not in a session" : role == SessionRole.Client ? "P2  /  you" : "P1  /  host  /  you",
            Where(local), identity ? steam.Avatar(steam.LocalId) : null, role == SessionRole.Client ? "P2" : "P1", true);
        CardModel other;
        if (snapshot.Connected)
        {
            var partner = _steamSession ? steam.Partner : 0;
            var remote = snapshot.RemotePose;
            var state = remote == null ? "Connected" : !remote.Alive ? "Respawning" : Where(remote);
            if (_steamSession && steam.Ping >= 0) state += $"  /  {steam.Ping} ms";
            other = new(PartnerName(snapshot), role == SessionRole.Host ? "P2" : "P1  /  host", state,
                partner != 0 ? steam.Avatar(partner) : null, role == SessionRole.Host ? "P2" : "P1", true);
        }
        else if (role == SessionRole.Client || steam.Phase == LobbyPhase.Joining)
            other = new("Host", "P1  /  host", "Connecting...", null, "P1", false);
        else if (inLobby)
            other = new("Empty slot", "P2", steam.Phase == LobbyPhase.Hosting ? "Invite a friend to fill this slot" : "Waiting for P2 to connect", null, "P2", false);
        else
            other = new("Empty slot", "P2", "Host a game to invite a friend", null, "P2", false);
        var hostFirst = role != SessionRole.Client;
        return new(role, steam.Phase, steam.Available, _steamSession, steam.InviteOnly, snapshot.Connected,
            hostFirst ? self : other, hostFirst ? other : self, status);
    }

    [HideFromIl2Cpp]
    private static string Where(PlayerPose pose)
    {
        if (!pose.Ready || pose.Level.Length == 0) return "In the menus";
        // Level keys end in the scene name, such as LEVEL_TheWall_Interior.
        var scene = pose.Level[(pose.Level.LastIndexOf(':') + 1)..];
        if (scene.StartsWith("LEVEL_", StringComparison.Ordinal)) scene = scene[6..];
        return "Playing " + scene.Replace('_', ' ');
    }

    [HideFromIl2Cpp]
    private string Badge(SessionSnapshot snapshot)
    {
        if (snapshot.Role == SessionRole.Offline) return _steam!.Phase == LobbyPhase.None ? "" : "CO-OP  /  STEAM LOBBY  /  F8";
        var role = snapshot.Role == SessionRole.Host ? "P1 HOST" : "P2";
        if (!snapshot.Connected) return $"CO-OP  /  {role}  /  {(snapshot.Role == SessionRole.Host ? "WAITING FOR A FRIEND" : "CONNECTING")}  /  F8";
        var ping = _steamSession && _steam!.Ping >= 0 ? $"  /  {_steam.Ping} MS" : "";
        return $"CO-OP  /  {role}  /  WITH {PartnerName(snapshot).ToUpperInvariant()}{ping}  /  F8";
    }

    private bool TryPort(out int port)
    {
        if (!int.TryParse(_port!.text, out port) || port is < 1 or > 65535)
        { _feedback = "Port must be between 1 and 65535."; return false; }
        return true;
    }

    [HideFromIl2Cpp]
    private void RememberReportContext(SessionSnapshot snapshot)
    {
        var life = _lifecycle;
        var playing = life != null && life.NativeReady && life.State.Action != TravelAction.Menu;
        if (_session == null || snapshot.Role == SessionRole.Offline || (!snapshot.Connected && !playing)) return;
        if (!ReferenceEquals(_reportSession, _session))
        {
            _reportSession = _session; _reportLevel = "";
            IssueReport.LastSession.Begin(snapshot.Role == SessionRole.Host, _steamSession);
        }
        if (!playing || life!.State.Level == _reportLevel) return;
        var level = SessionLifecycle.FindLevel(life.State.Level);
        if (!SessionLifecycle.Supported(level)) return;
        IssueReport.LastSession.RememberLevel(level!.name);
        _reportLevel = life.State.Level;
    }

    private void ResetSession()
    {
        _saves?.Dispose();_saves=null;
        try { _world?.Dispose(); }
        catch (Exception ex) { Plugin.Logger.LogError("World cleanup: " + ex); }
        _world=null;
        try { _lifecycle?.Dispose(); }
        catch (Exception ex) { Plugin.Logger.LogError("Campaign cleanup: " + ex); }
        _lifecycle = null;
        _session?.Dispose(); _session = null;
        _reportSession = null; _reportLevel = ""; // Keep the sanitized last-session report until the game closes.
        // The session owned the listener; leaving releases the lobby place for the partner too.
        _steam?.Leave(); _steamSession = false;
        _remoteVisual?.Dispose(); _remoteVisual = null;
        if (_avatarLabel != null) Object.Destroy(_avatarLabel.gameObject);
        _avatar = null; _avatarLabel = null; _hadRemote = false; _feedback = ""; _wasConnected = false;
    }

    private void BuildUI()
    {
        _view = new NativeMultiplayerUI(MenuManager.MainCanvas, HostSteam, () => _steam!.Invite(), () => _steam!.TogglePrivacy(),
            Host, Join, ResetSession, TogglePanel);
        _steam = new SteamService(OnLobbyHosted, OnLobbyEntered, OnJoinRequested, ShowInfo, message => _view!.Hud.Toast(message));
        _ui = _view.Root; _panel = _view.Panel; _menuButton = _view.MainEntry.gameObject;
        _address = _view.Address; _port = _view.Port; _status = _view.Status;
        _hostButton = _view.HostButton; _joinButton = _view.JoinButton; _disconnectButton = _view.DisconnectButton;
        Plugin.Logger.LogInfo("PBDMP native UI constructed: main/pause entries, lobby panel with player cards, Direct IP dialog, prompt and session badge.");
    }

    [HideFromIl2Cpp]
    private TMP_Text TextAt(Transform parent, string value, Vector2 center, Vector2 size, int fontSize) =>
        _view!.TextAt(parent, "Remote player label", value, center, size, fontSize);

    [HideFromIl2Cpp]
    private void Fail(Exception ex)
    {
        _failed = true;
        Plugin.Logger.LogError("PBDMP stopped after runtime error: " + ex);
        if (_smokeEnabled) Plugin.Logger.LogError("PBDMP SMOKE FAIL");
        if (_open) { try { SetPanel(false); } catch { } }
        ResetSession();
        _steam?.Dispose(); _steam = null;
        _view?.Dispose(); _view = null; _ui = null;
    }

    public void OnApplicationQuit() { _weaponObserver.Dispose(); ResetSession(); SteamCallbackTap.Remove(); }
    public void OnDestroy()
    {
        if (_open) { try { SetPanel(false); } catch { } }
        ResetSession();
        _weaponObserver.Dispose();
        _smokePeer?.Dispose();
        _steam?.Dispose(); _steam = null;
        _view?.Dispose(); _view = null; _ui = null;
    }

    // Explicit diagnostic mode uses synthetic positions in the menu, never loading or saving a level.
    // It exercises real IL2CPP callbacks and native objects; it is not a two-PC gameplay test.
    private void RunSmoke()
    {
        if (_smokeStep == 99) return;
        if (Time.unscaledTime > _smokeDeadline) throw new TimeoutException($"Runtime smoke step {_smokeStep} timed out.");
        if (Time.unscaledTime < _smokeNext) return;
        switch (_smokeStep)
        {
            case 0:
                if (!_buttonVisibleLogged) return;
                if (!_view!.MainEntry.IsInteractable()) return;
                MeshMaterialRegression.Run();
                NativeUiDiagnostics.AssertControls(_view!);
                _view!.MainEntry.OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
                SmokeAssert(_open && _inputPushed, "menu button opens panel and blocks game input");
                NativeUiDiagnostics.AssertModal(_view, true);
                NativeUiDiagnostics.AssertViewport(_view);
                _smokeStep = 1; _smokeNext = Time.unscaledTime + 2f;
                break;
            case 1:
                NativeUiDiagnostics.Capture(SmokeOutput());
                CaptureUI("runtime-panel.png");
                // Offline: Steam host, privacy (when Steam runs), Direct IP, saves and Back are reachable.
                NativeUiDiagnostics.AssertLayerNavigation(_view!, new Selectable[] { _view!.SteamHostButton, _view.PrivacyButton, _view.DirectButton, _view.SavesButton, _view.BackButton }
                    .Where(s => s.interactable).ToArray());
                SmokeAssert(!_view.InviteButton.interactable && !_view.DisconnectButton.interactable, "invite and leave stay disabled outside a session");
                _view.SavesButton.onClick.Invoke();
                SmokeAssert(_view.Saves.Open,"Co-op saves opens as its own dialog");
                NativeUiDiagnostics.AssertLayerNavigation(_view,_view.Saves.Controls);
                CaptureUI("runtime-saves.png");
                SmokeAssert(_view.Cancel() && !_view.Saves.Open && _open,"Escape closes Co-op Saves before the multiplayer panel");
                _view.DirectButton.onClick.Invoke();
                SmokeAssert(_view.DirectOpen, "Direct IP opens as its own dialog");
                _smokeStep = 7; _smokeNext = Time.unscaledTime + 0.5f;
                break;
            case 7:
                CaptureUI("runtime-direct.png");
                NativeUiDiagnostics.AssertLayerNavigation(_view!, new Selectable[] { _address!, _port!, _hostButton!, _joinButton!, _view!.DirectBackButton });
                SmokeAssert(_view.Cancel() && !_view.DirectOpen && _open, "Escape closes the Direct IP dialog before the panel");
                _view.Prompt.Ask("Join a friend's game?\nProgress since your last save will be lost.", "Join", null, "Stay", null);
                _smokeStep = 8; _smokeNext = Time.unscaledTime + 0.5f;
                break;
            case 8:
                CaptureUI("runtime-prompt.png");
                NativeUiDiagnostics.AssertLayerNavigation(_view!, new Selectable[] { _view!.Prompt.ConfirmButton, _view.Prompt.CancelButton });
                SmokeAssert(_view.Cancel() && !_view.Prompt.Open && _open, "Escape answers an open prompt before closing the panel");
                _smokeStep = 9; _smokeNext = Time.unscaledTime + 0.2f;
                break;
            case 9:
                _address!.text = "invalid";
                _address.onSubmit.Invoke(_address.text);
                SmokeAssert(_feedback.Contains("IPv4"), "native field Enter submits join and validates IP");
                _port!.text = "0";
                _hostButton!.onClick.Invoke();
                SmokeAssert(_feedback.Contains("Port must"), "host validates port");
                var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
                probe.Start(); _smokePort = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
                _address.text = "127.0.0.1"; _port.text = _smokePort.ToString();
                _hostButton.onClick.Invoke();
                SmokeAssert(_session?.Snapshot.Role == SessionRole.Host, "host button starts listener");
                _smokeLocal = new("PBDMP_SMOKE_TEST", true, 0, 0, 0, 0, 1.8f);
                _smokePeer = new(Plugin.Fingerprint);
                _smokePeer.Publish(_smokeLocal with { X = 2f });
                _smokePeer.StartJoin(IPAddress.Loopback, _smokePort);
                _smokeStep = 2;
                break;
            case 2:
                if (_avatar == null || !_avatar.activeSelf) return;
                SmokeAssert(_session!.Snapshot.Connected && _avatar.GetComponentsInChildren<Renderer>(true).Length > 0,
                    "host receives P2 pose and creates model renderers");
                SmokeAssert(_remoteVisual!.HasDude, "gown Dude body and head loaded");
                SmokeAssert(_remoteVisual.Animation != null, "native Dude animation set available");
                _remoteVisual.Animation!.AssertPlayback();
                SmokeAssert(_avatar.GetComponentsInChildren<MonoBehaviour>(true).Length == 0 &&
                    _avatar.GetComponentsInChildren<Collider>(true).All(c => !c.enabled), "remote model has no gameplay scripts or active colliders");
                SmokeAssert(_avatarLabel!.text == "P2", "host labels remote P2");
                VisualDiagnostics.AssertRendered(_avatar, "remote-dude", SmokeOutput());
                var weapons = Resources.FindObjectsOfTypeAll<WeaponId>().Where(w => w != null && w._pickupPrefab != null).ToArray();
                var pistol = weapons.FirstOrDefault(w => w.name.Contains("Pistol", StringComparison.OrdinalIgnoreCase));
                var shotgun = weapons.FirstOrDefault(w => w.name == "WEAPON_Shotgun") ?? weapons.FirstOrDefault(w => w.name.Contains("Shotgun", StringComparison.OrdinalIgnoreCase));
                SmokeAssert(pistol != null && shotgun != null, "pistol and shotgun pickup assets available");
                _smokeWeapons = new[] { pistol!.ID, shotgun!.ID };
                _smokeUnload = Resources.UnloadUnusedAssets();
                _smokeStep = 19;
                break;
            case 19:
                if (_smokeUnload?.isDone != true) return;
                _remoteVisual!.Animation!.AssertPlayback();
                _smokePeer!.Publish(_smokeLocal! with { Weapon = _smokeWeapons[0] });
                _smokeStep = 20;
                break;
            case 20:
                if (_remoteVisual!.WeaponKey != _smokeWeapons[0] || !_remoteVisual.HasWeapon) return;
                if (_remoteVisual.Animation!.CurrentClip != "alterDude@idleTarget_machinegun") return;
                SmokeAssert(_avatar!.GetComponentsInChildren<MonoBehaviour>(true).Length == 0,
                    "equipped gun contains no gameplay scripts");
                VisualDiagnostics.AssertRendered(_avatar, "remote-dude-pistol", SmokeOutput());
                VisualDiagnostics.AssertRendered(_remoteVisual.GunObject!, "remote-pistol", SmokeOutput());
                _smokePeer!.Publish(_smokeLocal! with { Weapon = _smokeWeapons[0], ShotSequence = 1 });
                _smokeStep = 21;
                break;
            case 21:
                if (!_remoteVisual!.FlashVisible) return;
                SmokeAssert(true, "new remote shot sequence triggers visual muzzle flash");
                var effect = _remoteVisual.Muzzle!;
                effect.SimulateForTest(0.02f);
                SmokeAssert(effect.SourceName == "VFX_Muzzle_Flash_Pistol" && effect.ParticleCount > 0 &&
                    effect.Root.GetComponentsInChildren<MonoBehaviour>(true).Length == 0 &&
                    effect.Root.GetComponentsInChildren<Collider>(true).Length == 0, "original pistol particles play without scripts or physics");
                VisualDiagnostics.AssertRendered(effect.Root, "native-muzzle-pistol", SmokeOutput());
                effect.Stop(); effect.Fire(); effect.SimulateForTest(0.02f);
                SmokeAssert(effect.ParticleCount > 0, "native pistol particles retrigger on a second shot");
                _smokePeer!.Publish(_smokeLocal! with { Weapon = _smokeWeapons[1], Pitch = -35f, ShotSequence = 1 });
                _smokeStep = 22;
                break;
            case 22:
                if (_remoteVisual!.WeaponKey != _smokeWeapons[1] || !_remoteVisual.HasWeapon) return;
                SmokeAssert(Quaternion.Angle(_remoteVisual.GunRotation, Quaternion.Euler(-35f, 0f, 0f)) < 1f,
                    "weapon switch and aim pitch replicated");
                SmokeAssert(!_remoteVisual.FlashVisible, "weapon switch does not invent a firing event");
                VisualDiagnostics.AssertRendered(_avatar!, "remote-dude-shotgun", SmokeOutput());
                VisualDiagnostics.AssertRendered(_remoteVisual.GunObject!, "remote-shotgun", SmokeOutput());
                var shotgunEffect = _remoteVisual.Muzzle!;
                SmokeAssert(shotgunEffect.SourceName == "VFX_Muzzle_Flash_Shotgun", "shotgun uses its own original effect");
                shotgunEffect.Fire(); shotgunEffect.SimulateForTest(0.03f);
                SmokeAssert(shotgunEffect.ParticleCount > 0, "native shotgun particles play");
                VisualDiagnostics.AssertRendered(shotgunEffect.Root, "native-muzzle-shotgun", SmokeOutput());
                shotgunEffect.Stop();
                _smokePeer!.Publish(_smokeLocal! with { Weapon = "", ShotSequence = 1 });
                _smokeStep = 23;
                break;
            case 23:
                if (_remoteVisual!.HasWeapon) return;
                SmokeAssert(!_remoteVisual.FlashVisible, "unequip removes gun and muzzle flash");
                _smokePeer!.Publish(_smokeLocal! with { Level = "OTHER_LEVEL" });
                _smokeStep = 3;
                break;
            case 3:
                if (_avatar!.activeSelf) return;
                SmokeAssert(_status!.text.Contains("Level mismatch"), "level mismatch hides avatar and reports status");
                _disconnectButton!.onClick.Invoke();
                _smokePeer!.Dispose(); _smokePeer = null;
                SmokeAssert(_session == null && _avatar == null, "disconnect destroys remote avatar");
                _smokeNext = Time.unscaledTime + 0.5f; _smokeStep = 4;
                break;
            case 4:
                _smokePeer = new(Plugin.Fingerprint);
                _smokePeer.Publish(_smokeLocal! with { X = 2f });
                _smokePeer.StartHost(_smokePort, IPAddress.Loopback);
                _joinButton!.onClick.Invoke();
                _smokeStep = 5;
                break;
            case 5:
                if (_avatar == null || !_avatar.activeSelf) return;
                SmokeAssert(_session!.Snapshot.Role == SessionRole.Client && _avatarLabel!.text == "P1",
                    "join button connects and shows P1 model");
                _disconnectButton!.onClick.Invoke();
                _smokePeer!.Dispose(); _smokePeer = null; _smokeLocal = null;
                _view!.BackButton.onClick.Invoke();
                SmokeAssert(!_open && !_inputPushed, "close button restores game input");
                NativeUiDiagnostics.AssertModal(_view, false);
                NativeUiDiagnostics.SubmitController(_view.MainEntry);
                SmokeAssert(_open && _inputPushed, "native controller submit opens multiplayer dialog");
                InputManager.OnMenuCancelDown?.Invoke();
                SmokeAssert(!_open && !_inputPushed, "controller cancel restores the previous native menu");
                _view.PauseEntry.onClick.Invoke();
                SmokeAssert(_open && _inputPushed, "pause entry opens the same native dialog");
                InputManager.OnMenuCancelDown?.Invoke();
                SmokeAssert(!_open && !_inputPushed, "native menu cancel closes only the multiplayer dialog");
                NativeUiDiagnostics.AssertModal(_view, false);
                _view.Refresh(); NativeUiDiagnostics.AssertControls(_view);
                _smokeNext = Time.unscaledTime + 1f; _smokeStep = 6;
                break;
            case 6:
                CaptureUI("runtime-menu.png");
                Plugin.Logger.LogInfo("PBDMP SMOKE PASS: native themed lobby panel, Direct IP dialog and prompt layers, safe menu/pause entries, modal/controller callbacks, host/join, clip retention after asset cleanup, animated Dude and native gun effects, level guard and cleanup. Synthetic poses; live gameplay untested.");
                _smokeStep = 99;
                break;
        }
    }

    private static void SmokeAssert(bool result, string label)
    {
        if (!result) throw new InvalidOperationException("Smoke assertion failed: " + label);
        Plugin.Logger.LogInfo("SMOKE PASS: " + label);
    }

    private static string SmokeOutput() => Environment.GetEnvironmentVariable("PBDMP_SMOKE_OUTPUT") ??
        Path.GetFullPath(Path.Combine(Paths.GameRootPath, "..", "PBDMP", ".local"));

    // Hidden windows may skip normal presentation. Explicit rendering permits UI layout inspection.
    private void CaptureUI(string filename)
    {
        var canvas = _ui!.GetComponent<Canvas>();
        var oldCamera = canvas.worldCamera;
        var oldMode = canvas.renderMode;
        var oldTarget = RenderTexture.active;
        var cameraObject = new GameObject("PBDMP smoke capture camera");
        var width = Screen.width; var height = Screen.height;
        var render = new RenderTexture(width, height, 24);
        var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
        try
        {
            SetUILayer(_ui.transform, 31);
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.05f, 0.05f);
            camera.cullingMask = 1 << 31;
            camera.targetTexture = render;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = render;
            pixels.ReadPixels(new UnityEngine.Rect(0, 0, width, height), 0, 0);
            pixels.Apply();
            File.WriteAllBytes(Path.Combine(SmokeOutput(), filename), ImageConversion.EncodeToPNG(pixels));
            if (filename == "runtime-panel.png")
            {
                var purple = pixels.GetPixels32().Count(pixel => pixel.r > 40 && pixel.b > pixel.r + 10 && pixel.g < pixel.r);
                SmokeAssert(purple > width * height / 12, "native dialog backing produces visible pixels at the actual viewport resolution");
            }
        }
        finally
        {
            canvas.renderMode = oldMode; canvas.worldCamera = oldCamera;
            SetUILayer(_ui.transform, 0);
            RenderTexture.active = oldTarget;
            render.Release(); Object.Destroy(render); Object.Destroy(pixels); Object.Destroy(cameraObject);
        }
    }

    private static void SetUILayer(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        for (var i = 0; i < root.childCount; i++) SetUILayer(root.GetChild(i), layer);
    }
}
