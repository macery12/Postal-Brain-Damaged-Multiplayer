using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using PBDMP.Network;
using UnityEngine;
using DG.Tweening;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    private sealed class BarrelClock
    {
        internal ExplosiveBarrel Barrel=null!;
        internal HitData Hit=new();
        internal float Left;
        internal byte Owner;
        internal uint Enemy;
    }
    private sealed class FallClock {internal FallingPlatform Platform=null!;internal float Left;internal byte Actor;}
    private sealed class ScaleClock {internal Scaler Scaler=null!;internal Vector3 From;internal float Elapsed,Duration;}
    private readonly Dictionary<IntPtr,BarrelClock> _barrelClocks=new();
    private readonly Dictionary<string,FallClock> _falls=new();
    private readonly Dictionary<string,ScaleClock> _scales=new();
    private readonly Dictionary<string,WorldFrame> _mechanismState=new();
    private readonly Dictionary<MovingPlatformTrigger,bool> _platformEnabled=new();
    private bool _executingMechanism;
    internal bool RestoringNative=>_life.RestoringWorld || RestoreInProgress;
    private float BarrelDelayLeft(ExplosiveBarrel barrel)=>_barrelClocks.GetValueOrDefault(barrel.Pointer)?.Left??0;
    private static void SetBurnPresentation(BurnableObject burn)
    {if(burn._propertyAnimator!=null) burn._propertyAnimator.SetFloat("_Burn",1-Mathf.Clamp01(burn._hitPoints.Current/Mathf.Max(1,burn._hitPoints.Max)));}
    private void ClearBarrelQueues()=>_barrelClocks.Clear();
    internal bool QueueBarrelHit(ExplosiveBarrel barrel,HitData hit)
    {
        if(!Active || !OwnsScenery(barrel)) return false;
        if(Client) return ClientBarrelHit(barrel,hit);
        if(RestoringNative || barrel._exploded || barrel._explosionQueued) return true;
        QueueBarrel(barrel,hit,hit.isExplosion?Mathf.Clamp(barrel._chainReactionDelay,0,60):0);return true;
    }
    internal bool QueueBarrelFlame(ExplosiveBarrel barrel,IAiActor owner,float delta,float damage)
    {
        if(!Active || !OwnsScenery(barrel)) return false;
        if(Client) return ClientWorldFlame(barrel,owner,delta,damage);
        if(RestoringNative || barrel._exploded || barrel._explosionQueued) return true;
        barrel._health.Subtract(Mathf.Clamp(delta,0,.25f)*Mathf.Clamp(damage,0,10000));
        if(barrel._health.Current<=0) QueueBarrel(barrel,new HitData{dealer=owner,position=barrel.transform.position,damage=0},0);
        return true;
    }
    private void QueueBarrel(ExplosiveBarrel barrel,HitData hit,float left)
    {
        barrel._explosionQueued=true;
        var owner=hit.dealer;var actor=owner?.Pointer==Player.Instance.Pointer?(byte)1:owner?.Pointer==_remote?.Pointer?(byte)2:(byte)0;
        var enemy=owner?.TryCast<Npc>() is {} npc?Register(npc):0;
        // Native hit records can be reused while an explosion visits colliders.
        // Keep value data and stable actor identity, not a mutable pooled hit/actor.
        var copy=new HitData {originEntity=hit.originEntity,damage=hit.damage,weakSpotMultiplier=hit.weakSpotMultiplier,
            position=hit.position,direction=hit.direction,normal=hit.normal,pushVelocity=hit.pushVelocity,
            ragdollForce=hit.ragdollForce,chanceToGib=hit.chanceToGib,isPiss=hit.isPiss,isCrit=hit.isCrit,
            isQuickMelee=hit.isQuickMelee,isPowerful=hit.isPowerful,isExplosion=hit.isExplosion,isFlame=hit.isFlame,
            forceRagdoll=hit.forceRagdoll,forceStagger=hit.forceStagger,forceLowHp=hit.forceLowHp,
            cancelDecals=hit.cancelDecals,spawnPickupsOnKill=hit.spawnPickupsOnKill};
        var clock=new BarrelClock {Barrel=barrel,Hit=copy,Left=left,Owner=actor,Enemy=enemy};
        _barrelClocks[barrel.Pointer]=clock;
        if(left<=0 && !_life.Frozen) {_barrelClocks.Remove(barrel.Pointer);ExecuteBarrel(clock);}
    }
    private WorldFrame BarrelState(string key,ExplosiveBarrel barrel)
    {
        var clock=_barrelClocks.GetValueOrDefault(barrel.Pointer);var hit=clock?.Hit;
        var actor=clock?.Owner??0;var enemy=clock?.Enemy??0;
        return new(WorldKind.Scenery,_epoch,1,Id(key),Revision:enemy,Key:key,Asset:hit?.originEntity?.ID??"",Actor:actor,Flags:3,
            Position:Net(hit?.position??barrel.transform.position),Rotation:Net(hit?.direction??Vector3.zero),Velocity:Net(hit?.pushVelocity??Vector3.zero),
            Value:barrel._health.Current,Value2:barrel._health.Max,
            Values:new[]{barrel._exploded?1f:0,barrel._explosionQueued?1f:0,clock?.Left??0,hit?.damage??0,hit!=null?HitFlags(hit):0,hit?.weakSpotMultiplier??1,hit?.ragdollForce??0,hit?.chanceToGib??0});
    }
    private void RestoreBarrelQueue(ExplosiveBarrel barrel,WorldFrame frame)
    {
        var v=frame.Values!;var pending=v[1]!=0;
        _barrelClocks.Remove(barrel.Pointer);barrel.StopAllCoroutines();barrel._explosionQueued=pending;
        if(pending && !barrel._exploded)
        {
            var enemy=_restoreActorIds.GetValueOrDefault(frame.Revision);
            var hit=DecodeHit(frame with {Entity=0,Flags=(uint)v[4],Value=v[3],Value2=v[5],Values=new[]{v[6],v[7],frame.Velocity.X,frame.Velocity.Y,frame.Velocity.Z}});
            hit.dealer=null;
            _barrelClocks[barrel.Pointer]=new(){Barrel=barrel,Left=v[2],Owner=frame.Actor,Enemy=enemy,Hit=hit};
        }
    }
    private void ExecuteBarrel(BarrelClock clock)
    {
        var enemy=_enemies.GetValueOrDefault(clock.Enemy);
        clock.Hit.dealer=clock.Owner==1?Player.Instance.Cast<IAiActor>():clock.Owner==2?_remote?.Actor:
            enemy!=null && !_enemyRespawns.Contains(enemy.Pointer)?enemy.Cast<IAiActor>():null;
        clock.Barrel.Execute(clock.Hit);
    }
    internal bool MechanismFall(FallingPlatform platform,string method)
    {
        if(!Active || !OwnsMission(platform) || _executingMechanism) return false;
        if(Client) {if(!Applying && !_falls.ContainsKey(_catalogue!.Key(platform)!)) ClientMissionFall(platform);return true;}
        if(RestoringNative) return true;
        StartFall(platform,1,method=="Fall"?0:platform._destructionDelay);return true;
    }
    private void StartFall(FallingPlatform platform,byte actor,float delay)
    {
        var key=_catalogue!.Key(platform)!;if(platform._fallen || _falls.ContainsKey(key)) return;
        _falls[key]=new(){Platform=platform,Left=Mathf.Clamp(delay,0,60),Actor=actor};
        platform._preFallParticle?.Play();platform._preFallEvent?.PlayOneShot(platform.transform.position);
    }
    internal bool StartScale(Scaler scaler)
    {
        if(!Active || !OwnsMission(scaler)) return false;
        if(Client || RestoringNative) return true;
        var key=_catalogue!.Key(scaler)!;if(_scales.ContainsKey(key)) return true;
        scaler._scaled=true;scaler.transform.DOKill(false);
        _scales[key]=new(){Scaler=scaler,From=scaler.transform.localScale,Duration=Mathf.Clamp(scaler._scaleDuration,.001f,3600)};
        scaler._scaleEvent?.PlayOneShot(scaler.transform.position);return true;
    }
    private void TickWorldMechanics()
    {
        HoldMachineCooldowns();
        if(Client) {TickRocketViews();TickSyringeViews();return;}
        if(_life.Frozen || RestoringNative) return;
        TickRocketLaunches();TickGateFuses();
        foreach(var pair in _barrelClocks.ToArray())
        {
            var clock=pair.Value;if(clock.Barrel==null || clock.Barrel._exploded) {_barrelClocks.Remove(pair.Key);continue;}
            clock.Left=Mathf.Max(0,clock.Left-Time.deltaTime);
            if(clock.Left>0) continue;
            _barrelClocks.Remove(pair.Key);ExecuteBarrel(clock);
        }
        foreach(var pair in _falls.ToArray())
        {
            var clock=pair.Value;if(clock.Platform==null || clock.Platform._fallen) {_falls.Remove(pair.Key);continue;}
            clock.Left=Mathf.Max(0,clock.Left-Time.deltaTime);
            if(clock.Left>0) continue;
            _falls.Remove(pair.Key);clock.Platform._fallen=true;_executingMechanism=true;
            try {if(clock.Actor==1) clock.Platform.Fall(Player.Instance);else {clock.Platform.Fall();if(clock.Actor==2) SendRemoteShake();}}
            finally {_executingMechanism=false;}
        }
        foreach(var pair in _scales.ToArray())
        {
            var clock=pair.Value;if(clock.Scaler==null) {_scales.Remove(pair.Key);continue;}
            clock.Elapsed=Mathf.Min(clock.Duration,clock.Elapsed+Time.deltaTime);
            var t=clock.Elapsed/clock.Duration;var curve=clock.Scaler._scaleCurve;
            clock.Scaler.transform.localScale=Vector3.LerpUnclamped(clock.From,clock.Scaler._scaleSize,curve!=null?curve.Evaluate(t):t);
            if(t>=1) _scales.Remove(pair.Key);
        }
    }
    private IEnumerable<int> CaptureMechanisms(bool complete)
    {
        foreach(var step in CaptureArena(complete)) yield return step;
        foreach(var step in CaptureRockets(complete)) yield return step;
        foreach(var step in CaptureScriptedRotations(complete)) yield return step;
        foreach(var step in CaptureMachineAudio(complete)) yield return step;
        foreach(var step in CaptureGateFuses(complete)) yield return step;
        foreach(var step in CaptureSecrets()) yield return step;
        foreach(var step in CapturePosters(complete)) yield return step;
        foreach(var key in _catalogue!.MechanismKeys)
        {
            var c=_catalogue.Components[key];if(c==null) {yield return 0;continue;}
            WorldFrame? f=null;
            if(c.TryCast<FallingPlatform>() is {} fall)
            {
                var clock=_falls.GetValueOrDefault(key);
                f=new(WorldKind.Mechanism,_epoch,1,Id(key),Key:key,Flags:1,Value:clock?.Left??0,Value2:clock?.Actor??0,Values:new[]{clock!=null?1f:0});
            }
            else if(c.TryCast<Scaler>() is {} scale)
            {
                var clock=_scales.GetValueOrDefault(key);
                f=new(WorldKind.Mechanism,_epoch,1,Id(key),Key:key,Flags:2,Position:Net(scale.transform.localScale),
                    Rotation:Net(clock?.From??scale.transform.localScale),Value:clock?.Elapsed??0,Value2:clock?.Duration??1,Values:new[]{clock!=null?1f:0});
            }
            if(f!=null)
            {
                if(complete || !_mechanismState.TryGetValue(key,out var old) || old.Position!=f.Position || old.Value!=f.Value || old.Value2!=f.Value2 || !(old.Values??Array.Empty<float>()).SequenceEqual(f.Values!))
                {_mechanismState[key]=f;Send(f);}
            }
            yield return 0;
        }
    }
    private void ApplyMechanism(WorldFrame f,bool hostRestore=false)
    {
        if(f.Flags is 9 or 10 or 11) {ApplyArena(f,hostRestore);return;}
        if(f.Flags==8) {ApplyRocket(f,hostRestore);return;}
        if(f.Flags==7) {ApplyScriptedRotation(f);return;}
        if(f.Flags==6) {ApplyPoster(f);return;}
        if(f.Flags==5) {ApplyMachineAudio(f,hostRestore);return;}
        if(f.Flags==4) {ApplyGateFuse(f,hostRestore);return;}
        if(f.Flags==3) {ApplySecret(f);return;}
        if(!_catalogue!.MechanismKeys.Contains(f.Key) || f.Values is not {Length:1} values || values[0] is not (0 or 1)) throw new InvalidDataException("Invalid timed mechanism.");
        var c=_catalogue.Components[f.Key];if(c==null) return;
        if(f.Flags==1 && c.TryCast<FallingPlatform>() is {} fall)
        {
            if(f.Value<0 || f.Value>60 || f.Value2 is not (0 or 1 or 2)) throw new InvalidDataException("Invalid fall countdown.");
            var prior=_falls.ContainsKey(f.Key);
            if(values[0]!=0)
            {
                _falls[f.Key]=new(){Platform=fall,Left=f.Value,Actor=(byte)f.Value2};
                if(!prior) fall._preFallParticle?.Play();
            }
            else {_falls.Remove(f.Key);fall._preFallParticle?.Stop();}
        }
        else if(f.Flags==2 && c.TryCast<Scaler>() is {} scale)
        {
            if(f.Value<0 || f.Value2<=0 || f.Value2>3600 || f.Value>f.Value2) throw new InvalidDataException("Invalid scale continuation.");
            scale.transform.DOKill(false);scale.transform.localScale=Unity(f.Position);
            if(hostRestore && values[0]!=0) _scales[f.Key]=new(){Scaler=scale,From=Unity(f.Rotation),Elapsed=f.Value,Duration=f.Value2};
            else _scales.Remove(f.Key);
        }
        else throw new InvalidDataException("Unknown timed mechanism family.");
    }
    internal void RebasePlatforms()
    {
        if(_catalogue==null) return;
        var localPlayer=Player.Instance;
        if(localPlayer!=null)
        {
            var volumes=_catalogue.MissionKeys.Select(k=>_catalogue.Components[k].TryCast<GravityChanger>()).Where(g=>g!=null).ToArray();
            foreach(var volume in volumes) volume!.OnTriggerExit(localPlayer.Collider);
            foreach(var volume in volumes) if(volume!.isActiveAndEnabled && volume.col!=null && volume.col.enabled && volume.col.bounds.Intersects(localPlayer.Collider.bounds)) volume.OnTriggerEnter(localPlayer.Collider);
        }
        foreach(var platform in _catalogue.Platforms) if(platform!=null)
        {
            platform.ClearMovables();platform.UpdateLastPosition();
            var player=Player.Instance;
            if(player?.IsAlive==true && CarrierOverlaps(platform,player.Collider.bounds))
                platform.OnTriggerEnter(player.Collider);
        }
    }
    private void TickReplicaPlatformCarry()
    {
        if(_catalogue==null) return;
        foreach(var platform in _catalogue.Platforms)
        {
            if(platform==null) continue;
            if(!_platformEnabled.ContainsKey(platform)) {_platformEnabled[platform]=platform.enabled;platform.enabled=false;platform.UpdateLastPosition();}
            // The host already moves shared props. Carry only the local player,
            // once, after the replica's received transform updates.
            var player=Player.Instance;
            // A pause may arrive before the host's final deck pose. Keep a
            // ready rider attached to that authoritative correction even while
            // frozen; discarding it lets the deck/underside kill plane move
            // through P2. Restoration/travel still rebase rather than carry.
            if(_life.PersonalReady && _life.State.Released && !RestoringNative && player?.IsAlive==true && platform._transformToFollow!=null)
            {
                var delta=platform._transformToFollow.position-platform._lastPostion;
                var body=player.Collider.bounds;var carried=new Bounds(body.center+delta,body.size);
                // Child collider exit callbacks can remove the root's native
                // membership even while the rider still occupies its volume.
                // Compare both sides of a received step so a fast deck cannot
                // leave its rider behind before contact callbacks catch up.
                if(CarrierOverlaps(platform,body) || CarrierOverlaps(platform,carried)) CarryReplicaPlayer(platform,delta);
            }
            platform.UpdateLastPosition();
        }
    }
    private void ClearMechanisms()
    {
        ClearMachineAudio();ClearRockets();ClearArena();_rotaterContinuations.Clear();_cannonTargets.Clear();_gateFuses.Clear();_sharedSecrets.Clear();_personalSecrets.Clear();_falls.Clear();_scales.Clear();_mechanismState.Clear();
        foreach(var pair in _platformEnabled) if(pair.Key!=null) {pair.Key.UpdateLastPosition();pair.Key.enabled=pair.Value;}
        _platformEnabled.Clear();
    }
    private void SendRemoteShake()
    {var pose=_session.Snapshot.RemotePose;if(pose?.Alive==true) Send(new(WorldKind.PlayerEffect,_epoch,1,Actor:2,Life:pose.Life,Flags:2));}
    private void SendRemotePush(Vector3 velocity,bool additive)
    {var pose=_session.Snapshot.RemotePose;if(pose?.Alive==true && !_life.Frozen) Send(new(WorldKind.PlayerEffect,_epoch,1,Actor:2,Life:pose.Life,Flags:1,Velocity:Net(Vector3.ClampMagnitude(velocity,100)),Value:additive?1:0));}
    private void ApplyPlayerEffect(WorldFrame f)
    {
        if(f.Actor!=2 || f.Life!=_life.Life || !Player.Instance.IsAlive) return;
        if(f.Flags==3) ApplyTeleport(f);
        else if(f.Flags==1) Player.Instance.Push(Unity(f.Velocity),f.Value!=0);
        else if(f.Flags==2) Player.Instance.ShakeCamera(new Vector3(.04f,.04f,.04f),new Vector3(.5f,.5f,.5f),.3f,15);
        else throw new InvalidDataException("Unknown player world effect.");
    }
}
