using System.Net;
using System.Net.Sockets;
using PBDMP.Network;

Environment.SetEnvironmentVariable("PBDMP_PERF_BASELINE","1");
var passed = 0;
await Test("player presentation snapshots roundtrip bounded actions without changing combat",()=>
{
    var animation=new PlayerAnimation(PlayerMotion.Sliding,PlayerActions.Melee|PlayerActions.Loop|PlayerActions.Alternate,42,.75f,.5f);
    var pose=new PlayerPose("level",true,1,2,3,90,1.8f,Animation:animation);
    Check(Protocol.Decode(Protocol.Encode(new(MessageKind.Pose,Pose:pose))).Pose==pose);
    foreach(var invalid in new[]{animation with {Motion=(PlayerMotion)3},animation with {Actions=(PlayerActions)1024},animation with {Phase=float.NaN},animation with {Phase=1.01f},animation with {Charge=float.PositiveInfinity},animation with {Charge=-1}})
        Throws(()=>Protocol.Encode(new(MessageKind.Pose,Pose:pose with {Animation=invalid})));
    Check(Protocol.Decode(Protocol.Encode(new(MessageKind.Pose,Pose:pose with {Animation=default}))).Pose!.Animation==default);
    return Task.CompletedTask;
});

await Test("Survival state frames are bounded, exact and reject stale or malformed runs",()=>
{
    var state=new SurvivalState(3,SurvivalPhase.Combat,12,11,41.5f,2,false,123456789012.25,4.5f,987,37,5_000_000.5);
    var frame=SurvivalProtocol.ToFrame(state,7);
    Check(frame.Kind==WorldKind.Survival && frame.Epoch==7 && SurvivalProtocol.FromFrame(frame)==state);
    // Scores beyond the generic world float bound survive exactly.
    Check(SurvivalProtocol.FromFrame(frame).Score==123456789012.25);
    var decoded=Protocol.Decode(Protocol.Encode(new(MessageKind.World,World:frame with {Sequence=9}))).World!;
    Check(SurvivalProtocol.FromFrame(decoded)==state);
    foreach(var invalid in new[]{state with {Phase=(SurvivalPhase)4},state with {Wave=0},state with {WavesSurvived=13},state with {TimeLeft=float.NaN},state with {TimeLeft=-61},
        state with {Countdown=11},state with {Score=-1},state with {Score=double.PositiveInfinity},state with {Score=2e15},state with {Combo=0.5f},state with {Combo=6},
        state with {Kills=-1},state with {Alive=5000},state with {Damage=double.NaN}})
        Throws(()=>SurvivalProtocol.ToFrame(invalid,7));
    foreach(var bad in new[]{frame with {Flags=4},frame with {Flags=64},frame with {Data=frame.Data![..6]},frame with {Key="x"},frame with {Actor=1},frame with {Values=new[]{1f}},
        frame with {Data=frame.Data.Select((t,i)=>i==2?"012":t).ToArray()},frame with {Data=frame.Data.Select((t,i)=>i==0?"NaN":t).ToArray()},frame with {Data=frame.Data.Select((t,i)=>i==5?"1e3":t).ToArray()},
        frame with {Kind=WorldKind.MissionState}})
        Throws(()=>SurvivalProtocol.FromFrame(bad));
    Check(SurvivalProtocol.Supersedes(null,state) && SurvivalProtocol.Supersedes(state,state with {Run=4}) && !SurvivalProtocol.Supersedes(state,state with {Run=2}));
    Check(!WorldProtocol.FromClient(WorldKind.Survival) && !WorldProtocol.Durable(frame));
    return Task.CompletedTask;
});

await Test("shared item and team inventory frames are bounded, exact and direction-checked",()=>
{
    var collect=new SharedItemCollect("a:0123456789abcdef",2,true,true);
    var frame=SharedItemsProtocol.ToFrame(collect,5);
    Check(frame.Kind==WorldKind.SharedItem && SharedItemsProtocol.CollectFromFrame(frame)==collect);
    Check(SharedItemsProtocol.CollectFromFrame(Protocol.Decode(Protocol.Encode(new(MessageKind.World,World:frame with {Sequence=3}))).World!)==collect);
    foreach(var bad in new[]{collect with {Pickup=""},collect with {Pickup="x:1"},collect with {Pickup="a:"+new string('f',200)},collect with {Picker=0},collect with {Picker=3}})
        Throws(()=>SharedItemsProtocol.ToFrame(bad,5));
    foreach(var bad in new[]{frame with {Flags=4},frame with {Actor=0},frame with {Asset="x"},frame with {Data=new[]{"x"}},frame with {Kind=WorldKind.Loot}})
        Throws(()=>SharedItemsProtocol.CollectFromFrame(bad));
    var request=new SharedItemRequest("d:42",7,new(1,2,3));
    var requestFrame=SharedItemsProtocol.ToFrame(request,5);
    Check(SharedItemsProtocol.RequestFromFrame(requestFrame)==request && WorldProtocol.FromClient(WorldKind.SharedItemRequest));
    Check(!WorldProtocol.FromClient(WorldKind.SharedItem) && !WorldProtocol.FromClient(WorldKind.TeamInventory));
    foreach(var bad in new[]{requestFrame with {Actor=1},requestFrame with {Flags=1},requestFrame with {Key="a:"}})
        Throws(()=>SharedItemsProtocol.RequestFromFrame(bad));
    // A full base-game team inventory packs into one bounded frame and decodes exactly.
    var items=Enumerable.Range(0,SharedItemsProtocol.MaxItems).Select(i=>new TeamItem($"{i:x32}",i%3==0?1:i)).ToArray();
    var team=new TeamInventory(9,items);
    var teamFrame=SharedItemsProtocol.ToFrame(team,5);
    var decoded=SharedItemsProtocol.TeamFromFrame(Protocol.Decode(Protocol.Encode(new(MessageKind.World,World:teamFrame with {Sequence=4}))).World!);
    Check(decoded.Version==9 && decoded.Items.OrderBy(i=>i.Id,StringComparer.Ordinal).SequenceEqual(items.OrderBy(i=>i.Id,StringComparer.Ordinal)));
    Check(SharedItemsProtocol.TeamFromFrame(SharedItemsProtocol.ToFrame(new TeamInventory(1,Array.Empty<TeamItem>()),5)).Items.Count==0);
    foreach(var bad in new[]{new TeamInventory(1,new[]{new TeamItem("a",0)}),new TeamInventory(1,new[]{new TeamItem("a",1001)}),new TeamInventory(1,new[]{new TeamItem("a=b",1)}),
        new TeamInventory(1,new[]{new TeamItem("a",1),new TeamItem("a",2)}),new TeamInventory(1,Enumerable.Range(0,129).Select(i=>new TeamItem("i"+i,1)).ToArray())})
        Throws(()=>SharedItemsProtocol.ToFrame(bad,5));
    foreach(var bad in new[]{teamFrame with {Data=new[]{"abc"}},teamFrame with {Data=new[]{"a=-1"}},teamFrame with {Data=new[]{"a=1,a=2"}},teamFrame with {Key="x"},teamFrame with {Flags=1}})
        Throws(()=>SharedItemsProtocol.TeamFromFrame(bad));
    Check(!WorldProtocol.Durable(frame) && !WorldProtocol.Durable(teamFrame));
    return Task.CompletedTask;
});

