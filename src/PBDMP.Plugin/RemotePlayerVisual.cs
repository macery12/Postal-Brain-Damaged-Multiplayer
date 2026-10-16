using Hyperstrange.PBD;
using PBDMP.Network;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PBDMP.Plugin;

internal sealed class RemotePlayerVisual : IDisposable
{
    internal GameObject Root { get; } = new("PBDMP Remote Player");
    internal bool HasDude { get; private set; }
    internal string ModelSource { get; private set; } = "Cylinder";
    internal string WeaponKey { get; private set; } = "";
    internal bool HasWeapon => _gun != null;
    internal bool FlashVisible => _muzzle?.IsPlaying == true;
    internal RemoteMuzzleEffect? Muzzle => _muzzle;
    internal RemoteAnimation? Animation => _animation;
    internal Quaternion GunRotation => _gun != null ? _gun.transform.rotation : Quaternion.identity;
    internal GameObject? GunObject => _gun;
    private GameObject? _body;
    private GameObject? _gun;
    private RemoteMuzzleEffect? _muzzle;
    private RemoteAnimation? _animation;
    private string _weaponName = "";
    private Material? _fallbackMaterial;
    private readonly List<Mesh> _ownedMeshes = new();
    private readonly List<Material> _ownedGunMaterials = new();
    private Transform? _rightShoulder, _rightWrist, _leftShoulder, _leftWrist, _leftHip, _leftAnkle, _rightHip, _rightAnkle;
    private readonly Dictionary<IntPtr, Quaternion> _bindRotations = new();
    private Transform[] _animatedJoints = Array.Empty<Transform>();
    private readonly Dictionary<string, WeaponId> _weapons = new();
    private float _modelHeight = 1.75f;
    private float _cycle;
    private uint _lastShot;
    private bool _hadShot;
    private Vector3 _previousPosition;
    private bool _hadPosition;

