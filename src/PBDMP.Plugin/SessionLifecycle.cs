using Hyperstrange.Base;
using Hyperstrange.PBD;
using Hyperstrange.PBD.UI.Menus;
using Il2CppInterop.Runtime;
using PBDMP.Network;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PBDMP.Plugin;

internal sealed class SessionLifecycle : IDisposable
{
    internal static SessionLifecycle? Current;
    private readonly CoopSession _session;
    private readonly SessionRole _role;
    private readonly Action<string> _notice;
    private readonly Action _closePanel;
    private readonly Action _disconnect;
    private readonly HostTravel _host = new();
    private readonly PlayerSessionState _player = new();
    private readonly SaveData? _originalSave;
    private readonly DifficultyData _originalDifficulty;
    private readonly Dictionary<LevelId, string> _originalLevels = new();
    private readonly Il2CppSystem.Action _readyHandler;
    private readonly OwnedInputMode _barrierInput = new(InputManager.InputState.NONE);
    private readonly OwnedInputMode _menuInput = new(InputManager.InputState.MENU);
    private readonly Dictionary<SaveController, bool> _saving = new();
    private readonly Dictionary<UnityEngine.UI.Button, bool> _buttons = new();
    private bool _connected, _everConnected, _nativeReady, _gateArmed, _joiningApplied, _disposed;
    private bool _cleanupPending;
    private bool _hostPaused, _timeBypass;
    private bool _loadingContinued;
    private float _deadline;
    private float _guardScanAt;
    private uint _clientEpoch;
    private TravelState? _clientState;
    private int _bypass;
    internal bool Bypass => _bypass > 0;
    internal SessionRole Role => _role;
    internal bool IsClient => Role == SessionRole.Client;
    internal bool NativeReady => _nativeReady;
    internal bool WorldReady { get; set; } = true;
    internal uint Epoch => IsClient ? _clientEpoch : _host.State.Epoch;
    internal uint Life => _player.Life;
    internal uint MapGeneration { get; private set; }
    internal string LevelKey { get; private set; } = "";
    internal TravelState State => IsClient ? _clientState ?? _host.State : _host.State;
    internal PlayerSessionState PlayerState => _player;
    internal bool Frozen => _nativeReady && (IsClient ? _clientState is { } state && (!state.Released || state.Paused) :
        _hostPaused || (_session.Snapshot.Connected && !_host.State.Released));
    internal string Status { get; private set; } = "Campaign travel active; shared combat/doors; saves disabled.";

    internal SessionLifecycle(CoopSession session, Action<string> notice, Action closePanel, Action disconnect)
    {
        _session = session; _role = session.Snapshot.Role; _notice = notice; _closePanel = closePanel; _disconnect = disconnect;
        if (Current != null) throw new InvalidOperationException("Wait for the previous campaign to finish unloading.");
        if (SessionManager.SessionActive && LevelManager.CurrentLevel != null && !Supported(LevelManager.CurrentLevel))
            throw new InvalidOperationException("Only built-in base campaign maps are supported.");
        _originalSave = ProfileManager.CurrentSaveData;
        _originalDifficulty = DifficultyManager.CurrentDifficulty;
        foreach (var level in LevelManager.AllLevels) _originalLevels[level] = level.Save();
        _readyHandler = (Il2CppSystem.Action)(Action)OnReady;
        // Menu smoke remains a separate diagnostic; it never traverses gameplay.
        if (SessionManager.SessionActive && Player.Instance != null && LevelManager.CurrentLevel != null && Player.Instance.gameObject.activeInHierarchy)
        {
            if (!Supported(LevelManager.CurrentLevel)) throw new InvalidOperationException("Only built-in base campaign maps are supported.");
            var active = LevelController._instance.LevelId;
            _nativeReady = true; MapGeneration++; _player.CaptureStart(); LevelKey = $"{active.Type}:{active.Id}:{active.name}";
            if (!IsClient) { _host.Begin(TravelAction.Load, active.ID, Difficulty()); _host.SetReady(); _host.PeerLeft(); }
        }
        // Publish ownership only after validation/capture succeeds, so a failed constructor cannot
        // strand global guards or a native event subscription without an owner to dispose them.
        LevelController.OnStartLevelReady = Il2CppSystem.Delegate.Combine(LevelController.OnStartLevelReady, _readyHandler).Cast<Il2CppSystem.Action>();
        Current = this;
        ProfileManager.SetCurrentSaveData(null);
    }

    internal static bool Supported(LevelId? level) => level != null && !level.IsMod && !level.name.StartsWith("LEVEL_CustomMaps_",StringComparison.Ordinal) && level.Type == LevelId.TypeEnum.CAMPAIGN &&
        level.DlcCategory == DLCCategory.NONE && FindLevel(level.ID) != null;

