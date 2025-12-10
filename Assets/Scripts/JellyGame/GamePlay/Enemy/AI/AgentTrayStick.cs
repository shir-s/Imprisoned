using JellyGame.GamePlay.Map;
using UnityEngine;

namespace JellyGame.GamePlay.Enemy.AI
{
    /// <summary>
    /// IMPROVED VERSION: Keeps an enemy "stuck" to a tilted tray BUT respects movement
    /// from behaviors (like AttackBehavior).
    /// 
    /// Key improvements:
    /// - Better raycast origin calculation (always from current position + offset)
    /// - Validates that raycasts actually hit the tray
    /// - Smoother Y transitions to prevent sudden jumps
    /// - Better handling of edge cases (no tray found, out of bounds)
    /// - Optional debug visualization
    /// </summary>
    [DisallowMultipleComponent]
    public class AgentTrayStick : MonoBehaviour
    {
        [Header("Tray")]
        [Tooltip("Tray transform (TiltTray). If empty, will try to auto-find one in the scene.")]
        [SerializeField] private Transform tray;

        [Tooltip("Optional layer mask for the tray collider. If left at 0, raycast will hit everything.")]
        [SerializeField] private LayerMask trayMask;

        [Header("Surface Settings")]
        [Tooltip("How far above the tray surface (along tray.up) the enemy should hover.")]
        [SerializeField] private float surfaceOffset = 0.1f;

        [Tooltip("Max ray distance when searching for the tray below/above the enemy.")]
        [SerializeField] private float rayDistance = 5f;

        [Tooltip("Align enemy's up axis with tray.up each frame.")]
        [SerializeField] private bool alignRotationToTray = true;

        [Header("Smoothing")]
        [Tooltip("If > 0, Y position changes are smoothed over time. 0 = instant snapping.")]
        [SerializeField] private float ySmoothTime = 0.1f;

        [Tooltip("If true, validates that raycasts actually hit the tray object (recommended).")]
        [SerializeField] private bool validateTrayHit = true;

        [Header("Map Bounds (Tray Local XZ)")]
        [Tooltip("If true, the enemy will be clamped inside a rectangle in tray-local XZ.")]
        [SerializeField] private bool useLocalBounds = true;

        [Tooltip("Min/Max X in tray local space for the allowed region.")]
        [SerializeField] private float minLocalX = -5f;
        [SerializeField] private float maxLocalX =  5f;

        [Tooltip("Min/Max Z in tray local space for the allowed region.")]
        [SerializeField] private float minLocalZ = -5f;
        [SerializeField] private float maxLocalZ =  5f;

        [Header("Fallback Behavior")]
        [Tooltip("What to do when tray is not found below the enemy.")]
        [SerializeField] private FallbackMode fallbackMode = FallbackMode.MaintainCurrentY;

        [Tooltip("If using ProjectToTray fallback, how far to cast upward looking for tray.")]
        [SerializeField] private float upwardRayDistance = 10f;

        [Header("Debug")]
        [SerializeField] private bool debugRays = false;
        [SerializeField] private bool debugBoundsGizmos = false;
        [SerializeField] private bool debugLogs = false;

        public enum FallbackMode
        {
            MaintainCurrentY,      // Keep current Y position (enemy floats)
            ProjectToTray,         // Try casting upward to find tray above
            ClampToLastKnownY      // Stay at last valid tray Y position
        }

        // Internal state
        private float _currentYVelocity;
        private float _lastValidTrayY;
        private bool _hasValidTrayY;
        private int _consecutiveMisses;
        private const int MAX_CONSECUTIVE_MISSES = 5;

