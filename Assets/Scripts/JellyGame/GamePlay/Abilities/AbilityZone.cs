using System.Collections.Generic;
using UnityEngine;

namespace JellyGame.GamePlay.Abilities.Zones
{
    [DisallowMultipleComponent]
    public class AbilityZone : MonoBehaviour
    {
        [Header("Lifetime")]
        [SerializeField] private float lifetimeSeconds = 4f;

        [Header("Tick")]
        [Tooltip("How often Tick() is called on effects. 0 = every frame.")]
        [SerializeField] private float tickInterval = 0.2f;

        [Header("Filtering")]
        [Tooltip("Only colliders on these layers will be forwarded to effects.")]
        [SerializeField] private LayerMask targetLayers = ~0;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        // MASTER LIST: Used by Effects to apply damage
        private readonly HashSet<Collider> _inside = new HashSet<Collider>();
        
        // REFERENCE COUNTER: Used to handle multi-triangle logic
        // Key = Enemy Collider, Value = Number of Zone Triangles touching it
        private readonly Dictionary<Collider, int> _triggerCounts = new Dictionary<Collider, int>();

        private IZoneEffect[] _effects;

        private float _lifeTimer;
        private float _tickTimer;

        private bool _effectsInitialized;
        private bool _awakeDone;

        public void Configure(float lifetime, float tickEverySeconds, LayerMask targets, bool debug = false)
        {
            lifetimeSeconds = Mathf.Max(0.05f, lifetime);
            tickInterval = Mathf.Max(0f, tickEverySeconds);
            targetLayers = targets;
            debugLogs = debug;

            _lifeTimer = lifetimeSeconds;
            _tickTimer = tickInterval;
        }

        private void Awake()
        {
            EnsureKinematicRigidbody();
            _lifeTimer = lifetimeSeconds;
            _tickTimer = tickInterval;
            _awakeDone = true;
        }

        private void Start()
        {
            CacheEffectsAndNotifySpawned();
        }

        private void Update()
        {
            EnsureEffectsInitialized();

            _lifeTimer -= Time.deltaTime;
            if (_lifeTimer <= 0f)
            {
                Despawn();
                return;
            }

            if (_effects == null || _effects.Length == 0)
                return;

            if (tickInterval <= 0f)
            {
                // Run every frame
                for (int i = 0; i < _effects.Length; i++)
                    _effects[i]?.Tick(Time.deltaTime);
                return;
            }

            _tickTimer -= Time.deltaTime;
            if (_tickTimer <= 0f)
            {
                _tickTimer += tickInterval;
                // Pass the fixed interval to ensure consistent damage calculation
                for (int i = 0; i < _effects.Length; i++)
                    _effects[i]?.Tick(tickInterval);
            }
        }

        internal void HandleTriggerEnter(Collider other)
        {
            EnsureEffectsInitialized();

            if (!IsInLayerMask(other.gameObject.layer, targetLayers))
                return;

            // --- REFERENCE COUNTING LOGIC ---
            if (!_triggerCounts.ContainsKey(other))
            {
                _triggerCounts[other] = 0;
            }

            _triggerCounts[other]++;

            // Only add to the "Active" list if this is the FIRST triangle encountered
            if (_triggerCounts[other] == 1)
            {
                if (_inside.Add(other))
                {
                    if (debugLogs)
                        Debug.Log($"[AbilityZone] Enter: {other.name} (Count: 1)", this);

                    NotifyEffectsTargetEntered(other);
                }
            }
        }

        internal void HandleTriggerExit(Collider other)
        {
            EnsureEffectsInitialized();

            // Handle edge case where object was destroyed/disabled and dictionary was cleared
            if (!_triggerCounts.ContainsKey(other))
                return;

            _triggerCounts[other]--;

            // Only remove from "Active" list if count drops to ZERO (left all triangles)
            if (_triggerCounts[other] <= 0)
            {
                _triggerCounts.Remove(other);

                if (_inside.Remove(other))
                {
                    if (debugLogs)
                        Debug.Log($"[AbilityZone] Exit: {other.name} (Count: 0)", this);

                    NotifyEffectsTargetExited(other);
                }
            }
        }

        private void NotifyEffectsTargetEntered(Collider other)
        {
            if (_effects == null) return;
            for (int i = 0; i < _effects.Length; i++)
                _effects[i]?.OnTargetEntered(other);
        }

        private void NotifyEffectsTargetExited(Collider other)
        {
            if (_effects == null) return;
            for (int i = 0; i < _effects.Length; i++)
                _effects[i]?.OnTargetExited(other);
        }

        private void EnsureEffectsInitialized()
        {
            if (_effectsInitialized) return;
            CacheEffectsAndNotifySpawned();
        }

        private void CacheEffectsAndNotifySpawned()
        {
            _effects = GetComponents<IZoneEffect>();
            _effectsInitialized = true;

            if (_effects != null)
            {
                for (int i = 0; i < _effects.Length; i++)
                    _effects[i]?.OnZoneSpawned(this);
            }
        }

        private void Despawn()
        {
            EnsureEffectsInitialized();

            if (_effects != null)
            {
                for (int i = 0; i < _effects.Length; i++)
                    _effects[i]?.OnZoneDespawned(this);
            }

            Destroy(gameObject);
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

        private static bool IsInLayerMask(int layer, LayerMask mask)
        {
            return (mask.value & (1 << layer)) != 0;
        }
    }
}