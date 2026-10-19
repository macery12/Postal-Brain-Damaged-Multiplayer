using Hyperstrange.PBD;
using Hyperstrange.PBD.PlayerComponents;
using Hyperstrange.PBD.UI.Menus;
using PBDMP.Network;
using UnityEngine;
using Object = UnityEngine.Object;
using HordeState = Hyperstrange.PBD.HordeModeController.HordeModeStateEnum;

namespace PBDMP.Plugin;

// Phase 4 Survival HUD/results fixture (Test-Travel.ps1 -SurvivalResults): P2's native HUD from
// P1's state, P2 damage in the shared combo, exact team results on both native results
// screens, P1-only results controls, local co-op records and untouched solo metadata.
// Labels: start by contact; enemies are killed with native Npc.Hit and players kept alive by
// restoring native hit points; P2's damage is one native Player.Hit; deaths use native
// HandleKill; readiness uses the Ready control's API.
public sealed partial class MultiplayerBehaviour
{
    private float _resultsAt, _resultsHitAt, _resultsSoloBest;
    private int _resultsDamageBase;
    private string _resultsTimeLabel = "";
    private bool _resultsKeepAlive = true;
    private static bool SurvivalResultsEnabled => Environment.GetEnvironmentVariable("PBDMP_SURVIVAL_RESULTS_TEST") == "1";

    private static string ResultSummary(SurvivalResult r) => $"{r.RunId}|{r.Arena}|{r.Difficulty}|{r.Score:R}|{r.Kills}|{r.WavesSurvived}|{r.Damage:R}|{r.BestWeapon}|{r.CompletionTime}";
    private static SurvivalResult? LastRecord() => new SurvivalRecordStore(SharedWorld.SurvivalRecordsPath).Load().Recent.FirstOrDefault();
    private static EndHordeMenu? ResultsMenu() => Object.FindObjectOfType<EndHordeMenu>() is { } menu && menu.gameObject.activeInHierarchy ? menu : null;

    private void RunSurvivalResultsTest()
    {
        if (_travelStep == 99) return;
        if (_travelStarted == 0) _travelStarted = Time.unscaledTime;
        var view = _world?.SurvivalView ?? _world?.SurvivalPublished;
        if (_lifecycle != null) File.WriteAllText(Marker(TravelRole + "-state.txt"), $"step={_travelStep}, dead={_lifecycle?.PlayerState.Pending}, view={view}, best={_world?.SurvivalBestLabelForTest}, banner={_view?.Hud.BannerText}, reports={SurvivalPatches.DamageReports}, damage={_world?.SurvivalPartnerDamage}, rejected={_world?.SurvivalRequestsRejected}");
        if (Time.unscaledTime - _travelStarted > 330) throw new TimeoutException("Survival results diagnostic timed out: " + _travelStep);
        if (_lifecycle is { NativeReady: false }) _lifecycle.AutoContinue();
        if (_resultsKeepAlive && _lifecycle is { NativeReady: true } && Player.Instance is { IsAlive: true } living)
            living.GetComponent<PlayerHitProcessorComponent>().SetCurrentHitPoints(living.HitPoints.Max);
        if (TravelRole == "host") ResultsHost(); else ResultsClient();
    }

