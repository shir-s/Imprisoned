// FILEPATH: Assets/Scripts/UI/Tutorial/TutorialSequenceController.cs
using System;
using System.Collections;
using System.Collections.Generic;
using JellyGame.GamePlay.Managers;
using UnityEngine;
using UnityEngine.Events;

namespace JellyGame.UI.Tutorial
{
    /// <summary>
    /// Shows a sequence of tutorial windows one after another.
    /// While a window is active, the game time is paused (Time.timeScale = 0).
    ///
    /// Features:
    /// - Skip button/key (default E) with cooldown per window (unscaled time).
    /// - Optional "intro move" before the sequence starts (unscaled time).
    /// - NEW: Optional "gates" per window: when you skip a window, instead of showing the next one,
    ///        the game resumes until the gate requirement is satisfied, then after a small countdown,
    ///        the game pauses and the next window appears.
    ///
    /// Gate example: "Press all arrow keys at least once" -> wait countdown -> pause -> show next window.
    /// </summary>
    [DisallowMultipleComponent]
    public class TutorialSequenceController : MonoBehaviour
    {
        // ===================== Windows =====================
        [Header("Windows (in order)")]
        [Tooltip("GameObjects that represent tutorial windows (Canvas roots/panels). They will be activated/deactivated by this controller.")]
        [SerializeField] private List<GameObject> windows = new List<GameObject>();

        // ===================== Pause =====================
        [Header("Pause")]
        [Tooltip("If true, pauses the game while any tutorial window is shown (Time.timeScale = 0).")]
        [SerializeField] private bool pauseGameWhileActive = true;

        [Tooltip("If true, restores the previous timeScale after the tutorial finishes.")]
        [SerializeField] private bool restorePreviousTimeScaleOnFinish = true;

        // ===================== Skip =====================
        [Header("Skip")]
        [Tooltip("How many seconds the user must wait before they are allowed to skip the current window.")]
        [SerializeField] private float skipCooldownSeconds = 0.75f;

        [Tooltip("Key used to skip the current window.")]
        [SerializeField] private KeyCode skipKey = KeyCode.E;

        [Tooltip("If true, holding the key will only skip once per window (recommended).")]
        [SerializeField] private bool requireKeyDown = true;

        // ===================== Flow =====================
        [Header("Flow")]
        [Tooltip("Start tutorial automatically on Start().")]
        [SerializeField] private bool autoStart = false;

        [Tooltip("If true, the tutorial ends automatically if windows list is empty.")]
        [SerializeField] private bool endImmediatelyIfNoWindows = true;

        // ===================== Intro Move =====================
        [Header("Intro Move (Before Tutorial)")]
        [Tooltip("If true, moves the target first, then starts the tutorial sequence.")]
        [SerializeField] private bool playIntroMoveBeforeTutorial = true;

        [Tooltip("The thing you want to move (your slime root transform).")]
        [SerializeField] private Transform introMoveTarget;

        public enum IntroMoveDirectionMode
        {
            WorldVector,
            TargetForward,
            TargetRight,
            TargetUp,
            ReferenceTransformForward,
            ReferenceTransformRight
        }

        [Tooltip("How to interpret the intro move direction.")]
        [SerializeField] private IntroMoveDirectionMode introDirectionMode = IntroMoveDirectionMode.WorldVector;

        [Tooltip("Used if direction mode is WorldVector.")]
        [SerializeField] private Vector3 introWorldDirection = Vector3.forward;

        [Tooltip("Used if direction mode uses a reference transform.")]
        [SerializeField] private Transform introDirectionReference;

        [Tooltip("How far to move in that direction (world units).")]
        [SerializeField] private float introDistance = 2.0f;

        [Tooltip("How long the move takes (seconds).")]
        [SerializeField] private float introDuration = 0.35f;

        [Tooltip("Speed at the start of the intro move (units/sec).")]
        [SerializeField] private float introStartSpeed = 0.0f;

        [Tooltip("Peak speed during the intro move (units/sec).")]
        [SerializeField] private float introMaxSpeed = 12.0f;

