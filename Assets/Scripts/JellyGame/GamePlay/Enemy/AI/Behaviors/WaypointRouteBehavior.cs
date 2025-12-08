// FILEPATH: Assets/Scripts/AI/Behaviors/WaypointRouteBehavior.cs

using UnityEngine;

namespace JellyGame.GamePlay.Enemy.AI.Behaviors
{
    /// <summary>
    /// Waypoint-based travel behavior:
    /// - Enemy moves through a list of waypoints in order (0 → 1 → 2 → ... → 0 → ...).
    /// - When it reaches a waypoint for the FIRST time, it wanders around that point for a short time,
    ///   then moves on to the next waypoint.
    /// - It remembers which waypoints were already visited (even if another behavior temporarily takes over),
    ///   and will NOT wander again at already-visited waypoints.
    /// - When this behavior becomes active again (OnEnter), it starts from the waypoint that is
    ///   NEAREST to the enemy's current position, but still respects the "already visited" flags.
    /// - While traveling between waypoints, it performs *bounded* obstacle avoidance:
    ///   * It always wants to move toward the current waypoint.
    ///   * It computes an "away from obstacles" vector from nearby colliders on obstacleLayers.
    ///   * It blends target direction + avoidance, BUT if that blended direction stops pointing toward
    ///     the waypoint (dot <= 0), it ignores avoidance and just goes straight to the waypoint.
    ///   → So avoidance can bend the path, but never flip it or send it far away.
    /// </summary>
    [DisallowMultipleComponent]
    public class WaypointRouteBehavior : MonoBehaviour, IEnemyBehavior, IEnemySound
    {
        [Header("Behavior Priority")]
        [Tooltip("Higher value = higher priority. Route travel is usually above Wander but below Attack etc.")]
        [SerializeField] private int priority = 0;

        [Header("Waypoints")]
        [Tooltip("The ordered list of points the enemy will visit in sequence.")]
        [SerializeField] private Transform[] waypoints;

        [Tooltip("Distance at which we consider a waypoint 'reached'.")]
        [SerializeField] private float waypointReachThreshold = 0.3f;

        [Header("Travel Movement")]
        [Tooltip("Movement speed while traveling between waypoints.")]
        [SerializeField] private float travelSpeed = 2.0f;

        [Header("Wandering At Waypoint")]
        [Tooltip("How long to wander around a waypoint the first time we reach it.")]
        [SerializeField] private float wanderDuration = 2.0f;

        [Tooltip("Maximum radius (on XZ) around the waypoint to wander.")]
        [SerializeField] private float wanderRadius = 1.5f;

        [Tooltip("Speed while wandering locally around the waypoint.")]
        [SerializeField] private float wanderSpeed = 1.0f;

        [Tooltip("How often to change wander direction (seconds).")]
        [SerializeField] private float wanderDirectionChangeInterval = 1.5f;

        [Header("Obstacle Avoidance (steering)")]
        [Tooltip("Layers treated as obstacles for local steering while traveling between waypoints.")]
        [SerializeField] private LayerMask obstacleLayers;

        [Tooltip("Radius around the enemy where we look for obstacles to steer away from.")]
        [SerializeField] private float avoidRadius = 2.0f;

        [Tooltip("The enemy's physical radius - used to determine if gaps are passable.")]
        [SerializeField] private float enemyRadius = 0.4f;

        [Tooltip("Extra clearance beyond enemyRadius that we prefer to maintain from obstacles.")]
        [SerializeField] private float preferredClearance = 0.3f;

        [Tooltip("Within this distance to the waypoint we ignore avoidance and just commit to reaching it.")]
        [SerializeField] private float commitToWaypointDistance = 1.5f;

        [Tooltip("How many directions to sample when looking for a clear path (higher = smoother but more expensive).")]
        [SerializeField] private int avoidanceSamples = 12;

        [Tooltip("0 = no steering; 1 = strong steering away from obstacles.\n" +
                 "Note: steering can NEVER flip us away from the waypoint, only bend the path.")]
        [Range(0f, 1f)]
        [SerializeField] private float avoidStrength = 0.7f;

