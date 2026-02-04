using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using JellyGame.GamePlay.Utils;

namespace JellyGame.GamePlay.Enemy
{
    /// <summary>
    /// Spawns waves of enemies at marked spawn points.
    /// Supports both single enemy type per wave and mixed enemy types per wave.
    /// </summary>
    public class WaveEnemySpawner : MonoBehaviour
    {
        [System.Serializable]
        public class WaveEntry
        {
            [Tooltip("Enemy prefab to spawn")]
            public GameObject enemyPrefab;
            
            [Tooltip("How many of this enemy type to spawn in this wave")]
            public int count = 1;
        }

        [System.Serializable]
        public class WaveConfig
        {
            [Tooltip("Optional name for this wave (for debugging)")]
            public string waveName = "Wave";
            
            [Tooltip("Enemy types and counts for this wave. One entry = one enemy type, multiple entries = mixed types")]
            public WaveEntry[] entries = new WaveEntry[1];
            
            [Tooltip("Seconds to wait before starting this wave (after previous wave finishes)")]
            public float delayBeforeWave = 0f;
            
            [Tooltip("Seconds between each spawn within this wave (0 = spawn all at once)")]
            public float delayBetweenSpawns = 0.5f;
        }

        public enum SpawnPointMode
        {
            Cycle,      // Use spawn points in order, cycling through them
            Random,     // Pick random spawn point each time
            AllAtOnce   // Spawn all enemies at all spawn points simultaneously
        }

        [Header("Spawn Points")]
        [Tooltip("Marked points on the map where enemies will appear. Drag empty GameObjects here.")]
        [SerializeField] private Transform[] spawnPoints = new Transform[0];

        [Header("Waves")]
        [Tooltip("Wave configurations. Each wave can have one or more enemy types.")]
        [SerializeField] private WaveConfig[] waves = new WaveConfig[0];

        [Header("Start Settings")]
        [Tooltip("Start spawning waves automatically when this component is enabled")]
        [SerializeField] private bool startOnEnable = true;
        
        [Tooltip("Seconds to wait before starting the first wave")]
        [SerializeField] private float delayBeforeFirstWave = 1f;

        [Header("Spawn Settings")]
        [Tooltip("How to choose spawn points")]
        [SerializeField] private SpawnPointMode spawnPointMode = SpawnPointMode.Cycle;
        
        [Tooltip("Parent spawned enemies under this transform (null = no parent)")]
        [SerializeField] private Transform parentForSpawnedEnemies = null;

        [Header("Win Condition")]
        [Tooltip("If true, track all spawned enemies and fire AllEnemiesDied when all waves are done AND all enemies are dead")]
        [SerializeField] private bool trackSpawnedEnemiesForWin = true;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = true;

        // Runtime tracking
        private List<EnemyHealth> _spawnedEnemies = new List<EnemyHealth>();
        private int _currentWaveIndex = -1;
        private bool _isSpawning = false;
        private bool _allWavesCompleted = false;
        private int _nextSpawnPointIndex = 0;

        private void OnEnable()
        {
            if (startOnEnable)
            {
                StartWaves();
            }
        }

        private void OnDisable()
        {
            StopAllCoroutines();
        }

        /// <summary>
        /// Start spawning waves (can be called manually or automatically on enable)
        /// </summary>
        public void StartWaves()
        {
            if (_isSpawning)
            {
                if (debugLogs) Debug.LogWarning("[WaveEnemySpawner] Already spawning waves!", this);
                return;
            }

            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                Debug.LogError("[WaveEnemySpawner] No spawn points assigned! Cannot spawn enemies.", this);
                return;
            }

            if (waves == null || waves.Length == 0)
            {
                Debug.LogError("[WaveEnemySpawner] No waves configured! Cannot spawn enemies.", this);
                return;
            }

            _isSpawning = true;
            _currentWaveIndex = -1;
            _allWavesCompleted = false;
            _spawnedEnemies.Clear();
            _nextSpawnPointIndex = 0;

            StartCoroutine(SpawnWavesCoroutine());
        }

        private IEnumerator SpawnWavesCoroutine()
        {
            if (debugLogs) Debug.Log($"[WaveEnemySpawner] Starting wave spawner. Total waves: {waves.Length}", this);

            // Wait before first wave
            if (delayBeforeFirstWave > 0f)
            {
                if (debugLogs) Debug.Log($"[WaveEnemySpawner] Waiting {delayBeforeFirstWave}s before first wave...", this);
                yield return new WaitForSeconds(delayBeforeFirstWave);
            }

            // Spawn each wave
            for (int i = 0; i < waves.Length; i++)
            {
                _currentWaveIndex = i;
                WaveConfig wave = waves[i];

                if (wave == null || wave.entries == null || wave.entries.Length == 0)
                {
                    if (debugLogs) Debug.LogWarning($"[WaveEnemySpawner] Wave {i} is null or has no entries. Skipping.", this);
                    continue;
                }

                // Wait before this wave
                if (wave.delayBeforeWave > 0f)
                {
                    if (debugLogs) Debug.Log($"[WaveEnemySpawner] Waiting {wave.delayBeforeWave}s before wave {i + 1} ({wave.waveName})...", this);
                    yield return new WaitForSeconds(wave.delayBeforeWave);
                }

                if (debugLogs) Debug.Log($"[WaveEnemySpawner] Starting wave {i + 1}/{waves.Length}: {wave.waveName}", this);

                // Spawn all entries in this wave
                yield return StartCoroutine(SpawnWaveEntries(wave));

                if (debugLogs) Debug.Log($"[WaveEnemySpawner] Wave {i + 1} ({wave.waveName}) completed.", this);
            }

            _allWavesCompleted = true;
            _isSpawning = false;

            if (debugLogs) Debug.Log("[WaveEnemySpawner] All waves completed!", this);

            // Check if we need to wait for enemies to die
            if (trackSpawnedEnemiesForWin)
            {
                yield return StartCoroutine(WaitForAllEnemiesDead());
            }
        }

