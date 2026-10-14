using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using System.Security.Cryptography;
using UnityEngine;
using HarmonyLib;
using PBDMP.Network;
using System.Text;

namespace PBDMP.Plugin;

[BepInPlugin("pbdmp.multiplayer", "PBDMP", Version)]
public sealed class Plugin : BasePlugin
{
    internal const string Version = "0.8.0";
    internal static ManualLogSource Logger = null!;
    internal static string Fingerprint = "";
    internal static string BuildId = "";
    /// <summary>Short public form of <see cref="Fingerprint"/>, advertised on Steam lobbies.</summary>
    internal static string LobbyBuild = "";
    internal static string DefaultAddress = "127.0.0.1";
    internal static int DefaultPort = 7777;
    internal static ConfigEntry<bool> InviteOnly = null!;

    public override void Load()
    {
        Logger = Log;
        DefaultAddress = Config.Bind("Connection", "Address", "127.0.0.1", "Numeric LAN IP of the host.").Value;
        DefaultPort = Config.Bind("Connection", "Port", 7777, "TCP port, 1-65535.").Value;
        InviteOnly = Config.Bind("Steam", "InviteOnly", false, "Hosted Steam lobbies accept invited friends only; otherwise any Steam friend can use Join Game.");
        // Both the game and exact mod DLLs must match, including development rebuilds.
        BuildId=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Hash(typeof(Plugin).Assembly.Location)+Hash(typeof(CoopSession).Assembly.Location))));
        Fingerprint = Hash(Path.Combine(Paths.GameRootPath, "GameAssembly.dll")) + ":" +
            Hash(Path.Combine(Paths.GameRootPath, "POSTAL Brain Damaged_Data", "il2cpp_data", "Metadata", "global-metadata.dat"))+":"+BuildId;
        LobbyBuild = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Fingerprint)))[..16];
        SessionPatches.Install(new Harmony("pbdmp.multiplayer.session"));
        SharedWorldPatches.Install(new Harmony("pbdmp.multiplayer.world"));
        SharedActorPatches.Install(new Harmony("pbdmp.multiplayer.actors"));
        Log.LogInfo("Installing shared door hooks.");
        SharedDoorPatches.Install(new Harmony("pbdmp.multiplayer.doors"));
        Log.LogInfo("Installing shared projectile hooks.");
        SharedProjectilePatches.Install(new Harmony("pbdmp.multiplayer.projectiles"));
        Log.LogInfo("Installing shared loot/encounter/effect hooks.");
        SharedLootPatches.Install(new Harmony("pbdmp.multiplayer.loot"));
        Log.LogInfo("Installing shared encounter hooks.");
        SharedEncounterPatches.Install(new Harmony("pbdmp.multiplayer.encounters"));
        Log.LogInfo("Installing native weapon effects.");
        SharedWeaponPatches.Install(new Harmony("pbdmp.multiplayer.weapon-effects"));
        Log.LogInfo("Installing special combat hooks.");
        SharedSpecialPatches.Install(new Harmony("pbdmp.multiplayer.specials"));
        SharedUrinePatches.Install(new Harmony("pbdmp.multiplayer.urine"));
        SharedMissionPatches.Install(new Harmony("pbdmp.multiplayer.missions"));
        RemoteWeaponPatches.Install(new Harmony("pbdmp.multiplayer.remote-weapons"));
        if (Environment.GetEnvironmentVariable("PBDMP_SMOKE_TEST") != "1" ||
            Environment.GetEnvironmentVariable("PBDMP_TEST_DISABLE_MESH_GUARD") != "1")
            new Harmony("pbdmp.multiplayer.solid-materials").PatchAll(typeof(UnreadableMeshGuard));
        AddComponent<MultiplayerBehaviour>();
        Log.LogInfo($"PBDMP {Version} development build {BuildId[..16]} loaded (wire {Protocol.Version}). Host-owned enemies/combat, shared doors, personal resources; multiplayer saves disabled.");
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream));
    }
}
