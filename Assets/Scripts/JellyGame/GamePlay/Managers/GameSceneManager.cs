// FILEPATH: Assets/Scripts/Managers/GameSceneManager.cs
using System.Collections;
using JellyGame.GamePlay.Audio.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace JellyGame.GamePlay.Managers
{
    /// <summary>
    /// Simplified Game Scene Manager.
    /// 
    /// Responsibilities:
    /// - Listen for GameWin event (triggered by FinishTrigger when player enters exit)
    /// - Load scenes via LoadingScreen (configured per level in winScenesToLoad)
    /// - Play win FX before loading
    /// - Trigger GameOver when player dies
    /// 
    /// No buttons, no manual level selection - everything is automatic via triggers.
    /// </summary>
    public class GameSceneManager : MonoBehaviour
    {
        [Header("Game Over")]
        [Tooltip("Build index of the GameOver scene.")]
        [SerializeField] private int gameOverSceneBuildIndex = 0;

        [Tooltip("If an EntityDied event is triggered with a victim layer in this mask => trigger GameOver.")]
        [SerializeField] private LayerMask gameOverOnVictimLayers;

        [Header("Loading Screen")]
        [Tooltip("Use loading screen for win transitions? If false, goes directly to scenes.")]
        [SerializeField] private bool useLoadingScreenForWin = true;

        [Tooltip("Scenes to load when player wins THIS level. Configure per level!\n" +
                 "Examples:\n" +
                 "- Tutorial: [3] (just Level1)\n" +
                 "- Level1: [5, 4] (Cutscene + Level2)\n" +
                 "- Level2: [6, 1] (Cutscene + Win scene)")]
        [SerializeField] private int[] winScenesToLoad = new int[0];

        [Header("Special Case: GameOver Portal")]
        [Tooltip("When player enters portal in GameOver scene, which level to load? (Usually Level1)")]
        [SerializeField] private int gameOverPortalDestination = 3; // Level1

        [Header("Win FX (Optional)")]
        [Tooltip("ROOT GameObject containing win particles (all ParticleSystems in children).")]
        [SerializeField] private GameObject winFxRoot;

        [Tooltip("Disable FX root on start so nothing auto-plays.")]
        [SerializeField] private bool disableWinFxRootOnStart = true;

        [Tooltip("Force-stop all ParticleSystems in winFxRoot on start.")]
        [SerializeField] private bool stopWinFxOnStart = true;

        [Tooltip("Delay after win before loading next scenes.\n" +
                 "If <= 0 and winFxRoot exists, auto-calculates from longest particle duration.")]
        [SerializeField] private float winSceneLoadDelay = 0f;

        [Tooltip("Pause gameplay (Time.timeScale = 0) during win sequence.")]
        [SerializeField] private bool pauseGameplayOnWin = false;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = true;

        [Header("Cheat Codes (Optional)")]
        [SerializeField] private KeyCode gameOverKey = KeyCode.None;
        [SerializeField] private KeyCode winKey = KeyCode.None;

        private bool _winSequenceRunning;

        private void Awake()
        {
            PrepareWinFxForStart();
        }

        private void Start()
        {
            // Background music
            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.StopAllSounds();
                if (SoundManager.Instance.FindAudioConfig("Background") != null)
                    SoundManager.Instance.PlaySound("Background", this.transform);
            }
        }

        private void OnEnable()
        {
            EventManager.StartListening(EventManager.GameEvent.EntityDied, OnEntityDiedEvent);
            EventManager.StartListening(EventManager.GameEvent.GameOver, OnGameOverEvent);
            EventManager.StartListening(EventManager.GameEvent.GameWin, OnWinEvent);

            if (debugLogs)
                Debug.Log($"[GameSceneManager] Enabled in scene: {SceneManager.GetActiveScene().name}", this);
        }

        private void OnDisable()
        {
            EventManager.StopListening(EventManager.GameEvent.EntityDied, OnEntityDiedEvent);
            EventManager.StopListening(EventManager.GameEvent.GameOver, OnGameOverEvent);
            EventManager.StopListening(EventManager.GameEvent.GameWin, OnWinEvent);
        }

        private void Update()
        {
            // Cheat codes
            if (gameOverKey != KeyCode.None && Input.GetKeyDown(gameOverKey))
            {
                if (debugLogs)
                    Debug.Log($"[GameSceneManager] Cheat: Triggering GameOver", this);

                EventManager.TriggerEvent(EventManager.GameEvent.GameOver);
            }

            if (winKey != KeyCode.None && Input.GetKeyDown(winKey))
            {
                if (debugLogs)
                    Debug.Log($"[GameSceneManager] Cheat: Triggering Win", this);

                EventManager.TriggerEvent(EventManager.GameEvent.GameWin);
            }
        }

        // ===================== Event Handlers =====================

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
                Debug.Log($"[GameSceneManager] EntityDied matched layer {died.VictimLayer}. Triggering GameOver.", this);

            EventManager.TriggerEvent(EventManager.GameEvent.GameOver);
        }

        private void OnGameOverEvent(object _)
        {
            if (debugLogs)
                Debug.Log($"[GameSceneManager] GameOver event! Loading scene index: {gameOverSceneBuildIndex}", this);

            LoadGameOverScene();
        }

        private void OnWinEvent(object _)
        {
            if (_winSequenceRunning)
                return;

            _winSequenceRunning = true;
            StartCoroutine(WinSequence());
        }

        // ===================== Win Sequence =====================

        private IEnumerator WinSequence()
        {
            if (debugLogs)
                Debug.Log("[GameSceneManager] Win sequence started.", this);

            // SPECIAL CASE: Win triggered in GameOver scene (portal)
            // Always go to the configured destination (usually Level1)
            if (IsInGameOverScene())
            {
                if (!IsValidBuildIndex(gameOverPortalDestination))
                {
                    Debug.LogError($"[GameSceneManager] GameOver portal destination invalid: {gameOverPortalDestination}", this);
                    yield break;
                }

                if (debugLogs)
                    Debug.Log($"[GameSceneManager] Win in GameOver scene → loading Level buildIndex={gameOverPortalDestination}", this);

                SceneManager.LoadScene(gameOverPortalDestination);
                yield break;
            }

            // Calculate delay (from FX or configured value)
            float delay = winSceneLoadDelay;

            if (winFxRoot != null)
            {
                if (!winFxRoot.activeInHierarchy)
                    winFxRoot.SetActive(true);

                ParticleSystem[] systems = winFxRoot.GetComponentsInChildren<ParticleSystem>(true);

                if (systems != null && systems.Length > 0)
                {
                    // Stop and clear any existing particles
                    foreach (var ps in systems)
                    {
                        if (ps == null) continue;
                        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    }

                    // Play all particles
                    foreach (var ps in systems)
                    {
                        if (ps == null) continue;
                        ps.Play(true);
                    }

                    // Auto-calculate delay if not set
                    if (delay <= 0f)
                        delay = ComputeLongestFxDurationSeconds(systems);
                }
                else if (debugLogs)
                {
                    Debug.LogWarning("[GameSceneManager] winFxRoot assigned but no ParticleSystem found.", this);
                }
            }

            // Pause if configured
            if (pauseGameplayOnWin)
                Time.timeScale = 0f;

            // Wait for FX to play
            if (delay > 0f)
            {
                if (pauseGameplayOnWin)
                    yield return new WaitForSecondsRealtime(delay);
                else
                    yield return new WaitForSeconds(delay);
            }

            // Unpause
            if (pauseGameplayOnWin)
                Time.timeScale = 1f;

            // Load next scenes via LoadingManager (persistent)
            if (useLoadingScreenForWin)
            {
                if (winScenesToLoad != null && winScenesToLoad.Length > 0)
                {
                    if (LoadingManager.Instance == null)
                    {
                        Debug.LogError("[GameSceneManager] LoadingManager.Instance is null! Make sure LoadingScreen scene is loaded.", this);
                        yield break;
                    }

                    if (debugLogs)
                        Debug.Log($"[GameSceneManager] Loading {winScenesToLoad.Length} scene(s) via LoadingManager.", this);

                    LoadingManager.Instance.StartLoading(winScenesToLoad);
                    yield break;
                }
                else
                {
                    Debug.LogError("[GameSceneManager] useLoadingScreenForWin enabled but winScenesToLoad is EMPTY! Configure it in Inspector.", this);
                }
            }
            else
            {
                Debug.LogWarning("[GameSceneManager] LoadingScreen disabled. Cannot load next scenes.", this);
            }
        }

        // ===================== Helper Methods =====================

        private void PrepareWinFxForStart()
        {
            if (winFxRoot == null)
                return;

            if (disableWinFxRootOnStart && winFxRoot.activeSelf)
                winFxRoot.SetActive(false);

            if (stopWinFxOnStart)
            {
                var systems = winFxRoot.GetComponentsInChildren<ParticleSystem>(true);
                foreach (var ps in systems)
                {
                    if (ps == null) continue;
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
        }

        private static float ComputeLongestFxDurationSeconds(ParticleSystem[] systems)
        {
            float max = 0f;

            foreach (var ps in systems)
            {
                if (ps == null) continue;

                var main = ps.main;
                float lifetime = main.startLifetime.constantMax;
                float startDelay = main.startDelay.constantMax;
                float total = Mathf.Max(0f, startDelay + main.duration + lifetime);

                if (total > max)
                    max = total;
            }

            return max;
        }

        private void LoadGameOverScene()
        {
            if (IsValidBuildIndex(gameOverSceneBuildIndex))
            {
                if (debugLogs)
                    Debug.Log($"[GameSceneManager] Loading GameOver scene: {gameOverSceneBuildIndex}", this);

                SceneManager.LoadScene(gameOverSceneBuildIndex);
            }
            else
            {
                Debug.LogError("[GameSceneManager] GameOver scene build index is invalid!", this);
            }
        }

        private bool IsInGameOverScene()
        {
            int active = SceneManager.GetActiveScene().buildIndex;
            return active == gameOverSceneBuildIndex;
        }

        private static bool IsValidBuildIndex(int buildIndex)
        {
            return buildIndex >= 0 && buildIndex < SceneManager.sceneCountInBuildSettings;
        }
    }
}