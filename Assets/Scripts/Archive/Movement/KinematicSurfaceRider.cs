// FILEPATH: Assets/Scripts/Movement/KinematicSurfaceRider.cs
using UnityEngine;

/// <summary>
/// Kinematic cube controller that:
/// - Slides on whatever surface is under it (tilting tray, ramps, etc.).
/// - Uses a raycast to stick to colliders (no real rigidbody forces).
/// - Applies "gravity along the surface" when grounded, so it slides down slopes.
/// - Goes airborne when it loses the surface (e.g. at the top of a ramp),
///   then falls with fake gravity and lands back when it hits a surface again.
///
/// Attach this to the cube. Give it a kinematic Rigidbody + Collider.
/// Put all surfaces (tray, ramps) on layers included in surfaceMask.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class KinematicSurfaceRider : MonoBehaviour
{
    private enum RiderState
    {
        Grounded,
        Airborne
    }

    [Header("Surfaces")]
    [Tooltip("Layers that count as surfaces (tray + ramps).")]
    [SerializeField] private LayerMask surfaceMask;

    [Tooltip("Distance above the hit point to place the cube when grounded.")]
    [SerializeField] private float surfaceOffset = 0.02f;

    [Tooltip("How high above the cube we start the raycast.")]
    [SerializeField] private float rayStartHeight = 1.0f;

    [Tooltip("How far below the cube we look for ground.")]
    [SerializeField] private float rayDownDistance = 3.0f;

    [Header("Grounded Slide Motion")]
    [Tooltip("Scale applied to gravity when computing slide acceleration along the surface.")]
    [SerializeField] private float slideGravityScale = 1.0f;

    [Tooltip("Maximum speed when sliding on surfaces.")]
    [SerializeField] private float maxSlideSpeed = 6.0f;

    [Tooltip("Friction while grounded. 0 = no friction.")]
    [SerializeField] private float groundedFriction = 4.0f;

    [Header("Airborne Motion")]
    [Tooltip("Gravity magnitude when airborne.")]
    [SerializeField] private float airborneGravity = 9.81f;

    [Tooltip("Extra vertical boost when we leave a steep ramp, multiplied by current speed.")]
    [SerializeField] private float rampJumpBoost = 0.6f;

    [Tooltip("Minimal speed needed to get an extra jump boost off a steep ramp.")]
    [SerializeField] private float minRampJumpSpeed = 2.5f;

    [Tooltip("Velocity damping while airborne.")]
    [SerializeField] private float airborneDrag = 0.1f;

    [Tooltip("How close to the ground we must be (moving down) to count as landed.")]
    [SerializeField] private float landingTolerance = 0.05f;

    [Header("Rotation")]
    [Tooltip("If true, aligns cube's up axis with the surface normal when grounded.")]
    [SerializeField] private bool alignToSurface = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool debugDrawRay = false;

    Rigidbody _rb;

    RiderState _state = RiderState.Grounded;
    Vector3 _velocityWS = Vector3.zero;
    Vector3 _groundNormal = Vector3.up;
    bool _hadGroundLastFrame;
    Vector3 _lastGroundNormal;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.isKinematic = true;
        _rb.useGravity = false;
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        switch (_state)
        {
            case RiderState.Grounded:
                UpdateGrounded(dt);
                break;
            case RiderState.Airborne:
                UpdateAirborne(dt);
                break;
        }
    }

    // ---------------- GROUND LOGIC ----------------
    void UpdateGrounded(float dt)
    {
        Vector3 pos = transform.position;

        // 1) Find ground under us
        if (!TryGroundRaycast(pos, out RaycastHit hit))
        {
            // No ground: go airborne with current velocity
            if (debugLogs)
                Debug.Log("[KinematicSurfaceRider] Lost ground, going AIRBORNE", this);

            _state = RiderState.Airborne;
            return;
        }

        _groundNormal = hit.normal;
        Vector3 groundPos = hit.point + _groundNormal * surfaceOffset;

        // 2) Slide acceleration along surface (project gravity onto plane)
        Vector3 gravity = Physics.gravity.sqrMagnitude > 0f
            ? Physics.gravity.normalized * airborneGravity
            : Vector3.down * airborneGravity;

        Vector3 slideAccel = Vector3.ProjectOnPlane(gravity, _groundNormal) * slideGravityScale;
        _velocityWS += slideAccel * dt;

        // Remove velocity component into the ground (keep motion tangent to surface)
        _velocityWS = Vector3.ProjectOnPlane(_velocityWS, _groundNormal);

        // 3) Apply friction
        if (groundedFriction > 0f)
        {
            float damping = Mathf.Clamp01(groundedFriction * dt);
            _velocityWS *= (1f - damping);
        }

        // Clamp max speed on surface
        float speed = _velocityWS.magnitude;
        if (maxSlideSpeed > 0f && speed > maxSlideSpeed)
        {
            _velocityWS = _velocityWS.normalized * maxSlideSpeed;
            speed = maxSlideSpeed;
        }

        // 4) Move along surface
        Vector3 newPos = groundPos + _velocityWS * dt;

        // 5) Check what happens at next position: still ground or leaving a ramp edge?
        bool willHaveGround = TryGroundRaycast(newPos, out RaycastHit nextHit);
        bool steepRamp = false;

        if (willHaveGround)
        {
            float angle = Vector3.Angle(nextHit.normal, Vector3.up);
            steepRamp = angle > 45f; // tweak if needed
        }

        // If we had ground before and now have none, and we were on a steep ramp -> boost jump
        if (_hadGroundLastFrame && !willHaveGround && steepRamp && speed >= minRampJumpSpeed)
        {
            Vector3 up = -Physics.gravity.normalized;
            _velocityWS += up * (speed * rampJumpBoost);

            if (debugLogs)
                Debug.Log("[KinematicSurfaceRider] Leaving steep ramp -> jump boost", this);

            _state = RiderState.Airborne;
            transform.position = newPos; // start from edge
            _hadGroundLastFrame = false;
            return;
        }

        // Normal grounded advance: place on new ground point
        if (willHaveGround)
        {
            _groundNormal = nextHit.normal;
            newPos = nextHit.point + _groundNormal * surfaceOffset;
        }

        _hadGroundLastFrame = willHaveGround;

        // 6) Apply to transform
        ApplyTransform(newPos, _groundNormal);

        if (debugLogs)
        {
            Debug.Log(
                $"[KinematicSurfaceRider] GROUND | speed={speed:F3}, vel={_velocityWS}, " +
                $"normal={_groundNormal}",
                this);
        }
    }

    // ---------------- AIR LOGIC ----------------
    void UpdateAirborne(float dt)
    {
        Vector3 pos = transform.position;

        // 1) Apply gravity
        Vector3 gravityDir = Physics.gravity.sqrMagnitude > 0f
            ? Physics.gravity.normalized
            : Vector3.down;

        _velocityWS += gravityDir * airborneGravity * dt;

        // 2) Air drag
        if (airborneDrag > 0f)
        {
            float damping = Mathf.Clamp01(airborneDrag * dt);
            _velocityWS *= (1f - damping);
        }

        // 3) Move
        Vector3 newPos = pos + _velocityWS * dt;

        // 4) Check for landing
        if (TryGroundRaycast(newPos, out RaycastHit hit))
        {
            // Only land if moving down relative to the surface normal
            float dot = Vector3.Dot(_velocityWS, hit.normal);
            float dist = hit.distance;

            if (dot < 0f && dist <= rayDownDistance + landingTolerance)
            {
                _groundNormal = hit.normal;
                newPos = hit.point + _groundNormal * surfaceOffset;

                // Remove component into the ground so we slide along it
                _velocityWS = Vector3.ProjectOnPlane(_velocityWS, _groundNormal);

                _state = RiderState.Grounded;
                _hadGroundLastFrame = true;

                if (debugLogs)
                    Debug.Log("[KinematicSurfaceRider] LAND", this);

                ApplyTransform(newPos, _groundNormal);
                return;
            }
        }

        // Still in air
        if (debugLogs)
        {
            Debug.Log($"[KinematicSurfaceRider] AIR | pos={newPos}, vel={_velocityWS}", this);
        }

        if (_rb.isKinematic)
            _rb.MovePosition(newPos);
        else
            transform.position = newPos;
    }

    // ---------------- HELPERS ----------------

    bool TryGroundRaycast(Vector3 fromPos, out RaycastHit hit)
    {
        Vector3 rayOrigin = fromPos + Vector3.up * rayStartHeight;
        Vector3 rayDir = Vector3.down;

        if (debugDrawRay)
        {
            Debug.DrawRay(rayOrigin, rayDir * (rayStartHeight + rayDownDistance), Color.green);
        }

        return Physics.Raycast(
            rayOrigin,
            rayDir,
            out hit,
            rayStartHeight + rayDownDistance,
            surfaceMask,
            QueryTriggerInteraction.Ignore
        );
    }

    void ApplyTransform(Vector3 pos, Vector3 normal)
    {
        if (_rb.isKinematic)
        {
            _rb.MovePosition(pos);

            if (alignToSurface)
            {
                Quaternion rot = Quaternion.FromToRotation(Vector3.up, normal);
                _rb.MoveRotation(rot);
            }
        }
        else
        {
            transform.position = pos;
            if (alignToSurface)
            {
                Quaternion rot = Quaternion.FromToRotation(Vector3.up, normal);
                transform.rotation = rot;
            }
        }
    }
}
