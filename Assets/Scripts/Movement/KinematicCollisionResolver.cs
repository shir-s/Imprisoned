// FILEPATH: Assets/Scripts/Movement/KinematicCollisionResolver.cs
using UnityEngine;

/// <summary>
/// Prevents a kinematic object (like the player cube) from passing through
/// other colliders by manually resolving overlaps using ComputePenetration.
///
/// Usage pattern:
/// - Attach this to the PLAYER / ENEMY / MOVING ACTOR.
/// - Do NOT attach this to static obstacles (trees, walls).
/// - For pushable objects (boxes), put them on a "Pushable" layer and set
///   pushableMask to include that layer. The resolver will try to push them
///   instead of pushing the player.
/// - Works best when this object is driven by KinematicTrayRider:
///   we then push via KinematicTrayRider.ApplyWorldPush instead of
///   teleporting the transform, so there is no "jumping".
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class KinematicCollisionResolver : MonoBehaviour
{
    [Tooltip("Which layers this object should collide with (be pushed out of / interact with).")]
    [SerializeField] private LayerMask collisionMask = ~0;

    [Header("Pushback (self)")]
    [Tooltip("How much to shrink the bounds when checking overlaps (to avoid tiny jitter).")]
    [SerializeField] private float skinWidth = 0.01f;

    [Tooltip("Maximum distance this object can be pushed in one FixedUpdate.\n" +
             "Set to something like 0.2 - 0.5 to avoid big teleports.\n" +
             "Use 0 or negative to disable clamping (full depenetration in one frame).")]
    [SerializeField] private float maxPushPerStep = 0.3f;

    [Header("Pushing Others (boxes, etc.)")]
    [Tooltip("If true, this object will try to push other kinematic objects instead of itself, " +
             "when those objects are on the pushableMask layers.")]
    [SerializeField] private bool allowPushingOthers = true;

    [Tooltip("Layers considered pushable (boxes, movable blocks, etc.).")]
    [SerializeField] private LayerMask pushableMask;

    [Tooltip("Maximum distance we will push OTHER objects per FixedUpdate.\n" +
             "If <= 0, uses maxPushPerStep.")]
    [SerializeField] private float maxPushOtherPerStep = 0.3f;

    [Header("Tray plane (optional)")]
    [Tooltip("If true, the pushback vector is projected onto the tray plane (no vertical jumps).")]
    [SerializeField] private bool projectPushOnTrayPlane = true;

    [Tooltip("If true and tray is not assigned, the resolver will try to auto-find a TiltTray.")]
    [SerializeField] private bool autoFindTray = true;

    [Tooltip("Tray transform that defines the up direction of the surface. " +
             "If left null and auto-find is enabled, this will be looked up in parents, then in the scene.")]
    [SerializeField] private Transform tray;

    private Collider _myCollider;
    private KinematicTrayRider _rider;

    private void Awake()
    {
        _myCollider = GetComponent<Collider>();
        _rider = GetComponent<KinematicTrayRider>();

        if (projectPushOnTrayPlane && autoFindTray && tray == null)
        {
            TryFindTray();
        }
    }

    private void FixedUpdate()
    {
        if (_myCollider == null)
            return;

        if (projectPushOnTrayPlane && autoFindTray && tray == null)
        {
            // In case tray was spawned later or script order changed.
            TryFindTray();
        }

        Bounds b = _myCollider.bounds;

        // Shrink bounds slightly to reduce constant tiny overlaps.
        Vector3 halfExtents = b.extents - Vector3.one * skinWidth;
        if (halfExtents.x < 0f) halfExtents.x = 0f;
        if (halfExtents.y < 0f) halfExtents.y = 0f;
        if (halfExtents.z < 0f) halfExtents.z = 0f;

        Collider[] hits = Physics.OverlapBox(
            b.center,
            halfExtents,
            transform.rotation,
            collisionMask,
            QueryTriggerInteraction.Ignore
        );

        Vector3 totalSelfPush = Vector3.zero;

        foreach (var hit in hits)
        {
            if (hit == null || hit == _myCollider)
                continue;

            if (!Physics.ComputePenetration(
                    _myCollider, transform.position, transform.rotation,
                    hit, hit.transform.position, hit.transform.rotation,
                    out Vector3 dir, out float distance))
            {
                continue;
            }

            if (distance <= 0f)
                continue;

            // dir points from OTHER to THIS collider.
            // To separate ourselves, we would move by dir * distance.
            Vector3 separation = dir * distance;

            bool isPushable =
                allowPushingOthers &&
                ((pushableMask.value & (1 << hit.gameObject.layer)) != 0);

            if (isPushable)
            {
                // Prefer to push the OTHER object instead of ourselves.
                Vector3 pushOther = -separation; // move other away from us

                // Project onto tray plane if requested
                if (projectPushOnTrayPlane && tray != null)
                {
                    pushOther = Vector3.ProjectOnPlane(pushOther, tray.up);
                }

                if (pushOther.sqrMagnitude > 0f)
                {
                    float maxOther = (maxPushOtherPerStep > 0f) ? maxPushOtherPerStep : maxPushPerStep;
                    if (maxOther > 0f)
                    {
                        float magOther = pushOther.magnitude;
                        if (magOther > maxOther)
                        {
                            pushOther *= maxOther / magOther;
                        }
                    }

                    ApplyPushToOther(hit.transform, pushOther);
                }

                // We do NOT add this to totalSelfPush.
                // This is what makes the player keep moving while the box slides.
            }
            else
            {
                // Non-pushable obstacle (trees, walls, etc.) -> push ourselves
                totalSelfPush += separation;
            }
        }

        if (totalSelfPush.sqrMagnitude <= 0f)
            return;

        // Project onto tray plane if requested, to avoid vertical fighting.
        if (projectPushOnTrayPlane && tray != null)
        {
            totalSelfPush = Vector3.ProjectOnPlane(totalSelfPush, tray.up);
        }

        if (totalSelfPush.sqrMagnitude <= 0f)
            return;

        float mag = totalSelfPush.magnitude;

        // Clamp how far we move in one physics step → smoother stop
        if (maxPushPerStep > 0f && mag > maxPushPerStep)
        {
            totalSelfPush *= maxPushPerStep / mag;
        }

        // If this object is driven by KinematicTrayRider, push via rider
        // so that internal _localPos is updated. That removes "jumping".
        if (_rider != null)
        {
            // Prevent further movement INTO static obstacle
            _rider.BlockVelocityInDirection(totalSelfPush);

            // Apply actual depenetration push
            _rider.ApplyWorldPush(totalSelfPush);
        }
        else
        {
            transform.position += totalSelfPush;
        }
    }

    private void ApplyPushToOther(Transform other, Vector3 push)
    {
        if (push.sqrMagnitude <= 0f || other == null)
            return;

        // 1) If other is a tray rider, use its API
        var otherRider = other.GetComponent<KinematicTrayRider>();
        if (otherRider != null)
        {
            otherRider.ApplyWorldPush(push);
            return;
        }

        // 2) If it has a rigidbody, move it kinematically
        var rb = other.GetComponent<Rigidbody>();
        if (rb != null && rb.isKinematic)
        {
            rb.MovePosition(rb.position + push);
            return;
        }

        // 3) Fallback: just move the transform
        other.position += push;
    }

    private void TryFindTray()
    {
        if (!autoFindTray || tray != null)
            return;

        // 1) Try parent TiltTray
        TiltTray parentTray = GetComponentInParent<TiltTray>();
        if (parentTray != null)
        {
            tray = parentTray.transform;
            return;
        }

        // 2) Try any TiltTray in the scene
        TiltTray tt = Object.FindObjectOfType<TiltTray>();
        if (tt != null)
        {
            tray = tt.transform;
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (_myCollider == null)
            _myCollider = GetComponent<Collider>();

        if (_myCollider == null)
            return;

        Bounds b = _myCollider.bounds;
        Vector3 halfExtents = b.extents - Vector3.one * skinWidth;
        if (halfExtents.x < 0f) halfExtents.x = 0f;
        if (halfExtents.y < 0f) halfExtents.y = 0f;
        if (halfExtents.z < 0f) halfExtents.z = 0f;

        Gizmos.color = new Color(1f, 0.3f, 0.1f, 0.25f);
        Gizmos.matrix = Matrix4x4.TRS(b.center, transform.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, halfExtents * 2f);
    }
#endif
}
