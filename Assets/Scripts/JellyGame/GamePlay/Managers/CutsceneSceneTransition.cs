// FILEPATH: Assets/Scripts/Managers/CutsceneSceneTransition.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace JellyGame.GamePlay.Managers
{
    /// <summary>
    /// Cutscene transition - when cutscene ends (or is skipped), transitions to the next scene
    /// via LoadingManager.
    /// 
    /// NEW ARCHITECTURE:
    /// - On Start(), preloads the next scene in the background.
    /// - On cutscene end (auto-skip) or manual skip, calls LoadingManager.TransitionToScene().
    /// - No need to manage scene activation/deactivation — LoadingManager handles everything.
    /// - Cutscenes naturally don't start until the scene is activated (allowSceneActivation pattern),
    ///   so visual scripting, Timeline, and animations only begin when the player sees the scene.
    /// </summary>
    [DisallowMultipleComponent]
    public class CutsceneSceneTransition : MonoBehaviour
    {
        [Header("Next Scene")]
        [Tooltip("Build index of the scene to load when cutscene ends.\n" +
                 "Examples:\n" +
                 "- Cutscene 1 → Level 2\n" +
                 "- Cutscene 2 → Level 3")]
        [SerializeField] private int nextSceneBuildIndex = -1;

        [Header("Skip Input")]
        [Tooltip("Enable manual skip?")]
        [SerializeField] private bool allowManualSkip = true;

        [Tooltip("Keyboard key to skip.")]
        [SerializeField] private KeyCode skipKey = KeyCode.Space;

        [Tooltip("Controller buttons to skip.")]
        [SerializeField] private List<KeyCode> controllerSkipButtons = new List<KeyCode>
        {
            KeyCode.JoystickButton2,
            KeyCode.JoystickButton3
        };

        [Header("Auto-Skip")]
        [Tooltip("Auto-transition after cutscene ends?")]
        [SerializeField] private bool enableAutoSkip = true;

        [Tooltip("Delay (seconds) after cutscene ends before auto-transitioning.")]
        [SerializeField] private float autoSkipDelay = 2f;

        [Tooltip("Detect cutscene end via PlayableDirector (Timeline)?")]
        [SerializeField] private bool detectCutsceneEnd = true;

        [Tooltip("If detectCutsceneEnd is false, use this fixed duration (seconds).")]
        [SerializeField] private float fixedCutsceneDuration = 10f;

        [Header("Input Guard")]
        [Tooltip("Ignore input for this many seconds after the scene starts.\n" +
                 "Prevents accidental skips from the loading screen continue press carrying over.")]
        [SerializeField] private float inputGuardSeconds = 0.5f;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = true;

        private bool _transitionTriggered;
        private PlayableDirector _timeline;
        private Coroutine _autoSkipCoroutine;
        private float _sceneStartTime;

        // ===================== Lifecycle =====================

        private void Start()
        {
            _transitionTriggered = false;
            _sceneStartTime = Time.realtimeSinceStartup;

            // Preload the next scene
            if (nextSceneBuildIndex >= 0 && LoadingManager.Instance != null)
            {
                LoadingManager.Instance.PreloadScene(nextSceneBuildIndex);

                if (debugLogs)
                    Debug.Log($"[CutsceneTransition] Preloading next scene: {nextSceneBuildIndex}", this);
            }

            // Setup auto-skip detection
            if (enableAutoSkip)
                SetupAutoSkip();

            if (debugLogs)
                Debug.Log($"[CutsceneTransition] Started in scene: {gameObject.scene.name}", this);
        }

        private void OnDestroy()
        {
            // Unsubscribe from Timeline
            if (_timeline != null)
                _timeline.stopped -= OnTimelineStopped;
        }

        private void Update()
        {
            if (_transitionTriggered)
                return;

            if (!allowManualSkip)
                return;

            // Input guard: ignore input for a short time after scene starts
            if (Time.realtimeSinceStartup - _sceneStartTime < inputGuardSeconds)
                return;

            // Ignore input if LoadingManager is currently showing its UI
            if (LoadingManager.Instance != null && LoadingManager.Instance.IsTransitioning)
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
                    Debug.Log("[CutsceneTransition] Manual skip.", this);

                DoTransition();
            }
        }

        // ===================== Auto-Skip Setup =====================

        private void SetupAutoSkip()
        {
            if (detectCutsceneEnd)
            {
                _timeline = FindObjectOfType<PlayableDirector>();

                if (_timeline != null && _timeline.duration > 0.1)
                {
                    _timeline.stopped += OnTimelineStopped;

                    if (debugLogs)
                        Debug.Log($"[CutsceneTransition] Timeline detected. Duration: {_timeline.duration:F1}s", this);

                    return;
                }

                if (debugLogs)
                    Debug.LogWarning("[CutsceneTransition] No valid Timeline found. Using fixed duration.", this);
            }

            // Fallback: fixed duration
            float totalWait = fixedCutsceneDuration + autoSkipDelay;
            _autoSkipCoroutine = StartCoroutine(AutoSkipAfterSeconds(totalWait));

            if (debugLogs)
                Debug.Log($"[CutsceneTransition] Auto-skip in {totalWait}s (fixed duration).", this);
        }

        private void OnTimelineStopped(PlayableDirector director)
        {
            if (_transitionTriggered)
                return;

            if (debugLogs)
                Debug.Log($"[CutsceneTransition] Timeline ended. Auto-skip in {autoSkipDelay}s.", this);

            _autoSkipCoroutine = StartCoroutine(AutoSkipAfterSeconds(autoSkipDelay));
        }

        private IEnumerator AutoSkipAfterSeconds(float seconds)
        {
            if (seconds > 0f)
                yield return new WaitForSeconds(seconds);

            if (!_transitionTriggered)
            {
                if (debugLogs)
                    Debug.Log("[CutsceneTransition] Auto-skip triggered.", this);

                DoTransition();
            }

            _autoSkipCoroutine = null;
        }

        // ===================== Transition =====================

        private void DoTransition()
        {
            if (_transitionTriggered)
                return;

            _transitionTriggered = true;

            // Stop auto-skip coroutine if running
            if (_autoSkipCoroutine != null)
            {
                StopCoroutine(_autoSkipCoroutine);
                _autoSkipCoroutine = null;
            }

            if (nextSceneBuildIndex < 0)
            {
                Debug.LogError("[CutsceneTransition] nextSceneBuildIndex not configured!", this);
                return;
            }

            if (debugLogs)
                Debug.Log($"[CutsceneTransition] Transitioning to scene {nextSceneBuildIndex}.", this);

            if (LoadingManager.Instance != null)
            {
                LoadingManager.Instance.TransitionToScene(nextSceneBuildIndex);
            }
            else
            {
                Debug.LogError("[CutsceneTransition] LoadingManager.Instance is null! Direct loading.", this);
                SceneManager.LoadScene(nextSceneBuildIndex);
            }
        }
    }
}