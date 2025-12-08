using System.Collections.Generic;
using JellyGame.GamePlay.Player;
using UnityEngine;

namespace JellyGame.GamePlay.Enemy
{
    [DisallowMultipleComponent]
    public class VolumeDamageOnTrigger : MonoBehaviour
    {
        [SerializeField] private LayerMask targetLayers = ~0;

        [Tooltip("How much of the cube's current size to remove per hit (in percent). Example: 10 = remove 10% each hit.")]
        [Range(0f, 100f)]
        [SerializeField] private float percentToRemovePerHit = 10f;

        [Tooltip("Seconds between each possible damage hit.")]
        [SerializeField] private float damageCooldown = 2f;

        // Tracks cooldown per target
        private readonly Dictionary<Collider, float> nextDamageTime = new();

        private void OnTriggerEnter(Collider other)
        {
            TryApplyDamage(other);
        }

        private void OnTriggerStay(Collider other)
        {
            TryApplyDamage(other);
        }

        private void OnTriggerExit(Collider other)
        {
            nextDamageTime.Remove(other);
        }

        private void TryApplyDamage(Collider other)
        {
            // Only react to objects on allowed layers
            if ((targetLayers.value & (1 << other.gameObject.layer)) == 0)
                return;

            // Cooldown per object
            if (nextDamageTime.TryGetValue(other, out float allowedTime))
            {
                if (Time.time < allowedTime)
                    return; // still cooling down
            }

            var scaler = other.GetComponent<CubeScaler>();
            if (scaler == null)
                return;

            // Convert percent → scale factor
            // 10% → 0.9 (keep 90% of current size)
            float factor = 1f - (percentToRemovePerHit / 100f);
            factor = Mathf.Clamp01(factor); // safety: stays between 0 and 1

            scaler.ChangeVolumeMultiply(factor);
            Debug.Log($"VolumeDamageOnTrigger: Scaled {other.name} by factor {factor} (removed {percentToRemovePerHit}%).");

            nextDamageTime[other] = Time.time + damageCooldown;
        }
    }
}
