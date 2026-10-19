using HarmonyLib;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using PBDMP.Network;
using UnityEngine;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    private readonly Dictionary<string,CodexPickup> _posters=new();
    private readonly Dictionary<IntPtr,string> _posterKeys=new();
    private readonly HashSet<string> _collectedPosters=new();
    private readonly Dictionary<string,float> _posterRequests=new();
    private readonly Dictionary<string,bool> _sentPosters=new();
    private void BuildPosters()
    {
        foreach(var entry in NativeSaveBridge.Entries()) if(entry.Component.TryCast<CodexPickup>() is {} poster)
        {
            _posters.Add(entry.Guid,poster);_posterKeys.Add(poster.Pointer,entry.Guid);
            if(poster._pickedUp) _collectedPosters.Add(entry.Guid);
        }
    }
    internal bool InteractPoster(Pickup pickup,Player player)
    {
        if(!Active || pickup.TryCast<CodexPickup>()==null || !_posterKeys.TryGetValue(pickup.Pointer,out var key)) return false;
        if(_life.Frozen || Applying || RestoringNative || !player.IsAlive || pickup._pickedUp) return true;
        if(Client)
        {
            if(player.Pointer!=Player.Instance.Pointer || _posterRequests.GetValueOrDefault(key)>Time.unscaledTime) return true;
            _posterRequests[key]=Time.unscaledTime+.5f;
            Send(new(WorldKind.Special,_epoch,1,Key:key,Actor:2,Life:_life.Life,Flags:94,Position:Net(player.FeetPosition)));
        }
        else if(pickup.CanBeInteractedWith(player)) CollectPoster(key);
        return true;
    }
    private void ApplyPosterRequest(WorldFrame frame)
    {
        if(!_posters.TryGetValue(frame.Key,out var poster) || poster==null || poster._pickedUp ||
            (!poster.gameObject.activeInHierarchy && !poster._optimizeState) || _remote?.IsAlive!=true) return;
        var feet=Unity(frame.Position);
        if(Vector3.Distance(feet,_remote.FeetPosition)>3 || Vector3.Distance(feet,poster.transform.position)>4) return;
        CollectPoster(frame.Key);
    }
    private void CollectPoster(string key)
    {
        if(!_collectedPosters.Add(key)) return;
        var poster=_posters[key];poster.SetEnabled(true);
        SetPosterCount();ShowPosterCollection(poster);
        SendPoster(key,true);Send(new(WorldKind.SceneryEvent,_epoch,1,Key:key,Flags:2));
    }
    private static void ShowPosterCollection(CodexPickup poster)
    {
        poster._onPickUpEvent?.PlayOneShot(poster.transform.position);
        if(poster._codexId!=null && !poster._codexId.Unlocked)
        {
            Player.Instance.ShowNotification(new Notification(poster._notificationString,true,true));
            poster._codexId.SetUnlocked();
        }
    }
    private void SendPoster(string key,bool live=false)
    {
        var collected=_collectedPosters.Contains(key);_sentPosters[key]=collected;
        Send(new(WorldKind.Mechanism,_epoch,1,Id("poster:"+key),Key:key,Flags:6,Value:collected?1:0,Value2:live?1:0,Values:Array.Empty<float>()));
    }
    private IEnumerable<int> CapturePosters(bool complete)
    {
        foreach(var key in _posters.Keys)
        {if(complete || !_sentPosters.TryGetValue(key,out var old) || old!=_collectedPosters.Contains(key)) SendPoster(key);yield return 0;}
    }
    private void SetPosterCount()
    {if(LevelController._instance?.GetComponent<CodexController>() is {} codex) codex.CollectedEntriesCount=_collectedPosters.Count;}
    private void ApplyPoster(WorldFrame frame)
    {
        if(!_posters.TryGetValue(frame.Key,out var poster) || frame.Value is not (0 or 1) || frame.Value2 is not (0 or 1) || frame.Values is not {Length:0}) throw new InvalidDataException("Invalid authored poster state.");
        if(frame.Value==1) _collectedPosters.Add(frame.Key);else _collectedPosters.Remove(frame.Key);
        poster.SetEnabled(frame.Value==1);SetPosterCount();
        // A current-state restore also makes the collected entry readable, but
        // does not replay its notification/sound or award historical achievements.
        // Live state arrives before its separate one-shot presentation event.
        if(frame.Value==1 && frame.Value2==0 && poster._codexId!=null) poster._codexId._unlocked=true;
    }
    internal void RestoreLegacyPosters(CoopSave save)
    {
        // Older schema-2 development saves recorded posters as personal authored
        // pickups. Their GUIDs permit an exact, non-destructive shared union.
        if(save.World.Any(f=>f.Kind==WorldKind.Mechanism && f.Flags==6)) return;
        var collected=save.Host.Collected.Concat(save.Guests.SelectMany(g=>g.Collected)).ToHashSet(StringComparer.Ordinal);
        foreach(var pair in _posters)
        {
            var picked=collected.Contains("a:"+pair.Key) || pair.Value._pickedUp;
            if(picked) _collectedPosters.Add(pair.Key);else _collectedPosters.Remove(pair.Key);
            pair.Value.SetEnabled(picked);
        }
        SetPosterCount();
    }
    private void ClearPosters()
    {_posters.Clear();_posterKeys.Clear();_collectedPosters.Clear();_posterRequests.Clear();_sentPosters.Clear();}
}

internal static class WorldPosterPatches
{
    internal static void Install(Harmony h)=>h.Patch(AccessTools.Method(typeof(Pickup),nameof(Pickup.Interact)),prefix:new(typeof(WorldPosterPatches),nameof(Interact)));
    private static bool Interact(Pickup __instance,Player __0)=>SharedWorld.Current?.InteractPoster(__instance,__0)!=true;
}
