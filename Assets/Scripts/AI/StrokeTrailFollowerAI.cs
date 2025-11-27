// FILEPATH: Assets/Scripts/AI/StrokeTrailFollowerAI.cs
using UnityEngine;

/// <summary>
/// Enemy AI that follows the cube's stroke trail:
/// - Behavior 1: Wander around.
/// - Behavior 2: FollowStroke – always goes to the CLOSEST stroke point
///   within detectionRadius, deletes it when reached, then picks the next
///   closest point in radius. It does NOT care about stroke index order,
///   only distance.
/// 
/// It tries to automatically bind to the "active" StrokeTrailRecorder:
/// - If no recorder is assigned, or the current one has 0 points,
///   it searches all StrokeTrailRecorders in the scene and picks
///   the one with the largest History.Count (usually the active cube).
/// 
/// Works nicely with StrokeTrailRecorder in "single point prune" mode,
/// but does not depend on that.
/// </summary>
[DisallowMultipleComponent]
public class StrokeTrailFollowerAI : MonoBehaviour
{
    private enum Behavior
    {
        Wander,
        FollowStroke
    }

    [Header("Stroke Source")]
    [Tooltip("Optional. If left empty, the AI will auto-find the best StrokeTrailRecorder in the scene.")]
    [SerializeField] private StrokeTrailRecorder recorder;

    [Header("Detection")]
    [Tooltip("Radius (in world units, XZ plane) within which the enemy can detect and choose stroke points.")]
    [SerializeField] private float detectionRadius = 3.0f;

    [Tooltip("How close we need to get to a stroke point to consider it 'reached' and delete it.")]
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

        // Always get history from current recorder (no caching)
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
    // FOLLOW STROKE BEHAVIOR (greedy nearest-point)
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
            // Reached this point → delete it so we never use it again.
            if (debugLogs)
                Debug.Log($"[StrokeTrailFollowerAI] Reached stroke index {_currentIndex}, deleting.", this);

            if (_currentIndex >= 0 && _currentIndex < history.Count)
            {
                history.RemoveAt(_currentIndex);
            }

            // After deletion, greedily pick the nearest point in detectionRadius.
            if (!TryAcquireStrokeTarget(history))
            {
                if (debugLogs)
                    Debug.Log("[StrokeTrailFollowerAI] No more points in radius after delete → Wander.", this);
                SwitchToWander();
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
            Debug.Log($"[StrokeTrailFollowerAI] SwitchToFollow index={index}, historyCount={count}", this);
        }
    }

    // ----------------------------------------------------------------
    // ACQUIRE TARGET – always closes point in radius
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
