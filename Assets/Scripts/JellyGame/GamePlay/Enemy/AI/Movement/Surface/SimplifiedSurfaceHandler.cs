// FILEPATH: Assets/Scripts/AI/Movement/Surface/SimplifiedSurfaceHandler.cs
using UnityEngine;

namespace JellyGame.GamePlay.Enemy.AI.Movement
{
    /// <summary>
    /// v31 - Supports TILTED SURFACES (no axis snapping):
    /// 
    /// The game surface tilts in real time, so normals can be any angle.
    /// This version uses raw normals instead of snapping to axes.
    /// 
    /// Key changes from v28:
    /// - Removed SnapToAxis() forcing - normals are used as-is
    /// - Transition detection uses angle thresholds, not axis comparison
    /// - Surface alignment follows the actual surface normal
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
        private const float TRANSITION_COOLDOWN = 1.0f;
        private const float TRANSITION_DURATION = 0.35f;

        private int _frameCount = 0;
        private const int LOG_EVERY_N_FRAMES = 60;

        // Ground surface tracking for logs
        private Collider _lastGroundCollider = null;
        private Vector3 _lastGroundNormal = Vector3.zero;

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
            
            // Initialize to current surface (don't snap)
            _currentNormal = transform.up;
            _targetNormal = _currentNormal;
        }

        public void UpdateSurface(Vector3 targetPosition, float deltaTime)
        {
            _frameCount++;

            if (_transitionCooldownTimer > 0f)
                _transitionCooldownTimer -= deltaTime;

            if (_isTransitioning)
            {
                ContinueTransition(deltaTime);
            }
            else
            {
                if (_transitionCooldownTimer <= 0f)
                    DetectSurfaceTransition(targetPosition);

                StayGrounded(deltaTime);
            }
        }

        private void ContinueTransition(float deltaTime)
        {
            _transitionProgress += deltaTime / TRANSITION_DURATION;
            _transform.position = _frozenPosition;

            if (_transitionProgress >= 1f)
            {
                _transitionProgress = 1f;
                AlignToNormal(_targetNormal);

                Vector3 oldNormal = _currentNormal;
                _currentNormal = _targetNormal;
                _isTransitioning = false;
                _lockedMoveDirection = Vector3.zero;
                _transitionCooldownTimer = TRANSITION_COOLDOWN;

                if (_debugLogs)
                    Debug.Log($"[Surface] ✓ COMPLETE: now on {V(_currentNormal)}", _transform);

                SnapToSurface(oldNormal);
                return;
            }

            // Smoothly interpolate rotation during transition
            Vector3 interpolatedNormal = Vector3.Slerp(_lockedStartNormal, _targetNormal, _transitionProgress).normalized;
            AlignToNormal(interpolatedNormal);
        }

        /// <summary>
        /// Main detection logic - works with tilted surfaces
        /// </summary>
        private void DetectSurfaceTransition(Vector3 targetPosition)
        {
            Vector3 pos = _transform.position;
            Vector3 up = _currentNormal;

            // Calculate movement direction (projected onto current surface)
            Vector3 toTarget = targetPosition - pos;
            Vector3 moveDir = Vector3.ProjectOnPlane(toTarget, up);

            if (moveDir.sqrMagnitude < 0.01f)
                moveDir = _transform.forward;
            else
                moveDir = moveDir.normalized;

            float checkDistance = _settings.ClimbStartDistance;
            bool shouldLog = _debugLogs && (_frameCount % LOG_EVERY_N_FRAMES == 0);

            // ==================== STEP 1: Check what's directly ahead ====================
            Vector3 forwardRayOrigin = pos + up * 0.1f;

            if (Physics.Raycast(forwardRayOrigin, moveDir, out RaycastHit forwardHit, checkDistance,
                _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
            {
                Vector3 hitNormal = forwardHit.normal.normalized;
                float angle = Vector3.Angle(up, hitNormal);

                if (_debugRays)
                    Debug.DrawRay(forwardRayOrigin, moveDir * forwardHit.distance, Color.yellow, 0.1f);

                if (shouldLog)
                    LogSurfaceHit("Detect STEP1 AHEAD-HIT", forwardHit, hitNormal, 
                        extra: $"angleToCurrent={angle:F1}° origin={V(forwardRayOrigin)} dir={V(moveDir)}");

                // Hit a significantly different surface directly ahead → transition to it
                if (angle > 30f)  // 30° threshold for tilted surfaces
                {
                    if (_debugLogs)
                        Debug.Log($"[Surface] SELECT TRANSITION (ahead): angle={angle:F1}° normal={V(hitNormal)}", _transform);

                    StartTransition(hitNormal, moveDir, $"surface ahead ({forwardHit.collider.name})");
                    return;
                }
            }
            else if (_debugRays)
            {
                Debug.DrawRay(forwardRayOrigin, moveDir * checkDistance, Color.gray, 0.1f);
            }

            // ==================== STEP 2: Check if current surface continues ====================
            Vector3 aheadPos = pos + moveDir * checkDistance;
            Vector3 surfaceCheckOrigin = aheadPos + up * 0.5f;

            if (Physics.Raycast(surfaceCheckOrigin, -up, out RaycastHit surfaceHit, 1.5f,
                _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
            {
                Vector3 surfaceNormal = surfaceHit.normal.normalized;
                float angle = Vector3.Angle(surfaceNormal, up);

                if (_debugRays)
                    Debug.DrawRay(surfaceCheckOrigin, -up * surfaceHit.distance, Color.cyan, 0.1f);

                if (shouldLog)
                    LogSurfaceHit("Detect STEP2 CONTINUE-CHECK", surfaceHit, surfaceNormal,
                        extra: $"angleToCurrent={angle:F1}° aheadPos={V(aheadPos)}");

                // Surface continues with similar orientation
                if (angle < 25f)  // Allow some tolerance for tilting surface
                {
                    return; // No transition needed
                }

                // Different surface orientation at ahead position
                if (_debugLogs)
                    Debug.Log($"[Surface] SELECT TRANSITION (ahead changed): angle={angle:F1}° normal={V(surfaceNormal)}", _transform);

                StartTransition(surfaceNormal, moveDir, "surface changed ahead");
                return;
            }

            if (_debugRays)
                Debug.DrawRay(surfaceCheckOrigin, -up * 1.5f, Color.red, 0.1f);

            // ==================== STEP 3: We're at an edge - find adjacent surface ====================
            if (shouldLog)
                Debug.Log($"[Surface] EDGE DETECTED at aheadPos={V(aheadPos)} - scanning for adjacent surface", _transform);

            Vector3 adjacentNormal = FindAdjacentSurfaceAtEdge(pos, moveDir, up, aheadPos, shouldLog);

            if (adjacentNormal != Vector3.zero && Vector3.Angle(adjacentNormal, up) > 30f)
            {
                if (_debugLogs)
                    Debug.Log($"[Surface] SELECT TRANSITION (edge): normal={V(adjacentNormal)}", _transform);

                StartTransition(adjacentNormal, moveDir, "edge transition");
                return;
            }

            if (shouldLog)
                Debug.Log($"[Surface] EDGE: No adjacent surface found (will keep trying)", _transform);
        }

        /// <summary>
        /// Find the adjacent surface at an edge.
        /// </summary>
        private Vector3 FindAdjacentSurfaceAtEdge(Vector3 pos, Vector3 moveDir, Vector3 currentUp, Vector3 edgePos, bool shouldLog)
        {
            Vector3 right = Vector3.Cross(currentUp, moveDir).normalized;
            if (right.sqrMagnitude < 0.01f)
                right = Vector3.Cross(currentUp, Vector3.forward).normalized;

            Vector3[] searchDirections = new Vector3[]
            {
                -currentUp,
                Vector3.down,
                Vector3.up,
                moveDir,
                -moveDir,
                right,
                -right,
                (moveDir + Vector3.down).normalized,
                (moveDir + Vector3.up).normalized,
                (-currentUp + moveDir).normalized,
            };

            foreach (var dir in searchDirections)
            {
                if (dir.sqrMagnitude < 0.01f)
                    continue;

                Vector3 origin = edgePos;
                float castDist = 3.0f;

                if (Physics.Raycast(origin, dir, out RaycastHit hit, castDist,
                    _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    Vector3 hitNormal = hit.normal.normalized;
                    float angle = Vector3.Angle(currentUp, hitNormal);

                    if (_debugRays)
                        Debug.DrawRay(origin, dir * hit.distance, Color.magenta, 0.3f);

                    if (shouldLog)
                        LogSurfaceHit("EdgeScan Ray", hit, hitNormal, 
                            extra: $"dir={V(dir)} angleToCurrent={angle:F1}°");

                    if (angle > 30f)
                    {
                        return hitNormal;
                    }
                }
                else if (_debugRays)
                {
                    Debug.DrawRay(origin, dir * castDist, Color.gray, 0.1f);
                }
            }

            // SphereCast fallback
            float sphereRadius = 0.4f;
            foreach (var dir in new[] { -currentUp, Vector3.down, moveDir })
            {
                if (dir.sqrMagnitude < 0.01f)
                    continue;

                if (Physics.SphereCast(edgePos, sphereRadius, dir, out RaycastHit hit, 2.5f,
                    _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    Vector3 hitNormal = hit.normal.normalized;
                    float angle = Vector3.Angle(currentUp, hitNormal);

                    if (angle > 30f)
                    {
                        if (shouldLog)
                            LogSurfaceHit("EdgeScan SphereCast", hit, hitNormal, 
                                extra: $"dir={V(dir)} angleToCurrent={angle:F1}°");
                        return hitNormal;
                    }
                }
            }

            return Vector3.zero;
        }

        private void StartTransition(Vector3 newNormal, Vector3 moveDirection, string reason)
        {
            newNormal = newNormal.normalized;

            if (newNormal.sqrMagnitude < 0.001f)
                return;

            // Only transition if the angle difference is significant
            if (Vector3.Angle(newNormal, _currentNormal) < 20f)
                return;

            _preTransitionPosition = _transform.position;
            _frozenPosition = _transform.position;

            _lockedStartNormal = _currentNormal;
            _targetNormal = newNormal;
            _isTransitioning = true;
            _transitionProgress = 0f;
            _lockedMoveDirection = moveDirection;

            if (_debugLogs)
                Debug.Log($"[Surface] ▶ START: {V(_currentNormal)} → {V(_targetNormal)} | {reason}", _transform);
        }

        private void SnapToSurface(Vector3 previousNormal)
        {
            Vector3 up = _currentNormal;

            Vector3[] origins = new Vector3[]
            {
                _preTransitionPosition + up * 1.0f,
                _preTransitionPosition - previousNormal * 0.5f + up * 0.5f,
                _transform.position + up * 1.5f,
            };

            foreach (var origin in origins)
            {
                if (Physics.Raycast(origin, -up, out RaycastHit hit, 3.0f,
                    _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    Vector3 newPos = hit.point + hit.normal * (_settings.BodyRadius * 0.1f);
                    _transform.position = newPos;
                    _isGrounded = true;

                    if (_debugLogs)
                        Debug.Log($"[Surface] Snapped to {hit.collider.name} at {V(newPos)}", _transform);

                    return;
                }
            }

            if (_debugLogs)
                Debug.LogWarning($"[Surface] SnapToSurface failed!", _transform);
        }

        private void StayGrounded(float deltaTime)
        {
            Vector3 pos = _transform.position;
            Vector3 up = _currentNormal;

            Vector3 rayOrigin = pos + up * 0.5f;
            float rayDist = _settings.StickDistance + 0.5f;

            if (Physics.Raycast(rayOrigin, -up, out RaycastHit hit, rayDist,
                _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
            {
                if (_debugRays)
                    Debug.DrawRay(rayOrigin, -up * hit.distance, Color.green, 0.05f);

                Vector3 groundNormal = hit.normal.normalized;
                
                // Smoothly align to surface normal (important for tilting surfaces!)
                float angle = Vector3.Angle(_transform.up, groundNormal);
                if (angle > 1f)
                {
                    // Continuously update current normal to match the tilting surface
                    _currentNormal = Vector3.Slerp(_currentNormal, groundNormal, deltaTime * _settings.AlignSpeed).normalized;
                    
                    Quaternion targetRot = Quaternion.FromToRotation(_transform.up, _currentNormal) * _transform.rotation;
                    _transform.rotation = Quaternion.Slerp(_transform.rotation, targetRot, deltaTime * _settings.AlignSpeed);
                }

                // Log when ground changes
                if (_debugLogs)
                {
                    bool colliderChanged = hit.collider != _lastGroundCollider;
                    bool normalChanged = _lastGroundNormal == Vector3.zero || Vector3.Angle(groundNormal, _lastGroundNormal) > 5.0f;

                    if (colliderChanged || normalChanged)
                    {
                        LogSurfaceHit("Ground USED", hit, groundNormal, 
                            extra: $"rayOrigin={V(rayOrigin)} dist={hit.distance:F2}");
                        _lastGroundCollider = hit.collider;
                        _lastGroundNormal = groundNormal;
                    }
                }

                // Stick to surface
                Vector3 targetPos = hit.point + hit.normal * (_settings.BodyRadius * 0.1f);
                float heightDiff = Vector3.Distance(pos, targetPos);
                if (heightDiff > 0.01f)
                {
                    float speed = heightDiff > 0.3f ? 20f : 10f;
                    _transform.position = Vector3.Lerp(pos, targetPos, deltaTime * speed);
                }

                _isGrounded = true;
            }
            else
            {
                if (_debugRays)
                    Debug.DrawRay(rayOrigin, -up * rayDist, Color.red, 0.05f);

                if (_debugLogs && (_frameCount % LOG_EVERY_N_FRAMES == 0))
                    Debug.Log($"[Surface] Ground LOST (origin={V(rayOrigin)} up={V(up)} dist={rayDist:F2})", _transform);

                _isGrounded = false;
            }
        }

        private void AlignToNormal(Vector3 normal)
        {
            if (normal.sqrMagnitude < 0.001f) return;

            normal = normal.normalized;

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

            if (Physics.Raycast(pos + up * 0.5f, -up, out RaycastHit hit, _settings.StickDistance + 0.5f,
                _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
            {
                _transform.position = hit.point + hit.normal * (_settings.BodyRadius * 0.1f);
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

            _currentNormal = _transform.up;
            _targetNormal = _currentNormal;
            _lockedStartNormal = _currentNormal;

            _lastGroundCollider = null;
            _lastGroundNormal = Vector3.zero;

            if (_debugLogs)
                Debug.Log($"[Surface] RESET: normal={V(_currentNormal)}", _transform);
        }

        private void LogSurfaceHit(string tag, RaycastHit hit, Vector3 normal, string extra = "")
        {
            if (!_debugLogs) return;

            string colName = hit.collider != null ? hit.collider.name : "<null>";
            string layerName = hit.collider != null ? LayerMask.LayerToName(hit.collider.gameObject.layer) : "<null>";
            string extraPart = string.IsNullOrEmpty(extra) ? "" : $" | {extra}";

            Debug.Log(
                $"[Surface] {tag}: col='{colName}' layer='{layerName}' pt={V(hit.point)} normal={V(normal)} d={hit.distance:F2}{extraPart}",
                _transform
            );
        }

        private string V(Vector3 v) => $"({v.x:F2},{v.y:F2},{v.z:F2})";
    }
}