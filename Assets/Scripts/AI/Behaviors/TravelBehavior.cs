// FILEPATH: Assets/Scripts/AI/TravelBehavior.cs
using UnityEngine;

/// <summary>
/// Travel behavior:
/// - Moves between random target points in a defined XZ area.
/// - Never immediately travels back to (or near) the previous target.
/// - Avoids obstacles on specified layers (e.g. water).
/// 
/// CanActivate() is always true, so it's a good "exploration" baseline behavior
/// instead of pure random wandering.
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

    [Header("Avoidance")]
    [Tooltip("If an obstacle is closer than this radius, we steer away.")]
    [SerializeField] private float avoidRadius = 1.5f;

    [Tooltip("Layers that the traveler should avoid (e.g., Water).")]
    [SerializeField] private LayerMask avoidLayers;

    [Tooltip("0 = ignore avoidance; 1 = very strong avoidance.")]
    [Range(0f, 1f)]
    [SerializeField] private float avoidStrength = 0.7f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool debugGizmos = false;

    private Vector3 _currentTarget;
    private Vector3 _lastTarget;
    private bool _hasLastTarget;

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

        // Base move direction is toward the target.
        Vector3 moveDir = toTarget.sqrMagnitude > 1e-4f ? toTarget.normalized : Vector3.zero;

        // Obstacle avoidance: steer away if something is nearby.
        if (avoidRadius > 0f && avoidLayers.value != 0 && avoidStrength > 0f)
        {
            if (TryComputeAwayDirection(pos, out Vector3 awayDir))
            {
                // Combine target direction and away direction.
                // avoidStrength controls how strong the steering is.
                moveDir = (moveDir * (1f - avoidStrength) + awayDir * avoidStrength).normalized;

                if (debugLogs)
                {
                    Debug.Log("[TravelBehavior] Steering away from obstacle. New dir: " + moveDir, this);
                }
            }
        }

        // Apply movement
        if (moveDir.sqrMagnitude > 1e-4f)
        {
            pos += moveDir * (travelSpeed * deltaTime);
            transform.position = pos;
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

            if (_hasLastTarget && !initial)
            {
                // Avoid picking a target too close to the previous one,
                // so we don't just bounce back and forth.
                Vector2 prev2D = new Vector2(_lastTarget.x, _lastTarget.z);
                Vector2 cand2D = new Vector2(candidate.x, candidate.z);
                float distPrev = Vector2.Distance(prev2D, cand2D);

                if (distPrev < minDistanceBetweenTargets)
                    continue;
            }

            chosen = candidate;
            found = true;
            break;
        }

        if (!found)
        {
            // If we failed to find a "far enough" target, just pick something.
            float x = Random.Range(min.x, max.x);
            float z = Random.Range(min.y, max.y);
            chosen = new Vector3(x, transform.position.y, z);
        }

        _currentTarget = chosen;

        if (debugLogs)
        {
            Debug.Log("[TravelBehavior] New target: " + _currentTarget, this);
        }
    }

    // ---------------------------------------------
    // Obstacle avoidance (same style as Wander)
    // ---------------------------------------------

    /// <summary>
    /// Returns true if there is any obstacle within avoidRadius.
    /// Outputs a vector pointing in the *overall* direction we should go
    /// to move away from all obstacles in the radius (XZ only).
    /// </summary>
    private bool TryComputeAwayDirection(Vector3 position, out Vector3 awayDir)
    {
        awayDir = Vector3.zero;

        Collider[] hits = Physics.OverlapSphere(position, avoidRadius, avoidLayers, QueryTriggerInteraction.Ignore);
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

            // XZ only
            away.y = 0f;

            float dist = away.magnitude;
            if (dist < 1e-4f)
                continue;

            float weight = Mathf.Clamp01((avoidRadius - dist) / avoidRadius); // closer = stronger
            sum += away.normalized * weight;
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

        // Avoid radius around current position
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, avoidRadius);
    }
#endif
}
