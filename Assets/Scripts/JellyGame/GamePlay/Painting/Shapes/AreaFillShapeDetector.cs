// FILEPATH: Assets/Scripts/Painting/Shapes/AreaFillShapeDetector.cs
using System.Collections;
using System.Collections.Generic;
using JellyGame.GamePlay.Abilities;
using JellyGame.GamePlay.Audio.Core;
using JellyGame.GamePlay.Managers;
using JellyGame.GamePlay.Map.Surfaces;
using JellyGame.GamePlay.Painting.Trails.Visibility;
using UnityEngine;

namespace JellyGame.GamePlay.Painting.Shapes
{
    [DisallowMultipleComponent]
    public class AreaFillShapeDetector : MonoBehaviour, IStrokeShapeDetector
    {
        public enum AreaFillMode
        {
            InstantGpuPreferred,
            ProgressiveAnimated
        }

        [Header("References")]
        [SerializeField] private List<SimplePaintSurface> paintSurfaces = new();
        [SerializeField] private RenderTextureTrailPainter painter;

        [Header("Fill Mode")]
        [SerializeField] private AreaFillMode fillMode = AreaFillMode.ProgressiveAnimated;
        [SerializeField] private bool useGpuPolygonFill = true;

        [Header("Progressive Animated Fill (budgeted)")]
        [Tooltip("Work time per frame for build+paint. Keeps FPS stable on weak PCs.")]
        [SerializeField] private float progressiveTimeBudgetMsPerFrame = 1.5f;

        [Tooltip("Hard cap on UV samples. Larger shapes increase UV step to fit this cap.")]
        [SerializeField] private int progressiveMaxTotalSamples = 160_000;

        [Tooltip("Base UV step (0..1). Smaller = better quality, more work.")]
        [SerializeField] private float progressiveBaseUvStep = 0.0022f;

        [Tooltip("How many 'rings' from outside to inside.")]
        [SerializeField] private int progressiveRingCount = 20;

        [Tooltip("If true: when the area is huge, auto-increase UV step a bit more to keep performance sane.")]
        [SerializeField] private bool progressiveDynamicQuality = true;

        [Tooltip("Extra multiplier for UV step when dynamic quality kicks in (>=1).")]
        [SerializeField] private float progressiveDynamicStepMaxMultiplier = 2.0f;

        [Header("Uneven / Liquid-like progression")]
        [SerializeField] private bool unevenFrontEnabled = true;
        [SerializeField, Range(0f, 1f)] private float unevenFrontStrength = 0.55f;
        [SerializeField] private float unevenFrontNoiseScale = 6.0f;

        [SerializeField] private bool unevenFingersEnabled = true;
        [SerializeField] private float unevenFingerCount = 4.0f;
        [SerializeField, Range(0f, 1f)] private float unevenFingerStrength = 0.55f;
        [SerializeField] private float unevenFingerWidth = 0.35f;
        [SerializeField] private int unevenSeed = 1337;

        [Header("Liquid Solid Look (per-stamp goo)")]
        [SerializeField] private bool liquidEnabled = true;
        [SerializeField] private float liquidBaseSizeMultiplier = 1.0f;
        [SerializeField, Range(0f, 1f)] private float liquidSizeVariance = 0.55f;
        [SerializeField] private float liquidJitterRadiusUv = 0.0045f;
        [SerializeField] private float liquidNoiseScale = 40f;
        [SerializeField] private float liquidFlowStrength = 1.2f;

        [Tooltip("Smear steps are the most expensive part. Keep these modest for weak PCs.")]
        [SerializeField] private int liquidSmearStepsMin = 1;
        [SerializeField] private int liquidSmearStepsMax = 3;

        [SerializeField] private int liquidExtraSplatsMin = 1;
        [SerializeField] private int liquidExtraSplatsMax = 2;

        [SerializeField] private int liquidSeed = 1337;

        [Header("Fallback (when GPU triangulation fails)")]
        [SerializeField] private bool fallbackToCpuUvFill = true;
        [SerializeField] private int fallbackMaxPaintsPerFrame = 2500;
        [SerializeField] private int fallbackMaxTotalSamples = 220_000;
        [SerializeField] private float fallbackBaseUvStep = 0.0020f;

        [Header("Multi-Surface")]
        [Tooltip("If true, clip polygons to each surface's UV bounds. If false, use original polygon for all surfaces.")]
        [SerializeField] private bool clipPolygonToSurfaceBounds = true;

