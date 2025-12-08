using UnityEngine;

namespace Toy2
{
    [RequireComponent(typeof(Transform))]
    public class RightStretch : MonoBehaviour
    {
        [Header("Setup")]
        [Tooltip("The BoxCollider of the parent object that defines the maximum stretching bounds")]
        public BoxCollider parentBounds;              
        [Tooltip("Key to hold for stretching")]
        public KeyCode stretchKey = KeyCode.RightArrow;

        [Header("Behavior")]
        [Tooltip("Speed at which the object grows along the X axis (units per second)")]
        public float growSpeed = 2f;
        [Tooltip("Small padding from the parent cube walls (on X axis)")]
        public float padding = 0f;

        // Internal values
        float leftEdgeLocal;     // Left edge of the child relative to parent (in parent's local space)
        float maxScaleX;         // Maximum allowed local scale on X
        Transform tr;
        Transform parentTr;

        void Reset()
        {
            // Auto-detect parent BoxCollider if available
            if (transform.parent != null && transform.parent.TryGetComponent(out BoxCollider bc))
                parentBounds = bc;
        }

        void Awake()
        {
            tr = transform;
            parentTr = tr.parent;

            if (parentTr == null || parentBounds == null)
            {
                Debug.LogError("StretchOnHold: Needs a parent with a BoxCollider assigned to parentBounds.");
                enabled = false;
                return;
            }

            // Parent bounds in local space
            float parentLeft  = parentBounds.center.x - parentBounds.size.x * 0.5f + padding;
            float parentRight = parentBounds.center.x + parentBounds.size.x * 0.5f - padding;

            // Calculate the local left edge of the child (relative to parent)
            leftEdgeLocal = tr.localPosition.x - 0.5f * tr.localScale.x;

            // Optional: lock the left edge exactly to parent's left boundary
            // leftEdgeLocal = parentLeft;

            leftEdgeLocal = Mathf.Max(leftEdgeLocal, parentLeft);

            // Maximum scale: when the right edge of the child reaches the parent's right edge
            maxScaleX = Mathf.Max(0.0001f, parentRight - leftEdgeLocal);

            // Initialize scale and position so the left edge stays fixed
            float initialScaleX = Mathf.Min(tr.localScale.x, maxScaleX);
            tr.localScale = new Vector3(initialScaleX, tr.localScale.y, tr.localScale.z);
            tr.localPosition = new Vector3(leftEdgeLocal + 0.5f * tr.localScale.x, tr.localPosition.y, tr.localPosition.z);
        }

        void Update()
        {
            if (!enabled) return;

            // Stretch while key is held
            if (Input.GetKey(stretchKey))
            {
                float newScaleX = tr.localScale.x + growSpeed * Time.deltaTime;
                newScaleX = Mathf.Min(newScaleX, maxScaleX);

                tr.localScale = new Vector3(newScaleX, tr.localScale.y, tr.localScale.z);

                // Keep left edge fixed
                tr.localPosition = new Vector3(leftEdgeLocal + 0.5f * newScaleX, tr.localPosition.y, tr.localPosition.z);
            }
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            // Visualize parent bounds in editor
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
