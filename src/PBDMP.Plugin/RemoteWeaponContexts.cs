using System.Reflection;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Util;
using Hyperstrange.PBD.LightningGun;
using Hyperstrange.PBD.PlayerWeapons;
using PBDMP.Network;
using UnityEngine;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    private readonly Dictionary<string,PlayerWeapon> _remoteWeapons=new();
    internal bool IsRemoteWeapon(PlayerWeapon weapon)=>_remoteWeapons.Values.Any(w=>w.Pointer==weapon.Pointer);
    private PlayerWeapon RemoteWeapon(WeaponId id)
    {
        if(_remoteWeapons.TryGetValue(id.ID,out var existing)) return existing;
        var staging=new GameObject("PBDMP dormant weapon context");staging.SetActive(false);
        var root=UnityEngine.Object.Instantiate(id.PlayerWeaponPrefab,staging.transform,false);
        var weapon=root.GetComponent<PlayerWeapon>();if(weapon==null) throw new InvalidOperationException("Remote weapon context missing.");
        weapon._parent=Player.Instance;weapon._parentCached=true;weapon._entityId=id;weapon._weaponModel=id.PlayerWeaponModel;
        weapon._animator=root.GetComponentInChildren<Animator>(true);
        var actual=NativeAssetGraph.Actual(weapon);
        foreach(var property in actual.GetType().GetProperties(BindingFlags.Public|BindingFlags.Instance))
            if(property.Name=="_model" && typeof(PlayerWeaponModel).IsAssignableFrom(property.PropertyType)) property.SetValue(actual,NativeAssetGraph.Cast(id.PlayerWeaponModel,property.PropertyType));
        if(weapon.TryCast<PlayerWeaponLightningGun>() is {} lightning)
        {
            var hitter=new Hitter();hitter.SetDealer(_remote!.Actor);hitter.SetWeaponId(id);hitter.SetWeakSpotMultiplier(1);lightning.Hitter=hitter;
        }
        _remoteWeapons[id.ID]=weapon;return weapon;
    }
    private void TickRemoteWeapons()
    {if(_remote!=null) foreach(var weapon in _remoteWeapons.Values) if(weapon!=null) weapon.transform.root.SetPositionAndRotation(_remote.ReferenceTransform.position,_remote.ReferenceTransform.rotation);}
    private void ClearRemoteWeapons()
    {foreach(var weapon in _remoteWeapons.Values) if(weapon!=null) UnityEngine.Object.Destroy(weapon.transform.root.gameObject);_remoteWeapons.Clear();}
    internal void TrackerMode(PlayerWeaponGatlingGun weapon,ProjectileTracker tracker,RaycastHit hit)
    {
        weapon._currentTracker=tracker;
        if(!_projectiles.TryGetValue(tracker.Pointer,out var life)) return;
        Send(new(WorldKind.Projectile,_epoch,1,life.Id,life.LocalShot,Actor:2,Flags:4,Position:Net(hit.point),Rotation:Net(hit.normal)));
    }
    internal void CatReturned(PlayerWeaponCatCanon weapon,ProjectileCat cat)
    {
        if(_projectiles.TryGetValue(cat.Pointer,out var life)) Send(new(WorldKind.Projectile,_epoch,1,life.Id,life.LocalShot,Actor:2,Flags:8));
        weapon.RemovePrimedCat(cat);
    }
    internal void CatRecall(PlayerWeaponCatCanon weapon)
    {if(Client && Active && !_life.Frozen) Send(new(WorldKind.Special,_epoch,1,Actor:2,Life:_life.Life,Flags:50,Asset:weapon.WeaponId.ID));}
    private void ApplyCatRecall(WorldFrame frame)
    {
        if(!_remoteWeapons.TryGetValue(frame.Asset,out var weapon) || weapon.TryCast<PlayerWeaponCatCanon>() is not {} cat) return;
        foreach(var projectile in cat._primedCats) if(projectile!=null) projectile.ReturnToPlayer(_remote!.ReferenceTransform.position);
    }
    internal void CatAttached(ProjectileCat cat)
    {if(!Client && _projectiles.TryGetValue(cat.Pointer,out var life) && life.Actor==2) Send(new(WorldKind.Projectile,_epoch,1,life.Id,life.LocalShot,Actor:2,Flags:16,Position:Net(cat.Position)));}
    internal void ClientCatKilled(ProjectileCat cat)
    {if(Client && Active && !_life.Frozen && !_clearingProjectiles && _localShots.TryGetValue(cat.Pointer,out var shot)) Send(new(WorldKind.Special,_epoch,1,shot,Actor:2,Life:_life.Life,Flags:53));}
    private void ApplyCatAction(WorldFrame frame)
    {
        var cat=_projectiles.Values.FirstOrDefault(p=>p.Actor==2 && p.LocalShot==frame.Entity)?.Projectile.TryCast<ProjectileCat>();
        if(cat==null) return;
        if(frame.Flags==52) cat.SetKillOnImpact(frame.Value!=0);else cat.Kill();
    }
    private void ApplyWeaponProjectileState(WorldFrame frame)
    {
        if(frame.Actor!=2 || !_localShotObjects.TryGetValue(frame.Revision,out var original) || original==null) return;
        if(frame.Flags==4 && original.TryCast<ProjectileTracker>() is {} tracker && tracker._gatlingGun!=null)
        {
            tracker.transform.position=Unity(frame.Position);
            tracker._gatlingGun.StartHomingMode(tracker,new RaycastHit{point=Unity(frame.Position),normal=Unity(frame.Rotation)});
        }
        else if(frame.Flags==8 && original.TryCast<ProjectileCat>() is {} cat && cat._catCanon!=null) cat._catCanon.OnCatReturned(cat);
        else if(frame.Flags==16 && original.TryCast<ProjectileCat>() is {} primed && primed._catCanon!=null)
        {primed.transform.position=Unity(frame.Position);primed._catCanon.OnCatAttachedToWall(primed);}
    }
    internal void ClientHomingTarget(ProjectileHoming projectile,Transform target)
    {
        if(!Client || !Active || _life.Frozen || !_localShots.TryGetValue(projectile.Pointer,out var shot)) return;
        var npc=target!=null?target.GetComponentInParent<Npc>():null;
        var targetProjectile=target!=null?target.GetComponentInParent<Projectile>():null;
        Send(new(WorldKind.Special,_epoch,1,shot,npc!=null?_enemyIds.GetValueOrDefault(npc.Pointer):0,Actor:2,Life:_life.Life,Flags:51,
            Value:targetProjectile!=null?_localShots.GetValueOrDefault(targetProjectile.Pointer):0));
    }
    private void ApplyHomingTarget(WorldFrame frame)
    {
        var projectile=_projectiles.Values.FirstOrDefault(p=>p.Actor==2 && p.LocalShot==frame.Entity)?.Projectile.TryCast<ProjectileHoming>();
        if(projectile==null) return;
        Transform? target=null;
        if(frame.Revision!=0 && _enemies.TryGetValue(frame.Revision,out var npc) && npc!=null && npc.IsAlive) target=npc.ReferenceTransform;
        else if(frame.Value>0) target=_projectiles.Values.FirstOrDefault(p=>p.Actor==2 && p.LocalShot==(uint)frame.Value)?.Projectile.transform;
        projectile.SetTarget(target);
    }
}
