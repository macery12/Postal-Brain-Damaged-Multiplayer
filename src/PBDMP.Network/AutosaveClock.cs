namespace PBDMP.Network;

/// <summary>Accumulates gameplay time, retaining one due capture until it can be accepted.</summary>
public sealed class AutosaveClock
{
    private readonly double _interval;
    private double _elapsed;
    public AutosaveClock(double intervalSeconds)
    {
        if(!double.IsFinite(intervalSeconds) || intervalSeconds<=0) throw new ArgumentOutOfRangeException(nameof(intervalSeconds));
        _interval=intervalSeconds;
    }
    public double Remaining=>Math.Max(0,_interval-_elapsed);
    public bool Due=>_elapsed>=_interval;
    public void Advance(double seconds,bool playing)
    {
        if(!double.IsFinite(seconds) || seconds<0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if(playing) _elapsed=Math.Min(_interval,_elapsed+seconds);
    }
    public bool TryConsume()
    {if(!Due) return false;Reset();return true;}
    public void Reset()=>_elapsed=0;
}
