using HarmonyLib;
using Hyperstrange.Base;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using PBDMP.Network;
using UnityEngine;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    private sealed record SyringeView(AntiCovidSyringe Syringe,WorldFrame Frame,float At,bool Enabled,bool Events);
    private readonly Dictionary<string,SyringeView> _syringeViews=new();
    private readonly Dictionary<string,NativePresentation> _syringeEffects=new();
    private static Cooldown? MachineCooldown(Component c)=>c.TryCast<Cannon>()?._shootCooldown??c.TryCast<AntiCovidSyringeController>()?._injectCooldown;
    private void HoldMachineCooldowns()
    {
        if(Client || !_life.Frozen || _catalogue==null) return;
        foreach(var key in _catalogue.MechanismKeys)
            if(MachineCooldown(_catalogue.Components[key]) is { } cooldown && cooldown._unscaledTime && cooldown._started)
                cooldown._timeStamp+=Time.unscaledDeltaTime;
    }
    private void PublishMechanism(string identity,WorldFrame frame,bool complete)
    {
        if(complete || !_mechanismState.TryGetValue(identity,out var old) || old.Position!=frame.Position || old.Revision!=frame.Revision ||
            old.Value!=frame.Value || old.Value2!=frame.Value2 || !(old.Values??Array.Empty<float>()).SequenceEqual(frame.Values??Array.Empty<float>()) ||
            !(old.Data??Array.Empty<string>()).SequenceEqual(frame.Data??Array.Empty<string>()))
        {_mechanismState[identity]=frame;Send(frame);}
    }
    private IEnumerable<int> CaptureArena(bool complete)
    {
        foreach(var key in _catalogue!.MechanismKeys)
        {
            var c=_catalogue.Components[key];if(c==null) continue;
            if(c.TryCast<CovidArenaController>() is {} arena)
            {
                var order=arena._segments.ToArray().Select(s=>_catalogue.Key(s)??throw new InvalidOperationException("Missing authored arena segment.")).ToArray();
                if(order.Length>32) throw new InvalidOperationException("Arena segment order exceeds bounded adapter.");
                PublishMechanism(key+"|arena",new(WorldKind.Mechanism,_epoch,1,Id(key),Key:key,Flags:9,
                    Position:Net(arena._arenaBase.transform.position),Data:order),complete);
            }
            if(MachineCooldown(c) is {} cooldown)
                PublishMechanism(key+"|cooldown",new(WorldKind.Mechanism,_epoch,1,Id(key),Key:key,Flags:10,
                    Value:Mathf.Max(0,cooldown.TimeLeft),Value2:cooldown._duration,
                    Values:new[]{cooldown._started?1f:0,cooldown._unscaledTime?1f:0,cooldown._difficultyBased?1f:0}),complete);
            if(c.TryCast<AntiCovidSyringe>() is {} syringe && syringe._animator is {} animator)
            {
                var info=animator.GetCurrentAnimatorStateInfo(0);var particles=syringe._injectVfx?.ParticleSystem;
                PublishMechanism(key+"|syringe",new(WorldKind.Mechanism,_epoch,1,Id(key),Key:key,Flags:11,Revision:unchecked((uint)info.fullPathHash),
                    Value:Mathf.Clamp(info.normalizedTime,0,1000000),Value2:Mathf.Clamp(info.length,0,3600),
                    Values:new[]{animator.speed,animator.GetInteger(syringe._currentStateParam),syringe._injected?1f:0,syringe._canInject?1f:0,particles?.IsAlive(true)==true?1f:0,Mathf.Clamp(particles?.time??0,0,30)}),complete);
            }
            yield return 0;
        }
    }
    private void ApplyArena(WorldFrame f,bool hostRestore)
    {
        if(!_catalogue!.MechanismKeys.Contains(f.Key)) throw new InvalidDataException("Unknown arena mechanism.");
        var c=_catalogue.Components[f.Key];
        if(f.Flags==9 && c.TryCast<CovidArenaController>() is {} arena && f.Data is {} order)
        {
            if(order.Length!=arena._segments.Length || order.Distinct().Count()!=order.Length) throw new InvalidDataException("Invalid arena segment order.");
            var authored=arena._segments.ToArray().Select(s=>s.Pointer).ToHashSet();
            var segments=order.Select(k=>_catalogue.Components.GetValueOrDefault(k)?.TryCast<CovidArenaSegment>()??throw new InvalidDataException("Unknown arena segment.")).ToArray();
            if(segments.Any(s=>!authored.Contains(s.Pointer))) throw new InvalidDataException("Arena segment belongs to another controller.");
            for(var i=0;i<segments.Length;i++) arena._segments[i]=segments[i];
            arena._arenaBase.transform.position=Unity(f.Position);return;
        }
        if(f.Flags==10 && MachineCooldown(c) is {} cooldown && f.Values is {Length:3} v && v.All(n=>n is 0 or 1) && f.Value>=0 && f.Value<=3600 && f.Value2>=0 && f.Value2<=3600)
        {
            cooldown._duration=f.Value2;cooldown._started=v[0]!=0;cooldown._unscaledTime=v[1]!=0;cooldown._difficultyBased=v[2]!=0;
            cooldown._timeStamp=(cooldown._unscaledTime?Time.unscaledTime:Time.time)-cooldown.Duration+f.Value;return;
        }
        if(f.Flags==11 && c.TryCast<AntiCovidSyringe>() is {} syringe && f.Values is {Length:6} a && a[0]>=0 && a[0]<=100 && a[1] is 0 or 1 or 2 && a[2] is 0 or 1 && a[3] is 0 or 1 && a[4] is 0 or 1 && a[5]>=0 && a[5]<=30 && f.Value>=0 && f.Value2>=0 && f.Value2<=3600)
        {
            syringe._injected=a[2]!=0;syringe._canInject=a[3]!=0;
            var animator=syringe._animator;if(animator==null) throw new InvalidDataException("Missing syringe animation binding.");
            animator.SetInteger(syringe._currentStateParam,(int)a[1]);
            if(hostRestore)
            {
                // Cosmetic particles are cleared on disk restore. Eligibility
                // and cooldown retain completed gameplay, without repeating a hit.
                syringe._injectVfx?.StopImmediate();SampleSyringe(syringe,f,f.Value);animator.speed=a[0];return;
            }
            if(a[4]!=0 && !_syringeEffects.ContainsKey(f.Key))
            {
                var source=syringe._injectVfx??throw new InvalidDataException("Missing native syringe particle source.");
                var effect=new NativePresentation(source.gameObject);effect.Move(source.transform.position,source.transform.rotation);effect.Tick(a[5]);_syringeEffects[f.Key]=effect;
            }
            else if(a[4]==0 && _syringeEffects.Remove(f.Key,out var ended)) ended.Dispose();
            if(!_syringeViews.TryGetValue(f.Key,out var prior)) prior=new(syringe,f,Time.time,animator.enabled,animator.fireEvents);
            animator.enabled=false;animator.fireEvents=false;
            _syringeViews[f.Key]=prior with {Frame=f,At=Time.time};SampleSyringe(syringe,f,f.Value);return;
        }
        throw new InvalidDataException("Invalid arena mechanism state.");
    }
    private static void SampleSyringe(AntiCovidSyringe syringe,WorldFrame frame,float phase)
    {
        var animator=syringe._animator;var hash=unchecked((int)frame.Revision);
        if(hash==0 || !animator.HasState(0,hash)) return;
        var events=animator.fireEvents;animator.fireEvents=false;
        try {animator.Play(hash,0,phase);animator.Update(0);}
        finally {animator.fireEvents=events;}
    }
    private void TickSyringeViews()
    {
        if(_life.Frozen) return;
        foreach(var view in _syringeViews.Values)
            if(view.Syringe!=null && view.Frame.Value2>0)
                SampleSyringe(view.Syringe,view.Frame,view.Frame.Value+Mathf.Clamp(Time.time-view.At,0,.2f)*view.Frame.Values![0]/view.Frame.Value2);
        foreach(var pair in _syringeEffects)
            if(_catalogue!.Components[pair.Key].TryCast<AntiCovidSyringe>()?._injectVfx is {} source)
            {pair.Value.Move(source.transform.position,source.transform.rotation);pair.Value.Tick(Time.deltaTime);}
    }
    internal int SyringeParticlesForTest=>_syringeEffects.Values.Sum(e=>e.ParticleCount);
    private void ClearArena()
    {
        foreach(var view in _syringeViews.Values) if(view.Syringe?._animator is {} animator)
        {animator.fireEvents=view.Events;animator.enabled=view.Enabled;}
        foreach(var effect in _syringeEffects.Values) effect.Dispose();_syringeEffects.Clear();
        _syringeViews.Clear();
    }
}

