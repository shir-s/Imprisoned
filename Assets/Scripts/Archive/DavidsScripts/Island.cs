using System;
using System.Collections.Generic;
using UnityEngine;

namespace JellyGame.GamePlay.Map
{
    /// <summary>
    /// Represents a single island in a multi-island level.
    /// Each island can tilt independently and tracks which units are currently on it.
    /// 
    /// Key features:
    /// - Independent tilt control (uses TiltTray internally)
    /// - Unit tracking (knows what's on the island)
    /// - Automatic activation when player enters
    /// - Collision detection for island boundaries
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class Island : MonoBehaviour
    {
        [Header("Island Identity")]
        [SerializeField] private string islandID = "Island_01";
        [Tooltip("Visual reference for the island (optional, for debugging)")]
        [SerializeField] private string islandName = "Main Island";

        [Header("Tilt Behavior")]
        [Tooltip("The TiltTray component that controls this island's rotation")]
        [SerializeField] private TiltTray tiltTray;
        
        [Tooltip("Auto-find TiltTray on this GameObject if not assigned")]
        [SerializeField] private bool autoFindTiltTray = true;

        [Tooltip("Should this island tilt when the player is on it?")]
        [SerializeField] private bool canTilt = true;

        [Header("Surface Detection")]
        [Tooltip("Layer mask for units that should stick to this island")]
        [SerializeField] private LayerMask unitLayers = -1;

        [Tooltip("Minimum time a unit must be on the island before it's considered 'landed'")]
        [SerializeField] private float landingStabilityTime = 0.1f;

        [Header("Boundaries")]
        [Tooltip("If true, units will be prevented from leaving the island bounds")]
        [SerializeField] private bool enforceBoundaries = true;

        [Tooltip("Extra margin inside the collider bounds where units are safe")]
        [SerializeField] private float boundaryMargin = 0.5f;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;
        [SerializeField] private bool showGizmos = true;
        [SerializeField] private Color islandColor = new Color(0.3f, 0.7f, 1f, 0.3f);

        // Runtime state
        private HashSet<Transform> _unitsOnIsland = new HashSet<Transform>();
        private Transform _playerTransform;
        private bool _isPlayerOnIsland;
        private Collider _collider;
        private Bounds _localBounds;

        // Events for external systems
        public event Action<Transform> OnUnitEntered;
        public event Action<Transform> OnUnitExited;
        public event Action OnPlayerEntered;
        public event Action OnPlayerExited;

        // Public properties
        public string IslandID => islandID;
        public string IslandName => islandName;
        public bool IsPlayerOnIsland => _isPlayerOnIsland;
        public int UnitCount => _unitsOnIsland.Count;
        public TiltTray TiltTray => tiltTray;
        public Bounds WorldBounds => _collider != null ? _collider.bounds : new Bounds(transform.position, Vector3.one);

        private void Awake()
        {
            _collider = GetComponent<Collider>();
            _collider.isTrigger = true; // Island collider should be a trigger

            if (autoFindTiltTray && tiltTray == null)
            {
                tiltTray = GetComponent<TiltTray>();
                if (tiltTray == null)
                {
                    tiltTray = GetComponentInChildren<TiltTray>();
                }
            }

            if (tiltTray == null && debugLogs)
            {
                Debug.LogWarning($"[Island] No TiltTray found on island '{islandName}'", this);
            }

            // Calculate local bounds for boundary enforcement
            if (_collider != null)
            {
                _localBounds = _collider.bounds;
            }
        }

        private void Start()
        {
            // Try to find player
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                _playerTransform = player.transform;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            // Check if this is a unit we should track
            if (((1 << other.gameObject.layer) & unitLayers) == 0)
                return;

            Transform unit = other.transform;
            bool isPlayer = IsPlayerUnit(unit);

            if (_unitsOnIsland.Add(unit))
            {
                if (debugLogs)
                {
                    Debug.Log($"[Island] Unit entered {islandName}: {unit.name} (Player: {isPlayer})", this);
                }

                OnUnitEntered?.Invoke(unit);

                if (isPlayer)
                {
                    _isPlayerOnIsland = true;
                    OnPlayerEntered?.Invoke();
                    
                    // Notify island manager to switch active island
                    IslandManager.Instance?.SetActiveIsland(this);
                }
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (((1 << other.gameObject.layer) & unitLayers) == 0)
                return;

            Transform unit = other.transform;
            bool isPlayer = IsPlayerUnit(unit);

            if (_unitsOnIsland.Remove(unit))
            {
                if (debugLogs)
                {
                    Debug.Log($"[Island] Unit exited {islandName}: {unit.name} (Player: {isPlayer})", this);
                }

                OnUnitExited?.Invoke(unit);

                if (isPlayer)
                {
                    _isPlayerOnIsland = false;
                    OnPlayerExited?.Invoke();
                }
            }
        }

        /// <summary>
        /// Check if a transform is the player
        /// </summary>
        private bool IsPlayerUnit(Transform unit)
        {
            if (_playerTransform == null)
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    _playerTransform = player.transform;
                }
            }

            return unit == _playerTransform || unit.CompareTag("Player");
        }

