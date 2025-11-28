// FILEPATH: Assets/Scripts/AI/StrokeTrailFollowerAI.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Enemy AI that follows the cube's stroke trail:
///
/// Modes:
/// - ConsumePoints:
///     * Enemy always goes to the closest stroke point in detectionRadius.
///     * When it reaches a point, it deletes it from StrokeHistory (RemoveAt).
///     * Never comes back to the same part of the trail.
/// 
/// - PersistentTrail:
///     * Points are never destroyed.
///     * Enemy always chooses the closest point within detectionRadius,
///       but avoids going straight back to the previous point if there is
///       any other option.
///     * Remembers how many times it visited each point index.
///     * At crossings, prefers points/directions with the lowest visit count
///       to avoid circles.
///     * If there are no other options except going back, it will go back,
///       effectively reversing and following the trail in the opposite
///       direction.
/// 
/// It also:
/// - Wanders when there is no trail in range.
/// - Auto-binds to the StrokeTrailRecorder with the most points if none
///   is assigned or current one has no points.
/// </summary>
[DisallowMultipleComponent]
public class StrokeTrailFollowerAI : MonoBehaviour
{
    private enum Behavior
    {
        Wander,
        FollowStroke
    }

    public enum FollowMode
    {
        ConsumePoints,
        PersistentTrail
    }

    [Header("Stroke Source")]
    [Tooltip("Optional. If left empty, the AI will auto-find the best StrokeTrailRecorder in the scene.")]
    [SerializeField] private StrokeTrailRecorder recorder;

    [Header("Follow Mode")]
    [Tooltip("ConsumePoints: deletes points when reached.\nPersistentTrail: keeps all points, uses visit counts to avoid circles.")]
    [SerializeField] private FollowMode followMode = FollowMode.ConsumePoints;

    [Header("Detection")]
    [Tooltip("Radius (in world units, XZ plane) within which the enemy can detect and choose stroke points.")]
    [SerializeField] private float detectionRadius = 3.0f;

    [Tooltip("How close we need to get to a stroke point to consider it 'reached'.")]
    [SerializeField] private float reachThreshold = 0.05f;

    [Header("Movement")]
    [Tooltip("Speed while following the stroke (world units/sec).")]
    [SerializeField] private float followSpeed = 2.0f;

    [Tooltip("Speed while wandering (world units/sec).")]
    [SerializeField] private float wanderSpeed = 1.0f;

    [Tooltip("How often to change wander direction (seconds).")]
    [SerializeField] private float wanderDirectionChangeInterval = 2.0f;

    [Header("Auto binding")]
    [Tooltip("If true, the AI will keep trying to re-bind to the StrokeTrailRecorder that has the most points in its history.")]
    [SerializeField] private bool autoBindToBestRecorder = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool debugGizmos = false;

    private Behavior _behavior = Behavior.Wander;

    // Index of the stroke point we are currently heading to
    private int _currentIndex = -1;

    // Wander state
    private Vector3 _wanderDir;
    private float _wanderTimer;

    // PersistentTrail visit tracking: how many times each index was visited
    private readonly Dictionary<int, int> _visitCounts = new Dictionary<int, int>();
    private int _lastIndexPersistent = -1;

    private void Awake()
    {
        if (debugLogs)
        {
            Debug.Log("[StrokeTrailFollowerAI] Awake on " + gameObject.name, this);
        }

        if (recorder != null && debugLogs)
        {
            int c = recorder.History != null ? recorder.History.Count : -1;
            Debug.Log("[StrokeTrailFollowerAI] Initial recorder: " + recorder.name +
                      " (HistoryCount=" + c + ")", this);
        }

        PickNewWanderDirection();
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        // Make sure we are bound to a useful StrokeTrailRecorder
        RefreshRecorderBinding();

        if (recorder == null)
        {
            // No recorder in scene at all – just wander (history is null)
            if (debugLogs)
            {
                Debug.Log("[StrokeTrailFollowerAI] No recorder available → wandering only.", this);
            }

            TickWander(dt, null);
            return;
        }

        // Always get history from current recorder (no caching of reference)
        StrokeHistory history = recorder.History;

        switch (_behavior)
        {
            case Behavior.Wander:
                TickWander(dt, history);
                break;

            case Behavior.FollowStroke:
                TickFollowStroke(dt, history);
                break;
        }
    }

