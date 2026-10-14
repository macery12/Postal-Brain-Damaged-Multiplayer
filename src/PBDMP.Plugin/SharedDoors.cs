using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using Hyperstrange.PBD.PlayerComponents;
using UnityEngine;
using Il2CppInterop.Runtime;
using PBDMP.Network;
using System.Globalization;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    private bool _doorPortalsDirty;
    private readonly HashSet<string> _reportedDoorReferences=new();
    private static bool ApplySwingDoor(Component component,string[] values)
    {
        var door=component.TryCast<Door>();var doubleDoor=component.TryCast<DoubleDoor>();
        if(door==null && doubleDoor==null) return false;
        // Native solo-save Load reads the handle even when it is missing/destroyed.
        // Replicas need logical state only; authored transforms arrive separately.
        if(values.Length!=8) throw new InvalidDataException("Invalid hinged-door state size.");
        var data=new Dictionary<string,string>(StringComparer.Ordinal);
        for(var i=0;i<values.Length;i+=2) if(!data.TryAdd(values[i],values[i+1])) throw new InvalidDataException("Duplicate hinged-door state key.");
        if(!data.TryGetValue("FirstInteraction",out var firstText) || !bool.TryParse(firstText,out var first) ||
            !data.TryGetValue("_isLocked",out var lockedText) || !bool.TryParse(lockedText,out var locked) ||
            !data.TryGetValue("_desiredRotation",out var desiredText) || !float.TryParse(desiredText,NumberStyles.Float,CultureInfo.InvariantCulture,out var desired) || !float.IsFinite(desired) ||
            !data.TryGetValue("_previousOpenRotation",out var previousText) || !float.TryParse(previousText,NumberStyles.Float,CultureInfo.InvariantCulture,out var previous) || !float.IsFinite(previous))
            throw new InvalidDataException("Invalid hinged-door logical state.");
        if(door!=null) {door.FirstInteraction=first;door._isLocked=locked;door._desiredRotation=desired;door._previousOpenRotation=previous;door._isChangingState=false;}
        else {doubleDoor!.FirstInteraction=first;doubleDoor._isLocked=locked;doubleDoor._desiredRotation=desired;doubleDoor._previousOpenRotation=previous;doubleDoor._isChangingState=false;}
        return true;
    }
    private void RefreshDoorPortals()
    {
        if(!_doorPortalsDirty || _catalogue==null) return;
        _doorPortalsDirty=false;
        foreach(var key in _catalogue.DoorKeys)
        {
            var component=_catalogue.Components[key];if(component==null) continue;
            var door=component.TryCast<Door>();var doubleDoor=component.TryCast<DoubleDoor>();
            if(door!=null && !door._disableOcclusionUpdate && door._occlusionPortal!=null && door._transformToAnimate!=null) door.UpdateOcclusionPortal();
            if(doubleDoor!=null && doubleDoor._occlusionPortal!=null && doubleDoor._primaryDoor.AnimatedTransform!=null) doubleDoor.UpdateOcclusionPortal();
        }
    }
    private string DoorDiagnostic(WorldFrame frame)
    {
        var component=_catalogue?.Components.GetValueOrDefault(frame.Key);
        var transform=component!=null?component.transform:_catalogue?.DoorMotion.GetValueOrDefault(frame.Key)??_catalogue?.EncounterMotion.GetValueOrDefault(frame.Key);
        var path="<missing>";var scene="<missing>";
        if(transform!=null)
        {
            var names=new List<string>();for(var current=transform;current!=null;current=current.parent) names.Add(current.name);
            names.Reverse();path=string.Join("/",names);scene=transform.gameObject.scene.name;
        }
        var door=component?.TryCast<Door>();var doubleDoor=component?.TryCast<DoubleDoor>();
        var refs=door!=null?$"handle={door._handleTransform!=null}, panel={door._transformToAnimate!=null}":
            doubleDoor!=null?$"primaryHandle={doubleDoor._primaryDoor.HandleTransform!=null}, secondaryHandle={doubleDoor._secondaryDoor.HandleTransform!=null}, primaryPanel={doubleDoor._primaryDoor.AnimatedTransform!=null}, secondaryPanel={doubleDoor._secondaryDoor.AnimatedTransform!=null}":"";
        return $"key={frame.Key}, type={component?.GetIl2CppType().FullName}, scene={scene}, path={path}, epoch={frame.Epoch}/{_epoch}, generation={_mapGeneration}, sequence={frame.Sequence}, flags={frame.Flags}, {refs}, state=[{string.Join(";",frame.Data??Array.Empty<string>())}]";
    }
    internal void LogDoorInteraction(Component component,string action)
    {
        if(_catalogue?.Key(component) is {} key && IsDoor(component))
            Plugin.Logger.LogInfo($"Door {action} ({(Client?"P2":"P1")}): {DoorDiagnostic(new(WorldKind.Door,_epoch,1,Key:key))}");
    }
    private void ReportMissingDoorReferences(Component component,WorldFrame frame)
    {
        var door=component.TryCast<Door>();var doubleDoor=component.TryCast<DoubleDoor>();
        var missing=door!=null && (door._handleTransform==null || door._transformToAnimate==null) ||
            doubleDoor!=null && (doubleDoor._primaryDoor.HandleTransform==null || doubleDoor._secondaryDoor.HandleTransform==null || doubleDoor._primaryDoor.AnimatedTransform==null || doubleDoor._secondaryDoor.AnimatedTransform==null);
        if(missing && _reportedDoorReferences.Add(_mapGeneration+":"+frame.Key))
            Plugin.Logger.LogWarning($"Door has missing native references; applying logical state without the solo-save loader: {DoorDiagnostic(frame)}");
    }
    private uint _requestId;
    private bool _remoteInteraction;
    private Vector3 _interactionPosition;
    private readonly Dictionary<uint,(string Key,Dictionary<string,int> Items,float Deadline)> _doorRequests=new();
    private readonly Dictionary<string,bool> _remoteInside=new();
    internal bool RemoteInteraction=>_remoteInteraction;
    internal Vector3 InteractionPosition=>_interactionPosition;
    internal bool IsDoor(Component component)=>_catalogue?.Key(component) is {} key && _catalogue.DoorKeys.Contains(key);
    internal bool RequestDoor(Interactable door,Player player)
    {
        if(Environment.GetEnvironmentVariable("PBDMP_DOOR_TEST")=="1") Plugin.Logger.LogInfo($"DOOR REQUEST: active={Active}, frozen={_life.Frozen}, alive={player.IsAlive}, known={IsDoor(door)}, pending={_doorRequests.Count}, can={door.CanBeInteractedWith(player)}");
        if (!Active || _life.Frozen || !player.IsAlive) return false;
        var key=_catalogue!.Key(door);
        if (key==null || !IsDoor(door) || _doorRequests.Values.Any(r=>r.Key==key)) return false;
        if (_doorRequests.Count>=32) return false;
        if (!door.CanBeInteractedWith(player)) return false;
        if (door.FirstInteraction && door._requiredStatesComponent!=null && !door._requiredStatesComponent.TryInteract(player)) return false;
        var inventory=player.GetComponent<PlayerInventoryComponent>();
        var before=new PlayerInventoryComponent.SaveData(inventory);
        if (door.FirstInteraction && door._requiredItemsComponent!=null && !door._requiredItemsComponent.TryInteract(player)) return false;
        var after=new PlayerInventoryComponent.SaveData(inventory);
        var consumed=new Dictionary<string,int>();
        foreach(var item in before.HeldItems)
        {
            var remaining=after.HeldItems.ContainsKey(item.Key)?after.HeldItems[item.Key]:0;
            if (item.Value>remaining) consumed[item.Key]=item.Value-remaining;
        }
        var id=++_requestId;
        _doorRequests[id]=(key,consumed,Time.unscaledTime+5f);
        Send(new(WorldKind.Interact,_epoch,1,id,Key:key,Actor:2,Life:_life.Life,Position:Net(player.FeetPosition)));
        return true;
    }
    private void ApplyDoorRequest(WorldFrame frame)
    {
        if(frame.Flags==1) {ApplyMissionGate(frame);return;}
        if(_catalogue!.MissionInteractionExits.Contains(frame.Key) && !BothNearInteractionExit(frame.Key))
        {Send(new(WorldKind.InteractionResult,_epoch,1,frame.Entity,Actor:2,Life:frame.Life,Flags:0));return;}
        var exit=_catalogue.MissionInteractionExits.Contains(frame.Key);var before=MissionFinishesForTest;
        if(exit) _finishingMission=true;
        try { ApplyDoorRequestState(frame); }
        catch(Exception error) {Plugin.Logger.LogError($"Door interaction failed: {DoorDiagnostic(frame)}\n{error}");throw;}
        finally {if(exit) {_finishingMission=false;_missionExitInteraction=null;SetMissionWait("","");if(MissionFinishesForTest>before) {MissionExitsForTest++;_finishedMissionExits.Add(frame.Key);}}}
    }
    private void ApplyDoorRequestState(WorldFrame frame)
    {
        var accepted=false;var alreadyInteracted=false; var pose=_session.Snapshot.RemotePose;
        if(Environment.GetEnvironmentVariable("PBDMP_DOOR_TEST")=="1") Plugin.Logger.LogInfo($"DOOR HOST: frozen={_life.Frozen}, alive={pose?.Alive}, life={frame.Life}/{pose?.Life}, known={_catalogue!.DoorKeys.Contains(frame.Key)}, position={frame.Position}");
        if (!_life.Frozen && pose?.Alive==true && frame.Actor==2 && frame.Life==pose.Life &&
            _catalogue!.DoorKeys.Contains(frame.Key) && _catalogue.Components.TryGetValue(frame.Key,out var component))
        {
            var door=component.TryCast<Interactable>();
            var position=Unity(frame.Position);
            if (door!=null && Vector3.Distance(position,new(pose.X,pose.Y,pose.Z))<3f && Vector3.Distance(position,door.transform.position)<12f)
            {
                alreadyInteracted=!door.FirstInteraction;
                _remoteInteraction=true; _interactionPosition=position;
                Il2CppSystem.Action<Player> callback=(Il2CppSystem.Action<Player>)(System.Action<Player>)(_=>accepted=true);
                door.OnInteractSuccess=Il2CppSystem.Delegate.Combine(door.OnInteractSuccess,callback).Cast<Il2CppSystem.Action<Player>>();
                try
                {
                    if(Environment.GetEnvironmentVariable("PBDMP_DOOR_TEST")=="1") Plugin.Logger.LogInfo($"DOOR HOST preconditions: can={door.CanBeInteractedWith(Player.Instance)}, first={door.FirstInteraction}, p1alive={Player.Instance.IsAlive}");
                    if (Player.Instance.IsAlive) door.HandleInteract(Player.Instance);
                    else if (door._requiredStatesComponent==null || door._requiredStatesComponent.RequirementsMet())
                    {
                        // Requester is alive; a dead P1 must not disable P2's door access.
                        door._insideHandleInteract=true;
                        try { if(door.FirstInteraction) {door.FirstInteraction=false;door.OnFirstInteractSuccess?.Invoke(Player.Instance);} door.OnInteractSuccess?.Invoke(Player.Instance); }
                        finally {door._insideHandleInteract=false;}
                    }
                }
                finally
                {
                    door.OnInteractSuccess=Il2CppSystem.Delegate.Remove(door.OnInteractSuccess,callback)?.Cast<Il2CppSystem.Action<Player>>();
                    _remoteInteraction=false;
                }
                if (accepted) SendState(false);
            }
        }
        Send(new(WorldKind.InteractionResult,_epoch,1,frame.Entity,Actor:2,Life:frame.Life,Flags:(accepted?1u:0)|(alreadyInteracted?2u:0)));
        Plugin.Logger.LogInfo($"P2 door request result: key={frame.Key}, accepted={accepted}, alreadyInteracted={alreadyInteracted}, epoch={_epoch}, life={frame.Life}/{pose?.Life}, frozen={_life.Frozen}");
        if(Environment.GetEnvironmentVariable("PBDMP_DOOR_TEST")=="1") Plugin.Logger.LogInfo($"DOOR HOST accepted={accepted}; first={alreadyInteracted}");
    }
    private void CompleteDoorRequest(WorldFrame frame)
    {
        if (!_doorRequests.Remove(frame.Entity,out var request)) return;
        if ((frame.Flags&1)==0 || (frame.Flags&2)!=0) RefundItems(request.Items);
    }
    private void RefundItems(Dictionary<string,int> items)
    {
        var player=Player.Instance; if (player==null) return;
        var inventory=player.GetComponent<PlayerInventoryComponent>();
        foreach(var item in items) if (_catalogue!.Assets.TryGetValue(item.Key,out var asset)) inventory.AddItem(asset,item.Value);
    }
    private void TickDoorRequests()
    {
        if (!Client) return;
        foreach(var pair in _doorRequests.ToArray())
            if (Time.unscaledTime>=pair.Value.Deadline) {RefundItems(pair.Value.Items);_doorRequests.Remove(pair.Key);}
    }
    private void RefundPendingDoorRequests()
    {if(Client) foreach(var request in _doorRequests.Values) RefundItems(request.Items);_doorRequests.Clear();}
    internal bool RemoteInside(SlidingDoors door)
    {
        if (_remote?.IsAlive!=true || door._playerTrigger==null) return false;
        return door._playerTrigger.GetComponents<Collider>().Any(c=>c.enabled && c.bounds.Contains(_remote.CenterOfMass));
    }
    private void TickDoorProximity()
    {
        if (_life.Frozen) return;
        foreach(var key in _catalogue!.DoorKeys)
        {
            var toilet=_catalogue.Components[key].TryCast<ToiletDoor>();
            if(toilet!=null && _remote?.IsAlive==true && _remote.Velocity.sqrMagnitude>0.01f && toilet.GetComponentsInChildren<Collider>().Any(c=>c.enabled && c.bounds.Intersects(_remote.GetComponent<CapsuleCollider>().bounds)))
                toilet.Push(Vector3.ClampMagnitude(_remote.Velocity,12f),false);
            var door=_catalogue.Components[key].TryCast<SlidingDoors>(); if(door==null) continue;
            var inside=RemoteInside(door);
            if(inside && !door._isLocked && !door._isOpen) door.Open();
            else if(!inside && _remoteInside.GetValueOrDefault(key) && !door._isLocked && door._playerTrigger!=null &&
                !door._playerTrigger.GetComponents<Collider>().Any(c=>c.bounds.Contains(Player.Instance.CenterOfMass))) door.Close();
            _remoteInside[key]=inside;
        }
    }
}
