using Hyperstrange.PBD;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PBDMP.Plugin;

internal sealed class RemoteMuzzleEffect : IDisposable
{
    internal GameObject Root { get; }
    internal string SourceName { get; }
    private readonly ParticleSystem[] _systems;
    private float _started;
    internal bool IsPlaying => Root.activeSelf && _systems.Any(p => p.IsAlive(false));
    internal int ParticleCount => _systems.Sum(p => p.particleCount);

    internal RemoteMuzzleEffect(GameObject weaponPrefab, Transform muzzle)
    {
        var source = weaponPrefab.GetComponentsInChildren<MuzzleFlash>(true)
            .FirstOrDefault(f => f._muzzleFlash != null)?._muzzleFlash;
        if (source == null) throw new InvalidOperationException("No original muzzle particle asset for this weapon.");
        SourceName = source.name;
        // Stripped IL2CPP bindings cannot disable collision/trigger modules or stop callbacks.
        // Reuse the shared serialized-module audit used by native combat presentation.
        if (!AuditedVisualSources.Names.Contains(SourceName))
            throw new InvalidOperationException("Muzzle source needs a particle-module audit: " + SourceName);
        var staging = new GameObject("PBDMP inactive effect staging");
        staging.SetActive(false);
        GameObject? copy = null;
        try
        {
            // Inactive parent prevents Awake/OnEnable before gameplay components are removed.
            copy = Object.Instantiate(source.gameObject, staging.transform, false);
            copy.SetActive(false);
            foreach (var component in copy.GetComponentsInChildren<Component>(true))
            {
                if (component.TryCast<Transform>() != null || component.TryCast<ParticleSystem>() != null ||
                    component.TryCast<ParticleSystemRenderer>() != null || component.TryCast<MeshFilter>() != null ||
                    component.TryCast<MeshRenderer>() != null || component.TryCast<TrailRenderer>() != null ||
                    component.TryCast<LineRenderer>() != null || component.TryCast<Light>() != null) continue;
                Object.DestroyImmediate(component);
            }
            Root = copy;
            Root.name = "PBDMP original muzzle effect " + SourceName;
            Root.transform.SetParent(muzzle, false);
            Root.transform.localPosition = Vector3.zero;
            Root.transform.localRotation = Quaternion.identity;
            foreach (var transform in Root.GetComponentsInChildren<Transform>(true)) transform.gameObject.layer = LayerManager.Layers.NPC;
            _systems = Root.GetComponentsInChildren<ParticleSystem>(true).ToArray();
            if (_systems.Length == 0) throw new InvalidOperationException("Original muzzle asset contains no particles.");
            foreach (var system in _systems)
            {
                var main = system.main;
                main.loop = false; main.playOnAwake = false;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            Plugin.Logger.LogInfo($"Original muzzle effect reused: {SourceName}; {_systems.Length} particle systems; scripts/physics removed before activation.");
        }
        catch { if (copy != null) Object.Destroy(copy); throw; }
        finally { Object.Destroy(staging); }
    }

    internal void Fire()
    {
        Root.SetActive(true);
        foreach (var system in _systems)
        { system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); system.Play(false); system.Pause(false); }
        _started = Time.unscaledTime;
    }

    internal void Update(float delta=-1)
    {
        if(delta==0)return;
        if (Root.activeSelf)
            foreach (var system in _systems) system.Simulate(delta<0?Time.unscaledDeltaTime:delta, false, false, false);
        if (Root.activeSelf && Time.unscaledTime - _started > 0.1f &&
            (!_systems.Any(p => p.IsAlive(false)) || Time.unscaledTime - _started > 2f)) Stop();
    }

    internal void Stop()
    {
        foreach (var system in _systems) system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        Root.SetActive(false);
    }

    internal void SimulateForTest(float seconds)
    { foreach (var system in _systems) system.Simulate(seconds, false, false, false); }

    public void Dispose() { Object.Destroy(Root); }
}
