// FILEPATH: Assets/Scripts/AI/Movement/IMovementSpeedEffectReceiver.cs
namespace JellyGame.GamePlay.Enemy.AI.Movement
{
    /// <summary>
    /// Public API for gameplay effects (slow, haste, etc.).
    /// This is what projectiles/zones call.
    /// </summary>
    public interface IMovementSpeedEffectReceiver
    {
        /// <summary>
        /// Applies a speed multiplier for a duration.
        /// Example: multiplier=0.5f for 2s -> half speed for 2 seconds.
        /// </summary>
        void ApplySpeedMultiplier(float multiplier, float durationSeconds);
    }
}