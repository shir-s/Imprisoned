// FILEPATH: Assets/Scripts/Abilities/PlayerAbilityManager.cs
using JellyGame.GamePlay.Abilities.Zones;
using JellyGame.GamePlay.Map.Surfaces;
using JellyGame.GamePlay.Player;
using UnityEngine;

namespace JellyGame.GamePlay.Abilities
{
    /// <summary>
    /// Tracks current active player ability.
    /// Abilities can optionally react to filled shapes by spawning zones.
    /// </summary>
    public class PlayerAbilityManager : MonoBehaviour
    {
        public static PlayerAbilityManager Instance { get; private set; }

        [Header("Active Ability")]
        [SerializeField] private ScriptableObject activeAbilityAsset; // should implement IPlayerAbility

        [Header("Available Abilities")]
        [SerializeField] private ScriptableObject damageAbilityAsset; // Damage Zone Ability
        [SerializeField] private ScriptableObject stickyAbilityAsset; // Sticky Zone Ability

        [Header("Input")]
        [Tooltip("Key to press to switch between abilities")]
        [SerializeField] private KeyCode switchAbilityKey = KeyCode.LeftControl;

        [Header("Filled Area Cost (optional)")]
        [Tooltip("If assigned, the player will take self-damage based on the filled area size.")]
        [SerializeField] private AreaFillSelfDamage areaFillSelfDamage;

        [Header("Fallback Paint Colors")]
        [SerializeField] private Color normalPaintColor = Color.black;

        [Header("Visual Feedback (optional)")]
        [SerializeField] private Renderer cubeRenderer;
        [SerializeField] private Material normalMaterial;
        [SerializeField] private Material abilityMaterial;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        public IPlayerAbility ActiveAbility => activeAbilityAsset as IPlayerAbility;

        public Color CurrentPaintColor
        {
            get
            {
                var a = ActiveAbility;
                if (a != null)
                    return a.PaintColor;

                return normalPaintColor;
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // FIX: Only destroy self if the existing instance is actually alive and active.
                // During scene transitions, an orphaned/deactivated instance from another scene
                // may still be referenced. In that case, replace it instead of destroying ourselves.
                if (Instance.gameObject.activeInHierarchy)
                {
                    Destroy(gameObject);
                    return;
                }
        
                // Existing instance is dead/inactive — take over
            }

            Instance = this;

            if (cubeRenderer == null)
            {
                cubeRenderer = GetComponent<Renderer>();
                if (cubeRenderer == null)
                    cubeRenderer = GetComponentInChildren<Renderer>();
            }

            if (areaFillSelfDamage == null)
                areaFillSelfDamage = GetComponent<AreaFillSelfDamage>();

            // Initialize with damage ability if activeAbilityAsset is not set
            if (activeAbilityAsset == null && damageAbilityAsset != null)
            {
                activeAbilityAsset = damageAbilityAsset;
            }

            UpdateMaterial();
        }

        private void Update()
        {
            // Check for ability switch input
            if (Input.GetKeyDown(switchAbilityKey))
            {
                SwitchAbility();
            }
        }

        private void OnValidate()
        {
            if (Application.isPlaying)
                UpdateMaterial();
        }

        private void UpdateMaterial()
        {
            if (cubeRenderer == null)
                return;

            Material target = (ActiveAbility != null && abilityMaterial != null)
                ? abilityMaterial
                : normalMaterial;

            if (target != null)
                cubeRenderer.material = target;
        }

        /// <summary>
        /// Called by AreaFillShapeDetector when a closed area was detected + filled.
        /// </summary>
        public void OnAreaFilled(
            SimplePaintSurface surface,
            System.Collections.Generic.IReadOnlyList<Vector2> localFillPolyXZ,
            System.Collections.Generic.IReadOnlyList<Vector2> localColliderPolyXZ,
            Bounds localColliderBounds)
        {
            var ability = ActiveAbility;
            bool abilityActive = (ability != null && ability.CanSpawnZone);

            // 1) Apply cost (optional) - uses fill polygon (smaller)
            if (areaFillSelfDamage != null)
                areaFillSelfDamage.HandleAreaFilled(surface, localFillPolyXZ, abilityActive);

            // 2) Spawn zone (if ability supports it) - uses collider polygon (larger)
            if (!abilityActive)
                return;

            if (debugLogs)
                Debug.Log($"[PlayerAbilityManager] OnAreaFilled -> {activeAbilityAsset.name}", this);

            var zonePoly = localColliderPolyXZ != null && localColliderPolyXZ.Count >= 3
                ? localColliderPolyXZ
                : localFillPolyXZ;

            ability.SpawnZone(new AbilityZoneContext(surface, zonePoly, localColliderBounds));
        }

        /// <summary>
        /// Switches between damage and sticky abilities.
        /// </summary>
        private void SwitchAbility()
        {
            // Determine current ability
            bool isCurrentlyDamage = activeAbilityAsset == damageAbilityAsset;
            
            // Switch to the other ability
            if (isCurrentlyDamage)
            {
                // Switch to sticky
                if (stickyAbilityAsset != null)
                {
                    activeAbilityAsset = stickyAbilityAsset;
                    Debug.Log("[PlayerAbilityManager] Switched ability to Stickiness Zone", this);
                }
                else
                {
                    Debug.LogWarning("[PlayerAbilityManager] Sticky Ability not assigned!", this);
                }
            }
            else
            {
                // Switch to damage (or default to damage if no ability is set)
                if (damageAbilityAsset != null)
                {
                    activeAbilityAsset = damageAbilityAsset;
                    Debug.Log("[PlayerAbilityManager] Switched ability to Damage Enemy Zone", this);
                }
                else
                {
                    Debug.LogWarning("[PlayerAbilityManager] Damage Ability not assigned!", this);
                }
            }
            
            UpdateMaterial();
        }

        [ContextMenu("Clear Ability")]
        public void ClearAbility()
        {
            activeAbilityAsset = null;
            UpdateMaterial();
        }
    }
}
