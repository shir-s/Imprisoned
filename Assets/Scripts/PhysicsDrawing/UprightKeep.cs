// UprightKeep.cs
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class UprightKeep : MonoBehaviour
{
    public Vector3 upLocal = Vector3.up;      // which local axis is "up" on your model
    public Vector3 targetUpWorld = Vector3.up;// world up (or set to paper normal when pressed)
    public float stiffness = 40f;             // more = stronger correction
    public float damping = 5f;                // counters overshoot

    Rigidbody rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.maxAngularVelocity = 50f;
    }

    void FixedUpdate()
    {
        Vector3 currentUp = transform.TransformDirection(upLocal);
        // smallest rotation from currentUp to targetUpWorld
        Vector3 axis = Vector3.Cross(currentUp, targetUpWorld);
        float angle = Mathf.Asin(Mathf.Clamp(axis.magnitude, -1f, 1f));
        if (angle < 1e-4f) return;
        axis = axis.normalized;

        // PD torque
        Vector3 torque = axis * (angle * stiffness) - rb.angularVelocity * damping;
        rb.AddTorque(torque, ForceMode.Acceleration);
    }
}