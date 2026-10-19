using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using PBDMP.Network;
using UnityEngine;
using Object = UnityEngine.Object;
using HordeState = Hyperstrange.PBD.HordeModeController.HordeModeStateEnum;

namespace PBDMP.Plugin;

// Survival run authority: P1's native HordeModeController simulates; P2 receives a typed,
// validated state frame. P2's controller never runs (see SurvivalPatches); its fields mirror
// P1's values for presentation only and no native state events are raised on P2.
internal sealed partial class SharedWorld
{
    private const float SurvivalTimerCadence = 0.5f, MinPartnerSpawnDistance = 8f, ReviveGrace = 1f, ReviveTimeout = 5f, ReadyHoldSeconds = 1f;
    private uint _survivalRun;
    private SurvivalState? _lastSurvival, _survivalView;
    private float _survivalSentAt, _countdownStartedAt = -1;
    private HordeModeController? _horde;
    private HordeModeWeaponSpawner? _offerSpawner;
    // P1's preparation and death ledger for the current run.
    private (uint Run, int Wave, HordeState Phase)? _prep;
    private byte _ready, _revive, _seenAlive;
    private float _prepAt;
    private bool _ended, _authorized, _pickingOffer;
    private readonly Dictionary<int, byte> _taken = new();
    private readonly List<SurvivalOffer> _offers = new();
    private string _offersText = "";
    // Offer instance -> authored prefab token, on both PCs.
    private readonly Dictionary<IntPtr, string> _offerTokens = new();
    // P2's local presentation of P1's ledger.
    private (uint Run, int Wave)? _clientOffersFor, _clientRevivedFor;
    private uint? _clientResourcesRun;
    private bool _clientEnded;
    private float _readyRequestAt, _useHeldSince = -1;
    internal SurvivalState? SurvivalView => _survivalView;
    internal SurvivalState? SurvivalPublished => _lastSurvival;
    internal int PartnerSpawnRejections { get; private set; }
    internal int SurvivalRequestsRejected { get; private set; }
    internal int SurvivalWaveStarts { get; private set; }
    internal int SurvivalEnds { get; private set; }
    internal int SurvivalFramesSent { get; private set; }

    private HordeModeController? Horde()
    {
        if (!_life.InSurvival) return null;
        if (_horde == null) _horde = Object.FindObjectOfType<HordeModeController>();
        return _horde;
    }
    private HordeModeWeaponSpawner? OfferSpawner()
    {
        if (!_life.InSurvival) return null;
        if (_offerSpawner == null) _offerSpawner = Object.FindObjectOfType<HordeModeWeaponSpawner>();
        return _offerSpawner;
    }

    // A join changes the travel epoch without reloading P1's arena: keep the run's ledger,
    // but a newly admitted P2 must become ready again.
    private void ClearSurvival(bool keepRun)
    {
        _lastSurvival = null; _survivalView = null; _horde = null; _offerSpawner = null;
        _clientOffersFor = null; _clientRevivedFor = null; _clientResourcesRun = null; _clientEnded = false; _useHeldSince = -1;
        ClearSurvivalPresentation(keepRun);
        if (keepRun) { _ready &= SurvivalProtocol.Host; return; }
        _countdownStartedAt = -1; _prep = null; _ready = 0; _revive = 0; _ended = false;
        _taken.Clear(); _offers.Clear(); _offersText = ""; _offerTokens.Clear(); _deadPartners.Clear();
    }

    internal void SurvivalBegan() { if (!Client && _life.InSurvival) { _survivalRun++; _taken.Clear(); _runId = Guid.NewGuid().ToString("N"); } }
    internal void SurvivalCombatStarted() { if (!Client && _life.InSurvival) { _countdownStartedAt = Time.time; SurvivalWaveStarts++; } }

    // Native spawn selection keeps 8 m only from P1's cached local player. Reject a choice
    // near living P2 as well; native code already put that spawn on its 2 s cooldown, so the
    // controller simply retries a different ready spawn on a later slow update.
    internal bool PartnerSpawnSafe(Vector3 position)
    {
        if (Client || !Active || _remote is not { IsAlive: true } remote) return true;
        if (Vector3.Distance(position, remote.FeetPosition) >= MinPartnerSpawnDistance) return true;
        PartnerSpawnRejections++; return false;
    }

