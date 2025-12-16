// FILEPATH: Assets/Scripts/Painting/RenderTextureTrailPainter.cs
using System.Collections.Generic;
using JellyGame.GamePlay.Abilities;
using JellyGame.GamePlay.Map.Surfaces;
using UnityEngine;

namespace JellyGame.GamePlay.Painting.Trails.Visibility
{
    [DisallowMultipleComponent]
    public class RenderTextureTrailPainter : MonoBehaviour, IMovementPainter
    {
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

        [Header("Polygon Fill (GPU)")]
        [Tooltip("Material using Shader \"Custom/PaintPolygonFill\". Used for fast area fills.")]
        [SerializeField] private Material polygonFillMaterial;

        [Tooltip("Opacity used when filling closed areas.")]
        [SerializeField, Range(0f, 1f)] private float polygonFillOpacity = 1f;

        [Tooltip("Remove near-duplicate and collinear points before triangulation.")]
        [SerializeField] private bool sanitizeFillPolygon = true;

        [Tooltip("How close UV points must be to be considered duplicates (0..1 UV space).")]
        [SerializeField] private float uvDuplicateEpsilon = 0.0005f;

        [Tooltip("Collinear removal threshold. Bigger = removes more points. (UV-space area threshold)")]
        [SerializeField] private float uvCollinearEpsilon = 0.0000005f;

        [Header("Sampling")]
        [SerializeField] private float minWorldStep = 0.0005f;

        [Header("Debug")]
        [SerializeField] private bool debugRays = false;
        [SerializeField] private bool debugFillFailures = true;

        private SimplePaintSurface _currentSurface;
        private RenderTexture _tempRT;

        private Color _currentPaintColor;

        private static readonly int FillColorId = Shader.PropertyToID("_FillColor");
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");

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

        // ========== Trail Painting (keeps scale-based sizing) ==========

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

            brushBlitMaterial.SetVector("_BrushCenter", new Vector4(uvCenter.x, uvCenter.y, 0, 0));
            brushBlitMaterial.SetVector("_BrushHalfSize", new Vector4(halfSizeUV.x, halfSizeUV.y, 0, 0));

            EnsureTemp(rt);

            brushBlitMaterial.SetTexture(brushSourceTexProperty, rt);
            Graphics.Blit(rt, _tempRT, brushBlitMaterial);
            Graphics.Blit(_tempRT, rt);
        }

        public void PaintAtUV(SimplePaintSurface surface, Vector2 uvCenter)
        {
            if (surface == null || brushBlitMaterial == null)
                return;

            var rt = surface.PaintRT;
            if (rt == null)
                return;

            UpdatePaintColor();

            brushBlitMaterial.SetVector("_BrushCenter", new Vector4(uvCenter.x, uvCenter.y, 0, 0));
            brushBlitMaterial.SetVector("_BrushHalfSize", new Vector4(fallbackHalfSizeUV, fallbackHalfSizeUV, 0, 0));
            brushBlitMaterial.SetFloat("_BrushOpacity", 1f);

            EnsureTemp(rt);

            brushBlitMaterial.SetTexture(brushSourceTexProperty, rt);
            Graphics.Blit(rt, _tempRT, brushBlitMaterial);
            Graphics.Blit(_tempRT, rt);
        }

        private void EnsureTemp(RenderTexture rt)
        {
            if (_tempRT == null ||
                _tempRT.width != rt.width ||
                _tempRT.height != rt.height ||
                _tempRT.format != rt.format)
            {
                if (_tempRT != null)
                    _tempRT.Release();
                _tempRT = new RenderTexture(rt.descriptor);
                _tempRT.Create();
            }

            _tempRT.wrapMode = rt.wrapMode;
            _tempRT.filterMode = rt.filterMode;
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

        // ========== NEW: GPU polygon fill with robustness + result ==========
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

            // Work on a copy so caller can reuse list
            List<Vector2> poly = uvPolygon;

            if (sanitizeFillPolygon)
            {
                poly = new List<Vector2>(uvPolygon);
                SanitizePolygonUV(poly, uvDuplicateEpsilon, uvCollinearEpsilon);
            }

            if (poly.Count < 3)
            {
                if (debugFillFailures)
                    Debug.LogWarning("[PaintFill] Polygon collapsed after sanitization (too few points).", this);
                return false;
            }

            if (!TriangulateEarClipping(poly, out var tris))
            {
                if (debugFillFailures)
                    Debug.LogWarning($"[PaintFill] Triangulation failed. points={poly.Count} area={Mathf.Abs(SignedArea(poly)):F6}", this);
                return false;
            }

            if (tris.Count < 3)
            {
                if (debugFillFailures)
                    Debug.LogWarning("[PaintFill] Triangulation produced no triangles.", this);
                return false;
            }

            UpdatePaintColor();
            polygonFillMaterial.SetColor(FillColorId, _currentPaintColor);
            polygonFillMaterial.SetFloat(OpacityId, polygonFillOpacity);

            var prev = RenderTexture.active;
            RenderTexture.active = rt;

            GL.PushMatrix();
            GL.LoadOrtho();

            polygonFillMaterial.SetPass(0);
            GL.Begin(GL.TRIANGLES);

            for (int i = 0; i < tris.Count; i++)
            {
                Vector2 uv = poly[tris[i]];
                GL.Vertex3(uv.x, uv.y, 0f);
            }

            GL.End();
            GL.PopMatrix();

            RenderTexture.active = prev;
            return true;
        }

        private static void SanitizePolygonUV(List<Vector2> poly, float dupEps, float colEps)
        {
            // 1) Clamp to [0..1] (just in case)
            for (int i = 0; i < poly.Count; i++)
                poly[i] = new Vector2(Mathf.Clamp01(poly[i].x), Mathf.Clamp01(poly[i].y));

            // 2) Remove near-duplicate consecutive points
            float dupEpsSqr = dupEps * dupEps;
            for (int i = poly.Count - 1; i >= 1; i--)
            {
                if ((poly[i] - poly[i - 1]).sqrMagnitude <= dupEpsSqr)
                    poly.RemoveAt(i);
            }
            // Also handle wrap-around duplicate (last ~ first)
            if (poly.Count >= 2 && (poly[0] - poly[poly.Count - 1]).sqrMagnitude <= dupEpsSqr)
                poly.RemoveAt(poly.Count - 1);

            if (poly.Count < 3)
                return;

            // 3) Remove collinear points (area of triangle near zero)
            // Iterate until stable (because removals create new collinearities)
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

            // 4) If still duplicates after collinear removal, clean again quickly
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

            // Quick reject: very tiny area
            float areaAbs = Mathf.Abs(SignedArea(poly));
            if (areaAbs < 1e-8f)
                return false;

            List<int> indices = new List<int>(n);
            for (int i = 0; i < n; i++)
                indices.Add(i);

            // Ensure CCW
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

                    // Accept slightly convex, reject near-collinear (handled earlier)
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
                    return false; // likely self-intersection or remaining degeneracy
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
            // Robust barycentric using signs
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

        // ========== Surface picking / ability color ==========
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
        }
    }
}
