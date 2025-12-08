// FILEPATH: Assets/Scripts/AI/FollowStrokeBehavior.cs

using System.Collections.Generic;
using UnityEngine;

namespace JellyGame.GamePlay.Enemy.AI.Behaviors
{
    /// <summary>
    /// Behavior that follows the cube's stroke trail.
    ///
    /// Modes:
    /// - ConsumePoints:
    ///     * Enemy always goes to the closest stroke point in detectionRadius.
    ///     * When it reaches a point, it deletes it from StrokeHistory (ConsumeUpTo/RemoveAt-style behavior).
    ///     * Never comes back to the same part of the trail.
    ///
    /// - PersistentTrail:
    ///     * Points are NOT deleted while exploring.
    ///     * Enemy remembers which indices it actually reached during THIS activation (session).
    ///     * Uses visit counts + a branch stack to explore both “directions” of the trail.
    ///     * When exploration ends (no unvisited points in radius OR in branches),
    ///       it deletes the explored chunk from StrokeHistory in one go:
    ///       - Find minVisitedIndex & maxVisitedIndex from this session.
    ///       - Call history.ConsumeRange(minVisitedIndex, maxVisitedIndex).
    ///     * If this behavior is interrupted by a higher-priority behavior (e.g. Attack),
    ///       it does NOT delete anything from history; it just forgets temp session data.
    /// </summary>
    [DisallowMultipleComponent]
    public class FollowStrokeBehavior : MonoBehaviour, IEnemyBehavior, IEnemySound
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
        [Tooltip("ConsumePoints: deletes points as it goes.\nPersistentTrail: keeps points while exploring, then deletes explored chunk at the end.")]
        [SerializeField] private FollowMode followMode = FollowMode.ConsumePoints;

        [Header("Detection")]
        [Tooltip("Radius (in world units, XZ plane) within which the enemy can detect and choose stroke points.")]
        [SerializeField] private float detectionRadius = 3.0f;

        [Tooltip("How close we need to get to a stroke point to consider it 'reached'.")]
        [SerializeField] private float reachThreshold = 0.05f;

        [Header("Movement")]
        [Tooltip("Speed while following the stroke (world units/sec).")]
        [SerializeField] private float followSpeed = 2.0f;

        private float _speedMultiplier = 1f;

        [Header("Sound")]
        [Tooltip("If disabled, this behavior will not produce any sound.")]
        [SerializeField] private bool enableSound = true;

        [Tooltip("How the sound for this behavior should be played.\nNone = no sound even if enableSound is true.")]
        [SerializeField] private SoundPlaybackMode soundMode = SoundPlaybackMode.RandomInterval;

        [Tooltip("Base interval in seconds (used for FixedInterval and as MIN for RandomInterval).")]
        [SerializeField] private float soundInterval = 2f;

        [Tooltip("MAX interval (seconds) for RandomInterval mode. Ignored for other modes.")]
        [SerializeField] private float maxRandomInterval = 4f;

        [Tooltip("Name of the sound to play for this behavior (must exist in AudioSettings).")]
        [SerializeField] private string soundName = "EnemyFollowStroke";

        [Tooltip("If true, use custom volume instead of default from AudioSettings.")]
        [SerializeField] private bool useCustomVolume = false;

        [Tooltip("Custom volume (0..1) when useCustomVolume is enabled.")]
        [Range(0f, 1f)]
        [SerializeField] private float soundVolume = 1f;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;
        [SerializeField] private bool debugGizmos = false;

        private BehaviorManager _brain;

        // Index of the stroke point we are currently heading to
        private int _currentIndex = -1;

        // Visit tracking inside a single activation (session)
        private readonly Dictionary<int, int> _visitCounts = new Dictionary<int, int>();
        private readonly HashSet<int> _visitedThisSession = new HashSet<int>();
        private int _lastIndexPersistent = -1;

        // Branch memory (PersistentTrail)
        private readonly Stack<int> _branchStack = new Stack<int>();
        private readonly HashSet<int> _knownBranchIndices = new HashSet<int>();

        public int Priority => priority;

        /// <summary>
        /// Sets a speed multiplier for this behavior (used by SlowZone).
        /// 1.0 = normal speed, 0.5 = half speed, etc.
        /// </summary>
        public void SetSpeedMultiplier(float multiplier)
        {
            _speedMultiplier = Mathf.Max(0f, multiplier);
        }

