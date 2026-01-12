using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using JellyGame.GamePlay.Managers;

namespace JellyGame.UI.Tutorial
{
    [DisallowMultipleComponent]
    public class TutorialSequenceController : MonoBehaviour
    {
        // ===================== Windows =====================
        [Header("Windows (in order)")]
        [SerializeField] private List<GameObject> windows = new List<GameObject>();

        // ===================== Pause =====================
        [Header("Pause")]
        [SerializeField] private bool pauseGameWhileActive = true;
        [SerializeField] private bool restorePreviousTimeScaleOnFinish = true;

        // ===================== Skip =====================
        [Header("Skip")]
        [SerializeField] private float skipCooldownSeconds = 0.75f;
        
        [Tooltip("Keyboard key to skip/advance.")]
        [SerializeField] private KeyCode skipKey = KeyCode.E;

        [Tooltip("Controller buttons to skip/advance. Add JoystickButton0 (PC) AND JoystickButton1 (Mac).")]
        [SerializeField] private List<KeyCode> gamepadSkipButtons = new List<KeyCode> 
        { 
            KeyCode.JoystickButton0, 
            KeyCode.JoystickButton1 
        };

        [SerializeField] private bool requireKeyDown = true;

        // ===================== Flow =====================
        [Header("Flow")]
        [SerializeField] private bool autoStart = false;
        [SerializeField] private bool endImmediatelyIfNoWindows = true;

        // ===================== Script enabling/disabling =====================
        [Header("Script Locks (Disabled During Tutorial)")]
        [SerializeField] private List<Component> disableOnTutorialStart = new List<Component>();

        [SerializeField] private bool restoreDisabledScriptsOnFinish = true;

        [Serializable]
        private class WindowScriptActions
        {
            public int windowIndex = 0;
            [Header("When this window is SHOWN")]
            public List<Component> enableOnShow = new List<Component>();
            public List<Component> disableOnShow = new List<Component>();

            [Header("When this window is COMPLETED")]
            public List<Component> enableOnComplete = new List<Component>();
            public List<Component> disableOnComplete = new List<Component>();
        }

        [Header("Per-Window Script Actions")]
        [SerializeField] private List<WindowScriptActions> windowScriptActions = new List<WindowScriptActions>();

        // ===================== Intro Move =====================
        [Header("Intro Move (Before Tutorial)")]
        [SerializeField] private bool playIntroMoveBeforeTutorial = true;
        [SerializeField] private Transform introMoveTarget;

        public enum IntroMoveDirectionMode { WorldVector, TargetForward, TargetRight, TargetUp, ReferenceTransformForward, ReferenceTransformRight }

        [SerializeField] private IntroMoveDirectionMode introDirectionMode = IntroMoveDirectionMode.WorldVector;
        [SerializeField] private Vector3 introWorldDirection = Vector3.forward;
        [SerializeField] private Transform introDirectionReference;

