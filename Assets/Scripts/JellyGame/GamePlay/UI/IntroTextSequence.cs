// FILEPATH: Assets/Scripts/JellyGame/UI/IntroTextSequence.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using JellyGame.GamePlay.Audio.Core; // <--- הוספנו את זה

namespace JellyGame.UI
{
    /// <summary>
    /// Simple intro text sequence for levels.
    /// 
    /// On Awake: Freezes time immediately (before any Start runs) + disables listed scripts
    /// On Start: Shows first window, waits for input to advance
    /// When done: Restores time, re-enables scripts, fires onSequenceComplete
    /// </summary>
    [DisallowMultipleComponent]
    public class IntroTextSequence : MonoBehaviour
    {
        [Header("Windows (in order)")]
        [Tooltip("Each entry is a GameObject to show. Only one is active at a time. Player advances with input.")]
        [SerializeField] private List<GameObject> windows = new List<GameObject>();

        [Header("Audio")]
        [Tooltip("List of audio names (from AudioSettings) matching the order of windows above.")]
        [SerializeField] private List<string> windowAudioNames = new List<string>();

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

        [Header("Pause During Intro")]
        [Tooltip("Scripts to disable during the intro and re-enable when done.")]
        [SerializeField] private List<Behaviour> disableDuringIntro = new List<Behaviour>();

        [Tooltip("GameObjects to deactivate during the intro and reactivate when done.")]
        [SerializeField] private List<GameObject> deactivateDuringIntro = new List<GameObject>();

        [Header("Events")]
        [Tooltip("Fired when the entire sequence finishes and game resumes.")]
        public UnityEvent onSequenceComplete;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private int _currentIndex = -1;
        private float _canSkipAtUnscaledTime;
        private float _prevTimeScale = 1f;
        private bool _active;

        private AudioSourceWrapper _currentVoiceover;

        // Snapshot of original states so we restore correctly
        private struct BehaviourSnapshot { public Behaviour b; public bool wasEnabled; }
        private struct GameObjectSnapshot { public GameObject go; public bool wasActive; }

        private readonly List<BehaviourSnapshot> _disabledSnapshot = new List<BehaviourSnapshot>();
        private readonly List<GameObjectSnapshot> _deactivatedSnapshot = new List<GameObjectSnapshot>();

        private void Awake()
        {
            if (windows == null || windows.Count == 0)
                return;

            // Freeze time IMMEDIATELY in Awake
            if (freezeTime)
            {
                _prevTimeScale = Time.timeScale;
                Time.timeScale = 0f;

                if (debugLogs)
                    Debug.Log($"[IntroTextSequence] Time frozen in Awake (was {_prevTimeScale}).", this);
            }

            // Disable scripts
            if (disableDuringIntro != null)
            {
                for (int i = 0; i < disableDuringIntro.Count; i++)
                {
                    Behaviour b = disableDuringIntro[i];
                    if (b == null) continue;
                    _disabledSnapshot.Add(new BehaviourSnapshot { b = b, wasEnabled = b.enabled });
                    b.enabled = false;
                }
            }

            // Deactivate GameObjects
            if (deactivateDuringIntro != null)
            {
                for (int i = 0; i < deactivateDuringIntro.Count; i++)
                {
                    GameObject go = deactivateDuringIntro[i];
                    if (go == null) continue;
                    _deactivatedSnapshot.Add(new GameObjectSnapshot { go = go, wasActive = go.activeSelf });
                    go.SetActive(false);
                }
            }
        }

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
            _currentIndex = -1;
            Advance();
        }

        private void Advance()
        {
            if (_currentVoiceover != null)
            {
                _currentVoiceover.Reset();
                if (_currentVoiceover.gameObject != null) _currentVoiceover.gameObject.SetActive(false);
                SoundPool.Instance.Return(_currentVoiceover);
                _currentVoiceover = null;
            }

            if (_currentIndex >= 0 && _currentIndex < windows.Count && windows[_currentIndex] != null)
                windows[_currentIndex].SetActive(false);

            _currentIndex++;

            if (_currentIndex >= windows.Count)
            {
                EndSequence();
                return;
            }

            if (windowAudioNames != null && _currentIndex < windowAudioNames.Count)
            {
                string audioName = windowAudioNames[_currentIndex];
                if (!string.IsNullOrEmpty(audioName))
                {
                    _currentVoiceover = SoundManager.Instance.PlaySound(audioName, transform);
                }
            }

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

            if (_currentVoiceover != null)
            {
                _currentVoiceover.Reset();
                if (_currentVoiceover.gameObject != null) _currentVoiceover.gameObject.SetActive(false);
                SoundPool.Instance.Return(_currentVoiceover);
                _currentVoiceover = null;
            }

            // Restore disabled scripts
            for (int i = 0; i < _disabledSnapshot.Count; i++)
            {
                if (_disabledSnapshot[i].b != null)
                    _disabledSnapshot[i].b.enabled = _disabledSnapshot[i].wasEnabled;
            }
            _disabledSnapshot.Clear();

            // Restore deactivated GameObjects
            for (int i = 0; i < _deactivatedSnapshot.Count; i++)
            {
                if (_deactivatedSnapshot[i].go != null)
                    _deactivatedSnapshot[i].go.SetActive(_deactivatedSnapshot[i].wasActive);
            }
            _deactivatedSnapshot.Clear();

            // Restore time
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