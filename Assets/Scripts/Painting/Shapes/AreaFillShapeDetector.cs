using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class AreaFillShapeDetector : MonoBehaviour, IStrokeShapeDetector
{
    [Header("References")]
    [SerializeField] private List<SimplePaintSurface> paintSurfaces = new();
    [SerializeField] private RenderTextureTrailPainter painter;

    [Header("Fill sampling (WORLD space)")]
    [Tooltip("Grid step size in world units for fill sampling")]
    [SerializeField] private float worldStepSize = 0.05f;

    [Tooltip("Maximum height above/below the average stroke height to consider for filling")]
    [SerializeField] private float fillHeightTolerance = 0.5f;

    [SerializeField] private bool debugPolygon = false;
    [SerializeField] private bool debugFillPoints = false;

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

        // 1) Build polygon in WORLD SPACE from the loop
        List<Vector3> worldPolygon = new List<Vector3>();
        float avgHeight = 0f;

        for (int i = startIndex; i <= endIndexInclusive; i++)
        {
            Vector3 worldPos = history[i].WorldPos;
            worldPolygon.Add(worldPos);
            avgHeight += worldPos.y;
        }

        if (worldPolygon.Count < 3)
            return false;

        avgHeight /= worldPolygon.Count;

        if (debugPolygon)
            DebugDrawWorldPolygon(worldPolygon);

        // 2) Compute world-space bounding box (XZ plane)
        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minZ = float.MaxValue;
        float maxZ = float.MinValue;

        foreach (var p in worldPolygon)
        {
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.z < minZ) minZ = p.z;
            if (p.z > maxZ) maxZ = p.z;
        }

        // 3) Project polygon to XZ plane for point-in-polygon test
        List<Vector2> polygonXZ = new List<Vector2>();
        foreach (var p in worldPolygon)
        {
            polygonXZ.Add(new Vector2(p.x, p.z));
        }

        // 4) Fill by iterating world-space grid
        bool anyFilled = false;
        int fillCount = 0;

        for (float x = minX; x <= maxX; x += worldStepSize)
        {
            for (float z = minZ; z <= maxZ; z += worldStepSize)
            {
                Vector2 pointXZ = new Vector2(x, z);

                // Check if this XZ point is inside the polygon
                if (!IsPointInPolygon2D(pointXZ, polygonXZ))
                    continue;

                // Construct world position at average height
                Vector3 worldPoint = new Vector3(x, avgHeight, z);

                // Find which surface this point is on and paint it
                if (TryPaintAtWorldPoint(worldPoint, out bool painted) && painted)
                {
                    anyFilled = true;
                    fillCount++;

                    if (debugFillPoints && fillCount % 10 == 0)
                    {
                        Debug.DrawRay(worldPoint, Vector3.up * 0.2f, Color.green, 2f);
                    }
                }
            }
        }

        if (debugPolygon)
            Debug.Log($"[AreaFill] Filled {fillCount} points across surfaces");

        return anyFilled;
    }

    /// <summary>
    /// Try to paint at a world point by finding which surface it's on.
    /// Returns true if we attempted painting, and out bool indicates if paint succeeded.
    /// </summary>
    private bool TryPaintAtWorldPoint(Vector3 worldPoint, out bool painted)
    {
        painted = false;

        // Try to find a surface under this world point using a short raycast
        // We raycast from slightly above to slightly below
        Vector3 rayStart = worldPoint + Vector3.up * fillHeightTolerance;
        Vector3 rayDir = Vector3.down;
        float rayDist = fillHeightTolerance * 2f;

        if (Physics.Raycast(rayStart, rayDir, out RaycastHit hit, rayDist))
        {
            // Check if this hit belongs to one of our paint surfaces
            SimplePaintSurface surface = hit.collider.GetComponentInParent<SimplePaintSurface>();

            if (surface != null && paintSurfaces.Contains(surface))
            {
                // Convert world hit point to UV on this surface
                if (surface.TryWorldToPaintUV(hit.point, out Vector2 uv))
                {
                    painter.PaintAtUV(surface, uv);
                    painted = true;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 2D point-in-polygon test (ray casting algorithm) in XZ plane
    /// </summary>
    private bool IsPointInPolygon2D(Vector2 p, List<Vector2> poly)
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

    private void DebugDrawWorldPolygon(List<Vector3> poly)
    {
        for (int i = 0; i < poly.Count; i++)
        {
            Vector3 a = poly[i];
            Vector3 b = poly[(i + 1) % poly.Count];
            Debug.DrawLine(a + Vector3.up * 0.15f, b + Vector3.up * 0.15f, Color.yellow, 3f);
        }
    }
}