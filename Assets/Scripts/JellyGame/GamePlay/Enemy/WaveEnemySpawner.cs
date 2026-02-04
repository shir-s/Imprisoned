using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using JellyGame.GamePlay.Utils;
using JellyGame.GamePlay.Enemy.AI.Behaviors;

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

            [Tooltip("Spawn points for this wave. Assign at least one per wave.")]
            public Transform[] waveSpawnPoints = new Transform[0];
        }

        public enum SpawnPointMode
        {
            Cycle,      // Use spawn points in order, cycling through them
            Random,     // Pick random spawn point each time
            AllAtOnce   // Spawn all enemies at all spawn points simultaneously
        }

        [Header("Waves")]
        [Tooltip("Wave configurations. Each wave can have one or more enemy types.")]
        [SerializeField] private WaveConfig[] waves = new WaveConfig[0];

        [Header("Start Settings")]
        [Tooltip("Start spawning waves automatically when this component is enabled")]
        [SerializeField] private bool startOnEnable = true;
        
        [Tooltip("Seconds to wait before starting the first wave")]
        [SerializeField] private float delayBeforeFirstWave = 1f;

        [Header("Repeat Waves")]
        [Tooltip("If true, after the last wave finishes we wait 'Delay Between Cycles' then start from the first wave again (endless loop).")]
        [SerializeField] private bool loopWaves = false;
        
        [Tooltip("Seconds to wait between cycles when looping (after last wave, before first wave again).")]
        [SerializeField] private float delayBetweenCycles = 10f;

        [Header("Spawn Settings")]
        [Tooltip("How to choose spawn points")]
        [SerializeField] private SpawnPointMode spawnPointMode = SpawnPointMode.Cycle;
        
        [Tooltip("Parent spawned enemies under this transform (null = no parent)")]
        [SerializeField] private Transform parentForSpawnedEnemies = null;

        [Tooltip("Optional. If set, spawned enemies with SingleTargetBehavior will have their target set to this (e.g. empty at center of square). Fixes wrong location when target is a prefab.")]
        [SerializeField] private Transform runtimeTargetForSingleTarget = null;

        [Header("Win Condition")]
        [Tooltip("If true and not looping, track spawned enemies and fire AllEnemiesDied when all waves are done AND all enemies are dead. When Loop Waves is on, win is never fired.")]
        [SerializeField] private bool trackSpawnedEnemiesForWin = true;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = true;

        // Runtime tracking
        private List<EnemyHealth> _spawnedEnemies = new List<EnemyHealth>();
        private int _currentWaveIndex = -1;
        private bool _isSpawning = false;
        private bool _allWavesCompleted = false;
        private int _nextSpawnPointIndex = 0;
        private Transform[] _currentWaveSpawnPoints;

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

            // Run waves (once or loop)
            while (true)
            {
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

                    // Use this wave's spawn points
                    _currentWaveSpawnPoints = wave.waveSpawnPoints;
                    _nextSpawnPointIndex = 0;

                    if (debugLogs)
                        Debug.Log($"[WaveEnemySpawner] Starting wave {i + 1}/{waves.Length}: {wave.waveName} (spawn points: {(_currentWaveSpawnPoints != null ? _currentWaveSpawnPoints.Length : 0)})", this);

                    // Spawn all entries in this wave
                    yield return StartCoroutine(SpawnWaveEntries(wave));

                    if (debugLogs) Debug.Log($"[WaveEnemySpawner] Wave {i + 1} ({wave.waveName}) completed.", this);
                }

                if (!loopWaves)
                    break;

                // Delay before starting the first wave again
                if (debugLogs) Debug.Log($"[WaveEnemySpawner] Loop: waiting {delayBetweenCycles}s before next cycle...", this);
                yield return new WaitForSeconds(delayBetweenCycles);
            }

            _allWavesCompleted = true;
            _isSpawning = false;

            if (debugLogs) Debug.Log("[WaveEnemySpawner] All waves completed!", this);

            // Check if we need to wait for enemies to die (only when not looping)
            if (trackSpawnedEnemiesForWin)
            {
                yield return StartCoroutine(WaitForAllEnemiesDead());
            }
        }

        private IEnumerator SpawnWaveEntries(WaveConfig wave)
        {
            if (_currentWaveSpawnPoints == null || _currentWaveSpawnPoints.Length == 0)
            {
                Debug.LogError("[WaveEnemySpawner] No spawn points assigned for this wave! Expand the wave in the Inspector and add at least one Transform to 'Wave Spawn Points'. Skipping this wave.", this);
                yield break;
            }

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

                    // Set runtime target for SingleTargetBehavior (so prefab doesn't need scene reference)
                    if (runtimeTargetForSingleTarget != null)
                    {
                        var singleTarget = spawned.GetComponent<SingleTargetBehavior>();
                        if (singleTarget != null)
                            singleTarget.SetTarget(runtimeTargetForSingleTarget);
                    }

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
            if (_currentWaveSpawnPoints == null || _currentWaveSpawnPoints.Length == 0)
                return null;

            Transform point = null;

            switch (spawnPointMode)
            {
                case SpawnPointMode.Cycle:
                    point = _currentWaveSpawnPoints[_nextSpawnPointIndex];
                    _nextSpawnPointIndex = (_nextSpawnPointIndex + 1) % _currentWaveSpawnPoints.Length;
                    break;

                case SpawnPointMode.Random:
                    point = _currentWaveSpawnPoints[Random.Range(0, _currentWaveSpawnPoints.Length)];
                    break;

                case SpawnPointMode.AllAtOnce:
                    point = _currentWaveSpawnPoints[_nextSpawnPointIndex];
                    _nextSpawnPointIndex = (_nextSpawnPointIndex + 1) % _currentWaveSpawnPoints.Length;
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
            if (waves == null) return;
            for (int w = 0; w < waves.Length; w++)
            {
                if (waves[w]?.waveSpawnPoints == null) continue;
                Gizmos.color = w == 0 ? Color.green : (w == 1 ? Color.blue : Color.yellow);
                foreach (Transform point in waves[w].waveSpawnPoints)
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