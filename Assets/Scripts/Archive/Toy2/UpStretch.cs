namespace Toy2
{
    using UnityEngine;

    [RequireComponent(typeof(Transform))]
    public class UpStretch : MonoBehaviour
    {
        [Header("Setup")]
        [Tooltip("The BoxCollider of the parent object that defines the maximum stretching bounds")]
        public BoxCollider parentBounds;
        [Tooltip("Key to hold for stretching forward (Z axis)")]
        public KeyCode stretchKey = KeyCode.UpArrow;

        [Header("Behavior")]
        [Tooltip("Speed at which the object grows along the Z axis (units per second)")]
        public float growSpeed = 2f;
        [Tooltip("Small padding from the parent cube walls (on Z axis)")]
        public float padding = 0f;

        // Internal values
        float backEdgeLocal;   // the child's back edge in parent local space
        float maxScaleZ;       // max allowed local scale on Z
        Transform tr;

        void Reset()
        {
            // Try to auto-detect parent BoxCollider
            if (transform.parent != null && transform.parent.TryGetComponent(out BoxCollider bc))
                parentBounds = bc;
        }

        void Awake()
        {
            tr = transform;

            if (tr.parent == null || parentBounds == null)
            {
                Debug.LogError("StretchOnHold: Needs a parent with a BoxCollider assigned to parentBounds.");
                enabled = false;
                return;
            }

            // Get parent bounds (Z axis)
            float parentBack = parentBounds.center.z - parentBounds.size.z * 0.5f + padding;
            float parentFront = parentBounds.center.z + parentBounds.size.z * 0.5f - padding;

            // Calculate local back edge (relative to parent)
            backEdgeLocal = tr.localPosition.z - 0.5f * tr.localScale.z;

            // Optional: snap to parent's back boundary
            // backEdgeLocal = parentBack;

            backEdgeLocal = Mathf.Max(backEdgeLocal, parentBack);

            // Compute maximum scale until front edge reaches parent's front edge
            maxScaleZ = Mathf.Max(0.0001f, parentFront - backEdgeLocal);

            // Initialize with fixed back edge
            float initialScaleZ = Mathf.Min(tr.localScale.z, maxScaleZ);
            tr.localScale = new Vector3(tr.localScale.x, tr.localScale.y, initialScaleZ);
            tr.localPosition = new Vector3(tr.localPosition.x, tr.localPosition.y, backEdgeLocal + 0.5f * tr.localScale.z);
        }

        void Update()
        {
            if (!enabled) return;

            // Stretch forward (along Z) while holding key
            if (Input.GetKey(stretchKey))
            {
                float newScaleZ = tr.localScale.z + growSpeed * Time.deltaTime;
                newScaleZ = Mathf.Min(newScaleZ, maxScaleZ);

                tr.localScale = new Vector3(tr.localScale.x, tr.localScale.y, newScaleZ);

                // Keep back edge fixed
                tr.localPosition = new Vector3(tr.localPosition.x, tr.localPosition.y, backEdgeLocal + 0.5f * newScaleZ);
            }
        }

    #if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            // Visualize the parent bounds for debugging
            if (parentBounds != null && transform.parent != null)
            {
                Gizmos.color = Color.green;
                var p = transform.parent;
                Vector3 center = p.TransformPoint(parentBounds.center);
                Vector3 size = Vector3.Scale(parentBounds.size, p.lossyScale);
                Gizmos.DrawWireCube(center, size);
            }
        }
    #endif
    }

}