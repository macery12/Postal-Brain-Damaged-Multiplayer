using System.Globalization;

namespace PBDMP.Network;

/// <summary>What a PBDMP host advertises in its Steam lobby data, as a friend's copy reads it.</summary>
public sealed record LobbyAdvert(string Version, string Protocol, string Build, string Level, string Mode, int Players)
{
    public const int Capacity = 2;
    public const string ModeMenu = "menu", ModeStory = "story", ModeSurvival = "survival";

    public bool Full => Players >= Capacity;

    /// <summary>A short reason this copy cannot join the advertised game, or null when it can.</summary>
    public string? Problem(string version, int protocol, string build) =>
        Version.Length == 0 ? "Not a PBDMP game" :
        Version != version || Protocol != protocol.ToString(CultureInfo.InvariantCulture) ? "Different PBDMP version (" + Version + ")" :
        Build != build ? "Different PBDMP files" :
        Full ? "Full" : null;

    public static int ParsePlayers(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var players) ? Math.Clamp(players, 0, 16) : 1;

    /// <summary>The lobby Steam asked this copy to join at launch (Join Game while the game was closed), or 0.</summary>
    public static ulong LaunchLobby(IReadOnlyList<string> arguments)
    {
        for (var i = 0; i + 1 < arguments.Count; i++)
            if (arguments[i] == "+connect_lobby" && ulong.TryParse(arguments[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var lobby))
                return lobby;
        return 0;
    }
}
