// FILEPATH: Assets/Scripts/World/Pickups/PickupRespawner.cs
using UnityEngine;
using JellyGame.GamePlay.Managers;
using JellyGame.GamePlay.Enemy;
using System.Collections;
using JellyGame.GamePlay.Audio.Core;

public class PickupRespawner : MonoBehaviour
{
    [Header("What to spawn")]
    [SerializeField] private GameObject pickupPrefab;
    [SerializeField] private bool parentToSpawner = true;

    [Header("Spawn Trigger")]
    [Tooltip("If true, spawn pickups when enemies die. If false, spawn when pickups are collected (old behavior).")]
    [SerializeField] private bool spawnOnEnemyDeath = true;

    [Tooltip("Only spawn pickups for enemies on these layers. If 0 (Nothing), spawn for all enemies.")]
    [SerializeField] private LayerMask enemyLayers = ~0;

    [Header("Spawn Position")]
    [Tooltip("Height offset above the enemy death position")]
    [SerializeField] private float spawnHeightOffset = 0.5f;
    
    [SerializeField] private float respawnDelaySeconds = 0f; // Delay before spawning (0 = immediate)

    [Header("Map bounds (for random spawn on map)")]
    [Tooltip("World space center of the map (X, Z only)")]
    [SerializeField] private Vector2 center = Vector2.zero;
    
    [Tooltip("Half size of the spawn area (X, Z only)")]
    [SerializeField] private Vector2 halfSize = new Vector2(25.11f, 25.11f);

    [Header("Random spawn on map settings")]
    [Tooltip("Height to start raycast from when finding ground")]
    [SerializeField] private float spawnHeight = 15f;
    
    [Tooltip("Layer mask for ground/map surface")]
    [SerializeField] private LayerMask groundLayer;

    [Tooltip("Minimum free radius around the spawned pickup")]
    [SerializeField] private float minClearance = 0.5f;

    [Tooltip("Layers that the pickup must NOT overlap with")]
    [SerializeField] private LayerMask forbiddenOverlapLayers;

    [Tooltip("Max attempts to find valid spawn position")]
    [SerializeField] private int maxTries = 10;

    [Header("Backup Spawn (when no pickups for a long time)")]
    [Tooltip("If true, spawn a backup pickup if no pickup was spawned for a long time.")]
    [SerializeField] private bool enableBackupSpawn = true;

    [Tooltip("Time in seconds without any pickup spawn before a backup pickup appears.")]
    [SerializeField] private float backupSpawnDelaySeconds = 12f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    private float _lastPickupSpawnTime = -1f; // Time when last pickup was spawned (-1 = never)
    private bool _backupSpawned = false; // Track if we already spawned a backup pickup

    private void Awake()
    {
        Debug.Log($"[PickupRespawner] Awake called. GameObject: {gameObject.name}, Active: {gameObject.activeInHierarchy}", this);
    }

    private void Start()
    {
        Debug.Log($"[PickupRespawner] Start called. spawnOnEnemyDeath: {spawnOnEnemyDeath}, pickupPrefab: {pickupPrefab?.name ?? "NULL"}", this);
        
        // Initialize last spawn time to current time so backup doesn't trigger immediately
        if (enableBackupSpawn && spawnOnEnemyDeath)
        {
            _lastPickupSpawnTime = Time.time;
            if (debugLogs)
                Debug.Log($"[PickupRespawner] Backup spawn enabled. Will spawn backup pickup after {backupSpawnDelaySeconds} seconds without any pickup.", this);
        }
    }

    private void Update()
    {
        // Only check for backup spawn if enabled and spawning on enemy death
        if (!enableBackupSpawn || !spawnOnEnemyDeath || _backupSpawned)
            return;

        if (_lastPickupSpawnTime < 0f)
            return; // No pickups spawned yet, wait

        float timeSinceLastSpawn = Time.time - _lastPickupSpawnTime;
        
        if (timeSinceLastSpawn >= backupSpawnDelaySeconds)
        {
            SpawnBackupPickup();
            _backupSpawned = true; // Only spawn one backup pickup
        }
    }

