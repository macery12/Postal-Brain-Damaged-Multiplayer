using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using BepInEx.Unity.IL2CPP.Hook;
using PBDMP.Network.Steam;

namespace PBDMP.Plugin;

internal readonly record struct SteamEvent(int Id, byte[] Data, bool Failed);

/// <summary>
/// Observes the game's own Steam callback pump. The game dispatches manually, so a second dispatcher would
/// consume callbacks the game needs; its inlined P/Invokes are only reachable at the DLL exports.
/// </summary>
internal static class SteamCallbackTap
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool NextCallback(int pipe, IntPtr message);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool CallResult(int pipe, ulong call, IntPtr result, int size, int expected, IntPtr failed);

    private const int MaxQueued = 512, MaxPayload = 4096;
    private static readonly ConcurrentQueue<SteamEvent> Events = new();
    // Native code holds these after the detours are applied; they must never be collected.
    private static NextCallback? _nextHook, _nextOriginal;
    private static CallResult? _resultHook, _resultOriginal;
    private static INativeDetour? _nextDetour, _resultDetour;
    private static bool _reported;

    internal static bool Installed => _resultDetour != null;

    internal static void Install(IntPtr module)
    {
        if (Installed) return;
        _nextHook = OnNextCallback; _resultHook = OnCallResult;
        _nextDetour = INativeDetour.CreateAndApply(NativeLibrary.GetExport(module, "SteamAPI_ManualDispatch_GetNextCallback"), _nextHook, out _nextOriginal);
        _resultDetour = INativeDetour.CreateAndApply(NativeLibrary.GetExport(module, "SteamAPI_ManualDispatch_GetAPICallResult"), _resultHook, out _resultOriginal);
        Plugin.Logger.LogInfo("Steam callback tap installed on the game's manual dispatch.");
    }

    internal static void Remove()
    {
        _nextDetour?.Dispose(); _resultDetour?.Dispose();
        _nextDetour = null; _resultDetour = null;
    }

    internal static bool TryDequeue(out SteamEvent steamEvent) => Events.TryDequeue(out steamEvent);

    private static bool OnNextCallback(int pipe, IntPtr message)
    {
        var found = _nextOriginal!(pipe, message);
        if (!found) return false;
        try
        {
            // CallbackMsg_t: user @0, callback id @4, payload pointer @8, payload size @16.
            var id = Marshal.ReadInt32(message, 4);
            if (id is SteamNative.LobbyJoinRequestedCallback or SteamNative.LobbyEnterCallback or SteamNative.LobbyDataUpdateCallback or SteamNative.ConnectionStatusChangedCallback)
                Copy(id, Marshal.ReadIntPtr(message, 8), Marshal.ReadInt32(message, 16), false);
        }
        catch (Exception ex) { Report(ex); }
        return true;
    }

    private static bool OnCallResult(int pipe, ulong call, IntPtr result, int size, int expected, IntPtr failed)
    {
        var fetched = _resultOriginal!(pipe, call, result, size, expected, failed);
        if (!fetched) return false;
        try
        {
            if (expected is SteamNative.LobbyCreatedCallback or SteamNative.LobbyEnterCallback)
                Copy(expected, result, size, failed != IntPtr.Zero && Marshal.ReadByte(failed) != 0);
        }
        catch (Exception ex) { Report(ex); }
        return true;
    }

    private static void Copy(int id, IntPtr payload, int size, bool failed)
    {
        if (payload == IntPtr.Zero || size is < 1 or > MaxPayload || Events.Count >= MaxQueued) return;
        var data = new byte[size];
        Marshal.Copy(payload, data, 0, size);
        Events.Enqueue(new(id, data, failed));
    }

    // Runs inside the game's dispatch loop: an escaping exception would unwind through native frames.
    private static void Report(Exception ex)
    {
        if (_reported) return;
        _reported = true;
        Plugin.Logger.LogError("Steam callback tap: " + ex);
    }
}
