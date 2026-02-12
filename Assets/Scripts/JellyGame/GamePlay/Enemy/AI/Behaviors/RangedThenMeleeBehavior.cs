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

        [Header("Adaptive Range (Approach On Miss)")]
        [Tooltip("Seconds of continuous shooting without a hit before the enemy starts approaching closer.")]
        [SerializeField] private float missTimeBeforeApproach = 3.0f;

        [Tooltip("How much the effective shoot range shrinks per second while missing (units/sec).")]
        [SerializeField] private float rangeShrinkPerSecond = 2.0f;

        [Tooltip("The minimum effective shoot range — enemy won't shrink below this distance.")]
        [SerializeField] private float minEffectiveShootRange = 3.0f;

        [Tooltip("How fast the effective range recovers back to full after a hit (units/sec). 0 = instant reset.")]
        [SerializeField] private float rangeRecoveryPerSecond = 0f;

        [Tooltip("Scale down target leading at long range to reduce overshoot. " +
                 "At distances >= shootRange, lead is multiplied by this factor.")]
        [Range(0f, 1f)]
        [SerializeField] private float longRangeLeadScale = 0.4f;

        [Header("Slow Window -> Chase & Melee")]
        [SerializeField] private float slowDurationSecondsOverride = -1f;

        [Header("Melee")]
        [SerializeField] private float meleeAttackRadius = 2f;
        [SerializeField] private float damagePerHit = 1f;
        [SerializeField] private float hitCooldownSeconds = 0.5f;

        [Header("Animation")]
        [Tooltip("Parameter name for attack animation. Leave empty to use default 'ran_attac'.")]
        [SerializeField] private string attackParamName = "ran_attac";

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        public int Priority => priority;

        private SteeringNavigator _nav;
        private Animator _animator;

        private Transform _target;
        private Collider _targetCol;

        private Phase _phase = Phase.None;

        private float _nextShootTime;
        private float _nextMeleeHitTime;
        private float _slowWindowEndTime = -1f;

        private Vector3 _lastTargetPos;
        private Vector3 _estimatedTargetVel;

        // ===== Adaptive range state =====
        /// <summary>Current effective shoot range. Shrinks on miss streaks, resets on hit.</summary>
        private float _effectiveShootRange;

        /// <summary>Time.time when the last projectile successfully hit the target.</summary>
        private float _lastHitTime;

        /// <summary>Time.time when the enemy first started shooting at the current target without landing a hit.</summary>
        private float _shootingWithoutHitSince;

        /// <summary>Total shots fired at the current target since the last hit (for debug).</summary>
        private int _shotsSinceLastHit;

        private void Awake()
        {
            _nav = GetComponent<SteeringNavigator>();
            _animator = GetComponentInChildren<Animator>();
            if (_animator == null)
                _animator = GetComponent<Animator>();

            _effectiveShootRange = shootRange;
        }

        public bool CanActivate()
        {
            if (_target != null && _targetCol != null)
            {
                if (IsTargetStillValid())
                    return true;

                ClearTarget();
                _phase = Phase.None;
                UpdateAttackAnimation(false);
            }

            bool hasTarget = TryAcquireTarget();
            if (!hasTarget)
                UpdateAttackAnimation(false);
            return hasTarget;
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

            _lastTargetPos = GetAimPosition();
            _estimatedTargetVel = Vector3.zero;

            // Reset adaptive range for fresh engagement
            ResetAdaptiveRange();
        }

        public void Tick(float dt)
        {
            if (_target == null || _targetCol == null)
            {
                _nav.Stop();
                UpdateAttackAnimation(false);
                return;
            }

            if (!IsTargetStillValid())
            {
                _nav.Stop();
                ClearTarget();
                _phase = Phase.None;
                UpdateAttackAnimation(false);
                return;
            }

            UpdateTargetVelocityEstimate(dt);
            UpdateAdaptiveRange(dt);

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
                UpdateAttackAnimation(true);

                if (distXZ > _effectiveShootRange)
                {
                    // Move closer — either because out of base range, or adaptive range shrank
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
                UpdateAttackAnimation(false);

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
            UpdateAttackAnimation(false);
        }

        public void OnExit()
        {
            _nav.Stop();
            ClearTarget();
            _phase = Phase.None;
            UpdateAttackAnimation(false);
        }

        // ===================== Adaptive Range Logic =====================

        /// <summary>
        /// Reset adaptive range to full shoot range. Called on new engagement or successful hit.
        /// </summary>
        private void ResetAdaptiveRange()
        {
            _effectiveShootRange = shootRange;
            _lastHitTime = Time.time;
            _shootingWithoutHitSince = Time.time;
            _shotsSinceLastHit = 0;
        }

        /// <summary>
        /// Called when a projectile successfully hits the target.
        /// Resets (or starts recovering) the effective shoot range.
        /// </summary>
        private void OnProjectileHitTarget()
        {
            _lastHitTime = Time.time;
            _shootingWithoutHitSince = Time.time;
            _shotsSinceLastHit = 0;

            if (rangeRecoveryPerSecond <= 0f)
            {
                // Instant reset
                _effectiveShootRange = shootRange;
            }

            if (debugLogs)
                Debug.Log($"[RangedAdaptive] HIT! Effective range reset to {_effectiveShootRange:F1}", this);
        }

        /// <summary>
        /// Each frame during Shooting phase: shrink range if missing too long, recover if recently hit.
        /// </summary>
        private void UpdateAdaptiveRange(float dt)
        {
            if (_phase != Phase.Shooting)
                return;

            float timeSinceHit = Time.time - _lastHitTime;

            // Recovery: if we recently hit and range is below max, grow it back
            if (rangeRecoveryPerSecond > 0f && _effectiveShootRange < shootRange && timeSinceHit < missTimeBeforeApproach)
            {
                _effectiveShootRange = Mathf.Min(shootRange, _effectiveShootRange + rangeRecoveryPerSecond * dt);
                return;
            }

            // Shrink: if we've been missing for too long, start reducing effective range
            float timeMissing = Time.time - _shootingWithoutHitSince;
            if (timeMissing > missTimeBeforeApproach)
            {
                float prevRange = _effectiveShootRange;
                _effectiveShootRange = Mathf.Max(minEffectiveShootRange, _effectiveShootRange - rangeShrinkPerSecond * dt);

                if (debugLogs && !Mathf.Approximately(prevRange, _effectiveShootRange) && Time.frameCount % 30 == 0)
                    Debug.Log($"[RangedAdaptive] Missing for {timeMissing:F1}s ({_shotsSinceLastHit} shots). " +
                              $"Effective range: {_effectiveShootRange:F1}/{shootRange:F1}", this);
            }
        }

        // ===================== Shooting =====================

        private void TryShootAtTarget()
        {
            if (projectilePrefab == null) return;
            if (Time.time < _nextShootTime) return;

            _nextShootTime = Time.time + Mathf.Max(0.05f, shootCooldownSeconds);
            _shotsSinceLastHit++;

            Transform spawnT = muzzle != null ? muzzle : transform;
            Vector3 baseAimPos = GetAimPosition();
            Vector3 gravity = GetEffectiveProjectileGravity();

            // 1. Target Leading — scale down at long range to reduce overshoot
            Vector3 finalTargetPos = baseAimPos;
            if (useTargetLead && _estimatedTargetVel.sqrMagnitude > 0.1f)
            {
                float distToTarget = Vector3.Distance(spawnT.position, baseAimPos);

                // Scale lead strength based on distance: full strength up close, reduced at range
                float distRatio = Mathf.Clamp01(distToTarget / Mathf.Max(1f, shootRange));
                float scaledLeadStrength = Mathf.Lerp(leadStrength, leadStrength * longRangeLeadScale, distRatio);

                float predictTime = Mathf.Clamp(distToTarget / 20f, 0f, 1f);
                Vector3 lead = _estimatedTargetVel * predictTime * scaledLeadStrength;
                if (lead.magnitude > maxLeadOffset) lead = lead.normalized * maxLeadOffset;
                finalTargetPos += lead;
            }

            // 2. Physics Calculation — Flat vs Optimal
            Vector3 vFlat = BallisticAimSolver.SolveVelocityForTime(spawnT.position, finalTargetPos, maxTimeToTarget, gravity);

            Vector3 toTarget = finalTargetPos - spawnT.position;
            float y = toTarget.y;
            Vector3 xz = new Vector3(toTarget.x, 0, toTarget.z);
            float x = xz.magnitude;
            float g = gravity.magnitude;

            float b = Mathf.Sqrt(x * x + y * y);
            float tOptimal = Mathf.Sqrt((b + y) / (0.5f * g));
            Vector3 vOptimal = BallisticAimSolver.SolveVelocityForTime(spawnT.position, finalTargetPos, tOptimal, gravity);

            // 3. Select the best trajectory
            Vector3 finalVelocity;

            if (vFlat.magnitude <= maxLaunchSpeed)
            {
                finalVelocity = vFlat;
            }
            else if (vOptimal.magnitude <= maxLaunchSpeed)
            {
                finalVelocity = vOptimal;
            }
            else
            {
                finalVelocity = vOptimal.normalized * maxLaunchSpeed;
            }

            // 4. Face target
            Vector3 flatV = new Vector3(finalVelocity.x, 0f, finalVelocity.z);
            if (flatV.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(flatV.normalized, Vector3.up);

            // 5. Fire
            BallisticFireProjectile proj = Instantiate(projectilePrefab, spawnT.position, Quaternion.identity);
            proj.OnHit += HandleProjectileHit;

            // Safety: Force drag to zero to ensure projectile behaves like the math says
            Rigidbody projRb = proj.GetComponent<Rigidbody>();
            if (projRb != null) projRb.linearDamping = 0f;

            proj.Launch(finalVelocity);

            if (debugLogs)
                Debug.Log($"[Ranged] Fire Speed: {finalVelocity.magnitude:F1} (Max: {maxLaunchSpeed}), " +
                          $"EffRange: {_effectiveShootRange:F1}/{shootRange:F1}, " +
                          $"Shots since hit: {_shotsSinceLastHit}", this);
        }

        private Vector3 GetAimPosition()
        {
            var provider = _target != null ? _target.GetComponentInParent<ITargetAimPoint>() : null;
            if (provider != null && provider.AimPoint != null)
                return provider.AimPoint.position;

            if (_targetCol != null)
                return _targetCol.bounds.center;

            return _target != null ? _target.position : transform.position;
        }

        private Vector3 GetEffectiveProjectileGravity()
        {
            return Physics.gravity * projectilePrefab.GravityMultiplier + Vector3.down * projectilePrefab.ExtraDownwardAccel;
        }

        private void HandleProjectileHit(Collider hit)
        {
            if (_target == null || _targetCol == null || hit == null)
                return;

            if (hit.transform == _target || hit.transform.IsChildOf(_target))
            {
                // Notify adaptive range system of the successful hit
                OnProjectileHitTarget();

                // Allow "ranged-only" mode: if set to 0, never enter melee/chase phase
                if (Mathf.Approximately(slowDurationSecondsOverride, 0f))
                    return;

                float dur = slowDurationSecondsOverride > 0f ? slowDurationSecondsOverride : projectilePrefab.SlowDurationSeconds;
                dur = Mathf.Max(0.01f, dur);

                _slowWindowEndTime = Time.time + dur;
                _phase = Phase.ChasingDuringSlow;
            }
        }

        // ===================== Melee =====================

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

        // ===================== Target Tracking =====================

        private void UpdateTargetVelocityEstimate(float dt)
        {
            if (!useTargetLead) return;
            if (dt <= 0f) return;

            Vector3 now = GetAimPosition();
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

            // Reset adaptive range for next engagement
            _effectiveShootRange = shootRange;
            _shotsSinceLastHit = 0;

            UpdateAttackAnimation(false);
        }

        private void UpdateAttackAnimation(bool isAttacking)
        {
            if (_animator == null) return;

            string attackParam = string.IsNullOrEmpty(attackParamName) ? "ran_attac" : attackParamName;
            _animator.SetBool(attackParam, isAttacking);

            if (debugLogs && Time.frameCount % 60 == 0)
                Debug.Log($"[RangedThenMelee] Attack animation - {attackParam}: {isAttacking}", this);
        }
    }
}