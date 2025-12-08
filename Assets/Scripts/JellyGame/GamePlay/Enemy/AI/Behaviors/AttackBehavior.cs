// FILEPATH: Assets/Scripts/AI/AttackBehavior_Debug.cs

using System;
using UnityEngine;

namespace JellyGame.GamePlay.Enemy.AI.Behaviors
{
    /// <summary>
    /// DEBUG VERSION of AttackBehavior with extensive logging to diagnose the stuck issue.
    /// Enable debugLogs and watch the Console to see what's happening.
    /// </summary>
    [DisallowMultipleComponent]
    public class AttackBehavior : MonoBehaviour, IEnemyBehavior, IEnemySound
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

        // Speed multiplier for slow effects
        private float _speedMultiplier = 1.0f;

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
        [SerializeField] private bool debugLogs = true;  // Default to TRUE
        [SerializeField] private bool debugGizmos = true;
    
        [Header("Sound")]
        [Tooltip("If disabled, this behavior will not produce any sound.")]
        [SerializeField] private bool enableSound = true;

        [Tooltip("How the sound for this behavior should be played.\nNone = no sound even if enableSound is true.")]
        [SerializeField] private SoundPlaybackMode soundMode = SoundPlaybackMode.OnEnterLoop;

        [Tooltip("Base interval in seconds (used for FixedInterval and as MIN for RandomInterval).")]
        [SerializeField] private float soundInterval = 0.7f;

        [Tooltip("MAX interval (seconds) for RandomInterval mode. Ignored for other modes.")]
        [SerializeField] private float maxRandomInterval = 1.2f;

        [Tooltip("Name of the sound to play for this behavior (must exist in AudioSettings).")]
        [SerializeField] private string soundName = "EnemyAttack";

        [Tooltip("If true, use custom volume instead of default from AudioSettings.")]
        [SerializeField] private bool useCustomVolume = false;

        [Tooltip("Custom volume (0..1) when useCustomVolume is enabled.")]
        [Range(0f, 1f)]
        [SerializeField] private float soundVolume = 1f;

        private Transform _currentTarget;
        private Collider _currentTargetCollider;
        private int _currentTargetPriority;
        private float _currentTargetAttackRadius;

        private Vector3 _lastPosition;
        private int _tickCount = 0;

        public int Priority => priority;

