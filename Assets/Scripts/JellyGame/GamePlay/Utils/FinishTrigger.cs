// FILEPATH: Assets/Scripts/World/Finish/FinishTrigger.cs
using System.Collections;
using System.Collections.Generic;
using JellyGame.GamePlay.Audio.Core;
using JellyGame.GamePlay.Managers;
using UnityEngine;

namespace JellyGame.GamePlay.World.Finish
{
    /// <summary>
    /// Triggers GameWin when player enters the trigger AND all enemies are dead.
    /// Listens to EventManager.GameEvent.EntityDied.
    /// 
    /// Win sequence:
    /// 1. Player enters portal → player frozen (scripts disabled, still visible)
    /// 2. Teleport slime objects activate → auto-play grab animation
    /// 3. After grab delay → player hidden (SetActive false)
    /// 4. After release delay → set animator bool "release" = true (release animation)
    /// 5. After release animation → deactivate teleport slime objects
    /// 6. Particle emission set to 0 → portal fades away gradually
    /// 7. Player destroyed → GameWin event fires → LoadingManager starts transition
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class FinishTrigger : MonoBehaviour
    {
        [Header("Trigger")]
        [Tooltip("Only objects on these layers can trigger GameWin. If 0 (Nothing), will accept all layers.")]
        [SerializeField] private LayerMask allowedLayers;

        [Tooltip("If true, trigger only once.")]
        [SerializeField] private bool triggerOnce = true;

        [Header("Win Condition")]
        [Tooltip("Only deaths on these layers will be counted.")]
        [SerializeField] private LayerMask countLayers = ~0;

        [Tooltip("Number of enemy deaths required before trigger can activate GameWin.")]
        [SerializeField] private int requiredDeaths = 4;

        [Header("Win Sequence - Player")]
        [Tooltip("Destroy the player character before triggering GameWin.\n" +
                 "Prevents duplicate-player issues when the new scene spawns its own player.")]
        [SerializeField] private bool destroyPlayerBeforeTransition = true;

        [Tooltip("Tag used to find the player character for destruction.")]
        [SerializeField] private string playerTag = "DrawingCube";

        [Header("Teleport Slime Animation")]
        [Tooltip("The 4 teleport slime parent objects (each has a child with an Animator).\n" +
                 "They start deactivated. On player enter they activate and auto-play intro animation.\n" +
                 "Then 'release' bool is set to true for the exit animation.")]
        [SerializeField] private List<GameObject> teleportSlimeObjects = new List<GameObject>();

        [Tooltip("Delay (seconds) after player enters before activating teleport slimes.\n" +
                 "Lets the player visually settle inside the portal.")]
        [SerializeField] private float slimeActivateDelay = 0.2f;

        [Tooltip("Delay (seconds) after teleport slimes activate before setting release = true.\n" +
                 "This is how long the grab animation plays.")]
        [SerializeField] private float releaseDelay = 1.0f;

        [Tooltip("Delay (seconds) after grab animation starts before hiding the player.\n" +
                 "Should be <= releaseDelay. The player stays visible during the grab, then disappears.")]
        [SerializeField] private float hidePlayerAfterGrabDelay = 0.5f;

        [Tooltip("Animator bool parameter name to trigger the release animation.")]
        [SerializeField] private string releaseBoolName = "release";

        [Tooltip("Delay (seconds) after setting release = true before deactivating teleport slimes.\n" +
                 "This is how long the release/exit animation plays.")]
        [SerializeField] private float deactivateAfterReleaseDelay = 1.0f;

        [Header("Portal Particle Fade")]
        [Tooltip("Root object containing portal particle systems.\n" +
                 "After release animation, emission rate will be set to 0 so particles fade out naturally.")]
        [SerializeField] private GameObject portalParticlesRoot;

        [Tooltip("Delay (seconds) after setting emission to 0 before triggering GameWin.\n" +
                 "Lets the remaining particles die out for a fade effect.")]
        [SerializeField] private float particleFadeDelay = 2.0f;

        [Header("Win FX (optional)")]
        [Tooltip("Root object that contains particle systems to play on win. Will be SetActive(true) before GameWin.")]
        [SerializeField] private GameObject winFxRoot;

        [Tooltip("Delay (seconds) after activating Win FX before triggering GameWin.")]
        [SerializeField] private float winFxDelaySeconds = 0.35f;

        [Tooltip("If true, tries to Play() all ParticleSystems under winFxRoot when activated.")]
        [SerializeField] private bool playParticleSystemsOnWin = true;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private bool _triggered;
        private int _deathCount = 0;

        private void Reset()
        {
            Collider c = GetComponent<Collider>();
            if (c != null)
                c.isTrigger = true;
        }

        private void Awake()
        {
            EnsureKinematicRigidbody();

            // Hide portal particles until win condition is met
            UpdatePortalVisibility(IsWinConditionMet());

            // Deactivate teleport slimes on start
            DeactivateTeleportSlimes();

            if (winFxRoot != null)
                winFxRoot.SetActive(false);

            if (debugLogs)
            {
                Debug.Log(
                    $"[FinishTrigger] Initialized. Required Deaths: {requiredDeaths}, Count Layers: {countLayers.value}, Allowed Layers: {allowedLayers.value}",
                    this
                );
            }
        }

        private void OnEnable()
        {
            EventManager.StartListening(EventManager.GameEvent.EntityDied, OnEntityDied);

            var counter = FindObjectOfType<JellyGame.GamePlay.Utils.EnemyDeathCounter>();
            if (counter != null)
            {
                int counterDeaths = counter.GetDeathCount();
                if (counterDeaths > _deathCount)
                    _deathCount = counterDeaths;
            }

            UpdatePortalVisibility(IsWinConditionMet());

            if (debugLogs)
                Debug.Log($"[FinishTrigger] OnEnable catch-up: deaths={_deathCount}/{requiredDeaths} (allowedLayers={allowedLayers.value}, countLayers={countLayers.value})", this);
        }

        private void OnDisable()
        {
            EventManager.StopListening(EventManager.GameEvent.EntityDied, OnEntityDied);
        }

        private void OnEntityDied(object eventData)
        {
            if (_triggered)
                return;

            if (eventData is not EntityDiedEventData e)
                return;

            int layer = e.VictimLayer;

            if ((countLayers.value & (1 << layer)) == 0)
            {
                if (debugLogs)
                    Debug.Log($"[FinishTrigger] Ignored death (layer={layer} '{LayerMask.LayerToName(layer)}') because it's not in countLayers ({countLayers.value}). Victim={e.Victim?.name}", this);
                return;
            }

            _deathCount++;

            if (debugLogs)
                Debug.Log($"[FinishTrigger] Counted death {_deathCount}/{requiredDeaths} (layer={layer} '{LayerMask.LayerToName(layer)}') Victim={e.Victim?.name}", this);

            if (IsWinConditionMet())
                UpdatePortalVisibility(true);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (debugLogs)
                Debug.Log($"[FinishTrigger] OnTriggerEnter called by: {other.name} (Layer: {other.gameObject.layer})", this);

            if (_triggered && triggerOnce)
            {
                if (debugLogs)
                    Debug.Log("[FinishTrigger] Already triggered and triggerOnce is true. Ignoring.", this);
                return;
            }

            int layerA = other.gameObject.layer;
            int layerB = other.attachedRigidbody != null ? other.attachedRigidbody.gameObject.layer : layerA;
            int layerC = other.transform.root != null ? other.transform.root.gameObject.layer : layerA;

            bool allowed =
                allowedLayers.value == 0 ||
                (allowedLayers.value & (1 << layerA)) != 0 ||
                (allowedLayers.value & (1 << layerB)) != 0 ||
                (allowedLayers.value & (1 << layerC)) != 0;

            if (!allowed)
            {
                if (debugLogs)
                    Debug.Log($"[FinishTrigger] Object {other.name} rejected by allowedLayers ({allowedLayers.value}). layers: collider={layerA}, rb={layerB}, root={layerC}", this);
                return;
            }

            if (!IsWinConditionMet())
            {
                if (debugLogs)
                    Debug.Log($"[FinishTrigger] Player entered trigger, but only {_deathCount}/{requiredDeaths} enemies are dead. Waiting...", this);
                return;
            }

            _triggered = true;

            if (debugLogs)
                Debug.Log($"[FinishTrigger] ✓ Win sequence started by {other.name} ({_deathCount}/{requiredDeaths} enemies dead)", this);

            SoundManager.Instance.StopAllSounds();
            SoundManager.Instance.PlaySound("Win", this.transform);

            StartCoroutine(GameWinEvent(other));
        }
        
        private bool IsWinConditionMet()
        {
            if (requiredDeaths <= 0)
                return true;

            return _deathCount >= requiredDeaths;
        }

        private IEnumerator GameWinEvent(Collider other)
        {
            // 1) Freeze the player — disable all scripts but keep visible inside the portal
            FreezePlayer();

            // 2) Wait a moment for the player to settle visually
            if (slimeActivateDelay > 0f)
                yield return new WaitForSeconds(slimeActivateDelay);

            // 3) Activate teleport slime objects — they auto-play their grab animation
            ActivateTeleportSlimes();

            if (debugLogs)
                Debug.Log($"[FinishTrigger] Teleport slimes activated (grab). hidePlayerAfterGrabDelay={hidePlayerAfterGrabDelay}s, releaseDelay={releaseDelay}s", this);

            // 4) Wait for grab animation, then hide the player mid-grab
            float hideDelay = Mathf.Max(0f, hidePlayerAfterGrabDelay);
            if (hideDelay > 0f)
                yield return new WaitForSeconds(hideDelay);

            // 5) Hide the player — it disappears as if "grabbed" by the teleport slimes
            HidePlayer();

            if (debugLogs)
                Debug.Log("[FinishTrigger] Player hidden (grabbed by teleport slimes).", this);

            // 6) Wait the remaining time before triggering release animation
            float remainingBeforeRelease = Mathf.Max(0f, releaseDelay - hideDelay);
            if (remainingBeforeRelease > 0f)
                yield return new WaitForSeconds(remainingBeforeRelease);

            // 7) Set animator bool "release" = true → triggers release/exit animation
            SetTeleportSlimeRelease(true);

            if (debugLogs)
                Debug.Log($"[FinishTrigger] Release animation triggered. Waiting {deactivateAfterReleaseDelay}s.", this);

            // 8) Wait for release animation to finish
            if (deactivateAfterReleaseDelay > 0f)
                yield return new WaitForSeconds(deactivateAfterReleaseDelay);

            // 9) Deactivate teleport slime objects
            DeactivateTeleportSlimes();

            if (debugLogs)
                Debug.Log("[FinishTrigger] Teleport slimes deactivated.", this);

            // 10) Set portal particle emission to 0 → particles fade out naturally
            SetPortalParticleEmissionToZero();

            if (debugLogs)
                Debug.Log($"[FinishTrigger] Portal particle emission set to 0. Waiting {particleFadeDelay}s for fade.", this);

            // 11) Wait for particles to fade out
            if (particleFadeDelay > 0f)
                yield return new WaitForSeconds(particleFadeDelay);

            // 12) Activate win FX (optional)
            if (winFxRoot != null)
            {
                winFxRoot.SetActive(true);

                if (playParticleSystemsOnWin)
                {
                    var ps = winFxRoot.GetComponentsInChildren<ParticleSystem>(true);
                    for (int i = 0; i < ps.Length; i++)
                    {
                        ps[i].Clear(true);
                        ps[i].Play(true);
                    }
                }

                if (debugLogs)
                    Debug.Log($"[FinishTrigger] Win FX activated: '{winFxRoot.name}', delay={winFxDelaySeconds:0.###}s", this);

                if (winFxDelaySeconds > 0f)
                    yield return new WaitForSeconds(winFxDelaySeconds);
                else
                    yield return null;
            }

            // 13) Destroy the player character BEFORE triggering GameWin
            if (destroyPlayerBeforeTransition)
            {
                DestroyAllPlayers();
            }

            // 14) Trigger GameWin → GameSceneManager → LoadingManager starts transition
            if (debugLogs)
                Debug.Log("[FinishTrigger] Triggering GameWin event.", this);

            EventManager.TriggerEvent(EventManager.GameEvent.GameWin, null);
        }

        // ===================== Portal Visibility =====================

        /// <summary>
        /// Shows or hides the portal particles root based on whether the win condition is met.
        /// Replaces the old mesh-based finish visuals.
        /// </summary>
        private void UpdatePortalVisibility(bool visible)
        {
            if (portalParticlesRoot != null)
                portalParticlesRoot.SetActive(visible);
        }

        // ===================== Teleport Slime Helpers =====================

        private void ActivateTeleportSlimes()
        {
            if (teleportSlimeObjects == null) return;

            for (int i = 0; i < teleportSlimeObjects.Count; i++)
            {
                if (teleportSlimeObjects[i] != null)
                {
                    teleportSlimeObjects[i].SetActive(true);

                    if (debugLogs)
                        Debug.Log($"[FinishTrigger] Activated teleport slime: {teleportSlimeObjects[i].name}", this);
                }
            }
        }

        private void DeactivateTeleportSlimes()
        {
            if (teleportSlimeObjects == null) return;

            for (int i = 0; i < teleportSlimeObjects.Count; i++)
            {
                if (teleportSlimeObjects[i] != null)
                    teleportSlimeObjects[i].SetActive(false);
            }
        }

        /// <summary>
        /// Sets the release bool on all Animator components found in teleport slime children.
        /// Each teleport slime parent has a child with an Animator.
        /// </summary>
        private void SetTeleportSlimeRelease(bool value)
        {
            if (teleportSlimeObjects == null) return;

            for (int i = 0; i < teleportSlimeObjects.Count; i++)
            {
                if (teleportSlimeObjects[i] == null) continue;

                Animator animator = teleportSlimeObjects[i].GetComponentInChildren<Animator>();
                if (animator != null)
                {
                    animator.SetBool(releaseBoolName, value);

                    if (debugLogs)
                        Debug.Log($"[FinishTrigger] Set '{releaseBoolName}'={value} on animator: {animator.gameObject.name}", this);
                }
                else if (debugLogs)
                {
                    Debug.LogWarning($"[FinishTrigger] No Animator found in children of: {teleportSlimeObjects[i].name}", this);
                }
            }
        }

        // ===================== Portal Particle Fade =====================

        /// <summary>
        /// Sets emission rate to 0 on all ParticleSystems under portalParticlesRoot.
        /// Existing particles will finish their lifetime naturally, creating a fade-out effect.
        /// </summary>
        private void SetPortalParticleEmissionToZero()
        {
            if (portalParticlesRoot == null) return;

            ParticleSystem[] systems = portalParticlesRoot.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] == null) continue;

