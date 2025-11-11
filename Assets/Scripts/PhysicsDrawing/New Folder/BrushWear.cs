// FILEPATH: Assets/Scripts/PhysicsDrawing/BrushWear.cs
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Deforms a mesh to simulate tool wear when painting against a surface.
/// Robust against NaN/Inf by clamping inputs and validating vertices/bounds.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter))]
public class BrushWear : MonoBehaviour
{
    public enum FacingMode { Off, OpposeSurface, AlignWithSurface, Auto }
    public enum DeformDirection { VertexNormal, PlaneNormal }

    [Header("Wear Amount")]
    [Tooltip("Meters of material removed per meter of stroke travel (before falloff).")]
    [SerializeField] private float wearPerMeter = 0.0002f;
    [Tooltip("Maximum total wear depth allowed (meters).")]
    [SerializeField] private float maxWearDepth = 0.01f;

    [Header("Contact Region")]
    [Tooltip("Max distance from the paint plane on both sides where wear is allowed (meters).")]
    [SerializeField] private float contactSlab = 0.004f;
    [Tooltip("Brush radius multiplier that controls radial falloff sharpness (higher = sharper).")]
    [SerializeField] private float radialSharpness = 3.0f;

    [Header("Facing Gate")]
    [Tooltip("If 'OpposeSurface', only vertices facing the surface (dot(nv, planeN) < -threshold) wear.\nIf 'AlignWithSurface', only vertices aligned with plane normal (dot > threshold) wear.\n'Auto' accepts either side above |threshold|.\n'Off' disables facing gate.")]
    [SerializeField] private FacingMode facing = FacingMode.Auto;
    [Range(-1f, 1f)] [SerializeField] private float alignThreshold = 0.1f;

    [Header("Deformation")]
    [Tooltip("PlaneNormal = shave along the surface normal; VertexNormal = along each vertex normal.")]
    [SerializeField] private DeformDirection deformDirection = DeformDirection.PlaneNormal;

    [Header("Cap / Visibility")]
    [Tooltip("Prevent vertices from going under the surface. Clamps to plane and adds a tiny lift to avoid z-fighting.")]
    [SerializeField] private bool capToPlane = true;
    [Tooltip("Meters to lift the clamped vertex off the plane (prevents z-fighting).")]
    [SerializeField] private float capLift = 0.0001f;

    [Header("Debug")]
    [SerializeField] private bool logPass = false;
    [SerializeField] private Color gizmoSlabColor = new Color(0, 1, 1, 0.25f);

    // runtime data
    private MeshFilter _mf;
    private Mesh _runtimeMesh;               // cloned dynamic mesh
    private Vector3[] _baseVerts;            // original local vertices
    private Vector3[] _normals;              // current vertex normals (local)
    private float[] _accumWear;              // total applied wear depth per vertex

    private struct PassStats { public int affected, faceFail, slabFail, radFail; public string passName; public float radius; }

    void Awake()
    {
        _mf = GetComponent<MeshFilter>();
        if (_mf.sharedMesh == null)
        {
            Debug.LogWarning("[BrushWear] No mesh on MeshFilter; disabling.");
            enabled = false; return;
        }
        // Clone mesh so we never mutate a shared asset
        _runtimeMesh = Instantiate(_mf.sharedMesh);
        _runtimeMesh.name = _mf.sharedMesh.name + " (WearInstance)";
        _runtimeMesh.MarkDynamic();
        _mf.sharedMesh = _runtimeMesh;

        _baseVerts = _runtimeMesh.vertices;
        _normals   = _runtimeMesh.normals;
        if (_normals == null || _normals.Length != _baseVerts.Length)
        {
            _runtimeMesh.RecalculateNormals();
            _normals = _runtimeMesh.normals;
        }
        _accumWear = new float[_baseVerts.Length];

        // Initial safe bounds
        SafeRecalculateBounds(_runtimeMesh);
    }

