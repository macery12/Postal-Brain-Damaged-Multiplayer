using HarmonyLib;
using Hyperstrange.Base;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using PBDMP.Network;
using UnityEngine;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    private byte _triggerRequester;
    private bool _executingTeleport;
    private readonly Dictionary<string,byte> _missionOccupants=new();
    internal bool RemoteWorldRequester=>_remoteInteraction || _triggerRequester==2;
    internal bool PermitPersonalCallback=>!Active || !RestoringNative && (Client || !RemoteWorldRequester);
    private void InvokeWorldTrigger(PlayerTrigger trigger,bool entering,bool remote)
    {
        var prior=_triggerRequester;_triggerRequester=remote?(byte)2:(byte)1;
        try
        {
            if(entering && trigger.TryCast<global::PlayerTriggerSpawnEnemies>() is {} spawn) spawn.SpawnEnemies();
            else if(entering) trigger.OnPlayerEnter?.Invoke(Player.Instance);
            else trigger.OnPlayerExit?.Invoke(Player.Instance);
        }
        finally {_triggerRequester=prior;}
    }
    internal bool PermitTeleport(Teleporter teleporter,Player player)
    {
        if(!Active || !OwnsMission(teleporter) || _executingTeleport) return true;
        if(Client || RestoringNative || _life.Frozen) return false;
        if(!RemoteWorldRequester) return true;
        var pose=_session.Snapshot.RemotePose;
        if(pose is {Alive:true,Ready:true} && pose.Epoch==_epoch)
            Send(new(WorldKind.PlayerEffect,_epoch,1,Key:_catalogue!.Key(teleporter)!,Actor:2,Life:pose.Life,Flags:3));
        return false;
    }
    private void ApplyTeleport(WorldFrame frame)
    {
        if(!_catalogue!.MissionKeys.Contains(frame.Key) || _catalogue.Components[frame.Key].TryCast<Teleporter>() is not {} teleporter)
            throw new InvalidDataException("Unknown scripted teleporter.");
        _executingTeleport=true;
        // Execute on the requester: the native method preserves their own
        // momentum/view and destination effects without ever moving P1 for P2.
        try {teleporter.Teleport(Player.Instance);RebasePlatforms();}
        finally {_executingTeleport=false;}
    }
    private IEnumerable<int> CaptureScriptedRotations(bool complete)
    {
        foreach(var key in _catalogue!.MechanismKeys)
        {
            if(_catalogue.Components[key].TryCast<LocalRotation>() is not {} rotation) continue;
            var frame=new WorldFrame(WorldKind.Mechanism,_epoch,1,Id(key),Key:key,Flags:7,
                Position:Net(rotation.AngularSpeed),Rotation:Net(rotation._currentLateUpdateRotation));
            if(complete || !_mechanismState.TryGetValue(key,out var old) || old.Position!=frame.Position || old.Rotation!=frame.Rotation)
            {_mechanismState[key]=frame;Send(frame);}
            yield return 0;
        }
    }
    private void ApplyScriptedRotation(WorldFrame frame)
    {
        if(!_catalogue!.MechanismKeys.Contains(frame.Key) || _catalogue.Components[frame.Key].TryCast<LocalRotation>() is not {} rotation)
            throw new InvalidDataException("Unknown scripted rotation.");
        rotation._angularSpeed=Unity(frame.Position);rotation._currentLateUpdateRotation=Unity(frame.Rotation);
    }
    internal bool PermitScriptedRotation(LocalRotation rotation)
    {
        if(!Active) return true;
        var replicated=OwnsMission(rotation) || _catalogue!.UrineSurfacePointers.Contains(rotation.transform.Pointer);
        if(!replicated) return true;
        if(SharedUrinePatches.DiagnosticHoldTankMotion && Environment.GetEnvironmentVariable("PBDMP_URINE_TEST")=="1") return false;
        return !Client && !_life.Frozen && !RestoringNative;
    }
    internal bool PermitFillDrivenSurface(Component listener)
    {
        if(!Active || !Client) return true;
        var target=listener.TryCast<Hyperstrange.PBD.Contraptions.PissFillListenerMovingObject>()?._moveTransform??
            listener.TryCast<Hyperstrange.PBD.Contraptions.PissFillListenerScalingObject>()?._scaleTransform;
        // Fill values still update the local tank view; authored moving/scaling
        // machinery receives its exact host pose and must not recalculate it.
        return target==null || !_catalogue!.UrineSurfacePointers.Contains(target.Pointer);
    }
}

internal static class WorldScriptedActionPatches
{
    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(Teleporter),"Teleport"),prefix:new(typeof(WorldScriptedActionPatches),nameof(Teleport)));
        foreach(var name in new[]{"OnUpdate","OnLateUpdate"})
            harmony.Patch(AccessTools.Method(typeof(LocalRotation),name),prefix:new(typeof(WorldScriptedActionPatches),nameof(Rotate)));
        foreach(var name in new[]{"Show","ShowWithDelay","ShowWithBiggerDelay"})
            harmony.Patch(AccessTools.Method(typeof(TutorialId),name),prefix:new(typeof(WorldScriptedActionPatches),nameof(Personal)));
        harmony.Patch(AccessTools.Method(typeof(LocalizedString),"ShowNotification"),prefix:new(typeof(WorldScriptedActionPatches),nameof(Personal)));
        harmony.Patch(AccessTools.Method(typeof(KillVolume),"KillInVolume"),prefix:new(typeof(WorldScriptedActionPatches),nameof(Kill)));
    }
    private static bool Teleport(Teleporter __instance,Player __0)=>SharedWorld.Current?.PermitTeleport(__instance,__0)!=false;
    private static bool Rotate(LocalRotation __instance)=>SharedWorld.Current?.PermitScriptedRotation(__instance)!=false;
    private static bool Personal()=>SharedWorld.Current?.PermitPersonalCallback!=false;
    private static bool Kill()=>!SharedWorldPatches.Replica;
}
