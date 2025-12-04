using UnityEngine;

/// <summary>
/// Pickup cube that grants the player the Stickiness ability when touched.
/// Automatically adds a BoxCollider if none exists.
/// </summary>
public class StickinessPickupCube : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("If true, the cube will be destroyed after being collected")]
    [SerializeField] private bool destroyOnPickup = true;

    [Tooltip("Visual effect when collected (optional particle system, etc.)")]
    [SerializeField] private GameObject collectEffectPrefab;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    private bool _hasBeenCollected = false;

    private void Awake()
    {
        // Ensure we have a collider (add one if missing)
        Collider col = GetComponent<Collider>();
        if (col == null)
        {
            // Add a BoxCollider by default
            BoxCollider boxCol = gameObject.AddComponent<BoxCollider>();
            boxCol.isTrigger = true;
            if (debugLogs)
                Debug.Log($"[StickinessPickupCube] Added BoxCollider to {gameObject.name}", this);
        }
        else
        {
            // Ensure existing collider is a trigger
            if (!col.isTrigger)
            {
                if (debugLogs)
                    Debug.LogWarning($"[StickinessPickupCube] Collider on {gameObject.name} is not a trigger! Setting it to trigger.", this);
                col.isTrigger = true;
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_hasBeenCollected)
            return;

        // Check if the collider belongs to the player
        // You might need to adjust this check based on your player setup
        if (IsPlayer(other))
        {
            CollectPickup();
        }
    }

    private bool IsPlayer(Collider col)
    {
        // Check if it's the player cube
        // Adjust these checks based on your player GameObject structure
        if (col.CompareTag("Player"))
            return true;

        // Check for player components
        if (col.GetComponent<CubePlayerKeyboardController>() != null)
            return true;

        if (col.GetComponentInParent<CubePlayerKeyboardController>() != null)
            return true;

        // Check for PlayerAbilityManager (should be on player)
        if (col.GetComponent<PlayerAbilityManager>() != null)
            return true;

        if (col.GetComponentInParent<PlayerAbilityManager>() != null)
            return true;

        return false;
    }

    private void CollectPickup()
    {
        if (_hasBeenCollected)
            return;

        _hasBeenCollected = true;

        // Grant stickiness ability
        if (PlayerAbilityManager.Instance != null)
        {
            PlayerAbilityManager.Instance.UnlockStickiness();
            
            if (debugLogs)
                Debug.Log($"[StickinessPickupCube] Player collected stickiness pickup! Ability unlocked.", this);
        }
        else
        {
            Debug.LogError("[StickinessPickupCube] PlayerAbilityManager.Instance is null! Make sure PlayerAbilityManager exists in the scene.", this);
        }

        // Spawn collect effect if assigned
        if (collectEffectPrefab != null)
        {
            Instantiate(collectEffectPrefab, transform.position, Quaternion.identity);
        }

        // Destroy the cube
        if (destroyOnPickup)
        {
            Destroy(gameObject);
        }
        else
        {
            // Just disable the visual/collider if we want to keep it for some reason
            GetComponent<Collider>().enabled = false;
            Renderer renderer = GetComponent<Renderer>();
            if (renderer != null)
                renderer.enabled = false;
        }
    }
}

