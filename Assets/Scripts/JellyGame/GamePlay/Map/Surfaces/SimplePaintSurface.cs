// FILEPATH: Assets/Scripts/Painting/SimplePaintSurface.cs

using UnityEngine;

namespace JellyGame.GamePlay.Map.Surfaces
{
    /// <summary>
    /// Simple per-object paint surface that owns a RenderTexture and plugs it into its material.
    /// Also provides a mapping from world-space points on the surface to 0..1 paint UVs,
    /// based on the mesh's local X/Z bounds (works well for planes / floor-like meshes).
    /// Includes options to swap/flip axes if the orientation is mirrored.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Renderer))]
    public class SimplePaintSurface : MonoBehaviour
    {
        [Header("Paint Texture")]
        [Tooltip("Resolution of the paint RenderTexture.")]
        [SerializeField] private int textureSize = 1024;

        [Tooltip("Clear color for the paint layer (usually transparent).")]
        [SerializeField] private Color clearColor = new Color(0, 0, 0, 0);

        [Tooltip("Name of the texture property on the material (default: _PaintTex).")]
        [SerializeField] private string paintTexProperty = "_PaintTex";

        [Header("World→Paint Mapping")]
        [Tooltip("If true, use local Z as U and local X as V (instead of X→U, Z→V).")]
        [SerializeField] private bool swapXZ = false;

        [Tooltip("Mirror the U axis (0↔1). Useful when paint moves opposite horizontally.")]
        [SerializeField] private bool invertU = false;

        [Tooltip("Mirror the V axis (0↔1). Useful when paint moves opposite vertically.")]
        [SerializeField] private bool invertV = false;

        private Renderer      _renderer;
        private RenderTexture _paintRT;
        private MeshFilter    _mf;

        // local 2D bounds (in X/Z or Z/X depending on swapXZ)
        private Vector2 _localMin;
        private Vector2 _localMax;
        private bool    _hasBounds;

        public RenderTexture PaintRT => _paintRT;

        void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _mf       = GetComponent<MeshFilter>();

            InitRenderTexture();
            CacheLocalPlaneBounds();
        }

        void OnDestroy()
        {
            if (_paintRT != null)
            {
                _paintRT.Release();
                Destroy(_paintRT);
                _paintRT = null;
            }
        }

        private void InitRenderTexture()
        {
            if (_paintRT != null)
            {
                _paintRT.Release();
                Destroy(_paintRT);
            }

            _paintRT = new RenderTexture(textureSize, textureSize, 0, RenderTextureFormat.ARGB32);
            _paintRT.wrapMode   = TextureWrapMode.Clamp;
            _paintRT.filterMode = FilterMode.Bilinear;
            _paintRT.Create();

            // Clear to transparent (or chosen clearColor)
            var active = RenderTexture.active;
            RenderTexture.active = _paintRT;
            GL.Clear(true, true, clearColor);
            RenderTexture.active = active;

            // Assign to material
            if (_renderer && _renderer.material != null)
            {
                _renderer.material.SetTexture(paintTexProperty, _paintRT);
            }
        }

        /// <summary>
        /// Cache local 2D bounds from the mesh so we can map world positions to 0..1 paint UV.
        /// Works well for plane-like meshes where local X/Z lie in the surface plane.
        /// </summary>
        private void CacheLocalPlaneBounds()
        {
            _hasBounds = false;

            if (_mf != null && _mf.sharedMesh != null)
            {
                var b = _mf.sharedMesh.bounds; // local-space bounds
                // Decide which axes are used as 2D plane
                if (!swapXZ)
                {
                    // X -> U, Z -> V
                    _localMin = new Vector2(b.min.x, b.min.z);
                    _localMax = new Vector2(b.max.x, b.max.z);
                }
                else
                {
                    // Z -> U, X -> V (swapped)
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

        /// <summary>
        /// Convert a world-space point on the surface into 0..1 UV in paint texture space.
        /// Uses local X/Z (or Z/X if swapXZ) and optional axis inversion.
        /// </summary>
        public bool TryWorldToPaintUV(Vector3 worldPos, out Vector2 uv)
        {
            if (!_hasBounds)
            {
                uv = Vector2.zero;
                return false;
            }

            Vector3 local = transform.InverseTransformPoint(worldPos); // into local space

            float a, b;
            if (!swapXZ)
            {
                a = local.x; // U-axis source
                b = local.z; // V-axis source
            }
            else
            {
                a = local.z;
                b = local.x;
            }

            float u = Mathf.InverseLerp(_localMin.x, _localMax.x, a);
            float v = Mathf.InverseLerp(_localMin.y, _localMax.y, b);

            if (invertU) u = 1f - u;
            if (invertV) v = 1f - v;

            uv = new Vector2(Mathf.Clamp01(u), Mathf.Clamp01(v));
            return true;
        }
    
    
        /// <summary>
        /// Convert paint UV (0..1) back into a world-space point on the surface.
        /// This is the inverse of TryWorldToPaintUV, using the same bounds & flags.
        /// </summary>
        public bool TryPaintUVToWorld(Vector2 uv, out Vector3 worldPos)
        {
            worldPos = Vector3.zero;
            if (!_hasBounds)
                return false;
            // Clamp and apply inversion flags
            float u = Mathf.Clamp01(uv.x);
            float v = Mathf.Clamp01(uv.y);
            if (invertU) u = 1f - u;
            if (invertV) v = 1f - v;
            // Map 0..1 back into local 2D bounds
            float a = Mathf.Lerp(_localMin.x, _localMax.x, u);
            float b = Mathf.Lerp(_localMin.y, _localMax.y, v);
            Vector3 local;
            if (!swapXZ)
            {
                // X -> U, Z -> V
                local = new Vector3(a, 0f, b);
            }
            else
            {
                // Z -> U, X -> V (swapped)
                local = new Vector3(b, 0f, a);
            }
            worldPos = transform.TransformPoint(local);
            return true;
        }

    }
}
