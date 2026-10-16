using System.Reflection;
using HarmonyLib;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using UnityEngine;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    private readonly Dictionary<IntPtr,(bool Guest,float Until)> _cannonTargets=new();
    internal bool CannonTarget(Cannon cannon,out RemoteActor? target)
    {
        target=null;
        if(!Active || !OwnsMission(cannon)) return true;
        if(Client || _life.Frozen || RestoringNative) return false;
        var local=Player.Instance;
        if(_remote?.IsAlive!=true) {_cannonTargets.Remove(cannon.Pointer);return local?.IsAlive==true;}
        var old=_cannonTargets.GetValueOrDefault(cannon.Pointer);
        if(Time.unscaledTime>=old.Until || local?.IsAlive!=true)
        {
            var position=cannon.transform.position;
            var guestDistance=Vector3.SqrMagnitude(_remote.FeetPosition-position);
            var hostDistance=local?.IsAlive==true?Vector3.SqrMagnitude(local.FeetPosition-position):float.PositiveInfinity;
            // Retain a nearby target briefly to avoid aim flipping between peers.
            old=(guestDistance<hostDistance,Time.unscaledTime+1);
            _cannonTargets[cannon.Pointer]=old;
        }
        if(old.Guest) target=_remote;
        return true;
    }
}

internal static class WorldCannonPatches
{
    private readonly record struct Aim(IntPtr Player,Vector3 Feet,Vector3 Center);
    [ThreadStatic] private static Aim _aim;
    internal static void Install(Harmony harmony)
    {
        foreach(var name in new[]{"OnLateUpdate","OnSlowUpdate"})
            harmony.Patch(AccessTools.Method(typeof(Cannon),name),prefix:new(typeof(WorldCannonPatches),nameof(Begin)),finalizer:new(typeof(WorldCannonPatches),nameof(End)));
        harmony.Patch(AccessTools.PropertyGetter(typeof(Player),nameof(Player.FeetPosition)),prefix:new(typeof(WorldCannonPatches),nameof(Feet)));
        harmony.Patch(AccessTools.PropertyGetter(typeof(Player),nameof(Player.CenterOfMass)),prefix:new(typeof(WorldCannonPatches),nameof(Center)));
    }
    private static bool Begin(Cannon __instance,out Aim __state)
    {
        __state=_aim;
        if(SharedWorldPatches.Replica) return false;
        if(SharedWorld.Current is not {} world) return true;
        if(!world.CannonTarget(__instance,out var remote)) return false;
        _aim=remote!=null && __instance._player!=null?new(__instance._player.Pointer,remote.FeetPosition,remote.CenterOfMass):default;
        return true;
    }
    private static void End(Aim __state)=>_aim=__state;
    // The native cannon hard-codes Player getters. Override only its synchronous
    // aiming scope; never teleport or replace the real host player.
    private static bool Feet(Player __instance,ref Vector3 __result)
    {if(_aim.Player!=__instance.Pointer) return true;__result=_aim.Feet;return false;}
    private static bool Center(Player __instance,ref Vector3 __result)
    {if(_aim.Player!=__instance.Pointer) return true;__result=_aim.Center;return false;}
}
