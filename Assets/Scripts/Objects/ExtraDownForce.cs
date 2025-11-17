// Simple "extra gravity" to keep butter pressed onto whatever is under it.
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class ExtraDownForce : MonoBehaviour
{
    [SerializeField] float extraGravity = 20f; // tweak

    Rigidbody _rb;

    void Awake() => _rb = GetComponent<Rigidbody>();

    void FixedUpdate()
    {
        // Extra gravity straight down
        _rb.AddForce(Physics.gravity.normalized * extraGravity, ForceMode.Acceleration);
    }
}