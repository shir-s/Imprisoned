// FILEPATH: Assets/Scripts/Painting/Shapes/AreaFillShapeDetector.cs
using System.Collections;
using System.Collections.Generic;
using JellyGame.GamePlay.Abilities;
using JellyGame.GamePlay.Map.Surfaces;
using JellyGame.GamePlay.Painting.Trails.Visibility;
using UnityEngine;

namespace JellyGame.GamePlay.Painting.Shapes
{
    [DisallowMultipleComponent]
    public class AreaFillShapeDetector : MonoBehaviour, IStrokeShapeDetector
    {
        [Header("References")]
        [SerializeField] private List<SimplePaintSurface> paintSurfaces = new();
        [SerializeField] private RenderTextureTrailPainter painter;

        [Header("Fill sampling (LOCAL space)")]
        [Tooltip("Grid step size in local units. 0.08 is usually good.")]
        [SerializeField] private float localStepSize = 0.08f;

        [Tooltip("How far to check above/below the surface for the paint raycast.")]
        [SerializeField] private float raycastTolerance = 1.0f;

        [Header("Performance")]
        [Tooltip("Max successful paints per frame. Higher = faster fill but potential lag.")]
        [SerializeField] private int maxPaintsPerFrame = 500;

        [Header("Debug")]
        [SerializeField] private bool debugPolygon = false;
        [SerializeField] private bool debugFillPoints = false;

        private Coroutine _currentFillCoroutine;

        public bool TryHandleShape(StrokeLoopSegment seg)
        {
            if (paintSurfaces == null || paintSurfaces.Count == 0 || painter == null || seg.history == null)
                return false;

            SimplePaintSurface referenceSurface = paintSurfaces[0];
            if (referenceSurface == null) return false;

            if (_currentFillCoroutine != null)
                StopCoroutine(_currentFillCoroutine);

            // Convert polygon to SURFACE LOCAL XZ (stable, tilt-safe)
            List<Vector2> localPolyXZ = new List<Vector2>();

            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;

            for (int i = seg.startIndex; i <= seg.endIndexInclusive; i++)
            {
                Vector3 worldPos = seg.history[i].WorldPos;
                Vector3 localPos = referenceSurface.transform.InverseTransformPoint(worldPos);

                localPolyXZ.Add(new Vector2(localPos.x, localPos.z));

                if (localPos.x < minX) minX = localPos.x;
                if (localPos.x > maxX) maxX = localPos.x;
                if (localPos.z < minZ) minZ = localPos.z;
                if (localPos.z > maxZ) maxZ = localPos.z;
            }

            if (localPolyXZ.Count < 3)
                return false;

            if (debugPolygon)
                Debug.Log($"[AreaFill] Starting Local Space Fill. Bounds X:{minX:F2}->{maxX:F2} Z:{minZ:F2}->{maxZ:F2}", this);

            _currentFillCoroutine = StartCoroutine(
                FillAsyncLocal(referenceSurface, minX, maxX, minZ, maxZ, localPolyXZ)
            );

            return true;
        }

        private IEnumerator FillAsyncLocal(SimplePaintSurface surface, float minX, float maxX, float minZ, float maxZ, List<Vector2> localPolyXZ)
        {
            int paintsThisFrame = 0;
            int totalFilled = 0;

            for (float x = minX; x <= maxX; x += localStepSize)
            {
                for (float z = minZ; z <= maxZ; z += localStepSize)
                {
                    Vector2 localPoint = new Vector2(x, z);

                    if (!IsPointInPolygon2D(localPoint, localPolyXZ))
                        continue;

                    // Convert *now* to world so it matches current tilt
                    Vector3 currentWorldPos = surface.transform.TransformPoint(new Vector3(x, 0f, z));

                    if (TryPaintAtWorldPoint(currentWorldPos, out bool painted))
                    {
                        if (painted) totalFilled++;
                    }

                    paintsThisFrame++;
                    if (paintsThisFrame >= maxPaintsPerFrame)
                    {
                        paintsThisFrame = 0;
                        yield return null;
                    }
                }
            }

            if (debugPolygon)
                Debug.Log($"[AreaFill] Finished. Total points: {totalFilled}", this);

            // Notify ability system to spawn a zone matching the polygon
            if (PlayerAbilityManager.Instance != null)
            {
                Bounds localBounds = new Bounds(
                    new Vector3((minX + maxX) * 0.5f, 0f, (minZ + maxZ) * 0.5f),
                    new Vector3(Mathf.Max(0.001f, maxX - minX), 0.01f, Mathf.Max(0.001f, maxZ - minZ))
                );

                PlayerAbilityManager.Instance.OnAreaFilled(surface, localPolyXZ, localBounds);
            }

            _currentFillCoroutine = null;
        }

        private bool TryPaintAtWorldPoint(Vector3 worldPoint, out bool painted)
        {
            painted = false;

            Vector3 rayOrigin = worldPoint + (Vector3.up * raycastTolerance);
            Vector3 rayDir = Vector3.down;

            if (Physics.Raycast(rayOrigin, rayDir, out RaycastHit hit, raycastTolerance * 2f))
            {
                SimplePaintSurface surface = hit.collider.GetComponentInParent<SimplePaintSurface>();
                if (surface != null && paintSurfaces.Contains(surface))
                {
                    if (surface.TryWorldToPaintUV(hit.point, out Vector2 uv))
                    {
                        painter.PaintAtUV(surface, uv);
                        painted = true;

                        if (debugFillPoints)
                            Debug.DrawRay(hit.point, Vector3.up * 0.1f, Color.green, 0.5f);

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
                if (((poly[i].y > p.y) != (poly[j].y > p.y)) &&
                    (p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / ((poly[j].y - poly[i].y) + 1e-6f) + poly[i].x))
                {
                    inside = !inside;
                }
            }

            return inside;
        }
    }
}
