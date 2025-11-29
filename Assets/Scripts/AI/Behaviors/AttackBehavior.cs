// FILEPATH: Assets/Scripts/AI/StrokeAttackBehavior.cs
using UnityEngine;

/// <summary>
/// Attack behavior:
/// - Looks for targets (colliders) on specified layers within detectionRadius.
/// - If a target is found, it becomes active and starts chasing that target.
/// - Moves towards the target (XZ plane) at chaseSpeed.
/// - When inside attackRadius, destroys the target GameObject.
/// 
/// CanActivate():
/// - True if there is at least one valid target within detectionRadius.
/// 
/// Typical priorities:
/// - Attack: 20
/// - Follow trail: 10
/// - Wander: 0
/// </summary>
[DisallowMultipleComponent]
public class AttackBehavior : MonoBehaviour, IEnemyBehavior
{
    [Header("Behavior Priority")]
    [Tooltip("Higher value = higher priority. Attack should usually be above Follow/Wander.")]
    [SerializeField] private int priority = 20;

    [Header("Targeting")]
    [Tooltip("Layers that can be attacked by this enemy.")]
    [SerializeField] private LayerMask targetLayers;

    [Tooltip("Radius in which we search for targets to attack.")]
    [SerializeField] private float detectionRadius = 4f;

    [Tooltip("Radius at which we consider the target close enough to attack (and destroy).")]
    [SerializeField] private float attackRadius = 0.8f;

    [Header("Movement")]
    [Tooltip("Movement speed while chasing a target (world units/sec).")]
    [SerializeField] private float chaseSpeed = 3f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool debugGizmos = false;

    // Current target we are chasing
    private Transform _currentTarget;

    public int Priority => priority;

    // --------------------------------------------------------
    // IEnemyBehavior
    // --------------------------------------------------------

    public bool CanActivate()
    {
        // If we already have a valid target in range, we can continue.
        if (HasValidTargetInRange())
            return true;

        // Otherwise, try to acquire a fresh target.
        TryAcquireTarget();
        return _currentTarget != null;
    }

    public void OnEnter()
    {
        if (debugLogs)
        {
            Debug.Log("[StrokeAttackBehavior] OnEnter", this);
        }

        // Make sure we have a target when entering.
        if (_currentTarget == null)
        {
            TryAcquireTarget();
        }
    }

    public void Tick(float deltaTime)
    {
        if (deltaTime <= 0f)
            return;

        if (_currentTarget == null)
        {
            // Try to reacquire if lost (brain will drop us next frame if CanActivate() fails).
            TryAcquireTarget();
            return;
        }

        // If target got destroyed or deactivated, clear it.
        if (!_currentTarget.gameObject.activeInHierarchy)
        {
            if (debugLogs)
                Debug.Log("[StrokeAttackBehavior] Target inactive/destroyed → clear.", this);

            _currentTarget = null;
            return;
        }

        Vector3 pos = transform.position;
        Vector3 targetPos = _currentTarget.position;

        // Work in XZ only
        Vector3 flatTargetPos = new Vector3(targetPos.x, pos.y, targetPos.z);
        Vector3 toTarget = flatTargetPos - pos;
        float dist = toTarget.magnitude;

        // If within attack radius → destroy target
        if (dist <= attackRadius)
        {
            if (debugLogs)
            {
                Debug.Log("[StrokeAttackBehavior] In attack radius → Destroy target " + _currentTarget.name, this);
            }

            GameObject toDestroy = _currentTarget.gameObject;
            _currentTarget = null;

            // Destroy target object
            if (toDestroy != null)
            {
                Object.Destroy(toDestroy);
            }

            return;
        }

        // If outside detection radius, target is considered lost.
        if (dist > detectionRadius)
        {
            if (debugLogs)
            {
                Debug.Log("[StrokeAttackBehavior] Target left detection radius → clear.", this);
            }

            _currentTarget = null;
            return;
        }

        // Chase towards target
        if (dist > 1e-5f)
        {
            Vector3 dir = toTarget / dist;
            pos += dir * (chaseSpeed * deltaTime);
            transform.position = pos;
        }
    }

    public void OnExit()
    {
        if (debugLogs)
        {
            Debug.Log("[StrokeAttackBehavior] OnExit", this);
        }

        _currentTarget = null;
    }

    // --------------------------------------------------------
    // Targeting helpers
    // --------------------------------------------------------

    private bool HasValidTargetInRange()
    {
        if (_currentTarget == null)
            return false;

        if (!_currentTarget.gameObject.activeInHierarchy)
            return false;

        // Must still be on a valid layer
        if ((targetLayers.value & (1 << _currentTarget.gameObject.layer)) == 0)
            return false;

        Vector3 pos = transform.position;
        Vector3 targetPos = _currentTarget.position;
        targetPos.y = pos.y;

        float dist = (targetPos - pos).magnitude;
        return dist <= detectionRadius;
    }

    /// <summary>
    /// Tries to find the nearest valid target within detectionRadius.
    /// </summary>
    private void TryAcquireTarget()
    {
        _currentTarget = null;

        if (targetLayers.value == 0 || detectionRadius <= 0f)
            return;

        Vector3 pos = transform.position;

        Collider[] hits = Physics.OverlapSphere(
            pos,
            detectionRadius,
            targetLayers,
            QueryTriggerInteraction.Ignore);

        if (hits == null || hits.Length == 0)
            return;

        float bestDistSq = float.PositiveInfinity;
        Transform best = null;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i];
            if (col == null)
                continue;

            Transform t = col.transform;
            if (t == transform)
                continue;

            Vector3 p = t.position;
            Vector3 flat = new Vector3(p.x, pos.y, p.z);
            float dSq = (flat - pos).sqrMagnitude;

            if (dSq < bestDistSq)
            {
                bestDistSq = dSq;
                best = t;
            }
        }

        _currentTarget = best;

        if (debugLogs)
        {
            if (_currentTarget != null)
            {
                Debug.Log("[StrokeAttackBehavior] Acquired target: " + _currentTarget.name +
                          " (dist=" + Mathf.Sqrt(bestDistSq) + ")", this);
            }
            else
            {
                Debug.Log("[StrokeAttackBehavior] No valid target found in detection radius.", this);
            }
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!debugGizmos)
            return;

        Vector3 pos = transform.position;

        // Detection radius
        Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.2f);
        Gizmos.DrawWireSphere(pos, detectionRadius);

        // Attack radius
        Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
        Gizmos.DrawWireSphere(pos, attackRadius);
    }
#endif
}
