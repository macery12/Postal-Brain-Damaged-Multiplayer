using UnityEngine;
using Hyperstrange.PBD;
using Object = UnityEngine.Object;

namespace PBDMP.Plugin;

internal static class VisualDiagnostics
{
    // Uses the running game's rendering pipeline. An enabled Renderer is not proof that pixels were drawn.
    internal static void AssertRendered(GameObject visual, string name, string output)
    {
        var cameraObject = new GameObject("PBDMP render probe");
        var lightObject = new GameObject("PBDMP render probe light");
        var target = new RenderTexture(256, 256, 24);
        var image = new Texture2D(256, 256, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        var transforms = visual.GetComponentsInChildren<Transform>(true);
        var layers = transforms.Select(t => t.gameObject.layer).ToArray();
        try
        {
            foreach (var transform in transforms) transform.gameObject.layer = 31;
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.1f, 0.15f, 1f);
            camera.cullingMask = 1 << 31;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 50f;
            camera.fieldOfView = 35f;
            camera.targetTexture = target;
            var renderers = visual.GetComponentsInChildren<Renderer>(true)
                .Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            if (renderers.Length == 0) throw new InvalidOperationException("No visual renderers.");
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            camera.transform.position = bounds.center + new Vector3(1.5f, 0.1f, 3.5f);
            camera.transform.LookAt(bounds.center);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 2f;
            light.cullingMask = camera.cullingMask;
            light.transform.rotation = Quaternion.Euler(25f, 180f, 0f);
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new UnityEngine.Rect(0, 0, 256, 256), 0, 0);
            image.Apply();
            File.WriteAllBytes(Path.Combine(output, name + ".png"), ImageConversion.EncodeToPNG(image));
            var pixels = image.GetPixels32();
            var corner = pixels[0];
            var changed = 0;
            foreach (var pixel in pixels)
                if (Math.Abs(pixel.r - corner.r) + Math.Abs(pixel.g - corner.g) + Math.Abs(pixel.b - corner.b) > 30) changed++;
            var mesh = visual.GetComponent<MeshFilter>();
            Plugin.Logger.LogInfo($"VISUAL PROBE {name}: pixels={changed}; mesh={(mesh == null || mesh.sharedMesh == null ? "none" : mesh.sharedMesh.vertexCount.ToString())}; pipeline={UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline?.name}; shader={renderers[0].sharedMaterial?.shader?.name}");
            if (changed < 30) throw new InvalidOperationException($"{name} drew no visible pixels through the game's rendering pipeline.");
        }
        finally
        {
            for (var i = 0; i < transforms.Length; i++) transforms[i].gameObject.layer = layers[i];
            RenderTexture.active = previous;
            target.Release(); Object.Destroy(target); Object.Destroy(image);
            Object.Destroy(cameraObject); Object.Destroy(lightObject);
        }
    }
}
