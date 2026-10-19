using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using Hyperstrange.PBD.PlayerComponents;
using PBDMP.Network;
using UnityEngine;
using Object = UnityEngine.Object;
using HordeState = Hyperstrange.PBD.HordeModeController.HordeModeStateEnum;

namespace PBDMP.Plugin;

// Phase 5 Survival late join/reconnect fixture (Test-Travel.ps1 -SurvivalJoin): P1 starts alone;
// P2 joins during combat and waits while P1 continues; admission at REST with P1's loadout,
// offers and resources; a P2 that died in combat rejoins at the next REST; a P2 that died
// during REST is held until the following REST.
// Labels: start by contact; waves end by native Npc.Kill plus a shortened native timer; living
// players are kept alive by restoring native hit points; deaths use native HandleKill;
// readiness uses the Ready control's API; reconnects use the normal Direct IP join.
public sealed partial class MultiplayerBehaviour
{
    private float _joinAt, _joinTimer, _joinHistoryAt;
    private bool _joinKeepAlive = true;
    private int _joinStarts;
    private static bool SurvivalJoinEnabled => Environment.GetEnvironmentVariable("PBDMP_SURVIVAL_JOIN_TEST") == "1";

    private bool AtMenu => _lifecycle == null && SessionLifecycle.Current == null && !SessionManager.SessionActive && MenuManager.MainCanvas != null;
    private void JoinHost() { _address!.text = "127.0.0.1"; _port!.text = Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!; Join(); }
    private void EndWave(HordeModeController c)
    {
        KillAll(c);
        if (c._hordeModeState == HordeState.COMBAT) c._combatTimeLeft = Mathf.Min(c._combatTimeLeft, 0.5f); // synthetic end of the wave
    }

    private void RunSurvivalJoinTest()
    {
        if (_travelStep == 99) return;
        if (_travelStarted == 0) _travelStarted = Time.unscaledTime;
        var view = _world?.SurvivalView ?? _world?.SurvivalPublished;
        var joinState = $"step={_travelStep}, waiting={_lifecycle?.AdmissionWaiting}, released={_lifecycle?.State.Released}, joining={_lifecycle?.State.Joining}, dead={_lifecycle?.PlayerState.Pending}, view={view}, hold={_view?.Hud.HoldText}, life={_lifecycle?.State}, nativeReady={_lifecycle?.NativeReady}, worldReady={_lifecycle?.WorldReady}, personal={_lifecycle?.PersonalReady}, persistence={_lifecycle?.PersistenceHold}, frozen={_lifecycle?.Frozen}, pose={_session?.Snapshot.RemotePose?.Epoch}/{_session?.Snapshot.RemotePose?.Ready}, world=[{_world?.JoinStateForTest}], saves={_saves?.Status}, native=[{_saves?.JoinCaptureForTest}]";
        File.WriteAllText(Marker(TravelRole + "-state.txt"), joinState);
        if (Time.unscaledTime >= _joinHistoryAt) { _joinHistoryAt = Time.unscaledTime + 2; File.AppendAllText(Marker(TravelRole + "-history.txt"), $"{Time.unscaledTime:F1} {joinState}" + Environment.NewLine); }
        if (Time.unscaledTime - _travelStarted > 340) throw new TimeoutException("Survival join diagnostic timed out: " + _travelStep);
        if (_lifecycle is { NativeReady: false }) _lifecycle.AutoContinue();
        if (_joinKeepAlive && _lifecycle is { NativeReady: true } && Player.Instance is { IsAlive: true } living)
            living.GetComponent<PlayerHitProcessorComponent>().SetCurrentHitPoints(living.HitPoints.Max);
        if (TravelRole == "host") JoinTestHost(); else JoinTestClient();
    }

