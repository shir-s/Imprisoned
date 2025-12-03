using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class AreaFillShapeDetector : MonoBehaviour, IStrokeShapeDetector
{
    [Header("References")]
    // Support multiple paint surfaces (different floor pieces)
    [SerializeField] private List<SimplePaintSurface> paintSurfaces = new();
    [SerializeField] private RenderTextureTrailPainter painter;

    [Header("Fill sampling (UV space)")]
    [SerializeField] private float uvStep = 0.01f;

    [SerializeField] private bool debugPolygon = false;

    public bool TryHandleShape(StrokeLoopSegment seg)
    {
        if (paintSurfaces == null || paintSurfaces.Count == 0 ||
            painter == null || seg.history == null)
            return false;

        StrokeHistory history = seg.history;
        int startIndex        = seg.startIndex;
        int endIndexInclusive = seg.endIndexInclusive;

        if (endIndexInclusive <= startIndex + 2)
            return false;

        // Active surface = the floor piece where the loop is drawn
        SimplePaintSurface activeSurface = null;
        List<Vector2> uvPolygon = new List<Vector2>();

        // 1) Build polygon in UV from the loop samples.
        for (int i = startIndex; i <= endIndexInclusive; i++)
        {
            StrokeSample sample = history[i];
            Vector3 worldPos    = sample.WorldPos;

            if (!TryWorldToPaintUVOnAnySurface(worldPos, out SimplePaintSurface surface, out Vector2 uv))
                continue;

            // First hit decides which surface we are filling
            if (activeSurface == null)
                activeSurface = surface;
            else if (surface != activeSurface)
            {
                // Ignore points on a different surface
                continue;
            }

            uvPolygon.Add(uv);
        }

        if (activeSurface == null || uvPolygon.Count < 3)
            return false;

        if (debugPolygon)
            DebugDrawUvPolygon(uvPolygon, activeSurface);

        // 2) Compute UV bounding box.
        float minU =  1f;
        float maxU =  0f;
        float minV =  1f;
        float maxV =  0f;

        foreach (var uv in uvPolygon)
        {
            if (uv.x < minU) minU = uv.x;
            if (uv.x > maxU) maxU = uv.x;
            if (uv.y < minV) minV = uv.y;
            if (uv.y > maxV) maxV = uv.y;
        }

        minU = Mathf.Clamp01(minU);
        maxU = Mathf.Clamp01(maxU);
        minV = Mathf.Clamp01(minV);
        maxV = Mathf.Clamp01(maxV);

        // 3) Scan a UV grid inside the bounding box and fill points inside the polygon.
        for (float u = minU; u <= maxU; u += uvStep)
        {
            for (float v = minV; v <= maxV; v += uvStep)
            {
                Vector2 p = new Vector2(u, v);
                if (IsPointInPolygon(p, uvPolygon))
                {
                    painter.PaintAtUV(activeSurface, p);
                }
            }
        }

        // Shape handled: we filled the area.
        return true;
    }

    /// <summary>
    /// Tries to map a world position to UV on any of the paint surfaces.
    /// Returns the surface that succeeded and the UV.
    /// </summary>
    private bool TryWorldToPaintUVOnAnySurface(
        Vector3 worldPos,
        out SimplePaintSurface surface,
        out Vector2 uv)
    {
        surface = null;
        uv = default;

        foreach (var s in paintSurfaces)
        {
            if (s == null) continue;

            if (s.TryWorldToPaintUV(worldPos, out uv))
            {
                surface = s;
                return true;
            }
        }

        return false;
    }

    private bool IsPointInPolygon(Vector2 p, List<Vector2> poly)
    {
        bool inside = false;
        int count   = poly.Count;

        for (int i = 0, j = count - 1; i < count; j = i++)
        {
            Vector2 pi = poly[i];
            Vector2 pj = poly[j];

            bool intersect =
                ((pi.y > p.y) != (pj.y > p.y)) &&
                (p.x < (pj.x - pi.x) * (p.y - pi.y) / ((pj.y - pi.y) + 1e-6f) + pi.x);

            if (intersect)
                inside = !inside;
        }

        return inside;
    }

    private void DebugDrawUvPolygon(List<Vector2> poly, SimplePaintSurface surface)
    {
        for (int i = 0; i < poly.Count; i++)
        {
            Vector2 a = poly[i];
            Vector2 b = poly[(i + 1) % poly.Count];

            if (surface.TryPaintUVToWorld(a, out Vector3 wa) &&
                surface.TryPaintUVToWorld(b, out Vector3 wb))
            {
                Debug.DrawLine(wa + Vector3.up * 0.1f,
                               wb + Vector3.up * 0.1f,
                               Color.yellow, 2f);
            }
        }
    }
}
