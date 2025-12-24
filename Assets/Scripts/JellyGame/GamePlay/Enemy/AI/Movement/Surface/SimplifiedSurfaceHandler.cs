// FILEPATH: Assets/Scripts/AI/Movement/Surface/SimplifiedSurfaceHandler.cs
using UnityEngine;

namespace JellyGame.GamePlay.Enemy.AI.Movement
{
    /// <summary>
    /// v27 - Fixed top-to-wall edge detection:
    /// - When on top/floor surface at edge, properly detect wall sides
    /// - Renamed function to CheckSurfaceContinuesAhead (works for both walls and floors)
    /// - Added TryDetectWallAtEdge for top→wall transitions
    /// - Better scan directions for edge cases
    /// </summary>
    public class SimplifiedSurfaceHandler : ISurfaceHandler, ISurfaceProvider
    {
        private readonly Transform _transform;
        private readonly SurfaceSettings _settings;
        private readonly bool _debugRays;
        private readonly bool _debugLogs;

        private Vector3 _currentNormal = Vector3.up;
        private Vector3 _targetNormal = Vector3.up;
        private bool _isTransitioning = false;
        private bool _isGrounded = true;

        private Vector3 _lockedMoveDirection = Vector3.zero;
        private Vector3 _lockedStartNormal = Vector3.up;
        private float _transitionProgress = 0f;

        private Vector3 _preTransitionPosition = Vector3.zero;
        private Vector3 _frozenPosition = Vector3.zero;

        private float _transitionCooldownTimer = 0f;
        private const float TRANSITION_COOLDOWN = 1f;
        private const float TRANSITION_DURATION = 0.35f;

        // Logging
        private bool _wasTransitioning = false;
        private bool _lastGroundedState = true;
        private int _consecutiveEdgeFailures = 0;
        private const int LOG_EDGE_FAILURE_INTERVAL = 30;

        private int _frameCount = 0;
        private const int LOG_DETECTION_EVERY_N_FRAMES = 60;

        #region Interface Properties
        public Vector3 CurrentUp => _transform.up;
        public bool IsGrounded => _isGrounded;
        public bool IsInTransition => _isTransitioning;
        public ClimbTransitionState TransitionState => _isTransitioning ? ClimbTransitionState.Climbing : ClimbTransitionState.None;
        public Vector3 TransitionMoveDirection => _lockedMoveDirection;
        public bool ShouldBlockMovement => _isTransitioning;
        #endregion

        public SimplifiedSurfaceHandler(Transform transform, SurfaceSettings settings, bool debugRays = false, bool debugLogs = false)
        {
            _transform = transform;
            _settings = settings;
            _debugRays = debugRays;
            _debugLogs = debugLogs;
            _currentNormal = SnapToAxis(transform.up);
            _targetNormal = _currentNormal;
        }

        public void UpdateSurface(Vector3 targetPosition, float deltaTime)
        {
            _frameCount++;

            if (_transitionCooldownTimer > 0f)
            {
                _transitionCooldownTimer -= deltaTime;
            }

            if (_isTransitioning)
            {
                ContinueTransition(deltaTime);
            }
            else
            {
                if (_transitionCooldownTimer <= 0f)
                {
                    DetectUpcomingSurface(targetPosition);
                }

                StayAlignedToSurface(deltaTime);
            }
        }

        private void ContinueTransition(float deltaTime)
        {
            _transitionProgress += deltaTime / TRANSITION_DURATION;
            _transform.position = _frozenPosition;

            if (_transitionProgress >= 1f)
            {
                _transitionProgress = 1f;

                AlignToNormalInstant(_targetNormal);

                Vector3 oldNormal = _currentNormal;
                _currentNormal = _targetNormal;
                _isTransitioning = false;
                _lockedMoveDirection = Vector3.zero;
                _transitionCooldownTimer = TRANSITION_COOLDOWN;

                if (_debugLogs)
                {
                    Debug.Log($"[Surface] ✓ COMPLETE: now on {V(_currentNormal)}", _transform);
                }

                _wasTransitioning = false;
                ForceGroundToCurrentSurface(oldNormal);
                return;
            }

            if (!_wasTransitioning && _debugLogs)
            {
                Debug.Log($"[Surface] TRANSITIONING: {V(_lockedStartNormal)} → {V(_targetNormal)}", _transform);
                _wasTransitioning = true;
            }

            Vector3 interpolatedNormal = Vector3.Slerp(_lockedStartNormal, _targetNormal, _transitionProgress).normalized;
            AlignToNormalSmooth(interpolatedNormal);
        }

