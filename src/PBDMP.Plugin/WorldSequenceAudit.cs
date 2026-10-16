using Hyperstrange.PBD.Contraptions;
using UnityEngine;
using UnityEngine.Timeline;

namespace PBDMP.Plugin;

internal sealed partial class WorldCatalogue
{
    internal readonly List<string> SequenceAudit=new();
    private void AuditSequence(TimelineSequence sequence)
    {
        var director=sequence._playableDirector;
        if(director?.playableAsset?.TryCast<TimelineAsset>() is not {} timeline) return;
        var seen=new HashSet<IntPtr>();var pending=new Queue<TrackAsset>();
        foreach(var track in timeline.flattenedTracks.ToArray()) pending.Enqueue(track);
        while(pending.Count>0)
        {
            var track=pending.Dequeue();if(track==null || !seen.Add(track.Pointer)) continue;
            if(seen.Count>4096) throw new InvalidOperationException("Timeline track catalogue exceeds bound.");
            var binding=director.GetGenericBinding(track);var component=binding?.TryCast<Component>();var obj=binding?.TryCast<GameObject>();
            SequenceAudit.Add($"{DiagnosticPath(sequence.transform)}\t{timeline.name}\t{track.GetIl2CppType().Name}\t{track.name}\t{binding?.name}\t{binding?.GetIl2CppType().FullName}");
            if(component?.TryCast<SignalReceiver>() is {} receiver)
            {
                var receiverKey=Add(receiver);MissionKeys.Add(receiverKey);EncounterKeys.Add(receiverKey);
                if(receiver.Count()>256) throw new InvalidOperationException("Timeline receiver exceeds bounded reactions.");
                for(var i=0;i<receiver.Count();i++)
                {
                    var reaction=receiver.GetReactionAtIndex(i);if(reaction==null) continue;
                    for(var j=0;j<reaction.GetPersistentEventCount();j++)
                        SequenceAudit.Add($"signal\t{DiagnosticPath(receiver.transform)}\t{receiver.GetSignalAssetAtIndex(i)?.name}\t{reaction.GetPersistentTarget(j)?.name}\t{reaction.GetPersistentMethodName(j)}");
                }
            }
            if(component!=null && SafeMotion(component.transform)) RegisterUrineSurface(component.transform);
            if(obj!=null && SafeMotion(obj.transform)) RegisterUrineSurface(obj.transform);
            foreach(var clip in track.clips.ToArray())
            {
                SequenceAudit.Add($"clip\t{track.name}\t{clip.start}\t{clip.duration}\t{clip.asset?.name}\t{clip.asset?.GetIl2CppType().Name}");
                if(clip.asset?.TryCast<ControlPlayableAsset>() is {} control)
                    SequenceAudit.Add($"control\t{track.name}\t{control.sourceGameObject.Resolve(director.Cast<UnityEngine.IExposedPropertyTable>())?.name}\t{control.prefabGameObject?.name}");
            }

        }
    }
}
