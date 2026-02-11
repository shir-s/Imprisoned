using System.Collections.Generic;
using UnityEngine;
using System.Linq;

namespace JellyGame.GamePlay.Abilities.Zones
{
    public static class ZoneMeshBuilder
    {
        public static List<int> TriangulatePolygonXZ(IReadOnlyList<Vector2> polyXZ, out List<Vector2> resultPoly)
        {
            //default return polygon is the original one (after sanitization), not a convex hull or any other modified version.
            resultPoly = null;

            if (polyXZ == null || polyXZ.Count < 3)
                return null;

            // 1. Sanitize basic cleanup
            List<int> map;
            List<Vector2> cleanPoly = SanitizePolygon(polyXZ, out map);

            if (cleanPoly.Count < 3) return null;

            // 2. Ensure CCW (clock wise order)
            if (SignedArea(cleanPoly) < 0f)
            {
                cleanPoly.Reverse();
                map.Reverse();
            }

            // 3. TRY CLEAN METHOD (Standard Ear Clipping)
            // trys to do it by the book, fails if there are self crosses
            List<int> tris = TriangulateEarClip(cleanPoly, useValidation: true);

            // 4. FALLBACK: DIRTY METHOD (No Validation)
            // if the clean way failed we turn off security checks (PointInTriangle).
            // cut off every "ear like" corner .
            // keeps it (Concave) instead of (Convex)!
            if (tris == null)
            {
                //Debug.LogWarning($"[ZoneMeshBuilder] Clean triangulation failed. Switching to DIRTY mode. Points: {cleanPoly.Count}");
                tris = TriangulateEarClip(cleanPoly, useValidation: false);
            }

            // 5. FINAL FALLBACK: FAN (Center to Edges)
            // if the dirty fails, use a simple fan triangulation. This will produce bad triangles for complex shapes, but it will at least produce something valid and keep the original vertices.
            if (tris == null)
            {
                Debug.LogWarning($"[ZoneMeshBuilder] Dirty triangulation failed. Using FAN fallback.");
                tris = TriangulateFan(cleanPoly);
            }

            // map back to original indices
            for (int i = 0; i < tris.Count; i++)
            {
                tris[i] = map[tris[i]];
            }

            // return the original polygon cleaned up, not the triangulated version. This allows the caller to have the original vertices for things like physics or visual effects, while still getting a valid triangulation for mesh generation.
            resultPoly = new List<Vector2>(polyXZ); 
            return tris;
        }

        // --- CORE ALGORITHM ---

        private static List<int> TriangulateEarClip(List<Vector2> polyPoints, bool useValidation)
        {
            //local copy of points, we will be modifying this list as we clip ears
            int n = polyPoints.Count;
            if (n < 3) return null;

            List<int> indices = new List<int>(n);
            for (int i = 0; i < n; i++) indices.Add(i);

            List<int> tris = new List<int>((n - 2) * 3);
            int loopGuard = 0;

            while (indices.Count > 2)
            {
                loopGuard++;
                if (loopGuard > 3000) break; // Infinite loop protection

                bool clipped = false;

                for (int i = 0; i < indices.Count; i++)
                {
                    int iPrev = indices[(i - 1 + indices.Count) % indices.Count];
                    int iCurr = indices[i];
                    int iNext = indices[(i + 1) % indices.Count];

                    Vector2 a = polyPoints[iPrev];
                    Vector2 b = polyPoints[iCurr];
                    Vector2 c = polyPoints[iNext];

                    // check 1: is the angle convex? (Convex)
                    //if useValidation is false, we skip this check, allowing us to cut "ears" even if they are not strictly convex. This can help in cases of self-intersecting polygons or very tight angles, where the strict convexity check might fail but we still want to produce a triangulation.
                    if (!IsConvex(a, b, c)) continue;

                    // check 2: is there any other point inside the triangle formed by (a,b,c)? (Not Self-Intersecting)
                    if (useValidation)
                    {
                        bool hasPointInside = false;
                        for (int k = 0; k < indices.Count; k++)
                        {
                            int idx = indices[k];
                            if (idx == iPrev || idx == iCurr || idx == iNext) continue;
                            if (PointInTriangle(polyPoints[idx], a, b, c))
                            {
                                hasPointInside = true;
                                break;
                            }
                        }
                        if (hasPointInside) continue;
                    }

                    // cut the ear
                    tris.Add(iPrev);
                    tris.Add(iCurr);
                    tris.Add(iNext);
                    indices.RemoveAt(i);
                    clipped = true;
                    break;
                }

                //if we went through a full loop without clipping, it means we are stuck. This can happen if the polygon is malformed (e.g., self-intersecting) or if we are in a very degenerate case. In this situation, we have two options:
                if (!clipped)
                {
                    if (useValidation) 
                    {
                        // in clean state - this is a failure.
                        return null; 
                    }
                    else
                    {
                        //in dirty state - agressive.
                        // just cut current index with force.
                        int i0 = indices[indices.Count - 1];
                        int i1 = indices[0];
                        int i2 = indices[1];
                        
                        tris.Add(i0);
                        tris.Add(i1);
                        tris.Add(i2);
                        indices.RemoveAt(0);
                    }
                }
            }

            return tris;
        }

