// FILEPATH: Assets/Scripts/AI/Movement/Locomotion/LocomotionBase.cs
using UnityEngine;

namespace JellyGame.GamePlay.Enemy.AI.Movement
{
    /// <summary>
    /// Base class for locomotion implementations.
    /// Provides common functionality and configuration.
    /// </summary>
    public abstract class LocomotionBase : ILocomotion
    {
        protected readonly Transform transform;
        protected readonly LocomotionSettings settings;
        protected readonly ISurfaceProvider surfaceProvider;

        public abstract bool IsInMotion { get; }
        public float CurrentSpeed => settings.MoveSpeed;

        protected LocomotionBase(Transform transform, LocomotionSettings settings, ISurfaceProvider surfaceProvider = null)
        {
            this.transform = transform;
            this.settings = settings;
            this.surfaceProvider = surfaceProvider;
        }

        public abstract void Move(Vector3 direction, float deltaTime, float speedMultiplier = 1f);

        public abstract void Rotate(Vector3 direction, float deltaTime);

        public virtual void Stop() { }
        public virtual void OnEnable() { }
        public virtual void OnDisable() { }

        protected Vector3 GetUp()
        {
            return surfaceProvider?.CurrentUp ?? Vector3.up;
        }

        protected Vector3 ProjectOnMovementPlane(Vector3 direction)
        {
            Vector3 up = GetUp();
            return Vector3.ProjectOnPlane(direction, up).normalized;
        }
    }
}