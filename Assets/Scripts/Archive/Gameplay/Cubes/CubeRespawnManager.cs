// FILEPATH: Assets/Scripts/Gameplay/CubeRespawnManager.cs
using UnityEngine;
using System.Collections;
using JellyGame.GamePlay.Managers;

[DisallowMultipleComponent]
public class CubeRespawnManager : MonoBehaviour
{
    [Header("Prefab & Spawn")]
    [Tooltip("List of cube prefabs to randomly choose from each respawn.")]
    [SerializeField] private GameObject[] cubePrefabs;
    [SerializeField] private Transform spawnPoint;

    [Header("Safe Spawn Settings")]
    [Tooltip("Alternative spawn points if primary is blocked.")]
    [SerializeField] private Transform[] alternativeSpawnPoints;
    [Tooltip("Radius to check for enemies around spawn point.")]
    [SerializeField] private float spawnCheckRadius = 1.5f;
    [Tooltip("Layer mask for enemies/hazards to avoid.")]
    [SerializeField] private LayerMask hazardLayers;
    [Tooltip("Max time to wait for clear spawn before forcing spawn anyway.")]
    [SerializeField] private float maxWaitTime = 3f;
    [Tooltip("How often to recheck if spawn is clear.")]
    [SerializeField] private float recheckInterval = 0.1f;

    [Header("Spawn Delay")]
    [Tooltip("Delay in seconds before spawning a new cube after the previous one is destroyed.")]
    [SerializeField] private float respawnDelay = 0.3f;

    [Header("Events")]
    [SerializeField] private bool useEvents = true;

    [Header("Debug")]
    [SerializeField] private bool logRespawns = false;
    [SerializeField] private bool drawGizmos = true;

    private GameObject _currentCube;
    private bool _isRespawning = false;

    private void Start()
    {
        if (!spawnPoint)
            spawnPoint = transform;

        SpawnNewCube();
    }

    private void Update()
    {
        if (_currentCube == null && !_isRespawning)
        {
            StartCoroutine(RespawnRoutine());
        }
    }

    private IEnumerator RespawnRoutine()
    {
        _isRespawning = true;

        if (respawnDelay > 0f)
            yield return new WaitForSeconds(respawnDelay);

        // Try to find a safe spawn point
        Transform safeSpawn = FindSafeSpawnPoint();
        
        if (safeSpawn != null)
        {
            SpawnNewCube(safeSpawn);
        }
        else
        {
            // No safe point found, wait for primary to clear (with timeout)
            yield return StartCoroutine(WaitForClearSpawn());
        }

        _isRespawning = false;
    }

    private Transform FindSafeSpawnPoint()
    {
        // Check primary spawn point first
        if (IsSpawnPointClear(spawnPoint))
            return spawnPoint;

        // Check alternative spawn points
        if (alternativeSpawnPoints != null)
        {
            foreach (var altSpawn in alternativeSpawnPoints)
            {
                if (altSpawn != null && IsSpawnPointClear(altSpawn))
                    return altSpawn;
            }
        }

        return null;
    }

    private bool IsSpawnPointClear(Transform point)
    {
        Collider[] hits = Physics.OverlapSphere(point.position, spawnCheckRadius, hazardLayers);
        return hits.Length == 0;
    }

    private IEnumerator WaitForClearSpawn()
    {
        float waitedTime = 0f;

        while (waitedTime < maxWaitTime)
        {
            Transform safeSpawn = FindSafeSpawnPoint();
            
            if (safeSpawn != null)
            {
                SpawnNewCube(safeSpawn);
                yield break;
            }

            yield return new WaitForSeconds(recheckInterval);
            waitedTime += recheckInterval;
        }

        // Timeout reached - spawn anyway at primary point
        // (or you could spawn with temporary invincibility instead)
        if (logRespawns)
            Debug.LogWarning("[CubeRespawnManager] Spawn timeout - forcing spawn at primary point.", this);
        
        SpawnNewCube(spawnPoint);
    }

    private void SpawnNewCube(Transform spawnTransform)
    {
        if (cubePrefabs == null || cubePrefabs.Length == 0)
        {
            Debug.LogError("[CubeRespawnManager] No cubePrefab assigned.", this);
            return;
        }

        int index = Random.Range(0, cubePrefabs.Length);
        GameObject prefabToSpawn = cubePrefabs[index];

        _currentCube = Instantiate(prefabToSpawn, spawnTransform.position, spawnTransform.rotation);

        if (logRespawns)
            Debug.Log($"[CubeRespawnManager] Spawned new cube at {spawnTransform.name}.", this);

        if (useEvents)
        {
            EventManager.TriggerEvent(EventManager.GameEvent.CubeRespawned, _currentCube);
            EventManager.TriggerEvent(EventManager.GameEvent.ActiveCubeChanged, _currentCube);
            EventManager.TriggerEvent(EventManager.GameEvent.CubeRespawnSound);
        }
    }

    // Keep the original overload for the initial spawn
    private void SpawnNewCube() => SpawnNewCube(spawnPoint);

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        // Draw primary spawn check radius
        Gizmos.color = Color.green;
        if (spawnPoint != null)
            Gizmos.DrawWireSphere(spawnPoint.position, spawnCheckRadius);

        // Draw alternative spawn points
        Gizmos.color = Color.yellow;
        if (alternativeSpawnPoints != null)
        {
            foreach (var alt in alternativeSpawnPoints)
            {
                if (alt != null)
                    Gizmos.DrawWireSphere(alt.position, spawnCheckRadius);
            }
        }
    }
}