        private static List<int> TriangulateFan(List<Vector2> poly)
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

        // --- MATH HELPERS ---

        private static List<Vector2> SanitizePolygon(IReadOnlyList<Vector2> source, out List<int> indexMap)
        {
            int capacity = source.Count;
            List<Vector2> clean = new List<Vector2>(capacity);
            indexMap = new List<int>(capacity);

            if (capacity == 0) return clean;

            //minimum distance between points to consider them distinct. This helps remove noise and very close vertices that can cause triangulation issues.
            //float minSqDist = 0.05f * 0.05f; 
            float minDist = 0.001f; 
            float minSqDist = minDist * minDist;

            clean.Add(source[0]);
            indexMap.Add(0);

            for (int i = 1; i < capacity; i++)
            {
                Vector2 prev = clean[clean.Count - 1];
                Vector2 curr = source[i];

                if ((curr - prev).sqrMagnitude <= minSqDist) continue;
                clean.Add(curr);
                indexMap.Add(i);
            }
            
            // circular check: if the last point is very close to the first, we can remove it to avoid issues with "almost closed" loops.
            if (clean.Count > 2)
            {
                if ((clean[clean.Count - 1] - clean[0]).sqrMagnitude <= minSqDist)
                {
                    clean.RemoveAt(clean.Count - 1);
                    indexMap.RemoveAt(indexMap.Count - 1);
                }
            }

            return clean;
        }

        private static bool IsConvex(Vector2 a, Vector2 b, Vector2 c)
        {
            // check if the turn was to the left side
            return ((b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x)) > -0.0001f;
        }

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 v0 = c - a, v1 = b - a, v2 = p - a;
            float d00 = Vector2.Dot(v0, v0), d01 = Vector2.Dot(v0, v1), d02 = Vector2.Dot(v0, v2);
            float d11 = Vector2.Dot(v1, v1), d12 = Vector2.Dot(v1, v2);
            float inv = 1f / (d00 * d11 - d01 * d01);
            float u = (d11 * d02 - d01 * d12) * inv, v = (d00 * d12 - d01 * d02) * inv;
            return (u >= 0) && (v >= 0) && (u + v <= 1);
        }

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
            int[] t = { 0,1,2, 3,5,4, 0,4,1, 0,3,4, 1,5,2, 1,4,5, 2,3,0, 2,5,3 };
            Mesh m = new Mesh { name = "ZoneTri" };
            m.vertices = v; m.triangles = t; m.RecalculateNormals();
            return m;
        }
    }
}