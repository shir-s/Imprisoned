using UnityEngine;
namespace Toy2
{
    [RequireComponent(typeof(Transform))]
    public class StretchableAxis : MonoBehaviour
    {
        public enum Axis { X, Z }

        [Header("Setup")]
        public BoxCollider parentBounds;
        public Axis axis = Axis.Z;
        public float padding = 0f;

        [Header("Debug (read-only)")]
        [SerializeField] private float anchorEdgeLocal;
        [SerializeField] private float maxScale;

        Transform tr;

        public bool IsComplete => tr != null && Mathf.Abs(GetScale() - maxScale) < 1e-4f;
        public float Completion01 => Mathf.Approximately(maxScale, 0f) ? 1f : Mathf.Clamp01(GetScale() / maxScale);

        void Reset()
        {
            if (transform.parent != null && transform.parent.TryGetComponent(out BoxCollider bc))
                parentBounds = bc;
        }

        void Awake()
        {
            tr = transform;

            if (tr.parent == null || parentBounds == null)
            {
                Debug.LogError("StretchableAxis: Needs a parent with a BoxCollider assigned to parentBounds.");
                enabled = false;
                return;
            }

            float pCenter = axis == Axis.X ? parentBounds.center.x : parentBounds.center.z;
            float pSize   = axis == Axis.X ? parentBounds.size.x   : parentBounds.size.z;
            float pMin = pCenter - 0.5f * pSize + padding;
            float pMax = pCenter + 0.5f * pSize - padding;

            float pos = GetPosition();
            float scale = GetScale();
            anchorEdgeLocal = pos - 0.5f * scale;

            // anchorEdgeLocal = pMin; // (optional) to snap start to parent's min edge
            anchorEdgeLocal = Mathf.Max(anchorEdgeLocal, pMin);

            maxScale = Mathf.Max(0.0001f, pMax - anchorEdgeLocal);

            float clamped = Mathf.Min(scale, maxScale);
            SetScale(clamped);
            SetPosition(anchorEdgeLocal + 0.5f * clamped);
        }

        public void StretchStep(float amount)
        {
            if (!enabled || tr == null || amount <= 0f) return;

            float newScale = Mathf.Min(GetScale() + amount, maxScale);
            if (Mathf.Approximately(newScale, GetScale())) return;

            SetScale(newScale);
            SetPosition(anchorEdgeLocal + 0.5f * newScale);
        }

        float GetScale() => axis == Axis.X ? tr.localScale.x : tr.localScale.z;
        void SetScale(float v)
        {
            var s = tr.localScale;
            if (axis == Axis.X) s.x = v; else s.z = v;
            tr.localScale = s;
        }

        float GetPosition() => axis == Axis.X ? tr.localPosition.x : tr.localPosition.z;
        void SetPosition(float v)
        {
            var p = tr.localPosition;
            if (axis == Axis.X) p.x = v; else p.z = v;
            tr.localPosition = p;
        }

    #if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
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