        private IEnumerator SpawnWaveEntries(WaveConfig wave)
        {
            foreach (WaveEntry entry in wave.entries)
            {
                if (entry == null || entry.enemyPrefab == null)
                {
                    if (debugLogs) Debug.LogWarning("[WaveEnemySpawner] Wave entry is null or has no prefab. Skipping.", this);
                    continue;
                }

                // Spawn 'count' instances of this enemy type
                for (int i = 0; i < entry.count; i++)
                {
                    Transform spawnPoint = GetNextSpawnPoint();
                    if (spawnPoint == null)
                    {
                        Debug.LogError("[WaveEnemySpawner] No valid spawn point found!", this);
                        continue;
                    }

                    GameObject spawned = Instantiate(entry.enemyPrefab, spawnPoint.position, spawnPoint.rotation, parentForSpawnedEnemies);
                    
                    if (debugLogs) Debug.Log($"[WaveEnemySpawner] Spawned {entry.enemyPrefab.name} at {spawnPoint.position}", this);

                    // Track enemy for win condition
                    if (trackSpawnedEnemiesForWin)
                    {
                        EnemyHealth health = spawned.GetComponent<EnemyHealth>();
                        if (health != null)
                        {
                            _spawnedEnemies.Add(health);
                            health.EnemyDied += OnSpawnedEnemyDied;
                        }
                    }

                    // Wait between spawns
                    if (wave.delayBetweenSpawns > 0f && i < entry.count - 1)
                    {
                        yield return new WaitForSeconds(wave.delayBetweenSpawns);
                    }
                }
            }
        }

        private Transform GetNextSpawnPoint()
        {
            if (spawnPoints == null || spawnPoints.Length == 0)
                return null;

            Transform point = null;

            switch (spawnPointMode)
            {
                case SpawnPointMode.Cycle:
                    point = spawnPoints[_nextSpawnPointIndex];
                    _nextSpawnPointIndex = (_nextSpawnPointIndex + 1) % spawnPoints.Length;
                    break;

                case SpawnPointMode.Random:
                    point = spawnPoints[Random.Range(0, spawnPoints.Length)];
                    break;

                case SpawnPointMode.AllAtOnce:
                    // This mode is handled differently - spawns at all points simultaneously
                    // For now, just cycle (you can extend this if needed)
                    point = spawnPoints[_nextSpawnPointIndex];
                    _nextSpawnPointIndex = (_nextSpawnPointIndex + 1) % spawnPoints.Length;
                    break;
            }

            return point;
        }

        private void OnSpawnedEnemyDied(EnemyHealth enemy)
        {
            if (_spawnedEnemies.Contains(enemy))
            {
                enemy.EnemyDied -= OnSpawnedEnemyDied;
                _spawnedEnemies.Remove(enemy);

                if (debugLogs) Debug.Log($"[WaveEnemySpawner] Enemy died. Remaining: {_spawnedEnemies.Count}", this);
            }
        }

        private IEnumerator WaitForAllEnemiesDead()
        {
            if (debugLogs) Debug.Log("[WaveEnemySpawner] Waiting for all spawned enemies to die...", this);

            while (_spawnedEnemies.Count > 0)
            {
                yield return new WaitForSeconds(0.5f); // Check every 0.5 seconds
            }

            if (debugLogs) Debug.Log("[WaveEnemySpawner] All spawned enemies are dead!", this);

            // Fire win event (same as EnemyGroupsManager does)
            JellyGameEvents.AllEnemiesDied?.Invoke();
        }

        // Public API for external control
        public bool IsSpawning => _isSpawning;
        public bool AllWavesCompleted => _allWavesCompleted;
        public int CurrentWaveIndex => _currentWaveIndex;
        public int RemainingEnemies => _spawnedEnemies.Count;

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Draw spawn points in editor
            if (spawnPoints != null)
            {
                Gizmos.color = Color.red;
                foreach (Transform point in spawnPoints)
                {
                    if (point != null)
                    {
                        Gizmos.DrawWireSphere(point.position, 0.5f);
                        Gizmos.DrawLine(point.position, point.position + Vector3.up * 2f);
                    }
                }
            }
        }
#endif
    }
}