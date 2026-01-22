// FILEPATH: Assets/Scripts/Painting/RenderTextureTrailPainter.cs
using System.Collections.Generic;
using JellyGame.GamePlay.Abilities;
using JellyGame.GamePlay.Map.Surfaces;
using UnityEngine;

namespace JellyGame.GamePlay.Painting.Trails.Visibility
{
    /// <summary>
    /// Trail painter with TIME-BASED AGING support.
    /// Paints to both color texture AND time texture.
    /// Supports different aging for trails vs filled areas.
    /// </summary>
    [DisallowMultipleComponent]
    public class RenderTextureTrailPainter : MonoBehaviour, IMovementPainter
    {
        // Clear colors (set these in inspector if you want)
        [Header("Clear / Reset")]
        [SerializeField] private Color clearPaintColor = new Color(0f, 0f, 0f, 0f); // transparent
        [SerializeField] private Color clearTimeColor  = new Color(0f, 0f, 0f, 0f); // time=0, fillFlag=0
        
        [Header("Trail Protection")]
        [Tooltip("When filling, don't paint over trails younger than this (seconds). Should match MaxAge in Shader Graph.")]
        [SerializeField] private float trailProtectionMaxAge = 10f;
        
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
        [SerializeField, Range(0f, 1f)] private float cornerRadius = 0.2f;

        [Header("Time Painting (for aging)")]
        [Tooltip("Material using TimeBrushBlit.shader")]
        [SerializeField] private Material timeBrushBlitMaterial;
        
        [Tooltip("Material using TimePolygonFill.shader")]
        [SerializeField] private Material timePolygonFillMaterial;

        [Header("Polygon Fill (GPU)")]
        [SerializeField] private Material polygonFillMaterial;
        [SerializeField, Range(0f, 1f)] private float polygonFillOpacity = 1f;
        [SerializeField] private bool sanitizeFillPolygon = true;
        [SerializeField] private float uvDuplicateEpsilon = 0.0005f;
        [SerializeField] private float uvCollinearEpsilon = 0.0000005f;

        [Header("Sampling")]
        [SerializeField] private float minWorldStep = 0.0005f;

        [Header("Debug")]
        [SerializeField] private bool debugRays = false;
        [SerializeField] private bool debugFillFailures = true;

        private SimplePaintSurface _currentSurface;
        private RenderTexture _tempRT;
        private RenderTexture _tempTimeRT;

        private Color _currentPaintColor;

        private static readonly int FillColorId = Shader.PropertyToID("_FillColor");
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        private static readonly int PaintTimeId = Shader.PropertyToID("_PaintTime");
        private static readonly int IsFillId = Shader.PropertyToID("_IsFill");
        private static readonly int PaintTypeId = Shader.PropertyToID("_PaintType"); 
        public float DefaultHalfSizeUV => fallbackHalfSizeUV;
        
        public float SizeWorldMultiplierForRecorder => sizeWorldMultiplier;

