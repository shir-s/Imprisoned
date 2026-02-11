// FILEPATH: Assets/Scripts/JellyGame/GamePlay/UI/TimerBarAnimation.cs
using UnityEngine;
using JellyGame.GamePlay.Managers;

namespace JellyGame.GamePlay.UI
{
    public class TimerBarAnimation : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The Logic script that handles the time.")]
        [SerializeField] private CountdownTimer countdownTimer;

        [Tooltip("The object to move (Timer Base). Drag the RectTransform here.")]
        [SerializeField] private RectTransform timerBaseRect;

        [Header("Animation Settings")]
        [Tooltip("Y Position at the START of the level (Full Time).")]
        [SerializeField] private float startY = -100.6f;

        [Tooltip("Y Position at the END of the level (Zero Time).")]
        [SerializeField] private float endY = -0.3f;

        private float _totalTime;

        private void Start()
        {
            // Auto-find timer if not assigned
            if (countdownTimer == null)
                countdownTimer = FindObjectOfType<CountdownTimer>();

            if (countdownTimer != null)
            {
                // We capture the initial time as the "Total Time" to calculate the percentage.
                // Assuming the UI starts when the timer is full.
                _totalTime = Mathf.Max(1f, countdownTimer.RemainingSeconds);
            }
            else
            {
                Debug.LogError("[TimerBarAnimation] CountdownTimer not found!");
            }
        }

        private void Update()
        {
            if (countdownTimer == null || timerBaseRect == null) return;

            // 1. Calculate the percentage of time REMAINING (0.0 to 1.0)
            float currentRemaining = countdownTimer.RemainingSeconds;
            float percentageRemaining = Mathf.Clamp01(currentRemaining / _totalTime);

            // 2. Calculate the percentage of time PASSED (0.0 to 1.0)
            // Because we want the bar to go UP as time passes:
            // Start (0% passed) = -100.6
            // End (100% passed) = -0.3
            float percentagePassed = 1f - percentageRemaining;

            // 3. Calculate new Y position
            float newY = Mathf.Lerp(startY, endY, percentagePassed);

            // 4. Apply to the RectTransform
            Vector2 newPos = timerBaseRect.anchoredPosition;
            newPos.y = newY;
            timerBaseRect.anchoredPosition = newPos;
        }
    }
}