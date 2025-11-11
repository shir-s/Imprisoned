// FILEPATH: Assets/Scripts/Painting/MouseBrushPainter.cs
using UnityEngine;

/// <summary>
/// Builds StrokeMesh and stamps its full thickness into VolumeMap (additive).
/// Keeps the stroke material width synced, informs GateGrid, ForceGate and GrowableCube on paint.
/// Auto-includes any referenced GateGrid layers into surfaceMask.
/// Now supports painting on TRIGGER colliders that have a PaintSurfaceMarker.
/// </summary>
public class MouseBrushPainter : MonoBehaviour
{
    [Header("Scene References")]
    [SerializeField] private Camera cam;
    [SerializeField] private LayerMask toolMask;
    [SerializeField] private LayerMask surfaceMask;
    [SerializeField] private VolumeMap volumeMap;

    // Include gates so their cubes are paintable
    [SerializeField] private GateGrid[] knownGates;

    [Header("Stroke Settings")]
    [SerializeField] private float minPointSpacing = 0.0015f;
    [SerializeField] private float strokeLift = 0.0002f;
    [SerializeField] private bool  stampDotOnClick = true;

    [Header("Input")]
    [SerializeField] private KeyCode putDownKey = KeyCode.Escape;

    private BrushTool  _held;
    private BrushWear  _heldWear;
    private StrokeMesh _currentStroke;
    private Vector3    _lastPaintPos;
    private bool       _hasLast;

    void Awake()
    {
        if (!cam) cam = Camera.main;

        // Automatically include Gate cell layers into the painter's surfaceMask
        if (knownGates != null)
        {
            foreach (var g in knownGates)
            {
                if (!g) continue;
                int layer = g.CellLayer;
                surfaceMask |= (1 << layer);
            }
        }
    }

    void Update()
    {
        if (!cam) return;

        if (_held == null) EndStroke();

        if ((_held != null) && (Input.GetKeyDown(putDownKey) || Input.GetMouseButtonDown(1)))
        {
            PutDown();
            return;
        }

        if (_held == null)
        {
            if (Input.GetMouseButtonDown(0))
                TryPickUpTool();
            return;
        }

        // ---- SURFACE RAYCAST (supports triggers with PaintSurfaceMarker) ----
        if (!TryRaycastPaintSurface(out var hit))
        {
            if (Input.GetMouseButtonUp(0)) EndStroke();
            _hasLast = false;
            return;
        }

        _held.SnapToPaintingPose(hit.point, hit.normal);

        Vector3 p = hit.point + hit.normal * strokeLift;
        Vector3 n = hit.normal;

        float brushDiameter = Mathf.Max(0.0005f, _held.BrushDiameter);
        float brushRadius   = brushDiameter * 0.5f;

        if (Input.GetMouseButtonDown(0))
        {
            StartNewStroke(brushDiameter, _held.StrokeMaterial, _held.BrushColor);

            if (stampDotOnClick)
                _currentStroke.StampDot(p, n, brushDiameter);

            _currentStroke.AddPoint(p, n, brushDiameter);

            if (volumeMap != null)
                volumeMap.AddStamp(hit.point, brushRadius, Mathf.Abs(_currentStroke.ThicknessMeters));

            TryPaintGate(hit, brushRadius, 0.0001f);
            TryPaintForceGate(hit, 0.0001f);
            TryPaintGrowableCube(hit, 0.0001f);

            if (_heldWear != null)
                _heldWear.ApplyWearAt(hit.point, hit.normal, brushRadius, 0.0001f, 0f);

            _lastPaintPos = p;
            _hasLast = true;
            return;
        }

        if (Input.GetMouseButton(0) && _currentStroke != null)
        {
            float minSq = (minPointSpacing * minPointSpacing);
            float sq = _hasLast ? (p - _lastPaintPos).sqrMagnitude : float.PositiveInfinity;

            if (!_hasLast || sq >= minSq)
            {
                _currentStroke.AddPoint(p, n, brushDiameter);

                float stepMeters = Mathf.Sqrt(Mathf.Max(0f, sq));

                if (volumeMap != null)
                    volumeMap.AddStamp(hit.point, brushRadius, Mathf.Abs(_currentStroke.ThicknessMeters));

                _held.ApplyWearAlongPlane(stepMeters, hit.point, hit.normal);

                TryPaintGate(hit, brushRadius, stepMeters);
                TryPaintForceGate(hit, stepMeters);
                TryPaintGrowableCube(hit, stepMeters);

                if (_heldWear != null)
                    _heldWear.ApplyWearAt(hit.point, hit.normal, brushRadius, stepMeters, 0f);

                _lastPaintPos = p;
                _hasLast = true;
            }
        }

        if (Input.GetMouseButtonUp(0)) EndStroke();

        // keep shader width in sync
        if (_currentStroke != null)
        {
            var mr = _currentStroke.GetComponent<MeshRenderer>();
            if (mr && mr.sharedMaterial && mr.sharedMaterial.HasProperty("_DesiredWorldWidth"))
                mr.sharedMaterial.SetFloat("_DesiredWorldWidth", brushDiameter);
        }

        if (!Input.GetMouseButton(0)) _hasLast = false;
    }