        [Tooltip("Shapes speed over normalized time (0..1). Y is a multiplier between startSpeed and maxSpeed.\n" +
                 "Recommended: starts low, ramps up smoothly, then eases out.\n" +
                 "Example points: (0,0) (0.35,1) (1,0)")]
        [SerializeField] private AnimationCurve introSpeedProfile = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0f),
            new Keyframe(0.35f, 1f, 0f, 0f),
            new Keyframe(1f, 0f, 0f, 0f)
        );

        [Tooltip("Optional sideways wobble amount (units). Makes it feel 'cartoon zippy'.")]
        [SerializeField] private float introWobbleAmplitude = 0.08f;

        [Tooltip("How many wobble cycles over the move.")]
        [SerializeField] private float introWobbleCycles = 2.0f;

        [Tooltip("If true, this script will temporarily disable Rigidbody movement and move the transform directly.")]
        [SerializeField] private bool introMoveTransformDirectly = true;

        [Tooltip("If moving directly and target has a Rigidbody, we can set it kinematic for the intro.")]
        [SerializeField] private bool introForceKinematicIfRigidBody = true;

        // ===================== NEW: Window Gates =====================
        [Serializable]
        private class WindowGate
        {
            public bool enabled = false;
            public RequirementType requirement = RequirementType.PressAllArrowKeysOnce;

            [Tooltip("After the requirement is satisfied, wait this many seconds (unscaled) before pausing and showing next window.")]
            public float afterCompleteCountdownSeconds = 0.5f;

            [Header("Events (optional)")]
            public UnityEvent onGateStart;
            public UnityEvent onGateComplete;
        }

        private enum RequirementType
        {
            PressAllArrowKeysOnce,
            AreaClosedOnce // NEW
        }

        [Header("Window Gates (optional, per index)")]
        [Tooltip("Optional gates. Index in this list matches the windows index. If list is shorter, missing entries = no gate.")]
        [SerializeField] private List<WindowGate> windowGates = new List<WindowGate>();

        [Header("Gate Input (for requirements)")]
        [Tooltip("Movement keys used for 'PressAllArrowKeysOnce' gate.")]
        [SerializeField] private KeyCode gateUpKey = KeyCode.UpArrow;
        [SerializeField] private KeyCode gateDownKey = KeyCode.DownArrow;
        [SerializeField] private KeyCode gateLeftKey = KeyCode.LeftArrow;
        [SerializeField] private KeyCode gateRightKey = KeyCode.RightArrow;

        // ===================== Debug =====================
        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        // ===================== Runtime =====================
        private enum FlowState
        {
            Idle,
            ShowingWindow,
            WaitingForGate
        }

        private FlowState _state = FlowState.Idle;

        private int _currentIndex = -1;
        private float _canSkipAtUnscaledTime = 0f;

        private float _prevTimeScale = 1f;
        private bool _prevTimeScaleCaptured = false;

        private Coroutine _flowRoutine;
        private Coroutine _gateRoutine;

        public bool IsRunning => _state != FlowState.Idle;
        public int CurrentIndex => _currentIndex;
        public int WindowCount => windows != null ? windows.Count : 0;

        private void Start()
        {
            HideAllWindows();

            if (autoStart)
                StartTutorial();
        }

        private void Update()
        {
            if (_state != FlowState.ShowingWindow)
                return;

            if (_currentIndex < 0 || _currentIndex >= WindowCount)
                return;

            if (Time.unscaledTime < _canSkipAtUnscaledTime)
                return;

            bool pressed = requireKeyDown ? Input.GetKeyDown(skipKey) : Input.GetKey(skipKey);
            if (pressed)
                SkipCurrentWindow();
        }

        /// <summary>
        /// Starts the whole flow: optional intro move, then the tutorial sequence.
        /// </summary>
        public void StartTutorial()
        {
            if (_flowRoutine != null)
                StopCoroutine(_flowRoutine);

            if (_gateRoutine != null)
            {
                StopCoroutine(_gateRoutine);
                _gateRoutine = null;
            }

            _flowRoutine = StartCoroutine(BeginFlow());
        }

        private IEnumerator BeginFlow()
        {
            _state = FlowState.Idle;
            _currentIndex = -1;
            HideAllWindows();

            if (WindowCount == 0)
            {
                if (debugLogs)
                    Debug.Log("[Tutorial] StartTutorial called but windows list is empty.", this);

                if (endImmediatelyIfNoWindows)
                    FinishTutorial();

                yield break;
            }

            if (playIntroMoveBeforeTutorial)
                yield return PlayIntroMove();

            // Now enter the actual window sequence
            StartTutorialSequenceOnly();
        }

        private void StartTutorialSequenceOnly()
        {
            if (_state != FlowState.Idle)
                return;

            _state = FlowState.ShowingWindow;

            if (pauseGameWhileActive)
                PauseGame();

            ShowWindowAtIndex(0);

            if (debugLogs)
                Debug.Log("[Tutorial] Started sequence.", this);
        }

        // ===================== Skip / Window Advance =====================

        public void SkipCurrentWindow()
        {
            if (_state != FlowState.ShowingWindow)
                return;

            if (_currentIndex < 0 || _currentIndex >= WindowCount)
                return;

            // If this window has a gate, we do NOT show the next window yet.
            if (TryStartGateForCurrentWindow())
                return;

            // Normal behavior: advance immediately
            AdvanceToNextWindowOrFinish();
        }

        private bool TryStartGateForCurrentWindow()
        {
            WindowGate gate = GetGate(_currentIndex);
            if (gate == null || !gate.enabled)
                return false;

            // Hide current window
            HideWindowAtIndex(_currentIndex);

            // Resume the game (so the user can perform the required action)
            if (pauseGameWhileActive)
                ResumeGame();

            _state = FlowState.WaitingForGate;

            if (debugLogs)
                Debug.Log($"[Tutorial] Gate START at window index {_currentIndex}. requirement={gate.requirement}", this);

            gate.onGateStart?.Invoke();

            // Start gate coroutine; when done, it will pause+show next window.
            if (_gateRoutine != null)
                StopCoroutine(_gateRoutine);

            _gateRoutine = StartCoroutine(RunGateThenContinue(_currentIndex, gate));
            return true;
        }

        private IEnumerator RunGateThenContinue(int gateWindowIndex, WindowGate gate)
        {
            switch (gate.requirement)
            {
                case RequirementType.PressAllArrowKeysOnce:
                    yield return WaitForPressAllArrowKeysOnce();
                    break;

                case RequirementType.AreaClosedOnce:
                    yield return WaitForAreaClosedOnce();
                    break;

                default:
                    break;
            }

            gate.onGateComplete?.Invoke();

            float cd = Mathf.Max(0f, gate.afterCompleteCountdownSeconds);
            if (cd > 0f)
            {
                float t = 0f;
                while (t < cd)
                {
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            if (pauseGameWhileActive)
                PauseGame();

            _currentIndex = gateWindowIndex;
            _state = FlowState.ShowingWindow;

            AdvanceToNextWindowOrFinish();
            _gateRoutine = null;
        }

        private IEnumerator WaitForAreaClosedOnce()
        {
            bool done = false;

            void OnAreaClosed(object data)
            {
                // We don't care about payload right now, only that an area was closed.
                done = true;
            }

            // IMPORTANT: subscribe ONLY while this gate is active
            EventManager.StartListening(EventManager.GameEvent.AreaClosed, OnAreaClosed);

            try
            {
                while (!done)
                    yield return null;
            }
            finally
            {
                EventManager.StopListening(EventManager.GameEvent.AreaClosed, OnAreaClosed);
            }
        }

        private IEnumerator WaitForPressAllArrowKeysOnce()
        {
            bool up = false, down = false, left = false, right = false;

            while (!(up && down && left && right))
            {
                if (!up && Input.GetKeyDown(gateUpKey)) up = true;
                if (!down && Input.GetKeyDown(gateDownKey)) down = true;
                if (!left && Input.GetKeyDown(gateLeftKey)) left = true;
                if (!right && Input.GetKeyDown(gateRightKey)) right = true;

                yield return null;
            }
        }

        private void AdvanceToNextWindowOrFinish()
        {
            // Hide current if it's still visible
            HideWindowAtIndex(_currentIndex);

            int next = _currentIndex + 1;
            if (next >= WindowCount)
            {
                FinishTutorial();
                return;
            }

            ShowWindowAtIndex(next);
        }

        public void FinishTutorial()
        {
            HideAllWindows();
            _state = FlowState.Idle;
            _currentIndex = -1;

            if (_gateRoutine != null)
            {
                StopCoroutine(_gateRoutine);
                _gateRoutine = null;
            }

            if (pauseGameWhileActive && restorePreviousTimeScaleOnFinish)
                ResumeGame();

            if (debugLogs)
                Debug.Log("[Tutorial] Finished.", this);
        }

        // ===================== Window Show/Hide =====================

        private void ShowWindowAtIndex(int index)
        {
            if (windows == null || windows.Count == 0)
                return;

            index = Mathf.Clamp(index, 0, windows.Count - 1);
            _currentIndex = index;

            GameObject go = windows[_currentIndex];
            if (go != null)
                go.SetActive(true);

            _canSkipAtUnscaledTime = Time.unscaledTime + Mathf.Max(0f, skipCooldownSeconds);

            if (debugLogs)
                Debug.Log($"[Tutorial] Showing window {_currentIndex + 1}/{WindowCount}. CanSkipAt={_canSkipAtUnscaledTime:F2} (unscaled).", this);
        }

        private void HideWindowAtIndex(int index)
        {
            if (windows == null || windows.Count == 0)
                return;

            if (index < 0 || index >= windows.Count)
                return;

            GameObject go = windows[index];
            if (go != null)
                go.SetActive(false);
        }

        private void HideAllWindows()
        {
            if (windows == null)
                return;

            for (int i = 0; i < windows.Count; i++)
            {
                if (windows[i] != null)
                    windows[i].SetActive(false);
            }
        }

        private WindowGate GetGate(int windowIndex)
        {
            if (windowGates == null)
                return null;

            if (windowIndex < 0 || windowIndex >= windowGates.Count)
                return null;

            return windowGates[windowIndex];
        }

        // ===================== Pause/Resume =====================

        private void PauseGame()
        {
            if (!_prevTimeScaleCaptured)
            {
                _prevTimeScale = Time.timeScale;
                _prevTimeScaleCaptured = true;
            }

            Time.timeScale = 0f;

            if (debugLogs)
                Debug.Log($"[Tutorial] Paused game. prevTimeScale={_prevTimeScale}", this);
        }

        private void ResumeGame()
        {
            float restore = _prevTimeScaleCaptured ? _prevTimeScale : 1f;

            Time.timeScale = restore;
            _prevTimeScaleCaptured = false;

            if (debugLogs)
                Debug.Log($"[Tutorial] Resumed game. timeScale={Time.timeScale}", this);
        }

        // ===================== Intro Move (Velocity-driven) =====================

        private IEnumerator PlayIntroMove()
        {
            if (introMoveTarget == null)
            {
                if (debugLogs)
                    Debug.Log("[Tutorial] Intro move enabled but introMoveTarget is null. Skipping intro move.", this);
                yield break;
            }

            Vector3 dir = ResolveIntroDirection(introMoveTarget);
            if (dir.sqrMagnitude < 0.0001f)
            {
                if (debugLogs)
                    Debug.Log("[Tutorial] Intro direction resolved to zero. Skipping intro move.", this);
                yield break;
            }

            dir.Normalize();

            Vector3 up = Vector3.up;
            Vector3 side = Vector3.Cross(up, dir);
            if (side.sqrMagnitude < 0.0001f)
            {
                up = Vector3.forward;
                side = Vector3.Cross(up, dir);
            }
            side.Normalize();

            float duration = Mathf.Max(0.01f, introDuration);
            float distance = Mathf.Max(0f, introDistance);

            Vector3 startPos = introMoveTarget.position;

            Rigidbody rb = introMoveTarget.GetComponent<Rigidbody>();
            bool hadRb = rb != null;
            bool prevKinematic = false;

            if (introMoveTransformDirectly && hadRb && introForceKinematicIfRigidBody)
            {
                prevKinematic = rb.isKinematic;
                rb.isKinematic = true;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            float startSpeed = Mathf.Max(0f, introStartSpeed);
            float maxSpeed = Mathf.Max(startSpeed, introMaxSpeed);

            float curveArea = ComputeCurveArea01(introSpeedProfile, 200);
            float curveAvg = Mathf.Max(0.0001f, curveArea);

            // distance = duration * (startSpeed + (maxSpeed-startSpeed)*area) * k
            float unscaledDistancePerK = duration * (startSpeed + (maxSpeed - startSpeed) * curveAvg);
            float k = (unscaledDistancePerK > 0.0001f) ? (distance / unscaledDistancePerK) : 0f;

            float traveled = 0f;
            float t = 0f;

            while (t < duration && traveled < distance - 1e-4f)
            {
                float u01 = Mathf.Clamp01(t / duration);

                float profile = introSpeedProfile != null ? introSpeedProfile.Evaluate(u01) : u01;
                profile = Mathf.Clamp01(profile);

                float speed = (startSpeed + (maxSpeed - startSpeed) * profile) * k;
                float dt = Time.unscaledDeltaTime;

                float step = speed * dt;
                if (traveled + step > distance)
                    step = distance - traveled;

                traveled += step;

                Vector3 basePos = startPos + dir * traveled;

                float wob = 0f;
                if (introWobbleAmplitude > 0f && introWobbleCycles > 0f)
                {
                    float wobPhase = u01 * Mathf.PI * 2f * introWobbleCycles;
                    wob = Mathf.Sin(wobPhase) * introWobbleAmplitude;

                    float taper = 1f - Mathf.SmoothStep(0.6f, 1f, u01);
                    wob *= taper;
                }

                Vector3 finalPos = basePos + side * wob;

                ApplyIntroPosition(introMoveTarget, rb, finalPos);

                t += dt;
                yield return null;
            }

            ApplyIntroPosition(introMoveTarget, rb, startPos + dir * distance);

            if (introMoveTransformDirectly && hadRb && introForceKinematicIfRigidBody)
                rb.isKinematic = prevKinematic;
        }

        private Vector3 ResolveIntroDirection(Transform target)
        {
            switch (introDirectionMode)
            {
                case IntroMoveDirectionMode.TargetForward: return target.forward;
                case IntroMoveDirectionMode.TargetRight: return target.right;
                case IntroMoveDirectionMode.TargetUp: return target.up;

                case IntroMoveDirectionMode.ReferenceTransformForward:
                    return introDirectionReference != null ? introDirectionReference.forward : Vector3.zero;

                case IntroMoveDirectionMode.ReferenceTransformRight:
                    return introDirectionReference != null ? introDirectionReference.right : Vector3.zero;

                default:
                case IntroMoveDirectionMode.WorldVector:
                    return introWorldDirection;
            }
        }

        private void ApplyIntroPosition(Transform target, Rigidbody rb, Vector3 pos)
        {
            if (!introMoveTransformDirectly && rb != null && !rb.isKinematic)
                rb.MovePosition(pos);
            else
                target.position = pos;
        }

        private static float ComputeCurveArea01(AnimationCurve curve, int samples)
        {
            if (curve == null || samples < 2)
                return 1f;

            float area = 0f;
            float prevT = 0f;
            float prevV = Mathf.Clamp01(curve.Evaluate(0f));

            for (int i = 1; i <= samples; i++)
            {
                float t = (float)i / samples;
                float v = Mathf.Clamp01(curve.Evaluate(t));

                float dt = t - prevT;
                area += (prevV + v) * 0.5f * dt;

                prevT = t;
                prevV = v;
            }

            return area;
        }
    }
}
