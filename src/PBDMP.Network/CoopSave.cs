using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PBDMP.Network;

/// <summary>Disk state is independent of transport epochs and contains no native pointers.</summary>
public sealed record SavedPlayer(string Identity, string Level, Dictionary<string,string> Native, string[] Collected, PlayerCarrier? Carrier = null);
public sealed class CoopSave
{
    public int Schema { get; set; } = CoopSaveStore.Schema;
    public string Game { get; set; } = "";
    public string Mod { get; set; } = "";
    public string Catalogue { get; set; } = "";
    public string Slot { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    public string Level { get; set; } = "";
    public int Difficulty { get; set; }
    public Dictionary<string,Dictionary<string,string>> Native { get; set; } = new();
    public Dictionary<string,string> Campaign { get; set; } = new();
    public SavedPlayer Host { get; set; } = new("", "", new(), Array.Empty<string>());
    // Schema 3: one personal record per player who has joined this campaign (anyone can play).
    public SavedPlayer[] Guests { get; set; } = Array.Empty<SavedPlayer>();
    // Display names of everyone who has played this campaign, P1 first.
    public string[] Players { get; set; } = Array.Empty<string>();
    public WorldFrame[] World { get; set; } = Array.Empty<WorldFrame>();
}

public sealed record SaveListing(string Slot, string Name, string Level, DateTimeOffset Created, string? Error, bool Backup, int Difficulty = 0, string[]? Players = null);
public sealed record LoadedSave(CoopSave Save, bool Recovered);
// Small sidecar written after each save so listings never parse multi-megabyte worlds.
public sealed record SaveInfo(int Schema, string Slot, string Name, string Level, DateTimeOffset Created, int Difficulty, string[] Players);
// A co-op campaign is one folder of saves: one continuous run of the story.
public sealed record CampaignListing(string Id, string Level, int Difficulty, DateTimeOffset LastPlayed, string[] Players, string NewestSlot, int Saves);

/// <summary>Bounded data-only files, atomic replacement and a previous known-good generation.</summary>
public sealed class CoopSaveStore
{
    public const int Schema = 3, MaxPlayers = 8, MaxThumbnailBytes = 2 * 1024 * 1024;
    public const int MaxBytes = 32 * 1024 * 1024;
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("PBDMPS01");
    private static readonly JsonSerializerOptions Json = new() { MaxDepth = 64 };
    private readonly string _root;
    public CoopSaveStore(string root) => _root = Path.GetFullPath(root);
    public string Folder => _root;
    public static bool ValidSlot(string slot) => slot.Length is > 0 and <= 64 && slot.All(c => (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9') || c == '-');
    private string FileFor(string slot)
    {
        if (!ValidSlot(slot)) throw new InvalidDataException("Invalid co-op save slot.");
        return Path.Combine(_root, slot + ".pbdmp");
    }
    public static void Validate(CoopSave save)
    {
        if (save.Schema != Schema) throw new InvalidDataException(save.Schema is 1 or 2 ? OlderFormat : "Unsupported co-op save format.");
        if (!ValidSlot(save.Slot) || string.IsNullOrWhiteSpace(save.Name) || save.Name.Length > 80 || save.Name.Any(char.IsControl) ||
            save.Game.Length != 129 || save.Game[64] != ':' || save.Game.Where(c=>c!=':').Any(c=>!Uri.IsHexDigit(c)) ||
            save.Level.Length is < 1 or > 64 || save.Catalogue.Length != 64 || save.Difficulty is < 0 or > 15 ||
            save.Native.Count is < 1 or > 32768 || save.Campaign.Count > 512 || save.World.Length > 32768)
            throw new InvalidDataException("Invalid co-op save metadata or size.");
        ValidatePlayer(save.Host);
        if (save.Guests == null || save.Guests.Length > MaxPlayers || save.Guests.Select(g => g?.Identity).Distinct(StringComparer.Ordinal).Count() != save.Guests.Length)
            throw new InvalidDataException("Invalid co-op player records.");
        foreach (var guest in save.Guests) ValidatePlayer(guest);
        ValidatePlayers(save.Players);
        foreach (var pair in save.Native) { Text(pair.Key,256); Pairs(pair.Value); }
        foreach (var pair in save.Campaign) { Text(pair.Key,64); Text(pair.Value,1048576); }
        foreach (var frame in save.World)
        {
            WorldProtocol.Validate(frame);
            if (!WorldProtocol.Durable(frame))
                throw new InvalidDataException("Save contains transient or unauthorized world events.");
        }
    }
    public static void ValidatePlayer(SavedPlayer player)
    {
        if (player == null || player.Identity == null || player.Level == null || player.Native == null || player.Collected == null)
            throw new InvalidDataException("Missing co-op player record.");
        Text(player.Identity,128); Text(player.Level,64); Pairs(player.Native); PlayerCarrier.Validate(player.Carrier);
        if (!player.Native.ContainsKey("_saveData") || player.Collected.Length > 32768 || player.Collected.Distinct(StringComparer.Ordinal).Count() != player.Collected.Length)
            throw new InvalidDataException("Invalid personal co-op state.");
        foreach (var key in player.Collected) Text(key,128);
        // Native JSON uses a fixed player type, never a peer-supplied type name.
        using var json = JsonDocument.Parse(player.Native["_saveData"], new JsonDocumentOptions { MaxDepth = 32 });
        CheckJson(json.RootElement);
    }
    private static void CheckJson(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object) foreach (var p in value.EnumerateObject())
        {
            if (p.Name.StartsWith('$')) throw new InvalidDataException("Polymorphic save metadata is not accepted.");
            CheckJson(p.Value);
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var p in value.EnumerateArray()) CheckJson(p);
        else if (value.ValueKind == JsonValueKind.Number && (!value.TryGetDouble(out var n) || !double.IsFinite(n) || Math.Abs(n) > 1e12))
            throw new InvalidDataException("Invalid native player scalar.");
    }
    private static void Pairs(Dictionary<string,string> pairs)
    {
        if (pairs == null || pairs.Count > 4096) throw new InvalidDataException("Invalid native save entry.");
        foreach (var pair in pairs) { Text(pair.Key,256); Text(pair.Value,8*1024*1024,true); }
    }
    private static void Text(string value,int max,bool empty=false)
    { if(value == null || (!empty && value.Length==0) || Encoding.UTF8.GetByteCount(value)>max) throw new InvalidDataException("Invalid co-op save text."); }
    public const string OlderFormat = "This co-op save was made by an older development build and can't be loaded. Start a new co-op game.";
    public static void ValidatePlayers(string[] players)
    {
        if (players == null || players.Length > MaxPlayers || players.Any(p => string.IsNullOrWhiteSpace(p) || p.Length > 64 || p.Any(char.IsControl)))
            throw new InvalidDataException("Invalid co-op player names.");
    }
    public static void RequireRestorableFormat(CoopSave save)
    {
        if(save.Schema!=Schema) throw new InvalidDataException(OlderFormat);
    }
    public void Write(CoopSave save, byte[]? thumbnail = null)
    {
        Validate(save);
        if (thumbnail != null && (thumbnail.Length > MaxThumbnailBytes || thumbnail.Length < Png.Length || !thumbnail.AsSpan(0, Png.Length).SequenceEqual(Png)))
            throw new InvalidDataException("Invalid save thumbnail.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(save, Json);
        if (bytes.Length > MaxBytes) throw new InvalidDataException("Co-op save exceeds the supported size.");
        Directory.CreateDirectory(_root);
        var path = FileFor(save.Slot); var temp = path + ".writing-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.WriteThrough))
            {
                using var writer = new BinaryWriter(stream,Encoding.UTF8,true);
                writer.Write(Magic);writer.Write(bytes.Length);writer.Write(SHA256.HashData(bytes));writer.Write(bytes);writer.Flush();stream.Flush(true);
            }
            // A corrupt/incompatible primary must never replace a valid recovery generation.
            if (File.Exists(path))
            {
                var valid = true;
                try
                {
                    var previous=ReadFile(path);
                    // An incompatible primary for this same map must not
                    // displace the usable recovery generation on the next save.
                    valid=previous.Schema==save.Schema && previous.Game==save.Game &&
                        (previous.Level!=save.Level || previous.Catalogue==save.Catalogue);
                }
                catch (Exception e) when (e is IOException or InvalidDataException or JsonException or ArgumentException or NullReferenceException) { valid=false; }
                File.Replace(temp,path,valid?path+".bak":null);
            }
            else File.Move(temp,path);
        }
        finally { if(File.Exists(temp)) File.Delete(temp); }
        // Sidecars follow the committed save; a missing or stale one only costs a full read.
        WriteSmall(Path.Combine(_root, save.Slot + ".info"), JsonSerializer.SerializeToUtf8Bytes(new SaveInfo(save.Schema, save.Slot, save.Name, save.Level, save.Created, save.Difficulty, save.Players), Json));
        var image = Path.Combine(_root, save.Slot + ".png");
        if (thumbnail != null) WriteSmall(image, thumbnail); else if (File.Exists(image)) File.Delete(image);
    }
    private static void WriteSmall(string path, byte[] bytes)
    {
        var temp = path + ".writing-" + Guid.NewGuid().ToString("N");
        try { File.WriteAllBytes(temp, bytes); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public byte[]? Thumbnail(string slot)
    {
        var path = Path.Combine(_root, ValidSlot(slot) ? slot + ".png" : throw new InvalidDataException("Invalid co-op save slot."));
        if (!File.Exists(path) || new FileInfo(path).Length > MaxThumbnailBytes) return null;
        var bytes = File.ReadAllBytes(path);
        return bytes.Length >= Png.Length && bytes.AsSpan(0, Png.Length).SequenceEqual(Png) ? bytes : null;
    }
    public void Delete(string slot)
    {
        var path = FileFor(slot);
        foreach (var file in new[] { path, path + ".bak", Path.Combine(_root, slot + ".info"), Path.Combine(_root, slot + ".png") })
            if (File.Exists(file)) File.Delete(file);
    }
    private SaveInfo? Info(string slot)
    {
        var path = Path.Combine(_root, slot + ".info"); var save = FileFor(slot);
        if (!File.Exists(path) || !File.Exists(save) || File.GetLastWriteTimeUtc(path) < File.GetLastWriteTimeUtc(save) || new FileInfo(path).Length > 64 * 1024) return null;
        try
        {
            var info = JsonSerializer.Deserialize<SaveInfo>(File.ReadAllBytes(path), Json);
            if (info == null || info.Schema != Schema || info.Slot != slot || string.IsNullOrWhiteSpace(info.Name) || info.Name.Length > 80 ||
                info.Level.Length is < 1 or > 64 || info.Difficulty is < 0 or > 15) return null;
            ValidatePlayers(info.Players);
            return info;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or JsonException or ArgumentException or NullReferenceException) { return null; }
    }
    public LoadedSave Read(string slot)
    {
        var path=FileFor(slot);
        try { var save=ReadFile(path); if(save.Slot!=slot) throw new InvalidDataException("Save slot mismatch."); return new(save,false); }
        catch(Exception primary) when(primary is IOException or InvalidDataException or JsonException or ArgumentException or NullReferenceException)
        {
            try { var save=ReadFile(path+".bak");if(save.Slot!=slot) throw new InvalidDataException("Backup slot mismatch."); return new(save,true); }
            catch(Exception backup) when(backup is IOException or InvalidDataException or JsonException or ArgumentException or NullReferenceException)
            { throw new InvalidDataException("Co-op save and its recovery backup could not be loaded. " + primary.Message, primary); }
        }
    }
    public LoadedSave ReadCompatibleRecovery(string slot,string game,string catalogue,string level)
    {
        var save=ReadFile(FileFor(slot)+".bak");
        RequireRestorableFormat(save);
        if(save.Slot!=slot || save.Game!=game || save.Catalogue!=catalogue || save.Level!=level)
            throw new InvalidDataException("Saved world is incompatible and no matching recovery backup is available. Original files were preserved.");
        return new(save,true);
    }
    private static CoopSave ReadFile(string path)
    {
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(stream.Length < 44 || stream.Length > MaxBytes+44) throw new InvalidDataException("Invalid co-op save size.");
        using var reader=new BinaryReader(stream);
        if(!reader.ReadBytes(8).SequenceEqual(Magic)) throw new InvalidDataException("Not a PBDMP co-op save.");
        var count=reader.ReadInt32(); if(count<1 || count>MaxBytes || count!=stream.Length-44) throw new InvalidDataException("Incomplete co-op save.");
        var hash=reader.ReadBytes(32);var bytes=reader.ReadBytes(count);
        if(!CryptographicOperations.FixedTimeEquals(hash,SHA256.HashData(bytes))) throw new InvalidDataException("Co-op save checksum failed.");
        var save=JsonSerializer.Deserialize<CoopSave>(bytes,Json)??throw new InvalidDataException("Missing co-op save data."); Validate(save);return save;
    }
    public SaveListing[] List()
    {
        if(!Directory.Exists(_root)) return Array.Empty<SaveListing>();
        return Directory.EnumerateFiles(_root,"*.pbdmp*").Where(p=>p.EndsWith(".pbdmp",StringComparison.Ordinal) || p.EndsWith(".pbdmp.bak",StringComparison.Ordinal))
            .Select(p=>Path.GetFileName(p).Split('.')[0]).Where(ValidSlot).Distinct().Take(512).Select(slot=>
            {
                if(Info(slot) is {} info) return new SaveListing(slot,info.Name,info.Level,info.Created,null,false,info.Difficulty,info.Players);
                try {var loaded=Read(slot);var s=loaded.Save;return new SaveListing(slot,s.Name,s.Level,s.Created,null,loaded.Recovered,s.Difficulty,s.Players);}
                catch(Exception e) when(e is IOException or InvalidDataException or JsonException or ArgumentException or NullReferenceException) {return new SaveListing(slot,slot,"",DateTimeOffset.MinValue,e.Message,false);}
            }).OrderByDescending(s=>s.Created).ToArray();
    }

    // Campaign folders: 32 lower-case hex digits under the saves root.
    public static bool ValidCampaign(string id) => id.Length == 32 && id.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static string NewCampaignId() => Guid.NewGuid().ToString("N");
    public static string CampaignFolder(string root, string id) =>
        ValidCampaign(id) ? Path.Combine(Path.GetFullPath(root), id) : throw new InvalidDataException("Invalid co-op campaign.");
    public static CampaignListing[] Campaigns(string root)
    {
        root = Path.GetFullPath(root);
        if (!Directory.Exists(root)) return Array.Empty<CampaignListing>();
        var campaigns = new List<CampaignListing>();
        foreach (var dir in Directory.EnumerateDirectories(root).Take(512))
        {
            var id = Path.GetFileName(dir);
            if (!ValidCampaign(id) || new DirectoryInfo(dir).Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            var saves = new CoopSaveStore(dir).List().Where(s => s.Error == null).ToArray();
            if (saves.Length == 0) continue;
            var newest = saves[0];
            var players = saves.SelectMany(s => s.Players ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).Take(MaxPlayers).ToArray();
            campaigns.Add(new(id, newest.Level, newest.Difficulty, newest.Created, players, newest.Slot, saves.Length));
        }
        return campaigns.OrderByDescending(c => c.LastPlayed).ToArray();
    }
    public static void DeleteCampaign(string root, string id)
    {
        var dir = CampaignFolder(root, id);
        if (!Directory.Exists(dir)) return;
        if (new DirectoryInfo(dir).Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("A co-op campaign folder cannot be a link.");
        foreach (var file in Directory.EnumerateFiles(dir)) File.Delete(file);
        Directory.Delete(dir);
    }
}
