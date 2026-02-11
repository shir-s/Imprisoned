// FILEPATH: Assets/Scripts/Managers/CutscenePortalSequence.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using JellyGame.GamePlay.World.Finish;

namespace JellyGame.GamePlay.Managers
{
    /// <summary>
    /// Specialized cutscene ending for scenes where, after the cutscene finishes,
    /// the player presses a button, then two characters walk to a portal which activates.
    ///
    /// USE THIS INSTEAD OF CutsceneSceneTransition on cutscene scenes that need this flow.
    ///
    /// Requires on the SAME scene:
    /// - GameSceneManager (configured with nextSceneBuildIndex → handles preloading + GameWin transition)
    /// - FinishTrigger    (the portal object → handles win FX, sound, and fires GameWin)
    ///
    /// Flow:
    /// 1. On Start() → immediately freeze both objects (disable scripts, kinematic, zero velocity)
    ///    and disable camera follow scripts so nothing moves on its own
    /// 2. Cutscene plays (Timeline auto-detection or fixed duration)
    /// 3. Cutscene ends (auto) OR player skips manually → stays on scene
    /// 4. Canvas appears (e.g. "Press E to continue")
    /// 5. Waits for player input
    /// 6. Moves two objects (slime + slime prime) in a direction for a distance
    /// 7. Calls FinishTrigger.ForceActivatePortal() → win sound, FX, GameWin event
    /// 8. GameSceneManager catches GameWin → transitions to next scene
    /// </summary>
    [DisallowMultipleComponent]
    public class CutscenePortalSequence : MonoBehaviour
    {
        // ===================== Cutscene Detection =====================
        [Header("Cutscene End Detection")]
        [Tooltip("Detect cutscene end via PlayableDirector (Timeline)?")]
        [SerializeField] private bool detectTimeline = true;

        [Tooltip("If no Timeline found (or detectTimeline is off), use this fixed duration.")]
        [SerializeField] private float fixedCutsceneDuration = 10f;

        [Tooltip("Extra delay after cutscene ends before showing the canvas.")]
        [SerializeField] private float postCutsceneDelay = 0f;

        // ===================== Manual Skip =====================
        [Header("Manual Skip (During Cutscene)")]
        [Tooltip("Allow the player to skip the cutscene with a button press.")]
        [SerializeField] private bool allowManualSkip = true;

        [SerializeField] private KeyCode skipKey = KeyCode.Space;

        [SerializeField] private List<KeyCode> controllerSkipButtons = new List<KeyCode>
        {
            KeyCode.JoystickButton2,
            KeyCode.JoystickButton3
        };

        [Tooltip("Ignore skip input for this many seconds after the scene starts.\n" +
                 "Prevents accidental skips from loading screen input carrying over.")]
        [SerializeField] private float inputGuardSeconds = 0.5f;

        // ===================== Post-Cutscene Canvas =====================
        [Header("Post-Cutscene Canvas")]
        [Tooltip("The Canvas/GameObject to enable after the cutscene ends. Will be disabled on Start.")]
        [SerializeField] private GameObject postCutsceneCanvas;

        [Tooltip("Key to press to continue after the canvas is shown.")]
        [SerializeField] private KeyCode continueKey = KeyCode.E;

        [SerializeField] private List<KeyCode> controllerContinueButtons = new List<KeyCode>
        {
            KeyCode.JoystickButton0,
            KeyCode.JoystickButton1
        };

        [Tooltip("Ignore continue input for this many seconds after the canvas appears.\n" +
                 "Prevents the skip press from immediately dismissing the canvas.")]
        [SerializeField] private float continueInputGuardSeconds = 0.3f;

        // ===================== Objects to Freeze & Move =====================
        [Header("Objects (Frozen on Start, Moved After Continue)")]
        [Tooltip("First object to freeze and move (e.g. the player slime).")]
        [SerializeField] private Transform objectA;

        [Tooltip("Second object to freeze and move (e.g. slime prime).")]
        [SerializeField] private Transform objectB;

        // ===================== Movement =====================
        public enum MoveDirectionMode
        {
            WorldVector,
            TowardTarget
        }

        [Header("Movement (After Continue)")]
        [SerializeField] private MoveDirectionMode directionMode = MoveDirectionMode.TowardTarget;

        [Tooltip("Target to move toward (used when directionMode = TowardTarget). Usually the portal.")]
        [SerializeField] private Transform moveDirectionTarget;

        [Tooltip("World direction vector (used when directionMode = WorldVector).")]
        [SerializeField] private Vector3 worldDirection = Vector3.forward;

