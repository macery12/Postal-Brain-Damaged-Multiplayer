using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using Hyperstrange.PBD.PlayerComponents;
using Hyperstrange.PBD.UI.Menus;
using PBDMP.Network;
using UnityEngine;
using Object = UnityEngine.Object;
using HordeState = Hyperstrange.PBD.HordeModeController.HordeModeStateEnum;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    // Diagnostics only: the size of every managed collection owned by the shared world.
    internal IEnumerable<(string Name, int Count)> LedgerCountsForTest() => MultiplayerBehaviour.Collections(this, "world.");
}

// Phase 6 Survival soak/performance fixture (Test-Travel.ps1 -SurvivalSoak).
// 1. Comparison: the same wave-1 workload (native spawning; the fixture kills every live enemy
//    every 1.5 s) offline, with P1 hosting alone, and with P1+P2 (both sampled). Both local
//    processes share one PC; this is not a two-PC network measurement.
// 2. Soak: P1+P2 play many short native waves (pooled respawns, offers on alternate waves),
//    recording every managed ledger, memory, frame stalls and traffic at each REST.
// 3. Bursts: one-per-frame kills (bounded Survival frames, exact P2 totals) and a flood of
//    duplicate P2 offer/ready requests (one readiness, one wave start).
// 4. Bounds: a very high score, kill count and a two-hour timer reach P2 and the results and
//    records of both PCs exactly; the best weapon comes from native hit statistics.
// Labels: start by contact; enemies are killed with native Npc.Hit carrying P1's weapon; waves
// end by a shortened native timer; players are kept alive by restoring native hit points;
// deaths use native HandleKill; readiness and the request flood use the fixture API; the high
// score/kills/timer are written into P1's native controller.
public sealed partial class MultiplayerBehaviour
{
    private const string SoakWorkload = "Survival wave 1, native spawning; fixture kills every live enemy each 1.5 s";
    private const int SoakOfferBurstWave = 5, SoakKillBurstWave = 7, SoakFlood = 40;
    private const float SoakBigTime = 7200;
    private static readonly float SoakBigScore = 987654321024f;
    private const int SoakBigKills = 123_456_789;
    private HordeModeController? _soakController;
    private float _soakAt, _soakChurnAt, _soakStateAt, _soakCombatAt;
    private long _soakFrameAt, _soakBinAt;
    private int _soakWaves, _soakLastRest, _soakBase, _soakBase2, _soakBurstKills, _soakBurstFrames;
    private float _soakBurstStarted;
    private bool _soakKeepAlive = true, _soakContinued, _soakSampling, _soakBurstChecked;
    private double _soakBinMax;
    private int _soakBinFrames, _soakBin100, _soakBin250;
    private ProtocolTraffic.Snapshot _soakTraffic;
    private readonly List<(int Wave, Dictionary<string, double> Values)> _soakRows = new();
    private List<Npc> _soakBurst = new();
    private static bool SurvivalSoakEnabled => Environment.GetEnvironmentVariable("PBDMP_SURVIVAL_SOAK_TEST") == "1";

