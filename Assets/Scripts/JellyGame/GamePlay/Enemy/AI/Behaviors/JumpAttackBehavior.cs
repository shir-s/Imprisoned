// FILEPATH: Assets/Scripts/AI/Behaviors/JumpAttackBehavior.cs
using System;
using UnityEngine;
using JellyGame.GamePlay.Combat;
using JellyGame.GamePlay.Managers;

namespace JellyGame.GamePlay.Enemy.AI.Behaviors
{
    /// <summary>
    /// Attack behavior that jumps on targets that have been stationary for a configurable duration.
    /// 
    /// Features:
    /// - Detects targets using the same layer priority system as AttackBehavior
    /// - Tracks target movement to detect when they stop moving
    /// - Performs an arc jump attack when target is stationary long enough
    /// - Deals damage on landing
    /// - Works on any surface (floor, wall, ceiling)
    /// - Has cooldown between jump attacks
    /// </summary>
    [DisallowMultipleComponent]
    public class JumpAttackBehavior : MonoBehaviour, IEnemyBehavior
    {
        #region Nested Types

        [Serializable]
        public struct TargetLayerPriority
        {
            public SingleLayer layer;
            public int attackPriority;
            public float customDetectionRadius;
        }

        [Serializable]
        public struct SingleLayer
        {
            [SerializeField] private int layerIndex;
            public int LayerIndex => layerIndex;
            public int LayerMask => layerIndex >= 0 ? (1 << layerIndex) : 0;
            public static implicit operator int(SingleLayer layer) => layer.layerIndex;
        }

        private enum JumpState
        {
            None,
            Jumping,
            PostLanding
        }

        #endregion

        #region Serialized Fields

        [Header("Behavior Priority")]
        [Tooltip("Should be higher than regular AttackBehavior to take precedence.")]
        [SerializeField] private int priority = 25;

        [Header("Target Detection")]
        [SerializeField] private TargetLayerPriority[] targetPriorities;
        [SerializeField] private float detectionRadius = 15f;

        [Header("Jump Distance")]
        [Tooltip("Minimum distance to target for jump attack (won't jump if too close).")]
        [SerializeField] private float minJumpDistance = 2f;
        [Tooltip("Maximum distance to target for jump attack (won't jump if too far).")]
        [SerializeField] private float maxJumpDistance = 8f;

        [Header("Stationary Detection")]
        [Tooltip("How long the target must be stationary before we jump on them.")]
        [SerializeField] private float stationaryTimeRequired = 1.5f;
        [Tooltip("Movement threshold - target moving less than this per second is considered stationary.")]
        [SerializeField] private float stationaryThreshold = 0.3f;

        [Header("Jump Settings")]
        [Tooltip("How long the jump takes in seconds.")]
        [SerializeField] private float jumpDuration = 0.5f;
        [Tooltip("Height of the jump arc.")]
        [SerializeField] private float jumpHeight = 2.5f;

        [Header("Damage")]
        [Tooltip("Damage dealt on landing.")]
        [SerializeField] private float landingDamage = 3f;

        [Header("Timing")]
        [Tooltip("How long to wait after landing before exiting behavior.")]
        [SerializeField] private float postLandingWait = 0.5f;
        [Tooltip("Cooldown after a jump attack before another can be performed.")]
        [SerializeField] private float jumpCooldown = 5f;

        [Header("Surface Detection")]
        [Tooltip("Layers considered as walkable surfaces.")]
        [SerializeField] private LayerMask surfaceLayers;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;
        [SerializeField] private bool debugGizmos = true;

        #endregion

        #region Private Fields

        // Target tracking
        private Transform _trackedTarget;
        private Collider _trackedTargetCollider;
        private int _trackedTargetPriority;
        private Vector3 _lastTrackedTargetPosition;
        private float _targetStationaryTime;

        // Current attack target
        private Transform _currentTarget;
        private Collider _currentTargetCollider;

        // Jump state
        private JumpState _jumpState = JumpState.None;
        private float _jumpProgress;
        private Vector3 _jumpStartPos;
        private Vector3 _jumpEndPos;
        private Vector3 _jumpStartUp;
        private Vector3 _jumpEndUp;
        private float _postLandingTimer;

        // Cooldown
        private float _lastJumpTime = -999f;

        #endregion

        #region Properties

        public int Priority => priority;

        /// <summary>
        /// True if currently in the middle of a jump attack
        /// </summary>
        public bool IsJumping => _jumpState == JumpState.Jumping;

        #endregion

        #region IEnemyBehavior Implementation

