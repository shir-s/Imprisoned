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
    /// Setup:
    /// - LoadingScreen scene should be loaded additively at game start
    /// - Canvas is hidden by default
    /// - When loading is needed, canvas shows, scenes load, user presses E, canvas hides
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
        
        // Track the scene we're leaving (to unload it)
        private Scene _previousScene;
        private bool _previousSceneUnloaded = false;

        // Singleton instance
        private static LoadingManager _instance;
        public static LoadingManager Instance => _instance;

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
                // Canvas is a root GameObject, make it persistent too
                DontDestroyOnLoad(loadingCanvas.gameObject);
                
                if (debugLogs)
                    Debug.Log("[LoadingManager] Canvas is root object - applied DontDestroyOnLoad to it too.", this);
            }

            // Hide UI by default
            if (loadingCanvas != null)
                loadingCanvas.enabled = false;

            // IMPORTANT: Disable any cameras in the LoadingScreen scene
            // so they don't interfere with the active scene's camera
            DisableAllCamerasInScene();

            // Also disable any audio listeners
            DisableAllAudioListenersInScene();
        }

        /// <summary>
        /// Disable all cameras in the LoadingScreen scene to prevent interference.
        /// </summary>
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

        /// <summary>
        /// Disable all audio listeners in the LoadingScreen scene to prevent conflicts.
        /// </summary>
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

            // Check for input
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
        /// Start loading scenes. Called by GameSceneManager or other triggers.
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

            // Store the current active scene so we can unload it later
            _previousScene = SceneManager.GetActiveScene();
            _previousSceneUnloaded = false; // Reset flag
            
            if (debugLogs)
                Debug.Log($"[LoadingManager] Stored previous scene for unloading: {_previousScene.name}", this);

            SceneTransitionHelper.SetScenesToLoad(sceneIndices, firstSceneToActivate);
            StartCoroutine(LoadScenesCoroutine());
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
                // CRITICAL: Enable the GameObject first (in case it was disabled)
                if (!loadingCanvas.gameObject.activeSelf)
                {
                    loadingCanvas.gameObject.SetActive(true);
                    
                    if (debugLogs)
                        Debug.Log("[LoadingManager] ✓ Enabled Canvas GameObject", this);
                }
                
                // Ensure canvas is in overlay mode (renders on top of everything)
                loadingCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                
                // Set high sort order to be on top
                loadingCanvas.sortingOrder = canvasSortOrder;
                
                // Enable the canvas component
                loadingCanvas.enabled = true;
                
                if (debugLogs)
                    Debug.Log($"[LoadingManager] ✓ Loading UI shown (sortOrder={canvasSortOrder})", this);
            }
            else
            {
                Debug.LogWarning("[LoadingManager] loadingCanvas is null! Cannot show loading UI.", this);
            }

            if (uiController != null)
            {
                uiController.UpdateProgress(0f);
                uiController.ShowContinuePrompt(false);
            }

            // CRITICAL: Destroy the player in the previous scene FIRST
            // This prevents singleton conflicts when new scene's player loads
            DestroyPlayerInPreviousScene();

            int[] sceneIndices = SceneTransitionHelper.GetScenesToLoad();

            // Start loading all scenes additively
            foreach (int buildIndex in sceneIndices)
            {
                if (!IsValidBuildIndex(buildIndex))
                {
                    Debug.LogError($"[LoadingManager] Invalid build index: {buildIndex}", this);
                    continue;
                }

                if (debugLogs)
                    Debug.Log($"[LoadingManager] Loading scene index {buildIndex}", this);

                AsyncOperation op = SceneManager.LoadSceneAsync(buildIndex, LoadSceneMode.Additive);
                op.allowSceneActivation = true; // Let scenes load fully
                _loadOperations.Add(op);
            }

            if (_loadOperations.Count == 0)
            {
                Debug.LogError("[LoadingManager] No valid scenes to load!", this);
                HideLoadingUI();
                _isLoading = false;
                yield break;
            }

            // Monitor loading and configure scenes as they load
            int firstSceneIndex = SceneTransitionHelper.GetFirstSceneToActivate();
            bool[] sceneConfigured = new bool[sceneIndices.Length];

            while (!AllScenesLoadedAndConfigured(sceneConfigured))
            {
                // Update progress
                float totalProgress = CalculateTotalProgress();

                if (uiController != null)
                    uiController.UpdateProgress(totalProgress);

                if (debugLogs && Time.frameCount % 30 == 0)
                    Debug.Log($"[LoadingManager] Progress: {totalProgress * 100f:F1}%", this);

                // Configure scenes as they load
                for (int i = 0; i < sceneIndices.Length; i++)
                {
                    if (sceneConfigured[i])
                        continue;

                    Scene scene = SceneManager.GetSceneByBuildIndex(sceneIndices[i]);

                    if (!scene.isLoaded)
                        continue;

                    Debug.Log($"[LoadingManager] 🔍 Scene loaded: {scene.name} (buildIndex={sceneIndices[i]}), i={i}, firstSceneIndex={firstSceneIndex}, isActive={i == firstSceneIndex}");
                    
                    // Scene just loaded!
                    if (i == firstSceneIndex)
                    {
                        // CRITICAL: Disable previous scene's objects BEFORE setting new scene active
                        // This prevents singleton conflicts during the brief moment before unload completes
                        if (!_previousSceneUnloaded && _previousScene.IsValid() && _previousScene.isLoaded && _previousScene != gameObject.scene)
                        {
                            if (debugLogs)
                                Debug.Log($"[LoadingManager] Disabling objects in previous scene: {_previousScene.name}", this);
                            
                            // Disable all root GameObjects in the previous scene
                            GameObject[] previousSceneObjects = _previousScene.GetRootGameObjects();
                            foreach (GameObject obj in previousSceneObjects)
                            {
                                obj.SetActive(false);
                            }
                        }
                        
                        // Now set new scene as active (previous scene objects already disabled)
                        SceneManager.SetActiveScene(scene);

                        if (debugLogs)
                            Debug.Log($"[LoadingManager] ✓ Activated: {scene.name}", this);

                        // Unload the previous scene (now safe - objects already disabled)
                        if (!_previousSceneUnloaded && _previousScene.IsValid() && _previousScene.isLoaded && _previousScene != gameObject.scene)
                        {
                            if (debugLogs)
                                Debug.Log($"[LoadingManager] ⚠ Unloading previous scene: {_previousScene.name}", this);

                            SceneManager.UnloadSceneAsync(_previousScene);
                            _previousSceneUnloaded = true;
                        }
                    }
                    else
                    {
                        // This scene should be hidden
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

            // Hide loading UI
            HideLoadingUI();

            // Previous scene already unloaded at the start of loading

            if (debugLogs)
                Debug.Log("[LoadingManager] User continued. Loading UI hidden.", this);
        }

        /// <summary>
        /// Destroy the player character in the previous scene to prevent singleton conflicts.
        /// Called BEFORE loading new scenes.
        /// </summary>
        private void DestroyPlayerInPreviousScene()
        {
            if (!_previousScene.IsValid() || !_previousScene.isLoaded)
            {
                if (debugLogs)
                    Debug.Log("[LoadingManager] No previous scene to clean up player from.", this);
                return;
            }

            if (_previousScene == gameObject.scene)
            {
                if (debugLogs)
                    Debug.Log("[LoadingManager] Previous scene is LoadingScreen - no player to destroy.", this);
                return;
            }

            // Find player by tag
            GameObject player = GameObject.FindGameObjectWithTag("DrawingCube");
            
            if (player != null && player.scene == _previousScene)
            {
                if (debugLogs)
                    Debug.Log($"[LoadingManager] 🗑️ Destroying player from previous scene: {_previousScene.name}", this);
                
                Destroy(player);
            }
            else if (debugLogs)
            {
                Debug.Log($"[LoadingManager] No player found in previous scene {_previousScene.name} (or already destroyed).", this);
            }
        }
        /// Don't unload LoadingScreen or special scenes.
        /// This is called BEFORE loading new scenes to prevent singleton conflicts.
        /// </summary>
        private IEnumerator UnloadPreviousSceneAsync()
        {
            Debug.Log($"[LoadingManager] === UnloadPreviousSceneAsync START ===", this);
            Debug.Log($"[LoadingManager] _previousScene.IsValid() = {_previousScene.IsValid()}", this);
            Debug.Log($"[LoadingManager] _previousScene.isLoaded = {_previousScene.isLoaded}", this);
            
            if (_previousScene.IsValid())
            {
                Debug.Log($"[LoadingManager] _previousScene.name = {_previousScene.name}", this);
                Debug.Log($"[LoadingManager] _previousScene.buildIndex = {_previousScene.buildIndex}", this);
            }
            
            if (!_previousScene.IsValid() || !_previousScene.isLoaded)
            {
                Debug.LogWarning("[LoadingManager] No valid previous scene to unload.", this);
                yield break;
            }

            // Don't unload the LoadingScreen scene itself
            Scene loadingScene = gameObject.scene;
            Debug.Log($"[LoadingManager] LoadingScreen scene name = {loadingScene.name}", this);
            Debug.Log($"[LoadingManager] LoadingScreen scene buildIndex = {loadingScene.buildIndex}", this);
            
            if (_previousScene == loadingScene)
            {
                Debug.LogWarning("[LoadingManager] Previous scene is LoadingScreen - not unloading.", this);
                yield break;
            }

            Debug.Log($"[LoadingManager] ⚠⚠⚠ STARTING UNLOAD of scene: {_previousScene.name} (index {_previousScene.buildIndex})", this);

            // Unload asynchronously
            AsyncOperation unloadOp = SceneManager.UnloadSceneAsync(_previousScene);
            
            if (unloadOp == null)
            {
                Debug.LogError("[LoadingManager] UnloadSceneAsync returned NULL!", this);
                yield break;
            }
            
            Debug.Log("[LoadingManager] UnloadSceneAsync operation started...", this);

            // Wait for unload to complete
            float timeout = 10f; // 10 second timeout
            float elapsed = 0f;
            
            while (!unloadOp.isDone)
            {
                elapsed += Time.unscaledDeltaTime;
                
                if (elapsed > timeout)
                {
                    Debug.LogError($"[LoadingManager] Unload TIMEOUT after {timeout}s! Progress: {unloadOp.progress}", this);
                    yield break;
                }
                
                if (debugLogs && Time.frameCount % 30 == 0)
                {
                    Debug.Log($"[LoadingManager] Unloading... progress: {unloadOp.progress}, isDone: {unloadOp.isDone}", this);
                }
                
                yield return null;
            }

            Debug.Log($"[LoadingManager] ✓✓✓ UNLOAD COMPLETE! Scene {_previousScene.name} unloaded successfully!", this);
            Debug.Log($"[LoadingManager] === UnloadPreviousSceneAsync END ===", this);
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
                return 0f;

            float sum = 0f;
            foreach (var op in _loadOperations)
            {
                sum += Mathf.Clamp01(op.progress);
            }

            return sum / _loadOperations.Count;
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