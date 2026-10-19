using System.Text;
using Hyperstrange.Base;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using Hyperstrange.PBD.UI.Menus;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;
using HordeState = Hyperstrange.PBD.HordeModeController.HordeModeStateEnum;

namespace PBDMP.Plugin;

// Phase 0 Survival research fixture. Offline only: it never hosts a session. It records the
// installed build's HORDE levels, authored controller/spawner bindings and one native solo
// phase/offer/end flow. Profile/metadata writes stay blocked through SessionPatches.Protect.
public sealed partial class MultiplayerBehaviour
{
    private SurvivalInventory? _survivalInventory;
    private void RunSurvivalInventory() => (_survivalInventory ??= new()).Tick();
}

internal sealed class SurvivalInventory
{
    internal static bool Enabled => Environment.GetEnvironmentVariable("PBDMP_SURVIVAL_INVENTORY") == "1";
    private readonly string _output = Environment.GetEnvironmentVariable("PBDMP_SURVIVAL_OUTPUT") ?? throw new InvalidOperationException("PBDMP_SURVIVAL_OUTPUT is required.");
    private readonly List<string> _events = new();
    private LevelId[]? _arenas;
    private string _observe = "";
    private int _index, _step, _observeStep;
    private float _at, _deadline, _started, _waveTimeout;
    private bool _continued, _done;
    private string _last = "";
    private float _lastTimerLog;
    private int _killed, _blockedBefore;
    private string _pickupRoute = "";

    internal void Tick()
    {
        try { Step(); }
        catch { Write("events.tsv", _events); throw; }
    }

    private void Step()
    {
        if (_done) return;
        var now = Time.unscaledTime;
        if (_arenas == null)
        {
            if (MenuManager.MainCanvas == null || now < 5) return;
            _arenas = Inventory();
            if (_arenas.Length == 0) throw new InvalidOperationException("No base HORDE levels were found.");
            _observe = Environment.GetEnvironmentVariable("PBDMP_SURVIVAL_OBSERVE") ?? _arenas[0].name;
            Load(); return;
        }
        if (now > _deadline) throw new TimeoutException($"Survival inventory timed out at {_arenas[_index].name}, step {_step}/{_observeStep}.");
        switch (_step)
        {
            case 0: // native loading continuation, as a solo player would press a key
                var loading = Object.FindObjectOfType<LoadingMenu>();
                if (!_continued && loading != null && loading.gameObject.activeInHierarchy && loading._sceneLoaded) { _continued = true; loading.OnAnyButtonDown(); }
                if (MultiplayerBehaviour.DoorSoloGate && LevelController._instance != null) { MultiplayerBehaviour.DoorSoloGate = false; LevelController._instance.OnAnyButtonDown(); }
                var active = LevelController._instance?.LevelId;
                if (active == null || active.ID != _arenas[_index].ID || Player.Instance == null || !Player.Instance.gameObject.activeInHierarchy || !Player.Instance.IsAlive) return;
                if (_at == 0) { _at = now + 2; return; }
                if (now < _at) return;
                Dump(_arenas[_index]);
                if (_arenas[_index].name == _observe) { _step = 1; _observeStep = 0; _started = now; _at = 0; _deadline = now + 600; Record("observe-start"); }
                else Next();
                return;
            case 1:
                Observe(now); return;
            case 2: // title between arenas so native pools are torn down
                if (SessionManager.SessionActive) return;
                if (_at == 0) { _at = now + 1; return; }
                if (now < _at) return;
                _index++;
                if (_index == _arenas.Length) Finish(); else Load();
                return;
        }
    }

