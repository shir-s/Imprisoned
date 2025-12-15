// FILEPATH: Assets/Scripts/Abilities/Zones/DamageZoneEffect.cs
using System.Collections.Generic;
using JellyGame.GamePlay.Combat;
using UnityEngine;

namespace JellyGame.GamePlay.Abilities.Zones
{
    [DisallowMultipleComponent]
    public class DamageZoneEffect : MonoBehaviour, IZoneEffect
    {
        [Header("Damage")]
        [SerializeField] private float damagePerSecond = 4f;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private readonly HashSet<Collider> _inside = new HashSet<Collider>();

        public void Configure(float dps, bool debug = false)
        {
            damagePerSecond = dps;
            debugLogs = debug;
        }

        public void OnZoneSpawned(AbilityZone zone)
        {
            if (debugLogs)
                Debug.Log($"[DamageZoneEffect] Spawned. DPS={damagePerSecond}", this);
        }

        public void OnZoneDespawned(AbilityZone zone)
        {
            _inside.Clear();
        }

        public void OnTargetEntered(Collider other)
        {
            _inside.Add(other);

            if (debugLogs)
                Debug.Log($"[DamageZoneEffect] Tracking: {other.name}", this);
        }

        public void OnTargetExited(Collider other)
        {
            _inside.Remove(other);

            if (debugLogs)
                Debug.Log($"[DamageZoneEffect] Untracking: {other.name}", this);
        }

        public void Tick(float deltaTime)
        {
            if (_inside.Count == 0)
                return;

            float dmg = Mathf.Max(0f, damagePerSecond) * Mathf.Max(0f, deltaTime);
            if (dmg <= 0f)
                return;

            foreach (var col in _inside)
            {
                if (col == null) continue;

                // More robust resolution: same GO, then parent, then children.
                IDamageable dmgable =
                    col.GetComponent<IDamageable>() ??
                    col.GetComponentInParent<IDamageable>() ??
                    col.GetComponentInChildren<IDamageable>();

                if (dmgable != null)
                {
                    dmgable.ApplyDamage(dmg);

                    if (debugLogs)
                        Debug.Log($"[DamageZoneEffect] Damage {col.name} +{dmg:F2}", this);
                }
                else if (debugLogs)
                {
                    Debug.Log($"[DamageZoneEffect] {col.name} has NO IDamageable (self/parent/children).", this);
                }
            }
        }
    }
}
