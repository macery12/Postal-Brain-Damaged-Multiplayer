using Hyperstrange.Base;
using Hyperstrange.PBD;
using Hyperstrange.PBD.UI.Menus;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PBDMP.Plugin;

public sealed partial class MultiplayerBehaviour
{
    private int _storyTestStep;
    private float _storyTestAt,_storyVideoAt;
    private CutsceneMenu? StoryMenu()=>Object.FindObjectOfType<CutsceneMenu>() is {} menu && menu.gameObject.activeInHierarchy && !menu._closing?menu:null;
    private void RunStoryTest()
    {
        if(_storyTestStep==99) return;
        File.WriteAllText(Marker(TravelRole+"-story-state.txt"),$"step={_storyTestStep}, epoch={_lifecycle?.Epoch}, status={_lifecycle?.Status}, video={StoryMenu()?._currentVideo?.name}, time={StoryMenu()?._videoPlayer.time}");
        File.WriteAllText(Marker(TravelRole+"-story-menus.txt"),string.Join("\n",Resources.FindObjectsOfTypeAll<CutsceneMenu>().Select(m=>$"{m.name} scene={m.gameObject.scene.name} self={m.gameObject.activeSelf} hierarchy={m.gameObject.activeInHierarchy} closing={m._closing} video={m._currentVideo?.name} prepared={m._videoPlayer?.isPrepared}")));
        if(_storyTestStep==0)
        {
            _port!.text=Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!;
            if(TravelRole=="host") {Host();TravelMark("host-ready");}else {_address!.text="127.0.0.1";Join();}
            _storyTestStep=1;return;
        }
        if(_lifecycle is {NativeReady:false}) _lifecycle.AutoContinue();
        if(TravelRole=="host") StoryHostTest();else StoryClientTest();
    }
    private void StoryHostTest()
    {
        switch(_storyTestStep)
        {
            case 1 when Marked("story-client-menu"):
                GameManager.LoadLevel(TestLevel(),false);_storyTestStep=2;break;
            case 2 when StoryMenu() is {} menu && menu._videoPlayer.isPrepared && Marked("story-client-video"):
                TravelCheck(_session!.Snapshot.Connected,"both admitted players enter the native story video");
                TravelMark("story-test-skip");_storyTestStep=3;break;
            case 3 when Marked("story-client-no-skip"):
                TravelCheck(StoryMenu()!=null,"P2 skip cannot close P1's story");StoryMenu()!.OnCloseMenu();_storyTestStep=4;break;
            case 4 when _lifecycle!.NativeReady && _lifecycle.State.Released && Marked("story-client-loaded"):
                QuietNpcs();SharedWorldPatches.DiagnosticFreeze=true;
                TravelCheck(InputManager.Instance.CurrentInputState==InputManager.InputState.GAMEPLAY,"P1 releases story/load input gates");
                GameManager.OpenTitle();_storyTestStep=40;break;
            case 40 when !SessionManager.SessionActive && _lifecycle!.State.Released:
                _session!.DropPeer("Story admission regression reconnect");_storyTestStep=41;break;
            case 41 when Marked("story-client-left") && !_session!.Snapshot.Connected:
                GameManager.LoadLevel(TestLevel(),false);_storyTestStep=5;break;
            case 5 when StoryMenu()?._videoPlayer.isPrepared==true:
                _storyVideoAt=(float)StoryMenu()!._videoPlayer.time;_storyTestAt=Time.unscaledTime;TravelMark("story-solo-video");_storyTestStep=6;break;
            case 6 when Marked("story-client-waiting") && Time.unscaledTime>_storyTestAt+1:
                TravelCheck(StoryMenu()!=null && StoryMenu()!._videoPlayer.time>_storyVideoAt+.5,"late join waits without freezing P1's video");
                StoryMenu()!.OnCloseMenu();_storyTestStep=7;break;
            case 7 when _lifecycle!.NativeReady && _lifecycle.State.Released && Marked("story-client-rejoined"):
                TravelCheck(_session!.Snapshot.Connected,"waiting guest joins the current campaign after the story");
                if(_saves!.Busy) return;
                GameManager.EndGame();_storyTestStep=8;break;
            case 8 when StoryMenu()?._videoPlayer.isPrepared==true && Marked("story-client-ending"):
                StoryMenu()!.OnCloseMenu();_storyTestStep=9;break;
            case 9 when Marked("story-client-credits"):
                TravelCheck(_session!.Snapshot.Connected,"host-controlled ending reaches both credits screens without disconnection");
                ResetSession();SharedWorldPatches.DiagnosticFreeze=false;TravelMark("host-pass");_storyTestStep=99;break;
        }
    }
    private void StoryClientTest()
    {
        switch(_storyTestStep)
        {
            case 1 when _session!.Snapshot.Connected && _lifecycle?.State.Action==PBDMP.Network.TravelAction.Menu:
                TravelMark("story-client-menu");_storyTestStep=2;break;
            case 2 when StoryMenu()?._videoPlayer.isPrepared==true:
                TravelMark("story-client-video");_storyTestStep=3;break;
            case 3 when Marked("story-test-skip"):
                var menu=StoryMenu()!;menu.OnCloseMenu();menu.OnUpdate(3);
                TravelCheck(!menu._closing && menu.gameObject.activeInHierarchy,"P2 cannot independently skip the shared video");
                TravelMark("story-client-no-skip");_storyTestStep=4;break;
            case 4 when _lifecycle!.NativeReady && _lifecycle.State.Released && StoryMenu()==null:
                TravelCheck(InputManager.Instance.CurrentInputState==InputManager.InputState.GAMEPLAY,"P2 restores native gameplay input after P1 skips");
                TravelMark("story-client-loaded");_storyTestStep=5;break;
            case 5 when _session==null && SessionLifecycle.Current==null:
                TravelMark("story-client-left");_storyTestStep=6;_storyTestAt=Time.unscaledTime;break;
            case 6 when SessionLifecycle.Current==null && !SessionManager.SessionActive && Marked("story-solo-video") && Time.unscaledTime>_storyTestAt+1:
                _address!.text="127.0.0.1";Join();_storyTestStep=7;break;
            case 7 when _session!.Snapshot.Connected && _lifecycle?.Status=="Waiting for story sequence.":
                TravelCheck(StoryMenu()==null,"late-joining P2 waits without replaying an already-started story");
                TravelMark("story-client-waiting");_storyTestStep=8;break;
            case 8 when _lifecycle!.NativeReady && _lifecycle.State.Released && _lifecycle.PersonalReady:
                TravelCheck(InputManager.Instance.CurrentInputState==InputManager.InputState.GAMEPLAY,"P2 joins with working controls after story admission");
                TravelMark("story-client-rejoined");_storyTestStep=9;break;
            case 9 when StoryMenu()?._videoPlayer.isPrepared==true:
                TravelMark("story-client-ending");_storyTestStep=10;break;
            case 10 when _lifecycle?.State.Action==PBDMP.Network.TravelAction.Menu && _lifecycle.State.StoryEnding && StoryMenu()==null:
                TravelMark("story-client-credits");_storyTestStep=11;break;
            case 11 when SessionLifecycle.Current==null && !SessionManager.SessionActive:
                TravelMark("client-pass");_storyTestStep=99;break;
        }
    }
}
