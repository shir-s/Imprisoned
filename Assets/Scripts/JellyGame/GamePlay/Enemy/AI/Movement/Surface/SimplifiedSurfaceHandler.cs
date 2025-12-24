// FILEPATH: Assets/Scripts/AI/Movement/Surface/SimplifiedSurfaceHandler.cs
using UnityEngine;

namespace JellyGame.GamePlay.Enemy.AI.Movement
{
    /// <summary>
    /// v13 - FIXED edge detection
    /// 
    /// Problem identified: RAY2 was casting from aheadPos+up toward -up.
    /// On walls, up=(0,0,-1), so -up=(0,0,1) which casts INTO the wall.
    /// This always hits the wall - edge never detected!
    /// 
    /// Solution: Check if current surface continues by casting ALONG the wall
    /// in the movement direction. If we don't hit wall below us, we've reached the edge.
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

        private float _transitionCooldownTimer = 0f;
        private const float TRANSITION_COOLDOWN = 0.5f;
        private const float TRANSITION_DURATION = 0.35f;

        // Throttled logging
        private float _lastLogTime = -999f;
        private const float LOG_INTERVAL = 0.3f;

        #region Interface Properties
        public Vector3 CurrentUp => _transform.up;
        public bool IsGrounded => _isGrounded;
        public bool IsInTransition => _isTransitioning;
        public ClimbTransitionState TransitionState => _isTransitioning ? ClimbTransitionState.Climbing : ClimbTransitionState.None;
        public Vector3 TransitionMoveDirection => _lockedMoveDirection;
        public bool ShouldBlockMovement => false;
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

        private bool ShouldLog() => _debugLogs && (Time.time - _lastLogTime > LOG_INTERVAL);
        private void MarkLogged() => _lastLogTime = Time.time;

        public void UpdateSurface(Vector3 targetPosition, float deltaTime)
        {
            if (_transitionCooldownTimer > 0f)
            {
                _transitionCooldownTimer -= deltaTime;
                return;
            }

            if (_isTransitioning)
            {
                ContinueTransition(deltaTime);
            }
            else
            {
                DetectUpcomingSurface(targetPosition);
                StayAlignedToSurface(deltaTime);
            }
        }

        private void ContinueTransition(float deltaTime)
        {
            _transitionProgress += deltaTime / TRANSITION_DURATION;

            if (_transitionProgress >= 1f)
            {
                _transitionProgress = 1f;
                AlignToNormalInstant(_targetNormal);
                _currentNormal = _targetNormal;
                _isTransitioning = false;
                _lockedMoveDirection = Vector3.zero;
                _transitionCooldownTimer = TRANSITION_COOLDOWN;

                Debug.Log($"[Surface] ✓ COMPLETE: now on {V(_currentNormal)}", _transform);
                return;
            }

            Vector3 interpolatedNormal = Vector3.Slerp(_lockedStartNormal, _targetNormal, _transitionProgress).normalized;
            AlignToNormalSmooth(interpolatedNormal);
        }

