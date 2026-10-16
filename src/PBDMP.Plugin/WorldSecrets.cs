using HarmonyLib;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using PBDMP.Network;
using UnityEngine;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    private readonly HashSet<string> _sharedSecrets=new();
    private readonly HashSet<string> _personalSecrets=new();
    internal bool DiscoverSecret(PlayerTriggerSecretComponent secret,Player player)
    {
        if(!Active || _catalogue?.Key(secret) is not {} key || !_catalogue.SecretKeys.Contains(key)) return false;
        if(Applying || RestoringNative || _life.Frozen || !player.IsAlive) return true;
        var trigger=secret.GetComponent<PlayerTrigger>();
        var here=trigger!=null && TriggerOverlaps(trigger,player.FeetPosition,player.Height);
        if(here && _personalSecrets.Add(key))
        {
            player.ShowNotification(secret._notificationText);
            player.OnSecretFound?.Invoke();
        }
        // Remote trigger presence is validated by the host's ordinary mission
        // occupancy path. Only the first shared discovery increments results.
        if(!Client && _sharedSecrets.Add(key))
        {
            LevelController.RegisterFoundSecret();SendSecret(key);
        }
        return true;
    }
    private void SendSecret(string key)=>Send(new(WorldKind.Mechanism,_epoch,1,Id(key),Key:key,Flags:3,Value:_sharedSecrets.Contains(key)?1:0,Values:Array.Empty<float>()));
    private IEnumerable<int> CaptureSecrets()
    {foreach(var key in _catalogue!.SecretKeys) {SendSecret(key);yield return 0;}}
    private void ApplySecret(WorldFrame f)
    {
        if(!_catalogue!.SecretKeys.Contains(f.Key) || f.Value is not (0 or 1) || f.Values is not {Length:0})
            throw new InvalidDataException("Invalid shared secret record.");
        if(f.Value!=0) _sharedSecrets.Add(f.Key);else {_sharedSecrets.Remove(f.Key);_personalSecrets.Remove(f.Key);}
        var controller=LevelController._instance.GetComponent<SecretsController>();
        controller.FoundSecretsCount=_sharedSecrets.Count;
    }
}

internal static class WorldSecretPatches
{
    internal static void Install(Harmony h)=>h.Patch(AccessTools.Method(typeof(PlayerTriggerSecretComponent),"OnPlayerEnter"),prefix:new(typeof(WorldSecretPatches),nameof(Enter)));
    private static bool Enter(PlayerTriggerSecretComponent __instance,Player __0)=>SharedWorld.Current?.DiscoverSecret(__instance,__0)!=true;
}