        private void Awake()
        {
            _lastPosition = transform.position;
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
                    if (TryFindBetterTarget(out Transform betterTarget, out Collider betterCollider, 
                            out int betterPriority, out float betterAttackRadius))
                    {
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

            bool canActivate = TryAcquireTarget();
        
            if (debugLogs)
            {
                Debug.Log($"[AttackBehavior] CanActivate() = {canActivate}, HasTarget = {_currentTarget != null}", this);
            }
        
            return canActivate;
        }

        public void OnEnter()
        {
            _tickCount = 0;
            _lastPosition = transform.position;
        
            if (debugLogs)
            {
                Debug.Log($"[AttackBehavior] OnEnter - Target: {(_currentTarget != null ? _currentTarget.name : "NULL")}", this);
            }
        }

        public void Tick(float deltaTime)
        {
            _tickCount++;
        
            if (_currentTarget == null || _currentTargetCollider == null)
            {
                if (debugLogs && _tickCount % 30 == 0)
                {
                    Debug.LogWarning($"[AttackBehavior] Tick #{_tickCount} - NO TARGET!", this);
                }
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

            // Log every 30 ticks
            if (debugLogs && _tickCount % 30 == 0)
            {
                float movedDist = Vector3.Distance(pos, _lastPosition);
                Debug.Log($"[AttackBehavior] Tick #{_tickCount} - Target: {_currentTarget.name}, Dist: {distXZ:F2}, " +
                          $"Moved: {movedDist:F3}, DeltaTime: {deltaTime:F4}", this);
                _lastPosition = pos;
            }

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

                if (debugLogs && _tickCount % 30 == 0)
                {
                    Debug.Log($"[AttackBehavior] ToTargetDir: {toTargetDir}, DesiredDir: {desiredDir}, " +
                              $"Angle diff: {Vector3.Angle(toTargetDir, desiredDir):F1}°", this);
                }

                // CRITICAL: Check if we're actually moving
                Vector3 movement = new Vector3(desiredDir.x, 0f, desiredDir.z) * (chaseSpeed * _speedMultiplier * deltaTime);
                Vector3 newPos = pos + movement;
            
                if (debugLogs && _tickCount % 30 == 0)
                {
                    Debug.Log($"[AttackBehavior] Movement vector: {movement}, Magnitude: {movement.magnitude:F4}, " +
                              $"Speed: {chaseSpeed}, DT: {deltaTime:F4}", this);
                }
            
                transform.position = newPos;

                // Rotate to face movement direction
                if (faceMovement)
                {
                    RotateTowardDirection(desiredDir, deltaTime);
                }
            }
            else
            {
                if (debugLogs)
                {
                    Debug.LogWarning($"[AttackBehavior] distXZ too small: {distXZ}", this);
                }
            }
        }

        public void OnExit()
        {
            if (debugLogs)
            {
                Debug.Log($"[AttackBehavior] OnExit after {_tickCount} ticks", this);
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
            {
                if (debugLogs && _tickCount % 30 == 0)
                {
                    Debug.Log($"[AttackBehavior] Close to target ({distToTarget:F2} <= {commitToTargetDistance}), going straight", this);
                }
                return toTargetDir;
            }

            // Skip avoidance if no obstacle layers set
            if (obstacleLayers.value == 0)
            {
                if (debugLogs && _tickCount == 1)
                {
                    Debug.Log("[AttackBehavior] No obstacle layers set, no avoidance", this);
                }
                return toTargetDir;
            }

            float checkDistance = Mathf.Min(avoidRadius, distToTarget);
            float totalRadius = enemyRadius + preferredClearance;

            // Check if direct path is clear
            bool pathClear = IsPathClear(position, toTargetDir, checkDistance, totalRadius);
        
            if (debugLogs && _tickCount % 30 == 0)
            {
                Debug.Log($"[AttackBehavior] Path clear: {pathClear}, CheckDist: {checkDistance:F2}, Radius: {totalRadius:F2}", this);
            }

            if (pathClear)
            {
                // Direct path is clear - check if we need to nudge away from nearby walls
                Vector3 nudge = ComputeClearanceNudge(position, toTargetDir, totalRadius);
                if (nudge.sqrMagnitude > 0.001f)
                {
                    Vector3 nudgedDir = (toTargetDir + nudge * 0.5f).normalized;
                    if (Vector3.Dot(nudgedDir, toTargetDir) > 0.5f)
                    {
                        if (debugLogs && _tickCount % 30 == 0)
                        {
                            Debug.Log($"[AttackBehavior] Applying nudge: {nudge}", this);
                        }
                        return nudgedDir;
                    }
                }
                return toTargetDir;
            }

            // Direct path is blocked - find best alternative
            if (debugLogs && _tickCount % 30 == 0)
            {
                Debug.Log("[AttackBehavior] Path blocked, finding alternative", this);
            }
        
            return FindBestAvoidanceDirection(position, toTargetDir, checkDistance, totalRadius);
        }

        private bool IsPathClear(Vector3 position, Vector3 direction, float distance, float radius)
        {
            RaycastHit[] hits = Physics.SphereCastAll(
                position,
                radius,
                direction,
                distance,
                obstacleLayers,
                QueryTriggerInteraction.Ignore
            );

            if (debugLogs && _tickCount % 30 == 0)
            {
                Debug.Log($"[AttackBehavior] SphereCast found {hits.Length} hits", this);
            }

            // Check all hits - ignore self and current target
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null)
                    continue;

                Transform hitTransform = hit.collider.transform;
            
                // Ignore self
                if (hitTransform == transform || hitTransform.IsChildOf(transform) || transform.IsChildOf(hitTransform))
                {
                    if (debugLogs && _tickCount % 30 == 0)
                    {
                        Debug.Log($"[AttackBehavior] Ignoring self hit: {hit.collider.name}", this);
                    }
                    continue;
                }
            
                // Ignore current target
                if (_currentTarget != null && (hitTransform == _currentTarget || hitTransform.IsChildOf(_currentTarget) || _currentTarget.IsChildOf(hitTransform)))
                {
                    if (debugLogs && _tickCount % 30 == 0)
                    {
                        Debug.Log($"[AttackBehavior] Ignoring target hit: {hit.collider.name}", this);
                    }
                    continue;
                }

                // Found a real obstacle
                if (debugLogs && _tickCount % 30 == 0)
                {
                    Debug.Log($"[AttackBehavior] Real obstacle found: {hit.collider.name} at distance {hit.distance:F2}", this);
                }
                return false;
            }

            // No blocking obstacles found
            return true;
        }

