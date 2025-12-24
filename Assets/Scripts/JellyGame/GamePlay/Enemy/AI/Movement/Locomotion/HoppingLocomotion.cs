// FILEPATH: Assets/Scripts/AI/Movement/Locomotion/HoppingLocomotion.cs
using UnityEngine;

namespace JellyGame.GamePlay.Enemy.AI.Movement
{
    /// <summary>
    /// Discrete hopping movement locomotion.
    /// The agent moves in distinct hops with an arc trajectory.
    /// Suitable for slimes, frogs, or other bouncy creatures.
    /// </summary>
    public class HoppingLocomotion : LocomotionBase
    {
        private bool _isHopping;
        private float _nextHopTime;
        private float _hopProgress;

        private Vector3 _hopStartPos;
        private Vector3 _hopEndPos;
        private Vector3 _hopUp;

        public override bool IsInMotion => _isHopping;

        public HoppingLocomotion(Transform transform, LocomotionSettings settings, ISurfaceProvider surfaceProvider = null)
            : base(transform, settings, surfaceProvider)
        {
            _nextHopTime = 0f;
        }

        public override void Move(Vector3 direction, float deltaTime, float speedMultiplier = 1f)
        {
            if (direction.sqrMagnitude < 0.0001f)
                return;

            Rotate(direction, deltaTime);

            if (_isHopping)
            {
                ContinueHop(deltaTime);
                return;
            }

            if (Time.time < _nextHopTime)
                return;

            StartHop(direction, speedMultiplier);
        }

        public override void Stop()
        {
            _isHopping = false;
            _hopProgress = 0f;
        }

        public override void OnDisable()
        {
            Stop();
        }

        private void StartHop(Vector3 direction, float speedMultiplier)
        {
            _nextHopTime = Time.time + Mathf.Max(0.05f, settings.HopInterval);
            _isHopping = true;
            _hopProgress = 0f;

            _hopStartPos = transform.position;
            _hopUp = GetUp();

            float currentSpeed = settings.MoveSpeed * Mathf.Max(0f, speedMultiplier);
            float hopDistance = currentSpeed * Mathf.Max(0.05f, settings.HopDuration);

            Vector3 planarForward = ProjectOnMovementPlane(transform.forward);
            if (planarForward.sqrMagnitude < 0.0001f)
                planarForward = direction;

            Vector3 hopEndCandidate = _hopStartPos + planarForward * hopDistance;

            if (surfaceProvider != null)
            {
                _hopEndPos = surfaceProvider.GroundPosition(hopEndCandidate);
            }
            else
            {
                _hopEndPos = hopEndCandidate;
            }
        }

        private void ContinueHop(float deltaTime)
        {
            float hopDuration = Mathf.Max(0.001f, settings.HopDuration);
            _hopProgress += deltaTime / hopDuration;

            float t = Mathf.Clamp01(_hopProgress);

            Vector3 pos = Vector3.Lerp(_hopStartPos, _hopEndPos, t);

            float arc = Mathf.Sin(t * Mathf.PI) * settings.HopHeight;
            pos += _hopUp * arc;

            if (t >= 1f && surfaceProvider != null)
            {
                pos = surfaceProvider.GroundPosition(pos);
            }

            transform.position = pos;

            if (t >= 1f)
            {
                _isHopping = false;
            }
        }
    }
}