/*
// FILE: Assets/Scripts/PhysicsDrawing/KinematicStrokePainter.cs
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider), typeof(Rigidbody))]
[DisallowMultipleComponent]
public class KinematicStrokePainter : MonoBehaviour
{
    [Header("Stroke Look")]
    [SerializeField] private Material strokeMaterial;
    [SerializeField] private float minPointSpacing = 0.0015f;

    [Header("Dynamic Width")]
    [SerializeField] private float minWidth = 0.004f;   // 4 mm
    [SerializeField] private float maxWidth = 0.04f;    // 4 cm
    [SerializeField] private float widthScale = 1.0f;
    [SerializeField] private float pressureBoost = 0.0f; // optional: widen with velocity into surface

    [Header("Detection")]
    [SerializeField] private LayerMask surfaceMask;
    [SerializeField] private float liftFromSurface = 0.0025f;
    [SerializeField] private bool requireMouseButton = false;

    [Header("Wear")]
    [SerializeField] private float wearPerMeter = 0.0006f;
    [SerializeField] private float wearRadius   = 0.0075f;

    [Header("Debug")]
    [SerializeField] private bool debugDraw = false;

    private IToolWear _toolWear;
    private Rigidbody _rb;
    private Collider  _selfCollider;

    private StrokeMesh _current;
    private Vector3 _lastAddedWorld;
    private bool _hasLast;

    private Vector3 _lastWearPos;
    private bool _hadWearPos;

    private bool _touching;
    private Vector3 _contactPoint;
    private Vector3 _contactNormal;
    private Collider _touchCollider; // collider actually touching (may be a child)

    private readonly List<ContactPoint> _contacts = new List<ContactPoint>(16);

    void Awake()
    {
        _rb           = GetComponent<Rigidbody>();
        _selfCollider = GetComponent<Collider>();
        _toolWear     = GetComponent<IToolWear>();

        /*if (!strokeMaterial)
            Debug.LogWarning("KinematicStrokePainter: strokeMaterial is missing.");#1#
        if (strokeMaterial) strokeMaterial.renderQueue = 3100;
    }

    void Update()
    {
        bool userIntent = !requireMouseButton || Input.GetMouseButton(0);
        bool drawingNow = _touching && userIntent;

        if (debugDraw && _touching)
            Debug.DrawRay(_contactPoint, _contactNormal * 0.03f, Color.cyan, 0f, false);

        if (drawingNow)
        {
            if (_current == null)
            {
                var go = new GameObject("Stroke");
                go.transform.position = _contactPoint;
                _current = go.AddComponent<StrokeMesh>();
                _current.Init(strokeMaterial, Mathf.Max(minWidth, 0.0001f), minPointSpacing);
                _hasLast = false;
            }

            // Lift a hair off the surface to avoid z-fighting
            Vector3 p = _contactPoint + _contactNormal * liftFromSurface;

            // Build in-plane frame (tangent = along-stroke, side = across-stroke)
            Vector3 tangent = _hasLast
                ? Vector3.ProjectOnPlane(p - _lastAddedWorld, _contactNormal)
                : Vector3.ProjectOnPlane(_rb.velocity, _contactNormal);
            if (tangent.sqrMagnitude < 1e-8f)
                tangent = Vector3.ProjectOnPlane(transform.forward, _contactNormal);
            tangent.Normalize();

            Vector3 side = Vector3.Cross(_contactNormal, tangent).normalized;

            // Shape-agnostic footprint measurement at the plane
            float halfSide, halfTan;
            ContactPatchEstimator.EstimateHalfExtents1D(
                _touchCollider ? _touchCollider : _selfCollider,
                _contactPoint,
                _contactNormal,
                side, tangent,
                out halfSide, out halfTan);

            float rawWidth = 2f * halfSide; // across-stroke width for a ribbon
            float dynWidth = Mathf.Clamp(rawWidth * widthScale, minWidth, maxWidth);

            if (pressureBoost > 0f)
            {
                float press = Mathf.Max(0f, Vector3.Dot(_rb.velocity, -_contactNormal)); // m/s into surface
                dynWidth = Mathf.Clamp(dynWidth + press * pressureBoost, minWidth, maxWidth);
            }

            // Only add when moved enough
            if (!_hasLast || (p - _lastAddedWorld).sqrMagnitude >= (minPointSpacing * minPointSpacing))
            {
                _current.AddPoint(p, _contactNormal, dynWidth);
                _lastAddedWorld = p;
                _hasLast = true;

                // Wear exactly along the moved path
                if (_toolWear != null)
                {
                    if (!_hadWearPos) { _lastWearPos = p; _hadWearPos = true; }
                    float dist = Vector3.Distance(p, _lastWearPos);
                    float amount = dist * wearPerMeter;
                    _toolWear.WearAt(_contactPoint, _contactNormal, amount, wearRadius);
                    _lastWearPos = p;
                }
            }
            return;
        }

        // Not drawing this frame → reset stroke-side state
        _current = null;
        _hadWearPos = false;
        _hasLast = false;
    }

    void OnCollisionStay(Collision collision)
    {
        if ((surfaceMask.value & (1 << collision.gameObject.layer)) == 0)
            return;

        // Cache the collider actually touching (may be a child collider)
        _touchCollider = collision.collider;

        _contacts.Clear();
        if (collision.contactCount > 0) collision.GetContacts(_contacts);
        if (_contacts.Count == 0) return;

        // Choose the most upward-facing contact for position/normal
        bool any = false;
        float best = float.NegativeInfinity;
        Vector3 bestP = default, bestN = default;

        for (int i = 0; i < _contacts.Count; i++)
        {
            var cp = _contacts[i];
            float score = Vector3.Dot(cp.normal, Vector3.up);
            if (score > best)
            {
                best  = score;
                bestP = cp.point;
                bestN = cp.normal;
                any = true;
            }
        }

        if (any)
        {
            _touching = true;
            _contactPoint  = bestP;
            _contactNormal = bestN;
        }
    }

    void OnCollisionExit(Collision collision)
    {
        if ((surfaceMask.value & (1 << collision.gameObject.layer)) == 0) return;
        _touching = false;
        _touchCollider = null;
    }
}
*/
