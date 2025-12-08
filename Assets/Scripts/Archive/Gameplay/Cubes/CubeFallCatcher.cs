// FILEPATH: Assets/Scripts/PhysicsDrawing/CubeFallCatcher.cs

using JellyGame.GamePlay.Managers;
using UnityEngine;

/// <summary>
/// Simple kill-volume placed under the tray / surface.
/// When a rigidbody (your cube) enters this trigger:
/// - Optionally fires EventManager.GameEvent.CubeDestroyed with the cube GameObject.
/// - Destroys the cube GameObject.
///
/// Place this as a big trigger collider under the drawing surface.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class CubeFallCatcher : MonoBehaviour
{
    [Header("Filter")]
    [Tooltip("Only destroy objects whose layer is included here.")]
    [SerializeField] private LayerMask cubeLayers = ~0; // default: everything

    [Tooltip("If not empty, only destroy objects with this tag. Leave empty to ignore tag.")]
    [SerializeField] private string requiredTag = "";

    [Header("Events")]
    [Tooltip("If true, will fire EventManager.GameEvent.CubeDestroyed with the hit GameObject.")]
    [SerializeField] private bool useEvents = true;

    [Header("Debug")]
    [SerializeField] private bool logDestroy = false;

    private void Reset()
    {
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void Awake()
    {
        var col = GetComponent<Collider>();
        if (col != null && !col.isTrigger)
        {
            col.isTrigger = true;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // Only care about things with a Rigidbody (your cube should have one).
        Rigidbody rb = other.attachedRigidbody;
        if (rb == null)
            return;

        GameObject go = rb.gameObject;

        // Layer filter
        int layerBit = 1 << go.layer;
        if ((cubeLayers.value & layerBit) == 0)
            return;

        // Optional tag filter
        if (!string.IsNullOrEmpty(requiredTag) && !go.CompareTag(requiredTag))
            return;

        if (logDestroy)
        {
            Debug.Log($"[CubeFallCatcher] Destroying object that entered kill volume: {go.name}", this);
        }

        if (useEvents)
        {
            EventManager.TriggerEvent(EventManager.GameEvent.CubeDestroyed, go);
        }

        Destroy(go);
    }
}
