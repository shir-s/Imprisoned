// FILEPATH: Assets/Scripts/AI/Movement/SteeringNavigator.cs
//
// UPDATED VERSION - Uses enhanced HoppingLocomotion for proper surface transitions
// v4 Changes: Don't call EnsureGrounded for hopping locomotion (prevents drift on tilted surfaces)

using UnityEngine;

namespace JellyGame.GamePlay.Enemy.AI.Movement
{
    /// <summary>
    /// Orchestrates navigation for spider-like enemies.
    /// Uses SimplifiedSurfaceHandler for clean, unified surface transitions.
    /// 
    /// v2 Changes:
    /// - Passes destination to HoppingLocomotion for smarter hop planning
    /// - Handles transition hops that the locomotion performs
    /// 
    /// v3 Changes:
    /// - Passes debug flag to HoppingLocomotion for landing diagnostics
    /// - Added LastHopLandingDistance property for monitoring
    /// 
    /// v4 Changes:
    /// - FIXED: Don't call EnsureGrounded for hopping locomotion
    /// - Hopping locomotion handles its own grounding at landing time
    /// - Prevents drift/sliding on tilted surfaces between hops
    /// </summary>
    [DisallowMultipleComponent]
    public class SteeringNavigator : MonoBehaviour, ISpeedMultiplierSink
    {
        public enum LocomotionType
        {
            Continuous,
            Hopping
        }

        #region Serialized Fields

        [Header("Locomotion")]
        [SerializeField] private LocomotionType locomotionType = LocomotionType.Continuous;
        [SerializeField] private LocomotionSettings locomotionSettings = new LocomotionSettings();

        [Header("Obstacle Avoidance")]
        [SerializeField] private ObstacleAvoidanceSettings avoidanceSettings = new ObstacleAvoidanceSettings();

        [Header("Surface Alignment (Spider)")]
        [Tooltip("Enable wall/ceiling adhesion for spider-like behavior.")]
        [SerializeField] private bool enableSurfaceAlignment = false;
        [SerializeField] private SurfaceSettings surfaceSettings = new SurfaceSettings();

        [Header("Navigation")]
        [SerializeField] private float directApproachDistance = 1.5f;
        [SerializeField] private float steeringDisableDistance = 0.8f;
        [SerializeField] private float maxSteeringAngle = 45f;

        [Header("Debug")]
        [SerializeField] private bool drawSensorRays = false;
        [SerializeField] private bool drawBestDirection = true;
        [SerializeField] private bool drawSurfaceRays = false;
        [SerializeField] private bool debugLogs = false;
        [Tooltip("Enable detailed hop landing diagnostics (separate from general debug logs)")]
        [SerializeField] private bool debugHopLanding = false;

        #endregion

        #region Runtime Components

        private ILocomotion _locomotion;
        private HoppingLocomotion _hoppingLocomotion; // Cached reference for destination-aware hopping
        private IObstacleAvoidance _obstacleAvoidance;
        private SimplifiedSurfaceHandler _surfaceHandler;
        private Animator _animator;

        #endregion

        #region State

        private Vector3? _currentTarget;
        private bool _isStopped = true;
        private float _speedMultiplier = 1f;
        private Vector3 _debugBestDir;

        #endregion

        #region Public Properties

        public bool IsInClimbTransition => _surfaceHandler?.IsInTransition ?? false;
        
        /// <summary>
        /// True if the hopping locomotion is currently performing a surface-transition hop
        /// </summary>
        public bool IsInHopTransition => _hoppingLocomotion?.IsTransitionHop ?? false;
        
        public bool IsSurfaceAlignmentEnabled => enableSurfaceAlignment;
        public Vector3? CurrentTarget => _currentTarget;
        public bool IsStopped => _isStopped;
        
        /// <summary>
        /// Distance from surface at the last hop landing (for diagnostics).
        /// Only valid when using Hopping locomotion.
        /// </summary>
        public float LastHopLandingDistance => _hoppingLocomotion?.LastLandingDistance ?? 0f;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            InitializeComponents();
            // Look for Animator on this object or in children (for rigged models)
            _animator = GetComponentInChildren<Animator>();
            if (_animator == null)
            {
                _animator = GetComponent<Animator>();
            }
        }

