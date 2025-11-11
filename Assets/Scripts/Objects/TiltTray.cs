// FILEPATH: Assets/Scripts/Interaction/TiltTray.cs
using UnityEngine;

/// <summary>
/// Tiltable tray controlled by Arrow keys (WASD untouched).
/// - Tilts around local X (Up/Down arrows) and Z (Left/Right arrows).
/// - Clamps to Max Tilt and smoothly returns to level when no input.
/// - Designed to be kinematic so physics objects on top slide due to gravity.
/// Tips:
///   • Assign a low-friction PhysicMaterial to this object's Collider (e.g., Dynamic/Static Friction ~ 0.01).
///   • Put your drawing cube (with Rigidbody) on top/inside; it will slide as you tilt.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class TiltTray : MonoBehaviour
{
    [Header("Input (Arrow Keys)")]
    [SerializeField] private KeyCode upKey = KeyCode.UpArrow;
    [SerializeField] private KeyCode downKey = KeyCode.DownArrow;
    [SerializeField] private KeyCode leftKey = KeyCode.LeftArrow;
    [SerializeField] private KeyCode rightKey = KeyCode.RightArrow;

    [Header("Tilt")]
    [Tooltip("Maximum absolute tilt around local X/Z (degrees).")]
    [SerializeField] private float maxTiltDeg = 20f;

    [Tooltip("How fast target tilt changes while holding arrows (deg/sec).")]
    [SerializeField] private float tiltAccelDegPerSec = 90f;

    [Tooltip("How fast the tray recenters when no arrow is held (deg/sec).")]
    [SerializeField] private float recenterDegPerSec = 60f;

    [Tooltip("Smoothing for the actual pose following the target (deg/sec).")]
    [SerializeField] private float followDegPerSec = 360f;

    [Header("Behavior")]
    [Tooltip("If true, tray returns to flat when there is no arrow input.")]
    [SerializeField] private bool autoRecenter = true;

    [Tooltip("Invert tilt around X (Up/Down).")]
    [SerializeField] private bool invertX = false;

    [Tooltip("Invert tilt around Z (Left/Right).")]
    [SerializeField] private bool invertZ = false;

    [Tooltip("If true, input is read every Update but pose is driven in FixedUpdate via MoveRotation.")]
    [SerializeField] private bool physicsDriven = true;

    Rigidbody _rb;
    Quaternion _baseRot;
    Vector2 _targetTiltXZ; // x = tilt around local X (pitch), z = tilt around local Z (roll)
    Vector2 _currentTiltXZ;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.isKinematic = true; // tray is scripted; items on it use physics
        _rb.interpolation = RigidbodyInterpolation.Interpolate;

        _baseRot = transform.rotation;
    }

    void Update()
    {
        // Read arrow input (no WASD)
        int v = (Input.GetKey(upKey) ? 1 : 0) - (Input.GetKey(downKey) ? 1 : 0);    // Up = +1, Down = -1
        int h = (Input.GetKey(rightKey) ? 1 : 0) - (Input.GetKey(leftKey) ? 1 : 0); // Right = +1, Left = -1

        float dt = Time.deltaTime;

        // Desired change to target tilt
        float xSign = invertX ? -1f : 1f;
        float zSign = invertZ ? -1f : 1f;

        // Up/Down tilt the tray around local X (pitch). UpArrow should tip "forward".
        _targetTiltXZ.x += xSign * v * tiltAccelDegPerSec * dt;

        // Left/Right tilt around local Z (roll). RightArrow should dip the right edge.
        _targetTiltXZ.y += zSign * -h * tiltAccelDegPerSec * dt; // minus so RightArrow rolls right edge down

        // Clamp target
        _targetTiltXZ.x = Mathf.Clamp(_targetTiltXZ.x, -maxTiltDeg, maxTiltDeg);
        _targetTiltXZ.y = Mathf.Clamp(_targetTiltXZ.y, -maxTiltDeg, maxTiltDeg);

        // Recenter target toward 0 when no input
        if (autoRecenter && v == 0)
            _targetTiltXZ.x = MoveToward(_targetTiltXZ.x, 0f, recenterDegPerSec * dt);
        if (autoRecenter && h == 0)
            _targetTiltXZ.y = MoveToward(_targetTiltXZ.y, 0f, recenterDegPerSec * dt);

        // Drive pose here if not physics-driven
        if (!physicsDriven)
            DriveRotation(dt);
    }

    void FixedUpdate()
    {
        if (!physicsDriven) return;
        DriveRotation(Time.fixedDeltaTime);
    }

    void DriveRotation(float dt)
    {
        // Smoothly follow target tilt
        _currentTiltXZ.x = MoveToward(_currentTiltXZ.x, _targetTiltXZ.x, followDegPerSec * dt);
        _currentTiltXZ.y = MoveToward(_currentTiltXZ.y, _targetTiltXZ.y, followDegPerSec * dt);

        // Compose rotation: base * Rx(pitch) * Rz(roll)
        Quaternion qx = Quaternion.AngleAxis(_currentTiltXZ.x, transform.right);
        Quaternion qz = Quaternion.AngleAxis(_currentTiltXZ.y, transform.forward);
        Quaternion target = _baseRot * qx * qz;

        if (_rb && _rb.isKinematic)
            _rb.MoveRotation(target);
        else
            transform.rotation = target;
    }

    static float MoveToward(float current, float target, float maxDelta)
        => Mathf.MoveTowards(current, target, maxDelta);

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        // Simple gizmo to show local axes and max tilt bounds
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.4f);
        Vector3 p = transform.position;
        Gizmos.DrawLine(p, p + transform.right * 0.5f);
        Gizmos.DrawLine(p, p + transform.forward * 0.5f);
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.2f);
        Gizmos.DrawWireSphere(p, 0.2f);
    }
#endif
}
