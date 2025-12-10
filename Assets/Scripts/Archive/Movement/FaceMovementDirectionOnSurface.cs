// FILEPATH: Assets/Scripts/Movement/FaceMovementDirectionOnSurface.cs

using JellyGame.GamePlay.Map;
using UnityEngine;

namespace JellyGame.GamePlay.Movement
{
    [DisallowMultipleComponent]
    public class FaceMovementDirectionOnSurface : MonoBehaviour
    {
        [Header("Sources")]
        [SerializeField] private Transform movementSource;
        [SerializeField] private Transform upSource;
        [SerializeField] private bool autoFindUpSource = true;

        [Header("Rotation Settings")]
        [SerializeField] private float minSpeedToRotate = 0.02f;
        [SerializeField] private float directionSmoothness = 8f;

        private Vector3 _lastPosWorld;
        private bool _hasLastPos;
        private Vector3 _smoothedDir;
        private bool _hasSmoothedDir;

        private void Start()
        {
            if (movementSource == null)
                movementSource = transform.parent != null ? transform.parent : transform;

            TryFindUpSource();

            if (movementSource != null)
            {
                _lastPosWorld = movementSource.position;
                _hasLastPos = true;
            }
        }

        private void TryFindUpSource()
        {
            if (upSource != null || !autoFindUpSource) return;
            TiltTray tray = GetComponentInParent<TiltTray>();
            if (tray == null) tray = FindObjectOfType<TiltTray>();
            if (tray != null) upSource = tray.transform;
        }

        private void LateUpdate()
        {
            if (movementSource == null) return;
            if (upSource == null && autoFindUpSource) TryFindUpSource();

            Vector3 surfaceUp = upSource ? upSource.up : Vector3.up;
            Vector3 currentPos = movementSource.position;

            if (!_hasLastPos)
            {
                _lastPosWorld = currentPos;
                _hasLastPos = true;
                return;
            }

            Vector3 delta = currentPos - _lastPosWorld;
            _lastPosWorld = currentPos;

            float dt = Mathf.Max(Time.deltaTime, 1e-6f);
            Vector3 planarDelta = Vector3.ProjectOnPlane(delta, surfaceUp);
            float distance = planarDelta.magnitude;

            if ((distance / dt) < minSpeedToRotate) return;

            Vector3 desiredDir = planarDelta.normalized;

            if (!_hasSmoothedDir)
            {
                _smoothedDir = desiredDir;
                _hasSmoothedDir = true;
            }
            else
            {
                float t = 1f - Mathf.Exp(-directionSmoothness * dt);
                _smoothedDir = Vector3.Slerp(_smoothedDir, desiredDir, t);
                _smoothedDir = Vector3.ProjectOnPlane(_smoothedDir, surfaceUp).normalized;
            }

            if (_smoothedDir.sqrMagnitude < 1e-6f) return;

            // Simply look in the smoothed direction (No locking!)
            Quaternion targetRot = Quaternion.LookRotation(_smoothedDir, surfaceUp);
            transform.rotation = targetRot;
        }
    }
}