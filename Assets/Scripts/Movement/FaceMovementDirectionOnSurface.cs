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
    [Tooltip("Minimum speed required to update rotation (prevents jitter).")]
    [SerializeField] private float minSpeedToRotate = 0.05f;

    [Tooltip("How fast rotation interpolates toward the movement direction.")]
    [SerializeField] private float rotationSmoothness = 10f;

    private Vector3 _lastPosWorld;
    private bool _initialized;

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

        _lastPosWorld = movementSource.position;
        _initialized = true;
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
        if (!_initialized || movementSource == null)
            return;

        if (upSource == null && autoFindUpSource)
        {
            // In case tray was spawned later
            TryFindUpSource();
        }

        Vector3 currentPos = movementSource.position;
        Vector3 delta = currentPos - _lastPosWorld;

        // Choose surface normal (tray up or world up)
        Vector3 surfaceUp = upSource ? upSource.up : Vector3.up;

        // Project movement onto the surface plane so we don't tilt into the tray
        Vector3 dirOnSurface = Vector3.ProjectOnPlane(delta, surfaceUp);

        float dt = Mathf.Max(Time.deltaTime, 1e-6f);
        float speed = dirOnSurface.magnitude / dt;

        if (speed > minSpeedToRotate)
        {
            Vector3 forward = dirOnSurface.normalized;
            Quaternion targetRot = Quaternion.LookRotation(forward, surfaceUp);

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRot,
                rotationSmoothness * Time.deltaTime
            );
        }

        _lastPosWorld = currentPos;
    }
}
