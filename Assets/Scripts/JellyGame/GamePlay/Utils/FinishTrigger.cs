// FILEPATH: Assets/Scripts/World/Finish/FinishTrigger.cs

using System.Collections;
using JellyGame.GamePlay.Audio.Core;
using JellyGame.GamePlay.Managers;
using UnityEngine;

namespace JellyGame.GamePlay.World.Finish
{
    /// <summary>
    /// Triggers GameWin when player enters the trigger AND all enemies are dead.
    /// Uses the same logic as DoorByDeaths - listens to EventManager.GameEvent.EntityDied.
    ///
    /// Usage:
    /// - Attach to a GameObject with a Collider set as Trigger.
    /// - Set Allowed Layers to the player layer.
    /// - Set Count Layers to the enemy layers you want to track.
    /// - Set Required Deaths to the number of enemies that must die.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class FinishTrigger : MonoBehaviour
    {
        [Header("Trigger")]
        [Tooltip("Only objects on these layers can trigger GameWin. If 0 (Nothing), will accept all layers.")]
        [SerializeField] private LayerMask allowedLayers;

        [Tooltip("If true, trigger only once.")]
        [SerializeField] private bool triggerOnce = true;

        [Header("Win Condition")]
        [Tooltip("Only deaths on these layers will be counted (like DoorByDeaths).")]
        [SerializeField] private LayerMask countLayers = ~0;

        [Tooltip("Number of enemy deaths required before trigger can activate GameWin.")]
        [SerializeField] private int requiredDeaths = 4;

        [Header("Finish Visuals")]
        [SerializeField] private Renderer meshOnThisObject;
        [SerializeField] private Renderer meshOnChildObject;
        
        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private bool _triggered;
        private int _deathCount = 0;

        private void Reset()
        {
            Collider c = GetComponent<Collider>();
            if (c != null)
                c.isTrigger = true;
        }
        
        private void UpdateFinishMeshes(bool enabled)
        {
            if (meshOnThisObject != null)
                meshOnThisObject.enabled = enabled;

            if (meshOnChildObject != null)
                meshOnChildObject.enabled = enabled;
        }


        private void Awake()
        {
            EnsureKinematicRigidbody();

            if (requiredDeaths < 1)
                requiredDeaths = 1;

            // NEW: hide meshes until win condition is met
            UpdateFinishMeshes(_deathCount >= requiredDeaths);

            if (debugLogs)
            {
                Debug.Log(
                    $"[FinishTrigger] Initialized. Required Deaths: {requiredDeaths}, Count Layers: {countLayers.value}, Allowed Layers: {allowedLayers.value}",
                    this
                );
            }
        }

        private void OnEnable()
        {
            // Listen to EntityDied event (same as DoorByDeaths)
            EventManager.StartListening(EventManager.GameEvent.EntityDied, OnEntityDied);

            // NEW: ensure correct visual state if object was disabled/enabled
            UpdateFinishMeshes(_deathCount >= requiredDeaths);
        }
        
        
        private void OnDisable()
        {
            // Unsubscribe to prevent memory leaks
            EventManager.StopListening(EventManager.GameEvent.EntityDied, OnEntityDied);
        }

        private void OnEntityDied(object eventData)
        {
            if (_triggered)
                return;

            if (eventData is not EntityDiedEventData e)
                return;

            int layer = e.VictimLayer;

            // Only count deaths on specified layers
            if ((countLayers.value & (1 << layer)) == 0)
                return;

            _deathCount++;

            if (debugLogs)
                Debug.Log($"[FinishTrigger] Counted death {_deathCount}/{requiredDeaths} (layer={layer})", this);

            // NEW: when condition met, show meshes (and keep them shown)
            if (_deathCount >= requiredDeaths)
                UpdateFinishMeshes(true);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (debugLogs)
                Debug.Log($"[FinishTrigger] OnTriggerEnter called by: {other.name} (Layer: {other.gameObject.layer})", this);
            
            if (_triggered && triggerOnce)
            {
                if (debugLogs)
                    Debug.Log("[FinishTrigger] Already triggered and triggerOnce is true. Ignoring.", this);
                return;
            }

            int layer = other.gameObject.layer;
            // If allowedLayers is 0 (Nothing), accept all layers. Otherwise check if layer is in mask.
            if (allowedLayers.value != 0 && (allowedLayers.value & (1 << layer)) == 0)
            {
                if (debugLogs)
                    Debug.Log($"[FinishTrigger] Object {other.name} on layer {layer} is not in allowed layers ({allowedLayers.value}). Ignoring.", this);
                return;
            }

            // Check if all required enemies are dead
            if (_deathCount < requiredDeaths)
            {
                if (debugLogs)
                    Debug.Log($"[FinishTrigger] Player entered trigger, but only {_deathCount}/{requiredDeaths} enemies are dead. Waiting...", this);
                return;
            }

            _triggered = true;

            if (debugLogs)
                Debug.Log($"[FinishTrigger] ✓ GameWin triggered by {other.name} ({_deathCount}/{requiredDeaths} enemies dead)", this);
            
            SoundManager.Instance.StopAllSounds();
            SoundManager.Instance.PlaySound("Win", this.transform);
            
            StartCoroutine(GameWinEvent(other));
        }

        private IEnumerator GameWinEvent(Collider other)
        {
            yield return new WaitForSeconds(0);

            EventManager.TriggerEvent(EventManager.GameEvent.GameWin, other.gameObject);
        }

        private void EnsureKinematicRigidbody()
        {
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb == null)
                rb = gameObject.AddComponent<Rigidbody>();

            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.None;
        }
    }
}