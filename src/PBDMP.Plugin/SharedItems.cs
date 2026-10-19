using HarmonyLib;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using Hyperstrange.PBD.PlayerComponents;
using PBDMP.Network;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PBDMP.Plugin;

// Identical players (campaign): weapons, keys and mission items form one team inventory owned by P1.
// A pickup holding any of them exists once. P1 commits every collection and both PCs then collect their
// own native copy of the same authored or dropped pickup. Ammo, health, armor and usables stay personal.
internal sealed partial class SharedWorld
{
    private readonly Dictionary<string,ItemPickup> _sharedPickups=new();
    private readonly Dictionary<IntPtr,string> _sharedPickupKeys=new();
    private readonly HashSet<string> _collectedShared=new();
    private readonly Dictionary<string,float> _sharedRequests=new();
    private uint _teamVersion,_appliedTeamVersion;
    private bool _teamDirty,_grantingShared,_suppressSharedPickUp;
    private (string Key,bool Grant)? _hostPickup;
    internal int SharedCollectionsForTest { get; private set; }
    internal int TeamAppliedForTest { get; private set; }
    internal int PartnerNoticesForTest { get; private set; }
    // The partner's display name (Steam name, else P1/P2), for pickup notices.
    internal string PartnerName { get; set; } = "";

    private bool SharedItemsActive=>Active && !_life.InSurvival;
    internal bool SharedKeys=>SharedItemsActive;

    // Keys and mission items are the native "key items" (neither ammo nor usable, as RemoveKeyItems
    // decides). Weapons count only when they have a pickup (not unarmed, the dong or drink variants).
    internal static bool SharedEntity(EntityId? id)=>id!=null &&
        (id.TryCast<WeaponId>() is {} weapon ? weapon._pickupPrefab!=null : id.TryCast<ItemId>() is {} item && !item.IsAmmo && !item.IsUsable);
    // HEALTH_*/ARMOR_* ids pass the key-item test too, but only ItemPickup holds shared contents.
    private static bool SharedContents(Pickup pickup,out ItemPickup items)
    {
        items=pickup.TryCast<ItemPickup>()!;
        if(items==null) return false;
        foreach(var item in items._items) if(SharedEntity(item.EntityId)) return true;
        return false;
    }
    private string? SharedPickupKey(Pickup pickup)=>SharedContents(pickup,out _)?DropKey(pickup)??_sharedPickupKeys.GetValueOrDefault(pickup.Pointer):null;

    private void BuildSharedItems()
    {
        foreach(var entry in NativeSaveBridge.Entries())
            if(entry.Component.TryCast<ItemPickup>() is {} pickup && SharedContents(pickup,out _))
            {
                var key="a:"+entry.Guid;_sharedPickups[key]=pickup;_sharedPickupKeys[pickup.Pointer]=key;
                if(!Client && pickup._pickedUp) _collectedShared.Add(key);
            }
        _teamDirty=!Client;
    }

    private bool ResolveSharedPickup(string key,out ItemPickup pickup)
    {
        if(_sharedPickups.TryGetValue(key,out pickup!) && pickup!=null) return true;
        if(key.StartsWith("d:",StringComparison.Ordinal))
            foreach(var dropped in Object.FindObjectsOfType<ItemPickup>(true))
                if(DropKey(dropped)==key) {pickup=dropped;return true;}
        pickup=null!;return false;
    }

    // The partner receives the native contents when the pickup holds a key or item, or a gun the team lacks.
    private static bool GrantsPartner(ItemPickup pickup,Player team)
    {
        var inventory=team.GetComponent<PlayerInventoryComponent>();
        foreach(var item in pickup._items)
        {
            if(!SharedEntity(item.EntityId)) continue;
            if(item.EntityId.TryCast<WeaponId>()==null || !inventory.HasItem(item.EntityId)) return true;
        }
        return false;
    }

    // Pickup.Interact for the local player. True means PBDMP handled it and native Interact is skipped.
    internal bool InteractSharedItem(Pickup pickup,Player player)
    {
        if(_grantingShared || !SharedItemsActive || Player.Instance==null || player.Pointer!=Player.Instance.Pointer || SharedPickupKey(pickup) is not {} key) return false;
        if(_life.Frozen || Applying || RestoringNative || !player.IsAlive || pickup._pickedUp || pickup._isBeingPickedUp) return true;
        if(!Client) return false;
        if(!pickup.CanBeInteractedWith(player) || _sharedRequests.GetValueOrDefault(key)>Time.unscaledTime) return true;
        _sharedRequests[key]=Time.unscaledTime+.5f;
        Send(SharedItemsProtocol.ToFrame(new SharedItemRequest(key,_life.Life,Net(player.FeetPosition)),_epoch));
        return true;
    }