        private void Awake()
        {
            var renderer = GetComponent<Renderer>();
            if (renderer != null)
                brushColor = renderer.material.color;

            _currentPaintColor = brushColor;

            if (brushBlitMaterial != null)
            {
                brushBlitMaterial.SetFloat("_BrushHardness", brushHardness);
                UpdatePaintColor();
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

        // ========== Trail Painting ==========

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

            UpdatePaintColor();
            
            // set geometry properties
            brushBlitMaterial.SetVector("_BrushCenter", new Vector4(uvCenter.x, uvCenter.y, 0, 0));
            brushBlitMaterial.SetVector("_BrushHalfSize", new Vector4(halfSizeUV.x, halfSizeUV.y, 0, 0));
            
            // set type to trail
            brushBlitMaterial.SetFloat(PaintTypeId, 0f);
            
            //blit logic
            EnsureTemp(rt, ref _tempRT);
            brushBlitMaterial.SetTexture(brushSourceTexProperty, rt);
            Graphics.Blit(rt, _tempRT, brushBlitMaterial);
            Graphics.Blit(_tempRT, rt);

            // Paint TIME (trail = isFill false)
            PaintTimeAtUV(_currentSurface, uvCenter, halfSizeUV.x, 1f, false);
        }

        /// <summary>
        /// Paint time data to the time texture.
        /// isFill = true writes G=1 (fill), isFill = false writes G=0 (trail)
        /// </summary>
        private void PaintTimeAtUV(SimplePaintSurface surface, Vector2 uvCenter, float halfSizeUV, float opacity, bool isFill)
        {
            if (!surface.EnableTimeAging || timeBrushBlitMaterial == null)
                return;

            var timeRT = surface.PaintTimeRT;
            if (timeRT == null)
                return;

            float currentTime = surface.GetCurrentTime();

            timeBrushBlitMaterial.SetVector("_BrushCenter", new Vector4(uvCenter.x, uvCenter.y, 0, 0));
            timeBrushBlitMaterial.SetVector("_BrushHalfSize", new Vector4(halfSizeUV, halfSizeUV, 0, 0));
            timeBrushBlitMaterial.SetFloat("_BrushOpacity", opacity);
            timeBrushBlitMaterial.SetFloat("_PaintTime", currentTime);
            timeBrushBlitMaterial.SetFloat("_IsFill", isFill ? 1f : 0f);
            timeBrushBlitMaterial.SetFloat("_MaxAge", trailProtectionMaxAge);
            timeBrushBlitMaterial.SetFloat("_CornerRadius", cornerRadius);
            
            EnsureTemp(timeRT, ref _tempTimeRT);
            timeBrushBlitMaterial.SetTexture("_MainTex", timeRT);
            Graphics.Blit(timeRT, _tempTimeRT, timeBrushBlitMaterial);
            Graphics.Blit(_tempTimeRT, timeRT);
        }
        
        // ========== Public API for UV painting ==========
        
        
        /// <summary>
        /// Forces a range of stroke history samples to appear "aged" (gray) on the paint texture.
        /// Call this when consuming history due to area closure.
        /// Uses a larger brush size to cover the full trail width (not just center points).
        /// </summary>
        public void AgeTrailRange(SimplePaintSurface surface, StrokeHistory history, int startIndex, int endIndexInclusive)
        {
            if (surface == null || history == null || !surface.EnableTimeAging || timeBrushBlitMaterial == null)
                return;

            var timeRT = surface.PaintTimeRT;
            if (timeRT == null)
                return;

            // Use a very old time so shader treats these as aged (gray)
            float oldTime = surface.GetCurrentTime() - 1000f;

            // Use LARGER size to cover full trail width (1.5x to cover edges + margin)
            float halfSizeUV = fallbackHalfSizeUV * sizeWorldMultiplier * 2f;

            for (int i = startIndex; i <= endIndexInclusive && i < history.Count; i++)
            {
                Vector3 worldPos = history[i].WorldPos;

                if (!surface.TryWorldToPaintUV(worldPos, out var uvCenter))
                    continue;

                timeBrushBlitMaterial.SetVector("_BrushCenter", new Vector4(uvCenter.x, uvCenter.y, 0, 0));
                timeBrushBlitMaterial.SetVector("_BrushHalfSize", new Vector4(halfSizeUV, halfSizeUV, 0, 0));
                timeBrushBlitMaterial.SetFloat("_BrushOpacity", 1f);
                timeBrushBlitMaterial.SetFloat("_PaintTime", oldTime);  // OLD time = gray
                timeBrushBlitMaterial.SetFloat("_IsFill", 0f);
                timeBrushBlitMaterial.SetFloat("_MaxAge", trailProtectionMaxAge);

                EnsureTemp(timeRT, ref _tempTimeRT);
                timeBrushBlitMaterial.SetTexture("_MainTex", timeRT);
                Graphics.Blit(timeRT, _tempTimeRT, timeBrushBlitMaterial);
                Graphics.Blit(_tempTimeRT, timeRT);
            }
        }


        /// <summary>
        /// Paint at UV (for trails - G=0)
        /// </summary>
        public void PaintAtUV(SimplePaintSurface surface, Vector2 uvCenter)
        {
            PaintAtUV(surface, uvCenter, fallbackHalfSizeUV, 1f, false);
        }

        /// <summary>
        /// Paint at UV with size and opacity (for trails - G=0)
        /// </summary>
        public void PaintAtUV(SimplePaintSurface surface, Vector2 uvCenter, float halfSizeUV, float opacity)
        {
            PaintAtUV(surface, uvCenter, halfSizeUV, opacity, false);
        }

        /// <summary>
        /// Paint at UV with full control including isFill flag.
        /// isFill = true: writes G=1 (for filled areas, uses MaxAgeFill)
        /// isFill = false: writes G=0 (for trails, uses MaxAge)
        /// </summary>
        public void PaintAtUV(SimplePaintSurface surface, Vector2 uvCenter, float halfSizeUV, float opacity, bool isFill)
        {
            if (surface == null || brushBlitMaterial == null)
                return;

            var rt = surface.PaintRT;
            var timeRT = surface.PaintTimeRT;
            if (rt == null)
                return;

            UpdatePaintColor();

            halfSizeUV = Mathf.Max(0.00001f, halfSizeUV);
            opacity = Mathf.Clamp01(opacity);
            
            float currentTime = surface.GetCurrentTime();

            brushBlitMaterial.SetVector("_BrushCenter", new Vector4(uvCenter.x, uvCenter.y, 0, 0));
            brushBlitMaterial.SetVector("_BrushHalfSize", new Vector4(halfSizeUV, halfSizeUV, 0, 0));
            brushBlitMaterial.SetFloat("_BrushOpacity", opacity);
            brushBlitMaterial.SetFloat("_CornerRadius", cornerRadius);
            
            brushBlitMaterial.SetTexture("_TimeTex", timeRT); // קריטי!
            brushBlitMaterial.SetFloat("_PaintTime", currentTime);
            brushBlitMaterial.SetFloat("_MaxAge", trailProtectionMaxAge);
            
            //Pass the fill flag to the shader
            brushBlitMaterial.SetFloat(PaintTypeId, isFill ? 1f : 0f);
            
            EnsureTemp(rt, ref _tempRT);
            brushBlitMaterial.SetTexture(brushSourceTexProperty, rt);
            Graphics.Blit(rt, _tempRT, brushBlitMaterial);
            Graphics.Blit(_tempRT, rt);

            // Paint time with fill flag
            PaintTimeAtUV(surface, uvCenter, halfSizeUV, opacity, isFill);
        }

        /// <summary>
        /// Convenience method for fill operations - paints with isFill=true
        /// </summary>
        public void PaintFillAtUV(SimplePaintSurface surface, Vector2 uvCenter, float halfSizeUV, float opacity)
        {
            PaintAtUV(surface, uvCenter, halfSizeUV, opacity, true);
        }

        /// <summary>
        /// Convenience method for fill operations - paints with isFill=true
        /// </summary>
        public void PaintFillAtUV(SimplePaintSurface surface, Vector2 uvCenter)
        {
            PaintAtUV(surface, uvCenter, fallbackHalfSizeUV, 1f, true);
        }

        // ========== Polygon Fill ==========

        public bool FillPolygonUV(SimplePaintSurface surface, List<Vector2> uvPolygon)
        {
            if (surface == null || uvPolygon == null || uvPolygon.Count < 3)
                return false;

            var rt = surface.PaintRT;
            if (rt == null)
                return false;

            if (polygonFillMaterial == null)
            {
                if (debugFillFailures)
                    Debug.LogWarning("[PaintFill] polygonFillMaterial is not assigned.", this);
                return false;
            }

            List<Vector2> poly = uvPolygon;

            if (sanitizeFillPolygon)
            {
                poly = new List<Vector2>(uvPolygon);
                SanitizePolygonUV(poly, uvDuplicateEpsilon, uvCollinearEpsilon);
            }

            if (poly.Count < 3)
            {
                if (debugFillFailures)
                    Debug.LogWarning("[PaintFill] Polygon collapsed after sanitization.", this);
                return false;
            }

            if (!TriangulateEarClipping(poly, out var tris))
            {
                if (debugFillFailures)
                    Debug.LogWarning($"[PaintFill] Triangulation failed. points={poly.Count}", this);
                return false;
            }

            if (tris.Count < 3)
            {
                if (debugFillFailures)
                    Debug.LogWarning("[PaintFill] Triangulation produced no triangles.", this);
                return false;
            }

            UpdatePaintColor();
            
            // Fill COLOR texture
            polygonFillMaterial.SetColor(FillColorId, new Color(0f, 1f, 0f, 1f));
            //polygonFillMaterial.SetColor(FillColorId, _currentPaintColor);
            polygonFillMaterial.SetFloat(OpacityId, polygonFillOpacity);
            FillPolygonToRT(rt, poly, tris, polygonFillMaterial);

            // Fill TIME texture (with G=1 for fill)
            if (surface.EnableTimeAging && timePolygonFillMaterial != null && surface.PaintTimeRT != null)
            {
                float currentTime = surface.GetCurrentTime();
                timePolygonFillMaterial.SetFloat(PaintTimeId, currentTime);
                FillPolygonToRT(surface.PaintTimeRT, poly, tris, timePolygonFillMaterial);
            }

            return true;
        }

        private void FillPolygonToRT(RenderTexture rt, List<Vector2> poly, List<int> tris, Material mat)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;

            GL.PushMatrix();
            GL.LoadOrtho();

            mat.SetPass(0);
            GL.Begin(GL.TRIANGLES);

            for (int i = 0; i < tris.Count; i++)
            {
                Vector2 uv = poly[tris[i]];
                GL.Vertex3(uv.x, uv.y, 0f);
            }

            GL.End();
            GL.PopMatrix();

            RenderTexture.active = prev;
        }

