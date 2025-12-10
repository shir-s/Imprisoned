// FILEPATH: Assets/Scripts/AI/StrokeTrailFollowerAI.cs

using JellyGame.GamePlay.Managers;
using JellyGame.GamePlay.Painting.Trails.Collision;
using UnityEngine;

namespace JellyGame.GamePlay.Enemy.AI
{
    /// <summary>
    /// AI "brain" that manages a set of behaviors (IEnemyBehavior / ICharacterBehavior).
    /// 
    /// Responsibilities:
    /// - Owns the stroke context (recorder + auto-bind logic).
    /// - Each frame:
    ///     * Optionally re-binds to the StrokeTrailRecorder with the most points.
    ///     * Queries all attached behaviors for CanActivate().
    ///     * Picks the behavior with the highest Priority that can activate.
    ///     * If a higher-priority behavior wants control, switches to it.
    ///     * Calls Tick() on the current behavior.
    /// 
    /// Notes:
    /// - Behaviors are separate components that implement IEnemyBehavior.
    /// - Typical setup on the enemy GameObject:
    ///     * BehaviorManager        (this brain)
    ///     * WanderBehavior
    ///     * FollowStrokeBehavior
    /// 
    /// Extra:
    /// - If isFriendlyNpc is true and this object is destroyed in play mode,
    ///   it will trigger the FriendlyNpcKilled event.
    /// </summary>
    [DisallowMultipleComponent]
    public class BehaviorManager : MonoBehaviour
    {
        [Header("Stroke Source")]
        [Tooltip("Optional. If left empty, the AI will auto-find the best StrokeTrailRecorder in the scene.")]
        [SerializeField] private StrokeTrailRecorder recorder;

        [Header("Auto binding")]
        [Tooltip("If true, the AI will keep trying to re-bind to the StrokeTrailRecorder that has the most points in its history.")]
        [SerializeField] private bool autoBindToBestRecorder = true;

        [Header("NPC Type")]
        [Tooltip("If true, this brain is on a friendly NPC. When this NPC is destroyed in play mode, a FriendlyNpcKilled event will be fired.")]
        [SerializeField] private bool isFriendlyNpc = false;

        [Header("Debug")]
        [Tooltip("Verbose logging for all behavior manager operations.")]
        [SerializeField] private bool debugLogs = false;

        [Tooltip("If true, logs once each time the active behavior changes (less verbose than debugLogs).")]
        [SerializeField] private bool logBehaviorSwitch = true;

        // Cached behaviors (same GameObject)
        private IEnemyBehavior[] _behaviors;
        private IEnemyBehavior _currentBehavior;

        // ----------------- PUBLIC CONTEXT -----------------

        /// <summary>Current stroke recorder (may be null if none found).</summary>
        public StrokeTrailRecorder Recorder => recorder;

        /// <summary>Shortcut to Recorder.History (may be null if recorder is null).</summary>
        public StrokeHistory CurrentHistory => recorder != null ? recorder.History : null;

        /// <summary>The currently active behavior (may be null).</summary>
        public IEnemyBehavior CurrentBehavior => _currentBehavior;

        /// <summary>Name of the currently active behavior, or "None" if no behavior is active.</summary>
        public string CurrentBehaviorName => _currentBehavior != null ? _currentBehavior.GetType().Name : "None";

