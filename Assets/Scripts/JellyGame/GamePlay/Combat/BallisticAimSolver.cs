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

        /// <summary>
        /// Solve for the launch velocity given a FIXED launch speed.
        /// Uses the classic ballistic angle equation to find the exact angle
        /// that hits the target at the specified speed.
        /// 
        /// This is the correct fallback when SolveVelocityForTime produces a velocity
        /// that exceeds the maximum launch speed — instead of naively capping the magnitude
        /// (which preserves the wrong angle and causes undershooting), this method finds
        /// the angle that actually reaches the target.
        /// 
        /// Returns Vector3.zero if the target is unreachable at the given speed.
        /// </summary>
        /// <param name="start">Launch position.</param>
        /// <param name="target">Target position.</param>
        /// <param name="speed">Fixed launch speed (magnitude of velocity).</param>
        /// <param name="gravity">Gravity vector (e.g. Physics.gravity * multiplier).</param>
        /// <param name="preferHighArc">If true, returns the lobbed (high-angle) trajectory.
        /// If false, returns the flatter (low-angle) trajectory which arrives faster.</param>
        public static Vector3 SolveForFixedSpeed(Vector3 start, Vector3 target, float speed, Vector3 gravity, bool preferHighArc = false)
        {
            Vector3 toTarget = target - start;
            Vector3 horizontalVec = new Vector3(toTarget.x, 0f, toTarget.z);
            float x = horizontalVec.magnitude;
            float y = toTarget.y;

            // Use the vertical component of gravity (positive magnitude).
            // This solver assumes gravity acts vertically, which is standard for Unity.
            float g = Mathf.Abs(gravity.y);
            if (g < 0.001f) g = 0.001f; // safety: avoid division by zero if gravity is near-zero

            float v = Mathf.Max(0.01f, speed);

            // Degenerate case: target is directly above or below
            if (x < 0.001f)
            {
                if (y >= 0f)
                    return Vector3.up * v;
                else
                    return Vector3.zero; // can't reach below with ballistic arc
            }

            // Classic ballistic angle formula:
            //
            //   θ = atan( (v² ± √(v⁴ - g(gx² + 2yv²))) / (gx) )
            //
            // Where:
            //   v = launch speed
            //   g = gravity magnitude (positive)
            //   x = horizontal distance
            //   y = vertical distance (positive = target above)
            //
            // The '+' gives the high arc, '-' gives the low arc.
            // If discriminant < 0, the target is out of range.

            float v2 = v * v;
            float v4 = v2 * v2;
            float gx = g * x;
            float disc = v4 - g * (gx * x + 2f * y * v2);

            if (disc < 0f)
                return Vector3.zero; // Target unreachable at this speed

            float sqrtDisc = Mathf.Sqrt(disc);

            float tanTheta;
            if (preferHighArc)
                tanTheta = (v2 + sqrtDisc) / gx;
            else
                tanTheta = (v2 - sqrtDisc) / gx;

            float theta = Mathf.Atan(tanTheta);

            // Convert angle back to velocity vector
            Vector3 horizontalDir = horizontalVec.normalized;
            float vHoriz = v * Mathf.Cos(theta);
            float vVert = v * Mathf.Sin(theta);

            return horizontalDir * vHoriz + Vector3.up * vVert;
        }
    }
}