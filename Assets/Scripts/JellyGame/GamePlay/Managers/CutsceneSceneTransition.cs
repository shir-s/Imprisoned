// FILEPATH: Assets/Scripts/Managers/CutsceneSceneTransition.cs
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Playables;
using System.Collections.Generic;

namespace JellyGame.GamePlay.Managers
{
    /// <summary>
    /// Handles cutscene scene transitions with support for:
    /// - Manual skip (keyboard/controller)
    /// - Auto-skip after cutscene ends
    /// - Loading screen integration
    /// - Preloaded scene detection
    /// 
    /// UPDATED: Now supports loading screen workflow and auto-detects cutscene end via Timeline.
    /// </summary>
    [DisallowMultipleComponent]
    public class CutsceneSceneTransition : MonoBehaviour
    {
        public enum SceneIdMode { BuildIndex, SceneName }
        public enum TransitionMode { Direct, UseLoadingScreen }

        [Header("Transition Mode")]
        [Tooltip("Direct: Load next scene immediately. UseLoadingScreen: Go to loading screen first.")]
        [SerializeField] private TransitionMode transitionMode = TransitionMode.Direct;

        [Header("Next Scene (Direct Mode)")]
        [SerializeField] private SceneIdMode sceneIdMode = SceneIdMode.BuildIndex;
        [SerializeField] private int nextSceneBuildIndex = -1;
        [SerializeField] private string nextSceneName = "";

        [Header("Loading Screen Mode")]
        [Tooltip("Build index of the LoadingScreen scene.")]
        [SerializeField] private int loadingScreenBuildIndex = -1;

        [Tooltip("Scenes to load via LoadingScreen (1 or 2 scenes). First scene will be activated first.")]
        [SerializeField] private int[] scenesToLoad = new int[0];

        [Header("Skip Input")]
        [Tooltip("Enable manual skip? If false, only auto-skip will work.")]
        [SerializeField] private bool allowManualSkip = true;

        [Tooltip("Keyboard key to skip.")]
        [SerializeField] private KeyCode skipKey = KeyCode.Space;

        [Tooltip("Controller buttons to skip.")]
        [SerializeField] private List<KeyCode> controllerSkipButtons = new List<KeyCode>
        {
            KeyCode.JoystickButton2, // X on Windows
            KeyCode.JoystickButton3  // X on Mac
        };

        [Header("Auto-Skip")]
        [Tooltip("Enable auto-skip after cutscene ends?")]
        [SerializeField] private bool enableAutoSkip = true;

        [Tooltip("Delay (seconds) after cutscene ends before auto-transitioning.")]
        [SerializeField] private float autoSkipDelay = 2f;

        [Tooltip("Auto-detect cutscene end via PlayableDirector (Timeline)? If false, uses fixed duration.")]
        [SerializeField] private bool detectCutsceneEnd = true;

        [Tooltip("If detectCutsceneEnd is false, use this fixed duration (seconds).")]
        [SerializeField] private float fixedCutsceneDuration = 10f;

        [Header("Preload")]
        [Tooltip("If true, preload starts immediately on Start (for Direct mode).")]
        [SerializeField] private bool preloadOnStart = true;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = true;

        private AsyncOperation _preloadOp;
        private bool _skipRequested;
        private bool _autoSkipTriggered;
        private PlayableDirector _timeline;

        private void Start()
        {
            // Check if next scene is already loaded (by LoadingScreen)
            if (IsNextSceneAlreadyLoaded())
            {
                if (debugLogs)
                    Debug.Log("[CutsceneSceneTransition] Next scene already loaded. Will activate when ready.", this);

                // Don't preload - scene is already there!
                preloadOnStart = false;
            }

            if (preloadOnStart && transitionMode == TransitionMode.Direct)
                StartPreload();

            // Setup auto-skip
            if (enableAutoSkip)
                SetupAutoSkip();
        }

        private void Update()
        {
            if (_skipRequested || _autoSkipTriggered)
                return;

            if (!allowManualSkip)
                return;

            // Check for manual skip input
            bool inputDetected = Input.GetKeyDown(skipKey);

            if (!inputDetected && controllerSkipButtons != null)
            {
                foreach (var button in controllerSkipButtons)
                {
                    if (Input.GetKeyDown(button))
                    {
                        inputDetected = true;
                        break;
                    }
                }
            }

            if (inputDetected)
            {
                if (debugLogs)
                    Debug.Log("[CutsceneSceneTransition] Manual skip input detected.", this);

                _skipRequested = true;
                PerformTransition();
            }
        }