        public bool CanActivate()
        {
            // If we're currently jumping or in post-landing, keep the behavior active
            if (_jumpState != JumpState.None)
                return true;

            // Check cooldown (only when not actively jumping)
            if (Time.time - _lastJumpTime < jumpCooldown)
                return false;

            // Update target tracking (this runs every frame via BehaviorManager)
            UpdateTargetTracking();

            // Check if we have a valid stationary target in range
            if (_trackedTarget == null || _trackedTargetCollider == null)
                return false;

            // Check if target has been stationary long enough
            if (_targetStationaryTime < stationaryTimeRequired)
                return false;

            // Check distance
            float distance = Vector3.Distance(transform.position, _trackedTarget.position);
            if (distance < minJumpDistance || distance > maxJumpDistance)
                return false;

            // All conditions met!
            if (debugLogs)
                Debug.Log($"[JumpAttack] CanActivate TRUE - target {_trackedTarget.name} stationary for {_targetStationaryTime:F1}s, dist={distance:F1}", this);

            return true;
        }

        public void OnEnter()
        {
            if (debugLogs)
                Debug.Log($"[JumpAttack] OnEnter - jumping on {_trackedTarget?.name}", this);

            // Transfer tracked target to current target
            _currentTarget = _trackedTarget;
            _currentTargetCollider = _trackedTargetCollider;

            // Start the jump
            StartJump();
        }

        public void Tick(float deltaTime)
        {
            switch (_jumpState)
            {
                case JumpState.Jumping:
                    UpdateJump(deltaTime);
                    break;

                case JumpState.PostLanding:
                    UpdatePostLanding(deltaTime);
                    break;

                case JumpState.None:
                    // Shouldn't happen during active behavior, but handle gracefully
                    break;
            }
        }

        public void OnExit()
        {
            if (debugLogs)
                Debug.Log($"[JumpAttack] OnExit", this);

            _jumpState = JumpState.None;
            _currentTarget = null;
            _currentTargetCollider = null;

            // Reset tracking for the target we just attacked
            if (_trackedTarget == _currentTarget)
            {
                _targetStationaryTime = 0f;
            }
        }

        #endregion

        #region Target Tracking

        private void UpdateTargetTracking()
        {
            // Find the best target
            Transform bestTarget = null;
            Collider bestCollider = null;
            int bestPriority = int.MinValue;
            float bestDistSq = float.PositiveInfinity;

            if (targetPriorities == null || targetPriorities.Length == 0)
            {
                ClearTrackedTarget();
                return;
            }

            Vector3 origin = transform.position;

            foreach (var tp in targetPriorities)
            {
                if (tp.layer.LayerMask == 0)
                    continue;

                float radius = tp.customDetectionRadius > 0f ? tp.customDetectionRadius : detectionRadius;
                Collider[] hits = Physics.OverlapSphere(origin, radius, tp.layer.LayerMask, QueryTriggerInteraction.Ignore);

                foreach (Collider c in hits)
                {
                    if (c == null)
                        continue;

                    float dSq = (c.transform.position - origin).sqrMagnitude;
                    bool isBetter = false;

                    if (tp.attackPriority > bestPriority)
                        isBetter = true;
                    else if (tp.attackPriority == bestPriority && dSq < bestDistSq)
                        isBetter = true;

                    if (isBetter)
                    {
                        bestTarget = c.transform;
                        bestCollider = c;
                        bestPriority = tp.attackPriority;
                        bestDistSq = dSq;
                    }
                }
            }

            // Update tracked target
            if (bestTarget != null)
            {
                if (bestTarget != _trackedTarget)
                {
                    // New target - reset tracking
                    _trackedTarget = bestTarget;
                    _trackedTargetCollider = bestCollider;
                    _trackedTargetPriority = bestPriority;
                    _lastTrackedTargetPosition = bestTarget.position;
                    _targetStationaryTime = 0f;

                    if (debugLogs)
                        Debug.Log($"[JumpAttack] Now tracking new target: {bestTarget.name}", this);
                }
                else
                {
                    // Same target - update stationary tracking
                    UpdateStationaryTracking();
                }
            }
            else
            {
                ClearTrackedTarget();
            }
        }

        private void UpdateStationaryTracking()
        {
            if (_trackedTarget == null)
                return;

            Vector3 currentPos = _trackedTarget.position;
            float movement = Vector3.Distance(currentPos, _lastTrackedTargetPosition);
            float movementPerSecond = movement / Time.deltaTime;

            if (movementPerSecond < stationaryThreshold)
            {
                // Target is stationary
                _targetStationaryTime += Time.deltaTime;
            }
            else
            {
                // Target is moving - reset timer
                _targetStationaryTime = 0f;
            }

            _lastTrackedTargetPosition = currentPos;
        }

        private void ClearTrackedTarget()
        {
            _trackedTarget = null;
            _trackedTargetCollider = null;
            _trackedTargetPriority = int.MinValue;
            _targetStationaryTime = 0f;
        }

