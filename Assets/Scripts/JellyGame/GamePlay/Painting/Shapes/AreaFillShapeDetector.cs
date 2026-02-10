// FILEPATH: Assets/Scripts/Painting/Shapes/AreaFillShapeDetector.cs
using System.Collections;
using System.Collections.Generic;
using JellyGame.GamePlay.Abilities;
using JellyGame.GamePlay.Audio.Core;
using JellyGame.GamePlay.Managers;
using JellyGame.GamePlay.Map.Surfaces;
using JellyGame.GamePlay.Painting.Trails.Collision;
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

        [Header("Inner/Outer From Stroke Edges")]
        [Tooltip("If true and StrokeTrailRecorder is recording edge pairs, we build TWO closed polygons from the stroke edges (left & right).\n" +
                 "The outer polygon is closed using a parallel point match (not just straight line back to start).\n" +
                 "We pick the BIGGER polygon for gameplay/zone colliders, and the SMALLER polygon for shader fill.")]
        [SerializeField] private bool useStrokeEdgeInnerOuter = true;

        [Tooltip("If true, shader fill uses the outer (larger) polygon. If false, shader fill uses the inner (smaller) polygon.\n" +
                 "The collider always uses the opposite polygon (larger for gameplay zones).")]
        [SerializeField] private bool fillUsesOuterPolygon = false;

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

        [Header("Auto-Find Settings")]
        [Tooltip("If true, auto-finds paintSurfaces and painter at runtime when not assigned in Inspector.\n" +
                 "Searches parent hierarchy first (player is child of TiltTray), then scene-wide.")]
        [SerializeField] private bool autoFindReferences = true;

        [Tooltip("If true, logs what was found/not found during auto-find.")]
        [SerializeField] private bool debugAutoFind = true;

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

        // ===================== Auto-Find =====================

        private void Awake()
        {
            if (!autoFindReferences)
                return;

            AutoFindPaintSurfaces();
            AutoFindPainter();
        }

        /// <summary>
        /// Auto-find SimplePaintSurface components if paintSurfaces list is empty or contains only nulls.
        /// Search order: parent hierarchy (TiltTray and its children), then scene-wide.
        /// </summary>
        private void AutoFindPaintSurfaces()
        {
            // Skip if already assigned in Inspector with valid entries
            if (paintSurfaces != null && paintSurfaces.Count > 0)
            {
                // Remove nulls that might be left from broken prefab references
                paintSurfaces.RemoveAll(s => s == null);

                if (paintSurfaces.Count > 0)
                {
                    if (debugAutoFind)
                        Debug.Log($"[AreaFillShapeDetector] paintSurfaces already assigned ({paintSurfaces.Count} surface(s)). Skipping auto-find.", this);
                    return;
                }
            }

            if (paintSurfaces == null)
                paintSurfaces = new List<SimplePaintSurface>();

            // 1) Search parent hierarchy (player is child of TiltTray → surfaces are siblings/cousins)
            Transform root = transform;
            while (root.parent != null)
                root = root.parent;

            var found = root.GetComponentsInChildren<SimplePaintSurface>(true);
            if (found != null && found.Length > 0)
            {
                paintSurfaces.AddRange(found);

                if (debugAutoFind)
                    Debug.Log($"[AreaFillShapeDetector] Auto-found {found.Length} SimplePaintSurface(s) in parent hierarchy (root: '{root.name}').", this);
                return;
            }

            // 2) Scene-wide fallback
            var sceneFound = FindObjectsOfType<SimplePaintSurface>(true);
            if (sceneFound != null && sceneFound.Length > 0)
            {
                paintSurfaces.AddRange(sceneFound);

                if (debugAutoFind)
                    Debug.Log($"[AreaFillShapeDetector] Auto-found {sceneFound.Length} SimplePaintSurface(s) scene-wide.", this);
                return;
            }

            Debug.LogWarning("[AreaFillShapeDetector] Auto-find FAILED: No SimplePaintSurface found anywhere! Area fill will not work.", this);
        }

        /// <summary>
        /// Auto-find RenderTextureTrailPainter if not assigned.
        /// Search order: this GameObject, children, parent hierarchy, scene-wide.
        /// </summary>
        private void AutoFindPainter()
        {
            if (painter != null)
            {
                if (debugAutoFind)
                    Debug.Log($"[AreaFillShapeDetector] painter already assigned: '{painter.name}'. Skipping auto-find.", this);
                return;
            }

            // 1) This GameObject
            painter = GetComponent<RenderTextureTrailPainter>();
            if (painter != null)
            {
                if (debugAutoFind)
                    Debug.Log("[AreaFillShapeDetector] Auto-found painter on this GameObject.", this);
                return;
            }

            // 2) Children
            painter = GetComponentInChildren<RenderTextureTrailPainter>(true);
            if (painter != null)
            {
                if (debugAutoFind)
                    Debug.Log($"[AreaFillShapeDetector] Auto-found painter in children: '{painter.name}'.", this);
                return;
            }

            // 3) Parent hierarchy
            painter = GetComponentInParent<RenderTextureTrailPainter>(true);
            if (painter != null)
            {
                if (debugAutoFind)
                    Debug.Log($"[AreaFillShapeDetector] Auto-found painter in parent hierarchy: '{painter.name}'.", this);
                return;
            }

            // 4) Scene-wide fallback
            painter = FindObjectOfType<RenderTextureTrailPainter>(true);
            if (painter != null)
            {
                if (debugAutoFind)
                    Debug.Log($"[AreaFillShapeDetector] Auto-found painter scene-wide: '{painter.name}'.", this);
                return;
            }

            Debug.LogWarning("[AreaFillShapeDetector] Auto-find FAILED: No RenderTextureTrailPainter found! Area fill will not work.", this);
        }

        // ===================== Shape Handling =====================

        public bool TryHandleShape(StrokeLoopSegment seg)
        {
            if (paintSurfaces == null || paintSurfaces.Count == 0 || painter == null || seg.history == null)
                return false;

            // Fallback: build world-space polygon from stroke history center points
            List<Vector3> worldCenterPolyPoints = new List<Vector3>();
            for (int i = seg.startIndex; i <= seg.endIndexInclusive; i++)
            {
                worldCenterPolyPoints.Add(seg.history[i].WorldPos);
            }

            if (worldCenterPolyPoints.Count < 3)
                return false;

            // Optional: build TWO world-space polygons from stroke edge pairs (left & right)
            // The outer polygon closure uses parallel point matching instead of straight line back to start
            List<Vector3> worldInnerPolyPoints = null;
            List<Vector3> worldOuterPolyPoints = null;
            bool haveEdgePolys = useStrokeEdgeInnerOuter && TryBuildEdgeWorldPolygonsWithParallelClosure(
                seg, 
                out worldInnerPolyPoints, 
                out worldOuterPolyPoints);

            // Collect fill data for each surface that intersects with the polygon
            List<SurfaceFillData> surfaceFillDatas = new List<SurfaceFillData>();

            // We will paint using the SMALLER polygon (shader fill), but spawn gameplay zones using the BIGGER polygon (colliders).
            bool haveAbilityPolys = false;
            SimplePaintSurface abilitySurface = null;
            List<Vector2> abilityColliderPolyXZ = null;
            List<Vector2> abilityFillPolyXZ = null;
            Bounds abilityColliderBounds = default;

            foreach (var surface in paintSurfaces)
            {
                if (surface == null)
                    continue;

                // Prefer edge-based inner/outer if available; otherwise fall back to centerline polygon.
                SurfaceFillData? fillSmall = null;
                SurfaceFillData? fillBig = null;

                if (haveEdgePolys)
                {
                    SurfaceFillData? inner = BuildSurfaceFillData(surface, worldInnerPolyPoints);
                    SurfaceFillData? outer = BuildSurfaceFillData(surface, worldOuterPolyPoints);

                    if (inner.HasValue && outer.HasValue)
                    {
                        float areaInner = PolygonAreaAbs(inner.Value.localPolyXZ);
                        float areaOuter = PolygonAreaAbs(outer.Value.localPolyXZ);

                        if (areaInner <= areaOuter)
                        {
                            fillSmall = inner;
                            fillBig = outer;
                        }
                        else
                        {
                            fillSmall = outer;
                            fillBig = inner;
                        }
                    }
                }

                if (!fillSmall.HasValue || !fillBig.HasValue)
                {
                    // fallback (single polygon)
                    SurfaceFillData? fallback = BuildSurfaceFillData(surface, worldCenterPolyPoints);
                    if (fallback.HasValue)
                    {
                        fillSmall = fallback;
                        fillBig = fallback;
                    }
                }

                if (fillSmall.HasValue && fillBig.HasValue)
                {
                    // Choose fill polygon based on setting
                    var fillPolygonToUse = fillUsesOuterPolygon ? fillBig.Value : fillSmall.Value;
                    var colliderPolygonToUse = fillUsesOuterPolygon ? fillSmall.Value : fillBig.Value;

                    // For painting, store the selected fill polygon
                    surfaceFillDatas.Add(fillPolygonToUse);

                    // For ability/colliders, store the opposite polygon (once, from the first valid surface)
                    if (!haveAbilityPolys)
                    {
                        haveAbilityPolys = true;
                        abilitySurface = colliderPolygonToUse.surface;
                        abilityColliderPolyXZ = colliderPolygonToUse.localPolyXZ;
                        abilityColliderBounds = colliderPolygonToUse.localBounds;
                        abilityFillPolyXZ = fillPolygonToUse.localPolyXZ;
                    }
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

            // Play sound once for the whole fill operation (with safeguard)
            try
            {
                if (SoundManager.Instance != null)
                {
                    var config = SoundManager.Instance.FindAudioConfig("CloseArea");
                    if (config != null)
                    {
                        SoundManager.Instance.PlaySound("CloseArea", this.transform);
                    }
                    else if (debugFillFailures)
                    {
                        Debug.Log("[AreaFill] 'CloseArea' sound not found - skipping.", this);
                    }
                }
            }
            catch (System.Exception ex)
            {
                if (debugFillFailures)
                    Debug.LogWarning($"[AreaFill] Could not play CloseArea sound: {ex.Message}", this);
            }

            // Notify ability manager:
            // - Fill polygon (SMALL) is used for "filled area" cost (self-damage).
            // - Collider polygon (BIG) is used for gameplay zone spawning.
            if (PlayerAbilityManager.Instance != null && haveAbilityPolys && abilitySurface != null)
            {
                PlayerAbilityManager.Instance.OnAreaFilled(abilitySurface, abilityFillPolyXZ, abilityColliderPolyXZ, abilityColliderBounds);
            }
            else if (PlayerAbilityManager.Instance != null && surfaceFillDatas.Count > 0)
            {
                // Fallback if no edge polygons available
                var firstData = surfaceFillDatas[0];
                PlayerAbilityManager.Instance.OnAreaFilled(firstData.surface, firstData.localPolyXZ, firstData.localPolyXZ, firstData.localBounds);
            }

            // NEW: Trigger a single "AreaClosed" event (once per closure)
            {
                var evt = new EventManager.AreaClosedEventData
                {
                    source = this,
                    surfaceTransform = abilitySurface != null ? abilitySurface.transform : (surfaceFillDatas.Count > 0 ? surfaceFillDatas[0].surface?.transform : null),
                    localBounds = haveAbilityPolys ? abilityColliderBounds : (surfaceFillDatas.Count > 0 ? surfaceFillDatas[0].localBounds : default)
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
        /// Builds two closed polygons from stroke edge pairs (left & right).
        /// The outer polygon closure uses parallel point matching: finds the point on the outer trail
        /// that is closest to being parallel with the inner closure point (instead of just connecting
        /// the outer end point directly back to the outer start point).
        /// </summary>
        private bool TryBuildEdgeWorldPolygonsWithParallelClosure(
            StrokeLoopSegment seg, 
            out List<Vector3> worldInner, 
            out List<Vector3> worldOuter)
        {
            worldInner = null;
            worldOuter = null;

            var recorder = GetComponent<StrokeTrailRecorder>();
            if (recorder == null || !recorder.EdgePairsEnabled)
                return false;

            var edgePairs = recorder.EdgePairs;
            if (edgePairs == null || edgePairs.Count == 0)
                return false;

            int start = seg.startIndex;
            int end = seg.endIndexInclusive;

            if (start < 0 || end < start || seg.history == null)
                return false;

            // Build left and right edge point lists
            int cap = Mathf.Max(0, end - start + 1);
            var left = new List<Vector3>(cap);
            var right = new List<Vector3>(cap);

            for (int i = start; i <= end; i++)
            {
                if (i < 0 || i >= seg.history.Count || i >= edgePairs.Count)
                    continue;

                Vector3 centerWorld = seg.history[i].WorldPos;
                var p = edgePairs[i];

                if (p.hasLeft)
                    left.Add(p.GetLeftWorld(centerWorld));

                if (p.hasRight)
                    right.Add(p.GetRightWorld(centerWorld));
            }

            RemoveConsecutiveNearDuplicates(left, 0.00001f);
            RemoveConsecutiveNearDuplicates(right, 0.00001f);

            RemoveClosingDuplicate(left, 0.00001f);
            RemoveClosingDuplicate(right, 0.00001f);

            if (left.Count < 3 || right.Count < 3)
                return false;

            // Determine which is inner and which is outer based on closure point distances
            Vector3 innerStart = seg.history[start].WorldPos;
            Vector3 innerEnd = seg.history[end].WorldPos;
            Vector3 closureDir = (innerEnd - innerStart).normalized;

            // Get edge points at closure
            var startPair = edgePairs[start];
            var endPair = edgePairs[end];
            Vector3 leftStart = startPair.hasLeft ? startPair.GetLeftWorld(innerStart) : innerStart;
            Vector3 leftEnd = endPair.hasLeft ? endPair.GetLeftWorld(innerEnd) : innerEnd;
            Vector3 rightStart = startPair.hasRight ? startPair.GetRightWorld(innerStart) : innerStart;
            Vector3 rightEnd = endPair.hasRight ? endPair.GetRightWorld(innerEnd) : innerEnd;

            // Find parallel closure point on the outer trail
            List<Vector3> innerPoly, outerPoly;

            float distLeftStart = Vector3.Distance(leftStart, innerStart);
            float distLeftEnd = Vector3.Distance(leftEnd, innerEnd);
            float distRightStart = Vector3.Distance(rightStart, innerStart);
            float distRightEnd = Vector3.Distance(rightEnd, innerEnd);

            float leftClosureDist = distLeftStart + distLeftEnd;
            float rightClosureDist = distRightStart + distRightEnd;

            if (leftClosureDist <= rightClosureDist)
            {
                innerPoly = left;
                outerPoly = right;
            }
            else
            {
                innerPoly = right;
                outerPoly = left;
            }

            List<Vector3> outerClosed = new List<Vector3>(outerPoly);

            int bestMatchIdx = -1;
            float bestMatchScore = float.PositiveInfinity;

            Vector3 innerClosureVec = innerEnd - innerStart;
            float innerClosureLen = innerClosureVec.magnitude;
            if (innerClosureLen < 0.0001f)
            {
                worldInner = innerPoly;
                worldOuter = outerClosed;
                return true;
            }

            Vector3 innerClosureDir = innerClosureVec / innerClosureLen;
            Vector3 innerStartToOuterStart = outerClosed[0] - innerStart;

            float projStart = Vector3.Dot(innerStartToOuterStart, innerClosureDir);
            float desiredProj = innerClosureLen;

            for (int i = 0; i < outerClosed.Count; i++)
            {
                Vector3 outerPt = outerClosed[i];
                Vector3 innerStartToOuterPt = outerPt - innerStart;
                float proj = Vector3.Dot(innerStartToOuterPt, innerClosureDir);

                float perpDist = Vector3.Cross(innerStartToOuterPt, innerClosureDir).magnitude;
                float score = Mathf.Abs(proj - desiredProj) + perpDist * 0.5f;

                if (score < bestMatchScore)
                {
                    bestMatchScore = score;
                    bestMatchIdx = i;
                }
            }

            if (bestMatchIdx >= 0 && bestMatchIdx < outerClosed.Count - 1)
            {
                if (bestMatchIdx > 0)
                {
                    Vector3 matchPoint = outerClosed[bestMatchIdx];
                    outerClosed.RemoveAt(bestMatchIdx);
                    outerClosed.Add(matchPoint);
                }
            }

            worldInner = innerPoly;
            worldOuter = outerClosed;
            return true;
        }

        private static void RemoveConsecutiveNearDuplicates(List<Vector3> pts, float minSqrDist)
        {
            if (pts == null || pts.Count < 2)
                return;

            for (int i = pts.Count - 1; i >= 1; i--)
            {
                if ((pts[i] - pts[i - 1]).sqrMagnitude <= minSqrDist)
                    pts.RemoveAt(i);
            }
        }

        private static void RemoveClosingDuplicate(List<Vector3> pts, float minSqrDist)
        {
            if (pts == null || pts.Count < 3)
                return;

            if ((pts[0] - pts[pts.Count - 1]).sqrMagnitude <= minSqrDist)
                pts.RemoveAt(pts.Count - 1);
        }

        private static float PolygonAreaAbs(List<Vector2> poly)
        {
            if (poly == null || poly.Count < 3)
                return 0f;

            double sum = 0.0;
            int n = poly.Count;

            for (int i = 0; i < n; i++)
            {
                Vector2 a = poly[i];
                Vector2 b = poly[(i + 1) % n];
                sum += (double)a.x * b.y - (double)b.x * a.y;
            }

            return Mathf.Abs((float)sum) * 0.5f;
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
                    if (!PolygonIntersectsUnitSquare(uvPoly))
                    {
                        if (debugMultiSurface)
                            Debug.Log($"[AreaFill] Polygon completely outside surface {surface.name} UV bounds.", this);
                        return null;
                    }
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

            foreach (var p in polygon)
            {
                if (p.x >= 0f && p.x <= 1f && p.y >= 0f && p.y <= 1f)
                    return true;
            }

            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % polygon.Count];

                if (LineIntersectsUnitSquare(a, b))
                    return true;
            }

            if (PointInPolygon(new Vector2(0.5f, 0.5f), polygon))
                return true;

            return false;
        }

        private bool LineIntersectsUnitSquare(Vector2 a, Vector2 b)
        {
            if (LinesIntersect(a, b, new Vector2(0, 0), new Vector2(0, 1))) return true;
            if (LinesIntersect(a, b, new Vector2(1, 0), new Vector2(1, 1))) return true;
            if (LinesIntersect(a, b, new Vector2(0, 0), new Vector2(1, 0))) return true;
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

            output = ClipPolygonAgainstEdge(output, new Vector2(0, 0), new Vector2(0, 1));
            if (output == null || output.Count < 3) return null;

            output = ClipPolygonAgainstEdge(output, new Vector2(1, 1), new Vector2(1, 0));
            if (output == null || output.Count < 3) return null;

            output = ClipPolygonAgainstEdge(output, new Vector2(1, 0), new Vector2(0, 0));
            if (output == null || output.Count < 3) return null;

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
            Vector2 edgeNormal = new Vector2(-edgeDir.y, edgeDir.x);

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
                        Vector2? intersection = LineEdgeIntersection(current, next, edgeP1, edgeP2);
                        if (intersection.HasValue)
                            output.Add(intersection.Value);
                    }
                }
                else if (nextInside)
                {
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
                return null;

            Vector2 d3 = e1 - p1;
            float t = (d3.x * d2.y - d3.y * d2.x) / cross;

            if (t < 0f || t > 1f)
                return null;

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

            float fingerRot = Hash01(centroid, unevenSeed + 999) * Mathf.PI * 2f;

            int paintRing = rings - 1;

            List<float> intersections = new List<float>(64);

            float v = minV;
            while (v <= maxV)
            {
                float frameStart = Time.realtimeSinceStartup;

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

                PaintAvailableOuterBucketsWithinBudget(surface, buckets, ref paintRing, frameStart, budgetSec);

                yield return null;
            }

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