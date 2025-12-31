// FILEPATH: Assets/Scripts/AI/Movement/Surface/SurfaceAlignmentHandler.cs
// 
// FIXED VERSION: Addresses transient invalid orientation during surface transitions
// 
// Key changes:
// 1. Added _rotationComplete flag to track when rotation to target is done
// 2. During transitions, use instant snapping instead of Slerp interpolation
// 3. Added ShouldBlockMovement property so navigator can pause movement during rotation
// 4. Modified AlignToNormal to snap directly during climb transitions

using UnityEngine;

namespace JellyGame.GamePlay.Enemy.AI.Movement
{
    /// <summary>
    /// Handles surface alignment and wall climbing transitions for spider-like agents.
    /// Implements both ISurfaceHandler and ISurfaceProvider for use with locomotion.
    /// 
    /// FIX NOTES:
    /// - During transitions, rotation now snaps instantly to axis-aligned orientations
    /// - Movement is blocked until rotation is complete to prevent transient invalid states
    /// - This eliminates the ~30° "tilted plane" glitch during floor→wall transitions
    /// </summary>
    public class SurfaceAlignmentHandler : ISurfaceHandler, ISurfaceProvider
    {
        private readonly Transform _transform;
        private readonly SurfaceSettings _settings;
        private readonly bool _debugRays;
        private readonly bool _debugLogs;

        // Surface state
        private Vector3 _lockedNormal = Vector3.zero;
        private float _normalUnlockTime = -1f;
        private bool _isGrounded = true;

        // Transition state
        private ClimbTransitionState _transitionState = ClimbTransitionState.None;
        private Vector3 _transitionDirection = Vector3.zero;
        private Vector3 _targetSurfaceNormal = Vector3.zero;
        private Vector3 _wallContactPoint = Vector3.zero;

        // NEW: Rotation completion tracking
        private bool _isRotationComplete = true;
        private Vector3 _pendingTargetUp = Vector3.zero;
        private const float ROTATION_COMPLETE_THRESHOLD_DEG = 2f;

        // Cache
        private readonly Collider[] _overlapBuffer = new Collider[64];

        // ---------------- DEBUG (throttled + event-based) ----------------
        private ClimbTransitionState _lastLoggedTransitionState = (ClimbTransitionState)(-1);
        private Vector3 _lastLoggedLockedNormal = new Vector3(999f, 999f, 999f);
        private int _lastBadAlignmentFrame = -999999;
        private int _lastBadTransitionFrame = -999999;

        private const float AXIS_MISALIGN_ANGLE_WARN_DEG = 6f;
        private const int BAD_ALIGN_LOG_COOLDOWN_FRAMES = 45;
        private const int BAD_TRANSITION_LOG_COOLDOWN_FRAMES = 45;
        private const bool INCLUDE_ROTATION_EULERS = true;
        
        // ---------------- Transition hysteresis / stability ----------------
        private const float EXIT_ALIGN_ANGLE_DEG = 2.0f;
        private const float EXIT_AXIS_MISALIGN_DEG = 2.0f;
        private const float MIN_TRANSITION_TIME = 0.08f;

        private float _transitionEnterTime = -999f;
        private Vector3 _lastTransitionTargetNormal = Vector3.zero;

        #region Properties

        public Vector3 CurrentUp => _transform.up;
        public bool IsGrounded => _isGrounded;
        public bool IsInTransition => _transitionState != ClimbTransitionState.None;
        public ClimbTransitionState TransitionState => _transitionState;
        public Vector3 TransitionMoveDirection => _transitionDirection;

        /// <summary>
        /// NEW: Returns true if movement should be blocked because rotation is incomplete.
        /// The navigator should check this and pause movement until rotation finishes.
        /// </summary>
        public bool ShouldBlockMovement => !_isRotationComplete && _transitionState == ClimbTransitionState.Climbing;

        #endregion

        public SurfaceAlignmentHandler(Transform transform, SurfaceSettings settings, bool debugRays = false, bool debugLogs = false)
        {
            _transform = transform;
            _settings = settings;
            _debugRays = debugRays;
            _debugLogs = debugLogs;
        }

        public void UpdateSurface(Vector3 targetPosition, float deltaTime)
        {
            UpdateTransitionState(targetPosition);

            Debug_LogTransitionAndLockChanges(targetPosition);

            if (_transitionState == ClimbTransitionState.Climbing && _lockedNormal != Vector3.zero)
            {
                // FIX: During climbing, use instant snap instead of gradual rotation
                AlignToNormalInstant(_lockedNormal, contextLabel: "Climbing/LockedNormal");
            }
            else if (_normalUnlockTime > Time.time && _lockedNormal != Vector3.zero)
            {
                // FIX: During locked normal phase, also snap instantly
                AlignToNormalInstant(_lockedNormal, contextLabel: "NormalLockHold");
            }
            else
            {
                // Normal surface alignment (not during transition) can use smooth rotation
                UpdateSurfaceAlignment(deltaTime);
            }

            // Update rotation completion status
            UpdateRotationCompletionStatus();

            Debug_CheckAndLogBadAxisAlignment(targetPosition);
        }