        private void SetupAutoSkip()
        {
            if (detectCutsceneEnd)
            {
                // Try to find PlayableDirector (Timeline)
                _timeline = FindObjectOfType<PlayableDirector>();

                if (_timeline != null)
                {
                    _timeline.stopped += OnTimelineStopped;

                    if (debugLogs)
                        Debug.Log($"[CutsceneSceneTransition] Timeline detected. Duration: {_timeline.duration:F1}s", this);
                }
                else
                {
                    Debug.LogWarning("[CutsceneSceneTransition] detectCutsceneEnd enabled but no PlayableDirector found! Falling back to fixed duration.", this);
                    StartCoroutine(AutoSkipAfterFixedDuration());
                }
            }
            else
            {
                // Use fixed duration
                StartCoroutine(AutoSkipAfterFixedDuration());
            }
        }

        private void OnTimelineStopped(PlayableDirector director)
        {
            if (_skipRequested || _autoSkipTriggered)
                return;

            if (debugLogs)
                Debug.Log($"[CutsceneSceneTransition] Timeline ended. Starting auto-skip delay: {autoSkipDelay}s", this);

            StartCoroutine(AutoSkipAfterDelay());
        }

        private IEnumerator AutoSkipAfterDelay()
        {
            if (autoSkipDelay > 0f)
                yield return new WaitForSeconds(autoSkipDelay);

            if (!_skipRequested && !_autoSkipTriggered)
            {
                _autoSkipTriggered = true;

                if (debugLogs)
                    Debug.Log("[CutsceneSceneTransition] Auto-skip triggered.", this);

                PerformTransition();
            }
        }

        private IEnumerator AutoSkipAfterFixedDuration()
        {
            float totalTime = fixedCutsceneDuration + autoSkipDelay;

            if (debugLogs)
                Debug.Log($"[CutsceneSceneTransition] Using fixed duration: {fixedCutsceneDuration}s + {autoSkipDelay}s delay", this);

            yield return new WaitForSeconds(totalTime);

            if (!_skipRequested && !_autoSkipTriggered)
            {
                _autoSkipTriggered = true;

                if (debugLogs)
                    Debug.Log("[CutsceneSceneTransition] Auto-skip triggered (fixed duration).", this);

                PerformTransition();
            }
        }

        private void PerformTransition()
        {
            if (transitionMode == TransitionMode.UseLoadingScreen)
            {
                LoadViaLoadingScreen();
            }
            else
            {
                LoadDirectly();
            }
        }

        private void LoadViaLoadingScreen()
        {
            if (scenesToLoad == null || scenesToLoad.Length == 0)
            {
                Debug.LogError("[CutsceneSceneTransition] LoadingScreen mode enabled but scenesToLoad is empty!", this);
                return;
            }

            if (!IsValidBuildIndex(loadingScreenBuildIndex))
            {
                Debug.LogError($"[CutsceneSceneTransition] Invalid loadingScreenBuildIndex: {loadingScreenBuildIndex}", this);
                return;
            }

            if (debugLogs)
                Debug.Log($"[CutsceneSceneTransition] Loading via LoadingScreen: {scenesToLoad.Length} scene(s)", this);

            SceneTransitionHelper.LoadWithLoadingScreen(loadingScreenBuildIndex, scenesToLoad);
        }

        private void LoadDirectly()
        {
            // Check if next scene is already loaded
            if (IsNextSceneAlreadyLoaded())
            {
                if (debugLogs)
                    Debug.Log("[CutsceneSceneTransition] Next scene already loaded. Activating it.", this);

                ActivatePreloadedScene();
                return;
            }

            // Normal loading
            if (_preloadOp == null)
                StartPreload();
            else
                TryActivateIfReady();
        }

