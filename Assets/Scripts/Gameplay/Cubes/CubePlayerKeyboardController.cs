using UnityEngine;

/// <summary>
/// Keyboard-controlled kinematic cube:
/// - Requires Rigidbody set to kinematic (done in Awake).
/// - Moves on the XZ plane using arrow keys / WASD.
/// - Keeps a fixed Y height so it "sits" on the board.
/// - Optional world-space bounds.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class CubePlayerKeyboardController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;

    [Header("Board / Height")]
    [SerializeField] private float fixedY = 0.5f;

    [Header("Bounds (world XZ)")]
    [SerializeField] private bool useBounds = false;
    [SerializeField] private Vector2 boundsCenter = Vector2.zero;
    [SerializeField] private Vector2 boundsHalfSize = new Vector2(50f, 50f);

    private Rigidbody _rb;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.isKinematic = true;
        _rb.useGravity = false;
    }

    private void Update()
    {
        // Read input (arrow keys / WASD)
        float horizontal = Input.GetAxisRaw("Horizontal"); // left/right
        float vertical   = Input.GetAxisRaw("Vertical");   // up/down

        Vector3 direction = new Vector3(horizontal, 0f, vertical).normalized;

        // Current position
        Vector3 pos = transform.position;

        if (direction.sqrMagnitude > 0f)
        {
            pos += direction * moveSpeed * Time.deltaTime;
        }

        // Keep the cube at a fixed height above the board
        pos.y = fixedY;

        // Optional bounds clamp
        if (useBounds)
        {
            float minX = boundsCenter.x - boundsHalfSize.x;
            float maxX = boundsCenter.x + boundsHalfSize.x;
            float minZ = boundsCenter.y - boundsHalfSize.y;
            float maxZ = boundsCenter.y + boundsHalfSize.y;

            pos.x = Mathf.Clamp(pos.x, minX, maxX);
            pos.z = Mathf.Clamp(pos.z, minZ, maxZ);
        }

        // Move kinematically
        _rb.MovePosition(pos);
    }
}
