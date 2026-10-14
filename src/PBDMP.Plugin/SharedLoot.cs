using System.Security.Cryptography;
using System.Text;
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
    {foreach(var prefab in Resources.FindObjectsOfTypeAll<Pickup>()) if(!prefab.gameObject.scene.IsValid()) _pickupPrefabs[PickupKey(prefab)]=prefab;}
    internal void PickupDropped(Pickup pickup)
    {
        if(Client || !Active) return;
        // Drop can precede native bag quantity setters. Capture at the end of the frame;
        // every pooled drop receives a fresh identity, including reuse of the same pointer.
        _pendingLoot.Add((++_nextId,pickup,pickup.GetDropEndPosition(),pickup.transform.rotation));
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
        if(!_pickupPrefabs.TryGetValue(frame.Key,out var prefab)) throw new InvalidDataException("Unknown personal-loot prefab.");
        var root=Object.Instantiate(prefab.gameObject,Unity(frame.Position),Quaternion.Euler(Unity(frame.Rotation)));
        var bag=root.GetComponent<BagPickup>();if(bag!=null){bag.SetHealth(frame.Value);bag.SetArmor(frame.Value2);}
        _ownedLoot.Add(root);
    }
    private void ClearLoot()
    {foreach(var root in _ownedLoot) if(root!=null) Object.Destroy(root);_ownedLoot.Clear();_pickupPrefabs.Clear();_loot.Clear();_personalLoot.Clear();_pendingLoot.Clear();}
}
