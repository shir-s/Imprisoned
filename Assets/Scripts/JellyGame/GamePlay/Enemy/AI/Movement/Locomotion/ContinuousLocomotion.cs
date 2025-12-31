// FILEPATH: Assets/Scripts/AI/Movement/Locomotion/ContinuousLocomotion.cs
using UnityEngine;

namespace JellyGame.GamePlay.Enemy.AI.Movement
{
    /// <summary>
    /// Smooth, continuous movement locomotion.
    /// The agent slides along the ground without visible stepping.
    /// </summary>
    public class ContinuousLocomotion : LocomotionBase
    {
        public override bool IsInMotion => false;

        public ContinuousLocomotion(Transform transform, LocomotionSettings settings, ISurfaceProvider surfaceProvider = null)
            : base(transform, settings, surfaceProvider)
        {
        }

        public override void Move(Vector3 direction, float deltaTime, float speedMultiplier = 1f)
        {
            if (direction.sqrMagnitude < 0.0001f)
                return;

            Rotate(direction, deltaTime);

            float currentSpeed = settings.MoveSpeed * Mathf.Max(0f, speedMultiplier);
            Vector3 movement = transform.forward * currentSpeed * deltaTime;

            Vector3 targetPos = transform.position + movement;

            if (surfaceProvider != null)
            {
                targetPos = surfaceProvider.GroundPosition(targetPos);
            }

            transform.position = targetPos;
        }
    }
}