// FILEPATH: Assets/Scripts/AI/Movement/Locomotion/HoppingLocomotion.cs
using UnityEngine;

namespace JellyGame.GamePlay.Enemy.AI.Movement
{
    /// <summary>
    /// Discrete hopping movement locomotion with surface transition support.
    /// The agent moves in distinct hops with an arc trajectory.
    /// Suitable for slimes, frogs, or other bouncy creatures.
    /// 
    /// v11 Changes:
    /// - FIXED: Now raycasts toward ACTUAL waypoint position to find landing surface
    /// - FIXED: Better surface finding with larger search radius
    /// - FIXED: Won't overshoot waypoint - hop distance clamped to actual distance
    /// - Added waypoint surface pre-check at hop start
    /// - Better handling of waypoints on different height surfaces
    /// 
    /// v12 Changes:
    /// - FIXED: Wrong-side landing detection using world-space expectations
    /// - For floors: validates spider is ABOVE surface when normal points up
    /// - For ceilings: validates spider is BELOW surface when normal points down
    /// - Compares new normal against previous normal - rejects sudden flips
    /// - Surface side validation in TryFindSurfaceAtPosition rejects bad normals
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
        private Vector3 _hopStartUp;
        private Vector3 _hopEndUp;

        // Surface-relative landing position
        private Transform _targetSurface;
        private Vector3 _hopEndPosLocal;
        private Vector3 _hopEndPosWorld;
        
        // Magnet effect settings
        private const float MAGNET_START_T = 0.5f;
        private const float MAGNET_STRENGTH = 5f;

        private bool _isTransitionHop;
        private Vector3 _transitionTargetNormal;

        // Destination tracking
        private Vector3? _currentDestination;
        
        // Track our own surface normal
        private Vector3 _currentSurfaceUp = Vector3.up;
        
        // Landing diagnostics
        private int _hopCount = 0;
        private float _lastLandingDistance = 0f;
        
        // Transition settings
        private const float MAX_TRANSITION_DISTANCE = 6.0f;
        private const float EDGE_DETECTION_DISTANCE = 3.0f;
        
        // v11: Surface finding settings
        private const float SURFACE_SEARCH_RADIUS = 5.0f;  // Increased from 3
        private const float SURFACE_SEARCH_OFFSET = 2.0f;  // Increased from 1
        
        public float LastLandingDistance => _lastLandingDistance;
        public Vector3 CurrentSurfaceUp => _currentSurfaceUp;
        public override bool IsInMotion => _isHopping;
        public bool IsTransitionHop => _isTransitionHop;
        public Vector3 TransitionTargetNormal => _transitionTargetNormal;

        public HoppingLocomotion(Transform transform, LocomotionSettings settings, ISurfaceProvider surfaceProvider = null)
            : base(transform, settings, surfaceProvider)
        {
            _nextHopTime = 0f;
            _currentSurfaceUp = transform.up;
        }

        public void SetDestination(Vector3? destination)
        {
            _currentDestination = destination;
        }

        public override void Move(Vector3 direction, float deltaTime, float speedMultiplier = 1f)
        {
            if (direction.sqrMagnitude < 0.0001f)
                return;

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
            // v11 FIX: If stopped mid-hop, ground the spider first!
            if (_isHopping)
            {
                Vector3 pos = transform.position;
                if (TryGroundHop(ref pos, _currentSurfaceUp, out string method))
                {
                    transform.position = pos;
                    AlignToSurface(_currentSurfaceUp);
                    
                    if (_debugLogs)
                        Debug.Log($"[HoppingLoco] Stop() called mid-hop - emergency grounding via {method}");
                }
            }
            
            _isHopping = false;
            _hopProgress = 0f;
            _isTransitionHop = false;
            _targetSurface = null;
        }

        public override void OnDisable()
        {
            Stop();
        }

        private void ClearHopState()
        {
            _isHopping = false;
            _isTransitionHop = false;
            _targetSurface = null;
            _hopProgress = 0f;
        }

        private void StartBasicHop(Vector3 direction, float speedMultiplier)
        {
            _nextHopTime = Time.time + Mathf.Max(0.05f, settings.HopInterval);
            _isHopping = true;
            _hopProgress = 0f;
            _isTransitionHop = false;
            _hopCount++;
            _targetSurface = null;

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

            if (TryFindLandingSurfaceEnhanced(hopEndCandidate, _hopStartUp, out RaycastHit hit, out Transform surface))
            {
                _targetSurface = surface;
                _hopEndPosWorld = hit.point + hit.normal * 0.05f;
                _hopEndPosLocal = _targetSurface.InverseTransformPoint(_hopEndPosWorld);
                _hopEndUp = hit.normal.normalized;
            }
            else
            {
                _targetSurface = null;
                _hopEndPosWorld = hopEndCandidate;
                _hopEndPosLocal = Vector3.zero;
            }
            
            if (_debugLogs)
            {
                Debug.Log($"[HoppingLoco] Hop #{_hopCount} START (basic): " +
                    $"startPos={V(_hopStartPos)} endPos={V(GetCurrentHopEndPos())} " +
                    $"distance={hopDistance:F2} up={V(_hopStartUp)} targetSurface={(_targetSurface != null ? _targetSurface.name : "none")}");
            }
        }

