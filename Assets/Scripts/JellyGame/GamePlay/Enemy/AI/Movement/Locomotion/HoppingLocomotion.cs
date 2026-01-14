// FILEPATH: Assets/Scripts/AI/Movement/Locomotion/HoppingLocomotion.cs
using UnityEngine;

namespace JellyGame.GamePlay.Enemy.AI.Movement
{
    /// <summary>
    /// Discrete hopping movement locomotion with surface transition support.
    /// The agent moves in distinct hops with an arc trajectory.
    /// Suitable for slimes, frogs, or other bouncy creatures.
    /// 
    /// v2 Changes:
    /// - Detects when destination is on a different surface
    /// - Calculates reachable "landing points" on the target surface
    /// - Performs surface-transition hops with mid-air rotation
    /// - Adjusts hop distance when close to destination (no overshooting)
    /// </summary>
    public class HoppingLocomotion : LocomotionBase
    {
        // Hop state
        private bool _isHopping;
        private float _nextHopTime;
        private float _hopProgress;

        private Vector3 _hopStartPos;
        private Vector3 _hopEndPos;
        private Vector3 _hopStartUp;
        private Vector3 _hopEndUp;

        private bool _isTransitionHop;
        private Vector3 _transitionTargetNormal;

        // Destination tracking for smarter hops
        private Vector3? _currentDestination;

        public override bool IsInMotion => _isHopping;

        /// <summary>
        /// True if currently performing a hop that changes surfaces
        /// </summary>
        public bool IsTransitionHop => _isTransitionHop;

        /// <summary>
        /// The target surface normal (valid during transition hop)
        /// </summary>
        public Vector3 TransitionTargetNormal => _transitionTargetNormal;

        public HoppingLocomotion(Transform transform, LocomotionSettings settings, ISurfaceProvider surfaceProvider = null)
            : base(transform, settings, surfaceProvider)
        {
            _nextHopTime = 0f;
        }

        /// <summary>
        /// Set the current destination for smarter hop planning.
        /// Call this before Move() to enable surface-aware hopping.
        /// </summary>
        public void SetDestination(Vector3? destination)
        {
            _currentDestination = destination;
        }

        public override void Move(Vector3 direction, float deltaTime, float speedMultiplier = 1f)
        {
            if (direction.sqrMagnitude < 0.0001f)
                return;

            // Don't rotate during mid-transition hop (we handle rotation specially)
            if (!_isTransitionHop)
            {
                Rotate(direction, deltaTime);
            }

            if (_isHopping)
            {
                ContinueHop(deltaTime);
                return;
            }

            if (Time.time < _nextHopTime)
                return;

            // Use destination-aware hop if we have a destination and surface provider
            if (_currentDestination.HasValue && surfaceProvider != null)
            {
                StartHopTowardDestination(_currentDestination.Value, speedMultiplier);
            }
            else
            {
                StartBasicHop(direction, speedMultiplier);
            }
        }

        public override void Stop()
        {
            _isHopping = false;
            _hopProgress = 0f;
            _isTransitionHop = false;
        }

        public override void OnDisable()
        {
            Stop();
        }

        private void StartBasicHop(Vector3 direction, float speedMultiplier)
        {
            _nextHopTime = Time.time + Mathf.Max(0.05f, settings.HopInterval);
            _isHopping = true;
            _hopProgress = 0f;
            _isTransitionHop = false;

            _hopStartPos = transform.position;
            _hopStartUp = GetUp();
            _hopEndUp = _hopStartUp;

            float hopDistance = GetHopDistance(speedMultiplier);

            Vector3 planarForward = ProjectOnMovementPlane(transform.forward);
            if (planarForward.sqrMagnitude < 0.0001f)
                planarForward = direction;

            if (planarForward.sqrMagnitude > 0.0001f)
                planarForward.Normalize();

            Vector3 hopEndCandidate = _hopStartPos + planarForward * hopDistance;

            if (surfaceProvider != null)
            {
                _hopEndPos = surfaceProvider.GroundPosition(hopEndCandidate);
            }
            else
            {
                _hopEndPos = hopEndCandidate;
            }
        }

