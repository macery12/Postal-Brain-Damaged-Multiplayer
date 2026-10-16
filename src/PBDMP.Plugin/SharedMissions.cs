using Hyperstrange.PBD.Contraptions;
using Hyperstrange.PBD;
using Hyperstrange.PBD.PlayerComponents;
using PBDMP.Network;
using UnityEngine;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    private readonly Dictionary<string,uint> _missionBreakState=new();
    internal bool OwnsMission(Component? c)=>c!=null && _catalogue?.Key(c) is {} key && _catalogue.MissionKeys.Contains(key);
    internal void ClientMissionHit(BreakableObject b,HitData hit)
    {
        if(_life.Frozen || !Player.Instance.IsAlive || _catalogue!.Key(b) is not {} key) return;
        Send(new(WorldKind.Special,_epoch,1,Key:key,Actor:2,Life:_life.Life,Flags:80,Asset:hit.originEntity?.ID??"",Position:Net(hit.position),Rotation:Net(hit.direction),Velocity:Net(hit.pushVelocity),Value:hit.damage,Value2:HitFlags(hit)));
    }
    internal void ClientMissionFall(FallingPlatform c)
    {
        if(_life.Frozen || !Player.Instance.IsAlive || _catalogue!.Key(c) is not {} key) return;
        Send(new(WorldKind.Special,_epoch,1,Key:key,Actor:2,Life:_life.Life,Flags:81,Position:Net(Player.Instance.FeetPosition)));
    }
    private void ApplyMissionFall(WorldFrame frame)
    {
        if(!_catalogue!.MissionKeys.Contains(frame.Key) || _catalogue.Components[frame.Key].TryCast<FallingPlatform>() is not {} c || c._fallen || Vector3.Distance(c.transform.position,_remote!.CenterOfMass)>8) return;
        StartFall(c,2,c._destructionDelay);
    }
    private void ApplyMissionHit(WorldFrame frame)
    {
        if(!_catalogue!.MissionBreakKeys.Contains(frame.Key) || _catalogue.Components[frame.Key].TryCast<BreakableObject>() is not {} b || b._destroyed ||
            Vector3.Distance(MissionBreakCenter(b),_remote!.CenterOfMass)>100 || Vector3.Distance(Unity(frame.Position),MissionBreakCenter(b))>20) return;
        b.Hit(new HitData{dealer=_remote.Actor,originEntity=_catalogue.Assets.GetValueOrDefault(frame.Asset),damage=Mathf.Clamp(frame.Value,0,10000),position=Unity(frame.Position),direction=Unity(frame.Rotation),pushVelocity=Unity(frame.Velocity),isQuickMelee=((uint)frame.Value2&4)!=0,isPowerful=((uint)frame.Value2&8)!=0});
    }
    private static Vector3 MissionBreakCenter(BreakableObject b)
    {
        var whole=b._breaker?._wholeObject;
        return whole?.GetComponentInChildren<Renderer>(true)?.bounds.center??b.transform.position;
    }
    internal void RequestMissionGate(WireWallGate gate,Player player)
    {
        if(!Client || !Active || _life.Frozen || !player.IsAlive || _catalogue!.Key(gate) is not {} key || !gate.CanBeInteractedWith(player) || _doorRequests.Count>=32 || _doorRequests.Values.Any(r=>r.Key==key)) return;
        var inventory=player.GetComponent<PlayerInventoryComponent>();var before=new PlayerInventoryComponent.SaveData(inventory);
        if(gate._requiredItemsComponent!=null && !gate._requiredItemsComponent.TryInteract(player)) return;
        var after=new PlayerInventoryComponent.SaveData(inventory);var consumed=new Dictionary<string,int>();
        foreach(var item in before.HeldItems) {var remaining=after.HeldItems.ContainsKey(item.Key)?after.HeldItems[item.Key]:0;if(item.Value>remaining) consumed[item.Key]=item.Value-remaining;}
        var id=++_requestId;_doorRequests[id]=(key,consumed,Time.unscaledTime+5);
        SendDoorRequest(new(WorldKind.Interact,_epoch,1,id,Key:key,Actor:2,Life:_life.Life,Flags:1,Position:Net(player.FeetPosition)));
    }
    private void ApplyMissionGate(WorldFrame frame)
    {
        var pose=_session.Snapshot.RemotePose;var accepted=false;
        if(!_life.Frozen && pose?.Alive==true && frame.Actor==2 && frame.Life==pose.Life && _catalogue!.MissionKeys.Contains(frame.Key) &&
            _catalogue.Components[frame.Key].TryCast<WireWallGate>() is {} gate && !gate._interacted &&
            Vector3.Distance(Unity(frame.Position),new(pose.X,pose.Y,pose.Z))<3 && Vector3.Distance(Unity(frame.Position),gate.transform.position)<12)
        {
            _remoteInteraction=true;try {gate.Interact(Player.Instance);accepted=true;} finally {_remoteInteraction=false;}
        }
        ReplyInteraction(frame,accepted?1u:0);
    }
    private void SendMissionBreak(string key,bool complete)
    {
        var c=_catalogue!.Components[key];var b=c.TryCast<BreakableObject>();var collision=c.TryCast<CollisionBreakableObject>();
        var gate=c.TryCast<WireWallGate>();var falling=c.TryCast<FallingPlatform>();
        var breaker=b?._breaker??collision?._breaker??gate?._breaker??falling?._breaker;
        if(breaker?._wholeObject==null) return;
        var destroyed=b?._destroyed??collision?._destroyed??falling?._fallen??!breaker._wholeObject.activeSelf;
        var flags=(destroyed?1u:0)|(b?._indestructible==true?2u:0);
        if(complete || !_missionBreakState.TryGetValue(key,out var prior) || prior!=flags)
        {_missionBreakState[key]=flags;Send(new(WorldKind.MissionState,_epoch,1,Id(key),Key:key,Flags:flags));}
    }
    private void ApplyMissionState(WorldFrame frame)
    {
        if(frame.Flags==1024) {ReceiveMissionCompletion(frame);return;}
        if(frame.Flags==256) {ApplyMissionWait(frame);return;}
        if(frame.Flags==128) {ApplyMissionArray(frame);return;}
        if(frame.Flags==64) {ApplyMissionLight(frame);return;}
        if(frame.Flags==512)
        {
            if(!_catalogue!.MissionVolumeKeys.Contains(frame.Key) || frame.Actor!=2 || frame.Life!=_life.Life) return;
            if(Player.Instance.IsAlive) Player.Instance.Kill();return;
        }
        if(!_catalogue!.MissionBreakKeys.Contains(frame.Key) || frame.Flags>3) throw new InvalidDataException("Unknown mission destruction state.");
        var c=_catalogue.Components[frame.Key];var b=c.TryCast<BreakableObject>();var collision=c.TryCast<CollisionBreakableObject>();
        var gate=c.TryCast<WireWallGate>();var falling=c.TryCast<FallingPlatform>();
        var destroyed=(frame.Flags&1)!=0;
        if(b!=null) {b._destroyed=destroyed;b._indestructible=(frame.Flags&2)!=0;}else if(collision!=null) collision._destroyed=destroyed;else if(falling!=null) falling._fallen=destroyed;
        var breaker=b?._breaker??collision?._breaker??gate?._breaker??falling?._breaker;if(breaker==null || breaker._wholeObject==null) throw new InvalidDataException("Missing authored mission breakable geometry.");
        // Restore persistent geometry without executing Break/Load: those native
        // paths replay listeners, damage/progression and cosmetic debris sounds.
        breaker._wholeObject.SetActive(!destroyed);
        if(breaker._parts!=null)
        {
            // The replacement hierarchy includes the permanent wall around the
            // hole as well as loose debris. Preserve its static mesh/collision.
            if(destroyed)
            {
                breaker._parts.transform.localPosition=breaker._wholeObject.transform.localPosition-breaker._localPositionOffset;
                breaker._parts.transform.localRotation=breaker._wholeObject.transform.localRotation;
                if(breaker._rigidbodies!=null) foreach(var body in breaker._rigidbodies)
                    if(body!=null && body.gameObject.Pointer!=breaker._parts.Pointer && !_catalogue.FragmentPointers.Contains(body.Pointer)) body.gameObject.SetActive(false);
            }
            breaker._parts.SetActive(destroyed);
        }
        if(breaker._occlusionPortal!=null) breaker._occlusionPortal.open=destroyed;
    }
}
