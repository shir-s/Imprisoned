// FILEPATH: Assets/Scripts/AI/Behaviors/UnstuckBehavior.cs
using UnityEngine;

/// <summary>
/// Emergency "unstuck" behavior with highest priority.
/// 
/// How it works:
/// - Passively monitors the unit's movement every frame (even when not active).
/// - If the unit moves less than expected for a sustained period, it's considered "stuck".
/// - When stuck is detected, CanActivate() returns true, triggering this behavior.
/// - The behavior then moves the unit away from nearby obstacles until it's free.
/// - Once the unit can move freely again, it deactivates and lets other behaviors take over.
/// 
/// Detection methods:
/// 1. Position-based: Unit hasn't moved enough despite time passing.
/// 2. Oscillation-based: Unit is jittering back and forth (changing direction rapidly).
/// 
/// Recovery methods:
/// 1. Move away from the nearest obstacle.
/// 2. If surrounded, try to find any open direction.
/// 3. If completely trapped, push through (last resort).
/// 
/// Attach this to any unit that uses the BehaviorManager system.
/// Set priority higher than all other behaviors (e.g., 100).
/// </summary>
[DisallowMultipleComponent]
public class UnstuckBehavior : MonoBehaviour, IEnemyBehavior
{
    [Header("Behavior Priority")]
    [Tooltip("Should be the HIGHEST priority so it can interrupt any behavior when stuck.")]
    [SerializeField] private int priority = 100;

    [Header("Stuck Detection - Position Based")]
    [Tooltip("If the unit moves less than this distance over stuckTimeThreshold, it's considered stuck.")]
    [SerializeField] private float stuckDistanceThreshold = 0.1f;

    [Tooltip("How long the unit must be nearly stationary before being considered stuck (seconds).")]
    [SerializeField] private float stuckTimeThreshold = 1.0f;

    [Header("Stuck Detection - Oscillation Based")]
    [Tooltip("If true, also detect stuck by rapid direction changes (jittering).")]
    [SerializeField] private bool detectOscillation = true;

    [Tooltip("How many direction reversals in the time window triggers oscillation detection.")]
    [SerializeField] private int oscillationCountThreshold = 4;

    [Tooltip("Time window for counting direction reversals (seconds).")]
    [SerializeField] private float oscillationTimeWindow = 1.0f;

    [Tooltip("Minimum angle change to count as a direction reversal (degrees).")]
    [SerializeField] private float oscillationAngleThreshold = 90f;

    [Header("Recovery Settings")]
    [Tooltip("Speed while recovering from stuck state.")]
    [SerializeField] private float recoverySpeed = 2.5f;

    [Tooltip("Layers to check for obstacles when finding escape direction.")]
    [SerializeField] private LayerMask obstacleLayers;

    [Tooltip("Radius to check for nearby obstacles.")]
    [SerializeField] private float obstacleCheckRadius = 2f;

    [Tooltip("How far to move before considering ourselves unstuck.")]
    [SerializeField] private float recoveryDistance = 1.5f;

    [Tooltip("Maximum time to spend in recovery before forcing deactivation (seconds).")]
    [SerializeField] private float maxRecoveryTime = 3f;

    [Tooltip("Number of directions to sample when looking for escape route.")]
    [SerializeField] private int escapeDirectionSamples = 16;

    [Header("Cooldown")]
    [Tooltip("After recovering, wait this long before allowing another stuck detection.")]
    [SerializeField] private float cooldownAfterRecovery = 2f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool debugGizmos = false;

    public int Priority => priority;

    // Stuck detection state (runs even when behavior is not active)
    private Vector3 _monitorStartPosition;
    private float _monitorTimer;
    private bool _isStuckDetected;

    // Oscillation detection
    private Vector3 _lastDirection;
    private float[] _directionChangeTimes;
    private int _directionChangeIndex;
    private int _directionChangeCount;

    // Recovery state (only when behavior is active)
    private Vector3 _recoveryStartPosition;
    private Vector3 _escapeDirection;
    private float _recoveryTimer;
    private bool _isRecovering;

    // Cooldown
    private float _cooldownTimer;