        [SerializeField] private float introDistance = 2.0f;
        [SerializeField] private float introDuration = 0.35f;
        [SerializeField] private float introStartSpeed = 0.0f;
        [SerializeField] private float introMaxSpeed = 12.0f;
        [SerializeField] private AnimationCurve introSpeedProfile = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0f));
        [SerializeField] private float introWobbleAmplitude = 0.08f;
        [SerializeField] private float introWobbleCycles = 2.0f;
        [SerializeField] private bool introMoveTransformDirectly = true;
        [SerializeField] private bool introForceKinematicIfRigidBody = true;

        // ===================== Gates =====================
        private enum RequirementType { PressAllArrowKeysOnce, AreaClosedOnce, PickupCollectedOnce, EnemyKilledOnce }

        [Serializable]
        private class WindowGate
        {
            public bool enabled = false;
            public RequirementType requirement = RequirementType.PressAllArrowKeysOnce;
            public float afterCompleteCountdownSeconds = 0.5f;
            public GameObject activateObjectOnGateStart;
            public bool deactivateObjectOnGateComplete = false;
            public string enemyLayerName = "Enemy";
            public UnityEvent onGateStart;
            public UnityEvent onGateComplete;
        }

        [Header("Window Gates")]
        [SerializeField] private List<WindowGate> windowGates = new List<WindowGate>();

        [Header("Gate Input (Keyboard)")]
        [SerializeField] private KeyCode gateUpKey = KeyCode.UpArrow;
        [SerializeField] private KeyCode gateDownKey = KeyCode.DownArrow;
        [SerializeField] private KeyCode gateLeftKey = KeyCode.LeftArrow;
        [SerializeField] private KeyCode gateRightKey = KeyCode.RightArrow;
        
        // --- NEW: Controller Axes Support for Gates ---
        [Header("Gate Input (Controller Axes)")]
        [Tooltip("Axis name for Up/Down check (e.g. Vertical).")]
        [SerializeField] private string gateVerticalAxis = "Vertical";
        [Tooltip("Axis name for Left/Right check (e.g. Horizontal).")]
        [SerializeField] private string gateHorizontalAxis = "Horizontal";
        [Tooltip("How far the stick needs to be pushed to count as a 'press' (0.5 is half-way).")]
        [SerializeField] private float axisThreshold = 0.5f;


        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        // ===================== Runtime =====================
        private enum FlowState { Idle, ShowingWindow, WaitingForGate }

        private FlowState _state = FlowState.Idle;
        private int _currentIndex = -1;
        private float _canSkipAtUnscaledTime = 0f;
        private float _prevTimeScale = 1f;
        private bool _prevTimeScaleCaptured = false;
        private Coroutine _flowRoutine;
        private Coroutine _gateRoutine;

        private struct BehaviourState
        {
            public Behaviour b;
            public bool wasEnabled;
            public BehaviourState(Behaviour b, bool wasEnabled) { this.b = b; this.wasEnabled = wasEnabled; }
        }

        private readonly List<BehaviourState> _startDisabledSnapshot = new List<BehaviourState>();

        private void Start()
        {
            HideAllWindows();
            if (autoStart) StartTutorial();
        }

        private void Update()
        {
            if (_state != FlowState.ShowingWindow) return;
            if (_currentIndex < 0 || _currentIndex >= WindowCount) return;
            if (Time.unscaledTime < _canSkipAtUnscaledTime) return;

            bool pressed = false;
            if (requireKeyDown) { if (Input.GetKeyDown(skipKey)) pressed = true; }
            else { if (Input.GetKey(skipKey)) pressed = true; }

            if (!pressed && gamepadSkipButtons != null)
            {
                for (int i = 0; i < gamepadSkipButtons.Count; i++)
                {
                    KeyCode btn = gamepadSkipButtons[i];
                    if (requireKeyDown) { if (Input.GetKeyDown(btn)) pressed = true; }
                    else { if (Input.GetKey(btn)) pressed = true; }
                    if (pressed) break;
                }
            }

            if (pressed) SkipCurrentWindow();
        }

        public int WindowCount => windows != null ? windows.Count : 0;

        public void StartTutorial()
        {
            if (_flowRoutine != null) StopCoroutine(_flowRoutine);
            if (_gateRoutine != null) { StopCoroutine(_gateRoutine); _gateRoutine = null; }
            _flowRoutine = StartCoroutine(BeginFlow());
        }

        private IEnumerator BeginFlow()
        {
            _state = FlowState.Idle;
            _currentIndex = -1;
            HideAllWindows();
            CaptureAndDisableStartScripts();

            if (WindowCount == 0)
            {
                if (debugLogs) Debug.Log("[Tutorial] StartTutorial called but windows list is empty.", this);
                if (endImmediatelyIfNoWindows) FinishTutorial();
                yield break;
            }

            if (playIntroMoveBeforeTutorial) yield return PlayIntroMove();
            StartTutorialSequenceOnly();
        }

        private void StartTutorialSequenceOnly()
        {
            if (_state != FlowState.Idle) return;
            _state = FlowState.ShowingWindow;
            if (pauseGameWhileActive) PauseGame();
            ShowWindowAtIndex(0);
        }

        public void SkipCurrentWindow()
        {
            if (_state != FlowState.ShowingWindow) return;
            if (_currentIndex < 0 || _currentIndex >= WindowCount) return;

            ApplyWindowCompleteScriptActions(_currentIndex);

            if (TryStartGateForCurrentWindow()) return;
            AdvanceToNextWindowOrFinish();
        }

        private bool TryStartGateForCurrentWindow()
        {
            WindowGate gate = GetGate(_currentIndex);
            if (gate == null || !gate.enabled) return false;

            HideWindowAtIndex(_currentIndex);
            if (gate.activateObjectOnGateStart != null) gate.activateObjectOnGateStart.SetActive(true);
            if (pauseGameWhileActive) ResumeGame();

            _state = FlowState.WaitingForGate;
            gate.onGateStart?.Invoke();

            if (_gateRoutine != null) StopCoroutine(_gateRoutine);
            _gateRoutine = StartCoroutine(RunGateThenContinue(_currentIndex, gate));
            return true;
        }

        private IEnumerator RunGateThenContinue(int gateWindowIndex, WindowGate gate)
        {
            switch (gate.requirement)
            {
                case RequirementType.PressAllArrowKeysOnce: yield return WaitForPressAllArrowKeysOnce(); break;
                case RequirementType.AreaClosedOnce: yield return WaitForAreaClosedOnce(); break;
                case RequirementType.PickupCollectedOnce: yield return WaitForPickupCollectedOnce(); break;
                case RequirementType.EnemyKilledOnce: yield return WaitForEnemyKilledOnce(gate.enemyLayerName); break;
            }

            gate.onGateComplete?.Invoke();
            if (gate.deactivateObjectOnGateComplete && gate.activateObjectOnGateStart != null)
                gate.activateObjectOnGateStart.SetActive(false);

            float cd = Mathf.Max(0f, gate.afterCompleteCountdownSeconds);
            if (cd > 0f)
            {
                float t = 0f;
                while (t < cd) { t += Time.unscaledDeltaTime; yield return null; }
            }

            if (pauseGameWhileActive) PauseGame();
            _currentIndex = gateWindowIndex;
            _state = FlowState.ShowingWindow;
            AdvanceToNextWindowOrFinish();
            _gateRoutine = null;
        }

        // ===================== Requirements =====================
        private IEnumerator WaitForPressAllArrowKeysOnce()
        {
            bool up = false, down = false, left = false, right = false;

            // Loop until ALL directions have been triggered at least once
            while (!(up && down && left && right))
            {
                // 1. Check Keyboard
                if (!up && Input.GetKeyDown(gateUpKey)) up = true;
                if (!down && Input.GetKeyDown(gateDownKey)) down = true;
                if (!left && Input.GetKeyDown(gateLeftKey)) left = true;
                if (!right && Input.GetKeyDown(gateRightKey)) right = true;
                
                // 2. Check Controller Stick (Axes)
                // We use GetAxisRaw or GetAxis. Raw is snappier for checks.
                float v = Input.GetAxis(gateVerticalAxis);
                float h = Input.GetAxis(gateHorizontalAxis);

                // Threshold check (e.g. if stick pushed more than 50%)
                if (!up && v > axisThreshold) up = true;
                if (!down && v < -axisThreshold) down = true;
                if (!right && h > axisThreshold) right = true;
                if (!left && h < -axisThreshold) left = true;

                yield return null;
            }
        }

        private IEnumerator WaitForAreaClosedOnce()
        {
            bool done = false;
            void OnAreaClosed(object data) => done = true;
            EventManager.StartListening(EventManager.GameEvent.AreaClosed, OnAreaClosed);
            try { while (!done) yield return null; }
            finally { EventManager.StopListening(EventManager.GameEvent.AreaClosed, OnAreaClosed); }
        }

        private IEnumerator WaitForPickupCollectedOnce()
        {
            bool done = false;
            void OnPickupCollected(object data) => done = true;
            EventManager.StartListening(EventManager.GameEvent.PickupCollected, OnPickupCollected);
            try { while (!done) yield return null; }
            finally { EventManager.StopListening(EventManager.GameEvent.PickupCollected, OnPickupCollected); }
        }

        private IEnumerator WaitForEnemyKilledOnce(string enemyLayerName)
        {
            bool done = false;
            int enemyLayer = LayerMask.NameToLayer(enemyLayerName);
            if (enemyLayer < 0) { Debug.LogError($"[Tutorial] Layer '{enemyLayerName}' missing.", this); yield break; }

            void OnEntityDied(object data)
            {
                if (data is EntityDiedEventData died) { if (died.VictimLayer == enemyLayer) done = true; }
                else if (data is GameObject go) { if (go.layer == enemyLayer) done = true; }
            }
            EventManager.StartListening(EventManager.GameEvent.EntityDied, OnEntityDied);
            try { while (!done) yield return null; }
            finally { EventManager.StopListening(EventManager.GameEvent.EntityDied, OnEntityDied); }
        }

        // ===================== Helpers =====================
        private void AdvanceToNextWindowOrFinish()
        {
            HideWindowAtIndex(_currentIndex);
            int next = _currentIndex + 1;
            if (next >= WindowCount) { FinishTutorial(); return; }
            ShowWindowAtIndex(next);
        }

        public void FinishTutorial()
        {
            HideAllWindows();
            _state = FlowState.Idle;
            _currentIndex = -1;
            if (_gateRoutine != null) { StopCoroutine(_gateRoutine); _gateRoutine = null; }
            if (pauseGameWhileActive && restorePreviousTimeScaleOnFinish) ResumeGame();
            RestoreStartScriptsIfNeeded();
        }

        private void ShowWindowAtIndex(int index)
        {
            if (windows == null || windows.Count == 0) return;
            index = Mathf.Clamp(index, 0, windows.Count - 1);
            _currentIndex = index;
            ApplyWindowShowScriptActions(_currentIndex);
            if (windows[_currentIndex] != null) windows[_currentIndex].SetActive(true);
            _canSkipAtUnscaledTime = Time.unscaledTime + Mathf.Max(0f, skipCooldownSeconds);
        }

        private void HideWindowAtIndex(int index)
        {
            if (windows == null || index < 0 || index >= windows.Count) return;
            if (windows[index] != null) windows[index].SetActive(false);
        }

        private void HideAllWindows()
        {
            if (windows == null) return;
            for (int i = 0; i < windows.Count; i++) if (windows[i] != null) windows[i].SetActive(false);
        }

        private WindowGate GetGate(int windowIndex)
        {
            if (windowGates == null || windowIndex < 0 || windowIndex >= windowGates.Count) return null;
            return windowGates[windowIndex];
        }

        private void CaptureAndDisableStartScripts()
        {
            _startDisabledSnapshot.Clear();
            if (disableOnTutorialStart == null) return;
            for (int i = 0; i < disableOnTutorialStart.Count; i++)
            {
                Component c = disableOnTutorialStart[i];
                if (c == null) continue;
                if (c is Behaviour b) { _startDisabledSnapshot.Add(new BehaviourState(b, b.enabled)); b.enabled = false; }
            }
        }

        private void RestoreStartScriptsIfNeeded()
        {
            if (!restoreDisabledScriptsOnFinish) { _startDisabledSnapshot.Clear(); return; }
            for (int i = 0; i < _startDisabledSnapshot.Count; i++) { var s = _startDisabledSnapshot[i]; if (s.b != null) s.b.enabled = s.wasEnabled; }
            _startDisabledSnapshot.Clear();
        }

        private void ApplyWindowShowScriptActions(int windowIndex)
        {
            WindowScriptActions a = GetWindowScriptActions(windowIndex);
            if (a == null) return;
            SetEnabled(a.disableOnShow, false); SetEnabled(a.enableOnShow, true);
        }

        private void ApplyWindowCompleteScriptActions(int windowIndex)
        {
            WindowScriptActions a = GetWindowScriptActions(windowIndex);
            if (a == null) return;
            SetEnabled(a.disableOnComplete, false); SetEnabled(a.enableOnComplete, true);
        }

        private WindowScriptActions GetWindowScriptActions(int windowIndex)
        {
            if (windowScriptActions == null) return null;
            for (int i = 0; i < windowScriptActions.Count; i++) if (windowScriptActions[i].windowIndex == windowIndex) return windowScriptActions[i];
            return null;
        }

        private static void SetEnabled(List<Component> list, bool enabled)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++) { Component c = list[i]; if (c is Behaviour b) b.enabled = enabled; }
        }

        private void PauseGame()
        {
            if (!_prevTimeScaleCaptured) { _prevTimeScale = Time.timeScale; _prevTimeScaleCaptured = true; }
            Time.timeScale = 0f;
        }

        private void ResumeGame()
        {
            Time.timeScale = _prevTimeScaleCaptured ? _prevTimeScale : 1f;
            _prevTimeScaleCaptured = false;
        }

        private IEnumerator PlayIntroMove()
        {
            if (introMoveTarget == null) yield break;
            Vector3 dir = ResolveIntroDirection(introMoveTarget);
            if (dir.sqrMagnitude < 0.0001f) yield break;
            dir.Normalize();

            Vector3 up = Vector3.up;
            Vector3 side = Vector3.Cross(up, dir);
            if (side.sqrMagnitude < 0.0001f) { up = Vector3.forward; side = Vector3.Cross(up, dir); }
            side.Normalize();

            float duration = Mathf.Max(0.01f, introDuration);
            float distance = Mathf.Max(0f, introDistance);
            Vector3 startPos = introMoveTarget.position;
            Rigidbody rb = introMoveTarget.GetComponent<Rigidbody>();
            bool hadRb = rb != null;
            bool prevKinematic = false;

            if (introMoveTransformDirectly && hadRb && introForceKinematicIfRigidBody)
            {
                prevKinematic = rb.isKinematic; rb.isKinematic = true; rb.angularVelocity = Vector3.zero;
#if UNITY_6000_0_OR_NEWER
                rb.linearVelocity = Vector3.zero;
#else
                rb.velocity = Vector3.zero;
#endif
            }

            float startSpeed = Mathf.Max(0f, introStartSpeed);
            float maxSpeed = Mathf.Max(startSpeed, introMaxSpeed);
            float curveArea = ComputeCurveArea01(introSpeedProfile, 200);
            float curveAvg = Mathf.Max(0.0001f, curveArea);
            float unscaledDistancePerK = duration * (startSpeed + (maxSpeed - startSpeed) * curveAvg);
            float k = (unscaledDistancePerK > 0.0001f) ? (distance / unscaledDistancePerK) : 0f;

            float traveled = 0f;
            float t = 0f;

            while (t < duration && traveled < distance - 1e-4f)
            {
                float u01 = Mathf.Clamp01(t / duration);
                float profile = introSpeedProfile != null ? introSpeedProfile.Evaluate(u01) : u01;
                float speed = (startSpeed + (maxSpeed - startSpeed) * profile) * k;
                float dt = Time.unscaledDeltaTime;
                float step = speed * dt;
                if (traveled + step > distance) step = distance - traveled;
                traveled += step;

                Vector3 basePos = startPos + dir * traveled;
                float wob = 0f;
                if (introWobbleAmplitude > 0f && introWobbleCycles > 0f)
                {
                    float wobPhase = u01 * Mathf.PI * 2f * introWobbleCycles;
                    wob = Mathf.Sin(wobPhase) * introWobbleAmplitude * (1f - Mathf.SmoothStep(0.6f, 1f, u01));
                }

                ApplyIntroPosition(introMoveTarget, rb, basePos + side * wob);
                t += dt;
                yield return null;
            }

            ApplyIntroPosition(introMoveTarget, rb, startPos + dir * distance);
            if (introMoveTransformDirectly && hadRb && introForceKinematicIfRigidBody) rb.isKinematic = prevKinematic;
        }

        private Vector3 ResolveIntroDirection(Transform target)
        {
            switch (introDirectionMode)
            {
                case IntroMoveDirectionMode.TargetForward: return target.forward;
                case IntroMoveDirectionMode.TargetRight: return target.right;
                case IntroMoveDirectionMode.TargetUp: return target.up;
                case IntroMoveDirectionMode.ReferenceTransformForward: return introDirectionReference != null ? introDirectionReference.forward : Vector3.zero;
                case IntroMoveDirectionMode.ReferenceTransformRight: return introDirectionReference != null ? introDirectionReference.right : Vector3.zero;
                default: return introWorldDirection;
            }
        }

        private void ApplyIntroPosition(Transform target, Rigidbody rb, Vector3 pos)
        {
            if (!introMoveTransformDirectly && rb != null && !rb.isKinematic) rb.MovePosition(pos);
            else target.position = pos;
        }

        private static float ComputeCurveArea01(AnimationCurve curve, int samples)
        {
            if (curve == null || samples < 2) return 1f;
            float area = 0f, prevT = 0f, prevV = Mathf.Clamp01(curve.Evaluate(0f));
            for (int i = 1; i <= samples; i++)
            {
                float t = (float)i / samples; float v = Mathf.Clamp01(curve.Evaluate(t));
                area += (prevV + v) * 0.5f * (t - prevT);
                prevT = t; prevV = v;
            }
            return area;
        }
    }
}