        #endregion

        #region Jump Logic

        private void StartJump()
        {
            if (_currentTarget == null)
            {
                _jumpState = JumpState.None;
                return;
            }

            _jumpState = JumpState.Jumping;
            _jumpProgress = 0f;
            _lastJumpTime = Time.time;

            // Store start position and up direction
            _jumpStartPos = transform.position;
            _jumpStartUp = transform.up;

            // Calculate end position - aim for the target's position
            Vector3 targetPos = _currentTarget.position;

            // Try to find the surface at the target location
            if (TryGetSurfaceAtPosition(targetPos, out Vector3 surfacePoint, out Vector3 surfaceNormal))
            {
                _jumpEndPos = surfacePoint;
                _jumpEndUp = surfaceNormal;
            }
            else
            {
                // Fallback - assume same surface orientation
                _jumpEndPos = targetPos;
                _jumpEndUp = _jumpStartUp;
            }

            if (debugLogs)
            {
                float dist = Vector3.Distance(_jumpStartPos, _jumpEndPos);
                Debug.Log($"[JumpAttack] Jump started: {V(_jumpStartPos)} -> {V(_jumpEndPos)} (dist={dist:F1})", this);
            }
        }

        private void UpdateJump(float deltaTime)
        {
            _jumpProgress += deltaTime / Mathf.Max(0.01f, jumpDuration);

            float t = Mathf.Clamp01(_jumpProgress);

            // Position interpolation
            Vector3 pos = Vector3.Lerp(_jumpStartPos, _jumpEndPos, t);

            // Arc height using sine curve
            float arc = Mathf.Sin(t * Mathf.PI) * jumpHeight;

            // Interpolate up direction for the arc
            Vector3 arcUp = Vector3.Slerp(_jumpStartUp, _jumpEndUp, t).normalized;

            pos += arcUp * arc;

            // Update rotation during jump
            UpdateJumpRotation(t, arcUp);

            transform.position = pos;

            // Check if jump is complete
            if (t >= 1f)
            {
                OnJumpLanded();
            }
        }

        private void UpdateJumpRotation(float t, Vector3 currentUp)
        {
            // Face toward the landing point
            Vector3 toEnd = _jumpEndPos - transform.position;
            Vector3 planarToEnd = Vector3.ProjectOnPlane(toEnd, currentUp);

            if (planarToEnd.sqrMagnitude > 0.001f)
            {
                planarToEnd.Normalize();
                Quaternion targetRot = Quaternion.LookRotation(planarToEnd, currentUp);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, t);
            }
            else
            {
                // Just align up direction
                Vector3 forward = Vector3.ProjectOnPlane(transform.forward, currentUp);
                if (forward.sqrMagnitude < 0.001f)
                    forward = Vector3.ProjectOnPlane(Vector3.forward, currentUp);
                if (forward.sqrMagnitude > 0.001f)
                {
                    forward.Normalize();
                    transform.rotation = Quaternion.LookRotation(forward, currentUp);
                }
            }
        }

        private void OnJumpLanded()
        {
            if (debugLogs)
                Debug.Log($"[JumpAttack] Landed at {V(transform.position)}", this);

            // Ground to surface
            GroundToSurface();

            // Deal damage
            DealLandingDamage();

            // Start post-landing wait
            _jumpState = JumpState.PostLanding;
            _postLandingTimer = postLandingWait;
        }

