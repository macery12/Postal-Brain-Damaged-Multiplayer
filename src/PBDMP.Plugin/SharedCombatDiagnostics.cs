using Hyperstrange.PBD;
using Hyperstrange.Base;
using Hyperstrange.PBD.Contraptions;
using Hyperstrange.PBD.NpcComponents;
using Hyperstrange.PBD.PlayerComponents;
using Il2CppInterop.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PBDMP.Plugin;

public sealed partial class MultiplayerBehaviour
{
    private int _sharedCombatStep;
    private Npc? _sharedCombatNpc;
    private float _sharedCombatHealth;
    private int _sharedCombatHitApplications;
    private Door? _sharedCombatDoor;
    private float _sharedCombatAt;
    private float _sharedCombatProjectileHealth;
    private bool _sharedCombatLootDropped;
    private ProjectileModel[]? _sharedCombatModels;
    private WeaponId[]? _sharedCombatWeapons;
    private int _sharedCombatModelIndex;
    private int _sharedCombatCreates;
    private float _sharedCombatHostHealth;
    private Hyperstrange.PBD.LightningGun.LightningGunBeam? _sharedCombatBeam;
    private float _sharedCombatBeamUntil;
    private BagPickup? _sharedCombatLoot;
    private int _testReflections;
    private int _testLocalReflections;
    private void RunSharedCombatTest()
    {
        if (_sharedCombatStep==99) return;
        if (_travelStarted==0) _travelStarted=Time.unscaledTime;
        File.WriteAllText(Marker(TravelRole+"-state.txt"),$"combat={_sharedCombatStep}, ready={_lifecycle?.NativeReady}, world={_lifecycle?.WorldReady}, status={_lifecycle?.Status}");
        if (Time.unscaledTime-_travelStarted>180) throw new TimeoutException("Shared combat diagnostic timed out.");
        if (_lifecycle is {NativeReady:false}) _lifecycle.AutoContinue();
        if (_sharedCombatStep==0)
        {
            _port!.text=Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!;
            if (TravelRole=="host") {Host(); GameManager.LoadLevel(TestLevel(),true);} else {_address!.text="127.0.0.1";Join();}
            _sharedCombatStep=1; return;
        }
        if (_sharedCombatStep<4 && _lifecycle?.NativeReady!=true) return;
        if (TravelRole=="host") SharedCombatHost(); else SharedCombatClient();
    }
    private void SharedCombatHost()
    {
        switch (_sharedCombatStep)
        {
            case 1:
                if(_world?.Active!=true) return;
                if(!_sharedCombatLootDropped)
                {
                    var bagPrefab=Resources.FindObjectsOfTypeAll<BagPickup>().First(b=>!b.gameObject.scene.IsValid());
                    var lootPosition=Player.Instance.FeetPosition+Vector3.right*50f;
                    var bag=Object.Instantiate(bagPrefab,lootPosition+Vector3.up*2f,Quaternion.identity);
                    bag.Drop(bag.transform.position,lootPosition);bag.SetHealth(17);bag.SetArmor(11);
                    _sharedCombatLoot=bag;
                    _sharedCombatLootDropped=true;return;
                }
                _sharedCombatNpc=Object.FindObjectsOfType<Npc>().Where(n=>n.IsAlive && n.GetComponentInChildren<NpcMeleeDamageComponent>(true)!=null)
                    .OrderBy(n=>Vector3.Distance(n.FeetPosition,Player.Instance.FeetPosition)).First();
                foreach(var other in Object.FindObjectsOfType<Npc>()) if(other.Pointer!=_sharedCombatNpc.Pointer) other.gameObject.SetActive(false);
                SharedWorldPatches.DiagnosticFreeze=true;
                _sharedCombatLoot!.gameObject.SetActive(false);
                foreach(var attackComponent in _sharedCombatNpc.GetComponentsInChildren<NpcMeleeDamageComponent>(true)) attackComponent.SetActive(false);
                _sharedCombatNpc.HitPoints.SetCurrentSilent(57);
                File.WriteAllText(Marker("enemy-asset"),_sharedCombatNpc.NpcId.ID);
                File.WriteAllLines(Marker("registered-weapons.txt"),Resources.FindObjectsOfTypeAll<WeaponId>().Select(w=>$"{w.name} category={w.Category} model={w.PlayerWeaponModel?.GetIl2CppType().Name} prefab={w.PlayerWeaponPrefab?.name}"));
                Player.Instance.GetComponent<PlayerHitProcessorComponent>().SetCurrentHitPoints(100);
                Player.Instance.GetComponent<PlayerHitProcessorComponent>().SetArmor(0);
                TravelMark("host-ready"); _sharedCombatStep=2; break;
            case 2 when Marked("client-hit") && _sharedCombatNpc!.HitPoints.Current==50:
                TravelCheck(true,"P2 native attack changes P1 authoritative enemy health exactly once");
                TravelMark("host-hit-result"); _sharedCombatStep=3; break;
            case 3 when Marked("client-health-synced") && _world!.Remote!=null:
                _sharedCombatHostHealth=Player.Instance.HitPoints.Current;
                _sharedCombatNpc!.SetTarget(_world.Remote.Actor);
                var melee=_sharedCombatNpc!.GetComponentsInChildren<NpcMeleeDamageComponent>(true).First(m=>m.gameObject.activeInHierarchy);
                melee.SetDamage(9); melee.ClearBuffer(); melee.OnTriggerEnter(_world.Remote.GetComponent<CapsuleCollider>());
                TravelMark("host-sent-hit"); _sharedCombatStep=4; break;
            case 4 when Marked("client-personal-hit"):
                TravelCheck(Player.Instance.HitPoints.Current==_sharedCombatHostHealth,"P2 incoming damage does not change P1 personal health");
                _sharedCombatDoor=Object.FindObjectsOfType<Door>().OrderBy(d=>Vector3.Distance(d.transform.position,Player.Instance.FeetPosition)).First();
                _sharedCombatDoor._isLocked=false; _sharedCombatDoor._disableInteraction=false; _sharedCombatDoor.FirstInteraction=false;
                File.WriteAllText(Marker("door-key"),_world!.KeyForTest(_sharedCombatDoor)!);
                _sharedCombatStep=5; break;
            case 5 when Marked("client-door-request") && Mathf.Abs(_sharedCombatDoor!._desiredRotation)>1f:
                TravelCheck(true,"P2 native door interaction commits host opening state");
                TravelMark("host-door-open"); _sharedCombatStep=6; break;
            case 6 when Marked("client-door-shared"):
                _sharedCombatProjectileHealth=_sharedCombatNpc!.HitPoints.Current;
                TravelMark("host-fire-ready"); _sharedCombatStep=7; break;
            case 7 when Marked("client-fired") && _sharedCombatNpc!.HitPoints.Current<_sharedCombatProjectileHealth:
                TravelCheck(true,"P2 native projectile is created and resolves enemy damage on P1");
                TravelMark("host-projectile-result"); _sharedCombatStep=8; break;
            case 8 when Marked("client-projectile-shared"):
                _sharedCombatCreates=_world!.ProjectileCreatesForTest;
                TravelMark("host-models-ready");_sharedCombatStep=9;break;
            case 9 when Marked("client-models-fired") && _world!.ProjectileCreatesForTest>=_sharedCombatCreates+int.Parse(File.ReadAllText(Marker("model-count"))):
                TravelCheck(true,"every loaded registered base-weapon projectile factory reaches P1, including rapid repeated fire");
                TravelMark("host-models-complete");_sharedCombatStep=10;break;
            case 10 when Marked("client-models-presented"):
                RetireCombatFixtureProjectiles();
                var enemyPosition=_sharedCombatNpc!.FeetPosition;
                var enemyRotation=_sharedCombatNpc.Transform.rotation;
                _sharedCombatNpc.gameObject.SetActive(false);
                _sharedCombatNpc=Object.Instantiate(_sharedCombatNpc.NpcId._npcPrefab,enemyPosition,enemyRotation).GetComponent<Npc>();
                _sharedCombatNpc.ResetOnSpawn();
                _sharedCombatNpc.HitPoints.SetCurrentSilent(57);_sharedCombatProjectileHealth=57;
                foreach(var meleeComponent in _sharedCombatNpc.GetComponentsInChildren<NpcMeleeDamageComponent>(true)) meleeComponent.SetActive(false);
                File.WriteAllText(Marker("beam-target-position"),$"{_sharedCombatNpc.FeetPosition.x:R},{_sharedCombatNpc.FeetPosition.y:R},{_sharedCombatNpc.FeetPosition.z:R}");
                TravelMark("host-beam-ready");_sharedCombatStep=11;break;
            case 11 when Marked("client-beam-ended") && _sharedCombatNpc!.HitPoints.Current<_sharedCombatProjectileHealth:
                var lightningId=Resources.FindObjectsOfTypeAll<WeaponId>().First(w=>w.name=="WEAPON_LightningGun").ID;
                TravelCheck(_world!.AttackOriginCount(lightningId)>0,"shared beam damage is attributed to lightning rather than lingering explosives");
                TravelCheck(true,"P2 native continuous beam resolves shared enemy damage on P1");TravelMark("host-beam-result");_sharedCombatStep=12;break;
            case 12 when Marked("client-beam-shared"):
                TravelMark("host-defense-ready");_sharedCombatStep=13;break;
            case 13 when Marked("client-block-ready"):
                SendDefenseTestHit(false);TravelMark("host-block-hit");_sharedCombatStep=14;break;
            case 14 when Marked("client-blocked"):
                TravelMark("host-reflect-ready");_sharedCombatStep=15;break;
            case 15 when Marked("client-reflect-ready"):
                _testReflections=_world!.ReflectionCreatesForTest;SendDefenseTestHit(true);TravelMark("host-reflect-hit");_sharedCombatStep=16;break;
            case 16 when Marked("client-reflected") && _world!.ReflectionCreatesForTest>_testReflections:
                TravelCheck(true,"native P2 reflection callback creates a canonical P1 reflected projectile");
                TravelMark("host-reflect-shared");_sharedCombatStep=17;break;
            case 17 when Marked("client-defense-complete"):
                ResetSession(); SharedWorldPatches.DiagnosticFreeze=false; _sharedCombatStep=18; break;
            case 18 when Marked("client-pass"):
                TravelMark("host-pass"); _sharedCombatStep=99; break;
        }
    }
    private void SharedCombatClient()
    {
        switch (_sharedCombatStep)
        {
            case 1 when _lifecycle!.WorldReady && _lifecycle.State.Released && File.Exists(Marker("enemy-asset")):
                _sharedCombatNpc=_world!.EnemyForTest(File.ReadAllText(Marker("enemy-asset")));
                TravelCheck(_sharedCombatNpc!=null && _sharedCombatNpc.HitPoints.Current==57,"late join receives host enemy health before gameplay");
                TravelCheck(_sharedCombatNpc!.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true).All(a=>!a.enabled),"client enemy navigation authority is disabled");
                TravelCheck(_world!.PersonalLootForTest==1,"late join receives a personal copy of the host's pre-join loot ledger");
                var loot=Object.FindObjectsOfType<BagPickup>().First(b=>b.name.Contains("Clone") && b._healthAmount==17);
                TravelCheck(loot._armorAmount==11,"native loot quantities are captured after native post-drop initialization");
                Player.Instance.GetComponent<PlayerHitProcessorComponent>().SetCurrentHitPoints(100);
                Player.Instance.GetComponent<PlayerHitProcessorComponent>().SetArmor(0);
                _sharedCombatHealth=Player.Instance.HitPoints.Current;
                _sharedCombatHitApplications=_world.IncomingHitApplications;
                _sharedCombatNpc.Hit(new HitData {damage=7,weakSpotMultiplier=1,dealer=Player.Instance.Cast<IAiActor>(),position=_sharedCombatNpc.CenterOfMass,direction=Vector3.forward});
                TravelCheck(_sharedCombatNpc.HitPoints.Current==57,"P2 attack does not independently damage its enemy replica");
                TravelMark("client-hit"); _sharedCombatStep=2; break;
            case 2 when Marked("host-hit-result") && _sharedCombatNpc!.HitPoints.Current==50:
                TravelCheck(true,"P2 enemy replica receives shared authoritative health");
                TravelMark("client-health-synced"); _sharedCombatStep=3; break;
            case 3 when Marked("host-sent-hit") && Player.Instance.HitPoints.Current<_sharedCombatHealth:
                TravelCheck(_world!.IncomingHitApplications==_sharedCombatHitApplications+1,"host enemy melee hit applies once through P2 native personal health/difficulty rules");
                TravelMark("client-personal-hit"); _sharedCombatStep=4; break;
            case 4 when File.Exists(Marker("door-key")):
                _sharedCombatDoor=_world!.ComponentForTest(File.ReadAllText(Marker("door-key")))!.Cast<Door>();
                Player.Instance.Teleport(_sharedCombatDoor.transform.position+_sharedCombatDoor.transform.forward,Vector3.zero,Vector3.zero);
                _sharedCombatAt=Time.unscaledTime+1f; _sharedCombatStep=5; break;
            case 5 when Time.unscaledTime>=_sharedCombatAt:
                _sharedCombatDoor!.HandleInteract(Player.Instance);
                TravelMark("client-door-request"); _sharedCombatStep=6; break;
            case 6 when Marked("host-door-open") && Mathf.Abs(_sharedCombatDoor!._desiredRotation)>1f:
                TravelCheck(true,"P2 receives host door opening and lock state");
                TravelMark("client-door-shared"); _sharedCombatStep=7; break;
            case 7 when Marked("host-fire-ready"):
                Player.Instance.Teleport(_sharedCombatNpc!.FeetPosition-_sharedCombatNpc.Transform.forward*3f,Vector3.zero,Vector3.zero);
                _sharedCombatProjectileHealth=_sharedCombatNpc.HitPoints.Current;
                _sharedCombatAt=Time.unscaledTime+1f; _sharedCombatStep=8; break;
            case 8 when Time.unscaledTime>=_sharedCombatAt:
                var weapon=Resources.FindObjectsOfTypeAll<WeaponId>().First(w=>w.name=="WEAPON_Pistol");
                var model=weapon.PlayerWeaponModel.Cast<PlayerWeaponModelPistol>().PrimaryFire.ProjectileModel;
                var direction=(_sharedCombatNpc!.CenterOfMass-Player.Instance.CenterOfMass).normalized;
                var origin=Player.Instance.CenterOfMass+direction*0.75f;
                model.CreateInstance(origin,Quaternion.LookRotation(direction),direction,Player.Instance.Cast<IAiActor>(),weapon);
                TravelMark("client-fired"); _sharedCombatStep=9; break;
            case 9 when Marked("host-projectile-result") && _sharedCombatNpc!.HitPoints.Current<_sharedCombatProjectileHealth:
                TravelCheck(true,"P2 receives authoritative projectile damage without local duplicate resolution");
                TravelMark("client-projectile-shared"); _sharedCombatStep=10; break;
            case 10 when Marked("host-models-ready"):
                // Keep factory coverage independent of self-inflicted splash damage.
                // Incoming enemy damage/difficulty was asserted separately above.
                Player.Instance.GetComponent<PlayerHitProcessorComponent>().SetCurrentHitPoints(10000);
                var entries=_world!.ModelsForTest.Where(p=>Resources.FindObjectsOfTypeAll<WeaponId>().Any(w=>w.ID==_world.ModelOriginForTest(p.Key) && w.Category==DLCCategory.NONE)).ToArray();
                _sharedCombatModels=entries.Select(p=>p.Value).ToArray();
                _sharedCombatWeapons=entries.Select(p=>Resources.FindObjectsOfTypeAll<WeaponId>().First(w=>w.ID==_world.ModelOriginForTest(p.Key))).ToArray();
                File.WriteAllText(Marker("model-count"),(_sharedCombatModels.Length*3).ToString());
                File.WriteAllLines(Marker("model-coverage.txt"),entries.Select(p=>_world.ModelOriginForTest(p.Key)+" "+p.Value.GetIl2CppType().Name+" "+p.Value.Prefab.name));
                _sharedCombatAt=Time.unscaledTime+0.5f;_sharedCombatStep=11;break;
            case 11 when Time.unscaledTime>=_sharedCombatAt:
                var modelIndex=_sharedCombatModelIndex/3;
                if(modelIndex<_sharedCombatModels!.Length)
                {
                    var aim=Player.Instance.WorldCamera.transform.forward;
                    var factory=_sharedCombatModels[modelIndex];
                    var nativeShot=factory.TryCast<ProjectileModelDildo>() is {} chargedBow
                        ?chargedBow.CreateInstance(Player.Instance.CenterOfMass+aim*0.8f,Quaternion.LookRotation(aim),aim,Player.Instance.Cast<IAiActor>(),_sharedCombatWeapons![modelIndex],(_sharedCombatModelIndex%3)*0.5f)
                        :factory.CreateInstance(Player.Instance.CenterOfMass+aim*0.8f,Quaternion.LookRotation(aim),aim,Player.Instance.Cast<IAiActor>(),_sharedCombatWeapons![modelIndex]);
                    var nativeWeapon=Resources.FindObjectsOfTypeAll<PlayerWeapon>().FirstOrDefault(w=>w.gameObject.scene.IsValid() && w.WeaponId?.ID==_sharedCombatWeapons[modelIndex].ID && w.Player?.Pointer==Player.Instance.Pointer);
                    if(nativeWeapon!=null) nativeShot.SetPlayerWeapon(nativeWeapon);
                    _sharedCombatModelIndex++;_sharedCombatAt=Time.unscaledTime+0.05f;
                }
                else {TravelMark("client-models-fired");_sharedCombatAt=Time.unscaledTime+6f;_sharedCombatStep=12;}
                break;
            case 12 when Marked("host-models-complete") && Time.unscaledTime>=_sharedCombatAt:
                TravelCheck(true,"loaded primary/secondary native projectile factories complete presentation without replica authority errors");
                RetireCombatFixtureProjectiles();
                TravelMark("client-models-presented");_sharedCombatStep=13;break;
            case 13 when Marked("host-beam-ready"):
                _sharedCombatNpc=_world!.EnemyForTest(File.ReadAllText(Marker("enemy-asset")));
                if(_sharedCombatNpc==null || _sharedCombatNpc.HitPoints.Current!=57) return;
                TravelCheck(PlayerSessionState.FindNearby(Player.Instance,_sharedCombatNpc.FeetPosition,out var beamSpawn),"continuous-beam fixture uses grounded, unblocked space beside the target");
                Player.Instance.Teleport(beamSpawn,Vector3.zero,Vector3.zero);
                _sharedCombatAt=Time.unscaledTime+1f;_sharedCombatStep=14;break;
            case 14 when Time.unscaledTime>=_sharedCombatAt:
                var lightning=Resources.FindObjectsOfTypeAll<WeaponId>().First(w=>w.name=="WEAPON_LightningGun");
                var beamInventory=Player.Instance.GetComponent<PlayerInventoryComponent>();beamInventory.AddItem(lightning,1);
                var beamGear=new PlayerInventoryComponent.SaveData(beamInventory);beamGear.CurrentWeapon=lightning.ID;beamInventory.LoadState(beamGear);
                var actualLightning=Player.Instance.CurrentWeapon.Cast<Hyperstrange.PBD.LightningGun.PlayerWeaponLightningGun>();
                var template=actualLightning._beam;
                var aimPoint=_sharedCombatNpc!.CenterOfMass;
                var viewRotation=Quaternion.LookRotation(aimPoint-Player.Instance.WorldCamera.transform.position).eulerAngles;
                Player.Instance.Teleport(Player.Instance.Transform.position,new(0,viewRotation.y,0),new(viewRotation.x,0,0));
                _sharedCombatBeam=new Hyperstrange.PBD.LightningGun.LightningGunBeam();
                _sharedCombatBeam._beamRendererPrefab=template._beamRendererPrefab;_sharedCombatBeam._muzzleVfxPrefab=template._muzzleVfxPrefab;
                _sharedCombatBeam._beamActiveLoopEvent=template._beamActiveLoopEvent;_sharedCombatBeam._beamEndEvent=template._beamEndEvent;
                var hitter=new Hyperstrange.PBD.Util.Hitter();hitter.SetDealer(Player.Instance.Cast<IAiActor>());hitter.SetWeaponId(lightning);hitter.SetWeakSpotMultiplier(1);
                var camera=actualLightning._primaryActionFx.Origin;
                _sharedCombatBeam.Init(hitter,30,10,camera,camera,actualLightning.transform,actualLightning.transform,Player.Instance);
                Plugin.Logger.LogInfo($"BEAM TEST origin={camera.position}; aim={aimPoint}; enemy={_sharedCombatNpc.FeetPosition}; colliders={string.Join(";",_sharedCombatNpc.GetComponentsInChildren<Collider>(true).Select(c=>c.name+":"+c.enabled+":"+c.gameObject.activeInHierarchy+":"+c.bounds.center))}");
                _sharedCombatBeam.OnCollisionHit=(Il2CppSystem.Action<RaycastHit,float>)(System.Action<RaycastHit,float>)((hit,delta)=>Plugin.Logger.LogInfo($"BEAM TEST hit {hit.collider?.name} at {hit.point}; dt={delta}"));
                _sharedCombatBeam.SetActive(true);_sharedCombatBeam.SetMaterial(template._beamRendererPrefab._material,Color.white);
                _sharedCombatBeamUntil=Time.unscaledTime+0.6f;_sharedCombatStep=15;break;
            case 15:
                if(Time.unscaledTime<_sharedCombatBeamUntil) _sharedCombatBeam!.HandleUpdate(Time.unscaledDeltaTime);
                else {_sharedCombatBeam!.SetActive(false);TravelMark("client-beam-ended");_sharedCombatStep=16;}
                break;
            case 16 when Marked("host-beam-result") && _sharedCombatNpc!.HitPoints.Current<57:
                TravelCheck(true,"P2 receives native beam damage through shared health without replica damage");
                TravelMark("client-beam-shared");_sharedCombatStep=17;break;
            case 17 when Marked("host-defense-ready"):
                var unarmed=Resources.FindObjectsOfTypeAll<WeaponId>().First(w=>w.name=="WEAPON_NoWeapon");
                var defenseInventory=Player.Instance.GetComponent<PlayerInventoryComponent>();defenseInventory.AddItem(unarmed,1);
                var defenseGear=new PlayerInventoryComponent.SaveData(defenseInventory);defenseGear.CurrentWeapon=unarmed.ID;defenseInventory.LoadState(defenseGear);
                var defense=Player.Instance.GetComponent<PlayerHitProcessorComponent>();
                defense.SetCurrentHitPoints(100);defense.SetArmor(0);defense._blockProcessor.SetBlockActive(true);defense._reflectProcessor.SetReflectActive(false);
                _sharedCombatAt=Time.unscaledTime+0.5f;_sharedCombatStep=18;break;
            case 18 when Time.unscaledTime>=_sharedCombatAt:
                TravelMark("client-block-ready");_sharedCombatAt=Time.unscaledTime+1f;_sharedCombatStep=19;break;
            case 19 when Marked("host-block-hit") && Time.unscaledTime>=_sharedCombatAt:
                TravelCheck(Player.Instance.HitPoints.Current==100,"native P2 frontal block stops host incoming damage locally");
                TravelMark("client-blocked");_sharedCombatStep=20;break;
            case 20 when Marked("host-reflect-ready"):
                var reflector=Player.Instance.GetComponent<PlayerHitProcessorComponent>();reflector._blockProcessor.SetBlockActive(false);reflector._reflectProcessor.SetReflectActive(true);
                _testLocalReflections=_world!.LocalReflectionCreatesForTest;
                _sharedCombatAt=Time.unscaledTime+0.5f;_sharedCombatStep=21;break;
            case 21 when Time.unscaledTime>=_sharedCombatAt:
                TravelMark("client-reflect-ready");_sharedCombatStep=22;break;
            case 22 when Marked("host-reflect-hit") && _world!.LocalReflectionCreatesForTest>_testLocalReflections:
                TravelCheck(Player.Instance.HitPoints.Current==100,"native P2 kick reflection stops incoming damage and preserves success callbacks");
                TravelMark("client-reflected");_sharedCombatStep=23;break;
            case 23 when Marked("host-reflect-shared"):
                Player.Instance.GetComponent<PlayerHitProcessorComponent>()._reflectProcessor.SetReflectActive(false);
                TravelMark("client-defense-complete");_sharedCombatStep=24;break;
            case 24 when _session==null && SessionLifecycle.Current==null && !SessionManager.SessionActive:
                TravelCheck(true,"host departure clears shared combat and returns P2 to title");
                TravelMark("client-pass"); _sharedCombatStep=99; break;
        }
    }
    private void SendDefenseTestHit(bool projectileHit)
    {
        var remote=_world!.Remote!;var forward=remote.ReferenceTransform.forward;
        Projectile? projectile=null;
        if(projectileHit)
        {
            var weapon=Resources.FindObjectsOfTypeAll<WeaponId>().First(w=>w.name=="WEAPON_Pistol");
            var model=weapon.PlayerWeaponModel.Cast<PlayerWeaponModelPistol>().PrimaryFire.ProjectileModel;
            projectile=model.CreateInstance(remote.CenterOfMass+forward*2f,Quaternion.LookRotation(-forward),-forward,_sharedCombatNpc!.Cast<IAiActor>(),_sharedCombatNpc.NpcId);
        }
        remote.Hit(new HitData{damage=9,weakSpotMultiplier=1,position=remote.CenterOfMass+forward*2f,direction=-forward,dealer=_sharedCombatNpc!.Cast<IAiActor>(),originEntity=_sharedCombatNpc.NpcId,projectile=projectile});
        if(projectile!=null) projectile.gameObject.SetActive(false);
    }
    private static void RetireCombatFixtureProjectiles()
    {
        // Factory coverage includes long-lived bouncing explosives. They must
        // not contaminate the later beam/block/reflection assertions.
        foreach(var projectile in Object.FindObjectsOfType<Projectile>()) projectile.gameObject.SetActive(false);
    }
}
