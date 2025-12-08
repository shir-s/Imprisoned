// FILEPATH: Assets/Scripts/PhysicsDrawing/WearWhenMovingScaler.cs
using UnityEngine;

/// <summary>
/// Cheap, non-mesh wear: converts traveled distance (m) into shrink along a chosen local axis.
/// Does NOT touch the mesh vertices, only transform scale + a small position shift to keep one end anchored.
/// Also exposes a toggle to disable painting scripts while wear still applies.
///
/// NOW:
/// - Movement-based wear is applied ONLY when the object is on a surface (raycast down to surfaceMask).
/// - There is NO max wear depth in meters: the cube just shrinks until its scale on the wear axis
///   reaches minAxisScale, and then it is considered "fully worn" and can be destroyed / fire events.
/// </summary>
[DisallowMultipleComponent]
public class WearWhenMovingScaler : MonoBehaviour
{
    public enum Axis { X, Y, Z }

    [Header("Wear")]
    [Tooltip("Meters of length removed per meter of travel (and per painting meter if you call AddPaintMeters).")]
    [SerializeField] private float wearPerMeter = 0.0002f;

    [Header("Movement Sampling")]
    [Tooltip("If true, shrink when the object moves in world space (physics or kinematic).")]
    [SerializeField] private bool wearOnMovement = true;
    [Tooltip("Ignore jitter below this world-space distance (meters).")]
    [SerializeField] private float minMoveDelta = 0.00005f;
    [Tooltip("If true and a Rigidbody exists, use its velocity to estimate distance instead of transform position delta.")]
    [SerializeField] private bool useRigidbodyVelocity = true;

    [Header("Shrink Axis & Anchor")]
    [SerializeField] private Axis shrinkAxis = Axis.Y;
    [Tooltip("Keeps the negative end fixed (e.g., -Y tip). If false, keeps the positive end fixed.")]
    [SerializeField] private bool anchorNegativeEnd = true;
    [Tooltip("Local scale floor to avoid collapse/NaN. When the scale reaches this value, the cube is 'fully worn'.")]
    [SerializeField] private float minAxisScale = 0.01f;

    [Header("Disable Painting While Wearing")]
    [Tooltip("If false, any MouseBrushPainter/BrushTool on this object will be disabled, but wear still happens.")]
    [SerializeField] private bool drawEnabled = true;

    [Header("Ground Check")]
    [Tooltip("Layers that count as 'surface' for movement-based wear.")]
    [SerializeField] private LayerMask surfaceMask = ~0;
    [Tooltip("Extra distance below the cube bottom to still be considered 'on surface'.")]
    [SerializeField] private float groundCheckPadding = 0.01f;

    [Header("Lifetime / Events")]
    [Tooltip("If true, when the cube shrinks down to minAxisScale this object will destroy itself.")]
    [SerializeField] private bool destroyWhenFullyWorn = true;
    [Tooltip("If true, when fully worn this script will fire EventManager.GameEvent.CubeDestroyed with this GameObject.")]
    [SerializeField] private bool triggerCubeDestroyedEvent = true;

    [Header("Debug")]
    [SerializeField] private bool logWear = false;
    [SerializeField] private bool debugGroundCheck = false;
    
    [Header("Anchor / Position")]
    [Tooltip("if it's true, the object's position will be adjusted to keep the anchored end in place as it shrinks.")]
    [SerializeField] private bool adjustPositionToAnchor = true;

    // --- runtime ---
    private Vector3 _prevPos;
    private bool _firstFrame = true;

    /// <summary>Baseline world length of the cube along the wear axis (at start).</summary>
    private float _baseAxisLenWorld;

    private MeshFilter _mf;
    private Collider _col;

    private Behaviour _mouseBrushPainter; // optional
    private Behaviour _brushTool;         // optional
    private Rigidbody _rb;                // optional

    private bool _fullyWorn;              // did we already hit minAxisScale?

