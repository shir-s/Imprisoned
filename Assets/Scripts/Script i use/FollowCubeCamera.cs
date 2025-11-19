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
    [SerializeField] private float followSmooth = 10f;
    [SerializeField] private float lookSmooth = 10f;

    [Header("Events")]
    [SerializeField] private bool listenToActiveCubeEvent = true;

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

        // --- FOLLOW FROM BEHIND ---
        Vector3 cubeForward = target.forward;

        // Desired position = behind cube + above cube
        Vector3 desiredPos =
            target.position
            - cubeForward * followDistance      // behind
            + Vector3.up * followHeight;        // above

        // Smoothly move camera
        float tPos = 1f - Mathf.Exp(-followSmooth * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, desiredPos, tPos);

        // --- LOOK AT THE CUBE ---
        Vector3 dir = (target.position - transform.position);
        if (dir.sqrMagnitude > 0.0001f)
        {
            Quaternion desiredRot = Quaternion.LookRotation(dir.normalized, Vector3.up);
            float tRot = 1f - Mathf.Exp(-lookSmooth * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, tRot);
        }
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }
}
