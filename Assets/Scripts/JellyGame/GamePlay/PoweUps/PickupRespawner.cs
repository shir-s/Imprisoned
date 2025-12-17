using UnityEngine;
using JellyGame.GamePlay.Managers;

public class PickupRespawner : MonoBehaviour
{
    [Header("What to spawn")]
    [SerializeField] private GameObject pickupPrefab;

    [Header("Map bounds (world space)")]
    [SerializeField] private Vector2 center = Vector2.zero;          // XZ center of your map
    [SerializeField] private Vector2 halfSize = new Vector2(20f, 20f); // half extents in XZ

    [Header("Placement")]
    [SerializeField] private float spawnHeight = 10f; // cast down from this height
    [SerializeField] private LayerMask groundLayer;   // set to your floor/terrain layer
    [SerializeField] private float minClearance = 0.5f; // height above hit point
    [SerializeField] private int maxTries = 10;

    private void OnEnable()
    {
        EventManager.StartListening(EventManager.GameEvent.PickupCollected, OnPickupCollected);
    }

    private void OnDisable()
    {
        EventManager.StopListening(EventManager.GameEvent.PickupCollected, OnPickupCollected);
    }

    private void OnPickupCollected(object _)
    {
        if (pickupPrefab == null)
        {
            Debug.LogWarning("PickupRespawner: pickupPrefab is not set.");
            return;
        }

        if (TryGetSpawnPosition(out Vector3 pos))
        {
            Instantiate(pickupPrefab, pos, Quaternion.identity);
        }
        else
        {
            Debug.LogWarning("PickupRespawner: failed to find spawn position.");
        }
    }

    private bool TryGetSpawnPosition(out Vector3 pos)
    {
        for (int i = 0; i < maxTries; i++)
        {
            float x = Random.Range(center.x - halfSize.x, center.x + halfSize.x);
            float z = Random.Range(center.y - halfSize.y, center.y + halfSize.y);
            Vector3 start = new Vector3(x, spawnHeight, z);

            if (Physics.Raycast(start, Vector3.down, out RaycastHit hit, spawnHeight * 2f, groundLayer))
            {
                pos = hit.point + Vector3.up * minClearance;
                return true;
            }
        }

        pos = Vector3.zero;
        return false;
    }
}