        [Header("Debug")]
        [SerializeField] private bool debugPolygon = false;
        [SerializeField] private bool debugFillFailures = true;
        [SerializeField] private bool debugMultiSurface = false;

        private Coroutine _fillRoutine;

        // Data structure to hold per-surface fill information
        private struct SurfaceFillData
        {
            public SimplePaintSurface surface;
            public List<Vector2> uvPoly;
            public List<Vector2> localPolyXZ;
            public Bounds localBounds;
        }

        public bool TryHandleShape(StrokeLoopSegment seg)
        {
            if (paintSurfaces == null || paintSurfaces.Count == 0 || painter == null || seg.history == null)
                return false;

            // Build world-space polygon from stroke history
            List<Vector3> worldPolyPoints = new List<Vector3>();
            for (int i = seg.startIndex; i <= seg.endIndexInclusive; i++)
            {
                worldPolyPoints.Add(seg.history[i].WorldPos);
            }

            if (worldPolyPoints.Count < 3)
                return false;

            // Collect fill data for each surface that intersects with the polygon
            List<SurfaceFillData> surfaceFillDatas = new List<SurfaceFillData>();

            foreach (var surface in paintSurfaces)
            {
                if (surface == null)
                    continue;

                SurfaceFillData? fillData = BuildSurfaceFillData(surface, worldPolyPoints);
                if (fillData.HasValue)
                {
                    surfaceFillDatas.Add(fillData.Value);
                }
            }

            if (surfaceFillDatas.Count == 0)
            {
                if (debugFillFailures)
                    Debug.LogWarning("[AreaFill] No surfaces had valid fill data from the closed shape.", this);
                return false;
            }

            if (debugMultiSurface)
                Debug.Log($"[AreaFill] Shape closed across {surfaceFillDatas.Count} surface(s).", this);

            // Play sound once for the whole fill operation
            if (SoundManager.Instance != null)
                SoundManager.Instance.PlaySound("CloseArea", this.transform);

            // Notify ability manager with the first surface's data (for zone spawning)
            if (PlayerAbilityManager.Instance != null && surfaceFillDatas.Count > 0)
            {
                var firstData = surfaceFillDatas[0];
                PlayerAbilityManager.Instance.OnAreaFilled(firstData.surface, firstData.localPolyXZ, firstData.localBounds);
            }

            // NEW: Trigger a single "AreaClosed" event (once per closure)
            {
                var firstData = surfaceFillDatas[0];
                var evt = new EventManager.AreaClosedEventData
                {
                    source = this,
                    surfaceTransform = firstData.surface != null ? firstData.surface.transform : null,
                    localBounds = firstData.localBounds
                };
                EventManager.TriggerEvent(EventManager.GameEvent.AreaClosed, evt);
            }

            // Stop any existing fill routine
            if (_fillRoutine != null)
            {
                StopCoroutine(_fillRoutine);
                _fillRoutine = null;
            }

            // Start filling all surfaces
            if (fillMode == AreaFillMode.ProgressiveAnimated)
            {
                _fillRoutine = StartCoroutine(FillAllSurfacesProgressiveAsync(surfaceFillDatas));
            }
            else
            {
                _fillRoutine = StartCoroutine(FillAllSurfacesInstantAsync(surfaceFillDatas));
            }

            return true;
        }


