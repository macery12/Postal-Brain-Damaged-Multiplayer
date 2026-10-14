using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using PBDMP.Network;
using UnityEngine;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    private (string Key,byte Actor,uint Life)? _missionExitInteraction;
    private (string Key,uint Life)? _clientExitInteraction;
    internal bool HoldMissionExitInteraction(Interactable c,Player player)
    {
        if(!Active || _finishingMission || _remoteInteraction || _catalogue!.Key(c) is not {} key || !_catalogue.MissionInteractionExits.Contains(key)) return false;
        if(_life.Frozen || !player.IsAlive) return true;
        if(Client)
        {
            if(_clientExitInteraction is not {} pending || pending.Key!=key || pending.Life!=_life.Life)
            {_clientExitInteraction=(key,_life.Life);Send(new(WorldKind.Special,_epoch,1,Key:key,Actor:2,Life:_life.Life,Flags:82,Position:Net(player.FeetPosition)));}
        }
        else _missionExitInteraction=(key,1,_life.Life);
        return true;
    }
    private bool NearInteractionExit(string key,Vector3 feet,float height)
    {
        var c=_catalogue!.Components[key];if(c==null || !c.gameObject.activeInHierarchy) return false;
        var center=feet+Vector3.up*(height*0.5f);
        // Native interactables often use child render/collision geometry rather
        // than a trigger area. A fixed nearby gathering radius preserves F-to-use.
        return Vector3.Distance(center,c.transform.position)<4.5f || c.GetComponentsInChildren<Collider>().Any(col=>col.enabled && Vector3.Distance(col.ClosestPoint(center),center)<3);
    }
    private bool BothNearInteractionExit(string key)
    {
        if(!BothMissionPlayersReady() || !NearInteractionExit(key,Player.Instance.FeetPosition,Player.Instance.Height)) return false;
        var pose=_session.Snapshot.RemotePose;
        return !_session.Snapshot.Connected || pose!=null && NearInteractionExit(key,new(pose.X,pose.Y,pose.Z),pose.Height);
    }
    private void TickClientExitInteraction()
    {
        if(_clientExitInteraction is not {} pending || !Active || _life.Frozen) return;
        if(pending.Life!=_life.Life || !Player.Instance.IsAlive || !NearInteractionExit(pending.Key,Player.Instance.FeetPosition,Player.Instance.Height)) {_clientExitInteraction=null;return;}
        var pose=_session.Snapshot.RemotePose;
        if(!_life.WorldReady || !_life.State.Released || pose is not {Alive:true,Ready:true} || pose.Epoch!=_epoch ||
            !NearInteractionExit(pending.Key,Player.Instance.FeetPosition,Player.Instance.Height) || !NearInteractionExit(pending.Key,new(pose.X,pose.Y,pose.Z),pose.Height)) return;
        _clientExitInteraction=null;
        RequestDoor(_catalogue!.Components[pending.Key].Cast<Interactable>(),Player.Instance);
    }
    private void ApplyMissionExitRequest(WorldFrame frame)
    {
        if(!_catalogue!.MissionInteractionExits.Contains(frame.Key) || Vector3.Distance(Unity(frame.Position),_remote!.FeetPosition)>3 || !NearInteractionExit(frame.Key,_remote.FeetPosition,_remote.Height)) return;
        _missionExitInteraction=(frame.Key,2,frame.Life);
    }
    private bool TickHostExitInteraction()
    {
        if(_missionExitInteraction is not {} pending) return false;
        var pose=_session.Snapshot.RemotePose;
        var requesterAlive=pending.Actor==1?Player.Instance.IsAlive && pending.Life==_life.Life:
            _session.Snapshot.Connected && pose is {Alive:true} && pending.Life==pose.Life;
        if(!requesterAlive) {_missionExitInteraction=null;SetMissionWait("","");return false;}
        var requesterFeet=pending.Actor==1?Player.Instance.FeetPosition:new Vector3(pose!.X,pose.Y,pose.Z);
        var requesterHeight=pending.Actor==1?Player.Instance.Height:pose!.Height;
        if(!NearInteractionExit(pending.Key,requesterFeet,requesterHeight)) {_missionExitInteraction=null;SetMissionWait("","");return false;}
        if(!BothNearInteractionExit(pending.Key)) {SetMissionWait(pending.Key,"Waiting for partner at the exit.");return true;}
        if(pending.Actor==2) return true; // P2 validates/consumes their own items through the existing handshake.
        _missionExitInteraction=null;_finishingMission=true;var before=MissionFinishesForTest;
        try {_catalogue!.Components[pending.Key].Cast<Interactable>().HandleInteract(Player.Instance);} finally {_finishingMission=false;}
        if(MissionFinishesForTest>before) {MissionExitsForTest++;_finishedMissionExits.Add(pending.Key);}
        SetMissionWait("","");return true;
    }
}
