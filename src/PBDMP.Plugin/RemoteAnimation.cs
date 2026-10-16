using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using PBDMP.Network;
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
    private Transform[] _joints = Array.Empty<Transform>();
    private Quaternion[] _previous = Array.Empty<Quaternion>(), _blendFrom = Array.Empty<Quaternion>();
    private Quaternion[] _bindJointRotations=Array.Empty<Quaternion>();
    private Vector3[] _bindJointPositions=Array.Empty<Vector3>();
    private float _blend=1;
    private string _weapon="";
    private string[] _familyClips=Families["machinegun"];
    private static readonly Dictionary<string,string[]> Families=new[]{"machinegun","shotgun","rocketlauncher"}.ToDictionary(f=>f,
        f=>new[]{"alterDude@idleTarget_"+f,"alterDude@shoot_"+f,"alterDude@inAir_"+f,"alterDude@runFront_"+f,"alterDude@runBack_"+f,"alterDude@runRight_"+f,"alterDude@runLeft_"+f});
    private static readonly string[] Slides={"alterDude@slideFront","alterDude@slideBack","alterDude@slideRight","alterDude@slideLeft"};
    private float _swapAt=10;
    private float _airTime;
    private PlayerMotion _motion;
    private float _time;
    private float _shotTime = 10f;
    internal float PlaybackTime=>_time;
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
        _joints=_target.GetComponentsInChildren<Transform>(true).Where(t=>t.name.Contains("SHJnt")).ToArray();
        _bindJointRotations=_joints.Select(t=>t.localRotation).ToArray();_bindJointPositions=_joints.Select(t=>t.localPosition).ToArray();
        _previous=_joints.Select(t=>t.localRotation).ToArray();_blendFrom=(Quaternion[])_previous.Clone();
        Plugin.Logger.LogInfo($"Native animation clips copied: {_clips.Count}; events stripped; controller-free Avatar binding.");
    }

    internal void Fire() { _shotTime = 0f; }

    internal void Update(string weaponName, Vector3 localVelocity, float delta, PlayerAnimation state=default)
    {

        if(state.Motion!=_motion){_motion=state.Motion;_airTime=0;}
        _airTime+=delta;
        var speed=new Vector2(localVelocity.x,localVelocity.z).magnitude;
        var armed=!string.IsNullOrEmpty(weaponName);
        var changed=weaponName!=_weapon;
        if(changed){if(_weapon.Length>0)_swapAt=0;_weapon=weaponName;_familyClips=Families[WeaponGripProfile.For(weaponName).Family];}
        var direction=Math.Abs(localVelocity.z)>=Math.Abs(localVelocity.x)?localVelocity.z>=0?0:1:localVelocity.x>=0?2:3;
        var shoot=_familyClips[1];
        var clipName=state.Has(PlayerActions.Kicking)?"alterDude@kick":state.Motion==PlayerMotion.Sliding?Slides[direction]:
            state.Motion==PlayerMotion.Airborne?(_airTime<.2f?"alterDude@jumpStart":_familyClips[2]):
            _swapAt<.3f?"alterDude@weaponSwap":
            armed && _shotTime<Mathf.Min(_clips[shoot].length,.3f)?shoot:
            speed>.25f?_familyClips[3+direction]:
            armed?_familyClips[0]:"AlterDude@idle";
        if(!_clips.ContainsKey(clipName))clipName="AlterDude@idle";
        if(clipName!=CurrentClip)
        {
            Array.Copy(_previous,_blendFrom,_previous.Length);_blend=0;CurrentClip=clipName;_time=0;
        }
        var clip=_clips[clipName];
        _time+=delta*(state.Has(PlayerActions.Kicking)?3:speed>.25f && clipName.Contains("run")?Mathf.Clamp(speed/6f,.4f,1.5f):1);
        ResetJoints();
        // Targeting clips contain only an upper-body layer. Start from a native
        // idle base so procedural IK/crouch cannot accumulate in unkeyed joints.
        var idle=_clips["AlterDude@idle"];
        if(clipName!="AlterDude@idle")idle.SampleAnimation(_target,_time%idle.length);
        clip.SampleAnimation(_target,_time%Mathf.Max(clip.length,.01f));
        _blend=Mathf.Min(1,_blend+delta/0.12f);
        for(var i=0;i<_joints.Length;i++)
        {
            if(_blend<1)_joints[i].localRotation=Quaternion.Slerp(_blendFrom[i],_joints[i].localRotation,_blend);
            _previous[i]=_joints[i].localRotation;
        }
        ResetRoot();_shotTime+=delta;_swapAt+=delta;
    }

    private void ResetJoints()
    {
        for(var i=0;i<_joints.Length;i++){_joints[i].localPosition=_bindJointPositions[i];_joints[i].localRotation=_bindJointRotations[i];}
    }

    private void ResetRoot()
    {
        _target.transform.localPosition=_rootPosition;_target.transform.localRotation=_rootRotation;_target.transform.localScale=_rootScale;
        _trajectory.localPosition=_trajectoryPosition;_trajectory.localRotation=_trajectoryRotation;
    }

    internal void SampleForTest(string name, float time)
    {
        ResetJoints();
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
