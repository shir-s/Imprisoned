// FILEPATH: Assets/Scripts/Gameplay/CubeRespawnManager.cs
using UnityEngine;

/// <summary>
/// Responsible ONLY for ensuring there is always an active cube:
/// - Spawns a cube at start.
/// - If the current cube gets destroyed (by wear, fall catcher, etc.), it respawns a new one.
/// 
/// It does NOT check position, does NOT detect falling, and does NOT handle wear.
/// That logic lives in:
/// - WearWhenMovingScaler  -> destroys cube when fully worn.
/// - CubeFallCatcher       -> destroys cube when it falls out of the surface.
/// 
/// EXPECTED SETUP:
/// - Put this on an empty GameObject in the scene (e.g. "CubeRespawnManager").
/// - Assign:
///     * cubePrefab  = prefab that has all your cube logic (Rigidbody, MovementPaintController,
///                     WearWhenMovingScaler, ExtraDownForce, etc.).
///     * spawnPoint  = Transform that marks where the cube should appear (position + rotation).
/// </summary>
[DisallowMultipleComponent]
public class CubeRespawnManager : MonoBehaviour
{
    [Header("Prefab & Spawn")]
    [Tooltip("Prefab of the cube (must contain all movement / painting / wear scripts).")]
    [SerializeField] private GameObject cubePrefab;

    [Tooltip("Where to spawn the cube. If null, uses this GameObject's transform.")]
    [SerializeField] private Transform spawnPoint;

    [Header("Events")]
    [Tooltip("If true, will fire EventManager events when cubes are (re)spawned.")]
    [SerializeField] private bool useEvents = true;

    [Header("Debug")]
    [SerializeField] private bool logRespawns = false;

    // --- runtime ---
    private GameObject _currentCube;

    private void Start()
    {
        if (!spawnPoint)
            spawnPoint = transform;

        SpawnNewCube();
    }

    private void Update()
    {
        // If cube was destroyed by other systems (wear, fall catcher, etc.), spawn a new one.
        if (_currentCube == null)
        {
            SpawnNewCube();
        }
    }

    // ----------------------
    // Spawn helper
    // ----------------------

    private void SpawnNewCube()
    {
        if (cubePrefab == null)
        {
            Debug.LogError("[CubeRespawnManager] No cubePrefab assigned.", this);
            return;
        }

        _currentCube = Instantiate(cubePrefab, spawnPoint.position, spawnPoint.rotation);

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
