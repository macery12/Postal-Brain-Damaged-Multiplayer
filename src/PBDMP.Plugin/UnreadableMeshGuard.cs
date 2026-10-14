using HarmonyLib;
using Hyperstrange.PBD;
using UnityEngine;

namespace PBDMP.Plugin;

// Preserve the game's readable-mesh path; avoid its CPU triangle getter for static-batched meshes.
[HarmonyPatch(typeof(SolidManager), "GetMaterialFromRaycastHit")]
internal static class UnreadableMeshGuard
{
    [HarmonyPrefix]
    private static bool Prefix(RaycastHit __0, ref Material __1, ref bool __result)
    {
        var collider = __0.collider != null ? __0.collider.TryCast<MeshCollider>() : null;
        // Static batching replaces MeshFilter's mesh, while physics keeps the original collider mesh.
        // The game's lookup reads MeshFilter.triangles using an index from the collider.
        var renderMesh = collider != null ? collider.GetComponent<MeshFilter>()?.sharedMesh : null;
        if (renderMesh == null || renderMesh.isReadable || collider!.convex) return true;
        __1 = null!; __result = false;
        var mesh = collider!.sharedMesh;
        if (mesh == null) return false;
        var renderer = collider!.GetComponent<Renderer>();
        if (renderer == null) return false;
        var materials = renderer.sharedMaterials;
        var triangleIndex = __0.triangleIndex;
        var firstTriangle = 0;
        for (var i = 0; i < mesh.subMeshCount && i < materials.Length; i++)
        {
            var submesh = mesh.GetSubMesh(i);
            if (submesh.topology != MeshTopology.Triangles) continue;
            var triangleCount = submesh.indexCount / 3;
            if (triangleIndex >= firstTriangle && triangleIndex < firstTriangle + triangleCount)
            { __1 = materials[i]; __result = __1 != null; return false; }
            firstTriangle += triangleCount;
        }
        return false;
    }
}
