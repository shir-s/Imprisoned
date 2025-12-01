// FILEPATH: Assets/Scripts/AI/Behaviors/HuntBehavior.cs
using UnityEngine;

/// <summary>
/// HUNT BEHAVIOR (relentless predator using stroke as a hint)
///
/// - The stroke is a CLUE, not a path.
/// - Enemy smells a trail near itself, infers a main direction and its opposite.
/// - It then runs like a predator in that direction, with some wandering,
///   occasionally re-sniffing the trail to refine direction.
/// - If it runs far enough in that direction with no new clues and is not near a river,
///   it may flip ONCE to the opposite direction and hunt there.
/// - When the trail is cut (e.g. by a river) it first searches for continuation
///   AROUND THE LAST CLUE POINT before flipping.
/// - It NEVER gives up by itself. Once it started hunting, it will keep hunting
///   until some higher-priority behavior (like Attack) takes over.
///
/// OnExit:
/// - Deletes the part of the stroke that was used as clues, so it won't keep
///   re-hunting the exact same stale trail.
/// </summary>
[DisallowMultipleComponent]
public class HuntBehavior : MonoBehaviour, IEnemyBehavior
{
    [System.Serializable]
    private struct DirectionSlot
    {
        public Vector3 dirXZ;       // normalized XZ direction
        public bool    initialized; // direction is valid
    }

    [Header("Behavior Priority")]
    [Tooltip("Higher than Wander, lower than Attack. e.g. Wander=0, Hunt=9, Attack=20.")]
    [SerializeField] private int priority = 9;

    [Header("Trail Detection / Re-sniffing")]
    [Tooltip("Radius around the enemy for initially detecting a stroke clue.")]
    [SerializeField] private float initialTrailDetectionRadius = 5f;

    [Tooltip("Radius around the enemy used while running to look for newer stroke clues.")]
    [SerializeField] private float resniffRadius = 6f;

    [Tooltip("How 'forward' a clue must be to count as ahead of us on the FIRST pass.\n" +
             "If no ahead clue is found, we do a second pass that ignores this filter.")]
    [Range(-1f, 1f)]
    [SerializeField] private float forwardDotThreshold = 0.1f;

    [Header("Movement")]
    [Tooltip("Base movement speed while hunting (world units/sec).")]
    [SerializeField] private float huntSpeed = 3.5f;

    [Tooltip("If true, enemy rotates to face its movement direction.")]
    [SerializeField] private bool faceMovement = true;

    [Header("Wander-style jitter")]
    [Tooltip("Strength of sideways wandering relative to forward direction.")]
    [SerializeField] private float wanderSideAmplitude = 0.4f;

    [Tooltip("How fast the sideways wandering oscillates.")]
    [SerializeField] private float wanderFrequency = 0.6f;

    [Header("Direction flipping")]
    [Tooltip("How far (world units) we are willing to run in one direction\n" +
             "SINCE THE LAST CLUE before flipping ONCE to the opposite direction,\n" +
             "if we are NOT near a river.")]
    [SerializeField] private float maxDistancePerDirection = 30f;

    [Header("Gap / cut handling")]
    [Tooltip("When we've travelled at least this distance since the last clue,\n" +
             "we run a special search around the LAST CLUE POSITION for continuation.")]
    [SerializeField] private float gapSearchTravelBeforeSearch = 2f;

    [Tooltip("Radius around the last clue position in which we search for\n" +
             "continuation samples when the trail is cut (e.g. across a river).")]
    [SerializeField] private float gapSearchRadius = 10f;

    [Header("River handling")]
    [Tooltip("Radius used to detect that we are near a RiverZone.\n" +
             "While near a river, the hunt will NOT flip direction because of\n" +
             "maxDistancePerDirection.")]
    [SerializeField] private float riverProbeRadius = 2f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool debugGizmos = false;

    private BehaviorManager _brain;

