// FILEPATH: Assets/Scripts/AI/Movement/SteeringNavigator.cs
using UnityEngine;

namespace JellyGame.GamePlay.Enemy.AI.Movement
{
    public class SteeringNavigator : MonoBehaviour, ISpeedMultiplierSink
    {
        public enum LocomotionMode
        {
            Continuous,
            Hopping
        }

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

        [Tooltip("If true, movement + steering happen on the plane orthogonal to transform.up (works for walls/ceilings).\nIf false, behaves like old code (world up / XZ).")]
        [SerializeField] private bool useLocalUpPlane = false;

        [Header("Locomotion Mode")]
        [SerializeField] private LocomotionMode locomotionMode = LocomotionMode.Continuous;

        [Tooltip("Time between hop starts (seconds).")]
        [SerializeField] private float hopInterval = 0.45f;

        [Tooltip("How long a hop takes (seconds).")]
        [SerializeField] private float hopDuration = 0.22f;

        [Tooltip("Hop height along the current up direction.")]
        [SerializeField] private float hopHeight = 0.35f;

        [Header("Surface Alignment (Spider)")]
        [Tooltip("Enable wall/floor adhesion + rotating transform.up to match surface normal.")]
        [SerializeField] private bool enableSurfaceAlignment = false;

        [Tooltip("Which layers count as walkable surfaces (floor/walls).")]
        [SerializeField] private LayerMask surfaceLayers;

        [Tooltip("Ray distance along -up to keep attached to current surface.")]
        [SerializeField] private float surfaceStickDistance = 1.5f;

        [Tooltip("Ray distance forward to detect an upcoming wall and transition to it.")]
        [SerializeField] private float forwardSurfaceProbeDistance = 1.0f;

        [Tooltip("How quickly we rotate to match a new surface normal.")]
        [SerializeField] private float surfaceAlignSpeed = 12f;

        [Header("Wall Climbing Transition")]
        [Tooltip("Distance at which spider starts approaching the surface (ignores obstacle avoidance).")]
        [SerializeField] private float wallApproachStartDistance = 3.0f;
        
        [Tooltip("Distance at which spider starts rotating onto the surface.")]
        [SerializeField] private float wallClimbStartDistance = 0.8f;
        
        [Tooltip("Angle threshold (degrees) to detect if target is on a different surface (wall vs floor).")]
        [SerializeField] private float surfaceAngleThreshold = 30f;

        [Header("Waypoint -> Surface Selection")]
        [Tooltip("Radius around the destination used to find the closest climbable surface to it.")]
        [SerializeField] private float waypointSurfaceSearchRadius = 2.0f;

        [Tooltip("If we can't find any surface inside the radius, we expand up to this radius (in steps).")]
        [SerializeField] private float waypointSurfaceMaxSearchRadius = 8.0f;

        [Tooltip("How much to increase the search radius each step.")]
        [SerializeField] private float waypointSurfaceSearchStep = 2.0f;

        [Tooltip("How far off the surface to stand when approaching it (prevents clipping).")]
        [SerializeField] private float surfaceAnchorOffset = 0.05f;

        [Tooltip("When we pick a destination surface, lock its normal for this many seconds to avoid flicker/dancing.")]
        [SerializeField] private float surfaceNormalLockSeconds = 0.5f;

        [Header("Debug")]
        [SerializeField] private bool drawSensorRays = false;
        [SerializeField] private bool drawDangerRays = false;
        [SerializeField] private bool drawRepulsion = true;
        [SerializeField] private bool drawBestDir = true;
        [SerializeField] private bool drawSurfaceRays = false;
        [SerializeField] private bool debugLogs = false;

        private Vector3? _currentTarget;
        private bool _isStopped = true;
        private LayerMask _activeObstacleMask;
        private Vector3 _debugBestDir;

        private float _speedMultiplier = 1.0f;

        // Hopping state
        private bool _isHopping;
        private float _nextHopTime;
        private float _hopT;
        private Vector3 _hopStartPos;
        private Vector3 _hopEndPos;
        private Vector3 _hopUp;

        // Surface normal lock
        private Vector3 _lockedSurfaceNormal = Vector3.zero;
        private float _surfaceNormalUnlockTime = -1f;

        // Wall climbing state
        private enum ClimbState { None, Approaching, Climbing }
        private ClimbState _climbState = ClimbState.None;
        private Vector3 _targetSurfaceNormal = Vector3.zero;
        private Vector3 _wallContactPoint = Vector3.zero;
        private bool _lastFrameWasClimbing = false;

        // Cache to reduce allocations
        private readonly Collider[] _surfaceOverlap = new Collider[64];

        private void Awake()
        {
            _activeObstacleMask = obstacleLayers;
        }

        public void SetSpeedMultiplier(float multiplier)
        {
            _speedMultiplier = Mathf.Max(0f, multiplier);
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
            _isHopping = false;
            _lockedSurfaceNormal = Vector3.zero;
            _surfaceNormalUnlockTime = -1f;
            _climbState = ClimbState.None;
        }

