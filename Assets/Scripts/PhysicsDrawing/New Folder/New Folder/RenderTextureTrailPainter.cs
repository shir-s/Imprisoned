
using UnityEngine;

/// <summary>
/// IMovementPainter that paints directly into a RenderTexture on the hit surface,
/// using WORLD SPACE positions mapped through SimplePaintSurface (not mesh UVs).
/// No StrokeMesh objects are created.
///
/// IMPORTANT:
/// - This painter now assumes MovementPaintController already sub-divides movement,
///   so it does NOT do its own sub-stepping. It simply paints once per OnMoveStep call.
/// </summary>
[DisallowMultipleComponent]
public class RenderTextureTrailPainter : MonoBehaviour, IMovementPainter
{
    [Header("Raycast")]
    [Tooltip("Layers that can receive paint.")]
    [SerializeField] private LayerMask surfaceMask;

    [Tooltip("Max ray distance from the cube to find a surface.")]
    [SerializeField] private float rayDistance = 2f;

    [Tooltip("If true, cast ray straight down (world -Y). If false, use -transform.up.")]
    [SerializeField] private bool useWorldDown = true;

    [Header("Brush")]
    [Tooltip("Material with a brush-blit shader that writes into the paint texture.")]
    [SerializeField] private Material brushBlitMaterial;

    [Tooltip("Name of the RenderTexture property on the brush material (default: _MainTex).")]
    [SerializeField] private string brushSourceTexProperty = "_MainTex";

    [Tooltip("Fallback half-size in UV units (0..1) if dynamic sizing fails.")]
    [SerializeField] private float fallbackHalfSizeUV = 0.02f;

    [Tooltip("Scale factor applied to cube half-size when computing brush size.")]
    [SerializeField] private float sizeWorldMultiplier = 1.0f;

    [Tooltip("Brush hardness: 0 = soft, 1 = hard edge.")]
    [SerializeField, Range(0f, 1f)] private float brushHardness = 0.5f;

    //[Tooltip("Brush opacity per dab (0..1).")]
    //[SerializeField, Range(0f, 1f)] private float brushOpacity = 0.1f;
    [Tooltip("How much opacity to add per meter of travel.")]
    [SerializeField] private float opacityPerMeter = 5.0f;
    
    [SerializeField] private Color brushColor = Color.black;

    [Header("Sampling")]
    [Tooltip("Minimum distance between paint dabs, in world meters, based on stepMeters from MovementPaintController.")]
    [SerializeField] private float minWorldStep = 0.0005f;

    [Header("Debug")]
    [SerializeField] private bool debugRays = false;

    private SimplePaintSurface _currentSurface;
    private RenderTexture _tempRT;
    private void Awake()
    {
        var renderer = GetComponent<Renderer>();
        if (renderer != null)
            brushColor = renderer.material.color;

        if (brushBlitMaterial != null)
        {
            brushBlitMaterial.SetFloat("_BrushHardness", brushHardness);
            brushBlitMaterial.SetColor("_BrushColor", brushColor);
        }
    }



    // ========== IMovementPainter API ==========

    public void OnMovementStart(Vector3 worldPos)
    {
        if (TryRaycastSurface(worldPos, out var hit))
        {
            SetSurfaceFromHit(hit);
            PaintAtWorldPoint(hit.point); // first dab
        }
        else
        {
            ClearSurface();
        }
    }

    /*public void OnMoveStep(Vector3 from, Vector3 to, float stepMeters, float deltaTime)
    {
        // Optional extra filter against ultra-dense calls
        if (stepMeters < minWorldStep)
            return;

        if (!TryRaycastSurface(to, out var hit))
        {
            ClearSurface();
            return;
        }

        SetSurfaceFromHit(hit);
        PaintAtWorldPoint(hit.point);
    }*/
    public void OnMoveStep(Vector3 from, Vector3 to, float stepMeters, float deltaTime)
    {
        if (stepMeters < minWorldStep)
            return;

        if (!TryRaycastSurface(to, out var hit))
        {
            ClearSurface();
            return;
        }

        SetSurfaceFromHit(hit);

        if (_currentSurface == null || brushBlitMaterial == null)
            return;

        float effectiveOpacity = Mathf.Clamp01(opacityPerMeter * stepMeters);

        brushBlitMaterial.SetFloat("_BrushOpacity", effectiveOpacity);

        PaintAtWorldPoint(hit.point);
    }



    public void OnMovementEnd(Vector3 worldPos)
    {
        // Nothing special needed, but we can clear cached surface to be safe.
        ClearSurface();
    }

    // ========== Surface helpers ==========

    private void SetSurfaceFromHit(RaycastHit hit)
    {
        var surf = hit.collider.GetComponentInParent<SimplePaintSurface>();
        _currentSurface = surf;
    }

    private void ClearSurface()
    {
        _currentSurface = null;
    }

    // ========== Painting ==========

