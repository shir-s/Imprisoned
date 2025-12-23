// FILEPATH: Assets/Scripts/Combat/Targeting/ITargetAimPoint.cs
using UnityEngine;

namespace JellyGame.GamePlay.Combat.Targeting
{
    public interface ITargetAimPoint
    {
        Transform AimPoint { get; }
    }
}