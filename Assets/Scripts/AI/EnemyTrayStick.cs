using UnityEngine;

/// <summary>
/// Keeps an enemy "stuck" to a tilted tray, similar to the cube:
/// - Rays toward the tray each frame.
/// - Snaps position to the tray surface + a small offset.
/// - Optionally aligns rotation so enemy up == tray.up.
/// - Optionally constrains the enemy inside a rectangular region
///   in tray-local XZ (so it can't leave the map).
///
/// Attach this to the enemy along with StrokeTrailFollowerAI.
/// </summary>
[DisallowMultipleComponent]
public class EnemyTrayStick : MonoBehaviour
{
    [Header("Tray")]
    [Tooltip("Tray transform (TiltTray). If empty, will try to auto-find one in the scene.")]
    [SerializeField] private Transform tray;

    [Tooltip("Optional layer mask for the tray collider. If left at 0, raycast will hit everything.")]
    [SerializeField] private LayerMask trayMask;

    [Header("Surface Settings")]
    [Tooltip("How far above the tray surface (along tray.up) the enemy should hover.")]
    [SerializeField] private float surfaceOffset = 0.1f;

    [Tooltip("Max ray distance when searching for the tray below/above the enemy.")]
    [SerializeField] private float rayDistance = 5f;

    [Tooltip("Align enemy's up axis with tray.up each frame.")]
    [SerializeField] private bool alignRotationToTray = true;

    [Header("Map Bounds (Tray Local XZ)")]
    [Tooltip("If true, the enemy will be clamped inside a rectangle in tray-local XZ.")]
    [SerializeField] private bool useLocalBounds = true;

    [Tooltip("Min/Max X in tray local space for the allowed region.")]
    [SerializeField] private float minLocalX = -5f;
    [SerializeField] private float maxLocalX =  5f;

    [Tooltip("Min/Max Z in tray local space for the allowed region.")]
    [SerializeField] private float minLocalZ = -5f;
    [SerializeField] private float maxLocalZ =  5f;

    [Header("Debug")]
    [SerializeField] private bool debugRays = false;
    [SerializeField] private bool debugBoundsGizmos = false;

    private void Awake()
    {
        // Auto-find tray if not assigned
        if (!tray)
        {
            TiltTray tilt = FindObjectOfType<TiltTray>();
            if (tilt != null)
            {
                tray = tilt.transform;
            }
        }
    }

    private void LateUpdate()
    {
        if (!tray)
            return;

        Vector3 trayUp = tray.up;

        // Start ray a bit above current position (along tray up),
        // and cast in -trayUp direction toward the surface.
        Vector3 origin = transform.position + trayUp * (rayDistance * 0.5f);
        Vector3 dir = -trayUp;

        if (debugRays)
        {
            Debug.DrawRay(origin, dir * rayDistance, Color.magenta);
        }

        RaycastHit hit;
        bool hitSomething;

        if (trayMask.value != 0)
        {
            hitSomething = Physics.Raycast(origin, dir, out hit, rayDistance, trayMask, QueryTriggerInteraction.Ignore);
        }
        else
        {
            hitSomething = Physics.Raycast(origin, dir, out hit, rayDistance, ~0, QueryTriggerInteraction.Ignore);
        }

        if (!hitSomething)
            return;

        // Snap position to tray surface + offset
        Vector3 targetPos = hit.point + trayUp * surfaceOffset;

        // Clamp inside tray-local bounds if enabled
        if (useLocalBounds)
        {
            targetPos = ClampToTrayLocalBounds(targetPos);
        }

        transform.position = targetPos;

        if (alignRotationToTray)
        {
            AlignRotation(trayUp);
        }
    }

    /// <summary>
    /// Clamp a world-space position into the configured tray-local XZ rectangle.
    /// </summary>
    private Vector3 ClampToTrayLocalBounds(Vector3 worldPos)
    {
        if (!tray)
            return worldPos;

        // Convert to tray local
        Vector3 local = tray.InverseTransformPoint(worldPos);

        // Clamp XZ
        local.x = Mathf.Clamp(local.x, minLocalX, maxLocalX);
        local.z = Mathf.Clamp(local.z, minLocalZ, maxLocalZ);

        // Back to world
        return tray.TransformPoint(local);
    }

    /// <summary>
    /// Align enemy's up axis to tray.up, keeping forward projected onto tray plane.
    /// </summary>
    private void AlignRotation(Vector3 trayUp)
    {
        // Project current forward onto tray plane so we don't flip randomly
        Vector3 fwd = transform.forward;
        Vector3 projectedFwd = Vector3.ProjectOnPlane(fwd, trayUp).normalized;

        if (projectedFwd.sqrMagnitude < 1e-4f)
        {
            // Fall back to tray forward if our own forward is too vertical
            projectedFwd = Vector3.ProjectOnPlane(tray.forward, trayUp).normalized;
        }

        if (projectedFwd.sqrMagnitude > 1e-4f)
        {
            transform.rotation = Quaternion.LookRotation(projectedFwd, trayUp);
        }
        else
        {
            // As a last resort, just set up = trayUp
            transform.up = trayUp;
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!debugBoundsGizmos || !tray || !useLocalBounds)
            return;

        // Draw the local XZ rectangle on the tray
        Vector3 a = tray.TransformPoint(new Vector3(minLocalX, 0f, minLocalZ));
        Vector3 b = tray.TransformPoint(new Vector3(maxLocalX, 0f, minLocalZ));
        Vector3 c = tray.TransformPoint(new Vector3(maxLocalX, 0f, maxLocalZ));
        Vector3 d = tray.TransformPoint(new Vector3(minLocalX, 0f, maxLocalZ));

        Gizmos.color = Color.black;
        Gizmos.DrawLine(a, b);
        Gizmos.DrawLine(b, c);
        Gizmos.DrawLine(c, d);
        Gizmos.DrawLine(d, a);
    }
#endif
}
