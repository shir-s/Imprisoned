// FILEPATH: Assets/Scripts/Managers/PlayerSpawner.cs
using JellyGame.GamePlay.Map;
using UnityEngine;

namespace JellyGame.GamePlay.Managers
{
    /// <summary>
    /// Spawns the player (slime) prefab when the scene starts.
    /// 
    /// Place this in every LEVEL scene (not cutscenes, not menus).
    /// Assign the player prefab and spawn position in the Inspector.
    /// 
    /// The player is spawned as a CHILD of the TiltTray object so it moves
    /// with the surface when the tray tilts.
    /// 
    /// The spawned player's scripts (like AreaFillShapeDetector) will auto-find
    /// surfaces and painters at runtime, so no inspector references to scene
    /// objects are needed on the prefab.
    /// </summary>
    public class PlayerSpawner : MonoBehaviour
    {
        [Header("Player Prefab")]
        [Tooltip("The player (slime) prefab to instantiate.")]
        [SerializeField] private GameObject playerPrefab;

        [Header("Spawn Settings")]
        [Tooltip("Where to spawn the player. If null, uses this GameObject's position.")]
        [SerializeField] private Transform spawnPoint;

        [Tooltip("Override rotation? If false, uses spawnPoint/this rotation.")]
        [SerializeField] private bool useCustomRotation = false;

        [Tooltip("Custom spawn rotation (only if useCustomRotation is true).")]
        [SerializeField] private Vector3 customEulerRotation = Vector3.zero;

        [Header("Parent (TiltTray)")]
        [Tooltip("If assigned, spawn the player as a child of this transform.\n" +
                 "If null, auto-finds the TiltTray in the scene.")]
        [SerializeField] private Transform parentTransform;

        [Tooltip("If true, auto-find TiltTray in the scene when parentTransform is not assigned.")]
        [SerializeField] private bool autoFindTiltTray = true;

        [Header("Safety")]
        [Tooltip("Tag used to check if a player already exists. Prevents double-spawn.")]
        [SerializeField] private string playerTag = "DrawingCube";

        [Header("Debug")]
        [SerializeField] private bool debugLogs = true;

        private void Start()
        {
            SpawnPlayer();
        }

        private void SpawnPlayer()
        {
            if (playerPrefab == null)
            {
                Debug.LogError("[PlayerSpawner] playerPrefab is not assigned!", this);
                return;
            }

            // Safety check: don't spawn if player already exists
            GameObject existingPlayer = GameObject.FindGameObjectWithTag(playerTag);
            if (existingPlayer != null)
            {
                Debug.LogWarning($"[PlayerSpawner] Player already exists: {existingPlayer.name}. Skipping spawn.", this);
                return;
            }

            // Find parent (TiltTray)
            Transform parent = ResolveParent();

            // Determine position and rotation
            Vector3 position = spawnPoint != null ? spawnPoint.position : transform.position;
            Quaternion rotation;

            if (useCustomRotation)
                rotation = Quaternion.Euler(customEulerRotation);
            else
                rotation = spawnPoint != null ? spawnPoint.rotation : transform.rotation;

            // Spawn as child of TiltTray (or world root if no parent found)
            GameObject player;

            if (parent != null)
            {
                player = Instantiate(playerPrefab, position, rotation, parent);

                if (debugLogs)
                    Debug.Log($"[PlayerSpawner] Spawned player '{player.name}' at {position} as child of '{parent.name}'", this);
            }
            else
            {
                player = Instantiate(playerPrefab, position, rotation);

                if (debugLogs)
                    Debug.Log($"[PlayerSpawner] Spawned player '{player.name}' at {position} (no parent found — spawned at root)", this);
            }
        }

        /// <summary>
        /// Find the parent transform for the player.
        /// Priority: Inspector assignment → auto-find TiltTray in scene.
        /// </summary>
        private Transform ResolveParent()
        {
            // 1. Inspector assignment
            if (parentTransform != null)
            {
                if (debugLogs)
                    Debug.Log($"[PlayerSpawner] Using assigned parent: {parentTransform.name}", this);
                return parentTransform;
            }

            // 2. Auto-find TiltTray
            if (autoFindTiltTray)
            {
                TiltTray tray = FindObjectOfType<TiltTray>();
                if (tray != null)
                {
                    if (debugLogs)
                        Debug.Log($"[PlayerSpawner] Auto-found TiltTray: {tray.name}", this);
                    return tray.transform;
                }

                Debug.LogWarning("[PlayerSpawner] autoFindTiltTray enabled but no TiltTray found in scene!", this);
            }

            return null;
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 pos = spawnPoint != null ? spawnPoint.position : transform.position;
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(pos, 0.5f);
            Gizmos.DrawLine(pos, pos + Vector3.up * 2f);
        }
    }
}