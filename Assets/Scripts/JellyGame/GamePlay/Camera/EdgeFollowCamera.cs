// FILEPATH: Assets/Scripts/Camera/EdgeFollowCamera.cs
using UnityEngine;
using System.Collections;
using JellyGame.GamePlay.Managers; // Added for EventManager

[DisallowMultipleComponent]
public class EdgeFollowCamera : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera cam;
    [Tooltip("Who to follow. If empty, will try to find the player at runtime (by tag or type).")]
    [SerializeField] private Transform target;
    [Tooltip("Tag used to find player when target is not set. Your player (PlayerJelly) uses DrawingCube.")]
    [SerializeField] private string playerTag = "DrawingCube";

    [Header("Start / Safety")]
    [Tooltip("On Start/Enable, reposition camera so target is centered.")]
    [SerializeField] private bool snapToTargetOnStart = true;
    [Tooltip("If the target somehow ends up behind the camera, snap to recover view.")]
    [SerializeField] private bool snapIfTargetBehindCamera = true;

    [Header("Edge Follow")]
    [Tooltip("Screen margin (0..0.45). 0.15 means 15% from each side.")]
    [SerializeField, Range(0f, 0.45f)] private float screenMargin = 0.15f;
    [SerializeField] private float followStrength = 1.0f;

    [Header("Dead Zone Easing")]
    [SerializeField, Range(0.5f, 4f)] private float easingPower = 2.0f;
    [SerializeField, Range(0f, 1f)] private float easingSoftness = 0.6f;

    [Header("Smoothing")]
    [SerializeField] private float smoothTime = 0.18f;
    [SerializeField] private float maxSpeed = 30f;

    [Header("Movement Axes")]
    [SerializeField] private bool moveOnX = true;
    [SerializeField] private bool moveOnY = false; 
    [SerializeField] private bool moveOnZ = true;

    [Header("Optional World Bounds")]
    [SerializeField] private bool useBounds = false;
    [SerializeField] private Vector3 boundsMin = new Vector3(-100f, -100f, -100f);
    [SerializeField] private Vector3 boundsMax = new Vector3(100f, 100f, 100f);

    [Header("Death Zoom Settings")]
    [SerializeField] private float zoomDuration = 1.5f;
    [Tooltip("How far back (horizontally) from the death point the camera should end up.")]
    [SerializeField] private float zoomEndDistance = 6.0f; 
    [Tooltip("How high (vertically) above the death point the camera should end up.")]
    [SerializeField] private float zoomEndHeight = 5.0f;   
    [SerializeField] private AnimationCurve zoomCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    private Vector3 _velocity;
    private bool _inDeathSequence = false;

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
        EventManager.StartListening(EventManager.GameEvent.PreDeathSequence, OnPreDeathSequence);
    }

    private void OnDisable()
    {
        EventManager.StopListening(EventManager.GameEvent.PreDeathSequence, OnPreDeathSequence);
    }

    private void Start()
    {
        if (target == null)
            TryFindPlayer();
        if (snapToTargetOnStart && target != null)
            SnapTargetToViewportCenter();
    }

    /// <summary>Find player at runtime when target is not set (player spawns per level). Uses tag DrawingCube (PlayerJelly).</summary>
    private void TryFindPlayer()
    {
        if (target != null) return;
        string tagToUse = string.IsNullOrEmpty(playerTag) ? "DrawingCube" : playerTag;
        GameObject player = GameObject.FindGameObjectWithTag(tagToUse);
        if (player != null)
        {
            target = player.transform;
            _velocity = Vector3.zero;
        }
    }

    private void LateUpdate()
    {
        if (target == null)
            TryFindPlayer();
        // STOP updating if we are missing refs OR if we are in the middle of the death zoom
        if (cam == null || target == null || _inDeathSequence)
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
        if (vp.x < minX) dx = vp.x - minX;         
        else if (vp.x > maxX) dx = vp.x - maxX;    

        float dy = 0f;
        if (vp.y < minY) dy = vp.y - minY;
        else if (vp.y > maxY) dy = vp.y - maxY;

        if (Mathf.Approximately(dx, 0f) && Mathf.Approximately(dy, 0f))
            return;

        float easedDx = ApplyDeadZoneEasing(dx);
        float easedDy = ApplyDeadZoneEasing(dy);

        float depth = vp.z;

        Vector3 worldAtVp = cam.ViewportToWorldPoint(new Vector3(vp.x, vp.y, depth));
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
        if (abs <= 0f) return 0f;

        float denom = Mathf.Max(0.0001f, screenMargin);
        float t = Mathf.Clamp01(abs / denom);

        float smooth = t * t * (3f - 2f * t); 
        float softer = Mathf.Pow(smooth, Mathf.Max(0.5f, easingPower));
        float eased = Mathf.Lerp(smooth, softer, easingSoftness);

        return Mathf.Sign(deltaOutside) * abs * eased;
    }

    private void SnapTargetToViewportCenter()
    {
        if (cam == null || target == null) return;

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

    // --- Death Zoom Logic ---

    private void OnPreDeathSequence(object data)
    {
        if (data is EventManager.PreDeathEventData deathData)
        {
            StartCoroutine(ZoomToDeathRoutine(deathData.deathPosition, deathData.onSequenceComplete));
        }
    }

    private IEnumerator ZoomToDeathRoutine(Vector3 deathPos, System.Action onComplete)
    {
        _inDeathSequence = true;
        _velocity = Vector3.zero; // Stop any SmoothDamp momentum

        Vector3 startPos = transform.position;
        Quaternion startRot = transform.rotation;

        // Determine "Backward" direction relative to the camera to know where to zoom FROM
        Vector3 camFwdXZ = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        if (camFwdXZ.sqrMagnitude < 0.01f) camFwdXZ = Vector3.forward; // Default if looking straight down
        
        Vector3 camBackXZ = -camFwdXZ;

        // End position: at deathPos, but pulled back and up
        Vector3 endPos = deathPos + (camBackXZ * zoomEndDistance) + (Vector3.up * zoomEndHeight);

        // Rotation: Look directly at the death spot
        Quaternion endRot = Quaternion.LookRotation(deathPos - endPos);

        float elapsed = 0f;
        while (elapsed < zoomDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / zoomDuration);
            float curveT = zoomCurve.Evaluate(t);

            transform.position = Vector3.Lerp(startPos, endPos, curveT);
            transform.rotation = Quaternion.Slerp(startRot, endRot, curveT);

            yield return null;
        }

        // Ensure we land exactly at the end
        transform.position = endPos;
        transform.rotation = endRot;

        // IMPORTANT: We do NOT set _inDeathSequence to false here.
        // We want the camera to stay frozen on the death spot until the scene reloads or game restarts.

        // Notify the caller (CubeScaler) that zoom is done, so it can destroy the object / trigger GameOver
        onComplete?.Invoke();
    }
}