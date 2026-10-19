using System.Security.Cryptography;
using System.Text;
using Hyperstrange.PBD;
using PBDMP.Network;
using UnityEngine;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    private readonly Dictionary<uint,Dictionary<string,Component>> _enemyParts=new();
    private readonly Dictionary<(uint Id,string Key),WorldFrame> _lastParts=new();
    private readonly NativeOriginals<Animator> _animatorEvents=new();
    private Dictionary<string,Component> EnemyParts(uint id,Npc npc)
    {
        if(_enemyParts.TryGetValue(id,out var parts)) return parts;
        parts=new();
        foreach(var component in npc.GetComponentsInChildren<Component>(true))
        {
            if(component.GetComponentInParent<Npc>()?.Pointer!=npc.Pointer) continue;
            if(component.TryCast<Renderer>()==null && component.TryCast<Collider>()==null) continue;
            var path=new List<string>();for(var t=component.transform;t!=null && t.Pointer!=npc.transform.Pointer;t=t.parent) path.Add(t.GetSiblingIndex()+":"+t.name);
            path.Reverse();var type=component.GetIl2CppType().FullName;
            var index=Array.FindIndex(component.GetComponents<Component>().Where(c=>c.GetIl2CppType().FullName==type).ToArray(),c=>c.Pointer==component.Pointer);
            var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("/",path)+"|"+type+"|"+index)));
            parts[key]=component;
        }
        _enemyParts[id]=parts;return parts;
    }
    private void SendEnemyPresentation(uint id,Npc npc,bool complete)
    {
        foreach(var pair in EnemyParts(id,npc))
        {
            if(pair.Value==null)
            {
                // A boss/balloon can destroy a cached visual part while its actor
                // is still alive. Send a tombstone instead of leaving it visible on P2.
                var removed=new WorldFrame(WorldKind.Enemy,_epoch,1,id,Key:pair.Key,Flags:4);
                if(complete || !_lastParts.TryGetValue((id,pair.Key),out var old) || old.Flags!=removed.Flags)
                {Send(removed);_lastParts[(id,pair.Key)]=removed;}
                continue;
            }
            var renderer=pair.Value.TryCast<Renderer>();var collider=pair.Value.TryCast<Collider>();
            var active=pair.Value.gameObject.activeSelf;var enabled=renderer!=null?renderer.enabled:collider!.enabled;
            var data=renderer!=null?renderer.sharedMaterials.Select(m=>m!=null?EffectKey("material",m.name):"").ToArray():Array.Empty<string>();
            var frame=new WorldFrame(WorldKind.Enemy,_epoch,1,id,Key:pair.Key,Flags:4u|(enabled?8u:0)|(active?16u:0),Data:data);
            if(complete || !_lastParts.TryGetValue((id,pair.Key),out var prior) || prior.Flags!=frame.Flags || !(prior.Data??Array.Empty<string>()).SequenceEqual(data))
            {Send(frame);_lastParts[(id,pair.Key)]=frame;}
        }
    }
    private void ApplyEnemyPresentation(WorldFrame frame)
    {
        if(!_enemies.TryGetValue(frame.Entity,out var npc) || npc==null || !EnemyParts(frame.Entity,npc).TryGetValue(frame.Key,out var component))
        {ReportPresentationMismatch(frame.Entity,frame.Key);return;}
        if(component==null) return;
        if(component.gameObject.Pointer!=npc.gameObject.Pointer) component.gameObject.SetActive((frame.Flags&16)!=0);
        var renderer=component.TryCast<Renderer>();var collider=component.TryCast<Collider>();
        if(renderer!=null)
        {
            renderer.enabled=(frame.Flags&8)!=0;
            var materials=renderer.sharedMaterials;var data=frame.Data??Array.Empty<string>();
            if(materials.Length==data.Length) {for(var i=0;i<data.Length;i++) if(_streamMaterials.TryGetValue(data[i],out var material)) materials[i]=material;renderer.sharedMaterials=materials;}
        }
        else if(collider!=null) collider.enabled=(frame.Flags&8)!=0;
    }
    private void SuppressEnemyAnimationEvents(Npc npc)
    {
        foreach(var animator in npc.GetComponentsInChildren<Animator>(true))
        {if(!_animatorEvents.ContainsKey(animator)) _animatorEvents[animator]=animator.fireEvents;animator.fireEvents=false;}
    }
    private void ClearEnemyPresentation()
    {foreach(var pair in _animatorEvents) if(pair.Key!=null) pair.Key.fireEvents=pair.Value;_animatorEvents.Clear();_enemyParts.Clear();_lastParts.Clear();}
}