await Test("Survival long-run extremes stay exact and inside the frame and record limits",()=>
{
    // The largest legal values of a very long run: score/damage, waves, kills, enemies, timer, offers and result fields.
    var offers=string.Join(",",Enumerable.Range(0,SurvivalProtocol.MaxOffers).Select(i=>$"{i}:W{SurvivalProtocol.MaxPrefab}"));
    var max=new SurvivalState(uint.MaxValue,SurvivalPhase.Rest,SurvivalProtocol.MaxWave,SurvivalProtocol.MaxWave,SurvivalProtocol.MaxTime,0,true,SurvivalProtocol.MaxScore,
        SurvivalProtocol.MaxCombo,SurvivalProtocol.MaxKills,SurvivalProtocol.MaxAlive,SurvivalProtocol.MaxScore,Ready:3,Required:3,Revive:3,Offers:offers,RunId:new string('f',32));
    var ended=max with {Phase=SurvivalPhase.Combat,Offers="",Ended=true,ResultTime=SurvivalProtocol.MaxResultTime,ResultWeapon=new string('w',SurvivalProtocol.MaxAssetId)};
    foreach(var state in new[]{max,ended,max with {Score=987654321024,Damage=0.1,TimeLeft=7200.25f}})
    {
        var bytes=Protocol.Encode(new(MessageKind.World,World:SurvivalProtocol.ToFrame(state,uint.MaxValue) with {Sequence=ulong.MaxValue}));
        Check(bytes.Length<Protocol.MaxFrameBytes/4 && SurvivalProtocol.FromFrame(Protocol.Decode(bytes).World!)==state);
    }
    var dir=Path.Combine(Path.GetTempPath(),"pbdmp-survival-extremes-"+Guid.NewGuid().ToString("N"));
    try
    {
        var store=new SurvivalRecordStore(Path.Combine(dir,"survival-records.json"));
        for(var i=0;i<SurvivalRecordStore.MaxBest;i++)
            store.Record(new(i.ToString("x32"),new string('a',SurvivalProtocol.MaxAssetId-2)+i.ToString("x2"),15,SurvivalProtocol.MaxScore,SurvivalProtocol.MaxKills,SurvivalProtocol.MaxWave,
                SurvivalProtocol.MaxScore,new string('w',SurvivalProtocol.MaxAssetId),SurvivalProtocol.MaxResultTime,DateTime.UnixEpoch.AddYears(500)));
        var file=store.Load();
        Check(file.Best.Length==SurvivalRecordStore.MaxBest && file.Recent.Length==SurvivalRecordStore.MaxRecent && file.Best.All(r=>r.Score==SurvivalProtocol.MaxScore));
        Check(new FileInfo(Path.Combine(dir,"survival-records.json")).Length<SurvivalRecordStore.MaxBytes);
        Throws(()=>store.Record(new(new string('e',32),"overflow",1,1,1,1,1,"",1,DateTime.UnixEpoch)));
    }
    finally { if(Directory.Exists(dir)) Directory.Delete(dir,true); }
    return Task.CompletedTask;
});

await Test("Survival preparation state and P2 requests carry bounded offers, readiness and revival",()=>
{
    var rest=new SurvivalState(5,SurvivalPhase.Rest,3,2,33,0,false,900,2,40,0,1200,Ready:1,Required:3,Revive:2,Offers:"0:W3,1:I8,2:W0");
    var frame=SurvivalProtocol.ToFrame(rest,4);
    Check(SurvivalProtocol.FromFrame(frame)==rest && SurvivalProtocol.ParseOffers(rest.Offers).SequenceEqual(new SurvivalOffer[]{new(0,"W3"),new(1,"I8"),new(2,"W0")}));
    Check(SurvivalProtocol.FromFrame(SurvivalProtocol.ToFrame(rest with {Phase=SurvivalPhase.Combat,Offers="",Ended=true},4)).Ended);
    Check(SurvivalProtocol.FormatOffers(new SurvivalOffer[]{new(2,"I10"),new(0,"W4")})=="2:I10,0:W4");
    foreach(var invalid in new[]{rest with {Ready=4},rest with {Required=7},rest with {Revive=255},rest with {Offers="0:W3,0:I1"},rest with {Offers="0:X3"},rest with {Offers="16:W1"},
        rest with {Offers="0:W64"},rest with {Offers="0:W01"},rest with {Offers=string.Join(",",Enumerable.Range(0,9).Select(i=>i+":I1"))},rest with {Phase=SurvivalPhase.Combat}})
        Throws(()=>SurvivalProtocol.ToFrame(invalid,4));
    Throws(()=>SurvivalProtocol.FromFrame(frame with {Data=frame.Data!.Select((t,i)=>i==7?"9":t).ToArray()}));
    var ready=new SurvivalRequest(5,3,SurvivalPhase.Rest,SurvivalRequestKind.Ready);
    var offer=ready with {Kind=SurvivalRequestKind.Offer,Offer="W3"};
    var intro=new SurvivalRequest(0,1,SurvivalPhase.Intro,SurvivalRequestKind.Ready);
    foreach(var request in new[]{ready,offer,intro})
    {
        var wire=SurvivalProtocol.ToFrame(request,4);
        Check(WorldProtocol.FromClient(wire.Kind) && SurvivalProtocol.RequestFromFrame(Protocol.Decode(Protocol.Encode(new(MessageKind.World,World:wire with {Sequence=2}))).World!)==request);
    }
    foreach(var invalid in new[]{ready with {Phase=SurvivalPhase.Combat},ready with {Wave=0},ready with {Offer="W1"},offer with {Offer=""},offer with {Offer="Z1"},
        offer with {Phase=SurvivalPhase.Intro},ready with {Kind=(SurvivalRequestKind)4}})
        Throws(()=>SurvivalProtocol.ToFrame(invalid,4));
    var damage=new SurvivalRequest(5,3,SurvivalPhase.Combat,SurvivalRequestKind.Damage);
    Check(SurvivalProtocol.RequestFromFrame(SurvivalProtocol.ToFrame(damage,4))==damage);
    Throws(()=>SurvivalProtocol.ToFrame(damage with {Offer="W1"},4));
    var ended=rest with {Phase=SurvivalPhase.Combat,Offers="",Ended=true,RunId="0123456789abcdef0123456789abcdef",ResultTime=754,ResultWeapon="5f2c3e0a9b7d4e1f8a6c2b9d0e3f4a51"};
    Check(SurvivalProtocol.FromFrame(SurvivalProtocol.ToFrame(ended,4))==ended);
    foreach(var invalid in new[]{ended with {RunId="ABC"},ended with {RunId=new string('g',32)},ended with {ResultTime=-1},ended with {ResultWeapon="a,b"},
        ended with {ResultWeapon=new string('a',65)},ended with {Ended=false},rest with {ResultTime=1}})
        Throws(()=>SurvivalProtocol.ToFrame(invalid,4));
    var good=SurvivalProtocol.ToFrame(offer,4);
    foreach(var bad in new[]{good with {Actor=1},good with {Data=Array.Empty<string>()},good with {Flags=good.Flags|64},good with {Key="k"},
        SurvivalProtocol.ToFrame(ready,4) with {Data=new[]{"W1"}}})
        Throws(()=>SurvivalProtocol.RequestFromFrame(bad));
    return Task.CompletedTask;
});

await Test("local co-op Survival records keep per-arena bests, bounded recent results and recover from corruption",()=>
{
    var dir=Path.Combine(Path.GetTempPath(),"pbdmp-survival-records-"+Guid.NewGuid().ToString("N"));
    try
    {
        var path=Path.Combine(dir,"survival-records.json");
        var store=new SurvivalRecordStore(path);
        Check(store.Load().Best.Length==0 && store.Best("arena",1)==null);
        SurvivalResult Run(int i,double score,string arena="arena",int difficulty=1)=>new(i.ToString("x32"),arena,difficulty,score,i,i,score*2,"weapon",60+i,DateTime.UnixEpoch.AddDays(i));
        var first=store.Record(Run(1,500));
        Check(first.Added && first.NewBest && first.PreviousBest==null);
        var lower=store.Record(Run(2,300));
        Check(lower.Added && !lower.NewBest && lower.PreviousBest!.Score==500);
        Check(!store.Record(Run(2,300)).Added && store.Load().Recent.Length==2); // once per run
        var higher=store.Record(Run(3,800));
        Check(higher.NewBest && higher.PreviousBest!.Score==500 && store.Best("arena",1)!.Score==800);
        store.Record(Run(4,100,"other",1));store.Record(Run(5,50,"arena",2));
        var reloaded=new SurvivalRecordStore(path).Load();
        Check(reloaded.Best.Length==3 && reloaded.Best.Single(r=>r.Arena=="arena" && r.Difficulty==1).RunId==3.ToString("x32"));
        for(var i=10;i<40;i++) store.Record(Run(i,10));
        Check(store.Load().Recent.Length==SurvivalRecordStore.MaxRecent && store.Load().Recent[0].RunId==39.ToString("x32"));
        Throws(()=>store.Record(Run(41,-1)));Throws(()=>store.Record(Run(42,1) with {RunId=""}));Throws(()=>store.Record(Run(43,1) with {Arena=""}));
        File.WriteAllText(path,"{ not json");
        var recovered=new SurvivalRecordStore(path);
        Check(recovered.Load().Best.Length==0 && recovered.RecoveredCorruption && File.Exists(path+".corrupt") && !File.Exists(path));
        Check(recovered.Record(Run(50,5)).NewBest && new SurvivalRecordStore(path).Load().Recent.Length==1);
        Check(Directory.GetFiles(dir).All(f=>!f.Contains(".writing-")));
    }
    finally { if(Directory.Exists(dir)) Directory.Delete(dir,true); }
    return Task.CompletedTask;
});

