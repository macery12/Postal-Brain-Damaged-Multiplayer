using HarmonyLib;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using UnityEngine;

namespace PBDMP.Plugin;

internal static class SharedSpecialPatches
{
    internal static void Install(Harmony harmony)
    {
        // Reached/Hit share the same native body. Started/Unhooked share a tiny
        // empty stub used across unrelated types; detouring it crashes IL2CPP.
        foreach(var name in new[]{"OnHookReached"})
        {
            Plugin.Logger.LogInfo("Installing native special: "+name);
            harmony.Patch(AccessTools.Method(typeof(Npc),name),prefix:new(typeof(SharedSpecialPatches),nameof(Hook)));
        }
        Plugin.Logger.LogInfo("Installing native status ticks.");
        harmony.Patch(AccessTools.Method(typeof(Npc),"FlameTick"),prefix:new(typeof(SharedSpecialPatches),nameof(Flame)));
        harmony.Patch(AccessTools.Method(typeof(Npc),"FreezeTick"),prefix:new(typeof(SharedSpecialPatches),nameof(Freeze)));
        harmony.Patch(AccessTools.Method(typeof(Npc),"ConfuseTick"),prefix:new(typeof(SharedSpecialPatches),nameof(Confuse)));
        harmony.Patch(AccessTools.Method(typeof(Npc),"Piss"),prefix:new(typeof(SharedSpecialPatches),nameof(Piss)));
        Plugin.Logger.LogInfo("Installing physics door and reflection requests.");
        harmony.Patch(AccessTools.Method(typeof(ToiletDoor),"Hit"),prefix:new(typeof(SharedSpecialPatches),nameof(DoorHit)));
        harmony.Patch(AccessTools.Method(typeof(ToiletDoor),"Push"),prefix:new(typeof(SharedSpecialPatches),nameof(DoorPush)));
        harmony.Patch(AccessTools.Method(typeof(Hyperstrange.PBD.PlayerComponents.HitReceiver.ReflectProcessor),"ReflectHit"),prefix:new(typeof(SharedSpecialPatches),nameof(Reflect)));
        foreach(var name in new[]{"OnHookStarted","OnHookReached","OnUnhooked"})
            harmony.Patch(AccessTools.Method(typeof(Hookable),name),prefix:new(typeof(SharedSpecialPatches),nameof(WorldHook)));
        // Button hook callbacks are empty stubs or a seven-byte tail jump. Hook
        // the real interaction body instead of detouring shared/tiny entrypoints.
        harmony.Patch(AccessTools.Method(typeof(ButtonHookable),"Interact"),prefix:new(typeof(SharedSpecialPatches),nameof(ButtonHook)));
    }
    private static bool Hook(Npc __instance,IAiActor __0,System.Reflection.MethodBase __originalMethod)
    {
        if(SharedWorld.Current is not {Client:true} world) return true;
        if(__0?.Pointer==Player.Instance?.Pointer) world.ClientSpecial(__instance,__originalMethod.Name switch{"OnHookStarted"=>10u,"OnHookReached"=>11u,"OnHookHit"=>12u,_=>13u});
        return false;
    }
    private static bool Flame(Npc __instance,IAiActor __0,float __1,float __2)
    {if(SharedWorld.Current is not {Client:true} world) return true;if(__0?.Pointer==Player.Instance?.Pointer) world.ClientSpecial(__instance,20,__1,__2);return false;}
    private static bool Freeze(Npc __instance,IAiActor __0,float __1,float __2,float __3)
    {if(SharedWorld.Current is not {Client:true} world) return true;if(__0?.Pointer==Player.Instance?.Pointer) world.ClientSpecial(__instance,21,__1,__2,__3);return false;}
    private static bool Confuse(Npc __instance,IAiActor __0,float __1,bool __2)
    {if(SharedWorld.Current is not {Client:true} world) return true;if(__0?.Pointer==Player.Instance?.Pointer) world.ClientSpecial(__instance,22,__1,state:__2);return false;}
    private static bool Piss(Npc __instance,PissData __0)
    {if(SharedWorld.Current is not {Client:true} world) return true;if(__0.owner?.Pointer==Player.Instance?.Pointer) world.ClientSpecial(__instance,23,__0.stagger?1:0);return false;}
    private static bool DoorHit(ToiletDoor __instance,HitData __0)
    {if(SharedWorld.Current is not {Client:true,Applying:false} world) return true;world.ClientDoorHit(__instance,__0);return false;}
    private static bool DoorPush(ToiletDoor __instance,Vector3 __0,bool __1)
    {if(SharedWorld.Current is not {Client:true,Applying:false} world) return true;world.ClientDoorPush(__instance,__0,__1);return false;}
    private static bool Reflect(Hyperstrange.PBD.PlayerComponents.HitReceiver.ReflectProcessor __instance,HitData __0,ref bool __result)
    {if(SharedWorld.Current?.ReflectIncoming(__instance,__0)!=true) return true;__result=true;return false;}
    private static bool WorldHook(Component __instance,IAiActor __0,System.Reflection.MethodBase __originalMethod)
    {if(__0?.Pointer!=Player.Instance?.Pointer) return true;return SharedWorld.Current?.ClientHookable(__instance,__originalMethod.Name switch{"OnHookStarted"=>30u,"OnHookReached"=>31u,_=>32u})!=true;}
    private static bool ButtonHook(ButtonHookable __instance)=>SharedWorld.Current?.ClientHookable(__instance,31)!=true;
}