    private LevelId[] Inventory()
    {
        var rows = new List<string> { "name\tID\tid\ttype\tmod\tdlc\tepisode\tsceneCluster\tunlockedByDefault\thordeScore\thordeWaves" };
        var arenas = new List<LevelId>();
        foreach (var level in LevelManager.AllLevels)
        {
            if (level == null) continue;
            rows.Add($"{level.name}\t{level.ID}\t{level.Id}\t{level.Type}\t{level.IsMod}\t{level.DlcCategory}\t{level.EpisodeId?.name}\t{level._sceneCluster?.name}\t{level._unlockedByDefault}\t{level.HordeScore}\t{level.HordeWavesSurvived}");
            if (level.Type == LevelId.TypeEnum.HORDE && !level.IsMod && level.DlcCategory == DLCCategory.NONE && !level.name.StartsWith("LEVEL_CustomMaps_", StringComparison.Ordinal)) arenas.Add(level);
        }
        Write("levels.tsv", rows);
        var selected = Environment.GetEnvironmentVariable("PBDMP_SURVIVAL_LEVELS");
        if (!string.IsNullOrEmpty(selected)) arenas = arenas.Where(l => selected.Split(',').Contains(l.name)).ToList();
        Write("arenas.txt", arenas.Select(l => l.name));
        Plugin.Logger.LogInfo($"SURVIVAL INVENTORY: {arenas.Count} base HORDE arena(s): {string.Join(", ", arenas.Select(l => l.name))}");
        return arenas.ToArray();
    }

    private void Load()
    {
        _step = 0; _at = 0; _continued = false; _deadline = Time.unscaledTime + 120;
        GameManager.LoadLevel(_arenas![_index], true);
    }

    private void Next() { _step = 2; _at = 0; _deadline = Time.unscaledTime + 60; GameManager.OpenTitle(); }

    private void Finish()
    {
        Write("events.tsv", _events);
        Plugin.Logger.LogInfo($"PBDMP SURVIVAL INVENTORY PASS: {_arenas!.Length} arena(s), observed {_observe}, pickup route {_pickupRoute}.");
        _done = true;
    }

