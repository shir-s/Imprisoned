// FILEPATH: Assets/Scripts/World/Finish/FinishTrigger.cs

using System.Collections;
using JellyGame.GamePlay.Audio.Core;
using JellyGame.GamePlay.Managers;
using UnityEngine;

namespace JellyGame.GamePlay.World.Finish
{
    /// <summary>
    /// Triggers GameWin when an object from the allowed layer(s) enters this trigger.
    ///
    /// IMPORTANT (Unity physics rule):
    /// Trigger callbacks require at least one Rigidbody in the interaction.
    /// This script ensures that by adding a kinematic Rigidbody to the finish object at runtime.
    ///
    /// Usage:
    /// - Attach to a GameObject with a Collider set as Trigger.
    /// - Set Allowed Layers to the player layer.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class FinishTrigger : MonoBehaviour
    {
        [Header("Trigger")]
        [Tooltip("Only objects on these layers can trigger GameWin.")]
        [SerializeField] private LayerMask allowedLayers;

        [Tooltip("If true, trigger only once.")]
        [SerializeField] private bool triggerOnce = true;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private bool _triggered;

        private void Reset()
        {
            Collider c = GetComponent<Collider>();
            if (c != null)
                c.isTrigger = true;
        }

        private void Awake()
        {
            EnsureKinematicRigidbody();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_triggered && triggerOnce)
                return;

            int layer = other.gameObject.layer;
            if ((allowedLayers.value & (1 << layer)) == 0)
                return;

            _triggered = true;

            if (debugLogs)
                Debug.Log($"[FinishTrigger] GameWin triggered by {other.name} (layer {layer})", this);
            
            SoundManager.Instance.StopAllSounds();
            SoundManager.Instance.PlaySound("Win", this.transform);
            
            StartCoroutine(GameWinEvent(other));
        }
        private IEnumerator GameWinEvent(Collider other)
        {
            yield return new WaitForSeconds(0);

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
