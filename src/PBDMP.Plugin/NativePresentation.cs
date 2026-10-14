using UnityEngine;
using Object=UnityEngine.Object;

namespace PBDMP.Plugin;

internal sealed class NativePresentation : IDisposable
{
    internal GameObject Root {get;}
    private readonly ParticleSystem[] _particles;
    internal NativePresentation(GameObject source,bool beamRenderer=false)
    {
        // Do not execute Awake/OnEnable from any original gameplay hierarchy.
        var staging=new GameObject("PBDMP visual staging"); staging.SetActive(false);
        GameObject? copy=null;
        try
        {
            copy=Object.Instantiate(source,staging.transform,false); copy.SetActive(false);
            foreach(var component in copy.GetComponentsInChildren<Component>(true))
            {
                // This specific native renderer only draws its original mesh/material.
                // It contains no beam targeting, damage, collision or weapon inputs.
                if(beamRenderer && component.TryCast<Hyperstrange.PBD.LightningGun.LightningGunBeamRenderer>()!=null) continue;
                if(component.TryCast<Transform>()!=null || component.TryCast<MeshRenderer>()!=null || component.TryCast<MeshFilter>()!=null ||
                    component.TryCast<SkinnedMeshRenderer>()!=null || component.TryCast<ParticleSystem>()!=null || component.TryCast<ParticleSystemRenderer>()!=null ||
                    component.TryCast<TrailRenderer>()!=null || component.TryCast<LineRenderer>()!=null || component.TryCast<Light>()!=null) continue;
                Object.DestroyImmediate(component);
            }
            _particles=copy.GetComponentsInChildren<ParticleSystem>(true).ToArray();
            if(_particles.Length>0 && !AuditedVisualSources.Names.Contains(source.name))
                throw new InvalidOperationException("Native particle source needs a serialized-module audit: "+source.name);
            foreach(var particle in _particles)
            {
                var main=particle.main; main.playOnAwake=false;
                particle.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            copy.transform.SetParent(null,false); copy.name="PBDMP visual "+source.name;
            Root=copy; Root.SetActive(true);
            foreach(var particle in _particles) {particle.Play(false);particle.Pause(false);}
        }
        catch {if(copy!=null) Object.Destroy(copy);throw;}
        finally {Object.Destroy(staging);}
    }
    internal void Move(Vector3 position,Quaternion rotation) => Root.transform.SetPositionAndRotation(position,rotation);
    internal void Tick(float delta)
    {foreach(var particle in _particles) if(particle!=null) particle.Simulate(delta,false,false,false);}
    internal bool Alive=>Root!=null;
    public void Dispose(){if(Root!=null) Object.Destroy(Root);}
}