    // P1's own native pickup: native PickupComplete (after the fly-to-player tween) calls PickUp.
    // Decide the partner grant before PickUp changes P1's inventory. False skips native PickUp.
    internal bool BeforeHostPickUp(ItemPickup pickup,Player player)
    {
        _hostPickup=null;
        if(_suppressSharedPickUp) return false;
        if(Client || _grantingShared || !SharedItemsActive || Player.Instance==null || player.Pointer!=Player.Instance.Pointer || SharedPickupKey(pickup) is not {} key) return true;
        _hostPickup=(key,GrantsPartner(pickup,player));
        return true;
    }
    internal void AfterHostPickUp()
    {
        if(_hostPickup is not {} pickup) return;
        _hostPickup=null;CommitShared(pickup.Key,1,pickup.Grant);
    }

    private void ApplySharedItemRequest(WorldFrame frame)
    {
        var request=SharedItemsProtocol.RequestFromFrame(frame);var pose=_session.Snapshot.RemotePose;
        if(!SharedItemsActive || _life.Frozen || pose?.Alive!=true || request.Life!=pose.Life || _remote==null) return;
        if(!ResolveSharedPickup(request.Pickup,out var pickup) || pickup._pickedUp || pickup._isBeingPickedUp || _collectedShared.Contains(request.Pickup)) return;
        var feet=Unity(request.Feet);
        if(Vector3.Distance(feet,_remote.FeetPosition)>3 || Vector3.Distance(feet,pickup.transform.position)>4) return;
        var grant=GrantsPartner(pickup,Player.Instance);
        // P1 is the partner here. Native completion hides it, plays its sound, grants P1 through PickUp
        // (skipped when P1 gets nothing) and fires the mission listeners once on the host.
        _grantingShared=true;_suppressSharedPickUp=!grant;
        try {pickup.PickupComplete(Player.Instance);}
        finally {_grantingShared=false;_suppressSharedPickUp=false;}
        if(grant) NotifyPartnerGrant(pickup);
        CommitShared(request.Pickup,2,grant);
    }

    private void CommitShared(string key,byte picker,bool grant)
    {
        if(!_collectedShared.Add(key)) return;
        SharedCollectionsForTest++;_teamDirty=true;
        Send(SharedItemsProtocol.ToFrame(new SharedItemCollect(key,picker,grant,true),_epoch));
        Plugin.Logger.LogInfo($"Shared pickup {key[..Math.Min(14,key.Length)]} taken by P{picker}; partner grant {grant}.");
    }

    private void ApplySharedItem(WorldFrame frame)
    {
        var collect=SharedItemsProtocol.CollectFromFrame(frame);
        _collectedShared.Add(collect.Pickup);
        if(_life.InSurvival || !ResolveSharedPickup(collect.Pickup,out var pickup) || pickup._pickedUp) return;
        _grantingShared=true;
        try
        {
            // The picker always receives the native pickup; the partner only when P1 granted it.
            if(collect.Live && (collect.Picker==2 || collect.Grant) && Player.Instance!=null) pickup.PickUp(Player.Instance);
            pickup.SetEnabled(true);
            if(collect.Live) pickup._onPickUpEvent?.PlayOneShot(pickup.transform.position);
        }
        finally {_grantingShared=false;}
        if(collect.Live && collect.Grant && collect.Picker==1) NotifyPartnerGrant(pickup);
    }

    // The game's own notification on the receiving PC, once per shared item (not its akimbo variant).
    private void NotifyPartnerGrant(ItemPickup pickup)
    {
        if(Player.Instance==null) return;
        foreach(var item in pickup._items)
        {
            if(!SharedEntity(item.EntityId) || item.EntityId.TryCast<WeaponId>()?.NonAkimboVariant!=null) continue;
            var name=item.EntityId.Name;if(string.IsNullOrEmpty(name)) name=item.EntityId.name;
            Player.Instance.ShowNotification(new Notification($"+ {name.ToUpperInvariant()} ({(PartnerName.Length>0?PartnerName:Client?"P1":"P2")})",false));
            PartnerNoticesForTest++;
        }
    }

    internal void SharedInventoryChanged(PlayerInventoryComponent inventory,EntityId entity)
    {
        if(Client || _grantingShared || Player.Instance==null || inventory.Pointer!=Player.Instance.GetComponent<PlayerInventoryComponent>()?.Pointer || !SharedEntity(entity)) return;
        _teamDirty=true;
    }

