// FILEPATH: Assets/Scripts/Abilities/Zones/AbilityZoneDebugGhost.cs
using System.Collections.Generic;
using UnityEngine;

namespace JellyGame.GamePlay.Abilities.Zones
{
    /// <summary>
    /// Debug-only "ghost" that persists after the real zone is destroyed.
    /// It draws the exact triangle prism meshes in world-space.
    /// </summary>
    [DisallowMultipleComponent]
    public class AbilityZoneDebugGhost : MonoBehaviour
    {
        [Header("Lifetime")]
        [SerializeField] private float lifetimeSeconds = 30f;

        [Header("Draw")]
        [SerializeField] private Color lineColor = new Color(0.1f, 1f, 0.2f, 1f);
        [SerializeField] private float yOffset = 0.03f;
        [SerializeField] private bool drawTriangles = true;
        [SerializeField] private bool drawBounds = true;

        private float _lifeTimer;

        // Stored in WORLD space so it doesn't depend on the surface/zone being alive.
        private readonly List<Vector3> _worldLinesA = new List<Vector3>();
        private readonly List<Vector3> _worldLinesB = new List<Vector3>();
        private Bounds _bounds;
        private bool _hasBounds;

        public void InitializeFromZone(GameObject zoneRoot, float keepSeconds, Color color, float offsetY)
        {
            lifetimeSeconds = Mathf.Max(0.05f, keepSeconds);
            lineColor = color;
            yOffset = offsetY;

            _lifeTimer = lifetimeSeconds;

            BakeWorldGeometry(zoneRoot);
        }

        private void Awake()
        {
            _lifeTimer = lifetimeSeconds;
        }

        private void Update()
        {
            _lifeTimer -= Time.deltaTime;
            if (_lifeTimer <= 0f)
                Destroy(gameObject);
        }

        private void OnDrawGizmos()
        {
            if (_worldLinesA.Count == 0)
                return;

            Gizmos.color = lineColor;

            if (drawBounds && _hasBounds)
                DrawWireBox(_bounds.center + Vector3.up * yOffset, _bounds.extents);

            if (!drawTriangles)
                return;

            for (int i = 0; i < _worldLinesA.Count; i++)
            {
                Gizmos.DrawLine(_worldLinesA[i] + Vector3.up * yOffset, _worldLinesB[i] + Vector3.up * yOffset);
            }
        }

        private void BakeWorldGeometry(GameObject zoneRoot)
        {
            _worldLinesA.Clear();
            _worldLinesB.Clear();
            _hasBounds = false;

            if (zoneRoot == null)
                return;

            var colliders = zoneRoot.GetComponentsInChildren<MeshCollider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                var mc = colliders[i];
                if (mc == null || !mc.isTrigger) continue;

                var mesh = mc.sharedMesh;
                if (mesh == null) continue;

                var t = mc.transform;
                var v = mesh.vertices;
                var tris = mesh.triangles;

                // bounds
                if (!_hasBounds)
                {
                    _bounds = mc.bounds;
                    _hasBounds = true;
                }
                else
                {
                    _bounds.Encapsulate(mc.bounds);
                }

                // wireframe edges for every triangle
                for (int ti = 0; ti < tris.Length; ti += 3)
                {
                    Vector3 a = t.TransformPoint(v[tris[ti + 0]]);
                    Vector3 b = t.TransformPoint(v[tris[ti + 1]]);
                    Vector3 c = t.TransformPoint(v[tris[ti + 2]]);

                    AddLine(a, b);
                    AddLine(b, c);
                    AddLine(c, a);
                }
            }
        }

        private void AddLine(Vector3 a, Vector3 b)
        {
            _worldLinesA.Add(a);
            _worldLinesB.Add(b);
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

            Gizmos.DrawLine(p0, p1); Gizmos.DrawLine(p1, p2); Gizmos.DrawLine(p2, p3); Gizmos.DrawLine(p3, p0);
            Gizmos.DrawLine(p4, p5); Gizmos.DrawLine(p5, p6); Gizmos.DrawLine(p6, p7); Gizmos.DrawLine(p7, p4);
            Gizmos.DrawLine(p0, p4); Gizmos.DrawLine(p1, p5); Gizmos.DrawLine(p2, p6); Gizmos.DrawLine(p3, p7);
        }
    }
}
