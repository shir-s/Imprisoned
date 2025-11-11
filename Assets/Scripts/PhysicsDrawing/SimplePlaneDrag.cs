// FILE: Assets/Scripts/PhysicsDrawing/SimplePlaneDrag.cs
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class SimplePlaneDrag : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Camera cam;
    [SerializeField] private LayerMask surfaceMask;     // layer(s) for your paper/board

    [Header("Motion")]
    [SerializeField] private float hover = 0.002f;      // keep slightly above surface
    [SerializeField] private float smoothTime = 0.025f; // lower = tighter follow

    private Rigidbody _rb;
    private bool _grabbing;
    private float _planeY;
    private float _lockedY;           // <- fixed Y while grabbing
    private Vector2 _velXZ;           // SmoothDamp velocity for X/Z separately
    private Vector3 _lastPos;

    public Vector3 PlanarVelocity { get; private set; } // optional: for drawing logic

    void Awake()
    {
        if (!cam) cam = Camera.main;
        _rb = GetComponent<Rigidbody>();
        if (_rb) _rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    void Update()
    {
        if (!_grabbing) return;

        // Project mouse to plane at _planeY
        var ray = cam.ScreenPointToRay(Input.mousePosition);
        float dy = Mathf.Abs(ray.direction.y) < 1e-6f ? 1e-6f : ray.direction.y;
        float t  = (_planeY - ray.origin.y) / dy;
        Vector3 p = ray.origin + ray.direction * t;

        // Targets only on XZ, Y remains locked
        Vector2 targetXZ = new Vector2(p.x, p.z);
        Vector2 currentXZ = new Vector2(transform.position.x, transform.position.z);

        // Smooth each axis independently (no coupling with Y)
        currentXZ.x = Mathf.SmoothDamp(currentXZ.x, targetXZ.x, ref _velXZ.x, smoothTime);
        currentXZ.y = Mathf.SmoothDamp(currentXZ.y, targetXZ.y, ref _velXZ.y, smoothTime);

        Vector3 newPos = new Vector3(currentXZ.x, _lockedY, currentXZ.y);

        if (_rb && _rb.isKinematic)
            _rb.MovePosition(newPos);
        else
            transform.position = newPos;

        // Planar velocity for downstream use
        Vector3 pv = (newPos - _lastPos) / Mathf.Max(Time.deltaTime, 1e-6f);
        pv.y = 0f;
        PlanarVelocity = pv;
        _lastPos = newPos;

        // Release on mouse up
        if (Input.GetMouseButtonUp(0)) EndGrab();
    }

    void OnMouseDown()
    {
        if (!RaycastSurface(out float y)) return;
        _planeY = y;
        StartGrab();
    }

    bool RaycastSurface(out float y)
    {
        y = 0f;
        var ray = cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out var hit, 1000f, surfaceMask))
        {
            y = hit.point.y;
            return true;
        }
        return false;
    }

    void StartGrab()
    {
        _grabbing = true;
        _lockedY = _planeY + hover;   // <- lock Y for the whole drag
        _lastPos = transform.position;
        _velXZ = Vector2.zero;

        if (_rb)
        {
            _rb.velocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.isKinematic = true;   // no physics during drag
            _rb.useGravity = false;
            // Optional: ensure no Y creep at all
            _rb.constraints |= RigidbodyConstraints.FreezePositionY;
        }
    }

    void EndGrab()
    {
        _grabbing = false;
        if (_rb)
        {
            // inherit planar velocity for natural release
            var v = _rb.velocity;
            v.x = PlanarVelocity.x;
            v.z = PlanarVelocity.z;
            v.y = Mathf.Max(0f, v.y);

            _rb.isKinematic = false;
            _rb.useGravity = true;
            _rb.constraints &= ~RigidbodyConstraints.FreezePositionY; // restore
            _rb.velocity = v;
        }
    }
}
