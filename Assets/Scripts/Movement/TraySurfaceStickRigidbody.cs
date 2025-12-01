// FILEPATH: Assets/Scripts/Movement/TraySurfaceStickRigidbody.cs
using UnityEngine;

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

    Rigidbody _rb;
    Collider _ownCollider;

    float _currentSinkOffset;
    bool _isSunk;
    bool _hasTray;

    const float EPS = 0.0001f;

    public bool IsSunk => _isSunk;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _ownCollider = GetComponent<Collider>();

        _rb.useGravity = false;
        _rb.isKinematic = true;
    }

    void Start()
    {
        TryFindTray();
        ApplyLayer(); // apply initial state
    }

    void TryFindTray()
    {
        if (tray != null || !autoFindTray)
        {
            _hasTray = tray != null;
            return;
        }

        TiltTray parentTray = GetComponentInParent<TiltTray>();
        if (parentTray != null)
        {
            tray = parentTray.transform;
            _hasTray = true;
            return;
        }

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

        // 1) Update sinking or rising logic
        UpdateSinkingState(dt);

        // 2) Stick to the tray plane with height offset
        Vector3 trayUp = tray.up;
        Plane trayPlane = new Plane(trayUp, tray.position);

        Vector3 currentPos = transform.position;
        float distanceToPlane = trayPlane.GetDistanceToPoint(currentPos);

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

        // Fully sunk objects no longer rise back
        if (_isSunk)
            return;

        bool fullyInsideRiver = IsFullyInsideAnyRiver();
        float targetOffset = fullyInsideRiver ? sinkDepth : 0f;

        float speed = (targetOffset > _currentSinkOffset) ? sinkingSpeed : risingSpeed;

        float before = _currentSinkOffset;
        _currentSinkOffset = Mathf.MoveTowards(_currentSinkOffset, targetOffset, speed * dt);

        if (debugSinking && !Mathf.Approximately(before, _currentSinkOffset))
        {
            Debug.Log($"[TraySurfaceStickRigidbody] sink {before:F3} -> {_currentSinkOffset:F3}", this);
        }

        // Case 1: Reached FULL sink depth inside river → becomes sunk permanently
        if (!_isSunk &&
            Mathf.Abs(_currentSinkOffset - sinkDepth) < EPS &&
            fullyInsideRiver)
        {
            _isSunk = true;

            if (lockPushWhenSunk)
            {
                var resolver = GetComponent<KinematicCollisionResolver>();
                if (resolver != null)
                    resolver.enabled = false;
            }

            ApplyLayer(); // switch layer

            if (debugSinking)
                Debug.Log("[TraySurfaceStickRigidbody] Fully sunk -> locked and layer switched.", this);

            return;
        }

        // Case 2: Not fully sunk anymore → should rise back (not sunk)
        if (!_isSunk && Mathf.Abs(targetOffset) < EPS && before > EPS)
        {
            ApplyLayer(); // ensure normal layer
        }
    }

    // --------------------------------------------------------------------
    // RIVER CHECK
    // --------------------------------------------------------------------

    bool IsFullyInsideAnyRiver()
    {
        if (_ownCollider == null)
            return false;

        Bounds myBounds = _ownCollider.bounds;

        Collider[] hits = Physics.OverlapBox(
            myBounds.center,
            myBounds.extents,
            transform.rotation,
            riverMask,
            QueryTriggerInteraction.Collide
        );

        foreach (var hit in hits)
        {
            if (hit == _ownCollider)
                continue;

            RiverZone river = hit.GetComponent<RiverZone>();
            if (river == null)
                continue;

            Bounds riverBounds = hit.bounds;

            if (riverBounds.Contains(myBounds.min) &&
                riverBounds.Contains(myBounds.max))
            {
                return true;
            }
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

    void OnValidate()
    {
        if (sinkDepth < 0f) sinkDepth = 0f;
        if (sinkingSpeed < 0f) sinkingSpeed = 0f;
        if (risingSpeed < 0f) risingSpeed = 0f;
    }
}
