using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using Hyperstrange.PBD.PlayerComponents;
using PBDMP.Network;
using UnityEngine;
using Object = UnityEngine.Object;
using HordeState = Hyperstrange.PBD.HordeModeController.HordeModeStateEnum;

namespace PBDMP.Plugin;

// Phase 2 Survival authority fixture (Test-Travel.ps1 -SurvivalWaves). P1 starts a native run;
// both players fight and are targeted; P2 parks on a native spawn; host pause and host death;
// wave advance once; native spawns of flying/summoning enemies; pooled wave 2 and cleanup.
// Labels: start/offer use native pickup contact/Interact; enemies are removed with native
// Npc.Hit/Kill to bound the run; players are kept alive by restoring native hit points except
// in the host-death step; forced special enemies use the controller's own SpawnNextNpc.
public sealed partial class MultiplayerBehaviour
{
    private float _wavesAt, _wavesWindow, _wavesSpawnAt;
    private int _wavesKills, _wavesOrigin, _wavesRejected;
    private float _wavesTime;
    private readonly HashSet<IntPtr> _wavesSeen = new();
    private bool _wavesTargetedPartner, _wavesKeepAlive = true;
    private readonly HashSet<int> _wavesCountdown = new();
    private int _wavesSummons;
    private static bool SurvivalWavesEnabled => Environment.GetEnvironmentVariable("PBDMP_SURVIVAL_WAVES_TEST") == "1";

    private static HordeModeController Controller() => Object.FindObjectOfType<HordeModeController>();

    private void KeepAlive()
    {
        if (!_wavesKeepAlive || Player.Instance is not { IsAlive: true } player) return;
        player.GetComponent<PlayerHitProcessorComponent>().SetCurrentHitPoints(player.HitPoints.Max);
    }

    private static List<Npc> Spawned(HordeModeController c)
    {
        var result = new List<Npc>();
        for (var i = 0; i < c._spawnedNpcs.Count; i++) if (c._spawnedNpcs[i] is { IsAlive: true } npc) result.Add(npc);
        return result;
    }

    private static void KillAll(HordeModeController c) { foreach (var npc in Spawned(c)) npc.Kill(true); }

    private static int ReplicaAlive() => Object.FindObjectsOfType<Npc>().Count(n => n.IsAlive && n.gameObject.activeInHierarchy && n.NpcId != null);

    private static Pickup StartPickup() => Object.FindObjectsOfType<InteractListener>().Select(l => l._interactable?.TryCast<Pickup>()).First(p => p != null && p.name.Contains("Shotgun"))!;

    private void RunSurvivalWavesTest()
    {
        if (_travelStep == 99) return;
        if (_travelStarted == 0) _travelStarted = Time.unscaledTime;
        var view = _world?.SurvivalView ?? _world?.SurvivalPublished;
        File.WriteAllText(Marker(TravelRole + "-state.txt"), $"step={_travelStep}, ready={_lifecycle?.NativeReady}, released={_lifecycle?.State.Released}, view={view}");
        if (Time.unscaledTime - _travelStarted > 330) throw new TimeoutException("Survival wave diagnostic timed out: " + _travelStep);
        if (_lifecycle is { NativeReady: false }) _lifecycle.AutoContinue();
        if (_lifecycle is { NativeReady: true } && Player.Instance != null) KeepAlive();
        if (TravelRole == "host") WavesHost(); else WavesClient();
    }