    private void OnEnable()
    {
        Debug.Log($"[PickupRespawner] OnEnable called. spawnOnEnemyDeath: {spawnOnEnemyDeath}", this);
        
        if (spawnOnEnemyDeath)
        {
            EventManager.StartListening(EventManager.GameEvent.EntityDied, OnEntityDied);
            Debug.Log("[PickupRespawner] ✓ Started listening to EntityDied events.", this);
        }
        else
        {
            EventManager.StartListening(EventManager.GameEvent.PickupCollected, OnPickupCollected);
            Debug.Log("[PickupRespawner] Started listening to PickupCollected events.", this);
        }
    }

    private void OnDisable()
    {
        if (spawnOnEnemyDeath)
        {
            EventManager.StopListening(EventManager.GameEvent.EntityDied, OnEntityDied);
        }
        else
        {
            EventManager.StopListening(EventManager.GameEvent.PickupCollected, OnPickupCollected);
        }
    }

    private void OnEntityDied(object eventData)
    {
        Debug.Log($"[PickupRespawner] OnEntityDied called! eventData type: {eventData?.GetType().Name}", this);
        
        if (eventData is not EntityDiedEventData e)
        {
            Debug.LogWarning($"[PickupRespawner] Event data is not EntityDiedEventData! Type: {eventData?.GetType().Name}", this);
            return;
        }

        Debug.Log($"[PickupRespawner] Received EntityDied event. Victim: {e.Victim?.name ?? "NULL"}, Layer: {e.VictimLayer}", this);

        // Check if it's an enemy by looking for EnemyHealth component
        if (e.Victim == null)
        {
            Debug.LogWarning("[PickupRespawner] Victim is null!", this);
            return;
        }

        // Check if it's an enemy by layer
        int layer = e.VictimLayer;
        
        // Check layer filter - if enemyLayers is set, only spawn for enemies on those layers
        if (enemyLayers.value != 0 && (enemyLayers.value & (1 << layer)) == 0)
        {
            Debug.Log($"[PickupRespawner] Entity {e.Victim.name} died on layer {layer}, but not in enemyLayers filter. Ignoring.", this);
            return;
        }

        if (pickupPrefab == null)
        {
            Debug.LogError("[PickupRespawner] pickupPrefab is not set. Cannot spawn pickup.", this);
            return;
        }

        // Get the death position from the victim (before it's destroyed)
        Vector3 deathPosition = e.Victim != null ? e.Victim.transform.position : Vector3.zero;
        
        if (e.Victim == null)
        {
            Debug.LogWarning("[PickupRespawner] Victim is null, cannot get death position!", this);
            return;
        }

        Debug.Log($"[PickupRespawner] ✓ Enemy {e.Victim.name} died at {deathPosition}. Spawning pickup...", this);

        // Update last spawn time for backup spawn tracking
        _lastPickupSpawnTime = Time.time;
        _backupSpawned = false; // Reset backup flag when normal spawn happens

        // Spawn pickup at death position (with optional delay)
        StartCoroutine(SpawnPickupAtPosition(deathPosition));
    }

    private void OnPickupCollected(object _)
    {
        if (pickupPrefab == null)
        {
            Debug.LogWarning("PickupRespawner: pickupPrefab is not set.");
            return;
        }
        
        SoundManager.Instance.PlaySound("RechargeCollect", transform);

        // Use the old random spawn logic on map
        StartCoroutine(RespawnAfterDelay());
    }

    private IEnumerator SpawnPickupAtPosition(Vector3 deathPosition)
    {
        if (respawnDelaySeconds > 0f)
        {
            Debug.Log($"[PickupRespawner] Waiting {respawnDelaySeconds} seconds before spawning...", this);
            yield return new WaitForSeconds(respawnDelaySeconds);
        }

        // Spawn pickup slightly above the death position
        Vector3 spawnPosition = deathPosition + Vector3.up * spawnHeightOffset;
        
        Transform parent = parentToSpawner ? transform : null;
        GameObject spawned = Instantiate(pickupPrefab, spawnPosition, Quaternion.identity, parent);
        
        Debug.Log($"[PickupRespawner] ✓ SUCCESS! Spawned pickup '{spawned.name}' at {spawnPosition} (enemy died at {deathPosition})", this);
    }

