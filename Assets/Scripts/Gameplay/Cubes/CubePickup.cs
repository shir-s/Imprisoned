using UnityEngine;

/// <summary>
/// Put this on the collectible object that sits on the tray (like a coin).
/// When the painting cube touches this pickup, it:
/// - Asks CubeStackManager to add a new cube from 'cubePrefab'.
/// - Destroys the pickup.
/// 
/// Usage:
/// - The pickup object must have a Collider with 'isTrigger = true'.
/// - The painting cube must be on a layer included in 'collectorLayers'.
/// - 'cubePrefab' is one of your duplicated cube prefabs (with its own material,
///   WearWhenMovingScaler settings, PhysicMaterial etc.).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class CubePickup : MonoBehaviour
{
    [Tooltip("The cube prefab the player receives when this pickup is collected.")]
    [SerializeField] private GameObject cubePrefab;

    [Tooltip("Layers allowed to collect this pickup (should include the painting cube layer).")]
    [SerializeField] private LayerMask collectorLayers = ~0;

    Collider _col;

    void Reset()
    {
        _col = GetComponent<Collider>();
        _col.isTrigger = true;
    }

    void Awake()
    {
        _col = GetComponent<Collider>();
        if (_col != null)
            _col.isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if ((collectorLayers.value & (1 << other.gameObject.layer)) == 0)
            return; // not an allowed collector

        if (CubeStackManager.Instance == null)
        {
            Debug.LogWarning("[CubePickup] No CubeStackManager in scene, cannot give cube.");
            return;
        }

        // We don't care WHICH cube it is exactly; we just give a cube to the stack.
        CubeStackManager.Instance.AddCubeFromPickup(cubePrefab);

        Destroy(gameObject);
    }
}