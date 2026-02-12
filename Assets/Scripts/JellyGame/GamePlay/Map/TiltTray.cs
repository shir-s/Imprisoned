// FILEPATH: Assets/Scripts/Interaction/TiltTray.cs

using UnityEngine;

namespace JellyGame.GamePlay.Map
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class TiltTray : MonoBehaviour
    {
        [Header("Selection")]
        [SerializeField] private bool tintWhenSelected = true;
        [SerializeField] private Color selectedTint = new Color(1f, 0.9f, 0.25f, 1f);
        [SerializeField] private bool requireSelectionForInput = false;

        private static TiltTray _current;
        private Renderer _r;
        private MaterialPropertyBlock _mpb;
        private Color _origColor;
        private bool _hasOrig;

        [Header("Input (Arrow Keys)")]
        [SerializeField] private KeyCode upKey = KeyCode.UpArrow;
        [SerializeField] private KeyCode downKey = KeyCode.DownArrow;
        [SerializeField] private KeyCode leftKey = KeyCode.LeftArrow;
        [SerializeField] private KeyCode rightKey = KeyCode.RightArrow;
        
        [Header("Input (Controller Settings)")]
        [Tooltip("Axis name in Input Manager.")]
        [SerializeField] private string horizontalAxisName = "Horizontal";
        [SerializeField] private string verticalAxisName = "Vertical";
        
        [Tooltip("Make small movements precise and big movements fast. 1 = Linear, 2 = Exponential (Recommended: 1.5 to 2).")]
        [Range(1f, 3f)] 
        [SerializeField] private float responseCurve = 2.0f; 

        [Tooltip("Higher = reaches max tilt faster. Combine with Curve for best feel.")]
        [Range(0.1f, 3f)] 
        [SerializeField] private float controllerSensitivity = 1.5f;

        [Tooltip("Deadzone to prevent drift.")]
        [Range(0f, 0.5f)] 
        [SerializeField] private float controllerDeadzone = 0.1f;

        [Tooltip("Check this if pushing UP should tilt the tray UP (instead of forward/down).")]
        [SerializeField] private bool invertVertical = false;

        [Header("Input Basis (Fixed Forward)")]
        [SerializeField] private Vector3 inputForward = Vector3.forward;

        [Header("Tilt Settings (Physics)")]
        [SerializeField] private float maxTiltDeg = 20f;
        [SerializeField] private float tiltAccelDegPerSec = 90f;
        [SerializeField] private float recenterDegPerSec = 60f; 
        [SerializeField] private float followDegPerSec = 360f;

        [Header("Behavior")]
        [SerializeField] private bool physicsDriven = true;

        [Header("Camera Input")]
        [SerializeField] private Transform inputCamera;

        [Header("Model Fixes")]
        [SerializeField] private bool flipModelPhysicsX = false;
        [SerializeField] private bool flipModelPhysicsZ = false;

        [Header("Reset On Disable")]
        [Tooltip("When disabled, snap tray back to flat so the slime stops sliding.")]
        [SerializeField] private bool resetTiltOnDisable = true;
        

        private Rigidbody _rb;
        private Quaternion _baseRot;
        private Vector2 _targetTiltXZ;
        private Vector2 _currentTiltXZ;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.isKinematic = true;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _baseRot = transform.rotation;
            _r = GetComponentInChildren<Renderer>();
            if (_r) _mpb = new MaterialPropertyBlock();
        }

        /// <summary>
        /// Clear stale static reference after scene reload.
        /// Without this, requireSelectionForInput permanently blocks input
        /// because _current still points to the destroyed TiltTray from the old scene.
        /// </summary>
        private void OnEnable()
        {
            // If the previous _current was destroyed (scene unload), clear the stale reference
            if (_current != null && !_current)
                _current = null;

            // If requireSelectionForInput is false AND no tray is selected, auto-select this one
            if (!requireSelectionForInput && _current == null)
                _current = this;
        }

        private void OnDisable()
        {
            // If this tray is being disabled/destroyed, clear the static reference
            if (_current == this)
                _current = null;

            // Reset tilt to flat so the slime doesn't keep sliding
            if (resetTiltOnDisable)
            {
                _targetTiltXZ = Vector2.zero;
                _currentTiltXZ = Vector2.zero;

                if (_rb && _rb.isKinematic)
                    _rb.MoveRotation(_baseRot);
                else
                    transform.rotation = _baseRot;
            }
        }

        private void OnMouseDown() { SelectThis(); }

        private void Update()
        {
            bool activeForInput = !requireSelectionForInput || _current == this;
            if (!activeForInput) return;

            if (requireSelectionForInput && Input.GetMouseButtonDown(1) && _current == this)
            {
                Deselect();
                return;
            }

            HandleInput(Time.deltaTime);

            if (!physicsDriven) DriveRotation(Time.deltaTime);
        }

        private void FixedUpdate()
        {
            if ((!requireSelectionForInput || _current == this) && physicsDriven)
                DriveRotation(Time.fixedDeltaTime);
        }

        private void HandleInput(float dt)
        {
            // 1. Keyboard Input (Original & Sharp)
            float inputV = (Input.GetKey(upKey) ? 1 : 0) - (Input.GetKey(downKey) ? 1 : 0);
            float inputH = (Input.GetKey(rightKey) ? 1 : 0) - (Input.GetKey(leftKey) ? 1 : 0);

            // 2. Controller Input (Only if keyboard is idle)
            if (Mathf.Approximately(inputV, 0f) && Mathf.Approximately(inputH, 0f))
            {
                float joyV = Input.GetAxis(verticalAxisName);
                float joyH = Input.GetAxis(horizontalAxisName);

                Vector2 joyVec = new Vector2(joyH, joyV);
                float magnitude = joyVec.magnitude;

                // Deadzone Check
                if (magnitude < controllerDeadzone)
                {
                    joyVec = Vector2.zero;
                }
                else
                {
                    // Normalized vector (direction only)
                    Vector2 direction = joyVec.normalized;
                    
                    // Remap magnitude from [deadzone...1] to [0...1]
                    // This avoids the "jump" when leaving the deadzone
                    float effectiveMag = Mathf.InverseLerp(controllerDeadzone, 1f, magnitude);
                    
                    // Apply Response Curve (Mathf.Pow)
                    // If curve is 2: 0.5 input becomes 0.25 output (finer control)
                    // 1.0 input stays 1.0 output
                    effectiveMag = Mathf.Pow(effectiveMag, responseCurve);
                    
                    // Apply Sensitivity
                    effectiveMag *= controllerSensitivity;

                    // Reconstruct vector
                    joyVec = direction * effectiveMag;
                }

                inputH = joyVec.x;
                inputV = joyVec.y;

                // Apply Invert
                if (invertVertical) inputV *= -1f;
            }

            // Clamp final magnitude to 1
            Vector2 combinedInput = new Vector2(inputH, inputV);
            if (combinedInput.magnitude > 1f) combinedInput.Normalize();
            
            float finalH = combinedInput.x;
            float finalV = combinedInput.y;

            // ---------------------------------------------------------
            // PHYSICS CALCULATION
            // ---------------------------------------------------------
            Vector3 trayUp = transform.up;

            Vector3 forward = Vector3.ProjectOnPlane(inputForward, trayUp);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.ProjectOnPlane(transform.forward, trayUp);

            forward.Normalize();
            Vector3 right = Vector3.Cross(forward, trayUp).normalized;

            Vector3 desiredMoveDir = (forward * finalV) + (right * finalH);

            Vector3 baseForward = _baseRot * Vector3.forward;
            Vector3 baseRight = _baseRot * Vector3.right;

            float moveLocalZ = Vector3.Dot(desiredMoveDir, baseForward);
            float moveLocalX = Vector3.Dot(desiredMoveDir, baseRight);

            float goalPitch = -moveLocalZ * maxTiltDeg;
            float goalRoll  = -moveLocalX * maxTiltDeg;

            if (flipModelPhysicsX) goalPitch *= -1f;
            if (flipModelPhysicsZ) goalRoll *= -1f;

            _targetTiltXZ = Vector2.MoveTowards(_targetTiltXZ, new Vector2(goalPitch, goalRoll), tiltAccelDegPerSec * dt);
        }

        private void DriveRotation(float dt)
        {
            _currentTiltXZ.x = Mathf.MoveTowards(_currentTiltXZ.x, _targetTiltXZ.x, followDegPerSec * dt);
            _currentTiltXZ.y = Mathf.MoveTowards(_currentTiltXZ.y, _targetTiltXZ.y, followDegPerSec * dt);

            Vector3 baseRight = _baseRot * Vector3.right;
            Vector3 baseForward = _baseRot * Vector3.forward;

            Quaternion qx = Quaternion.AngleAxis(_currentTiltXZ.x, baseRight);
            Quaternion qz = Quaternion.AngleAxis(_currentTiltXZ.y, baseForward);

            if (_rb && _rb.isKinematic) _rb.MoveRotation(_baseRot * qx * qz);
            else transform.rotation = _baseRot * qx * qz;
        }

        public void SetInputCamera(Transform cam, bool enableCameraRelativeInput) { inputCamera = cam; }
        public Transform InputCamera { get => inputCamera; set => inputCamera = value; }
        private void SelectThis() { if (_current == this) return; if (_current != null) _current.Deselect(); _current = this; UpdateTint(true); }
        private void Deselect() { if (_current != this) return; UpdateTint(false); _current = null; }
        private void UpdateTint(bool selected) { if (!tintWhenSelected || _r == null) return; _r.GetPropertyBlock(_mpb); if (selected) { if (_r.sharedMaterial.HasProperty("_Color")) { _origColor = _r.sharedMaterial.color; _hasOrig = true; } _mpb.SetColor("_Color", selectedTint); } else { if (_hasOrig) _mpb.SetColor("_Color", _origColor); else _mpb.Clear(); } _r.SetPropertyBlock(_mpb); }
        
#if UNITY_EDITOR
        private void OnDrawGizmosSelected() { Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.4f); Vector3 p = transform.position; Gizmos.DrawLine(p, p + transform.right * 0.5f); Gizmos.DrawLine(p, p + transform.forward * 0.5f); }
#endif
    }
}