using UnityEngine;

namespace JellyGame.GamePlay.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class CubeScaler : MonoBehaviour
    {
        [Header("Scale Settings")]
        [Tooltip("If true, scale X, Y, Z together. If false, scale only Y (height).")]
        [SerializeField] private bool uniformScale = true;

        [Tooltip("The smallest size the cube is allowed to shrink to.")]
        [SerializeField] private float minSize = 0.2f;

        [Tooltip("The largest size the cube is allowed to grow to.")]
        [SerializeField] private float maxSize = 3.0f;

        [Header("Ground Snap")]
        [Tooltip("Layers that represent the tray / map surface the cube should sit on.")]
        [SerializeField] private LayerMask surfaceMask = ~0;

        [Tooltip("A small offset above the surface to prevent z-fighting.")]
        [SerializeField] private float surfaceOffset = 0.001f;

        [Header("Debug")]
        [SerializeField] private bool logChanges = false;
        [SerializeField] private bool debugRay = false;

        private Collider _col;

        void Awake()
        {
            _col = GetComponent<Collider>();
            if (_col == null)
            {
                Debug.LogError("[CubeScaler] Requires a Collider on the same object.", this);
            }
        }

        /// <summary>
        /// Change size by a constant amount:
        /// amount > 0 → increases size
        /// amount < 0 → decreases size
        /// </summary>
        public void ChangeVolumeAdd(float amount)
        {
            if (_col == null) return;

            Vector3 s = transform.localScale;
            float size = uniformScale ? s.x : s.y;

            size += amount;
            ApplySize(size);
        }

        /// <summary>
        /// Change size by percentage:
        /// factor = 1.2 → +20%
        /// factor = 0.7 → -30% (scale to 70% of current size)
        /// </summary>
        public void ChangeVolumeMultiply(float factor)
        {
            if (_col == null) return;

            Vector3 s = transform.localScale;
            float size = uniformScale ? s.x : s.y;

            size *= factor;
            ApplySize(size);
        }

        /// <summary>
        /// Clamps the size, applies the scale change, and re-snaps the cube to the ground.
        /// </summary>
        private void ApplySize(float size)
        {
            size = Mathf.Clamp(size, minSize, maxSize);

            Vector3 newScale = transform.localScale;
            if (uniformScale)
            {
                newScale = new Vector3(size, size, size);
            }
            else
            {
                newScale.y = size;
            }

            transform.localScale = newScale;

            // After scaling, snap the cube back to the surface below it
            SnapToSurface();

            if (logChanges)
            {
                Debug.Log($"[CubeScaler] New scale = {transform.localScale}", this);
            }
        }

        /// <summary>
        /// Casts a ray downward (using gravity direction) and positions the cube
        /// so that its bottom sits exactly on the surface.
        /// </summary>
        private void SnapToSurface()
        {
            if (_col == null) return;
            if (Physics.gravity.sqrMagnitude < 1e-6f) return; // no gravity = no downward direction

            Vector3 down = Physics.gravity.normalized;
            Bounds b = _col.bounds;

            // Cast a ray from the cube downward
            Vector3 origin = b.center;
            float rayLength = b.extents.magnitude + 1f;

            if (debugRay)
            {
                Debug.DrawRay(origin, down * rayLength, Color.cyan, 0.2f);
            }

            if (Physics.Raycast(origin, down, out RaycastHit hit, rayLength, surfaceMask, QueryTriggerInteraction.Ignore))
            {
                // Compute half-height of the collider along the gravity direction
                Vector3 absDown = new Vector3(Mathf.Abs(down.x), Mathf.Abs(down.y), Mathf.Abs(down.z));
                float halfHeight = Vector3.Dot(absDown, b.extents);

                // We want: bottom = hit.point + small offset
                // so: center = bottom + halfHeight * (-down)
                Vector3 newCenter = hit.point - down * (halfHeight + surfaceOffset);
                transform.position = newCenter;
            }
        }
    }
}
