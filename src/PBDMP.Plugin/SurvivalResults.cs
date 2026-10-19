using BepInEx;
using Hyperstrange.Base;
using Hyperstrange.PBD;
using Hyperstrange.PBD.PlayerView;
using Hyperstrange.PBD.UI.Menus;
using PBDMP.Network;
using UnityEngine;
using Object = UnityEngine.Object;
using HordeState = Hyperstrange.PBD.HordeModeController.HordeModeStateEnum;

namespace PBDMP.Plugin;

// Survival presentation and results: P2's native HUD driven from P1's state, P2 damage for the
// shared combo, the shared final result on both native results screens, local co-op records,
// and the preparation-boundary admission rule for joining/rejoining players.
internal sealed partial class SharedWorld
{
    private string _runId = "", _resultWeapon = "", _recordedRun = "";
    private int _resultTime;
    private PlayerHordeModeView? _hudView;
    private SurvivalState? _hudShown;
    private int _hudSecond = int.MinValue;
    private float _bestLabelAt;
    private string _bestLabel = "";
    private EndHordeMenu? _results;
    // Partner identity -> run/wave in which P1 saw it dead; a rejoin cannot revive it early.
    private readonly Dictionary<string, (uint Run, int Wave)> _deadPartners = new();
    internal int SurvivalPartnerDamage { get; private set; }
    internal (float Before, float After) SurvivalPartnerPenaltyForTest { get; private set; }
    internal RecordOutcome? SurvivalRecordForTest { get; private set; }
    internal string SurvivalBestLabelForTest => _hudView != null ? _hudView._currentBestLabel.text : "";
    internal PlayerHordeModeView? SurvivalHudForTest => _hudView;
    internal int DeadPartnersForTest => _deadPartners.Count;
    internal byte SurvivalParticipantsForTest => Participants;

    // Local co-op Survival records; diagnostics keep their own copy beside the run output.
    internal static string SurvivalRecordsPath =>
        Environment.GetEnvironmentVariable("PBDMP_TRAVEL_TEST") is { Length: > 0 } role && Environment.GetEnvironmentVariable("PBDMP_TRAVEL_OUTPUT") is { Length: > 0 } output
            ? Path.Combine(output, role + "-survival-records.json") : Path.Combine(Paths.BepInExRootPath, "PBDMP", "survival-records.json");

    private void ClearSurvivalPresentation(bool keepRun)
    {
        _hudView = null; _hudShown = null; _hudSecond = int.MinValue; _results = null; _bestLabel = ""; _bestLabelAt = 0;
        if (keepRun) return;
        _runId = ""; _resultWeapon = ""; _resultTime = 0;
    }

    // Host: the native end has gathered the level time; the best weapon comes from native damage stats.
    private void CaptureResult(HordeModeController c)
    {
        _resultTime = Math.Clamp(LevelController._instance != null ? LevelController._instance.TimeStat.CompletionTime : 0, 0, SurvivalProtocol.MaxResultTime);
        var weapon = c.BestWeapon?.ID ?? "";
        _resultWeapon = weapon.Length <= SurvivalProtocol.MaxAssetId && weapon.All(ch => ch is >= '!' and <= '~' && ch != ',') ? weapon : "";
    }

    // P2: present P1's exact final values through the native results screen.
    private static void ApplyFinalResult(HordeModeController c, SurvivalState v)
    {
        c._currentScore = (float)Math.Min(v.Score, float.MaxValue); c._currentWave = v.Wave;
        c.Kills = v.Kills; c.WavesSurvived = v.WavesSurvived; c.DamageDealt = (float)Math.Min(v.Damage, float.MaxValue);
        var stats = new Il2CppSystem.Collections.Generic.Dictionary<WeaponId, float>();
        if (v.ResultWeapon.Length > 0 && EntityManager.Get<EntityId>(v.ResultWeapon)?.TryCast<WeaponId>() is { } weapon) stats[weapon] = Mathf.Max(1, c.DamageDealt);
        c._damageStats = stats;
    }

