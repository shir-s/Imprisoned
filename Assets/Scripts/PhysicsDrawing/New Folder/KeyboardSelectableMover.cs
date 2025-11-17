// FILEPATH: Assets/Scripts/Interaction/KeyboardPhysicsMover.cs
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider), typeof(Rigidbody))]
public class KeyboardSelectableMover : MonoBehaviour
{
    [Header("Selection")]
    [SerializeField] private KeyCode deselectKey = KeyCode.Escape;

    [Header("Movement")]
    [Tooltip("Target speed on XZ when holding an arrow key.")]
    [SerializeField] private float moveSpeed = 2.5f;
    [SerializeField] private float sprintMultiplier = 2.0f;   // LeftShift
    [Tooltip("How fast velocity matches target (higher = snappier).")]
    [SerializeField] private float accel = 12f;
    [Tooltip("Max horizontal speed cap (safety).")]
    [SerializeField] private float maxHorizontalSpeed = 6f;

    [Header("Damping")]
    [Tooltip("Extra horizontal damping when no input (in addition to Rigidbody drag).")]
    [SerializeField] private float idleDamp = 8f;

    [Header("Anti-Roll / Upright")]
    [Tooltip("Freeze all rotations on the Rigidbody to prevent any rolling or tipping.")]
    [SerializeField] private bool freezeAllRotation = true;
    [Tooltip("Forcefully zero angular velocity every physics step (extra safety).")]
    [SerializeField] private bool zeroAngularVelocity = true;

    [Header("Optional: Visual Feedback")]
    [SerializeField] private bool tintWhenSelected = true;
    [SerializeField] private Color selectedTint = new Color(1f, 0.9f, 0.25f, 1f);

    // ---- runtime ----
    private static KeyboardSelectableMover _current; // single active selection
    private Rigidbody _rb;
    private Renderer _r;
    private MaterialPropertyBlock _mpb;
    private Color _origColor;
    private bool _hasOrigColor;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.isKinematic = false; // must be dynamic so velocity actually moves it
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        ApplyRotationConstraints();

        _r = GetComponentInChildren<Renderer>();
        if (_r) _mpb = new MaterialPropertyBlock();
    }

    void OnEnable()
    {
        if (_rb != null)
        {
            _rb.isKinematic = false;
            ApplyRotationConstraints();
        }
    }

    void OnValidate()
    {
        if (_rb != null)
        {
            ApplyRotationConstraints();
        }
    }

    private void ApplyRotationConstraints()
    {
        if (_rb == null) return;

        var c = _rb.constraints;

        // Clear any rotation freeze flags
        c &= ~(RigidbodyConstraints.FreezeRotationX |
               RigidbodyConstraints.FreezeRotationY |
               RigidbodyConstraints.FreezeRotationZ);

        if (freezeAllRotation)
        {
            // Add them back if we want to lock rotation
            c |= RigidbodyConstraints.FreezeRotationX |
                 RigidbodyConstraints.FreezeRotationY |
                 RigidbodyConstraints.FreezeRotationZ;
        }

        _rb.constraints = c;
    }

    void OnMouseDown()
    {
        SelectThis();
    }

    void OnMouseOver()
    {
        if (Input.GetMouseButtonDown(1) && _current == this) Deselect();
    }

    void Update()
    {
        if (_current != this) return;

        if (Input.GetKeyDown(deselectKey) || Input.GetMouseButtonDown(1))
        {
            Deselect();
            return;
        }
    }

    void FixedUpdate()
    {
        if (_current != this || _rb == null) return;

        // Arrow keys only (camera uses WASD)
        int h = (Input.GetKey(KeyCode.LeftArrow) ? -1 : 0) + (Input.GetKey(KeyCode.RightArrow) ? 1 : 0);
        int v = (Input.GetKey(KeyCode.DownArrow) ? -1 : 0) + (Input.GetKey(KeyCode.UpArrow) ? 1 : 0);

        Vector2 input = new Vector2(h, v);
        if (input.sqrMagnitude > 1f) input.Normalize();

        float speed = moveSpeed * (Input.GetKey(KeyCode.LeftShift) ? sprintMultiplier : 1f);

        // Current horizontal velocity (XZ only)
        Vector3 vel = _rb.linearVelocity;
        Vector3 velXZ = new Vector3(vel.x, 0f, vel.z);

        // Desired horizontal velocity
        Vector3 targetXZ = new Vector3(input.x, 0f, input.y) * speed;

        // Blend toward target (accel) or toward zero (idleDamp)
        float k = (input.sqrMagnitude > 0f) ? accel : idleDamp;
        Vector3 newXZ = Vector3.MoveTowards(velXZ, targetXZ, k * Time.fixedDeltaTime);

        // Cap
        if (newXZ.magnitude > maxHorizontalSpeed)
            newXZ = newXZ.normalized * maxHorizontalSpeed;

        // Apply back with original Y (gravity untouched)
        _rb.linearVelocity = new Vector3(newXZ.x, vel.y, newXZ.z);

        // Anti-roll
        if (zeroAngularVelocity)
            _rb.angularVelocity = Vector3.zero;
    }

    // --- select/deselect ---
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
                _hasOrigColor = true;
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
            if (_hasOrigColor) _mpb.SetColor("_Color", _origColor);
            else _mpb.Clear();
            _r.SetPropertyBlock(_mpb);
        }
        _current = null;
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (_current != this) return;
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
        Gizmos.DrawWireCube(transform.position, Vector3.one * 0.12f);
    }
#endif
}
