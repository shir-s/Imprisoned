using System.Collections.Generic;
using UnityEngine;

namespace JellyGame.GamePlay.Abilities.Zones
{
    /// <summary>
    /// Triangulates a polygon (XZ) and can build convex extruded triangle meshes.
    /// Includes sanitization to prevent failures when strokes bunch up against walls.
    /// </summary>
    public static class ZoneMeshBuilder
    {
        /// <summary>
        /// Returns triangle indices that ALWAYS refer to the ORIGINAL <paramref name="polyXZ"/> indexing.
        /// </summary>
        public static List<int> TriangulatePolygonXZ(IReadOnlyList<Vector2> polyXZ)
        {
            if (polyXZ == null || polyXZ.Count < 3)
                return null;

            // --- STEP 1: SANITIZE ---
            // We create a clean list of points to perform the math on.
            // We also need a mapping from "Clean Index" back to "Original Index"
            // so we can return indices that point to the original raw data.
            List<int> cleanToOriginalMap;
            List<Vector2> cleanPoly = SanitizePolygon(polyXZ, out cleanToOriginalMap);

            if (cleanPoly.Count < 3)
            {
                Debug.LogWarning($"[ZoneMeshBuilder] Polygon collapsed to {cleanPoly.Count} points after sanitization. Too small to fill.");
                return null;
            }

            // --- STEP 2: WINDING ORDER ---
            // Ear clipping assumes CCW.
            bool reversed = (SignedArea(cleanPoly) < 0f);
            if (reversed)
            {
                cleanPoly.Reverse();
                // We must also reverse our index map so we point to the right original verts
                cleanToOriginalMap.Reverse();
            }

            // --- STEP 3: TRIANGULATE ---
            List<int> tris = TriangulateEarClip(cleanPoly);
            
            if (tris == null)
            {
                Debug.LogWarning($"[ZoneMeshBuilder] Triangulation failed (Guard hit). Poly count: {polyXZ.Count} -> Clean: {cleanPoly.Count}");
                return null;
            }

            // --- STEP 4: REMAP INDICES ---
            // The triangulation returned indices into 'cleanPoly'.
            // We need to convert them back to indices into 'polyXZ' (the original list).
            for (int i = 0; i < tris.Count; i++)
            {
                int cleanIndex = tris[i];
                tris[i] = cleanToOriginalMap[cleanIndex];
            }

            return tris;
        }

        /// <summary>
        /// Removes duplicate/close points and collinear vertices that confuse the ear-clipper.
        /// </summary>
        private static List<Vector2> SanitizePolygon(IReadOnlyList<Vector2> source, out List<int> indexMap)
        {
            int capacity = source.Count;
            List<Vector2> clean = new List<Vector2>(capacity);
            indexMap = new List<int>(capacity);

            if (capacity == 0) return clean;

            // 1. Filter out points that are too close to the previous point
            //    When hitting a wall, you often get 5-10 points in the exact same spot.
            float minSqDist = 0.02f * 0.02f; 

            clean.Add(source[0]);
            indexMap.Add(0);

            for (int i = 1; i < capacity; i++)
            {
                Vector2 prev = clean[clean.Count - 1];
                Vector2 curr = source[i];

                if ((curr - prev).sqrMagnitude > minSqDist)
                {
                    clean.Add(curr);
                    indexMap.Add(i);
                }
            }

            // 1b. Check loop closure (last point vs first point)
            if (clean.Count > 1)
            {
                Vector2 last = clean[clean.Count - 1];
                Vector2 first = clean[0];
                if ((last - first).sqrMagnitude < minSqDist)
                {
                    clean.RemoveAt(clean.Count - 1);
                    indexMap.RemoveAt(indexMap.Count - 1);
                }
            }

            // 2. Simplify collinear points (straight lines)
            //    Ear clipping gets stuck if there are 180-degree angles (flat lines).
            if (clean.Count >= 3)
            {
                // We iterate backwards so removing doesn't mess up future indices
                for (int i = clean.Count - 2; i > 0; i--)
                {
                    Vector2 pPrev = clean[i - 1];
                    Vector2 pCurr = clean[i];
                    Vector2 pNext = clean[i + 1];

                    if (IsCollinear(pPrev, pCurr, pNext))
                    {
                        clean.RemoveAt(i);
                        indexMap.RemoveAt(i);
                    }
                }
                
                // Check wrap-around collinearity (Last point relative to First and Second-to-Last)
                if (clean.Count >= 3)
                {
                     Vector2 pPrev = clean[clean.Count - 2];
                     Vector2 pCurr = clean[clean.Count - 1];
                     Vector2 pNext = clean[0];
                     
                     if (IsCollinear(pPrev, pCurr, pNext))
                     {
                         clean.RemoveAt(clean.Count - 1);
                         indexMap.RemoveAt(indexMap.Count - 1);
                     }
                }
            }

            return clean;
        }

