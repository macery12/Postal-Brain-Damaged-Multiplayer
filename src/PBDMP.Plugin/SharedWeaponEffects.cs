using Hyperstrange.PBD;
using Il2CppInterop.Runtime;
using PBDMP.Network;
using UnityEngine;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    internal RemotePlayerVisual? PeerVisual;
    private readonly Dictionary<(byte Actor,string Key),FMODInstance> _chargeAudio=new();
    internal IEnumerable<WeaponAction.FxModule> EffectsForTest=>_assets!.Effects.Values;
    internal int FxEventsForTest {get;private set;}
    internal int ChargeLoopsForTest=>_chargeAudio.Count;
    // Per-mode callbacks supply effects; the observer still updates pose animations.
    internal void WeaponFired(PlayerWeapon weapon) { }
    internal void WeaponFx(WeaponAction.FxModule fx,uint action)
    {
        if(!Active || _life.Frozen || Player.Instance?.IsAlive!=true || _assets!.Bind(fx) is not {} key) return;
        var camera=Player.Instance.WorldCamera.transform;
        Send(new(Client?WorldKind.ClientEffect:WorldKind.WeaponEffect,_epoch,1,Key:key,Actor:Client?(byte)2:(byte)1,Life:_life.Life,
            Position:Net(camera.position+camera.forward*0.7f),Rotation:Net(camera.eulerAngles),Flags:action));
    }
    private void ForwardWeaponEffect(WorldFrame frame)
    {
        var pose=_session.Snapshot.RemotePose;
        if(frame.Flags==111)
        {if(!_life.Frozen && pose?.Alive==true && frame.Actor==2 && frame.Life==pose.Life){Send(frame with{Kind=WorldKind.WeaponEffect});PlayPlayerAction(frame);}return;}
        if(frame.Flags is 100 or 101 or 102)
        {
            if(!_life.Frozen && pose?.Alive==true && frame.Actor==2 && frame.Life==pose.Life && _streamPrefabs.ContainsKey(frame.Key))
            {Send(frame with{Kind=WorldKind.WeaponEffect});PlayStream(frame);}return;
        }
        if(_life.Frozen || pose?.Alive!=true || frame.Actor!=2 || frame.Life!=pose.Life || !_assets!.Effects.ContainsKey(frame.Key) ||
            frame.Flags>4 || Vector3.Distance(Unity(frame.Position),new(pose.X,pose.Y,pose.Z))>8f) return;
        Send(frame with{Kind=WorldKind.WeaponEffect});PlayWeaponEffect(frame);
    }
    private void PlayWeaponEffect(WorldFrame frame)
    {
        if(frame.Flags is 130 or 131) {ApplyPlayerContact(frame);return;}
        if(frame.Flags is 110 or 111) {PlayPlayerAction(frame);return;}
        if(frame.Flags is 100 or 101 or 102) {PlayStream(frame);return;}
        if(frame.Actor==(Client?2:1) || !_assets!.Effects.TryGetValue(frame.Key,out var fx)) return;
        FxEventsForTest++;
        var key=(frame.Actor,frame.Key);var position=Unity(frame.Position);
        if(frame.Flags==3)
        {if(_chargeAudio.Remove(key,out var stop)){stop.Stop(false);stop.Release();}return;}
        if(frame.Flags==2)
        {
            if(!_chargeAudio.ContainsKey(key) && fx._chargeEvent!=null)
            {var sound=new FMODInstance(fx._chargeEvent);sound.SetPosition(position);sound.Play();_chargeAudio[key]=sound;}
            return;
        }
        var rotation=Quaternion.Euler(Unity(frame.Rotation));
        if(PeerVisual is {HasWeapon:true} visual && _assets.EffectOrigins.TryGetValue(frame.Key,out var origin) && visual.WeaponKey==origin)
        {position=visual.MuzzlePosition;rotation=visual.MuzzleRotation;}
        var source=fx._muzzleFlash?._muzzleFlash;
        if(frame.Flags<=1 && source!=null)
        {var effect=new NativePresentation(source.gameObject);effect.Move(position,rotation);_effects.Add((effect,Time.unscaledTime+2f));}
        var audio=frame.Flags==1?fx._fireChargeFullEvent??fx._fireEvent:frame.Flags==4?fx._chargeFullEvent:fx._fireEvent;
        audio?.PlayOneShot(position);
    }
    private void TickWeaponAudio()
    {
        var pose=_session.Snapshot.RemotePose;
        foreach(var pair in _chargeAudio.ToArray())
        {
            if(pose?.Alive!=true) {pair.Value.Stop(false);pair.Value.Release();_chargeAudio.Remove(pair.Key);}
            else {pair.Value.SetPosition(new(pose.X,pose.Y+pose.Height*0.5f,pose.Z));pair.Value.SetVolume(_life.Frozen?0:1);}
        }
    }
    private void ClearWeaponAudio()
    {foreach(var sound in _chargeAudio.Values){sound.Stop(false);sound.Release();}_chargeAudio.Clear();}
}
