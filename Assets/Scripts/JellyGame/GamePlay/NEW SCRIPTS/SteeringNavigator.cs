// FILEPATH: Assets/Scripts/AI/Movement/SteeringNavigator.cs

using UnityEngine;

namespace JellyGame.GamePlay.Enemy.AI.Movement
{
    public class SteeringNavigator : MonoBehaviour
    {
        [Header("Agent Size")]
        [SerializeField] private float bodyRadius = 0.5f;
        [SerializeField] private float clearance = 0.2f;

        [Header("Sensors")]
        [SerializeField] private LayerMask obstacleLayers; 
        [SerializeField] private float lookAheadDistance = 2.0f;
        [SerializeField] private float sensorVerticalOffset = 0.1f;
        [SerializeField] private int sensorResolution = 16;

        [Header("Movement")]
        [SerializeField] private float moveSpeed = 3f;
        [SerializeField] private float turnSpeed = 720f;

        [Header("Debug")]
        [SerializeField] private bool drawSensorRays = false;
        [SerializeField] private bool drawDangerRays = false;
        [SerializeField] private bool drawRepulsion = true;
        [SerializeField] private bool drawBestDir = true;

        private Vector3? _currentTarget;
        private bool _isStopped = true;
        private LayerMask _activeObstacleMask; 
        private Vector3 _debugBestDir;

        private void Awake()
        {
            _activeObstacleMask = obstacleLayers;
        }

        public void SetObstacleMask(LayerMask newMask) { _activeObstacleMask = newMask; }
        public void ResetObstacleMask() { _activeObstacleMask = obstacleLayers; }

        public void SetDestination(Vector3 targetPoint)
        {
            _currentTarget = targetPoint;
            _isStopped = false;
        }

        public void Stop()
        {
            _currentTarget = null;
            _isStopped = true;
        }

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
            if (_isStopped || _currentTarget == null) return;

            Vector3 targetPos = _currentTarget.Value;
            targetPos.y = transform.position.y;

            Vector3 idealDir = (targetPos - transform.position).normalized;
            float distToTarget = Vector3.Distance(transform.position, targetPos);

            Vector3 finalDir = idealDir;

            if (distToTarget > 0.5f)
            {
                // Emergency Repulsion (Stops us from walking THROUGH walls)
                if (TryGetEmergencyRepulsion(out Vector3 repulsionDir))
                {
                    finalDir = repulsionDir;
                }
                else
                {
                    // Context Steering (Stops us from getting STUCK on walls)
                    finalDir = ComputeContextSteering(idealDir);
                }
            }

            MoveAndRotate(finalDir, Time.deltaTime);
            _debugBestDir = finalDir;
        }

        private bool TryGetEmergencyRepulsion(out Vector3 repulsionDir)
        {
            repulsionDir = Vector3.zero;
            Vector3 sensorPos = transform.position + transform.up * sensorVerticalOffset;
            
            Collider[] hits = Physics.OverlapSphere(sensorPos, bodyRadius * 0.9f, _activeObstacleMask, QueryTriggerInteraction.Ignore);

            if (hits.Length > 0)
            {
                Vector3 avgPush = Vector3.zero;
                foreach (var col in hits)
                {
                    Vector3 closestPoint = col.ClosestPoint(sensorPos);
                    Vector3 push = sensorPos - closestPoint;
                    push.y = 0; 
                    if (push.sqrMagnitude < 0.0001f) push = transform.forward;
                    avgPush += push.normalized;
                }

                if (avgPush != Vector3.zero)
                {
                    repulsionDir = avgPush.normalized;
                    if(drawRepulsion) Debug.DrawRay(transform.position, repulsionDir * 2f, Color.magenta);
                    return true;
                }
            }
            return false;
        }

        private Vector3 ComputeContextSteering(Vector3 idealDir)
        {
            int count = Mathf.Max(4, sensorResolution);
            float[] dangerMap = new float[count];
            float[] interestMap = new float[count];
            Vector3[] directions = new Vector3[count];

            Vector3 baseOrigin = transform.position + transform.up * sensorVerticalOffset;
            float castRadius = bodyRadius + clearance;

            // Pre-calculate Momentum Vector (Current Forward)
            Vector3 currentForward = transform.forward;

            for (int i = 0; i < count; i++)
            {
                float angle = i * 2f * Mathf.PI / count;
                Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                directions[i] = dir;

                // --- CHANGE 1: WIDER INTEREST ---
                // Old: Mathf.Max(0, dot). This killed any path > 90 degrees away.
                // New: Map [-1, 1] to [0, 1]. This allows turning 90 degrees (interest 0.5) to avoid a wall.
                float targetDot = Vector3.Dot(dir, idealDir);
                float targetInterest = (targetDot + 1f) * 0.5f; 

                // --- CHANGE 2: MOMENTUM ---
                // Bias slightly towards where we are currently facing to prevent jitter/circles.
                float forwardDot = Vector3.Dot(dir, currentForward);
                float momentumInterest = (forwardDot + 1f) * 0.5f;

                // Blend: 80% Target, 20% Momentum
                interestMap[i] = (targetInterest * 0.8f) + (momentumInterest * 0.2f);

                // Danger Scan
                Vector3 origin = baseOrigin - dir * (castRadius * 0.5f); 
                bool hitSomething = Physics.SphereCast(
                    origin,
                    castRadius,
                    dir,
                    out RaycastHit hit,
                    lookAheadDistance + castRadius,
                    _activeObstacleMask,
                    QueryTriggerInteraction.Ignore
                );

                if (hitSomething)
                {
                    float effectiveDistance = Mathf.Max(0.001f, hit.distance - (castRadius * 0.5f));
                    float normalized = 1f - Mathf.Clamp01(effectiveDistance / lookAheadDistance);
                    dangerMap[i] = normalized;

                    if (drawDangerRays) Debug.DrawRay(origin, dir * hit.distance, Color.red);
                }
            }

            // --- CHANGE 3: AGGRESSIVE DANGER FALLOFF ---
            for (int i = 0; i < count; i++)
            {
                float danger = dangerMap[i];
                
                // If danger is high, kill interest completely
                if (danger >= 0.7f) 
                {
                    interestMap[i] = 0f;
                }
                else if (danger > 0f) 
                {
                    // Apply a steep curve. Even small danger reduces interest significantly.
                    // This ensures "Safe Sideways" > "Dangerous Forward"
                    float safetyFactor = 1f - danger;
                    safetyFactor = safetyFactor * safetyFactor; // Square it to punish danger harder
                    interestMap[i] *= safetyFactor;
                }
            }

            // Pick Winner
            Vector3 bestDir = Vector3.zero;
            float bestScore = 0f;

            for (int i = 0; i < count; i++)
            {
                if (interestMap[i] > bestScore)
                {
                    bestScore = interestMap[i];
                    bestDir = directions[i];
                }
            }

            // If completely blocked, stop.
            if (bestDir == Vector3.zero) return Vector3.zero; 

            return bestDir.normalized;
        }

        private void MoveAndRotate(Vector3 dir, float dt)
        {
            if (dir == Vector3.zero) return; 

            Quaternion targetRot = Quaternion.LookRotation(dir, Vector3.up);
            // Spherically interpolate rotation for smoother turning
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * dt);
            transform.position += transform.forward * moveSpeed * dt;
        }

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying) return;
            if (drawBestDir)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawLine(transform.position, transform.position + _debugBestDir * 2f);
            }
            Gizmos.color = Color.yellow;
            if (_currentTarget != null) Gizmos.DrawWireSphere(_currentTarget.Value, 0.3f);
        }
    }
}