internal static class WorldArenaPatches
{
    internal static void Install(Harmony harmony)
    {
        // OnSlowUpdate is a tiny forwarding stub on the arena controller.
        // Patch its substantive body and preserve local registration handles.
        foreach(var (type,names) in new[]{
            (typeof(CovidArenaController),new[]{"UpdatePlayerCoord","HandlePlayerNewCoord","ShiftSegments","OnCovidKilled","OnSyringePrepared","OnSyringePull"}),
            (typeof(CovidArenaSegment),new[]{"OnShift","OnCovidKilled","SetContraptionsState"}),
            (typeof(AntiCovidSyringeController),new[]{"Prepare","ForcePrepare","Inject","PullOut","OnSlowUpdate","OnShift","Wait","WaitEnd","OnCovidKilled"}),
            (typeof(AntiCovidSyringe),new[]{"OnTriggerEnterCallback"}),
            (typeof(CovidArenaBase),new[]{"SetContraptionsActiveState","ReleaseCovid"})})
            foreach(var name in names) harmony.Patch(AccessTools.Method(type,name),prefix:new(typeof(WorldArenaPatches),nameof(Host)));
    }
    private static bool Host(Component __instance)=>SharedWorld.Current is not {Active:true} world || !world.OwnsMission(__instance) || !world.Client && !world.RestoringNative && !SessionLifecycle.Current!.Frozen;
}
