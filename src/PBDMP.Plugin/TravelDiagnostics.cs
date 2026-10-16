using Hyperstrange.Base;
using Hyperstrange.PBD;
using Hyperstrange.PBD.PlayerComponents;
using PBDMP.Network;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PBDMP.Plugin;

public sealed partial class MultiplayerBehaviour
{
    private int _travelStep;
    private float _travelStarted, _travelAt;
    private uint _travelEpoch;
    private int[] _travelNpcs = Array.Empty<int>();
    private PlayerInventoryComponent.SaveData? _travelInventory;
    private bool _saveCallback;
    private string _nextMap = "";
    private IntPtr _originalTestDifficulty;
    private string TravelRole => Environment.GetEnvironmentVariable("PBDMP_TRAVEL_TEST")!;
    private string TravelOutput => Environment.GetEnvironmentVariable("PBDMP_TRAVEL_OUTPUT")!;
    private string Marker(string name) => Path.Combine(TravelOutput, name);
    private void TravelMark(string name) => File.WriteAllText(Marker(name), "pass");
    private bool Marked(string name) => File.Exists(Marker(name));
    private void TravelCheck(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("TRAVEL TEST: " + description);
        Plugin.Logger.LogInfo("TRAVEL PASS: " + description);
        File.AppendAllText(Marker(TravelRole + "-checks.txt"), description + "\n");
    }

    private void RunTravelTest()
    {
        if (_travelStep == 99) return;
        if (_travelStarted == 0) _travelStarted = Time.unscaledTime;
        File.WriteAllText(Marker(TravelRole + "-state.txt"), $"step={_travelStep}, ready={_lifecycle?.NativeReady}, epoch={_lifecycle?.Epoch}, status={_lifecycle?.Status}");
        if (Time.unscaledTime - _travelStarted > 210) throw new TimeoutException("Travel diagnostic timed out: " + _travelStep);
        if (_travelStep > 0 && _travelStep < (TravelRole == "host" ? 8 : 7) && _lifecycle == null)
            throw new InvalidOperationException("Travel session unexpectedly ended at step " + _travelStep);
        if (_lifecycle is { NativeReady: false }) _lifecycle.AutoContinue(); // diagnostic P1 also skips its native continue gates
        if (TravelRole == "host") TravelHost(); else TravelClient();
    }

    private LevelId TestLevel()
    {
        var requested=Environment.GetEnvironmentVariable("PBDMP_TEST_LEVEL");
        if(!string.IsNullOrEmpty(requested)) foreach(var level in LevelManager.AllLevels) if(level.name==requested && SessionLifecycle.Supported(level)) return level;
        if(!string.IsNullOrEmpty(requested)) throw new InvalidOperationException("Unknown/unsupported requested campaign test map: "+requested);
        foreach (var level in LevelManager.AllLevels) if (SessionLifecycle.Supported(level) && level.Id == 1) return level;
        foreach (var level in LevelManager.AllLevels) if (SessionLifecycle.Supported(level)) return level;
        throw new InvalidOperationException("No base campaign test map.");
    }

    private void QuietNpcs()
    {
        var catalogue = new WorldCatalogue(); catalogue.Build();
        var identity = Marker("catalogue-" + LevelManager.CurrentLevel.name + "-" + TravelRole);
        File.WriteAllText(identity, catalogue.Manifest);
        var peerIdentity = Marker("catalogue-" + LevelManager.CurrentLevel.name + "-" + (TravelRole == "host" ? "client" : "host"));
        if (File.Exists(peerIdentity)) TravelCheck(File.ReadAllText(peerIdentity) == catalogue.Manifest, "independent game instances have matching authored world catalogues");
        foreach (var key in catalogue.DoorKeys) WorldCatalogue.Save(catalogue.Components[key]);
        // Diagnostic only: preserve NPC instances but keep them from attacking stationary test players.
        foreach (var npc in Object.FindObjectsOfType<Npc>()) npc.gameObject.SetActive(false);
        _travelNpcs = Resources.FindObjectsOfTypeAll<Npc>().Where(n => n.gameObject.scene.IsValid()).Select(n => n.GetInstanceID()).OrderBy(x=>x).ToArray();
    }
    private void EndFrozenDiagnosticSession()
    {
        // Fixtures intentionally skip NPC state-entry initialization. Retire
        // those test actors before releasing the artificial AI freeze; allowing
        // an update first would dereference an uninitialized native state cache.
        if(SharedWorldPatches.DiagnosticFreeze)
            foreach(var npc in Object.FindObjectsOfType<Npc>()) npc.gameObject.SetActive(false);
        ResetSession();SharedWorldPatches.DiagnosticFreeze=false;
    }
    private void CheckWorldUnchanged() => TravelCheck(_travelNpcs.SequenceEqual(Resources.FindObjectsOfTypeAll<Npc>()
        .Where(n=>n.gameObject.scene.IsValid()).Select(n=>n.GetInstanceID()).OrderBy(x=>x)), "player respawn retains existing NPC instances");

