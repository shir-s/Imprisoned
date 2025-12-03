// FILEPATH: Assets/Scripts/AI/Behaviors/TravelBehavior.cs
using UnityEngine;

/// <summary>
/// Travel behavior:
/// - Moves between random target points in a defined XZ area.
/// - Never immediately travels back to (or near) the previous target.
/// - Soft-avoids obstacles on specified layers (e.g. water) by steering.
/// - Treats soft obstacles more strongly when inside them (escape first).
/// - Hard-avoids "wall" layers by re-picking a target if the direct path is blocked.
/// - Uses simple stuck detection to avoid jittering against obstacles.
/// 
/// This is NOT full pathfinding, just "intelligent roaming".
/// </summary>
[DisallowMultipleComponent]
public class TravelBehavior : MonoBehaviour, IEnemyBehavior
{
    [Header("Behavior Priority")]
    [Tooltip("Higher value = higher priority. Travel is usually low (e.g. 0) or slightly above Wander.")]
    [SerializeField] private int priority = 0;

    [Header("Movement")]
    [Tooltip("Speed while traveling (units/sec).")]
    [SerializeField] private float travelSpeed = 2.0f;

    [Tooltip("Distance at which we consider a target 'reached'.")]
    [SerializeField] private float targetReachThreshold = 0.3f;

    [Tooltip("Minimum distance between new target and the previous target, so we don't ping-pong.")]
    [SerializeField] private float minDistanceBetweenTargets = 3f;

    [Header("Travel Area (XZ)")]
    [Tooltip("Minimum XZ of the travel rectangle.")]
    [SerializeField] private Vector2 areaMinXZ = new Vector2(-10f, -10f);

    [Tooltip("Maximum XZ of the travel rectangle.")]
    [SerializeField] private Vector2 areaMaxXZ = new Vector2(10f, 10f);

    [Header("Soft Avoidance (steering, e.g. water)")]
    [Tooltip("If an obstacle is closer than this radius, we steer away.")]
    [SerializeField] private float avoidRadius = 1.5f;

    [Tooltip("Layers to soft-avoid (water, dangerous zones, etc.).")]
    [SerializeField] private LayerMask softAvoidLayers;

    [Tooltip("0 = ignore avoidance; 1 = very strong steering away.")]
    [Range(0f, 1f)]
    [SerializeField] private float avoidStrength = 0.7f;

    [Tooltip("If true, TravelBehavior will try not to pick new targets inside softAvoidLayers (e.g. water).")]
    [SerializeField] private bool avoidChoosingTargetsInsideSoftObstacles = true;

    [Header("Hard Obstacles (walls)")]
    [Tooltip("Layers that behave like walls. If the direct path to the target hits these, we re-pick a target.")]
    [SerializeField] private LayerMask hardBlockLayers;

    [Tooltip("Extra multiplier on the ray length to check a bit beyond the target for walls.")]
    [SerializeField] private float wallCheckDistanceMultiplier = 1.1f;

    [Header("Turning / Smoothness")]
    [Tooltip("How quickly we can change direction (degrees per second).")]
    [SerializeField] private float maxTurnDegreesPerSecond = 360f;

    [Header("Stuck Handling")]
    [Tooltip("If the enemy moves less than this distance for 'stuckRepathTime' seconds, it will pick a new target.")]
    [SerializeField] private float stuckDistanceThreshold = 0.05f;

    [Tooltip("How long the enemy must be nearly not moving before we consider it stuck.")]
    [SerializeField] private float stuckRepathTime = 1.5f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool debugGizmos = false;

    private Vector3 _currentTarget;
    private Vector3 _lastTarget;
    private bool _hasLastTarget;

    private Vector3 _lastPosition;
    private float _stuckTimer;

    public int Priority => priority;

    public bool CanActivate()
    {
        // Always allowed as a fallback / base behavior.
        return true;
    }

    public void OnEnter()
    {
        if (debugLogs)
        {
            Debug.Log("[TravelBehavior] OnEnter", this);
        }

        _lastPosition = transform.position;
        _stuckTimer = 0f;

        // First target is somewhere in the area.
        PickNewTarget(initial: true);
    }

