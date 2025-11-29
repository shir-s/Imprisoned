// FILEPATH: Assets/Scripts/AI/StrokeTrailWanderBehavior.cs
using UnityEngine;

/// <summary>
/// Simple wandering behavior:
/// - Moves in a random direction on the XZ plane.
/// - Changes direction every few seconds.
/// - If it gets too close to objects on specified layers, it picks a new
///   wander direction (instead of being pushed back).
/// 
/// CanActivate() is always true, so as long as there is no higher-priority
/// behavior that can activate, the controller will use this one.
/// </summary>
[DisallowMultipleComponent]
public class WanderBehavior : MonoBehaviour, IEnemyBehavior
{
    [Header("Behavior Priority")]
    [Tooltip("Higher value = higher priority. Wander is usually the lowest (e.g. 0).")]
    [SerializeField] private int priority = 0;

    [Header("Movement")]
    [Tooltip("Speed while wandering (world units/sec).")]
    [SerializeField] private float wanderSpeed = 1.0f;

    [Tooltip("How often to change wander direction (seconds), if nothing else happens.")]
    [SerializeField] private float wanderDirectionChangeInterval = 2.0f;

    [Header("Avoidance")]
    [Tooltip("If an obstacle is closer than this radius, pick a new wander direction.")]
    [SerializeField] private float avoidRadius = 1.5f;

    [Tooltip("Layers that the wanderer should keep its distance from.")]
    [SerializeField] private LayerMask avoidLayers;

    [Tooltip("How strongly the new direction is biased away from nearby obstacles (0..1). Higher = more away.")]
    [Range(0f, 1f)]
    [SerializeField] private float awayBias = 0.7f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool debugGizmos = false;

    // Internal state
    private Vector3 _wanderDir;
    private float _wanderTimer;

    public int Priority => priority;

    public bool CanActivate()
    {
        // Wander is always allowed as a fallback.
        return true;
    }

    public void OnEnter()
    {
        if (debugLogs)
        {
            Debug.Log("[StrokeTrailWanderBehavior] OnEnter", this);
        }

        PickNewWanderDirection();
    }

    public void Tick(float deltaTime)
    {
        if (deltaTime <= 0f)
            return;

        Vector3 pos = transform.position;

        // 1) If we are too close to obstacles, pick a new direction
        if (avoidRadius > 0f && avoidLayers.value != 0)
        {
            if (TryComputeAwayDirection(pos, out Vector3 awayDir))
            {
                PickNewDirectionAwayFrom(awayDir);
            }
        }

        // 2) Move in current wander direction (XZ only)
        Vector3 moveDir = _wanderDir;
        moveDir.y = 0f;

        if (moveDir.sqrMagnitude > 1e-4f)
        {
            moveDir.Normalize();
            pos += moveDir * (wanderSpeed * deltaTime);
            transform.position = pos;
        }

        // 3) Change direction periodically (even if no obstacles)
        _wanderTimer -= deltaTime;
        if (_wanderTimer <= 0f)
        {
            PickNewWanderDirection();
        }
    }

    public void OnExit()
    {
        if (debugLogs)
        {
            Debug.Log("[StrokeTrailWanderBehavior] OnExit", this);
        }
    }

    // --------------------------------------------------------
    // Direction picking
    // --------------------------------------------------------

    private void PickNewWanderDirection()
    {
        // Random direction on XZ plane
        Vector2 dir2D = Random.insideUnitCircle.normalized;
        if (dir2D.sqrMagnitude < 1e-4f)
            dir2D = Vector2.right;

        _wanderDir = new Vector3(dir2D.x, 0f, dir2D.y);
        _wanderTimer = wanderDirectionChangeInterval;

        if (debugLogs)
        {
            Debug.Log("[StrokeTrailWanderBehavior] New random wander direction: " + _wanderDir, this);
        }
    }

    /// <summary>
    /// Picks a new wander direction that is biased away from obstacles
    /// (using the provided awayDir vector).
    /// </summary>
    private void PickNewDirectionAwayFrom(Vector3 awayDir)
    {
        awayDir.y = 0f;
        if (awayDir.sqrMagnitude < 1e-4f)
        {
            // Fallback to fully random if awayDir is degenerate
            PickNewWanderDirection();
            return;
        }

        awayDir.Normalize();

        // We'll try a few random directions and keep the one that mostly
        // points in the same direction as awayDir (dot >= awayBias).
        Vector3 bestDir = awayDir;
        bool foundGood = false;

        const int maxTries = 8;
        for (int i = 0; i < maxTries; i++)
        {
            Vector2 rand2D = Random.insideUnitCircle.normalized;
            if (rand2D.sqrMagnitude < 1e-4f)
                continue;

            Vector3 candidate = new Vector3(rand2D.x, 0f, rand2D.y);
            float dot = Vector3.Dot(candidate, awayDir); // 1 = same direction, -1 = opposite

            if (dot >= awayBias)
            {
                bestDir = candidate;
                foundGood = true;
                break;
            }
        }

        _wanderDir = bestDir.normalized;
        _wanderTimer = wanderDirectionChangeInterval; // reset timer when forced to turn

        if (debugLogs)
        {
            string reason = foundGood ? "biased away from obstacle" : "fallback (awayDir)";
            Debug.Log($"[StrokeTrailWanderBehavior] PickNewDirectionAwayFrom ({reason}): {_wanderDir}", this);
        }
    }

    // --------------------------------------------------------
    // Obstacle detection
    // --------------------------------------------------------

    /// <summary>
    /// Returns true if there is any obstacle within avoidRadius.
    /// Outputs a vector pointing in the *overall* direction we should go
    /// to move away from all obstacles in the radius.
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

            // Ignore own colliders (if any)
            if (col.transform == transform)
                continue;

            // Closest point on the collider to our position
            Vector3 closest = col.ClosestPoint(position);
            Vector3 away = position - closest;

            // Work in XZ plane
            away.y = 0f;

            float dist = away.magnitude;
            if (dist < 1e-4f)
                continue;

            // Stronger contribution when closer
            float weight = Mathf.Clamp01((avoidRadius - dist) / avoidRadius); // 1 at center, 0 at border
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

        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, avoidRadius);
    }
#endif
}