        private void DetectUpcomingSurface(Vector3 targetPosition)
        {
            Vector3 pos = _transform.position;
            Vector3 up = _currentNormal;
            
            Vector3 toTarget = targetPosition - pos;
            Vector3 forward = Vector3.ProjectOnPlane(toTarget, up);
            
            if (forward.sqrMagnitude < 0.01f)
                forward = _transform.forward;
            else
                forward = forward.normalized;

            float checkDist = _settings.ClimbStartDistance;

            // ========== RAY 1: Look for WALL/OBSTACLE directly ahead ==========
            Vector3 ray1Origin = pos + up * 0.15f;
            bool ray1Hit = Physics.Raycast(ray1Origin, forward, out RaycastHit hit1, checkDist, 
                _settings.SurfaceLayers, QueryTriggerInteraction.Ignore);

            if (_debugRays)
                Debug.DrawRay(ray1Origin, forward * checkDist, ray1Hit ? Color.yellow : Color.gray);

            if (ray1Hit)
            {
                Vector3 hitNormal = SnapToAxis(hit1.normal);
                float angle = Vector3.Angle(up, hitNormal);
                
                if (angle > 30f)
                {
                    StartTransition(hitNormal, forward, $"wall ahead ({hit1.collider.name}, angle={angle:F0}°)");
                    return;
                }
            }

            // ========== EDGE DETECTION: Does surface continue in movement direction? ==========
            // The key insight: we need to check if the WALL continues below us,
            // not if something is in the -up direction (which on a wall points INTO the wall)
            
            // Cast from a point ahead (in movement direction) BACK toward the current surface
            Vector3 aheadPos = pos + forward * checkDist;
            
            // From ahead, cast toward the surface we're standing on
            // This checks if the surface continues at the ahead position
            Vector3 surfaceCheckOrigin = aheadPos + up * 0.5f; // Move away from surface
            Vector3 surfaceCheckDir = -up; // Cast back toward surface
            float surfaceCheckDist = 1.5f;
            
            bool surfaceContinues = Physics.Raycast(surfaceCheckOrigin, surfaceCheckDir, out RaycastHit surfaceHit, 
                surfaceCheckDist, _settings.SurfaceLayers, QueryTriggerInteraction.Ignore);

            if (_debugRays)
                Debug.DrawRay(surfaceCheckOrigin, surfaceCheckDir * surfaceCheckDist, surfaceContinues ? Color.cyan : Color.red);

            if (surfaceContinues)
            {
                Vector3 hitNormal = SnapToAxis(surfaceHit.normal);
                float angleDiff = Vector3.Angle(up, hitNormal);
                
                // Check if this is SAME surface or DIFFERENT surface
                if (angleDiff < 30f)
                {
                    // Same surface continues - but wait!
                    // We need to ALSO check if we're near an edge by looking for floor/ceiling
                    
                    // Additional check: is there a floor below us?
                    Vector3 floorCheckOrigin = pos + forward * (checkDist * 0.5f);
                    bool foundFloor = Physics.Raycast(floorCheckOrigin, Vector3.down, out RaycastHit floorHit, 
                        3f, _settings.SurfaceLayers, QueryTriggerInteraction.Ignore);
                    
                    if (_debugRays)
                        Debug.DrawRay(floorCheckOrigin, Vector3.down * 3f, foundFloor ? Color.green : Color.gray);
                    
                    if (foundFloor)
                    {
                        Vector3 floorNormal = SnapToAxis(floorHit.normal);
                        float floorAngle = Vector3.Angle(up, floorNormal);
                        
                        // If floor normal is different from current surface AND we're close to it
                        if (floorAngle > 30f && floorHit.distance < 1.5f)
                        {
                            if (ShouldLog())
                            {
                                Debug.Log($"[Surface] Floor detected below! dist={floorHit.distance:F2} normal={V(floorNormal)}", _transform);
                                MarkLogged();
                            }
                            StartTransition(floorNormal, forward, $"floor below ({floorHit.collider.name}, dist={floorHit.distance:F1})");
                            return;
                        }
                    }
                    
                    // No transition needed
                    return;
                }
                else
                {
                    // Surface ahead has different normal - transition to it
                    StartTransition(hitNormal, forward, $"surface changes ahead ({surfaceHit.collider.name})");
                    return;
                }
            }
            
            // ========== Surface doesn't continue - we're at an edge! ==========
            if (ShouldLog())
            {
                Debug.Log($"[Surface] EDGE DETECTED - surface ends! Searching for next surface...", _transform);
                MarkLogged();
            }

            Vector3 nextSurface = FindNextSurface(pos, aheadPos, forward, up);
            
            if (nextSurface != Vector3.zero)
            {
                float angle = Vector3.Angle(up, nextSurface);
                if (angle > 30f)
                {
                    StartTransition(nextSurface, forward, $"edge → {V(nextSurface)} (angle={angle:F0}°)");
                    return;
                }
            }
        }

        private Vector3 FindNextSurface(Vector3 currentPos, Vector3 aheadPos, Vector3 forward, Vector3 currentUp)
        {
            // Try world-space directions since local up/down are unreliable on walls

            // Try 1: World DOWN (most common - wall to floor)
            if (TryCast(aheadPos + Vector3.up * 0.5f, Vector3.down, 4f, "world DOWN", currentUp, out Vector3 n1))
                return n1;

            // Try 2: World UP (wall to ceiling)
            if (TryCast(aheadPos - Vector3.up * 0.5f, Vector3.up, 4f, "world UP", currentUp, out Vector3 n2))
                return n2;

            // Try 3: Diagonal forward+down
            Vector3 diagDown = (forward + Vector3.down).normalized;
            if (TryCast(aheadPos, diagDown, 3f, "diag DOWN", currentUp, out Vector3 n3))
                return n3;

            // Try 4: Diagonal forward+up
            Vector3 diagUp = (forward + Vector3.up).normalized;
            if (TryCast(aheadPos, diagUp, 3f, "diag UP", currentUp, out Vector3 n4))
                return n4;

            // Try 5: Current movement direction (forward)
            if (TryCast(currentPos + currentUp * 0.3f, forward, 2f, "forward", currentUp, out Vector3 n5))
                return n5;

            return Vector3.zero;
        }

