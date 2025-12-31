// FILEPATH: Assets/Scripts/Combat/Projectiles/BallisticAimSolver.cs
using UnityEngine;

namespace JellyGame.GamePlay.Combat.Projectiles
{
    public static class BallisticAimSolver
    {
        /// <summary>
        /// Compute initial velocity needed to hit target in a fixed time under gravity.
        /// Works even with different start/target heights (great for tilting surfaces).
        /// </summary>
        public static Vector3 SolveVelocityForTime(Vector3 startPos, Vector3 targetPos, float timeToTarget, Vector3 gravity)
        {
            timeToTarget = Mathf.Max(0.05f, timeToTarget);

            Vector3 toTarget = targetPos - startPos;
            // v0 = (x / t) - 0.5*g*t
            return (toTarget / timeToTarget) - (0.5f * gravity * timeToTarget);
        }
    }
}