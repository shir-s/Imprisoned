using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using JellyGame.GamePlay.Enemy.AI;
using JellyGame.GamePlay.Enemy.AI.Behaviors;


namespace JellyGame.GamePlay.Abilities.Stickiness
{
    /// <summary>
    /// Invisible trigger zone that slows enemies.
    /// Created when player fills a shape with stickiness ability active.
    /// Works with BoxCollider, MeshCollider, or any Collider.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class SlowZone : MonoBehaviour
    {
        [Header("Slow Effect")]
        [Tooltip(
            "Speed multiplier for enemies in this zone (0.15 = 15% speed = very slow, 0.5 = 50% speed = half speed)")]
        [Range(0.01f, 1f)]
        [SerializeField]
        private float slowMultiplier = 0.15f;

        [Header("Lifetime")] [Tooltip("How long this zone lasts (seconds). 0 = infinite")] [SerializeField]
        private float lifetime = 15f;

        [Header("Visual (Optional)")]
        [Tooltip("Material to show the sticky area (can be semi-transparent green)")]
        [SerializeField]
        private Material zoneMaterial;

        [SerializeField] private Color zoneColor = new Color(0, 1, 0, 0.3f);

        [Header("Debug")] [Tooltip("Show debug logs when enemies enter/exit")] [SerializeField]
        private bool debugLogs = false;

        private HashSet<MonoBehaviour> _affectedEnemies = new HashSet<MonoBehaviour>();
        private float _spawnTime;

        private void Start()
        {
            _spawnTime = Time.time;

            // Setup visual if material provided
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
            // Expire after lifetime
            if (lifetime > 0 && Time.time - _spawnTime >= lifetime)
            {
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// Set the slow multiplier for this zone (called when spawning)
        /// </summary>
        public void SetSlowMultiplier(float multiplier)
        {
            slowMultiplier = Mathf.Clamp01(multiplier);
        }

        private void OnTriggerEnter(Collider other)
        {
            ApplySlowEffect(other, true);
        }

        private void OnTriggerExit(Collider other)
        {
            ApplySlowEffect(other, false);
        }

        private void ApplySlowEffect(Collider col, bool entering)
        {
            if (col == null) return;

            // Try different enemy types your game might have
            // Check both on the collider's GameObject and its parent hierarchy

            // 1) Check for BehaviorManager (your AI enemies)
            var behaviorManager = col.GetComponent<BehaviorManager>();
            if (behaviorManager == null)
                behaviorManager = col.GetComponentInParent<BehaviorManager>();

            if (behaviorManager != null)
            {
                if (debugLogs)
                    Debug.Log(
                        $"[SlowZone] {(entering ? "ENTER" : "EXIT")}: {col.name} has BehaviorManager on {behaviorManager.gameObject.name}",
                        this);
                ApplyToEnemy(behaviorManager, entering);
                return;
            }

            // 2) Check for EnemyController (your patrol enemies)
            var enemyController = col.GetComponent<EnemyController>();
            if (enemyController == null)
                enemyController = col.GetComponentInParent<EnemyController>();

            if (enemyController != null)
            {
                if (debugLogs)
                    Debug.Log(
                        $"[SlowZone] {(entering ? "ENTER" : "EXIT")}: {col.name} has EnemyController on {enemyController.gameObject.name}",
                        this);
                ApplyToEnemy(enemyController, entering);
                return;
            }

            // 3) Check for MonsterAStarFollower
            var monster = col.GetComponent<MonsterAStarFollower>();
            if (monster == null)
                monster = col.GetComponentInParent<MonsterAStarFollower>();

            if (monster != null)
            {
                if (debugLogs)
                    Debug.Log(
                        $"[SlowZone] {(entering ? "ENTER" : "EXIT")}: {col.name} has MonsterAStarFollower on {monster.gameObject.name}",
                        this);
                ApplyToEnemy(monster, entering);
                return;
            }

            if (debugLogs)
                Debug.LogWarning(
                    $"[SlowZone] {(entering ? "ENTER" : "EXIT")}: {col.name} has no recognized enemy component! GameObject: {col.gameObject.name}, Parent: {(col.transform.parent != null ? col.transform.parent.name : "none")}",
                    this);
        }

        private void ApplyToEnemy(MonoBehaviour enemy, bool entering)
        {
            if (enemy == null) return;

            if (entering)
            {
                if (_affectedEnemies.Add(enemy))
                {
                    // Slow this enemy
                    SetEnemySpeedMultiplier(enemy, slowMultiplier);
                }
            }
            else
            {
                if (_affectedEnemies.Remove(enemy))
                {
                    // Restore normal speed
                    SetEnemySpeedMultiplier(enemy, 1f);
                }
            }
        }

        private void SetEnemySpeedMultiplier(MonoBehaviour enemy, float multiplier)
        {
            if (enemy == null) return;

            // Get the GameObject that has the enemy component
            GameObject enemyObj = enemy.gameObject;
            bool foundAnyBehavior = false;

            // For WanderBehavior (on the same GameObject)
            var wander = enemyObj.GetComponent<WanderBehavior>();
            if (wander != null)
            {
                wander.SetSpeedMultiplier(multiplier);
                foundAnyBehavior = true;
                if (debugLogs)
                    Debug.Log($"[SlowZone] Applied speed multiplier {multiplier} to WanderBehavior on {enemyObj.name}",
                        this);
            }

            // For HuntBehavior
            var hunt = enemyObj.GetComponent<HuntBehavior>();
            if (hunt != null)
            {
                hunt.SetSpeedMultiplier(multiplier);
                foundAnyBehavior = true;
                if (debugLogs)
                    Debug.Log($"[SlowZone] Applied speed multiplier {multiplier} to HuntBehavior on {enemyObj.name}",
                        this);
            }

            /*// For AttackBehavior
            var attack = enemyObj.GetComponent<AttackBehavior>();
            if (attack != null)
            {
                attack.SetSpeedMultiplier(multiplier);
                foundAnyBehavior = true;
                if (debugLogs)
                    Debug.Log($"[SlowZone] Applied speed multiplier {multiplier} to AttackBehavior on {enemyObj.name}",
                        this);
            }*/

            // For TravelBehavior
            var travel = enemyObj.GetComponent<TravelBehavior>();
            if (travel != null)
            {
                travel.SetSpeedMultiplier(multiplier);
                foundAnyBehavior = true;
                if (debugLogs)
                    Debug.Log($"[SlowZone] Applied speed multiplier {multiplier} to TravelBehavior on {enemyObj.name}",
                        this);
            }

            // For FollowStrokeBehavior
            var followStroke = enemyObj.GetComponent<FollowStrokeBehavior>();
            if (followStroke != null)
            {
                followStroke.SetSpeedMultiplier(multiplier);
                foundAnyBehavior = true;
                if (debugLogs)
                    Debug.Log(
                        $"[SlowZone] Applied speed multiplier {multiplier} to FollowStrokeBehavior on {enemyObj.name}",
                        this);
            }

            // For EnemyController
            var controller = enemyObj.GetComponent<EnemyController>();
            if (controller != null)
            {
                controller.SetSpeedMultiplier(multiplier);
                foundAnyBehavior = true;
                if (debugLogs)
                    Debug.Log($"[SlowZone] Applied speed multiplier {multiplier} to EnemyController on {enemyObj.name}",
                        this);
            }

            // For MonsterAStarFollower
            var monster = enemyObj.GetComponent<MonsterAStarFollower>();
            if (monster != null)
            {
                monster.SetSpeedMultiplier(multiplier);
                foundAnyBehavior = true;
                if (debugLogs)
                    Debug.Log(
                        $"[SlowZone] Applied speed multiplier {multiplier} to MonsterAStarFollower on {enemyObj.name}",
                        this);
            }

            if (!foundAnyBehavior && debugLogs)
            {
                Debug.LogWarning(
                    $"[SlowZone] No speed-multiplier-compatible behavior found on {enemyObj.name}! Available components: {string.Join(", ", enemyObj.GetComponents<MonoBehaviour>().Select(c => c.GetType().Name))}",
                    this);
            }
        }

        private void OnDestroy()
        {
            // Restore all affected enemies to normal speed
            foreach (var enemy in _affectedEnemies)
            {
                if (enemy != null)
                {
                    SetEnemySpeedMultiplier(enemy, 1f);
                }
            }

            _affectedEnemies.Clear();
        }
    }
}