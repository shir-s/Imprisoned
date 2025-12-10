using System.Collections;
using System.Collections.Generic;
using JellyGame.GamePlay.Abilities;
using JellyGame.GamePlay.Abilities.Stickiness;
using JellyGame.GamePlay.Map.Surfaces;
using JellyGame.GamePlay.Painting.Trails.Visibility;
using UnityEngine;

namespace JellyGame.GamePlay.Painting.Shapes
{
    [DisallowMultipleComponent]
    public class AreaFillShapeDetector : MonoBehaviour, IStrokeShapeDetector
    {
        [Header("References")]
        [SerializeField] private List<SimplePaintSurface> paintSurfaces = new();
        [SerializeField] private RenderTextureTrailPainter painter;

        [Header("Fill sampling (LOCAL space)")]
        [Tooltip("Grid step size in local units. 0.08 is usually good.")]
        [SerializeField] private float localStepSize = 0.08f;

        [Tooltip("How far to check above/below the surface for the paint raycast.")]
        [SerializeField] private float raycastTolerance = 1.0f;

        [Header("Performance")]
        [Tooltip("Max successful paints per frame. Higher = faster fill but potential lag.")]
        [SerializeField] private int maxPaintsPerFrame = 500;

        [Header("Stickiness Ability")]
        [SerializeField] private GameObject slowZonePrefab;
        [Range(0.01f, 1f)]
        [SerializeField] private float slowMultiplier = 0.15f;

        [Header("Visual Feedback")]
        [SerializeField] private bool showVisualFeedback = false;
        [SerializeField] private Renderer visualFeedbackRenderer;

        [Header("Debug")]
        [SerializeField] private bool debugPolygon = false;
        [SerializeField] private bool debugFillPoints = false;

        private Coroutine _currentFillCoroutine;
        private bool _fillModeActive = true;

        private void Start()
        {
            if (showVisualFeedback && visualFeedbackRenderer == null)
            {
                visualFeedbackRenderer = GetComponentInChildren<Renderer>();
            }
            UpdateVisualFeedback();
        }

        public bool TryHandleShape(StrokeLoopSegment seg)
        {
            if (paintSurfaces == null || paintSurfaces.Count == 0 || painter == null || seg.history == null)
                return false;

            // We assume the fill happens on the first surface in the list (usually the main floor)
            // This allows us to lock coordinates to this object's rotation.
            SimplePaintSurface referenceSurface = paintSurfaces[0];
            if (referenceSurface == null) return false;

            // Stop any existing fill to prevent overlap
            if (_currentFillCoroutine != null)
            {
                StopCoroutine(_currentFillCoroutine);
            }

            // 1. Convert Polygon to LOCAL Space
            // This fixes the "Tilt Drift" issue.
            List<Vector2> localPolyXZ = new List<Vector2>();
            
            // We also calculate local bounds here
            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minZ = float.MaxValue;
            float maxZ = float.MinValue;

            for (int i = seg.startIndex; i <= seg.endIndexInclusive; i++)
            {
                Vector3 worldPos = seg.history[i].WorldPos;
                
                // Convert World Point -> Surface Local Point
                Vector3 localPos = referenceSurface.transform.InverseTransformPoint(worldPos);

                localPolyXZ.Add(new Vector2(localPos.x, localPos.z));

                if (localPos.x < minX) minX = localPos.x;
                if (localPos.x > maxX) maxX = localPos.x;
                if (localPos.z < minZ) minZ = localPos.z;
                if (localPos.z > maxZ) maxZ = localPos.z;
            }

            if (localPolyXZ.Count < 3) return false;

            if (debugPolygon) Debug.Log($"[AreaFill] Starting Local Space Fill. Bounds: {minX:F2} to {maxX:F2}");

            // 2. Start the Local Space Fill
            _currentFillCoroutine = StartCoroutine(
                FillAsyncLocal(referenceSurface, minX, maxX, minZ, maxZ, localPolyXZ)
            );

            return true;
        }

        private IEnumerator FillAsyncLocal(SimplePaintSurface surface, float minX, float maxX, float minZ, float maxZ, List<Vector2> localPolyXZ)
        {
            int paintsThisFrame = 0;
            int totalFilled = 0;

            // Iterate through the bounding box in LOCAL coordinates
            for (float x = minX; x <= maxX; x += localStepSize)
            {
                for (float z = minZ; z <= maxZ; z += localStepSize)
                {
                    Vector2 localPoint = new Vector2(x, z);

                    // Math check (Cheap) - Is point inside the shape?
                    if (!IsPointInPolygon2D(localPoint, localPolyXZ))
                        continue;

                    // It is inside! Now calculate current World Position
                    // Since we transform it NOW, it accounts for the map's current tilt rotation.
                    Vector3 currentWorldPos = surface.transform.TransformPoint(new Vector3(x, 0, z));

                    // Perform the Paint (Expensive)
                    if (TryPaintAtWorldPoint(currentWorldPos, out bool painted))
                    {
                        if (painted) totalFilled++;
                    }

                    // Count "Expensive" operations for performance budget
                    paintsThisFrame++;

                    if (paintsThisFrame >= maxPaintsPerFrame)
                    {
                        paintsThisFrame = 0;
                        yield return null; // Wait for next frame
                    }
                }
            }

            if (debugPolygon) Debug.Log($"[AreaFill] Finished. Total points: {totalFilled}");

            // Spawn the SlowZone, attached to the surface so it moves with it
            SpawnSlowZoneLocal(surface, localPolyXZ, minX, maxX, minZ, maxZ);

            _currentFillCoroutine = null;
        }

