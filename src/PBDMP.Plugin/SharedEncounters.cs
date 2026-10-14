using System.Globalization;
using System.Reflection;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using PBDMP.Network;
using UnityEngine;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    private readonly Dictionary<string,string[]> _encounterState=new();
    private readonly Dictionary<Type,PropertyInfo[]> _encounterFields=new();
    private readonly Dictionary<string,object> _encounterActuals=new();
    private object EncounterActual(string key,Component component)
    {if(!_encounterActuals.TryGetValue(key,out var actual)) _encounterActuals[key]=actual=NativeAssetGraph.Actual(component);return actual;}
    private PropertyInfo[] EncounterFields(Type type)
    {
        if(_encounterFields.TryGetValue(type,out var fields)) return fields;
        fields=type.GetProperties(BindingFlags.Public|BindingFlags.Instance).Where(p=>p.DeclaringType!=typeof(Hyperstrange.Base.BaseBehaviour) && (p.Name.StartsWith('_') || p.Name is "State" or "CurrentState" && type.Name is "GenericStateable" or "GenericInteractableStateable" or "Elevator") && p.CanWrite && p.GetIndexParameters().Length==0 &&
            (p.PropertyType==typeof(bool) || p.PropertyType==typeof(int) || p.PropertyType==typeof(float) || p.PropertyType.IsEnum)).OrderBy(p=>p.Name,StringComparer.Ordinal).ToArray();
        _encounterFields[type]=fields;return fields;
    }
    private readonly Dictionary<MonoBehaviour,bool> _encounterBehaviours=new();
    private float _encounterPollAt;
    private readonly HashSet<string> _insideEncounters=new();
    private readonly HashSet<string> _remoteTriggerInside=new();
    private void TickEncounterTriggers()
    {
        if(!Client || !Active || _life.Frozen || !Player.Instance.IsAlive || Time.unscaledTime<_encounterPollAt) return;
        _encounterPollAt=Time.unscaledTime+0.25f;
        foreach(var key in _catalogue!.EncounterKeys)
        {
            var trigger=_catalogue.Components[key].TryCast<PlayerTrigger>();
            if(trigger==null || !trigger.gameObject.activeInHierarchy) continue;
            var inside=trigger._enabled && TriggerOverlaps(trigger,Player.Instance.FeetPosition,Player.Instance.Height);
            if(inside && (_catalogue.MissionTriggerKeys.Contains(key) || trigger._multiUse || !trigger._triggeredOnEnter)) TriggerEncounter(trigger);
            else if(!inside) TriggerEncounter(trigger,true);
        }
    }
    internal void TriggerEncounter(Component trigger,bool exiting=false)
    {
        if(!Client || !Active || _life.Frozen || _catalogue!.Key(trigger) is not {} key || !_catalogue.EncounterKeys.Contains(key)) return;
        if(exiting?!_insideEncounters.Remove(key):!_insideEncounters.Add(key)) return;
        Send(new(WorldKind.Trigger,_epoch,1,Key:key,Actor:2,Life:_life.Life,Position:Net(Player.Instance.FeetPosition),Flags:exiting?1u:0u));
        // World listeners are guarded on P2. Sibling personal notifications,
        // music and platform attachment callbacks still run for the local player.
        if(_catalogue.MissionTriggerKeys.Contains(key) && trigger.TryCast<PlayerTrigger>() is {} personal)
        {if(exiting) personal.OnPlayerExit?.Invoke(Player.Instance);else personal.OnPlayerEnter?.Invoke(Player.Instance);}
    }
    private void ApplyEncounterTrigger(WorldFrame frame)
    {
        var pose=_session.Snapshot.RemotePose;
        if(_life.Frozen || pose?.Alive!=true || frame.Life!=pose.Life || frame.Actor!=2 || !_catalogue!.EncounterKeys.Contains(frame.Key)) return;
        var trigger=_catalogue.Components[frame.Key].TryCast<PlayerTrigger>();if(trigger==null || !trigger._enabled) return;
        if(_catalogue.MissionTriggerKeys.Contains(frame.Key)) return; // Host polls validated poses and aggregate occupancy.
        var position=Unity(frame.Position);
        if(Vector3.Distance(position,new(pose.X,pose.Y,pose.Z))>3f) return;
        if(frame.Flags==1) {if(_remoteTriggerInside.Remove(frame.Key) && (trigger._multiUse || !trigger._triggeredOnExit)) {trigger._triggeredOnExit=true;trigger.OnPlayerExit?.Invoke(Player.Instance);}return;}
        if(!TriggerOverlaps(trigger,position,pose.Height)) return;
        if(!trigger._multiUse && trigger._triggeredOnEnter) return;
        if(!_remoteTriggerInside.Add(frame.Key)) return;
        trigger._triggeredOnEnter=true;
        if(trigger.TryCast<global::PlayerTriggerSpawnEnemies>() is {} spawn) spawn.SpawnEnemies();
        else trigger.OnPlayerEnter?.Invoke(Player.Instance);
    }
    private static bool TriggerOverlaps(PlayerTrigger trigger,Vector3 feet,float height)
    {
        if(!trigger.gameObject.activeInHierarchy) return false;
        // Floor strips can touch the player's feet while their center is above
        // the trigger. Match capsule contact rather than center containment.
        height=Mathf.Clamp(height,0.5f,3f);
        var playerBounds=new Bounds(feet+Vector3.up*(height*0.5f),new(0.7f,height,0.7f));
        return trigger.GetComponents<Collider>().Any(c=>c.enabled && c.bounds.Intersects(playerBounds));
    }
    private void SendEncounters(bool complete)
    {
        foreach(var key in _catalogue!.EncounterKeys) SendEncounter(key,complete);
    }
    private void SendEncounter(string key,bool complete)
    {
        var component=_catalogue!.Components[key];if(component==null) return;var actual=EncounterActual(key,component);var data=new List<string>();
        foreach(var property in EncounterFields(actual.GetType()))
        {
            var value=property.GetValue(actual);
            data.Add(property.Name);data.Add(property.PropertyType.IsEnum?Convert.ToInt32(value,CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture):Convert.ToString(value,CultureInfo.InvariantCulture)!);
        }
        var state=data.ToArray();
        if(complete || !_encounterState.TryGetValue(key,out var prior) || !state.SequenceEqual(prior))
        {
            _encounterState[key]=state;
            var flags=component.TryCast<MonoBehaviour>()?.enabled==true?1u:0u;
            if(state.Length==0) Send(new(WorldKind.Encounter,_epoch,1,Id(key),Key:key,Data:state,Flags:flags));
            for(var start=0;start<state.Length;start+=32) Send(new(WorldKind.Encounter,_epoch,1,Id(key),Key:key,Data:state.Skip(start).Take(32).ToArray(),Flags:flags));
        }
    }
    private void ApplyEncounter(WorldFrame frame)
    {
        if(!_catalogue!.EncounterKeys.Contains(frame.Key)) throw new InvalidDataException("Unknown host encounter.");
        var component=_catalogue.Components[frame.Key];var behaviour=component.TryCast<MonoBehaviour>();
        if(behaviour!=null) {if(!_encounterBehaviours.ContainsKey(behaviour)) {_encounterBehaviours[behaviour]=behaviour.enabled;StopMissionController(component);}behaviour.enabled=component.GetIl2CppType().Name=="JumpPad" && (frame.Flags&1)!=0;}
        var actual=EncounterActual(frame.Key,component);var data=frame.Data??Array.Empty<string>();
        if(data.Length%2!=0) throw new InvalidDataException("Invalid encounter state.");
        for(var i=0;i<data.Length;i+=2)
        {
            var property=actual.GetType().GetProperty(data[i]);
            if(property==null || !EncounterFields(actual.GetType()).Contains(property)) throw new InvalidDataException("Unknown encounter field.");
            object value;
            if(property.PropertyType==typeof(bool)) value=bool.Parse(data[i+1]);
            else if(property.PropertyType==typeof(int)) value=int.Parse(data[i+1],CultureInfo.InvariantCulture);
            else if(property.PropertyType==typeof(float)) value=float.Parse(data[i+1],CultureInfo.InvariantCulture);
            else if(property.PropertyType.IsEnum) {value=Enum.ToObject(property.PropertyType,int.Parse(data[i+1],CultureInfo.InvariantCulture));if(!Enum.IsDefined(property.PropertyType,value)) throw new InvalidDataException("Invalid encounter enum.");}
            else throw new InvalidDataException("Unsafe encounter field.");
            if(value is float scalar && !float.IsFinite(scalar)) throw new InvalidDataException("Invalid encounter scalar.");
            property.SetValue(actual,value);
        }
    }
    private void ClearEncounters()
    {foreach(var pair in _encounterBehaviours) if(pair.Key!=null) pair.Key.enabled=pair.Value;_encounterBehaviours.Clear();_encounterState.Clear();_encounterFields.Clear();_encounterActuals.Clear();_insideEncounters.Clear();_remoteTriggerInside.Clear();}
}