        // ========== Helper Methods ==========

        
        private void EnsureTemp(RenderTexture rt, ref RenderTexture tempRT)
        {
            if (tempRT == null ||
                tempRT.width != rt.width ||
                tempRT.height != rt.height ||
                tempRT.format != rt.format)
            {
                if (tempRT != null)
                    tempRT.Release();
                tempRT = new RenderTexture(rt.descriptor);
                tempRT.Create();
            }

            tempRT.wrapMode = rt.wrapMode;
            tempRT.filterMode = rt.filterMode;
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
            bool okR = _currentSurface.TryWorldToPaintUV(worldCenter + surf.right * halfWorldX, out uvR);
            bool okF = _currentSurface.TryWorldToPaintUV(worldCenter + surf.forward * halfWorldZ, out uvF);

            if (!okR || !okF)
                return new Vector2(fallbackHalfSizeUV, fallbackHalfSizeUV);

            float halfU = Mathf.Abs(uvR.x - uvCenter.x);
            float halfV = Mathf.Abs(uvF.y - uvCenter.y);

            if (halfU <= 1e-6f) halfU = fallbackHalfSizeUV;
            if (halfV <= 1e-6f) halfV = fallbackHalfSizeUV;

            return new Vector2(halfU, halfV);
        }

        private void SetSurface(RaycastHit hit) => _currentSurface = hit.collider.GetComponentInParent<SimplePaintSurface>();
        private void ClearSurface() => _currentSurface = null;

