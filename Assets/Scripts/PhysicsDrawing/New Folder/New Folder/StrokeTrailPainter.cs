// FILEPATH: Assets/Scripts/Painting/StrokeTrailPainter.cs
using UnityEngine;

/// <summary>
/// IMovementPainter implementation that leaves StrokeMesh trails
/// under the object as it moves. Each stroke is a separate GameObject,
/// and when the stroke is finished it is parented under the collider we painted on.
/// </summary>
[DisallowMultipleComponent]
public class StrokeTrailPainter : MonoBehaviour, IMovementPainter
{
    [Header("Raycast")]
    [Tooltip("Layers that can receive paint strokes.")]
    [SerializeField] private LayerMask surfaceMask;

    [Tooltip("Max ray distance from the cube to find a surface.")]
    [SerializeField] private float rayDistance = 2f;

    [Tooltip("If true, cast ray straight down (world -Y). If false, use -transform.up.")]
    [SerializeField] private bool useWorldDown = true;

    [Header("Stroke Settings")]
    [SerializeField] private Material strokeMaterial;
    [SerializeField] private Color strokeColor = Color.black;

    [Tooltip("World diameter (meters) of the stroke width.")]
    [SerializeField] private float strokeDiameter = 0.02f;

    [Tooltip("Lift stroke vertices along the surface normal to avoid z-fighting.")]
    [SerializeField] private float strokeLift = 0.0002f;

    [Tooltip("Minimum spacing between successive stroke points (meters).")]
    [SerializeField] private float minPointSpacing = 0.0015f;

    [Tooltip("Whether to stamp a dot at the first contact point of a stroke.")]
    [SerializeField] private bool stampDotOnStart = true;

    [Header("Debug")]
    [SerializeField] private bool debugRays = false;

    // --- runtime ---
    private StrokeMesh _currentStroke;
    private Transform  _currentParent;      // collider we are currently painting on
    private Vector3    _lastStrokePosWS;
    private bool       _hasLastStroke;

    // ========== IMovementPainter API ==========

    public void OnMovementStart(Vector3 worldPos)
    {
        _hasLastStroke = false;

        if (TryRaycastSurface(worldPos, out var hit))
        {
            StartNewStroke(hit);
        }
        else
        {
            EndStroke();
        }
    }

    public void OnMoveStep(Vector3 from, Vector3 to, float stepMeters, float deltaTime)
    {
        if (!TryRaycastSurface(to, out var hit))
        {
            // Lost surface under us -> end current stroke
            EndStroke();
            _hasLastStroke = false;
            return;
        }

        // If we have no stroke yet, start one now
        if (_currentStroke == null)
        {
            StartNewStroke(hit);
            return;
        }

        // If we changed the painted object, end old stroke and start a new one
        if (hit.collider.transform != _currentParent)
        {
            EndStroke();
            StartNewStroke(hit);
            return;
        }

        Vector3 p = hit.point + hit.normal * strokeLift;
        Vector3 n = hit.normal;

        if (!_hasLastStroke)
        {
            // First point for this stroke segment
            _currentStroke.AddPoint(p, n, strokeDiameter);
            _lastStrokePosWS = p;
            _hasLastStroke = true;
            return;
        }

        float sq = (p - _lastStrokePosWS).sqrMagnitude;
        float minSq = minPointSpacing * minPointSpacing;

        if (sq < minSq)
            return; // too close, skip

        _currentStroke.AddPoint(p, n, strokeDiameter);
        _lastStrokePosWS = p;
        _hasLastStroke = true;
    }

    public void OnMovementEnd(Vector3 worldPos)
    {
        EndStroke();
        _hasLastStroke = false;
    }

    // ========== Stroke helpers ==========

    private void StartNewStroke(RaycastHit hit)
    {
        EndStroke();

        if (strokeMaterial == null)
        {
            Debug.LogWarning($"[StrokeTrailPainter] No strokeMaterial set on {name}, cannot create stroke.");
            return;
        }

        // Remember which object we are painting on.
        // We will parent the stroke to this transform when it is finished.
        _currentParent = hit.collider.transform;

        // Create stroke GameObject at root (no parent).
        // StrokeMesh expects world-space positions, so keeping transform identity is easiest.
        var go = new GameObject("StrokeMesh_Trail");
        _currentStroke = go.AddComponent<StrokeMesh>();

        // Instance material
        var instancedMat = new Material(strokeMaterial) { color = strokeColor };

        // Optional shader properties as in MouseBrushPainter
        if (instancedMat.HasProperty("_DesiredWorldWidth"))
            instancedMat.SetFloat("_DesiredWorldWidth", strokeDiameter);
        if (instancedMat.HasProperty("_CoreFill"))
            instancedMat.SetFloat("_CoreFill", 1.0f);
        if (instancedMat.HasProperty("_EdgeSoft"))
            instancedMat.SetFloat("_EdgeSoft", 0.10f);

        _currentStroke.Init(instancedMat,
                            Mathf.Max(0.0005f, strokeDiameter),
                            Mathf.Max(0.0002f, minPointSpacing));
        _currentStroke.SetVertexLiftAlongNormal(Mathf.Max(0f, strokeLift));

        // First dot
        Vector3 p = hit.point + hit.normal * strokeLift;
        Vector3 n = hit.normal;

        if (stampDotOnStart)
            _currentStroke.StampDot(p, n, strokeDiameter);

        _currentStroke.AddPoint(p, n, strokeDiameter);
        _lastStrokePosWS = p;
        _hasLastStroke = true;
    }

    private void EndStroke()
    {
        // If we have an active stroke and a parent surface, parent the stroke
        // so that future movement of the surface will carry the stroke with it.
        if (_currentStroke != null && _currentParent != null)
        {
            // worldPositionStays = true -> keep the stroke exactly where it is in world,
            // but make it a child of the painted object.
            _currentStroke.transform.SetParent(_currentParent, worldPositionStays: true);
        }

        _currentStroke = null;
        _currentParent = null;
        _hasLastStroke = false;
    }

    // ========== Raycast helper (similar idea to MouseBrushPainter but from cube, not camera) ==========

    private bool TryRaycastSurface(Vector3 fromPos, out RaycastHit bestHit)
    {
        Vector3 dir = useWorldDown ? Vector3.down : -transform.up;
        Vector3 start = fromPos + dir * -0.01f; // small lift to avoid starting inside collider

        if (debugRays)
            Debug.DrawRay(start, dir * rayDistance, Color.magenta, 0.1f);

        // Pass 1: ignore triggers
        if (Physics.Raycast(start, dir, out bestHit, rayDistance, surfaceMask, QueryTriggerInteraction.Ignore))
            return true;

        // Pass 2: include triggers but only with PaintSurfaceMarker
        var hits = Physics.RaycastAll(start, dir, rayDistance, surfaceMask, QueryTriggerInteraction.Collide);
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
                    bestHit = h;
                    return true;
                }
            }
        }

        bestHit = default;
        return false;
    }
}