        private void Awake()
        {
            _brain = GetComponent<BehaviorManager>();
            if (_brain == null)
            {
                Debug.LogError("[FollowStrokeBehavior] Missing BehaviorManager on the same GameObject.", this);
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

            // Explored parts are physically removed from history (via ConsumeUpTo / ConsumeRange),
            // so we never re-activate on already explored trails.
            return HasAnyPointInRadius(history);
        }

        public void OnEnter()
        {
            _currentIndex = -1;
            _visitCounts.Clear();
            _visitedThisSession.Clear();
            _lastIndexPersistent = -1;
            _branchStack.Clear();
            _knownBranchIndices.Clear();

            if (debugLogs)
            {
                Debug.Log("[FollowStrokeBehavior] OnEnter (mode=" + followMode + ")", this);
            }

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
                _currentIndex = -1;
                return;
            }

            TickFollowStroke(deltaTime, history);
        }

        public void OnExit()
        {
            if (debugLogs)
            {
                Debug.Log("[FollowStrokeBehavior] OnExit", this);
            }

            // If exploration finished normally, we already removed explored part in CleanupExploredHistory().
            // If a higher-priority behavior (e.g. Attack) interrupted us, we keep history and just clear session state.

            _currentIndex = -1;
            _visitCounts.Clear();
            _visitedThisSession.Clear();
            _lastIndexPersistent = -1;
            _branchStack.Clear();
            _knownBranchIndices.Clear();
        }

