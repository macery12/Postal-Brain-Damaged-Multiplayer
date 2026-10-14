using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using PBDMP.Network;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    internal bool ClientWorldFlame(UnityEngine.Component target,IAiActor owner,float delta,float damage)
    {
        if(!Client || !Active || Applying || _catalogue!.Key(target) is not {} key || !_catalogue.UrineKeys.Contains(key)) return false;
        if(!_life.Frozen && Player.Instance.IsAlive && owner?.Pointer==Player.Instance.Pointer)
            Send(new(WorldKind.Special,_epoch,1,Key:key,Actor:2,Life:_life.Life,Flags:75,Value:delta,Value2:damage,Position:Net(Player.Instance.CenterOfMass)));
        return true;
    }
    internal bool ClientBarrelHit(ExplosiveBarrel barrel,HitData hit)
    {
        if(!Client || !Active || Applying || _catalogue!.Key(barrel) is not {} key || !_catalogue.UrineKeys.Contains(key)) return false;
        if(!_life.Frozen && Player.Instance.IsAlive && hit.projectile==null && !hit.isExplosion && hit.dealer?.Pointer==Player.Instance.Pointer)
            Send(new(WorldKind.Special,_epoch,1,Key:key,Actor:2,Life:_life.Life,Flags:76,Revision:HitFlags(hit),Asset:hit.originEntity?.ID??"",
                Position:Net(hit.position),Rotation:Net(hit.direction),Velocity:Net(hit.normal),Value:hit.damage,Value2:hit.weakSpotMultiplier,
                Values:new[]{hit.ragdollForce,hit.chanceToGib,hit.pushVelocity.x,hit.pushVelocity.y,hit.pushVelocity.z}));
        return true;
    }
}
