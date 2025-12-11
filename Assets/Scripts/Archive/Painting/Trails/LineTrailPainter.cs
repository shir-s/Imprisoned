using JellyGame.GamePlay.Map.Surfaces;
using JellyGame.GamePlay.Painting;
using JellyGame.GamePlay.Painting.Trails;
using JellyGame.GamePlay.Painting.Trails.Visibility;
using UnityEngine;
namespace Painting.Trails
{
        

    /// <summary>
    /// IMovementPainter that draws a line using a LineRenderer on top of the hit surface.
    /// - Raycasts from the cube to the surface (plane, board, etc).
    /// - Builds one or more LineRenderer "segments" during movement.
    /// - Each segment is parented under the painted surface so it moves with it.
    /// - Designed to be artist-friendly: many exposed parameters for look & feel.
    /// </summary>
    [DisallowMultipleComponent]
    public class LineTrailPainter : MonoBehaviour, IMovementPainter
    {
        // --------------------------
        // Raycast settings
        // --------------------------
        [Header("Raycast")]
        [Tooltip("Layers that can receive line strokes.")]
        [SerializeField] private LayerMask surfaceMask = ~0;

        [Tooltip("Max ray distance from the cube to find a surface.")]
        [SerializeField] private float rayDistance = 2f;

        [Tooltip("If true, cast ray straight down (world -Y). If false, use -transform.up.")]
        [SerializeField] private bool useWorldDown = true;

        // --------------------------
        // Line visual settings
        // --------------------------
        public enum ColorSource
        {
            FromLineMaterial,  // use material as-is
            FromCubeRenderer,  // take color from this object's Renderer.material.color
            OverrideColor      // use overrideColor
        }

        [Header("Line Visual")]
        [Tooltip("Base material for the line. Should be a transparent / additive material.")]
        [SerializeField] private Material baseLineMaterial;

        [Tooltip("Base width of the line in world units.")]
        [SerializeField] private float lineWidth = 0.02f;

        [Tooltip("End width scale relative to start width (1 = same width).")]
        [SerializeField] private float endWidthScale = 1f;

        [Tooltip("How many extra vertices to use along corners (smoother corners = higher cost).")]
        [SerializeField] private int cornerVertices = 4;

        [Tooltip("How many extra vertices to use at caps (rounded ends).")]
        [SerializeField] private int capVertices = 4;

        [Header("Line Color")]
        [Tooltip("Where to take the line color from.")]
        [SerializeField] private ColorSource colorSource = ColorSource.FromCubeRenderer;

        [Tooltip("Color to use if ColorSource is OverrideColor.")]
        [SerializeField] private Color overrideColor = Color.cyan;

        [Tooltip("Use a Gradient instead of a single color (for start->end color).")]
        [SerializeField] private bool useColorGradient = false;

        [Tooltip("Gradient along the line length. Used only if useColorGradient = true.")]
        [SerializeField] private Gradient colorGradient;

        // --------------------------
        // Sampling / segment settings
        // --------------------------
        [Header("Sampling & Segments")]
        [Tooltip("Minimum distance between successive line points in world units.")]
        [SerializeField] private float minPointSpacing = 0.0015f;

        [Tooltip("Maximum number of points per LineRenderer segment. When reached, a new segment starts.")]
        [SerializeField] private int maxPointsPerSegment = 256;

        [Tooltip("Small offset along the surface normal to avoid z-fighting.")]
        [SerializeField] private float liftFromSurface = 0.0002f;

        // --------------------------
        // Parenting settings
        // --------------------------
        [Header("Parenting")]
        [Tooltip("If true, try to parent the line under a SimplePaintSurface on the hit object.")]
        [SerializeField] private bool parentToSimplePaintSurface = true;

        [Tooltip("If no SimplePaintSurface is found, parent under the collider's transform.")]
        [SerializeField] private bool fallbackToColliderTransform = true;

        // --------------------------
        // Debug
        // --------------------------
        [Header("Debug")]
        [SerializeField] private bool debugRays = false;

