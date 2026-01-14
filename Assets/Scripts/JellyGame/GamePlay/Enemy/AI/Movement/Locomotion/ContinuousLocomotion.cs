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
        
        public override void Rotate(Vector3 direction, float deltaTime)
        {
            if (direction.sqrMagnitude < 0.0001f)
                return;

            Vector3 up = surfaceProvider?.CurrentUp ?? Vector3.up;

            // ✅ Always rotate using planar direction, so you never tilt toward "up"
            Vector3 planar = Vector3.ProjectOnPlane(direction, up);
            if (planar.sqrMagnitude < 0.0001f)
                return;

            planar.Normalize();

            Quaternion targetRot = Quaternion.LookRotation(planar, up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRot,
                settings.TurnSpeed * deltaTime
            );
        }
    }
}