    private void ResultsHost()
    {
        var arena = SurvivalArena();
        var c = _lifecycle is { InSurvival: true } ? Controller() : null;
        var s = _world?.SurvivalPublished;
        var now = Time.unscaledTime;
        switch (_travelStep)
        {
            case 0:
                if (!_buttonVisibleLogged) return;
                _port!.text = Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!; Host(); TravelMark("host-ready"); _travelStep = 1; break;
            case 1:
                if (!_session!.Snapshot.Connected) return;
                GameManager.LoadLevel(arena, true); _travelStep = 2; break;
            case 2:
                if (!InArena(arena) || !Marked("client-arena") || _world!.SurvivalBestLabelForTest.Length == 0) return;
                TravelCheck(_world.SurvivalBestLabelForTest.EndsWith(": 0", StringComparison.Ordinal), "P1's HUD best line shows the (empty) local co-op best");
                _resultsSoloBest = LevelManager.CurrentLevel.HordeScore;
                Player.Instance.Teleport(StartPickup().transform.position + Vector3.up * 0.1f, Player.Instance.transform.rotation);
                _resultsAt = now + 15; _travelStep = 3; break;
            case 3: // kill several enemies to raise the shared combo
                if (c!._hordeModeState != HordeState.COMBAT || !c._combatCountDownFinished) { if (now > _resultsAt) throw new TimeoutException("Run did not start."); return; }
                if (c.Kills < 4)
                {
                    if (now < _resultsHitAt) return;
                    _resultsHitAt = now + 0.3f;
                    var target = Spawned(c).FirstOrDefault();
                    target?.Hit(new HitData { dealer = Player.Instance.Cast<IAiActor>(), damage = 100000, weakSpotMultiplier = 1, position = target.CenterOfMass, direction = Vector3.forward, collider = target.GetComponentInChildren<Collider>() });
                    if (now > _resultsAt + 30) throw new TimeoutException("P1 could not score kills.");
                    return;
                }
                c._combatTimeLeft += 60; // synthetic: keep the wave running through the checks
                TravelCheck(c._currentScore > 0, "P1 native kills score in the shared run");
                TravelMark("host-scored"); _travelStep = 4; break;
            case 4: // quiet the wave: synthetic spawn-power cap, remaining enemies removed
                if (!Marked("client-hud")) return;
                c!._usedSpawnPower = 1e9f; KillAll(c); _resultsAt = now + 2; _travelStep = 40; break;
            case 40:
                KillAll(c!);
                if (now < _resultsAt) return;
                _resultsDamageBase = _world!.SurvivalPartnerDamage; TravelMark("host-quiet"); _travelStep = 41; break;
            case 41: // one hit on P2's proxy travels through the shared incoming-hit path
                if (!Marked("client-quiet")) return;
                _world!.Remote!.Hit(new HitData { damage = 5, weakSpotMultiplier = 1, position = _world.Remote.CenterOfMass, direction = Vector3.forward });
                TravelMark("host-damage"); _resultsAt = now + 6; _travelStep = 5; break;
            case 5: // P2's one native damage event applies the native combo penalty once on P1
                if (_world!.SurvivalPartnerDamage == _resultsDamageBase) { if (now > _resultsAt) throw new TimeoutException("P2 damage did not reach P1."); return; }
                if (now < _resultsAt - 4) return; // allow any duplicate to arrive
                var (before, after) = _world.SurvivalPartnerPenaltyForTest;
                TravelCheck(_world.SurvivalPartnerDamage == _resultsDamageBase + 1 && Mathf.Abs(before - after - Mathf.Min(1, before - 1)) < 0.001f,
                    "one P2 damage event applies the native combo penalty once on P1");
                TravelMark("host-wipe"); _travelStep = 6; break;
            case 6:
                if (!Marked("client-dead")) return;
                _resultsKeepAlive = false; Die(); _resultsAt = now + 10; _travelStep = 7; break;
            case 7:
                var menu = ResultsMenu();
                if (s?.Ended != true || menu == null) { if (now > _resultsAt) throw new TimeoutException("P1 results did not open."); return; }
                var record = LastRecord();
                TravelCheck(record != null && record.RunId == s.RunId && record.Score == s.Score && record.Kills == s.Kills && record.WavesSurvived == s.WavesSurvived,
                    "P1 records the team result locally once");
                TravelCheck(_world!.SurvivalRecordForTest is { Added: true, NewBest: true } && c!.IsScoreNewBest && c.OldScoreToDisplay == 0, "P1's native results show the first co-op best");
                TravelCheck(LevelManager.CurrentLevel.HordeScore == _resultsSoloBest && SessionPatches.BlockedSaves > 0, "solo Survival metadata stays untouched");
                TravelCheck(menu._resetLevelButton.interactable || !menu._resetLevelButton.gameObject.activeInHierarchy, "P1 keeps its native results controls");
                File.WriteAllText(Marker("host-record.txt"), ResultSummary(record!));
                TravelMark("host-results"); _travelStep = 8; break;
            case 8:
                if (!Marked("client-results")) return;
                _travelEpoch = _lifecycle!.Epoch; ResultsMenu()!.ResetLevel(); _travelStep = 9; break;
            case 9:
                if (_lifecycle!.Epoch == _travelEpoch || !InArena(arena) || !Marked("client-retry")) return;
                _resultsKeepAlive = true;
                var best = File.ReadAllText(Marker("host-record.txt")).Split('|')[3];
                if (!_world!.SurvivalBestLabelForTest.EndsWith(": " + ((float)double.Parse(best, System.Globalization.CultureInfo.InvariantCulture)).ToString("0"), StringComparison.Ordinal)) return;
                TravelCheck(true, "after retry P1's HUD best line shows the recorded co-op best");
                ResetSession(); TravelMark("host-pass"); _travelStep = 99; break;
        }
    }