        private void Awake()
        {
            // Auto-find tray if not assigned
            if (!tray)
            {
                TiltTray tilt = FindObjectOfType<TiltTray>();
                if (tilt != null)
                {
                    tray = tilt.transform;
                    if (debugLogs)
                    {
                        Debug.Log($"[EnemyTrayStick] Auto-found tray: {tray.name}", this);
                    }
                }
                else if (debugLogs)
                {
                    Debug.LogWarning("[EnemyTrayStick] No TiltTray found in scene!", this);
                }
            }

            _lastValidTrayY = transform.position.y;
            _hasValidTrayY = false;
        }

        /// <summary>
        /// CRITICAL: We now respect the position that was set by behaviors in Update().
        /// We only adjust Y and clamp to bounds, we don't override the XZ movement.
        /// </summary>
        private void LateUpdate()
        {
            if (!tray)
            {
                if (debugLogs && Time.frameCount % 120 == 0)
                {
                    Debug.LogWarning("[EnemyTrayStick] No tray assigned!", this);
                }
                return;
            }

            Vector3 trayUp = tray.up;
        
            // IMPORTANT: Use CURRENT position (which may have just been updated by AttackBehavior)
            // as the starting point for our raycast
            Vector3 currentPos = transform.position;

            // Clamp XZ to bounds FIRST (before raycasting) so we don't raycast outside valid area
            Vector3 clampedPos = currentPos;
            if (useLocalBounds)
            {
                clampedPos = ClampToTrayLocalBounds(currentPos);
            }

            // Now find the tray surface at this XZ position
            float targetY = FindTrayYAtPosition(clampedPos, trayUp, out bool hitFound);

            if (!hitFound)
            {
                _consecutiveMisses++;

                if (debugLogs && _consecutiveMisses == 1)
                {
                    Debug.LogWarning($"[EnemyTrayStick] Lost tray surface! Using fallback: {fallbackMode}", this);
                }

                // Handle fallback modes
                switch (fallbackMode)
                {
                    case FallbackMode.MaintainCurrentY:
                        targetY = currentPos.y;
                        break;

                    case FallbackMode.ProjectToTray:
                        // Try casting upward
                        if (TryFindTrayAbove(clampedPos, trayUp, out float foundY))
                        {
                            targetY = foundY;
                            _consecutiveMisses = 0; // Reset on success
                        }
                        else
                        {
                            targetY = _hasValidTrayY ? _lastValidTrayY : currentPos.y;
                        }
                        break;

                    case FallbackMode.ClampToLastKnownY:
                        targetY = _hasValidTrayY ? _lastValidTrayY : currentPos.y;
                        break;
                }

                // If we've been missing for too long, something is wrong
                if (_consecutiveMisses >= MAX_CONSECUTIVE_MISSES && debugLogs)
                {
                    Debug.LogError($"[EnemyTrayStick] Failed to find tray for {MAX_CONSECUTIVE_MISSES} consecutive frames!", this);
                    _consecutiveMisses = 0; // Reset to avoid spam
                }
            }
            else
            {
                // Successfully found tray
                _consecutiveMisses = 0;
                _lastValidTrayY = targetY;
                _hasValidTrayY = true;
            }

            // Apply Y position (smoothed or instant)
            float newY;
            if (ySmoothTime > 0f)
            {
                newY = Mathf.SmoothDamp(currentPos.y, targetY, ref _currentYVelocity, ySmoothTime);
            }
            else
            {
                newY = targetY;
                _currentYVelocity = 0f;
            }

            // Final position respects XZ from behaviors, applies our calculated Y
            Vector3 finalPos = clampedPos;
            finalPos.y = newY;
            transform.position = finalPos;

            // Optionally align rotation to tray surface
            if (alignRotationToTray)
            {
                AlignRotation(trayUp);
            }

            // Debug visualization
            if (debugRays)
            {
                Vector3 surfacePoint = clampedPos;
                surfacePoint.y = targetY;
                Debug.DrawLine(transform.position, surfacePoint, hitFound ? Color.green : Color.red);
            }
        }