    public void Tick(float deltaTime)
    {
        if (deltaTime <= 0f)
            return;

        Vector3 pos = transform.position;
        Vector3 targetXZ = new Vector3(_currentTarget.x, pos.y, _currentTarget.z);

        // Distance on XZ plane
        Vector3 toTarget = targetXZ - pos;
        toTarget.y = 0f;
        float dist = toTarget.magnitude;

        // If we reached the target → pick a new one.
        if (dist <= targetReachThreshold)
        {
            if (debugLogs)
            {
                Debug.Log("[TravelBehavior] Reached target " + _currentTarget + " → picking new target.", this);
            }

            _lastTarget = _currentTarget;
            _hasLastTarget = true;
            PickNewTarget(initial: false);
            return;
        }

        bool insideSoft = IsInsideSoftObstacle(pos);

        // 1) Check if a hard obstacle (wall) is directly between us and the target.
        if (dist > 0.01f && hardBlockLayers.value != 0)
        {
            Vector3 rayOrigin = pos + Vector3.up * 0.2f;
            Vector3 rayDir = toTarget.normalized;
            float rayDist = dist * wallCheckDistanceMultiplier;

            if (Physics.Raycast(rayOrigin, rayDir, out RaycastHit hit, rayDist, hardBlockLayers, QueryTriggerInteraction.Ignore))
            {
                // There's a wall on the way -> re-pick a new target instead of pushing into it forever.
                if (debugLogs)
                {
                    Debug.Log($"[TravelBehavior] Path to target blocked by HARD '{hit.collider.name}' → re-pick target.", this);
                }

                _lastTarget = _currentTarget;
                _hasLastTarget = true;
                PickNewTarget(initial: false);
                return;
            }
        }

        // 1.5) If we are NOT inside water yet, and the direct path to target crosses water, treat it like blocked too.
        if (!insideSoft && dist > 0.01f && softAvoidLayers.value != 0 && avoidRadius > 0f)
        {
            Vector3 rayOrigin = pos + Vector3.up * 0.2f;
            Vector3 rayDir = toTarget.normalized;
            float rayDist = dist * wallCheckDistanceMultiplier;

            if (Physics.Raycast(rayOrigin, rayDir, out RaycastHit softHit, rayDist, softAvoidLayers, QueryTriggerInteraction.Collide))
            {
                if (debugLogs)
                {
                    Debug.Log($"[TravelBehavior] Path to target crosses SOFT (water) '{softHit.collider.name}' → re-pick target.", this);
                }

                _lastTarget = _currentTarget;
                _hasLastTarget = true;
                PickNewTarget(initial: false);
                return;
            }
        }

        // 2) Base move direction is toward the target.
        Vector3 desiredDir = dist > 1e-4f ? toTarget.normalized : Vector3.forward;

        // 3) Soft avoidance (water etc.) → steering.
        Vector3 awayDir = Vector3.zero;
        bool hasSoftAway = false;
        if (avoidRadius > 0f && softAvoidLayers.value != 0 && avoidStrength > 0f)
        {
            hasSoftAway = TryComputeSoftAwayDirection(pos, out awayDir);
        }

        if (hasSoftAway)
        {
            if (insideSoft)
            {
                // If we are INSIDE water: ignore the target and just escape.
                desiredDir = awayDir;
                if (debugLogs)
                {
                    Debug.Log("[TravelBehavior] Inside soft obstacle → escape direction only: " + desiredDir, this);
                }
            }
            else
            {
                // Outside water: blend target direction and avoidance.
                Vector3 blended = desiredDir * (1f - avoidStrength) + awayDir * avoidStrength;
                if (blended.sqrMagnitude > 1e-4f)
                    desiredDir = blended.normalized;

                if (debugLogs)
                {
                    Debug.Log("[TravelBehavior] Soft avoidance steer. desiredDir=" + desiredDir, this);
                }
            }
        }

        // 4) Smooth turning: limit how fast we change direction.
        Vector3 currentForward = transform.forward;
        currentForward.y = 0f;
        if (currentForward.sqrMagnitude < 1e-4f)
            currentForward = desiredDir;
        currentForward.Normalize();

        Quaternion fromRot = Quaternion.LookRotation(currentForward, Vector3.up);
        Quaternion toRot = Quaternion.LookRotation(desiredDir, Vector3.up);
        float maxAngle = maxTurnDegreesPerSecond * deltaTime;
        Quaternion limitedRot = Quaternion.RotateTowards(fromRot, toRot, maxAngle);
        Vector3 finalMoveDir = limitedRot * Vector3.forward;
        finalMoveDir.y = 0f;
        finalMoveDir.Normalize();

        // 5) Apply movement
        if (finalMoveDir.sqrMagnitude > 1e-4f)
        {
            pos += finalMoveDir * (travelSpeed * deltaTime);
            transform.position = pos;
        }

        // 6) Stuck detection (works together with KinematicCollisionResolver)
        Vector3 newPos = transform.position;
        float movedDist = (newPos - _lastPosition).magnitude;

        if (movedDist < stuckDistanceThreshold)
        {
            _stuckTimer += deltaTime;
        }
        else
        {
            _stuckTimer = 0f;
        }

        _lastPosition = newPos;

        if (_stuckTimer >= stuckRepathTime)
        {
            if (debugLogs)
            {
                Debug.Log("[TravelBehavior] Detected as stuck → re-pick target.", this);
            }

            _lastTarget = _currentTarget;
            _hasLastTarget = true;
            PickNewTarget(initial: false);
            _stuckTimer = 0f;
        }
    }