        private void Update()
        { 
            // If stopped or no target, set to idle
            if (_isStopped || _currentTarget == null)
            {
                UpdateAnimationState(false);
                return;
            }

            float dt = Time.deltaTime;
            Vector3 target = _currentTarget.Value;

            // Check if we've reached the destination (for waypoint waiting)
            bool hasReachedDestination = HasReachedDestination(0.5f);

            // Update hopping locomotion with current destination
            if (_hoppingLocomotion != null)
            {
                _hoppingLocomotion.SetDestination(target);
            }

            // Handle surface alignment - ONLY for continuous locomotion
            // Hopping locomotion handles its own grounding at landing time
            if (_surfaceHandler != null && locomotionType == LocomotionType.Continuous)
            {
                bool isMidHop = _locomotion != null && _locomotion.IsInMotion;

                if (!isMidHop)
                {
                    _surfaceHandler.UpdateSurface(target, dt);

                    if (_surfaceHandler.ShouldBlockMovement)
                    {
                        _locomotion.Stop();
                        _surfaceHandler.EnsureGrounded();
                        UpdateAnimationState(false);
                        return;
                    }

                    if (!_locomotion.IsInMotion)
                    {
                        _surfaceHandler.EnsureGrounded();
                    }
                }
            }
            // For hopping locomotion: DO NOT call EnsureGrounded!
            // The hop landing already positions the spider correctly.
            // Calling EnsureGrounded causes drift on tilted surfaces.

            // Compute move direction
            Vector3 moveDirection = ComputeMoveDirection(target);
            _debugBestDir = moveDirection;

            // If we've reached destination, we're idle (waiting at waypoint)
            // Otherwise, check if we have a direction to move
            bool isMoving = !hasReachedDestination && moveDirection.sqrMagnitude > 0.0001f;
            
            if (isMoving)
            {
                _locomotion.Move(moveDirection, dt, _speedMultiplier);
            }
            
            // Update animation state
            UpdateAnimationState(isMoving);
        }

        #endregion

        #region Initialization

        private void InitializeComponents()
        {
            ISurfaceProvider surfaceProvider = null;

            if (enableSurfaceAlignment)
            {
                _surfaceHandler = new SimplifiedSurfaceHandler(transform, surfaceSettings, drawSurfaceRays, debugLogs);
                surfaceProvider = _surfaceHandler;
            }

            if (locomotionType == LocomotionType.Hopping)
            {
                _hoppingLocomotion = new HoppingLocomotion(transform, locomotionSettings, surfaceProvider);
                _hoppingLocomotion.SetDebugLogs(debugLogs || debugHopLanding); // Enable logging if either flag is set
                _locomotion = _hoppingLocomotion;
            }
            else
            {
                _locomotion = new ContinuousLocomotion(transform, locomotionSettings, surfaceProvider);
            }

            _obstacleAvoidance = new ContextSteering(avoidanceSettings, surfaceProvider, drawSensorRays);
        }

        #endregion

        #region Public API

        public void SetDestination(Vector3 targetPoint)
        {
            _currentTarget = targetPoint;
            _isStopped = false;
        }

        public void Stop()
        {
            _currentTarget = null;
            _isStopped = true;
            _locomotion?.Stop();
            _surfaceHandler?.ResetTransition();
            
            UpdateAnimationState(false);
            
            if (_hoppingLocomotion != null)
            {
                _hoppingLocomotion.SetDestination(null);
            }
        }

        public bool HasReachedDestination(float threshold = 0.2f)
        {
            if (_currentTarget == null)
                return true;

            Vector3 diff = transform.position - _currentTarget.Value;
            float distance = enableSurfaceAlignment ? diff.magnitude : Vector3.ProjectOnPlane(diff, GetUp()).magnitude;

            return distance < threshold;
        }

        public float GetDistanceToDestination()
        {
            if (_currentTarget == null)
                return 0f;

            Vector3 diff = transform.position - _currentTarget.Value;
            return enableSurfaceAlignment ? diff.magnitude : Vector3.ProjectOnPlane(diff, GetUp()).magnitude;
        }

        public void SetSpeedMultiplier(float multiplier)
        {
            _speedMultiplier = Mathf.Max(0f, multiplier);
        }

        public void SetObstacleMask(LayerMask newMask)
        {
            _obstacleAvoidance?.SetObstacleMask(newMask);
        }

        public void ResetObstacleMask()
        {
            _obstacleAvoidance?.ResetObstacleMask();
        }
        
        /// <summary>
        /// Enable or disable hop landing debug logs at runtime
        /// </summary>
        public void SetHopDebugLogs(bool enabled)
        {
            _hoppingLocomotion?.SetDebugLogs(enabled);
        }

        #endregion

        #region Navigation Logic