    // Native RegisterNpc targets P1's local player, and an existing target stops awareness from
    // choosing again. Start each enemy on the nearer living player; native hit processing still
    // retargets attackers. Dead players never keep enemies: retarget to the living partner.
    internal void ChooseSurvivalTarget(Npc npc)
    {
        if (Client || !Active || !npc.IsAlive) return;
        var local = Player.Instance; var remote = _remote;
        var localAlive = local != null && local.IsAlive; var remoteAlive = remote is { IsAlive: true };
        if (!localAlive && !remoteAlive) return;
        var position = npc.Transform.position;
        var useRemote = remoteAlive && (!localAlive || Vector3.Distance(position, remote!.CenterOfMass) < Vector3.Distance(position, local!.CenterOfMass));
        npc.SetTarget(useRemote ? remote!.Actor : local!.Cast<IAiActor>());
    }

    private float _survivalTargetsAt;
    private void TickSurvivalTargets()
    {
        if (Client || !Active || Time.unscaledTime < _survivalTargetsAt || Horde() is not { } c) return;
        _survivalTargetsAt = Time.unscaledTime + 0.5f;
        var local = Player.Instance; var remote = _remote;
        for (var i = 0; i < c._spawnedNpcs.Count; i++)
        {
            var npc = c._spawnedNpcs[i];
            if (npc == null || !npc.IsAlive || npc.Target is not { } target) continue;
            var deadLocal = local != null && target.Pointer == local.Pointer && !local.IsAlive;
            var deadRemote = remote != null && target.Pointer == remote.Pointer && !remote.IsAlive;
            if (deadLocal || deadRemote) ChooseSurvivalTarget(npc);
        }
    }

    // Participants are P1 plus an admitted, world-matched P2. A loading or joining P2 is not
    // an extra life and adds no readiness requirement.
    private PlayerPose? PartnerPose => _session.Snapshot.RemotePose is { Ready: true } pose && pose.Epoch == _epoch ? pose : null;
    private bool PartnerParticipant => !Client && _session.Snapshot.Connected && _life.State.Released && _peerMatched && PartnerPose != null;
    private byte Participants => (byte)(SurvivalProtocol.Host | (PartnerParticipant ? SurvivalProtocol.Partner : 0));
    private bool LocalAlive => Player.Instance != null && Player.Instance.IsAlive && !_life.PlayerState.Pending;
    private byte AliveMask => (byte)((LocalAlive ? SurvivalProtocol.Host : 0) | (PartnerParticipant && PartnerPose!.Alive ? SurvivalProtocol.Partner : 0));
    private static bool Preparation(HordeState phase) => phase is HordeState.INTRO or HordeState.REST;

    // Each INTRO/REST of a run is one preparation phase. Entering REST authorizes revival of
    // every participant then dead, with a short grace for a death frame still in flight. A
    // player seen alive in this phase is never authorized again until the next one.
    private void SyncPreparation(HordeModeController c)
    {
        var phase = c._hordeModeState;
        if (!Preparation(phase)) { _prep = null; _ready = 0; _revive = 0; return; }
        var key = (_survivalRun, Math.Max(1, c._currentWave), phase);
        var alive = AliveMask; var dead = (byte)(Participants & ~alive);
        if (_prep != key) { _prep = key; _ready = 0; _prepAt = Time.unscaledTime; _seenAlive = alive; _revive = phase == HordeState.REST ? dead : (byte)0; }
        else if (phase == HordeState.REST && Time.unscaledTime - _prepAt < ReviveGrace) _revive |= (byte)(dead & ~_seenAlive);
        _seenAlive |= alive;
        // An authorized P2 that never revives (lost frame, failed restore) must not block the wave.
        if (Time.unscaledTime - _prepAt > ReviveTimeout) _revive &= SurvivalProtocol.Host;
    }

    private bool ReadyToStart()
    {
        var required = (byte)(Participants & (AliveMask | _revive));
        _ready &= Participants;
        return required != 0 && (_ready & required) == required && !_life.Frozen;
    }