        /// <summary>
        /// NEW: Check if current rotation matches the target axis-aligned orientation.
        /// </summary>
        private void UpdateRotationCompletionStatus()
        {
            if (_pendingTargetUp == Vector3.zero)
            {
                _isRotationComplete = true;
                return;
            }

            float angleDiff = Vector3.Angle(_transform.up, _pendingTargetUp);
            _isRotationComplete = angleDiff < ROTATION_COMPLETE_THRESHOLD_DEG;

            if (_isRotationComplete && _debugLogs)
            {
                Debug.Log($"[SurfaceAlign] Rotation complete. up={Vec(_transform.up)} target={Vec(_pendingTargetUp)}", _transform);
            }
        }

        public void ResetTransition()
        {
            _transitionState = ClimbTransitionState.None;
            _transitionDirection = Vector3.zero;
            _lockedNormal = Vector3.zero;
            _normalUnlockTime = -1f;
            _isRotationComplete = true;
            _pendingTargetUp = Vector3.zero;

            if (_debugLogs)
            {
                Debug.Log($"[SurfaceAlign] ResetTransition() on '{_transform.name}'.", _transform);
            }
        }

        #region Transition Detection

        private void UpdateTransitionState(Vector3 targetPosition)
        {
            bool foundDifferentSurface = CheckIfTargetOnDifferentSurface(
                targetPosition,
                out Vector3 surfacePoint,
                out Vector3 surfaceNormal
            );

            if (!foundDifferentSurface)
            {
                if (_transitionState == ClimbTransitionState.None)
                {
                    _transitionDirection = Vector3.zero;
                    return;
                }

                if (ShouldEndTransitionNow())
                {
                    _transitionState = ClimbTransitionState.None;
                    _transitionDirection = Vector3.zero;
                }

                return;
            }

            _wallContactPoint = surfacePoint;
            _targetSurfaceNormal = surfaceNormal;

            if (_transitionState == ClimbTransitionState.None || _lastTransitionTargetNormal != _targetSurfaceNormal)
            {
                _transitionEnterTime = Time.time;
                _lastTransitionTargetNormal = _targetSurfaceNormal;
            }

            float distToSurface = Vector3.Distance(_transform.position, surfacePoint);

            bool targetIsWall = Vector3.Angle(surfaceNormal, Vector3.up) > 45f;
            bool targetIsFloor = !targetIsWall;
            bool targetIsTopFace = !targetIsWall && Vector3.Dot(surfaceNormal, Vector3.up) > 0.9f;

            bool spiderOnFloor = Vector3.Angle(_transform.up, Vector3.up) < 45f;
            bool spiderOnWall = !spiderOnFloor;
            bool spiderOnTopFace = Vector3.Dot(_transform.up, Vector3.up) > 0.9f;

            if (targetIsWall && spiderOnFloor)
            {
                HandleFloorToWallTransition(surfacePoint, surfaceNormal, distToSurface);
            }
            else if (targetIsFloor && spiderOnWall)
            {
                HandleWallToFloorTransition(surfacePoint, surfaceNormal, distToSurface, targetPosition);
            }
            else if (targetIsWall && spiderOnTopFace)
            {
                HandleTopToSideTransition(surfacePoint, surfaceNormal, distToSurface, targetPosition);
            }
            else if (targetIsTopFace && spiderOnWall)
            {
                HandleSideToTopTransition(surfacePoint, surfaceNormal, distToSurface, targetPosition);
            }
            else
            {
                HandleStandardTransition(surfacePoint, surfaceNormal, distToSurface, targetPosition, targetIsFloor);
            }
        }
        
        private bool ShouldEndTransitionNow()
        {
            if (Time.time - _transitionEnterTime < MIN_TRANSITION_TIME)
                return false;

            if (_lockedNormal != Vector3.zero && _normalUnlockTime > Time.time)
                return false;

            // FIX: Also require rotation to be complete before ending transition
            if (!_isRotationComplete)
                return false;

            float axisMisalign = AxisMisalignmentAngleDeg(_transform.up);
            if (axisMisalign > EXIT_AXIS_MISALIGN_DEG)
                return false;

            if (_lastTransitionTargetNormal != Vector3.zero)
            {
                float toTarget = Vector3.Angle(_transform.up, _lastTransitionTargetNormal);
                if (toTarget > EXIT_ALIGN_ANGLE_DEG)
                    return false;
            }

            return true;
        }