    // Hunt state
    private bool    _isActive;               // once true, CanActivate keeps returning true
    private Vector3 _huntDirectionXZ;        // current direction (XZ)
    private DirectionSlot[] _dirs = new DirectionSlot[2]; // [0] primary, [1] opposite
    private int _currentDirIndex = 0;        // 0 or 1

    private float _distanceSinceLastClue;    // distance since we last sniffed a fresh stroke point
    private float _lastClueStrokeTime;       // newest StrokeSample.time used
    private Vector3 _lastClueWorldPos;       // world position of last used stroke sample
    private bool _gapSearchDoneForCurrentClue;
    private bool _hasFlippedOnce;

    // Stroke indices used as clues (for deletion on exit)
    private int _minUsedIndex = int.MaxValue;
    private int _maxUsedIndex = -1;

    public int Priority => priority;

    private void Awake()
    {
        _brain = GetComponent<BehaviorManager>();
        if (_brain == null)
        {
            Debug.LogError("[HuntBehavior] Missing BehaviorManager on same GameObject.", this);
        }
    }

    // -----------------------------------------------------------------------
    // IEnemyBehavior
    // -----------------------------------------------------------------------

    public bool CanActivate()
    {
        if (_brain == null)
            return false;

        // Once hunting started, we NEVER voluntarily stop.
        if (_isActive)
            return true;

        StrokeHistory history = _brain.CurrentHistory;
        if (history == null || history.Count == 0)
            return false;

        // Before first activation we still need a stroke near us.
        return HasAnyStrokePointInRadius(history, initialTrailDetectionRadius);
    }

    public void OnEnter()
    {
        if (debugLogs)
            Debug.Log("[HuntBehavior] OnEnter", this);

        // If we are already active and BehaviorManager re-enters us, do nothing.
        if (_isActive)
            return;

        ResetStateInternal(keepPersistent: true);

        StrokeHistory history = _brain != null ? _brain.CurrentHistory : null;
        if (history == null || history.Count == 0)
            return;

        // Find the closest stroke point around us.
        if (!TryFindNearestStrokeIndex(history, initialTrailDetectionRadius, out int idx, out _))
        {
            if (debugLogs)
                Debug.Log("[HuntBehavior] OnEnter: no stroke point in initial radius.", this);

            return;
        }

        StrokeSample sample = history[idx];

        // Track used range for later consumption.
        _minUsedIndex = Mathf.Min(_minUsedIndex, idx);
        _maxUsedIndex = Mathf.Max(_maxUsedIndex, idx);
        _lastClueStrokeTime = sample.time;
        _lastClueWorldPos   = sample.WorldPos;
        _distanceSinceLastClue = 0f;
        _gapSearchDoneForCurrentClue = false;
        _hasFlippedOnce = false;

        // Compute main forward direction from stroke tangent.
        Vector3 forwardDir = EstimateForwardDirectionXZ(history, idx);
        if (forwardDir.sqrMagnitude < 1e-6f)
        {
            // Fallback random direction.
            Vector2 rand = Random.insideUnitCircle.normalized;
            forwardDir = new Vector3(rand.x, 0f, rand.y);
        }

        Vector3 oppositeDir = -forwardDir;

        _dirs[0] = new DirectionSlot
        {
            dirXZ = forwardDir.normalized,
            initialized = true
        };

        _dirs[1] = new DirectionSlot
        {
            dirXZ = oppositeDir.normalized,
            initialized = true
        };

        // Pick which direction to start with based on neighbor times.
        _currentDirIndex = ChooseInitialDirectionSlot(history, idx);
        _huntDirectionXZ = _dirs[_currentDirIndex].dirXZ;

        _isActive = true;

        if (debugLogs)
        {
            Debug.Log($"[HuntBehavior] Initial clue idx={idx}, time={sample.time:F3}, " +
                      $"primaryDir={_dirs[_currentDirIndex].dirXZ}, " +
                      $"secondaryDir={_dirs[1 - _currentDirIndex].dirXZ}", this);
        }
    }

