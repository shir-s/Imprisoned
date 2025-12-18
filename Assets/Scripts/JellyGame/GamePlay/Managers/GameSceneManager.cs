// FILEPATH: Assets/Scripts/Managers/GameSceneManager.cs
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace JellyGame.GamePlay.Managers
{
    /// <summary>
    /// Manages scene transitions between Game, GameOver, and Win scenes.
    /// Also listens to EntityDied and triggers GameOver if the victim layer matches.
    /// Win is triggered by listening to GameWin or via a cheat key.
    /// </summary>
    public class GameSceneManager : MonoBehaviour
    {
        [Header("Scene Settings (Use Build Index)")]
        [Tooltip("Build index of the game scene. Check Build Settings to see the index number.")]
        [SerializeField] private int gameSceneBuildIndex = 0;

        [Tooltip("Build index of the game over scene. Check Build Settings to see the index number.")]
        [SerializeField] private int gameOverSceneBuildIndex = 1;

        [Tooltip("Build index of the win scene. Check Build Settings to see the index number.")]
        [SerializeField] private int winSceneBuildIndex = 2;

        [Header("Scene Names (Fallback)")]
        [Tooltip("Name of the game scene (used as fallback if build index fails)")]
        [SerializeField] private string gameSceneName = "JellyWithArt";

        [Tooltip("Name of the game over scene (used as fallback if build index fails)")]
        [SerializeField] private string gameOverSceneName = "GameOver";

        [Tooltip("Name of the win scene (used as fallback if build index fails)")]
        [SerializeField] private string winSceneName = "Win";

        [Header("GameOver Trigger")]
        [Tooltip("If an EntityDied event is triggered with a victim layer in this mask => trigger GameOver.")]
        [SerializeField] private LayerMask gameOverOnVictimLayers;

        [Header("Win Trigger")]
        [Tooltip("If true, listens to EventManager.GameEvent.GameWin.")]
        [SerializeField] private bool listenToWinEvent = true;

        [Header("Win FX (optional)")]
        [Tooltip("If assigned, will Play() this particle system before loading the Win scene.")]
        [SerializeField] private ParticleSystem winParticles;

        [Tooltip("Delay (seconds) after win is triggered before loading the Win scene.\n" +
                 "If winParticles is assigned and this is <= 0, we will use its main.duration.")]
        [SerializeField] private float winSceneLoadDelay = 0f;

        [Tooltip("If true, pause gameplay during win (sets Time.timeScale=0 AFTER particles are started).\n" +
                 "Note: ParticleSystem uses scaled time by default, so if you pause, make sure your particles use unscaled time if needed.")]
        [SerializeField] private bool pauseGameplayOnWin = false;

        [Header("Cheat Codes")]
        [Tooltip("Key to press to trigger GameOver (cheat code)")]
        [SerializeField] private KeyCode gameOverKey = KeyCode.R;

        [Tooltip("Key to press to trigger Win (cheat code)")]
        [SerializeField] private KeyCode winKey = KeyCode.T;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = true;

        private bool _winSequenceRunning;

        private void OnEnable()
        {
            EventManager.StartListening(EventManager.GameEvent.EntityDied, OnEntityDiedEvent);
            EventManager.StartListening(EventManager.GameEvent.GameOver, OnGameOverEvent);

            if (listenToWinEvent)
                EventManager.StartListening(EventManager.GameEvent.GameWin, OnWinEvent);

            if (debugLogs)
                Debug.Log($"[GameSceneManager] Enabled in scene: {SceneManager.GetActiveScene().name}", this);
        }

        private void OnDisable()
        {
            EventManager.StopListening(EventManager.GameEvent.EntityDied, OnEntityDiedEvent);
            EventManager.StopListening(EventManager.GameEvent.GameOver, OnGameOverEvent);

            if (listenToWinEvent)
                EventManager.StopListening(EventManager.GameEvent.GameWin, OnWinEvent);
        }

        private void Update()
        {
            // Cheat code: trigger GameOver
            if (Input.GetKeyDown(gameOverKey))
            {
                if (debugLogs)
                    Debug.Log($"[GameSceneManager] Cheat code pressed ({gameOverKey})! Triggering GameOver", this);

                EventManager.TriggerEvent(EventManager.GameEvent.GameOver);
            }

            // Cheat code: trigger Win
            if (Input.GetKeyDown(winKey))
            {
                if (debugLogs)
                    Debug.Log($"[GameSceneManager] Cheat code pressed ({winKey})! Triggering Win", this);

                EventManager.TriggerEvent(EventManager.GameEvent.GameWin);
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

        private void OnWinEvent(object _)
        {
            if (_winSequenceRunning)
                return;

            _winSequenceRunning = true;
            StartCoroutine(WinSequence());
        }

        private IEnumerator WinSequence()
        {
            if (debugLogs)
                Debug.Log("[GameSceneManager] Win sequence started.", this);

            // Play particles first (optional)
            float delay = winSceneLoadDelay;

            if (winParticles != null)
            {
                // Ensure particles are visible even if they were stopped
                winParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                winParticles.Play(true);

                if (delay <= 0f)
                    delay = Mathf.Max(0f, winParticles.main.duration);
            }

            if (pauseGameplayOnWin)
            {
                // WARNING: default particles are scaled-time. If you pause, they may freeze unless set to unscaled.
                Time.timeScale = 0f;
            }

            if (delay > 0f)
            {
                // If gameplay is paused, WaitForSeconds would also pause. Use realtime wait in that case.
                if (pauseGameplayOnWin)
                    yield return new WaitForSecondsRealtime(delay);
                else
                    yield return new WaitForSeconds(delay);
            }

            // If we paused gameplay, restore time before switching scene (optional but sane).
            if (pauseGameplayOnWin)
                Time.timeScale = 1f;

            LoadWinScene();
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
        /// Loads the Win scene
        /// </summary>
        public void LoadWinScene()
        {
            if (winSceneBuildIndex >= 0 && winSceneBuildIndex < SceneManager.sceneCountInBuildSettings)
            {
                if (debugLogs)
                    Debug.Log($"[GameSceneManager] Loading Win scene by index: {winSceneBuildIndex}", this);
                SceneManager.LoadScene(winSceneBuildIndex);
                return;
            }

            if (!string.IsNullOrEmpty(winSceneName))
            {
                if (debugLogs)
                    Debug.Log($"[GameSceneManager] Loading Win scene by name: {winSceneName}", this);
                SceneManager.LoadScene(winSceneName);
                return;
            }

            Debug.LogError("[GameSceneManager] Win scene build index and name are not set correctly!", this);
        }

        /// <summary>
        /// Restarts the game by loading the game scene
        /// Call this from the restart button in GameOver/Win scenes
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
