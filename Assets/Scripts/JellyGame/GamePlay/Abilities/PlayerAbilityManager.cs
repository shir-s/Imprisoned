using JellyGame.GamePlay.Managers;
using UnityEngine;

namespace JellyGame.GamePlay.Abilities
{
    /// <summary>
    /// Tracks player abilities (like Stickiness power)
    /// Singleton pattern for easy global access
    /// </summary>
    public class PlayerAbilityManager : MonoBehaviour
    {
        public static PlayerAbilityManager Instance { get; private set; }

        [Header("Abilities")]
        [SerializeField] private bool hasStickinessAbility = false;

        [Header("Visual Feedback")]
        [Tooltip("Cube renderer to change material (optional)")]
        [SerializeField] private Renderer cubeRenderer;

        [Tooltip("Normal material for the cube")]
        [SerializeField] private Material normalMaterial;

        [Tooltip("Material when stickiness ability is active (e.g., glowing/sticky look)")]
        [SerializeField] private Material stickyMaterial;

        [Header("Paint Colors")]
        [Tooltip("Normal paint color (for trail and fills)")]
        [SerializeField] private Color normalPaintColor = Color.black;

        [Tooltip("Paint color when stickiness is active (e.g., green/glowing)")]
        [SerializeField] private Color stickyPaintColor = new Color(0f, 1f, 0.5f, 1f); // Green-ish

        /// <summary>
        /// Current paint color based on ability state
        /// </summary>
        public Color CurrentPaintColor => hasStickinessAbility ? stickyPaintColor : normalPaintColor;

        [Header("Debug")]
        [SerializeField] private bool startWithAllAbilities = false;

        [Header("Runtime Toggle")]
        [Tooltip("Key to press to toggle stickiness during gameplay (for testing)")]
        [SerializeField] private KeyCode toggleKey = KeyCode.T;

        public bool HasStickinessAbility => hasStickinessAbility;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // Auto-find cube renderer if not assigned
            if (cubeRenderer == null)
            {
                cubeRenderer = GetComponent<Renderer>();
                if (cubeRenderer == null)
                {
                    cubeRenderer = GetComponentInChildren<Renderer>();
                }
            }

            // Respect inspector checkbox value OR startWithAllAbilities
            if (startWithAllAbilities)
            {
                hasStickinessAbility = true;
            }
        
            // Update material based on current state
            UpdateMaterial();
        }

        private void Update()
        {
            // Toggle with key press during gameplay
            if (Input.GetKeyDown(toggleKey))
            {
                ToggleStickiness();
            }
        }

        private void OnValidate()
        {
            // Update material when inspector value changes (even in editor)
            if (Application.isPlaying)
            {
                UpdateMaterial();
            }
        }

        /// <summary>
        /// Call this when player picks up the sticky box
        /// </summary>
        public void UnlockStickiness()
        {
            if (hasStickinessAbility)
            {
                Debug.Log("[Ability] Stickiness already unlocked!");
                return;
            }

            hasStickinessAbility = true;
            UpdateMaterial();
            Debug.Log("[Ability] ✨ Stickiness power unlocked! Filled areas now slow enemies!");
        
            // You can add visual/audio feedback here
            EventManager.TriggerEvent(EventManager.GameEvent.KeyCollected, this); // Or create a new event
        }

        /// <summary>
        /// Toggle stickiness ability on/off (for testing or gameplay)
        /// </summary>
        [ContextMenu("Toggle Stickiness (Test)")]
        public void ToggleStickiness()
        {
            hasStickinessAbility = !hasStickinessAbility;
            UpdateMaterial();
            Debug.Log($"[Ability] Stickiness: {(hasStickinessAbility ? "ON ✓" : "OFF ✗")}");
        }

        /// <summary>
        /// Updates the cube material based on ability state
        /// </summary>
        private void UpdateMaterial()
        {
            if (cubeRenderer == null)
                return;

            Material targetMaterial = hasStickinessAbility && stickyMaterial != null
                ? stickyMaterial
                : normalMaterial;

            if (targetMaterial != null)
            {
                cubeRenderer.material = targetMaterial;
            }
        }
    }
}

