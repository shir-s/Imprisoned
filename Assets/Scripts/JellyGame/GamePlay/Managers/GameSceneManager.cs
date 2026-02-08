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
    /// - On Start(), tell LoadingManager to preload the next scene in the background
    /// - Listen for GameWin → transition to next scene (instant if preloaded, loading screen otherwise)
    /// - Listen for EntityDied → trigger GameOver → transition to GameOver scene
    /// - Play win FX before transitioning
    /// - Handle Main Menu "Press Any Key" flow
    /// 
    /// IMPORTANT: Configure nextSceneBuildIndex in the Inspector for each level!
    /// This is the scene that will be preloaded and transitioned to on win.
    /// </summary>
    public class GameSceneManager : MonoBehaviour
    {
        [Header("Main Menu")]
        [Tooltip("Enable 'Press Any Key' to start from Main Menu.")]
        [SerializeField] private bool isMainMenu = false;

        [Tooltip("Which scene to load when user presses any key in Main Menu.")]
        [SerializeField] private int mainMenuNextScene = 4;

        [Tooltip("Play background music in Main Menu?")]
        [SerializeField] private bool playBackgroundMusic = true;

        [Header("Scene Flow")]
        [Tooltip("Build index of the NEXT scene after winning this level.\n" +
                 "Examples:\n" +
                 "- Tutorial → Level 1 build index\n" +
                 "- Level 1 → Cutscene 1 build index\n" +
                 "- Level 3 → Win Scene build index\n" +
                 "- Win Scene → Main Menu build index\n" +
                 "Set to -1 to disable preloading.")]
        [SerializeField] private int nextSceneBuildIndex = -1;

        [Tooltip("If true, skip the loading screen when the next scene is already preloaded.\n" +
                 "If false, always show the loading screen for at least the minimum display time.")]
        [SerializeField] private bool useInstantTransition = true;

        [Header("Game Over")]
        [Tooltip("Build index of the GameOver scene.")]
        [SerializeField] private int gameOverSceneBuildIndex = 0;

        [Tooltip("If an EntityDied event victim layer matches this mask → trigger GameOver.")]
        [SerializeField] private LayerMask gameOverOnVictimLayers;

        [Header("Special Case: GameOver Scene Portal")]
        [Tooltip("When winning from the GameOver scene (portal), go to this scene.\n" +
                 "Usually Level 1. Set to -1 to use nextSceneBuildIndex instead.")]
        [SerializeField] private int gameOverPortalDestination = -1;

        [Header("Win FX (Optional)")]
        [Tooltip("ROOT GameObject containing win particle systems.")]
        [SerializeField] private GameObject winFxRoot;

        [Tooltip("Disable FX root on start.")]
        [SerializeField] private bool disableWinFxRootOnStart = true;

        [Tooltip("Force-stop all ParticleSystems on start.")]
        [SerializeField] private bool stopWinFxOnStart = true;

        [Tooltip("Delay after win before transitioning.\n" +
                 "If <= 0 and winFxRoot exists, auto-calculates from particle duration.")]
        [SerializeField] private float winFxDelay = 0f;

        [Tooltip("Pause gameplay (Time.timeScale = 0) during win sequence.")]
        [SerializeField] private bool pauseGameplayOnWin = false;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = true;

        [Header("Cheat Codes (Optional)")]
        [SerializeField] private KeyCode gameOverKey = KeyCode.None;
        [SerializeField] private KeyCode winKey = KeyCode.None;

        private bool _winSequenceRunning;

        // ===================== Lifecycle =====================

        private void Awake()
        {
            PrepareWinFx();
        }

        private void Start()
        {
            PlayBackgroundMusic();
            StartPreloading();
        }

        private void OnEnable()
        {
            EventManager.StartListening(EventManager.GameEvent.EntityDied, OnEntityDied);
            EventManager.StartListening(EventManager.GameEvent.GameOver, OnGameOver);
            EventManager.StartListening(EventManager.GameEvent.GameWin, OnGameWin);

            if (debugLogs)
                Debug.Log($"[GameSceneManager] Enabled in scene: {SceneManager.GetActiveScene().name}", this);
        }

        private void OnDisable()
        {
            EventManager.StopListening(EventManager.GameEvent.EntityDied, OnEntityDied);
            EventManager.StopListening(EventManager.GameEvent.GameOver, OnGameOver);
            EventManager.StopListening(EventManager.GameEvent.GameWin, OnGameWin);
        }

        private void Update()
        {
            // Main Menu: Press any key to start
            if (isMainMenu && Input.anyKeyDown)
            {
                if (debugLogs)
                    Debug.Log("[GameSceneManager] Main Menu: key pressed → loading next scene.", this);

                isMainMenu = false;
                TransitionTo(mainMenuNextScene);
                return;
            }

            // Cheat codes
            if (gameOverKey != KeyCode.None && Input.GetKeyDown(gameOverKey))
            {
                if (debugLogs) Debug.Log("[GameSceneManager] Cheat: GameOver", this);
                EventManager.TriggerEvent(EventManager.GameEvent.GameOver);
            }

            if (winKey != KeyCode.None && Input.GetKeyDown(winKey))
            {
                if (debugLogs) Debug.Log("[GameSceneManager] Cheat: Win", this);
                EventManager.TriggerEvent(EventManager.GameEvent.GameWin);
            }
        }

        // ===================== Preloading =====================

        private void StartPreloading()
        {
            if (LoadingManager.Instance == null)
            {
                if (debugLogs)
                    Debug.LogWarning("[GameSceneManager] LoadingManager not available yet. Preloading skipped.", this);
                return;
            }

            // Main Menu preloads its target
            if (isMainMenu && mainMenuNextScene >= 0)
            {
                LoadingManager.Instance.PreloadScene(mainMenuNextScene);

                if (debugLogs)
                    Debug.Log($"[GameSceneManager] Main Menu preloading scene {mainMenuNextScene}", this);
                return;
            }

            // Normal level preloads next scene
            if (nextSceneBuildIndex >= 0)
            {
                LoadingManager.Instance.PreloadScene(nextSceneBuildIndex);

                if (debugLogs)
                    Debug.Log($"[GameSceneManager] Preloading next scene: {nextSceneBuildIndex}", this);
            }
        }

        // ===================== Event Handlers =====================

        private void OnEntityDied(object eventData)
        {
            if (eventData is not EntityDiedEventData died)
                return;

            bool match = (gameOverOnVictimLayers.value & (1 << died.VictimLayer)) != 0;
            if (!match)
                return;

            if (debugLogs)
                Debug.Log($"[GameSceneManager] EntityDied on matching layer → GameOver.", this);

            EventManager.TriggerEvent(EventManager.GameEvent.GameOver);
        }

        private void OnGameOver(object _)
        {
            if (debugLogs)
                Debug.Log($"[GameSceneManager] GameOver → loading scene {gameOverSceneBuildIndex}", this);

            TransitionTo(gameOverSceneBuildIndex);
        }

        private void OnGameWin(object _)
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

            // Determine destination
            int destination = nextSceneBuildIndex;

            // Special case: winning from the GameOver scene (portal)
            if (IsInGameOverScene() && gameOverPortalDestination >= 0)
            {
                destination = gameOverPortalDestination;

                if (debugLogs)
                    Debug.Log($"[GameSceneManager] Win in GameOver scene → going to {destination}", this);
            }

            if (destination < 0)
            {
                Debug.LogError("[GameSceneManager] nextSceneBuildIndex not configured! Can't transition.", this);
                _winSequenceRunning = false;
                yield break;
            }

            // Play win FX
            float delay = PlayWinFx();

            // Pause if configured (FinishTrigger may have already frozen time)
            if (pauseGameplayOnWin)
                Time.timeScale = 0f;

            // Wait for FX — always use realtime because timeScale may be 0
            if (delay > 0f)
            {
                yield return new WaitForSecondsRealtime(delay);
            }

            // Always restore timeScale before transition
            Time.timeScale = 1f;

            // Transition — try instant first, fall back to loading screen
            TransitionTo(destination);
        }

        // ===================== Transition Helper =====================

        /// <summary>
        /// Transition to a scene. Tries instant (no loading screen) if the scene is preloaded
        /// and ready. Falls back to full loading screen transition otherwise.
        /// </summary>
        private void TransitionTo(int buildIndex)
        {
            if (LoadingManager.Instance == null)
            {
                Debug.LogError("[GameSceneManager] LoadingManager.Instance is null! Falling back to direct load.", this);
                SceneManager.LoadScene(buildIndex);
                return;
            }

            // Try instant transition (no loading screen) if enabled
            if (useInstantTransition && LoadingManager.Instance.TryInstantTransition(buildIndex))
            {
                if (debugLogs)
                    Debug.Log($"[GameSceneManager] ⚡ Instant transition to scene {buildIndex} (preload was ready).", this);
                return;
            }

            // Preload not ready or instant disabled — use full loading screen
            if (debugLogs)
            {
                string reason = useInstantTransition ? "preload not ready" : "instant transition disabled";
                Debug.Log($"[GameSceneManager] {reason} → using loading screen for scene {buildIndex}.", this);
            }

            LoadingManager.Instance.TransitionToScene(buildIndex);
        }

        // ===================== Win FX =====================

        private void PrepareWinFx()
        {
            if (winFxRoot == null) return;

            if (disableWinFxRootOnStart && winFxRoot.activeSelf)
                winFxRoot.SetActive(false);

            if (stopWinFxOnStart)
            {
                var systems = winFxRoot.GetComponentsInChildren<ParticleSystem>(true);
                foreach (var ps in systems)
                {
                    if (ps != null)
                        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
        }

        /// <summary>Play win FX and return the delay to wait.</summary>
        private float PlayWinFx()
        {
            float delay = winFxDelay;

            if (winFxRoot == null)
                return Mathf.Max(0f, delay);

            if (!winFxRoot.activeInHierarchy)
                winFxRoot.SetActive(true);

            ParticleSystem[] systems = winFxRoot.GetComponentsInChildren<ParticleSystem>(true);

            if (systems == null || systems.Length == 0)
                return Mathf.Max(0f, delay);

            foreach (var ps in systems)
            {
                if (ps == null) continue;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            foreach (var ps in systems)
            {
                if (ps == null) continue;
                ps.Play(true);
            }

            if (delay <= 0f)
                delay = ComputeLongestFxDuration(systems);

            return delay;
        }

        private static float ComputeLongestFxDuration(ParticleSystem[] systems)
        {
            float max = 0f;

            foreach (var ps in systems)
            {
                if (ps == null) continue;
                var main = ps.main;
                float total = main.startDelay.constantMax + main.duration + main.startLifetime.constantMax;
                if (total > max) max = total;
            }

            return max;
        }

        // ===================== Helpers =====================

        private void PlayBackgroundMusic()
        {
            if (!playBackgroundMusic || SoundManager.Instance == null)
                return;

            try
            {
                SoundManager.Instance.StopAllSounds();

                if (SoundManager.Instance.FindAudioConfig("Background") != null)
                    SoundManager.Instance.PlaySound("Background", this.transform);
                else if (debugLogs)
                    Debug.Log("[GameSceneManager] No 'Background' audio config found.", this);
            }
            catch (System.Exception ex)
            {
                if (debugLogs)
                    Debug.LogWarning($"[GameSceneManager] Background music error: {ex.Message}", this);
            }
        }

        private bool IsInGameOverScene()
        {
            return SceneManager.GetActiveScene().buildIndex == gameOverSceneBuildIndex;
        }
    }
}