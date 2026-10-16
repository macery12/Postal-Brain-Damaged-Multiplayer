using HarmonyLib;
using Hyperstrange.PBD;
using Hyperstrange.PBD.NpcComponents;
using System.Reflection;
using UnityEngine;

namespace PBDMP.Plugin;

internal static class SharedEncounterPatches
{
    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(global::PlayerTriggerSpawnEnemies),"SpawnEnemies"),prefix:new(typeof(SharedEncounterPatches),nameof(Trigger)));
        harmony.Patch(AccessTools.Method(typeof(Hyperstrange.PBD.Contraptions.Trigger),"OnTriggerEnter"),prefix:new(typeof(SharedEncounterPatches),nameof(Enter)));
        harmony.Patch(AccessTools.Method(typeof(Hyperstrange.PBD.Contraptions.Trigger),"OnTriggerExit"),prefix:new(typeof(SharedEncounterPatches),nameof(Exit)));
        foreach(var pair in new[]{(typeof(NpcGroup),"SpawnGroup"),(typeof(NpcGroup),"SetTarget"),(typeof(NpcGroup),"AttackPlayer"),(typeof(NpcGroup),"OnNpcDeath"),(typeof(NpcGroup),"OnNpcDespawned"),
            (typeof(NpcGroupChain),"StartNextGroupAttack"),(typeof(NpcGroupChain),"OnNpcWiped"),(typeof(NpcGroupChain),"OnSlowUpdate"),(typeof(SpawnPointListener),"Callback")})
            harmony.Patch(AccessTools.Method(pair.Item1,pair.Item2),prefix:new(typeof(SharedEncounterPatches),nameof(HostOnly)));
        foreach(var pair in new[]{(typeof(SpawnPointDeathListener),"OnDeath"),(typeof(SpawnPointsDeathListener),"OnNpcDeath"),(typeof(SpawnPointsDeathListener),"OnAllNpcDeath"),
            (typeof(NpcGroupWipeListener),"OnNpcWiped"),(typeof(NpcGroupWipeListener),"OnGroupWiped"),(typeof(SpawnPointRespawnOnDeath),"OnDeath")})
            harmony.Patch(AccessTools.Method(pair.Item1,pair.Item2),prefix:new(typeof(SharedEncounterPatches),nameof(HostOnly)));
        foreach(var type in new[]{typeof(LastBossArena),typeof(BigCatBossArena)})
            foreach(var method in type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly))
                if(method.Name is "OnDeath" or "OnDudeDeath" or "OnMechDeath" or "OnHeroLand" or "OnOutLand" or "OnMechInteracted" or "PostPuppetDeathSequence" or "PrepareArena" or "PrepareMinigunArena" or "GenerateButtonSequence" or "OnButtonPressed" or "ActivateCat" or "Update" or "OnUpdate")
                    harmony.Patch(method,prefix:new(typeof(SharedEncounterPatches),nameof(HostOnly)));
        foreach(var method in typeof(SpawnPoint).GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly).Where(m=>m.Name is "Spawn" or "SpawnAt" or "SpawnRuntimeNpc"))
            harmony.Patch(method,prefix:new(typeof(SharedEncounterPatches),nameof(SpawnAuthority)));
    }
    private static bool HostOnly()=>SharedWorld.Current is not {Client:true};
    private static bool SpawnAuthority()=>SharedWorld.Current is not {Client:true,Active:true};
    private static bool Trigger(global::PlayerTriggerSpawnEnemies __instance)
    {if(SharedWorld.Current is {Client:true} world) {world.TriggerEncounter(__instance);return false;}return true;}
    private static bool Enter(Component __instance,Collider __0)=>TriggerContact(__instance,__0,false);
    private static bool Exit(Component __instance,Collider __0)=>TriggerContact(__instance,__0,true);
    private static bool TriggerContact(Component trigger,Collider collider,bool exiting)
    {
        var world=SharedWorld.Current;
        if(world is {Client:false,Active:true} && world.OwnsMission(trigger)) return false;
        if(world is {Client:true,Active:true} && !exiting && Player.Instance is {} localPlayer && collider.GetComponentInParent<Player>()?.Pointer==localPlayer.Pointer && trigger.GetComponent<Hyperstrange.PBD.Contraptions.PlayerTriggerCheckpointComponent>() is {} checkpoint)
        {localPlayer.SaveLastCheckpointPosition();CoopSaveService.Current?.Checkpoint(checkpoint);}
        if(world is not {Client:true,Active:true} || !world.SharedComponent(trigger)) return true;
        if(collider.GetComponentInParent<Player>()?.Pointer==Player.Instance?.Pointer) world.TriggerEncounter(trigger,exiting);
        return false;
    }
}
