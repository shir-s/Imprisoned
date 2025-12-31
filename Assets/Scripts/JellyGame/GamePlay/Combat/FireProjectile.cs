// FILEPATH: Assets/Scripts/Combat/Projectiles/FireProjectile.cs
using System;
using JellyGame.GamePlay.Enemy.AI.Movement;
using UnityEngine;

namespace JellyGame.GamePlay.Combat.Projectiles
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class FireProjectile : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float speed = 12f;
        [SerializeField] private float lifetime = 5f;

        [Header("Hit")]
        [Tooltip("Projectile is destroyed on any hit (trigger or collision).")]
        [SerializeField] private bool destroyOnHit = true;

        [Header("Slow Effect")]
        [SerializeField] private LayerMask slowLayers;
        [SerializeField, Range(0f, 2f)] private float slowMultiplier = 0.5f;
        [SerializeField] private float slowDurationSeconds = 2f;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        public Action<Collider> OnHit; // shooter can subscribe to confirm hit on its chosen target

        private Vector3 _velocity;
        private float _dieTime;
        private bool _dead;

        public void Init(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-6f) direction = transform.forward;
            direction.Normalize();

            _velocity = direction * speed;
            _dieTime = Time.time + Mathf.Max(0.05f, lifetime);
        }

        private void Awake()
        {
            // Ensure trigger collider is allowed.
            // You can also use collisions; both are supported below.
            var col = GetComponent<Collider>();
            if (col != null && !col.isTrigger)
            {
                // Not forcing it, just warning.
                if (debugLogs) Debug.Log("[FireProjectile] Collider is not trigger. Collision callbacks will be used.", this);
            }
        }

        private void Update()
        {
            if (_dead) return;

            transform.position += _velocity * Time.deltaTime;

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

            if (debugLogs) Debug.Log($"[FireProjectile] Hit {other.name} (layer {other.gameObject.layer})", this);

            // Apply slow if layer matches
            if ((slowLayers.value & (1 << other.gameObject.layer)) != 0)
            {
                var receiver = other.GetComponentInParent<IMovementSpeedEffectReceiver>();
                if (receiver != null)
                    receiver.ApplySpeedMultiplier(slowMultiplier, slowDurationSeconds);
            }

            // Notify shooter
            OnHit?.Invoke(other);

            if (destroyOnHit)
                Kill(other);
        }

        private void Kill(Collider hit)
        {
            if (_dead) return;
            _dead = true;

            // Optional: spawn VFX here.

            Destroy(gameObject);
        }
    }
}
