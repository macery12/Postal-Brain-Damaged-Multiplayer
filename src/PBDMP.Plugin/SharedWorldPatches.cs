using System.Reflection;
using HarmonyLib;
using Hyperstrange.PBD;
using Hyperstrange.PBD.NpcComponents;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace PBDMP.Plugin;

internal static class SharedWorldPatches
{
    internal static bool Replica => SharedWorld.Current is {Client:true};
    internal static bool DiagnosticFreeze;
    internal static void Install(Harmony harmony)
    {
        var blocked=new HashSet<MethodBase>();
        foreach (var type in typeof(Npc).Assembly.GetTypes())
        {
            if (type.ContainsGenericParameters) continue;
            if (typeof(Npc).IsAssignableFrom(type))
            {
                foreach (var name in new[]{"OnSlowUpdate","Hit","Kill"})
                {
                    var parameter=name=="Hit"?typeof(HitData):name=="Kill"?typeof(bool):typeof(float);
                    var method=type.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly,null,new[]{parameter},null);
                    if (method==null || !blocked.Add(method)) continue;
                    harmony.Patch(method,prefix:new HarmonyMethod(typeof(SharedWorldPatches),name=="Hit"?nameof(EnemyHit):name=="Kill"?nameof(BlockClientOnly):nameof(BlockReplica)));
                }
            }
            if (typeof(NpcStateMachineBehaviour).IsAssignableFrom(type))
                foreach (var name in new[]{"OnStateEnter","OnStateUpdate","OnStateExit"})
                {
                    var method=type.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly,null,new[]{typeof(Animator),typeof(AnimatorStateInfo),typeof(int)},null);
                    if (method!=null && blocked.Add(method)) harmony.Patch(method,prefix:new HarmonyMethod(typeof(SharedWorldPatches),nameof(BlockReplica)));
                }
            if (type.Namespace?.Contains("NpcComponents")==true && typeof(MonoBehaviour).IsAssignableFrom(type))
                foreach (var name in new[]{"OnSlowUpdate","OnUpdate","OnLateUpdate","OnFixedUpdate","OnAnimatorMove","Update","FixedUpdate"})
                {
                    var method=type.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly,null,name is "OnSlowUpdate" or "OnUpdate" or "OnLateUpdate" or "OnFixedUpdate"?new[]{typeof(float)}:Type.EmptyTypes,null);
                    if (method!=null && !method.DeclaringType!.IsGenericType && blocked.Add(method))
                        harmony.Patch(method,prefix:new HarmonyMethod(typeof(SharedWorldPatches),nameof(BlockReplica)));
                }
        }
        harmony.Patch(AccessTools.Method(typeof(NpcAwarenessComponent),"UpdatePlayerTarget"),
            prefix:new HarmonyMethod(typeof(SharedWorldPatches),nameof(ChoosePlayer)));
        harmony.Patch(AccessTools.Method(typeof(Player),nameof(Player.Hit),new[]{typeof(HitData)}),
            prefix:new HarmonyMethod(typeof(SharedWorldPatches),nameof(PlayerHit)));
        harmony.Patch(AccessTools.Method(typeof(Npc),"OnEnable"),prefix:new(typeof(SharedWorldPatches),nameof(EnableAudit)),postfix:new(typeof(SharedWorldPatches),nameof(Enabled)));
        Plugin.Logger.LogInfo($"Shared-combat replica authority guards installed on {blocked.Count} native methods.");
    }
    private static bool BlockReplica()=>!Replica && !DiagnosticFreeze;
    private static void Enabled(Npc __instance)=>SharedWorld.Current?.EnemyEnabled(__instance);
    private static void EnableAudit(Npc __instance)
    {
        if(Environment.GetEnvironmentVariable("PBDMP_CAMPAIGN_AUDIT")!="1") return;
        if(__instance._npcId==null || __instance._npcHitProcessorComponent==null || __instance._npcColliderComponent==null || __instance._behaviourUpdaters==null)
            Plugin.Logger.LogInfo($"NPC AUDIT before enable: {__instance.name}; scene={__instance.gameObject.scene.name}; id={__instance._npcId!=null}; hit={__instance._npcHitProcessorComponent!=null}; collider={__instance._npcColliderComponent!=null}; updaters={__instance._behaviourUpdaters!=null}; level={LevelController._instance!=null}");
    }
    private static bool BlockClientOnly()=>!Replica;
    private static bool EnemyHit(Npc __instance,HitData hitData)
    {
        if (!Replica) return true;
        if(hitData.projectile!=null || hitData.isExplosion) return false; // Authoritative native projectiles resolve these on P1.
        if (hitData.dealer?.Pointer==Player.Instance?.Pointer || hitData.projectile?.Owner?.Pointer==Player.Instance?.Pointer)
            SharedWorld.Current!.ClientHit(__instance,hitData);
        return false;
    }
    private static bool PlayerHit(HitData hitData)
    {
        var world=SharedWorld.Current; if (world==null) return true;
        if (hitData.dealer?.IsPlayer==true && hitData.dealer.Pointer!=Player.Instance?.Pointer) return false;
        if (world.Client && !world.Applying && (hitData.dealer?.TryCast<Npc>()!=null || hitData.projectile?.Owner?.TryCast<Npc>()!=null)) return false;
        return true;
    }
    private static void ChoosePlayer(NpcAwarenessComponent __instance)
    {
        var world=SharedWorld.Current; if (world==null || world.Client || !world.Active || Player.Instance==null) return;
        var local=Player.Instance.Cast<IAiActor>(); var remote=world.Remote;
        if (remote?.IsAlive!=true) { __instance._player=local; return; }
        var position=__instance.transform.position;
        __instance._player=!local.IsAlive || Vector3.Distance(position,remote.CenterOfMass)<Vector3.Distance(position,local.CenterOfMass) ? remote.Actor : local;
    }
}
