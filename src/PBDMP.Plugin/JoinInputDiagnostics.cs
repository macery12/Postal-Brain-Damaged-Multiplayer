using Hyperstrange.PBD;
using Hyperstrange.PBD.UI.Menus;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PBDMP.Plugin;

public sealed partial class MultiplayerBehaviour
{
    private int _joinInputStep;
    private bool _joinSoloReady,_joinSoloContinued;
    private Il2CppSystem.Action? _joinSoloHandler;
    private float _joinInputAt;
    private int _focusLosses,_focusReturns;
    private bool _focusMoveSeen,_focusLookSeen;
    public void OnApplicationFocus(bool focused)
    {
        if(Environment.GetEnvironmentVariable("PBDMP_FOCUS_INPUT_TEST")!="1") return;
        if(focused) _focusReturns++;else _focusLosses++;
    }
    private void RunJoinInputTest()
    {
        if(_joinInputStep==99) return;
        File.WriteAllText(Marker(TravelRole+"-input-state.txt"),$"step={_joinInputStep}, ready={_lifecycle?.NativeReady}, world={_lifecycle?.WorldReady}, released={_lifecycle?.State.Released}, paused={_lifecycle?.State.Paused}, scale={Time.timeScale}, input={InputManager.Instance.CurrentInputState}, stack={string.Join(",",InputManager.InputStateStack.ToArray())}, panel={_open}");
        if(_joinInputStep==0)
        {
            _port!.text=Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!;
            if(TravelRole=="host")
            {
                _joinSoloHandler=(Il2CppSystem.Action)(Action)(()=>_joinSoloReady=true);
                LevelController.OnStartLevelReady=Il2CppSystem.Delegate.Combine(LevelController.OnStartLevelReady,_joinSoloHandler).Cast<Il2CppSystem.Action>();
                GameManager.LoadLevel(TestLevel(),true);_joinInputStep=1;
            }
            else {SetPanel(true);_address!.text="127.0.0.1";_joinButton!.onClick.Invoke();_joinInputStep=2;}
            return;
        }
        if(TravelRole=="host") switch(_joinInputStep)
        {
            case 1:
                var loading=Object.FindObjectOfType<LoadingMenu>();
                if(!_joinSoloContinued && loading!=null && loading.gameObject.activeInHierarchy && loading._sceneLoaded) {_joinSoloContinued=true;loading.OnAnyButtonDown();}
                if(DoorSoloGate && LevelController._instance!=null) {DoorSoloGate=false;LevelController._instance.OnAnyButtonDown();}
                if(!_joinSoloReady) return;
                SyncPerformance.ApplyFrameRate();
                LevelController.OnStartLevelReady=Il2CppSystem.Delegate.Remove(LevelController.OnStartLevelReady,_joinSoloHandler)?.Cast<Il2CppSystem.Action>();
                SetPanel(true);_hostButton!.onClick.Invoke();SetPanel(false);
                // Retain the authored enemies so the normal full-world join barrier
                // runs; suppress attacks on the stationary diagnostic players.
                SharedWorldPatches.DiagnosticFreeze=true;
                CheckJoinGameplayInput("P1 after hosting through the multiplayer dialog");
                TravelMark("host-ready");_joinInputStep=2;break;
            case 2 when Marked("client-input-ready"):
                if(SyncPerformance.Enabled)
                {
                    if(!SyncPerformance.Ready) return;
                    var p95=SyncPerformance.Export(Marker("host-sync-performance.tsv"));
                    TravelCheck(p95<=4,"steady host shared-world work stays within a 4 ms p95 budget; actual="+p95.ToString("F3"));
                    TravelCheck(SyncPerformance.CycleP95<=200,"budgeted world sweeps retain timely updates (p95 <= 200 ms); actual="+SyncPerformance.CycleP95.ToString("F3"));
                }
                TravelCheck(_lifecycle!.State.Released && !_lifecycle.Frozen,"P1 releases the joining player after the current-world snapshot");
                if(!Marked("client-local-pause")) return;
                GameManager.PauseGame();_joinInputAt=Time.unscaledTime+1f;_joinInputStep=3;break;
            case 3 when Marked("client-input-paused") && Time.unscaledTime>=_joinInputAt:
                GameManager.DisableMenu();_joinInputAt=Time.unscaledTime+1f;_joinInputStep=4;break;
            case 4 when Time.unscaledTime>=_joinInputAt && !_lifecycle!.Frozen:
                CheckJoinGameplayInput("P1 after shared pause/resume");_joinInputStep=5;break;
            case 5 when Marked("client-input-resumed"):
                ResetSession();SharedWorldPatches.DiagnosticFreeze=false;_joinInputStep=6;break;
            case 6 when Marked("client-pass"):
                TravelMark("host-pass");_joinInputStep=99;break;
        }
        else switch(_joinInputStep)
        {
            case 2 when _lifecycle!.NativeReady && _lifecycle.WorldReady && _lifecycle.State.Released && !_lifecycle.Frozen:
                _joinInputAt=Time.unscaledTime+1f;_joinInputStep=3;break;
            case 3 when Time.unscaledTime>=_joinInputAt:
                TravelCheck(!_open && Time.timeScale>0,"P2 closes the main-menu multiplayer dialog and resumes time after joining");
                CheckJoinGameplayInput("P2 immediately after joining P1 from the main menu");
                SyncPerformance.ApplyFrameRate();
                GameManager.PauseGame();TravelMark("client-local-pause");
                TravelMark("client-input-ready");_joinInputStep=4;break;
            case 4 when _lifecycle!.State.Paused:
                TravelCheck(Time.timeScale==0 && !InputManager.Instance._gameplayKeyboardMap.enabled && !InputManager.Instance._gameplayMouseMap.enabled,"P1 shared pause disables P2 gameplay maps");
                GameManager.DisableMenu();
                TravelCheck(!InputManager.Instance._gameplayKeyboardMap.enabled && !InputManager.Instance._gameplayMouseMap.enabled,"closing P2's menu preserves P1's shared pause lock");
                TravelMark("client-input-paused");_joinInputStep=5;break;
            case 5 when !_lifecycle!.State.Paused && !_lifecycle.Frozen:
                CheckJoinGameplayInput("P2 after P1 resumes the shared pause");
                SetPanel(true);SetPanel(false);CheckJoinGameplayInput("P2 after opening/closing F8");
                GameManager.PauseGame();_joinInputAt=Time.unscaledTime+1f;_joinInputStep=6;break;
            case 6 when Time.unscaledTime>=_joinInputAt:
                GameManager.DisableMenu();_joinInputAt=Time.unscaledTime+1f;_joinInputStep=7;break;
            case 7 when Time.unscaledTime>=_joinInputAt:
                CheckJoinGameplayInput("P2 after opening/closing its local pause menu");
                if(Environment.GetEnvironmentVariable("PBDMP_FOCUS_INPUT_TEST")=="1")
                {_focusLosses=0;_focusReturns=0;TravelMark("client-focus-ready");_joinInputStep=9;break;}
                TravelMark("client-input-resumed");_joinInputStep=8;break;
            case 9:
                if(Application.isFocused)
                {
                    _focusMoveSeen|=InputManager.MoveAxis.sqrMagnitude>0.01f;
                    _focusLookSeen|=InputManager.LookAxis.sqrMagnitude>0.01f;
                }
                if(!Marked("focus-switches-done")) break;
                TravelCheck(_focusLosses>=5 && _focusReturns>=5,"P2 receives repeated native window focus loss/return callbacks");
                CheckJoinGameplayInput("P2 after five real window focus round trips");
                TravelCheck(_focusMoveSeen && _focusLookSeen,"P2 reads actual movement and mouse-look input after switching windows");
                TravelMark("client-input-resumed");_joinInputStep=8;break;
            case 8 when _session==null && SessionLifecycle.Current==null:
                TravelMark("client-pass");_joinInputStep=99;break;
        }
    }
    private void CheckJoinGameplayInput(string context)
    {
        var input=InputManager.Instance;
        TravelCheck(input.CurrentInputState==InputManager.InputState.GAMEPLAY && input._gameplayKeyboardMap.enabled && input._gameplayMouseMap.enabled && Time.timeScale>0,
            context+" accepts gameplay keyboard/mouse input; actual="+input.CurrentInputState+", keyboard="+input._gameplayKeyboardMap.enabled+", mouse="+input._gameplayMouseMap.enabled+", stack="+string.Join(",",InputManager.InputStateStack.ToArray()));
    }
}
