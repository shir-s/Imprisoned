// FILEPATH: Assets/Scripts/Camera/FollowCubeCamera.cs
using UnityEngine;

[DisallowMultipleComponent]
public class FollowCubeCamera : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;   // current drawing cube

    [Header("Follow")]
    [Tooltip("Offset from the target in world space.")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 6f, -6f);

    [Tooltip("How fast the camera moves toward the target position.")]
    [SerializeField] private float followSmooth = 10f;

    [Header("Look")]
    [Tooltip("If true, camera will look at the target.")]
    [SerializeField] private bool lookAtTarget = true;

    [Tooltip("How fast the camera rotates toward the look direction.")]
    [SerializeField] private float lookSmooth = 10f;

    private void OnEnable()
    {
        var mgr = CubeStackManager.Instance;
        if (mgr != null)
        {
            mgr.MainCubeChanged += HandleMainCubeChanged;

            // If we don't have a target yet but manager already has one, use it.
            if (target == null && mgr.CurrentMainTransform != null)
            {
                target = mgr.CurrentMainTransform;
            }
        }
    }

    private void OnDisable()
    {
        var mgr = CubeStackManager.Instance;
        if (mgr != null)
        {
            mgr.MainCubeChanged -= HandleMainCubeChanged;
        }
    }

    private void HandleMainCubeChanged(GameObject cubeGO)
    {
        if (cubeGO == null) return;
        target = cubeGO.transform;
    }

    private void LateUpdate()
    {
        if (target == null)
            return;

        // Smooth follow
        Vector3 desiredPos = target.position + offset;
        transform.position = Vector3.Lerp(
            transform.position,
            desiredPos,
            1f - Mathf.Exp(-followSmooth * Time.deltaTime)
        );

        if (!lookAtTarget)
            return;

        // Smooth look at target
        Vector3 dir = (target.position - transform.position);
        if (dir.sqrMagnitude > 0.0001f)
        {
            Quaternion desiredRot = Quaternion.LookRotation(dir.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                desiredRot,
                1f - Mathf.Exp(-lookSmooth * Time.deltaTime)
            );
        }
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }
}
