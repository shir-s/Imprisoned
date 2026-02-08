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
    /// 1. Player enters portal (trigger collider)
    /// 2. Gameplay freezes (timeScale = 0)
    /// 3. Win FX plays
    /// 4. Player character is destroyed
    /// 5. GameWin event fires → LoadingManager starts transition
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

        [Header("Finish Visuals")]
        [SerializeField] private Renderer meshOnThisObject;
        [SerializeField] private Renderer meshOnChildObject;
        
        [Tooltip("Additional renderers to show/hide (e.g., key, multiple objects). Leave empty if not needed.")]
        [SerializeField] private List<Renderer> additionalRenderers = new List<Renderer>();
        
        [Tooltip("GameObjects to activate/deactivate (e.g., key parent object with many child renderers). More efficient than managing many renderers separately.")]
        [SerializeField] private List<GameObject> gameObjectsToToggle = new List<GameObject>();

        [Header("Win Sequence")]
        [Tooltip("Freeze gameplay (timeScale=0) when player enters the portal.")]
        [SerializeField] private bool freezeOnWin = true;

        [Tooltip("Destroy the player character before triggering GameWin.\n" +
                 "Prevents duplicate-player issues when the new scene spawns its own player.")]
        [SerializeField] private bool destroyPlayerBeforeTransition = true;

        [Tooltip("Tag used to find the player character for destruction.")]
        [SerializeField] private string playerTag = "DrawingCube";

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

        private void UpdateFinishMeshes(bool enabled)
        {
            if (meshOnThisObject != null)
                meshOnThisObject.enabled = enabled;

            if (meshOnChildObject != null)
                meshOnChildObject.enabled = enabled;
            
            if (additionalRenderers != null)
            {
                for (int i = 0; i < additionalRenderers.Count; i++)
                {
                    if (additionalRenderers[i] != null)
                        additionalRenderers[i].enabled = enabled;
                }
            }
            
            if (gameObjectsToToggle != null)
            {
                for (int i = 0; i < gameObjectsToToggle.Count; i++)
                {
                    if (gameObjectsToToggle[i] != null)
                        gameObjectsToToggle[i].SetActive(enabled);
                }
            }
        }

        private void Awake()
        {
            EnsureKinematicRigidbody();

            UpdateFinishMeshes(IsWinConditionMet());

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

            UpdateFinishMeshes(IsWinConditionMet());

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
                UpdateFinishMeshes(true);
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
            // 1) Freeze gameplay — everything stops immediately
            if (freezeOnWin)
            {
                Time.timeScale = 0f;

                if (debugLogs)
                    Debug.Log("[FinishTrigger] Gameplay frozen (timeScale=0).", this);
            }

            // 2) Activate win FX
            if (winFxRoot != null)
            {
                winFxRoot.SetActive(true);

                if (playParticleSystemsOnWin)
                {
                    var ps = winFxRoot.GetComponentsInChildren<ParticleSystem>(true);
                    for (int i = 0; i < ps.Length; i++)
                    {
                        ps[i].Clear(true);
                        // Use unscaled time so particles play even when timeScale=0
                        var main = ps[i].main;
                        main.useUnscaledTime = true;
                        ps[i].Play(true);
                    }
                }

                if (debugLogs)
                    Debug.Log($"[FinishTrigger] Win FX activated: '{winFxRoot.name}', delay={winFxDelaySeconds:0.###}s", this);
            }

            // 3) Wait for FX to play (real time — works even with timeScale=0)
            float delay = Mathf.Max(0f, winFxDelaySeconds);
            if (delay > 0f)
                yield return new WaitForSecondsRealtime(delay);
            else
                yield return null;

            // 4) Destroy the player character BEFORE triggering GameWin.
            //    This prevents duplicate-player issues: when the new scene activates,
            //    its PlayerSpawner creates a fresh player — the old one must be gone by then.
            if (destroyPlayerBeforeTransition)
            {
                DestroyAllPlayers();
            }

            // 5) Trigger GameWin → GameSceneManager → LoadingManager starts transition
            //    Note: LoadingManager resets timeScale=1 in Step 8 when the new scene is ready.
            if (debugLogs)
                Debug.Log("[FinishTrigger] Triggering GameWin event.", this);

            EventManager.TriggerEvent(EventManager.GameEvent.GameWin, null);
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