await Test("admission reuses only an unfinished identical map load across epochs",()=>
{
    var load=new TravelState(2,TravelAction.Load,"prison",0,false,false,false,false);
    var admission=load with {Epoch=3,Joining=true};
    Check(ClientTravelPolicy.ReusePendingLoad(load,admission,false));
    Check(!ClientTravelPolicy.ReusePendingLoad(load,admission,true));
    Check(!ClientTravelPolicy.ReusePendingLoad(null,admission,false));
    Check(!ClientTravelPolicy.ReusePendingLoad(load,admission with {Level="forest"},false));
    Check(!ClientTravelPolicy.ReusePendingLoad(load,admission with {Difficulty=1},false));
    Check(!ClientTravelPolicy.ReusePendingLoad(load,admission with {Action=TravelAction.Restart},false));
    Check(!ClientTravelPolicy.ReusePendingLoad(load,admission with {Joining=false},false));
    Check(!ClientTravelPolicy.ReusePendingLoad(load,admission with {Epoch=2},false));
    // Host release describes P1's prior solo state, not P2's unfinished load.
    Check(ClientTravelPolicy.ReusePendingLoad(load with {Released=true},admission,false));
    Check(!ClientTravelPolicy.ReusePendingLoad(load,admission with {StoryVideo="intro"},false));
    return Task.CompletedTask;
});