                var emission = systems[i].emission;
                emission.rateOverTime = 0f;
                emission.rateOverDistance = 0f;

                // Also disable burst emissions
                for (int b = 0; b < emission.burstCount; b++)
                {
                    var burst = emission.GetBurst(b);
                    burst.count = 0;
                    emission.SetBurst(b, burst);
                }

                if (debugLogs)
                    Debug.Log($"[FinishTrigger] Emission set to 0 on particle: {systems[i].name}", this);
            }
        }

        // ===================== Player Helpers =====================

        /// <summary>
        /// Freezes the player in place by disabling all MonoBehaviour scripts.
        /// The player remains visible (renderers stay active) so it looks like it's inside the portal.
        /// </summary>
        private void FreezePlayer()
        {
            GameObject[] players = GameObject.FindGameObjectsWithTag(playerTag);

            foreach (GameObject player in players)
            {
                if (player == null) continue;

                // Disable all MonoBehaviours (movement, input, etc.) but keep renderers
                MonoBehaviour[] scripts = player.GetComponentsInChildren<MonoBehaviour>();
                foreach (MonoBehaviour script in scripts)
                {
                    if (script != null)
                        script.enabled = false;
                }

                if (debugLogs)
                    Debug.Log($"[FinishTrigger] Froze player (disabled scripts): {player.name}", this);
            }
        }

        /// <summary>
        /// Hides the player by deactivating the GameObject.
        /// Called after the grab animation makes the player "disappear".
        /// </summary>
        private void HidePlayer()
        {
            GameObject[] players = GameObject.FindGameObjectsWithTag(playerTag);

            foreach (GameObject player in players)
            {
                if (player != null)
                {
                    player.SetActive(false);

                    if (debugLogs)
                        Debug.Log($"[FinishTrigger] Hid player: {player.name}", this);
                }
            }
        }

        /// <summary>
        /// Destroy all player characters in the current scene.
        /// Uses the same tag-based approach as LoadingManager.DestroyPlayersInScene.
        /// </summary>
        private void DestroyAllPlayers()
        {
            GameObject[] players = GameObject.FindGameObjectsWithTag(playerTag);

            foreach (GameObject player in players)
            {
                if (player != null)
                {
                    if (debugLogs)
                        Debug.Log($"[FinishTrigger] Destroying player: {player.name}", this);

                    Destroy(player);
                }
            }

            if (debugLogs && (players == null || players.Length == 0))
                Debug.LogWarning($"[FinishTrigger] No objects found with tag '{playerTag}' to destroy.", this);
        }

        private void EnsureKinematicRigidbody()
        {
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb == null)
                rb = gameObject.AddComponent<Rigidbody>();

            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.None;
        }
    }
}