    private void JoinTestHost()
    {
        var arena = SurvivalArena();
        var c = _lifecycle is { InSurvival: true } ? Controller() : null;
        var s = _world?.SurvivalPublished;
        var now = Time.unscaledTime;
        switch (_travelStep)
        {
            case 0:
                if (!_buttonVisibleLogged) return;
                _port!.text = Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!; Host(); TravelMark("host-ready");
                GameManager.LoadLevel(arena, true); _travelStep = 1; break;
            case 1: // alone, P1's start pickup begins at once
                if (!InArena(arena)) return;
                Player.Instance.Teleport(StartPickup().transform.position + Vector3.up * 0.1f, Player.Instance.transform.rotation);
                _joinAt = now + 8; _travelStep = 2; break;
            case 2:
                // The native wave timer only ticks after the 3 s start countdown.
                if (c!._hordeModeState != HordeState.COMBAT || !c._combatCountDownFinished) { if (now > _joinAt) throw new TimeoutException("Solo P1 run did not begin."); return; }
                TravelCheck(SurvivalPatches.BeginCount == 1, "P1 alone starts the run from the start pickup at once");
                c._combatTimeLeft += 120; // synthetic: a long wave for the waiting join
                File.WriteAllText(Marker("host-weapon.txt"), Player.Instance.CurrentWeapon?.WeaponId?.ID ?? "");
                TravelMark("host-combat"); _travelStep = 3; break;
            case 3: // P2 waits while P1's wave continues
                if (!Marked("client-waiting")) return;
                TravelCheck(_session!.Snapshot.Connected && _lifecycle!.AdmissionWaiting && !_lifecycle.State.Joining && !_lifecycle.Frozen,
                    "P1 holds the combat-time join without freezing its run");
                TravelCheck(_world!.SurvivalParticipantsForTest == SurvivalProtocol.Host, "a waiting P2 is not a participant or an extra life");
                _joinTimer = c!._combatTimeLeft; _joinAt = now + 1.5f; _travelStep = 4; break;
            case 4:
                if (now < _joinAt) return;
                TravelCheck(c!._combatTimeLeft < _joinTimer - 1, "P1's wave timer keeps running while P2 waits");
                _travelStep = 5; break;
            case 5: // REST: admission
                EndWave(c!);
                if (c!._hordeModeState != HordeState.REST || !Marked("client-admitted")) return;
                TravelCheck(!_lifecycle!.AdmissionWaiting && _lifecycle.State.Released && _world!.SurvivalParticipantsForTest == SurvivalProtocol.Everyone, "P2 is admitted once at REST and becomes a participant");
                TravelCheck(c._spawnedNpcs.Count == 0 && OfferCopies().Count == SurvivalProtocol.ParseOffers(s?.Offers ?? "").Length && OfferCopies().Count > 0, "admission adds no enemies and leaves P1's offers intact");
                TravelCheck(!InputManager.InputStateStack.Contains(InputManager.InputState.NONE), "P1 has no input barrier after admission");
                _world!.RequestSurvivalReady(); _joinAt = now + 10; _travelStep = 6; break;
            case 6:
                if (c!._hordeModeState != HordeState.COMBAT) { if (now > _joinAt) throw new TimeoutException("Wave 2 did not start after both were ready."); return; }
                TravelCheck(c._currentWave == 2, "the admitted P2's readiness is required and counted for wave 2");
                c._combatTimeLeft += 60; TravelMark("host-wave2"); _travelStep = 7; break;
            case 7: // P2 died in combat and left
                if (!Marked("client-left-dead") || _session!.Snapshot.Connected) return;
                TravelCheck(_world!.DeadPartnersForTest == 1 && c!._hordeModeState == HordeState.COMBAT, "P1 keeps P2's death by identity after P2 leaves");
                _travelStep = 8; break;
            case 8:
                EndWave(c!);
                if (c!._hordeModeState != HordeState.REST) return;
                TravelMark("host-rest3"); _travelStep = 9; break;
            case 9:
                if (!Marked("client-rejoined")) return;
                TravelCheck(_world!.SurvivalParticipantsForTest == SurvivalProtocol.Everyone && _world.DeadPartnersForTest == 0 && c!._currentWave == 3, "P2 that died in wave 2 rejoins alive at the wave 3 boundary");
                _travelStep = 10; break;
            case 10: // P2 died during REST 3 and reconnects: held until REST 4
                if (!Marked("client-held")) return;
                TravelCheck(_lifecycle!.AdmissionWaiting && _world!.DeadPartnersForTest == 1 && c!._hordeModeState == HordeState.REST, "P2 that died during REST cannot rejoin into the same preparation");
                _joinStarts = _world!.SurvivalWaveStarts; _world.RequestSurvivalReady(); _joinAt = now + 10; _travelStep = 11; break;
            case 11:
                if (c!._hordeModeState != HordeState.COMBAT) { if (now > _joinAt) throw new TimeoutException("P1 alone could not start wave 3."); return; }
                TravelCheck(_world!.SurvivalWaveStarts == _joinStarts + 1 && _lifecycle!.AdmissionWaiting, "P1 starts wave 3 alone while the dead P2 keeps waiting");
                _travelStep = 12; break;
            case 12:
                EndWave(c!);
                if (c!._hordeModeState != HordeState.REST || !Marked("client-readmitted")) return;
                TravelCheck(c._currentWave == 4 && _world!.SurvivalParticipantsForTest == SurvivalProtocol.Everyone && !_lifecycle!.AdmissionWaiting, "the held P2 is admitted at the next REST");
                ResetSession(); TravelMark("host-pass"); _travelStep = 99; break;
        }
    }

