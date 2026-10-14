using PBDMP.Network;
using UnityEngine;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    private readonly Dictionary<string,float[]> _missionLightState=new();
    private void SendMissionLights(string key,Component[] components,bool complete)
    {
        for(var i=0;i<components.Length;i++)
        {
            if(components[i] is not Light light || light==null) continue;
            var color=light.color;var values=new[]{color.r,color.g,color.b,color.a,light.intensity};var identity=key+"|light|"+i;
            if(!complete && _missionLightState.TryGetValue(identity,out var prior) && values.SequenceEqual(prior)) continue;
            _missionLightState[identity]=values;Send(new(WorldKind.MissionState,_epoch,1,Id(identity),Key:key,Flags:64,Value:i,Values:values));
        }
    }
    private void ApplyMissionLight(WorldFrame frame)
    {
        if(!_catalogue!.UrineSurfaces.TryGetValue(frame.Key,out var components) || frame.Value<0 || frame.Value>=components.Length || frame.Value!=Mathf.Floor(frame.Value) ||
            components[(int)frame.Value] is not Light light || frame.Values is not {Length:5} values || values[4]<0) throw new InvalidDataException("Invalid mission light state.");
        if(light==null) return;light.color=new(values[0],values[1],values[2],values[3]);light.intensity=values[4];
    }
}
