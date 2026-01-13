// FILEPATH: Assets/Scripts/Painting/Shapes/StrokeTrailAnalyzer.cs

using JellyGame.GamePlay.Painting.Trails.Collision;
using JellyGame.GamePlay.Painting.Trails.Visibility;
using JellyGame.GamePlay.Map.Surfaces;
using UnityEngine;

namespace JellyGame.GamePlay.Painting.Shapes
{
    [DisallowMultipleComponent]
    public class StrokeTrailAnalyzer : MonoBehaviour, IMovementPainter
    {
        [Header("Loop Detection")]
        [SerializeField] private float closureMaxDistance = 0.25f;
        [SerializeField] private int minLoopIndexSeparation = 6;

        [Header("Edge-aware Closure")]
        [SerializeField] private bool useEdgePairsForClosure = true;

        [Header("On Loop Closed")]
        [Tooltip("If true, when a loop is accepted we delete ALL points from the start of history up to the closure point (end index).\n" +
                 "This recreates the old 'consume history on closure' behavior.")]
        [SerializeField] private bool consumeHistoryUpToClosurePoint = true;

        [Tooltip("If true, when consuming history, also age the corresponding trail section (make it gray).")]
        [SerializeField] private bool ageConsumedTrailSection = true;

        [Header("Path Analysis")]
        [SerializeField] private float minCornerSeparationMeters = 2f;

        [Header("References")]
        [Tooltip("The painter used to age the trail. Auto-found if null.")]
        [SerializeField] private RenderTextureTrailPainter trailPainter;

        [Tooltip("The paint surface to age. Auto-found if null.")]
        [SerializeField] private SimplePaintSurface paintSurface;

        [Header("Debug")]
        [SerializeField] private bool debugLoop = false;
        [SerializeField] private bool debugLoopVerbose = false;

        private StrokeTrailRecorder _recorder;
        private StrokeHistory _history;
        private IStrokeShapeDetector[] _detectors;

        private bool _closureHandled;

        private void Awake()
        {
            _recorder = GetComponent<StrokeTrailRecorder>();
            if (!_recorder)
            {
                Debug.LogError("[StrokeTrailAnalyzer] Missing StrokeTrailRecorder!", this);
                enabled = false;
                return;
            }

            _history = _recorder.History;
            _detectors = GetComponents<IStrokeShapeDetector>();

            // Auto-find painter if not assigned
            if (trailPainter == null)
                trailPainter = GetComponent<RenderTextureTrailPainter>();

            // Auto-find surface if not assigned (look for first one in scene or on parent)
            if (paintSurface == null)
                paintSurface = FindObjectOfType<SimplePaintSurface>();
        }

        public void OnMovementStart(Vector3 worldPos) => _closureHandled = false;

        public void OnMoveStep(Vector3 from, Vector3 to, float stepMeters, float deltaTime)
        {
            TryDetectLoopAndNotifyDetectors();
        }

        public void OnMovementEnd(Vector3 worldPos) => _closureHandled = false;

