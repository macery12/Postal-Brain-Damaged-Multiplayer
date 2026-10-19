using System.Diagnostics;
using PBDMP.Network;
using Hyperstrange.PBD;
using Hyperstrange.PBD.UI.Menus;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PBDMP.Plugin;

// Opt-in benchmark only. Never writes or samples during ordinary gameplay.
internal static class PerformanceBaseline
{
    internal static readonly bool Enabled=Environment.GetEnvironmentVariable("PBDMP_PERF_BASELINE")=="1";
    private readonly record struct Sample(double Frame,double Mod,double World,double Lifecycle,double Persistence,long Allocated);
    private static readonly List<Sample> Frames=new(Enabled?32768:0);
    private static readonly Dictionary<string,List<double>> Sections=new();
    private static long _last,_allocated;
    private static double _previousMod,_world,_life,_save;
    private static string _phase="",_workload="";
    private static float _sampleAt,_endAt;
    private static int _gen0,_gen1,_gen2;
    private static bool _samplingStarted;
    private static ProtocolTraffic.Snapshot _traffic;
    private static long _trafficAt;
    internal static long BeginUpdate()
    {
        if(!Enabled) return 0;
        var now=Stopwatch.GetTimestamp();
        if(_phase.Length>0 && Time.unscaledTime>=_sampleAt && !_samplingStarted)
        {
            _samplingStarted=true;_last=0;_traffic=ProtocolTraffic.Read();_trafficAt=now;
            _gen0=GC.CollectionCount(0);_gen1=GC.CollectionCount(1);_gen2=GC.CollectionCount(2);
        }
        if(_phase.Length>0 && _samplingStarted && _last!=0)
            Frames.Add(new((now-_last)*1000d/Stopwatch.Frequency,_previousMod,_world,_life,_save,GC.GetAllocatedBytesForCurrentThread()-_allocated));
        _last=now;_allocated=GC.GetAllocatedBytesForCurrentThread();
        _world=_life=_save=0;
        return now;
    }
    internal static void EndUpdate(long started)
    {if(started!=0) _previousMod=(Stopwatch.GetTimestamp()-started)*1000d/Stopwatch.Frequency;}
    internal static void Section(string name,double ms)
    {
        if(!Enabled) return;
        if(name=="world") _world=ms;
        if(name=="lifecycle") _life=ms;
        if(name=="persistence") _save=ms;
        if(_phase.Length==0 || Time.unscaledTime<_sampleAt) return;
        if(!Sections.TryGetValue(name,out var values)) Sections[name]=values=new(32768);
        values.Add(ms);
    }
    internal static void Begin(string phase,string workload="frozen (idle sync isolation)")
    {
        _phase=phase;_workload=workload;Frames.Clear();Sections.Clear();_last=0;_samplingStarted=false;
        _sampleAt=Time.unscaledTime+8;_endAt=_sampleAt+25;
        _gen0=GC.CollectionCount(0);_gen1=GC.CollectionCount(1);_gen2=GC.CollectionCount(2);
        QualitySettings.vSyncCount=0;Application.targetFrameRate=120;Application.runInBackground=true;
    }
    internal static bool Complete=>_phase.Length>0 && Time.unscaledTime>=_endAt;
    private static double Percentile(double[] sorted,double q)=>sorted[Math.Min(sorted.Length-1,(int)Math.Ceiling(sorted.Length*q)-1)];
    internal static void Export(string directory,string role)
    {
        if(Frames.Count<50) throw new InvalidOperationException("Too few benchmark frames.");
        var phase=_phase;_phase="";
        var frames=Frames.Select(f=>f.Frame).OrderBy(v=>v).ToArray();
        var rows=new List<string>{"metric\tvalue",$"phase\t{phase}",$"role\t{role}",$"build\t{Plugin.BuildId[..16]}",$"level\t{LevelManager.CurrentLevel.name}","resolution\t1280x720","fps-cap\t120",$"npc-ai\t{_workload}",
            $"frames\t{frames.Length}",$"mean-fps\t{1000/frames.Average():F3}",$"frame-median-ms\t{Percentile(frames,.5):F3}",$"frame-p95-ms\t{Percentile(frames,.95):F3}",$"frame-p99-ms\t{Percentile(frames,.99):F3}",$"frame-max-ms\t{frames[^1]:F3}",
            $"over-50-ms\t{frames.Count(v=>v>50)}",$"over-100-ms\t{frames.Count(v=>v>100)}",$"over-250-ms\t{frames.Count(v=>v>250)}",$"gen0\t{GC.CollectionCount(0)-_gen0}",$"gen1\t{GC.CollectionCount(1)-_gen1}",$"gen2\t{GC.CollectionCount(2)-_gen2}"};
        var traffic=ProtocolTraffic.Read();var seconds=(Stopwatch.GetTimestamp()-_trafficAt)/(double)Stopwatch.Frequency;
        rows.Add(FormattableString.Invariant($"tx-kib-per-second\t{(traffic.SentBytes-_traffic.SentBytes)/1024d/seconds:F3}"));
        rows.Add(FormattableString.Invariant($"rx-kib-per-second\t{(traffic.ReceivedBytes-_traffic.ReceivedBytes)/1024d/seconds:F3}"));
        rows.Add(FormattableString.Invariant($"tx-frames-per-second\t{(traffic.SentFrames-_traffic.SentFrames)/seconds:F3}"));
        rows.Add(FormattableString.Invariant($"rx-frames-per-second\t{(traffic.ReceivedFrames-_traffic.ReceivedFrames)/seconds:F3}"));
        rows.Add(FormattableString.Invariant($"main-thread-allocated-mib-per-second\t{Frames.Sum(f=>(double)f.Allocated)/1048576d/seconds:F3}"));
        File.WriteAllLines(Path.Combine(directory,role+"-"+phase+"-summary.tsv"),rows);
        var details=new List<string>{"section\tcount\tmedian-ms\tp95-ms\tp99-ms\tmax-ms"};
        Sections["mod-update"]=Frames.Select(f=>f.Mod).ToList();
        foreach(var pair in Sections.OrderBy(p=>p.Key))
        {var s=pair.Value.OrderBy(v=>v).ToArray();if(s.Length>0)details.Add($"{pair.Key}\t{s.Length}\t{Percentile(s,.5):F3}\t{Percentile(s,.95):F3}\t{Percentile(s,.99):F3}\t{s[^1]:F3}");}
        File.WriteAllLines(Path.Combine(directory,role+"-"+phase+"-sections.tsv"),details);
        File.WriteAllLines(Path.Combine(directory,role+"-"+phase+"-frames.tsv"),new[]{"frame-ms\tprevious-mod-ms\tprevious-world-ms\tprevious-lifecycle-ms\tprevious-persistence-ms\tmanaged-thread-bytes"}.Concat(Frames.Select(f=>FormattableString.Invariant($"{f.Frame:F3}\t{f.Mod:F3}\t{f.World:F3}\t{f.Lifecycle:F3}\t{f.Persistence:F3}\t{f.Allocated}"))));
        Plugin.Logger.LogInfo($"PERF BASELINE: {role} {phase}: {1000/frames.Average():F1} FPS, p99 {Percentile(frames,.99):F2} ms, max {frames[^1]:F2} ms");
    }
}