        private bool TryPaintAtWorldPoint(Vector3 worldPoint, out bool painted)
        {
            painted = false;
            
            // Raycast slightly up and down relative to the point to find the UV
            // We use world up/down because the painter usually expects world raycasts, 
            // OR use the surface normal if possible. Let's stick to simple vertical check 
            // but ensure we start high enough relative to the surface.
            
            // We assume the point is roughly on the surface, so we cast from "above" to "below" relative to the surface
            // BUT, since we generated this point via TransformPoint(0), it IS exactly on the surface plane.
            // We just need to nudge the ray origin out along the normal.
            
            // NOTE: Using Vector3.up works if the map isn't 90 degrees vertical. 
            // For full robustness on rotating maps, calculate the ray direction:
            Vector3 rayOrigin = worldPoint + (Vector3.up * raycastTolerance);
            Vector3 rayDir = Vector3.down;

            if (Physics.Raycast(rayOrigin, rayDir, out RaycastHit hit, raycastTolerance * 2f))
            {
                SimplePaintSurface surface = hit.collider.GetComponentInParent<SimplePaintSurface>();

                // Ensure we hit a valid paint surface
                if (surface != null && paintSurfaces.Contains(surface))
                {
                    if (surface.TryWorldToPaintUV(hit.point, out Vector2 uv))
                    {
                        painter.PaintAtUV(surface, uv);
                        painted = true;
                        
                        if (debugFillPoints) Debug.DrawRay(hit.point, Vector3.up * 0.1f, Color.green, 0.5f);
                        return true;
                    }
                }
            }
            return false;
        }

        private bool IsPointInPolygon2D(Vector2 p, List<Vector2> poly)
        {
            bool inside = false;
            int count = poly.Count;
            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                if (((poly[i].y > p.y) != (poly[j].y > p.y)) &&
                    (p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / ((poly[j].y - poly[i].y) + 1e-6f) + poly[i].x))
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        private void SpawnSlowZoneLocal(SimplePaintSurface surface, List<Vector2> localPoly, float minX, float maxX, float minZ, float maxZ)
        {
            if (PlayerAbilityManager.Instance == null || !PlayerAbilityManager.Instance.HasStickinessAbility) return;
            if (slowZonePrefab == null) return;

            float centerX = (minX + maxX) * 0.5f;
            float centerZ = (minZ + maxZ) * 0.5f;
            float sizeX = maxX - minX;
            float sizeZ = maxZ - minZ;

            // Instantiate in World Space first
            Vector3 worldCenter = surface.transform.TransformPoint(new Vector3(centerX, 0, centerZ));
            GameObject zoneObj = Instantiate(slowZonePrefab, worldCenter, Quaternion.identity);
            
            // CRITICAL: Parent to the surface so it tilts with the map
            zoneObj.transform.SetParent(surface.transform, true);
            
            // Reset rotation to match the surface
            zoneObj.transform.localRotation = Quaternion.identity;

            // Setup Collider
            BoxCollider box = zoneObj.GetComponent<BoxCollider>();
            if (box == null) box = zoneObj.AddComponent<BoxCollider>();
            
            box.size = new Vector3(sizeX, 1.0f, sizeZ); // Thick collider so agents don't slip over
            box.center = Vector3.zero;
            box.isTrigger = true;

            // Clean other colliders
            foreach (var c in zoneObj.GetComponents<Collider>())
                if (c != box) Destroy(c);

            // Configure Logic
            SlowZone logic = zoneObj.GetComponent<SlowZone>();
            if (logic != null) logic.SetSlowMultiplier(slowMultiplier);
        }
        
        // Helper visual updates
        public void ToggleFillMode() { _fillModeActive = !_fillModeActive; UpdateVisualFeedback(); }
        public bool IsFillModeActive => _fillModeActive;

        private void UpdateVisualFeedback()
        {
            if (!showVisualFeedback || visualFeedbackRenderer == null) return;
            bool active = _fillModeActive && PlayerAbilityManager.Instance != null && PlayerAbilityManager.Instance.HasStickinessAbility;
            Material mat = visualFeedbackRenderer.material;
            if (active) { mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", new Color(0.2f, 0.4f, 1f, 1f)); }
            else { mat.DisableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", Color.black); }
        }
    }
}