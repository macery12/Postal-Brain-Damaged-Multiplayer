using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using PBDMP.Network;
using UnityEngine;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    internal void ClientSpecial(Npc npc,uint action,float delta=0,float intensity=0,float multiplier=0,bool state=false)
    {
        if(!Active || _life.Frozen || !_enemyIds.TryGetValue(npc.Pointer,out var id)) return;
        Send(new(WorldKind.Special,_epoch,1,id,Actor:2,Life:_life.Life,Flags:action,Value:delta,Value2:intensity,Values:new[]{multiplier,state?1f:0f}));
    }
    internal void ClientDoorHit(Component door,HitData hit)
    {
        if(!Active || _life.Frozen || _catalogue!.Key(door) is not {} key || hit.projectile!=null || hit.dealer?.Pointer!=Player.Instance?.Pointer) return;
        Send(new(WorldKind.Special,_epoch,1,Key:key,Asset:hit.originEntity?.ID??"",Actor:2,Life:_life.Life,Flags:1,Position:Net(hit.position),Rotation:Net(hit.direction),Velocity:Net(hit.pushVelocity),Value:hit.damage,Value2:HitFlags(hit)));
    }
    internal void ClientDoorPush(ToiletDoor door,Vector3 velocity,bool additive)
    {
        if(!Active || _life.Frozen || _catalogue!.Key(door) is not {} key) return;
        Send(new(WorldKind.Special,_epoch,1,Key:key,Actor:2,Life:_life.Life,Flags:2,Velocity:Net(velocity),Value:additive?1:0));
    }
    internal bool ClientHookable(Component component,uint action)
    {
        if(!Client || !Active || _catalogue!.Key(component) is not {} key || !_catalogue.HookableKeys.Contains(key)) return false;
        if(!_life.Frozen) Send(new(WorldKind.Special,_epoch,1,Key:key,Actor:2,Life:_life.Life,Flags:action));
        return true;
    }
    private void ApplySpecial(WorldFrame frame)
    {
        var pose=_session.Snapshot.RemotePose;
        if(_life.Frozen || pose?.Alive!=true || frame.Actor!=2 || frame.Life!=pose.Life || _remote==null) return;
        if(frame.Flags is >=70 and <=74) {ApplyUrineRequest(frame);return;}
        if(frame.Flags==80) {ApplyMissionHit(frame);return;}
        if(frame.Flags==81) {ApplyMissionFall(frame);return;}
        if(frame.Flags==82) {ApplyMissionExitRequest(frame);return;}
        if(frame.Flags==40) {ApplySpecialExplosion(frame);return;}
        if(frame.Flags==50) {ApplyCatRecall(frame);return;}
        if(frame.Flags==51) {ApplyHomingTarget(frame);return;}
        if(frame.Flags==60) {ApplyScriptRun(frame);return;}
        if(frame.Flags is 52 or 53) {ApplyCatAction(frame);return;}
        if(frame.Flags is 30 or 31 or 32)
        {
            if(!_catalogue!.HookableKeys.Contains(frame.Key)) return;
            var component=_catalogue.Components[frame.Key];if(Vector3.Distance(component.transform.position,_remote.CenterOfMass)>100f) return;
            var hook=component.TryCast<IHookable>();
            if(hook==null) return;
            if(frame.Flags==30) hook.OnHookStarted(_remote.Actor);else if(frame.Flags==31) hook.OnHookReached(_remote.Actor);else hook.OnUnhooked(_remote.Actor);
            return;
        }
        if(frame.Flags<=2)
        {
            if(!_catalogue!.DoorKeys.Contains(frame.Key) || !_catalogue.Components.TryGetValue(frame.Key,out var component)) return;
            var door=component.TryCast<IHittable>();if(door==null || Vector3.Distance(component.transform.position,_remote.CenterOfMass)>8f) return;
            if(frame.Flags==1) door.Hit(new HitData{dealer=_remote.Actor,originEntity=_catalogue.Assets.GetValueOrDefault(frame.Asset),damage=Mathf.Clamp(frame.Value,0,10000),position=Unity(frame.Position),direction=Unity(frame.Rotation),pushVelocity=Unity(frame.Velocity),isQuickMelee=((uint)frame.Value2&4)!=0,isPowerful=((uint)frame.Value2&8)!=0});
            else component.TryCast<ToiletDoor>()?.Push(Vector3.ClampMagnitude(Unity(frame.Velocity),100),frame.Value!=0);
            return;
        }
        if(!_enemies.TryGetValue(frame.Entity,out var npc) || npc==null || !npc.IsAlive || Vector3.Distance(npc.CenterOfMass,_remote.CenterOfMass)>100f) return;
        var data=frame.Values??Array.Empty<float>();
        switch(frame.Flags)
        {
            case 10:npc.OnHookStarted(_remote.Actor);break;
            case 11:npc.OnHookReached(_remote.Actor);break;
            case 12:npc.OnHookHit(_remote.Actor);break;
            case 13:npc.OnUnhooked(_remote.Actor);break;
            case 20:npc.FlameTick(_remote.Actor,Mathf.Clamp(frame.Value,0,0.25f),Mathf.Clamp(frame.Value2,0,10000));break;
            case 21:if(data.Length==2) npc.FreezeTick(_remote.Actor,Mathf.Clamp(frame.Value,0,0.25f),frame.Value2,data[0]);break;
            case 22:if(data.Length==2) npc.ConfuseTick(_remote.Actor,Mathf.Clamp(frame.Value,0,0.25f),data[1]!=0);break;
            case 23:npc.Piss(new PissData(_remote.Actor,frame.Value!=0));break;
        }
    }
}