    /*private void PaintAtWorldPoint(Vector3 worldPoint)
    {
        if (_currentSurface == null) return;
        if (brushBlitMaterial == null)
        {
            Debug.LogWarning($"[RenderTextureTrailPainter] No brushBlitMaterial set on {name}", this);
            return;
        }

        var rt = _currentSurface.PaintRT;
        if (rt == null)
        {
            Debug.LogWarning($"[RenderTextureTrailPainter] Surface {_currentSurface.name} has no RenderTexture.", this);
            return;
        }

        // Map world position -> paint UV
        if (!_currentSurface.TryWorldToPaintUV(worldPoint, out var uvCenter))
            return;

        // Compute square half-size in UV from cube world size
        Vector2 halfSizeUV = ComputeHalfSizeUV(worldPoint, uvCenter);
        if (!float.IsFinite(halfSizeUV.x) || !float.IsFinite(halfSizeUV.y) ||
            halfSizeUV.x <= 0f || halfSizeUV.y <= 0f)
        {
            halfSizeUV = new Vector2(fallbackHalfSizeUV, fallbackHalfSizeUV);
        }

        // Set brush parameters
        brushBlitMaterial.SetVector("_BrushCenter",   new Vector4(uvCenter.x, uvCenter.y, 0, 0));
        brushBlitMaterial.SetVector("_BrushHalfSize", new Vector4(halfSizeUV.x, halfSizeUV.y, 0, 0));
        //brushBlitMaterial.SetFloat("_BrushHardness",  brushHardness);
        //brushBlitMaterial.SetFloat("_BrushOpacity",   brushOpacity);
        //brushBlitMaterial.SetColor("_BrushColor",     brushColor);

        // Ping-pong blit
        RenderTexture temp = RenderTexture.GetTemporary(rt.width, rt.height, 0, rt.format);
        temp.wrapMode   = rt.wrapMode;
        temp.filterMode = rt.filterMode;

        brushBlitMaterial.SetTexture(brushSourceTexProperty, rt);
        Graphics.Blit(rt, temp, brushBlitMaterial);
        Graphics.Blit(temp, rt);

        RenderTexture.ReleaseTemporary(temp);
    }*/
    private void PaintAtWorldPoint(Vector3 worldPoint)
    {
        if (_currentSurface == null || brushBlitMaterial == null)
            return;

        var rt = _currentSurface.PaintRT;
        if (rt == null)
            return;

        if (!_currentSurface.TryWorldToPaintUV(worldPoint, out var uvCenter))
            return;

        Vector2 halfSizeUV = ComputeHalfSizeUV(worldPoint, uvCenter);
        if (!float.IsFinite(halfSizeUV.x) || !float.IsFinite(halfSizeUV.y) ||
            halfSizeUV.x <= 0f || halfSizeUV.y <= 0f)
        {
            halfSizeUV = new Vector2(fallbackHalfSizeUV, fallbackHalfSizeUV);
        }

        brushBlitMaterial.SetVector("_BrushCenter",   new Vector4(uvCenter.x, uvCenter.y, 0, 0));
        brushBlitMaterial.SetVector("_BrushHalfSize", new Vector4(halfSizeUV.x, halfSizeUV.y, 0, 0));

        if (_tempRT == null ||
            _tempRT.width  != rt.width ||
            _tempRT.height != rt.height ||
            _tempRT.format != rt.format)
        {
            if (_tempRT != null)
                _tempRT.Release();

            _tempRT = new RenderTexture(rt.descriptor);
            _tempRT.Create();
        }

        _tempRT.wrapMode   = rt.wrapMode;
        _tempRT.filterMode = rt.filterMode;

        brushBlitMaterial.SetTexture(brushSourceTexProperty, rt);
        Graphics.Blit(rt, _tempRT, brushBlitMaterial);
        Graphics.Blit(_tempRT, rt);
    }


    /// <summary>
    /// Compute brush half-size in UV space from the cube's world X/Z size.
    /// The square is axis-aligned in UV.
    /// </summary>
    private Vector2 ComputeHalfSizeUV(Vector3 worldCenter, Vector2 uvCenter)
    {
        // Approximate cube footprint size on the plane
        Vector3 s = transform.lossyScale;
        float halfWorldX = 0.5f * Mathf.Abs(s.x) * sizeWorldMultiplier;
        float halfWorldZ = 0.5f * Mathf.Abs(s.z) * sizeWorldMultiplier;

        if (halfWorldX <= 0f || halfWorldZ <= 0f)
            return new Vector2(fallbackHalfSizeUV, fallbackHalfSizeUV);

        // Use surface's local right/forward as world axes for paint space
        Transform surfT = _currentSurface.transform;
        Vector3 right   = surfT.right;
        Vector3 forward = surfT.forward;

        // Sample UV at +X and +Z directions to get extents
        Vector2 uvRight, uvForward;
        bool okR = _currentSurface.TryWorldToPaintUV(worldCenter + right   * halfWorldX, out uvRight);
        bool okF = _currentSurface.TryWorldToPaintUV(worldCenter + forward * halfWorldZ, out uvForward);

        if (!okR || !okF)
            return new Vector2(fallbackHalfSizeUV, fallbackHalfSizeUV);

        // Half-size along U and V (axis-aligned in UV)
        float halfU = Mathf.Abs(uvRight.x   - uvCenter.x);
        float halfV = Mathf.Abs(uvForward.y - uvCenter.y);

        if (halfU <= 1e-6f) halfU = fallbackHalfSizeUV;
        if (halfV <= 1e-6f) halfV = fallbackHalfSizeUV;

        return new Vector2(halfU, halfV);
    }

    // ========== Raycast helper ==========

    private bool TryRaycastSurface(Vector3 fromPos, out RaycastHit hit)
    {
        Vector3 dir   = useWorldDown ? Vector3.down : -transform.up;
        Vector3 start = fromPos - dir * 0.01f;

        if (debugRays)
            Debug.DrawRay(start, dir * rayDistance, Color.cyan, 0.1f);

        return Physics.Raycast(start, dir, out hit, rayDistance, surfaceMask, QueryTriggerInteraction.Collide);
    }
    
    private void OnDestroy()
    {
        if (_tempRT != null)
        {
            _tempRT.Release();
            _tempRT = null;
        }
    }

}
