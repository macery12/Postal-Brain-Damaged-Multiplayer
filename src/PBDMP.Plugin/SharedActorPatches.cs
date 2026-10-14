using System.Reflection;
using HarmonyLib;
using Hyperstrange.PBD;
using Hyperstrange.PBD.NpcComponents;
using UnityEngine;

namespace PBDMP.Plugin;

internal static class SharedActorPatches
{
    private static readonly Dictionary<IntPtr,int> VfxDepth=new();
    internal static void Install(Harmony harmony)
    {
        foreach(var type in new[]{typeof(Boid),typeof(Covid),typeof(RpgTable),typeof(FakeCitizen)})
            foreach(var method in type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly)
                .Where(m=>m.Name is "OnUpdate" or "OnSlowUpdate"))
                harmony.Patch(method,prefix:new(typeof(SharedActorPatches),nameof(HostUpdate)));
        foreach(var pair in new[]{(typeof(RpgTable),"SpawnRpgPlayers"),
            (typeof(Hyperstrange.PBD.NpcBehaviours.SummonBehaviour),"SummonAt"),
            (typeof(Hyperstrange.PBD.NpcBehaviours.BoidSummonBehaviour),"Summon")})
            harmony.Patch(AccessTools.Method(pair.Item1,pair.Item2),prefix:new(typeof(SharedActorPatches),nameof(HostSpawn)));
        foreach(var type in new[]{typeof(BalloonGroup),typeof(CovidEye),typeof(MiniCovidEye),typeof(ElonMechWeakSpotComponent),typeof(FakeCitizen),typeof(FakePostalDude)})
            harmony.Patch(AccessTools.Method(type,"Hit",new[]{typeof(HitData)}),prefix:new(typeof(SharedActorPatches),nameof(PartHit)));
        foreach(var method in new[]{"SpawnRagdoll","SpawnGib","SpawnFreezeGib","SpawnDeath","SpawnCrit"})
            harmony.Patch(AccessTools.Method(typeof(NpcDeathComponent),method),prefix:new(typeof(SharedActorPatches),nameof(DeathEffect)));
        foreach(var method in typeof(NpcRagdoll).GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly)
            .Where(m=>m.Name=="Init" && m.GetParameters().Any(p=>p.ParameterType==typeof(Npc))))
            harmony.Patch(method,postfix:new(typeof(SharedActorPatches),nameof(CosmeticCorpse)));
        harmony.Patch(AccessTools.Method(typeof(FMODEvent),"PlayOneShot",new[]{typeof(Vector3)}),postfix:new(typeof(SharedActorPatches),nameof(SoundPosition)));
        harmony.Patch(AccessTools.Method(typeof(FMODEvent),"PlayOneShot",new[]{typeof(GameObject)}),postfix:new(typeof(SharedActorPatches),nameof(SoundObject)));
        harmony.Patch(AccessTools.Method(typeof(FMODInstance),"Play"),postfix:new(typeof(SharedActorPatches),nameof(SoundInstance)));
        foreach(var type in typeof(Npc).Assembly.GetTypes().Where(t=>!t.ContainsGenericParameters && typeof(NpcVfxComponent).IsAssignableFrom(t)))
        {
            var method=type.GetMethod("OnHit",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly,null,new[]{typeof(HitData)},null);
            if(method!=null) harmony.Patch(method,prefix:new(typeof(SharedActorPatches),nameof(VfxHit)),finalizer:new(typeof(SharedActorPatches),nameof(VfxEnd)));
        }
        harmony.Patch(AccessTools.Method(typeof(FakeCitizen),"TriggerRun"),prefix:new(typeof(SharedActorPatches),nameof(ScriptRun)));
        harmony.Patch(AccessTools.Method(typeof(FakePostalDude),"TriggeRun"),prefix:new(typeof(SharedActorPatches),nameof(ScriptRun)));
        harmony.Patch(AccessTools.Method(typeof(FakePostalDude),"OnNoticeEnd_AnimationCallback"),prefix:new(typeof(SharedActorPatches),nameof(HostUpdate)));
        Plugin.Logger.LogInfo("Actor authority installed for flying updates, scripted humans, boss weak spots and balloon targets.");
    }
    private static bool HostUpdate()=>!SharedWorldPatches.Replica && !SharedWorldPatches.DiagnosticFreeze;
    private static bool HostSpawn()=>SharedWorld.Current is not {Client:true,Active:true};
    private static bool PartHit(Component __instance,HitData __0)=>SharedWorld.Current?.ClientActorHit(__instance,__0)!=true;
    private static void DeathEffect(NpcDeathComponent __instance,MethodBase __originalMethod)=>SharedWorld.Current?.ActorDeathEffect(__instance,__originalMethod.Name);
    private static void CosmeticCorpse(NpcRagdoll __instance)
    {
        if(SharedWorld.Current is not {Client:true,Applying:true} || Player.Instance?.Collider==null) return;
        foreach(var collider in __instance.GetComponentsInChildren<Collider>(true)) Physics.IgnoreCollision(collider,Player.Instance.Collider,true);
    }
    private static void SoundPosition(FMODEvent __instance,Vector3 __0)=>SharedWorld.Current?.ActorSoundPlayed(__instance,__0);
    private static void SoundObject(FMODEvent __instance,GameObject __0)
    {if(__0!=null) SharedWorld.Current?.ActorSoundPlayed(__instance,__0.transform.position,__0);}
    private static void SoundInstance(FMODInstance __instance)=>SharedWorld.Current?.ActorSoundInstancePlayed(__instance);
    private static void VfxHit(Component __instance,HitData __0)
    {
        var depth=VfxDepth.GetValueOrDefault(__instance.Pointer);VfxDepth[__instance.Pointer]=depth+1;
        if(depth==0) SharedWorld.Current?.ActorHitPresentation(__instance,__0);
    }
    private static void VfxEnd(Component __instance)
    {if(VfxDepth.TryGetValue(__instance.Pointer,out var depth)){if(depth<=1)VfxDepth.Remove(__instance.Pointer);else VfxDepth[__instance.Pointer]=depth-1;}}
    private static bool ScriptRun(Component __instance)
    {
        if(SharedWorld.Current is not {Client:true,Active:true} world) return true;
        world.ClientScriptRun(__instance);return false;
    }
}
