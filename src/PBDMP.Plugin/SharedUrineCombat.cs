using Hyperstrange.PBD;
using Hyperstrange.PBD.NpcComponents;
using PBDMP.Network;
using UnityEngine;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    internal bool ClientProjectileUrine(ProjectileFireBall projectile,PissData data)
    {
        if(!Client || !Active || Applying) return false;
        // Canonical fireballs are represented by a separate harmless receiver.
        return true;
    }
    internal void RequestProjectileUrine(uint id,PissData data)
    {
        if(!Client || !Active || _life.Frozen || data.owner?.Pointer!=Player.Instance?.Pointer) return;
        Send(new(WorldKind.Special,_epoch,1,id,Actor:2,Life:_life.Life,Flags:72,Position:Net(Player.Instance!.CenterOfMass),Value:data.stagger?1:0));
    }
    private void ApplyProjectileUrine(WorldFrame frame)
    {
        var life=_projectiles.Values.FirstOrDefault(p=>p.Id==frame.Entity);
        var ball=life?.Projectile.TryCast<ProjectileFireBall>();
        if(ball==null || !ball.gameObject.activeInHierarchy || Vector3.Distance(ball.Position,_remote!.CenterOfMass)>15) return;
        ball.Piss(new PissData(_remote.Actor,frame.Value!=0));UrineRequestsApplied++;
    }
    internal void FireBallExtinguished(ProjectileFireBall ball)
    {
        if(!Active || Client) return;
        var vfx=ball._extinguishVfx;var sound=ball._extinguishSfx;var key=vfx!=null?EffectKey("vfx",vfx.name):"";var audio=sound!=null?EffectKey("audio",sound.name):"";
        if(vfx!=null) _effectPrefabs[key]=vfx.gameObject;if(sound!=null) _effectSounds[audio]=sound;
        Send(new(WorldKind.UrineState,_epoch,1,Key:key,Asset:audio,Flags:2,Position:Net(ball.Position)));
    }
}
