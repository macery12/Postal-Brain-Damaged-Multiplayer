using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using UnityEngine;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    internal void ReconcileRemoteHazardContact(DamageTrigger hazard)
    {
        var remote=_remote;var contacts=hazard._hittablesCollidersDict;
        if(remote==null || contacts==null) return;
        var hittable=remote.Cast<IHittable>();
        if(!contacts.ContainsKey(hittable)) return;
        var body=remote.GetComponent<CapsuleCollider>();
        if(remote.IsAlive && body!=null && body.enabled)
        {
            // Trigger exit callbacks can lag a received pose or native deck
            // movement. Query the current transforms, not cached physics bounds
            // or the contact ledger's older rider position. Native contact
            // producers retain their own layer/eligibility rules.
            var feet=CurrentRemoteCarrierFeet()??remote.FeetPosition;
            foreach(var volume in hazard.GetComponentsInChildren<Collider>(true))
                if(volume!=null && volume.enabled && volume.isTrigger && volume.gameObject.activeInHierarchy &&
                    Physics.ComputePenetration(body,feet,remote.transform.rotation,volume,volume.transform.position,volume.transform.rotation,out _,out _)) return;
        }
        contacts.Remove(hittable);
    }
}
