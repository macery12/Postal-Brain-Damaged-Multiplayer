using PBDMP.Network;
using UnityEngine;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    private readonly Dictionary<uint,(string Key,uint Life,uint Result)> _interactionResults=new();
    private readonly Dictionary<uint,WorldFrame> _pendingInteractionFrames=new();
    private readonly Dictionary<uint,int> _interactionRetries=new();
    private uint _lastInteractionId;
    internal void RepeatInteractionForTest()
    {foreach(var frame in _pendingInteractionFrames.Values.ToArray()) {Send(frame);Send(frame);}}
    private void SendDoorRequest(WorldFrame frame)
    {_pendingInteractionFrames.Add(frame.Entity,frame);_interactionRetries.Add(frame.Entity,0);Send(frame);}
    private bool ReplayInteractionResult(WorldFrame frame)
    {
        if(_interactionResults.TryGetValue(frame.Entity,out var prior))
        {
            if(prior.Key!=frame.Key || prior.Life!=frame.Life) throw new InvalidDataException("Interaction ID was reused with different content.");
            Send(new(WorldKind.InteractionResult,_epoch,1,frame.Entity,Actor:2,Life:frame.Life,Flags:prior.Result));return true;
        }
        if(frame.Entity==0 || frame.Entity<=_lastInteractionId)
        {Send(new(WorldKind.InteractionResult,_epoch,1,frame.Entity,Actor:2,Life:frame.Life));return true;}
        _lastInteractionId=frame.Entity;return false;
    }
    private void ReplyInteraction(WorldFrame request,uint result)
    {
        if(_interactionResults.Count>=4096) _interactionResults.Remove(_interactionResults.Keys.Min());
        _interactionResults[request.Entity]=(request.Key,request.Life,result);
        Send(new(WorldKind.InteractionResult,_epoch,1,request.Entity,Actor:2,Life:request.Life,Flags:result));
    }
    private void RetryInteractions()
    {
        foreach(var pair in _doorRequests.ToArray())
        {
            if(Time.unscaledTime<pair.Value.Deadline || !_pendingInteractionFrames.TryGetValue(pair.Key,out var frame)) continue;
            // A timeout is not a rejection: P1 may already have consumed the
            // request. Retry its immutable ID and obtain the cached result.
            var attempts=++_interactionRetries[pair.Key];
            if(attempts>6) {_life.StopClient("A shared interaction could not be confirmed. Rejoin the session.");return;}
            _doorRequests[pair.Key]=(pair.Value.Key,pair.Value.Items,Time.unscaledTime+5);Send(frame);
        }
    }
    private void ClearInteractionLedger()
    {_interactionResults.Clear();_pendingInteractionFrames.Clear();_interactionRetries.Clear();_lastInteractionId=0;}
}