        /// <summary>
        /// Check if a world position is within this island's bounds
        /// </summary>
        public bool IsPositionInBounds(Vector3 worldPosition, float margin = 0f)
        {
            if (_collider == null)
                return false;

            Bounds bounds = _collider.bounds;
            bounds.Expand(-margin * 2f); // Shrink bounds by margin on all sides

            return bounds.Contains(worldPosition);
        }

        /// <summary>
        /// Clamp a world position to stay within island bounds
        /// </summary>
        public Vector3 ClampPositionToBounds(Vector3 worldPosition, float margin = 0f)
        {
            if (_collider == null)
                return worldPosition;

            Bounds bounds = _collider.bounds;
            if (margin > 0f)
            {
                bounds.Expand(-margin * 2f);
            }

            return new Vector3(
                Mathf.Clamp(worldPosition.x, bounds.min.x, bounds.max.x),
                worldPosition.y, // Don't clamp Y
                Mathf.Clamp(worldPosition.z, bounds.min.z, bounds.max.z)
            );
        }

        /// <summary>
        /// Get the surface Y position at a given XZ coordinate using raycast
        /// </summary>
        public bool TryGetSurfaceY(Vector3 worldPosition, out float surfaceY, float rayDistance = 10f)
        {
            surfaceY = worldPosition.y;

            if (tiltTray == null)
                return false;

            Vector3 trayUp = tiltTray.transform.up;
            Vector3 rayOrigin = worldPosition + trayUp * rayDistance;
            Vector3 rayDir = -trayUp;

            if (Physics.Raycast(rayOrigin, rayDir, out RaycastHit hit, rayDistance * 2f, ~0, QueryTriggerInteraction.Ignore))
            {
                // Verify we hit this island
                if (hit.collider.transform == transform || 
                    hit.collider.transform.IsChildOf(transform) ||
                    transform.IsChildOf(hit.collider.transform))
                {
                    surfaceY = hit.point.y;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Enable/disable tilting for this island
        /// </summary>
        public void SetTiltEnabled(bool enabled)
        {
            if (tiltTray == null)
                return;

            tiltTray.enabled = enabled && canTilt;
        }

        /// <summary>
        /// Check if a unit is currently on this island
        /// </summary>
        public bool IsUnitOnIsland(Transform unit)
        {
            return _unitsOnIsland.Contains(unit);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!showGizmos || _collider == null)
                return;

            // Draw island bounds
            Gizmos.color = islandColor;
            Gizmos.matrix = transform.localToWorldMatrix;
            
            if (_collider is BoxCollider box)
            {
                Gizmos.DrawWireCube(box.center, box.size);
            }
            else if (_collider is SphereCollider sphere)
            {
                Gizmos.DrawWireSphere(sphere.center, sphere.radius);
            }
            else
            {
                Gizmos.DrawWireCube(_collider.bounds.center, _collider.bounds.size);
            }

            // Draw player indicator if player is on this island
            if (Application.isPlaying && _isPlayerOnIsland)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(transform.position + Vector3.up * 2f, 0.5f);
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (_collider == null)
                return;

            // Draw boundary margin
            Gizmos.color = Color.red;
            Bounds bounds = _collider.bounds;
            bounds.Expand(-boundaryMargin * 2f);
            Gizmos.DrawWireCube(bounds.center, bounds.size);

            // Draw up vector
            if (tiltTray != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawRay(transform.position, tiltTray.transform.up * 2f);
            }
        }
#endif
    }
}