    internal static LevelId? FindLevel(string id)
    {
        foreach (var level in LevelManager.AllLevels) if (level != null && level.ID == id) return level;
        return null;
    }

    internal static int Difficulty()
    {
        var current = DifficultyManager.CurrentDifficulty;
        for (var i = 0; i < DifficultyManager.Difficulties.Count; i++)
            if (DifficultyManager.Difficulties[i].Pointer == current.Pointer) return i;
        throw new InvalidOperationException("Current difficulty is not registered.");
    }

    internal bool PermitTravel()
    {
        if (!IsClient || Bypass) return true;
        _notice("P1 controls level starts, restarts and travel."); return false;
    }
    internal void Explain(string message) => _notice(message);

    internal void BeginHost(TravelAction action, LevelId? level)
    {
        if (Bypass || IsClient) return;
        _closePanel(); _menuInput.Release(); _barrierInput.Release(); _player.Cancel();
        _nativeReady = action == TravelAction.Menu; _gateArmed = false; _loadingContinued = false; _hostPaused = false;
        _host.Begin(action, level?.ID ?? "", Difficulty()); _deadline = Time.unscaledTime + 120;
        _session.PublishTravel(_host.State);
        Status = action == TravelAction.Menu ? "Returning to main menu." : "P1 loading campaign; waiting for both players.";
    }

    internal void OnReady()
    {
        if (_disposed || _nativeReady || LevelManager.CurrentLevel == null) return;
        var activeLevel = LevelController._instance?.LevelId;
        if (activeLevel == null || activeLevel.ID != State.Level) return;
        _nativeReady = true; MapGeneration++; _gateArmed = false; _player.CaptureStart();
        LevelKey = $"{activeLevel.Type}:{activeLevel.Id}:{activeLevel.name}";
        if (!IsClient)
        {
            _host.SetReady(_host.State.Joining ? _player.CaptureJoin() : null);
            if (!_session.Snapshot.Connected) _host.PeerLeft();
        }
        Plugin.Logger.LogInfo($"Campaign initialized: {LevelManager.CurrentLevel.name}, epoch {Epoch}, role {Role}.");
        ApplyTime();
    }

    internal void ArmGate() { if (!_nativeReady) _gateArmed = true; }
    internal void Death() { if (_nativeReady) _player.Died(); }

