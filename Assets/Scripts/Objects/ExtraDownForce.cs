// FILEPATH: Assets/Scripts/PhysicsDrawing/ExtraDownForce.cs
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class ExtraDownForce : MonoBehaviour
{
    [SerializeField] private float extraGravity = 20f;

    [Tooltip("Maximum distance to search for a surface under the object.")]
    [SerializeField] private float rayDistance = 0.5f;

    [Tooltip("Layers considered as 'ground' for extra pressing force. Leave as Everything to use all colliders.")]
    [SerializeField] private LayerMask groundMask = ~0;

    private Rigidbody _rb;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    private void FixedUpdate()
    {
        // Default direction = world-down (same as Physics.gravity)
        Vector3 forceDir = Physics.gravity.normalized;

        // Try to find the surface under the cube and push along its normal
        if (Physics.Raycast(transform.position, -Physics.gravity.normalized, out RaycastHit hit, rayDistance, groundMask, QueryTriggerInteraction.Ignore))
        {
            // Push into the surface (opposite of the surface normal)
            forceDir = -hit.normal.normalized;
        }

        _rb.AddForce(forceDir * extraGravity, ForceMode.Acceleration);
    }
}

/*[RequireComponent(typeof(Rigidbody))]
public class ExtraDownForce : MonoBehaviour
{
    [SerializeField] float extraGravity = 20f; // tweak

    Rigidbody _rb;

    void Awake() => _rb = GetComponent<Rigidbody>();

    void FixedUpdate()
    {
        // Extra gravity straight down
        _rb.AddForce(Physics.gravity.normalized * extraGravity, ForceMode.Acceleration);
    }
}*/