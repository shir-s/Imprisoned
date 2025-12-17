// FILEPATH: Assets/Scripts/Camera/EdgeFollowCamera.cs
using UnityEngine;

[DisallowMultipleComponent]
public class EdgeFollowCamera : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera cam;
    [SerializeField] private Transform target;

    [Header("Start / Safety")]
    [Tooltip("On Start/Enable, reposition camera so target is centered (prevents initial jump / losing the target).")]
    [SerializeField] private bool snapToTargetOnStart = true;

    [Tooltip("If the target somehow ends up behind the camera, snap to recover view.")]
    [SerializeField] private bool snapIfTargetBehindCamera = true;

    [Header("Edge Follow")]
    [Tooltip("Screen margin (0..0.45). 0.15 means 15% from each side.")]
    [SerializeField, Range(0f, 0.45f)] private float screenMargin = 0.15f;

    [Tooltip("Overall strength of the edge push.")]
    [SerializeField] private float followStrength = 1.0f;

    [Header("Dead Zone Easing")]
    [Tooltip("How the push ramps up after leaving the safe area. 1 = smooth, 2-3 = gentler start.")]
    [SerializeField, Range(0.5f, 4f)] private float easingPower = 2.0f;

    [Tooltip("Extra softness near the edge (0 = more direct, 1 = very soft start).")]
    [SerializeField, Range(0f, 1f)] private float easingSoftness = 0.6f;

    [Header("Smoothing")]
    [Tooltip("Time to smooth toward the desired position. Smaller = snappier.")]
    [SerializeField] private float smoothTime = 0.18f;

    [Tooltip("Optional max speed for SmoothDamp (units/sec).")]
    [SerializeField] private float maxSpeed = 30f;

    [Header("Movement Axes")]
    [SerializeField] private bool moveOnX = true;
    [SerializeField] private bool moveOnY = false; // usually false for top-down
    [SerializeField] private bool moveOnZ = true;

    [Header("Optional World Bounds")]
    [SerializeField] private bool useBounds = false;
    [SerializeField] private Vector3 boundsMin = new Vector3(-100f, -100f, -100f);
    [SerializeField] private Vector3 boundsMax = new Vector3(100f, 100f, 100f);

    private Vector3 _velocity;

    private void Reset()
    {
        cam = GetComponent<Camera>();
        if (cam == null) cam = Camera.main;
    }

    private void Awake()
    {
        if (cam == null) cam = GetComponent<Camera>();
        if (cam == null) cam = Camera.main;
    }

    private void OnEnable()
    {
        _velocity = Vector3.zero;
    }

    private void Start()
    {
        if (snapToTargetOnStart)
            SnapTargetToViewportCenter();
    }

    private void LateUpdate()
    {
        if (cam == null || target == null)
            return;

        Vector3 vp = cam.WorldToViewportPoint(target.position);

        if (vp.z <= 0.01f)
        {
            if (snapIfTargetBehindCamera)
                SnapTargetToViewportCenter();
            return;
        }

        float minX = screenMargin;
        float maxX = 1f - screenMargin;
        float minY = screenMargin;
        float maxY = 1f - screenMargin;

        float dx = 0f;
        if (vp.x < minX) dx = vp.x - minX;         // negative
        else if (vp.x > maxX) dx = vp.x - maxX;    // positive

        float dy = 0f;
        if (vp.y < minY) dy = vp.y - minY;
        else if (vp.y > maxY) dy = vp.y - maxY;

        if (Mathf.Approximately(dx, 0f) && Mathf.Approximately(dy, 0f))
            return;

        float easedDx = ApplyDeadZoneEasing(dx);
        float easedDy = ApplyDeadZoneEasing(dy);

        float depth = vp.z;

        Vector3 worldAtVp = cam.ViewportToWorldPoint(new Vector3(vp.x, vp.y, depth));

        // FIX: use + (not -) so the camera moves in the correct direction
        Vector3 worldAtVpShifted = cam.ViewportToWorldPoint(new Vector3(vp.x + easedDx, vp.y + easedDy, depth));

        Vector3 neededWorldDelta = (worldAtVpShifted - worldAtVp) * followStrength;

        Vector3 current = transform.position;
        Vector3 desired = current + neededWorldDelta;

        if (!moveOnX) desired.x = current.x;
        if (!moveOnY) desired.y = current.y;
        if (!moveOnZ) desired.z = current.z;

        if (useBounds)
        {
            desired.x = Mathf.Clamp(desired.x, boundsMin.x, boundsMax.x);
            desired.y = Mathf.Clamp(desired.y, boundsMin.y, boundsMax.y);
            desired.z = Mathf.Clamp(desired.z, boundsMin.z, boundsMax.z);
        }

        transform.position = Vector3.SmoothDamp(
            current,
            desired,
            ref _velocity,
            Mathf.Max(0.0001f, smoothTime),
            Mathf.Max(0f, maxSpeed),
            Time.deltaTime
        );
    }

    private float ApplyDeadZoneEasing(float deltaOutside)
    {
        float abs = Mathf.Abs(deltaOutside);
        if (abs <= 0f)
            return 0f;

        float denom = Mathf.Max(0.0001f, screenMargin);
        float t = Mathf.Clamp01(abs / denom);

        float smooth = t * t * (3f - 2f * t); // SmoothStep
        float softer = Mathf.Pow(smooth, Mathf.Max(0.5f, easingPower));
        float eased = Mathf.Lerp(smooth, softer, easingSoftness);

        return Mathf.Sign(deltaOutside) * abs * eased;
    }

    private void SnapTargetToViewportCenter()
    {
        if (cam == null || target == null)
            return;

        Vector3 vp = cam.WorldToViewportPoint(target.position);
        float depth = Mathf.Max(0.01f, vp.z);

        Vector3 worldAtCenter = cam.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, depth));
        Vector3 delta = target.position - worldAtCenter;

        Vector3 current = transform.position;
        Vector3 desired = current + delta;

        if (!moveOnX) desired.x = current.x;
        if (!moveOnY) desired.y = current.y;
        if (!moveOnZ) desired.z = current.z;

        if (useBounds)
        {
            desired.x = Mathf.Clamp(desired.x, boundsMin.x, boundsMax.x);
            desired.y = Mathf.Clamp(desired.y, boundsMin.y, boundsMax.y);
            desired.z = Mathf.Clamp(desired.z, boundsMin.z, boundsMax.z);
        }

        transform.position = desired;
        _velocity = Vector3.zero;
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        _velocity = Vector3.zero;

        if (snapToTargetOnStart && isActiveAndEnabled)
            SnapTargetToViewportCenter();
    }
}
