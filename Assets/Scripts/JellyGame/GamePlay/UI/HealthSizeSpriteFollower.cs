// FILEPATH: Assets/Scripts/UI/HealthSizeSpriteFollower.cs
using JellyGame.GamePlay.Player;
using UnityEngine;
using UnityEngine.UI;

namespace JellyGame.UI
{
    [DisallowMultipleComponent]
    public class HealthSizeSpriteFollower : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CubeScaler cubeScaler;
        [SerializeField] private RectTransform targetRect; // UI sprite rect (usually this)

        [Header("Mapping")]
        [Tooltip("UI scale when cube is at min size.")]
        [SerializeField] private float uiScaleAtMinSize = 0.5f;

        [Tooltip("UI scale when cube is at max size.")]
        [SerializeField] private float uiScaleAtMaxSize = 1.5f;

        [Header("Smoothing")]
        [SerializeField] private bool smooth = true;

        [Tooltip("Smaller = faster response.")]
        [SerializeField] private float smoothTime = 0.12f;

        [Header("Source Size Range (match CubeScaler)")]
        [SerializeField] private float minSize = 0.2f;
        [SerializeField] private float maxSize = 3.0f;

        private float _targetUiScale = 1f;
        private float _uiScaleVelocity;

        private void Reset()
        {
            targetRect = GetComponent<RectTransform>();
        }

        private void Awake()
        {
            if (targetRect == null)
                targetRect = GetComponent<RectTransform>();
        }

        private void OnEnable()
        {
            if (cubeScaler != null)
                cubeScaler.OnSizeChanged += HandleSizeChanged;

            ForceRefresh();
        }

        private void OnDisable()
        {
            if (cubeScaler != null)
                cubeScaler.OnSizeChanged -= HandleSizeChanged;
        }

        private void Update()
        {
            if (!smooth || targetRect == null)
                return;

            float current = targetRect.localScale.x;
            float newScale = Mathf.SmoothDamp(current, _targetUiScale, ref _uiScaleVelocity, Mathf.Max(0.0001f, smoothTime));
            targetRect.localScale = new Vector3(newScale, newScale, 1f);
        }

        private void HandleSizeChanged(float oldSize, float newSize)
        {
            _targetUiScale = ComputeUiScale(newSize);

            if (!smooth && targetRect != null)
                targetRect.localScale = new Vector3(_targetUiScale, _targetUiScale, 1f);
        }

        private float ComputeUiScale(float cubeSize)
        {
            float s = Mathf.Clamp(cubeSize, minSize, maxSize);
            float t = Mathf.InverseLerp(minSize, maxSize, s);
            return Mathf.Lerp(uiScaleAtMinSize, uiScaleAtMaxSize, t);
        }

        private void ForceRefresh()
        {
            if (cubeScaler == null || targetRect == null)
                return;

            float cubeSize = cubeScaler.transform.localScale.x;
            _targetUiScale = ComputeUiScale(cubeSize);

            if (!smooth)
                targetRect.localScale = new Vector3(_targetUiScale, _targetUiScale, 1f);
        }
    }
}
