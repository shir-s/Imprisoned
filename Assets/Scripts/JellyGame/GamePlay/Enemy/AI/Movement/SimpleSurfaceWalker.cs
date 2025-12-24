// FILEPATH: Assets/Scripts/AI/Movement/SimpleSurfaceWalker.cs
using UnityEngine;

namespace JellyGame.GamePlay.Enemy.AI.Movement
{
    /// <summary>
    /// Simple surface walker that handles ALL surface transitions uniformly.
    /// 
    /// Core concept:
    /// - Enemy walks on surfaces, always aligned to surface normal
    /// - When reaching an edge, detect the adjacent surface's normal
    /// - Smoothly rotate from current normal to new normal
    /// - NO special cases for floor/wall/ceiling - just "current surface" → "next surface"
    /// 
    /// How it works:
    /// 1. Cast ray downward (relative to current up) to stay on surface
    /// 2. Cast ray forward to detect edges/walls ahead
    /// 3. When edge detected, find the adjacent surface normal
    /// 4. Rotate transform.up toward that normal while continuing to move
    /// </summary>
    [DisallowMultipleComponent]
    public class SimpleSurfaceWalker : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float moveSpeed = 3f;
        [SerializeField] private float turnSpeed = 360f;

        [Header("Surface Detection")]
        [SerializeField] private LayerMask surfaceLayers;
        [SerializeField] private float groundCheckDistance = 1f;
        [SerializeField] private float edgeDetectionDistance = 0.8f;
        [SerializeField] private float bodyRadius = 0.3f;

        [Header("Transition")]
        [SerializeField] private float surfaceTransitionSpeed = 8f;
        
        [Header("Debug")]
        [SerializeField] private bool debugRays = true;
        [SerializeField] private bool debugLogs = false;

        // State
        private Vector3 _currentSurfaceNormal = Vector3.up;
        private Vector3 _targetSurfaceNormal = Vector3.up;
        private bool _isTransitioning = false;
        private Vector3? _destination;

        // Public API
        public bool IsTransitioning => _isTransitioning;
        public Vector3 CurrentSurfaceNormal => _currentSurfaceNormal;

        private void Start()
        {
            // Initialize to current orientation
            _currentSurfaceNormal = SnapToAxis(transform.up);
            _targetSurfaceNormal = _currentSurfaceNormal;
            AlignToNormal(_currentSurfaceNormal, instant: true);
        }

        public void SetDestination(Vector3 destination)
        {
            _destination = destination;
        }

        public void Stop()
        {
            _destination = null;
        }

        public bool HasReachedDestination(float threshold = 0.5f)
        {
            if (_destination == null) return true;
            return Vector3.Distance(transform.position, _destination.Value) < threshold;
        }

        private void Update()
        {
            if (_destination == null) return;

            float dt = Time.deltaTime;
            
            // 1. Check what's ahead and update target normal if needed
            UpdateSurfaceDetection();
            
            // 2. Rotate toward target normal
            UpdateRotation(dt);
            
            // 3. Move forward on current surface
            UpdateMovement(dt);
            
            // 4. Stick to surface
            StickToSurface();
        }

