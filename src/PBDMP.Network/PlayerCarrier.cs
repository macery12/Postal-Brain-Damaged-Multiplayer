namespace PBDMP.Network;

/// <summary>Authored moving-platform identity and feet in its local frame; never a native pointer.</summary>
public sealed record PlayerCarrier(string Key, NetVector Offset)
{
    public static void Validate(PlayerCarrier? carrier)
    {
        if(carrier==null) return;
        if(carrier.Key==null || carrier.Key.Length!=64 || carrier.Key.Any(c=>!Uri.IsHexDigit(c)) ||
            !float.IsFinite(carrier.Offset.X) || !float.IsFinite(carrier.Offset.Y) || !float.IsFinite(carrier.Offset.Z) ||
            Math.Abs(carrier.Offset.X)>256 || Math.Abs(carrier.Offset.Y)>256 || Math.Abs(carrier.Offset.Z)>256)
            throw new InvalidDataException("Invalid player carrier.");
    }
}
