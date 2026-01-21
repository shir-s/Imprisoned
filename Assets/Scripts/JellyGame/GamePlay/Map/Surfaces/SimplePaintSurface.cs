// FILEPATH: Assets/Scripts/Painting/SimplePaintSurface.cs
using UnityEngine;

namespace JellyGame.GamePlay.Map.Surfaces
{
    /// <summary>
    /// Paint surface with TIME-BASED AGING support.
    /// Uses RAW SECONDS for time (simpler, no normalization confusion).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Renderer))]
    public class SimplePaintSurface : MonoBehaviour
    {
        [Header("Paint Texture")]
        [SerializeField] private int textureSize = 1024;
        [SerializeField] private Color clearColor = new Color(0, 0, 0, 0);
        [SerializeField] private string paintTexProperty = "_PaintTex";
        
        [Header("Time-Based Aging")]
        [Tooltip("Enable time tracking for trail aging effect")]
        [SerializeField] private bool enableTimeAging = true;
        [SerializeField] private string paintTimeTexProperty = "_PaintTimeTex";
        [SerializeField] private string currentTimeProperty = "_CurrentTime";
        
        [Tooltip("How many seconds until paint is fully 'old' (gray)")]
        [SerializeField] private float maxAgeSeconds = 10f;

        [Header("World→Paint Mapping")]
        [SerializeField] private bool swapXZ = false;
        [SerializeField] private bool invertU = false;
        [SerializeField] private bool invertV = false;
        
        [Header("Debug")]
        [SerializeField] private bool debugTime = false;

        private Renderer _renderer;
        private RenderTexture _paintRT;
        private RenderTexture _paintTimeRT;
        private MeshFilter _mf;

        private Vector2 _localMin;
        private Vector2 _localMax;
        private bool _hasBounds;

        public RenderTexture PaintRT => _paintRT;
        public RenderTexture PaintTimeRT => _paintTimeRT;
        public bool EnableTimeAging => enableTimeAging;
        public float MaxAgeSeconds => maxAgeSeconds;

        void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _mf = GetComponent<MeshFilter>();

            InitRenderTextures();
            CacheLocalPlaneBounds();
        }

        void Update()
        {
            if (enableTimeAging && _renderer != null && _renderer.material != null)
            {
                // Send current time in RAW SECONDS (modulo a large number to prevent float precision issues)
                // Using modulo 100000 gives us ~27 hours before wrap
                float currentTime = Time.time % 100000f;
                _renderer.material.SetFloat(currentTimeProperty, currentTime);
                
                if (debugTime && Time.frameCount % 60 == 0)
                {
                    Debug.Log($"[SimplePaintSurface] CurrentTime sent to shader: {currentTime:F2}");
                }
            }
        }

        void OnDestroy()
        {
            if (_paintRT != null)
            {
                _paintRT.Release();
                Destroy(_paintRT);
                _paintRT = null;
            }
            
            if (_paintTimeRT != null)
            {
                _paintTimeRT.Release();
                Destroy(_paintTimeRT);
                _paintTimeRT = null;
            }
        }

        private void InitRenderTextures()
        {
            // Paint color texture (existing)
            if (_paintRT != null)
            {
                _paintRT.Release();
                Destroy(_paintRT);
            }

            _paintRT = new RenderTexture(textureSize, textureSize, 0, RenderTextureFormat.ARGB32);
            _paintRT.wrapMode = TextureWrapMode.Clamp;
            _paintRT.filterMode = FilterMode.Point;
            _paintRT.Create();

            ClearRT(_paintRT, clearColor);

            // Paint TIME texture - using RFloat for single channel high precision
            if (enableTimeAging)
            {
                if (_paintTimeRT != null)
                {
                    _paintTimeRT.Release();
                    Destroy(_paintTimeRT);
                }

                // RFloat gives us full 32-bit float precision for time values
                _paintTimeRT = new RenderTexture(textureSize, textureSize, 0, RenderTextureFormat.RGFloat);
                _paintTimeRT.wrapMode = TextureWrapMode.Clamp;
                _paintTimeRT.filterMode = FilterMode.Bilinear;
                _paintTimeRT.Create();

                // Initialize with 0 (will be overwritten when painted)
                ClearRT(_paintTimeRT, new Color(0, 0, 0, 0));
            }

            // Assign to material
            if (_renderer && _renderer.material != null)
            {
                _renderer.material.SetTexture(paintTexProperty, _paintRT);
                
                if (enableTimeAging && _paintTimeRT != null)
                {
                    _renderer.material.SetTexture(paintTimeTexProperty, _paintTimeRT);
                }
            }
        }

