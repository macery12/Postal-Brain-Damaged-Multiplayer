using System.Globalization;

namespace PBDMP.Network;

// Native HORDE controller states, in native enum order.
public enum SurvivalPhase : byte { Intro, Combat, Cleanup, Rest }

// Damage reports one eligible native damage event on P2 for the shared combo penalty.
public enum SurvivalRequestKind : byte { Ready = 1, Offer = 2, Damage = 3 }

// One host-selected preparation offer: native spawn slot plus the authored prefab, "W<n>" for the
// spawner's weapon list or "I<n>" for its item list. Both peers hold identical authored arrays.
public readonly record struct SurvivalOffer(int Slot, string Token);

// P1's authoritative Survival run presentation state. Run increases on every native Begin
// in a session; retry is a new travel epoch as well as a new run. Score and damage use
// decimal strings so long runs are not capped by the bounded world-frame float range.
// Masks use bit 1 for P1 and bit 2 for P2. Offers is the canonical "slot:token,..." list.
// RunId is P1's random 32-hex identity for the run (co-op records); ResultTime (the native
// level TimeStat value) and ResultWeapon (best weapon asset ID) are the final team result, sent once Ended.
public sealed record SurvivalState(uint Run, SurvivalPhase Phase, int Wave, int WavesSurvived, float TimeLeft,
    int Countdown, bool CountdownDone, double Score, float Combo, int Kills, int Alive, double Damage,
    byte Ready = 0, byte Required = 0, byte Revive = 0, bool Ended = false, string Offers = "",
    string RunId = "", int ResultTime = 0, string ResultWeapon = "");

// P2's bounded preparation request for the host's current preparation phase.
public sealed record SurvivalRequest(uint Run, int Wave, SurvivalPhase Phase, SurvivalRequestKind Kind, string Offer = "");

public static class SurvivalProtocol
{
    public const int MaxWave = 1_000_000, MaxCountdown = 10, MaxKills = 1_000_000_000, MaxAlive = 4096, MaxOffers = 8, MaxSlot = 15, MaxPrefab = 63;
    public const float MinTime = -60, MaxTime = 86_400, MinCombo = 1, MaxCombo = 5;
    public const double MaxScore = 1e15;
    public const int MaxResultTime = 100_000_000, MaxAssetId = 64;
    public const byte Host = 1, Partner = 2, Everyone = 3;

    public static void Validate(SurvivalState s)
    {
        if (!Enum.IsDefined(typeof(SurvivalPhase), s.Phase) || s.Wave is < 1 or > MaxWave || s.WavesSurvived < 0 || s.WavesSurvived > s.Wave ||
            !float.IsFinite(s.TimeLeft) || s.TimeLeft is < MinTime or > MaxTime || s.Countdown is < 0 or > MaxCountdown ||
            !Valid(s.Score) || !Valid(s.Damage) || !float.IsFinite(s.Combo) || s.Combo < MinCombo - 0.001f || s.Combo > MaxCombo + 0.001f ||
            s.Kills is < 0 or > MaxKills || s.Alive is < 0 or > MaxAlive || s.Ready > Everyone || s.Required > Everyone || s.Revive > Everyone ||
            s.Offers == null || (s.Offers.Length > 0 && s.Phase != SurvivalPhase.Rest) || !ValidRunId(s.RunId) ||
            s.ResultTime is < 0 or > MaxResultTime || s.ResultWeapon == null || s.ResultWeapon.Length > MaxAssetId ||
            s.ResultWeapon.Any(c => c is < '!' or > '~' || c == ',') || (!s.Ended && (s.ResultTime != 0 || s.ResultWeapon.Length != 0)))
            throw new InvalidDataException($"Invalid Survival state: run={s.Run}, phase={s.Phase}, wave={s.Wave}/{s.WavesSurvived}, time={s.TimeLeft}, countdown={s.Countdown}, score={s.Score}, combo={s.Combo}, kills={s.Kills}, alive={s.Alive}, damage={s.Damage}, masks={s.Ready}/{s.Required}/{s.Revive}.");
        ParseOffers(s.Offers);
    }

    public static WorldFrame ToFrame(SurvivalState s, uint epoch)
    {
        Validate(s);
        return new(WorldKind.Survival, epoch, 1, Entity: s.Run, Flags: (uint)s.Phase | (s.CountdownDone ? 16u : 0) | (s.Ended ? 32u : 0),
            Value: s.TimeLeft, Value2: s.Combo,
            Data: new[] { Text(s.Score), Text(s.Damage), Int(s.Wave), Int(s.WavesSurvived), Int(s.Countdown), Int(s.Kills), Int(s.Alive),
                Int(s.Ready), Int(s.Required), Int(s.Revive), s.Offers, s.RunId, Int(s.ResultTime), s.ResultWeapon });
    }

    public static SurvivalState FromFrame(WorldFrame f)
    {
        if (f.Kind != WorldKind.Survival || (f.Flags & ~63u) != 0 || (f.Flags & 15) > 3 || f.Data is not { Length: 14 } d ||
            f.Key.Length != 0 || f.Asset.Length != 0 || f.Values is { Length: > 0 } || f.Revision != 0 || f.Actor != 0 || f.Life != 0)
            throw new InvalidDataException("Invalid Survival frame layout.");
        var state = new SurvivalState(f.Entity, (SurvivalPhase)(f.Flags & 15), ParseInt(d[2]), ParseInt(d[3]), f.Value,
            ParseInt(d[4]), (f.Flags & 16) != 0, ParseDouble(d[0]), f.Value2, ParseInt(d[5]), ParseInt(d[6]), ParseDouble(d[1]),
            Mask(d[7]), Mask(d[8]), Mask(d[9]), (f.Flags & 32) != 0, d[10], d[11], ParseInt(d[12]), d[13]);
        Validate(state);
        return state;
    }

