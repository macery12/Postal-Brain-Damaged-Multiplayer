using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PBDMP.Plugin;

/// <summary>Construct visual-only graphs. Never Instantiate an NPC, player, weapon or pickup prefab.</summary>
internal static class VisualClone
{
    internal static GameObject SkinnedBody(Transform source, params string[] rendererNames)
    {
        var renderers = source.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Where(r => rendererNames.Contains(r.name)).ToArray();
        if (renderers.Length == 0) throw new InvalidOperationException("No requested body renderers.");
        var required = new HashSet<IntPtr>();
        void Include(Transform transform)
        {
            for (var current = transform; current != null; current = current.parent)
            {
                required.Add(current.Pointer);
                if (current.Pointer == source.Pointer) return;
            }
            throw new InvalidOperationException("Rig reference outside body source.");
        }
        foreach (var renderer in renderers)
        {
            Include(renderer.transform);
            if (renderer.rootBone != null) Include(renderer.rootBone);
            foreach (var bone in renderer.bones) if (bone != null) Include(bone);
        }
        var map = new Dictionary<IntPtr, Transform>();
        var copy = CopyTransforms(source, null, required, map);
        try
        {
            copy.localPosition = Vector3.zero; copy.localRotation = Quaternion.identity; copy.localScale = Vector3.one;
            foreach (var renderer in renderers)
            {
                var destination = map[renderer.transform.Pointer].gameObject.AddComponent<SkinnedMeshRenderer>();
                destination.sharedMesh = renderer.sharedMesh;
                destination.sharedMaterials = renderer.sharedMaterials;
                destination.bones = new Il2CppReferenceArray<Transform>(renderer.bones.Select(b => map[b.Pointer]).ToArray());
                destination.rootBone = renderer.rootBone != null ? map[renderer.rootBone.Pointer] : copy;
                destination.localBounds = renderer.localBounds;
                destination.updateWhenOffscreen = true;
                destination.forceMatrixRecalculationPerRender = true;
                destination.enabled = true;
                destination.forceRenderingOff = false;
            }
            return copy.gameObject;
        }
        catch { Object.Destroy(copy.gameObject); throw; }
    }

    internal static GameObject PickupGeometry(GameObject source, List<Mesh> owned, List<Material> ownedMaterials, Material? fallback)
    {
        var required = new HashSet<IntPtr>();
        var meshes = source.GetComponentsInChildren<MeshRenderer>(true)
            .Where(r => r.GetComponent<MeshFilter>()?.sharedMesh != null).ToArray();
        var skins = source.GetComponentsInChildren<SkinnedMeshRenderer>(true).ToArray();
        void Include(Transform transform)
        {
            for (var current = transform; current != null; current = current.parent)
            { required.Add(current.Pointer); if (current.Pointer == source.transform.Pointer) break; }
        }
        foreach (var renderer in meshes) Include(renderer.transform);
        foreach (var renderer in skins) Include(renderer.transform);
        if (required.Count == 0) throw new InvalidOperationException("Weapon pickup has no visible mesh.");
        var map = new Dictionary<IntPtr, Transform>();
        var copy = CopyTransforms(source.transform, null, required, map);
        copy.localPosition = Vector3.zero; copy.localRotation = Quaternion.identity; copy.localScale = Vector3.one;
        foreach(var original in source.GetComponentsInChildren<Transform>(true))
            if(map.TryGetValue(original.Pointer,out var clone) && clone!=copy)clone.gameObject.SetActive(original.gameObject.activeSelf);
        try
        {
            foreach (var renderer in meshes)
            {
                var obj = map[renderer.transform.Pointer].gameObject;
                obj.AddComponent<MeshFilter>().sharedMesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                obj.AddComponent<MeshRenderer>().sharedMaterials = CompatibleGunMaterials(renderer.sharedMaterials, ownedMaterials, fallback);
            }
            foreach (var renderer in skins)
            {
                var mesh = new Mesh();
                owned.Add(mesh); renderer.BakeMesh(mesh);
                var obj = map[renderer.transform.Pointer].gameObject;
                obj.AddComponent<MeshFilter>().sharedMesh = mesh;
                obj.AddComponent<MeshRenderer>().sharedMaterials = CompatibleGunMaterials(renderer.sharedMaterials, ownedMaterials, fallback);
            }
            return copy.gameObject;
        }
        catch { Object.Destroy(copy.gameObject); throw; }
    }

    private static Il2CppReferenceArray<Material> CompatibleGunMaterials(Il2CppReferenceArray<Material> source, List<Material> owned, Material? fallback)
    {
        return new Il2CppReferenceArray<Material>(source.Select(material =>
        {
            var actual = material != null ? material : fallback;
            var copy = actual != null ? new Material(actual) : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            owned.Add(copy);
            copy.name = actual != null ? "PBDMP " + actual.name : "PBDMP missing gun material";
            var texture = actual == null ? null : actual.HasProperty("_BaseMap") ? actual.GetTexture("_BaseMap") :
                actual.HasProperty("_MainTex") ? actual.GetTexture("_MainTex") : null;
            if(texture==null && fallback!=null)texture=fallback.HasProperty("_BaseMap")?fallback.GetTexture("_BaseMap"):fallback.HasProperty("_MainTex")?fallback.GetTexture("_MainTex"):null;
            if(copy.HasProperty("_BaseMap"))copy.SetTexture("_BaseMap", texture);
            if(actual==null && copy.HasProperty("_BaseColor"))copy.SetColor("_BaseColor", Color.white);
            return copy;
        }).ToArray());
    }

    private static Transform CopyTransforms(Transform source, Transform? parent, HashSet<IntPtr> required, Dictionary<IntPtr, Transform> map)
    {
        var copy = new GameObject(source.name).transform;
        copy.SetParent(parent, false);
        copy.localPosition = source.localPosition; copy.localRotation = source.localRotation; copy.localScale = source.localScale;
        copy.gameObject.layer = Hyperstrange.PBD.LayerManager.Layers.NPC;
        map.Add(source.Pointer, copy);
        for (var i = 0; i < source.childCount; i++)
            if (required.Contains(source.GetChild(i).Pointer)) CopyTransforms(source.GetChild(i), copy, required, map);
        return copy;
    }
}
