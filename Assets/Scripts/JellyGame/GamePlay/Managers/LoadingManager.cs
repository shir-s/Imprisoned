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
    /// NEW ARCHITECTURE:
    /// - PreloadScene(): Starts loading a scene in the background (allowSceneActivation = false).
    ///   Nothing in the preloaded scene runs (no Awake, no Start, no visual scripting, no Timeline).
    /// - TransitionToScene(): Unloads ALL old scenes first, THEN activates the new scene.
    ///   This guarantees no singleton conflicts (old player is destroyed before new one spawns).
    /// - Loading screen shows while transition is in progress.
    /// - Handles orphaned preloads (e.g. player dies → preloaded cutscene is cleaned up properly).
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
        private bool _acceptContinueInput = false; // Only true when we're ready for user to press continue

        // Preloading
        private AsyncOperation _preloadOp = null;
        private int _preloadedBuildIndex = -1;

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
        /// Start preloading a scene in the background.
        /// The scene will load to ~90% but NOT activate — nothing in it will run.
        /// Call TransitionToScene() later to actually switch to it.
        /// 
        /// Safe to call multiple times with the same index. If a DIFFERENT scene is requested,
        /// the old preload reference is dropped (it will be cleaned up during the next transition).
        /// </summary>
        public void PreloadScene(int buildIndex)
        {
            if (!IsValidBuildIndex(buildIndex))
            {
                Debug.LogError($"[LoadingManager] PreloadScene: invalid build index {buildIndex}", this);
                return;
            }

            // Already preloading this exact scene?
            if (_preloadedBuildIndex == buildIndex && _preloadOp != null)
            {
                if (debugLogs)
                    Debug.Log($"[LoadingManager] Scene {buildIndex} already preloading. Skipping.", this);
                return;
            }

            // If we had a different preload, drop the reference.
            // The orphaned AsyncOperation will be cleaned up in the next TransitionCoroutine.
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
        /// Transition to a scene. This will:
        /// 1. Show loading screen
        /// 2. Wait for scene to finish loading (if not preloaded yet)
        /// 3. Clean up any orphaned preloads
        /// 4. Unload ALL current gameplay scenes (old player is destroyed here)
        /// 5. Activate the new scene (new player spawns here, cutscenes start here)
        /// 6. Wait for user input (optional)
        /// 7. Hide loading screen
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

            if (debugLogs)
                Debug.Log($"[LoadingManager] === TransitionToScene({buildIndex}) ===", this);

            StartCoroutine(TransitionCoroutine(buildIndex));
        }

        /// <summary>Check if a specific scene is preloaded and ready to activate.</summary>
        public bool IsScenePreloaded(int buildIndex)
        {
            return _preloadedBuildIndex == buildIndex
                && _preloadOp != null
                && _preloadOp.progress >= 0.9f;
        }

        // ===================== Transition Coroutine =====================

        private IEnumerator TransitionCoroutine(int buildIndex)
        {
            _isTransitioning = true;
            _continuePressed = false;
            _acceptContinueInput = false;
            float transitionStartTime = Time.realtimeSinceStartup;

            // ---- STEP 1: Show loading screen ----

            ShowLoadingUI();

            if (uiController != null)
            {
                uiController.ResetProgress();
                uiController.ShowContinuePrompt(false);
            }

            // ---- STEP 2: Get or create the async load operation ----
            // CRITICAL: We consume/clear _preloadOp and _preloadedBuildIndex IMMEDIATELY here.
            // If we delay clearing to the end of the coroutine, the new scene's Start() might
            // call PreloadScene() during Step 6 (activation), and our late clear would WIPE that
            // new preload — causing duplicate loads and deadlocks on the next transition.

            AsyncOperation loadOp;
            AsyncOperation orphanedOp = null;
            int orphanedBuildIndex = -1;

            if (_preloadedBuildIndex == buildIndex && _preloadOp != null)
            {
                // Consume the preload
                loadOp = _preloadOp;
                _preloadOp = null;
                _preloadedBuildIndex = -1;

                if (debugLogs)
                    Debug.Log($"[LoadingManager] Using preloaded scene {buildIndex} (progress={loadOp.progress:F2})", this);
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

                // Clear preload state NOW — before any new scene can call PreloadScene()
                _preloadOp = null;
                _preloadedBuildIndex = -1;

                if (debugLogs)
                    Debug.Log($"[LoadingManager] Scene {buildIndex} not preloaded. Loading now.", this);

                loadOp = SceneManager.LoadSceneAsync(buildIndex, LoadSceneMode.Additive);
                loadOp.allowSceneActivation = false;
            }

            // ---- STEP 3: Wait for scene to reach 0.9 + minimum display time ----
            // The scene stays FROZEN at 0.9 — nothing runs, nothing renders.
            // We animate the slime fill from 0% to 100% over minimumDisplayTime.
            // If loading takes longer than minimumDisplayTime, we wait for loading too.

            bool sceneReady = false;

            while (true)
            {
                float now = Time.realtimeSinceStartup;
                float totalElapsed = now - transitionStartTime;

                // Check if the async load reached 0.9
                if (!sceneReady && loadOp.progress >= 0.9f)
                {
                    sceneReady = true;

                    if (debugLogs)
                        Debug.Log($"[LoadingManager] Scene {buildIndex} ready at {totalElapsed:F2}s.", this);
                }

                // Animate fill: 0% → 100% over minimumDisplayTime
                // If loading is slow, the bar will pause at 80% until scene is ready,
                // then continue to 100%.
                float displayProgress;

                if (minimumDisplayTime > 0f)
                {
                    float timeProgress = Mathf.Clamp01(totalElapsed / minimumDisplayTime);

                    if (!sceneReady)
                    {
                        // Scene still loading: cap at 80% so we don't show 100% before ready
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
                    // No minimum time: just show actual load progress
                    displayProgress = sceneReady ? 1f : Mathf.Clamp01(loadOp.progress / 0.9f);
                }

                if (uiController != null)
                    uiController.UpdateProgress(displayProgress);

                // Exit when BOTH conditions are met:
                // 1) Scene is ready (progress >= 0.9)
                // 2) Minimum display time has passed
                if (sceneReady && totalElapsed >= minimumDisplayTime)
                    break;

                yield return null;
            }

            if (uiController != null)
                uiController.UpdateProgress(1f);

            if (debugLogs)
                Debug.Log($"[LoadingManager] Loading wait complete ({Time.realtimeSinceStartup - transitionStartTime:F2}s).", this);

            // ---- STEP 4: (Optional) Wait for user input ----

            if (waitForUserInput)
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
            // Destroy players and disable ALL objects. After this, old scenes are functionally
            // dead — no scripts run, no singletons exist, no rendering.
            // The actual memory unload happens later (Step 9) once the load op is no longer blocking.

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

            // ---- STEP 6: Activate the new scene ----
            // Old scenes are neutralized (all objects disabled, player destroyed).
            // NOW scripts run, player spawns, cutscenes start.
            // NOTE: Must activate BEFORE unloading — Unity's scene op queue is sequential,
            // and a pending load at 0.9 blocks all UnloadSceneAsync calls.
            //
            // CRITICAL: We use sceneLoaded callback to set the active scene IMMEDIATELY
            // when the scene finishes loading, BEFORE Start() runs on the new scene's objects.
            // Without this, Instantiate() calls in Start() (e.g. PlayerSpawner) would place
            // objects in the OLD active scene (Tutorial), which then gets unloaded — destroying
            // the newly spawned player.
            //
            // Unity execution order after allowSceneActivation:
            //   1. Awake() → 2. OnEnable() → 3. sceneLoaded callback ← we set active here
            //   4. Start() → 5. coroutine resumes (yield return null)

            if (debugLogs)
                Debug.Log($"[LoadingManager] Activating scene {buildIndex}...", this);

            // Register callback to set active scene as soon as it loads (before Start)
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

            // Safety: ensure active scene is set (in case callback didn't fire)
            if (!activeSceneSet)
            {
                SceneManager.sceneLoaded -= onSceneLoaded; // cleanup

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

            // ---- STEP 7: Clean up orphaned preload (if any) ----

            if (orphanedOp != null)
            {
                if (debugLogs)
                    Debug.Log($"[LoadingManager] Cleaning up orphaned scene {orphanedBuildIndex}...", this);

                orphanedOp.allowSceneActivation = true;

                while (!orphanedOp.isDone)
                    yield return null;

                Scene orphanedScene = SceneManager.GetSceneByBuildIndex(orphanedBuildIndex);
                if (orphanedScene.isLoaded)
                {
                    DestroyPlayersInScene(orphanedScene);

                    foreach (GameObject obj in orphanedScene.GetRootGameObjects())
                        obj.SetActive(false);

                    if (!scenesToUnload.Contains(orphanedScene))
                        scenesToUnload.Add(orphanedScene);
                }
            }

            // ---- STEP 8: Hide loading screen — new scene becomes visible ----
            // SAFETY: Ensure timeScale is 1. It can get stuck at 0 if a WinSequence
            // coroutine was killed mid-pause, or a pause menu was open during transition.

            Time.timeScale = 1f;
            HideLoadingUI();

            _isTransitioning = false;
            _acceptContinueInput = false;

            if (debugLogs)
                Debug.Log("[LoadingManager] Loading UI hidden. New scene is now visible.", this);

            // ---- STEP 9: Unload old scenes (background cleanup) ----
            // UI is already hidden — user sees the new scene.
            // Wait for unloads to finish so scenes don't stay as "(is unloading)" in hierarchy.

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

                // Wait for all unloads (with safety timeout)
                float unloadStartTime = Time.realtimeSinceStartup;
                const float unloadTimeout = 10f;

                while (unloadOps.Count > 0)
                {
                    unloadOps.RemoveAll(op => op.isDone);

                    if (Time.realtimeSinceStartup - unloadStartTime > unloadTimeout)
                    {
                        Debug.LogWarning($"[LoadingManager] Unload timeout ({unloadTimeout}s)! {unloadOps.Count} scene(s) still unloading.", this);
                        break;
                    }

                    yield return null;
                }

                if (debugLogs)
                    Debug.Log("[LoadingManager] All old scenes unloaded.", this);
            }

            if (debugLogs)
                Debug.Log($"[LoadingManager] === Transition complete ({Time.realtimeSinceStartup - transitionStartTime:F2}s) ===", this);
        }

        // ===================== Scene Unloading =====================

        /// <summary>
        /// Collect all loaded scenes that should be unloaded (everything except target + LoadingScreen + DDOL).
        /// Does NOT unload them — just returns the list.
        /// </summary>
        private List<Scene> CollectOldScenes(int keepBuildIndex)
        {
            List<Scene> result = new List<Scene>();

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);

                if (!scene.isLoaded) continue;
                if (scene.buildIndex == keepBuildIndex) continue;
                if (scene.buildIndex == _mySceneBuildIndex) continue; // Never unload LoadingScreen
                if (scene.name == "DontDestroyOnLoad") continue;

                result.Add(scene);
            }

            return result;
        }

        private void DestroyPlayersInScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            foreach (GameObject player in GameObject.FindGameObjectsWithTag("DrawingCube"))
            {
                if (player != null && player.scene == scene)
                {
                    if (debugLogs)
                        Debug.Log($"[LoadingManager]   Destroying player: {player.name} in {scene.name}", this);

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

        /// <summary>
        /// Destroy any EventSystem in the LoadingScreen scene.
        /// LoadingManager only uses Input.GetKeyDown (no UI events), so it doesn't need one.
        /// If an EventSystem persists via DontDestroyOnLoad, it conflicts with every gameplay
        /// scene's EventSystem and can block UI input (buttons, raycasts, etc.).
        /// </summary>
        private void DestroyEventSystemsInMyScene()
        {
            Scene myScene = gameObject.scene;
            if (!myScene.IsValid()) return;

            foreach (GameObject obj in myScene.GetRootGameObjects())
            {
                foreach (var es in obj.GetComponentsInChildren<EventSystem>(true))
                {
                    if (debugLogs)
                        Debug.Log($"[LoadingManager] Destroying EventSystem: {es.name} (prevents conflict with gameplay scenes)", this);

                    // Also destroy any input modules on the same GameObject
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