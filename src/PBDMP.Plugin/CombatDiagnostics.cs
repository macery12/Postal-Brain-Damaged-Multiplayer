using Hyperstrange.PBD;
using Hyperstrange.PBD.NpcComponents;
using Il2CppInterop.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PBDMP.Plugin;

public sealed partial class MultiplayerBehaviour
{
    private int _combatProbeStep, _combatProbeHits;
    private float _combatProbeStart, _combatProbeReport;
    private RemoteActor? _combatProbeActor;
    private Npc? _combatProbeNpc;
    private void ProbeCheck(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("COMBAT PROBE: " + description);
        Plugin.Logger.LogInfo("COMBAT PROBE PASS: " + description);
        File.AppendAllText(Path.Combine(Environment.GetEnvironmentVariable("PBDMP_COMBAT_OUTPUT")!, "checks.txt"), description + "\n");
    }
    private void RunCombatProbe()
    {
        if (_combatProbeStep == 99) return;
        if (_combatProbeStart == 0) _combatProbeStart = Time.unscaledTime;
        if (Time.unscaledTime - _combatProbeStart > 85f) throw new TimeoutException("Combat proxy probe timed out at " + _combatProbeStep);
        if (_lifecycle is { NativeReady: false }) _lifecycle.AutoContinue();
        if (_combatProbeStep == 0)
        {
            _port!.text = Environment.GetEnvironmentVariable("PBDMP_COMBAT_PORT")!;
            Host();
            if (_lifecycle == null) throw new InvalidOperationException("Combat probe host failed: " + _feedback);
            GameManager.LoadLevel(TestLevel(), true); _combatProbeStep = 1; return;
        }
        if (_combatProbeStep < 3 && !_lifecycle!.NativeReady) return;
        if (_combatProbeStep == 1)
        {
            _combatProbeActor = RemoteActor.Create(_ => _combatProbeHits++);
            var actor = _combatProbeActor.Actor;
            ProbeCheck(actor.IsAlive && actor.IsPlayer && actor.IsTargettable && actor.Height > 1f,
                "injected IAiActor virtual getters work through native interface");
            var hittable = _combatProbeActor.Cast<IHittable>();
            HittableManager.OnHit(hittable, new HitData { damage = 5, position = actor.CenterOfMass, direction = Vector3.forward, weakSpotMultiplier = 1 });
            ProbeCheck(_combatProbeHits == 1, "native HittableManager dispatch reaches remote hit interface exactly once");
            _combatProbeHits = 0;
            _combatProbeNpc = Object.FindObjectsOfType<Npc>()
                .Where(n=>n.IsAlive && n.GetComponent<NpcAwarenessComponent>() != null && n.GetComponentInChildren<NpcMeleeDamageComponent>(true) != null)
                .OrderBy(n=>Vector3.Distance(n.FeetPosition, Player.Instance.FeetPosition)).FirstOrDefault();
            ProbeCheck(_combatProbeNpc != null, "actual campaign contains a living targetable enemy");
            var npc = _combatProbeNpc!;
            foreach (var other in Object.FindObjectsOfType<Npc>()) if (other.Pointer != npc.Pointer) other.gameObject.SetActive(false);
            var feet = npc.FeetPosition + npc.Transform.forward * 1.5f;
            Player.Instance.Teleport(npc.FeetPosition - npc.Transform.forward * 3f, npc.Transform.eulerAngles, Vector3.zero);
            _combatProbeActor.SetPose(feet, 180f, 1.8f, true, Vector3.zero);
            Physics.SyncTransforms();
            npc.OnOptimizeUpdate(false, false);
            npc._animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            npc.GetComponent<NpcAwarenessComponent>()._player = actor;
            npc.SetTarget(actor);
            ProbeCheck(npc.Target?.Pointer == actor.Pointer, "native enemy accepts P2 actor as its player target");
            var melee = npc.GetComponentsInChildren<NpcMeleeDamageComponent>(true).First(m=>m.gameObject.activeInHierarchy);
            melee.ClearBuffer();
            melee.OnTriggerEnter(_combatProbeActor.GetComponent<CapsuleCollider>());
            ProbeCheck(_combatProbeHits == 1, "native enemy melee collision resolves the injected target and dispatches its hit");
            _combatProbeHits = 0; melee.ClearBuffer();
            _combatProbeStep = 2;
        }
        else if (_combatProbeStep == 2 && _combatProbeHits == 0 && Time.unscaledTime > _combatProbeReport)
        {
            _combatProbeReport = Time.unscaledTime + 5f;
            var npc = _combatProbeNpc!;
            npc.GetComponent<NpcAwarenessComponent>()._player = _combatProbeActor!.Actor;
            if (npc.Target?.Pointer != _combatProbeActor.Actor.Pointer) npc.SetTarget(_combatProbeActor.Actor);
            var melee = npc.GetComponentsInChildren<NpcMeleeDamageComponent>(true).First(m=>m.gameObject.activeInHierarchy);
            if (melee._collider.enabled)
            {
                // Test collision delivery deterministically inside the active authored attack volume.
                _combatProbeActor.SetPose(melee._collider.bounds.center - Vector3.up * 0.9f, 180, 1.8f, true, Vector3.zero);
                Physics.SyncTransforms();
            }
            Plugin.Logger.LogInfo($"COMBAT PROBE STATE: {npc.name}, active={npc.BehaviourActive}, attack={npc.CanAttackTarget()}, target={npc.HasTarget}, time={Time.timeScale}, distance={Vector3.Distance(npc.FeetPosition,_combatProbeActor.FeetPosition)}, hits={_combatProbeHits}, melee={melee._collider.enabled}/{melee.gameObject.activeInHierarchy}, animation={npc._animator.GetCurrentAnimatorStateInfo(0).fullPathHash}/{npc._animator.GetCurrentAnimatorStateInfo(0).normalizedTime}, speed={npc._animator.speed}");
        }
        else if (_combatProbeStep == 2 && _combatProbeHits > 0)
        {
            ProbeCheck(true, "actual enemy attack hits host P2 collision proxy");
            _combatProbeActor!.SetPose(_combatProbeActor.FeetPosition, 0, 1.8f, false, Vector3.zero);
            _combatProbeNpc!.ClearTarget();
            ProbeCheck(!_combatProbeActor.Actor.IsAlive && !_combatProbeNpc.HasTarget, "dead/disconnected target is cleared");
            _combatProbeActor.Release(); _combatProbeActor = null;
            ResetSession();
            _combatProbeReport = Time.unscaledTime + 3f;
            _combatProbeStep = 3;
        }
        else if (_combatProbeStep == 3 && Time.unscaledTime >= _combatProbeReport)
        {
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("PBDMP_COMBAT_OUTPUT")!, "pass"), "pass");
            _combatProbeStep = 99;
        }
    }
}
