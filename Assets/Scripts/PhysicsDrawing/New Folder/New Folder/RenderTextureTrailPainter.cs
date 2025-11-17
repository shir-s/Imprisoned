// FILEPATH: Assets/Scripts/Painting/RenderTextureTrailPainter.cs
using UnityEngine;

/// <summary>
/// IMovementPainter that paints directly into a RenderTexture on the hit surface,
/// using WORLD SPACE positions mapped through SimplePaintSurface (not mesh UVs).
/// No StrokeMesh objects are created.
/// Brush footprint is an axis-aligned SQUARE in paint UV space, whose size is
/// derived from the cube's world X/Z size, so it roughly matches the cube shape.
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

    [Tooltip("Brush opacity per dab (0..1).")]
    [SerializeField, Range(0f, 1f)] private float brushOpacity = 1.0f;

    [SerializeField] private Color brushColor = Color.black;

    [Header("Sampling")]
    [Tooltip("Minimum distance between paint dabs, in world meters.")]
    [SerializeField] private float minWorldStep = 0.0015f;

    [Header("Debug")]
    [SerializeField] private bool debugRays = false;

    // --- runtime ---
    private SimplePaintSurface _currentSurface;
    private Vector3            _lastPosWS;
    private bool               _hasLast;

    // ========== IMovementPainter API ==========

    public void OnMovementStart(Vector3 worldPos)
    {
        _hasLast = false;

        if (TryRaycastSurface(worldPos, out var hit))
        {
            SetSurfaceFromHit(hit);
            PaintAtWorldPoint(hit.point); // first dab
            _lastPosWS = hit.point;
            _hasLast   = true;
        }
        else
        {
            ClearSurface();
        }
    }

    public void OnMoveStep(Vector3 from, Vector3 to, float stepMeters, float deltaTime)
    {
        if (!TryRaycastSurface(to, out var hit))
        {
            ClearSurface();
            _hasLast = false;
            return;
        }

        SetSurfaceFromHit(hit);

        Vector3 p = hit.point;
        if (!_hasLast)
        {
            PaintAtWorldPoint(p);
            _lastPosWS = p;
            _hasLast   = true;
            return;
        }

        float sq    = (p - _lastPosWS).sqrMagnitude;
        float minSq = minWorldStep * minWorldStep;
        if (sq < minSq)
            return;

        PaintAtWorldPoint(p);
        _lastPosWS = p;
        _hasLast   = true;
    }

    public void OnMovementEnd(Vector3 worldPos)
    {
        _hasLast = false;
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

    private void PaintAtWorldPoint(Vector3 worldPoint)
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
        brushBlitMaterial.SetFloat("_BrushHardness",  brushHardness);
        brushBlitMaterial.SetFloat("_BrushOpacity",   brushOpacity);
        brushBlitMaterial.SetColor("_BrushColor",     brushColor);

        // Ping-pong blit
        RenderTexture temp = RenderTexture.GetTemporary(rt.width, rt.height, 0, rt.format);
        temp.wrapMode   = rt.wrapMode;
        temp.filterMode = rt.filterMode;

        brushBlitMaterial.SetTexture(brushSourceTexProperty, rt);
        Graphics.Blit(rt, temp, brushBlitMaterial);
        Graphics.Blit(temp, rt);

        RenderTexture.ReleaseTemporary(temp);
    }

    /// <summary>
    /// Compute brush half-size in UV space from the cube's world X/Z size.
    /// The square is axis-aligned in paint UV.
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
}