        private void StartPreload()
        {
            if (_preloadOp != null)
                return;

            if (!TryGetSceneToLoad(out int buildIndex, out string sceneName))
            {
                Debug.LogError("[CutsceneSceneTransition] Invalid next scene configuration.", this);
                return;
            }

            if (debugLogs)
                Debug.Log("[CutsceneSceneTransition] Starting scene preload.", this);

            _preloadOp = (sceneIdMode == SceneIdMode.BuildIndex)
                ? SceneManager.LoadSceneAsync(buildIndex)
                : SceneManager.LoadSceneAsync(sceneName);

            _preloadOp.allowSceneActivation = false;

            StartCoroutine(PreloadWatcher());
        }

        private IEnumerator PreloadWatcher()
        {
            while (_preloadOp != null && !_preloadOp.isDone)
            {
                if (_skipRequested || _autoSkipTriggered)
                    TryActivateIfReady();

                yield return null;
            }
        }

        private void TryActivateIfReady()
        {
            if (_preloadOp == null)
                return;

            if (_preloadOp.progress < 0.89f)
                return;

            if (debugLogs)
                Debug.Log("[CutsceneSceneTransition] Activating preloaded scene.", this);

            _preloadOp.allowSceneActivation = true;
        }

        private bool IsNextSceneAlreadyLoaded()
        {
            if (transitionMode == TransitionMode.UseLoadingScreen)
                return false; // LoadingScreen handles this

            if (!TryGetSceneToLoad(out int buildIndex, out string sceneName))
                return false;

            // Check if scene is loaded
            Scene scene = (sceneIdMode == SceneIdMode.BuildIndex)
                ? SceneManager.GetSceneByBuildIndex(buildIndex)
                : SceneManager.GetSceneByName(sceneName);

            if (!scene.isLoaded)
                return false;

            // Check if scene was loaded but deactivated by LoadingManager
            if (sceneIdMode == SceneIdMode.BuildIndex && SceneTransitionHelper.IsSceneInactive(buildIndex))
            {
                if (debugLogs)
                    Debug.Log($"[CutsceneSceneTransition] Next scene {scene.name} is loaded but inactive (objects disabled).", this);
                return true;
            }

            return scene.isLoaded;
        }

        private void ActivatePreloadedScene()
        {
            if (!TryGetSceneToLoad(out int buildIndex, out string sceneName))
                return;

            Scene scene = (sceneIdMode == SceneIdMode.BuildIndex)
                ? SceneManager.GetSceneByBuildIndex(buildIndex)
                : SceneManager.GetSceneByName(sceneName);

            if (scene.isLoaded)
            {
                // Reactivate all root GameObjects in the scene
                GameObject[] rootObjects = scene.GetRootGameObjects();
                foreach (GameObject obj in rootObjects)
                {
                    obj.SetActive(true);
                }

                // Set as active scene
                SceneManager.SetActiveScene(scene);

                if (debugLogs)
                    Debug.Log($"[CutsceneSceneTransition] Activated preloaded scene: {scene.name} (enabled {rootObjects.Length} root objects)", this);

                // Optional: Unload the current cutscene scene to free memory
                Scene currentScene = SceneManager.GetActiveScene();
                if (currentScene != scene)
                {
                    StartCoroutine(UnloadSceneAfterDelay(currentScene, 0.5f));
                }
            }
        }

        private IEnumerator UnloadSceneAfterDelay(Scene scene, float delay)
        {
            yield return new WaitForSeconds(delay);

            if (scene.isLoaded)
            {
                if (debugLogs)
                    Debug.Log($"[CutsceneSceneTransition] Unloading cutscene scene: {scene.name}", this);

                SceneManager.UnloadSceneAsync(scene);
            }
        }

        private bool TryGetSceneToLoad(out int buildIndex, out string sceneName)
        {
            buildIndex = nextSceneBuildIndex;
            sceneName = nextSceneName;

            if (sceneIdMode == SceneIdMode.BuildIndex)
            {
                return buildIndex >= 0 &&
                       buildIndex < SceneManager.sceneCountInBuildSettings;
            }

            return !string.IsNullOrWhiteSpace(sceneName);
        }

        private static bool IsValidBuildIndex(int buildIndex)
        {
            return buildIndex >= 0 && buildIndex < SceneManager.sceneCountInBuildSettings;
        }

        private void OnDestroy()
        {
            // Cleanup timeline listener
            if (_timeline != null)
                _timeline.stopped -= OnTimelineStopped;
        }
    }
}