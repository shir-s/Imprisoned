// FILEPATH: Assets/Scripts/PhysicsDrawing/CubeSurfaceSafety.cs
using UnityEngine;

/// <summary>
/// Safety net for the drawing cube:
/// - Gently keeps it aligned with the paintable surface using a raycast and a small snap.
/// - Only corrects small gaps (micro-snap), so the cube can fall naturally at start.
/// - If the cube falls too far away from the surface, teleports it back to the last safe position.
/// Designed to work with a tilting surface.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class CubeSurfaceSafety : MonoBehaviour
{
    [Header("Surface Detection")]
    [Tooltip("Layers that are considered valid surfaces (your tray / floor).")]
    [SerializeField] private LayerMask surfaceMask;

    [Tooltip("Max distance along ray we search for the surface to snap to.")]
    [SerializeField] private float snapRayDistance = 0.4f;

    [Tooltip("Small offset above the hit point along surface normal.")]
    [SerializeField] private float surfaceOffset = 0.001f;

    [Tooltip("Maximum distance we are allowed to move the cube in one snap.\n" +
             "Prevents big teleports and lets gravity handle the initial fall.")]
    [SerializeField] private float maxSnapMovePerFrame = 0.05f;

    [Header("Rescue From Fall")]
    [Tooltip("If true, teleport the cube back to last safe position when it falls too far.")]
    [SerializeField] private bool enableRescue = true;

    [Tooltip("If the cube moves farther than this from last safe position, we consider it 'fallen'.")]
    [SerializeField] private float maxDistanceFromSafe = 1.5f;

    [Tooltip("Optional hard clamp on world Y: if cube goes below this, rescue it.")]
    [SerializeField] private float rescueMinY = -5f;

    [Header("Velocity Tuning")]
    [Tooltip("If true, remove ANY velocity along the surface normal when snapping (both up and down).")]
    [SerializeField] private bool killNormalVelocityWhenSnapping = true;

    [Header("Debug")]
    [SerializeField] private bool drawDebugRay = false;
    [SerializeField] private bool logRescue = false;

    private Rigidbody _rb;
    private Collider  _col;

    private Vector3    _lastSafePosition;
    private Quaternion _lastSafeRotation;
    private bool       _hasSafe;

    private void Awake()
    {
        _rb  = GetComponent<Rigidbody>();
        _col = GetComponent<Collider>();
    }

    private void Start()
    {
        // Treat starting position as safe
        _lastSafePosition = transform.position;
        _lastSafeRotation = transform.rotation;
        _hasSafe = true;
    }

    private void FixedUpdate()
    {
        HandleSnapToSurface();
        HandleRescueIfFallen();
    }

    private void HandleSnapToSurface()
    {
        // Use gravity direction as "down" towards the surface
        if (Physics.gravity.sqrMagnitude < 0.0001f)
            return;

        Vector3 rayDir = Physics.gravity.normalized; // e.g. (0, -1, 0)
        // Start a bit above the cube center (opposite to ray direction)
        Vector3 start = transform.position - rayDir * 0.05f;

        if (drawDebugRay)
            Debug.DrawRay(start, rayDir * snapRayDistance, Color.yellow, 0.02f);

        if (!Physics.Raycast(start, rayDir, out RaycastHit hit, snapRayDistance,
                             surfaceMask, QueryTriggerInteraction.Ignore))
        {
            // No surface found near us this frame
            return;
        }

        // Approx half-height along "down" direction (gravity)
        float halfHeight = ComputeHalfHeightAlongDirection(rayDir);

        // Desired center position: surface point + "up" from surface along opposite of rayDir
        // rayDir is down, so center = hit.point - rayDir * (halfHeight + offset)
        Vector3 desiredPos = hit.point - rayDir * (halfHeight + surfaceOffset);

        // Don't allow big teleports: only micro-adjust when already close
        float moveDist = Vector3.Distance(transform.position, desiredPos);
        if (moveDist > maxSnapMovePerFrame)
        {
            // Too far: let gravity / normal physics handle it (e.g. at game start)
            return;
        }

        // Snap position
        transform.position = desiredPos;

        // Optionally remove all velocity along the surface normal so we stop bobbing
        if (killNormalVelocityWhenSnapping && _rb != null)
        {
            Vector3 v = _rb.linearVelocity;
            float vn = Vector3.Dot(v, hit.normal);
            v -= hit.normal * vn; // remove both upward and downward component
            _rb.linearVelocity = v;
        }

        // Update safe position only when we're actually on/near the surface
        _lastSafePosition = transform.position;
        _lastSafeRotation = transform.rotation;
        _hasSafe = true;
    }

    private void HandleRescueIfFallen()
    {
        if (!enableRescue || !_hasSafe)
            return;

        float dist = Vector3.Distance(transform.position, _lastSafePosition);
        bool tooFar = dist > maxDistanceFromSafe;
        bool tooLow = transform.position.y < rescueMinY;

        if (!tooFar && !tooLow)
            return;

        // Teleport back to last safe pose
        transform.position = _lastSafePosition;
        transform.rotation = _lastSafeRotation;

        if (_rb != null)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }

        if (logRescue)
        {
            Debug.Log($"[CubeSurfaceSafety] Rescued cube back to safe position at {_lastSafePosition}", this);
        }
    }

    /// <summary>
    /// Approximate half-size of the collider along a given direction,
    /// using collider.bounds extents.
    /// </summary>
    private float ComputeHalfHeightAlongDirection(Vector3 dir)
    {
        dir = dir.normalized;
        if (_col == null)
            return 0.05f;

        Bounds b = _col.bounds;
        Vector3 ext = b.extents;

        // Support function of an AABB along dir: dot(abs(dir), extents)
        Vector3 ad = new Vector3(Mathf.Abs(dir.x), Mathf.Abs(dir.y), Mathf.Abs(dir.z));
        float support = Vector3.Dot(ad, ext);

        return support;
    }
}
