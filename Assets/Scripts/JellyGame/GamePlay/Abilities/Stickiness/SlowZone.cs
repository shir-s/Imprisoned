using UnityEngine;
using System.Collections.Generic;
using JellyGame.GamePlay.Enemy.AI.Movement; // Reference to SteeringNavigator

namespace JellyGame.GamePlay.Abilities.Stickiness
{
    /// <summary>
    /// Invisible trigger zone that slows enemies.
    /// Adapted to work with the SteeringNavigator architecture.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class SlowZone : MonoBehaviour
    {
        [Header("Slow Effect")]
        [Tooltip("Speed multiplier (0.15 = 15% speed = very slow, 0.5 = 50% speed).")]
        [Range(0.01f, 1f)]
        [SerializeField] private float slowMultiplier = 0.15f;

        [Header("Lifetime")] 
        [Tooltip("How long this zone lasts (seconds). 0 = infinite")] 
        [SerializeField] private float lifetime = 15f;

        [Header("Visual (Optional)")]
        [SerializeField] private Material zoneMaterial;
        [SerializeField] private Color zoneColor = new Color(0, 1, 0, 0.3f);

        [Header("Debug")] 
        [SerializeField] private bool debugLogs = false;

        // We store the components directly to avoid calling GetComponent repeatedly on exit
        private HashSet<Component> _affectedMovers = new HashSet<Component>();
        private float _spawnTime;

        private void Start()
        {
            _spawnTime = Time.time;

            if (zoneMaterial != null)
            {
                MeshRenderer renderer = GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.material = zoneMaterial;
                    renderer.material.color = zoneColor;
                }
            }
        }

        private void Update()
        {
            if (lifetime > 0 && Time.time - _spawnTime >= lifetime)
            {
                Destroy(gameObject);
            }
        }

        public void SetSlowMultiplier(float multiplier)
        {
            slowMultiplier = Mathf.Clamp01(multiplier);
        }

        private void OnTriggerEnter(Collider other)
        {
            HandleCollision(other, true);
        }

        private void OnTriggerExit(Collider other)
        {
            HandleCollision(other, false);
        }

        private void HandleCollision(Collider col, bool entering)
        {
            if (col == null) return;

            // 1. Look for the SteeringNavigator (The new standard Motor)
            var navigator = col.GetComponent<SteeringNavigator>();
            if (navigator == null) navigator = col.GetComponentInParent<SteeringNavigator>();

            if (navigator != null)
            {
                ApplyToMover(navigator, entering);
                return;
            }

            // 2. Legacy Support: Check for EnemyController (Simple script)
            var simpleController = col.GetComponent<EnemyController>();
            if (simpleController == null) simpleController = col.GetComponentInParent<EnemyController>();

            if (simpleController != null)
            {
                ApplyToMover(simpleController, entering);
                return;
            }

            // If we are debugging, log that we hit something relevant but found no motor
            if (debugLogs && entering && col.gameObject.layer != LayerMask.NameToLayer("Ground")) 
            {
                 // Filter out ground/static objects to reduce spam
                 Debug.LogWarning($"[SlowZone] Object '{col.name}' entered but has no SteeringNavigator or EnemyController.", this);
            }
        }

        private void ApplyToMover(Component mover, bool entering)
        {
            if (mover == null) return;

            if (entering)
            {
                if (_affectedMovers.Add(mover))
                {
                    SetSpeed(mover, slowMultiplier);
                    if (debugLogs) Debug.Log($"[SlowZone] Slowing {mover.gameObject.name}", this);
                }
            }
            else
            {
                if (_affectedMovers.Remove(mover))
                {
                    SetSpeed(mover, 1f); // Restore speed
                    if (debugLogs) Debug.Log($"[SlowZone] Releasing {mover.gameObject.name}", this);
                }
            }
        }

        private void SetSpeed(Component mover, float multiplier)
        {
            // 1. Check for SteeringNavigator
            if (mover is SteeringNavigator nav)
            {
                nav.SetSpeedMultiplier(multiplier);
                return;
            }

            // 2. Check for Legacy EnemyController
            if (mover is EnemyController ctrl)
            {
                ctrl.SetSpeedMultiplier(multiplier);
                return;
            }
        }

        private void OnDestroy()
        {
            // Restore speed to everyone still inside when the zone disappears
            foreach (var mover in _affectedMovers)
            {
                if (mover != null)
                {
                    SetSpeed(mover, 1f);
                }
            }
            _affectedMovers.Clear();
        }
    }
}