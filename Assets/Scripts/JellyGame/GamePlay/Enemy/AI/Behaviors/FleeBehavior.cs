// FILEPATH: Assets/Scripts/AI/Behaviors/FleeBehavior.cs

using UnityEngine;

namespace JellyGame.GamePlay.Enemy.AI.Behaviors
{
    /// <summary>
    /// Flee behavior:
    /// - Detects threats (units on specified layers) within a detection radius.
    /// - Runs away from the nearest/most dangerous threat.
    /// - Handles two types of obstacles while fleeing:
    ///   * Small obstacles: navigates around them while still fleeing
    ///   * Large obstacles: treats them as impassable, picks a new flee direction
    /// - Stops fleeing when threats leave the detection radius (+ safe buffer).
    ///
    /// CanActivate():
    /// - True if there is at least one threat within detectionRadius.
    ///
    /// Typical priorities:
    /// - Flee: 15 (higher than wander/travel, but could be lower than attack depending on your needs)
    /// </summary>
    [DisallowMultipleComponent]
    public class FleeBehavior : MonoBehaviour, IEnemyBehavior, IEnemySound
    {
        [Header("Behavior Priority")]
        [Tooltip("Higher value = higher priority. Flee is usually high (e.g. 15).")]
        [SerializeField] private int priority = 15;

        [Header("Threat Detection")]
        [Tooltip("Layers that count as threats to flee from.")]
        [SerializeField] private LayerMask threatLayers;

        [Tooltip("How far we can detect a threat and start fleeing.")]
        [SerializeField] private float detectionRadius = 8f;

        [Tooltip("Extra distance beyond detectionRadius before we stop fleeing (hysteresis to prevent jitter).")]
        [SerializeField] private float safeBufferDistance = 2f;

        [Header("Movement")]
        [Tooltip("Speed while fleeing (world units/sec). Usually faster than normal movement.")]
        [SerializeField] private float fleeSpeed = 4f;

        [Tooltip("If true, the unit will rotate to face its movement direction.")]
        [SerializeField] private bool faceMovement = true;

        [Tooltip("How quickly we can change facing direction (degrees per second).")]
        [SerializeField] private float maxTurnDegreesPerSecond = 360f;

        [Header("Small Obstacles (can go around)")]
        [Tooltip("Layers treated as small obstacles we can navigate around.")]
        [SerializeField] private LayerMask smallObstacleLayers;

        [Tooltip("How far ahead to check for small obstacles.")]
        [SerializeField] private float smallObstacleCheckDistance = 2f;

        [Tooltip("The unit's physical radius for small obstacle avoidance.")]
        [SerializeField] private float unitRadius = 0.4f;

        [Tooltip("How many directions to sample when going around small obstacles.")]
        [SerializeField] private int avoidanceSamples = 8;

        [Header("Large Obstacles (impassable, change direction)")]
        [Tooltip("Layers treated as large/impassable obstacles.")]
        [SerializeField] private LayerMask largeObstacleLayers;

        [Tooltip("How far ahead to check for large obstacles.")]
        [SerializeField] private float largeObstacleCheckDistance = 3f;

        [Tooltip("When hitting a large obstacle, how wide an angle to search for alternate flee directions.")]
        [SerializeField] private float largeObstacleSearchAngle = 120f;

        [Tooltip("Number of directions to try when blocked by a large obstacle.")]
        [SerializeField] private int largeObstacleSearchSamples = 12;

        [Header("Corner/Trapped Handling")]
        [Tooltip("If true, when completely trapped, the unit will try to move sideways along walls.")]
        [SerializeField] private bool enableWallSliding = true;

        [Tooltip("If completely stuck for this many seconds, temporarily ignore obstacles and push through.")]
        [SerializeField] private float stuckTimeout = 1.5f;

        [Header("Sound")]
        [Tooltip("If disabled, this behavior will not produce any sound.")]
        [SerializeField] private bool enableSound = true;