    private void ResultsClient()
    {
        var arena = SurvivalArena();
        var c = _lifecycle is { InSurvival: true } ? Controller() : null;
        var view = _world?.SurvivalView;
        var hud = _world?.SurvivalHudForTest;
        var now = Time.unscaledTime;
        switch (_travelStep)
        {
            case 0:
                if (!_buttonVisibleLogged) return;
                _address!.text = "127.0.0.1"; _port!.text = Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!; Join(); _travelStep = 1; break;
            case 1:
                if (!InArena(arena) || view is not { Phase: SurvivalPhase.Intro } || hud == null) return;
                TravelCheck(_world!.SurvivalBestLabelForTest.EndsWith(": 0", StringComparison.Ordinal), "P2's HUD best line shows its local co-op best");
                _world.RequestSurvivalReady(); TravelMark("client-arena"); _resultsAt = now + 30; _travelStep = 2; break;
            case 2: // P2's native HUD follows P1's state
                if (!Marked("host-scored") || view is not { Phase: SurvivalPhase.Combat, CountdownDone: true } || view.Kills < 4) { if (now > _resultsAt) throw new TimeoutException("P2 did not see P1's scored combat."); return; }
                if (!hud!._scoreLabel.text.Contains(((float)view.Score).ToString("000000000"), StringComparison.Ordinal) ||
                    !hud._hostilesLeftLabel.text.Contains(view.Alive.ToString(), StringComparison.Ordinal)) { if (now > _resultsAt) throw new TimeoutException("P2 HUD labels did not follow P1."); return; }
                TravelCheck(hud.gameObject.activeInHierarchy, "P2's native Survival HUD is shown");
                TravelCheck(true, "P2's native score and hostiles labels show P1's values");
                _resultsTimeLabel = hud._waveTimeLabel.text; _resultsAt = now + 2.5f; _travelStep = 3; break;
            case 3:
                if (now < _resultsAt) return;
                TravelCheck(hud!._waveTimeLabel.text != _resultsTimeLabel && hud._waveTimeLabel.text.Contains(view!.Wave.ToString(), StringComparison.Ordinal),
                    "P2's native wave timer counts down between P1's updates");
                TravelMark("client-hud"); _travelStep = 4; break;
            case 4: // P2 takes the hit natively and reports it once
                if (!Marked("host-quiet")) return;
                if (_resultsHitAt == 0) { _resultsDamageBase = SurvivalPatches.DamageReports; _resultsHitAt = now; TravelMark("client-quiet"); }
                if (!Marked("host-damage") || SurvivalPatches.DamageReports == _resultsDamageBase) { if (now > _resultsHitAt + 20) throw new TimeoutException("P2 took no native damage."); return; }
                TravelCheck(SurvivalPatches.DamageReports == _resultsDamageBase + 1, "P2's native damage is reported once instead of penalizing its mirrored combo");
                _travelStep = 5; break;
            case 5:
                if (!Marked("host-wipe")) return;
                _resultsKeepAlive = false; Die(); TravelMark("client-dead"); _resultsAt = now + 15; _travelStep = 6; break;
            case 6: // P2's native results show P1's exact team result
                var menu = ResultsMenu();
                if (view is not { Ended: true } || menu == null || !Marked("host-results")) { if (now > _resultsAt) throw new TimeoutException("P2 results did not open."); return; }
                TravelCheck(c!.Kills == view.Kills && c.WavesSurvived == view.WavesSurvived && c._currentScore == (float)view.Score && LevelController._instance.TimeStat.CompletionTime == view.ResultTime,
                    "P2's native results use P1's final score, kills, waves and time");
                TravelCheck(view.ResultWeapon.Length == 0 || c.BestWeapon?.ID == view.ResultWeapon, "P2's native results show P1's best weapon");
                TravelCheck(LastRecord() is { } record && ResultSummary(record) == File.ReadAllText(Marker("host-record.txt")), "P2 keeps an identical local copy of the team result");
                TravelCheck(c.IsScoreNewBest && c.OldScoreToDisplay == 0, "P2's native results compare with its local co-op best");
                if (!_world!.ClientResultsLockedForTest) return;
                TravelCheck(true, "P2's native retry and menu buttons are disabled");
                TravelCheck(_view!.Hud.BannerText.StartsWith("Run over", StringComparison.Ordinal), "P2 is told that P1 controls the results");
                var epoch = _lifecycle!.Epoch; GameManager.OpenTitle(); GameManager.Load(null);
                TravelCheck(_lifecycle.Epoch == epoch && InArena(arena), "P2 cannot leave or retry from the results itself");
                _travelEpoch = epoch; TravelMark("client-results"); _travelStep = 7; break;
            case 7:
                if (_lifecycle!.Epoch == _travelEpoch || !InArena(arena) || view is not { Phase: SurvivalPhase.Intro, Ended: false }) return;
                _resultsKeepAlive = true;
                var best = File.ReadAllText(Marker("host-record.txt")).Split('|')[3];
                if (!_world!.SurvivalBestLabelForTest.EndsWith(": " + ((float)double.Parse(best, System.Globalization.CultureInfo.InvariantCulture)).ToString("0"), StringComparison.Ordinal)) return;
                TravelCheck(ResultsMenu() == null && !InputManager.InputStateStack.Contains(InputManager.InputState.NONE), "P2 follows P1's retry with results and input cleared");
                TravelCheck(true, "P2's HUD best line shows the recorded co-op best");
                TravelMark("client-retry"); _travelStep = 8; break;
            case 8:
                if (_lifecycle != null || SessionLifecycle.Current != null || SessionManager.SessionActive) return;
                TravelMark("client-pass"); _travelStep = 99; break;
        }
    }
}
