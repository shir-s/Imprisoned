using JellyGame.GamePlay.Map.Surfaces;
using UnityEngine;

/// <summary>
/// Samples the SimplePaintSurface RenderTexture into a low-res logical grid.
/// Each cell in the grid marks whether there is paint (trail) there or not.
/// This will be used later by the monster's A*.
/// Attach this to the same GameObject that has SimplePaintSurface.
/// </summary>
[DisallowMultipleComponent]
public class PaintTrailGrid : MonoBehaviour
{
    [Header("Surface")]
    [SerializeField] private SimplePaintSurface surface;

    [Header("Grid Settings")]
    [SerializeField] private int gridResolution = 64;
    [Tooltip("Seconds between texture samples.")]
    [SerializeField] private float sampleInterval = 0.2f;
    [Tooltip("Threshold for deciding if a pixel is 'painted'.")]
    [SerializeField] private float paintedThreshold = 0.1f;
    [Tooltip("If true, use alpha channel. If false, use grayscale of RGB.")]
    [SerializeField] private bool useAlpha = true;

    [Header("Debug")]
    [SerializeField] private bool debugDrawGizmos = true;
    [SerializeField] private Color paintedCellColor = new Color(0f, 1f, 0.2f, 0.3f);

    private bool[,] _painted;
    private float _nextSampleTime;

    private Texture2D _readTexture;
    private RenderTexture _downsampleRT;

    public int Resolution => gridResolution;
    public bool[,] Painted => _painted;

    private void Awake()
    {
        if (surface == null)
            surface = GetComponent<SimplePaintSurface>();

        if (surface == null)
        {
            Debug.LogError("[PaintTrailGrid] Missing SimplePaintSurface reference.", this);
            enabled = false;
            return;
        }

        InitGrid();
    }

    private void OnDestroy()
    {
        if (_downsampleRT != null)
        {
            _downsampleRT.Release();
            _downsampleRT = null;
        }

        if (_readTexture != null)
        {
            Destroy(_readTexture);
            _readTexture = null;
        }
    }

    private void OnValidate()
    {
        if (gridResolution < 4) gridResolution = 4;
        if (sampleInterval < 0.01f) sampleInterval = 0.01f;
    }

    private void InitGrid()
    {
        _painted = new bool[gridResolution, gridResolution];
    }

    private void Update()
    {
        if (surface == null || surface.PaintRT == null)
            return;

        if (Time.time < _nextSampleTime)
            return;

        _nextSampleTime = Time.time + sampleInterval;
        SamplePaintTexture();
    }

    /// <summary>
    /// Downsample the paint RenderTexture into a small Texture2D and fill the grid.
    /// </summary>
    private void SamplePaintTexture()
    {
        var rt = surface.PaintRT;
        if (rt == null)
            return;

        // Ensure downsample RT
        if (_downsampleRT == null ||
            _downsampleRT.width != gridResolution ||
            _downsampleRT.height != gridResolution)
        {
            if (_downsampleRT != null)
                _downsampleRT.Release();

            _downsampleRT = new RenderTexture(gridResolution, gridResolution, 0, rt.format);
            _downsampleRT.wrapMode = rt.wrapMode;
            _downsampleRT.filterMode = FilterMode.Bilinear;
            _downsampleRT.Create();
        }

        // Blit high-res paint RT into low-res RT
        Graphics.Blit(rt, _downsampleRT);

        // Ensure read Texture2D
        if (_readTexture == null ||
            _readTexture.width != gridResolution ||
            _readTexture.height != gridResolution)
        {
            _readTexture = new Texture2D(gridResolution, gridResolution, TextureFormat.RGBA32, false);
        }

        // Read pixels from downsample RT
        var active = RenderTexture.active;
        RenderTexture.active = _downsampleRT;
        _readTexture.ReadPixels(new Rect(0, 0, gridResolution, gridResolution), 0, 0);
        _readTexture.Apply();
        RenderTexture.active = active;

        // Fill logical grid
        int paintedCount = 0;

        for (int y = 0; y < gridResolution; y++)
        {
            for (int x = 0; x < gridResolution; x++)
            {
                Color c = _readTexture.GetPixel(x, y);
                float value = useAlpha ? c.a : c.grayscale;
                bool isPainted = value > paintedThreshold;
                _painted[x, y] = isPainted;
                if (isPainted) paintedCount++;
            }
        }

        //Debug.Log($"[PaintTrailGrid] Painted cells: {paintedCount}");

    }

    /// <summary>
    /// Convert a world-space point on the surface to grid coordinates.
    /// </summary>
    public bool WorldToGrid(Vector3 worldPos, out int gx, out int gy)
    {
        gx = gy = 0;

        if (surface == null)
            return false;

        if (!surface.TryWorldToPaintUV(worldPos, out Vector2 uv))
            return false;

        gx = Mathf.Clamp(Mathf.FloorToInt(uv.x * gridResolution), 0, gridResolution - 1);
        gy = Mathf.Clamp(Mathf.FloorToInt(uv.y * gridResolution), 0, gridResolution - 1);
        return true;
    }

    /// <summary>
    /// Convert grid coordinates to a world-space point on the surface
    /// (center of the cell).
    /// </summary>
    public bool GridToWorld(int gx, int gy, out Vector3 worldPos)
    {
        worldPos = Vector3.zero;

        if (surface == null)
            return false;

        gx = Mathf.Clamp(gx, 0, gridResolution - 1);
        gy = Mathf.Clamp(gy, 0, gridResolution - 1);

        float u = (gx + 0.5f) / gridResolution;
        float v = (gy + 0.5f) / gridResolution;

        return surface.TryPaintUVToWorld(new Vector2(u, v), out worldPos);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!debugDrawGizmos || _painted == null || surface == null)
            return;

        // Draw small translucent quads over painted cells, for debugging.
        Gizmos.color = paintedCellColor;

        int res = gridResolution;

        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                if (!_painted[x, y])
                    continue;

                if (GridToWorld(x, y, out Vector3 wp))
                {
                    // Small square above the surface
                    Gizmos.DrawCube(wp + Vector3.up * 0.001f, Vector3.one * 0.05f);
                }
            }
        }
    }
#endif
}