        private void GroundToSurface()
        {
            Vector3 up = _jumpEndUp;
            Vector3 pos = transform.position;

            // Raycast to find exact surface position
            Vector3 rayOrigin = pos + up * 0.5f;

            if (Physics.Raycast(rayOrigin, -up, out RaycastHit hit, 2f, surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                transform.position = hit.point + hit.normal * 0.05f;

                // Align to surface
                Vector3 forward = Vector3.ProjectOnPlane(transform.forward, hit.normal);
                if (forward.sqrMagnitude < 0.001f)
                    forward = Vector3.ProjectOnPlane(Vector3.forward, hit.normal);
                if (forward.sqrMagnitude > 0.001f)
                {
                    forward.Normalize();
                    transform.rotation = Quaternion.LookRotation(forward, hit.normal);
                }

                if (debugLogs)
                    Debug.Log($"[JumpAttack] Grounded to {hit.collider.name} at {V(transform.position)}", this);
            }
            else
            {
                // Try world down as fallback
                rayOrigin = pos + Vector3.up * 0.5f;
                if (Physics.Raycast(rayOrigin, Vector3.down, out hit, 2f, surfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    transform.position = hit.point + hit.normal * 0.05f;

                    Vector3 forward = Vector3.ProjectOnPlane(transform.forward, hit.normal);
                    if (forward.sqrMagnitude > 0.001f)
                    {
                        forward.Normalize();
                        transform.rotation = Quaternion.LookRotation(forward, hit.normal);
                    }
                }

                if (debugLogs)
                    Debug.LogWarning($"[JumpAttack] Grounding with target up failed, used world down", this);
            }
        }

        private void DealLandingDamage()
        {
            if (_currentTarget == null)
                return;

            // Check if still close enough to deal damage
            float dist = Vector3.Distance(transform.position, _currentTarget.position);
            if (dist > 2f) // Landing tolerance
            {
                if (debugLogs)
                    Debug.Log($"[JumpAttack] Target too far on landing (dist={dist:F1}), no damage", this);
                return;
            }

            IDamageable damageable = _currentTarget.GetComponentInParent<IDamageable>();
            if (damageable != null)
            {
                damageable.ApplyDamage(landingDamage);

                if (debugLogs)
                    Debug.Log($"[JumpAttack] Dealt {landingDamage} damage to {_currentTarget.name}", this);

                // Trigger event
                EventManager.TriggerEvent(EventManager.GameEvent.PlayerDamaged, damageable);
            }
            else
            {
                if (debugLogs)
                    Debug.Log($"[JumpAttack] Target {_currentTarget.name} has no IDamageable", this);
            }
        }

        private void UpdatePostLanding(float deltaTime)
        {
            _postLandingTimer -= deltaTime;

            if (_postLandingTimer <= 0f)
            {
                // Done with behavior - this will cause BehaviorManager to exit us
                // and pick another behavior (like WaypointRouteBehavior)
                _jumpState = JumpState.None;

                if (debugLogs)
                    Debug.Log($"[JumpAttack] Post-landing wait complete, behavior ending", this);
            }
        }

        #endregion

        #region Surface Detection

        private bool TryGetSurfaceAtPosition(Vector3 position, out Vector3 surfacePoint, out Vector3 surfaceNormal)
        {
            surfacePoint = position;
            surfaceNormal = Vector3.up;

            // Try casting down from above the position
            Vector3 checkOrigin = position + Vector3.up * 2f;

            if (Physics.Raycast(checkOrigin, Vector3.down, out RaycastHit hit, 4f, surfaceLayers, QueryTriggerInteraction.Ignore))
            {
                surfacePoint = hit.point + hit.normal * 0.05f;
                surfaceNormal = hit.normal.normalized;
                return true;
            }

            // Try multiple directions for walls/ceilings
            Vector3[] checkDirs = new Vector3[]
            {
                Vector3.up,
                Vector3.left,
                Vector3.right,
                Vector3.forward,
                Vector3.back
            };

            foreach (var dir in checkDirs)
            {
                checkOrigin = position + dir * 1.5f;
                if (Physics.Raycast(checkOrigin, -dir, out hit, 3f, surfaceLayers, QueryTriggerInteraction.Ignore))
                {
                    surfacePoint = hit.point + hit.normal * 0.05f;
                    surfaceNormal = hit.normal.normalized;
                    return true;
                }
            }

            return false;
        }

        #endregion

        #region Gizmos

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!debugGizmos)
                return;

            // Detection radius
            Gizmos.color = new Color(0.5f, 0f, 1f, 0.15f);
            Gizmos.DrawWireSphere(transform.position, detectionRadius);

            // Min jump distance
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, minJumpDistance);

            // Max jump distance
            Gizmos.color = new Color(0f, 1f, 0.5f, 0.2f);
            Gizmos.DrawWireSphere(transform.position, maxJumpDistance);

            if (!Application.isPlaying)
                return;

            // Tracked target
            if (_trackedTarget != null)
            {
                float fillRatio = Mathf.Clamp01(_targetStationaryTime / stationaryTimeRequired);
                Gizmos.color = Color.Lerp(Color.yellow, Color.red, fillRatio);
                Gizmos.DrawLine(transform.position, _trackedTarget.position);
                Gizmos.DrawWireSphere(_trackedTarget.position, 0.5f);

                // Show stationary progress
                if (_targetStationaryTime > 0f)
                {
                    Vector3 progressPos = _trackedTarget.position + Vector3.up * 1.5f;
                    Gizmos.color = Color.red;
                    Gizmos.DrawCube(progressPos, new Vector3(fillRatio * 2f, 0.2f, 0.2f));
                }
            }

            // Current jump
            if (_jumpState == JumpState.Jumping)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawLine(_jumpStartPos, _jumpEndPos);
                Gizmos.DrawWireSphere(_jumpEndPos, 0.3f);
            }
        }
#endif

        #endregion

        #region Helpers

        private string V(Vector3 v) => $"({v.x:F2},{v.y:F2},{v.z:F2})";

        #endregion
    }
}