    // ----------------------------------------------------------------
    // Recorder binding / re-binding
    // ----------------------------------------------------------------

    private void RefreshRecorderBinding()
    {
        if (!autoBindToBestRecorder)
            return;

        bool needSearch = recorder == null ||
                          recorder.History == null ||
                          recorder.History.Count == 0;

        if (!needSearch)
            return;

        StrokeTrailRecorder[] all = FindObjectsOfType<StrokeTrailRecorder>();
        StrokeTrailRecorder best = null;
        int bestCount = -1;

        foreach (var rec in all)
        {
            if (rec == null) continue;
            int c = rec.History != null ? rec.History.Count : 0;

            if (c > bestCount)
            {
                bestCount = c;
                best = rec;
            }
        }

        if (best != null && best != recorder)
        {
            recorder = best;
            ResetVisitHistory(); // different history, reset per-index data

            if (debugLogs)
            {
                Debug.Log("[StrokeTrailFollowerAI] Bound to recorder: " + recorder.name +
                          " (HistoryCount=" + bestCount + ")", this);
            }
        }
        else if (debugLogs)
        {
            if (all.Length == 0)
            {
                Debug.Log("[StrokeTrailFollowerAI] No StrokeTrailRecorder found in scene.", this);
            }
            else
            {
                Debug.Log("[StrokeTrailFollowerAI] Found " + all.Length +
                          " StrokeTrailRecorder(s), but none have points yet.", this);
            }
        }
    }

    private void ResetVisitHistory()
    {
        _visitCounts.Clear();
        _lastIndexPersistent = -1;
    }

    // ----------------------------------------------------------------
    // WANDER BEHAVIOR
    // ----------------------------------------------------------------

    private void TickWander(float dt, StrokeHistory history)
    {
        // Move in current wander direction
        if (_wanderDir.sqrMagnitude > 1e-4f)
        {
            Vector3 pos = transform.position;
            pos += _wanderDir * (wanderSpeed * dt);
            transform.position = pos;
        }

        // Change direction every interval
        _wanderTimer -= dt;
        if (_wanderTimer <= 0f)
        {
            PickNewWanderDirection();
        }

        // Try to detect a nearby stroke point and switch to follow mode
        if (history != null)
            TryAcquireStrokeTarget(history);
    }

    private void PickNewWanderDirection()
    {
        // Random direction on XZ plane
        Vector2 dir2D = Random.insideUnitCircle.normalized;
        if (dir2D.sqrMagnitude < 1e-4f)
            dir2D = Vector2.right;

        _wanderDir = new Vector3(dir2D.x, 0f, dir2D.y);
        _wanderTimer = wanderDirectionChangeInterval;

        if (debugLogs)
        {
            Debug.Log("[StrokeTrailFollowerAI] New wander direction: " + _wanderDir, this);
        }
    }

    // ----------------------------------------------------------------
    // FOLLOW STROKE BEHAVIOR
    // ----------------------------------------------------------------

    private void TickFollowStroke(float dt, StrokeHistory history)
    {
        if (history == null)
        {
            if (debugLogs)
                Debug.Log("[StrokeTrailFollowerAI] No history in Follow mode → Wander.", this);
            SwitchToWander();
            return;
        }

        int count = history.Count;
        if (count == 0)
        {
            if (debugLogs)
                Debug.Log("[StrokeTrailFollowerAI] History empty in Follow mode → Wander.", this);
            SwitchToWander();
            return;
        }

        // Ensure our index is valid, otherwise try to acquire a new target
        if (_currentIndex < 0 || _currentIndex >= count)
        {
            if (debugLogs)
            {
                Debug.Log("[StrokeTrailFollowerAI] Current index out of range in Follow mode. " +
                          "Re-acquiring target. Count=" + count, this);
            }

            if (!TryAcquireStrokeTarget(history))
            {
                SwitchToWander();
            }
            return;
        }

        // Move towards current target point (keeping our current Y height)
        StrokeSample sample = history[_currentIndex];
        Vector3 targetWS = sample.WorldPos;
        Vector3 pos = transform.position;

        Vector3 flatTarget = new Vector3(targetWS.x, pos.y, targetWS.z);
        Vector3 toTarget = flatTarget - pos;
        float dist = toTarget.magnitude;

        if (dist <= reachThreshold)
        {
            // We reached this point; now behavior depends on mode.
            if (followMode == FollowMode.ConsumePoints)
            {
                HandleReachedPoint_Consume(history);
            }
            else // PersistentTrail
            {
                HandleReachedPoint_Persistent(history);
            }

            return;
        }

        if (dist > 1e-5f)
        {
            Vector3 dir = toTarget / dist;
            pos += dir * (followSpeed * dt);
            transform.position = pos;
        }
    }

