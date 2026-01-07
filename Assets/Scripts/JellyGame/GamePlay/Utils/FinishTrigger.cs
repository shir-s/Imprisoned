// FILEPATH: Assets/Scripts/World/Finish/FinishTrigger.cs

using System.Collections;
using JellyGame.GamePlay.Audio.Core;
using JellyGame.GamePlay.Managers;
using JellyGame.GamePlay.Utils;
using UnityEngine;

namespace JellyGame.GamePlay.World.Finish
{
    /// <summary>
    /// Triggers GameWin when player enters the trigger AND all enemies are dead.
    /// 
    /// Smart design: Uses EnemyDeathCounter to check if all enemies are dead.
    /// EnemyDeathCounter is the single source of truth for counting enemy deaths.
    ///
    /// Usage:
    /// - Attach to a GameObject with a Collider set as Trigger.
    /// - Set Allowed Layers to the player layer.
    /// - (Optional) Assign EnemyDeathCounter reference, or it will be auto-found in scene.
    /// - Make sure EnemyDeathCounter exists in scene and is configured correctly.
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
        [Tooltip("Reference to EnemyDeathCounter. Will be auto-found in scene if not assigned. If null, trigger will activate without checking enemy deaths.")]
        [SerializeField] private EnemyDeathCounter enemyDeathCounter;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private bool _triggered;

        private void Reset()
        {
            Collider c = GetComponent<Collider>();
            if (c != null)
                c.isTrigger = true;
        }

        private void Awake()
        {
            EnsureKinematicRigidbody();
        }

        private void Start()
        {
            // Auto-find EnemyDeathCounter in scene (in Start to ensure all objects are initialized)
            EnsureEnemyDeathCounter();
        }

        private void OnEnable()
        {
            // Also try to find in OnEnable (in case EnemyDeathCounter was activated after this)
            EnsureEnemyDeathCounter();
        }

        private void EnsureEnemyDeathCounter()
        {
            if (enemyDeathCounter != null)
                return; // Already found

            // Auto-find EnemyDeathCounter in scene
            enemyDeathCounter = FindObjectOfType<EnemyDeathCounter>();

            if (enemyDeathCounter == null)
            {
                // No EnemyDeathCounter found - this is OK for scenes without enemies
                // The trigger will work, just without the enemy death check
                if (debugLogs)
                {
                    Debug.Log($"[FinishTrigger] No EnemyDeathCounter found in scene. {gameObject.name} will trigger GameWin when player enters (no enemy death requirement).", this);
                }
            }
            else if (debugLogs)
            {
                Debug.Log($"[FinishTrigger] Auto-found EnemyDeathCounter: {enemyDeathCounter.name}. Required: {enemyDeathCounter.GetRequiredDeaths()}", this);
            }
        }

        private bool AreAllEnemiesDead()
        {
            // If no EnemyDeathCounter, assume all enemies are dead (for scenes without enemies)
            if (enemyDeathCounter == null)
            {
                return true; // No enemy requirement - trigger works immediately
            }

            // Use EnemyDeathCounter - single source of truth!
            return enemyDeathCounter.AreAllEnemiesDead();
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

            // Check if all required enemies are dead (using EnemyDeathCounter)
            if (!AreAllEnemiesDead())
            {
                if (enemyDeathCounter != null)
                {
                    int currentCount = enemyDeathCounter.GetDeathCount();
                    int requiredCount = enemyDeathCounter.GetRequiredDeaths();
                    
                    if (debugLogs)
                        Debug.Log($"[FinishTrigger] Player entered trigger, but only {currentCount}/{requiredCount} enemies are dead. Waiting...", this);
                }
                return;
            }

            _triggered = true;

            if (enemyDeathCounter != null)
            {
                int deadCount = enemyDeathCounter.GetDeathCount();
                int reqCount = enemyDeathCounter.GetRequiredDeaths();
                
                if (debugLogs)
                    Debug.Log($"[FinishTrigger] ✓ GameWin triggered by {other.name} ({deadCount}/{reqCount} enemies dead)", this);
            }
            
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