    internal void Tick()
    {
        if (_disposed) return;
        var snapshot = _session.Snapshot;
        // Discovery scans are expensive in large maps. Native entrypoint guards
        // protect the whole session; cached UI/save objects are enforced each frame.
        if(Time.unscaledTime>=_guardScanAt)
        {
            _guardScanAt=Time.unscaledTime+0.5f;
            if (MenuManager.MainCanvas != null)
                foreach (var button in MenuManager.MainCanvas.GetComponentsInChildren<UnityEngine.UI.Button>(true))
                {
                    var name = button.gameObject.name;
                    var blocked = name is "BUTTON_Load" or "BUTTON_Save" or "BUTTON_Horde" or "BUTTON_Mods" or "BUTTON_LoadMods" or "BUTTON_SaveMods" ||
                        IsClient && name is "BUTTON_Start" or "BUTTON_Restart" or "BUTTON_NextLevel" or "BUTTON_LastLevel";
                    if (!blocked) continue;
                    if (!_buttons.ContainsKey(button)) _buttons[button] = button.interactable;
                }
            foreach (var saving in Object.FindObjectsOfType<SaveController>())
            {
                if (!_saving.ContainsKey(saving)) _saving[saving] = saving.DisabledSaving;
            }
        }
        foreach(var button in _buttons.Keys) if(button!=null) button.interactable=false;
        foreach(var saving in _saving.Keys) if(saving!=null) saving.DisableSaving(true);
        if (snapshot.Connected && !_connected)
        {
            _everConnected = true; _deadline = Time.unscaledTime + 120;
            if (!IsClient)
            {
                var level = _host.State.Action == TravelAction.Menu ? null : FindLevel(_host.State.Level);
                _host.Begin(level == null ? TravelAction.Menu : TravelAction.Load, level?.ID ?? "", Difficulty(), level != null);
                if (level == null) _host.SetReady();
                else if (_nativeReady && Player.Instance.IsAlive) _host.SetReady(_player.CaptureJoin());
            }
        }
        if (_connected && !snapshot.Connected)
        {
            if (IsClient) { StopClient(snapshot.Status + ". Returned to main menu."); return; }
            _host.PeerLeft(); _notice("P2 left; P1 can continue.");
        }
        _connected = snapshot.Connected;
        if (IsClient)
        {
            if (snapshot.Travel is { } state)
            {
                if (state.Epoch != _clientEpoch)
                {
                    if (state.Epoch < _clientEpoch) throw new InvalidDataException("Host travel generation went backwards.");
                    _clientState = state; _clientEpoch = state.Epoch; BeginClient(state);
                }
                else _clientState = state;
                AutoContinue();
                if (_nativeReady && state.Joining && !_joiningApplied && state.Loadout != null)
                {
                    var nearby = _player.ApplyJoin(state.Loadout); _joiningApplied = true;
                    if (!nearby) _notice("No clear space beside P1; used normal map spawn.");
                }
                var ready = _nativeReady && WorldReady && (!state.Joining || _joiningApplied);
                _session.PublishReady(new(_clientEpoch, ready));
                Status = state.Action == TravelAction.Menu ? "Connected. Waiting for P1 to start campaign." : !ready || !state.Released ? "Loading/waiting for both players." : state.Paused ? "P1 paused the session." :
                    "Campaign connected. Enemies, combat and doors shared; saves disabled.";
                if (!state.Released && Time.unscaledTime > _deadline)
                { StopClient("Campaign loading timed out. Returned to menu; P1 can continue."); return; }
            }
            else if (snapshot.Connected && Time.unscaledTime > _deadline)
            { StopClient("Host did not provide campaign state."); return; }
        }
        else
        {
            // A late join while P1 is dead waits for P1's player-only respawn before capturing a spawn/loadout.
            if (_nativeReady && _host.State.Joining && _host.State.Loadout == null && Player.Instance != null && Player.Instance.IsAlive)
                _host.SetReady(_player.CaptureJoin());
            _host.SetPaused(_hostPaused); _host.Acknowledge(snapshot.Ready);
            _session.PublishTravel(_host.State);
            Status = _host.State.Action == TravelAction.Menu ? "Hosting. P1 can start a campaign level." : snapshot.Connected && !_host.State.Released ? "Waiting for P2 to load." : _hostPaused ? "P1 paused the session." :
                "Campaign host. Enemies, combat and doors shared; saves disabled.";
            if (snapshot.Connected && !_host.State.Released && Time.unscaledTime > _deadline)
            { _session.DropPeer("Campaign loading timed out."); _host.PeerLeft(); _notice("P2 load timed out; P1 can continue."); }
        }
        _player.Tick(Time.unscaledDeltaTime, IsClient ? _clientState?.Paused == true : _hostPaused);
        ApplyTime();
    }

    private void BeginClient(TravelState state)
    {
        _closePanel(); _menuInput.Release(); _barrierInput.Release(); _player.Cancel();
        _nativeReady = false; _gateArmed = false; _loadingContinued = false; _joiningApplied = false;
        _deadline = Time.unscaledTime + 120;
        _session.PublishReady(new(state.Epoch, false));
        Authorized(() =>
        {
            ProfileManager.SetCurrentSaveData(null);
            if (state.Difficulty >= DifficultyManager.Difficulties.Count) throw new InvalidDataException("Unavailable host difficulty.");
            SettingsManager.GameplaySettings.SetDifficulty(DifficultyManager.Difficulties[state.Difficulty]);
            if (state.Action == TravelAction.Menu) { GameManager.OpenTitle(); _nativeReady = true; _joiningApplied = true; return; }
            var level = FindLevel(state.Level);
            if (!Supported(level)) throw new InvalidDataException("Host requested an unsupported campaign map.");
            if (state.Action == TravelAction.Restart && LevelManager.CurrentLevel?.ID == state.Level && Player.Instance != null)
                GameManager.Load(null);
            else GameManager.LoadLevel(level!, true);
        });
        Plugin.Logger.LogInfo($"P2 follows {state.Action}, epoch {state.Epoch}.");
    }

    internal void AutoContinue()
    {
        if (_nativeReady) return;
        var loading = Object.FindObjectOfType<LoadingMenu>();
        if (!_loadingContinued && loading != null && loading.gameObject.activeInHierarchy && loading._sceneLoaded)
        { _loadingContinued = true; Authorized(loading.OnAnyButtonDown); }
        if (_gateArmed && LevelController._instance != null)
        {
            _gateArmed = false; Authorized(LevelController._instance.OnAnyButtonDown);
        }
    }

    internal void NativeTime(ref float scale)
    {
        if (_timeBypass || _disposed) return;
        if (!IsClient && _nativeReady) _hostPaused = scale == 0;
        if (Frozen) scale = 0;
    }

