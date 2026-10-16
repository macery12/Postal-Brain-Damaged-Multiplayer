namespace PBDMP.Network;

/// <summary>Native incremental fill/drain can cross an endpoint before its next update.</summary>
public static class NativeProgress
{
    public static float Unit(float value)
    {
        if(!float.IsFinite(value) || Math.Abs(value)>1_000_000) throw new InvalidDataException("Invalid native progress value.");
        return Math.Clamp(value,0,1);
    }
}