    private void Dump(LevelId level)
    {
        var lines = new List<string> { $"level\t{level.name}\t{level.ID}\tscene={LevelController._instance!.gameObject.scene.name}" };
        var controllers = Object.FindObjectsOfType<HordeModeController>(true);
        lines.Add($"controllers\t{controllers.Length}");
        foreach (var c in controllers)
        {
            lines.Add($"controller\t{Path(c.transform)}\tstate={c._hordeModeState}\twave={c._currentWave}\ttime={c._combatTimeLeft}\tscore={c._currentScore}\tcombo={c._comboMultiplier}\tspawnTransforms={c._spawnTransforms?.Length}\tparentIsLevel={c.transform.IsChildOf(LevelController._instance.transform)}");
            if (c._spawnTransforms != null) foreach (var t in c._spawnTransforms) lines.Add($"npcSpawn\t{Path(t)}\t{V(t.position)}\tflyingHeight=4");
            var m = c._hordeModeControllerModel;
            if (m == null) { lines.Add("model\tnull"); continue; }
            lines.Add($"model\t{m.name}\tinitialPower={m._initialSpawnPower}\tpowerIncrease={m._spawnPowerIncrease}\tpowerExponent={m._spawnPowerIncreasePower}\tinitialDuration={m._initialWaveDuration}\tdurationIncrease={m._waveDurationIncrease}\tdurationExponent={m._waveDurationIncreasePower}\twaveToReachAll={m._waveToReachAllNpcs}");
            for (var wave = 1; wave <= 10; wave++) lines.Add($"wave\t{wave}\tduration={m.GetWaveDuration(wave)}\tspawnPower={m.GetSpawnPower(wave)}");
            foreach (var npc in m.Npcs) lines.Add($"modelNpc\t{npc?.name}\t{npc?.ID}\tpoints={npc?.HordeModePoints}\ttype={npc?._npcPrefab?.GetComponent<Npc>()?.GetIl2CppType().Name}");
        }
        foreach (var w in Object.FindObjectsOfType<HordeModeWeaponSpawner>(true))
        {
            lines.Add($"weaponSpawner\t{Path(w.transform)}\tspawns={w._spawns?.Length}\tweapons={w._weaponPickups?.Length}\titems={w._itemPickups?.Length}\tsaveable=native-empty");
            if (w._spawns != null) foreach (var t in w._spawns) lines.Add($"offerSpawn\t{Path(t)}\t{V(t.position)}");
            if (w._weaponPickups != null) foreach (var p in w._weaponPickups) lines.Add($"offerWeapon\t{p?.name}\tmain={p?.MainItem?.name}\t{p?.MainItem?.ID}\t{p?.GetIl2CppType().Name}");
            if (w._itemPickups != null) foreach (var p in w._itemPickups) lines.Add($"offerItem\t{p?.name}\tmain={p?.MainItem?.name}\t{p?.MainItem?.ID}\t{p?.GetIl2CppType().Name}");
        }
        foreach (var s in Object.FindObjectsOfType<HordeModePickupSpawner>(true))
        {
            lines.Add($"pickupSpawner\t{Path(s.transform)}\t{V(s.transform.position)}\tfirst={s._firstPickup?.name}\tcooldown={s._cooldown}\trepeat={s._allowSpawnedItemRepetition}\tonDemand={s._spawnOnDemand}\tcurrent={s._currentPickupInstance?.name}\tfirstDone={s._firstPickupHappened}");
            if (s._availablePickups != null) foreach (var p in s._availablePickups) lines.Add($"spawnerPickup\t{Path(s.transform)}\t{p?.name}\tmain={p?.MainItem?.name}\t{p?.MainItem?.ID}");
            var e = s._onFirstPickupEvent;
            if (e != null) for (var i = 0; i < e.GetPersistentEventCount(); i++)
                lines.Add($"firstPickupListener\t{Path(s.transform)}\t{e.GetPersistentTarget(i)?.GetIl2CppType().Name}\t{e.GetPersistentTarget(i)?.name}\t{e.GetPersistentMethodName(i)}");
        }
        foreach (var (owner, field, e) in Events())
            for (var i = 0; i < e.GetPersistentEventCount(); i++)
                lines.Add($"listener\t{owner.GetIl2CppType().Name}\t{Path(owner.transform)}\t{field}\t{e.GetPersistentTarget(i)?.GetIl2CppType().Name}\t{e.GetPersistentTarget(i)?.name}\t{e.GetPersistentMethodName(i)}");
        foreach (var l in Object.FindObjectsOfType<InteractListener>(true))
        {
            var source = l._interactable?.TryCast<MonoBehaviour>();
            lines.Add($"interactListener\t{Path(l.transform)}\tevery={l._everyInteraction}\tsource={source?.GetIl2CppType().Name}\t{(source == null ? "" : Path(source.transform) + "\t" + V(source.transform.position))}");
        }
        foreach (var name in new[] { "LostClown", "ExplosiveBarrel", "HordeTeaserController", "PlayerHordeModeView", "SpawnPoint", "SpawnPointsController", "PlayerSpawnPoint", "Door", "PlayerTriggerCheckpointComponent" })
            lines.Add($"special\t{name}\t{Object.FindObjectsOfType<MonoBehaviour>(true).Count(c => c.GetIl2CppType().Name == name)}");
        Write("arena-" + level.name + ".tsv", lines);
        Write("census-" + level.name + ".tsv", Object.FindObjectsOfType<MonoBehaviour>(true).GroupBy(c => c.GetIl2CppType().Name).OrderBy(g => g.Key).Select(g => g.Key + "\t" + g.Count()));
        Plugin.Logger.LogInfo("SURVIVAL INVENTORY ARENA: " + level.name);
    }