        private void ForceGroundToCurrentSurface(Vector3 previousNormal)
        {
            Vector3 pos = _transform.position;
            Vector3 up = _currentNormal;

            bool wasOnWall = Mathf.Abs(previousNormal.y) < 0.5f;
            bool nowOnFloor = up.y > 0.5f;
            bool wasOnFloor = Mathf.Abs(previousNormal.y) > 0.5f;
            bool nowOnWall = Mathf.Abs(up.y) < 0.5f;

            RaycastHit bestHit = default;
            bool found = false;

            // Wall → Floor transition
            if (wasOnWall && nowOnFloor)
            {
                Vector3 awayFromWall = _preTransitionPosition - previousNormal * 1.0f;
                Vector3 searchOrigin = awayFromWall + Vector3.up * 0.5f;

                if (Physics.Raycast(searchOrigin, Vector3.down, out RaycastHit hit1, 3.0f,
                    _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    Vector3 hitNormal = SnapToAxis(hit1.normal);
                    if (hitNormal.y > 0.5f)
                    {
                        bestHit = hit1;
                        found = true;

                        if (_debugLogs)
                            Debug.Log($"[Surface] Wall→Floor grounding: found at {V(hit1.point)}", _transform);
                    }
                }
            }

            // Floor/Top → Wall transition
            if (wasOnFloor && nowOnWall)
            {
                // Cast from pre-transition position toward the new wall
                Vector3 searchOrigin = _preTransitionPosition + up * 0.5f;

                if (Physics.Raycast(searchOrigin, -up, out RaycastHit hit1, 3.0f,
                    _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    bestHit = hit1;
                    found = true;

                    if (_debugLogs)
                        Debug.Log($"[Surface] Floor→Wall grounding: found at {V(hit1.point)}", _transform);
                }
            }

            if (!found)
            {
                Vector3 rayOrigin1 = pos + up * 1.5f;
                if (Physics.Raycast(rayOrigin1, -up, out RaycastHit hit1, 3.0f,
                    _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    bestHit = hit1;
                    found = true;
                }
            }

            if (!found)
            {
                if (Physics.Raycast(pos, -up, out RaycastHit hit2, 2.0f,
                    _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    bestHit = hit2;
                    found = true;
                }
            }

            if (!found)
            {
                if (Physics.SphereCast(pos + up * 0.5f, 0.3f, -up, out RaycastHit hit3, 2.0f,
                    _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    bestHit = hit3;
                    found = true;
                }
            }

            if (found)
            {
                Vector3 newPos = bestHit.point + up * (_settings.BodyRadius * 0.1f);
                _transform.position = newPos;
                _isGrounded = true;

                if (_debugLogs)
                    Debug.Log($"[Surface] Post-transition grounded at {V(newPos)}", _transform);
            }
        }

        private void DetectUpcomingSurface(Vector3 targetPosition)
        {
            Vector3 pos = _transform.position;
            Vector3 up = _currentNormal;

            Vector3 toTarget = targetPosition - pos;
            Vector3 forward = Vector3.ProjectOnPlane(toTarget, up);

            float distToTarget = forward.magnitude;

            if (forward.sqrMagnitude < 0.01f)
                forward = _transform.forward;
            else
                forward = forward.normalized;

            float checkDist = _settings.ClimbStartDistance;

            bool onWall = Mathf.Abs(up.y) < 0.5f;
            bool onFloor = !onWall; // On floor or top of wall
            float movingUpward = Vector3.Dot(forward, Vector3.up);

            bool shouldLog = _debugLogs && (_frameCount % LOG_DETECTION_EVERY_N_FRAMES == 0);
            if (shouldLog)
            {
                Debug.Log($"[Surface] DETECT: pos={V(pos)} up={V(up)} fwd={V(forward)} onWall={onWall} movingUp={movingUpward:F2} distToTarget={distToTarget:F2}", _transform);
            }

            // ========== PROACTIVE FLOOR CHECK (WALL ONLY) ==========
            if (onWall)
            {
                if (TryDetectFloorNearby(pos, forward, up, shouldLog, out Vector3 floorNormal, out float floorDist))
                {
                    bool floorIsClose = floorDist < 1.5f;
                    bool movingTowardFloor = movingUpward < -0.3f && floorDist < 3.0f;

                    if (floorIsClose || movingTowardFloor)
                    {
                        if (shouldLog)
                            Debug.Log($"[Surface] Floor detected! dist={floorDist:F2} movingUp={movingUpward:F2}", _transform);

                        StartTransition(floorNormal, forward, $"floor nearby (dist={floorDist:F2})");
                        return;
                    }
                }
            }

            // ========== RAY 1: Obstacle ahead? ==========
            Vector3 ray1Origin = pos + up * 0.15f;
            if (Physics.Raycast(ray1Origin, forward, out RaycastHit hit1, checkDist,
                _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
            {
                Vector3 hitNormal = SnapToAxis(hit1.normal);
                float angle = Vector3.Angle(up, hitNormal);

                if (_debugRays)
                    Debug.DrawRay(ray1Origin, forward * hit1.distance, Color.yellow);

                if (angle > 30f)
                {
                    StartTransition(hitNormal, forward, $"surface ahead ({hit1.collider.name})");
                    return;
                }
            }

            // ========== RAY 2: Check if current surface continues ahead ==========
            Vector3 aheadPos = pos + forward * checkDist;
            bool surfaceContinues = CheckSurfaceContinuesAhead(pos, forward, up, checkDist, shouldLog);

            if (surfaceContinues)
            {
                _consecutiveEdgeFailures = 0;
                return; // Surface continues, keep walking
            }

            // ========== EDGE DETECTED ==========
            if (shouldLog)
            {
                Debug.Log($"[Surface] EDGE: Surface doesn't continue ahead at {V(aheadPos)}", _transform);
            }

            // --- FLOOR/TOP → WALL TRANSITION ---
            if (onFloor)
            {
                // Try to find a wall at the edge we're approaching
                if (TryDetectWallAtEdge(pos, forward, up, aheadPos, shouldLog, out Vector3 wallNormal))
                {
                    StartTransition(wallNormal, forward, "wall at edge");
                    _consecutiveEdgeFailures = 0;
                    return;
                }
            }

            // --- WALL → TOP TRANSITION ---
            if (onWall && movingUpward > 0.2f)
            {
                if (TryDetectWallTop(pos, forward, up, aheadPos, out Vector3 topNormal))
                {
                    StartTransition(topNormal, forward, "wall top");
                    _consecutiveEdgeFailures = 0;
                    return;
                }
            }

            // --- WALL → ADJACENT WALL (corner) ---
            if (onWall)
            {
                if (TryDetectAdjacentWall(pos, forward, up, shouldLog, out Vector3 adjWallNormal))
                {
                    StartTransition(adjWallNormal, forward, "adjacent wall");
                    _consecutiveEdgeFailures = 0;
                    return;
                }
            }

            // --- GENERIC SCAN ---
            Vector3 nextSurface = ScanForAdjacentSurface(pos, forward, up, shouldLog);
            if (nextSurface != Vector3.zero && Vector3.Angle(nextSurface, up) > NORMAL_EQUAL_ANGLE_DEG)
            {
                StartTransition(nextSurface, forward, $"scanned surface {V(nextSurface)}");
                _consecutiveEdgeFailures = 0;
                return;
            }

            _consecutiveEdgeFailures++;

            if (_debugLogs && (_consecutiveEdgeFailures % LOG_EDGE_FAILURE_INTERVAL == 0))
            {
                Debug.LogWarning($"[Surface] EDGE FAILED x{_consecutiveEdgeFailures}", _transform);
            }
        }

        /// <summary>
        /// Check if the current surface continues in the movement direction.
        /// Works for both walls and floors.
        /// </summary>
        private bool CheckSurfaceContinuesAhead(Vector3 pos, Vector3 forward, Vector3 up, float checkDist, bool shouldLog)
        {
            Vector3 aheadPos = pos + forward * checkDist;

            // Cast from above the ahead position, downward relative to current surface
            Vector3 rayOrigin = aheadPos + up * 0.5f;

            if (Physics.Raycast(rayOrigin, -up, out RaycastHit hit, 1.5f,
                _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
            {
                Vector3 hitNormal = SnapToAxis(hit.normal);

                if (_debugRays)
                    Debug.DrawRay(rayOrigin, -up * hit.distance, Color.cyan, 0.1f);

                // Check if it's the same surface type (tolerant for non-axis-aligned normals)
                if (Vector3.Angle(hitNormal, up) <= SAME_SURFACE_ANGLE_DEG)
                {
                    return true;
                }

                if (shouldLog)
                    Debug.Log($"[Surface] Surface check: found different surface {V(hitNormal)} at ahead pos", _transform);

                return false;
            }

            if (_debugRays)
                Debug.DrawRay(rayOrigin, -up * 1.5f, Color.red, 0.1f);

            if (shouldLog)
                Debug.Log($"[Surface] Surface check: no surface at ahead pos {V(aheadPos)}", _transform);

            return false;
        }

        /// <summary>
        /// Detect a wall at the edge of a floor/top surface.
        /// Used when spider is on top and approaching an edge where there's a wall side.
        /// </summary>
        private bool TryDetectWallAtEdge(Vector3 pos, Vector3 forward, Vector3 up, Vector3 aheadPos, bool shouldLog, out Vector3 wallNormal)
        {
            wallNormal = Vector3.zero;

            // Strategy 1: Cast forward from slightly below the surface level
            // This looks for the wall face that's perpendicular to the floor
            Vector3 origin1 = aheadPos - up * 0.5f; // Go below the floor level
            if (Physics.Raycast(origin1, forward, out RaycastHit hit1, 2.0f,
                _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
            {
                Vector3 normal1 = SnapToAxis(hit1.normal);

                if (_debugRays)
                    Debug.DrawRay(origin1, forward * hit1.distance, Color.magenta, 0.5f);

                // Check if it's a wall (not floor/ceiling)
                if (Mathf.Abs(normal1.y) < 0.5f && Vector3.Angle(up, normal1) > 30f)
                {
                    if (shouldLog)
                        Debug.Log($"[Surface] WallAtEdge S1: found {V(normal1)} at {V(hit1.point)}", _transform);
                    wallNormal = normal1;
                    return true;
                }
            }

            // Strategy 2: Cast downward near the edge, then cast sideways
            Vector3 origin2 = aheadPos + up * 0.2f;
            if (Physics.Raycast(origin2, -up, out RaycastHit hit2, 1.5f,
                _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
            {
                Vector3 downNormal = SnapToAxis(hit2.normal);
                if (_debugRays)
                    Debug.DrawRay(origin2, -up * hit2.distance, Color.cyan, 0.5f);

                // If we hit floor/top, look for wall beside it
                if (downNormal.y > 0.5f)
                {
                    Vector3 sideOrigin = hit2.point - up * 0.2f;
                    Vector3[] sideDirs =
                    {
                        Vector3.Cross(up, forward).normalized,
                        -Vector3.Cross(up, forward).normalized,
                        -forward,
                        forward
                    };

                    foreach (var dir in sideDirs)
                    {
                        if (Physics.Raycast(sideOrigin, dir, out RaycastHit sideHit, 1.5f,
                            _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
                        {
                            Vector3 sideNormal = SnapToAxis(sideHit.normal);
                            if (_debugRays)
                                Debug.DrawRay(sideOrigin, dir * sideHit.distance, Color.blue, 0.5f);

                            if (Mathf.Abs(sideNormal.y) < 0.5f && Vector3.Angle(up, sideNormal) > 30f)
                            {
                                if (shouldLog)
                                    Debug.Log($"[Surface] WallAtEdge S2: found {V(sideNormal)} at {V(sideHit.point)}", _transform);
                                wallNormal = sideNormal;
                                return true;
                            }
                        }
                    }
                }
            }

            // Strategy 3: Cast from above down and see if wall is below edge
            Vector3 origin3 = aheadPos + Vector3.up * 1.0f;
            if (Physics.Raycast(origin3, Vector3.down, out RaycastHit hit3, 3.0f,
                _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
            {
                Vector3 normal3 = SnapToAxis(hit3.normal);
                if (_debugRays)
                    Debug.DrawRay(origin3, Vector3.down * hit3.distance, Color.green, 0.5f);

                if (Mathf.Abs(normal3.y) < 0.5f)
                {
                    if (shouldLog)
                        Debug.Log($"[Surface] WallAtEdge S3: found {V(normal3)} at {V(hit3.point)}", _transform);
                    wallNormal = normal3;
                    return true;
                }
            }

            // Strategy 4: Sphere cast forward from edge
            Vector3 origin4 = aheadPos - up * 0.3f;
            if (Physics.SphereCast(origin4, 0.25f, forward, out RaycastHit hit4, 2.0f,
                _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
            {
                Vector3 normal4 = SnapToAxis(hit4.normal);
                if (_debugRays)
                    Debug.DrawRay(origin4, forward * hit4.distance, Color.red, 0.5f);

                if (Mathf.Abs(normal4.y) < 0.5f)
                {
                    if (shouldLog)
                        Debug.Log($"[Surface] WallAtEdge S4: found {V(normal4)} at {V(hit4.point)}", _transform);
                    wallNormal = normal4;
                    return true;
                }
            }

            // Strategy 5: Expected wall normal based on movement direction
            // If moving +Z, wall might have normal -Z
            Vector3 expectedWallNormal = -forward;
            expectedWallNormal.y = 0;
            expectedWallNormal = SnapToAxis(expectedWallNormal.normalized);

            if (expectedWallNormal != Vector3.zero)
            {
                Vector3 origin5 = aheadPos - expectedWallNormal * 1.0f; // Start from "inside" where wall would be
                if (Physics.Raycast(origin5, expectedWallNormal, out RaycastHit hit5, 2.0f,
                    _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    Vector3 normal5 = SnapToAxis(hit5.normal);

                    if (_debugRays)
                        Debug.DrawRay(origin5, expectedWallNormal * hit5.distance, Color.white, 0.5f);

                    if (Mathf.Abs(normal5.y) < 0.5f)
                    {
                        if (shouldLog)
                            Debug.Log($"[Surface] WallAtEdge S5: found {V(normal5)} at {V(hit5.point)}", _transform);
                        wallNormal = normal5;
                        return true;
                    }
                }
            }

            if (shouldLog)
                Debug.Log($"[Surface] WallAtEdge: no wall found at edge", _transform);

            return false;
        }

        /// <summary>
        /// Detect floor near the spider's current position (for wall→floor).
        /// </summary>
        private bool TryDetectFloorNearby(Vector3 pos, Vector3 forward, Vector3 up, bool shouldLog, out Vector3 floorNormal, out float floorDistance)
        {
            floorNormal = Vector3.zero;
            floorDistance = float.MaxValue;

            Vector3 origin1 = pos + Vector3.up * 0.3f;
            if (Physics.Raycast(origin1, Vector3.down, out RaycastHit hit1, 5.0f,
                _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
            {
                Vector3 normal1 = SnapToAxis(hit1.normal);

                if (_debugRays)
                    Debug.DrawRay(origin1, Vector3.down * hit1.distance, Color.green, 0.1f);

                if (normal1.y > 0.5f)
                {
                    floorDistance = pos.y - hit1.point.y;
                    floorNormal = normal1;

                    if (shouldLog)
                        Debug.Log($"[Surface] FloorNearby: found at dist={floorDistance:F2}", _transform);

                    return true;
                }
            }

            Vector3 awayFromWall = pos - up * 0.8f;
            Vector3 origin2 = awayFromWall + Vector3.up * 0.3f;
            if (Physics.Raycast(origin2, Vector3.down, out RaycastHit hit2, 5.0f,
                _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
            {
                Vector3 normal2 = SnapToAxis(hit2.normal);

                if (_debugRays)
                    Debug.DrawRay(origin2, Vector3.down * hit2.distance, Color.yellow, 0.1f);

                if (normal2.y > 0.5f)
                {
                    float dist2 = pos.y - hit2.point.y;
                    if (dist2 < floorDistance)
                    {
                        floorDistance = dist2;
                        floorNormal = normal2;
                        return true;
                    }
                }
            }

            return floorNormal != Vector3.zero;
        }

        private bool TryDetectAdjacentWall(Vector3 pos, Vector3 forward, Vector3 up, bool shouldLog, out Vector3 wallNormal)
        {
            wallNormal = Vector3.zero;

            Vector3 origin = pos + up * 0.2f;
            if (Physics.Raycast(origin, forward, out RaycastHit hit, _settings.ClimbStartDistance * 1.5f,
                _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
            {
                Vector3 normal = SnapToAxis(hit.normal);
                float angle = Vector3.Angle(up, normal);

                if (shouldLog)
                    Debug.Log($"[Surface] AdjacentWall: hit {hit.collider.name} normal={V(normal)} angle={angle:F1}", _transform);

                if (angle > 30f && Mathf.Abs(normal.y) < 0.5f)
                {
                    wallNormal = normal;
                    return true;
                }
            }

            return false;
        }

        private bool TryDetectWallTop(Vector3 pos, Vector3 forward, Vector3 up, Vector3 aheadPos, out Vector3 topNormal)
        {
            topNormal = Vector3.zero;

            Vector3 origin1 = aheadPos + Vector3.up * 2.0f;
            if (Physics.Raycast(origin1, Vector3.down, out RaycastHit hit1, 3.0f,
                _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
            {
                Vector3 normal1 = SnapToAxis(hit1.normal);
                if (_debugRays) Debug.DrawRay(origin1, Vector3.down * hit1.distance, Color.magenta, 0.5f);
                if (normal1.y > 0.5f && Vector3.Angle(up, normal1) > 30f)
                {
                    topNormal = normal1;
                    return true;
                }
            }

            Vector3 origin2 = pos + Vector3.up * 2.5f + forward * 0.5f;
            if (Physics.Raycast(origin2, Vector3.down, out RaycastHit hit2, 3.5f,
                _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
            {
                Vector3 normal2 = SnapToAxis(hit2.normal);
                if (_debugRays) Debug.DrawRay(origin2, Vector3.down * hit2.distance, Color.yellow, 0.5f);
                if (normal2.y > 0.5f && Vector3.Angle(up, normal2) > 30f)
                {
                    topNormal = normal2;
                    return true;
                }
            }

            Vector3 origin3 = aheadPos + Vector3.up * 1.5f;
            if (Physics.SphereCast(origin3, 0.3f, Vector3.down, out RaycastHit hit3, 2.0f,
                _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
            {
                Vector3 normal3 = SnapToAxis(hit3.normal);
                if (normal3.y > 0.5f && Vector3.Angle(up, normal3) > 30f)
                {
                    topNormal = normal3;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Scan in multiple directions to find any adjacent surface.
        /// </summary>
        private Vector3 ScanForAdjacentSurface(Vector3 pos, Vector3 forward, Vector3 up, bool shouldLog)
        {
            // For floor→wall, we need to check directions that include the forward component
            Vector3[] directions;

            bool onFloor = Mathf.Abs(up.y) > 0.5f;

            if (onFloor)
            {
                // When on floor/top, prioritize forward directions to find walls
                directions = new Vector3[]
                {
                    forward,                                    // Straight ahead
                    (forward - up * 0.5f).normalized,          // Slightly down-forward
                    (forward + Vector3.right * 0.5f).normalized,
                    (forward - Vector3.right * 0.5f).normalized,
                    Vector3.down,
                    -forward
                };
            }
            else
            {
                // When on wall, check all directions
                directions = new Vector3[]
                {
                    Vector3.down,
                    Vector3.up,
                    forward,
                    -forward,
                    Vector3.Cross(up, forward).normalized,
                    -Vector3.Cross(up, forward).normalized
                };
            }

            foreach (var dir in directions)
            {
                if (dir.sqrMagnitude < 0.01f)
                    continue;

                Vector3 origin = pos + up * 0.2f;
                if (Physics.Raycast(origin, dir, out RaycastHit hit, 3.0f,
                    _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    Vector3 normal = SnapToAxis(hit.normal);
                    float angle = Vector3.Angle(up, normal);

                    if (angle > 30f)
                    {
                        if (_debugRays)
                            Debug.DrawRay(origin, dir * hit.distance, Color.white, 0.3f);

                        if (shouldLog)
                            Debug.Log($"[Surface] Scan found: {V(normal)} in dir {V(dir)}", _transform);

                        return normal;
                    }
                }
            }

            return Vector3.zero;
        }

        private void StartTransition(Vector3 newNormal, Vector3 moveDirection, string reason)
        {
            newNormal = SnapToAxis(newNormal);

            if (newNormal == Vector3.zero)
                return;

            // Avoid starting micro-transitions due to float noise.
            if (Vector3.Angle(newNormal, _currentNormal) <= NORMAL_EQUAL_ANGLE_DEG)
                return;

            _preTransitionPosition = _transform.position;
            _frozenPosition = _transform.position;

            _lockedStartNormal = _currentNormal;
            _targetNormal = newNormal;
            _isTransitioning = true;
            _transitionProgress = 0f;
            _lockedMoveDirection = moveDirection;

            if (_debugLogs)
            {
                Debug.Log($"[Surface] ▶ START: {V(_currentNormal)} → {V(_targetNormal)} | {reason}", _transform);
            }
        }

        private void StayAlignedToSurface(float deltaTime)
        {
            Vector3 pos = _transform.position;
            Vector3 up = _currentNormal;

            if (TryFindGroundOnSurface(pos, up, out RaycastHit hit))
            {
                Vector3 groundNormal = SnapToAxis(hit.normal);
                float angle = Vector3.Angle(_transform.up, groundNormal);

                if (angle > 2f && angle < 20f)
                {
                    Quaternion correction = Quaternion.FromToRotation(_transform.up, groundNormal);
                    Quaternion targetRot = correction * _transform.rotation;
                    _transform.rotation = Quaternion.Slerp(_transform.rotation, targetRot, deltaTime * _settings.AlignSpeed);
                }

                Vector3 targetPos = hit.point + up * (_settings.BodyRadius * 0.1f);
                float heightDiff = Vector3.Distance(pos, targetPos);

                if (heightDiff > 0.01f)
                {
                    float lerpSpeed = heightDiff > 0.3f ? 20f : 10f;
                    _transform.position = Vector3.Lerp(pos, targetPos, deltaTime * lerpSpeed);
                }

                if (!_lastGroundedState && _debugLogs)
                {
                    Debug.Log($"[Surface] Grounded on {V(groundNormal)}", _transform);
                }
                _lastGroundedState = true;
                _isGrounded = true;
            }
            else
            {
                if (_lastGroundedState && _debugLogs)
                {
                    Debug.LogWarning($"[Surface] Lost ground!", _transform);
                }
                _lastGroundedState = false;
                _isGrounded = false;
            }
        }

        private bool TryFindGroundOnSurface(Vector3 pos, Vector3 surfaceUp, out RaycastHit bestHit)
        {
            bestHit = default;

            Vector3 rayOrigin = pos + surfaceUp * 0.5f;
            float rayDist = _settings.StickDistance + 0.5f;

            if (Physics.Raycast(rayOrigin, -surfaceUp, out bestHit, rayDist,
                _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
            {
                if (_debugRays) Debug.DrawRay(rayOrigin, -surfaceUp * bestHit.distance, Color.green);
                return true;
            }

            if (Physics.Raycast(pos, -surfaceUp, out bestHit, rayDist,
                _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
            {
                if (_debugRays) Debug.DrawRay(pos, -surfaceUp * bestHit.distance, Color.yellow);
                return true;
            }

            if (_debugRays) Debug.DrawRay(rayOrigin, -surfaceUp * rayDist, Color.red);
            return false;
        }

        private void AlignToNormalInstant(Vector3 normal)
        {
            if (normal == Vector3.zero) return;

            Vector3 forward = _transform.forward;
            Vector3 projectedForward = Vector3.ProjectOnPlane(forward, normal);

            if (projectedForward.sqrMagnitude < 0.001f)
            {
                projectedForward = Vector3.ProjectOnPlane(Vector3.forward, normal);
                if (projectedForward.sqrMagnitude < 0.001f)
                    projectedForward = Vector3.ProjectOnPlane(Vector3.right, normal);
            }

            projectedForward.Normalize();
            _transform.rotation = Quaternion.LookRotation(projectedForward, normal);
        }

        private void AlignToNormalSmooth(Vector3 normal)
        {
            if (normal.sqrMagnitude < 0.001f) return;

            Vector3 forward = _transform.forward;
            Vector3 projectedForward = Vector3.ProjectOnPlane(forward, normal);

            if (projectedForward.sqrMagnitude < 0.001f)
            {
                projectedForward = Vector3.ProjectOnPlane(Vector3.forward, normal);
                if (projectedForward.sqrMagnitude < 0.001f)
                    projectedForward = Vector3.ProjectOnPlane(Vector3.right, normal);
            }

            projectedForward.Normalize();
            _transform.rotation = Quaternion.LookRotation(projectedForward, normal);
        }

        public Vector3 GroundPosition(Vector3 position)
        {
            if (_isTransitioning)
                return _frozenPosition;

            Vector3 up = _currentNormal;
            Vector3 rayOrigin = position + up * _settings.StickDistance;

            if (Physics.Raycast(rayOrigin, -up, out RaycastHit hit, _settings.StickDistance * 2f,
                _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
            {
                return hit.point + hit.normal * (_settings.BodyRadius * 0.1f);
            }

            return position;
        }

        public void EnsureGrounded()
        {
            if (_isTransitioning)
            {
                _transform.position = _frozenPosition;
                return;
            }

            Vector3 pos = _transform.position;
            Vector3 up = _currentNormal;

            if (TryFindGroundOnSurface(pos, up, out RaycastHit hit))
            {
                Vector3 newPos = hit.point + up * (_settings.BodyRadius * 0.1f);
                float heightChange = Vector3.Distance(pos, newPos);

                if (heightChange > 0.3f && _debugLogs)
                    Debug.Log($"[Surface] Height corrected by {heightChange:F2}", _transform);

                _transform.position = newPos;
                _isGrounded = true;
            }
            else
            {
                _isGrounded = false;
            }
        }

        public void ResetTransition()
        {
            _isTransitioning = false;
            _lockedMoveDirection = Vector3.zero;
            _transitionProgress = 0f;

            _currentNormal = SnapToAxis(_transform.up);
            _targetNormal = _currentNormal;
            _lockedStartNormal = _currentNormal;
            _wasTransitioning = false;
            _consecutiveEdgeFailures = 0;

            if (_debugLogs)
                Debug.Log($"[Surface] RESET: normal={V(_currentNormal)}", _transform);
        }

        private const float AXIS_SNAP_ANGLE_DEG = 6f;          // Keep cube-stable axis snapping when nearly axis-aligned
        private const float SAME_SURFACE_ANGLE_DEG = 7.5f;      // How close normals must be to treat as "same surface"
        private const float NORMAL_EQUAL_ANGLE_DEG = 1.0f;      // Treat normals as equal within this angle (for transition checks)

        private Vector3 SnapToAxis(Vector3 v)
        {
            // IMPORTANT:
            // Old versions forced EVERY normal to the closest world axis. That breaks any connected surface
            // whose rotation isn't a clean 90° step (e.g. 15°-tilted faces).
            //
            // New behavior:
            // - If the normal is ALREADY very close to an axis (within AXIS_SNAP_ANGLE_DEG), snap it.
            //   This preserves the super-stable behavior for cube-like geometry.
            // - Otherwise, keep the real (normalized) normal so arbitrary tilts still work.
            if (v.sqrMagnitude < 0.001f)
                return Vector3.zero;

            v.Normalize();

            float ax = Mathf.Abs(v.x);
            float ay = Mathf.Abs(v.y);
            float az = Mathf.Abs(v.z);

            Vector3 axis;
            if (ax >= ay && ax >= az) axis = new Vector3(Mathf.Sign(v.x), 0f, 0f);
            else if (ay >= ax && ay >= az) axis = new Vector3(0f, Mathf.Sign(v.y), 0f);
            else axis = new Vector3(0f, 0f, Mathf.Sign(v.z));

            float dot = Mathf.Clamp(Vector3.Dot(v, axis), -1f, 1f);
            float angle = Mathf.Acos(dot) * Mathf.Rad2Deg;

            return angle <= AXIS_SNAP_ANGLE_DEG ? axis : v;
        }

        private string V(Vector3 v) => $"({v.x:F2},{v.y:F2},{v.z:F2})";
    }
}