        private Vector3 ComputeMoveDirection(Vector3 target)
        {
            // If the hopping locomotion is doing a transition hop, don't override its direction
            if (_hoppingLocomotion != null && _hoppingLocomotion.IsTransitionHop)
            {
                // Still need to return a direction for the hop to continue
                Vector3 toTarget = target - transform.position;
                return toTarget.sqrMagnitude > 0.001f ? toTarget.normalized : transform.forward;
            }

            // If in a surface transition (continuous locomotion), use the transition direction
            if (_surfaceHandler != null && _surfaceHandler.IsInTransition)
            {
                Vector3 transitionDir = _surfaceHandler.TransitionMoveDirection;
                if (transitionDir.sqrMagnitude > 0.001f)
                {
                    if (debugLogs && Time.frameCount % 30 == 0)
                        Debug.Log($"[Navigator] Using transition direction");
                    return transitionDir;
                }
            }

            // Standard navigation
            return ComputeStandardDirection(target);
        }

        private Vector3 ComputeStandardDirection(Vector3 target)
        {
            Vector3 up = GetUp();
            Vector3 toTarget = target - transform.position;

            Vector3 toTargetPlanar = Vector3.ProjectOnPlane(toTarget, up);
            float planarDist = toTargetPlanar.magnitude;

            // When surface alignment is enabled, NEVER return a direction that contains "up".
            // If planar is tiny, just keep going forward on the current surface.
            if (enableSurfaceAlignment)
            {
                if (planarDist < 0.001f)
                {
                    // Target might be on a different surface (above/below on wall)
                    // Check if we should be approaching a surface transition
                    if (toTarget.magnitude > 0.5f)
                    {
                        // There's significant distance but no planar component
                        // This means target is "above" or "below" us in current orientation
                        // Keep moving forward to find the surface edge
                        Vector3 fallback = Vector3.ProjectOnPlane(transform.forward, up);
                        return fallback.sqrMagnitude > 0.0001f ? fallback.normalized : Vector3.zero;
                    }
                    
                    Vector3 fallbackDir = Vector3.ProjectOnPlane(transform.forward, up);
                    return fallbackDir.sqrMagnitude > 0.0001f ? fallbackDir.normalized : Vector3.zero;
                }
            }
            else
            {
                if (planarDist < 0.01f && toTarget.magnitude > 0.1f)
                    return toTarget.normalized;
                if (planarDist < 0.001f)
                    return Vector3.zero;
            }

            if (planarDist < 0.001f)
                return Vector3.zero;

            Vector3 idealDir = toTargetPlanar.normalized;
            Vector3 finalDir = idealDir;

            // Obstacle avoidance
            if (planarDist > directApproachDistance)
            {
                if (_obstacleAvoidance.TryGetEmergencyRepulsion(transform.position, out Vector3 repulsion))
                {
                    finalDir = repulsion;
                }
                else
                {
                    finalDir = _obstacleAvoidance.ComputeSafeDirection(idealDir, transform.position, transform.forward);
                }
            }
            else if (planarDist > steeringDisableDistance)
            {
                Vector3 steeringDir = _obstacleAvoidance.ComputeSafeDirection(idealDir, transform.position, transform.forward);
                float steeringAngle = Vector3.Angle(idealDir, steeringDir);

                if (steeringAngle < maxSteeringAngle)
                {
                    finalDir = steeringDir;
                }
                else
                {
                    finalDir = Vector3.Slerp(steeringDir, idealDir, 0.7f).normalized;
                }
            }

            return finalDir;
        }

        private Vector3 GetUp()
        {
            return enableSurfaceAlignment ? transform.up : Vector3.up;
        }

        private void UpdateAnimationState(bool isMoving)
        {
            if (_animator == null) return;

            // Don't update walking/idle animations if currently attacking
            // Attack animation has priority
            bool isAttacking = _animator.GetBool("reg_attac");
            if (isAttacking)
                return;

            _animator.SetBool("reg_walking", isMoving);
            _animator.SetBool("reg_idle", !isMoving);
            
            // Debug log (remove after testing)
            if (debugLogs && Time.frameCount % 60 == 0)
            {
                Debug.Log($"[SteeringNavigator] Animation state - isMoving: {isMoving}, reg_walking: {_animator.GetBool("reg_walking")}, reg_idle: {_animator.GetBool("reg_idle")}, reg_attac: {isAttacking}", this);
            }
        }

        #endregion

        #region Gizmos

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying)
                return;

            if (drawBestDirection)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawLine(transform.position, transform.position + _debugBestDir * 2f);
            }

            if (_currentTarget != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(_currentTarget.Value, 0.3f);
            }

            // Show surface handler transition
            if (_surfaceHandler != null && _surfaceHandler.IsInTransition)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawRay(transform.position, _surfaceHandler.TransitionMoveDirection * 2f);
            }

            // Show hop transition
            if (_hoppingLocomotion != null && _hoppingLocomotion.IsTransitionHop)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawRay(transform.position, _hoppingLocomotion.TransitionTargetNormal * 1.5f);
            }
        }

        #endregion
    }
}