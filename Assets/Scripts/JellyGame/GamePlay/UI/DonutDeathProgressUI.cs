// FILEPATH: Assets/Scripts/UI/DonutDeathProgressUI.cs
using UnityEngine;
using UnityEngine.UI;

namespace JellyGame.UI
{
    /// <summary>
    /// Two Images with the same sprite:
    /// - Center image shows only the inner disk (always visible)
    /// - Ring image shows only the outer ring, and fills in slices per kills
    /// Uses shader: UI/RadialSpriteDonut
    /// </summary>
    [DisallowMultipleComponent]
    public class DonutDeathProgressUI : MonoBehaviour
    {
        [Header("Images (same sprite recommended)")]
        [SerializeField] private Image centerImage;
        [SerializeField] private Image ringImage;

        [Header("Slices")]
        [SerializeField] private int totalSlices = 3;
        [SerializeField] private float startAngleDegrees = 90f;
        [SerializeField] private bool clockwise = true;

        [Header("Donut Shape")]
        [Range(0f, 0.49f)]
        [SerializeField] private float innerRadius = 0.20f;

        [Range(0.01f, 0.50f)]
        [SerializeField] private float outerRadius = 0.50f;

        [Header("Preview")]
        [SerializeField] private bool updateEveryFrame = true;

        private static readonly int ModeProp = Shader.PropertyToID("_Mode");
        private static readonly int InnerRadiusProp = Shader.PropertyToID("_InnerRadius");
        private static readonly int OuterRadiusProp = Shader.PropertyToID("_OuterRadius");
        private static readonly int FillProp = Shader.PropertyToID("_Fill");
        private static readonly int StartAngleProp = Shader.PropertyToID("_StartAngleDeg");
        private static readonly int ClockwiseProp = Shader.PropertyToID("_Clockwise");
        private static readonly int RectSizeProp = Shader.PropertyToID("_RectSize");
        private static readonly int PivotProp = Shader.PropertyToID("_Pivot01");

        private Material _centerMat;
        private Material _ringMat;
        private RectTransform _rt;

        private void Awake()
        {
            _rt = transform as RectTransform;
            EnsureMaterials();
            ApplyStaticParams();
            SetSlicesFilled(0); // IMPORTANT: ring starts hidden
            UpdateRectParams();
        }

        private void LateUpdate()
        {
            if (updateEveryFrame)
                UpdateRectParams();
        }

        private void OnDestroy()
        {
            if (_centerMat != null) Destroy(_centerMat);
            if (_ringMat != null) Destroy(_ringMat);
        }

        public void SetTotalSlices(int slices)
        {
            totalSlices = Mathf.Max(1, slices);
        }

        public void SetSlicesFilled(int filled)
        {
            EnsureMaterials();
            ApplyStaticParams();

            int total = Mathf.Max(1, totalSlices);
            int f = Mathf.Clamp(filled, 0, total);

            float fill01 = (float)f / total;
            _ringMat.SetFloat(FillProp, fill01);
        }

        private void EnsureMaterials()
        {
            if (centerImage == null || ringImage == null)
                return;

            Shader s = Shader.Find("UI/RadialSpriteDonut");
            if (s == null)
            {
                Debug.LogError("[DonutDeathProgressUI] Shader 'UI/RadialSpriteDonut' not found.", this);
                return;
            }

            if (_centerMat == null)
            {
                _centerMat = new Material(s);
                centerImage.material = _centerMat;
            }

            if (_ringMat == null)
            {
                _ringMat = new Material(s);
                ringImage.material = _ringMat;
            }
        }

        private void ApplyStaticParams()
        {
            if (_centerMat == null || _ringMat == null)
                return;

            float ir = Mathf.Clamp(innerRadius, 0f, 0.49f);
            float orr = Mathf.Clamp(outerRadius, 0.01f, 0.50f);

            // center: show inner disk only
            _centerMat.SetFloat(ModeProp, 0f);
            _centerMat.SetFloat(InnerRadiusProp, ir);
            _centerMat.SetFloat(OuterRadiusProp, orr);

            // ring: show ring only + radial fill
            _ringMat.SetFloat(ModeProp, 1f);
            _ringMat.SetFloat(InnerRadiusProp, ir);
            _ringMat.SetFloat(OuterRadiusProp, orr);
            _ringMat.SetFloat(StartAngleProp, startAngleDegrees);
            _ringMat.SetFloat(ClockwiseProp, clockwise ? 1f : 0f);
        }

        private void UpdateRectParams()
        {
            if (_centerMat == null || _ringMat == null || _rt == null)
                return;

            Vector2 size = _rt.rect.size;
            Vector2 pivot = _rt.pivot;

            _centerMat.SetVector(RectSizeProp, new Vector4(size.x, size.y, 0f, 0f));
            _centerMat.SetVector(PivotProp, new Vector4(pivot.x, pivot.y, 0f, 0f));

            _ringMat.SetVector(RectSizeProp, new Vector4(size.x, size.y, 0f, 0f));
            _ringMat.SetVector(PivotProp, new Vector4(pivot.x, pivot.y, 0f, 0f));
        }
    }
}
