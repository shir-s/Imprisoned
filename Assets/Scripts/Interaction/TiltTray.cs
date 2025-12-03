// FILEPATH: Assets/Scripts/Interaction/TiltTray.cs
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class TiltTray : MonoBehaviour
{
    [Header("Selection")]
    [SerializeField] private bool tintWhenSelected = true;
    [SerializeField] private Color selectedTint = new Color(1f, 0.9f, 0.25f, 1f);

    [Tooltip("If true, tray must be selected to respond to input. If false, arrow keys always control this tray.")]
    [SerializeField] private bool requireSelectionForInput = false;

    private static TiltTray _current;   // global selection (only one tray at a time)

    private Renderer _r;
    private MaterialPropertyBlock _mpb;
    private Color _origColor;
    private bool _hasOrig;

    [Header("Input (Arrow Keys)")]
    [SerializeField] private KeyCode upKey = KeyCode.UpArrow;
    [SerializeField] private KeyCode downKey = KeyCode.DownArrow;
    [SerializeField] private KeyCode leftKey = KeyCode.LeftArrow;
    [SerializeField] private KeyCode rightKey = KeyCode.RightArrow;

    [Header("Tilt")]
    [SerializeField] private float maxTiltDeg = 20f;
    [SerializeField] private float tiltAccelDegPerSec = 90f;
    [SerializeField] private float recenterDegPerSec = 60f;
    [SerializeField] private float followDegPerSec = 360f;

    [Header("Behavior")]
    [SerializeField] private bool autoRecenter = true;
    [SerializeField] private bool invertX = false;
    [SerializeField] private bool invertZ = false;
    [SerializeField] private bool physicsDriven = true;

    [Header("Camera-relative input (for follow camera)")]
    [Tooltip("If true, arrow keys are interpreted relative to the given camera's right/forward.")]
    [SerializeField] private bool useCameraRelativeInput = false;

    [Tooltip("Camera whose right/forward are used when camera-relative input is enabled.")]
    [SerializeField] private Transform inputCamera;

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

    // --------------------------------
    // SELECTION (only affects tint, and input if requireSelectionForInput = true)
    // --------------------------------
    private void OnMouseDown()
    {
        SelectThis();
    }

    private void Update()
    {
        bool activeForInput = !requireSelectionForInput || _current == this;
        if (!activeForInput)
            return;

        // right mouse button deselect (only makes sense if selection is used)
        if (requireSelectionForInput && Input.GetMouseButtonDown(1) && _current == this)
        {
            Deselect();
            return;
        }

        HandleInput(Time.deltaTime);

        if (!physicsDriven)
            DriveRotation(Time.deltaTime);
    }

    private void FixedUpdate()
    {
        bool activeForInput = !requireSelectionForInput || _current == this;
        if (!activeForInput || !physicsDriven)
            return;

        DriveRotation(Time.fixedDeltaTime);
    }

    private void HandleInput(float dt)
    {
        // Raw input from keys (camera-agnostic).
        int rawV = (Input.GetKey(upKey) ? 1 : 0) - (Input.GetKey(downKey) ? 1 : 0);
        int rawH = (Input.GetKey(rightKey) ? 1 : 0) - (Input.GetKey(leftKey) ? 1 : 0);

        float v = rawV;
        float h = rawH;

        // If enabled, reinterpret input relative to a camera's right/forward on the tray plane.
        if (useCameraRelativeInput && inputCamera != null && (rawV != 0 || rawH != 0))
        {
            Vector3 trayUp = transform.up;

            // Project camera axes onto the tray plane so tilt is always along the surface.
            Vector3 camRight = Vector3.ProjectOnPlane(inputCamera.right, trayUp).normalized;
            Vector3 camForward = Vector3.ProjectOnPlane(inputCamera.forward, trayUp).normalized;

            // Desired move direction on the tray, from player's point of view.
            // Up arrow = "forward from camera", Right arrow = "right from camera".
            Vector3 desiredDirWorld = camForward * rawV + camRight * rawH;

            if (desiredDirWorld.sqrMagnitude > 0.0001f)
            {
                desiredDirWorld.Normalize();

                // Express that direction in the tray's local basis (forward/right).
                float alongForward = Vector3.Dot(desiredDirWorld, transform.forward);
                float alongRight = Vector3.Dot(desiredDirWorld, transform.right);

                v = alongForward;
                h = alongRight;
            }
            else
            {
                v = 0f;
                h = 0f;
            }
        }

        float xSign = invertX ? -1f : 1f;
        float zSign = invertZ ? -1f : 1f;

        _targetTiltXZ.x += xSign * v * tiltAccelDegPerSec * dt;
        _targetTiltXZ.y += zSign * -h * tiltAccelDegPerSec * dt;

        _targetTiltXZ.x = Mathf.Clamp(_targetTiltXZ.x, -maxTiltDeg, maxTiltDeg);
        _targetTiltXZ.y = Mathf.Clamp(_targetTiltXZ.y, -maxTiltDeg, maxTiltDeg);

        // Recentering is based on whether the player is pressing keys, not the camera mapping.
        if (autoRecenter && rawV == 0)
            _targetTiltXZ.x = MoveToward(_targetTiltXZ.x, 0f, recenterDegPerSec * dt);
        if (autoRecenter && rawH == 0)
            _targetTiltXZ.y = MoveToward(_targetTiltXZ.y, 0f, recenterDegPerSec * dt);
    }

    private void DriveRotation(float dt)
    {
        _currentTiltXZ.x = MoveToward(_currentTiltXZ.x, _targetTiltXZ.x, followDegPerSec * dt);
        _currentTiltXZ.y = MoveToward(_currentTiltXZ.y, _targetTiltXZ.y, followDegPerSec * dt);

        Quaternion qx = Quaternion.AngleAxis(_currentTiltXZ.x, transform.right);
        Quaternion qz = Quaternion.AngleAxis(_currentTiltXZ.y, transform.forward);
        Quaternion target = _baseRot * qx * qz;

        if (_rb && _rb.isKinematic)
            _rb.MoveRotation(target);
        else
            transform.rotation = target;
    }

    private static float MoveToward(float current, float target, float maxDelta)
        => Mathf.MoveTowards(current, target, maxDelta);

    /// <summary>
    /// Configure which camera should define "left/right/up" for camera-relative controls.
    /// Call this from your camera-switch script when toggling between top and follow cameras.
    /// </summary>
    public void SetInputCamera(Transform cam, bool enableCameraRelativeInput)
    {
        inputCamera = cam;
        useCameraRelativeInput = enableCameraRelativeInput;
    }

    public Transform InputCamera
    {
        get => inputCamera;
        set => inputCamera = value;
    }

    public bool UseCameraRelativeInput
    {
        get => useCameraRelativeInput;
        set => useCameraRelativeInput = value;
    }

    // --------------------------------
    // SELECT / DESELECT (only visual by default)
    // --------------------------------
    private void SelectThis()
    {
        if (_current == this) return;
        if (_current != null) _current.Deselect();
        _current = this;

        if (tintWhenSelected && _r != null)
        {
            _r.GetPropertyBlock(_mpb);
            if (_r.sharedMaterial && _r.sharedMaterial.HasProperty("_Color"))
            {
                _origColor = _r.sharedMaterial.color;
                _hasOrig = true;
            }
            _mpb.SetColor("_Color", selectedTint);
            _r.SetPropertyBlock(_mpb);
        }
    }

    private void Deselect()
    {
        if (_current != this) return;

        if (tintWhenSelected && _r != null)
        {
            _r.GetPropertyBlock(_mpb);
            if (_hasOrig) _mpb.SetColor("_Color", _origColor);
            else _mpb.Clear();
            _r.SetPropertyBlock(_mpb);
        }

        _current = null;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.4f);
        Vector3 p = transform.position;
        Gizmos.DrawLine(p, p + transform.right * 0.5f);
        Gizmos.DrawLine(p, p + transform.forward * 0.5f);
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.2f);
        Gizmos.DrawWireSphere(p, 0.2f);
    }
#endif
}
