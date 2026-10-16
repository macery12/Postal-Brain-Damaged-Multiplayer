using Hyperstrange.Base;
using Hyperstrange.PBD;
using UnityEngine;

namespace PBDMP.Plugin;

public sealed partial class MultiplayerBehaviour
{
    private RaycastHit NativeFleshRaycast()
    {
        foreach(var c in _actorTestNpc!.GetComponentsInChildren<Collider>().Where(c=>c.enabled && !c.isTrigger))
        {
            var b=c.bounds;var distance=b.extents.magnitude+1;
            if(c.Raycast(new Ray(b.center+Vector3.right*distance,Vector3.left),out var hit,distance*2)) return hit;
        }
        throw new InvalidOperationException("Native flesh fixture has no hittable collider.");
    }
    private void AuditNativeBloodPresentation()
    {
        foreach(var model in Resources.FindObjectsOfTypeAll<SolidObjectModel>().Where(m=>m.HitVfx!=null && m.HitVfx.name.Contains("Blood",StringComparison.OrdinalIgnoreCase)).DistinctBy(m=>m.HitVfx.Pointer))
        {
            var source=model.HitVfx;
            var native=PoolManager.SpawnPoolable(source,Player.Instance.CenterOfMass+Vector3.up*4,Quaternion.identity).Cast<ParticleController>();
            using var replica=new NativePresentation(source.gameObject);
            replica.Move(native.transform.position,Quaternion.identity);
            var originals=native.GetComponentsInChildren<ParticleSystem>(true);var copies=replica.Root.GetComponentsInChildren<ParticleSystem>(true);
            TravelCheck(originals.Length==copies.Length,"blood presentation retains every native particle emitter: "+source.name);
            for(var i=0;i<originals.Length;i++)
            {
                var a=originals[i];var b=copies[i];a.useAutoRandomSeed=false;a.randomSeed=7;b.useAutoRandomSeed=false;b.randomSeed=7;
                a.Simulate(.15f,false,true,false);b.Simulate(.15f,false,true,false);
                File.AppendAllText(Marker("blood-emitter-audit.tsv"),$"{source.name}\t{a.name}\tnative={a.particleCount}\treplica={b.particleCount}\tactive={a.gameObject.activeInHierarchy}/{b.gameObject.activeInHierarchy}\trender={a.GetComponent<ParticleSystemRenderer>().enabled}/{b.GetComponent<ParticleSystemRenderer>().enabled}\n");
                TravelCheck(a.particleCount==b.particleCount,"native burst particle count matches on the harmless replica: "+source.name+"/"+a.name);
            }
            PoolManager.ReturnToPool(native.Cast<IPoolable>());
        }
    }
}