        [Tooltip("How the sound for this behavior should be played.\nNone = no sound even if enableSound is true.")]
        [SerializeField] private SoundPlaybackMode soundMode = SoundPlaybackMode.Loop;

        [Tooltip("Base interval in seconds (used for FixedInterval and as MIN for RandomInterval).")]
        [SerializeField] private float soundInterval = 0.8f;

        [Tooltip("MAX interval (seconds) for RandomInterval mode. Ignored for other modes.")]
        [SerializeField] private float maxRandomInterval = 1.5f;

        [Tooltip("Name of the sound to play for this behavior (must exist in AudioSettings).")]
        [SerializeField] private string soundName = "EnemyFlee";

        [Tooltip("If true, use custom volume instead of default from AudioSettings.")]
        [SerializeField] private bool useCustomVolume = false;

        [Tooltip("Custom volume (0..1) when useCustomVolume is enabled.")]
        [Range(0f, 1f)]
        [SerializeField] private float soundVolume = 1f;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;
        [SerializeField] private bool debugGizmos = false;

        // Runtime state
        private Transform _currentThreat;
        private Collider _currentThreatCollider;
        private Vector3 _lastFleeDirection;
        private float _stuckTimer;
        private Vector3 _lastPosition;
        private bool _isStuck;

        public int Priority => priority;

        // --------------------------------------------------------
        // IEnemyBehavior
        // --------------------------------------------------------

        public bool CanActivate()
        {
            // If we have a current threat that's still valid and in extended range, stay active
            if (_currentThreat != null && _currentThreatCollider != null)
            {
                float extendedRadius = detectionRadius + safeBufferDistance;
                if (IsThreatValidAndInRadius(_currentThreat, _currentThreatCollider, extendedRadius))
                    return true;

                ClearThreat();
            }

            // Try to find a new threat
            return TryAcquireThreat();
        }

        public void OnEnter()
        {
            if (debugLogs)
            {
                Debug.Log("[FleeBehavior] OnEnter", this);
            }

            _lastFleeDirection = Vector3.zero;
            _stuckTimer = 0f;
            _lastPosition = transform.position;
            _isStuck = false;
        }

        public void Tick(float deltaTime)
        {
            if (_currentThreat == null || _currentThreatCollider == null)
            {
                return;
            }

            // Check if threat is still valid
            float extendedRadius = detectionRadius + safeBufferDistance;
            if (!IsThreatValidAndInRadius(_currentThreat, _currentThreatCollider, extendedRadius))
            {
                if (debugLogs)
                {
                    Debug.Log("[FleeBehavior] Threat left extended radius → clear.", this);
                }
                ClearThreat();
                return;
            }

            // Calculate base flee direction (away from threat)
            Vector3 pos = transform.position;
            Vector3 threatPos = _currentThreat.position;

            Vector3 selfXZ = new Vector3(pos.x, 0f, pos.z);
            Vector3 threatXZ = new Vector3(threatPos.x, 0f, threatPos.z);
            Vector3 awayFromThreat = selfXZ - threatXZ;

            if (awayFromThreat.sqrMagnitude < 0.0001f)
            {
                // Threat is exactly on us, pick a random direction
                Vector2 rand = Random.insideUnitCircle.normalized;
                awayFromThreat = new Vector3(rand.x, 0f, rand.y);
            }
            else
            {
                awayFromThreat.Normalize();
            }

            // Compute final flee direction with obstacle handling
            Vector3 fleeDirection = ComputeFleeDirection(pos, awayFromThreat, deltaTime);

            // Update stuck detection
            UpdateStuckDetection(pos, deltaTime);

            // If stuck, temporarily ignore obstacles
            if (_isStuck)
            {
                fleeDirection = awayFromThreat;
                if (debugLogs)
                {
                    Debug.Log("[FleeBehavior] Stuck! Ignoring obstacles temporarily.", this);
                }
            }

            // Move
            Vector3 newPos = pos + new Vector3(fleeDirection.x, 0f, fleeDirection.z) * (fleeSpeed * deltaTime);
            transform.position = newPos;

            // Rotate to face movement direction
            if (faceMovement)
            {
                RotateTowardDirection(fleeDirection, deltaTime);
            }

            _lastFleeDirection = fleeDirection;
        }