    // One native solo phase cycle. Actions are labelled: player contact/Interact are native
    // pickup paths; Npc.Kill and Player.Kill are direct native calls used to bound the run.
    private void Observe(float now)
    {
        var c = Object.FindObjectOfType<HordeModeController>();
        if (c == null) throw new InvalidOperationException("Observed arena has no HordeModeController.");
        Sample(c, now);
        var player = Player.Instance;
        // Native first-use tutorials stop scaled time until Use; dismiss them as a player would.
        if (player.GetComponent<Hyperstrange.PBD.PlayerComponents.PlayerTutorialComponent>() is { _isTutorialShown: true } tutorial)
        { Record($"tutorial-dismissed\tstate={c._hordeModeState}"); tutorial.OnUseDown(); return; }
        switch (_observeStep)
        {
            case 0: // idle at load: confirm the run does not start by itself
                if (c._hordeModeState == HordeState.COMBAT) { _pickupRoute = "auto"; Record("combat-started-by\tauto"); _observeStep = 2; return; }
                if (now - _started < 10) return;
                Record("idle-10s");
                var start = BeginSource();
                if (start == null) throw new InvalidOperationException("No authored interaction or first pickup starts the run.");
                var target = start.TryCast<MonoBehaviour>()!;
                Record($"begin-source\t{target.GetIl2CppType().Name}\t{Path(target.transform)}\t{V(target.transform.position)}\tcanInteract={start.CanBeInteractedWith(player)}");
                player.Teleport(target.transform.position + Vector3.up * 0.1f, player.transform.rotation);
                _at = now + 3; _observeStep = 1; return;
            case 1:
                if (c._hordeModeState == HordeState.COMBAT) { if (_pickupRoute.Length == 0) _pickupRoute = "contact"; Record("combat-started-by\t" + _pickupRoute); _observeStep = 2; _waveTimeout = now + Math.Min(300, c._combatTimeLeft + 90); return; }
                if (now < _at) return;
                if (_pickupRoute.Length > 0) throw new InvalidOperationException("Native interaction did not start combat.");
                var source = BeginSource()!;
                _pickupRoute = "interact:" + source.TryCast<MonoBehaviour>()!.GetIl2CppType().Name;
                Record($"begin-interact\tcanInteract={source.CanBeInteractedWith(player)}"); source.Interact(player); _at = now + 8; return;
            case 2: // combat: remove each registered enemy so the observation cannot end by death
                if (_waveTimeout == 0) _waveTimeout = now + Math.Min(300, c._combatTimeLeft + 90);
                for (var i = c._spawnedNpcs.Count - 1; i >= 0; i--)
                {
                    var npc = c._spawnedNpcs[i];
                    if (npc == null || !npc.IsAlive) continue;
                    Record($"npc-registered\t{npc.NpcId?.name}\t{V(npc.transform.position)}\tdistance={Vector3.Distance(npc.transform.position, player.transform.position):0.0}\ttarget={(npc.Target == null ? "none" : npc.Target.TryCast<Player>() != null ? "local-player" : "other")}");
                    npc.Kill(true); _killed++;
                }
                if (c._hordeModeState == HordeState.REST) { Record("rest-reached"); _at = now + 3; _observeStep = 3; return; }
                if (now > _waveTimeout) throw new TimeoutException("Wave did not reach REST.");
                return;
            case 3: // rest: record offers and whether the timer runs
                if (now < _at) return;
                var offers = Offers();
                Record("offers\t" + string.Join(",", offers.Select(p => p.name + "@" + V(p.transform.position))));
                if (offers.Count == 0) throw new InvalidOperationException("REST produced no weapon/item offers.");
                Record("offer-contact\t" + offers[0].name);
                player.Teleport(offers[0].transform.position, player.transform.rotation);
                _at = now + 8; _observeStep = 4; return;
            case 4:
                if (c._hordeModeState == HordeState.COMBAT)
                {
                    Record($"offer-started-combat\tremainingOffers={Offers().Count}\twave={c._currentWave}");
                    _at = now + 3; _observeStep = 5; return;
                }
                if (now < _at) return;
                var remaining = Offers();
                if (remaining.Count == 0) throw new InvalidOperationException("Offer disappeared without starting combat.");
                Record("offer-interact\t" + remaining[0].name); remaining[0].Interact(player); _at = now + 5; return;
            case 5: // end path: native player death ends the solo run
                if (now < _at) return;
                _blockedBefore = SessionPatches.BlockedSaves;
                Record("player-kill"); player.Kill(); _at = now + 20; _observeStep = 6; return;
            case 6:
                var end = Object.FindObjectOfType<EndHordeMenu>();
                if (end != null && end.gameObject.activeInHierarchy)
                {
                    Record($"end-horde-menu\tblockedProfileWrites={SessionPatches.BlockedSaves - _blockedBefore}\tkills={c.Kills}\twavesSurvived={c.WavesSurvived}\tscore={c._currentScore}\tstate={c._hordeModeState}");
                    if (SessionPatches.BlockedSaves == _blockedBefore) throw new InvalidOperationException("Run end did not reach the blocked metadata/profile write.");
                    Write("events.tsv", _events); Next(); return;
                }
                if (now > _at) throw new TimeoutException("Native death did not open the Survival result menu.");
                return;
        }
    }

