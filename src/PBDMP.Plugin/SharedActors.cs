using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Hyperstrange.PBD;
using Hyperstrange.Base;
using Hyperstrange.PBD.NpcComponents;
using PBDMP.Network;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    private readonly Dictionary<uint,Dictionary<string,Component>> _actorParts=new();
    private readonly Dictionary<(uint,string,uint,string),WorldFrame> _actorStates=new();
    private readonly Dictionary<uint,(Component Actor,Vector3 Position,Quaternion Rotation)> _actorMotion=new();
    private readonly NativeOriginals<Rigidbody> _actorBodies=new();
    private readonly HashSet<IntPtr> _observedActors=new();
    private readonly Dictionary<IntPtr,List<Component>> _actorSoundSources=new();
    private readonly Dictionary<string,FMODEvent> _actorSounds=new();
    private readonly Dictionary<string,RuntimeAnimatorController> _actorControllers=new();
    private readonly List<(Component Owner,object Audio)> _actorAudioComponents=new();
    private readonly HashSet<uint> _actorDeaths=new();
    private readonly HashSet<string> _invalidActorParameters=new();
    private sealed record ActorParameter(int Hash,AnimatorControllerParameterType Kind,string Name);
    private sealed record ActorAnimatorSchema(IntPtr Controller,string[] Name,ActorParameter[] Parameters);
    private readonly Dictionary<IntPtr,ActorAnimatorSchema> _actorAnimatorSchemas=new();
    private ActorAnimatorSchema AnimatorSchema(Animator animator,RuntimeAnimatorController controller)
    {
        if(_actorAnimatorSchemas.TryGetValue(animator.Pointer,out var schema) && schema.Controller==controller.Pointer) return schema;
        var parameters=animator.parameters.Select(p=>new ActorParameter(p.nameHash,p.type,p.name)).ToArray();
        _actorAnimatorSchemas[animator.Pointer]=schema=new(controller.Pointer,new[]{controller.name},parameters);
        return schema;
    }
    private readonly Dictionary<string,Component> _scriptActors=new();
    internal int ActorEffectsForTest {get;private set;}
    internal int ActorDeathEffectsForTest {get;private set;}
    internal uint ActorIdForTest(Npc npc)=>_enemyIds.GetValueOrDefault(npc.Pointer);
    internal int ActorPartHitsForTest {get;private set;}
    internal Component? ScriptActorForTest(string type)=>_catalogue?.Components.Values.FirstOrDefault(c=>c!=null && c.GetIl2CppType().Name==type);
    internal IReadOnlyDictionary<uint,Npc> ActorsForTest=>_enemies;
    private static readonly Dictionary<string,string[]> ActorFields=new(StringComparer.Ordinal)
    {
        ["FakeCitizen"]=new[]{"_running"},
        ["FakePostalDude"]=new[]{"_currentState","_shouldHide","_runTriggered","_pathDuration"},
        ["CovidEye"]=new[]{"_weakSpotHitted"},
        ["MiniCovidEye"]=new[]{"_weakSpotHitted"},
        ["BalloonGroup"]=new[]{"_isAlive"},
        ["ElonMechWeakSpotComponent"]=Array.Empty<string>(),
        ["ElonMechArmorPlateComponent"]=new[]{"_isDestroyed"},
        ["CovidArm"]=Array.Empty<string>(),
        ["GoliathLeg"]=Array.Empty<string>()
    };
    internal static string ActorPartKey(Component component,Transform root)
    {
        var path=new List<string>();
        for(var t=component.transform;t!=null && t.Pointer!=root.Pointer;t=t.parent) path.Add(t.GetSiblingIndex()+":"+t.name);
        path.Reverse();var type=component.GetIl2CppType().FullName;
        var index=Array.FindIndex(component.GetComponents<Component>().Where(c=>c.GetIl2CppType().FullName==type).ToArray(),c=>c.Pointer==component.Pointer);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("/",path)+"|"+type+"|"+index)));
    }
    private Dictionary<string,Component> ActorParts(uint id,Component actor)
    {
        if(_actorParts.TryGetValue(id,out var parts)) return parts;
        parts=new();
        foreach(var part in actor.GetComponentsInChildren<Component>(true))
        {
            var npc=part.GetComponentInParent<Npc>();
            if(actor.TryCast<Npc>()!=null && npc?.Pointer!=actor.Pointer) continue;
            if(part.TryCast<Animator>()==null && part.TryCast<ParticleSystem>()==null && part.TryCast<Light>()==null &&
                !ActorFields.ContainsKey(part.GetIl2CppType().Name) && !(actor.TryCast<Npc>()==null && (part.TryCast<Renderer>()!=null || part.TryCast<Collider>()!=null))) continue;
            parts[ActorPartKey(part,actor.transform)]=part;
        }
        _actorParts[id]=parts;return parts;
    }
    private void SendActorFrame(WorldFrame frame,bool complete,string scriptKey="")
    {
        var cache=(frame.Entity,frame.Key,frame.Flags,frame.Asset);
        if(complete || !_actorStates.TryGetValue(cache,out var prior) || prior.Value!=frame.Value || prior.Value2!=frame.Value2 ||
            prior.Position!=frame.Position || prior.Rotation!=frame.Rotation || prior.Asset!=frame.Asset || prior.Revision!=frame.Revision ||
            !(prior.Values??Array.Empty<float>()).SequenceEqual(frame.Values??Array.Empty<float>()) ||
            !(prior.Data??Array.Empty<string>()).SequenceEqual(frame.Data??Array.Empty<string>()))
        {Send(scriptKey.Length==0?frame:frame with{Entity=0,Asset=scriptKey});_actorStates[cache]=frame;}
    }
    private void SendActorState(uint id,Npc npc,bool complete)
    {
        ObserveActor(npc);
        var state=(npc.IsFrozen?1:0)|(npc.IsBurned?2:0)|(npc.IsConfused?4:0)|(npc.IsGibbed?8:0);
        var scale=npc.transform.lossyScale;
        SendActorFrame(new(WorldKind.ActorState,_epoch,1,id,Flags:1,Value:state,
            Value2:npc._npcHitProcessorComponent?.ShieldProcessor?.CurrentShield??-1,Values:new[]{scale.x,scale.y,scale.z}),complete);
        SendActorParts(id,npc,complete);
    }
    private void SendActorParts(uint id,Component actor,bool complete,string scriptKey="")
    {
        void Publish(WorldFrame frame)=>SendActorFrame(frame,complete,scriptKey);
        foreach(var pair in ActorParts(id,actor))
        {
            var part=pair.Value;if(part==null) continue;
            if(part.TryCast<Animator>() is {} animator && animator.runtimeAnimatorController is {} controller && controller!=null)
            {
                var schema=AnimatorSchema(animator,controller);
                for(var layer=0;layer<animator.layerCount;layer++)
                {
                    var state=animator.GetCurrentAnimatorStateInfo(layer);
                    if(!float.IsFinite(state.normalizedTime)) continue;
                    Publish(new(WorldKind.ActorState,_epoch,1,id,Key:pair.Key,Flags:2u+(uint)layer*256,
                        Value:animator.speed,Value2:animator.GetLayerWeight(layer),
                        Values:new[]{(float)(state.fullPathHash>>16),(float)(state.fullPathHash&65535),state.normalizedTime},
                        Data:schema.Name));
                }
                // Parameter hashes are split to preserve all 32 bits in float payloads.
                var parameters=schema.Parameters;
                for(var start=0;start<parameters.Length;start+=6)
                {
                    var values=new List<float>(30);
                    for(var index=start;index<Math.Min(start+6,parameters.Length);index++)
                    {
                        var parameter=parameters[index];
                        if(parameter.Kind==AnimatorControllerParameterType.Trigger) continue;
                        var scalar=parameter.Kind==AnimatorControllerParameterType.Float?animator.GetFloat(parameter.Hash):0;
                        if(!float.IsFinite(scalar) || Math.Abs(scalar)>1_000_000)
                        {
                            var diagnostic=actor.GetIl2CppType().Name+"/"+parameter.Name;
                            if(_invalidActorParameters.Add(diagnostic)) Plugin.Logger.LogWarning("Native actor animation parameter is not finite; retaining peer value until initialized: "+diagnostic);
                            continue;
                        }
                        values.Add(parameter.Hash>>16);values.Add(parameter.Hash&65535);values.Add((float)parameter.Kind);
                        if(parameter.Kind==AnimatorControllerParameterType.Int)
                        {var value=animator.GetInteger(parameter.Hash);values.Add(value>>16);values.Add(value&65535);}
                        else
                        {values.Add(parameter.Kind==AnimatorControllerParameterType.Float?scalar:animator.GetBool(parameter.Hash)?1:0);values.Add(0);}
                    }
                    Publish(new(WorldKind.ActorState,_epoch,1,id,Key:pair.Key,Flags:3u+(uint)start*256,Values:values.ToArray()));
                }
            }
            else if(part.TryCast<ParticleSystem>() is {} particle)
                Publish(new(WorldKind.ActorState,_epoch,1,id,Key:pair.Key,Flags:8,Value:particle.isPlaying?1:0));
            else if(part.TryCast<Light>() is {} light)
                Publish(new(WorldKind.ActorState,_epoch,1,id,Key:pair.Key,Flags:9,Value:light.enabled?light.intensity:-1));
            else if(part.TryCast<Renderer>() is {} renderer)
                Publish(new(WorldKind.ActorState,_epoch,1,id,Key:pair.Key,Flags:13,Value:renderer.enabled?1:0,Value2:renderer.gameObject.activeSelf?1:0,
                    Data:renderer.sharedMaterials.Select(m=>m!=null?EffectKey("material",m.name):"").ToArray()));
            else if(part.TryCast<Collider>() is {} collider)
                Publish(new(WorldKind.ActorState,_epoch,1,id,Key:pair.Key,Flags:13,Value:collider.enabled?1:0,Value2:collider.gameObject.activeSelf?1:0));
            else if(ActorFields.TryGetValue(part.GetIl2CppType().Name,out var fields))
            {
                var actual=NativeAssetGraph.Actual(part);var data=new List<string>();
                foreach(var name in fields)
                {
                    var property=actual.GetType().GetProperty(name);
                    if(property?.CanWrite!=true) continue;
                    data.Add(name);data.Add(Convert.ToString(property.GetValue(actual),CultureInfo.InvariantCulture)!);
                }
                var hp=PartHealth(part);
                Publish(new(WorldKind.ActorState,_epoch,1,id,Key:pair.Key,Flags:4,
                    Position:Net(part.transform.localPosition),Rotation:Net(part.transform.localEulerAngles),
                    Value:hp?.Current??-1,Value2:hp?.Max??-1,Data:data.ToArray(),Values:new[]{part.transform.localScale.x,part.transform.localScale.y,part.transform.localScale.z}));
            }
        }
    }
    private static HitPoints? PartHealth(Component part)=>part.TryCast<BalloonGroup>()?.HitPoints??part.TryCast<ElonMechWeakSpotComponent>()?.HitPoints;
    private Component? ActorFor(WorldFrame frame)
    {
        if(frame.Entity!=0) return _enemies.GetValueOrDefault(frame.Entity);
        return _scriptActors.GetValueOrDefault(frame.Asset);
    }
    private void ApplyActorState(WorldFrame frame)
    {
        if((frame.Flags&255)==5) {ApplyScriptActor(frame);return;}
        if(frame.Flags==6) {ApplyBossView(frame);return;}
        var actor=ActorFor(frame);if(actor==null) throw new InvalidDataException("Unknown shared actor: "+frame.Entity+"/"+frame.Asset);
        ObserveActorSounds(actor);
        if(frame.Flags==1)
        {
            var npc=actor.Cast<Npc>();var flags=(int)frame.Value;
            var changed=npc.IsFrozen!=((flags&1)!=0) || npc.IsBurned!=((flags&2)!=0);
            npc.IsFrozen=(flags&1)!=0;npc.IsBurned=(flags&2)!=0;npc.IsGibbed=(flags&8)!=0;
            typeof(Npc).GetProperty(nameof(Npc.IsConfused))!.SetValue(npc,(flags&4)!=0);
            if(frame.Value2>=0) npc._npcHitProcessorComponent?.ShieldProcessor?.SetCurrentArmor(frame.Value2);
            if(frame.Values is {Length:3} scale)
            {
                var parent=npc.transform.parent?.lossyScale??Vector3.one;
                npc.transform.localScale=new(Mathf.Abs(parent.x)>0.0001f?scale[0]/parent.x:scale[0],Mathf.Abs(parent.y)>0.0001f?scale[1]/parent.y:scale[1],Mathf.Abs(parent.z)>0.0001f?scale[2]/parent.z:scale[2]);
            }
            if(changed) foreach(var material in npc.GetComponentsInChildren<NpcMaterialComponent>(true))
            {
                if(npc.IsFrozen && material._freezeMap!=null) material.SetFreezeMap();
                else if(npc.IsBurned && material._burnMap!=null) material.SetBurnMap();
                else material.SetInitialMap();
            }
            return;
        }
        if(!ActorParts(frame.Entity!=0?frame.Entity:ScriptId(frame.Asset),actor).TryGetValue(frame.Key,out var part))
        {
            // A pooled/instantiated enemy can carry runtime-only children on P1 (e.g. Survival
            // Mage effects); authored scripted actors must still match exactly.
            if(frame.Entity!=0) {ReportPresentationMismatch(frame.Entity,frame.Key);return;}
            throw new InvalidDataException("Unknown shared actor part: "+actor.name+"/"+frame.Key);
        }
        if(part==null) return;
        switch(frame.Flags&255)
        {
            case 2:
                var animator=part.Cast<Animator>();var layer=(int)(frame.Flags>>8);var values=frame.Values;
                if(values is not {Length:3}) throw new InvalidDataException("Invalid actor animator layer.");
                if(frame.Data is {Length:1} controller && animator.runtimeAnimatorController?.name!=controller[0])
                {
                    if(!_actorControllers.TryGetValue(controller[0],out var asset))
                    {
                        asset=Resources.FindObjectsOfTypeAll<RuntimeAnimatorController>().FirstOrDefault(c=>c.name==controller[0]);
                        if(asset==null) throw new InvalidDataException("Unavailable actor animation controller: "+controller[0]);
                        _actorControllers[controller[0]]=asset;
                    }
                    animator.runtimeAnimatorController=asset;
                }
                if(!actor.gameObject.activeInHierarchy || animator.runtimeAnimatorController==null) break;
                if(layer>=animator.layerCount) throw new InvalidDataException("Invalid actor animator layer: "+actor.name+" layer="+layer+" available="+animator.layerCount);
                if(!_rootMotion.ContainsKey(animator)) _rootMotion[animator]=animator.applyRootMotion;
                if(!_animatorEvents.ContainsKey(animator)) _animatorEvents[animator]=animator.fireEvents;
                animator.fireEvents=false;animator.applyRootMotion=false;animator.speed=animator.gameObject.activeInHierarchy?frame.Value:0;
                var hash=((int)values[0]<<16)|(int)values[1];
                animator.SetLayerWeight(layer,frame.Value2);
                if(hash!=0) animator.Play(hash,layer,values[2]);
                break;
            case 3:
                var target=part.Cast<Animator>();var parameters=frame.Values??Array.Empty<float>();
                if(!actor.gameObject.activeInHierarchy || target.runtimeAnimatorController==null) break;
                if(parameters.Length%5!=0) throw new InvalidDataException("Invalid actor animation parameters.");
                for(var i=0;i<parameters.Length;i+=5)
                {
                    var name=((int)parameters[i]<<16)|(int)parameters[i+1];var type=(AnimatorControllerParameterType)parameters[i+2];
                    if(type==AnimatorControllerParameterType.Float) target.SetFloat(name,parameters[i+3]);
                    else if(type==AnimatorControllerParameterType.Int) target.SetInteger(name,((int)parameters[i+3]<<16)|(int)parameters[i+4]);
                    else if(type==AnimatorControllerParameterType.Bool) target.SetBool(name,parameters[i+3]!=0);
                    else throw new InvalidDataException("Unsupported actor animation parameter.");
                }
                break;
            case 4:
                var actual=NativeAssetGraph.Actual(part);var fields=frame.Data??Array.Empty<string>();
                if(fields.Length%2!=0 || !ActorFields.TryGetValue(part.GetIl2CppType().Name,out var allowed)) throw new InvalidDataException("Invalid actor fields.");
                for(var i=0;i<fields.Length;i+=2)
                {
                    if(!allowed.Contains(fields[i])) throw new InvalidDataException("Unknown actor field.");
                    var property=actual.GetType().GetProperty(fields[i])!;var type=property.PropertyType;
                    var value=type.IsEnum?Enum.Parse(type,fields[i+1]):Convert.ChangeType(fields[i+1],type,CultureInfo.InvariantCulture);
                    if(value is float scalar && !float.IsFinite(scalar)) throw new InvalidDataException("Invalid actor scalar.");
                    property.SetValue(actual,value);
                }
                var hp=PartHealth(part);if(hp!=null && frame.Value>=0) {hp.SetMax(frame.Value2);hp.SetCurrentSilent(frame.Value);}
                if(part.transform.Pointer!=actor.transform.Pointer)
                {
                    part.transform.localPosition=Unity(frame.Position);part.transform.localRotation=Quaternion.Euler(Unity(frame.Rotation));
                    if(frame.Values is {Length:3} partScale) part.transform.localScale=new(partScale[0],partScale[1],partScale[2]);
                }
                break;
            case 8:
                var particle=part.Cast<ParticleSystem>();
                if(frame.Value!=0 && !particle.isPlaying) particle.Play(false);
                else if(frame.Value==0 && particle.isPlaying) particle.Stop(false,ParticleSystemStopBehavior.StopEmitting);
                break;
            case 9:
                var light=part.Cast<Light>();light.enabled=frame.Value>=0;if(frame.Value>=0) light.intensity=frame.Value;
                break;
            case 13:
                if(part.gameObject.Pointer!=actor.gameObject.Pointer) part.gameObject.SetActive(frame.Value2!=0);
                if(part.TryCast<Renderer>() is {} renderer)
                {
                    renderer.enabled=frame.Value!=0;var materials=renderer.sharedMaterials;var data=frame.Data??Array.Empty<string>();
                    if(data.Length==materials.Length)
                    {for(var i=0;i<data.Length;i++)if(_streamMaterials.TryGetValue(data[i],out var material))materials[i]=material;renderer.sharedMaterials=materials;}
                }
                else if(part.TryCast<Collider>() is {} collider) collider.enabled=frame.Value!=0;
                break;
            default: throw new InvalidDataException("Unknown actor state adapter.");
        }
    }
    private void SetActorMotion(uint id,Component actor,NetVector position,NetVector rotation)
    {
        foreach(var body in actor.GetComponentsInChildren<Rigidbody>(true))
        {if(!_actorBodies.ContainsKey(body)) _actorBodies[body]=body.isKinematic;body.isKinematic=true;}
        var target=Unity(position);var orientation=Quaternion.Euler(Unity(rotation));
        if(!_snapshotApplied || !_actorMotion.ContainsKey(id) || Vector3.Distance(actor.transform.position,target)>8f)
            actor.transform.SetPositionAndRotation(target,orientation);
        _actorMotion[id]=(actor,target,orientation);
    }
    private void TickActorMotion()
    {
        if(!Client || _life.Frozen) return;
        var factor=1-Mathf.Exp(-25*Time.unscaledDeltaTime);
        foreach(var entry in _actorMotion.Values)
        {
            if(entry.Actor==null || !entry.Actor.gameObject.activeInHierarchy) continue;
            var transform=entry.Actor.transform;
            transform.SetPositionAndRotation(Vector3.Lerp(transform.position,entry.Position,factor),Quaternion.Slerp(transform.rotation,entry.Rotation,factor));
            if(entry.Actor.TryCast<Npc>() is {} npc) npc._npcPosition=transform.position;
        }
    }
    private void SendScriptedActors(bool complete)
    {
        foreach(var pair in _catalogue!.ScriptedActors)
        {
            var actor=pair.Value;if(actor==null) continue;_scriptActors[pair.Key]=actor;var id=ScriptId(pair.Key);
            ObserveActorSounds(actor);
            SendActorFrame(new(WorldKind.ActorState,_epoch,1,Asset:pair.Key,Flags:5,Value:actor.gameObject.activeSelf?1:0,
                Position:Net(actor.transform.position),Rotation:Net(actor.transform.eulerAngles)),complete);
            // Entity zero addresses an authored actor by Asset, never a cross-PC pointer.
            SendActorParts(id,actor,complete,pair.Key);
        }
    }
    private void SendBossView(bool complete)
    {
        var view=BossView();
        var boss=view?._currentBoss;var active=view!=null && view._healthBar!=null && view._healthBar.activeSelf && boss!=null;
        var id=active?Register(boss!):0;
        if(active && !_lastEnemy.ContainsKey(id)) SendEnemyIdentity(id,boss!);
        SendActorFrame(new(WorldKind.ActorState,_epoch,1,Revision:id,Key:"boss-hud",Flags:6,Value:active?1:0),complete);
    }
    private void ApplyBossView(WorldFrame frame)
    {
        var view=BossView();if(view==null) return;
        if(frame.Value==0)
        {
            if(view._currentBoss!=null) view.OnBossEncounterEnd(view._currentBoss);
            return;
        }
        if(!_enemies.TryGetValue(frame.Revision,out var boss) || boss==null) throw new InvalidDataException("Unknown boss HUD actor.");
        if(view._currentBoss?.Pointer!=boss.Pointer) view.OnBossEncounterStart(boss);
        view.OnHealthChanged(boss.HitPoints);
    }
    internal static Hyperstrange.PBD.PlayerComponents.PlayerBossView? BossView()=>Player.Instance?.GetComponentInChildren<Hyperstrange.PBD.PlayerComponents.PlayerBossView>(true)
        ??Object.FindObjectOfType<Hyperstrange.PBD.PlayerComponents.PlayerBossView>(true);
    private uint ScriptId(string key)=>0x80000000u|Id(key);
    private void ApplyScriptActor(WorldFrame frame)
    {
        if(!_catalogue!.Components.TryGetValue(frame.Asset,out var actor) || actor.GetIl2CppType().Name is not ("FakeCitizen" or "FakePostalDude")) throw new InvalidDataException("Unknown scripted actor.");
        if(!_scriptActors.ContainsKey(frame.Asset))
        {
            // A chase tween started during the local map intro must not keep
            // moving this authored actor after its host snapshot takes ownership.
            DG.Tweening.DOTween.Kill(actor.transform.Cast<Il2CppSystem.Object>(),false);
            DG.Tweening.DOTween.Kill(actor.Cast<Il2CppSystem.Object>(),false);
        }
        _scriptActors[frame.Asset]=actor;actor.gameObject.SetActive(frame.Value!=0);
        ObserveActorSounds(actor);
        if(actor.TryCast<MonoBehaviour>() is {} behaviour) {if(!_behaviours.ContainsKey(behaviour)) _behaviours[behaviour]=behaviour.enabled;behaviour.enabled=false;}
        foreach(var agent in actor.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) {if(!_agents.ContainsKey(agent)) _agents[agent]=agent.enabled;agent.enabled=false;}
        SetActorMotion(ScriptId(frame.Asset),actor,frame.Position,frame.Rotation);
    }
    internal void ClientScriptRun(Component actor)
    {
        if(_life.Frozen || _catalogue!.Key(actor) is not {} key) return;
        Send(new(WorldKind.Special,_epoch,1,Key:key,Flags:60,Actor:2,Life:_life.Life));
    }
    private void ApplyScriptRun(WorldFrame frame)
    {
        if(!_catalogue!.Components.TryGetValue(frame.Key,out var actor) || Vector3.Distance(actor.transform.position,_remote!.CenterOfMass)>100) return;
        if(actor.TryCast<FakeCitizen>() is {} citizen) citizen.TriggerRun();
        else actor.TryCast<FakePostalDude>()?.TriggeRun();
    }
    internal bool ClientActorHit(Component target,HitData hit)
    {
        if(!Client || !Active) return false;
        if(hit.projectile!=null || hit.isExplosion || hit.dealer?.Pointer!=Player.Instance?.Pointer || _life.Frozen) return true;
        var npc=target.GetComponentInParent<Npc>();
        uint id=0;string key;
        if(npc!=null) {if(!_enemyIds.TryGetValue(npc.Pointer,out id)) return true;key=ActorPartKey(target,npc.transform);}
        else {if(_catalogue!.Key(target) is not {} authored) return true;key=authored;}
        Send(new(WorldKind.Melee,_epoch,1,id,Key:key,Asset:hit.originEntity?.ID??"",Actor:2,Life:_life.Life,Flags:HitFlags(hit),
            Position:Net(hit.position),Rotation:Net(hit.direction),Velocity:Net(hit.normal),Value:hit.damage,Value2:hit.weakSpotMultiplier,
            Values:new[]{hit.ragdollForce,hit.chanceToGib,hit.pushVelocity.x,hit.pushVelocity.y,hit.pushVelocity.z}));
        return true;
    }
    private void ApplyActorPartHit(WorldFrame frame)
    {
        Component? part=null;
        if(frame.Entity!=0 && _enemies.TryGetValue(frame.Entity,out var npc)) ActorParts(frame.Entity,npc).TryGetValue(frame.Key,out part);
        else if(_catalogue!.Components.TryGetValue(frame.Key,out var scripted) && scripted.GetIl2CppType().Name is "FakeCitizen" or "FakePostalDude") part=scripted;
        if(part==null || Vector3.Distance(part.transform.position,_remote!.CenterOfMass)>100) return;
        ActorPartHitsForTest++;
        var hit=DecodeHit(frame);hit.dealer=_remote.Actor;
        hit.collider=part.GetComponent<Collider>()??part.GetComponentInChildren<Collider>();
        part.TryCast<IHittable>()?.Hit(hit);
    }
    private void ObserveActor(Npc npc)
    {
        ObserveActorSounds(npc);
    }
    private static string ActorSoundKey(FMODEvent sound)=>EffectKey("actor-sound",sound.name+"|"+sound.DefaultEvent.Guid.ToString());
    private void ObserveActorSounds(Component actor)
    {
        if(!_observedActors.Add(actor.Pointer)) return;
        foreach(var component in actor.GetComponentsInChildren<MonoBehaviour>(true))
        {
            var actual=NativeAssetGraph.Actual(component);
            if(actual.GetType().GetProperty("_fmodInstanceCache")!=null) _actorAudioComponents.Add((actor,actual));
            foreach(var property in actual.GetType().GetProperties(BindingFlags.Public|BindingFlags.Instance))
            {
                if(property.GetIndexParameters().Length!=0 || !typeof(FMODEvent).IsAssignableFrom(property.PropertyType)) continue;
                if(property.GetValue(actual) is not FMODEvent sound || sound==null) continue;
                _actorSounds[ActorSoundKey(sound)]=sound;
                if(!_actorSoundSources.TryGetValue(sound.Pointer,out var sources)) _actorSoundSources[sound.Pointer]=sources=new();
                if(!sources.Any(s=>s.Pointer==actor.Pointer)) sources.Add(actor);
            }
        }
    }
    internal void ActorSoundPlayed(FMODEvent sound,Vector3 position,GameObject? source=null)
    {
        if(Client || !Active || !_actorSoundSources.TryGetValue(sound.Pointer,out var sources)) return;
        var actor=sources.Where(s=>s!=null && s.gameObject.scene.IsValid()).OrderBy(s=>Vector3.Distance(s.transform.position,position)).FirstOrDefault();
        if(source!=null) actor=sources.FirstOrDefault(s=>s!=null && (source.transform.Pointer==s.transform.Pointer || source.transform.IsChildOf(s.transform)))??actor;
        if(actor==null || Vector3.Distance(actor.transform.position,position)>10) return;
        var npc=actor.TryCast<Npc>();var id=npc!=null?Register(npc):0;
        if(npc!=null) SendEnemyIdentity(id,npc);
        Send(new(WorldKind.ActorEffect,_epoch,1,id,Key:ActorSoundKey(sound),Asset:npc==null?_catalogue!.Key(actor)??"":"",Flags:21,Position:Net(position)));
    }
    internal void ActorSoundInstancePlayed(FMODInstance instance)
    {
        if(Client || !Active) return;
        foreach(var entry in _actorAudioComponents)
        {
            if(entry.Owner==null || entry.Audio is Component component && component==null) continue;
            if(entry.Audio.GetType().GetProperty("_fmodInstanceCache")?.GetValue(entry.Audio) is not Il2CppSystem.Collections.Generic.Dictionary<FMODEvent,FMODInstance> cache) continue;
            foreach(var pair in cache)
                if(pair.Value?.Pointer==instance.Pointer)
                {
                    var position=entry.Owner.TryCast<Npc>()?.CenterOfMass??entry.Owner.transform.position;
                    ActorSoundPlayed(pair.Key,position,entry.Owner.gameObject);return;
                }
        }
    }
    private void SendEnemyIdentity(uint id,Npc npc)
    {
        // Events can precede the next periodic state update, including a spawn-and-kill in one frame.
        Send(new(WorldKind.Enemy,_epoch,1,id,Key:_catalogue!.Key(npc.SpawnPoint)??"",Asset:WorldCatalogue.NpcAssetKey(npc.NpcId),
            Flags:(npc.IsAlive?1u:0u)|(npc.gameObject.activeInHierarchy?2u:0u),Position:Net(npc.Transform.position),Rotation:Net(npc.Transform.eulerAngles),Value:npc.HitPoints.Current,Value2:npc.HitPoints.Max));
    }
    internal void ActorDeathEffect(NpcDeathComponent component,string method)
    {
        if(Client || !Active) return;
        var npc=component.GetComponentInParent<Npc>();if(npc==null || npc.NpcId==null) return;
        ActorDeathEffectsForTest++;
        var id=Register(npc);SendEnemyIdentity(id,npc);var hit=npc.LastHitData;
        Send(new(WorldKind.ActorEffect,_epoch,1,id,Asset:method,Flags:10,Position:Net(npc.transform.position),Rotation:Net(npc.transform.eulerAngles),
            Revision:HitFlags(hit),Velocity:Net(hit.direction),Value:hit.ragdollForce,Value2:hit.chanceToGib,
            Values:new[]{hit.pushVelocity.x,hit.pushVelocity.y,hit.pushVelocity.z,hit.position.x,hit.position.y,hit.position.z,hit.normal.x,hit.normal.y,hit.normal.z,hit.weakSpotMultiplier},
            Data:new[]{npc.IsFrozen.ToString(),npc.IsGibbed.ToString(),hit.forceRagdoll.ToString(),hit.collider!=null?ActorPartKey(hit.collider,npc.transform):"",hit.originEntity?.ID??""}));
    }
    internal void ActorHitPresentation(Component component,HitData hit)
    {
        if(Client || !Active || component.GetComponentInParent<Npc>() is not {} npc || npc.NpcId==null) return;
        var id=Register(npc);SendEnemyIdentity(id,npc);
        // Flesh/armor bursts follow the native SolidManager hit path. Choosing
        // the first SolidOverride here loses material-specific blood effects.
        Send(new(WorldKind.ActorEffect,_epoch,1,id,Revision:HitFlags(hit),Key:ActorPartKey(component,npc.transform),Asset:hit.originEntity?.ID??"",Flags:30,
            Position:Net(hit.position),Rotation:Net(hit.direction),Velocity:Net(hit.normal),Value:hit.damage,Value2:hit.weakSpotMultiplier,
            Values:new[]{hit.ragdollForce,hit.chanceToGib,hit.pushVelocity.x,hit.pushVelocity.y,hit.pushVelocity.z}));
    }
    private void ApplyActorEffect(WorldFrame frame)
    {
        if(frame.Flags==21)
        {
            if(ActorFor(frame) is {} actor) ObserveActorSounds(actor);
            if(_actorSounds.TryGetValue(frame.Key,out var sound)) {sound.PlayOneShot(Unity(frame.Position));ActorEffectsForTest++;}
            else Plugin.Logger.LogWarning("Actor sound unavailable: "+frame.Key);
            return;
        }
        if(!_enemies.TryGetValue(frame.Entity,out var npc) || npc==null) return;
        if(frame.Flags==30)
        {
            var component=npc.GetComponentsInChildren<NpcVfxComponent>(true).FirstOrDefault(c=>ActorPartKey(c,npc.transform)==frame.Key);
            if(component==null) return;
            var presentationHit=DecodeHit(frame with{Flags=frame.Revision});
            presentationHit.collider=npc.GetComponentsInChildren<Collider>(true).Where(c=>c!=null).OrderBy(c=>Vector3.Distance(c.transform.position,presentationHit.position)).FirstOrDefault();
            var actual=NativeAssetGraph.Actual(component);actual.GetType().GetMethod("OnHit")!.Invoke(actual,new object[]{presentationHit});
            ActorEffectsForTest++;return;
        }
        if(frame.Flags!=10 || frame.Asset is not ("SpawnRagdoll" or "SpawnGib" or "SpawnFreezeGib" or "SpawnDeath" or "SpawnCrit")) throw new InvalidDataException("Unknown NPC death effect.");
        var death=npc.GetComponentInChildren<NpcDeathComponent>(true);if(death==null) return;
        var data=frame.Data??Array.Empty<string>();
        npc.transform.SetPositionAndRotation(Unity(frame.Position),Quaternion.Euler(Unity(frame.Rotation)));
        if(data.Length!=5 || frame.Values is not {Length:10} values) throw new InvalidDataException("Invalid native death presentation data.");
        npc.IsFrozen=bool.Parse(data[0]);npc.IsGibbed=bool.Parse(data[1]);
        var hit=DecodeHit(frame with{Flags=frame.Revision,Asset=data[4]});
        hit.direction=Unity(frame.Velocity);hit.ragdollForce=frame.Value;hit.chanceToGib=frame.Value2;hit.forceRagdoll=bool.Parse(data[2]);
        hit.pushVelocity=new(values[0],values[1],values[2]);hit.position=new(values[3],values[4],values[5]);hit.normal=new(values[6],values[7],values[8]);hit.weakSpotMultiplier=values[9];
        if(data[3].Length!=0 && EnemyParts(frame.Entity,npc).TryGetValue(data[3],out var source)) hit.collider=source.TryCast<Collider>();
        hit.collider??=npc.GetComponentsInChildren<Collider>(true).Where(c=>c!=null).OrderBy(c=>Vector3.Distance(c.transform.position,hit.position)).FirstOrDefault();
        npc._npcHitProcessorComponent.LastHitData=hit;
        // Invoke only the selected presentation helper, never NPC kill/death delegates.
        if(frame.Asset=="SpawnRagdoll") death.SpawnRagdoll();else if(frame.Asset=="SpawnGib") death.SpawnGib();
        else if(frame.Asset=="SpawnFreezeGib") death.SpawnFreezeGib();else if(frame.Asset=="SpawnCrit") death.SpawnCrit();else death.SpawnDeath();
        _actorDeaths.Add(frame.Entity);ActorEffectsForTest++;ActorDeathEffectsForTest++;
    }
    private void ClearActors()
    {
        foreach(var pair in _actorBodies) if(pair.Key!=null) pair.Key.isKinematic=pair.Value;
        _actorBodies.Clear();_actorAnimatorSchemas.Clear();
        _observedActors.Clear();_actorSoundSources.Clear();_actorSounds.Clear();_actorControllers.Clear();_actorAudioComponents.Clear();_actorParts.Clear();_actorStates.Clear();_actorMotion.Clear();_scriptActors.Clear();_actorDeaths.Clear();_invalidActorParameters.Clear();
    }
}
