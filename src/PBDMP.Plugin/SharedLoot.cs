using System.Security.Cryptography;
using System.Text;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using PBDMP.Network;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    private readonly Dictionary<string,Pickup> _pickupPrefabs=new();
    private readonly Dictionary<uint,WorldFrame> _loot=new();
    private readonly HashSet<uint> _personalLoot=new();
    private readonly List<(uint Id,Pickup Pickup,Vector3 Position,Quaternion Rotation)> _pendingLoot=new();
    private readonly List<GameObject> _ownedLoot=new();
    private static string PickupKey(Pickup pickup)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pickup.GetIl2CppType().FullName+"/"+pickup.name.Replace("(Clone)","",StringComparison.Ordinal))));
    private void BuildLootPrefabs()
    {
        foreach(var prefab in Resources.FindObjectsOfTypeAll<Pickup>()) if(!prefab.gameObject.scene.IsValid()) _pickupPrefabs[PickupKey(prefab)]=prefab;
        // Hosting may begin after native enemies have already dropped loot.
        if(!Client && !_life.RestoringWorld)
            foreach(var controller in Object.FindObjectsOfType<LootController>())
                foreach(var pickup in controller._spawnedLoot)
                    if(pickup!=null && !pickup._pickedUp && !_dropKeys.ContainsKey(pickup.Pointer)) PickupDropped(pickup);
    }
    internal void PickupDropped(Pickup pickup)
    {
        if(Client || !Active || _life.RestoringWorld) return;
        // Drop can precede native bag quantity setters. Capture at the end of the frame;
        // every pooled drop receives a fresh identity, including reuse of the same pointer.
        _dropKeys[pickup.Pointer]=++_nextId;
        _pendingLoot.Add((_nextId,pickup,pickup.GetDropEndPosition(),pickup.transform.rotation));
    }
    private void TickLoot()
    {
        foreach(var drop in _pendingLoot)
        {
            if(drop.Pickup==null) continue;
            var bag=drop.Pickup.TryCast<BagPickup>();
            var frame=new WorldFrame(WorldKind.Loot,_epoch,1,drop.Id,Key:PickupKey(drop.Pickup),Position:Net(drop.Position),Rotation:Net(drop.Rotation.eulerAngles),Value:bag?._healthAmount??0,Value2:bag?._armorAmount??0);
            _loot[frame.Entity]=frame;Send(frame);
        }
        _pendingLoot.Clear();
    }
    private void ApplyLoot(WorldFrame frame)
    {
        if(!_personalLoot.Add(frame.Entity)) return;
        CreatePersonalDrop(frame);
    }
    private void ClearLoot()
    {foreach(var root in _ownedLoot) if(root!=null) Object.Destroy(root);_ownedLoot.Clear();_pickupPrefabs.Clear();_loot.Clear();_personalLoot.Clear();_pendingLoot.Clear();_dropKeys.Clear();}
}
