// FILEPATH: Assets/Scripts/Managers/GameSceneManager.cs
using System;
using System.Collections;
using JellyGame.GamePlay.Audio.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace JellyGame.GamePlay.Managers
{
    /// <summary>
    /// Manages scene transitions between Levels, GameOver, and Win scenes.
    /// Also listens to EntityDied and triggers GameOver if the victim layer matches.
    /// Win is triggered by listening to GameWin or via a cheat key.
    ///
    /// v2:
    /// - Supports multiple levels (tutorial, level 1, future levels).
    /// - Win scene can have "Restart" (restart current level) and "Next Level" buttons.
    /// - Current level is stored in PlayerPrefs so Win/GameOver scenes can load the right level.
    /// </summary>
    public class GameSceneManager : MonoBehaviour
    {
        private const string PlayerPrefsLevelKey = "JellyGame.CurrentLevelIndex";

        [Header("Levels (Ordered)")]
        [Tooltip("Build indices of your playable levels in order: Tutorial, Level1, Level2, ...")]
        [SerializeField] private int[] levelSceneBuildIndices = new int[] { 0 };

        [Header("Win Cutscenes (Per Level)")]
        [Tooltip("Build indices of win cutscene scenes in the SAME order as Levels. If empty, falls back to Win scene.")]
        [SerializeField] private int[] winCutsceneSceneBuildIndices = new int[] { };
        
        [Tooltip("If a level has no win cutscene configured, load the NEXT playable level instead of the Win scene.")]
        [SerializeField] private bool ifNoWinCutsceneLoadNextLevel = true;


        [Tooltip("If true, we load the per-level win cutscene scene on win (if configured). If false, uses Win scene as before.")]
        
        [SerializeField] private bool usePerLevelWinCutsceneScenes = true;
        [Tooltip("If true, when NextLevel is pressed on the last level, it loops back to the first level.")]
        [SerializeField] private bool loopToFirstLevelIfNoNext = false;

        [Tooltip("If true, when a level scene is loaded, we detect its index in 'levelSceneBuildIndices' and store it as current.")]
        [SerializeField] private bool autoDetectCurrentLevelFromActiveScene = true;

        [Header("Non-Level Scenes (Use Build Index)")]
        [Tooltip("Build index of the game over scene. Check Build Settings to see the index number.")]
        [SerializeField] private int gameOverSceneBuildIndex = 1;

        [Tooltip("Build index of the win scene. Check Build Settings to see the index number.")]
        [SerializeField] private int winSceneBuildIndex = 2;

        [Header("Scene Names (Fallback)")]
        [Tooltip("Name of the game over scene (used as fallback if build index fails)")]
        [SerializeField] private string gameOverSceneName = "GameOver";

        [Tooltip("Name of the win scene (used as fallback if build index fails)")]
        [SerializeField] private string winSceneName = "Win";

        [Header("GameOver Trigger")]
        [Tooltip("If an EntityDied event is triggered with a victim layer in this mask => trigger GameOver.")]
        [SerializeField] private LayerMask gameOverOnVictimLayers;

        [Header("Win Trigger")]
        [Tooltip("If true, listens to EventManager.GameEvent.GameWin.")]
        [SerializeField] private bool listenToWinEvent = true;

        [Header("Win FX (optional)")]
        [Tooltip("Assign the ROOT GameObject that contains the win particles (can have multiple ParticleSystem children).")]
        [SerializeField] private GameObject winFxRoot;

        [Tooltip("If true, the FX root is disabled on start so nothing can PlayOnAwake.\nIt will be enabled only when Win is triggered.")]
        [SerializeField] private bool disableWinFxRootOnStart = true;

        [Tooltip("If true, we also force-stop and clear all ParticleSystems under winFxRoot on start.")]
        [SerializeField] private bool stopWinFxOnStart = true;

        [Tooltip("Delay (seconds) after win is triggered before loading the Win scene.\nIf winFxRoot is assigned and this is <= 0, we will auto-compute a delay from the longest ParticleSystem.")]
        [SerializeField] private float winSceneLoadDelay = 0f;

        [Tooltip("If true, pause gameplay during win.\nNote: ParticleSystem uses scaled time by default, so if you pause and your particles are scaled-time, they may freeze.")]
        [SerializeField] private bool pauseGameplayOnWin = false;

        [Header("Cheat Codes")]
        [Tooltip("Key to press to trigger GameOver (cheat code)")]
        [SerializeField] private KeyCode gameOverKey = KeyCode.R;

        [Tooltip("Key to press to trigger Win (cheat code)")]
        [SerializeField] private KeyCode winKey = KeyCode.T;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = true;

        private bool _winSequenceRunning;

        private void Awake()
        {
            PrepareWinFxForStart();
        }

        private void Start()
        {
            // Background music (kept from your original)
            SoundManager.Instance.StopAllSounds();
            if (SoundManager.Instance.FindAudioConfig("Background") != null)
                SoundManager.Instance.PlaySound("Background", this.transform);

            // IMPORTANT: never auto-detect current level from Win/GameOver scenes
            int activeBuildIndex = SceneManager.GetActiveScene().buildIndex;
            if (activeBuildIndex == winSceneBuildIndex || activeBuildIndex == gameOverSceneBuildIndex)
                return;

            if (autoDetectCurrentLevelFromActiveScene)
                TryDetectAndStoreCurrentLevelFromActiveScene();
        }

        private void OnEnable()
        {
            EventManager.StartListening(EventManager.GameEvent.EntityDied, OnEntityDiedEvent);
            EventManager.StartListening(EventManager.GameEvent.GameOver, OnGameOverEvent);

            if (listenToWinEvent)
                EventManager.StartListening(EventManager.GameEvent.GameWin, OnWinEvent);

            if (debugLogs)
                Debug.Log($"[GameSceneManager] Enabled in scene: {SceneManager.GetActiveScene().name}", this);
        }

        private void OnDisable()
        {
            EventManager.StopListening(EventManager.GameEvent.EntityDied, OnEntityDiedEvent);
            EventManager.StopListening(EventManager.GameEvent.GameOver, OnGameOverEvent);

            if (listenToWinEvent)
                EventManager.StopListening(EventManager.GameEvent.GameWin, OnWinEvent);
        }

        private void Update()
        {
            if (Input.GetKeyDown(gameOverKey))
            {
                if (debugLogs)
                    Debug.Log($"[GameSceneManager] Cheat code pressed ({gameOverKey})! Triggering GameOver", this);

                EventManager.TriggerEvent(EventManager.GameEvent.GameOver);
            }

            if (Input.GetKeyDown(winKey))
            {
                if (debugLogs)
                    Debug.Log($"[GameSceneManager] Cheat code pressed ({winKey})! Triggering Win", this);

                EventManager.TriggerEvent(EventManager.GameEvent.GameWin);
            }
        }

        private void PrepareWinFxForStart()
        {
            if (winFxRoot == null)
                return;

            if (disableWinFxRootOnStart && winFxRoot.activeSelf)
                winFxRoot.SetActive(false);

            if (stopWinFxOnStart)
                StopAndClearAllWinFx(includeInactive: true);
        }

        private void StopAndClearAllWinFx(bool includeInactive)
        {
            if (winFxRoot == null)
                return;

            var systems = winFxRoot.GetComponentsInChildren<ParticleSystem>(includeInactive);
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] == null) continue;
                systems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void TryDetectAndStoreCurrentLevelFromActiveScene()
        {
            int activeBuildIndex = SceneManager.GetActiveScene().buildIndex;

            // Hard block: if the active scene is not one of the playable levels, don't touch the stored level index.
            int idx = FindLevelListIndexByBuildIndex(activeBuildIndex);
            if (idx < 0)
            {
                if (debugLogs)
                    Debug.Log($"[GameSceneManager] Active scene buildIndex={activeBuildIndex} is not a playable level (not in Levels list). Not updating CurrentLevelIndex.", this);
                return;
            }

            SetCurrentLevelIndex(idx);

            if (debugLogs)
                Debug.Log($"[GameSceneManager] Detected current level: listIndex={idx}, buildIndex={activeBuildIndex}", this);
        }


        private void OnEntityDiedEvent(object eventData)
        {
            if (eventData is not EntityDiedEventData died)
            {
                if (debugLogs)
                    Debug.LogWarning("[GameSceneManager] EntityDied received with unexpected payload type.", this);
                return;
            }

            bool match = (gameOverOnVictimLayers.value & (1 << died.VictimLayer)) != 0;
            if (!match)
                return;

            if (debugLogs)
                Debug.Log($"[GameSceneManager] EntityDied matched layer {died.VictimLayer} ({LayerMask.LayerToName(died.VictimLayer)}). Triggering GameOver.", this);

            EventManager.TriggerEvent(EventManager.GameEvent.GameOver);
        }

        private void OnGameOverEvent(object _)
        {
            if (debugLogs)
                Debug.Log($"[GameSceneManager] GameOver event received! Loading scene index: {gameOverSceneBuildIndex}", this);

            LoadGameOverScene();
        }

        private void OnWinEvent(object _)
        {
            if (_winSequenceRunning)
                return;

            _winSequenceRunning = true;
            StartCoroutine(WinSequence());
        }

        private IEnumerator WinSequence()
        {
            if (debugLogs)
                Debug.Log("[GameSceneManager] Win sequence started.", this);

            // SPECIAL CASE: Win triggered inside the GameOver scene (portal).
            // Always go to the FIRST playable level (index 0 in levels list).
            if (IsInGameOverScene())
            {
                if (levelSceneBuildIndices == null || levelSceneBuildIndices.Length == 0)
                {
                    Debug.LogError("[GameSceneManager] Cannot leave GameOver: levelSceneBuildIndices is empty.", this);
                    yield break;
                }

                int firstLevelBuildIndex = levelSceneBuildIndices[0];
                if (!IsValidBuildIndex(firstLevelBuildIndex))
                {
                    Debug.LogError($"[GameSceneManager] Cannot leave GameOver: first level buildIndex={firstLevelBuildIndex} is invalid.", this);
                    yield break;
                }

                // Reset "current level" to 0 so future NextLevel logic is consistent.
                SetCurrentLevelIndex(0);

                if (debugLogs)
                    Debug.Log($"[GameSceneManager] Win in GameOver scene -> loading FIRST level buildIndex={firstLevelBuildIndex}.", this);

                SceneManager.LoadScene(firstLevelBuildIndex);
                yield break;
            }

            
            // Store current level from the WINNING scene (this object still exists there).
            if (autoDetectCurrentLevelFromActiveScene)
                TryDetectAndStoreCurrentLevelFromActiveScene();

            float delay = winSceneLoadDelay;

            if (winFxRoot != null)
            {
                if (!winFxRoot.activeInHierarchy)
                    winFxRoot.SetActive(true);

                ParticleSystem[] systems = winFxRoot.GetComponentsInChildren<ParticleSystem>(true);

                if (systems != null && systems.Length > 0)
                {
                    for (int i = 0; i < systems.Length; i++)
                    {
                        if (systems[i] == null) continue;
                        systems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    }

                    for (int i = 0; i < systems.Length; i++)
                    {
                        if (systems[i] == null) continue;
                        systems[i].Play(true);
                    }

                    if (delay <= 0f)
                        delay = ComputeLongestFxDurationSeconds(systems);
                }
                else if (debugLogs)
                {
                    Debug.LogWarning("[GameSceneManager] winFxRoot assigned but no ParticleSystem found in children.", this);
                }
            }

            if (pauseGameplayOnWin)
                Time.timeScale = 0f;

            if (delay > 0f)
            {
                if (pauseGameplayOnWin)
                    yield return new WaitForSecondsRealtime(delay);
                else
                    yield return new WaitForSeconds(delay);
            }

            if (pauseGameplayOnWin)
                Time.timeScale = 1f;
            
            if (usePerLevelWinCutsceneScenes && TryGetWinCutsceneBuildIndexForCurrentLevel(out int cutsceneBuildIndex))
            {
                if (debugLogs)
                    Debug.Log($"[GameSceneManager] Loading WIN CUTSCENE scene buildIndex={cutsceneBuildIndex} for current level.", this);

                SceneManager.LoadScene(cutsceneBuildIndex);
                yield break;
            }
            
            if (usePerLevelWinCutsceneScenes && ifNoWinCutsceneLoadNextLevel)
            {
                if (debugLogs)
                    Debug.Log("[GameSceneManager] No win cutscene configured for this level -> loading NEXT level.", this);

                LoadNextLevel();
                yield break;
            }

            LoadWinScene();
        }
        
        private bool TryGetWinCutsceneBuildIndexForCurrentLevel(out int buildIndex)
        {
            buildIndex = -1;

            if (winCutsceneSceneBuildIndices == null || winCutsceneSceneBuildIndices.Length == 0)
                return false;

            int levelIdx = GetCurrentLevelIndex();
            if (levelIdx < 0 || levelIdx >= winCutsceneSceneBuildIndices.Length)
                return false;

            int candidate = winCutsceneSceneBuildIndices[levelIdx];
            if (!IsValidBuildIndex(candidate))
                return false;

            buildIndex = candidate;
            return true;
        }



        private static float ComputeLongestFxDurationSeconds(ParticleSystem[] systems)
        {
            float max = 0f;

            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem ps = systems[i];
                if (ps == null) continue;

                var main = ps.main;

                float lifetimeMax = main.startLifetime.constantMax;
                float startDelayMax = main.startDelay.constantMax;

                float total = Mathf.Max(0f, startDelayMax + main.duration + lifetimeMax);
                if (total > max)
                    max = total;
            }

            return max;
        }

        // ===================== Level API (UI Buttons) =====================

        /// <summary>
        /// Restart button (Win/GameOver): restarts the CURRENT level (tutorial/level1/level2...).
        /// </summary>
        public void RestartGame()
        {
            int levelBuildIndex = GetCurrentLevelBuildIndexSafe();
            if (levelBuildIndex >= 0)
            {
                if (debugLogs)
                    Debug.Log($"[GameSceneManager] RestartGame() -> Loading current level buildIndex={levelBuildIndex}", this);

                SceneManager.LoadScene(levelBuildIndex);
                return;
            }

            Debug.LogError("[GameSceneManager] RestartGame failed: no valid current level. Check levelSceneBuildIndices.", this);
        }

        /// <summary>
        /// NEXT LEVEL button (Win scene): loads the next level in levelSceneBuildIndices.
        /// </summary>
        public void LoadNextLevel()
        {
            int current = GetCurrentLevelIndex();
            if (current < 0) current = 0;

            int next = current + 1;

            if (levelSceneBuildIndices == null || levelSceneBuildIndices.Length == 0)
            {
                Debug.LogError("[GameSceneManager] LoadNextLevel failed: levelSceneBuildIndices is empty.", this);
                return;
            }

            if (next >= levelSceneBuildIndices.Length)
            {
                if (!loopToFirstLevelIfNoNext)
                {
                    if (debugLogs)
                        Debug.Log("[GameSceneManager] No next level. Staying on Win scene.", this);
                    return;
                }

                next = 0;
            }

            int nextBuildIndex = levelSceneBuildIndices[next];
            if (!IsValidBuildIndex(nextBuildIndex))
            {
                Debug.LogError($"[GameSceneManager] LoadNextLevel failed: next buildIndex={nextBuildIndex} is not valid. Check Build Settings.", this);
                return;
            }

            SetCurrentLevelIndex(next);

            if (debugLogs)
                Debug.Log($"[GameSceneManager] LoadNextLevel -> listIndex={next}, buildIndex={nextBuildIndex}", this);

            SceneManager.LoadScene(nextBuildIndex);
        }

        // ===================== Scene Loads =====================

        public void LoadGameOverScene()
        {
            if (IsValidBuildIndex(gameOverSceneBuildIndex))
            {
                if (debugLogs)
                    Debug.Log($"[GameSceneManager] Loading GameOver scene by index: {gameOverSceneBuildIndex}", this);
                SceneManager.LoadScene(gameOverSceneBuildIndex);
                return;
            }

            if (!string.IsNullOrEmpty(gameOverSceneName))
            {
                if (debugLogs)
                    Debug.Log($"[GameSceneManager] Loading GameOver scene by name: {gameOverSceneName}", this);
                SceneManager.LoadScene(gameOverSceneName);
                return;
            }

            Debug.LogError("[GameSceneManager] GameOver scene build index and name are not set correctly!", this);
        }

        public void LoadWinScene()
        {
            if (IsValidBuildIndex(winSceneBuildIndex))
            {
                if (debugLogs)
                    Debug.Log($"[GameSceneManager] Loading Win scene by index: {winSceneBuildIndex}", this);
                SceneManager.LoadScene(winSceneBuildIndex);
                return;
            }

            if (!string.IsNullOrEmpty(winSceneName))
            {
                if (debugLogs)
                    Debug.Log($"[GameSceneManager] Loading Win scene by name: {winSceneName}", this);
                SceneManager.LoadScene(winSceneName);
                return;
            }

            Debug.LogError("[GameSceneManager] Win scene build index and name are not set correctly!", this);
        }

        // ===================== Internal helpers =====================

        private int GetCurrentLevelBuildIndexSafe()
        {
            if (levelSceneBuildIndices == null || levelSceneBuildIndices.Length == 0)
                return -1;

            int idx = Mathf.Clamp(GetCurrentLevelIndex(), 0, levelSceneBuildIndices.Length - 1);
            int buildIndex = levelSceneBuildIndices[idx];

            if (!IsValidBuildIndex(buildIndex))
                return -1;

            return buildIndex;
        }

        private int FindLevelListIndexByBuildIndex(int buildIndex)
        {
            if (levelSceneBuildIndices == null)
                return -1;

            for (int i = 0; i < levelSceneBuildIndices.Length; i++)
            {
                if (levelSceneBuildIndices[i] == buildIndex)
                    return i;
            }

            return -1;
        }

        private static bool IsValidBuildIndex(int buildIndex)
        {
            return buildIndex >= 0 && buildIndex < SceneManager.sceneCountInBuildSettings;
        }

        private static int GetCurrentLevelIndex()
        {
            return PlayerPrefs.GetInt(PlayerPrefsLevelKey, 0);
        }

        private static void SetCurrentLevelIndex(int idx)
        {
            PlayerPrefs.SetInt(PlayerPrefsLevelKey, Mathf.Max(0, idx));
            PlayerPrefs.Save();
        }
        
        private bool IsInGameOverScene()
        {
            int active = SceneManager.GetActiveScene().buildIndex;
            return active == gameOverSceneBuildIndex;
        }

    }
}
