using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using Hyperstrange.PBD.Ready;
using PBDMP.Network;
using UnityEngine;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    private LevelController? _pendingMissionEnd;
    private bool _finishingMission;
    private string? _lastMissionWait;
    private float _missionPollAt;
    private readonly Dictionary<string,bool> _missionPresence=new();
    private readonly HashSet<string> _finishedMissionExits=new();
    private readonly Dictionary<string,float> _missionVolumeHitAt=new();
    internal string MissionStatus {get;private set;}="";
    internal int MissionFinishesForTest {get;private set;}
    internal int MissionExitsForTest {get;private set;}
    internal bool MissionEndPendingForTest=>_pendingMissionEnd!=null;
    internal bool IsMissionExit(Component c)=>_catalogue?.Key(c) is {} key && _catalogue.MissionExitKeys.Contains(key);
    private bool BothMissionPlayersReady()=>Player.Instance?.IsAlive==true && (!_session.Snapshot.Connected ||
        _life.WorldReady && _life.State.Released && _session.Snapshot.RemotePose is {Alive:true,Ready:true} pose && pose.Epoch==_epoch);
    internal bool FinishMission(LevelController level)
    {
        if(!Active) return true;
        if(_finishingMission) {MissionFinishesForTest++;return true;}
        if(Client) return false;
        if(BothMissionPlayersReady() && !_life.Frozen) {MissionFinishesForTest++;return true;}
        _pendingMissionEnd=level;SetMissionWait("","Waiting for both players to be alive and loaded.");return false;
    }
    private void TickMissionProgress()
    {
        if(!Active || _life.Frozen) return;
        if(Client) {TickClientExitInteraction();return;}
        if(_pendingMissionEnd!=null)
        {
            if(!BothMissionPlayersReady()) return;
            var level=_pendingMissionEnd;_pendingMissionEnd=null;SetMissionWait("","");_finishingMission=true;
            try {level.EndLevel(LevelController.EndLevelType.FINISH);} finally {_finishingMission=false;}
            return;
        }
        if(Time.unscaledTime<_missionPollAt) return;_missionPollAt=Time.unscaledTime+0.1f;
        TickMissionTriggerPresence();TickMissionVolumes();
        if(TickHostExitInteraction()) return;
        string? waiting=null;
        foreach(var key in _catalogue!.MissionExitKeys)
        {
            if(_catalogue.MissionInteractionExits.Contains(key))
            {
                var poseForExit=_session.Snapshot.RemotePose;
                if(!_finishedMissionExits.Contains(key) && !BothNearInteractionExit(key) && (Player.Instance?.IsAlive==true && NearInteractionExit(key,Player.Instance.FeetPosition,Player.Instance.Height) ||
                    _session.Snapshot.Connected && poseForExit is {Alive:true,Ready:true} && NearInteractionExit(key,new(poseForExit.X,poseForExit.Y,poseForExit.Z),poseForExit.Height))) waiting=key;
                continue;
            }
            var c=_catalogue.Components[key];var trigger=c.TryCast<PlayerTrigger>();
            if(c==null || _finishedMissionExits.Contains(key) || !c.gameObject.activeInHierarchy || c.TryCast<MonoBehaviour>()?.enabled!=true || trigger!=null && (!trigger._enabled || trigger._triggeredOnEnter)) continue;
            var local=Player.Instance?.IsAlive==true && MissionOverlaps(key,Player.Instance.FeetPosition,Player.Instance.Height);
            var pose=_session.Snapshot.RemotePose;
            var remote=_session.Snapshot.Connected && pose is {Alive:true,Ready:true} && pose.Epoch==_epoch && MissionOverlaps(key,new(pose.X,pose.Y,pose.Z),pose.Height);
            if(!local && !remote) continue;
            if(!local || _session.Snapshot.Connected && !remote || !BothMissionPlayersReady()) {waiting=key;continue;}
            SetMissionWait("","");_finishedMissionExits.Add(key);_finishingMission=true;
            MissionExitsForTest++;
            try {if(trigger!=null) {trigger._triggeredOnEnter=true;trigger.OnPlayerEnter?.Invoke(Player.Instance);}else LevelController._instance.CloseLevel();} finally {_finishingMission=false;}
            Plugin.Logger.LogInfo($"Both-player campaign exit accepted: {WorldCatalogue.DiagnosticPath(c.transform)}; P1={Player.Instance?.FeetPosition}; P2={pose?.X}/{pose?.Y}/{pose?.Z}; epoch={_epoch}");return;
        }
        SetMissionWait(waiting??"",waiting!=null?"Waiting for partner at the exit.":"");
    }
    private bool MissionOverlaps(string key,Vector3 feet,float height)
    {
        height=Mathf.Clamp(height,0.5f,3f);var bounds=new Bounds(feet+Vector3.up*(height*0.5f),new(0.7f,height,0.7f));
        return _catalogue!.MissionContacts.TryGetValue(key,out var colliders) && colliders.Any(c=>c!=null && c.enabled && c.gameObject.activeInHierarchy && c.bounds.Intersects(bounds));
    }
    private void TickMissionTriggerPresence()
    {
        var pose=_session.Snapshot.RemotePose;
        foreach(var key in _catalogue!.MissionTriggerKeys)
        {
            if(_catalogue.MissionExitKeys.Contains(key)) continue;
            var trigger=_catalogue.Components[key].TryCast<PlayerTrigger>();if(trigger==null || !trigger._enabled || !trigger.gameObject.activeInHierarchy) continue;
            var inside=Player.Instance.IsAlive && MissionOverlaps(key,Player.Instance.FeetPosition,Player.Instance.Height) ||
                _session.Snapshot.Connected && pose is {Alive:true,Ready:true} && pose.Epoch==_epoch && MissionOverlaps(key,new(pose.X,pose.Y,pose.Z),pose.Height);
            if(inside==_missionPresence.GetValueOrDefault(key)) continue;_missionPresence[key]=inside;
            trigger._player=Player.Instance;
            if(inside && (trigger._multiUse || !trigger._triggeredOnEnter))
            {trigger._triggeredOnEnter=true;if(trigger.TryCast<global::PlayerTriggerSpawnEnemies>() is {} spawn) spawn.SpawnEnemies();else trigger.OnPlayerEnter?.Invoke(Player.Instance);}
            else if(!inside && (trigger._multiUse || !trigger._triggeredOnExit)) {trigger._triggeredOnExit=true;trigger.OnPlayerExit?.Invoke(Player.Instance);}
        }
    }
    private void TickMissionVolumes()
    {
        var pose=_session.Snapshot.RemotePose;if(!_session.Snapshot.Connected || pose is not {Alive:true,Ready:true} || pose.Epoch!=_epoch || _remote==null) return;
        foreach(var key in _catalogue!.MissionVolumeKeys)
        {
            var volume=_catalogue.Components[key].TryCast<GameplayVolume>();
            if(volume==null || !volume.isActiveAndEnabled || volume.kind==VolumeKind.Win || !MissionOverlaps(key,new(pose.X,pose.Y,pose.Z),pose.Height)) {_missionVolumeHitAt.Remove(key);continue;}
            if(volume.kind==VolumeKind.Kill) {Send(new(WorldKind.MissionState,_epoch,1,Key:key,Actor:2,Life:pose.Life,Flags:512));continue;}
            if(Time.time<_missionVolumeHitAt.GetValueOrDefault(key)) continue;
            _missionVolumeHitAt[key]=Time.time+Mathf.Clamp(volume.interval,0.05f,60);
            _remote.Hit(new HitData{damage=Mathf.Clamp(volume.damage,0,1000000),weakSpotMultiplier=1,position=_remote.CenterOfMass,direction=Vector3.up});
        }
    }
    private void SetMissionWait(string key,string message,bool complete=false)
    {
        MissionStatus=message;var state=key+"|"+message;
        if(!complete && _lastMissionWait==state) return;_lastMissionWait=state;
        Send(new(WorldKind.MissionState,_epoch,1,Key:key,Flags:256,Value:message.Length==0?0:key.Length==0?2:1));
    }
    private void ApplyMissionWait(WorldFrame frame)
    {
        if(frame.Value is not (0 or 1 or 2) || frame.Key.Length!=0 && !_catalogue!.MissionExitKeys.Contains(frame.Key)) throw new InvalidDataException("Invalid campaign exit wait state.");
        MissionStatus=frame.Value==0?"":frame.Value==1?"Waiting for partner at the exit.":"Waiting for both players to be alive and loaded.";
    }
    private void ClearMissions()
    {_missionLightState.Clear();_missionExitInteraction=null;_clientExitInteraction=null;_missionBreakState.Clear();_missionArrayFields.Clear();_missionArrays.Clear();_pendingMissionEnd=null;_finishingMission=false;_lastMissionWait=null;MissionStatus="";_missionPresence.Clear();_finishedMissionExits.Clear();_missionVolumeHitAt.Clear();_missionPollAt=0;}
}
