using Hyperstrange.PBD;
using Hyperstrange.PBD.NpcComponents;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PBDMP.Plugin;

// This is a host-side target/hit surface. It never owns local inputs, a camera or inventory.
[Il2CppImplements(typeof(IAiActor), typeof(IHittable))]
public sealed class RemoteActor : MonoBehaviour
{
    private bool _alive;
    private float _height = 1.8f;
    private Vector3 _velocity;
    private HitData _lastHit = new();
    private CapsuleCollider? _capsule;
    private Transform? _aim;
    private System.Action<HitData>? _received;
    private System.Action<IAiActor,bool>? _contacted;

    public RemoteActor(IntPtr pointer) : base(pointer) { }
    public Transform ReferenceTransform => _aim ?? transform;
    public Transform Transform => transform;
    public HitData LastHitData => _lastHit;
    public Vector3 FeetPosition => transform.position;
    public Vector3 CenterOfMass => transform.position + Vector3.up * (_height * 0.5f);
    public Vector3 Forward => transform.forward;
    public Vector3 Right => transform.right;
    public Vector3 Velocity => _velocity;
    public float Radius => 0.35f;
    public float Height => _height;
    public bool IsAlive => _alive;
    public bool IsPlayer => true;
    public bool IsTargettable => _alive;
    public bool CanBeTargetedByAutoProjectiles => _alive;
    public bool PoolProjectileOnHit => true;
    public void OnHitOtherActor(IAiActor actorThatWasHit) => _contacted?.Invoke(actorThatWasHit,false);
    public void OnKilledOtherActor(IAiActor actorThatWasKilled) => _contacted?.Invoke(actorThatWasKilled,true);
    public void Hit(HitData data)
    {
        if (!_alive) return;
        _lastHit = data;
        _received?.Invoke(data);
    }

    [HideFromIl2Cpp]
    internal static RemoteActor Create(System.Action<HitData> received,System.Action<IAiActor,bool>? contacted=null)
    {
        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<RemoteActor>())
            ClassInjector.RegisterTypeInIl2Cpp<RemoteActor>();
        var root = new GameObject("PBDMP_RemoteActor");
        root.layer = Player.Instance.Collider.gameObject.layer;
        root.tag = Player.Instance.Collider.gameObject.tag;
        var actor = root.AddComponent<RemoteActor>();
        actor._received = received;
        actor._contacted = contacted;
        actor._aim = new GameObject("Aim").transform; actor._aim.SetParent(root.transform,false);
        // Static colliders do not generate trigger pairs with animated static attack colliders.
        var body = root.AddComponent<Rigidbody>();
        body.isKinematic = true; body.useGravity = false;
        actor._capsule = root.AddComponent<CapsuleCollider>();
        actor._capsule.radius = actor.Radius;
        if (Player.Instance.Collider != null)
            Physics.IgnoreCollision(actor._capsule, Player.Instance.Collider, true);
        actor.SetPose(Player.Instance.FeetPosition + Vector3.right * 2f, 0f, 1.8f, true, Vector3.zero);
        return actor;
    }

    [HideFromIl2Cpp]
    internal void SetPose(Vector3 feet, float yaw, float height, bool alive, Vector3 velocity)
    {
        _height = Mathf.Clamp(height, 0.5f, 3f); _alive = alive; _velocity = velocity;
        transform.SetPositionAndRotation(feet, Quaternion.Euler(0f, yaw, 0f));
        if (_aim != null) _aim.localPosition=Vector3.up * (_height * 0.85f);
        if (_capsule != null)
        { _capsule.height = _height; _capsule.center = Vector3.up * (_height * 0.5f); _capsule.enabled = alive; }
    }

    [HideFromIl2Cpp]
    internal IAiActor Actor => this.Cast<IAiActor>();
    [HideFromIl2Cpp]
    internal void Release()
    {
        _alive = false; _received = null; _contacted=null;
        var actorPointer = Pointer;
        var local = Player.Instance != null ? Player.Instance.Cast<IAiActor>() : null;
        foreach (var npc in Resources.FindObjectsOfTypeAll<Npc>())
        {
            if (!npc.gameObject.scene.IsValid()) continue;
            var awareness = npc.GetComponent<NpcAwarenessComponent>();
            if (awareness != null && awareness._player?.Pointer == actorPointer) awareness._player = local;
            var targets = npc.TargetProcessor;
            if (targets == null || targets.Target?.Pointer != actorPointer && targets.LastTarget?.Pointer != actorPointer && targets.InFightTarget?.Pointer != actorPointer) continue;
            npc.ClearTarget(); targets.Reset();
            if (npc.IsAlive && local?.IsAlive == true) npc.SetTarget(local);
        }
        Object.Destroy(gameObject);
    }
}
