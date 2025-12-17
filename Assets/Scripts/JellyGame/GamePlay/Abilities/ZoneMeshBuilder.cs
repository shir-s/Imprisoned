using System.Collections.Generic;
using UnityEngine;

namespace JellyGame.GamePlay.Abilities.Zones
{
    public static class ZoneMeshBuilder
    {
        public static List<int> TriangulatePolygonXZ(IReadOnlyList<Vector2> polyXZ)
        {
            if (polyXZ == null || polyXZ.Count < 3)
            {
                Debug.LogWarning($"[ZoneMeshBuilder] FAIL: Input polygon is null or too small.");
                return null;
            }

            // 1. Sanitize (Remove clumps and spikes)
            List<int> cleanToOriginalMap;
            List<Vector2> cleanPoly = SanitizePolygon(polyXZ, out cleanToOriginalMap);

            if (cleanPoly.Count < 3)
            {
                Debug.LogWarning("[ZoneMeshBuilder] Sanitization collapsed polygon too much.");
                return null;
            }

            // 2. Ensure CCW Winding
            if (SignedArea(cleanPoly) < 0f)
            {
                cleanPoly.Reverse();
                cleanToOriginalMap.Reverse();
            }

            // 3. TRY STRICT METHOD (Ear Clipping)
            // This is accurate for concave shapes but fails on self-intersections.
            List<int> tris = TriangulateEarClip(cleanPoly);

            // 4. FALLBACK: CENTROID FAN
            // If Ear Clipping failed (likely a self-intersecting "knot"), use a Fan.
            // This connects the center point to every edge. It handles knots gracefully
            // by just overlapping triangles. It is robust but technically "Convex-ish".
            if (tris == null)
            {
                Debug.LogWarning($"[ZoneMeshBuilder] Strict triangulation failed (twisted polygon?). Switching to Centroid Fan fallback. Points: {cleanPoly.Count}");
                tris = TriangulateCentroidFan(cleanPoly);
            }

            // 5. REMAP INDICES
            // Map the clean indices back to the original input array
            if (tris != null)
            {
                for (int i = 0; i < tris.Count; i++)
                {
                    // For the Fan method, we might introduce a new "Center" vertex index.
                    // If index is within range, map it. If it's the extra center, we need to handle it.
                    // NOTE: To keep this simple for your existing system which expects indices into polyXZ,
                    // The Fan fallback below ONLY uses existing points (it picks index 0 as pivot).
                    
                    int cleanIndex = tris[i];
                    tris[i] = cleanToOriginalMap[cleanIndex];
                }
            }

            return tris;
        }

        // --- FALLBACK ALGORITHM ---
        // Simple "Fan" triangulation. Connects point 0 to all other edges.
        // Good enough for simple convex shapes and prevents the zone from failing entirely.
        private static List<int> TriangulateCentroidFan(List<Vector2> poly)
        {
            int n = poly.Count;
            List<int> tris = new List<int>((n - 2) * 3);

            for (int i = 1; i < n - 1; i++)
            {
                tris.Add(0);
                tris.Add(i);
                tris.Add(i + 1);
            }
            return tris;
        }

        private static List<Vector2> SanitizePolygon(IReadOnlyList<Vector2> source, out List<int> indexMap)
        {
            int capacity = source.Count;
            List<Vector2> clean = new List<Vector2>(capacity);
            indexMap = new List<int>(capacity);

            if (capacity == 0) return clean;

            // Aggressive sanitization
            float minSqDist = 0.08f * 0.08f; 

            clean.Add(source[0]);
            indexMap.Add(0);

            for (int i = 1; i < capacity; i++)
            {
                Vector2 prev = clean[clean.Count - 1];
                Vector2 curr = source[i];

                if ((curr - prev).sqrMagnitude <= minSqDist) continue;

                // Anti-Spike: If we go A->B->A, ignore B
                if (clean.Count > 1)
                {
                    Vector2 prePrev = clean[clean.Count - 2];
                    if ((curr - prePrev).sqrMagnitude < minSqDist)
                    {
                        clean.RemoveAt(clean.Count - 1);
                        indexMap.RemoveAt(indexMap.Count - 1);
                        continue;
                    }
                }

                clean.Add(curr);
                indexMap.Add(i);
            }

            // Closure check
            if (clean.Count > 1)
            {
                 if ((clean[clean.Count - 1] - clean[0]).sqrMagnitude < minSqDist)
                 {
                     clean.RemoveAt(clean.Count - 1);
                     indexMap.RemoveAt(indexMap.Count - 1);
                 }
            }

            // Collinear check
            if (clean.Count >= 3)
            {
                for (int i = clean.Count - 2; i > 0; i--)
                {
                    if (IsCollinear(clean[i - 1], clean[i], clean[i + 1]))
                    {
                        clean.RemoveAt(i);
                        indexMap.RemoveAt(i);
                    }
                }
            }

            return clean;
        }

