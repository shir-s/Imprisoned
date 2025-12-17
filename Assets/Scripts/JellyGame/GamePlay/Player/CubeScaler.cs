// FILEPATH: Assets/Scripts/Player/CubeScaler.cs
using JellyGame.GamePlay.Managers;
using JellyGame.GamePlay.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace JellyGame.GamePlay.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class CubeScaler : MonoBehaviour, IDamageable
    {
        [Header("Scale Settings")]
        [SerializeField] private bool uniformScale = true;
        [SerializeField] private float minSize = 0.2f;
        [SerializeField] private float maxSize = 3.0f;

        [Header("Damage/Heal Mapping")]
        [SerializeField] private float sizeLossPerDamage = 0.1f;
        [SerializeField] private float sizeGainPerHeal = 0.1f;

        [Header("Death")]
        [SerializeField] private bool destroyOnDeath = true;

        [Header("KinematicSurfaceSlider Integration")]
        [Tooltip("If assigned, we will update slider hoverHeight when size changes.")]
        [SerializeField] private KinematicSurfaceSlider surfaceSlider;

        [Tooltip("At size=1, what hoverHeight should be used.")]
        [SerializeField] private float hoverHeightAtSize1 = 0.5f;

        [Tooltip("If true: hoverHeight scales linearly with size (size=2 => hoverHeight*2).")]
        [SerializeField] private bool scaleHoverHeightWithSize = true;

        [Header("UI Health Indicator (optional)")]
        [Tooltip("Optional UI element (Image/RectTransform) that moves DOWN as the cube shrinks.\n" +
                 "Assumes its current anchoredPosition.y is the 'full health' position (at maxSize).")]
        [SerializeField] private RectTransform healthImageRect;

        [Tooltip("How many UI units (anchored Y) to move DOWN when size reaches minSize.\n" +
                 "Example: 30 => at minSize, Y is (startY - 30).")]
        [SerializeField] private float healthImageDownAtMinSize = 30f;

        [Tooltip("If true, update health UI on Awake as well (so it's correct on scene start).")]
        [SerializeField] private bool updateHealthUiOnAwake = true;

        [Header("Debug")]
        [SerializeField] private bool logChanges = false;

        public event System.Action<float, float> OnSizeChanged; // (oldSize, newSize)

        private bool _dead;

        // UI baseline (at max size)
        private float _healthStartAnchoredY;
        private bool _hasHealthStartY;

        private void Awake()
        {
            if (surfaceSlider == null)
                surfaceSlider = GetComponent<KinematicSurfaceSlider>();

            CacheHealthUiStartY();

            if (updateHealthUiOnAwake)
            {
                float current = GetCurrentSize();
                UpdateHealthUiY(current);
            }
        }

        private void CacheHealthUiStartY()
        {
            if (healthImageRect == null)
                return;

            _healthStartAnchoredY = healthImageRect.anchoredPosition.y;
            _hasHealthStartY = true;
        }

        public void ApplyDamage(float amount)
        {
            if (_dead || amount <= 0f) return;
            ChangeVolumeAdd(-amount * sizeLossPerDamage);
        }

        public void Heal(float amount)
        {
            if (_dead || amount <= 0f) return;
            ChangeVolumeAdd(amount * sizeGainPerHeal);
        }

        public void ChangeVolumeAdd(float amount)
        {
            if (_dead) return;

            float oldSize = GetCurrentSize();
            float newSize = oldSize + amount;

            ApplySize(newSize, oldSize);
        }

        public void ChangeVolumeMultiply(float factor)
        {
            if (_dead) return;

            float oldSize = GetCurrentSize();
            float newSize = oldSize * factor;

            ApplySize(newSize, oldSize);
        }

        private float GetCurrentSize()
        {
            Vector3 s = transform.localScale;
            return uniformScale ? s.x : s.y;
        }

        private void ApplySize(float size, float oldSize)
        {
            size = Mathf.Clamp(size, minSize, maxSize);

            if (Mathf.Approximately(size, oldSize))
                return;

            Vector3 newScale = transform.localScale;
            if (uniformScale)
                newScale = new Vector3(size, size, size);
            else
                newScale.y = size;

            transform.localScale = newScale;

            // Tell movement script how far the pivot should hover above the surface.
            UpdateHoverHeight(size);

            // Update UI health position (optional)
            UpdateHealthUiY(size);

            OnSizeChanged?.Invoke(oldSize, size);

            if (logChanges)
                Debug.Log($"[CubeScaler] Size {oldSize:F3} -> {size:F3}", this);

            if (!_dead && size <= minSize + 1e-4f)
                Die();
        }

        private void UpdateHoverHeight(float size)
        {
            if (surfaceSlider == null)
                return;

            float newHover = hoverHeightAtSize1;

            if (scaleHoverHeightWithSize)
                newHover *= Mathf.Max(0.01f, size);

            surfaceSlider.SetHoverHeight(newHover);
        }

        private void UpdateHealthUiY(float size)
        {
            if (healthImageRect == null)
                return;

            if (!_hasHealthStartY)
                CacheHealthUiStartY();

            // Normalize size: maxSize => 1, minSize => 0
            float t = Mathf.InverseLerp(minSize, maxSize, size);

            // At maxSize (t=1): offsetDown = 0
            // At minSize (t=0): offsetDown = healthImageDownAtMinSize
            float offsetDown = (1f - t) * healthImageDownAtMinSize;

            Vector2 p = healthImageRect.anchoredPosition;
            p.y = _healthStartAnchoredY - offsetDown;
            healthImageRect.anchoredPosition = p;
        }

        private void Die()
        {
            if (_dead) return;
            _dead = true;

            EventManager.TriggerEvent(
                EventManager.GameEvent.EntityDied,
                new EntityDiedEventData(gameObject, gameObject.layer)
            );

            if (destroyOnDeath)
                Destroy(gameObject);
        }
    }
}