    private void GiveTestGear()
    {
        var inventory = Player.Instance.GetComponent<PlayerInventoryComponent>();
        var weapon = Resources.FindObjectsOfTypeAll<WeaponId>().First(w=>w.name == "WEAPON_Shotgun");
        inventory.AddItem(weapon,1);
        foreach (var ammo in weapon.AmmoTypes) inventory.AddItem(ammo,37);
        var state = new PlayerInventoryComponent.SaveData(inventory);
        state.CurrentWeapon = weapon.ID; state.LastWeapon = weapon.ID; inventory.LoadState(state);
        _travelInventory = new(inventory);
    }
    private void CheckInventory()
    {
        var state = new PlayerInventoryComponent.SaveData(Player.Instance.GetComponent<PlayerInventoryComponent>());
        TravelCheck(state.CurrentWeapon == _travelInventory!.CurrentWeapon && state.HeldItems.Count == _travelInventory.HeldItems.Count &&
            _travelInventory.HeldItems.AllForTest(pair => state.HeldItems.ContainsKey(pair.Key) && state.HeldItems[pair.Key] == pair.Value),
            "respawn preserves weapons, ammo and item quantities");
    }
    private void KillForTest()
    {
        _travelInventory = new(Player.Instance.GetComponent<PlayerInventoryComponent>());
        Player.Instance.GetComponent<PlayerHitProcessorComponent>().SetCurrentHitPoints(13);
        Player.Instance.GetComponent<PlayerHitProcessorComponent>().SetArmor(50);
        Player.Instance.GetComponent<PlayerHitProcessorComponent>().HandleKill(new HitData
        { position = Player.Instance.CenterOfMass, direction = Vector3.forward, weakSpotMultiplier = 1 });
        _travelAt = Time.unscaledTime;
        TravelCheck(!Player.Instance.IsAlive && _lifecycle!.PlayerState.Pending, "native player death schedules individual respawn");
    }

    private void CheckRevival()
    {
        TravelCheck(Player.Instance.HitPoints.Current == Player.Instance.HitPoints.Max, "respawn restores full native health");
        var spawn = Object.FindObjectsOfType<PlayerSpawnPoint>().First(p=>p._player != null && p._player.Pointer == Player.Instance.Pointer);
        TravelCheck(Vector3.Distance(Player.Instance.Transform.position,spawn.transform.position) < 0.3f, "respawn returns to authored map spawn");
        TravelCheck(Player.Instance.Collider.enabled, "respawn restores player collider");
    }

