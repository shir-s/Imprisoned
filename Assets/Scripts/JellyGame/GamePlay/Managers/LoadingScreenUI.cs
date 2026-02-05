// FILEPATH: Assets/Scripts/UI/LoadingScreenUI.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace JellyGame.GamePlay.Managers
{
    /// <summary>
    /// Controls the visual elements of the loading screen.
    /// Updates progress bar and text based on loading state.
    /// </summary>
    [DisallowMultipleComponent]
    public class LoadingScreenUI : MonoBehaviour
    {
        [Header("UI Elements")]
        [Tooltip("Progress bar slider (0-1 range).")]
        [SerializeField] private Slider progressBar;

        [Tooltip("Loading text (e.g., 'Loading...' or 'Press E to continue').")]
        [SerializeField] private TextMeshProUGUI loadingText;

        [Tooltip("Alternative: Unity UI Text component (if not using TextMeshPro).")]
        [SerializeField] private Text loadingTextLegacy;

        [Header("Text Messages")]
        [SerializeField] private string loadingMessage = "Loading...";
        [SerializeField] private string continueMessage = "Press E to continue";

        [Header("Continue Prompt")]
        [Tooltip("Optional: GameObject to show/hide for 'Press E' prompt (e.g., animated icon).")]
        [SerializeField] private GameObject continuePromptObject;

        [Header("Progress Display")]
        [Tooltip("Show percentage in text? (e.g., 'Loading... 45%')")]
        [SerializeField] private bool showPercentage = false;

        private void Start()
        {
            // Initialize UI
            UpdateProgress(0f);
            ShowContinuePrompt(false);
        }

        /// <summary>
        /// Update the progress bar and optionally the text.
        /// </summary>
        /// <param name="progress">Progress value 0-1.</param>
        public void UpdateProgress(float progress)
        {
            progress = Mathf.Clamp01(progress);

            if (progressBar != null)
                progressBar.value = progress;

            if (showPercentage)
            {
                string message = $"{loadingMessage} {Mathf.RoundToInt(progress * 100f)}%";
                SetText(message);
            }
            else
            {
                SetText(loadingMessage);
            }
        }

        /// <summary>
        /// Show or hide the "Press E to continue" prompt.
        /// </summary>
        public void ShowContinuePrompt(bool show)
        {
            if (show)
            {
                SetText(continueMessage);

                if (progressBar != null)
                    progressBar.value = 1f; // Show full bar
            }

            if (continuePromptObject != null)
                continuePromptObject.SetActive(show);
        }

        private void SetText(string message)
        {
            if (loadingText != null)
                loadingText.text = message;

            if (loadingTextLegacy != null)
                loadingTextLegacy.text = message;
        }
    }
}