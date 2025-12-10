using UnityEngine;

namespace JellyGame.GamePlay.Player
{
    /// <summary>
    /// Smooth, slime-like keyboard movement on the XZ plane.
    /// UPDATED: Now works correctly with SurfaceStickerController
    /// - Only controls XZ movement
    /// - Preserves Y position (handled by SurfaceStickerController)
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class CubePlayerKeyboardController : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float maxSpeed = 8f;
        [SerializeField] private float acceleration = 25f;
        [SerializeField] private float deceleration = 20f;

        [Header("Bounds (world XZ)")]
        [SerializeField] private bool useBounds = false;
        [SerializeField] private Vector2 boundsCenter = Vector2.zero;
        [SerializeField] private Vector2 boundsHalfSize = new Vector2(50f, 50f);

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private Vector3 _velocity = Vector3.zero;
    
        private float _sizeFactor = 1f;
        
        /// <summary>
        /// Called by CubeStackManager whenever the cube grows.
        /// sizeFactor = approx local scale (x or y).
        /// </summary>
        public void SetSizeFactor(float sizeFactor)
        {
            _sizeFactor = Mathf.Max(0.1f, sizeFactor);
        }
        
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

            // 3) Intended movement - ONLY IN XZ PLANE
            Vector3 currentPos = transform.position;
            Vector3 pos = currentPos; // Start with current position (including Y)
            Vector3 delta = _velocity * dt;

            if (delta.sqrMagnitude > 0.0001f)
            {
                Vector3 dir  = delta.normalized;
                float   dist = delta.magnitude;

                float castDistance = dist + 0.1f;
                float radius       = 0.49f * _sizeFactor;

                if (Physics.SphereCast(pos, radius, dir, out RaycastHit hit, castDistance))
                {
                    // Slide along the wall instead of stopping completely
                    Vector3 normal   = hit.normal;
                    Vector3 slideVel = Vector3.ProjectOnPlane(_velocity, normal); // remove into-wall component

                    _velocity = slideVel;
                    delta = _velocity * dt;
                    
                    // Only update XZ
                    pos.x += delta.x;
                    pos.z += delta.z;
                }
                else
                {
                    // No hit: free movement (only XZ)
                    pos.x += delta.x;
                    pos.z += delta.z;
                }
            }

            // 4) Optional bounds clamp
            if (useBounds)
            {
                float minX = boundsCenter.x - boundsHalfSize.x;
                float maxX = boundsCenter.x + boundsHalfSize.x;
                float minZ = boundsCenter.y - boundsHalfSize.y;
                float maxZ = boundsCenter.y + boundsHalfSize.y;

                pos.x = Mathf.Clamp(pos.x, minX, maxX);
                pos.z = Mathf.Clamp(pos.z, minZ, maxZ);
            }

            // CRITICAL FIX: Only update XZ, preserve current Y
            // SurfaceStickerController will handle Y in LateUpdate
            transform.position = new Vector3(pos.x, currentPos.y, pos.z);
            
            if (debugLogs && delta.sqrMagnitude > 0.0001f)
            {
                Debug.Log($"[CubePlayerKeyboard] Moved to XZ: ({pos.x:F2}, {pos.z:F2}), Y preserved: {currentPos.y:F2}");
            }
        }

        /// <summary>
        /// Get current movement velocity (for other systems)
        /// </summary>
        public Vector3 GetVelocity()
        {
            return _velocity;
        }

        /// <summary>
        /// Check if player is currently moving
        /// </summary>
        public bool IsMoving()
        {
            return _velocity.sqrMagnitude > 0.01f;
        }
    }
}