    internal RemotePlayerVisual(Color fallbackColor)
    {
        Object.DontDestroyOnLoad(Root);
        Root.layer = LayerManager.Layers.NPC;
        try
        {
            var npcId = Resources.Load<NpcId>("Npc/NoLifeMatters/NPC_AlterDude");
            var npc = npcId != null ? npcId._npcPrefab : null;
            var coat = npc != null ? npc.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.name == "geo_AlterDude") : null;
            var prefix = "DudeMenu";
            if (coat != null)
            {
                _body = VisualClone.SkinnedBody(npc!.transform, "geo_AlterDude");
                ModelSource = "Coat/gown Dude"; prefix = "AlterDude";
            }
            else
            {
                var menu = Resources.Load<GameObject>("MenuSceneComponent");
                var source = menu != null ? menu.transform.Find("Dude_Transform/PostalDudeMenu@bind") : null;
                if (source == null) throw new InvalidOperationException("Gown model not found.");
                _body = VisualClone.SkinnedBody(source, "NPC_PostalDud_body", "NPC_PostalDud");
                ModelSource = "Menu gown Dude";
            }
            _body.transform.SetParent(Root.transform, false);
            var renderers = _body.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            _modelHeight = Mathf.Max(0.5f, bounds.size.y);
            _body.transform.localPosition = new Vector3(0f, -bounds.min.y, 0f);
            _rightShoulder = Bone(prefix + "_r_Arm_ShoulderSHJnt"); _rightWrist = Bone(prefix + "_r_Arm_WristSHJnt");
            _leftShoulder = Bone(prefix + "_l_Arm_ShoulderSHJnt"); _leftWrist = Bone(prefix + "_l_Arm_WristSHJnt");
            _leftHip = Bone(prefix + "_l_Leg_HipSHJnt"); _leftAnkle = Bone(prefix + "_l_Leg_AnkleSHJnt");
            _rightHip = Bone(prefix + "_r_Leg_HipSHJnt"); _rightAnkle = Bone(prefix + "_r_Leg_AnkleSHJnt");
            foreach (var bone in _body.GetComponentsInChildren<Transform>(true)) _bindRotations[bone.Pointer] = bone.localRotation;
            _animatedJoints = new[] { _rightShoulder, _leftShoulder, _leftHip, _rightHip }.Where(t => t != null).Select(t => t!).ToArray();
            HasDude = true;
            if (coat != null)
            {
                try { _animation = new RemoteAnimation(_body, npc!.GetComponent<Animator>()); }
                catch (Exception ex) { Plugin.Logger.LogWarning("Native clips unavailable; procedural pose fallback: " + ex.Message); }
            }
            Plugin.Logger.LogInfo($"Remote {ModelSource} constructed: {renderers.Length} skinned meshes; height={_modelHeight:F2}; no scripts/physics/Animator copied.");
        }
        catch (Exception ex)
        {
            if (_body != null) Object.Destroy(_body);
            Plugin.Logger.LogWarning("Dude visual unavailable; using visible cylinder: " + ex.Message);
            _body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _body.transform.SetParent(Root.transform, false);
            var collider = _body.GetComponent<Collider>();
            if (collider != null) { collider.enabled = false; Object.Destroy(collider); }
            _fallbackMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            _fallbackMaterial.color = fallbackColor;
            _body.GetComponent<Renderer>().sharedMaterial = _fallbackMaterial;
        }
        foreach (var id in Resources.FindObjectsOfTypeAll<WeaponId>())
            if (id != null && !string.IsNullOrEmpty(id.ID)) _weapons[id.ID] = id;
    }

    internal void Update(PlayerPose pose, float delta)
    {
        _body!.SetActive(true);
        if (pose.Weapon != WeaponKey) SetWeapon(pose.Weapon);
        if (HasDude)
        {
            _body.transform.localScale = new Vector3(1.8f / _modelHeight, pose.Height / _modelHeight, 1.8f / _modelHeight);
            var speed = _hadPosition ? Vector3.Distance(Root.transform.position, _previousPosition) / Mathf.Max(delta, 0.001f) : 0f;
            var velocity = _hadPosition ? Root.transform.InverseTransformDirection(Root.transform.position - _previousPosition) / Mathf.Max(delta, 0.001f) : Vector3.zero;
            if (_animation != null) _animation.Update(_weaponName, velocity, delta);
            else
            {
                foreach (var bone in _animatedJoints) bone.localRotation = _bindRotations[bone.Pointer];
                _cycle += delta * Mathf.Min(speed, 12f) * 2.5f;
                var swing = Mathf.Sin(_cycle) * Mathf.Min(speed * 0.08f, 0.45f);
                PointBone(_leftHip, _leftAnkle, Vector3.down + Root.transform.forward * swing);
                PointBone(_rightHip, _rightAnkle, Vector3.down - Root.transform.forward * swing);
                var aim = Root.transform.rotation * Quaternion.Euler(pose.Pitch, 0f, 0f) * Vector3.forward;
                PointBone(_rightShoulder, _rightWrist, string.IsNullOrEmpty(pose.Weapon) ? Vector3.down : aim - Vector3.up * 0.1f);
                PointBone(_leftShoulder, _leftWrist, string.IsNullOrEmpty(pose.Weapon) ? Vector3.down : aim - Vector3.up * 0.1f);
            }
        }
        else
        {
            _body.transform.localScale = new Vector3(0.55f, pose.Height * 0.5f, 0.55f);
            _body.transform.localPosition = new Vector3(0f, pose.Height * 0.5f, 0f);
        }
        _previousPosition = Root.transform.position; _hadPosition = true;
        if(SharedWorld.Current is {} world)
        {
            if(world.PeerBlocking || world.PeerShielding)
            {PointBone(_rightShoulder,_rightWrist,Root.transform.forward+Vector3.up*0.5f);PointBone(_leftShoulder,_leftWrist,Root.transform.forward+Vector3.up*0.5f);}
            if(world.PeerKicking) PointBone(_rightHip,_rightAnkle,Root.transform.forward+Vector3.up*0.1f);
        }
        if (_gun != null)
        {
            _gun.transform.position = _rightWrist != null ? _rightWrist.position : Root.transform.TransformPoint(new Vector3(0.28f, pose.Height * 0.75f, 0.15f));
            _gun.transform.rotation = Root.transform.rotation * Quaternion.Euler(pose.Pitch, 0f, 0f);
        }
        if (_hadShot && pose.ShotSequence != _lastShot) { if(SharedWorld.Current==null) _muzzle?.Fire(); _animation?.Fire(); }
        _lastShot = pose.ShotSequence; _hadShot = true;
        _muzzle?.Update();
    }

    internal void ResetInterpolation() { _hadPosition = false; _hadShot = false; _muzzle?.Stop(); }

    private readonly HashSet<string> _missingGeometry=new(StringComparer.Ordinal);

    private void SetWeapon(string key)
    {
        if (_gun != null) Object.Destroy(_gun);
        _muzzle?.Dispose(); _muzzle = null;
        foreach (var mesh in _ownedMeshes) Object.Destroy(mesh);
        foreach (var material in _ownedGunMaterials) Object.Destroy(material);
        _ownedGunMaterials.Clear();
        _ownedMeshes.Clear(); _gun = null; _weaponName = "";
        WeaponKey = key;
        if (string.IsNullOrEmpty(key)) return;
        if (!_weapons.TryGetValue(key, out var id) || id == null)
        {
            foreach (var candidate in Resources.FindObjectsOfTypeAll<WeaponId>())
                if (candidate != null && candidate.ID == key) { id = candidate; _weapons[key] = candidate; break; }
        }
        if (id == null || id._pickupPrefab == null)
        { if(_missingGeometry.Add(key)) Plugin.Logger.LogWarning("No local weapon pickup geometry for remote ID " + key+"; keeping the remote character visible. Reported once per remote avatar."); return; }
        try
        {
            _gun = new GameObject("PBDMP Gun " + id.name);
            _gun.transform.SetParent(Root.transform, false);
            var fallbackMaterial = id.PlayerWeaponPrefab != null ? id.PlayerWeaponPrefab.GetComponentsInChildren<Renderer>(true)
                .SelectMany(r => r.sharedMaterials).FirstOrDefault(m => m != null &&
                    !m.name.Contains("Crosshair", StringComparison.OrdinalIgnoreCase) &&
                    (m.HasProperty("_BaseMap") || m.HasProperty("_MainTex"))) : null;
            var geometry = VisualClone.PickupGeometry(id._pickupPrefab, _ownedMeshes, _ownedGunMaterials, fallbackMaterial);
            geometry.transform.SetParent(_gun.transform, false);
            var bounds = GeometryBounds(geometry);
            var width = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (width < 0.01f) throw new InvalidOperationException("Empty weapon bounds.");
            var length = id.name.Contains("Pistol", StringComparison.OrdinalIgnoreCase) ? 0.45f : 0.8f;
            var scale = length / width;
            // Pickups are centered presentation meshes. Orient their longest horizontal axis down the aim direction.
            var rotation = bounds.size.x > bounds.size.z && bounds.size.x >= bounds.size.y ? Quaternion.Euler(0, -90, 0) :
                bounds.size.y > bounds.size.z ? Quaternion.Euler(90, 0, 0) : Quaternion.identity;
            geometry.transform.localRotation = rotation;
            geometry.transform.localScale = Vector3.one * scale;
            geometry.transform.localPosition = rotation * (-bounds.center * scale) + Vector3.forward * (length * 0.35f);
            _weaponName = id.name;
            var muzzle = new GameObject("PBDMP muzzle socket").transform;
            muzzle.SetParent(_gun.transform, false);
            muzzle.localPosition = new Vector3(0, 0, length * 0.85f);
            try { if (SharedWorld.Current==null && id.PlayerWeaponPrefab != null) _muzzle = new RemoteMuzzleEffect(id.PlayerWeaponPrefab, muzzle); }
            catch (Exception ex) { Plugin.Logger.LogWarning("Native muzzle effect unavailable: " + ex.Message); }
            Plugin.Logger.LogInfo("Remote gun visual equipped: " + id.name + "; render geometry only.");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning("Cannot construct gun visual: " + ex);
            if (_gun != null) Object.Destroy(_gun);
            _gun = null; _muzzle?.Dispose(); _muzzle = null;
        }
    }

    private Transform? Bone(string name) => _body!.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

    private static void PointBone(Transform? joint, Transform? end, Vector3 direction)
    {
        if (joint == null || end == null) return;
        joint.rotation = Quaternion.FromToRotation(end.position - joint.position, direction) * joint.rotation;
    }

    private static Bounds GeometryBounds(GameObject geometry)
    {
        var bounds = new Bounds(); var first = true;
        foreach (var filter in geometry.GetComponentsInChildren<MeshFilter>(true))
        {
            var b = filter.sharedMesh.bounds;
            for (var x = -1; x <= 1; x += 2)
            for (var y = -1; y <= 1; y += 2)
            for (var z = -1; z <= 1; z += 2)
            {
                var corner = geometry.transform.InverseTransformPoint(filter.transform.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3(x, y, z))));
                if (first) { bounds = new Bounds(corner, Vector3.zero); first = false; } else bounds.Encapsulate(corner);
            }
        }
        return bounds;
    }

    public void Dispose()
    {
        Object.Destroy(Root);
        if (_fallbackMaterial != null) Object.Destroy(_fallbackMaterial);
        _muzzle?.Dispose(); _animation?.Dispose();
        foreach (var mesh in _ownedMeshes) Object.Destroy(mesh);
        foreach (var material in _ownedGunMaterials) Object.Destroy(material);
    }
}
