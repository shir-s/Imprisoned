// FILEPATH: Assets/Scripts/AI/Movement/NavigatorTypes.cs
// 
// FIXED VERSION: Added ShouldBlockMovement to ISurfaceHandler interface

using UnityEngine;

namespace JellyGame.GamePlay.Enemy.AI.Movement
{
    #region Interfaces

    /// <summary>
    /// Interface for components that can have their speed modified externally.
    /// Used by slow effects, buffs, terrain modifiers, etc.
    /// </summary>
    public interface ISpeedMultiplierSink
    {
        void SetSpeedMultiplier(float multiplier);
    }

    /// <summary>
    /// Provides surface information for locomotion.
    /// Implemented by the surface alignment system.
    /// </summary>
    public interface ISurfaceProvider
    {
        Vector3 CurrentUp { get; }
        bool IsGrounded { get; }
        Vector3 GroundPosition(Vector3 position);
    }

    /// <summary>
    /// Defines how an agent physically moves through space.
    /// </summary>
    public interface ILocomotion
    {
        bool IsInMotion { get; }
        float CurrentSpeed { get; }
        void Move(Vector3 direction, float deltaTime, float speedMultiplier = 1f);
        void Rotate(Vector3 direction, float deltaTime);
        void Stop();
        void OnEnable();
        void OnDisable();
    }

    /// <summary>
    /// Interface for obstacle avoidance strategies.
    /// </summary>
    public interface IObstacleAvoidance
    {
        Vector3 ComputeSafeDirection(Vector3 idealDirection, Vector3 position, Vector3 forward);
        bool TryGetEmergencyRepulsion(Vector3 position, out Vector3 repulsionDirection);
        void SetObstacleMask(LayerMask mask);
        void ResetObstacleMask();
        void SetIgnoredCollider(Collider collider);
        void ClearIgnoredCollider();
    }

    /// <summary>
    /// Handles surface alignment and wall climbing transitions.
    /// </summary>
    public interface ISurfaceHandler
    {
        Vector3 CurrentUp { get; }
        bool IsGrounded { get; }
        bool IsInTransition { get; }
        ClimbTransitionState TransitionState { get; }
        Vector3 TransitionMoveDirection { get; }
        
        /// <summary>
        /// NEW: Returns true if movement should be blocked because rotation is incomplete.
        /// During surface transitions, the enemy should pause movement until rotation
        /// to the new surface orientation is complete. This prevents transient
        /// non-axis-aligned orientations from being visible.
        /// </summary>
        bool ShouldBlockMovement { get; }
        
        void UpdateSurface(Vector3 targetPosition, float deltaTime);
        Vector3 GroundPosition(Vector3 position);
        void EnsureGrounded();
        void ResetTransition();
    }

    #endregion

    #region Enums

    /// <summary>
    /// State of a surface transition (e.g., floor to wall).
    /// </summary>
    public enum ClimbTransitionState
    {
        None,
        Approaching,
        Climbing
    }

    #endregion

    #region Settings Classes

    /// <summary>
    /// Configuration for locomotion behavior.
    /// </summary>
    [System.Serializable]
    public class LocomotionSettings
    {
        [Tooltip("Base movement speed in units per second.")]
        public float MoveSpeed = 3f;

        [Tooltip("Rotation speed in degrees per second.")]
        public float TurnSpeed = 720f;

        [Header("Hopping (if applicable)")]
        [Tooltip("Time between hop starts (seconds).")]
        public float HopInterval = 0.45f;

        [Tooltip("How long a hop takes (seconds).")]
        public float HopDuration = 0.22f;

        [Tooltip("Hop height along the current up direction.")]
        public float HopHeight = 0.35f;
    }

    /// <summary>
    /// Configuration for obstacle avoidance behavior.
    /// </summary>
    [System.Serializable]
    public class ObstacleAvoidanceSettings
    {
        [Header("Agent Size")]
        [Tooltip("Radius of the agent body for collision detection.")]
        public float BodyRadius = 0.5f;

        [Tooltip("Extra clearance around obstacles.")]
        public float Clearance = 0.2f;

        [Header("Sensors")]
        [Tooltip("Layers considered as obstacles.")]
        public LayerMask ObstacleLayers;

        [Tooltip("How far ahead to look for obstacles.")]
        public float LookAheadDistance = 2.0f;

        [Tooltip("Vertical offset for sensor rays.")]
        public float SensorVerticalOffset = 0.1f;

        [Tooltip("Number of directions to sample (higher = more accurate, slower).")]
        public int SensorResolution = 16;
    }

    /// <summary>
    /// Configuration for surface handling behavior.
    /// </summary>
    [System.Serializable]
    public class SurfaceSettings
    {
        [Header("Surface Detection")]
        [Tooltip("Which layers count as walkable surfaces.")]
        public LayerMask SurfaceLayers;

        [Tooltip("Ray distance along -up to find the supporting surface.")]
        public float StickDistance = 1.5f;

        [Tooltip("Ray distance forward to detect upcoming walls.")]
        public float ForwardProbeDistance = 1.0f;

        [Tooltip("How quickly we rotate to match a new surface normal.")]
        public float AlignSpeed = 12f;

        [Tooltip("Dot product threshold for what counts as a supporting surface (0.7 ~= 45°).")]
        [Range(0f, 0.99f)]
        public float SupportNormalDot = 0.7f;

        [Header("Wall Climbing Transition")]
        [Tooltip("Distance at which to start approaching a different surface.")]
        public float ApproachStartDistance = 3.0f;

        [Tooltip("Distance at which to start rotating onto the surface.")]
        public float ClimbStartDistance = 0.8f;

        [Tooltip("Angle difference (degrees) to consider surfaces 'different'.")]
        public float SurfaceAngleThreshold = 30f;

        [Header("Waypoint Surface Detection")]
        [Tooltip("Radius to search for surfaces near a waypoint.")]
        public float WaypointSearchRadius = 2.0f;

        [Tooltip("Maximum search radius (searched in steps).")]
        public float WaypointMaxSearchRadius = 8.0f;

        [Tooltip("Search radius increment per step.")]
        public float WaypointSearchStep = 2.0f;

        [Tooltip("Offset from surface to prevent clipping.")]
        public float SurfaceAnchorOffset = 0.05f;

        [Tooltip("Lock the surface normal for this duration to prevent flicker.")]
        public float NormalLockDuration = 0.5f;

        [Header("Agent Size")]
        [Tooltip("Agent body radius for grounding calculations.")]
        public float BodyRadius = 0.5f;
    }

    #endregion
}