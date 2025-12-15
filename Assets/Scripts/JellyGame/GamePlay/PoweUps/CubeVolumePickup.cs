// FILEPATH: Assets/Scripts/PowerUps/CubeVolumePickup.cs
using JellyGame.GamePlay.Combat;
using UnityEngine;

namespace JellyGame.GamePlay.PowerUps
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class CubeVolumePickup : MonoBehaviour
    {
        [Header("Collection")]
        [SerializeField] private LayerMask collectorLayers = ~0;

        [Tooltip("How much HEAL to apply. CubeScaler converts this into size via sizeGainPerHeal.")]
        [SerializeField] private float healAmount = 1f;

        [SerializeField] private bool destroyOnCollect = true;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private Collider _col;

        private void Reset()
        {
            _col = GetComponent<Collider>();
            if (_col != null)
                _col.isTrigger = true;
        }

        private void Awake()
        {
            _col = GetComponent<Collider>();
            if (_col != null)
                _col.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            // Layer filter
            if ((collectorLayers.value & (1 << other.gameObject.layer)) == 0)
                return;

            // Find a damageable target (player slime, enemy slime, etc.)
            var damageable = other.GetComponentInParent<IDamageable>();
            if (damageable == null)
            {
                if (debugLogs)
                    Debug.Log("[CubeVolumePickup] Collector has no IDamageable.", other);
                return;
            }

            // Heal = grow
            damageable.Heal(healAmount);

            if (debugLogs)
                Debug.Log($"[CubeVolumePickup] Healed {other.name} for {healAmount}", this);

            if (destroyOnCollect)
                Destroy(gameObject);
        }

        private void OnValidate()
        {
            if (healAmount < 0f)
                healAmount = 0f;
        }
    }
}