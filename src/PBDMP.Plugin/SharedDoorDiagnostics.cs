using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using Hyperstrange.PBD.PlayerComponents;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PBDMP.Plugin;

public sealed partial class MultiplayerBehaviour
{
    private int _doorTestStep;
    private Interactable? _keyDoor;
    private readonly Dictionary<string,int> _keyCounts=new();
    private float _doorTestAt;
    private SlidingDoors? _proximityTestDoor;
    private Door? _meleeTestDoor;
    private Vector3 _doorTestReturnPoint;
    private void RunDoorTest()
    {
        if(_doorTestStep==99) return;
        if(_doorTestStep==0)
        {
            _port!.text=Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!;
            if(TravelRole=="host"){Host();GameManager.LoadLevel(TestLevel(),true);}else{_address!.text="127.0.0.1";Join();}
            _doorTestStep=1;return;
        }
        if(_lifecycle is {NativeReady:false}) {_lifecycle.AutoContinue();return;}
        if(_doorTestStep<8 && _world?.Active!=true) return;
        if(TravelRole=="host") DoorTestHost();else DoorTestClient();
    }
    private void DoorTestHost()
    {
        if(_doorTestStep<11) Hyperstrange.Base.TimeManager.SetApplicationTimeScale(1);
        switch(_doorTestStep)
        {
            case 1:
                QuietNpcs();
                Hyperstrange.Base.TimeManager.SetApplicationTimeScale(1);
                _keyDoor=Object.FindObjectsOfType<Interactable>(true).FirstOrDefault(d=>_world!.IsDoor(d) && d._requiredItemsComponent?._requiredItemsQuantity.Count>0);
                TravelCheck(_keyDoor!=null,"campaign fixture contains a native required-item door");
                var inventory=Player.Instance.GetComponent<PlayerInventoryComponent>();
                foreach(var item in _keyDoor!._requiredItemsComponent._requiredItemsQuantity)
                {var count=inventory.GetItemQuantity(item.Key);if(count>0) inventory.RemoveItem(item.Key,count);_keyCounts[item.Key.ID]=0;}
                _keyDoor.FirstInteraction=true;
                if(_keyDoor.TryCast<Door>() is {} hinged) hinged._disableInteraction=false;
                Plugin.Logger.LogInfo($"KEY TEST selected {_keyDoor.name} {_keyDoor.GetIl2CppType().Name}; first={_keyDoor.FirstInteraction}");
                File.WriteAllText(Marker("key-door"),_world!.KeyForTest(_keyDoor)!);
                var keyItems=new List<string>();foreach(var item in _keyDoor._requiredItemsComponent._requiredItemsQuantity) keyItems.Add(item.Key.ID+"\t"+item.Value);
                File.WriteAllLines(Marker("key-items"),keyItems);
                TravelMark("host-ready");_doorTestStep=2;break;
            case 2 when Marked("client-key-request") && !_keyDoor!.FirstInteraction:
                foreach(var item in _keyDoor._requiredItemsComponent._requiredItemsQuantity)
                    TravelCheck(Player.Instance.GetComponent<PlayerInventoryComponent>().GetItemQuantity(item.Key)==0,"P2 required-item interaction never spends or copies P1's keys");
                TravelMark("host-key-committed");_doorTestStep=3;break;
            case 3 when Marked("client-key-shared"):
                GameManager.LoadLevel(DoorTestLevel("LEVEL_NLM_DeathStar"),true);_doorTestStep=4;break;
            case 4:
                QuietNpcs();
                foreach(var d in Object.FindObjectsOfType<SlidingDoors>(true)) Plugin.Logger.LogInfo($"PROXIMITY TEST door {d.name}: active={d.gameObject.activeInHierarchy}, useTrigger={d._useTrigger}, trigger={d._playerTrigger!=null}");
                _proximityTestDoor=Object.FindObjectsOfType<SlidingDoors>().Where(d=>d._useTrigger && d._playerTrigger!=null).OrderByDescending(d=>Vector3.Distance(d.transform.position,Player.Instance.FeetPosition)).First();
                _proximityTestDoor.Close();_proximityTestDoor.SetLockState(false);
                File.WriteAllText(Marker("proximity-door"),_world!.KeyForTest(_proximityTestDoor)!);_doorTestStep=5;break;
            case 5 when Marked("client-proximity-inside") && _proximityTestDoor!._isOpen:
                TravelCheck(true,"P2 proximity opens the authoritative native sliding door without P1 nearby");
                TravelMark("host-proximity-open");_doorTestStep=6;break;
            case 6 when Marked("client-proximity-outside") && !_proximityTestDoor!._isOpen:
                TravelCheck(true,"P2 leaving the trigger closes the authoritative native sliding door");
                TravelMark("host-proximity-closed");_doorTestStep=7;break;
            case 7 when Marked("client-proximity-closed"):
                GameManager.LoadLevel(DoorTestLevel("LEVEL_AD_Prison"),true);_doorTestStep=8;break;
            case 8:
                QuietNpcs();_meleeTestDoor=Object.FindObjectsOfType<Door>().First(AcceptsNativeKick);
                _meleeTestDoor.ResetState();_meleeTestDoor._isLocked=false;_meleeTestDoor._disableInteraction=false;_meleeTestDoor.FirstInteraction=false;
                File.WriteAllText(Marker("melee-door"),_world!.KeyForTest(_meleeTestDoor)!);_doorTestStep=9;break;
            case 9 when Marked("client-door-melee") && Mathf.Abs(_meleeTestDoor!._desiredRotation)>1f:
                TravelCheck(true,"P2 native kick hit opens a hinged door through P1 authority");
                TravelMark("host-door-melee");_doorTestStep=10;break;
            case 10 when Marked("client-door-melee-shared"):
                ResetSession();_doorTestStep=11;break;
            case 11 when Marked("client-pass"):
                TravelMark("host-pass");_doorTestStep=99;break;
        }
    }
    private void DoorTestClient()
    {
        switch(_doorTestStep)
        {
            case 1 when _lifecycle!.State.Released && _lifecycle.WorldReady:
                QuietNpcs();
                _keyDoor=_world!.ComponentForTest(File.ReadAllText(Marker("key-door")))!.Cast<Interactable>();
                var inventory=Player.Instance.GetComponent<PlayerInventoryComponent>();
                foreach(var item in _keyDoor._requiredItemsComponent._requiredItemsQuantity)
                {inventory.AddItem(item.Key,item.Value);_keyCounts[item.Key.ID]=inventory.GetItemQuantity(item.Key);}
                TravelCheck(PlayerSessionState.FindNearby(Player.Instance,_keyDoor.transform.position,out var point),"key fixture has grounded, clear space near its door");
                Player.Instance.Teleport(point,Vector3.zero,Vector3.zero);_doorTestAt=Time.unscaledTime+1f;_doorTestStep=2;break;
            case 2 when Time.unscaledTime>=_doorTestAt && !_lifecycle!.Frozen:
                TravelCheck(_keyDoor!.FirstInteraction,"guest receives the current locked first-interaction state");
                Plugin.Logger.LogInfo($"KEY TEST requester distance={Vector3.Distance(Player.Instance.FeetPosition,_keyDoor.transform.position)}; can={_keyDoor.CanBeInteractedWith(Player.Instance)}; states={_keyDoor._requiredStatesComponent?.RequirementsMet()}; missing={_keyDoor._requiredItemsComponent.GetLackingItems(Player.Instance).Count}");
                _keyDoor.HandleInteract(Player.Instance);TravelMark("client-key-request");_doorTestStep=3;break;
            case 3 when Marked("host-key-committed") && !_keyDoor!.FirstInteraction:
                TravelCheck(true,"requester's native required items unlock the shared door on both peers");
                foreach(var item in _keyDoor._requiredItemsComponent._requiredItemsQuantity)
                    TravelCheck(Player.Instance.GetComponent<PlayerInventoryComponent>().GetItemQuantity(item.Key)<=_keyCounts[item.Key.ID],"P2 retains native requester-only required-item consumption policy");
                TravelMark("client-key-shared");_doorTestStep=4;break;
            case 4 when File.Exists(Marker("proximity-door")) && _lifecycle!.WorldReady && _lifecycle.State.Released:
                _proximityTestDoor=_world!.ComponentForTest(File.ReadAllText(Marker("proximity-door")))!.Cast<SlidingDoors>();
                _doorTestReturnPoint=Player.Instance.FeetPosition;
                var bounds=_proximityTestDoor._playerTrigger.GetComponents<Collider>().First(c=>c.enabled).bounds;
                Player.Instance.Teleport(bounds.center-Vector3.up*0.9f,Vector3.zero,Vector3.zero);
                TravelMark("client-proximity-inside");_doorTestStep=5;break;
            case 5 when Marked("host-proximity-open") && _proximityTestDoor!._isOpen:
                TravelCheck(true,"guest receives shared proximity-door opening");
                Player.Instance.Teleport(_doorTestReturnPoint,Vector3.zero,Vector3.zero);TravelMark("client-proximity-outside");_doorTestStep=6;break;
            case 6 when Marked("host-proximity-closed") && !_proximityTestDoor!._isOpen:
                TravelCheck(true,"guest receives shared proximity-door closing");
                TravelMark("client-proximity-closed");_doorTestStep=7;break;
            case 7 when File.Exists(Marker("melee-door")) && _lifecycle!.WorldReady && _lifecycle.State.Released:
                _meleeTestDoor=_world!.ComponentForTest(File.ReadAllText(Marker("melee-door")))!.Cast<Door>();
                TravelCheck(PlayerSessionState.FindNearby(Player.Instance,_meleeTestDoor.transform.position,out var meleePoint),"kick-door fixture has a valid nearby player position");
                Player.Instance.Teleport(meleePoint,Vector3.zero,Vector3.zero);_doorTestAt=Time.unscaledTime+1f;_doorTestStep=8;break;
            case 8 when Time.unscaledTime>=_doorTestAt:
                var unarmed=Resources.FindObjectsOfTypeAll<WeaponId>().First(w=>w.name=="WEAPON_NoWeapon");
                _meleeTestDoor!.Hit(new HitData{dealer=Player.Instance.Cast<IAiActor>(),originEntity=unarmed,position=Player.Instance.CenterOfMass,damage=10,weakSpotMultiplier=1,isQuickMelee=true,isPowerful=true});
                TravelMark("client-door-melee");_doorTestStep=9;break;
            case 9 when Marked("host-door-melee") && Mathf.Abs(_meleeTestDoor!._desiredRotation)>1f:
                TravelCheck(true,"guest receives shared hinged-door kick motion");
                TravelMark("client-door-melee-shared");_doorTestStep=10;break;
            case 10 when _session==null && SessionLifecycle.Current==null && !SessionManager.SessionActive:
                TravelMark("client-pass");_doorTestStep=99;break;
        }
    }
    private static bool AcceptsNativeKick(Door door)
    {foreach(var entity in door._openerEntities) if(entity.name=="WEAPON_NoWeapon") return true;return false;}
    private static LevelId DoorTestLevel(string name)
    {foreach(var level in LevelManager.AllLevels) if(level.name==name) return level;throw new InvalidOperationException("Missing door fixture map: "+name);}
}
