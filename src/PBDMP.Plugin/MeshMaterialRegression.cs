using Hyperstrange.PBD;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PBDMP.Plugin;

internal static class MeshMaterialRegression
{
    internal static void Run()
    {
        var fixture = new GameObject("PBDMP unreadable material fixture");
        fixture.transform.position = new Vector3(10000, 10000, 10000);
        var mesh = new Mesh { name = "PBDMP NonReadableMeshProbe" };
        var left = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        var right = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        Mesh? readableCollision = null;
        try
        {
            mesh.vertices = new Il2CppStructArray<Vector3>(new[] {
                new Vector3(-1, 0, -1), new Vector3(-1, 0, 1), new Vector3(0, 0, 1), new Vector3(0, 0, -1),
                new Vector3(0, 0, -1), new Vector3(0, 0, 1), new Vector3(1, 0, 1), new Vector3(1, 0, -1) });
            mesh.subMeshCount = 2;
            mesh.SetTriangles(new Il2CppStructArray<int>(new[] { 0, 1, 2, 0, 2, 3 }), 0);
            mesh.SetTriangles(new Il2CppStructArray<int>(new[] { 4, 5, 6, 4, 6, 7 }), 1);
            mesh.RecalculateBounds();
            fixture.AddComponent<MeshFilter>().sharedMesh = mesh;
            fixture.AddComponent<MeshRenderer>().sharedMaterials = new Il2CppReferenceArray<Material>(new[] { left, right });
            var collider = fixture.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            readableCollision = Object.Instantiate(mesh);
            mesh.UploadMeshData(true);
            Physics.SyncTransforms();
            if (mesh.isReadable) throw new InvalidOperationException("Mesh fixture must be unreadable.");
            foreach (var collisionMesh in new[] { mesh, readableCollision })
            {
                collider.sharedMesh = collisionMesh;
                Physics.SyncTransforms();
                foreach (var x in new[] { -0.5f, 0.5f })
                {
                    var ray = new Ray(fixture.transform.position + new Vector3(x, 2, 0), Vector3.down);
                    if (!collider.Raycast(ray, out var hit, 4)) throw new InvalidOperationException("Fixture ray missed.");
                    if (!SolidManager.Instance.GetMaterialFromRaycastHit(hit, out var material) || material == null ||
                        material.Pointer != (x < 0 ? left : right).Pointer)
                        throw new InvalidOperationException("Unreadable render mesh lookup must preserve both collider submesh materials.");
                }
            }
            Plugin.Logger.LogInfo("SMOKE PASS: native SolidManager preserves both materials with unreadable render mesh and readable/unreadable collider meshes.");
        }
        finally { Object.Destroy(fixture); Object.Destroy(mesh); if (readableCollision != null) Object.Destroy(readableCollision); Object.Destroy(left); Object.Destroy(right); }
    }
}
