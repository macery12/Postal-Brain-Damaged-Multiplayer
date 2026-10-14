using Hyperstrange.PBD;
using Hyperstrange.PBD.PlayerWeapons;
using Hyperstrange.PBD.PlayerComponents.HitReceiver;
using PBDMP.Network;
using UnityEngine;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    private readonly Dictionary<uint,(uint Life,float Until,uint Dealer)> _reflectionHits=new();
    private uint _incomingProjectile;
    internal bool ReflectIncoming(ReflectProcessor processor,HitData hit)
    {
        if(!Client || !Applying || _incomingProjectile==0 || !processor._isReflectActive || !processor._hitProcessorComponent.IsHitBlockable(hit)) return false;
        var camera=Player.Instance.WorldCamera.transform;
        var before=_localShot;
        // Keep the native successful-kick callbacks (feedback and the actual local
        // reflection factory). Its projectile is frozen and forwarded by the owner hook.
        Player.Instance.OnPlayerReflectSuccess?.Invoke(hit);
        if(_localShot==before) Send(new(WorldKind.Reflect,_epoch,1,_incomingProjectile,Actor:2,Life:_life.Life,Position:Net(camera.position+camera.forward*0.75f),Velocity:Net(camera.forward)));
        return true;
    }
    private void ApplyReflection(WorldFrame frame)
    {
        var pose=_session.Snapshot.RemotePose;
        if(_life.Frozen || _remote==null || pose?.Alive!=true || frame.Actor!=2 || frame.Life!=pose.Life || !_reflectionHits.Remove(frame.Entity,out var pending) ||
            pending.Life!=frame.Life || Time.unscaledTime>pending.Until || Vector3.Distance(Unity(frame.Position),_remote.CenterOfMass)>8f) return;
        var weapon=!string.IsNullOrEmpty(frame.Asset)&&_catalogue!.Assets.TryGetValue(frame.Asset,out var requested)?requested.TryCast<WeaponId>():null;
        weapon??=Resources.FindObjectsOfTypeAll<WeaponId>().FirstOrDefault(w=>w.name=="WEAPON_NoWeapon");
        var model=weapon?.PlayerWeaponModel.TryCast<PlayerWeaponModelNoWeapon>();var shovel=weapon?.PlayerWeaponModel.TryCast<PlayerWeaponModelShovel>();
        var prefab=model?._reflectProjectile??shovel?._reflectProjectile;if(prefab==null) return;
        var direction=Unity(frame.Velocity).normalized;
        var target=_enemies.GetValueOrDefault(pending.Dealer);
        if(target?.IsAlive==true) direction=(target.CenterOfMass-Unity(frame.Position)).normalized;
        var root=UnityEngine.Object.Instantiate(prefab.gameObject,Unity(frame.Position),Quaternion.LookRotation(direction));
        var projectile=root.GetComponent<ProjectileHoming>();if(projectile==null) {UnityEngine.Object.Destroy(root);throw new InvalidOperationException("Native reflected projectile lacks homing adapter.");}
        if(model!=null) projectile.AddExplosion(model._explosionSettings);
        projectile.Init(direction,model?._projectileSpeed??shovel!._projectileSpeed,model?._projectileTrackingSpeed??shovel!._projectileTrackingSpeed,model?._projectileDamage??shovel!._projectileDamage,1,model?._projectileRagdollForce??shovel!._projectileRagdollForce,0,_remote.Actor,weapon);
        if(target?.IsAlive==true) projectile.SetTarget(target.ReferenceTransform);
        RegisterProjectile(projectile,prefab,frame.Revision,2);
        ReflectionCreatesForTest++;
    }
}
