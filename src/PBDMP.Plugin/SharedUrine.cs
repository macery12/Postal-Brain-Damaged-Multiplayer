using System.Globalization;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using Hyperstrange.PBD.NpcComponents;
using PBDMP.Network;
using UnityEngine;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    private readonly Dictionary<string,string[]> _urineState=new();
    private readonly Dictionary<string,float[]> _urineSurfaceState=new();
    private readonly Dictionary<string,(float Time,int Count)> _urineRequests=new();
    private readonly HashSet<IntPtr> _urineSounds=new();
    private readonly Dictionary<Animator,bool> _urineAnimators=new();
    private readonly HashSet<DG.Tweening.Tween> _urinePausedTweens=new();
    private bool? _urineFrozen;
    private void BuildUrineSounds()
    {foreach(var sound in _catalogue!.UrineAudioAssets.Values) _urineSounds.Add(sound.Pointer);foreach(var key in _catalogue.UrineKeys) foreach(var sound in NativeUrineSounds(_catalogue.Components[key])) if(sound!=null) _urineSounds.Add(sound.Pointer);}
    internal bool OwnsUrineConsequence(Component component)=>_catalogue?.Key(component) is {} key && _catalogue.UrineConsequences.Contains(key);
    private void SendUrineConsequence(string key,bool complete)
    {
        var c=_catalogue!.Components[key];if(c==null || c.GetIl2CppType().Name is not ("EmissionAnimator" or "Laser" or "RailSwitch")) return;
        var data=WorldCatalogue.Save(c);
        if(complete || !_urineState.TryGetValue(key,out var prior) || !data.SequenceEqual(prior)) {_urineState[key]=data;Send(new(WorldKind.UrineState,_epoch,1,Id(key),Key:key,Flags:16,Data:data));}
    }
    internal int UrineRequestsApplied {get;private set;}
    internal int UrineEffectsPlayed {get;private set;}
    internal bool ClientUrine(Component target,uint action=70,bool stagger=true)
    {
        if(!Client || !Active || Applying) return false;
        var key=_catalogue!.Key(target);
        if(key==null || !_catalogue.UrineKeys.Contains(key)) return false;
        if(!_life.Frozen && Player.Instance.IsAlive)
            Send(new(WorldKind.Special,_epoch,1,Key:key,Actor:2,Life:_life.Life,Flags:action,Value:stagger?1:0,Position:Net(Player.Instance.CenterOfMass)));
        return true;
    }
    private void ApplyUrineRequest(WorldFrame frame)
    {
        if(frame.Flags==74) {ApplyUrineBoarding(frame);return;}
        if(frame.Flags==72) {ApplyProjectileUrine(frame);return;}
        if(!_catalogue!.UrineKeys.Contains(frame.Key) || !_catalogue.Components.TryGetValue(frame.Key,out var c) || c==null) return;
        if(c.TryCast<MetalButton>()!=null) return; // Uses the requester-owned interaction handshake.
        if(frame.Flags==76)
        {if(Vector3.Distance(Unity(frame.Position),c.transform.position)>20 || Vector3.Distance(c.transform.position,_remote!.CenterOfMass)>100) return;}
        else if(Vector3.Distance(Unity(frame.Position),_remote!.CenterOfMass)>3 || Vector3.Distance(c.transform.position,_remote.CenterOfMass)>15) return;
        // Preserve native per-callback fill increments, with a bounded request
        // rate. Continuous listener callbacks are normally fixed-update paced.
        var rate=_urineRequests.GetValueOrDefault(frame.Key);
        if(Time.unscaledTime-rate.Time>=1) rate=(Time.unscaledTime,0);
        if(rate.Count>=120) return;
        _urineRequests[frame.Key]=(rate.Time,rate.Count+1);
        if(frame.Flags==75 && c.TryCast<IFlammable>() is {} flammable)
            flammable.FlameTick(_remote.Actor,Mathf.Clamp(frame.Value,0,0.25f),Mathf.Clamp(frame.Value2,0,10000));
        else if(frame.Flags==76 && c.TryCast<ExplosiveBarrel>() is {} barrel)
        {var hit=DecodeHit(frame with{Flags=frame.Revision,Value=Mathf.Clamp(frame.Value,0,10000),Value2=Mathf.Clamp(frame.Value2,0,100)});hit.dealer=_remote.Actor;barrel.Hit(hit);}
        else if(frame.Flags==71 && c.TryCast<PissFiller>() is {} filler) filler.OnPiss(new PissData(_remote.Actor,frame.Value!=0));
        else if(frame.Flags==70 && c.TryCast<IPissable>() is {} target) target.Piss(new PissData(_remote.Actor,frame.Value!=0));
        else return;
        UrineRequestsApplied++;
    }
    internal static string[] UrineSnapshot(Component c)
    {
        var data=WorldCatalogue.Save(c);
        if(c.TryCast<PissFiller>()!=null)
            for(var i=0;i+1<data.Length;i+=2) if(data[i]=="FillPercentage")
                data[i+1]=NativeProgress.Unit(float.Parse(data[i+1],CultureInfo.InvariantCulture)).ToString("R",CultureInfo.InvariantCulture);
        return data;
    }
    private void SendUrineState(string key,bool complete)
    {
        var c=_catalogue!.Components[key];if(c==null) return;
        var data=UrineSnapshot(c);
        if(c.TryCast<Toilet>() is {} toilet) data=data.Concat(new[]{"Flushing",toilet._isFlushing.ToString(),"Cycle",toilet._cycleIndex.ToString(CultureInfo.InvariantCulture)}).ToArray();
        var peeing=c.TryCast<Toilet>() is {} audioToilet && Time.time-audioToilet._lastHitTime<0.1f;
        if(complete || peeing || !_urineState.TryGetValue(key,out var prior) || !data.SequenceEqual(prior))
        {_urineState[key]=data;Send(new(WorldKind.UrineState,_epoch,1,Id(key),Key:key,Data:data,Value:peeing?1:0));}
    }
    internal float[]? ReceivedSurfaceForTest(string key)=>_urineSurfaceState.GetValueOrDefault(key);
    private void SendUrineSurface(string key,Component[] components,bool complete)
    {
        var t=_catalogue!.EncounterMotion[key];if(t==null) return;
        var values=components.Select(c=>SurfaceEnabled(c)?1f:0f).ToArray();
        if(complete || !_urineSurfaceState.TryGetValue(key,out var prior) || !values.SequenceEqual(prior))
        {_urineSurfaceState[key]=values;Send(new(WorldKind.UrineState,_epoch,1,Id(key),Key:key,Flags:4,Values:values));}
    }
    private static bool SurfaceEnabled(Component c)=>c!=null && c switch {Renderer r=>r.enabled,Collider col=>col.enabled,Light light=>light.enabled,UnityEngine.AI.NavMeshObstacle obstacle=>obstacle.enabled,OcclusionPortal portal=>portal.open,_=>false};
    private static void SetSurfaceEnabled(Component c,bool enabled)
    {
        if(c==null) return;
        if(c is Renderer renderer) renderer.enabled=enabled;
        else if(c is Collider collider) collider.enabled=enabled;
        else if(c is Light light) light.enabled=enabled;
        else if(c is UnityEngine.AI.NavMeshObstacle obstacle) obstacle.enabled=enabled;
        else if(c is OcclusionPortal portal) portal.open=enabled;
    }
    private void ApplyUrineState(WorldFrame frame)
    {
        if(frame.Flags==32) {ApplyUrineWagon(frame);return;}
        if(frame.Flags==16)
        {
            if(!_catalogue!.UrineConsequences.Contains(frame.Key)) throw new InvalidDataException("Unknown urine consequence.");
            var consequence=_catalogue.Components[frame.Key];if(consequence==null) return;
            if(consequence.GetIl2CppType().Name is not ("EmissionAnimator" or "Laser" or "RocketSequence" or "RailSwitch")) throw new InvalidDataException("Unsafe urine consequence restore.");
            WorldCatalogue.Load(consequence,frame.Data??Array.Empty<string>());return;
        }
        if(frame.Flags==8)
        {
            if(!_catalogue!.UrineKeys.Contains(frame.Key) || _catalogue.Components[frame.Key].TryCast<Toilet>() is not {} flushing) throw new InvalidDataException("Unknown toilet flush.");
            flushing._flushParticle?.Play();UrineEffectsPlayed++;return;
        }
        if(frame.Flags==2)
        {
            if(_effectSounds.TryGetValue(frame.Asset,out var sound)) sound.PlayOneShot(Unity(frame.Position));
            if(_effectPrefabs.TryGetValue(frame.Key,out var source)) {var visual=new NativePresentation(source);visual.Move(Unity(frame.Position),Quaternion.identity);_effects.Add((visual,Time.unscaledTime+5));}
            UrineEffectsPlayed++;return;
        }
        if(frame.Flags==4)
        {
            if(!_catalogue!.UrineSurfaces.TryGetValue(frame.Key,out var components) || frame.Values is not {} values || values.Any(v=>v is not (0 or 1))) throw new InvalidDataException("Invalid urine collision/presentation state.");
            // Legacy development frames omitted portal bits. Native baseline
            // restoration supplies those; preserve exact ordering of old bits.
            var targets=values.Length==components.Length?components:components.Where(c=>c.TryCast<OcclusionPortal>()==null).ToArray();
            if(values.Length!=targets.Length) throw new InvalidDataException("Invalid urine surface layout.");
            if(Environment.GetEnvironmentVariable("PBDMP_WORLD_REGRESSION")=="1") _urineSurfaceState[frame.Key]=values.ToArray();
            for(var i=0;i<values.Length;i++) SetSurfaceEnabled(targets[i],values[i]!=0);
            return;
        }
        if(frame.Flags!=0 || !_catalogue!.UrineKeys.Contains(frame.Key)) throw new InvalidDataException("Unknown urine target state.");
        var c=_catalogue.Components[frame.Key];if(c==null) return;
        var data=frame.Data??Array.Empty<string>();
        try
        {
            if(c.TryCast<Toilet>() is {} toilet)
            {
                if(data.Length!=8 || data[0]!="_currentWaterLevel" || data[2]!="_isOpen" || data[4]!="Flushing" || data[6]!="Cycle") throw new InvalidDataException("Invalid toilet state.");
                toilet._currentWaterLevel=Mathf.Clamp01(float.Parse(data[1],CultureInfo.InvariantCulture));toilet._isOpen=bool.Parse(data[3]);
                toilet._isFlushing=bool.Parse(data[5]);toilet._cycleIndex=int.Parse(data[7],CultureInfo.InvariantCulture);
                toilet._lastHitTime=frame.Value==1?Time.time+0.15f:Time.time-1;
                toilet.UpdateWaterLevel(true);
            }
            else if(c.TryCast<PissFiller>() is {} filler)
            {
                if(data.Length%2!=0) throw new InvalidDataException("Invalid urine fill state.");
                var fields=new Dictionary<string,string>(StringComparer.Ordinal);
                for(var i=0;i<data.Length;i+=2) if(!fields.TryAdd(data[i],data[i+1])) throw new InvalidDataException("Duplicate urine fill field.");
                var tank=filler.TryCast<PissTank>();
                if(fields.Count!=(tank!=null?3:2) || !fields.ContainsKey("_filled") || !fields.ContainsKey("FillPercentage") || tank!=null && !fields.ContainsKey("_activeState")) throw new InvalidDataException("Unknown urine fill fields.");
                var filled=bool.Parse(fields["_filled"]);var percent=float.Parse(fields["FillPercentage"],CultureInfo.InvariantCulture);
                percent=NativeProgress.Unit(percent);
                var changed=filler._filled!=filled;filler._filled=filled;filler.FillPercentage=percent;
                filler.OnFillChanged?.Invoke(percent);
                if(tank!=null)
                {
                    var active=bool.Parse(fields["_activeState"]);changed|=tank._activeState!=active;tank._activeState=active;
                    if(changed) {tank.SetColors(filled?PissTank.StateEnum.FILLED:active?PissTank.StateEnum.ACTIVE:PissTank.StateEnum.DISABLED);tank.EnableTriggers(active && !filled);}
                    tank.TileContainer();tank._onTankMoveInstance?.SetParameter("PissTankPeeParameter",percent is >0 and <1?1:0);
                    // Host motion owns pistons/totems/cogs. Native Load snaps
                    // them to endpoints and can overwrite an unchanged pose.
                }
            }
            else if(c.TryCast<ExplosiveBarrel>()!=null || c.TryCast<BurnableObject>()!=null) return;
            else if(c.TryCast<ISaveable>()!=null)
            {
                WorldCatalogue.Load(c,data);
                if(c.TryCast<ButtonShootable>() is {} button) {button.SetColor(button._interacted?button._interactColor:button._initialColor);if(button._overrideLight) button.SetLightColor(button._lightColor,button._interacted?button._lightIntensity:0);}
            }
        }
        catch(Exception error) {Plugin.Logger.LogError($"Urine state failed: target={c.name}, type={c.GetIl2CppType().Name}, key={frame.Key}, epoch={frame.Epoch}, state=[{string.Join(';',data)}]\n{error}");throw;}
    }
    internal bool PermitWorldOneShot(FMODEvent sound)=>!RestoringNative || !_urineSounds.Contains(sound.Pointer);
    internal void UrineSound(FMODEvent sound,Vector3 position)
    {
        if(!Active || Client || Applying || RestoringNative || _catalogue==null) return;
        if(!_urineSounds.Contains(sound.Pointer)) return;
        var key=EffectKey("audio",sound.name);_effectSounds[key]=sound;
        Send(new(WorldKind.UrineState,_epoch,1,Asset:key,Position:Net(position),Flags:2));
    }
    private static IEnumerable<FMODEvent?> NativeUrineSounds(Component c)
    {
        if(c.TryCast<Toilet>() is {} toilet) {yield return toilet._flushingEvent;yield return toilet._peeEvent;}
        if(c.TryCast<PissTank>() is {} tank) {yield return tank._onTankFilledEvent;yield return tank._onTankMoveEvent;}
        if(c.TryCast<ButtonShootable>() is {} button) yield return button._activateEvent;
        if(c.TryCast<MetalButton>() is {} metal) yield return metal._pressSFX;
    }
    internal void ToiletFlushPresentation(Toilet toilet)
    {
        if(!Active || Client || _catalogue!.Key(toilet) is not {} key || !_catalogue.UrineKeys.Contains(key) || toilet._flushParticle==null) return;
        Send(new(WorldKind.UrineState,_epoch,1,Key:key,Flags:8));
    }
    private void ClearUrine()
    {ResumeUrineTweens();_urineFrozen=null;foreach(var pair in _urineAnimators) if(pair.Key!=null) pair.Key.enabled=pair.Value;_urineAnimators.Clear();_urineState.Clear();_urineSurfaceState.Clear();_urineRequests.Clear();_urineSounds.Clear();ClearUrineWagons();}
    private void TickUrinePause()
    {
        var frozen=_life.Frozen;if(_urineFrozen==frozen || _catalogue==null) return;
        _urineFrozen=frozen;
        if(!frozen) {ResumeUrineTweens();return;}
        // Native toilet flushing uses an unscaled tween. Hold it during the
        // shared pause/join barrier, then resume only tweens paused by PBDMP.
        if(!Client) foreach(var key in _catalogue.UrineKeys)
        {
            var tween=_catalogue.Components[key].TryCast<Toilet>()?._flushTween;
            if(tween is {active:true,isPlaying:true}) {tween.isPlaying=false;_urinePausedTweens.Add(tween);}
        }
    }
    private void ResumeUrineTweens()
    {foreach(var tween in _urinePausedTweens) if(tween.active) tween.isPlaying=true;_urinePausedTweens.Clear();}
    private void StopReplicaUrineAnimation(string key,Transform transform)
    {
        if(!_catalogue!.UrineSurfaces.ContainsKey(key)) return;
        var animator=transform.GetComponent<Animator>();if(animator==null) return;
        if(!_urineAnimators.ContainsKey(animator)) _urineAnimators[animator]=animator.enabled;
        animator.enabled=false;
    }
}
