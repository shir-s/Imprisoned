// FILEPATH: Assets/Scripts/Painting/RenderTextureTrailPainter.cs
using UnityEngine;

/// <summary>
/// IMovementPainter that paints directly into a RenderTexture on the hit surface.
/// Fully robust against the cube sinking a bit into the surface.
/// Raycasts both DOWN and UP so the cube always finds the surface.
/// </summary>
[DisallowMultipleComponent]
public class RenderTextureTrailPainter : MonoBehaviour, IMovementPainter
{
    [Header("Raycast")]
    [SerializeField] private LayerMask surfaceMask;
    [SerializeField] private float rayDistance = 2f;
    [SerializeField] private bool useWorldDown = true;

    [Header("Brush")]
    [SerializeField] private Material brushBlitMaterial;
    [SerializeField] private string brushSourceTexProperty = "_MainTex";
    [SerializeField] private float fallbackHalfSizeUV = 0.02f;
    [SerializeField] private float sizeWorldMultiplier = 1.0f;
    [SerializeField, Range(0f, 1f)] private float brushHardness = 0.5f;
    [SerializeField] private float opacityPerMeter = 5.0f;
    [SerializeField] private Color brushColor = Color.black;

    [Header("Sampling")]
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

    // ========== IMovementPainter ==========

    public void OnMovementStart(Vector3 worldPos)
    {
        if (TryRaycastSurface(worldPos, out var hit))
        {
            SetSurface(hit);
            PaintAtWorldPoint(hit.point);
        }
        else
        {
            ClearSurface();
        }
    }

    public void OnMoveStep(Vector3 from, Vector3 to, float stepMeters, float dt)
    {
        if (stepMeters < minWorldStep)
            return;

        if (!TryRaycastSurface(to, out var hit))
        {
            ClearSurface();
            return;
        }

        SetSurface(hit);

        if (_currentSurface == null || brushBlitMaterial == null)
            return;

        float effectiveOpacity = Mathf.Clamp01(opacityPerMeter * stepMeters);
        brushBlitMaterial.SetFloat("_BrushOpacity", effectiveOpacity);

        PaintAtWorldPoint(hit.point);
    }

    public void OnMovementEnd(Vector3 worldPos)
    {
        ClearSurface();
    }

    // ========== Surface Helpers ==========

    private void SetSurface(RaycastHit hit)
    {
        _currentSurface = hit.collider.GetComponentInParent<SimplePaintSurface>();
    }

    private void ClearSurface()
    {
        _currentSurface = null;
    }

    // ========== Painting ==========

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
        if (!float.IsFinite(halfSizeUV.x) || halfSizeUV.x <= 0f)
            halfSizeUV = new Vector2(fallbackHalfSizeUV, fallbackHalfSizeUV);

        brushBlitMaterial.SetVector("_BrushCenter",   new Vector4(uvCenter.x, uvCenter.y, 0, 0));
        brushBlitMaterial.SetVector("_BrushHalfSize", new Vector4(halfSizeUV.x, halfSizeUV.y, 0, 0));

        if (_tempRT == null ||
            _tempRT.width != rt.width ||
            _tempRT.height != rt.height ||
            _tempRT.format != rt.format)
        {
            if (_tempRT != null)
                _tempRT.Release();
            _tempRT = new RenderTexture(rt.descriptor);
            _tempRT.Create();
        }

        _tempRT.wrapMode = rt.wrapMode;
        _tempRT.filterMode = rt.filterMode;

        brushBlitMaterial.SetTexture(brushSourceTexProperty, rt);
        Graphics.Blit(rt, _tempRT, brushBlitMaterial);
        Graphics.Blit(_tempRT, rt);
    }

    private Vector2 ComputeHalfSizeUV(Vector3 worldCenter, Vector2 uvCenter)
    {
        Vector3 s = transform.lossyScale;
        float halfWorldX = 0.5f * Mathf.Abs(s.x) * sizeWorldMultiplier;
        float halfWorldZ = 0.5f * Mathf.Abs(s.z) * sizeWorldMultiplier;

        if (halfWorldX <= 0f || halfWorldZ <= 0f)
            return new Vector2(fallbackHalfSizeUV, fallbackHalfSizeUV);

        Transform surf = _currentSurface.transform;

        Vector2 uvR, uvF;
        bool okR = _currentSurface.TryWorldToPaintUV(worldCenter + surf.right   * halfWorldX, out uvR);
        bool okF = _currentSurface.TryWorldToPaintUV(worldCenter + surf.forward * halfWorldZ, out uvF);

        if (!okR || !okF)
            return new Vector2(fallbackHalfSizeUV, fallbackHalfSizeUV);

        float halfU = Mathf.Abs(uvR.x - uvCenter.x);
        float halfV = Mathf.Abs(uvF.y - uvCenter.y);

        if (halfU <= 1e-6f) halfU = fallbackHalfSizeUV;
        if (halfV <= 1e-6f) halfV = fallbackHalfSizeUV;

        return new Vector2(halfU, halfV);
    }

    // ========== NEW — Robust Raycast (DOWN + UP) ==========

    private bool TryRaycastSurface(Vector3 fromPos, out RaycastHit bestHit)
    {
        Vector3 primaryDir = useWorldDown ? Vector3.down : -transform.up;

        // 1) Try downwards
        if (RaycastOneDirection(fromPos, primaryDir, out bestHit))
            return true;

        // 2) If cube is inside the collider, down fails → try upwards
        if (RaycastOneDirection(fromPos, -primaryDir, out bestHit))
            return true;

        bestHit = default;
        return false;
    }

    private bool RaycastOneDirection(Vector3 fromPos, Vector3 dir, out RaycastHit hit)
    {
        // Start slightly outside to avoid beginning under the surface
        Vector3 start = fromPos - dir * 0.05f;

        if (debugRays)
            Debug.DrawRay(start, dir * rayDistance, Color.magenta, 0.1f);

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
