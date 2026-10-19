namespace PBDMP.Network;

// Identical players (campaign only): weapons, keys and mission items are one team inventory owned by P1.
// A shared pickup exists once. Pickup is its authored ("a:<save guid>") or dropped ("d:<loot id>") key.
// Picker 1 = P1, 2 = P2. Grant tells the partner to receive the pickup's native contents: always for keys and
// items, and for weapons only when the gun was new to the team. Live plays the native pickup feedback;
// a snapshot sends Live=false, so restored pickups disappear without sound or notices.
public sealed record SharedItemCollect(string Pickup, byte Picker, bool Grant, bool Live);

// P2's request to take one shared pickup, from its current life and position.
public sealed record SharedItemRequest(string Pickup, uint Life, NetVector Feet);

// P1's absolute team holdings of the shared classes: native entity asset ID and quantity (weapons are 1).
// Version increases with every change; a receiver ignores anything older than what it has applied.
public sealed record TeamInventory(uint Version, IReadOnlyList<TeamItem> Items);
public readonly record struct TeamItem(string Id, int Quantity);

public static class SharedItemsProtocol
{
    public const int MaxItems = 128, MaxQuantity = 1000, MaxId = 64, MaxPickupKey = 128;

    public static WorldFrame ToFrame(SharedItemCollect c, uint epoch)
    {
        if (!ValidPickup(c.Pickup) || c.Picker is not (1 or 2)) throw new InvalidDataException("Invalid shared pickup.");
        return new(WorldKind.SharedItem, epoch, 1, Key: c.Pickup, Actor: c.Picker, Flags: (c.Grant ? 1u : 0) | (c.Live ? 2u : 0));
    }

    public static SharedItemCollect CollectFromFrame(WorldFrame f)
    {
        if (f.Kind != WorldKind.SharedItem || !ValidPickup(f.Key) || f.Actor is not (1 or 2) || (f.Flags & ~3u) != 0 ||
            f.Asset.Length != 0 || f.Values is { Length: > 0 } || f.Data is { Length: > 0 })
            throw new InvalidDataException("Invalid shared pickup frame.");
        return new(f.Key, f.Actor, (f.Flags & 1) != 0, (f.Flags & 2) != 0);
    }

    public static WorldFrame ToFrame(SharedItemRequest r, uint epoch)
    {
        if (!ValidPickup(r.Pickup)) throw new InvalidDataException("Invalid shared pickup request.");
        return new(WorldKind.SharedItemRequest, epoch, 1, Key: r.Pickup, Actor: 2, Life: r.Life, Position: r.Feet);
    }

    public static SharedItemRequest RequestFromFrame(WorldFrame f)
    {
        if (f.Kind != WorldKind.SharedItemRequest || !ValidPickup(f.Key) || f.Actor != 2 || f.Flags != 0 ||
            f.Asset.Length != 0 || f.Values is { Length: > 0 } || f.Data is { Length: > 0 })
            throw new InvalidDataException("Invalid shared pickup request frame.");
        return new(f.Key, f.Life, f.Position);
    }

    // Entries are packed "id=quantity" tokens joined by ',' into at most 32 texts of at most 256 bytes.
    public static WorldFrame ToFrame(TeamInventory t, uint epoch)
    {
        Validate(t);
        var data = new List<string>(); var current = "";
        foreach (var item in t.Items.OrderBy(i => i.Id, StringComparer.Ordinal))
        {
            var token = item.Id + "=" + item.Quantity;
            if (current.Length > 0 && current.Length + 1 + token.Length > 256) { data.Add(current); current = ""; }
            current = current.Length == 0 ? token : current + "," + token;
        }
        if (current.Length > 0) data.Add(current);
        if (data.Count > 32) throw new InvalidDataException("Team inventory too large.");
        return new(WorldKind.TeamInventory, epoch, 1, Revision: t.Version, Data: data.ToArray());
    }

    public static TeamInventory TeamFromFrame(WorldFrame f)
    {
        if (f.Kind != WorldKind.TeamInventory || f.Key.Length != 0 || f.Asset.Length != 0 || f.Flags != 0 || f.Values is { Length: > 0 })
            throw new InvalidDataException("Invalid team inventory frame.");
        var items = new List<TeamItem>();
        foreach (var text in f.Data ?? Array.Empty<string>())
            foreach (var token in text.Split(','))
            {
                var split = token.Split('=');
                if (split.Length != 2 || !int.TryParse(split[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var quantity))
                    throw new InvalidDataException("Invalid team inventory entry.");
                items.Add(new(split[0], quantity));
            }
        var team = new TeamInventory(f.Revision, items);
        Validate(team);
        return team;
    }

    public static void Validate(TeamInventory t)
    {
        if (t.Items == null || t.Items.Count > MaxItems || t.Items.Any(i => !ValidId(i.Id) || i.Quantity is < 1 or > MaxQuantity) ||
            t.Items.Select(i => i.Id).Distinct(StringComparer.Ordinal).Count() != t.Items.Count)
            throw new InvalidDataException("Invalid team inventory.");
    }

    public static bool ValidId(string id) => !string.IsNullOrEmpty(id) && id.Length <= MaxId && id.All(c => c is > ' ' and <= '~' && c != ',' && c != '=');
    private static bool ValidPickup(string key) => key is { Length: > 2 } && key.Length <= MaxPickupKey &&
        (key.StartsWith("a:", StringComparison.Ordinal) || key.StartsWith("d:", StringComparison.Ordinal)) && key.All(c => c is > ' ' and <= '~');
}
