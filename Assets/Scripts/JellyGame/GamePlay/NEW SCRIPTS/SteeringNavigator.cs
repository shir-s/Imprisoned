// FILEPATH: Assets/Scripts/AI/Movement/SteeringNavigator.cs

using UnityEngine;

namespace JellyGame.GamePlay.Enemy.AI.Movement
{
    /// <summary>
    /// The "Driver" of the AI.
    /// It accepts a target destination and calculates the best physical path to take,
    /// handling obstacle avoidance, narrow gaps, and smoothing automatically.
    /// </summary>
    public class SteeringNavigator : MonoBehaviour
    {
        [Header("Agent Size")]
        [Tooltip("Radius of the agent (used for collision checks).")]
        [SerializeField] private float bodyRadius = 0.5f;
        [Tooltip("Extra space to keep away from walls.")]
        [SerializeField] private float clearance = 0.2f;

        [Header("Sensors")]
        [Tooltip("Layers considered as obstacles for steering.")]
        [SerializeField] private LayerMask obstacleLayers;
        [Tooltip("How far ahead to look for obstacles.")]
        [SerializeField] private float lookAheadDistance = 2.0f;
        [Tooltip("Height offset above the agent center for obstacle sensors.")]
        [SerializeField] private float sensorVerticalOffset = 0.1f;
        [Tooltip("Higher = smoother movement, more expensive. 12-16 is good.")]
        [SerializeField] private int sensorResolution = 16;

        [Header("Movement")]
        [SerializeField] private float moveSpeed = 3f;
        [SerializeField] private float turnSpeed = 720f;

        [Header("Debug")]
        [SerializeField] private bool drawSensorRays = false;
        [SerializeField] private bool drawDangerRays = false;

        // The current destination requested by a Behavior script
        private Vector3? _currentTarget;
        private bool _isStopped = true;

        // Visualization
        private Vector3 _debugBestDir;

        /// <summary>
        /// Call this from your Behavior script to tell the agent where to go.
        /// </summary>
        public void SetDestination(Vector3 targetPoint)
        {
            _currentTarget = targetPoint;
            _isStopped = false;
        }

        /// <summary>
        /// Call this to stop the agent immediately.
        /// </summary>
        public void Stop()
        {
            _currentTarget = null;
            _isStopped = true;
        }

        /// <summary>
        /// Returns true if we are very close to the destination.
        /// </summary>
        public bool HasReachedDestination(float threshold = 0.2f)
        {
            if (_currentTarget == null) return true;

            Vector3 target = _currentTarget.Value;
            Vector3 diff = transform.position - target;
            diff.y = 0;
            return diff.magnitude < threshold;
        }

        private void Update()
        {
            if (_isStopped || _currentTarget == null)
                return;

            // 1. Calculate the Ideal Direction (straight to target)
            Vector3 targetPos = _currentTarget.Value;
            // Keep target at same Y level to avoid tilting up/down
            targetPos.y = transform.position.y;

            Vector3 idealDir = (targetPos - transform.position).normalized;
            float distToTarget = Vector3.Distance(transform.position, targetPos);

            // 2. Compute the Best Steering Direction (Avoidance Logic)
            Vector3 finalDir = idealDir;

            if (distToTarget > 0.5f)
            {
                finalDir = ComputeContextSteering(idealDir);
            }

            // 3. Apply Movement
            MoveAndRotate(finalDir, Time.deltaTime);

            // Debug
            _debugBestDir = finalDir;
        }

        private Vector3 ComputeContextSteering(Vector3 idealDir)
        {
            int count = Mathf.Max(4, sensorResolution);
            float[] dangerMap = new float[count];
            float[] interestMap = new float[count];
            Vector3[] directions = new Vector3[count];

            // Base origin slightly above the surface
            Vector3 baseOrigin = transform.position + transform.up * sensorVerticalOffset;
            float castRadius = bodyRadius + clearance;

            // A. Build maps
            for (int i = 0; i < count; i++)
            {
                float angle = i * 2f * Mathf.PI / count;
                // World-space directions in XZ plane
                Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                directions[i] = dir;

                // 1. Interest: alignment with idealDir (0..1)
                float dot = Vector3.Dot(dir, idealDir);
                interestMap[i] = Mathf.Max(0f, dot);

                // 2. Danger: spherecast in this direction
                // IMPORTANT: start the cast a bit *behind* the agent in this direction.
                // If we start exactly at the center, once we are very close or slightly
                // inside the collider, SphereCast stops hitting and we "lose" the wall.
                Vector3 origin = baseOrigin - dir * castRadius;

                bool hitSomething = Physics.SphereCast(
                    origin,
                    castRadius,
                    dir,
                    out RaycastHit hit,
                    lookAheadDistance + castRadius,   // extra so we still see walls when close
                    obstacleLayers,
                    QueryTriggerInteraction.Ignore
                );

                if (hitSomething)
                {
                    // Convert distance to [0,1] danger:
                    // 0 = far (no danger), 1 = very close.
                    float effectiveDistance = Mathf.Max(0.001f, hit.distance - castRadius);
                    float normalized = 1f - Mathf.Clamp01(effectiveDistance / lookAheadDistance);
                    dangerMap[i] = normalized;

                    if (drawDangerRays)
                    {
                        Debug.DrawRay(origin, dir * hit.distance, Color.red);
                    }
                }
                else if (drawSensorRays)
                {
                    Debug.DrawRay(origin, dir * (lookAheadDistance + castRadius), Color.cyan);
                }
            }

            // B. Combine maps – reduce or kill interest based on danger
            for (int i = 0; i < count; i++)
            {
                float danger = dangerMap[i];

                if (danger >= 0.8f)
                {
                    // Direction basically blocked
                    interestMap[i] = 0f;
                }
                else if (danger > 0f)
                {
                    // Gradually reduce interest based on danger
                    interestMap[i] *= (1f - danger);
                }
            }

            // C. Pick best direction
            Vector3 bestDir = Vector3.zero;
            float bestScore = 0f;

            for (int i = 0; i < count; i++)
            {
                float score = interestMap[i];
                if (score > bestScore)
                {
                    bestScore = score;
                    bestDir = directions[i];
                }
            }

            // If we are totally blocked, just keep current forward to avoid jitter
            if (bestDir == Vector3.zero)
                return transform.forward;

            return bestDir.normalized;
        }

        private void MoveAndRotate(Vector3 dir, float dt)
        {
            if (dir == Vector3.zero) return;

            // Rotation
            Quaternion targetRot = Quaternion.LookRotation(dir, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * dt);

            // Translation
            transform.position += transform.forward * moveSpeed * dt;
        }

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying) return;

            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, transform.position + _debugBestDir * 2f);
            Gizmos.color = Color.yellow;

            if (_currentTarget != null)
            {
                Gizmos.DrawWireSphere(_currentTarget.Value, 0.3f);
            }
        }
    }
}