    private void TickSurvivalRules()
    {
        _pickingOffer = false;
        if (Client || !Active || _ended || Horde() is not { } c) return;
        SyncPreparation(c);
        TrackDeadPartner(c);
        if (_prep is { } prep)
        {
            // P1's revival is local and immediate; P2's is authorized through the state frame.
            if ((_revive & SurvivalProtocol.Host) != 0) { if (!LocalAlive) ReviveLocal(prep.Wave, PartnerParticipant ? PartnerPose : null); _revive &= SurvivalProtocol.Partner; }
            _revive &= (byte)(Participants & ~AliveMask);
            if (ReadyToStart()) StartWave(c, prep.Phase);
        }
        // Team wipe: no admitted, connected participant is alive or awaiting this revival.
        if ((AliveMask & Participants) == 0 && (_revive & Participants) == 0 && !_life.Frozen) EndRun(c);
    }

    private void StartWave(HordeModeController c, HordeState phase)
    {
        _ready = 0; _authorized = true;
        try
        {
            if (phase == HordeState.INTRO) c.Begin();
            else { c.SetHordeModeState(HordeState.COMBAT); OfferSpawner()?.ClearSpawnedPickups(); }
        }
        finally { _authorized = false; }
    }

    private void EndRun(HordeModeController c)
    {
        _ended = true; _life.SurvivalEnded = true; SurvivalEnds++;
        Plugin.Logger.LogInfo($"Survival team wipe: run {_survivalRun} ended once.");
        NativeEnd(); CaptureResult(c);
        FinalizeRecord(c, _runId, Math.Max(0, (double)c._currentScore), Math.Max(0, c.Kills), Math.Max(0, c.WavesSurvived), Math.Max(0, (double)c.DamageDealt), _resultWeapon, _resultTime);
    }

    private static void NativeEnd()
    {
        if (LevelController._instance is not { } level) return;
        // P2's inert controller never caches its LevelController parent, which native EndLevel reads.
        if (level._hordeModeController is { } horde && horde.Parent == null) { horde._parent = level; horde._parentCached = true; }
        SurvivalPatches.Ending = true;
        try { level.EndLevel(LevelController.EndLevelType.DEATH); }
        finally { SurvivalPatches.Ending = false; }
    }

    private void ReviveLocal(int wave, PlayerPose? partner)
    {
        Vector3? beside = partner is { Alive: true } ? ResolvePoseFeet(partner) : null;
        if (_life.ReviveForWave(beside, partner?.Yaw ?? 0)) _life.Explain($"Revived for wave {wave}.");
    }

    // Start pickup (INTRO): P1 begins only once every participant is ready; P2 requests.
    internal bool SurvivalBeginRequested()
    {
        if (Client) { RequestSurvivalReady(); return false; }
        if (_authorized || _ended || Horde() is not { } c || c._hordeModeState != HordeState.INTRO) return true;
        SyncPreparation(c); _ready |= SurvivalProtocol.Host;
        if (ReadyToStart()) { _ready = 0; return true; }
        _life.Explain("Ready. Waiting for P2.");
        return false;
    }

    // P1: REST rebuilds the native offer pool from the shared ledger before native spawning;
    // a picked offer marks P1 ready and starts the wave only when everyone is ready.
    // P2: the native offer handler must never change P2's inert controller state.
    internal bool SurvivalStateChanging(HordeModeController c, HordeState next)
    {
        if (Client) return !_pickingOffer;
        if (_authorized) return true;
        if (next == HordeState.REST) { RebuildOfferPool(); _offers.Clear(); _offersText = ""; _offerTokens.Clear(); return true; }
        if (next != HordeState.COMBAT || c._hordeModeState != HordeState.REST || !_pickingOffer) return true;
        SyncPreparation(c); _ready |= SurvivalProtocol.Host;
        if (ReadyToStart()) { _ready = 0; return true; }
        _life.Explain("Ready. Waiting for P2.");
        return false;
    }

    // Native rule: offers draw from weapons not yet taken, otherwise items. In co-op a weapon
    // leaves the pool once every participant has taken it from an offer.
    private void RebuildOfferPool()
    {
        if (OfferSpawner() is not { } s) return;
        var left = s._weaponPickupsLeft; var all = s._weaponPickups; var required = Participants;
        left.Clear();
        for (var i = 0; i < all.Length; i++)
            if (all[i] != null && (_taken.GetValueOrDefault(i) & required) != required) left.Add(all[i]);
    }

