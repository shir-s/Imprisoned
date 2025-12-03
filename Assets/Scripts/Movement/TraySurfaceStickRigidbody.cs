// FILEPATH: Assets/Scripts/Movement/TraySurfaceStickRigidbody.cs
using UnityEngine;

/// <summary>
/// Keeps a kinematic rigidbody stuck to a tilted tray:
/// - Projects current position onto the tray plane.
/// - Sets its height along the tray's normal (tray.up).
/// - Optionally aligns rotation to the tray.
///
/// Sinking behaviour (for bridge blocks etc.):
/// - If allowSinkingInRiver is true and this object's collider CENTER
///   is inside any RiverZone collider, it sinks smoothly down along tray.up
///   up to sinkDepth.
/// - If it leaves the river BEFORE reaching sinkDepth, it rises back to baseHeight.
/// - Once it has sunk to sinkDepth, it is considered "locked"/sunk:
///     * IsSunk becomes true
///     * optionally disables KinematicCollisionResolver so it cannot be pushed anymore
///     * it no longer rises back, even if not inside the river afterwards.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class TraySurfaceStickRigidbody : MonoBehaviour
{
    [Header("Tray")]
    [SerializeField] private Transform tray;
    [SerializeField] private bool autoFindTray = true;

    [Header("Height Along Tray")]
    [SerializeField] private float baseHeight = 0.05f;
    [SerializeField] private bool alignRotationToTray = true;

    [Header("Sinking In River")]
    [SerializeField] private bool allowSinkingInRiver = false;
    [SerializeField] private float sinkDepth = 0.1f;
    [SerializeField] private float sinkingSpeed = 0.2f;
    [SerializeField] private float risingSpeed = 0.3f;
    [SerializeField] private bool lockPushWhenSunk = true;

    [Header("River Detection")]
    [Tooltip("Layer mask used to detect river colliders (e.g. Water layer).")]
    [SerializeField] private LayerMask riverMask = ~0;

    [Header("Layer Switching When Sunk")]
    [Tooltip("Layer when fully sunk.")]
    [SerializeField] private string sunkLayer = "SunkObject";

    [Tooltip("Layer when not sunk / normal state.")]
    [SerializeField] private string normalLayer = "Default";

    [Tooltip("Also apply layer change to children?")]
    [SerializeField] private bool applyLayerToChildren = true;

    [Header("Debug")]
    [SerializeField] private bool debugSinking = false;

    private Rigidbody _rb;
    private Collider _ownCollider;

    private float _currentSinkOffset;
    private bool _isSunk;
    private bool _hasTray;

    private const float EPS = 0.0001f;

    /// <summary>True when the object has finished sinking to sinkDepth and is "committed".</summary>
    public bool IsSunk => _isSunk;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _ownCollider = GetComponent<Collider>();

        _rb.useGravity = false;   // height controlled manually
        _rb.isKinematic = true;   // fully script driven movement
    }

    private void Start()
    {
        TryFindTray();
        ApplyLayer(); // apply initial state
    }

    private void TryFindTray()
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

    private void FixedUpdate()
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

    private void UpdateSinkingState(float dt)
    {
        if (!allowSinkingInRiver)
            return;

        // Fully sunk objects no longer rise back
        if (_isSunk)
            return;

        bool insideRiver = IsCenterInsideAnyRiver();
        float targetOffset = insideRiver ? sinkDepth : 0f;

        float speed = (targetOffset > _currentSinkOffset) ? sinkingSpeed : risingSpeed;

        float before = _currentSinkOffset;
        _currentSinkOffset = Mathf.MoveTowards(_currentSinkOffset, targetOffset, speed * dt);

        if (debugSinking && !Mathf.Approximately(before, _currentSinkOffset))
        {
            Debug.Log($"[TraySurfaceStickRigidbody] sink {before:F3} -> {_currentSinkOffset:F3} (insideRiver={insideRiver})", this);
        }

        // Case 1: Reached FULL sink depth while inside river → lock as sunk.
        if (!_isSunk &&
            Mathf.Abs(_currentSinkOffset - sinkDepth) < EPS &&
            insideRiver)
        {
            _isSunk = true;

            if (lockPushWhenSunk)
            {
                // Disable custom collision-based pushing so it becomes static.
                var resolver = GetComponent<KinematicCollisionResolver>();
                if (resolver != null)
                    resolver.enabled = false;
            }

            ApplyLayer(); // switch layer

            if (debugSinking)
            {
                Debug.Log("[TraySurfaceStickRigidbody] Fully sunk -> locked and layer switched.", this);
            }

            return;
        }

        // Case 2: Not fully sunk anymore → should rise back (not sunk)
        if (!_isSunk && Mathf.Abs(targetOffset) < EPS && before > EPS)
        {
            ApplyLayer(); // ensure normal layer
        }
    }

    // --------------------------------------------------------------------
    // RIVER CHECK (simple: center inside any RiverZone collider)
    // --------------------------------------------------------------------

    private bool IsCenterInsideAnyRiver()
    {
        if (_ownCollider == null)
            return false;

        Bounds myBounds = _ownCollider.bounds;
        Vector3 center = myBounds.center;

        Collider[] hits = Physics.OverlapBox(
            myBounds.center,
            myBounds.extents,
            transform.rotation,
            riverMask,
            QueryTriggerInteraction.Collide
        );

        if (debugSinking)
        {
            Debug.Log($"[TraySurfaceStickRigidbody] River hits count = {hits.Length}", this);
        }

        foreach (var hit in hits)
        {
            if (hit == null || hit == _ownCollider)
                continue;

            // Colliders can be on children of the RiverZone object
            RiverZone river = hit.GetComponentInParent<RiverZone>();
            if (river == null)
                continue;

            if (hit.bounds.Contains(center))
            {
                if (debugSinking)
                {
                    Debug.Log($"[TraySurfaceStickRigidbody] Center inside river '{river.name}'.", this);
                }
                return true;
            }
        }

        if (debugSinking)
        {
            Debug.Log("[TraySurfaceStickRigidbody] Center not inside any river.", this);
        }

        return false;
    }

    // --------------------------------------------------------------------
    // LAYER SWITCHING
    // --------------------------------------------------------------------

    private void ApplyLayer()
    {
        string targetLayer = _isSunk ? sunkLayer : normalLayer;
        int layerID = LayerMask.NameToLayer(targetLayer);

        if (layerID < 0)
        {
            Debug.LogWarning($"[TraySurfaceStickRigidbody] Layer '{targetLayer}' does not exist.");
            return;
        }

        if (applyLayerToChildren)
        {
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = layerID;
        }
        else
        {
            gameObject.layer = layerID;
        }
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

        ApplyLayer(); // revert to normal layer
    }

    private void OnValidate()
    {
        if (sinkDepth < 0f) sinkDepth = 0f;
        if (sinkingSpeed < 0f) sinkingSpeed = 0f;
        if (risingSpeed < 0f) risingSpeed = 0f;
    }
}
