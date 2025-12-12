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
        public enum FillMode
        {
            /// <summary>
            /// The Old Way. Works exactly like your original script.
            /// Calculates bounds in World Space. Checks ALL surfaces.
            /// Good for your Old Scene.
            /// </summary>
            WorldSpaceLegacy,

            /// <summary>
            /// The New Way. Calculates bounds in Local Space.
            /// Fixes the scale/tilt issues.
            /// Good for your New Scene.
            /// </summary>
            LocalSpaceSmart
        }

        [Header("Configuration")]
        [Tooltip("Select 'WorldSpaceLegacy' for the old scene, 'LocalSpaceSmart' for the new scene.")]
        [SerializeField] private FillMode fillMode = FillMode.WorldSpaceLegacy;

        [Header("References")]
        [SerializeField] private List<SimplePaintSurface> paintSurfaces = new();
        [SerializeField] private RenderTextureTrailPainter painter;

        [Header("Fill sampling")]
        [Tooltip("Step size in World Units (used by both modes).")]
        [SerializeField] private float worldStepSize = 0.08f;

        [Tooltip("How far to check above/below the surface for the paint raycast.")]
        [SerializeField] private float raycastTolerance = 1.0f;
        
        [Header("Performance")]
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

            if (_currentFillCoroutine != null) StopCoroutine(_currentFillCoroutine);

            if (fillMode == FillMode.WorldSpaceLegacy)
            {
                // Exact logic from your original script
                return HandleShapeLegacy(seg);
            }
            else
            {
                // New logic for the new scene
                // Smart mode needs a reference surface for local space calc
                SimplePaintSurface refSurface = paintSurfaces[0];
                if (refSurface == null) return false;
                return HandleShapeSmart(seg, refSurface);
            }
        }

        // ========================================================================
        // 1. LEGACY MODE (The Old Way - For Old Scene)
        // ========================================================================
        private bool HandleShapeLegacy(StrokeLoopSegment seg)
        {
            // 1. Build polygon in WORLD SPACE
            List<Vector2> polygonXZ = new List<Vector2>();
            List<Vector3> worldPoly3D = new List<Vector3>(); // For debug
            
            float avgHeight = 0f;
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;

            for (int i = seg.startIndex; i <= seg.endIndexInclusive; i++)
            {
                Vector3 wPos = seg.history[i].WorldPos;
                avgHeight += wPos.y;
                
                polygonXZ.Add(new Vector2(wPos.x, wPos.z));
                worldPoly3D.Add(wPos);

                if (wPos.x < minX) minX = wPos.x;
                if (wPos.x > maxX) maxX = wPos.x;
                if (wPos.z < minZ) minZ = wPos.z;
                if (wPos.z > maxZ) maxZ = wPos.z;
            }

            if (polygonXZ.Count < 3) return false;
            avgHeight /= polygonXZ.Count;

            if (debugPolygon) DebugDrawWorldPolygon(worldPoly3D);

            // 2. Start Async Fill
            _currentFillCoroutine = StartCoroutine(
                FillAsyncLegacy(minX, maxX, minZ, maxZ, avgHeight, polygonXZ)
            );

            return true;
        }

        private IEnumerator FillAsyncLegacy(float minX, float maxX, float minZ, float maxZ, float avgHeight, List<Vector2> polygonXZ)
        {
            int paintsThisFrame = 0;
            int totalFilled = 0;

            for (float x = minX; x <= maxX; x += worldStepSize)
            {
                for (float z = minZ; z <= maxZ; z += worldStepSize)
                {
                    Vector2 pointXZ = new Vector2(x, z);

                    if (!IsPointInPolygon2D(pointXZ, polygonXZ))
                        continue;

                    Vector3 worldPoint = new Vector3(x, avgHeight, z);

                    // Original Raycast Logic
                    if (TryPaintLegacy(worldPoint))
                    {
                        totalFilled++;
                    }

                    paintsThisFrame++;
                    if (paintsThisFrame >= maxPaintsPerFrame)
                    {
                        paintsThisFrame = 0;
                        yield return null;
                    }
                }
            }

            if (debugPolygon) Debug.Log($"[AreaFill-Legacy] Filled {totalFilled} points.");
            
            // Legacy Spawn (World Space, No Parenting)
            SpawnSlowZoneLegacy(polygonXZ, avgHeight);
            
            _currentFillCoroutine = null;
        }

        private bool TryPaintLegacy(Vector3 worldPoint)
        {
            Vector3 rayStart = worldPoint + Vector3.up * raycastTolerance;
            Vector3 rayDir = Vector3.down;
            float rayDist = raycastTolerance * 2f;

            // This allows hitting ANY surface in the list (crucial for old scene)
            if (Physics.Raycast(rayStart, rayDir, out RaycastHit hit, rayDist))
            {
                SimplePaintSurface surface = hit.collider.GetComponentInParent<SimplePaintSurface>();

                if (surface != null && paintSurfaces.Contains(surface))
                {
                    if (surface.TryWorldToPaintUV(hit.point, out Vector2 uv))
                    {
                        painter.PaintAtUV(surface, uv);
                        if (debugFillPoints) Debug.DrawRay(hit.point, Vector3.up * 0.1f, Color.red, 0.5f);
                        return true;
                    }
                }
            }
            return false;
        }

        private void SpawnSlowZoneLegacy(List<Vector2> polygonXZ, float avgHeight)
        {
            if (!CanSpawnZone()) return;
            if (polygonXZ.Count < 3) return;

            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;

            foreach (var p in polygonXZ)
            {
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.y < minZ) minZ = p.y;
                if (p.y > maxZ) maxZ = p.y;
            }

            float cx = (minX + maxX) * 0.5f;
            float cz = (minZ + maxZ) * 0.5f;
            Vector3 center = new Vector3(cx, avgHeight, cz);
            
            // Spawn without parenting (Legacy behavior)
            CreateZoneObject(center, maxX - minX, maxZ - minZ, null);
        }

        // ========================================================================
        // 2. SMART MODE (The New Way - For New Scene)
        // ========================================================================
        private bool HandleShapeSmart(StrokeLoopSegment seg, SimplePaintSurface surface)
        {
            List<Vector2> localPolyXZ = new List<Vector2>();
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;

            for (int i = seg.startIndex; i <= seg.endIndexInclusive; i++)
            {
                Vector3 wPos = seg.history[i].WorldPos;
                Vector3 lPos = surface.transform.InverseTransformPoint(wPos);
                localPolyXZ.Add(new Vector2(lPos.x, lPos.z));

                if (lPos.x < minX) minX = lPos.x;
                if (lPos.x > maxX) maxX = lPos.x;
                if (lPos.z < minZ) minZ = lPos.z;
                if (lPos.z > maxZ) maxZ = lPos.z;
            }

            if (localPolyXZ.Count < 3) return false;

            // Smart Step Size (handles scaling)
            float scaleFactor = surface.transform.lossyScale.x;
            float localStep = worldStepSize / Mathf.Max(0.001f, scaleFactor);

            if (debugPolygon) Debug.Log($"[AreaFill-Smart] Scale: {scaleFactor:F1}, Step: {localStep:F4}");

            _currentFillCoroutine = StartCoroutine(
                FillAsyncSmart(surface, minX, maxX, minZ, maxZ, localPolyXZ, localStep)
            );
            return true;
        }

        private IEnumerator FillAsyncSmart(SimplePaintSurface surface, float minX, float maxX, float minZ, float maxZ, List<Vector2> poly, float step)
        {
            int paintsThisFrame = 0;
            int totalFilled = 0;

            for (float x = minX; x <= maxX; x += step)
            {
                for (float z = minZ; z <= maxZ; z += step)
                {
                    if (!IsPointInPolygon2D(new Vector2(x, z), poly)) continue;

                    // Convert local grid point to current world position (handles tilt)
                    Vector3 currentWorldPos = surface.transform.TransformPoint(new Vector3(x, 0, z));

                    if (TryPaintSmart(currentWorldPos, surface))
                    {
                        totalFilled++;
                    }

                    paintsThisFrame++;
                    if (paintsThisFrame >= maxPaintsPerFrame)
                    {
                        paintsThisFrame = 0;
                        yield return null;
                    }
                }
            }

            if (debugPolygon) Debug.Log($"[AreaFill-Smart] Filled {totalFilled} points.");
            
            // Smart Spawn (Parents to surface)
            SpawnSlowZoneSmart(surface, minX, maxX, minZ, maxZ);
            
            _currentFillCoroutine = null;
        }

        private bool TryPaintSmart(Vector3 worldPos, SimplePaintSurface targetSurface)
        {
            // Use Surface Up instead of World Up
            Vector3 up = targetSurface.transform.up;
            Vector3 start = worldPos + up * raycastTolerance;
            
            if (Physics.Raycast(start, -up, out RaycastHit hit, raycastTolerance * 2f))
            {
                SimplePaintSurface hitSurf = hit.collider.GetComponentInParent<SimplePaintSurface>();
                if (hitSurf == targetSurface && targetSurface.TryWorldToPaintUV(hit.point, out Vector2 uv))
                {
                    painter.PaintAtUV(targetSurface, uv);
                    if (debugFillPoints) Debug.DrawRay(hit.point, up * 0.1f, Color.green, 0.5f);
                    return true;
                }
            }
            return false;
        }

        private void SpawnSlowZoneSmart(SimplePaintSurface surface, float minX, float maxX, float minZ, float maxZ)
        {
            if (!CanSpawnZone()) return;

            float cx = (minX + maxX) * 0.5f;
            float cz = (minZ + maxZ) * 0.5f;
            Vector3 worldCenter = surface.transform.TransformPoint(new Vector3(cx, 0, cz));
            
            float sx = maxX - minX;
            float sz = maxZ - minZ;
            
            // Spawn and Parent to surface
            CreateZoneObject(worldCenter, sx, sz, surface.transform);
        }

        // ========================================================================
        // SHARED HELPERS
        // ========================================================================

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

        private bool CanSpawnZone()
        {
            return PlayerAbilityManager.Instance != null && 
                   PlayerAbilityManager.Instance.HasStickinessAbility && 
                   slowZonePrefab != null;
        }

        private void CreateZoneObject(Vector3 pos, float sizeX, float sizeZ, Transform parent)
        {
            GameObject zoneObj = Instantiate(slowZonePrefab, pos, Quaternion.identity);
            
            if (parent != null)
            {
                zoneObj.transform.SetParent(parent, true);
                zoneObj.transform.localRotation = Quaternion.identity;
            }

            BoxCollider box = zoneObj.GetComponent<BoxCollider>();
            if (box == null) box = zoneObj.AddComponent<BoxCollider>();
            box.size = new Vector3(sizeX, 1.0f, sizeZ);
            box.center = Vector3.zero;
            box.isTrigger = true;

            foreach (var c in zoneObj.GetComponents<Collider>())
                if (c != box) Destroy(c);

            SlowZone logic = zoneObj.GetComponent<SlowZone>();
            if (logic != null) logic.SetSlowMultiplier(slowMultiplier);
        }

        private void DebugDrawWorldPolygon(List<Vector3> poly)
        {
            for (int i = 0; i < poly.Count; i++)
            {
                Vector3 a = poly[i];
                Vector3 b = poly[(i + 1) % poly.Count];
                Debug.DrawLine(a + Vector3.up * 0.15f, b + Vector3.up * 0.15f, Color.yellow, 3f);
            }
        }

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