using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class AreaFillShapeDetector : MonoBehaviour, IStrokeShapeDetector
{
    [Header("References")]
    [SerializeField] private List<SimplePaintSurface> paintSurfaces = new();
    [SerializeField] private RenderTextureTrailPainter painter;

    [Header("Fill sampling (WORLD space)")]
    [Tooltip("Grid step size in world units. Larger = faster")]
    [SerializeField] private float worldStepSize = 0.08f;

    [Tooltip("Maximum height above/below the average stroke height")]
    [SerializeField] private float fillHeightTolerance = 0.5f;

    [Header("Performance")]
    [Tooltip("Max fill points per frame. 200-400 recommended")]
    [SerializeField] private int maxPointsPerFrame = 300;

    [Tooltip("Fill over multiple frames to avoid freezing")]
    [SerializeField] private bool useAsyncFill = true;

    [Header("Debug")]
    [SerializeField] private bool debugPolygon = false;
    [SerializeField] private bool debugFillPoints = false;

    private Coroutine _currentFillCoroutine;

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

        // Stop any previous fill
        if (_currentFillCoroutine != null)
        {
            StopCoroutine(_currentFillCoroutine);
            _currentFillCoroutine = null;
        }

        // 1) Build polygon in WORLD SPACE
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

        // 2) Compute bounding box
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

        // 3) Project to XZ
        List<Vector2> polygonXZ = new List<Vector2>();
        foreach (var p in worldPolygon)
        {
            polygonXZ.Add(new Vector2(p.x, p.z));
        }

        // 4) Fill async or instant
        if (useAsyncFill)
        {
            _currentFillCoroutine = StartCoroutine(
                FillAsync(minX, maxX, minZ, maxZ, avgHeight, polygonXZ));
            return true;
        }
        else
        {
            return FillImmediate(minX, maxX, minZ, maxZ, avgHeight, polygonXZ);
        }
    }

    private IEnumerator FillAsync(float minX, float maxX, float minZ, float maxZ, 
                                   float avgHeight, List<Vector2> polygonXZ)
    {
        int fillCount = 0;
        int pointsThisFrame = 0;

        for (float x = minX; x <= maxX; x += worldStepSize)
        {
            for (float z = minZ; z <= maxZ; z += worldStepSize)
            {
                Vector2 pointXZ = new Vector2(x, z);

                if (!IsPointInPolygon2D(pointXZ, polygonXZ))
                    continue;

                Vector3 worldPoint = new Vector3(x, avgHeight, z);

                if (TryPaintAtWorldPoint(worldPoint, out bool painted) && painted)
                {
                    fillCount++;
                    
                    if (debugFillPoints && fillCount % 10 == 0)
                    {
                        Debug.DrawRay(worldPoint, Vector3.up * 0.2f, Color.green, 2f);
                    }
                }

                pointsThisFrame++;

                // Yield every maxPointsPerFrame
                if (pointsThisFrame >= maxPointsPerFrame)
                {
                    pointsThisFrame = 0;
                    yield return null;
                }
            }
        }

        if (debugPolygon)
            Debug.Log($"[AreaFill] Completed {fillCount} points");

        _currentFillCoroutine = null;
    }

    private bool FillImmediate(float minX, float maxX, float minZ, float maxZ, 
                               float avgHeight, List<Vector2> polygonXZ)
    {
        bool anyFilled = false;
        int fillCount = 0;

        for (float x = minX; x <= maxX; x += worldStepSize)
        {
            for (float z = minZ; z <= maxZ; z += worldStepSize)
            {
                Vector2 pointXZ = new Vector2(x, z);

                if (!IsPointInPolygon2D(pointXZ, polygonXZ))
                    continue;

                Vector3 worldPoint = new Vector3(x, avgHeight, z);

                if (TryPaintAtWorldPoint(worldPoint, out bool painted) && painted)
                {
                    anyFilled = true;
                    fillCount++;
                }
            }
        }

        if (debugPolygon)
            Debug.Log($"[AreaFill] Filled {fillCount} points");

        return anyFilled;
    }

    private bool TryPaintAtWorldPoint(Vector3 worldPoint, out bool painted)
    {
        painted = false;

        Vector3 rayStart = worldPoint + Vector3.up * fillHeightTolerance;
        Vector3 rayDir = Vector3.down;
        float rayDist = fillHeightTolerance * 2f;

        if (Physics.Raycast(rayStart, rayDir, out RaycastHit hit, rayDist))
        {
            SimplePaintSurface surface = hit.collider.GetComponentInParent<SimplePaintSurface>();

            if (surface != null && paintSurfaces.Contains(surface))
            {
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

    private bool IsPointInPolygon2D(Vector2 p, List<Vector2> poly)
    {
        bool inside = false;
        int count = poly.Count;

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