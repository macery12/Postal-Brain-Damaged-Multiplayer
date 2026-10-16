using Hyperstrange.PBD;
using PBDMP.Network;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PBDMP.Plugin;

// Opt-in render fixture. No gameplay prefabs or native weapon scripts are instantiated.
public sealed partial class MultiplayerBehaviour
{
    private RemotePlayerVisual? _poseProbe;
    private WeaponId[]? _poseWeapons;
    private int _poseIndex;
    private int _poseView;
    private float _poseStart;
    private uint _poseShot;
    private bool _posePending;
    private bool _poseBenchmark;
    private readonly string[] _poseViews = { "front", "back", "left", "right", "quarter", "grip", "aim-up", "aim-down", "aim-top", "aim-bottom", "crouch", "fire-early", "fire-late", "alternate", "charge", "block", "shield", "melee-early", "melee-late", "loop", "air", "slide", "kick" };
    private void RunPoseCapture()
    {
        if (_poseIndex == -1) return;
        if (_poseStart == 0) _poseStart = Time.unscaledTime;
        if (Time.unscaledTime - _poseStart > 180) throw new TimeoutException("Weapon capture stalled.");
        var output = SmokeOutput(); Directory.CreateDirectory(output);
        if (_poseWeapons == null)
        {
            _poseWeapons = Resources.FindObjectsOfTypeAll<WeaponId>().Where(w => w != null && (w._pickupPrefab != null || w.name.EndsWith("_Akimbo")) && w.PlayerWeaponPrefab != null)
                .GroupBy(w => w.ID).Select(g => g.First()).OrderBy(w => w.name).ToArray();
            var filter=Environment.GetEnvironmentVariable("PBDMP_POSE_FILTER");
            if(!string.IsNullOrEmpty(filter))_poseWeapons=_poseWeapons.Where(w=>w.name==filter).ToArray();
            if (_poseWeapons.Length == 0) throw new InvalidOperationException("No weapon assets available.");
            _poseProbe = new RemotePlayerVisual(Color.cyan);
            var dump = new System.Text.StringBuilder();
            dump.AppendLine("WEAPONS " + _poseWeapons.Length);
            foreach (var id in _poseWeapons)
            {
                dump.AppendLine($"WEAPON {id.name} | category={id.Category} | {id.ID} | {id.PlayerWeaponPrefab.GetIl2CppType().Name}");
                if(id._pickupPrefab==null)continue;
                foreach (var t in id._pickupPrefab.GetComponentsInChildren<Transform>(true))
                {
                    var renderer = t.GetComponent<Renderer>();
                    dump.AppendLine($"  {t.name} p={t.localPosition.ToString("F4")} r={t.localEulerAngles.ToString("F2")} s={t.localScale.ToString("F4")} mesh={t.GetComponent<MeshFilter>()?.sharedMesh?.name} bounds={renderer?.bounds.ToString()}");
                    if (renderer != null) foreach (var m in renderer.sharedMaterials)
                        if (m != null) dump.AppendLine($"    MAT {m.name} shader={m.shader?.name}");
                }
            }
            foreach (var id in _poseWeapons)
            {
                dump.AppendLine("FIRSTPERSON " + id.name);
                foreach(var t in id.PlayerWeaponPrefab.GetComponentsInChildren<Transform>(true))
                {
                    var renderer=t.GetComponent<Renderer>();
                    dump.AppendLine($"FP {t.name} | {t.parent?.name} | p={t.localPosition.ToString("F4")} r={t.localEulerAngles.ToString("F2")} s={t.localScale.ToString("F4")} mesh={t.GetComponent<MeshFilter>()?.sharedMesh?.name} bounds={t.GetComponent<MeshFilter>()?.sharedMesh?.bounds.ToString()}");
                    if(renderer!=null)foreach(var m in renderer.sharedMaterials)if(m!=null)
                        dump.AppendLine($"FPMAT {m.name} shader={m.shader?.name} base={(m.HasProperty("_BaseMap")?m.GetTexture("_BaseMap")?.name:"")} main={(m.HasProperty("_MainTex")?m.GetTexture("_MainTex")?.name:"")}");
                }
            }
            var npc = Resources.Load<NpcId>("Npc/NoLifeMatters/NPC_AlterDude");
            foreach (var clip in npc._npcPrefab.GetComponent<Animator>().runtimeAnimatorController.animationClips)
                dump.AppendLine($"CLIP {clip.name} {clip.length}");
            foreach (var t in _poseProbe.Root.GetComponentsInChildren<Transform>(true))
                if (t.name.Contains("SHJnt")) dump.AppendLine($"BONE {t.name} | {t.parent?.name} | {t.position}");
            File.WriteAllText(Path.Combine(output,"weapon-assets.txt"), dump.ToString());
            if(Environment.GetEnvironmentVariable("PBDMP_POSE_ASSETS_ONLY")=="1")_poseIndex=_poseWeapons.Length;
            return;
        }
        if (_poseIndex >= _poseWeapons.Length)
        {
            if(!_poseBenchmark)
            {
                _poseBenchmark=true;
                var benchmarkPose=new PlayerPose("POSE",true,0,0,0,0,1.8f,_poseWeapons.First(w=>w.name=="WEAPON_Shovel").ID);
                for(var i=0;i<200;i++)_poseProbe!.Update(benchmarkPose,1f/60);
                var samples=new double[1000];var allocation=GC.GetAllocatedBytesForCurrentThread();
                for(var i=0;i<samples.Length;i++)
                {
                    var start=System.Diagnostics.Stopwatch.GetTimestamp();_poseProbe!.Update(benchmarkPose,1f/60);
                    samples[i]=(System.Diagnostics.Stopwatch.GetTimestamp()-start)*1000d/System.Diagnostics.Stopwatch.Frequency;
                }
                allocation=GC.GetAllocatedBytesForCurrentThread()-allocation;Array.Sort(samples);
                File.WriteAllText(Path.Combine(output,"presentation-cost.txt"),$"Repeated native remote-avatar update, saw idle: mean={samples.Average():F5}ms; p95={samples[949]:F5}ms; max={samples[^1]:F5}ms; allocated={allocation} bytes/1000 updates. Excludes equip, rendering and whole-world sync.");
            }
            _poseProbe!.Dispose(); _poseProbe=null; _poseIndex=-1;
            Plugin.Logger.LogInfo("PBDMP POSE CAPTURE PASS: all weapon views saved."); return;
        }
        var weapon = _poseWeapons[_poseIndex]; var view = _poseViews[_poseView];
        if(_posePending)
        {
            if(view=="crouch")
            {
                var details=string.Join("\n",_poseProbe!.Root.GetComponentsInChildren<Transform>(true).Where(t=>t.name.Contains("Leg_Hip") || t.name.Contains("Leg_Knee") || t.name.Contains("Leg_Ankle")).Select(t=>$"{t.name}: {t.position.ToString("F5")}, scale={t.lossyScale.ToString("F4")}"));
                File.AppendAllText(Path.Combine(output,"feet-checks.txt"),weapon.name+" error="+_poseProbe.FootError+"\n"+details+"\n");
            }
            PoseCapture.Render(_poseProbe!.Root,view,Path.Combine(output,weapon.name+"-"+view+".png"));
            _posePending=false;
            if (++_poseView == _poseViews.Length) { _poseView=0; _poseIndex++; }
            return;
        }
        _poseProbe!.Root.transform.position = Vector3.zero;
        _poseProbe.Root.transform.rotation = Quaternion.identity;
        var pitch = view == "aim-up" ? -45f : view == "aim-down" ? 45f : view=="aim-top"?-85f:view=="aim-bottom"?85f:0;
        var pose=new PlayerPose("POSE",true,0,0,0,0,view=="crouch"?1.1f:1.8f,weapon.ID,pitch,ShotSequence:_poseShot);
        _poseProbe.ResetInterpolation();
        for(var i=0;i<90;i++)_poseProbe.Update(pose,1f/60);
        var actions=view switch
        {
            "fire-early" or "fire-late"=>PlayerActions.Firing,"alternate"=>PlayerActions.Firing|PlayerActions.Alternate,
            "charge"=>PlayerActions.Charging,"block"=>PlayerActions.Blocking,"shield"=>PlayerActions.Shielding,
            "melee-early" or "melee-late"=>PlayerActions.Melee,"loop"=>PlayerActions.Melee|PlayerActions.Loop,
            "kick"=>PlayerActions.Kicking,_=>PlayerActions.None
        };
        if((actions&PlayerActions.Melee)!=0)actions|=PlayerActions.SawRunning;
        var state=new PlayerAnimation(view=="air"?PlayerMotion.Airborne:view=="slide"?PlayerMotion.Sliding:PlayerMotion.Grounded,
            actions,++_poseShot,view=="melee-late"?.7f:.2f,view=="charge"?.7f:0);
        if(actions!=PlayerActions.None || state.Motion!=PlayerMotion.Grounded)
        {
            pose=pose with{Animation=state,ShotSequence=actions.HasFlag(PlayerActions.Firing)?_poseShot:pose.ShotSequence};
            for(var i=0;i<(view=="fire-late"?10:3);i++)_poseProbe.Update(pose,1f/60);
        }
        File.AppendAllText(Path.Combine(output,"grip-checks.tsv"),$"{weapon.name}\t{view}\t{_poseProbe.GripError:F5}\t{_poseProbe.SupportError:F5}\t{_poseProbe.Animation?.CurrentClip}\n");
        if(view=="crouch" && _poseProbe.FootError>.01f)throw new InvalidOperationException("Crouch feet lost their grounded targets.");
        if(_poseProbe.GripError>.035f || _poseProbe.SupportError>.035f)throw new InvalidOperationException($"Grip unreachable: {weapon.name}/{view}: {_poseProbe.GripError}/{_poseProbe.SupportError}");
        var mono=_poseProbe.Root.GetComponentsInChildren<MonoBehaviour>(true);
        if(mono.Length!=0 || _poseProbe.Root.GetComponentsInChildren<Collider>(true).Length!=0)throw new InvalidOperationException("Visual contains gameplay components.");
        // Unity updates skinned render data between frames. Rendering in the same Update
        // as 90 manual samples can show the preceding body's pose with the new gun.
        _posePending=true;
    }
}

