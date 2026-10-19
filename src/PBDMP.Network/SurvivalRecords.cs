using System.Text.Json;

namespace PBDMP.Network;

// One finished co-op Survival team result, identical on both PCs (P1's final state frame).
public sealed record SurvivalResult(string RunId, string Arena, int Difficulty, double Score, int Kills, int WavesSurvived,
    double Damage, string BestWeapon, int CompletionTime, DateTime EndedUtc);

public sealed record SurvivalRecordFile(int Version, SurvivalResult[] Best, SurvivalResult[] Recent)
{
    public static readonly SurvivalRecordFile Empty = new(SurvivalRecordStore.Version, Array.Empty<SurvivalResult>(), Array.Empty<SurvivalResult>());
}

public readonly record struct RecordOutcome(bool Added, bool NewBest, SurvivalResult? PreviousBest);

// Local co-op Survival records: best result per arena and difficulty plus recent results.
// Separate from solo metadata and campaign saves; records never resume a run. Writes replace the
// file atomically; an unreadable file is set aside once and the store starts empty.
public sealed class SurvivalRecordStore
{
    public const int Version = 1, MaxRecent = 20, MaxBest = 64, MaxBytes = 256 * 1024;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _path;
    public bool RecoveredCorruption { get; private set; }

    public SurvivalRecordStore(string path) => _path = path;

    public SurvivalRecordFile Load()
    {
        if (!File.Exists(_path)) return SurvivalRecordFile.Empty;
        try
        {
            var bytes = File.ReadAllBytes(_path);
            if (bytes.Length > MaxBytes) throw new InvalidDataException("Survival records exceed the supported size.");
            var file = JsonSerializer.Deserialize<SurvivalRecordFile>(bytes) ?? throw new InvalidDataException("Empty Survival records.");
            Validate(file);
            return file;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or JsonException or ArgumentException or NullReferenceException)
        {
            // Keep the unreadable file for inspection; never fail the session over local records.
            File.Copy(_path, _path + ".corrupt", true); File.Delete(_path);
            RecoveredCorruption = true;
            return SurvivalRecordFile.Empty;
        }
    }

    public SurvivalResult? Best(string arena, int difficulty) => Load().Best.FirstOrDefault(r => r.Arena == arena && r.Difficulty == difficulty);

    // Idempotent per run: a result already stored for the same run is ignored.
    public RecordOutcome Record(SurvivalResult result)
    {
        Validate(result);
        var file = Load();
        if (file.Recent.Any(r => r.RunId == result.RunId) || file.Best.Any(r => r.RunId == result.RunId))
            return new(false, false, Best(file, result));
        var previous = Best(file, result);
        var newBest = previous == null || result.Score > previous.Score;
        var best = newBest ? file.Best.Where(r => r.Arena != result.Arena || r.Difficulty != result.Difficulty).Append(result).ToArray() : file.Best;
        if (best.Length > MaxBest) throw new InvalidDataException("Too many Survival record categories.");
        var recent = new[] { result }.Concat(file.Recent).Take(MaxRecent).ToArray();
        Write(new(Version, best, recent));
        return new(true, newBest, previous);
    }

    private static SurvivalResult? Best(SurvivalRecordFile file, SurvivalResult result) =>
        file.Best.FirstOrDefault(r => r.Arena == result.Arena && r.Difficulty == result.Difficulty);

    private void Write(SurvivalRecordFile file)
    {
        Validate(file);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(file, Json);
        if (bytes.Length > MaxBytes) throw new InvalidDataException("Survival records exceed the supported size.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var temp = _path + ".writing-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { stream.Write(bytes); stream.Flush(true); }
            if (File.Exists(_path)) File.Replace(temp, _path, null); else File.Move(temp, _path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static void Validate(SurvivalRecordFile file)
    {
        if (file.Version != Version || file.Best == null || file.Recent == null || file.Best.Length > MaxBest || file.Recent.Length > MaxRecent ||
            file.Best.GroupBy(r => (r?.Arena, r?.Difficulty)).Any(g => g.Count() > 1))
            throw new InvalidDataException("Invalid Survival records file.");
        foreach (var result in file.Best.Concat(file.Recent)) Validate(result);
    }

    public static void Validate(SurvivalResult r)
    {
        if (r == null || !SurvivalProtocol.ValidRunId(r.RunId) || r.RunId.Length == 0 || string.IsNullOrEmpty(r.Arena) || r.Arena.Length > SurvivalProtocol.MaxAssetId ||
            r.Arena.Any(char.IsControl) || r.Difficulty is < 0 or > 15 || !double.IsFinite(r.Score) || r.Score is < 0 or > SurvivalProtocol.MaxScore ||
            r.Kills is < 0 or > SurvivalProtocol.MaxKills || r.WavesSurvived is < 0 or > SurvivalProtocol.MaxWave || !double.IsFinite(r.Damage) ||
            r.Damage is < 0 or > SurvivalProtocol.MaxScore || r.BestWeapon == null || r.BestWeapon.Length > SurvivalProtocol.MaxAssetId ||
            r.BestWeapon.Any(char.IsControl) || r.CompletionTime is < 0 or > SurvivalProtocol.MaxResultTime)
            throw new InvalidDataException("Invalid Survival result.");
    }
}