await Test("diagnostic traffic counts completed framed writes and reads, excluding failed reads",async()=>
{
    var before=ProtocolTraffic.Read();
    var packet=new Packet(MessageKind.Pose,Pose:PlayerPose.Empty);
    using var stream=new MemoryStream();
    await Protocol.WriteAsync(stream,packet,CancellationToken.None);
    var sent=ProtocolTraffic.Read();
    Check(sent.SentBytes-before.SentBytes==Protocol.Encode(packet).Length+4 && sent.SentFrames-before.SentFrames==1);
    stream.Position=0;
    Check((await Protocol.ReadAsync(stream,CancellationToken.None)).Kind==MessageKind.Pose);
    var read=ProtocolTraffic.Read();
    Check(read.ReceivedBytes-before.ReceivedBytes==stream.Length && read.ReceivedFrames-before.ReceivedFrames==1);
    try {await Protocol.ReadAsync(stream,CancellationToken.None);throw new InvalidOperationException("Expected EOF.");}
    catch(EndOfStreamException) { }
    Check(ProtocolTraffic.Read().ReceivedBytes==read.ReceivedBytes);
});
await Test("catalogue recovery reads only a fully compatible backup and preserves the primary", () =>
{
    var folder=Path.Combine(Path.GetTempPath(),"pbdmp-compat-"+Guid.NewGuid().ToString("N"));
    try
    {
        var store=new CoopSaveStore(folder);
        var save=new CoopSave {Game=new string('A',64)+":"+new string('B',64),Catalogue=new string('C',64),Slot="auto-1",Name="Compatible backup",Level="level",Mod="test",Host=new("direct:owner","level",new() {{"_saveData","{}"}},Array.Empty<string>()),Native=new() {{"world",new() {{"state","true"}}}}};
        store.Write(save);var file=Path.Combine(folder,"auto-1.pbdmp");
        // Reproduce an existing legacy recovery generation. The current writer
        // deliberately does not promote a different-catalogue primary over it.
        File.Copy(file,file+".bak");
        var catalogue=save.Catalogue;save.Catalogue=new string('D',64);save.Name="Incompatible primary";store.Write(save);
        var original=File.ReadAllBytes(file);
        var recovered=store.ReadCompatibleRecovery("auto-1",save.Game,catalogue,"level");
        Check(recovered.Recovered && recovered.Save.Name=="Compatible backup" && File.ReadAllBytes(file).SequenceEqual(original));
        Throws(()=>store.ReadCompatibleRecovery("auto-1",save.Game,new string('E',64),"level"));
        Throws(()=>store.ReadCompatibleRecovery("auto-1",new string('F',64),catalogue,"level"));
        Throws(()=>store.ReadCompatibleRecovery("auto-1",save.Game,catalogue,"other-level"));
        Throws(()=>store.ReadCompatibleRecovery("../solo",save.Game,catalogue,"level"));
        Check(store.Read("auto-1").Save.Name=="Incompatible primary" && File.ReadAllBytes(file).SequenceEqual(original));
        save.Catalogue=catalogue;save.Name="New compatible primary";store.Write(save);
        Check(store.Read("auto-1").Save.Name=="New compatible primary");
        Check(store.ReadCompatibleRecovery("auto-1",save.Game,catalogue,"level").Save.Name=="Compatible backup");
    }
    finally {if(Directory.Exists(folder)) Directory.Delete(folder,true);}
    return Task.CompletedTask;
});
await Test("host menu pause is atomic travel metadata and distinct from capture holds", () =>
{
    var state=new TravelState(4,TravelAction.Load,"level",0,true,true,true,false,HostMenuPaused:true);
    Check(Protocol.Decode(Protocol.Encode(new(MessageKind.Travel,Travel:state))).Travel==state);
    Check(!Protocol.Decode(Protocol.Encode(new(MessageKind.Travel,Travel:state with {HostMenuPaused=false}))).Travel!.HostMenuPaused);
    Throws(()=>Protocol.Validate(state with {Paused=false}));
    var waiting=new TravelState(5,TravelAction.Load,"arena",1,true,false,true,false,AdmissionWaiting:true);
    Check(Protocol.Decode(Protocol.Encode(new(MessageKind.Travel,Travel:waiting))).Travel==waiting);
    Check(Protocol.Decode(Protocol.Encode(new(MessageKind.Travel,Travel:waiting with {Joining=true}))).Travel!.AdmissionWaiting);
    Throws(()=>Protocol.Validate(new TravelState(5,TravelAction.Menu,"",1,true,false,true,false,AdmissionWaiting:true)));
    return Task.CompletedTask;
});
await Test("native tank progress accepts drain/fill endpoint overshoot but rejects corrupt values", () =>
{
    Check(NativeProgress.Unit(-0.005902154f)==0 && NativeProgress.Unit(1.005f)==1 && NativeProgress.Unit(.625f)==.625f);
    Throws(()=>NativeProgress.Unit(float.NaN));Throws(()=>NativeProgress.Unit(float.PositiveInfinity));Throws(()=>NativeProgress.Unit(1_000_001));
    return Task.CompletedTask;
});
await Test("autosave clock advances only during play, waits for its interval and never catches up with a write storm", () =>
{
    var clock=new AutosaveClock(300);
    clock.Advance(299,true);Check(!clock.Due && !clock.TryConsume());
    clock.Advance(3600,false);Check(clock.Remaining==1 && !clock.Due);
    clock.Advance(1,true);Check(clock.Due);clock.Advance(3600,true);
    Check(clock.TryConsume() && !clock.TryConsume() && clock.Remaining==300);
    clock.Advance(250,true);clock.Reset();Check(clock.Remaining==300);
    Throws(()=>clock.Advance(double.NaN,true));Throws(()=>clock.Advance(-1,true));Throws(()=>new AutosaveClock(0));
    return Task.CompletedTask;
});
await Test("player poses and personal saves carry one bounded authored platform reference", () =>
{
    var carrier=new PlayerCarrier(new string('A',64),new(.25f,1,-3));
    var pose=new PlayerPose("level",true,1,2,3,90,1.8f,Carrier:carrier);
    var bytes=Protocol.Encode(new(MessageKind.Pose,Pose:pose));
    Check(Protocol.Decode(bytes).Pose==pose);
    Check(Protocol.Decode(Protocol.Encode(new(MessageKind.Pose,Pose:pose with {Carrier=null}))).Pose!.Carrier==null);
    var player=new SavedPlayer("direct:owner","level",new() {{"_saveData","{}"}},Array.Empty<string>(),carrier);
    var json=System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(player);
    var restored=System.Text.Json.JsonSerializer.Deserialize<SavedPlayer>(json)!;
    CoopSaveStore.ValidatePlayer(restored);Check(restored.Carrier==carrier);
    foreach(var invalid in new[]{carrier with {Key="runtime-pointer"},carrier with {Offset=new(float.NaN,0,0)},carrier with {Offset=new(0,257,0)}})
    {Throws(()=>Protocol.Encode(new(MessageKind.Pose,Pose:pose with {Carrier=invalid})));Throws(()=>CoopSaveStore.ValidatePlayer(player with {Carrier=invalid}));}
    var bad=(byte[])bytes.Clone();bad[bad.Length-79-15]=2;Throws(()=>Protocol.Decode(bad));
    return Task.CompletedTask;
});
await Test("co-op save atomic replacement, corruption recovery and strict format isolation", () =>
{
    var folder=Path.Combine(Path.GetTempPath(),"pbdmp-save-test-"+Guid.NewGuid().ToString("N"));
    try
    {
        var store=new CoopSaveStore(folder);
        CoopSave Example()=>new() {Game=new string('A',64)+":"+new string('B',64),Catalogue=new string('C',64),Slot="manual-test",Name="First",Level="level",Mod="test",
            Host=new("direct:"+Guid.NewGuid().ToString("N"),"level",new() {{"_saveData","{}"}},new[]{"a:pickup-one"}),Native=new() {{"world",new() {{"state","true"}}}},
            World=new[]{new WorldFrame(WorldKind.MissionState,1,1,Key:"wall",Flags:1)}};
        var save=Example();store.Write(save);Check(store.Read(save.Slot).Save.Name=="First");
        save.Name="Second";store.Write(save);Check(store.Read(save.Slot).Save.Name=="Second" && !store.Read(save.Slot).Recovered);
        var file=Path.Combine(folder,save.Slot+".pbdmp");File.WriteAllText(file,"interrupted or corrupt");
        var recovered=store.Read(save.Slot);Check(recovered.Recovered && recovered.Save.Name=="First");
        save.Name="Third";store.Write(save);Check(store.Read(save.Slot).Save.Name=="Third");
        File.WriteAllText(file,"corrupt again");Check(store.Read(save.Slot).Save.Name=="First");
        Throws(()=>store.Read("../solo-profile"));
        save.Schema=99;Throws(()=>store.Write(save));Check(store.Read(save.Slot).Save.Name=="First");save.Schema=2;
        save.World=new[]{new WorldFrame(WorldKind.Fire,1,1)};Throws(()=>store.Write(save));
        File.WriteAllText(Path.Combine(folder,"manual-other.pbdmp.writing-abandoned"),"partial");
        Check(store.List().Length==1 && store.List()[0].Backup);
        save=Example();save.Host=save.Host with {Collected=new[]{"duplicate","duplicate"}};Throws(()=>store.Write(save));
        File.WriteAllText(file+".bak","also corrupt");Throws(()=>store.Read("manual-test"));Check(store.List()[0].Error!=null);
    }
    finally {if(Directory.Exists(folder)) {foreach(var file in Directory.EnumerateFiles(folder)) File.Delete(file);Directory.Delete(folder);}}
    return Task.CompletedTask;
});
await Test("shared story travel metadata is bounded and round trips without changing loadout ownership", () =>
{
    var story=new TravelState(9,TravelAction.Load,"level",2,false,false,false,false,StoryVideo:"VIDEO_Episode_1",StoryTime:12.5f,StoryWaiting:true);
    Check(Protocol.Decode(Protocol.Encode(new(MessageKind.Travel,Travel:story))).Travel==story);
    Throws(()=>Protocol.Validate(story with {StoryTime=float.NaN}));
    Throws(()=>Protocol.Validate(story with {StoryTime=-1}));
    Throws(()=>Protocol.Validate(story with {StoryVideo="",StoryWaiting=true}));
    Throws(()=>Protocol.Validate(story with {StoryVideo=new string('x',129)}));
    Throws(()=>Protocol.Validate(story with {StoryVideo="bad\nname"}));
    return Task.CompletedTask;
});
await Test("legacy co-op saves remain readable and untouched but cannot silently reset new world families", () =>
{
    var folder=Path.Combine(Path.GetTempPath(),"pbdmp-legacy-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
    try
    {
        var legacy=new CoopSave {Schema=1,Game=new string('A',64)+":"+new string('B',64),Catalogue=new string('C',64),Slot="legacy",Name="Original",
            Level="level",Host=new("direct:original","level",new() {{"_saveData","{}"}},Array.Empty<string>()),Native=new() {{"world",new() {{"state","true"}}}}};
        var payload=System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(legacy);
        var path=Path.Combine(folder,"legacy.pbdmp");
        using(var stream=File.Create(path)) using(var writer=new BinaryWriter(stream))
        {writer.Write(System.Text.Encoding.ASCII.GetBytes("PBDMPS01"));writer.Write(payload.Length);writer.Write(System.Security.Cryptography.SHA256.HashData(payload));writer.Write(payload);}
        var original=File.ReadAllBytes(path);var store=new CoopSaveStore(folder);var read=store.Read("legacy").Save;
        Check(read.Schema==1);Throws(()=>CoopSaveStore.RequireRestorableFormat(read));Throws(()=>store.Write(read));
        Check(File.ReadAllBytes(path).SequenceEqual(original) && !File.Exists(path+".bak"));
        read.Schema=2;CoopSaveStore.RequireRestorableFormat(read);
        read.World=new[]{new WorldFrame(WorldKind.Scenery,1,1,Key:"glass",Flags:1,Values:new[]{1f,0}),
            new WorldFrame(WorldKind.Mechanism,1,1,Key:"gate",Flags:4,Value:2.5f,Values:new[]{1f}),
            new WorldFrame(WorldKind.Sequence,1,1,Key:"train",Flags:3,Value:4,Value2:10)};
        store.Write(read);Check(store.Read("legacy").Save.World.Length==3);
        read.World=new[]{new WorldFrame(WorldKind.SceneryEvent,1,1,Key:"old-smash",Flags:1)};Throws(()=>store.Write(read));
    }
    finally {foreach(var file in Directory.EnumerateFiles(folder)) File.Delete(file);Directory.Delete(folder);}
    return Task.CompletedTask;
});
await Test("large current-world baselines survive paging, cancellation and strict bounds", () =>
{
    var data=new byte[9*1024*1024+17];new Random(17).NextBytes(data);
    using var baseline=new WorldBaseline();var pages=new SaveAssembler();uint transaction=1;
    byte[]? Transfer(SaveCommand command,byte[] bytes)
    {
        byte[]? result=null;
        foreach(var page in SaveProtocol.Split(command,7,transaction++,bytes))
        {
            var assembled=pages.Accept(Protocol.Decode(Protocol.Encode(new(MessageKind.Save,Save:page))).Save!);
            if(assembled!=null) result=baseline.Accept(command,assembled);
        }
        return result;
    }
    Transfer(SaveCommand.WorldRestoreBegin,WorldBaseline.Begin(data));
    for(var at=0;at<data.Length;at+=WorldBaseline.ChunkBytes)
        Transfer(SaveCommand.WorldRestore,data.AsSpan(at,Math.Min(WorldBaseline.ChunkBytes,data.Length-at)).ToArray());
    Check(Transfer(SaveCommand.WorldRestoreEnd,Array.Empty<byte>())!.SequenceEqual(data));
    Throws(()=>baseline.Accept(SaveCommand.WorldRestore,new byte[]{1}));
    Throws(()=>WorldBaseline.Begin(Array.Empty<byte>()));
    Throws(()=>WorldBaseline.Begin(new byte[CoopSaveStore.MaxBytes+1]));
    var small=new byte[]{1,2,3};var header=WorldBaseline.Begin(small);
    baseline.Accept(SaveCommand.WorldRestoreBegin,header);
    Throws(()=>baseline.Accept(SaveCommand.WorldRestoreBegin,header));
    Throws(()=>baseline.Accept(SaveCommand.WorldRestore,new byte[WorldBaseline.ChunkBytes+1]));
    Throws(()=>baseline.Accept(SaveCommand.WorldRestore,new byte[4]));
    baseline.Accept(SaveCommand.WorldRestore,new byte[]{1,2});
    Throws(()=>baseline.Accept(SaveCommand.WorldRestoreEnd,Array.Empty<byte>()));
    baseline.Reset();baseline.Accept(SaveCommand.WorldRestoreBegin,header);
    baseline.Accept(SaveCommand.WorldRestore,new byte[]{1,2,4});
    Throws(()=>baseline.Accept(SaveCommand.WorldRestoreEnd,Array.Empty<byte>()));
    baseline.Reset();baseline.Accept(SaveCommand.WorldRestoreBegin,header);
    baseline.Accept(SaveCommand.WorldRestore,small);
    Throws(()=>baseline.Accept(SaveCommand.WorldRestoreEnd,new byte[]{0}));
    Check(baseline.Accept(SaveCommand.WorldRestoreEnd,Array.Empty<byte>())!.SequenceEqual(small));
    var oversized=System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new {Bytes=int.MaxValue,Hash=new string('A',64)});
    Throws(()=>baseline.Accept(SaveCommand.WorldRestoreBegin,oversized));
    baseline.Accept(SaveCommand.WorldRestoreBegin,header);baseline.Reset();
    baseline.Accept(SaveCommand.WorldRestoreBegin,header);baseline.Accept(SaveCommand.WorldRestore,small);
    Check(baseline.Accept(SaveCommand.WorldRestoreEnd,Array.Empty<byte>())!.SequenceEqual(small));
    return Task.CompletedTask;
});

