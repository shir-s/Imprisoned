using UnityEngine;

/// <summary>
/// FIXED VERSION: Keeps an enemy "stuck" to a tilted tray BUT respects movement
/// from behaviors (like AttackBehavior).
/// 
/// The fix: Instead of raycasting from the current position (which may have just been
/// updated by AttackBehavior), we first let the behavior move the enemy, THEN we raycast
/// from the NEW position to stick it to the tray surface at that XZ location.
/// 
/// This way:
/// - AttackBehavior (or any behavior) moves the enemy in XZ
/// - EnemyTrayStick adjusts the Y position to keep it on the tray surface
/// - The enemy can move freely while staying stuck to the tray
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

    /// <summary>
    /// CRITICAL FIX: We now respect the position that was set by behaviors in Update().
    /// We only adjust Y and clamp to bounds, we don't override the XZ movement.
    /// </summary>
    private void LateUpdate()
    {
        if (!tray)
            return;

        Vector3 trayUp = tray.up;
        
        // IMPORTANT: Use CURRENT position (which may have just been updated by AttackBehavior)
        // as the starting point for our raycast, not some previous position
        Vector3 currentPos = transform.position;

        // Clamp XZ to bounds FIRST (before raycasting) so we don't raycast outside valid area
        Vector3 clampedPos = currentPos;
        if (useLocalBounds)
        {
            clampedPos = ClampToTrayLocalBounds(currentPos);
        }

        // Now raycast from ABOVE this clamped XZ position to find tray surface
        Vector3 origin = clampedPos + trayUp * (rayDistance * 0.5f);
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
        {
            // No tray found - just clamp to bounds and keep current Y
            if (useLocalBounds)
            {
                Vector3 bounded = ClampToTrayLocalBounds(currentPos);
                bounded.y = currentPos.y;
                transform.position = bounded;
            }
            return;
        }

        // Snap to tray surface at the clamped XZ location
        Vector3 targetPos = hit.point + trayUp * surfaceOffset;

        // Final position respects the XZ movement from behaviors, only adjusts Y
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