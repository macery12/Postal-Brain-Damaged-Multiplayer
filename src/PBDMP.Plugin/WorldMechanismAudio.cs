using System.Reflection;
using Hyperstrange.PBD;
using PBDMP.Network;
using UnityEngine;

namespace PBDMP.Plugin;

internal sealed partial class WorldCatalogue
{
    internal sealed record MachineAudio(string Key,string Field,Component Component,object Actual,PropertyInfo Instance,FMODEvent Source);
    internal readonly Dictionary<string,MachineAudio> MachineSounds=new();
    private void BuildMachineSounds()
    {
        foreach(var key in MechanismKeys)
        {
            var c=Components[key];var actual=NativeAssetGraph.Actual(c);var type=actual.GetType();
            foreach(var (field,source) in new[]{("_moveInstance","_moveEvent"),("_musicInstance","_musicEvent"),("_rotateInstance","_rotateEvent")})
            {
                var instance=type.GetProperty(field);var asset=type.GetProperty(source)?.GetValue(actual) as FMODEvent;
                if(instance?.PropertyType!=typeof(FMODInstance) || asset==null) continue;
                MachineSounds[key+"|"+field]=new(key,field,c,actual,instance,asset);
            }
        }
    }
}

internal sealed partial class SharedWorld
{
    private readonly Dictionary<string,FMODInstance> _machineSounds=new();
    private readonly Dictionary<IntPtr,FMOD.Studio.EventInstance> _pausedWorldSounds=new();
    private bool _worldAudioFrozen;
    private float _audioPauseScan;
    private IEnumerable<int> CaptureMachineAudio(bool complete)
    {
        foreach(var pair in _catalogue!.MachineSounds)
        {
            var binding=pair.Value;var instance=binding.Component!=null?binding.Instance.GetValue(binding.Actual) as FMODInstance:null;
            var playing=instance?.IsPlaying==true;
            var frame=new WorldFrame(WorldKind.Mechanism,_epoch,1,Id(pair.Key),Key:binding.Key,Asset:binding.Field,Flags:5,
                Value:playing?Mathf.Clamp(instance!.GetTimelinePosition()/1000f,0,86400):0,Values:new[]{playing?1f:0});
            if(complete || !_mechanismState.TryGetValue(pair.Key,out var old) || old.Value!=frame.Value || old.Values![0]!=frame.Values![0])
            {_mechanismState[pair.Key]=frame;Send(frame);}
            yield return 0;
        }
    }
    private void ApplyMachineAudio(WorldFrame frame,bool hostRestore)
    {
        var key=frame.Key+"|"+frame.Asset;
        if(!_catalogue!.MachineSounds.TryGetValue(key,out var binding) || frame.Values is not {Length:1} v || v[0] is not (0 or 1) || frame.Value<0 || frame.Value>86400)
            throw new InvalidDataException("Unknown machinery audio state.");
        var instance=hostRestore?(binding.Component!=null?binding.Instance.GetValue(binding.Actual) as FMODInstance:null):_machineSounds.GetValueOrDefault(key);
        if(v[0]==0)
        {
            if(hostRestore) instance?.Stop(false);
            else if(_machineSounds.Remove(key,out var old)) {old.Stop(false);old.Release();}
            return;
        }
        if(binding.Component==null) return;
        if(instance==null)
        {
            instance=new FMODInstance(binding.Source,binding.Component.transform);
            if(hostRestore) binding.Instance.SetValue(binding.Actual,instance);else _machineSounds[key]=instance;
        }
        if(!instance.IsPlaying) {instance.Play();instance.SetTimelinePosition((int)(frame.Value*1000));}
        else if(hostRestore || Math.Abs(instance.GetTimelinePosition()-frame.Value*1000)>500) instance.SetTimelinePosition((int)(frame.Value*1000));
        if(_life.Frozen) PauseWorldSound(instance._eventInstance);
    }
    private void PauseWorldSound(FMOD.Studio.EventInstance instance)
    {
        if(!instance.isValid() || _pausedWorldSounds.ContainsKey(instance.handle)) return;
        instance.getPaused(out var paused);
        if(!paused) {instance.setPaused(true);_pausedWorldSounds[instance.handle]=instance;}
    }
    private void TickWorldAudioPause()
    {
        if(_catalogue==null) return;
        var frozen=_life.Frozen;
        if(!frozen && _worldAudioFrozen)
        {
            foreach(var instance in _pausedWorldSounds.Values) if(instance.isValid()) instance.setPaused(false);
            _pausedWorldSounds.Clear();
        }
        _worldAudioFrozen=frozen;
        if(!frozen || Time.unscaledTime<_audioPauseScan) return;_audioPauseScan=Time.unscaledTime+.2f;
        foreach(var instance in _machineSounds.Values) PauseWorldSound(instance._eventInstance);
        if(!Client) foreach(var binding in _catalogue.MachineSounds.Values)
            if(binding.Component!=null && binding.Instance.GetValue(binding.Actual) is FMODInstance instance) PauseWorldSound(instance._eventInstance);
        foreach(var view in _sequences.Values) foreach(var instance in view.Sounds.Values) PauseWorldSound(instance._eventInstance);
        if(!Client) foreach(var key in _catalogue.SequenceKeys)
            if(_catalogue.Components[key].TryCast<Hyperstrange.PBD.Contraptions.TimelineSequence>()?._playableDirector.playableAsset.TryCast<UnityEngine.Timeline.TimelineAsset>() is {} timeline)
                foreach(var track in timeline.flattenedTracks.ToArray()) foreach(var clip in track.clips.ToArray())
                    if(clip.asset.TryCast<FMODUnity.FMODEventPlayable>()?.behavior is {} behavior) PauseWorldSound(behavior.eventInstance);
    }
    private void ClearMachineAudio()
    {
        foreach(var instance in _pausedWorldSounds.Values) if(instance.isValid()) instance.setPaused(false);
        _pausedWorldSounds.Clear();_worldAudioFrozen=false;
        foreach(var instance in _machineSounds.Values) {instance.Stop(false);instance.Release();}_machineSounds.Clear();
    }
}
