using Hyperstrange.PBD;
using Hyperstrange.Base;
using UnityEngine;

namespace PBDMP.Plugin;

public sealed partial class MultiplayerBehaviour
{
    private LevelId[]? _auditLevels;
    private int _auditIndex;
    private float _auditDeadline;
    private float _auditReadyAt;
    private bool _auditFinished;
    private bool _auditMenu;
    private void RunCampaignAudit()
    {
        if(_auditFinished) return;
        if(_auditLevels==null)
        {
            _port!.text=Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!;Host();
            var levels=new List<LevelId>();foreach(var level in LevelManager.AllLevels) if(SessionLifecycle.Campaign(level)) levels.Add(level);
            _auditLevels=levels.OrderBy(l=>l.EpisodeId?.Id??0).ThenBy(l=>l.Id).ToArray();
            var selected=Environment.GetEnvironmentVariable("PBDMP_AUDIT_LEVELS");
            if(!string.IsNullOrEmpty(selected)) _auditLevels=_auditLevels.Where(l=>selected.Split(',').Contains(l.name)).ToArray();
            if(_auditLevels.Length==0) throw new InvalidOperationException("No campaign audit levels selected.");
            File.WriteAllLines(Marker("audit-levels.txt"),_auditLevels.Select(l=>l.name));
            LoadAuditLevel();return;
        }
        if(_auditMenu)
        {
            if(SessionManager.SessionActive) return;
            if(_auditReadyAt==0) {_auditReadyAt=Time.unscaledTime+1f;return;}
            if(Time.unscaledTime<_auditReadyAt) return;
            _auditMenu=false;LoadAuditLevel();return;
        }
        if(Time.unscaledTime>_auditDeadline) throw new TimeoutException("Campaign audit failed to load "+_auditLevels[_auditIndex].name);
        if(_lifecycle is {NativeReady:false}) {_lifecycle.AutoContinue();return;}
        if(_world?.Active!=true) return;
        if(_auditReadyAt==0) {_auditReadyAt=Time.unscaledTime+2f;return;}
        if(Time.unscaledTime<_auditReadyAt) return;
        SharedWorldPatches.DiagnosticFreeze=true;
        File.WriteAllText(Marker("coverage-"+_auditLevels[_auditIndex].name+".tsv"),_world.CatalogueReport());
        File.WriteAllLines(Marker("missions-"+_auditLevels[_auditIndex].name+".tsv"),_world.CatalogueForTest.MissionAudit);
        File.WriteAllLines(Marker("census-"+_auditLevels[_auditIndex].name+".tsv"),UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true).GroupBy(c=>c.GetIl2CppType().Name).OrderBy(g=>g.Key).Select(g=>g.Key+"\t"+g.Count()));
        File.WriteAllLines(Marker("actors-"+_auditLevels[_auditIndex].name+".tsv"),_world.CatalogueForTest.Components.Values.Select(c=>c.TryCast<SpawnPoint>()).Where(s=>s?.NpcId?._npcPrefab!=null)
            .Select(s=>s!.NpcId).DistinctBy(a=>WorldCatalogue.NpcAssetKey(a)).OrderBy(a=>a.name)
            .Select(a=>$"{a.name}\t{a._npcPrefab.GetComponent<Npc>()?.GetIl2CppType().Name}\t{WorldCatalogue.NpcAssetKey(a)}"));
        File.WriteAllLines(Marker("bosses-"+_auditLevels[_auditIndex].name+".tsv"),Resources.FindObjectsOfTypeAll<MonoBehaviour>().Where(c=>c.GetIl2CppType().Name.Contains("BossArena")).Select(c=>$"{c.GetIl2CppType().Name}\t{c.name}\tscene={c.gameObject.scene.name}\tvalid={c.gameObject.scene.IsValid()}\tactive={c.gameObject.activeInHierarchy}"));
        File.WriteAllLines(Marker("sequences-"+_auditLevels[_auditIndex].name+".tsv"),_world.CatalogueForTest.SequenceAudit);
        Plugin.Logger.LogInfo("CAMPAIGN AUDIT: "+_auditLevels[_auditIndex].name);
        _auditIndex++;
        if(_auditIndex==_auditLevels.Length)
        {ResetSession();SharedWorldPatches.DiagnosticFreeze=false;TravelMark("host-pass");_auditFinished=true;return;}
        // Use the supported title-to-campaign flow between separate map audits.
        // Direct cross-episode loads reuse native NPC pools before full teardown.
        SharedWorldPatches.DiagnosticFreeze=false;GameManager.OpenTitle();_auditMenu=true;_auditReadyAt=0;
    }
    private void LoadAuditLevel()
    {SharedWorldPatches.DiagnosticFreeze=false;_auditReadyAt=0;_auditDeadline=Time.unscaledTime+120;GameManager.LoadLevel(_auditLevels![_auditIndex],true);}
}
