// FILEPATH: Assets/Scripts/AI/Movement/Locomotion/HoppingLocomotion.cs
using UnityEngine;

namespace JellyGame.GamePlay.Enemy.AI.Movement
{
    /// <summary>
    /// Discrete hopping movement locomotion with surface transition support.
    /// The agent moves in distinct hops with an arc trajectory.
    /// Suitable for slimes, frogs, or other bouncy creatures.
    /// 
    /// v8 Changes:
    /// - Fixed wall-to-floor transition detection (Check 4 was checking INTO wall, not world-down)
    /// - Added Check 5: Destination surface detection - checks what surface the destination is on
    /// - When on a non-horizontal surface and destination is on floor, properly detects transition
    /// </summary>
    public class HoppingLocomotion : LocomotionBase
    {
        // Debug settings
        private bool _debugLogs = false;
        
        /// <summary>
        /// Enable or disable debug logging for hop diagnostics
        /// </summary>
        public void SetDebugLogs(bool enabled) => _debugLogs = enabled;
        
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
        
        // IMPORTANT: Track our own surface normal (surfaceProvider may not update after transitions)
        private Vector3 _currentSurfaceUp = Vector3.up;
        
        // Landing diagnostics
        private int _hopCount = 0;
        private float _lastLandingDistance = 0f;
        
        // Transition settings
        private const float MAX_TRANSITION_DISTANCE = 6.0f;
        private const float EDGE_DETECTION_DISTANCE = 3.0f;
        
        /// <summary>
        /// Distance from surface at last landing (for diagnostics)
        /// </summary>
        public float LastLandingDistance => _lastLandingDistance;
        
        /// <summary>
        /// The current surface normal this locomotion thinks it's on
        /// </summary>
        public Vector3 CurrentSurfaceUp => _currentSurfaceUp;

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
            _currentSurfaceUp = transform.up;
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

            // Use destination-aware hop if we have a destination
            if (_currentDestination.HasValue)
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
            _hopCount++;

            _hopStartPos = transform.position;
            _hopStartUp = _currentSurfaceUp;
            _hopEndUp = _hopStartUp;

            float hopDistance = GetHopDistance(speedMultiplier);

            Vector3 planarForward = Vector3.ProjectOnPlane(transform.forward, _hopStartUp);
            if (planarForward.sqrMagnitude < 0.0001f)
                planarForward = direction;

            if (planarForward.sqrMagnitude > 0.0001f)
                planarForward.Normalize();

            Vector3 hopEndCandidate = _hopStartPos + planarForward * hopDistance;

            _hopEndPos = GroundPositionOnSurface(hopEndCandidate, _hopStartUp);
            
            if (_debugLogs)
            {
                Debug.Log($"[HoppingLoco] Hop #{_hopCount} START (basic): " +
                    $"startPos={V(_hopStartPos)} endPos={V(_hopEndPos)} " +
                    $"distance={hopDistance:F2} up={V(_hopStartUp)}");
            }
        }

        private void StartHopTowardDestination(Vector3 destination, float speedMultiplier)
        {
            _nextHopTime = Time.time + Mathf.Max(0.05f, settings.HopInterval);
            _isHopping = true;
            _hopProgress = 0f;
            _isTransitionHop = false;
            _hopCount++;

            _hopStartPos = transform.position;
            _hopStartUp = _currentSurfaceUp;
            _hopEndUp = _hopStartUp;

            float maxHopDistance = GetHopDistance(speedMultiplier);

            // Get both the planar move direction AND the raw direction to destination
            Vector3 toDestination = destination - _hopStartPos;
            Vector3 rawDirToDestination = toDestination.normalized;
            Vector3 planarMoveDir = GetPlanarMoveDirection(destination);

            // === STEP 1: Check for nearby surface transition ===
            // Use BOTH planar direction (for walls ahead) AND raw direction (for surfaces the destination is on)
            if (TryDetectNearbyTransition(destination, planarMoveDir, rawDirToDestination, maxHopDistance, out Vector3 transitionNormal, out Vector3 transitionPoint))
            {
                if (_debugLogs)
                    Debug.Log($"[HoppingLoco] Hop #{_hopCount}: Nearby transition detected! normal={V(transitionNormal)} point={V(transitionPoint)}");
                
                StartNearbyTransitionHop(transitionNormal, transitionPoint, maxHopDistance);
                return;
            }

            // === STEP 2: No nearby transition - do a normal hop toward destination ===
            if (_debugLogs)
                Debug.Log($"[HoppingLoco] Hop #{_hopCount}: No nearby transition, doing normal hop toward destination");
            
            StartSameSurfaceHop(destination, maxHopDistance);
        }