        [Header("Turning / Smoothness")]
        [Tooltip("How quickly we can change direction (degrees per second).")]
        [SerializeField] private float maxTurnDegreesPerSecond = 360f;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;
        [SerializeField] private bool debugGizmos = false;
    
        // NEW FIELDS (put before [Header("Debug")])
        [Header("Sound")]
        [Tooltip("If disabled, this behavior will not produce any sound.")]
        [SerializeField] private bool enableSound = true;

        [Tooltip("How the sound for this behavior should be played.\nNone = no sound even if enableSound is true.")]
        [SerializeField] private SoundPlaybackMode soundMode = SoundPlaybackMode.RandomInterval;

        [Tooltip("Base interval in seconds (used for FixedInterval and as MIN for RandomInterval).")]
        [SerializeField] private float soundInterval = 2.5f;

        [Tooltip("MAX interval (seconds) for RandomInterval mode. Ignored for other modes.")]
        [SerializeField] private float maxRandomInterval = 5.0f;

        [Tooltip("Name of the sound to play for this behavior (must exist in AudioSettings).")]
        [SerializeField] private string soundName = "EnemyRoute";

        [Tooltip("If true, use custom volume instead of default from AudioSettings.")]
        [SerializeField] private bool useCustomVolume = false;

        [Tooltip("Custom volume (0..1) when useCustomVolume is enabled.")]
        [Range(0f, 1f)]
        [SerializeField] private float soundVolume = 1f;

        // NEW: IEnemySound implementation (add near the bottom, before #if UNITY_EDITOR)
        // -------------------------------------------------
        // IEnemySound
        // -------------------------------------------------

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
        /// Only play sound while we are actively traveling between waypoints.
        /// Avoids spamming sound during idle / local wandering.
        /// </summary>
        public bool ShouldPlaySound()
        {
            return enableSound && _state == State.TravelingToWaypoint;
        }


        private enum State
        {
            Idle,
            TravelingToWaypoint,
            WanderingAtWaypoint
        }

        private State _state = State.Idle;

        private int _currentIndex = -1;
        private bool[] _visited;

        // Wandering state
        private Vector3 _wanderCenter;
        private Vector3 _wanderDir;
        private float _wanderTimer;
        private float _wanderDirTimer;

        public int Priority => priority;

        public bool CanActivate()
        {
            // If there are no waypoints, this behavior should never be chosen.
            return waypoints != null && waypoints.Length > 0;
        }

        public void OnEnter()
        {
            if (debugLogs)
            {
                Debug.Log("[WaypointRouteBehavior] OnEnter", this);
            }

            EnsureVisitedArray();

            // Decide where to (re)start: nearest waypoint to our current position.
            _currentIndex = FindNearestWaypointIndex();
            if (_currentIndex < 0)
            {
                _state = State.Idle;
                if (debugLogs)
                {
                    Debug.Log("[WaypointRouteBehavior] No valid waypoints found.", this);
                }
                return;
            }

            _state = State.TravelingToWaypoint;

            if (debugLogs)
            {
                Debug.Log("[WaypointRouteBehavior] Starting from waypoint index " + _currentIndex +
                          " (" + waypoints[_currentIndex].name + ")", this);
            }
        }

        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f)
                return;

            if (waypoints == null || waypoints.Length == 0 || _currentIndex < 0 || _currentIndex >= waypoints.Length)
                return;

