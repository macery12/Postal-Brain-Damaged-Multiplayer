using Hyperstrange.PBD;
using PBDMP.Network;

namespace PBDMP.Plugin;

internal sealed partial class SessionLifecycle
{
    private bool _hostPauseMenuOwned,_observedHostMenuPause;
    internal bool HostPauseNotice=>IsClient && _nativeReady && State.Action!=TravelAction.Menu && State.Released && State.HostMenuPaused && !LocalTutorialActive && !StoryActive;
    private void UpdateHostPauseMenu()
    {
        if(!IsClient) return;
        if(!HostPauseNotice) {if(_observedHostMenuPause) ReleaseHostPauseMenu();return;}
        if(_observedHostMenuPause) return;
        _observedHostMenuPause=true;
        // Keep P2-owned menus and dialogs. Only an empty native menu stack is
        // ours to open and automatically close when the host resumes.
        var manager=UnityEngine.Object.FindObjectOfType<MenuManager>();
        if(_menuInput.Active || manager==null || manager._openMenus.Count>0) return;
        _barrierInput.Release();_menuInput.Set(true);MenuManager.OpenPauseMenu();_hostPauseMenuOwned=true;
    }
    private void ReleaseHostPauseMenu()
    {
        if(_hostPauseMenuOwned)
        {_hostPauseMenuOwned=false;_menuInput.Release();MenuManager.CloseAll();}
        _observedHostMenuPause=false;
    }
}
