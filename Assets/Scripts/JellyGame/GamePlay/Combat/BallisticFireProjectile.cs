// FILEPATH: Assets/Scripts/Combat/Projectiles/BallisticFireProjectile.cs
using System;
using JellyGame.GamePlay.Combat;
using JellyGame.GamePlay.Enemy.AI.Movement;
using UnityEngine;

namespace JellyGame.GamePlay.Combat.Projectiles
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    [RequireComponent(typeof(Rigidbody))]
    public class BallisticFireProjectile : MonoBehaviour
    {
        [Header("Physics")]
        [Tooltip("Gravity multiplier for this projectile (1 = Unity gravity).")]
        [SerializeField] private float gravityMultiplier = 1f;

        [Tooltip("Extra downward accel (in addition to gravity). Use 0 if you don't want.")]
        [SerializeField] private float extraDownwardAccel = 0f;

        [Header("Lifetime")]
        [SerializeField] private float lifetime = 6f;

        [Header("Damage")]
        [Tooltip("Only hits on these layers will receive damage. If 0 (Nothing), no damage will be applied.")]
        [SerializeField] private LayerMask damageLayers;

        [Tooltip("Damage applied via IDamageable.ApplyDamage(amount).")]
        [SerializeField] private float damageAmount = 1f;

        [Header("Slow Effect")]
        [SerializeField] private LayerMask slowLayers;
        [SerializeField, Range(0f, 2f)] private float slowMultiplier = 0.5f;
        [SerializeField] private float slowDurationSeconds = 2f;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        public Action<Collider> OnHit;

        // Expose these so the shooter can aim with the SAME gravity and timing.
        public float GravityMultiplier => gravityMultiplier;
        public float ExtraDownwardAccel => extraDownwardAccel;
        public float SlowDurationSeconds => slowDurationSeconds;

        private Rigidbody _rb;
        private float _dieTime;
        private bool _dead;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.useGravity = true;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;

            _dieTime = Time.time + Mathf.Max(0.1f, lifetime);
        }

        /// <summary>
        /// Sets the initial velocity for ballistic flight.
        /// </summary>
        public void Launch(Vector3 initialVelocity)
        {
            if (_rb == null) _rb = GetComponent<Rigidbody>();

            // If you are on a Unity version without linearVelocity, replace with: _rb.velocity = initialVelocity;
            _rb.linearVelocity = initialVelocity;
        }

        private void FixedUpdate()
        {
            if (_dead) return;

            // Modify gravity for this projectile only.
            // Total gravity = Physics.gravity * gravityMultiplier + Vector3.down * extraDownwardAccel
            if (!Mathf.Approximately(gravityMultiplier, 1f) || extraDownwardAccel > 0f)
            {
                Vector3 gDelta = Physics.gravity * (gravityMultiplier - 1f);
                Vector3 extra = Vector3.down * extraDownwardAccel;
                _rb.AddForce(gDelta + extra, ForceMode.Acceleration);
            }

            if (Time.time >= _dieTime)
                Kill(null);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_dead) return;
            HandleHit(other);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (_dead) return;
            HandleHit(collision.collider);
        }

        private void HandleHit(Collider other)
        {
            if (other == null) { Kill(null); return; }

            if (debugLogs) Debug.Log($"[BallisticProjectile] Hit {other.name}", this);

            // 1) Apply slow (existing mechanic)
            if ((slowLayers.value & (1 << other.gameObject.layer)) != 0)
            {
                var receiver = other.GetComponentInParent<IMovementSpeedEffectReceiver>();
                if (receiver != null)
                    receiver.ApplySpeedMultiplier(slowMultiplier, slowDurationSeconds);
            }

            // 2) Apply damage using the EXISTING system (IDamageable)
            if (damageAmount > 0f && damageLayers.value != 0 && (damageLayers.value & (1 << other.gameObject.layer)) != 0)
            {
                var damageable = other.GetComponentInParent<IDamageable>();
                if (damageable != null)
                {
                    if (debugLogs) Debug.Log($"[BallisticProjectile] Dealt {damageAmount} damage to {other.name}", this);
                    damageable.ApplyDamage(damageAmount);
                }
                else if (debugLogs)
                {
                    Debug.Log($"[BallisticProjectile] Damage layer matched, but no IDamageable found on {other.name} (or parents).", this);
                }
            }

            OnHit?.Invoke(other);
            Kill(other);
        }

        private void Kill(Collider hit)
        {
            if (_dead) return;
            _dead = true;
            Destroy(gameObject);
        }
    }
}