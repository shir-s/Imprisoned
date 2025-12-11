using UnityEngine;

namespace Archive.Gameplay.Cubes
{
    /// <summary>
    /// Put this on the collectible object that sits on the tray (like a coin).
    /// When the painting cube touches this pickup, it:
    /// - Asks CubeStackManager to add a new cube from 'cubePrefab'.
    /// - Optionally destroys the pickup (controlled by 'destroyOnCollect').
    /// 
    /// Usage:
    /// - The pickup object must have a Collider with 'isTrigger = true'.
    /// - The painting cube must be on a layer included in 'collectorLayers'.
    /// - 'cubePrefab' is one of your duplicated cube prefabs.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class CubePickup : MonoBehaviour
    {
        [Tooltip("The cube prefab the player receives when this pickup is collected.")]
        [SerializeField] private GameObject cubePrefab;

        [Tooltip("Layers allowed to collect this pickup (should include the painting cube layer).")]
        [SerializeField] private LayerMask collectorLayers = ~0;

        [Tooltip("If true, this pickup is destroyed after being collected.")]
        [SerializeField] private bool destroyOnCollect = true;

        private Collider _col;

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
            // Check if allowed collector
            if ((collectorLayers.value & (1 << other.gameObject.layer)) == 0)
                return;

            // Check stack manager
            if (CubeStackManager.Instance == null)
            {
                Debug.LogWarning("[CubePickup] No CubeStackManager in scene, cannot give cube.");
                return;
            }

            // Give cube
            CubeStackManager.Instance.AddCubeFromPickup(cubePrefab);

            // Destroy only if allowed
            if (destroyOnCollect)
                Destroy(gameObject);
        }
    }
}