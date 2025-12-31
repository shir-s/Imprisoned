// FILEPATH: Assets/Scripts/Combat/Targeting/TargetAimPoint.cs
using UnityEngine;

namespace JellyGame.GamePlay.Combat.Targeting
{
    [DisallowMultipleComponent]
    public class TargetAimPoint : MonoBehaviour, ITargetAimPoint
    {
        [SerializeField] private Transform aimPoint;

        public Transform AimPoint => aimPoint != null ? aimPoint : transform;
    }
}