    void Awake()
    {
        _mf = GetComponent<MeshFilter>(); // only to read initial bounds; safe if missing too
        _rb = GetComponent<Rigidbody>();  // if present, lets us use velocity-based sampling
        _col = GetComponent<Collider>();  // for ground check

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
        _fullyWorn = false;
        CachePainterComponents();
        ApplyDrawToggle();
    }

    void OnValidate()
    {
        if (minAxisScale < 0.0001f)       minAxisScale       = 0.0001f;
        if (minMoveDelta  < 0f)           minMoveDelta       = 0f;
        if (wearPerMeter  < 0f)           wearPerMeter       = 0f;
        if (groundCheckPadding < 0f)      groundCheckPadding = 0f;

        if (isActiveAndEnabled)
        {
            CachePainterComponents();
            ApplyDrawToggle();
        }
    }

    void LateUpdate()
    {
        // Movement-based wear only
        if (!wearOnMovement || _fullyWorn)
            return;

        float moved = 0f;

        // Prefer Rigidbody-based distance if available and enabled
        if (useRigidbodyVelocity && _rb != null)
        {
            // distance = speed * dt
            float speed = _rb.linearVelocity.magnitude;
            moved = speed * Time.deltaTime;
        }
        else
        {
            // Fallback: world position delta
            Vector3 p = transform.position;
            if (_firstFrame)
            {
                _prevPos = p;
                _firstFrame = false;
                return;
            }

            moved = (p - _prevPos).magnitude;
            _prevPos = p;
        }

        if (moved < minMoveDelta || !float.IsFinite(moved))
            return;

        // Only apply wear from movement while on the surface
        if (!IsOnSurface())
            return;

        float meters = wearPerMeter * moved;
        ApplyWearMeters(meters);
    }

    /// <summary>
    /// Call this from your painting path (e.g., where you know metersDrawn).
    /// Keeps this script decoupled from any painter code.
    /// Painting normally only happens while on the surface, so we don't gate this by IsOnSurface().
    /// </summary>
    public void AddPaintMeters(float metersDrawn, float pressure = 1f)
    {
        if (_fullyWorn) return;
        if (metersDrawn <= 0f || !float.IsFinite(metersDrawn)) return;

        ApplyWearMeters(wearPerMeter * metersDrawn * Mathf.Max(0f, pressure));
    }

    /// <summary>
    /// Convert wear in meters (world) to a change in local scale along the chosen axis.
    /// The cube is considered fully worn once its axis scale reaches minAxisScale.
    /// </summary>
    private void ApplyWearMeters(float wearMeters)
    {
        if (_fullyWorn || wearMeters <= 0f)
            return;

        float axisScaleNow = GetAxisScale(transform.localScale);
        if (axisScaleNow <= minAxisScale + 1e-6f)
        {
            // Already at or below minimum scale
            OnFullyWorn();
            return;
        }

        // Convert meters to local scale delta along axis (relative to baseline world length)
        float deltaScale = wearMeters / Mathf.Max(1e-6f, _baseAxisLenWorld);

        // Shrink along the axis
        float newAxisScale = axisScaleNow - deltaScale;
        bool reachedMin = newAxisScale <= minAxisScale + 1e-6f;
        newAxisScale = Mathf.Max(minAxisScale, newAxisScale);

        // world length change for center shift (keep one end anchored)
        float worldDeltaLen = (axisScaleNow - newAxisScale) * _baseAxisLenWorld;

        if (adjustPositionToAnchor)
        {
            Vector3 axisWorldDir = AxisWorldDirection();
            transform.position += (anchorNegativeEnd ? -1f : 1f) * axisWorldDir * (worldDeltaLen * 0.5f);
        }
        // apply scale
        var ls = transform.localScale;
        switch (shrinkAxis)
        {
            case Axis.X: ls.x = newAxisScale; break;
            case Axis.Y: ls.y = newAxisScale; break;
            case Axis.Z: ls.z = newAxisScale; break;
        }
        transform.localScale = ls;

        // NEW: Snap to ground surface after shrinking (especially important for Y-axis shrinking)
        if (shrinkAxis == Axis.Y && IsOnSurface())
        {
            SnapToGroundSurface();
        }

        if (logWear)
        {
            Debug.Log($"[WearWhenMovingScaler] wear={wearMeters:F6}m, newScaleAxis={newAxisScale:F4}, worldΔ={worldDeltaLen:F6}m");
        }

        // If we reached or went below the minimum scale, mark as fully worn.
        if (reachedMin)
        {
            OnFullyWorn();
        }
    }