        private void HandleFloorToWallTransition(Vector3 surfacePoint, Vector3 surfaceNormal, float distance)
        {
            if (distance < _settings.ClimbStartDistance)
            {
                _transitionState = ClimbTransitionState.Climbing;

                Vector3 snapped = SnapNormalToAxis(surfaceNormal);
                if (_lockedNormal != snapped || _normalUnlockTime <= Time.time)
                    LockNormal(surfaceNormal, reason: "Floor->Wall ClimbStart");

                Vector3 toWall = surfacePoint - _transform.position;
                Vector3 toWallFlat = Vector3.ProjectOnPlane(toWall, Vector3.up);

                _transitionDirection = toWallFlat.sqrMagnitude > 0.001f
                    ? toWallFlat.normalized
                    : toWall.normalized;

                return;
            }

            if (distance < _settings.ApproachStartDistance)
            {
                _transitionState = ClimbTransitionState.Approaching;

                LockNormal(surfaceNormal, reason: "Floor->Wall Approach (pre-lock)", durationOverride: 0.25f);

                Vector3 toSurface = surfacePoint - _transform.position;
                Vector3 toSurfacePlanar = Vector3.ProjectOnPlane(toSurface, _transform.up);

                _transitionDirection = toSurfacePlanar.sqrMagnitude > 0.01f
                    ? toSurfacePlanar.normalized
                    : toSurface.normalized;

                return;
            }

            _transitionState = (_transitionState == ClimbTransitionState.None) ? ClimbTransitionState.None : _transitionState;
        }


        private void HandleWallToFloorTransition(Vector3 surfacePoint, Vector3 surfaceNormal, float distance, Vector3 waypoint)
        {
            if (distance < _settings.ClimbStartDistance)
            {
                _transitionState = ClimbTransitionState.Climbing;

                Vector3 snapped = SnapNormalToAxis(surfaceNormal);
                if (_lockedNormal != snapped || _normalUnlockTime <= Time.time)
                    LockNormal(surfaceNormal, reason: "Wall->Floor ClimbStart");

                Vector3 toFloor = surfacePoint - _transform.position;
                Vector3 toFloorOnWall = Vector3.ProjectOnPlane(toFloor, _transform.up);

                if (toFloorOnWall.sqrMagnitude > 0.01f)
                {
                    _transitionDirection = toFloorOnWall.normalized;
                }
                else
                {
                    Vector3 wallDownDir = Vector3.ProjectOnPlane(Vector3.down, _transform.up).normalized;
                    _transitionDirection = wallDownDir.sqrMagnitude > 0.01f ? wallDownDir : _transform.forward;
                }

                return;
            }

            if (distance < _settings.ApproachStartDistance)
            {
                _transitionState = ClimbTransitionState.Approaching;

                Vector3 toSurface = surfacePoint - _transform.position;
                Vector3 toSurfacePlanar = Vector3.ProjectOnPlane(toSurface, _transform.up);

                _transitionDirection = toSurfacePlanar.sqrMagnitude > 0.01f
                    ? toSurfacePlanar.normalized
                    : toSurface.normalized;

                return;
            }

            _transitionState = (_transitionState == ClimbTransitionState.None) ? ClimbTransitionState.None : _transitionState;
        }

        private void HandleTopToSideTransition(Vector3 surfacePoint, Vector3 surfaceNormal, float distance, Vector3 waypoint)
        {
            Vector3 cleanNormal = surfaceNormal;
            cleanNormal.y = 0f;

            if (cleanNormal.sqrMagnitude > 0.01f)
            {
                cleanNormal = SnapNormalToAxis(cleanNormal);
            }
            else
            {
                Vector3 toWaypoint = waypoint - _transform.position;
                toWaypoint.y = 0f;
                if (toWaypoint.sqrMagnitude > 0.01f)
                {
                    cleanNormal = -toWaypoint.normalized;
                    cleanNormal = SnapNormalToAxis(cleanNormal);
                }
            }

            if (distance < _settings.ClimbStartDistance)
            {
                _transitionState = ClimbTransitionState.Climbing;
                LockNormal(cleanNormal, reason: "Top->Side ClimbStart");

                Vector3 toEdge = surfacePoint - _transform.position;
                Vector3 toEdgePlanar = Vector3.ProjectOnPlane(toEdge, _transform.up);

                if (toEdgePlanar.sqrMagnitude > 0.01f)
                {
                    _transitionDirection = toEdgePlanar.normalized;
                }
                else
                {
                    _transitionDirection = -cleanNormal;
                }

                if (_debugRays)
                {
                    Debug.DrawRay(_transform.position, cleanNormal * 3f, Color.magenta, 0.5f);
                }
            }
            else if (distance < _settings.ApproachStartDistance)
            {
                _transitionState = ClimbTransitionState.Climbing;
                LockNormal(cleanNormal, reason: "Top->Side ApproachStart (pre-rotate)");

                Vector3 toSurface = surfacePoint - _transform.position;
                Vector3 toSurfacePlanar = Vector3.ProjectOnPlane(toSurface, _transform.up);

                _transitionDirection = toSurfacePlanar.sqrMagnitude > 0.01f
                    ? toSurfacePlanar.normalized
                    : toSurface.normalized;

                if (_debugRays)
                {
                    Debug.DrawRay(_transform.position, cleanNormal * 3f, Color.yellow, 0.5f);
                }
            }
            else
            {
                _transitionState = ClimbTransitionState.None;
            }
        }

