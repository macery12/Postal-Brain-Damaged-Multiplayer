using Hyperstrange.PBD;
using Hyperstrange.Base;
using Hyperstrange.PBD.PlayerComponents;
using PBDMP.Network;
using UnityEngine;

namespace PBDMP.Plugin;

// Separate from gameplay: native equips/attacks plus explicit presentation-state transport fixtures.
public sealed partial class MultiplayerBehaviour
{
    private int _wpStep,_wpIndex,_wpMode;
    private float _wpAt,_wpStarted,_wpPlayback;
    private WeaponId[]? _wpWeapons;
    private uint _wpLocalShot,_wpPeerShot,_wpLocalAction,_wpPeerAction;
    private PlayerAnimation? _wpOverride;
    private PlayerAnimation CapturePlayerPresentation(Player player)=>_wpOverride??_weaponObserver.Capture(player);
    private void RunWeaponPresentationTest()
    {
        if(_wpStep==99)return;
        if(_wpStarted==0)_wpStarted=Time.unscaledTime;
        if(Time.unscaledTime-_wpStarted>300)throw new TimeoutException($"Weapon presentation stalled: {_wpStep}/{_wpIndex}/{_wpMode}");
        File.WriteAllText(Marker(TravelRole+"-weapon-state.txt"),$"{_wpStep}/{_wpIndex}/{_wpMode}: {Player.Instance?.CurrentWeapon?.WeaponId?.name}; {_lifecycle?.Status}");
        if(_wpStep==0)
        {
            _port!.text=Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!;
            if(TravelRole=="host"){Host();SharedWorldPatches.DiagnosticFreeze=true;GameManager.LoadLevel(TestLevel(),true);}
            else {_address!.text="127.0.0.1";Join();}
            _wpStep=1;return;
        }
        if(_lifecycle is {NativeReady:false}){_lifecycle.AutoContinue();return;}
        if(_lifecycle?.LocalTutorialActive==true){Player.Instance!.GetComponent<PlayerTutorialComponent>().OnUseDown();return;}
        var player=Player.Instance;if(player==null)return;
        if(_wpStep==1 && _world?.Active==true)
        {
            if(TravelRole=="host")TravelMark("host-ready");
            if(!_session!.Snapshot.Connected || !_lifecycle!.State.Released)return;
            SetPanel(false);GameManager.DisableMenu();TimeManager.SetApplicationTimeScale(1);
            QuietNpcs();
            _wpWeapons=Resources.FindObjectsOfTypeAll<WeaponId>().Where(w=>w.Category==DLCCategory.NONE && w._pickupPrefab!=null && w.PlayerWeaponPrefab!=null)
                .GroupBy(w=>w.ID).Select(g=>g.First()).OrderBy(w=>w.name).ToArray();
            var inventory=player.GetComponent<PlayerInventoryComponent>();
            foreach(var weapon in _wpWeapons){inventory.AddItem(weapon,1);foreach(var ammo in weapon.AmmoTypes)inventory.AddItem(ammo,100);}
            _wpStep=2;return;
        }
        if(_wpStep==2)
        {
            if(_wpIndex>=_wpWeapons!.Length){_wpStep=6;_wpMode=0;return;}
            var inventory=player.GetComponent<PlayerInventoryComponent>();var state=new PlayerInventoryComponent.SaveData(inventory);
            state.CurrentWeapon=_wpWeapons[_wpIndex].ID;state.LastWeapon=state.CurrentWeapon;inventory.LoadState(state);
            TravelMark(TravelRole+"-equipped-"+_wpIndex);_wpAt=Time.unscaledTime+1.2f;_wpStep=3;return;
        }
        if(_wpStep==3 && Time.unscaledTime>=_wpAt)
        {
            var id=_wpWeapons![_wpIndex];var peer=TravelRole=="host"?"client":"host";
            if(!Marked(peer+"-equipped-"+_wpIndex) || _session!.Snapshot.RemotePose?.Weapon!=id.ID || _remoteVisual?.WeaponKey!=id.ID || !_remoteVisual.HasWeapon)return;
            TravelCheck(_remoteVisual.GripError<.035f && _remoteVisual.SupportError<.035f,"both-direction native equip and reachable grips: "+id.name);
            PoseCapture.Render(_remoteVisual.Root,"quarter",Marker(TravelRole+"-"+id.name+"-connected.png"));
            _wpPeerShot=_session.Snapshot.RemotePose.ShotSequence;_wpLocalShot=_weaponObserver.ShotSequence;
            _wpPeerAction=_session.Snapshot.RemotePose.Animation.Sequence;_wpLocalAction=_weaponObserver.Capture(player).Sequence;
            TravelMark(TravelRole+"-observed-"+_wpIndex);_wpStep=4;return;
        }
        if(_wpStep==4)
        {
            var peer=TravelRole=="host"?"client":"host";if(!Marked(peer+"-observed-"+_wpIndex))return;
            if(_wpWeapons![_wpIndex].name is "WEAPON_Pistol" or "WEAPON_Shotgun" or "WEAPON_Shovel")
            {if(player.CurrentWeapon.TryCast<PlayerWeaponShovel>() is {} saw){saw.OnFireDown();saw.OnFireUp();}else player.CurrentWeapon.PlayAttackAnimation(PlayerWeapon.ANIM_SHOOT);_wpAt=Time.unscaledTime+4;_wpStep=5;}
            else {_wpIndex++;_wpStep=2;}
            return;
        }
        if(_wpStep==5)
        {
            var local=_weaponObserver.Capture(player);var peer=_session!.Snapshot.RemotePose!;
            var localChanged=_weaponObserver.ShotSequence!=_wpLocalShot || local.Sequence!=_wpLocalAction;
            var peerChanged=peer.ShotSequence!=_wpPeerShot || peer.Animation.Sequence!=_wpPeerAction;
            if((!localChanged || !peerChanged) && Time.unscaledTime<_wpAt)return;
            TravelCheck(localChanged && peerChanged,"native primary attack callback traverses both directions: "+_wpWeapons![_wpIndex].name);
            TravelMark(TravelRole+"-attacked-"+_wpIndex);_wpStep=51;return;
        }
        if(_wpStep==51)
        {
            if(!Marked((TravelRole=="host"?"client":"host")+"-attacked-"+_wpIndex))return;
            _wpIndex++;_wpStep=2;return;
        }
        if(_wpStep==6)
        {
            if(_wpMode>=8){_wpOverride=null;TravelMark(TravelRole+"-poses-complete");_wpStep=8;return;}
            var flags=_wpMode switch {0=>PlayerActions.Melee|PlayerActions.Loop,1=>PlayerActions.Blocking,2=>PlayerActions.Shielding,
                3=>PlayerActions.Charging|PlayerActions.Alternate,4=>PlayerActions.Firing|PlayerActions.Alternate,7=>PlayerActions.Kicking,_=>PlayerActions.None};
            _wpOverride=new(_wpMode==5?PlayerMotion.Airborne:_wpMode==6?PlayerMotion.Sliding:PlayerMotion.Grounded,flags,(uint)(100+_wpMode),.4f,_wpMode==3?.75f:0);
            TravelMark(TravelRole+"-pose-"+_wpMode);_wpAt=Time.unscaledTime+.4f;_wpStep=7;return;
        }
        if(_wpStep==7 && Time.unscaledTime>=_wpAt)
        {
            if(_session!.Snapshot.RemotePose?.Animation!=_wpOverride.GetValueOrDefault())return;
            TravelCheck(true,"presentation state roundtrip both directions: "+_wpOverride.GetValueOrDefault().Actions+"/"+_wpOverride.GetValueOrDefault().Motion);
            PoseCapture.Render(_remoteVisual!.Root,"quarter",Marker(TravelRole+"-action-"+_wpMode+".png"));
            TravelMark(TravelRole+"-pose-observed-"+_wpMode);_wpStep=71;return;
        }
        if(_wpStep==71)
        {if(!Marked((TravelRole=="host"?"client":"host")+"-pose-observed-"+_wpMode))return;_wpMode++;_wpStep=6;return;}
        if(_wpStep==8)
        {
            if(!Marked("host-poses-complete") || !Marked("client-poses-complete"))return;
            if(TravelRole=="host")_lifecycle!.HostPauseRequest(true);
            _wpAt=Time.unscaledTime+.4f;_wpStep=9;return;
        }
        if(_wpStep==9 && Time.unscaledTime>=_wpAt)
        {
            if(!_lifecycle!.Frozen)return;
            _wpPlayback=_remoteVisual!.Animation!.PlaybackTime;_wpAt=Time.unscaledTime+.4f;_wpStep=10;return;
        }
        if(_wpStep==10 && Time.unscaledTime>=_wpAt)
        {
            TravelCheck(_remoteVisual!.Animation!.PlaybackTime==_wpPlayback,"remote native clip clock stops during shared pause");
            TravelMark(TravelRole+"-pause-checked");_wpStep=11;return;
        }
        if(_wpStep==11)
        {
            if(!Marked("host-pause-checked") || !Marked("client-pause-checked"))return;
            if(TravelRole=="host")_lifecycle!.HostPauseRequest(false);
            _wpStep=12;_wpAt=Time.unscaledTime+.4f;return;
        }
        if(_wpStep==12 && Time.unscaledTime>=_wpAt && !_lifecycle!.Frozen)
        {
            TravelCheck(_remoteVisual!.Animation!.PlaybackTime!=_wpPlayback,"remote clip resumes after shared pause");
            TravelMark(TravelRole+"-pass");_wpStep=99;
        }
    }
}