    private TeamInventory CurrentTeam()
    {
        var inventory=Player.Instance.GetComponent<PlayerInventoryComponent>();var items=new List<TeamItem>();
        foreach(var asset in _catalogue!.Assets.Values)
            if(SharedEntity(asset) && SharedItemsProtocol.ValidId(asset.ID) && inventory.GetItemQuantity(asset) is var quantity && quantity>0)
                items.Add(new(asset.ID,Math.Min(quantity,SharedItemsProtocol.MaxQuantity)));
        return new(++_teamVersion,items);
    }

    private void TickSharedItems()
    {
        if(Client || !_teamDirty || !SharedItemsActive || Player.Instance==null || !_session.Snapshot.Connected) return;
        _teamDirty=false;Send(SharedItemsProtocol.ToFrame(CurrentTeam(),_epoch));
    }

    private void CaptureSharedItems(bool complete)
    {
        if(!complete || Client || !SharedItemsActive) return;
        foreach(var key in _collectedShared) Send(SharedItemsProtocol.ToFrame(new SharedItemCollect(key,1,false,false),_epoch));
        if(Player.Instance!=null) {_teamDirty=false;Send(SharedItemsProtocol.ToFrame(CurrentTeam(),_epoch));}
    }

    // P2 matches P1's absolute holdings of the shared classes through native AddItem/RemoveItem.
    private void ApplyTeamInventory(WorldFrame frame)
    {
        var team=SharedItemsProtocol.TeamFromFrame(frame);
        if(_life.InSurvival || team.Version<=_appliedTeamVersion || Player.Instance==null) return;
        _appliedTeamVersion=team.Version;TeamAppliedForTest++;
        var inventory=Player.Instance.GetComponent<PlayerInventoryComponent>();
        var target=team.Items.ToDictionary(i=>i.Id,i=>i.Quantity,StringComparer.Ordinal);
        _grantingShared=true;
        try
        {
            foreach(var asset in _catalogue!.Assets.Values)
            {
                if(!SharedEntity(asset)) continue;
                var want=target.GetValueOrDefault(asset.ID);var have=inventory.GetItemQuantity(asset);
                if(want>have) inventory.AddItem(asset,want-have);
                else if(want<have) inventory.RemoveItem(asset,have-want);
            }
        }
        finally {_grantingShared=false;}
    }

    internal bool SharedPickupCollectedForTest(Pickup pickup)=>SharedPickupKey(pickup) is {} key && _collectedShared.Contains(key);
    internal bool IsSharedPickupForTest(Pickup pickup)=>SharedPickupKey(pickup)!=null;
    internal string? SharedPickupKeyForTest(Pickup pickup)=>SharedPickupKey(pickup);
    internal ItemPickup? SharedPickupForTest(string key)=>ResolveSharedPickup(key,out var pickup)?pickup:null;

    private void ClearSharedItems()
    {_sharedPickups.Clear();_sharedPickupKeys.Clear();_collectedShared.Clear();_sharedRequests.Clear();_appliedTeamVersion=0;_teamDirty=false;_hostPickup=null;}
}

internal static class SharedItemPatches
{
    internal static void Install(Harmony h)
    {
        h.Patch(AccessTools.Method(typeof(Pickup),nameof(Pickup.Interact)),prefix:new(typeof(SharedItemPatches),nameof(Interact)));
        h.Patch(AccessTools.Method(typeof(ItemPickup),"PickUp"),prefix:new(typeof(SharedItemPatches),nameof(BeforePickUp)),postfix:new(typeof(SharedItemPatches),nameof(AfterPickUp)));
        h.Patch(AccessTools.Method(typeof(PlayerInventoryComponent),nameof(PlayerInventoryComponent.AddItem)),postfix:new(typeof(SharedItemPatches),nameof(Changed)));
        h.Patch(AccessTools.Method(typeof(PlayerInventoryComponent),nameof(PlayerInventoryComponent.RemoveItem)),postfix:new(typeof(SharedItemPatches),nameof(Changed)));
    }
    private static bool Interact(Pickup __instance,Player __0)=>SharedWorld.Current?.InteractSharedItem(__instance,__0)!=true;
    private static bool BeforePickUp(ItemPickup __instance,Player __0)=>SharedWorld.Current?.BeforeHostPickUp(__instance,__0)!=false;
    private static void AfterPickUp()=>SharedWorld.Current?.AfterHostPickUp();
    private static void Changed(PlayerInventoryComponent __instance,EntityId __0)=>SharedWorld.Current?.SharedInventoryChanged(__instance,__0);
}
