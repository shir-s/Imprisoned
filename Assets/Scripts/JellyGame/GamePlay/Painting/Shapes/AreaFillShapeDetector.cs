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

        [Header("Fill Mode")]
        [SerializeField] private bool useGpuPolygonFill = true;

        [Header("Fallback (when GPU triangulation fails)")]
        [Tooltip("If GPU fill fails (bad polygon), use a CPU scanline fill in UV space (no raycasts).")]
        [SerializeField] private bool fallbackToCpuUvFill = true;

        [Tooltip("Max paint calls per frame for the fallback fill.")]
        [SerializeField] private int fallbackMaxPaintsPerFrame = 4000;

        [Tooltip("Hard cap on UV samples. Larger shapes increase UV step to fit this cap.")]
        [SerializeField] private int fallbackMaxTotalSamples = 250_000;

        [Tooltip("Base UV step (0..1). Smaller = better quality, slower fallback.")]
        [SerializeField] private float fallbackBaseUvStep = 0.0015f;

        [Header("Debug")]
        [SerializeField] private bool debugPolygon = false;
        [SerializeField] private bool debugFillFailures = true;

        private Coroutine _fallbackRoutine;

        public bool TryHandleShape(StrokeLoopSegment seg)
        {
            if (paintSurfaces == null || paintSurfaces.Count == 0 || painter == null || seg.history == null)
                return false;

            SimplePaintSurface referenceSurface = paintSurfaces[0];
            if (referenceSurface == null)
                return false;

            // Build polygon in surface LOCAL XZ
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
                Debug.Log($"[AreaFill] Loop closed. Local bounds X:{minX:F2}->{maxX:F2} Z:{minZ:F2}->{maxZ:F2}", this);

            // Start damage immediately (no delay)
            if (PlayerAbilityManager.Instance != null)
            {
                Bounds localBounds = new Bounds(
                    new Vector3((minX + maxX) * 0.5f, 0f, (minZ + maxZ) * 0.5f),
                    new Vector3(Mathf.Max(0.001f, maxX - minX), 0.01f, Mathf.Max(0.001f, maxZ - minZ))
                );

                PlayerAbilityManager.Instance.OnAreaFilled(referenceSurface, localPolyXZ, localBounds);
            }

            // Convert local XZ -> UV polygon using the surface mapping (no world transform)
            List<Vector2> uvPoly = new List<Vector2>(localPolyXZ.Count);
            for (int i = 0; i < localPolyXZ.Count; i++)
            {
                Vector2 xz = localPolyXZ[i];
                if (referenceSurface.TryLocalToPaintUV(new Vector3(xz.x, 0f, xz.y), out Vector2 uv))
                    uvPoly.Add(uv);
            }

            if (uvPoly.Count < 3)
            {
                if (debugFillFailures)
                    Debug.LogWarning("[AreaFill] UV polygon too small (mapping failed / collapsed).", this);
                return true;
            }

            // Try GPU fill
            if (useGpuPolygonFill)
            {
                bool ok = painter.FillPolygonUV(referenceSurface, uvPoly);
                if (ok)
                    return true;

                if (debugFillFailures)
                    Debug.LogWarning($"[AreaFill] GPU fill failed -> fallback={fallbackToCpuUvFill}. uvPoints={uvPoly.Count}", this);
            }

            // Fallback CPU fill (UV scanline, no raycasts)
            if (fallbackToCpuUvFill)
            {
                if (_fallbackRoutine != null)
                    StopCoroutine(_fallbackRoutine);

                _fallbackRoutine = StartCoroutine(FillUvScanlineAsync(referenceSurface, uvPoly));
            }

            return true;
        }

        private IEnumerator FillUvScanlineAsync(SimplePaintSurface surface, List<Vector2> uvPoly)
        {
            // Compute UV bounds
            float minU = 1f, maxU = 0f, minV = 1f, maxV = 0f;
            for (int i = 0; i < uvPoly.Count; i++)
            {
                Vector2 p = uvPoly[i];
                if (p.x < minU) minU = p.x;
                if (p.x > maxU) maxU = p.x;
                if (p.y < minV) minV = p.y;
                if (p.y > maxV) maxV = p.y;
            }

            float width = Mathf.Max(1e-6f, maxU - minU);
            float height = Mathf.Max(1e-6f, maxV - minV);

            float step = Mathf.Max(1e-6f, fallbackBaseUvStep);
            float estSamples = (width / step) * (height / step);

            if (estSamples > fallbackMaxTotalSamples)
            {
                float ratio = estSamples / Mathf.Max(1f, fallbackMaxTotalSamples);
                step *= Mathf.Sqrt(ratio);
                step = Mathf.Clamp(step, fallbackBaseUvStep, fallbackBaseUvStep * 8f);
            }

            int paintsThisFrame = 0;
            List<float> intersections = new List<float>(64);

            for (float v = minV; v <= maxV; v += step)
            {
                intersections.Clear();
                ComputeScanlineIntersections(v, uvPoly, intersections);
                if (intersections.Count < 2)
                    continue;

                intersections.Sort();

                for (int i = 0; i + 1 < intersections.Count; i += 2)
                {
                    float u0 = intersections[i];
                    float u1 = intersections[i + 1];
                    if (u1 < u0) { float t = u0; u0 = u1; u1 = t; }

                    for (float u = u0; u <= u1; u += step)
                    {
                        painter.PaintAtUV(surface, new Vector2(u, v));

                        paintsThisFrame++;
                        if (paintsThisFrame >= fallbackMaxPaintsPerFrame)
                        {
                            paintsThisFrame = 0;
                            yield return null;
                        }
                    }
                }
            }

            _fallbackRoutine = null;
        }

        private void ComputeScanlineIntersections(float v, List<Vector2> poly, List<float> outUs)
        {
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = poly[i];
                Vector2 b = poly[(i + 1) % n];

                if (Mathf.Approximately(a.y, b.y))
                    continue;

                float vMin = Mathf.Min(a.y, b.y);
                float vMax = Mathf.Max(a.y, b.y);

                // include lower, exclude upper
                if (v < vMin || v >= vMax)
                    continue;

                float t = (v - a.y) / (b.y - a.y);
                float u = Mathf.Lerp(a.x, b.x, t);
                outUs.Add(u);
            }
        }
    }
}
