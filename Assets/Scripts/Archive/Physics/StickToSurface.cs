// FILEPATH: Assets/Scripts/PhysicsDrawing/StickToSurface.cs
using UnityEngine;

/// <summary>
/// Hard-locks this rigidbody onto the surface under it:
/// - Each FixedUpdate: raycast along gravity to find the tray.
/// - Compute the ideal center so the bottom of the collider sits exactly on the surface.
/// - Snap the rigidbody to that position (no tolerance, no partial move).
/// - Remove velocity along the surface normal so it can't dig in or fly off.
/// 
/// Result: cube always sits flush on the tray and only moves *along* the tray.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class StickToSurface : MonoBehaviour
{
    [Header("Surface")]
    [Tooltip("Layers considered as the tray / paintable surface.")]
    [SerializeField] private LayerMask surfaceMask = ~0;

    [Tooltip("Extra offset above the surface along the surface normal (meters).")]
    [SerializeField] private float surfaceOffset = 0.001f;

    [Tooltip("Extra ray length beyond the cube bottom to search for a surface (meters).")]
    [SerializeField] private float extraSearchDistance = 0.2f;

    [Header("Debug")]
    [SerializeField] private bool drawDebug = false;

    Rigidbody _rb;
    Collider _col;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _col = GetComponent<Collider>();

        // We want physics but will override the normal motion along the tray normal.
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }

    void FixedUpdate()
    {
        if (_rb == null || _col == null) return;
        if (Physics.gravity.sqrMagnitude < 1e-6f) return;

        Vector3 down = Physics.gravity.normalized;

        Bounds b = _col.bounds;

        // How far the collider extends along gravity (half-height along -down)
        Vector3 absDown = new Vector3(Mathf.Abs(down.x), Mathf.Abs(down.y), Mathf.Abs(down.z));
        float halfAlongDown = Vector3.Dot(absDown, b.extents);

        // Start ray a bit *above* the cube along opposite of gravity
        Vector3 start = b.center - (-down) * (halfAlongDown + 0.01f);
        float rayLength = halfAlongDown + extraSearchDistance;

        if (drawDebug)
        {
            Debug.DrawRay(start, down * rayLength, Color.magenta, 0.05f);
        }

        if (!Physics.Raycast(start, down, out RaycastHit hit, rayLength, surfaceMask,
                QueryTriggerInteraction.Ignore))
        {
            // No surface found under us in range -> do nothing, cube is "falling".
            return;
        }

        Vector3 n = hit.normal.normalized;

        // Project collider bounds center onto the plane normal to get proper half-height in that direction
        Vector3 absN = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
        float halfAlongNormal = Vector3.Dot(absN, b.extents);

        // Ideal center so the bottom face just touches the surface (plus tiny offset)
        float desiredDistFromPlane = halfAlongNormal + surfaceOffset;
        Vector3 currentCenter = b.center;

        float currentDistFromPlane = Vector3.Dot(n, currentCenter - hit.point);
        float deltaDist = desiredDistFromPlane - currentDistFromPlane;

        // Snap rigidbody position so collider center is exactly where we want it
        Vector3 deltaWorld = n * deltaDist;
        _rb.MovePosition(_rb.position + deltaWorld);

        // Also kill any velocity along the normal so it can't push in or bounce away
        Vector3 v = _rb.linearVelocity;
        float vAlong = Vector3.Dot(v, n);
        _rb.linearVelocity = v - n * vAlong;  // keep only tangential component
    }
}
