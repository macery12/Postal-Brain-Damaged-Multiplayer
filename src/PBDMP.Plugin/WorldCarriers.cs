using Hyperstrange.PBD;
using PBDMP.Network;
using UnityEngine;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    // Capture with the pose, so a world-space position can never be paired with
    // another packet's platform context. Save capture is allowed while frozen.
    internal PlayerCarrier? CaptureCarrier()
    {
        var player=Player.Instance;
        if(!Active || player?.IsAlive!=true || !_life.PersonalReady) return null;
        foreach(var platform in _catalogue!.Platforms)
        {
            if(platform==null || !platform.gameObject.activeInHierarchy || platform._transformToFollow==null ||
                !_catalogue.CarrierKeys.TryGetValue(platform.Pointer,out var key) ||
                !CarrierOverlaps(platform,player.Collider.bounds)) continue;
            var carrier=new PlayerCarrier(key,Net(platform._transformToFollow.InverseTransformPoint(player.FeetPosition)));
            return carrier;
        }
        return null;
    }
    private bool CarrierOverlaps(MovingPlatformTrigger platform,Bounds body)
        => _catalogue?.CarrierContacts.TryGetValue(platform.Pointer,out var contacts)==true &&
            contacts.Any(c=>c!=null && c.enabled && c.gameObject.activeInHierarchy && c.bounds.Intersects(body));

    internal Vector3 ResolvePoseFeet(PlayerPose pose)
    {
        var feet=new Vector3(pose.X,pose.Y,pose.Z);
        if(!pose.Alive || !TryCarrierFeet(pose.Carrier,pose.Height,out var carried) || (carried-feet).sqrMagnitude>4096) return feet;
        return carried;
    }
    private Vector3? CurrentRemoteCarrierFeet()
    {
        var pose=_session.Snapshot.RemotePose;
        return pose is {Alive:true,Ready:true,Carrier:not null} && pose.Epoch==_epoch && TryCarrierFeet(pose.Carrier,pose.Height,out var feet) &&
            (feet-new Vector3(pose.X,pose.Y,pose.Z)).sqrMagnitude<=4096?feet:null;
    }
    private Transform? CurrentRemoteCarrierTransform()
    {
        var pose=_session.Snapshot.RemotePose;
        if(CurrentRemoteCarrierFeet()==null || pose?.Carrier==null ||
            !_catalogue!.CarrierPlatforms.TryGetValue(pose.Carrier.Key,out var platform)) return null;
        var follow=platform._transformToFollow;var scale=follow.lossyScale;
        return Mathf.Abs(scale.x)>.0001f && Mathf.Abs(scale.y)>.0001f && Mathf.Abs(scale.z)>.0001f?follow:null;
    }
    private bool TryCarrierFeet(PlayerCarrier? carrier,float height,out Vector3 feet)
    {
        feet=default;
        if(carrier==null || _catalogue==null || !_catalogue.CarrierPlatforms.TryGetValue(carrier.Key,out var platform) ||
            platform==null || !platform.gameObject.activeInHierarchy || platform._transformToFollow==null) return false;
        feet=platform._transformToFollow.TransformPoint(Unity(carrier.Offset));
        // A valid identifier does not grant arbitrary placement around a map.
        return CarrierOverlaps(platform,new Bounds(feet+Vector3.up*(height*.5f),new(.6f,height,.6f)));
    }
    private void CarryReplicaPlayer(MovingPlatformTrigger platform,Vector3 delta)
    {
        if(delta.sqrMagnitude<.00000001f || delta.sqrMagnitude>4096) return;
        var player=Player.Instance;var body=player.Collider;
        var ignored=new List<Collider>();
        if(_catalogue!.CarrierSurfaces.TryGetValue(platform.Pointer,out var surfaces))
            foreach(var surface in surfaces)
                if(surface!=null && surface.enabled && surface.gameObject.activeInHierarchy && !Physics.GetIgnoreCollision(body,surface))
                {Physics.IgnoreCollision(body,surface,true);ignored.Add(surface);}
        // A 100 ms replica step can place the deck through the capsule before
        // native Move sweeps upward. Ignore that deck only during the carry;
        // walls/ceilings still constrain the native character-controller sweep.
        try {player.PlatformMoved(delta);}
        finally {foreach(var surface in ignored) if(surface!=null) Physics.IgnoreCollision(body,surface,false);}
    }
    internal void RestoreCarrier(PlayerCarrier? carrier)
    {
        var player=Player.Instance;
        if(player?.IsAlive!=true || !TryCarrierFeet(carrier,player.Height,out var feet)) return;
        var controller=player.Collider.TryCast<CharacterController>();var enabled=controller?.enabled==true;
        if(controller!=null) controller.enabled=false;
        try {player.Transform.position+=feet-player.FeetPosition;}
        finally {if(controller!=null) controller.enabled=enabled;}
    }
}
