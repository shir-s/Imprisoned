// FILEPATH: Assets/Scripts/JellyGame/GamePlay/Managers/CountdownTimer.cs
using UnityEngine;

namespace JellyGame.GamePlay.Managers
{
    /// <summary>
    /// Countdown timer for survival-style levels (e.g. stage 3).
    /// When time reaches 0 and the player is still alive (GameOver not triggered), fires GameWin
    /// so GameSceneManager loads the victory screen.
    /// </summary>
    public class CountdownTimer : MonoBehaviour
    {
        [Tooltip("Total countdown time in seconds (e.g. 60).")]
        [SerializeField] private float durationSeconds = 60f;

        [Tooltip("If true, timer starts on Enable. If false, call StartTimer() to begin.")]
        [SerializeField] private bool startOnEnable = true;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        /// <summary>Remaining time in seconds. 0 or below when finished. Use this for UI.</summary>
        public float RemainingSeconds => _remaining;

        /// <summary>True when the countdown has reached zero (win or already lost).</summary>
        public bool IsFinished => _remaining <= 0f;

        private float _remaining;
        private bool _gameOverTriggered;
        private bool _running;

        private void OnEnable()
        {
            EventManager.StartListening(EventManager.GameEvent.GameOver, OnGameOver);
            _gameOverTriggered = false;
            _remaining = durationSeconds;
            _running = startOnEnable;
            if (debugLogs && _running)
                Debug.Log($"[CountdownTimer] Started. Duration={durationSeconds}s", this);
        }

        private void OnDisable()
        {
            EventManager.StopListening(EventManager.GameEvent.GameOver, OnGameOver);
        }

        private void OnGameOver(object _)
        {
            _gameOverTriggered = true;
            _running = false;
            if (debugLogs)
                Debug.Log("[CountdownTimer] GameOver received - will not trigger win when time ends.", this);
        }

        private void Update()
        {
            if (!_running || _remaining <= 0f)
                return;

            _remaining -= Time.deltaTime;
            if (_remaining <= 0f)
            {
                _remaining = 0f;
                _running = false;
                if (_gameOverTriggered)
                {
                    if (debugLogs)
                        Debug.Log("[CountdownTimer] Time's up but player already dead - no win.", this);
                    return;
                }
                if (debugLogs)
                    Debug.Log("[CountdownTimer] Time's up and player alive → GameWin.", this);
                EventManager.TriggerEvent(EventManager.GameEvent.GameWin, null);
            }
        }

        /// <summary>Start or restart the countdown (e.g. when startOnEnable is false).</summary>
        public void StartTimer()
        {
            _remaining = durationSeconds;
            _running = true;
            if (debugLogs)
                Debug.Log($"[CountdownTimer] StartTimer called. Duration={durationSeconds}s", this);
        }

        /// <summary>Pause the countdown (no-op when time already finished).</summary>
        public void Pause() => _running = false;

        /// <summary>Resume the countdown if it was paused and not finished.</summary>
        public void Resume() { if (_remaining > 0f) _running = true; }
    }
}