    private void ApplyTime()
    {
        if (!_nativeReady || State.Action == TravelAction.Menu) { _barrierInput.Release(); return; }
        // P1's native pause menu already owns MENU. Adding NONE above it makes
        // native resume pop our lock instead of its menu, leaving controls blocked.
        var nativeHostMenu=!IsClient && _hostPaused && InputManager.InputStateStack.Contains(InputManager.InputState.MENU);
        _barrierInput.Set(Frozen && !nativeHostMenu);
        _timeBypass = true;
        try { TimeManager.SetApplicationTimeScale(Frozen ? 0 : 1); }
        finally { _timeBypass = false; }
    }

    internal bool ClientPause()
    {
        if (!IsClient) return false;
        _menuInput.Set(true); MenuManager.OpenPauseMenu(); return true;
    }
    internal bool ClientUnpause()
    {
        if (!IsClient || Bypass) return false;
        _closePanel(); _menuInput.Release(); MenuManager.CloseAll(); ApplyTime(); return true;
    }

    private void StopClient(string reason)
    {
        _closePanel(); _disconnect(); _notice(reason);
    }

    internal void Authorized(Action action)
    { _bypass++; try { action(); } finally { _bypass--; } }

    public void Dispose()
    {
        if (_disposed) return;
        // A guest never continues this campaign as an offline session.
        if (IsClient && _everConnected && SessionManager.SessionActive) { _closePanel(); Authorized(GameManager.OpenTitle); }
        if (!IsClient) _player.FinishRespawn();
        _disposed = true; _player.Cancel(); _barrierInput.Release(); _menuInput.Release();
        LevelController.OnStartLevelReady = Il2CppSystem.Delegate.Remove(LevelController.OnStartLevelReady, _readyHandler).Cast<Il2CppSystem.Action>();
        _cleanupPending = IsClient && _everConnected;
        if (!_cleanupPending) RestoreOffline();
    }

    internal static void TickCleanup()
    {
        if (Current is not { _cleanupPending: true } pending || SessionManager.SessionActive) return;
        var player = Player.Instance;
        if (player != null && player.gameObject.activeInHierarchy) return;
        var title = Hyperstrange.PBD.SceneManager.Instance._titleCluster;
        foreach (var scene in title.Scenes)
            if (!UnityEngine.SceneManagement.SceneManager.GetSceneByName(scene.SceneName).isLoaded) return;
        pending._cleanupPending = false; pending.RestoreOffline();
    }

    private void RestoreOffline()
    {
        foreach (var pair in _saving) if (pair.Key != null) pair.Key.DisableSaving(pair.Value);
        foreach (var pair in _buttons) if (pair.Key != null) pair.Key.interactable = pair.Value;
        foreach (var pair in _originalLevels) pair.Key.Load(pair.Value);
        Authorized(() => SettingsManager.GameplaySettings.SetDifficulty(_originalDifficulty));
        ProfileManager.SetCurrentSaveData(_originalSave);
        if (Current == this) Current = null;
        // Preserve a native P1 pause if its menu remains open; clients are now in the menu.
        TimeManager.SetApplicationTimeScale(!SessionManager.SessionActive || _hostPaused ? 0 : 1);
    }
}

internal sealed class OwnedInputMode
{
    private static readonly List<OwnedInputMode> Owners = new();
    private readonly InputManager.InputState _mode;
    private Il2CppSystem.Collections.Generic.List<InputManager.InputState>? _stack;
    private int _index;
    internal bool Active => _stack != null;
    internal OwnedInputMode(InputManager.InputState mode) => _mode = mode;
    internal void Set(bool active)
    {
        if (!active) { Release(); return; }
        var current=InputManager.InputStateStack;
        if (_stack != null && current.Pointer==_stack.Pointer && current.Count>_index && current[_index]==_mode) return;
        Owners.Remove(this);
        _stack = InputManager.InputStateStack; _index = _stack.Count; InputManager.AddInputMode(_mode);
        Owners.Add(this);
    }
    internal bool Release()
    {
        var current = InputManager.InputStateStack;
        var wasTop=false;
        if (_stack != null && current.Pointer == _stack.Pointer && current.Count > _index && current[_index] == _mode)
        {
            wasTop=current.Count==_index+1;
            current.RemoveAt(_index);
            Removed(current.Pointer,_index);
            // UpdateControllerMaps only recaches bindings. The native setter also
            // enables the remaining state's maps and restores mouse capture.
            InputManager.InputStateStack=current;
        }
        Owners.Remove(this);_stack = null;
        return wasTop;
    }
    // MENU and NONE can be removed in either order. Keep each owner's exact
    // position rather than searching by enum and accidentally removing a native menu.
    internal static void Removed(IntPtr stack,int index)
    {
        foreach(var owner in Owners.ToArray())
        {
            if(owner._stack?.Pointer!=stack) continue;
            if(owner._index==index) {owner._stack=null;Owners.Remove(owner);}
            else if(owner._index>index) owner._index--;
        }
    }
}
