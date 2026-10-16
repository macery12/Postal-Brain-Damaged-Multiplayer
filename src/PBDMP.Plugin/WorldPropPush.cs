using HarmonyLib;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using Hyperstrange.PBD.PlayerComponents.PlayerMovement;
using PBDMP.Network;
using UnityEngine;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    private readonly Dictionary<Rigidbody,(Vector3 Delta,float At)> _bodyPushes=new();
    internal bool PushReplicaBodies(float delta,Vector3 velocity)
    {
        if(!Active || !Client || _catalogue==null) return false;
        if(_life.Frozen || Player.Instance?.IsAlive!=true || velocity.sqrMagnitude<.001f) return true;
        delta=Mathf.Clamp(delta,0,.05f);velocity=Vector3.ClampMagnitude(velocity,60);
        var player=Player.Instance;var feet=player.FeetPosition;var direction=velocity.normalized;
        var contacts=Physics.OverlapCapsule(feet+Vector3.up*.35f,feet+Vector3.up*Mathf.Max(.35f,player.Height-.35f),.55f,~0,QueryTriggerInteraction.Ignore);
        var seen=new HashSet<IntPtr>();
        foreach(var collider in contacts.Take(128))
        {
            var body=collider.attachedRigidbody;if(body==null || !seen.Add(body.Pointer) || Vector3.Dot(collider.bounds.center-player.CenterOfMass,direction)<-.1f) continue;
            var prop=body.GetComponentInParent<PhysicalObject>();string? key=null;uint id=0;
            if(prop!=null && SharedPhysics(prop)) {key=_catalogue.Key(prop)??"";id=_physicalIds.GetValueOrDefault(prop.Pointer).Id;}
            else if(_catalogue.FragmentPointers.Contains(body.Pointer)) key=_catalogue.Fragments.First(p=>p.Value.Pointer==body.Pointer).Key;
            if(key==null) continue;
            var old=_bodyPushes.GetValueOrDefault(body);if(Time.unscaledTime-old.At>.1f) old=default;var impulse=Vector3.ClampMagnitude(old.Delta+velocity*delta,20);
            if(Time.unscaledTime-old.At<.05f) {_bodyPushes[body]=(impulse,old.At);continue;}
            _bodyPushes[body]=(Vector3.zero,Time.unscaledTime);
            Send(new(WorldKind.Special,_epoch,1,id,Key:key,Actor:2,Life:_life.Life,Flags:93,
                Position:Net(collider.ClosestPoint(player.CenterOfMass)),Velocity:Net(impulse)));
        }
        return true;
    }
    private void ApplyBodyPush(WorldFrame frame)
    {
        Rigidbody? body=null;
        if(_catalogue!.Fragments.TryGetValue(frame.Key,out var fragment)) body=fragment;
        else if(frame.Key.Length>0 && _catalogue.SceneryKeys.Contains(frame.Key)) body=_catalogue.Components[frame.Key].TryCast<PhysicalObject>()?.Rigidbody;
        else if(frame.Key.Length==0 && _physicalInstances.GetValueOrDefault(frame.Entity) is {} prop) body=prop.Rigidbody;
        if(body==null || body.isKinematic || !body.gameObject.activeInHierarchy || _remote==null) return;
        var contact=Unity(frame.Position);var bounds=_remote.GetComponent<CapsuleCollider>().bounds;bounds.Expand(.7f);
        if(!bounds.Contains(contact) || Vector3.Distance(contact,body.ClosestPointOnBounds(contact))>.5f) return;
        body.WakeUp();body.AddForceAtPosition(Vector3.ClampMagnitude(Unity(frame.Velocity),20),contact,ForceMode.VelocityChange);
    }
}

internal static class WorldPropPushPatches
{
    internal static void Install(Harmony h)=>h.Patch(AccessTools.Method(typeof(PlayerRigidbodyPusher),"PushRigidbodies"),prefix:new(typeof(WorldPropPushPatches),nameof(Push)));
    private static bool Push(float __0,Vector3 __1)=>SharedWorld.Current?.PushReplicaBodies(__0,__1)!=true;
}
