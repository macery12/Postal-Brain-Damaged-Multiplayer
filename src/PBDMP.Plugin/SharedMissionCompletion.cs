using System.Globalization;
using Hyperstrange.PBD;
using PBDMP.Network;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    private WorldFrame? _missionCompletion;
    private bool _clientMissionCompleted;
    internal LevelStats? ClientLevelStats {get;private set;}
    internal void PublishMissionCompletion()
    {
        if(Client || !Active || _missionCompletion!=null) return;
        var stats=LevelController.GetLevelStats();
        _missionCompletion=new(WorldKind.MissionState,_epoch,1,Flags:1024,
            Revision:(stats.KillsStat.Finished?1u:0)|(stats.SecretsStat.Finished?2u:0)|(stats.QuantityStat.Finished?4u:0),
            Values:new[]{(float)stats.KillsStat.CurrentCount,stats.KillsStat.MaxCount,stats.SecretsStat.CurrentCount,stats.SecretsStat.MaxCount,
                stats.QuantityStat.CurrentCount,stats.QuantityStat.MaxCount},
            Data:new[]{stats.TimeStat.CompletionTime.ToString(CultureInfo.InvariantCulture)});
        Send(_missionCompletion);
    }
    private void ReceiveMissionCompletion(WorldFrame frame)
    {
        if(frame.Values is not {Length:6} v || frame.Revision>7 || v.Any(n=>n<0 || n!=MathF.Floor(n)) ||
            frame.Data is not {Length:1} data || !int.TryParse(data[0],NumberStyles.None,CultureInfo.InvariantCulture,out var milliseconds))
            throw new InvalidDataException("Invalid shared level results.");
        if(_missionCompletion!=null) return;
        ClientLevelStats=new LevelStats{
            KillsStat=new QuantityStat((int)v[0],(int)v[1],(frame.Revision&1)!=0),
            SecretsStat=new QuantityStat((int)v[2],(int)v[3],(frame.Revision&2)!=0),
            QuantityStat=new QuantityStat((int)v[4],(int)v[5],(frame.Revision&4)!=0),TimeStat=new TimeStat(milliseconds)};
        _missionCompletion=frame;
    }
    private void TickMissionCompletion()
    {
        if(_missionCompletion==null || _clientMissionCompleted || !_life.WorldReady || !_life.State.Released || !Player.Instance.IsAlive) return;
        _clientMissionCompleted=true;_finishingMission=true;
        try {LevelController._instance.EndLevel(LevelController.EndLevelType.FINISH);}
        finally {_finishingMission=false;}
    }
    private void ClearMissionCompletion()
    {_missionCompletion=null;_clientMissionCompleted=false;ClientLevelStats=null;}
}