internal static class PoseCapture
{
    internal static void Render(GameObject visual,string view,string path)
    {
        var cameraObject=new GameObject("PBDMP pose camera"); var lightObject=new GameObject("PBDMP pose light");
        var target=new RenderTexture(768,768,24);var texture=new Texture2D(768,768,TextureFormat.RGB24,false);
        var previous=RenderTexture.active;var transforms=visual.GetComponentsInChildren<Transform>(true);var layers=transforms.Select(t=>t.gameObject.layer).ToArray();
        try
        {
            foreach(var t in transforms)t.gameObject.layer=31;
            var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color(.055f,.065f,.08f,1);camera.cullingMask=1<<31;camera.nearClipPlane=.02f;camera.farClipPlane=30;
            camera.fieldOfView=33;camera.targetTexture=target;
            var direction=view switch {"front"=>new Vector3(0,.15f,1),"back"=>new Vector3(0,.15f,-1),"left"=>new Vector3(-1,.15f,0),"right"=>new Vector3(1,.15f,0),_=>new Vector3(1,.2f,1)};
            var center=visual.transform.position+Vector3.up*(view=="grip"?1.2f:view=="aim-top"?1.1f:.95f);
            camera.transform.position=center+direction.normalized*(view=="grip"?2.2f:view is "aim-top" or "aim-bottom"?4.4f:3.7f);camera.transform.LookAt(center);
            var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;light.cullingMask=camera.cullingMask;light.transform.rotation=Quaternion.Euler(30,155,0);
            camera.Render();RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,768,768),0,0);texture.Apply();
            File.WriteAllBytes(path,ImageConversion.EncodeToPNG(texture));
        }
        finally
        {
            for(var i=0;i<transforms.Length;i++)transforms[i].gameObject.layer=layers[i];
            RenderTexture.active=previous;target.Release();Object.Destroy(target);Object.Destroy(texture);Object.Destroy(cameraObject);Object.Destroy(lightObject);
        }
    }
}