        [Tooltip("How far each object moves (in world units).")]
        [SerializeField] private float moveDistance = 5f;

        [Tooltip("How long the movement takes (in seconds, real time).")]
        [SerializeField] private float moveDuration = 1.5f;

        [Tooltip("Speed curve over the movement duration (0→1 normalized time). Default ease-in-out.")]
        [SerializeField] private AnimationCurve speedCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Movement Delay")]
        [Tooltip("Delay (seconds) after continue input before movement starts.")]
        [SerializeField] private float preMovementDelay = 0.2f;

        [Tooltip("If true, hide the post-cutscene canvas when movement starts.")]
        [SerializeField] private bool hideCanvasOnMovementStart = true;

        // ===================== Portal =====================
        [Header("Portal")]
        [Tooltip("The FinishTrigger component on the portal. ForceActivatePortal() will be called after movement.")]
        [SerializeField] private FinishTrigger portalTrigger;

        [Tooltip("Delay (seconds) after movement completes before activating the portal.")]
        [SerializeField] private float postMovementDelay = 0.3f;

        // ===================== Debug =====================
        [Header("Debug")]
        [SerializeField] private bool debugLogs = true;

        // ===================== Runtime =====================
        private bool _cutsceneEnded;
        private bool _sequenceStarted;
        private PlayableDirector _timeline;
        private float _sceneStartTime;
        private Coroutine _autoEndCoroutine;

        // ===================== Lifecycle =====================

        private void Start()
        {
            _cutsceneEnded = false;
            _sequenceStarted = false;
            _sceneStartTime = Time.realtimeSinceStartup;

            // Ensure canvas is hidden
            if (postCutsceneCanvas != null)
                postCutsceneCanvas.SetActive(false);

            // IMMEDIATELY freeze both objects — no movement, no physics, no scripts
            FreezeObject(objectA, "ObjectA");
            FreezeObject(objectB, "ObjectB");

            // Setup auto-end detection
            SetupCutsceneEndDetection();

            if (debugLogs)
                Debug.Log($"[CutscenePortalSequence] Started in scene: {gameObject.scene.name}. Both objects frozen.", this);
        }

        private void OnDestroy()
        {
            if (_timeline != null)
                _timeline.stopped -= OnTimelineStopped;
        }

        private void Update()
        {
            // Only check for manual skip while cutscene is playing
            if (_cutsceneEnded || _sequenceStarted)
                return;

            if (!allowManualSkip)
                return;

            // Input guard
            if (Time.realtimeSinceStartup - _sceneStartTime < inputGuardSeconds)
                return;

            // Ignore input if LoadingManager is transitioning
            if (LoadingManager.Instance != null && LoadingManager.Instance.IsTransitioning)
                return;

            bool skipPressed = Input.GetKeyDown(skipKey);

            if (!skipPressed && controllerSkipButtons != null)
            {
                for (int i = 0; i < controllerSkipButtons.Count; i++)
                {
                    if (Input.GetKeyDown(controllerSkipButtons[i]))
                    {
                        skipPressed = true;
                        break;
                    }
                }
            }

            if (skipPressed)
            {
                if (debugLogs)
                    Debug.Log("[CutscenePortalSequence] Manual skip → starting post-cutscene sequence.", this);

                OnCutsceneEnded();
            }
        }

        // ===================== Freeze Helpers =====================

        /// <summary>
        /// Completely freezes a GameObject:
        /// - Disables ALL MonoBehaviour scripts (movement, AI, input, etc.)
        /// - Sets ALL Rigidbodies to kinematic with zero velocity
        /// The object remains visible (renderers untouched).
        /// Scripts are NOT restored — FinishTrigger.ForceActivatePortal() handles end-of-life.
        /// </summary>
        private void FreezeObject(Transform obj, string label)
        {
            if (obj == null) return;

            int frozenCount = 0;

            // Disable all MonoBehaviour scripts on the object and its children
            MonoBehaviour[] scripts = obj.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (MonoBehaviour script in scripts)
            {
                if (script == null) continue;
                if (script == this) continue; // Don't freeze ourselves!

                if (script.enabled)
                {
                    script.enabled = false;
                    frozenCount++;
                }
            }

            // Set ALL Rigidbodies to kinematic and zero velocity (root + children)
            Rigidbody[] rigidbodies = obj.GetComponentsInChildren<Rigidbody>(true);
            foreach (Rigidbody rb in rigidbodies)
            {
                if (rb == null) continue;
                rb.isKinematic = true;
                rb.angularVelocity = Vector3.zero;
#if UNITY_6000_0_OR_NEWER
                rb.linearVelocity = Vector3.zero;
#else
                rb.velocity = Vector3.zero;
#endif
            }

            if (debugLogs)
                Debug.Log($"[CutscenePortalSequence] Froze {label} '{obj.name}': disabled {frozenCount} scripts, set {rigidbodies.Length} rigidbodies kinematic.", this);
        }