        private void Awake()
        {
            // Find all behaviors on this GameObject
            _behaviors = GetComponents<IEnemyBehavior>();
            if (debugLogs)
            {
                Debug.Log($"[BehaviorManager] Found {_behaviors.Length} IEnemyBehavior components on {name}.", this);
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            if (autoBindToBestRecorder)
            {
                RefreshRecorderBinding();
            }

            // 1) If there is an active behavior but it no longer wants to run, exit it.
            if (_currentBehavior != null && !_currentBehavior.CanActivate())
            {
                if (debugLogs)
                {
                    Debug.Log($"[BehaviorManager] Current behavior '{_currentBehavior.GetType().Name}' no longer CanActivate() → OnExit().", this);
                }

                string exitingName = _currentBehavior.GetType().Name;
                _currentBehavior.OnExit();
                _currentBehavior = null;

                if (logBehaviorSwitch)
                {
                    Debug.Log($"[{name}] Behavior: {exitingName} → None", this);
                }
            }

            // 2) Find the best behavior that *can* activate right now.
            IEnemyBehavior best = null;
            int bestPriority = int.MinValue;

            for (int i = 0; i < _behaviors.Length; i++)
            {
                IEnemyBehavior b = _behaviors[i];
                if (b == null)
                    continue;

                // If this behavior is also a MonoBehaviour, respect enabled/active flags
                if (b is Behaviour mb && !mb.isActiveAndEnabled)
                    continue;

                if (!b.CanActivate())
                    continue;

                int p = b.Priority;
                if (p > bestPriority)
                {
                    bestPriority = p;
                    best = b;
                }
            }

            // 3) Decide whether to switch behaviors.
            if (best != null)
            {
                if (_currentBehavior == null)
                {
                    // No current → just take the best.
                    _currentBehavior = best;
                
                    if (debugLogs)
                    {
                        Debug.Log($"[BehaviorManager] No current behavior → OnEnter '{_currentBehavior.GetType().Name}'.", this);
                    }

                    if (logBehaviorSwitch)
                    {
                        Debug.Log($"[{name}] Behavior: None → {_currentBehavior.GetType().Name} (P={_currentBehavior.Priority})", this);
                    }

                    _currentBehavior.OnEnter();
                }
                else if (!ReferenceEquals(best, _currentBehavior))
                {
                    // There is a current behavior; only switch if new one has strictly higher priority.
                    if (best.Priority > _currentBehavior.Priority)
                    {
                        string oldName = _currentBehavior.GetType().Name;
                        int oldPriority = _currentBehavior.Priority;

                        if (debugLogs)
                        {
                            Debug.Log(
                                $"[BehaviorManager] Switching behavior '{oldName}'(P={oldPriority}) " +
                                $"→ '{best.GetType().Name}'(P={best.Priority}).", this);
                        }

                        _currentBehavior.OnExit();
                        _currentBehavior = best;
                        _currentBehavior.OnEnter();

                        if (logBehaviorSwitch)
                        {
                            Debug.Log($"[{name}] Behavior: {oldName} (P={oldPriority}) → {_currentBehavior.GetType().Name} (P={_currentBehavior.Priority})", this);
                        }
                    }
                    // If best.Priority <= current.Priority, stay on current behavior.
                }
                // else: best == current, keep running it.
            }
            else
            {
                // No behavior wants control right now.
                if (_currentBehavior != null)
                {
                    string exitingName = _currentBehavior.GetType().Name;

                    if (debugLogs)
                    {
                        Debug.Log("[BehaviorManager] No behaviors CanActivate() → exiting current behavior.", this);
                    }

                    _currentBehavior.OnExit();
                    _currentBehavior = null;

                    if (logBehaviorSwitch)
                    {
                        Debug.Log($"[{name}] Behavior: {exitingName} → None (no behaviors can activate)", this);
                    }
                }
            }

            // 4) Tick the active behavior (if any)
            if (_currentBehavior != null)
            {
                _currentBehavior.Tick(dt);
            }
        }

        private void OnDestroy()
        {
            // Only fire gameplay events when actually playing, not when stopping the editor, recompiling, etc.
            if (!Application.isPlaying)
                return;

            if (isFriendlyNpc)
            {
                if (debugLogs)
                {
                    Debug.Log("[BehaviorManager] Friendly NPC destroyed → triggering FriendlyNpcKilled event.", this);
                }

                // Send this NPC (or its GameObject) as event data so listeners know which one died.
                EventManager.TriggerEvent(EventManager.GameEvent.FriendlyNpcKilled, gameObject);
            }
        }

        // ----------------------------------------------------------------
        // Recorder binding / re-binding
        // ----------------------------------------------------------------

        private void RefreshRecorderBinding()
        {
            bool needSearch =
                recorder == null ||
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
                    Debug.Log("[BehaviorManager] Bound to recorder: " + recorder.name +
                              " (HistoryCount=" + bestCount + ")", this);
                }
            }
            else if (debugLogs)
            {
                if (all.Length == 0)
                {
                    Debug.Log("[BehaviorManager] No StrokeTrailRecorder found in scene.", this);
                }
                /*else
                {
                    Debug.Log("[BehaviorManager] Found " + all.Length +
                              " StrokeTrailRecorder(s), but none have points yet.", this);
                }*/
            }
        }
    }
}