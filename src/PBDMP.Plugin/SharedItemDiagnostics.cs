using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using Hyperstrange.PBD.PlayerComponents;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PBDMP.Plugin;

// -SharedItems (Prison): identical-player weapons, keys and items.
// Labels: pickups are taken through native Pickup.Interact on the local player; P2 is placed on the
// pickup by teleport; inventories are prepared with native AddItem/RemoveItem; death uses native HandleKill.
public sealed partial class MultiplayerBehaviour
{
    private int _itemStep;
    private float _itemAt;
    private int _itemAmmo;
    private ItemPickup? _itemTarget;
    private static bool SharedItemsTestEnabled=>Environment.GetEnvironmentVariable("PBDMP_SHARED_ITEMS_TEST")=="1";
    private static T Entity<T>(string name) where T:EntityId=>Resources.FindObjectsOfTypeAll<T>().First(e=>e.name==name);
    private static PlayerInventoryComponent LocalInventory=>Player.Instance.GetComponent<PlayerInventoryComponent>();
    private static int Quantity(string name)=>LocalInventory.GetItemQuantity(Resources.FindObjectsOfTypeAll<EntityId>().First(e=>e.name==name));
    private static ItemPickup FirstPickup(string item)=>Object.FindObjectsOfType<ItemPickup>().First(p=>!p._pickedUp && p._items.ToArray().Any(i=>i.EntityId?.name==item));

    private void RunSharedItemsTest()
    {
        if(_itemStep==99) return;
        if(_itemStep==0)
        {
            _port!.text=Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!;
            if(TravelRole=="host"){Host();GameManager.LoadLevel(TestLevel(),true);}else{_address!.text="127.0.0.1";Join();}
            _itemStep=1;return;
        }
        if(_lifecycle is {NativeReady:false}) {_lifecycle.AutoContinue();return;}
        if(_lifecycle is {NativeReady:true,LocalTutorialActive:true})
        {Player.Instance!.GetComponent<PlayerTutorialComponent>().OnUseDown();return;}
        if(_itemStep is <12 or >=20 && _world?.Active!=true) return;
        if(TravelRole=="host") SharedItemsHost();else SharedItemsClient();
    }

    private void SharedItemsHost()
    {
        if(_itemStep is <12 or >=20) Hyperstrange.Base.TimeManager.SetApplicationTimeScale(1);
        switch(_itemStep)
        {
            case 1:
                QuietNpcs();
                var lightning=FirstPickup("WEAPON_LightningGun");var machinegun=FirstPickup("WEAPON_MachineGun");
                TravelCheck(_world!.IsSharedPickupForTest(lightning) && _world.IsSharedPickupForTest(FirstPickup("KEY_Red")),"authored weapon and key pickups are shared items");
                TravelCheck(!_world.IsSharedPickupForTest(FirstPickup("AMMO_Shells")),"ammo pickups stay personal");
                foreach(var name in new[]{"WEAPON_LightningGun","WEAPON_LightningGun_Akimbo"}) if(Quantity(name)>0) LocalInventory.RemoveItem(Entity<WeaponId>(name),Quantity(name));
                foreach(var name in new[]{"WEAPON_MachineGun","WEAPON_MachineGun_Akimbo"}) if(Quantity(name)==0) LocalInventory.AddItem(Entity<WeaponId>(name),1);
                File.WriteAllText(Marker("item-lightning"),_world.SharedPickupKeyForTest(lightning)!);
                File.WriteAllText(Marker("item-machinegun"),_world.SharedPickupKeyForTest(machinegun)!);
                File.WriteAllText(Marker("item-red"),_world.SharedPickupKeyForTest(FirstPickup("KEY_Red"))!);
                File.WriteAllText(Marker("item-blue"),_world.SharedPickupKeyForTest(FirstPickup("KEY_Blue"))!);
                TravelMark("host-ready");_itemStep=2;break;
            case 2 when Marked("client-prepared") && _world!.Remote!=null:
                _itemTarget=_world!.SharedPickupForTest(File.ReadAllText(Marker("item-lightning")));
                _itemTarget!.Interact(Player.Instance);_itemStep=20;break;
            case 20 when _itemTarget!._pickedUp && Quantity("WEAPON_LightningGun")==1:
                TravelCheck(true,"P1's native pickup gives P1 the new gun");
                TravelMark("host-took-weapon");_itemStep=3;break;
            case 3 when Marked("client-took-key") && _world!.SharedPickupForTest(File.ReadAllText(Marker("item-red")))!._pickedUp && Quantity("KEY_Red")>0:
                TravelCheck(true,"P2's key pickup gives P1 the key and removes the pickup on P1");
                _itemAmmo=Quantity("AMMO_MachinegunBullets");
                TravelMark("host-mg-ready");_itemStep=4;break;
            case 4 when Marked("client-took-mg"):
                _itemAt=Time.unscaledTime+1.5f;_itemStep=5;break;
            case 5 when Time.unscaledTime>=_itemAt:
                TravelCheck(_world!.SharedPickupForTest(File.ReadAllText(Marker("item-machinegun")))!._pickedUp,"P2's pickup of an owned gun removes it on P1");
                TravelCheck(Quantity("AMMO_MachinegunBullets")==_itemAmmo,"an owned gun gives the partner nothing");
                TravelCheck(_world.PartnerNoticesForTest==1,"P1 sees a notice for P2's key but none for the owned gun");
                TravelMark("host-mg-checked");_itemStep=6;break;
            case 6 when Marked("client-dead") && _session?.Snapshot.RemotePose is {Alive:false}:
                _itemTarget=_world!.SharedPickupForTest(File.ReadAllText(Marker("item-blue")));
                _itemTarget!.Interact(Player.Instance);_itemStep=21;break;
            case 21 when _itemTarget!._pickedUp && Quantity("KEY_Blue")>0:
                TravelCheck(true,"P1 takes a key while P2 is dead");
                TravelMark("host-took-blue");_itemStep=7;break;
            case 7 when Marked("client-pass"):
                ResetSession();_itemStep=12;break;
            case 12:
                TravelMark("host-pass");_itemStep=99;break;
        }
    }

