using System.Text.Json;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using Hyperstrange.PBD.PlayerComponents;
using PBDMP.Network;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Timeline;

namespace PBDMP.Plugin;

public sealed partial class MultiplayerBehaviour
{
    private sealed record MechanismFixture(string Key,string Kind);
    private sealed record MechanismProbe(string Key,float[] Position,float[] Rotation,float Clock,int State,float[] Animation);
    private int _mechanismStep;
    private float _mechanismAt;
    private uint _mechanismEpoch;
    private MechanismFixture[] _mechanismFixtures=Array.Empty<MechanismFixture>();
    private readonly Dictionary<string,int> _mechanismCallbacks=new();
    private readonly Dictionary<string,int> _mechanismSignalExpected=new();
    private readonly List<(UnityEvent Event,UnityAction Action)> _mechanismHandlers=new();
    private Vector3 _mechanismClientStart;
    private Elevator? _mechanismRider;
    private Vector3 _mechanismRiderOffset,_mechanismRiderPlatformStart;
    private bool _mechanismRiderChecked, _mechanismRiderDied;
    private int _mechanismGateIssued;
    private readonly Dictionary<string,int> _mechanismGateItems=new();
    private readonly List<(Breaker Breaker,Il2CppSystem.Action Handler)> _mechanismBreakHandlers=new();
    private void FinishMechanismClientTriggers()
    {
        var fixture=_mechanismFixtures.FirstOrDefault(f=>f.Kind=="gate");
        if(fixture==null) {TravelMark("mechanism-client-triggered");_mechanismStep=4;return;}
        var gate=_world!.ComponentForTest(fixture.Key)!.Cast<WireWallGate>();var inventory=Player.Instance.GetComponent<PlayerInventoryComponent>();
        foreach(var item in gate._requiredItemsComponent._requiredItemsQuantity)
        {
            var old=inventory.GetItemQuantity(item.Key);if(old>0) inventory.RemoveItem(item.Key,old);
            var total=item.Value*gate._requiredTntCount;inventory.AddItem(item.Key,total);_mechanismGateItems[item.Key.ID]=total;
        }
        TravelCheck(PlayerSessionState.FindNearby(Player.Instance,gate.transform.position,out var point),"TNT fixture supplies safe requester space");
        Player.Instance.Teleport(point,Vector3.zero,Vector3.zero);_mechanismAt=Time.unscaledTime+.5f;_mechanismStep=40;
    }
    private float _mechanismRiderAt;
    private void BoardMechanismElevator()
    {
        var fixture=_mechanismFixtures.FirstOrDefault(f=>f.Kind=="elevator");
        _mechanismRider=fixture!=null?_world!.ComponentForTest(fixture.Key)!.Cast<Elevator>():null;
        if(_mechanismRider?._movingPlatformTrigger==null) {_mechanismRider=null;return;}
        var floor=_mechanismRider._platform.GetComponentsInChildren<Collider>(true).Where(c=>c.enabled && !c.isTrigger && c.bounds.size.x>.5f && c.bounds.size.z>.5f).OrderBy(c=>c.bounds.size.y).FirstOrDefault();
        if(floor==null) throw new InvalidOperationException("Native elevator fixture has no rideable floor collider.");
        File.WriteAllLines(Marker("rider-floor-"+TravelRole+".tsv"),_mechanismRider._platform.GetComponentsInChildren<Collider>(true).Select(c=>WorldCatalogue.DiagnosticPath(c.transform)+"\tsize="+c.bounds.size+"\tcenter="+c.bounds.center+"\ttrigger="+c.isTrigger));
        var bounds=floor.bounds;
        Physics.SyncTransforms();
        TravelCheck(floor.Raycast(new Ray(new Vector3(bounds.center.x,bounds.max.y+2,bounds.center.z),Vector3.down),out var surface,bounds.size.y+4),"rider fixture resolves the actual central deck rather than its mesh bounds");
        var feet=surface.point+Vector3.up*.02f;
        Player.Instance.Teleport(feet,Vector3.zero,Vector3.zero);_world!.RebasePlatforms();
        _mechanismRider._movingPlatformTrigger.OnTriggerEnter(Player.Instance.Collider);
        _mechanismRiderOffset=Player.Instance.FeetPosition-_mechanismRider._platform.position;
        _mechanismRiderPlatformStart=_mechanismRider._platform.position;
    }
    private void BeginMechanismFallFixture()
    {
        var fall=_mechanismFixtures.FirstOrDefault(f=>f.Kind=="fall");
        if(fall!=null)
        {
            var target=_world!.ComponentForTest(fall.Key)!.Cast<FallingPlatform>();
            Player.Instance.Teleport(target.transform.position+Vector3.up,Vector3.zero,Vector3.zero);
            _mechanismAt=Time.unscaledTime+.5f;_mechanismStep=3;return;
        }
        FinishMechanismClientTriggers();
    }
    private void CheckMechanismRider(string stage)
    {
        var platform=_mechanismRider!._platform;
        TravelCheck(Vector3.Distance(platform.position,_mechanismRiderPlatformStart)>.05f,stage+" samples a moving native platform");
        var offset=Player.Instance.FeetPosition-platform.position;var drift=Vector3.Distance(offset,_mechanismRiderOffset);
        TravelCheck(Player.Instance.IsAlive && drift<.25f,stage+" carries P2 without a saved-journey impulse; relative drift="+drift+", initial="+_mechanismRiderOffset+", current="+offset+", platform delta="+(platform.position-_mechanismRiderPlatformStart)+", alive="+Player.Instance.IsAlive+", trigger="+_mechanismRider._movingPlatformTrigger._affectedMoveables.Count);
    }
    private void RunMechanismTest()
    {
        if(_mechanismStep==99) return;
        File.WriteAllText(Marker(TravelRole+"-mechanism-state.txt"),$"step={_mechanismStep}, gameTime={Time.time}, deadline={_mechanismAt}, ready={_lifecycle?.NativeReady}, frozen={_lifecycle?.Frozen}, save={_saves?.Status}");
        if(_mechanismStep==0)
        {
            _port!.text=Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!;
            if(TravelRole=="host") {Host();SharedWorldPatches.DiagnosticFreeze=true;GameManager.LoadLevel(TestLevel(),true);}
            else {_address!.text="127.0.0.1";Join();}
            _mechanismStep=1;return;
        }
        if(_lifecycle is {NativeReady:false}) {_lifecycle.AutoContinue();return;}
        if(_lifecycle?.LocalTutorialActive==true)
        {Player.Instance.GetComponent<Hyperstrange.PBD.PlayerComponents.PlayerTutorialComponent>().OnUseDown();return;}
        if(TravelRole=="host" && _mechanismStep==7 && _lifecycle is {WorldReady:true} life && life.Epoch>_mechanismEpoch) life.HostPauseRequest(true);
        if(TravelRole=="client" && _mechanismRider!=null)
        {
            if(_mechanismStep is 8 or 9 && Player.Instance!=null && _lifecycle!=null && (!Player.Instance.IsAlive || _lifecycle.PlayerState.Pending)) _mechanismRiderDied=true;
        }
        if(TravelRole=="host") MechanismHostTest();else MechanismClientTest();
    }
    private void PrepareMechanisms()
    {
        var fixtures=new List<MechanismFixture>();
        var components=_world!.CatalogueForTest.MechanismKeys.Concat(_world.CatalogueForTest.SequenceKeys).Distinct().Select(k=>_world.ComponentForTest(k)!).Where(c=>c.gameObject.activeInHierarchy).ToArray();
        void Add(Component? c,string type) {if(c!=null) fixtures.Add(new(_world.KeyForTest(c)!,type));}
        Add(components.FirstOrDefault(c=>c.TryCast<Mover>() is {} m && m._moveOffset.sqrMagnitude>.01f && m._moveCurve!=null),"mover");
        Add(components.FirstOrDefault(c=>c.TryCast<Rotater>() is {} m && m._rotateAmount.sqrMagnitude>.01f),"rotater");
        Add(components.FirstOrDefault(c=>c.TryCast<Elevator>() is {} e && e._platform!=null && e._top!=null && e._bottom!=null && Vector3.Distance(e._top.position,e._bottom.position)>1),"elevator");
        Add(components.FirstOrDefault(c=>c.TryCast<Scaler>() is {} s && Vector3.Distance(s.transform.localScale,s._scaleSize)>.1f),"scaler");
        Add(components.FirstOrDefault(c=>c.TryCast<FallingPlatform>() is {} f && !f._fallen && f._breaker?._wholeObject!=null),"fall");
        Add(components.FirstOrDefault(c=>c.TryCast<MultiRotater>() is {} m && m._animations.Length>0 && m._animations[0].amount.sqrMagnitude>.01f),"multi-rotater");
        Add(components.FirstOrDefault(c=>c.TryCast<TimelineSequence>() is {} sequence && sequence.name.Contains("SleepingCar",StringComparison.Ordinal) && sequence._playableDirector!=null),"timeline");
        Add(components.FirstOrDefault(c=>c.TryCast<RocketSequence>() is {} r && r._rocketHead!=null && r._animator!=null),"rocket");
        Add(components.FirstOrDefault(c=>c.TryCast<WireWallGate>() is {} g && g._requiredTntCount>0 && g._requiredItemsComponent!=null && g._breaker?._wholeObject!=null),"gate");
        _mechanismFixtures=fixtures.ToArray();
        TravelCheck(fixtures.Count>0,"map supplies authored native machinery fixtures");
        foreach(var f in fixtures)
        {
            var c=_world.ComponentForTest(f.Key)!;
            if(f.Kind=="mover") {var m=c.Cast<Mover>();m.ResetState();m._moveDuration=12;}
            if(f.Kind=="rotater") {var m=c.Cast<Rotater>();m.ResetState();m._rotateDuration=12;}
            if(f.Kind=="multi-rotater") {var m=c.Cast<MultiRotater>();m.ResetState();m._animations[0].duration=12;}
            if(f.Kind=="elevator") c.Cast<Elevator>()._duration=12;
            if(f.Kind=="scaler") c.Cast<Scaler>()._scaleDuration=12;
            if(f.Kind=="fall") c.Cast<FallingPlatform>()._destructionDelay=12;
            if(f.Kind=="rocket") c.Cast<RocketSequence>().ResetState();
            if(f.Kind=="gate")
            {
                var gate=c.Cast<WireWallGate>();gate.ResetState();gate._explosionDelay=12;
                var inventory=Player.Instance.GetComponent<PlayerInventoryComponent>();
                foreach(var item in gate._requiredItemsComponent._requiredItemsQuantity) {var n=inventory.GetItemQuantity(item.Key);if(n>0) inventory.RemoveItem(item.Key,n);}
            }
        }
        // Diagnostic timing is extended on authored components so disk capture
        // can inspect the actual native in-flight state. This is a fixture,
        // distinct from the natural-route campaign checks.
        File.WriteAllText(Marker("mechanism-fixtures.json"),JsonSerializer.Serialize(_mechanismFixtures));
        File.WriteAllLines(Marker("mechanism-paths.txt"),fixtures.Select(f=>f.Kind+"\\t"+WorldCatalogue.DiagnosticPath(_world.ComponentForTest(f.Key)!.transform)));
        AttachMechanismCounters();
    }
    private void AttachMechanismCounters(bool afterRestore=false)
    {
        foreach(var handler in _mechanismHandlers) if(handler.Event!=null) handler.Event.RemoveListener(handler.Action);
        foreach(var pair in _mechanismBreakHandlers) if(pair.Breaker!=null) pair.Breaker.OnBreak=Il2CppSystem.Delegate.Remove(pair.Breaker.OnBreak,pair.Handler)?.Cast<Il2CppSystem.Action>();
        _mechanismBreakHandlers.Clear();_mechanismCallbacks.Clear();_mechanismSignalExpected.Clear();_mechanismHandlers.Clear();
        foreach(var f in _mechanismFixtures)
        {
            var c=_world!.ComponentForTest(f.Key)!;
            if(c.TryCast<TimelineSequence>() is {} sequence && sequence._playableDirector.playableAsset.TryCast<TimelineAsset>() is {} timeline)
            {
                var at=afterRestore?JsonSerializer.Deserialize<MechanismProbe[]>(File.ReadAllText(Marker("mechanism-paused.json")))!.Single(p=>p.Key==f.Key).Clock:-1;
                foreach(var track in timeline.flattenedTracks.ToArray())
                {
                    var receiver=sequence._playableDirector.GetGenericBinding(track)?.TryCast<SignalReceiver>();if(receiver==null) continue;
                    for(var i=0;i<receiver.Count();i++)
                    {
                        var asset=receiver.GetSignalAssetAtIndex(i);var reaction=receiver.GetReactionAtIndex(i);if(asset==null || reaction==null) continue;
                        var expected=0;for(var j=0;j<track.GetMarkerCount();j++) if(track.GetMarker(j).TryCast<SignalEmitter>() is {} signal && signal.asset?.Pointer==asset.Pointer && signal.time>at) expected++;
                        var identity=f.Key+"|signal|"+i;_mechanismCallbacks[identity]=0;_mechanismSignalExpected[identity]=expected;
                        UnityAction handler=(Action)(()=>_mechanismCallbacks[identity]=_mechanismCallbacks.GetValueOrDefault(identity)+1);
                        reaction.AddListener(handler);_mechanismHandlers.Add((reaction,handler));
                    }
                }
            }
            if(c.TryCast<WireWallGate>() is {} gate)
            {
                var identity=f.Key;_mechanismCallbacks[identity]=0;
                Il2CppSystem.Action handler=(Action)(()=>_mechanismCallbacks[identity]=_mechanismCallbacks.GetValueOrDefault(identity)+1);
                gate._breaker.OnBreak=Il2CppSystem.Delegate.Combine(gate._breaker.OnBreak,handler).Cast<Il2CppSystem.Action>();_mechanismBreakHandlers.Add((gate._breaker,handler));
            }
            var events=c.TryCast<Mover>()?._onMoveEnd??c.TryCast<Rotater>()?._onRotateEnd??c.TryCast<RocketSequence>()?._OnRocketHeadHit;
            if(events==null) continue;
            var key=f.Key;_mechanismCallbacks[key]=0;
            UnityAction action=(Action)(()=>_mechanismCallbacks[key]=_mechanismCallbacks.GetValueOrDefault(key)+1);
            events.AddListener(action);_mechanismHandlers.Add((events,action));
        }
    }
    private MechanismProbe ProbeMechanism(MechanismFixture f)
    {
        var c=_world!.ComponentForTest(f.Key)!;var t=c.TryCast<Elevator>()?._platform??c.transform;
        float clock=0;int state=0;
        if(c.TryCast<Mover>() is {} mover) {clock=mover._movementTimeLeft;state=(int)mover._state;}
        if(c.TryCast<Rotater>() is {} rotater) {clock=rotater._rotationDurationLeft;state=(int)rotater._state;}
        if(c.TryCast<Elevator>() is {} elevator) state=(int)elevator.CurrentState;
        if(c.TryCast<Scaler>() is {} scaler) {clock=scaler.transform.localScale.x;state=scaler._scaled?1:0;}
        if(c.TryCast<FallingPlatform>() is {} fall) state=fall._fallen?1:0;
        if(c.TryCast<WireWallGate>() is {} gate) {clock=_world.GateFuseForTest(f.Key);state=!gate._breaker._wholeObject.activeSelf?2:gate._interacted?1:0;}
        var animation=Array.Empty<float>();
        if(c.TryCast<MultiRotater>() is {} multi) {clock=multi._currentRotationDuration;state=multi._isRotating?1:0;}
        if(c.TryCast<TimelineSequence>() is {} sequence)
        {
            var director=sequence._playableDirector;clock=(float)director.time;state=sequence._sequenceStarted?1:0;
            var values=new List<float>();
            if(director.playableAsset.TryCast<TimelineAsset>() is {} timeline)
                foreach(var track in timeline.flattenedTracks.ToArray().Where(t=>t.TryCast<AnimationTrack>()!=null))
                {
                    var binding=director.GetGenericBinding(track);var root=binding?.TryCast<Component>()?.transform??binding?.TryCast<GameObject>()?.transform;
                    if(root==null) continue;
                    var children=root.GetComponentsInChildren<Transform>(true).Where(WorldCatalogue.SafeMotion).Take(256).ToArray();
                    var listing=Marker("timeline-binding-"+TravelRole+".tsv");
                    if(!File.Exists(listing)) File.WriteAllLines(listing,children.Select((t,i)=>i+"\t"+WorldCatalogue.DiagnosticPath(t)+"\t"+string.Join(',',t.GetComponents<MonoBehaviour>().Select(b=>b.GetIl2CppType().Name))));
                    foreach(var child in children)
                    {var p=child.position;var q=child.rotation;var scale=child.localScale;values.AddRange(new[]{p.x,p.y,p.z,q.x,q.y,q.z,q.w,scale.x,scale.y,scale.z});}
                }
            animation=values.ToArray();
        }
        if(c.TryCast<RocketSequence>() is {} rocket)
        {
            clock=rocket._rocketHead.transform.localPosition.z;state=rocket.State;t=rocket._rocketHead.transform;
        }
        var position=t.position;var rotation=t.eulerAngles;
        return new(f.Key,new[]{position.x,position.y,position.z},new[]{rotation.x,rotation.y,rotation.z},clock,state,animation);
    }
    private void WriteMechanismProbes(string marker)
    {File.WriteAllText(Marker(marker),JsonSerializer.Serialize(_mechanismFixtures.Select(ProbeMechanism).ToArray()));}
    private void CompareMechanisms(string marker,string stage,bool compareClock=true)
    {
        var expected=JsonSerializer.Deserialize<MechanismProbe[]>(File.ReadAllText(Marker(marker)))!;
        foreach(var f in _mechanismFixtures)
        {
            var a=ProbeMechanism(f);var e=expected.Single(p=>p.Key==f.Key);
            TravelCheck(a.Animation.Length==e.Animation.Length,stage+" retains authored animation bindings");
            for(var i=0;i<a.Animation.Length;i+=10)
            {
                var position=Vector3.Distance(new(a.Animation[i],a.Animation[i+1],a.Animation[i+2]),new(e.Animation[i],e.Animation[i+1],e.Animation[i+2]));
                var angleDelta=Quaternion.Angle(new(a.Animation[i+3],a.Animation[i+4],a.Animation[i+5],a.Animation[i+6]),new(e.Animation[i+3],e.Animation[i+4],e.Animation[i+5],e.Animation[i+6]));
                if(position>=.2f || angleDelta>=2) TravelCheck(false,stage+" preserves native timeline binding "+i/10+" pose; position="+position+", angle="+angleDelta);
            }
            var pos=Vector3.Distance(new(a.Position[0],a.Position[1],a.Position[2]),new(e.Position[0],e.Position[1],e.Position[2]));
            var angle=Quaternion.Angle(Quaternion.Euler(a.Rotation[0],a.Rotation[1],a.Rotation[2]),Quaternion.Euler(e.Rotation[0],e.Rotation[1],e.Rotation[2]));
            TravelCheck(pos<.2f && angle<2 && a.State==e.State && (!compareClock || Mathf.Abs(a.Clock-e.Clock)<.3f),
                stage+" preserves "+f.Kind+" pose/phase; position="+pos+", angle="+angle+", clock="+a.Clock+"/"+e.Clock+", state="+a.State+"/"+e.State);
        }
    }
    private void StartMechanisms()
    {
        foreach(var f in _mechanismFixtures)
        {
            var c=_world!.ComponentForTest(f.Key)!;
            switch(f.Kind)
            {
                case "mover":c.Cast<Mover>().Move();break;
                case "rotater":c.Cast<Rotater>().Rotate();break;
                case "elevator":c.Cast<Elevator>().CallOpposite();break;
                case "scaler":c.Cast<Scaler>().Scale();break;
                case "multi-rotater":c.Cast<MultiRotater>().Rotate(0);break;
                case "timeline":c.Cast<TimelineSequence>().StartSequence();break;
                // Invoke the native fall marker explicitly in this phase fixture;
                // save during the real .25-second launch, before its hit callback.
                case "rocket":var r=c.Cast<RocketSequence>();r.StartSequence();r.OnFall_AnimationCallback();r.ShootRocket();break;
                // The client activates the actual delayed-fall entry point.
            }
        }
    }
    private void MechanismHostTest()
    {
        switch(_mechanismStep)
        {
            case 1 when _world?.Active==true:
                QuietNpcs();PrepareMechanisms();TravelMark("host-ready");_mechanismStep=2;break;
            case 2 when _lifecycle!.State.Released && Marked("mechanism-client-ready") && !_saves!.Busy:
                GameManager.DisableMenu();StartMechanisms();TravelMark("mechanism-started");_mechanismAt=Time.unscaledTime+(_mechanismFixtures.Any(f=>f.Kind=="rocket")?.08f:1);_mechanismStep=3;break;
            case 3 when Time.unscaledTime>=_mechanismAt && Marked("mechanism-client-triggered"):
                GameManager.PauseGame();_mechanismStep=31;break;
            case 31 when Time.timeScale==0 && _lifecycle!.Frozen:
                // Pause is requested inside Update. Native tweens later in that
                // frame may still consume its already sampled deltaTime. Take
                // the baseline on the next paused frame, without relaxing the
                // pose/clock assertions or changing gameplay pause behavior.
                WriteMechanismProbes("mechanism-paused.json");_mechanismAt=Time.unscaledTime+1;_mechanismStep=4;break;
            case 4 when Time.unscaledTime>=_mechanismAt:
                CompareMechanisms("mechanism-paused.json","shared pause");TravelMark("mechanism-paused");_mechanismStep=5;break;
            case 5 when Marked("mechanism-client-paused"):
                _saves!.RequestSave("manual-mechanism","In-flight native machinery regression");_mechanismStep=6;break;
            case 6 when !_saves!.Busy:
                var save=_saves.Store.Read("manual-mechanism").Save;
                WriteMechanismProbes("mechanism-end-probe.json");
                foreach(var f in _mechanismFixtures)
                    TravelCheck(save.World.Any(w=>w.Key==f.Key),"durable capture includes "+f.Kind);
                foreach(var gateFixture in _mechanismFixtures.Where(f=>f.Kind=="gate"))
                {
                    var gate=_world!.ComponentForTest(gateFixture.Key)!.Cast<WireWallGate>();
                    TravelCheck(gate._interacted && gate._tntCount==gate._requiredTntCount,"duplicate immutable P2 requests plant the native TNT count exactly once");
                    foreach(var item in gate._requiredItemsComponent._requiredItemsQuantity) TravelCheck(Player.Instance.GetComponent<PlayerInventoryComponent>().GetItemQuantity(item.Key)==0,"P2 TNT never consumes P1 items");
                    TravelCheck(save.World.Any(w=>w.Key==gateFixture.Key && w.Kind==WorldKind.Mechanism && w.Flags==4 && w.Value>0 && w.Value<12),"save includes the partially elapsed native gate fuse");
                }
                if(_mechanismFixtures.Any(f=>f.Kind=="rocket")) TravelCheck(save.World.Any(w=>w.Kind==WorldKind.Mechanism && w.Flags==8 && w.Values is {Length:6} v && v[5]==1 && v[3]>0 && v[3]<.25f),"rocket capture is inside the actual launch interval");
                _mechanismEpoch=_lifecycle!.Epoch;_saves.RequestLoad("manual-mechanism");_mechanismStep=7;break;
            case 7 when !_saves!.Busy && _lifecycle!.State.Released && _lifecycle.Epoch>_mechanismEpoch:
                GameManager.PauseGame();CompareMechanisms("mechanism-paused.json","host disk restoration");
                AttachMechanismCounters(true);WriteMechanismProbes("mechanism-restored.json");TravelMark("mechanism-restored");_mechanismStep=8;break;
            case 8 when Marked("mechanism-client-restored"):
                GameManager.DisableMenu();_mechanismAt=Time.time+16;_mechanismStep=9;break;
            case 9 when Time.time>=_mechanismAt:
                WriteMechanismProbes("mechanism-end-probe.json");
                foreach(var f in _mechanismFixtures)
                {
                    var c=_world!.ComponentForTest(f.Key)!;var p=ProbeMechanism(f);
                    TravelCheck(f.Kind switch {"mover"=>p.State==3,"rotater"=>p.State==3,"elevator"=>p.State is 0 or 2,"fall"=>p.State==1,"gate"=>p.State==2,"rocket"=>p.State==2 && !c.Cast<RocketSequence>()._rocketHead.activeSelf,"multi-rotater"=>p.State==0,"timeline"=>_world.SequencePeakForTest(f.Key)>=c.Cast<TimelineSequence>()._playableDirector.duration-.15,"scaler"=>Vector3.Distance(c.transform.localScale,c.Cast<Scaler>()._scaleSize)<.01f,_=>false},"restored "+f.Kind+" reaches its native endpoint; state="+p.State+", clock="+p.Clock+", callbacks="+_mechanismCallbacks.GetValueOrDefault(f.Key));
                    if(_mechanismCallbacks.ContainsKey(f.Key)) TravelCheck(_mechanismCallbacks[f.Key]==1,"restored "+f.Kind+" invokes its future completion callback once");
                }
                foreach(var signal in _mechanismSignalExpected) TravelCheck(_mechanismCallbacks[signal.Key]==signal.Value,"restored timeline executes only future signal reactions; actual="+_mechanismCallbacks[signal.Key]+", expected="+signal.Value);
                GameManager.PauseGame();WriteMechanismProbes("mechanism-end.json");TravelMark("mechanism-ended");_mechanismStep=10;break;
            case 10 when Marked("mechanism-client-ended"):
                if(_mechanismFixtures.Any(f=>f.Kind=="timeline")) {_mechanismStep=20;break;}
                EndFrozenDiagnosticSession();_mechanismStep=11;break;
            case 20 when Marked("mechanism-client-left") && !_session!.Snapshot.Connected:
                TravelMark("mechanism-rejoin");_mechanismStep=21;break;
            case 21 when Marked("mechanism-client-current") && _lifecycle!.State.Released:
                EndFrozenDiagnosticSession();_mechanismStep=11;break;
            case 11 when Marked("client-pass"):
                TravelMark("host-pass");_mechanismStep=99;break;
        }
    }
    private void MechanismClientTest()
    {
        switch(_mechanismStep)
        {
            case 1 when _world?.Active==true && _lifecycle!.State.Released:
                _mechanismFixtures=JsonSerializer.Deserialize<MechanismFixture[]>(File.ReadAllText(Marker("mechanism-fixtures.json")))!;
                TravelMark("mechanism-client-ready");_mechanismStep=2;break;
            case 2 when Marked("mechanism-started"):
                _mechanismClientStart=Player.Instance.FeetPosition;
                // Keep the very short rocket launch fixture independent of riding.
                if(!_mechanismFixtures.Any(f=>f.Kind=="rocket")) BoardMechanismElevator();
                if(_mechanismRider!=null) {_mechanismAt=Time.unscaledTime+.5f;_mechanismStep=30;break;}
                BeginMechanismFallFixture();break;
            case 30 when Time.unscaledTime>=_mechanismAt && !_lifecycle!.Frozen:
                CheckMechanismRider("live platform");BeginMechanismFallFixture();break;
            case 3 when Time.unscaledTime>=_mechanismAt && !_lifecycle!.Frozen:
                _world!.ComponentForTest(_mechanismFixtures.First(f=>f.Kind=="fall").Key)!.Cast<FallingPlatform>().FallWithDelay(Player.Instance);
                FinishMechanismClientTriggers();break;
            case 40 when Time.unscaledTime>=_mechanismAt && !_lifecycle!.Frozen:
                var gate=_world!.ComponentForTest(_mechanismFixtures.First(f=>f.Kind=="gate").Key)!.Cast<WireWallGate>();
                if(_world.HasPendingInteractions || gate._tntCount<_mechanismGateIssued) return;
                TravelCheck(gate._tntCount==_mechanismGateIssued,"duplicate request delivery does not duplicate TNT placement");
                if(gate._interacted)
                {
                    foreach(var item in gate._requiredItemsComponent._requiredItemsQuantity) TravelCheck(Player.Instance.GetComponent<PlayerInventoryComponent>().GetItemQuantity(item.Key)==0,"P2 spends only the TNT actually planted");
                    TravelMark("mechanism-client-triggered");_mechanismStep=4;break;
                }
                gate.Interact(Player.Instance);_world.RepeatInteractionForTest();_mechanismGateIssued++;_mechanismAt=Time.unscaledTime+.15f;break;
            case 4 when Marked("mechanism-paused") && _lifecycle!.Frozen:
                if(_mechanismRider!=null) BoardMechanismElevator();
                else if(_mechanismFixtures.Any(f=>f.Kind=="fall")) Player.Instance.Teleport(_mechanismClientStart,Vector3.zero,Vector3.zero);
                _mechanismAt=Time.unscaledTime+.5f;_mechanismStep=5;break;
            case 5 when Time.unscaledTime>=_mechanismAt:
                TravelCheck(Player.Instance.IsAlive && !_lifecycle!.PlayerState.Pending,"P2 remains alive through the host's final paused platform correction");
                CompareMechanisms("mechanism-paused.json","P2 shared pause");TravelMark("mechanism-client-paused");_mechanismStep=6;break;
            case 6 when Marked("mechanism-restored") && !_saves!.Busy && _lifecycle!.State.Released:
                _mechanismAt=Time.unscaledTime+.5f;_mechanismStep=7;break;
            case 7 when Time.unscaledTime>=_mechanismAt:
                CompareMechanisms("mechanism-restored.json","P2 disk restoration");
                if(_mechanismRider!=null)
                {
                    _mechanismRider=_world!.ComponentForTest(_mechanismFixtures.First(f=>f.Kind=="elevator").Key)!.Cast<Elevator>();
                    _mechanismRiderOffset=Player.Instance.FeetPosition-_mechanismRider._platform.position;_mechanismRiderPlatformStart=_mechanismRider._platform.position;_mechanismRiderAt=Time.time;
                }
                TravelMark("mechanism-client-restored");_mechanismStep=8;break;
            case 8 when _mechanismRider!=null && !_mechanismRiderChecked && !_lifecycle!.Frozen && Time.time>=_mechanismRiderAt+.5f:
                // Unpause metadata can precede the next bounded world sweep.
                // Measure a real received step, with a finite failure deadline.
                if(Vector3.Distance(_mechanismRider._platform.position,_mechanismRiderPlatformStart)<=.05f && Time.time<_mechanismRiderAt+5) break;
                CheckMechanismRider("restored platform");_mechanismRiderChecked=true;break;
            case 8 when Marked("mechanism-ended") && _lifecycle!.Frozen:
                _mechanismAt=Time.unscaledTime+.5f;_mechanismStep=9;break;
            case 9 when Time.unscaledTime>=_mechanismAt:
                if(_mechanismRider!=null)
                {
                    TravelCheck(!_mechanismRiderDied,"P2 survives the whole restored native elevator journey, including intermediate physics steps");
                    CheckMechanismRider("completed platform journey");
                }
                CompareMechanisms("mechanism-end.json","P2 completed native motion",false);TravelMark("mechanism-client-ended");
                if(_mechanismFixtures.Any(f=>f.Kind=="timeline")) {ResetSession();TravelMark("mechanism-client-left");_mechanismStep=20;}else _mechanismStep=10;
                break;
            case 20 when Marked("mechanism-rejoin") && SessionLifecycle.Current==null && !SessionManager.SessionActive:
                _address!.text="127.0.0.1";Join();_mechanismStep=21;break;
            case 21 when _world?.Active==true && _lifecycle!.State.Released && _lifecycle.PersonalReady:
                CompareMechanisms("mechanism-end.json","P2 current-world rejoin",false);
                foreach(var f in _mechanismFixtures.Where(f=>f.Kind=="timeline"))
                {
                    var d=_world.ComponentForTest(f.Key)!.Cast<TimelineSequence>()._playableDirector;
                    TravelCheck(d.time>=d.duration-.15,"rejoining restores completed timeline phase instead of reset initialTime");
                }
                TravelMark("mechanism-client-current");_mechanismStep=10;break;
            case 10 when SessionLifecycle.Current==null && !SessionManager.SessionActive:
                TravelMark("client-pass");_mechanismStep=99;break;
        }
    }
}