        private void StartHopTowardDestination(Vector3 destination, float speedMultiplier)
        {
            _nextHopTime = Time.time + Mathf.Max(0.05f, settings.HopInterval);
            _isHopping = true;
            _hopProgress = 0f;
            _isTransitionHop = false;

            _hopStartPos = transform.position;
            _hopStartUp = GetUp();
            _hopEndUp = _hopStartUp;

            float maxHopDistance = GetHopDistance(speedMultiplier);
            float hopHeight = settings.HopHeight;

            // === STEP 1: Find what surface the destination is on ===
            Vector3 destinationSurfaceNormal;
            bool foundDestSurface = TryGetSurfaceNormalAt(destination, out destinationSurfaceNormal);

            if (!foundDestSurface)
            {
                // Fallback to basic hop toward destination
                StartBasicHopToward(destination, maxHopDistance);
                return;
            }

            // === STEP 2: Check if destination is on the same surface ===
            float surfaceAngle = Vector3.Angle(_hopStartUp, destinationSurfaceNormal);
            bool isSameSurface = surfaceAngle < 30f;

            if (isSameSurface)
            {
                // Same surface - do basic hop with distance adjustment
                StartSameSurfaceHop(destination, maxHopDistance);
                return;
            }

            // === STEP 3: Different surface - calculate transition hop ===
            StartTransitionHop(destination, destinationSurfaceNormal, maxHopDistance, hopHeight);
        }

        private void StartBasicHopToward(Vector3 destination, float maxHopDistance)
        {
            Vector3 toTarget = destination - _hopStartPos;
            Vector3 planarToTarget = Vector3.ProjectOnPlane(toTarget, _hopStartUp);

            Vector3 hopDir;
            if (planarToTarget.sqrMagnitude < 0.0001f)
            {
                hopDir = transform.forward;
            }
            else
            {
                hopDir = planarToTarget.normalized;
            }

            Vector3 hopEndCandidate = _hopStartPos + hopDir * maxHopDistance;

            if (surfaceProvider != null)
            {
                _hopEndPos = surfaceProvider.GroundPosition(hopEndCandidate);
            }
            else
            {
                _hopEndPos = hopEndCandidate;
            }

            _hopEndUp = _hopStartUp;
        }

        private void StartSameSurfaceHop(Vector3 destination, float maxHopDistance)
        {
            Vector3 toTarget = destination - _hopStartPos;
            Vector3 planarToTarget = Vector3.ProjectOnPlane(toTarget, _hopStartUp);
            float planarDist = planarToTarget.magnitude;

            Vector3 hopDir;
            float hopDistance;

            if (planarDist < 0.01f)
            {
                // Very close, tiny hop or no hop
                hopDir = transform.forward;
                hopDistance = Mathf.Min(0.1f, maxHopDistance);
            }
            else
            {
                hopDir = planarToTarget.normalized;
                // Adjust hop distance to not overshoot
                hopDistance = Mathf.Min(planarDist, maxHopDistance);
            }

            Vector3 hopEndCandidate = _hopStartPos + hopDir * hopDistance;

            if (surfaceProvider != null)
            {
                _hopEndPos = surfaceProvider.GroundPosition(hopEndCandidate);
            }
            else
            {
                _hopEndPos = hopEndCandidate;
            }

            _hopEndUp = _hopStartUp;
        }

        private void StartTransitionHop(Vector3 destination, Vector3 targetSurfaceNormal, float maxHopDistance, float hopHeight)
        {
            // === Find the closest point on target surface to the destination ===
            Vector3 surfacePoint;
            if (!TryFindSurfacePoint(destination, targetSurfaceNormal, out surfacePoint))
            {
                // Can't find surface point, fall back to basic hop
                StartBasicHopToward(destination, maxHopDistance);
                return;
            }

            // === Calculate if we can reach this point ===
            Vector3 toSurfacePoint = surfacePoint - _hopStartPos;
            
            // Decompose into "planar" (along current surface) and "vertical" (along current up)
            float verticalComponent = Vector3.Dot(toSurfacePoint, _hopStartUp);
            Vector3 planarComponent = toSurfacePoint - _hopStartUp * verticalComponent;
            float planarDistance = planarComponent.magnitude;

            // Calculate effective reach considering both horizontal distance and height
            float effectiveReach = CalculateEffectiveReach(planarDistance, verticalComponent, maxHopDistance, hopHeight);

            Vector3 landingPoint;

            if (effectiveReach >= 1f)
            {
                // We can reach the surface point directly
                landingPoint = surfacePoint;
            }
            else
            {
                // We can't reach - find an intermediate point on the target surface
                landingPoint = FindReachablePointOnSurface(
                    surfacePoint, 
                    targetSurfaceNormal, 
                    _hopStartPos, 
                    maxHopDistance, 
                    hopHeight);
            }

            // Set up transition hop
            _hopEndPos = landingPoint;
            _hopEndUp = targetSurfaceNormal;
            _isTransitionHop = true;
            _transitionTargetNormal = targetSurfaceNormal;
        }