        private void HandleSideToTopTransition(Vector3 surfacePoint, Vector3 surfaceNormal, float distance, Vector3 waypoint)
        {
            if (distance < _settings.ClimbStartDistance)
            {
                _transitionState = ClimbTransitionState.Climbing;
                LockNormal(surfaceNormal, reason: "Side->Top ClimbStart");

                Vector3 toTop = surfacePoint - _transform.position;
                Vector3 toTopOnWall = Vector3.ProjectOnPlane(toTop, _transform.up);

                if (toTopOnWall.sqrMagnitude > 0.01f)
                {
                    _transitionDirection = toTopOnWall.normalized;
                }
                else
                {
                    Vector3 wallUp = Vector3.Cross(Vector3.Cross(_transform.up, Vector3.up), _transform.up).normalized;
                    _transitionDirection = wallUp.sqrMagnitude > 0.01f ? wallUp : _transform.forward;
                }
            }
            else if (distance < _settings.ApproachStartDistance)
            {
                _transitionState = ClimbTransitionState.Approaching;

                Vector3 toSurface = surfacePoint - _transform.position;
                Vector3 toSurfacePlanar = Vector3.ProjectOnPlane(toSurface, _transform.up);

                _transitionDirection = toSurfacePlanar.sqrMagnitude > 0.01f
                    ? toSurfacePlanar.normalized
                    : toSurface.normalized;
            }
            else
            {
                _transitionState = ClimbTransitionState.None;
            }
        }

        private void HandleStandardTransition(Vector3 surfacePoint, Vector3 surfaceNormal, float distance, Vector3 waypoint, bool targetIsFloor)
        {
            if (distance < _settings.ClimbStartDistance)
            {
                _transitionState = ClimbTransitionState.Climbing;
                LockNormal(surfaceNormal, reason: "Standard ClimbStart");
                _transitionDirection = CalculateTransitionDirection(surfacePoint, surfaceNormal, waypoint, targetIsFloor);
            }
            else if (distance < _settings.ApproachStartDistance)
            {
                _transitionState = ClimbTransitionState.Approaching;

                Vector3 toSurface = surfacePoint - _transform.position;
                Vector3 toSurfacePlanar = Vector3.ProjectOnPlane(toSurface, _transform.up);

                _transitionDirection = toSurfacePlanar.sqrMagnitude > 0.01f
                    ? toSurfacePlanar.normalized
                    : toSurface.normalized;
            }
            else
            {
                _transitionState = ClimbTransitionState.None;
            }
        }

        private Vector3 CalculateTransitionDirection(Vector3 surfacePoint, Vector3 surfaceNormal, Vector3 waypoint, bool targetIsFloor)
        {
            Vector3 toSurface = surfacePoint - _transform.position;

            if (targetIsFloor)
            {
                Vector3 toFloorPlanar = Vector3.ProjectOnPlane(toSurface, _transform.up);

                if (toFloorPlanar.sqrMagnitude > 0.01f)
                    return toFloorPlanar.normalized;

                Vector3 wallDownDir = Vector3.ProjectOnPlane(Vector3.down, _transform.up).normalized;
                return wallDownDir.sqrMagnitude > 0.01f ? wallDownDir : _transform.forward;
            }
            else
            {
                Vector3 toWallPlanar = Vector3.ProjectOnPlane(toSurface, _transform.up);

                if (toWallPlanar.sqrMagnitude > 0.01f)
                    return toWallPlanar.normalized;

                Vector3 toWaypoint = waypoint - _transform.position;
                Vector3 onWall = Vector3.ProjectOnPlane(toWaypoint, surfaceNormal);

                return onWall.sqrMagnitude > 0.01f ? onWall.normalized : _transform.forward;
            }
        }

        #endregion

        #region Surface Detection

        private bool CheckIfTargetOnDifferentSurface(Vector3 target, out Vector3 surfacePoint, out Vector3 surfaceNormal)
        {
            surfacePoint = target;
            surfaceNormal = Vector3.up;

            if (!TryFindSurfaceAtWaypoint(target, out _, out Vector3 waypointSurfacePoint, out Vector3 waypointNormal))
                return false;

            float angle = Vector3.Angle(_transform.up, waypointNormal);

            if (angle <= _settings.SurfaceAngleThreshold)
                return false;

            surfacePoint = FindApproachPoint(waypointSurfacePoint, waypointNormal);
            surfaceNormal = waypointNormal;

            return true;
        }