await Test("large world baselines retain order under TCP and message-carrier backpressure", async () =>
{
    foreach(var messages in new[]{false,true})
    {
        using var listener=new LoopbackListener(64);
        using var host=new CoopSession("baseline-bulk");using var client=new CoopSession("baseline-bulk");
        if(messages) {host.StartHost(listener,"Hosting");client.StartJoin(listener.ConnectAsync,"Connecting",5);}
        else {var port=FreePort();host.StartHost(port,IPAddress.Loopback);client.StartJoin(IPAddress.Loopback,port);}
        await Until(()=>host.Snapshot.Connected && client.Snapshot.Connected);
        var data=new byte[9*1024*1024+7];new Random(41).NextBytes(data);
        var outbound=new Queue<SavePage>();uint transaction=1;
        void Enqueue(SaveCommand command,byte[] bytes)
        {foreach(var page in SaveProtocol.Split(command,3,transaction++,bytes)) outbound.Enqueue(page);}
        Enqueue(SaveCommand.WorldRestoreBegin,WorldBaseline.Begin(data));
        for(var at=0;at<data.Length;at+=WorldBaseline.ChunkBytes)
            Enqueue(SaveCommand.WorldRestore,data.AsSpan(at,Math.Min(WorldBaseline.ChunkBytes,data.Length-at)).ToArray());
        Enqueue(SaveCommand.WorldRestoreEnd,Array.Empty<byte>());
        using var baseline=new WorldBaseline();var pages=new SaveAssembler();byte[]? received=null;
        var clock=System.Diagnostics.Stopwatch.StartNew();var blocked=false;
        while(received==null)
        {
            Check(clock.Elapsed.TotalSeconds<20 && host.Snapshot.Connected && client.Snapshot.Connected);
            while(outbound.Count>0)
            {if(!host.PublishSave(outbound.Peek())) {blocked=true;break;}outbound.Dequeue();}
            // A loading client does not immediately drain the main-thread queue.
            if(clock.Elapsed.TotalMilliseconds>=600) foreach(var page in client.DrainSaves())
            {var payload=pages.Accept(page);if(payload!=null) received=baseline.Accept(page.Command,payload);}
            await Task.Delay(1);
        }
        Check(blocked && outbound.Count==0 && received.SequenceEqual(data));
        host.DropPeer("baseline cancellation test");await Until(()=>!client.Snapshot.Connected && !host.Snapshot.Connected);
        Check(client.DrainSaves().Length==0 && host.DrainSaves().Length==0);
    }
});

