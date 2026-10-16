using System.Security.Cryptography;
using System.Text;
using Hyperstrange.PBD;
using Il2CppInterop.Runtime;
using Hyperstrange.PBD.Contraptions;
using PBDMP.Network;
using UnityEngine;
using Object=UnityEngine.Object;
using NativePairs=Il2CppSystem.Collections.Generic.Dictionary<string,string>;

namespace PBDMP.Plugin;

/// <summary>Audited native save GUIDs, isolated from ProfileManager and the solo save writer.</summary>
internal static class NativeSaveBridge
{
    internal sealed record Entry(string Guid, Component Component);
    internal static Entry[] Entries()
    {
        var result=new Dictionary<string,Entry>(StringComparer.Ordinal);
        foreach(var controller in Object.FindObjectsOfType<SaveController>(true))
            foreach(var entry in controller._behavioursToSave)
            {
                if(entry.Behaviour==null || !entry.Behaviour.gameObject.scene.IsValid() || entry.Behaviour.TryCast<ISaveable>()==null) continue;
                if(string.IsNullOrWhiteSpace(entry.GUID)) throw new InvalidOperationException("Native save entry has no authored identity.");
                if(result.TryGetValue(entry.GUID,out var previous) && previous.Component.Pointer!=entry.Behaviour.Pointer)
                    throw new InvalidOperationException("Duplicate authored native save identity: "+entry.GUID);
                result[entry.GUID]=new(entry.GUID,entry.Behaviour);
            }
        return result.Values.ToArray();
    }
    internal static Dictionary<string,string> Copy(NativePairs pairs)
    {
        var result=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var p in pairs) result.Add(p.Key,p.Value??"");
        return result;
    }
    internal static NativePairs Native(Dictionary<string,string> pairs)
    {var result=new NativePairs();foreach(var p in pairs) result.Add(p.Key,p.Value);return result;}
    internal static PlayerSpawnPoint PlayerPoint()=>Object.FindObjectsOfType<PlayerSpawnPoint>(true)
        .First(p=>p._player!=null && p._player.Pointer==Hyperstrange.PBD.Player.Instance?.Pointer);
    internal static Dictionary<string,string> Player()=>Copy(PlayerPoint().Save());
    internal static void LoadPlayer(Dictionary<string,string> record)
    {
        PlayerPoint().Load(Native(record));
        // PlayerSpawnPoint.Load ends with Player.OnLevelStart, masking input
        // and hiding the view. Unlike native map loading, a personal co-op
        // restore has no later OnStartLevelReady event to complete that pair.
        // The session's owned barriers still prevent early gameplay release.
        Hyperstrange.PBD.Player.Instance.OnLevelStartReady();
        if(SessionLifecycle.Current?.LocalTutorialActive==true) InputManager.CancelInput=true;
    }
    internal static string GameKey=>string.Join(":",Plugin.Fingerprint.Split(':').Take(2));
    internal static string CheckpointKey(PlayerTriggerCheckpointComponent point)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(WorldCatalogue.PathOf(point.transform))));
    internal static bool NpcOwned(Component c)=>c.GetComponentInParent(Il2CppType.Of<Npc>(),true)!=null;
    internal static bool Personal(Component c)=>c.TryCast<Pickup>()!=null && c.TryCast<CodexPickup>()==null || c.TryCast<PlayerSpawnPoint>()!=null;
}
