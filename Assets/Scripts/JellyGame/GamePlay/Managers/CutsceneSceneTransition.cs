// FILEPATH: Assets/Scripts/Managers/CutsceneSceneTransition.cs
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

namespace JellyGame.GamePlay.Managers
{
    /// <summary>
    /// Preloads a target scene and activates it ONLY when a key is pressed (Space or Controller A).
    /// </summary>
    [DisallowMultipleComponent]
    public class CutsceneSceneTransition : MonoBehaviour
    {
        public enum SceneIdMode { BuildIndex, SceneName }

        [Header("Next Scene")]
        [SerializeField] private SceneIdMode sceneIdMode = SceneIdMode.BuildIndex;
        [SerializeField] private int nextSceneBuildIndex = -1;
        [SerializeField] private string nextSceneName = "";

        [Header("Skip Input")]
        [Tooltip("Keyboard key to skip.")]
        [SerializeField] private KeyCode skipKey = KeyCode.Space;

        [Tooltip("Controller buttons to skip (Auto-filled for PC/Mac support).")]
        [SerializeField] private List<KeyCode> controllerSkipButtons = new List<KeyCode>
        {
            KeyCode.JoystickButton0, // A on Windows
            KeyCode.JoystickButton1  // A on Mac
        };

        [Header("Preload")]
        [Tooltip("If true, preload starts immediately on Start.")]
        [SerializeField] private bool preloadOnStart = true;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = true;

        private AsyncOperation _preloadOp;
        private bool _skipRequested;

        private void Start()
        {
            if (preloadOnStart)
                StartPreload();
        }

        private void Update()
        {
            if (_skipRequested) return;

            // 1. Check Keyboard
            bool inputDetected = Input.GetKeyDown(skipKey);

            // 2. Check Controller Buttons (only if keyboard wasn't pressed)
            if (!inputDetected && controllerSkipButtons != null)
            {
                for (int i = 0; i < controllerSkipButtons.Count; i++)
                {
                    if (Input.GetKeyDown(controllerSkipButtons[i]))
                    {
                        inputDetected = true;
                        break;
                    }
                }
            }

            // 3. Action
            if (inputDetected)
            {
                if (debugLogs)
                    Debug.Log($"[CutsceneSceneTransition] Skip input detected.", this);

                _skipRequested = true;

                if (_preloadOp == null)
                    StartPreload();
                else
                    TryActivateIfReady();
            }
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
                if (_skipRequested)
                    TryActivateIfReady();

                yield return null;
            }
        }

        private void TryActivateIfReady()
        {
            if (_preloadOp == null)
                return;

            // Unity loads to ~0.9f and waits until allowSceneActivation = true
            if (_preloadOp.progress < 0.89f)
                return;

            if (debugLogs)
                Debug.Log("[CutsceneSceneTransition] Activating preloaded scene (skip).", this);

            _preloadOp.allowSceneActivation = true;
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
    }
}