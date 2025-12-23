// FILEPATH: Assets/Scripts/AI/Behaviors/WaypointRouteBehavior.cs

using UnityEngine;
using JellyGame.GamePlay.Enemy.AI.Movement;

namespace JellyGame.GamePlay.Enemy.AI.Behaviors
{
    [RequireComponent(typeof(SteeringNavigator))]
    public class WaypointRouteBehavior : MonoBehaviour, IEnemyBehavior
    {
        public enum RouteType { Loop, PingPong }

        [Header("Configuration")]
        [SerializeField] private int priority = 1;
        [SerializeField] private RouteType routeType = RouteType.Loop;
        [SerializeField] private Transform[] waypoints;
        [SerializeField] private float waitTimeAtWaypoint = 2f;

        [Header("Arrival")]
        [Tooltip("How close the agent must be to consider the waypoint 'reached'. Increase if agent circles near waypoints.")]
        [SerializeField] private float arrivalThreshold = 1.0f;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private SteeringNavigator _navigator;
        private int _currentIndex = 0;
        private int _direction = 1;
        private float _waitTimer = 0f;
        private bool _isWaiting = false;

        // Keep track of the current waypoint transform
        private Transform _currentWaypoint;

        // Anti-stuck detection
        private Vector3 _lastPosition;
        private float _stuckCheckTimer;
        private float _stuckCheckInterval = 1.0f;
        private int _stuckCounter = 0;

        public int Priority => priority;

        private void Awake()
        {
            _navigator = GetComponent<SteeringNavigator>();
        }

        public bool CanActivate()
        {
            return waypoints != null && waypoints.Length > 1;
        }

        public void OnEnter()
        {
            _currentIndex = GetNearestWaypointIndex();
            _stuckCounter = 0;
            _lastPosition = transform.position;
            _stuckCheckTimer = _stuckCheckInterval;
            MoveToCurrentIndex();

            if (debugLogs)
                Debug.Log($"[WaypointRoute] OnEnter, starting at waypoint {_currentIndex}");
        }

        public void Tick(float deltaTime)
        {
            if (_isWaiting)
            {
                _waitTimer -= deltaTime;
                if (_waitTimer <= 0)
                {
                    _isWaiting = false;
                    NextWaypoint();
                    MoveToCurrentIndex();
                }
                return;
            }

            // Continuously update the destination so it follows the tilting surface
            if (_currentWaypoint != null)
            {
                _navigator.SetDestination(_currentWaypoint.position);
            }

            // Check if Navigator says we arrived
            if (_navigator.HasReachedDestination(arrivalThreshold))
            {
                if (debugLogs)
                    Debug.Log($"[WaypointRoute] Reached waypoint {_currentIndex}");

                _navigator.Stop();
                _isWaiting = true;
                _waitTimer = waitTimeAtWaypoint;
                _stuckCounter = 0;
                return;
            }

            // Anti-stuck detection: if we haven't moved much in a while, skip to next waypoint
            _stuckCheckTimer -= deltaTime;
            if (_stuckCheckTimer <= 0)
            {
                _stuckCheckTimer = _stuckCheckInterval;

                float movedDistance = Vector3.Distance(transform.position, _lastPosition);
                _lastPosition = transform.position;

                if (movedDistance < 0.1f) // Moved less than 10cm in the check interval
                {
                    _stuckCounter++;

                    if (debugLogs)
                        Debug.LogWarning($"[WaypointRoute] Possibly stuck (count={_stuckCounter}), moved only {movedDistance:F2}m");

                    // If stuck for too long, force move to next waypoint
                    if (_stuckCounter >= 3)
                    {
                        if (debugLogs)
                            Debug.LogWarning($"[WaypointRoute] Stuck too long, forcing next waypoint");

                        _navigator.Stop();
                        NextWaypoint();
                        MoveToCurrentIndex();
                        _stuckCounter = 0;
                    }
                }
                else
                {
                    _stuckCounter = 0; // Reset if we're moving
                }
            }
        }

        public void OnExit()
        {
            _navigator.Stop();
            _isWaiting = false;
            _currentWaypoint = null;

            if (debugLogs)
                Debug.Log($"[WaypointRoute] OnExit");
        }

        private void MoveToCurrentIndex()
        {
            _currentWaypoint = null;

            if (waypoints == null || waypoints.Length == 0)
                return;

            if (_currentIndex < 0 || _currentIndex >= waypoints.Length)
                _currentIndex = Mathf.Clamp(_currentIndex, 0, waypoints.Length - 1);

            var wp = waypoints[_currentIndex];
            if (wp != null)
            {
                _currentWaypoint = wp;
                _navigator.SetDestination(_currentWaypoint.position);

                if (debugLogs)
                    Debug.Log($"[WaypointRoute] Moving to waypoint {_currentIndex}: {wp.name}");
            }
        }

        private void NextWaypoint()
        {
            if (waypoints == null || waypoints.Length == 0)
                return;

            int previousIndex = _currentIndex;

            if (routeType == RouteType.Loop)
            {
                _currentIndex = (_currentIndex + 1) % waypoints.Length;
            }
            else // PingPong
            {
                _currentIndex += _direction;
                if (_currentIndex >= waypoints.Length - 1 || _currentIndex <= 0)
                {
                    _direction *= -1;
                }
            }

            if (debugLogs)
                Debug.Log($"[WaypointRoute] Next waypoint: {previousIndex} -> {_currentIndex}");
        }

        private int GetNearestWaypointIndex()
        {
            if (waypoints == null || waypoints.Length == 0)
                return 0;

            int best = 0;
            float minDst = float.MaxValue;
            Vector3 myPos = transform.position;

            for (int i = 0; i < waypoints.Length; i++)
            {
                var wp = waypoints[i];
                if (wp == null) continue;

                float d = Vector3.Distance(myPos, wp.position);
                if (d < minDst)
                {
                    minDst = d;
                    best = i;
                }
            }

            return best;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (waypoints == null || waypoints.Length == 0) return;

            // Draw waypoints
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (waypoints[i] == null) continue;

                // Current waypoint is green, others are yellow
                bool isCurrent = Application.isPlaying && i == _currentIndex;
                Gizmos.color = isCurrent ? Color.green : Color.yellow;
                Gizmos.DrawWireSphere(waypoints[i].position, arrivalThreshold);

                // Draw lines between waypoints
                int nextIndex = (i + 1) % waypoints.Length;
                if (waypoints[nextIndex] != null)
                {
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawLine(waypoints[i].position, waypoints[nextIndex].position);
                }
            }
        }
#endif
    }
}