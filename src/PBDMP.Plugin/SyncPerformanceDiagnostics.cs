using System.Diagnostics;
using System.Globalization;

namespace PBDMP.Plugin;

// Opt-in native regression measurements; no diagnostic sampling or file IO in normal play.
internal static class SyncPerformance
{
    internal static readonly bool Enabled=Environment.GetEnvironmentVariable("PBDMP_SYNC_PERF_TEST")=="1";
    internal static void ApplyFrameRate()
    {
        if(Enabled && int.TryParse(Environment.GetEnvironmentVariable("PBDMP_SYNC_PERF_RATE"),out var rate) && rate>0)
        {UnityEngine.QualitySettings.vSyncCount=0;UnityEngine.Application.targetFrameRate=rate;}
    }
    private static float _cycleStarted;
    internal static void BeginCycle()=>_cycleStarted=UnityEngine.Time.unscaledTime;
    private static readonly Dictionary<string,List<double>> Samples=new();
    private static int _warmup;
    private static bool _previousSync,_thisSync;
    internal static long Start()=>Enabled?Stopwatch.GetTimestamp():0;
    internal static void Finish(string name,long start)
    {
        if(!Enabled || start==0 || SharedWorld.Current?.ReadyForPerformance!=true || Samples.GetValueOrDefault("sync")?.Count>=80) return;
        if(!Samples.TryGetValue(name,out var values)) Samples[name]=values=new();
        values.Add((Stopwatch.GetTimestamp()-start)*1000d/Stopwatch.Frequency);
        if(name=="world")
        {
            var section=_previousSync?"frame-after-sync":"frame-without-sync";
            if(!Samples.TryGetValue(section,out var frames)) Samples[section]=frames=new();
            frames.Add(UnityEngine.Time.unscaledDeltaTime*1000d);
            _previousSync=_thisSync;_thisSync=false;
        }
    }
    private static void Add(string name,double value)
    {if(!Samples.TryGetValue(name,out var values)) Samples[name]=values=new();values.Add(value);}
    internal static void Slice(double milliseconds)
    {
        if(!Enabled || SharedWorld.Current?.ReadyForPerformance!=true || Ready) return;
        _thisSync=true;Add("slice",milliseconds);
    }
    internal static void Sync(double milliseconds,long allocated)
    {
        if(!Enabled || SharedWorld.Current?.ReadyForPerformance!=true || ++_warmup<=5) return;
        _thisSync=true;
        if(Ready) return;
        Add("sync",milliseconds);
        Add("cycle-latency",(UnityEngine.Time.unscaledTime-_cycleStarted)*1000d);
        if(!Samples.TryGetValue("sync-allocated-bytes",out var values)) Samples["sync-allocated-bytes"]=values=new();
        if(values.Count<80) values.Add(allocated);
    }
    internal static bool Ready=>Samples.GetValueOrDefault("sync")?.Count>=80;
    internal static double CycleP95
    {
        get {var times=Samples["cycle-latency"].OrderBy(v=>v).ToArray();return times[(int)Math.Ceiling(times.Length*0.95)-1];}
    }
    internal static double Export(string path)
    {
        var rows=new List<string>{"section\tcount\tmedian\tp95\tmax"};
        foreach(var pair in Samples.OrderBy(p=>p.Key))
        {
            var sorted=pair.Value.OrderBy(v=>v).ToArray();
            rows.Add(string.Join("\t",pair.Key,sorted.Length,sorted[sorted.Length/2].ToString("F3",CultureInfo.InvariantCulture),
                sorted[(int)Math.Ceiling(sorted.Length*0.95)-1].ToString("F3",CultureInfo.InvariantCulture),sorted[^1].ToString("F3",CultureInfo.InvariantCulture)));
        }
        File.WriteAllLines(path,rows);
        var work=Samples["world"].OrderBy(v=>v).ToArray();return work[(int)Math.Ceiling(work.Length*0.95)-1];
    }
}
