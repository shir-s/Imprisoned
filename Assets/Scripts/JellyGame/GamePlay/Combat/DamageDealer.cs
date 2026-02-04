// FILEPATH: Assets/Scripts/Combat/DamageDealer.cs
using System.Collections.Generic;
using JellyGame.GamePlay.Managers;
using UnityEngine;

namespace JellyGame.GamePlay.Combat
{
    /// <summary>
    /// Unified damage system that can be applied to any GameObject that deals damage.
    /// Supports both percent-based and absolute damage.
    /// Can work automatically on trigger contact OR be called manually by AI behaviors.
    /// 
    /// Usage:
    /// - Melee enemies: Add to enemy, set to Manual mode, AI behavior calls DealDamage()
    /// - Projectiles: Add to projectile prefab, set to OnContact mode with destroyOnHit
    /// - Environmental hazards: Add to trigger collider, set to OnContact mode
    /// </summary>
    [DisallowMultipleComponent]
    public class DamageDealer : MonoBehaviour
    {
        public enum DamageMode
        {
            OnContact,  // Automatically damages on trigger contact (projectiles, hazards)
            Manual      // Only damages when DealDamage() is called (melee AI)
        }

        public enum DamageType
        {
            Absolute,   // Fixed amount (e.g., 1.0 damage removes 1.0 * sizeLossPerDamage)
            Percent     // Percentage (e.g., 10% removes 10% of current size)
        }

        [Header("Damage Configuration")]
        [Tooltip("How this component triggers damage.")]
        [SerializeField] private DamageMode damageMode = DamageMode.OnContact;

        [Tooltip("Absolute (fixed amount) or Percent (% of current size).")]
        [SerializeField] private DamageType damageType = DamageType.Absolute;

        [Tooltip("Damage amount. Absolute: raw value. Percent: 0-100 (e.g., 10 = 10%).")]
        [SerializeField] private float damageAmount = 1f;

        [Header("Targeting")]
        [Tooltip("Which layers this can damage.")]
        [SerializeField] private LayerMask targetLayers = ~0;

        [Header("Cooldown")]
        [Tooltip("Seconds between damage hits on the same target. 0 = can hit every frame.")]
        [SerializeField] private float damageCooldown = 0.5f;

        [Header("Projectile Options (OnContact mode only)")]
        [Tooltip("Destroy this GameObject after hitting a target.")]
        [SerializeField] private bool destroyOnHit = false;

        [Tooltip("If true, can only damage one target total, then stops/destroys.")]
        [SerializeField] private bool hitOnce = false;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        // Tracks when each target can be damaged again
        private readonly Dictionary<Collider, float> _nextDamageTime = new Dictionary<Collider, float>();
        
        // For hitOnce mode
        private bool _hasHitTarget = false;

        #region Unity Trigger Events (OnContact Mode)

        private void OnTriggerEnter(Collider other)
        {
            if (damageMode != DamageMode.OnContact)
                return;

            TryDealDamageToCollider(other);
        }

        private void OnTriggerStay(Collider other)
        {
            if (damageMode != DamageMode.OnContact)
                return;

            TryDealDamageToCollider(other);
        }

        private void OnTriggerExit(Collider other)
        {
            // Clean up cooldown tracking when target leaves
            _nextDamageTime.Remove(other);
        }

        #endregion

        #region Public API (Manual Mode)

        /// <summary>
        /// Manually deal damage to a target (for AI behaviors).
        /// Returns true if damage was successfully applied.
        /// </summary>
        public bool DealDamage(IDamageable target, GameObject targetGameObject = null)
        {
            if (target == null)
                return false;

            if (hitOnce && _hasHitTarget)
                return false;

            // Check cooldown if we have a GameObject to track
            if (targetGameObject != null)
            {
                int instanceId = targetGameObject.GetInstanceID();
                if (_nextDamageTime.ContainsKey(null)) // Using null as placeholder
                {
                    // We need a better way to track cooldowns for manual mode
                    // For now, just use Time.time comparison
                }
            }

            ApplyDamageToTarget(target);
            _hasHitTarget = true;

            return true;
        }

        /// <summary>
        /// Manually deal damage to a collider (finds IDamageable component).
        /// Returns true if damage was successfully applied.
        /// </summary>
        public bool DealDamage(Collider targetCollider)
        {
            return TryDealDamageToCollider(targetCollider);
        }

        #endregion

        #region Internal Damage Logic

        private bool TryDealDamageToCollider(Collider other)
        {
            if (other == null)
                return false;

            // Check layer
            if ((targetLayers.value & (1 << other.gameObject.layer)) == 0)
                return false;

            // Check if already hit (hitOnce mode)
            if (hitOnce && _hasHitTarget)
                return false;

            // Check cooldown
            if (_nextDamageTime.TryGetValue(other, out float nextAllowedTime))
            {
                if (Time.time < nextAllowedTime)
                    return false;
            }

            // Find damageable target
            IDamageable damageable = other.GetComponentInParent<IDamageable>();
            if (damageable == null)
            {
                if (debugLogs)
                    Debug.Log($"[DamageDealer] {other.name} has no IDamageable component.", this);
                return false;
            }

            // Apply damage
            ApplyDamageToTarget(damageable);

            // Track cooldown
            _nextDamageTime[other] = Time.time + damageCooldown;
            _hasHitTarget = true;

            // Handle projectile destruction
            if (destroyOnHit)
            {
                if (debugLogs)
                    Debug.Log($"[DamageDealer] Destroying {gameObject.name} after hit.", this);
                
                Destroy(gameObject);
            }

            return true;
        }

        private void ApplyDamageToTarget(IDamageable target)
        {
            if (damageType == DamageType.Absolute)
            {
                // Absolute damage goes through IDamageable.ApplyDamage()
                target.ApplyDamage(damageAmount);

                if (debugLogs)
                    Debug.Log($"[DamageDealer] Applied {damageAmount} ABSOLUTE damage.", this);
            }
            else // Percent
            {
                // Percent damage requires IPercentDamageable interface
                // Check if the target supports percent damage
                if (target is IPercentDamageable percentDamageable)
                {
                    percentDamageable.ApplyPercentDamage(damageAmount);

                    if (debugLogs)
                        Debug.Log($"[DamageDealer] Applied {damageAmount}% PERCENT damage.", this);
                }
                else
                {
                    // Fallback: convert percent to absolute damage
                    // This is a best-effort approach for targets that don't support percent damage
                    if (debugLogs)
                        Debug.LogWarning($"[DamageDealer] Target doesn't implement IPercentDamageable, falling back to absolute damage estimation.", this);

                    // Convert percent to absolute (rough estimate: 10% = 0.1 damage)
                    float absoluteEquivalent = damageAmount / 100f;
                    target.ApplyDamage(absoluteEquivalent);
                }
            }

            // Trigger damage event
            EventManager.TriggerEvent(EventManager.GameEvent.PlayerDamaged, target);
        }

        #endregion

        #region Validation

        private void OnValidate()
        {
            if (damageAmount < 0f)
                damageAmount = 0f;

            if (damageCooldown < 0f)
                damageCooldown = 0f;

            if (damageType == DamageType.Percent)
            {
                // Clamp percent to 0-100 range
                damageAmount = Mathf.Clamp(damageAmount, 0f, 100f);
            }
        }

        #endregion
    }
}