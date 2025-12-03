// FILEPATH: Assets/Scripts/Camera/FollowCubeCamera.cs
using UnityEngine;

[DisallowMultipleComponent]
public class FollowCubeCamera : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;

    [Header("Dynamic Follow Behind")]
    [SerializeField] private float followDistance = 6f;
    [SerializeField] private float followHeight = 6f;

    [Header("Occlusion Handling")]
    [Tooltip("Layers considered as obstacles between camera and target.")]
    [SerializeField] private LayerMask occlusionLayers = ~0;

    [Tooltip("How much extra height the camera can gain to see over obstacles behind the character.")]
    [SerializeField] private float maxExtraHeight = 4f;

    [Tooltip("How far above the camera we check for overhead blockers (trees, ceilings, etc.).")]
    [SerializeField] private float overheadCheckDistance = 3f;

    [Header("Centering Mode")]
    [Tooltip("If true, the camera will always keep the target EXACTLY at the screen center (no smoothing).")]
    [SerializeField] private bool hardLockCenter = true;

    [Header("Smoothness (ignored if Hard Lock is enabled)")]
    [SerializeField] private float followSmooth = 0.4f;
    [SerializeField] private float lookSmooth = 120f;

    [Header("Events")]
    [SerializeField] private bool listenToActiveCubeEvent = true;

    [Header("Initialization")]
    [SerializeField] private bool snapOnTargetChange = true;

    private Vector3 _followVelocity = Vector3.zero;
    private bool _needsSnap = true;

    private void Awake()
    {
        if (listenToActiveCubeEvent)
            EventManager.StartListening(EventManager.GameEvent.ActiveCubeChanged, OnActiveCubeChanged);
    }

    private void OnDestroy()
    {
        if (listenToActiveCubeEvent)
            EventManager.StopListening(EventManager.GameEvent.ActiveCubeChanged, OnActiveCubeChanged);
    }

    private void OnEnable()
    {
        _needsSnap = true;
    }

    private void OnActiveCubeChanged(object data)
    {
        Transform newTarget = null;

        if (data is Transform tr) newTarget = tr;
        else if (data is GameObject go) newTarget = go.transform;

        if (newTarget != null)
            SetTarget(newTarget);
    }

    private void LateUpdate()
    {
        if (target == null) return;

        // Base camera position: behind + above the target.
        Vector3 cubeForward = target.forward;
        Vector3 basePos =
            target.position
            - cubeForward * followDistance
            + Vector3.up * followHeight;

        Vector3 desiredPos = basePos;

        // -------------------------
        // OCCLUSION / HEIGHT ADJUST
        // -------------------------
        // Check if something blocks the view between camera and target
        if (Physics.Linecast(basePos, target.position, out RaycastHit hitToTarget, occlusionLayers, QueryTriggerInteraction.Ignore))
        {
            // Now check if there is ALSO something above the camera that is NOT the same collider.
            bool overheadBlocked = false;

            if (Physics.Raycast(basePos, Vector3.up, out RaycastHit overheadHit, overheadCheckDistance, occlusionLayers, QueryTriggerInteraction.Ignore))
            {
                if (overheadHit.collider != hitToTarget.collider)
                {
                    // Different collider above camera = tree / ceiling / canopy.
                    // In that case we DO NOT raise the camera to avoid getting buried in it.
                    overheadBlocked = true;
                }
            }

            if (!overheadBlocked)
            {
                // We are blocked by something behind the character and there is no separate
                // overhead blocker, so we can safely raise the camera.
                desiredPos = basePos + Vector3.up * maxExtraHeight;
            }
            else
            {
                // There is an overhead blocker that is NOT the same as the one between camera & target.
                // Keep base height to avoid shoving the camera into the tree canopy.
                desiredPos = basePos;
            }
        }

        // -------------------------
        // FINAL ROTATION (ALWAYS LOOK AT TARGET)
        // -------------------------
        Vector3 lookDir = target.position - desiredPos;
        Quaternion desiredRot = lookDir.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(lookDir.normalized, Vector3.up)
            : transform.rotation;

        // -------------------------
        // HARD LOCK CENTER (NO SMOOTHING)
        // -------------------------
        if (hardLockCenter)
        {
            transform.position = desiredPos;
            transform.rotation = desiredRot;
            return;
        }

        // -------------------------
        // SMOOTH MODE
        // -------------------------
        if (_needsSnap)
        {
            transform.position = desiredPos;
            transform.rotation = desiredRot;
            _followVelocity = Vector3.zero;
            _needsSnap = false;
        }
        else
        {
            float smoothTime = Mathf.Max(0.01f, followSmooth);
            transform.position = Vector3.SmoothDamp(
                transform.position,
                desiredPos,
                ref _followVelocity,
                smoothTime
            );

            float maxRotSpeed = Mathf.Max(1f, lookSmooth);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                desiredRot,
                maxRotSpeed * Time.deltaTime
            );
        }
    }

    public void SetTarget(Transform newTarget, bool? snapImmediately = null)
    {
        target = newTarget;

        bool shouldSnap = snapImmediately ?? snapOnTargetChange;
        if (shouldSnap && target != null)
            _needsSnap = true;
    }

    public void SnapToTarget() => _needsSnap = true;

    public void SnapToTargetImmediate()
    {
        if (target == null) return;

        Vector3 cubeForward = target.forward;
        Vector3 basePos =
            target.position
            - cubeForward * followDistance
            + Vector3.up * followHeight;

        transform.position = basePos;

        Vector3 lookDir = target.position - basePos;
        if (lookDir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(lookDir.normalized, Vector3.up);

        _followVelocity = Vector3.zero;
        _needsSnap = false;
    }
}
