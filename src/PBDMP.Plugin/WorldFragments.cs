using PBDMP.Network;
using UnityEngine;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    private readonly Dictionary<Rigidbody,(bool Kinematic,bool Collisions)> _fragmentBodies=new();
    private IEnumerable<int> CaptureFragments(bool complete)
    {
        foreach(var pair in _catalogue!.Fragments)
        {
            var body=pair.Value;if(body==null) {yield return 0;continue;}
            var frame=new WorldFrame(WorldKind.Scenery,_epoch,1,Id(pair.Key),Key:pair.Key,Flags:5,
                Position:Net(body.position),Rotation:Net(body.rotation.eulerAngles),Velocity:Net(body.velocity),
                Values:new[]{body.angularVelocity.x,body.angularVelocity.y,body.angularVelocity.z,
                    body.IsSleeping()?1f:0,body.gameObject.activeSelf?1f:0,body.detectCollisions?1f:0,body.isKinematic?1f:0});
            PublishScenery(pair.Key,frame,complete);yield return 0;
        }
    }
    private void ApplyFragment(WorldFrame f,bool hostRestore)
    {
        if(!_catalogue!.Fragments.TryGetValue(f.Key,out var body) || f.Values is not {Length:7} v || v.Skip(3).Any(n=>n is not (0 or 1)))
            throw new InvalidDataException("Invalid gameplay fragment state.");
        if(body==null) throw new InvalidDataException("Missing authored gameplay fragment.");
        if(!hostRestore && !_fragmentBodies.ContainsKey(body)) _fragmentBodies[body]=(body.isKinematic,body.detectCollisions);
        OwnBodyPresentation(body);
        body.isKinematic=!hostRestore || v[6]!=0;body.detectCollisions=v[5]!=0;
        body.position=Unity(f.Position);body.rotation=Quaternion.Euler(Unity(f.Rotation));
        if(!body.isKinematic)
        {
            body.velocity=Unity(f.Velocity);body.angularVelocity=new(v[0],v[1],v[2]);
            if(v[3]!=0) body.Sleep();else body.WakeUp();
        }
        body.gameObject.SetActive(v[4]!=0);
    }
    private void ClearFragments()
    {
        foreach(var pair in _fragmentBodies) if(pair.Key!=null)
        {pair.Key.isKinematic=pair.Value.Kinematic;pair.Key.detectCollisions=pair.Value.Collisions;}
        _fragmentBodies.Clear();
    }
}
