// FILEPATH: Assets/Scripts/AI/IEnemyBehavior.cs

namespace JellyGame.GamePlay.Enemy.AI
{
    /// <summary>
    /// Generic enemy behavior interface controlled by a behavior controller / brain.
    /// 
    /// Contract:
    /// - Priority: higher value = more important.
    /// - CanActivate(): returns true if this behavior wants control *right now*.
    /// - OnEnter(): called when the controller switches to this behavior.
    /// - Tick(dt): called every frame while this behavior is active.
    /// - OnExit(): called when the controller leaves this behavior.
    /// </summary>
    public interface IEnemyBehavior
    {
        /// <summary>Higher value = higher priority.</summary>
        int Priority { get; }

        /// <summary>
        /// Returns true if this behavior considers itself valid to run at this moment.
        /// Example:
        /// - FollowStroke: true only if there is a detectable stroke point in radius.
        /// - Wander: almost always true (fallback).
        /// </summary>
        bool CanActivate();

        /// <summary>Called once when this behavior becomes the active one.</summary>
        void OnEnter();

        /// <summary>Called every frame while this behavior is the active one.</summary>
        void Tick(float deltaTime);

        /// <summary>Called once when this behavior stops being active.</summary>
        void OnExit();
    }
}