    private void HandleReachedPoint_Consume(StrokeHistory history)
    {
        if (debugLogs)
            Debug.Log($"[StrokeTrailFollowerAI] (Consume) Reached stroke index {_currentIndex}, deleting.", this);

        if (_currentIndex >= 0 && _currentIndex < history.Count)
        {
            history.RemoveAt(_currentIndex);
        }

        // After deletion, try to find a new nearest point in radius.
        if (!TryAcquireStrokeTarget(history))
        {
            if (debugLogs)
                Debug.Log("[StrokeTrailFollowerAI] (Consume) No more points in radius after delete → Wander.", this);
            SwitchToWander();
        }
    }

    private void HandleReachedPoint_Persistent(StrokeHistory history)
    {
        if (debugLogs)
            Debug.Log($"[StrokeTrailFollowerAI] (Persistent) Reached stroke index {_currentIndex}", this);

        // Register visit for this point index
        RegisterVisit(_currentIndex);

        // Greedy step: choose next closest point in radius,
        // avoiding immediate backtracking if possible, and
        // preferring points with fewer total visits.
        if (!ChooseNextPersistentTarget(history))
        {
            if (debugLogs)
                Debug.Log("[StrokeTrailFollowerAI] (Persistent) No suitable next point → Wander.", this);
            SwitchToWander();
        }
    }

    private void RegisterVisit(int index)
    {
        if (index < 0)
            return;

        if (!_visitCounts.TryGetValue(index, out int count))
            count = 0;

        count++;
        _visitCounts[index] = count;
        _lastIndexPersistent = index;
    }

    /// <summary>
    /// PersistentTrail logic:
    /// - Look at all points within detectionRadius.
    /// - Prefer points with the fewest visits.
    /// - Avoid going straight back to the lastIndex if there are other options.
    /// - If only the lastIndex is available, allow it (to reverse at trail end).
    /// </summary>
    private bool ChooseNextPersistentTarget(StrokeHistory history)
    {
        int count = history.Count;
        if (count == 0)
            return false;

        Vector3 pos = transform.position;
        Vector2 enemyXZ = new Vector2(pos.x, pos.z);
        float maxDistSq = detectionRadius * detectionRadius;

        // First pass: find all candidates in radius
        List<int> candidates = new List<int>();
        float[] dists = new float[count];

        for (int i = 0; i < count; i++)
        {
            Vector3 p = history[i].WorldPos;
            Vector2 pXZ = new Vector2(p.x, p.z);
            float dSq = (pXZ - enemyXZ).sqrMagnitude;
            dists[i] = dSq;

            if (dSq <= maxDistSq)
            {
                candidates.Add(i);
            }
        }

        if (candidates.Count == 0)
            return false;

        // Among candidates, find the minimal visit count
        int minVisits = int.MaxValue;
        foreach (int idx in candidates)
        {
            int v = _visitCounts.TryGetValue(idx, out int vc) ? vc : 0;
            if (v < minVisits)
                minVisits = v;
        }

        // Filter to those that have the minimal visit count
        List<int> bestByVisits = new List<int>();
        foreach (int idx in candidates)
        {
            int v = _visitCounts.TryGetValue(idx, out int vc) ? vc : 0;
            if (v == minVisits)
                bestByVisits.Add(idx);
        }

        // If possible, avoid immediately going back to lastIndex
        List<int> filtered = new List<int>();
        foreach (int idx in bestByVisits)
        {
            if (idx != _lastIndexPersistent)
                filtered.Add(idx);
        }

        List<int> finalCandidates = filtered.Count > 0 ? filtered : bestByVisits;

        // Choose the nearest among finalCandidates
        int bestIndex = -1;
        float bestDistSq = float.PositiveInfinity;

        foreach (int idx in finalCandidates)
        {
            float dSq = dists[idx];
            if (dSq < bestDistSq)
            {
                bestDistSq = dSq;
                bestIndex = idx;
            }
        }

        if (bestIndex < 0)
            return false;

        _currentIndex = bestIndex;

        if (debugLogs)
        {
            int v = _visitCounts.TryGetValue(bestIndex, out int vc) ? vc : 0;
            Debug.Log($"[StrokeTrailFollowerAI] (Persistent) Next index={bestIndex}, dist={Mathf.Sqrt(bestDistSq):F3}, visits={v}", this);
        }

        return true;
    }

