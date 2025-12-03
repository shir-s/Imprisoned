// FILEPATH: Assets/Scripts/Painting/Shapes/AreaFillShapeDetector.cs
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class AreaFillShapeDetector : MonoBehaviour, IStrokeShapeDetector
{
    [Header("References")]
    [SerializeField] private SimplePaintSurface paintSurface;
    [SerializeField] private RenderTextureTrailPainter painter;

    [Header("Fill sampling (UV space)")]
    [SerializeField] private float uvStep = 0.01f;

    [SerializeField] private bool debugPolygon = false;

    public bool TryHandleShape(StrokeLoopSegment seg)
    {
        if (paintSurface == null || painter == null || seg.history == null)
            return false;

        StrokeHistory history = seg.history;
        int startIndex        = seg.startIndex;
        int endIndexInclusive = seg.endIndexInclusive;

        if (endIndexInclusive <= startIndex + 2)
            return false;

        // 1) Build polygon in UV from the loop samples.
        List<Vector2> uvPolygon = new List<Vector2>();

        for (int i = startIndex; i <= endIndexInclusive; i++)
        {
            StrokeSample sample = history[i];
            Vector3 worldPos    = sample.WorldPos;

            if (!paintSurface.TryWorldToPaintUV(worldPos, out Vector2 uv))
                continue;

            uvPolygon.Add(uv);
        }

        if (uvPolygon.Count < 3)
            return false;

        if (debugPolygon)
            DebugDrawUvPolygon(uvPolygon);

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
                    painter.PaintAtUV(paintSurface, p);
                }
            }
        }

        // Shape handled: we filled the area.
        return true;
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

    private void DebugDrawUvPolygon(List<Vector2> poly)
    {
        // Optional: only if SimplePaintSurface has a UV→world helper.
        for (int i = 0; i < poly.Count; i++)
        {
            Vector2 a = poly[i];
            Vector2 b = poly[(i + 1) % poly.Count];

            if (paintSurface.TryPaintUVToWorld(a, out Vector3 wa) &&
                paintSurface.TryPaintUVToWorld(b, out Vector3 wb))
            {
                Debug.DrawLine(wa + Vector3.up * 0.1f,
                               wb + Vector3.up * 0.1f,
                               Color.yellow, 2f);
            }
        }
    }
}
