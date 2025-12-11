// FILEPATH: Assets/Scripts/AI/Behaviors/AttackBehavior.cs

using System;
using UnityEngine;
using JellyGame.GamePlay.Enemy.AI.Movement;

namespace JellyGame.GamePlay.Enemy.AI.Behaviors
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SteeringNavigator))]
    public class AttackBehavior : MonoBehaviour, IEnemyBehavior, IEnemySound
    {
        [Serializable]
        public struct TargetLayerPriority
        {
            public SingleLayer layer;
            public int attackPriority;
            public float customDetectionRadius;
            public float customAttackRadius;
        }

        [Serializable]
        public struct SingleLayer
        {
            [SerializeField] private int layerIndex;
            public int LayerIndex => layerIndex;
            public int LayerMask => layerIndex >= 0 ? (1 << layerIndex) : 0;
            public static implicit operator int(SingleLayer layer) => layer.layerIndex;
        }

        [Header("Behavior Priority")]
        [SerializeField] private int priority = 20;

        [Header("Target Layers & Priorities")]
        [SerializeField] private TargetLayerPriority[] targetPriorities;

        [Header("Detection")]
        [SerializeField] private float detectionRadius = 15f;
        [SerializeField] private float attackRadius = 2f;
        
        // --- NEW SECTION ---
        [Header("Navigation Override")]
        [Tooltip("What layers count as obstacles WHILE ATTACKING? Uncheck 'Traps' here to walk through them.")]
        [SerializeField] private LayerMask attackObstacleLayers;
        // -------------------

        [Header("Sound")]
        [SerializeField] private bool enableSound = true;
        [SerializeField] private SoundPlaybackMode soundMode = SoundPlaybackMode.OnEnterLoop;
        [SerializeField] private float soundInterval = 0.7f;
        [SerializeField] private float maxRandomInterval = 1.2f;
        [SerializeField] private string soundName = "EnemyAttack";
        [SerializeField] private bool useCustomVolume = false;
        [Range(0f, 1f)]
        [SerializeField] private float soundVolume = 1f;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;
        [SerializeField] private bool debugGizmos = true;

        private SteeringNavigator _navigator;
        
        private Transform _currentTarget;
        private Collider _currentTargetCollider;
        private int _currentTargetPriority;
        private float _currentTargetAttackRadius;
        private int _tickCount = 0;

        public int Priority => priority;

        private void Awake()
        {
            _navigator = GetComponent<SteeringNavigator>();
        }

        public bool CanActivate()
        {
            if (_currentTarget != null && _currentTargetCollider != null)
            {
                if (IsTargetValidAndInDetectionRadius(_currentTarget, _currentTargetCollider))
                {
                    if (TryFindBetterTarget(out Transform betterTarget, out Collider betterCollider, 
                            out int betterPriority, out float betterAttackRadius))
                    {
                        SetTarget(betterTarget, betterCollider, betterPriority, betterAttackRadius);
                        if (debugLogs) Debug.Log($"[AttackBehavior] Switched to better target: {_currentTarget.name}", this);
                    }
                    return true;
                }
                ClearTarget();
            }
            return TryAcquireTarget();
        }

        public void OnEnter()
        {
            _tickCount = 0;
            if (debugLogs) Debug.Log($"[AttackBehavior] OnEnter. Target: {(_currentTarget != null ? _currentTarget.name : "None")}", this);

            // --- NAVIGATION OVERRIDE START ---
            // Tell navigator to ignore traps (or whatever is excluded from attackObstacleLayers)
            if (_navigator != null)
            {
                _navigator.SetObstacleMask(attackObstacleLayers);
            }
            // ---------------------------------
        }

        public void Tick(float deltaTime)
        {
            _tickCount++;

            if (_currentTarget == null || _currentTargetCollider == null)
            {
                _navigator.Stop();
                return;
            }

            if (!IsTargetValidAndInDetectionRadius(_currentTarget, _currentTargetCollider))
            {
                if (debugLogs) Debug.Log("[AttackBehavior] Target lost.", this);
                ClearTarget();
                _navigator.Stop();
                return;
            }

            Vector3 pos = transform.position;
            Vector3 targetPos = _currentTarget.position;
            
            Vector3 selfXZ = new Vector3(pos.x, 0f, pos.z);
            Vector3 targetXZ = new Vector3(targetPos.x, 0f, targetPos.z);
            float distXZ = Vector3.Distance(selfXZ, targetXZ);

            float effectiveAttackRadius = _currentTargetAttackRadius > 0f ? _currentTargetAttackRadius : attackRadius;

            if (distXZ <= effectiveAttackRadius)
            {
                if (debugLogs) Debug.Log($"[AttackBehavior] Attacking {_currentTarget.name}!", this);
                GameObject targetGO = _currentTarget.gameObject;
                _navigator.Stop();
                ClearTarget();
                Destroy(targetGO);
                return;
            }

            _navigator.SetDestination(targetPos);
            
            if (debugLogs && _tickCount % 60 == 0)
            {
                Debug.Log($"[AttackBehavior] Chasing target. Dist: {distXZ:F1}", this);
            }
        }

        public void OnExit()
        {
            if (debugLogs) Debug.Log("[AttackBehavior] Exiting.", this);
            _navigator.Stop();
            ClearTarget();

            // --- NAVIGATION OVERRIDE END ---
            // Important: Reset the mask so other behaviors (like Waypoint) avoid traps again
            if (_navigator != null)
            {
                _navigator.ResetObstacleMask();
            }
            // -------------------------------
        }

        // ... [Rest of the methods: TryAcquireTarget, IsTargetValid, etc. remain unchanged] ...
        
        // (Included below for completeness in case you copy-paste the whole file)
        private bool TryAcquireTarget()
        {
            if (targetPriorities == null || targetPriorities.Length == 0) return false;
            Vector3 origin = transform.position;
            Transform bestTarget = null;
            Collider bestCollider = null;
            int bestPriority = int.MinValue;
            float bestDistSq = float.PositiveInfinity;
            float bestAttackRadius = attackRadius;

            foreach (var tp in targetPriorities)
            {
                if (tp.layer.LayerMask == 0) continue;
                float radius = tp.customDetectionRadius > 0f ? tp.customDetectionRadius : detectionRadius;
                Collider[] hits = Physics.OverlapSphere(origin, radius, tp.layer.LayerMask, QueryTriggerInteraction.Ignore);

                foreach (Collider c in hits)
                {
                    if (c == null) continue;
                    float dSq = (c.transform.position - origin).sqrMagnitude;
                    bool isBetter = false;
                    if (tp.attackPriority > bestPriority) isBetter = true;
                    else if (tp.attackPriority == bestPriority && dSq < bestDistSq) isBetter = true;

                    if (isBetter)
                    {
                        bestTarget = c.transform;
                        bestCollider = c;
                        bestPriority = tp.attackPriority;
                        bestDistSq = dSq;
                        bestAttackRadius = tp.customAttackRadius > 0f ? tp.customAttackRadius : attackRadius;
                    }
                }
            }
            if (bestTarget != null) { SetTarget(bestTarget, bestCollider, bestPriority, bestAttackRadius); return true; }
            return false;
        }

        private bool TryFindBetterTarget(out Transform betterTarget, out Collider betterCollider, out int betterPriority, out float betterAttackRadius)
        {
            betterTarget = null; betterCollider = null; betterPriority = _currentTargetPriority; betterAttackRadius = _currentTargetAttackRadius;
            return false; 
        }

        private bool IsTargetValidAndInDetectionRadius(Transform t, Collider c)
        {
            if (t == null || c == null) return false;
            float radius = detectionRadius;
            bool found = false;
            foreach(var tp in targetPriorities) {
                if(tp.layer.LayerIndex == c.gameObject.layer) {
                    radius = tp.customDetectionRadius > 0 ? tp.customDetectionRadius : detectionRadius;
                    found = true; break;
                }
            }
            if(!found) return false;
            float dist = Vector3.Distance(transform.position, t.position);
            return dist <= (radius + 3.0f);
        }

        private void SetTarget(Transform t, Collider c, int p, float r) { _currentTarget = t; _currentTargetCollider = c; _currentTargetPriority = p; _currentTargetAttackRadius = r; }
        private void ClearTarget() { _currentTarget = null; _currentTargetCollider = null; _currentTargetPriority = int.MinValue; }

        public SoundPlaybackMode GetSoundMode() => enableSound ? soundMode : SoundPlaybackMode.None;
        public float GetSoundInterval() => soundInterval;
        public float GetMaxSoundInterval() => maxRandomInterval;
        public string GetSoundName() => enableSound ? soundName : null;
        public float GetSoundVolume() => (!enableSound) ? -1f : (useCustomVolume ? soundVolume : -1f);
        public bool ShouldPlaySound() => enableSound && _currentTarget != null;

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!debugGizmos) return;
            Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.2f);
            Gizmos.DrawWireSphere(transform.position, detectionRadius);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, attackRadius);
            if (Application.isPlaying && _currentTarget != null) { Gizmos.DrawLine(transform.position, _currentTarget.position); }
        }
#endif
    }
}