    private void Awake()
    {
        _monitorStartPosition = transform.position;
        _directionChangeTimes = new float[oscillationCountThreshold + 2];
        _lastDirection = transform.forward;
    }

    private void Update()
    {
        // Always monitor for stuck, even when not the active behavior
        if (!_isRecovering)
        {
            MonitorForStuck();
        }

        // Update cooldown
        if (_cooldownTimer > 0f)
        {
            _cooldownTimer -= Time.deltaTime;
        }
    }

    // --------------------------------------------------------
    // IEnemyBehavior
    // --------------------------------------------------------

    public bool CanActivate()
    {
        // Don't activate during cooldown
        if (_cooldownTimer > 0f)
            return false;

        // If already recovering, stay active until done
        if (_isRecovering)
            return true;

        return _isStuckDetected;
    }

    public void OnEnter()
    {
        if (debugLogs)
        {
            Debug.Log("[UnstuckBehavior] OnEnter - Starting recovery", this);
        }

        _isRecovering = true;
        _recoveryStartPosition = transform.position;
        _recoveryTimer = 0f;

        // Find escape direction
        _escapeDirection = FindEscapeDirection();

        if (debugLogs)
        {
            Debug.Log($"[UnstuckBehavior] Escape direction: {_escapeDirection}", this);
        }
    }

    public void Tick(float deltaTime)
    {
        if (!_isRecovering)
            return;

        _recoveryTimer += deltaTime;

        // Check if we've recovered enough
        float movedDistance = Vector3.Distance(transform.position, _recoveryStartPosition);
        
        if (movedDistance >= recoveryDistance)
        {
            if (debugLogs)
            {
                Debug.Log($"[UnstuckBehavior] Recovery complete! Moved {movedDistance:F2} units.", this);
            }
            CompleteRecovery();
            return;
        }

        // Check for timeout
        if (_recoveryTimer >= maxRecoveryTime)
        {
            if (debugLogs)
            {
                Debug.Log("[UnstuckBehavior] Recovery timeout - forcing completion.", this);
            }
            CompleteRecovery();
            return;
        }

        // Move in escape direction
        Vector3 pos = transform.position;
        Vector3 newPos = pos + _escapeDirection * (recoverySpeed * deltaTime);
        transform.position = newPos;

        // Face movement direction
        if (_escapeDirection.sqrMagnitude > 0.001f)
        {
            Vector3 lookDir = _escapeDirection;
            lookDir.y = 0f;
            if (lookDir.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(lookDir.normalized, Vector3.up);
            }
        }

        // Check if we're still stuck during recovery (escape direction blocked)
        float expectedMove = recoverySpeed * deltaTime * 0.5f;
        float actualMove = Vector3.Distance(pos, transform.position);
        
        if (actualMove < expectedMove && _recoveryTimer > 0.5f)
        {
            // Try a new escape direction
            _escapeDirection = FindEscapeDirection();
            
            if (debugLogs)
            {
                Debug.Log($"[UnstuckBehavior] Blocked during recovery, trying new direction: {_escapeDirection}", this);
            }
        }
    }

    public void OnExit()
    {
        if (debugLogs)
        {
            Debug.Log("[UnstuckBehavior] OnExit", this);
        }

        // This shouldn't normally happen (we control our own exit via CompleteRecovery)
        // but handle it gracefully
        if (_isRecovering)
        {
            CompleteRecovery();
        }
    }

    // --------------------------------------------------------
    // Stuck Detection (passive monitoring)
    // --------------------------------------------------------

    private void MonitorForStuck()
    {
        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f)
            return;

        Vector3 currentPos = transform.position;

        // --- Position-based detection ---
        float movedDistance = Vector3.Distance(currentPos, _monitorStartPosition);

        if (movedDistance < stuckDistanceThreshold)
        {
            _monitorTimer += deltaTime;

            if (_monitorTimer >= stuckTimeThreshold)
            {
                if (debugLogs)
                {
                    Debug.Log($"[UnstuckBehavior] STUCK DETECTED (position-based): moved only {movedDistance:F3} in {_monitorTimer:F2}s", this);
                }
                _isStuckDetected = true;
            }
        }
        else
        {
            // Reset position monitoring
            _monitorStartPosition = currentPos;
            _monitorTimer = 0f;
        }

