// FILEPATH: Assets/Scripts/Abilities/Zones/StickyZoneAbility.cs
using UnityEngine;

namespace JellyGame.GamePlay.Abilities.Zones
{
    [CreateAssetMenu(menuName = "JellyGame/Abilities/Sticky Zone Ability")]
    public class StickyZoneAbility : ScriptableObject, IPlayerAbility
    {
        [Header("Visuals")]
        [SerializeField] private Color paintColor = new Color(0.2f, 0.8f, 1f, 1f); // Cyan/blue color for sticky zones

        [Header("Zone")]
        [SerializeField] private float zoneLifetimeSeconds = 4f;
        [SerializeField] private float zoneSpawnDelaySeconds = 2f; // Delay before zone becomes active (wait for fill animation)
        [SerializeField] private float zoneThickness = 1.2f;
        [SerializeField] private float effectTickInterval = 0.2f;
        [SerializeField] private LayerMask stickyTargetLayers = ~0;

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
                Debug.LogWarning("[StickyZoneAbility] Spawn aborted: Invalid context or empty polygon.");
                return;
            }

            GameObject root = new GameObject("AbilityZone_Sticky");
            Transform rt = root.transform;
            rt.SetParent(ctx.Surface.transform, false);

            Vector3 localCenter = ctx.LocalBounds.center;
            rt.localPosition = new Vector3(localCenter.x, 0f, localCenter.z);
            rt.localRotation = Quaternion.identity;
            rt.localScale = Vector3.one;

            AbilityZone zone = root.AddComponent<AbilityZone>();
            // Add delay to lifetime so zone stays active for full duration after activation
            zone.Configure(zoneLifetimeSeconds + zoneSpawnDelaySeconds, effectTickInterval, stickyTargetLayers, debugLogs);

            // Add delay activator to disable colliders until fill animation completes
            var delayActivator = root.AddComponent<ZoneDelayActivator>();
            delayActivator.Configure(zoneSpawnDelaySeconds, zone, debugLogs);

            StickyZoneEffect sticky = root.AddComponent<StickyZoneEffect>();
            // StickyZoneEffect doesn't need configuration, but we could add it if needed

            if (debugDrawZoneGizmos)
            {
                var viz = root.AddComponent<AbilityZoneDebugVisualizer>();
                viz.Configure(paintColor, debugDrawYOffset, true);
            }

            // 2. Prepare Polygon for Triangulation
            var shifted = ShiftPolygon(ctx.LocalPolygonXZ, -new Vector2(localCenter.x, localCenter.z));

            // 3. Attempt Triangulation
            System.Collections.Generic.List<Vector2> fixedPoly;
            var tris = ZoneMeshBuilder.TriangulatePolygonXZ(shifted, out fixedPoly);

            // 4. CHECK FOR FAILURE
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

                Vector2 a = fixedPoly[ia];
                Vector2 b = fixedPoly[ib];
                Vector2 c = fixedPoly[ic];

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
                Debug.Log($"[StickyZoneAbility] SUCCESS. Spawned sticky zone with {tris.Count / 3} tris.", root);
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

