// FILEPATH: Assets/Scripts/PhysicsDrawing/CubeFallCatcher.cs
using UnityEngine;

/// <summary>
/// Big trigger placed under the tray/map:
/// - Tracks the cube's movement and stores the last TWO "safe" poses.
///   A safe pose is recorded every time the cube has moved at least sampleDistance
///   AND a valid surface is found below it.
/// - When the cube falls into this trigger, it teleports back to the most recent
///   safe pose. If that pose is too close to where it fell, it uses the previous one.
/// - Before teleporting, it re-projects the chosen safe point onto the current
///   surface using a raycast and aligns the cube to the surface normal.
/// 
/// This does not interfere with normal movement at all; it only runs when the cube
/// actually hits the catch volume or while passively tracking its path.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class CubeFallCatcher : MonoBehaviour
{
    [Header("Cube & Surface")]
    [Tooltip("The cube we want to rescue if it falls.")]
    [SerializeField] private Rigidbody cubeRigidbody;

    [Tooltip("Collider of the cube (used to estimate its half-height).")]
    [SerializeField] private Collider cubeCollider;

    [Tooltip("Layers considered as valid floor/tray surfaces.")]
    [SerializeField] private LayerMask surfaceMask;

    [Header("Safe Point Recording")]
    [Tooltip("Record a new safe point after the cube traveled at least this distance (meters) from the last recorded point.")]
    [SerializeField] private float sampleDistance = 0.25f;

    [Tooltip("Max ray distance used when projecting cube down to the surface when recording / teleporting.")]
    [SerializeField] private float maxRayDistance = 2.0f;

    [Tooltip("Small offset above the hit point along surface normal when placing the cube.")]
    [SerializeField] private float surfaceOffset = 0.002f;

    [Header("Teleport Distance Logic")]
    [Tooltip("If the latest safe point is closer than this to the fall position, use the older safe point instead.")]
    [SerializeField] private float minTeleportDistance = 0.7f;

    [Header("Fallback Respawn (optional)")]
    [Tooltip("If no safe points are available or raycast fails, use this respawn point.")]
    [SerializeField] private Transform fallbackRespawnPoint;

    [Header("Debug")]
    [SerializeField] private bool logRescue = false;
    [SerializeField] private bool drawDebugRays = false;

    // last two safe poses (A = most recent, B = previous)
    private Vector3    _safePosA;
    private Quaternion _safeRotA;
    private bool       _hasA;

    private Vector3    _safePosB;
    private Quaternion _safeRotB;
    private bool       _hasB;

    // last position used for distance-based sampling
    private Vector3 _lastSamplePos;
    private bool    _hasSamplePos;

    private void Reset()
    {
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void Awake()
    {
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;

        if (cubeRigidbody == null)
            Debug.LogWarning("[CubeFallCatcher] cubeRigidbody is not assigned.", this);

        if (cubeCollider == null && cubeRigidbody != null)
            cubeCollider = cubeRigidbody.GetComponent<Collider>();
    }

    private void FixedUpdate()
    {
        TrackSafePoints();
    }

    private void TrackSafePoints()
    {
        if (cubeRigidbody == null)
            return;

        Vector3 pos = cubeRigidbody.position;

        if (!_hasSamplePos)
        {
            _lastSamplePos = pos;
            _hasSamplePos  = true;
            TryRecordSafePoint(pos);
            return;
        }

        float dist = Vector3.Distance(pos, _lastSamplePos);
        if (!float.IsFinite(dist) || dist < sampleDistance)
            return;

        _lastSamplePos = pos;
        TryRecordSafePoint(pos);
    }

    /// <summary>
    /// Try to record a safe point at the given approximate cube position.
    /// We raycast along gravity to find the actual surface and compute the ideal center.
    /// </summary>
    private void TryRecordSafePoint(Vector3 approxCenter)
    {
        if (Physics.gravity.sqrMagnitude < 0.0001f)
            return;

        Vector3 down = Physics.gravity.normalized; // e.g. (0, -1, 0)
        Vector3 origin = approxCenter - down * 0.05f;

        if (drawDebugRays)
            Debug.DrawRay(origin, down * maxRayDistance, Color.green, 0.05f);

        if (!Physics.Raycast(origin, down, out RaycastHit hit, maxRayDistance, surfaceMask, QueryTriggerInteraction.Ignore))
        {
            // Not above a surface we care about -> don't record as safe
            return;
        }

        Vector3 center;
        Quaternion rot;
        if (!ComputeCenterAndRotationOnSurface(hit, out center, out rot))
            return;

        // Shift A -> B, then save new in A
        if (_hasA)
        {
            _safePosB = _safePosA;
            _safeRotB = _safeRotA;
            _hasB     = true;
        }

        _safePosA = center;
        _safeRotA = rot;
        _hasA     = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (cubeRigidbody == null)
            return;

        if (other.attachedRigidbody != cubeRigidbody)
            return;

        TeleportCubeBack();
    }

    private void TeleportCubeBack()
    {
        if (!TryGetTeleportTarget(out Vector3 targetPos, out Quaternion targetRot))
        {
            if (fallbackRespawnPoint != null)
            {
                targetPos = fallbackRespawnPoint.position;
                targetRot = fallbackRespawnPoint.rotation;
            }
            else
            {
                // Nothing we can do.
                if (logRescue)
                    Debug.LogWarning("[CubeFallCatcher] No safe points or fallback respawn. Cannot rescue cube.", this);
                return;
            }
        }

        cubeRigidbody.transform.position = targetPos;
        cubeRigidbody.transform.rotation = targetRot;
        cubeRigidbody.linearVelocity = Vector3.zero;
        cubeRigidbody.angularVelocity = Vector3.zero;

        if (logRescue)
            Debug.Log($"[CubeFallCatcher] Cube rescued to {targetPos}", this);
    }

    /// <summary>
    /// Decide which safe point to use (A or B), and re-project it onto the current surface.
    /// </summary>
    private bool TryGetTeleportTarget(out Vector3 targetPos, out Quaternion targetRot)
    {
        targetPos = Vector3.zero;
        targetRot = Quaternion.identity;

        bool haveAny = _hasA || _hasB;
        if (!haveAny)
            return false;

        Vector3 fallPos = cubeRigidbody.position;

        // Decide which saved point to start from
        Vector3 basePos;
        Quaternion baseRot;

        if (_hasA && _hasB)
        {
            float distToA = Vector3.Distance(fallPos, _safePosA);
            if (!float.IsFinite(distToA)) distToA = float.MaxValue;

            if (distToA >= minTeleportDistance)
            {
                basePos = _safePosA;
                baseRot = _safeRotA;
            }
            else
            {
                basePos = _safePosB;
                baseRot = _safeRotB;
            }
        }
        else if (_hasA)
        {
            basePos = _safePosA;
            baseRot = _safeRotA;
        }
        else // only B
        {
            basePos = _safePosB;
            baseRot = _safeRotB;
        }

        // Re-project onto current surface so we match the new tilt
        if (Physics.gravity.sqrMagnitude < 0.0001f)
            return false;

        Vector3 down = Physics.gravity.normalized;
        Vector3 origin = basePos - down * 0.05f;

        if (drawDebugRays)
            Debug.DrawRay(origin, down * maxRayDistance, Color.magenta, 0.1f);

        if (!Physics.Raycast(origin, down, out RaycastHit hit, maxRayDistance, surfaceMask, QueryTriggerInteraction.Ignore))
        {
            // Surface under saved point no longer exists? Fallback to base pose.
            targetPos = basePos;
            targetRot = baseRot;
            return true;
        }

        return ComputeCenterAndRotationOnSurface(hit, out targetPos, out targetRot);
    }

    /// <summary>
    /// From a raycast hit, compute where the cube center should be and a rotation
    /// whose up axis aligns with the surface normal.
    /// </summary>
    private bool ComputeCenterAndRotationOnSurface(RaycastHit hit, out Vector3 center, out Quaternion rotation)
    {
        center = Vector3.zero;
        rotation = Quaternion.identity;

        if (cubeCollider == null)
            return false;

        // Estimate half-size along the surface normal
        Vector3 n = hit.normal.normalized;
        Bounds b = cubeCollider.bounds;
        Vector3 ext = b.extents;
        Vector3 ad = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
        float half = Vector3.Dot(ad, ext);

        center = hit.point + n * (half + surfaceOffset);

        // Build a rotation where 'up' = normal and 'forward' is projected from the cube's current forward
        Vector3 forward = cubeRigidbody.transform.forward;
        Vector3 projectedForward = Vector3.ProjectOnPlane(forward, n);
        if (projectedForward.sqrMagnitude < 1e-6f)
        {
            // Fallback if forward is nearly parallel to normal
            projectedForward = Vector3.Cross(n, Vector3.right);
            if (projectedForward.sqrMagnitude < 1e-6f)
                projectedForward = Vector3.Cross(n, Vector3.forward);
        }

        rotation = Quaternion.LookRotation(projectedForward.normalized, n);
        return true;
    }
}
