using Hyperstrange.PBD;
using Hyperstrange.PBD.PlayerComponents;
using PBDMP.Network;
using UnityEngine;
using HordeState = Hyperstrange.PBD.HordeModeController.HordeModeStateEnum;

namespace PBDMP.Plugin;

// Phase 6 Survival failure-path fixture (Test-Travel.ps1 -SurvivalFailures):
// F1 P2 leaves during the wave countdown; F2 P2 leaves while its REST admission is loading;
// F3 the last living player (P2) leaves while P1 is dead; F4 P2 joins during P1's results and
// is admitted at the retry; F5 P1 leaves during combat.
// Labels: start by contact; waves end by native Npc.Kill plus a shortened native timer; living
// players are kept alive by restoring native hit points; deaths use native HandleKill;
// readiness uses the Ready control's API; joins/leaves use the normal Direct IP session.
public sealed partial class MultiplayerBehaviour
{
    private float _failAt, _failLoadAt;
    private bool _failKeepAlive = true, _failCountdownAtLeave, _failSawFrozen;
    private int _failRecords;
    private static bool SurvivalFailuresEnabled => Environment.GetEnvironmentVariable("PBDMP_SURVIVAL_FAILURES_TEST") == "1";
    private static bool NoInputBarrier => !InputManager.InputStateStack.Contains(InputManager.InputState.NONE);
    // The title loads asynchronously after a guest leaves; check controls once it is shown.
    private bool AtTitle => AtMenu && _view!.News.Visible;
    private static string InputDetail => $" (input {string.Join(">", InputManager.InputStateStack.ToArray())}, time scale {Time.timeScale})";

    private void RunSurvivalFailuresTest()
    {
        if (_travelStep == 99) return;
        if (_travelStarted == 0) _travelStarted = Time.unscaledTime;
        var view = _world?.SurvivalView ?? _world?.SurvivalPublished;
        File.WriteAllText(Marker(TravelRole + "-state.txt"), $"step={_travelStep}, connected={_session?.Snapshot.Connected}, waiting={_lifecycle?.AdmissionWaiting}, released={_lifecycle?.State.Released}, frozen={_lifecycle?.Frozen}, view={view}");
        if (Time.unscaledTime - _travelStarted > 340) throw new TimeoutException("Survival failure diagnostic timed out: " + _travelStep);
        if (_lifecycle is { NativeReady: false }) _lifecycle.AutoContinue();
        if (_failKeepAlive && _lifecycle is { NativeReady: true } && Player.Instance is { IsAlive: true } living)
            living.GetComponent<PlayerHitProcessorComponent>().SetCurrentHitPoints(living.HitPoints.Max);
        if (TravelRole == "host") FailHost(); else FailClient();
    }