        // --- Oscillation-based detection ---
        if (detectOscillation)
        {
            DetectOscillation(currentPos, deltaTime);
        }
    }

    private void DetectOscillation(Vector3 currentPos, float deltaTime)
    {
        // Calculate current movement direction
        Vector3 currentDirection = (currentPos - _monitorStartPosition);
        currentDirection.y = 0f;

        if (currentDirection.sqrMagnitude < 0.0001f)
        {
            // Not moving enough to determine direction
            return;
        }

        currentDirection.Normalize();

        // Check for direction reversal
        float angle = Vector3.Angle(_lastDirection, currentDirection);

        if (angle >= oscillationAngleThreshold)
        {
            // Record this direction change
            float currentTime = Time.time;
            _directionChangeTimes[_directionChangeIndex] = currentTime;
            _directionChangeIndex = (_directionChangeIndex + 1) % _directionChangeTimes.Length;

            // Count recent direction changes
            int recentChanges = 0;
            float windowStart = currentTime - oscillationTimeWindow;

            for (int i = 0; i < _directionChangeTimes.Length; i++)
            {
                if (_directionChangeTimes[i] >= windowStart)
                {
                    recentChanges++;
                }
            }

            if (recentChanges >= oscillationCountThreshold)
            {
                if (debugLogs)
                {
                    Debug.Log($"[UnstuckBehavior] STUCK DETECTED (oscillation): {recentChanges} direction reversals in {oscillationTimeWindow}s", this);
                }
                _isStuckDetected = true;

                // Clear the times to prevent immediate re-trigger
                for (int i = 0; i < _directionChangeTimes.Length; i++)
                {
                    _directionChangeTimes[i] = 0f;
                }
            }

            _lastDirection = currentDirection;
        }
    }

    // --------------------------------------------------------
    // Recovery Logic
    // --------------------------------------------------------

    private Vector3 FindEscapeDirection()
    {
        Vector3 pos = transform.position;

        // Method 1: Move away from nearby obstacles
        Vector3 awayFromObstacles = ComputeAwayFromObstacles(pos);
        
        if (awayFromObstacles.sqrMagnitude > 0.001f)
        {
            // Verify this direction is actually clear
            if (IsDirectionClear(pos, awayFromObstacles))
            {
                return awayFromObstacles.normalized;
            }
        }

        // Method 2: Sample directions and find the clearest one
        Vector3 bestDir = Vector3.zero;
        float bestClearance = -1f;

        float angleStep = 360f / escapeDirectionSamples;

        for (int i = 0; i < escapeDirectionSamples; i++)
        {
            float angle = angleStep * i;
            Vector3 testDir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;

            float clearance = MeasureClearance(pos, testDir);

            if (clearance > bestClearance)
            {
                bestClearance = clearance;
                bestDir = testDir;
            }
        }

        if (bestDir.sqrMagnitude > 0.001f)
        {
            return bestDir.normalized;
        }

        // Method 3: Last resort - just go backwards from current facing
        Vector3 backward = -transform.forward;
        backward.y = 0f;
        
        if (backward.sqrMagnitude > 0.001f)
        {
            return backward.normalized;
        }

        // Truly last resort - random direction
        Vector2 rand = Random.insideUnitCircle.normalized;
        return new Vector3(rand.x, 0f, rand.y);
    }

    private Vector3 ComputeAwayFromObstacles(Vector3 position)
    {
        if (obstacleLayers.value == 0)
            return Vector3.zero;

        Collider[] hits = Physics.OverlapSphere(
            position,
            obstacleCheckRadius,
            obstacleLayers,
            QueryTriggerInteraction.Ignore
        );

        if (hits == null || hits.Length == 0)
            return Vector3.zero;

        Vector3 awaySum = Vector3.zero;
        int count = 0;

        foreach (Collider col in hits)
        {
            if (col == null)
                continue;

            // Skip self
            if (col.transform == transform)
                continue;

            Vector3 closest = col.ClosestPoint(position);
            Vector3 away = position - closest;
            away.y = 0f;

            float dist = away.magnitude;

            if (dist < 0.001f)
            {
                // Inside the collider, use direction from center
                away = position - col.bounds.center;
                away.y = 0f;
                dist = away.magnitude;

                if (dist < 0.001f)
                    continue;
            }

            // Weight by inverse distance (closer = stronger push)
            float weight = 1f - Mathf.Clamp01(dist / obstacleCheckRadius);
            awaySum += away.normalized * weight;
            count++;
        }

        if (count == 0)
            return Vector3.zero;

        return awaySum.normalized;
    }

    private bool IsDirectionClear(Vector3 position, Vector3 direction)
    {
        if (obstacleLayers.value == 0)
            return true;

        return !Physics.Raycast(
            position + Vector3.up * 0.1f,
            direction,
            obstacleCheckRadius,
            obstacleLayers,
            QueryTriggerInteraction.Ignore
        );
    }

    private float MeasureClearance(Vector3 position, Vector3 direction)
    {
        if (obstacleLayers.value == 0)
            return obstacleCheckRadius;

        if (Physics.Raycast(
            position + Vector3.up * 0.1f,
            direction,
            out RaycastHit hit,
            obstacleCheckRadius * 2f,
            obstacleLayers,
            QueryTriggerInteraction.Ignore))
        {
            return hit.distance;
        }

        return obstacleCheckRadius * 2f; // No obstacle found, maximum clearance
    }

    private void CompleteRecovery()
    {
        _isRecovering = false;
        _isStuckDetected = false;
        _monitorStartPosition = transform.position;
        _monitorTimer = 0f;
        _cooldownTimer = cooldownAfterRecovery;

        // Clear oscillation history
        for (int i = 0; i < _directionChangeTimes.Length; i++)
        {
            _directionChangeTimes[i] = 0f;
        }
        _directionChangeIndex = 0;
        _lastDirection = transform.forward;
    }

    // --------------------------------------------------------
    // Public Utilities
    // --------------------------------------------------------

    /// <summary>
    /// Manually reset the stuck detection state.
    /// Call this if you teleport the unit or otherwise change its state externally.
    /// </summary>
    public void ResetStuckDetection()
    {
        _monitorStartPosition = transform.position;
        _monitorTimer = 0f;
        _isStuckDetected = false;
        _cooldownTimer = 0f;

        for (int i = 0; i < _directionChangeTimes.Length; i++)
        {
            _directionChangeTimes[i] = 0f;
        }
        _directionChangeIndex = 0;
        _lastDirection = transform.forward;

        if (debugLogs)
        {
            Debug.Log("[UnstuckBehavior] Stuck detection manually reset.", this);
        }
    }

    /// <summary>
    /// Check if the unit is currently in recovery mode.
    /// </summary>
    public bool IsRecovering => _isRecovering;

    /// <summary>
    /// Check if stuck has been detected (but recovery may not have started yet).
    /// </summary>
    public bool IsStuckDetected => _isStuckDetected;

    // --------------------------------------------------------
    // Gizmos
    // --------------------------------------------------------

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!debugGizmos)
            return;

        Vector3 pos = transform.position;

        // Obstacle check radius
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.25f);
        Gizmos.DrawWireSphere(pos, obstacleCheckRadius);

        // Recovery distance
        Gizmos.color = new Color(0f, 1f, 0.5f, 0.2f);
        Gizmos.DrawWireSphere(pos, recoveryDistance);

        // Stuck threshold visualization
        Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
        Gizmos.DrawWireSphere(pos, stuckDistanceThreshold);

        // Current escape direction (if recovering)
        if (Application.isPlaying && _isRecovering)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(pos, pos + _escapeDirection * 2f);
            Gizmos.DrawWireSphere(pos + _escapeDirection * 2f, 0.15f);
        }

        // Monitor position
        if (Application.isPlaying)
        {
            Gizmos.color = _isStuckDetected ? Color.red : Color.yellow;
            Gizmos.DrawLine(pos, _monitorStartPosition);
            Gizmos.DrawWireSphere(_monitorStartPosition, 0.1f);
        }
    }
#endif
}