    private void TravelHost()
    {
        switch (_travelStep)
        {
            case 0:
                if (!_buttonVisibleLogged) return;
                _port!.text = Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!; Host();
                GameManager.LoadLevel(TestLevel(), true); _travelStep = 1; break;
            case 1:
                if (!_lifecycle!.NativeReady) return;
                QuietNpcs();
                var noWeapon = Player.Instance.GetComponent<PlayerInventoryComponent>()._weaponsController._noWeapon;
                var initial = _lifecycle.PlayerState.CaptureJoin();
                _lifecycle.PlayerState.ApplyJoin(initial with { CurrentWeapon = noWeapon.ID, LastWeapon = noWeapon.ID });
                TravelCheck(Player.Instance.CurrentWeapon.WeaponId.ID == noWeapon.ID, "unarmed join resolves native no-weapon selection without requiring inventory ownership");
                GiveTestGear();
                if (PlayerSessionState.FindNearby(Player.Instance, Player.Instance.FeetPosition, out var moved))
                    Player.Instance.Teleport(moved, Vector3.zero, Vector3.zero);
                var before = SessionPatches.BlockedSaves; _saveCallback = false;
                Object.FindObjectOfType<SaveController>().Save(null,(Il2CppSystem.Action)(Action)(()=>_saveCallback = true));
                ProfileManager.SaveProfilesData(); LevelController.AutoSave(); LevelController.QuickSave();
                TravelCheck(ProfileManager.SaveProfilesDataAsync().IsCompleted, "blocked asynchronous profile save returns a completed task");
                TravelCheck(_saveCallback && SessionPatches.BlockedSaves >= before+5, "native save/autosave/quicksave/profile writers are blocked");
                GameManager.PauseGame();
                TravelCheck(_lifecycle.Frozen, "P1 native pause freezes its campaign");
                TravelMark("host-ready"); _travelStep = 2; break;
            case 2:
                if (!Marked("client-dead-paused")) return;
                TravelCheck(Player.Instance.IsAlive && _session!.Snapshot.Connected, "P2 death leaves P1 alive in current level");
                GameManager.DisableMenu(); _travelStep = 3; break;
            case 3:
                if (!Marked("client-revived")) return;
                KillForTest(); _travelStep = 4; break;
            case 4:
                if (!Player.Instance.IsAlive) return;
                TravelCheck(Time.unscaledTime - _travelAt >= 2.9f, "P1 independent respawn waits three seconds");
                CheckInventory(); CheckWorldUnchanged(); CheckRevival();
                TravelCheck(Player.Instance.GetComponent<PlayerHitProcessorComponent>().Armor == 0, "P1 respawn clears armor");
                _travelEpoch = _lifecycle!.Epoch; GameManager.Load(null); _travelStep = 5; break;
            case 5:
                if (!_lifecycle!.NativeReady || !_lifecycle.State.Released) return;
                if (!Marked("client-restarted")) return;
                TravelCheck(_lifecycle.Epoch > _travelEpoch, "same-map restart advances travel generation and waits for P2");
                var next = LevelManager.NextLevel;
                TravelCheck(SessionLifecycle.Supported(next), "next map is a supported built-in campaign map");
                _nextMap = next.ID; GameManager.LoadNextLevel(); GameManager.OpenLoading(); _travelStep = 9; break;
            case 9:
                if (!_lifecycle!.NativeReady || !_lifecycle.State.Released || !Marked("client-next-map")) return;
                TravelCheck(LevelManager.CurrentLevel.ID == _nextMap, "P1 and P2 follow native next-level travel into a different map");
                GameManager.OpenTitle(); _travelStep = 6; break;
            case 6:
                if (!Marked("client-followed-menu")) return;
                TravelCheck(!SessionManager.SessionActive, "P1 returns to title through native game flow");
                GameManager.LoadLevel(TestLevel(), true); _travelStep = 7; break;
            case 7:
                if (!_lifecycle!.NativeReady || !_lifecycle.State.Released || !Marked("client-reentered")) return;
                QuietNpcs(); ResetSession(); _travelStep = 8; break;
            case 8:
                if (!Marked("client-host-left")) return;
                TravelCheck(SessionLifecycle.Current == null, "P1 disconnect restores offline session guards");
                TravelMark("host-pass"); _travelStep = 99; break;
        }
    }

