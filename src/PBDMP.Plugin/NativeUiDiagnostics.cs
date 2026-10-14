using System.Text.Json;
using Hyperstrange.PBD;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using EventSystem = UnityEngine.EventSystems.EventSystem;
using UnityEngine.EventSystems;

namespace PBDMP.Plugin;

// Opt-in research capture of the game's existing canvas; never invokes its menu actions.
internal static class NativeUiDiagnostics
{
    internal static void SubmitController(Hyperstrange.PBD.UI.Components.MainHyperButton button)
    {
        var manager = InputManager.Instance;
        var previous = manager._joystickEnabled;
        try { manager._joystickEnabled = true; button.OnSubmit(new BaseEventData(EventSystem.current)); }
        finally { manager._joystickEnabled = previous; }
    }

    internal static void AssertControls(NativeMultiplayerUI view)
    {
        var entry = view.MainEntry;
        var parent = entry.transform.parent;
        if (entry.transform.GetSiblingIndex() != parent.Find("BUTTON_Horde").GetSiblingIndex() + 1 ||
            entry.onClick.GetPersistentEventCount() != 0 || view.PauseEntry.onClick.GetPersistentEventCount() != 0)
            throw new InvalidOperationException("Native entries must be correctly placed and contain no original game actions.");
        var buttons = new[] { view.HostButton, view.JoinButton, view.DisconnectButton, view.BackButton };
        if (buttons.Any(b => b.onClick.GetPersistentEventCount() != 0) ||
            view.Address.onValueChanged.GetPersistentEventCount() != 0 || view.Address.onSubmit.GetPersistentEventCount() != 0)
            throw new InvalidOperationException("Cloned controls must not retain native prompt actions.");
        if (entry.LabelText.font.name != "FONT_Clickable" || view.Address.textComponent.font.name != "FONT_InputText" ||
            view.Panel.transform.Find("Multiplayer panel").GetComponent<Image>().sprite.name != "UI_Panel")
            throw new InvalidOperationException("UI must reuse the original native fonts and panel art.");
        entry.LabelText.SetText(); view.PauseEntry.LabelText.SetText();
        if (entry.LabelText.text != "Multiplayer" || view.PauseEntry.LabelText.text != "Multiplayer")
            throw new InvalidOperationException("Language refresh must not overwrite multiplayer labels.");
        var down = parent.Find("BUTTON_Horde").GetComponent<Selectable>().navigation.selectOnDown;
        if (down == null || down.Pointer != entry.Pointer)
            throw new InvalidOperationException("Native navigation must include the new menu entry.");
        if (parent.GetComponentsInChildren<Transform>(true).Count(t => t.name == "BUTTON_PBDMP_Multiplayer") != 1)
            throw new InvalidOperationException("Native menu entry must be unique.");
        entry.OnPointerEnter(new PointerEventData(EventSystem.current));
        entry.OnPointerExit(new PointerEventData(EventSystem.current));
        Plugin.Logger.LogInfo("SMOKE PASS: native UI assets, safe actions, entry order/navigation, persistent labels, uniqueness and hover callbacks.");
    }

    internal static void AssertModal(NativeMultiplayerUI view, bool open)
    {
        var manager = MenuManager.Instance;
        var group = MenuManager.MainCanvas.transform.Find("MainCanvasGroup").GetComponent<CanvasGroup>();
        if (open && (manager.enabled || group.interactable || group.blocksRaycasts || !view.Panel.activeSelf))
            throw new InvalidOperationException("Modal must block native back/navigation and underlying pointer controls.");
        if (!open && (!manager.enabled || !group.interactable || !group.blocksRaycasts || view.Panel.activeSelf))
            throw new InvalidOperationException("Closing modal must restore native menu input and raycasts.");
        Plugin.Logger.LogInfo("SMOKE PASS: native menu input ownership " + (open ? "blocked" : "restored"));
    }

