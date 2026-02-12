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

        [Tooltip("Scale down target leading at long range to reduce overshoot. " +
                 "At distances >= shootRange, lead is multiplied by this factor.")]
        [Range(0f, 1f)]
        [SerializeField] private float longRangeLeadScale = 0.4f;

        [Header("Fixed-Speed Arc Selection")]
        [Tooltip("When the target is too far for a flat/optimal arc within maxLaunchSpeed, " +
                 "use a high (lobbed) arc instead of a low (flat) arc.\n" +
                 "High arc is slower but clears obstacles better.")]
        [SerializeField] private bool preferHighArcWhenSpeedCapped = false;

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

        private void Awake()
        {
            _nav = GetComponent<SteeringNavigator>();
            _animator = GetComponentInChildren<Animator>();
            if (_animator == null)
                _animator = GetComponent<Animator>();
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

        // ===================== Shooting =====================

        private void TryShootAtTarget()
        {
            if (projectilePrefab == null) return;
            if (Time.time < _nextShootTime) return;

            _nextShootTime = Time.time + Mathf.Max(0.05f, shootCooldownSeconds);

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

            // 2. Physics Calculation — try flat arc first, then optimal-time arc
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
            string debugMethod = "flat";

            if (vFlat.magnitude <= maxLaunchSpeed)
            {
                // Flat arc fits within speed limit — fast, direct trajectory
                finalVelocity = vFlat;
                debugMethod = "flat";
            }
            else if (vOptimal.magnitude <= maxLaunchSpeed)
            {
                // Optimal (minimum-energy) arc fits — slightly higher but still within limit
                finalVelocity = vOptimal;
                debugMethod = "optimal";
            }
            else
            {
                // FIX: Both arcs exceed maxLaunchSpeed.
                //
                // OLD (broken):
                //   finalVelocity = vOptimal.normalized * maxLaunchSpeed;
                //   This preserves the angle from a higher-speed solution but at the capped
                //   lower speed the projectile doesn't travel far enough -> hits the floor.
                //
                // NEW (correct):
                //   Solve the classic ballistic angle equation for the fixed maxLaunchSpeed.
                //   This finds the EXACT angle where the parabola passes through the target
                //   at the given speed. Two solutions exist (low arc / high arc) or zero
                //   if the target is truly out of range.

                Vector3 fixedSpeedVel = BallisticAimSolver.SolveForFixedSpeed(
                    spawnT.position, finalTargetPos, maxLaunchSpeed, gravity, preferHighArcWhenSpeedCapped);

                if (fixedSpeedVel.sqrMagnitude > 0.01f)
                {
                    finalVelocity = fixedSpeedVel;
                    debugMethod = "fixedSpeed";
                }
                else
                {
                    // Target is truly out of range at maxLaunchSpeed — no valid angle exists.
                    // Fire at 45° (maximum range angle) as a best-effort attempt.
                    Vector3 dir = xz.magnitude > 0.001f ? xz.normalized : transform.forward;
                    float angle45 = 45f * Mathf.Deg2Rad;
                    finalVelocity = dir * (maxLaunchSpeed * Mathf.Cos(angle45))
                                  + Vector3.up * (maxLaunchSpeed * Mathf.Sin(angle45));
                    debugMethod = "outOfRange_45deg";

                    if (debugLogs)
                        Debug.LogWarning($"[Ranged] Target at {x:F1}m is OUT OF RANGE at speed {maxLaunchSpeed}. " +
                                         $"Max range ~ {(maxLaunchSpeed * maxLaunchSpeed / g):F1}m. Firing 45deg best-effort.", this);
                }
            }

            // 4. Face target
            Vector3 flatV = new Vector3(finalVelocity.x, 0f, finalVelocity.z);
            if (flatV.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(flatV.normalized, Vector3.up);

            // 5. Fire
            BallisticFireProjectile proj = Instantiate(projectilePrefab, spawnT.position, Quaternion.identity);
            proj.OnHit += HandleProjectileHit;

            // Safety: Force drag to zero so the projectile trajectory matches the math.
            // If the prefab has any drag set in the Inspector, it would cause undershooting.
            Rigidbody projRb = proj.GetComponent<Rigidbody>();
            if (projRb != null) projRb.linearDamping = 0f;

            proj.Launch(finalVelocity);

            if (debugLogs)
                Debug.Log($"[Ranged] Fired ({debugMethod}). Speed: {finalVelocity.magnitude:F1}/{maxLaunchSpeed}, " +
                          $"Dist: {x:F1}m, Angle: {Mathf.Atan2(finalVelocity.y, flatV.magnitude) * Mathf.Rad2Deg:F1}deg", this);
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