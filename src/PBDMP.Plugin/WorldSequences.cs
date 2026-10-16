using FMODUnity;
using HarmonyLib;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using PBDMP.Network;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    private sealed class SequenceView
    {
        internal TimelineSequence Sequence=null!;
        internal WorldFrame Frame=null!;
        internal float ReceivedAt;
        internal bool Live;
        internal DirectorUpdateMode UpdateMode;
        internal readonly Dictionary<TrackAsset,bool> Muted=new();
        internal readonly Dictionary<Animator,bool> Events=new();
        internal readonly Dictionary<IntPtr,FMODInstance> Sounds=new();
        internal readonly Dictionary<IntPtr,NativePresentation> Effects=new();
    }
    private readonly Dictionary<string,SequenceView> _sequences=new();
    private readonly Dictionary<string,WorldFrame> _sequenceState=new();
    private readonly Dictionary<string,(double Last,bool Playing,double? Stopped,double Peak)> _hostSequencePhases=new();
    internal float SequencePeakForTest(string key)=>(float)_hostSequencePhases.GetValueOrDefault(key).Peak;
    internal void SequenceReset(TimelineSequence sequence)
    {if(!Client && _catalogue?.Key(sequence) is {} key) _hostSequencePhases.Remove(key);}
    private void TickHostSequencePhases()
    {
        foreach(var key in _catalogue!.SequenceKeys)
        {
            var sequence=_catalogue.Components[key].Cast<TimelineSequence>();var director=sequence._playableDirector;
            if(director==null || !sequence._sequenceStarted) {_hostSequencePhases.Remove(key);continue;}
            var old=_hostSequencePhases.GetValueOrDefault(key);var time=director.time;var playing=director.state==PlayState.Playing;
            // A completed director can reset time to initialTime while retaining
            // the final animated pose. Keep the phase that actually played.
            var stopped=playing?null:old.Playing && time<old.Last-.01?old.Last:old.Stopped;
            _hostSequencePhases[key]=(time,playing,stopped,Math.Max(old.Peak,time));
        }
    }
    private IEnumerable<int> CaptureSequences(bool complete)
    {
        foreach(var key in _catalogue!.SequenceKeys)
        {
            var sequence=_catalogue.Components[key].Cast<TimelineSequence>();var director=sequence._playableDirector;
            if(director==null) {yield return 0;continue;}
            var frame=new WorldFrame(WorldKind.Sequence,_epoch,1,Id(key),Key:key,
                Flags:(sequence._sequenceStarted?1u:0)|(director.state==PlayState.Playing?2u:0),
                Value:(float)(_hostSequencePhases.GetValueOrDefault(key).Stopped??director.time),Value2:(float)director.duration);
            if(complete || !_sequenceState.TryGetValue(key,out var old) || old.Flags!=frame.Flags || old.Value!=frame.Value)
            {Send(frame);_sequenceState[key]=frame;}
            yield return 0;
        }
    }
    private void ApplySequence(WorldFrame f,bool hostRestore=false)
    {
        if(!_catalogue!.SequenceKeys.Contains(f.Key) || f.Flags>3 || f.Value<0 || f.Value2<0 || f.Value>f.Value2+.1f || f.Value2>86400)
            throw new InvalidDataException("Invalid campaign timeline state.");
        var sequence=_catalogue.Components[f.Key].Cast<TimelineSequence>();var director=sequence._playableDirector;
        sequence._sequenceStarted=(f.Flags&1)!=0;sequence._sequenceTime=f.Value;
        if(hostRestore)
        {
            director.Stop();director.initialTime=f.Value;
            var retroactive=new Dictionary<SignalEmitter,bool>();
            if(director.playableAsset.TryCast<TimelineAsset>() is {} savedTimeline)
                foreach(var track in savedTimeline.flattenedTracks.ToArray()) for(var i=0;i<track.GetMarkerCount();i++)
                    if(track.GetMarker(i).TryCast<SignalEmitter>() is {} signal) {retroactive[signal]=signal.retroactive;signal.retroactive=false;}
            try {if((f.Flags&2)!=0) {director.Play();director.time=f.Value;director.Evaluate();}else director.time=f.Value;}
            finally {foreach(var pair in retroactive) pair.Key.retroactive=pair.Value;}
            _hostSequencePhases[f.Key]=(f.Value,(f.Flags&2)!=0,(f.Flags&2)==0?(double?)f.Value:null,f.Value);
            return;
        }
        if(!_sequences.TryGetValue(f.Key,out var view))
        {
            view=new(){Sequence=sequence,UpdateMode=director.timeUpdateMode};_sequences[f.Key]=view;
            director.Stop();
            if(director.playableAsset.TryCast<TimelineAsset>() is {} timeline)
            foreach(var track in timeline.flattenedTracks.ToArray())
            {
                view.Muted[track]=track.muted;
                // Signal receivers and control scripts may invoke gameplay. Only
                // animation goes through the graph; audio/VFX have passive replicas.
                track.muted=track.TryCast<AnimationTrack>()==null;
                var animator=director.GetGenericBinding(track)?.TryCast<Animator>();
                if(animator!=null) {view.Events[animator]=animator.fireEvents;animator.fireEvents=false;}
            }
            director.timeUpdateMode=DirectorUpdateMode.Manual;director.RebuildGraph();director.Pause();
        }
        view.Frame=f;view.ReceivedAt=Time.time;
        // Evaluate the animation-only graph even under a join/pause barrier.
        // This also restores completed bindings without replaying any signals.
        if((f.Flags&1)!=0) {director.time=f.Value;director.Evaluate();}
        // A snapshot restores phase/geometry. It never replays completed cues.
        view.Live=_snapshotApplied && (f.Flags&2)!=0;
        if((f.Flags&2)==0) StopSequenceCues(view);
    }
    private void TickSequences()
    {
        if(!Client) {TickHostSequencePhases();return;}
        if(_life.Frozen) return;
        foreach(var view in _sequences.Values)
        {
            var f=view.Frame;if(f==null || (f.Flags&2)==0 || view.Sequence==null) continue;
            var director=view.Sequence._playableDirector;
            var time=Math.Min(f.Value2,f.Value+Mathf.Clamp(Time.time-view.ReceivedAt,0,.2f));
            director.time=time;director.Evaluate();
            if(!view.Live || director.playableAsset.TryCast<TimelineAsset>() is not {} timeline) continue;
            var active=new HashSet<IntPtr>();
            foreach(var track in timeline.flattenedTracks.ToArray()) foreach(var clip in track.clips.ToArray())
            {
                if(time<clip.start || time>=clip.end) continue;
                var id=clip.Pointer;active.Add(id);
                if(clip.asset.TryCast<FMODEventPlayable>() is {} sound)
                {
                    if(!view.Sounds.TryGetValue(id,out var instance))
                    {
                        var target=director.GetGenericBinding(track)?.TryCast<GameObject>()??view.Sequence.gameObject;
                        instance=new FMODInstance(sound.EventReference,target);
                        if(sound.Parameters!=null) foreach(var parameter in sound.Parameters) instance.SetParameter(parameter.Name,parameter.Value);
                        instance.Play();instance.SetTimelinePosition((int)(((time-clip.start)*clip.timeScale+clip.clipIn)*1000));view.Sounds[id]=instance;
                    }
                }
                else if(clip.asset.TryCast<ControlPlayableAsset>() is {} control)
                {
                    var source=control.sourceGameObject.Resolve(director.Cast<IExposedPropertyTable>());
                    if(source==null) continue;
                    if(!view.Effects.TryGetValue(id,out var effect))
                    {
                        effect=new NativePresentation(source);view.Effects[id]=effect;
                        effect.Tick((float)Math.Max(0,(time-clip.start)*clip.timeScale+clip.clipIn));
                    }
                    effect.Move(source.transform.position,source.transform.rotation);effect.Tick(Time.deltaTime);
                }
            }
            foreach(var id in view.Sounds.Keys.Where(id=>!active.Contains(id)).ToArray())
            {view.Sounds[id].Stop(false);view.Sounds[id].Release();view.Sounds.Remove(id);}
            foreach(var id in view.Effects.Keys.Where(id=>!active.Contains(id)).ToArray())
            {view.Effects[id].Dispose();view.Effects.Remove(id);}
        }
    }
    private static void StopSequenceCues(SequenceView view)
    {
        foreach(var sound in view.Sounds.Values) {sound.Stop(false);sound.Release();}view.Sounds.Clear();
        foreach(var effect in view.Effects.Values) effect.Dispose();view.Effects.Clear();
    }
    private void ClearSequences()
    {
        foreach(var view in _sequences.Values)
        {
            StopSequenceCues(view);
            if(view.Sequence!=null) view.Sequence._playableDirector.timeUpdateMode=view.UpdateMode;
            foreach(var pair in view.Muted) if(pair.Key!=null) pair.Key.muted=pair.Value;
            foreach(var pair in view.Events) if(pair.Key!=null) pair.Key.fireEvents=pair.Value;
        }
        _sequences.Clear();_sequenceState.Clear();_hostSequencePhases.Clear();
    }
    internal bool PermitSequenceStart(TimelineSequence sequence)=>!Active || !OwnsMission(sequence) || !Client && !RestoringNative;
}

internal static class WorldSequencePatches
{
    internal static void Install(Harmony h)
    {
        h.Patch(AccessTools.Method(typeof(TimelineSequence),"StartSequence"),prefix:new(typeof(WorldSequencePatches),nameof(Start)));
        h.Patch(AccessTools.Method(typeof(TimelineSequence),"ResetState"),prefix:new(typeof(WorldSequencePatches),nameof(Reset)));
        h.Patch(AccessTools.Method(typeof(SignalReceiver),"OnNotify"),prefix:new(typeof(WorldSequencePatches),nameof(Signal)));
    }
    private static bool Signal(SignalReceiver __instance)=>SharedWorld.Current is not {Active:true} world || !world.OwnsMission(__instance) || !world.Client && !world.RestoringNative;
    private static bool Reset(TimelineSequence __instance)
    {
        var world=SharedWorld.Current;if(world?.PermitSequenceStart(__instance)==false) return false;
        world?.SequenceReset(__instance);return true;
    }
    private static bool Start(TimelineSequence __instance)=>Reset(__instance);
}