        private void ClearRT(RenderTexture rt, Color color)
        {
            var active = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, color);
            RenderTexture.active = active;
        }

        /// <summary>
        /// Get current time in seconds (for painting).
        /// Uses same modulo as Update() to stay in sync.
        /// </summary>
        public float GetCurrentTime()
        {
            return Time.time % 100000f;
        }

        // Keep this for compatibility but it now returns raw seconds
        public float GetNormalizedTime()
        {
            return GetCurrentTime();
        }

        private void CacheLocalPlaneBounds()
        {
            _hasBounds = false;

            if (_mf != null && _mf.sharedMesh != null)
            {
                var b = _mf.sharedMesh.bounds;
                if (!swapXZ)
                {
                    _localMin = new Vector2(b.min.x, b.min.z);
                    _localMax = new Vector2(b.max.x, b.max.z);
                }
                else
                {
                    _localMin = new Vector2(b.min.z, b.min.x);
                    _localMax = new Vector2(b.max.z, b.max.x);
                }

                if (Mathf.Abs(_localMax.x - _localMin.x) > 1e-6f &&
                    Mathf.Abs(_localMax.y - _localMin.y) > 1e-6f)
                {
                    _hasBounds = true;
                }
            }
        }

        public void SetPaintTexture(RenderTexture rt)
        {
            GetComponent<Renderer>().material.SetTexture(paintTexProperty, rt);
        }

        public bool TryWorldToPaintUV(Vector3 worldPos, out Vector2 uv)
        {
            if (!_hasBounds)
            {
                uv = Vector2.zero;
                return false;
            }

            Vector3 local = transform.InverseTransformPoint(worldPos);
            return TryLocalToPaintUV(local, out uv);
        }

        public bool TryLocalToPaintUV(Vector3 localPos, out Vector2 uv)
        {
            if (!_hasBounds)
            {
                uv = Vector2.zero;
                return false;
            }

            float a, b;
            if (!swapXZ)
            {
                a = localPos.x;
                b = localPos.z;
            }
            else
            {
                a = localPos.z;
                b = localPos.x;
            }

            float u = Mathf.InverseLerp(_localMin.x, _localMax.x, a);
            float v = Mathf.InverseLerp(_localMin.y, _localMax.y, b);

            if (invertU) u = 1f - u;
            if (invertV) v = 1f - v;

            uv = new Vector2(Mathf.Clamp01(u), Mathf.Clamp01(v));
            return true;
        }

        public bool TryPaintUVToWorld(Vector2 uv, out Vector3 worldPos)
        {
            worldPos = Vector3.zero;
            if (!_hasBounds)
                return false;

            float u = Mathf.Clamp01(uv.x);
            float v = Mathf.Clamp01(uv.y);
            if (invertU) u = 1f - u;
            if (invertV) v = 1f - v;

            float a = Mathf.Lerp(_localMin.x, _localMax.x, u);
            float b = Mathf.Lerp(_localMin.y, _localMax.y, v);

            Vector3 local;
            if (!swapXZ)
                local = new Vector3(a, 0f, b);
            else
                local = new Vector3(b, 0f, a);

            worldPos = transform.TransformPoint(local);
            return true;
        }
        
        /// <summary>
        /// Clear all paint.
        /// </summary>
        public void ClearAllPaint()
        {
            ClearRT(_paintRT, clearColor);
            if (_paintTimeRT != null)
                ClearRT(_paintTimeRT, new Color(0, 0, 0, 0));
        }
    }
}
