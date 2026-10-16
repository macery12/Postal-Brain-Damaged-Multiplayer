using HarmonyLib;
using Hyperstrange.Base;
using Hyperstrange.PBD;
using Hyperstrange.PBD.UI.Menus;
using PBDMP.Network;
using UnityEngine;

namespace PBDMP.Plugin;

internal sealed partial class SessionLifecycle
{
    private CutsceneMenu? _hostStory,_clientStory;
    private VideoData? _clientStoryAsset;
    private bool _storyAdmissionPending,_closingStory,_endedCredits;
    private float _storyOpenAt;
    private VideoData? _expectedStory;
    internal void ExpectStory(VideoData video)=>_expectedStory=video;
    private bool NativeStoryActive=>_hostStory!=null && _hostStory.gameObject.activeInHierarchy && !_hostStory._closing;
    internal bool StoryActive=>_expectedStory!=null || NativeStoryActive;
    private TravelState AdvertisedState=>_host.State with
    {
        HostMenuPaused=_hostMenuPaused && _host.State.Paused && !StoryActive && !LocalTutorialActive,
        StoryVideo=_expectedStory?.name??(NativeStoryActive?_hostStory!._currentVideo?.name??"":""),
        StoryTime=NativeStoryActive && _hostStory!._videoPlayer.isPrepared?(float)_hostStory._videoPlayer.time:0,
        StoryWaiting=StoryActive && (!_connected || _storyAdmissionPending),
        StoryEnding=_endedCredits || StoryActive && LevelManager.CurrentLevel==null
    };
    internal void StoryOpened(CutsceneMenu menu)
    {
        if(IsClient) {_clientStory=menu;return;}
        _expectedStory=null;_hostStory=menu;_session.PublishTravel(AdvertisedState);
    }
    internal void StoryClosed(CutsceneMenu menu)
    {if(_hostStory?.Pointer==menu.Pointer) _hostStory=null;if(_clientStory?.Pointer==menu.Pointer) _clientStory=null;}
    internal void StoryFinished()
    {
        if(IsClient || LevelManager.CurrentLevel!=null) return;
        BeginHost(TravelAction.Menu,null);_endedCredits=true;
    }
    private void AdmitPeer()
    {
        var level=_host.State.Action==TravelAction.Menu?null:FindLevel(_host.State.Level);
        _host.Begin(level==null?TravelAction.Menu:TravelAction.Load,level?.ID??"",Difficulty(),level!=null);
        if(level==null) _host.SetReady();
        else if(_nativeReady && Player.Instance.IsAlive) _host.SetReady(_player.CaptureJoin());
        _deadline=Time.unscaledTime+120;
    }
    private bool FollowStory(TravelState state)
    {
        if(state.StoryVideo.Length==0)
        {
            if(_clientStory!=null)
            {
                _barrierInput.Release();_closingStory=true;
                try {Authorized(MenuManager.CloseAll);}finally {_closingStory=false;}
                _clientStory=null;_clientStoryAsset=null;
            }
            return false;
        }
        if(state.Epoch!=_clientEpoch)
        {
            // A title-menu readiness flag is not readiness for the deferred map.
            // Do not construct a world catalogue or personal record from menu objects.
            _nativeReady=false;PersonalReady=false;WorldReady=false;
            _joiningApplied=false;_gateArmed=false;_loadingContinued=false;
        }
        _clientState=state;_deadline=Time.unscaledTime+120;
        _session.PublishReady(new(state.Epoch,false));
        Status=state.StoryWaiting?"Waiting for story sequence.":"Story sequence; P1 controls skipping.";
        if(state.StoryWaiting)
        {
            var waitingMenu=UnityEngine.Object.FindObjectOfType<MenuManager>()?._cutsceneMenu;
            if(waitingMenu?.gameObject.activeInHierarchy==true) waitingMenu.gameObject.SetActive(false);
            _clientStory=null;_clientStoryAsset=null;return true;
        }
        if((_clientStoryAsset?.name!=state.StoryVideo || _clientStory==null || !_clientStory.gameObject.activeInHierarchy || _clientStory._closing) && Time.unscaledTime>=_storyOpenAt)
        {
            _clientStoryAsset=Resources.FindObjectsOfTypeAll<VideoData>().FirstOrDefault(v=>v.name==state.StoryVideo)??
                throw new InvalidDataException("Host selected an unavailable story video.");
            _storyOpenAt=Time.unscaledTime+1f;
            Plugin.Logger.LogInfo("P2 opening host story video: "+state.StoryVideo);
            var manager=UnityEngine.Object.FindObjectOfType<MenuManager>();
            Plugin.Logger.LogInfo("Story menu native state: "+manager?._cutsceneMenu?.GetIl2CppType().FullName+" / "+manager?._cutsceneMenu?.gameObject.activeSelf+" / "+manager?._cutsceneMenu?.gameObject.activeInHierarchy+" / menus="+manager?._openMenus.Count+" / scale="+Time.timeScale);
            if(manager==null) throw new InvalidOperationException("Native menu manager is unavailable.");
            var nativeMenu=manager._cutsceneMenu;
            Authorized(()=>{if(!manager._openMenus.Contains(nativeMenu)) MenuManager.Open(nativeMenu);if(!nativeMenu.gameObject.activeInHierarchy) nativeMenu.Enable();});
        }
        if(_clientStory?._videoPlayer is {} video && video.isPrepared)
        {
            if(Math.Abs(video.time-state.StoryTime)>.5)
            {video.time=state.StoryTime;_clientStory._audioInstance?.SetTimelinePosition((int)(state.StoryTime*1000));}
            _clientStory._skipProgress=0;_clientStory.UpdateSubtitles();_clientStory.UpdateSkipIcon();
        }
        return true;
    }
    internal bool PlayClientStory(CutsceneMenu menu)
    {
        if(!IsClient || _clientStoryAsset==null) return false;
        menu._closing=false;menu._skipProgress=0;menu._currentVideo=_clientStoryAsset;
        QualitySettings.vSyncCount=1;
        menu._videoPlayer.clip=_clientStoryAsset.VideoClip;
        menu._audioInstance=new FMODInstance(_clientStoryAsset.AudioClip);menu._videoPlayer.Prepare();return true;
    }
    internal bool PermitStoryClose=>!IsClient || _closingStory;
}