    /// <summary>
    /// Snaps the cube's bottom to the ground surface below it.
    /// Only works when shrinking on Y-axis (vertical).
    /// </summary>
    private void SnapToGroundSurface()
    {
        if (_col == null || Physics.gravity.sqrMagnitude < 0.0001f)
            return;

        Vector3 down = Physics.gravity.normalized;
        Bounds b = _col.bounds;
        
        // Cast from center downward to find the surface
        Vector3 origin = b.center;
        Vector3 ad = new Vector3(Mathf.Abs(down.x), Mathf.Abs(down.y), Mathf.Abs(down.z));
        float halfHeight = Vector3.Dot(ad, b.extents);
        float rayLength = halfHeight + 2f; // Cast further to ensure we hit the ground

        if (Physics.Raycast(origin, down, out RaycastHit hit, rayLength, surfaceMask, QueryTriggerInteraction.Ignore))
        {
            // Calculate where the bottom of the cube should be
            float bottomOffset = halfHeight;
            Vector3 desiredPosition = hit.point + down * (-bottomOffset) + hit.normal * 0.01f; // Small offset to prevent z-fighting
            
            // Only adjust Y position (or the component along gravity direction)
            Vector3 currentPos = transform.position;
            Vector3 gravityComponent = Vector3.Dot(currentPos - desiredPosition, down) * down;
            transform.position = currentPos - gravityComponent;

            if (debugGroundCheck)
            {
                Debug.DrawRay(origin, down * rayLength, Color.cyan, 0.1f);
                Debug.DrawLine(hit.point, desiredPosition, Color.yellow, 0.1f);
            }
        }
    }

    private void OnFullyWorn()
    {
        if (_fullyWorn)
            return;

        _fullyWorn = true;

        if (logWear)
            Debug.Log($"[WearWhenMovingScaler] Fully worn on {name}", this);

        if (triggerCubeDestroyedEvent)
        {
            EventManager.TriggerEvent(EventManager.GameEvent.CubeDestroyed, gameObject);
        }

        if (destroyWhenFullyWorn)
        {
            Destroy(gameObject);
        }
    }

    // --- ground check ---
    private bool IsOnSurface()
    {
        if (Physics.gravity.sqrMagnitude < 0.0001f)
            return false; // no gravity? treat as not grounded

        Vector3 down = Physics.gravity.normalized;
        Vector3 origin;
        float rayLength;

        if (_col != null)
        {
            Bounds b = _col.bounds;
            origin = b.center;

            // project extents onto gravity direction to get half-height along gravity
            Vector3 ad = new Vector3(Mathf.Abs(down.x), Mathf.Abs(down.y), Mathf.Abs(down.z));
            float half = Vector3.Dot(ad, b.extents);

            rayLength = half + groundCheckPadding;
        }
        else
        {
            origin = transform.position;
            rayLength = groundCheckPadding + 0.1f;
        }

        bool hit = Physics.Raycast(origin, down, out RaycastHit hitInfo, rayLength, surfaceMask, QueryTriggerInteraction.Ignore);

        if (debugGroundCheck)
        {
            Debug.DrawRay(origin, down * rayLength, hit ? Color.green : Color.red, 0.05f);
        }

        return hit;
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
