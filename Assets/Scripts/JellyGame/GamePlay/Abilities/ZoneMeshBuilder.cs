// FILEPATH: Assets/Scripts/Abilities/Zones/ZoneMeshBuilder.cs
using System.Collections.Generic;
using UnityEngine;

namespace JellyGame.GamePlay.Abilities.Zones
{
    /// <summary>
    /// Triangulates a polygon (XZ) and can build convex extruded triangle meshes.
    /// We use one convex trigger MeshCollider per triangle => supports ANY concave polygon exactly.
    /// </summary>
    public static class ZoneMeshBuilder
    {
        public static List<int> TriangulatePolygonXZ(IReadOnlyList<Vector2> polyXZ)
        {
            if (polyXZ == null || polyXZ.Count < 3)
                return null;

            List<Vector2> poly = new List<Vector2>(polyXZ);

            // Ear clipping below assumes CCW
            if (SignedArea(poly) < 0f)
                poly.Reverse();

            return TriangulateEarClip(poly);
        }

        public static Mesh BuildExtrudedTriangleXZ(Vector2 a, Vector2 b, Vector2 c, float thickness)
        {
            float halfY = Mathf.Max(0.001f, thickness * 0.5f);

            // 6 verts: top (a,b,c) and bottom (a,b,c)
            Vector3[] v = new Vector3[6];
            v[0] = new Vector3(a.x, +halfY, a.y);
            v[1] = new Vector3(b.x, +halfY, b.y);
            v[2] = new Vector3(c.x, +halfY, c.y);

            v[3] = new Vector3(a.x, -halfY, a.y);
            v[4] = new Vector3(b.x, -halfY, b.y);
            v[5] = new Vector3(c.x, -halfY, c.y);

            // Triangles:
            // Top: 0,1,2
            // Bottom: 3,5,4 (reversed)
            // Sides: three quads (each 2 tris)
            int[] t =
            {
                0,1,2,
                3,5,4,

                0,4,1, 0,3,4, // edge a-b
                1,5,2, 1,4,5, // edge b-c
                2,3,0, 2,5,3  // edge c-a
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
            List<int> indices = new List<int>(n);
            for (int i = 0; i < n; i++) indices.Add(i);

            List<int> tris = new List<int>((n - 2) * 3);

            int guard = 0;
            while (indices.Count > 3 && guard++ < 5000)
            {
                bool clipped = false;

                for (int i = 0; i < indices.Count; i++)
                {
                    int i0 = indices[(i - 1 + indices.Count) % indices.Count];
                    int i1 = indices[i];
                    int i2 = indices[(i + 1) % indices.Count];

                    Vector2 a = poly[i0];
                    Vector2 b = poly[i1];
                    Vector2 c = poly[i2];

                    if (!IsConvex(a, b, c))
                        continue;

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

                    tris.Add(i0);
                    tris.Add(i1);
                    tris.Add(i2);

                    indices.RemoveAt(i);
                    clipped = true;
                    break;
                }

                if (!clipped)
                    return null;
            }

            if (indices.Count == 3)
            {
                tris.Add(indices[0]);
                tris.Add(indices[1]);
                tris.Add(indices[2]);
            }

            return tris;
        }

        private static bool IsConvex(Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 ab = b - a;
            Vector2 bc = c - b;
            float cross = ab.x * bc.y - ab.y * bc.x;
            return cross > 0f;
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

            return (u >= 0f) && (v >= 0f) && (u + v <= 1f);
        }
    }
}
