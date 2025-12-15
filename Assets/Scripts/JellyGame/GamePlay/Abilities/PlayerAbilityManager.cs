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
                Destroy(gameObject);
                return;
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

            UpdateMaterial();
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
        public void OnAreaFilled(SimplePaintSurface surface, System.Collections.Generic.IReadOnlyList<Vector2> localPolyXZ, Bounds localBounds)
        {
            var ability = ActiveAbility;
            bool abilityActive = (ability != null && ability.CanSpawnZone);

            // 1) Apply cost (optional)
            if (areaFillSelfDamage != null)
                areaFillSelfDamage.HandleAreaFilled(surface, localPolyXZ, abilityActive);

            // 2) Spawn zone (if ability supports it)
            if (!abilityActive)
                return;

            if (debugLogs)
                Debug.Log($"[PlayerAbilityManager] OnAreaFilled -> {activeAbilityAsset.name}", this);

            ability.SpawnZone(new AbilityZoneContext(surface, localPolyXZ, localBounds));
        }

        [ContextMenu("Clear Ability")]
        public void ClearAbility()
        {
            activeAbilityAsset = null;
            UpdateMaterial();
        }
    }
}
