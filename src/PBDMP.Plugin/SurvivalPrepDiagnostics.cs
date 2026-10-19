using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using Hyperstrange.PBD.PlayerComponents;
using Hyperstrange.PBD.UI.Menus;
using PBDMP.Network;
using UnityEngine;
using Object = UnityEngine.Object;
using HordeState = Hyperstrange.PBD.HordeModeController.HordeModeStateEnum;

namespace PBDMP.Plugin;

// Phase 3 Survival preparation fixture (Test-Travel.ps1 -SurvivalPrep): start-pickup readiness,
// personal resources and offers (same and different choices), stale/forged requests, wave
// revival, spectator, team wipe once, retry from results and membership recompute on leave.
// Labels: start pickup by contact and offers by native Interact; deaths use native HandleKill;
// enemies are removed with native Npc.Kill and waves end by shortening the native timer;
// living players are kept alive by restoring native hit points; the Ready control and the
// stale/forged requests use the fixture API instead of physical input.
public sealed partial class MultiplayerBehaviour
{
    private float _prepAt;
    private bool _prepKeepAlive = true;
    private int _prepStarts, _prepRejected, _prepGear;
    private Vector3 _prepSpot;
    private string _prepPick = "";
    private readonly HashSet<IntPtr> _prepTried = new();
    private static bool SurvivalPrepEnabled => Environment.GetEnvironmentVariable("PBDMP_SURVIVAL_PREP_TEST") == "1";

    private static HordeModeWeaponSpawner Offers() => Object.FindObjectOfType<HordeModeWeaponSpawner>();
    private static List<Pickup> OfferCopies()
    {
        var list = new List<Pickup>(); var spawned = Offers()._spawnedPickups;
        for (var i = 0; i < spawned.Count; i++) if (spawned[i] != null) list.Add(spawned[i]);
        return list;
    }
    private static string OfferName(Pickup pickup) => pickup.name.Replace("(Clone)", "").Trim();
    private static Pickup? Resource(Func<HordeModePickupSpawner, bool>? where = null) =>
        Object.FindObjectsOfType<HordeModePickupSpawner>().Where(s => s._currentPickupInstance != null && !s._currentPickupInstance._pickedUp && (where == null || where(s))).Select(s => s._currentPickupInstance).FirstOrDefault();
    // A collected resource keeps its spawner reference until the native cooldown replaces it.
    private static bool Available(HordeModePickupSpawner spawner) => spawner._currentPickupInstance != null && !spawner._currentPickupInstance._pickedUp;
    private static int Gear() => new PlayerInventoryComponent.SaveData(Player.Instance.GetComponent<PlayerInventoryComponent>()).HeldItems.Count;
    private static void Die()
    {
        var player = Player.Instance;
        player.GetComponent<PlayerHitProcessorComponent>().HandleKill(new HitData { position = player.CenterOfMass, direction = Vector3.forward, weakSpotMultiplier = 1 });
    }
    private Vector3 PartnerFeet() { var p = _session!.Snapshot.RemotePose!; return _world?.ResolvePoseFeet(p) ?? new Vector3(p.X, p.Y, p.Z); }
    private bool Revived() => Player.Instance is { IsAlive: true } player && !_lifecycle!.PlayerState.Pending &&
        Mathf.Approximately(player.HitPoints.Current, player.HitPoints.Max) && player.GetComponent<PlayerHitProcessorComponent>().Armor == 0;
    // The chase camera eases toward its target; a partner launched by a jump pad (VIP Zone)
    // can briefly outrun it. Wait for it to settle before the single-frame checks.
    private bool SpectatorSettled() => _spectator.Active && Vector3.Distance(_spectator.CameraPosition, Player.Instance.Transform.position) > 2 &&
        Vector3.Distance(_spectator.CameraPosition, PartnerFeet()) < 6;
    private void SpectatorChecks(string who)
    {
        TravelCheck(_lifecycle!.PlayerState.Pending && !Player.Instance.IsAlive, who + " stays dead: no timed campaign respawn in Survival");
        TravelCheck(_spectator.Active && Vector3.Distance(_spectator.CameraPosition, Player.Instance.Transform.position) > 2 &&
            Vector3.Distance(_spectator.CameraPosition, PartnerFeet()) < 6, who + " spectator camera follows the living partner");
        TravelCheck(_view!.Hud.BannerText.StartsWith("Waiting for next wave", StringComparison.Ordinal), who + " sees the Waiting for next wave notice");
        var item = Resource();
        if (item != null) { item.Interact(Player.Instance); TravelCheck(!item._pickedUp, who + " cannot collect while spectating"); }
    }