        private bool HasEnoughClearance(Vector3 position, Vector3 moveDirection, float requiredWidth)
        {
            Vector3 right = Vector3.Cross(Vector3.up, moveDirection).normalized;

            float leftDist = requiredWidth * 2f;
            float rightDist = requiredWidth * 2f;

            if (Physics.Raycast(position, -right, out RaycastHit leftHit, requiredWidth * 2f, obstacleLayers, QueryTriggerInteraction.Ignore))
            {
                if (!ShouldIgnoreCollider(leftHit.collider))
                    leftDist = leftHit.distance;
            }

            if (Physics.Raycast(position, right, out RaycastHit rightHit, requiredWidth * 2f, obstacleLayers, QueryTriggerInteraction.Ignore))
            {
                if (!ShouldIgnoreCollider(rightHit.collider))
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
                if (!ShouldIgnoreCollider(leftHit.collider))
                {
                    float penetration = desiredClearance - leftHit.distance;
                    if (penetration > 0)
                    {
                        nudge += right * (penetration / desiredClearance);
                    }
                }
            }

            if (Physics.Raycast(position, right, out RaycastHit rightHit, desiredClearance * 1.5f, obstacleLayers, QueryTriggerInteraction.Ignore))
            {
                if (!ShouldIgnoreCollider(rightHit.collider))
                {
                    float penetration = desiredClearance - rightHit.distance;
                    if (penetration > 0)
                    {
                        nudge -= right * (penetration / desiredClearance);
                    }
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

                    float alignmentScore = Vector3.Dot(testDir, toTargetDir);
                
                    Vector3 targetPos = _currentTarget != null ? _currentTarget.position : position + toTargetDir * 100f;
                    Vector3 projectedPos = position + testDir * checkDistance;
                    Vector3 toTargetFromProjected = targetPos - projectedPos;
                    toTargetFromProjected.y = 0f;
                    float currentToTarget = (targetPos - position).magnitude;
                    float projectedToTarget = toTargetFromProjected.magnitude;
                    float progressScore = Mathf.Clamp01((currentToTarget - projectedToTarget) / checkDistance);

                    float totalScore = alignmentScore * 2f + progressScore;

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
                if (ShouldIgnoreCollider(hit.collider))
                {
                    return toTargetDir;
                }

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

        private bool ShouldIgnoreCollider(Collider col)
        {
            if (col == null)
                return true;

            Transform colTransform = col.transform;

            if (colTransform == transform || colTransform.IsChildOf(transform) || transform.IsChildOf(colTransform))
                return true;

            if (_currentTarget != null && 
                (colTransform == _currentTarget || colTransform.IsChildOf(_currentTarget) || _currentTarget.IsChildOf(colTransform)))
                return true;

            return false;
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
            {
                if (debugLogs)
                {
                    Debug.LogWarning("[AttackBehavior] No target priorities configured!", this);
                }
                return false;
            }

            Vector3 origin = transform.position;
            Vector3 selfXZ = new Vector3(origin.x, 0f, origin.z);

            Transform bestTarget = null;
            Collider bestCollider = null;
            int bestPriority = int.MinValue;
            float bestDistSq = float.PositiveInfinity;
            float bestAttackRadius = attackRadius;

            foreach (var tp in targetPriorities)
            {
                if (tp.layer.LayerMask == 0)
                    continue;

                float effectiveDetectionRadius = tp.customDetectionRadius > 0f ? tp.customDetectionRadius : detectionRadius;

                Collider[] hits = Physics.OverlapSphere(origin, effectiveDetectionRadius, tp.layer.LayerMask, QueryTriggerInteraction.Ignore);

                if (debugLogs && hits.Length > 0)
                {
                    Debug.Log($"[AttackBehavior] Found {hits.Length} potential targets on layer {tp.layer.LayerIndex}", this);
                }

                foreach (Collider c in hits)
                {
                    if (c == null || c.gameObject == null)
                        continue;

                    Transform t = c.transform;
                    Vector3 targetPos = t.position;
                    Vector3 targetXZ = new Vector3(targetPos.x, 0f, targetPos.z);

                    float distSq = (targetXZ - selfXZ).sqrMagnitude;

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
                Debug.Log($"<color=green>[AttackBehavior] *** ACQUIRED TARGET ***: {_currentTarget.name} (dist={dist:F2}, priority={_currentTargetPriority})</color>", this);
            }

            return true;
        }

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
                        effectiveDetectionRadius = tp.customDetectionRadius > 0f 
                            ? tp.customDetectionRadius 
                            : detectionRadius;
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

            // CRITICAL FIX: Add hysteresis
            // Physics.OverlapSphere uses 3D distance, but we measure XZ distance
            // This can cause targets to be found at slightly different distances
            // Add a buffer zone so targets don't immediately flip-flop
            // 
            // Acquisition: uses radius R (via OverlapSphere)
            // Validation: uses radius R + 3 (extra buffer)
            //
            // With your values:
            // - Detection radius: 11
            // - Target found at: 12.55
            // - Validation radius: 11 + 3 = 14
            // - 12.55 < 14 = TRUE, target stays valid!
            const float HYSTERESIS_BUFFER = 3f;
            float validationRadius = effectiveDetectionRadius + HYSTERESIS_BUFFER;
    
            return dist <= validationRadius;
        }

        private void ClearTarget()
        {
            if (debugLogs && _currentTarget != null)
            {
                Debug.Log($"[AttackBehavior] Clearing target: {_currentTarget.name}", this);
            }
        
            _currentTarget = null;
            _currentTargetCollider = null;
            _currentTargetPriority = int.MinValue;
            _currentTargetAttackRadius = attackRadius;
        }
    
        // --------------------------------------------------------
        // IEnemySound
        // --------------------------------------------------------

        /// <summary>
        /// How should sound be played while this behavior is active?
        /// If enableSound is false, returns None to completely mute this behavior.
        /// </summary>
        public SoundPlaybackMode GetSoundMode()
        {
            if (!enableSound)
                return SoundPlaybackMode.None;

            return soundMode;
        }

        /// <summary>
        /// Base interval for FixedInterval and MIN interval for RandomInterval.
        /// </summary>
        public float GetSoundInterval()
        {
            return soundInterval;
        }

        /// <summary>
        /// MAX interval for RandomInterval mode.
        /// </summary>
        public float GetMaxSoundInterval()
        {
            return maxRandomInterval;
        }

        /// <summary>
        /// Name of the sound to play. If sound is disabled, returns null.
        /// </summary>
        public string GetSoundName()
        {
            if (!enableSound)
                return null;

            return soundName;
        }

        /// <summary>
        /// Optional custom volume for this behavior.
        /// </summary>
        public float GetSoundVolume()
        {
            if (!enableSound)
                return -1f;

            return useCustomVolume ? soundVolume : -1f;
        }

        /// <summary>
        /// Only play sound while we actually have a valid target.
        /// Lets you toggle sound in inspector and avoids idle attack sounds.
        /// </summary>
        public bool ShouldPlaySound()
        {
            return enableSound && _currentTarget != null;
        }


        // --------------------------------------------------------
        // Speed Multiplier
        // --------------------------------------------------------

        public void SetSpeedMultiplier(float multiplier)
        {
            _speedMultiplier = Mathf.Max(0f, multiplier);
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

            Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.2f);
            Gizmos.DrawWireSphere(pos, detectionRadius);

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

            Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
            Gizmos.DrawWireSphere(pos, attackRadius);

            if (obstacleLayers.value != 0)
            {
                Gizmos.color = new Color(1f, 1f, 0f, 0.15f);
                Gizmos.DrawWireSphere(pos, avoidRadius);
            }

            if (Application.isPlaying && _currentTarget != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(pos, _currentTarget.position);
            
                float effectiveAttackRadius = _currentTargetAttackRadius > 0f ? _currentTargetAttackRadius : attackRadius;
                Gizmos.color = new Color(1f, 0f, 0f, 0.5f);
                Gizmos.DrawWireSphere(_currentTarget.position, effectiveAttackRadius);
            }
        }
#endif
    }
}