        private void StartHopTowardDestination(Vector3 destination, float speedMultiplier)
        {
            _nextHopTime = Time.time + Mathf.Max(0.05f, settings.HopInterval);
            _isHopping = true;
            _hopProgress = 0f;
            _isTransitionHop = false;
            _hopCount++;
            _targetSurface = null;

            _hopStartPos = transform.position;
            _hopStartUp = _currentSurfaceUp;
            _hopEndUp = _hopStartUp;

            float maxHopDistance = GetHopDistance(speedMultiplier);

            // v11: First, find what surface the DESTINATION is actually on
            Vector3 actualDestinationOnSurface;
            Vector3 destinationSurfaceNormal;
            Transform destinationSurface;
            
            if (TryFindSurfaceAtPosition(destination, out RaycastHit destHit, out destinationSurface))
            {
                actualDestinationOnSurface = destHit.point + destHit.normal * 0.05f;
                destinationSurfaceNormal = destHit.normal.normalized;
                
                if (_debugLogs)
                    Debug.Log($"[HoppingLoco] Destination surface found: {destinationSurface.name} at {V(actualDestinationOnSurface)} normal={V(destinationSurfaceNormal)}");
            }
            else
            {
                // Fallback to raw destination
                actualDestinationOnSurface = destination;
                destinationSurfaceNormal = _hopStartUp;
                destinationSurface = null;
                
                if (_debugLogs)
                    Debug.LogWarning($"[HoppingLoco] Could not find surface at destination {V(destination)}");
            }

            // Calculate actual distance to destination (on surface)
            float actualDistToDestination = Vector3.Distance(_hopStartPos, actualDestinationOnSurface);
            
            // Check if destination is on a different surface orientation (transition needed)
            float angleBetweenSurfaces = Vector3.Angle(_hopStartUp, destinationSurfaceNormal);
            bool needsTransition = angleBetweenSurfaces > 30f;

            if (needsTransition)
            {
                if (_debugLogs)
                    Debug.Log($"[HoppingLoco] Hop #{_hopCount}: Surface transition needed (angle={angleBetweenSurfaces:F1}°)");
                
                // Check for nearby transition point
                Vector3 toDestination = actualDestinationOnSurface - _hopStartPos;
                Vector3 planarMoveDir = GetPlanarMoveDirection(actualDestinationOnSurface);
                
                if (TryDetectNearbyTransition(actualDestinationOnSurface, planarMoveDir, toDestination.normalized, maxHopDistance,
                    out Vector3 transitionNormal, out Vector3 transitionPoint, out Transform transitionSurface))
                {
                    StartNearbyTransitionHop(transitionNormal, transitionPoint, transitionSurface, maxHopDistance);
                    return;
                }
            }

            // Same surface hop - go toward the actual destination on surface
            StartSameSurfaceHopToward(actualDestinationOnSurface, destinationSurfaceNormal, destinationSurface, maxHopDistance);
        }

