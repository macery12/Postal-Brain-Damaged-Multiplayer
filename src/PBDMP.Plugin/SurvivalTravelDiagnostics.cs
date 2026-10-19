using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using Hyperstrange.PBD.UI.Menus;
using PBDMP.Network;
using UnityEngine;
using Object = UnityEngine.Object;
using HordeState = Hyperstrange.PBD.HordeModeController.HordeModeStateEnum;

namespace PBDMP.Plugin;

// Phase 1 Survival entry/travel fixture (Test-Travel.ps1 -Survival). P1 hosts from inside an
// arena, retries, crosses to campaign and back from the lobby, then leaves while in the arena.
// The run itself is never started; P2's start paths must remain inert.
public sealed partial class MultiplayerBehaviour
{
    private bool? _survivalHordeButton;
    private float _survivalAt;

    private static LevelId SurvivalArena()
    {
        var requested = Environment.GetEnvironmentVariable("PBDMP_TEST_LEVEL");
        foreach (var level in LevelManager.AllLevels)
            if (SessionLifecycle.Survival(level) && (string.IsNullOrEmpty(requested) || level.name == requested)) return level;
        throw new InvalidOperationException("No supported Survival arena for the travel fixture: " + requested);
    }

    private static LevelId SurvivalCampaignTarget()
    {
        foreach (var level in LevelManager.AllLevels) if (SessionLifecycle.Campaign(level) && level.Id == 1) return level;
        throw new InvalidOperationException("No base campaign map for the Survival transition check.");
    }

