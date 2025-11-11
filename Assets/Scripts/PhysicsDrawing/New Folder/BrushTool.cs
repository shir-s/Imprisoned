// FILEPATH: Assets/Scripts/Painting/BrushTool.cs
using UnityEngine;

/// <summary>
/// A simple “cube brush” tool that you can pick up and paint with.
/// While painting, it shrinks along the axis perpendicular to the current plane,
/// keeps the contact face aligned & at a constant distance from the plane,
/// and exposes a BrushDiameter used by strokes/shaders.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class BrushTool : MonoBehaviour
{
    [Header("Visuals & Material")]
    [SerializeField] private Material strokeMaterial;
    [SerializeField] private Color    brushColor = Color.black;

    [Header("Wear & Contact")]
    [Tooltip("Meters of length lost per meter of drawing.")]
    [SerializeField] private float wearPerMeter = 0.25f;
    [Tooltip("Small separation to keep the brush above the plane.")]
    [SerializeField] private float surfaceOffset = 0.0001f;
    [Tooltip("Minimal local scale allowed on each axis (meters).")]
    [SerializeField] private Vector3 minLocalScale = new Vector3(0.002f, 0.002f, 0.002f);

    [Header("Orientation")]
    [Tooltip("When snapping to a plane, the brush's painting face points along +UpLocal.")]
    [SerializeField] private Vector3 upLocal = Vector3.up;

    Rigidbody _rb;
    Collider  _col;
    bool      _isHeld;
    Vector3   _cachedLocalScale;     // for constraints / clamping
    Vector3   _lastPlaneNormalWS;    // world normal used when painting
    Vector3   _lastContactPointWS;   // world contact point on plane

    // ===== Public API used by MouseBrushPainter =====
    public Material StrokeMaterial => strokeMaterial;
    public Color    BrushColor     => brushColor;

    /// <summary> Diameter in meters across the painting width (the narrower in-plane dimension). </summary>
    public float BrushDiameter
    {
        get
        {
            // Heuristic: while painting we choose the smaller of the two in-plane axes
            // (axes perpendicular to the dominant normal).
            var n = _lastPlaneNormalWS.sqrMagnitude > 0f ? _lastPlaneNormalWS : transform.up;
            Axis dominant = DominantAxis(n, transform);
            Vector3 s = transform.lossyScale;

            switch (dominant)
            {
                case Axis.Right:   // plane normal ≈ ±right → painting plane is YZ → width=min(y,z)
                    return Mathf.Min(s.y, s.z);
                case Axis.Up:      // plane normal ≈ ±up    → painting plane is XZ → width=min(x,z)
                    return Mathf.Min(s.x, s.z);
                case Axis.Forward: // plane normal ≈ ±fwd   → painting plane is XY → width=min(x,y)
                    return Mathf.Min(s.x, s.y);
                default: return Mathf.Min(s.x, Mathf.Min(s.y, s.z));
            }
        }
    }

    void Awake()
    {
        _rb  = GetComponent<Rigidbody>();
        _col = GetComponent<Collider>();
        _cachedLocalScale = transform.localScale;
    }

    // ===== Pick up / put down =====
    public void OnPickedUp(Vector3 planePoint, Vector3 planeNormal)
    {
        _isHeld = true;
        _lastPlaneNormalWS = planeNormal.normalized;
        _lastContactPointWS = planePoint;

        if (_rb)
        {
            _rb.isKinematic = true;       // user controls it; no gravity while held
            _rb.velocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }
        SnapToPaintingPose(planePoint, planeNormal);
    }

    public void OnPutDown()
    {
        _isHeld = false;

        // Place slightly above the last plane to avoid interpenetration, so it won't fall through
        MaintainContact(_lastContactPointWS, _lastPlaneNormalWS);

        if (_rb)
        {
            _rb.isKinematic = false;      // let physics resume
            // small upward nudge to ensure separation from the plane collider
            _rb.position += _lastPlaneNormalWS * (surfaceOffset * 2f);
        }
    }

    /// <summary>
    /// Aligns brush so its upLocal faces the plane normal and its contact face sits on the plane.
    /// </summary>
    public void SnapToPaintingPose(Vector3 planePoint, Vector3 planeNormal)
    {
        _lastPlaneNormalWS = planeNormal.normalized;
        _lastContactPointWS = planePoint;

        // Rotate so upLocal (in brush space) matches the plane normal.
        // (This is fine to do with quaternions here.)
        Quaternion fromUp = Quaternion.FromToRotation(transform.TransformDirection(upLocal), _lastPlaneNormalWS);
        transform.rotation = fromUp * transform.rotation;

        // Slide into correct height so the contact face sits on the plane
        MaintainContact(planePoint, _lastPlaneNormalWS);
    }

    /// <summary>
    /// Apply wear (shrink) along the axis aligned with the plane normal.
    /// Keeps the contact face glued to the plane by repositioning along the normal.
    /// </summary>
    public void ApplyWearAlongPlane(float metersDrawn, Vector3 planePoint, Vector3 planeNormal)
    {
        if (metersDrawn <= 0f) return;

        _lastPlaneNormalWS = planeNormal.normalized;
        _lastContactPointWS = planePoint;

        Axis shrinkAxis = DominantAxis(_lastPlaneNormalWS, transform);

        Vector3 ls = transform.localScale;
        float  delta = wearPerMeter * metersDrawn;
        switch (shrinkAxis)
        {
            case Axis.Right:   ls.x = Mathf.Max(minLocalScale.x, ls.x - delta); break;
            case Axis.Up:      ls.y = Mathf.Max(minLocalScale.y, ls.y - delta); break;
            case Axis.Forward: ls.z = Mathf.Max(minLocalScale.z, ls.z - delta); break;
        }
        transform.localScale = ls;

        // After scale changed, keep face on the plane
        MaintainContact(planePoint, _lastPlaneNormalWS);
    }

    // ===== Helpers =====

    enum Axis { Right, Up, Forward }

    static Axis DominantAxis(Vector3 worldDir, Transform t)
    {
        // Which local axis aligns best with worldDir?
        float xr = Mathf.Abs(Vector3.Dot(worldDir.normalized, t.right));
        float yu = Mathf.Abs(Vector3.Dot(worldDir.normalized, t.up));
        float zf = Mathf.Abs(Vector3.Dot(worldDir.normalized, t.forward));
        if (xr > yu && xr > zf) return Axis.Right;
        if (yu > zf) return Axis.Up;
        return Axis.Forward;
    }

    /// <summary>
    /// Move center so the OBB face that points along +planeNormal lies on planePoint.
    /// </summary>
    void MaintainContact(Vector3 planePoint, Vector3 planeNormal)
    {
        Vector3 n = planeNormal.normalized;

        // Oriented-box half extents in local space (unit cube assumed as mesh base).
        Vector3 h = transform.localScale * 0.5f;

        // Contribution of each local axis projected onto n
        float e =
            Mathf.Abs(Vector3.Dot(n, transform.right))   * h.x +
            Mathf.Abs(Vector3.Dot(n, transform.up))      * h.y +
            Mathf.Abs(Vector3.Dot(n, transform.forward)) * h.z;

        // Center should be offset by +n * (e + offset) from the contact point.
        Vector3 targetPos = planePoint + n * (e + surfaceOffset);

        if (_rb && _rb.isKinematic)
            _rb.position = targetPos;
        else
            transform.position = targetPos;
    }
}
