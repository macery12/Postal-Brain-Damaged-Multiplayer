using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace PBDMP.Network.Steam;

public readonly record struct SteamConnectionInfo(ulong Remote, uint ListenSocket, int State, int EndReason, string EndDebug);
public readonly record struct SteamOption(int Key, int Value);

/// <summary>
/// Flat-API bindings for the game's own steam_api64.dll (Steamworks SDK 1.51). The game owns Steam's
/// initialisation, shutdown and callback pump; this only calls into the already running client.
/// </summary>
public static class SteamNative
{
    private const string Library = "steam_api64";

    public const int LobbyJoinRequestedCallback = 333, LobbyEnterCallback = 504, LobbyDataUpdateCallback = 505, LobbyCreatedCallback = 513,
        ConnectionStatusChangedCallback = 1221;
    public const int FriendFlagImmediate = 4;
    public const int ResultOk = 1, ResultLimitExceeded = 25;
    public const int LobbyPrivate = 0, LobbyFriendsOnly = 1;
    public const int EnterSuccess = 1;
    public const int StateNone = 0, StateConnecting = 1, StateFindingRoute = 2, StateConnected = 3,
        StateClosedByPeer = 4, StateProblemDetectedLocally = 5;
    public const int ConfigSendBufferSize = 9, ConfigSendRateMin = 10, ConfigSendRateMax = 11, ConfigTimeoutInitial = 24;
    public const int SendReliable = 8;

    // Native sizes from the SDK 1.51 headers. Buffers are allocated larger than needed.
    private const int IdentityBytes = 136, ConnectionInfoBytes = 696, OptionBytes = 16;
    // FriendGameInfo_t: game id @0, lobby id @16; 24 bytes.
    private const int FriendGameBytes = 24, FriendGameLobby = 16;
    private const int InfoListenSocket = 144, InfoState = 176, InfoEndReason = 180, InfoEndDebug = 184, InfoEndDebugBytes = 128;

    private static IntPtr _module, _user, _friends, _utils, _matchmaking, _sockets, _networkingUtils;

    /// <summary>Points the imports at the module the game loaded. Returns it for export lookups.</summary>
    public static IntPtr Bind(string path)
    {
        if (_module != IntPtr.Zero) return _module;
        var module = NativeLibrary.Load(path);
        NativeLibrary.SetDllImportResolver(typeof(SteamNative).Assembly, (name, _, _) => name == Library ? module : IntPtr.Zero);
        return _module = module;
    }

    /// <summary>True once the game has initialised Steam in this process.</summary>
    public static bool Running => _module != IntPtr.Zero && SteamAPI_GetHSteamPipe() != 0 && User != IntPtr.Zero;

    private static IntPtr User => _user != IntPtr.Zero ? _user : _user = SteamAPI_SteamUser_v021();
    private static IntPtr Friends => _friends != IntPtr.Zero ? _friends : _friends = SteamAPI_SteamFriends_v017();
    private static IntPtr Utils => _utils != IntPtr.Zero ? _utils : _utils = SteamAPI_SteamUtils_v010();
    private static IntPtr Matchmaking => _matchmaking != IntPtr.Zero ? _matchmaking : _matchmaking = SteamAPI_SteamMatchmaking_v009();
    private static IntPtr Sockets => _sockets != IntPtr.Zero ? _sockets : _sockets = SteamAPI_SteamNetworkingSockets_SteamAPI_v009();
    private static IntPtr NetworkingUtils => _networkingUtils != IntPtr.Zero ? _networkingUtils : _networkingUtils = SteamAPI_SteamNetworkingUtils_SteamAPI_v003();

    public static ulong LocalId => SteamAPI_ISteamUser_GetSteamID(User);
    public static string LocalName => Text(SteamAPI_ISteamFriends_GetPersonaName(Friends));
    public static string NameOf(ulong id) => Text(SteamAPI_ISteamFriends_GetFriendPersonaName(Friends, id));
    public static bool OverlayEnabled => SteamAPI_ISteamUtils_IsOverlayEnabled(Utils);
    public static void OpenInviteDialog(ulong lobby) => SteamAPI_ISteamFriends_ActivateGameOverlayInviteDialog(Friends, lobby);
    public static uint AppId => SteamAPI_ISteamUtils_GetAppID(Utils);
    public static int FriendCount => Math.Max(SteamAPI_ISteamFriends_GetFriendCount(Friends, FriendFlagImmediate), 0);
    public static ulong FriendAt(int index) => SteamAPI_ISteamFriends_GetFriendByIndex(Friends, index, FriendFlagImmediate);

