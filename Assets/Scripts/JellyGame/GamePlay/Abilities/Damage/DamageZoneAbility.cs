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
        [SerializeField] private bool debugDrawZoneGizmos = true;
        [SerializeField] private bool debugSpawnGhost = true;
        [SerializeField] private float debugGhostLifetimeSeconds = 30f;
        [SerializeField] private float debugDrawYOffset = 0.03f;

        public Color PaintColor => paintColor;
        public bool CanSpawnZone => true;

        public void SpawnZone(AbilityZoneContext ctx)
        {
            // 1. Initial Validation
            if (ctx.Surface == null || ctx.LocalPolygonXZ == null || ctx.LocalPolygonXZ.Count < 3)
            {
                Debug.LogWarning("[DamageZoneAbility] Spawn aborted: Invalid context or empty polygon.");
                return;
            }

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

            // 2. Prepare Polygon for Triangulation
            var shifted = ShiftPolygon(ctx.LocalPolygonXZ, -new Vector2(localCenter.x, localCenter.z));

            // 3. Attempt Triangulation
            var tris = ZoneMeshBuilder.TriangulatePolygonXZ(shifted);

            // 4. CHECK FOR FAILURE
            if (tris == null || tris.Count < 3)
            {
                // HERE IS THE PROBLEM: If we reach here, physics will not work.
                //Debug.LogError($"[DamageZoneAbility] TRIANGULATION FAILED. Destroying Zone. Point Count: {shifted.Count}");
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

            if (debugSpawnGhost)
            {
                GameObject ghostGo = new GameObject("AbilityZone_DebugGhost");
                var ghost = ghostGo.AddComponent<AbilityZoneDebugGhost>();
                ghost.InitializeFromZone(root, debugGhostLifetimeSeconds, paintColor, debugDrawYOffset);
            }

            if (debugLogs)
                Debug.Log($"[DamageZoneAbility] SUCCESS. Spawned zone with {tris.Count / 3} tris.", root);
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