    internal void SurvivalOfferSpawned(HordeModeWeaponSpawner spawner, Pickup prefab, Transform at)
    {
        var spawned = spawner._spawnedPickups;
        if (spawned.Count == 0 || OfferToken(spawner, prefab) is not { } token) return;
        _offerTokens[spawned[spawned.Count - 1].Pointer] = token;
        if (Client) return;
        var slots = spawner._spawns; var slot = -1;
        for (var i = 0; i < slots.Length; i++) if (slots[i] != null && slots[i].Pointer == at.Pointer) { slot = i; break; }
        if (slot is < 0 or > SurvivalProtocol.MaxSlot || _offers.Count >= SurvivalProtocol.MaxOffers || _offers.Any(o => o.Slot == slot)) return;
        _offers.Add(new(slot, token)); _offersText = SurvivalProtocol.FormatOffers(_offers);
    }

    private static string? OfferToken(HordeModeWeaponSpawner spawner, Pickup prefab)
    {
        var weapons = spawner._weaponPickups;
        for (var i = 0; i < weapons.Length && i <= SurvivalProtocol.MaxPrefab; i++) if (weapons[i] != null && weapons[i].Pointer == prefab.Pointer) return "W" + i;
        var items = spawner._itemPickups;
        for (var i = 0; i < items.Length && i <= SurvivalProtocol.MaxPrefab; i++) if (items[i] != null && items[i].Pointer == prefab.Pointer) return "I" + i;
        return null;
    }

    private static Pickup OfferPrefab(HordeModeWeaponSpawner spawner, string token)
    {
        var weapon = SurvivalProtocol.Weapon(token, out var index);
        var list = weapon ? spawner._weaponPickups : spawner._itemPickups;
        if (index >= list.Length || list[index] == null) throw new InvalidDataException("Host offered an unavailable Survival prefab: " + token);
        return list[index];
    }

    // The collected offer is the player's own copy. P1 records weapon choices; P2 reports its
    // choice, which also marks P2 ready.
    internal void SurvivalOfferTaking(Pickup pickup)
    {
        _pickingOffer = true;
        if (!_offerTokens.Remove(pickup.Pointer, out var token)) return;
        if (Client) { SendSurvivalRequest(SurvivalRequestKind.Offer, token); return; }
        if (SurvivalProtocol.Weapon(token, out var index)) _taken[index] = (byte)(_taken.GetValueOrDefault(index) | SurvivalProtocol.Host);
    }
    internal void SurvivalOfferTaken() => _pickingOffer = false;

    // Diagnostics only: deliver an arbitrary (stale or forged) bounded request to P1.
    internal void SendSurvivalRequestForTest(SurvivalRequest request) { if (Client) Send(SurvivalProtocol.ToFrame(request, _epoch)); }

    // Explicit Ready control (hold Use) and the start pickup share this path.
    internal void RequestSurvivalReady() => SendSurvivalRequest(SurvivalRequestKind.Ready, "");

    private void SendSurvivalRequest(SurvivalRequestKind kind, string offer)
    {
        if (!Active || !_life.InSurvival) return;
        if (!Client)
        {
            if (_ended || Horde() is not { } c) return;
            SyncPreparation(c);
            if (_prep != null && LocalAlive) _ready |= SurvivalProtocol.Host;
            return;
        }
        if (_survivalView is not { Phase: SurvivalPhase.Intro or SurvivalPhase.Rest, Ended: false } v || !LocalAlive) return;
        Send(SurvivalProtocol.ToFrame(new SurvivalRequest(v.Run, v.Wave, v.Phase, v.Phase == SurvivalPhase.Rest ? kind : SurvivalRequestKind.Ready,
            v.Phase == SurvivalPhase.Rest && kind == SurvivalRequestKind.Offer ? offer : ""), _epoch));
        _readyRequestAt = Time.unscaledTime;
    }

