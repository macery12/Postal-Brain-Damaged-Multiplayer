using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using Hyperstrange.PBD.UI.Menus;
using UnityEngine;

namespace PBDMP.Plugin;

public sealed partial class MultiplayerBehaviour
{
    private int _reportedStep,_reportedCount;
    private float _reportedAt;
    private CodexPickup? _reportedPoster;
    private string ReportedMode=>Environment.GetEnvironmentVariable("PBDMP_PRESENTATION_TEST")!;
    private bool PauseVisible=>UnityEngine.Object.FindObjectsOfType<PauseMenu>(true).Any(m=>m.gameObject.activeInHierarchy);
    private void RunReportedPresentationTest()
    {
        if(_reportedStep==99) return;
        File.WriteAllText(Marker(TravelRole+"-reported-state.txt"),$"mode={ReportedMode}, step={_reportedStep}, ready={_lifecycle?.State.Released}, frozen={_lifecycle?.Frozen}, pause={PauseVisible}, caption={_view?.Hud.HoldText}");
        if(_reportedStep==0)
        {
            _port!.text=Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!;
            if(TravelRole=="host") {Host();SharedWorldPatches.DiagnosticFreeze=true;GameManager.LoadLevel(TestLevel(),true);}
            else {_address!.text="127.0.0.1";Join();}
            _reportedStep=1;return;
        }
        if(_lifecycle is {NativeReady:false}) {_lifecycle.AutoContinue();return;}
        if(TravelRole=="host") switch(_reportedStep)
        {
            case 1 when _world?.Active==true:
                QuietNpcs();
                _reportedPoster=UnityEngine.Object.FindObjectsOfType<CodexPickup>(true).Where(p=>p.gameObject.scene.IsValid() && p.gameObject.activeInHierarchy && !p._pickedUp).OrderBy(p=>WorldCatalogue.PathOf(p.transform)).First();
                File.WriteAllText(Marker("reported-poster"),WorldCatalogue.TransformKey(_reportedPoster.transform));
                _reportedCount=LevelController._instance.GetComponent<CodexController>().CollectedEntriesCount;
                TravelMark("host-ready");_reportedStep=2;break;
            case 2 when _lifecycle!.State.Released && Marked("reported-client-ready"):
                GameManager.DisableMenu();
                if(ReportedMode=="pause") GameManager.PauseGame();
                TravelMark("reported-go");_reportedStep=3;break;
            case 3 when Marked("reported-client-action"):
                _reportedAt=Time.unscaledTime+1;_reportedStep=4;break;
            case 4 when Time.unscaledTime>=_reportedAt:
                if(ReportedMode=="posters")
                {
                    TravelCheck(_reportedPoster!._pickedUp,"P2 collecting an authored poster removes its shared copy on P1");
                    TravelCheck(LevelController._instance.GetComponent<CodexController>().CollectedEntriesCount==_reportedCount+1,"P2 poster collection increments the shared native result exactly once");
                    _reportedPoster.Interact(Player.Instance);
                    TravelCheck(LevelController._instance.GetComponent<CodexController>().CollectedEntriesCount==_reportedCount+1,"duplicate poster interaction does not double-count");
                    _saves!.RequestSave("manual-posters","Shared poster regression");_reportedStep=10;break;
                }
                GameManager.DisableMenu();TravelMark("reported-host-resumed");_reportedStep=5;break;
            case 5 when Marked("reported-client-own-menu"):
                GameManager.PauseGame();_reportedAt=Time.unscaledTime+1;_reportedStep=6;break;
            case 6 when Time.unscaledTime>=_reportedAt:
                GameManager.DisableMenu();TravelMark("reported-host-resumed-again");_reportedStep=7;break;
            case 7 when Marked("reported-client-done"):
                EndFrozenDiagnosticSession();_reportedStep=8;break;
            case 8 when Marked("client-pass"):
                TravelMark("host-pass");_reportedStep=99;break;
            case 10 when !_saves!.Busy:
                var save=_saves.Store.Read("manual-posters").Save;
                TravelCheck(save.World.Any(f=>f.Kind==PBDMP.Network.WorldKind.Mechanism && f.Flags==6 && f.Value==1),"disk save contains the shared poster record");
                _saves.RequestLoad("manual-posters");_reportedStep=11;break;
            case 11 when !_saves!.Busy && _lifecycle!.State.Released:
                _reportedPoster=UnityEngine.Object.FindObjectsOfType<CodexPickup>(true).Single(p=>WorldCatalogue.TransformKey(p.transform)==File.ReadAllText(Marker("reported-poster")));
                TravelCheck(_reportedPoster._pickedUp && LevelController._instance.GetComponent<CodexController>().CollectedEntriesCount==_reportedCount+1,"poster state and result count survive disk rollback on P1");
                TravelMark("reported-restored");_reportedStep=12;break;
            case 12 when Marked("reported-client-left") && !_session!.Snapshot.Connected:
                TravelMark("reported-rejoin");_reportedStep=7;break;
        }
        else switch(_reportedStep)
        {
            case 1 when _world?.Active==true && _lifecycle!.State.Released:
                _reportedPoster=UnityEngine.Object.FindObjectsOfType<CodexPickup>(true).Single(p=>WorldCatalogue.TransformKey(p.transform)==File.ReadAllText(Marker("reported-poster")));
                _reportedCount=LevelController._instance.GetComponent<CodexController>().CollectedEntriesCount;
                TravelMark("reported-client-ready");_reportedStep=2;break;
            case 2 when Marked("reported-go"):
                if(ReportedMode=="pause") {_reportedAt=Time.unscaledTime+1;_reportedStep=3;break;}
                Player.Instance.Teleport(_reportedPoster!.transform.position+Vector3.back,Vector3.zero,Vector3.zero);
                _reportedAt=Time.unscaledTime+.6f;_reportedStep=20;break;
            case 20 when Time.unscaledTime>=_reportedAt:
                _reportedPoster!.Interact(Player.Instance);TravelMark("reported-client-action");_reportedStep=21;break;
            case 21 when Marked("reported-restored"):
                _reportedPoster=UnityEngine.Object.FindObjectsOfType<CodexPickup>(true).Single(p=>WorldCatalogue.TransformKey(p.transform)==File.ReadAllText(Marker("reported-poster")));
                TravelCheck(_reportedPoster._pickedUp && LevelController._instance.GetComponent<CodexController>().CollectedEntriesCount==_reportedCount+1,"P2 poster state/results survive disk restoration");
                ResetSession();TravelMark("reported-client-left");_reportedStep=22;break;
            case 22 when Marked("reported-rejoin") && SessionLifecycle.Current==null:
                _address!.text="127.0.0.1";Join();_reportedStep=23;break;
            case 23 when _world?.Active==true && _lifecycle!.State.Released:
                _reportedPoster=UnityEngine.Object.FindObjectsOfType<CodexPickup>(true).Single(p=>WorldCatalogue.TransformKey(p.transform)==File.ReadAllText(Marker("reported-poster")));
                TravelCheck(_reportedPoster._pickedUp && LevelController._instance.GetComponent<CodexController>().CollectedEntriesCount==_reportedCount+1,"late joining restores shared poster state and native results");
                TravelMark("reported-client-done");_reportedStep=8;break;
            case 3 when Time.unscaledTime>=_reportedAt:
                TravelCheck(PauseVisible && _view!.Hud.HoldText=="Host has paused","P1 pause opens P2's native pause screen with its own caption");
                if(Environment.GetEnvironmentVariable("PBDMP_FOCUS_INPUT_TEST")=="1" && !Marked("focus-switches-done")) {TravelMark("client-focus-ready");break;}
                ScreenCapture.CaptureScreenshot(Marker("client-host-paused.png"));
                GameManager.DisableMenu();
                TravelCheck(PauseVisible && _lifecycle!.Frozen,"P2 cannot dismiss the pause screen owned by P1");
                TravelMark("reported-client-action");_reportedStep=4;break;
            case 4 when Marked("reported-host-resumed") && !_lifecycle!.State.Paused:
                _reportedAt=Time.unscaledTime+1;_reportedStep=40;break;
            case 40 when Time.unscaledTime>=_reportedAt:
                TravelCheck(!PauseVisible && _view!.Hud.HoldText.Length==0,"P1 resume closes its owned P2 pause screen and caption");
                CheckJoinGameplayInput("P2 after automatic host-pause close");
                GameManager.PauseGame();TravelMark("reported-client-own-menu");_reportedStep=5;break;
            case 5 when Marked("reported-host-resumed-again") && !_lifecycle!.State.Paused:
                TravelCheck(PauseVisible,"P1 resuming preserves a pause menu that P2 opened independently");
                GameManager.DisableMenu();CheckJoinGameplayInput("P2 after closing its independent menu");
                TravelMark("reported-client-done");_reportedStep=8;break;
            case 8 when SessionLifecycle.Current==null:
                TravelMark("client-pass");_reportedStep=99;break;
        }
    }
}
