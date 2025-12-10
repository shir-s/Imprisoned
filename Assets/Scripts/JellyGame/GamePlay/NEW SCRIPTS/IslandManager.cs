// FILEPATH: Assets/Scripts/GamePlay/Map/IslandManager.cs
using System;
using System.Collections.Generic;
using UnityEngine;

namespace JellyGame.GamePlay.Map
{
    /// <summary>
    /// Central manager for all islands in the level.
    /// Handles switching between islands and coordinating tilt behavior.
    /// 
    /// Key responsibilities:
    /// - Track all islands in the scene
    /// - Activate/deactivate island tilting based on player position
    /// - Provide utility methods for finding islands
    /// - Handle edge cases (player between islands, etc.)
    /// </summary>
    public class IslandManager : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("Should only one island tilt at a time?")]
        [SerializeField] private bool singleActiveIsland = true;

        [Tooltip("Time delay before switching to a new island (prevents rapid switching)")]
        [SerializeField] private float switchDelay = 0.2f;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = true;

        // Singleton
        private static IslandManager _instance;
        public static IslandManager Instance => _instance;

        // Runtime state
        private readonly List<Island> _allIslands = new List<Island>();
        private Island _activeIsland;
        private float _lastSwitchTime;

        // Events
        public event Action<Island> OnActiveIslandChanged;

        // Properties
        public Island ActiveIsland => _activeIsland;
        public IReadOnlyList<Island> AllIslands => _allIslands;

        private void Awake()
        {
            // Singleton setup
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning("[IslandManager] Multiple IslandManagers detected. Destroying duplicate.", this);
                Destroy(gameObject);
                return;
            }
            _instance = this;

            // Find all islands in the scene
            FindAllIslands();
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void FindAllIslands()
        {
            _allIslands.Clear();
            _allIslands.AddRange(FindObjectsOfType<Island>());

            if (debugLogs)
            {
                Debug.Log($"[IslandManager] Found {_allIslands.Count} islands in scene", this);
            }

            // Subscribe to island events
            foreach (var island in _allIslands)
            {
                island.OnPlayerEntered += () => OnPlayerEnteredIsland(island);
                island.OnPlayerExited += () => OnPlayerExitedIsland(island);
            }

            // Disable all island tilting initially (will enable when player lands)
            if (singleActiveIsland)
            {
                foreach (var island in _allIslands)
                {
                    island.SetTiltEnabled(false);
                }
            }
        }

        /// <summary>
        /// Called when player enters an island trigger
        /// </summary>
        private void OnPlayerEnteredIsland(Island island)
        {
            if (debugLogs)
            {
                Debug.Log($"[IslandManager] Player entered island: {island.IslandName}", this);
            }

            // Use SetActiveIsland which handles the delay and switching logic
            SetActiveIsland(island);
        }

        /// <summary>
        /// Called when player exits an island trigger
        /// </summary>
        private void OnPlayerExitedIsland(Island island)
        {
            if (debugLogs)
            {
                Debug.Log($"[IslandManager] Player exited island: {island.IslandName}", this);
            }

            // If this was the active island, we might want to keep it active
            // until the player lands on another island (prevents disabling mid-jump)
            // The next island will take over when the player enters it
        }

        /// <summary>
        /// Set which island should be actively tilting.
        /// Called automatically when player enters a new island.
        /// </summary>
        public void SetActiveIsland(Island island)
        {
            if (island == null)
            {
                if (debugLogs)
                {
                    Debug.LogWarning("[IslandManager] Attempted to set null island as active", this);
                }
                return;
            }

            // Prevent rapid switching
            if (Time.time - _lastSwitchTime < switchDelay)
            {
                return;
            }

            // Already active, no change needed
            if (_activeIsland == island)
            {
                return;
            }

            if (debugLogs)
            {
                string fromName = _activeIsland != null ? _activeIsland.IslandName : "None";
                Debug.Log($"[IslandManager] Switching active island: {fromName} → {island.IslandName}", this);
            }

            // Deactivate previous island
            if (_activeIsland != null && singleActiveIsland)
            {
                _activeIsland.SetTiltEnabled(false);
            }

            // Activate new island
            _activeIsland = island;
            _activeIsland.SetTiltEnabled(true);
            _lastSwitchTime = Time.time;

            // Notify listeners
            OnActiveIslandChanged?.Invoke(_activeIsland);

            // Update camera reference in TiltTray if needed
            UpdateTiltTrayCamera(_activeIsland);
        }

        /// <summary>
        /// Update the active island's TiltTray to use the correct camera for input
        /// </summary>
        private void UpdateTiltTrayCamera(Island island)
        {
            if (island == null || island.TiltTray == null)
                return;

            // Explicitly use UnityEngine.Camera type to avoid namespace conflicts
            UnityEngine.Camera mainCam = UnityEngine.Camera.main;
            if (mainCam == null)
            {
                mainCam = FindObjectOfType<UnityEngine.Camera>();
            }

            if (mainCam != null)
            {
                island.TiltTray.SetInputCamera(mainCam.transform, island.TiltTray.UseCameraRelativeInput);
            }
        }

        /// <summary>
        /// Find the island at a given world position
        /// </summary>
        public Island FindIslandAtPosition(Vector3 worldPosition)
        {
            foreach (var island in _allIslands)
            {
                if (island.IsPositionInBounds(worldPosition))
                {
                    return island;
                }
            }
            return null;
        }

        /// <summary>
        /// Get the island a unit is currently on
        /// </summary>
        public Island GetIslandForUnit(Transform unit)
        {
            foreach (var island in _allIslands)
            {
                if (island.IsUnitOnIsland(unit))
                {
                    return island;
                }
            }
            return null;
        }

        /// <summary>
        /// Find the closest island to a given position
        /// </summary>
        public Island FindClosestIsland(Vector3 worldPosition)
        {
            Island closest = null;
            float closestDist = float.MaxValue;

            foreach (var island in _allIslands)
            {
                float dist = Vector3.Distance(worldPosition, island.transform.position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = island;
                }
            }

            return closest;
        }

        /// <summary>
        /// Manually register an island (useful if islands are spawned at runtime)
        /// </summary>
        public void RegisterIsland(Island island)
        {
            if (!_allIslands.Contains(island))
            {
                _allIslands.Add(island);

                island.OnPlayerEntered += () => OnPlayerEnteredIsland(island);
                island.OnPlayerExited += () => OnPlayerExitedIsland(island);

                if (debugLogs)
                {
                    Debug.Log($"[IslandManager] Registered island: {island.IslandName}", this);
                }
            }
        }

        /// <summary>
        /// Manually unregister an island (useful if islands are destroyed at runtime)
        /// </summary>
        public void UnregisterIsland(Island island)
        {
            if (_allIslands.Remove(island))
            {
                if (_activeIsland == island)
                {
                    _activeIsland = null;
                }

                if (debugLogs)
                {
                    Debug.Log($"[IslandManager] Unregistered island: {island.IslandName}", this);
                }
            }
        }

        /// <summary>
        /// Enable/disable tilting for all islands
        /// </summary>
        public void SetAllIslandsTiltEnabled(bool enabled)
        {
            foreach (var island in _allIslands)
            {
                island.SetTiltEnabled(enabled);
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!Application.isPlaying || _activeIsland == null)
                return;

            // Draw indicator for active island
            Gizmos.color = Color.yellow;
            Vector3 pos = _activeIsland.transform.position + Vector3.up * 3f;
            Gizmos.DrawWireSphere(pos, 0.7f);

            // Draw line from active island to player if found
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                Gizmos.DrawLine(pos, player.transform.position);
            }
        }
#endif
    }
}
