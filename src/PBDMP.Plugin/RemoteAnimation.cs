using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PBDMP.Plugin;

internal sealed class RemoteAnimation : IDisposable
{
    private readonly GameObject _target;
    private readonly Dictionary<string, AnimationClip> _clips = new();
    private readonly Transform _trajectory;
    private readonly Vector3 _trajectoryPosition;
    private readonly Quaternion _trajectoryRotation;
    private readonly Vector3 _rootPosition;
    private readonly Quaternion _rootRotation;
    private readonly Vector3 _rootScale;
    private float _time;
    private float _shotTime = 10f;
    internal string CurrentClip { get; private set; } = "";
    internal GameObject Target => _target;

    internal RemoteAnimation(GameObject body, Animator source)
    {
        var root = body.transform.Find("Renderer/AlterDude@bind");
        if (root == null || source == null || source.avatar == null || source.runtimeAnimatorController == null)
            throw new InvalidOperationException("Missing animation binding root, avatar or clip source.");
        _target = root.gameObject;
        _rootPosition = root.localPosition; _rootRotation = root.localRotation; _rootScale = root.localScale;
        _trajectory = root.GetComponentsInChildren<Transform>(true).First(t => t.name == "AlterDude_TrajectorySHJnt");
        _trajectoryPosition = _trajectory.localPosition; _trajectoryRotation = _trajectory.localRotation;
        try
        {
            foreach (var sourceClip in source.runtimeAnimatorController.animationClips)
            {
                if (_clips.ContainsKey(sourceClip.name)) continue;
                var clip = Object.Instantiate(sourceClip);
                // A managed dictionary is not a Unity asset reference during UnloadUnusedAssets.
                // These detached clips have no controller holding them; Dispose releases them explicitly.
                clip.hideFlags = HideFlags.DontUnloadUnusedAsset;
                _clips.Add(sourceClip.name, clip);
                clip.events = new Il2CppReferenceArray<AnimationEvent>(0);
            }
            var required = new[] { "AlterDude@idle" }.Concat(new[] { "machinegun", "shotgun", "rocketlauncher" }
                .SelectMany(family => new[] { "idleTarget", "shoot", "runFront", "runBack", "runLeft", "runRight" }
                    .Select(action => "alterDude@" + action + "_" + family)));
            if (required.Any(name => !_clips.ContainsKey(name))) throw new InvalidOperationException("Required native clips unavailable.");
        }
        catch { Dispose(); throw; }
        // Generic imported clip bindings require an Avatar even for direct sampling.
        // This fresh disabled Animator has no controller or gameplay behaviours.
        var animator = _target.AddComponent<Animator>();
        animator.enabled = false;
        animator.avatar = source.avatar;
        animator.applyRootMotion = false;
        animator.fireEvents = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        Plugin.Logger.LogInfo($"Native animation clips copied: {_clips.Count}; events stripped; controller-free Avatar binding.");
    }

    internal void Fire() { _shotTime = 0f; }

    internal void Update(string weaponName, Vector3 localVelocity, float delta)
    {
        var family = weaponName.Contains("Shotgun", StringComparison.OrdinalIgnoreCase) ? "shotgun" :
            weaponName.Contains("Rocket", StringComparison.OrdinalIgnoreCase) ? "rocketlauncher" : "machinegun";
        var speed = new Vector2(localVelocity.x, localVelocity.z).magnitude;
        var armed = !string.IsNullOrEmpty(weaponName);
        var shoot = "alterDude@shoot_" + family;
        string clipName;
        if (armed && _shotTime < Mathf.Min(_clips[shoot].length, 0.45f)) clipName = shoot;
        else if (speed > 0.25f)
        {
            var direction = Math.Abs(localVelocity.z) >= Math.Abs(localVelocity.x)
                ? localVelocity.z >= 0 ? "Front" : "Back" : localVelocity.x >= 0 ? "Right" : "Left";
            clipName = "alterDude@run" + direction + "_" + family;
        }
        else clipName = armed ? "alterDude@idleTarget_" + family : "AlterDude@idle";
        if (clipName != CurrentClip) { CurrentClip = clipName; _time = 0f; }
        var clip = _clips[clipName];
        _time += delta * (speed > 0.25f && clipName.Contains("run") ? Mathf.Clamp(speed / 6f, 0.4f, 1.5f) : 1f);
        clip.SampleAnimation(_target, _time % Mathf.Max(clip.length, 0.01f));
        // Network snapshots own motion. Imported run clips otherwise displace the character several units.
        _target.transform.localPosition = _rootPosition; _target.transform.localRotation = _rootRotation; _target.transform.localScale = _rootScale;
        _trajectory.localPosition = _trajectoryPosition; _trajectory.localRotation = _trajectoryRotation;
        _shotTime += delta;
    }

    internal void SampleForTest(string name, float time)
    {
        _clips[name].SampleAnimation(_target, time);
        _target.transform.localPosition = _rootPosition; _target.transform.localRotation = _rootRotation; _target.transform.localScale = _rootScale;
        _trajectory.localPosition = _trajectoryPosition; _trajectory.localRotation = _trajectoryRotation;
    }

    internal bool AllEventsStripped => _clips.Values.All(c => c.events.Length == 0);
    internal void AssertPlayback()
    {
        var joints = _target.GetComponentsInChildren<Transform>(true).Where(t => t.name.Contains("SHJnt")).ToArray();
        var before = joints.Select(t => t.localRotation).ToArray();
        SampleForTest("alterDude@runFront_machinegun", 0.05f);
        var first = joints.Select(t => t.localRotation).ToArray();
        SampleForTest("alterDude@runFront_machinegun", 0.3f);
        var moved = joints.Select((t, i) => Quaternion.Angle(first[i], t.localRotation)).Max();
        var fromBind = joints.Select((t, i) => Quaternion.Angle(before[i], t.localRotation)).Max();
        Plugin.Logger.LogInfo($"ANIMATION PROBE: animatedDelta={moved:F3}; bindDelta={fromBind:F3}; eventless={AllEventsStripped}");
        if (moved < 1f || !AllEventsStripped) throw new InvalidOperationException("Native clips must move the copied skeleton and contain no callbacks.");
        var networkRoot = _target.transform.root;
        var worldPosition = networkRoot.position;
        for (var i = 0; i < 20; i++) SampleForTest("alterDude@runFront_machinegun", i * 0.13f % 0.533f);
        if (Vector3.Distance(networkRoot.position, worldPosition) > 0.001f ||
            Vector3.Distance(_trajectory.localPosition, _trajectoryPosition) > 0.001f ||
            Vector3.Distance(_target.transform.localPosition, _rootPosition) > 0.001f)
            throw new InvalidOperationException("Animation root motion must not move the network avatar.");
        Plugin.Logger.LogInfo("SMOKE PASS: native run samples animate joints without root drift or callbacks.");
        SampleForTest("AlterDude@idle", 0);
    }
    public void Dispose() { foreach (var clip in _clips.Values) Object.Destroy(clip); _clips.Clear(); }
}