    public void OnExit()
    {
        if (debugLogs)
        {
            Debug.Log("[TravelBehavior] OnExit", this);
        }
    }

    // ---------------------------------------------
    // Target picking
    // ---------------------------------------------

    private void PickNewTarget(bool initial)
    {
        // Clamp area in case values are reversed in inspector.
        Vector2 min = new Vector2(
            Mathf.Min(areaMinXZ.x, areaMaxXZ.x),
            Mathf.Min(areaMinXZ.y, areaMaxXZ.y)
        );
        Vector2 max = new Vector2(
            Mathf.Max(areaMinXZ.x, areaMaxXZ.x),
            Mathf.Max(areaMinXZ.y, areaMaxXZ.y)
        );

        const int maxTries = 16;
        Vector3 chosen = Vector3.zero;
        bool found = false;

        for (int i = 0; i < maxTries; i++)
        {
            float x = Random.Range(min.x, max.x);
            float z = Random.Range(min.y, max.y);
            Vector3 candidate = new Vector3(x, transform.position.y, z);

            // 1) Avoid choosing targets too close to the previous target.
            if (_hasLastTarget && !initial)
            {
                Vector2 prev2D = new Vector2(_lastTarget.x, _lastTarget.z);
                Vector2 cand2D = new Vector2(candidate.x, candidate.z);
                float distPrev = Vector2.Distance(prev2D, cand2D);

                if (distPrev < minDistanceBetweenTargets)
                    continue;
            }

            // 2) Optionally: don't choose targets inside soft-avoid layers (e.g., water).
            if (avoidChoosingTargetsInsideSoftObstacles && softAvoidLayers.value != 0)
            {
                Vector3 checkPos = candidate + Vector3.up * 0.1f;
                bool insideForbidden = Physics.CheckSphere(
                    checkPos,
                    Mathf.Max(0.25f, avoidRadius * 0.5f),
                    softAvoidLayers,
                    QueryTriggerInteraction.Collide // include triggers
                );

                if (insideForbidden)
                {
                    if (debugLogs)
                    {
                        Debug.Log("[TravelBehavior] Skipping candidate inside soft-avoid layer at " + candidate, this);
                    }
                    continue;
                }
            }

            // 3) Optional: ensure there isn't an immediate wall on the way to this candidate
            if (hardBlockLayers.value != 0)
            {
                Vector3 origin = transform.position + Vector3.up * 0.2f;
                Vector3 dir = (candidate - origin);
                dir.y = 0f;
                float dist = dir.magnitude;
                if (dist > 0.1f)
                {
                    dir.Normalize();
                    float rayDist = dist * wallCheckDistanceMultiplier;

                    if (Physics.Raycast(origin, dir, rayDist, hardBlockLayers, QueryTriggerInteraction.Ignore))
                    {
                        // This candidate requires crossing a wall → skip.
                        if (debugLogs)
                        {
                            Debug.Log("[TravelBehavior] Skipping candidate blocked by wall at " + candidate, this);
                        }
                        continue;
                    }
                }
            }

            chosen = candidate;
            found = true;
            break;
        }

        if (!found)
        {
            // If we failed to find a "far enough" or "safe" target, just pick something.
            float x = Random.Range(min.x, max.x);
            float z = Random.Range(min.y, max.y);
            chosen = new Vector3(x, transform.position.y, z);

            if (debugLogs)
            {
                Debug.Log("[TravelBehavior] Fallback target: " + chosen, this);
            }
        }

        _currentTarget = chosen;

        if (debugLogs)
        {
            Debug.Log("[TravelBehavior] New target: " + _currentTarget, this);
        }
    }

