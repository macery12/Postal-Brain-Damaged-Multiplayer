using HarmonyLib;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using Hyperstrange.PBD.PlayerComponents;
using HordeState = Hyperstrange.PBD.HordeModeController.HordeModeStateEnum;

namespace PBDMP.Plugin;

// Survival (native HORDE) authority guards. The controller is not an NPC and is not covered
// by the generic shared-world NPC guards, so each simulation entry point is guarded here.
internal static class SurvivalPatches
{
    internal static void Install(Harmony harmony)
    {
        var horde = typeof(HordeModeController);
        harmony.Patch(AccessTools.Method(horde, nameof(HordeModeController.Begin)), prefix: new(typeof(SurvivalPatches), nameof(Begin)), postfix: new(typeof(SurvivalPatches), nameof(Began)));
        harmony.Patch(AccessTools.Method(horde, "CombatStart"), postfix: new(typeof(SurvivalPatches), nameof(CombatStarted)));
        harmony.Patch(AccessTools.Method(horde, nameof(HordeModeController.SetHordeModeState)), prefix: new(typeof(SurvivalPatches), nameof(StateChanging)));
        // P2's controller is presentation only: no combat/cleanup ticks, spawning or score subscriptions.
        harmony.Patch(AccessTools.Method(horde, nameof(HordeModeController.OnSlowUpdate)), prefix: new(typeof(SurvivalPatches), nameof(HostOnly)));
        harmony.Patch(AccessTools.Method(horde, nameof(HordeModeController.RegisterNpc)), prefix: new(typeof(SurvivalPatches), nameof(HostOnly)), postfix: new(typeof(SurvivalPatches), nameof(Registered)));
        harmony.Patch(AccessTools.Method(horde, "GetNextNpcSpawnPosition"), postfix: new(typeof(SurvivalPatches), nameof(SpawnSafety)));
        // The shared combo penalty belongs to P1's controller. P2's inert controller is not
        // subscribed to P2's player damage at runtime, so P2 reports each native damage event
        // (health or armor lost in ProcessHitData) to P1, and never penalizes locally.
        harmony.Patch(AccessTools.Method(horde, "OnPlayerReceivedDamage"), prefix: new(typeof(SurvivalPatches), nameof(HostOnly)));
        harmony.Patch(AccessTools.Method(typeof(PlayerHitProcessorComponent), "ProcessHitData"), prefix: new(typeof(SurvivalPatches), nameof(HitStart)), postfix: new(typeof(SurvivalPatches), nameof(HitEnd)));
        var offers = typeof(HordeModeWeaponSpawner);
        harmony.Patch(AccessTools.Method(offers, "SpawnPickup"), postfix: new(typeof(SurvivalPatches), nameof(OfferSpawned)));
        harmony.Patch(AccessTools.Method(offers, "OnPickup"), prefix: new(typeof(SurvivalPatches), nameof(OfferTaking)), postfix: new(typeof(SurvivalPatches), nameof(OfferTaken)));
        // A spectating (dead) Survival player cannot collect or use anything.
        harmony.Patch(AccessTools.Method(typeof(PlayerInteractionComponent), nameof(PlayerInteractionComponent.OnUpdate)), prefix: new(typeof(SurvivalPatches), nameof(Living)));
        harmony.Patch(AccessTools.Method(typeof(PlayerInteractionComponent), "OnUseDown"), prefix: new(typeof(SurvivalPatches), nameof(Living)));
        harmony.Patch(AccessTools.Method(typeof(Pickup), nameof(Pickup.Interact)), prefix: new(typeof(SurvivalPatches), nameof(Living)));
    }

    internal static bool ClientArena => SessionLifecycle.Current is { IsClient: true } life && life.InSurvival;
    internal static bool Spectating => SessionLifecycle.Current is { InSurvival: true } life && life.PlayerState.Pending;
    // Set only while PBDMP runs the native end of a wiped Survival run.
    internal static bool Ending;
    internal static int BeginCount { get; private set; }

    // The authored start pickup stays a personal item on both PCs. Its listener's Begin marks
    // that player ready; P1 begins the native run once every admitted, living player is ready.
    private static bool Begin()
    {
        if (SessionLifecycle.Current is not { InSurvival: true } || SharedWorld.Current is not { } world) return !ClientArena;
        return world.SurvivalBeginRequested();
    }
    private static void Began(bool __runOriginal) { if (__runOriginal && !ClientArena) { BeginCount++; SharedWorld.Current?.SurvivalBegan(); } }
    private static void CombatStarted() { if (!ClientArena) SharedWorld.Current?.SurvivalCombatStarted(); }
    private static bool StateChanging(HordeModeController __instance, HordeState __0) =>
        SessionLifecycle.Current is not { InSurvival: true } || SharedWorld.Current is not { } world || world.SurvivalStateChanging(__instance, __0);
    private static bool HostOnly() => !ClientArena;
    internal static int DamageReports { get; private set; }
    private static void HitStart(PlayerHitProcessorComponent __instance, out (float Health, float Armor) __state) => __state = (__instance.HitPoints.Current, __instance.Armor);
    private static void HitEnd(PlayerHitProcessorComponent __instance, (float Health, float Armor) __state)
    {
        if (!ClientArena || __instance.HitPoints.Current >= __state.Health && __instance.Armor >= __state.Armor) return;
        DamageReports++; SharedWorld.Current?.ReportSurvivalDamage();
    }
    internal static int Registrations { get; private set; }
    private static void Registered(Npc npc) { if (!ClientArena) { Registrations++; SharedWorld.Current?.ChooseSurvivalTarget(npc); } }
    private static void SpawnSafety(ref bool __result, ref UnityEngine.Vector3 spawnPosition)
    { if (__result && SharedWorld.Current is { } world && !world.PartnerSpawnSafe(spawnPosition)) __result = false; }
    private static void OfferSpawned(HordeModeWeaponSpawner __instance, Pickup __0, UnityEngine.Transform __1)
    { if (SessionLifecycle.Current is { InSurvival: true }) SharedWorld.Current?.SurvivalOfferSpawned(__instance, __0, __1); }
    private static void OfferTaking(Pickup __0)
    { if (SessionLifecycle.Current is { InSurvival: true }) SharedWorld.Current?.SurvivalOfferTaking(__0); }
    private static void OfferTaken() => SharedWorld.Current?.SurvivalOfferTaken();
    private static bool Living() => !Spectating;
}
