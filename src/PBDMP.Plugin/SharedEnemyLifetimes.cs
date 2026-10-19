using Hyperstrange.PBD;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PBDMP.Plugin;

// Enemy life identities are never reused: P1 gives a pooled enemy a new id each time it is
// enabled again. Endless pooling (Survival) therefore needs each retired life's state to be
// dropped on both PCs; otherwise per-id ledgers and P2's replicas grow with every respawn.
internal sealed partial class SharedWorld
{
    private readonly HashSet<string> _reportedPresentationMismatches = new();
    internal int RetiredReplicasForTest { get; private set; }
    internal string JoinStateForTest => $"matched={_peerMatched} snapshot={_snapshotApplied} outbound={_outbound.Count} capture={_stateCapture != null} persistent={_persistentFrames != null} epoch={_epoch}";

    // Drops every per-life ledger entry of one enemy id (both roles).
    private void ForgetEnemy(uint id)
    {
        _enemies.Remove(id); _lastEnemy.Remove(id); _revisions.Remove(id); _snapshotEnemies.Remove(id);
        _enemyParts.Remove(id); _actorParts.Remove(id); _actorMotion.Remove(id); _actorDeaths.Remove(id);
        foreach (var key in _lastParts.Keys.Where(k => k.Id == id).ToArray()) _lastParts.Remove(key);
        foreach (var key in _actorStates.Keys.Where(k => k.Item1 == id).ToArray()) _actorStates.Remove(key);
    }

    private bool OwnedReplica(Npc npc, out int index)
    {
        index = -1;
        for (var i = 0; i < _ownedEnemies.Count; i++)
            if (_ownedEnemies[i] != null && _ownedEnemies[i].Pointer == npc.gameObject.Pointer) { index = i; return true; }
        return false;
    }

    // P2: P1 deactivated this life, which never becomes active again under the same id.
    // Destroy a replica PBDMP instantiated (authored scene enemies are only hidden), after
    // removing every cache that refers to its components or native addresses.
    private bool RetireReplica(uint id, Npc npc)
    {
        if (!Client || !OwnedReplica(npc, out var index)) return false;
        var root = _ownedEnemies[index];
        foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true)) { _behaviours.Remove(behaviour); _encounterBehaviours.Remove(behaviour); }
        foreach (var agent in root.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) _agents.Remove(agent);
        foreach (var body in root.GetComponentsInChildren<Rigidbody>(true)) _actorBodies.Remove(body);
        foreach (var animator in root.GetComponentsInChildren<Animator>(true))
        { _rootMotion.Remove(animator); _animatorEvents.Remove(animator); _actorAnimatorSchemas.Remove(animator.Pointer); }
        _observedActors.Remove(npc.Pointer);
        _actorAudioComponents.RemoveAll(entry => entry.Owner == null || entry.Owner.Pointer == npc.Pointer);
        foreach (var pair in _actorSoundSources.ToArray())
        {
            pair.Value.RemoveAll(source => source == null || source.Pointer == npc.Pointer);
            if (pair.Value.Count == 0) _actorSoundSources.Remove(pair.Key);
        }
        _enemyIds.Remove(npc.Pointer);
        ForgetEnemy(id);
        _ownedEnemies.RemoveAt(index);
        Object.Destroy(root);
        RetiredReplicasForTest++;
        return true;
    }

    // A runtime-only child on P1's pooled instance (or a native part created differently on
    // P2) has no counterpart on P2's replica. It is presentation only: report it once and
    // keep the session, instead of treating it as a protocol violation.
    private void ReportPresentationMismatch(uint id, string key)
    {
        var asset = _lastEnemy.TryGetValue(id, out var frame) ? frame.Asset : "unknown";
        if (_reportedPresentationMismatches.Count < 32 && _reportedPresentationMismatches.Add(asset + "|" + key))
            Plugin.Logger.LogWarning($"Enemy presentation part has no local counterpart and was ignored: enemy {asset}, part {key[..Math.Min(12, key.Length)]}.");
    }
}