        // --------------------------------------------------------
        // Core follow logic
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
                    Debug.Log("[FollowStrokeBehavior] Current index out of range. Re-acquiring target. Count=" + count, this);
                }

                if (!TryAcquireStrokeTarget(history))
                {
                    _currentIndex = -1;
                    return;
                }
            }

            StrokeSample sample = history[_currentIndex];
            Vector3 targetWS = sample.WorldPos;
            Vector3 pos = transform.position;

            Vector3 flatTarget = new Vector3(targetWS.x, pos.y, targetWS.z);
            Vector3 toTarget = flatTarget - pos;
            float dist = toTarget.magnitude;

            if (dist <= reachThreshold)
            {
                if (followMode == FollowMode.ConsumePoints)
                {
                    HandleReachedPoint_Consume(history);
                }
                else
                {
                    HandleReachedPoint_Persistent(history);
                }
                return;
            }

            if (dist > 1e-5f)
            {
                Vector3 dir = toTarget / dist;
                pos += dir * (followSpeed * _speedMultiplier * dt);
                transform.position = pos;
            }
        }

        // ----------------- ConsumePoints mode -----------------

        private void HandleReachedPoint_Consume(StrokeHistory history)
        {
            if (debugLogs)
                Debug.Log($"[FollowStrokeBehavior] (Consume) Reached stroke index {_currentIndex}, deleting via ConsumeUpTo.", this);

            if (_currentIndex >= 0 && _currentIndex < history.Count)
            {
                history.ConsumeUpTo(_currentIndex);
            }

            if (!TryAcquireStrokeTarget(history))
            {
                if (debugLogs)
                    Debug.Log("[FollowStrokeBehavior] (Consume) No more points in radius after deletion.", this);

                _currentIndex = -1;
            }
        }

        // ----------------- PersistentTrail mode -----------------

        private void HandleReachedPoint_Persistent(StrokeHistory history)
        {
            if (debugLogs)
                Debug.Log($"[FollowStrokeBehavior] (Persistent) Reached stroke index {_currentIndex}", this);

            RegisterVisit(_currentIndex);

            if (!ChooseNextPersistentTarget(history))
            {
                // No unvisited points left (in radius or branches) → exploration finished.
                if (debugLogs)
                    Debug.Log("[FollowStrokeBehavior] (Persistent) Exploration finished – deleting explored segment from history.", this);

                CleanupExploredHistory(history);
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
            _visitedThisSession.Add(index);
            _lastIndexPersistent = index;
        }

        /// <summary>
        /// Delete the explored segment from StrokeHistory once we are done exploring
        /// (no unvisited points left in radius or in branch stack).
        ///
        /// Strategy:
        /// - Find minVisitedIndex and maxVisitedIndex from this session.
        /// - Call history.ConsumeRange(minVisitedIndex, maxVisitedIndex).
        /// </summary>
        private void CleanupExploredHistory(StrokeHistory history)
        {
            if (_visitedThisSession.Count == 0)
                return;

            int minVisited = int.MaxValue;
            int maxVisited = -1;

            foreach (int idx in _visitedThisSession)
            {
                if (idx < minVisited) minVisited = idx;
                if (idx > maxVisited) maxVisited = idx;
            }

            if (minVisited == int.MaxValue || maxVisited < 0)
                return;

            // Clamp to current history size in case something pruned while exploring.
            if (minVisited >= history.Count)
                return;

            if (maxVisited >= history.Count)
                maxVisited = history.Count - 1;

            if (minVisited > maxVisited)
                return;

            history.ConsumeRange(minVisited, maxVisited);

            _visitCounts.Clear();
            _visitedThisSession.Clear();
            _branchStack.Clear();
            _knownBranchIndices.Clear();
            _lastIndexPersistent = -1;
        }

        /// <summary>
        /// Branch-aware selection of the next index in PersistentTrail mode.
        /// Stops when there are no UNVISITED points left (neither in radius nor in branches).
        /// </summary>
        private bool ChooseNextPersistentTarget(StrokeHistory history)
        {
            int count = history.Count;
            if (count == 0)
                return false;

            Vector3 pos = transform.position;
            Vector2 enemyXZ = new Vector2(pos.x, pos.z);
            float maxDistSq = detectionRadius * detectionRadius;

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

            // If no candidates in radius, see if we have some unvisited branch to backtrack to.
            if (candidates.Count == 0)
            {
                if (TryPopValidBranchIndex(history, out int branchIndex))
                {
                    _currentIndex = branchIndex;

                    if (debugLogs)
                    {
                        Debug.Log($"[FollowStrokeBehavior] (Persistent) Dead-end, backtracking to saved branch index={branchIndex}.", this);
                    }

                    return true;
                }

                // No candidates and no unvisited branches → DONE.
                return false;
            }

            // Check if there is ANY unvisited point reachable (in radius OR in branch stack).
            bool hasUnvisitedCandidate = false;
            foreach (int idx in candidates)
            {
                int v = _visitCounts.TryGetValue(idx, out int vc) ? vc : 0;
                if (v == 0)
                {
                    hasUnvisitedCandidate = true;
                    break;
                }
            }

            bool hasUnvisitedBranch = HasAnyUnvisitedBranch(history);

            // If there is no unvisited candidate AND no unvisited branch,
            // then every reachable direction has been explored at least once.
            if (!hasUnvisitedCandidate && !hasUnvisitedBranch)
            {
                if (debugLogs)
                {
                    Debug.Log("[FollowStrokeBehavior] (Persistent) No unvisited candidates or branches left. Exploration complete.", this);
                }
                return false;
            }

            // From here we know that either:
            // - there is some unvisited candidate in radius, OR
            // - there is an unvisited branch somewhere further away (we might need to walk back over visited points to get there).

            // Among candidates, find minimal visit count.
            int minVisits = int.MaxValue;
            foreach (int idx in candidates)
            {
                int v = _visitCounts.TryGetValue(idx, out int vc) ? vc : 0;
                if (v < minVisits)
                    minVisits = v;
            }

            List<int> bestByVisits = new List<int>();
            foreach (int idx in candidates)
            {
                int v = _visitCounts.TryGetValue(idx, out int vc) ? vc : 0;
                if (v == minVisits)
                    bestByVisits.Add(idx);
            }

            // Avoid immediately going back to the last index if possible.
            List<int> filtered = new List<int>();
            foreach (int idx in bestByVisits)
            {
                if (idx != _lastIndexPersistent)
                    filtered.Add(idx);
            }

            List<int> finalCandidates = filtered.Count > 0 ? filtered : bestByVisits;

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
            {
                // Weird situation: fall back to an unvisited branch if we have one.
                if (TryPopValidBranchIndex(history, out int branchIndex))
                {
                    _currentIndex = branchIndex;

                    if (debugLogs)
                    {
                        Debug.Log($"[FollowStrokeBehavior] (Persistent) No suitable candidate, backtracking to branch index={branchIndex}.", this);
                    }

                    return true;
                }

                return false;
            }

            // Save alternatives as potential branches (only unvisited ones).
            foreach (int idx in finalCandidates)
            {
                if (idx == bestIndex)
                    continue;

                bool alreadyVisited = _visitCounts.TryGetValue(idx, out int v) && v > 0;
                if (alreadyVisited)
                    continue;

                if (_knownBranchIndices.Add(idx))
                {
                    _branchStack.Push(idx);

                    if (debugLogs)
                    {
                        Debug.Log($"[FollowStrokeBehavior] (Persistent) Saved alternative branch index={idx} for later.", this);
                    }
                }
            }

            _currentIndex = bestIndex;

            if (debugLogs)
            {
                int v = _visitCounts.TryGetValue(bestIndex, out int vc) ? vc : 0;
                Debug.Log($"[FollowStrokeBehavior] (Persistent) Next index={bestIndex}, dist={Mathf.Sqrt(bestDistSq):F3}, visits={v}", this);
            }

            return true;
        }

        /// <summary>
        /// Returns true if there exists at least one UNVISITED branch index still valid in history.
        /// </summary>
        private bool HasAnyUnvisitedBranch(StrokeHistory history)
        {
            int count = history.Count;
            if (count == 0 || _branchStack.Count == 0)
                return false;

            foreach (int idx in _branchStack)
            {
                if (idx < 0 || idx >= count)
                    continue;

                int v = _visitCounts.TryGetValue(idx, out int vc) ? vc : 0;
                if (v == 0)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Pops indices from the branch stack until it finds one that:
        /// - is still valid in history, AND
        /// - has never been visited (visit count == 0).
        /// Returns true if such an index was found.
        /// </summary>
        private bool TryPopValidBranchIndex(StrokeHistory history, out int branchIndex)
        {
            int count = history.Count;

            while (_branchStack.Count > 0)
            {
                int idx = _branchStack.Pop();

                if (idx < 0 || idx >= count)
                    continue;

                int visits = _visitCounts.TryGetValue(idx, out int v) ? v : 0;
                if (visits > 0)
                    continue; // already explored

                branchIndex = idx;
                return true;
            }

            branchIndex = -1;
            return false;
        }

        // ----------------------------------------------------------------
        // Helpers
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

        private bool TryAcquireStrokeTarget(StrokeHistory history)
        {
            if (history == null)
            {
                if (debugLogs)
                    Debug.Log("[FollowStrokeBehavior] TryAcquireStrokeTarget: history is null.", this);
                return false;
            }

            int count = history.Count;
            if (count == 0)
            {
                if (debugLogs)
                    Debug.Log("[FollowStrokeBehavior] TryAcquireStrokeTarget: history empty.", this);
                return false;
            }

            Vector3 pos = transform.position;
            Vector2 enemyXZ = new Vector2(pos.x, pos.z);
            float maxDistSq = detectionRadius * detectionRadius;

            int bestIndex = -1;
            float bestDistSq = float.PositiveInfinity;
            int bestVisits = int.MaxValue;

            for (int i = 0; i < count; i++)
            {
                Vector3 p = history[i].WorldPos;
                Vector2 pXZ = new Vector2(p.x, p.z);
                float dSq = (pXZ - enemyXZ).sqrMagnitude;

                if (dSq > maxDistSq)
                    continue;

                if (followMode == FollowMode.ConsumePoints)
                {
                    if (dSq < bestDistSq)
                    {
                        bestDistSq = dSq;
                        bestIndex = i;
                    }
                }
                else
                {
                    int visits = _visitCounts.TryGetValue(i, out int v) ? v : 0;

                    if (visits < bestVisits || (visits == bestVisits && dSq < bestDistSq))
                    {
                        bestVisits = visits;
                        bestDistSq = dSq;
                        bestIndex = i;
                    }
                }
            }

            if (debugLogs)
            {
                if (bestIndex < 0)
                {
                    Debug.Log("[FollowStrokeBehavior] TryAcquireStrokeTarget: no point in radius. " +
                              $"HistoryCount={count}, detectionRadius={detectionRadius}", this);
                }
                else
                {
                    Debug.Log("[FollowStrokeBehavior] TryAcquireStrokeTarget: found index=" + bestIndex +
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

        // --------------------------------------------------------
        // IEnemySound
        // --------------------------------------------------------

        /// <summary>
        /// How should sound be played while this behavior is active?
        /// If enableSound is false, returns None to completely mute this behavior.
        /// </summary>
        public SoundPlaybackMode GetSoundMode()
        {
            if (!enableSound)
                return SoundPlaybackMode.None;

            return soundMode;
        }

        /// <summary>
        /// Base interval for FixedInterval and MIN interval for RandomInterval.
        /// </summary>
        public float GetSoundInterval()
        {
            return soundInterval;
        }

        /// <summary>
        /// MAX interval for RandomInterval mode.
        /// </summary>
        public float GetMaxSoundInterval()
        {
            return maxRandomInterval;
        }

        /// <summary>
        /// Name of the sound to play. If sound is disabled, returns null.
        /// </summary>
        public string GetSoundName()
        {
            if (!enableSound)
                return null;

            return soundName;
        }

        /// <summary>
        /// Optional custom volume for this behavior.
        /// </summary>
        public float GetSoundVolume()
        {
            if (!enableSound)
                return -1f;

            return useCustomVolume ? soundVolume : -1f;
        }

        /// <summary>
        /// Called every time before a sound is played.
        /// Uses enableSound so you can flip it at runtime in the inspector.
        /// </summary>
        public bool ShouldPlaySound()
        {
            return enableSound;
        }
    }
}
