// FILEPATH: Assets/Scripts/Gameplay/CubeRespawnManager.cs
using UnityEngine;
using System.Collections;

[DisallowMultipleComponent]
public class CubeRespawnManager : MonoBehaviour
{
    [Header("Prefab & Spawn")]
    [Tooltip("List of cube prefabs to randomly choose from each respawn.")]
    [SerializeField] private GameObject[] cubePrefabs;
    [SerializeField] private Transform spawnPoint;

    [Header("Spawn Delay")]
    [Tooltip("Delay in seconds before spawning a new cube after the previous one is destroyed.")]
    [SerializeField] private float respawnDelay = 0.3f;

    [Header("Events")]
    [SerializeField] private bool useEvents = true;

    [Header("Debug")]
    [SerializeField] private bool logRespawns = false;

    // --- runtime ---
    private GameObject _currentCube;
    private bool _isRespawning = false;

    private void Start()
    {
        if (!spawnPoint)
            spawnPoint = transform;

        SpawnNewCube(); // First spawn without delay
    }

    private void Update()
    {
        // If the cube was destroyed by other systems, start respawn coroutine.
        if (_currentCube == null && !_isRespawning)
        {
            StartCoroutine(RespawnRoutine());
        }
    }

    // ----------------------
    // Respawn with delay
    // ----------------------
    private IEnumerator RespawnRoutine()
    {
        _isRespawning = true;

        if (respawnDelay > 0f)
            yield return new WaitForSeconds(respawnDelay);

        SpawnNewCube();
        _isRespawning = false;
    }

    // ----------------------
    // Spawn helper
    // ----------------------
    private void SpawnNewCube()
    {
        if (cubePrefabs == null || cubePrefabs.Length == 0)
        {
            Debug.LogError("[CubeRespawnManager] No cubePrefab assigned.", this);
            return;
        }
        
        int index = Random.Range(0, cubePrefabs.Length);
        GameObject prefabToSpawn = cubePrefabs[index];
        
        _currentCube = Instantiate(prefabToSpawn, spawnPoint.position, spawnPoint.rotation);
        
        if (logRespawns)
            Debug.Log("[CubeRespawnManager] Spawned new cube.", this);

        if (useEvents)
        {
            EventManager.TriggerEvent(EventManager.GameEvent.CubeRespawned, _currentCube);
            EventManager.TriggerEvent(EventManager.GameEvent.ActiveCubeChanged, _currentCube);
            EventManager.TriggerEvent(EventManager.GameEvent.CubeRespawnSound);
        }
    }
}