        /// <summary>
        /// v11: Improved same-surface hop that targets the actual destination position
        /// </summary>
        private void StartSameSurfaceHopToward(Vector3 destinationOnSurface, Vector3 destNormal, Transform destSurface, float maxHopDistance)
        {
            Vector3 toDestination = destinationOnSurface - _hopStartPos;
            float distToDestination = toDestination.magnitude;
            
            // Clamp hop distance to not overshoot the destination
            float hopDistance = Mathf.Min(distToDestination, maxHopDistance);
            
            // Direction toward destination (3D, not just planar)
            Vector3 hopDir = distToDestination > 0.01f ? toDestination.normalized : transform.forward;
            
            // Calculate hop end candidate
            Vector3 hopEndCandidate;
            if (hopDistance >= distToDestination * 0.9f)
            {
                // Close enough to land at destination
                hopEndCandidate = destinationOnSurface;
                _targetSurface = destSurface;
                _hopEndPosWorld = destinationOnSurface;
                if (_targetSurface != null)
                    _hopEndPosLocal = _targetSurface.InverseTransformPoint(_hopEndPosWorld);
                else
                    _hopEndPosLocal = Vector3.zero;
                _hopEndUp = destNormal;
            }
            else
            {
                // Partial hop - find surface at intermediate point
                hopEndCandidate = _hopStartPos + hopDir * hopDistance;
                
                // Try to find surface at the hop end candidate
                if (TryFindLandingSurfaceEnhanced(hopEndCandidate, _hopStartUp, out RaycastHit hit, out Transform surface))
                {
                    _targetSurface = surface;
                    _hopEndPosWorld = hit.point + hit.normal * 0.05f;
                    _hopEndPosLocal = _targetSurface.InverseTransformPoint(_hopEndPosWorld);
                    _hopEndUp = hit.normal.normalized;
                }
                else
                {
                    // Fallback: use destination surface info
                    _targetSurface = destSurface;
                    _hopEndPosWorld = hopEndCandidate;
                    if (_targetSurface != null)
                        _hopEndPosLocal = _targetSurface.InverseTransformPoint(_hopEndPosWorld);
                    else
                        _hopEndPosLocal = Vector3.zero;
                    _hopEndUp = destNormal;
                }
            }
            
            if (_debugLogs)
            {
                Debug.Log($"[HoppingLoco] Hop #{_hopCount} START (sameSurface): " +
                    $"startPos={V(_hopStartPos)} endPos={V(GetCurrentHopEndPos())} " +
                    $"surfaceUp={V(_hopStartUp)} targetUp={V(_hopEndUp)} " +
                    $"hopDist={hopDistance:F2} actualDist={distToDestination:F2}");
            }
        }

