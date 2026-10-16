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
    private GameObject? _gun,_leftGun;
    private bool _dual;
    private RemoteMuzzleEffect? _leftMuzzle;
    private RemoteMuzzleEffect? _muzzle;
    private RemoteAnimation? _animation;
    private string _weaponName = "";
    private WeaponGripProfile _grip=WeaponGripProfile.For("");
    private Transform? _pelvis,_leftKnee,_rightKnee;
    private Vector3 _pelvisBindPosition;
    private Transform[] _fallbackJoints=Array.Empty<Transform>();
    private Transform? _rightElbow,_leftElbow,_rightIndex,_leftIndex,_rightThumb,_leftThumb;
    private Vector3 _rightPalmAxis,_leftPalmAxis,_rightThumbAxis,_leftThumbAxis;
    private Material[] _teeth=Array.Empty<Material>();
    private float _teethPhase,_recoil=10,_equipTime=1,_aimPitch;
    private Transform[] _rightFingers=Array.Empty<Transform>(),_leftFingers=Array.Empty<Transform>();
    internal float GripError {get;private set;}
    internal float SupportError {get;private set;}
    internal Quaternion MuzzleRotation=>_gun!=null?_gun.transform.rotation:Root.transform.rotation;
    internal Vector3 MuzzlePosition=>_gun!=null?_gun.transform.TransformPoint(_muzzlePoint):Root.transform.position;
    private Vector3 _muzzlePoint;
    internal string PoseProfile=>_weaponName;

    private Material? _fallbackMaterial;
    private readonly List<Mesh> _ownedMeshes = new();
    private readonly List<Material> _ownedGunMaterials = new();
    private Transform? _rightShoulder, _rightWrist, _leftShoulder, _leftWrist, _leftHip, _leftAnkle, _rightHip, _rightAnkle;
    private readonly Dictionary<IntPtr, Quaternion> _bindRotations = new();
    private Transform[] _animatedJoints = Array.Empty<Transform>();
    private readonly Dictionary<string, WeaponId> _weapons = new();
    private float _modelHeight = 1.75f;
    private float _cycle;
    internal float FootError {get;private set;}
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
            _pelvis=Bone(prefix+"_ROOTSHJnt");_leftKnee=Bone(prefix+"_l_Leg_KneeSHJnt");_rightKnee=Bone(prefix+"_r_Leg_KneeSHJnt");
            if(_pelvis!=null)_pelvisBindPosition=_pelvis.localPosition;
            _rightElbow=Bone(prefix+"_r_Arm_ElbowSHJnt");_leftElbow=Bone(prefix+"_l_Arm_ElbowSHJnt");
            _rightIndex=Bone(prefix+"_r_Index_01_01SHJnt");_leftIndex=Bone(prefix+"_l_Index_01_01SHJnt");
            _rightThumb=Bone(prefix+"_r_Thumb_01_01SHJnt");_leftThumb=Bone(prefix+"_l_Thumb_01_01SHJnt");
            if(_rightWrist!=null && _rightIndex!=null && _rightThumb!=null)
            {_rightPalmAxis=_rightWrist.InverseTransformDirection(_rightIndex.position-_rightWrist.position);_rightThumbAxis=_rightWrist.InverseTransformDirection(_rightThumb.position-_rightWrist.position);}
            if(_leftWrist!=null && _leftIndex!=null && _leftThumb!=null)
            {_leftPalmAxis=_leftWrist.InverseTransformDirection(_leftIndex.position-_leftWrist.position);_leftThumbAxis=_leftWrist.InverseTransformDirection(_leftThumb.position-_leftWrist.position);}
            _leftHip = Bone(prefix + "_l_Leg_HipSHJnt"); _leftAnkle = Bone(prefix + "_l_Leg_AnkleSHJnt");
            _rightHip = Bone(prefix + "_r_Leg_HipSHJnt"); _rightAnkle = Bone(prefix + "_r_Leg_AnkleSHJnt");
            foreach (var bone in _body.GetComponentsInChildren<Transform>(true)) _bindRotations[bone.Pointer] = bone.localRotation;
            _fallbackJoints=_body.GetComponentsInChildren<Transform>(true).Where(t=>t.name.Contains("SHJnt")).ToArray();
            _animatedJoints = new[] { _rightShoulder, _leftShoulder, _leftHip, _rightHip }.Where(t => t != null).Select(t => t!).ToArray();
            _rightFingers=_body.GetComponentsInChildren<Transform>(true).Where(t=>t.name.Contains("_r_Index_01_")).ToArray();
            _leftFingers=_body.GetComponentsInChildren<Transform>(true).Where(t=>t.name.Contains("_l_Index_01_")).ToArray();
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
        _aimPitch=_hadPosition?Mathf.LerpAngle(_aimPitch,pose.Pitch,1-Mathf.Exp(-20*delta)):pose.Pitch;
        _equipTime+=delta;
        if (_hadShot && pose.ShotSequence != _lastShot) { if(_dual){if(pose.Animation.Has(PlayerActions.LeftHand))_leftMuzzle?.Fire();else _muzzle?.Fire();}else if(SharedWorld.Current==null) _muzzle?.Fire(); _animation?.Fire(); _recoil=0; }
        _lastShot=pose.ShotSequence;_hadShot=true;_recoil+=delta;
        if (HasDude)
        {
            _body.transform.localScale = Vector3.one*(1.8f/_modelHeight);
            var speed = _hadPosition ? Vector3.Distance(Root.transform.position, _previousPosition) / Mathf.Max(delta, 0.001f) : 0f;
            var velocity = _hadPosition ? Root.transform.InverseTransformDirection(Root.transform.position - _previousPosition) / Mathf.Max(delta, 0.001f) : Vector3.zero;
            if(_pelvis!=null)_pelvis.localPosition=_pelvisBindPosition;
            if (_animation != null)
            {
                var state=pose.Animation;
                if(SharedWorld.Current?.PeerKicking==true)state=state with{Actions=state.Actions|PlayerActions.Kicking};
                _animation.Update(_weaponName, velocity, delta, state);
            }
            else
            {
                foreach (var bone in _fallbackJoints) bone.localRotation = _bindRotations[bone.Pointer];
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
        ApplyCrouch(pose.Height);
        if(pose.Animation.Has(PlayerActions.Kicking) || SharedWorld.Current?.PeerKicking==true)
            PointBone(_rightHip,_rightAnkle,Root.transform.forward+Vector3.up*.1f);
        _previousPosition = Root.transform.position; _hadPosition = true;
        if (_gun != null) ApplyWeaponPose(pose,delta);
        _muzzle?.Update(delta);_leftMuzzle?.Update(delta);
    }

    internal void ResetInterpolation() { _hadPosition = false; _hadShot = false; _muzzle?.Stop(); }

    private readonly HashSet<string> _missingGeometry=new(StringComparer.Ordinal);

    private void SetWeapon(string key)
    {
        _equipTime=_gun!=null?0:1;
        if (_gun != null) Object.Destroy(_gun);
        _muzzle?.Dispose(); _muzzle = null;_leftMuzzle?.Dispose();_leftMuzzle=null;
        if(_leftGun!=null)Object.Destroy(_leftGun);_leftGun=null;_dual=false;
        foreach (var mesh in _ownedMeshes) Object.Destroy(mesh);
        foreach (var material in _ownedGunMaterials) Object.Destroy(material);
        _ownedGunMaterials.Clear();
        _ownedMeshes.Clear(); _gun = null; _weaponName = "";
        WeaponKey = key;_teeth=Array.Empty<Material>();_recoil=10;
        if (string.IsNullOrEmpty(key)) return;
        if (!_weapons.TryGetValue(key, out var id) || id == null)
        {
            foreach (var candidate in Resources.FindObjectsOfTypeAll<WeaponId>())
                if (candidate != null && candidate.ID == key) { id = candidate; _weapons[key] = candidate; break; }
        }
        if(id!=null && id._pickupPrefab==null && id.name.EndsWith("_Akimbo",StringComparison.Ordinal))
        {
            _dual=true;var original=id.name.Replace("_Akimbo","");
            id=_weapons.Values.FirstOrDefault(w=>w.name==original && w._pickupPrefab!=null);
        }
        if (id == null || id._pickupPrefab == null)
        { if(_missingGeometry.Add(key)) Plugin.Logger.LogWarning("No local weapon pickup geometry for remote ID " + key+"; keeping the remote character visible. Reported once per remote avatar."); return; }
        try
        {
            _gun = new GameObject("PBDMP Gun " + id.name);
            _gun.transform.SetParent(Root.transform, false);
            var fallbackMaterial = id.PlayerWeaponPrefab != null ? id.PlayerWeaponPrefab.GetComponentsInChildren<Renderer>(true)
                .SelectMany(r => r.sharedMaterials).FirstOrDefault(m => m != null &&
                    !m.name.Contains("Crosshair", StringComparison.OrdinalIgnoreCase) && !m.name.Contains("Hands",StringComparison.OrdinalIgnoreCase) &&
                    m.shader.name.StartsWith("Universal Render Pipeline/",StringComparison.Ordinal) && !m.shader.name.Contains("Particles") &&
                    (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap")!=null || m.HasProperty("_MainTex") && m.GetTexture("_MainTex")!=null)) : null;
            var geometry = VisualClone.PickupGeometry(id._pickupPrefab, _ownedMeshes, _ownedGunMaterials, fallbackMaterial);
            geometry.transform.SetParent(_gun.transform, false);
            _grip=WeaponGripProfile.For(id.name);
            if(_grip.NeutralNode.Length>0)
            {
                var node=geometry.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==_grip.NeutralNode);
                if(node!=null)node.localRotation=Quaternion.identity;
            }
            geometry.transform.localRotation=Quaternion.Euler(_grip.GeometryRotation);
            var bounds = GeometryBounds(_gun);
            var width = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (width < 0.01f) throw new InvalidOperationException("Empty weapon bounds.");
            var length=_grip.Length;
            var scale=length/Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z);
            geometry.transform.localScale=Vector3.one*scale;
            var gripPoint=bounds.min+Vector3.Scale(bounds.size,_grip.Grip);
            geometry.transform.localPosition=-gripPoint*scale;
            _supportPoint=Vector3.Scale(bounds.size,_grip.Support-_grip.Grip)*scale;
            _muzzlePoint=(new Vector3(bounds.center.x,bounds.min.y+bounds.size.y*.65f,bounds.max.z)-gripPoint)*scale;
            _teeth=_ownedGunMaterials.Where(m=>m.name.Contains("Teeth",StringComparison.OrdinalIgnoreCase)).ToArray();
            if(_dual)
            {
                _leftGun=new GameObject("PBDMP left gun "+id.name);_leftGun.transform.SetParent(Root.transform,false);
                var left=Object.Instantiate(geometry,_leftGun.transform,false);left.name="Left visual geometry";
            }
            _weaponName = id.name;
            var muzzle = new GameObject("PBDMP muzzle socket").transform;
            muzzle.SetParent(_gun.transform, false);
            muzzle.localPosition = _muzzlePoint;
            try { if ((SharedWorld.Current==null || _dual) && id.PlayerWeaponPrefab != null)
                {
                    _muzzle = new RemoteMuzzleEffect(id.PlayerWeaponPrefab, muzzle);
                    if(_leftGun!=null){var socket=new GameObject("Left muzzle socket").transform;socket.SetParent(_leftGun.transform,false);socket.localPosition=_muzzlePoint;_leftMuzzle=new RemoteMuzzleEffect(id.PlayerWeaponPrefab,socket);}
                } }
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

    private Vector3 _supportPoint;
    private void ApplyWeaponPose(PlayerPose pose,float delta)
    {
        var state=pose.Animation;
        var aim=Root.transform.rotation*Quaternion.Euler(_aimPitch,0,0);
        var hold=_grip.Hold;
        if(_dual)hold.x=Mathf.Max(.18f,Mathf.Abs(hold.x));
        hold.y-=Mathf.Sin(Mathf.Clamp01(_equipTime/.3f)*Mathf.PI)*.16f;
        var recoil=Mathf.Exp(-_recoil*24)*.045f;
        hold.z-=recoil;
        var swing=state.Has(PlayerActions.Melee) && !_grip.Bow ? Mathf.Sin(state.Phase*Mathf.PI*2) : 0;
        var rotation=aim*Quaternion.Euler(-recoil*100+swing*(_grip.Saw?18:8),swing*(_grip.Saw?28:0),0);
        if(state.Has(PlayerActions.Shielding)){hold.y+=.12f;rotation=aim*Quaternion.Euler(-30,0,-15);}
        if(state.Has(PlayerActions.Firing) && state.Has(PlayerActions.Alternate))rotation*=Quaternion.Euler(-4,0,0);
        if(state.Has(PlayerActions.Blocking))rotation=aim*Quaternion.Euler(-25,0,55);
        var pivot=new Vector3(0,pose.Height*.75f,0);
        hold.y*=pose.Height/1.8f;
        var anchor=Root.transform.position+Root.transform.rotation*pivot+aim*(hold-pivot);
        _gun!.transform.SetPositionAndRotation(anchor,rotation);
        var handAxis=rotation*Vector3.down;
        var handUp=rotation*Vector3.forward;
        for(var i=0;i<(_dual?1:4);i++)
        {
            anchor=ReachAnchor(_grip.Bow && !_dual?_leftShoulder:_rightShoulder,_grip.Bow && !_dual?_leftElbow:_rightElbow,_grip.Bow && !_dual?_leftWrist:_rightWrist,anchor,Vector3.zero,handAxis);
            if(_grip.TwoHands && !_dual)anchor=ReachAnchor(_grip.Bow && !_dual?_rightShoulder:_leftShoulder,_grip.Bow && !_dual?_rightElbow:_leftElbow,_grip.Bow && !_dual?_rightWrist:_leftWrist,anchor,rotation*_supportPoint,handAxis);
        }
        _gun.transform.position=anchor;
        var support=_gun.transform.TransformPoint(_supportPoint);
        if(_leftGun!=null)
        {
            var leftHold=hold;leftHold.x=-Mathf.Abs(hold.x);
            var leftAnchor=Root.transform.position+Root.transform.rotation*pivot+aim*(leftHold-pivot);
            leftAnchor=ReachAnchor(_leftShoulder,_leftElbow,_leftWrist,leftAnchor,Vector3.zero,handAxis);
            _leftGun.transform.SetPositionAndRotation(leftAnchor,rotation);
            GripError=SolveArm(_rightShoulder,_rightElbow,_rightWrist,anchor,handAxis,handUp,true);
            SupportError=SolveArm(_leftShoulder,_leftElbow,_leftWrist,leftAnchor,handAxis,handUp,false);
        }
        else if(_grip.Bow)
        {
            // Bow is carried by the left hand; the right hand draws the string toward the body.
            support-=rotation*Vector3.forward*(state.Has(PlayerActions.Charging)?Mathf.Clamp01(state.Charge)*.18f:0);
            GripError=SolveArm(_leftShoulder,_leftElbow,_leftWrist,anchor,handAxis,handUp,false);
            SupportError=SolveArm(_rightShoulder,_rightElbow,_rightWrist,support,handAxis,handUp,true);
        }
        else
        {
            GripError=SolveArm(_rightShoulder,_rightElbow,_rightWrist,anchor,handAxis,handUp,true);
            SupportError=_grip.TwoHands?SolveArm(_leftShoulder,_leftElbow,_leftWrist,support,handAxis,handUp,false):0;
        }
        CurlFingers(_rightFingers,rotation*Vector3.right);
        if(_grip.TwoHands)CurlFingers(_leftFingers,rotation*Vector3.right);
        _teethPhase=Mathf.Repeat(_teethPhase+delta*(state.Has(PlayerActions.SawRunning)?-100:1),1);
        foreach(var material in _teeth)
        {
            if(material.HasProperty("_BaseMap"))material.SetTextureOffset("_BaseMap",new(0,_teethPhase));
            if(material.HasProperty("_MainTex"))material.SetTextureOffset("_MainTex",new(0,_teethPhase));
        }
    }

    private void ApplyCrouch(float height)
    {
        if(_pelvis==null || _leftHip==null || _rightHip==null || _leftKnee==null || _rightKnee==null || _leftAnkle==null || _rightAnkle==null)return;
        var amount=Mathf.Clamp((1.8f-height)*.7f,0,.45f);
        if(amount<=0)return;
        var weight=Mathf.Clamp01(amount/.45f);
        var left=Vector3.Lerp(_leftAnkle.position,Root.transform.TransformPoint(new Vector3(-.17f,.1f,.04f)),weight);
        var right=Vector3.Lerp(_rightAnkle.position,Root.transform.TransformPoint(new Vector3(.17f,.1f,.04f)),weight);
        var leftRotation=_leftAnkle.rotation;var rightRotation=_rightAnkle.rotation;
        _pelvis.position-=Vector3.up*amount;
        BendLeg(_leftHip,_leftKnee,_leftAnkle,left);BendLeg(_rightHip,_rightKnee,_rightAnkle,right);
        _leftAnkle.rotation=leftRotation;_rightAnkle.rotation=rightRotation;
        FootError=Mathf.Max(Vector3.Distance(left,_leftAnkle.position),Vector3.Distance(right,_rightAnkle.position));
    }
    private void BendLeg(Transform hip,Transform knee,Transform ankle,Vector3 target)
    {
        var a=hip.position;var b=knee.position;var c=ankle.position;
        var upper=Vector3.Distance(a,b);var lower=Vector3.Distance(b,c);var direction=(target-a).normalized;
        var distance=Mathf.Clamp(Vector3.Distance(target,a),.005f,upper+lower-.001f);
        var pole=Root.transform.forward;pole=(pole-direction*Vector3.Dot(pole,direction)).normalized;
        var cos=Mathf.Clamp((upper*upper+distance*distance-lower*lower)/(2*upper*distance),-1,1);
        var desired=a+direction*(cos*upper)+pole*(Mathf.Sqrt(1-cos*cos)*upper);
        hip.rotation=Quaternion.FromToRotation(b-a,desired-a)*hip.rotation;
        knee.rotation=Quaternion.FromToRotation(ankle.position-knee.position,target-knee.position)*knee.rotation;
    }

    private static void CurlFingers(Transform[] fingers,Vector3 axis)
    {
        for(var i=0;i<fingers.Length;i++)fingers[i].rotation=Quaternion.AngleAxis(i==0?25:i==1?55:25,axis)*fingers[i].rotation;
    }

    private static Vector3 ReachAnchor(Transform? shoulder,Transform? elbow,Transform? wrist,Vector3 anchor,Vector3 support,Vector3 fingers)
    {
        if(shoulder==null || elbow==null || wrist==null)return anchor;
        var reach=Vector3.Distance(shoulder.position,elbow.position)+Vector3.Distance(elbow.position,wrist.position)-.008f;
        var offset=anchor+support-fingers*.05f-shoulder.position;
        return offset.magnitude>reach?anchor-offset.normalized*(offset.magnitude-reach):anchor;
    }

    private float SolveArm(Transform? shoulder,Transform? elbow,Transform? wrist,Vector3 palm,Vector3 forward,Vector3 up,bool right)
    {
        if(shoulder==null || elbow==null || wrist==null)return 0;
        var palmAxis=right?_rightPalmAxis:_leftPalmAxis;
        var thumbAxis=right?_rightThumbAxis:_leftThumbAxis;
        var palmLength=palmAxis.magnitude*Mathf.Abs(wrist.lossyScale.x)*.65f;
        var target=palm-forward*palmLength;
        var a=shoulder.position;var b=elbow.position;var c=wrist.position;
        var upper=Vector3.Distance(a,b);var lower=Vector3.Distance(b,c);
        var offset=target-a;var d=Mathf.Clamp(offset.magnitude,.005f,upper+lower-.001f);
        var direction=offset.normalized;
        var pole=Root.transform.TransformDirection(new Vector3(right?1:-1,-1,-.15f));
        pole=(pole-direction*Vector3.Dot(pole,direction)).normalized;
        var cos=Mathf.Clamp((upper*upper+d*d-lower*lower)/(2*upper*d),-1,1);
        var desired=a+direction*(cos*upper)+pole*(Mathf.Sqrt(1-cos*cos)*upper);
        shoulder.rotation=Quaternion.FromToRotation(b-a,desired-a)*shoulder.rotation;
        elbow.rotation=Quaternion.FromToRotation(wrist.position-elbow.position,target-elbow.position)*elbow.rotation;
        if(palmAxis.sqrMagnitude>1e-6f && thumbAxis.sqrMagnitude>1e-6f)
            wrist.rotation=Quaternion.LookRotation(forward,up)*Quaternion.Inverse(Quaternion.LookRotation(palmAxis,thumbAxis));
        return Vector3.Distance(wrist.position+forward*palmLength,palm);
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
            if(!filter.gameObject.activeInHierarchy)continue;
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
        if(_leftGun!=null)Object.Destroy(_leftGun);_leftMuzzle?.Dispose();
        _muzzle?.Dispose(); _animation?.Dispose();
        foreach (var mesh in _ownedMeshes) Object.Destroy(mesh);
        foreach (var material in _ownedGunMaterials) Object.Destroy(material);
    }
}
