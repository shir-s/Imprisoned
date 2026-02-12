// FILEPATH: Assets/Scripts/JellyGame/UI/IntroTextSequence.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace JellyGame.UI
{
    /// <summary>
    /// Simple intro text sequence for levels.
    /// 
    /// On Start:
    /// 1. Freezes time (timeScale = 0)
    /// 2. Activates the first GameObject from the list (deactivates all others)
    /// 3. Waits for player input to advance to the next one
    /// 4. When all are shown and skipped, hides everything, restores time, game begins
    /// 
    /// Each entry in the list is a GameObject (e.g. a UI panel with text/images).
    /// Only one is active at a time.
    /// </summary>
    [DisallowMultipleComponent]
    public class IntroTextSequence : MonoBehaviour
    {
        [Header("Windows (in order)")]
        [Tooltip("Each entry is a GameObject to show. Only one is active at a time. Player advances with input.")]
        [SerializeField] private List<GameObject> windows = new List<GameObject>();

        [Header("Input")]
        [Tooltip("Keyboard key to advance to next window.")]
        [SerializeField] private KeyCode skipKey = KeyCode.E;

        [Tooltip("Controller buttons to advance.")]
        [SerializeField] private List<KeyCode> gamepadSkipButtons = new List<KeyCode>
        {
            KeyCode.JoystickButton0,
            KeyCode.JoystickButton1
        };

        [Tooltip("Cooldown between skips to prevent accidental double-tap.")]
        [SerializeField] private float skipCooldownSeconds = 0.5f;

        [Header("Time")]
        [Tooltip("Freeze time while windows are showing.")]
        [SerializeField] private bool freezeTime = true;

        [Tooltip("Restore the previous timeScale when done (otherwise sets to 1).")]
        [SerializeField] private bool restorePreviousTimeScale = true;

        [Header("Events")]
        [Tooltip("Fired when the entire sequence finishes and game resumes.")]
        public UnityEvent onSequenceComplete;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private int _currentIndex = -1;
        private float _canSkipAtUnscaledTime;
        private float _prevTimeScale = 1f;
        private bool _active;

        private void Start()
        {
            HideAll();

            if (windows == null || windows.Count == 0)
            {
                if (debugLogs)
                    Debug.Log("[IntroTextSequence] No windows configured. Skipping sequence.", this);
                return;
            }

            BeginSequence();
        }

        private void Update()
        {
            if (!_active)
                return;

            if (Time.unscaledTime < _canSkipAtUnscaledTime)
                return;

            if (IsSkipPressed())
                Advance();
        }

        private void BeginSequence()
        {
            _active = true;

            if (freezeTime)
            {
                _prevTimeScale = Time.timeScale;
                Time.timeScale = 0f;

                if (debugLogs)
                    Debug.Log($"[IntroTextSequence] Time frozen (was {_prevTimeScale}).", this);
            }

            _currentIndex = -1;
            Advance();
        }

        private void Advance()
        {
            // Hide current
            if (_currentIndex >= 0 && _currentIndex < windows.Count && windows[_currentIndex] != null)
                windows[_currentIndex].SetActive(false);

            _currentIndex++;

            if (_currentIndex >= windows.Count)
            {
                EndSequence();
                return;
            }

            // Show next
            if (windows[_currentIndex] != null)
                windows[_currentIndex].SetActive(true);

            _canSkipAtUnscaledTime = Time.unscaledTime + Mathf.Max(0f, skipCooldownSeconds);

            if (debugLogs)
                Debug.Log($"[IntroTextSequence] Showing window {_currentIndex + 1}/{windows.Count}", this);
        }

        private void EndSequence()
        {
            _active = false;

            HideAll();

            if (freezeTime)
            {
                Time.timeScale = restorePreviousTimeScale ? _prevTimeScale : 1f;

                if (Time.timeScale == 0f)
                    Time.timeScale = 1f;

                if (debugLogs)
                    Debug.Log($"[IntroTextSequence] Time restored to {Time.timeScale}.", this);
            }

            if (debugLogs)
                Debug.Log("[IntroTextSequence] Sequence complete. Game started.", this);

            onSequenceComplete?.Invoke();
        }

        private void HideAll()
        {
            if (windows == null) return;
            for (int i = 0; i < windows.Count; i++)
                if (windows[i] != null)
                    windows[i].SetActive(false);
        }

        private bool IsSkipPressed()
        {
            if (Input.GetKeyDown(skipKey))
                return true;

            if (gamepadSkipButtons != null)
            {
                for (int i = 0; i < gamepadSkipButtons.Count; i++)
                {
                    if (Input.GetKeyDown(gamepadSkipButtons[i]))
                        return true;
                }
            }

            return false;
        }
    }
}