    private void SharedItemsClient()
    {
        switch(_itemStep)
        {
            case 1 when _lifecycle!.State.Released && _lifecycle.WorldReady:
                QuietNpcs();_itemStep=2;break;
            case 2 when Marked("host-ready") && Quantity("WEAPON_LightningGun")==0 && Quantity("WEAPON_MachineGun")==1:
                TravelCheck(true,"P2 matches P1's team weapons (gun removed and gun added on P1)");
                _itemAmmo=Quantity("AMMO_Brains");
                TravelMark("client-prepared");_itemStep=3;break;
            case 3 when Marked("host-took-weapon") && Quantity("WEAPON_LightningGun")==1 && _world!.SharedPickupForTest(File.ReadAllText(Marker("item-lightning")))!._pickedUp:
                TravelCheck(Quantity("AMMO_Brains")>_itemAmmo,"P2 receives P1's new gun with its native starting ammo");
                TravelCheck(_world.PartnerNoticesForTest==1,"P2 sees one native pickup notice naming P1");
                TravelCheck(true,"P1's weapon pickup disappears for P2");
                _itemTarget=_world.SharedPickupForTest(File.ReadAllText(Marker("item-red")));
                _itemAmmo=Quantity("KEY_Red");
                Player.Instance.Teleport(_itemTarget!.transform.position+Vector3.up*0.2f,Vector3.zero,Vector3.zero);
                _itemAt=Time.unscaledTime+0.5f;_itemStep=4;break;
            case 4 when Time.unscaledTime>=_itemAt:
                _itemTarget!.Interact(Player.Instance);_itemAt=Time.unscaledTime+0.6f;_itemStep=5;break;
            case 5 when _itemTarget!._pickedUp && Quantity("KEY_Red")>_itemAmmo:
                TravelCheck(true,"P2's key pickup is committed by P1 and gives P2 the key");
                TravelMark("client-took-key");_itemStep=6;break;
            case 5 when Time.unscaledTime>=_itemAt:
                _itemTarget!.Interact(Player.Instance);_itemAt=Time.unscaledTime+0.6f;break;
            case 6 when Marked("host-mg-ready"):
                _itemTarget=_world!.SharedPickupForTest(File.ReadAllText(Marker("item-machinegun")));
                _itemAmmo=Quantity("AMMO_MachinegunBullets");
                Player.Instance.Teleport(_itemTarget!.transform.position+Vector3.up*0.2f,Vector3.zero,Vector3.zero);
                _itemAt=Time.unscaledTime+0.5f;_itemStep=7;break;
            case 7 when Time.unscaledTime>=_itemAt:
                _itemTarget!.Interact(Player.Instance);_itemAt=Time.unscaledTime+0.6f;_itemStep=8;break;
            case 8 when _itemTarget!._pickedUp:
                TravelCheck(Quantity("AMMO_MachinegunBullets")>_itemAmmo,"the picker of an owned gun gets its native ammo");
                TravelMark("client-took-mg");_itemStep=9;break;
            case 8 when Time.unscaledTime>=_itemAt:
                _itemTarget!.Interact(Player.Instance);_itemAt=Time.unscaledTime+0.6f;break;
            case 9 when Marked("host-mg-checked"):
                Player.Instance.GetComponent<PlayerHitProcessorComponent>().HandleKill(new HitData{position=Player.Instance.CenterOfMass,direction=Vector3.forward,weakSpotMultiplier=1});
                TravelMark("client-dead");_itemStep=10;break;
            case 10 when Marked("host-took-blue") && Quantity("KEY_Blue")>0:
                TravelCheck(!Player.Instance.IsAlive,"a dead partner receives the key at once");
                _itemStep=11;break;
            case 11 when Player.Instance.IsAlive && _lifecycle!.State.Released:
                TravelCheck(Quantity("KEY_Blue")>0 && Quantity("WEAPON_LightningGun")==1,"shared gear is kept through the respawn");
                TravelMark("client-pass");_itemStep=12;break;
            case 12 when _session==null && SessionLifecycle.Current==null && !SessionManager.SessionActive:
                _itemStep=99;break;
        }
    }
}
