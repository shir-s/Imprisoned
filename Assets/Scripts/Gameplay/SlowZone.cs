using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Invisible trigger zone that slows enemies.
/// Created when player fills a shape with stickiness ability active.
/// </summary>
[RequireComponent(typeof(MeshCollider))]
public class SlowZone : MonoBehaviour
{
    [Header("Slow Effect")]
    [Tooltip("Speed multiplier for enemies in this zone (0.3 = 30% speed)")]
    [SerializeField] private float slowMultiplier = 0.3f;

    [Header("Lifetime")]
    [Tooltip("How long this zone lasts (seconds). 0 = infinite")]
    [SerializeField] private float lifetime = 15f;

    [Header("Visual (Optional)")]
    [Tooltip("Material to show the sticky area (can be semi-transparent green)")]
    [SerializeField] private Material zoneMaterial;

    [SerializeField] private Color zoneColor = new Color(0, 1, 0, 0.3f);

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
        // Try different enemy types your game might have
        
        // 1) Check for BehaviorManager (your AI enemies)
        var behaviorManager = col.GetComponentInParent<BehaviorManager>();
        if (behaviorManager != null)
        {
            ApplyToEnemy(behaviorManager, entering);
            return;
        }

        // 2) Check for EnemyController (your patrol enemies)
        var enemyController = col.GetComponentInParent<EnemyController>();
        if (enemyController != null)
        {
            ApplyToEnemy(enemyController, entering);
            return;
        }

        // 3) Check for MonsterAStarFollower
        var monster = col.GetComponentInParent<MonsterAStarFollower>();
        if (monster != null)
        {
            ApplyToEnemy(monster, entering);
            return;
        }
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
        // For WanderBehavior
        var wander = enemy.GetComponent<WanderBehavior>();
        if (wander != null)
        {
            wander.SetSpeedMultiplier(multiplier);
        }

        // For HuntBehavior
        var hunt = enemy.GetComponent<HuntBehavior>();
        if (hunt != null)
        {
            hunt.SetSpeedMultiplier(multiplier);
        }

        // For AttackBehavior
        var attack = enemy.GetComponent<AttackBehavior>();
        if (attack != null)
        {
            attack.SetSpeedMultiplier(multiplier);
        }

        // For TravelBehavior
        var travel = enemy.GetComponent<TravelBehavior>();
        if (travel != null)
        {
            travel.SetSpeedMultiplier(multiplier);
        }

        // For EnemyController
        if (enemy is EnemyController controller)
        {
            controller.SetSpeedMultiplier(multiplier);
        }

        // For MonsterAStarFollower
        var monster = enemy as MonsterAStarFollower;
        if (monster != null)
        {
            monster.SetSpeedMultiplier(multiplier);
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

