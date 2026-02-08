// FILEPATH: Assets/Scripts/UI/LoadingScreenUI.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace JellyGame.GamePlay.Managers
{
    /// <summary>
    /// Controls the visual elements of the loading screen.
    /// 
    /// Supports two progress display modes:
    /// 1. Slider (classic horizontal bar)
    /// 2. Fill Image (e.g. a slime outline that fills from bottom to top)
    /// 
    /// For the slime fill effect:
    /// - Create an Image with the filled slime sprite
    /// - Set Image Type = Filled, Fill Method = Vertical, Fill Origin = Bottom
    /// - Assign it to fillImage below
    /// - Place the slime outline image on top (separate Image, not filled)
    /// </summary>
    [DisallowMultipleComponent]
    public class LoadingScreenUI : MonoBehaviour
    {
        [Header("Progress Display Mode")]
        [Tooltip("Which UI element to use for showing progress.")]
        [SerializeField] private ProgressMode progressMode = ProgressMode.FillImage;

        public enum ProgressMode
        {
            Slider,
            FillImage
        }

        [Header("Slider (if progressMode = Slider)")]
        [Tooltip("Progress bar slider (0-1 range).")]
        [SerializeField] private Slider progressBar;

        [Header("Fill Image (if progressMode = FillImage)")]
        [Tooltip("Image that fills vertically to show progress.\n" +
                 "Setup: Image Type = Filled, Fill Method = Vertical, Fill Origin = Bottom.")]
        [SerializeField] private Image fillImage;

        [Header("Text")]
        [Tooltip("Loading text (TextMeshPro).")]
        [SerializeField] private TextMeshProUGUI loadingText;

        [Tooltip("Alternative: Unity UI Text component.")]
        [SerializeField] private Text loadingTextLegacy;

        [Header("Text Messages")]
        [SerializeField] private string loadingMessage = "Loading...";
        [SerializeField] private string continueMessage = "Press E to continue";

        [Header("Continue Prompt")]
        [Tooltip("Optional: GameObject to show/hide for 'Press E' prompt.")]
        [SerializeField] private GameObject continuePromptObject;

        [Header("Additional Objects")]
        [Tooltip("GameObjects to activate when loading starts and deactivate when loading ends.\n" +
                 "Drag in: background image, slime outline, any decorations, etc.")]
        [SerializeField] private List<GameObject> objectsToShowDuringLoading = new List<GameObject>();

        [Header("Progress Display")]
        [Tooltip("Show percentage in text? (e.g., 'Loading... 45%')")]
        [SerializeField] private bool showPercentage = false;

        [Header("Smooth Fill")]
        [Tooltip("Smoothly animate the fill instead of jumping to target value.")]
        [SerializeField] private bool smoothFill = true;

        [Tooltip("How fast the fill catches up to the target (higher = faster).")]
        [SerializeField] private float smoothSpeed = 3f;

        private float _targetProgress = 0f;
        private float _displayedProgress = 0f;

        private void Start()
        {
            UpdateProgress(0f);
            ShowContinuePrompt(false);
        }

        private void Update()
        {
            if (!smoothFill)
                return;

            // Smoothly interpolate towards target
            if (Mathf.Abs(_displayedProgress - _targetProgress) > 0.001f)
            {
                _displayedProgress = Mathf.MoveTowards(_displayedProgress, _targetProgress, smoothSpeed * Time.unscaledDeltaTime);
                ApplyProgress(_displayedProgress);
            }
        }

        /// <summary>
        /// Update the progress (0-1). If smoothFill is enabled, it animates towards this value.
        /// </summary>
        public void UpdateProgress(float progress)
        {
            progress = Mathf.Clamp01(progress);
            _targetProgress = progress;

            if (!smoothFill)
            {
                _displayedProgress = progress;
                ApplyProgress(progress);
            }

            // Update text
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

                // Snap to full
                _targetProgress = 1f;
                _displayedProgress = 1f;
                ApplyProgress(1f);
            }

            if (continuePromptObject != null)
                continuePromptObject.SetActive(show);
        }

        /// <summary>
        /// Apply the progress value to the active UI element.
        /// </summary>
        private void ApplyProgress(float value)
        {
            switch (progressMode)
            {
                case ProgressMode.Slider:
                    if (progressBar != null)
                        progressBar.value = value;
                    break;

                case ProgressMode.FillImage:
                    if (fillImage != null)
                        fillImage.fillAmount = value;
                    break;
            }
        }

        /// <summary>
        /// Show or hide all loading screen elements.
        /// Called by LoadingManager when showing/hiding the loading UI.
        /// </summary>
        public void SetVisible(bool visible)
        {
            foreach (var obj in objectsToShowDuringLoading)
            {
                if (obj != null)
                    obj.SetActive(visible);
            }
        }

        /// <summary>
        /// Reset progress to 0 instantly (no smooth). Called when loading screen is shown.
        /// </summary>
        public void ResetProgress()
        {
            _targetProgress = 0f;
            _displayedProgress = 0f;
            ApplyProgress(0f);
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