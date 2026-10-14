using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using PBDMP.Network;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    private readonly Dictionary<string,Rail> _urineRails=new();
    private readonly Dictionary<string,PathCreation.PathCreator> _urinePaths=new();
    private readonly Dictionary<IntPtr,string> _urinePathKeys=new();
    private readonly HashSet<IntPtr> _urineRailPointers=new();
    private readonly Dictionary<(string Key,uint Index),RailWagon> _urineWagons=new();
    private readonly List<GameObject> _ownedUrineWagons=new();
    private (string Key,uint Index)? _remoteWagon;
    private void BuildUrineRails()
    {
        foreach(var key in _catalogue!.UrineConsequences) if(_catalogue.Components[key].TryCast<Rail>() is {} rail) {_urineRails[key]=rail;_urineRailPointers.Add(rail.Pointer);}
        if(_urineRails.Count==0) return;
        foreach(var path in Object.FindObjectsOfType<PathCreation.PathCreator>(true)) {var key=WorldCatalogue.TransformKey(path.transform);_urinePaths[key]=path;_urinePathKeys[path.Pointer]=key;}
    }
    private IEnumerable<int> SendUrineWagons(bool complete)
    {
        foreach(var entry in _urineRails)
        {
            var rail=entry.Value;if(rail==null || rail._wagonInstances==null) continue;
            if(rail._wagonInstances.Count>512) throw new InvalidOperationException("Urine rail exceeds bounded wagon adapter.");
            for(var i=0;i<rail._wagonInstances.Count;i++)
            {
                var wagon=rail._wagonInstances[i];if(wagon==null) continue;
                Send(new(WorldKind.UrineState,_epoch,1,Id(entry.Key),(uint)i,Key:entry.Key,Flags:32,
                    Asset:wagon._pathCreator!=null?_urinePathKeys.GetValueOrDefault(wagon._pathCreator.Pointer)??"":"",Position:Net(wagon.transform.position),Rotation:Net(wagon.transform.eulerAngles),Velocity:Net(wagon.transform.localScale),
                    Value:rail._wagonInstances.Count,Value2:(int)wagon._currentState,Values:new[]{wagon._distanceTraveled,wagon._currentSpeed,wagon._disableJumpingOff?1f:0f,wagon.gameObject.activeSelf?1f:0f}));
                yield return 0;
            }
        }
    }
    private void ApplyUrineWagon(WorldFrame frame)
    {
        if(!_urineRails.TryGetValue(frame.Key,out var rail) || frame.Value is <1 or >512 || frame.Revision>=frame.Value || frame.Values is not {Length:4} data || frame.Value2 is <0 or >2) throw new InvalidDataException("Invalid urine rail wagon.");
        var key=(frame.Key,frame.Revision);
        if(!_urineWagons.TryGetValue(key,out var wagon) || wagon==null)
        {
            if(frame.Revision<rail._wagonInstances.Count) wagon=rail._wagonInstances[(int)frame.Revision];
            if(wagon==null)
            {
                wagon=Object.Instantiate(rail._railWagonPrefab,Unity(frame.Position),Quaternion.Euler(Unity(frame.Rotation)));
                _ownedUrineWagons.Add(wagon.gameObject);rail._wagonInstances.Add(wagon);
            }
            wagon.SetRail(rail);_urineWagons[key]=wagon;
        }
        if(frame.Asset.Length>0)
        {
            if(!_urinePaths.TryGetValue(frame.Asset,out var path)) throw new InvalidDataException("Unknown urine wagon path.");
            if(wagon._pathCreator?.Pointer!=path.Pointer) wagon.SetPathCreator(path);
        }
        wagon.transform.SetPositionAndRotation(Unity(frame.Position),Quaternion.Euler(Unity(frame.Rotation)));wagon.transform.localScale=Unity(frame.Velocity);
        wagon._distanceTraveled=data[0];wagon._currentSpeed=data[1];wagon._disableJumpingOff=data[2]!=0;
        // A local rider retains their own input/attachment. Restoring P1's state
        // must never attach P2 through the native solo-save loader.
        if(wagon._player?.Pointer!=Player.Instance?.Pointer) wagon._currentState=(RailWagon.StateEnum)(int)frame.Value2;
        wagon.gameObject.SetActive(data[3]!=0);
        for(var i=(int)frame.Value;i<rail._wagonInstances.Count;i++) if(rail._wagonInstances[i]!=null) rail._wagonInstances[i].gameObject.SetActive(false);
    }
    internal bool UpdateReplicaWagon(RailWagon wagon)
    {
        if(!Client || !Active || wagon._rail==null || !_urineRailPointers.Contains(wagon._rail.Pointer)) return false;
        if(!_life.Frozen && wagon._player?.Pointer==Player.Instance?.Pointer && wagon._player!.IsAlive && wagon._currentState==RailWagon.StateEnum.PLAYER_ATTACHED && wagon._playerPosition!=null)
        {wagon.UpdateTeleportRotation(Time.unscaledDeltaTime);wagon._player.Teleport(wagon._playerPosition.position,wagon._playerRotation);}
        return true;
    }
    internal void UrineWagonBoarded(RailWagon wagon,bool boarded)
    {
        if(!Client || !Active || _life.Frozen || Player.Instance?.IsAlive!=true) return;
        foreach(var pair in _urineWagons) if(pair.Value.Pointer==wagon.Pointer)
        {Send(new(WorldKind.Special,_epoch,1,Revision:pair.Key.Index,Key:pair.Key.Key,Actor:2,Life:_life.Life,Flags:74,Value:boarded?1:0,Position:Net(Player.Instance.CenterOfMass)));return;}
    }
    private void ApplyUrineBoarding(WorldFrame frame)
    {
        if(!_urineRails.TryGetValue(frame.Key,out var rail) || frame.Revision>=rail._wagonInstances.Count || frame.Value is not (0 or 1)) return;
        var wagon=rail._wagonInstances[(int)frame.Revision];if(wagon==null || Vector3.Distance(wagon.transform.position,_remote!.CenterOfMass)>8) return;
        if(frame.Value==0) {ReleaseUrineRider();return;}
        ReleaseUrineRider();_remoteWagon=(frame.Key,frame.Revision);rail._syncedWagonState=RailWagon.StateEnum.PLAYER_ATTACHED;
    }
    private void ReleaseUrineRider()
    {
        if(_remoteWagon is {} rider && _urineRails.TryGetValue(rider.Key,out var rail) && rail!=null)
            rail._syncedWagonState=rail._wagonInstances.Count>0?rail._wagonInstances[0]._currentState:RailWagon.StateEnum.IDLE;
        _remoteWagon=null;
    }
    private void ClearUrineWagons()
    {ReleaseUrineRider();foreach(var obj in _ownedUrineWagons) if(obj!=null) Object.Destroy(obj);_ownedUrineWagons.Clear();_urineRails.Clear();_urineRailPointers.Clear();_urinePaths.Clear();_urinePathKeys.Clear();_urineWagons.Clear();}
}