    // ---------------------------------------------
    // Helpers
    // ---------------------------------------------

    private bool IsInsideSoftObstacle(Vector3 position)
    {
        if (softAvoidLayers.value == 0)
            return false;

        return Physics.CheckSphere(
            position + Vector3.up * 0.1f,
            0.2f,
            softAvoidLayers,
            QueryTriggerInteraction.Collide
        );
    }

    /// <summary>
    /// Returns true if there is any soft obstacle within avoidRadius.
    /// Outputs a vector pointing in the *overall* direction we should go
    /// to move away from all obstacles in the radius (XZ only).
    /// </summary>
    private bool TryComputeSoftAwayDirection(Vector3 position, out Vector3 awayDir)
    {
        awayDir = Vector3.zero;

        Collider[] hits = Physics.OverlapSphere(
            position,
            avoidRadius,
            softAvoidLayers,
            QueryTriggerInteraction.Collide // include triggers (water volumes etc.)
        );

        if (hits == null || hits.Length == 0)
            return false;

        Vector3 sum = Vector3.zero;
        int count = 0;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i];
            if (col == null)
                continue;

            if (col.transform == transform)
                continue;

            Vector3 closest = col.ClosestPoint(position);
            Vector3 away = position - closest;

            // If we're *inside* the collider, ClosestPoint == position → away = 0.
            // In that case, use a fallback direction based on the collider's center.
            away.y = 0f;
            float dist = away.magnitude;

            if (dist < 1e-4f)
            {
                // Fallback: push away from the collider's center in XZ.
                Vector3 fromCenter = position - col.bounds.center;
                fromCenter.y = 0f;

                if (fromCenter.sqrMagnitude < 1e-4f)
                {
                    // If still degenerate, just skip this collider.
                    continue;
                }

                away = fromCenter.normalized;
                dist = 0.001f; // treat as very close
            }
            else
            {
                away.Normalize();
            }

            // Stronger contribution when closer
            float weight = Mathf.Clamp01((avoidRadius - dist) / avoidRadius); // closer = stronger
            sum += away * weight;
            count++;
        }

        if (count == 0)
            return false;

        Vector3 result = sum;
        if (result.sqrMagnitude < 1e-4f)
            return false;

        awayDir = result.normalized;
        return true;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!debugGizmos)
            return;

        // Travel area rectangle
        Vector2 min = new Vector2(
            Mathf.Min(areaMinXZ.x, areaMaxXZ.x),
            Mathf.Min(areaMinXZ.y, areaMaxXZ.y)
        );
        Vector2 max = new Vector2(
            Mathf.Max(areaMinXZ.x, areaMaxXZ.x),
            Mathf.Max(areaMinXZ.y, areaMaxXZ.y)
        );

        Vector3 p1 = new Vector3(min.x, transform.position.y, min.y);
        Vector3 p2 = new Vector3(max.x, transform.position.y, min.y);
        Vector3 p3 = new Vector3(max.x, transform.position.y, max.y);
        Vector3 p4 = new Vector3(min.x, transform.position.y, max.y);

        Gizmos.color = new Color(0.2f, 0.8f, 0.4f, 0.25f);
        Gizmos.DrawLine(p1, p2);
        Gizmos.DrawLine(p2, p3);
        Gizmos.DrawLine(p3, p4);
        Gizmos.DrawLine(p4, p1);

        // Soft avoid radius around current position
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, avoidRadius);
    }
#endif
}