        /// <summary>
        /// Find the Y position of the tray surface at the given XZ position.
        /// Returns the Y value and whether a valid hit was found.
        /// 
        /// Strategy:
        /// 1. Cast downward from above the position
        /// 2. Optionally validate that we hit the tray object
        /// 3. Add surface offset
        /// </summary>
        private float FindTrayYAtPosition(Vector3 position, Vector3 trayUp, out bool hitFound)
        {
            hitFound = false;

            // Start the ray ABOVE the current position
            // Use a fixed offset based on rayDistance to ensure we're always above the tray
            Vector3 rayOrigin = position + trayUp * rayDistance;
            Vector3 rayDir = -trayUp;

            if (debugRays)
            {
                Debug.DrawRay(rayOrigin, rayDir * (rayDistance * 2f), Color.yellow);
            }

            RaycastHit hit;
            bool didHit;

            if (trayMask.value != 0)
            {
                didHit = Physics.Raycast(rayOrigin, rayDir, out hit, rayDistance * 2f, trayMask, QueryTriggerInteraction.Ignore);
            }
            else
            {
                didHit = Physics.Raycast(rayOrigin, rayDir, out hit, rayDistance * 2f, ~0, QueryTriggerInteraction.Ignore);
            }

            if (!didHit)
            {
                return position.y; // Fallback to current Y
            }

            // Optional: Validate that we actually hit the tray object
            if (validateTrayHit)
            {
                Transform hitTransform = hit.collider.transform;
                bool isPartOfTray = hitTransform == tray || 
                                    hitTransform.IsChildOf(tray) || 
                                    tray.IsChildOf(hitTransform);

                if (!isPartOfTray)
                {
                    if (debugLogs && Time.frameCount % 60 == 0)
                    {
                        Debug.LogWarning($"[EnemyTrayStick] Hit non-tray object: {hit.collider.name}", this);
                    }
                    return position.y; // Not the tray, maintain current Y
                }
            }

            hitFound = true;

            // Calculate final Y with offset
            float trayY = hit.point.y;
            float offsetDistance = surfaceOffset;
        
            // If tray is tilted, we need to offset along the tray's up direction, not world up
            Vector3 offsetVector = trayUp * offsetDistance;
            Vector3 finalPosition = hit.point + offsetVector;

            return finalPosition.y;
        }

        /// <summary>
        /// Attempt to find the tray by casting upward (for ProjectToTray fallback).
        /// </summary>
        private bool TryFindTrayAbove(Vector3 position, Vector3 trayUp, out float trayY)
        {
            trayY = position.y;

            Vector3 rayOrigin = position;
            Vector3 rayDir = trayUp; // Cast upward

            if (debugRays)
            {
                Debug.DrawRay(rayOrigin, rayDir * upwardRayDistance, Color.cyan);
            }

            RaycastHit hit;
            bool didHit;

            if (trayMask.value != 0)
            {
                didHit = Physics.Raycast(rayOrigin, rayDir, out hit, upwardRayDistance, trayMask, QueryTriggerInteraction.Ignore);
            }
            else
            {
                didHit = Physics.Raycast(rayOrigin, rayDir, out hit, upwardRayDistance, ~0, QueryTriggerInteraction.Ignore);
            }

            if (!didHit)
            {
                return false;
            }

            // Validate hit if enabled
            if (validateTrayHit)
            {
                Transform hitTransform = hit.collider.transform;
                bool isPartOfTray = hitTransform == tray || 
                                    hitTransform.IsChildOf(tray) || 
                                    tray.IsChildOf(hitTransform);

                if (!isPartOfTray)
                {
                    return false;
                }
            }

            // Found tray above - calculate Y with offset
            Vector3 offsetVector = trayUp * surfaceOffset;
            Vector3 finalPosition = hit.point + offsetVector;
            trayY = finalPosition.y;

            return true;
        }