        private bool TryRaycastSurface(Vector3 fromPos, out RaycastHit bestHit)
        {
            Vector3 primaryDir = useWorldDown ? Vector3.down : -transform.up;

            if (RaycastOneDirection(fromPos, primaryDir, out bestHit))
                return true;

            if (RaycastOneDirection(fromPos, -primaryDir, out bestHit))
                return true;

            bestHit = default;
            return false;
        }

        private bool RaycastOneDirection(Vector3 fromPos, Vector3 dir, out RaycastHit hit)
        {
            Vector3 start = fromPos - dir * 0.05f;

            if (debugRays)
                Debug.DrawRay(start, dir * rayDistance, Color.magenta, 0.1f);

            return Physics.Raycast(start, dir, out hit, rayDistance, surfaceMask, QueryTriggerInteraction.Collide);
        }

        private void UpdatePaintColor()
        {
            if (brushBlitMaterial == null)
                return;

            Color currentColor = brushColor;

            if (PlayerAbilityManager.Instance != null)
                currentColor = PlayerAbilityManager.Instance.CurrentPaintColor;

            _currentPaintColor = currentColor;
            brushBlitMaterial.SetColor("_BrushColor", currentColor);
        }

        private void OnDestroy()
        {
            if (_tempRT != null)
            {
                _tempRT.Release();
                _tempRT = null;
            }
            if (_tempTimeRT != null)
            {
                _tempTimeRT.Release();
                _tempTimeRT = null;
            }
        }

        // ========== Polygon Helpers (unchanged) ==========

        private static void SanitizePolygonUV(List<Vector2> poly, float dupEps, float colEps)
        {
            for (int i = 0; i < poly.Count; i++)
                poly[i] = new Vector2(Mathf.Clamp01(poly[i].x), Mathf.Clamp01(poly[i].y));

            float dupEpsSqr = dupEps * dupEps;
            for (int i = poly.Count - 1; i >= 1; i--)
            {
                if ((poly[i] - poly[i - 1]).sqrMagnitude <= dupEpsSqr)
                    poly.RemoveAt(i);
            }
            if (poly.Count >= 2 && (poly[0] - poly[poly.Count - 1]).sqrMagnitude <= dupEpsSqr)
                poly.RemoveAt(poly.Count - 1);

            if (poly.Count < 3)
                return;

            bool removed;
            int guard = 0;
            do
            {
                removed = false;
                guard++;
                if (guard > 10_000) break;

                for (int i = 0; i < poly.Count; i++)
                {
                    int prev = (i - 1 + poly.Count) % poly.Count;
                    int next = (i + 1) % poly.Count;

                    Vector2 a = poly[prev];
                    Vector2 b = poly[i];
                    Vector2 c = poly[next];

                    float twiceArea = Mathf.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x));
                    if (twiceArea <= colEps)
                    {
                        poly.RemoveAt(i);
                        removed = true;
                        break;
                    }
                }
            } while (removed && poly.Count >= 3);

