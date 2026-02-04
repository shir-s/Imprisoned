// FILEPATH: Assets/Scripts/Managers/LoadingScreenInitializer.cs
using UnityEngine;
using UnityEngine.SceneManagement;

namespace JellyGame.GamePlay.Managers
{
    /// <summary>
    /// Loads the LoadingScreen scene additively at game start.
    /// 
    /// Place this script in your FIRST scene (Main Menu, Tutorial, or Starting Screen).
    /// It will load the LoadingScreen scene once and keep it loaded throughout the game.
    /// </summary>
    public class LoadingScreenInitializer : MonoBehaviour
    {
        [Header("Loading Screen Scene")]
        [Tooltip("Build index of the LoadingScreen scene.")]
        [SerializeField] private int loadingScreenBuildIndex = 8;

        [Header("Options")]
        [Tooltip("If true, only load if LoadingScreen scene isn't already loaded.")]
        [SerializeField] private bool checkIfAlreadyLoaded = true;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = true;

        private void Awake()
        {
            // Check if already loaded
            if (checkIfAlreadyLoaded && IsLoadingScreenAlreadyLoaded())
            {
                if (debugLogs)
                    Debug.Log("[LoadingScreenInitializer] LoadingScreen already loaded. Skipping.", this);
                return;
            }

            // Validate build index
            if (!IsValidBuildIndex(loadingScreenBuildIndex))
            {
                Debug.LogError($"[LoadingScreenInitializer] Invalid build index: {loadingScreenBuildIndex}. Check Build Settings!", this);
                return;
            }

            // Load additively
            if (debugLogs)
                Debug.Log($"[LoadingScreenInitializer] Loading LoadingScreen scene (index {loadingScreenBuildIndex}) additively...", this);

            SceneManager.LoadSceneAsync(loadingScreenBuildIndex, LoadSceneMode.Additive);
        }

        private bool IsLoadingScreenAlreadyLoaded()
        {
            // Check if a scene with this build index is already loaded
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.buildIndex == loadingScreenBuildIndex)
                    return true;
            }

            return false;
        }

        private static bool IsValidBuildIndex(int buildIndex)
        {
            return buildIndex >= 0 && buildIndex < SceneManager.sceneCountInBuildSettings;
        }
    }
}