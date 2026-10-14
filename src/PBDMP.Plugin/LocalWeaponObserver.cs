using Hyperstrange.PBD;
using Il2CppInterop.Runtime;

namespace PBDMP.Plugin;

internal sealed class LocalWeaponObserver : IDisposable
{
    private Player? _player;
    private Il2CppSystem.Action<PlayerWeapon>? _fired;
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
        Plugin.Logger.LogInfo("Bound local player's actual weapon-fired callback.");
    }

    private void OnFired(PlayerWeapon weapon)
    {
        if (_player != null && weapon != null && weapon.Player != null && weapon.Player.Pointer == _player.Pointer)
        {
            ShotSequence = unchecked(ShotSequence + 1);
            SharedWorld.Current?.WeaponFired(weapon);
        }
    }

    public void Dispose()
    {
        if (_player != null && _fired != null)
        {
            var remaining = Il2CppSystem.Delegate.Remove(_player.OnWeaponFired, _fired);
            _player.OnWeaponFired = remaining?.Cast<Il2CppSystem.Action<PlayerWeapon>>();
        }
        _player = null; _fired = null;
    }
}