    private static bool? HordeButton()
    {
        if (MenuManager.MainCanvas == null) return null;
        foreach (var button in MenuManager.MainCanvas.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            if (button.gameObject.name == "BUTTON_Horde") return button.interactable;
        return null;
    }

    private bool InArena(LevelId arena) => _lifecycle is { NativeReady: true } life && life.State.Released && life.State.Level == arena.ID &&
        LevelManager.CurrentLevel?.ID == arena.ID && Object.FindObjectOfType<HordeModeController>() != null;

    private void RunSurvivalTravelTest()
    {
        if (_travelStep == 99) return;
        if (_travelStarted == 0) _travelStarted = Time.unscaledTime;
        File.WriteAllText(Marker(TravelRole + "-state.txt"), $"step={_travelStep}, ready={_lifecycle?.NativeReady}, epoch={_lifecycle?.Epoch}, survival={_lifecycle?.InSurvival}, status={_lifecycle?.Status}");
        if (Time.unscaledTime - _travelStarted > 280) throw new TimeoutException("Survival travel diagnostic timed out: " + _travelStep);
        if (_lifecycle is { NativeReady: false }) _lifecycle.AutoContinue();
        if (TravelRole == "host") SurvivalHost(); else SurvivalClient();
    }

    private void SurvivalHost()
    {
        var arena = SurvivalArena();
        switch (_travelStep)
        {
            case 0: // offline solo arena, as a player who opens Survival before hosting
                if (!_buttonVisibleLogged) return;
                _survivalHordeButton = HordeButton();
                TravelCheck(_survivalHordeButton != null, "native Survival button found on the main menu");
                GameManager.LoadLevel(arena, true); _travelStep = 1; break;
            case 1:
                var loading = Object.FindObjectOfType<LoadingMenu>();
                if (loading != null && loading.gameObject.activeInHierarchy && loading._sceneLoaded) loading.OnAnyButtonDown();
                if (DoorSoloGate && LevelController._instance != null) { DoorSoloGate = false; LevelController._instance.OnAnyButtonDown(); }
                if (LevelManager.CurrentLevel?.ID != arena.ID || Player.Instance == null || !Player.Instance.gameObject.activeInHierarchy) return;
                if (_survivalAt == 0) { _survivalAt = Time.unscaledTime + 1; return; }
                if (Time.unscaledTime < _survivalAt) return;
                _port!.text = Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!; Host();
                TravelCheck(_lifecycle is { NativeReady: true, InSurvival: true } && _lifecycle.State.Level == arena.ID, "P1 hosts an already-running native Survival arena");
                TravelCheck(Object.FindObjectOfType<HordeModeController>()._hordeModeState == HordeState.INTRO, "hosting leaves the native run in INTRO");
                TravelMark("host-ready"); _travelStep = 2; break;
            case 2:
                if (!_session!.Snapshot.Connected || !InArena(arena)) return;
                TravelCheck(HordeButton() == _survivalHordeButton, "P1 keeps native Survival selection while hosting");
                var before = SessionPatches.BlockedSaves;
                _saves!.RequestSave("manual-survival", "Survival test"); _saves.QuickSave(); _saves.AutoSave();
                TravelCheck(!_saves.Busy, "co-op manual, quick and automatic saves are refused in Survival");
                TravelCheck(SessionPatches.BlockedSaves == before, "Survival save refusal does not reach native writers");
                TravelMark("host-admitted"); _travelStep = 3; break;
            case 3:
                if (!Marked("client-guarded")) return;
                TravelCheck(Object.FindObjectOfType<HordeModeController>()._hordeModeState == HordeState.INTRO, "P2 start paths do not start P1's run");
                _travelEpoch = _lifecycle!.Epoch; GameManager.Load(null); _travelStep = 4; break;
            case 4:
                if (_lifecycle!.Epoch == _travelEpoch || !InArena(arena) || !Marked("client-restarted")) return;
                TravelCheck(_lifecycle.State.Action == TravelAction.Restart && _lifecycle.InSurvival, "same-arena retry advances travel and waits for P2");
                TravelCheck(Object.FindObjectOfType<HordeModeController>() is { _hordeModeState: HordeState.INTRO, _currentWave: 1 }, "retry resets the native run to INTRO wave 1");
                _nextMap = SurvivalCampaignTarget().ID; GameManager.LoadLevel(SurvivalCampaignTarget(), true); _travelStep = 5; break;
            case 5:
                if (!_lifecycle!.NativeReady || !_lifecycle.State.Released || LevelManager.CurrentLevel?.ID != _nextMap || !Marked("client-campaign")) return;
                TravelCheck(!_lifecycle.InSurvival && SessionLifecycle.Campaign(LevelManager.CurrentLevel), "Survival to campaign travel switches mode for both players");
                QuietNpcs(); GameManager.OpenTitle(); _travelStep = 6; break;
            case 6:
                if (!Marked("client-menu") || SessionManager.SessionActive) return;
                TravelCheck(_lifecycle is { InSurvival: false } && _lifecycle.State.Action == TravelAction.Menu, "both players return to the lobby");
                GameManager.LoadLevel(arena, true); _travelStep = 7; break;
            case 7:
                if (!InArena(arena) || !Marked("client-arena-lobby")) return;
                TravelCheck(_lifecycle!.InSurvival, "lobby to Survival travel loads both players into the arena");
                ResetSession(); _travelStep = 8; break;
            case 8:
                if (!Marked("client-host-left")) return;
                TravelCheck(SessionLifecycle.Current == null, "P1 leaving from Survival restores offline guards");
                TravelMark("host-pass"); _travelStep = 99; break;
        }
    }

    private void SurvivalClient()
    {
        var arena = SurvivalArena();
        switch (_travelStep)
        {
            case 0:
                if (!_buttonVisibleLogged) return;
                _survivalHordeButton = HordeButton();
                _address!.text = "127.0.0.1"; _port!.text = Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!;
                Join(); _travelStep = 1; break;
            case 1:
                if (!InArena(arena)) return;
                if (_survivalAt == 0) { _survivalAt = Time.unscaledTime + 1; return; } // one guard scan
                if (Time.unscaledTime < _survivalAt) return;
                TravelCheck(_lifecycle!.InSurvival, "P2 follows P1 into the active Survival arena");
                TravelCheck(HordeButton() == false, "P2 cannot open native Survival selection");
                var menu = Object.FindObjectOfType<HordeModeMenu>(true);
                if (menu != null) { MenuManager.Open(menu); TravelCheck(!menu.gameObject.activeInHierarchy, "P2 native Survival menu is blocked"); }
                var epoch = _lifecycle.Epoch; GameManager.LoadLevel(arena, true); GameManager.Load(null);
                TravelCheck(_lifecycle.Epoch == epoch && _lifecycle.NativeReady, "P2 cannot start or retry a Survival arena independently");
                var controller = Object.FindObjectOfType<HordeModeController>();
                controller.Begin();
                TravelCheck(controller._hordeModeState == HordeState.INTRO && controller._spawnedNpcs.Count == 0, "P2 native Begin is inert");
                TravelCheck(Object.FindObjectsOfType<HordeModePickupSpawner>().All(s => s._currentPickupInstance == null), "P2 does not spawn its own resource offers");
                var start = Object.FindObjectsOfType<InteractListener>().Select(l => l._interactable?.TryCast<Pickup>()).FirstOrDefault(p => p != null && p.name.Contains("Shotgun"));
                TravelCheck(start != null, "authored Survival start pickup is present on P2");
                Player.Instance.Teleport(start!.transform.position + Vector3.up * 0.1f, Player.Instance.transform.rotation);
                _survivalAt = Time.unscaledTime + 5; _travelStep = 2; break;
            case 2:
                var picked = Object.FindObjectsOfType<InteractListener>().All(l => l._interactable?.TryCast<Pickup>() is not { } p || !p.name.Contains("Shotgun") || p._pickedUp);
                if (!picked && Time.unscaledTime < _survivalAt) return;
                TravelCheck(picked, "P2 collects its personal copy of the start pickup by contact");
                TravelCheck(Object.FindObjectOfType<HordeModeController>()._hordeModeState == HordeState.INTRO, "P2 start pickup leaves its local run in INTRO");
                _travelEpoch = _lifecycle!.Epoch; TravelMark("client-guarded"); _travelStep = 3; break;
            case 3:
                if (_lifecycle!.Epoch == _travelEpoch || !InArena(arena)) return;
                TravelCheck(_lifecycle.State.Action == TravelAction.Restart, "P2 follows the same-arena retry");
                TravelCheck(Object.FindObjectOfType<HordeModeController>() is { _hordeModeState: HordeState.INTRO, _currentWave: 1 }, "P2 retry resets the native arena");
                TravelMark("client-restarted"); _travelStep = 4; break;
            case 4:
                if (_lifecycle is not { NativeReady: true } life || !life.State.Released || LevelManager.CurrentLevel == null || !SessionLifecycle.Campaign(LevelManager.CurrentLevel)) return;
                TravelCheck(!life.InSurvival, "P2 follows Survival to campaign travel");
                QuietNpcs(); TravelMark("client-campaign"); _travelStep = 5; break;
            case 5:
                if (_lifecycle!.State.Action != TravelAction.Menu || SessionManager.SessionActive) return;
                TravelMark("client-menu"); _travelStep = 6; break;
            case 6:
                if (!InArena(arena)) return;
                TravelCheck(_lifecycle!.InSurvival, "P2 follows lobby to Survival travel");
                TravelMark("client-arena-lobby"); _travelStep = 7; break;
            case 7:
                if (_lifecycle != null || SessionLifecycle.Current != null || SessionManager.SessionActive) return;
                if (MenuManager.MainCanvas == null) return;
                TravelCheck(HordeButton() == _survivalHordeButton, "P2 Survival button returns to its offline state after P1 leaves");
                TravelCheck(!InputManager.InputStateStack.Contains(InputManager.InputState.NONE), "P2 host-loss cleanup leaves no input barrier");
                TravelMark("client-host-left"); TravelMark("client-pass"); _travelStep = 99; break;
        }
    }
}
