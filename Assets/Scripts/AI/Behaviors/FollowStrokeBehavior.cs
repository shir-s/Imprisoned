// FILEPATH: Assets/Scripts/AI/StrokeTrailFollowBehavior.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Behavior that follows the cube's stroke trail.
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
/// CanActivate():
/// - Only true if the brain has a StrokeHistory AND there is at least one
///   point within detectionRadius.
/// </summary>
[DisallowMultipleComponent]
public class FollowStrokeBehavior : MonoBehaviour, IEnemyBehavior
{
    public enum FollowMode
    {
        ConsumePoints,
        PersistentTrail
    }

    [Header("Behavior Priority")]
    [Tooltip("Higher value = higher priority. Should be higher than Wander (e.g. 10).")]
    [SerializeField] private int priority = 10;

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

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool debugGizmos = false;

    private BehaviorManager _brain;

    // Index of the stroke point we are currently heading to
    private int _currentIndex = -1;

    // PersistentTrail visit tracking: how many times each index was visited
    private readonly Dictionary<int, int> _visitCounts = new Dictionary<int, int>();
    private int _lastIndexPersistent = -1;

    public int Priority => priority;

    private void Awake()
    {
        _brain = GetComponent<BehaviorManager>();
        if (_brain == null)
        {
            Debug.LogError("[StrokeTrailFollowBehavior] Missing StrokeTrailFollowerAI on the same GameObject.", this);
        }
    }

    // --------------------------------------------------------
    // IEnemyBehavior
    // --------------------------------------------------------

    public bool CanActivate()
    {
        if (_brain == null)
            return false;

        StrokeHistory history = _brain.CurrentHistory;
        if (history == null || history.Count == 0)
            return false;

        // Only activate if there is at least one point within detectionRadius.
        return HasAnyPointInRadius(history);
    }

    public void OnEnter()
    {
        _currentIndex = -1;
        _visitCounts.Clear();
        _lastIndexPersistent = -1;

        if (debugLogs)
        {
            Debug.Log("[StrokeTrailFollowBehavior] OnEnter (mode=" + followMode + ")", this);
        }

        // Try to immediately acquire a target.
        StrokeHistory history = _brain != null ? _brain.CurrentHistory : null;
        if (history != null)
        {
            TryAcquireStrokeTarget(history);
        }
    }

    public void Tick(float deltaTime)
    {
        if (_brain == null)
            return;

        StrokeHistory history = _brain.CurrentHistory;
        if (history == null || history.Count == 0)
        {
            // No history -> can't really follow, but the brain will decide
            // next frame that we can't activate anymore.
            _currentIndex = -1;
            return;
        }

        TickFollowStroke(deltaTime, history);
    }

    public void OnExit()
    {
        if (debugLogs)
        {
            Debug.Log("[StrokeTrailFollowBehavior] OnExit", this);
        }

        _currentIndex = -1;
        _visitCounts.Clear();
        _lastIndexPersistent = -1;
    }

    // --------------------------------------------------------
    // Core follow logic (adapted from old StrokeTrailFollowerAI)
    // --------------------------------------------------------

    private void TickFollowStroke(float dt, StrokeHistory history)
    {
        int count = history.Count;
        if (count == 0)
        {
            _currentIndex = -1;
            return;
        }

        // Ensure our index is valid, otherwise try to acquire a new target
        if (_currentIndex < 0 || _currentIndex >= count)
        {
            if (debugLogs)
            {
                Debug.Log("[StrokeTrailFollowBehavior] Current index out of range. Re-acquiring target. Count=" + count, this);
            }

            if (!TryAcquireStrokeTarget(history))
            {
                // No more valid targets in radius → brain will drop us next frame
                _currentIndex = -1;
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
            Debug.Log($"[StrokeTrailFollowBehavior] (Consume) Reached stroke index {_currentIndex}, deleting.", this);

        if (_currentIndex >= 0 && _currentIndex < history.Count)
        {
            history.RemoveAt(_currentIndex);
        }

        // After deletion, try to find a new nearest point in radius.
        if (!TryAcquireStrokeTarget(history))
        {
            if (debugLogs)
                Debug.Log("[StrokeTrailFollowBehavior] (Consume) No more points in radius after delete.", this);
            _currentIndex = -1;
        }
    }

    private void HandleReachedPoint_Persistent(StrokeHistory history)
    {
        if (debugLogs)
            Debug.Log($"[StrokeTrailFollowBehavior] (Persistent) Reached stroke index {_currentIndex}", this);

        // Register visit for this point index
        RegisterVisit(_currentIndex);

        // Greedy step: choose next closest point in radius,
        // avoiding immediate backtracking if possible, and
        // preferring points with fewer total visits.
        if (!ChooseNextPersistentTarget(history))
        {
            if (debugLogs)
                Debug.Log("[StrokeTrailFollowBehavior] (Persistent) No suitable next point.", this);
            _currentIndex = -1;
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
            Debug.Log($"[StrokeTrailFollowBehavior] (Persistent) Next index={bestIndex}, dist={Mathf.Sqrt(bestDistSq):F3}, visits={v}", this);
        }

        return true;
    }

    // ----------------------------------------------------------------
    // Target acquisition / CanActivate helper
    // ----------------------------------------------------------------

    private bool HasAnyPointInRadius(StrokeHistory history)
    {
        int count = history.Count;
        if (count == 0)
            return false;

        Vector3 pos = transform.position;
        Vector2 enemyXZ = new Vector2(pos.x, pos.z);
        float maxDistSq = detectionRadius * detectionRadius;

        for (int i = 0; i < count; i++)
        {
            Vector3 p = history[i].WorldPos;
            Vector2 pXZ = new Vector2(p.x, p.z);
            float dSq = (pXZ - enemyXZ).sqrMagnitude;
            if (dSq <= maxDistSq)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Look for the nearest stroke point within detectionRadius (XZ plane).
    /// If found, set _currentIndex.
    /// </summary>
    private bool TryAcquireStrokeTarget(StrokeHistory history)
    {
        if (history == null)
        {
            if (debugLogs)
                Debug.Log("[StrokeTrailFollowBehavior] TryAcquireStrokeTarget: history is null.", this);
            return false;
        }

        int count = history.Count;
        if (count == 0)
        {
            if (debugLogs)
                Debug.Log("[StrokeTrailFollowBehavior] TryAcquireStrokeTarget: history empty.", this);
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
                Debug.Log("[StrokeTrailFollowBehavior] TryAcquireStrokeTarget: no point in radius. " +
                          $"HistoryCount={count}, detectionRadius={detectionRadius}", this);
            }
            else
            {
                Debug.Log("[StrokeTrailFollowBehavior] TryAcquireStrokeTarget: found index=" + bestIndex +
                          ", dist=" + Mathf.Sqrt(bestDistSq), this);
            }
        }

        if (bestIndex < 0)
            return false;

        _currentIndex = bestIndex;
        return true;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!debugGizmos)
            return;

        Gizmos.color = new Color(0.3f, 1f, 0.3f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
    }
#endif
}