        /// <summary>
        /// Detect if we're approaching an edge or a wall, and find the next surface normal.
        /// </summary>
        private void UpdateSurfaceDetection()
        {
            Vector3 pos = transform.position;
            Vector3 forward = transform.forward;
            Vector3 up = transform.up;

            // Cast forward to detect walls/edges
            Vector3 forwardRayOrigin = pos + up * 0.1f;
            
            if (Physics.Raycast(forwardRayOrigin, forward, out RaycastHit forwardHit, 
                edgeDetectionDistance, surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                // Hit something ahead - this is a wall/obstacle
                Vector3 hitNormal = SnapToAxis(forwardHit.normal);
                
                // Check if this is a different surface (not the one we're on)
                float angleDiff = Vector3.Angle(up, hitNormal);
                if (angleDiff > 10f)
                {
                    // New surface detected! Transition to it
                    SetTargetNormal(hitNormal, "wall ahead");
                }

                if (debugRays)
                    Debug.DrawLine(forwardRayOrigin, forwardHit.point, Color.yellow);
            }
            else
            {
                // Nothing ahead - check if we're approaching an edge (dropoff)
                Vector3 edgeCheckPos = pos + forward * edgeDetectionDistance;
                Vector3 edgeRayOrigin = edgeCheckPos + up * 0.5f;
                
                if (!Physics.Raycast(edgeRayOrigin, -up, groundCheckDistance + 0.5f, 
                    surfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    // No ground ahead - we're at an edge!
                    // Find the surface below/around the edge
                    Vector3 newNormal = FindSurfaceAtEdge(pos, forward, up);
                    if (newNormal != Vector3.zero)
                    {
                        SetTargetNormal(newNormal, "edge dropoff");
                    }
                }

                if (debugRays)
                    Debug.DrawRay(edgeRayOrigin, -up * (groundCheckDistance + 0.5f), Color.cyan);
            }

            // Also check the surface directly below to ensure we stay aligned
            if (!_isTransitioning)
            {
                if (Physics.Raycast(pos + up * 0.2f, -up, out RaycastHit groundHit, 
                    groundCheckDistance, surfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    Vector3 groundNormal = SnapToAxis(groundHit.normal);
                    if (Vector3.Angle(up, groundNormal) > 5f)
                    {
                        SetTargetNormal(groundNormal, "ground realign");
                    }
                }
            }
        }

        /// <summary>
        /// Find the surface normal at an edge (when walking off a ledge onto another surface).
        /// </summary>
        private Vector3 FindSurfaceAtEdge(Vector3 currentPos, Vector3 forward, Vector3 currentUp)
        {
            // Cast rays in multiple directions to find the adjacent surface
            Vector3 edgePoint = currentPos + forward * edgeDetectionDistance;
            
            // Try casting "down and forward" (wrapping around edge)
            Vector3 wrapDir = (forward - currentUp).normalized;
            if (Physics.Raycast(edgePoint, wrapDir, out RaycastHit hit, 2f, 
                surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                if (debugRays)
                    Debug.DrawLine(edgePoint, hit.point, Color.green, 0.5f);
                return SnapToAxis(hit.normal);
            }

            // Try casting straight down from edge
            if (Physics.Raycast(edgePoint + currentUp * 0.5f, -currentUp, out hit, 3f, 
                surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                // Check if this is a wall (perpendicular to our current surface)
                Vector3 snapped = SnapToAxis(hit.normal);
                float angle = Vector3.Angle(currentUp, snapped);
                if (angle > 45f) // It's a wall or ceiling, not just floor continuation
                {
                    if (debugRays)
                        Debug.DrawLine(edgePoint + currentUp * 0.5f, hit.point, Color.magenta, 0.5f);
                    return snapped;
                }
            }

            // Try casting in the direction of -forward (the wall we're about to go down)
            if (Physics.Raycast(edgePoint, -forward, out hit, 2f, 
                surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                Vector3 snapped = SnapToAxis(hit.normal);
                if (Vector3.Angle(currentUp, snapped) > 45f)
                {
                    if (debugRays)
                        Debug.DrawLine(edgePoint, hit.point, Color.blue, 0.5f);
                    return snapped;
                }
            }

            return Vector3.zero;
        }

        private void SetTargetNormal(Vector3 normal, string reason)
        {
            normal = SnapToAxis(normal);
            
            if (normal == _targetSurfaceNormal)
                return;
            
            if (normal == Vector3.zero)
                return;

            _targetSurfaceNormal = normal;
            _isTransitioning = true;

            if (debugLogs)
            {
                Debug.Log($"[SurfaceWalker] Target normal changed: {_currentSurfaceNormal} → {_targetSurfaceNormal} (reason: {reason})", this);
            }
        }

        /// <summary>
        /// Smoothly rotate transform.up toward target normal.
        /// </summary>
        private void UpdateRotation(float dt)
        {
            if (!_isTransitioning)
                return;

            Vector3 currentUp = transform.up;
            float angle = Vector3.Angle(currentUp, _targetSurfaceNormal);

            if (angle < 1f)
            {
                // Close enough - snap and finish transition
                AlignToNormal(_targetSurfaceNormal, instant: true);
                _currentSurfaceNormal = _targetSurfaceNormal;
                _isTransitioning = false;
                
                if (debugLogs)
                    Debug.Log($"[SurfaceWalker] Transition complete. Now on surface: {_currentSurfaceNormal}", this);
                return;
            }

            // Rotate toward target
            float rotationThisFrame = surfaceTransitionSpeed * dt * 90f; // degrees
            
            Quaternion fromTo = Quaternion.FromToRotation(currentUp, _targetSurfaceNormal);
            Quaternion targetRotation = fromTo * transform.rotation;
            
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationThisFrame);
        }

        /// <summary>
        /// Move toward destination.
        /// </summary>
        private void UpdateMovement(float dt)
        {
            if (_destination == null)
                return;

            Vector3 toTarget = _destination.Value - transform.position;
            Vector3 up = transform.up;
            
            // Project direction onto current surface plane
            Vector3 moveDir = Vector3.ProjectOnPlane(toTarget, up);
            
            if (moveDir.sqrMagnitude < 0.001f)
                return;

            moveDir.Normalize();

            // Rotate to face movement direction
            Quaternion targetFacing = Quaternion.LookRotation(moveDir, up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetFacing, turnSpeed * dt);

            // Move forward
            float speed = moveSpeed;
            if (_isTransitioning)
                speed *= 0.5f; // Slow down during transitions

            Vector3 movement = transform.forward * speed * dt;
            transform.position += movement;
        }

        /// <summary>
        /// Keep the enemy stuck to the surface.
        /// </summary>
        private void StickToSurface()
        {
            Vector3 pos = transform.position;
            Vector3 up = transform.up;

            // Cast down from slightly above current position
            Vector3 rayOrigin = pos + up * 0.3f;
            
            if (Physics.Raycast(rayOrigin, -up, out RaycastHit hit, groundCheckDistance + 0.3f, 
                surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                // Offset from surface
                Vector3 targetPos = hit.point + up * (bodyRadius * 0.1f);
                transform.position = targetPos;

                if (debugRays)
                    Debug.DrawLine(rayOrigin, hit.point, Color.green);
            }
            else if (debugRays)
            {
                Debug.DrawRay(rayOrigin, -up * (groundCheckDistance + 0.3f), Color.red);
            }
        }

        /// <summary>
        /// Align transform so that transform.up = normal, preserving forward direction as much as possible.
        /// </summary>
        private void AlignToNormal(Vector3 normal, bool instant)
        {
            if (normal == Vector3.zero)
                return;

            Vector3 forward = transform.forward;
            Vector3 projectedForward = Vector3.ProjectOnPlane(forward, normal);
            
            if (projectedForward.sqrMagnitude < 0.001f)
            {
                // Forward is parallel to normal, pick an arbitrary perpendicular
                projectedForward = Vector3.ProjectOnPlane(Vector3.forward, normal);
                if (projectedForward.sqrMagnitude < 0.001f)
                    projectedForward = Vector3.ProjectOnPlane(Vector3.right, normal);
            }

            projectedForward.Normalize();
            transform.rotation = Quaternion.LookRotation(projectedForward, normal);
        }

        /// <summary>
        /// Snap a normal vector to the nearest axis (±X, ±Y, or ±Z).
        /// This ensures the enemy is always on a clean axis-aligned surface.
        /// </summary>
        private Vector3 SnapToAxis(Vector3 normal)
        {
            if (normal.sqrMagnitude < 0.001f)
                return Vector3.zero;

            float absX = Mathf.Abs(normal.x);
            float absY = Mathf.Abs(normal.y);
            float absZ = Mathf.Abs(normal.z);

            if (absX >= absY && absX >= absZ)
                return new Vector3(Mathf.Sign(normal.x), 0, 0);
            
            if (absY >= absX && absY >= absZ)
                return new Vector3(0, Mathf.Sign(normal.y), 0);
            
            return new Vector3(0, 0, Mathf.Sign(normal.z));
        }

        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying)
                return;

            // Draw current surface normal
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(transform.position, _currentSurfaceNormal * 1.5f);

            // Draw target surface normal (if different)
            if (_isTransitioning)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawRay(transform.position, _targetSurfaceNormal * 1.5f);
            }

            // Draw destination
            if (_destination != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(_destination.Value, 0.3f);
                Gizmos.DrawLine(transform.position, _destination.Value);
            }
        }
    }
}