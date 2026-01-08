using JellyGame.GamePlay.Managers;
using UnityEngine;
using UnityEngine.Events;

namespace JellyGame.GamePlay.Utils
{
    /// <summary>
    /// Simple script that counts enemy deaths and activates a GameObject when all enemies die.
    /// 
    /// How to use:
    /// 1. Set Required Deaths to the number of enemies (e.g., 5)
    /// 2. Set Count Layers to the enemy layers (e.g., "Enemy")
    /// 3. Drag the GameObject you want to activate to Activate Game Object
    /// 
    /// How it works:
    /// - Listens to EntityDied events from EventManager
    /// - Counts deaths for enemies on the specified layers
    /// - When death count >= required deaths → activates GameObject and calls UnityEvent
    /// </summary>
    [DisallowMultipleComponent]
    public class EnemyDeathCounter : MonoBehaviour
    {
        [Header("Enemy Death Tracking")]
        [Tooltip("Number of enemy deaths required before activating the GameObject.")]
        [SerializeField] private int requiredDeaths = 5;

        [Tooltip("Only deaths on these layers will be counted (e.g., Enemy layer).")]
        [SerializeField] private LayerMask countLayers = ~0;

        [Header("Actions When All Enemies Die")]
        [Tooltip("GameObject to activate when all enemies die. Leave null to skip.")]
        [SerializeField] private GameObject activateGameObject;

        [Tooltip("If true, deactivate the GameObject at start (ensures it's hidden initially).")]
        [SerializeField] private bool deactivateAtStart = true;

        [Tooltip("UnityEvent that will be called when all enemies die. You can assign multiple actions here.")]
        [SerializeField] private UnityEvent onAllEnemiesDied;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private int _deathCount = 0;
        private bool _allDead = false;

        private void Awake()
        {
            if (requiredDeaths < 1)
                requiredDeaths = 1;

            // Deactivate target GameObject at start if requested
            if (activateGameObject != null && deactivateAtStart)
            {
                activateGameObject.SetActive(false);
                if (debugLogs)
                    Debug.Log($"[EnemyDeathCounter] Deactivated {activateGameObject.name} at start.", this);
            }

            if (debugLogs)
            {
                Debug.Log($"[EnemyDeathCounter] Initialized. Required Deaths: {requiredDeaths}, Count Layers: {countLayers.value}", this);
            }
        }

        private void OnEnable()
        {
            // Listen to EntityDied event (same as FinishTrigger)
            EventManager.StartListening(EventManager.GameEvent.EntityDied, OnEntityDied);
        }

        private void OnDisable()
        {
            // Unsubscribe to prevent memory leaks
            EventManager.StopListening(EventManager.GameEvent.EntityDied, OnEntityDied);
        }

        private void OnEntityDied(object eventData)
        {
            if (_allDead)
                return;

            if (eventData is not EntityDiedEventData e)
                return;

            int layer = e.VictimLayer;

            // Only count deaths on specified layers
            if ((countLayers.value & (1 << layer)) == 0)
            {
                if (debugLogs)
                    Debug.Log($"[EnemyDeathCounter] Entity {e.Victim?.name} died on layer {layer}, but not in countLayers. Ignoring.", this);
                return;
            }

            _deathCount++;

            if (debugLogs)
                Debug.Log($"[EnemyDeathCounter] Counted death {_deathCount}/{requiredDeaths} (layer={layer})", this);

            // Check if all enemies are dead
            if (_deathCount >= requiredDeaths)
            {
                _allDead = true;
                OnAllEnemiesDied();
            }
        }

        private void OnAllEnemiesDied()
        {
            if (debugLogs)
                Debug.Log($"[EnemyDeathCounter] ✓ All enemies dead! ({_deathCount}/{requiredDeaths}). Executing actions...", this);

            // Activate GameObject if specified
            if (activateGameObject != null)
            {
                if (activateGameObject.activeSelf)
                {
                    if (debugLogs)
                        Debug.Log($"[EnemyDeathCounter] {activateGameObject.name} is already active. Skipping activation.", this);
                }
                else
                {
                    activateGameObject.SetActive(true);
                    if (debugLogs)
                        Debug.Log($"[EnemyDeathCounter] ✓ Activated {activateGameObject.name}", this);
                    else
                        Debug.Log($"[EnemyDeathCounter] ✓ Activated {activateGameObject.name} (all enemies died)", this);
                }
            }

            // Call UnityEvent if assigned
            if (onAllEnemiesDied != null)
            {
                onAllEnemiesDied.Invoke();
                if (debugLogs)
                    Debug.Log("[EnemyDeathCounter] ✓ Invoked onAllEnemiesDied UnityEvent", this);
            }
        }

        /// <summary>
        /// Public method to get current death count (for debugging or UI).
        /// </summary>
        public int GetDeathCount() => _deathCount;

        /// <summary>
        /// Public method to get required deaths (for debugging or UI).
        /// </summary>
        public int GetRequiredDeaths() => requiredDeaths;

        /// <summary>
        /// Public method to check if all enemies are dead.
        /// </summary>
        public bool AreAllEnemiesDead() => _allDead;
    }
}

