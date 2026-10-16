using HarmonyLib;
using Hyperstrange.PBD;
using UnityEngine;

namespace PBDMP.Plugin;

// Unattended fixtures use native actions, not desktop mouse capture. The
// dedicated real-focus/input fixture deliberately retains the normal behavior.
internal static class DiagnosticCursor
{
    internal static readonly bool KeepFree =
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PBDMP_TRAVEL_TEST")) &&
        Environment.GetEnvironmentVariable("PBDMP_FOCUS_INPUT_TEST")!="1";

    internal static void Release()
    {Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}

    internal static void Verify()
    {
        if(!KeepFree || Cursor.lockState==CursorLockMode.None) return;
        Release();
        throw new InvalidOperationException("Unattended native diagnostic attempted to capture the desktop cursor.");
    }
}
