// FILEPATH: Assets/Scripts/Abilities/Zones/DamageZoneAbility.cs
using UnityEngine;

namespace JellyGame.GamePlay.Abilities.Zones
{
    [CreateAssetMenu(menuName = "JellyGame/Abilities/Damage Zone Ability")]
    public class DamageZoneAbility : ScriptableObject, IPlayerAbility
    {
        [Header("Visuals")]
        [SerializeField] private Color paintColor = new Color(1f, 0.2f, 0.2f, 1f);

        [Header("Zone")]
        [SerializeField] private float zoneLifetimeSeconds = 4f;
        [SerializeField] private float zoneThickness = 1.2f;
        [SerializeField] private float effectTickInterval = 0.2f;
        [SerializeField] private LayerMask damageTargetLayers = ~0;

        [Header("Damage")]
        [SerializeField] private float damagePerSecond = 4f;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        [Tooltip("Draw gizmos on the live zone while it exists.")]
        [SerializeField] private bool debugDrawZoneGizmos = true;

        [Tooltip("Spawn a separate ghost object that keeps the gizmos after the zone is destroyed.")]
        [SerializeField] private bool debugSpawnGhost = true;

        [Tooltip("How long the ghost stays (seconds).")]
        [SerializeField] private float debugGhostLifetimeSeconds = 30f;

        [SerializeField] private float debugDrawYOffset = 0.03f;

        public Color PaintColor => paintColor;
        public bool CanSpawnZone => true;

        public void SpawnZone(AbilityZoneContext ctx)
        {
            if (ctx.Surface == null || ctx.LocalPolygonXZ == null || ctx.LocalPolygonXZ.Count < 3)
                return;

            GameObject root = new GameObject("AbilityZone_Damage");
            Transform rt = root.transform;
            rt.SetParent(ctx.Surface.transform, false);

            Vector3 localCenter = ctx.LocalBounds.center;
            rt.localPosition = new Vector3(localCenter.x, 0f, localCenter.z);
            rt.localRotation = Quaternion.identity;
            rt.localScale = Vector3.one;

            AbilityZone zone = root.AddComponent<AbilityZone>();
            zone.Configure(zoneLifetimeSeconds, effectTickInterval, damageTargetLayers, debugLogs);

            DamageZoneEffect dmg = root.AddComponent<DamageZoneEffect>();
            dmg.Configure(damagePerSecond, debugLogs);

            if (debugDrawZoneGizmos)
            {
                var viz = root.AddComponent<AbilityZoneDebugVisualizer>();
                viz.Configure(paintColor, debugDrawYOffset, true);
            }

            var shifted = ShiftPolygon(ctx.LocalPolygonXZ, -new Vector2(localCenter.x, localCenter.z));

            var tris = ZoneMeshBuilder.TriangulatePolygonXZ(shifted);
            if (tris == null || tris.Count < 3)
            {
                Object.Destroy(root);
                return;
            }

            for (int i = 0; i < tris.Count; i += 3)
            {
                int ia = tris[i + 0];
                int ib = tris[i + 1];
                int ic = tris[i + 2];

                Vector2 a = shifted[ia];
                Vector2 b = shifted[ib];
                Vector2 c = shifted[ic];

                GameObject triGo = new GameObject($"ZoneTri_{i / 3}");
                triGo.transform.SetParent(rt, false);

                MeshCollider mc = triGo.AddComponent<MeshCollider>();
                mc.sharedMesh = ZoneMeshBuilder.BuildExtrudedTriangleXZ(a, b, c, zoneThickness);
                mc.convex = true;
                mc.isTrigger = true;

                triGo.AddComponent<ZoneTriggerForwarder>();
            }

            // IMPORTANT: spawn ghost AFTER colliders exist (so it can bake them).
            if (debugSpawnGhost)
            {
                GameObject ghostGo = new GameObject("AbilityZone_DebugGhost");
                var ghost = ghostGo.AddComponent<AbilityZoneDebugGhost>();
                ghost.InitializeFromZone(root, debugGhostLifetimeSeconds, paintColor, debugDrawYOffset);
            }

            if (debugLogs)
                Debug.Log($"[DamageZoneAbility] Spawned zone with {tris.Count / 3} triangle triggers.", root);
        }

        private static System.Collections.Generic.List<Vector2> ShiftPolygon(System.Collections.Generic.IReadOnlyList<Vector2> poly, Vector2 delta)
        {
            var res = new System.Collections.Generic.List<Vector2>(poly.Count);
            for (int i = 0; i < poly.Count; i++)
                res.Add(poly[i] + delta);
            return res;
        }
    }
}