        private static bool IsCollinear(Vector2 a, Vector2 b, Vector2 c)
        {
            // Cross product of (b-a) and (c-b). If near 0, they are parallel/collinear.
            float val = (b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x);
            return Mathf.Abs(val) < 0.01f;
        }

        public static Mesh BuildExtrudedTriangleXZ(Vector2 a, Vector2 b, Vector2 c, float thickness)
        {
            float halfY = Mathf.Max(0.001f, thickness * 0.5f);

            Vector3[] v = new Vector3[6];
            v[0] = new Vector3(a.x, +halfY, a.y);
            v[1] = new Vector3(b.x, +halfY, b.y);
            v[2] = new Vector3(c.x, +halfY, c.y);

            v[3] = new Vector3(a.x, -halfY, a.y);
            v[4] = new Vector3(b.x, -halfY, b.y);
            v[5] = new Vector3(c.x, -halfY, c.y);

            int[] t =
            {
                0,1,2,
                3,5,4,
                0,4,1, 0,3,4,
                1,5,2, 1,4,5,
                2,3,0, 2,5,3
            };

            Mesh m = new Mesh { name = "ZoneTrianglePrism" };
            m.vertices = v;
            m.triangles = t;
            m.RecalculateBounds();
            m.RecalculateNormals();
            return m;
        }

        // ----------------- Ear clipping triangulation -----------------

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
            // Create a local index list 0,1,2...n-1
            List<int> indices = new List<int>(n);
            for (int i = 0; i < n; i++) indices.Add(i);

            List<int> tris = new List<int>((n - 2) * 3);

            int guard = 0;
            // Slightly increased guard and count check
            while (indices.Count > 2 && guard++ < 5000)
            {
                bool clipped = false;

                for (int i = 0; i < indices.Count; i++)
                {
                    // Get indices in the CURRENT (shrinking) polygon
                    int i0 = indices[(i - 1 + indices.Count) % indices.Count];
                    int i1 = indices[i];
                    int i2 = indices[(i + 1) % indices.Count];

                    Vector2 a = poly[i0];
                    Vector2 b = poly[i1];
                    Vector2 c = poly[i2];

                    // 1. Must be convex corner
                    if (!IsConvex(a, b, c))
                        continue;

                    // 2. Must not contain any other vertex
                    bool hasPointInside = false;
                    for (int k = 0; k < indices.Count; k++)
                    {
                        int ik = indices[k];
                        if (ik == i0 || ik == i1 || ik == i2) continue;

                        if (PointInTriangle(poly[ik], a, b, c))
                        {
                            hasPointInside = true;
                            break;
                        }
                    }

                    if (hasPointInside)
                        continue;

                    // Valid ear found
                    tris.Add(i0);
                    tris.Add(i1);
                    tris.Add(i2);

                    indices.RemoveAt(i);
                    clipped = true;
                    break;
                }

                // If we went through all vertices and found no ears, the poly is invalid (self-intersecting or degenerate)
                if (!clipped)
                    return null;
            }

            return tris;
        }

        private static bool IsConvex(Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 ab = b - a;
            Vector2 bc = c - b;
            float cross = ab.x * bc.y - ab.y * bc.x;
            // Relaxed epsilon slightly to handle float imprecision
            return cross > -0.0001f; 
        }

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 v0 = c - a;
            Vector2 v1 = b - a;
            Vector2 v2 = p - a;

            float dot00 = Vector2.Dot(v0, v0);
            float dot01 = Vector2.Dot(v0, v1);
            float dot02 = Vector2.Dot(v0, v2);
            float dot11 = Vector2.Dot(v1, v1);
            float dot12 = Vector2.Dot(v1, v2);

            float denom = (dot00 * dot11 - dot01 * dot01);
            if (Mathf.Abs(denom) < 1e-8f)
                return false;

            float inv = 1f / denom;
            float u = (dot11 * dot02 - dot01 * dot12) * inv;
            float v = (dot00 * dot12 - dot01 * dot02) * inv;

            // Relaxed check to avoid edge-case failures
            return (u >= -0.01f) && (v >= -0.01f) && (u + v <= 1.01f);
        }
    }
}