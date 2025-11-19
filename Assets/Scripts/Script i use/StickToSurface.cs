// FILEPATH: Assets/Scripts/PhysicsDrawing/StickToSurface.cs
using UnityEngine;

/// <summary>
/// Forces the cube to "stick" to the surface under it at a mostly constant distance
/// along the surface normal, without fighting normal physics too much.
///
/// How it works:
/// - Each FixedUpdate, raycast down (using gravity direction) to find the surface.
/// - Compute where the collider center *should* be so the bottom face sits on the surface.
/// - If the cube is clearly FLOATING above that point, move it closer along the normal.
/// - Optionally damp velocity that pushes the cube AWAY from the surface.
/// 
/// Important: we only correct when the cube is floating too high, and we leave
/// penetration / minor offsets to the normal physics solver to avoid jitter.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class StickToSurface : MonoBehaviour
{
    [Header("Surface")]
    [Tooltip("Layers considered as the drawing / tray surface.")]
    [SerializeField] private LayerMask surfaceMask = ~0;

    [Tooltip("How far below the cube we search for a surface (world meters).")]
    [SerializeField] private float searchDistance = 0.2f;

    [Tooltip("Extra offset above the surface along its normal.")]
    [SerializeField] private float surfaceOffset = 0.001f;

    [Header("Correction")]
    [Tooltip("If the cube is floating above the ideal height by more than this, we snap it down (meters).")]
    [SerializeField] private float floatTolerance = 0.001f;

    [Tooltip("Max distance we correct in a single step along the normal (meters).")]
    [SerializeField] private float maxCorrectionPerStep = 0.02f;

    [Header("Velocity Adjustment")]
    [Tooltip("If true, damp velocity that pushes the cube AWAY from the surface normal.")]
    [SerializeField] private bool dampAwayFromSurfaceVelocity = true;

    [Tooltip("Strength of damping for velocity away from the surface (0..1). 1 = fully remove.")]
    [SerializeField, Range(0f, 1f)] private float awayVelocityDamp = 1f;

    [Header("Debug")]
    [SerializeField] private bool drawDebugRays = false;

    private Rigidbody _rb;
    private Collider _col;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _col = GetComponent<Collider>();
    }

    private void FixedUpdate()
    {
        if (_rb == null || _col == null) return;
        if (Physics.gravity.sqrMagnitude < 0.0001f) return; // no gravity => we don't know "down"

        Vector3 down = Physics.gravity.normalized;

        // Collider bounds in world space
        Bounds b = _col.bounds;

        // Ray origin: slightly above the center along -down, so we are clearly outside the surface.
        Vector3 origin = b.center - down * 0.1f;
        float rayLength = 0.1f + searchDistance;

        if (drawDebugRays)
        {
            Debug.DrawRay(origin, down * rayLength, Color.cyan, 0.02f);
        }

        if (!Physics.Raycast(origin, down, out RaycastHit hit, rayLength, surfaceMask, QueryTriggerInteraction.Ignore))
        {
            // No surface under us in search range -> treat as actually in the air, don't snap.
            return;
        }

        Vector3 n = hit.normal.normalized;

        // How far the collider extends along this normal (half-height in that direction)
        Vector3 absN = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
        float halfAlongNormal = Vector3.Dot(absN, b.extents);

        // Where the center SHOULD be: surface point + normal * (half height + offset)
        float desiredDistFromPlane = halfAlongNormal + surfaceOffset;
        Vector3 currentCenter = b.center;

        // Signed distance from current center to the surface plane along the normal
        float currentDistFromPlane = Vector3.Dot(n, currentCenter - hit.point);

        // If currentDist > desiredDist => we are ABOVE the desired height (floating)
        float excess = currentDistFromPlane - desiredDistFromPlane;

        // We only correct if we're clearly floating (excess greater than tolerance).
        if (excess > floatTolerance)
        {
            // Move center toward the plane along -normal by at most maxCorrectionPerStep.
            float moveDist = Mathf.Min(excess, maxCorrectionPerStep);
            Vector3 delta = -n * moveDist;

            // Use MovePosition to cooperate with physics
            _rb.MovePosition(_rb.position + delta);
        }

        // Optional: damp velocity AWAY from the surface (only the "popping off" component)
        if (dampAwayFromSurfaceVelocity && awayVelocityDamp > 0f)
        {
            Vector3 v = _rb.linearVelocity;

            // Component along the normal (positive = moving away from the plane)
            float vAlong = Vector3.Dot(v, n);
            if (vAlong > 0f) // only damp if moving AWAY from surface
            {
                Vector3 vNormal = n * vAlong;
                Vector3 vTangent = v - vNormal;
                Vector3 newVNormal = vNormal * (1f - awayVelocityDamp);
                _rb.linearVelocity = vTangent + newVNormal;
            }
        }
    }
}