    private void FailHost()
    {
        var arena = SurvivalArena();
        var c = _lifecycle is { InSurvival: true } ? Controller() : null;
        var s = _world?.SurvivalPublished;
        var now = Time.unscaledTime;
        var connected = _session?.Snapshot.Connected == true;
        switch (_travelStep)
        {
            case 0:
                if (!_buttonVisibleLogged) return;
                _port!.text = Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!; Host(); TravelMark("host-ready"); _travelStep = 1; break;
            case 1:
                if (!connected) return;
                GameManager.LoadLevel(arena, true); _travelStep = 2; break;
            case 2:
                if (!InArena(arena) || !Marked("client-arena")) return;
                Player.Instance.Teleport(StartPickup().transform.position + Vector3.up * 0.1f, Player.Instance.transform.rotation);
                _failAt = now + 15; _travelStep = 3; break;
            case 3:
                if (c!._hordeModeState != HordeState.COMBAT || !c._combatCountDownFinished) { if (now > _failAt) throw new TimeoutException("Shared run did not begin."); return; }
                _travelStep = 4; break;
            case 4: // F1: REST 2, P2 ready first, then P1's readiness starts the countdown
                EndWave(c!);
                if (c!._hordeModeState != HordeState.REST || s is not { Phase: SurvivalPhase.Rest, Offers.Length: > 0 }) return;
                TravelMark("host-rest2");
                if ((s.Ready & SurvivalProtocol.Partner) == 0) return;
                _world!.RequestSurvivalReady(); _failAt = now + 10; _travelStep = 5; break;
            case 5:
                if (connected) { if (now > _failAt) throw new TimeoutException("P2 did not leave during the countdown."); return; }
                _failCountdownAtLeave = c!._hordeModeState == HordeState.COMBAT && !c._combatCountDownFinished; _failAt = now + 6; _travelStep = 6; break;
            case 6:
                if (!c!._combatCountDownFinished) { if (now > _failAt) throw new TimeoutException("Countdown did not finish after P2 left."); return; }
                if (_failAt - now > 4.5f) return;
                TravelCheck(_failCountdownAtLeave, "P2 left while P1's wave countdown was running");
                TravelCheck(c._hordeModeState == HordeState.COMBAT && c._currentWave == 2 && _world!.SurvivalEnds == 0 && _world.SurvivalParticipantsForTest == SurvivalProtocol.Host,
                    "the countdown finishes and P1 keeps playing wave 2 alone");
                TravelCheck(!_lifecycle!.Frozen && NoInputBarrier && Time.timeScale > 0, "P1 has no freeze or input barrier after P2 leaves during the countdown");
                c._combatTimeLeft += 120; TravelMark("host-f1"); _failAt = now + 90; _travelStep = 7; break;
            case 7: // F2: P2 held in combat, then admitted at REST 3, and leaves while loading
                if (!Marked("client-held")) return;
                EndWave(c!);
                if (c!._hordeModeState != HordeState.REST) return;
                _failSawFrozen |= _lifecycle!.Frozen;
                if (!Marked("client-left-loading") || connected) { if (now > _failAt) throw new TimeoutException("P2 did not leave during admission."); return; }
                _failAt = now + 2; _travelStep = 8; break;
            case 8:
                _failSawFrozen |= _lifecycle!.Frozen;
                if (now < _failAt) return;
                TravelCheck(!_lifecycle.Frozen && !_lifecycle.AdmissionWaiting && NoInputBarrier && _world!.SurvivalParticipantsForTest == SurvivalProtocol.Host,
                    $"P2 leaving during its admission load releases P1 (P1 {(_failSawFrozen ? "was" : "was not")} held for the admission)");
                TravelCheck(c!._hordeModeState == HordeState.REST && c._currentWave == 3 && OfferCopies().Count > 0 && c._spawnedNpcs.Count == 0, "P1's REST and offers stay intact after the abandoned admission");
                _world!.RequestSurvivalReady(); _failRecords = _world.SurvivalWaveStarts; _failAt = now + 10; _travelStep = 9; break;
            case 9:
                if (c!._hordeModeState != HordeState.COMBAT) { if (now > _failAt) throw new TimeoutException("P1 alone could not start wave 3."); return; }
                TravelCheck(_world!.SurvivalWaveStarts == _failRecords + 1, "P1 alone starts wave 3 once after the abandoned admission");
                c._combatTimeLeft += 120; TravelMark("host-f2"); _failAt = 0; _travelStep = 10; break;
            case 10: // P2 rejoins for F3 at REST 4
                if (!Marked("client-held2")) return;
                EndWave(c!);
                if (c!._hordeModeState != HordeState.REST || _world!.SurvivalParticipantsForTest != SurvivalProtocol.Everyone || !Marked("client-admitted2")) return;
                _world.RequestSurvivalReady(); _failAt = now + 10; _travelStep = 11; break;
            case 11:
                if (c!._hordeModeState != HordeState.COMBAT || !c._combatCountDownFinished) { if (now > _failAt) throw new TimeoutException("Wave 4 did not start."); return; }
                c._combatTimeLeft += 120; _failKeepAlive = false; Die(); _failAt = now + 2; _travelStep = 12; break;
            case 12: // F3: P1 dead, P2 alive; the run continues until P2 leaves
                if (now < _failAt) return;
                TravelCheck(s is { Ended: false } && _world!.SurvivalEnds == 0 && c!._hordeModeState == HordeState.COMBAT, "P1 dead with P2 alive keeps the run going");
                _failRecords = new SurvivalRecordStore(SharedWorld.SurvivalRecordsPath).Load().Recent.Length;
                TravelMark("host-dead"); _failAt = now + 15; _travelStep = 13; break;
            case 13:
                // With nobody connected P1 publishes no new state frame; read its own end count.
                if (connected || _world!.SurvivalEnds == 0 || ResultsMenu() == null) { if (now > _failAt) throw new TimeoutException("Run did not end after the last survivor left."); return; }
                TravelCheck(_world!.SurvivalEnds == 1, "the last living player leaving ends the run once");
                TravelCheck(new SurvivalRecordStore(SharedWorld.SurvivalRecordsPath).Load().Recent.Length == _failRecords + 1, "the ended run is recorded once on P1");
                TravelMark("host-f3"); _failAt = now + 30; _travelStep = 14; break;
            case 14: // F4: P2 joins during the results and is admitted at P1's retry
                if (!Marked("client-held3")) { if (now > _failAt) throw new TimeoutException("P2 was not held during results."); return; }
                TravelCheck(_lifecycle!.AdmissionWaiting && ResultsMenu() != null && _world!.SurvivalEnds == 1, "P1 holds a join that arrives during its results");
                _travelEpoch = _lifecycle.Epoch; ResultsMenu()!.ResetLevel(); _failKeepAlive = true; _failAt = now + 60; _travelStep = 15; break;
            case 15:
                if (_lifecycle!.Epoch == _travelEpoch || !InArena(arena) || c is not { _hordeModeState: HordeState.INTRO } || _world!.SurvivalParticipantsForTest != SurvivalProtocol.Everyone || !Marked("client-admitted3"))
                { if (now > _failAt) throw new TimeoutException("Results-time join was not admitted at the retry."); return; }
                TravelCheck(!_lifecycle.AdmissionWaiting && NoInputBarrier && ResultsMenu() == null && Player.Instance.IsAlive, "the results-time join is admitted at P1's retry with results and input cleared");
                Player.Instance.Teleport(StartPickup().transform.position + Vector3.up * 0.1f, Player.Instance.transform.rotation);
                _failAt = now + 15; _travelStep = 16; break;
            case 16: // F5: P1 leaves during combat
                if (c!._hordeModeState != HordeState.COMBAT || !c._combatCountDownFinished) { if (now > _failAt) throw new TimeoutException("Second run did not start."); return; }
                _failAt = now + 2; _travelStep = 17; break;
            case 17:
                if (now < _failAt) return;
                ResetSession(); TravelMark("host-left"); _travelStep = 18; break;
            case 18:
                if (!Marked("client-f5")) return;
                TravelMark("host-pass"); _travelStep = 99; break;
        }
    }