public sealed partial class MultiplayerBehaviour
{
    private int _baselineStep;
    private bool _baselineReady,_baselineContinued;
    private Il2CppSystem.Action? _baselineHandler;
    private float _baselineDeadline;
    private void CheckSurfaceMembership()
    {
        var catalogue=_world!.CatalogueForTest;
        var surfaces=Object.FindObjectsOfType<Hyperstrange.Base.LocalRotation>(true).Select(r=>r.transform)
            .Concat(catalogue.EncounterMotion.Values).DistinctBy(t=>t.Pointer).ToArray();
        var scripts=catalogue.Components.Where(p=>p.Value!=null && p.Value.GetIl2CppType().Name is "FakeCitizen" or "FakePostalDude").Select(p=>(p.Key,p.Value.Pointer));
        TravelCheck(scripts.SequenceEqual(catalogue.ScriptedActors.Select(p=>(p.Key,p.Value.Pointer))),"cached scripted actors match the authored catalogue");
        var motions=catalogue.DoorMotion.Concat(catalogue.EncounterMotion).GroupBy(p=>p.Key).Select(g=>g.First()).Select(p=>(p.Key,p.Value.Pointer));
        TravelCheck(motions.SequenceEqual(catalogue.AuthoredMotion.Select(p=>(p.Key,p.Value.Pointer))),"cached authored motion preserves identity, order and door precedence");
        TravelCheck(surfaces.All(t=>catalogue.UrineSurfacePointers.Contains(t.Pointer)==catalogue.UrineSurfaces.ContainsKey(WorldCatalogue.TransformKey(t))),
            $"cached authored surface membership matches hierarchy identity for {surfaces.Length} native transforms");
    }
    private void RunPerformanceBaseline()
    {
        if(_baselineStep==99) return;
        if(_baselineStep==0)
        {
            if(!_buttonVisibleLogged) return;
            SharedWorldPatches.DiagnosticFreeze=true;
            if((Environment.GetEnvironmentVariable("PBDMP_PERF_TRACE")=="1" || Environment.GetEnvironmentVariable("PBDMP_PERF_CONNECTED_ONLY")=="1") && TravelRole=="client")
            {
                _port!.text=Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!;_address!.text="127.0.0.1";
                Join();SetPanel(false);_baselineStep=6;_baselineDeadline=Time.unscaledTime+180;return;
            }
            _baselineHandler=(Il2CppSystem.Action)(Action)(()=>_baselineReady=true);
            LevelController.OnStartLevelReady=Il2CppSystem.Delegate.Combine(LevelController.OnStartLevelReady,_baselineHandler).Cast<Il2CppSystem.Action>();
            _port!.text=Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!;
            GameManager.LoadLevel(TestLevel(),true);_baselineStep=1;_baselineDeadline=Time.unscaledTime+240;return;
        }
        if(Time.unscaledTime>_baselineDeadline) throw new TimeoutException("Performance baseline timed out: "+_baselineStep);
        if(_lifecycle is {NativeReady:false}) _lifecycle.AutoContinue();
        if(_baselineStep==1 || TravelRole=="client" && _baselineStep==6)
        {
            var loading=Object.FindObjectOfType<LoadingMenu>();
            if(!_baselineContinued && loading!=null && loading.gameObject.activeInHierarchy && loading._sceneLoaded)
            {_baselineContinued=true;loading.OnAnyButtonDown();}
            if(DoorSoloGate && LevelController._instance!=null) {DoorSoloGate=false;LevelController._instance.OnAnyButtonDown();}
            if(Player.Instance?.GetComponent<Hyperstrange.PBD.PlayerComponents.PlayerTutorialComponent>() is {_isTutorialShown:true} tutorial) tutorial.OnUseDown();
        }
        if(_baselineStep==1)
        {
            if(!_baselineReady || Time.timeScale==0) return;
            LevelController.OnStartLevelReady=Il2CppSystem.Delegate.Remove(LevelController.OnStartLevelReady,_baselineHandler)?.Cast<Il2CppSystem.Action>();
            if(TravelRole=="host" && (Environment.GetEnvironmentVariable("PBDMP_PERF_TRACE")=="1" || Environment.GetEnvironmentVariable("PBDMP_PERF_CONNECTED_ONLY")=="1"))
            {Host();SetPanel(false);TravelMark("host-ready");_baselineStep=6;}
            else if(TravelRole=="host") {PerformanceBaseline.Begin("single-offline");_baselineStep=2;}
            else {TravelMark("client-offline-ready");_baselineStep=3;}
            return;
        }
        if(TravelRole=="host") switch(_baselineStep)
        {
            case 2 when PerformanceBaseline.Complete:
                PerformanceBaseline.Export(TravelOutput,TravelRole);TravelMark("host-ready");_baselineStep=3;break;
            case 3 when Marked("client-offline-ready"):
                PerformanceBaseline.Begin("dual-offline");TravelMark("dual-sample");_baselineStep=4;break;
            case 4 when PerformanceBaseline.Complete:
                PerformanceBaseline.Export(TravelOutput,TravelRole);Host();SetPanel(false);
                PerformanceBaseline.Begin("host-no-peer");_baselineStep=5;break;
            case 5 when PerformanceBaseline.Complete:
                PerformanceBaseline.Export(TravelOutput,TravelRole);TravelMark("host-listening");_baselineStep=6;break;
            case 6 when _session?.Snapshot.Connected==true && _world?.ReadyForPerformance==true && Marked("client-connected-ready"):
                CheckSurfaceMembership();CheckJoinGameplayInput("P1 baseline released");PerformanceBaseline.Begin("connected");TravelMark("connected-sample");_baselineStep=7;break;
            case 7 when PerformanceBaseline.Complete:
                PerformanceBaseline.Export(TravelOutput,TravelRole);_baselineStep=8;break;
            case 8 when Marked("client-connected-done"):
                TravelMark("host-pass");_baselineStep=99;break;
        }
        else switch(_baselineStep)
        {
            case 3 when Marked("dual-sample"):
                PerformanceBaseline.Begin("dual-offline");_baselineStep=4;break;
            case 4 when PerformanceBaseline.Complete:
                PerformanceBaseline.Export(TravelOutput,TravelRole);_baselineStep=5;break;
            case 5 when Marked("host-listening"):
                GameManager.OpenTitle();_baselineStep=50;break;
            case 50 when !SessionManager.SessionActive && _view!.News.Visible:
                _address!.text="127.0.0.1";Join();SetPanel(false);_baselineStep=6;break;
            case 6 when _lifecycle?.NativeReady==true && _lifecycle.WorldReady && _lifecycle.State.Released && !_lifecycle.Frozen:
                CheckSurfaceMembership();CheckJoinGameplayInput("P2 baseline released");TravelMark("client-connected-ready");_baselineStep=7;break;
            case 7 when Marked("connected-sample"):
                PerformanceBaseline.Begin("connected");_baselineStep=8;break;
            case 8 when PerformanceBaseline.Complete:
                PerformanceBaseline.Export(TravelOutput,TravelRole);TravelMark("client-connected-done");TravelMark("client-pass");_baselineStep=99;break;
        }
    }
}
