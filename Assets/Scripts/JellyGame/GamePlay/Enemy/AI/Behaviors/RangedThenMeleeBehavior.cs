// FILEPATH: Assets/Scripts/AI/Behaviors/RangedThenMeleeBehavior.cs
using JellyGame.GamePlay.Combat;
using JellyGame.GamePlay.Combat.Projectiles;
using JellyGame.GamePlay.Combat.Targeting;
using JellyGame.GamePlay.Enemy.AI.Movement;
using UnityEngine;

namespace JellyGame.GamePlay.Enemy.AI.Behaviors
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SteeringNavigator))]
    public class RangedThenMeleeBehavior : MonoBehaviour, IEnemyBehavior
    {
        private enum Phase { None, Shooting, ChasingDuringSlow }

        [Header("Behavior Priority")]
        [SerializeField] private int priority = 25;

        [Header("Targeting")]
        [SerializeField] private LayerMask targetLayers;
        [SerializeField] private float detectionRadius = 18f;

        [Header("Shooting")]
        [SerializeField] private BallisticFireProjectile projectilePrefab;
        [SerializeField] private Transform muzzle;
        [SerializeField] private float shootRange = 12f;
        [SerializeField] private float shootCooldownSeconds = 0.8f;

        [Header("Arc Time Range")]
        [SerializeField] private float minTimeToTarget = 0.25f;
        [SerializeField] private float maxTimeToTarget = 1.2f;
        [SerializeField] private float maxLaunchSpeed = 30f;

        [Header("Target Leading (anti-jitter)")]
        [SerializeField] private bool useTargetLead = true;
        [Range(0f, 1f)]
        [SerializeField] private float leadStrength = 0.8f;
        [SerializeField] private float leadMinSpeed = 0.25f;
        [SerializeField] private float leadSmoothingTime = 0.15f;
        [SerializeField] private float maxLeadOffset = 2.0f;

        [Header("Slow Window -> Chase & Melee")]
        [SerializeField] private float slowDurationSecondsOverride = -1f;

        [Header("Melee")]
        [SerializeField] private float meleeAttackRadius = 2f;
        [SerializeField] private float damagePerHit = 1f;
        [SerializeField] private float hitCooldownSeconds = 0.5f;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        public int Priority => priority;

        private SteeringNavigator _nav;

        private Transform _target;
        private Collider _targetCol;

        private Phase _phase = Phase.None;

        private float _nextShootTime;
        private float _nextMeleeHitTime;
        private float _slowWindowEndTime = -1f;

        private Vector3 _lastTargetPos;
        private Vector3 _estimatedTargetVel;

        private void Awake()
        {
            _nav = GetComponent<SteeringNavigator>();
        }

        public bool CanActivate()
        {
            if (_target != null && _targetCol != null)
            {
                if (IsTargetStillValid())
                    return true;

                ClearTarget();
                _phase = Phase.None;
            }

            return TryAcquireTarget();
        }

        public void OnEnter()
        {
            if (_target == null || _targetCol == null)
            {
                if (!TryAcquireTarget())
                {
                    _phase = Phase.None;
                    return;
                }
            }

            _phase = Phase.Shooting;
            _nextShootTime = Time.time;
            _nextMeleeHitTime = Time.time;
            _slowWindowEndTime = -1f;

            _lastTargetPos = GetAimPosition(); // important: start from aim pos
            _estimatedTargetVel = Vector3.zero;
        }

        public void Tick(float dt)
        {
            if (_target == null || _targetCol == null)
            {
                _nav.Stop();
                return;
            }

            if (!IsTargetStillValid())
            {
                _nav.Stop();
                ClearTarget();
                _phase = Phase.None;
                return;
            }

            UpdateTargetVelocityEstimate(dt);

            if (_phase == Phase.ChasingDuringSlow && Time.time >= _slowWindowEndTime)
            {
                _phase = Phase.Shooting;
                _nav.Stop();
            }

            Vector3 pos = transform.position;
            Vector3 tpos = _target.position;

            float distXZ = Vector3.Distance(new Vector3(pos.x, 0f, pos.z), new Vector3(tpos.x, 0f, tpos.z));

            if (_phase == Phase.Shooting)
            {
                if (distXZ > shootRange)
                {
                    _nav.SetDestination(tpos);
                }
                else
                {
                    _nav.Stop();
                    TryShootAtTarget();
                }
                return;
            }

            if (_phase == Phase.ChasingDuringSlow)
            {
                if (distXZ > meleeAttackRadius)
                {
                    _nav.SetDestination(tpos);
                    return;
                }

                _nav.Stop();
                TryDealMeleeDamage();
                return;
            }

            _nav.Stop();
        }

        public void OnExit()
        {
            _nav.Stop();
            ClearTarget();
            _phase = Phase.None;
        }

    private void TryShootAtTarget()
    {
        if (projectilePrefab == null) return;
        if (Time.time < _nextShootTime) return;

        _nextShootTime = Time.time + Mathf.Max(0.05f, shootCooldownSeconds);

        Transform spawnT = muzzle != null ? muzzle : transform;
        Vector3 baseAimPos = GetAimPosition(); // Target Center
        Vector3 gravity = GetEffectiveProjectileGravity();
        
        // 1. Target Leading (Skip if velocity is tiny to avoid jitter)
        Vector3 finalTargetPos = baseAimPos;
        if (useTargetLead && _estimatedTargetVel.sqrMagnitude > 0.1f)
        {
            // Simple linear prediction based on flight time of ~0.5s
            float predictTime = Mathf.Clamp(Vector3.Distance(spawnT.position, baseAimPos) / 20f, 0f, 1f);
            Vector3 lead = _estimatedTargetVel * predictTime * leadStrength;
            if (lead.magnitude > maxLeadOffset) lead = lead.normalized * maxLeadOffset;
            finalTargetPos += lead;
        }

        // 2. Physics Calculation
        // We compare two modes: "Preferred Flat Shot" vs "Optimal High Shot"
        
        // A. The "Flat" Shot (Fast, direct, tries to hit within maxTimeToTarget)
        // We try to solve for exactly maxTimeToTarget (e.g. 1.2s).
        Vector3 vFlat = BallisticAimSolver.SolveVelocityForTime(spawnT.position, finalTargetPos, maxTimeToTarget, gravity);
        
        // B. The "Optimal" Shot (The Minimum Energy/Speed required to reach distance)
        // This creates a ~45 degree parabola. It is the furthest possible shot for a given speed.
        Vector3 toTarget = finalTargetPos - spawnT.position;
        float y = toTarget.y;
        Vector3 xz = new Vector3(toTarget.x, 0, toTarget.z);
        float x = xz.magnitude;
        float g = gravity.magnitude;
        
        // Derived formula for time of minimum initial speed: t = sqrt( (sqrt(x^2 + y^2) + y) / (g/2) )
        float b = Mathf.Sqrt(x * x + y * y);
        float tOptimal = Mathf.Sqrt((b + y) / (0.5f * g));
        Vector3 vOptimal = BallisticAimSolver.SolveVelocityForTime(spawnT.position, finalTargetPos, tOptimal, gravity);

        // 3. Select the best trajectory
        Vector3 finalVelocity;

        // Check if the Flat shot is within our Speed Limit
        if (vFlat.magnitude <= maxLaunchSpeed)
        {
            // We have enough power to shoot flat! Do it.
            finalVelocity = vFlat;
        }
        else if (vOptimal.magnitude <= maxLaunchSpeed)
        {
            // Flat shot is impossible (too weak), but High Arc is possible. Use High Arc.
            finalVelocity = vOptimal;
        }
        else
        {
            // Even the optimal shot requires more speed than we have. 
            // We are physically out of range. 
            // Fire at Max Speed using the Optimal Arc angle (45 deg) to get as close as possible.
            finalVelocity = vOptimal.normalized * maxLaunchSpeed;
        }

        // 4. Fire
        Vector3 flatV = new Vector3(finalVelocity.x, 0f, finalVelocity.z);
        if (flatV.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(flatV.normalized, Vector3.up);

        BallisticFireProjectile proj = Instantiate(projectilePrefab, spawnT.position, Quaternion.identity);
        proj.OnHit += HandleProjectileHit;
        
        // SAFETY: Force drag to zero to ensure projectile behaves like the math says
        if (proj.GetComponent<Rigidbody>()) proj.GetComponent<Rigidbody>().linearDamping = 0f; 
        
        proj.Launch(finalVelocity);

        if(debugLogs) Debug.Log($"[Ranged] Fire Speed: {finalVelocity.magnitude:F1} (Max: {maxLaunchSpeed})");
    }

        private Vector3 GetAimPosition()
        {
            // Preferred: explicit aim point provider in target hierarchy
            var provider = _target != null ? _target.GetComponentInParent<ITargetAimPoint>() : null;
            if (provider != null && provider.AimPoint != null)
                return provider.AimPoint.position;

            // Fallback 1: collider bounds center
            if (_targetCol != null)
                return _targetCol.bounds.center;

            // Fallback 2: target transform position
            return _target != null ? _target.position : transform.position;
        }

        private Vector3 GetEffectiveProjectileGravity()
        {
            return Physics.gravity * projectilePrefab.GravityMultiplier + Vector3.down * projectilePrefab.ExtraDownwardAccel;
        }

        private float ChooseTimeForSpeedCap(Vector3 startPos, Vector3 targetPos, Vector3 gravity)
        {
            // Start with the user's preferred window
            float tMin = Mathf.Max(0.05f, minTimeToTarget);
            float tMax = Mathf.Max(tMin + 0.01f, maxTimeToTarget);

            // 1. Check if the FASTEST shot (tMin) is valid
            float speedAtMin = BallisticAimSolver.SolveVelocityForTime(startPos, targetPos, tMin, gravity).magnitude;
            if (speedAtMin <= maxLaunchSpeed)
                return tMin;

            // 2. Check if the SLOWEST PREFERRED shot (tMax) is valid
            float speedAtMax = BallisticAimSolver.SolveVelocityForTime(startPos, targetPos, tMax, gravity).magnitude;
            
            // If even the slowest shot is too fast, it means we are shooting too "flat".
            // We must INCREASE time (lob higher) to reduce speed requirements.
            if (speedAtMax > maxLaunchSpeed)
            {
                // Try extending time up to 2.5 seconds (high arc)
                // This loop looks for a valid time where speed < maxLaunchSpeed
                float extendedTime = tMax;
                for (int k = 0; k < 10; k++)
                {
                    extendedTime += 0.2f; 
                    float s = BallisticAimSolver.SolveVelocityForTime(startPos, targetPos, extendedTime, gravity).magnitude;
                    if (s <= maxLaunchSpeed)
                        return extendedTime; // Found a valid "Lob" shot
                }
                
                // If we get here, the target is likely out of physical range for this speed limit.
                // We return the longest time tried to get as close as possible.
                return extendedTime;
            }

            // 3. Binary Search
            // If we are here, tMin requires TOO MUCH speed, and tMax is OK.
            // We want to find the fastest shot (smallest t) that is still under the speed limit.
            float lo = tMin;
            float hi = tMax;

            for (int i = 0; i < 8; i++)
            {
                float mid = (lo + hi) * 0.5f;
                float s = BallisticAimSolver.SolveVelocityForTime(startPos, targetPos, mid, gravity).magnitude;

                if (s > maxLaunchSpeed)
                    lo = mid; // Too fast, need more time (higher arc)
                else
                    hi = mid; // Valid speed, try to go faster/flatter
            }

            return hi;
        }

        private void HandleProjectileHit(Collider hit)
        {
            if (_target == null || _targetCol == null || hit == null)
                return;

            if (hit.transform == _target || hit.transform.IsChildOf(_target))
            {
                float dur = slowDurationSecondsOverride > 0f ? slowDurationSecondsOverride : projectilePrefab.SlowDurationSeconds;
                dur = Mathf.Max(0.01f, dur);

                _slowWindowEndTime = Time.time + dur;
                _phase = Phase.ChasingDuringSlow;
            }
        }

        private void TryDealMeleeDamage()
        {
            if (Time.time < _nextMeleeHitTime) return;
            _nextMeleeHitTime = Time.time + Mathf.Max(0.05f, hitCooldownSeconds);

            IDamageable dmg = _target.GetComponentInParent<IDamageable>();
            if (dmg == null)
            {
                ClearTarget();
                _phase = Phase.None;
                return;
            }

            dmg.ApplyDamage(damagePerHit);
        }

        private void UpdateTargetVelocityEstimate(float dt)
        {
            if (!useTargetLead) return;
            if (dt <= 0f) return;

            Vector3 now = GetAimPosition(); // important: estimate based on aim point, not root
            Vector3 raw = (now - _lastTargetPos) / dt;
            raw.y = 0f;

            _lastTargetPos = now;

            float smoothT = (leadSmoothingTime <= 0f) ? 1f : Mathf.Clamp01(dt / leadSmoothingTime);
            _estimatedTargetVel = Vector3.Lerp(_estimatedTargetVel, raw, smoothT);

            if (_estimatedTargetVel.magnitude < leadMinSpeed)
                _estimatedTargetVel = Vector3.zero;
        }

        private bool TryAcquireTarget()
        {
            Vector3 origin = transform.position;
            Collider[] hits = Physics.OverlapSphere(origin, detectionRadius, targetLayers, QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0) return false;

            Collider best = null;
            float bestDistSq = float.PositiveInfinity;

            for (int i = 0; i < hits.Length; i++)
            {
                Collider c = hits[i];
                if (c == null) continue;

                float dSq = (c.transform.position - origin).sqrMagnitude;
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    best = c;
                }
            }

            if (best == null) return false;

            _targetCol = best;
            _target = best.transform;

            _lastTargetPos = GetAimPosition();
            _estimatedTargetVel = Vector3.zero;

            return true;
        }

        private bool IsTargetStillValid()
        {
            if (_target == null || _targetCol == null) return false;
            if ((targetLayers.value & (1 << _targetCol.gameObject.layer)) == 0) return false;

            float dist = Vector3.Distance(transform.position, _target.position);
            return dist <= detectionRadius + 2f;
        }

        private void ClearTarget()
        {
            _target = null;
            _targetCol = null;

            _estimatedTargetVel = Vector3.zero;

            _nextShootTime = 0f;
            _nextMeleeHitTime = 0f;
            _slowWindowEndTime = -1f;
        }
    }
}
