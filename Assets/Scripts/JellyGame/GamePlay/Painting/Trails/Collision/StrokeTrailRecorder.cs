// FILEPATH: Assets/Scripts/Painting/Trails/StrokeTrailRecorder.cs

using JellyGame.GamePlay.Painting.Trails.Visibility;
using UnityEngine;

namespace JellyGame.GamePlay.Painting.Trails.Collision
{
    /// <summary>
    /// Responsible only for:
    /// - Raycasting from the cube to the surface.
    /// - Recording StrokeSamples into StrokeHistory.
    /// - Pruning ONLY by time-to-live (TTL).
    ///
    /// Does NOT clear history between strokes – so the trail is continuous.
    /// Recording can be turned on/off at runtime via RecordingEnabled.
    /// </summary>
    [DisallowMultipleComponent]
    public class StrokeTrailRecorder : MonoBehaviour, IMovementPainter
    {
        [Header("Raycast")]
        [SerializeField] private LayerMask surfaceMask = ~0;
        [SerializeField] private float rayDistance = 2f;
        [SerializeField] private bool useWorldDown = true;

        [Header("Sampling")]
        [Tooltip("Minimum world-space distance between consecutive stored samples.")]
        [SerializeField] private float minSampleDistance = 0.02f;

        [Header("Front Edge Offset")]
        [Tooltip("If true, offset the sample point to the front edge of the character (based on movement direction).")]
        [SerializeField] private bool useForwardOffset = true;

        [Tooltip("Multiplier for forward offset. 0.5 = half of character depth (front edge).")]
        [SerializeField, Range(0f, 1f)] private float forwardOffsetMultiplier = 0.5f;

        [Header("History Lifetime (ONLY prune rule)")]
        [Tooltip("Each point is deleted after it lived for this many seconds.\n" +
                 "0 or negative = never delete by time.")]
        [SerializeField] private float pointLifetimeSeconds = 10f;

        [Header("Recording")]
        [Tooltip("If false, movement events will not add new samples to the history.")]
        [SerializeField] private bool recordingEnabled = true;

        [Header("Debug")]
        [SerializeField] private bool debugRays = false;
        [SerializeField] private bool debugSampleNormals = false;
        [SerializeField] private bool debugForwardOffset = false;

        public StrokeHistory History { get; } = new StrokeHistory();

        /// <summary>
        /// Global toggle for whether this recorder should add new stroke samples.
        /// Does NOT clear existing history, only stops new points.
        /// </summary>
        public bool RecordingEnabled
        {
            get => recordingEnabled;
            set => recordingEnabled = value;
        }

        private Transform _currentSurface;
        private Vector3 _lastMoveDirection = Vector3.forward;

        // -------- IMovementPainter --------

        public void OnMovementStart(Vector3 worldPos)
        {
            // Do NOT clear history here – we want a continuous trail.
            TryRecordAt(worldPos, Vector3.zero);
        }

        public void OnMoveStep(Vector3 from, Vector3 to, float stepMeters, float deltaTime)
        {
            Vector3 moveDir = (to - from);
            if (moveDir.sqrMagnitude > 0.0001f)
                _lastMoveDirection = moveDir.normalized;
            
            TryRecordAt(to, _lastMoveDirection);
        }

        public void OnMovementEnd(Vector3 worldPos)
        {
            // Keep history.
        }

        private void Update()
        {
            // Prune even if the player/enemy stops moving (so old points still expire).
            PruneExpiredPoints();
        }

        // -------- Internal helpers --------

        private void TryRecordAt(Vector3 worldPos, Vector3 moveDirection)
        {
            // global gate for rivers / special zones
            if (!recordingEnabled)
                return;

            if (!TryRaycastSurface(worldPos, out var hit))
            {
                _currentSurface = null;
                return;
            }

            Transform surface = hit.collider.transform;
            _currentSurface = surface;

            Vector3 sampleWorldPos = hit.point;

            // Apply forward offset if enabled
            if (useForwardOffset && moveDirection.sqrMagnitude > 0.001f)
            {
                // Calculate forward offset based on character scale
                float forwardOffset = Mathf.Abs(transform.lossyScale.z) * forwardOffsetMultiplier;
                Vector3 offsetPos = sampleWorldPos + moveDirection.normalized * forwardOffset;
                
                // Raycast at the offset position to get the actual surface point
                if (TryRaycastSurface(offsetPos + Vector3.up * 0.1f, out var hitOffset))
                {
                    sampleWorldPos = hitOffset.point;
                    
                    if (debugForwardOffset)
                        Debug.DrawLine(hit.point, sampleWorldPos, Color.yellow, 0.5f);
                }
            }

            // If we already have samples, enforce min distance
            if (History.Count > 0)
            {
                Vector3 lastWorldPos = History[History.Count - 1].WorldPos;
                float dist = Vector3.Distance(lastWorldPos, sampleWorldPos);
                if (dist < minSampleDistance)
                    return;
            }

            StrokeSample s = new StrokeSample
            {
                surface     = surface,
                localPos    = surface.InverseTransformPoint(sampleWorldPos),
                localNormal = surface.InverseTransformDirection(hit.normal),
                time        = Time.time
            };

            History.AddSample(s);

            // ONLY prune rule: time-to-live
            PruneExpiredPoints();

            if (debugSampleNormals)
                Debug.DrawRay(s.WorldPos, s.WorldNormal * 0.2f, Color.cyan, 0.3f);
        }

        private void PruneExpiredPoints()
        {
            if (pointLifetimeSeconds <= 0f)
                return;

            int count = History.Count;
            if (count == 0)
                return;

            float cutoffTime = Time.time - pointLifetimeSeconds;

            // Times are monotonic (we always store Time.time when added),
            // so expired points are a prefix of the list.
            int lastExpiredIndex = -1;
            for (int i = 0; i < count; i++)
            {
                if (History[i].time <= cutoffTime)
                    lastExpiredIndex = i;
                else
                    break;
            }

            if (lastExpiredIndex >= 0)
            {
                // Remove all expired points [0..lastExpiredIndex]
                History.ConsumeUpTo(lastExpiredIndex);
            }
        }

        private bool TryRaycastSurface(Vector3 fromPos, out RaycastHit hit)
        {
            Vector3 dir = useWorldDown ? Vector3.down : -transform.up;
            Vector3 start = fromPos - dir * 0.01f; // small lift

            if (debugRays)
                Debug.DrawRay(start, dir * rayDistance, Color.yellow, 0.1f);

            return Physics.Raycast(start, dir, out hit, rayDistance, surfaceMask, QueryTriggerInteraction.Collide);
        }

        private void OnValidate()
        {
            if (minSampleDistance < 0f) minSampleDistance = 0f;
            // allow <= 0 to mean "never expire"
        }
    }
}