    private void ApplySurvivalRequest(WorldFrame frame)
    {
        var request = SurvivalProtocol.RequestFromFrame(frame);
        if (request.Kind == SurvivalRequestKind.Damage) { ApplySurvivalDamage(request); return; }
        if (_ended || Horde() is not { } c || !PartnerParticipant || PartnerPose?.Alive != true) { SurvivalRequestsRejected++; return; }
        SyncPreparation(c);
        if (_prep is not { } prep || prep.Run != request.Run || prep.Wave != request.Wave ||
            (prep.Phase == HordeState.REST ? SurvivalPhase.Rest : SurvivalPhase.Intro) != request.Phase ||
            request.Kind == SurvivalRequestKind.Offer && !_offers.Any(o => o.Token == request.Offer))
        { SurvivalRequestsRejected++; return; }
        if (request.Kind == SurvivalRequestKind.Offer && SurvivalProtocol.Weapon(request.Offer, out var index))
            _taken[index] = (byte)(_taken.GetValueOrDefault(index) | SurvivalProtocol.Partner);
        _ready |= SurvivalProtocol.Partner;
    }

    // P2: personal resource spawners, personal offer copies, authorized revival and the
    // shared native end of a wiped run.
    private void TickSurvivalClient()
    {
        _pickingOffer = false;
        if (!Client || !Active || _survivalView is not { } v || Horde() is not { } c) return;
        if (v.Ended)
        {
            if (!_clientEnded)
            {
                _clientEnded = true; _life.SurvivalEnded = true; SurvivalEnds++;
                ApplyFinalResult(c, v); NativeEnd();
                // Native EndLevel gathered P2's own level clock; show P1's team result.
                if (LevelController._instance is { } level) level.TimeStat = new TimeStat { CompletionTime = v.ResultTime };
                FinalizeRecord(c, v.RunId, v.Score, v.Kills, v.WavesSurvived, v.Damage, v.ResultWeapon, v.ResultTime);
            }
            HoldClientResults();
            return;
        }
        // Resource pickups stay personal: P2's native spawners run their own random items and cooldowns.
        if (v.Phase != SurvivalPhase.Intro && _clientResourcesRun != v.Run) { _clientResourcesRun = v.Run; HordeModePickupSpawner.SpawnAll(); }
        if (OfferSpawner() is { } spawner)
        {
            if (v.Phase == SurvivalPhase.Rest && v.Offers.Length > 0 && _clientOffersFor != (v.Run, v.Wave))
            {
                _clientOffersFor = (v.Run, v.Wave); spawner.ClearSpawnedPickups(); _offerTokens.Clear();
                foreach (var offer in SurvivalProtocol.ParseOffers(v.Offers))
                {
                    if (offer.Slot >= spawner._spawns.Length || spawner._spawns[offer.Slot] == null) throw new InvalidDataException("Host offered an unavailable Survival slot.");
                    spawner.SpawnPickup(OfferPrefab(spawner, offer.Token), spawner._spawns[offer.Slot]);
                }
            }
            else if (v.Phase != SurvivalPhase.Rest && spawner._spawnedPickups.Count > 0) spawner.ClearSpawnedPickups();
        }
        if ((v.Revive & SurvivalProtocol.Partner) != 0 && _life.PlayerState.Pending && _clientRevivedFor != (v.Run, v.Wave))
        { _clientRevivedFor = (v.Run, v.Wave); ReviveLocal(v.Wave, PartnerPose); }
    }

    private void TickSurvivalReadyInput()
    {
        var ready = Client ? (_survivalView?.Ready & SurvivalProtocol.Partner) != 0 : (_ready & SurvivalProtocol.Host) != 0;
        var preparing = Client ? _survivalView is { Phase: SurvivalPhase.Intro or SurvivalPhase.Rest, Ended: false } : !_ended && _prep != null;
        var stack = InputManager.InputStateStack;
        var gameplay = stack != null && stack.Count > 0 && stack[stack.Count - 1] == InputManager.InputState.GAMEPLAY;
        if (!Active || !_life.InSurvival || !preparing || ready || !LocalAlive || !gameplay || _life.Frozen ||
            InputManager.RewiredPlayer?.GetButton("INPUT_Use") != true) { _useHeldSince = -1; return; }
        if (_useHeldSince < 0) _useHeldSince = Time.unscaledTime;
        if (Time.unscaledTime - _useHeldSince < ReadyHoldSeconds || Time.unscaledTime < _readyRequestAt + 1) return;
        _useHeldSince = -1; RequestSurvivalReady();
    }