        public bool HasReachedDestination(float threshold = 0.2f)
        {
            if (_currentTarget == null) return true;

            Vector3 target = _currentTarget.Value;
            Vector3 diff = transform.position - target;
            float distance;

            // For spiders with surface alignment, use 3D distance
            if (enableSurfaceAlignment && useLocalUpPlane)
            {
                distance = diff.magnitude;
            }
            else
            {
                // For regular ground units, use planar distance
                Vector3 up = GetUp();
                distance = Vector3.ProjectOnPlane(diff, up).magnitude;
            }

            // Basic distance check
            if (distance >= threshold)
                return false;

            // IMPORTANT: If we're close enough by distance, verify there's no wall between us and the waypoint
            // This prevents "reaching" a waypoint that's on the other side of a wall
            if (enableSurfaceAlignment)
            {
                Vector3 toTarget = target - transform.position;
                float rayDist = toTarget.magnitude;
                
                if (rayDist > 0.1f) // Only check if not extremely close
                {
                    // Cast ray from spider to waypoint
                    if (Physics.Raycast(transform.position, toTarget.normalized, out RaycastHit hit, rayDist, surfaceLayers, QueryTriggerInteraction.Ignore))
                    {
                        // We hit something between us and the waypoint
                        // Check if the hit point is significantly closer than the waypoint
                        float hitDist = hit.distance;
                        if (hitDist < rayDist - 0.3f) // Wall is blocking the path
                        {
                            if (debugLogs && Time.frameCount % 60 == 0)
                                Debug.Log($"[Spider] Close to waypoint ({distance:F2}m) but wall blocking at {hitDist:F2}m");
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Returns how close we are to the destination (3D distance for spiders, planar for others).
        /// </summary>
        public float GetDistanceToDestination()
        {
            if (_currentTarget == null) return 0f;

            Vector3 diff = transform.position - _currentTarget.Value;

            if (enableSurfaceAlignment && useLocalUpPlane)
                return diff.magnitude;

            Vector3 up = GetUp();
            return Vector3.ProjectOnPlane(diff, up).magnitude;
        }

        /// <summary>
        /// Checks if there's a clear line of sight to the current destination.
        /// </summary>
        public bool HasLineOfSightToDestination()
        {
            if (_currentTarget == null) return false;

            Vector3 toTarget = _currentTarget.Value - transform.position;
            float dist = toTarget.magnitude;

            if (dist < 0.1f) return true;

            return !Physics.Raycast(transform.position, toTarget.normalized, dist - 0.1f, surfaceLayers, QueryTriggerInteraction.Ignore);
        }

        private void Update()
        {
            if (_isStopped || _currentTarget == null) return;

            float dt = Time.deltaTime;
            Vector3 rawTarget = _currentTarget.Value;
            Vector3 up = GetUp();

            // First, update surface alignment (keeps spider stuck to current surface)
            if (enableSurfaceAlignment)
                UpdateSurfaceAlignment(dt);

            Vector3 finalDir = Vector3.zero;

            // Debug: Show current orientation
            if (debugLogs && Time.frameCount % 60 == 0)
            {
                float angleFromWorldUp = Vector3.Angle(transform.up, Vector3.up);
                Debug.Log($"[Spider] Up angle from world: {angleFromWorldUp:F1}°, State: {_climbState}");
            }

            if (enableSurfaceAlignment && useLocalUpPlane)
            {
                // Check if waypoint is on a different surface than we're currently on
                bool waypointOnDifferentSurface = CheckIfTargetOnDifferentSurface(rawTarget, out Vector3 surfacePoint, out Vector3 surfaceNormal);

                if (waypointOnDifferentSurface)
                {
                    _wallContactPoint = surfacePoint;
                    _targetSurfaceNormal = surfaceNormal;

                    float distToSurface = Vector3.Distance(transform.position, surfacePoint);
                    
                    // Determine if we're going TO a floor or TO a wall
                    bool targetIsFloor = Vector3.Angle(surfaceNormal, Vector3.up) < 45f;

                    if (debugLogs && Time.frameCount % 30 == 0)
                    {
                        string targetType = targetIsFloor ? "FLOOR" : "WALL";
                        Debug.Log($"[Spider] Approaching {targetType}, dist: {distToSurface:F2}, State: {_climbState}");
                    }

                    // State machine for surface transitions
                    if (distToSurface < wallClimbStartDistance)
                    {
                        // TRANSITION STATE: Very close to new surface, rotate onto it
                        _climbState = ClimbState.Climbing;
                        LockSurfaceNormal(surfaceNormal);
                        
                        // Calculate movement direction based on transition type
                        finalDir = CalculateTransitionDirection(surfacePoint, surfaceNormal, rawTarget, targetIsFloor);
                        
                        if (drawSurfaceRays)
                        {
                            Debug.DrawLine(transform.position, surfacePoint, targetIsFloor ? Color.green : Color.red);
                            Debug.DrawRay(transform.position, finalDir * 2f, Color.white);
                        }
                    }
                    else if (distToSurface < wallApproachStartDistance)
                    {
                        // APPROACH STATE: Close enough, move toward surface, ignore obstacle avoidance
                        _climbState = ClimbState.Approaching;
                        
                        // Move toward the surface contact point on our current plane
                        Vector3 toSurface = surfacePoint - transform.position;
                        Vector3 toSurfacePlanar = Vector3.ProjectOnPlane(toSurface, up);
                        
                        if (toSurfacePlanar.sqrMagnitude > 0.01f)
                            finalDir = toSurfacePlanar.normalized;
                        else
                        {
                            // We're directly above/below the surface point
                            // Use world-space direction toward the surface
                            finalDir = toSurface.normalized;
                        }
                        
                        if (drawSurfaceRays)
                            Debug.DrawLine(transform.position, surfacePoint, Color.yellow);
                    }
                    else
                    {
                        // FAR STATE: Use normal navigation toward the surface
                        _climbState = ClimbState.None;
                    }
                }
                else
                {
                    // Waypoint is on the same surface type as us
                    _climbState = ClimbState.None;
                }
            }
            else
            {
                _climbState = ClimbState.None;
            }

            // Standard navigation (used when not in special climbing states)
            if (_climbState == ClimbState.None)
            {
                Vector3 toTargetWorld = rawTarget - transform.position;
                Vector3 toTargetPlanar = Vector3.ProjectOnPlane(toTargetWorld, up);
                float planarDist = toTargetPlanar.magnitude;
                float totalDist = toTargetWorld.magnitude;

                // ANTI-CIRCLING FIX:
                // When very close to target, go directly toward it without obstacle avoidance
                float directApproachDist = 1.5f; // Within this distance, reduce obstacle influence
                float steeringDisableDist = 0.8f; // Within this distance, go direct
                
                if (planarDist < 0.01f && totalDist > 0.1f)
                {
                    // Target is directly above/below us on different surface
                    finalDir = toTargetWorld.normalized;
                    
                    if (debugLogs)
                        Debug.Log($"[Spider] Target above/below, going 3D direct");
                }
                else if (planarDist > 0.001f)
                {
                    Vector3 idealDir = toTargetPlanar.normalized;
                    finalDir = idealDir;

                    if (planarDist > directApproachDist)
                    {
                        // Far from target - full obstacle avoidance
                        if (TryGetEmergencyRepulsion(out Vector3 repulsionDir))
                        {
                            finalDir = repulsionDir;
                            if (debugLogs && Time.frameCount % 60 == 0)
                                Debug.Log($"[Spider] Emergency repulsion active");
                        }
                        else
                        {
                            finalDir = ComputeContextSteering(idealDir);
                        }
                    }
                    else if (planarDist > steeringDisableDist)
                    {
                        // Medium distance - steering with limits
                        Vector3 steeringDir = ComputeContextSteering(idealDir);
                        float steeringAngle = Vector3.Angle(idealDir, steeringDir);
                        
                        if (steeringAngle < 45f)
                        {
                            finalDir = steeringDir;
                        }
                        else
                        {
                            // Steering deviates too much - blend toward target
                            finalDir = Vector3.Slerp(steeringDir, idealDir, 0.7f).normalized;
                            
                            if (debugLogs && Time.frameCount % 30 == 0)
                                Debug.Log($"[Spider] Steering angle {steeringAngle:F0}° too high, blending toward target");
                        }
                    }
                    else
                    {
                        // Very close - go direct to target, ignore all obstacles
                        finalDir = idealDir;
                        
                        if (debugLogs && Time.frameCount % 30 == 0)
                            Debug.Log($"[Spider] Very close ({planarDist:F2}m), going direct to waypoint");
                    }
                    
                    // CIRCLING DETECTION: Check if we're moving perpendicular to target
                    if (debugLogs && planarDist < 2.0f)
                    {
                        float moveTowardTarget = Vector3.Dot(finalDir, idealDir);
                        if (moveTowardTarget < 0.3f && Time.frameCount % 20 == 0)
                        {
                            Debug.LogWarning($"[Spider] POSSIBLE CIRCLING: dist={planarDist:F2}, dot={moveTowardTarget:F2}, finalDir angle from target={Vector3.Angle(finalDir, idealDir):F0}°");
                        }
                    }
                }
                else
                {
                    // Extremely close - we're essentially at the target
                    finalDir = Vector3.zero;
                    
                    if (debugLogs && Time.frameCount % 60 == 0)
                        Debug.Log($"[Spider] At waypoint (planarDist={planarDist:F3})");
                }
            }

            _debugBestDir = finalDir;

            // Handle hopping state reset when transitioning to climbing
            bool isClimbingNow = _climbState == ClimbState.Climbing;
            if (isClimbingNow && !_lastFrameWasClimbing)
            {
                _isHopping = false;
                _nextHopTime = Time.time;
            }
            _lastFrameWasClimbing = isClimbingNow;

            // Execute movement
            if (locomotionMode == LocomotionMode.Continuous)
                MoveAndRotateContinuous(finalDir, dt);
            else
                MoveAndRotateHopping(finalDir, dt);
        }

        /// <summary>
        /// Calculates the movement direction when transitioning between surfaces.
        /// </summary>
        private Vector3 CalculateTransitionDirection(Vector3 surfacePoint, Vector3 surfaceNormal, Vector3 waypoint, bool targetIsFloor)
        {
            Vector3 toSurface = surfacePoint - transform.position;
            
            if (targetIsFloor)
            {
                // WALL -> FLOOR transition
                // We want to move "down" the wall toward the floor
                
                // Project the direction to floor onto our current movement plane (the wall)
                Vector3 toFloorPlanar = Vector3.ProjectOnPlane(toSurface, transform.up);
                
                if (toFloorPlanar.sqrMagnitude > 0.01f)
                {
                    return toFloorPlanar.normalized;
                }
                else
                {
                    // We're directly above the floor point on the wall
                    // Move in the direction that goes "down" the wall (toward world down)
                    Vector3 wallDownDir = Vector3.ProjectOnPlane(Vector3.down, transform.up).normalized;
                    if (wallDownDir.sqrMagnitude > 0.01f)
                        return wallDownDir;
                    else
                        return transform.forward;
                }
            }
            else
            {
                // FLOOR -> WALL transition
                // We want to move toward the wall and then up it
                
                // First, move toward the wall contact point
                Vector3 toWall = toSurface;
                Vector3 toWallPlanar = Vector3.ProjectOnPlane(toWall, transform.up);
                
                if (toWallPlanar.sqrMagnitude > 0.01f)
                {
                    return toWallPlanar.normalized;
                }
                else
                {
                    // We're at the base of the wall, move toward the waypoint projected onto the wall
                    Vector3 toWaypoint = waypoint - transform.position;
                    Vector3 toWaypointOnWall = Vector3.ProjectOnPlane(toWaypoint, surfaceNormal);
                    
                    if (toWaypointOnWall.sqrMagnitude > 0.01f)
                        return toWaypointOnWall.normalized;
                    else
                        return transform.forward;
                }
            }
        }

        /// <summary>
        /// Checks if the target waypoint is on a surface with a significantly different normal
        /// (e.g., a wall when we're on the floor, OR floor when we're on wall).
        /// 
        /// IMPORTANT: Returns the point on the surface that the spider should walk TO,
        /// which is the closest point on the SAME FACE as the waypoint, not just any face of the collider.
        /// </summary>
        private bool CheckIfTargetOnDifferentSurface(Vector3 target, out Vector3 surfacePoint, out Vector3 surfaceNormal)
        {
            surfacePoint = target;
            surfaceNormal = Vector3.up;

            // Step 1: Find the surface that the TARGET/WAYPOINT is on or near
            // We raycast FROM the waypoint in multiple directions to find which surface face it's on
            if (!TryFindSurfaceAtWaypoint(target, out Collider surfaceCol, out Vector3 waypointSurfacePoint, out Vector3 waypointFaceNormal))
            {
                if (debugLogs && Time.frameCount % 120 == 0)
                    Debug.Log("[Spider] CheckIfTargetOnDifferentSurface: No surface found near waypoint");
                return false;
            }

            // Compare the target surface normal with our current up direction
            float angle = Vector3.Angle(transform.up, waypointFaceNormal);

            if (debugLogs && Time.frameCount % 120 == 0)
            {
                Debug.Log($"[Spider] Surface check: our up vs target face normal = {angle:F1}° (threshold: {surfaceAngleThreshold}°)");
            }

            // If the angle is NOT significant, no transition needed
            if (angle <= surfaceAngleThreshold)
                return false;

            // Step 2: Find where the spider should go to reach this SPECIFIC FACE of the wall
            // NOT just any point on the collider, but a point on the SAME FACE as the waypoint
            surfacePoint = FindApproachPointOnSameFace(surfaceCol, waypointSurfacePoint, waypointFaceNormal);
            surfaceNormal = waypointFaceNormal;

            if (debugLogs && Time.frameCount % 60 == 0)
            {
                bool targetIsFloor = Vector3.Angle(waypointFaceNormal, Vector3.up) < 45f;
                bool currentlyOnFloor = Vector3.Angle(transform.up, Vector3.up) < 45f;
                
                string transitionType;
                if (currentlyOnFloor && !targetIsFloor)
                    transitionType = "FLOOR->WALL";
                else if (!currentlyOnFloor && targetIsFloor)
                    transitionType = "WALL->FLOOR";
                else
                    transitionType = "SURFACE->SURFACE";

                float distToApproach = Vector3.Distance(transform.position, surfacePoint);
                Debug.Log($"[Spider] {transitionType} detected. Approach point dist: {distToApproach:F2}");
            }

            return true;
        }

        /// <summary>
        /// Finds the surface and face normal at a waypoint position.
        /// Raycasts from the waypoint outward to find which face of a surface the waypoint is on/near.
        /// </summary>
        private bool TryFindSurfaceAtWaypoint(Vector3 waypoint, out Collider surface, out Vector3 surfacePoint, out Vector3 faceNormal)
        {
            surface = null;
            surfacePoint = waypoint;
            faceNormal = Vector3.up;

            // Try raycasting from waypoint in several directions to find the surface it's on
            Vector3[] rayDirections = new Vector3[]
            {
                Vector3.down,    // Floor below waypoint
                Vector3.up,      // Ceiling above waypoint
                Vector3.forward,
                Vector3.back,
                Vector3.left,
                Vector3.right,
                // Also try from waypoint toward spider (the surface might be between us)
                (transform.position - waypoint).normalized
            };

            float bestDist = float.MaxValue;
            RaycastHit bestHit = default;
            bool found = false;

            foreach (var dir in rayDirections)
            {
                // Raycast from slightly offset from waypoint
                Vector3 rayOrigin = waypoint - dir * 0.5f;
                
                if (Physics.Raycast(rayOrigin, dir, out RaycastHit hit, 3f, surfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    // Check if this hit is close to the waypoint
                    float distFromWaypoint = Vector3.Distance(hit.point, waypoint);
                    
                    if (distFromWaypoint < bestDist && distFromWaypoint < waypointSurfaceSearchRadius)
                    {
                        bestDist = distFromWaypoint;
                        bestHit = hit;
                        found = true;
                    }
                }
            }

            if (!found)
            {
                // Fallback: use overlap sphere like before
                return TryFindSurfaceNearPoint(waypoint, out surface, out surfacePoint, out faceNormal);
            }

            surface = bestHit.collider;
            surfacePoint = bestHit.point;
            faceNormal = bestHit.normal;

            if (drawSurfaceRays)
            {
                Debug.DrawLine(waypoint, surfacePoint, Color.yellow, 0.5f);
                Debug.DrawRay(surfacePoint, faceNormal, Color.yellow, 0.5f);
            }

            return true;
        }

        /// <summary>
        /// Given a surface collider and a point on a specific face (with its normal),
        /// find the best approach point for the spider on that SAME FACE.
        /// </summary>
        private Vector3 FindApproachPointOnSameFace(Collider surfaceCol, Vector3 pointOnFace, Vector3 faceNormal)
        {
            // Project the spider's position onto the plane of the target face
            // Then find the closest point on the collider that's on that face

            // First, cast a ray from the spider toward the face
            Vector3 toFace = pointOnFace - transform.position;
            
            // Check if there's a clear line of sight to the face
            if (Physics.Raycast(transform.position, toFace.normalized, out RaycastHit directHit, toFace.magnitude + 1f, surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                // Check if we hit the same face (normal should be similar)
                if (Vector3.Angle(directHit.normal, faceNormal) < 30f)
                {
                    // Great, we can see this face directly
                    if (drawSurfaceRays)
                        Debug.DrawLine(transform.position, directHit.point, Color.green, 0.5f);
                    return directHit.point;
                }
            }

            // We can't see the target face directly (it's on the other side of the wall)
            // Find the edge of the wall and return a point that leads around it
            
            // Project spider position onto the face plane
            Plane facePlane = new Plane(faceNormal, pointOnFace);
            Vector3 projectedSpider = transform.position - faceNormal * facePlane.GetDistanceToPoint(transform.position);
            
            // The approach point should be at the edge of the collider, on the target face side
            // For now, return the closest point on the collider from the projected position
            Vector3 approachPoint = surfaceCol.ClosestPoint(projectedSpider);
            
            // Verify this point is actually on the correct face by checking normal
            Vector3 checkDir = (approachPoint - (approachPoint + faceNormal * 0.5f)).normalized;
            if (Physics.Raycast(approachPoint + faceNormal * 0.5f, -faceNormal, out RaycastHit checkHit, 1f, surfaceLayers))
            {
                if (Vector3.Angle(checkHit.normal, faceNormal) < 30f)
                {
                    if (drawSurfaceRays)
                        Debug.DrawLine(transform.position, approachPoint, Color.cyan, 0.5f);
                    return approachPoint;
                }
            }

            // If we still can't find a good point on the correct face,
            // return the waypoint's surface point as fallback (spider will need to go around)
            if (debugLogs)
                Debug.Log("[Spider] Can't find direct approach to target face, waypoint may require going around obstacle");
            
            if (drawSurfaceRays)
                Debug.DrawLine(transform.position, pointOnFace, Color.red, 0.5f);
            
            return pointOnFace;
        }

        private bool TryFindSurfaceNearPoint(Vector3 point, out Collider surface, out Vector3 closestPoint, out Vector3 normal)
        {
            surface = null;
            closestPoint = point;
            normal = Vector3.up;

            float r = Mathf.Max(0.25f, waypointSurfaceSearchRadius);
            int found = 0;

            while (r <= waypointSurfaceMaxSearchRadius)
            {
                found = Physics.OverlapSphereNonAlloc(point, r, _surfaceOverlap, surfaceLayers, QueryTriggerInteraction.Ignore);
                if (found > 0) break;
                r += Mathf.Max(0.25f, waypointSurfaceSearchStep);
            }

            if (found <= 0) return false;

            // Find the closest surface
            float bestDist = float.PositiveInfinity;
            Collider bestCol = null;

            for (int i = 0; i < found; i++)
            {
                Collider c = _surfaceOverlap[i];
                if (c == null) continue;

                Vector3 cp = c.ClosestPoint(point);
                float d = (cp - point).sqrMagnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    bestCol = c;
                    closestPoint = cp;
                }
                _surfaceOverlap[i] = null;
            }

            if (bestCol == null) return false;

            surface = bestCol;

            // Get the surface normal by raycasting
            Vector3 toSurface = (closestPoint - point);
            Vector3 rayDir = toSurface.sqrMagnitude > 0.001f ? toSurface.normalized : Vector3.down;
            Vector3 rayOrigin = closestPoint - rayDir * 0.3f;

            if (Physics.Raycast(rayOrigin, rayDir, out RaycastHit hit, 1.0f, surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                normal = hit.normal;
                closestPoint = hit.point;
            }
            else
            {
                // Fallback: try casting from above the point downward
                rayOrigin = point + Vector3.up * 2f;
                if (Physics.Raycast(rayOrigin, Vector3.down, out hit, 5f, surfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    normal = hit.normal;
                    closestPoint = hit.point;
                }
                else
                {
                    normal = Vector3.up; // Best guess for floor
                }
            }

            return true;
        }

        private Vector3 GetUp()
        {
            return useLocalUpPlane ? transform.up : Vector3.up;
        }

        private void LockSurfaceNormal(Vector3 normal)
        {
            if (normal == Vector3.zero) return;
            _lockedSurfaceNormal = normal.normalized;
            _surfaceNormalUnlockTime = Time.time + Mathf.Max(0.01f, surfaceNormalLockSeconds);
        }

        private bool TryGetEmergencyRepulsion(out Vector3 repulsionDir)
        {
            repulsionDir = Vector3.zero;

            Vector3 up = GetUp();
            Vector3 sensorPos = transform.position + up * sensorVerticalOffset;

            Collider[] hits = Physics.OverlapSphere(sensorPos, bodyRadius * 0.9f, _activeObstacleMask, QueryTriggerInteraction.Ignore);
            if (hits.Length <= 0) return false;

            Vector3 avgPush = Vector3.zero;

            foreach (var col in hits)
            {
                // When climbing/approaching, only ignore the SPECIFIC surface we're transitioning to
                // Not ALL surfaces in surfaceLayers
                if (_climbState != ClimbState.None && enableSurfaceAlignment)
                {
                    // Check if this collider is the surface we're trying to climb
                    if (IsColliderTheTargetSurface(col))
                        continue;
                }

                Vector3 closestPoint = col.ClosestPoint(sensorPos);
                Vector3 push = sensorPos - closestPoint;
                push = Vector3.ProjectOnPlane(push, up);

                if (push.sqrMagnitude < 0.0001f)
                    push = Vector3.ProjectOnPlane(transform.forward, up);

                if (push.sqrMagnitude < 0.0001f)
                    push = AnyPlanarAxis(up);

                avgPush += push.normalized;
            }

            if (avgPush == Vector3.zero) return false;

            repulsionDir = avgPush.normalized;
            if (drawRepulsion) Debug.DrawRay(transform.position, repulsionDir * 2f, Color.magenta);
            return true;
        }

        /// <summary>
        /// Checks if a collider is the surface we're currently trying to climb onto.
        /// We only want to ignore collision with the specific surface we're transitioning to.
        /// </summary>
        private bool IsColliderTheTargetSurface(Collider col)
        {
            if (_climbState == ClimbState.None)
                return false;

            if (_wallContactPoint == Vector3.zero)
                return false;

            // Check if this collider contains our target contact point
            Vector3 closestToContact = col.ClosestPoint(_wallContactPoint);
            float distToContact = Vector3.Distance(closestToContact, _wallContactPoint);

            // If the collider is very close to our target contact point, it's the surface we're climbing
            return distToContact < 0.5f;
        }

        private Vector3 ComputeContextSteering(Vector3 idealDir)
        {
            int count = Mathf.Max(4, sensorResolution);
            float[] dangerMap = new float[count];
            float[] interestMap = new float[count];
            Vector3[] directions = new Vector3[count];

            Vector3 up = GetUp();

            Vector3 baseOrigin = transform.position + up * sensorVerticalOffset;
            float castRadius = bodyRadius + clearance;

            Vector3 currentForwardPlanar = Vector3.ProjectOnPlane(transform.forward, up).normalized;
            if (currentForwardPlanar.sqrMagnitude < 0.0001f)
                currentForwardPlanar = AnyPlanarAxis(up);

            Vector3 planarX = currentForwardPlanar;
            Vector3 planarZ = Vector3.Cross(up, planarX).normalized;
            planarX = Vector3.Cross(planarZ, up).normalized;

            for (int i = 0; i < count; i++)
            {
                float angle = i * 2f * Mathf.PI / count;

                Vector3 dir = (Mathf.Cos(angle) * planarX) + (Mathf.Sin(angle) * planarZ);
                dir = dir.normalized;

                directions[i] = dir;

                float targetDot = Vector3.Dot(dir, idealDir);
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
                    lookAheadDistance + castRadius,
                    _activeObstacleMask,
                    QueryTriggerInteraction.Ignore
                );

                if (hitSomething)
                {
                    // When climbing/approaching, ignore the SPECIFIC surface we're transitioning to
                    // but still avoid other surfaces
                    if (_climbState != ClimbState.None && enableSurfaceAlignment)
                    {
                        if (IsColliderTheTargetSurface(hit.collider))
                        {
                            // This is the surface we're climbing - don't mark as danger
                            continue;
                        }
                    }

                    float effectiveDistance = Mathf.Max(0.001f, hit.distance - (castRadius * 0.5f));
                    float normalized = 1f - Mathf.Clamp01(effectiveDistance / lookAheadDistance);
                    dangerMap[i] = normalized;

                    if (drawDangerRays) Debug.DrawRay(origin, dir * hit.distance, Color.red);
                }

                if (drawSensorRays)
                    Debug.DrawRay(origin, dir * (lookAheadDistance + castRadius), Color.cyan);
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

        private Vector3 AnyPlanarAxis(Vector3 up)
        {
            Vector3 a = Vector3.Cross(up, Vector3.right);
            if (a.sqrMagnitude < 0.0001f)
                a = Vector3.Cross(up, Vector3.forward);
            return a.normalized;
        }

        private void MoveAndRotateContinuous(Vector3 dir, float dt)
        {
            if (dir == Vector3.zero) return;

            Vector3 up = GetUp();

            Quaternion targetRot = Quaternion.LookRotation(dir, up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * dt);

            float currentSpeed = moveSpeed * _speedMultiplier;
            transform.position += transform.forward * currentSpeed * dt;
        }

        private void MoveAndRotateHopping(Vector3 dir, float dt)
        {
            if (dir == Vector3.zero) return;

            Vector3 up = GetUp();

            Quaternion targetRot = Quaternion.LookRotation(dir, up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * dt);

            float currentSpeed = moveSpeed * _speedMultiplier;

            if (_isHopping)
            {
                _hopT += dt / Mathf.Max(0.001f, hopDuration);
                float t = Mathf.Clamp01(_hopT);

                Vector3 pos = Vector3.Lerp(_hopStartPos, _hopEndPos, t);
                float arc = Mathf.Sin(t * Mathf.PI) * hopHeight;
                pos += _hopUp * arc;

                transform.position = pos;

                if (t >= 1f)
                    _isHopping = false;

                return;
            }

            if (Time.time < _nextHopTime)
                return;

            _nextHopTime = Time.time + Mathf.Max(0.05f, hopInterval);

            _isHopping = true;
            _hopT = 0f;

            _hopStartPos = transform.position;
            _hopUp = up;

            float hopDistance = currentSpeed * Mathf.Max(0.05f, hopDuration);
            Vector3 planarForward = Vector3.ProjectOnPlane(transform.forward, up).normalized;
            if (planarForward.sqrMagnitude < 0.0001f)
                planarForward = dir;

            _hopEndPos = _hopStartPos + planarForward * hopDistance;
        }

        private void UpdateSurfaceAlignment(float dt)
        {
            // PRIORITY 1: If actively climbing, use locked normal with faster alignment
            if (_climbState == ClimbState.Climbing && _lockedSurfaceNormal != Vector3.zero)
            {
                AlignUp(_lockedSurfaceNormal, dt * 3f); // 3x faster rotation during climb
                
                if (drawSurfaceRays)
                    Debug.DrawRay(transform.position, _lockedSurfaceNormal * 2f, Color.white);
                return;
            }
            
            // Respect time-locked normal
            if (_surfaceNormalUnlockTime > Time.time && _lockedSurfaceNormal != Vector3.zero)
            {
                AlignUp(_lockedSurfaceNormal, dt);
                return;
            }

            Vector3 up = transform.up;
            Vector3 origin = transform.position + up * 0.1f;

            // Try to find surface along our current -up direction
            bool foundSurfaceBelow = Physics.Raycast(origin, -up, out RaycastHit stickHit, surfaceStickDistance, surfaceLayers, QueryTriggerInteraction.Ignore);
            
            if (foundSurfaceBelow)
            {
                AlignUp(stickHit.normal, dt);
                if (drawSurfaceRays) Debug.DrawRay(origin, -up * stickHit.distance, Color.green);
            }
            else
            {
                if (drawSurfaceRays) Debug.DrawRay(origin, -up * surfaceStickDistance, Color.yellow);
                
                // IMPORTANT: If we can't find a surface along our current -up,
                // we might be transitioning from wall to floor.
                // Try to find ANY nearby surface and align to it.
                if (TryFindNearestSurfaceNormal(origin, out Vector3 nearestNormal, out float dist))
                {
                    if (dist < surfaceStickDistance * 1.5f)
                    {
                        AlignUp(nearestNormal, dt);
                        if (drawSurfaceRays) Debug.DrawRay(origin, nearestNormal * 2f, Color.magenta);
                        
                        if (debugLogs && Time.frameCount % 30 == 0)
                            Debug.Log($"[Spider] Recovery align to nearest surface, dist: {dist:F2}");
                    }
                }
            }

            // Probe forward for surface transitions (only when not actively in climbing state)
            if (_climbState != ClimbState.Climbing)
            {
                Vector3 forwardPlanar = Vector3.ProjectOnPlane(transform.forward, transform.up).normalized;
                if (forwardPlanar.sqrMagnitude < 0.0001f)
                    forwardPlanar = transform.forward;

                Vector3 fwdOrigin = transform.position + transform.up * 0.2f;
                if (Physics.Raycast(fwdOrigin, forwardPlanar, out RaycastHit wallHit, forwardSurfaceProbeDistance, surfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    if (wallHit.distance < wallClimbStartDistance)
                    {
                        AlignUp(wallHit.normal, dt);
                    }
                    if (drawSurfaceRays) Debug.DrawRay(fwdOrigin, forwardPlanar * wallHit.distance, Color.blue);
                }
                else
                {
                    if (drawSurfaceRays) Debug.DrawRay(fwdOrigin, forwardPlanar * forwardSurfaceProbeDistance, Color.gray);
                }
            }
        }

        /// <summary>
        /// Tries to find the nearest surface in any direction (used for transition recovery).
        /// </summary>
        private bool TryFindNearestSurfaceNormal(Vector3 origin, out Vector3 normal, out float distance)
        {
            normal = Vector3.up;
            distance = float.MaxValue;

            // Cast in several directions to find any nearby surface
            Vector3[] directions = new Vector3[]
            {
                Vector3.down,           // World down (floor)
                Vector3.up,             // World up (ceiling)
                -transform.up,          // Our current "down"
                transform.forward,      // Forward
                -transform.forward,     // Back
            };

            bool found = false;
            float bestDist = float.MaxValue;
            Vector3 bestNormal = Vector3.up;

            float searchDist = surfaceStickDistance * 2f;

            foreach (var dir in directions)
            {
                if (Physics.Raycast(origin, dir, out RaycastHit hit, searchDist, surfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    if (hit.distance < bestDist)
                    {
                        bestDist = hit.distance;
                        bestNormal = hit.normal;
                        found = true;
                        
                        if (drawSurfaceRays)
                            Debug.DrawLine(origin, hit.point, Color.cyan);
                    }
                }
            }

            if (found)
            {
                normal = bestNormal;
                distance = bestDist;
            }

            return found;
        }

        private void AlignUp(Vector3 desiredUp, float dt)
        {
            desiredUp = desiredUp.normalized;
            if (desiredUp == Vector3.zero) return;

            // Check if we're already aligned
            float currentAngle = Vector3.Angle(transform.up, desiredUp);
            if (currentAngle < 0.5f) return;

            Quaternion fromTo = Quaternion.FromToRotation(transform.up, desiredUp);
            Quaternion target = fromTo * transform.rotation;

            transform.rotation = Quaternion.Slerp(transform.rotation, target, 1f - Mathf.Exp(-surfaceAlignSpeed * dt));
        }

        private static bool IsLayerInMask(int layer, LayerMask mask)
        {
            return (mask.value & (1 << layer)) != 0;
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

            // Draw surface contact point when in transition
            if (_climbState != ClimbState.None)
            {
                Gizmos.color = _climbState == ClimbState.Climbing ? Color.red : Color.cyan;
                Gizmos.DrawWireSphere(_wallContactPoint, 0.2f);
                Gizmos.DrawRay(_wallContactPoint, _targetSurfaceNormal);
            }
        }
    }
}