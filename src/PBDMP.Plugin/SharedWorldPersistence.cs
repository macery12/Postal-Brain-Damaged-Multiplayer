using Hyperstrange.PBD;
using Il2CppInterop.Runtime;
using Hyperstrange.PBD.Contraptions;
using PBDMP.Network;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    private readonly Dictionary<uint,uint> _restoreActorIds=new();
    private List<WorldFrame>? _persistentFrames;
    private readonly Dictionary<IntPtr,uint> _dropKeys=new();
    internal void BeginGuestRestore() {_applying=true;RestoreInProgress=true;}
    internal void EndGuestRestore() {_applying=false;RestoreInProgress=false;}
    internal void ClearTransientForRestore()
    {
        _clearingProjectiles=true;
        try
        {
            foreach(var projectile in Object.FindObjectsOfType<Projectile>())
            {if(_localShots.ContainsKey(projectile.Pointer)) continue;if(projectile.OriginalPoolable!=null) RecycleOriginalProjectile(projectile);else projectile.gameObject.SetActive(false);}
        }
        finally {_clearingProjectiles=false;}
        ClearProjectiles();BuildProjectilePrefabs();
    }
    internal string PersistentManifest=>_catalogue?.Manifest??"";
    internal void OwnDiagnosticDrop(GameObject root)=>_ownedLoot.Add(root);
    internal string? DropKey(Pickup pickup)=>_dropKeys.TryGetValue(pickup.Pointer,out var id)?"d:"+id:null;
    internal bool RestoreInProgress {get;private set;}
    private readonly List<(Rotater Rotater,Rotater.State State,float Left)> _rotaterContinuations=new();
    internal void ResumeNativeMachines(WorldFrame[] frames)
    {
        foreach(var pending in _rotaterContinuations)
        {
            if(pending.Rotater==null) throw new InvalidDataException("Restored rotating machinery disappeared.");
            // Native overload records its argument but builds the tween using
            // _rotateDuration. Use the remaining duration for this continuation.
            var configured=pending.Rotater._rotateDuration;pending.Rotater._rotateDuration=Mathf.Max(.001f,pending.Left);
            try
            {
                if(pending.State==Rotater.State.ROTATING) pending.Rotater.Rotate(pending.Rotater._rotateDuration);
                else if(pending.State==Rotater.State.ROTATING_BACK) pending.Rotater.RotateBack(pending.Rotater._rotateDuration);
            }
            finally {pending.Rotater._rotateDuration=configured;}
        }
        _rotaterContinuations.Clear();
        foreach(var frame in frames.Where(f=>f.Kind==WorldKind.Mechanism && f.Flags==5)) ApplyMachineAudio(frame,true);

    }
    internal bool RestoreNativeMachine(Component c,Dictionary<string,string> state)
    {
        if(c.TryCast<Rotater>() is {} rotater)
        {
            if(!state.TryGetValue("state",out var rotationStateText) || !int.TryParse(rotationStateText,out var rotationState) || rotationState is <0 or >3 ||
                !state.TryGetValue("_rotationDurationLeft",out var leftText) || !float.TryParse(leftText,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var left) || !float.IsFinite(left) || left>3600 ||
                !state.TryGetValue("_currentRotation",out var rotationText)) throw new InvalidDataException("Invalid saved rotater phase.");
            DG.Tweening.ShortcutExtensions.DOKill(rotater.transform,false);rotater._rotateInstance?.Stop(false);
            var rotation=SimpleVector3.Parse(rotationText);rotater._currentRotation=rotation;rotater.transform.localEulerAngles=new(rotation.x,rotation.y,rotation.z);
            rotater._state=(Rotater.State)rotationState;rotater._rotationDurationLeft=Mathf.Max(0,left);
            if(rotationState is 1 or 2) _rotaterContinuations.Add((rotater,(Rotater.State)rotationState,Mathf.Max(0,left)));
            return true;
        }
        if(c.TryCast<Elevator>() is not {} elevator) return false;
        if(elevator._platform==null || !state.TryGetValue("position",out var text) ||
            !state.TryGetValue("CurrentState",out var stateText) || !int.TryParse(stateText,out var value) || value is <0 or >3)
            throw new InvalidDataException("Invalid saved elevator state.");
        // Native Load calls ResetState, which dereferences optional audio/rider
        // helpers even for dormant authored elevators. Restore logical state
        // directly and use its normal continuation only for a moving platform.
        elevator.StopAllCoroutines();DG.Tweening.ShortcutExtensions.DOKill(elevator._platform,false);
        elevator._moveInstance?.Stop(false);elevator._musicInstance?.Stop(false);
        elevator._movingPlatformTrigger?.ClearMovables();
        var position=SimpleVector3.Parse(text);elevator._platform.position=new(position.x,position.y,position.z);
        elevator.CurrentState=(Elevator.State)value;
        if(value is 1 or 3)
        {
            if(elevator._top==null || elevator._bottom==null || elevator._movingPlatformTrigger==null || elevator._moveInstance==null)
                throw new InvalidDataException("Moving elevator lacks its native continuation references.");
            var moved=elevator.OnElevatorStateChanged;var changed=elevator.OnStateChanged;
            elevator.OnElevatorStateChanged=(Action<Elevator>)(_=>{});elevator.OnStateChanged=(Action<int>)(_=>{});
            try {elevator.GoTo(value==1?Elevator.State.TOP:Elevator.State.BOTTOM);}
            finally {elevator.OnElevatorStateChanged=moved;elevator.OnStateChanged=changed;}
        }
        else elevator._desiredTransform=value==2?elevator._top:elevator._bottom;
        return true;
    }
    internal bool RestoreHingedDoor(Component c,Dictionary<string,string> state)=>ApplySwingDoor(c,state.OrderBy(p=>p.Key,StringComparer.Ordinal).SelectMany(p=>new[]{p.Key,p.Value}).ToArray());
    internal IEnumerable<int> CapturePersistent(List<WorldFrame> frames)
    {
        if(Client || !Active) throw new InvalidOperationException("Only a ready host can capture the shared world.");
        CancelStateCapture();TickLoot();_persistentFrames=frames;
        try
        {
            foreach(var step in CaptureState(true))
            {
                // Capture shares durable records with the live queue. Yield
                // under pressure so repeated saves cannot outrun publication;
                // the normal world tick keeps draining while gameplay is held.
                while(_session.Snapshot.Connected && _outbound.Count>=512) yield return 0;
                yield return step;
            }
        }
        finally {_persistentFrames=null;}
    }
    internal void ReservePersistentIds(WorldFrame[] frames)
    {
        _nextId=Math.Max(_nextId,frames.Where(f=>f.Kind is WorldKind.Enemy or WorldKind.Loot or WorldKind.Scenery).Select(f=>f.Entity).DefaultIfEmpty().Max());
        _enemies.Clear();_enemyIds.Clear();_enemyRespawns.Clear();_actorParts.Clear();
    }
    internal IEnumerable<int> RestorePersistent(WorldFrame[] frames)
    {
        if(Client || !Active) throw new InvalidOperationException("Only P1 restores the native simulation.");
        RestoreInProgress=true;_applying=true;
        var actors=_restoreActorIds;actors.Clear();
        try
        {
            ClearTransientForRestore();
            _nextId=Math.Max(_nextId,frames.Where(f=>f.Kind is WorldKind.Enemy or WorldKind.Loot or WorldKind.Scenery).Select(f=>f.Entity).DefaultIfEmpty().Max());
            foreach(var frame in frames.Where(f=>f.Kind==WorldKind.Enemy))
            {
                var npc=Object.FindObjectsOfType<Npc>(true).Where(n=>n.NpcId!=null && WorldCatalogue.NpcAssetKey(n.NpcId)==frame.Asset && _catalogue!.Key(n.SpawnPoint)==frame.Key)
                    .OrderBy(n=>Vector3.Distance(n.Transform.position,Unity(frame.Position))).FirstOrDefault(n=>!actors.Values.Contains(_enemyIds.GetValueOrDefault(n.Pointer)) || !_enemyIds.ContainsKey(n.Pointer));
                if(npc!=null) actors[frame.Entity]=Register(npc);
                else if((frame.Flags&3)==3)
                {
                    throw new InvalidDataException("Native save did not restore a living saved actor: "+frame.Asset+"/"+frame.Key);
                }
                yield return 0;
            }
            foreach(var original in frames.Where(f=>f.Kind!=WorldKind.Enemy))
            {
                var frame=original with {Epoch=_epoch,Sequence=1};
                switch(frame.Kind)
                {
                    case WorldKind.Encounter: ApplyEncounter(frame,true);break;
                    case WorldKind.Scenery:
                        if(frame.Flags is not (4 or 5)) ApplyScenery(frame,true);
                        break;
                    case WorldKind.Sequence: ApplySequence(frame,true);break;
                    case WorldKind.Mechanism: ApplyMechanism(frame,true);break;
                    case WorldKind.Door: ApplyDoor(frame);break;
                    case WorldKind.UrineState: ApplyUrineState(frame);break;
                    case WorldKind.MissionState:
                        if(frame.Flags==1024) ReceiveMissionCompletion(frame);
                        else if(frame.Flags!=256 && frame.Flags!=512) ApplyMissionState(frame);
                        break;
                    case WorldKind.Loot:
                        _nextId=Math.Max(_nextId,frame.Entity);_loot[frame.Entity]=frame;
                        CreatePersonalDrop(frame);break;
                    case WorldKind.ActorState:
                        if((frame.Flags&255)==5)
                        {
                            if(!_catalogue!.Components.TryGetValue(frame.Asset,out var actor)) throw new InvalidDataException("Missing saved scripted actor.");
                            _scriptActors[frame.Asset]=actor;actor.gameObject.SetActive(frame.Value!=0);actor.transform.SetPositionAndRotation(Unity(frame.Position),Quaternion.Euler(Unity(frame.Rotation)));
                        }
                        else if(frame.Flags==6) { }
                        else if(frame.Entity==0) ApplyActorState(frame);
                        else if(actors.TryGetValue(frame.Entity,out var actorId)) ApplyActorState(frame with {Entity=actorId});
                        break;
                }
                yield return 0;
            }
            // Parents, activation and machinery must settle before restoring
            // world-space bodies, including files captured in the older order.
            foreach(var original in frames.Where(f=>f.Kind==WorldKind.Scenery && f.Flags is 4 or 5))
            {
                ApplyScenery(original with {Epoch=_epoch,Sequence=1},true);
                yield return 0;
            }
            RefreshDoorPortals();
            // Republish fresh epoch IDs from the restored simulation, never reuse disk sequence numbers.
            _lastDoor.Clear();_lastMotion.Clear();_lastEnemy.Clear();_encounterState.Clear();
            _peerMatched=false;_manifestAt=0;
        }
        finally {RestoreInProgress=false;_applying=false;_restoreActorIds.Clear();}
    }
    private void CreatePersonalDrop(WorldFrame frame)
    {
        if(!_pickupPrefabs.TryGetValue(frame.Key,out var prefab)) throw new InvalidDataException("Unavailable saved pickup prefab.");
        var root=Object.Instantiate(prefab.gameObject,Unity(frame.Position),Quaternion.Euler(Unity(frame.Rotation)));
        var pickup=root.GetComponent<Pickup>();_dropKeys[pickup.Pointer]=frame.Entity;
        var bag=pickup.TryCast<BagPickup>();if(bag!=null) {bag.SetHealth(frame.Value);bag.SetArmor(frame.Value2);}
        _ownedLoot.Add(root);
        CoopSaveService.Current?.RestoreDropCollection(pickup,"d:"+frame.Entity);
        if(_collectedShared.Contains("d:"+frame.Entity)) pickup.SetEnabled(true);
    }
}
