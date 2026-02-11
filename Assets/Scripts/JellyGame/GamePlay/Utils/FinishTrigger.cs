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

        [Tooltip("At what point during the grab animation should the player disappear?\n" +
                 "0.0 = immediately, 0.5 = halfway, 1.0 = at the very end.\n" +
                 "The grab animation length is auto-detected from the Animator.")]
        [Range(0f, 1f)]
        [SerializeField] private float hidePlayerAtGrabProgress = 0.7f;

        [Tooltip("Animator bool parameter name to trigger the release animation.")]
        [SerializeField] private string releaseBoolName = "release";

        [Header("Portal Particle Fade")]
        [Tooltip("Root object containing portal particle systems.\n" +
                 "After release animation, emission smoothly fades to 0.")]
        [SerializeField] private GameObject portalParticlesRoot;

        [Tooltip("How long (seconds) it takes for particle emission to smoothly fade from current value to 0.")]
        [SerializeField] private float particleFadeDuration = 2.0f;

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

            // Need to wait one frame for Animators to initialize after SetActive(true)
            yield return null;

            // 4) Get the first valid animator to read animation state from
            Animator refAnimator = GetFirstTeleportSlimeAnimator();

            if (refAnimator == null)
            {
                Debug.LogWarning("[FinishTrigger] No Animator found on teleport slimes! Skipping animation sequence.", this);
            }
            else
            {
                // 5) Wait for grab animation — hide player partway through
                if (debugLogs)
                    Debug.Log($"[FinishTrigger] Waiting for grab animation. Will hide player at {hidePlayerAtGrabProgress:P0} progress.", this);

                // Wait until animator is in the grab state
                yield return WaitForAnimatorStateEnter(refAnimator, "teleport grab");

                // Now wait for it to finish, hiding player at the configured progress
                bool playerHidden = false;
                yield return WaitForAnimatorStateComplete(refAnimator, "teleport grab", hidePlayerAtGrabProgress, () =>
                {
                    if (!playerHidden)
                    {
                        playerHidden = true;
                        HidePlayer();

                        if (debugLogs)
                            Debug.Log("[FinishTrigger] Player hidden (grabbed by teleport slimes).", this);
                    }
                });

                // Make sure player is hidden even if progress callback didn't fire
                if (!playerHidden)
                    HidePlayer();

                if (debugLogs)
                    Debug.Log("[FinishTrigger] Grab animation complete.", this);

                // 6) Set animator bool "release" = true → triggers release/exit animation
                SetTeleportSlimeRelease(true);

                if (debugLogs)
                    Debug.Log("[FinishTrigger] Release animation triggered. Waiting for transition then completion.", this);

                // 7) Wait for the animator to transition INTO the release state,
                //    then set release=false so "Any State → teleport release" doesn't keep re-triggering
                //    (which would restart the animation every frame and prevent it from playing).
                yield return WaitForAnimatorStateEnter(refAnimator, "teleport release");

                // CRITICAL: Clear the bool so the Any State transition stops re-firing
                SetTeleportSlimeRelease(false);

                if (debugLogs)
                    Debug.Log("[FinishTrigger] Entered release state. Cleared release bool. Waiting for animation to finish.", this);

                // 8) Now wait for the release animation to actually finish
                yield return WaitForAnimatorStateComplete(refAnimator, "teleport release");

                if (debugLogs)
                    Debug.Log("[FinishTrigger] Release animation complete.", this);
            }

            // 8) Deactivate teleport slime objects
            DeactivateTeleportSlimes();

            if (debugLogs)
                Debug.Log("[FinishTrigger] Teleport slimes deactivated.", this);

            // 9) Smoothly fade portal particle emission to 0
            if (debugLogs)
                Debug.Log($"[FinishTrigger] Starting particle fade over {particleFadeDuration}s.", this);

            yield return FadePortalParticleEmission(particleFadeDuration);

            // 11) Activate win FX (optional)
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

            // 12) Destroy the player character BEFORE triggering GameWin
            if (destroyPlayerBeforeTransition)
            {
                DestroyAllPlayers();
            }

            // 13) Trigger GameWin → GameSceneManager → LoadingManager starts transition
            if (debugLogs)
                Debug.Log("[FinishTrigger] Triggering GameWin event.", this);

            EventManager.TriggerEvent(EventManager.GameEvent.GameWin, null);
        }

        // ===================== Animation Helpers =====================

        /// <summary>
        /// Gets the first valid Animator from the teleport slime objects.
        /// Used as a reference to track animation progress (all slimes play the same animation).
        /// </summary>
        private Animator GetFirstTeleportSlimeAnimator()
        {
            if (teleportSlimeObjects == null) return null;

            for (int i = 0; i < teleportSlimeObjects.Count; i++)
            {
                if (teleportSlimeObjects[i] == null) continue;

                Animator animator = teleportSlimeObjects[i].GetComponentInChildren<Animator>();
                if (animator != null)
                    return animator;
            }

            return null;
        }

        /// <summary>
        /// Waits until the animator enters the named state (and finishes transitioning into it).
        /// Returns once the animator is fully IN the state. Does NOT wait for it to finish playing.
        /// </summary>
        private IEnumerator WaitForAnimatorStateEnter(Animator animator, string stateName)
        {
            if (animator == null) yield break;

            float safetyTimeout = 10f;
            float elapsed = 0f;
            int stateHash = Animator.StringToHash(stateName);

            while (elapsed < safetyTimeout)
            {
                AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
                
                if (stateInfo.shortNameHash == stateHash && !animator.IsInTransition(0))
                {
                    if (debugLogs)
                        Debug.Log($"[FinishTrigger] Entered state '{stateName}'.", this);
                    yield break;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            if (debugLogs)
                Debug.LogWarning($"[FinishTrigger] Timed out waiting to enter state '{stateName}'!", this);
        }

        /// <summary>
        /// Waits until the named animation state finishes playing (normalizedTime >= 1.0).
        /// Assumes the animator is already in (or entering) the target state.
        /// Optionally fires a callback at a specific progress point.
        /// </summary>
        private IEnumerator WaitForAnimatorStateComplete(Animator animator, string stateName, float callbackProgress = -1f, System.Action onProgressReached = null)
        {
            if (animator == null) yield break;

            float safetyTimeout = 10f;
            float elapsed = 0f;
            bool callbackFired = false;
            int stateHash = Animator.StringToHash(stateName);

            while (elapsed < safetyTimeout)
            {
                AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);

                // If we left the state (e.g. another transition happened), we're done
                if (stateInfo.shortNameHash != stateHash && !animator.IsInTransition(0))
                    break;

                // Only read progress when actually in the target state
                if (stateInfo.shortNameHash == stateHash)
                {
                    float normalizedTime = stateInfo.normalizedTime;

                    // Fire callback at specified progress
                    if (!callbackFired && onProgressReached != null && callbackProgress >= 0f && normalizedTime >= callbackProgress)
                    {
                        callbackFired = true;
                        onProgressReached.Invoke();
                    }

                    // Animation complete
                    if (normalizedTime >= 1f)
                    {
                        if (!callbackFired && onProgressReached != null)
                        {
                            callbackFired = true;
                            onProgressReached.Invoke();
                        }
                        yield break;
                    }
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            // Fire callback if it never fired
            if (!callbackFired && onProgressReached != null)
                onProgressReached.Invoke();

            if (elapsed >= safetyTimeout && debugLogs)
                Debug.LogWarning($"[FinishTrigger] Animation '{stateName}' timed out after {safetyTimeout}s!", this);
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
        /// Smoothly fades emission rates on all ParticleSystems under portalParticlesRoot from their
        /// current values down to 0 over the specified duration, then stops the systems.
        /// </summary>
        private IEnumerator FadePortalParticleEmission(float duration)
        {
            if (portalParticlesRoot == null) yield break;

            ParticleSystem[] systems = portalParticlesRoot.GetComponentsInChildren<ParticleSystem>(true);
            if (systems.Length == 0) yield break;

            // Capture starting emission values for each particle system
            float[] startRateOverTime = new float[systems.Length];
            float[] startRateOverDistance = new float[systems.Length];
            float[][] startBurstCounts = new float[systems.Length][];

            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] == null) continue;

                var emission = systems[i].emission;

                // Read the effective rate (handles Constant, Curve, RandomBetweenTwoConstants, etc.)
                var rotCurve = emission.rateOverTime;
                startRateOverTime[i] = rotCurve.mode == ParticleSystemCurveMode.Constant
                    ? rotCurve.constant
                    : rotCurve.constantMax;

                var rodCurve = emission.rateOverDistance;
                startRateOverDistance[i] = rodCurve.mode == ParticleSystemCurveMode.Constant
                    ? rodCurve.constant
                    : rodCurve.constantMax;

                // Capture burst counts
                int burstCount = emission.burstCount;
                startBurstCounts[i] = new float[burstCount];
                for (int b = 0; b < burstCount; b++)
                {
                    var burst = emission.GetBurst(b);
                    startBurstCounts[i][b] = burst.count.mode == ParticleSystemCurveMode.Constant
                        ? burst.count.constant
                        : burst.count.constantMax;
                }

                if (debugLogs)
                    Debug.Log($"[FinishTrigger] Particle '{systems[i].name}': rateOverTime={startRateOverTime[i]}, rateOverDistance={startRateOverDistance[i]}, bursts={burstCount}", this);
            }

            // Lerp everything to 0 over duration
            float elapsed = 0f;
            float fadeDur = Mathf.Max(0.01f, duration);

            while (elapsed < fadeDur)
            {
                float t = elapsed / fadeDur;

                for (int i = 0; i < systems.Length; i++)
                {
                    if (systems[i] == null) continue;

                    var emission = systems[i].emission;
                    emission.rateOverTime = Mathf.Lerp(startRateOverTime[i], 0f, t);
                    emission.rateOverDistance = Mathf.Lerp(startRateOverDistance[i], 0f, t);

                    // Fade bursts too
                    for (int b = 0; b < startBurstCounts[i].Length; b++)
                    {
                        var burst = emission.GetBurst(b);
                        burst.count = Mathf.Lerp(startBurstCounts[i][b], 0f, t);
                        emission.SetBurst(b, burst);
                    }
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            // Ensure final values are exactly 0 and stop all systems
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] == null) continue;

                var emission = systems[i].emission;
                emission.rateOverTime = 0f;
                emission.rateOverDistance = 0f;

                for (int b = 0; b < emission.burstCount; b++)
                {
                    var burst = emission.GetBurst(b);
                    burst.count = 0;
                    emission.SetBurst(b, burst);
                }

                // Stop emitting but let remaining particles finish their lifetime
                systems[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            if (debugLogs)
                Debug.Log("[FinishTrigger] Particle emission fade complete. Systems stopped.", this);

            // Wait a bit for remaining alive particles to die out naturally
            yield return new WaitForSeconds(1.5f);
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
        /// Destroys the player immediately.
        /// Called after the grab animation makes the player "disappear".
        /// Using DestroyImmediate so it's truly gone — prevents singleton conflicts
        /// when the next scene's slime runs Awake().
        /// </summary>
        private void HidePlayer()
        {
            GameObject[] players = GameObject.FindGameObjectsWithTag(playerTag);

            foreach (GameObject player in players)
            {
                if (player != null)
                {
                    if (debugLogs)
                        Debug.Log($"[FinishTrigger] Destroying player: {player.name}", this);

                    DestroyImmediate(player);
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

                    // DestroyImmediate so the player is truly gone NOW.
                    // Deferred Destroy() leaves it alive for the rest of the frame,
                    // so if the next scene's slime has a singleton check in Awake(),
                    // it finds the old one and destroys itself — both slimes end up gone.
                    DestroyImmediate(player);
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