    public void Tick(float deltaTime)
    {
        if (!_isActive || _brain == null)
            return;

        StrokeHistory history = _brain.CurrentHistory;

        if (history != null && history.Count > 0)
        {
            // First try normal re-sniff around current position.
            TryResniff(history);

            // If we've run a bit without new clues, search around the last clue position
            // for continuation (gap / cut handling).
            TryGapSearch(history);
        }

        // Move in current direction with wandering.
        MoveHunting(deltaTime);

        // Direction flip logic (no stopping, at most ONE flip per hunt).
        EvaluateDirectionFlip();
    }

    public void OnExit()
    {
        if (debugLogs)
            Debug.Log("[HuntBehavior] OnExit → consuming used stroke range.", this);

        StrokeHistory history = _brain != null ? _brain.CurrentHistory : null;
        if (history != null && history.Count > 0)
            ConsumeUsedStrokeRange(history);

        // Reset active flag on exit; if Attack finishes and we later re-enter Hunt,
        // we will require a new stroke nearby again.
        ResetStateInternal(keepPersistent: false);
    }

    // -----------------------------------------------------------------------
    // State helpers
    // -----------------------------------------------------------------------

    private void ResetStateInternal(bool keepPersistent)
    {
        if (!keepPersistent)
            _isActive = false;

        _huntDirectionXZ = Vector3.zero;

        for (int i = 0; i < _dirs.Length; i++)
        {
            _dirs[i].dirXZ = Vector3.zero;
            _dirs[i].initialized = false;
        }

        _currentDirIndex = 0;
        _lastClueStrokeTime = float.NegativeInfinity;
        _lastClueWorldPos = Vector3.zero;
        _distanceSinceLastClue = 0f;
        _gapSearchDoneForCurrentClue = false;
        _hasFlippedOnce = false;
        _minUsedIndex = int.MaxValue;
        _maxUsedIndex = -1;
    }

    // -----------------------------------------------------------------------
    // Movement & direction updates
    // -----------------------------------------------------------------------

