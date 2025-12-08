using JellyGame.GamePlay.Utils;
using UnityEngine;

namespace JellyGame.GamePlay.Enemy
{
    /// <summary>
    /// Spawns a pickup cube when an enemy dies.
    /// Listens to EnemyKilled events and spawns the cube at the enemy's death location.
    /// </summary>
    public class EnemyDeathDropSpawner : MonoBehaviour
    {
        [Header("Drop Settings")]
        [Tooltip("Prefab of the cube to spawn when enemy dies")]
        [SerializeField] private GameObject dropCubePrefab;

        [Tooltip("Offset from enemy position where cube spawns (Y offset to place on ground)")]
        [SerializeField] private Vector3 spawnOffset = new Vector3(0f, 0f, 0f);

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private void OnEnable()
        {
            // Listen for enemy death events
            //EventManager.StartListening(EventManager.GameEvent.EnemyKilled, OnEnemyKilled);
            JellyGameEvents.EnemyDied += OnEnemyKilled;
        }

        private void OnDisable()
        {
            // Stop listening when disabled
            //EventManager.StopListening(EventManager.GameEvent.EnemyKilled, OnEnemyKilled);
            JellyGameEvents.EnemyDied -= OnEnemyKilled;
        }

        private void OnEnemyKilled(Vector3 deathPosition)
        {
            Debug.Log("EnemyDeathDropSpawner.OnEnemyKilled()");
            if (dropCubePrefab == null)
            {
                if (debugLogs)
                    Debug.LogWarning("[EnemyDeathDropSpawner] No drop cube prefab assigned!", this);
                return;
            }

            // Get the death position from event data
            //Vector3 deathPosition = Vector3.zero;
            //bool hasValidPosition = false;

            /*if (eventData is Vector3 pos)
        {
            deathPosition = pos;
            hasValidPosition = true;
        }
        else if (eventData is GameObject go && go != null)
        {
            // Fallback: try to get position from GameObject (might be null if already destroyed)
            deathPosition = go.transform.position;
            hasValidPosition = true;
        }
        else if (eventData is Transform tr && tr != null)
        {
            // Fallback: try to get position from Transform
            deathPosition = tr.position;
            hasValidPosition = true;
        }

        if (!hasValidPosition)
        {
            if (debugLogs)
                Debug.LogWarning("[EnemyDeathDropSpawner] EnemyKilled event data is not a Vector3, GameObject, or Transform!", this);
            return;
        }*/

            // Calculate spawn position with offset
            Vector3 spawnPosition = deathPosition + spawnOffset;

            // Spawn the cube
        
            GameObject dropCube = Instantiate(dropCubePrefab, spawnPosition, Quaternion.identity);
            dropCube.name = "StickinessPickup_" + Time.time;

            if (debugLogs)
            {
                Debug.Log($"[EnemyDeathDropSpawner] Spawned drop cube at {spawnPosition} (enemy died at {deathPosition})", this);
            }
        }
    }
}