internal static class SessionStoryPatches
{
    internal static void Install(Harmony h)
    {
        h.Patch(AccessTools.Method(typeof(CutsceneMenu),"OnEnable"),postfix:new(typeof(SessionStoryPatches),nameof(Opened)));
        h.Patch(AccessTools.Method(typeof(CutsceneMenu),"OnDisable"),postfix:new(typeof(SessionStoryPatches),nameof(Closed)));
        h.Patch(AccessTools.Method(typeof(CutsceneMenu),"PlayVideo"),prefix:new(typeof(SessionStoryPatches),nameof(Play)));
        h.Patch(AccessTools.Method(typeof(CutsceneMenu),"OnUpdate"),prefix:new(typeof(SessionStoryPatches),nameof(Update)));
        h.Patch(AccessTools.Method(typeof(CutsceneMenu),"OnCloseMenu"),prefix:new(typeof(SessionStoryPatches),nameof(Close)),postfix:new(typeof(SessionStoryPatches),nameof(Finished)));
    }
    private static void Opened(CutsceneMenu __instance)=>SessionLifecycle.Current?.StoryOpened(__instance);
    private static void Closed(CutsceneMenu __instance)=>SessionLifecycle.Current?.StoryClosed(__instance);
    private static bool Play(CutsceneMenu __instance)=>SessionLifecycle.Current?.PlayClientStory(__instance)!=true;
    private static bool Update()=>SessionLifecycle.Current?.IsClient!=true;
    private static bool Close()=>SessionLifecycle.Current?.PermitStoryClose!=false;
    private static void Finished()=>SessionLifecycle.Current?.StoryFinished();
}
