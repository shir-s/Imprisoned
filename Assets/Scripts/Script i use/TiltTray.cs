// FILEPATH: Assets/Scripts/Interaction/TiltTray.cs
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class TiltTray : MonoBehaviour
{
    [Header("Selection")]
    [SerializeField] private bool tintWhenSelected = true;
    [SerializeField] private Color selectedTint = new Color(1f, 0.9f, 0.25f, 1f);

    static TiltTray _current;   // global selection (only one tray at a time)

    Renderer _r;
    MaterialPropertyBlock _mpb;
    Color _origColor;
    bool _hasOrig;

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

    Rigidbody _rb;
    Quaternion _baseRot;
    Vector2 _targetTiltXZ;
    Vector2 _currentTiltXZ;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.isKinematic = true;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;

        _baseRot = transform.rotation;

        _r = GetComponentInChildren<Renderer>();
        if (_r) _mpb = new MaterialPropertyBlock();
    }

    // --------------------------------
    // SELECTION
    // --------------------------------
    void OnMouseDown()
    {
        SelectThis();
    }

    void Update()
    {
        if (_current != this) return;

        // right mouse button deselect
        if (Input.GetMouseButtonDown(1))
        {
            Deselect();
            return;
        }

        // Arrow-key logic only if selected
        HandleInput(Time.deltaTime);

        if (!physicsDriven)
            DriveRotation(Time.deltaTime);
    }

    void FixedUpdate()
    {
        if (_current != this || !physicsDriven) return;
        DriveRotation(Time.fixedDeltaTime);
    }

    void HandleInput(float dt)
    {
        int v = (Input.GetKey(upKey) ? 1 : 0) - (Input.GetKey(downKey) ? 1 : 0);
        int h = (Input.GetKey(rightKey) ? 1 : 0) - (Input.GetKey(leftKey) ? 1 : 0);

        float xSign = invertX ? -1f : 1f;
        float zSign = invertZ ? -1f : 1f;

        _targetTiltXZ.x += xSign * v * tiltAccelDegPerSec * dt;
        _targetTiltXZ.y += zSign * -h * tiltAccelDegPerSec * dt;

        _targetTiltXZ.x = Mathf.Clamp(_targetTiltXZ.x, -maxTiltDeg, maxTiltDeg);
        _targetTiltXZ.y = Mathf.Clamp(_targetTiltXZ.y, -maxTiltDeg, maxTiltDeg);

        if (autoRecenter && v == 0)
            _targetTiltXZ.x = MoveToward(_targetTiltXZ.x, 0f, recenterDegPerSec * dt);
        if (autoRecenter && h == 0)
            _targetTiltXZ.y = MoveToward(_targetTiltXZ.y, 0f, recenterDegPerSec * dt);
    }

    void DriveRotation(float dt)
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

    static float MoveToward(float current, float target, float maxDelta)
        => Mathf.MoveTowards(current, target, maxDelta);

    // --------------------------------
    // SELECT / DESELECT
    // --------------------------------
    void SelectThis()
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

    void Deselect()
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
    void OnDrawGizmosSelected()
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