    private void RunSurvivalPrepTest()
    {
        if (_travelStep == 99) return;
        if (_travelStarted == 0) _travelStarted = Time.unscaledTime;
        var view = _world?.SurvivalView ?? _world?.SurvivalPublished;
        File.WriteAllText(Marker(TravelRole + "-state.txt"), $"step={_travelStep}, released={_lifecycle?.State.Released}, dead={_lifecycle?.PlayerState.Pending}, view={view}, banner={_view?.Hud.BannerText}");
        if (Time.unscaledTime - _travelStarted > 330) throw new TimeoutException("Survival preparation diagnostic timed out: " + _travelStep);
        if (_lifecycle is { NativeReady: false }) _lifecycle.AutoContinue();
        if (_prepKeepAlive && _lifecycle is { NativeReady: true } && Player.Instance is { IsAlive: true } living)
            living.GetComponent<PlayerHitProcessorComponent>().SetCurrentHitPoints(living.HitPoints.Max);
        if (TravelRole == "host") PrepHost(); else PrepClient();
    }

    private void PrepHost()
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
                if (!InArena(arena) || !Marked("client-arena")) return;
                Player.Instance.Teleport(StartPickup().transform.position + Vector3.up * 0.1f, Player.Instance.transform.rotation);
                _prepAt = now + 8; _travelStep = 3; break;
            case 3: // P1's start pickup marks P1 ready; the run waits for P2
                if (s is not { Ready: SurvivalProtocol.Host }) { if (now > _prepAt) throw new TimeoutException("P1 start pickup did not mark P1 ready."); return; }
                TravelCheck(c!._hordeModeState == HordeState.INTRO && SurvivalPatches.BeginCount == 0, "P1's start pickup does not begin the run before P2 is ready");
                TravelCheck(s.Required == SurvivalProtocol.Everyone, "both admitted living players are required to be ready");
                TravelCheck(_view!.Hud.BannerText.StartsWith("Ready - waiting", StringComparison.Ordinal), "P1 sees the waiting-for-partner notice");
                TravelMark("host-start-ready"); _prepAt = now + 12; _travelStep = 4; break;
            case 4:
                if (c!._hordeModeState != HordeState.COMBAT || s?.Run != 1) { if (now > _prepAt) throw new TimeoutException("Run did not begin after P2's start pickup."); return; }
                TravelCheck(SurvivalPatches.BeginCount == 1, "P2's start pickup begins exactly one native run");
                c._combatTimeLeft += 60; // synthetic: keep wave 1 running through the death checks
                TravelMark("host-run"); _travelStep = 5; break;
            case 5:
                if (!Marked("client-resource")) return;
                var spot = File.ReadAllText(Marker("client-resource.txt")).Split(',').Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                var mine = Object.FindObjectsOfType<HordeModePickupSpawner>().OrderBy(r => Vector3.Distance(r.transform.position, new Vector3(spot[0], spot[1], spot[2]))).First();
                TravelCheck(Available(mine), "P2's resource collection leaves P1's personal copy");
                TravelMark("host-kill-p2"); _travelStep = 6; break;
            case 6: // P2 dead: not required, not targeted, not revived during combat
                if (!Marked("client-dead")) return;
                TravelCheck(_session!.Snapshot.RemotePose?.Alive == false && s!.Required == SurvivalProtocol.Host && !s.Ended, "P1 records P2's death; the run continues");
                TravelCheck(Spawned(c!).All(n => n.Target == null || n.Target.Pointer != _world!.Remote!.Pointer), "enemies do not target dead P2");
                _travelStep = 7; break;
            case 7: // synthetic end of wave 1
                KillAll(c!);
                if (c!._hordeModeState == HordeState.COMBAT) c._combatTimeLeft = Mathf.Min(c._combatTimeLeft, 0.5f);
                if (c._hordeModeState != HordeState.REST || OfferCopies().Count == 0 || s is not { Phase: SurvivalPhase.Rest, Offers.Length: > 0 }) return;
                TravelCheck(c._currentWave == 2 && SurvivalProtocol.ParseOffers(s!.Offers).Length == OfferCopies().Count, "P1 publishes its native REST offer set");
                File.WriteAllText(Marker("host-offers.txt"), string.Join("\n", OfferCopies().Select(OfferName).OrderBy(n => n)));
                TravelMark("host-rest2"); _travelStep = 8; break;
            case 8: // same offer: P1 picks first and waits
                if (!Marked("client-rest2")) return;
                TravelCheck(File.ReadAllText(Marker("client-offers.txt")) == File.ReadAllText(Marker("host-offers.txt")), "both players see identical offers");
                var pick = OfferCopies()[0]; _prepPick = OfferName(pick); File.WriteAllText(Marker("host-pick.txt"), _prepPick);
                _prepStarts = _world!.SurvivalWaveStarts; pick.Interact(Player.Instance); _prepAt = now + 1.5f; _travelStep = 9; break;
            case 9:
                if (now < _prepAt) return;
                TravelCheck(c!._hordeModeState == HordeState.REST && s!.Ready == SurvivalProtocol.Host && OfferCopies().Count == 0, "P1's offer is personal: P1 is ready, its other offers clear and the wave waits");
                TravelMark("host-picked"); _prepAt = now + 10; _travelStep = 10; break;
            case 10:
                if (c!._hordeModeState != HordeState.COMBAT) { if (now > _prepAt) throw new TimeoutException("Wave 2 did not start after both picks."); return; }
                TravelCheck(_world!.SurvivalWaveStarts == _prepStarts + 1 && c._currentWave == 2 && s?.Run == 1, "both choosing the same offer starts wave 2 once");
                TravelMark("host-wave2"); _prepKeepAlive = false; Die(); _prepAt = now + 5; _travelStep = 11; break;
            case 11: // P1 dead while P2 fights
                if (now < _prepAt || !Marked("client-wave2")) return;
                if (!SpectatorSettled() && now < _prepAt + 3) return;
                SpectatorChecks("P1");
                TravelCheck(c!._hordeModeState is HordeState.COMBAT && !s!.Ended && Spawned(c).All(n => n.Target == null || n.Target.Pointer != Player.Instance.Pointer), "the run continues for P2 and enemies leave dead P1");
                _prepAt = 0; _travelStep = 12; break;
            case 12: // synthetic end of wave 2: P1 revives at REST
                KillAll(c!);
                if (c!._hordeModeState == HordeState.COMBAT) c._combatTimeLeft = Mathf.Min(c._combatTimeLeft, 0.5f);
                if (c._hordeModeState != HordeState.REST) return;
                if (!Revived() || _spectator.Active) { if (_prepAt == 0) _prepAt = now + 3; else if (now > _prepAt) throw new TimeoutException("P1 did not revive at REST."); return; }
                _prepKeepAlive = true;
                TravelCheck(c._currentWave == 3, "P1 revives with full health and zero armor at wave 3 preparation; spectator released");
                var leftNames = new List<string>(); var left = Offers()._weaponPickupsLeft; for (var i = 0; i < left.Count; i++) leftNames.Add(left[i].name);
                if (_prepPick.Contains("Weapon")) TravelCheck(!leftNames.Contains(_prepPick), "a weapon both players took leaves the offer pool");
                File.WriteAllText(Marker("host-offers3.txt"), string.Join("\n", OfferCopies().Select(OfferName)));
                _prepRejected = _world!.SurvivalRequestsRejected; TravelMark("host-rest3"); _travelStep = 13; break;
            case 13:
                if (!Marked("client-stale")) return;
                _prepAt = now + 1; _travelStep = 14; break;
            case 14:
                if (now < _prepAt) return;
                TravelCheck(_world!.SurvivalRequestsRejected >= _prepRejected + 2 && s!.Ready == 0 && c!._hordeModeState == HordeState.REST, "stale-wave and unoffered requests are rejected without readiness");
                var first = OfferCopies()[0]; File.WriteAllText(Marker("host-pick3.txt"), OfferName(first));
                _prepStarts = _world.SurvivalWaveStarts; first.Interact(Player.Instance); TravelMark("host-picked3"); _prepAt = now + 10; _travelStep = 15; break;
            case 15:
                if (c!._hordeModeState != HordeState.COMBAT) { if (now > _prepAt) throw new TimeoutException("Wave 3 did not start after different picks."); return; }
                TravelCheck(_world!.SurvivalWaveStarts == _prepStarts + 1 && c._currentWave == 3, "different offer choices both succeed and start wave 3 once");
                TravelMark("host-wipe"); _travelStep = 16; break;
            case 16: // team wipe: P2 dies first, the run continues; then P1
                if (!Marked("client-dead3")) return;
                TravelCheck(!s!.Ended && _world!.SurvivalEnds == 0, "one survivor keeps the run alive");
                _prepKeepAlive = false; Die(); _prepAt = now + 8; _travelStep = 17; break;
            case 17:
                var results = Object.FindObjectOfType<EndHordeMenu>();
                if (s?.Ended != true || results == null || !results.gameObject.activeInHierarchy) { if (now > _prepAt) throw new TimeoutException("Team wipe did not end the run."); return; }
                TravelCheck(_world!.SurvivalEnds == 1 && _lifecycle!.SurvivalEnded, "both players dead ends the run through the native results");
                _prepAt = now + 3; _travelStep = 18; break;
            case 18:
                if (now < _prepAt || !Marked("client-ended")) return;
                TravelCheck(_world!.SurvivalEnds == 1 && _lifecycle!.PlayerState.Pending, "the wipe finalizes once and nobody revives");
                _travelEpoch = _lifecycle!.Epoch; Object.FindObjectOfType<EndHordeMenu>().ResetLevel(); _travelStep = 19; break;
            case 19: // P1 retries from results
                if (_lifecycle!.Epoch == _travelEpoch || !InArena(arena) || !Marked("client-retry")) return;
                _prepKeepAlive = true;
                TravelCheck(c is { _hordeModeState: HordeState.INTRO, _currentWave: 1 } && Player.Instance.IsAlive && !_lifecycle.PlayerState.Pending && !_lifecycle.SurvivalEnded,
                    "retry from results starts a clean INTRO with P1 alive");
                TravelCheck(!InputManager.InputStateStack.Contains(InputManager.InputState.NONE), "P1 retry leaves no input barrier");
                _world!.RequestSurvivalReady(); _prepAt = now + 2; _travelStep = 20; break;
            case 20: // Ready control without the start pickup; P2 leaving recomputes membership
                if (now < _prepAt) return;
                TravelCheck(c!._hordeModeState == HordeState.INTRO && s?.Ready == SurvivalProtocol.Host, "P1's Ready control waits for P2");
                _prepStarts = SurvivalPatches.BeginCount; TravelMark("host-waiting"); _prepAt = now + 20; _travelStep = 21; break;
            case 21:
                if (c!._hordeModeState != HordeState.COMBAT) { if (now > _prepAt) throw new TimeoutException("P2 leaving did not release the ready wait."); return; }
                TravelCheck(!_session!.Snapshot.Connected && SurvivalPatches.BeginCount == _prepStarts + 1, "P2 leaving removes its readiness requirement and the run begins once");
                ResetSession(); TravelMark("host-pass"); _travelStep = 99; break;
        }
    }

    private void PrepClient()
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
                if (!InArena(arena) || view is not { Phase: SurvivalPhase.Intro }) return;
                TravelMark("client-arena"); _travelStep = 2; break;
            case 2:
                if (!Marked("host-start-ready")) return;
                if (view is not { Ready: SurvivalProtocol.Host }) { if (_prepAt == 0) _prepAt = now + 5; else if (now > _prepAt) throw new TimeoutException("P2 did not see P1 ready."); return; }
                TravelCheck(view is { Phase: SurvivalPhase.Intro, Ready: SurvivalProtocol.Host }, "P2 sees P1 ready while the run waits");
                TravelCheck(_view!.Hud.BannerText.StartsWith("Take the shotgun", StringComparison.Ordinal), "P2 sees the start-pickup readiness notice");
                Player.Instance.Teleport(StartPickup().transform.position + Vector3.up * 0.1f, Player.Instance.transform.rotation);
                _prepAt = now + 12; _travelStep = 3; break;
            case 3:
                if (view is not { Run: 1, Phase: SurvivalPhase.Combat } || Resource() == null) { if (now > _prepAt) throw new TimeoutException("P2 did not see run 1 with personal resources."); return; }
                TravelCheck(StartPickup()._pickedUp && c!._spawnedNpcs.Count == 0, "P2 keeps its personal start shotgun without simulating the run");
                TravelCheck(Object.FindObjectsOfType<HordeModePickupSpawner>().Count(Available) > 0, "P2's native resource spawners provide personal pickups");
                // Native health pickups refuse a full-health player (kept full here); try each resource.
                _prepTried.Clear(); _prepAt = 0; _travelStep = 4; break;
            case 4:
                var collected = Object.FindObjectsOfType<HordeModePickupSpawner>().OrderBy(r => Vector3.Distance(r.transform.position, _prepSpot)).First();
                if (_prepAt != 0 && Available(collected) && now < _prepAt) return;
                if (_prepAt == 0 || Available(collected))
                {
                    var resource = Object.FindObjectsOfType<HordeModePickupSpawner>().FirstOrDefault(r => Available(r) && !_prepTried.Contains(r.Pointer));
                    TravelCheck(resource != null, "P2 collects its personal resource");
                    _prepTried.Add(resource!.Pointer); _prepSpot = resource.transform.position;
                    resource._currentPickupInstance.Interact(Player.Instance); _prepAt = now + 1.5f; return;
                }
                TravelCheck(true, "P2 collects its personal resource");
                File.WriteAllText(Marker("client-resource.txt"), string.Join(",", new[] { _prepSpot.x, _prepSpot.y, _prepSpot.z }.Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture))));
                TravelMark("client-resource"); _travelStep = 5; break;
            case 5:
                if (!Marked("host-kill-p2")) return;
                _prepGear = Gear(); _prepKeepAlive = false; Die(); _prepAt = now + 5; _travelStep = 6; break;
            case 6:
                if (now < _prepAt) return;
                if (!SpectatorSettled() && now < _prepAt + 3) return;
                SpectatorChecks("P2");
                TravelMark("client-dead"); _prepAt = 0; _travelStep = 7; break;
            case 7: // REST: P2 revives beside P1 and receives personal offer copies
                if (view is not { Phase: SurvivalPhase.Rest, Wave: 2 }) return;
                if (_prepAt == 0) _prepAt = now + 8;
                // The spectator releases the camera in the LateUpdate after the revival.
                if (!Revived() || _spectator.Active || OfferCopies().Count < SurvivalProtocol.ParseOffers(view.Offers).Length) { if (now > _prepAt) throw new TimeoutException("P2 did not revive with offers at REST."); return; }
                _prepKeepAlive = true;
                TravelCheck(true, "P2 revives at preparation with full health and zero armor; spectator released");
                TravelCheck(Gear() >= _prepGear, "P2 keeps its weapons and ammo through the wave revival");
                TravelCheck(Vector3.Distance(Player.Instance.FeetPosition, PartnerFeet()) < 6, "P2 revives beside living P1");
                File.WriteAllText(Marker("client-offers.txt"), string.Join("\n", OfferCopies().Select(OfferName).OrderBy(n => n)));
                TravelMark("client-rest2"); _travelStep = 8; break;
            case 8:
                if (!Marked("host-picked")) return;
                TravelCheck(OfferCopies().Count == SurvivalProtocol.ParseOffers(view!.Offers).Length && (view.Ready & SurvivalProtocol.Host) != 0, "P1's choice leaves P2's offers available");
                var same = OfferCopies().First(p => OfferName(p) == File.ReadAllText(Marker("host-pick.txt")));
                same.Interact(Player.Instance); _prepAt = now + 3; _travelStep = 9; break;
            case 9: // native pickup animation completes before the offer handler runs
                if (OfferCopies().Count > 0 && now < _prepAt) return;
                if (_prepAt > 0) { TravelCheck(OfferCopies().Count == 0, "P2 takes the same offer as its own copy"); _prepAt = 0; }
                if (view is not { Phase: SurvivalPhase.Combat, Wave: 2 }) return;
                TravelMark("client-wave2"); _travelStep = 10; break;
            case 10:
                if (!Marked("host-rest3") || view is not { Phase: SurvivalPhase.Rest, Wave: 3 }) return;
                _world!.SendSurvivalRequestForTest(new SurvivalRequest(view.Run, 2, SurvivalPhase.Rest, SurvivalRequestKind.Ready));
                _world.SendSurvivalRequestForTest(new SurvivalRequest(view.Run, 3, SurvivalPhase.Rest, SurvivalRequestKind.Offer, "I63"));
                TravelMark("client-stale"); _travelStep = 11; break;
            case 11:
                if (!Marked("host-picked3")) return;
                var hostPick = File.ReadAllText(Marker("host-pick3.txt"));
                var different = OfferCopies().FirstOrDefault(p => OfferName(p) != hostPick) ?? OfferCopies().First();
                TravelCheck(OfferName(different) != hostPick, "P2 can choose a different offer than P1");
                different.Interact(Player.Instance); _travelStep = 12; break;
            case 12:
                if (view is not { Phase: SurvivalPhase.Combat, Wave: 3 } || !Marked("host-wipe")) return;
                _prepKeepAlive = false; Die(); TravelMark("client-dead3"); _prepAt = now + 10; _travelStep = 13; break;
            case 13:
                var results = Object.FindObjectOfType<EndHordeMenu>();
                if (view is not { Ended: true } || results == null || !results.gameObject.activeInHierarchy) { if (now > _prepAt) throw new TimeoutException("P2 did not receive the team wipe."); return; }
                TravelCheck(_world!.SurvivalEnds == 1 && _lifecycle!.PlayerState.Pending, "P2 receives the shared end once and stays dead");
                var epoch = _lifecycle!.Epoch; GameManager.Load(null);
                TravelCheck(_lifecycle.Epoch == epoch, "P2 cannot retry the Survival run itself");
                _travelEpoch = epoch; TravelMark("client-ended"); _travelStep = 14; break;
            case 14:
                if (_lifecycle!.Epoch == _travelEpoch || !InArena(arena) || view is not { Phase: SurvivalPhase.Intro, Ended: false }) return;
                _prepKeepAlive = true;
                TravelCheck(Player.Instance.IsAlive && !_lifecycle.PlayerState.Pending && !_spectator.Active, "P2 follows the retry alive");
                TravelCheck(!InputManager.InputStateStack.Contains(InputManager.InputState.NONE) && Object.FindObjectOfType<EndHordeMenu>()?.gameObject.activeInHierarchy != true,
                    "P2's results and input barrier are cleared by the retry");
                TravelMark("client-retry"); _travelStep = 15; break;
            case 15:
                if (!Marked("host-waiting")) return;
                ResetSession(); _travelStep = 16; break;
            case 16:
                if (_lifecycle != null || SessionLifecycle.Current != null || SessionManager.SessionActive) return;
                TravelMark("client-pass"); _travelStep = 99; break;
        }
    }
}
