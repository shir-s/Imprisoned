// FILEPATH: Assets/Scripts/UI/RadialDonutSliceUI.cs
using UnityEngine;

namespace JellyGame.UI
{
    /// <summary>
    /// Controls a UI material that draws a radial "pizza slice" fill with an inner hole (donut).
    /// Works with the shader: "UI/RadialDonutSlice".
    /// </summary>
    [DisallowMultipleComponent]
    public class RadialDonutSliceUI : MonoBehaviour
    {
        [Header("Renderer")]
        [SerializeField] private UnityEngine.UI.Graphic targetGraphic;

        [Header("Slice Settings")]
        [Tooltip("How many total slices make a full circle (usually equals requiredDeaths).")]
        [SerializeField] private int totalSlices = 3;

        [Tooltip("Start angle in degrees (0 = right, 90 = up).")]
        [SerializeField] private float startAngleDegrees = 90f;

        [Tooltip("If true, fills clockwise. If false, fills counter-clockwise.")]
        [SerializeField] private bool clockwise = true;

        [Header("Donut Hole")]
        [Tooltip("Inner radius of the donut hole (0..0.49). 0 = no hole.")]
        [Range(0f, 0.49f)]
        [SerializeField] private float innerRadius = 0.2f;

        private static readonly int FillProp = Shader.PropertyToID("_Fill");
        private static readonly int InnerRadiusProp = Shader.PropertyToID("_InnerRadius");
        private static readonly int StartAngleProp = Shader.PropertyToID("_StartAngleDeg");
        private static readonly int ClockwiseProp = Shader.PropertyToID("_Clockwise");

        private Material _instancedMat;

        private void Awake()
        {
            if (targetGraphic == null)
                targetGraphic = GetComponent<UnityEngine.UI.Graphic>();

            EnsureMaterialInstance();
            ApplyStaticParams();
        }

        private void OnDestroy()
        {
            if (_instancedMat != null)
            {
                Destroy(_instancedMat);
                _instancedMat = null;
            }
        }

        public void SetTotalSlices(int slices)
        {
            totalSlices = Mathf.Max(1, slices);
        }

        public void SetInnerRadius(float r01)
        {
            innerRadius = Mathf.Clamp(r01, 0f, 0.49f);
            EnsureMaterialInstance();
            _instancedMat.SetFloat(InnerRadiusProp, innerRadius);
        }

        public void SetSlicesFilled(int filledSlices)
        {
            EnsureMaterialInstance();
            ApplyStaticParams();

            int total = Mathf.Max(1, totalSlices);
            int filled = Mathf.Clamp(filledSlices, 0, total);

            float fill01 = (float)filled / total;
            _instancedMat.SetFloat(FillProp, fill01);
        }

        public void SetProgress01(float fill01)
        {
            EnsureMaterialInstance();
            ApplyStaticParams();
            _instancedMat.SetFloat(FillProp, Mathf.Clamp01(fill01));
        }

        private void ApplyStaticParams()
        {
            if (_instancedMat == null) return;

            _instancedMat.SetFloat(InnerRadiusProp, innerRadius);
            _instancedMat.SetFloat(StartAngleProp, startAngleDegrees);
            _instancedMat.SetFloat(ClockwiseProp, clockwise ? 1f : 0f);
        }

        private void EnsureMaterialInstance()
        {
            if (targetGraphic == null)
                return;

            if (_instancedMat != null)
                return;

            // IMPORTANT: We need an instance so changing one UI doesn't change all of them.
            Material src = targetGraphic.material;
            if (src == null)
                return;

            _instancedMat = new Material(src);
            targetGraphic.material = _instancedMat;
        }
    }
}
