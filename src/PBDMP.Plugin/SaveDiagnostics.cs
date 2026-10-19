using System.Text.Json;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using Hyperstrange.PBD.PlayerComponents;
using Il2CppInterop.Runtime;
using PBDMP.Network;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PBDMP.Plugin;
public sealed partial class MultiplayerBehaviour
{
    private int _saveStep;
    private float _saveAt;
    private Npc? _savedActor;
    private string _savedActorSpawn="",_savedPickup="",_savedDoor="";
    private Vector3 _savedPosition;
    private float _saveStageAt;
    private int _saveAutoCount;
    private uint _saveAutoEpoch;
    private int _saveCheckpointCount,_saveWritesBefore;
    private bool _saveSawLoading,_saveSawCapture,_saveSawAuto;
    private void RunSaveTest()
    {
        if(Environment.GetEnvironmentVariable("PBDMP_SAVE_REPLAY")=="1") {RunSavedPlaytestReplay();return;}
        if(_saveStep==99) return;
        var caption=_view!.Hud.HoldText;
        if(caption=="Loading...") _saveSawLoading=true;
        if(caption=="Saving...") _saveSawCapture=true;
        if(caption=="Autosaving...") _saveSawAuto=true;
        if(caption.Length>0 && !Marked(TravelRole+"-hold-"+caption.TrimEnd('.')))
        {ScreenCapture.CaptureScreenshot(Marker(TravelRole+"-hold-"+caption.TrimEnd('.')+".png"));TravelMark(TravelRole+"-hold-"+caption.TrimEnd('.'));}
        if(caption=="Loading..." && _saveStep>=4 && !Marked(TravelRole+"-restore-hold"))
        {ScreenCapture.CaptureScreenshot(Marker(TravelRole+"-restore-hold.png"));TravelMark(TravelRole+"-restore-hold");}
        File.WriteAllText(Marker(TravelRole+"-save-state.txt"),$"step={_saveStep}; native={_lifecycle?.NativeReady}; ready={_lifecycle?.State.Released}; personal={_lifecycle?.PersonalReady}; status={_saves?.Status}");
        if(_saveStep==0)
        {
            _port!.text=Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!;
            if(TravelRole=="host")
            {
                Host();SharedWorldPatches.DiagnosticFreeze=true;
                if(Environment.GetEnvironmentVariable("PBDMP_SAVE_RESUME")=="1") {_saves!.RequestLoad("manual-regression");_saveStep=20;}
                else {GameManager.LoadLevel(TestLevel(),true);_saveStep=1;}
            }
            else {_address!.text="127.0.0.1";Join();_saveStep=1;}
            _saveAt=Time.unscaledTime;return;
        }
        if(_lifecycle is {NativeReady:false}) {_lifecycle.AutoContinue();return;}
        if(_lifecycle?.LocalTutorialActive==true)
        {Player.Instance!.GetComponent<PlayerTutorialComponent>().OnUseDown();return;}
        if(Time.unscaledTime-_saveAt>420) throw new TimeoutException("Save diagnostic stalled: "+_saveStep+" / "+_saves?.Status);
        if(TravelRole=="host" && Environment.GetEnvironmentVariable("PBDMP_SAVE_REPLACE")=="1" && _view!.Prompt.Open && _view.Prompt.Message.Contains("different partner",StringComparison.Ordinal))
        {
            // Anyone can join a co-op game: no partner-assignment prompt may appear.
            _view.Prompt.ConfirmButton.onClick.Invoke();TravelMark("save-replacement-prompted");
        }
        if(TravelRole=="host") SaveTestHost();else SaveTestGuest();
    }
    private void SaveTestHost()
    {
        switch(_saveStep)
        {
            case 1 when _world?.Active==true:
                GiveTestGear();
                var hp=Player.Instance.GetComponent<PlayerHitProcessorComponent>();hp.SetCurrentHitPoints(63);hp.SetArmor(27);
                _savedPosition=Player.Instance.Transform.position;
                var spawn=Object.FindObjectsOfType<SpawnPoint>(true).First(s=>s.gameObject.scene.IsValid() && s.NpcId!=null && s.NpcId._npcPrefab?.GetComponent<Npc>()!=null);
                spawn.Spawn();_savedActor=Object.FindObjectsOfType<Npc>().First(n=>n.SpawnPoint?.Pointer==spawn.Pointer && n.IsAlive);
                _savedActor.HitPoints.SetCurrentSilent(17);_savedActorSpawn=_world.KeyForTest(spawn)!;
                File.WriteAllText(Marker("save-actor-spawn"),_savedActorSpawn);
                var door=_world.CatalogueForTest.DoorKeys.Select(k=>_world.ComponentForTest(k)).Select(c=>c?.TryCast<Door>()).First(d=>d!=null && d._transformToAnimate!=null)!;
                door._isLocked=false;door.FirstInteraction=false;door._desiredRotation=70;door._transformToAnimate.localEulerAngles=new Vector3(0,70,0);_savedDoor=_world.KeyForTest(door)!;
                File.WriteAllText(Marker("save-door"),_savedDoor);
                var pickup=NativeSaveBridge.Entries().First(e=>e.Component.TryCast<Pickup>()!=null);
                _savedPickup="a:"+pickup.Guid;var p=pickup.Component.Cast<Pickup>();p.SetEnabled(true);_saves!.Collected(p);
                File.WriteAllText(Marker("save-personal-pickup"),_savedPickup);
                var bagPrefab=Resources.FindObjectsOfTypeAll<BagPickup>().First(b=>!b.gameObject.scene.IsValid());
                var drop=Object.Instantiate(bagPrefab,Player.Instance.FeetPosition+Vector3.right*50+Vector3.up,Quaternion.identity);
                drop.Drop(drop.transform.position,drop.transform.position);drop.SetHealth(17);drop.SetArmor(11);_world.OwnDiagnosticDrop(drop.gameObject);
                drop.SetEnabled(true);_saves.Collected(drop);
                Object.FindObjectsOfType<FuseBox>(true).First(f=>f.gameObject.scene.IsValid()).Execute();
                TravelMark("host-ready");_saveStep=2;break;
            case 2 when _lifecycle!.State.Released && Marked("save-client-initial") && (Environment.GetEnvironmentVariable("PBDMP_FOCUS_INPUT_TEST")!="1" || Marked("focus-switches-done")):
                _saves!.RequestSave("manual-regression","Native regression");_saveStep=3;break;
            case 3 when !_saves!.Busy:
                var saved=_saves.Store.Read("manual-regression").Save;
                TravelCheck(saved.Host.Collected.Contains(_savedPickup) && saved.Guests.Length==1,"disk save captures distinct host and guest records plus personal pickup history");
                TravelCheck(saved.Native.Count>0 && saved.World.Any(f=>f.Kind==WorldKind.Enemy && f.Value==17),"disk save includes native simulation and damaged authored NPC");
                TravelCheck(saved.World.Any(f=>f.Kind==WorldKind.Loot) && saved.Host.Collected.Any(k=>k.StartsWith("d:",StringComparison.Ordinal)),"generated loot has durable identities and personal collection state");
                TravelCheck(saved.Guests[0].Collected.Any(k=>k.StartsWith("a:",StringComparison.Ordinal)),"native P2 pickup completion is captured separately");
                var png=_saves.Store.Thumbnail("manual-regression");
                TravelCheck(png!=null && png.Length>8,"co-op saves keep the game's own screenshot thumbnail");
                var listing=_saves.Store.List().First(l=>l.Slot=="manual-regression");
                TravelCheck(saved.Players.Length==2 && listing.Players!.SequenceEqual(saved.Players) && File.Exists(Path.Combine(_saves.Store.Folder,"manual-regression.info")),"the save lists both players from its small sidecar");
                TravelCheck(CoopSaveStore.Campaigns(CoopSaveService.Root).Any(c=>c.Id==CoopSaveService.DiagnosticCampaign && c.Saves>0),"the save belongs to its co-op campaign folder");
                File.WriteAllText(Marker("save-captured.json"),JsonSerializer.Serialize(saved));
                SetPanel(true);_view!.ShowSaves(true);_saveStageAt=Time.unscaledTime+.5f;_saveStep=30;break;
            case 30 when Time.unscaledTime>=_saveStageAt:
                ScreenCapture.CaptureScreenshot(Marker("co-op-saves-ui.png"));_saveStageAt=Time.unscaledTime+.2f;_saveStep=31;break;
            case 31 when Time.unscaledTime>=_saveStageAt:
                GameManager.PauseGame();SetPanel(true);_view!.ShowSaves(true);
                _savedActor!.HitPoints.SetCurrentSilent(5);Player.Instance.GetComponent<PlayerHitProcessorComponent>().SetCurrentHitPoints(22);
                TravelMark("save-mutated");_saves!.RequestLoad("manual-regression");_saveStep=4;break;
            case 4 when !_saves!.Busy && _lifecycle!.State.Released && Marked("save-client-restored"):
                CheckRestoredSave();
                CheckJoinGameplayInput("P1 after restoring from an open native pause/save menu");
                TravelCheck(_session!.Snapshot.Connected,"save restoration retains the multiplayer session");
                WriteRestoredWorldFixture();TravelMark("save-host-restored");_saveStep=5;break;
            case 5 when Marked("save-client-done"):
                if(Environment.GetEnvironmentVariable("PBDMP_SAVE_RESUME")=="1")
                {ResetSession();SharedWorldPatches.DiagnosticFreeze=false;_saveStep=6;break;}
                Environment.SetEnvironmentVariable("PBDMP_SAVE_AUTOTEST","1");
                _saveCheckpointCount=_saves!.CheckpointRequestsForTest;_saveWritesBefore=_saves.CompletedWritesForTest;
                CreateSaveCheckpointFixture(new Vector3(_session!.Snapshot.RemotePose!.X,_session.Snapshot.RemotePose.Y,_session.Snapshot.RemotePose.Z));
                TravelMark("save-request-client-checkpoint");_saveStageAt=Time.unscaledTime+3;_saveAutoCount=1;_saveStep=39;break;
            case 39 when Time.unscaledTime>=_saveStageAt && Marked("save-client-checkpoint-requested"):
                TravelCheck(_saves!.CheckpointRequestsForTest>_saveCheckpointCount,"P2 native checkpoint request is validated without immediate disk capture");
                for(var i=0;i<8;i++) LevelController.AutoSave();
                TravelCheck(!_saves.Busy && _saves.CompletedWritesForTest==_saveWritesBefore,"repeated native checkpoint/autosave requests cannot bypass the gameplay timer");
                _saves.AdvanceAutosaveForTest(_saves.AutosaveRemainingForTest-1);LevelController.AutoSave();
                TravelCheck(!_saves.Busy,"native autosave remains idle before the full interval");
                _saves.AdvanceAutosaveForTest(1);LevelController.AutoSave();
                TravelCheck(_saves.Busy,"native autosave queues once when the interval expires");_saveStep=40;break;
            case 40 when !_saves!.Busy:
                if(_saveAutoCount<4) {_saves.AdvanceAutosaveForTest(_saves.AutosaveRemainingForTest);LevelController.AutoSave();_saveAutoCount++;break;}
                var autos=_saves.Store.List().Where(s=>s.Slot.StartsWith("auto-",StringComparison.Ordinal) && s.Error==null).ToArray();
                TravelCheck(autos.Length==3,"native autosaves rotate through exactly three slots");
                TravelCheck(autos.Any(s=>File.Exists(Path.Combine(_saves.Store.Folder,s.Slot+".pbdmp.bak"))),"rotating autosave preserves a recovery backup");
                Environment.SetEnvironmentVariable("PBDMP_SAVE_AUTOTEST",null);
                var damaged=_world!.ActorsForTest.Values.First(n=>n!=null && n.IsAlive && n.HitPoints.Current==17);
                damaged.HitPoints.SetCurrentSilent(4);Player.Instance.GetComponent<PlayerHitProcessorComponent>().SetCurrentHitPoints(12);
                _saveAutoEpoch=_lifecycle!.Epoch;_saves.RestoreCheckpoint();TravelMark("save-checkpoint-requested");_saveStep=41;break;
            case 41 when !_saves!.Busy && _lifecycle!.State.Released && _lifecycle.Epoch>_saveAutoEpoch:
                CheckRestoredSave();TravelCheck(_session!.Snapshot.Connected,"checkpoint rollback retains the partner connection");
                TravelMark("save-checkpoint-restored");_saveStep=42;break;
            case 42 when Marked("save-client-checkpoint"):
                TravelCheck(_saveSawLoading && _saveSawCapture && _saveSawAuto,"P1 displays loading, manual capture and timed autosave overlays");
                TravelCheck(_view!.Hud.HoldText.Length==0,"P1 clears the hold overlay when gameplay is released");
                LevelController._instance.EndLevel(LevelController.EndLevelType.FINISH);_saveStep=43;break;
            case 43 when Object.FindObjectOfType<Hyperstrange.PBD.UI.Menus.EndLevelMenu>() is {} results && results.gameObject.activeInHierarchy && Marked("save-client-completed"):
                File.WriteAllText(Marker("save-result-stats"),ResultStatsText());
                _saves!.RequestSave("completed-regression","Completed level regression");_saveStep=44;break;
            case 44 when !_saves!.Busy:
                _saveAutoEpoch=_lifecycle!.Epoch;_saves.RequestLoad("completed-regression");TravelMark("save-results-reloading");_saveStep=45;break;
            case 45 when !_saves!.Busy && _lifecycle!.State.Released && _lifecycle.Epoch>_saveAutoEpoch && Object.FindObjectOfType<Hyperstrange.PBD.UI.Menus.EndLevelMenu>() is {} loadedResults && loadedResults.gameObject.activeInHierarchy:
                TravelCheck(ResultStatsText()==File.ReadAllText(Marker("save-result-stats")),"P1 restores completed-map results and exact statistics");
                TravelMark("save-results-restored");_saveStep=46;break;
            case 46 when Marked("save-client-results-restored"):
                ResetSession();SharedWorldPatches.DiagnosticFreeze=false;_saveStep=6;break;
            case 6 when Marked("client-pass"):
                TravelMark("host-pass");_saveStep=99;break;
            case 20 when _lifecycle!.NativeReady && !_saves!.Busy && _world!.Active:
                CheckRestoredSave();TravelCheck(!_session!.Snapshot.Connected,"P1 resumes a persistent co-op save alone before the partner joins");
                TravelMark("host-ready");TravelMark("save-resume-host-ready");_saveStep=21;break;
            case 21 when _lifecycle!.State.Released && Marked("save-client-initial"):
                if(Environment.GetEnvironmentVariable("PBDMP_SAVE_REPLACE")=="1") TravelCheck(!Marked("save-replacement-prompted"),"a different player joins a co-op save without an assignment prompt");
                CheckRestoredSave();TravelCheck(_saves!.GuestForTest!=null,"fresh process retains the saved partner identity and personal record");
                WriteRestoredWorldFixture();TravelMark("save-host-restored");_saveStep=5;break;
        }
    }
    private static PlayerTriggerCheckpointComponent CreateSaveCheckpointFixture(Vector3 feet)
    {
        // A native component fixture isolates checkpoint transport from unrelated mission callbacks.
        var parent=Object.FindObjectsOfType<PlayerTriggerCheckpointComponent>(true)
            .Where(p=>p.name!="PBDMP_CheckpointRegression" && p.gameObject.scene.IsValid())
            .OrderBy(p=>WorldCatalogue.PathOf(p.transform),StringComparer.Ordinal).First().transform;
        var root=new GameObject("PBDMP_CheckpointRegression");
        // Appended scene-root indices vary with local presentation pools. Anchor
        // both peer fixtures to the same authored checkpoint and child index.
        root.transform.SetParent(parent,false);root.transform.SetSiblingIndex(0);root.transform.position=feet;
        var bounds=root.AddComponent<BoxCollider>();bounds.isTrigger=true;bounds.size=new Vector3(4,5,4);bounds.center=Vector3.up;
        var trigger=root.AddComponent<PlayerTrigger>();trigger._enabled=true;trigger.enabled=false;
        return root.AddComponent<PlayerTriggerCheckpointComponent>();
    }
    private void CheckRestoredSave()
    {
        var data=_saves!.Store.Read("manual-regression").Save;
        var actorFrame=data.World.First(f=>f.Kind==WorldKind.Enemy && f.Value==17);
        var actor=_world!.ActorsForTest.Values.FirstOrDefault(n=>n!=null && n.IsAlive && _world.KeyForTest(n.SpawnPoint)==actorFrame.Key && WorldCatalogue.NpcAssetKey(n.NpcId)==actorFrame.Asset);
        TravelCheck(actor!=null && actor.HitPoints.Current==17,"native host restores saved NPC health after scene reload");
        TravelCheck(Player.Instance.HitPoints.Current==63 && Player.Instance.Armor==27,"P1 restores saved health and armor");
        var point=NativeSaveBridge.Player();
        TravelCheck(point["_saveData"].Contains("63"),"native player serializer restored host personal values");
        var key=data.Host.Collected.First(k=>k.StartsWith("a:",StringComparison.Ordinal));
        var pickup=NativeSaveBridge.Entries().First(e=>"a:"+e.Guid==key).Component.Cast<Pickup>();
        TravelCheck(pickup._pickedUp,"host collected authored pickup remains collected after restore");
        var doorFrame=data.World.First(f=>f.Kind==WorldKind.Door && f.Data?.Contains("70")==true);
        var door=_world.ComponentForTest(doorFrame.Key)!.Cast<Door>();
        TravelCheck(!door._isLocked && !door.FirstInteraction && Mathf.Abs(door._desiredRotation-70)<.01f,"host door logical state restored without native missing-handle load");
    }
    private void WriteRestoredWorldFixture()
    {
        var wall=Object.FindObjectsOfType<BreakableObject>(true).First(b=>MissionPath(b.transform)=="OBJECTIVES/BreakableWall");
        TravelCheck(wall._destroyed,"saved first-mission wall remains destroyed after host restore");
        File.WriteAllText(Marker("saved-wall-after-restore.json"),JsonSerializer.Serialize(MissionBreakState(wall)));
    }
    private void SaveTestGuest()
    {
        switch(_saveStep)
        {
            case 1 when _world?.Active==true && _lifecycle!.State.Released && _lifecycle.PersonalReady && !_lifecycle.Frozen:
                TravelCheck(Player.Instance.HitPoints.Current==Player.Instance.HitPoints.Max && Player.Instance.Armor==0,"joining P2 resets health and armor");
                var gear=_lifecycle.PlayerState.CaptureJoin();TravelCheck(gear.Items.Any(i=>EntityManager.Get<EntityId>(i.Id)?.TryCast<WeaponId>()?.name=="WEAPON_Shotgun"),"joining P2 catches up to P1 weapons");
                if(Environment.GetEnvironmentVariable("PBDMP_SAVE_RESUME")=="1") {TravelMark("save-client-initial");_saveStep=2;break;}
                var entries=NativeSaveBridge.Entries();var hostKey=File.ReadAllText(Marker("save-personal-pickup"));
                Player.Instance.GetComponent<PlayerHitProcessorComponent>().SetCurrentHitPoints(50);
                var entry=entries.First(e=>"a:"+e.Guid!=hostKey && e.Component.TryCast<HealthPickup>() is {} h && !h._pickedUp && h.gameObject.activeInHierarchy && h.CanBeInteractedWith(Player.Instance));
                var healthPickup=entry.Component.Cast<HealthPickup>();
                File.WriteAllText(Marker("save-pickup-eligibility.txt"),$"item={healthPickup.name}, eligible={healthPickup.CanBeInteractedWith(Player.Instance)}, max={healthPickup._maxAmount}, health={Player.Instance.HitPoints.Current}, frozen={_lifecycle.Frozen}");
                healthPickup.Interact(Player.Instance);_savedPickup="a:"+entry.Guid;
                _saveStageAt=Time.time+1;_saveStep=10;break;
            case 10 when Time.time>=_saveStageAt && !_lifecycle!.Frozen:
                var capturedPickup=_savedPickup;
                TravelCheck(NativeSaveBridge.Entries().First(e=>"a:"+e.Guid==capturedPickup).Component.Cast<Pickup>()._pickedUp,"P2 collects an authored pickup through native interaction");
                if(Environment.GetEnvironmentVariable("PBDMP_FOCUS_INPUT_TEST")=="1") TravelMark("client-focus-ready");
                TravelMark("save-client-initial");_saveStep=2;break;
            case 2 when Environment.GetEnvironmentVariable("PBDMP_SAVE_RESUME")=="1":
                TravelMark("save-client-restored");_saveStep=3;break;
            case 2 when Marked("save-mutated") && !_saves!.Busy && _lifecycle!.State.Released && _lifecycle.PersonalReady && _lifecycle.Epoch>3:
                TravelCheck(Player.Instance.HitPoints.Current==Player.Instance.HitPoints.Max && Player.Instance.Armor==0,"restoring P2 resets health and armor");
                TravelMark("save-client-restored");_saveStep=3;break;
            case 3 when Marked("save-host-restored"):
                var saved=new CoopSaveStore(CoopSaveStore.CampaignFolder(Path.Combine(Path.GetDirectoryName(CoopSaveService.Root)!,"save-regression-host"),CoopSaveService.DiagnosticCampaign)).Read("manual-regression").Save;
                var own=saved.Guests[0].Collected.First(k=>k.StartsWith("a:",StringComparison.Ordinal));
                var ownCollected=NativeSaveBridge.Entries().First(e=>"a:"+e.Guid==own).Component.Cast<Pickup>()._pickedUp;
                // A different player joins fresh: the saved partner's personal pickup history is not theirs.
                if(Environment.GetEnvironmentVariable("PBDMP_SAVE_REPLACE")=="1") TravelCheck(!ownCollected,"a different player starts with fresh personal pickups, not the saved partner's history");
                else TravelCheck(ownCollected,"P2 retains their own collected pickup after restore/rejoin");
                var hostPickup=saved.Host.Collected.First(k=>k.StartsWith("a:",StringComparison.Ordinal));
                TravelCheck(!NativeSaveBridge.Entries().First(e=>"a:"+e.Guid==hostPickup).Component.Cast<Pickup>()._pickedUp,"P1 pickup collection does not remove P2's copy after restore");
                TravelCheck(Object.FindObjectsOfType<BagPickup>(true).Any(b=>b.name.Contains("Clone") && b._healthAmount==17 && !b._pickedUp),"P2 receives an available personal copy of P1's collected enemy drop");
                var wall=Object.FindObjectsOfType<BreakableObject>(true).First(b=>MissionPath(b.transform)=="OBJECTIVES/BreakableWall");
                TravelCheck(JsonSerializer.Serialize(MissionBreakState(wall))==File.ReadAllText(Marker("saved-wall-after-restore.json")),"P2 restores the same structural wall cutout and collision as P1");
                CheckJoinGameplayInput("P2 after co-op save restoration");
                TravelCheck(_session!.Snapshot.Connected && InputManager.Instance.CurrentInputState==InputManager.InputState.GAMEPLAY,"P2 releases loading/save input gates after restoration");
                TravelMark("save-client-done");_saveStep=4;break;
            case 4 when Marked("save-request-client-checkpoint") && !Marked("save-client-checkpoint-requested"):
                CreateSaveCheckpointFixture(Player.Instance.FeetPosition).OnPlayerEnter(Player.Instance);
                TravelMark("save-client-checkpoint-requested");break;
            case 4 when Marked("save-checkpoint-restored") && _lifecycle!.State.Released && _lifecycle.PersonalReady:
                TravelCheck(Player.Instance.HitPoints.Current==Player.Instance.HitPoints.Max && Player.Instance.Armor==0,"checkpoint rollback restores P2's health reset and releases personal input");
                TravelCheck(_saveSawLoading && _saveSawCapture && _saveSawAuto,"P2 displays loading, manual capture and timed autosave overlays");
                TravelCheck(_view!.Hud.HoldText.Length==0,"P2 clears the hold overlay when gameplay is released");
                CheckJoinGameplayInput("P2 after restoring the timed autosave checkpoint");
                TravelMark("save-client-checkpoint");_saveStep=5;break;
            case 5 when Object.FindObjectOfType<Hyperstrange.PBD.UI.Menus.EndLevelMenu>() is {} completed && completed.gameObject.activeInHierarchy:
                TravelMark("save-client-completed");_saveStep=6;break;
            case 6 when Marked("save-results-restored") && !_saves!.Busy && _lifecycle!.State.Released && Object.FindObjectOfType<Hyperstrange.PBD.UI.Menus.EndLevelMenu>() is {} restored && restored.gameObject.activeInHierarchy:
                TravelCheck(ResultStatsText()==File.ReadAllText(Marker("save-result-stats")),"P2 restores completed-map results with host statistics");
                TravelMark("save-client-results-restored");_saveStep=7;break;
            case 4 or 7 when _session==null:
                TravelMark("client-pass");_saveStep=99;break;
        }
    }
}