        [SerializeField] private bool debugLogSegments = false;

        // --------------------------
        // Runtime state
        // --------------------------
        private LineRenderer _currentLine;
        private Transform    _currentParent;   // the surface root we parent to
        private int          _currentPointCount;
        private Vector3      _lastPointWS;
        private bool         _hasLastPoint;

        private void OnDisable()
        {
            // We don't destroy lines here; we just stop updating current segment.
            _currentLine      = null;
            _currentParent    = null;
            _hasLastPoint     = false;
            _currentPointCount = 0;
        }

        // ========== IMovementPainter API ==========

        public void OnMovementStart(Vector3 worldPos)
        {
            _hasLastPoint      = false;
            _currentPointCount = 0;

            if (!TryRaycastSurface(worldPos, out var hit))
            {
                EndCurrentSegment();
                return;
            }

            StartNewSegment(hit, hit.point);
        }

        public void OnMoveStep(Vector3 from, Vector3 to, float stepMeters, float deltaTime)
        {
            if (!TryRaycastSurface(to, out var hit))
            {
                // Lost the surface -> finish this segment
                EndCurrentSegment();
                _hasLastPoint = false;
                return;
            }

            Transform suggestedParent = GetParentFromHit(hit);

            // If we have no active line yet, start one now
            if (_currentLine == null)
            {
                StartNewSegment(hit, hit.point);
            }
            else if (suggestedParent != _currentParent && suggestedParent != null)
            {
                // Surface changed -> close old segment and start a new one on new surface
                EndCurrentSegment();
                StartNewSegment(hit, hit.point);
            }

            // At this point we should have a valid line & parent
            if (_currentLine == null)
                return;

            Vector3 p = hit.point + hit.normal * liftFromSurface;

            if (!_hasLastPoint)
            {
                AddPointToCurrentLine(p);
                _lastPointWS  = p;
                _hasLastPoint = true;
                return;
            }

            float sq = (p - _lastPointWS).sqrMagnitude;
            float minSq = minPointSpacing * minPointSpacing;

            if (sq < minSq)
                return; // too close, skip

            AddPointToCurrentLine(p);
            _lastPointWS = p;
            _hasLastPoint = true;
        }

        public void OnMovementEnd(Vector3 worldPos)
        {
            EndCurrentSegment();
            _hasLastPoint      = false;
            _currentPointCount = 0;
        }

        // ========== Segment management ==========

        private void StartNewSegment(RaycastHit hit, Vector3 startPointWS)
        {
            EndCurrentSegment();

            Transform parent = GetParentFromHit(hit);
            _currentParent = parent;

            // Create a new GameObject at root (no parent yet)
            var go = new GameObject("LineStroke_Segment");
            _currentLine = go.AddComponent<LineRenderer>();

            // Configure LineRenderer
            _currentLine.useWorldSpace     = true;
            _currentLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _currentLine.receiveShadows    = false;
            _currentLine.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            _currentLine.positionCount = 0;
            _currentPointCount         = 0;

            _currentLine.numCornerVertices = Mathf.Max(0, cornerVertices);
            _currentLine.numCapVertices    = Mathf.Max(0, capVertices);

            _currentLine.widthMultiplier   = 1f; // we control absolute width via start/end
            _currentLine.startWidth        = Mathf.Max(0f, lineWidth);
            _currentLine.endWidth          = Mathf.Max(0f, lineWidth * endWidthScale);

            // Instance material so each stroke can have its own color/gradient
            if (baseLineMaterial != null)
            {
                var matInstance = new Material(baseLineMaterial);
                ApplyColorSettingsToMaterial(matInstance);
                _currentLine.material = matInstance;
            }

            // Also support gradient directly on the LineRenderer if desired
            ApplyGradientOrColorToLineRenderer(_currentLine);

            // First point
            Vector3 p = startPointWS + hit.normal * liftFromSurface;
            AddPointToCurrentLine(p);
            _lastPointWS  = p;
            _hasLastPoint = true;

            if (debugLogSegments)
                Debug.Log($"[LineTrailPainter] Started new segment on '{_currentParent?.name ?? "null"}'");
        }

