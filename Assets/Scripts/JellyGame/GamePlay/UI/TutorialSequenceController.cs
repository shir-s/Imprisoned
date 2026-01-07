// FILEPATH: Assets/Scripts/UI/Tutorial/TutorialSequenceController.cs
using System.Collections.Generic;
using UnityEngine;

namespace JellyGame.UI.Tutorial
{
    /// <summary>
    /// Shows a sequence of tutorial "windows" (usually Canvas roots) one after another.
    /// While active, the game time is paused (Time.timeScale = 0).
    ///
    /// - Each window can be skipped only after a cooldown (uses unscaled time).
    /// - Default skip key is 'E' (configurable).
    /// - Windows are provided via Inspector and shown in order.
    ///
    /// Usage:
    /// - Create each tutorial window as a GameObject (Canvas root / panel).
    /// - Put them in "windows" list in order.
    /// - Optionally call StartTutorial() manually, or enable autoStart.
    /// </summary>
    [DisallowMultipleComponent]
    public class TutorialSequenceController : MonoBehaviour
    {
        [Header("Windows (in order)")]
        [Tooltip("GameObjects that represent tutorial windows (Canvas roots/panels). They will be activated/deactivated by this controller.")]
        [SerializeField] private List<GameObject> windows = new List<GameObject>();

        [Header("Pause")]
        [Tooltip("If true, pauses the game while any tutorial window is shown (Time.timeScale = 0).")]
        [SerializeField] private bool pauseGameWhileActive = true;

        [Tooltip("If true, restores the previous timeScale after the tutorial finishes.")]
        [SerializeField] private bool restorePreviousTimeScaleOnFinish = true;

        [Header("Skip")]
        [Tooltip("How many seconds the user must wait before they are allowed to skip the current window.")]
        [SerializeField] private float skipCooldownSeconds = 0.75f;

        [Tooltip("Key used to skip the current window.")]
        [SerializeField] private KeyCode skipKey = KeyCode.E;

        [Tooltip("If true, holding the key will only skip once per window (recommended).")]
        [SerializeField] private bool requireKeyDown = true;

        [Header("Flow")]
        [Tooltip("Start tutorial automatically on Start().")]
        [SerializeField] private bool autoStart = false;

        [Tooltip("If true, the tutorial ends automatically if windows list is empty.")]
        [SerializeField] private bool endImmediatelyIfNoWindows = true;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private int _currentIndex = -1;
        private float _canSkipAtUnscaledTime = 0f;
        private bool _isRunning = false;

        private float _prevTimeScale = 1f;
        private bool _prevTimeScaleCaptured = false;

        public bool IsRunning => _isRunning;
        public int CurrentIndex => _currentIndex;
        public int WindowCount => windows != null ? windows.Count : 0;

        private void Start()
        {
            // Make sure everything is hidden initially
            HideAllWindows();

            if (autoStart)
                StartTutorial();
        }

        private void Update()
        {
            if (!_isRunning)
                return;

            if (_currentIndex < 0 || _currentIndex >= WindowCount)
                return;

            // Allow skip only after cooldown (unscaled time so it works while paused)
            if (Time.unscaledTime < _canSkipAtUnscaledTime)
                return;

            bool pressed = requireKeyDown ? Input.GetKeyDown(skipKey) : Input.GetKey(skipKey);
            if (pressed)
                SkipCurrentWindow();
        }

        /// <summary>
        /// Start the tutorial sequence from the beginning.
        /// </summary>
        public void StartTutorial()
        {
            if (_isRunning)
                return;

            if (WindowCount == 0)
            {
                if (debugLogs)
                    Debug.Log("[Tutorial] StartTutorial called but windows list is empty.", this);

                if (endImmediatelyIfNoWindows)
                    FinishTutorial();

                return;
            }

            _isRunning = true;

            if (pauseGameWhileActive)
                PauseGame();

            ShowWindowAtIndex(0);

            if (debugLogs)
                Debug.Log("[Tutorial] Started.", this);
        }

        /// <summary>
        /// Skips the current window and shows the next one. If there is no next, finishes.
        /// </summary>
        public void SkipCurrentWindow()
        {
            if (!_isRunning)
                return;

            if (_currentIndex < 0 || _currentIndex >= WindowCount)
                return;

            HideWindowAtIndex(_currentIndex);

            int next = _currentIndex + 1;
            if (next >= WindowCount)
            {
                FinishTutorial();
                return;
            }

            ShowWindowAtIndex(next);
        }

        /// <summary>
        /// Immediately finish the tutorial and restore game time (if configured).
        /// </summary>
        public void FinishTutorial()
        {
            HideAllWindows();
            _isRunning = false;
            _currentIndex = -1;

            if (pauseGameWhileActive && restorePreviousTimeScaleOnFinish)
                ResumeGame();

            if (debugLogs)
                Debug.Log("[Tutorial] Finished.", this);
        }

        // -------------------- Internals --------------------

        private void ShowWindowAtIndex(int index)
        {
            if (windows == null || windows.Count == 0)
                return;

            index = Mathf.Clamp(index, 0, windows.Count - 1);

            _currentIndex = index;

            GameObject go = windows[_currentIndex];
            if (go != null)
                go.SetActive(true);

            _canSkipAtUnscaledTime = Time.unscaledTime + Mathf.Max(0f, skipCooldownSeconds);

            if (debugLogs)
                Debug.Log($"[Tutorial] Showing window {_currentIndex + 1}/{WindowCount}. CanSkipAt={_canSkipAtUnscaledTime:F2} (unscaled).", this);
        }

        private void HideWindowAtIndex(int index)
        {
            if (windows == null || windows.Count == 0)
                return;

            if (index < 0 || index >= windows.Count)
                return;

            GameObject go = windows[index];
            if (go != null)
                go.SetActive(false);

            if (debugLogs)
                Debug.Log($"[Tutorial] Hiding window {index + 1}/{WindowCount}.", this);
        }

        private void HideAllWindows()
        {
            if (windows == null)
                return;

            for (int i = 0; i < windows.Count; i++)
            {
                if (windows[i] != null)
                    windows[i].SetActive(false);
            }
        }

        private void PauseGame()
        {
            if (!_prevTimeScaleCaptured)
            {
                _prevTimeScale = Time.timeScale;
                _prevTimeScaleCaptured = true;
            }

            Time.timeScale = 0f;

            if (debugLogs)
                Debug.Log($"[Tutorial] Paused game. prevTimeScale={_prevTimeScale}", this);
        }

        private void ResumeGame()
        {
            // If we never captured it (shouldn't happen), default to 1
            float restore = _prevTimeScaleCaptured ? _prevTimeScale : 1f;

            Time.timeScale = restore;
            _prevTimeScaleCaptured = false;

            if (debugLogs)
                Debug.Log($"[Tutorial] Resumed game. timeScale={Time.timeScale}", this);
        }
    }
}
