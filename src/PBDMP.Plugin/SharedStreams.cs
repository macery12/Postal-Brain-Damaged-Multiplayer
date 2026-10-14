using Hyperstrange.Base;
using Hyperstrange.PBD;
using Hyperstrange.PBD.LightningGun;
using PBDMP.Network;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    private readonly Dictionary<string,GameObject> _streamPrefabs=new();
    private readonly Dictionary<string,Material> _streamMaterials=new();
    private readonly Dictionary<string,Mesh> _streamMeshes=new();
    private readonly Dictionary<IntPtr,uint> _localStreamIds=new();
    private readonly Dictionary<IntPtr,LightningGunBeam> _beamLogic=new();
    internal void BeamState(LightningGunBeam beam,bool active)
    {if(active) _beamLogic[beam.Pointer]=beam;else _beamLogic.Remove(beam.Pointer);}
    private readonly Dictionary<(byte Actor,uint Id),(NativePresentation Visual,float Until,FMODInstance? Audio)> _streams=new();
    private uint _streamId;
    private float _streamAt;
    private int _streamCapturePhase=-1;
    private void BuildStreams()
    {
        foreach(var component in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            if(!component.gameObject.scene.IsValid() && component.GetIl2CppType().Name is "LightningGunBeamRenderer" or "LineController" or "PeeStreamRenderer")
                _streamPrefabs[EffectKey("stream",component.name)]=component.gameObject;
        foreach(var source in Resources.FindObjectsOfTypeAll<Material>()) _streamMaterials[EffectKey("material",source.name)]=source;
        foreach(var source in Resources.FindObjectsOfTypeAll<Mesh>()) if(!source.name.StartsWith("Combined Mesh",StringComparison.Ordinal)) _streamMeshes[EffectKey("mesh",source.name)]=source;
    }
    private void TickStreams()
    {
        if(!_life.Frozen)
        {
            // Each type still polls once per cycle; avoid three scene-wide
            // Unity searches on the same gameplay frame, including empty scenes.
            if(_streamCapturePhase<0 && Time.unscaledTime>=_streamAt)
            {_streamAt=Time.unscaledTime+0.1f;_streamCapturePhase=0;}
            if(_streamCapturePhase==0)
            {
                foreach(var beam in Object.FindObjectsOfType<LightningGunBeamRenderer>())
                {
                    if(ReplicaStream(beam.transform)) continue;
                    var source=beam.OriginalPoolable?.TryCast<Component>();var name=source?.name??beam.name.Replace("(Clone)","",StringComparison.Ordinal);
                    var key=EffectKey("stream",name);if(!_streamPrefabs.ContainsKey(key)) continue;
                    var logic=_beamLogic.Values.FirstOrDefault(l=>l._mainBeamRenderer?.Pointer==beam.Pointer);var sound=logic?._beamActiveLoopEvent;
                    SendStream(beam,key,100,Net(beam._origin),Net(beam._end),Array.Empty<float>(),new[]{beam._material!=null?EffectKey("material",beam._material.name):"",beam._mesh!=null?EffectKey("mesh",beam._mesh.name):"",sound!=null?EffectKey("audio",sound.name):""});
                }
            }
            else if(_streamCapturePhase==1)
            {
                foreach(var line in Object.FindObjectsOfType<LineController>())
                {
                    if(ReplicaStream(line.transform) || line.Renderer==null || line.Renderer.positionCount<2) continue;
                    var source=line.OriginalPoolable?.TryCast<Component>();var name=source?.name??line.name.Replace("(Clone)","",StringComparison.Ordinal);
                    var key=EffectKey("stream",name);if(!_streamPrefabs.ContainsKey(key)) continue;
                    var renderer=line.Renderer;var points=new List<float>();var count=Math.Min(8,renderer.positionCount);
                    for(var i=0;i<count;i++) {var point=renderer.GetPosition(i*(renderer.positionCount-1)/(count-1));if(!renderer.useWorldSpace) point=renderer.transform.TransformPoint(point);points.Add(point.x);points.Add(point.y);points.Add(point.z);}
                    SendStream(line,key,101,default,default,points.ToArray(),Array.Empty<string>());
                }
            }
            else if(_streamCapturePhase==2)
            {
                foreach(var pee in Object.FindObjectsOfType<PeeStreamRenderer>())
                {
                    if(ReplicaStream(pee.transform) || pee._lineRenderer==null || pee._lineRenderer.positionCount<2) continue;
                    var source=pee.OriginalPoolable?.TryCast<Component>();var name=source?.name??pee.name.Replace("(Clone)","",StringComparison.Ordinal);
                    var key=EffectKey("stream",name);if(!_streamPrefabs.ContainsKey(key)) continue;
                    var renderer=pee._lineRenderer;var points=new List<float>();var count=Math.Min(8,renderer.positionCount);
                    var color=pee._baseColor;points.AddRange(new[]{color.r,color.g,color.b,pee._currentAlpha});
                    for(var i=0;i<count;i++) {var point=renderer.GetPosition(i*(renderer.positionCount-1)/(count-1));if(!renderer.useWorldSpace) point=renderer.transform.TransformPoint(point);points.Add(point.x);points.Add(point.y);points.Add(point.z);}
                    var dong=Player.Instance?.CurrentDong;var sound=!pee._disconnectedFromDong && dong?._currentPeeRenderer?.Pointer==pee.Pointer?dong._peeEventId:null;
                    SendStream(pee,key,102,Net(renderer.GetPosition(0)),default,points.ToArray(),sound!=null?new[]{EffectKey("audio",sound.name)}:Array.Empty<string>());
                }
            }
            if(_streamCapturePhase>=0) _streamCapturePhase=_streamCapturePhase==2?-1:_streamCapturePhase+1;
        }
        foreach(var pair in _streams.ToArray())
            if(!_life.Frozen && Time.unscaledTime>pair.Value.Until)
            {pair.Value.Visual.Dispose();pair.Value.Audio?.Stop(false);pair.Value.Audio?.Release();_streams.Remove(pair.Key);}
            else pair.Value.Audio?.SetVolume(_life.Frozen?0:1);
    }
    private static bool ReplicaStream(Transform transform)
    {for(var parent=transform;parent!=null;parent=parent.parent) if(parent.name.StartsWith("PBDMP visual ",StringComparison.Ordinal)) return true;return false;}
    private void SendStream(Component source,string key,uint type,NetVector origin,NetVector end,float[] points,string[] data)
    {
        if(!_localStreamIds.TryGetValue(source.Pointer,out var id)) _localStreamIds[source.Pointer]=id=++_streamId;
        Send(new(Client?WorldKind.ClientEffect:WorldKind.WeaponEffect,_epoch,1,id,Key:key,Flags:type,Actor:Client?(byte)2:(byte)1,Life:_life.Life,Position:origin,Velocity:end,Values:points,Data:data));
    }
    private void PlayStream(WorldFrame frame)
    {
        if(frame.Actor==(Client?2:1) || !_streamPrefabs.TryGetValue(frame.Key,out var prefab)) return;
        var key=(frame.Actor,frame.Entity);var data=frame.Data??Array.Empty<string>();
        if(!_streams.TryGetValue(key,out var stream))
        {
            var visual=new NativePresentation(prefab,frame.Flags==100);FMODInstance? sound=null;
            if(data.Length==3 && _effectSounds.TryGetValue(data[2],out var audio)) {sound=new(audio);sound.SetPosition(Unity(frame.Position));sound.Play();}
            else if(frame.Flags==102 && data.Length==1 && _effectSounds.TryGetValue(data[0],out var peeAudio)) {sound=new(peeAudio);sound.SetPosition(Unity(frame.Position));sound.Play();}
            stream=(visual,Time.unscaledTime+0.5f,sound);
        }
        if(frame.Flags==100)
        {
            var beam=stream.Visual.Root.GetComponent<LightningGunBeamRenderer>();if(beam==null) throw new InvalidDataException("Native beam presentation unavailable.");
            beam.SetRenderMode(LightningGunBeamRenderer.RenderModeEnum.WORLD_CAMERA);beam.SetPoints(Unity(frame.Position),Unity(frame.Velocity));
            if(data.Length==3) {if(_streamMaterials.TryGetValue(data[0],out var material)) beam.SetMaterial(material);if(_streamMeshes.TryGetValue(data[1],out var mesh)) beam.SetMesh(mesh);}
            stream.Audio?.SetPosition(Unity(frame.Position));
        }
        else if(frame.Flags is 101 or 102 && frame.Values is {} points)
        {
            if(frame.Flags==102)
            {
                if(data.Length==0 && stream.Audio!=null) {stream.Audio.Stop(false);stream.Audio.Release();stream=(stream.Visual,stream.Until,null);}
                else stream.Audio?.SetPosition(Unity(frame.Position));
            }
            var offset=frame.Flags==102?4:0;
            if(points.Length-offset<6 || (points.Length-offset)%3!=0) throw new InvalidDataException("Invalid native line presentation.");
            var renderer=stream.Visual.Root.GetComponent<LineRenderer>()??stream.Visual.Root.GetComponentInChildren<LineRenderer>();
            if(renderer!=null)
            {
                renderer.useWorldSpace=true;renderer.positionCount=(points.Length-offset)/3;
                for(var i=0;i<renderer.positionCount;i++) renderer.SetPosition(i,new(points[offset+i*3],points[offset+i*3+1],points[offset+i*3+2]));
                if(offset!=0) {var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",new(points[0],points[1],points[2],points[3]));renderer.SetPropertyBlock(block);}
            }
        }
        _streams[key]=(stream.Visual,Time.unscaledTime+0.5f,stream.Audio);
    }
    private void ClearStreams()
    {ClearRemoteStreams();_localStreamIds.Clear();_beamLogic.Clear();_streamPrefabs.Clear();_streamMaterials.Clear();_streamMeshes.Clear();_streamCapturePhase=-1;_streamAt=0;}
    private void ClearRemoteStreams()
    {foreach(var stream in _streams.Values){stream.Visual.Dispose();stream.Audio?.Stop(false);stream.Audio?.Release();}_streams.Clear();}
}
