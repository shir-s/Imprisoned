// FILEPATH: Assets/Scripts/Managers/CutsceneSceneTransition.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace JellyGame.GamePlay.Managers
{
    /// <summary>
    /// Cutscene transition - when cutscene ends, triggers LoadingManager to load next scenes.
    /// 
    /// Handles being loaded-but-deactivated, then reactivated later.
    /// Uses OnEnable to restart auto-skip when scene is reactivated.
    /// </summary>
    [DisallowMultipleComponent]
    public class CutsceneSceneTransition : MonoBehaviour
    {
        [Header("Next Scenes (via LoadingManager)")]
        [Tooltip("Scenes to load via LoadingManager when cutscene ends.\n" +
                 "Examples:\n" +
                 "- Cutscene1: [7, 8] (Level2 + Cutscene2)\n" +
                 "- Cutscene2: [9] (Level3)")]
        [SerializeField] private int[] scenesToLoadOnComplete = new int[0];

        [Tooltip("Which scene to activate first (index into scenesToLoadOnComplete array).\n" +
                 "Usually 0 (the level), not the cutscene.")]
        [SerializeField] private int firstSceneToActivate = 0;

        [Header("Legacy: Activate Already-Loaded Scene")]
        [Tooltip("(LEGACY) Build index of scene to activate if it's already loaded.\n" +
                 "Only used if scenesToLoadOnComplete is EMPTY.\n" +
                 "Set to -1 to disable.")]
        [SerializeField] private int nextSceneBuildIndex = -1;

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

        [Header("Debug")]
        [SerializeField] private bool debugLogs = true;

        private bool _skipRequested;
        private bool _autoSkipTriggered;
        private PlayableDirector _timeline;
        private bool _loadingWasVisibleLastFrame = false;
        private Coroutine _setupCoroutine;
        private Coroutine _autoSkipCoroutine;

        /// <summary>
        /// Called every time this GameObject is enabled (including reactivation).
        /// This is the key to handling scene reactivation properly.
        /// </summary>
        private void OnEnable()
        {
            if (debugLogs)
                Debug.Log($"[CutsceneSceneTransition] OnEnable called", this);

            // Reset state for fresh start
            _skipRequested = false;
            _autoSkipTriggered = false;
            _loadingWasVisibleLastFrame = false;

            // Stop any existing coroutines (safety cleanup)
            if (_setupCoroutine != null)
            {
                StopCoroutine(_setupCoroutine);
                _setupCoroutine = null;
            }
            if (_autoSkipCoroutine != null)
            {
                StopCoroutine(_autoSkipCoroutine);
                _autoSkipCoroutine = null;
            }

            // Unsubscribe from timeline (will resubscribe if needed)
            if (_timeline != null)
            {
                _timeline.stopped -= OnTimelineStopped;
                _timeline = null;
            }

            // Start waiting for this scene to become active, then setup auto-skip
            _setupCoroutine = StartCoroutine(SetupAutoSkipWhenReady());
        }

        private void OnDisable()
        {
            if (debugLogs)
                Debug.Log($"[CutsceneSceneTransition] OnDisable called", this);

            // Stop all coroutines
            if (_setupCoroutine != null)
            {
                StopCoroutine(_setupCoroutine);
                _setupCoroutine = null;
            }
            if (_autoSkipCoroutine != null)
            {
                StopCoroutine(_autoSkipCoroutine);
                _autoSkipCoroutine = null;
            }

            // Unsubscribe from timeline
            if (_timeline != null)
            {
                _timeline.stopped -= OnTimelineStopped;
            }
        }

        /// <summary>
        /// Wait until this scene is the active scene before starting auto-skip.
        /// This prevents the cutscene from auto-skipping while it's loaded but deactivated.
        /// </summary>
        private IEnumerator SetupAutoSkipWhenReady()
        {
            Scene myScene = gameObject.scene;

            if (debugLogs)
                Debug.Log($"[CutsceneSceneTransition] SetupAutoSkipWhenReady started. My scene: {myScene.name}", this);

            // Wait until this scene is the active scene
            while (SceneManager.GetActiveScene() != myScene)
            {
                if (debugLogs && Time.frameCount % 60 == 0)
                    Debug.Log($"[CutsceneSceneTransition] Waiting to become active. Current: {SceneManager.GetActiveScene().name}, Mine: {myScene.name}", this);

                yield return null;
            }

            // Wait one more frame for everything to settle
            yield return null;

            if (debugLogs)
                Debug.Log($"[CutsceneSceneTransition] Scene is now active! Setting up auto-skip.", this);

            // Now setup auto-skip
            if (enableAutoSkip)
            {
                SetupAutoSkip();
            }

            _setupCoroutine = null;
        }

        private void Update()
        {
            if (_skipRequested || _autoSkipTriggered)
                return;

            if (!allowManualSkip)
                return;

            // Check if LoadingManager is currently visible
            bool loadingVisible = IsLoadingManagerVisible();

            // Ignore input while LoadingManager is visible
            if (loadingVisible)
            {
                _loadingWasVisibleLastFrame = true;

                if (debugLogs && Time.frameCount % 60 == 0)
                    Debug.Log("[CutsceneSceneTransition] Ignoring input - LoadingManager is visible", this);

                return;
            }

            // Ignore input for one frame after LoadingManager becomes hidden
            if (_loadingWasVisibleLastFrame)
            {
                _loadingWasVisibleLastFrame = false;

                if (debugLogs)
                    Debug.Log("[CutsceneSceneTransition] Ignoring input for 1 frame after LoadingManager hidden (prevent carryover)", this);

                return;
            }

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
                TransitionToNextScenes();
            }
        }

        private bool IsLoadingManagerVisible()
        {
            LoadingManager loadingManager = LoadingManager.Instance;

            if (loadingManager == null)
                return false;

            Canvas canvas = loadingManager.GetComponentInChildren<Canvas>();
            if (canvas != null)
            {
                return canvas.enabled && canvas.gameObject.activeInHierarchy;
            }

            return false;
        }

        private void SetupAutoSkip()
        {
            if (debugLogs)
                Debug.Log($"[CutsceneSceneTransition] SetupAutoSkip called. detectCutsceneEnd={detectCutsceneEnd}", this);

            if (detectCutsceneEnd)
            {
                // Try to find PlayableDirector (Timeline)
                _timeline = FindObjectOfType<PlayableDirector>();

                if (_timeline != null)
                {
                    if (_timeline.duration > 0.1)
                    {
                        _timeline.stopped += OnTimelineStopped;

                        if (debugLogs)
                            Debug.Log($"[CutsceneSceneTransition] Timeline detected. Duration: {_timeline.duration:F1}s", this);
                    }
                    else
                    {
                        Debug.LogWarning($"[CutsceneSceneTransition] Timeline found but duration is {_timeline.duration:F1}s (invalid)! " +
                            "Falling back to fixed duration.", this);
                        _autoSkipCoroutine = StartCoroutine(AutoSkipAfterFixedDuration());
                    }
                }
                else
                {
                    Debug.LogWarning("[CutsceneSceneTransition] detectCutsceneEnd enabled but no PlayableDirector found! " +
                        "Falling back to fixed duration.", this);
                    _autoSkipCoroutine = StartCoroutine(AutoSkipAfterFixedDuration());
                }
            }
            else
            {
                // Use fixed duration
                _autoSkipCoroutine = StartCoroutine(AutoSkipAfterFixedDuration());
            }
        }

        private void OnTimelineStopped(PlayableDirector director)
        {
            if (_skipRequested || _autoSkipTriggered)
                return;

            if (debugLogs)
                Debug.Log($"[CutsceneSceneTransition] Timeline ended. Starting auto-skip delay: {autoSkipDelay}s", this);

            _autoSkipCoroutine = StartCoroutine(AutoSkipAfterDelay());
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

                TransitionToNextScenes();
            }

            _autoSkipCoroutine = null;
        }

        private IEnumerator AutoSkipAfterFixedDuration()
        {
            float totalTime = fixedCutsceneDuration + autoSkipDelay;

            if (debugLogs)
                Debug.Log($"[CutsceneSceneTransition] Using fixed duration: {fixedCutsceneDuration}s + {autoSkipDelay}s delay = {totalTime}s total", this);

            yield return new WaitForSeconds(totalTime);

            if (!_skipRequested && !_autoSkipTriggered)
            {
                _autoSkipTriggered = true;

                if (debugLogs)
                    Debug.Log("[CutsceneSceneTransition] Auto-skip triggered (fixed duration).", this);

                TransitionToNextScenes();
            }

            _autoSkipCoroutine = null;
        }

        private void TransitionToNextScenes()
        {
            if (debugLogs)
                Debug.Log($"[CutsceneSceneTransition] TransitionToNextScenes called", this);

            // PRIMARY: Use LoadingManager to load new scenes
            if (scenesToLoadOnComplete != null && scenesToLoadOnComplete.Length > 0)
            {
                if (debugLogs)
                    Debug.Log($"[CutsceneSceneTransition] Loading {scenesToLoadOnComplete.Length} scene(s) via LoadingManager", this);

                if (LoadingManager.Instance != null)
                {
                    LoadingManager.Instance.StartLoading(scenesToLoadOnComplete, firstSceneToActivate);
                }
                else
                {
                    Debug.LogError("[CutsceneSceneTransition] LoadingManager.Instance is null! Cannot load scenes.", this);

                    // Fallback: direct load first scene
                    if (IsValidBuildIndex(scenesToLoadOnComplete[0]))
                    {
                        SceneManager.LoadScene(scenesToLoadOnComplete[0]);
                    }
                }
                return;
            }

            // LEGACY: Activate already-loaded scene (old behavior)
            if (nextSceneBuildIndex >= 0)
            {
                ActivateAlreadyLoadedScene();
                return;
            }

            Debug.LogError("[CutsceneSceneTransition] No scenes configured! Set either scenesToLoadOnComplete or nextSceneBuildIndex.", this);
        }

        private void ActivateAlreadyLoadedScene()
        {
            if (!IsValidBuildIndex(nextSceneBuildIndex))
            {
                Debug.LogError($"[CutsceneSceneTransition] Invalid nextSceneBuildIndex: {nextSceneBuildIndex}", this);
                return;
            }

            Scene nextScene = SceneManager.GetSceneByBuildIndex(nextSceneBuildIndex);

            if (!nextScene.isLoaded)
            {
                Debug.LogError($"[CutsceneSceneTransition] Next scene (index {nextSceneBuildIndex}) is not loaded! Cannot activate.", this);
                return;
            }

            if (debugLogs)
                Debug.Log($"[CutsceneSceneTransition] Activating already-loaded scene: {nextScene.name}", this);

            GameObject[] rootObjects = nextScene.GetRootGameObjects();

            // Re-enable cameras and audio listeners FIRST
            foreach (GameObject obj in rootObjects)
            {
                UnityEngine.Camera[] cameras = obj.GetComponentsInChildren<UnityEngine.Camera>(true);
                foreach (UnityEngine.Camera cam in cameras)
                {
                    cam.enabled = true;

                    if (debugLogs)
                        Debug.Log($"[CutsceneSceneTransition] Re-enabled camera: {cam.name}", this);
                }

                AudioListener[] listeners = obj.GetComponentsInChildren<AudioListener>(true);
                foreach (AudioListener listener in listeners)
                {
                    listener.enabled = true;

                    if (debugLogs)
                        Debug.Log($"[CutsceneSceneTransition] Re-enabled audio listener: {listener.name}", this);
                }
            }

            // Now activate GameObjects
            foreach (GameObject obj in rootObjects)
            {
                obj.SetActive(true);
            }

            // Set as active scene
            SceneManager.SetActiveScene(nextScene);

            if (debugLogs)
                Debug.Log($"[CutsceneSceneTransition] Activated scene {nextScene.name}", this);

            // Unload this cutscene scene
            Scene currentScene = gameObject.scene;
            if (currentScene.isLoaded && currentScene != nextScene)
            {
                StartCoroutine(UnloadCutsceneAfterDelay(currentScene, 0.5f));
            }
        }

        private IEnumerator UnloadCutsceneAfterDelay(Scene scene, float delay)
        {
            yield return new WaitForSeconds(delay);

            if (scene.isLoaded)
            {
                if (debugLogs)
                    Debug.Log($"[CutsceneSceneTransition] Unloading cutscene scene: {scene.name}", this);

                SceneManager.UnloadSceneAsync(scene);
            }
        }

        private static bool IsValidBuildIndex(int buildIndex)
        {
            return buildIndex >= 0 && buildIndex < SceneManager.sceneCountInBuildSettings;
        }

        private void OnDestroy()
        {
            // Final cleanup
            if (_timeline != null)
                _timeline.stopped -= OnTimelineStopped;
        }
    }
}