    private void WavesHost()
    {
        var arena = SurvivalArena();
        var c = _lifecycle is { InSurvival: true } ? Controller() : null;
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
                if (!InArena(arena) || !Marked("client-arena")) return;
                TravelCheck(c!._hordeModeState == HordeState.INTRO && _world!.SurvivalPublished is { Run: 0, Phase: SurvivalPhase.Intro }, "P1 publishes the INTRO run state");
                var start = StartPickup();
                Player.Instance.Teleport(start.transform.position + Vector3.up * 0.1f, Player.Instance.transform.rotation); _wavesAt = now + 8; _travelStep = 3; break;
            case 3:
                if (c!._hordeModeState != HordeState.COMBAT || _world!.SurvivalPublished?.Run != 1) { if (now > _wavesAt) throw new TimeoutException("P1 start pickup did not begin run 1."); return; }
                TravelCheck(true, "native Begin starts and publishes run 1 on P1");
                c._combatTimeLeft += 60; // synthetic: lengthen wave 1 so every observation fits inside one wave
                TravelMark("host-run-started"); _travelStep = 4; break;
            case 4: // P1 fights: native local hit kills one enemy
                if (!Marked("client-combat")) return;
                var target = Spawned(c!).FirstOrDefault();
                if (target == null) return;
                _wavesKills = c!.Kills;
                target.Hit(new HitData { dealer = Player.Instance.Cast<IAiActor>(), damage = 100000, weakSpotMultiplier = 1, position = target.CenterOfMass, direction = Vector3.forward, collider = target.GetComponentInChildren<Collider>() });
                _wavesAt = now + 5; _travelStep = 5; break;
            case 5:
                if (c!.Kills <= _wavesKills) { if (now > _wavesAt) throw new TimeoutException("P1 native hit did not register a Survival kill."); return; }
                TravelCheck(c._currentScore > 0, "P1's native kill scores in the shared run");
                _wavesKills = c.Kills; _wavesOrigin = _world!.AttackOriginCount(""); TravelMark("host-p2-hit"); _wavesAt = now + 15; _travelStep = 6; break;
            case 6: // P2 fights through its replica and the validated hit request
                if (!Marked("client-hit-sent")) { if (now > _wavesAt) throw new TimeoutException("P2 did not send a hit."); return; }
                if (_world!.AttackOriginCount("") <= _wavesOrigin || c!.Kills <= _wavesKills) { if (now > _wavesAt) throw new TimeoutException("P2 hit did not kill a host enemy."); return; }
                TravelCheck(true, "P2's hit request kills P1's native enemy and counts in the shared run");
                _wavesRejected = _world.PartnerSpawnRejections; _wavesSeen.Clear(); _wavesTargetedPartner = false;
                TravelMark("host-park"); _wavesAt = now + 25; _wavesWindow = 0; _travelStep = 7; break;
            case 7: // P2 parks on a native spawn: no spawn within 8 m; partner can be targeted
                if (!Marked("client-on-spawn")) { if (now > _wavesAt) throw new TimeoutException("P2 did not park on a spawn."); return; }
                if (_wavesWindow == 0) _wavesWindow = now + 18;
                var remote = _world!.Remote!;
                foreach (var npc in Spawned(c!))
                {
                    if (_wavesSeen.Add(npc.Pointer))
                    {
                        var distance = Vector3.Distance(npc.Transform.position, remote.FeetPosition);
                        File.AppendAllText(Marker("host-spawns.txt"), $"{npc.NpcId?.name}\tpartner={distance:0.0}\thost={Vector3.Distance(npc.Transform.position, Player.Instance.FeetPosition):0.0}\n");
                        TravelCheck(distance >= 7.5f, "new native spawn keeps 8 m from living P2");
                        _wavesSpawnAt = now;
                    }
                    if (npc.Target != null && npc.Target.Pointer == remote.Pointer) _wavesTargetedPartner = true;
                }
                // Let awareness choose targets for ~1.5 s, then free spawn power for further attempts.
                if (_wavesSpawnAt > 0 && now - _wavesSpawnAt > 1.5f) KillAll(c!);
                if (now < _wavesWindow) return;
                TravelCheck(_world.PartnerSpawnRejections > _wavesRejected, "spawn selection rejected a ready spawn near P2");
                TravelCheck(_wavesTargetedPartner, "native enemies can target P2 when P2 is nearer");
                KillAll(c!); _wavesTime = c!._combatTimeLeft; GameManager.PauseGame(); _wavesAt = now + 2; _travelStep = 8; break;
            case 8: // shared pause freezes the native run
                if (now < _wavesAt) return;
                TravelCheck(_lifecycle!.Frozen && Mathf.Abs(c!._combatTimeLeft - _wavesTime) < 0.01f, "P1 pause freezes the Survival timer");
                TravelMark("host-paused"); _travelStep = 9; break;
            case 9: // host death: run keeps going and enemies target P2
                if (!Marked("client-paused")) return;
                if (_lifecycle!.Frozen) { GameManager.DisableMenu(); return; }
                _wavesKeepAlive = false; _wavesTime = c!._combatTimeLeft;
                Player.Instance.GetComponent<PlayerHitProcessorComponent>().HandleKill(new HitData { position = Player.Instance.CenterOfMass, direction = Vector3.forward, weakSpotMultiplier = 1 });
                _wavesAt = now + 2.5f; _wavesTargetedPartner = false; _travelStep = 10; break;
            case 10:
                foreach (var npc in Spawned(c!)) if (npc.Target != null && npc.Target.Pointer == _world!.Remote!.Pointer) _wavesTargetedPartner = true;
                if (now < _wavesAt) return;
                TravelCheck(!Player.Instance.IsAlive, "P1 is dead for the continuation check");
                TravelCheck(c!._combatTimeLeft < _wavesTime - 1 && c._hordeModeState is HordeState.COMBAT or HordeState.CLEANUP, "P1's dead player does not stop the authoritative run timer");
                TravelCheck(Spawned(c).Count == 0 || _wavesTargetedPartner, "enemies target living P2 while P1 is dead");
                _travelStep = 11; break;
            case 11: // advance to REST exactly once; dead P1 waits for the wave revival
                KillAll(c!);
                if (c!._hordeModeState == HordeState.COMBAT) c._combatTimeLeft = Mathf.Min(c._combatTimeLeft, 0.5f); // synthetic end of the lengthened wave
                if (c._hordeModeState != HordeState.REST || !Player.Instance.IsAlive) return;
                _wavesKeepAlive = true;
                TravelCheck(c._currentWave == 2 && c.WavesSurvived == 1, "wave 1 resolves once into REST for wave 2");
                TravelCheck(_lifecycle!.PlayerState is { Pending: false }, "dead P1 revives at the REST boundary");
                TravelMark("host-rest"); _travelStep = 12; break;
            case 12:
                if (!Marked("client-rest")) return;
                var offer = Object.FindObjectOfType<HordeModeWeaponSpawner>()._spawnedPickups;
                TravelCheck(offer.Count > 0, "P1 native REST offers exist");
                offer[0].Interact(Player.Instance); _wavesAt = now + 8; _travelStep = 13; break;
            case 13:
                if (c!._hordeModeState != HordeState.COMBAT || !c._combatCountDownFinished) { if (now > _wavesAt) throw new TimeoutException("Wave 2 did not start."); return; }
                TravelCheck(_world!.SurvivalPublished?.Run == 1 && c._currentWave == 2, "native offer starts wave 2 of the same run");
                // Native spawn path for flying/summoning enemies, away from both players.
                var assets = c._hordeModeControllerModel.Npcs; var spawns = c._spawnTransforms;
                var far = spawns.Where(t => Vector3.Distance(t.position, Player.Instance.FeetPosition) > 12 && Vector3.Distance(t.position, _world.Remote!.FeetPosition) > 12).Take(2).ToArray();
                TravelCheck(far.Length == 2, "two spawns are clear of both players");
                var index = 0;
                foreach (var name in new[] { "NPC_StarFuck", "NPC_ClownElemental" })
                {
                    NpcId? asset = null; foreach (var a in assets) if (a.name == name) asset = a;
                    var list = new Il2CppSystem.Collections.Generic.List<NpcId>();
                    for (var i = 0; i < assets.Count; i++) list.Add(asset!);
                    c.SpawnNextNpc(list, far[index++].position);
                }
                TravelCheck(Spawned(c).Any(n => n.NpcId.name == "NPC_StarFuck") && Spawned(c).Any(n => n.NpcId.name == "NPC_ClownElemental"), "native controller spawns flying enemies");
                File.WriteAllText(Marker("host-flying.txt"), string.Join("\n", Spawned(c).Where(n => n.NpcId.name is "NPC_StarFuck" or "NPC_ClownElemental").Select(n => $"{n.NpcId.name}\t{n.Transform.position}")));
                _wavesSummons = c._spawnedNpcs.Count; TravelMark("host-flying"); _wavesAt = now + 15; _travelStep = 14; break;
            case 14: // observe natural summons, then let the timer expire into CLEANUP
                foreach (var npc in Spawned(c!)) if (_wavesSeen.Add(npc.Pointer) && npc.NpcId.name is not ("NPC_StarFuck" or "NPC_ClownElemental"))
                    File.AppendAllText(Marker("host-wave2.txt"), npc.NpcId.name + "\n");
                if (now < _wavesAt || !Marked("client-flying")) return;
                c!._combatTimeLeft = Mathf.Min(c._combatTimeLeft, 0.5f); // synthetic: shorten wave 2 to reach native CLEANUP
                _travelStep = 15; break;
            case 15:
                if (c!._hordeModeState == HordeState.COMBAT) return;
                if (c._hordeModeState == HordeState.CLEANUP) { File.WriteAllText(Marker("host-cleanup.txt"), $"alive={Spawned(c).Count}"); KillAll(c); return; }
                TravelCheck(c._hordeModeState == HordeState.REST && c._currentWave == 3 && c._spawnedNpcs.Count == 0, "native CLEANUP clears the wave into REST once");
                TravelMark("host-rest2"); _travelStep = 16; break;
            case 16:
                if (!Marked("client-rest2")) return;
                ResetSession(); _travelStep = 17; break;
            case 17:
                if (!Marked("client-host-left")) return;
                TravelMark("host-pass"); _travelStep = 99; break;
        }
    }

    private void WavesClient()
    {
        var arena = SurvivalArena();
        var c = _lifecycle is { InSurvival: true } ? Controller() : null;
        var view = _world?.SurvivalView;
        var now = Time.unscaledTime;
        switch (_travelStep)
        {
            case 0:
                if (!_buttonVisibleLogged) return;
                _address!.text = "127.0.0.1"; _port!.text = Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!; Join(); _travelStep = 1; break;
            case 1:
                if (!InArena(arena) || view == null) return;
                TravelCheck(view is { Run: 0, Phase: SurvivalPhase.Intro, Wave: 1 }, "P2 receives P1's INTRO run state");
                TravelMark("client-arena"); _travelStep = 2; break;
            case 2:
                // Readiness gate (Phase 3): P2 uses the Ready control so P1's start pickup begins the run.
                if (view is { Phase: SurvivalPhase.Intro } && (view.Ready & SurvivalProtocol.Partner) == 0 && now > _wavesAt) { _world!.RequestSurvivalReady(); _wavesAt = now + 1; }
                if (view is { Phase: SurvivalPhase.Combat, CountdownDone: false }) _wavesCountdown.Add(view.Countdown);
                if (view is not { Run: 1, Phase: SurvivalPhase.Combat, CountdownDone: true } || ReplicaAlive() == 0) return;
                TravelCheck(c!._hordeModeState == HordeState.COMBAT && c._currentWave == 1 && c._spawnedNpcs.Count == 0, "P2 mirrors P1's COMBAT state without simulating spawns");
                TravelCheck(_wavesCountdown.Count > 0, "P2 receives P1's countdown");
                TravelMark("client-combat"); _travelStep = 3; break;
            case 3: // retry against current replicas: a replica can briefly outlive P1's enemy
                if (!Marked("host-p2-hit")) return;
                if (Marked("host-park")) { _travelStep = 4; return; }
                if (now < _wavesAt) return;
                _wavesAt = now + 1;
                var replicas = Object.FindObjectsOfType<Npc>().Where(n => n.IsAlive && n.gameObject.activeInHierarchy && n.NpcId != null).ToArray();
                if (replicas.Length == 0) return;
                var replica = replicas[UnityEngine.Random.Range(0, replicas.Length)];
                Player.Instance.Teleport(replica.Transform.position + Vector3.back * 2 + Vector3.up * 0.1f, Player.Instance.transform.rotation);
                replica.Hit(new HitData { dealer = Player.Instance.Cast<IAiActor>(), damage = 100000, weakSpotMultiplier = 1, position = replica.CenterOfMass, direction = Vector3.forward });
                File.AppendAllText(Marker("client-hits.txt"), replica.NpcId.name + "\n"); TravelMark("client-hit-sent"); return;
            case 4:
                // Park on a native spawn far from P1; enemies spawned nearby must avoid it.
                var p1 = _session!.Snapshot.RemotePose!;
                var spawn = c!._spawnTransforms.OrderByDescending(t => Vector3.Distance(t.position, new Vector3(p1.X, p1.Y, p1.Z)))
                    .OrderByDescending(t => c._spawnTransforms.Count(o => Vector3.Distance(o.position, t.position) < 8)).First();
                Player.Instance.Teleport(spawn.position + Vector3.up * 0.1f, Player.Instance.transform.rotation);
                TravelMark("client-on-spawn"); _travelStep = 5; break;
            case 5:
                if (!Marked("host-paused")) return;
                _wavesTime = view!.TimeLeft; _wavesAt = now + 1; _travelStep = 6; break;
            case 6:
                if (now < _wavesAt) return;
                TravelCheck(_lifecycle!.Frozen && Mathf.Abs(view!.TimeLeft - _wavesTime) < 0.01f, "P2's mirrored timer stays frozen during P1 pause");
                TravelMark("client-paused"); _travelStep = 7; break;
            case 7:
                if (view is not { Phase: SurvivalPhase.Rest, Wave: 2, WavesSurvived: 1 }) return;
                TravelCheck(c!._hordeModeState == HordeState.REST && c._spawnedNpcs.Count == 0, "P2 mirrors REST for wave 2 without local enemies");
                if (Object.FindObjectOfType<HordeModeWeaponSpawner>()._spawnedPickups.Count < SurvivalProtocol.ParseOffers(view.Offers).Length) return;
                TravelCheck(view.Offers.Length > 0, "P2 creates personal copies of P1's REST offers");
                _world!.RequestSurvivalReady(); // readiness gate: P1's offer starts wave 2 once P2 is ready
                TravelMark("client-rest"); _travelStep = 8; break;
            case 8:
                if (!Marked("host-flying")) return;
                var flying = Object.FindObjectsOfType<Npc>().Where(n => n.IsAlive && n.gameObject.activeInHierarchy && n.NpcId?.name is "NPC_StarFuck" or "NPC_ClownElemental").ToArray();
                if (flying.Length < 2) return;
                File.WriteAllText(Marker("client-flying.txt"), string.Join("\n", flying.Select(n => $"{n.NpcId.name}\t{n.Transform.position}")));
                TravelCheck(view is { Run: 1, Wave: 2 } && Math.Abs(ReplicaAlive() - view.Alive) <= 3, "P2 presents P1's pooled wave-2 population including flying enemies");
                TravelMark("client-flying"); _travelStep = 9; break;
            case 9:
                if (view is not { Phase: SurvivalPhase.Rest, Wave: 3 }) return;
                if (_wavesSpawnAt == 0) _wavesSpawnAt = now + 3; // allow the final inactive frames to arrive
                if (ReplicaAlive() > 0 && now < _wavesSpawnAt) return;
                TravelCheck(ReplicaAlive() == 0, "P2 replicas are cleared with P1's CLEANUP/REST");
                TravelMark("client-rest2"); _travelStep = 10; break;
            case 10:
                if (_lifecycle != null || SessionLifecycle.Current != null || SessionManager.SessionActive) return;
                TravelMark("client-host-left"); TravelMark("client-pass"); _travelStep = 99; break;
        }
    }
}
