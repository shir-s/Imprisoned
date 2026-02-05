// FILEPATH: Assets/Scripts/Managers/LoadingManager.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace JellyGame.GamePlay.Managers
{
    /// <summary>
    /// Persistent Loading Manager - stays loaded throughout the game.
    /// Shows/hides loading UI as needed without unloading the scene.
    /// 
    /// UPDATED: Now properly unloads ALL non-essential scenes when loading new ones,
    /// preventing duplicate scenes and memory leaks.
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
        [Tooltip("Sort order for the loading canvas (higher = on top). Set high to ensure it's above everything.")]
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
        [Tooltip("Minimum time to show loading screen (prevents flash if loading is instant).")]
        [SerializeField] private float minimumDisplayTime = 1.0f;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = true;

        // State
        private bool _isLoading = false;
        private bool _loadingComplete = false;
        private bool _canContinue = false;
        private List<AsyncOperation> _loadOperations = new List<AsyncOperation>();
        private float _loadStartTime;
        
        // Track scenes to unload (all scenes except LoadingScreen and the new ones)
        private List<Scene> _scenesToUnload = new List<Scene>();

        // Singleton instance
        private static LoadingManager _instance;
        public static LoadingManager Instance => _instance;

        /// <summary>
        /// Get the build index of the LoadingScreen scene (for exclusion during unload).
        /// </summary>
        private int LoadingScreenBuildIndex => gameObject.scene.buildIndex;

        private void Awake()
        {
            // Singleton pattern
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning("[LoadingManager] Multiple instances detected! Destroying duplicate.", this);
                Destroy(gameObject);
                return;
            }

            _instance = this;

            // This scene should persist
            DontDestroyOnLoad(gameObject);
            
            // CRITICAL: If Canvas is a separate root GameObject, it needs DontDestroyOnLoad too!
            if (loadingCanvas != null && loadingCanvas.transform.parent == null)
            {
                DontDestroyOnLoad(loadingCanvas.gameObject);
                
                if (debugLogs)
                    Debug.Log("[LoadingManager] Canvas is root object - applied DontDestroyOnLoad to it too.", this);
            }

            // Hide UI by default
            if (loadingCanvas != null)
                loadingCanvas.enabled = false;

            // Disable any cameras in the LoadingScreen scene
            DisableAllCamerasInScene();
            DisableAllAudioListenersInScene();
        }

        private void DisableAllCamerasInScene()
        {
            Scene loadingScene = gameObject.scene;
            GameObject[] rootObjects = loadingScene.GetRootGameObjects();

            foreach (GameObject obj in rootObjects)
            {
                UnityEngine.Camera[] cameras = obj.GetComponentsInChildren<UnityEngine.Camera>(true);
                foreach (UnityEngine.Camera cam in cameras)
                {
                    cam.enabled = false;
                    if (debugLogs)
                        Debug.Log($"[LoadingManager] Disabled camera: {cam.name}", this);
                }
            }
        }

        private void DisableAllAudioListenersInScene()
        {
            Scene loadingScene = gameObject.scene;
            GameObject[] rootObjects = loadingScene.GetRootGameObjects();

            foreach (GameObject obj in rootObjects)
            {
                AudioListener[] listeners = obj.GetComponentsInChildren<AudioListener>(true);
                foreach (AudioListener listener in listeners)
                {
                    listener.enabled = false;
                    if (debugLogs)
                        Debug.Log($"[LoadingManager] Disabled audio listener: {listener.name}", this);
                }
            }
        }

        private void Update()
        {
            if (!_canContinue)
                return;

            bool inputDetected = Input.GetKeyDown(continueKey);

            if (!inputDetected && continueControllerButtons != null)
            {
                foreach (var button in continueControllerButtons)
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
                    Debug.Log("[LoadingManager] Continue input detected.", this);

                OnUserContinue();
            }
        }

        /// <summary>
        /// Start loading scenes. Called by GameSceneManager or CutsceneSceneTransition.
        /// </summary>
        public void StartLoading(int[] sceneIndices, int firstSceneToActivate = 0)
        {
            if (_isLoading)
            {
                Debug.LogWarning("[LoadingManager] Already loading! Ignoring new request.", this);
                return;
            }

            if (sceneIndices == null || sceneIndices.Length == 0)
            {
                Debug.LogError("[LoadingManager] Cannot load null or empty scene array!", this);
                return;
            }

            if (debugLogs)
                Debug.Log($"[LoadingManager] Starting load of {sceneIndices.Length} scene(s)", this);

            // Collect ALL scenes that need to be unloaded (everything except LoadingScreen)
            CollectScenesToUnload(sceneIndices);

            SceneTransitionHelper.SetScenesToLoad(sceneIndices, firstSceneToActivate);
            StartCoroutine(LoadScenesCoroutine());
        }

        /// <summary>
        /// Collect all currently loaded scenes that should be unloaded.
        /// Excludes: LoadingScreen scene and the new scenes we're about to load.
        /// </summary>
        private void CollectScenesToUnload(int[] newSceneIndices)
        {
            _scenesToUnload.Clear();
            
            HashSet<int> newSceneSet = new HashSet<int>(newSceneIndices);
            int loadingScreenIndex = LoadingScreenBuildIndex;

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                
                if (!scene.isLoaded)
                    continue;

                // Don't unload LoadingScreen
                if (scene.buildIndex == loadingScreenIndex)
                    continue;

                // Don't unload scenes we're about to load (they might already be loaded additively)
                if (newSceneSet.Contains(scene.buildIndex))
                    continue;

                _scenesToUnload.Add(scene);
                
                if (debugLogs)
                    Debug.Log($"[LoadingManager] Will unload scene: {scene.name} (buildIndex={scene.buildIndex})", this);
            }

            if (debugLogs)
                Debug.Log($"[LoadingManager] Total scenes to unload: {_scenesToUnload.Count}", this);
        }

        private IEnumerator LoadScenesCoroutine()
        {
            _isLoading = true;
            _loadingComplete = false;
            _canContinue = false;
            _loadStartTime = Time.realtimeSinceStartup;
            _loadOperations.Clear();

            // Show loading UI
            if (loadingCanvas != null)
            {
                if (!loadingCanvas.gameObject.activeSelf)
                {
                    loadingCanvas.gameObject.SetActive(true);
                    if (debugLogs)
                        Debug.Log("[LoadingManager] ✓ Enabled Canvas GameObject", this);
                }
                
                loadingCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                loadingCanvas.sortingOrder = canvasSortOrder;
                loadingCanvas.enabled = true;
                
                if (debugLogs)
                    Debug.Log($"[LoadingManager] ✓ Loading UI shown (sortOrder={canvasSortOrder})", this);
            }

            if (uiController != null)
            {
                uiController.UpdateProgress(0f);
                uiController.ShowContinuePrompt(false);
            }

            // STEP 1: Disable and destroy objects in scenes we're about to unload
            // This prevents issues with singletons and event listeners
            foreach (Scene sceneToUnload in _scenesToUnload)
            {
                if (!sceneToUnload.isLoaded)
                    continue;

                if (debugLogs)
                    Debug.Log($"[LoadingManager] Disabling objects in scene before unload: {sceneToUnload.name}", this);

                // Destroy player first to prevent singleton conflicts
                DestroyPlayerInScene(sceneToUnload);

                // Disable all root objects
                GameObject[] rootObjects = sceneToUnload.GetRootGameObjects();
                foreach (GameObject obj in rootObjects)
                {
                    obj.SetActive(false);
                }
            }

            // STEP 2: Unload old scenes
            List<AsyncOperation> unloadOperations = new List<AsyncOperation>();
            foreach (Scene sceneToUnload in _scenesToUnload)
            {
                if (!sceneToUnload.isLoaded)
                    continue;

                if (debugLogs)
                    Debug.Log($"[LoadingManager] ⚠ Unloading scene: {sceneToUnload.name}", this);

                AsyncOperation unloadOp = SceneManager.UnloadSceneAsync(sceneToUnload);
                if (unloadOp != null)
                    unloadOperations.Add(unloadOp);
            }

            // Wait for all unloads to complete
            while (unloadOperations.Count > 0)
            {
                unloadOperations.RemoveAll(op => op.isDone);
                yield return null;
            }

            if (debugLogs)
                Debug.Log("[LoadingManager] ✓ All old scenes unloaded", this);

            // STEP 3: Load new scenes (or track already-loaded ones)
            int[] sceneIndices = SceneTransitionHelper.GetScenesToLoad();
            
            // Track which scenes are already loaded (need reactivation, not loading)
            HashSet<int> alreadyLoadedScenes = new HashSet<int>();

            foreach (int buildIndex in sceneIndices)
            {
                if (!IsValidBuildIndex(buildIndex))
                {
                    Debug.LogError($"[LoadingManager] Invalid build index: {buildIndex}", this);
                    continue;
                }

                // Check if scene is already loaded (might happen if reloading same scene)
                Scene existingScene = SceneManager.GetSceneByBuildIndex(buildIndex);
                if (existingScene.isLoaded)
                {
                    if (debugLogs)
                        Debug.Log($"[LoadingManager] Scene {buildIndex} already loaded - will reactivate", this);
                    
                    alreadyLoadedScenes.Add(buildIndex);
                    continue;
                }

                if (debugLogs)
                    Debug.Log($"[LoadingManager] Loading scene index {buildIndex}", this);

                AsyncOperation op = SceneManager.LoadSceneAsync(buildIndex, LoadSceneMode.Additive);
                op.allowSceneActivation = true;
                _loadOperations.Add(op);
            }

            if (_loadOperations.Count == 0 && alreadyLoadedScenes.Count == 0)
            {
                Debug.LogError("[LoadingManager] No valid scenes to load!", this);
                HideLoadingUI();
                _isLoading = false;
                yield break;
            }

            // STEP 4: Wait for all scenes to load and configure them
            int firstSceneIndex = SceneTransitionHelper.GetFirstSceneToActivate();
            bool[] sceneConfigured = new bool[sceneIndices.Length];

            while (!AllScenesLoadedAndConfigured(sceneConfigured))
            {
                float totalProgress = CalculateTotalProgress();

                if (uiController != null)
                    uiController.UpdateProgress(totalProgress);

                // Configure scenes as they load (or reactivate already-loaded ones)
                for (int i = 0; i < sceneIndices.Length; i++)
                {
                    if (sceneConfigured[i])
                        continue;

                    Scene scene = SceneManager.GetSceneByBuildIndex(sceneIndices[i]);

                    if (!scene.isLoaded)
                        continue;

                    bool wasAlreadyLoaded = alreadyLoadedScenes.Contains(sceneIndices[i]);
                    
                    if (debugLogs)
                        Debug.Log($"[LoadingManager] 🔍 Scene loaded: {scene.name} (buildIndex={sceneIndices[i]}), i={i}, firstSceneIndex={firstSceneIndex}, isActive={i == firstSceneIndex}, wasAlreadyLoaded={wasAlreadyLoaded}");

                    if (i == firstSceneIndex)
                    {
                        // This is the main scene - activate it
                        // If it was already loaded (but deactivated), we need to reactivate its objects
                        if (wasAlreadyLoaded)
                        {
                            if (debugLogs)
                                Debug.Log($"[LoadingManager] Reactivating already-loaded scene: {scene.name}", this);
                            
                            ReactivateSceneObjects(scene);
                        }
                        
                        SceneManager.SetActiveScene(scene);

                        if (debugLogs)
                            Debug.Log($"[LoadingManager] ✓ Activated: {scene.name}", this);
                    }
                    else
                    {
                        // This scene should be hidden (e.g., cutscene waiting to play after level)
                        DeactivateSceneObjects(scene);
                        SceneTransitionHelper.RegisterInactiveScene(sceneIndices[i]);

                        if (debugLogs)
                            Debug.Log($"[LoadingManager] ✗ Deactivated: {scene.name}", this);
                    }

                    sceneConfigured[i] = true;
                }

                yield return null;
            }

            if (debugLogs)
                Debug.Log("[LoadingManager] All scenes loaded and configured.", this);

            // Ensure minimum display time
            float elapsed = Time.realtimeSinceStartup - _loadStartTime;
            float remaining = minimumDisplayTime - elapsed;

            if (remaining > 0f)
            {
                if (uiController != null)
                    uiController.UpdateProgress(1.0f);

                yield return new WaitForSecondsRealtime(remaining);
            }

            // Ready for user input
            _loadingComplete = true;
            _canContinue = true;

            if (uiController != null)
                uiController.ShowContinuePrompt(true);

            if (debugLogs)
                Debug.Log("[LoadingManager] Ready! Waiting for user input...", this);
        }

        private void OnUserContinue()
        {
            _canContinue = false;
            _isLoading = false;
            HideLoadingUI();

            if (debugLogs)
                Debug.Log("[LoadingManager] User continued. Loading UI hidden.", this);
        }

        /// <summary>
        /// Destroy the player character in a specific scene to prevent singleton conflicts.
        /// </summary>
        private void DestroyPlayerInScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            // Find all players by tag
            GameObject[] players = GameObject.FindGameObjectsWithTag("DrawingCube");
            
            foreach (GameObject player in players)
            {
                if (player != null && player.scene == scene)
                {
                    if (debugLogs)
                        Debug.Log($"[LoadingManager] 🗑️ Destroying player from scene: {scene.name}", this);
                    
                    Destroy(player);
                }
            }
        }

        private void HideLoadingUI()
        {
            if (loadingCanvas != null)
            {
                loadingCanvas.enabled = false;
                
                if (debugLogs)
                    Debug.Log("[LoadingManager] Loading UI hidden.", this);
            }
        }

        private bool AllScenesLoadedAndConfigured(bool[] configured)
        {
            foreach (bool c in configured)
            {
                if (!c) return false;
            }
            return true;
        }

        private float CalculateTotalProgress()
        {
            if (_loadOperations.Count == 0)
                return 1f;

            float sum = 0f;
            foreach (var op in _loadOperations)
            {
                sum += Mathf.Clamp01(op.progress);
            }

            return sum / _loadOperations.Count;
        }

        /// <summary>
        /// Reactivate all root GameObjects in a scene that was previously deactivated.
        /// Used when a scene is already loaded (from a previous LoadingManager call) but needs to become active.
        /// </summary>
        private void ReactivateSceneObjects(Scene scene)
        {
            GameObject[] allObjects = scene.GetRootGameObjects();

            // Enable cameras and audio listeners FIRST
            foreach (GameObject obj in allObjects)
            {
                UnityEngine.Camera[] cameras = obj.GetComponentsInChildren<UnityEngine.Camera>(true);
                foreach (UnityEngine.Camera cam in cameras)
                {
                    cam.enabled = true;
                    
                    if (debugLogs)
                        Debug.Log($"[LoadingManager] Re-enabled camera: {cam.name}", this);
                }

                AudioListener[] listeners = obj.GetComponentsInChildren<AudioListener>(true);
                foreach (AudioListener listener in listeners)
                {
                    listener.enabled = true;
                    
                    if (debugLogs)
                        Debug.Log($"[LoadingManager] Re-enabled audio listener: {listener.name}", this);
                }
            }

            // Activate all root objects
            foreach (GameObject obj in allObjects)
            {
                if (!obj.activeSelf)
                {
                    obj.SetActive(true);

                    if (debugLogs)
                        Debug.Log($"[LoadingManager]   + Reactivated: {obj.name}", this);
                }
            }
        }

        /// <summary>
        /// Deactivate all root GameObjects in a scene to hide it.
        /// </summary>
        private void DeactivateSceneObjects(Scene scene)
        {
            GameObject[] allObjects = scene.GetRootGameObjects();

            // Disable cameras first (stops rendering immediately)
            foreach (GameObject obj in allObjects)
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

            // Disable all root objects
            foreach (GameObject obj in allObjects)
            {
                if (obj.activeSelf)
                {
                    obj.SetActive(false);

                    if (debugLogs)
                        Debug.Log($"[LoadingManager]   - Deactivated: {obj.name}", this);
                }
            }
        }

        private static bool IsValidBuildIndex(int buildIndex)
        {
            return buildIndex >= 0 && buildIndex < SceneManager.sceneCountInBuildSettings;
        }
    }
}