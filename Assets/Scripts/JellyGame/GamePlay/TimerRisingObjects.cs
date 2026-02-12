// FILEPATH: Assets/Scripts/JellyGame/GamePlay/World/TimerRisingObjects.cs
using UnityEngine;

namespace JellyGame.GamePlay.World
{
    /// <summary>
    /// Moves objects upward (local Y) as the CountdownTimer progresses.
    /// At timer start → objects at starting position.
    /// At timer end   → objects have risen by maxRiseHeight.
    /// 
    /// Objects should be children of the surface so tilting is handled automatically.
    /// </summary>
    [DisallowMultipleComponent]
    public class TimerRisingObjects : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The CountdownTimer to read progress from.")]
        [SerializeField] private Managers.CountdownTimer countdownTimer;

        [Header("Objects")]
        [Tooltip("Objects to move upward. Must be children of the surface.")]
        [SerializeField] private Transform[] risingObjects;

        [Header("Rise Settings")]
        [Tooltip("Maximum height (local Y) the objects rise by when the timer reaches 0.")]
        [SerializeField] private float maxRiseHeight = 5f;

        [Tooltip("Animation curve for the rise. X = timer progress (0=start, 1=end). Y = rise amount (0=none, 1=full height).")]
        [SerializeField] private AnimationCurve riseCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private Vector3[] _startLocalPositions;
        private float _totalDuration;
        private bool _initialized;

        private void Start()
        {
            if (countdownTimer == null)
            {
                countdownTimer = FindObjectOfType<Managers.CountdownTimer>();
                if (countdownTimer == null)
                {
                    Debug.LogError("[TimerRisingObjects] No CountdownTimer found!", this);
                    enabled = false;
                    return;
                }
            }

            if (risingObjects == null || risingObjects.Length == 0)
            {
                Debug.LogWarning("[TimerRisingObjects] No rising objects assigned.", this);
                enabled = false;
                return;
            }

            _startLocalPositions = new Vector3[risingObjects.Length];
            for (int i = 0; i < risingObjects.Length; i++)
            {
                if (risingObjects[i] != null)
                    _startLocalPositions[i] = risingObjects[i].localPosition;
            }

            _initialized = true;

            if (debugLogs)
                Debug.Log($"[TimerRisingObjects] Initialized with {risingObjects.Length} objects, maxHeight={maxRiseHeight}", this);
        }

        private void Update()
        {
            if (!_initialized)
                return;

            if (_totalDuration <= 0f)
            {
                float r = countdownTimer.RemainingSeconds;
                if (r > 0f) _totalDuration = r;
                else return;
            }

            float elapsed = _totalDuration - countdownTimer.RemainingSeconds;
            float progress = Mathf.Clamp01(elapsed / _totalDuration);
            float height = riseCurve.Evaluate(progress) * maxRiseHeight;

            for (int i = 0; i < risingObjects.Length; i++)
            {
                if (risingObjects[i] == null)
                    continue;

                risingObjects[i].localPosition = _startLocalPositions[i] + Vector3.up * height;
            }
        }
    }
}