    private void MoveHunting(float dt)
    {
        if (_huntDirectionXZ.sqrMagnitude < 1e-6f)
            return;

        Vector3 fwd = _huntDirectionXZ.normalized;

        // sideways wandering
        Vector3 side = new Vector3(-fwd.z, 0f, fwd.x);
        float offset = Mathf.Sin(Time.time * wanderFrequency) * wanderSideAmplitude;
        Vector3 moveDir = (fwd + side * offset).normalized;

        Vector3 pos = transform.position;
        Vector3 delta = moveDir * (huntSpeed * dt);
        Vector3 newPos = pos + delta;
        newPos.y = pos.y;

        transform.position = newPos;

        float step = delta.magnitude;
        _distanceSinceLastClue += step;

        if (faceMovement && moveDir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(moveDir, Vector3.up);
    }

    /// <summary>
    /// Re-sniff logic: two-pass search around the ENEMY.
    /// Pass 1: fresher samples ahead of current direction.
    /// Pass 2: if none ahead, any fresher sample in radius (for loops / sharp turns).
    /// </summary>
    private void TryResniff(StrokeHistory history)
    {
        int count = history.Count;
        if (count == 0)
            return;

        Vector3 selfPos = transform.position;
        Vector2 selfXZ = new Vector2(selfPos.x, selfPos.z);
        float maxDistSq = resniffRadius * resniffRadius;

        Vector2 dirXZ2 = new Vector2(_huntDirectionXZ.x, _huntDirectionXZ.z);

        int   bestIndexAhead  = -1;
        float bestTimeAhead   = _lastClueStrokeTime;
        float bestDistSqAhead = float.PositiveInfinity;

        int   bestIndexAny  = -1;
        float bestTimeAny   = _lastClueStrokeTime;
        float bestDistSqAny = float.PositiveInfinity;

        for (int i = 0; i < count; i++)
        {
            StrokeSample s = history[i];
            if (s.time <= _lastClueStrokeTime)
                continue; // only fresher samples matter

            Vector3 p = s.WorldPos;
            Vector2 pXZ = new Vector2(p.x, p.z);
            float dSq = (pXZ - selfXZ).sqrMagnitude;

            if (dSq > maxDistSq)
                continue;

            // --------- pass 2 candidate: ANY fresher sample in radius ----------
            if (s.time > bestTimeAny || (Mathf.Approximately(s.time, bestTimeAny) && dSq < bestDistSqAny))
            {
                bestTimeAny = s.time;
                bestDistSqAny = dSq;
                bestIndexAny = i;
            }

            // --------- pass 1 candidate: only if ahead ----------
            Vector2 toClue = pXZ - selfXZ;
            if (toClue.sqrMagnitude < 1e-6f)
                continue;

            toClue.Normalize();
            float dot = Vector2.Dot(toClue, dirXZ2);
            if (dot < forwardDotThreshold)
                continue;

            if (s.time > bestTimeAhead || (Mathf.Approximately(s.time, bestTimeAhead) && dSq < bestDistSqAhead))
            {
                bestTimeAhead = s.time;
                bestDistSqAhead = dSq;
                bestIndexAhead = i;
            }
        }

        int chosenIndex = bestIndexAhead >= 0 ? bestIndexAhead : bestIndexAny;
        if (chosenIndex < 0)
            return;

        UseNewClue(history, chosenIndex, fromGapSearch:false);
    }

    /// <summary>
    /// Gap / cut handling:
    /// if we've moved some distance since the last clue and haven't yet done
    /// a local gap search for that clue, search around the LAST CLUE POSITION
    /// for continuation samples (e.g. other side of a river).
    /// </summary>
    private void TryGapSearch(StrokeHistory history)
    {
        if (_gapSearchDoneForCurrentClue)
            return;

        if (_lastClueStrokeTime <= float.NegativeInfinity)
            return;

        if (_distanceSinceLastClue < gapSearchTravelBeforeSearch)
            return;

        int count = history.Count;
        if (count == 0)
            return;

        Vector2 clueXZ = new Vector2(_lastClueWorldPos.x, _lastClueWorldPos.z);
        float maxDistSq = gapSearchRadius * gapSearchRadius;

        int   bestIndex  = -1;
        float bestTime   = _lastClueStrokeTime;
        float bestDistSq = float.PositiveInfinity;

        for (int i = 0; i < count; i++)
        {
            StrokeSample s = history[i];
            if (s.time <= _lastClueStrokeTime)
                continue; // need newer than the last clue

            Vector3 p = s.WorldPos;
            Vector2 pXZ = new Vector2(p.x, p.z);
            float dSq = (pXZ - clueXZ).sqrMagnitude;

            if (dSq > maxDistSq)
                continue;

            // prefer newest; tie-breaker: closer
            if (s.time > bestTime || (Mathf.Approximately(s.time, bestTime) && dSq < bestDistSq))
            {
                bestTime = s.time;
                bestDistSq = dSq;
                bestIndex = i;
            }
        }

        _gapSearchDoneForCurrentClue = true; // only try once per clue

        if (bestIndex < 0)
            return;

        UseNewClue(history, bestIndex, fromGapSearch:true);
    }

    /// <summary>
    /// Apply a newly chosen stroke sample as a clue: update direction, timers and
    /// bookkeeping (used indices, last clue pos/time, reset gap-search flag).
    /// </summary>
    private void UseNewClue(StrokeHistory history, int index, bool fromGapSearch)
    {
        StrokeSample s = history[index];

        _lastClueStrokeTime = s.time;
        _lastClueWorldPos   = s.WorldPos;
        _minUsedIndex = Mathf.Min(_minUsedIndex, index);
        _maxUsedIndex = Mathf.Max(_maxUsedIndex, index);
        _distanceSinceLastClue = 0f;
        _gapSearchDoneForCurrentClue = false; // we can run gap search again after this new clue

        Vector3 newDir = EstimateForwardDirectionXZ(history, index);
        if (newDir.sqrMagnitude > 1e-6f)
        {
            Vector3 oldDir = _huntDirectionXZ.sqrMagnitude > 1e-6f
                ? _huntDirectionXZ.normalized
                : newDir.normalized;

            float angle = Vector3.Angle(oldDir, newDir);
            // Stronger snap on big turns (loops / sharp changes).
            float turnStrength = angle > 90f ? 0.9f : 0.45f;

            Vector3 blended = Vector3.Slerp(oldDir, newDir.normalized, turnStrength);
            blended.y = 0f;
            _huntDirectionXZ = blended.normalized;
        }

        if (debugLogs)
        {
            string src = fromGapSearch ? "GAP" : "RESNIFF";
            Debug.Log($"[HuntBehavior] {src} clue idx={index}, time={s.time:F3}, newDir={_huntDirectionXZ}", this);
        }
    }

    /// <summary>
    /// If we ran a long distance since the last clue, and we are not near a
    /// river, flip ONCE to the opposite direction slot (0 ↔ 1). This makes the
    /// hunter patrol back and forth along the predicted line instead of giving up.
    /// </summary>
    private void EvaluateDirectionFlip()
    {
        if (_hasFlippedOnce)
            return; // only one flip per hunt → no ping-pong

        if (_distanceSinceLastClue < maxDistancePerDirection)
            return;

        if (IsNearRiver(transform.position))
            return;

        int other = 1 - _currentDirIndex;
        if (other >= 0 && other < _dirs.Length && _dirs[other].initialized)
        {
            _currentDirIndex = other;
            _huntDirectionXZ = _dirs[_currentDirIndex].dirXZ;
            _distanceSinceLastClue = 0f;
            _hasFlippedOnce = true;

            if (debugLogs)
            {
                Debug.Log($"[HuntBehavior] Flipping hunt direction ONCE to {_huntDirectionXZ}", this);
            }
        }
        else
        {
            _distanceSinceLastClue = 0f;
        }
    }

    // -----------------------------------------------------------------------
    // Stroke helpers
    // -----------------------------------------------------------------------

    private bool HasAnyStrokePointInRadius(StrokeHistory history, float radius)
    {
        int count = history.Count;
        if (count == 0)
            return false;

        Vector3 pos = transform.position;
        Vector2 selfXZ = new Vector2(pos.x, pos.z);
        float maxDistSq = radius * radius;

        for (int i = 0; i < count; i++)
        {
            Vector3 p = history[i].WorldPos;
            Vector2 pXZ = new Vector2(p.x, p.z);
            if ((pXZ - selfXZ).sqrMagnitude <= maxDistSq)
                return true;
        }

        return false;
    }

    private bool TryFindNearestStrokeIndex(StrokeHistory history, float radius,
                                           out int bestIndex, out Vector3 bestPosWS)
    {
        int count = history.Count;
        bestIndex = -1;
        bestPosWS = Vector3.zero;

        if (count == 0)
            return false;

        Vector3 pos = transform.position;
        Vector2 selfXZ = new Vector2(pos.x, pos.z);
        float maxDistSq = radius * radius;

        float bestDistSq = float.PositiveInfinity;

        for (int i = 0; i < count; i++)
        {
            Vector3 p = history[i].WorldPos;
            Vector2 pXZ = new Vector2(p.x, p.z);
            float dSq = (pXZ - selfXZ).sqrMagnitude;
            if (dSq <= maxDistSq && dSq < bestDistSq)
            {
                bestDistSq = dSq;
                bestIndex = i;
                bestPosWS = p;
            }
        }

        return bestIndex >= 0;
    }

    /// <summary>
    /// Estimate forward direction in XZ from the local stroke tangent at index.
    /// Uses neighbor samples in StrokeHistory (higher index ~ newer sample).
    /// </summary>
    private Vector3 EstimateForwardDirectionXZ(StrokeHistory history, int index)
    {
        int count = history.Count;
        if (count == 0)
            return Vector3.zero;

        int forwardIdx = index + 1;
        int backwardIdx = index - 1;

        Vector3 dir = Vector3.zero;

        if (forwardIdx >= 0 && forwardIdx < count)
        {
            Vector3 p0 = history[index].WorldPos;
            Vector3 p1 = history[forwardIdx].WorldPos;
            dir = p1 - p0;
        }
        else if (backwardIdx >= 0 && backwardIdx < count)
        {
            Vector3 p0 = history[backwardIdx].WorldPos;
            Vector3 p1 = history[index].WorldPos;
            dir = p1 - p0;
        }

        dir.y = 0f;
        return dir.normalized;
    }

    /// <summary>
    /// Decide which direction slot (0/1) to start from, based on stroke times near the entry index.
    /// </summary>
    private int ChooseInitialDirectionSlot(StrokeHistory history, int index)
    {
        int count = history.Count;
        if (count == 0)
            return 0;

        float timeForward = float.NegativeInfinity;
        float timeBackward = float.NegativeInfinity;

        int fIdx = index + 1;
        int bIdx = index - 1;

        if (fIdx >= 0 && fIdx < count)
            timeForward = history[fIdx].time;

        if (bIdx >= 0 && bIdx < count)
            timeBackward = history[bIdx].time;

        // If we can tell which neighbor is newer, start in that direction.
        if (timeForward > timeBackward)
            return 0; // forwardDir is slot 0
        if (timeBackward > timeForward)
            return 1; // oppositeDir is slot 1

        // If inconclusive, random choice for variety.
        return Random.value < 0.5f ? 0 : 1;
    }

    /// <summary>
    /// On exit: consume the stroke indices we used as clues, so this local
    /// trail won't trigger Hunt again.
    /// </summary>
    private void ConsumeUsedStrokeRange(StrokeHistory history)
    {
        if (_minUsedIndex > _maxUsedIndex)
            return;

        int count = history.Count;
        if (count == 0)
            return;

        int start = Mathf.Clamp(_minUsedIndex, 0, count - 1);
        int end   = Mathf.Clamp(_maxUsedIndex, 0, count - 1);

        if (start > end)
            return;

        if (debugLogs)
        {
            Debug.Log($"[HuntBehavior] ConsumeUsedStrokeRange [{start} .. {end}] (count={count})", this);
        }

        history.ConsumeRange(start, end);
    }

    // -----------------------------------------------------------------------
    // River helpers
    // -----------------------------------------------------------------------

    private bool IsNearRiver(Vector3 worldPos)
    {
        Vector3 center = worldPos + Vector3.up * 0.2f;
        Collider[] hits = Physics.OverlapSphere(center, riverProbeRadius, ~0, QueryTriggerInteraction.Collide);

        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i] == null) continue;
            var river = hits[i].GetComponentInParent<RiverZone>();
            if (river != null)
                return true;
        }

        return false;
    }

    // -----------------------------------------------------------------------
    // Gizmos
    // -----------------------------------------------------------------------

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!debugGizmos)
            return;

        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, initialTrailDetectionRadius);

        Gizmos.color = new Color(0.9f, 0.6f, 0.1f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, resniffRadius);

        Gizmos.color = new Color(0.2f, 0.4f, 1f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, riverProbeRadius);

        Gizmos.color = new Color(0.6f, 0.2f, 1f, 0.25f);
        Gizmos.DrawWireSphere(_lastClueWorldPos, gapSearchRadius);

        if (_huntDirectionXZ.sqrMagnitude > 0.001f)
        {
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.7f);
            Vector3 start = transform.position;
            Vector3 end = start + _huntDirectionXZ.normalized * 4f;
            Gizmos.DrawLine(start, end);
            Gizmos.DrawWireSphere(end, 0.15f);
        }
    }
#endif
}
