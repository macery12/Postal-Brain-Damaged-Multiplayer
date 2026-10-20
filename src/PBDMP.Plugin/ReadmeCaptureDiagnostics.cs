using System.Text;
using System.Text.Json;
using Il2CppInterop.Runtime.Attributes;
using Hyperstrange.PBD;
using Hyperstrange.PBD.Contraptions;
using Hyperstrange.PBD.PlayerComponents;
using Hyperstrange.PBD.UI.Components;
using Hyperstrange.PBD.UI.Menus;
using PBDMP.Network;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PBDMP.Plugin;

// Opt-in, visible two-process documentation captures. Commands are consumed on Unity's
// main thread. No seeded saves, fake poses, enemy spawns or normal-install writes.
public sealed partial class MultiplayerBehaviour
{
    private static bool ReadmeCaptureEnabled => Environment.GetEnvironmentVariable("PBDMP_README_CAPTURE") == "1";
    private bool _readmeStarted, _readmeFire;
    private float _readmeStatusAt, _readmeFireAt;
    private int _readmeShots;
    private string _readmeShotPrefix = "";
    private float _readmeShotAt;

    private void RunReadmeCapture()
    {
        if (!_buttonVisibleLogged) return;
        if (_avatarLabel != null && _open) _avatarLabel.gameObject.SetActive(false);
        if (!_readmeStarted)
        {
            _port!.text = Environment.GetEnvironmentVariable("PBDMP_TRAVEL_PORT")!;
            if (TravelRole == "host") { Host(); TravelMark("host-ready"); }
            else { _address!.text = "127.0.0.1"; Join(); }
            _readmeStarted = true;
        }
        if (_lifecycle is { NativeReady: false }) _lifecycle.AutoContinue();
        if (_lifecycle is { LocalTutorialActive: true } && Player.Instance != null)
            Player.Instance.GetComponent<PlayerTutorialComponent>().OnUseDown();
        if (_readmeFire && Time.unscaledTime >= _readmeFireAt && Player.Instance?.CurrentWeapon is { } weapon)
        {
            weapon.PlayAttackAnimation(PlayerWeapon.ANIM_SHOOT); _readmeFireAt = Time.unscaledTime + 1.1f;
        }
        if (_readmeShots > 0 && Time.unscaledTime >= _readmeShotAt)
        {
            ScreenCapture.CaptureScreenshot(Marker(_readmeShotPrefix + "-" + _readmeShots-- + ".png"));
            _readmeShotAt = Time.unscaledTime + .2f;
        }
        if (Time.unscaledTime >= _readmeStatusAt)
        {
            _readmeStatusAt = Time.unscaledTime + 1;
            var p = Player.Instance;
            File.WriteAllText(Marker(TravelRole + "-capture-state.txt"),
                $"ready={_lifecycle?.NativeReady}; released={_lifecycle?.State.Released}; connected={_session?.Snapshot.Connected}; frozen={_lifecycle?.Frozen}; level={LevelManager.CurrentLevel?.name}; pos={p?.FeetPosition}; camera={p?.WorldCamera?.transform.eulerAngles}; health={p?.HitPoints.Current}; weapon={p?.CurrentWeapon?.WeaponId?.name}; survival={_world?.SurvivalPublished ?? _world?.SurvivalView}; saves={_saves?.Status}", Encoding.UTF8);
        }
        var command = Marker(TravelRole + "-command.json");
        if (!File.Exists(command)) return;
        // Rename first, so a producer can never replace a command being parsed.
        var pending = Marker(TravelRole + "-command-running.json");
        File.Move(command, pending, true);
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(pending, Encoding.UTF8));
            CaptureCommand(json.RootElement);
            File.WriteAllText(Marker(TravelRole + "-command-result.txt"), "OK", Encoding.UTF8);
        }
        catch (Exception error)
        {
            File.WriteAllText(Marker(TravelRole + "-command-result.txt"), error.ToString(), Encoding.UTF8);
        }
        finally { File.Delete(pending); }
    }

    [HideFromIl2Cpp]
    private void CaptureCommand(JsonElement cmd)
    {
        var action = cmd.GetProperty("action").GetString();
        string Value(string key) => cmd.GetProperty(key).GetString()!;
        float Number(string key) => cmd.GetProperty(key).GetSingle();
        switch (action)
        {
            case "shot":
                if (_steamSession) throw new InvalidOperationException("Documentation captures require Direct IP.");
                var name = Value("name");
                if (name != Path.GetFileName(name) || !name.EndsWith(".png", StringComparison.Ordinal)) throw new ArgumentException("PNG filename required.");
                ScreenCapture.CaptureScreenshot(Marker(name)); break;
            case "burst":
                _readmeShotPrefix = Path.GetFileNameWithoutExtension(Value("name"));
                _readmeShots = 10; _readmeShotAt = Time.unscaledTime; break;
            case "multiplayer": OpenCoopMenu(); break;
            case "compact-preview":
                // Keep the real native main-menu camera: put the live Continue card
                // below the couch instead of obscuring the Dude in the room shot.
                var preview = _coopMenu!.Menu.transform.Find("Continue preview").GetComponent<RectTransform>();
                preview.localScale = Vector3.one * .65f; preview.anchoredPosition = new Vector2(2950, 620); break;
            case "new": _coopMenu!.NewGame.onClick.Invoke(); break;
            case "start": Group.Find("MENU_LevelSelection").GetComponent<LevelSelectionMenu>().StartLevel(); break;
            case "confirm":
                Group.Find(Value("menu") + "/OverlayImage/Background/BUTTON_Confirm").GetComponent<MainHyperButton>().onClick.Invoke(); break;
            case "load-menu": _coopMenu!.Load.onClick.Invoke(); break;
            case "back": MenuManager.Close(); break;
            case "resume": SetPanel(false); GameManager.DisableMenu(); break;
            case "skip": StoryMenu()?.OnCloseMenu(); break;
            case "restart": GameManager.Load(null); break;
            case "pause": GameManager.PauseGame(); break;
            case "players": _view!.PauseEntry.onClick.Invoke(); break;
            case "title": GameManager.OpenTitle(); break;
            case "load":
                if (_lifecycle?.IsClient != false) throw new InvalidOperationException("P1 loads levels.");
                LevelId? level = null;
                foreach (var candidate in LevelManager.AllLevels)
                    if (candidate.name == Value("level") && SessionLifecycle.Supported(candidate)) { level = candidate; break; }
                if (level == null) throw new ArgumentException("Unknown supported level.");
                _saves!.StartNewCampaign();
                GameManager.LoadLevel(level, true); break;
            case "save": _saves!.RequestSave(Value("slot"), Value("name")); break;
            case "teleport":
                var position = new Vector3(Number("x"), Number("y"), Number("z"));
                var movement = Player.Instance.GetComponent<PlayerMovementComponent>();
                var state = new PlayerMovementComponent.SaveData(movement);
                state._velocity = Vector3.zero; state._velocityToApply = Vector3.zero;
                state._position = position; state._state = PlayerMovementComponent.MovementStateEnum.MID_AIR;
                state._currentStateTimestamp = 0; movement.LoadState(state);
                Player.Instance.Teleport(position, new Vector3(Number("pitch"), Number("yaw"), 0), Vector3.zero); break;
            case "fire": _readmeFire = cmd.GetProperty("enabled").GetBoolean(); break;
            case "click": Group.Find(Value("path")).GetComponent<UnityEngine.UI.Button>().onClick.Invoke(); break;
            case "hide": Group.Find(Value("path")).gameObject.SetActive(false); break;
            case "inspect-ui":
                File.WriteAllLines(Marker(TravelRole + "-ui.txt"), Group.GetComponentsInChildren<Transform>(true).Select(t =>
                {
                    var path = t.name; var parent = t.parent;
                    while (parent != null && parent.Pointer != Group.Pointer) { path = parent.name + "/" + path; parent = parent.parent; }
                    return path + " | " + t.GetComponent<TMPro.TMP_Text>()?.text;
                }), Encoding.UTF8); break;
            case "ready": _world!.RequestSurvivalReady(); break;
            case "pickup":
                var pickup = Object.FindObjectsOfType<Pickup>().First(p => p.GetInstanceID() == cmd.GetProperty("id").GetInt32());
                Player.Instance.Teleport(pickup.transform.position + Vector3.up * .1f, Player.Instance.transform.rotation);
                pickup.Interact(Player.Instance); break;
            case "friend":
                _coopJoin!.TestHosts = new[] { new FriendHost(0, 1, "Friend", new LobbyAdvert(Plugin.Version, Protocol.Version.ToString(), Plugin.LobbyBuild, "LEVEL_AD_Neighborhood", LobbyAdvert.ModeStory, 1)) };
                _coopMenu!.JoinFriend.onClick.Invoke(); break;
            case "inspect":
                var lines = Object.FindObjectsOfType<Npc>().Where(n => n.IsAlive && n.gameObject.activeInHierarchy).Take(1000).Select(n => $"NPC {n.GetInstanceID()} {n.name} {n.Transform.position}")
                    .Concat(Object.FindObjectsOfType<Pickup>().Take(1000).Select(p => $"PICKUP {p.GetInstanceID()} {p.name} {p.transform.position}"));
                File.WriteAllLines(Marker(TravelRole + "-scene.txt"), lines, Encoding.UTF8); break;
            case "finish": TravelMark(TravelRole + "-pass"); break;
            default: throw new ArgumentException("Unknown capture command.");
        }
    }
}
