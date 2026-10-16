using Hyperstrange.PBD;
using Il2CppInterop.Runtime;
using PBDMP.Network;
using UnityEngine;
using Hyperstrange.PBD.PlayerComponents;
using Hyperstrange.PBD.PlayerWeapons;

namespace PBDMP.Plugin;

internal sealed class LocalWeaponObserver : IDisposable
{
    private Player? _player;
    private Il2CppSystem.Action<PlayerWeapon>? _fired;
    private Il2CppSystem.Action<PlayerAttack>? _attackStart, _attackEnd;
    private PlayerAttack? _attack;
    private uint _actionSequence;
    private float _shotAt=-10;
    private bool _alternate;
    private IntPtr _weaponPointer;
    private static float _kickAt=-10;
    internal static void NativeKick()=>_kickAt=Time.time;
    internal uint ShotSequence { get; private set; }

    internal void Bind(Player? player)
    {
        if (player == null && _player == null) return;
        if (player != null && _player != null && player.Pointer == _player.Pointer) return;
        Dispose();
        _player = player;
        if (player == null) return;
        _fired = (Il2CppSystem.Action<PlayerWeapon>)(System.Action<PlayerWeapon>)OnFired;
        player.OnWeaponFired = Il2CppSystem.Delegate.Combine(player.OnWeaponFired, _fired).Cast<Il2CppSystem.Action<PlayerWeapon>>();
        _attackStart=(Action<PlayerAttack>)(attack=>{_attack=attack;_actionSequence=unchecked(_actionSequence+1);});
        _attackEnd=(Action<PlayerAttack>)(attack=>{if(_attack?.Pointer==attack.Pointer)_attack=null;});
        player.OnPlayerAttackStart=Il2CppSystem.Delegate.Combine(player.OnPlayerAttackStart,_attackStart).Cast<Il2CppSystem.Action<PlayerAttack>>();
        player.OnPlayerAttackEnd=Il2CppSystem.Delegate.Combine(player.OnPlayerAttackEnd,_attackEnd).Cast<Il2CppSystem.Action<PlayerAttack>>();
        Plugin.Logger.LogInfo("Bound local player's actual weapon-fired callback.");
    }

    private void OnFired(PlayerWeapon weapon)
    {
        if (_player != null && weapon != null && weapon.Player != null && weapon.Player.Pointer == _player.Pointer)
        {
            ShotSequence = unchecked(ShotSequence + 1);
            _shotAt=Time.unscaledTime;_alternate=InputManager.AlternateFire;
            SharedWorld.Current?.WeaponFired(weapon);
        }
    }

    internal PlayerAnimation Capture(Player player)
    {
        var weapon=player.CurrentWeapon;
        var pointer=weapon?.Pointer??IntPtr.Zero;
        if(pointer!=_weaponPointer){_weaponPointer=pointer;_attack=null;_shotAt=-10;_alternate=false;}
        if(!player.IsAlive)return default;
        var motion=player.MovementState==PlayerMovementComponent.MovementStateEnum.SLIDING?PlayerMotion.Sliding:
            player.MovementState is PlayerMovementComponent.MovementStateEnum.GROUNDED or PlayerMovementComponent.MovementStateEnum.ON_SLOPE?PlayerMotion.Grounded:PlayerMotion.Airborne;
        var flags=_alternate?PlayerActions.Alternate:PlayerActions.None;
        if(Time.unscaledTime-_shotAt<.12f)flags|=PlayerActions.Firing;
        if(weapon?.TryCast<PlayerWeaponAkimbo>()?._currentAnimatorLayer==1)flags|=PlayerActions.LeftHand;
        var saw=weapon?.TryCast<PlayerWeaponShovel>();
        var attack=saw?._playerAttack??_attack;
        if(attack!=null){flags|=PlayerActions.Melee;if(weapon?.TryCast<PlayerWeaponNoWeapon>()!=null)flags|=PlayerActions.Kicking;if(attack.IsLoopAttack)flags|=PlayerActions.Loop;}
        if(Time.time-_kickAt<.35f)flags|=PlayerActions.Kicking;
        if(saw?._isDamageEnabled==true)flags|=PlayerActions.SawRunning;
        if(saw?.IsBlockActive==true)flags|=PlayerActions.Blocking;
        if(weapon?.TryCast<PlayerWeaponBubbleGumMachinegun>()?.IsBlockActive==true)flags|=PlayerActions.Shielding;
        var charge=Mathf.Clamp(weapon?.ChargeValue??0,0,100);
        if(charge>0)flags|=PlayerActions.Charging;
        return new(motion,flags,_actionSequence,attack!=null?Mathf.Repeat(attack.NormalizedTime,1):0,charge);
    }

    public void Dispose()
    {
        if (_player != null && _fired != null)
        {
            var remaining = Il2CppSystem.Delegate.Remove(_player.OnWeaponFired, _fired);
            _player.OnWeaponFired = remaining?.Cast<Il2CppSystem.Action<PlayerWeapon>>();
        }
        if(_player!=null)
        {
            if(_attackStart!=null)_player.OnPlayerAttackStart=Il2CppSystem.Delegate.Remove(_player.OnPlayerAttackStart,_attackStart)?.Cast<Il2CppSystem.Action<PlayerAttack>>();
            if(_attackEnd!=null)_player.OnPlayerAttackEnd=Il2CppSystem.Delegate.Remove(_player.OnPlayerAttackEnd,_attackEnd)?.Cast<Il2CppSystem.Action<PlayerAttack>>();
        }
        _player = null; _fired = null;_attackStart=null;_attackEnd=null;_attack=null;_weaponPointer=IntPtr.Zero;_shotAt=-10;_kickAt=-10;
    }
}