        /// <summary>
        /// Clamp a world-space position into the configured tray-local XZ rectangle.
        /// </summary>
        private Vector3 ClampToTrayLocalBounds(Vector3 worldPos)
        {
            if (!tray)
                return worldPos;

            // Convert to tray local
            Vector3 local = tray.InverseTransformPoint(worldPos);

            // Clamp XZ
            local.x = Mathf.Clamp(local.x, minLocalX, maxLocalX);
            local.z = Mathf.Clamp(local.z, minLocalZ, maxLocalZ);

            // Back to world
            return tray.TransformPoint(local);
        }

        /// <summary>
        /// Align enemy's up axis to tray.up, keeping forward projected onto tray plane.
        /// </summary>
        private void AlignRotation(Vector3 trayUp)
        {
            // Project current forward onto tray plane so we don't flip randomly
            Vector3 fwd = transform.forward;
            Vector3 projectedFwd = Vector3.ProjectOnPlane(fwd, trayUp).normalized;

            if (projectedFwd.sqrMagnitude < 1e-4f)
            {
                // Fall back to tray forward if our own forward is too vertical
                projectedFwd = Vector3.ProjectOnPlane(tray.forward, trayUp).normalized;
            }

            if (projectedFwd.sqrMagnitude > 1e-4f)
            {
                transform.rotation = Quaternion.LookRotation(projectedFwd, trayUp);
            }
            else
            {
                // As a last resort, just set up = trayUp
                transform.up = trayUp;
            }
        }

        // --------------------------------------------------------
        // Public API
        // --------------------------------------------------------

        /// <summary>
        /// Get the current tray transform.
        /// </summary>
        public Transform Tray => tray;

        /// <summary>
        /// Check if the enemy is currently on the tray surface.
        /// </summary>
        public bool IsOnTray => _consecutiveMisses == 0;

        /// <summary>
        /// Manually set a new tray (useful for multi-tray levels).
        /// </summary>
        public void SetTray(Transform newTray)
        {
            tray = newTray;
            _consecutiveMisses = 0;
            _hasValidTrayY = false;
        
            if (debugLogs)
            {
                Debug.Log($"[EnemyTrayStick] Tray changed to: {(newTray ? newTray.name : "null")}", this);
            }
        }

        /// <summary>
        /// Force an immediate update of the Y position (useful after teleporting).
        /// </summary>
        public void ForceUpdate()
        {
            if (!tray)
                return;

            Vector3 currentPos = transform.position;
            Vector3 trayUp = tray.up;

            float targetY = FindTrayYAtPosition(currentPos, trayUp, out bool hitFound);

            if (hitFound)
            {
                Vector3 newPos = currentPos;
                newPos.y = targetY;
                transform.position = newPos;

                _lastValidTrayY = targetY;
                _hasValidTrayY = true;
                _consecutiveMisses = 0;
                _currentYVelocity = 0f;

                if (debugLogs)
                {
                    Debug.Log("[EnemyTrayStick] Force updated Y position", this);
                }
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!tray)
                return;

            // Draw bounds
            if (debugBoundsGizmos && useLocalBounds)
            {
                Vector3 a = tray.TransformPoint(new Vector3(minLocalX, 0f, minLocalZ));
                Vector3 b = tray.TransformPoint(new Vector3(maxLocalX, 0f, minLocalZ));
                Vector3 c = tray.TransformPoint(new Vector3(maxLocalX, 0f, maxLocalZ));
                Vector3 d = tray.TransformPoint(new Vector3(minLocalX, 0f, maxLocalZ));

                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(a, b);
                Gizmos.DrawLine(b, c);
                Gizmos.DrawLine(c, d);
                Gizmos.DrawLine(d, a);
            }

            // Draw status indicator
            if (Application.isPlaying)
            {
                Gizmos.color = _consecutiveMisses > 0 ? Color.red : Color.green;
                Gizmos.DrawWireSphere(transform.position, 0.2f);
            }
        }
#endif
    }
}