await Test("bounded co-op save pages preserve identity/data and enforce ordering", () =>
{
    var data=Enumerable.Range(0,13000).Select(i=>(byte)i).ToArray();var pages=SaveProtocol.Split(SaveCommand.Restore,3,7,data);
    var assembler=new SaveAssembler();byte[]? result=null;
    foreach(var page in pages)
    {var encoded=Protocol.Encode(new(MessageKind.Save,Save:page));var decoded=Protocol.Decode(encoded).Save!;result=assembler.Accept(decoded);}
    Check(result!.SequenceEqual(data));
    Throws(()=>new SaveAssembler().Accept(pages[1]));
    assembler=new SaveAssembler();assembler.Accept(pages[0]);Throws(()=>assembler.Accept(pages[0]));
    Throws(()=>SaveProtocol.Validate(pages[0] with {Count=int.MaxValue}));
    Throws(()=>SaveProtocol.Validate(pages[0] with {Command=(SaveCommand)99}));
    Throws(()=>SaveProtocol.Validate(pages[0] with {Bytes=new byte[4097]}));
    return Task.CompletedTask;
});
await Test("save controls enforce sender authority over a real reliable session", async () =>
{
    var port=FreePort();using var host=new CoopSession("save");using var guest=new CoopSession("save");host.StartHost(port,IPAddress.Loopback);guest.StartJoin(IPAddress.Loopback,port);
    await Until(()=>host.Snapshot.Connected && guest.Snapshot.Connected);
    var identity=SaveProtocol.Split(SaveCommand.Identity,0,1,new byte[]{1,2,3})[0];guest.PublishSave(identity);identity.Bytes[0]=99;
    host.PublishSave(SaveProtocol.Split(SaveCommand.Capture,1,1,Array.Empty<byte>())[0]);
    Throws(()=>host.PublishSave(SaveProtocol.Split(SaveCommand.GuestState,1,2,new byte[]{1})[0]));
    Throws(()=>guest.PublishSave(SaveProtocol.Split(SaveCommand.Resume,1,2,Array.Empty<byte>())[0]));
    var toHost=new List<SavePage>();var toGuest=new List<SavePage>();
    await Until(()=>{toHost.AddRange(host.DrainSaves());toGuest.AddRange(guest.DrainSaves());return toHost.Count==1 && toGuest.Count==1;});
    Check(toHost[0].Bytes[0]==1 && toGuest[0].Command==SaveCommand.Capture);
});
await Test("world codec bounds, strict values and per-event preservation", () =>
{
    var frame = new WorldFrame(WorldKind.Projectile,2,17,21,3,"key","asset",2,4,1,
        new(1,2,3),new(0,90,0),new(0,0,80),4,7,new[]{1f,0.5f},new[]{"phase","2"});
    var bytes=Protocol.Encode(new Packet(MessageKind.World,World:frame));
    Check(bytes.SequenceEqual(Protocol.Encode(Protocol.Decode(bytes))));
    foreach(var kind in new[]{WorldKind.ActorState,WorldKind.ActorEffect,WorldKind.UrineState})
    {
        var actor=frame with{Kind=kind,Values=new[]{-32768f,65535f,32767f,65535f},Data=new[]{"actor-controller","death-context"}};
        var actorBytes=Protocol.Encode(new Packet(MessageKind.World,World:actor));
        Check(actorBytes.SequenceEqual(Protocol.Encode(Protocol.Decode(actorBytes))));
    }
    Throws(()=>Protocol.Encode(new Packet(MessageKind.World,World:frame with {Epoch=0})));
    Throws(()=>Protocol.Encode(new Packet(MessageKind.World,World:frame with {Kind=(WorldKind)31})));
    Throws(()=>Protocol.Encode(new Packet(MessageKind.World,World:frame with {Values=new[]{float.NaN}})));
    Throws(()=>Protocol.Encode(new Packet(MessageKind.World,World:frame with {Values=new float[33]})));
    Throws(()=>Protocol.Encode(new Packet(MessageKind.World,World:frame with {Data=new string[33]})));
    Throws(()=>Protocol.Encode(new Packet(MessageKind.World,World:frame with {Data=new[]{new string('界',100)}})));
    Throws(()=>Protocol.Encode(new Packet(MessageKind.World,World:frame with {Data=new string[]{null!}})));
    Throws(()=>Protocol.Encode(new Packet(MessageKind.World,World:frame with {Key=new string('界',100)})));
    return Task.CompletedTask;
});
await Test("reliable world events, role guards and array ownership over TCP", async () =>
{
    var port=FreePort(); using var host=new CoopSession("world"); using var client=new CoopSession("world");
    host.StartHost(port,IPAddress.Loopback); client.StartJoin(IPAddress.Loopback,port);
    await Until(()=>host.Snapshot.Connected && client.Snapshot.Connected);
    var values=new[]{2f};
    var data=new[]{"phase"};
    for (ulong i=1;i<=120;i++) host.PublishWorld(new(WorldKind.Impact,1,i,Values:values,Data:data));
    values[0]=99;
    data[0]="mutated";
    client.PublishWorld(new(WorldKind.Fire,1,1,Actor:2));
    Throws(()=>client.PublishWorld(new(WorldKind.Enemy,1,2)));
    Throws(()=>client.PublishWorld(new(WorldKind.ActorState,1,2)));
    Throws(()=>client.PublishWorld(new(WorldKind.ActorEffect,1,2)));
    Throws(()=>host.PublishWorld(new(WorldKind.Fire,1,121)));
    var received=new List<WorldFrame>(); var requests=new List<WorldFrame>();
    await Until(()=>{received.AddRange(client.DrainWorld()); requests.AddRange(host.DrainWorld()); return received.Count==120 && requests.Count==1;});
    Check(received.Select(f=>f.Sequence).SequenceEqual(Enumerable.Range(1,120).Select(i=>(ulong)i)));
    Check(received.All(f=>f.Values![0]==2 && f.Data![0]=="phase") && requests[0].Kind==WorldKind.Fire);
    client.Dispose(); await Until(()=>!host.Snapshot.Connected);
    Check(host.DrainWorld().Length==0);
});
await Test("large mission snapshots survive bounded transport backpressure", async () =>
{
    var port=FreePort();using var host=new CoopSession("bulk");using var client=new CoopSession("bulk");
    host.StartHost(port,IPAddress.Loopback);client.StartJoin(IPAddress.Loopback,port);
    await Until(()=>host.Snapshot.Connected && client.Snapshot.Connected);
    const int total=12288;ulong next=1;var received=new List<WorldFrame>();var clock=System.Diagnostics.Stopwatch.StartNew();var blocked=false;
    while(next<=total || received.Count<total)
    {
        Check(clock.Elapsed.TotalSeconds<10 && host.Snapshot.Connected && client.Snapshot.Connected);
        while(next<=total)
        {
            if(!host.PublishWorld(new(WorldKind.MissionState,1,next,Key:"mission",Flags:128,Values:new[]{(float)next}))) {blocked=true;break;}
            next++;
        }
        // Model a main thread busy building the scene before it can consume its snapshot.
        if(clock.Elapsed.TotalMilliseconds>=600) received.AddRange(client.DrainWorld(CoopSession.MaxWorldQueue));
        await Task.Delay(1);
    }
    Check(blocked && received.Select(f=>f.Sequence).SequenceEqual(Enumerable.Range(1,total).Select(i=>(ulong)i)));
    Check(received.All(f=>f.Values![0]==f.Sequence));
    Throws(()=>client.PublishWorld(new(WorldKind.MissionState,1,1)));
});
await Test("protocol round trip and fragmented TCP frame", async () =>
{
    var pose = new PlayerPose("CAMPAIGN:1:test", true, 12, 4, -20, 150, 1.8f, "weapon-guid", -30f, uint.MaxValue);
    var packet = new Packet(MessageKind.Pose, Pose: pose);
    Check(Protocol.Decode(Protocol.Encode(packet)) == packet);
    using var stream = new MemoryStream();
    await Protocol.WriteAsync(stream, packet, CancellationToken.None);
    using var fragments = new FragmentStream(stream.ToArray());
    Check(await Protocol.ReadAsync(fragments, CancellationToken.None) == packet);
});
await Test("malformed, oversized, trailing, NaN and truncated frames rejected", async () =>
{
    Throws(() => Protocol.Decode(new byte[] { 99 }));
    Throws(() => Protocol.Decode(Protocol.Encode(new Packet(MessageKind.Reject, Text: "ok")).Concat(new byte[] { 0 }).ToArray()));
    Throws(() => Protocol.Encode(new Packet(MessageKind.Reject, Text: new string('x', 257))));
    Throws(() => Protocol.Encode(new Packet(MessageKind.Pose, Pose: new("test", true, float.NaN, 0, 0, 0, 2))));
    Throws(() => Protocol.Encode(new Packet(MessageKind.Pose, Pose: new("", true, 0, 0, 0, 0, 2))));
    Throws(() => Protocol.Encode(new Packet(MessageKind.Pose, Pose: new("test", true, 0, 0, 0, 0, 2, Pitch: float.NaN))));
    Throws(() => Protocol.Encode(new Packet(MessageKind.Pose, Pose: new("test", true, 0, 0, 0, 0, 2, Pitch: 91))));
    Throws(() => Protocol.Encode(new Packet(MessageKind.Pose, Pose: new("test", true, 0, 0, 0, 0, 2, Weapon: new string('w',65)))));
    using var huge = new MemoryStream(BitConverter.GetBytes(Protocol.MaxFrameBytes + 1));
    await ThrowsAsync(() => Protocol.ReadAsync(huge, CancellationToken.None));
    using var truncated = new MemoryStream(new byte[] { 10, 0, 0, 0, 3 });
    await ThrowsAsync(() => Protocol.ReadAsync(truncated, CancellationToken.None));
});
await Test("travel generations, join loadouts, stale readiness and malformed state", () =>
{
    var host = new HostTravel();
    host.Begin(TravelAction.Load, "level-guid", 2, true);
    var first = host.State.Epoch;
    host.Acknowledge(new(first, true)); Check(!host.State.Released);
    var loadout = new JoinLoadout("weapon", "weapon", new[] { new InventoryEntry("weapon",1), new InventoryEntry("ammo",20) }, 1,2,3,90);
    host.SetReady(loadout); host.Acknowledge(new(first, false)); Check(!host.State.Released);
    host.Acknowledge(new(first, true)); Check(host.State.Released);
    var encoded = Protocol.Encode(new Packet(MessageKind.Travel, Travel: host.State));
    Check(encoded.SequenceEqual(Protocol.Encode(Protocol.Decode(encoded))));
    host.SetPaused(true); Check(host.State.Paused);
    host.Begin(TravelAction.Restart, "level-guid", 2); Check(host.State.Epoch != first && !host.State.HostReady);
    host.SetReady(); host.Acknowledge(new(first, true)); Check(!host.State.Released);
    host.PeerLeft(); Check(host.State.Released);
    Throws(() => Protocol.Encode(new Packet(MessageKind.Travel, Travel: host.State with { Epoch = 0 })));
    Throws(() => Protocol.Encode(new Packet(MessageKind.Travel, Travel: host.State with { Action = (TravelAction)99 })));
    Throws(() => Protocol.Encode(new Packet(MessageKind.Travel, Travel: host.State with { Difficulty = -1 })));
    Throws(() => Protocol.Encode(new Packet(MessageKind.Travel, Travel: host.State with { Joining = true, Loadout = loadout with { X = float.NaN } })));
    Throws(() => Protocol.Encode(new Packet(MessageKind.Travel, Travel: host.State with { Joining = true, Loadout = loadout with { Items = new[] { new InventoryEntry("x",1), new InventoryEntry("x",2) } } })));
    Throws(() => Protocol.Encode(new Packet(MessageKind.Travel, Travel: host.State with { Joining = true, Loadout = loadout with { Items = new[] { new InventoryEntry("x",-1) } } })));
    return Task.CompletedTask;
});
await Test("host-only travel, client-only ready and reconnect generations over TCP", async () =>
{
    var port = FreePort(); using var host = new CoopSession("travel"); using var client = new CoopSession("travel");
    host.StartHost(port, IPAddress.Loopback); client.StartJoin(IPAddress.Loopback, port);
    Throws(() => client.PublishTravel(new(1, TravelAction.Menu, "",0,true,false,true,false)));
    Throws(() => host.PublishReady(new(1,true)));
    var state = new TravelState(10,TravelAction.Load,"level",1,false,false,false,false);
    host.PublishTravel(state); client.PublishReady(new(10,false));
    await Until(() => client.Snapshot.Travel?.Epoch == 10 && host.Snapshot.Ready?.Ready == false);
    host.PublishTravel(state with { HostReady = true }); client.PublishReady(new(10,true));
    await Until(() => client.Snapshot.Travel?.HostReady == true && host.Snapshot.Ready?.Ready == true);
    host.PublishTravel(state with { Epoch = 11, Action = TravelAction.Restart });
    await Until(() => client.Snapshot.Travel?.Epoch == 11);
    client.Dispose(); await Until(() => !host.Snapshot.Connected && host.Snapshot.Ready == null);
    using var next = new CoopSession("travel"); next.StartJoin(IPAddress.Loopback,port);
    await Until(() => next.Snapshot.Travel?.Epoch == 11 && host.Snapshot.Ready == null);
});
await Test("wire authority violation releases slot; host loading-failure reason reaches client", async () =>
{
    var port = FreePort(); using var host = new CoopSession("authority"); host.StartHost(port,IPAddress.Loopback);
    using (var forged = new TcpClient())
    {
        await forged.ConnectAsync(IPAddress.Loopback,port);
        var stream = forged.GetStream();
        await Protocol.WriteAsync(stream,new(MessageKind.Hello,Text:"authority"),CancellationToken.None);
        Check((await Protocol.ReadAsync(stream,CancellationToken.None)).Kind == MessageKind.Welcome);
        await Until(()=>host.Snapshot.Connected);
        await Protocol.WriteAsync(stream,new(MessageKind.Travel,Travel:new(1,TravelAction.Menu,"",0,true,false,true,false)),CancellationToken.None);
        await Until(()=>!host.Snapshot.Connected);
        Check(host.Snapshot.Status.Contains("unauthorized"));
    }
    using var client = new CoopSession("authority"); client.StartJoin(IPAddress.Loopback,port);
    await Until(()=>client.Snapshot.Connected && host.Snapshot.Connected);
    host.DropPeer("Campaign loading timed out.");
    await Until(()=>!client.Snapshot.Connected && !host.Snapshot.Connected);
    Check(client.Snapshot.Status.Contains("Campaign loading timed out"));
});
await Test("invalid ports and duplicate start rejected", () =>
{
    using var session = new CoopSession("test");
    Throws(() => session.StartHost(0));
    session.StartHost(FreePort(), IPAddress.Loopback);
    Throws(() => session.StartJoin(IPAddress.Loopback, FreePort()));
    return Task.CompletedTask;
});
await Test("bidirectional poses, level change, full host, leave, rejoin, host stop", async () =>
{
    var port = FreePort();
    using var host = new CoopSession("test-game");
    using var client = new CoopSession("test-game");
    var p1 = new PlayerPose("level-1", true, 1, 2, 3, 90, 1.8f);
    var p2 = new PlayerPose("level-1", true, 4, 5, 6, 180, 1.3f);
    host.Publish(p1); client.Publish(p2);
    host.StartHost(port, IPAddress.Loopback);
    client.StartJoin(IPAddress.Loopback, port);
    await Until(() => host.Snapshot.RemotePose == p2 && client.Snapshot.RemotePose == p1);
    var firing = p2 with { Weapon = "shotgun", Pitch = -40f, ShotSequence = 42 };
    client.Publish(firing);
    await Until(() => host.Snapshot.RemotePose == firing);
    client.Publish(firing with { Weapon = "pistol", Pitch = 20f, ShotSequence = 43 });
    await Until(() => host.Snapshot.RemotePose?.Weapon == "pistol" && host.Snapshot.RemotePose.ShotSequence == 43);
    for (var attempt = 0; attempt < 5; attempt++)
    {
        using var extra = new CoopSession("test-game");
        extra.StartJoin(IPAddress.Loopback, port);
        try { await Until(() => extra.Snapshot.Status.Contains("full")); }
        catch { throw new Exception("Full-host rejection failed: " + extra.Snapshot.Status); }
        Check(host.Snapshot.Connected && client.Snapshot.Connected);
    }
    client.Publish(p2 with { Level = "level-2" });
    await Until(() => host.Snapshot.RemotePose?.Level == "level-2");
    client.Publish(PlayerPose.Empty);
    await Until(() => host.Snapshot.RemotePose?.Ready == false);
    client.Dispose();
    await Until(() => !host.Snapshot.Connected && host.Snapshot.RemotePose == null);
    using var rejoin = new CoopSession("test-game");
    rejoin.StartJoin(IPAddress.Loopback, port);
    await Until(() => rejoin.Snapshot.Connected && host.Snapshot.Connected);
    host.Dispose();
    await Until(() => !rejoin.Snapshot.Connected);
});
await Test("game and protocol version mismatches rejected", async () =>
{
    var port = FreePort();
    using var host = new CoopSession("game-a");
    host.StartHost(port, IPAddress.Loopback);
    using var wrongGame = new CoopSession("game-b");
    wrongGame.StartJoin(IPAddress.Loopback, port);
    await Until(() => wrongGame.Snapshot.Status.Contains("Game or PBDMP build mismatch"));
    using var wrongVersion = new CoopSession("game-a", 1); // Previous 0.1 wire format.
    wrongVersion.StartJoin(IPAddress.Loopback, port);
    await Until(() => wrongVersion.Snapshot.Status.Contains("protocol version mismatch"));
    Check(!host.Snapshot.Connected);
});
await Test("occupied host port and refused join report errors", async () =>
{
    var port = FreePort();
    using var host = new CoopSession("test");
    host.StartHost(port, IPAddress.Loopback);
    using var duplicate = new CoopSession("test");
    Throws(() => duplicate.StartHost(port, IPAddress.Loopback));
    using var client = new CoopSession("test");
    client.StartJoin(IPAddress.Loopback, FreePort());
    await Until(() => client.Snapshot.Status.StartsWith("Join failed:"));
});
await Test("malformed peer dropped and host admits a valid replacement", async () =>
{
    var port = FreePort();
    using var host = new CoopSession("test");
    host.StartHost(port, IPAddress.Loopback);
    using (var bad = new TcpClient())
    {
        await bad.ConnectAsync(IPAddress.Loopback, port);
        await Protocol.WriteAsync(bad.GetStream(), new(MessageKind.Hello, Text: "test"), CancellationToken.None);
        await Protocol.ReadAsync(bad.GetStream(), CancellationToken.None);
        await bad.GetStream().WriteAsync(new byte[] { 0, 0, 0, 0 });
        await Until(() => !host.Snapshot.Connected);
    }
    using var client = new CoopSession("test");
    client.StartJoin(IPAddress.Loopback, port);
    await Until(() => client.Snapshot.Connected && host.Snapshot.Connected);
});
await Test("silent peer times out and releases host slot", async () =>
{
    var port = FreePort();
    using var host = new CoopSession("test");
    host.StartHost(port, IPAddress.Loopback);
    using var silent = new TcpClient();
    await silent.ConnectAsync(IPAddress.Loopback, port);
    await Protocol.WriteAsync(silent.GetStream(), new(MessageKind.Hello, Text: "test"), CancellationToken.None);
    await Protocol.ReadAsync(silent.GetStream(), CancellationToken.None);
    await Until(() => host.Snapshot.Connected);
    await Until(() => !host.Snapshot.Connected && host.Snapshot.Status.Contains("timed out"), 13);
});
await Test("cancellation during handshake and idempotent disposal", async () =>
{
    var port = FreePort();
    using var host = new CoopSession("test");
    host.StartHost(port, IPAddress.Loopback);
    using var pending = new TcpClient();
    await pending.ConnectAsync(IPAddress.Loopback, port);
    host.Dispose(); host.Dispose();
    await Task.Delay(100);
    Check(host.Snapshot.Role == SessionRole.Offline && !host.Snapshot.Connected);
    Throws(() => host.StartHost(port));
});
await Test("handshake, poses, ordered world events, full host, stated drop reason and rejoin over a message carrier", async () =>
{
    using var listener = new LoopbackListener();
    using var host = new CoopSession("carrier"); using var client = new CoopSession("carrier");
    var p1 = new PlayerPose("level-1", true, 1, 2, 3, 90, 1.8f);
    var p2 = new PlayerPose("level-1", true, 4, 5, 6, 180, 1.3f);
    host.Publish(p1); client.Publish(p2);
    host.StartHost(listener, "Hosting carrier; waiting for P2");
    Check(host.Snapshot.Role == SessionRole.Host && host.Snapshot.Status == "Hosting carrier; waiting for P2");
    Throws(() => host.StartHost(new LoopbackListener(), "again"));
    client.StartJoin(listener.ConnectAsync, "Connecting through carrier", 5);
    await Until(() => host.Snapshot.RemotePose == p2 && client.Snapshot.RemotePose == p1);
    for (ulong i=1;i<=120;i++) host.PublishWorld(new(WorldKind.Impact,1,i,Values:new[]{2f}));
    client.PublishWorld(new(WorldKind.Fire,1,1,Actor:2));
    var received=new List<WorldFrame>(); var requests=new List<WorldFrame>();
    await Until(()=>{received.AddRange(client.DrainWorld()); requests.AddRange(host.DrainWorld()); return received.Count==120 && requests.Count==1;});
    Check(received.Select(f=>f.Sequence).SequenceEqual(Enumerable.Range(1,120).Select(i=>(ulong)i)));
    using (var extra = new CoopSession("carrier"))
    {
        extra.StartJoin(listener.ConnectAsync, "Connecting through carrier", 5);
        await Until(() => extra.Snapshot.Status.Contains("full"));
        Check(host.Snapshot.Connected && client.Snapshot.Connected);
    }
    for (var attempt = 0; attempt < 10; attempt++)
    {
        using var dropped = attempt == 0 ? client : new CoopSession("carrier");
        if (attempt > 0) dropped.StartJoin(listener.ConnectAsync, "Connecting through carrier", 5);
        await Until(() => dropped.Snapshot.Connected && host.Snapshot.Connected);
        host.DropPeer("Campaign loading timed out.");
        await Until(() => !dropped.Snapshot.Connected && !host.Snapshot.Connected);
        // The peer's own send can fail on the closed carrier before it reads the reason.
        if (!dropped.Snapshot.Status.Contains("Campaign loading timed out")) throw new Exception("Drop reason lost: " + dropped.Snapshot.Status);
    }
    using var rejoin = new CoopSession("carrier");
    rejoin.StartJoin(listener.ConnectAsync, "Connecting through carrier", 5);
    await Until(() => rejoin.Snapshot.Connected && host.Snapshot.Connected);
    host.Dispose();
    await Until(() => !rejoin.Snapshot.Connected);
});
await Test("large snapshots survive a bounded message carrier", async () =>
{
    // Each refusal by the full carrier costs the sender one timer tick, so capacity sets the throughput here.
    using var listener = new LoopbackListener(64);
    using var host=new CoopSession("bulk");using var client=new CoopSession("bulk");
    host.StartHost(listener,"Hosting");client.StartJoin(listener.ConnectAsync,"Connecting",5);
    await Until(()=>host.Snapshot.Connected && client.Snapshot.Connected);
    const int total=12288;ulong next=1;var received=new List<WorldFrame>();var clock=System.Diagnostics.Stopwatch.StartNew();var blocked=false;
    while(next<=total || received.Count<total)
    {
        Check(clock.Elapsed.TotalSeconds<20 && host.Snapshot.Connected && client.Snapshot.Connected);
        while(next<=total)
        {
            if(!host.PublishWorld(new(WorldKind.MissionState,1,next,Key:"mission",Flags:128,Values:new[]{(float)next}))) {blocked=true;break;}
            next++;
        }
        if(clock.Elapsed.TotalMilliseconds>=600) received.AddRange(client.DrainWorld(CoopSession.MaxWorldQueue));
        await Task.Delay(1);
    }
    Check(blocked && received.Select(f=>f.Sequence).SequenceEqual(Enumerable.Range(1,total).Select(i=>(ulong)i)));
});
await Test("build mismatch, refused dial and slow dial report errors over a message carrier", async () =>
{
    using var listener = new LoopbackListener();
    using var host = new CoopSession("game-a");
    host.StartHost(listener, "Hosting");
    using var wrongGame = new CoopSession("game-b");
    wrongGame.StartJoin(listener.ConnectAsync, "Connecting", 5);
    await Until(() => wrongGame.Snapshot.Status.Contains("Game or PBDMP build mismatch"));
    using var refused = new CoopSession("game-a");
    refused.StartJoin(_ => Task.FromException<IPeerConnection>(new IOException("Host is not reachable.")), "Connecting", 5);
    await Until(() => refused.Snapshot.Status == "Join failed: Host is not reachable.");
    using var slow = new CoopSession("game-a");
    slow.StartJoin(async stop => { await Task.Delay(Timeout.Infinite, stop); throw new IOException(); }, "Connecting", 1);
    await Until(() => slow.Snapshot.Status == "Join failed: Connection timed out");
    Check(!host.Snapshot.Connected);
});
await Test("message stream reassembles split and coalesced frames", async () =>
{
    var (near, far) = LoopbackPipe.Pair(64);
    using var stream = new MessageStream(near);
    var first = new Packet(MessageKind.Pose, Pose: new PlayerPose("level", true, 1, 2, 3, 4, 1.8f, "weapon"));
    var second = new Packet(MessageKind.Reject, Text: "reason");
    using var wire = new MemoryStream();
    await Protocol.WriteAsync(wire, first, CancellationToken.None);
    await Protocol.WriteAsync(wire, second, CancellationToken.None);
    var bytes = wire.ToArray();
    // One byte, an empty message, then everything else including the whole second frame.
    Check(far.TrySend(bytes.AsSpan(0, 1)) && far.TrySend(ReadOnlySpan<byte>.Empty) && far.TrySend(bytes.AsSpan(1)));
    Check(await Protocol.ReadAsync(stream, CancellationToken.None) == first);
    Check(await Protocol.ReadAsync(stream, CancellationToken.None) == second);
    far.Dispose();
    await ThrowsAsync(() => Protocol.ReadAsync(stream, CancellationToken.None));
});
Console.WriteLine($"PASS: {passed} network acceptance scenarios.");

