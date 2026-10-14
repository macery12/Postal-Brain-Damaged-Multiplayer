using System.Security.Cryptography;
using System.Text;
using Hyperstrange.PBD;
using PBDMP.Network;
using UnityEngine;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    private readonly Dictionary<string,GameObject> _effectPrefabs=new();
    private readonly Dictionary<string,FMODEvent> _effectSounds=new();
    private static string EffectKey(string type,string name)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(type+"/"+name)));
    private void BuildExplosionAssets()
    {
        _effectPrefabs.Clear();_effectSounds.Clear();
        foreach(var source in Resources.FindObjectsOfTypeAll<ParticleController>())
            if(!source.gameObject.scene.IsValid()) _effectPrefabs[EffectKey("vfx",source.name)]=source.gameObject;
        foreach(var source in Resources.FindObjectsOfTypeAll<FMODEvent>()) _effectSounds[EffectKey("audio",source.name)]=source;
    }
    internal bool ExplosionInvoked(Explosion explosion,Vector3 position,IAiActor owner,EntityId origin)
    {
        if(!Active) return true;
        var settings=explosion.Settings;
        var key=settings.vfx!=null?EffectKey("vfx",settings.vfx.name):"";
        var audio=settings.sfxEvent!=null?EffectKey("audio",settings.sfxEvent.name):"";
        if(Client)
        {
            // Projectile replicas are inert; their original frozen local objects must
            // not explode when returned to the pool. Non-projectile special attacks
            // originate in native player weapons and resolve through P1 once.
            if(!_clearingProjectiles && owner?.Pointer==Player.Instance?.Pointer && !_localShotObjects.Values.Any(p=>p!=null && p._explosionComponent?._explosion?.Pointer==explosion.Pointer))
                Send(new(WorldKind.Special,_epoch,1,Key:key,Asset:audio,Actor:2,Life:_life.Life,Flags:40,Position:Net(position),Value:settings.radius,Value2:settings.damage,
                    Values:new[]{settings.pushForce,settings.ragdollForce,settings.upwardsForceModifier,settings.forceLowHp?1f:0f,settings.spawnPickupsOnKill?1f:0f}));
            return false;
        }
        if(settings.vfx!=null) _effectPrefabs[key]=settings.vfx.gameObject;
        if(settings.sfxEvent!=null) _effectSounds[audio]=settings.sfxEvent;
        Send(new(WorldKind.Impact,_epoch,1,Key:key,Asset:audio,Flags:2,Position:Net(position),Value:settings.radius,Value2:settings.useParticleOriginalSize?1:0));
        return true;
    }
    internal int SolidImpactEffectsForTest {get;private set;}
    internal int EffectParticlesForTest=>_effects.Sum(e=>e.Visual.ParticleCount);
    internal bool SolidImpact(SolidObjectModel model,RaycastHit hit)
    {
        if(!Active) return true;
        // P2's native melee can reach this callback before its authoritative hit.
        // Let P1 select the flesh/armor material and publish the visual once.
        if(Client) return Applying || hit.collider?.GetComponentInParent<Npc>()==null;
        if(hit.collider?.GetComponentInParent<Npc>()==null) PublishSolidImpact(model,hit.point,hit.normal);
        return true;
    }
    private void PublishSolidImpact(SolidObjectModel model,Vector3 point,Vector3 normal)
    {
        var key=model.HitVfx!=null?EffectKey("vfx",model.HitVfx.name):"";
        var audio=model.HitSfx!=null?EffectKey("audio",model.HitSfx.name):"";
        if(model.HitVfx!=null) _effectPrefabs[key]=model.HitVfx.gameObject;
        if(model.HitSfx!=null) _effectSounds[audio]=model.HitSfx;
        Send(new(WorldKind.Impact,_epoch,1,Key:key,Asset:audio,Flags:4,Position:Net(point),Rotation:Net(normal),Value2:1));
    }
    private void PlayExplosion(WorldFrame frame)
    {
        var position=Unity(frame.Position);
        if((frame.Flags&4)!=0) SolidImpactEffectsForTest++;
        if(_effectSounds.TryGetValue(frame.Asset,out var sound)) sound.PlayOneShot(position);
        if(_effectPrefabs.TryGetValue(frame.Key,out var source))
        {
            var effect=new NativePresentation(source);effect.Move(position,(frame.Flags&4)!=0?Quaternion.FromToRotation(Vector3.forward,Unity(frame.Rotation)):Quaternion.identity);
            if(frame.Value2==0) effect.Root.transform.localScale=Vector3.one*Mathf.Max(0.01f,frame.Value*2f);
            _effects.Add((effect,Time.unscaledTime+5f));
        }
    }
    internal int SpecialExplosionsForTest {get;private set;}
    private void ApplySpecialExplosion(WorldFrame frame)
    {
        if(frame.Value<=0 || frame.Value>100 || frame.Value2<0 || frame.Value2>100000 || Vector3.Distance(Unity(frame.Position),_remote!.CenterOfMass)>15f || frame.Values is not {Length:5} data) return;
        var settings=new ExplosionSettings{radius=frame.Value,damage=frame.Value2,pushForce=data[0],ragdollForce=data[1],upwardsForceModifier=data[2],forceLowHp=data[3]!=0,spawnPickupsOnKill=data[4]!=0};
        if(_effectPrefabs.TryGetValue(frame.Key,out var source)) settings.vfx=source.GetComponent<ParticleController>();
        if(_effectSounds.TryGetValue(frame.Asset,out var sound)) settings.sfxEvent=sound;
        new Explosion(settings).Invoke(Unity(frame.Position),_remote.Actor,
            particleRotation:new Il2CppSystem.Nullable<Quaternion>());
        SpecialExplosionsForTest++;
    }
}