    private void SwitchToWander()
    {
        _behavior = Behavior.Wander;
        _currentIndex = -1;
        _wanderTimer = 0f; // so it picks a fresh direction quickly

        if (debugLogs)
            Debug.Log("[StrokeTrailFollowerAI] SwitchToWander", this);
    }

    private void SwitchToFollow(int index)
    {
        _behavior = Behavior.FollowStroke;
        _currentIndex = index;

        if (debugLogs)
        {
            int count = (recorder != null && recorder.History != null) ? recorder.History.Count : -1;
            Debug.Log($"[StrokeTrailFollowerAI] SwitchToFollow index={index}, historyCount={count}, mode={followMode}", this);
        }
    }

    // ----------------------------------------------------------------
    // ACQUIRE TARGET – nearest point in radius
    // ----------------------------------------------------------------

    /// <summary>
    /// Look for the nearest stroke point within detectionRadius (XZ plane).
    /// If found, switch to FollowStroke and set _currentIndex.
    /// </summary>
    private bool TryAcquireStrokeTarget(StrokeHistory history)
    {
        if (history == null)
        {
            if (debugLogs)
                Debug.Log("[StrokeTrailFollowerAI] TryAcquireStrokeTarget: history is null.", this);
            return false;
        }

        int count = history.Count;
        if (count == 0)
        {
            if (debugLogs)
                Debug.Log("[StrokeTrailFollowerAI] TryAcquireStrokeTarget: history empty.", this);
            return false;
        }

        Vector3 pos = transform.position;
        Vector2 enemyXZ = new Vector2(pos.x, pos.z);
        float maxDistSq = detectionRadius * detectionRadius;

        int bestIndex = -1;
        float bestDistSq = float.PositiveInfinity;

        for (int i = 0; i < count; i++)
        {
            Vector3 p = history[i].WorldPos;
            Vector2 pXZ = new Vector2(p.x, p.z);
            float dSq = (pXZ - enemyXZ).sqrMagnitude;

            if (dSq < bestDistSq && dSq <= maxDistSq)
            {
                bestDistSq = dSq;
                bestIndex = i;
            }
        }

        if (debugLogs)
        {
            if (bestIndex < 0)
            {
                Debug.Log("[StrokeTrailFollowerAI] TryAcquireStrokeTarget: no point in radius. " +
                          $"HistoryCount={count}, detectionRadius={detectionRadius}", this);
            }
            else
            {
                Debug.Log("[StrokeTrailFollowerAI] TryAcquireStrokeTarget: found index=" + bestIndex +
                          ", dist=" + Mathf.Sqrt(bestDistSq), this);
            }
        }

        if (bestIndex < 0)
            return false;

        SwitchToFollow(bestIndex);
        return true;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!debugGizmos)
            return;

        Gizmos.color = new Color(0.3f, 1f, 0.3f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        if (recorder != null)
        {
            StrokeHistory history = recorder.History;
            if (_behavior == Behavior.FollowStroke && history != null)
            {
                if (_currentIndex >= 0 && _currentIndex < history.Count)
                {
                    Gizmos.color = Color.red;
                    Gizmos.DrawSphere(history[_currentIndex].WorldPos, 0.03f);
                    Gizmos.DrawLine(transform.position, history[_currentIndex].WorldPos);
                }
            }
        }
    }
#endif
}