        private bool TryFindSurfaceAtWaypoint(Vector3 waypoint, out Collider surface, out Vector3 point, out Vector3 normal)
        {
            surface = null;
            point = waypoint;
            normal = Vector3.up;

            Vector3[] directions = new Vector3[]
            {
                Vector3.down,
                Vector3.up,
                Vector3.forward,
                Vector3.back,
                Vector3.left,
                Vector3.right
            };

            float bestScore = float.MinValue;
            RaycastHit bestHit = default;
            bool found = false;

            float castStartOffset = 1.5f;
            float castDistance = 4f;

            foreach (var dir in directions)
            {
                Vector3 origin = waypoint - dir * castStartOffset;

                if (Physics.Raycast(origin, dir, out RaycastHit hit, castDistance, _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    float distFromWaypoint = Vector3.Distance(hit.point, waypoint);

                    if (distFromWaypoint > _settings.WaypointSearchRadius * 2f)
                        continue;

                    float normalCleanness = GetNormalCleanness(hit.normal);

                    if (normalCleanness < 0.5f)
                        continue;

                    float facingScore = Mathf.Max(0f, Vector3.Dot(-dir, hit.normal));
                    float distScore = 1f - Mathf.Clamp01(distFromWaypoint / (_settings.WaypointSearchRadius * 2f));
                    float totalScore = distScore * 0.2f + normalCleanness * 0.6f + facingScore * 0.2f;

                    if (totalScore > bestScore)
                    {
                        bestScore = totalScore;
                        bestHit = hit;
                        found = true;
                    }
                }
            }

            if (!found)
            {
                Vector3 toWaypoint = waypoint - _transform.position;
                if (toWaypoint.sqrMagnitude > 0.01f)
                {
                    Vector3 dir = toWaypoint.normalized;
                    if (Physics.Raycast(_transform.position, dir, out RaycastHit hit, toWaypoint.magnitude + 2f,
                        _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
                    {
                        float cleanness = GetNormalCleanness(hit.normal);
                        if (cleanness >= 0.5f)
                        {
                            bestHit = hit;
                            found = true;
                        }
                    }
                }
            }

            if (found)
            {
                surface = bestHit.collider;
                point = bestHit.point;

                normal = SnapNormalToAxis(bestHit.normal);

                if (_debugRays)
                {
                    Debug.DrawLine(waypoint, point, Color.yellow, 0.5f);
                    Debug.DrawRay(point, normal * 2f, Color.blue, 0.5f);
                }

                Debug_LogSuspiciousWaypointHit(waypoint, bestHit, normal);
            }

            return found;
        }

        private float GetNormalCleanness(Vector3 normal)
        {
            float absX = Mathf.Abs(normal.x);
            float absY = Mathf.Abs(normal.y);
            float absZ = Mathf.Abs(normal.z);

            float maxComponent = Mathf.Max(absX, Mathf.Max(absY, absZ));
            return Mathf.Clamp01((maxComponent - 0.5f) * 2f);
        }

        private Vector3 SnapNormalToAxis(Vector3 normal)
        {
            float absX = Mathf.Abs(normal.x);
            float absY = Mathf.Abs(normal.y);
            float absZ = Mathf.Abs(normal.z);

            if (absX >= absY && absX >= absZ)
                return new Vector3(Mathf.Sign(normal.x), 0f, 0f);

            if (absY >= absX && absY >= absZ)
                return new Vector3(0f, Mathf.Sign(normal.y), 0f);

            return new Vector3(0f, 0f, Mathf.Sign(normal.z));
        }

        private Vector3 FindApproachPoint(Vector3 surfacePoint, Vector3 surfaceNormal)
        {
            bool targetIsWall = Vector3.Angle(surfaceNormal, Vector3.up) > 45f;
            bool targetIsFloor = !targetIsWall;
            bool spiderOnWall = Vector3.Angle(_transform.up, Vector3.up) > 45f;
            bool spiderOnFloor = !spiderOnWall;

            if (targetIsWall && spiderOnFloor)
            {
                Vector3 toWaypoint = surfacePoint - _transform.position;
                Vector3 toWaypointFlat = new Vector3(toWaypoint.x, 0f, toWaypoint.z);

                if (toWaypointFlat.sqrMagnitude > 0.01f)
                {
                    Vector3 rayDir = toWaypointFlat.normalized;
                    Vector3 rayOrigin = _transform.position + Vector3.up * 0.3f;

                    if (Physics.Raycast(rayOrigin, rayDir, out RaycastHit wallHit,
                        toWaypointFlat.magnitude + 5f, _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
                    {
                        if (Vector3.Angle(wallHit.normal, Vector3.up) > 45f)
                        {
                            Vector3 basePoint = wallHit.point;
                            basePoint.y = _transform.position.y;
                            basePoint += wallHit.normal * (_settings.BodyRadius + 0.1f);
                            return basePoint;
                        }
                    }
                }
            }

            if (targetIsFloor && spiderOnWall)
            {
                Vector3 spiderPos = _transform.position;

                if (Physics.Raycast(spiderPos, Vector3.down, out RaycastHit floorHit,
                    10f, _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    if (Vector3.Angle(floorHit.normal, Vector3.up) < 45f)
                    {
                        Vector3 floorPoint = floorHit.point + Vector3.up * (_settings.BodyRadius * 0.1f);

                        if (_debugRays)
                            Debug.DrawLine(spiderPos, floorPoint, Color.green, 0.5f);

                        return floorPoint;
                    }
                }

                Vector3 wallDownDir = Vector3.ProjectOnPlane(Vector3.down, _transform.up).normalized;
                if (wallDownDir.sqrMagnitude > 0.01f)
                {
                    if (Physics.Raycast(spiderPos, wallDownDir, out RaycastHit edgeHit,
                        5f, _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
                    {
                        if (Vector3.Angle(edgeHit.normal, Vector3.up) < 45f)
                        {
                            if (_debugRays)
                                Debug.DrawLine(spiderPos, edgeHit.point, Color.cyan, 0.5f);

                            return edgeHit.point + edgeHit.normal * (_settings.BodyRadius * 0.1f);
                        }
                    }
                }

                Vector3 floorLevelPoint = surfacePoint;
                if (Physics.Raycast(surfacePoint + Vector3.up * 2f, Vector3.down, out RaycastHit finalHit,
                    5f, _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    floorLevelPoint = finalHit.point + Vector3.up * (_settings.BodyRadius * 0.1f);
                }
                return floorLevelPoint;
            }

            Vector3 toSurface = surfacePoint - _transform.position;
            if (Physics.Raycast(_transform.position, toSurface.normalized, out RaycastHit directHit,
                toSurface.magnitude + 1f, _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
            {
                if (Vector3.Angle(directHit.normal, surfaceNormal) < 30f)
                    return directHit.point;
            }

            return surfacePoint;
        }

        #endregion

        #region Grounding

        public Vector3 GroundPosition(Vector3 position)
        {
            if (_transitionState != ClimbTransitionState.None)
                return position;

            Vector3 up = _transform.up;
            float searchDist = _settings.StickDistance * 2f;

            Vector3 rayOrigin = position + up * _settings.StickDistance;

            if (Physics.Raycast(rayOrigin, -up, out RaycastHit hit, searchDist,
                _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
            {
                if (_debugRays)
                    Debug.DrawLine(rayOrigin, hit.point, Color.green, 0.1f);

                return hit.point + hit.normal * (_settings.BodyRadius * 0.1f);
            }

            if (Physics.Raycast(position + Vector3.up * 2f, Vector3.down, out hit, 5f,
                _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
            {
                if (_debugRays)
                    Debug.DrawLine(position, hit.point, Color.yellow, 0.1f);

                return hit.point + hit.normal * (_settings.BodyRadius * 0.1f);
            }

            return position;
        }

        public void EnsureGrounded()
        {
            if (_transitionState != ClimbTransitionState.None)
                return;

            Vector3 currentPos = _transform.position;

            if (IsPositionOnSurface(currentPos))
            {
                _isGrounded = true;
                return;
            }

            _isGrounded = false;

            Vector3 groundedPos = GroundPosition(currentPos);

            if (IsPositionOnSurface(groundedPos))
            {
                _transform.position = groundedPos;
                _isGrounded = true;
            }
        }

        private bool IsPositionOnSurface(Vector3 position)
        {
            Vector3 up = _transform.up;
            float checkDist = _settings.BodyRadius + 0.3f;

            if (Physics.Raycast(position + up * 0.1f, -up, checkDist,
                _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
                return true;

            if (Physics.Raycast(position + Vector3.up * 0.1f, Vector3.down, checkDist,
                _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
                return true;

            return Physics.OverlapSphere(position, _settings.BodyRadius * 0.5f,
                _settings.SurfaceLayers, QueryTriggerInteraction.Ignore).Length > 0;
        }

        #endregion

        #region Surface Alignment

        private void UpdateSurfaceAlignment(float deltaTime)
        {
            Vector3 pos = _transform.position;
            Vector3 currentUp = _transform.up;

            Vector3[] rayDirs = new Vector3[]
            {
                -currentUp,
                Vector3.down,
                -_transform.forward,
            };

            RaycastHit bestHit = default;
            bool foundSurface = false;
            float bestDist = float.MaxValue;

            foreach (var dir in rayDirs)
            {
                Vector3 origin = pos - dir * 0.1f;

                if (Physics.Raycast(origin, dir, out RaycastHit hit, _settings.StickDistance + 0.2f,
                    _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    if (hit.distance < bestDist)
                    {
                        bestDist = hit.distance;
                        bestHit = hit;
                        foundSurface = true;
                    }
                }
            }

            if (foundSurface)
            {
                Vector3 snappedNormal = SnapNormalToAxis(bestHit.normal);

                if (_debugRays)
                {
                    Debug.DrawRay(bestHit.point, bestHit.normal * 2f, Color.red);
                    Debug.DrawRay(bestHit.point, snappedNormal * 2f, Color.blue);
                }

                // Use smooth alignment when NOT in a transition
                AlignToNormalSmooth(snappedNormal, deltaTime, contextLabel: "UpdateSurfaceAlignment");
            }
            else
            {
                Vector3 forward = Vector3.ProjectOnPlane(_transform.forward, currentUp).normalized;
                if (forward.sqrMagnitude < 0.0001f)
                    forward = _transform.forward;

                if (Physics.Raycast(pos + currentUp * 0.2f, forward, out RaycastHit hit,
                    _settings.ForwardProbeDistance, _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    if (hit.distance < _settings.ClimbStartDistance)
                    {
                        Vector3 snappedNormal = SnapNormalToAxis(hit.normal);
                        AlignToNormalSmooth(snappedNormal, deltaTime, contextLabel: "ForwardProbe");
                    }

                    if (_debugRays)
                        Debug.DrawRay(pos + currentUp * 0.2f, forward * hit.distance, Color.yellow);
                }
            }
        }

        /// <summary>
        /// NEW: Instant snap to axis-aligned orientation. Used during transitions.
        /// This eliminates the intermediate non-axis-aligned frames.
        /// </summary>
        private void AlignToNormalInstant(Vector3 desiredUp, string contextLabel)
        {
            desiredUp = SnapNormalToAxis(desiredUp);

            if (desiredUp == Vector3.zero)
                return;

            _pendingTargetUp = desiredUp;

            float angle = Vector3.Angle(_transform.up, desiredUp);
            if (angle < 0.5f)
            {
                _isRotationComplete = true;
                return;
            }

            // Compute the target rotation directly
            Quaternion fromTo = Quaternion.FromToRotation(_transform.up, desiredUp);
            Quaternion target = fromTo * _transform.rotation;

            // INSTANT SNAP - no interpolation
            _transform.rotation = target;

            // Verify we're now axis-aligned
            float finalMisalign = AxisMisalignmentAngleDeg(_transform.up);
            if (finalMisalign > 1f && _debugLogs)
            {
                Debug.LogWarning($"[SurfaceAlign] AlignToNormalInstant resulted in misaligned up! " +
                    $"misalign={finalMisalign:F1}deg up={Vec(_transform.up)} target={Vec(desiredUp)}", _transform);
            }

            _isRotationComplete = finalMisalign < ROTATION_COMPLETE_THRESHOLD_DEG;
        }

        /// <summary>
        /// Smooth alignment using Slerp. Only used when NOT in a transition.
        /// </summary>
        private void AlignToNormalSmooth(Vector3 desiredUp, float deltaTime, string contextLabel)
        {
            desiredUp = SnapNormalToAxis(desiredUp);

            if (desiredUp == Vector3.zero)
                return;

            float angle = Vector3.Angle(_transform.up, desiredUp);
            if (angle < 0.5f)
                return;

            Quaternion fromTo = Quaternion.FromToRotation(_transform.up, desiredUp);
            Quaternion target = fromTo * _transform.rotation;

            Quaternion before = _transform.rotation;
            Vector3 upBefore = _transform.up;

            _transform.rotation = Quaternion.Slerp(_transform.rotation, target,
                1f - Mathf.Exp(-_settings.AlignSpeed * deltaTime));

            Debug_CheckAndLogBadAxisAlignment_Internal(contextLabel, desiredUp, upBefore, before);
        }

        private void LockNormal(Vector3 normal, string reason, float durationOverride = -1f)
        {
            if (normal == Vector3.zero)
                return;

            Vector3 snapped = SnapNormalToAxis(normal);

            if (_lockedNormal == snapped && _normalUnlockTime > Time.time)
                return;

            _lockedNormal = snapped;
            _pendingTargetUp = snapped;
            _isRotationComplete = false; // Mark rotation as pending

            float duration = (durationOverride > 0f) ? durationOverride : _settings.NormalLockDuration;
            _normalUnlockTime = Time.time + duration;

            if (_debugLogs)
            {
                string rotPart = INCLUDE_ROTATION_EULERS ? $" rotEuler={_transform.rotation.eulerAngles}" : "";
                Debug.Log(
                    $"[SurfaceAlign] LockNormal('{_transform.name}') reason='{reason}' " +
                    $"locked={Vec(_lockedNormal)} duration={duration:F3}s unlockAt={_normalUnlockTime:F3} now={Time.time:F3} " +
                    $"state={_transitionState} up={Vec(_transform.up)} pos={Vec(_transform.position)}{rotPart}",
                    _transform
                );
            }
        }


        #endregion

        // ----------------------------- DEBUG HELPERS -----------------------------

        private void Debug_LogTransitionAndLockChanges(Vector3 targetPosition)
        {
            if (!_debugLogs)
                return;

            if (_transitionState != _lastLoggedTransitionState)
            {
                _lastLoggedTransitionState = _transitionState;

                string rotPart = INCLUDE_ROTATION_EULERS ? $" rotEuler={_transform.rotation.eulerAngles}" : "";
                Debug.Log(
                    $"[SurfaceAlign] TransitionState('{_transform.name}') -> {_transitionState} " +
                    $"target={Vec(targetPosition)} " +
                    $"contact={Vec(_wallContactPoint)} targetNormal={Vec(_targetSurfaceNormal)} " +
                    $"moveDir={Vec(_transitionDirection)} up={Vec(_transform.up)} pos={Vec(_transform.position)}{rotPart}",
                    _transform
                );
            }

            if ((_lockedNormal - _lastLoggedLockedNormal).sqrMagnitude > 1e-6f)
            {
                _lastLoggedLockedNormal = _lockedNormal;
                Debug.Log(
                    $"[SurfaceAlign] LockedNormalChanged('{_transform.name}') locked={Vec(_lockedNormal)} " +
                    $"unlockAt={_normalUnlockTime:F3} now={Time.time:F3} state={_transitionState}",
                    _transform
                );
            }
        }

        private void Debug_CheckAndLogBadAxisAlignment(Vector3 targetPosition)
        {
            if (!_debugLogs)
                return;

            float misalignDeg = AxisMisalignmentAngleDeg(_transform.up);

            if (misalignDeg <= AXIS_MISALIGN_ANGLE_WARN_DEG)
                return;

            int f = Time.frameCount;
            if (f - _lastBadAlignmentFrame < BAD_ALIGN_LOG_COOLDOWN_FRAMES)
                return;

            _lastBadAlignmentFrame = f;

            Vector3 nearestAxis = NearestAxis(_transform.up);
            string rotPart = INCLUDE_ROTATION_EULERS ? $" rotEuler={_transform.rotation.eulerAngles}" : "";

            Debug.LogWarning(
                $"[SurfaceAlign][BAD-UP] '{_transform.name}' up is NOT axis-aligned! " +
                $"misalign={misalignDeg:F1}deg nearestAxis={Vec(nearestAxis)} " +
                $"up={Vec(_transform.up)} locked={Vec(_lockedNormal)} unlockIn={(Mathf.Max(0f, _normalUnlockTime - Time.time)):F3}s " +
                $"state={_transitionState} target={Vec(targetPosition)} contact={Vec(_wallContactPoint)} targetNormal={Vec(_targetSurfaceNormal)} " +
                $"pos={Vec(_transform.position)} moveDir={Vec(_transitionDirection)}{rotPart}",
                _transform
            );
        }

        private void Debug_CheckAndLogBadAxisAlignment_Internal(string contextLabel, Vector3 desiredUpSnapped, Vector3 upBefore, Quaternion rotBefore)
        {
            if (!_debugLogs)
                return;

            float misalignAfter = AxisMisalignmentAngleDeg(_transform.up);
            if (misalignAfter <= AXIS_MISALIGN_ANGLE_WARN_DEG)
                return;

            int f = Time.frameCount;
            if (f - _lastBadTransitionFrame < BAD_TRANSITION_LOG_COOLDOWN_FRAMES)
                return;

            _lastBadTransitionFrame = f;

            Vector3 nearestAxis = NearestAxis(_transform.up);
            float angleToDesired = Vector3.Angle(_transform.up, desiredUpSnapped);

            string beforeEuler = INCLUDE_ROTATION_EULERS ? rotBefore.eulerAngles.ToString("F2") : "n/a";
            string afterEuler = INCLUDE_ROTATION_EULERS ? _transform.rotation.eulerAngles.ToString("F2") : "n/a";

            Debug.LogWarning(
                $"[SurfaceAlign][BAD-ALIGN] '{_transform.name}' context='{contextLabel}' " +
                $"after-up misalign={misalignAfter:F1}deg nearestAxis={Vec(nearestAxis)} angleToDesired={angleToDesired:F1}deg " +
                $"desiredUp={Vec(desiredUpSnapped)} upBefore={Vec(upBefore)} upAfter={Vec(_transform.up)} " +
                $"state={_transitionState} locked={Vec(_lockedNormal)} unlockIn={(Mathf.Max(0f, _normalUnlockTime - Time.time)):F3}s " +
                $"rotBefore={beforeEuler} rotAfter={afterEuler}",
                _transform
            );
        }

        private void Debug_LogSuspiciousWaypointHit(Vector3 waypoint, RaycastHit hit, Vector3 snappedNormal)
        {
            if (!_debugLogs)
                return;

            float rawMisalign = AxisMisalignmentAngleDeg(hit.normal);
            if (rawMisalign <= 12f)
                return;

            int f = Time.frameCount;
            if (f - _lastBadTransitionFrame < BAD_TRANSITION_LOG_COOLDOWN_FRAMES)
                return;

            _lastBadTransitionFrame = f;

            Debug.LogWarning(
                $"[SurfaceAlign][WAYPOINT-HIT] '{_transform.name}' got suspicious raw normal near waypoint. " +
                $"waypoint={Vec(waypoint)} hitPoint={Vec(hit.point)} collider='{(hit.collider ? hit.collider.name : "null")}' " +
                $"rawNormal={Vec(hit.normal)} rawMisalign={rawMisalign:F1}deg snappedNormal={Vec(snappedNormal)} " +
                $"dist={hit.distance:F3} state={_transitionState}",
                _transform
            );
        }

        private static float AxisMisalignmentAngleDeg(Vector3 v)
        {
            if (v.sqrMagnitude < 1e-8f)
                return 180f;

            v.Normalize();
            Vector3 axis = NearestAxis(v);
            return Vector3.Angle(v, axis);
        }

        private static Vector3 NearestAxis(Vector3 v)
        {
            if (v.sqrMagnitude < 1e-8f)
                return Vector3.up;

            v.Normalize();
            float ax = Mathf.Abs(v.x);
            float ay = Mathf.Abs(v.y);
            float az = Mathf.Abs(v.z);

            if (ax >= ay && ax >= az)
                return new Vector3(Mathf.Sign(v.x), 0f, 0f);

            if (ay >= ax && ay >= az)
                return new Vector3(0f, Mathf.Sign(v.y), 0f);

            return new Vector3(0f, 0f, Mathf.Sign(v.z));
        }

        private static string Vec(Vector3 v)
        {
            return $"({v.x:F3},{v.y:F3},{v.z:F3})";
        }
    }
}