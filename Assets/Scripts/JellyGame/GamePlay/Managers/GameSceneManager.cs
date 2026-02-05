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
    /// - Handle post-level cutscenes that are already loaded (activate directly)
    /// - Play win FX before loading
    /// - Trigger GameOver when player dies
    /// 
    /// No buttons, no manual level selection - everything is automatic via triggers.
    /// </summary>
    public class GameSceneManager : MonoBehaviour
    {
        [Header("Main Menu")]
        [Tooltip("Enable 'Press Any Key' to start from Main Menu.")]
        [SerializeField] private bool isMainMenu = false;

        [Tooltip("Which scene to load when user presses any key in Main Menu (usually Tutorial).")]
        [SerializeField] private int mainMenuNextScene = 4; // Tutorial

        [Tooltip("Text to display (optional). Leave empty to skip.")]
        [SerializeField] private string pressAnyKeyText = "Press Any Key to Start";

        [Tooltip("Play background music in Main Menu? Disable if you don't have SoundManager set up.")]
        [SerializeField] private bool playBackgroundMusic = true;

        [Header("Game Over")]
        [Tooltip("Build index of the GameOver scene.")]
        [SerializeField] private int gameOverSceneBuildIndex = 0;

        [Tooltip("Use LoadingManager for GameOver transitions? If false, loads directly.")]
        [SerializeField] private bool useLoadingScreenForGameOver = true;

        [Tooltip("If an EntityDied event is triggered with a victim layer in this mask => trigger GameOver.")]
        [SerializeField] private LayerMask gameOverOnVictimLayers;

        [Header("Loading Screen")]
        [Tooltip("Use loading screen for win transitions? If false, goes directly to scenes.")]
        [SerializeField] private bool useLoadingScreenForWin = true;

        [Tooltip("Scenes to load when player wins THIS level. Configure per level!\n" +
                 "Examples:\n" +
                 "- Tutorial: [5, 6] (Level1 + Cutscene1)\n" +
                 "- Level3: [3] (MainMenu)\n" +
                 "NOTE: If postLevelCutsceneBuildIndex is set, this is ignored.")]
        [SerializeField] private int[] winScenesToLoad = new int[0];

        [Header("Post-Level Cutscene (Already Loaded)")]
        [Tooltip("If >= 0, this cutscene was loaded together with this level by LoadingManager.\n" +
                 "On win, it will be activated directly (no loading screen).\n" +
                 "Set to -1 if no cutscene follows this level.")]
        [SerializeField] private int postLevelCutsceneBuildIndex = -1;

        [Header("Special Case: GameOver Portal")]
        [Tooltip("When player enters portal in GameOver scene, which scenes to load? (Level1 + Cutscene1)")]
        [SerializeField] private int[] gameOverPortalScenesToLoad = new int[] { 5, 6 };

        [Tooltip("Which scene to activate first from gameOverPortalScenesToLoad (0 = first scene).")]
        [SerializeField] private int gameOverPortalFirstSceneIndex = 0;

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
            // Background music (optional - can be disabled)
            if (playBackgroundMusic && SoundManager.Instance != null)
            {
                try
                {
                    SoundManager.Instance.StopAllSounds();
                    
                    // Only play if "Background" audio exists
                    if (SoundManager.Instance.FindAudioConfig("Background") != null)
                    {
                        SoundManager.Instance.PlaySound("Background", this.transform);
                    }
                    else if (debugLogs)
                    {
                        Debug.Log("[GameSceneManager] No 'Background' audio config found - skipping background music.", this);
                    }
                }
                catch (System.Exception ex)
                {
                    // Silently catch any audio errors (common in Main Menu without SoundManager)
                    if (debugLogs)
                        Debug.LogWarning($"[GameSceneManager] Background music error (safe to ignore): {ex.Message}", this);
                }
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
            // Main Menu: Press any key to start
            if (isMainMenu && Input.anyKeyDown)
            {
                if (debugLogs)
                    Debug.Log("[GameSceneManager] Main Menu: Any key pressed, loading next scene.", this);

                LoadFromMainMenu();
                return; // Don't check cheat codes after starting
            }

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

        // ===================== Main Menu =====================

        private void LoadFromMainMenu()
        {
            if (!IsValidBuildIndex(mainMenuNextScene))
            {
                Debug.LogError($"[GameSceneManager] Main Menu next scene invalid: {mainMenuNextScene}", this);
                return;
            }

            if (LoadingManager.Instance == null)
            {
                Debug.LogError("[GameSceneManager] LoadingManager.Instance is null! Make sure LoadingScreen is loaded.", this);
                return;
            }

            if (debugLogs)
                Debug.Log($"[GameSceneManager] Loading from Main Menu → scene {mainMenuNextScene}", this);

            // Disable this flag so we don't trigger again
            isMainMenu = false;

            // Load via LoadingManager (just one scene - Tutorial)
            LoadingManager.Instance.StartLoading(new int[] { mainMenuNextScene });
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
            // Always go to the configured destination (usually Level1 + Cutscene1)
            if (IsInGameOverScene())
            {
                if (gameOverPortalScenesToLoad == null || gameOverPortalScenesToLoad.Length == 0)
                {
                    Debug.LogError("[GameSceneManager] GameOver portal scenes not configured!", this);
                    yield break;
                }

                if (debugLogs)
                    Debug.Log($"[GameSceneManager] Win in GameOver scene → loading {gameOverPortalScenesToLoad.Length} scene(s)", this);

                if (LoadingManager.Instance != null)
                {
                    LoadingManager.Instance.StartLoading(gameOverPortalScenesToLoad, gameOverPortalFirstSceneIndex);
                }
                else
                {
                    // Fallback: direct load first scene
                    SceneManager.LoadScene(gameOverPortalScenesToLoad[0]);
                }
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

            // ==================== NEW: Post-Level Cutscene Logic ====================
            // Check if there's a cutscene already loaded that should play after this level
            if (postLevelCutsceneBuildIndex >= 0)
            {
                Scene cutsceneScene = SceneManager.GetSceneByBuildIndex(postLevelCutsceneBuildIndex);
                
                if (cutsceneScene.isLoaded)
                {
                    if (debugLogs)
                        Debug.Log($"[GameSceneManager] Post-level cutscene (index {postLevelCutsceneBuildIndex}) is already loaded. Activating directly.", this);

                    ActivatePostLevelCutscene(cutsceneScene);
                    yield break;
                }
                else
                {
                    Debug.LogWarning($"[GameSceneManager] postLevelCutsceneBuildIndex={postLevelCutsceneBuildIndex} but scene is NOT loaded! " +
                        "Falling back to winScenesToLoad.", this);
                }
            }

            // ==================== Standard: Load via LoadingManager ====================
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

                    LoadingManager.Instance.StartLoading(winScenesToLoad, 0);
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

        // ===================== Post-Level Cutscene Activation =====================

        /// <summary>
        /// Activate a cutscene scene that was already loaded by LoadingManager (but deactivated).
        /// This is used when Level wins and the cutscene should play without a loading screen.
        /// </summary>
        private void ActivatePostLevelCutscene(Scene cutsceneScene)
        {
            if (!cutsceneScene.isLoaded)
            {
                Debug.LogError($"[GameSceneManager] Cannot activate cutscene - scene not loaded!", this);
                return;
            }

            if (debugLogs)
                Debug.Log($"[GameSceneManager] Activating post-level cutscene: {cutsceneScene.name}", this);

            // FIRST: Disable cameras and audio listeners in the CURRENT scene (level)
            // This prevents "multiple audio listeners" warnings
            Scene currentLevelScene = gameObject.scene;
            if (currentLevelScene.isLoaded && currentLevelScene != cutsceneScene)
            {
                if (debugLogs)
                    Debug.Log($"[GameSceneManager] Disabling cameras/audio in level scene: {currentLevelScene.name}", this);

                GameObject[] levelObjects = currentLevelScene.GetRootGameObjects();
                foreach (GameObject obj in levelObjects)
                {
                    UnityEngine.Camera[] cameras = obj.GetComponentsInChildren<UnityEngine.Camera>(true);
                    foreach (UnityEngine.Camera cam in cameras)
                    {
                        cam.enabled = false;
                    }

                    AudioListener[] listeners = obj.GetComponentsInChildren<AudioListener>(true);
                    foreach (AudioListener listener in listeners)
                    {
                        listener.enabled = false;
                    }
                }
            }

            // NOW: Activate the cutscene scene
            GameObject[] rootObjects = cutsceneScene.GetRootGameObjects();

            // Re-enable cameras and audio listeners FIRST
            foreach (GameObject obj in rootObjects)
            {
                UnityEngine.Camera[] cameras = obj.GetComponentsInChildren<UnityEngine.Camera>(true);
                foreach (UnityEngine.Camera cam in cameras)
                {
                    cam.enabled = true;
                    
                    if (debugLogs)
                        Debug.Log($"[GameSceneManager] Re-enabled camera: {cam.name}", this);
                }

                AudioListener[] listeners = obj.GetComponentsInChildren<AudioListener>(true);
                foreach (AudioListener listener in listeners)
                {
                    listener.enabled = true;
                    
                    if (debugLogs)
                        Debug.Log($"[GameSceneManager] Re-enabled audio listener: {listener.name}", this);
                }
            }

            // Now activate GameObjects
            foreach (GameObject obj in rootObjects)
            {
                obj.SetActive(true);
            }

            // Set cutscene as active scene
            SceneManager.SetActiveScene(cutsceneScene);

            if (debugLogs)
                Debug.Log($"[GameSceneManager] Activated cutscene scene {cutsceneScene.name}", this);

            // Unload the current level scene
            if (currentLevelScene.isLoaded && currentLevelScene != cutsceneScene)
            {
                StartCoroutine(UnloadSceneAfterDelay(currentLevelScene, 0.5f));
            }
        }

        private IEnumerator UnloadSceneAfterDelay(Scene scene, float delay)
        {
            yield return new WaitForSeconds(delay);

            if (scene.isLoaded)
            {
                if (debugLogs)
                    Debug.Log($"[GameSceneManager] Unloading level scene: {scene.name}", this);

                SceneManager.UnloadSceneAsync(scene);
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
            if (!IsValidBuildIndex(gameOverSceneBuildIndex))
            {
                Debug.LogError("[GameSceneManager] GameOver scene build index is invalid!", this);
                return;
            }

            if (useLoadingScreenForGameOver && LoadingManager.Instance != null)
            {
                if (debugLogs)
                    Debug.Log($"[GameSceneManager] Loading GameOver scene via LoadingManager: {gameOverSceneBuildIndex}", this);

                LoadingManager.Instance.StartLoading(new int[] { gameOverSceneBuildIndex });
            }
            else
            {
                if (debugLogs)
                    Debug.Log($"[GameSceneManager] Loading GameOver scene directly: {gameOverSceneBuildIndex}", this);

                SceneManager.LoadScene(gameOverSceneBuildIndex);
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