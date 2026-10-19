using Hyperstrange.PBD;

namespace PBDMP.Plugin;
internal sealed partial class CoopSaveService
{
    internal void CampaignCompleted(LevelId level)
    {
        if(_world.ReplayingCompletion || _life.IsClient || !_life.NativeReady || _life.RestoringWorld || level.ID!=_life.State.Level || !SessionLifecycle.Campaign(level)) return;
        // Native UpdateMetaData also uploads leaderboards and writes profiles. Its guard stays in place.
        // Apply only the co-op session's in-memory level data; lifecycle cleanup restores solo metadata.
        var stats=LevelController.GetLevelStats();var data=new LevelId.Data();data.Save(level);
        data.State=LevelId.LevelState.FINISHED;data.KillsStat=stats.KillsStat;data.SecretsStat=stats.SecretsStat;data.QuantityStat=stats.QuantityStat;
        if(data.AnyTimeStat.CompletionTime<=0 || stats.TimeStat.CompletionTime<data.AnyTimeStat.CompletionTime) data.AnyTimeStat=stats.TimeStat;
        if(stats.KillsStat.CurrentCount>=stats.KillsStat.MaxCount && stats.SecretsStat.CurrentCount>=stats.SecretsStat.MaxCount && stats.QuantityStat.CurrentCount>=stats.QuantityStat.MaxCount && (data.PerfectTimeStat.CompletionTime<=0 || stats.TimeStat.CompletionTime<data.PerfectTimeStat.CompletionTime)) data.PerfectTimeStat=stats.TimeStat;
        data.Load(level);
        var next=LevelManager.NextLevel;if(next!=null && next.State==LevelId.LevelState.LOCKED) next.State=LevelId.LevelState.UNLOCKED;
    }
}