            switch (_state)
            {
                case State.TravelingToWaypoint:
                    TickTravel(deltaTime);
                    break;

                case State.WanderingAtWaypoint:
                    TickWander(deltaTime);
                    break;

                case State.Idle:
                default:
                    // Do nothing
                    break;
            }
        }

        public void OnExit()
        {
            if (debugLogs)
            {
                Debug.Log("[WaypointRouteBehavior] OnExit", this);
            }
        }

        // -------------------------------------------------
        // Internal helpers
        // -------------------------------------------------

        private void EnsureVisitedArray()
        {
            if (waypoints == null)
            {
                _visited = null;
                return;
            }

            if (_visited == null || _visited.Length != waypoints.Length)
            {
                bool[] newVisited = new bool[waypoints.Length];

                // Try to preserve old info if lengths changed slightly
                if (_visited != null)
                {
                    int copyLen = Mathf.Min(_visited.Length, newVisited.Length);
                    for (int i = 0; i < copyLen; i++)
                        newVisited[i] = _visited[i];
                }

                _visited = newVisited;
            }
        }

        private int FindNearestWaypointIndex()
        {
            if (waypoints == null || waypoints.Length == 0)
                return -1;

            Vector3 pos = transform.position;
            int bestIndex = -1;
            float bestSqrDist = float.MaxValue;

            for (int i = 0; i < waypoints.Length; i++)
            {
                Transform wp = waypoints[i];
                if (wp == null)
                    continue;

                Vector3 wpPos = wp.position;
                Vector3 diff = wpPos - pos;
                diff.y = 0f;
                float sqrDist = diff.sqrMagnitude;

                if (sqrDist < bestSqrDist)
                {
                    bestSqrDist = sqrDist;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private void TickTravel(float deltaTime)
        {
            Transform currentWp = waypoints[_currentIndex];
            if (currentWp == null)
            {
                AdvanceToNextWaypoint();
                return;
            }

            Vector3 pos = transform.position;
            Vector3 target = currentWp.position;
            target.y = pos.y;

            Vector3 toTarget = target - pos;
            toTarget.y = 0f;
            float dist = toTarget.magnitude;

            if (dist <= waypointReachThreshold)
            {
                bool firstTimeHere = !_visited[_currentIndex];
                _visited[_currentIndex] = true;

                if (debugLogs)
                {
                    Debug.Log($"[WaypointRouteBehavior] Reached waypoint {_currentIndex}", this);
                }

                if (firstTimeHere && wanderDuration > 0f)
                {
                    StartWanderingAtCurrentWaypoint();
                }
                else
                {
                    AdvanceToNextWaypoint();
                }
                return;
            }

            Vector3 toTargetDir = toTarget / dist;
    
            // Get steering direction (handles avoidance internally)
            Vector3 desiredDir = ComputeSteeringDirection(pos, toTargetDir, dist);

            MoveInDirection(pos, desiredDir, travelSpeed, deltaTime);
        }
    
        /// <summary>
        /// Improved obstacle avoidance that:
        /// 1. Checks if direct path is clear with enough width for the enemy
        /// 2. If blocked, finds the best path around while maintaining clearance
        /// 3. Detects narrow gaps and avoids trying to squeeze through them
        /// </summary>
        private Vector3 ComputeSteeringDirection(Vector3 position, Vector3 toTargetDir, float distToTarget)
        {
            // If very close to target, just go straight
            if (distToTarget <= commitToWaypointDistance)
                return toTargetDir;

            float checkDistance = Mathf.Min(avoidRadius, distToTarget);
            float totalRadius = enemyRadius + preferredClearance;

            // First, check if direct path is clear with full clearance
            if (IsPathClear(position, toTargetDir, checkDistance, totalRadius))
            {
                // Direct path is clear - but check if we're too close to any obstacle on our sides
                // and nudge away if needed
                Vector3 nudge = ComputeClearanceNudge(position, toTargetDir, totalRadius);
                if (nudge.sqrMagnitude > 0.001f)
                {
                    Vector3 nudgedDir = (toTargetDir + nudge * 0.5f).normalized;
                    // Make sure nudge doesn't send us away from target
                    if (Vector3.Dot(nudgedDir, toTargetDir) > 0.5f)
                        return nudgedDir;
                }
                return toTargetDir;
            }

            // Direct path is blocked - find best alternative direction
            return FindBestAvoidanceDirection(position, toTargetDir, checkDistance, totalRadius);
        }

        /// <summary>
        /// Check if a path is clear using a SphereCast.
        /// </summary>
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

        /// <summary>
        /// Check if there's enough space to pass through at a given position.
        /// Uses raycasts to the left and right to measure available width.
        /// </summary>
        private bool HasEnoughClearance(Vector3 position, Vector3 moveDirection, float requiredWidth)
        {
            Vector3 right = Vector3.Cross(Vector3.up, moveDirection).normalized;
        
            // Cast rays to the left and right to find obstacles
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

        /// <summary>
        /// Compute a nudge vector to maintain clearance from nearby obstacles on our sides.
        /// This prevents wall-hugging behavior.
        /// </summary>
        private Vector3 ComputeClearanceNudge(Vector3 position, Vector3 moveDirection, float desiredClearance)
        {
            Vector3 right = Vector3.Cross(Vector3.up, moveDirection).normalized;
            Vector3 nudge = Vector3.zero;
        
            // Check left side
            if (Physics.Raycast(position, -right, out RaycastHit leftHit, desiredClearance * 1.5f, obstacleLayers, QueryTriggerInteraction.Ignore))
            {
                float penetration = desiredClearance - leftHit.distance;
                if (penetration > 0)
                {
                    nudge += right * (penetration / desiredClearance);
                }
            }
        
            // Check right side
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

        /// <summary>
        /// Find the best direction to move that avoids obstacles while staying as close
        /// to the target direction as possible. Also checks that the path has enough width.
        /// </summary>
        private Vector3 FindBestAvoidanceDirection(Vector3 position, Vector3 toTargetDir, float checkDistance, float clearanceRadius)
        {
            Vector3 bestDir = toTargetDir;
            float bestScore = float.MinValue;
        
            // We'll sample directions in a semicircle centered on the target direction
            // Starting from small angles and working outward
            float angleStep = 180f / avoidanceSamples;
        
            for (int i = 1; i <= avoidanceSamples; i++)
            {
                float angle = angleStep * i;
            
                // Try both left and right at this angle
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    float testAngle = angle * sign * 0.5f; // Divide by 2 so we cover -90 to +90
                    Vector3 testDir = Quaternion.Euler(0f, testAngle, 0f) * toTargetDir;
                
                    // Check if this direction is clear
                    if (!IsPathClear(position, testDir, checkDistance, clearanceRadius))
                        continue;
                
                    // Check if there's enough width to pass through
                    // Sample a point ahead and check clearance there
                    Vector3 aheadPos = position + testDir * (checkDistance * 0.5f);
                    if (!HasEnoughClearance(aheadPos, testDir, clearanceRadius))
                        continue;
                
                    // Score this direction - prefer directions closer to target
                    float dotScore = Vector3.Dot(testDir, toTargetDir);
                
                    // Bonus for directions that lead toward the target (check if continuing this way gets us closer)
                    Vector3 projectedPos = position + testDir * checkDistance;
                    Vector3 newToTarget = (position + toTargetDir * 100f) - projectedPos; // Approximate target in that direction
                    float progressScore = Vector3.Dot(testDir, toTargetDir);
                
                    float totalScore = dotScore + progressScore * 0.5f;
                
                    if (totalScore > bestScore)
                    {
                        bestScore = totalScore;
                        bestDir = testDir;
                    }
                }
            
                // If we found a good direction at this angle, don't search wider
                // unless the score is really bad
                if (bestScore > 0.3f)
                    break;
            }
        
            // If still no good direction, try to slide along the obstacle
            if (bestScore == float.MinValue)
            {
                bestDir = ComputeSlideDirection(position, toTargetDir, checkDistance);
            }
        
            return bestDir;
        }

        /// <summary>
        /// When completely blocked, try to slide along the obstacle surface toward the target.
        /// </summary>
        private Vector3 ComputeSlideDirection(Vector3 position, Vector3 toTargetDir, float checkDistance)
        {
            // Raycast toward target to find the obstacle
            if (Physics.Raycast(position, toTargetDir, out RaycastHit hit, checkDistance, obstacleLayers, QueryTriggerInteraction.Ignore))
            {
                // Get the surface normal (in XZ plane)
                Vector3 normal = hit.normal;
                normal.y = 0f;
                normal.Normalize();
            
                // Project our desired direction onto the surface to get slide direction
                Vector3 slideDir = Vector3.ProjectOnPlane(toTargetDir, normal).normalized;
            
                // If slide direction is valid and somewhat toward target, use it
                if (slideDir.sqrMagnitude > 0.001f && Vector3.Dot(slideDir, toTargetDir) > -0.5f)
                {
                    return slideDir;
                }
            
                // Otherwise, pick the perpendicular direction that's more toward the target
                Vector3 perpRight = Vector3.Cross(Vector3.up, normal).normalized;
                Vector3 perpLeft = -perpRight;
            
                if (Vector3.Dot(perpRight, toTargetDir) > Vector3.Dot(perpLeft, toTargetDir))
                    return perpRight;
                else
                    return perpLeft;
            }
        
            // Fallback - just go toward target
            return toTargetDir;
        }
        private void StartWanderingAtCurrentWaypoint()
        {
            _state = State.WanderingAtWaypoint;

            Transform wp = waypoints[_currentIndex];
            _wanderCenter = (wp != null) ? wp.position : transform.position;

            _wanderTimer = wanderDuration;
            _wanderDirTimer = 0f;
            _wanderDir = RandomDirectionOnXZ();

            if (debugLogs)
            {
                Debug.Log($"[WaypointRouteBehavior] Start wandering at waypoint {_currentIndex} for {wanderDuration} seconds.", this);
            }
        }

        private void TickWander(float deltaTime)
        {
            _wanderTimer -= deltaTime;
            if (_wanderTimer <= 0f)
            {
                // Done wandering at this waypoint -> move to next.
                AdvanceToNextWaypoint();
                return;
            }

            // Change wander direction every interval
            _wanderDirTimer -= deltaTime;
            if (_wanderDirTimer <= 0f || _wanderDir.sqrMagnitude < 1e-4f)
            {
                _wanderDir = RandomDirectionOnXZ();
                _wanderDirTimer = wanderDirectionChangeInterval;
            }

            // Try to move within the wanderRadius around the center.
            Vector3 pos = transform.position;
            Vector3 candidate = pos + _wanderDir * (wanderSpeed * deltaTime);

            Vector3 offset = candidate - _wanderCenter;
            offset.y = 0f;
            float offsetMag = offset.magnitude;

            if (offsetMag > wanderRadius)
            {
                // Clamp to the radius and bounce direction a bit.
                candidate = _wanderCenter + offset.normalized * wanderRadius;
                _wanderDir = Quaternion.Euler(0f, Random.Range(-90f, 90f), 0f) * (-_wanderDir);
            }

            transform.position = candidate;
    
            // Update rotation to face wander direction (so transform.forward stays valid)
            if (_wanderDir.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(_wanderDir, Vector3.up);
            }
        }
    
        private void AdvanceToNextWaypoint()
        {
            if (waypoints == null || waypoints.Length == 0)
            {
                _state = State.Idle;
                return;
            }

            _currentIndex++;
            if (_currentIndex >= waypoints.Length)
            {
                // Loop back to start
                _currentIndex = 0;
            }

            if (debugLogs)
            {
                string wpName = waypoints[_currentIndex] != null ? waypoints[_currentIndex].name : "(null)";
                Debug.Log($"[WaypointRouteBehavior] Advance to next waypoint index {_currentIndex} ({wpName}).", this);
            }

            _state = State.TravelingToWaypoint;
    
            // Immediately orient toward the new waypoint so we don't drift in the old direction
            Transform nextWp = waypoints[_currentIndex];
            if (nextWp != null)
            {
                Vector3 toNext = nextWp.position - transform.position;
                toNext.y = 0f;
                if (toNext.sqrMagnitude > 0.01f)
                {
                    transform.rotation = Quaternion.LookRotation(toNext.normalized, Vector3.up);
                }
            }
        }

        // -------------------------------------------------
        // Obstacle steering
        // -------------------------------------------------

        /// <summary>
        /// Looks for colliders on obstacleLayers within avoidRadius.
        /// Returns a direction in XZ plane pointing away from the "center of mass" of obstacles.
        /// Does NOT know anything about the waypoint, just "get away from nearby obstacles".
        /// The travel logic clamps this so it can never fully override the waypoint direction.
        /// </summary>
        private bool TryComputeAvoidDirection(Vector3 position, out Vector3 awayDir)
        {
            awayDir = Vector3.zero;

            Collider[] hits = Physics.OverlapSphere(
                position,
                avoidRadius,
                obstacleLayers,
                QueryTriggerInteraction.Ignore
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

                // Closest point on obstacle
                Vector3 closest = col.ClosestPoint(position);
                Vector3 away = position - closest;
                away.y = 0f;

                float dist = away.magnitude;
                if (dist < 1e-4f)
                {
                    // If we're basically inside it, push away from its center.
                    Vector3 fromCenter = position - col.bounds.center;
                    fromCenter.y = 0f;
                    if (fromCenter.sqrMagnitude < 1e-4f)
                        continue;

                    away = fromCenter.normalized;
                    dist = 0.001f;
                }
                else
                {
                    away /= dist; // normalize
                }

                // Closer = stronger influence
                float weight = Mathf.Clamp01((avoidRadius - dist) / avoidRadius);
                sum += away * weight;
                count++;
            }

            if (count == 0)
                return false;

            if (sum.sqrMagnitude < 1e-4f)
                return false;

            awayDir = sum.normalized;
            return true;
        }

        private void MoveInDirection(Vector3 currentPos, Vector3 desiredDir, float speed, float deltaTime)
        {
            desiredDir.y = 0f;
            if (desiredDir.sqrMagnitude < 1e-4f)
                return;
            desiredDir.Normalize();

            // MOVE directly in the desired direction (no turn limitation on movement)
            Vector3 newPos = currentPos + desiredDir * (speed * deltaTime);
            transform.position = newPos;

            // ROTATE smoothly toward the desired direction (visual only, doesn't affect movement)
            Vector3 currentForward = transform.forward;
            currentForward.y = 0f;
            if (currentForward.sqrMagnitude < 1e-4f)
                currentForward = desiredDir;
            currentForward.Normalize();

            Quaternion fromRot = Quaternion.LookRotation(currentForward, Vector3.up);
            Quaternion toRot = Quaternion.LookRotation(desiredDir, Vector3.up);
            float maxAngle = maxTurnDegreesPerSecond * deltaTime;
            transform.rotation = Quaternion.RotateTowards(fromRot, toRot, maxAngle);
        }

        private static Vector3 RandomDirectionOnXZ()
        {
            Vector2 v2 = Random.insideUnitCircle.normalized;
            if (v2.sqrMagnitude < 1e-4f)
                v2 = Vector2.right;
            return new Vector3(v2.x, 0f, v2.y);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!debugGizmos || waypoints == null || waypoints.Length == 0)
                return;

            // Draw waypoint spheres and lines in order
            Gizmos.color = new Color(0.4f, 1f, 0.4f, 0.6f);

            Transform prev = null;
            for (int i = 0; i < waypoints.Length; i++)
            {
                Transform wp = waypoints[i];
                if (wp == null)
                    continue;

                Gizmos.DrawSphere(wp.position, 0.2f);

                if (prev != null)
                {
                    Gizmos.DrawLine(prev.position, wp.position);
                }

                prev = wp;
            }

            // Close the loop visually
            if (waypoints.Length > 1 && waypoints[0] != null && prev != null && prev != waypoints[0])
            {
                Gizmos.DrawLine(prev.position, waypoints[0].position);
            }

            // Wander radius at current waypoint
            if (Application.isPlaying && _currentIndex >= 0 && _currentIndex < waypoints.Length)
            {
                Transform wp = waypoints[_currentIndex];
                if (wp != null)
                {
                    Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.3f);
                    Gizmos.DrawWireSphere(wp.position, wanderRadius);
                }
            }

            // Obstacle avoid radius around enemy
            if (Application.isPlaying)
            {
                Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.25f);
                Gizmos.DrawWireSphere(transform.position, avoidRadius);
            }
        }
#endif
    }
}