    // A frame from an older run within the same epoch is stale and must not replace newer state.
    public static bool Supersedes(SurvivalState? current, SurvivalState next) => current == null || next.Run >= current.Run;

    public static WorldFrame ToFrame(SurvivalRequest r, uint epoch)
    {
        ValidateRequest(r);
        return new(WorldKind.SurvivalRequest, epoch, 1, Entity: r.Run, Revision: (uint)r.Wave, Actor: 2, Flags: (uint)r.Phase | ((uint)r.Kind << 4),
            Data: r.Kind == SurvivalRequestKind.Offer ? new[] { r.Offer } : Array.Empty<string>());
    }

    public static SurvivalRequest RequestFromFrame(WorldFrame f)
    {
        if (f.Kind != WorldKind.SurvivalRequest || f.Actor != 2 || (f.Flags & ~0x3Fu) != 0 || f.Key.Length != 0 || f.Asset.Length != 0 ||
            f.Values is { Length: > 0 } || f.Life != 0 || f.Data is not { } d || d.Length > 1 || f.Revision > MaxWave)
            throw new InvalidDataException("Invalid Survival request layout.");
        var request = new SurvivalRequest(f.Entity, (int)f.Revision, (SurvivalPhase)(f.Flags & 15), (SurvivalRequestKind)(f.Flags >> 4), d.Length == 1 ? d[0] : "");
        ValidateRequest(request);
        if ((request.Kind == SurvivalRequestKind.Offer) != (d.Length == 1)) throw new InvalidDataException("Invalid Survival request offer.");
        return request;
    }

    public static void ValidateRequest(SurvivalRequest r)
    {
        if (!Enum.IsDefined(typeof(SurvivalPhase), r.Phase) || r.Wave is < 1 or > MaxWave || r.Offer == null ||
            r.Kind is not (SurvivalRequestKind.Ready or SurvivalRequestKind.Offer or SurvivalRequestKind.Damage) ||
            (r.Kind != SurvivalRequestKind.Damage && r.Phase is not (SurvivalPhase.Intro or SurvivalPhase.Rest)) ||
            (r.Kind == SurvivalRequestKind.Offer ? !ValidToken(r.Offer) || r.Phase != SurvivalPhase.Rest : r.Offer.Length != 0))
            throw new InvalidDataException($"Invalid Survival request: run={r.Run}, wave={r.Wave}, phase={r.Phase}, kind={r.Kind}, offer={r.Offer}.");
    }

    public static string FormatOffers(IEnumerable<SurvivalOffer> offers)
    {
        var text = string.Join(",", offers.Select(o => Int(o.Slot) + ":" + o.Token));
        ParseOffers(text);
        return text;
    }

    public static SurvivalOffer[] ParseOffers(string text)
    {
        if (text.Length == 0) return Array.Empty<SurvivalOffer>();
        var parts = text.Split(',');
        if (parts.Length > MaxOffers) throw new InvalidDataException("Too many Survival offers.");
        var offers = new SurvivalOffer[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            var pair = parts[i].Split(':');
            if (pair.Length != 2 || !ValidToken(pair[1])) throw new InvalidDataException("Invalid Survival offer.");
            var slot = ParseInt(pair[0]);
            if (slot is < 0 or > MaxSlot || offers.Take(i).Any(o => o.Slot == slot)) throw new InvalidDataException("Invalid Survival offer slot.");
            offers[i] = new(slot, pair[1]);
        }
        return offers;
    }

    public static bool ValidRunId(string id) => id != null && (id.Length == 0 || id.Length == 32 && id.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'));

    public static bool ValidToken(string token) =>
        token.Length is >= 2 and <= 3 && token[0] is 'W' or 'I' && int.TryParse(token.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var index) &&
        index <= MaxPrefab && index.ToString(CultureInfo.InvariantCulture) == token.Substring(1);

    public static bool Weapon(string token, out int index)
    {
        if (!ValidToken(token)) throw new InvalidDataException("Invalid Survival offer token.");
        index = int.Parse(token.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture);
        return token[0] == 'W';
    }

    private static bool Valid(double v) => double.IsFinite(v) && v >= 0 && v <= MaxScore;
    private static string Text(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    private static string Int(int v) => v.ToString(CultureInfo.InvariantCulture);
    private static byte Mask(string t) { var v = ParseInt(t); if (v is < 0 or > Everyone) throw new InvalidDataException("Invalid Survival mask."); return (byte)v; }
    private static int ParseInt(string t)
    {
        if (t.Length is 0 or > 11 || !int.TryParse(t, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v) || v.ToString(CultureInfo.InvariantCulture) != t)
            throw new InvalidDataException("Invalid Survival integer.");
        return v;
    }
    private static double ParseDouble(string t)
    {
        if (t.Length is 0 or > 32 || !double.TryParse(t, NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var v) || !double.IsFinite(v))
            throw new InvalidDataException("Invalid Survival decimal.");
        return v;
    }
}
