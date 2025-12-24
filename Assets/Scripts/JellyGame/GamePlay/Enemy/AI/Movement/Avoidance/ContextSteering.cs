// FILEPATH: Assets/Scripts/AI/Movement/Avoidance/ContextSteering.cs
using UnityEngine;

namespace JellyGame.GamePlay.Enemy.AI.Movement
{
    /// <summary>
    /// Context steering obstacle avoidance.
    /// Uses interest and danger maps to find the best movement direction.
    /// </summary>
    public class ContextSteering : IObstacleAvoidance
    {
        private readonly ObstacleAvoidanceSettings _settings;
        private readonly ISurfaceProvider _surfaceProvider;
        private readonly bool _debugRays;
        
        private LayerMask _activeObstacleMask;
        private Collider _ignoredCollider;

        public ContextSteering(
            ObstacleAvoidanceSettings settings, 
            ISurfaceProvider surfaceProvider = null,
            bool debugRays = false)
        {
            _settings = settings;
            _surfaceProvider = surfaceProvider;
            _debugRays = debugRays;
            _activeObstacleMask = settings.ObstacleLayers;
        }

        public void SetIgnoredCollider(Collider collider)
        {
            _ignoredCollider = collider;
        }

        public void ClearIgnoredCollider()
        {
            _ignoredCollider = null;
        }

        public void SetObstacleMask(LayerMask mask)
        {
            _activeObstacleMask = mask;
        }

        public void ResetObstacleMask()
        {
            _activeObstacleMask = _settings.ObstacleLayers;
        }

        public bool TryGetEmergencyRepulsion(Vector3 position, out Vector3 repulsionDirection)
        {
            repulsionDirection = Vector3.zero;

            Vector3 up = GetUp();
            Vector3 sensorPos = position + up * _settings.SensorVerticalOffset;

            Collider[] hits = Physics.OverlapSphere(
                sensorPos, 
                _settings.BodyRadius * 0.9f, 
                _activeObstacleMask, 
                QueryTriggerInteraction.Ignore
            );

            if (hits.Length == 0)
                return false;

            Vector3 avgPush = Vector3.zero;

            foreach (var col in hits)
            {
                if (_ignoredCollider != null && col == _ignoredCollider)
                    continue;

                Vector3 closestPoint = col.ClosestPoint(sensorPos);
                Vector3 push = sensorPos - closestPoint;
                push = Vector3.ProjectOnPlane(push, up);

                if (push.sqrMagnitude < 0.0001f)
                {
                    push = GetAnyPlanarAxis(up);
                }

                avgPush += push.normalized;
            }

            if (avgPush == Vector3.zero)
                return false;

            repulsionDirection = avgPush.normalized;

            if (_debugRays)
                Debug.DrawRay(position, repulsionDirection * 2f, Color.magenta);

            return true;
        }

        public Vector3 ComputeSafeDirection(Vector3 idealDirection, Vector3 position, Vector3 forward)
        {
            int count = Mathf.Max(4, _settings.SensorResolution);
            float[] dangerMap = new float[count];
            float[] interestMap = new float[count];
            Vector3[] directions = new Vector3[count];

            Vector3 up = GetUp();
            Vector3 baseOrigin = position + up * _settings.SensorVerticalOffset;
            float castRadius = _settings.BodyRadius + _settings.Clearance;

            Vector3 currentForwardPlanar = Vector3.ProjectOnPlane(forward, up).normalized;
            if (currentForwardPlanar.sqrMagnitude < 0.0001f)
                currentForwardPlanar = GetAnyPlanarAxis(up);

            Vector3 planarX = currentForwardPlanar;
            Vector3 planarZ = Vector3.Cross(up, planarX).normalized;
            planarX = Vector3.Cross(planarZ, up).normalized;

            for (int i = 0; i < count; i++)
            {
                float angle = i * 2f * Mathf.PI / count;

                Vector3 dir = (Mathf.Cos(angle) * planarX) + (Mathf.Sin(angle) * planarZ);
                dir = dir.normalized;
                directions[i] = dir;

                float targetDot = Vector3.Dot(dir, idealDirection);
                float targetInterest = (targetDot + 1f) * 0.5f;

                float forwardDot = Vector3.Dot(dir, currentForwardPlanar);
                float momentumInterest = (forwardDot + 1f) * 0.5f;

                interestMap[i] = (targetInterest * 0.8f) + (momentumInterest * 0.2f);

                Vector3 origin = baseOrigin - dir * (castRadius * 0.5f);

                bool hitSomething = Physics.SphereCast(
                    origin,
                    castRadius,
                    dir,
                    out RaycastHit hit,
                    _settings.LookAheadDistance + castRadius,
                    _activeObstacleMask,
                    QueryTriggerInteraction.Ignore
                );

                if (hitSomething)
                {
                    if (_ignoredCollider != null && hit.collider == _ignoredCollider)
                        continue;

                    float effectiveDistance = Mathf.Max(0.001f, hit.distance - (castRadius * 0.5f));
                    float normalized = 1f - Mathf.Clamp01(effectiveDistance / _settings.LookAheadDistance);
                    dangerMap[i] = normalized;

                    if (_debugRays)
                        Debug.DrawRay(origin, dir * hit.distance, Color.red);
                }

                if (_debugRays)
                    Debug.DrawRay(origin, dir * (_settings.LookAheadDistance + castRadius), Color.cyan);
            }

            for (int i = 0; i < count; i++)
            {
                float danger = dangerMap[i];

                if (danger >= 0.7f)
                {
                    interestMap[i] = 0f;
                }
                else if (danger > 0f)
                {
                    float safetyFactor = 1f - danger;
                    safetyFactor *= safetyFactor;
                    interestMap[i] *= safetyFactor;
                }
            }

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

            return bestDir == Vector3.zero ? Vector3.zero : bestDir.normalized;
        }

        private Vector3 GetUp()
        {
            return _surfaceProvider?.CurrentUp ?? Vector3.up;
        }

        private Vector3 GetAnyPlanarAxis(Vector3 up)
        {
            Vector3 a = Vector3.Cross(up, Vector3.right);
            if (a.sqrMagnitude < 0.0001f)
                a = Vector3.Cross(up, Vector3.forward);
            return a.normalized;
        }
    }
}