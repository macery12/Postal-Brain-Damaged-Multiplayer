using Hyperstrange.Base;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PBDMP.Plugin;

public sealed partial class MultiplayerBehaviour
{
    private int _reloadDoorStep;
    private Door? _reloadDoor;
    private Door[] _reloadDoors=Array.Empty<Door>();
    private float _reloadDoorAt;
    private uint _doorReloadEpoch;
    internal static bool DoorSoloGate;
    private bool _doorSoloReady,_doorSoloContinued;
    private Il2CppSystem.Action? _doorSoloHandler;
    private void RunDoorReloadTest()
    {
        if(_reloadDoorStep==99) return;
        if(_reloadDoorStep==0)
        {
            _port!.text=Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!;
            _doorSoloHandler=(Il2CppSystem.Action)(Action)(()=>_doorSoloReady=true);
            LevelController.OnStartLevelReady=Il2CppSystem.Delegate.Combine(LevelController.OnStartLevelReady,_doorSoloHandler).Cast<Il2CppSystem.Action>();
            GameManager.LoadLevel(TestLevel(),true);_reloadDoorStep=-1;return;
        }
        if(_reloadDoorStep==-1)
        {
            var loading=Object.FindObjectOfType<Hyperstrange.PBD.UI.Menus.LoadingMenu>();
            if(!_doorSoloContinued && loading!=null && loading.gameObject.activeInHierarchy && loading._sceneLoaded) {_doorSoloContinued=true;loading.OnAnyButtonDown();}
            if(DoorSoloGate && LevelController._instance!=null) {DoorSoloGate=false;LevelController._instance.OnAnyButtonDown();}
            if(!_doorSoloReady) return;
            LevelController.OnStartLevelReady=Il2CppSystem.Delegate.Remove(LevelController.OnStartLevelReady,_doorSoloHandler)?.Cast<Il2CppSystem.Action>();
            if(TravelRole=="host") Host();else {_address!.text="127.0.0.1";Join();}
            _reloadDoorStep=1;return;
        }
        if(_lifecycle is {NativeReady:false}) {_lifecycle.AutoContinue();return;}
        if(_reloadDoorStep<4 && _world?.Active!=true) return;
        if(TravelRole=="host")
        {
            TimeManager.SetApplicationTimeScale(1);
            switch(_reloadDoorStep)
            {
                case 1:
                    QuietNpcs();
                    _reloadDoors=Object.FindObjectsOfType<Door>(true).OrderBy(d=>Vector3.Distance(d.transform.position,Player.Instance.CenterOfMass)).Take(1).ToArray();
                    File.WriteAllLines(Marker("door-paths.txt"),_reloadDoors.Select(d=>DoorScenePath(d.transform)));
                    _reloadDoor=_reloadDoors.First();
                    foreach(var d in _reloadDoors) {d._isLocked=false;d._disableInteraction=false;d.FirstInteraction=false;}
                    File.WriteAllText(Marker("reload-door"),_world!.KeyForTest(_reloadDoor)!);TravelMark("host-ready");_reloadDoorStep=2;break;
                case 2 when Marked("client-door-baseline"):
                    GameManager.Load(null);_reloadDoorStep=3;break;
                case 3 when _lifecycle!.State.Released && Marked("client-door-restart-ready"):
                    QuietNpcs();_reloadDoors=new[]{_world!.ComponentForTest(File.ReadAllText(Marker("reload-door")))!.Cast<Door>()};
                    foreach(var d in _reloadDoors) d.HandleInteract(Player.Instance);TravelMark("host-door-open-request");_reloadDoorStep=4;break;
                case 4 when Marked("client-door-open-shared"):
                    TravelCheck(_session!.Snapshot.Connected,"host door opening after restart keeps both peers connected");_reloadDoorStep=5;break;
                case 5 when Marked("client-door-close-request") && Mathf.Abs(_reloadDoors[0]._desiredRotation)<1:
                    TravelCheck(_session!.Snapshot.Connected,"P2 closes the reloaded bedroom door on P1");TravelMark("host-door-close-shared");_reloadDoorStep=6;break;
                case 6 when Marked("client-door-reopen-request") && Mathf.Abs(_reloadDoors[0]._desiredRotation)>1:
                    TravelCheck(_session!.Snapshot.Connected,"P2 reopens the reloaded bedroom door on P1");TravelMark("host-door-reopen-shared");_reloadDoorStep=7;break;
                case 7 when Marked("client-door-reopen-shared"):
                    ResetSession();_reloadDoorStep=8;break;
                case 8 when Marked("client-pass"):
                    TravelMark("host-pass");_reloadDoorStep=99;break;
            }
        }
        else switch(_reloadDoorStep)
        {
            case 1 when _lifecycle!.WorldReady && _lifecycle.State.Released:
                _reloadDoor=_world!.ComponentForTest(File.ReadAllText(Marker("reload-door")))!.Cast<Door>();
                _doorReloadEpoch=_lifecycle.Epoch;
                TravelMark("client-door-baseline");_reloadDoorStep=2;break;
            case 2 when _lifecycle!.Epoch!=_doorReloadEpoch && _lifecycle.WorldReady && _lifecycle.State.Released:
                _reloadDoor=_world!.ComponentForTest(File.ReadAllText(Marker("reload-door")))!.Cast<Door>();
                if(Environment.GetEnvironmentVariable("PBDMP_DOOR_MISSING_HANDLE")=="1") _reloadDoor._handleTransform=null;
                TravelMark("client-door-restart-ready");_reloadDoorAt=Time.unscaledTime+3f;_reloadDoorStep=3;break;
            case 3 when Marked("host-door-open-request") && Time.unscaledTime>=_reloadDoorAt:
                TravelCheck(_session!.Snapshot.Connected && Mathf.Abs(_reloadDoor!._desiredRotation)>1,"native Neighborhood door interaction updates P2 and keeps the session connected");
                TravelMark("client-door-open-shared");_reloadDoorStep=4;break;
            case 4:
                _reloadDoor!.HandleInteract(Player.Instance);TravelMark("client-door-close-request");_reloadDoorAt=Time.unscaledTime+2f;_reloadDoorStep=5;break;
            case 5 when Marked("host-door-close-shared") && Time.unscaledTime>=_reloadDoorAt:
                TravelCheck(_session!.Snapshot.Connected && Mathf.Abs(_reloadDoor!._desiredRotation)<1,"P2 receives the shared closed bedroom door");
                _reloadDoor!.HandleInteract(Player.Instance);TravelMark("client-door-reopen-request");_reloadDoorAt=Time.unscaledTime+2f;_reloadDoorStep=6;break;
            case 6 when Marked("host-door-reopen-shared") && Time.unscaledTime>=_reloadDoorAt:
                TravelCheck(_session!.Snapshot.Connected && Mathf.Abs(_reloadDoor!._desiredRotation)>1,"P2 receives its shared reopened bedroom door");TravelMark("client-door-reopen-shared");_reloadDoorStep=7;break;
            case 7 when _session==null && SessionLifecycle.Current==null:
                TravelMark("client-pass");_reloadDoorStep=99;break;
        }
    }
    private static string DoorScenePath(Transform transform)
    {var names=new List<string>();for(var t=transform;t!=null;t=t.parent) names.Add(t.name);names.Reverse();return string.Join("/",names);}
}