            for (int i = poly.Count - 1; i >= 1; i--)
            {
                if ((poly[i] - poly[i - 1]).sqrMagnitude <= dupEpsSqr)
                    poly.RemoveAt(i);
            }
            if (poly.Count >= 2 && (poly[0] - poly[poly.Count - 1]).sqrMagnitude <= dupEpsSqr)
                poly.RemoveAt(poly.Count - 1);
        }

        private static bool TriangulateEarClipping(List<Vector2> poly, out List<int> outTris)
        {
            outTris = new List<int>();
            int n = poly.Count;
            if (n < 3)
                return false;

            float areaAbs = Mathf.Abs(SignedArea(poly));
            if (areaAbs < 1e-8f)
                return false;

            List<int> indices = new List<int>(n);
            for (int i = 0; i < n; i++)
                indices.Add(i);

            if (SignedArea(poly) < 0f)
                indices.Reverse();

            int guard = 0;
            while (indices.Count > 3 && guard < 20000)
            {
                guard++;
                bool clipped = false;

                for (int i = 0; i < indices.Count; i++)
                {
                    int iPrev = indices[(i - 1 + indices.Count) % indices.Count];
                    int iCurr = indices[i];
                    int iNext = indices[(i + 1) % indices.Count];

                    Vector2 a = poly[iPrev];
                    Vector2 b = poly[iCurr];
                    Vector2 c = poly[iNext];

                    if (!IsConvexCCW(a, b, c))
                        continue;

                    bool hasPointInside = false;
                    for (int k = 0; k < indices.Count; k++)
                    {
                        int idx = indices[k];
                        if (idx == iPrev || idx == iCurr || idx == iNext)
                            continue;

                        if (PointInTriangle(poly[idx], a, b, c))
                        {
                            hasPointInside = true;
                            break;
                        }
                    }

                    if (hasPointInside)
                        continue;

                    outTris.Add(iPrev);
                    outTris.Add(iCurr);
                    outTris.Add(iNext);

                    indices.RemoveAt(i);
                    clipped = true;
                    break;
                }

                if (!clipped)
                    return false;
            }

            if (indices.Count == 3)
            {
                outTris.Add(indices[0]);
                outTris.Add(indices[1]);
                outTris.Add(indices[2]);
                return true;
            }

            return false;
        }

        private static float SignedArea(List<Vector2> poly)
        {
            float a = 0f;
            for (int i = 0; i < poly.Count; i++)
            {
                Vector2 p = poly[i];
                Vector2 q = poly[(i + 1) % poly.Count];
                a += (p.x * q.y - q.x * p.y);
            }
            return 0.5f * a;
        }

        private static bool IsConvexCCW(Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 ab = b - a;
            Vector2 ac = c - a;
            float cross = ab.x * ac.y - ab.y * ac.x;
            return cross > 1e-10f;
        }

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Sign(p, a, b);
            float d2 = Sign(p, b, c);
            float d3 = Sign(p, c, a);

            bool hasNeg = (d1 < 0f) || (d2 < 0f) || (d3 < 0f);
            bool hasPos = (d1 > 0f) || (d2 > 0f) || (d3 > 0f);

            return !(hasNeg && hasPos);
        }

        private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
        {
            return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
        }
        
        public void ClearAllPaint()
        {
            // Clears ALL paint surfaces in the scene
            SimplePaintSurface[] surfaces = FindObjectsOfType<SimplePaintSurface>(includeInactive: true);

            for (int i = 0; i < surfaces.Length; i++)
            {
                ClearSurfacePaint(surfaces[i]);
            }

            // Also clear any cached current surface reference
            ClearSurface();
        }

        public void ClearSurfacePaint(SimplePaintSurface surface)
        {
            if (surface == null)
                return;

            if (surface.PaintRT != null)
                ClearRenderTexture(surface.PaintRT, clearPaintColor);

            if (surface.EnableTimeAging && surface.PaintTimeRT != null)
                ClearRenderTexture(surface.PaintTimeRT, clearTimeColor);
        }

        private static void ClearRenderTexture(RenderTexture rt, Color clearColor)
        {
            if (rt == null)
                return;

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;

            GL.Clear(true, true, clearColor);

            RenderTexture.active = prev;
        }

    }
}
