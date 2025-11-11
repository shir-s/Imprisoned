// FILEPATH: Assets/Scripts/PhysicsDrawing/MeshWear.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Localized mesh abrasion: pushes only vertices near the contact inward,
/// with a smooth falloff around the contact point. Works for corners/edges,
/// so only the used side shrinks.
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[DisallowMultipleComponent]
public class MeshWear : MonoBehaviour, IToolWear
{
    [Header("Wear Shape")]
    [SerializeField] private float maxWearRadius = 0.01f;     // meters around contact that can be affected
    [SerializeField] private float maxPerFrameWear = 0.0005f; // clamp safety
    [SerializeField, Range(0f,1f)] private float hardness = 0.65f;          // 0..1: lower = softer = faster wear
    [SerializeField, Range(0f,1f)] private float directionalBias = 0.6f;    // 0..1: align wear with surface normal

    [Header("Rebuild & Collider")]
    [SerializeField] private bool updateNormals = true;
    [SerializeField] private bool updateCollider = true;

    MeshFilter _mf;
    Mesh _runtimeMesh;

    Vector3[] _verts;
    Vector3[] _normals;          // working normals
    Vector3[] _origNormals;      // original normals (for stability)
    readonly List<int> _affected = new();

    void Awake()
    {
        _mf = GetComponent<MeshFilter>();

        // Make a unique, runtime-editable mesh
        _runtimeMesh = Instantiate(_mf.sharedMesh);
        _runtimeMesh.name = _mf.sharedMesh.name + " (MeshWear)";
        _runtimeMesh.MarkDynamic();
        _mf.sharedMesh = _runtimeMesh;

        _verts = _runtimeMesh.vertices;
        _normals = _runtimeMesh.normals;
        _origNormals = (Vector3[])_normals.Clone();
    }

    public void WearAt(Vector3 contactPointWorld, Vector3 surfaceNormalWorld, float amount, float radius)
    {
        if (_runtimeMesh == null || _verts == null || _verts.Length == 0) return;

        // Safety clamp (e.g., very large dt spikes)
        amount = Mathf.Min(amount, maxPerFrameWear);
        float r = Mathf.Clamp(radius <= 0f ? maxWearRadius : radius, 0.0005f, maxWearRadius);

        // Convert contact to local space
        Vector3 contactLocal = transform.InverseTransformPoint(contactPointWorld);
        Vector3 nLocalSurf = transform.InverseTransformDirection(surfaceNormalWorld).normalized;

        // We'll bias the push direction between the surface normal and each vertex normal.
        _affected.Clear();
        float r2 = r * r;

        // Pass 1: Collect affected vertices and compute per-vertex displacement.
        for (int i = 0; i < _verts.Length; i++)
        {
            Vector3 v = _verts[i];
            Vector3 to = v - contactLocal;
            float d2 = to.sqrMagnitude;
            if (d2 > r2) continue;              // outside radius

            // Smooth falloff (cubic)
            float d = Mathf.Sqrt(d2);
            float t = 1f - Mathf.Clamp01(d / r); // 1 at center -> 0 at edge
            float falloff = t * t * (3f - 2f * t);

            // Directional push: blend surface normal with original vertex normal
            Vector3 nBlend = Vector3.Slerp(_origNormals[i], nLocalSurf, directionalBias).normalized;

            // Wear magnitude scales with falloff and softness (hardness inverse)
            float wearMag = amount * (1f - hardness) * falloff;

            // Push inward, opposite to blended normal
            _verts[i] -= nBlend * wearMag;
            _affected.Add(i);
        }

        if (_affected.Count == 0) return;

        // Pass 2: optional smooth relax for just the affected zone (prevents spiky corners)
        LaplacianRelax(_affected, 1, 0.15f);

        // Apply to mesh
        _runtimeMesh.vertices = _verts;

        if (updateNormals)
        {
            // Unity 6000.x: use the parameterless overload
            _runtimeMesh.RecalculateNormals();
            _normals = _runtimeMesh.normals;
        }

        _runtimeMesh.RecalculateBounds();

        if (updateCollider)
        {
            // If you have a MeshCollider on the same GO or child, refresh it.
            var mc = GetComponent<MeshCollider>();
            if (mc != null)
            {
                mc.sharedMesh = null;          // force Unity to update BVH
                mc.sharedMesh = _runtimeMesh;
            }
        }
    }

    // A tiny, bounded Laplacian relax over the affected vertices only.
    // This keeps corners from turning into noisy spikes after many abrasions.
    void LaplacianRelax(List<int> indices, int iterations, float alpha)
    {
        if (iterations <= 0 || indices.Count == 0) return;

        // Build quick adjacency once per call (local neighborhood via triangles)
        var tris = _runtimeMesh.triangles;
        var adjacency = new Dictionary<int, List<int>>(indices.Count);

        // Seed keys so ContainsKey is O(1)
        foreach (var idx in indices) adjacency[idx] = new List<int>(6);

        for (int t = 0; t < tris.Length; t += 3)
        {
            int a = tris[t]; int b = tris[t + 1]; int c = tris[t + 2];

            if (adjacency.ContainsKey(a)) { if (!adjacency[a].Contains(b)) adjacency[a].Add(b); if (!adjacency[a].Contains(c)) adjacency[a].Add(c); }
            if (adjacency.ContainsKey(b)) { if (!adjacency[b].Contains(a)) adjacency[b].Add(a); if (!adjacency[b].Contains(c)) adjacency[b].Add(c); }
            if (adjacency.ContainsKey(c)) { if (!adjacency[c].Contains(a)) adjacency[c].Add(a); if (!adjacency[c].Contains(b)) adjacency[c].Add(b); }
        }

        var orig = new Vector3[_verts.Length];

        for (int it = 0; it < iterations; it++)
        {
            // copy
            System.Array.Copy(_verts, orig, _verts.Length);

            foreach (var i in indices)
            {
                var neigh = adjacency[i];
                if (neigh == null || neigh.Count == 0) continue;

                Vector3 avg = Vector3.zero;
                for (int k = 0; k < neigh.Count; k++) avg += orig[neigh[k]];
                avg /= neigh.Count;

                _verts[i] = Vector3.Lerp(orig[i], avg, alpha);
            }
        }
    }
}
