// FILEPATH: Assets/Scripts/Player/WearOnMovementDamage.cs
using JellyGame.GamePlay.Combat;
using UnityEngine;

namespace JellyGame.GamePlay.Player
{
    /// <summary>
    /// Converts movement distance (meters along the surface plane) into damage.
    /// Uses ticked application to avoid per-frame micro shrink + snap jitter.
    /// </summary>
    [DisallowMultipleComponent]
    public class WearOnMovementDamage : MonoBehaviour
    {
        [Header("Damage From Movement")]
        [SerializeField] private float damagePerMeter = 0.5f;

        [Tooltip("Accumulate movement and apply damage in discrete ticks (recommended).")]
        [SerializeField] private float tickInterval = 0.1f; // 10 times/sec

        [Tooltip("Ignore tiny planar jitter (meters, after projection).")]
        [SerializeField] private float minProjectedMoveDelta = 0.001f;

        [Header("Surface Detection")]
        [SerializeField] private LayerMask surfaceMask = ~0;
        [SerializeField] private float rayDistance = 3f;
        [SerializeField] private float rayStartOffset = 0.05f;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;
        [SerializeField] private bool debugRays = false;

        private IDamageable _damageable;

        private Vector3 _prevPos;
        private bool _hasPrev;

        private Vector3 _lastSurfaceNormal = Vector3.up;
        private bool _isOnSurface;

        private float _accumulatedMeters;
        private float _tickTimer;

        private void Awake()
        {
            _damageable = GetComponent<IDamageable>();
            if (_damageable == null)
            {
                Debug.LogError("[WearOnMovementDamage] Missing IDamageable on the same object (CubeScaler should implement it).", this);
                enabled = false;
                return;
            }
        }

        private void OnEnable()
        {
            _hasPrev = false;
            _accumulatedMeters = 0f;
            _tickTimer = 0f;
        }

        private void LateUpdate()
        {
            Vector3 pos = transform.position;

            if (!_hasPrev)
            {
                _prevPos = pos;
                _hasPrev = true;
                UpdateSurfaceInfo(pos);
                return;
            }

            Vector3 rawDelta = pos - _prevPos;
            _prevPos = pos;

            UpdateSurfaceInfo(pos);

            if (_isOnSurface)
            {
                Vector3 planarDelta = Vector3.ProjectOnPlane(rawDelta, _lastSurfaceNormal);
                float moved = planarDelta.magnitude;

                if (float.IsFinite(moved) && moved >= minProjectedMoveDelta)
                    _accumulatedMeters += moved;
            }

            // Apply damage in ticks (reduces jitter)
            _tickTimer += Time.deltaTime;
            if (_tickTimer < tickInterval)
                return;

            _tickTimer = 0f;

            if (_accumulatedMeters <= 0f || damagePerMeter <= 0f)
                return;

            float damage = _accumulatedMeters * damagePerMeter;
            _accumulatedMeters = 0f;

            if (debugLogs)
                Debug.Log($"[WearOnMovementDamage] tick damage={damage:F4} (normal={_lastSurfaceNormal})", this);

            _damageable.ApplyDamage(damage);
        }

        private void UpdateSurfaceInfo(Vector3 worldPos)
        {
            Vector3 gravityDir = (Physics.gravity.sqrMagnitude > 1e-6f) ? Physics.gravity.normalized : Vector3.down;

            if (TryRaycast(worldPos, gravityDir, out RaycastHit hit) ||
                TryRaycast(worldPos, -gravityDir, out hit))
            {
                _isOnSurface = true;
                _lastSurfaceNormal = hit.normal.normalized;
            }
            else
            {
                _isOnSurface = false;
            }
        }

        private bool TryRaycast(Vector3 worldPos, Vector3 dir, out RaycastHit hit)
        {
            Vector3 start = worldPos - dir * rayStartOffset;

            if (debugRays)
                Debug.DrawRay(start, dir * rayDistance, Color.yellow, 0.02f);

            return Physics.Raycast(start, dir, out hit, rayDistance, surfaceMask, QueryTriggerInteraction.Ignore);
        }

        private void OnValidate()
        {
            if (damagePerMeter < 0f) damagePerMeter = 0f;
            if (tickInterval < 0.01f) tickInterval = 0.01f;
            if (minProjectedMoveDelta < 0f) minProjectedMoveDelta = 0f;
            if (rayDistance < 0.1f) rayDistance = 0.1f;
            if (rayStartOffset < 0f) rayStartOffset = 0f;
        }
    }
}
