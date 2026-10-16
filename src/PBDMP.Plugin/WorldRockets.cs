using DG.Tweening;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using PBDMP.Network;
using UnityEngine;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    private sealed class RocketClock {internal RocketSequence Rocket=null!;internal float From,Elapsed;}
    private sealed record RocketView(RocketSequence Rocket,WorldFrame Frame,float At,bool Enabled,bool Events);
    private readonly Dictionary<string,RocketClock> _rocketClocks=new();
    private readonly Dictionary<string,RocketView> _rocketViews=new();
    internal bool StartRocketLaunch(RocketSequence rocket)
    {
        if(!Active || !OwnsMission(rocket)) return false;
        if(Client || RestoringNative || (int)rocket._rocketState!=1) return true;
        var key=_catalogue!.Key(rocket)!;if(_rocketClocks.ContainsKey(key)) return true;
        rocket._rocketState=(RocketSequence.RocketState)2;rocket.OnStateChanged?.Invoke(2);
        // Native ShootRocket moves local Z to -14 over .25 s, using InQuint.
        // Own that short clock so a mid-launch checkpoint retains its callback.
        rocket._rocketHead.transform.DOKill(false);
        _rocketClocks[key]=new(){Rocket=rocket,From=rocket._rocketHead.transform.localPosition.z};return true;
    }
    private void TickRocketLaunches()
    {
        foreach(var pair in _rocketClocks.ToArray())
        {
            var clock=pair.Value;var rocket=clock.Rocket;
            if(rocket==null || rocket._rocketHead==null) {_rocketClocks.Remove(pair.Key);continue;}
            clock.Elapsed=Mathf.Min(.25f,clock.Elapsed+Time.deltaTime);var t=clock.Elapsed/.25f;
            var p=rocket._rocketHead.transform.localPosition;p.z=Mathf.LerpUnclamped(clock.From,-14,t*t*t*t*t);rocket._rocketHead.transform.localPosition=p;
            if(t<1) continue;
            _rocketClocks.Remove(pair.Key);rocket._OnRocketHeadHit?.Invoke();rocket._rocketHead.SetActive(false);
        }
    }
    private IEnumerable<int> CaptureRockets(bool complete)
    {
        foreach(var key in _catalogue!.MechanismKeys)
        {
            if(_catalogue.Components[key].TryCast<RocketSequence>() is not {} rocket || rocket._animator==null || rocket._rocketHead==null) continue;
            var info=rocket._animator.GetCurrentAnimatorStateInfo(0);var clock=_rocketClocks.GetValueOrDefault(key);
            var frame=new WorldFrame(WorldKind.Mechanism,_epoch,1,Id(key),Revision:unchecked((uint)info.fullPathHash),Key:key,Flags:8,
                Position:Net(rocket._rocketHead.transform.localPosition),Value:Mathf.Clamp(info.normalizedTime,0,1000000),Value2:Mathf.Clamp(info.length,0,3600),
                Values:new[]{(float)rocket._rocketState,rocket._fallenCalled?1f:0,rocket._animator.speed,clock?.Elapsed??0,clock?.From??0,clock!=null?1f:0});
            if(complete || !_mechanismState.TryGetValue(key,out var old) || old.Position!=frame.Position || old.Revision!=frame.Revision || old.Value!=frame.Value || old.Value2!=frame.Value2 || !old.Values!.SequenceEqual(frame.Values!))
            {_mechanismState[key]=frame;Send(frame);}
            yield return 0;
        }
    }
    private void ApplyRocket(WorldFrame f,bool hostRestore)
    {
        if(!_catalogue!.MechanismKeys.Contains(f.Key) || _catalogue.Components[f.Key].TryCast<RocketSequence>() is not {} rocket ||
            f.Values is not {Length:6} v || v[0] is not (0 or 1 or 2) || v[1] is not (0 or 1) || v[2]<0 || v[2]>100 || v[3]<0 || v[3]>.25f || v[5] is not (0 or 1) || f.Value<0 || f.Value2<0 || f.Value2>3600)
            throw new InvalidDataException("Invalid rocket sequence phase.");
        rocket._rocketState=(RocketSequence.RocketState)(int)v[0];rocket._fallenCalled=v[1]!=0;
        var animator=rocket._animator;
        if(rocket._rocketHead==null || animator==null) throw new InvalidDataException("Missing authored rocket sequence references.");
        rocket._rocketHead.transform.DOKill(false);rocket._rocketHead.transform.localPosition=Unity(f.Position);
        rocket._rocketHead.SetActive(v[0]!=2 || v[5]!=0);
        animator.SetInteger(rocket._currentStateParam,v[0]==0?0:1);
        if(hostRestore)
        {
            _rocketClocks.Remove(f.Key);
            if(v[5]!=0) _rocketClocks[f.Key]=new(){Rocket=rocket,From=v[4],Elapsed=v[3]};
            SampleRocket(rocket,f,f.Value);animator.speed=v[2];return;
        }
        if(!_rocketViews.TryGetValue(f.Key,out var prior)) prior=new(rocket,f,Time.time,animator.enabled,animator.fireEvents);
        animator.enabled=false;animator.fireEvents=false;
        _rocketViews[f.Key]=prior with {Frame=f,At=Time.time};SampleRocket(rocket,f,f.Value);
    }
    private static void SampleRocket(RocketSequence rocket,WorldFrame frame,float normalized)
    {
        var animator=rocket._animator;var hash=unchecked((int)frame.Revision);
        if(hash==0 || !animator.HasState(0,hash)) return;
        var events=animator.fireEvents;animator.fireEvents=false;
        try {animator.Play(hash,0,normalized);animator.Update(0);}
        finally {animator.fireEvents=events;}
    }
    private void TickRocketViews()
    {
        if(_life.Frozen) return;
        foreach(var view in _rocketViews.Values)
        {
            if(view.Rocket==null || view.Frame.Value2<=0) continue;
            var elapsed=Mathf.Clamp(Time.time-view.At,0,.2f);
            SampleRocket(view.Rocket,view.Frame,view.Frame.Value+elapsed*view.Frame.Values![2]/view.Frame.Value2);
        }
    }
    private void ClearRockets()
    {
        foreach(var view in _rocketViews.Values) if(view.Rocket?._animator is {} animator)
        {animator.fireEvents=view.Events;animator.enabled=view.Enabled;}
        _rocketViews.Clear();_rocketClocks.Clear();
    }
}
