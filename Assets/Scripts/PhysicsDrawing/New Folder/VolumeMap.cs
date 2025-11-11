// FILEPATH: Assets/Scripts/Painting/VolumeMap.cs
using UnityEngine;

/// <summary>
/// Accumulative height map publisher for the Jam shader.
/// Writes additively into an RFloat RenderTexture and exposes the exact
/// globals used by the URP/JamStroke shader.
/// </summary>
[DisallowMultipleComponent]
public class VolumeMap : MonoBehaviour
{
    [Header("Paintable Area in Local Space (meters)")]
    [SerializeField] private float _sizeX = 1.0f;        // local X spans [-sizeX/2, +sizeX/2]
    [SerializeField] private float _sizeZ = 1.0f;        // local Z spans [-sizeZ/2, +sizeZ/2]

    [Header("Texture Resolution (pixels)")]
    [SerializeField] private int _width  = 1024;
    [SerializeField] private int _height = 1024;

    [Header("Map Scaling")]
    [Tooltip("How many texture height units to write per 1 meter of real thickness.")]
    [SerializeField] private float _metersToMap = 50f;   // e.g. 0.02 m * 50 = 1.0 in map

    [Header("Runtime (read-only)")]
    [SerializeField] private RenderTexture _heightRT;

    private Texture2D _patch;

    public RenderTexture HeightRT => _heightRT;
    public float metersToMap => Mathf.Max(0f, _metersToMap);

    void OnEnable()
    {
        EnsureRT();
        PublishGlobals();
    }

    void OnDisable()
    {
        Shader.SetGlobalTexture("_JamVolumeTex", null);
    }

    void Update()
    {
        // in case transform/res changes at runtime
        PublishGlobals();
    }

    void EnsureRT()
    {
        if (_heightRT != null && _heightRT.width == _width && _heightRT.height == _height) return;

        if (_heightRT != null)
        {
            _heightRT.Release();
            DestroyImmediate(_heightRT);
        }

        _heightRT = new RenderTexture(_width, _height, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear)
        {
            name = "Jam_VolumeHeightRT",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            useMipMap = false,
            autoGenerateMips = false
        };
        _heightRT.Create();

        // clear to 0
        var prev = RenderTexture.active;
        RenderTexture.active = _heightRT;
        GL.Clear(false, true, Color.black);
        RenderTexture.active = prev;

        if (_patch == null) _patch = new Texture2D(1, 1, TextureFormat.RFloat, false, true);
    }

    void PublishGlobals()
    {
        EnsureRT();

        // Texture + texel size
        Shader.SetGlobalTexture("_JamVolumeTex", _heightRT);
        Shader.SetGlobalVector("_JamVolumeTex_TexelSize", new Vector4(1f / _width, 1f / _height, _width, _height));

        // World->paper (paper UV = local x,z in [-0.5..+0.5] mapped to [0..1])
        // Build a matrix that: world -> local -> normalize -> uv
        var W2L = transform.worldToLocalMatrix;
        // scale/offset to uv
        var N = Matrix4x4.identity;
        N.m00 = 1f / _sizeX;  // x
        N.m22 = 1f / _sizeZ;  // z
        var T = Matrix4x4.Translate(new Vector3(0.5f, 0f, 0.5f));
        var S = Matrix4x4.Scale(new Vector3(1f, 1f, 1f));
        // paper.xz will be used in shader; y is unused
        var JamWorldToPaper = T * S * N * W2L;
        Shader.SetGlobalMatrix("_JamWorldToPaper", JamWorldToPaper);

        // Tangent axes in world for gradient-to-normal conversion
        Shader.SetGlobalVector("_JamPaperRightWS", transform.right);
        Shader.SetGlobalVector("_JamPaperFwdWS",   transform.forward);
    }

    /// <summary>
    /// Additive circular stamp (heightMeters converted to map units internally).
    /// </summary>
    public void AddStamp(Vector3 worldPos, float radiusMeters, float heightMeters)
    {
        if (radiusMeters <= 0f || heightMeters <= 0f) return;
        EnsureRT();

        // World -> UV -> pixels
        var local = transform.InverseTransformPoint(worldPos);
        float u = (local.x / _sizeX) + 0.5f;
        float v = (local.z / _sizeZ) + 0.5f;

        int cx = Mathf.RoundToInt(u * (_width  - 1));
        int cy = Mathf.RoundToInt(v * (_height - 1));
        float pxPerMeterX = (_width  - 1) / _sizeX;
        float pxPerMeterY = (_height - 1) / _sizeZ;
        float rpx = radiusMeters * 0.5f * (pxPerMeterX + pxPerMeterY); // avg to keep circle

        int ir = Mathf.Max(1, Mathf.CeilToInt(rpx));
        int x0 = Mathf.Clamp(cx - ir, 0, _width  - 1);
        int y0 = Mathf.Clamp(cy - ir, 0, _height - 1);
        int x1 = Mathf.Clamp(cx + ir, 0, _width  - 1);
        int y1 = Mathf.Clamp(cy + ir, 0, _height - 1);
        int w = x1 - x0 + 1;
        int h = y1 - y0 + 1;

        // read patch
        if (_patch.width != w || _patch.height != h)
            _patch.Reinitialize(w, h);
        var prev = RenderTexture.active;
        RenderTexture.active = _heightRT;
        _patch.ReadPixels(new Rect(x0, y0, w, h), 0, 0, false);
        _patch.Apply(false, false);
        RenderTexture.active = prev;

        // modify patch additively
        var data = _patch.GetPixelData<float>(0);
        float r2 = rpx * rpx;
        float addUnits = heightMeters * _metersToMap;
        int idx = 0;
        for (int yy = 0; yy < h; yy++)
        {
            float dy = (y0 + yy - cy);
            float dy2 = dy * dy;
            for (int xx = 0; xx < w; xx++, idx++)
            {
                float dx = (x0 + xx - cx);
                float d2 = dx * dx + dy2;
                if (d2 > r2) continue;

                float t = Mathf.Clamp01(1f - Mathf.Sqrt(d2 / r2));     // 1..0
                float profile = t * t * (3f - 2f * t);                  // smooth cubic
                data[idx] = data[idx] + addUnits * profile;             // ADD
            }
        }
        _patch.Apply(false, false);

        // write back ONLY the sub-rect
        Graphics.CopyTexture(_patch, 0, 0, 0, 0, w, h, _heightRT, 0, 0, x0, y0);

        // keep globals fresh
        PublishGlobals();
    }
}
