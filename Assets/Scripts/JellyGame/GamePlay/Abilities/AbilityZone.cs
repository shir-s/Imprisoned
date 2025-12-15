// FILEPATH: Assets/Scripts/Abilities/Zones/AbilityZone.cs
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

        private readonly HashSet<Collider> _inside = new HashSet<Collider>();
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

            // IMPORTANT: Configure can be called AFTER Awake at runtime.
            // So we must also update the actual running timers here.
            _lifeTimer = lifetimeSeconds;
            _tickTimer = tickInterval;
        }

        private void Awake()
        {
            EnsureKinematicRigidbody();

            // Initialize timers with current values (may be overwritten by Configure() after Awake)
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
                for (int i = 0; i < _effects.Length; i++)
                    _effects[i]?.Tick(Time.deltaTime);
                return;
            }

            _tickTimer -= Time.deltaTime;
            if (_tickTimer <= 0f)
            {
                _tickTimer += tickInterval;

                for (int i = 0; i < _effects.Length; i++)
                    _effects[i]?.Tick(tickInterval);
            }
        }

        internal void HandleTriggerEnter(Collider other)
        {
            EnsureEffectsInitialized();

            if (!IsInLayerMask(other.gameObject.layer, targetLayers))
                return;

            if (_inside.Add(other))
            {
                if (debugLogs)
                    Debug.Log($"[AbilityZone] Enter: {other.name} (layer {other.gameObject.layer})", this);

                if (_effects == null) return;
                for (int i = 0; i < _effects.Length; i++)
                    _effects[i]?.OnTargetEntered(other);
            }
        }

        internal void HandleTriggerExit(Collider other)
        {
            EnsureEffectsInitialized();

            if (_inside.Remove(other))
            {
                if (debugLogs)
                    Debug.Log($"[AbilityZone] Exit: {other.name}", this);

                if (_effects == null) return;
                for (int i = 0; i < _effects.Length; i++)
                    _effects[i]?.OnTargetExited(other);
            }
        }

        private void EnsureEffectsInitialized()
        {
            if (_effectsInitialized)
                return;

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

            if (debugLogs)
                Debug.Log($"[AbilityZone] Effects found: {(_effects == null ? 0 : _effects.Length)} | lifetime={lifetimeSeconds} tick={tickInterval}", this);
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
