// FILEPATH: Assets/Scripts/AI/StrokeAttackBehavior.cs
using UnityEngine;
using System;

/// <summary>
/// Attack behavior:
/// - Looks for targets (colliders) on specified layers within detectionRadius.
/// - If a target is found, it becomes active and starts chasing that target.
/// - Moves towards the target (XZ plane) at chaseSpeed, avoiding obstacles.
/// - When inside attackRadius, destroys the target GameObject.
/// 
/// Target Priority:
/// - Each layer can have its own attack priority.
/// - Higher priority targets are chosen over lower priority ones.
/// - If priorities are equal, the closest target is chosen.
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
    [Serializable]
    public struct TargetLayerPriority
    {
        [Tooltip("The layer to target.")]
        public SingleLayer layer;

        [Tooltip("Attack priority for this layer. Higher = more important target.")]
        public int attackPriority;

        [Tooltip("Optional: Custom detection radius for this layer. If 0, uses the default detectionRadius.")]
        public float customDetectionRadius;

        [Tooltip("Optional: Custom attack radius for this layer. If 0, uses the default attackRadius.")]
        public float customAttackRadius;
    }

    /// <summary>
    /// Helper struct to select a single layer in the Inspector.
    /// </summary>
    [Serializable]
    public struct SingleLayer
    {
        [SerializeField] private int layerIndex;

        public int LayerIndex => layerIndex;
        public int LayerMask => layerIndex >= 0 ? (1 << layerIndex) : 0;

        public static implicit operator int(SingleLayer layer) => layer.layerIndex;
    }

    [Header("Behavior Priority")]
    [SerializeField] private int priority = 20;

    [Header("Target Layers & Priorities")]
    [Tooltip("List of layers to target, each with its own attack priority.")]
    [SerializeField] private TargetLayerPriority[] targetPriorities;

    [Header("Detection (Defaults)")]
    [Tooltip("Default detection radius if not specified per-layer.")]
    [SerializeField] private float detectionRadius = 15f;

    [Tooltip("Default attack radius if not specified per-layer.")]
    [SerializeField] private float attackRadius = 2f;

    [Header("Movement")]
    [Tooltip("Chase speed while attacking (world units/sec).")]
    [SerializeField] private float chaseSpeed = 4f;

    [Tooltip("If true, the enemy will rotate to face its movement direction.")]
    [SerializeField] private bool faceMovement = true;

    [Tooltip("How quickly we can change facing direction (degrees per second).")]
    [SerializeField] private float maxTurnDegreesPerSecond = 360f;

    [Header("Obstacle Avoidance")]
    [Tooltip("Layers treated as obstacles to avoid while chasing.")]
    [SerializeField] private LayerMask obstacleLayers;

    [Tooltip("How far ahead to check for obstacles.")]
    [SerializeField] private float avoidRadius = 2.0f;

    [Tooltip("The enemy's physical radius - used to determine if gaps are passable.")]
    [SerializeField] private float enemyRadius = 0.4f;

    [Tooltip("Extra clearance beyond enemyRadius to maintain from obstacles.")]
    [SerializeField] private float preferredClearance = 0.3f;

    [Tooltip("Within this distance to the target, ignore avoidance and commit to reaching it.")]
    [SerializeField] private float commitToTargetDistance = 1.5f;

    [Tooltip("How many directions to sample when looking for a clear path.")]
    [SerializeField] private int avoidanceSamples = 12;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool debugGizmos = false;

    private Transform _currentTarget;
    private Collider _currentTargetCollider;
    private int _currentTargetPriority;
    private float _currentTargetAttackRadius;

    public int Priority => priority;

    /// <summary>
    /// Combined layer mask of all target layers.
    /// </summary>
    private int CombinedTargetMask
    {
        get
        {
            int mask = 0;
            if (targetPriorities != null)
            {
                foreach (var tp in targetPriorities)
                {
                    mask |= tp.layer.LayerMask;
                }
            }
            return mask;
        }
    }

    // --------------------------------------------------------
    // IEnemyBehavior
    // --------------------------------------------------------

    public bool CanActivate()
    {
        if (_currentTarget != null && _currentTargetCollider != null)
        {
            if (IsTargetValidAndInDetectionRadius(_currentTarget, _currentTargetCollider))
            {
                // Check if there's a higher priority target available
                if (TryFindBetterTarget(out Transform betterTarget, out Collider betterCollider, 
                    out int betterPriority, out float betterAttackRadius))
                {
                    // Switch to the better target
                    _currentTarget = betterTarget;
                    _currentTargetCollider = betterCollider;
                    _currentTargetPriority = betterPriority;
                    _currentTargetAttackRadius = betterAttackRadius;

                    if (debugLogs)
                    {
                        Debug.Log($"[AttackBehavior] Switched to higher priority target: {_currentTarget.name} (priority={_currentTargetPriority})", this);
                    }
                }
                return true;
            }

            ClearTarget();
        }

        return TryAcquireTarget();
    }

    public void OnEnter()
    {
        if (debugLogs)
        {
            Debug.Log("[AttackBehavior] OnEnter", this);
        }
    }

    public void Tick(float deltaTime)
    {
        if (_currentTarget == null || _currentTargetCollider == null)
        {
            return;
        }

        if (!IsTargetValidAndInDetectionRadius(_currentTarget, _currentTargetCollider))
        {
            if (debugLogs)
            {
                Debug.Log("[AttackBehavior] Target left detection radius → clear.", this);
            }
            ClearTarget();
            return;
        }

        Vector3 pos = transform.position;
        Vector3 targetPos = _currentTarget.position;

        Vector3 selfXZ = new Vector3(pos.x, 0f, pos.z);
        Vector3 targetXZ = new Vector3(targetPos.x, 0f, targetPos.z);
        Vector3 toTarget = targetXZ - selfXZ;
        float distXZ = toTarget.magnitude;

        // Inside attack radius → "hit" target.
        float effectiveAttackRadius = _currentTargetAttackRadius > 0f ? _currentTargetAttackRadius : attackRadius;
        if (distXZ <= effectiveAttackRadius)
        {
            if (debugLogs)
            {
                Debug.Log($"[AttackBehavior] ATTACK! Destroying target '{_currentTarget.name}' (dist={distXZ})", this);
            }

            GameObject targetGO = _currentTarget.gameObject;
            ClearTarget();
            Destroy(targetGO);

            return;
        }

        // Move towards target with obstacle avoidance
        if (distXZ > 1e-5f)
        {
            Vector3 toTargetDir = toTarget / distXZ;

            // Compute steering direction (handles avoidance)
            Vector3 desiredDir = ComputeSteeringDirection(pos, toTargetDir, distXZ);

            // Move in the computed direction
            Vector3 newPos = pos + new Vector3(desiredDir.x, 0f, desiredDir.z) * (chaseSpeed * deltaTime);
            transform.position = newPos;

            // Rotate to face movement direction
            if (faceMovement)
            {
                RotateTowardDirection(desiredDir, deltaTime);
            }
        }
    }

    public void OnExit()
    {
        if (debugLogs)
        {
            Debug.Log("[AttackBehavior] OnExit", this);
        }

        ClearTarget();
    }

    // --------------------------------------------------------
    // Obstacle Avoidance
    // --------------------------------------------------------

    private Vector3 ComputeSteeringDirection(Vector3 position, Vector3 toTargetDir, float distToTarget)
    {
        // If very close to target, just go straight
        if (distToTarget <= commitToTargetDistance)
            return toTargetDir;

        // Skip avoidance if no obstacle layers set
        if (obstacleLayers.value == 0)
            return toTargetDir;

        float checkDistance = Mathf.Min(avoidRadius, distToTarget);
        float totalRadius = enemyRadius + preferredClearance;

        // Check if direct path is clear
        if (IsPathClear(position, toTargetDir, checkDistance, totalRadius))
        {
            // Direct path is clear - check if we need to nudge away from nearby walls
            Vector3 nudge = ComputeClearanceNudge(position, toTargetDir, totalRadius);
            if (nudge.sqrMagnitude > 0.001f)
            {
                Vector3 nudgedDir = (toTargetDir + nudge * 0.5f).normalized;
                if (Vector3.Dot(nudgedDir, toTargetDir) > 0.5f)
                    return nudgedDir;
            }
            return toTargetDir;
        }

        // Direct path is blocked - find best alternative
        return FindBestAvoidanceDirection(position, toTargetDir, checkDistance, totalRadius);
    }

    private bool IsPathClear(Vector3 position, Vector3 direction, float distance, float radius)
    {
        return !Physics.SphereCast(
            position,
            radius,
            direction,
            out RaycastHit hit,
            distance,
            obstacleLayers,
            QueryTriggerInteraction.Ignore
        );
    }

    private bool HasEnoughClearance(Vector3 position, Vector3 moveDirection, float requiredWidth)
    {
        Vector3 right = Vector3.Cross(Vector3.up, moveDirection).normalized;

        float leftDist = requiredWidth * 2f;
        float rightDist = requiredWidth * 2f;

        if (Physics.Raycast(position, -right, out RaycastHit leftHit, requiredWidth * 2f, obstacleLayers, QueryTriggerInteraction.Ignore))
        {
            leftDist = leftHit.distance;
        }

        if (Physics.Raycast(position, right, out RaycastHit rightHit, requiredWidth * 2f, obstacleLayers, QueryTriggerInteraction.Ignore))
        {
            rightDist = rightHit.distance;
        }

        float totalWidth = leftDist + rightDist;
        return totalWidth >= requiredWidth * 2f;
    }

    private Vector3 ComputeClearanceNudge(Vector3 position, Vector3 moveDirection, float desiredClearance)
    {
        Vector3 right = Vector3.Cross(Vector3.up, moveDirection).normalized;
        Vector3 nudge = Vector3.zero;

        if (Physics.Raycast(position, -right, out RaycastHit leftHit, desiredClearance * 1.5f, obstacleLayers, QueryTriggerInteraction.Ignore))
        {
            float penetration = desiredClearance - leftHit.distance;
            if (penetration > 0)
            {
                nudge += right * (penetration / desiredClearance);
            }
        }

        if (Physics.Raycast(position, right, out RaycastHit rightHit, desiredClearance * 1.5f, obstacleLayers, QueryTriggerInteraction.Ignore))
        {
            float penetration = desiredClearance - rightHit.distance;
            if (penetration > 0)
            {
                nudge -= right * (penetration / desiredClearance);
            }
        }

        return nudge;
    }

    private Vector3 FindBestAvoidanceDirection(Vector3 position, Vector3 toTargetDir, float checkDistance, float clearanceRadius)
    {
        Vector3 bestDir = toTargetDir;
        float bestScore = float.MinValue;

        float angleStep = 180f / avoidanceSamples;

        for (int i = 1; i <= avoidanceSamples; i++)
        {
            float angle = angleStep * i;

            for (int sign = -1; sign <= 1; sign += 2)
            {
                float testAngle = angle * sign * 0.5f;
                Vector3 testDir = Quaternion.Euler(0f, testAngle, 0f) * toTargetDir;

                if (!IsPathClear(position, testDir, checkDistance, clearanceRadius))
                    continue;

                Vector3 aheadPos = position + testDir * (checkDistance * 0.5f);
                if (!HasEnoughClearance(aheadPos, testDir, clearanceRadius))
                    continue;

                float dotScore = Vector3.Dot(testDir, toTargetDir);
                float progressScore = Vector3.Dot(testDir, toTargetDir);

                float totalScore = dotScore + progressScore * 0.5f;

                if (totalScore > bestScore)
                {
                    bestScore = totalScore;
                    bestDir = testDir;
                }
            }

            if (bestScore > 0.3f)
                break;
        }

        if (bestScore == float.MinValue)
        {
            bestDir = ComputeSlideDirection(position, toTargetDir, checkDistance);
        }

        return bestDir;
    }

    private Vector3 ComputeSlideDirection(Vector3 position, Vector3 toTargetDir, float checkDistance)
    {
        if (Physics.Raycast(position, toTargetDir, out RaycastHit hit, checkDistance, obstacleLayers, QueryTriggerInteraction.Ignore))
        {
            Vector3 normal = hit.normal;
            normal.y = 0f;
            normal.Normalize();

            Vector3 slideDir = Vector3.ProjectOnPlane(toTargetDir, normal).normalized;

            if (slideDir.sqrMagnitude > 0.001f && Vector3.Dot(slideDir, toTargetDir) > -0.5f)
            {
                return slideDir;
            }

            Vector3 perpRight = Vector3.Cross(Vector3.up, normal).normalized;
            Vector3 perpLeft = -perpRight;

            if (Vector3.Dot(perpRight, toTargetDir) > Vector3.Dot(perpLeft, toTargetDir))
                return perpRight;
            else
                return perpLeft;
        }

        return toTargetDir;
    }

    private void RotateTowardDirection(Vector3 desiredDir, float deltaTime)
    {
        desiredDir.y = 0f;
        if (desiredDir.sqrMagnitude < 0.0001f)
            return;

        desiredDir.Normalize();

        Vector3 currentForward = transform.forward;
        currentForward.y = 0f;
        if (currentForward.sqrMagnitude < 0.0001f)
            currentForward = desiredDir;
        currentForward.Normalize();

        Quaternion fromRot = Quaternion.LookRotation(currentForward, Vector3.up);
        Quaternion toRot = Quaternion.LookRotation(desiredDir, Vector3.up);
        float maxAngle = maxTurnDegreesPerSecond * deltaTime;
        transform.rotation = Quaternion.RotateTowards(fromRot, toRot, maxAngle);
    }

    // --------------------------------------------------------
    // Targeting helpers
    // --------------------------------------------------------

    private bool TryAcquireTarget()
    {
        if (targetPriorities == null || targetPriorities.Length == 0)
            return false;

        Vector3 origin = transform.position;
        Vector3 selfXZ = new Vector3(origin.x, 0f, origin.z);

        Transform bestTarget = null;
        Collider bestCollider = null;
        int bestPriority = int.MinValue;
        float bestDistSq = float.PositiveInfinity;
        float bestAttackRadius = attackRadius;

        // Check each priority layer
        foreach (var tp in targetPriorities)
        {
            if (tp.layer.LayerMask == 0)
                continue;

            float effectiveDetectionRadius = tp.customDetectionRadius > 0f ? tp.customDetectionRadius : detectionRadius;

            Collider[] hits = Physics.OverlapSphere(origin, effectiveDetectionRadius, tp.layer.LayerMask, QueryTriggerInteraction.Ignore);

            foreach (Collider c in hits)
            {
                if (c == null || c.gameObject == null)
                    continue;

                Transform t = c.transform;
                Vector3 targetPos = t.position;
                Vector3 targetXZ = new Vector3(targetPos.x, 0f, targetPos.z);

                float distSq = (targetXZ - selfXZ).sqrMagnitude;

                // Check if this target is better:
                // 1. Higher priority wins
                // 2. If same priority, closer wins
                bool isBetter = false;
                if (tp.attackPriority > bestPriority)
                {
                    isBetter = true;
                }
                else if (tp.attackPriority == bestPriority && distSq < bestDistSq)
                {
                    isBetter = true;
                }

                if (isBetter)
                {
                    bestTarget = t;
                    bestCollider = c;
                    bestPriority = tp.attackPriority;
                    bestDistSq = distSq;
                    bestAttackRadius = tp.customAttackRadius > 0f ? tp.customAttackRadius : attackRadius;
                }
            }
        }

        if (bestTarget == null)
            return false;

        _currentTarget = bestTarget;
        _currentTargetCollider = bestCollider;
        _currentTargetPriority = bestPriority;
        _currentTargetAttackRadius = bestAttackRadius;

        if (debugLogs)
        {
            float dist = Mathf.Sqrt(bestDistSq);
            Debug.Log($"[AttackBehavior] Acquired target: {_currentTarget.name} (dist={dist:F2}, priority={_currentTargetPriority})", this);
        }

        return true;
    }

    /// <summary>
    /// Check if there's a target with higher priority than the current one.
    /// </summary>
    private bool TryFindBetterTarget(out Transform betterTarget, out Collider betterCollider, 
        out int betterPriority, out float betterAttackRadius)
    {
        betterTarget = null;
        betterCollider = null;
        betterPriority = _currentTargetPriority;
        betterAttackRadius = _currentTargetAttackRadius;

        if (targetPriorities == null || targetPriorities.Length == 0)
            return false;

        Vector3 origin = transform.position;
        Vector3 selfXZ = new Vector3(origin.x, 0f, origin.z);

        foreach (var tp in targetPriorities)
        {
            // Only check layers with higher priority than current target
            if (tp.attackPriority <= _currentTargetPriority)
                continue;

            if (tp.layer.LayerMask == 0)
                continue;

            float effectiveDetectionRadius = tp.customDetectionRadius > 0f ? tp.customDetectionRadius : detectionRadius;

            Collider[] hits = Physics.OverlapSphere(origin, effectiveDetectionRadius, tp.layer.LayerMask, QueryTriggerInteraction.Ignore);

            float bestDistSq = float.PositiveInfinity;

            foreach (Collider c in hits)
            {
                if (c == null || c.gameObject == null)
                    continue;

                Transform t = c.transform;
                Vector3 targetPos = t.position;
                Vector3 targetXZ = new Vector3(targetPos.x, 0f, targetPos.z);

                float distSq = (targetXZ - selfXZ).sqrMagnitude;

                if (distSq < bestDistSq)
                {
                    betterTarget = t;
                    betterCollider = c;
                    betterPriority = tp.attackPriority;
                    betterAttackRadius = tp.customAttackRadius > 0f ? tp.customAttackRadius : attackRadius;
                    bestDistSq = distSq;
                }
            }
        }

        return betterTarget != null;
    }

    private bool IsTargetValidAndInDetectionRadius(Transform t, Collider c)
    {
        if (t == null || c == null || c.gameObject == null)
            return false;

        int targetLayer = c.gameObject.layer;
        
        // Find the matching layer priority entry
        float effectiveDetectionRadius = detectionRadius;
        bool foundLayer = false;

        if (targetPriorities != null)
        {
            foreach (var tp in targetPriorities)
            {
                if (tp.layer.LayerIndex == targetLayer)
                {
                    effectiveDetectionRadius = tp.customDetectionRadius > 0f ? tp.customDetectionRadius : detectionRadius;
                    foundLayer = true;
                    break;
                }
            }
        }

        if (!foundLayer)
            return false;

        Vector3 pos = transform.position;
        Vector3 targetPos = t.position;

        Vector3 selfXZ = new Vector3(pos.x, 0f, pos.z);
        Vector3 targetXZ = new Vector3(targetPos.x, 0f, targetPos.z);

        float dist = Vector3.Distance(selfXZ, targetXZ);

        return dist <= effectiveDetectionRadius;
    }

    private void ClearTarget()
    {
        _currentTarget = null;
        _currentTargetCollider = null;
        _currentTargetPriority = int.MinValue;
        _currentTargetAttackRadius = attackRadius;
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

        // Default detection radius
        Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.2f);
        Gizmos.DrawWireSphere(pos, detectionRadius);

        // Per-layer detection radii (if different from default)
        if (targetPriorities != null)
        {
            foreach (var tp in targetPriorities)
            {
                if (tp.customDetectionRadius > 0f && Mathf.Abs(tp.customDetectionRadius - detectionRadius) > 0.01f)
                {
                    Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.15f);
                    Gizmos.DrawWireSphere(pos, tp.customDetectionRadius);
                }
            }
        }

        // Default attack radius
        Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
        Gizmos.DrawWireSphere(pos, attackRadius);

        // Avoid radius
        if (obstacleLayers.value != 0)
        {
            Gizmos.color = new Color(1f, 1f, 0f, 0.15f);
            Gizmos.DrawWireSphere(pos, avoidRadius);
        }

        // Current target line
        if (Application.isPlaying && _currentTarget != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(pos, _currentTarget.position);
            
            // Draw attack radius around target
            Gizmos.color = new Color(1f, 0f, 0f, 0.5f);
            float effectiveAttackRadius = _currentTargetAttackRadius > 0f ? _currentTargetAttackRadius : attackRadius;
            Gizmos.DrawWireSphere(_currentTarget.position, effectiveAttackRadius);
        }
    }
#endif
}