async Task Test(string name, Func<Task> action)
{
    await action();
    passed++;
    Console.WriteLine("PASS " + name);
}
static void Check(bool value) { if (!value) throw new Exception("Assertion failed."); }
static void Throws(Action action)
{
    try { action(); } catch { return; }
    throw new Exception("Expected exception.");
}
static async Task ThrowsAsync(Func<Task> action)
{
    try { await action(); } catch { return; }
    throw new Exception("Expected exception.");
}
static async Task Until(Func<bool> predicate, int seconds = 5)
{
    var watch = System.Diagnostics.Stopwatch.StartNew();
    while (!predicate())
    {
        if (watch.Elapsed.TotalSeconds > seconds) throw new Exception("Timed out waiting for session state.");
        await Task.Delay(20);
    }
}
static int FreePort()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
}
sealed class FragmentStream : MemoryStream
{
    public FragmentStream(byte[] bytes) : base(bytes) { }
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        => base.ReadAsync(buffer[..Math.Min(buffer.Length, 1)], token);
}
// In-process stand-in for a relayed message link: bounded, ordered, and closed for both ends at once.
sealed class LoopbackPipe : IMessagePipe
{
    private sealed class Link { public volatile bool Closed; }
    private readonly System.Collections.Concurrent.ConcurrentQueue<byte[]> _inbound, _outbound;
    private readonly Link _link;
    private readonly int _capacity;
    private LoopbackPipe(System.Collections.Concurrent.ConcurrentQueue<byte[]> inbound,
        System.Collections.Concurrent.ConcurrentQueue<byte[]> outbound, Link link, int capacity)
    { _inbound = inbound; _outbound = outbound; _link = link; _capacity = capacity; }
    public static (LoopbackPipe Near, LoopbackPipe Far) Pair(int capacity)
    {
        var a = new System.Collections.Concurrent.ConcurrentQueue<byte[]>(); var b = new System.Collections.Concurrent.ConcurrentQueue<byte[]>();
        var link = new Link();
        return (new(a, b, link, capacity), new(b, a, link, capacity));
    }
    public bool TrySend(ReadOnlySpan<byte> message)
    {
        if (_link.Closed) throw new IOException("Carrier closed.");
        if (_outbound.Count >= _capacity) return false;
        _outbound.Enqueue(message.ToArray()); return true;
    }
    public void Receive(List<byte[]> into)
    {
        var closed = _link.Closed;
        while (_inbound.TryDequeue(out var message)) into.Add(message);
        if (closed && into.Count == 0) throw new IOException("Carrier closed.");
    }
    public void Dispose() => _link.Closed = true;
}
sealed class LoopbackListener : IPeerListener
{
    private readonly System.Threading.Channels.Channel<IPeerConnection> _incoming = System.Threading.Channels.Channel.CreateUnbounded<IPeerConnection>();
    private readonly int _capacity;
    public LoopbackListener(int capacity = 64) => _capacity = capacity;
    public Task<IPeerConnection> ConnectAsync(CancellationToken stop)
    {
        var (near, far) = LoopbackPipe.Pair(_capacity);
        if (!_incoming.Writer.TryWrite(new MessagePeerConnection(far))) throw new IOException("Host is not listening.");
        return Task.FromResult<IPeerConnection>(new MessagePeerConnection(near));
    }
    public async Task<IPeerConnection> AcceptAsync(CancellationToken stop) => await _incoming.Reader.ReadAsync(stop);
    public void Dispose() => _incoming.Writer.TryComplete();
}
