// FILEPATH: Assets/Scripts/UI/DeathProgressCircleUI.cs
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using JellyGame.GamePlay.Managers;

namespace JellyGame.GamePlay.UI
{
    /// <summary>
    /// Fills a UI circle based on how many enemies died (listens to EntityDied event).
    /// Example: requiredDeaths=3 => each counted death adds +1/3 fill.
    /// </summary>
    [DisallowMultipleComponent]
    public class DeathProgressCircleUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Canvas progressCanvas;
        [SerializeField] private Image progressCircle;

        [Header("Death Requirement")]
        [Tooltip("Only deaths on these layers will be counted.")]
        [SerializeField] private LayerMask countLayers = ~0;

        [Min(1)]
        [SerializeField] private int requiredDeaths = 3;

        [Header("Behavior")]
        [Tooltip("If true, canvas is shown when progress starts and can be hidden when completed.")]
        [SerializeField] private bool autoShowCanvas = true;

        [Tooltip("If true, hides the canvas once filled to 100%.")]
        [SerializeField] private bool hideOnComplete = false;

        [Header("Animation")]
        [Tooltip("If 0, fill changes instantly. Otherwise animates fill to target.")]
        [SerializeField] private float fillTweenDuration = 0.2f;

        [SerializeField] private DG.Tweening.Ease fillEase = DG.Tweening.Ease.OutQuad;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private int _count;
        private float _targetFill;
        private DG.Tweening.Tween _fillTween;

        private void Awake()
        {
            if (requiredDeaths < 1) requiredDeaths = 1;

            if (progressCanvas != null && autoShowCanvas)
                progressCanvas.gameObject.SetActive(false);

            SetFillImmediate(0f);
        }

        private void OnEnable()
        {
            EventManager.StartListening(EventManager.GameEvent.EntityDied, OnEntityDied);
        }

        private void OnDisable()
        {
            EventManager.StopListening(EventManager.GameEvent.EntityDied, OnEntityDied);
            if (_fillTween != null)
            {
                _fillTween.Kill();
                _fillTween = null;
            }
        }

        private void OnEntityDied(object eventData)
        {
            if (!(eventData is EntityDiedEventData))
                return;

            EntityDiedEventData e = (EntityDiedEventData)eventData;

            int layer = e.VictimLayer;

            // Layer filtering (same logic as DoorByDeaths)
            if ((countLayers.value & (1 << layer)) == 0)
                return;

            _count = Mathf.Min(_count + 1, requiredDeaths);

            if (autoShowCanvas && progressCanvas != null && _count > 0)
                progressCanvas.gameObject.SetActive(true);

            _targetFill = Mathf.Clamp01(_count / (float)requiredDeaths);
            AnimateFillTo(_targetFill);

            if (debugLogs)
                Debug.Log($"[DeathProgressCircleUI] Counted death {_count}/{requiredDeaths} -> fill={_targetFill:0.00}", this);

            if (_count >= requiredDeaths && hideOnComplete && progressCanvas != null)
                progressCanvas.gameObject.SetActive(false);
        }

        private void AnimateFillTo(float value01)
        {
            if (progressCircle == null)
                return;

            if (_fillTween != null)
            {
                _fillTween.Kill();
                _fillTween = null;
            }

            if (fillTweenDuration <= 0f)
            {
                SetFillImmediate(value01);
                return;
            }

            float start = progressCircle.fillAmount;
            _fillTween = DOTween.To(() => start, v =>
            {
                start = v;
                progressCircle.fillAmount = v;
            }, value01, fillTweenDuration).SetEase(fillEase);
        }

        private void SetFillImmediate(float value01)
        {
            if (progressCircle != null)
                progressCircle.fillAmount = Mathf.Clamp01(value01);
        }

        // Optional: if you want to reset from other scripts (e.g., new run / restart)
        public void ResetProgress(bool hideCanvas = true)
        {
            if (_fillTween != null)
            {
                _fillTween.Kill();
                _fillTween = null;
            }
            _count = 0;
            _targetFill = 0f;
            SetFillImmediate(0f);

            if (progressCanvas != null && hideCanvas)
                progressCanvas.gameObject.SetActive(false);
        }
    }
}