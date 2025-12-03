// FILEPATH: Assets/Scripts/PhysicsDrawing/SurfaceAlignAndSlide.cs
using UnityEngine;

/// <summary>
/// Makes the cube:
/// - Detect the surface underneath it (Raycast along gravity).
/// - Align its transform.up with the surface normal.
/// - Stay slightly above the surface (offset + safety margin).
/// - Remove velocity along the surface normal and slide downhill
///   (gravity projected onto the surface).
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(Collider))]
[DisallowMultipleComponent]
public class SurfaceAlignAndSlide : MonoBehaviour
{
    [Header("Surface")]
    [Tooltip("Which layers count as a surface/tray.")]
    [SerializeField] private LayerMask surfaceMask = ~0;

    [Tooltip("How far below the cube to check for a surface.")]
    [SerializeField] private float extraSearchDistance = 0.3f;

    [Tooltip("Base offset above the surface (prevents Z-fighting).")]
    [SerializeField] private float surfaceOffset = 0.001f;

    [Tooltip("Additional safety separation above the surface to prevent sinking in builds.")]
    [SerializeField] private float planeSeparation = 0.0015f;

    [Header("Rotation Align")]
    [Tooltip("How fast the cube rotates to match the surface normal.")]
    [SerializeField] private float alignSpeed = 15f;

    [Tooltip("If true – instantly snaps rotation instead of Slerp (much more 'sticky').")]
    [SerializeField] private bool snapRotation = false;

    [Header("Downhill Sliding")]
    [Tooltip("When enabled – velocity slides in the downhill direction (gravity projected onto the surface).")]
    [SerializeField] private bool enableDownhillSliding = true;

    [Tooltip("How quickly the movement direction rotates toward the downhill direction.")]
    [SerializeField] private float downhillTurnSpeed = 8f;

    [Header("Debug")]
    [SerializeField] private bool drawDebug = false;

    private Rigidbody _rb;
    private Collider _col;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _col = GetComponent<Collider>();

        // Important: do NOT freeze rotation — this script controls rotation manually.
        _rb.constraints &= ~(RigidbodyConstraints.FreezeRotationX |
                             RigidbodyConstraints.FreezeRotationY |
                             RigidbodyConstraints.FreezeRotationZ);
    }

    private void FixedUpdate()
    {
        if (_rb == null || _col == null) return;
        if (Physics.gravity.sqrMagnitude < 1e-6f) return;

        Vector3 down = Physics.gravity.normalized;
        Bounds b = _col.bounds;

        // Like in StickToSurface — compute where to start a ray toward gravity.
        Vector3 absDown = new Vector3(Mathf.Abs(down.x), Mathf.Abs(down.y), Mathf.Abs(down.z));
        float halfAlongDown = Vector3.Dot(absDown, b.extents);

        // Start slightly *below* the cube (but still above the surface).
        Vector3 start = b.center - (-down) * (halfAlongDown + 0.01f);
        float rayLength = halfAlongDown + extraSearchDistance;

        if (drawDebug)
        {
            Debug.DrawRay(start, down * rayLength, Color.cyan, 0.05f);
        }

        if (!Physics.Raycast(start, down, out RaycastHit hit, rayLength, surfaceMask,
                QueryTriggerInteraction.Ignore))
        {
            // No surface beneath — allow normal physics (falling, etc.)
            return;
        }

        Vector3 n = hit.normal.normalized;

        // --- 1) Align rotation to the surface normal ---

        Quaternion targetRotation =
            Quaternion.FromToRotation(transform.up, n) * transform.rotation;

        if (snapRotation)
        {
            _rb.MoveRotation(targetRotation);
        }
        else
        {
            Quaternion smoothed = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                alignSpeed * Time.fixedDeltaTime);

            _rb.MoveRotation(smoothed);
        }

        // Update bounds after the rotation
        b = _col.bounds;

        // --- 2) Maintain height slightly above the surface ---

        Vector3 absN = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
        float halfAlongNormal = Vector3.Dot(absN, b.extents);

        // Add both surfaceOffset and planeSeparation so the cube does NOT sit *exactly* on the surface.
        float desiredDistFromPlane = halfAlongNormal + surfaceOffset + planeSeparation;
        Vector3 currentCenter = b.center;

        float currentDistFromPlane = Vector3.Dot(n, currentCenter - hit.point);
        float deltaDist = desiredDistFromPlane - currentDistFromPlane;

        Vector3 deltaWorld = n * deltaDist;
        _rb.MovePosition(_rb.position + deltaWorld);

        // --- 3) Remove velocity along the normal (prevents sticking or popping off the surface) ---

        Vector3 v = _rb.linearVelocity;
        float vAlong = Vector3.Dot(v, n);
        Vector3 vTangent = v - n * vAlong;   // keep only component inside the surface plane

        // --- 4) Turn velocity toward the downhill direction (optional) ---

        if (enableDownhillSliding)
        {
            // Downhill = gravity projected onto the surface.
            Vector3 downhill = Vector3.ProjectOnPlane(Physics.gravity, n);

            if (downhill.sqrMagnitude > 1e-6f)
            {
                float tangentSpeed = vTangent.magnitude;

                if (tangentSpeed > 0.0001f)
                {
                    Vector3 targetDir = downhill.normalized;
                    Vector3 currentDir = vTangent.normalized;

                    Vector3 newDir = Vector3.Slerp(
                        currentDir,
                        targetDir,
                        downhillTurnSpeed * Time.fixedDeltaTime);

                    vTangent = newDir * tangentSpeed;
                }
            }
        }

        // Final velocity — tangent to the surface and optionally steered downhill.
        _rb.linearVelocity = vTangent;
    }
}
