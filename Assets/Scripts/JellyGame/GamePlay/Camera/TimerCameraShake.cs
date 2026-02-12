// FILEPATH: Assets/Scripts/JellyGame/Camera/TimerCameraShake.cs
using UnityEngine;

namespace JellyGame.GamePlay.Camera
{
    /// <summary>
    /// Progressive camera shake driven by CountdownTimer.
    /// 
    /// Shake starts when the timer reaches shakeStartPercent (e.g. 0.5 = halfway through)
    /// and intensifies to full strength when the timer hits 0.
    /// 
    /// Uses additive offset so it works on top of EdgeFollowCamera without interference.
    /// 
    /// Execution order:
    /// 1. Update()     → removes previous frame's shake offset (clean position for EdgeFollowCamera)
    /// 2. EdgeFollowCamera.LateUpdate() → normal follow logic on clean position
    /// 3. This.LateUpdate() → applies new shake offset on top
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)] // Run after EdgeFollowCamera (default order 0)
    public class TimerCameraShake : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The CountdownTimer to read progress from.")]
        [SerializeField] private Managers.CountdownTimer countdownTimer;

        [Header("Shake Timing")]
        [Tooltip("Timer progress (0=start, 1=end) at which shake begins.\n" +
                 "Example: 0.5 means shake starts when 50% of time has passed.")]
        [SerializeField, Range(0f, 1f)] private float shakeStartPercent = 0.5f;

        [Header("Shake Intensity")]
        [Tooltip("Maximum shake displacement at full intensity (when timer hits 0).")]
        [SerializeField] private float maxShakeAmount = 0.3f;

        [Tooltip("How the shake ramps up. X = shake progress (0=just started, 1=timer end). Y = intensity multiplier.")]
        [SerializeField] private AnimationCurve intensityCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Shake Speed")]
        [Tooltip("How fast the shake oscillates. Higher = more frantic.")]
        [SerializeField] private float shakeSpeed = 25f;

        [Tooltip("Speed multiplier increase as intensity grows (makes it feel more urgent near the end).")]
        [SerializeField] private float speedRampMultiplier = 1.5f;

        [Header("Shake Axes")]
        [SerializeField] private bool shakeX = true;
        [SerializeField] private bool shakeY = true;
        [SerializeField] private bool shakeZ = false;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private Vector3 _currentOffset;
        private float _totalDuration;
        private float _noiseOffsetX;
        private float _noiseOffsetY;
        private float _noiseOffsetZ;
        private bool _shakeActive;

        private void Awake()
        {
            // Random seeds so each axis shakes differently
            _noiseOffsetX = Random.Range(0f, 100f);
            _noiseOffsetY = Random.Range(100f, 200f);
            _noiseOffsetZ = Random.Range(200f, 300f);
        }

        private void Start()
        {
            if (countdownTimer == null)
            {
                countdownTimer = FindObjectOfType<Managers.CountdownTimer>();
                if (countdownTimer == null)
                {
                    Debug.LogError("[TimerCameraShake] No CountdownTimer found!", this);
                    enabled = false;
                    return;
                }
            }
        }

        /// <summary>
        /// Runs BEFORE EdgeFollowCamera.LateUpdate().
        /// Removes previous frame's shake so EdgeFollowCamera works with a clean position.
        /// </summary>
        private void Update()
        {
            if (_currentOffset.sqrMagnitude > 0f)
            {
                transform.position -= _currentOffset;
                _currentOffset = Vector3.zero;
            }
        }

        /// <summary>
        /// Runs AFTER EdgeFollowCamera.LateUpdate() (DefaultExecutionOrder 100).
        /// Applies new shake offset on top of the follow position.
        /// </summary>
        private void LateUpdate()
        {
            if (_totalDuration <= 0f)
            {
                float r = countdownTimer.RemainingSeconds;
                if (r > 0f) _totalDuration = r;
                else return;
            }

            // Timer progress: 0 = start, 1 = end
            float elapsed = _totalDuration - countdownTimer.RemainingSeconds;
            float timerProgress = Mathf.Clamp01(elapsed / _totalDuration);

            // No shake yet
            if (timerProgress < shakeStartPercent)
            {
                if (_shakeActive)
                {
                    _shakeActive = false;
                    if (debugLogs)
                        Debug.Log("[TimerCameraShake] Shake stopped (timer reset?).", this);
                }
                return;
            }

            if (!_shakeActive)
            {
                _shakeActive = true;
                if (debugLogs)
                    Debug.Log($"[TimerCameraShake] Shake started at {timerProgress * 100f:F0}% timer progress.", this);
            }

            // Shake progress: 0 = just started shaking, 1 = timer at 0
            float shakeRange = 1f - shakeStartPercent;
            float shakeProgress = Mathf.Clamp01((timerProgress - shakeStartPercent) / shakeRange);

            float intensity = intensityCurve.Evaluate(shakeProgress) * maxShakeAmount;
            float speed = shakeSpeed * (1f + shakeProgress * speedRampMultiplier);
            float time = Time.unscaledTime * speed;

            // Perlin noise based shake (smooth, not jarring)
            float offsetX = shakeX ? (Mathf.PerlinNoise(time + _noiseOffsetX, 0f) - 0.5f) * 2f * intensity : 0f;
            float offsetY = shakeY ? (Mathf.PerlinNoise(0f, time + _noiseOffsetY) - 0.5f) * 2f * intensity : 0f;
            float offsetZ = shakeZ ? (Mathf.PerlinNoise(time + _noiseOffsetZ, time) - 0.5f) * 2f * intensity : 0f;

            _currentOffset = new Vector3(offsetX, offsetY, offsetZ);
            transform.position += _currentOffset;
        }

        /// <summary>
        /// Clean up offset if disabled mid-shake.
        /// </summary>
        private void OnDisable()
        {
            if (_currentOffset.sqrMagnitude > 0f)
            {
                transform.position -= _currentOffset;
                _currentOffset = Vector3.zero;
            }
        }
    }
}