    private void FailClient()
    {
        var arena = SurvivalArena();
        var view = _world?.SurvivalView;
        var now = Time.unscaledTime;
        switch (_travelStep)
        {
            case 0:
                if (!_buttonVisibleLogged) return;
                JoinHost(); _travelStep = 1; break;
            case 1:
                if (!InArena(arena) || view is not { Phase: SurvivalPhase.Intro } || !_lifecycle!.PersonalReady || !_lifecycle.WorldReady) return;
                _world!.RequestSurvivalReady(); TravelMark("client-arena"); _travelStep = 2; break;
            case 2:
                if (!Marked("host-rest2") || view is not { Phase: SurvivalPhase.Rest, Wave: 2 }) return;
                _world!.RequestSurvivalReady(); _failAt = now + 15; _travelStep = 3; break;
            case 3: // F1: leave as soon as the countdown is shown
                if (view is not { Phase: SurvivalPhase.Combat, CountdownDone: false, Countdown: > 0 }) { if (now > _failAt) throw new TimeoutException("P2 did not see the countdown."); return; }
                ResetSession(); TravelMark("client-left-countdown"); _travelStep = 4; break;
            case 4:
                if (!AtTitle || !Marked("host-f1")) return;
                TravelCheck(NoInputBarrier, "P2 leaving during the countdown returns to the menu with no input barrier" + InputDetail);
                JoinHost(); _failAt = now + 20; _travelStep = 5; break;
            case 5:
                if (_lifecycle is not { AdmissionWaiting: true }) { if (now > _failAt) throw new TimeoutException("P2 was not held."); return; }
                TravelMark("client-held"); _failAt = now + 90; _travelStep = 6; break;
            case 6: // F2: P1 began admitting P2; leave before the release
                if (_lifecycle is not { AdmissionWaiting: false } life) return;
                if (life.State.Released && life.PersonalReady && life.WorldReady) throw new InvalidOperationException("P2's admission completed before it could leave.");
                if (life.State.Action == TravelAction.Menu) { if (now > _failAt) throw new TimeoutException("P2's admission did not begin."); return; }
                if (_failLoadAt == 0) _failLoadAt = now + 1; // leave a moment into the admission load
                if (now < _failLoadAt) return;
                ResetSession(); TravelMark("client-left-loading"); _travelStep = 7; break;
            case 7:
                if (!AtTitle || !Marked("host-f2")) return;
                TravelCheck(NoInputBarrier, "P2 leaving during its admission load returns to the menu with no input barrier" + InputDetail);
                JoinHost(); _failAt = now + 20; _travelStep = 8; break;
            case 8:
                if (_lifecycle is not { AdmissionWaiting: true }) { if (now > _failAt) throw new TimeoutException("P2 was not held again."); return; }
                TravelMark("client-held2"); _travelStep = 9; break;
            case 9:
                if (!InArena(arena) || view is not { Phase: SurvivalPhase.Rest, Wave: 4 } || !_lifecycle!.PersonalReady || !_lifecycle.WorldReady) return;
                _world!.RequestSurvivalReady(); TravelMark("client-admitted2"); _travelStep = 10; break;
            case 10: // F3: leave alive while P1 is dead
                if (!Marked("host-dead")) return;
                ResetSession(); _travelStep = 11; break;
            case 11: // F4: join during P1's results
                if (!AtTitle || !Marked("host-f3")) return;
                JoinHost(); _failAt = now + 20; _travelStep = 12; break;
            case 12:
                if (_lifecycle is not { AdmissionWaiting: true }) { if (now > _failAt) throw new TimeoutException("P2 was not held during results."); return; }
                TravelCheck(_view!.Hud.HoldText.StartsWith("Waiting for P1's next Survival wave", StringComparison.Ordinal), "a results-time join sees the waiting notice");
                TravelMark("client-held3"); _travelStep = 13; break;
            case 13:
                if (!InArena(arena) || view is not { Phase: SurvivalPhase.Intro, Ended: false } || !_lifecycle!.PersonalReady || !_lifecycle.WorldReady) return;
                TravelCheck(Revived() && NoInputBarrier && ResultsMenu() == null, "the results-time joiner enters P1's retry alive with no results or input barrier");
                _world!.RequestSurvivalReady(); TravelMark("client-admitted3"); _travelStep = 14; break;
            case 14: // F5: P1 leaves during combat
                if (!Marked("host-left") || !AtTitle) return;
                TravelCheck(NoInputBarrier && ResultsMenu() == null && !SessionManager.SessionActive, "P1 leaving during combat returns P2 to the menu with no input barrier" + InputDetail);
                TravelMark("client-f5"); TravelMark("client-pass"); _travelStep = 99; break;
        }
    }
}
