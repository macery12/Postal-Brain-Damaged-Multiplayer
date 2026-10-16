using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using Hyperstrange.PBD.PlayerComponents;
using UnityEngine;

namespace PBDMP.Plugin;

// Native runtime hazards are created after catalogue discovery, as pooled boss
// attack prefabs are. This is deliberately not an authored-component-only test.
public sealed partial class MultiplayerBehaviour
{
    private PawShockwaveTrigger? _hazardPaw;
    private int _hazardHits;
    private float HazardExpectedHp=>float.Parse(File.ReadAllText(Marker("hazard-hp")),System.Globalization.CultureInfo.InvariantCulture);
    private void PrepareHazardFixture()
    {
        var root=new GameObject("PBDMP_RuntimePawRegression");
        root.transform.position=Player.Instance.FeetPosition+Vector3.up*100;
        _hazardPaw=root.AddComponent<PawShockwaveTrigger>();
        _hazardPaw._damage=7;
        _hazardPaw._trigger=root.AddComponent<BoxCollider>();_hazardPaw._trigger.isTrigger=true;
        TravelCheck(!_world!.OwnsMission(_hazardPaw),"native paw fixture has the same uncatalogued lifetime as a pooled attack");
        TravelCheck(Player.Instance.Collider.gameObject.layer==7,"native paw victim uses the game's required player layer");
        var hp=Player.Instance.GetComponent<PlayerHitProcessorComponent>();hp.SetCurrentHitPoints(100);hp.SetArmor(0);
        _hazardHits=_world.IncomingHitApplications;
    }
    private void RunHazardTest()
    {
        if(_pathStep==99) return;
        if(_pathStarted==0) _pathStarted=Time.unscaledTime;
        if(Time.unscaledTime-_pathStarted>180) throw new TimeoutException("Runtime hazard fixture stalled at "+_pathStep);
        if(_pathStep==0)
        {
            _port!.text=Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!;
            if(TravelRole=="host") {Host();SharedWorldPatches.DiagnosticFreeze=true;GameManager.LoadLevel(TestLevel(),true);}
            else {_address!.text="127.0.0.1";Join();}
            _pathStep=1;return;
        }
        if(_lifecycle is {NativeReady:false}) {_lifecycle.AutoContinue();return;}
        if(_lifecycle?.LocalTutorialActive==true)
        {Player.Instance!.GetComponent<PlayerTutorialComponent>().OnUseDown();return;}
        if(TravelRole=="host") switch(_pathStep)
        {
            case 1 when _world?.Active==true:
                PrepareHazardFixture();TravelMark("host-ready");_pathStep=2;break;
            case 2 when _lifecycle!.State.Released && Marked("hazard-client-ready"):
                GameManager.DisableMenu();_pathAt=Time.unscaledTime+1;_pathStep=20;break;
            case 20 when Time.unscaledTime>=_pathAt && !_lifecycle!.Frozen:
                TravelCheck(!Player.Instance.CancelIncomingDamage && !DebugManager.GodMode,"native paw fixture has no menu/god-mode damage immunity");
                _hazardPaw!.Hit(Player.Instance.Cast<IHittable>(),Player.Instance.Collider);
                TravelCheck(Player.Instance.HitPoints.Current<100 && Player.Instance.HitPoints.Current>0,"native ownerless paw hit actually damages P1; health="+Player.Instance.HitPoints.Current);
                File.WriteAllText(Marker("hazard-hp"),Player.Instance.HitPoints.Current.ToString(System.Globalization.CultureInfo.InvariantCulture));
                TravelMark("hazard-check-client");_pathStep=3;break;
            case 3 when Marked("hazard-client-passive"):
                _pathAt=Time.unscaledTime+2;_pathStep=4;break;
            case 4 when Time.unscaledTime>=_pathAt:
                var remote=_world!.Remote!;
                var body=remote.GetComponent<Collider>();
                var stale=UnityEngine.Object.FindObjectsOfType<DamageTrigger>().FirstOrDefault(h=>h._hittablesCollidersDict!=null &&
                    h.GetComponents<Collider>().Any(c=>c.enabled && c.isTrigger && !c.bounds.Intersects(body.bounds)));
                if(stale!=null)
                {
                    // Reproduce delayed OnTriggerExit: the native contact ledger
                    // still contains a rider after its deck/capsule has moved on.
                    stale.OnTriggerEnter(body);
                    TravelCheck(stale._hittablesCollidersDict.ContainsKey(remote.Cast<IHittable>()),"native hazard fixture seeds a real stale rider contact");
                    var damage=stale._damage;stale._damage=1;stale._tickTimer=Time.time-1;
                    try {stale.OnSlowUpdate(.2f);}finally {stale._damage=damage;stale.OnTriggerExit(body);}
                    TravelMark("hazard-stale-contact-checked");
                }
                else TravelCheck(LevelManager.CurrentLevel.name!="LEVEL_Asylum_Area69","Area69 supplies the authored stale-contact hazard fixture");
                // Positive control: a true current overlap still damages P2.
                var root=new GameObject("PBDMP_OverlappingHazardRegression");root.transform.position=remote.CenterOfMass;
                root.layer=stale?.gameObject.layer??0;
                var volume=root.AddComponent<BoxCollider>();volume.isTrigger=true;volume.size=new Vector3(1,3,1);
                Physics.SyncTransforms();
                var overlap=Physics.ComputePenetration(body,remote.FeetPosition,remote.transform.rotation,volume,volume.transform.position,volume.transform.rotation,out _,out _);
                TravelCheck(overlap,"positive native damage fixture has a genuine current overlap; overlap="+overlap+", body="+body.bounds+", volume="+volume.bounds+", layers="+body.gameObject.layer+"/"+volume.gameObject.layer);
                var touching=root.AddComponent<DamageTrigger>();touching._damage=7;touching._timeBetweenTick=600;touching._tickTimer=Time.time-1;
                touching._hittablesCollidersDict.Clear();touching.OnTriggerEnter(body);
                touching.OnSlowUpdate(.2f);UnityEngine.Object.Destroy(root);
                _hazardPaw!.Hit(remote.Cast<IHittable>(),body);
                TravelMark("hazard-host-hit");_pathStep=5;break;
            case 5 when Marked("hazard-client-hit"):
                TravelCheck(Mathf.Abs(Player.Instance.HitPoints.Current-HazardExpectedHp)<.001f,"P2's hit does not damage P1 again");
                GameManager.PauseGame();_pathAt=Time.unscaledTime+2;_pathStep=6;break;
            case 6 when Time.unscaledTime>=_pathAt && Marked("hazard-client-paused"):
                _hazardPaw!.Hit(Player.Instance.Cast<IHittable>(),Player.Instance.Collider);
                _hazardPaw.Hit(_world!.Remote!.Cast<IHittable>(),_world.Remote.GetComponent<Collider>());
                TravelCheck(Mathf.Abs(Player.Instance.HitPoints.Current-HazardExpectedHp)<.001f,"native ownerless paw cannot damage P1 during shared pause");
                TravelMark("hazard-pause-hit");_pathStep=7;break;
            case 7 when Marked("hazard-client-pause-checked"):
                GameManager.DisableMenu();UnityEngine.Object.Destroy(_hazardPaw!.gameObject);
                EndFrozenDiagnosticSession();_pathStep=8;break;
            case 8 when Marked("client-pass"):
                TravelMark("host-pass");_pathStep=99;break;
        }
        else switch(_pathStep)
        {
            case 1 when _world?.Active==true && _lifecycle!.State.Released && _lifecycle.PersonalReady:
                PrepareHazardFixture();TravelMark("hazard-client-ready");_pathStep=2;break;
            case 2 when Marked("hazard-check-client"):
                TravelCheck(!Player.Instance.CancelIncomingDamage && !DebugManager.GodMode,"P2 has no native damage immunity that could hide duplicate hits");
                _hazardPaw!.Hit(Player.Instance.Cast<IHittable>(),Player.Instance.Collider);
                TravelCheck(Player.Instance.HitPoints.Current==100,"P2's uncatalogued native paw replica cannot apply local duplicate damage");
                TravelMark("hazard-client-passive");_pathStep=3;break;
            case 3 when Marked("hazard-host-hit"):
                _pathAt=Time.unscaledTime+.5f;_pathStep=30;break;
            case 30 when Time.unscaledTime>=_pathAt:
                TravelCheck(_world!.IncomingHitApplications==_hazardHits+2 && Mathf.Abs(Player.Instance.HitPoints.Current-(100-2*(100-HazardExpectedHp)))<.001f,"native paw and genuine hazard contact each reach P2 once; stale contact does not; hits="+(_world.IncomingHitApplications-_hazardHits)+", health="+Player.Instance.HitPoints.Current);
                TravelMark("hazard-client-hit");_pathStep=4;break;
            case 4 when _lifecycle!.Frozen:
                TravelMark("hazard-client-paused");_pathStep=5;break;
            case 5 when Marked("hazard-pause-hit"):
                _pathAt=Time.unscaledTime+.5f;_pathStep=6;break;
            case 6 when Time.unscaledTime>=_pathAt:
                TravelCheck(_world!.IncomingHitApplications==_hazardHits+2 && Mathf.Abs(Player.Instance.HitPoints.Current-(100-2*(100-HazardExpectedHp)))<.001f,"shared pause cannot deliver an extra paw hit to P2");
                UnityEngine.Object.Destroy(_hazardPaw!.gameObject);TravelMark("hazard-client-pause-checked");_pathStep=7;break;
            case 7 when SessionLifecycle.Current==null && !SessionManager.SessionActive:
                TravelMark("client-pass");_pathStep=99;break;
        }
    }
}
