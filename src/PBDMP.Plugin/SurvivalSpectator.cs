using Hyperstrange.PBD;
using UnityEngine;

namespace PBDMP.Plugin;

// PBDMP-owned Survival spectator view: the native game has no spectator camera. While the
// local player is dead and the partner lives, the native world camera follows the partner
// from behind; its local pose and the weapon overlay camera are restored on revival or exit.
// The dead player's body, collider and inventory stay where native death left them.
internal sealed class SurvivalSpectator
{
    private const float DeathDelay = 1.5f, Distance = 3.2f;
    private Transform? _camera;
    private Vector3 _localPosition;
    private Quaternion _localRotation;
    private Camera? _weapon;
    private bool _weaponEnabled, _placed;
    private Vector3 _position;
    private float _since = -1;
    internal bool Active => _camera != null;
    internal Vector3 CameraPosition => _camera != null ? _camera.position : Vector3.zero;

    internal void Tick(bool spectate, Vector3 feet, float yaw, float pitch, float height)
    {
        var player = Player.Instance;
        if (!spectate || player == null || player.WorldCamera == null) { Stop(); _since = -1; return; }
        if (_since < 0) _since = Time.unscaledTime;
        // Let the native death presentation play before taking the camera.
        if (Time.unscaledTime - _since < DeathDelay) return;
        if (_camera == null)
        {
            _camera = player.WorldCamera.transform; _localPosition = _camera.localPosition; _localRotation = _camera.localRotation;
            _weapon = player.WeaponCamera; _weaponEnabled = _weapon != null && _weapon.enabled;
            if (_weapon != null) _weapon.enabled = false;
            _placed = false;
        }
        var head = feet + Vector3.up * Mathf.Clamp(height * 0.9f, 0.5f, 2.7f);
        var look = Quaternion.Euler(Mathf.Clamp(pitch, -30, 50) * 0.5f + 12, yaw, 0);
        var desired = head - look * Vector3.forward * Distance + Vector3.up * 0.4f;
        var offset = desired - head;
        var mask = Physics.DefaultRaycastLayers & ~(1 << player.gameObject.layer);
        if (Physics.SphereCast(head, 0.25f, offset.normalized, out var hit, offset.magnitude, mask, QueryTriggerInteraction.Ignore))
            desired = head + offset.normalized * Mathf.Max(0.3f, hit.distance - 0.1f);
        _position = _placed && Vector3.Distance(_position, desired) < 10 ? Vector3.Lerp(_position, desired, 1 - Mathf.Exp(-10 * Time.unscaledDeltaTime)) : desired;
        _placed = true;
        _camera.position = _position;
        _camera.rotation = Quaternion.LookRotation(head + look * Vector3.forward * 2 - _position);
    }

    internal void Stop()
    {
        if (_camera != null) { _camera.localPosition = _localPosition; _camera.localRotation = _localRotation; }
        if (_weapon != null) _weapon.enabled = _weaponEnabled;
        _camera = null; _weapon = null; _placed = false;
    }
}
