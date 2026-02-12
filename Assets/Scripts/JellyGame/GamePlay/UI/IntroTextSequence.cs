// FILEPATH: Assets/Scripts/JellyGame/UI/IntroTextSequence.cs
using System.Collections; // חובה בשביל Coroutines
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using JellyGame.GamePlay.Audio.Core;

namespace JellyGame.UI
{
    [DisallowMultipleComponent]
    public class IntroTextSequence : MonoBehaviour
    {
        [Header("Windows (in order)")]
        [SerializeField] private List<GameObject> windows = new List<GameObject>();

        [Header("Audio")]
        [SerializeField] private List<string> windowAudioNames = new List<string>();

        [Header("Animation")]
        [Tooltip("Assign the Animator of Slime Prime here")]
        [SerializeField] private Animator slimeAnimator;

        [Header("Input")]
        [SerializeField] private KeyCode skipKey = KeyCode.E;
        [SerializeField] private List<KeyCode> gamepadSkipButtons = new List<KeyCode> { KeyCode.JoystickButton0, KeyCode.JoystickButton1 };
        [SerializeField] private float skipCooldownSeconds = 0.5f;

        [Header("Time")]
        [SerializeField] private bool freezeTime = true;
        [SerializeField] private bool restorePreviousTimeScale = true;

        [Header("Pause During Intro")]
        [SerializeField] private List<Behaviour> disableDuringIntro = new List<Behaviour>();
        [SerializeField] private List<GameObject> deactivateDuringIntro = new List<GameObject>();

        [Header("Events")]
        public UnityEvent onSequenceComplete;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private int _currentIndex = -1;
        private float _canSkipAtUnscaledTime;
        private float _prevTimeScale = 1f;
        private bool _active;
        private AudioSourceWrapper _currentVoiceover;

        // Snapshots for restoring state
        private struct BehaviourSnapshot { public Behaviour b; public bool wasEnabled; }
        private struct GameObjectSnapshot { public GameObject go; public bool wasActive; }
        private readonly List<BehaviourSnapshot> _disabledSnapshot = new List<BehaviourSnapshot>();
        private readonly List<GameObjectSnapshot> _deactivatedSnapshot = new List<GameObjectSnapshot>();

        private void Awake()
        {
            if (windows == null || windows.Count == 0) return;

            if (freezeTime)
            {
                _prevTimeScale = Time.timeScale;
                Time.timeScale = 0f;
            }

            // Snapshot & Disable scripts
            if (disableDuringIntro != null)
            {
                foreach (var b in disableDuringIntro)
                {
                    if (b == null) continue;
                    _disabledSnapshot.Add(new BehaviourSnapshot { b = b, wasEnabled = b.enabled });
                    b.enabled = false;
                }
            }

            // Snapshot & Deactivate objects
            if (deactivateDuringIntro != null)
            {
                foreach (var go in deactivateDuringIntro)
                {
                    if (go == null) continue;
                    _deactivatedSnapshot.Add(new GameObjectSnapshot { go = go, wasActive = go.activeSelf });
                    go.SetActive(false);
                }
            }
        }

        private void Start()
        {
            HideAll();
            if (windows == null || windows.Count == 0) return;
            BeginSequence();
        }

        private void Update()
        {
            if (!_active) return;
            if (Time.unscaledTime < _canSkipAtUnscaledTime) return;
            if (IsSkipPressed()) Advance();
        }

        private void BeginSequence()
        {
            _active = true;
            _currentIndex = -1;
            Advance();
        }

        private void Advance()
        {
            // Stop previous audio
            if (_currentVoiceover != null)
            {
                _currentVoiceover.Reset();
                if (_currentVoiceover.gameObject != null) _currentVoiceover.gameObject.SetActive(false);
                SoundPool.Instance.Return(_currentVoiceover);
                _currentVoiceover = null;
            }

            // Hide current window
            if (_currentIndex >= 0 && _currentIndex < windows.Count && windows[_currentIndex] != null)
                windows[_currentIndex].SetActive(false);

            _currentIndex++;

            // Check if finished
            if (_currentIndex >= windows.Count)
            {
                EndSequence();
                return;
            }

            // Play new audio
            if (windowAudioNames != null && _currentIndex < windowAudioNames.Count)
            {
                string audioName = windowAudioNames[_currentIndex];
                if (!string.IsNullOrEmpty(audioName))
                {
                    _currentVoiceover = SoundManager.Instance.PlaySound(audioName, transform);
                }
            }

            // Show next window
            if (windows[_currentIndex] != null)
                windows[_currentIndex].SetActive(true);

            _canSkipAtUnscaledTime = Time.unscaledTime + Mathf.Max(0f, skipCooldownSeconds);
        }
        
        private void EndSequence()
        {
            _active = false;
            HideAll();

            // Stop last audio
            if (_currentVoiceover != null)
            {
                _currentVoiceover.Reset();
                if (_currentVoiceover.gameObject != null) _currentVoiceover.gameObject.SetActive(false);
                SoundPool.Instance.Return(_currentVoiceover);
                _currentVoiceover = null;
            }

            // Restore scripts
            foreach (var s in _disabledSnapshot) if (s.b != null) s.b.enabled = s.wasEnabled;
            _disabledSnapshot.Clear();

            // Restore objects
            foreach (var s in _deactivatedSnapshot) if (s.go != null) s.go.SetActive(s.wasActive);
            _deactivatedSnapshot.Clear();

            // Restore time
            if (freezeTime)
            {
                Time.timeScale = restorePreviousTimeScale ? _prevTimeScale : 1f;
                if (Time.timeScale == 0f) Time.timeScale = 1f;
            }

            if (slimeAnimator != null)
            {
                StartCoroutine(PlaySlimeAnimation());
            }

            onSequenceComplete?.Invoke();
        }

        private IEnumerator PlaySlimeAnimation()
        {
            slimeAnimator.SetBool("cast in", true);

            yield return new WaitForSeconds(2.0f);

            slimeAnimator.SetBool("cast in", false);
            slimeAnimator.SetBool("casting", true);
        }

        private void HideAll()
        {
            if (windows == null) return;
            foreach (var w in windows) if (w != null) w.SetActive(false);
        }

        private bool IsSkipPressed()
        {
            if (Input.GetKeyDown(skipKey)) return true;
            if (gamepadSkipButtons != null)
            {
                foreach (var btn in gamepadSkipButtons) if (Input.GetKeyDown(btn)) return true;
            }
            return false;
        }
    }
}