        /// <summary>
        /// Get the planar movement direction toward a destination (projected onto current surface).
        /// </summary>
        private Vector3 GetPlanarMoveDirection(Vector3 destination)
        {
            Vector3 toTarget = destination - _hopStartPos;
            Vector3 planarToTarget = Vector3.ProjectOnPlane(toTarget, _hopStartUp);
            
            if (planarToTarget.sqrMagnitude < 0.0001f)
            {
                return Vector3.ProjectOnPlane(transform.forward, _hopStartUp).normalized;
            }
            
            return planarToTarget.normalized;
        }

        /// <summary>
        /// Detect if there's a surface transition nearby.
        /// Checks both the planar direction (walls ahead) and raw direction (destination's surface).
        /// </summary>
        private bool TryDetectNearbyTransition(Vector3 destination, Vector3 planarMoveDir, Vector3 rawDirToDestination, float hopDistance, 
            out Vector3 transitionNormal, out Vector3 transitionPoint)
        {
            transitionNormal = Vector3.zero;
            transitionPoint = Vector3.zero;
            
            LayerMask surfaceLayers = GetSurfaceLayers();
            float checkDistance = Mathf.Max(hopDistance, EDGE_DETECTION_DISTANCE);
            
            // === Check 1: Surface in RAW direction to destination ===
            // This catches floors when on walls, walls when destination is around corner, etc.
            Vector3 rawCheckOrigin = _hopStartPos + _hopStartUp * 0.2f;
            
            if (Physics.Raycast(rawCheckOrigin, rawDirToDestination, out RaycastHit rawHit, checkDistance, 
                surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                Vector3 hitNormal = rawHit.normal.normalized;
                float angleToSurface = Vector3.Angle(_hopStartUp, hitNormal);
                
                if (_debugLogs)
                    Debug.Log($"[HoppingLoco] Raw direction check: hit {rawHit.collider.name} at dist={rawHit.distance:F2} normal={V(hitNormal)} angle={angleToSurface:F1}°");
                
                // Is this a different surface?
                if (angleToSurface > 30f && rawHit.distance < MAX_TRANSITION_DISTANCE)
                {
                    transitionNormal = hitNormal;
                    transitionPoint = rawHit.point + hitNormal * 0.05f;
                    return true;
                }
            }
            
            // === Check 2: Wall directly ahead (in planar direction) ===
            Vector3 wallCheckOrigin = _hopStartPos + _hopStartUp * 0.2f;
            
            if (Physics.Raycast(wallCheckOrigin, planarMoveDir, out RaycastHit wallHit, checkDistance, 
                surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                Vector3 wallNormal = wallHit.normal.normalized;
                float angleToWall = Vector3.Angle(_hopStartUp, wallNormal);
                
                if (_debugLogs)
                    Debug.Log($"[HoppingLoco] Planar direction check: hit {wallHit.collider.name} at dist={wallHit.distance:F2} normal={V(wallNormal)} angle={angleToWall:F1}°");
                
                // Is this actually a different surface (wall/ceiling)?
                if (angleToWall > 30f && wallHit.distance < MAX_TRANSITION_DISTANCE)
                {
                    transitionNormal = wallNormal;
                    transitionPoint = wallHit.point + wallNormal * 0.05f;
                    return true;
                }
            }
            
            // === Check 3: Edge/dropoff ahead (in planar direction) ===
            Vector3 aheadPos = _hopStartPos + planarMoveDir * checkDistance;
            Vector3 edgeCheckOrigin = aheadPos + _hopStartUp * 0.5f;
            
            // Check if ground continues ahead
            if (!Physics.Raycast(edgeCheckOrigin, -_hopStartUp, out RaycastHit groundAhead, 1.5f, 
                surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                // No ground ahead - we're at an edge!
                if (_debugLogs)
                    Debug.Log($"[HoppingLoco] Edge detected at {V(aheadPos)} - searching for adjacent surface");
                
                // Search for the surface below the edge
                if (TryFindSurfaceAtEdge(aheadPos, planarMoveDir, out Vector3 edgeSurfaceNormal, out Vector3 edgeSurfacePoint))
                {
                    float angleToEdgeSurface = Vector3.Angle(_hopStartUp, edgeSurfaceNormal);
                    
                    if (angleToEdgeSurface > 30f)
                    {
                        transitionNormal = edgeSurfaceNormal;
                        transitionPoint = edgeSurfacePoint;
                        return true;
                    }
                }
            }
            
            // === Check 4: WORLD DOWN for floor when on wall/ceiling ===
            // FIXED: Previously used -_hopStartUp which on a wall points INTO the wall, not toward floor!
            // Now we check world-down when we're on a non-horizontal surface
            float currentSurfaceVerticalDot = Mathf.Abs(Vector3.Dot(_hopStartUp, Vector3.up));
            bool isOnWallOrCeiling = currentSurfaceVerticalDot < 0.7f; // Surface is more than ~45° from horizontal
            
            if (isOnWallOrCeiling)
            {
                Vector3 worldDownOrigin = _hopStartPos + Vector3.up * 0.2f;
                
                if (Physics.Raycast(worldDownOrigin, Vector3.down, out RaycastHit floorHit, MAX_TRANSITION_DISTANCE, 
                    surfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    Vector3 floorNormal = floorHit.normal.normalized;
                    float angleToFloor = Vector3.Angle(_hopStartUp, floorNormal);
                    
                    // Check if this floor is close AND if destination is lower than us (suggesting we need to go down)
                    bool destIsLower = destination.y < _hopStartPos.y - 0.5f;
                    bool floorIsClose = floorHit.distance < 3.0f;
                    
                    if (angleToFloor > 30f && (floorIsClose || destIsLower))
                    {
                        if (_debugLogs)
                            Debug.Log($"[HoppingLoco] World-down check: hit {floorHit.collider.name} at dist={floorHit.distance:F2} normal={V(floorNormal)} angle={angleToFloor:F1}° destIsLower={destIsLower}");
                        
                        transitionNormal = floorNormal;
                        transitionPoint = floorHit.point + floorNormal * 0.05f;
                        return true;
                    }
                }
            }
            
            // === Check 5: What surface is the DESTINATION on? ===
            // This is crucial for wall-to-floor transitions when destination is on a different surface type
            if (TryGetDestinationSurface(destination, out Vector3 destSurfaceNormal, out Vector3 destSurfacePoint))
            {
                float angleToDestSurface = Vector3.Angle(_hopStartUp, destSurfaceNormal);
                
                if (angleToDestSurface > 30f) // Destination is on a fundamentally different surface
                {
                    float distToDest = Vector3.Distance(_hopStartPos, destination);
                    
                    // Only trigger if we're close enough to reasonably transition
                    if (distToDest < MAX_TRANSITION_DISTANCE * 2f)
                    {
                        if (_debugLogs)
                            Debug.Log($"[HoppingLoco] Destination surface check: dest is on surface with normal={V(destSurfaceNormal)} angle={angleToDestSurface:F1}° distToDest={distToDest:F2}");
                        
                        // Use the destination surface as our target
                        transitionNormal = destSurfaceNormal;
                        
                        // Calculate a reasonable transition point - either the destination itself or
                        // a point on the target surface between us and the destination
                        if (distToDest < hopDistance * 1.5f)
                        {
                            // Close enough to hop directly to destination area
                            transitionPoint = destSurfacePoint;
                        }
                        else
                        {
                            // Find an intermediate point on the target surface
                            transitionPoint = FindTransitionPointTowardDestination(destination, destSurfaceNormal, hopDistance);
                        }
                        
                        return true;
                    }
                }
            }
            
            return false;
        }

        /// <summary>
        /// Try to determine what surface the destination is on.
        /// </summary>
        private bool TryGetDestinationSurface(Vector3 destination, out Vector3 surfaceNormal, out Vector3 surfacePoint)
        {
            surfaceNormal = Vector3.zero;
            surfacePoint = Vector3.zero;
            
            LayerMask surfaceLayers = GetSurfaceLayers();
            
            // Cast down from above the destination to find what surface it's on
            Vector3 checkOrigin = destination + Vector3.up * 2.0f;
            
            if (Physics.Raycast(checkOrigin, Vector3.down, out RaycastHit hit, 4f, 
                surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                surfaceNormal = hit.normal.normalized;
                surfacePoint = hit.point + hit.normal * 0.05f;
                return true;
            }
            
            // Try casting from multiple directions in case destination is on a wall
            Vector3[] checkDirs = new Vector3[]
            {
                Vector3.up,     // Destination might be on ceiling
                Vector3.left,   // On wall facing right
                Vector3.right,  // On wall facing left
                Vector3.forward,
                Vector3.back
            };
            
            foreach (var dir in checkDirs)
            {
                checkOrigin = destination + dir * 1.5f;
                if (Physics.Raycast(checkOrigin, -dir, out hit, 3f, surfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    surfaceNormal = hit.normal.normalized;
                    surfacePoint = hit.point + hit.normal * 0.05f;
                    return true;
                }
            }
            
            return false;
        }

        /// <summary>
        /// Find a good transition point toward the destination surface.
        /// </summary>
        private Vector3 FindTransitionPointTowardDestination(Vector3 destination, Vector3 targetSurfaceNormal, float maxDistance)
        {
            LayerMask surfaceLayers = GetSurfaceLayers();
            
            // Direction from us toward destination
            Vector3 towardDest = (destination - _hopStartPos).normalized;
            
            // Cast toward destination to find where the target surface is
            Vector3 origin = _hopStartPos + _hopStartUp * 0.2f;
            
            if (Physics.Raycast(origin, towardDest, out RaycastHit hit, maxDistance * 2f, 
                surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                // Check if this hit is on a surface with similar normal to our target
                float angleDiff = Vector3.Angle(hit.normal, targetSurfaceNormal);
                if (angleDiff < 30f)
                {
                    return hit.point + hit.normal * 0.05f;
                }
            }
            
            // Fallback: cast from between us and destination downward
            Vector3 midPoint = Vector3.Lerp(_hopStartPos, destination, 0.5f);
            midPoint.y = Mathf.Max(_hopStartPos.y, destination.y) + 1f;
            
            if (Physics.Raycast(midPoint, Vector3.down, out hit, 5f, surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                float angleDiff = Vector3.Angle(hit.normal, targetSurfaceNormal);
                if (angleDiff < 30f)
                {
                    return hit.point + hit.normal * 0.05f;
                }
            }
            
            // Last resort: just use the destination
            return destination;
        }

        /// <summary>
        /// Find what surface exists at/below an edge point.
        /// </summary>
        private bool TryFindSurfaceAtEdge(Vector3 edgePos, Vector3 moveDir, out Vector3 surfaceNormal, out Vector3 surfacePoint)
        {
            surfaceNormal = Vector3.zero;
            surfacePoint = Vector3.zero;
            
            LayerMask surfaceLayers = GetSurfaceLayers();
            
            // Try different ray directions to find the adjacent surface
            Vector3[] searchDirs = new Vector3[]
            {
                -_hopStartUp,                           // Straight down (relative to current surface)
                (moveDir - _hopStartUp).normalized,    // Forward-down (wrap around edge)
                moveDir,                                // Forward (wall below edge)
                -moveDir,                               // Behind (wall we're walking off)
                Vector3.down,                           // World down
            };

            foreach (var dir in searchDirs)
            {
                if (dir.sqrMagnitude < 0.001f) continue;
                
                Vector3 origin = edgePos + _hopStartUp * 0.2f;
                
                if (Physics.Raycast(origin, dir, out RaycastHit hit, 3f, surfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    float angle = Vector3.Angle(_hopStartUp, hit.normal);
                    
                    if (angle > 30f) // Different surface
                    {
                        surfaceNormal = hit.normal.normalized;
                        surfacePoint = hit.point + hit.normal * 0.05f;
                        
                        if (_debugLogs)
                            Debug.Log($"[HoppingLoco] Found edge surface via dir={V(dir)}: normal={V(surfaceNormal)} point={V(surfacePoint)}");
                        
                        return true;
                    }
                }
            }
            
            return false;
        }

        /// <summary>
        /// Start a transition hop to a nearby surface (wall or edge).
        /// </summary>
        private void StartNearbyTransitionHop(Vector3 targetNormal, Vector3 targetPoint, float maxHopDistance)
        {
            // Calculate landing point - should be ON the target surface, close to us
            Vector3 toTarget = targetPoint - _hopStartPos;
            float distanceToTarget = toTarget.magnitude;
            
            Vector3 landingPoint;
            
            if (distanceToTarget <= maxHopDistance)
            {
                // We can reach the target point directly
                landingPoint = targetPoint;
            }
            else
            {
                // Hop as far as we can toward the target
                landingPoint = _hopStartPos + toTarget.normalized * maxHopDistance;
                
                // Ground this point on the target surface
                landingPoint = GroundPositionOnSurface(landingPoint, targetNormal);
            }

            _hopEndPos = landingPoint;
            _hopEndUp = targetNormal;
            _isTransitionHop = true;
            _transitionTargetNormal = targetNormal;
            
            float actualDistance = Vector3.Distance(_hopStartPos, _hopEndPos);
            
            if (_debugLogs)
            {
                Debug.Log($"[HoppingLoco] Hop #{_hopCount} START (nearbyTransition): " +
                    $"startPos={V(_hopStartPos)} endPos={V(_hopEndPos)} " +
                    $"startUp={V(_hopStartUp)} endUp={V(_hopEndUp)} " +
                    $"distance={actualDistance:F2}");
            }
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
                hopDir = Vector3.ProjectOnPlane(transform.forward, _hopStartUp).normalized;
                hopDistance = Mathf.Min(0.1f, maxHopDistance);
            }
            else
            {
                hopDir = planarToTarget.normalized;
                // Adjust hop distance to not overshoot
                hopDistance = Mathf.Min(planarDist, maxHopDistance);
            }

            Vector3 hopEndCandidate = _hopStartPos + hopDir * hopDistance;

            // Ground the end position using our own logic with current surface up
            _hopEndPos = GroundPositionOnSurface(hopEndCandidate, _hopStartUp);

            _hopEndUp = _hopStartUp;
            
            if (_debugLogs)
            {
                Debug.Log($"[HoppingLoco] Hop #{_hopCount} START (sameSurface): " +
                    $"startPos={V(_hopStartPos)} endPos={V(_hopEndPos)} " +
                    $"surfaceUp={V(_hopStartUp)} distance={hopDistance:F2} planarDist={planarDist:F2}");
            }
        }

        /// <summary>
        /// Ground a position onto a surface with the given up direction.
        /// This is our own grounding logic that doesn't rely on surfaceProvider.
        /// </summary>
        private Vector3 GroundPositionOnSurface(Vector3 position, Vector3 surfaceUp)
        {
            LayerMask surfaceLayers = GetSurfaceLayers();
            
            // Cast from above the position, along -surfaceUp
            Vector3 rayOrigin = position + surfaceUp * 1.0f;
            
            if (Physics.Raycast(rayOrigin, -surfaceUp, out RaycastHit hit, 3f, 
                surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                Vector3 groundedPos = hit.point + hit.normal * 0.05f;
                
                if (_debugLogs)
                    Debug.Log($"[HoppingLoco] GroundPosition: {V(position)} -> {V(groundedPos)} (hit {hit.collider.name})");
                
                return groundedPos;
            }
            
            // Fallback: try world down
            rayOrigin = position + Vector3.up * 1.0f;
            if (Physics.Raycast(rayOrigin, Vector3.down, out hit, 3f, 
                surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                Vector3 groundedPos = hit.point + hit.normal * 0.05f;
                
                if (_debugLogs)
                    Debug.Log($"[HoppingLoco] GroundPosition (worldDown fallback): {V(position)} -> {V(groundedPos)}");
                
                return groundedPos;
            }
            
            if (_debugLogs)
                Debug.LogWarning($"[HoppingLoco] GroundPosition FAILED for {V(position)} with up={V(surfaceUp)}");
            
            return position;
        }

        private LayerMask GetSurfaceLayers()
        {
            return Physics.DefaultRaycastLayers;
        }

        public override void Rotate(Vector3 direction, float deltaTime)
        {
            if (direction.sqrMagnitude < 0.0001f)
                return;

            Vector3 up = _currentSurfaceUp;

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
                Vector3 posBeforeGrounding = pos;
                bool groundingSucceeded = false;
                string groundingMethod = "none";
                
                // Ground using the target surface up direction
                groundingSucceeded = TryGroundHop(ref pos, _hopEndUp, out groundingMethod);
                
                // Update our tracked surface normal
                _currentSurfaceUp = _hopEndUp;
                
                // === DIAGNOSTIC: Measure distance from surface after landing ===
                _lastLandingDistance = MeasureDistanceFromSurface(pos, _currentSurfaceUp);
                
                if (_debugLogs)
                {
                    string hopType = _isTransitionHop ? "TRANSITION" : "NORMAL";
                    Debug.Log($"[HoppingLoco] Hop #{_hopCount} LANDING ({hopType}): " +
                        $"posBeforeGround={V(posBeforeGrounding)} " +
                        $"finalPos={V(pos)} " +
                        $"groundingMethod={groundingMethod} " +
                        $"succeeded={groundingSucceeded} " +
                        $"newSurfaceUp={V(_currentSurfaceUp)} " +
                        $"distanceFromSurface={_lastLandingDistance:F3}");
                    
                    // Warn if distance is too large
                    if (_lastLandingDistance > 0.15f)
                    {
                        Debug.LogWarning($"[HoppingLoco] Hop #{_hopCount} WARNING: " +
                            $"Landing distance ({_lastLandingDistance:F3}) exceeds threshold!");
                    }
                }

                // Final rotation alignment
                AlignToSurface(_currentSurfaceUp);

                _isHopping = false;
                _isTransitionHop = false;
            }

            transform.position = pos;
        }
        
        /// <summary>
        /// Align transform rotation to the given surface normal
        /// </summary>
        private void AlignToSurface(Vector3 surfaceUp)
        {
            Vector3 finalForward = Vector3.ProjectOnPlane(transform.forward, surfaceUp);
            if (finalForward.sqrMagnitude < 0.001f)
            {
                finalForward = Vector3.ProjectOnPlane(_hopEndPos - _hopStartPos, surfaceUp);
            }
            if (finalForward.sqrMagnitude < 0.001f)
            {
                finalForward = Vector3.ProjectOnPlane(Vector3.forward, surfaceUp);
            }
            if (finalForward.sqrMagnitude > 0.001f)
            {
                finalForward.Normalize();
                transform.rotation = Quaternion.LookRotation(finalForward, surfaceUp);
            }
        }
        
        /// <summary>
        /// Try to ground after a hop using the given up direction
        /// </summary>
        private bool TryGroundHop(ref Vector3 pos, Vector3 surfaceUp, out string method)
        {
            LayerMask surfaceLayers = GetSurfaceLayers();
            
            if (_debugLogs)
                Debug.Log($"[HoppingLoco] TryGroundHop: pos={V(pos)} surfaceUp={V(surfaceUp)}");
            
            // Method 1: Raycast from above along target up direction
            Vector3 rayOrigin1 = pos + surfaceUp * 0.5f;
            if (Physics.Raycast(rayOrigin1, -surfaceUp, out RaycastHit hit1, 2f, 
                surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                pos = hit1.point + hit1.normal * 0.05f;
                // Update surfaceUp to match actual hit normal (in case surface is tilted)
                _hopEndUp = hit1.normal.normalized;
                method = "raycast-surfaceUp";
                
                if (_debugLogs)
                {
                    Debug.Log($"[HoppingLoco] Ground Method 1: hit={hit1.collider.name} " +
                        $"point={V(hit1.point)} normal={V(hit1.normal)} dist={hit1.distance:F3}");
                }
                return true;
            }
            else if (_debugLogs)
            {
                Debug.Log($"[HoppingLoco] Ground Method 1 MISS: origin={V(rayOrigin1)} dir={V(-surfaceUp)}");
            }
            
            // Method 2: Raycast straight down (world space)
            Vector3 rayOrigin2 = pos + Vector3.up * 0.5f;
            if (Physics.Raycast(rayOrigin2, Vector3.down, out RaycastHit hit2, 2f, 
                surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                pos = hit2.point + hit2.normal * 0.05f;
                _hopEndUp = hit2.normal.normalized;
                method = "raycast-worldDown";
                
                if (_debugLogs)
                {
                    Debug.Log($"[HoppingLoco] Ground Method 2: hit={hit2.collider.name} " +
                        $"point={V(hit2.point)} normal={V(hit2.normal)} dist={hit2.distance:F3}");
                }
                return true;
            }
            else if (_debugLogs)
            {
                Debug.Log($"[HoppingLoco] Ground Method 2 MISS: origin={V(rayOrigin2)} dir=down");
            }
            
            // Method 3: SphereCast for better tolerance
            if (Physics.SphereCast(rayOrigin1, 0.3f, -surfaceUp, out RaycastHit hit3, 2f, 
                surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                pos = hit3.point + hit3.normal * 0.1f;
                _hopEndUp = hit3.normal.normalized;
                method = "spherecast";
                
                if (_debugLogs)
                {
                    Debug.Log($"[HoppingLoco] Ground Method 3 (sphere): hit={hit3.collider.name} " +
                        $"point={V(hit3.point)} normal={V(hit3.normal)} dist={hit3.distance:F3}");
                }
                return true;
            }
            else if (_debugLogs)
            {
                Debug.Log($"[HoppingLoco] Ground Method 3 MISS (spherecast)");
            }
            
            // Method 4: Try using the start up direction as fallback
            Vector3 rayOrigin4 = pos + _hopStartUp * 0.5f;
            if (Physics.Raycast(rayOrigin4, -_hopStartUp, out RaycastHit hit4, 2f, 
                surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                pos = hit4.point + hit4.normal * 0.05f;
                _hopEndUp = hit4.normal.normalized;
                method = "raycast-startUp-fallback";
                
                if (_debugLogs)
                {
                    Debug.Log($"[HoppingLoco] Ground Method 4 (startUp fallback): hit={hit4.collider.name} " +
                        $"point={V(hit4.point)} normal={V(hit4.normal)} dist={hit4.distance:F3}");
                }
                return true;
            }
            else if (_debugLogs)
            {
                Debug.Log($"[HoppingLoco] Ground Method 4 MISS: origin={V(rayOrigin4)} dir={V(-_hopStartUp)}");
            }
            
            method = "FAILED";
            
            if (_debugLogs)
            {
                Debug.LogWarning($"[HoppingLoco] All grounding methods FAILED! " +
                    $"pos={V(pos)} surfaceUp={V(surfaceUp)} startUp={V(_hopStartUp)}");
            }
            
            return false;
        }
        
        /// <summary>
        /// Measure actual distance from the nearest surface (for diagnostics)
        /// </summary>
        private float MeasureDistanceFromSurface(Vector3 pos, Vector3 surfaceUp)
        {
            LayerMask surfaceLayers = GetSurfaceLayers();
            
            // Cast down from current position using the surface up
            Vector3 rayOrigin = pos + surfaceUp * 0.5f;
            if (Physics.Raycast(rayOrigin, -surfaceUp, out RaycastHit hit, 2f, 
                surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                // Distance is from hit point to current position, along the surface normal
                Vector3 toPos = pos - hit.point;
                float dist = Vector3.Dot(toPos, hit.normal);
                return Mathf.Abs(dist);
            }
            
            // Try world down as fallback
            rayOrigin = pos + Vector3.up * 0.5f;
            if (Physics.Raycast(rayOrigin, Vector3.down, out hit, 2f, 
                surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                return Mathf.Abs(pos.y - hit.point.y);
            }
            
            // Can't measure - return large value to indicate problem
            return float.MaxValue;
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
        
        // Helper for vector formatting
        private string V(Vector3 v) => $"({v.x:F2},{v.y:F2},{v.z:F2})";
    }
}