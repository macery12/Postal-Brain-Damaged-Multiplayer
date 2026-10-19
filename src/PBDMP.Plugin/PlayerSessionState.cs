using Hyperstrange.PBD;
using Hyperstrange.Base;
using Hyperstrange.PBD.PlayerComponents;
using Il2CppInterop.Runtime;
using PBDMP.Network;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PBDMP.Plugin;

internal sealed class PlayerSessionState
{
    private Player? _player;
    private Vector3 _spawn;
    private Vector3 _rotation;
    private PlayerInventoryComponent.SaveData? _deathInventory;
    private float _remaining;
    internal uint Life { get; private set; }
    internal bool Pending => _deathInventory != null;

    internal void CaptureStart()
    {
        _player = Player.Instance;
        if (_player == null) throw new InvalidOperationException("Player is not initialized.");
        var authoredSpawn = Object.FindObjectsOfType<PlayerSpawnPoint>().FirstOrDefault(point => point._player != null && point._player.Pointer == _player.Pointer);
        if (authoredSpawn == null) throw new InvalidOperationException("Cannot find the map's authored player spawn.");
        _spawn = authoredSpawn.transform.position; _rotation = authoredSpawn.transform.eulerAngles;
        _deathInventory = null; Life++;
    }

    // Survival deaths wait for the host's wave revival instead of the campaign timer.
    internal bool WaveRevival { get; private set; }

    internal void Died(bool waveRevival = false)
    {
        var player = Player.Instance;
        if (player == null || Pending) return;
        _deathInventory = new PlayerInventoryComponent.SaveData(player.GetComponent<PlayerInventoryComponent>());
        _remaining = 3; WaveRevival = waveRevival; Life++;
    }

    internal void Tick(float seconds, bool paused)
    {
        if (!Pending || WaveRevival || paused || Player.Instance == null) return;
        _remaining -= seconds;
        if (_remaining > 0) return;
        Revive(null);
    }

    // Native revival restores health, zero armor, head, collider and UI; restore a clean local
    // movement baseline rather than loading the host's movement, checkpoint, health or powerups.
    // A wave revival is placed beside the living partner when there is clear ground.
    internal bool Revive(Vector3? beside, float yaw = 0)
    {
        var player = Player.Instance;
        if (!Pending || player == null) return false;
        if (_player == null || player.Pointer != _player.Pointer) { Cancel(); return false; }
        var inventory = _deathInventory!;
        // Shared weapons, keys and items may change while dead (partner pickups, door keys).
        if (!WaveRevival) KeepSharedSinceDeath(inventory, new PlayerInventoryComponent.SaveData(player.GetComponent<PlayerInventoryComponent>()));
        player.Respawn();
        if (beside is { } feet && FindNearby(player, feet, out var position)) RestoreMovement(position, new(0, yaw, 0));
        else RestoreMovement(_spawn, _rotation);
        player.GetComponent<PlayerInventoryComponent>().LoadState(inventory);
        _deathInventory = null; WaveRevival = false; Life++;
        Plugin.Logger.LogInfo("Independent player respawn completed; inventory retained and world not reset.");
        return true;
    }

    private void RestoreMovement(Vector3 position, Vector3 rotation)
    {
        var player = Player.Instance;
        var baseline = new PlayerMovementComponent.SaveData(player.GetComponent<PlayerMovementComponent>());
        baseline._velocity = Vector3.zero; baseline._velocityToApply = Vector3.zero;
        baseline._position = position; baseline._lastCheckpointPosition = position; baseline._killPlaneRecoveryPosition = position;
        baseline._state = PlayerMovementComponent.MovementStateEnum.MID_AIR;
        baseline._currentStateTimestamp = 0;
        player.GetComponent<PlayerMovementComponent>().LoadState(baseline);
        player.Teleport(position, rotation, Vector3.zero);
    }

    internal JoinLoadout CaptureJoin()
    {
        var player = Player.Instance;
        var inventory = new PlayerInventoryComponent.SaveData(player.GetComponent<PlayerInventoryComponent>());
        var items = new List<InventoryEntry>();
        foreach (var pair in inventory.HeldItems)
        {
            var id = EntityManager.Get<EntityId>(pair.Key);
            if (IsGear(id)) items.Add(new(pair.Key, pair.Value));
        }
        if (items.Count > Protocol.MaxInventoryItems) throw new InvalidOperationException("Host loadout exceeds supported inventory size.");
        var feet = player.FeetPosition;
        return new(inventory.CurrentWeapon ?? "", inventory.LastWeapon ?? "", items.ToArray(), feet.x, feet.y, feet.z,
            player.WorldCamera.transform.eulerAngles.y);
    }

