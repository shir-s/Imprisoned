// FILEPATH: Assets/Scripts/Movement/KinematicCollisionResolver.cs
using UnityEngine;

/// <summary>
/// Prevents a kinematic object (like the drawing cube) from passing through
/// other kinematic colliders by manually resolving overlaps.
/// Works like a "character controller pushback", but for kinematic tray movement.
/// </summary>
[DisallowMultipleComponent]
public class BoxKinematicCollisionResolver : MonoBehaviour
{
    [SerializeField] private LayerMask collisionMask = ~0;

    private Collider _myCollider;

    void Awake()
    {
        _myCollider = GetComponent<Collider>();
    }

    void FixedUpdate()
    {
        // Find all overlaps
        Collider[] hits = Physics.OverlapBox(
            _myCollider.bounds.center,
            _myCollider.bounds.extents,
            transform.rotation,
            collisionMask,
            QueryTriggerInteraction.Ignore
        );

        foreach (var hit in hits)
        {
            if (hit == _myCollider)
                continue;

            // Compute direction to push our cube out
            if (Physics.ComputePenetration(
                    _myCollider, transform.position, transform.rotation,
                    hit, hit.transform.position, hit.transform.rotation,
                    out Vector3 dir, out float distance))
            {
                // Move cube out of penetration
                Vector3 push = dir * distance;
                transform.position += push;
            }
        }
    }
}