    private IEnumerator RespawnAfterDelay()
    {
        yield return new WaitForSeconds(respawnDelaySeconds);

        if (TryGetSpawnPosition(out Vector3 pos))
        {
            Transform parent = parentToSpawner ? transform : null;
            GameObject spawned = Instantiate(pickupPrefab, pos, Quaternion.identity, parent);
            
            if (debugLogs)
                Debug.Log($"[PickupRespawner] ✓ Spawned pickup '{spawned.name}' at random position {pos}", this);
        }
        else
        {
            Debug.LogWarning("[PickupRespawner] Failed to find valid spawn position.", this);
        }
    }

    /// <summary>
    /// Tries to find a valid spawn position on the map using the old working logic.
    /// </summary>
    private bool TryGetSpawnPosition(out Vector3 pos)
    {
        for (int i = 0; i < maxTries; i++)
        {
            // Generate random X, Z within map bounds
            float x = Random.Range(center.x - halfSize.x, center.x + halfSize.x);
            float z = Random.Range(center.y - halfSize.y, center.y + halfSize.y);
            Vector3 rayStart = new Vector3(x, spawnHeight, z);

            // 1) Find ground using raycast
            if (!Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, spawnHeight * 2f, groundLayer))
            {
                if (debugLogs && i == 0)
                    Debug.Log($"[PickupRespawner] Raycast from {rayStart} did not hit ground layer {groundLayer.value}.", this);
                continue;
            }

            Vector3 candidatePos = hit.point + Vector3.up * minClearance;

            // 2) Check overlap against forbidden layers
            bool blocked = Physics.CheckSphere(
                candidatePos,
                minClearance,
                forbiddenOverlapLayers,
                QueryTriggerInteraction.Ignore
            );

            if (blocked)
            {
                if (debugLogs && i == 0)
                    Debug.Log($"[PickupRespawner] Position {candidatePos} is blocked by forbidden layers.", this);
                continue;
            }

            pos = candidatePos;
            if (debugLogs)
                Debug.Log($"[PickupRespawner] ✓ Found valid spawn position at {pos} (attempt {i + 1})", this);
            return true;
        }

        pos = Vector3.zero;
        if (debugLogs)
            Debug.LogWarning($"[PickupRespawner] Could not find valid spawn position after {maxTries} attempts.", this);
        return false;
    }

    private void SpawnBackupPickup()
    {
        if (pickupPrefab == null)
        {
            Debug.LogError("[PickupRespawner] Cannot spawn backup pickup: pickupPrefab is not set.", this);
            return;
        }

        // Use the same random spawn logic as old behavior
        if (TryGetSpawnPosition(out Vector3 pos))
        {
            Transform parent = parentToSpawner ? transform : null;
            GameObject spawned = Instantiate(pickupPrefab, pos, Quaternion.identity, parent);
            
            Debug.Log($"[PickupRespawner] ✓ BACKUP pickup spawned: '{spawned.name}' at {pos} (no pickup for {Time.time - _lastPickupSpawnTime:F1}s)", this);
            
            // Update last spawn time so we don't immediately spawn another backup
            _lastPickupSpawnTime = Time.time;
        }
        else
        {
            Debug.LogWarning("[PickupRespawner] Failed to spawn backup pickup - could not find valid position.", this);
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // Draw map bounds for visualization
        Gizmos.color = new Color(1f, 0f, 0f, 0.25f);
        Gizmos.DrawWireCube(
            new Vector3(center.x, 0f, center.y),
            new Vector3(halfSize.x * 2f, 0.1f, halfSize.y * 2f)
        );
    }
#endif

}
