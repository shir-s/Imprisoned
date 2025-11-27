// FILEPATH: Assets/Scripts/Painting/Trails/StrokeTrailRecorder.cs
using UnityEngine;

/// <summary>
/// Responsible only for:
/// - Raycasting from the cube to the surface.
/// - Recording StrokeSamples into StrokeHistory.
/// - Applying pruning rules.
///
/// Does NOT clear history between strokes – so the trail is continuous.
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

    [Header("History Limits")]
    [Tooltip("Kept for API compatibility; currently not used in pruning.")]
    [SerializeField] private float maxHistoryLength = 50f;

    [SerializeField] private int maxHistoryPoints = 1000;

    [Header("History Mode")]
    [Tooltip("If true, when maxHistoryPoints is exceeded we delete ONLY one oldest point per new sample (boss/enemy mode).\n" +
             "If false, we delete a 5% chunk (scrolling snake mode used by shape detection scenes).")]
    [SerializeField] private bool useSinglePointPrune = false;

    [Header("Debug")]
    [SerializeField] private bool debugRays = false;
    [SerializeField] private bool debugSampleNormals = false;

    public StrokeHistory History { get; } = new StrokeHistory();

    private Transform _currentSurface;

    // -------- IMovementPainter --------

    public void OnMovementStart(Vector3 worldPos)
    {
        // Do NOT clear history here – we want an infinite snake.
        TryRecordAt(worldPos);
    }

    public void OnMoveStep(Vector3 from, Vector3 to, float stepMeters, float deltaTime)
    {
        TryRecordAt(to);
    }

    public void OnMovementEnd(Vector3 worldPos)
    {
        // Keep history.
    }

    // -------- Internal helpers --------

    private void TryRecordAt(Vector3 worldPos)
    {
        if (!TryRaycastSurface(worldPos, out var hit))
        {
            _currentSurface = null;
            return;
        }

        Transform surface = hit.collider.transform;
        _currentSurface = surface;

        // Compute world pos for spacing check
        Vector3 sampleWorldPos = hit.point;

        // If we already have samples, enforce min distance
        if (History.Count > 0)
        {
            Vector3 lastWorldPos = History[History.Count - 1].WorldPos;
            float dist = Vector3.Distance(lastWorldPos, sampleWorldPos);
            if (dist < minSampleDistance)
                return; // too close, skip this one
        }

        StrokeSample s = new StrokeSample
        {
            surface     = surface,
            localPos    = surface.InverseTransformPoint(sampleWorldPos),
            localNormal = surface.InverseTransformDirection(hit.normal),
            time        = Time.time
        };

        History.AddSample(s);

        // Choose prune mode
        if (useSinglePointPrune)
        {
            // Boss / enemy mode: never nuke a whole segment, only eat one oldest point.
            History.PruneSingleOldest(maxHistoryPoints);
        }
        else
        {
            // Original "scrolling snake" behavior.
            History.Prune(maxHistoryLength, maxHistoryPoints);
        }

        if (debugSampleNormals)
        {
            Debug.DrawRay(s.WorldPos, s.WorldNormal * 0.2f, Color.cyan, 0.3f);
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
        if (maxHistoryPoints < 10) maxHistoryPoints = 10;
        if (minSampleDistance < 0f) minSampleDistance = 0f;
    }
}
