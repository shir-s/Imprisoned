// FILEPATH: Assets/Scripts/Physics/UprightRigidbodyConstraints.cs

using UnityEngine;

namespace JellyGame.GamePlay.Player
{
    /// <summary>
    /// Keeps a Rigidbody upright and optionally clamps its linear and angular speed.
    /// Intended to be the single place that handles:
    /// - Rotation constraints (no rolling / tipping)
    /// - Zeroing or limiting angular velocity
    /// - Optional linear speed cap
    ///
    /// DOES NOT handle input or movement logic; that's for other scripts
    /// (e.g., KeyboardSelectableMover) if you decide to enable them later.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class UprightRigidbodyConstraints : MonoBehaviour
    {
        [Header("Orientation / Upright")]
        [Tooltip("Freeze all rotations on the Rigidbody to prevent any rolling or tipping.")]
        [SerializeField] private bool freezeAllRotation = true;

        [Tooltip("Forcefully zero angular velocity every physics step (strong anti-roll).")]
        [SerializeField] private bool zeroAngularVelocity = true;

        [Header("Speed Limits")]
        [Tooltip("If true, clamps linear velocity magnitude to maxLinearSpeed.")]
        [SerializeField] private bool limitLinearSpeed = true;

        [Tooltip("Max allowed linear speed (m/s). Set high if you only want a soft safety cap.")]
        [SerializeField] private float maxLinearSpeed = 3f;

        [Tooltip("If true AND zeroAngularVelocity is false, clamps angular velocity magnitude.")]
        [SerializeField] private bool limitAngularSpeed = false;

        [Tooltip("Max allowed angular speed (rad/s). Ignored if zeroAngularVelocity is true.")]
        [SerializeField] private float maxAngularSpeed = 10f;

        private Rigidbody _rb;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            ApplyRotationConstraints();
        }

        private void OnEnable()
        {
            if (_rb == null) _rb = GetComponent<Rigidbody>();
            ApplyRotationConstraints();
        }

        private void OnValidate()
        {
            if (_rb == null) _rb = GetComponent<Rigidbody>();
            if (maxLinearSpeed < 0f) maxLinearSpeed = 0f;
            if (maxAngularSpeed < 0f) maxAngularSpeed = 0f;
            ApplyRotationConstraints();
        }

        private void FixedUpdate()
        {
            if (_rb == null) return;

            // --- Linear speed clamp ---
            if (limitLinearSpeed && maxLinearSpeed > 0f)
            {
                Vector3 v = _rb.linearVelocity;
                float speed = v.magnitude;
                if (speed > maxLinearSpeed && speed > 0.0001f)
                {
                    _rb.linearVelocity = v * (maxLinearSpeed / speed);
                }
            }

            // --- Angular control ---
            if (zeroAngularVelocity)
            {
                _rb.angularVelocity = Vector3.zero;
            }
            else if (limitAngularSpeed && maxAngularSpeed > 0f)
            {
                Vector3 av = _rb.angularVelocity;
                float w = av.magnitude;
                if (w > maxAngularSpeed && w > 0.0001f)
                {
                    _rb.angularVelocity = av * (maxAngularSpeed / w);
                }
            }
        }

        private void ApplyRotationConstraints()
        {
            if (_rb == null) return;

            var c = _rb.constraints;

            // Clear any rotation freeze flags
            c &= ~(RigidbodyConstraints.FreezeRotationX |
                   RigidbodyConstraints.FreezeRotationY |
                   RigidbodyConstraints.FreezeRotationZ);

            if (freezeAllRotation)
            {
                c |= RigidbodyConstraints.FreezeRotationX |
                     RigidbodyConstraints.FreezeRotationY |
                     RigidbodyConstraints.FreezeRotationZ;
            }

            _rb.constraints = c;
        }
    }
}
