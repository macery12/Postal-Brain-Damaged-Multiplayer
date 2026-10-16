using HarmonyLib;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using PBDMP.Network;
using UnityEngine;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    private readonly Dictionary<string,(WireWallGate Gate,float Left)> _gateFuses=new();
    internal void GateInteracted(WireWallGate gate,bool wasInteracted)
    {
        if(!Active || Client || RestoringNative || wasInteracted || !gate._interacted || !OwnsMission(gate)) return;
        // Native Interact has committed items, callbacks and windup presentation.
        // Replace its unrecorded delayed closure with an explicit durable clock.
        gate.StopAllCoroutines();
        _gateFuses[_catalogue!.Key(gate)!]=(gate,Mathf.Clamp(gate._explosionDelay,0,60));
    }
    private void TickGateFuses()
    {
        foreach(var pair in _gateFuses.ToArray())
        {
            var gate=pair.Value.Gate;var left=Mathf.Max(0,pair.Value.Left-Time.deltaTime);
            if(gate==null) {_gateFuses.Remove(pair.Key);continue;}
            if(left>0) {_gateFuses[pair.Key]=(gate,left);continue;}
            _gateFuses.Remove(pair.Key);
            var position=gate._explosionTransform.position;
            gate._exploder.Explode(position,gate._explosionSettings,particleRotation:new Il2CppSystem.Nullable<Quaternion>());
            gate._breaker.Break(position,Vector3.up*gate._explosionSettings.ragdollForce);
        }
    }
    private IEnumerable<int> CaptureGateFuses(bool complete)
    {
        foreach(var key in _catalogue!.MechanismKeys)
        {
            if(_catalogue.Components[key].TryCast<WireWallGate>() is not {} gate) continue;
            var pending=_gateFuses.TryGetValue(key,out var clock);
            var frame=new WorldFrame(WorldKind.Mechanism,_epoch,1,Id(key),Key:key,Flags:4,Value:pending?clock.Left:0,Values:new[]{pending?1f:0});
            if(!_mechanismState.TryGetValue(key,out var prior) || prior.Value!=frame.Value || prior.Values![0]!=frame.Values![0] || _persistentFrames!=null || complete)
            {_mechanismState[key]=frame;Send(frame);}
            yield return 0;
        }
    }
    internal float GateFuseForTest(string key)=>Client?_mechanismState.GetValueOrDefault(key)?.Value??0:_gateFuses.GetValueOrDefault(key).Left;
    private void ApplyGateFuse(WorldFrame f,bool hostRestore)
    {
        if(!_catalogue!.MechanismKeys.Contains(f.Key) || _catalogue.Components[f.Key].TryCast<WireWallGate>() is not {} gate ||
            f.Value<0 || f.Value>60 || f.Values is not {Length:1} v || v[0] is not (0 or 1)) throw new InvalidDataException("Invalid gate fuse.");
        gate.StopAllCoroutines();_mechanismState[f.Key]=f;
        if(hostRestore && v[0]!=0) _gateFuses[f.Key]=(gate,f.Value);else _gateFuses.Remove(f.Key);
    }
}

internal static class WorldGateFusePatches
{
    internal static void Install(Harmony h)=>h.Patch(AccessTools.Method(typeof(WireWallGate),"Interact"),
        prefix:new(typeof(WorldGateFusePatches),nameof(Before)),postfix:new(typeof(WorldGateFusePatches),nameof(After)));
    private static void Before(WireWallGate __instance,out bool __state)=>__state=__instance._interacted;
    private static void After(WireWallGate __instance,bool __state)=>SharedWorld.Current?.GateInteracted(__instance,__state);
}