        private void TryDetectLoopAndNotifyDetectors()
        {
            if (_history == null)
                return;

            int count = _history.Count;
            if (count < 4)
                return;

            int last = count - 1;
            float lenToLast = _history.GetLengthAt(last);

            int bestStart = -1;
            float bestChord = float.PositiveInfinity;
            float bestLoopLen = 0f;

            for (int i = 0; i < last - 1; i++)
            {
                int indexDiff = last - i;
                if (indexDiff < minLoopIndexSeparation)
                    continue;

                float chord = useEdgePairsForClosure 
                    ? ComputeEdgeAwareChord(i, last) 
                    : Vector3.Distance(_history[i].WorldPos, _history[last].WorldPos);

                if (chord < bestChord)
                {
                    float lenToStart = (i > 0) ? _history.GetLengthAt(i) : 0f;
                    float loopLen = lenToLast - lenToStart;

                    bestChord = chord;
                    bestStart = i;
                    bestLoopLen = loopLen;
                }
            }

            if (bestStart < 0)
                return;

            if (debugLoopVerbose)
            {
                Debug.Log(
                    $"[StrokeTrailAnalyzer] Best candidate: start={bestStart}, last={last}, " +
                    $"indexDiff={last - bestStart}, len≈{bestLoopLen:F3}, chord={bestChord:F3}, closureMax={closureMaxDistance:F3}"
                );
            }

            if (bestChord > closureMaxDistance * 1.5f)
                _closureHandled = false;

            if (bestChord > closureMaxDistance)
                return;

            if (_closureHandled)
                return;

            _closureHandled = true;

            if (debugLoop)
                Debug.Log($"[StrokeTrailAnalyzer] Loop ACCEPTED [{bestStart}..{last}] (len≈{bestLoopLen:F3})");

            StrokePathLoop loopPath = StrokePathBuilder.BuildLoopCorners(_history, bestStart, last, minCornerSeparationMeters);

            StrokeLoopSegment seg = new StrokeLoopSegment
            {
                history = _history,
                startIndex = bestStart,
                endIndexInclusive = last,
                path = loopPath
            };

            bool anyDetectorHandled = false;

            if (_detectors != null && _detectors.Length > 0)
            {
                foreach (var det in _detectors)
                {
                    if (det == null) continue;

                    bool handled = det.TryHandleShape(seg);
                    if (handled)
                    {
                        anyDetectorHandled = true;
                        break;
                    }
                }
            }

            // Consume history and age the trail
            if (consumeHistoryUpToClosurePoint)
            {
                // FIRST: Age the trail section BEFORE removing the points (we need their positions!)
                if (ageConsumedTrailSection && trailPainter != null && paintSurface != null)
                {
                    AgeTrailSection(0, last);
                }

                // THEN: Remove the points from history
                _recorder.ConsumeUpToInclusive(last);

                if (debugLoop)
                    Debug.Log($"[StrokeTrailAnalyzer] Consumed and aged history up to index {last}.");
            }
        }

        /// <summary>
        /// Ages a section of the trail (makes it gray) by painting old timestamps
        /// at each point position in the time texture.
        /// </summary>
        private void AgeTrailSection(int startIndex, int endIndexInclusive)
        {
            if (trailPainter == null || paintSurface == null || _history == null)
                return;

            // Clamp indices
            int count = _history.Count;
            startIndex = Mathf.Clamp(startIndex, 0, count - 1);
            endIndexInclusive = Mathf.Clamp(endIndexInclusive, 0, count - 1);

            if (startIndex > endIndexInclusive)
                return;

            // Call the painter's method to age these points
            trailPainter.AgeTrailRange(paintSurface, _history, startIndex, endIndexInclusive);

            if (debugLoop)
                Debug.Log($"[StrokeTrailAnalyzer] Aged trail section [{startIndex}..{endIndexInclusive}] ({endIndexInclusive - startIndex + 1} points)");
        }

        private float ComputeEdgeAwareChord(int aIndex, int bIndex)
        {
            var edgePairs = _recorder != null ? _recorder.EdgePairs : null;

            Vector3 aCenter = _history[aIndex].WorldPos;
            Vector3 bCenter = _history[bIndex].WorldPos;

            float best = Vector3.Distance(aCenter, bCenter);

            if (!useEdgePairsForClosure || edgePairs == null)
                return best;

            if (aIndex < 0 || bIndex < 0 || aIndex >= edgePairs.Count || bIndex >= edgePairs.Count)
                return best;

            var a = edgePairs[aIndex];
            var b = edgePairs[bIndex];

            Vector3 aL = a.GetLeftWorld(aCenter);
            Vector3 aR = a.GetRightWorld(aCenter);

            Vector3 bL = b.GetLeftWorld(bCenter);
            Vector3 bR = b.GetRightWorld(bCenter);

            float d00 = Vector3.Distance(aL, bL);
            float d01 = Vector3.Distance(aL, bR);
            float d10 = Vector3.Distance(aR, bL);
            float d11 = Vector3.Distance(aR, bR);

            return Mathf.Min(best, d00, d01, d10, d11);
        }
    }
}