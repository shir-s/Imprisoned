// FILEPATH: Assets/Scripts/Camera/FollowCubeCamera.cs
using UnityEngine;

[DisallowMultipleComponent]
public class FollowCubeCamera : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;   // current drawing cube

    [Header("Dynamic Follow Behind")]
    [Tooltip("Distance behind the cube (along -forward).")]
    [SerializeField] private float followDistance = 6f;

    [Tooltip("Height above the cube.")]
    [SerializeField] private float followHeight = 6f;

    [Header("Smoothness")]
    [Tooltip("Smooth time (in seconds) for following position. Larger = slower, smoother.")]
    [SerializeField] private float followSmooth = 0.4f;

    [Tooltip("Maximum rotation speed in degrees per second.")]
    [SerializeField] private float lookSmooth = 120f;

    [Header("Events")]
    [SerializeField] private bool listenToActiveCubeEvent = true;

    [Header("Initialization")]
    [Tooltip("If true, camera will snap instantly to the correct position when first activated or when target changes.")]
    [SerializeField] private bool snapOnTargetChange = true;

    // Internal velocity used by SmoothDamp
    private Vector3 _followVelocity = Vector3.zero;

    // Track if we need to snap on next update
    private bool _needsSnap = true;

    private void Awake()
    {
        if (listenToActiveCubeEvent)
        {
            EventManager.StartListening(EventManager.GameEvent.ActiveCubeChanged, OnActiveCubeChanged);
        }
    }

    private void OnDestroy()
    {
        if (listenToActiveCubeEvent)
        {
            EventManager.StopListening(EventManager.GameEvent.ActiveCubeChanged, OnActiveCubeChanged);
        }
    }

    private void OnEnable()
    {
        // When camera becomes active, snap to target position immediately
        _needsSnap = true;
    }

    private void OnActiveCubeChanged(object eventData)
    {
        if (eventData == null)
            return;

        Transform newTarget = null;

        if (eventData is Transform tr)
            newTarget = tr;
        else if (eventData is GameObject go && go != null)
            newTarget = go.transform;

        if (newTarget != null)
        {
            SetTarget(newTarget);
        }
    }

    private void LateUpdate()
    {
        if (target == null)
            return;

        // Calculate desired position
        Vector3 cubeForward = target.forward;
        Vector3 desiredPos =
            target.position
            - cubeForward * followDistance      // behind
            + Vector3.up * followHeight;        // above

        // Calculate desired rotation
        Vector3 dir = target.position - desiredPos;
        Quaternion desiredRot = Quaternion.identity;
        if (dir.sqrMagnitude > 0.0001f)
        {
            desiredRot = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        // Snap instantly or smooth follow
        if (_needsSnap)
        {
            // Instant snap to position and rotation
            transform.position = desiredPos;
            transform.rotation = desiredRot;
            _followVelocity = Vector3.zero;
            _needsSnap = false;
        }
        else
        {
            // Smooth position using SmoothDamp
            float smoothTime = Mathf.Max(0.01f, followSmooth);
            transform.position = Vector3.SmoothDamp(
                transform.position,
                desiredPos,
                ref _followVelocity,
                smoothTime
            );

            // Smooth rotation
            if (dir.sqrMagnitude > 0.0001f)
            {
                float maxDegreesPerSecond = Mathf.Max(1f, lookSmooth);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    desiredRot,
                    maxDegreesPerSecond * Time.deltaTime
                );
            }
        }
    }

    /// <summary>
    /// Set a new target for the camera to follow.
    /// </summary>
    /// <param name="newTarget">The new target transform.</param>
    /// <param name="snapImmediately">If true, camera snaps to position instantly. If false, uses the snapOnTargetChange setting.</param>
    public void SetTarget(Transform newTarget, bool? snapImmediately = null)
    {
        target = newTarget;

        bool shouldSnap = snapImmediately ?? snapOnTargetChange;
        
        if (shouldSnap && target != null)
        {
            _needsSnap = true;
        }
    }

    /// <summary>
    /// Force the camera to snap to its target position immediately on the next frame.
    /// Useful when teleporting the target or switching cameras.
    /// </summary>
    public void SnapToTarget()
    {
        _needsSnap = true;
    }

    /// <summary>
    /// Immediately snap the camera to its target position right now (not waiting for next frame).
    /// </summary>
    public void SnapToTargetImmediate()
    {
        if (target == null)
            return;

        Vector3 cubeForward = target.forward;
        Vector3 desiredPos =
            target.position
            - cubeForward * followDistance
            + Vector3.up * followHeight;

        transform.position = desiredPos;

        Vector3 dir = target.position - desiredPos;
        if (dir.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        _followVelocity = Vector3.zero;
        _needsSnap = false;
    }
}