// FILEPATH: Assets/Scripts/Player/AreaFillSelfDamage.cs
using System.Collections.Generic;
using JellyGame.GamePlay.Combat;
using JellyGame.GamePlay.Map.Surfaces;
using UnityEngine;

namespace JellyGame.GamePlay.Player
{
    /// <summary>
    /// Applies self-damage (shrinks via IDamageable) when the player closes a filled area.
    /// Damage is proportional to the filled world-space area.
    ///
    /// This is deliberately separated from PlayerAbilityManager so you can swap/extend cost logic easily.
    /// </summary>
    [DisallowMultipleComponent]
    public class AreaFillSelfDamage : MonoBehaviour
    {
        [Header("Damage From Filled Area")]
        [Tooltip("Damage per 1 square meter of filled area.")]
        [SerializeField] private float damagePerSquareMeter = 1f;

        [Tooltip("Clamp damage per fill. Use 0 to disable clamp.")]
        [SerializeField] private float minDamagePerFill = 0f;

        [Tooltip("Clamp damage per fill. Use 0 to disable clamp.")]
        [SerializeField] private float maxDamagePerFill = 0f;

        [Header("Behavior")]
        [Tooltip("If true, only apply damage when there is an active ability that can spawn zones.")]
        [SerializeField] private bool onlyWhenAbilityActive = true;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private IDamageable _self;

        private void Awake()
        {
            _self = GetComponent<IDamageable>();
            if (_self == null)
                Debug.LogError("[AreaFillSelfDamage] Missing IDamageable on the same object (CubeScaler should implement it).", this);
        }

        /// <summary>
        /// Called by PlayerAbilityManager when a closed area was detected and filled.
        /// </summary>
        public void HandleAreaFilled(SimplePaintSurface surface, IReadOnlyList<Vector2> localPolyXZ, bool abilityIsActive)
        {
            if (_self == null)
                return;

            if (onlyWhenAbilityActive && !abilityIsActive)
                return;

            if (surface == null || localPolyXZ == null || localPolyXZ.Count < 3)
                return;

            float areaWorld = ComputeWorldAreaXZ(surface.transform, localPolyXZ);
            if (!float.IsFinite(areaWorld) || areaWorld <= 0f)
                return;

            float damage = areaWorld * Mathf.Max(0f, damagePerSquareMeter);

            if (minDamagePerFill > 0f) damage = Mathf.Max(minDamagePerFill, damage);
            if (maxDamagePerFill > 0f) damage = Mathf.Min(maxDamagePerFill, damage);

            if (damage <= 0f)
                return;

            if (debugLogs)
                Debug.Log($"[AreaFillSelfDamage] area={areaWorld:F3} m^2, damage={damage:F3}", this);

            _self.ApplyDamage(damage);
        }

        /// <summary>
        /// Computes polygon area from local XZ points, then converts to world square meters using lossyScale.
        /// Assumption: localPolyXZ uses surface local X as X and local Z as Y (stored in Vector2).
        /// </summary>
        private static float ComputeWorldAreaXZ(Transform surface, IReadOnlyList<Vector2> localPolyXZ)
        {
            // Shoelace formula in local space
            double sum = 0.0;
            int n = localPolyXZ.Count;

            for (int i = 0; i < n; i++)
            {
                Vector2 a = localPolyXZ[i];
                Vector2 b = localPolyXZ[(i + 1) % n];
                sum += (double)a.x * b.y - (double)b.x * a.y;
            }

            float areaLocal = Mathf.Abs((float)sum) * 0.5f;

            // Convert local area to world area for XZ plane
            Vector3 s = surface.lossyScale;
            float scaleXZ = Mathf.Abs(s.x * s.z);

            return areaLocal * scaleXZ;
        }

        private void OnValidate()
        {
            if (damagePerSquareMeter < 0f) damagePerSquareMeter = 0f;
            if (minDamagePerFill < 0f) minDamagePerFill = 0f;
            if (maxDamagePerFill < 0f) maxDamagePerFill = 0f;
        }
    }
}
