// FILEPATH: Assets/Scripts/Interaction/KeyboardSelectableMover.cs
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class KeyboardSelectableMover : MonoBehaviour
{
    [Header("Selection")]
    [SerializeField] private KeyCode deselectKey = KeyCode.Escape;

    [Header("Movement Mode")]
    [Tooltip("Continuous = hold keys to move smoothly. GridStep = move in fixed steps per key press.")]
    [SerializeField] private MoveMode _mode = MoveMode.Continuous;

    public enum MoveMode { Continuous, GridStep }

    [Header("Continuous Settings")]
    [SerializeField] private float _moveSpeed = 1.5f;     // meters/sec on XZ
    [SerializeField] private float _sprintMul = 2.0f;     // hold LeftShift

    [Header("Grid Step Settings")]
    [SerializeField] private float _stepMeters = 0.1f;    // meters per key press
    [SerializeField] private float _repeatDelay = 0.28f;  // first repeat delay when key is held
    [SerializeField] private float _repeatRate  = 0.06f;  // time between repeats while held

    [Header("Y Lock")]
    [Tooltip("If true, Y is frozen at the value captured on selection.")]
    [SerializeField] private bool _lockYOnSelect = true;

    [Header("Optional: Visual Feedback")]
    [SerializeField] private bool _tintWhenSelected = true;
    [SerializeField] private Color _selectedTint = new Color(1f, 0.9f, 0.25f, 1f);

    // --- runtime ---
    private static KeyboardSelectableMover _current;   // single active selection

    private float _lockedY;
    private Renderer _r;
    private MaterialPropertyBlock _mpb;
    private Color _origColor;
    private bool _hasOrigColor;

    // grid repeat
    private float _hHoldTime, _vHoldTime;
    private int   _lastH, _lastV;
    private bool  _hFirstRepeatDone, _vFirstRepeatDone;

    void Awake()
    {
        _r = GetComponentInChildren<Renderer>();
        if (_r != null) { _mpb = new MaterialPropertyBlock(); }
    }

    void OnMouseDown()
    {
        SelectThis();
    }

    void OnMouseOver()
    {
        // Right-click to deselect quickly (useful when mouse is over the same object)
        if (Input.GetMouseButtonDown(1) && _current == this)
            Deselect();
    }

    void Update()
    {
        if (_current != this) return;

        if (Input.GetKeyDown(deselectKey) || Input.GetMouseButtonDown(1))
        {
            Deselect();
            return;
        }

        if (_mode == MoveMode.Continuous)
            TickContinuous();
        else
            TickGridStep();
    }

    // ===== Select / Deselect =====

    private void SelectThis()
    {
        if (_current == this) return;

        // Clear previous selection
        if (_current != null)
            _current.Deselect();

        _current = this;

        if (_lockYOnSelect)
            _lockedY = transform.position.y;

        // Optional highlight
        if (_tintWhenSelected && _r != null)
        {
            _r.GetPropertyBlock(_mpb);
            if (_r.sharedMaterial != null && _r.sharedMaterial.HasProperty("_Color"))
            {
                _origColor = _r.sharedMaterial.color;
                _hasOrigColor = true;
            }
            // Use per-renderer override so we don't duplicate materials
            _mpb.SetColor("_Color", _selectedTint);
            _r.SetPropertyBlock(_mpb);
        }

        // Reset grid timers
        _hHoldTime = _vHoldTime = 0f;
        _lastH = _lastV = 0;
        _hFirstRepeatDone = _vFirstRepeatDone = false;
    }

    private void Deselect()
    {
        if (_current != this) return;

        // Remove tint
        if (_tintWhenSelected && _r != null)
        {
            _r.GetPropertyBlock(_mpb);
            if (_hasOrigColor)
                _mpb.SetColor("_Color", _origColor);
            else
                _mpb.Clear(); // no color property – clear block
            _r.SetPropertyBlock(_mpb);
        }

        _current = null;
    }

    // ===== Movement (Continuous) =====
    private void TickContinuous()
    {
        // Unity "Horizontal"/"Vertical" respond to Arrow keys AND WASD by default.
        float h = Input.GetAxisRaw("Horizontal"); // -1..1   (Left/Right)
        float v = Input.GetAxisRaw("Vertical");   // -1..1   (Down/Up)

        Vector3 dxz = new Vector3(h, 0f, v);
        if (dxz.sqrMagnitude > 1f) dxz.Normalize();

        float speed = _moveSpeed * (Input.GetKey(KeyCode.LeftShift) ? _sprintMul : 1f);
        Vector3 p = transform.position + dxz * (speed * Time.deltaTime);

        if (_lockYOnSelect)
            p.y = _lockedY;

        transform.position = p;
    }

    // ===== Movement (Grid Step) =====
    private void TickGridStep()
    {
        // get raw intentions: -1, 0, 1
        int h = (Input.GetKey(KeyCode.LeftArrow) ? -1 : 0) + (Input.GetKey(KeyCode.RightArrow) ? 1 : 0);
        int v = (Input.GetKey(KeyCode.DownArrow) ? -1 : 0) + (Input.GetKey(KeyCode.UpArrow) ? 1 : 0);

        // prioritize the last non-zero axis to avoid diagonal double-steps in one frame
        Vector3 step = Vector3.zero;

        // Horizontal handling (with repeat)
        if (h != 0)
        {
            if (_lastH != h) // direction changed or started holding
            {
                step.x += h * _stepMeters;
                _hHoldTime = 0f;
                _hFirstRepeatDone = false;
            }
            else
            {
                _hHoldTime += Time.unscaledDeltaTime;
                float wait = _hFirstRepeatDone ? _repeatRate : _repeatDelay;
                if (_hHoldTime >= wait)
                {
                    step.x += h * _stepMeters;
                    _hHoldTime = 0f;
                    _hFirstRepeatDone = true;
                }
            }
        }
        else
        {
            _hHoldTime = 0f;
            _hFirstRepeatDone = false;
        }

        // Vertical handling (with repeat)
        if (v != 0)
        {
            if (_lastV != v)
            {
                step.z += v * _stepMeters;
                _vHoldTime = 0f;
                _vFirstRepeatDone = false;
            }
            else
            {
                _vHoldTime += Time.unscaledDeltaTime;
                float wait = _vFirstRepeatDone ? _repeatRate : _repeatDelay;
                if (_vHoldTime >= wait)
                {
                    step.z += v * _stepMeters;
                    _vHoldTime = 0f;
                    _vFirstRepeatDone = true;
                }
            }
        }
        else
        {
            _vHoldTime = 0f;
            _vFirstRepeatDone = false;
        }

        _lastH = h;
        _lastV = v;

        if (step.sqrMagnitude > 0f)
        {
            Vector3 p = transform.position + step;
            if (_lockYOnSelect) p.y = _lockedY;
            transform.position = p;
        }
    }

    // Optional: draw a tiny gizmo when selected
#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (_current != this) return;
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
        Gizmos.DrawWireCube(transform.position, Vector3.one * 0.12f);
    }
#endif
}
