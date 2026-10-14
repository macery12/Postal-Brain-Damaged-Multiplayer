using System.Reflection;
using HarmonyLib;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using UnityEngine;

namespace PBDMP.Plugin;

internal static class SharedDoorPatches
{
    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(Interactable),"HandleInteract",new[]{typeof(Player)}),prefix:new(typeof(SharedDoorPatches),nameof(Interact)));
        harmony.Patch(AccessTools.Method(typeof(InteractableRequiredItemsComponent),"TryInteract",new[]{typeof(Player)}),prefix:new(typeof(SharedDoorPatches),nameof(Requirements)));
        var families=new[]{typeof(Door),typeof(DoubleDoor),typeof(SlidingDoors),typeof(ElevatorDoors),typeof(ToiletDoor),typeof(Vault)};
        foreach(var type in families)
            foreach(var method in type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly))
            {
                if(method.Name=="Hit" && (type==typeof(Door) || type==typeof(DoubleDoor) || type==typeof(ToiletDoor))) continue;
                if(type==typeof(ToiletDoor) && method.Name=="Push") continue;
                if(method.Name is "UpdateDesiredRotationAndMove" or "Move" or "MoveDoors" or "UpdateDoorPosition" or "Open" or "Close" or "CloseAndLock" or "UnlockAndOpen" or "CloseAndLockDoor" or "SetLockState" or "Hit" or "Push" or "PlayerInteractSuccess")
                    harmony.Patch(method,prefix:new(typeof(SharedDoorPatches),nameof(HostOnly)));
            }
        harmony.Patch(AccessTools.Method(typeof(Door),"UpdateDesiredRotationAndMove"),prefix:new(typeof(SharedDoorPatches),nameof(SwingPosition)));
        harmony.Patch(AccessTools.Method(typeof(DoubleDoor),"UpdateDesiredRotationAndMove"),prefix:new(typeof(SharedDoorPatches),nameof(SwingPosition)));
        foreach(var type in new[]{typeof(Door),typeof(DoubleDoor)})
            harmony.Patch(AccessTools.Method(type,"Hit"),prefix:new(typeof(SharedDoorPatches),nameof(DoorHit)));
        harmony.Patch(AccessTools.Method(typeof(SlidingDoors),"UpdateDoorPosition"),prefix:new(typeof(SharedDoorPatches),nameof(SafeClose)));
        foreach(var type in new[]{typeof(Mover),typeof(MoverController),typeof(Rotater),typeof(Toggler)})
            foreach(var method in type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly))
                if(method.Name is "Move" or "MoveBack" or "Rotate" or "RotateBack" or "Toggle" or "ChangeState" or "NotifyChange")
                    harmony.Patch(method,prefix:new(typeof(SharedDoorPatches),nameof(SharedMover)));
    }
    private static bool Interact(Interactable __instance,Player player)
    {
        var world=SharedWorld.Current;
        if(world?.HoldMissionExitInteraction(__instance,player)==true) return false;
        world?.LogDoorInteraction(__instance,"interaction");
        if(world is not {Client:true} || !world.IsDoor(__instance)) return true;
        world.RequestDoor(__instance,player); return false;
    }
    private static bool Requirements(ref bool __result)
    {if(SharedWorld.Current is {RemoteInteraction:true}) {__result=true;return false;} return true;}
    private static bool HostOnly()=>SharedWorld.Current is not {Client:true,Applying:false};
    private static bool DoorHit(Component __instance,HitData __0)
    {if(SharedWorld.Current is not {Client:true,Applying:false} world || !world.IsDoor(__instance)) return true;world.ClientDoorHit(__instance,__0);return false;}
    private static bool SharedMover(Component __instance)=>SharedWorld.Current is not {Client:true,Applying:false} world || !world.SharedComponent(__instance);
    private static void SwingPosition(ref Vector3 __0)
    {if(SharedWorld.Current is {RemoteInteraction:true} world) __0=world.InteractionPosition;}
    private static bool SafeClose(SlidingDoors __instance,bool __0)
    {return __0 || SharedWorld.Current is not {Client:false} world || __instance._isLocked || !world.RemoteInside(__instance);}
}
