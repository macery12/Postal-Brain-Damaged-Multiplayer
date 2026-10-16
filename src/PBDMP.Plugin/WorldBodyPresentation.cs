using UnityEngine;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    private readonly Dictionary<Rigidbody,RigidbodyInterpolation> _replicaBodyInterpolation=new();
    private void OwnBodyPresentation(Rigidbody body)
    {
        if(!Client) return;
        if(!_replicaBodyInterpolation.ContainsKey(body)) _replicaBodyInterpolation[body]=body.interpolation;
        // Physics interpolation can display a stale local simulation pose even
        // after the kinematic body receives its exact authoritative transform.
        body.interpolation=RigidbodyInterpolation.None;
    }
    private void ReleaseBodyPresentation()
    {
        foreach(var pair in _replicaBodyInterpolation) if(pair.Key!=null) pair.Key.interpolation=pair.Value;
        _replicaBodyInterpolation.Clear();
    }
}
