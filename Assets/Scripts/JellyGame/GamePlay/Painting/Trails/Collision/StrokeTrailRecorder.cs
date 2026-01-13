// FILEPATH: Assets/Scripts/Painting/Trails/StrokeTrailRecorder.cs

using System.Collections.Generic;
using JellyGame.GamePlay.Painting.Trails.Visibility;
using UnityEngine;

namespace JellyGame.GamePlay.Painting.Trails.Collision
{
    [DisallowMultipleComponent]
    public class StrokeTrailRecorder : MonoBehaviour, IMovementPainter
    {
        [Header("Raycast")]
        [SerializeField] private LayerMask surfaceMask = ~0;
        [SerializeField] private float rayDistance = 2f;
        [SerializeField] private bool useWorldDown = true;

        [Header("Sampling")]
        [SerializeField] private float minSampleDistance = 0.02f;

        [Header("Front Edge Offset")]
        [SerializeField] private bool useForwardOffset = true;
        [SerializeField, Range(0f, 1f)] private float forwardOffsetMultiplier = 0.5f;

        [Header("Edge Samples (dynamic, size-based)")]
        [SerializeField] private bool recordEdgePairs = true;

        [Header("History Lifetime (ONLY prune rule)")]
        [SerializeField] private float pointLifetimeSeconds = 10f;

        [Header("Recording")]
        [SerializeField] private bool recordingEnabled = true;

        [Header("Integration (auto if null)")]
        [SerializeField] private RenderTextureTrailPainter trailPainter;

        [Header("Debug")]
        [SerializeField] private bool debugRays = false;
        [SerializeField] private bool debugSampleNormals = false;
        [SerializeField] private bool debugForwardOffset = false;
        [SerializeField] private bool debugEdgePairs = false;

        public StrokeHistory History { get; } = new StrokeHistory();

        public bool RecordingEnabled
        {
            get => recordingEnabled;
            set => recordingEnabled = value;
        }

        public bool EdgePairsEnabled => recordEdgePairs;

        /// <summary>
        /// Stored in surface-local space so they move with the tilted surface like History points.
        /// </summary>
        public struct EdgePair
        {
            public Transform surface;
            public bool hasLeft;
            public bool hasRight;
            public Vector3 localLeft;
            public Vector3 localRight;
            public float time;

            public Vector3 GetLeftWorld(Vector3 fallbackWorld)
            {
                if (!hasLeft || surface == null) return fallbackWorld;
                return surface.TransformPoint(localLeft);
            }

            public Vector3 GetRightWorld(Vector3 fallbackWorld)
            {
                if (!hasRight || surface == null) return fallbackWorld;
                return surface.TransformPoint(localRight);
            }
        }

        public IReadOnlyList<EdgePair> EdgePairs => _edgePairs;
        private readonly List<EdgePair> _edgePairs = new List<EdgePair>(4096);

        private Vector3 _lastMoveDirection = Vector3.forward;

        private void Awake()
        {
            if (trailPainter == null)
                trailPainter = GetComponent<RenderTextureTrailPainter>();
        }

        public void OnMovementStart(Vector3 worldPos)
        {
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
        }

        private void Update()
        {
            PruneExpiredPoints();
        }

        /// <summary>
        /// Removes samples [0..indexInclusive] from History AND keeps EdgePairs in sync.
        /// Use this when you want the "clear on closure" behavior.
        /// </summary>
        public void ConsumeUpToInclusive(int indexInclusive)
        {
            int count = History.Count;
            if (count == 0)
                return;

            indexInclusive = Mathf.Clamp(indexInclusive, 0, count - 1);

            History.ConsumeUpTo(indexInclusive);

            int removeCount = Mathf.Min(indexInclusive + 1, _edgePairs.Count);
            if (removeCount > 0)
                _edgePairs.RemoveRange(0, removeCount);
        }

