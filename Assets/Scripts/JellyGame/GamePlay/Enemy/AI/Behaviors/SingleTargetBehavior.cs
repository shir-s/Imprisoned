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
        [Tooltip("If true and target has a Collider, move toward the nearest surface point so the enemy stops at the collider instead of entering it.")]
        [SerializeField] private bool stopAtSurface = true;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private SteeringNavigator _navigator;
        private bool _hasReachedTarget;
        private Collider _targetCollider;

        public int Priority => priority;

        /// <summary>
        /// Set the target at runtime (e.g. from WaveEnemySpawner or level script).
        /// Use this when the target is a scene object so the prefab doesn't need a reference.
        /// </summary>
        public void SetTarget(Transform target)
        {
            targetPoint = target;
            _targetCollider = target != null ? target.GetComponent<Collider>() : null;
        }

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
                _targetCollider = targetPoint.GetComponent<Collider>();
                Vector3 dest = GetDestination();
                _navigator.SetDestination(dest);
                if (debugLogs)
                    Debug.Log($"[SingleTarget] OnEnter, moving to target: {targetPoint.name} (stop at surface: {stopAtSurface && _targetCollider != null})", this);
            }
        }

        public void Tick(float deltaTime)
        {
            if (targetPoint == null)
                return;

            if (_hasReachedTarget && stopWhenReached)
                return;

            // Continuously update destination: use nearest surface point if stop-at-surface, else center
            Vector3 dest = GetDestination();
            _navigator.SetDestination(dest);

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
            _targetCollider = null;
            if (debugLogs)
                Debug.Log("[SingleTarget] OnExit", this);
        }

        /// <summary>
        /// Returns the position to move toward: nearest point on target's collider surface (so we stop at surface)
        /// or target.position if no collider or stopAtSurface is false.
        /// </summary>
        private Vector3 GetDestination()
        {
            if (targetPoint == null)
                return transform.position;

            if (stopAtSurface && _targetCollider != null)
                return _targetCollider.ClosestPoint(transform.position);
            return targetPoint.position;
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