        /// <summary>
        /// v11: Enhanced surface finding - searches in multiple directions with larger radius
        /// v12: Added surface side validation - rejects normals pointing away from waypoint
        /// </summary>
        private bool TryFindSurfaceAtPosition(Vector3 position, out RaycastHit bestHit, out Transform surface)
        {
            bestHit = default;
            surface = null;
            LayerMask surfaceLayers = GetSurfaceLayers();
            
            // Try multiple ray directions to find the surface
            Vector3[] rayDirections = new Vector3[]
            {
                Vector3.down,           // World down (most common)
                -_hopStartUp,           // Current surface down
                Vector3.up,             // In case position is below surface
                _hopStartUp,            // Current surface up
                Vector3.left,
                Vector3.right,
                Vector3.forward,
                Vector3.back
            };
            
            float bestDist = float.MaxValue;
            bool found = false;
            
            foreach (var dir in rayDirections)
            {
                Vector3 origin = position - dir * SURFACE_SEARCH_OFFSET;
                
                if (Physics.Raycast(origin, dir, out RaycastHit hit, SURFACE_SEARCH_RADIUS, 
                    surfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    // v12 FIX: Validate surface side!
                    // The normal should point TOWARD the waypoint position, not away from it.
                    // If waypoint is at Y=0.1 and we hit floor from below (normal 0,-1,0),
                    // the dot product of (waypoint - hitPoint) and normal would be negative = wrong side!
                    Vector3 fromHitToWaypoint = position - hit.point;
                    float dotWithNormal = Vector3.Dot(fromHitToWaypoint.normalized, hit.normal);
                    
                    if (dotWithNormal < -0.1f)
                    {
                        // Normal points away from waypoint = wrong side of surface
                        if (_debugLogs)
                            Debug.Log($"[HoppingLoco] Rejected surface hit: normal {V(hit.normal)} points away from waypoint (dot={dotWithNormal:F2})");
                        continue;
                    }
                    
                    float dist = Vector3.Distance(position, hit.point);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestHit = hit;
                        surface = hit.collider.transform;
                        found = true;
                    }
                }
            }
            
            // Also try sphere cast for better coverage
            if (!found)
            {
                if (Physics.SphereCast(position + Vector3.up * 2f, 0.5f, Vector3.down, out RaycastHit sphereHit, 
                    SURFACE_SEARCH_RADIUS, surfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    // Also validate sphere cast result
                    Vector3 fromHitToWaypoint = position - sphereHit.point;
                    float dotWithNormal = Vector3.Dot(fromHitToWaypoint.normalized, sphereHit.normal);
                    
                    if (dotWithNormal >= -0.1f)
                    {
                        bestHit = sphereHit;
                        surface = sphereHit.collider.transform;
                        found = true;
                    }
                }
            }
            
            return found;
        }

        /// <summary>
        /// v11: Enhanced landing surface finder
        /// </summary>
        private bool TryFindLandingSurfaceEnhanced(Vector3 position, Vector3 surfaceUp, out RaycastHit hit, out Transform surface)
        {
            surface = null;
            LayerMask surfaceLayers = GetSurfaceLayers();
            
            // Method 1: Raycast along current surface down
            Vector3 rayOrigin = position + surfaceUp * SURFACE_SEARCH_OFFSET;
            if (Physics.Raycast(rayOrigin, -surfaceUp, out hit, SURFACE_SEARCH_RADIUS, 
                surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                surface = hit.collider.transform;
                return true;
            }
            
            // Method 2: World down
            rayOrigin = position + Vector3.up * SURFACE_SEARCH_OFFSET;
            if (Physics.Raycast(rayOrigin, Vector3.down, out hit, SURFACE_SEARCH_RADIUS, 
                surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                surface = hit.collider.transform;
                return true;
            }
            
            // Method 3: Sphere cast for better coverage
            if (Physics.SphereCast(position + surfaceUp * 1f, 0.3f, -surfaceUp, out hit, SURFACE_SEARCH_RADIUS,
                surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                surface = hit.collider.transform;
                return true;
            }
            
            // Method 4: Try from the actual position (in case we're slightly inside geometry)
            if (Physics.Raycast(position, -surfaceUp, out hit, SURFACE_SEARCH_RADIUS * 0.5f,
                surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                surface = hit.collider.transform;
                return true;
            }
            
            hit = default;
            return false;
        }

        private Vector3 GetCurrentHopEndPos()
        {
            if (_targetSurface != null)
            {
                return _targetSurface.TransformPoint(_hopEndPosLocal);
            }
            return _hopEndPosWorld;
        }

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

        private bool TryDetectNearbyTransition(Vector3 destination, Vector3 planarMoveDir, Vector3 rawDirToDestination, float hopDistance, 
            out Vector3 transitionNormal, out Vector3 transitionPoint, out Transform transitionSurface)
        {
            transitionNormal = Vector3.zero;
            transitionPoint = Vector3.zero;
            transitionSurface = null;
            
            LayerMask surfaceLayers = GetSurfaceLayers();
            float checkDistance = Mathf.Max(hopDistance, EDGE_DETECTION_DISTANCE);
            
            // Check 1: Surface in RAW direction to destination
            Vector3 rawCheckOrigin = _hopStartPos + _hopStartUp * 0.2f;
            
            if (Physics.Raycast(rawCheckOrigin, rawDirToDestination, out RaycastHit rawHit, checkDistance, 
                surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                Vector3 hitNormal = rawHit.normal.normalized;
                float angleToSurface = Vector3.Angle(_hopStartUp, hitNormal);
                
                if (angleToSurface > 30f && rawHit.distance < MAX_TRANSITION_DISTANCE)
                {
                    transitionNormal = hitNormal;
                    transitionPoint = rawHit.point + hitNormal * 0.05f;
                    transitionSurface = rawHit.collider.transform;
                    return true;
                }
            }
            
            // Check 2: Wall directly ahead
            Vector3 wallCheckOrigin = _hopStartPos + _hopStartUp * 0.2f;
            
            if (Physics.Raycast(wallCheckOrigin, planarMoveDir, out RaycastHit wallHit, checkDistance, 
                surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                Vector3 wallNormal = wallHit.normal.normalized;
                float angleToWall = Vector3.Angle(_hopStartUp, wallNormal);
                
                if (angleToWall > 30f && wallHit.distance < MAX_TRANSITION_DISTANCE)
                {
                    transitionNormal = wallNormal;
                    transitionPoint = wallHit.point + wallNormal * 0.05f;
                    transitionSurface = wallHit.collider.transform;
                    return true;
                }
            }
            
            // Check 3: Edge/dropoff ahead
            Vector3 aheadPos = _hopStartPos + planarMoveDir * checkDistance;
            Vector3 edgeCheckOrigin = aheadPos + _hopStartUp * 0.5f;
            
            if (!Physics.Raycast(edgeCheckOrigin, -_hopStartUp, out RaycastHit groundAhead, 1.5f, 
                surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                if (TryFindSurfaceAtEdge(aheadPos, planarMoveDir, out Vector3 edgeSurfaceNormal, out Vector3 edgeSurfacePoint, out Transform edgeSurface))
                {
                    float angleToEdgeSurface = Vector3.Angle(_hopStartUp, edgeSurfaceNormal);
                    
                    if (angleToEdgeSurface > 30f)
                    {
                        transitionNormal = edgeSurfaceNormal;
                        transitionPoint = edgeSurfacePoint;
                        transitionSurface = edgeSurface;
                        return true;
                    }
                }
            }
            
            // Check 4: WORLD DOWN for floor when on wall/ceiling
            float currentSurfaceVerticalDot = Mathf.Abs(Vector3.Dot(_hopStartUp, Vector3.up));
            bool isOnWallOrCeiling = currentSurfaceVerticalDot < 0.7f;
            
            if (isOnWallOrCeiling)
            {
                Vector3 worldDownOrigin = _hopStartPos + Vector3.up * 0.2f;
                
                if (Physics.Raycast(worldDownOrigin, Vector3.down, out RaycastHit floorHit, MAX_TRANSITION_DISTANCE, 
                    surfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    Vector3 floorNormal = floorHit.normal.normalized;
                    float angleToFloor = Vector3.Angle(_hopStartUp, floorNormal);
                    
                    bool destIsLower = destination.y < _hopStartPos.y - 0.5f;
                    bool floorIsClose = floorHit.distance < 3.0f;
                    
                    if (angleToFloor > 30f && (floorIsClose || destIsLower))
                    {
                        transitionNormal = floorNormal;
                        transitionPoint = floorHit.point + floorNormal * 0.05f;
                        transitionSurface = floorHit.collider.transform;
                        return true;
                    }
                }
            }
            
            return false;
        }

        private bool TryFindSurfaceAtEdge(Vector3 edgePos, Vector3 moveDir, out Vector3 surfaceNormal, out Vector3 surfacePoint, out Transform surface)
        {
            surfaceNormal = Vector3.zero;
            surfacePoint = Vector3.zero;
            surface = null;
            
            LayerMask surfaceLayers = GetSurfaceLayers();
            
            Vector3[] searchDirs = new Vector3[]
            {
                -_hopStartUp,
                (moveDir - _hopStartUp).normalized,
                moveDir,
                -moveDir,
                Vector3.down,
            };

            foreach (var dir in searchDirs)
            {
                if (dir.sqrMagnitude < 0.001f) continue;
                
                Vector3 origin = edgePos + _hopStartUp * 0.2f;
                
                if (Physics.Raycast(origin, dir, out RaycastHit hit, 3f, surfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    float angle = Vector3.Angle(_hopStartUp, hit.normal);
                    
                    if (angle > 30f)
                    {
                        surfaceNormal = hit.normal.normalized;
                        surfacePoint = hit.point + hit.normal * 0.05f;
                        surface = hit.collider.transform;
                        return true;
                    }
                }
            }
            
            return false;
        }

        private void StartNearbyTransitionHop(Vector3 targetNormal, Vector3 targetPoint, Transform targetSurface, float maxHopDistance)
        {
            Vector3 toTarget = targetPoint - _hopStartPos;
            float distanceToTarget = toTarget.magnitude;
            
            Vector3 landingPoint;
            
            if (distanceToTarget <= maxHopDistance)
            {
                landingPoint = targetPoint;
            }
            else
            {
                landingPoint = _hopStartPos + toTarget.normalized * maxHopDistance;
                if (TryFindLandingSurfaceEnhanced(landingPoint, targetNormal, out RaycastHit groundHit, out Transform groundSurface))
                {
                    landingPoint = groundHit.point + groundHit.normal * 0.05f;
                    targetNormal = groundHit.normal.normalized;
                    targetSurface = groundSurface;
                }
            }

            _targetSurface = targetSurface;
            _hopEndPosWorld = landingPoint;
            if (_targetSurface != null)
            {
                _hopEndPosLocal = _targetSurface.InverseTransformPoint(landingPoint);
            }
            else
            {
                _hopEndPosLocal = Vector3.zero;
            }
            
            _hopEndUp = targetNormal;
            _isTransitionHop = true;
            _transitionTargetNormal = targetNormal;
            
            if (_debugLogs)
            {
                float actualDistance = Vector3.Distance(_hopStartPos, landingPoint);
                Debug.Log($"[HoppingLoco] Hop #{_hopCount} START (nearbyTransition): " +
                    $"startPos={V(_hopStartPos)} endPos={V(landingPoint)} " +
                    $"startUp={V(_hopStartUp)} endUp={V(_hopEndUp)} " +
                    $"distance={actualDistance:F2} targetSurface={(_targetSurface != null ? _targetSurface.name : "none")}");
            }
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

            Vector3 currentEndPos = GetCurrentHopEndPos();
            Vector3 pos = Vector3.Lerp(_hopStartPos, currentEndPos, t);

            float arc = Mathf.Sin(t * Mathf.PI) * settings.HopHeight;
            
            Vector3 arcUp;
            if (_isTransitionHop)
            {
                arcUp = Vector3.Slerp(_hopStartUp, _hopEndUp, t).normalized;
                
                Vector3 currentForward = Vector3.ProjectOnPlane(transform.forward, arcUp);
                if (currentForward.sqrMagnitude < 0.001f)
                {
                    currentForward = Vector3.ProjectOnPlane(currentEndPos - _hopStartPos, arcUp);
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

            if (t >= MAGNET_START_T && t < 1f)
            {
                pos = ApplyMagnetEffect(pos, currentEndPos, t, deltaTime);
            }

            if (t >= 1f)
            {
                Vector3 posBeforeGrounding = pos;
                bool groundingSucceeded = false;
                string groundingMethod = "none";
                
                groundingSucceeded = TryGroundHop(ref pos, _hopEndUp, out groundingMethod);
                
                if (groundingSucceeded)
                {
                    pos = ValidateAndCorrectSurfaceSide(pos, _hopEndUp);
                }
                
                _currentSurfaceUp = _hopEndUp;
                _lastLandingDistance = MeasureDistanceFromSurface(pos, _currentSurfaceUp);
                
                if (_debugLogs)
                {
                    string hopType = _isTransitionHop ? "TRANSITION" : "NORMAL";
                    Debug.Log($"[HoppingLoco] Hop #{_hopCount} LANDING ({hopType}): " +
                        $"posBeforeGround={V(posBeforeGrounding)} finalPos={V(pos)} " +
                        $"groundingMethod={groundingMethod} succeeded={groundingSucceeded} " +
                        $"newSurfaceUp={V(_currentSurfaceUp)} distanceFromSurface={_lastLandingDistance:F3}");
                    
                    if (_lastLandingDistance > 0.15f)
                    {
                        Debug.LogWarning($"[HoppingLoco] Hop #{_hopCount} WARNING: Landing distance ({_lastLandingDistance:F3}) exceeds threshold!");
                    }
                }

                AlignToSurface(_currentSurfaceUp);
                ClearHopState();
            }

            transform.position = pos;
        }

        private Vector3 ApplyMagnetEffect(Vector3 currentPos, Vector3 intendedEndPos, float t, float deltaTime)
        {
            float descentProgress = (t - MAGNET_START_T) / (1f - MAGNET_START_T);
            descentProgress = Mathf.Clamp01(descentProgress);
            
            float pullStrength = MAGNET_STRENGTH * descentProgress * descentProgress;
            
            Vector3 toTarget = intendedEndPos - currentPos;
            float distanceToTarget = toTarget.magnitude;
            
            if (distanceToTarget > 0.05f)
            {
                float pullAmount = pullStrength * deltaTime;
                pullAmount = Mathf.Min(pullAmount, distanceToTarget * 0.5f);
                currentPos = Vector3.MoveTowards(currentPos, intendedEndPos, pullAmount);
            }
            
            return currentPos;
        }

        /// <summary>
        /// v11 Enhanced: Validate spider is on correct side of surface.
        /// Uses multiple checks including world-space expectations and previous normal comparison.
        /// </summary>
        private Vector3 ValidateAndCorrectSurfaceSide(Vector3 position, Vector3 surfaceUp)
        {
            LayerMask surfaceLayers = GetSurfaceLayers();
            
            // === CHECK 1: Is the normal physically reasonable? ===
            // For mostly-horizontal surfaces (floors/ceilings), the normal should match world expectations
            // If surface is horizontal (normal.y close to ±1), check if we're on the expected side
            
            bool isHorizontalSurface = Mathf.Abs(surfaceUp.y) > 0.7f; // Surface is floor-like or ceiling-like
            
            if (isHorizontalSurface)
            {
                // For horizontal surfaces, check if we're on the WORLD-UP side or WORLD-DOWN side
                // A floor should have normal pointing UP (y > 0)
                // If we got a downward normal for a floor, we're on the wrong side!
                
                bool normalPointsDown = surfaceUp.y < -0.7f;
                bool spiderAboveSurface = true; // Assume true, will verify below
                
                // Raycast world-down to find the actual surface
                Vector3 worldDownOrigin = position + Vector3.up * 1.0f;
                if (Physics.Raycast(worldDownOrigin, Vector3.down, out RaycastHit worldDownHit, 3f,
                    surfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    // Is the spider above the hit point?
                    spiderAboveSurface = position.y > worldDownHit.point.y - 0.1f;
                    
                    // If spider is above the surface but got a downward normal, that's WRONG
                    if (spiderAboveSurface && normalPointsDown)
                    {
                        // Use the world-down raycast result instead - it has the correct normal
                        Vector3 correctedPos = worldDownHit.point + worldDownHit.normal * 0.05f;
                        _hopEndUp = worldDownHit.normal.normalized;
                        
                        if (_debugLogs)
                            Debug.LogWarning($"[HoppingLoco] WRONG SIDE FIX: Spider above floor but got down-normal. " +
                                $"Correcting normal from {V(surfaceUp)} to {V(_hopEndUp)}, pos to {V(correctedPos)}");
                        
                        return correctedPos;
                    }
                }
                
                // Also check: if spider is BELOW a surface and got an upward normal, that's wrong too
                Vector3 worldUpOrigin = position + Vector3.down * 1.0f;
                if (Physics.Raycast(worldUpOrigin, Vector3.up, out RaycastHit worldUpHit, 3f,
                    surfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    bool spiderBelowSurface = position.y < worldUpHit.point.y + 0.1f;
                    bool normalPointsUp = surfaceUp.y > 0.7f;
                    
                    // If spider is below surface but got an upward normal, wrong!
                    if (spiderBelowSurface && normalPointsUp && !spiderAboveSurface)
                    {
                        Vector3 correctedPos = worldUpHit.point + worldUpHit.normal * 0.05f;
                        _hopEndUp = worldUpHit.normal.normalized;
                        
                        if (_debugLogs)
                            Debug.LogWarning($"[HoppingLoco] WRONG SIDE FIX: Spider below ceiling but got up-normal. " +
                                $"Correcting normal from {V(surfaceUp)} to {V(_hopEndUp)}, pos to {V(correctedPos)}");
                        
                        return correctedPos;
                    }
                }
            }
            
            // === CHECK 2: Compare with previous surface normal ===
            // If the new normal is roughly OPPOSITE to where we came from, that's suspicious
            // (Unless it's a legitimate transition like floor-to-ceiling)
            
            float angleFromStart = Vector3.Angle(_hopStartUp, surfaceUp);
            bool isOppositeToStart = angleFromStart > 150f; // Nearly opposite
            bool wasTransitionHop = _isTransitionHop;
            
            if (isOppositeToStart && !wasTransitionHop)
            {
                // We weren't doing a transition but ended up with opposite normal - suspicious!
                // Try to find the correct surface by raycasting from start direction
                
                Vector3 fromStartOrigin = position + _hopStartUp * 0.5f;
                if (Physics.Raycast(fromStartOrigin, -_hopStartUp, out RaycastHit startDirHit, 2f,
                    surfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    float angleWithStartNormal = Vector3.Angle(_hopStartUp, startDirHit.normal);
                    
                    // If this hit has a normal similar to where we came from, use it
                    if (angleWithStartNormal < 45f)
                    {
                        Vector3 correctedPos = startDirHit.point + startDirHit.normal * 0.05f;
                        _hopEndUp = startDirHit.normal.normalized;
                        
                        if (_debugLogs)
                            Debug.LogWarning($"[HoppingLoco] WRONG SIDE FIX: Got opposite normal without transition. " +
                                $"Correcting from {V(surfaceUp)} to {V(_hopEndUp)}");
                        
                        return correctedPos;
                    }
                }
            }
            
            // === CHECK 3: Original validation - position relative to surface ===
            Vector3 rayOrigin = position + surfaceUp * 0.5f;
            
            if (Physics.Raycast(rayOrigin, -surfaceUp, out RaycastHit hit, 2f, 
                surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                Vector3 fromHitToPos = position - hit.point;
                float dotWithNormal = Vector3.Dot(fromHitToPos, hit.normal);
                
                if (dotWithNormal < -0.01f)
                {
                    Vector3 correctedPos = hit.point + hit.normal * 0.05f;
                    _hopEndUp = hit.normal.normalized;
                    if (_debugLogs)
                        Debug.LogWarning($"[HoppingLoco] WRONG SIDE detected (dot check)! Correcting to {V(correctedPos)}");
                    return correctedPos;
                }
                else if (dotWithNormal > 0.3f)
                {
                    Vector3 correctedPos = hit.point + hit.normal * 0.05f;
                    return correctedPos;
                }
            }
            else
            {
                rayOrigin = position - surfaceUp * 0.5f;
                if (Physics.Raycast(rayOrigin, surfaceUp, out hit, 2f, 
                    surfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    Vector3 correctedPos = hit.point + hit.normal * 0.05f;
                    _hopEndUp = hit.normal.normalized;
                    if (_debugLogs)
                        Debug.LogWarning($"[HoppingLoco] Hit surface from WRONG SIDE - correcting to {V(correctedPos)}");
                    return correctedPos;
                }
            }
            
            return position;
        }

        private void AlignToSurface(Vector3 surfaceUp)
        {
            Vector3 finalForward = Vector3.ProjectOnPlane(transform.forward, surfaceUp);
            if (finalForward.sqrMagnitude < 0.001f)
            {
                finalForward = Vector3.ProjectOnPlane(GetCurrentHopEndPos() - _hopStartPos, surfaceUp);
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
        
        private bool TryGroundHop(ref Vector3 pos, Vector3 surfaceUp, out string method)
        {
            LayerMask surfaceLayers = GetSurfaceLayers();
            Vector3 intendedLanding = GetCurrentHopEndPos(); // Use this for side validation
            
            // Helper to validate surface side
            bool IsValidSurfaceSide(RaycastHit hit)
            {
                // The normal should point toward the intended landing position
                // If we're trying to land at Y=0.1 and hit returns normal (0,-1,0),
                // that means we'd end up below the surface = wrong!
                Vector3 fromHitToIntended = intendedLanding - hit.point;
                float dot = Vector3.Dot(fromHitToIntended.normalized, hit.normal);
                
                // Allow some tolerance - dot should be >= -0.1 (not pointing strongly away)
                // A dot of 0 means perpendicular which is fine for edges
                // A dot of -1 means completely wrong side
                if (dot < -0.3f)
                {
                    if (_debugLogs)
                        Debug.Log($"[HoppingLoco] TryGroundHop rejected hit: normal {V(hit.normal)} points away from intended landing (dot={dot:F2})");
                    return false;
                }
                return true;
            }
            
            // Method 1: Raycast from above along target up direction
            Vector3 rayOrigin1 = pos + surfaceUp * 0.5f;
            if (Physics.Raycast(rayOrigin1, -surfaceUp, out RaycastHit hit1, 2f, 
                surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                if (IsValidSurfaceSide(hit1))
                {
                    pos = hit1.point + hit1.normal * 0.05f;
                    _hopEndUp = hit1.normal.normalized;
                    method = "raycast-surfaceUp";
                    return true;
                }
            }
            
            // Method 2: World down
            Vector3 rayOrigin2 = pos + Vector3.up * 0.5f;
            if (Physics.Raycast(rayOrigin2, Vector3.down, out RaycastHit hit2, 2f, 
                surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                if (IsValidSurfaceSide(hit2))
                {
                    pos = hit2.point + hit2.normal * 0.05f;
                    _hopEndUp = hit2.normal.normalized;
                    method = "raycast-worldDown";
                    return true;
                }
            }
            
            // Method 3: SphereCast
            if (Physics.SphereCast(rayOrigin1, 0.3f, -surfaceUp, out RaycastHit hit3, 2f, 
                surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                if (IsValidSurfaceSide(hit3))
                {
                    pos = hit3.point + hit3.normal * 0.1f;
                    _hopEndUp = hit3.normal.normalized;
                    method = "spherecast";
                    return true;
                }
            }
            
            // Method 4: Start up fallback
            Vector3 rayOrigin4 = pos + _hopStartUp * 0.5f;
            if (Physics.Raycast(rayOrigin4, -_hopStartUp, out RaycastHit hit4, 2f, 
                surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                if (IsValidSurfaceSide(hit4))
                {
                    pos = hit4.point + hit4.normal * 0.05f;
                    _hopEndUp = hit4.normal.normalized;
                    method = "raycast-startUp-fallback";
                    return true;
                }
            }
            
            // Method 5: Emergency all directions - WITH validation
            Vector3[] emergencyDirs = { Vector3.down, Vector3.up, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
            foreach (var dir in emergencyDirs)
            {
                Vector3 origin = pos - dir * 0.5f;
                if (Physics.Raycast(origin, dir, out RaycastHit emergencyHit, 3f, 
                    surfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    if (IsValidSurfaceSide(emergencyHit))
                    {
                        pos = emergencyHit.point + emergencyHit.normal * 0.05f;
                        _hopEndUp = emergencyHit.normal.normalized;
                        method = $"emergency-{dir}";
                        return true;
                    }
                }
            }
            
            // Method 6: LAST RESORT - accept any surface but log warning
            // This prevents getting completely stuck, but warns about potential wrong-side landing
            foreach (var dir in emergencyDirs)
            {
                Vector3 origin = pos - dir * 0.5f;
                if (Physics.Raycast(origin, dir, out RaycastHit lastResortHit, 3f, 
                    surfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    pos = lastResortHit.point + lastResortHit.normal * 0.05f;
                    _hopEndUp = lastResortHit.normal.normalized;
                    method = $"lastResort-{dir}";
                    
                    if (_debugLogs)
                        Debug.LogWarning($"[HoppingLoco] Last resort grounding used - may be wrong side! normal={V(_hopEndUp)}");
                    
                    return true;
                }
            }
            
            method = "FAILED";
            return false;
        }
        
        private float MeasureDistanceFromSurface(Vector3 pos, Vector3 surfaceUp)
        {
            LayerMask surfaceLayers = GetSurfaceLayers();
            
            Vector3 rayOrigin = pos + surfaceUp * 0.5f;
            if (Physics.Raycast(rayOrigin, -surfaceUp, out RaycastHit hit, 2f, 
                surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                Vector3 toPos = pos - hit.point;
                float dist = Vector3.Dot(toPos, hit.normal);
                return Mathf.Abs(dist);
            }
            
            rayOrigin = pos + Vector3.up * 0.5f;
            if (Physics.Raycast(rayOrigin, Vector3.down, out hit, 2f, 
                surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                return Mathf.Abs(pos.y - hit.point.y);
            }
            
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
        
        private string V(Vector3 v) => $"({v.x:F2},{v.y:F2},{v.z:F2})";
    }
}