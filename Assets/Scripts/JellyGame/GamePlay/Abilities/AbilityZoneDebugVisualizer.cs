// FILEPATH: Assets/Scripts/Abilities/Zones/AbilityZoneDebugVisualizer.cs
using System.Collections.Generic;
using UnityEngine;

namespace JellyGame.GamePlay.Abilities.Zones
{
    /// <summary>
    /// Runtime debug visualization for AbilityZone.
    /// Draws:
    /// - wireframe bounds (approx)
    /// - all triangle collider prisms edges (exact)
    /// - optional surface normal ray
    ///
    /// This is meant to be added at runtime by the ability when debug is enabled.
    /// </summary>
    [DisallowMultipleComponent]
    public class AbilityZoneDebugVisualizer : MonoBehaviour
    {
        [Header("Draw")]
        [SerializeField] private bool draw = true;
        [SerializeField] private bool drawTriangles = true;
        [SerializeField] private bool drawBounds = true;
        [SerializeField] private bool drawNormal = true;

        [Header("Style")]
        [SerializeField] private Color lineColor = new Color(0.1f, 1f, 0.2f, 1f);
        [SerializeField] private float yOffset = 0.03f;

        private readonly List<MeshCollider> _triangleColliders = new List<MeshCollider>();
        private Transform _cachedTransform;

        public void Configure(Color color, float offsetY = 0.03f, bool show = true)
        {
            lineColor = color;
            yOffset = offsetY;
            draw = show;
        }

        private void Awake()
        {
            _cachedTransform = transform;

            _triangleColliders.Clear();
            var all = GetComponentsInChildren<MeshCollider>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].isTrigger)
                    _triangleColliders.Add(all[i]);
            }
        }

        private void OnDrawGizmos()
        {
            if (!draw)
                return;

            Gizmos.color = lineColor;

            if (drawBounds)
                DrawCombinedBounds();

            if (drawTriangles)
                DrawTrianglePrisms();

            if (drawNormal)
                DrawNormalRay();
        }

        private void DrawCombinedBounds()
        {
            if (_triangleColliders.Count == 0)
                return;

            Bounds b = _triangleColliders[0].bounds;
            for (int i = 1; i < _triangleColliders.Count; i++)
            {
                if (_triangleColliders[i] != null)
                    b.Encapsulate(_triangleColliders[i].bounds);
            }

            // Slight lift so it doesn't Z-fight with the surface visually.
            Vector3 c = b.center + Vector3.up * yOffset;

            DrawWireBox(c, b.extents);
        }

        private void DrawTrianglePrisms()
        {
            for (int i = 0; i < _triangleColliders.Count; i++)
            {
                MeshCollider mc = _triangleColliders[i];
                if (mc == null) continue;

                Mesh mesh = mc.sharedMesh;
                if (mesh == null) continue;

                // Mesh is in mc local space (triGo). Convert to world.
                Transform t = mc.transform;

                Vector3[] v = mesh.vertices;
                int[] tris = mesh.triangles;

                // Draw edges of all triangles (wireframe).
                for (int ti = 0; ti < tris.Length; ti += 3)
                {
                    Vector3 a = t.TransformPoint(v[tris[ti + 0]]) + Vector3.up * yOffset;
                    Vector3 b = t.TransformPoint(v[tris[ti + 1]]) + Vector3.up * yOffset;
                    Vector3 c = t.TransformPoint(v[tris[ti + 2]]) + Vector3.up * yOffset;

                    Gizmos.DrawLine(a, b);
                    Gizmos.DrawLine(b, c);
                    Gizmos.DrawLine(c, a);
                }
            }
        }

        private void DrawNormalRay()
        {
            // The zone is parented to the surface, so up is "surface normal" only if surface has no extra roll,
            // but still useful to see orientation.
            Vector3 origin = _cachedTransform.position + Vector3.up * (yOffset + 0.05f);
            Vector3 dir = _cachedTransform.up * 0.6f;
            Gizmos.DrawLine(origin, origin + dir);
        }

        private static void DrawWireBox(Vector3 center, Vector3 extents)
        {
            Vector3 p0 = center + new Vector3(-extents.x, -extents.y, -extents.z);
            Vector3 p1 = center + new Vector3(+extents.x, -extents.y, -extents.z);
            Vector3 p2 = center + new Vector3(+extents.x, -extents.y, +extents.z);
            Vector3 p3 = center + new Vector3(-extents.x, -extents.y, +extents.z);

            Vector3 p4 = center + new Vector3(-extents.x, +extents.y, -extents.z);
            Vector3 p5 = center + new Vector3(+extents.x, +extents.y, -extents.z);
            Vector3 p6 = center + new Vector3(+extents.x, +extents.y, +extents.z);
            Vector3 p7 = center + new Vector3(-extents.x, +extents.y, +extents.z);

            // bottom
            Gizmos.DrawLine(p0, p1);
            Gizmos.DrawLine(p1, p2);
            Gizmos.DrawLine(p2, p3);
            Gizmos.DrawLine(p3, p0);

            // top
            Gizmos.DrawLine(p4, p5);
            Gizmos.DrawLine(p5, p6);
            Gizmos.DrawLine(p6, p7);
            Gizmos.DrawLine(p7, p4);

            // verticals
            Gizmos.DrawLine(p0, p4);
            Gizmos.DrawLine(p1, p5);
            Gizmos.DrawLine(p2, p6);
            Gizmos.DrawLine(p3, p7);
        }
    }
}
