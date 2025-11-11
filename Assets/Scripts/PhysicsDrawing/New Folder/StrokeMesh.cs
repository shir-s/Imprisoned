// FILEPATH: Assets/Scripts/PhysicsDrawing/StrokeMesh.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class StrokeMesh : MonoBehaviour
{
    private Mesh _mesh;
    private Material _mat;
    private float _minSpacing;

    // tiny lift to avoid z-fighting with the surface (applied to BOTH top & bottom)
    [SerializeField] private float _liftAlongNormal = 0.001f;

    // Real thickness in meters (bottom extends down from the top plane)
    [SerializeField] private float _thicknessMeters = 0.01f;

    // spine
    private readonly List<Vector3> _pts = new List<Vector3>();
    private readonly List<Vector3> _nrm = new List<Vector3>();
    private readonly List<float>   _dia = new List<float>();

    private MeshFilter _mf;
    private MeshRenderer _mr;

    public float ThicknessMeters => _thicknessMeters;
    public float GetThicknessMeters() => _thicknessMeters;

    void Awake()
    {
        _mf = GetComponent<MeshFilter>();
        _mr = GetComponent<MeshRenderer>();
        if (_mesh == null)
        {
            _mesh = new Mesh { name = "StrokeMesh" };
            _mesh.MarkDynamic();
        }
        _mf.sharedMesh = _mesh;
    }

    public void Init(Material mat, float brushDiameter, float minSpacing)
    {
        _mat = mat;
        _mr.sharedMaterial = _mat;
        _minSpacing = Mathf.Max(1e-5f, minSpacing);

        _pts.Clear(); _nrm.Clear(); _dia.Clear();
        _mesh.Clear();
    }

    public void SetVertexLiftAlongNormal(float lift) => _liftAlongNormal = Mathf.Max(0f, lift);

    public void SetUniformThickness(float meters) => _thicknessMeters = Mathf.Max(0f, meters);

    public void AddPoint(Vector3 p, Vector3 n, float brushDiameter)
    {
        // spacing
        if (_pts.Count > 0)
        {
            var d = (p - _pts[_pts.Count - 1]).sqrMagnitude;
            if (d < _minSpacing * _minSpacing) return;
        }

        _pts.Add(p);
        _nrm.Add(n.normalized);
        _dia.Add(Mathf.Max(1e-5f, brushDiameter));

        RebuildMesh();
    }

    public void StampDot(Vector3 p, Vector3 n, float brushDiameter)
    {
        // Put three very short samples to force a small disk section
        AddPoint(p, n, brushDiameter);
        AddPoint(p + n * 1e-5f, n, brushDiameter);
        AddPoint(p + n * 2e-5f, n, brushDiameter);
    }

    void RebuildMesh()
    {
        if (_pts.Count < 2) return;

        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uvs   = new List<Vector2>();
        var tris  = new List<int>();

        float halfT = _thicknessMeters * 0.5f;

        // simple ribbon with top & bottom (no sides for brevity)
        for (int i = 0; i < _pts.Count; i++)
        {
            Vector3 p = _pts[i];
            Vector3 n = _nrm[i];
            float d   = _dia[i];
            float r   = d * 0.5f;

            // build local tangent frame
            Vector3 t = (i == _pts.Count - 1) ? (_pts[i] - _pts[i - 1]) : (_pts[i + 1] - _pts[i]);
            if (t.sqrMagnitude < 1e-10f) t = Vector3.right;
            t = Vector3.ProjectOnPlane(t, n).normalized;
            Vector3 b = Vector3.Cross(n, t).normalized; // across

            Vector3 lift = n * _liftAlongNormal;

            // top quad edge
            Vector3 pL = p - b * r + lift;
            Vector3 pR = p + b * r + lift;

            // bottom (down from top by thickness)
            Vector3 pLb = pL - n * _thicknessMeters;
            Vector3 pRb = pR - n * _thicknessMeters;

            // add ring (Ltop, Rtop, Lbot, Rbot)
            int baseIdx = verts.Count;
            verts.Add(pL); norms.Add(n);   uvs.Add(new Vector2(0, 1));
            verts.Add(pR); norms.Add(n);   uvs.Add(new Vector2(1, 1));
            verts.Add(pLb);norms.Add(-n);  uvs.Add(new Vector2(0, 0));
            verts.Add(pRb);norms.Add(-n);  uvs.Add(new Vector2(1, 0));

            if (i > 0)
            {
                int prev = baseIdx - 4;

                // top strip
                tris.Add(prev + 0); tris.Add(prev + 1); tris.Add(baseIdx + 0);
                tris.Add(prev + 1); tris.Add(baseIdx + 1); tris.Add(baseIdx + 0);

                // bottom strip
                tris.Add(prev + 2); tris.Add(baseIdx + 2); tris.Add(prev + 3);
                tris.Add(prev + 3); tris.Add(baseIdx + 2); tris.Add(baseIdx + 3);

                // left side
                tris.Add(prev + 0); tris.Add(baseIdx + 0); tris.Add(prev + 2);
                tris.Add(prev + 2); tris.Add(baseIdx + 0); tris.Add(baseIdx + 2);

                // right side
                tris.Add(prev + 1); tris.Add(prev + 3); tris.Add(baseIdx + 1);
                tris.Add(prev + 3); tris.Add(baseIdx + 3); tris.Add(baseIdx + 1);
            }
        }

        _mesh.SetVertices(verts);
        _mesh.SetNormals(norms);
        _mesh.SetUVs(0, uvs);
        _mesh.SetTriangles(tris, 0, true, 0);
        _mesh.RecalculateBounds();
    }
}
