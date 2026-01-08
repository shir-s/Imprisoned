// FILEPATH: Assets/Scripts/Painting/Shapes/StrokeTrailAnalyzer.cs

using JellyGame.GamePlay.Painting.Trails.Collision;
using JellyGame.GamePlay.Painting.Trails.Visibility;
using UnityEngine;

namespace JellyGame.GamePlay.Painting.Shapes
{
    [DisallowMultipleComponent]
    public class StrokeTrailAnalyzer : MonoBehaviour, IMovementPainter
    {
        [Header("Loop Detection")]
        [Tooltip("Max world distance between loop start and end for the loop to count as 'closed'.")]
        [SerializeField] private float closureMaxDistance = 0.25f;

        [Tooltip("Minimum number of indices between loop start and end.\n" +
                 "Prevents treating tiny wiggles as full loops.")]
        [SerializeField] private int minLoopIndexSeparation = 6;

        [Header("Path Analysis")]
        [Tooltip("Minimum separation (meters along the stroke) between accepted corners.\nUsed when building the simplified loop path.")]
        [SerializeField] private float minCornerSeparationMeters = 2f;

        [Header("Debug")]
        [Tooltip("Log when loops are accepted and sent to detectors.")]
        [SerializeField] private bool debugLoop = false;

        [Tooltip("If true, also log per-step 'Best candidate' / 'not closed yet' spam.")]
        [SerializeField] private bool debugLoopVerbose = false;

        private StrokeTrailRecorder _recorder;
        private StrokeHistory _history;
        private IStrokeShapeDetector[] _detectors;

        // Prevents spamming while we stay in a closed area
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

            if (debugLoop)
                Debug.Log($"[StrokeTrailAnalyzer] Found {_detectors.Length} IStrokeShapeDetector components on {name}.");
        }

        // -------- IMovementPainter --------

        public void OnMovementStart(Vector3 worldPos)
        {
            _closureHandled = false;
        }

        public void OnMoveStep(Vector3 from, Vector3 to, float stepMeters, float deltaTime)
        {
            TryDetectLoopAndNotifyDetectors();
        }

        public void OnMovementEnd(Vector3 worldPos)
        {
            _closureHandled = false;
        }

        // -------- Core loop detection --------

        private void TryDetectLoopAndNotifyDetectors()
        {
            if (_history == null)
                return;

            int count = _history.Count;
            if (count < 4)
            {
                if (debugLoopVerbose)
                    Debug.Log($"[StrokeTrailAnalyzer] Not enough points yet (count={count}).");
                return;
            }

            int last = count - 1;
            var lastPos = _history[last].WorldPos;
            float lenToLast = _history.GetLengthAt(last); // only for debug

            int bestStart = -1;
            float bestChord = float.PositiveInfinity;
            float bestLoopLen = 0f;

            // Scan earlier samples to find best loop candidate.
            for (int i = 0; i < last - 1; i++)
            {
                int indexDiff = last - i;
                if (indexDiff < minLoopIndexSeparation)
                    continue;

                float chord = Vector3.Distance(lastPos, _history[i].WorldPos);
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
            {
                if (debugLoopVerbose)
                    Debug.Log("[StrokeTrailAnalyzer] No loop candidate with enough index separation.");
                return;
            }

            if (debugLoopVerbose)
            {
                Debug.Log(
                    $"[StrokeTrailAnalyzer] Best candidate: start={bestStart}, last={last}, " +
                    $"indexDiff={last - bestStart}, len≈{bestLoopLen:F3}, " +
                    $"chord={bestChord:F3}, closureMax={closureMaxDistance:F3}"
                );
            }

            // Hysteresis: if we are clearly far away, re-arm detection.
            if (bestChord > closureMaxDistance * 1.5f)
                _closureHandled = false;

            bool closedNow = bestChord <= closureMaxDistance;

            if (!closedNow)
            {
                if (debugLoopVerbose)
                    Debug.Log("[StrokeTrailAnalyzer] Candidate not closed yet (too far).");
                return;
            }

            if (_closureHandled)
            {
                if (debugLoopVerbose)
                    Debug.Log("[StrokeTrailAnalyzer] Closure already handled for this pass.");
                return;
            }

            _closureHandled = true;

            if (debugLoop)
                Debug.Log($"[StrokeTrailAnalyzer] Loop candidate ACCEPTED [{bestStart}..{last}] (len≈{bestLoopLen:F3})");

            // -------- Build pre-analyzed loop data (corners) --------
            StrokePathLoop loopPath = StrokePathBuilder.BuildLoopCorners(
                _history,
                bestStart,
                last,
                minCornerSeparationMeters
            );

            if (debugLoop)
            {
                int cornerCount = loopPath != null ? loopPath.CornerCount : 0;
                Debug.Log($"[StrokeTrailAnalyzer] Built StrokePathLoop with {cornerCount} corners for segment [{bestStart}..{last}].");
            }

            StrokeLoopSegment seg = new StrokeLoopSegment
            {
                history = _history,
                startIndex = bestStart,
                endIndexInclusive = last,
                path = loopPath
            };

            // -------- Call detectors on this loop segment --------
            if (_detectors == null || _detectors.Length == 0)
            {
                if (debugLoop)
                    Debug.Log("[StrokeTrailAnalyzer] No IStrokeShapeDetector found on this GameObject.");
            }
            else
            {
                foreach (var det in _detectors)
                {
                    if (det == null) continue;

                    if (debugLoop)
                        Debug.Log($"[StrokeTrailAnalyzer] Sending loop [{bestStart}..{last}] to detector {det.GetType().Name}");

                    // We still notify detectors, but we DO NOT delete/clear history here anymore.
                    bool handled = det.TryHandleShape(seg);
                    if (handled)
                        break;
                }
            }

            // IMPORTANT CHANGE:
            // We no longer clear history on intersections/loops.
            // Point deletion is now handled ONLY by StrokeTrailRecorder's time-to-live pruning.

            // Allow new loops again only after we move away (handled by hysteresis above).
        }
    }
}