    // Short preparation/spectator notice. Shown only with a partner, so solo hosting stays native.
    internal string SurvivalBanner(string partner)
    {
        if (!Active || !_life.InSurvival || (Client ? _survivalView : _lastSurvival) is not { } s) return "";
        if (s.Ended) return Client ? "Run over - P1 chooses retry or return to menu" : "";
        var partnerPresent = Client || PartnerParticipant;
        if (SurvivalPatches.Spectating)
            return partnerPresent ? $"Waiting for next wave - watching {partner}  |  Wave {s.Wave}  {Mathf.CeilToInt(Mathf.Max(0, s.TimeLeft))}s  {s.Alive} enemies" : "";
        if (!partnerPresent || s.Phase is not (SurvivalPhase.Intro or SurvivalPhase.Rest)) return "";
        var local = Client ? SurvivalProtocol.Partner : SurvivalProtocol.Host;
        if ((s.Ready & local) != 0) return $"Ready - waiting for {partner}";
        if (_useHeldSince >= 0) return "Getting ready...";
        return s.Phase == SurvivalPhase.Intro ? "Take the shotgun, or hold Use when ready" : "Take an offer, or hold Use to keep your gear";
    }

    private void SendSurvival(bool complete)
    {
        if (Client || Horde() is not { } c) return;
        // Presentation-only countdown, derived from native CombatStart's 3 x 1 s scaled waits.
        var countdown = c._combatCountDownFinished || _countdownStartedAt < 0 ? 0 : Mathf.Clamp(3 - Mathf.FloorToInt(Time.time - _countdownStartedAt), 1, 3);
        var phase = (SurvivalPhase)(int)c._hordeModeState;
        var state = new SurvivalState(_survivalRun, phase, Math.Max(1, c._currentWave), Math.Clamp(c.WavesSurvived, 0, Math.Max(1, c._currentWave)),
            Mathf.Clamp(c._combatTimeLeft, SurvivalProtocol.MinTime, SurvivalProtocol.MaxTime), countdown, c._combatCountDownFinished, Math.Max(0, (double)c._currentScore),
            Mathf.Clamp(c._comboMultiplier, SurvivalProtocol.MinCombo, SurvivalProtocol.MaxCombo), Math.Max(0, c.Kills), Math.Min(c._spawnedNpcs.Count, SurvivalProtocol.MaxAlive), Math.Max(0, (double)c.DamageDealt),
            _prep != null ? _ready : (byte)0, (byte)(Participants & (AliveMask | _revive)), _revive, _ended, phase == SurvivalPhase.Rest ? _offersText : "",
            _runId, _ended ? _resultTime : 0, _ended ? _resultWeapon : "");
        if (!complete && _lastSurvival != null)
        {
            if (state == _lastSurvival) return;
            // Timer and combo decay continuously; other changes (kills, phase, readiness) go at once.
            if (state with { TimeLeft = _lastSurvival.TimeLeft, Combo = _lastSurvival.Combo } == _lastSurvival && Time.unscaledTime < _survivalSentAt + SurvivalTimerCadence) return;
        }
        Send(SurvivalProtocol.ToFrame(state, _epoch)); SurvivalFramesSent++;
        _lastSurvival = state; _survivalSentAt = Time.unscaledTime;
    }

    private void ApplySurvival(WorldFrame frame)
    {
        var state = SurvivalProtocol.FromFrame(frame);
        if (!SurvivalProtocol.Supersedes(_survivalView, state)) return;
        _survivalView = state;
        if (Horde() is not { } c) return;
        c._hordeModeState = (HordeState)(int)state.Phase; c._currentWave = state.Wave; c._combatTimeLeft = state.TimeLeft;
        c._currentScore = (float)Math.Min(state.Score, float.MaxValue); c._comboMultiplier = state.Combo; c._combatCountDownFinished = state.CountdownDone;
    }
}
