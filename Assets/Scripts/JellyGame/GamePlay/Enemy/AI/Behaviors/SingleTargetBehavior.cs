// FILEPATH: Assets/Scripts/JellyGame/GamePlay/Enemy/AI/Behaviors/SingleTargetBehavior.cs
// Single target point: enemy tries to reach one point (e.g. for dragon on level with one destination).

using UnityEngine;
using JellyGame.GamePlay.Enemy.AI.Movement;

namespace JellyGame.GamePlay.Enemy.AI.Behaviors
{
    [RequireComponent(typeof(SteeringNavigator))]
    public class SingleTargetBehavior : MonoBehaviour, IEnemyBehavior
    {
        [Header("Configuration")]
        [SerializeField] private int priority = 1;
        [Tooltip("The single target point the enemy will try to reach.")]
        [SerializeField] private Transform targetPoint;

        [Header("Arrival")]
        [Tooltip("How close the agent must be to consider the target 'reached'. Increase if agent circles near target.")]
        [SerializeField] private float arrivalThreshold = 1f;
        [Tooltip("If true, stop moving when within arrival threshold. If false, keep updating destination (e.g. if target moves).")]
        [SerializeField] private bool stopWhenReached = true;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private SteeringNavigator _navigator;
        private bool _hasReachedTarget;

        public int Priority => priority;

        private void Awake()
        {
            _navigator = GetComponent<SteeringNavigator>();
        }

        public bool CanActivate()
        {
            return targetPoint != null;
        }

        public void OnEnter()
        {
            _hasReachedTarget = false;
            if (targetPoint != null)
            {
                _navigator.SetDestination(targetPoint.position);
                if (debugLogs)
                    Debug.Log($"[SingleTarget] OnEnter, moving to target: {targetPoint.name}", this);
            }
        }

        public void Tick(float deltaTime)
        {
            if (targetPoint == null)
                return;

            if (_hasReachedTarget && stopWhenReached)
                return;

            // Continuously update destination so it follows tilting surface (or moving target)
            _navigator.SetDestination(targetPoint.position);

            if (_navigator.HasReachedDestination(arrivalThreshold))
            {
                if (!_hasReachedTarget)
                {
                    _hasReachedTarget = true;
                    if (stopWhenReached)
                        _navigator.Stop();
                    if (debugLogs)
                        Debug.Log("[SingleTarget] Reached target.", this);
                }
            }
        }

        public void OnExit()
        {
            _navigator.Stop();
            _hasReachedTarget = false;
            if (debugLogs)
                Debug.Log("[SingleTarget] OnExit", this);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (targetPoint == null) return;
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(targetPoint.position, arrivalThreshold);
            if (Application.isPlaying)
                Gizmos.DrawLine(transform.position, targetPoint.position);
        }
#endif
    }
}