    internal static void AssertViewport(NativeMultiplayerUI view)
    {
        Canvas.ForceUpdateCanvases();
        var root = view.Root.GetComponent<RectTransform>();
        var overlay = view.Panel.GetComponent<RectTransform>();
        var box = view.Panel.transform.Find("Multiplayer panel").GetComponent<RectTransform>();
        if (Math.Abs(overlay.rect.width - root.rect.width) > 1 || Math.Abs(overlay.rect.height - root.rect.height) > 1 ||
            Vector3.Distance(root.TransformPoint(root.rect.center), box.TransformPoint(box.rect.center)) > 1)
            throw new InvalidOperationException("Overlay must cover the viewport and dialog must stay centered.");
        var corners = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
        box.GetWorldCorners(corners);
        foreach (var corner in corners)
        {
            var local = root.InverseTransformPoint(corner);
            if (!root.rect.Contains(new Vector2(local.x, local.y))) throw new InvalidOperationException("Dialog is clipped by the viewport.");
        }
        Plugin.Logger.LogInfo($"SMOKE PASS: centered full-viewport native dialog at {Screen.width}x{Screen.height}.");
    }

    internal static void Capture(string output)
    {
        var canvas = MenuManager.MainCanvas;
        var nodes = new List<object>();
        foreach (var transform in canvas.GetComponentsInChildren<Transform>(true))
        {
            var text = transform.GetComponent<TMP_Text>();
            var image = transform.GetComponent<Image>();
            var rect = transform.TryCast<RectTransform>();
            var button = transform.GetComponent<Button>();
            var callbacks = new List<string>();
            if (button != null)
                for (var i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                    callbacks.Add(button.onClick.GetPersistentMethodName(i));
            nodes.Add(new {
                path = PathTo(transform, canvas.transform), active = transform.gameObject.activeInHierarchy,
                components = transform.GetComponents<Component>().Select(c => c.GetIl2CppType().FullName).ToArray(),
                label = text?.text, font = text?.font?.name, material = text?.fontSharedMaterial?.name,
                fontSize = text != null ? text.fontSize : 0,
                sprite = image?.sprite?.name, texture = image?.sprite?.texture?.name,
                width = rect != null ? rect.rect.width : 0, height = rect != null ? rect.rect.height : 0,
                x = rect != null ? rect.anchoredPosition.x : 0, y = rect != null ? rect.anchoredPosition.y : 0,
                callbacks
            });
        }
        File.WriteAllText(Path.Combine(output, "native-ui.json"), JsonSerializer.Serialize(nodes, new JsonSerializerOptions { WriteIndented = true }));
        var cameraObject = new GameObject("PBDMP native UI research camera");
        var target = new RenderTexture(1280, 720, 24);
        var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        var previousMode = canvas.renderMode;
        var previousCamera = canvas.worldCamera;
        var previousTarget = RenderTexture.active;
        var transforms = canvas.GetComponentsInChildren<Transform>(true);
        var layers = transforms.Select(t => t.gameObject.layer).ToArray();
        try
        {
            foreach (var transform in transforms) transform.gameObject.layer = 31;
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false; camera.cullingMask = 1 << 31;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.04f, 0.04f, 0.04f);
            camera.targetTexture = target;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
            pixels.ReadPixels(new UnityEngine.Rect(0, 0, 1280, 720), 0, 0); pixels.Apply();
            File.WriteAllBytes(Path.Combine(output, "native-main-menu.png"), ImageConversion.EncodeToPNG(pixels));
        }
        finally
        {
            canvas.renderMode = previousMode; canvas.worldCamera = previousCamera;
            for (var i = 0; i < transforms.Length; i++) transforms[i].gameObject.layer = layers[i];
            RenderTexture.active = previousTarget; target.Release();
            Object.Destroy(target); Object.Destroy(pixels); Object.Destroy(cameraObject);
        }
        Plugin.Logger.LogInfo("UI RESEARCH: native menu canvas inventory and isolated render captured.");
    }

    private static string PathTo(Transform node, Transform root)
    {
        var path = node.name;
        while (node.Pointer != root.Pointer && node.parent != null) { node = node.parent; path = node.name + "/" + path; }
        return path;
    }
}
