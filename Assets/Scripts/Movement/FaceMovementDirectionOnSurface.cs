// FILEPATH: Assets/Scripts/Movement/FaceMovementDirectionOnSurface.cs
using UnityEngine;

/// <summary>
/// Rotates this object so it faces the movement direction of another object,
/// projected onto a surface defined by an "up" vector (e.g. a tilted tray).
///
/// - Put this on the MODEL (child) you want to rotate.
/// - movementSource is usually the root object that actually moves (cube / enemy).
/// - upSource is usually the tray; its .up defines the surface normal.
/// - If not assigned, both are auto-detected:
///     * movementSource = parent transform
///     * upSource = TiltTray from parent or scene
///
/// Uses position delta to infer movement direction, then smooths that direction
/// over time to avoid shaking / jitter.
/// </summary>
[DisallowMultipleComponent]
public class FaceMovementDirectionOnSurface : MonoBehaviour
{
    [Header("Sources")]
    [Tooltip("Object whose movement we track. If null, script will use parent, then self.")]
    [SerializeField] private Transform movementSource;

    [Tooltip("Transform whose 'up' defines the surface normal (e.g. TiltTray). If null, script will auto-find a TiltTray.")]
    [SerializeField] private Transform upSource;

    [Tooltip("If true, will keep trying to auto-find a TiltTray while upSource is null.")]
    [SerializeField] private bool autoFindUpSource = true;

    [Header("Rotation Settings")]
    [Tooltip("Minimum planar speed required to update rotation (prevents jitter).")]
    [SerializeField] private float minSpeedToRotate = 0.02f;

    [Tooltip("How fast the facing direction follows the actual movement direction.\n" +
             "Higher = snappier, lower = smoother.")]
    [SerializeField] private float directionSmoothness = 8f;

    private Vector3 _lastPosWorld;
    private bool _hasLastPos;

    private Vector3 _smoothedDir;     // smoothed forward direction on the surface plane
    private bool _hasSmoothedDir;

    private void Start()
    {
        // Auto-assign movementSource if needed
        if (movementSource == null)
        {
            if (transform.parent != null)
                movementSource = transform.parent;
            else
                movementSource = transform;
        }

        TryFindUpSource();

        if (movementSource != null)
        {
            _lastPosWorld = movementSource.position;
            _hasLastPos = true;
        }
    }

    private void TryFindUpSource()
    {
        if (upSource != null || !autoFindUpSource)
            return;

        // 1) Try parent TiltTray
        TiltTray tray = GetComponentInParent<TiltTray>();
        if (tray != null)
        {
            upSource = tray.transform;
            return;
        }

        // 2) Try any TiltTray in the scene
        tray = FindObjectOfType<TiltTray>();
        if (tray != null)
        {
            upSource = tray.transform;
        }
    }

    private void LateUpdate()
    {
        if (movementSource == null)
            return;

        if (upSource == null && autoFindUpSource)
        {
            // In case tray was spawned later
            TryFindUpSource();
        }

        Vector3 surfaceUp = upSource ? upSource.up : Vector3.up;

        Vector3 currentPos = movementSource.position;

        // First frame with a valid pos – just cache it
        if (!_hasLastPos)
        {
            _lastPosWorld = currentPos;
            _hasLastPos = true;
            return;
        }

        Vector3 delta = currentPos - _lastPosWorld;
        _lastPosWorld = currentPos;

        float dt = Mathf.Max(Time.deltaTime, 1e-6f);

        // Movement projected onto the surface plane
        Vector3 planarDelta = Vector3.ProjectOnPlane(delta, surfaceUp);
        float distance = planarDelta.magnitude;
        if (distance < 1e-6f)
            return;

        float speed = distance / dt;

        if (speed < minSpeedToRotate)
            return;

        // Raw desired direction
        Vector3 desiredDir = planarDelta.normalized;

        // Initialize smoothed direction on first valid movement
        if (!_hasSmoothedDir)
        {
            _smoothedDir = desiredDir;
            _hasSmoothedDir = true;
        }
        else
        {
            // Exponential smoothing of direction to avoid twitching
            float t = 1f - Mathf.Exp(-directionSmoothness * dt);
            _smoothedDir = Vector3.Slerp(_smoothedDir, desiredDir, t);
            // Re-project to keep it perfectly on the plane
            _smoothedDir = Vector3.ProjectOnPlane(_smoothedDir, surfaceUp).normalized;
        }

        if (_smoothedDir.sqrMagnitude < 1e-6f)
            return;

        Quaternion targetRot = Quaternion.LookRotation(_smoothedDir, surfaceUp);

        // Only one smooth step here; most smoothing happens on the direction itself
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRot,
            t: 1f  // we already smoothed direction, so we can go straight to the target rot
        );
    }
}
