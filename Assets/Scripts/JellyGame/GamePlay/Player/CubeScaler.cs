// FILEPATH: Assets/Scripts/Player/CubeScaler.cs

using System.Collections;
using JellyGame.GamePlay.Audio.Core;
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

        [Tooltip("How long to wait before firing death event + destroying (lets death SFX play).")]
        [SerializeField] private float destroyDelaySeconds = 1f;

        [Tooltip("If true, hide the character visuals immediately on death, while scripts can keep running.")]
        [SerializeField] private bool hideVisualsOnDeath = true;

        [Tooltip("If assigned, only these renderers will be hidden. If empty, we auto-hide ALL child renderers.")]
        [SerializeField] private Renderer[] renderersToHide;

        [Tooltip("Optional: hide the health UI image immediately on death.")]
        [SerializeField] private bool hideHealthUiOnDeath = true;

        [Header("KinematicSurfaceSlider Integration")]
        [Tooltip("If assigned, we will update slider hoverHeight when size changes.")]
        [SerializeField] private KinematicSurfaceSlider surfaceSlider;

        [Tooltip("At size=1, what hoverHeight should be used.")]
        [SerializeField] private float hoverHeightAtSize1 = 0.5f;

        [Tooltip("If true: hoverHeight scales linearly with size (size=2 => hoverHeight*2).")]
        [SerializeField] private bool scaleHoverHeightWithSize = true;

        [Header("Particles Integration")]
        [Tooltip("Optional: ParticleSystem GameObject (child of player) that should scale with the player.")]
        [SerializeField] private GameObject particlesObject;

        [Tooltip("If true, particles will scale with player size. If false, particles stay at original size.")]
        [SerializeField] private bool scaleParticlesWithSize = true;

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

        // Cached visuals
        private Renderer[] _cachedRenderersToHide;

        private void Awake()
        {
            if (surfaceSlider == null)
                surfaceSlider = GetComponent<KinematicSurfaceSlider>();

            CacheHealthUiStartY();
            CacheRenderersToHide();

            if (updateHealthUiOnAwake)
            {
                float current = GetCurrentSize();
                UpdateHealthUiY(current);
                UpdateParticlesScale(current);
            }
        }

        private void CacheHealthUiStartY()
        {
            if (healthImageRect == null)
                return;

            _healthStartAnchoredY = healthImageRect.anchoredPosition.y;
            _hasHealthStartY = true;
        }

        private void CacheRenderersToHide()
        {
            if (renderersToHide != null && renderersToHide.Length > 0)
            {
                _cachedRenderersToHide = renderersToHide;
                return;
            }

            // Auto: hide everything visual under this object (including self)
            _cachedRenderersToHide = GetComponentsInChildren<Renderer>(true);
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

            // Update particles scale (optional)
            UpdateParticlesScale(size);

            OnSizeChanged?.Invoke(oldSize, size);

            if (logChanges)
                Debug.Log($"[CubeScaler] Size {oldSize:F3} -> {size:F3}", this);

            // Death check
            if (!_dead && size <= minSize + 1e-4f)
            {
                HandleDeathStarted();
            }
        }

        private void HandleDeathStarted()
        {
            // Mark dead immediately so no more damage/heal/scale changes happen.
            _dead = true;

            // Hide visuals immediately so player won't see post-death movement
            if (hideVisualsOnDeath)
            {
                HideAllVisuals();
            }

            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.StopAllSounds();
                SoundManager.Instance.PlaySound("Lose", this.transform);
            }

            StartCoroutine(Die());
        }

        private void HideAllVisuals()
        {
            if (_cachedRenderersToHide == null || _cachedRenderersToHide.Length == 0)
                CacheRenderersToHide();

            if (_cachedRenderersToHide != null)
            {
                for (int i = 0; i < _cachedRenderersToHide.Length; i++)
                {
                    if (_cachedRenderersToHide[i] != null)
                        _cachedRenderersToHide[i].enabled = false;
                }
            }

            if (hideHealthUiOnDeath && healthImageRect != null)
                healthImageRect.gameObject.SetActive(false);
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

        private void UpdateParticlesScale(float size)
        {
            if (!scaleParticlesWithSize || particlesObject == null)
                return;

            // Scale particles GameObject transform to match player size
            if (uniformScale)
            {
                particlesObject.transform.localScale = new Vector3(size, size, size);
            }
            else
            {
                Vector3 currentScale = particlesObject.transform.localScale;
                particlesObject.transform.localScale = new Vector3(currentScale.x, size, currentScale.z);
            }

            // Also scale ParticleSystem if it exists
            ParticleSystem ps = particlesObject.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
        }

        private IEnumerator Die()
        {
            // Wait so the lose sound can be heard while the object stays alive (but invisible).
            float delay = Mathf.Max(0f, destroyDelaySeconds);
            if (delay > 0f)
                yield return new WaitForSeconds(delay);

            EventManager.TriggerEvent(
                EventManager.GameEvent.EntityDied,
                new EntityDiedEventData(gameObject, gameObject.layer)
            );

            if (destroyOnDeath)
                Destroy(gameObject);
        }
    }
}
