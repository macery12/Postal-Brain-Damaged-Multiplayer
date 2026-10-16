using Hyperstrange.Base;
using System.Text.Json;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using Il2CppInterop.Runtime;
using PBDMP.Network;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PBDMP.Plugin;

public sealed partial class MultiplayerBehaviour
{
    private sealed record SceneryFixture(string Key,string Kind,float Initial);
    private sealed record SceneryResult(string Key,string Kind,float Value,bool State);
    private int _sceneryStep,_sceneryIndex;
    private float _sceneryAt;
    private uint _sceneryEpoch;
    private SceneryFixture[] _sceneryFixtures=Array.Empty<SceneryFixture>();
    private void RunSceneryTest()
    {
        if(_sceneryStep==99) return;
        File.WriteAllText(Marker(TravelRole+"-scenery-state.txt"),$"step={_sceneryStep}, fixture={_sceneryIndex}, status={_lifecycle?.Status}, save={_saves?.Status}");
        if(_sceneryStep==0)
        {
            _port!.text=Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!;
            if(TravelRole=="host") {Host();SharedWorldPatches.DiagnosticFreeze=true;GameManager.LoadLevel(TestLevel(),true);}
            else {_address!.text="127.0.0.1";Join();}
            _sceneryStep=1;return;
        }
        if(_lifecycle is {NativeReady:false}) {_lifecycle.AutoContinue();return;}
        if(TravelRole=="host") SceneryHostTest();else SceneryClientTest();
    }
    private SceneryResult ProbeScenery(SceneryFixture f)
    {
        var c=_world!.ComponentForTest(f.Key)??throw new InvalidOperationException("Missing scenery fixture: "+f.Key);
        return f.Kind switch
        {
            "glass"=>new(f.Key,f.Kind,0,c.Cast<GlassObject>()._hit),
            "breakable"=>new(f.Key,f.Kind,0,c.Cast<BreakableObject>()._destroyed),
            "barrel"=>new(f.Key,f.Kind,c.Cast<ExplosiveBarrel>()._health.Current,c.Cast<ExplosiveBarrel>()._exploded),
            "burn"=>new(f.Key,f.Kind,c.Cast<BurnableObject>()._hitPoints.Current,c.Cast<BurnableObject>()._burned),
            _=>throw new InvalidOperationException("Unknown scenery fixture.")
        };
    }
    private void PrepareSceneryFixtures()
    {
        var components=_world!.CatalogueForTest.SceneryKeys.Select(k=>_world.ComponentForTest(k)!).Where(c=>c.gameObject.activeInHierarchy).ToArray();
        var fixtures=new List<SceneryFixture>();
        void Add(Component? c,string kind,float initial=0) {if(c!=null) fixtures.Add(new(_world.KeyForTest(c)!,kind,initial));}
        Add(components.FirstOrDefault(c=>c.TryCast<GlassObject>() is {} g && !g._hit && !g._disabled && g.GetComponentInChildren<Collider>()?.enabled==true),"glass");
        Add(components.FirstOrDefault(c=>c.TryCast<BreakableObject>() is {} b && !b._destroyed && !b._indestructible && !b._onlyExplosion && b._breaker?._wholeObject!=null),"breakable");
        var barrel=components.Select(c=>c.TryCast<ExplosiveBarrel>()).FirstOrDefault(b=>b!=null && !b._exploded && b._health.Current>0);
        Add(barrel,"barrel",barrel?._health.Current??0);
        var burn=components.Select(c=>c.TryCast<BurnableObject>()).FirstOrDefault(b=>b!=null && !b._burned && b._hitPoints.Current>0);
        Add(burn,"burn",burn?._hitPoints.Current??0);
        TravelCheck(fixtures.Any(f=>f.Kind=="glass") && fixtures.Any(f=>f.Kind is "barrel" or "burn"),"map supplies authored glass and partially damageable scenery fixtures");
        _sceneryFixtures=fixtures.ToArray();File.WriteAllText(Marker("scenery-fixtures.json"),JsonSerializer.Serialize(_sceneryFixtures));
    }
    private void SceneryHostTest()
    {
        switch(_sceneryStep)
        {
            case 1 when _world?.Active==true:
                QuietNpcs();PrepareSceneryFixtures();TravelMark("host-ready");_sceneryStep=2;break;
            case 2 when _lifecycle!.State.Released && Marked("scenery-client-ready"):
                GameManager.DisableMenu();TimeManager.SetApplicationTimeScale(1);
                TravelMark("scenery-op-"+_sceneryIndex);_sceneryStep=3;_sceneryAt=Time.unscaledTime;break;
            case 3 when Marked("scenery-sent-"+_sceneryIndex):
                var f=_sceneryFixtures[_sceneryIndex];var result=ProbeScenery(f);
                var done=f.Kind is "glass" or "breakable"?result.State:result.Value<f.Initial;
                if(!done && Time.unscaledTime<_sceneryAt+8) return;
                TravelCheck(done,"P2 native "+f.Kind+" action changes P1 once");
                if(f.Kind is "barrel" or "burn") TravelCheck(Mathf.Abs(result.Value-(f.Initial*.75f))<.01f && !result.State,"partial "+f.Kind+" damage is exact and does not destroy it");
                File.WriteAllText(Marker("scenery-result-"+_sceneryIndex),JsonSerializer.Serialize(result));_sceneryStep=4;break;
            case 4 when Marked("scenery-checked-"+_sceneryIndex):
                _sceneryIndex++;
                if(_sceneryIndex<_sceneryFixtures.Length) {_sceneryStep=2;break;}
                _saves!.RequestSave("manual-scenery","Scenery adapter regression");_sceneryStep=5;break;
            case 5 when !_saves!.Busy:
                var saved=_saves.Store.Read("manual-scenery").Save;
                TravelCheck(_sceneryFixtures.All(f=>saved.World.Any(w=>w.Key==f.Key)),"co-op save includes each tested scenery family");
                foreach(var fixture in _sceneryFixtures)
                {
                    var target=_world!.ComponentForTest(fixture.Key)!;
                    if(fixture.Kind=="barrel") target.Cast<ExplosiveBarrel>()._health.SetCurrentSilent(1);
                    if(fixture.Kind=="burn") target.Cast<BurnableObject>()._hitPoints.SetCurrentSilent(1);
                }
                _sceneryEpoch=_lifecycle!.Epoch;_saves.RequestLoad("manual-scenery");TravelMark("scenery-restoring");_sceneryStep=6;break;
            case 6 when !_saves!.Busy && _lifecycle!.State.Released && _lifecycle.Epoch>_sceneryEpoch:
                CheckSceneryResults("host save/restore");TravelMark("scenery-restored");_sceneryStep=7;break;
            case 7 when Marked("scenery-client-restored"):
                var barrelFixture=_sceneryFixtures.First(f=>f.Kind is "barrel" or "burn");
                var targetBarrel=_world!.ComponentForTest(barrelFixture.Key)!;
                targetBarrel.Cast<IFlammable>().FlameTick(Player.Instance.Cast<IAiActor>(),.05f,barrelFixture.Initial*5);
                File.WriteAllText(Marker("scenery-result-"+Array.IndexOf(_sceneryFixtures,barrelFixture)),JsonSerializer.Serialize(ProbeScenery(barrelFixture)));
                TravelMark("scenery-advance-after-load");_sceneryStep=8;break;
            case 8 when Marked("scenery-client-left") && !_session!.Snapshot.Connected:
                TravelMark("scenery-rejoin");_sceneryStep=9;break;
            case 9 when _lifecycle!.State.Released && Marked("scenery-client-current"):
                TravelCheck(_session!.Snapshot.Connected,"late join after disk load and new world changes retains the session");
                EndFrozenDiagnosticSession();_sceneryStep=10;break;
            case 10 when Marked("client-pass"):
                TravelMark("host-pass");_sceneryStep=99;break;
        }
    }
    private void CheckSceneryResults(string stage)
    {
        for(var i=0;i<_sceneryFixtures.Length;i++)
        {
            var expected=JsonSerializer.Deserialize<SceneryResult>(File.ReadAllText(Marker("scenery-result-"+i)))!;
            var actual=ProbeScenery(_sceneryFixtures[i]);
            TravelCheck(expected.State==actual.State && Mathf.Abs(expected.Value-actual.Value)<.01f,stage+" restores "+expected.Kind+" without resetting partial progress");
        }
    }
    private void SceneryClientTest()
    {
        switch(_sceneryStep)
        {
            case 1 when _world?.Active==true && _lifecycle!.State.Released:
                _sceneryFixtures=JsonSerializer.Deserialize<SceneryFixture[]>(File.ReadAllText(Marker("scenery-fixtures.json")))!;
                TravelMark("scenery-client-ready");_sceneryStep=2;break;
            case 2 when Marked("scenery-op-"+_sceneryIndex):
                var f=_sceneryFixtures[_sceneryIndex];var target=_world!.ComponentForTest(f.Key)!;
                var center=target.GetComponentInChildren<Collider>(true)?.bounds.center??target.transform.position;
                Player.Instance.Teleport(center+Vector3.back*2,Vector3.zero,Vector3.zero);_sceneryAt=Time.unscaledTime+.7f;_sceneryStep=3;break;
            case 3 when Time.unscaledTime>=_sceneryAt && !_lifecycle!.Frozen:
                var fixture=_sceneryFixtures[_sceneryIndex];var component=_world!.ComponentForTest(fixture.Key)!;
                var hit=new HitData{dealer=Player.Instance.Cast<IAiActor>(),position=component.GetComponentInChildren<Collider>(true)?.bounds.center??component.transform.position,direction=Vector3.forward,damage=7,weakSpotMultiplier=1};
                if(fixture.Kind=="barrel") component.Cast<ExplosiveBarrel>().FlameTick(Player.Instance.Cast<IAiActor>(),.05f,fixture.Initial*5);
                else if(fixture.Kind=="burn") component.Cast<BurnableObject>().FlameTick(Player.Instance.Cast<IAiActor>(),.05f,fixture.Initial*5);
                else component.Cast<IHittable>().Hit(hit);
                TravelMark("scenery-sent-"+_sceneryIndex);_sceneryAt=Time.unscaledTime+1;_sceneryStep=4;break;
            case 4 when Marked("scenery-result-"+_sceneryIndex) && Time.unscaledTime>=_sceneryAt:
                var expected=JsonSerializer.Deserialize<SceneryResult>(File.ReadAllText(Marker("scenery-result-"+_sceneryIndex)))!;
                var actual=ProbeScenery(_sceneryFixtures[_sceneryIndex]);
                TravelCheck(expected.State==actual.State && Mathf.Abs(expected.Value-actual.Value)<.01f,"P2 receives host "+expected.Kind+" state");
                TravelMark("scenery-checked-"+_sceneryIndex);_sceneryIndex++;_sceneryStep=_sceneryIndex<_sceneryFixtures.Length?2:5;break;
            case 5 when Marked("scenery-restored") && !_saves!.Busy && _lifecycle!.State.Released && _lifecycle.PersonalReady:
                CheckSceneryResults("client save/restore");TravelMark("scenery-client-restored");_sceneryStep=6;break;
            case 6 when Marked("scenery-advance-after-load"):
                ResetSession();TravelMark("scenery-client-left");_sceneryStep=7;break;
            case 7 when Marked("scenery-rejoin") && SessionLifecycle.Current==null && !SessionManager.SessionActive:
                _address!.text="127.0.0.1";Join();_sceneryStep=8;break;
            case 8 when _world?.Active==true && _lifecycle!.State.Released && _lifecycle.PersonalReady:
                CheckSceneryResults("current-world late join");TravelMark("scenery-client-current");_sceneryStep=9;break;
            case 9 when SessionLifecycle.Current==null && !SessionManager.SessionActive:
                TravelMark("client-pass");_sceneryStep=99;break;
        }
    }
}
