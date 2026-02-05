// FILEPATH: Assets/Scripts/JellyGame/GamePlay/UI/CountdownTimerUI.cs
using UnityEngine;
using UnityEngine.UI;
using JellyGame.GamePlay.Managers;

namespace JellyGame.GamePlay.UI
{
    /// <summary>
    /// Displays the countdown timer as text (e.g. "1:00", "0:45", "0:00").
    /// Assign a CountdownTimer and either a TextMeshProUGUI or a legacy Text component.
    /// </summary>
    public class CountdownTimerUI : MonoBehaviour
    {
        [Header("Timer")]
        [Tooltip("The CountdownTimer to read from. If empty, will try to find one in the scene.")]
        [SerializeField] private CountdownTimer countdownTimer;

        [Header("Display")]
        [Tooltip("TextMeshPro text to show the time. Use this OR Legacy Text, not both.")]
        [SerializeField] private TMPro.TextMeshProUGUI textTMP;

        [Tooltip("Legacy UI Text to show the time. Use this OR TextMeshPro, not both.")]
        [SerializeField] private Text textLegacy;

        [Tooltip("Format: {0} = minutes, {1} = seconds (e.g. \"Time: {0}:{1}\"). Leave empty for default \"M:SS\".")]
        [SerializeField] private string format = "";

        [Tooltip("How often to refresh the displayed text (seconds). Lower = smoother, more updates.")]
        [SerializeField] private float refreshInterval = 0.25f;

        private float _lastRefreshTime;

        private void Awake()
        {
            if (countdownTimer == null)
                countdownTimer = FindObjectOfType<CountdownTimer>();
            if (countdownTimer == null)
                Debug.LogWarning("[CountdownTimerUI] No CountdownTimer assigned or found in scene.", this);
        }

        private void Update()
        {
            if (countdownTimer == null) return;
            if (Time.time - _lastRefreshTime < refreshInterval) return;

            _lastRefreshTime = Time.time;
            float remaining = Mathf.Max(0f, countdownTimer.RemainingSeconds);
            int minutes = Mathf.FloorToInt(remaining / 60f);
            int seconds = Mathf.FloorToInt(remaining % 60f);
            string timeStr = string.IsNullOrEmpty(format)
                ? $"{minutes}:{seconds:D2}"
                : string.Format(format, minutes, seconds);

            if (textTMP != null)
                textTMP.text = timeStr;
            if (textLegacy != null)
                textLegacy.text = timeStr;
        }
    }
}
