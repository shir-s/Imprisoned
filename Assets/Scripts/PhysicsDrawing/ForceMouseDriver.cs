/*
// FILEPATH: Assets/Scripts/PhysicsDrawing/ForceMouseDriver.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Click/hold-to-drive physics controller:
/// - Click (or hold) to drive the tool toward the mouse on the paper plane.
/// - Applies PD force + a small pressure into the paper so contacts persist.
/// - Robust click hit-test supports any collider in this object's hierarchy.
/// - If no paperPlane set, auto-derives a plane from last collision or world-up.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class ForceMouseDriver : MonoBehaviour
{
    [Header("Scene References")]
    [SerializeField] private Camera cam;                 // defaults to Camera.main
    [Tooltip("If not set, the script derives a plane from last contact or uses world-up at current height.")]
    [SerializeField] private Transform paperPlane;

    [Header("Targeting")]
    [SerializeField] private float planeOffset = 0.0f;   // offset along plane normal (meters)
    [SerializeField] private bool clampWithinBounds = false;
    [SerializeField] private Vector2 boundsHalfSize = new Vector2(2, 2); // local XZ bounds on paper

    [Header("Drive (PD)")]
    [SerializeField] private float stiffness = 120f;     // proportional gain (m/s^2 per m)
    [SerializeField] private float damping   = 18f;      // derivative gain (m/s^2 per m/s)
    [SerializeField] private float maxAccel  = 40f;      // clamp on commanded acceleration (m/s^2)
    [SerializeField] private float maxSpeed  = 2.5f;     // velocity clamp (m/s)

    [Header("Contact")]
    [SerializeField] private float contactPressure = 5f; // push into plane along -normal (m/s^2)
    [SerializeField] private float lateralFriction = 0f;// extra planar damping (m/s^2)

    [Header("Input")]
    [Tooltip("When true: drive only while LMB is held over the tool. When false: click-to-toggle.")]
    [SerializeField] private bool holdToDrive = false;
    [SerializeField] private KeyCode toggleKey = KeyCode.Mouse0; // LMB
    [SerializeField] private KeyCode releaseKey = KeyCode.Space;
    [Tooltip("Physics raycast mask for clicking the tool.")]
    [SerializeField] private LayerMask clickableMask = ~0;

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;
    [SerializeField] private bool debugDraw = false;

    Rigidbody _rb;
    Collider[] _myColliders;
    bool _driving;
    Vector3 _targetWorld;

    // last known plane if paperPlane not set
    bool _haveDerivedPlane;
    Vector3 _derivedOrigin;
    Vector3 _derivedNormal = Vector3.up;

    // buffer to accept plane info from painter (optional helper)
    static readonly List<ContactPoint> _tmpCp = new List<ContactPoint>(8);

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _myColliders = GetComponentsInChildren<Collider>(true);
        if (!cam) cam = Camera.main;

        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    void Update()
    {
        // INPUT: hold or toggle
        if (holdToDrive)
        {
            if (Input.GetKey(toggleKey))
            {
                // Start when first pressed on the tool
                if (!_driving)
                {
                    if (RayHitsMyHierarchy(Input.mousePosition))
                    {
                        _driving = true;
                        if (debugLog) Debug.Log("[ForceMouseDriver] DRIVING (hold) begin");
                    }
                }
            }
            else if (_driving)
            {
                _driving = false;
                if (debugLog) Debug.Log("[ForceMouseDriver] DRIVING (hold) end");
            }
        }
        else
        {
            if (Input.GetKeyDown(toggleKey))
            {
                if (RayHitsMyHierarchy(Input.mousePosition))
                {
                    _driving = !_driving;
                    if (debugLog) Debug.Log($"[ForceMouseDriver] DRIVING (toggle) -> {(_driving ? "ON" : "OFF")}");
                }
            }
            if (Input.GetKeyDown(releaseKey) && _driving)
            {
                _driving = false;
                if (debugLog) Debug.Log("[ForceMouseDriver] DRIVING (toggle) OFF by releaseKey");
            }
        }

        // Compute target point on a plane
        if (_driving && cam)
        {
            GetActivePlane(out Vector3 origin, out Vector3 normal);

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            Plane plane = new Plane(normal, origin + normal * planeOffset);

            if (plane.Raycast(ray, out float enter))
            {
                Vector3 hit = ray.GetPoint(enter);

                // If we have an explicit paper plane and want bounds, clamp in its local space
                if (clampWithinBounds && paperPlane)
                {
                    Vector3 local = paperPlane.InverseTransformPoint(hit);
                    local.x = Mathf.Clamp(local.x, -boundsHalfSize.x, boundsHalfSize.x);
                    local.z = Mathf.Clamp(local.z, -boundsHalfSize.y, boundsHalfSize.y);
                    hit = paperPlane.TransformPoint(local);
                }

                _targetWorld = hit;

                if (debugDraw)
                {
                    Debug.DrawLine(transform.position, _targetWorld, Color.green, 0f, false);
                    Debug.DrawRay(_targetWorld, normal * 0.05f, Color.cyan, 0f, false);
                }
            }
        }
    }

    void FixedUpdate()
    {
        if (!_driving) return;

        GetActivePlane(out Vector3 origin, out Vector3 normal);

        Vector3 pos = _rb.position;
        Vector3 vel = _rb.velocity;

        Vector3 to = _targetWorld - pos;

        Vector3 accCmd = stiffness * to - damping * vel;

        // clamp acceleration
        float a2 = accCmd.sqrMagnitude;
        float maxA2 = maxAccel * maxAccel;
        if (a2 > maxA2) accCmd = accCmd * (maxAccel / Mathf.Sqrt(a2));

        // press into plane (keeps contact alive)
        accCmd += -normal * contactPressure;

        // optional planar friction
        if (lateralFriction > 0f)
        {
            Vector3 vN = Vector3.Project(vel, normal);
            Vector3 vT = vel - vN;
            if (vT.sqrMagnitude > 1e-6f)
                accCmd += -vT.normalized * Mathf.Min(vT.magnitude, lateralFriction);
        }

        _rb.AddForce(accCmd, ForceMode.Acceleration);

        if (maxSpeed > 0f && _rb.velocity.magnitude > maxSpeed)
            _rb.velocity = _rb.velocity.normalized * maxSpeed;
    }

    // -------- Helpers --------

    bool RayHitsMyHierarchy(Vector3 mousePos)
    {
        Ray ray = cam.ScreenPointToRay(mousePos);
        // We’ll raycast all and match against any of our colliders (parent or children).
        if (Physics.Raycast(ray, out RaycastHit hit, 1000f, clickableMask, QueryTriggerInteraction.Ignore))
        {
            var hitCol = hit.collider;
            for (int i = 0; i < _myColliders.Length; i++)
                if (_myColliders[i] == hitCol) return true;
        }
        return false;
    }

    void GetActivePlane(out Vector3 origin, out Vector3 normal)
    {
        if (paperPlane)
        {
            origin = paperPlane.position;
            normal = paperPlane.up.normalized;
            return;
        }

        // If we had a recent contact (via DerivePlaneFromCollision), use it
        if (_haveDerivedPlane)
        {
            origin = _derivedOrigin;
            normal = _derivedNormal;
            return;
        }

        // Fallback: world-up plane through current tool height
        origin = new Vector3(0f, transform.position.y, 0f);
        normal = Vector3.up;
    }

    // Call this from another script (e.g., your painter in OnCollisionStay) to feed plane info.
    public void DerivePlaneFromCollision(Collision c)
    {
        if (c == null || c.contactCount == 0) return;

        _tmpCp.Clear();
        c.GetContacts(_tmpCp);

        // Average contact normal and a representative point
        Vector3 sumN = Vector3.zero;
        Vector3 sumP = Vector3.zero;
        for (int i = 0; i < _tmpCp.Count; i++)
        {
            sumN += _tmpCp[i].normal;
            sumP += _tmpCp[i].point;
        }
        Vector3 n = sumN.normalized;
        Vector3 p = sumP / Mathf.Max(1, _tmpCp.Count);

        if (n.sqrMagnitude > 1e-4f)
        {
            _derivedNormal = n;
            _derivedOrigin = p;
            _haveDerivedPlane = true;
            if (debugLog) Debug.Log("[ForceMouseDriver] Plane derived from collision.");
        }
    }
}
*/
