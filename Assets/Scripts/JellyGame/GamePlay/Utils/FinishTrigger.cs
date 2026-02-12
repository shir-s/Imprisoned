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
        public enum WinConditionMode
        {
            KillEnemies,    // Standard mode: Player must kill X enemies
            ExternalEvent   // New mode: Portal opens only when the function is called externally
        }

        [Header("Win Settings")]
        [Tooltip("Choose how the portal is unlocked.")]
        [SerializeField] private WinConditionMode winMode = WinConditionMode.KillEnemies;

        [Header("Trigger")]
        [Tooltip("Only objects on these layers can trigger GameWin. If 0 (Nothing), will accept all layers.")]
        [SerializeField] private LayerMask allowedLayers;

        [Tooltip("If true, trigger only once.")]
        [SerializeField] private bool triggerOnce = true;

        [Header("Win Condition (Enemies Mode)")]
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

        [Tooltip("Additional objects to destroy alongside the player (e.g. slime prime in cutscene scenes).\n" +
                 "These get DestroyImmediate'd at the same time as the player during the grab animation.")]
        [SerializeField] private List<GameObject> additionalObjectsToDestroyWithPlayer = new List<GameObject>();

        [Header("Teleport Slime Animation")]
        [SerializeField] private List<GameObject> teleportSlimeObjects = new List<GameObject>();
        [SerializeField] private float slimeActivateDelay = 0.2f;
        [Range(0f, 1f)]
        [SerializeField] private float hidePlayerAtGrabProgress = 0.7f;
        [SerializeField] private string releaseBoolName = "release";

        [Header("Portal & Extra Object")]
        [SerializeField] private GameObject portalParticlesRoot;
        [Tooltip("Object (e.g. key) hidden at start, activated when all enemies are dead, same as portal.")]
        [SerializeField] private GameObject extraObjectToActivate;
        [SerializeField] private float particleFadeDuration = 2.0f;

        [Header("Win FX (optional)")]
        [SerializeField] private GameObject winFxRoot;
        [SerializeField] private float winFxDelaySeconds = 0.35f;
        [SerializeField] private bool playParticleSystemsOnWin = true;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private bool _triggered;
        private int _deathCount = 0;
        
        // Track if the external event has been triggered
        private bool _manualEventMet = false;

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
                    $"[FinishTrigger] Initialized. Mode: {winMode}, Required Deaths: {requiredDeaths}",
                    this
                );
            }
        }

        private void OnEnable()
        {
            // Listen for enemy deaths only if we are in 'KillEnemies' mode
            if (winMode == WinConditionMode.KillEnemies)
            {
                EventManager.StartListening(EventManager.GameEvent.EntityDied, OnEntityDied);

                var counter = FindObjectOfType<JellyGame.GamePlay.Utils.EnemyDeathCounter>();
                if (counter != null)
                {
                    int counterDeaths = counter.GetDeathCount();
                    if (counterDeaths > _deathCount)
                        _deathCount = counterDeaths;
                }
            }

            if (winMode == WinConditionMode.ExternalEvent)
            {
                EventManager.StartListening(EventManager.GameEvent.PortalLvl3, UnlockManualCondition);
            }

            UpdatePortalVisibility(IsWinConditionMet());
        }

        private void OnDisable()
        {
            if (winMode == WinConditionMode.KillEnemies)
            {
                EventManager.StopListening(EventManager.GameEvent.EntityDied, OnEntityDied);
            }
            if (winMode == WinConditionMode.ExternalEvent)
            {
                EventManager.StopListening(EventManager.GameEvent.PortalLvl3, UnlockManualCondition);
            }
        }

        /// <summary>
        /// Call this function externally to unlock the portal when WinMode is set to 'ExternalEvent'.
        /// </summary>
        public void UnlockManualCondition(object eventData)
        {
            if (winMode != WinConditionMode.ExternalEvent)
            {
                if (debugLogs) Debug.LogWarning("[FinishTrigger] UnlockManualCondition called, but mode is NOT ExternalEvent.", this);
                return;
            }

            if (_manualEventMet) return; // Already unlocked

            _manualEventMet = true;
            
            if (debugLogs) Debug.Log("[FinishTrigger] Manual external condition met!", this);

            UpdatePortalVisibility(true);
        }

        private void OnEntityDied(object eventData)
        {
            if (_triggered) return;
            
            // If in ExternalEvent mode, ignore enemy deaths
            if (winMode == WinConditionMode.ExternalEvent) return;

            if (eventData is not EntityDiedEventData e) return;

            int layer = e.VictimLayer;

            if ((countLayers.value & (1 << layer)) == 0)
            {
                return;
            }

            _deathCount++;

            if (debugLogs)
                Debug.Log($"[FinishTrigger] Counted death {_deathCount}/{requiredDeaths}", this);

            if (IsWinConditionMet())
                UpdatePortalVisibility(true);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_triggered && triggerOnce) return;

            int layerA = other.gameObject.layer;
            int layerB = other.attachedRigidbody != null ? other.attachedRigidbody.gameObject.layer : layerA;
            int layerC = other.transform.root != null ? other.transform.root.gameObject.layer : layerA;

            bool allowed =
                allowedLayers.value == 0 ||
                (allowedLayers.value & (1 << layerA)) != 0 ||
                (allowedLayers.value & (1 << layerB)) != 0 ||
                (allowedLayers.value & (1 << layerC)) != 0;

            if (!allowed) return;

            if (!IsWinConditionMet())
            {
                if (debugLogs)
                    Debug.Log($"[FinishTrigger] Player entered, but Win Condition ({winMode}) not met yet.", this);
                return;
            }

            _triggered = true;

            if (debugLogs)
                Debug.Log($"[FinishTrigger] ✓ Win sequence started by {other.name}", this);

            SoundManager.Instance.StopAllSounds();
            SoundManager.Instance.PlaySound("Win", this.transform);

            StartCoroutine(GameWinEvent(other));
        }

        public void ForceActivatePortal()
        {
            if (_triggered && triggerOnce) return;

            _triggered = true;
            SoundManager.Instance.StopAllSounds();
            SoundManager.Instance.PlaySound("Win", this.transform);

            StartCoroutine(GameWinEvent(null));
        }
        
        private bool IsWinConditionMet()
        {
            // Check based on the selected mode
            if (winMode == WinConditionMode.ExternalEvent)
            {
                return _manualEventMet;
            }
            else // Default: KillEnemies
            {
                if (requiredDeaths <= 0) return true;
                return _deathCount >= requiredDeaths;
            }
        }

        private IEnumerator GameWinEvent(Collider other)
        {
            FreezePlayer();

            if (slimeActivateDelay > 0f)
                yield return new WaitForSeconds(slimeActivateDelay);

            ActivateTeleportSlimes();
            yield return null;

            Animator refAnimator = GetFirstTeleportSlimeAnimator();

            if (refAnimator != null)
            {
                yield return WaitForAnimatorStateEnter(refAnimator, "teleport grab");

                bool playerHidden = false;
                yield return WaitForAnimatorStateComplete(refAnimator, "teleport grab", hidePlayerAtGrabProgress, () =>
                {
                    if (!playerHidden)
                    {
                        playerHidden = true;
                        HidePlayer();
                    }
                });

                if (!playerHidden) HidePlayer();

                SetTeleportSlimeRelease(true);
                yield return WaitForAnimatorStateEnter(refAnimator, "teleport release");

                SetTeleportSlimeRelease(false);
                yield return WaitForAnimatorStateComplete(refAnimator, "teleport release");
            }

            DeactivateTeleportSlimes();

            yield return FadePortalParticleEmission(particleFadeDuration);

            if (winFxRoot != null)
            {
                winFxRoot.SetActive(true);
                if (playParticleSystemsOnWin)
                {
                    var ps = winFxRoot.GetComponentsInChildren<ParticleSystem>(true);
                    for (int i = 0; i < ps.Length; i++) { ps[i].Clear(true); ps[i].Play(true); }
                }
                
                if (winFxDelaySeconds > 0f) yield return new WaitForSeconds(winFxDelaySeconds);
                else yield return null;
            }

            if (destroyPlayerBeforeTransition)
            {
                DestroyAllPlayers();
            }

            EventManager.TriggerEvent(EventManager.GameEvent.GameWin, null);
        }

        // ===================== Helpers =====================

        private Animator GetFirstTeleportSlimeAnimator()
        {
            if (teleportSlimeObjects == null) return null;
            for (int i = 0; i < teleportSlimeObjects.Count; i++)
            {
                if (teleportSlimeObjects[i] == null) continue;
                Animator animator = teleportSlimeObjects[i].GetComponentInChildren<Animator>();
                if (animator != null) return animator;
            }
            return null;
        }

        private IEnumerator WaitForAnimatorStateEnter(Animator animator, string stateName)
        {
            if (animator == null) yield break;
            float safetyTimeout = 10f;
            float elapsed = 0f;
            int stateHash = Animator.StringToHash(stateName);

            while (elapsed < safetyTimeout)
            {
                AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
                if (stateInfo.shortNameHash == stateHash && !animator.IsInTransition(0)) yield break;
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

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
                if (stateInfo.shortNameHash != stateHash && !animator.IsInTransition(0)) break;

                if (stateInfo.shortNameHash == stateHash)
                {
                    float normalizedTime = stateInfo.normalizedTime;
                    if (!callbackFired && onProgressReached != null && callbackProgress >= 0f && normalizedTime >= callbackProgress)
                    {
                        callbackFired = true;
                        onProgressReached.Invoke();
                    }
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
            if (!callbackFired && onProgressReached != null) onProgressReached.Invoke();
        }

        private void UpdatePortalVisibility(bool visible)
        {
            if (portalParticlesRoot != null)
                portalParticlesRoot.SetActive(visible);
            if (extraObjectToActivate != null)
                extraObjectToActivate.SetActive(visible);
        }

        private void ActivateTeleportSlimes()
        {
            if (teleportSlimeObjects == null) return;
            for (int i = 0; i < teleportSlimeObjects.Count; i++)
            {
                if (teleportSlimeObjects[i] != null) teleportSlimeObjects[i].SetActive(true);
            }
        }

        private void DeactivateTeleportSlimes()
        {
            if (teleportSlimeObjects == null) return;
            for (int i = 0; i < teleportSlimeObjects.Count; i++)
            {
                if (teleportSlimeObjects[i] != null) teleportSlimeObjects[i].SetActive(false);
            }
        }

        private void SetTeleportSlimeRelease(bool value)
        {
            if (teleportSlimeObjects == null) return;
            for (int i = 0; i < teleportSlimeObjects.Count; i++)
            {
                if (teleportSlimeObjects[i] == null) continue;
                Animator animator = teleportSlimeObjects[i].GetComponentInChildren<Animator>();
                if (animator != null) animator.SetBool(releaseBoolName, value);
            }
        }

        private IEnumerator FadePortalParticleEmission(float duration)
        {
            if (portalParticlesRoot == null) yield break;
            ParticleSystem[] systems = portalParticlesRoot.GetComponentsInChildren<ParticleSystem>(true);
            if (systems.Length == 0) yield break;

            float[] startRateOverTime = new float[systems.Length];
            float[] startRateOverDistance = new float[systems.Length];
            float[][] startBurstCounts = new float[systems.Length][];

            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] == null) continue;
                var emission = systems[i].emission;
                var rotCurve = emission.rateOverTime;
                startRateOverTime[i] = rotCurve.mode == ParticleSystemCurveMode.Constant ? rotCurve.constant : rotCurve.constantMax;
                var rodCurve = emission.rateOverDistance;
                startRateOverDistance[i] = rodCurve.mode == ParticleSystemCurveMode.Constant ? rodCurve.constant : rodCurve.constantMax;
                int burstCount = emission.burstCount;
                startBurstCounts[i] = new float[burstCount];
                for (int b = 0; b < burstCount; b++)
                {
                    var burst = emission.GetBurst(b);
                    startBurstCounts[i][b] = burst.count.mode == ParticleSystemCurveMode.Constant ? burst.count.constant : burst.count.constantMax;
                }
            }

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
                systems[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            yield return new WaitForSeconds(1.5f);
        }

        private void FreezePlayer()
        {
            GameObject[] players = GameObject.FindGameObjectsWithTag(playerTag);
            foreach (GameObject player in players)
            {
                if (player == null) continue;
                MonoBehaviour[] scripts = player.GetComponentsInChildren<MonoBehaviour>();
                foreach (MonoBehaviour script in scripts) if (script != null) script.enabled = false;
            }
        }

        private void HidePlayer()
        {
            GameObject[] players = GameObject.FindGameObjectsWithTag(playerTag);
            foreach (GameObject player in players) if (player != null) DestroyImmediate(player);
            if (additionalObjectsToDestroyWithPlayer != null)
            {
                for (int i = 0; i < additionalObjectsToDestroyWithPlayer.Count; i++)
                {
                    if (additionalObjectsToDestroyWithPlayer[i] != null) DestroyImmediate(additionalObjectsToDestroyWithPlayer[i]);
                }
            }
        }

        private void DestroyAllPlayers()
        {
            GameObject[] players = GameObject.FindGameObjectsWithTag(playerTag);
            foreach (GameObject player in players) if (player != null) DestroyImmediate(player);
        }

        private void EnsureKinematicRigidbody()
        {
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.None;
        }
    }
}