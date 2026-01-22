// FILEPATH: Assets/Scripts/World/Finish/FinishTrigger.cs
using System.Collections;
using JellyGame.GamePlay.Audio.Core;
using JellyGame.GamePlay.Managers;
using UnityEngine;

namespace JellyGame.GamePlay.World.Finish
{
    /// <summary>
    /// Triggers GameWin when player enters the trigger AND all enemies are dead.
    /// Listens to EventManager.GameEvent.EntityDied.
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
        }

        private void Awake()
        {
            EnsureKinematicRigidbody();

            if (requiredDeaths < 1)
                requiredDeaths = 1;

            // Hide meshes until win condition is met
            UpdateFinishMeshes(_deathCount >= requiredDeaths);

            // Optional: keep FX off until win
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

            // Catch-up from EnemyDeathCounter
            var counter = FindObjectOfType<JellyGame.GamePlay.Utils.EnemyDeathCounter>();
            if (counter != null)
            {
                int counterDeaths = counter.GetDeathCount();
                if (counterDeaths > _deathCount)
                    _deathCount = counterDeaths;
            }

            UpdateFinishMeshes(_deathCount >= requiredDeaths);

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

            if (_deathCount >= requiredDeaths)
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

            if (_deathCount < requiredDeaths)
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

        private IEnumerator GameWinEvent(Collider other)
        {
            // 1) Activate FX
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
            }

            // 2) Delay (real time, even if timescale changes elsewhere)
            float delay = Mathf.Max(0f, winFxDelaySeconds);
            if (delay > 0f)
                yield return new WaitForSecondsRealtime(delay);
            else
                yield return null;

            // 3) Trigger GameWin
            EventManager.TriggerEvent(EventManager.GameEvent.GameWin, other.gameObject);
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
