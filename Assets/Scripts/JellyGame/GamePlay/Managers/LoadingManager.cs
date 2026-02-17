// FILEPATH: Assets/Scripts/Managers/LoadingManager.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace JellyGame.GamePlay.Managers
{
    /// <summary>
    /// Persistent Loading Manager - stays loaded throughout the game via DontDestroyOnLoad.
    /// 
    /// ARCHITECTURE:
    /// - PreloadScene(): Starts loading a scene in the background (allowSceneActivation = false).
    ///   Nothing in the preloaded scene runs (no Awake, no Start, no visual scripting, no Timeline).
    ///   Scene loads to ~90% and waits.
    ///
    /// - PreloadSceneFully(): Loads a scene to 100% and activates it, but immediately disables
    ///   all root GameObjects via a sceneLoaded callback. The scene is fully loaded (Awake runs,
    ///   but Start does NOT because objects are deactivated before the first frame). On transition,
    ///   objects are simply re-enabled — ZERO load delay.
    ///   Perfect for cutscene scenes that gate their start on an Animator bool.
    ///
    /// - TryInstantTransition(): If the preloaded scene is ready (>= 90% or fully preloaded),
    ///   switch to it IMMEDIATELY without showing any loading screen. Seamless transition.
    ///
    /// - TransitionToScene(): Full loading screen transition. Used as fallback when preload
    ///   isn't ready, or for transitions that don't have a preload (GameOver, Main Menu).
    ///
    /// Both paths share the same coroutine logic for neutralizing old scenes, activating the
    /// new scene, and cleaning up — only the UI and wait steps differ.
    /// </summary>
    [DisallowMultipleComponent]
    public class LoadingManager : MonoBehaviour
    {
        [Header("UI References")]
        [Tooltip("The main Canvas that contains all loading UI.")]
        [SerializeField] private Canvas loadingCanvas;

        [Tooltip("Optional: UI controller for progress bar and text.")]
        [SerializeField] private LoadingScreenUI uiController;

        [Header("Canvas Settings")]
        [Tooltip("Sort order for the loading canvas (higher = on top).")]
        [SerializeField] private int canvasSortOrder = 1000;

        [Header("Input")]
        [Tooltip("Key to press to continue after loading completes.")]
        [SerializeField] private KeyCode continueKey = KeyCode.E;

        [Tooltip("Controller buttons to continue.")]
        [SerializeField] private List<KeyCode> continueControllerButtons = new List<KeyCode>
        {
            KeyCode.JoystickButton0,
        };

        [Header("Timing")]
        [Tooltip("Minimum time (seconds) to show the loading screen.\n" +
                 "The slime fill animation will be spread over this duration.\n" +
                 "Set higher for a nicer fill effect (e.g. 2-3 seconds).")]
        [SerializeField] private float minimumDisplayTime = 2.0f;

        [Header("Options")]
        [Tooltip("If true, show 'Press E' and wait for input after minimum time.\n" +
                 "If false, auto-dismiss the loading screen after minimum time.")]
        [SerializeField] private bool waitForUserInput = false;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = true;

        // ===================== State =====================

        private bool _isTransitioning = false;
        private bool _continuePressed = false;
        private bool _acceptContinueInput = false;

        // Standard preloading (90%)
        private AsyncOperation _preloadOp = null;
        private int _preloadedBuildIndex = -1;

        // Full preloading (100%, dormant)
        private int _fullyPreloadedBuildIndex = -1;
        private bool _isFullyPreloading = false;
        private Coroutine _fullPreloadCoroutine = null;
        private AsyncOperation _fullPreloadOp = null; // Track so we can unblock queue if coroutine is stopped

        // The build index of the LoadingScreen scene itself — must never be unloaded
        private int _mySceneBuildIndex = -1;

        // Singleton
        private static LoadingManager _instance;
        public static LoadingManager Instance => _instance;

        /// <summary>True while a transition is in progress.</summary>
        public bool IsTransitioning => _isTransitioning;

        // ===================== Lifecycle =====================

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning("[LoadingManager] Duplicate instance detected! Destroying.", this);
                Destroy(gameObject);
                return;
            }

            _instance = this;

            // IMPORTANT: Capture our scene build index BEFORE DontDestroyOnLoad moves us
            _mySceneBuildIndex = gameObject.scene.buildIndex;

            // CRITICAL: All scene-based cleanup must happen BEFORE DontDestroyOnLoad,
            // because after DDOL, gameObject.scene changes to the "DontDestroyOnLoad"
            // pseudo-scene and we can no longer find objects in the LoadingScreen scene.
            DestroyEventSystemsInMyScene();
            DisableAllCamerasInMyScene();
            DisableAllAudioListenersInMyScene();

            // NOW move to DontDestroyOnLoad (gameObject.scene changes after this!)
            DontDestroyOnLoad(gameObject);

            // If Canvas is a separate root GameObject, persist it too
            if (loadingCanvas != null && loadingCanvas.transform.parent == null)
            {
                DontDestroyOnLoad(loadingCanvas.gameObject);

                if (debugLogs)
                    Debug.Log("[LoadingManager] Canvas is root object — applied DontDestroyOnLoad.", this);
            }

            // Start hidden
            HideLoadingUI();
        }

        private void Update()
        {
            // Only listen for continue input when the coroutine is ready for it
            if (!_acceptContinueInput || _continuePressed)
                return;

            if (Input.GetKeyDown(continueKey))
            {
                _continuePressed = true;
                return;
            }

            if (continueControllerButtons != null)
            {
                foreach (var button in continueControllerButtons)
                {
                    if (Input.GetKeyDown(button))
                    {
                        _continuePressed = true;
                        return;
                    }
                }
            }
        }

        // ===================== PUBLIC API =====================

        /// <summary>
        /// Start preloading a scene in the background (standard 90% preload).
        /// The scene will load to ~90% but NOT activate — nothing in it will run.
        /// Call TransitionToScene() or TryInstantTransition() later to switch to it.
        /// </summary>
        public void PreloadScene(int buildIndex)
        {
            if (!IsValidBuildIndex(buildIndex))
            {
                Debug.LogError($"[LoadingManager] PreloadScene: invalid build index {buildIndex}", this);
                return;
            }

            // Already preloading this exact scene (standard)?
            if (_preloadedBuildIndex == buildIndex && _preloadOp != null)
            {
                if (debugLogs)
                    Debug.Log($"[LoadingManager] Scene {buildIndex} already preloading. Skipping.", this);
                return;
            }

            // Already fully preloaded?
            if (_fullyPreloadedBuildIndex == buildIndex)
            {
                if (debugLogs)
                    Debug.Log($"[LoadingManager] Scene {buildIndex} already fully preloaded. Skipping.", this);
                return;
            }

            // If we had a different preload, drop the reference.
            if (_preloadOp != null && _preloadedBuildIndex != buildIndex && _preloadedBuildIndex >= 0)
            {
                if (debugLogs)
                    Debug.Log($"[LoadingManager] Dropping preload of scene {_preloadedBuildIndex} in favor of {buildIndex}." +
                              " Orphan will be cleaned up during next transition.", this);
            }

            if (debugLogs)
                Debug.Log($"[LoadingManager] Preloading scene {buildIndex}...", this);

            _preloadOp = SceneManager.LoadSceneAsync(buildIndex, LoadSceneMode.Additive);
            _preloadOp.allowSceneActivation = false;
            _preloadedBuildIndex = buildIndex;
        }

        /// <summary>
        /// Preload a scene to 100% — fully loaded and activated, but dormant (all root objects disabled).
        /// 
        /// HOW IT WORKS:
        /// 1. Loads scene additively to 90% (allowSceneActivation = false).
        /// 2. Registers a sceneLoaded callback that immediately disables all root GameObjects.
        /// 3. Sets allowSceneActivation = true — scene activates to 100%.
        /// 4. Awake() runs on all scripts, but Start() does NOT because objects are disabled
        ///    before the next frame.
        /// 5. On transition, objects are simply re-enabled — Start() runs, zero load delay.
        ///
        /// PERFECT FOR: Cutscene scenes where the cutscene is gated by an Animator bool.
        /// The Animator initializes in Awake (default state = waiting), and CutsceneStartTrigger
        /// fires in Start() after re-enable to set the bool and start the cutscene.
        /// </summary>
        public void PreloadSceneFully(int buildIndex)
        {
            if (!IsValidBuildIndex(buildIndex))
            {
                Debug.LogError($"[LoadingManager] PreloadSceneFully: invalid build index {buildIndex}", this);
                return;
            }

            // Already fully preloaded?
            if (_fullyPreloadedBuildIndex == buildIndex && !_isFullyPreloading)
            {
                if (debugLogs)
                    Debug.Log($"[LoadingManager] Scene {buildIndex} already fully preloaded. Skipping.", this);
                return;
            }

            // Already doing a full preload for a different scene? Cancel it.
            if (_fullPreloadCoroutine != null)
            {
                if (debugLogs)
                    Debug.Log($"[LoadingManager] Cancelling in-progress full preload of scene {_fullyPreloadedBuildIndex}.", this);

                StopCoroutine(_fullPreloadCoroutine);
                _fullPreloadCoroutine = null;
                _isFullyPreloading = false;

                // CRITICAL (WebGL): If the cancelled coroutine's AsyncOperation is still at
                // allowSceneActivation=false, it blocks the entire async queue. Activate it
                // so it can complete and unblock subsequent loads.
                if (_fullPreloadOp != null && !_fullPreloadOp.isDone)
                {
                    _fullPreloadOp.allowSceneActivation = true;

                    if (debugLogs)
                        Debug.Log($"[LoadingManager] Activated orphaned full preload op to unblock async queue.", this);
                }
                _fullPreloadOp = null;
                // Note: the partially loaded scene becomes an orphan — cleaned up on next transition.
            }

            // Cancel any standard preload for this scene (we're upgrading to full)
            if (_preloadedBuildIndex == buildIndex && _preloadOp != null)
            {
                if (debugLogs)
                    Debug.Log($"[LoadingManager] Upgrading standard preload of scene {buildIndex} to full preload.", this);

                // Reuse the existing AsyncOperation
                _fullPreloadCoroutine = StartCoroutine(FullPreloadCoroutine(buildIndex, _preloadOp));
                _preloadOp = null;
                _preloadedBuildIndex = -1;
                return;
            }

            // Drop any existing standard preload for a different scene
            if (_preloadOp != null && _preloadedBuildIndex >= 0)
            {
                if (debugLogs)
                    Debug.Log($"[LoadingManager] Dropping standard preload of scene {_preloadedBuildIndex} " +
                              $"in favor of full preload of {buildIndex}.", this);
            }
            _preloadOp = null;
            _preloadedBuildIndex = -1;

            _fullPreloadCoroutine = StartCoroutine(FullPreloadCoroutine(buildIndex, null));
        }

        private IEnumerator FullPreloadCoroutine(int buildIndex, AsyncOperation existingOp)
        {
            _isFullyPreloading = true;
            _fullyPreloadedBuildIndex = buildIndex;

            if (debugLogs)
                Debug.Log($"[LoadingManager] Full preload starting for scene {buildIndex}...", this);

            // Step 1: Get or create the async load operation
            AsyncOperation loadOp = existingOp;
            if (loadOp == null)
            {
                loadOp = SceneManager.LoadSceneAsync(buildIndex, LoadSceneMode.Additive);
                loadOp.allowSceneActivation = false;
            }

            // Store reference so TransitionCoroutine can unblock the queue if this coroutine is stopped
            _fullPreloadOp = loadOp;

            // Step 2: Wait for 90%
            while (loadOp.progress < 0.9f)
                yield return null;

            if (debugLogs)
                Debug.Log($"[LoadingManager] Full preload: scene {buildIndex} at 90%. Activating dormant...", this);

            // Step 3: Register callback to neutralize the scene THE MOMENT it activates.
            // sceneLoaded fires after Awake() but before Start(), so disabling root objects
            // prevents Start() from running on any scripts.
            int idx = buildIndex;
            bool neutralized = false;

            UnityEngine.Events.UnityAction<Scene, LoadSceneMode> neutralizeOnLoad = null;
            neutralizeOnLoad = (scene, mode) =>
            {
                if (scene.buildIndex == idx)
                {
                    foreach (GameObject obj in scene.GetRootGameObjects())
                        obj.SetActive(false);

                    neutralized = true;
                    SceneManager.sceneLoaded -= neutralizeOnLoad;

                    if (debugLogs)
                        Debug.Log($"[LoadingManager] Full preload: scene {idx} ({scene.name}) neutralized on activation. " +
                                  "Awake() ran, Start() blocked.", this);
                }
            };
            SceneManager.sceneLoaded += neutralizeOnLoad;

            // Step 4: Let it fully activate
            loadOp.allowSceneActivation = true;

            while (!loadOp.isDone)
                yield return null;

            // Safety: unsubscribe if callback didn't fire
            if (!neutralized)
            {
                SceneManager.sceneLoaded -= neutralizeOnLoad;

                Scene loadedScene = SceneManager.GetSceneByBuildIndex(buildIndex);
                if (loadedScene.isLoaded)
                {
                    foreach (GameObject obj in loadedScene.GetRootGameObjects())
                        obj.SetActive(false);
                }

                if (debugLogs)
                    Debug.LogWarning($"[LoadingManager] Full preload: callback didn't fire for scene {buildIndex}. " +
                                     "Neutralized manually.", this);
            }

            _isFullyPreloading = false;
            _fullPreloadCoroutine = null;
            _fullPreloadOp = null; // Fully loaded — no longer needed

            if (debugLogs)
                Debug.Log($"[LoadingManager] Full preload COMPLETE: scene {buildIndex} is 100% loaded and dormant.", this);
        }

        /// <summary>Check if a specific scene is preloaded (standard 90%) and ready to activate.</summary>
        public bool IsScenePreloaded(int buildIndex)
        {
            return _preloadedBuildIndex == buildIndex
                && _preloadOp != null
                && _preloadOp.progress >= 0.9f;
        }

        /// <summary>Check if a specific scene is fully preloaded (100%, dormant) and ready for instant switch.</summary>
        public bool IsSceneFullyPreloaded(int buildIndex)
        {
            return _fullyPreloadedBuildIndex == buildIndex
                && !_isFullyPreloading;
        }

        /// <summary>
        /// Try to switch to the preloaded scene INSTANTLY (no loading screen).
        /// Works with both standard preload (90%) and full preload (100% dormant).
        /// Full preload is prioritized — truly zero-delay switch.
        /// Returns true if transition starts. Returns false if not ready.
        /// </summary>
        public bool TryInstantTransition(int buildIndex)
        {
            if (_isTransitioning)
            {
                if (debugLogs)
                    Debug.Log("[LoadingManager] TryInstantTransition: already transitioning.", this);
                return false;
            }

            // Priority 1: Fully preloaded (100% dormant) — zero delay
            if (IsSceneFullyPreloaded(buildIndex))
            {
                if (debugLogs)
                    Debug.Log($"[LoadingManager] ⚡ Instant transition to FULLY preloaded scene {buildIndex} (zero delay!)", this);

                StartCoroutine(FullyPreloadedTransitionCoroutine(buildIndex));
                return true;
            }

            // Priority 2: Standard preload (90%) — minimal delay
            if (IsScenePreloaded(buildIndex))
            {
                if (debugLogs)
                    Debug.Log($"[LoadingManager] ⚡ Instant transition to scene {buildIndex} (preload ready!)", this);

                StartCoroutine(TransitionCoroutine(buildIndex, showLoadingScreen: false));
                return true;
            }

            if (debugLogs)
            {
                float progress = (_preloadedBuildIndex == buildIndex && _preloadOp != null)
                    ? _preloadOp.progress
                    : -1f;
                bool isFullyPreloading = (_fullyPreloadedBuildIndex == buildIndex && _isFullyPreloading);
                Debug.Log($"[LoadingManager] TryInstantTransition: scene {buildIndex} not ready " +
                          $"(preloaded={_preloadedBuildIndex}, progress={progress:F2}, " +
                          $"fullyPreloaded={_fullyPreloadedBuildIndex}, isFullyPreloading={isFullyPreloading}). " +
                          "Using loading screen.", this);
            }
            return false;
        }

        /// <summary>
        /// Full transition with loading screen. Use when preload isn't ready or not available.
        /// </summary>
        public void TransitionToScene(int buildIndex)
        {
            if (_isTransitioning)
            {
                Debug.LogWarning($"[LoadingManager] Already transitioning! Ignoring request for scene {buildIndex}.", this);
                return;
            }

            if (!IsValidBuildIndex(buildIndex))
            {
                Debug.LogError($"[LoadingManager] TransitionToScene: invalid build index {buildIndex}", this);
                return;
            }

            // If scene is fully preloaded, use the fast path even for loading screen requests
            if (IsSceneFullyPreloaded(buildIndex))
            {
                if (debugLogs)
                    Debug.Log($"[LoadingManager] TransitionToScene({buildIndex}): scene is fully preloaded, using fast path.", this);

                StartCoroutine(FullyPreloadedTransitionCoroutine(buildIndex));
                return;
            }

            if (debugLogs)
                Debug.Log($"[LoadingManager] === TransitionToScene({buildIndex}) with loading screen ===", this);

            StartCoroutine(TransitionCoroutine(buildIndex, showLoadingScreen: true));
        }

        // ===================== Fully Preloaded Transition =====================

        /// <summary>
        /// Fast transition for scenes that are already 100% loaded and dormant.
        /// Just neutralizes old scenes, re-enables the target scene, and sets it as active.
        /// No async loading, no waiting — instant.
        /// </summary>
        private IEnumerator FullyPreloadedTransitionCoroutine(int buildIndex)
        {
            _isTransitioning = true;
            _continuePressed = false;
            _acceptContinueInput = false;
            float transitionStartTime = Time.realtimeSinceStartup;

            // Consume the full preload state
            _fullyPreloadedBuildIndex = -1;
            _fullPreloadOp = null; // Already null after successful full preload, but clear for safety

            // Also clear any standard preload state to avoid confusion
            AsyncOperation orphanedOp = null;
            int orphanedBuildIndex = -1;

            if (_preloadOp != null && _preloadedBuildIndex >= 0)
            {
                orphanedOp = _preloadOp;
                orphanedBuildIndex = _preloadedBuildIndex;

                if (debugLogs)
                    Debug.Log($"[LoadingManager] Orphaned standard preload: scene {_preloadedBuildIndex}.", this);
            }
            _preloadOp = null;
            _preloadedBuildIndex = -1;

            // ---- STEP 1: Neutralize old scenes ----

            List<Scene> scenesToUnload = CollectOldScenes(buildIndex);

            foreach (Scene scene in scenesToUnload)
            {
                if (!scene.isLoaded) continue;

                DestroyPlayersInScene(scene);

                foreach (GameObject obj in scene.GetRootGameObjects())
                    obj.SetActive(false);

                if (debugLogs)
                    Debug.Log($"[LoadingManager]   Neutralized: {scene.name} (build {scene.buildIndex})", this);
            }

            // ---- STEP 1.5: Clean up orphaned standard preload if any ----

            if (orphanedOp != null)
            {
                if (debugLogs)
                    Debug.Log($"[LoadingManager] Cleaning up orphaned preload scene {orphanedBuildIndex}...", this);

                int orphanIdx = orphanedBuildIndex;
                UnityEngine.Events.UnityAction<Scene, LoadSceneMode> neutralizeOrphan = null;
                neutralizeOrphan = (scene, mode) =>
                {
                    if (scene.buildIndex == orphanIdx)
                    {
                        foreach (GameObject obj in scene.GetRootGameObjects())
                            obj.SetActive(false);

                        SceneManager.sceneLoaded -= neutralizeOrphan;
                    }
                };
                SceneManager.sceneLoaded += neutralizeOrphan;

                orphanedOp.allowSceneActivation = true;

                while (!orphanedOp.isDone)
                    yield return null;

                SceneManager.sceneLoaded -= neutralizeOrphan;

                Scene orphanedScene = SceneManager.GetSceneByBuildIndex(orphanedBuildIndex);
                if (orphanedScene.isLoaded)
                {
                    DestroyPlayersInScene(orphanedScene);
                    foreach (GameObject obj in orphanedScene.GetRootGameObjects())
                        obj.SetActive(false);

                    if (!scenesToUnload.Contains(orphanedScene))
                        scenesToUnload.Add(orphanedScene);
                }

                if (debugLogs)
                    Debug.Log($"[LoadingManager] Orphan scene {orphanedBuildIndex} cleaned up.", this);
            }

            // ---- STEP 2: Re-enable the fully preloaded scene ----

            Scene targetScene = SceneManager.GetSceneByBuildIndex(buildIndex);

            if (!targetScene.isLoaded)
            {
                Debug.LogError($"[LoadingManager] Fully preloaded scene {buildIndex} is not loaded! Falling back.", this);
                _isTransitioning = false;
                yield break;
            }

            // Set as active scene BEFORE re-enabling objects, so any Instantiate() in Start()
            // goes to the right scene.
            SceneManager.SetActiveScene(targetScene);

            if (debugLogs)
                Debug.Log($"[LoadingManager] Active scene set: {targetScene.name}", this);

            // Re-enable all root objects — this triggers OnEnable(), then Start() on the next frame
            foreach (GameObject obj in targetScene.GetRootGameObjects())
                obj.SetActive(true);

            if (debugLogs)
                Debug.Log($"[LoadingManager] Re-enabled {targetScene.name} root objects. Scene is now live.", this);

            // ---- STEP 3: Finalize ----

            Time.timeScale = 1f;
            _isTransitioning = false;
            _acceptContinueInput = false;

            if (debugLogs)
                Debug.Log($"[LoadingManager] Fully preloaded transition complete ({Time.realtimeSinceStartup - transitionStartTime:F4}s).", this);

            // ---- STEP 4: Unload old scenes in background ----

            if (scenesToUnload.Count > 0)
            {
                if (debugLogs)
                    Debug.Log($"[LoadingManager] Unloading {scenesToUnload.Count} old scene(s)...", this);

                List<AsyncOperation> unloadOps = new List<AsyncOperation>();

                foreach (Scene scene in scenesToUnload)
                {
                    if (!scene.isLoaded) continue;

                    AsyncOperation op = SceneManager.UnloadSceneAsync(scene);
                    if (op != null)
                    {
                        unloadOps.Add(op);

                        if (debugLogs)
                            Debug.Log($"[LoadingManager]   Unloading: {scene.name}", this);
                    }
                }

                float unloadStartTime = Time.realtimeSinceStartup;
                const float unloadTimeout = 10f;

                while (unloadOps.Count > 0)
                {
                    unloadOps.RemoveAll(op => op.isDone);

                    if (Time.realtimeSinceStartup - unloadStartTime > unloadTimeout)
                    {
                        Debug.LogWarning($"[LoadingManager] Unload timeout! {unloadOps.Count} scene(s) still unloading.", this);
                        break;
                    }

                    yield return null;
                }

                if (debugLogs)
                    Debug.Log("[LoadingManager] All old scenes unloaded.", this);
            }

            if (debugLogs)
                Debug.Log($"[LoadingManager] === Fully preloaded transition fully complete " +
                          $"({Time.realtimeSinceStartup - transitionStartTime:F2}s) ===", this);
        }

        // ===================== Standard Transition Coroutine =====================

        /// <summary>
        /// Core transition logic used by both instant and loading-screen paths.
        /// When showLoadingScreen is false, skips UI display, minimum wait, and user input.
        /// </summary>
        private IEnumerator TransitionCoroutine(int buildIndex, bool showLoadingScreen)
        {
            _isTransitioning = true;
            _continuePressed = false;
            _acceptContinueInput = false;
            float transitionStartTime = Time.realtimeSinceStartup;

            // ---- STEP 1: Show loading screen (only if needed) ----

            if (showLoadingScreen)
            {
                ShowLoadingUI();

                if (uiController != null)
                {
                    uiController.ResetProgress();
                    uiController.ShowContinuePrompt(false);
                }
            }

            // ---- STEP 2: Get or create the async load operation ----
            // CRITICAL: Consume/clear preload state IMMEDIATELY to prevent the new scene's
            // Start() from calling PreloadScene() and having our late clear wipe that new preload.

            AsyncOperation loadOp;
            AsyncOperation orphanedOp = null;
            int orphanedBuildIndex = -1;
            int orphanedFullBuildIndex = -1;

            if (_preloadedBuildIndex == buildIndex && _preloadOp != null)
            {
                // Consume the standard preload
                loadOp = _preloadOp;
                _preloadOp = null;
                _preloadedBuildIndex = -1;

                if (debugLogs)
                    Debug.Log($"[LoadingManager] Using preloaded scene {buildIndex} (progress={loadOp.progress:F2})", this);
            }
            else if (_fullyPreloadedBuildIndex == buildIndex && _fullPreloadOp != null && !_fullPreloadOp.isDone)
            {
                // Full preload is in progress for THIS scene — reuse its AsyncOperation.
                // This avoids creating a duplicate load for the same scene.
                loadOp = _fullPreloadOp;
                _fullPreloadOp = null;

                if (_fullPreloadCoroutine != null)
                {
                    StopCoroutine(_fullPreloadCoroutine);
                    _fullPreloadCoroutine = null;
                }
                _isFullyPreloading = false;
                _fullyPreloadedBuildIndex = -1;

                // Also capture any standard preload as orphan
                if (_preloadOp != null && _preloadedBuildIndex >= 0)
                {
                    orphanedOp = _preloadOp;
                    orphanedBuildIndex = _preloadedBuildIndex;
                }
                _preloadOp = null;
                _preloadedBuildIndex = -1;

                if (debugLogs)
                    Debug.Log($"[LoadingManager] Using in-progress full preload for scene {buildIndex} (progress={loadOp.progress:F2})", this);
            }
            else
            {
                // Capture orphan before clearing
                if (_preloadOp != null && _preloadedBuildIndex >= 0)
                {
                    orphanedOp = _preloadOp;
                    orphanedBuildIndex = _preloadedBuildIndex;

                    if (debugLogs)
                        Debug.Log($"[LoadingManager] Orphaned preload: scene {_preloadedBuildIndex}.", this);
                }

                // Clear preload state NOW
                _preloadOp = null;
                _preloadedBuildIndex = -1;

                if (debugLogs)
                    Debug.Log($"[LoadingManager] Scene {buildIndex} not preloaded. Loading now.", this);

                loadOp = SceneManager.LoadSceneAsync(buildIndex, LoadSceneMode.Additive);
                loadOp.allowSceneActivation = false;
            }

            // Handle orphaned full preload (transitioning to a different scene than what was fully preloaded)
            if (_fullyPreloadedBuildIndex >= 0 && _fullyPreloadedBuildIndex != buildIndex)
            {
                orphanedFullBuildIndex = _fullyPreloadedBuildIndex;

                if (debugLogs)
                    Debug.Log($"[LoadingManager] Orphaned full preload: scene {_fullyPreloadedBuildIndex} " +
                              $"(inProgress={_isFullyPreloading}).", this);
            }

            // If the full preload coroutine is still running, stop it and capture its AsyncOperation.
            // CRITICAL: If the coroutine was stopped before allowSceneActivation=true,
            // _fullPreloadOp is still blocking the async queue and must be cleaned up.
            AsyncOperation orphanedFullPreloadOp = null;
            if (_fullPreloadCoroutine != null)
            {
                StopCoroutine(_fullPreloadCoroutine);
                _fullPreloadCoroutine = null;

                // If the coroutine was mid-execution, the load op may still be at allowSceneActivation=false
                if (_fullPreloadOp != null && !_fullPreloadOp.isDone)
                {
                    orphanedFullPreloadOp = _fullPreloadOp;

                    if (debugLogs)
                        Debug.Log($"[LoadingManager] Captured in-progress full preload AsyncOperation for cleanup.", this);
                }

                _isFullyPreloading = false;
            }
            _fullPreloadOp = null;
            _fullyPreloadedBuildIndex = -1;

            // ---- STEP 2.5: Clean up orphaned preloads BEFORE waiting for new scene ----
            // CRITICAL (WebGL fix): Unity queues LoadSceneAsync operations. An earlier operation
            // sitting at allowSceneActivation=false BLOCKS ALL subsequent async operations from
            // completing — progress stays at 0 forever. On desktop this is sometimes tolerated
            // due to multi-threaded loading, but on WebGL (single-threaded) the block is absolute.
            // We MUST activate and dispose of ALL orphans BEFORE STEP 3, otherwise the new loadOp
            // will never reach 0.9 and the transition hangs forever.

            if (orphanedOp != null)
            {
                if (debugLogs)
                    Debug.Log($"[LoadingManager] Cleaning up orphaned scene {orphanedBuildIndex} (must clear before new scene can load)...", this);

                int orphanIdx = orphanedBuildIndex;
                UnityEngine.Events.UnityAction<Scene, LoadSceneMode> neutralizeOrphan = null;
                neutralizeOrphan = (scene, mode) =>
                {
                    if (scene.buildIndex == orphanIdx)
                    {
                        foreach (GameObject obj in scene.GetRootGameObjects())
                            obj.SetActive(false);

                        SceneManager.sceneLoaded -= neutralizeOrphan;

                        if (debugLogs)
                            Debug.Log($"[LoadingManager] Orphan scene {orphanIdx} ({scene.name}) neutralized on activation.", this);
                    }
                };
                SceneManager.sceneLoaded += neutralizeOrphan;

                orphanedOp.allowSceneActivation = true;

                while (!orphanedOp.isDone)
                    yield return null;

                // Safety: unsubscribe in case the callback didn't fire
                SceneManager.sceneLoaded -= neutralizeOrphan;

                if (debugLogs)
                    Debug.Log($"[LoadingManager] Orphan scene {orphanedBuildIndex} cleaned up. Async queue unblocked.", this);
            }

            // Clean up orphaned full preload AsyncOperation (from stopped coroutine)
            if (orphanedFullPreloadOp != null)
            {
                if (debugLogs)
                    Debug.Log($"[LoadingManager] Cleaning up orphaned full preload op (must clear before new scene can load)...", this);

                int fullOrphanIdx = orphanedFullBuildIndex >= 0 ? orphanedFullBuildIndex : -1;
                UnityEngine.Events.UnityAction<Scene, LoadSceneMode> neutralizeFullOrphan = null;
                neutralizeFullOrphan = (scene, mode) =>
                {
                    if (fullOrphanIdx >= 0 && scene.buildIndex == fullOrphanIdx)
                    {
                        foreach (GameObject obj in scene.GetRootGameObjects())
                            obj.SetActive(false);

                        SceneManager.sceneLoaded -= neutralizeFullOrphan;

                        if (debugLogs)
                            Debug.Log($"[LoadingManager] Full preload orphan scene {fullOrphanIdx} ({scene.name}) neutralized.", this);
                    }
                };
                SceneManager.sceneLoaded += neutralizeFullOrphan;

                orphanedFullPreloadOp.allowSceneActivation = true;

                while (!orphanedFullPreloadOp.isDone)
                    yield return null;

                SceneManager.sceneLoaded -= neutralizeFullOrphan;

                if (debugLogs)
                    Debug.Log($"[LoadingManager] Full preload orphan cleaned up. Async queue unblocked.", this);
            }

            // ---- STEP 3: Wait for scene to be ready ----

            if (showLoadingScreen)
            {
                bool sceneReady = false;

                while (true)
                {
                    float now = Time.realtimeSinceStartup;
                    float totalElapsed = now - transitionStartTime;

                    if (!sceneReady && loadOp.progress >= 0.9f)
                    {
                        sceneReady = true;
                        if (debugLogs)
                            Debug.Log($"[LoadingManager] Scene {buildIndex} ready at {totalElapsed:F2}s.", this);
                    }

                    float displayProgress;

                    if (minimumDisplayTime > 0f)
                    {
                        float timeProgress = Mathf.Clamp01(totalElapsed / minimumDisplayTime);

                        if (!sceneReady)
                        {
                            float loadProgress = Mathf.Clamp01(loadOp.progress / 0.9f) * 0.8f;
                            displayProgress = Mathf.Min(timeProgress, loadProgress);
                        }
                        else
                        {
                            displayProgress = timeProgress;
                        }
                    }
                    else
                    {
                        displayProgress = sceneReady ? 1f : Mathf.Clamp01(loadOp.progress / 0.9f);
                    }

                    if (uiController != null)
                        uiController.UpdateProgress(displayProgress);

                    if (sceneReady && totalElapsed >= minimumDisplayTime)
                        break;

                    yield return null;
                }

                if (uiController != null)
                    uiController.UpdateProgress(1f);

                if (debugLogs)
                    Debug.Log($"[LoadingManager] Loading wait complete ({Time.realtimeSinceStartup - transitionStartTime:F2}s).", this);
            }
            else
            {
                while (loadOp.progress < 0.9f)
                {
                    if (debugLogs && Time.frameCount % 30 == 0)
                        Debug.Log($"[LoadingManager] Instant: waiting for scene (progress={loadOp.progress:F2})", this);

                    yield return null;
                }

                if (debugLogs)
                    Debug.Log($"[LoadingManager] Instant: scene ready ({Time.realtimeSinceStartup - transitionStartTime:F2}s).", this);
            }

            // ---- STEP 4: (Optional) Wait for user input — only with loading screen ----

            if (showLoadingScreen && waitForUserInput)
            {
                if (uiController != null)
                    uiController.ShowContinuePrompt(true);

                if (debugLogs)
                    Debug.Log("[LoadingManager] Waiting for user to press continue...", this);

                _continuePressed = false;
                _acceptContinueInput = true;

                while (!_continuePressed)
                    yield return null;

                _acceptContinueInput = false;

                if (debugLogs)
                    Debug.Log("[LoadingManager] Continue pressed.", this);
            }

            // ---- STEP 5: Neutralize old scenes ----

            List<Scene> scenesToUnload = CollectOldScenes(buildIndex);

            foreach (Scene scene in scenesToUnload)
            {
                if (!scene.isLoaded) continue;

                DestroyPlayersInScene(scene);

                foreach (GameObject obj in scene.GetRootGameObjects())
                    obj.SetActive(false);

                if (debugLogs)
                    Debug.Log($"[LoadingManager]   Neutralized: {scene.name} (build {scene.buildIndex})", this);
            }

            // ---- STEP 5.5: Track orphaned scenes for unloading ----
            // Note: Orphaned standard preloads were already activated and neutralized in STEP 2.5
            // (required to unblock the async queue). Here we just add them to the unload list.

            if (orphanedOp != null && orphanedBuildIndex >= 0)
            {
                Scene orphanedScene = SceneManager.GetSceneByBuildIndex(orphanedBuildIndex);
                if (orphanedScene.isLoaded)
                {
                    DestroyPlayersInScene(orphanedScene);

                    // Safety: ensure objects are still deactivated
                    foreach (GameObject obj in orphanedScene.GetRootGameObjects())
                        obj.SetActive(false);

                    if (!scenesToUnload.Contains(orphanedScene))
                        scenesToUnload.Add(orphanedScene);

                    if (debugLogs)
                        Debug.Log($"[LoadingManager] Orphan scene {orphanedBuildIndex} queued for unload.", this);
                }
            }

            // Clean up orphaned fully preloaded scene (already activated but dormant — just unload)
            if (orphanedFullBuildIndex >= 0)
            {
                Scene orphanFullScene = SceneManager.GetSceneByBuildIndex(orphanedFullBuildIndex);
                if (orphanFullScene.isLoaded && !scenesToUnload.Contains(orphanFullScene))
                {
                    scenesToUnload.Add(orphanFullScene);

                    if (debugLogs)
                        Debug.Log($"[LoadingManager] Orphaned fully preloaded scene {orphanedFullBuildIndex} queued for unload.", this);
                }
            }

            // ---- STEP 6: Activate the new scene ----

            if (debugLogs)
                Debug.Log($"[LoadingManager] Activating scene {buildIndex}...", this);

            int targetIndex = buildIndex;
            bool activeSceneSet = false;

            UnityEngine.Events.UnityAction<Scene, LoadSceneMode> onSceneLoaded = null;
            onSceneLoaded = (scene, mode) =>
            {
                if (scene.buildIndex == targetIndex)
                {
                    SceneManager.SetActiveScene(scene);
                    activeSceneSet = true;
                    SceneManager.sceneLoaded -= onSceneLoaded;

                    if (debugLogs)
                        Debug.Log($"[LoadingManager] Active scene set early (sceneLoaded): {scene.name}", this);
                }
            };
            SceneManager.sceneLoaded += onSceneLoaded;

            loadOp.allowSceneActivation = true;

            while (!loadOp.isDone)
                yield return null;

            // Safety: ensure active scene is set
            if (!activeSceneSet)
            {
                SceneManager.sceneLoaded -= onSceneLoaded;

                Scene newScene = SceneManager.GetSceneByBuildIndex(buildIndex);
                if (newScene.isLoaded)
                {
                    SceneManager.SetActiveScene(newScene);

                    if (debugLogs)
                        Debug.Log($"[LoadingManager] Active scene set (fallback): {newScene.name}", this);
                }
                else
                {
                    Debug.LogError($"[LoadingManager] Scene {buildIndex} failed to activate!", this);
                }
            }

            // ---- STEP 8: Hide loading screen ----

            Time.timeScale = 1f;

            if (showLoadingScreen)
                HideLoadingUI();

            _isTransitioning = false;
            _acceptContinueInput = false;

            if (debugLogs)
            {
                string mode = showLoadingScreen ? "loading screen" : "instant";
                Debug.Log($"[LoadingManager] Transition complete ({mode}, {Time.realtimeSinceStartup - transitionStartTime:F2}s).", this);
            }

            // ---- STEP 9: Unload old scenes ----

            if (scenesToUnload.Count > 0)
            {
                if (debugLogs)
                    Debug.Log($"[LoadingManager] Unloading {scenesToUnload.Count} old scene(s)...", this);

                List<AsyncOperation> unloadOps = new List<AsyncOperation>();

                foreach (Scene scene in scenesToUnload)
                {
                    if (!scene.isLoaded) continue;

                    AsyncOperation op = SceneManager.UnloadSceneAsync(scene);
                    if (op != null)
                    {
                        unloadOps.Add(op);

                        if (debugLogs)
                            Debug.Log($"[LoadingManager]   Unloading: {scene.name}", this);
                    }
                }

                float unloadStartTime = Time.realtimeSinceStartup;
                const float unloadTimeout = 10f;

                while (unloadOps.Count > 0)
                {
                    unloadOps.RemoveAll(op => op.isDone);

                    if (Time.realtimeSinceStartup - unloadStartTime > unloadTimeout)
                    {
                        Debug.LogWarning($"[LoadingManager] Unload timeout! {unloadOps.Count} scene(s) still unloading.", this);
                        break;
                    }

                    yield return null;
                }

                if (debugLogs)
                    Debug.Log("[LoadingManager] All old scenes unloaded.", this);
            }

            if (debugLogs)
                Debug.Log($"[LoadingManager] === Transition fully complete ({Time.realtimeSinceStartup - transitionStartTime:F2}s) ===", this);
        }

        // ===================== Scene Unloading =====================

        private List<Scene> CollectOldScenes(int keepBuildIndex)
        {
            List<Scene> result = new List<Scene>();

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);

                if (!scene.isLoaded) continue;
                if (scene.buildIndex == keepBuildIndex) continue;
                if (scene.buildIndex == _mySceneBuildIndex) continue;
                if (scene.name == "DontDestroyOnLoad") continue;

                result.Add(scene);
            }

            return result;
        }

        private void DestroyPlayersInScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            GameObject[] allPlayers = GameObject.FindGameObjectsWithTag("DrawingCube");
    
            if (debugLogs)
                Debug.Log($"[LoadingManager] DestroyPlayersInScene({scene.name}): Found {allPlayers.Length} DrawingCube object(s) total", this);

            foreach (GameObject player in allPlayers)
            {
                if (player == null) continue;
        
                if (debugLogs)
                    Debug.Log($"[LoadingManager]   Found: '{player.name}' in scene '{player.scene.name}' (build {player.scene.buildIndex}). " +
                              $"Target scene: '{scene.name}' (build {scene.buildIndex}). Match={player.scene == scene}", this);

                if (player.scene == scene)
                {
                    if (debugLogs)
                        Debug.Log($"[LoadingManager]   >>> DESTROYING player: {player.name} in {scene.name}", this);

                    Destroy(player);
                }
            }
        }

        // ===================== UI Helpers =====================

        private void ShowLoadingUI()
        {
            if (loadingCanvas == null) return;

            if (!loadingCanvas.gameObject.activeSelf)
                loadingCanvas.gameObject.SetActive(true);

            loadingCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            loadingCanvas.sortingOrder = canvasSortOrder;
            loadingCanvas.enabled = true;

            if (uiController != null)
                uiController.SetVisible(true);

            if (debugLogs)
                Debug.Log("[LoadingManager] Loading UI shown.", this);
        }

        private void HideLoadingUI()
        {
            if (uiController != null)
                uiController.SetVisible(false);

            if (loadingCanvas != null)
            {
                loadingCanvas.enabled = false;

                if (debugLogs)
                    Debug.Log("[LoadingManager] Loading UI hidden.", this);
            }
        }

        // ===================== Utility =====================

        private void DestroyEventSystemsInMyScene()
        {
            Scene myScene = gameObject.scene;
            if (!myScene.IsValid()) return;

            foreach (GameObject obj in myScene.GetRootGameObjects())
            {
                foreach (var es in obj.GetComponentsInChildren<EventSystem>(true))
                {
                    if (debugLogs)
                        Debug.Log($"[LoadingManager] Destroying EventSystem: {es.name}", this);

                    foreach (var inputModule in es.GetComponents<BaseInputModule>())
                        Destroy(inputModule);

                    Destroy(es);
                }
            }
        }

        private void DisableAllCamerasInMyScene()
        {
            Scene myScene = gameObject.scene;
            if (!myScene.IsValid()) return;

            foreach (GameObject obj in myScene.GetRootGameObjects())
                foreach (var cam in obj.GetComponentsInChildren<UnityEngine.Camera>(true))
                    cam.enabled = false;
        }

        private void DisableAllAudioListenersInMyScene()
        {
            Scene myScene = gameObject.scene;
            if (!myScene.IsValid()) return;

            foreach (GameObject obj in myScene.GetRootGameObjects())
                foreach (var listener in obj.GetComponentsInChildren<AudioListener>(true))
                    listener.enabled = false;
        }

        private static bool IsValidBuildIndex(int buildIndex)
        {
            return buildIndex >= 0 && buildIndex < SceneManager.sceneCountInBuildSettings;
        }
    }
}