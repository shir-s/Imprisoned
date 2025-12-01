// FILEPATH: Assets/Scripts/Gameplay/KeyItem.cs
using UnityEngine;

[DisallowMultipleComponent]
public class KeyItem : MonoBehaviour
{
    [Header("Collision")]
    [Tooltip("Only these layers can pick up the key.")]
    [SerializeField] private LayerMask collectorMask;

    [Header("Events")]
    [Tooltip("Event to trigger when the key is collected.")]
    [SerializeField] private EventManager.GameEvent eventToTrigger = EventManager.GameEvent.KeyCollected;

    private void Reset()
    {
        // Auto-guess default layer mask: player cube is usually on Default or Player
        collectorMask = ~0; // All layers (you can change in inspector)
    }

    private void OnTriggerEnter(Collider other)
    {
        if ((collectorMask.value & (1 << other.gameObject.layer)) == 0)
            return; // wrong layer -> ignore

        // Trigger event with reference to this key
        EventManager.TriggerEvent(eventToTrigger, this.transform);

        // Destroy key
        Destroy(gameObject);
    }
}