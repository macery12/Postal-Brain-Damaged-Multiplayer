using System.Text.Json;
using Hyperstrange.Base;
using Hyperstrange.PBD;
using UnityEngine;

namespace PBDMP.Plugin;

public sealed partial class MultiplayerBehaviour
{
    private int _missionExitStep,_missionFinishBefore;
    private float _missionExitAt;
    private Vector3 _missionExitPosition;
    private Vector3 _missionExitAway;
    private string _missionExitKey="";
    private bool _missionExitInteractive;
    private static string ResultStatsText()
    {
        var s=LevelController.GetLevelStats();return JsonSerializer.Serialize(new[]{s.KillsStat.CurrentCount,s.KillsStat.MaxCount,s.SecretsStat.CurrentCount,s.SecretsStat.MaxCount,s.QuantityStat.CurrentCount,s.QuantityStat.MaxCount,s.TimeStat.CompletionTime});
    }
    private void RunMissionExitTest()
    {
        if(_missionExitStep==99) return;
        File.WriteAllText(Marker(TravelRole+"-mission-exit-state.txt"),$"step={_missionExitStep}, ready={_lifecycle?.NativeReady}, world={_lifecycle?.WorldReady}, released={_lifecycle?.State.Released}, frozen={_lifecycle?.Frozen}, status={_world?.MissionStatus}, pending={_world?.MissionEndPendingForTest}, ends={_world?.MissionFinishesForTest}, exits={_world?.MissionExitsForTest}");
        if(_missionExitStep==0)
        {
            _port!.text=Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!;
            if(TravelRole=="host") {Host();GameManager.LoadLevel(TestLevel(),true);}else {_address!.text="127.0.0.1";Join();}
            _missionExitStep=1;return;
        }
        if(_lifecycle is {NativeReady:false}) {_lifecycle.AutoContinue();return;}
        // A native tutorial (Wallmarkt shows one) holds the shared pause until its player dismisses it.
        if(_lifecycle?.LocalTutorialActive==true && Player.Instance!=null) {Player.Instance.GetComponent<Hyperstrange.PBD.PlayerComponents.PlayerTutorialComponent>().OnUseDown();return;}
        if(TravelRole=="host") MissionExitHostTest();else MissionExitClientTest();
    }
    private void MissionExitHostTest()
    {
        switch(_missionExitStep)
        {
            case 1 when _world?.Active==true:
                QuietNpcs();SharedWorldPatches.DiagnosticFreeze=true;TimeManager.SetApplicationTimeScale(1);TravelMark("host-ready");_missionExitStep=2;break;
            case 2 when Marked("client-mission-ready") && _lifecycle!.State.Released:
                GameManager.DisableMenu();TimeManager.SetApplicationTimeScale(1);_missionFinishBefore=_world!.MissionFinishesForTest;TravelMark("mission-kill-client");_missionExitStep=3;break;
            case 3 when Marked("client-mission-dead") && _session!.Snapshot.RemotePose?.Alive==false:
                LevelController._instance.EndLevel(LevelController.EndLevelType.FINISH);
                TravelCheck(_world!.MissionEndPendingForTest && _world.MissionFinishesForTest==_missionFinishBefore,"automatic story ending waits while P2 is dead");_missionExitStep=4;break;
            case 4 when _world!.MissionFinishesForTest>_missionFinishBefore:
                TravelCheck(!_world.MissionEndPendingForTest && _session!.Snapshot.RemotePose?.Alive==true,"automatic story ending resumes once after P2 independently respawns");
                _missionExitAt=Time.unscaledTime+3;_missionExitStep=41;break;
            case 41 when Time.unscaledTime>=_missionExitAt:
                File.WriteAllText(Marker("mission-result-stats.json"),ResultStatsText());TravelMark("mission-results-check");_missionExitStep=42;break;
            case 42 when Marked("client-mission-results-checked"):
                GameManager.Load(null);_missionExitStep=5;break;
            case 5 when _lifecycle!.NativeReady && _lifecycle.WorldReady && _lifecycle.State.Released && Marked("client-mission-restarted"):
                QuietNpcs();GameManager.DisableMenu();TimeManager.SetApplicationTimeScale(1);
                var catalogue=_world!.CatalogueForTest;var key=catalogue.MissionExitKeys.First();_missionExitKey=key;_missionExitInteractive=catalogue.MissionInteractionExits.Contains(key);
                if(_missionExitInteractive) _missionExitPosition=catalogue.Components[key].transform.position+Vector3.up*0.1f;
                else {var bounds=catalogue.MissionContacts[key].First(c=>c.enabled).bounds;_missionExitPosition=bounds.center-Vector3.up*(Player.Instance.Height*0.5f);}
                File.WriteAllText(Marker("mission-exit-position.json"),JsonSerializer.Serialize(new[]{_missionExitPosition.x,_missionExitPosition.y,_missionExitPosition.z}));
                File.WriteAllText(Marker("mission-exit-key.txt"),key);File.WriteAllText(Marker("mission-exit-interactive.txt"),_missionExitInteractive.ToString());
                File.WriteAllLines(Marker("mission-trigger-audit.tsv"),catalogue.MissionAudit);
                _missionFinishBefore=_world.MissionFinishesForTest;TravelMark("mission-exit-client-go");_missionExitStep=6;break;
            case 6 when Marked("client-mission-at-exit"):
                _missionExitAt=Time.unscaledTime+1;_missionExitStep=7;break;
            case 7 when Time.unscaledTime>=_missionExitAt:
                TravelCheck(_world!.MissionExitsForTest==0 && _world.MissionFinishesForTest==_missionFinishBefore && _world.MissionStatus.Contains("partner"),"P2 alone at native campaign exit waits for P1");
                TravelMark("mission-exit-client-leave");_missionExitStep=8;break;
            case 8 when Marked("client-mission-left-exit"):
                _missionExitAt=Time.unscaledTime+0.5f;_missionExitStep=9;break;
            case 9 when Time.unscaledTime>=_missionExitAt:
                TravelCheck(_world!.MissionStatus=="" && _world.MissionExitsForTest==0,"leaving the exit area withdraws readiness without transition");
                Player.Instance.Teleport(_missionExitPosition,Vector3.zero,Vector3.zero);_missionExitAt=Time.unscaledTime+0.5f;_missionExitStep=10;break;
            case 10 when Time.unscaledTime>=_missionExitAt:
                TravelCheck(_world!.MissionExitsForTest==0 && _world.MissionStatus.Contains("partner"),"P1 alone at native campaign exit waits for P2");TravelMark("mission-exit-client-return");_missionExitStep=11;break;
            case 11 when _world!.MissionFinishesForTest>_missionFinishBefore:
                TravelCheck(_world.MissionFinishesForTest==_missionFinishBefore+1,"both-player native exit commits one level finish");TravelMark("mission-exit-accepted");_missionExitStep=12;break;
            case 12 when Marked("client-mission-exit-checked"):
                ResetSession();SharedWorldPatches.DiagnosticFreeze=false;_missionExitStep=13;break;
            case 13 when Marked("client-pass"):
                TravelMark("host-pass");_missionExitStep=99;break;
        }
    }
    private void MissionExitClientTest()
    {
        switch(_missionExitStep)
        {
            case 1 when _world?.Active==true && _lifecycle!.WorldReady && _lifecycle.State.Released:
                TravelMark("client-mission-ready");_missionExitStep=2;break;
            case 2 when Marked("mission-kill-client"):
                Player.Instance.Kill();TravelCheck(!Player.Instance.IsAlive,"P2 is dead for the automatic-ending readiness regression");TravelMark("client-mission-dead");_missionExitStep=3;break;
            case 3 when Marked("mission-results-check"):
                var results=UnityEngine.Object.FindObjectOfType<Hyperstrange.PBD.UI.Menus.EndLevelMenu>();
                TravelCheck(results!=null && results.gameObject.activeInHierarchy,"P2 receives the native level complete screen");
                TravelCheck(_world!.ClientLevelStats.HasValue,"P2 displays authoritative host result statistics");
                TravelCheck(ResultStatsText()==File.ReadAllText(Marker("mission-result-stats.json")),"P2 result counts and exact completion time match P1");
                ResetSession();_missionExitStep=32;break;
            case 32 when SessionLifecycle.Current==null && !SessionManager.SessionActive:
                _address!.text="127.0.0.1";Join();_missionExitAt=Time.unscaledTime+1;_missionExitStep=33;break;
            case 33 when _world?.Active==true && _lifecycle!.WorldReady && _lifecycle.State.Released:
                if(UnityEngine.Object.FindObjectOfType<Hyperstrange.PBD.UI.Menus.EndLevelMenu>() is not {} restored || !restored.gameObject.activeInHierarchy) return;
                TravelCheck(ResultStatsText()==File.ReadAllText(Marker("mission-result-stats.json")),"late join restores the completed map's native results with host statistics");
                TravelMark("client-mission-results-checked");_missionExitStep=31;break;
            case 31 when _lifecycle!.State.Action==PBDMP.Network.TravelAction.Restart:
                _missionExitStep=4;break;
            case 4 when _world?.Active==true && _lifecycle!.WorldReady && _lifecycle.State.Released:
                TravelMark("client-mission-restarted");_missionExitStep=5;break;
            case 5 when Marked("mission-exit-client-go"):
                var p=JsonSerializer.Deserialize<float[]>(File.ReadAllText(Marker("mission-exit-position.json")))!;_missionExitPosition=new(p[0],p[1],p[2]);
                _missionExitKey=File.ReadAllText(Marker("mission-exit-key.txt"));_missionExitInteractive=bool.Parse(File.ReadAllText(Marker("mission-exit-interactive.txt")));
                _missionExitAway=Player.Instance.FeetPosition;Player.Instance.Teleport(_missionExitPosition,Vector3.zero,Vector3.zero);TravelMark("client-mission-at-exit");_missionExitStep=6;break;
            case 6 when Marked("mission-exit-client-leave"):
                TravelCheck(_world!.MissionStatus.Contains("partner"),"P2 receives shared exit waiting feedback");
                Player.Instance.Teleport(_missionExitAway,Vector3.zero,Vector3.zero);TravelMark("client-mission-left-exit");_missionExitStep=7;break;
            case 7 when Marked("mission-exit-client-return"):
                Player.Instance.Teleport(_missionExitPosition,Vector3.zero,Vector3.zero);
                if(_missionExitInteractive) _world!.ComponentForTest(_missionExitKey)!.Cast<Hyperstrange.PBD.Contraptions.Interactable>().HandleInteract(Player.Instance);
                _missionExitStep=8;break;
            case 8 when Marked("mission-exit-accepted"):
                if(UnityEngine.Object.FindObjectOfType<Hyperstrange.PBD.UI.Menus.EndLevelMenu>() is not {} finalResults || !finalResults.gameObject.activeInHierarchy) return;
                TravelCheck(_world!.ClientLevelStats.HasValue,"native authored exit opens the level-complete screen on P2");
                TravelMark("client-mission-exit-checked");_missionExitStep=9;break;
            case 9 when SessionLifecycle.Current==null && !SessionManager.SessionActive:
                TravelMark("client-pass");_missionExitStep=99;break;
        }
    }
}
