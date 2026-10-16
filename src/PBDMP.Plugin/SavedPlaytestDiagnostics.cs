using Hyperstrange.PBD;
using Hyperstrange.PBD.PlayerComponents;
using UnityEngine;

namespace PBDMP.Plugin;

public sealed partial class MultiplayerBehaviour
{
    private Vector3 _replayFeet,_replayLook;
    private void RunSavedPlaytestReplay()
    {
        if(_saveStep==99) return;
        File.WriteAllText(Marker(TravelRole+"-replay-state.txt"),$"step={_saveStep}; native={_lifecycle?.NativeReady}; ready={_lifecycle?.State.Released}; frozen={_lifecycle?.Frozen}; cancel={InputManager.CancelInput}; status={_saves?.Status}");
        if(_saveStep==0)
        {
            _port!.text=Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!;
            if(TravelRole=="host") {Host();SharedWorldPatches.DiagnosticFreeze=true;_saves!.RequestLoad(Environment.GetEnvironmentVariable("PBDMP_REPLAY_SLOT")??"auto-0");}
            else {_address!.text="127.0.0.1";Join();}
            _saveStep=1;return;
        }
        if(_lifecycle is {NativeReady:false}) {_lifecycle.AutoContinue();return;}
        if(TravelRole=="host" && _view!.Prompt.Open && _view.Prompt.Message.Contains("different partner",StringComparison.Ordinal))
        {_view.Prompt.ConfirmButton.onClick.Invoke();TravelMark("replay-partner-assigned");}
        if(TravelRole=="host") switch(_saveStep)
        {
            case 1 when _world?.Active==true && !_saves!.Busy:
                CheckJoinGameplayInput("P1 resuming the preserved autosave alone");
                var nativePointers=NativeSaveBridge.Entries().Select(e=>e.Component.Pointer).ToHashSet();
                File.WriteAllLines(Marker("physical-authorship.tsv"),UnityEngine.Object.FindObjectsOfType<Hyperstrange.PBD.Contraptions.PhysicalObject>(true).Select(p=>$"{WorldCatalogue.PathOf(p.transform)}|{p.GetIl2CppType().FullName}|0\tregistry={nativePointers.Contains(p.Pointer)}\tpooled={p.OriginalPoolable!=null}\tsafe={WorldCatalogue.SafeMotion(p.transform)}\tcatalogued={_world!.KeyForTest(p)!=null}\tposition={p.transform.position}"));
                if(Environment.GetEnvironmentVariable("PBDMP_CATALOGUE_REBUILD")=="1")
                {
                    var active=_world!.CatalogueForTest;var rebuilt=new WorldCatalogue();rebuilt.Build();
                    File.WriteAllLines(Marker("restored-catalogue-difference.tsv"),rebuilt.Identities.Where(p=>!active.Identities.ContainsKey(p.Key)).Select(p=>p.Key+"\t"+p.Value));
                    TravelCheck(rebuilt.Manifest==active.Manifest,"rebuilding the catalogue after a progressed save keeps authored identities stable");
                }
                if(Environment.GetEnvironmentVariable("PBDMP_REHOST_REPLAY")=="1")
                {
                    // Follow the actual reported sequence: restore, leave the
                    // session without reloading the map, then host it again.
                    File.WriteAllLines(Marker("before-rehost.tsv"),_world!.CatalogueForTest.Identities.Select(p=>p.Key+"\t"+p.Value));
                    ResetSession();_saveStep=20;break;
                }
                TravelMark("host-ready");_saveStep=2;break;
            case 20 when _session==null && SessionLifecycle.Current==null:
                Host();_saveStep=21;break;
            case 21 when _world?.Active==true && !_saves!.Busy:
                var before=File.ReadAllLines(Marker("before-rehost.tsv")).Select(s=>s.Split('\t')[0]).ToHashSet();
                var extras=_world.CatalogueForTest.Identities.Where(p=>!before.Contains(p.Key)).ToArray();
                File.WriteAllLines(Marker("rehost-extra.tsv"),extras.Select(p=>p.Key+"\t"+p.Value));
                TravelCheck(extras.Length==0 && _world.CatalogueForTest.Identities.Count==before.Count,"restoring then rehosting the same map retains the authored catalogue");
                var rehostNativePointers=NativeSaveBridge.Entries().Select(e=>e.Component.Pointer).ToHashSet();
                File.WriteAllLines(Marker("physical-authorship.tsv"),UnityEngine.Object.FindObjectsOfType<Hyperstrange.PBD.Contraptions.PhysicalObject>(true).Select(p=>$"{WorldCatalogue.PathOf(p.transform)}|{p.GetIl2CppType().FullName}|0\tregistry={rehostNativePointers.Contains(p.Pointer)}\tpooled={p.OriginalPoolable!=null}\tsafe={WorldCatalogue.SafeMotion(p.transform)}\tcatalogued={_world!.KeyForTest(p)!=null}\tposition={p.transform.position}"));
                TravelMark("host-ready");_saveStep=2;break;
            case 2 when _session!.Snapshot.Connected && _lifecycle!.State.Released && !_saves!.Busy && Marked("replay-client-ready"):
                _saveStageAt=Time.unscaledTime+3;_saveStep=3;break;
            case 3 when Time.unscaledTime>=_saveStageAt:
                CheckJoinGameplayInput("P1 after replaying the preserved campaign autosave");
                TravelCheck(Player.Instance.GetComponent<PlayerMovementComponent>().enabled,"P1 movement component remains enabled after replay");
                TravelMark("replay-host-input");_saveStep=4;break;
            case 4 when Marked("replay-client-input"):
                EndFrozenDiagnosticSession();_saveStep=5;break;
            case 5 when Marked("client-pass"):
                TravelMark("host-pass");_saveStep=99;break;
        }
        else switch(_saveStep)
        {
            case 1 when _lifecycle!.State.Released && _lifecycle.PersonalReady && !_saves!.Busy:
                TravelMark("replay-client-ready");_saveStageAt=Time.unscaledTime+3;_saveStep=2;break;
            case 2 when Time.unscaledTime>=_saveStageAt && Marked("replay-host-input"):
                CheckJoinGameplayInput("P2 after replaying the preserved campaign autosave");
                TravelCheck(Player.Instance.GetComponent<PlayerMovementComponent>().enabled,"P2 movement component remains enabled after replay");
                if(Environment.GetEnvironmentVariable("PBDMP_FOCUS_INPUT_TEST")=="1")
                {
                    _replayFeet=Player.Instance.FeetPosition;_replayLook=Player.Instance.WorldCamera.transform.eulerAngles;
                    _focusMoveSeen=false;_focusLookSeen=false;_focusLosses=0;_focusReturns=0;
                    TravelMark("client-focus-ready");_saveStep=10;break;
                }
                TravelMark("replay-client-input");_saveStep=3;break;
            case 10:
                if(Application.isFocused) {_focusMoveSeen|=InputManager.MoveAxis.sqrMagnitude>.01f;_focusLookSeen|=InputManager.LookAxis.sqrMagnitude>.01f;}
                if(!Marked("focus-switches-done")) break;
                CheckJoinGameplayInput("P2 actual controls after saved-world restoration and focus round trips");
                TravelCheck(_focusMoveSeen && _focusLookSeen,"restored P2 receives real WASD and mouse input");
                TravelCheck(Vector3.Distance(Player.Instance.FeetPosition,_replayFeet)>.05f && Quaternion.Angle(Quaternion.Euler(_replayLook),Player.Instance.WorldCamera.transform.rotation)>.5f,"restored P2 physically moves and turns in response to real keyboard/mouse input");
                TravelMark("replay-client-input");_saveStep=3;break;
            case 3 when _session==null:
                TravelMark("client-pass");_saveStep=99;break;
        }
    }
}
