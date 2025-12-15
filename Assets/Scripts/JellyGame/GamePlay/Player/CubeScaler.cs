// FILEPATH: Assets/Scripts/Player/CubeScaler.cs
using JellyGame.GamePlay.Managers;
using JellyGame.GamePlay.Combat;
using UnityEngine;

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

        [Header("Debug")]
        [SerializeField] private bool logChanges = false;

        public event System.Action<float, float> OnSizeChanged; // (oldSize, newSize)

        private bool _dead;

        private void Awake()
        {
            if (surfaceSlider == null)
                surfaceSlider = GetComponent<KinematicSurfaceSlider>();
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