        // ===================== Cutscene End Detection =====================

        private void SetupCutsceneEndDetection()
        {
            if (detectTimeline)
            {
                _timeline = FindObjectOfType<PlayableDirector>();

                if (_timeline != null && _timeline.duration > 0.1)
                {
                    _timeline.stopped += OnTimelineStopped;

                    if (debugLogs)
                        Debug.Log($"[CutscenePortalSequence] Timeline detected. Duration: {_timeline.duration:F1}s", this);
                    return;
                }

                if (debugLogs)
                    Debug.LogWarning("[CutscenePortalSequence] No valid Timeline found. Using fixed duration.", this);
            }

            // Fallback: fixed duration
            _autoEndCoroutine = StartCoroutine(AutoEndAfterSeconds(fixedCutsceneDuration));

            if (debugLogs)
                Debug.Log($"[CutscenePortalSequence] Auto-end in {fixedCutsceneDuration}s (fixed duration).", this);
        }

        private void OnTimelineStopped(PlayableDirector director)
        {
            if (_cutsceneEnded || _sequenceStarted)
                return;

            if (debugLogs)
                Debug.Log("[CutscenePortalSequence] Timeline ended.", this);

            OnCutsceneEnded();
        }

        private IEnumerator AutoEndAfterSeconds(float seconds)
        {
            if (seconds > 0f)
                yield return new WaitForSeconds(seconds);

            if (!_cutsceneEnded && !_sequenceStarted)
            {
                if (debugLogs)
                    Debug.Log("[CutscenePortalSequence] Fixed duration elapsed.", this);

                OnCutsceneEnded();
            }

            _autoEndCoroutine = null;
        }

        // ===================== Post-Cutscene Sequence =====================

        private void OnCutsceneEnded()
        {
            if (_cutsceneEnded)
                return;

            _cutsceneEnded = true;

            // Stop auto-end coroutine if it's still running (manual skip case)
            if (_autoEndCoroutine != null)
            {
                StopCoroutine(_autoEndCoroutine);
                _autoEndCoroutine = null;
            }

            StartCoroutine(PostCutsceneSequence());
        }

        private IEnumerator PostCutsceneSequence()
        {
            if (_sequenceStarted)
                yield break;

            _sequenceStarted = true;

            if (debugLogs)
                Debug.Log("[CutscenePortalSequence] Post-cutscene sequence started.", this);

            // --- Optional delay before showing canvas ---
            if (postCutsceneDelay > 0f)
                yield return new WaitForSecondsRealtime(postCutsceneDelay);

            // --- Show canvas ---
            if (postCutsceneCanvas != null)
            {
                postCutsceneCanvas.SetActive(true);

                if (debugLogs)
                    Debug.Log("[CutscenePortalSequence] Canvas shown. Waiting for continue input.", this);
            }

            // --- Wait for continue input ---
            yield return WaitForContinueInput();

            if (debugLogs)
                Debug.Log("[CutscenePortalSequence] Continue input received.", this);

            // --- Pre-movement delay ---
            if (preMovementDelay > 0f)
                yield return new WaitForSecondsRealtime(preMovementDelay);

            // --- Hide canvas ---
            if (hideCanvasOnMovementStart && postCutsceneCanvas != null)
                postCutsceneCanvas.SetActive(false);

            // --- Move objects ---
            if (debugLogs)
                Debug.Log("[CutscenePortalSequence] Starting movement.", this);

            yield return MoveObjects();

            if (debugLogs)
                Debug.Log("[CutscenePortalSequence] Movement complete.", this);

            // --- Post-movement delay ---
            if (postMovementDelay > 0f)
                yield return new WaitForSecondsRealtime(postMovementDelay);

            // --- Activate portal ---
            if (portalTrigger != null)
            {
                if (debugLogs)
                    Debug.Log("[CutscenePortalSequence] Activating portal.", this);

                portalTrigger.ForceActivatePortal();
            }
            else
            {
                Debug.LogError("[CutscenePortalSequence] No FinishTrigger assigned! Firing GameWin directly.", this);
                EventManager.TriggerEvent(EventManager.GameEvent.GameWin, null);
            }
        }

