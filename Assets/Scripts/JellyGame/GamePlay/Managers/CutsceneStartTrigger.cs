// FILEPATH: Assets/Scripts/Managers/CutsceneStartTrigger.cs
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace JellyGame.GamePlay.Managers
{
    /// <summary>
    /// Triggers the start of a cutscene after the scene is fully loaded and visually ready.
    ///
    /// WORKS WITH FULL PRELOAD (LoadingManager.PreloadSceneFully):
    /// When a scene is fully preloaded, all root objects are disabled after Awake but before Start.
    /// On transition, LoadingManager re-enables them. This causes:
    ///   1. OnEnable() fires on all scripts
    ///   2. Start() fires on the next frame (first time only)
    ///   3. This script's Start() kicks off the wait-and-trigger coroutine
    ///   4. After a few frames (rendering stabilizes), the Animator bool is set
    ///   5. Camera animation transitions → cutscene plays from the beginning, fully visible
    ///
    /// ALSO WORKS WITH STANDARD PRELOAD:
    /// Scene activates normally, Start() runs, same trigger logic applies.
    ///
    /// USAGE:
    /// 1. Drop this on any GameObject in the cutscene scene.
    /// 2. Drag the camera's Animator into the "triggers" list.
    /// 3. Set the parameter name to match the designer's bool (e.g. "StartCutscene").
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public class CutsceneStartTrigger : MonoBehaviour
    {
        [Serializable]
        public class AnimatorTrigger
        {
            [Tooltip("The Animator to trigger (e.g. the cutscene camera's Animator).")]
            public Animator animator;

            [Tooltip("The bool parameter name that starts the cutscene.\n" +
                     "Must match exactly what the designers set up in the Animator Controller.")]
            public string boolParameterName = "StartCutscene";

            [Tooltip("If true, uses SetTrigger() instead of SetBool().\n" +
                     "Use this if the designer used a Trigger parameter instead of a Bool.")]
            public bool useTriggerInsteadOfBool = false;
        }

        [Header("Animator Triggers")]
        [Tooltip("List of animators and their bool/trigger parameters to set when the cutscene should start.")]
        [SerializeField] private List<AnimatorTrigger> triggers = new List<AnimatorTrigger>();

        [Header("Timing")]
        [Tooltip("Number of frames to wait after Start() before triggering.\n" +
                 "2 frames is usually enough for rendering to stabilize.\n" +
                 "With full preload, the scene is already loaded — this just ensures " +
                 "the first rendered frame is visible before the cutscene starts.")]
        [SerializeField] private int waitFrames = 2;

        [Tooltip("Optional additional real-time delay (seconds) after frame wait.\n" +
                 "Use 0 in most cases.")]
        [SerializeField] private float additionalDelaySeconds = 0f;

        [Header("Options")]
        [Tooltip("If true, starts automatically on Start().\n" +
                 "If false, call TriggerCutscene() manually.")]
        [SerializeField] private bool autoStart = true;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = true;

        private Coroutine _triggerRoutine;
        private bool _triggered = false;

        /// <summary>True after the cutscene has been triggered.</summary>
        public bool HasTriggered => _triggered;

        private void Start()
        {
            if (autoStart)
                TriggerCutscene();
        }

        /// <summary>
        /// Start the wait-and-trigger sequence. Safe to call multiple times (only triggers once).
        /// </summary>
        public void TriggerCutscene()
        {
            if (_triggered) return;

            if (_triggerRoutine != null)
                StopCoroutine(_triggerRoutine);

            _triggerRoutine = StartCoroutine(WaitAndTrigger());
        }

        private IEnumerator WaitAndTrigger()
        {
            if (debugLogs)
                Debug.Log($"[CutsceneStartTrigger] Waiting {waitFrames} frame(s) before triggering...", this);

            // Wait N frames for rendering to stabilize
            for (int i = 0; i < waitFrames; i++)
                yield return null;

            // Optional additional delay (real-time, works even if timeScale = 0)
            if (additionalDelaySeconds > 0f)
                yield return new WaitForSecondsRealtime(additionalDelaySeconds);

            // Wait for end of frame to ensure the frame is fully rendered
            yield return new WaitForEndOfFrame();

            // Set all animator parameters
            for (int i = 0; i < triggers.Count; i++)
            {
                var t = triggers[i];

                if (t.animator == null)
                {
                    if (debugLogs)
                        Debug.LogWarning($"[CutsceneStartTrigger] Trigger [{i}] has no Animator assigned.", this);
                    continue;
                }

                if (string.IsNullOrEmpty(t.boolParameterName))
                {
                    if (debugLogs)
                        Debug.LogWarning($"[CutsceneStartTrigger] Trigger [{i}] has no parameter name.", this);
                    continue;
                }

                if (t.useTriggerInsteadOfBool)
                {
                    t.animator.SetTrigger(t.boolParameterName);

                    if (debugLogs)
                        Debug.Log($"[CutsceneStartTrigger] SetTrigger('{t.boolParameterName}') on '{t.animator.gameObject.name}'", this);
                }
                else
                {
                    t.animator.SetBool(t.boolParameterName, true);

                    if (debugLogs)
                        Debug.Log($"[CutsceneStartTrigger] SetBool('{t.boolParameterName}', true) on '{t.animator.gameObject.name}'", this);
                }
            }

            _triggered = true;
            _triggerRoutine = null;

            if (debugLogs)
                Debug.Log($"[CutsceneStartTrigger] Cutscene triggered after {waitFrames} frame(s) + {additionalDelaySeconds:F2}s delay.", this);
        }
    }
}