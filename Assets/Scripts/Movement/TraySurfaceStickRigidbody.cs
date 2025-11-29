// FILEPATH: Assets/Scripts/Movement/TraySurfaceStickRigidbody.cs
using UnityEngine;

/// <summary>
/// Keeps a kinematic rigidbody stuck to a tilted tray:
/// - Projects current position onto the tray plane.
/// - Sets its height along the tray's normal (tray.up).
/// - Optionally aligns rotation to the tray.
///
/// Sinking behaviour (for bridge blocks etc.):
/// - If allowSinkingInRiver is true and this object is FULLY inside a RiverZone
///   collider, it sinks smoothly down along tray.up up to sinkDepth.
/// - If it leaves the river BEFORE reaching sinkDepth, it rises back to baseHeight.
/// - Once it has sunk to sinkDepth, it is considered "locked"/sunk:
///     * IsSunk becomes true
///     * optionally disables KinematicCollisionResolver so it cannot be pushed anymore
///     * it no longer rises back, even if not fully inside the river afterwards.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class TraySurfaceStickRigidbody : MonoBehaviour
{
    [Header("Tray")]
    [Tooltip("Tray transform (TiltTray). If empty, will try to auto-find one in parents or in the scene.")]
    [SerializeField] private Transform tray;

    [Tooltip("Try to automatically find a TiltTray if 'tray' is not assigned.")]
    [SerializeField] private bool autoFindTray = true;

    [Header("Height Along Tray")]
    [Tooltip("Base height above the tray plane along tray.up.")]
    [SerializeField] private float baseHeight = 0.05f;

    [Tooltip("Align object's rotation with the tray each physics step.")]
    [SerializeField] private bool alignRotationToTray = true;

    [Header("Sinking In River")]
    [Tooltip("If true, this object can sink when fully inside a RiverZone collider.")]
    [SerializeField] private bool allowSinkingInRiver = false;

    [Tooltip("How far below the baseHeight the object should end up when fully sunk (positive value).")]
    [SerializeField] private float sinkDepth = 0.1f;

    [Tooltip("How fast the object sinks (units of height per second).")]
    [SerializeField] private float sinkingSpeed = 0.2f;

    [Tooltip("How fast the object rises back up when leaving the river before fully sinking.")]
    [SerializeField] private float risingSpeed = 0.3f;

    [Tooltip("If true, once sunk we disable KinematicCollisionResolver so this object stops being pushable.")]
    [SerializeField] private bool lockPushWhenSunk = true;

    [Header("River Detection")]
    [Tooltip("Optional mask for river colliders. If zero, all layers are checked.")]
    [SerializeField] private LayerMask riverMask = ~0;

    [Header("Debug")]
    [SerializeField] private bool debugSinking = false;

    Rigidbody _rb;
    Collider _ownCollider;

    float _currentSinkOffset;   // 0 → sinkDepth
    bool _isSunk;               // has reached sinkDepth and locked
    bool _hasTray;

    const float EPS = 0.0001f;

    /// <summary>True when the object has finished sinking to sinkDepth and is "committed".</summary>
    public bool IsSunk => _isSunk;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _ownCollider = GetComponent<Collider>();

        _rb.useGravity = false;   // height controlled manually
        _rb.isKinematic = true;   // fully script driven movement
    }

    void Start()
    {
        TryFindTray();
    }

    void TryFindTray()
    {
        if (tray != null || !autoFindTray)
        {
            _hasTray = tray != null;
            return;
        }

        // 1) Try parent TiltTray
        TiltTray parentTray = GetComponentInParent<TiltTray>();
        if (parentTray != null)
        {
            tray = parentTray.transform;
            _hasTray = true;
            return;
        }

        // 2) Try any TiltTray in the scene
        TiltTray anyTray = FindObjectOfType<TiltTray>();
        if (anyTray != null)
        {
            tray = anyTray.transform;
            _hasTray = true;
        }
    }

    void FixedUpdate()
    {
        if (!_hasTray)
        {
            TryFindTray();
            if (!_hasTray)
                return;
        }

        float dt = Time.fixedDeltaTime;

        // 1) Update sinking / rising
        UpdateSinkingState(dt);

        // 2) Stick to tray plane at (baseHeight - currentSinkOffset)
        Vector3 trayUp = tray.up;
        Plane trayPlane = new Plane(trayUp, tray.position);

        Vector3 currentPos = transform.position;
        float distanceToPlane = trayPlane.GetDistanceToPoint(currentPos);

        // Point on tray plane directly under/over current position
        Vector3 onPlane = currentPos - trayUp * distanceToPlane;

        float targetHeight = baseHeight - _currentSinkOffset;
        Vector3 targetPos = onPlane + trayUp * targetHeight;

        _rb.MovePosition(targetPos);

        if (alignRotationToTray)
        {
            _rb.MoveRotation(tray.rotation);
        }
    }

    void UpdateSinkingState(float dt)
    {
        if (!allowSinkingInRiver)
            return;

        if (_isSunk)
            return;

        bool fullyInsideRiver = IsFullyInsideAnyRiver();

        float targetOffset = fullyInsideRiver ? sinkDepth : 0f;

        // Choose sinking or rising speed based on which direction we are going.
        float speed = (targetOffset > _currentSinkOffset) ? sinkingSpeed : risingSpeed;

        float before = _currentSinkOffset;
        _currentSinkOffset = Mathf.MoveTowards(_currentSinkOffset, targetOffset, speed * dt);

        if (debugSinking && !Mathf.Approximately(before, _currentSinkOffset))
        {
            Debug.Log($"[TraySurfaceStickRigidbody] sinkOffset {before:F3} -> {_currentSinkOffset:F3} (target={targetOffset:F3}, inRiver={fullyInsideRiver})", this);
        }

        // Reached full sink depth while still in river -> lock as bridge.
        if (!_isSunk && Mathf.Abs(_currentSinkOffset - sinkDepth) < EPS && fullyInsideRiver)
        {
            _isSunk = true;

            if (lockPushWhenSunk)
            {
                // Disable custom collision-based pushing so it becomes static.
                var resolver = GetComponent<KinematicCollisionResolver>();
                if (resolver != null)
                    resolver.enabled = false;
            }

            if (debugSinking)
            {
                Debug.Log("[TraySurfaceStickRigidbody] Reached sinkDepth -> now sunk & locked.", this);
            }
        }
    }

    /// <summary>
    /// Returns true if this object's collider bounds are fully inside ANY RiverZone collider.
    /// This is evaluated each physics step, so if the block is pushed out before it sinks
    /// fully, it will stop sinking and rise back.
    /// </summary>
    bool IsFullyInsideAnyRiver()
    {
        if (_ownCollider == null)
            return false;

        Bounds myBounds = _ownCollider.bounds;

        // Use OverlapBox to find possible rivers around us
        Collider[] hits = Physics.OverlapBox(
            myBounds.center,
            myBounds.extents,
            transform.rotation,
            riverMask,
            QueryTriggerInteraction.Collide
        );

        for (int i = 0; i < hits.Length; i++)
        {
            Collider c = hits[i];
            if (c == _ownCollider)
                continue;

            RiverZone river = c.GetComponent<RiverZone>();
            if (river == null)
                continue;

            Bounds riverBounds = c.bounds;

            // Fully inside river?
            if (riverBounds.Contains(myBounds.min) && riverBounds.Contains(myBounds.max))
            {
                return true;
            }
        }

        return false;
    }

    public void ResetSinking()
    {
        _isSunk = false;
        _currentSinkOffset = 0f;

        if (lockPushWhenSunk)
        {
            var resolver = GetComponent<KinematicCollisionResolver>();
            if (resolver != null)
                resolver.enabled = true;
        }
    }

    void OnValidate()
    {
        if (sinkDepth < 0f) sinkDepth = 0f;
        if (sinkingSpeed < 0f) sinkingSpeed = 0f;
        if (risingSpeed < 0f) risingSpeed = 0f;
    }
}
