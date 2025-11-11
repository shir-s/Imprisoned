/*
// FILEPATH: Assets/Scripts/PhysicsDrawing/MouseBrushTool.cs
using UnityEngine;

/// <summary>
/// Click the tool to "take" it. While held, mouse motion moves the tool over a paint plane.
/// The tool DOES NOT rotate while painting. You paint using a single "contact side"
/// (set via contactHandle), projected onto the plane. Paint is emitted via StrokeMesh.
/// 
/// Dependencies: StrokeMesh (already in your project).
/// Does NOT require Rigidbody. Tool is moved kinematically.
/// </summary>
[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public class MouseBrushTool : MonoBehaviour
{
    [Header("Surface (paint plane)")]
    [SerializeField] private Transform paintPlane;               // The plane you paint on. Its up = plane normal.

    [Header("Brush geometry")]
    [SerializeField] private Transform contactHandle;            // A child Transform marking the side that should touch the plane. If null, uses this transform.
    [SerializeField] private float brushWidth = 0.01f;           // Width in meters of the side that paints (e.g., 1cm).
    [SerializeField] private float minPointSpacing = 0.0015f;    // Minimum meters between stroke points.

    [Header("Stroke look")]
    [SerializeField] private Material strokeMaterial;            // Unlit material; defaults to black if null.

    [Header("Input")]
    [SerializeField] private int mouseButton = 0;                // 0 = left mouse button

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private Color handleGizmoColor = new Color(1f, 0.6f, 0f, 0.9f);
    [SerializeField] private Color planeGizmoColor = new Color(0f, 0.8f, 1f, 0.6f);

    // Internal
    private Collider _collider;
    private Camera _cam;
    private bool _isHeld;
    private Quaternion _frozenRotation;
    private StrokeMesh _stroke;

    // Cached for spacing & plane math
    private Vector3 _lastPaintPoint;
    private bool _haveLastPoint;

    void Awake()
    {
        _collider = GetComponent<Collider>();
        _cam = Camera.main;

        if (paintPlane == null)
            Debug.LogWarning("[MouseBrushTool] Paint plane not assigned. Set 'paintPlane' to your canvas transform.");

        if (strokeMaterial == null)
        {
            var m = new Material(Shader.Find("Unlit/Color"));
            m.color = Color.black;
            strokeMaterial = m;
        }

        // Create/attach a StrokeMesh child (one per tool)
        const string childName = "StrokeMesh (Brush)";
        var child = transform.Find(childName);
        GameObject go;
        if (child == null)
        {
            go = new GameObject(childName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
        }
        else go = child.gameObject;

        _stroke = go.GetComponent<StrokeMesh>();
        if (_stroke == null) _stroke = go.AddComponent<StrokeMesh>();
        _stroke.Init(strokeMaterial, Mathf.Max(0.0002f, minPointSpacing));

        if (contactHandle == null) contactHandle = transform;
    }

    void Update()
    {
        if (_cam == null) _cam = Camera.main;

        // Mouse down: either pick the tool (if clicked on it) or start painting if already held
        if (Input.GetMouseButtonDown(mouseButton))
        {
            if (!_isHeld)
            {
                if (ClickedThisTool())
                {
                    _isHeld = true;
                    _frozenRotation = transform.rotation; // LOCK rotation while painting
                    _haveLastPoint = false;
                }
            }
        }

        // Mouse up: release the tool
        if (Input.GetMouseButtonUp(mouseButton))
        {
            _isHeld = false;
        }

        if (!_isHeld || paintPlane == null) return;

        // 1) Project mouse to plane
        if (!RayToPlane(_cam, paintPlane.position, paintPlane.up, out var planeHit))
            return;

        // 2) Move WITHOUT rotation: shift tool so that the contactHandle sits exactly on the planeHit
        Vector3 handleWorld = contactHandle.position;
        Vector3 delta = planeHit - handleWorld;
        transform.position += delta;

        // Freeze rotation strictly (no drift)
        transform.rotation = _frozenRotation;

        // 3) Emit paint at the contact point with plane normal
        Vector3 contactPoint = contactHandle.position; // now should be exactly on the plane
        Vector3 planeNormal = paintPlane.up;

        if (!_haveLastPoint || (contactPoint - _lastPaintPoint).sqrMagnitude >= (minPointSpacing * minPointSpacing))
        {
            _stroke.AddPoint(contactPoint, planeNormal, brushWidth);
            _lastPaintPoint = contactPoint;
            _haveLastPoint = true;
        }
    }

    private bool ClickedThisTool()
    {
        if (_cam == null) return false;
        Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
        return _collider.Raycast(ray, out _, 1000f);
    }

    private static bool RayToPlane(Camera cam, Vector3 planePoint, Vector3 planeNormal, out Vector3 hit)
    {
        if (cam == null) { hit = default; return false; }
        var plane = new Plane(planeNormal.sqrMagnitude > 0f ? planeNormal.normalized : Vector3.up, planePoint);
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (plane.Raycast(ray, out float enter))
        {
            hit = ray.GetPoint(enter);
            return true;
        }
        hit = default;
        return false;
    }

    void OnDrawGizmos()
    {
        if (!drawGizmos) return;

        // Draw contact handle & normal
        Transform h = contactHandle == null ? transform : contactHandle;
        Gizmos.color = handleGizmoColor;
        Gizmos.DrawSphere(h.position, 0.004f);

        if (paintPlane != null)
        {
            Gizmos.color = planeGizmoColor;
            Vector3 n = paintPlane.up;
            Gizmos.DrawLine(h.position, h.position + n * 0.04f);
        }
    }
}
*/
