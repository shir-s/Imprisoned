using UnityEngine;

/// <summary>
/// Smooth, slime-like keyboard movement on the XZ plane.
/// Does NOT force a fixed Y height.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class CubePlayerSlimeController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float maxSpeed = 8f;
    [SerializeField] private float acceleration = 25f;
    [SerializeField] private float deceleration = 20f;

    [Header("Bounds (world XZ)")]
    [SerializeField] private bool useBounds = false;
    [SerializeField] private Vector2 boundsCenter = Vector2.zero;
    [SerializeField] private Vector2 boundsHalfSize = new Vector2(50f, 50f);

    private Vector3 _velocity = Vector3.zero;

    private void Update()
    {
        float dt = Time.deltaTime;

        // 1) Input
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical   = Input.GetAxisRaw("Vertical");

        Vector3 inputDir = new Vector3(horizontal, 0f, vertical).normalized;

        // 2) Velocity update
        if (inputDir.sqrMagnitude > 0f)
        {
            Vector3 desiredVel = inputDir * maxSpeed;
            _velocity = Vector3.MoveTowards(_velocity, desiredVel, acceleration * dt);
        }
        else
        {
            _velocity = Vector3.MoveTowards(_velocity, Vector3.zero, deceleration * dt);
        }

        // 3) Position update
        Vector3 pos = transform.position;
        pos += _velocity * dt;

        // 4) Optional bounds clamp (X,Z only)
        if (useBounds)
        {
            float minX = boundsCenter.x - boundsHalfSize.x;
            float maxX = boundsCenter.x + boundsHalfSize.x;
            float minZ = boundsCenter.y - boundsHalfSize.y;
            float maxZ = boundsCenter.y + boundsHalfSize.y;

            pos.x = Mathf.Clamp(pos.x, minX, maxX);
            pos.z = Mathf.Clamp(pos.z, minZ, maxZ);
        }

        transform.position = pos;
    }
}