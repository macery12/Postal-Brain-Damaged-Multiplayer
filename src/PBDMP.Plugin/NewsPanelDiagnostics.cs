using Hyperstrange.PBD;
using Hyperstrange.PBD.UI.Components;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using EventSystem = UnityEngine.EventSystems.EventSystem;

namespace PBDMP.Plugin;

public sealed partial class MultiplayerBehaviour
{
    private int _newsStep;
    private float _newsNext;

    private void RunNewsTest()
    {
        if (_newsStep == 99) return;
        if (Time.unscaledTime > 90) throw new TimeoutException("News panel diagnostic timed out at step " + _newsStep);
        if (Time.unscaledTime < _newsNext) return;
        var view = _view!;
        switch (_newsStep)
        {
            case 0:
                if (!view.News.Visible || Time.unscaledTime < 5) return;
                Canvas.ForceUpdateCanvases();
                AssertNews(view);
                CaptureNews("news-main-menu.png");
                var links = new List<string>();
                var browser = view.News.OpenLink;
                view.News.OpenLink = links.Add;
                try
                {
                    EventSystem.current.SetSelectedGameObject(view.MainEntry.gameObject);
                    view.MainEntry.OnMove(new AxisEventData(EventSystem.current) { moveDir = MoveDirection.Right, moveVector = Vector2.right });
                    SmokeAssert(EventSystem.current.currentSelectedGameObject.Pointer == view.News.ReportButton.gameObject.Pointer, "main-menu right navigation reaches Report a Bug");
                    NativeUiDiagnostics.SubmitController(view.News.ReportButton);
                    view.News.ReportButton.OnMove(new AxisEventData(EventSystem.current) { moveDir = MoveDirection.Down, moveVector = Vector2.down });
                    SmokeAssert(EventSystem.current.currentSelectedGameObject.Pointer == view.News.RepositoryButton.gameObject.Pointer, "news down navigation reaches GitHub");
                    view.News.RepositoryButton.OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
                    SmokeAssert(links.Count == 2 && links[0] == NativeNewsPanel.ReportUrl && links[1] == NativeNewsPanel.RepositoryUrl,
                        "native controller/pointer callbacks open only the selected project links");
                    var fields = new Uri(links[0]).Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2))
                        .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
                    SmokeAssert(fields.Count == 3 && fields["template"] == ReportDraft.Template &&
                        fields["software"].Contains("PBDMP version: " + Plugin.Version) && fields["software"].Contains(Plugin.BuildId[..16]) &&
                        fields["software"].Contains("Unity: ") && fields["software"].Contains("BepInEx core: ") &&
                        fields["session"].Contains("Level: Unavailable") && !fields.ContainsKey("body"),
                        "issue-form URL selects the template and prefills live software with unavailable session context before play");
                    SmokeAssert(links[0].Length < 4000 && !fields["software"].Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase) &&
                        !fields["software"].Contains("GPU:") && !fields["software"].Contains("CPU:") &&
                        !fields["software"].Contains(BepInEx.Paths.GameRootPath, StringComparison.OrdinalIgnoreCase),
                        "prefill is bounded and excludes hardware, username and game path");
                    view.News.RepositoryButton.OnMove(new AxisEventData(EventSystem.current) { moveDir = MoveDirection.Left, moveVector = Vector2.left });
                    SmokeAssert(EventSystem.current.currentSelectedGameObject.Pointer == view.MainEntry.gameObject.Pointer, "news left navigation returns to Multiplayer");
                }
                finally { view.News.OpenLink = browser; }
                view.MainEntry.transform.parent.Find("BUTTON_Settings").GetComponent<MainHyperButton>().onClick.Invoke();
                _newsStep = 1; _newsNext = Time.unscaledTime + 1; break;
            case 1:
                SmokeAssert(!view.News.Visible, "news is hidden behind native Settings");
                MenuManager.Close(); _newsStep = 2; _newsNext = Time.unscaledTime + 1; break;
            case 2:
                SmokeAssert(view.News.Visible, "news returns after Settings closes");
                view.MainEntry.transform.parent.Find("BUTTON_Exit").GetComponent<MainHyperButton>().onClick.Invoke();
                _newsStep = 20; _newsNext = Time.unscaledTime + 1; break;
            case 20:
                SmokeAssert(!view.News.Visible, "news is hidden behind the native exit confirmation prompt");
                MenuManager.Close(); _newsStep = 21; _newsNext = Time.unscaledTime + 1; break;
            case 21:
                SmokeAssert(view.News.Visible, "news returns after dismissing the exit prompt");
                view.MainEntry.onClick.Invoke();
                view.Refresh();
                SmokeAssert(!view.News.Visible, "news is hidden behind Multiplayer");
                view.BackButton.onClick.Invoke(); _newsStep = 3; _newsNext = Time.unscaledTime + 1; break;
            case 3:
                SmokeAssert(view.News.Visible, "news returns after Multiplayer closes");
                MenuManager.OpenLoadingMenu(); _newsStep = 4; _newsNext = Time.unscaledTime + 1; break;
            case 4:
                SmokeAssert(!view.News.Visible, "news is hidden during native loading");
                MenuManager.CloseAll(); MenuManager.OpenMainMenu(); _newsStep = 5; _newsNext = Time.unscaledTime + 1; break;
            case 5:
                SmokeAssert(view.News.Visible, "news returns after native main-menu reentry without duplication");
                AssertNews(view);
                Plugin.Logger.LogInfo("PBDMP NEWS PASS: native art/text, viewport fit, report link privacy, controller/pointer navigation, Settings/prompt/Multiplayer/loading visibility and main-menu reentry.");
                _newsStep = 99; break;
        }
    }

    private static void AssertNews(NativeMultiplayerUI view)
    {
        var news = view.News;
        SmokeAssert(news.Root.GetComponent<Image>().sprite.name == "UI_Panel" && news.ReportButton.LabelText.font.name == "FONT_Clickable" &&
            news.ReportButton.onClick.GetPersistentEventCount() == 0 && news.RepositoryButton.onClick.GetPersistentEventCount() == 0,
            "news reuses native panel/font without inherited actions");
        foreach (var label in news.Root.GetComponentsInChildren<TMP_Text>(true))
        {
            label.ForceMeshUpdate();
            SmokeAssert(!label.isTextOverflowing, "news text fits: " + label.name);
        }
        var viewport = view.Root.GetComponent<RectTransform>();
        var corners = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
        news.Root.GetComponent<RectTransform>().GetWorldCorners(corners);
        var left = float.MaxValue;
        foreach (var corner in corners)
        {
            var local = viewport.InverseTransformPoint(corner);
            SmokeAssert(viewport.rect.Contains(new Vector2(local.x, local.y)), "news panel corner stays in viewport");
            left = Mathf.Min(left, corner.x);
        }
        foreach (var button in view.MainEntry.transform.parent.GetComponentsInChildren<Selectable>(true))
        {
            button.GetComponent<RectTransform>().GetWorldCorners(corners);
            SmokeAssert(corners.All(c => c.x < left), "news leaves native menu clear: " + button.name);
        }
    }

    // Both canvases are rendered together so the captured layout includes the native menu.
    private void CaptureNews(string name)
    {
        var canvases = new[] { MenuManager.MainCanvas, _view!.Root.GetComponent<Canvas>() };
        var states = canvases.Select(c => (Canvas: c, Mode: c.renderMode, Camera: c.worldCamera, Distance: c.planeDistance)).ToArray();
        var nodes = canvases.SelectMany(c => c.GetComponentsInChildren<Transform>(true)).ToArray();
        var layers = nodes.Select(t => t.gameObject.layer).ToArray();
        var cameraObject = new GameObject("PBDMP news diagnostic camera");
        var target = new RenderTexture(Screen.width, Screen.height, 24);
        var pixels = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            foreach (var node in nodes) node.gameObject.layer = 31;
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false; camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.04f, .04f, .04f); camera.targetTexture = target;
            foreach (var canvas in canvases) { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1; }
            Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
            pixels.ReadPixels(new UnityEngine.Rect(0, 0, Screen.width, Screen.height), 0, 0); pixels.Apply();
            var output = Environment.GetEnvironmentVariable("PBDMP_NEWS_OUTPUT")!;
            Directory.CreateDirectory(output); File.WriteAllBytes(Path.Combine(output, name), ImageConversion.EncodeToPNG(pixels));
        }
        finally
        {
            foreach (var state in states) { state.Canvas.renderMode = state.Mode; state.Canvas.worldCamera = state.Camera; state.Canvas.planeDistance = state.Distance; }
            for (var i = 0; i < nodes.Length; i++) nodes[i].gameObject.layer = layers[i];
            RenderTexture.active = previous; target.Release(); Object.Destroy(target); Object.Destroy(pixels); Object.Destroy(cameraObject);
        }
    }
}
