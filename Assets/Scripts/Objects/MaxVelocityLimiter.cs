// FILEPATH: Assets/Scripts/PhysicsDrawing/MaxVelocityLimiter.cs
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class MaxVelocityLimiter : MonoBehaviour
{
    [SerializeField] private float maxSpeed = 3f;        // tune this
    [SerializeField] private float maxAngularSpeed = 10f;

    private Rigidbody _rb;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    private void FixedUpdate()
    {
        if (_rb == null) return;

        Vector3 v = _rb.linearVelocity;
        float speed = v.magnitude;
        if (speed > maxSpeed && speed > 0.0001f)
        {
            _rb.linearVelocity = v * (maxSpeed / speed);
        }

        Vector3 av = _rb.angularVelocity;
        float w = av.magnitude;
        if (w > maxAngularSpeed && w > 0.0001f)
        {
            _rb.angularVelocity = av * (maxAngularSpeed / w);
        }
    }
}