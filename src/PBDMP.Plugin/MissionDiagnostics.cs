using System.Text.Json;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using UnityEngine;

namespace PBDMP.Plugin;

public sealed partial class MultiplayerBehaviour
{
    private sealed record MissionBreakFixture(string Key,string Path,bool Destroyed,bool Blocking,bool Visible,string[] StaticParts);
    private PlayerTriggerNotificationComponent? _missionHint;
    private Notification? _missionUnrelatedHint;
    private BreakableObject[] _missionBreaks=Array.Empty<BreakableObject>();
    private static bool MissionTest=>Environment.GetEnvironmentVariable("PBDMP_MISSION_TEST")=="1";
    private void BeginMissionWallFixture()
    {
        if(!MissionTest) return;
        var fuse=UnityEngine.Object.FindObjectsOfType<FuseBox>().First();
        var events=fuse.GetComponent<ExplosionListener>()._onExplosionEvents;
        for(var i=0;i<events.GetPersistentEventCount();i++)
            if(events.GetPersistentTarget(i)?.TryCast<PlayerTrigger>() is {} trigger && trigger.GetComponent<PlayerTriggerNotificationComponent>() is {} hint) _missionHint=hint;
        TravelCheck(_missionHint!=null,"fuse callback identifies its authored urine hint owner");
        _missionHint!.OnPlayerEnter(Player.Instance);
        _missionUnrelatedHint=new Notification("Independent diagnostic notification",false);
        Player.Instance.ShowNotification(_missionUnrelatedHint);
        _missionBreaks=UnityEngine.Object.FindObjectsOfType<BreakableObject>(true).Where(b=>b.gameObject.scene.IsValid() && MissionPath(b.transform)=="OBJECTIVES/BreakableWall").ToArray();
        TravelCheck(_missionBreaks.Length==1,"Neighborhood regression independently locates OBJECTIVES/BreakableWall");
        var bounds=_missionBreaks[0]._breaker._wholeObject.GetComponentsInChildren<Collider>(true).First(c=>!c.isTrigger && c.enabled).bounds;
        File.WriteAllText(Marker("mission-wall-bounds.json"),JsonSerializer.Serialize(new[]{bounds.center.x,bounds.center.y,bounds.center.z,bounds.size.x,bounds.size.y,bounds.size.z}));
        File.WriteAllText(Marker("mission-breaks-before.json"),JsonSerializer.Serialize(_missionBreaks.Select(MissionBreakState)));
    }
    private static MissionBreakFixture MissionBreakState(BreakableObject b)
    {
        var whole=b._breaker._wholeObject;
        return new(WorldCatalogue.TransformKey(b.transform),MissionPath(b.transform),b._destroyed,
            whole.GetComponentsInChildren<Collider>(true).Any(c=>c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger),
            whole.GetComponentsInChildren<Renderer>(true).Any(r=>r.enabled && r.gameObject.activeInHierarchy),
            b._breaker._parts==null?Array.Empty<string>():b._breaker._parts.GetComponentsInChildren<Component>(true)
                .Where(c=>c.gameObject.activeInHierarchy && (c.TryCast<Renderer>()?.enabled??c.TryCast<Collider>()?.enabled??false) && c.GetComponentInParent<Rigidbody>()==null)
                .Select(c=>MissionPath(c.transform)+"|"+c.GetIl2CppType().Name+"|"+c.gameObject.activeInHierarchy+"|"+(c.TryCast<Renderer>()?.enabled??c.TryCast<Collider>()!.enabled)).OrderBy(v=>v).ToArray());
    }
    private static string MissionPath(Transform t)
    {var names=new List<string>();for(var p=t;p!=null;p=p.parent) names.Add(p.name);names.Reverse();return string.Join("/",names);}
    private void WriteMissionWallFixture(string name)
    {
        if(!MissionTest) return;
        var changed=_missionBreaks.Where(b=>b!=null && b._destroyed).Select(MissionBreakState).ToArray();
        TravelCheck(changed.Length>0,"fuse explosion destroys authored breakable geometry on P1");
        TravelCheck(changed.All(b=>!b.Blocking && !b.Visible),"P1 removes the actual mission wall collider and renderer");
        if(name=="urine-live.json")
        {
            var view=Player.Instance.GetComponentInChildren<Hyperstrange.PBD.PlayerView.PlayerNotificationView>(true);
            TravelCheck(!_missionHint!._playerIn && !view._notificationsStack.Contains(_missionHint._notification),"P2 fuse activation retires P1's exact urine hint");
            TravelCheck(view._notificationsStack.Contains(_missionUnrelatedHint!),"retiring the urine hint preserves unrelated local notifications");
            Player.Instance.HideNotification(_missionUnrelatedHint!);
        }
        CheckMissionPortal();
        if(name=="urine-live.json") ChooseMissionPassage();
        File.WriteAllText(Marker(name+"-breaks.json"),JsonSerializer.Serialize(changed));
    }
    private void CheckMissionWallFixture(string name)
    {
        if(!MissionTest) return;
        var local=UnityEngine.Object.FindObjectsOfType<BreakableObject>(true).Where(b=>b.gameObject.scene.IsValid()).ToDictionary(b=>WorldCatalogue.TransformKey(b.transform));
        foreach(var expected in JsonSerializer.Deserialize<MissionBreakFixture[]>(File.ReadAllText(Marker(name+"-breaks.json")))!)
        {
            TravelCheck(local.TryGetValue(expected.Key,out var target),"destroyed mission geometry exists on P2: "+expected.Path);
            var actual=MissionBreakState(target!);
            File.AppendAllText(Marker("mission-wall-comparison.jsonl"),JsonSerializer.Serialize(new{expected,actual})+"\n");
            TravelCheck(actual.Destroyed==expected.Destroyed && actual.Blocking==expected.Blocking && actual.Visible==expected.Visible && actual.StaticParts.SequenceEqual(expected.StaticParts),
                "wall opening and remaining static wall geometry match P1: "+expected.Path);
        }
        CheckMissionPortal();
        CheckMissionWallPassage();
    }
    private void CheckMissionPortal()
    {
        if(!MissionTest) return;
        var portals=_world!.CatalogueForTest.UrineSurfaces.SelectMany(p=>p.Value.Select(c=>c.TryCast<OcclusionPortal>())).Where(p=>p!=null).Select(p=>p!).ToArray();
        File.AppendAllText(Marker("mission-portals.txt"),TravelRole+": "+string.Join(";",portals.Select(p=>MissionPath(p.transform)+"="+p.open))+"\n");
        if(TravelRole=="host") File.WriteAllText(Marker("mission-portal-state.json"),JsonSerializer.Serialize(portals.ToDictionary(p=>WorldCatalogue.TransformKey(p.transform),p=>p.open)));
        else
        {
            var expected=JsonSerializer.Deserialize<Dictionary<string,bool>>(File.ReadAllText(Marker("mission-portal-state.json")))!;
            TravelCheck(portals.Length>0 && portals.All(p=>expected.TryGetValue(WorldCatalogue.TransformKey(p.transform),out var open) && p.open==open),"native authored portal state matches P1 before crossing the garage opening");
        }
    }
    private void CheckMissionWallPassage()
    {
        var data=JsonSerializer.Deserialize<float[]>(File.ReadAllText(Marker("mission-wall-route.json")))!;
        var start=new Vector3(data[0],data[1],data[2]);var normal=new Vector3(data[3],data[4],data[5]);var distance=data[6];
        TravelCheck(TryMissionPassage(start,normal,distance),"native player controller passes through the destroyed wall opening");
    }
    private void ChooseMissionPassage()
    {
        var data=JsonSerializer.Deserialize<float[]>(File.ReadAllText(Marker("mission-wall-bounds.json")))!;
        var normal=data[3]<data[5]?Vector3.right:Vector3.forward;var along=Vector3.Cross(normal,Vector3.up);
        var distance=(data[3]<data[5]?data[3]:data[5])*0.5f+0.7f;var width=data[3]<data[5]?data[5]:data[3];
        foreach(var offset in new[]{0f,-0.2f,0.2f,-0.35f,0.35f}) foreach(var height in new[]{0.1f,0.6f,1.1f,1.6f})
        {
            var start=new Vector3(data[0],data[1]-data[4]*0.5f+height,data[2])-normal*distance+along*(width*offset);
            if(!TryMissionPassage(start,normal,distance*2)) continue;
            File.WriteAllText(Marker("mission-wall-route.json"),JsonSerializer.Serialize(new[]{start.x,start.y,start.z,normal.x,normal.y,normal.z,distance*2}));return;
        }
        TravelCheck(false,"P1 has a native-controller route through the destroyed mission wall");
    }
    private bool TryMissionPassage(Vector3 start,Vector3 normal,float distance)
    {
        var before=Player.Instance.FeetPosition;
        Player.Instance.Teleport(start,Vector3.zero,Vector3.zero);Physics.SyncTransforms();
        var controller=Player.Instance.GetComponent<CharacterController>();
        var actualStart=Player.Instance.FeetPosition;controller.Move(normal*distance);
        var progress=Vector3.Dot(Player.Instance.FeetPosition-actualStart,normal);
        File.AppendAllText(Marker("mission-passage.txt"),$"{TravelRole}: start={actualStart}, normal={normal}, required={distance}, moved={progress}\n");
        if(progress<distance-0.2f) foreach(var c in Physics.OverlapCapsule(actualStart+Vector3.up*0.4f,actualStart+normal*distance+Vector3.up*0.4f,0.4f))
            if(!c.isTrigger && c.GetComponentInParent<Player>()==null) File.AppendAllText(Marker("mission-passage-colliders.txt"),TravelRole+": "+MissionPath(c.transform)+"\n");
        Player.Instance.Teleport(before,Vector3.zero,Vector3.zero);
        return progress>=distance-0.2f;
    }
}