        /// <summary>
        /// Build fill data for a single surface from world-space polygon points.
        /// Returns null if the polygon doesn't intersect this surface or is invalid.
        /// </summary>
        private SurfaceFillData? BuildSurfaceFillData(SimplePaintSurface surface, List<Vector3> worldPolyPoints)
        {
            // Build polygon in surface LOCAL XZ
            List<Vector2> localPolyXZ = new List<Vector2>();

            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;

            for (int i = 0; i < worldPolyPoints.Count; i++)
            {
                Vector3 worldPos = worldPolyPoints[i];
                Vector3 localPos = surface.transform.InverseTransformPoint(worldPos);

                localPolyXZ.Add(new Vector2(localPos.x, localPos.z));

                if (localPos.x < minX) minX = localPos.x;
                if (localPos.x > maxX) maxX = localPos.x;
                if (localPos.z < minZ) minZ = localPos.z;
                if (localPos.z > maxZ) maxZ = localPos.z;
            }

            if (localPolyXZ.Count < 3)
                return null;

            // Ensure CCW winding
            if (IsClockwise(localPolyXZ))
            {
                localPolyXZ.Reverse();
                if (debugPolygon) Debug.Log($"[AreaFill] Reversed polygon winding to CCW for surface {surface.name}.");
            }

            // Convert local XZ -> UV polygon
            List<Vector2> uvPoly = new List<Vector2>(localPolyXZ.Count);
            for (int i = 0; i < localPolyXZ.Count; i++)
            {
                Vector2 xz = localPolyXZ[i];
                if (surface.TryLocalToPaintUV(new Vector3(xz.x, 0f, xz.y), out Vector2 uv))
                    uvPoly.Add(uv);
            }

            if (uvPoly.Count < 3)
            {
                if (debugFillFailures)
                    Debug.LogWarning($"[AreaFill] UV polygon too small for surface {surface.name}.", this);
                return null;
            }

            // Optionally clip UV polygon to valid UV range [0,1]
            List<Vector2> finalUvPoly = uvPoly;
            
            if (clipPolygonToSurfaceBounds)
            {
                List<Vector2> clippedUvPoly = ClipPolygonToUnitSquare(uvPoly);

                if (clippedUvPoly == null || clippedUvPoly.Count < 3)
                {
                    // Check if polygon completely outside or just clipping failed
                    if (!PolygonIntersectsUnitSquare(uvPoly))
                    {
                        if (debugMultiSurface)
                            Debug.Log($"[AreaFill] Polygon completely outside surface {surface.name} UV bounds.", this);
                        return null;
                    }
                    // Clipping failed but polygon intersects - use original (will be clamped during fill)
                    if (debugPolygon)
                        Debug.Log($"[AreaFill] Clipping failed for surface {surface.name}, using original polygon.", this);
                }
                else
                {
                    finalUvPoly = clippedUvPoly;
                }
            }

            if (debugPolygon)
                Debug.Log($"[AreaFill] Surface {surface.name}: UV points={finalUvPoly.Count}", this);

            Bounds localBounds = new Bounds(
                new Vector3((minX + maxX) * 0.5f, 0f, (minZ + maxZ) * 0.5f),
                new Vector3(Mathf.Max(0.001f, maxX - minX), 0.5f, Mathf.Max(0.001f, maxZ - minZ))
            );

            return new SurfaceFillData
            {
                surface = surface,
                uvPoly = finalUvPoly,
                localPolyXZ = localPolyXZ,
                localBounds = localBounds
            };
        }

        /// <summary>
        /// Check if any part of the polygon intersects or is inside the unit square [0,1] x [0,1].
        /// </summary>
        private bool PolygonIntersectsUnitSquare(List<Vector2> polygon)
        {
            if (polygon == null || polygon.Count < 3)
                return false;

            // Check if any vertex is inside [0,1]
            foreach (var p in polygon)
            {
                if (p.x >= 0f && p.x <= 1f && p.y >= 0f && p.y <= 1f)
                    return true;
            }

            // Check if any edge intersects the unit square
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % polygon.Count];

                if (LineIntersectsUnitSquare(a, b))
                    return true;
            }

            // Check if unit square is completely inside the polygon
            // (test center point of unit square)
            if (PointInPolygon(new Vector2(0.5f, 0.5f), polygon))
                return true;