        /// <summary>
        /// Calculate how much of the distance we can cover (0 to 1+)
        /// Returns >= 1 if we can reach the target
        /// </summary>
        private float CalculateEffectiveReach(float planarDist, float verticalDist, float maxHopDist, float maxHeight)
        {
            float horizontalRatio = maxHopDist > 0.001f ? planarDist / maxHopDist : float.MaxValue;
            
            float verticalRatio;
            if (verticalDist > 0) // Going up
            {
                // Need to use hop height to reach upward
                verticalRatio = maxHeight > 0.001f ? verticalDist / (maxHeight * 1.5f) : float.MaxValue;
            }
            else // Going down or level
            {
                // Going down is "free" (gravity helps)
                verticalRatio = 0f;
            }

            // Combined reach using pythagorean-like combination
            float combinedRatio = Mathf.Sqrt(horizontalRatio * horizontalRatio + verticalRatio * verticalRatio);
            
            return combinedRatio > 0.001f ? 1f / combinedRatio : float.MaxValue;
        }

        /// <summary>
        /// Find a point on the target surface that we CAN reach
        /// </summary>
        private Vector3 FindReachablePointOnSurface(
            Vector3 idealSurfacePoint, 
            Vector3 surfaceNormal, 
            Vector3 startPos,
            float maxHopDist, 
            float maxHeight)
        {
            // Direction from ideal point toward us, projected onto surface
            Vector3 toStart = startPos - idealSurfacePoint;
            Vector3 slideDir = Vector3.ProjectOnPlane(toStart, surfaceNormal);
            
            if (slideDir.sqrMagnitude < 0.001f)
            {
                // Ideal point is directly "above" us on the new surface
                slideDir = Vector3.ProjectOnPlane(startPos - idealSurfacePoint, surfaceNormal);
            }

            if (slideDir.sqrMagnitude < 0.001f)
            {
                // Still no direction - use perpendicular to both normals
                slideDir = Vector3.Cross(_hopStartUp, surfaceNormal);
            }

            if (slideDir.sqrMagnitude > 0.001f)
                slideDir = slideDir.normalized;
            else
                return idealSurfacePoint; // Give up, return ideal

            // Search along the surface toward us until we find a reachable point
            float searchStep = 0.25f;
            float maxSearchDist = 10f;
            
            ISurfaceProvider sp = surfaceProvider;
            LayerMask surfaceLayers = GetSurfaceLayers();

            for (float offset = 0f; offset < maxSearchDist; offset += searchStep)
            {
                Vector3 candidatePoint = idealSurfacePoint + slideDir * offset;
                
                // Verify it's still on a surface
                if (Physics.Raycast(candidatePoint + surfaceNormal * 0.5f, -surfaceNormal, out RaycastHit hit, 
                    1.5f, surfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    candidatePoint = hit.point + hit.normal * 0.05f;
                    
                    // Check if reachable
                    Vector3 toCandidate = candidatePoint - startPos;
                    float vertComp = Vector3.Dot(toCandidate, _hopStartUp);
                    Vector3 planarComp = toCandidate - _hopStartUp * vertComp;
                    float planarD = planarComp.magnitude;

                    float reach = CalculateEffectiveReach(planarD, vertComp, maxHopDist, maxHeight);

                    if (reach >= 1f)
                    {
                        return candidatePoint;
                    }
                }
            }

            // Return best effort (the ideal point, even if not fully reachable)
            return idealSurfacePoint;
        }

        private bool TryGetSurfaceNormalAt(Vector3 position, out Vector3 normal)
        {
            // Try to use ISurfaceNormalQuery if available
            if (surfaceProvider is ISurfaceNormalQuery query)
            {
                return query.TryGetSurfaceNormalAt(position, out normal);
            }

            // Fallback: raycast in multiple directions
            normal = Vector3.zero;
            LayerMask surfaceLayers = GetSurfaceLayers();
            
            Vector3[] directions = new Vector3[]
            {
                Vector3.down,
                Vector3.up,
                -_hopStartUp,
                _hopStartUp,
                transform.forward,
                -transform.forward,
                transform.right,
                -transform.right
            };

            float bestDist = float.MaxValue;

            foreach (var dir in directions)
            {
                if (dir.sqrMagnitude < 0.001f) continue;
                
                Vector3 origin = position - dir.normalized * 0.5f;
                if (Physics.Raycast(origin, dir.normalized, out RaycastHit hit, 2f, surfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    if (hit.distance < bestDist)
                    {
                        bestDist = hit.distance;
                        normal = hit.normal.normalized;
                    }
                }
            }

            return normal != Vector3.zero;
        }

        private bool TryFindSurfacePoint(Vector3 nearPosition, Vector3 expectedNormal, out Vector3 surfacePoint)
        {
            surfacePoint = nearPosition;
            LayerMask surfaceLayers = GetSurfaceLayers();

            // Cast toward the surface
            Vector3 origin = nearPosition + expectedNormal * 1f;
            if (Physics.Raycast(origin, -expectedNormal, out RaycastHit hit, 3f, surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                surfacePoint = hit.point + hit.normal * 0.05f;
                return true;
            }

            // Try sphere cast for more tolerance
            if (Physics.SphereCast(origin, 0.3f, -expectedNormal, out hit, 3f, surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                surfacePoint = hit.point + hit.normal * 0.05f;
                return true;
            }

            return false;
        }

        private LayerMask GetSurfaceLayers()
        {
            // Try to get from surface provider if it exposes settings
            // Otherwise use a reasonable default
            return Physics.DefaultRaycastLayers;
        }

        public override void Rotate(Vector3 direction, float deltaTime)
        {
            if (direction.sqrMagnitude < 0.0001f)
                return;

            Vector3 up = surfaceProvider?.CurrentUp ?? Vector3.up;

            Vector3 planar = Vector3.ProjectOnPlane(direction, up);
            if (planar.sqrMagnitude < 0.0001f)
                return;

            planar.Normalize();

            Quaternion targetRot = Quaternion.LookRotation(planar, up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRot,
                settings.TurnSpeed * deltaTime
            );
        }

        private void ContinueHop(float deltaTime)
        {
            float hopDuration = Mathf.Max(0.001f, settings.HopDuration);
            _hopProgress += deltaTime / hopDuration;

            float t = Mathf.Clamp01(_hopProgress);

            // Position interpolation
            Vector3 pos = Vector3.Lerp(_hopStartPos, _hopEndPos, t);

            // Arc height
            float arc = Mathf.Sin(t * Mathf.PI) * settings.HopHeight;
            
            // For transition hops, interpolate the "up" direction for the arc
            Vector3 arcUp;
            if (_isTransitionHop)
            {
                // Blend between start and end up directions
                arcUp = Vector3.Slerp(_hopStartUp, _hopEndUp, t).normalized;
                
                // Also interpolate rotation during the hop
                Vector3 currentForward = Vector3.ProjectOnPlane(transform.forward, arcUp);
                if (currentForward.sqrMagnitude < 0.001f)
                {
                    currentForward = Vector3.ProjectOnPlane(_hopEndPos - _hopStartPos, arcUp);
                }
                if (currentForward.sqrMagnitude > 0.001f)
                {
                    currentForward.Normalize();
                    Quaternion targetRot = Quaternion.LookRotation(currentForward, arcUp);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, t);
                }
            }
            else
            {
                arcUp = _hopStartUp;
            }

            pos += arcUp * arc;

            // At end of hop, ground to surface
            if (t >= 1f)
            {
                if (_isTransitionHop)
                {
                    // Snap to target surface
                    Vector3 finalUp = _hopEndUp;
                    LayerMask surfaceLayers = GetSurfaceLayers();
                    
                    if (Physics.Raycast(pos + finalUp * 0.5f, -finalUp, out RaycastHit hit, 2f, 
                        surfaceLayers, QueryTriggerInteraction.Ignore))
                    {
                        pos = hit.point + hit.normal * 0.05f;
                        finalUp = hit.normal;
                    }

                    // Final rotation alignment
                    Vector3 finalForward = Vector3.ProjectOnPlane(transform.forward, finalUp);
                    if (finalForward.sqrMagnitude < 0.001f)
                    {
                        finalForward = Vector3.ProjectOnPlane(_hopEndPos - _hopStartPos, finalUp);
                    }
                    if (finalForward.sqrMagnitude > 0.001f)
                    {
                        finalForward.Normalize();
                        transform.rotation = Quaternion.LookRotation(finalForward, finalUp);
                    }
                }
                else if (surfaceProvider != null)
                {
                    pos = surfaceProvider.GroundPosition(pos);
                }

                _isHopping = false;
                _isTransitionHop = false;
            }

            transform.position = pos;
        }

        private float GetHopDistance(float speedMultiplier)
        {
            if (settings.HopDistance > 0f)
            {
                return settings.HopDistance * Mathf.Max(0f, speedMultiplier);
            }
            else
            {
                float currentSpeed = settings.MoveSpeed * Mathf.Max(0f, speedMultiplier);
                return currentSpeed * Mathf.Max(0.05f, settings.HopDuration);
            }
        }
    }
}