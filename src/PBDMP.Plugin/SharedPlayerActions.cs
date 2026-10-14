using Hyperstrange.PBD;
using Hyperstrange.PBD.PlayerComponents;
using Hyperstrange.PBD.PlayerWeapons;
using PBDMP.Network;
using UnityEngine;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    private float _actionAt;
    private WorldFrame? _peerDefense;
    private float _peerMeleeAt=-10;
    internal bool PeerBlocking=>_peerDefense?.Revision is {} state && (state&1)!=0;
    internal bool PeerShielding=>_peerDefense?.Revision is {} state && (state&2)!=0;
    internal float PeerCharge=>_peerDefense?.Value??0;
    internal bool PeerKicking=>Time.unscaledTime-_peerMeleeAt<0.35f;
    private void TickPlayerActions()
    {
        if(!Active || _life.Frozen || Time.unscaledTime<_actionAt || Player.Instance?.IsAlive!=true) return;
        _actionAt=Time.unscaledTime+0.1f;var weapon=Player.Instance.CurrentWeapon;
        var hit=Player.Instance.GetComponent<PlayerHitProcessorComponent>();
        var state=(hit._blockProcessor._isBLockActive?1u:0)|(weapon?.TryCast<PlayerWeaponBubbleGumMachinegun>()?.IsBlockActive==true?2u:0)|(hit._reflectProcessor._isReflectActive?4u:0);
        Send(new(Client?WorldKind.Defense:WorldKind.WeaponEffect,_epoch,1,Revision:state,Flags:110,Actor:Client?(byte)2:(byte)1,Life:_life.Life,Asset:weapon?.WeaponId?.ID??"",Value:weapon?.ChargeValue??0));
    }
    private void ApplyDefense(WorldFrame frame)
    {
        var pose=_session.Snapshot.RemotePose;
        if(pose?.Alive!=true || frame.Life!=pose.Life || frame.Actor!=2 || frame.Revision>7 || frame.Value is <0 or >100) return;
        _peerDefense=frame;Send(frame with{Kind=WorldKind.WeaponEffect});
    }
    internal void MeleeSwing(PlayerWeaponNoWeapon weapon)
    {if(Active && !_life.Frozen) Send(new(Client?WorldKind.ClientEffect:WorldKind.WeaponEffect,_epoch,1,Flags:111,Actor:Client?(byte)2:(byte)1,Life:_life.Life,Position:Net(Player.Instance.CenterOfMass),Asset:weapon.WeaponId.ID));}
    private void PlayPlayerAction(WorldFrame frame)
    {
        if(frame.Actor==(Client?2:1)) return;
        if(frame.Flags==110) _peerDefense=frame;
        else if(frame.Flags==111)
        {
            _peerMeleeAt=Time.unscaledTime;
            if(_catalogue!.Assets.TryGetValue(frame.Asset,out var id)) id.TryCast<WeaponId>()?.PlayerWeaponPrefab.GetComponent<PlayerWeaponNoWeapon>()?._kick?.PlayOneShot(Unity(frame.Position));
        }
    }
    private void SendPlayerContact(IAiActor actor,bool killed)
    {
        var npc=actor.TryCast<Npc>();var pose=_session.Snapshot.RemotePose;
        if(npc==null || pose==null) return;
        Send(new(WorldKind.WeaponEffect,_epoch,1,Register(npc),Actor:2,Life:pose.Life,Flags:killed?131u:130u));
    }
    private void ApplyPlayerContact(WorldFrame frame)
    {
        if(!Client || frame.Actor!=2 || frame.Life!=_life.Life || !_enemies.TryGetValue(frame.Entity,out var npc) || npc==null) return;
        if(frame.Flags==131) Player.Instance.OnKilledOtherActor(npc.Cast<IAiActor>());
        else Player.Instance.OnHitOtherActor(npc.Cast<IAiActor>());
    }
}