        // ===================== Wait for Continue Input =====================

        private IEnumerator WaitForContinueInput()
        {
            // Guard: ignore input briefly so the skip press doesn't immediately count as continue
            float canvasShownTime = Time.realtimeSinceStartup;

            while (true)
            {
                // Input guard
                if (Time.realtimeSinceStartup - canvasShownTime < continueInputGuardSeconds)
                {
                    yield return null;
                    continue;
                }

                bool pressed = Input.GetKeyDown(continueKey);

                if (!pressed && controllerContinueButtons != null)
                {
                    for (int i = 0; i < controllerContinueButtons.Count; i++)
                    {
                        if (Input.GetKeyDown(controllerContinueButtons[i]))
                        {
                            pressed = true;
                            break;
                        }
                    }
                }

                if (pressed)
                    yield break;

                yield return null;
            }
        }

        // ===================== Movement =====================

        private IEnumerator MoveObjects()
        {
            // Compute direction BEFORE movement starts (objects are frozen in place, positions are stable)
            Vector3 dir = ComputeMoveDirection();

            if (dir.sqrMagnitude < 0.0001f)
            {
                Debug.LogWarning("[CutscenePortalSequence] Move direction is zero! Skipping movement.", this);
                yield break;
            }

            dir.Normalize();

            if (debugLogs)
                Debug.Log($"[CutscenePortalSequence] Move direction: {dir}, distance: {moveDistance}, duration: {moveDuration}s", this);

            float duration = Mathf.Max(0.01f, moveDuration);
            float distance = Mathf.Max(0f, moveDistance);

            // Capture start positions (objects are frozen, so these are stable)
            Vector3 startA = objectA != null ? objectA.position : Vector3.zero;
            Vector3 startB = objectB != null ? objectB.position : Vector3.zero;

            // Safety: re-zero velocity (objects should already be kinematic from FreezeObject)
            ReZeroVelocity(objectA);
            ReZeroVelocity(objectB);

            // Animate movement using unscaled time (works even if timeScale is modified)
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                // Evaluate the curve to get normalized progress (0→1)
                float curveValue = speedCurve != null ? speedCurve.Evaluate(t) : t;
                float currentDistance = curveValue * distance;

                // Set position directly — all scripts are disabled so nothing fights this
                if (objectA != null)
                    objectA.position = startA + dir * currentDistance;

                if (objectB != null)
                    objectB.position = startB + dir * currentDistance;

                yield return null;
            }

            // Snap to final position
            if (objectA != null)
                objectA.position = startA + dir * distance;

            if (objectB != null)
                objectB.position = startB + dir * distance;
        }

        private Vector3 ComputeMoveDirection()
        {
            switch (directionMode)
            {
                case MoveDirectionMode.TowardTarget:
                    if (moveDirectionTarget == null)
                    {
                        Debug.LogWarning("[CutscenePortalSequence] moveDirectionTarget is null! Falling back to Vector3.forward.", this);
                        return Vector3.forward;
                    }

                    // Compute direction from midpoint of both objects toward the target
                    Vector3 midpoint = Vector3.zero;
                    int count = 0;

                    if (objectA != null) { midpoint += objectA.position; count++; }
                    if (objectB != null) { midpoint += objectB.position; count++; }

                    if (count > 0)
                        midpoint /= count;

                    Vector3 toTarget = moveDirectionTarget.position - midpoint;

                    // Flatten to XZ plane (ground movement — ignore height differences)
                    toTarget.y = 0f;

                    if (debugLogs)
                        Debug.Log($"[CutscenePortalSequence] Direction calc: midpoint={midpoint}, target={moveDirectionTarget.position}, dir={toTarget.normalized}", this);

                    return toTarget.normalized;

                case MoveDirectionMode.WorldVector:
                default:
                    return worldDirection.normalized;
            }
        }

        /// <summary>
        /// Safety: re-zero velocity on an object's Rigidbodies.
        /// Objects should already be kinematic from FreezeObject(), but this ensures no residual velocity.
        /// </summary>
        private static void ReZeroVelocity(Transform obj)
        {
            if (obj == null) return;

            Rigidbody[] rigidbodies = obj.GetComponentsInChildren<Rigidbody>(true);
            foreach (Rigidbody rb in rigidbodies)
            {
                if (rb == null) continue;
                rb.angularVelocity = Vector3.zero;
#if UNITY_6000_0_OR_NEWER
                rb.linearVelocity = Vector3.zero;
#else
                rb.velocity = Vector3.zero;
#endif
            }
        }
    }
}