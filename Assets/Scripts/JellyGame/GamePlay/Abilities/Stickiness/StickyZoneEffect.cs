// FILEPATH: Assets/Scripts/Abilities/Zones/StickyZoneEffect.cs
using System.Collections.Generic;
using JellyGame.GamePlay.Enemy;
using JellyGame.GamePlay.Enemy.AI.Movement;
using UnityEngine;

namespace JellyGame.GamePlay.Abilities.Zones
{
    [DisallowMultipleComponent]
    public class StickyZoneEffect : MonoBehaviour, IZoneEffect
    {
        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        // Track which enemies are currently frozen
        private readonly Dictionary<Collider, Component> _frozenEnemies = new Dictionary<Collider, Component>();

        public void OnZoneSpawned(AbilityZone zone)
        {
            if (debugLogs)
                Debug.Log($"[StickyZoneEffect] Spawned. Will freeze enemies on contact.", this);
        }

        public void OnZoneDespawned(AbilityZone zone)
        {
            // Restore all frozen enemies before zone is destroyed
            foreach (var kvp in _frozenEnemies)
            {
                if (kvp.Value != null)
                {
                    RestoreEnemySpeed(kvp.Value);
                }
            }
            _frozenEnemies.Clear();
        }

        public void OnTargetEntered(Collider other)
        {
            if (other == null) return;

            // Check if we're already tracking this enemy
            if (_frozenEnemies.ContainsKey(other))
                return;

            // Try to find enemy movement component
            Component mover = FindEnemyMover(other);
            if (mover != null)
            {
                _frozenEnemies[other] = mover;
                FreezeEnemy(mover);

                if (debugLogs)
                    Debug.Log($"[StickyZoneEffect] Frozen: {other.name}", this);
            }
            else if (debugLogs)
            {
                Debug.Log($"[StickyZoneEffect] {other.name} has no EnemyController or SteeringNavigator.", this);
            }
        }

        public void OnTargetExited(Collider other)
        {
            if (other == null) return;

            if (_frozenEnemies.TryGetValue(other, out Component mover))
            {
                if (mover != null)
                {
                    RestoreEnemySpeed(mover);
                }
                _frozenEnemies.Remove(other);

                if (debugLogs)
                    Debug.Log($"[StickyZoneEffect] Unfrozen: {other.name}", this);
            }
        }

        public void Tick(float deltaTime)
        {
            // No per-tick logic needed - freezing is handled on enter/exit
        }

        private Component FindEnemyMover(Collider col)
        {
            // 1. Try SteeringNavigator (newer system)
            var navigator = col.GetComponent<SteeringNavigator>();
            if (navigator != null) return navigator;

            navigator = col.GetComponentInParent<SteeringNavigator>();
            if (navigator != null) return navigator;

            // 2. Try EnemyController (legacy system)
            var controller = col.GetComponent<EnemyController>();
            if (controller != null) return controller;

            controller = col.GetComponentInParent<EnemyController>();
            if (controller != null) return controller;

            return null;
        }

        private void FreezeEnemy(Component mover)
        {
            if (mover is SteeringNavigator nav)
            {
                nav.SetSpeedMultiplier(0f); // Completely freeze
            }
            else if (mover is EnemyController ctrl)
            {
                ctrl.SetSpeedMultiplier(0f); // Completely freeze
            }
        }

        private void RestoreEnemySpeed(Component mover)
        {
            if (mover is SteeringNavigator nav)
            {
                nav.SetSpeedMultiplier(1f); // Restore normal speed
            }
            else if (mover is EnemyController ctrl)
            {
                ctrl.SetSpeedMultiplier(1f); // Restore normal speed
            }
        }
    }
}