    // The authored source whose persistent listener calls HordeModeController.Begin.
    private static IInteractable? BeginSource()
    {
        foreach (var l in Object.FindObjectsOfType<InteractListener>())
        {
            var e = l._onInteractEvents;
            if (e == null || l._interactable == null) continue;
            for (var i = 0; i < e.GetPersistentEventCount(); i++)
                if (e.GetPersistentMethodName(i) == "Begin" && e.GetPersistentTarget(i)?.TryCast<HordeModeController>() != null) return l._interactable;
        }
        foreach (var s in Object.FindObjectsOfType<HordeModePickupSpawner>()) if (s._currentPickupInstance != null && s._firstPickup != null && !s._firstPickupHappened) return s._currentPickupInstance.TryCast<IInteractable>();
        return null;
    }

    private static IEnumerable<(MonoBehaviour Owner, string Field, UnityEventBase Event)> Events()
    {
        var scene = LevelController._instance!.gameObject.scene.handle;
        var eventType = Il2CppType.Of<UnityEventBase>();
        var flags = Il2CppSystem.Reflection.BindingFlags.Instance | Il2CppSystem.Reflection.BindingFlags.Public | Il2CppSystem.Reflection.BindingFlags.NonPublic | Il2CppSystem.Reflection.BindingFlags.DeclaredOnly;
        foreach (var c in Object.FindObjectsOfType<MonoBehaviour>(true))
        {
            if (c.gameObject.scene.handle != scene) continue;
            for (var t = c.GetIl2CppType(); t != null && t.FullName != "UnityEngine.MonoBehaviour"; t = t.BaseType)
                foreach (var f in t.GetFields(flags))
                    if (eventType.IsAssignableFrom(f.FieldType) && f.GetValue(c)?.TryCast<UnityEventBase>() is { } e) yield return (c, f.Name, e);
        }
    }

    private static List<Pickup> Offers()
    {
        var result = new List<Pickup>();
        foreach (var w in Object.FindObjectsOfType<HordeModeWeaponSpawner>())
            foreach (var p in w._spawnedPickups) if (p != null && p.gameObject.activeInHierarchy) result.Add(p);
        return result;
    }

    private void Sample(HordeModeController c, float now)
    {
        var resources = string.Join(",", Object.FindObjectsOfType<HordeModePickupSpawner>().Select(s => s._currentPickupInstance?.name ?? "-"));
        var key = $"{c._hordeModeState}|{c._currentWave}|{c._spawnedNpcs.Count}|{c.Kills}|{c._combatCountDownFinished}|{resources}|{Offers().Count}";
        if (key == _last && now - _lastTimerLog < 5) return;
        _last = key; _lastTimerLog = now;
        Record($"sample\tstate={c._hordeModeState}\twave={c._currentWave}\ttime={c._combatTimeLeft:0.00}\tcountdownDone={c._combatCountDownFinished}\tspawned={c._spawnedNpcs.Count}\tusedPower={c._usedSpawnPower:0.##}\tscore={c._currentScore:0.##}\tcombo={c._comboMultiplier:0.##}\tkills={c.Kills}\twavesSurvived={c.WavesSurvived}\tresources={resources}\toffers={Offers().Count}\tscale={Time.timeScale}");
    }

    private void Record(string line) => _events.Add($"{Time.unscaledTime - _started:0.00}\t{line}");
    private void Write(string name, IEnumerable<string> lines) => File.WriteAllLines(System.IO.Path.Combine(_output, name), lines, new UTF8Encoding(false));
    private static string V(Vector3 v) => $"{v.x:0.##},{v.y:0.##},{v.z:0.##}";
    private static string Path(Transform t) => WorldCatalogue.PathOf(t);
}
