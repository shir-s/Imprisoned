using UnityEngine;
using UnityEngine.SceneManagement;
using JellyGame.GamePlay.Utils;

namespace JellyGame.GamePlay.Managers
{
    /// <summary>
    /// Manages scene transitions between Game and GameOver scenes
    /// </summary>
    public class GameSceneManager : MonoBehaviour
    {
        [Header("Scene Settings (Use Build Index)")]
        [Tooltip("Build index of the game scene. Check Build Settings to see the index number.")]
        [SerializeField] private int gameSceneBuildIndex = 0;
        
        [Tooltip("Build index of the game over scene. Check Build Settings to see the index number.")]
        [SerializeField] private int gameOverSceneBuildIndex = 1;

        [Header("Scene Names (Fallback)")]
        [Tooltip("Name of the game scene (used as fallback if build index fails)")]
        [SerializeField] private string gameSceneName = "JellyWithArt";
        
        [Tooltip("Name of the game over scene (used as fallback if build index fails)")]
        [SerializeField] private string gameOverSceneName = "GameOver";

        [Header("Cheat Code")]
        [Tooltip("Key to press to trigger GameOver (cheat code)")]
        [SerializeField] private KeyCode gameOverKey = KeyCode.R;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = true; // Set to true to see what's happening

        private void OnEnable()
        {
            // Subscribe to GameOver event
            JellyGameEvents.GameOver += OnGameOver;
            
            if (debugLogs)
                Debug.Log($"[GameSceneManager] Enabled in scene: {SceneManager.GetActiveScene().name}", this);
        }

        private void OnDisable()
        {
            // Unsubscribe from GameOver event
            JellyGameEvents.GameOver -= OnGameOver;
        }

        private void Update()
        {
            // Cheat code: Press R to trigger GameOver (only in game scene)
            if (Input.GetKeyDown(gameOverKey))
            {
                if (debugLogs)
                    Debug.Log($"[GameSceneManager] Cheat code pressed ({gameOverKey})! Triggering GameOver", this);
                
                // Trigger the GameOver event (which will call OnGameOver and switch scenes)
                JellyGameEvents.GameOver?.Invoke();
            }
        }

        /// <summary>
        /// Called when GameOver event is triggered - switches to GameOver scene
        /// </summary>
        private void OnGameOver()
        {
            if (debugLogs)
                Debug.Log($"[GameSceneManager] GameOver triggered! Loading scene index: {gameOverSceneBuildIndex}", this);
            
            LoadGameOverScene();
        }

        /// <summary>
        /// Loads the GameOver scene
        /// </summary>
        public void LoadGameOverScene()
        {
            // Try build index first (more reliable)
            if (gameOverSceneBuildIndex >= 0 && gameOverSceneBuildIndex < SceneManager.sceneCountInBuildSettings)
            {
                if (debugLogs)
                    Debug.Log($"[GameSceneManager] Loading GameOver scene by index: {gameOverSceneBuildIndex}", this);
                SceneManager.LoadScene(gameOverSceneBuildIndex);
                return;
            }

            // Fallback to scene name
            if (!string.IsNullOrEmpty(gameOverSceneName))
            {
                if (debugLogs)
                    Debug.Log($"[GameSceneManager] Loading GameOver scene by name: {gameOverSceneName}", this);
                SceneManager.LoadScene(gameOverSceneName);
                return;
            }

            Debug.LogError("[GameSceneManager] GameOver scene build index and name are not set correctly!", this);
        }

        /// <summary>
        /// Restarts the game by loading the game scene
        /// Call this from the restart button in GameOver scene
        /// </summary>
        public void RestartGame()
        {
            if (debugLogs)
                Debug.Log($"[GameSceneManager] RestartGame() called! Loading game scene index: {gameSceneBuildIndex}", this);
            
            // Try build index first (more reliable)
            if (gameSceneBuildIndex >= 0 && gameSceneBuildIndex < SceneManager.sceneCountInBuildSettings)
            {
                if (debugLogs)
                    Debug.Log($"[GameSceneManager] Loading game scene by index: {gameSceneBuildIndex}", this);
                SceneManager.LoadScene(gameSceneBuildIndex);
                return;
            }

            // Fallback to scene name
            if (!string.IsNullOrEmpty(gameSceneName))
            {
                if (debugLogs)
                    Debug.Log($"[GameSceneManager] Loading game scene by name: {gameSceneName}", this);
                SceneManager.LoadScene(gameSceneName);
                return;
            }

            Debug.LogError("[GameSceneManager] Game scene build index and name are not set correctly!", this);
        }
    }
}