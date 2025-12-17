// FILEPATH: Assets/Scripts/Managers/GameSceneManager.cs
using UnityEngine;
using UnityEngine.SceneManagement;
using JellyGame.GamePlay.Managers;

namespace JellyGame.GamePlay.Managers
{
    /// <summary>
    /// Manages scene transitions between Game and GameOver scenes.
    /// Also listens to EntityDied and triggers GameOver if the victim layer matches.
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

        [Header("GameOver Trigger")]
        [Tooltip("If an EntityDied event is triggered with a victim layer in this mask => trigger GameOver.")]
        [SerializeField] private LayerMask gameOverOnVictimLayers;

        [Header("Cheat Code")]
        [Tooltip("Key to press to trigger GameOver (cheat code)")]
        [SerializeField] private KeyCode gameOverKey = KeyCode.R;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = true;

        private void OnEnable()
        {
            EventManager.StartListening(EventManager.GameEvent.EntityDied, OnEntityDiedEvent);
            EventManager.StartListening(EventManager.GameEvent.GameOver, OnGameOverEvent);

            if (debugLogs)
                Debug.Log($"[GameSceneManager] Enabled in scene: {SceneManager.GetActiveScene().name}", this);
        }

        private void OnDisable()
        {
            EventManager.StopListening(EventManager.GameEvent.EntityDied, OnEntityDiedEvent);
            EventManager.StopListening(EventManager.GameEvent.GameOver, OnGameOverEvent);
        }

        private void Update()
        {
            // Cheat code: Press R to trigger GameOver
            if (Input.GetKeyDown(gameOverKey))
            {
                if (debugLogs)
                    Debug.Log($"[GameSceneManager] Cheat code pressed ({gameOverKey})! Triggering GameOver", this);

                EventManager.TriggerEvent(EventManager.GameEvent.GameOver);
            }
        }

        private void OnEntityDiedEvent(object eventData)
        {
            if (eventData is not EntityDiedEventData died)
            {
                if (debugLogs)
                    Debug.LogWarning("[GameSceneManager] EntityDied received with unexpected payload type.", this);
                return;
            }

            // layer mask match
            bool match = (gameOverOnVictimLayers.value & (1 << died.VictimLayer)) != 0;
            if (!match)
                return;

            if (debugLogs)
                Debug.Log($"[GameSceneManager] EntityDied matched layer {died.VictimLayer} ({LayerMask.LayerToName(died.VictimLayer)}). Triggering GameOver.", this);

            EventManager.TriggerEvent(EventManager.GameEvent.GameOver);
        }

        private void OnGameOverEvent(object _)
        {
            if (debugLogs)
                Debug.Log($"[GameSceneManager] GameOver event received! Loading scene index: {gameOverSceneBuildIndex}", this);

            LoadGameOverScene();
        }

        /// <summary>
        /// Loads the GameOver scene
        /// </summary>
        public void LoadGameOverScene()
        {
            if (gameOverSceneBuildIndex >= 0 && gameOverSceneBuildIndex < SceneManager.sceneCountInBuildSettings)
            {
                if (debugLogs)
                    Debug.Log($"[GameSceneManager] Loading GameOver scene by index: {gameOverSceneBuildIndex}", this);
                SceneManager.LoadScene(gameOverSceneBuildIndex);
                return;
            }

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

            if (gameSceneBuildIndex >= 0 && gameSceneBuildIndex < SceneManager.sceneCountInBuildSettings)
            {
                if (debugLogs)
                    Debug.Log($"[GameSceneManager] Loading game scene by index: {gameSceneBuildIndex}", this);
                SceneManager.LoadScene(gameSceneBuildIndex);
                return;
            }

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
