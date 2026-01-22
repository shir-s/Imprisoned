// FILEPATH: Assets/Scripts/Managers/CutsceneSceneTransition.cs
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace JellyGame.GamePlay.Managers
{
    /// <summary>
    /// Preloads a target scene and activates it ONLY when a key is pressed (default: Space).
    ///
    /// - No UI
    /// - No Visual Scripting
    /// - No Timeline hooks
    /// - Designer-proof
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
        [SerializeField] private KeyCode skipKey = KeyCode.Space;

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
            if (!_skipRequested && Input.GetKeyDown(skipKey))
            {
                if (debugLogs)
                    Debug.Log($"[CutsceneSceneTransition] Skip key pressed ({skipKey}).", this);

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