    /// <summary>
    /// Apply wear at a world-space point against a world-space paint plane.
    /// </summary>
    public void ApplyWearAt(Vector3 worldPoint, Vector3 worldNormal, float brushRadius, float metersDrawn, float pressure)
    {
        if (_runtimeMesh == null || _baseVerts == null) return;

        // Guard inputs
        if (!IsFinite(worldPoint) || !IsFinite(worldNormal)) return;
        if (brushRadius <= 0f || !IsFinite(brushRadius)) return;
        if (metersDrawn <= 0f || !IsFinite(metersDrawn)) return;

        // Safe normal
        Vector3 nWS = SafeNormalize(worldNormal, Vector3.up);
        // Transform plane & point into local space of this mesh
        Matrix4x4 worldToLocal = transform.worldToLocalMatrix;
        Vector3 p0LS = worldToLocal.MultiplyPoint(worldPoint);
        Vector3 nLS  = worldToLocal.MultiplyVector(nWS);
        nLS = SafeNormalize(nLS, Vector3.up);

        // Precompute constants
        float wearStep = Mathf.Max(0f, wearPerMeter) * metersDrawn;
        float maxWear  = Mathf.Max(0f, maxWearDepth);
        float slab     = Mathf.Max(1e-6f, contactSlab);
        float r        = Mathf.Max(1e-6f, brushRadius);
        float sharp    = Mathf.Max(0.1f, radialSharpness);

        Vector3[] verts = _runtimeMesh.vertices; // copy to edit
        Vector3[] norms = _runtimeMesh.normals;

        if (norms == null || norms.Length != verts.Length)
        {
            _runtimeMesh.RecalculateNormals();
            norms = _runtimeMesh.normals;
            if (norms == null || norms.Length != verts.Length)
            {
                norms = new Vector3[verts.Length];
                for (int i = 0; i < norms.Length; i++) norms[i] = Vector3.forward;
            }
        }

        PassStats stats = default;
        stats.passName = "P1-Hit(" + facing.ToString() + ")"; stats.radius = r;

        // Facing selector
        Func<float, bool> FacingOk = (dot) => true;
        switch (facing)
        {
            case FacingMode.OpposeSurface: FacingOk = (dot) => (dot <= -alignThreshold); break;
            case FacingMode.AlignWithSurface: FacingOk = (dot) => (dot >=  alignThreshold); break;
            case FacingMode.Off: FacingOk = (dot) => true; break;
            case FacingMode.Auto: FacingOk = (dot) => Mathf.Abs(dot) >= alignThreshold; break;
        }

        // Iterate and deform
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 v = verts[i];

            // signed distance to plane (local)
            float d = Vector3.Dot(v - p0LS, nLS);
            if (!IsFinite(d)) { stats.slabFail++; continue; }
            if (Mathf.Abs(d) > slab) { stats.slabFail++; continue; }

            // radial falloff: project onto plane
            Vector3 delta = v - p0LS - nLS * d;
            float radial2 = delta.sqrMagnitude;
            if (!IsFinite(radial2)) { stats.radFail++; continue; }

            float w = Mathf.Exp(-radial2 / (2f * (r * r) / sharp));
            if (w < 1e-4f) { stats.radFail++; continue; }

            // facing gate
            Vector3 vn = (norms[i].sqrMagnitude > 1e-12f) ? norms[i].normalized : Vector3.forward;
            float dot = Vector3.Dot(vn, nLS);
            if (!FacingOk(dot)) { stats.faceFail++; continue; }

            // push direction
            Vector3 pushDirLS = (deformDirection == DeformDirection.VertexNormal && norms[i].sqrMagnitude > 1e-12f)
                                ? -vn
                                : -nLS;

            // wear amount
            float dv = wearStep * w;
            if (!IsFinite(dv) || dv <= 0f) continue;

            // clamp accumulation
            float newAccum = Mathf.Min(maxWear, _accumWear[i] + dv);
            float applied  = newAccum - _accumWear[i];
            if (applied <= 0f) continue;
            _accumWear[i] = newAccum;

            // displace
            Vector3 newV = v + pushDirLS * applied;

            // optional cap to plane + lift
            if (capToPlane)
            {
                float dNew = Vector3.Dot(newV - p0LS, nLS);
                if (dNew < 0f)
                {
                    newV = newV - nLS * dNew + nLS * Mathf.Max(0f, capLift);
                }
            }

            if (!IsFinite(newV)) continue; // safety
            verts[i] = newV;
            stats.affected++;
        }

        // Write back vertices & keep normals reasonable
        _runtimeMesh.SetVertices(new List<Vector3>(verts));
        _runtimeMesh.RecalculateNormals();

        // Safe bounds (avoids invalid AABB)
        SafeRecalculateBounds(_runtimeMesh);

        if (logPass)
        {
            Debug.Log($"[BrushWear] Cube pass={stats.passName} affected:{stats.affected} faceFail:{stats.faceFail} slabFail:{stats.slabFail} radFail:{stats.radFail} | R={r:F3} slab=±{slab:F3} dir={deformDirection} cap={capToPlane}");
        }
    }

    // ---------- Safety helpers ----------

    private static bool IsFinite(Vector3 v)
    {
        return float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    }

    private static bool IsFinite(float f) => float.IsFinite(f);

    private static Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
    {
        float m2 = v.sqrMagnitude;
        if (!(m2 > 1e-12f) || !IsFinite(v)) return fallback;
        return v / Mathf.Sqrt(m2);
    }

    private static void SafeRecalculateBounds(Mesh m)
    {
        var verts = m.vertices;
        if (verts == null || verts.Length == 0)
        {
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 0.001f);
            return;
        }

        Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

        for (int i = 0; i < verts.Length; i++)
        {
            var v = verts[i];
            if (!float.IsFinite(v.x) || !float.IsFinite(v.y) || !float.IsFinite(v.z))
            {
                // sanitize bad verts
                v = Vector3.zero;
                verts[i] = v;
            }
            if (v.x < min.x) min.x = v.x; if (v.x > max.x) max.x = v.x;
            if (v.y < min.y) min.y = v.y; if (v.y > max.y) max.y = v.y;
            if (v.z < min.z) min.z = v.z; if (v.z > max.z) max.z = v.z;
        }

        if (!float.IsFinite(min.x) || !float.IsFinite(max.x))
        {
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 0.001f);
            return;
        }

        Vector3 center = (min + max) * 0.5f;
        Vector3 size   = new Vector3(Mathf.Max(1e-5f, max.x - min.x),
                                     Mathf.Max(1e-5f, max.y - min.y),
                                     Mathf.Max(1e-5f, max.z - min.z));
        m.bounds = new Bounds(center, size);
    }

    // ---------- Gizmos (optional slab visual) ----------
    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;
        Gizmos.color = gizmoSlabColor;
        // Visual slab thickness around last known plane would require caching hit data.
        // Kept minimal here to avoid accidental NaN gizmos.
    }
}
