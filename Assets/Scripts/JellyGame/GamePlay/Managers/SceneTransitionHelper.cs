// FILEPATH: Assets/Scripts/Managers/SceneTransitionHelper.cs
using System.Collections.Generic;
using UnityEngine;

namespace JellyGame.GamePlay.Managers
{
    /// <summary>
    /// Static helper to pass scene loading information between scenes.
    /// Used by triggers to tell LoadingScreen which scenes to load.
    /// </summary>
    public static class SceneTransitionHelper
    {
        private static int[] _scenesToLoad = null;
        private static int _firstSceneToActivate = 0;
        private static bool _hasData = false;

        // Track which scenes were loaded but deactivated
        private static List<int> _inactiveSceneIndices = new List<int>();

        /// <summary>
        /// Configure which scenes the LoadingScreen should load.
        /// </summary>
        /// <param name="sceneIndices">Array of build indices to load. Can be 1 or 2 scenes.</param>
        /// <param name="firstSceneIndex">Which scene to activate first (default 0 = first in array).</param>
        public static void SetScenesToLoad(int[] sceneIndices, int firstSceneIndex = 0)
        {
            if (sceneIndices == null || sceneIndices.Length == 0)
            {
                Debug.LogError("[SceneTransitionHelper] Cannot set null or empty scene array!");
                return;
            }

            _scenesToLoad = sceneIndices;
            _firstSceneToActivate = Mathf.Clamp(firstSceneIndex, 0, sceneIndices.Length - 1);
            _hasData = true;

            Debug.Log($"[SceneTransitionHelper] Configured loading: {sceneIndices.Length} scene(s), activate index {_firstSceneToActivate}");
        }

        /// <summary>
        /// Get the scenes to load (called by LoadingManager).
        /// </summary>
        public static int[] GetScenesToLoad()
        {
            return _scenesToLoad;
        }

        /// <summary>
        /// Get which scene should be activated first.
        /// </summary>
        public static int GetFirstSceneToActivate()
        {
            return _firstSceneToActivate;
        }

        /// <summary>
        /// Check if we have valid data.
        /// </summary>
        public static bool HasData()
        {
            return _hasData && _scenesToLoad != null && _scenesToLoad.Length > 0;
        }

        /// <summary>
        /// Register that a scene was loaded but is currently inactive (objects disabled).
        /// Called by LoadingManager.
        /// </summary>
        public static void RegisterInactiveScene(int buildIndex)
        {
            if (!_inactiveSceneIndices.Contains(buildIndex))
            {
                _inactiveSceneIndices.Add(buildIndex);
            }
        }

        /// <summary>
        /// Check if a scene is loaded but inactive (objects disabled).
        /// Used by CutsceneSceneTransition to know if it should reactivate objects.
        /// </summary>
        public static bool IsSceneInactive(int buildIndex)
        {
            return _inactiveSceneIndices.Contains(buildIndex);
        }

        /// <summary>
        /// Clear data after LoadingScreen uses it.
        /// </summary>
        public static void Clear()
        {
            _scenesToLoad = null;
            _firstSceneToActivate = 0;
            _hasData = false;
            _inactiveSceneIndices.Clear();
        }

        /// <summary>
        /// Convenience method: Load LoadingScreen scene with the specified scenes to preload.
        /// </summary>
        /// <param name="loadingScreenBuildIndex">Build index of your LoadingScreen scene.</param>
        /// <param name="scenesToLoad">Scenes to preload (1 or 2).</param>
        /// <param name="firstSceneIndex">Which scene to activate first.</param>
        public static void LoadWithLoadingScreen(int loadingScreenBuildIndex, int[] scenesToLoad, int firstSceneIndex = 0)
        {
            SetScenesToLoad(scenesToLoad, firstSceneIndex);
            UnityEngine.SceneManagement.SceneManager.LoadScene(loadingScreenBuildIndex);
        }
    }
}