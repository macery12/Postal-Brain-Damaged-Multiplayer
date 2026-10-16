namespace PBDMP.Plugin;

/// <summary>Only reportable, nonpersonal session facts; never persisted to disk.</summary>
internal sealed class ReportSessionContext
{
    private string _role = "Unavailable", _connection = "Unavailable", _level = "Unavailable";

    internal void Begin(bool host, bool steam)
    {
        _role = host ? "Host (P1)" : "Joining player (P2)";
        _connection = steam ? "Steam" : "Direct IP";
        _level = "Unavailable";
    }

    internal void RememberLevel(string level)
    {
        // The caller resolves this from the built-in campaign catalogue, never a mod path.
        if (level.Length is > 6 and <= 100 && level.StartsWith("LEVEL_", StringComparison.Ordinal) &&
            level.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_'))
            _level = level;
    }

    internal string Describe() => $"Last multiplayer session (this game launch only)\nLevel: {_level}\nRole: {_role}\nConnection: {_connection}";
}

internal static class ReportDraft
{
    internal const string RepositoryUrl = "https://github.com/macery12/Postal-Brain-Damaged-Multiplayer";
    internal const string Template = "bug-report.yml";

    internal static string Url(string software, string session) => RepositoryUrl + "/issues/new?template=" + Template +
        "&software=" + Uri.EscapeDataString(software) + "&session=" + Uri.EscapeDataString(session);
}