        public void OnExit()
        {
            if (debugLogs)
            {
                Debug.Log("[FleeBehavior] OnExit", this);
            }

            ClearThreat();
            _lastFleeDirection = Vector3.zero;
            _stuckTimer = 0f;
            _isStuck = false;
        }

        // --------------------------------------------------------
        // Flee Direction Computation
        // --------------------------------------------------------

        private Vector3 ComputeFleeDirection(Vector3 position, Vector3 awayFromThreat, float deltaTime)
        {
            Vector3 desiredDir = awayFromThreat;

            // First, check for large obstacles (impassable)
            if (largeObstacleLayers.value != 0)
            {
                if (IsPathBlockedByLargeObstacle(position, desiredDir))
                {
                    // Find alternative direction away from threat that avoids large obstacle
                    Vector3 altDir = FindAlternativeFleeDirection(position, awayFromThreat);
                    if (altDir.sqrMagnitude > 0.001f)
                    {
                        desiredDir = altDir;
                    }
                    else if (enableWallSliding)
                    {
                        // Completely blocked, try wall sliding
                        desiredDir = ComputeWallSlideDirection(position, awayFromThreat);
                    }
                }
            }

            // Then, handle small obstacles (can go around)
            if (smallObstacleLayers.value != 0)
            {
                desiredDir = AdjustForSmallObstacles(position, desiredDir, awayFromThreat);
            }

            return desiredDir.normalized;
        }

        // --------------------------------------------------------
        // Large Obstacle Handling
        // --------------------------------------------------------

        private bool IsPathBlockedByLargeObstacle(Vector3 position, Vector3 direction)
        {
            return Physics.SphereCast(
                position + Vector3.up * 0.1f,
                unitRadius,
                direction,
                out RaycastHit hit,
                largeObstacleCheckDistance,
                largeObstacleLayers,
                QueryTriggerInteraction.Ignore
            );
        }

        /// <summary>
        /// When blocked by a large obstacle, find an alternative direction that:
        /// 1. Still moves away from the threat (dot product > 0 with awayFromThreat)
        /// 2. Doesn't hit a large obstacle
        /// </summary>
        private Vector3 FindAlternativeFleeDirection(Vector3 position, Vector3 awayFromThreat)
        {
            float halfAngle = largeObstacleSearchAngle * 0.5f;
            float angleStep = largeObstacleSearchAngle / largeObstacleSearchSamples;

            Vector3 bestDir = Vector3.zero;
            float bestScore = float.MinValue;

            for (int i = 1; i <= largeObstacleSearchSamples; i++)
            {
                float angle = angleStep * i;

                // Try both left and right
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    float testAngle = angle * sign * 0.5f;
                
                    // Don't search beyond our max angle
                    if (Mathf.Abs(testAngle) > halfAngle)
                        continue;

                    Vector3 testDir = Quaternion.Euler(0f, testAngle, 0f) * awayFromThreat;

                    // Must still be moving away from threat (at least somewhat)
                    float awayDot = Vector3.Dot(testDir, awayFromThreat);
                    if (awayDot < 0.1f)
                        continue;

                    // Check if this direction is clear of large obstacles
                    if (IsPathBlockedByLargeObstacle(position, testDir))
                        continue;

                    // Score: prefer directions more aligned with "away from threat"
                    float score = awayDot;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestDir = testDir;
                    }
                }

                // Found a good direction, stop searching
                if (bestScore > 0.5f)
                    break;
            }

            if (debugLogs && bestDir.sqrMagnitude > 0.001f)
            {
                Debug.Log($"[FleeBehavior] Large obstacle avoided, new direction: {bestDir}", this);
            }

