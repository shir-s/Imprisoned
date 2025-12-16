// FILEPATH: Assets/Scripts/AI/Behaviors/WaypointRouteBehavior.cs

using UnityEngine;
using JellyGame.GamePlay.Enemy.AI.Movement; // Reference the namespace above

namespace JellyGame.GamePlay.Enemy.AI.Behaviors
{
    [RequireComponent(typeof(SteeringNavigator))] // Ensures the navigator exists
    public class WaypointRouteBehavior : MonoBehaviour, IEnemyBehavior
    {
        public enum RouteType { Loop, PingPong }

        [Header("Configuration")]
        [SerializeField] private int priority = 1;
        [SerializeField] private RouteType routeType = RouteType.Loop;
        [SerializeField] private Transform[] waypoints;
        [SerializeField] private float waitTimeAtWaypoint = 2f;

        private SteeringNavigator _navigator;
        private int _currentIndex = 0;
        private int _direction = 1;
        private float _waitTimer = 0f;
        private bool _isWaiting = false;

        // NEW: keep track of the current waypoint transform
        private Transform _currentWaypoint;

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
            // Find nearest waypoint index to start
            _currentIndex = GetNearestWaypointIndex();

            // Tell the navigator to go there
            MoveToCurrentIndex();
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

            // Continuously update the destination so it follows the tilting surface.
            // This keeps the internal _currentTarget in SteeringNavigator in sync
            // with the actual waypoint Transform, which is moving with the tray.
            if (_currentWaypoint != null)
            {
                _navigator.SetDestination(_currentWaypoint.position);
            }

            // Check if Navigator says we arrived
            if (_navigator.HasReachedDestination(0.5f))
            {
                // Stop and wait
                _navigator.Stop();
                _isWaiting = true;
                _waitTimer = waitTimeAtWaypoint;
            }
        }

        public void OnExit()
        {
            _navigator.Stop();
            _isWaiting = false;
            _currentWaypoint = null;
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
            }
        }

        private void NextWaypoint()
        {
            if (waypoints == null || waypoints.Length == 0)
                return;

            if (routeType == RouteType.Loop)
            {
                _currentIndex = (_currentIndex + 1) % waypoints.Length;
            }
            else
            {
                _currentIndex += _direction;
                if (_currentIndex >= waypoints.Length - 1 || _currentIndex <= 0)
                {
                    _direction *= -1; // Flip direction
                }
            }
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
    }
}
