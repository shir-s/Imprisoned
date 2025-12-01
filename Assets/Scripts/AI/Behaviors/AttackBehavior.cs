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
    [SerializeField] private int priority = 20;

    [Header("Targeting")]
    [Tooltip("Layers that count as valid attack targets (e.g. Player).")]
    [SerializeField] private LayerMask targetLayers;

    [Tooltip("How far we can see a target and decide to attack.")]
    [SerializeField] private float detectionRadius = 15f;

    [Tooltip("How close we must be to actually 'hit' and destroy the target.")]
    [SerializeField] private float attackRadius = 2f;

    [Header("Movement")]
    [Tooltip("Chase speed while attacking (world units/sec).")]
    [SerializeField] private float chaseSpeed = 4f;

    [Tooltip("If true, the enemy will rotate to face its movement direction.")]
    [SerializeField] private bool faceMovement = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool debugGizmos = false;

    private Transform _currentTarget;
    private Collider  _currentTargetCollider;

    public int Priority => priority;

    // --------------------------------------------------------
    // IEnemyBehavior
    // --------------------------------------------------------

    public bool CanActivate()
    {
        // If we already have a target, keep using it as long as it's still valid.
        if (_currentTarget != null && _currentTargetCollider != null)
        {
            if (IsTargetValidAndInDetectionRadius(_currentTarget, _currentTargetCollider))
                return true;

            // Target moved away or got destroyed.
            ClearTarget();
        }

        // Otherwise, try to acquire a new one.
        return TryAcquireTarget();
    }

    public void OnEnter()
    {
        if (debugLogs)
        {
            Debug.Log("[StrokeAttackBehavior] OnEnter", this);
        }
    }

    public void Tick(float deltaTime)
    {
        if (_currentTarget == null || _currentTargetCollider == null)
        {
            // Nothing to do; CanActivate will either reacquire next frame or another
            // behavior will take over.
            return;
        }

        // Re-check validity & distance with EXACT same logic as CanActivate / TryAcquireTarget.
        if (!IsTargetValidAndInDetectionRadius(_currentTarget, _currentTargetCollider))
        {
            if (debugLogs)
            {
                Debug.Log("[StrokeAttackBehavior] Target left detection radius → clear.", this);
            }
            ClearTarget();
            return;
        }

        Vector3 pos = transform.position;
        Vector3 targetPos = _currentTarget.position;

        // Work in XZ plane (ignore Y for chasing).
        Vector3 selfXZ   = new Vector3(pos.x, 0f, pos.z);
        Vector3 targetXZ = new Vector3(targetPos.x, 0f, targetPos.z);
        Vector3 toTarget = targetXZ - selfXZ;
        float   distXZ   = toTarget.magnitude;

        // Inside attack radius → "hit" target.
        if (distXZ <= attackRadius)
        {
            if (debugLogs)
            {
                Debug.Log($"[StrokeAttackBehavior] ATTACK! Destroying target '{_currentTarget.name}' (dist={distXZ})", this);
            }

            // Destroy target GameObject.
            GameObject targetGO = _currentTarget.gameObject;
            ClearTarget();
            Object.Destroy(targetGO);

            return;
        }

        // Otherwise, move towards target.
        if (distXZ > 1e-5f)
        {
            Vector3 dirXZ = toTarget / distXZ;

            // Move in XZ, keep our current Y.
            Vector3 newPos = pos + new Vector3(dirXZ.x, 0f, dirXZ.z) * (chaseSpeed * deltaTime);
            transform.position = newPos;

            if (faceMovement)
            {
                Vector3 forward = new Vector3(dirXZ.x, 0f, dirXZ.z);
                if (forward.sqrMagnitude > 0.0001f)
                {
                    transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
                }
            }
        }
    }

    public void OnExit()
    {
        if (debugLogs)
        {
            Debug.Log("[StrokeAttackBehavior] OnExit", this);
        }

        // Don't destroy anything on exit; just forget about our current target.
        ClearTarget();
    }

    // --------------------------------------------------------
    // Targeting helpers
    // --------------------------------------------------------

    private bool TryAcquireTarget()
    {
        Vector3 origin = transform.position;

        // Physics.OverlapSphere uses true radius in 3D space, but for our logic
        // we still compute XZ distance for logs/decisions to keep it consistent.
        Collider[] hits = Physics.OverlapSphere(origin, detectionRadius, targetLayers, QueryTriggerInteraction.Ignore);
        if (hits.Length == 0)
            return false;

        Collider best = null;
        float bestDistSqXZ = float.PositiveInfinity;

        Vector3 selfXZ = new Vector3(origin.x, 0f, origin.z);

        foreach (Collider c in hits)
        {
            if (c == null || c.gameObject == null)
                continue;

            Transform t = c.transform;
            Vector3 targetPos = t.position;
            Vector3 targetXZ = new Vector3(targetPos.x, 0f, targetPos.z);

            float distSq = (targetXZ - selfXZ).sqrMagnitude;
            if (distSq < bestDistSqXZ)
            {
                bestDistSqXZ = distSq;
                best = c;
            }
        }

        if (best == null)
            return false;

        float bestDist = Mathf.Sqrt(bestDistSqXZ);

        // Extra safety: enforce detectionRadius in XZ plane as well.
        if (bestDist > detectionRadius)
            return false;

        _currentTarget         = best.transform;
        _currentTargetCollider = best;

        if (debugLogs)
        {
            Debug.Log($"[StrokeAttackBehavior] Acquired target: {_currentTarget.name} (dist={bestDist})", this);
        }

        return true;
    }

    private bool IsTargetValidAndInDetectionRadius(Transform t, Collider c)
    {
        if (t == null || c == null || c.gameObject == null)
            return false;

        // If layer changed, treat as invalid.
        if ((targetLayers.value & (1 << c.gameObject.layer)) == 0)
            return false;

        Vector3 pos = transform.position;
        Vector3 targetPos = t.position;

        Vector3 selfXZ   = new Vector3(pos.x, 0f, pos.z);
        Vector3 targetXZ = new Vector3(targetPos.x, 0f, targetPos.z);

        float dist = Vector3.Distance(selfXZ, targetXZ);

        return dist <= detectionRadius;
    }

    private void ClearTarget()
    {
        _currentTarget = null;
        _currentTargetCollider = null;
    }

    // --------------------------------------------------------
    // Gizmos
    // --------------------------------------------------------

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
