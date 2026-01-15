// FILEPATH: Assets/Scripts/UI/Tutorial/OrderedTriggerSequence.cs

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace JellyGame.UI.Tutorial
{
    [DisallowMultipleComponent]
    public class OrderedTriggerSequence : MonoBehaviour
    {
        [Header("Triggers (in order)")]
        [SerializeField] private List<GameObject> steps = new List<GameObject>();

        [Header("Filtering")]
        [SerializeField] private int requiredLayer = -1;
        [SerializeField] private string requiredTag = "";

        [Header("Behavior")]
        [SerializeField] private bool autoStartOnEnable = false; // keep off

        public UnityEvent onCompleted;

        public bool Completed { get; private set; }

        private int _index = -1;

        private void OnEnable()
        {
            // IMPORTANT: if parent becomes active and children were left activeSelf=true,
            // they will all pop on. This prevents that.
            HideAllSteps();

            if (autoStartOnEnable)
                StartSequence();
        }

        public void StartSequence()
        {
            Completed = false;
            _index = -1;

            HideAllSteps();
            Advance();
        }

        private void HideAllSteps()
        {
            if (steps == null) return;
            for (int i = 0; i < steps.Count; i++)
                if (steps[i] != null)
                    steps[i].SetActive(false);
        }

        private void Advance()
        {
            _index++;

            if (_index >= (steps?.Count ?? 0))
            {
                Completed = true;
                onCompleted?.Invoke();
                return;
            }

            if (steps[_index] != null)
                steps[_index].SetActive(true);
        }

        // ===== Forwarder-style API (like AbilityZone) =====
        public void HandleTriggerEnter(OrderedTriggerForwarder step, Collider other)
        {
            if (Completed) return;
            if (!isActiveAndEnabled) return;

            // Filter who can trigger
            if (requiredLayer >= 0 && other.gameObject.layer != requiredLayer) return;
            if (!string.IsNullOrEmpty(requiredTag) && !other.CompareTag(requiredTag)) return;

            // Only accept the CURRENT step. This prevents:
            // - overlapping colliders
            // - multiple steps firing same frame
            // - player starting inside several triggers
            if (_index < 0 || _index >= steps.Count) return;
            if (steps[_index] == null) return;
            if (step == null) return;

            if (step.gameObject != steps[_index])
                return;

            // Hide current and advance
            steps[_index].SetActive(false);
            Advance();
        }

        public void HandleTriggerExit(OrderedTriggerForwarder step, Collider other)
        {
            // not needed for this sequence, but kept for symmetry / future use
        }
    }
}