        private void TryRecordAt(Vector3 worldPos, Vector3 moveDirection)
        {
            if (!recordingEnabled)
                return;

            if (!TryRaycastSurface(worldPos, out var hit))
                return;

            Transform surface = hit.collider.transform;

            Vector3 sampleWorldPos = hit.point;
            Vector3 sampleWorldNormal = hit.normal;

            if (useForwardOffset && moveDirection.sqrMagnitude > 0.001f)
            {
                float sizeWorldMultiplier = GetPainterSizeWorldMultiplier();
                float forwardOffset = Mathf.Abs(transform.lossyScale.z) * forwardOffsetMultiplier * sizeWorldMultiplier;

                Vector3 offsetPos = sampleWorldPos + moveDirection.normalized * forwardOffset;

                if (TryRaycastSurface(offsetPos + Vector3.up * 0.1f, out var hitOffset))
                {
                    sampleWorldPos = hitOffset.point;
                    sampleWorldNormal = hitOffset.normal;

                    if (debugForwardOffset)
                        Debug.DrawLine(hit.point, sampleWorldPos, Color.yellow, 0.5f);
                }
            }

            if (History.Count > 0)
            {
                Vector3 lastWorldPos = History[History.Count - 1].WorldPos;
                if (Vector3.Distance(lastWorldPos, sampleWorldPos) < minSampleDistance)
                    return;
            }

            StrokeSample s = new StrokeSample
            {
                surface = surface,
                localPos = surface.InverseTransformPoint(sampleWorldPos),
                localNormal = surface.InverseTransformDirection(sampleWorldNormal),
                time = Time.time
            };

            History.AddSample(s);

            if (recordEdgePairs)
                _edgePairs.Add(BuildEdgePair(surface, sampleWorldPos, sampleWorldNormal, moveDirection));
            else
                _edgePairs.Add(new EdgePair
                {
                    surface = surface,
                    hasLeft = false,
                    hasRight = false,
                    localLeft = Vector3.zero,
                    localRight = Vector3.zero,
                    time = Time.time
                });

            PruneExpiredPoints();

            if (debugSampleNormals)
                Debug.DrawRay(s.WorldPos, s.WorldNormal * 0.2f, Color.cyan, 0.3f);
        }

        private EdgePair BuildEdgePair(Transform surface, Vector3 centerWorldPos, Vector3 worldNormal, Vector3 moveDirection)
        {
            EdgePair pair = new EdgePair
            {
                surface = surface,
                hasLeft = false,
                hasRight = false,
                localLeft = Vector3.zero,
                localRight = Vector3.zero,
                time = Time.time
            };

            if (surface == null)
                return pair;

            if (moveDirection.sqrMagnitude < 0.001f)
                return pair;

            Vector3 upN = worldNormal.normalized;

            Vector3 forwardOnPlane = Vector3.ProjectOnPlane(moveDirection.normalized, upN);
            if (forwardOnPlane.sqrMagnitude < 0.0001f)
                return pair;

            forwardOnPlane.Normalize();

            Vector3 rightOnPlane = Vector3.Cross(upN, forwardOnPlane).normalized;
            if (rightOnPlane.sqrMagnitude < 0.0001f)
                return pair;

            float sizeWorldMultiplier = GetPainterSizeWorldMultiplier();
            float halfWorldX = 0.5f * Mathf.Abs(transform.lossyScale.x) * sizeWorldMultiplier;
            if (halfWorldX <= 0.00001f)
                return pair;

            Vector3 leftCandidate = centerWorldPos - rightOnPlane * halfWorldX;
            Vector3 rightCandidate = centerWorldPos + rightOnPlane * halfWorldX;

            Vector3 lift = upN * 0.15f;

            if (TryRaycastSurface(leftCandidate + lift, out var hitL))
            {
                pair.hasLeft = true;
                pair.localLeft = surface.InverseTransformPoint(hitL.point);

                if (debugEdgePairs)
                    Debug.DrawLine(centerWorldPos, hitL.point, Color.green, 0.25f);
            }

            if (TryRaycastSurface(rightCandidate + lift, out var hitR))
            {
                pair.hasRight = true;
                pair.localRight = surface.InverseTransformPoint(hitR.point);

                if (debugEdgePairs)
                    Debug.DrawLine(centerWorldPos, hitR.point, Color.blue, 0.25f);
            }

            return pair;
        }

        private float GetPainterSizeWorldMultiplier()
        {
            return trailPainter != null ? trailPainter.SizeWorldMultiplierForRecorder : 1f;
        }

        private void PruneExpiredPoints()
        {
            if (pointLifetimeSeconds <= 0f)
                return;

            int count = History.Count;
            if (count == 0)
                return;

            float cutoffTime = Time.time - pointLifetimeSeconds;

            int lastExpiredIndex = -1;
            for (int i = 0; i < count; i++)
            {
                if (History[i].time <= cutoffTime)
                    lastExpiredIndex = i;
                else
                    break;
            }

            if (lastExpiredIndex < 0)
                return;

            ConsumeUpToInclusive(lastExpiredIndex);
        }

        private bool TryRaycastSurface(Vector3 fromPos, out RaycastHit hit)
        {
            Vector3 dir = useWorldDown ? Vector3.down : -transform.up;
            Vector3 start = fromPos - dir * 0.01f;

            if (debugRays)
                Debug.DrawRay(start, dir * rayDistance, Color.yellow, 0.1f);

            return Physics.Raycast(start, dir, out hit, rayDistance, surfaceMask, QueryTriggerInteraction.Collide);
        }

        private void OnValidate()
        {
            if (minSampleDistance < 0f) minSampleDistance = 0f;
        }
    }
}
