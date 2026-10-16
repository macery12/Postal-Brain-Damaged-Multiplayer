using Hyperstrange.PBD;
using Hyperstrange.PBD.PlayerComponents;

namespace PBDMP.Plugin;

internal sealed partial class SessionLifecycle
{
    private bool _tutorialPause, _pauseBeforeTutorial;
    internal bool LocalTutorialActive => Player.Instance != null &&
        Player.Instance.GetComponent<PlayerTutorialComponent>() is {} tutorial && tutorial._isTutorialShown;

    internal void BeginTutorial()
    {
        if (IsClient || _disposed || _tutorialPause) return;
        _pauseBeforeTutorial=_hostPaused;_tutorialPause=true;_hostPaused=true;
    }

    internal void EndTutorial()
    {
        if (!IsClient && _tutorialPause)
        {_tutorialPause=false;_hostPaused=_pauseBeforeTutorial;_host.SetPaused(_hostPaused || PersistenceHold);}
        // Native tutorial input uses CancelInput to mask movement while leaving
        // the noncancelable Use action enabled. Reapply any other owned hold.
        if (!_disposed) ApplyTime();
    }
}