        private static bool IsCollinear(Vector2 a, Vector2 b, Vector2 c)
        {
            float val = (b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x);
            return Mathf.Abs(val) < 0.05f;
        }

        // --- STANDARD EAR CLIPPING (Unchanged) ---
        private static float SignedArea(List<Vector2> p)
        {
            float a = 0f;
            for (int i = 0; i < p.Count; i++)
            {
                Vector2 c = p[i];
                Vector2 n = p[(i + 1) % p.Count];
                a += (c.x * n.y) - (n.x * c.y);
            }
            return a * 0.5f;
        }

        private static List<int> TriangulateEarClip(List<Vector2> poly)
        {
            int n = poly.Count;
            List<int> indices = new List<int>(n);
            for (int i = 0; i < n; i++) indices.Add(i);
            List<int> tris = new List<int>((n - 2) * 3);
            int guard = 0;

            while (indices.Count > 2)
            {
                if (guard++ > 2000) return null; // FAIL

                bool clipped = false;
                for (int i = 0; i < indices.Count; i++)
                {
                    int i0 = indices[(i - 1 + indices.Count) % indices.Count];
                    int i1 = indices[i];
                    int i2 = indices[(i + 1) % indices.Count];

                    Vector2 a = poly[i0];
                    Vector2 b = poly[i1];
                    Vector2 c = poly[i2];

                    if (!IsConvex(a, b, c)) continue;

                    bool hasPointInside = false;
                    for (int k = 0; k < indices.Count; k++)
                    {
                        int ik = indices[k];
                        if (ik == i0 || ik == i1 || ik == i2) continue;
                        if (PointInTriangle(poly[ik], a, b, c)) { hasPointInside = true; break; }
                    }

                    if (hasPointInside) continue;

                    tris.Add(i0); tris.Add(i1); tris.Add(i2);
                    indices.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) return null; // FAIL
            }
            return tris;
        }

        private static bool IsConvex(Vector2 a, Vector2 b, Vector2 c)
        {
            float cross = (b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x);
            return cross > -0.001f;
        }

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 v0 = c - a, v1 = b - a, v2 = p - a;
            float d00 = Vector2.Dot(v0, v0), d01 = Vector2.Dot(v0, v1), d02 = Vector2.Dot(v0, v2);
            float d11 = Vector2.Dot(v1, v1), d12 = Vector2.Dot(v1, v2);
            float inv = 1f / (d00 * d11 - d01 * d01);
            float u = (d11 * d02 - d01 * d12) * inv, v = (d00 * d12 - d01 * d02) * inv;
            return (u >= -0.01f) && (v >= -0.01f) && (u + v <= 1.01f);
        }

        public static Mesh BuildExtrudedTriangleXZ(Vector2 a, Vector2 b, Vector2 c, float thickness)
        {
            // Same as before
            float halfY = Mathf.Max(0.001f, thickness * 0.5f);
            Vector3[] v = new Vector3[6];
            v[0] = new Vector3(a.x, +halfY, a.y);
            v[1] = new Vector3(b.x, +halfY, b.y);
            v[2] = new Vector3(c.x, +halfY, c.y);
            v[3] = new Vector3(a.x, -halfY, a.y);
            v[4] = new Vector3(b.x, -halfY, b.y);
            v[5] = new Vector3(c.x, -halfY, c.y);
            int[] t = { 0,1,2, 3,5,4, 0,4,1, 0,3,4, 1,5,2, 1,4,5, 2,3,0, 2,5,3 };
            Mesh m = new Mesh { name = "ZoneTri" };
            m.vertices = v; m.triangles = t; m.RecalculateNormals();
            return m;
        }
    }
}