    // Both PCs keep their own copy of the same team result, once per run. Native results
    // compare with solo metadata (whose writes stay blocked in co-op); show the co-op record.
    private void FinalizeRecord(HordeModeController c, string runId, double score, int kills, int waves, double damage, string weapon, int time)
    {
        if (runId.Length == 0 || _recordedRun == runId) return;
        _recordedRun = runId;
        try
        {
            var store = new SurvivalRecordStore(SurvivalRecordsPath);
            var outcome = store.Record(new(runId, LevelManager.CurrentLevel.ID, SessionLifecycle.Difficulty(), score, kills, waves, damage, weapon, time, DateTime.UtcNow));
            if (store.RecoveredCorruption) _life.Explain("Co-op Survival records were unreadable and were set aside.");
            SurvivalRecordForTest = outcome;
            c.IsScoreNewBest = outcome.NewBest; c.OldScoreToDisplay = (float)Math.Min(outcome.PreviousBest?.Score ?? 0, float.MaxValue);
            _bestLabel = ""; _bestLabelAt = 0;
            Plugin.Logger.LogInfo($"Co-op Survival result recorded: run {runId}, score {score}, new best {outcome.NewBest}.");
            if (outcome.NewBest) _life.Explain("New co-op Survival best.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or ArgumentException)
        {
            Plugin.Logger.LogWarning("Co-op Survival record was not saved: " + e.Message);
            _life.Explain("Co-op Survival record could not be saved.");
        }
    }

    // The HUD's current-best line shows the local co-op best for this arena and difficulty.
    private void TickBestLabel(PlayerHordeModeView view)
    {
        if (Time.unscaledTime < _bestLabelAt) return;
        _bestLabelAt = Time.unscaledTime + 1;
        if (_bestLabel.Length == 0)
        {
            double best = 0;
            try { best = new SurvivalRecordStore(SurvivalRecordsPath).Best(LevelManager.CurrentLevel.ID, SessionLifecycle.Difficulty())?.Score ?? 0; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Plugin.Logger.LogWarning("Co-op Survival records unavailable: " + e.Message); }
            _bestLabel = (string)view._currentBestString + ": " + ((float)Math.Min(best, float.MaxValue)).ToString("0");
        }
        // Native OnEnable rewrites the line from solo metadata; keep the co-op value.
        if (view._currentBestLabel != null && view._currentBestLabel.text != _bestLabel) view._currentBestLabel.text = _bestLabel;
    }

    // P2's controller raises no events; call its native view handlers (labels, notices and
    // sounds only) from P1's state. The timer counts down locally between P1's frames.
    private void TickSurvivalHud()
    {
        if (!Active || !_life.InSurvival) return;
        if (_hudView == null) { _hudView = Object.FindObjectOfType<PlayerHordeModeView>(); _hudShown = null; if (_hudView == null) return; }
        var view = _hudView;
        TickBestLabel(view);
        if (!Client || _survivalView is not { } v || Horde() is not { } c || _clientEnded) return;
        var shown = _hudShown; var newRun = shown == null || shown.Run != v.Run;
        if (newRun || shown!.Phase != v.Phase) view.OnStateChanged((HordeState)(int)v.Phase);
        if (newRun || shown!.Wave != v.Wave) view.OnCurrentWaveChanged(v.Wave);
        if (newRun || shown!.Score != v.Score || shown.Combo != v.Combo) view.OnScoreChanged(c._currentScore);
        if (newRun || shown!.Alive != v.Alive) view.OnSpawnedNpcCountChanged(v.Alive);
        if (v.Phase == SurvivalPhase.Combat && !v.CountdownDone && v.Countdown > 0 && (newRun || shown!.Phase != v.Phase || shown.Countdown != v.Countdown))
            view.OnCombatCountDownTick(v.Countdown);
        if (v.Phase == SurvivalPhase.Combat && v.CountdownDone && !newRun && shown is { Phase: SurvivalPhase.Combat, CountdownDone: false }) view.OnCombatCountDownFinish();
        if (v.Phase == SurvivalPhase.Combat && v.CountdownDone)
            c._combatTimeLeft = Mathf.Max(v.TimeLeft - SurvivalTimerCadence - 0.25f, c._combatTimeLeft - Time.deltaTime);
        var second = Mathf.CeilToInt(c._combatTimeLeft);
        if (second != _hudSecond || newRun || shown!.Wave != v.Wave) { _hudSecond = second; view.OnCombatTimeLeftChanged(c._combatTimeLeft); }
        _hudShown = v;
    }

    // P1 alone controls retry and leaving; P2's native results buttons stay disabled.
    private void HoldClientResults()
    {
        if (_results == null) _results = Object.FindObjectOfType<EndHordeMenu>();
        if (_results == null || !_results.gameObject.activeInHierarchy) return;
        foreach (var button in new[] { _results._resetLevelButton, _results._exitToMenuButton })
            if (button != null && button.interactable) { button.SetDisabled(); button.interactable = false; }
    }
    internal bool ClientResultsLockedForTest => _results != null && _results._resetLevelButton is { interactable: false } && _results._exitToMenuButton is { interactable: false };

    // P2: one eligible native damage event reduces the shared combo once on P1.
    internal void ReportSurvivalDamage()
    {
        if (!Client || !Active || _survivalView is not { Ended: false, Run: > 0 } v || v.Phase == SurvivalPhase.Intro) return;
        Send(SurvivalProtocol.ToFrame(new SurvivalRequest(v.Run, v.Wave, v.Phase, SurvivalRequestKind.Damage), _epoch));
    }

    private void ApplySurvivalDamage(SurvivalRequest request)
    {
        if (_ended || Horde() is not { } c || !PartnerParticipant || request.Run != _survivalRun || c._hordeModeState == HordeState.INTRO) { SurvivalRequestsRejected++; return; }
        SurvivalPartnerDamage++;
        var before = c._comboMultiplier;
        c.OnPlayerReceivedDamage(new HitData());
        SurvivalPartnerPenaltyForTest = (before, c._comboMultiplier);
    }

    // P1 records a dead participant by identity; seeing it alive again clears the entry.
    private void TrackDeadPartner(HordeModeController c)
    {
        if (!PartnerParticipant || CoopSaveService.Current?.PeerIdentity is not { Length: > 0 } identity) return;
        if (PartnerPose!.Alive) _deadPartners.Remove(identity);
        else if (!_deadPartners.ContainsKey(identity)) _deadPartners[identity] = (_survivalRun, Math.Max(1, c._currentWave));
    }

    // Join/rejoin rule: P1 admits a waiting P2 only at a preparation phase (INTRO/REST). A
    // participant seen dead waits for the first REST after its death. A finished run waits for
    // P1's retry or travel, because the results keep P1 dead.
    internal bool HoldSurvivalAdmission(string identity)
    {
        if (Client || Horde() is not { } c) return false;
        if (_ended || !Preparation(c._hordeModeState)) return true;
        if (identity.Length == 0) return true;
        return _deadPartners.TryGetValue(identity, out var death) && death.Run == _survivalRun && Math.Max(1, c._currentWave) <= death.Wave;
    }
}
