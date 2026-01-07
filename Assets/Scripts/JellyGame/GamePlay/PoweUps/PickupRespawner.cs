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

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    private void Awake()
    {
        Debug.Log($"[PickupRespawner] Awake called. GameObject: {gameObject.name}, Active: {gameObject.activeInHierarchy}", this);
    }

    private void Start()
    {
        Debug.Log($"[PickupRespawner] Start called. spawnOnEnemyDeath: {spawnOnEnemyDeath}, pickupPrefab: {pickupPrefab?.name ?? "NULL"}", this);
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

        // For old behavior (spawn when pickup collected), spawn at a random position near the spawner
        // This is a simple fallback - you might want to implement proper random spawn logic if needed
        Vector3 randomPosition = transform.position + new Vector3(
            Random.Range(-5f, 5f),
            spawnHeightOffset,
            Random.Range(-5f, 5f)
        );
        
        StartCoroutine(SpawnPickupAtPosition(randomPosition));
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

}
