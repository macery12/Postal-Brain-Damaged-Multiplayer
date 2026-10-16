using System.Text.Json;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using PBDMP.Network;
using UnityEngine;

namespace PBDMP.Plugin;

public sealed partial class MultiplayerBehaviour
{
    private sealed record ArenaFixture(string Arena,string Syringe,string Button);
    private ArenaFixture? _arenaFixture;
    private WorldFrame[] _arenaSaved=Array.Empty<WorldFrame>();
    private int _arenaStep;
    private float _arenaAt;
    private uint _arenaEpoch;
    private CovidArenaController TestArena=>_world!.ComponentForTest(_arenaFixture!.Arena)!.Cast<CovidArenaController>();
    private AntiCovidSyringeController TestSyringe=>_world!.ComponentForTest(_arenaFixture!.Syringe)!.Cast<AntiCovidSyringeController>();
    private void RunArenaTest()
    {
        if(_arenaStep==99) return;
        File.WriteAllText(Marker(TravelRole+"-arena-state.txt"),$"step={_arenaStep}, time={Time.time}, deadline={_arenaAt}, ready={_lifecycle?.NativeReady}, frozen={_lifecycle?.Frozen}, save={_saves?.Status}");
        if(_arenaStep==0)
        {
            _port!.text=Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!;
            if(TravelRole=="host") {Host();SharedWorldPatches.DiagnosticFreeze=true;GameManager.LoadLevel(TestLevel(),true);}
            else {_address!.text="127.0.0.1";Join();}
            _arenaStep=1;return;
        }
        if(_lifecycle is {NativeReady:false}) {_lifecycle.AutoContinue();return;}
        if(_lifecycle?.LocalTutorialActive==true) {Player.Instance.GetComponent<Hyperstrange.PBD.PlayerComponents.PlayerTutorialComponent>().OnUseDown();return;}
        if(TravelRole=="host" && _arenaStep==7 && _lifecycle is {WorldReady:true} life && life.Epoch>_arenaEpoch) life.HostPauseRequest(true);
        if(TravelRole=="host") ArenaHostTest();else ArenaClientTest();
    }
    private void CheckArena(string stage)
    {
        var arena=TestArena;var frame=_arenaSaved.Single(f=>f.Flags==9);
        TravelCheck(arena._segments.Select(s=>_world!.KeyForTest(s)).SequenceEqual(frame.Data!),stage+" preserves shifted arena segment order");
        TravelCheck(Vector3.Distance(arena._arenaBase.transform.position,new(frame.Position.X,frame.Position.Y,frame.Position.Z))<.1f,stage+" preserves arena base position");
        var cooldown=_arenaSaved.Single(f=>f.Flags==10 && f.Key==_arenaFixture!.Syringe);
        TravelCheck(TestSyringe.State==2 && Mathf.Abs(Mathf.Max(0,TestSyringe._injectCooldown.TimeLeft)-cooldown.Value)<.3f,stage+" preserves syringe state and remaining cooldown; state="+TestSyringe.State+", left="+TestSyringe._injectCooldown.TimeLeft+"/"+cooldown.Value);
        foreach(var f in _arenaSaved.Where(f=>f.Flags==11))
        {
            var s=_world!.ComponentForTest(f.Key)!.Cast<AntiCovidSyringe>();var info=s._animator.GetCurrentAnimatorStateInfo(0);
            TravelCheck(s._canInject==(f.Values![3]!=0) && s._injected==(f.Values[2]!=0) && unchecked((uint)info.fullPathHash)==f.Revision && Mathf.Abs(info.normalizedTime-f.Value)<.1f,stage+" preserves syringe animation and collision eligibility");
        }
    }
    private void ArenaHostTest()
    {
        switch(_arenaStep)
        {
            case 1 when _world?.Active==true:
                QuietNpcs();
                var arena=_world.CatalogueForTest.MechanismKeys.Select(k=>_world.ComponentForTest(k)!.TryCast<CovidArenaController>()).First(c=>c!=null)!;
                var segment=arena._segments.Skip(2).First(s=>s._antiCovidSyringeController!=null && s._buttonShootable!=null);
                _arenaFixture=new(_world.KeyForTest(arena)!,_world.KeyForTest(segment._antiCovidSyringeController)!,_world.KeyForTest(segment._buttonShootable)!);
                File.WriteAllLines(Marker("arena-sources.tsv"),_world.CatalogueForTest.MechanismKeys.Select(k=>_world.ComponentForTest(k)!.TryCast<AntiCovidSyringe>()).Where(c=>c!=null).Select(c=>WorldCatalogue.DiagnosticPath(c!.transform)+"\t"+c._injectVfx?.name+"\taudited="+(c._injectVfx!=null && AuditedVisualSources.Names.Contains(c._injectVfx.name))));
                File.WriteAllText(Marker("arena-fixture.json"),JsonSerializer.Serialize(_arenaFixture));TravelMark("host-ready");_arenaStep=2;break;
            case 2 when _lifecycle!.State.Released && Marked("arena-client-ready") && !_saves!.Busy:
                GameManager.DisableMenu();
                var old=TestArena._segments.Select(s=>s.Pointer).ToArray();TestArena.ShiftSegments(true,1);
                TravelCheck(!old.SequenceEqual(TestArena._segments.Select(s=>s.Pointer)),"native arena shift reorders authored segments");
                _arenaAt=Time.unscaledTime+1;_arenaStep=20;break;
            case 20 when Time.unscaledTime>=_arenaAt:
                TravelMark("arena-button");_arenaAt=Time.unscaledTime+10;_arenaStep=3;break;
            case 3 when Marked("arena-client-triggered"):
                if(TestSyringe.State==0 && Time.unscaledTime<_arenaAt) return;
                TravelCheck(TestSyringe.State==1,"P2's native shootable button prepares the host-owned syringe");
                // Extend the real cooldown, not its clock implementation, so the
                // native completion path remains observable after disk restore.
                TestSyringe._injectCooldown._difficultyBased=false;TestSyringe._injectCooldown._duration=12;TestSyringe.Inject();foreach(var syringe in TestSyringe._syringes) syringe._injectVfx.Play();
                TravelMark("arena-effects");
                _arenaAt=Time.unscaledTime+.5f;_arenaStep=4;break;
            case 4 when Time.unscaledTime>=_arenaAt:
                if(!Marked("arena-client-effects")) return;
                GameManager.PauseGame();_saves!.RequestSave("manual-arena","Shifted arena and active syringe");_arenaStep=5;break;
            case 5 when !_saves!.Busy:
                var saved=_saves.Store.Read("manual-arena").Save;
                _arenaSaved=saved.World.Where(f=>f.Kind==WorldKind.Mechanism && f.Flags is 9 or 10 or 11).ToArray();
                TravelCheck(_arenaSaved.Any(f=>f.Flags==9) && _arenaSaved.Any(f=>f.Flags==11),"durable save includes arena ordering and native syringe animations");
                File.WriteAllText(Marker("arena-saved.json"),JsonSerializer.Serialize(_arenaSaved));_arenaAt=Time.unscaledTime+1;_arenaStep=6;break;
            case 6 when Time.unscaledTime>=_arenaAt:
                CheckArena("shared pause");_arenaEpoch=_lifecycle!.Epoch;_saves!.RequestLoad("manual-arena");_arenaStep=7;break;
            case 7 when !_saves!.Busy && _lifecycle!.State.Released && _lifecycle.Epoch>_arenaEpoch:
                GameManager.PauseGame();CheckArena("host disk restoration");TravelMark("arena-restored");_arenaStep=8;break;
            case 8 when Marked("arena-client-left") && !_session!.Snapshot.Connected:
                TravelMark("arena-rejoin");_arenaStep=21;break;
            case 21 when Marked("arena-client-current") && _lifecycle!.State.Released:
                GameManager.DisableMenu();_arenaAt=Time.time+14;_arenaStep=9;break;
            case 9 when Time.time>=_arenaAt:
                TravelCheck(TestSyringe.State!=2,"restored syringe resumes its native cooldown completion");
                GameManager.PauseGame();TravelMark("arena-ended");_arenaStep=10;break;
            case 10 when Marked("arena-client-ended"):
                EndFrozenDiagnosticSession();_arenaStep=11;break;
            case 11 when Marked("client-pass"):
                TravelMark("host-pass");_arenaStep=99;break;
        }
    }
    private void ArenaClientTest()
    {
        switch(_arenaStep)
        {
            case 1 when _world?.Active==true && _lifecycle!.State.Released:
                _arenaFixture=JsonSerializer.Deserialize<ArenaFixture>(File.ReadAllText(Marker("arena-fixture.json")))!;
                TravelMark("arena-client-ready");_arenaStep=2;break;
            case 2 when Marked("arena-button"):
                var button=_world!.ComponentForTest(_arenaFixture!.Button)!.Cast<ButtonShootable>();
                Player.Instance.Teleport(button.transform.position+Vector3.back,Vector3.zero,Vector3.zero);_arenaAt=Time.unscaledTime+.5f;_arenaStep=3;break;
            case 3 when Time.unscaledTime>=_arenaAt && !_lifecycle!.Frozen:
                _world!.ComponentForTest(_arenaFixture!.Button)!.Cast<ButtonShootable>().Interact();TravelMark("arena-client-triggered");_arenaStep=4;break;
            case 4 when Marked("arena-effects") && !Marked("arena-client-effects"):
                if(_world!.SyringeParticlesForTest==0) return;
                TravelCheck(_world.SyringeParticlesForTest>0,"P2 shows native injection particles with gameplay scripts isolated");TravelMark("arena-client-effects");break;
            case 4 when Marked("arena-restored") && !_saves!.Busy && _lifecycle!.State.Released:
                _arenaAt=Time.unscaledTime+.5f;_arenaStep=5;break;
            case 5 when Time.unscaledTime>=_arenaAt:
                _arenaSaved=JsonSerializer.Deserialize<WorldFrame[]>(File.ReadAllText(Marker("arena-saved.json")))!;
                CheckArena("P2 disk restoration");TravelMark("arena-client-restored");ResetSession();TravelMark("arena-client-left");_arenaStep=60;break;
            case 60 when Marked("arena-rejoin") && SessionLifecycle.Current==null && !SessionManager.SessionActive:
                _address!.text="127.0.0.1";Join();_arenaStep=61;break;
            case 61 when _world?.Active==true && _lifecycle!.State.Released && _lifecycle.PersonalReady:
                CheckArena("P2 current-world rejoin");TravelMark("arena-client-current");_arenaStep=6;break;
            case 6 when Marked("arena-ended"):
                TravelCheck(TestSyringe.State!=2,"P2 sees the native syringe completion");TravelMark("arena-client-ended");_arenaStep=7;break;
            case 7 when SessionLifecycle.Current==null && !SessionManager.SessionActive:
                TravelMark("client-pass");_arenaStep=99;break;
        }
    }
}
