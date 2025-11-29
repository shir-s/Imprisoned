// FILEPATH: Assets/Scripts/Movement/KinematicTrayRider.cs
using UnityEngine;

/// <summary>
/// Kinematic "cube rider" that slides on a tilted tray:
/// - Tray tilts (TiltTray or any script).
/// - Cube moves as if affected by gravity projected onto the tray plane.
/// - Cube stays at a fixed distance above the tray (no bouncing vertically).
/// - Cube's rotation can be aligned to the tray tilt.
/// - Optionally bounces inside a rectangular region in tray local XZ.
/// 
/// Attach this to the cube prefab that gets spawned.
/// You can:
///   - Leave "tray" empty and it will auto-find a TiltTray in the scene, OR
///   - Drag a specific tray Transform in the inspector.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class KinematicTrayRider : MonoBehaviour
{
    [Header("Tray")]
    [Tooltip("If left empty, the script will try to auto-find a TiltTray in parents or in the scene.")]
    [SerializeField] private Transform tray;
    
    [Tooltip("Try to automatically find a TiltTray if 'tray' is not assigned.")]
    [SerializeField] private bool autoFindTray = true;

    [Header("Height")]
    [Tooltip("Height above the tray plane, in tray local Y.")]
    [SerializeField] private float localHeightAboveSurface = 0.05f;

    [Header("Motion")]
    [Tooltip("How strongly gravity along the tray accelerates the cube.")]
    [SerializeField] private float slopeAcceleration = 50f;

    [Tooltip("Maximum speed along the tray plane (world units/sec).")]
    [SerializeField] private float maxSpeed = 5f;

    [Tooltip("Friction / drag that slows the cube when moving. 0 = no friction.")]
    [SerializeField] private float friction = 1.0f;

    [Header("Rotation")]
    [Tooltip("If true, the cube's rotation will match the tray's rotation each physics step.")]
    [SerializeField] private bool alignRotationToTray = true;

    [Header("Start Placement")]
    [Tooltip("If true, snap the cube onto the tray plane on Awake (keeping approx. XZ).")]
    [SerializeField] private bool snapOntoTrayOnAwake = true;

    [Tooltip("Extra offset along tray normal when snapping.")]
    [SerializeField] private float snapOffsetAlongNormal = 0.01f;

    [Header("Bounds / Walls (tray local XZ)")]
    [Tooltip("If true, the cube will bounce inside a rectangle in tray local XZ.")]
    [SerializeField] private bool useBounds = true;

    [Tooltip("Center of the allowed area, in tray local XZ.")]
    [SerializeField] private Vector2 boundsCenterLocal = Vector2.zero;

    [Tooltip("Half-size of the allowed area. If your map is 100x100 and centered, use (50, 50).")]
    [SerializeField] private Vector2 boundsHalfSize = new Vector2(50f, 50f);

    [Tooltip("How strong the bounce is when hitting the bounds. 1 = same speed, <1 = lose energy, >1 = extra bouncy.")]
    [SerializeField] private float bounceStrength = 0.8f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool debugDrawBounds = false;

    // Runtime state
    Rigidbody _rb;
    Vector2 _velLocalXZ;   // velocity in tray local XZ
    Vector3 _localPos;     // position in tray local space
    bool _initialized;

    // External speed multiplier (used by RiverZone etc.)
    // 1 = normal speed, 0.5 = half speed, 0 = stopped.
    float _externalSpeedMultiplier = 1f;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.isKinematic = true;
        _rb.useGravity = false;
    }

    void Start()
    {
        TryInitialize();
    }

    void TryInitialize()
    {
        if (!tray && autoFindTray)
        {
            // 1) Try a parent TiltTray
            TiltTray parentTray = GetComponentInParent<TiltTray>();
            if (parentTray != null)
            {
                tray = parentTray.transform;
                if (debugLogs) Debug.Log("[KinematicTrayRider] Using parent TiltTray: " + tray.name, this);
            }

            // 2) If still null, try any TiltTray in the scene
            if (!tray)
            {
                TiltTray anyTray = FindObjectOfType<TiltTray>();
                if (anyTray != null)
                {
                    tray = anyTray.transform;
                    if (debugLogs) Debug.Log("[KinematicTrayRider] Auto-found TiltTray in scene: " + tray.name, this);
                }
            }
        }

        if (!tray)
        {
            if (debugLogs) Debug.LogWarning("[KinematicTrayRider] No tray found yet. Will keep trying.", this);
            _initialized = false;
            return;
        }

        // --- INITIAL LOCAL POSITION ---
        if (snapOntoTrayOnAwake)
        {
            // Project current position onto tray plane and move slightly above it
            Plane plane = new Plane(tray.up, tray.position);
            Vector3 worldPos = transform.position;

            float dist;
            if (plane.Raycast(new Ray(worldPos + tray.up * 5f, -tray.up), out dist))
            {
                worldPos = worldPos + tray.up * 5f + (-tray.up * dist);
            }

            worldPos += tray.up * snapOffsetAlongNormal;
            _localPos = tray.InverseTransformPoint(worldPos);
        }
        else
        {
            _localPos = tray.InverseTransformPoint(transform.position);
        }

        _localPos.y = localHeightAboveSurface; // constant height above tray
        _velLocalXZ = Vector2.zero;

        _initialized = true;
    }

    void FixedUpdate()
    {
        // If not initialized yet, keep trying (helps if cube spawns before tray).
        if (!_initialized)
        {
            TryInitialize();
            if (!_initialized)
                return;
        }

        float dt = Time.fixedDeltaTime;
        float speedMul = Mathf.Max(0f, _externalSpeedMultiplier); // safety

        // 1) Compute "downhill" direction on the tray plane
        Vector3 normalWS = tray.up;        // tray plane normal
        Vector3 gravityWS = Vector3.down;  // assume world gravity is -Y

        Vector3 slopeWS = Vector3.ProjectOnPlane(gravityWS, normalWS);
        Vector3 slopeLocal = tray.InverseTransformDirection(slopeWS);

        Vector2 slopeLocalXZ = new Vector2(slopeLocal.x, slopeLocal.z);
        float slopeMag = slopeLocalXZ.magnitude;

        Vector2 accelLocalXZ = Vector2.zero;

        if (slopeMag > 1e-4f)
        {
            Vector2 slopeDir = slopeLocalXZ / slopeMag;
            // acceleration magnitude proportional to tilt, scaled by river multiplier
            accelLocalXZ = slopeDir * slopeAcceleration * slopeMag * speedMul;
        }

        // 2) Integrate velocity
        _velLocalXZ += accelLocalXZ * dt;

        // 2.5) Friction as multiplicative damping (can't zero in one frame)
        if (friction > 0f)
        {
            float damping = Mathf.Clamp01(friction * dt); // 0..1
            _velLocalXZ *= (1f - damping);
        }

        // Clamp max speed (also scaled by river multiplier)
        float speed = _velLocalXZ.magnitude;
        float effectiveMaxSpeed = maxSpeed * speedMul;

        if (effectiveMaxSpeed > 0f && speed > effectiveMaxSpeed)
        {
            _velLocalXZ = _velLocalXZ.normalized * effectiveMaxSpeed;
            speed = effectiveMaxSpeed;
        }

        // 3) Integrate position in tray local XZ
        _localPos.x += _velLocalXZ.x * dt;
        _localPos.z += _velLocalXZ.y * dt;
        _localPos.y = localHeightAboveSurface;

        // 3.5) Bounds + bounce (in tray local XZ)
        if (useBounds && bounceStrength > 0f)
        {
            bool bounced = false;

            float minX = boundsCenterLocal.x - boundsHalfSize.x;
            float maxX = boundsCenterLocal.x + boundsHalfSize.x;
            float minZ = boundsCenterLocal.y - boundsHalfSize.y;
            float maxZ = boundsCenterLocal.y + boundsHalfSize.y;

            if (_localPos.x < minX)
            {
                _localPos.x = minX;
                _velLocalXZ.x = -_velLocalXZ.x * bounceStrength;
                bounced = true;
            }
            else if (_localPos.x > maxX)
            {
                _localPos.x = maxX;
                _velLocalXZ.x = -_velLocalXZ.x * bounceStrength;
                bounced = true;
            }

            if (_localPos.z < minZ)
            {
                _localPos.z = minZ;
                _velLocalXZ.y = -_velLocalXZ.y * bounceStrength;
                bounced = true;
            }
            else if (_localPos.z > maxZ)
            {
                _localPos.z = maxZ;
                _velLocalXZ.y = -_velLocalXZ.y * bounceStrength;
                bounced = true;
            }

            if (bounced && debugLogs)
            {
                Debug.Log($"[KinematicTrayRider] BOUNCE. newVelLocal=({_velLocalXZ.x:F3},{_velLocalXZ.y:F3})", this);
            }
        }

        Vector3 worldPos = tray.TransformPoint(_localPos);

        if (debugLogs)
        {
            Debug.Log(
                $"[KinematicTrayRider] slopeMag={slopeMag:F4}, " +
                $"accel=({accelLocalXZ.x:F3},{accelLocalXZ.y:F3}), " +
                $"vel=({_velLocalXZ.x:F3},{_velLocalXZ.y:F3}), speed={speed:F3}, " +
                $"localPos=({_localPos.x:F2},{_localPos.z:F2})",
                this);
        }

        // 4) Apply position & rotation
        if (_rb != null && _rb.isKinematic)
        {
            _rb.MovePosition(worldPos);

            if (alignRotationToTray)
            {
                _rb.MoveRotation(tray.rotation);
            }
        }
        else
        {
            transform.position = worldPos;
            if (alignRotationToTray)
            {
                transform.rotation = tray.rotation;
            }
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!tray || !debugDrawBounds)
            return;

        float minX = boundsCenterLocal.x - boundsHalfSize.x;
        float maxX = boundsCenterLocal.x + boundsHalfSize.x;
        float minZ = boundsCenterLocal.y - boundsHalfSize.y;
        float maxZ = boundsCenterLocal.y + boundsHalfSize.y;

        // 4 corners of the bounds rect in tray local XZ
        Vector3 p1Local = new Vector3(minX, localHeightAboveSurface, minZ);
        Vector3 p2Local = new Vector3(minX, localHeightAboveSurface, maxZ);
        Vector3 p3Local = new Vector3(maxX, localHeightAboveSurface, maxZ);
        Vector3 p4Local = new Vector3(maxX, localHeightAboveSurface, minZ);

        Vector3 p1 = tray.TransformPoint(p1Local);
        Vector3 p2 = tray.TransformPoint(p2Local);
        Vector3 p3 = tray.TransformPoint(p3Local);
        Vector3 p4 = tray.TransformPoint(p4Local);

        Gizmos.color = Color.green;
        Gizmos.DrawLine(p1, p2);
        Gizmos.DrawLine(p2, p3);
        Gizmos.DrawLine(p3, p4);
        Gizmos.DrawLine(p4, p1);

        // Cube position
        Vector3 pos = Application.isPlaying
            ? tray.TransformPoint(_localPos)
            : transform.position;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(pos, 0.05f);

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(pos, pos + tray.up * 0.3f);
    }
#endif

    // --------------------------------------------------------------------
    // Public API for external zones (river, mud, etc.)
    // --------------------------------------------------------------------

    /// <summary>
    /// Sets an external speed multiplier, e.g. from a river or mud zone.
    /// 1 = normal speed, 0.5 = half speed, 0 = stopped.
    /// </summary>
    public void SetSpeedMultiplier(float multiplier)
    {
        _externalSpeedMultiplier = Mathf.Max(0f, multiplier);
    }

    /// <summary>
    /// Clears external speed modifier and returns to normal speed.
    /// </summary>
    public void ClearSpeedMultiplier()
    {
        _externalSpeedMultiplier = 1f;
    }
}