        private void EndCurrentSegment()
        {
            if (_currentLine != null)
            {
                if (_currentParent != null)
                {
                    // Keep world positions, just parent under the painted surface
                    _currentLine.transform.SetParent(_currentParent, worldPositionStays: true);
                }

                if (debugLogSegments)
                {
                    Debug.Log($"[LineTrailPainter] End segment with {_currentPointCount} points on '{_currentParent?.name ?? "null"}'");
                }
            }

            _currentLine      = null;
            _currentParent    = null;
            _currentPointCount = 0;
        }

        private void AddPointToCurrentLine(Vector3 p)
        {
            if (_currentLine == null)
                return;

            // If this segment is too long, start a new one that continues from the last point
            if (_currentPointCount >= Mathf.Max(2, maxPointsPerSegment))
            {
                Vector3 last = _currentLine.GetPosition(_currentPointCount - 1);
                // Close old:
                EndCurrentSegment();
                // Open new starting at last:
                var hitDummy = new RaycastHit();
                hitDummy.point  = last;
                hitDummy.normal = Vector3.up; // fallback; we only care about position here
                StartNewSegment(hitDummy, last);
            }

            _currentLine.positionCount = _currentPointCount + 1;
            _currentLine.SetPosition(_currentPointCount, p);
            _currentPointCount++;
        }

        // ========== Color / Material helpers ==========

        private void ApplyColorSettingsToMaterial(Material mat)
        {
            if (mat == null)
                return;

            Color finalColor = overrideColor;

            switch (colorSource)
            {
                case ColorSource.FromLineMaterial:
                    // do nothing; material color as is
                    return;
                case ColorSource.FromCubeRenderer:
                    var r = GetComponent<Renderer>();
                    if (r != null)
                        finalColor = r.material.color;
                    break;
                case ColorSource.OverrideColor:
                    // overrideColor already used
                    break;
            }

            if (mat.HasProperty("_Color"))
                mat.color = finalColor;
        }

        private void ApplyGradientOrColorToLineRenderer(LineRenderer line)
        {
            if (line == null)
                return;

            if (useColorGradient && colorGradient != null)
            {
                line.colorGradient = colorGradient;
            }
            else
            {
                // Single color based on colorSource
                Color finalColor = overrideColor;
                switch (colorSource)
                {
                    case ColorSource.FromLineMaterial:
                        // take from material if possible
                        if (line.material != null && line.material.HasProperty("_Color"))
                            finalColor = line.material.color;
                        break;
                    case ColorSource.FromCubeRenderer:
                        var r = GetComponent<Renderer>();
                        if (r != null)
                            finalColor = r.material.color;
                        break;
                    case ColorSource.OverrideColor:
                        break;
                }

                line.startColor = finalColor;
                line.endColor   = finalColor;
            }
        }

        // ========== Parenting helpers ==========

        private Transform GetParentFromHit(RaycastHit hit)
        {
            Transform parent = null;

            if (parentToSimplePaintSurface)
            {
                var surf = hit.collider.GetComponentInParent<SimplePaintSurface>();
                if (surf != null)
                    parent = surf.transform;
            }

            if (parent == null && fallbackToColliderTransform && hit.collider != null)
            {
                parent = hit.collider.transform;
            }

            return parent;
        }

        // ========== Raycast helper ==========

        private bool TryRaycastSurface(Vector3 fromPos, out RaycastHit hit)
        {
            Vector3 dir   = useWorldDown ? Vector3.down : -transform.up;
            Vector3 start = fromPos - dir * 0.01f;

            if (debugRays)
                Debug.DrawRay(start, dir * rayDistance, Color.yellow, 0.1f);

            return Physics.Raycast(start, dir, out hit, rayDistance, surfaceMask, QueryTriggerInteraction.Collide);
        }
    }

}