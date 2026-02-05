// FILEPATH: Assets/Scripts/Managers/CutsceneSceneTransition.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace JellyGame.GamePlay.Managers
{
    /// <summary>
    /// Simplified cutscene transition - just activates the already-loaded next scene.
    /// 
    /// NEW ARCHITECTURE:
    /// - GameSceneManager loads BOTH cutscene AND next level via LoadingManager
    /// - Cutscene plays while next level waits (inactive)
    /// - This script just activates the next scene when user skips
    /// 
    /// Supports:
    /// - Manual skip (keyboard/controller)
    /// - Auto-skip after cutscene ends
    /// - Timeline (PlayableDirector) detection
    /// </summary>
    [DisallowMultipleComponent]
    public class CutsceneSceneTransition : MonoBehaviour
    {
        [Header("Next Scene")]
        [Tooltip("Build index of the scene to activate (should already be loaded by GameSceneManager).")]
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

        private void Start()
        {
            // Verify next scene is loaded
            if (!IsNextSceneLoaded())
            {
                Debug.LogWarning($"[CutsceneSceneTransition] Next scene (index {nextSceneBuildIndex}) is not loaded! " +
                    "Make sure GameSceneManager loaded it before this cutscene.", this);
            }

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

            // Check if LoadingManager is currently visible
            bool loadingVisible = IsLoadingManagerVisible();

            // CRITICAL: Ignore input WHILE LoadingManager is visible
            if (loadingVisible)
            {
                _loadingWasVisibleLastFrame = true;
                
                if (debugLogs && Time.frameCount % 60 == 0)
                    Debug.Log("[CutsceneSceneTransition] Ignoring input - LoadingManager is visible", this);
                
                return;
            }

            // CRITICAL: Also ignore input for ONE FRAME after LoadingManager becomes hidden
            // This prevents the SAME key press that hid the loading screen from also skipping the cutscene
            if (_loadingWasVisibleLastFrame)
            {
                _loadingWasVisibleLastFrame = false;
                
                if (debugLogs)
                    Debug.Log("[CutsceneSceneTransition] Ignoring input for 1 frame after LoadingManager hidden (prevent carryover)", this);
                
                return;
            }

            // Now safe to check for manual skip input
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
                ActivateNextScene();
            }
        }

        /// <summary>
        /// Check if LoadingManager canvas is currently visible.
        /// If it is, we shouldn't accept skip input (user is still on loading screen).
        /// </summary>
        private bool IsLoadingManagerVisible()
        {
            // Find LoadingManager instance
            LoadingManager loadingManager = LoadingManager.Instance;
            
            if (loadingManager == null)
                return false;

            // Check if canvas is enabled (LoadingManager disables it when done)
            // Use reflection to access private field, or make it public
            // For now, we'll check if the canvas GameObject is active
            
            // Try to find the canvas
            Canvas canvas = loadingManager.GetComponentInChildren<Canvas>();
            if (canvas != null)
            {
                return canvas.enabled && canvas.gameObject.activeInHierarchy;
            }

            return false;
        }

        private void SetupAutoSkip()
        {
            if (detectCutsceneEnd)
            {
                // Try to find PlayableDirector (Timeline)
                _timeline = FindObjectOfType<PlayableDirector>();

                if (_timeline != null)
                {
                    // Validate timeline has a valid duration
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
                        StartCoroutine(AutoSkipAfterFixedDuration());
                    }
                }
                else
                {
                    Debug.LogWarning("[CutsceneSceneTransition] detectCutsceneEnd enabled but no PlayableDirector found! " +
                        "Falling back to fixed duration.", this);
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

                ActivateNextScene();
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

                ActivateNextScene();
            }
        }

        /// <summary>
        /// Activate the next scene that was already loaded by GameSceneManager.
        /// </summary>
        private void ActivateNextScene()
        {
            if (!IsValidBuildIndex(nextSceneBuildIndex))
            {
                Debug.LogError($"[CutsceneSceneTransition] Invalid nextSceneBuildIndex: {nextSceneBuildIndex}", this);
                return;
            }

            Scene nextScene = SceneManager.GetSceneByBuildIndex(nextSceneBuildIndex);

            if (!nextScene.isLoaded)
            {
                Debug.LogError($"[CutsceneSceneTransition] Next scene (index {nextSceneBuildIndex}) is not loaded! " +
                    "Cannot activate.", this);
                return;
            }

            if (debugLogs)
                Debug.Log($"[CutsceneSceneTransition] Activating next scene: {nextScene.name}", this);

            // Reactivate all root GameObjects (LoadingManager disabled them)
            GameObject[] rootObjects = nextScene.GetRootGameObjects();
            
            // IMPORTANT: Re-enable cameras and audio listeners FIRST (before activating GameObjects)
            // LoadingManager disables these components separately
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
                Debug.Log($"[CutsceneSceneTransition] Activated scene {nextScene.name} (enabled {rootObjects.Length} root objects)", this);

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

        private bool IsNextSceneLoaded()
        {
            if (!IsValidBuildIndex(nextSceneBuildIndex))
                return false;

            Scene scene = SceneManager.GetSceneByBuildIndex(nextSceneBuildIndex);
            return scene.isLoaded;
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