            return bestDir;
        }

        /// <summary>
        /// When completely blocked, try to slide along the wall.
        /// </summary>
        private Vector3 ComputeWallSlideDirection(Vector3 position, Vector3 awayFromThreat)
        {
            // Raycast to find the wall normal
            if (Physics.Raycast(position + Vector3.up * 0.1f, awayFromThreat, out RaycastHit hit, 
                    largeObstacleCheckDistance, largeObstacleLayers, QueryTriggerInteraction.Ignore))
            {
                Vector3 wallNormal = hit.normal;
                wallNormal.y = 0f;
                wallNormal.Normalize();

                // Project flee direction onto the wall plane
                Vector3 slideDir = Vector3.ProjectOnPlane(awayFromThreat, wallNormal).normalized;

                if (slideDir.sqrMagnitude > 0.001f)
                {
                    // Check both slide directions, pick the one that doesn't lead back to threat
                    Vector3 slideLeft = slideDir;
                    Vector3 slideRight = -slideDir;

                    // Prefer the direction that is more "away" from threat
                    float leftDot = Vector3.Dot(slideLeft, awayFromThreat);
                    float rightDot = Vector3.Dot(slideRight, awayFromThreat);

                    Vector3 bestSlide = leftDot >= rightDot ? slideLeft : slideRight;

                    if (debugLogs)
                    {
                        Debug.Log($"[FleeBehavior] Wall sliding in direction: {bestSlide}", this);
                    }

                    return bestSlide;
                }
            }

            // Last resort: perpendicular to threat direction
            Vector3 perp = Vector3.Cross(Vector3.up, awayFromThreat).normalized;
            return Random.value < 0.5f ? perp : -perp;
        }

        // --------------------------------------------------------
        // Small Obstacle Handling
        // --------------------------------------------------------

        /// <summary>
        /// Adjust flee direction to go around small obstacles while still fleeing.
        /// </summary>
        private Vector3 AdjustForSmallObstacles(Vector3 position, Vector3 currentDir, Vector3 awayFromThreat)
        {
            // Check if current direction hits a small obstacle
            if (!Physics.SphereCast(position + Vector3.up * 0.1f, unitRadius, currentDir, 
                    out RaycastHit hit, smallObstacleCheckDistance, smallObstacleLayers, QueryTriggerInteraction.Ignore))
            {
                // Path is clear
                return currentDir;
            }

            // Find a direction that goes around the obstacle
            float angleStep = 90f / avoidanceSamples;

            for (int i = 1; i <= avoidanceSamples; i++)
            {
                float angle = angleStep * i;

                for (int sign = -1; sign <= 1; sign += 2)
                {
                    float testAngle = angle * sign;
                    Vector3 testDir = Quaternion.Euler(0f, testAngle, 0f) * currentDir;

                    // Must still be somewhat away from threat
                    if (Vector3.Dot(testDir, awayFromThreat) < 0f)
                        continue;

                    // Check if clear
                    if (!Physics.SphereCast(position + Vector3.up * 0.1f, unitRadius, testDir, 
                            out RaycastHit testHit, smallObstacleCheckDistance, smallObstacleLayers, QueryTriggerInteraction.Ignore))
                    {
                        if (debugLogs)
                        {
                            Debug.Log($"[FleeBehavior] Going around small obstacle, angle offset: {testAngle}", this);
                        }
                        return testDir.normalized;
                    }
                }
            }

            // Couldn't find a way around, return original direction
            return currentDir;
        }

        // --------------------------------------------------------
        // Stuck Detection
        // --------------------------------------------------------

        private void UpdateStuckDetection(Vector3 currentPos, float deltaTime)
        {
            float movedDist = Vector3.Distance(currentPos, _lastPosition);
            float expectedDist = fleeSpeed * deltaTime * 0.3f; // Expect at least 30% of intended movement

            if (movedDist < expectedDist)
            {
                _stuckTimer += deltaTime;
                if (_stuckTimer >= stuckTimeout)
                {
                    _isStuck = true;
                }
            }
            else
            {
                _stuckTimer = 0f;
                _isStuck = false;
            }

            _lastPosition = currentPos;
        }

        // --------------------------------------------------------
        // Rotation
        // --------------------------------------------------------

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
        // Threat Management
        // --------------------------------------------------------

        private bool TryAcquireThreat()
        {
            Vector3 pos = transform.position;
            Collider[] hits = Physics.OverlapSphere(pos, detectionRadius, threatLayers, QueryTriggerInteraction.Ignore);

            if (hits.Length == 0)
                return false;

            // Find the closest threat
            Collider closest = null;
            float closestDistSq = float.PositiveInfinity;

            Vector3 selfXZ = new Vector3(pos.x, 0f, pos.z);

            foreach (Collider c in hits)
            {
                if (c == null || c.gameObject == null)
                    continue;

                // Don't flee from self
                if (c.transform == transform)
                    continue;

                Vector3 threatPos = c.transform.position;
                Vector3 threatXZ = new Vector3(threatPos.x, 0f, threatPos.z);
                float distSq = (threatXZ - selfXZ).sqrMagnitude;

                if (distSq < closestDistSq)
                {
                    closestDistSq = distSq;
                    closest = c;
                }
            }

            if (closest == null)
                return false;

            _currentThreat = closest.transform;
            _currentThreatCollider = closest;

            if (debugLogs)
            {
                float dist = Mathf.Sqrt(closestDistSq);
                Debug.Log($"[FleeBehavior] Acquired threat: {_currentThreat.name} (dist={dist:F2})", this);
            }

            return true;
        }

        private bool IsThreatValidAndInRadius(Transform t, Collider c, float radius)
        {
            if (t == null || c == null || c.gameObject == null)
                return false;

            // Check if still on threat layer
            if ((threatLayers.value & (1 << c.gameObject.layer)) == 0)
                return false;

            Vector3 pos = transform.position;
            Vector3 threatPos = t.position;

            Vector3 selfXZ = new Vector3(pos.x, 0f, pos.z);
            Vector3 threatXZ = new Vector3(threatPos.x, 0f, threatPos.z);

            float dist = Vector3.Distance(selfXZ, threatXZ);
            return dist <= radius;
        }

        private void ClearThreat()
        {
            _currentThreat = null;
            _currentThreatCollider = null;
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
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.2f);
            Gizmos.DrawWireSphere(pos, detectionRadius);

            // Extended (safe) radius
            Gizmos.color = new Color(0.2f, 1f, 0.2f, 0.15f);
            Gizmos.DrawWireSphere(pos, detectionRadius + safeBufferDistance);

            // Small obstacle check distance
            if (smallObstacleLayers.value != 0)
            {
                Gizmos.color = new Color(1f, 1f, 0f, 0.2f);
                Vector3 forward = transform.forward;
                Gizmos.DrawLine(pos, pos + forward * smallObstacleCheckDistance);
            }

            // Large obstacle check distance
            if (largeObstacleLayers.value != 0)
            {
                Gizmos.color = new Color(1f, 0.5f, 0f, 0.2f);
                Vector3 forward = transform.forward;
                Gizmos.DrawLine(pos + Vector3.up * 0.1f, pos + Vector3.up * 0.1f + forward * largeObstacleCheckDistance);
            }

            // Current flee direction
            if (Application.isPlaying && _lastFleeDirection.sqrMagnitude > 0.001f)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(pos, pos + _lastFleeDirection * 2f);
                Gizmos.DrawWireSphere(pos + _lastFleeDirection * 2f, 0.15f);
            }

            // Current threat
            if (Application.isPlaying && _currentThreat != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(pos, _currentThreat.position);
            }
        }
#endif

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
        /// Called every time before a sound is played.
        /// Uses enableSound so you can flip it at runtime in the inspector.
        /// </summary>
        public bool ShouldPlaySound()
        {
            return enableSound;
        }
    }
}
