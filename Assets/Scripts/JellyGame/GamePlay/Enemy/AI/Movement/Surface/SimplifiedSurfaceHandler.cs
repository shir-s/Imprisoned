// FILEPATH: Assets/Scripts/AI/Movement/Surface/SimplifiedSurfaceHandler.cs
using UnityEngine;

namespace JellyGame.GamePlay.Enemy.AI.Movement
{
    /// <summary>
    /// v22 - Fixed falling during transition:
    /// - Position is now FROZEN during transition (only rotation changes)
    /// - GroundPosition() returns the frozen position during transition
    /// - ShouldBlockMovement returns true during transition to stop locomotion
    /// - Position only updates at the end via ForceGroundToCurrentSurface()
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
        
        // Position management during transition
        private Vector3 _preTransitionPosition = Vector3.zero;
        private Vector3 _frozenPosition = Vector3.zero; // Position to maintain during transition

        private float _transitionCooldownTimer = 0f;
        private const float TRANSITION_COOLDOWN = 0.3f;
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
        
        /// <summary>
        /// Block movement during transitions to prevent falling/drifting.
        /// </summary>
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
            
            // IMPORTANT: Keep position frozen during transition!
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
                    Debug.Log($"[Surface] ✓ COMPLETE: now on {V(_currentNormal)} | preTransPos={V(_preTransitionPosition)}", _transform);
                }
                
                _wasTransitioning = false;
                
                // NOW we can update position to the correct surface
                ForceGroundToCurrentSurface(oldNormal);
                return;
            }

            if (!_wasTransitioning && _debugLogs)
            {
                Debug.Log($"[Surface] TRANSITIONING: {V(_lockedStartNormal)} → {V(_targetNormal)}", _transform);
                _wasTransitioning = true;
            }

            // Only rotate, don't move
            Vector3 interpolatedNormal = Vector3.Slerp(_lockedStartNormal, _targetNormal, _transitionProgress).normalized;
            AlignToNormalSmooth(interpolatedNormal);
        }

        private void ForceGroundToCurrentSurface(Vector3 previousNormal)
        {
            Vector3 pos = _transform.position;
            Vector3 up = _currentNormal;
            
            bool wasOnWall = Mathf.Abs(previousNormal.y) < 0.5f;
            bool nowOnFloor = up.y > 0.5f;
            
            RaycastHit bestHit = default;
            bool found = false;
            
            // CASE 1: Wall → Floor/Top transition
            if (wasOnWall && nowOnFloor)
            {
                // Use the pre-transition position to find the top surface
                Vector3 searchOrigin = _preTransitionPosition + Vector3.up * 2.0f;
                
                if (Physics.Raycast(searchOrigin, Vector3.down, out RaycastHit hit1, 4.0f, 
                    _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    Vector3 hitNormal = SnapToAxis(hit1.normal);
                    if (hitNormal.y > 0.5f)
                    {
                        bestHit = hit1;
                        found = true;
                        
                        if (_debugLogs)
                        {
                            Debug.Log($"[Surface] Wall→Top grounding: found surface at {V(hit1.point)}", _transform);
                        }
                        
                        if (_debugRays)
                            Debug.DrawRay(searchOrigin, Vector3.down * hit1.distance, Color.green, 1.0f);
                    }
                }
                
                // Fallback: try from slightly forward
                if (!found)
                {
                    Vector3 forwardDir = _lockedMoveDirection.sqrMagnitude > 0.01f ? _lockedMoveDirection : _transform.forward;
                    Vector3 searchOrigin2 = _preTransitionPosition + forwardDir * 0.5f + Vector3.up * 2.0f;
                    
                    if (Physics.Raycast(searchOrigin2, Vector3.down, out RaycastHit hit2, 4.0f, 
                        _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
                    {
                        Vector3 hitNormal = SnapToAxis(hit2.normal);
                        if (hitNormal.y > 0.5f)
                        {
                            bestHit = hit2;
                            found = true;
                            
                            if (_debugLogs)
                            {
                                Debug.Log($"[Surface] Wall→Top grounding (fallback): found surface at {V(hit2.point)}", _transform);
                            }
                        }
                    }
                }
            }
            
            // CASE 2: Standard grounding
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
                float dist = Vector3.Distance(pos, newPos);
                
                _transform.position = newPos;
                _isGrounded = true;
                
                if (_debugLogs)
                {
                    Debug.Log($"[Surface] Post-transition grounded at {V(newPos)} (moved {dist:F2})", _transform);
                }
            }
            else if (_debugLogs)
            {
                Debug.LogWarning($"[Surface] Post-transition grounding FAILED!", _transform);
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
            float movingUpward = Vector3.Dot(forward, Vector3.up);
            
            bool shouldLogDetection = _debugLogs && (_frameCount % LOG_DETECTION_EVERY_N_FRAMES == 0);
            if (shouldLogDetection)
            {
                Debug.Log($"[Surface] DETECT: pos={V(pos)} up={V(up)} fwd={V(forward)} onWall={onWall} movingUp={movingUpward:F2} distToTarget={distToTarget:F2}", _transform);
            }

            // RAY 1: Look for wall ahead
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
                    StartTransition(hitNormal, forward, $"wall ahead ({hit1.collider.name})");
                    return;
                }
            }

            // RAY 2: Check surface ahead
            Vector3 aheadPos = pos + forward * checkDist;
            bool surfaceContinues = CheckSurfaceExistsBelow(aheadPos, up, out RaycastHit hit2, out Vector3 hitNormal2);

            if (_debugRays)
            {
                Vector3 rayOrigin = aheadPos + up * 1.0f;
                Debug.DrawRay(rayOrigin, -up * 2.0f, surfaceContinues ? Color.cyan : Color.red);
            }

            if (surfaceContinues)
            {
                float angleDiff = Vector3.Angle(up, hitNormal2);
                
                if (angleDiff < 30f)
                {
                    _consecutiveEdgeFailures = 0;
                    return;
                }
                
                StartTransition(hitNormal2, forward, $"surface changes to {V(hitNormal2)}");
                return;
            }
            
            // Edge detection
            if (shouldLogDetection)
            {
                Debug.Log($"[Surface] EDGE DETECTED: No surface ahead. onWall={onWall} movingUp={movingUpward:F2}", _transform);
            }
            
            // Wall top detection
            if (onWall && movingUpward > 0.2f)
            {
                if (TryDetectWallTop(pos, forward, up, aheadPos, distToTarget, out Vector3 topNormal))
                {
                    StartTransition(topNormal, forward, "wall top");
                    _consecutiveEdgeFailures = 0;
                    return;
                }
                else if (shouldLogDetection)
                {
                    Debug.LogWarning($"[Surface] Wall top detection FAILED!", _transform);
                }
            }
            
            // Wall bottom detection
            if (onWall && movingUpward < -0.2f)
            {
                if (TryDetectFloorBelow(pos, forward, up, out Vector3 floorNormal))
                {
                    StartTransition(floorNormal, forward, "wall bottom → floor");
                    _consecutiveEdgeFailures = 0;
                    return;
                }
            }
            
            // Generic edge detection
            Vector3 nextSurface = FindNextSurface(pos, aheadPos, forward, up);
            
            if (nextSurface != Vector3.zero && nextSurface != up)
            {
                float angle = Vector3.Angle(up, nextSurface);
                if (angle > 30f)
                {
                    StartTransition(nextSurface, forward, $"edge → {V(nextSurface)}");
                    _consecutiveEdgeFailures = 0;
                    return;
                }
            }
            
            _consecutiveEdgeFailures++;
            
            if (_debugLogs && (_consecutiveEdgeFailures % LOG_EDGE_FAILURE_INTERVAL == 0))
            {
                Debug.LogWarning($"[Surface] EDGE FAILED x{_consecutiveEdgeFailures}", _transform);
            }
        }

        private bool CheckSurfaceExistsBelow(Vector3 position, Vector3 up, out RaycastHit hit, out Vector3 hitNormal)
        {
            hit = default;
            hitNormal = Vector3.zero;
            
            Vector3 rayOrigin = position + up * 1.0f;
            
            if (Physics.Raycast(rayOrigin, -up, out hit, 2.0f, 
                _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
            {
                hitNormal = SnapToAxis(hit.normal);
                return true;
            }
            
            return false;
        }

        private bool TryDetectWallTop(Vector3 pos, Vector3 forward, Vector3 up, Vector3 aheadPos, float distToTarget, out Vector3 topNormal)
        {
            topNormal = Vector3.zero;
            
            // Strategy 1
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
            
            // Strategy 2
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
            
            // Strategy 3
            Vector3 origin3 = pos + Vector3.up * 1.0f;
            if (Physics.Raycast(origin3, Vector3.down, out RaycastHit hit3, 2.0f, 
                _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
            {
                Vector3 normal3 = SnapToAxis(hit3.normal);
                if (normal3.y > 0.5f && Vector3.Angle(up, normal3) > 30f)
                {
                    topNormal = normal3;
                    return true;
                }
            }
            
            // Strategy 4: SphereCast
            Vector3 origin4 = aheadPos + Vector3.up * 1.5f;
            if (Physics.SphereCast(origin4, 0.3f, Vector3.down, out RaycastHit hit4, 2.0f, 
                _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
            {
                Vector3 normal4 = SnapToAxis(hit4.normal);
                if (normal4.y > 0.5f && Vector3.Angle(up, normal4) > 30f)
                {
                    topNormal = normal4;
                    return true;
                }
            }
            
            // Strategy 5: Diagonal
            Vector3 diagDir = (Vector3.up * 0.7f + forward * 0.3f).normalized;
            if (Physics.Raycast(pos, diagDir, out RaycastHit hit5, 3.0f, 
                _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
            {
                Vector3 aboveHit = hit5.point + Vector3.up * 0.5f;
                if (Physics.Raycast(aboveHit, Vector3.down, out RaycastHit topHit, 1.5f, 
                    _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    Vector3 normal5 = SnapToAxis(topHit.normal);
                    if (normal5.y > 0.5f && Vector3.Angle(up, normal5) > 30f)
                    {
                        topNormal = normal5;
                        return true;
                    }
                }
            }
            
            // Strategy 6: Target-based
            if (distToTarget < 3.0f)
            {
                Vector3 origin6 = new Vector3(aheadPos.x, pos.y + 2.0f, aheadPos.z);
                if (Physics.Raycast(origin6, Vector3.down, out RaycastHit hit6, 4.0f, 
                    _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    Vector3 normal6 = SnapToAxis(hit6.normal);
                    if (normal6.y > 0.5f && Vector3.Angle(up, normal6) > 30f)
                    {
                        topNormal = normal6;
                        return true;
                    }
                }
            }
            
            return false;
        }

        private bool TryDetectFloorBelow(Vector3 pos, Vector3 forward, Vector3 up, out Vector3 floorNormal)
        {
            floorNormal = Vector3.zero;
            
            if (Physics.Raycast(pos, Vector3.down, out RaycastHit hit, 3.0f, 
                _settings.SurfaceLayers, QueryTriggerInteraction.Ignore))
            {
                Vector3 normal = SnapToAxis(hit.normal);
                if (_debugRays) Debug.DrawRay(pos, Vector3.down * hit.distance, Color.green);
                if (normal.y > 0.5f && Vector3.Angle(up, normal) > 30f && hit.distance < 2.0f)
                {
                    floorNormal = normal;
                    return true;
                }
            }
            
            return false;
        }

        private Vector3 FindNextSurface(Vector3 currentPos, Vector3 aheadPos, Vector3 forward, Vector3 currentUp)
        {
            bool onHorizontalSurface = Mathf.Abs(currentUp.y) > 0.5f;
            
            if (!onHorizontalSurface)
            {
                float movingDown = Vector3.Dot(forward, Vector3.down);
                if (movingDown > 0.3f)
                {
                    if (TryCast(aheadPos + Vector3.up * 0.5f, Vector3.down, 3f, currentUp, out Vector3 n1))
                        return n1;
                }
                
                float movingUp = Vector3.Dot(forward, Vector3.up);
                if (movingUp > 0.3f)
                {
                    if (TryCast(aheadPos - Vector3.up * 0.5f, Vector3.up, 3f, currentUp, out Vector3 n2))
                        return n2;
                }
            }
            else
            {
                if (TryCast(aheadPos + Vector3.up * 0.5f, Vector3.down, 4f, currentUp, out Vector3 n1))
                    return n1;
                if (TryCast(aheadPos - Vector3.up * 0.5f, Vector3.up, 4f, currentUp, out Vector3 n2))
                    return n2;
            }

            Vector3 diagDown = (forward + Vector3.down).normalized;
            if (TryCast(aheadPos, diagDown, 3f, currentUp, out Vector3 n3))
                return n3;

            Vector3 diagUp = (forward + Vector3.up).normalized;
            if (TryCast(aheadPos, diagUp, 3f, currentUp, out Vector3 n4))
                return n4;

            if (TryCast(currentPos + currentUp * 0.3f, forward, 2f, currentUp, out Vector3 n5))
                return n5;

            return Vector3.zero;
        }

        private bool TryCast(Vector3 origin, Vector3 dir, float dist, Vector3 currentUp, out Vector3 snappedNormal)
        {
            snappedNormal = Vector3.zero;
            
            bool hit = Physics.Raycast(origin, dir, out RaycastHit hitInfo, dist, 
                _settings.SurfaceLayers, QueryTriggerInteraction.Ignore);

            if (_debugRays)
                Debug.DrawRay(origin, dir * dist, hit ? Color.green : Color.gray, 0.1f);

            if (hit)
            {
                snappedNormal = SnapToAxis(hitInfo.normal);
                float angle = Vector3.Angle(currentUp, snappedNormal);
                if (angle > 30f)
                    return true;
            }
            return false;
        }

        private void StartTransition(Vector3 newNormal, Vector3 moveDirection, string reason)
        {
            newNormal = SnapToAxis(newNormal);
            
            if (newNormal == _currentNormal || newNormal == Vector3.zero)
                return;

            // Save position BEFORE transition
            _preTransitionPosition = _transform.position;
            _frozenPosition = _transform.position; // Freeze at this position during transition
            
            _lockedStartNormal = _currentNormal;
            _targetNormal = newNormal;
            _isTransitioning = true;
            _transitionProgress = 0f;
            _lockedMoveDirection = moveDirection;

            if (_debugLogs)
            {
                Debug.Log($"[Surface] ▶ START: {V(_currentNormal)} → {V(_targetNormal)} | {reason} | frozenPos={V(_frozenPosition)}", _transform);
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

        /// <summary>
        /// Returns grounded position. During transition, returns the frozen position.
        /// </summary>
        public Vector3 GroundPosition(Vector3 position)
        {
            // During transition, don't try to ground - return frozen position
            if (_isTransitioning)
            {
                return _frozenPosition;
            }
            
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
            // Don't ground during transition
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
                {
                    Debug.Log($"[Surface] Height corrected by {heightChange:F2}", _transform);
                }
                
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
            {
                Debug.Log($"[Surface] RESET: normal={V(_currentNormal)}", _transform);
            }
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