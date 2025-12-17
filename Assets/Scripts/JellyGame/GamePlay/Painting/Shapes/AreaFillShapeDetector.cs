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
        [Tooltip("Max CPU/GPU work time per frame for the progressive fill. Lower = safer FPS on weak PCs.")]
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

        [Header("Liquid Solid Look")]
        [SerializeField] private bool liquidEnabled = true;

        [SerializeField] private float liquidBaseSizeMultiplier = 1.0f;

        [SerializeField, Range(0f, 1f)]
        private float liquidSizeVariance = 0.55f;

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

        [Header("Debug")]
        [SerializeField] private bool debugPolygon = false;
        [SerializeField] private bool debugFillFailures = true;

        private Coroutine _fillRoutine;

        public bool TryHandleShape(StrokeLoopSegment seg)
        {
            if (paintSurfaces == null || paintSurfaces.Count == 0 || painter == null || seg.history == null)
                return false;

            SimplePaintSurface referenceSurface = paintSurfaces[0];
            if (referenceSurface == null)
                return false;

            // 1) Build polygon in surface LOCAL XZ
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

            // Force CCW winding
            if (IsClockwise(localPolyXZ))
            {
                localPolyXZ.Reverse();
                if (debugPolygon) Debug.Log("[AreaFill] Reversed polygon winding to CCW.");
            }

            if (debugPolygon)
                Debug.Log($"[AreaFill] Loop closed. Local bounds X:{minX:F2}->{maxX:F2} Z:{minZ:F2}->{maxZ:F2}", this);

            // Ability / zone logic stays immediate
            if (PlayerAbilityManager.Instance != null)
            {
                Bounds localBounds = new Bounds(
                    new Vector3((minX + maxX) * 0.5f, 0f, (minZ + maxZ) * 0.5f),
                    new Vector3(Mathf.Max(0.001f, maxX - minX), 0.5f, Mathf.Max(0.001f, maxZ - minZ))
                );

                PlayerAbilityManager.Instance.OnAreaFilled(referenceSurface, localPolyXZ, localBounds);
            }

            // Convert local XZ -> UV polygon
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

            if (_fillRoutine != null)
            {
                StopCoroutine(_fillRoutine);
                _fillRoutine = null;
            }

            if (fillMode == AreaFillMode.ProgressiveAnimated)
            {
                _fillRoutine = StartCoroutine(FillUvProgressiveOutsideInAsync(referenceSurface, uvPoly));
                return true;
            }

            if (useGpuPolygonFill)
            {
                bool ok = painter.FillPolygonUV(referenceSurface, uvPoly);
                if (ok)
                    return true;

                if (debugFillFailures)
                    Debug.LogWarning($"[AreaFill] GPU fill failed -> fallback={fallbackToCpuUvFill}. uvPoints={uvPoly.Count}", this);
            }

            if (fallbackToCpuUvFill)
                _fillRoutine = StartCoroutine(FillUvScanlineAsync(referenceSurface, uvPoly));

            return true;
        }

        // ----------------- Progressive fill (outside -> inside), time-sliced -----------------

        private IEnumerator FillUvProgressiveOutsideInAsync(SimplePaintSurface surface, List<Vector2> uvPoly)
        {
            // UV bounds
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
                // Optional extra safety: if still big, bump step a bit more (bounded)
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

            // Build buckets (yield during build using the same time budget)
            List<float> intersections = new List<float>(64);

            int buildOps = 0;
            float buildBudgetSec = Mathf.Max(0.0001f, progressiveTimeBudgetMsPerFrame / 1000f);

            for (float v = minV; v <= maxV; v += step)
            {
                float frameStart = Time.realtimeSinceStartup;

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
                        Vector2 uv = new Vector2(u, v);

                        float r = Vector2.Distance(uv, centroid);
                        float tNorm = Mathf.Clamp01(r / maxR);
                        int bucket = Mathf.Clamp(Mathf.FloorToInt(tNorm * (rings - 1)), 0, rings - 1);

                        buckets[bucket].Add(uv);

                        buildOps++;
                        if (buildOps >= 5000)
                        {
                            buildOps = 0;

                            if (Time.realtimeSinceStartup - frameStart >= buildBudgetSec)
                                yield return null;
                        }
                    }
                }

                if (Time.realtimeSinceStartup - frameStart >= buildBudgetSec)
                    yield return null;
            }

            // Paint buckets from outside -> inside with a strict time budget each frame
            float paintBudgetSec = Mathf.Max(0.0001f, progressiveTimeBudgetMsPerFrame / 1000f);
            int ring = rings - 1;

            while (ring >= 0)
            {
                float frameStart = Time.realtimeSinceStartup;

                while (ring >= 0 && (Time.realtimeSinceStartup - frameStart) < paintBudgetSec)
                {
                    var list = buckets[ring];

                    if (list.Count == 0)
                    {
                        ring--;
                        continue;
                    }

                    // Pop from end
                    int last = list.Count - 1;
                    Vector2 uv = list[last];
                    list.RemoveAt(last);

                    PaintLiquidSolidAtUV(surface, uv);

                    // Keep going until budget is exhausted
                    if (list.Count == 0)
                        ring--;
                }

                yield return null;
            }

            _fillRoutine = null;
        }

        private void PaintLiquidSolidAtUV(SimplePaintSurface surface, Vector2 uv)
        {
            // If you still want more FPS safety on weak machines, keep these conservative:
            // smearMax 2-3, splatsMax 1-2, jitter modest.
            if (!liquidEnabled)
            {
                painter.PaintAtUV(surface, uv);
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

            // Base blob
            painter.PaintAtUV(surface, uv, half0, 1f);

            // Extra splats (cheap-ish)
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

                painter.PaintAtUV(surface, uv2, h, op);
            }

            // Smear chain (most expensive) — keep it short for low-end
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

                painter.PaintAtUV(surface, uvS, size, op);
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

        // ----------------- Fallback scanline (unchanged) -----------------

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

            _fillRoutine = null;
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
