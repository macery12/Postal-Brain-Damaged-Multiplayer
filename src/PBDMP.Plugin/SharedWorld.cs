using Hyperstrange.PBD;
using System.Diagnostics;
using Hyperstrange.PBD.NpcComponents;
using Il2CppInterop.Runtime;
using PBDMP.Network;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld : IDisposable
{
    internal static SharedWorld? Current;
    private readonly CoopSession _session;
    private readonly SessionLifecycle _life;
    private WorldCatalogue? _catalogue;
    private NativeAssetGraph? _assets;
    private readonly Dictionary<uint,Npc> _enemies = new();
    private readonly Dictionary<IntPtr,uint> _enemyIds = new();
    private readonly HashSet<IntPtr> _enemyRespawns=new();
    private readonly Dictionary<uint,WorldFrame> _lastEnemy = new();
    private readonly HashSet<uint> _snapshotEnemies = new();
    private readonly Dictionary<string,uint> _componentIds = new();
    private readonly Dictionary<string,string[]> _lastDoor = new();
    private readonly Dictionary<string,WorldFrame> _lastMotion = new();
    private readonly Dictionary<Rigidbody,bool> _doorBodies = new();
    private readonly Dictionary<uint,ulong> _revisions = new();
    private readonly Dictionary<UnityEngine.AI.NavMeshAgent,bool> _agents = new();
    private readonly Dictionary<Animator,bool> _rootMotion = new();
    private readonly Dictionary<MonoBehaviour,bool> _behaviours = new();
    private readonly List<GameObject> _ownedEnemies = new();
    private RemoteActor? _remote;
    private uint _epoch, _nextId, _mapGeneration;
    private ulong _sequence, _peerSequence;
    private float _sendAt, _manifestAt;
    private float _stateAt;
    private IEnumerator<int>? _stateCapture;
    private long _stateWorkTicks,_stateAllocated;
    private Vector3 _remoteFeet;
    private float _remotePoseAt;
    private bool _peerMatched, _snapshotApplied, _applying;
    private bool _validatingAuditFrames;
    private readonly Queue<WorldFrame> _outbound=new();
    internal bool Client => _life.IsClient;
    internal bool Applying => _applying;
    internal RemoteActor? Remote => _remote;
    internal Npc? EnemyForTest(string asset)=>_enemies.Values.FirstOrDefault(n=>n!=null && n.gameObject.activeInHierarchy && n.NpcId!=null && (n.NpcId.ID==asset || WorldCatalogue.NpcAssetKey(n.NpcId)==asset) && n.IsAlive);
    internal Npc? EnemyForSpawnForTest(string key)=>_lastEnemy.Where(p=>p.Value.Key==key && (p.Value.Flags&1)!=0).Select(p=>_enemies.GetValueOrDefault(p.Key)).FirstOrDefault(n=>n!=null && n.IsAlive);
    internal Component? ComponentForTest(string key)=>_catalogue?.Components.GetValueOrDefault(key);
    internal string? KeyForTest(Component component)=>_catalogue?.Key(component);
    internal WorldCatalogue CatalogueForTest=>_catalogue!;
    internal bool SharedComponent(Component component)=>_catalogue?.Key(component) is {} key && (_catalogue.DoorKeys.Contains(key) || _catalogue.EncounterKeys.Contains(key));
    internal bool Active => _life.NativeReady && _catalogue != null && _life.State.Action != TravelAction.Menu;
    internal bool ReadyForPerformance=>Active && !Client && _peerMatched && _session.Snapshot.Connected && _life.State.Released && !_life.Frozen;
    internal int IncomingHitApplications { get; private set; }
    internal int PersonalLootForTest=>_personalLoot.Count;
    internal IReadOnlyDictionary<string,ProjectileModel> ModelsForTest=>_assets!.Models;
    internal string ModelOriginForTest(string key)=>_assets!.ModelOrigins[key];
    internal int ProjectileCreatesForTest { get; private set; }
    internal int ReflectionCreatesForTest { get; private set; }
    internal int LocalReflectionCreatesForTest { get; private set; }
    internal int ProjectilePresentationsForTest=>_projectileVisuals.Count;
    internal int ProjectileSoundsForTest=>_projectileAudio.Count;
    private readonly Dictionary<string,int> _attackOrigins=new();
    internal int AttackOriginCount(string origin)=>_attackOrigins.GetValueOrDefault(origin);
    internal string CatalogueReport()
    {
        var report=new System.Text.StringBuilder();
        report.AppendLine($"{LevelManager.CurrentLevel.name}\t{_catalogue!.Manifest}\t{_catalogue.Components.Count}\t{_catalogue.DoorMotion.Count}\t{_catalogue.EncounterMotion.Count}");
        foreach(var group in _catalogue.Components.Values.GroupBy(c=>c.GetIl2CppType().Name).OrderBy(g=>g.Key)) report.AppendLine(group.Key+"\t"+group.Count());
        foreach(var key in _catalogue.DoorKeys) WorldCatalogue.Save(_catalogue.Components[key]);
        SendEncounters(true); // Exercise the bounded, paged typed snapshot without any peer.
        _validatingAuditFrames=true;
        try {SendState(true);} finally {_validatingAuditFrames=false;}
        return report.ToString();
    }
    internal SharedWorld(CoopSession session,SessionLifecycle life)
    { _session=session; _life=life; Current=this; if (Client) life.WorldReady=false; }
    internal static NetVector Net(Vector3 v)=>new(v.x,v.y,v.z);
    internal static Vector3 Unity(NetVector v)=>new(v.X,v.Y,v.Z);

    internal void Tick()
    {
        if (_epoch!=_life.Epoch || _mapGeneration!=_life.MapGeneration)
        {
            CancelStateCapture();_stateAt=0;
            ReleaseActor(); ClearWeaponAudio(); RefundPendingDoorRequests();
            // Joining changes the travel epoch without reloading P1's world. Keep its current
            // entity lifetimes and loot ledger so the new peer receives the ongoing encounter.
            var keepHostWorld=!Client && _catalogue!=null && _life.NativeReady && _mapGeneration==_life.MapGeneration && _life.State.Action!=TravelAction.Menu;
            if(!keepHostWorld)
            {
                ClearSequences();ClearScenery();ClearMechanisms();ClearMissions();
                ClearPosters(); ClearProjectiles(); ClearLoot(); ClearEncounters(); ClearStreams(); ClearActors(); ClearEnemyPresentation(); ClearUrine(); _catalogue=null; _assets=null; _enemies.Clear(); _enemyIds.Clear(); _lastEnemy.Clear();
                _componentIds.Clear(); _lastDoor.Clear(); _lastMotion.Clear(); _revisions.Clear(); _snapshotEnemies.Clear(); _enemyRespawns.Clear(); _nextId=0;
            }
            _epoch=_life.Epoch; _mapGeneration=_life.MapGeneration;
            _outbound.Clear();
            _remoteInside.Clear();_remoteTriggerInside.Clear();
            _peerMatched=false; _snapshotApplied=false; _peerSequence=0;
            _lastMissionWait=null;
            _life.WorldReady=!Client || _life.State.Action==TravelAction.Menu;
        }
        if (!_life.NativeReady || _life.State.Action==TravelAction.Menu) return;
        if (_catalogue==null)
        { MultiplayerBehaviour.EnsurePropSpawnerFixture();_catalogue=new(); _catalogue.Build(); _assets=new(); _assets.Build(); BuildProjectilePrefabs(); BuildLootPrefabs(); BuildExplosionAssets(); BuildStreams(); BuildUrineSounds(); BuildUrineRails(); BuildPosters(); }
        if(!Client) TickLoot();
        var snapshot=_session.Snapshot;
        TickWorldMechanics();TickBreakDebris();TickSequences();
        TickMissionProgress();
        TickUrinePause();TickWorldAudioPause();
        if (!snapshot.Connected)
        { CancelStateCapture();ReleaseActor(); _peerMatched=false; _outbound.Clear(); return; }
        var captureDeadline=Stopwatch.GetTimestamp()+(long)(Stopwatch.Frequency*0.003);
        if (!Client)
        {
            if (!_life.RestoringWorld && snapshot.RemotePose is {Ready:true} pose && pose.Epoch==_epoch)
            {
                _remote ??= RemoteActor.Create(SendIncomingHit,SendPlayerContact,SendRemotePush,CurrentRemoteCarrierFeet,CurrentRemoteCarrierTransform);
                var feet=ResolvePoseFeet(pose);var velocity=_remote.Velocity;
                if(_remotePoseAt==0 || !pose.Alive) velocity=Vector3.zero;
                else if(feet!=_remoteFeet) velocity=Vector3.ClampMagnitude((feet-_remoteFeet)/Mathf.Max(0.01f,Time.unscaledTime-_remotePoseAt),60);
                else if(Time.unscaledTime-_remotePoseAt>0.15f) velocity=Vector3.zero;
                if(feet!=_remoteFeet || _remotePoseAt==0) {_remoteFeet=feet;_remotePoseAt=Time.unscaledTime;}
                _remote.SetPose(feet,pose.Yaw,pose.Height,pose.Alive,velocity);
                if(!pose.Alive) ReleaseUrineRider();
            }
            if (!_peerMatched && Time.unscaledTime>=_manifestAt)
            { _manifestAt=Time.unscaledTime+0.5f; Send(new(WorldKind.Manifest,_epoch,1,Key:_catalogue.Manifest)); }
        }
        foreach (var frame in _session.DrainWorld())
        {
            if (frame.Epoch!=_epoch || frame.Sequence<=_peerSequence) continue;
            _peerSequence=frame.Sequence;
            Receive(frame);
        }
        RefreshDoorPortals();
        if(Client) TickReplicaPlatformCarry();
        TickNativeProjectiles();
        if (!Client && _peerMatched)
        {
            if(Time.unscaledTime>=_sendAt)
            { var perf=SyncPerformance.Start();_sendAt=Time.unscaledTime+0.1f;TickDoorProximity();SendProjectileState(false);SyncPerformance.Finish("proximity-projectiles",perf); }
        }
        TickDoorRequests();
        TickEncounterTriggers();
        TickPresentations();
        TickWeaponAudio();
        var streamPerf=SyncPerformance.Start();TickStreams();SyncPerformance.Finish("streams",streamPerf);
        if(!Client && _peerMatched) TickStateCapture(captureDeadline);
        TickRemoteWeapons();
        TickPlayerActions();
        TickActorMotion();
        foreach(var pair in _reflectionHits.ToArray()) if(Time.unscaledTime>pair.Value.Until) _reflectionHits.Remove(pair.Key);
        var publishPerf=SyncPerformance.Start();
        for(var i=0;i<128 && _outbound.Count>0;i++)
        {if(!_session.PublishWorld(_outbound.Peek())) break;_outbound.Dequeue();}
        SyncPerformance.Finish("publish",publishPerf);
    }
    private void Send(WorldFrame frame)
    {
        if(_persistentFrames!=null)
        {
            if(!WorldProtocol.Durable(frame)) return;
            _persistentFrames.Add(frame with {Epoch=1,Sequence=1});
            // Capture updates the same delta caches as live replication. Keep
            // publishing durable state through the bounded live queue, or a
            // change first observed by a save becomes "already sent" forever.
            // Historical/transient effects remain excluded from capture.
        }
        if(!_session.Snapshot.Connected && !_validatingAuditFrames) return;
        frame=frame with {Epoch=_epoch,Sequence=++_sequence};
        WorldProtocol.Validate(frame);
        if(!_session.Snapshot.Connected) return;
        if(_outbound.Count>=16384) throw new InvalidOperationException("Shared-world backlog exceeded its bounded snapshot/event budget.");
        _outbound.Enqueue(frame);
    }
    private uint Id(string key)
    { if (!_componentIds.TryGetValue(key,out var id)) _componentIds[key]=id=++_nextId; return id; }
    private uint Register(Npc npc)
    {
        if (_enemyIds.TryGetValue(npc.Pointer,out var id))
        {
            if(!_enemyRespawns.Remove(npc.Pointer)) return id;
            if(_lastEnemy.TryGetValue(id,out var old)) Send(old with {Flags=0});
            _enemies.Remove(id);
        }
        id=++_nextId; _enemyIds[npc.Pointer]=id; _enemies[id]=npc; return id;
    }
    internal void EnemyEnabled(Npc npc)
    {
        if(Client || !Active || !npc.gameObject.scene.IsValid()) return;
        if(_enemyIds.ContainsKey(npc.Pointer)) _enemyRespawns.Add(npc.Pointer);
        else Register(npc);
    }
    private void Receive(WorldFrame frame)
    {
        if (frame.Kind==WorldKind.Manifest)
        {
            if (frame.Key!=_catalogue!.Manifest) throw new InvalidDataException("Authored enemy/door catalogue mismatch. Compare "+WorldCatalogue.ListingPath(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)+" from both PCs.");
            if (!_snapshotApplied) Send(new(WorldKind.ManifestAck,_epoch,1,Key:_catalogue.Manifest));
            return;
        }
        if (frame.Kind==WorldKind.ManifestAck)
        {
            if(_life.RestoringWorld || !_life.State.HostReady || CoopSaveService.Current?.MaySendSnapshot==false) return;
            if (frame.Key!=_catalogue!.Manifest) throw new InvalidDataException("P2 world catalogue mismatch.");
            if (!_peerMatched)
            { _peerMatched=true; Send(new(WorldKind.SnapshotBegin,_epoch,1)); SendState(true); Send(new(WorldKind.SnapshotEnd,_epoch,1)); }
            return;
        }
        if (frame.Kind==WorldKind.SnapshotBegin) { _snapshotEnemies.Clear(); _snapshotApplied=false; _life.WorldReady=false; return; }
        if (frame.Kind==WorldKind.SnapshotEnd)
        {
            foreach (var npc in Object.FindObjectsOfType<Npc>())
                if (!_enemyIds.TryGetValue(npc.Pointer,out var id) || !_snapshotEnemies.Contains(id)) npc.gameObject.SetActive(false);
            _snapshotApplied=true; _life.WorldReady=true; Send(new(WorldKind.SnapshotAck,_epoch,1));
            Plugin.Logger.LogInfo($"Authoritative enemy/door snapshot applied: {_snapshotEnemies.Count} enemies."); return;
        }
        if (frame.Kind==WorldKind.SnapshotAck) return;
        if (Client)
        {
            _applying=true;
            try
            {
                if (frame.Kind==WorldKind.Enemy) ApplyEnemy(frame);
                else if (frame.Kind==WorldKind.Door) ApplyDoor(frame);
                else if (frame.Kind==WorldKind.IncomingHit) ApplyIncomingHit(frame);
                else if (frame.Kind==WorldKind.InteractionResult) CompleteDoorRequest(frame);
                else if (frame.Kind==WorldKind.Projectile) ApplyProjectile(frame);
                else if (frame.Kind==WorldKind.Impact) ApplyImpact(frame);
                else if (frame.Kind==WorldKind.WeaponEffect) PlayWeaponEffect(frame);
                else if (frame.Kind==WorldKind.Loot) ApplyLoot(frame);
                else if (frame.Kind==WorldKind.Encounter) ApplyEncounter(frame);
                else if (frame.Kind==WorldKind.ActorState) ApplyActorState(frame);
                else if (frame.Kind==WorldKind.ActorEffect) ApplyActorEffect(frame);
                else if (frame.Kind==WorldKind.UrineState) ApplyUrineState(frame);
                else if (frame.Kind==WorldKind.MissionState) ApplyMissionState(frame);
                else if (frame.Kind==WorldKind.Scenery) ApplyScenery(frame);
                else if (frame.Kind==WorldKind.Mechanism) ApplyMechanism(frame);
                else if (frame.Kind==WorldKind.Sequence) ApplySequence(frame);
                else if (frame.Kind==WorldKind.SceneryEvent) PlaySceneryEvent(frame);
                else if (frame.Kind==WorldKind.PlayerEffect) ApplyPlayerEffect(frame);
            }
            finally { _applying=false; }
        }
        else if (frame.Kind==WorldKind.Melee) ApplyEnemyHit(frame);
        else if (frame.Kind==WorldKind.Interact) ApplyDoorRequest(frame);
        else if (frame.Kind==WorldKind.Fire) ApplyFire(frame);
        else if (frame.Kind==WorldKind.ClientEffect) ForwardWeaponEffect(frame);
        else if (frame.Kind==WorldKind.Trigger) ApplyEncounterTrigger(frame);
        else if (frame.Kind==WorldKind.Special) ApplySpecial(frame);
        else if (frame.Kind==WorldKind.Reflect) ApplyReflection(frame);
        else if (frame.Kind==WorldKind.Defense) ApplyDefense(frame);
    }
    private void SendState(bool complete)
    {
        // A join snapshot is captured atomically while gameplay is held by the
        // readiness barrier. Steady updates use the same capture with frame budgeting.
        CancelStateCapture();
        SendProjectileState(complete);
        foreach(var ignored in CaptureState(complete)) { }
    }
    private void CancelStateCapture()
    { _stateCapture?.Dispose();_stateCapture=null; }
    private void TickStateCapture(long deadline)
    {
        // The full save sweep owns the shared delta caches until it yields its
        // final record. Do not run a second incremental producer into it.
        if(_persistentFrames!=null) return;
        if(_stateCapture==null)
        {
            if(Time.unscaledTime<_stateAt) return;
            _stateAt=Time.unscaledTime+0.1f;
            _stateCapture=CaptureState(false).GetEnumerator();_stateWorkTicks=0;_stateAllocated=0;
            if(SyncPerformance.Enabled) SyncPerformance.BeginCycle();
        }
        var started=Stopwatch.GetTimestamp();var allocated=SyncPerformance.Enabled?GC.GetAllocatedBytesForCurrentThread():0;
        var finished=false;
        // Keep Unity reads on its main thread, but yield between actors, doors
        // and movers. Always advance one item so costly native effects cannot
        // starve the sweep; do not enqueue missed cycles on a slow machine.
        do {if(!_stateCapture.MoveNext()){finished=true;CancelStateCapture();break;}}
        while(Stopwatch.GetTimestamp()<deadline);
        var elapsed=Stopwatch.GetTimestamp()-started;
        _stateWorkTicks+=elapsed;
        if(SyncPerformance.Enabled)
        {
            _stateAllocated+=GC.GetAllocatedBytesForCurrentThread()-allocated;
            SyncPerformance.Slice(elapsed*1000d/Stopwatch.Frequency);
            if(finished) SyncPerformance.Sync(_stateWorkTicks*1000d/Stopwatch.Frequency,_stateAllocated);
        }
    }
    private IEnumerable<int> CaptureState(bool complete)
    {
        if(complete && !Client) SetMissionWait("",_pendingMissionEnd!=null?"Waiting for both players to be alive and loaded.":"",true);
        foreach(var key in _catalogue!.MissionBreakKeys) {SendMissionBreak(key,complete);yield return 0;}
        foreach(var key in _catalogue.MissionConditionKeys) {SendMissionArrays(key,complete);yield return 0;}
        if(complete && _missionCompletion!=null) Send(_missionCompletion);
        foreach(var key in _catalogue!.UrineKeys) {SendUrineState(key,complete);yield return 0;}
        foreach(var key in _catalogue.UrineConsequences) {SendUrineConsequence(key,complete);yield return 0;}
        foreach(var step in SendUrineWagons(complete)) yield return step;
        foreach(var pair in _catalogue.UrineSurfaces) {SendUrineSurface(pair.Key,pair.Value,complete);SendMissionLights(pair.Key,pair.Value,complete);yield return 0;}
        foreach(var key in _catalogue!.EncounterKeys) {SendEncounter(key,complete);yield return 0;}
        foreach(var step in CaptureSequences(complete)) yield return step;
        foreach(var step in CaptureMechanisms(complete)) yield return step;
        if(complete) foreach(var loot in _loot.Values) Send(loot);
        IEnumerable<Npc> actors=complete?Object.FindObjectsOfType<Npc>():_enemies.Values.ToArray();
        foreach (var npc in actors)
        {
            if (npc==null || !npc.gameObject.scene.IsValid() || npc.NpcId==null || !npc.gameObject.activeInHierarchy)
            {yield return 0;continue;}
            var id=Register(npc);
            var frame=new WorldFrame(WorldKind.Enemy,_epoch,1,id,Key:_catalogue!.Key(npc.SpawnPoint)??"",Asset:WorldCatalogue.NpcAssetKey(npc.NpcId),
                Flags:(npc.IsAlive?1u:0u)|(npc.gameObject.activeInHierarchy?2u:0u),Position:Net(npc.Transform.position),Rotation:Net(npc.Transform.eulerAngles),
                Value:npc.HitPoints.Current,Value2:npc.HitPoints.Max);
            if(complete || !_lastEnemy.TryGetValue(id,out var prior) || !SameEnemy(prior,frame)) Send(frame);
            _lastEnemy[id]=frame;
            SendEnemyPresentation(id,npc,complete);
            SendActorState(id,npc,complete);
            yield return 0;
        }
        SendScriptedActors(complete);
        SendBossView(complete);
        yield return 0;
        foreach (var pair in _enemies.ToArray())
        {
            if (pair.Value==null || !pair.Value.gameObject.activeInHierarchy)
            {
                if (_lastEnemy.TryGetValue(pair.Key,out var prior) && (complete || prior.Flags!=0))
                {var dead=prior with {Flags=0,Value=pair.Value!=null?pair.Value.HitPoints.Current:0};Send(dead);_lastEnemy[pair.Key]=dead;}
            }
            yield return 0;
        }
        foreach (var key in _catalogue!.DoorKeys)
        {
            var component=_catalogue.Components[key]; if(component==null) continue; var data=WorldCatalogue.Save(component);
            if (complete || !_lastDoor.TryGetValue(key,out var prior) || !data.SequenceEqual(prior))
            { _lastDoor[key]=data; Send(new(WorldKind.Door,_epoch,1,Id(key),Key:key,Data:data,Flags:2)); }
            yield return 0;
        }
        foreach (var pair in _catalogue.DoorMotion.Concat(_catalogue.EncounterMotion).GroupBy(p=>p.Key).Select(g=>g.First()))
        {
            var t=pair.Value;
            if(t==null) continue;
            var frame=new WorldFrame(WorldKind.Door,_epoch,1,Id(pair.Key),Key:pair.Key,Position:Net(t.localPosition),Rotation:Net(t.localEulerAngles),Velocity:Net(t.localScale),Flags:t.gameObject.activeSelf?1u:0u);
            if(complete || !_lastMotion.TryGetValue(pair.Key,out var prior) || prior.Position!=frame.Position || prior.Rotation!=frame.Rotation || prior.Velocity!=frame.Velocity || prior.Flags!=frame.Flags)
            {Send(frame);_lastMotion[pair.Key]=frame;}
            yield return 0;
        }
        // Parent machinery and display transforms arrive first. A physical
        // prop's world-space body is the final authority over its own pose.
        foreach(var step in CaptureScenery(complete)) yield return step;
    }
    private static bool SameEnemy(WorldFrame a,WorldFrame b)=>a.Flags==b.Flags && a.Position==b.Position && a.Rotation==b.Rotation && a.Value==b.Value && a.Value2==b.Value2 && (a.Values??Array.Empty<float>()).SequenceEqual(b.Values??Array.Empty<float>());
    private void ApplyEnemy(WorldFrame frame)
    {
        if((frame.Flags&4)!=0) {ApplyEnemyPresentation(frame);return;}
        if (_revisions.TryGetValue(frame.Entity,out var revision) && frame.Sequence<=revision) return;
        _revisions[frame.Entity]=frame.Sequence; _snapshotEnemies.Add(frame.Entity);
        _lastEnemy[frame.Entity]=frame;
        if (!_enemies.TryGetValue(frame.Entity,out var npc) || npc==null)
        {
            npc=Object.FindObjectsOfType<Npc>(true).FirstOrDefault(n=>n.NpcId!=null && WorldCatalogue.NpcAssetKey(n.NpcId)==frame.Asset &&
                _catalogue!.Key(n.SpawnPoint)==frame.Key && !_enemyIds.ContainsKey(n.Pointer));
            if (npc==null)
            {
                if (!_catalogue!.NpcAssets.TryGetValue(frame.Asset,out var asset))
                {
                    asset=Resources.FindObjectsOfTypeAll<NpcId>().FirstOrDefault(a=>WorldCatalogue.NpcAssetKey(a)==frame.Asset);
                    if(asset==null) throw new InvalidDataException("Unavailable host enemy asset: "+frame.Asset);
                    _catalogue.NpcAssets[frame.Asset]=asset;
                }
                var parent=new GameObject("PBDMP_ReplicaStaging"); parent.SetActive(false);
                var root=Object.Instantiate(asset._npcPrefab,parent.transform); npc=root.GetComponent<Npc>();
                if (npc==null) throw new InvalidDataException("Enemy prefab lacks native NPC.");
                _enemies[frame.Entity]=npc; _enemyIds[npc.Pointer]=frame.Entity;
                root.transform.SetParent(null,true); _ownedEnemies.Add(root); Object.Destroy(parent);
            }
            _enemies[frame.Entity]=npc; _enemyIds[npc.Pointer]=frame.Entity;
        }
        npc.HitPoints.SetMax(frame.Value2);npc.HitPoints.SetCurrentSilent(frame.Value);
        var bossView=BossView();
        if(bossView?._currentBoss?.Pointer==npc.Pointer) bossView.OnHealthChanged(npc.HitPoints);
        var active=(frame.Flags&2)!=0; npc.gameObject.SetActive(active);
        if (!active) return;
        SuppressEnemyAnimationEvents(npc);
        foreach (var behaviour in npc.GetComponentsInChildren<MonoBehaviour>(true))
        {
            var name=behaviour.GetIl2CppType().Name;
            if (!name.Contains("Behaviour") && !name.Contains("AnimationComponent") && name is not "NpcAwarenessComponent" and not "NpcPushComponent" and not "NpcLinkTraversalComponent" and not "Boid") continue;
            if (!_behaviours.ContainsKey(behaviour)) _behaviours[behaviour]=behaviour.enabled;
            behaviour.enabled=false;
        }
        foreach (var agent in npc.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true))
        { if (!_agents.ContainsKey(agent)) _agents[agent]=agent.enabled; agent.enabled=false; }
        SetActorMotion(frame.Entity,npc,frame.Position,frame.Rotation);
        npc.HitPoints.SetMax(frame.Value2); npc.HitPoints.SetCurrentSilent(frame.Value);
    }
    private void ApplyDoor(WorldFrame frame)
    {
        try { ApplyDoorState(frame); }
        catch(Exception error)
        {
            Plugin.Logger.LogError($"Door synchronization failed: {DoorDiagnostic(frame)}\n{error}");
            throw;
        }
    }
    private void ApplyDoorState(WorldFrame frame)
    {
        if ((frame.Flags&2)!=0)
        {
            if (!_catalogue!.DoorKeys.Contains(frame.Key)) throw new InvalidDataException("Unknown authoritative door.");
            var component=_catalogue.Components[frame.Key];
            if(component==null) return;
            // These machines also appear as doors by hierarchy name. Their
            // typed native restore/encounter + motion adapters own continuation.
            // Loading them again here creates a second tween and callback.
            if(component.GetIl2CppType().Name is "Mover" or "MoverController" or "Rotater" or "Toggler" && _catalogue.EncounterKeys.Contains(frame.Key)) return;
            ReportMissingDoorReferences(component,frame);
            if(!ApplySwingDoor(component,frame.Data??Array.Empty<string>())) WorldCatalogue.Load(component,frame.Data??Array.Empty<string>());
        }
        else
        {
            if (!_catalogue!.DoorMotion.TryGetValue(frame.Key,out var transform) && !_catalogue.EncounterMotion.TryGetValue(frame.Key,out transform)) throw new InvalidDataException("Unknown shared motion.");
            // A destroyed decorative handle is not a reason to end the campaign session.
            if(transform==null) return;
            if(Client) StopReplicaUrineAnimation(frame.Key,transform);
            var body=transform.GetComponent<Rigidbody>(); if (Client && body!=null) {if(!_doorBodies.ContainsKey(body)) _doorBodies[body]=body.isKinematic;body.isKinematic=true;OwnBodyPresentation(body);}
            var physical=body?.GetComponent<Hyperstrange.PBD.Contraptions.PhysicalObject>();
            // An interpolated Transform can lag its physics body. Keep generic
            // activation/scale, but never replace this body's authoritative pose.
            if(physical==null || !SharedPhysics(physical))
            {transform.localPosition=Unity(frame.Position);transform.localRotation=Quaternion.Euler(Unity(frame.Rotation));}
            transform.localScale=Unity(frame.Velocity);
            transform.gameObject.SetActive((frame.Flags&1)!=0);
        }
        _doorPortalsDirty=true;
    }
    private void SendIncomingHit(HitData hit)
    {
        var remote=_session.Snapshot.RemotePose; if (remote==null || !remote.Alive || _life.Frozen) return;
        if (hit.dealer?.IsPlayer==true && hit.dealer.Pointer!=_remote?.Pointer) return;
        if (Environment.GetEnvironmentVariable("PBDMP_SHARED_COMBAT_TEST")=="1") Plugin.Logger.LogInfo($"COMBAT HIT SEND: damage={hit.damage}, weak={hit.weakSpotMultiplier}, life={remote.Life}");
        if(hit.projectile!=null && !_projectiles.ContainsKey(hit.projectile.Pointer)) RegisterNativeProjectile(hit.projectile);
        var projectileId=hit.projectile!=null && _projectiles.TryGetValue(hit.projectile.Pointer,out var projectile)?projectile.Id:0;
        var dealerId=_enemyIds.GetValueOrDefault(hit.dealer?.Pointer??IntPtr.Zero);
        if(projectileId!=0) _reflectionHits[projectileId]=(remote.Life,Time.unscaledTime+3f,dealerId);
        Send(new(WorldKind.IncomingHit,_epoch,1,dealerId,projectileId,Actor:2,Life:remote.Life,Asset:hit.originEntity?.ID??"",Flags:HitFlags(hit),
            Position:Net(hit.position),Rotation:Net(hit.direction),Velocity:Net(hit.normal),Value:hit.damage,Value2:hit.weakSpotMultiplier,
            Values:new[]{hit.ragdollForce,hit.chanceToGib,hit.pushVelocity.x,hit.pushVelocity.y,hit.pushVelocity.z}));
    }
    private void ApplyIncomingHit(WorldFrame frame)
    {
        if (frame.Actor!=2 || frame.Life!=_life.Life || !Player.Instance.IsAlive) return;
        IncomingHitApplications++;
        if (Environment.GetEnvironmentVariable("PBDMP_SHARED_COMBAT_TEST")=="1") Plugin.Logger.LogInfo($"COMBAT HIT APPLY: damage={frame.Value}, weak={frame.Value2}, life={frame.Life}, before={Player.Instance.HitPoints.Current}");
        _incomingProjectile=frame.Revision;
        try {Player.Instance.Hit(DecodeHit(frame));} finally {_incomingProjectile=0;}
        if (Environment.GetEnvironmentVariable("PBDMP_SHARED_COMBAT_TEST")=="1") Plugin.Logger.LogInfo($"COMBAT HIT AFTER: {Player.Instance.HitPoints.Current}");
    }
    internal void ClientHit(Npc npc,HitData hit)
    {
        if (!_enemyIds.TryGetValue(npc.Pointer,out var id) || _life.Frozen) return;
        Send(new(WorldKind.Melee,_epoch,1,id,Actor:2,Life:_life.Life,Asset:hit.originEntity?.ID??"",Flags:HitFlags(hit),
            Position:Net(hit.position),Rotation:Net(hit.direction),Velocity:Net(hit.normal),Value:hit.damage,Value2:hit.weakSpotMultiplier,
            Values:new[]{hit.ragdollForce,hit.chanceToGib,hit.pushVelocity.x,hit.pushVelocity.y,hit.pushVelocity.z}));
    }
    private void ApplyEnemyHit(WorldFrame frame)
    {
        var pose=_session.Snapshot.RemotePose;
        if (_life.Frozen || pose?.Alive!=true || frame.Actor!=2 || frame.Life!=pose.Life || _remote==null ||
            (frame.Entity!=0 && (!_enemies.TryGetValue(frame.Entity,out var actor) || actor==null || !actor.IsAlive))) return;
        if(frame.Key.Length!=0) {ApplyActorPartHit(frame);return;}
        if(!_enemies.TryGetValue(frame.Entity,out var npc) || npc==null) return;
        var hit=DecodeHit(frame); hit.dealer=_remote.Actor;
        _attackOrigins[frame.Asset]=_attackOrigins.GetValueOrDefault(frame.Asset)+1;
        var colliders=npc.GetComponentsInChildren<Collider>();
        hit.collider=colliders.OrderBy(c=>Vector3.Distance(c.ClosestPoint(hit.position),hit.position)).FirstOrDefault();
        npc.Hit(hit);
    }
    private HitData DecodeHit(WorldFrame frame)
    {
        var h=new HitData {damage=frame.Value,weakSpotMultiplier=frame.Value2,position=Unity(frame.Position),direction=Unity(frame.Rotation),normal=Unity(frame.Velocity),
            isPiss=(frame.Flags&1)!=0,isCrit=(frame.Flags&2)!=0,isQuickMelee=(frame.Flags&4)!=0,isPowerful=(frame.Flags&8)!=0,isExplosion=(frame.Flags&16)!=0,
            isFlame=(frame.Flags&32)!=0,forceRagdoll=(frame.Flags&64)!=0,forceStagger=(frame.Flags&128)!=0,forceLowHp=(frame.Flags&256)!=0,
            cancelDecals=(frame.Flags&512)!=0,spawnPickupsOnKill=(frame.Flags&1024)!=0};
        if (_catalogue!.Assets.TryGetValue(frame.Asset,out var asset)) h.originEntity=asset;
        if (_enemies.TryGetValue(frame.Entity,out var dealer) && dealer!=null) h.dealer=dealer.Cast<IAiActor>();
        if (frame.Values is {Length:5}) {h.ragdollForce=frame.Values[0];h.chanceToGib=frame.Values[1];h.pushVelocity=new(frame.Values[2],frame.Values[3],frame.Values[4]);}
        return h;
    }
    private static uint HitFlags(HitData h)=>(h.isPiss?1u:0)|(h.isCrit?2u:0)|(h.isQuickMelee?4u:0)|(h.isPowerful?8u:0)|(h.isExplosion?16u:0)|
        (h.isFlame?32u:0)|(h.forceRagdoll?64u:0)|(h.forceStagger?128u:0)|(h.forceLowHp?256u:0)|(h.cancelDecals?512u:0)|(h.spawnPickupsOnKill?1024u:0);
    private void ReleaseActor()
    {
        ClearRemoteStreams();
        ReleaseUrineRider();
        if(_remote==null) return;
        // Native projectiles retain their owner and homing target. Release those
        // references before destroying the proxy, including when P2 disconnects.
        foreach(var life in _projectiles.Values.ToArray())
        {
            if(life.Projectile==null) continue;
            if(life.Actor==2) {life.Projectile.gameObject.SetActive(false);EndProjectile(life);}
            else if(life.Projectile._movementModule?.TryCast<Hyperstrange.PBD.ProjectileComponents.ProjectileMovementModuleHoming>() is {} homing &&
                homing._target!=null && (homing._target.Pointer==_remote.ReferenceTransform.Pointer || homing._target.IsChildOf(_remote.transform))) homing.SetTarget(null);
        }
        ClearRemoteWeapons();_remote.Release(); _remote=null;_remotePoseAt=0;
    }
    public void Dispose()
    {
        CancelStateCapture();
        RefundPendingDoorRequests();
        ClearSequences();ClearScenery(preserveHost:!Client);ClearMechanisms();ClearMissions();
        ReleaseActor(); ClearPosters(); ClearProjectiles(); ClearLoot(); ClearEncounters(); ClearWeaponAudio(); ClearStreams(); ClearActors(); ClearEnemyPresentation(); ClearUrine();
        foreach (var pair in _agents) if (pair.Key!=null) pair.Key.enabled=pair.Value;
        foreach (var pair in _rootMotion) if (pair.Key!=null) pair.Key.applyRootMotion=pair.Value;
        foreach (var pair in _behaviours) if (pair.Key!=null) pair.Key.enabled=pair.Value;
        foreach (var pair in _doorBodies) if (pair.Key!=null) pair.Key.isKinematic=pair.Value;
        foreach (var root in _ownedEnemies) if (root!=null) Object.Destroy(root);
        if (ReferenceEquals(Current,this)) Current=null;
    }
}
