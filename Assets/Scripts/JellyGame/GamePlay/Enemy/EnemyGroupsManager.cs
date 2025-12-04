using System.Collections.Generic;
using JellyGame.GamePlay.Utils;
using UnityEngine;

namespace JellyGame.GamePlay.Enemy
{
     public class EnemyGroupsManager : MonoBehaviour
    {
        [SerializeField] private List<EnemyGroupConfig> groups = new List<EnemyGroupConfig>();

        readonly Dictionary<EnemyHealth, EnemyGroupConfig> _enemyToGroup =
            new Dictionary<EnemyHealth, EnemyGroupConfig>();

        int _totalCountedEnemies;
        int _deadCountedEnemies;
        bool _firstEnemyDiedRaised;

        void OnEnable()
        {
            _enemyToGroup.Clear();
            _totalCountedEnemies = 0;
            _deadCountedEnemies = 0;
            _firstEnemyDiedRaised = false;

            foreach (var group in groups)
            {
                if (group == null) continue;

                for (int i = group.enemies.Count - 1; i >= 0; i--)
                {
                    var enemy = group.enemies[i];
                    if (enemy == null)
                    {
                        group.enemies.RemoveAt(i);
                        continue;
                    }

                    if (_enemyToGroup.ContainsKey(enemy))
                        continue;

                    _enemyToGroup.Add(enemy, group);
                    enemy.EnemyDied += OnEnemyDied;

                    if (group.countTowardsAll)
                        _totalCountedEnemies++;
                }
            }
        }

        void OnDisable()
        {
            foreach (var kvp in _enemyToGroup)
            {
                if (kvp.Key != null)
                    kvp.Key.EnemyDied -= OnEnemyDied;
            }

            _enemyToGroup.Clear();
        }

        void OnEnemyDied(EnemyHealth enemy)
        {
            if (!_enemyToGroup.TryGetValue(enemy, out var group))
                return;

            enemy.EnemyDied -= OnEnemyDied;
            _enemyToGroup.Remove(enemy);
            group.enemies.Remove(enemy);

            if (group.countTowardsAll)
            {
                _deadCountedEnemies++;
                if (_totalCountedEnemies > 0 &&
                    _deadCountedEnemies == _totalCountedEnemies)
                {
                    JellyGameEvents.AllEnemiesDied?.Invoke();
                }
            }

            if (group.raisesFirstEnemyDied && !_firstEnemyDiedRaised)
            {
                _firstEnemyDiedRaised = true;
                JellyGameEvents.FirstEnemyDied?.Invoke();
            }

            if (group.enemies.Count == 0)
            {
                group.onGroupCleared?.Invoke();
            }
        }
    }
}