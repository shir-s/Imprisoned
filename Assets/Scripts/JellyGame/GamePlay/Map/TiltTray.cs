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
        
        [Header("Input Basis (Fixed Forward)")]
        [Tooltip("World-space forward direction used for tilt input (camera independent). Example: (0,0,1).")]
        [SerializeField] private Vector3 inputForward = Vector3.forward;

        [Header("Tilt Settings")]
        [SerializeField] private float maxTiltDeg = 20f;
        [SerializeField] private float tiltAccelDegPerSec = 90f;
        [SerializeField] private float recenterDegPerSec = 60f; // Kept for future use if needed
        [SerializeField] private float followDegPerSec = 360f;

        [Header("Behavior")]
        [SerializeField] private bool physicsDriven = true;

        [Header("Camera Input")]
        [Tooltip("The camera used to determine which way is 'Right' or 'Forward'. If empty, it uses the Active Main Camera.")]
        [SerializeField] private Transform inputCamera;

        [Header("Model Fixes (Rarely Needed)")]
        [Tooltip("Only check this if the tray moves left when it should move right even on the Follow Camera.")]
        [SerializeField] private bool flipModelPhysicsX = false;
        
        [Tooltip("Only check this if the tray moves down when it should move up even on the Follow Camera.")]
        [SerializeField] private bool flipModelPhysicsZ = false;
        

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
            // 1. Get Arrow Key Input
            float rawV = (Input.GetKey(upKey) ? 1 : 0) - (Input.GetKey(downKey) ? 1 : 0);
            float rawH = (Input.GetKey(rightKey) ? 1 : 0) - (Input.GetKey(leftKey) ? 1 : 0);

            // 2. Build a stable basis using a FIXED forward vector (camera-independent)
            Vector3 trayUp = transform.up;

            Vector3 forward = Vector3.ProjectOnPlane(inputForward, trayUp);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.ProjectOnPlane(transform.forward, trayUp);

            forward.Normalize();

            // IMPORTANT FIX:
            // Right should be computed as Cross(forward, up) (not Cross(up, forward)).
            Vector3 right = Vector3.Cross(forward, trayUp).normalized;

            // 3. Desired move direction in world space (on tray plane)
            Vector3 desiredMoveDir = (forward * rawV) + (right * rawH);
            if (desiredMoveDir.sqrMagnitude > 1f) desiredMoveDir.Normalize();

            // 4. Convert World Movement -> Local Tray Tilt (use BASE axes, not current tilted axes)
            Vector3 baseForward = _baseRot * Vector3.forward;
            Vector3 baseRight = _baseRot * Vector3.right;

            float moveLocalZ = Vector3.Dot(desiredMoveDir, baseForward);
            float moveLocalX = Vector3.Dot(desiredMoveDir, baseRight);

            // 5. Calculate Goal Angles
            float goalPitch = -moveLocalZ * maxTiltDeg;
            float goalRoll  = -moveLocalX * maxTiltDeg;

            // 6. Apply Model Physics Fixes (Only for broken prefabs)
            if (flipModelPhysicsX) goalPitch *= -1f;
            if (flipModelPhysicsZ) goalRoll *= -1f;

            _targetTiltXZ = Vector2.MoveTowards(_targetTiltXZ, new Vector2(goalPitch, goalRoll), tiltAccelDegPerSec * dt);
        }



        private void DriveRotation(float dt)
        {
            _currentTiltXZ.x = Mathf.MoveTowards(_currentTiltXZ.x, _targetTiltXZ.x, followDegPerSec * dt);
            _currentTiltXZ.y = Mathf.MoveTowards(_currentTiltXZ.y, _targetTiltXZ.y, followDegPerSec * dt);

            // Use stable base axes so "pitch" always means the same thing.
            Vector3 baseRight = _baseRot * Vector3.right;
            Vector3 baseForward = _baseRot * Vector3.forward;

            Quaternion qx = Quaternion.AngleAxis(_currentTiltXZ.x, baseRight);
            Quaternion qz = Quaternion.AngleAxis(_currentTiltXZ.y, baseForward);

            if (_rb && _rb.isKinematic) _rb.MoveRotation(_baseRot * qx * qz);
            else transform.rotation = _baseRot * qx * qz;
        }


        // Maintains compatibility with your Follow Camera script
        public void SetInputCamera(Transform cam, bool enableCameraRelativeInput)
        {
            // We ignore 'enableCameraRelativeInput' because it is ALWAYS relative now.
            inputCamera = cam;
        }

        public Transform InputCamera { get => inputCamera; set => inputCamera = value; }

        private void SelectThis()
        {
            if (_current == this) return;
            if (_current != null) _current.Deselect();
            _current = this;
            UpdateTint(true);
        }

        private void Deselect()
        {
            if (_current != this) return;
            UpdateTint(false);
            _current = null;
        }

        private void UpdateTint(bool selected)
        {
            if (!tintWhenSelected || _r == null) return;
            _r.GetPropertyBlock(_mpb);
            if (selected)
            {
                if (_r.sharedMaterial.HasProperty("_Color")) 
                {
                    _origColor = _r.sharedMaterial.color;
                    _hasOrig = true;
                }
                _mpb.SetColor("_Color", selectedTint);
            }
            else
            {
                if (_hasOrig) _mpb.SetColor("_Color", _origColor);
                else _mpb.Clear();
            }
            _r.SetPropertyBlock(_mpb);
        }
        
#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.4f);
            Vector3 p = transform.position;
            Gizmos.DrawLine(p, p + transform.right * 0.5f);
            Gizmos.DrawLine(p, p + transform.forward * 0.5f);
        }
#endif
    }
}