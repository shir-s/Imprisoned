// FILEPATH: Assets/Scripts/Camera/FollowCubeCamera.cs

using JellyGame.GamePlay.Managers;
using UnityEngine;

namespace JellyGame.GamePlay.Camera
{
    [DisallowMultipleComponent]
    public class FollowCubeCamera : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Transform target;

        [Header("Dynamic Follow Behind")]
        [SerializeField] private float followDistance = 6f;
        [SerializeField] private float followHeight = 6f;

        [Header("Occlusion Handling")]
        [SerializeField] private LayerMask occlusionLayers = ~0;
        [SerializeField] private float maxExtraHeight = 4f;
        [SerializeField] private float overheadCheckDistance = 3f;

        [Header("Smart Rotation")]
        [Tooltip("How fast the camera orbits to get behind the player.")]
        [SerializeField] private float orbitSpeed = 90f;
        
        [Tooltip("If the player runs towards the camera, stop orbiting to prevent spinning.")]
        [SerializeField] private bool preventReverseSpin = true;

        [Header("Smoothing")]
        [SerializeField] private float positionSmoothTime = 0.1f;
        [SerializeField] private float lookSmoothTime = 0.1f; // For the camera's own rotation

        [Header("Events")]
        [SerializeField] private bool listenToActiveCubeEvent = true;
        [SerializeField] private bool snapOnTargetChange = true;

        private Vector3 _currentVelocity; // For SmoothDamp position
        private Vector3 _lastTargetPos;
        
        // We track a virtual "Forward" vector for the camera orbit, 
        // separate from the player's actual rotation.
        private Vector3 _currentOrbitForward; 

        private void Awake()
        {
            if (listenToActiveCubeEvent)
                EventManager.StartListening(EventManager.GameEvent.ActiveCubeChanged, OnActiveCubeChanged);
        }

        private void OnDestroy()
        {
            if (listenToActiveCubeEvent)
                EventManager.StopListening(EventManager.GameEvent.ActiveCubeChanged, OnActiveCubeChanged);
        }

        private void Start()
        {
            if (target != null)
            {
                _currentOrbitForward = target.forward;
                // Flatten orbit forward to prevent camera diving into ground
                _currentOrbitForward.y = 0; 
                _currentOrbitForward.Normalize();
                SnapToTargetImmediate();
            }
        }

        private void OnActiveCubeChanged(object data)
        {
            Transform newTarget = null;
            if (data is Transform tr) newTarget = tr;
            else if (data is GameObject go) newTarget = go.transform;

            if (newTarget != null) SetTarget(newTarget);
        }

        private void LateUpdate()
        {
            if (target == null) return;

            float dt = Time.deltaTime;

            // 1. Calculate Orbit Rotation
            // We want the camera to be behind the target, but intelligently.
            Vector3 targetForward = target.forward;
            targetForward.y = 0; // Work on XZ plane
            targetForward.Normalize();

            // Check alignment: 1.0 = Facing Away, -1.0 = Facing Camera
            float alignment = Vector3.Dot(targetForward, _currentOrbitForward);

            float effectiveOrbitSpeed = orbitSpeed;

            if (preventReverseSpin)
            {
                // If alignment is negative (facing camera), reduce orbit speed to zero.
                // Map [-1, 0] -> [0, 0] speed. Map [0, 1] -> [0, Max] speed.
                // We use a curve so sideways movement still rotates a bit.
                float rotationFactor = Mathf.Clamp01((alignment + 0.2f)); // +0.2 allows small rotation when sideways
                effectiveOrbitSpeed *= rotationFactor;
            }

            // Smoothly rotate our orbit vector towards the target's forward
            if (effectiveOrbitSpeed > 0.01f && targetForward.sqrMagnitude > 0.01f)
            {
                _currentOrbitForward = Vector3.RotateTowards(
                    _currentOrbitForward, 
                    targetForward, 
                    effectiveOrbitSpeed * Mathf.Deg2Rad * dt, 
                    0f
                );
                _currentOrbitForward.Normalize();
            }

            // 2. Calculate Base Position
            // Position is relative to target using our STABLE orbit vector, not the jittery target.forward
            Vector3 basePos = target.position 
                              - _currentOrbitForward * followDistance 
                              + Vector3.up * followHeight;

            // 3. Occlusion Logic (Same as before)
            Vector3 desiredPos = basePos;
            if (Physics.Linecast(basePos, target.position, out RaycastHit hitToTarget, occlusionLayers, QueryTriggerInteraction.Ignore))
            {
                bool overheadBlocked = false;
                if (Physics.Raycast(basePos, Vector3.up, out RaycastHit overheadHit, overheadCheckDistance, occlusionLayers, QueryTriggerInteraction.Ignore))
                {
                    if (overheadHit.collider != hitToTarget.collider) overheadBlocked = true;
                }

                if (!overheadBlocked) desiredPos = basePos + Vector3.up * maxExtraHeight;
                else desiredPos = basePos;
            }

            // 4. Apply Position with Smoothing
            transform.position = Vector3.SmoothDamp(transform.position, desiredPos, ref _currentVelocity, positionSmoothTime);

            // 5. Look At Target
            Vector3 lookDir = target.position - transform.position;
            if (lookDir.sqrMagnitude > 0.001f)
            {
                Quaternion desiredRot = Quaternion.LookRotation(lookDir, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, lookSmoothTime * 10f * dt);
            }
        }

        public void SetTarget(Transform newTarget, bool? snapImmediately = null)
        {
            target = newTarget;
            bool shouldSnap = snapImmediately ?? snapOnTargetChange;
            if (shouldSnap && target != null) SnapToTargetImmediate();
        }

        public void SnapToTargetImmediate()
        {
            if (target == null) return;

            // Reset orbit to match target instantly
            _currentOrbitForward = target.forward;
            _currentOrbitForward.y = 0;
            _currentOrbitForward.Normalize();

            Vector3 basePos = target.position - _currentOrbitForward * followDistance + Vector3.up * followHeight;
            transform.position = basePos;
            
            transform.LookAt(target);
            _currentVelocity = Vector3.zero;
        }
    }
}