    /// <summary>The game a friend is playing, and the joinable lobby they are in (0 for none, or an invite-only one).</summary>
    public static bool FriendGame(ulong friend, out ulong game, out ulong lobby)
    {
        var buffer = Zeroed(FriendGameBytes);
        try
        {
            game = lobby = 0;
            if (!SteamAPI_ISteamFriends_GetFriendGamePlayed(Friends, friend, buffer)) return false;
            game = (ulong)Marshal.ReadInt64(buffer, 0); lobby = (ulong)Marshal.ReadInt64(buffer, FriendGameLobby);
            return true;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public static void WarmRelay() => SteamAPI_ISteamNetworkingUtils_InitRelayNetworkAccess(NetworkingUtils);

    /// <summary>Medium avatar as top-down RGBA, or null while Steam has not loaded it.</summary>
    public static byte[]? Avatar(ulong id, out int width, out int height)
    {
        width = height = 0;
        var image = SteamAPI_ISteamFriends_GetMediumFriendAvatar(Friends, id);
        if (image <= 0 || !SteamAPI_ISteamUtils_GetImageSize(Utils, image, out var w, out var h) || w is 0 or > 512 || h is 0 or > 512) return null;
        var pixels = new byte[w * h * 4];
        if (!SteamAPI_ISteamUtils_GetImageRGBA(Utils, image, pixels, pixels.Length)) return null;
        width = (int)w; height = (int)h;
        return pixels;
    }

    public static ulong CreateLobby(int type, int members) => SteamAPI_ISteamMatchmaking_CreateLobby(Matchmaking, type, members);
    public static ulong JoinLobby(ulong lobby) => SteamAPI_ISteamMatchmaking_JoinLobby(Matchmaking, lobby);
    public static void LeaveLobby(ulong lobby) => SteamAPI_ISteamMatchmaking_LeaveLobby(Matchmaking, lobby);
    public static bool SetLobbyType(ulong lobby, int type) => SteamAPI_ISteamMatchmaking_SetLobbyType(Matchmaking, lobby, type);
    public static bool SetLobbyData(ulong lobby, string key, string value) => SteamAPI_ISteamMatchmaking_SetLobbyData(Matchmaking, lobby, key, value);
    public static string GetLobbyData(ulong lobby, string key) => Text(SteamAPI_ISteamMatchmaking_GetLobbyData(Matchmaking, lobby, key));
    /// <summary>Asks Steam for a lobby's data; LobbyDataUpdate (505) reports when it has arrived.</summary>
    public static bool RequestLobbyData(ulong lobby) => SteamAPI_ISteamMatchmaking_RequestLobbyData(Matchmaking, lobby);
    public static ulong LobbyOwner(ulong lobby) => SteamAPI_ISteamMatchmaking_GetLobbyOwner(Matchmaking, lobby);

    public static ulong[] LobbyMembers(ulong lobby)
    {
        var members = new ulong[Math.Clamp(SteamAPI_ISteamMatchmaking_GetNumLobbyMembers(Matchmaking, lobby), 0, 16)];
        for (var i = 0; i < members.Length; i++) members[i] = SteamAPI_ISteamMatchmaking_GetLobbyMemberByIndex(Matchmaking, lobby, i);
        return members;
    }

    public static (int Result, ulong Lobby) ReadLobbyCreated(ReadOnlySpan<byte> data) =>
        (BinaryPrimitives.ReadInt32LittleEndian(data), BinaryPrimitives.ReadUInt64LittleEndian(data[8..]));
    public static (ulong Lobby, int Response) ReadLobbyEnter(ReadOnlySpan<byte> data) =>
        (BinaryPrimitives.ReadUInt64LittleEndian(data), BinaryPrimitives.ReadInt32LittleEndian(data[16..]));
    public static (ulong Lobby, ulong Friend) ReadLobbyJoinRequested(ReadOnlySpan<byte> data) =>
        (BinaryPrimitives.ReadUInt64LittleEndian(data), BinaryPrimitives.ReadUInt64LittleEndian(data[8..]));
    public static (ulong Lobby, ulong Member, bool Success) ReadLobbyDataUpdate(ReadOnlySpan<byte> data) =>
        (BinaryPrimitives.ReadUInt64LittleEndian(data), BinaryPrimitives.ReadUInt64LittleEndian(data[8..]), data[16] != 0);
    public static uint ReadStatusChangedConnection(ReadOnlySpan<byte> data) => BinaryPrimitives.ReadUInt32LittleEndian(data);

    public static uint CreateListenSocket(params SteamOption[] options)
    {
        var buffer = Options(options);
        try { return SteamAPI_ISteamNetworkingSockets_CreateListenSocketP2P(Sockets, 0, options.Length, buffer); }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public static uint Connect(ulong remote, params SteamOption[] options)
    {
        var identity = Identity(remote);
        var buffer = Options(options);
        try { return SteamAPI_ISteamNetworkingSockets_ConnectP2P(Sockets, identity, 0, options.Length, buffer); }
        finally { Marshal.FreeHGlobal(identity); Marshal.FreeHGlobal(buffer); }
    }

    /// <summary>Two connected in-process handles, for exercising the carrier without a second account.</summary>
    public static bool CreateSocketPair(out uint first, out uint second)
    {
        var identity = Identity(LocalId);
        try { return SteamAPI_ISteamNetworkingSockets_CreateSocketPair(Sockets, out first, out second, false, identity, identity); }
        finally { Marshal.FreeHGlobal(identity); }
    }

    public static int Accept(uint connection) => SteamAPI_ISteamNetworkingSockets_AcceptConnection(Sockets, connection);
    public static void Close(uint connection, string reason, bool linger) =>
        SteamAPI_ISteamNetworkingSockets_CloseConnection(Sockets, connection, 0, reason, linger);
    public static void CloseListenSocket(uint socket) => SteamAPI_ISteamNetworkingSockets_CloseListenSocket(Sockets, socket);
    public static int Send(uint connection, ReadOnlySpan<byte> message) =>
        SteamAPI_ISteamNetworkingSockets_SendMessageToConnection(Sockets, connection, in MemoryMarshal.GetReference(message),
            (uint)message.Length, SendReliable, IntPtr.Zero);

    /// <summary>Appends queued messages. Returns the count, or a negative value for a dead handle.</summary>
    public static int Receive(uint connection, IntPtr[] scratch, List<byte[]> into)
    {
        var count = SteamAPI_ISteamNetworkingSockets_ReceiveMessagesOnConnection(Sockets, connection, scratch, scratch.Length);
        for (var i = 0; i < count; i++)
        {
            // SteamNetworkingMessage_t: payload pointer @0, payload size @8.
            var size = Marshal.ReadInt32(scratch[i], 8);
            var bytes = new byte[Math.Max(size, 0)];
            if (bytes.Length > 0) Marshal.Copy(Marshal.ReadIntPtr(scratch[i], 0), bytes, 0, bytes.Length);
            SteamAPI_SteamNetworkingMessage_t_Release(scratch[i]);
            into.Add(bytes);
        }
        return count;
    }

    public static bool TryGetConnectionInfo(uint connection, out SteamConnectionInfo info)
    {
        var buffer = Zeroed(ConnectionInfoBytes + 328);
        try
        {
            if (!SteamAPI_ISteamNetworkingSockets_GetConnectionInfo(Sockets, connection, buffer)) { info = default; return false; }
            var debug = new byte[InfoEndDebugBytes];
            Marshal.Copy(buffer + InfoEndDebug, debug, 0, debug.Length);
            var length = Array.IndexOf(debug, (byte)0);
            info = new(SteamAPI_SteamNetworkingIdentity_GetSteamID64(buffer), (uint)Marshal.ReadInt32(buffer, InfoListenSocket),
                Marshal.ReadInt32(buffer, InfoState), Marshal.ReadInt32(buffer, InfoEndReason),
                System.Text.Encoding.UTF8.GetString(debug, 0, length < 0 ? debug.Length : length));
            return true;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    /// <summary>Round-trip time in milliseconds, or -1 when unknown.</summary>
    public static int Ping(uint connection)
    {
        // SteamNetworkingQuickConnectionStatus: state @0, ping @4; 120 bytes in SDK 1.51.
        var buffer = Zeroed(512);
        try { return SteamAPI_ISteamNetworkingSockets_GetQuickConnectionStatus(Sockets, connection, buffer) ? Marshal.ReadInt32(buffer, 4) : -1; }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static IntPtr Identity(ulong id)
    {
        var buffer = Zeroed(IdentityBytes);
        SteamAPI_SteamNetworkingIdentity_Clear(buffer);
        SteamAPI_SteamNetworkingIdentity_SetSteamID64(buffer, id);
        return buffer;
    }

    private static IntPtr Options(SteamOption[] options)
    {
        // SteamNetworkingConfigValue_t: key @0, data type @4 (1 = int32), value union @8.
        var buffer = Zeroed(Math.Max(options.Length, 1) * OptionBytes);
        for (var i = 0; i < options.Length; i++)
        {
            Marshal.WriteInt32(buffer, i * OptionBytes, options[i].Key);
            Marshal.WriteInt32(buffer, i * OptionBytes + 4, 1);
            Marshal.WriteInt32(buffer, i * OptionBytes + 8, options[i].Value);
        }
        return buffer;
    }

    private static IntPtr Zeroed(int bytes)
    {
        var buffer = Marshal.AllocHGlobal(bytes);
        Marshal.Copy(new byte[bytes], 0, buffer, bytes);
        return buffer;
    }

    private static string Text(IntPtr utf8) => utf8 == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(utf8) ?? "";

    [DllImport(Library)] private static extern int SteamAPI_GetHSteamPipe();
    [DllImport(Library)] private static extern IntPtr SteamAPI_SteamUser_v021();
    [DllImport(Library)] private static extern IntPtr SteamAPI_SteamFriends_v017();
    [DllImport(Library)] private static extern IntPtr SteamAPI_SteamUtils_v010();
    [DllImport(Library)] private static extern IntPtr SteamAPI_SteamMatchmaking_v009();
    [DllImport(Library)] private static extern IntPtr SteamAPI_SteamNetworkingSockets_SteamAPI_v009();
    [DllImport(Library)] private static extern IntPtr SteamAPI_SteamNetworkingUtils_SteamAPI_v003();

    [DllImport(Library)] private static extern ulong SteamAPI_ISteamUser_GetSteamID(IntPtr self);
    [DllImport(Library)] private static extern IntPtr SteamAPI_ISteamFriends_GetPersonaName(IntPtr self);
    [DllImport(Library)] private static extern IntPtr SteamAPI_ISteamFriends_GetFriendPersonaName(IntPtr self, ulong friend);
    [DllImport(Library)] private static extern int SteamAPI_ISteamFriends_GetMediumFriendAvatar(IntPtr self, ulong friend);
    [DllImport(Library)] private static extern void SteamAPI_ISteamFriends_ActivateGameOverlayInviteDialog(IntPtr self, ulong lobby);
    [DllImport(Library)] private static extern int SteamAPI_ISteamFriends_GetFriendCount(IntPtr self, int flags);
    [DllImport(Library)] private static extern ulong SteamAPI_ISteamFriends_GetFriendByIndex(IntPtr self, int index, int flags);
    [DllImport(Library)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool SteamAPI_ISteamFriends_GetFriendGamePlayed(IntPtr self, ulong friend, IntPtr info);
    [DllImport(Library)] private static extern uint SteamAPI_ISteamUtils_GetAppID(IntPtr self);
    [DllImport(Library)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool SteamAPI_ISteamUtils_IsOverlayEnabled(IntPtr self);
    [DllImport(Library)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool SteamAPI_ISteamUtils_GetImageSize(IntPtr self, int image, out uint width, out uint height);
    [DllImport(Library)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool SteamAPI_ISteamUtils_GetImageRGBA(IntPtr self, int image, [Out] byte[] destination, int size);

    [DllImport(Library)] private static extern ulong SteamAPI_ISteamMatchmaking_CreateLobby(IntPtr self, int type, int members);
    [DllImport(Library)] private static extern ulong SteamAPI_ISteamMatchmaking_JoinLobby(IntPtr self, ulong lobby);
    [DllImport(Library)] private static extern void SteamAPI_ISteamMatchmaking_LeaveLobby(IntPtr self, ulong lobby);
    [DllImport(Library)] private static extern int SteamAPI_ISteamMatchmaking_GetNumLobbyMembers(IntPtr self, ulong lobby);
    [DllImport(Library)] private static extern ulong SteamAPI_ISteamMatchmaking_GetLobbyMemberByIndex(IntPtr self, ulong lobby, int member);
    [DllImport(Library)] private static extern ulong SteamAPI_ISteamMatchmaking_GetLobbyOwner(IntPtr self, ulong lobby);
    [DllImport(Library)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool SteamAPI_ISteamMatchmaking_RequestLobbyData(IntPtr self, ulong lobby);
    [DllImport(Library)] private static extern IntPtr SteamAPI_ISteamMatchmaking_GetLobbyData(IntPtr self, ulong lobby, [MarshalAs(UnmanagedType.LPUTF8Str)] string key);
    [DllImport(Library)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool SteamAPI_ISteamMatchmaking_SetLobbyData(IntPtr self, ulong lobby,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string key, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(Library)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool SteamAPI_ISteamMatchmaking_SetLobbyType(IntPtr self, ulong lobby, int type);

    [DllImport(Library)] private static extern uint SteamAPI_ISteamNetworkingSockets_CreateListenSocketP2P(IntPtr self, int virtualPort, int optionCount, IntPtr options);
    [DllImport(Library)] private static extern uint SteamAPI_ISteamNetworkingSockets_ConnectP2P(IntPtr self, IntPtr identity, int virtualPort, int optionCount, IntPtr options);
    [DllImport(Library)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool SteamAPI_ISteamNetworkingSockets_CreateSocketPair(IntPtr self,
        out uint first, out uint second, [MarshalAs(UnmanagedType.I1)] bool networkLoopback, IntPtr firstIdentity, IntPtr secondIdentity);
    [DllImport(Library)] private static extern int SteamAPI_ISteamNetworkingSockets_AcceptConnection(IntPtr self, uint connection);
    [DllImport(Library)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool SteamAPI_ISteamNetworkingSockets_CloseConnection(IntPtr self,
        uint connection, int reason, [MarshalAs(UnmanagedType.LPUTF8Str)] string debug, [MarshalAs(UnmanagedType.I1)] bool linger);
    [DllImport(Library)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool SteamAPI_ISteamNetworkingSockets_CloseListenSocket(IntPtr self, uint socket);
    [DllImport(Library)] private static extern int SteamAPI_ISteamNetworkingSockets_SendMessageToConnection(IntPtr self, uint connection,
        in byte data, uint size, int flags, IntPtr messageNumber);
    [DllImport(Library)] private static extern int SteamAPI_ISteamNetworkingSockets_ReceiveMessagesOnConnection(IntPtr self, uint connection,
        [Out] IntPtr[] messages, int maximum);
    [DllImport(Library)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool SteamAPI_ISteamNetworkingSockets_GetConnectionInfo(IntPtr self, uint connection, IntPtr info);
    [DllImport(Library)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool SteamAPI_ISteamNetworkingSockets_GetQuickConnectionStatus(IntPtr self, uint connection, IntPtr status);
    [DllImport(Library)] private static extern void SteamAPI_ISteamNetworkingUtils_InitRelayNetworkAccess(IntPtr self);

    [DllImport(Library)] private static extern void SteamAPI_SteamNetworkingIdentity_Clear(IntPtr identity);
    [DllImport(Library)] private static extern void SteamAPI_SteamNetworkingIdentity_SetSteamID64(IntPtr identity, ulong id);
    [DllImport(Library)] private static extern ulong SteamAPI_SteamNetworkingIdentity_GetSteamID64(IntPtr identity);
    [DllImport(Library)] private static extern void SteamAPI_SteamNetworkingMessage_t_Release(IntPtr message);
}