        private bool TryCast(Vector3 origin, Vector3 dir, float dist, string label, Vector3 currentUp, out Vector3 snappedNormal)
        {
            snappedNormal = Vector3.zero;
            
            bool hit = Physics.Raycast(origin, dir, out RaycastHit hitInfo, dist, 
                _settings.SurfaceLayers, QueryTriggerInteraction.Ignore);

            if (_debugRays)
                Debug.DrawRay(origin, dir * dist, hit ? Color.green : Color.gray, 0.2f);

            if (hit)
            {
                snappedNormal = SnapToAxis(hitInfo.normal);
                float angle = Vector3.Angle(currentUp, snappedNormal);
                
                // Only return surfaces that are actually different
                if (angle > 30f)
                {
                    if (_debugLogs)
                        Debug.Log($"[Surface] {label}: FOUND {hitInfo.collider.name} normal={V(snappedNormal)} angle={angle:F0}°", _transform);
                    return true;
                }
            }
            return false;
        }

        private void StartTransition(Vector3 newNormal, Vector3 moveDirection, string reason)
        {
            newNormal = SnapToAxis(newNormal);
            
            if (newNormal == _currentNormal || newNormal == Vector3.zero)
                return;

            _lockedStartNormal = _currentNormal;
            _targetNormal = newNormal;
            _isTransitioning = true;
            _transitionProgress = 0f;
            _lockedMoveDirection = moveDirection;

            Debug.Log($"[Surface] ▶ START: {V(_currentNormal)} → {V(_targetNormal)} | {reason}", _transform);
        }

        private void StayAlignedToSurface(float deltaTime)
        {
            Vector3 pos = _transform.position;
            Vector3 up = _transform.up;

            if (Physics.Raycast(pos + up * 0.2f, -up, out RaycastHit hit, _settings.StickDistance, 
                _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
            {
                Vector3 groundNormal = SnapToAxis(hit.normal);
                float angle = Vector3.Angle(up, groundNormal);

                if (angle > 2f && angle < 20f)
                {
                    Quaternion correction = Quaternion.FromToRotation(up, groundNormal);
                    Quaternion targetRot = correction * _transform.rotation;
                    _transform.rotation = Quaternion.Slerp(_transform.rotation, targetRot, deltaTime * _settings.AlignSpeed);
                    _currentNormal = groundNormal;
                }
            }
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
            Vector3 up = _isTransitioning ? Vector3.Slerp(_lockedStartNormal, _targetNormal, _transitionProgress) : _currentNormal;
            Vector3 rayOrigin = position + up * _settings.StickDistance;
            if (Physics.Raycast(rayOrigin, -up, out RaycastHit hit, _settings.StickDistance * 2f, 
                _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
                return hit.point + hit.normal * (_settings.BodyRadius * 0.1f);
            return position;
        }

        public void EnsureGrounded()
        {
            if (_isTransitioning) return;
            Vector3 pos = _transform.position;
            Vector3 up = _currentNormal;
            if (Physics.Raycast(pos + up * 0.3f, -up, out RaycastHit hit, _settings.StickDistance + 0.3f, 
                _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
            {
                _transform.position = hit.point + up * (_settings.BodyRadius * 0.1f);
                _isGrounded = true;
            }
            else
                _isGrounded = false;
        }

        public void ResetTransition()
        {
            _isTransitioning = false;
            _lockedMoveDirection = Vector3.zero;
            _transitionProgress = 0f;
            _transitionCooldownTimer = 0f;
            _currentNormal = SnapToAxis(_transform.up);
            _targetNormal = _currentNormal;
            _lockedStartNormal = _currentNormal;
        }

        private Vector3 SnapToAxis(Vector3 v)
        {
            if (v.sqrMagnitude < 0.001f) return Vector3.zero;
            float ax = Mathf.Abs(v.x);
            float ay = Mathf.Abs(v.y);
            float az = Mathf.Abs(v.z);
            if (ax >= ay && ax >= az) return new Vector3(Mathf.Sign(v.x), 0f, 0f);
            if (ay >= ax && ay >= az) return new Vector3(0f, Mathf.Sign(v.y), 0f);
            return new Vector3(0f, 0f, Mathf.Sign(v.z));
        }

        private string V(Vector3 v) => $"({v.x:F2},{v.y:F2},{v.z:F2})";
    }
}