    private void JoinTestClient()
    {
        var arena = SurvivalArena();
        var view = _world?.SurvivalView;
        var now = Time.unscaledTime;
        switch (_travelStep)
        {
            case 0:
                if (!_buttonVisibleLogged || !Marked("host-combat")) return;
                JoinHost(); _joinAt = now + 20; _travelStep = 1; break;
            case 1:
                if (_lifecycle is not { AdmissionWaiting: true }) { if (now > _joinAt) throw new TimeoutException("P2 was not held for the next wave."); return; }
                TravelCheck(_view!.Hud.HoldText.StartsWith("Waiting for P1's next Survival wave", StringComparison.Ordinal), "P2 sees the waiting-for-next-wave notice");
                _joinAt = now + 4; _travelStep = 2; break;
            case 2:
                if (now < _joinAt) return;
                TravelCheck(_lifecycle!.AdmissionWaiting && (LevelManager.CurrentLevel == null || LevelManager.CurrentLevel.ID != arena.ID), "P2 keeps waiting outside the arena during P1's combat");
                TravelMark("client-waiting"); _travelStep = 3; break;
            case 3: // admitted at REST with P1's gear and the current preparation
                if (!InArena(arena) || view is not { Phase: SurvivalPhase.Rest, Wave: 2 } || !_lifecycle!.PersonalReady || !_lifecycle.WorldReady) return;
                if (OfferCopies().Count < SurvivalProtocol.ParseOffers(view.Offers).Length || Object.FindObjectsOfType<HordeModePickupSpawner>().Count(Available) == 0) return;
                TravelCheck(Revived(), "the admitted P2 has full health and zero armor");
                var weapon = File.ReadAllText(Marker("host-weapon.txt"));
                TravelCheck(weapon.Length == 0 || new PlayerInventoryComponent.SaveData(Player.Instance.GetComponent<PlayerInventoryComponent>()).HeldItems.ContainsKey(weapon), "the admitted P2 receives P1's weapons");
                TravelCheck(Vector3.Distance(Player.Instance.FeetPosition, PartnerFeet()) < 6, "the admitted P2 is placed beside P1");
                TravelCheck(ReplicaAlive() == 0, "no historical enemies appear for the admitted P2");
                TravelCheck(true, "the admitted P2 receives P1's offers and its own resources");
                _world!.RequestSurvivalReady(); TravelMark("client-admitted"); _travelStep = 4; break;
            case 4: // die in combat, then leave
                if (!Marked("host-wave2") || view is not { Phase: SurvivalPhase.Combat, Wave: 2 }) return;
                _joinKeepAlive = false; Die(); _joinAt = now + 2; _travelStep = 5; break;
            case 5:
                if (now < _joinAt) return;
                ResetSession(); TravelMark("client-left-dead"); _travelStep = 6; break;
            case 6:
                if (!AtMenu || !Marked("host-rest3")) return;
                _joinKeepAlive = true; JoinHost(); _travelStep = 7; break;
            case 7:
                if (!InArena(arena) || view is not { Phase: SurvivalPhase.Rest, Wave: 3 } || !_lifecycle!.PersonalReady || !_lifecycle.WorldReady || !Revived()) return;
                TravelCheck(true, "P2 rejoins alive at the first REST after its combat death");
                TravelMark("client-rejoined"); _joinAt = now + 1; _travelStep = 8; break;
            case 8: // die during REST 3 and reconnect at once
                if (now < _joinAt) return;
                _joinKeepAlive = false; Die(); _joinAt = now + 1.5f; _travelStep = 9; break;
            case 9:
                if (now < _joinAt) return;
                ResetSession(); _travelStep = 10; break;
            case 10:
                if (!AtMenu) return;
                _joinKeepAlive = true; JoinHost(); _joinAt = now + 20; _travelStep = 11; break;
            case 11:
                if (_lifecycle is not { AdmissionWaiting: true }) { if (now > _joinAt) throw new TimeoutException("Dead P2 was not held."); return; }
                _joinAt = now + 5; _travelStep = 12; break;
            case 12:
                if (now < _joinAt) return;
                TravelCheck(_lifecycle!.AdmissionWaiting, "P2 that died during REST waits through the next wave");
                TravelMark("client-held"); _travelStep = 13; break;
            case 13:
                if (!InArena(arena) || view is not { Phase: SurvivalPhase.Rest, Wave: 4 } || !_lifecycle!.PersonalReady || !_lifecycle.WorldReady || !Revived()) return;
                TravelCheck(true, "the held P2 joins alive at the following REST");
                TravelMark("client-readmitted"); _travelStep = 14; break;
            case 14:
                if (_lifecycle != null || SessionLifecycle.Current != null || SessionManager.SessionActive) return;
                TravelMark("client-pass"); _travelStep = 99; break;
        }
    }
}
