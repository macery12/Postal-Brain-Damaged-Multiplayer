using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime;
using PBDMP.Network;
using UnityEngine;

namespace PBDMP.Plugin;

internal static class IssueReport
{
    internal static ReportSessionContext LastSession { get; } = new();
    internal static string Url => ReportDraft.Url(Software(), LastSession.Describe());

    private static string Software() => string.Join("\n", new[]
    {
        "PBDMP version: " + Plugin.Version,
        "PBDMP build: " + Plugin.BuildId[..Math.Min(16, Plugin.BuildId.Length)],
        "Protocol: " + Protocol.Version,
        "Game version (reported by game): " + Read(() => Application.version),
        "Game compatibility ID (binary hash, not release version): " + Read(GameCompatibilityId),
        "Unity: " + Read(() => Application.unityVersion),
        "Windows: " + Read(() => SystemInfo.operatingSystem),
        "BepInEx core: " + VersionOf(typeof(Paths).Assembly),
        "BepInEx IL2CPP loader: " + VersionOf(typeof(BasePlugin).Assembly),
        "Il2CppInterop: " + VersionOf(typeof(Il2CppException).Assembly),
        ".NET runtime: " + RuntimeInformation.FrameworkDescription
    });

    private static string GameCompatibilityId()
    {
        var parts = Plugin.Fingerprint.Split(':');
        if (parts.Length < 2 || parts.Take(2).Any(s => s.Length != 64 || !s.All(Uri.IsHexDigit))) return "Unavailable";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(parts[0] + ":" + parts[1])))[..16];
    }

    private static string VersionOf(Assembly assembly) => Read(() =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "Unavailable");

    private static string Read(Func<string> value)
    {
        try
        {
            var text = value();
            return string.IsNullOrWhiteSpace(text) ? "Unavailable" : new string(text.Where(c => !char.IsControl(c)).Take(160).ToArray());
        }
        // A native getter or missing assembly attribute cannot break the menu or expose an exception path.
        catch { return "Unavailable"; }
    }
}
