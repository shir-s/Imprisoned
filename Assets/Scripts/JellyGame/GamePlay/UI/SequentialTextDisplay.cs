// FILEPATH: Assets/Scripts/UI/SequentialTextDisplay.cs
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace JellyGame.UI
{
    /// <summary>
    /// Sequentially shows/hides GameObjects (typically TMP text) with configurable delays.
    /// 
    /// Each entry has:
    /// - The GameObject to show
    /// - How long to wait BEFORE showing it (delay)
    /// - How long it stays visible before hiding and moving to the next
    /// 
    /// The first entry is assumed to already be active when the scene starts.
    /// 
    /// USAGE:
    /// 1. Add this to a GameObject in your cutscene scene.
    /// 2. Drag your TMP GameObjects into the entries list.
    /// 3. Set delays per entry in the Inspector.
    /// </summary>
    [DisallowMultipleComponent]
    public class SequentialTextDisplay : MonoBehaviour
    {
        [Serializable]
        public class TextEntry
        {
            [Tooltip("The GameObject to show (e.g. a TMP text object).")]
            public GameObject target;

            [Tooltip("Seconds to wait BEFORE showing this entry.\n" +
                     "For the first entry this is ignored (it starts active).")]
            public float delayBeforeShow = 2f;

            [Tooltip("Seconds this entry stays visible before moving to the next.\n" +
                     "If this is the last entry, it stays visible until the scene ends.")]
            public float visibleDuration = 3f;

            [Header("Events (Optional)")]
            public UnityEvent onShow;
            public UnityEvent onHide;
        }

        [Header("Text Entries (in order)")]
        [SerializeField] private List<TextEntry> entries = new List<TextEntry>();

        [Header("Options")]
        [Tooltip("If true, starts the sequence automatically on Start().\n" +
                 "If false, call StartSequence() manually.")]
        [SerializeField] private bool autoStart = true;

        [Tooltip("If true, hides the last entry after its visibleDuration.\n" +
                 "If false, the last entry stays visible indefinitely.")]
        [SerializeField] private bool hideLastEntry = false;

        [Tooltip("If true, uses unscaled time (works even when Time.timeScale = 0).")]
        [SerializeField] private bool useUnscaledTime = true;

        [Header("Events")]
        public UnityEvent onSequenceComplete;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private Coroutine _sequenceRoutine;

        private void Start()
        {
            // Hide all except the first
            for (int i = 1; i < entries.Count; i++)
            {
                if (entries[i].target != null)
                    entries[i].target.SetActive(false);
            }

            if (autoStart)
                StartSequence();
        }

        public void StartSequence()
        {
            if (_sequenceRoutine != null)
                StopCoroutine(_sequenceRoutine);

            _sequenceRoutine = StartCoroutine(RunSequence());
        }

        public void StopSequence()
        {
            if (_sequenceRoutine != null)
            {
                StopCoroutine(_sequenceRoutine);
                _sequenceRoutine = null;
            }
        }

        private IEnumerator RunSequence()
        {
            if (entries == null || entries.Count == 0)
            {
                if (debugLogs)
                    Debug.Log("[SequentialTextDisplay] No entries configured.", this);
                yield break;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry.target == null) continue;

                bool isFirst = (i == 0);
                bool isLast = (i == entries.Count - 1);

                // Wait before showing (skip for first entry since it's already active)
                if (!isFirst)
                {
                    yield return Wait(entry.delayBeforeShow);

                    entry.target.SetActive(true);

                    if (debugLogs)
                        Debug.Log($"[SequentialTextDisplay] Showing entry {i}: '{entry.target.name}'", this);
                }
                else if (debugLogs)
                {
                    Debug.Log($"[SequentialTextDisplay] First entry already active: '{entry.target.name}'", this);
                }

                entry.onShow?.Invoke();

                // Stay visible for duration
                if (isLast && !hideLastEntry)
                {
                    // Last entry stays visible — we're done
                    if (debugLogs)
                        Debug.Log($"[SequentialTextDisplay] Last entry '{entry.target.name}' stays visible.", this);
                }
                else
                {
                    yield return Wait(entry.visibleDuration);

                    entry.target.SetActive(false);
                    entry.onHide?.Invoke();

                    if (debugLogs)
                        Debug.Log($"[SequentialTextDisplay] Hidden entry {i}: '{entry.target.name}'", this);
                }
            }

            onSequenceComplete?.Invoke();

            if (debugLogs)
                Debug.Log("[SequentialTextDisplay] Sequence complete.", this);

            _sequenceRoutine = null;
        }

        private object Wait(float seconds)
        {
            if (seconds <= 0f)
                return null;

            if (useUnscaledTime)
                return new WaitForSecondsRealtime(seconds);

            return new WaitForSeconds(seconds);
        }
    }
}