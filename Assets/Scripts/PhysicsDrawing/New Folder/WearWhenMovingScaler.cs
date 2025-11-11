// FILEPATH: Assets/Scripts/PhysicsDrawing/WearWhenMovingScaler.cs
using UnityEngine;

/// <summary>
/// Cheap, non-mesh wear: converts traveled distance (m) into shrink along a chosen local axis.
/// Does NOT touch the mesh vertices, only transform scale + a small position shift to keep one end anchored.
/// Also exposes a toggle to disable painting scripts while wear still applies.
/// </summary>
[DisallowMultipleComponent]
public class WearWhenMovingScaler : MonoBehaviour
{
    public enum Axis { X, Y, Z }

    [Header("Wear")]
    [Tooltip("Meters of length removed per meter of travel (and per painting meter if you call AddPaintMeters).")]
    [SerializeField] private float wearPerMeter = 0.0002f;
    [Tooltip("Maximum total wear (meters) that can be removed along the shrink axis.")]
    [SerializeField] private float maxWearDepth = 0.01f;

    [Header("Movement Sampling")]
    [Tooltip("If true, shrink when the object moves in world space (physics or kinematic).")]
    [SerializeField] private bool wearOnMovement = true;
    [Tooltip("Ignore jitter below this world-space distance (meters).")]
    [SerializeField] private float minMoveDelta = 0.00005f;

    [Header("Shrink Axis & Anchor")]
    [SerializeField] private Axis shrinkAxis = Axis.Y;
    [Tooltip("Keeps the negative end fixed (e.g., -Y tip). If false, keeps the positive end fixed.")]
    [SerializeField] private bool anchorNegativeEnd = true;
    [Tooltip("Local scale floor to avoid collapse/NaN.")]
    [SerializeField] private float minAxisScale = 0.01f;

    [Header("Disable Painting While Wearing")]
    [Tooltip("If false, any MouseBrushPainter/BrushTool on this object will be disabled, but wear still happens.")]
    [SerializeField] private bool drawEnabled = true;

    [Header("Debug")]
    [SerializeField] private bool logWear = false;

    // --- runtime ---
    private Vector3 _prevPos;
    private bool _firstFrame = true;
    private float _accumWear;          // meters removed so far
    private float _baseAxisLenWorld;   // baseline world length along axis
    private MeshFilter _mf;

    private Behaviour _mouseBrushPainter; // optional
    private Behaviour _brushTool;         // optional

    void Awake()
    {
        _mf = GetComponent<MeshFilter>(); // only to read initial bounds; safe if missing too
        CachePainterComponents();
        ApplyDrawToggle();

        // Baseline axis world length (from mesh bounds if available)
        float localLen = 1f;
        if (_mf && _mf.sharedMesh) // local bounds
        {
            var b = _mf.sharedMesh.bounds;
            localLen = Mathf.Abs(
                shrinkAxis == Axis.X ? b.size.x :
                shrinkAxis == Axis.Y ? b.size.y : b.size.z);
            if (localLen <= 1e-6f) localLen = 1f;
        }

        var ls = transform.lossyScale;
        float axisLossy = Mathf.Abs(
            shrinkAxis == Axis.X ? ls.x :
            shrinkAxis == Axis.Y ? ls.y : ls.z);

        _baseAxisLenWorld = localLen * Mathf.Max(1e-6f, axisLossy);
    }

    void OnEnable()
    {
        _firstFrame = true;
        CachePainterComponents();
        ApplyDrawToggle();
    }

    void OnValidate()
    {
        if (minAxisScale < 0.0001f) minAxisScale = 0.0001f;
        if (minMoveDelta  < 0f)     minMoveDelta  = 0f;
        if (wearPerMeter  < 0f)     wearPerMeter  = 0f;
        if (maxWearDepth  < 0f)     maxWearDepth  = 0f;

        if (isActiveAndEnabled)
        {
            CachePainterComponents();
            ApplyDrawToggle();
        }
    }

    void LateUpdate()
    {
        if (!wearOnMovement) return;

        Vector3 p = transform.position;
        if (_firstFrame)
        {
            _prevPos = p;
            _firstFrame = false;
            return;
        }

        float moved = (p - _prevPos).magnitude;
        _prevPos = p;

        if (moved >= minMoveDelta && float.IsFinite(moved))
        {
            float meters = wearPerMeter * moved;
            ApplyWearMeters(meters);
        }
    }

    /// <summary>
    /// Call this from your painting path (e.g., where you know metersDrawn).
    /// Keeps this script decoupled from any painter code.
    /// </summary>
    public void AddPaintMeters(float metersDrawn, float pressure = 1f)
    {
        if (metersDrawn <= 0f || !float.IsFinite(metersDrawn)) return;
        ApplyWearMeters(wearPerMeter * metersDrawn * Mathf.Max(0f, pressure));
    }

    private void ApplyWearMeters(float wearMeters)
    {
        if (wearMeters <= 0f) return;

        float remain = Mathf.Max(0f, maxWearDepth - _accumWear);
        if (remain <= 0f) return;

        float d = Mathf.Min(wearMeters, remain);
        _accumWear += d;

        // Convert meters to local scale delta along axis (use baseline world length for consistency)
        float axisScaleNow = GetAxisScale(transform.localScale);
        float deltaScale = d / Mathf.Max(1e-6f, _baseAxisLenWorld);
        float newAxisScale = Mathf.Max(minAxisScale, axisScaleNow - deltaScale);

        // world length change for center shift (keep one end anchored)
        float worldDeltaLen = (axisScaleNow - newAxisScale) * _baseAxisLenWorld;

        Vector3 axisWorldDir = AxisWorldDirection();
        transform.position += (anchorNegativeEnd ? -1f : 1f) * axisWorldDir * (worldDeltaLen * 0.5f);

        // apply scale
        var ls = transform.localScale;
        switch (shrinkAxis)
        {
            case Axis.X: ls.x = newAxisScale; break;
            case Axis.Y: ls.y = newAxisScale; break;
            case Axis.Z: ls.z = newAxisScale; break;
        }
        transform.localScale = ls;

        if (logWear)
            Debug.Log($"[WearWhenMovingScaler] d={d:F6}m, newScaleAxis={newAxisScale:F4}, worldΔ={worldDeltaLen:F6}m, accum={_accumWear:F6}/{maxWearDepth:F6}m");
    }

    // --- painter toggle helpers ---
    private void CachePainterComponents()
    {
        if (_mouseBrushPainter == null)
            _mouseBrushPainter = GetComponent("MouseBrushPainter") as Behaviour;
        if (_brushTool == null)
            _brushTool = GetComponent("BrushTool") as Behaviour;
    }

    private void ApplyDrawToggle()
    {
        if (_mouseBrushPainter) _mouseBrushPainter.enabled = drawEnabled;
        if (_brushTool)         _brushTool.enabled = drawEnabled;
    }

    // --- axis helpers ---
    private float GetAxisScale(Vector3 s)
    {
        switch (shrinkAxis)
        {
            case Axis.X: return s.x;
            case Axis.Y: return s.y;
            default:     return s.z;
        }
    }

    private Vector3 AxisWorldDirection()
    {
        switch (shrinkAxis)
        {
            case Axis.X: return transform.TransformDirection(Vector3.right).normalized;
            case Axis.Y: return transform.TransformDirection(Vector3.up).normalized;
            default:     return transform.TransformDirection(Vector3.forward).normalized;
        }
    }
}
