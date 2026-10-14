using System.Reflection;
using Hyperstrange.PBD.Ready;
using Hyperstrange.PBD.Contraptions;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using PBDMP.Network;
using UnityEngine;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    private readonly Dictionary<Type,PropertyInfo[]> _missionArrayFields=new();
    private readonly Dictionary<string,bool[]> _missionArrays=new();
    private PropertyInfo[] MissionArrayFields(Type type)
    {
        if(!_missionArrayFields.TryGetValue(type,out var fields)) _missionArrayFields[type]=fields=type.GetProperties(BindingFlags.Public|BindingFlags.Instance)
            .Where(p=>p.Name.StartsWith('_') && p.CanWrite && p.GetIndexParameters().Length==0 && p.PropertyType==typeof(Il2CppStructArray<bool>)).ToArray();
        return fields;
    }
    private void SendMissionArrays(string key,bool complete)
    {
        var c=_catalogue!.Components[key];if(c==null) return;var actual=NativeAssetGraph.Actual(c);
        foreach(var p in MissionArrayFields(actual.GetType()))
        {
            if(p.GetValue(actual) is not Il2CppStructArray<bool> array) continue;
            if(array.Length>256) throw new InvalidOperationException("Mission condition array exceeds bounded adapter: "+p.Name);
            var values=array.ToArray();var identity=key+"|"+p.Name;
            if(!complete && _missionArrays.TryGetValue(identity,out var prior) && values.SequenceEqual(prior)) continue;
            _missionArrays[identity]=values;
            for(var start=0;start<Math.Max(1,values.Length);start+=32)
                Send(new(WorldKind.MissionState,_epoch,1,Id(identity),Key:key,Asset:p.Name,Flags:128,Value:start,Value2:values.Length,Values:values.Skip(start).Take(32).Select(v=>v?1f:0f).ToArray()));
        }
    }
    private void ApplyMissionArray(WorldFrame frame)
    {
        if(!_catalogue!.MissionKeys.Contains(frame.Key) || frame.Value!=Mathf.Floor(frame.Value) || frame.Value2!=Mathf.Floor(frame.Value2) || frame.Value<0 || frame.Value2<0 || frame.Value2>256 || frame.Values is not {} values || values.Any(v=>v is not (0 or 1)) || frame.Value+values.Length>frame.Value2)
            throw new InvalidDataException("Invalid mission condition page.");
        var c=_catalogue.Components[frame.Key];if(c==null) return;var actual=NativeAssetGraph.Actual(c);
        var p=MissionArrayFields(actual.GetType()).SingleOrDefault(p=>p.Name==frame.Asset)??throw new InvalidDataException("Unknown mission condition field.");
        var array=p.GetValue(actual) as Il2CppStructArray<bool>;
        if(array==null || array.Length!=(int)frame.Value2) {array=new((long)frame.Value2);p.SetValue(actual,array);}
        for(var i=0;i<values.Length;i++) array[(int)frame.Value+i]=values[i]!=0;
    }
    private void StopMissionController(Component c)
    {
        if(!OwnsMission(c)) return;
        c.TryCast<MonoBehaviour>()?.StopAllCoroutines();
        var actual=NativeAssetGraph.Actual(c);
        foreach(var p in actual.GetType().GetProperties(BindingFlags.Public|BindingFlags.Instance).Where(p=>p.Name.StartsWith('_') && p.GetIndexParameters().Length==0 && typeof(DG.Tweening.Tween).IsAssignableFrom(p.PropertyType)))
            if(p.GetValue(actual) is DG.Tweening.Tween tween && tween.active) DG.Tweening.TweenExtensions.Kill(tween,false);
        if(c.TryCast<TimelineSequence>()?._playableDirector is {} director) director.Pause();
        if(c.TryCast<GameplayVolume>() is {} volume) {volume.StopAllCoroutines();volume._targetCoroutines.Clear();}
    }
}
