using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using DG.Tweening;

namespace PBDMP.Plugin;

internal sealed partial class SharedWorld
{
    internal void RetireTriggerHint(Trigger trigger)
    {
        if(!Active || trigger._enabled || !OwnsMission(trigger) && !OwnsUrineConsequence(trigger) || Player.Instance==null) return;
        foreach(var hint in trigger.GetComponents<PlayerTriggerNotificationComponent>())
        {
            if(hint._exitTween is {active:true} tween) TweenExtensions.Kill(tween,false);
            hint._playerIn=false;
            // Disable can bypass native OnPlayerExit for one-shot triggers.
            // Retire only the notification owned by this completed trigger.
            if(hint._notification!=null) Player.Instance.HideNotification(hint._notification);
        }
    }
}
