// FILEPATH: Assets/Scripts/Camera/FreeMoveCameraXZ.cs
using UnityEngine;

public class FreeMoveCameraXZ : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] float moveSpeed = 6f;
    [SerializeField] float sprintMultiplier = 2f;

    [Header("Vertical Movement (Y axis)")]
    [SerializeField] KeyCode moveUpKey = KeyCode.E;
    [SerializeField] KeyCode moveDownKey = KeyCode.Q;

    [Header("Mouse Look (toggle)")]
    [SerializeField] bool mouseLookEnabled = false;        // start disabled so mouse is free
    [SerializeField] KeyCode toggleMouseLookKey = KeyCode.F;
    [SerializeField] float sensitivity = 2f;
    [SerializeField] float pitchLimit = 85f;

    float _yaw, _pitch;

    void Start()
    {
        var e = transform.eulerAngles;
        _yaw = e.y;
        _pitch = e.x;
        ApplyCursorState();
    }

    void Update()
    {
        // --- Toggle mouse look ---
        if (Input.GetKeyDown(toggleMouseLookKey))
        {
            mouseLookEnabled = !mouseLookEnabled;
            ApplyCursorState();
        }

        // --- Movement (WASD only) ---
        float h = 0f;
        float v = 0f;

        if (Input.GetKey(KeyCode.A)) h = -1f;
        else if (Input.GetKey(KeyCode.D)) h = 1f;

        if (Input.GetKey(KeyCode.W)) v = 1f;
        else if (Input.GetKey(KeyCode.S)) v = -1f;

        float speed = moveSpeed * (Input.GetKey(KeyCode.LeftShift) ? sprintMultiplier : 1f);

        // project camera's forward/right onto XZ so Y is always 0
        Vector3 fwdXZ = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        Vector3 rightXZ = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;

        Vector3 moveDir = (fwdXZ * v + rightXZ * h);

        // --- Vertical movement (E/Q) ---
        float upDown = 0f;
        if (Input.GetKey(moveUpKey))   upDown += 1f;
        if (Input.GetKey(moveDownKey)) upDown -= 1f;

        Vector3 vertical = Vector3.up * upDown;

        // combine XZ + Y
        Vector3 delta = moveDir + vertical;
        if (delta.sqrMagnitude > 1f) delta.Normalize();     // consistent diagonal speed

        transform.position += delta * speed * Time.deltaTime;

        // --- Mouse look (only when enabled) ---
        if (mouseLookEnabled)
        {
            _yaw   += Input.GetAxis("Mouse X") * sensitivity;
            _pitch -= Input.GetAxis("Mouse Y") * sensitivity;
            _pitch  = Mathf.Clamp(_pitch, -pitchLimit, pitchLimit);
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }
    }

    void ApplyCursorState()
    {
        Cursor.lockState = mouseLookEnabled ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible   = !mouseLookEnabled;
    }
}
