using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Hyperstrange.PBD;
using Hyperstrange.PBD.ProjectileComponents;
using Il2CppInterop.Runtime;
using PBDMP.Network;
using UnityEngine;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    private sealed record ProjectileLife(uint Id,Projectile Projectile,string PrefabKey,uint LocalShot,byte Actor);
    private readonly Dictionary<IntPtr,ProjectileLife> _projectiles=new();
    private readonly Dictionary<string,Projectile> _projectilePrefabs=new();
    private readonly Dictionary<uint,NativePresentation> _projectileVisuals=new();
    private readonly Dictionary<uint,NativePresentation> _trailVisuals=new();
    private readonly Dictionary<IntPtr,uint> _localShots=new();
    private readonly Dictionary<uint,Projectile> _localShotObjects=new();
    private readonly List<(NativePresentation Visual,float Until)> _effects=new();
    private readonly Dictionary<uint,FMODInstance> _projectileAudio=new();
    private readonly Dictionary<IntPtr,Projectile> _pendingNativeProjectiles=new();
    private readonly Dictionary<IntPtr,(Renderer Renderer,bool Enabled)> _localProjectileRenderers=new();
    private readonly Dictionary<uint,bool> _localCatModes=new();
    private uint _localShot;
    private uint _replayingShot;
    private bool _creatingRemoteShot;
    private bool _clearingProjectiles;
    private float _projectileReportAt;
    private static string PrefabKey(Projectile prefab)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(prefab.GetIl2CppType().FullName+"/"+prefab.name)));
    private void BuildProjectilePrefabs()
    {
        foreach(var prefab in Resources.FindObjectsOfTypeAll<Projectile>())
            if(!prefab.gameObject.scene.IsValid()) _projectilePrefabs[PrefabKey(prefab)]=prefab;
    }
    internal bool FrozenClientProjectile(Projectile projectile)=>Client && _localShots.ContainsKey(projectile.Pointer);
    internal void CapturedShot(ProjectileModel model,Projectile projectile,Vector3 position,Quaternion rotation,Vector3 direction,IAiActor owner,EntityId origin,float? charge=null)
    {
        if(!Active || projectile==null || owner==null) return;
        if(Environment.GetEnvironmentVariable("PBDMP_SHARED_COMBAT_TEST")=="1") Plugin.Logger.LogInfo($"PROJECTILE CAPTURE: client={Client}, model={model.GetIl2CppType().Name}, origin={origin?.ID}, owner={owner.Pointer}, local={Player.Instance?.Pointer}");
        if(Client)
        {
            if(owner.Pointer!=Player.Instance?.Pointer) {projectile.gameObject.SetActive(false);return;}
            if(_localShots.ContainsKey(projectile.Pointer)) return; // Bow's normal factory calls its charged overload.
            var key=_assets!.Bind(model,origin?.ID??""); var shot=++_localShot;
            _localShots[projectile.Pointer]=shot; _localShotObjects[shot]=projectile;
            foreach(var renderer in projectile.GetComponentsInChildren<Renderer>(true))
            {if(!_localProjectileRenderers.ContainsKey(renderer.Pointer)) _localProjectileRenderers[renderer.Pointer]=(renderer,renderer.enabled);renderer.enabled=false;}
            projectile._trailComponent?.Stop();
            var data=new List<string>();
            if(model._additionalData!=null) foreach(var pair in model._additionalData)
            {data.Add(pair.Key);data.Add(pair.Value.ToString("R",CultureInfo.InvariantCulture));}
            Send(new(WorldKind.Fire,_epoch,1,shot,Key:key,Asset:origin?.ID??"",Actor:2,Life:_life.Life,
                Position:Net(position),Rotation:Net(rotation.eulerAngles),Velocity:Net(direction),Data:data.ToArray(),Flags:charge.HasValue?1u:0u,Value:charge??0));
            return;
        }
        var actualPrefab=projectile.OriginalPoolable?.TryCast<Projectile>()??model.Prefab;
        RegisterProjectile(projectile,actualPrefab,_creatingRemoteShot?_replayingShot:0,owner.Pointer==_remote?.Pointer?(byte)2:owner.IsPlayer?(byte)1:(byte)0);
    }
    private void RegisterProjectile(Projectile projectile,Projectile prefab,uint localShot,byte actor)
    {
        _pendingNativeProjectiles.Remove(projectile.Pointer);
        var key=PrefabKey(prefab); _projectilePrefabs[key]=prefab;
        if(_projectiles.TryGetValue(projectile.Pointer,out var previous))
        {
            var updated=previous with{PrefabKey=key,LocalShot=localShot,Actor=actor};_projectiles[projectile.Pointer]=updated;Send(ProjectileFrame(updated));return;
        }
        var life=new ProjectileLife(++_nextId,projectile,key,localShot,actor);
        _projectiles[projectile.Pointer]=life;
        Send(ProjectileFrame(life));
    }
    internal void NativeProjectileInitialized(Projectile projectile)
    {
        if(!Active) return;
        if(!Client) _pendingNativeProjectiles[projectile.Pointer]=projectile;
        else if(projectile.Owner?.Pointer==Player.Instance?.Pointer && _incomingProjectile!=0)
        {
            // Native perfect-block callbacks create a homing projectile directly,
            // outside ProjectileModel.CreateInstance. Preserve the local callback,
            // but route its reflected attack through the host's pending hit token.
            var shot=++_localShot;_localShots[projectile.Pointer]=shot;_localShotObjects[shot]=projectile;LocalReflectionCreatesForTest++;
            foreach(var renderer in projectile.GetComponentsInChildren<Renderer>(true))
            {if(!_localProjectileRenderers.ContainsKey(renderer.Pointer)) _localProjectileRenderers[renderer.Pointer]=(renderer,renderer.enabled);renderer.enabled=false;}
            var camera=Player.Instance!.WorldCamera.transform;
            Send(new(WorldKind.Reflect,_epoch,1,_incomingProjectile,shot,Actor:2,Life:_life.Life,Asset:Player.Instance.CurrentWeapon?.WeaponId?.ID??"",Position:Net(camera.position+camera.forward*0.75f),Velocity:Net(camera.forward)));
        }
    }
    private void TickNativeProjectiles()
    {
        if(Client)
        {
            if(_life.Frozen) return;
            foreach(var pair in _localShotObjects)
            {
                var cat=pair.Value?.TryCast<ProjectileCat>();if(cat==null) continue;
                if(!_localCatModes.TryGetValue(pair.Key,out var mode) || mode!=cat._killOnImpact)
                {_localCatModes[pair.Key]=cat._killOnImpact;Send(new(WorldKind.Special,_epoch,1,pair.Key,Actor:2,Life:_life.Life,Flags:52,Value:cat._killOnImpact?1:0));}
            }
            return;
        }
        foreach(var projectile in _pendingNativeProjectiles.Values.ToArray()) RegisterNativeProjectile(projectile);
        _pendingNativeProjectiles.Clear();
    }
    private void RegisterNativeProjectile(Projectile projectile)
    {
        if(!Active || Client || projectile==null || !projectile.gameObject.activeInHierarchy || projectile.Owner==null || _projectiles.ContainsKey(projectile.Pointer)) return;
        var name=projectile.name.Replace("(Clone)","",StringComparison.Ordinal);
        var prefab=_projectilePrefabs.Values.FirstOrDefault(p=>p.name==name && p.GetIl2CppType().FullName==projectile.GetIl2CppType().FullName);
        if(prefab!=null) RegisterProjectile(projectile,prefab,0,projectile.Owner.Pointer==_remote?.Pointer?(byte)2:projectile.Owner.IsPlayer?(byte)1:(byte)0);
    }
    internal void NativeProjectileEnabled(Projectile projectile)
    {
        if(!Client && _projectiles.TryGetValue(projectile.Pointer,out var old)) EndProjectile(old);
        if(Client && _localShots.Remove(projectile.Pointer,out var shot)) {_localShotObjects.Remove(shot);RestoreProjectileRenderers(projectile);}
    }
    private WorldFrame ProjectileFrame(ProjectileLife life)=>new(WorldKind.Projectile,_epoch,1,life.Id,life.LocalShot,
        Key:life.PrefabKey,Asset:life.Projectile.OriginEntity?.ID??"",Actor:life.Actor,Flags:1,Position:Net(life.Projectile.Position),
        Rotation:Net(life.Projectile.transform.eulerAngles),Velocity:Net(life.Projectile._movementModule?._currentVelocity??Vector3.zero),Value:life.Projectile.NormalizedLifetimeLeft);
    private void ApplyFire(WorldFrame frame)
    {
        var pose=_session.Snapshot.RemotePose;
        if(Environment.GetEnvironmentVariable("PBDMP_SHARED_COMBAT_TEST")=="1") Plugin.Logger.LogInfo($"PROJECTILE REQUEST: frozen={_life.Frozen}, life={frame.Life}/{pose?.Life}, alive={pose?.Alive}, key={frame.Key}");
        if(_life.Frozen || pose?.Alive!=true || frame.Actor!=2 || frame.Life!=pose.Life || _remote==null) return;
        if(!_assets!.Models.TryGetValue(frame.Key,out var model) || !_catalogue!.Assets.TryGetValue(frame.Asset,out var origin))
            throw new InvalidDataException("Unknown native firing model or weapon.");
        if(origin.TryCast<WeaponId>()==null || !_assets.AllowsOrigin(frame.Key,frame.Asset) || Vector3.Distance(Unity(frame.Position),_remote.CenterOfMass)>8f || Unity(frame.Velocity).sqrMagnitude is <0.25f or >4f)
            throw new InvalidDataException("Invalid shot origin or direction.");
        var data=frame.Data??Array.Empty<string>(); if(data.Length%2!=0) throw new InvalidDataException("Invalid charged-shot data.");
        var saved=new Dictionary<string,float>();
        for(var i=0;i<data.Length;i+=2)
        {
            var value=float.Parse(data[i+1],CultureInfo.InvariantCulture);
            if(!float.IsFinite(value) || Math.Abs(value)>1_000_000) throw new InvalidDataException("Invalid shot parameter.");
            saved[data[i]]=model.GetValue(data[i]); model.SetValue(data[i],value);
        }
        _creatingRemoteShot=true; _replayingShot=frame.Entity;
        try
        {
            var position=Unity(frame.Position);var rotation=Quaternion.Euler(Unity(frame.Rotation));var direction=Unity(frame.Velocity).normalized;
            var created=(frame.Flags&1)!=0 && model.TryCast<ProjectileModelDildo>() is {} bow
                ?bow.CreateInstance(position,rotation,direction,_remote.Actor,origin,Mathf.Clamp01(frame.Value))
                :model.CreateInstance(position,rotation,direction,_remote.Actor,origin);
            var context=RemoteWeapon(origin.Cast<WeaponId>());created.SetPlayerWeapon(context);
            if(created.TryCast<ProjectileTracker>() is {} tracker && context.TryCast<Hyperstrange.PBD.PlayerWeapons.PlayerWeaponGatlingGun>() is {} gatling) tracker.SetWeaponScript(gatling);
            ProjectileCreatesForTest++;
            if(Environment.GetEnvironmentVariable("PBDMP_SHARED_COMBAT_TEST")=="1") Plugin.Logger.LogInfo($"PROJECTILE HOST CREATED: {created.name}, active={created.gameObject.activeSelf}/{created.gameObject.activeInHierarchy}, position={created.Position}, direction={created.Direction}, time={created._localTimeScale}, life={created.NormalizedLifetimeLeft}");
        }
        finally {_creatingRemoteShot=false;foreach(var pair in saved) model.SetValue(pair.Key,pair.Value);}
    }
    private void SendProjectileState(bool snapshot)
    {
        foreach(var life in _projectiles.Values.ToArray())
        {
            if(life.Projectile==null || !life.Projectile.gameObject.activeInHierarchy)
            {EndProjectile(life);continue;}
            Send(ProjectileFrame(life) with {Flags=snapshot?3u:1u});
            if(Environment.GetEnvironmentVariable("PBDMP_SHARED_COMBAT_TEST")=="1" && Time.unscaledTime>=_projectileReportAt)
            { _projectileReportAt=Time.unscaledTime+1f; Plugin.Logger.LogInfo($"PROJECTILE STATE: {life.Projectile.name}, position={life.Projectile.Position}, velocity={life.Projectile._movementModule?._currentVelocity}, time={life.Projectile._localTimeScale}, weapon={life.Projectile._playerWeapon?.WeaponId?.name}, age={life.Projectile.NormalizedLifetimeLeft}"); }
        }
    }
    internal void ProjectileImpact(Projectile projectile,RaycastHit hit)
    {
        if(!Client && !_projectiles.ContainsKey(projectile.Pointer)) RegisterNativeProjectile(projectile);
        if(Client || !_projectiles.TryGetValue(projectile.Pointer,out var life)) return;
        if(Environment.GetEnvironmentVariable("PBDMP_SHARED_COMBAT_TEST")=="1") Plugin.Logger.LogInfo($"PROJECTILE IMPACT: {hit.collider?.name}, point={hit.point}");
        Send(new(WorldKind.Impact,_epoch,1,life.Id,Key:life.PrefabKey,Actor:life.Actor,Position:Net(hit.point),Rotation:Net(hit.normal)));
    }
    private void EndProjectile(ProjectileLife life)
    {
        var position=life.Projectile!=null?life.Projectile.Position:Vector3.zero;
        Send(new(WorldKind.Projectile,_epoch,1,life.Id,life.LocalShot,Key:life.PrefabKey,Actor:life.Actor,Flags:0,Position:Net(position)));
        if(life.Projectile!=null) _projectiles.Remove(life.Projectile.Pointer);
    }
    private void ApplyProjectile(WorldFrame frame)
    {
        if(frame.Flags is 4 or 8 or 16) {ApplyWeaponProjectileState(frame);return;}
        if((frame.Flags&1)==0)
        {
            if(_projectileAudio.Remove(frame.Entity,out var audio)) {audio.Stop(false);audio.Release();}
            if(_projectileVisuals.Remove(frame.Entity,out var visual)) visual.Dispose();
            if(_trailVisuals.Remove(frame.Entity,out var trail))
            {trail.Move(Unity(frame.Position),Quaternion.identity);trail.Tick(0.025f);_effects.Add((trail,Time.unscaledTime+2f));}
            if(frame.Actor==2 && _localShotObjects.Remove(frame.Revision,out var original) && original!=null)
            {
                if(original.TryCast<ProjectileTracker>() is {} tracker && tracker._gatlingGun?._currentTracker?.Pointer==original.Pointer) tracker._gatlingGun.EndHomingMode();
                RecycleOriginalProjectile(original);_localShots.Remove(original.Pointer);
                _localCatModes.Remove(frame.Revision);
            }
            return;
        }
        if(!_projectilePrefabs.TryGetValue(frame.Key,out var prefab)) throw new InvalidDataException("Unknown authoritative projectile prefab.");
        if(!_projectileVisuals.TryGetValue(frame.Entity,out var copy))
        {
            copy=new(prefab.gameObject);_projectileVisuals[frame.Entity]=copy;
            if(prefab.TryCast<ProjectileFireBall>()!=null) UrineProjectileProxy.Attach(copy.Root,frame.Entity,prefab);
            var trail=prefab._trailComponent?._trailPrefab;
            if(trail!=null) _trailVisuals[frame.Entity]=new(trail.gameObject);
            if((frame.Flags&2)==0) StartProjectileAudio(frame.Entity,prefab,Unity(frame.Position));
        }
        copy.Move(Unity(frame.Position),Quaternion.Euler(Unity(frame.Rotation)));
        if(_projectileAudio.TryGetValue(frame.Entity,out var sound)) sound.SetPosition(Unity(frame.Position));
        if(_trailVisuals.TryGetValue(frame.Entity,out var trailCopy)) trailCopy.Move(Unity(frame.Position),Quaternion.Euler(Unity(frame.Rotation)));
    }
    private void StartProjectileAudio(uint id,Projectile prefab,Vector3 position)
    {
        var source=prefab.GetComponent<ProjectileAudioComponent>()?._onEnable;
        if(source==null) return;
        var instance=new FMODInstance(source);instance.SetPosition(position);instance.Play();_projectileAudio[id]=instance;
    }
    private void ApplyImpact(WorldFrame frame)
    {
        if((frame.Flags&2)!=0) {PlayExplosion(frame);return;}
        if(!_projectilePrefabs.TryGetValue(frame.Key,out var prefab)) return;
        var position=Unity(frame.Position); prefab.GetComponent<ProjectileAudioComponent>()?._onImpact?.PlayOneShot(position);
        if(_trailVisuals.TryGetValue(frame.Entity,out var trail)) {trail.Move(position,Quaternion.identity);trail.Tick(0.025f);}
        var controller=prefab.GetComponent<ProjectileVfxComponent>()?._onDisable;
        if(controller!=null)
        {var effect=new NativePresentation(controller.gameObject);effect.Move(position,Quaternion.FromToRotation(Vector3.forward,Unity(frame.Rotation)));_effects.Add((effect,Time.unscaledTime+3f));}
    }
    private void TickPresentations()
    {
        var delta=_life.Frozen?0f:Time.unscaledDeltaTime;
        foreach(var visual in _projectileVisuals.Values) visual.Tick(delta);
        foreach(var visual in _trailVisuals.Values) visual.Tick(delta);
        foreach(var pair in _effects.ToArray())
        {pair.Visual.Tick(delta);if(Time.unscaledTime>=pair.Until){pair.Visual.Dispose();_effects.Remove(pair);}}
    }
    private void ClearProjectiles()
    {
        foreach(var audio in _projectileAudio.Values) {audio.Stop(false);audio.Release();}
        _projectileAudio.Clear();
        foreach(var visual in _projectileVisuals.Values) visual.Dispose();
        foreach(var visual in _trailVisuals.Values) visual.Dispose();
        foreach(var pair in _effects) pair.Visual.Dispose();
        _clearingProjectiles=true;
        try {foreach(var original in _localShotObjects.Values.ToArray()) if(original!=null) RecycleOriginalProjectile(original);}
        finally {_clearingProjectiles=false;}
        _projectiles.Clear();_projectileVisuals.Clear();_trailVisuals.Clear();_effects.Clear();_localShotObjects.Clear();_localShots.Clear();_projectilePrefabs.Clear();
        _localProjectileRenderers.Clear();_reflectionHits.Clear();_localCatModes.Clear();
        _pendingNativeProjectiles.Clear();
    }
    private void RestoreProjectileRenderers(Projectile projectile)
    {foreach(var renderer in projectile.GetComponentsInChildren<Renderer>(true)) if(_localProjectileRenderers.Remove(renderer.Pointer,out var prior) && prior.Renderer!=null) prior.Renderer.enabled=prior.Enabled;}
    private void RecycleOriginalProjectile(Projectile projectile)
    {
        var previous=_clearingProjectiles;_clearingProjectiles=true;
        try {RestoreProjectileRenderers(projectile);Hyperstrange.Base.PoolManager.ReturnToPool(projectile.Cast<Hyperstrange.Base.IPoolable>());}
        finally {_clearingProjectiles=previous;}
    }
}