    internal static IEnumerable<(string Name, int Count)> Collections(object? owner, string prefix)
    {
        if (owner == null) yield break;
        foreach (var field in owner.GetType().GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).OrderBy(f => f.Name))
        {
            var value = field.GetValue(field.IsStatic ? null : owner);
            if (value == null || value.GetType().Namespace?.StartsWith("System.Collections", StringComparison.Ordinal) != true && !value.GetType().Name.StartsWith("NativeOriginals", StringComparison.Ordinal)) continue;
            if (value.GetType().GetProperty("Count")?.GetValue(value) is int count) yield return (prefix + field.Name, count);
        }
    }

    // Native labels format with IL2CPP's runtime; compare P1's and P2's longest digit runs.
    private static string ScoreDigits(string? label) =>
        System.Text.RegularExpressions.Regex.Matches(label ?? "", "[0-9]+").Select(m => m.Value).OrderByDescending(v => v.Length).FirstOrDefault() ?? "";
    private HordeModeController? SoakController() => _soakController != null ? _soakController : _soakController = Controller();
    private static void SoakHit(Npc npc) => npc.Hit(new HitData
    {
        dealer = Player.Instance.Cast<IAiActor>(), originEntity = Player.Instance.CurrentWeapon?.WeaponId, damage = 100000, weakSpotMultiplier = 1,
        position = npc.CenterOfMass, direction = Vector3.forward, collider = npc.GetComponentInChildren<Collider>()
    });
    private void Churn(HordeModeController c, float now)
    {
        if (now < _soakChurnAt) return;
        _soakChurnAt = now + 1.5f;
        foreach (var npc in Spawned(c)) SoakKill(npc);
    }
    private float _soakCleanupAt;
    // Some enemies (Illuminati) ignore the synthetic hit; native Kill still runs the native death.
    private static void SoakKill(Npc npc) { SoakHit(npc); if (npc.IsAlive) npc.Kill(true); }
    private string StuckEnemies(HordeModeController c)
    {
        var rows = new List<string>();
        for (var i = 0; i < c._spawnedNpcs.Count; i++)
        {
            var npc = c._spawnedNpcs[i];
            rows.Add(npc == null ? "destroyed" : $"{npc.name} alive={npc.IsAlive} active={npc.gameObject.activeInHierarchy} hp={npc.HitPoints.Current}/{npc.HitPoints.Max} pos={npc.Transform.position} id={_world?.ActorIdForTest(npc)} target={npc.Target?.Pointer.ToString() ?? "none"}");
        }
        return string.Join("; ", rows);
    }
    private static void SoakEndWave(HordeModeController c)
    {
        foreach (var npc in Spawned(c)) SoakKill(npc);
        if (c._hordeModeState == HordeState.COMBAT) c._combatTimeLeft = Mathf.Min(c._combatTimeLeft, 0.5f); // synthetic end of the wave
    }
    private bool OfflineInArena(LevelId arena)
    {
        var loading = Object.FindObjectOfType<LoadingMenu>();
        if (!_soakContinued && loading != null && loading.gameObject.activeInHierarchy && loading._sceneLoaded) { _soakContinued = true; loading.OnAnyButtonDown(); }
        if (DoorSoloGate && LevelController._instance != null) { DoorSoloGate = false; LevelController._instance.OnAnyButtonDown(); }
        if (Player.Instance?.GetComponent<PlayerTutorialComponent>() is { _isTutorialShown: true } tutorial) tutorial.OnUseDown();
        return LevelController._instance?.LevelId?.ID == arena.ID && Player.Instance is { IsAlive: true } p && p.gameObject.activeInHierarchy && Time.timeScale > 0 && Controller() != null;
    }
    private void SoakPrepare(int wave)
    {
        // Alternate the two native preparation choices: an offer on even waves, Ready on odd ones.
        var offers = OfferCopies();
        if (wave % 2 == 0 && offers.Count > 0) offers[0].Interact(Player.Instance); else _world!.RequestSurvivalReady();
    }

    // Frame stalls between rows, measured on the main thread outside loading and holds.
    private void SoakFrame()
    {
        var now = Stopwatch.GetTimestamp();
        if (_soakSampling && _soakFrameAt != 0 && _lifecycle is { NativeReady: true, Frozen: false })
        {
            var ms = (now - _soakFrameAt) * 1000d / Stopwatch.Frequency;
            _soakBinFrames++; _soakBinMax = Math.Max(_soakBinMax, ms);
            if (ms > 100) _soakBin100++;
            if (ms > 250) _soakBin250++;
        }
        _soakFrameAt = now;
    }
    private void SoakRow(int wave, int spawns)
    {
        var values = new Dictionary<string, double>();
        var seconds = (Stopwatch.GetTimestamp() - _soakBinAt) / (double)Stopwatch.Frequency;
        var traffic = ProtocolTraffic.Read();
        values["spawns"] = spawns;
        values["private-mib"] = Process.GetCurrentProcess().PrivateMemorySize64 / 1048576d;
        values["managed-mib"] = GC.GetTotalMemory(false) / 1048576d;
        values["gen2"] = GC.CollectionCount(2);
        values["scene.npc-objects"] = Object.FindObjectsOfType<Npc>(true).Length;
        values["scene.pickups"] = Object.FindObjectsOfType<Pickup>().Length;
        values["retired-replicas"] = _world?.RetiredReplicasForTest ?? 0;
        values["frames"] = _soakBinFrames; values["frame-max-ms"] = _soakBinMax; values["over-100-ms"] = _soakBin100; values["over-250-ms"] = _soakBin250;
        values["tx-kib-per-second"] = (traffic.SentBytes - _soakTraffic.SentBytes) / 1024d / seconds;
        values["rx-kib-per-second"] = (traffic.ReceivedBytes - _soakTraffic.ReceivedBytes) / 1024d / seconds;
        values["survival-frames"] = _world?.SurvivalFramesSent ?? 0;
        foreach (var (name, count) in _world!.LedgerCountsForTest().Concat(Collections(_lifecycle, "life.")).Concat(Collections(_saves, "saves.")).Concat(Collections(_session, "session.")))
            values[name] = count;
        _soakRows.Add((wave, values));
        _soakTraffic = traffic; _soakBinAt = Stopwatch.GetTimestamp(); _soakBinFrames = 0; _soakBinMax = 0; _soakBin100 = 0; _soakBin250 = 0;
        var names = _soakRows[0].Values.Keys.ToArray();
        File.WriteAllLines(Marker(TravelRole + "-soak.tsv"), new[] { "wave\t" + string.Join("\t", names) }
            .Concat(_soakRows.Select(r => r.Wave + "\t" + string.Join("\t", names.Select(n => r.Values.GetValueOrDefault(n).ToString("0.###", CultureInfo.InvariantCulture))))));
    }

    // Growth must follow live/relevant state, not the number of waves or pooled respawns.
    private void SoakBoundedChecks(string who)
    {
        var early = _soakRows.First(r => r.Wave >= 4); var last = _soakRows[^1];
        var spawns = last.Values["spawns"] - early.Values["spawns"];
        TravelCheck(spawns >= 60, $"{who} soak respawned {spawns} pooled enemies between REST {early.Wave} and REST {last.Wave}");
        // Higher waves legitimately keep more enemies and uncollected native drops alive: scale
        // the early value by the growth of the native pool or pickups. A per-respawn leak still
        // grows with the number of respawns and fails (allowance: one entry per four respawns;
        // per-pooled-object caches such as actor sounds vary by enemy type).
        var scale = Math.Max(1, Math.Max(last.Values["scene.npc-objects"] / Math.Max(1, early.Values["scene.npc-objects"]),
            last.Values["scene.pickups"] / Math.Max(1, early.Values["scene.pickups"])));
        var grown = last.Values.Where(p => p.Key.Contains('.') && !p.Key.StartsWith("scene.", StringComparison.Ordinal) &&
                p.Value - early.Values.GetValueOrDefault(p.Key) * scale > 16 + 0.25 * spawns)
            .Select(p => $"{p.Key} {early.Values.GetValueOrDefault(p.Key)}->{p.Value}").ToArray();
        File.WriteAllText(Marker(TravelRole + "-soak-growth.txt"), string.Join("\n", grown));
        TravelCheck(grown.Length == 0, $"{who} managed ledgers stay bounded by live state over {_soakRows.Count} soak rows (native pool/pickup scale {scale:F2})" + (grown.Length > 0 ? ": " + string.Join(", ", grown) : ""));
        var managed = last.Values["managed-mib"] - early.Values["managed-mib"];
        TravelCheck(managed < 64, $"{who} managed heap grows {managed:F1} MiB over the soak (bound 64)");
        var rows = _soakRows.Skip(1).ToArray();
        var max = rows.Max(r => r.Values["frame-max-ms"]); var stalls = rows.Sum(r => r.Values["over-100-ms"]);
        TravelCheck(max < 500 && stalls <= rows.Length, $"{who} has no periodic main-thread freeze (worst {max:F0} ms, {stalls} frames over 100 ms in {rows.Length} waves)");
    }

    private void RunSurvivalSoakTest()
    {
        if (_travelStep == 99) return;
        var now = Time.unscaledTime;
        if (_travelStarted == 0) { _travelStarted = now; QualitySettings.vSyncCount = 0; Application.targetFrameRate = 120; Application.runInBackground = true; }
        SoakFrame();
        if (now >= _soakStateAt)
        {
            _soakStateAt = now + 1;
            File.WriteAllText(Marker(TravelRole + "-state.txt"), $"step={_travelStep}, waiting={_lifecycle?.AdmissionWaiting}, view={_world?.SurvivalView ?? _world?.SurvivalPublished}, rows={_soakRows.Count}");
        }
        if (now - _travelStarted > 880) throw new TimeoutException("Survival soak diagnostic timed out: " + _travelStep);
        if (_lifecycle is { NativeReady: false }) _lifecycle.AutoContinue();
        if (_soakKeepAlive && Player.Instance is { IsAlive: true } living && living.gameObject.activeInHierarchy)
            living.GetComponent<PlayerHitProcessorComponent>().SetCurrentHitPoints(living.HitPoints.Max);
        if (TravelRole == "host") SoakHost(now); else SoakClient(now);
    }

    private void SoakHost(float now)
    {
        var arena = SurvivalArena();
        var c = _travelStep is >= 2 and <= 3 or >= 6 ? SoakController() : null;
        var s = _world?.SurvivalPublished;
        switch (_travelStep)
        {
            case 0:
                if (!_buttonVisibleLogged) return;
                _soakWaves = int.TryParse(Environment.GetEnvironmentVariable("PBDMP_SURVIVAL_SOAK_WAVES"), out var waves) && waves >= 8 ? waves : 15;
                TravelMark("host-ready"); GameManager.LoadLevel(arena, true); _soakAt = now + 120; _travelStep = 1; break;
            case 1: // offline native solo
                if (!OfflineInArena(arena)) { if (now > _soakAt) throw new TimeoutException("Offline arena did not load."); return; }
                Player.Instance.Teleport(StartPickup().transform.position + Vector3.up * 0.1f, Player.Instance.transform.rotation);
                _soakController = null; _soakAt = now + 10; _travelStep = 2; break;
            case 2:
                if (c!._hordeModeState != HordeState.COMBAT) { if (now > _soakAt) throw new TimeoutException("Offline run did not begin."); return; }
                TravelCheck(_lifecycle == null && SessionLifecycle.Current == null, "the comparison's first run is offline native Survival");
                c._combatTimeLeft += 900; PerformanceBaseline.Begin("survival-offline", SoakWorkload); _travelStep = 3; break;
            case 3:
                Churn(c!, now);
                if (!PerformanceBaseline.Complete) return;
                PerformanceBaseline.Export(TravelOutput, TravelRole); GameManager.OpenTitle(); _soakAt = now + 3; _travelStep = 4; break;
            case 4: // P1 hosting alone
                if (!AtMenu || !_view!.News.Visible || now < _soakAt) return;
                _port!.text = Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!; Host(); GameManager.LoadLevel(arena, true); _soakController = null; _soakAt = now + 120; _travelStep = 5; break;
            case 5:
                if (!InArena(arena) || Player.Instance is not { IsAlive: true }) { if (now > _soakAt) throw new TimeoutException("Hosted arena did not load."); return; }
                _soakController = null;
                Player.Instance.Teleport(StartPickup().transform.position + Vector3.up * 0.1f, Player.Instance.transform.rotation);
                _soakAt = now + 10; _travelStep = 6; break;
            case 6:
                if (c!._hordeModeState != HordeState.COMBAT) { if (now > _soakAt) throw new TimeoutException("Hosted solo run did not begin."); return; }
                c._combatTimeLeft += 900; PerformanceBaseline.Begin("survival-host-alone", SoakWorkload); _travelStep = 7; break;
            case 7:
                Churn(c!, now);
                if (!PerformanceBaseline.Complete) return;
                PerformanceBaseline.Export(TravelOutput, TravelRole); TravelMark("host-alone-done"); _travelStep = 8; break;
            case 8: // P2 joins during combat and waits; P1 retries for a clean shared wave 1
                Churn(c!, now);
                if (!Marked("client-waiting")) return;
                TravelCheck(_lifecycle!.AdmissionWaiting && !_lifecycle.Frozen, "P1 holds P2's combat-time join for the comparison run");
                _travelEpoch = _lifecycle.Epoch; _soakController = null; GameManager.Load(null); _soakAt = now + 120; _travelStep = 9; break;
            case 9:
                if (_lifecycle!.Epoch == _travelEpoch || !InArena(arena) || c!._hordeModeState != HordeState.INTRO || _world!.SurvivalParticipantsForTest != SurvivalProtocol.Everyone || !Marked("client-admitted"))
                { if (now > _soakAt) throw new TimeoutException("P2 was not admitted at P1's retry."); return; }
                TravelCheck(!_lifecycle.AdmissionWaiting, "a P2 held during combat is admitted at P1's retry (INTRO)");
                Player.Instance.Teleport(StartPickup().transform.position + Vector3.up * 0.1f, Player.Instance.transform.rotation);
                _soakAt = now + 15; _travelStep = 10; break;
            case 10:
                if (c!._hordeModeState != HordeState.COMBAT) { if (now > _soakAt) throw new TimeoutException("Shared run did not begin."); return; }
                c._combatTimeLeft += 900; TravelMark("connected-run"); PerformanceBaseline.Begin("survival-connected", SoakWorkload); _travelStep = 11; break;
            case 11:
                Churn(c!, now);
                if (!PerformanceBaseline.Complete) return;
                PerformanceBaseline.Export(TravelOutput, TravelRole); _travelStep = 12; break;
            case 12: // multiwave soak
                Churn(c!, now);
                if (!Marked("client-connected-done")) return;
                _soakTraffic = ProtocolTraffic.Read(); _soakBinAt = Stopwatch.GetTimestamp(); _soakSampling = true;
                SoakRow(1, SurvivalPatches.Registrations); _soakCombatAt = now; _travelStep = 13; break;
            case 13:
                if (c!._hordeModeState == HordeState.COMBAT)
                {
                    if (!c._combatCountDownFinished) return;
                    if (_soakCombatAt == 0) _soakCombatAt = now;
                    if (c._currentWave == SoakKillBurstWave && !_soakBurstChecked) { _soakBurstChecked = true; _soakAt = now + 10; _travelStep = 30; return; }
                    if (now < _soakCombatAt + 8) { Churn(c, now); return; }
                    SoakEndWave(c); return;
                }
                if (c._hordeModeState == HordeState.CLEANUP)
                {
                    if (_soakCleanupAt == 0) _soakCleanupAt = now;
                    SoakEndWave(c);
                    if (now > _soakCleanupAt + 15) throw new InvalidOperationException($"Wave {c._currentWave} CLEANUP stuck with registered enemies: {StuckEnemies(c)}");
                    return;
                }
                _soakCleanupAt = 0;
                if (c._hordeModeState != HordeState.REST || c._currentWave == _soakLastRest || OfferCopies().Count == 0 || s is not { Phase: SurvivalPhase.Rest, Offers.Length: > 0 }) return;
                _soakLastRest = c._currentWave;
                SoakRow(c._currentWave, SurvivalPatches.Registrations);
                File.WriteAllText(Marker($"host-rest-{c._currentWave}"), SurvivalPatches.Registrations.ToString(CultureInfo.InvariantCulture));
                if (c._currentWave == SoakOfferBurstWave) { _soakBase = _world!.SurvivalWaveStarts; _soakBase2 = _world.SurvivalRequestsRejected; _soakAt = now + 20; _travelStep = 40; return; }
                if (c._currentWave > _soakWaves) { SoakBoundedChecks("P1"); _world!.RequestSurvivalReady(); _soakAt = now + 15; _travelStep = 20; return; }
                SoakPrepare(c._currentWave); _soakAt = now + 15; _travelStep = 14; break;
            case 14:
                if (c!._hordeModeState != HordeState.COMBAT) { if (now > _soakAt) throw new TimeoutException("Soak wave did not start: " + c._currentWave); return; }
                _soakCombatAt = 0; _travelStep = 13; break;
            case 30: // kill burst: let native spawns accumulate, then one kill per frame
                if (Spawned(c!).Count < 6 && now < _soakAt) return;
                _soakBurst = Spawned(c!); _soakBase = _world!.SurvivalFramesSent; _soakBase2 = c!.Kills; _soakBurstFrames = 0; _soakBurstStarted = now; _travelStep = 31; break;
            case 31:
                var next = _soakBurst.FirstOrDefault(n => n != null && n.IsAlive);
                if (next != null) { SoakKill(next); _soakBurstFrames++; if (_soakBurstFrames < 200) return; }
                _soakAt = now + 1; _travelStep = 32; break;
            case 32:
                if (now < _soakAt) return;
                var sent = _world!.SurvivalFramesSent - _soakBase; var duration = now - _soakBurstStarted;
                _soakBurstKills = c!.Kills - _soakBase2;
                TravelCheck(_soakBurstKills == _soakBurst.Count && _soakBurstKills >= 3 && sent <= Mathf.CeilToInt(duration / 0.1f) + 2,
                    $"a burst of {_soakBurstKills} kills over {_soakBurstFrames} frames sends {sent} Survival frames in {duration:F2} s (bounded by the 0.1 s sweep)");
                File.WriteAllText(Marker("host-burst.txt"), FormattableString.Invariant($"{c.Kills}|{(double)c._currentScore:R}"));
                TravelMark("host-burst"); _soakAt = now + 10; _travelStep = 33; break;
            case 33:
                if (!Marked("client-burst")) { if (now > _soakAt) throw new TimeoutException("P2 did not confirm the kill burst."); return; }
                _soakCombatAt = now; _travelStep = 13; break;
            case 40: // offer/ready flood from P2: one readiness, no wave start before P1
                if (!Marked("client-offer-burst")) { if (now > _soakAt) throw new TimeoutException("P2 did not send its request flood."); return; }
                _soakAt = now + 1.5f; _travelStep = 42; break; // allow the flood to arrive
            case 42:
                if (now < _soakAt) return;
                TravelCheck(_world!.SurvivalWaveStarts == _soakBase && c!._hordeModeState == HordeState.REST && s is { } flood && (flood.Ready & SurvivalProtocol.Partner) != 0 && _world.SurvivalRequestsRejected == _soakBase2,
                    $"{2 * SoakFlood} duplicate P2 offer/ready requests mark P2 ready once and start nothing before P1");
                TravelCheck(_session!.Snapshot.Connected, "the request flood keeps the session connected");
                SoakPrepare(c!._currentWave); _soakAt = now + 15; _travelStep = 41; break;
            case 41:
                if (c!._hordeModeState != HordeState.COMBAT) { if (now > _soakAt) throw new TimeoutException("Wave did not start after the flood."); return; }
                TravelCheck(_world!.SurvivalWaveStarts == _soakBase + 1, "the flooded preparation starts its wave exactly once");
                _soakCombatAt = 0; _travelStep = 13; break;
            case 20: // high score, kill count and a long timer
                if (c!._hordeModeState != HordeState.COMBAT || !c._combatCountDownFinished) { if (now > _soakAt) throw new TimeoutException("Bounds wave did not start."); return; }
                c._usedSpawnPower = 1e9f; KillAll(c); // synthetic: no further kills change the values
                c._currentScore = SoakBigScore; c.Kills = SoakBigKills; c._combatTimeLeft = SoakBigTime;
                // The native score event would follow a real kill; raise P1's own label update once.
                _world!.SurvivalHudForTest?.OnScoreChanged(c._currentScore);
                File.WriteAllText(Marker("host-bounds.txt"), FormattableString.Invariant($"{(double)SoakBigScore:R}|{SoakBigKills}|{ScoreDigits(_world.SurvivalHudForTest?._scoreLabel.text)}"));
                TravelMark("host-bounds"); _soakAt = now + 20; _travelStep = 21; break;
            case 21:
                if (!Marked("client-bounds")) { if (now > _soakAt) throw new TimeoutException("P2 did not receive the bounds."); return; }
                TravelCheck(c!._combatTimeLeft > SoakBigTime - 60 && s is { } big && big.Score == SoakBigScore && big.Kills == SoakBigKills, "P1 publishes the high score, kill count and two-hour timer exactly");
                TravelMark("host-wipe"); _travelStep = 22; break;
            case 22:
                if (!Marked("client-dead")) return;
                _soakKeepAlive = false; Die(); _soakAt = now + 10; _travelStep = 23; break;
            case 23:
                if (s?.Ended != true || ResultsMenu() == null) { if (now > _soakAt) throw new TimeoutException("P1 results did not open."); return; }
                var record = LastRecord();
                TravelCheck(record != null && record.Score == SoakBigScore && record.Kills == SoakBigKills && record.RunId == s.RunId, "P1's local record keeps the high score and kill count exactly");
                TravelCheck(s.ResultWeapon.Length > 0 && s.ResultWeapon == c!.BestWeapon?.ID, "P1's result carries its native best weapon");
                File.WriteAllText(Marker("host-record.txt"), ResultSummary(record!));
                TravelMark("host-results"); _travelStep = 24; break;
            case 24:
                if (!Marked("client-results")) return;
                ResetSession(); TravelMark("host-pass"); _travelStep = 99; break;
        }
    }

    private void SoakClient(float now)
    {
        var arena = SurvivalArena();
        var view = _world?.SurvivalView;
        var c = _lifecycle is { InSurvival: true } ? SoakController() : null;
        switch (_travelStep)
        {
            case 0: // idle at the menu during P1's offline and solo-host comparisons
                if (!_buttonVisibleLogged || !Marked("host-alone-done")) return;
                JoinHost(); _soakAt = now + 30; _travelStep = 1; break;
            case 1:
                if (_lifecycle is not { AdmissionWaiting: true }) { if (now > _soakAt) throw new TimeoutException("P2 was not held for the next wave."); return; }
                TravelMark("client-waiting"); _travelStep = 2; break;
            case 2:
                if (!InArena(arena) || view is not { Phase: SurvivalPhase.Intro } || !_lifecycle!.PersonalReady || !_lifecycle.WorldReady) return;
                _world!.RequestSurvivalReady(); TravelMark("client-admitted"); _travelStep = 3; break;
            case 3:
                if (!Marked("connected-run") || view is not { Phase: SurvivalPhase.Combat }) return;
                PerformanceBaseline.Begin("survival-connected", SoakWorkload); _travelStep = 4; break;
            case 4:
                if (!PerformanceBaseline.Complete) return;
                PerformanceBaseline.Export(TravelOutput, TravelRole);
                _soakWaves = int.TryParse(Environment.GetEnvironmentVariable("PBDMP_SURVIVAL_SOAK_WAVES"), out var waves) && waves >= 8 ? waves : 15;
                _soakTraffic = ProtocolTraffic.Read(); _soakBinAt = Stopwatch.GetTimestamp(); _soakSampling = true;
                SoakRow(1, 0); TravelMark("client-connected-done"); _travelStep = 5; break;
            case 5: // soak: follow each REST, alternate offer and Ready, flood once
                if (Marked("host-burst") && !_soakBurstChecked)
                {
                    var burst = File.ReadAllText(Marker("host-burst.txt")).Split('|');
                    if (view == null || view.Kills != int.Parse(burst[0], CultureInfo.InvariantCulture) || view.Score != double.Parse(burst[1], CultureInfo.InvariantCulture)) return;
                    _soakBurstChecked = true;
                    TravelCheck(true, "after the kill burst P2's view has P1's exact kills and score");
                    TravelMark("client-burst");
                }
                if (view is not { Phase: SurvivalPhase.Rest } || view.Wave == _soakLastRest || OfferCopies().Count == 0 || OfferCopies().Count < SurvivalProtocol.ParseOffers(view.Offers).Length) return;
                if (!Marked($"host-rest-{view.Wave}")) return;
                _soakLastRest = view.Wave;
                SoakRow(view.Wave, int.Parse(File.ReadAllText(Marker($"host-rest-{view.Wave}")), CultureInfo.InvariantCulture));
                if (view.Wave == SoakOfferBurstWave)
                {
                    var token = SurvivalProtocol.ParseOffers(view.Offers)[0].Token;
                    for (var i = 0; i < SoakFlood; i++)
                    {
                        _world!.SendSurvivalRequestForTest(new(view.Run, view.Wave, SurvivalPhase.Rest, SurvivalRequestKind.Offer, token));
                        _world.SendSurvivalRequestForTest(new(view.Run, view.Wave, SurvivalPhase.Rest, SurvivalRequestKind.Ready));
                    }
                    TravelMark("client-offer-burst"); return;
                }
                if (view.Wave > _soakWaves) { SoakBoundedChecks("P2"); _world!.RequestSurvivalReady(); _soakAt = now + 30; _travelStep = 6; return; }
                SoakPrepare(view.Wave); break;
            case 6: // high values reach P2's view, HUD and controller
                if (!Marked("host-bounds")) return;
                var bounds = File.ReadAllText(Marker("host-bounds.txt")).Split('|');
                var score = double.Parse(bounds[0], CultureInfo.InvariantCulture);
                var hud = _world!.SurvivalHudForTest;
                if (view is not { Phase: SurvivalPhase.Combat } || view.Score != score || hud == null || bounds[2].Length < 9 || ScoreDigits(hud._scoreLabel.text) != bounds[2])
                { if (now > _soakAt) throw new TimeoutException("P2 did not receive the bounds."); return; }
                TravelCheck(view.Kills == int.Parse(bounds[1], CultureInfo.InvariantCulture) && c!._currentScore == (float)score, "P2 mirrors the high score and kill count exactly, including its native score label");
                TravelCheck(view.TimeLeft > SoakBigTime - 60 && c!._combatTimeLeft > SoakBigTime - 60 && c._combatTimeLeft <= SoakBigTime, "P2 presents the two-hour wave timer");
                TravelMark("client-bounds"); _travelStep = 7; break;
            case 7:
                if (!Marked("host-wipe")) return;
                _soakKeepAlive = false; Die(); TravelMark("client-dead"); _soakAt = now + 20; _travelStep = 8; break;
            case 8:
                if (view is not { Ended: true } || ResultsMenu() == null || !Marked("host-results")) { if (now > _soakAt) throw new TimeoutException("P2 results did not open."); return; }
                TravelCheck(c!._currentScore == (float)view.Score && c.Kills == view.Kills, "P2's native results show the high team score and kills");
                TravelCheck(view.ResultWeapon.Length > 0 && c.BestWeapon?.ID == view.ResultWeapon, "P2's native results show P1's best weapon");
                TravelCheck(LastRecord() is { } record && ResultSummary(record) == File.ReadAllText(Marker("host-record.txt")), "P2 keeps an identical local record of the high result");
                TravelMark("client-results"); _travelStep = 9; break;
            case 9:
                if (_lifecycle != null || SessionLifecycle.Current != null || SessionManager.SessionActive) return;
                TravelMark("client-pass"); _travelStep = 99; break;
        }
    }
}