    private void TravelClient()
    {
        switch (_travelStep)
        {
            case 0:
                if (!_buttonVisibleLogged) return;
                _originalTestDifficulty = DifficultyManager.CurrentDifficulty.Pointer;
                _address!.text = "127.0.0.1"; _port!.text = Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!;
                Join(); _travelStep = 1; break;
            case 1:
                if (!_lifecycle!.NativeReady || !_lifecycle.State.Released) return;
                QuietNpcs();
                TravelCheck(Player.Instance.CurrentWeapon.WeaponId.name == "WEAPON_Shotgun", "late join copies P1 equipped shotgun");
                var host = _lifecycle.State.Loadout!;
                foreach (var item in host.Items) TravelCheck(Player.Instance.GetComponent<PlayerInventoryComponent>().GetItemQuantity(EntityManager.Get<EntityId>(item.Id)) == item.Quantity,
                    "late join imports validated host weapon/ammo quantity");
                TravelCheck(Vector3.Distance(Player.Instance.FeetPosition, new(host.X,host.Y,host.Z)) < 5, "late join places P2 near P1 on grounded clear geometry");
                TravelCheck(_lifecycle.Frozen && Time.timeScale == 0, "P1 pause also freezes P2");
                var epoch = _lifecycle.Epoch; GameManager.LoadLevel(TestLevel(),true); GameManager.Load(null);
                TravelCheck(_lifecycle.Epoch == epoch && _lifecycle.NativeReady, "P2 cannot start or restart a level independently");
                KillForTest(); _travelStep = 2; break;
            case 2:
                if (Time.unscaledTime - _travelAt < 4) return;
                TravelCheck(!Player.Instance.IsAlive, "P1 pause stops P2 respawn timer");
                TravelMark("client-dead-paused"); _travelStep = 3; break;
            case 3:
                if (!Player.Instance.IsAlive || _lifecycle!.State.Paused) return;
                CheckInventory(); CheckWorldUnchanged(); CheckRevival();
                TravelCheck(!PlayerSessionState.FindNearby(Player.Instance,new(100000,100000,100000),out _), "blocked/unsupported nearby placement produces normal-spawn fallback");
                TravelCheck(Player.Instance.GetComponent<PlayerHitProcessorComponent>().Armor == 0, "P2 respawn clears armor");
                GameManager.PauseGame();
                TravelCheck(!_lifecycle.Frozen && Time.timeScale > 0, "P2 local pause menu does not pause the shared world");
                GameManager.DisableMenu(); _travelEpoch = _lifecycle.Epoch;
                TravelCheck(InputManager.Instance.CurrentInputState == InputManager.InputState.GAMEPLAY && InputManager.Instance._gameplayKeyboardMap.enabled && InputManager.Instance._gameplayMouseMap.enabled,
                    "P2 gameplay keyboard/mouse input restored after loading/pause/respawn");
                TravelMark("client-revived"); _travelStep = 4; break;
            case 4:
                if (_lifecycle!.Epoch == _travelEpoch || !_lifecycle.NativeReady || !_lifecycle.State.Released) return;
                TravelCheck(_lifecycle.State.Action == TravelAction.Restart, "P2 follows same-map restart through native reset flow");
                TravelMark("client-restarted"); _travelStep = 5; break;
            case 5:
                if (_lifecycle!.NativeReady && _lifecycle.State.Action != TravelAction.Menu && LevelManager.CurrentLevel.ID != TestLevel().ID)
                    TravelMark("client-next-map");
                if (_lifecycle!.State.Action != TravelAction.Menu || SessionManager.SessionActive) return;
                TravelMark("client-followed-menu"); _travelStep = 6; break;
            case 6:
                if (_lifecycle!.State.Action == TravelAction.Menu || !_lifecycle.NativeReady || !_lifecycle.State.Released) return;
                QuietNpcs(); TravelMark("client-reentered"); _travelStep = 7; break;
            case 7:
                if (_lifecycle != null || SessionLifecycle.Current != null) return;
                TravelCheck(!SessionManager.SessionActive && SessionLifecycle.Current == null, "P1 disconnect returns P2 to title and restores offline guards");
                TravelCheck(DifficultyManager.CurrentDifficulty.Pointer == _originalTestDifficulty, "P2 original difficulty restored after disconnect");
                TravelMark("client-host-left"); TravelMark("client-pass"); _travelStep = 99; break;
        }
    }
}

internal static class TravelDictionaryCheck
{
    internal static bool AllForTest(this Il2CppSystem.Collections.Generic.Dictionary<string,int> entries,
        Func<Il2CppSystem.Collections.Generic.KeyValuePair<string,int>,bool> check)
    { foreach (var pair in entries) if (!check(pair)) return false; return true; }
}