            return false;
        }

        private bool LineIntersectsUnitSquare(Vector2 a, Vector2 b)
        {
            // Check intersection with each edge of unit square
            // Left edge
            if (LinesIntersect(a, b, new Vector2(0, 0), new Vector2(0, 1))) return true;
            // Right edge
            if (LinesIntersect(a, b, new Vector2(1, 0), new Vector2(1, 1))) return true;
            // Bottom edge
            if (LinesIntersect(a, b, new Vector2(0, 0), new Vector2(1, 0))) return true;
            // Top edge
            if (LinesIntersect(a, b, new Vector2(0, 1), new Vector2(1, 1))) return true;

            return false;
        }

        private bool LinesIntersect(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2)
        {
            Vector2 d1 = a2 - a1;
            Vector2 d2 = b2 - b1;

            float cross = d1.x * d2.y - d1.y * d2.x;
            if (Mathf.Abs(cross) < 1e-10f)
                return false;

            Vector2 d3 = b1 - a1;
            float t = (d3.x * d2.y - d3.y * d2.x) / cross;
            float u = (d3.x * d1.y - d3.y * d1.x) / cross;

            return t >= 0f && t <= 1f && u >= 0f && u <= 1f;
        }

        private bool PointInPolygon(Vector2 point, List<Vector2> polygon)
        {
            bool inside = false;
            int j = polygon.Count - 1;

            for (int i = 0; i < polygon.Count; i++)
            {
                if (((polygon[i].y > point.y) != (polygon[j].y > point.y)) &&
                    (point.x < (polygon[j].x - polygon[i].x) * (point.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x))
                {
                    inside = !inside;
                }
                j = i;
            }

            return inside;
        }

        /// <summary>
        /// Clip a polygon to the unit square [0,1] x [0,1] using Sutherland-Hodgman algorithm.
        /// </summary>
        private List<Vector2> ClipPolygonToUnitSquare(List<Vector2> polygon)
        {
            if (polygon == null || polygon.Count < 3)
                return null;

            List<Vector2> output = new List<Vector2>(polygon);

            // Clip against each edge of the unit square
            // Left edge (x = 0)
            output = ClipPolygonAgainstEdge(output, new Vector2(0, 0), new Vector2(0, 1));
            if (output == null || output.Count < 3) return null;

            // Right edge (x = 1)
            output = ClipPolygonAgainstEdge(output, new Vector2(1, 1), new Vector2(1, 0));
            if (output == null || output.Count < 3) return null;

            // Bottom edge (y = 0)
            output = ClipPolygonAgainstEdge(output, new Vector2(1, 0), new Vector2(0, 0));
            if (output == null || output.Count < 3) return null;

            // Top edge (y = 1)
            output = ClipPolygonAgainstEdge(output, new Vector2(0, 1), new Vector2(1, 1));
            if (output == null || output.Count < 3) return null;

            return output;
        }

        /// <summary>
        /// Clip polygon against a single edge defined by two points (edge goes from p1 to p2, inside is to the left).
        /// </summary>
        private List<Vector2> ClipPolygonAgainstEdge(List<Vector2> polygon, Vector2 edgeP1, Vector2 edgeP2)
        {
            if (polygon == null || polygon.Count < 3)
                return null;

            List<Vector2> output = new List<Vector2>();
            Vector2 edgeDir = edgeP2 - edgeP1;
            Vector2 edgeNormal = new Vector2(-edgeDir.y, edgeDir.x); // Points inward (left of edge direction)

            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 current = polygon[i];
                Vector2 next = polygon[(i + 1) % polygon.Count];

                float currentDot = Vector2.Dot(current - edgeP1, edgeNormal);
                float nextDot = Vector2.Dot(next - edgeP1, edgeNormal);

                bool currentInside = currentDot >= -1e-6f;
                bool nextInside = nextDot >= -1e-6f;

                if (currentInside)
                {
                    output.Add(current);

                    if (!nextInside)
                    {
                        // Exiting: add intersection
                        Vector2? intersection = LineEdgeIntersection(current, next, edgeP1, edgeP2);
                        if (intersection.HasValue)
                            output.Add(intersection.Value);
                    }
                }
                else if (nextInside)
                {
                    // Entering: add intersection
                    Vector2? intersection = LineEdgeIntersection(current, next, edgeP1, edgeP2);
                    if (intersection.HasValue)
                        output.Add(intersection.Value);
                }
            }

            return output.Count >= 3 ? output : null;
        }

        /// <summary>
        /// Find intersection point between line segment (p1, p2) and infinite line through (e1, e2).
        /// </summary>
        private Vector2? LineEdgeIntersection(Vector2 p1, Vector2 p2, Vector2 e1, Vector2 e2)
        {
            Vector2 d1 = p2 - p1;
            Vector2 d2 = e2 - e1;

            float cross = d1.x * d2.y - d1.y * d2.x;
            if (Mathf.Abs(cross) < 1e-10f)
                return null; // Parallel

            Vector2 d3 = e1 - p1;
            float t = (d3.x * d2.y - d3.y * d2.x) / cross;

            if (t < 0f || t > 1f)
                return null; // Intersection outside segment

            return p1 + d1 * t;
        }

        // ================== Multi-surface fill routines ==================

        private IEnumerator FillAllSurfacesInstantAsync(List<SurfaceFillData> surfaceFillDatas)
        {
            Debug.Log("FillAllSurfacesInstantAsync");
            foreach (var fillData in surfaceFillDatas)
            {
                if (useGpuPolygonFill)
                {
                    bool ok = painter.FillPolygonUV(fillData.surface, fillData.uvPoly);
                    if (ok)
                    {
                        if (debugMultiSurface)
                            Debug.Log($"[AreaFill] GPU fill succeeded for surface {fillData.surface.name}.", this);
                        continue;
                    }

                    if (debugFillFailures)
                        Debug.LogWarning($"[AreaFill] GPU fill failed for surface {fillData.surface.name} -> fallback={fallbackToCpuUvFill}.", this);
                }

                if (fallbackToCpuUvFill)
                {
                    yield return StartCoroutine(FillUvScanlineAsync(fillData.surface, fillData.uvPoly));
                }
            }

            _fillRoutine = null;
        }

        private IEnumerator FillAllSurfacesProgressiveAsync(List<SurfaceFillData> surfaceFillDatas)
        {
            Debug.Log("FillAllSurfacesProgressiveAsync");
            // Create parallel fill coroutines for all surfaces
            List<IEnumerator> fillEnumerators = new List<IEnumerator>();

            foreach (var fillData in surfaceFillDatas)
            {
                fillEnumerators.Add(CreateProgressiveFillEnumerator(fillData.surface, fillData.uvPoly));
            }

            if (fillEnumerators.Count == 0)
            {
                _fillRoutine = null;
                yield break;
            }

            // Run all fills in parallel by advancing each one per frame
            // This distributes the time budget across all surfaces
            float budgetPerSurface = progressiveTimeBudgetMsPerFrame / Mathf.Max(1, fillEnumerators.Count);
            List<bool> completed = new List<bool>(fillEnumerators.Count);
            for (int i = 0; i < fillEnumerators.Count; i++)
                completed.Add(false);

            while (true)
            {
                bool anyActive = false;

                for (int i = 0; i < fillEnumerators.Count; i++)
                {
                    if (completed[i])
                        continue;

                    float frameStart = Time.realtimeSinceStartup;
                    float budgetSec = budgetPerSurface / 1000f;

                    // Advance this surface's fill until it yields or completes
                    while ((Time.realtimeSinceStartup - frameStart) < budgetSec)
                    {
                        if (!fillEnumerators[i].MoveNext())
                        {
                            completed[i] = true;
                            if (debugMultiSurface)
                                Debug.Log($"[AreaFill] Progressive fill completed for surface {i}.", this);
                            break;
                        }
                    }

                    if (!completed[i])
                        anyActive = true;
                }

                if (!anyActive)
                    break;

                yield return null;
            }

            _fillRoutine = null;
        }

        private IEnumerator CreateProgressiveFillEnumerator(SimplePaintSurface surface, List<Vector2> uvPoly)
        {
            return FillUvProgressiveOutsideIn_InterleavedAsync_ForSurface(surface, uvPoly);
        }

        // ================== Progressive fill for a single surface ==================

        private IEnumerator FillUvProgressiveOutsideIn_InterleavedAsync_ForSurface(SimplePaintSurface surface, List<Vector2> uvPoly)
        {
            // UV bounds + centroid
            float minU = 1f, maxU = 0f, minV = 1f, maxV = 0f;
            Vector2 centroid = Vector2.zero;

            for (int i = 0; i < uvPoly.Count; i++)
            {
                Vector2 p = uvPoly[i];
                if (p.x < minU) minU = p.x;
                if (p.x > maxU) maxU = p.x;
                if (p.y < minV) minV = p.y;
                if (p.y > maxV) maxV = p.y;
                centroid += p;
            }
            centroid /= Mathf.Max(1, uvPoly.Count);

            float width = Mathf.Max(1e-6f, maxU - minU);
            float height = Mathf.Max(1e-6f, maxV - minV);

            // Step sizing (cap samples)
            float step = Mathf.Max(1e-6f, progressiveBaseUvStep);
            float estSamples = (width / step) * (height / step);

            if (estSamples > progressiveMaxTotalSamples)
            {
                float ratio = estSamples / Mathf.Max(1f, progressiveMaxTotalSamples);
                step *= Mathf.Sqrt(ratio);
                step = Mathf.Clamp(step, progressiveBaseUvStep, progressiveBaseUvStep * 10f);
            }

            if (progressiveDynamicQuality)
            {
                float est2 = (width / step) * (height / step);
                if (est2 > progressiveMaxTotalSamples * 0.95f)
                {
                    float mul = Mathf.Clamp(progressiveDynamicStepMaxMultiplier, 1f, 6f);
                    step *= mul;
                    step = Mathf.Clamp(step, progressiveBaseUvStep, progressiveBaseUvStep * 12f);
                }
            }

            int rings = Mathf.Clamp(progressiveRingCount, 6, 128);

            List<Vector2>[] buckets = new List<Vector2>[rings];
            for (int i = 0; i < rings; i++)
                buckets[i] = new List<Vector2>(1024);

            // radius normalization
            Vector2 corner0 = new Vector2(minU, minV);
            Vector2 corner1 = new Vector2(minU, maxV);
            Vector2 corner2 = new Vector2(maxU, minV);
            Vector2 corner3 = new Vector2(maxU, maxV);

            float maxR = Mathf.Max(
                Vector2.Distance(centroid, corner0),
                Vector2.Distance(centroid, corner1),
                Vector2.Distance(centroid, corner2),
                Vector2.Distance(centroid, corner3)
            );
            maxR = Mathf.Max(1e-6f, maxR);

            float budgetSec = Mathf.Max(0.0001f, progressiveTimeBudgetMsPerFrame / 1000f);

            // Stable rotation for finger directions
            float fingerRot = Hash01(centroid, unevenSeed + 999) * Mathf.PI * 2f;

            // We'll paint from outside->inside as soon as we have points
            int paintRing = rings - 1;

            List<float> intersections = new List<float>(64);

            // Build scanlines, but time-slice and paint within the same budget
            float v = minV;
            while (v <= maxV)
            {
                float frameStart = Time.realtimeSinceStartup;

                // Do build work until budget is about half used, then paint with the remainder
                while (v <= maxV && (Time.realtimeSinceStartup - frameStart) < budgetSec * 0.55f)
                {
                    intersections.Clear();
                    ComputeScanlineIntersections(v, uvPoly, intersections);

                    if (intersections.Count >= 2)
                    {
                        intersections.Sort();

                        for (int i = 0; i + 1 < intersections.Count; i += 2)
                        {
                            float u0 = intersections[i];
                            float u1 = intersections[i + 1];
                            if (u1 < u0) { float t = u0; u0 = u1; u1 = t; }

                            for (float u = u0; u <= u1; u += step)
                            {
                                Vector2 uv = new Vector2(u, v);

                                float r = Vector2.Distance(uv, centroid);
                                float tNorm = Mathf.Clamp01(r / maxR);

                                float tWarped = tNorm;

                                if (unevenFrontEnabled)
                                {
                                    float ns = Mathf.Max(0.0001f, unevenFrontNoiseScale);
                                    float n = Mathf.PerlinNoise(
                                        uv.x * ns + unevenSeed * 0.01f,
                                        uv.y * ns + unevenSeed * 0.02f
                                    );
                                    tWarped += (n * 2f - 1f) * unevenFrontStrength * 0.18f;

                                    if (unevenFingersEnabled)
                                    {
                                        Vector2 d = uv - centroid;
                                        float ang = Mathf.Atan2(d.y, d.x) + fingerRot;
                                        float stripes = Mathf.Sin(ang * Mathf.Max(0.5f, unevenFingerCount));
                                        float ridge = Mathf.Abs(stripes);
                                        ridge = Mathf.Pow(Mathf.Clamp01(1f - ridge), Mathf.Lerp(1f, 6f, Mathf.Clamp01(unevenFingerWidth)));
                                        tWarped -= ridge * unevenFingerStrength * 0.22f;
                                    }

                                    tWarped = Mathf.Clamp01(tWarped);
                                }

                                int bucket = Mathf.Clamp(Mathf.FloorToInt(tWarped * (rings - 1)), 0, rings - 1);
                                buckets[bucket].Add(uv);

                                if ((Time.realtimeSinceStartup - frameStart) >= budgetSec * 0.55f)
                                    break;
                            }

                            if ((Time.realtimeSinceStartup - frameStart) >= budgetSec * 0.55f)
                                break;
                        }
                    }

                    v += step;

                    if ((Time.realtimeSinceStartup - frameStart) >= budgetSec * 0.55f)
                        break;
                }

                // Paint whatever we already have (outside -> inside) for the rest of the budget
                PaintAvailableOuterBucketsWithinBudget(surface, buckets, ref paintRing, frameStart, budgetSec);

                yield return null;
            }

            // Build is done; finish painting remaining points (still budgeted)
            while (paintRing >= 0)
            {
                float frameStart = Time.realtimeSinceStartup;
                PaintAvailableOuterBucketsWithinBudget(surface, buckets, ref paintRing, frameStart, budgetSec);
                yield return null;
            }
        }

        private void PaintAvailableOuterBucketsWithinBudget(
            SimplePaintSurface surface,
            List<Vector2>[] buckets,
            ref int paintRing,
            float frameStart,
            float budgetSec)
        {
            bool paintedOne = false;

            while (paintRing >= 0 && (Time.realtimeSinceStartup - frameStart) < budgetSec)
            {
                var list = buckets[paintRing];

                if (list.Count == 0)
                {
                    paintRing--;
                    continue;
                }

                int last = list.Count - 1;
                Vector2 uv = list[last];
                list.RemoveAt(last);

                PaintLiquidSolidAtUV(surface, uv);
                paintedOne = true;

                if (paintedOne && (Time.realtimeSinceStartup - frameStart) >= budgetSec)
                    break;
            }

            if (!paintedOne)
            {
                int r = paintRing;
                while (r >= 0)
                {
                    var list = buckets[r];
                    if (list.Count > 0)
                    {
                        int last = list.Count - 1;
                        Vector2 uv = list[last];
                        list.RemoveAt(last);

                        PaintLiquidSolidAtUV(surface, uv);
                        break;
                    }
                    r--;
                }
            }
        }

        // ----------------- Liquid stamping -----------------

        private void PaintLiquidSolidAtUV(SimplePaintSurface surface, Vector2 uv)
        {
            if (!liquidEnabled)
            {
                painter.PaintFillAtUV(surface, uv);
                return;
            }

            float baseHalf = Mathf.Max(0.00001f, painter.DefaultHalfSizeUV * Mathf.Max(0.05f, liquidBaseSizeMultiplier));

            float r0 = Hash01(uv, liquidSeed);
            float r1 = Hash01(uv * 1.73f, liquidSeed + 11);
            float r2 = Hash01(uv * 2.41f, liquidSeed + 37);

            float sizeMul = Mathf.Lerp(1f - liquidSizeVariance, 1f + liquidSizeVariance, r0);
            float half0 = baseHalf * sizeMul;

            float nA = Mathf.PerlinNoise(uv.x * liquidNoiseScale, uv.y * liquidNoiseScale);
            float nB = Mathf.PerlinNoise((uv.x + 9.13f) * liquidNoiseScale, (uv.y - 5.27f) * liquidNoiseScale);

            float angle = (nA * 2f - 1f) * Mathf.PI * liquidFlowStrength + (r1 * 2f - 1f) * 0.55f;
            Vector2 flowDir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)).normalized;

            painter.PaintFillAtUV(surface, uv, half0, 1f);

            int extraMin = Mathf.Max(0, liquidExtraSplatsMin);
            int extraMax = Mathf.Max(extraMin, liquidExtraSplatsMax);
            int extraSplats = (extraMax == extraMin) ? extraMin : extraMin + Mathf.FloorToInt(r2 * (extraMax - extraMin + 1));

            float jitterR = Mathf.Max(0f, liquidJitterRadiusUv);

            for (int i = 0; i < extraSplats; i++)
            {
                float ri = Hash01(uv * (i + 2.19f), liquidSeed + 101 + i);
                float ai = Hash01(uv * (i + 5.61f), liquidSeed + 303 + i) * Mathf.PI * 2f;

                Vector2 swirl = new Vector2(Mathf.Cos(ai), Mathf.Sin(ai));
                Vector2 dir = Vector2.Lerp(swirl, flowDir, 0.55f).normalized;

                float rad = jitterR * Mathf.Lerp(0.2f, 1.0f, ri);
                Vector2 uv2 = Clamp01(uv + dir * rad);

                float h = baseHalf * Mathf.Lerp(0.55f, 0.95f, ri);
                float op = Mathf.Lerp(0.25f, 0.65f, ri);

                painter.PaintFillAtUV(surface, uv2, h, op);
            }

            int smearMin = Mathf.Max(0, liquidSmearStepsMin);
            int smearMax = Mathf.Max(smearMin, liquidSmearStepsMax);
            int smearSteps = (smearMax == smearMin)
                ? smearMin
                : smearMin + Mathf.FloorToInt(Hash01(uv * 3.07f, liquidSeed + 71) * (smearMax - smearMin + 1));

            if (smearSteps <= 0)
                return;

            float stepUv = Mathf.Max(0.00001f, baseHalf * Mathf.Lerp(0.75f, 2.2f, nB) * liquidFlowStrength);
            Vector2 pos = uv;

            for (int s = 1; s <= smearSteps; s++)
            {
                float t = (float)s / Mathf.Max(1, smearSteps);

                float wob = (Hash01(uv * (19.7f + s), liquidSeed + 500 + s) * 2f - 1f);
                Vector2 wobDir = new Vector2(-flowDir.y, flowDir.x);

                pos = pos + flowDir * stepUv + wobDir * (wob * stepUv * 0.35f);

                Vector2 uvS = Clamp01(pos);

                float size = Mathf.Lerp(half0 * 0.95f, baseHalf * 0.35f, t);
                float op = Mathf.Lerp(0.55f, 0.08f, t);

                painter.PaintFillAtUV(surface, uvS, size, op);
            }
        }

        private static Vector2 Clamp01(Vector2 v)
        {
            v.x = Mathf.Clamp01(v.x);
            v.y = Mathf.Clamp01(v.y);
            return v;
        }

        private static float Hash01(Vector2 uv, int seed)
        {
            float x = uv.x * 127.1f + uv.y * 311.7f + seed * 17.0f;
            float y = uv.x * 269.5f + uv.y * 183.3f + seed * 31.0f;
            float s = Mathf.Sin(x) * 43758.5453f + Mathf.Sin(y) * 24634.6345f;
            return s - Mathf.Floor(s);
        }

        // ----------------- Fallback scanline -----------------

        private IEnumerator FillUvScanlineAsync(SimplePaintSurface surface, List<Vector2> uvPoly)
        {
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
                        painter.PaintFillAtUV(surface, new Vector2(u, v));

                        paintsThisFrame++;
                        if (paintsThisFrame >= fallbackMaxPaintsPerFrame)
                        {
                            paintsThisFrame = 0;
                            yield return null;
                        }
                    }
                }
            }
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

                if (v < vMin || v >= vMax)
                    continue;

                float t = (v - a.y) / (b.y - a.y);
                float u = Mathf.Lerp(a.x, b.x, t);
                outUs.Add(u);
            }
        }

        private bool IsClockwise(List<Vector2> poly)
        {
            float signedArea = 0f;
            for (int i = 0; i < poly.Count; i++)
            {
                Vector2 p1 = poly[i];
                Vector2 p2 = poly[(i + 1) % poly.Count];
                signedArea += (p2.x - p1.x) * (p2.y + p1.y);
            }
            return signedArea > 0f;
        }

        private void OnValidate()
        {
            progressiveTimeBudgetMsPerFrame = Mathf.Clamp(progressiveTimeBudgetMsPerFrame, 0.2f, 8f);
            progressiveMaxTotalSamples = Mathf.Max(10_000, progressiveMaxTotalSamples);
            progressiveBaseUvStep = Mathf.Clamp(progressiveBaseUvStep, 0.0004f, 0.02f);
            progressiveRingCount = Mathf.Clamp(progressiveRingCount, 6, 128);

            progressiveDynamicStepMaxMultiplier = Mathf.Clamp(progressiveDynamicStepMaxMultiplier, 1f, 6f);

            unevenFrontNoiseScale = Mathf.Clamp(unevenFrontNoiseScale, 0.5f, 40f);
            unevenFingerCount = Mathf.Clamp(unevenFingerCount, 1f, 16f);
            unevenFingerWidth = Mathf.Clamp(unevenFingerWidth, 0.05f, 0.9f);

            liquidBaseSizeMultiplier = Mathf.Clamp(liquidBaseSizeMultiplier, 0.25f, 5f);
            liquidJitterRadiusUv = Mathf.Clamp(liquidJitterRadiusUv, 0f, 0.03f);
            liquidNoiseScale = Mathf.Clamp(liquidNoiseScale, 1f, 250f);
            liquidFlowStrength = Mathf.Clamp(liquidFlowStrength, 0f, 4f);
            liquidSmearStepsMin = Mathf.Clamp(liquidSmearStepsMin, 0, 30);
            liquidSmearStepsMax = Mathf.Clamp(liquidSmearStepsMax, liquidSmearStepsMin, 40);
            liquidExtraSplatsMin = Mathf.Clamp(liquidExtraSplatsMin, 0, 30);
            liquidExtraSplatsMax = Mathf.Clamp(liquidExtraSplatsMax, liquidExtraSplatsMin, 40);

            fallbackMaxPaintsPerFrame = Mathf.Max(200, fallbackMaxPaintsPerFrame);
            fallbackMaxTotalSamples = Mathf.Max(10_000, fallbackMaxTotalSamples);
            fallbackBaseUvStep = Mathf.Clamp(fallbackBaseUvStep, 0.0004f, 0.02f);
        }
    }
}