// FILEPATH: Assets/Scripts/AI/Movement/ISpeedMultiplierSink.cs
using UnityEngine;

namespace JellyGame.GamePlay.Enemy.AI.Movement
{
    /// <summary>
    /// Any component that can have its speed scaled by a multiplier should implement this.
    /// 1 = normal, 0.5 = half speed, 2 = double speed.
    /// </summary>
    public interface ISpeedMultiplierSink
    {
        void SetSpeedMultiplier(float multiplier);
    }
}