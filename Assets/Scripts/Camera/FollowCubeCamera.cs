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

    // Internal velocity used by SmoothDamp
    private Vector3 _followVelocity = Vector3.zero;

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

    private void OnActiveCubeChanged(object eventData)
    {
        if (eventData == null)
            return;

        if (eventData is Transform tr)
            target = tr;
        else if (eventData is GameObject go && go != null)
            target = go.transform;
    }

    private void LateUpdate()
    {
        if (target == null)
            return;

        // --- FOLLOW FROM BEHIND (desired position) ---
        Vector3 cubeForward = target.forward;

        Vector3 desiredPos =
            target.position
            - cubeForward * followDistance      // behind
            + Vector3.up * followHeight;        // above

        // Very smooth position using SmoothDamp
        float smoothTime = Mathf.Max(0.01f, followSmooth);
        transform.position = Vector3.SmoothDamp(
            transform.position,
            desiredPos,
            ref _followVelocity,
            smoothTime
        );

        // --- LOOK AT THE CUBE (smoothed rotation) ---
        Vector3 dir = target.position - transform.position;
        if (dir.sqrMagnitude > 0.0001f)
        {
            Quaternion desiredRot = Quaternion.LookRotation(dir.normalized, Vector3.up);

            // limit rotation speed (degrees per second)
            float maxDegreesPerSecond = Mathf.Max(1f, lookSmooth);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                desiredRot,
                maxDegreesPerSecond * Time.deltaTime
            );
        }
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }
}
