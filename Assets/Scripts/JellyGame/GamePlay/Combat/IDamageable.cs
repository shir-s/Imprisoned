// FILEPATH: Assets/Scripts/Combat/IDamageable.cs
using UnityEngine;

namespace JellyGame.GamePlay.Combat
{
    /// <summary>
    /// Basic damage interface - all damageable objects implement this.
    /// </summary>
    public interface IDamageable
    {
        void ApplyDamage(float amount);
        void Heal(float amount);
    }

    /// <summary>
    /// Extended damage interface for objects that support percent-based damage.
    /// Implement this in addition to IDamageable if your object can handle multiplicative damage.
    /// 
    /// Example: CubeScaler would implement both IDamageable and IPercentDamageable.
    /// </summary>
    public interface IPercentDamageable
    {
        /// <summary>
        /// Apply percent-based damage.
        /// </summary>
        /// <param name="percent">Percent to remove (0-100). E.g., 10 = remove 10% of current health/size.</param>
        void ApplyPercentDamage(float percent);
    }
}