    internal bool ApplyJoin(JoinLoadout loadout,bool placeBeside=true)
    {
        var player = Player.Instance;
        var component = player.GetComponent<PlayerInventoryComponent>();
        var local = new PlayerInventoryComponent.SaveData(component);
        var merged = new Il2CppSystem.Collections.Generic.Dictionary<string, int>();
        foreach (var pair in local.HeldItems)
            if (!IsGear(EntityManager.Get<EntityId>(pair.Key))) merged[pair.Key] = pair.Value;
        foreach (var entry in loadout.Items)
        {
            var id = EntityManager.Get<EntityId>(entry.Id);
            if (!IsGear(id)) throw new InvalidDataException("Host supplied an unavailable weapon/ammo ID.");
            if (id.TryCast<WeaponId>() is { } weapon && weapon.Category != DLCCategory.NONE)
                throw new InvalidDataException("DLC equipment is outside this campaign milestone.");
            merged[entry.Id] = Mathf.Min(entry.Quantity, Mathf.Max(0, id.InventoryCapacity));
        }
        var noWeapon = component._weaponsController._noWeapon;
        bool HasWeapon(string guid) => !string.IsNullOrEmpty(guid) && (merged.ContainsKey(guid) || guid == noWeapon.ID) &&
            EntityManager.Get<EntityId>(guid)?.TryCast<WeaponId>() != null;
        var selected = HasWeapon(loadout.CurrentWeapon) ? loadout.CurrentWeapon : HasWeapon(local.CurrentWeapon) ? local.CurrentWeapon : "";
        if (string.IsNullOrEmpty(selected))
            foreach (var pair in merged)
                if (EntityManager.Get<EntityId>(pair.Key)?.TryCast<WeaponId>() is { IsUtility: false }) { selected = pair.Key; break; }
        if (string.IsNullOrEmpty(selected)) selected = noWeapon.ID;
        local.HeldItems = merged; local.CurrentWeapon = selected;
        local.LastWeapon = HasWeapon(loadout.LastWeapon) ? loadout.LastWeapon : selected;
        component.LoadState(local);
        if(!placeBeside) return true;
        var nearby = FindNearby(player, new(loadout.X, loadout.Y, loadout.Z), out var position);
        if (nearby) { RestoreMovement(position, new(0, loadout.Yaw, 0)); Life++; }
        Plugin.Logger.LogInfo(nearby ? "P2 placed beside P1; host weapons/ammo imported once." :
            "P2 used normal map spawn: no grounded, unblocked position beside P1.");
        return nearby;
    }

    private static bool IsGear(EntityId? id) => id != null &&
        (id.TryCast<WeaponId>() != null || id.TryCast<ItemId>() is { IsAmmo: true });

    internal static bool FindNearby(Player player, Vector3 hostFeet, out Vector3 position)
    {
        var capsule = player.Collider.TryCast<CharacterController>();
        if (capsule == null) { position = default; return false; }
        var radius = capsule.radius * Mathf.Max(player.Transform.lossyScale.x, player.Transform.lossyScale.z);
        var height = Mathf.Max(2 * radius, capsule.height * player.Transform.lossyScale.y);
        var offset = player.Transform.position - player.FeetPosition;
        // Include local doors/NPCs/props, excluding triggers and the local/remote player layers.
        var mask = ~(1 << player.gameObject.layer);
        foreach (var distance in new[] { 1.5f, 2.5f, 4f })
            for (var i = 0; i < 12; i++)
            {
                var radians = i * Mathf.PI / 6;
                var candidate = hostFeet + new Vector3(Mathf.Cos(radians), 0, Mathf.Sin(radians)) * distance;
                if (!Physics.Raycast(candidate + Vector3.up * 2, Vector3.down, out var hit, 5, mask, QueryTriggerInteraction.Ignore) ||
                    Vector3.Dot(hit.normal, Vector3.up) < 0.7f || Mathf.Abs(hit.point.y - hostFeet.y) > 2f) continue;
                var feet = hit.point + Vector3.up * 0.08f;
                if (Physics.CheckCapsule(feet + Vector3.up * radius, feet + Vector3.up * (height-radius),
                    radius + 0.03f, mask, QueryTriggerInteraction.Ignore)) continue;
                position = feet + offset; return true;
            }
        position = default; return false;
    }

    internal void Cancel() { _deathInventory = null; _player = null; WaveRevival = false; Life++; }
    private static void KeepSharedSinceDeath(PlayerInventoryComponent.SaveData death, PlayerInventoryComponent.SaveData current)
    {
        foreach (var entity in Resources.FindObjectsOfTypeAll<EntityId>())
        {
            if (!SharedWorld.SharedEntity(entity) || string.IsNullOrEmpty(entity.ID)) continue;
            if (current.HeldItems.ContainsKey(entity.ID)) death.HeldItems[entity.ID] = current.HeldItems[entity.ID];
            else if (death.HeldItems.ContainsKey(entity.ID)) death.HeldItems.Remove(entity.ID);
        }
    }

    internal void FinishRespawn() { if (Pending) Revive(null); }
}