    // === Raycast helper that first ignores triggers, then (if needed) includes triggers with PaintSurfaceMarker ===
    bool TryRaycastPaintSurface(out RaycastHit bestHit)
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        // pass 1: regular colliders (triggers ignored) — original behavior
        if (Physics.Raycast(ray, out bestHit, 1000f, surfaceMask, QueryTriggerInteraction.Ignore))
            return true;

        // pass 2: include triggers, but only accept colliders that have PaintSurfaceMarker somewhere (self or parent)
        var hits = Physics.RaycastAll(ray, 1000f, surfaceMask, QueryTriggerInteraction.Collide);
        if (hits != null && hits.Length > 0)
        {
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (h.collider == null) continue;
                if (h.collider.isTrigger)
                {
                    if (h.collider.GetComponent<PaintSurfaceMarker>() != null ||
                        h.collider.GetComponentInParent<PaintSurfaceMarker>() != null)
                    {
                        bestHit = h;
                        return true;
                    }
                }
                else
                {
                    // if a solid collider appears in this pass, it's also fine
                    bestHit = h;
                    return true;
                }
            }
        }

        bestHit = default;
        return false;
    }

    // === Hooks for Gate and other paintable objects ===

    void TryPaintGate(RaycastHit hit, float brushRadius, float metersDrawn)
    {
        var cell = hit.collider.GetComponent<GateCell>();
        if (cell != null && cell.Grid != null)
        {
            cell.Grid.AddPaint(hit.point, brushRadius, metersDrawn);
        }
    }

    void TryPaintForceGate(RaycastHit hit, float metersDrawn)
    {
        var forceGate = hit.collider.GetComponentInParent<ForceGate>();
        if (forceGate != null)
        {
            float r = _held ? _held.BrushDiameter * 0.5f : 0.05f;
            forceGate.AddPaint(hit.point, r, metersDrawn);
        }
    }

    void TryPaintGrowableCube(RaycastHit hit, float metersDrawn)
    {
        var grow = hit.collider.GetComponent<GrowableCube>();
        if (grow != null)
        {
            grow.OnDrawOnFace(hit.normal, metersDrawn);
        }
    }

    // === Stroke management ===

    void StartNewStroke(float brushDiameter, Material baseMat, Color color)
    {
        EndStroke();
        var go = new GameObject("StrokeMesh_Current");
        _currentStroke = go.AddComponent<StrokeMesh>();
        var instancedMat = new Material(baseMat) { color = color };

        if (instancedMat.HasProperty("_DesiredWorldWidth"))
            instancedMat.SetFloat("_DesiredWorldWidth", brushDiameter);
        if (instancedMat.HasProperty("_CoreFill"))
            instancedMat.SetFloat("_CoreFill", 1.0f);
        if (instancedMat.HasProperty("_EdgeSoft"))
            instancedMat.SetFloat("_EdgeSoft", 0.10f);

        _currentStroke.Init(instancedMat,
                            Mathf.Max(0.0005f, brushDiameter),
                            Mathf.Max(0.0002f, minPointSpacing));
        _currentStroke.SetVertexLiftAlongNormal(Mathf.Max(0f, strokeLift));
    }

    void EndStroke()
    {
        _currentStroke = null;
        _hasLast = false;
    }

    // === Tool pickup & release ===

    void TryPickUpTool()
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out var hitTool, 1000f, toolMask, QueryTriggerInteraction.Ignore)) return;

        var tool = hitTool.collider.GetComponentInParent<BrushTool>();
        if (tool == null) return;

        if (!TryRaycastPaintSurface(out var hitSurf)) return;

        _held = tool;
        _heldWear = _held.GetComponentInChildren<BrushWear>();
        _held.OnPickedUp(hitSurf.point, hitSurf.normal);

        EndStroke();
    }

    void PutDown()
    {
        if (_held != null)
        {
            _held.OnPutDown();
            _held = null;
            _heldWear = null;
        }
        EndStroke();
    }
}
